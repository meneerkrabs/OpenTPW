using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SettingsFileTests
{
	private static SettingsFile Read( string text ) => new( new MemoryStream( Encoding.ASCII.GetBytes( text ) ) );

	private static string[] Pairs( SettingsFile file ) => file.Entries.Select( entry => $"{entry.Key}={entry.Value}" ).ToArray();

	[TestMethod]
	public void AKeyWithoutAValueDoesNotTakeTheNextLine()
	{
		// The old reader skipped the newline after a bare key and paired it with the next key.
		var file = Read( "Info.Shape\nInfo.Id 1110\nUsageInfo.MaxCapacity\t\t# no value yet\nUsageInfo.ExcitementLevel 30\n" );
		CollectionAssert.AreEqual( new[] { "Info.Id=1110", "UsageInfo.ExcitementLevel=30" }, Pairs( file ) );
		Assert.IsNull( file["Info.Shape"] );
	}

	[TestMethod]
	public void ValuesEndAtWhitespaceOrHashAndTheRestIsAComment()
	{
		var file = Read( "#\n#\tRide\n#\nInfo.HasQueue\t1\t\tBoolean comment\r\nUpgrades[0].CostOfUpgrade\t1000 cash cost\r\nUsageInfo.ExcitementLevel 30 // Ben's fault\r\nInfo.Speed 4#fast\n// a whole-line comment\n" );
		CollectionAssert.AreEqual( new[] { "Info.HasQueue=1", "Upgrades[0].CostOfUpgrade=1000", "UsageInfo.ExcitementLevel=30", "Info.Speed=4" }, Pairs( file ) );
	}

	[TestMethod]
	public void ObjectReaderEndsUnquotedValuesAtHashAndKeepsQuotedValuesRaw()
	{
		var text = "Info.Int 4#positive\nInfo.Negative -7#negative\nInfo.Float 2.5#fraction\nInfo.NegativeFloat -1.25#fraction\n"
			+ "Info.Name \"A # B\"#comment\nInfo.Url \"a // b\"#comment\nInfo.Empty \"\"#empty\n"
			+ "Info.Comment # only a comment\nInfo.Bare\nInfo.Next 1\n";
		var objects = new ObjectSettingsFile( text, "synthetic.sam" );
		var settings = Read( text );

		// The attached comment ends the unquoted value for both readers.
		CollectionAssert.AreEqual( new[] { "Info.Int=4", "Info.Negative=-7", "Info.Float=2.5", "Info.NegativeFloat=-1.25",
			"Info.Name=A # B", "Info.Url=a // b", "Info.Empty=", "Info.Next=1" }, Pairs( settings ) );

		// Quoted hashes and slashes stay in the value; an empty quoted string is kept.
		Assert.AreEqual( "4", objects.Values["Info.Int"] );
		Assert.AreEqual( "-7", objects.Values["Info.Negative"] );
		Assert.AreEqual( "2.5", objects.Values["Info.Float"] );
		Assert.AreEqual( "-1.25", objects.Values["Info.NegativeFloat"] );
		Assert.AreEqual( "A # B", objects.Values["Info.Name"] );
		Assert.AreEqual( "a // b", objects.Values["Info.Url"] );
		Assert.AreEqual( "", objects.Values["Info.Empty"] );

		// A comment with no value and a bare key produce no pair and do not consume the next line.
		Assert.IsFalse( objects.Values.ContainsKey( "Info.Comment" ) );
		Assert.IsFalse( objects.Values.ContainsKey( "Info.Bare" ) );
		Assert.AreEqual( "1", objects.Values["Info.Next"] );

		// The conversions see the stripped numbers.
		var layered = new ObjectSettings( new[] { objects } );
		Assert.AreEqual( 4, layered.GetInt( "Info.Int" ) );
		Assert.AreEqual( -7, layered.GetInt( "Info.Negative" ) );
		Assert.AreEqual( 2.5f, layered.GetFloat( "Info.Float" ) );
		Assert.AreEqual( -1.25f, layered.GetFloat( "Info.NegativeFloat" ) );
	}

	[TestMethod]
	public void ObjectReaderKeepsHashesInKeyedBlockRowsAndLayerSemantics()
	{
		var first = "Info.Id 1#one\nInfo.Name \"First\"\nInfo.Shape\n---\n*S*\n*2* # row comment\n---\n";
		var objects = new ObjectSettingsFile( first, "first.sam" );
		CollectionAssert.AreEqual( new[] { "*S*", "*2* # row comment" }, objects.Blocks["Info.Shape"].ToArray() );
		Assert.AreEqual( "1", objects.Values["Info.Id"], "the attached hash ends the value, the block rows keep theirs" );

		// Later layers override earlier ones; blocks are carried with the layer that supplies them.
		var layered = new ObjectSettings( new[] { objects, new ObjectSettingsFile( "Info.Id 2#two\nInfo.Name \"Second\"\n", "second.sam" ) } );
		Assert.AreEqual( "2", layered["Info.Id"], "the later layer wins" );
		Assert.AreEqual( 2, layered.GetInt( "Info.Id" ) );
		Assert.AreEqual( "Second", layered["Info.Name"] );
		CollectionAssert.AreEqual( new[] { "*S*", "*2* # row comment" }, layered.GetBlock( "Info.Shape" )!.ToArray() );

		// Duplicates inside one reader: SettingsFile keeps both and indexes the first, ObjectSettingsFile keeps the last.
		const string duplicates = "A.One 1#first\nA.Two 2\nA.One 3#last\n";
		CollectionAssert.AreEqual( new[] { "A.One=1", "A.Two=2", "A.One=3" }, Pairs( Read( duplicates ) ) );
		Assert.AreEqual( "1", Read( duplicates )["A.One"] );
		Assert.AreEqual( "3", new ObjectSettingsFile( duplicates, "duplicates.sam" ).Values["A.One"] );
	}

	[TestMethod]
	public void QuotedValuesKeepTheirSpacesAndMayBeEmpty()
	{
		var file = Read( "Info.Name\t\t\"Inca Totem\"\nSkyObjects[0].Filename\t\"\"\t\t# none\nInfo.PreviewAnimType\t\"m\"\t\tm = main loop\nInfo.Broken \"no closing quote\n" );
		CollectionAssert.AreEqual( new[] { "Info.Name=Inca Totem", "SkyObjects[0].Filename=", "Info.PreviewAnimType=m", "Info.Broken=no closing quote" }, Pairs( file ) );
		Assert.AreEqual( "", file["SkyObjects[0].Filename"] );
	}

	[TestMethod]
	public void BlockRowsAreNotSettings()
	{
		var file = Read( "Info.Id 1110\nInfo.Shape\n---\n*S*\n***\n*2*\n---\n\n\nInfo.Hoarding\n---\nF.7\nL.J\n---\nUpgrades[1].InitCapacity 8" );
		CollectionAssert.AreEqual( new[] { "Info.Id=1110", "Upgrades[1].InitCapacity=8" }, Pairs( file ) );
		// Only a line of exactly "---" delimits a block, and a block left open is rejected (as ObjectSettingsFile does).
		CollectionAssert.AreEqual( new[] { "A.Value=1" }, Pairs( Read( "-----\nA.Value 1\n" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( "Info.Shape\n---\n*S*\nInfo.Id 1110\n" ) );
	}

	[TestMethod]
	public void LineEndingsAndDuplicatesKeepTheirOrderAndTheFirstValueWins()
	{
		var file = Read( "A.One 1\rA.Two 2\r\nA.One 3\nA.Last 4" );
		CollectionAssert.AreEqual( new[] { "A.One=1", "A.Two=2", "A.One=3", "A.Last=4" }, Pairs( file ) );
		Assert.AreEqual( "1", file["a.one"] );
		Assert.IsNull( file["A.Missing"] );
	}

	[TestMethod]
	public void ReadsTheRestOfAnyStreamAndClosesIt()
	{
		var bytes = Encoding.ASCII.GetBytes( "skipped line\nA.Value 7\nB.Value 8\n" );
		var seekable = new MemoryStream( bytes );
		seekable.Position = "skipped line\n".Length;
		CollectionAssert.AreEqual( new[] { "A.Value=7", "B.Value=8" }, Pairs( new SettingsFile( seekable ) ) );
		Assert.IsFalse( seekable.CanRead, "the reader closes the stream it was given" );

		var trickle = new TrickleStream( bytes );
		CollectionAssert.AreEqual( new[] { "A.Value=7", "B.Value=8" }, Pairs( new SettingsFile( trickle ) ).Skip( 1 ).ToArray() );
		Assert.IsTrue( trickle.Disposed );
	}

	[TestMethod]
	public void OriginalSettingsFilesAgreeWithTheObjectReader()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original .sam files." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;

		var files = new List<(string Name, byte[] Data)>();
		foreach ( var path in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ) )
		{
			if ( path.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) )
				files.Add( (Path.GetRelativePath( dataPath, path ), File.ReadAllBytes( path )) );
			else if ( path.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) )
			{
				using var archive = new WadArchive( path );
				foreach ( var (name, member) in FshFileTests.WadMembers( archive.Root, Path.GetRelativePath( dataPath, path ) + "/" ) )
				{
					if ( name.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) )
						files.Add( (name, member.GetData()) );
				}
			}
		}

		// ObjectSettingsFile is the independent line reader the object catalogue uses. It keeps the last value of a
		// repeated key, so the comparison does too; SettingsFile's indexer returns the first (checked synthetically above).
		var mismatches = new List<string>();
		var pairs = 0;
		var archived = 0;
		foreach ( var (name, data) in files )
		{
			var text = Encoding.ASCII.GetString( data );
			var settings = new SettingsFile( new MemoryStream( data ) );
			var lastValues = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var entry in settings.Entries )
				lastValues[entry.Key] = entry.Value;
			var reference = new ObjectSettingsFile( text, name ).Values;
			pairs += settings.Entries.Count;
			archived += name.Contains( ".wad/", StringComparison.OrdinalIgnoreCase ) ? 1 : 0;
			foreach ( var key in lastValues.Keys.Union( reference.Keys, StringComparer.OrdinalIgnoreCase ) )
			{
				lastValues.TryGetValue( key, out var actual );
				reference.TryGetValue( key, out var expected );
				if ( actual != expected )
					mismatches.Add( $"{name}: {key} = '{actual}' (ObjectSettingsFile '{expected}')" );
			}
		}

		Assert.AreEqual( 0, mismatches.Count, string.Join( "\n", mismatches.Take( 20 ) ) );
		Assert.IsTrue( files.Count > 55 && archived > 0, $"{files.Count} .sam files, {archived} in archives" );
		Assert.IsTrue( pairs > 1000, $"{pairs} settings" );
	}

	/// <summary>A non-seekable stream that returns at most three bytes per read.</summary>
	private sealed class TrickleStream : Stream
	{
		private readonly byte[] data;
		private int position;

		public TrickleStream( byte[] data ) => this.data = data;

		public bool Disposed { get; private set; }

		public override bool CanRead => !Disposed;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override int Read( byte[] buffer, int offset, int count )
		{
			var read = Math.Min( Math.Min( count, 3 ), data.Length - position );
			Array.Copy( data, position, buffer, offset, read );
			position += read;
			return read;
		}

		public override void Flush() { }
		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		protected override void Dispose( bool disposing )
		{
			Disposed = true;
			base.Dispose( disposing );
		}
	}
}
