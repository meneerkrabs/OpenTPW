using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Moderation;
using OpenTPW.Online.Packages;

namespace OpenTPW.Tests;

[TestClass]
public class OnlineFormatTests
{
	internal static readonly Guid FixedId = new( "0123456789abcdef0123456789abcdef" );
	internal static readonly DateTimeOffset FixedTime = new( 2026, 10, 9, 12, 0, 0, TimeSpan.Zero );

	internal static byte[] Thumbnail( int width = 8, int height = 6 )
	{
		var pixels = new byte[width * height * 4];
		for ( var index = 0; index < pixels.Length; index++ )
			pixels[index] = (byte)(index * 7);
		return PngImage.EncodeRgba( width, height, pixels );
	}

	internal static MinimalParkPayload Payload() => new( "jungle", MinimalParkPayload.SourceOriginalLevel,
		new[] { new CellRef( 47, 17 ), new CellRef( 39, 21 ) },
		new[] { new ParkObjectRecord( 1100, 50, 22, 3, 3, 90, false ), new ParkObjectRecord( 1, 30, 5, 0, 0, 0, true ) },
		new PrototypeRideState( 12.5f, -40f, true ), null );

	internal static ParkPackage Package( string name = "My Jungle", byte[]? thumbnail = null ) => ParkPackage.Create(
		Payload().ToSnapshot( new[] { new RequiredContent( ContentKinds.Level, "jungle", new string( 'a', 64 ) ), new RequiredContent( ContentKinds.Object, "1100", null ) } ),
		new ParkInfo( name, "Line one\nline two", "Sander", "ignored" ), new GameInfo( "Theme Park World", "English" ), thumbnail ?? Thumbnail(), FixedId, FixedTime );

	[TestMethod]
	public void ParkPackageRoundTripsAndIsDeterministic()
	{
		var package = Package();
		var bytes = package.ToBytes();
		CollectionAssert.AreEqual( bytes, Package().ToBytes() );
		var read = ParkPackage.Read( new MemoryStream( bytes ) );
		Assert.AreEqual( "jungle", read.Manifest.Park.Level );
		Assert.AreEqual( "My Jungle", read.Manifest.Park.Name );
		Assert.AreEqual( FixedId, read.Manifest.PackageId );
		CollectionAssert.AreEqual( new[] { OpenTPW.Online.Packages.CompatibilityFlags.ReadOnlyVisit, OpenTPW.Online.Packages.CompatibilityFlags.NoEconomy }, read.Manifest.Flags.ToArray() );
		Assert.AreEqual( (8, 6), (read.Manifest.Thumbnail!.Width, read.Manifest.Thumbnail.Height) );
		var payload = MinimalParkPayload.FromSnapshot( read.ToSnapshot() );
		Assert.AreEqual( 2, payload.PathCells.Count );
		Assert.AreEqual( new ParkObjectRecord( 1100, 50, 22, 3, 3, 90, false ), payload.Objects[0] );
		Assert.AreEqual( new PrototypeRideState( 12.5f, -40f, true ), payload.PrototypeRide );
		Assert.IsNull( payload.Economy );
	}

	private static byte[] Zip( params (string Name, byte[] Data)[] entries )
	{
		using var output = new MemoryStream();
		using ( var archive = new ZipArchive( output, ZipArchiveMode.Create, true ) )
		{
			foreach ( var (name, data) in entries )
			{
				using var stream = archive.CreateEntry( name ).Open();
				stream.Write( data );
			}
		}
		return output.ToArray();
	}

	private static (string Name, byte[] Data)[] Entries( ParkPackage package ) =>
		new[] { (ParkPackage.ManifestEntry, StrictJson.Serialize( package.Manifest )), (ParkPackage.PayloadEntry, package.Payload), (ParkPackage.ThumbnailEntry, package.Thumbnail!) };

	private static void AssertRejected( byte[] bytes, string fragment )
	{
		var exception = Assert.ThrowsException<InvalidDataException>( () => ParkPackage.Read( bytes ) );
		StringAssert.Contains( exception.Message, fragment );
	}

	[TestMethod]
	public void RejectsHostileContainers()
	{
		var package = Package();
		var entries = Entries( package );
		AssertRejected( Encoding.ASCII.GetBytes( "not a zip at all" ), "not a valid OpenTPW container" );
		AssertRejected( Array.Empty<byte>(), "not a valid OpenTPW container" );
		AssertRejected( package.ToBytes()[..40], "not a valid OpenTPW container" );
		AssertRejected( Zip( entries.Append( ("../evil.txt", new byte[] { 1 }) ).ToArray() ), "entries" );
		AssertRejected( Zip( ("../manifest.json", entries[0].Data), entries[1] ), "path separator" );
		AssertRejected( Zip( ("sub/manifest.json", entries[0].Data), entries[1] ), "path separator" );
		AssertRejected( Zip( ("C:manifest.json", entries[0].Data), entries[1] ), "path separator" );
		AssertRejected( Zip( (".manifest", entries[0].Data), entries[1] ), "plain file name" );
		AssertRejected( Zip( entries[0], ("other.bin", entries[1].Data) ), "not allowed" );
		AssertRejected( Zip( entries[0], entries[0], entries[1] ), "more than once" );
		AssertRejected( Zip( entries[1] ), "manifest.json' is missing" );
		// A payload that inflates past the 16 MiB cap (zip bomb) is refused before it is decompressed.
		AssertRejected( Zip( entries[0], (ParkPackage.PayloadEntry, new byte[ParkPackage.MaximumPayloadBytes + 1]) ), "limit" );
		// Highly compressible entries under the size cap are refused by the ratio check.
		AssertRejected( Zip( entries[0], (ParkPackage.PayloadEntry, new byte[2 * 1024 * 1024]) ), "compression ratio" );
		var tooBig = new MemoryStream( new byte[ParkPackage.MaximumPackageBytes + 1] );
		Assert.ThrowsException<InvalidDataException>( () => ParkPackage.Read( tooBig ) );
	}

	[TestMethod]
	public void RejectsLyingZipHeaders()
	{
		var bytes = Zip( Entries( Package() ) );
		// Patch the central directory's uncompressed size of the first entry down to 10 bytes.
		var central = FindSignature( bytes, 0x02014b50 );
		BitConverter.GetBytes( 10 ).CopyTo( bytes, central + 24 );
		Assert.ThrowsException<InvalidDataException>( () => ParkPackage.Read( bytes ) );
	}

	private static int FindSignature( byte[] bytes, uint signature )
	{
		for ( var index = 0; index + 4 <= bytes.Length; index++ )
		{
			if ( BitConverter.ToUInt32( bytes, index ) == signature )
				return index;
		}
		throw new InvalidOperationException();
	}

	private static byte[] WithManifest( string json )
	{
		var entries = Entries( Package() );
		entries[0] = (ParkPackage.ManifestEntry, Encoding.UTF8.GetBytes( json ));
		return Zip( entries );
	}

	[TestMethod]
	public void RejectsHostileManifests()
	{
		var json = Encoding.UTF8.GetString( StrictJson.Serialize( Package().Manifest ) );
		AssertRejected( WithManifest( json.Replace( "\"formatVersion\":1", "\"formatVersion\":2" ) ), "version 2" );
		AssertRejected( WithManifest( json.Replace( "\"format\":\"opentpw.park\"", "\"format\":\"other\"" ) ), "not an OpenTPW park" );
		AssertRejected( WithManifest( json.Replace( "{\"format\"", "{\"extra\":1,\"format\"" ) ), "malformed" );
		AssertRejected( WithManifest( json.Replace( "{\"format\":\"opentpw.park\"", "{\"format\":\"opentpw.park\",\"format\":\"opentpw.park\"" ) ), "more than once" );
		AssertRejected( WithManifest( json.Replace( "\"requires\":[]", "\"requires\":[\"time-travel\"]" ) ), "time-travel" );
		AssertRejected( WithManifest( json.Replace( "\"level\":\"jungle\"", "\"level\":\"../jungle\"" ) ), "level" );
		AssertRejected( WithManifest( json.Replace( "\"id\":\"1100\"", "\"id\":\"../../etc\"" ) ), "content id" );
		AssertRejected( WithManifest( json.Replace( "\"kind\":\"object\"", "\"kind\":\"executable\"" ) ), "content kind" );
		AssertRejected( WithManifest( json.Replace( "My Jungle", "My\\u0007Jungle" ) ), "control character" );
		AssertRejected( WithManifest( json.Replace( "My Jungle", new string( 'x', 40 ) ) ), "longer than" );
		AssertRejected( WithManifest( json.Replace( "\"sha256\":\"", "\"sha256\":\"0" ).Replace( "\"sha256\":\"0aaaa", "\"sha256\":\"aaaa" ) ), "hash" );
		AssertRejected( WithManifest( json[..^5] ), "malformed" );
		AssertRejected( WithManifest( new string( '[', 100 ) ), "malformed" );
		AssertRejected( WithManifest( "null" ), "null" );
	}

	[TestMethod]
	public void RejectsTamperedPayloadAndThumbnail()
	{
		var entries = Entries( Package() );
		var payload = (byte[])entries[1].Data.Clone();
		payload[5] ^= 1;
		AssertRejected( Zip( entries[0], (ParkPackage.PayloadEntry, payload), entries[2] ), "payload does not match" );
		AssertRejected( Zip( entries[0], entries[1] ), "thumbnail and manifest disagree" );
		var badPng = (byte[])entries[2].Data.Clone();
		badPng[20] ^= 1;
		Assert.ThrowsException<InvalidDataException>( () => ParkPackage.Read( Zip( entries[0], entries[1], (ParkPackage.ThumbnailEntry, badPng) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Package( thumbnail: Thumbnail( 600, 10 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Package( thumbnail: Encoding.ASCII.GetBytes( "GIF89a not a png" ) ) );
	}

	[TestMethod]
	public void MinimalPayloadRejectsOutOfRangeValues()
	{
		var good = Payload();
		Assert.ThrowsException<InvalidDataException>( () => (good with { Level = "Jungle/../x" }).ToBytes() );
		Assert.ThrowsException<InvalidDataException>( () => (good with { Source = "network" }).ToBytes() );
		Assert.ThrowsException<InvalidDataException>( () => (good with { PathCells = new[] { new CellRef( 1, 1 ), new CellRef( 1, 1 ) } }).ToBytes() );
		Assert.ThrowsException<InvalidDataException>( () => (good with { PathCells = new[] { new CellRef( -1, 1 ) } }).ToBytes() );
		Assert.ThrowsException<InvalidDataException>( () => (good with { Objects = new[] { new ParkObjectRecord( 1, 1, 1, 17, 1, 0, false ) } }).ToBytes() );
		Assert.ThrowsException<InvalidDataException>( () => (good with { Objects = new[] { new ParkObjectRecord( 1, 1, 1, 1, 1, 45, false ) } }).ToBytes() );
		Assert.ThrowsException<InvalidDataException>( () => (good with { PrototypeRide = new PrototypeRideState( float.NaN, 0, false ) }).ToBytes() );
		var withEconomy = Encoding.UTF8.GetString( good.ToBytes() ).Replace( "\"economy\":null", "\"economy\":{\"cash\":1}" );
		Assert.ThrowsException<InvalidDataException>( () => MinimalParkPayload.FromBytes( Encoding.UTF8.GetBytes( withEconomy ) ) );
		var snapshot = good.ToSnapshot( Array.Empty<RequiredContent>() ) with { PayloadVersion = 2 };
		Assert.ThrowsException<InvalidDataException>( () => MinimalParkPayload.FromSnapshot( snapshot ) );
	}

	[TestMethod]
	public void PostcardRoundTripsAndRejectsHostileInput()
	{
		var card = Postcard.Create( "Sander", new[] { "Bob", "Ann Marie" }, "Greetings from Theme Park World!", "Come and see\nmy park", "English",
			new PostcardParkReference( FixedId, "My Jungle", "jungle", "abc123" ), Thumbnail(), FixedId, FixedTime );
		var read = Postcard.Read( card.ToBytes() );
		Assert.AreEqual( "Sander", read.Manifest.From );
		CollectionAssert.AreEqual( new[] { "Bob", "Ann Marie" }, read.Manifest.To.ToArray() );
		Assert.AreEqual( "Come and see\nmy park", read.Manifest.Text );
		Assert.AreEqual( "abc123", read.Manifest.Park!.ServerParkId );
		Assert.IsNotNull( read.Image );
		Assert.AreEqual( "a\nb", Postcard.Create( "Sander", new[] { "Bob" }, "t", "a\r\nb", "English" ).Manifest.Text );

		Assert.ThrowsException<InvalidDataException>( () => Postcard.Create( "Sander", Array.Empty<string>(), "t", "x", "English" ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Create( "Sander", new[] { "Bob", "BOB" }, "t", "x", "English" ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Create( "Sander", Enumerable.Range( 0, 11 ).Select( i => $"Player{i}" ), "t", "x", "English" ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Create( "../x", new[] { "Bob" }, "t", "x", "English" ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Create( "Sander", new[] { "Bob" }, "", "x", "English" ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Create( "Sander", new[] { "Bob" }, "t", new string( 'x', 1025 ), "English" ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Create( "Sander", new[] { "Bob" }, "t", "evil\u202Etext", "English" ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Create( "Sander", new[] { "Bob" }, "t", "x", "../English" ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Read( Zip( ("card.json", Encoding.UTF8.GetBytes( "{}" )) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Read( Zip( ("card.json", Encoding.UTF8.GetBytes( "{}" )), ("script.js", new byte[1]) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Read( Encoding.ASCII.GetBytes( "PK\u0003\u0004 garbage" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkPackage.Read( card.ToBytes() ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Read( Package().ToBytes() ) );
	}

	[TestMethod]
	public void PlayerNamesAreValidatedAndCaseInsensitive()
	{
		foreach ( var name in new[] { "Bob", "Ann Marie", "Søren_1", "Jürgen-K." } )
			Assert.IsTrue( OnlineText.IsValidPlayerName( name ), name );
		foreach ( var name in new[] { null, "", "ab", " Bob", "Bob ", "Bo  b", "a/b", "a\\b", "Bob\n", new string( 'x', 21 ), "Bo\u0000b" } )
			Assert.IsFalse( OnlineText.IsValidPlayerName( name ), name ?? "null" );
		Assert.AreEqual( OnlineText.NormalizeName( "søren" ), OnlineText.NormalizeName( "SØREN" ) );
	}

	[TestMethod]
	public void PngEncoderOutputValidates()
	{
		var png = Thumbnail( 3, 2 );
		Assert.AreEqual( (3, 2), PngImage.Validate( png, 16 ) );
		Assert.ThrowsException<InvalidDataException>( () => PngImage.Validate( png, 2 ) );
		Assert.ThrowsException<InvalidDataException>( () => PngImage.Validate( png.Concat( new byte[] { 0 } ).ToArray(), 16 ) );
		Assert.ThrowsException<InvalidDataException>( () => PngImage.Validate( png[..^12], 16 ) );
		Assert.AreEqual( 0xCBF43926u, PngImage.Crc32( Encoding.ASCII.GetBytes( "123456789" ) ) );
	}

	[TestMethod]
	public void WordListsUseTheOriginalInvertedUtf16Encoding()
	{
		var encoded = WordFilter.EncodeList( new[] { " badword", "darn" } );
		// "b" (0x0062) is stored as 0xFF9D; CR/LF stay plain.
		CollectionAssert.AreEqual( new byte[] { 0xDF, 0xFF, 0x9D, 0xFF }, encoded[..4] );
		CollectionAssert.AreEqual( new byte[] { 0x0D, 0x00, 0x0A, 0x00 }, encoded[^4..] );
		CollectionAssert.AreEqual( new[] { " badword", "darn" }, WordFilter.DecodeList( encoded ).ToArray() );
		Assert.ThrowsException<InvalidDataException>( () => WordFilter.DecodeList( new byte[] { 1, 2, 3 } ) );
	}

	[TestMethod]
	public void WordFilterMasksHitsButKeepsAllowedWords()
	{
		var filter = new WordFilter( new[] { " darn", "heck" }, new[] { "checked" } );
		Assert.AreEqual( "oh **** it", filter.Apply( "oh darn it" ) );
		Assert.AreEqual( "**** it", filter.Apply( "Darn it" ) );
		Assert.AreEqual( "undarned", filter.Apply( "undarned" ) );
		Assert.AreEqual( "I checked", filter.Apply( "I checked" ) );
		Assert.AreEqual( "what the ****", filter.Apply( "what the heck" ) );
		Assert.AreEqual( "plain", WordFilter.Empty.Apply( "plain" ) );
	}

	[TestMethod]
	public void OriginalWordListsDecode()
	{
		var directory = Path.Combine( OriginalParkImportTests.OriginalDataPath(), "Language", "English" );
		if ( !File.Exists( Path.Combine( directory, WordFilter.SwearsFileName ) ) )
			Assert.Inconclusive( "Original English word lists are missing." );
		var filter = WordFilter.LoadDirectory( directory );
		Assert.AreEqual( 156, filter.SwearCount );
		Assert.AreEqual( 59, filter.AllowedCount );
	}

	[TestMethod]
	public void ChatCommandTableParsesCanonicalLocalizedAndMultiWordCommands()
	{
		var strings = new List<string>( Enumerable.Repeat( "", 176 ) );
		for ( var index = 0; index < ChatCommands.CommandCount; index++ )
		{
			strings[index] = ChatCommands.CanonicalNames[index] + "x";
			strings[ChatCommands.CanonicalNamesOffset + index] = ChatCommands.CanonicalNames[index];
		}
		strings[0] = "sage";
		var table = new ChatCommandTable( strings );
		Assert.IsTrue( table.TryParse( "hello there", out var command, out var argument ) );
		Assert.AreEqual( (ChatCommand.Say, "hello there"), (command, argument) );
		Assert.IsTrue( table.TryParse( "/sage hallo", out command, out argument ) );
		Assert.AreEqual( (ChatCommand.Say, "hallo"), (command, argument) );
		Assert.IsTrue( table.TryParse( "/TELL Bob hi", out command, out argument ) );
		Assert.AreEqual( (ChatCommand.Tell, "Bob hi"), (command, argument) );
		Assert.IsTrue( table.TryParse( "/hungry thirsty", out command, out _ ) );
		Assert.AreEqual( ChatCommand.HungryThirsty, command );
		Assert.IsTrue( table.TryParse( "/hungry", out command, out _ ) );
		Assert.AreEqual( ChatCommand.Hungry, command );
		Assert.IsTrue( table.TryParse( "/queue too long", out command, out _ ) );
		Assert.AreEqual( ChatCommand.QueueTooLong, command );
		Assert.IsFalse( table.TryParse( "/sayy x", out _, out _ ) );
		Assert.AreEqual( ChatNotice.YouCheer, ChatCommands.MoodNotice( ChatCommand.Cheer ) );
		Assert.IsNull( ChatCommands.MoodNotice( ChatCommand.Think ) );
		Assert.AreEqual( (ChatNotice)151, ChatCommands.MoodNotice( ChatCommand.Confused ) );
		Assert.AreEqual( ("Big Bob", "hi there"), ChatCommands.SplitName( "\"Big Bob\" hi there" ) );
		Assert.AreEqual( ("Bob", ""), ChatCommands.SplitName( "Bob" ) );
	}

	[TestMethod]
	public void ChatRequestsAreStrict()
	{
		var request = ChatRequest.Parse( StrictJson.Serialize( ChatRequest.ForCommand( ChatCommand.Tell, "Bob hi" ) ) );
		Assert.AreEqual( "tell", request.Command );
		Assert.ThrowsException<InvalidDataException>( () => ChatRequest.Parse( Encoding.UTF8.GetBytes( "{\"type\":\"command\",\"command\":\"format c:\",\"argument\":\"\",\"room\":null}" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ChatRequest.Parse( Encoding.UTF8.GetBytes( "{\"type\":\"join\",\"command\":null,\"argument\":null,\"room\":\"park:../x\"}" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ChatRequest.Parse( Encoding.UTF8.GetBytes( "{\"type\":\"command\",\"command\":\"say\",\"argument\":\"" + new string( 'x', 300 ) + "\",\"room\":null}" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ChatRequest.Parse( Encoding.UTF8.GetBytes( "{\"type\":\"exec\"}" ) ) );
	}

	[TestMethod]
	public void OriginalChatCommandStringsMatchTheProtocolNames()
	{
		var path = Path.Combine( OriginalParkImportTests.OriginalDataPath(), "Language", "English" );
		if ( !File.Exists( Path.Combine( path, "CHAT_COMMANDS.str" ) ) )
			Assert.Inconclusive( "Original English string tables are missing." );
		using var tableStream = File.OpenRead( Path.Combine( path, "MBToUni.dat" ) );
		var characterTable = new BFMUReader( tableStream );
		using var stream = File.OpenRead( Path.Combine( path, "CHAT_COMMANDS.str" ) );
		var strings = new StringFile( stream, characterTable ).Entries;
		CollectionAssert.AreEqual( ChatCommands.CanonicalNames.ToArray(), strings.Skip( ChatCommands.CanonicalNamesOffset ).Take( ChatCommands.CommandCount ).ToArray() );
		Assert.AreEqual( "You told ", strings[(int)ChatNotice.YouTold] );
		Assert.AreEqual( "You cheer", strings[(int)ChatNotice.YouCheer] );
		Assert.AreEqual( "You appear confused", strings[(int)ChatCommands.MoodNotice( ChatCommand.Confused )!.Value] );
		Assert.AreEqual( "Welcome to Theme Park World online!", strings[(int)ChatNotice.WelcomeThemeParkWorld] );
		Assert.AreEqual( "There is already a player of that name online", strings[(int)ChatNotice.NameAlreadyOnline] );
		Assert.AreEqual( "You have exceeded your maximum number of votes for today. Try again tomorrow", strings[(int)ChatNotice.VoteLimitReached] );
	}
}
