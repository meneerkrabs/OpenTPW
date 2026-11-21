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
	internal sealed record Layout
	{
		public int Theme { get; init; } = 2;
		public int Version { get; init; } = ThemeParkIncGlobalSave.SupportedVersion;
		public int Mission { get; init; } = 5;
		public string[] Zones { get; init; } = { "arabian", "science" };
		public int MapFlags { get; init; } = 17;
		public int AmbientTags { get; init; } = 2;
		public string? BrokenMarker { get; init; }
		public string CompanyName { get; init; } = "Theme Park AG";
	}

	/// <summary>A file in the order of the original's body (0x0074D840 and its sub-serializers), with distinct values so a misplaced field shows.</summary>
	internal static byte[] Create( Layout layout )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		void Marker( string tag ) => writer.Write( Encoding.ASCII.GetBytes( layout.BrokenMarker == tag ? "XXXX" : tag ) );
		writer.Write( layout.Theme );
		writer.Write( layout.Version );
		writer.Write( 7 );
		writer.Write( new byte[] { 0, 0, 0, 0, 0, 0, 1, 0 } );
		for ( var card = 0; card < ThemeParkIncGlobalSave.CardSlots; card++ )
		{
			var used = card < 3;
			writer.Write( used ? 1190 + card : 0 );
			writer.Write( used ? card : 4 );
			writer.Write( used ? 1 : int.MaxValue );
			writer.Write( used ? 7500 : -1 );
		}
		writer.Write( 3 );
		writer.Write( 0 );
		writer.Write( (byte)1 );
		writer.Write( (byte)0 );
		writer.Write( new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 } );
		writer.Write( layout.Mission );
		writer.Write( 3 );
		writer.Write( 4 );
		writer.Write( Enumerable.Range( 0, 15 ).Select( index => (byte)(index % 2) ).ToArray() );
		writer.Write( (byte)1 );
		writer.Write( (byte)0 );
		for ( var word = 0; word < 60; word++ )
			writer.Write( word * 3 );
		Marker( "TATS" );
		for ( var region = 0; region < 15; region++ )
			writer.Write( region == 14 || region == 13 ? 1 : 0 );
		Marker( "AMTA" );
		writer.Write( layout.Zones.Length );
		for ( var index = 0; index < layout.Zones.Length; index++ )
		{
			var name = Encoding.Latin1.GetBytes( layout.Zones[index] );
			writer.Write( name.Length );
			writer.Write( name );
			writer.Write( new byte[] { 0, 0, 0, 0, 0, 1 } );
			for ( var pair = 0; pair < 12; pair++ )
			{
				writer.Write( (byte)(pair == 10 ? 1 : 0) );
				writer.Write( pair == 10 ? 11 : unchecked( (int)0xCDCDCDCD ) );
			}
			for ( var character = 0; character < 33; character++ )
			{
				writer.Write( (ushort)(character < 5 ? 'A' + character : 0) );
				writer.Write( (ushort)(character < 3 ? 'a' + character : 0) );
			}
			writer.Write( (byte)1 );
			writer.Write( (byte)0 );
			writer.Write( index );
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
		writer.Write( (byte)0 );
		writer.Write( (byte)1 );
		writer.Write( 12057 );
		writer.Write( Enumerable.Range( 0, ThemeParkIncGlobalSave.HistoryEntries * ThemeParkIncGlobalSave.HistoryEntryBytes ).Select( index => (byte)index ).ToArray() );
		writer.Write( 9 );
		writer.Write( new byte[0x5C] );
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
		writer.Write( new byte[Math.Max( 0, layout.MapFlags ) + 1 + 3 * 4] );
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
		Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Complete, save.Outcome );
		Assert.AreEqual( (2, 12, 5, 7, 3, 4), (save.Theme, save.Version, save.Mission, save.GoldenTickets, save.Rank, save.FiringLine) );
		CollectionAssert.AreEqual( new[] { new ThemeParkIncCdkEntry( 1190, 0, 1, 7500 ), new ThemeParkIncCdkEntry( 1191, 1, 1, 7500 ), new ThemeParkIncCdkEntry( 1192, 2, 1, 7500 ) },
			save.Cards.Where( card => !card.IsEmpty ).ToArray() );
		Assert.AreEqual( (3, 1, 0, 1, 0), (save.CardCount, (int)save.SwearFilter, (int)save.NewCareer, (int)save.ObstacleResearched, (int)save.ObstacleRemoved) );
		Assert.AreEqual( 177, save.Statuses![59] );
		Assert.AreEqual( (1, 1), (save.Regions![13], save.Regions[14]) );
		Assert.AreEqual( 2, save.Zones!.Count );
		var science = save.Zones[1];
		Assert.AreEqual( ("science", 1, 0, 1, 5, 2, (byte)1, 11), (science.Name, (int)science.Available, (int)science.Open, science.ParkNumber, science.ChallengesDone, science.CurrentTechLevel,
			science.Awards[6].Flag, science.Awards[6].Value) );
		var options = save.Options!;
		Assert.AreEqual( (new ThemeParkIncVolume( true, 75 ), new ThemeParkIncVolume( true, 70 ), new ThemeParkIncVolume( false, 100 )), (options.VolumeA, options.Speech, options.Movie) );
		Assert.AreEqual( (true, true, false, 100, 70f, 200f, 10f, 1), (options.AdvisorOn, options.TutorialOn, options.TooltipsOn, options.GameSpeed, options.ZoomMin, options.ZoomMax, options.FpsClip, options.MusicEnabled) );
		Assert.AreEqual( ((byte)0, (byte)1, 12057), (save.GmiFlag, save.GameFinished, save.FirstHourTime) );
		CollectionAssert.AreEqual( new[] { false, true, false }, save.Shares!.SlotsPresent.ToArray() );
		Assert.AreEqual( (5500, 18, "Theme Park AG", 126), (save.Shares.NumberOwned, save.Shares.PercentCompanySharesOwned, save.Shares.CompanyName, save.Shares.CurrentValue) );
		Assert.AreEqual( 2, save.AmbientTagCount );
	}

	[TestMethod]
	public void WritingAReadFileReproducesItByteForByte()
	{
		foreach ( var layout in new[] { new Layout(), new Layout { Zones = Array.Empty<string>(), MapFlags = 0, AmbientTags = 0 }, new Layout { MapFlags = ThemeParkIncGlobalSave.MaximumMapFlags } } )
		{
			var bytes = Create( layout );
			CollectionAssert.AreEqual( bytes, ThemeParkIncGlobalSave.Read( bytes ).Write() );
		}
	}

	[TestMethod]
	public void AMarkerThatDiffersIsASuccessfulLoadThatStopsThere()
	{
		var stopped = ThemeParkIncGlobalSave.Read( Create( new Layout { BrokenMarker = "TATS" } ) );
		Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Stopped, stopped.Outcome );
		Assert.AreEqual( 5, stopped.Mission, "the fields before the marker are read" );
		Assert.IsNotNull( stopped.Statuses );
		Assert.IsNull( stopped.Regions, "nothing after the marker is read" );
		Assert.IsNull( stopped.Zones );
		Assert.ThrowsException<InvalidOperationException>( () => stopped.Write(), "a stopped load cannot be written until its sections are filled" );
		foreach ( var marker in new[] { "AMTA", "MEHT", "DTOT", "RAHS", "MAPS" } )
			Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Stopped, ThemeParkIncGlobalSave.Read( Create( new Layout { BrokenMarker = marker } ) ).Outcome, marker );
		Assert.IsNotNull( ThemeParkIncGlobalSave.Read( Create( new Layout { BrokenMarker = "MAPS" } ) ).MapFlagsRaw );
	}

	[TestMethod]
	public void TheHeaderTakesAnyVersionFromTwelveAndAnyTheme()
	{
		Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Failed, ThemeParkIncGlobalSave.Read( Create( new Layout { Version = 11 } ) ).Outcome );
		var newer = ThemeParkIncGlobalSave.Read( Create( new Layout { Version = 13, Theme = 7 } ) );
		Assert.AreEqual( (ThemeParkIncGlobalSaveOutcome.Complete, 13, 7), (newer.Outcome, newer.Version, newer.Theme) );
		Assert.AreEqual( 12, BinaryPrimitives.ReadInt32LittleEndian( newer.Write().AsSpan( 4 ) ), "the writer stores version 12" );
	}

	[TestMethod]
	public void TrailingBytesAreIgnored()
	{
		var bytes = Create( new Layout() );
		var save = ThemeParkIncGlobalSave.Read( bytes.Concat( new byte[] { 1, 2, 3 } ).ToArray() );
		Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Complete, save.Outcome );
		CollectionAssert.AreEqual( bytes, save.Write() );
	}

	[TestMethod]
	public void EveryTruncationFailsOrStopsAsTheBodyDecides()
	{
		var valid = Create( new Layout() );
		var tats = valid.AsSpan().IndexOf( "TATS"u8 );
		var dtot = valid.AsSpan().IndexOf( "DTOT"u8 );
		var maps = valid.AsSpan().IndexOf( "MAPS"u8 );
		for ( var length = 0; length < valid.Length; length++ )
		{
			var outcome = ThemeParkIncGlobalSave.Read( valid.AsSpan( 0, length ) ).Outcome;
			Assert.AreNotEqual( ThemeParkIncGlobalSaveOutcome.Complete, outcome, $"prefix {length}" );
			if ( length < tats )
				Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Failed, outcome, $"a career field cut short fails (prefix {length})" );
			else if ( length < tats + 4 || length >= dtot && length < maps + 4 )
				// a short marker, and a shares or 0xAA0180 section cut short (their errors are ignored, so the next marker comes back short), are successful stops
				Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Stopped, outcome, $"prefix {length}" );
			else if ( length >= maps + 4 )
				Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Failed, outcome, $"the ambient tags cut short fail (prefix {length})" );
		}
	}

	[TestMethod]
	public void AMapFlagCountAboveTheObjectIsRefused()
	{
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( Create( new Layout { MapFlags = ThemeParkIncGlobalSave.MaximumMapFlags + 1 } ) ) );
		Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Complete, ThemeParkIncGlobalSave.Read( Create( new Layout { MapFlags = -1 } ) ).Outcome, "a negative count reads no flags" );
	}

	[TestMethod]
	public void ARepeatedZoneNameFailsTheLoad()
	{
		var save = ThemeParkIncGlobalSave.Read( Create( new Layout { Zones = new[] { "science", "science" } } ) );
		Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Failed, save.Outcome );
	}

	[TestMethod]
	public void CompanyNameTextKeepsPairsAndRefusesUnpairedSurrogates()
	{
		Assert.AreEqual( "A🎢", ThemeParkIncGlobalSave.Read( Create( new Layout { CompanyName = "A🎢" } ) ).Shares!.CompanyName );
		Assert.AreEqual( "C", ThemeParkIncGlobalSave.Read( Create( new Layout { CompanyName = "C\0\uD800" } ) ).Shares!.CompanyName );
		var error = Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncGlobalSave.Read( Create( new Layout { CompanyName = "A\uD800\0\uDC00" } ) ).Shares );
		StringAssert.Contains( error.Message, "code unit 1" );
	}

	/// <summary>
	/// <c>OPENTPW_TPI_SAVES</c>: a folder with original Theme Park Inc <c>.GMS</c> files and any <c>.GMI</c> files (kept private). Each must read completely, to its last byte, and write back
	/// byte for byte.
	/// </summary>
	[TestMethod]
	public void OriginalGlobalSavesReadCompletelyAndWriteBackByteForByte()
	{
		foreach ( var file in CorpusFiles() )
		{
			var bytes = File.ReadAllBytes( file );
			var save = ThemeParkIncGlobalSave.Read( bytes );
			Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Complete, save.Outcome, file );
			Assert.AreEqual( ThemeParkIncGlobalSave.SupportedVersion, save.Version, file );
			Assert.IsTrue( save.Zones!.Count > 0 && save.Zones.All( zone => zone.Name.Length > 0 ), file );
			CollectionAssert.AreEqual( bytes, save.Write(), $"{file} round trip" );
			if ( Path.GetExtension( file ).Equals( ".GMS", StringComparison.OrdinalIgnoreCase ) )
				// the flag word is cleared when the object is researched (0x74D740, "GlobaMasterSave has been informed that Object %d has been researched")
				CollectionAssert.AreEqual( ThemeParkIncCareer.BuiltInCards.Select( card => (card.Id, card.Level, card.Amount) ).ToArray(),
					save.Cards.Take( 16 ).Select( card => (card.Id, card.Level, card.Amount) ).ToArray(), $"{file}: the reset's built-in CDK table" );
		}
	}

	internal static System.Collections.Generic.List<string> CorpusFiles()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPI_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_SAVES to a folder with Theme Park Inc .GMS or .GMI files." );
		var files = Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".GMS" or ".GMI" ).ToList();
		if ( files.Count == 0 )
			Assert.Inconclusive( "The OPENTPW_TPI_SAVES folder has no .GMS or .GMI files." );
		return files;
	}
}
