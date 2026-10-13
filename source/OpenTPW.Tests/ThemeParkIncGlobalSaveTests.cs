using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ThemeParkIncGlobalSaveTests
{
	private sealed record Layout
	{
		public int World { get; init; } = 2;
		public int Version { get; init; } = ThemeParkIncGlobalSave.SupportedVersion;
		public int Mission { get; init; } = 5;
		public string[] Parks { get; init; } = { "arabian", "science" };
		public int MapFlags { get; init; } = 17;
		public int AmbientTags { get; init; } = 2;
		public string? BrokenMarker { get; init; }
		public string SignLine1 { get; init; } = "Belly";
		public string SignLine2 { get; init; } = "Bounce";
		public string CompanyName { get; init; } = "Theme Park AG";
	}

	/// <summary>A file in the order of the original loader (0x0074D840 and its sub-serializers).</summary>
	private static byte[] Create( Layout layout )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		void Marker( string tag ) => writer.Write( Encoding.ASCII.GetBytes( layout.BrokenMarker == tag ? "XXXX" : tag ) );
		writer.Write( layout.World );
		writer.Write( layout.Version );
		writer.Write( 7 );
		writer.Write( new byte[8] );
		for ( var card = 0; card < ThemeParkIncGlobalSave.CardSlots; card++ )
		{
			var used = card < 3;
			writer.Write( used ? 1190 + card : 0 );
			writer.Write( used ? card : 4 );
			writer.Write( used ? 1 : int.MaxValue );
			writer.Write( used ? 7500 : -1 );
		}
		writer.Write( new byte[4 + 4 + 1 + 1 + 10] );
		writer.Write( layout.Mission );
		writer.Write( new byte[4 + 4 + 15 + 1 + 1 + 5 * 0x30] );
		Marker( "TATS" );
		writer.Write( new byte[15 * 4] );
		Marker( "AMTA" );
		writer.Write( layout.Parks.Length );
		for ( var index = 0; index < layout.Parks.Length; index++ )
		{
			var name = Encoding.Latin1.GetBytes( layout.Parks[index] + "\0" );
			writer.Write( name.Length );
			writer.Write( name );
			writer.Write( new byte[6 + 4 * 5 + 8 * 5] );
			for ( var character = 0; character < 33; character++ )
			{
				writer.Write( (ushort)(index == 0 && character < layout.SignLine1.Length ? layout.SignLine1[character] : 0) );
				writer.Write( (ushort)(index == 0 && character < layout.SignLine2.Length ? layout.SignLine2[character] : 0) );
			}
			writer.Write( (byte)1 );
			writer.Write( (byte)0 );
			writer.Write( index + 1 );
			writer.Write( (byte)0 );
			writer.Write( (byte)(index == 1 ? 1 : 0) );
			writer.Write( index == 1 ? 5 : 0 );
			writer.Write( (byte)0 );
			writer.Write( index + 1 );
		}
		Marker( "MEHT" );
		foreach ( var (enabled, level) in new[] { (1, 75), (1, 0), (1, 70), (0, 100) } )
		{
			writer.Write( enabled );
			writer.Write( level );
		}
		writer.Write( new byte[] { 1, 1, 0, 1, 0, 1, 0, 1 } );
		writer.Write( (ushort)100 );
		writer.Write( (byte)0 );
		writer.Write( 70f );
		writer.Write( 200f );
		writer.Write( 10f );
		writer.Write( 1 );
		writer.Write( new byte[2] );
		writer.Write( 12057 );
		writer.Write( new byte[ThemeParkIncGlobalSave.HistoryEntries * ThemeParkIncGlobalSave.HistoryEntryBytes] );
		writer.Write( new byte[4 + 0x5C] );
		Marker( "DTOT" );
		for ( var slot = 0; slot < 3; slot++ )
		{
			writer.Write( slot == 1 ? 1 : 0 );
			writer.Write( new byte[4 * 4 + 8 + 8] );
		}
		writer.Write( 5500 );
		writer.Write( 18 );
		for ( var character = 0; character < 31; character++ )
			writer.Write( (ushort)(character < layout.CompanyName.Length ? layout.CompanyName[character] : 0) );
		writer.Write( 126 );
		Marker( "RAHS" );
		writer.Write( layout.MapFlags );
		// A negative count still gets its own bytes so the file stays aligned and only the count guard can reject it.
		writer.Write( new byte[Math.Max( 0, layout.MapFlags + 1 + 3 * 4 )] );
		Marker( "MAPS" );
		writer.Write( layout.AmbientTags );
		writer.Write( new byte[Math.Max( 0, layout.AmbientTags ) * 16] );
		writer.Flush();
		return stream.ToArray();
	}

	[TestMethod]
	public void ReadsEverySectionInTheOriginalOrder()
	{
		var save = ThemeParkIncGlobalSave.Read( Create( new Layout() ) );
		Assert.AreEqual( (2, 12, 5), (save.World, save.Version, save.Mission) );
		Assert.AreEqual( ThemeParkIncGlobalSave.CardSlots, save.Cards.Count );
		CollectionAssert.AreEqual( new[] { new ThemeParkIncCard( 1190, 0, 1, 7500 ), new ThemeParkIncCard( 1191, 1, 1, 7500 ), new ThemeParkIncCard( 1192, 2, 1, 7500 ) }, save.Cards.Where( card => !card.IsEmpty ).ToArray() );
		Assert.AreEqual( 2, save.Parks.Count );
		var arabian = save.Parks[0];
		Assert.AreEqual( ("arabian", "Belly", "Bounce", true, false, 1, 1), (arabian.Name, arabian.SignLine1, arabian.SignLine2, arabian.Available, arabian.Open, arabian.ParkNumber, arabian.CurrentTechLevel) );
		var science = save.Parks[1];
		Assert.AreEqual( (true, 5, 2), (science.AllResearchCompleted, science.ChallengesDone, science.CurrentTechLevel) );
		var options = save.Options;
		Assert.AreEqual( (new ThemeParkIncVolume( true, 75 ), new ThemeParkIncVolume( true, 70 ), new ThemeParkIncVolume( false, 100 )), (options.VolumeA, options.Speech, options.Movie) );
		Assert.AreEqual( (true, true, false, 100, 70f, 200f, 10f, 1), (options.AdvisorOn, options.TutorialOn, options.TooltipsOn, options.GameSpeed, options.ZoomMin, options.ZoomMax, options.FpsClip, options.MusicEnabled) );
		CollectionAssert.AreEqual( new[] { false, true, false }, save.Shares.SlotsPresent.ToArray() );
		Assert.AreEqual( (5500, 18, "Theme Park AG", 126), (save.Shares.NumberOwned, save.Shares.PercentCompanySharesOwned, save.Shares.CompanyName, save.Shares.CurrentValue) );
		Assert.AreEqual( (12057, 2), (save.FirstHourTime, save.AmbientTagCount) );
	}

	[TestMethod]
	public void RejectsFilesTheOriginalWouldNotLoad()
	{
		foreach ( var marker in new[] { "TATS", "AMTA", "MEHT", "DTOT", "RAHS", "MAPS" } )
			Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( Create( new Layout { BrokenMarker = marker } ) ), marker );
		Assert.ThrowsException<NotSupportedException>( () => ThemeParkIncGlobalSave.Read( Create( new Layout { Version = 11 } ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( Create( new Layout { World = 4 } ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( Create( new Layout { Parks = Enumerable.Range( 0, ThemeParkIncGlobalSave.MaximumParks + 1 ).Select( index => $"park{index}" ).ToArray() } ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( Create( new Layout { MapFlags = -1 } ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( Create( new Layout { AmbientTags = ThemeParkIncGlobalSave.MaximumAmbientTags + 1 } ) ) );
		var valid = Create( new Layout() );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( valid.AsSpan( 0, valid.Length - 1 ) ), "truncated" );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( valid.Concat( new byte[1] ).ToArray() ), "trailing byte" );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public void RejectsMalformedUtf16InEachDecodedTextField( int field )
	{
		foreach ( var text in new[] { "\uD800", "\uDC00", "\uDC00\uD800", "\uD800A", new string( 'A', 32 ) + "\uD800" } )
		{
			var layout = field switch
			{
				0 => new Layout { SignLine1 = text },
				1 => new Layout { SignLine2 = text },
				_ => new Layout { CompanyName = text.Length > 31 ? new string( 'A', 30 ) + "\uD800" : text }
			};
			Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( Create( layout ) ), $"UTF-16 field {field}" );
		}
	}

	[TestMethod]
	public void ValidUtf16PairsAndUnusedTextAfterTheTerminatorRemainReadable()
	{
		const string text = "A\uD83C\uDFA2";
		var save = ThemeParkIncGlobalSave.Read( Create( new Layout { SignLine1 = text, SignLine2 = text, CompanyName = text } ) );
		Assert.AreEqual( text, save.Parks[0].SignLine1 );
		Assert.AreEqual( text, save.Parks[0].SignLine2 );
		Assert.AreEqual( text, save.Shares.CompanyName );
		var fullSign = new string( 'A', 31 ) + "\uD83C\uDFA2";
		var fullCompany = new string( 'A', 29 ) + "\uD83C\uDFA2";
		var full = ThemeParkIncGlobalSave.Read( Create( new Layout { SignLine1 = fullSign, SignLine2 = fullSign, CompanyName = fullCompany } ) );
		Assert.AreEqual( fullSign, full.Parks[0].SignLine1 );
		Assert.AreEqual( fullSign, full.Parks[0].SignLine2 );
		Assert.AreEqual( fullCompany, full.Shares.CompanyName );
		var terminated = ThemeParkIncGlobalSave.Read( Create( new Layout { SignLine1 = "A\0\uD800", SignLine2 = "B\0\uDC00", CompanyName = "C\0\uD800" } ) );
		Assert.AreEqual( "A", terminated.Parks[0].SignLine1 );
		Assert.AreEqual( "B", terminated.Parks[0].SignLine2 );
		Assert.AreEqual( "C", terminated.Shares.CompanyName );
	}

	[TestMethod]
	public void EveryTruncatedPrefixIsRefusedAndMaximumSizedTablesStillRead()
	{
		var valid = Create( new Layout() );
		for ( var length = 0; length < valid.Length; length++ )
		{
			var prefixLength = length;
			Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( valid.AsSpan( 0, prefixLength ) ), $"prefix {length}" );
		}
		var maximum = ThemeParkIncGlobalSave.Read( Create( new Layout
		{
			Parks = Enumerable.Repeat( new string( 'A', ThemeParkIncGlobalSave.MaximumNameBytes - 1 ), ThemeParkIncGlobalSave.MaximumParks ).ToArray(),
			MapFlags = ThemeParkIncGlobalSave.MaximumMapFlags,
			AmbientTags = ThemeParkIncGlobalSave.MaximumAmbientTags
		} ) );
		Assert.AreEqual( ThemeParkIncGlobalSave.MaximumParks, maximum.Parks.Count );
		Assert.AreEqual( ThemeParkIncGlobalSave.MaximumNameBytes - 1, maximum.Parks[0].Name.Length );
		Assert.AreEqual( ThemeParkIncGlobalSave.MaximumAmbientTags, maximum.AmbientTagCount );
	}

	[TestMethod]
	public void ExtremeCountsAndNameLengthsAreRefusedBeforeSectionReads()
	{
		var valid = Create( new Layout() );
		var parkCount = valid.AsSpan().IndexOf( "AMTA"u8 ) + 4;
		var mapCount = valid.AsSpan().IndexOf( "RAHS"u8 ) + 4;
		var ambientCount = valid.AsSpan().IndexOf( "MAPS"u8 ) + 4;
		foreach ( var offset in new[] { parkCount, parkCount + 4, mapCount, ambientCount } )
			foreach ( var count in new[] { int.MinValue, -1, int.MaxValue } )
			{
				var malformed = (byte[])valid.Clone();
				BinaryPrimitives.WriteInt32LittleEndian( malformed.AsSpan( offset, 4 ), count );
				Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( malformed ), $"count at {offset}: {count}" );
			}
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( Create( new Layout { Parks = new[] { new string( 'A', ThemeParkIncGlobalSave.MaximumNameBytes ) } } ) ) );
	}

	/// <summary>
	/// <c>OPENTPW_TPI_SAVES</c>: a folder with original Theme Park Inc <c>.GMS</c> files and any <c>.GMI</c> files (kept private).
	/// Each must read to its last byte with every marker in place.
	/// </summary>
	[TestMethod]
	public void OriginalGlobalSavesReadToTheirLastByte()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPI_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_SAVES to a folder with Theme Park Inc .GMS or .GMI files." );
		var files = Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".GMS" or ".GMI" ).ToList();
		if ( files.Count == 0 )
			Assert.Inconclusive( "The OPENTPW_TPI_SAVES folder has no .GMS or .GMI files." );
		foreach ( var file in files )
		{
			var save = ThemeParkIncGlobalSave.Read( File.ReadAllBytes( file ) );
			Assert.AreEqual( ThemeParkIncGlobalSave.SupportedVersion, save.Version, file );
			Assert.IsTrue( save.Parks.Count > 0, file );
			Assert.IsTrue( save.Parks.All( park => park.Name.Length > 0 ), file );
			if ( Path.GetExtension( file ).Equals( ".GMS", StringComparison.OrdinalIgnoreCase ) )
				Assert.IsTrue( save.Cards.Any( card => !card.IsEmpty ), file );
		}
	}
}
