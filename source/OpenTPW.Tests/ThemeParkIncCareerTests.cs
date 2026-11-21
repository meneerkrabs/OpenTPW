using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Career files from memory (synthetic) or from a Theme Park Inc data folder; paths compare without case, as the original's Windows file system does.</summary>
internal sealed class TestCareerFiles : IThemeParkIncCareerFiles
{
	private readonly Dictionary<string, byte[]> files = new( StringComparer.OrdinalIgnoreCase );
	private readonly string? root;

	public TestCareerFiles( string? root = null ) => this.root = root;

	public TestCareerFiles With( string path, string text )
	{
		files[path] = Encoding.ASCII.GetBytes( text );
		return this;
	}

	public bool HasCdkRegistrations => false;

	public byte[]? Read( string path )
	{
		if ( files.TryGetValue( path, out var bytes ) )
			return bytes;
		if ( root == null )
			return null;
		var current = root;
		foreach ( var part in path.Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var match = Directory.Exists( current ) ? Directory.EnumerateFileSystemEntries( current ).FirstOrDefault( entry => string.Equals( Path.GetFileName( entry ), part, StringComparison.OrdinalIgnoreCase ) ) : null;
			if ( match == null )
				return null;
			current = match;
		}
		return File.Exists( current ) ? File.ReadAllBytes( current ) : null;
	}

	/// <summary>The shipped data under <c>OPENTPW_TPI_PATH</c> (its <c>Data</c> folder when it has one), or an inconclusive test.</summary>
	public static TestCareerFiles Shipped()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( entry => Path.GetFileName( entry ).Equals( "Data", StringComparison.OrdinalIgnoreCase ) );
		return new TestCareerFiles( data ?? path );
	}

	/// <summary>The shipped table: FiringLineForRank 255, 5, 5, 5, 5, 5 and the count 6.</summary>
	public static readonly int[] ShippedFiringLines = { 255, 5, 5, 5, 5, 5, 6 };
}

[TestClass]
public class ThemeParkIncMissionTests
{
	private static ThemeParkIncMission Parse( string text, out bool ok )
	{
		var mission = new ThemeParkIncMission();
		ok = mission.Object.Parse( Encoding.ASCII.GetBytes( text ), overlay: false );
		return mission;
	}

	[TestMethod]
	public void TheSchemaBindsTheProvenOffsets()
	{
		var mission = new ThemeParkIncMission();
		Assert.AreEqual( ThemeParkIncMission.Size, 8 + mission.Object.Words.Count * 4, "202 slots after the data base" );
		var parsed = Parse( "Info.Number 7\nInfo.NoResearch 1\nCompletion.EndingTheme 3\nTechLevel.NewMaxGroup 4\nBoundaryClicks[63].Y -2\nExitStatistic[2].Type 8\n"
			+ "ExitStatistic[2].TargetStaffType 9\nObstacle.GoldenTickets 6\nObstacleStaff[4].Number 3\nPromotion.StockAward 11\nRequiredChallenges.Number 12\n", out var ok );
		Assert.IsTrue( ok );
		Assert.AreEqual( (7, 1, 3, 4), (parsed.Object[0x0C], parsed.Object[0x10], parsed.Object[0x2C], parsed.Object[0x34]) );
		Assert.AreEqual( -2, parsed.Object[0x3C + 63 * 8] );
		Assert.AreEqual( (8, 9), (parsed.Object[0x240 + 2 * 0x1C], parsed.Object[0x254 + 2 * 0x1C]) );
		Assert.AreEqual( (6, 3, 11, 12), (parsed.Object[0x2DC], parsed.Object[0x2E8 + 4 * 0xC], parsed.Object[0x324], parsed.Object[0x32C]) );
		Assert.AreEqual( (64, 3, 5), (parsed.BoundaryClickCount, parsed.ExitStatisticCount, parsed.ObstacleStaffCount), "each count is one past the highest index set" );
	}

	[TestMethod]
	public void AParseResetsToDefaultsUnlessItOverlays()
	{
		var mission = Parse( "Info.Number 7\nExitStatistic[1].Type 8\n", out _ );
		Assert.IsTrue( mission.Object.Parse( "Completion.EndOfGame 1\n"u8, overlay: true ) );
		Assert.AreEqual( (7, 8, 1, 2), (mission.Number, mission.ExitStatistic( 1 ).Type, mission.EndOfGame, mission.ExitStatisticCount) );
		Assert.IsTrue( mission.Object.Parse( "Completion.EndOfGame 0\n"u8, overlay: false ) );
		Assert.AreEqual( (0, 0, 0), (mission.Number, mission.ExitStatistic( 1 ).Type, mission.ExitStatisticCount), "the reset clears values and counts" );
	}

	[TestMethod]
	public void AnArrayIndexAtTheCapacityStopsTheParseWithTheEarlierLinesKept()
	{
		var mission = Parse( "Info.Number 3\nExitStatistic[5].Type 8\nInfo.NoResearch 1\n", out var ok );
		Assert.IsFalse( ok );
		Assert.AreEqual( (3, 0, 0), (mission.Number, mission.NoResearch, mission.ExitStatisticCount) );
		Parse( "ObstacleStaff[5].Ability 1\n", out ok );
		Assert.IsFalse( ok );
		Parse( "BoundaryClicks[64].X 1\n", out ok );
		Assert.IsFalse( ok );
	}

	[TestMethod]
	public void ARangeLeafRefusesAValueOutsideItsRange()
	{
		foreach ( var line in new[] { "Info.NoResearch 2", "Info.NoResearch -1", "Completion.EndOfGame 2", "TechLevel.Theme 3" } )
		{
			Parse( line + "\n", out var ok );
			Assert.IsFalse( ok, line );
		}
		Assert.AreEqual( 2, Parse( "TechLevel.Theme 2\n", out var good ).TechLevelTheme );
		Assert.IsTrue( good );
	}

	[TestMethod]
	public void MalformedLinesStopTheParse()
	{
		foreach ( var text in new[] { "Info.Number 4x\n", "Info.Number 5#comment\n", "Foo.Bar 1\n", "Info.Bar 1\n", "info.Number 1\n", "Info.Number", "Info[0].Number 1\n",
			"ExitStatistic.Type 1\n", "ExitStatistic[a].Type 1\n", "ExitStatistic[0]Type 1\n", "9Info.Number 1\n", "Info.9Number 1\n", "Info.Number+1\n" } )
		{
			Parse( text, out var ok );
			Assert.IsFalse( ok, text );
		}
	}

	[TestMethod]
	public void ValueTextFollowsTheOriginalsChecks()
	{
		Assert.AreEqual( 5, Parse( "Info.Number 5 # comment after white space\n", out var ok ).Number );
		Assert.IsTrue( ok );
		Assert.AreEqual( 0, Parse( "Info.Number -\n", out ok ).Number, "a lone minus is atol's 0" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 0, Parse( "Info.Number ", out ok ).Number, "white space then the end of the file is atol's 0" );
		Assert.IsTrue( ok );
		Assert.AreEqual( -12, Parse( "Info.Number\t-12\r\n", out ok ).Number );
		Assert.IsTrue( ok );
		Assert.AreEqual( unchecked( (int)4294967295 ), Parse( "Info.Number 4294967295\n", out ok ).Number, "atol wraps in 32 bits" );
		Assert.IsTrue( ok );
		var multi = Parse( "Obstacle.Type.InTheme 1700 1\n", out ok );
		Assert.IsTrue( ok );
		Assert.AreEqual( (1700, 1), (multi.ObstacleType, multi.ObstacleInTheme), "one line may set several fields of a group" );
		var blank = Parse( "\n\n\nInfo.Number 9\n#\n   # indented comment\n\nObstacle.Type 3", out ok );
		Assert.IsTrue( ok );
		Assert.AreEqual( (9, 3), (blank.Number, blank.ObstacleType) );
		Assert.ThrowsException<InvalidDataException>( () => new ThemeParkIncMission().Object.Parse( new byte[] { 0xE9, (byte)'I' }, overlay: false ), "a byte above 0x7F where a character is classified" );
		Assert.IsTrue( new ThemeParkIncMission().Object.Parse( new byte[] { (byte)'#', 0xE9, (byte)'\n' }, overlay: false ), "a comment's bytes are never classified" );
	}

	[TestMethod]
	public void AMissingFileFailsAndChangesNothingAndAnEmptyFileSucceedsWithoutAReset()
	{
		var mission = Parse( "Info.Number 4\n", out _ );
		Assert.IsFalse( mission.Object.Load( null, overlay: false ) );
		Assert.AreEqual( 4, mission.Number );
		Assert.IsTrue( mission.Object.Load( Array.Empty<byte>(), overlay: false ) );
		Assert.AreEqual( 4, mission.Number );
		Assert.IsTrue( mission.Object.Load( "\0Info.Number 6\n"u8.ToArray(), overlay: false ) );
		Assert.AreEqual( 0, mission.Number, "a NUL ends the text after the reset" );
	}

	[TestMethod]
	public void TheMissionPathFollowsTheFormat() => Assert.AreEqual( ("/missions/Miss00.sam", "/missions/Miss17.sam", "/missions/Miss-1.sam"),
		(ThemeParkIncMission.PathOf( 0 ), ThemeParkIncMission.PathOf( 17 ), ThemeParkIncMission.PathOf( -1 )) );

	/// <summary>Every shipped mission file (<c>OPENTPW_TPI_PATH</c>) parses completely, numbers itself and has the fields the campaign notes list.</summary>
	[TestMethod]
	public void EveryShippedMissionParses()
	{
		var files = TestCareerFiles.Shipped();
		for ( var counter = 0; counter < 18; counter++ )
		{
			var mission = new ThemeParkIncMission();
			Assert.IsTrue( mission.Object.Load( files.Read( ThemeParkIncMission.PathOf( counter ) ), overlay: false ), $"mission {counter}" );
			Assert.AreEqual( counter, mission.Number, $"mission {counter} names itself" );
			Assert.IsTrue( mission.ExitStatisticCount <= ThemeParkIncMission.ExitStatisticCapacity && mission.ObstacleStaffCount <= ThemeParkIncMission.ObstacleStaffCapacity );
			Assert.AreEqual( counter == 17 ? 1 : 0, mission.EndOfGame, $"mission {counter}" );
		}
		var first = new ThemeParkIncMission();
		first.Object.Load( files.Read( ThemeParkIncMission.PathOf( 0 ) ), overlay: false );
		Assert.AreEqual( (1, 1700, 1, 1, 1700), (first.NoResearch, first.ObstacleOpened, first.LoadNewHoardings, first.EndingTheme, first.ObstacleType) );
		Assert.AreEqual( new ThemeParkIncExitStatistic( 0, 8, 1, 40, 0, 0, 0 ), first.ExitStatistic( 0 ) );
		Assert.AreEqual( (43, 1), (first.BoundaryClickCount, first.ExitStatisticCount) );
		Assert.AreEqual( (512, 512), first.BoundaryClick( 42 ) );
	}
}

[TestClass]
public class ThemeParkIncCareerTests
{
	private static TestCareerFiles Synthetic() => new TestCareerFiles()
		.With( "/missions/Miss00.sam", "Info.Number 0\nExitStatistic[0].Type 8\nExitStatistic[0].InTheme 1\nExitStatistic[0].TargetValue 40\nObstacle.GoldenTickets 2\n" )
		.With( "/missions/Miss01.sam", "Info.Number 1\nObstacleStaff[0].Ability 3\nObstacleStaff[0].Number 1\nObstacleStaff[1].Ability 4\nCompletion.ThemeOpened 1\nCompletion.ThemeNumber 2\n" )
		.With( "/missions/Miss02.sam", "Info.Number 2\nCompletion.EndOfGame 1\n" )
		.With( "/levels/science/global.sam", "ParkInfo.AvailableFromStart 1\n" )
		.With( "/levels/water/global.sam", "ParkInfo.AvailableFromStart 0\n" );

	private static ThemeParkIncCareer New( IThemeParkIncCareerFiles? files = null ) => new( files ?? Synthetic(), TestCareerFiles.ShippedFiringLines );

	[TestMethod]
	public void TheResetGivesTheNewCareersValues()
	{
		var career = New();
		Assert.AreEqual( (0, 1, 5, 0, false, true, true, 16, 0), (career.MissionCounter, career.Rank, career.FiringLine, career.GoldenTickets, career.GameFinished, career.SwearFilter,
			career.NewCareer, career.CardCount, career.CardCursor) );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 15 ).Select( index => (byte)(index is 1 or 7 or 9 ? 1 : 0) ).ToArray(), career.Abilities.ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 15 ).Select( index => index == 14 ? 1 : 0 ).ToArray(), career.Regions.ToArray() );
		CollectionAssert.AreEqual( new byte[] { 0, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, career.Flags.ToArray() );
		CollectionAssert.AreEqual( ThemeParkIncCareer.BuiltInCards.ToArray(), career.Cards.Take( 16 ).ToArray() );
		Assert.IsTrue( career.Cards.Skip( 16 ).All( card => card == ThemeParkIncCdkEntry.Empty ) );
		// the mission file's exit statistics over the cleared statuses: state 0, type, target, time, zone
		Assert.AreEqual( (0, 8, 40, 0, 1), (career.StatusWord( 0, 0 ), career.StatusWord( 0, 1 ), career.StatusWord( 0, 2 ), career.StatusWord( 0, 3 ), career.StatusWord( 0, 8 )) );
		Assert.AreEqual( (0, 0), (career.StatusWord( 1, 1 ), career.StatusWord( 1, 8 )), "an unused slot takes the file's default zone 0" );
	}

	[TestMethod]
	public void AMissingMissionFileLeavesTheStatusesAsTheyWere()
	{
		var career = New( new TestCareerFiles() );
		Assert.AreEqual( 4, career.StatusWord( 0, 8 ), "the reset's zone 4 stays when Miss00 is missing" );
		Assert.IsFalse( career.LoadMission() );
	}

	[TestMethod]
	public void AdvancingOpensTheTablesRegionAndLoadsTheNextMission()
	{
		var career = New();
		career.Advance();
		Assert.AreEqual( (1, 1, 1), (career.MissionCounter, career.Mission.Number, career.Regions[13]), "counter 1 opens region 13" );
		Assert.AreEqual( 0, career.StatusWord( 0, 1 ), "the statuses take the new file's (no exit statistic)" );
		career.DiscoverObstacleAbilities();
		Assert.AreEqual( ((byte)1, (byte)0, true), (career.Abilities[3], career.Abilities[4], career.ObstacleResearched), "only obstacle staff with a number discover their ability" );
		career.Advance();
		Assert.AreEqual( (2, false, 1), (career.MissionCounter, career.ObstacleResearched, career.Regions[10]) );
	}

	[TestMethod]
	public void PromotionRaisesTheFiringLineToTheRanksLimit()
	{
		var career = New();
		career.LowerFiringLine();
		career.LowerFiringLine();
		career.Promote( 2 );
		Assert.AreEqual( (2, 5), (career.Rank, career.FiringLine) );
		career.Promote( 0 );
		Assert.AreEqual( (0, 255), (career.Rank, career.FiringLine) );
		career.Promote( 6 );
		Assert.AreEqual( (6, 255), (career.Rank, career.FiringLine), "rank 6 reads the count dword and does not lower the line" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => career.Promote( 7 ) );
	}

	[TestMethod]
	public void GoldenTicketsAreAddedAndSpentOnlyWhenEnough()
	{
		var career = New();
		career.AddGoldenTickets( 3 );
		Assert.IsFalse( career.TrySpendGoldenTickets( 4 ) );
		Assert.AreEqual( 3, career.GoldenTickets );
		Assert.IsTrue( career.TrySpendGoldenTickets( 3 ) );
		Assert.AreEqual( 0, career.GoldenTickets );
	}

	[TestMethod]
	public void ZoneRecordsAreMadeFromTheirGlobalSamAndOpenedByAMission()
	{
		var career = New();
		Assert.AreEqual( (true, 1), (career.ZoneRecord( 1 )!.Available, career.ZoneRecord( 1 )!.ParkNumber) );
		// the original's record constructor reads the global.sam it failed to load (a null object) and faults: a zone without one is refused
		Assert.ThrowsException<InvalidDataException>( () => career.ZoneRecord( 0 ) );
		career.OpenTheme( 2 );
		Assert.IsFalse( career.Zones.ContainsKey( "water" ), "opening a zone does not create its record" );
		Assert.IsFalse( career.ZoneRecord( 2 )!.Available );
		career.OpenTheme( 2 );
		Assert.IsTrue( career.ZoneRecord( 2 )!.Available );
	}

	[TestMethod]
	public void CompletingMissionsSpendsTicketsOpensThemesAndFinishesTheGame()
	{
		var career = New();
		career.AddGoldenTickets( 1 );
		career.ZoneRecord( 2 );
		var first = career.CompleteMission();
		Assert.AreEqual( (true, 1, 1), (first.Advanced, career.MissionCounter, career.GoldenTickets), "two tickets are not there to spend: nothing is spent and the mission still advances" );
		var second = career.CompleteMission();
		Assert.AreEqual( (true, true), (second.Advanced, career.ZoneRecord( 2 )!.Available), "Miss01 opens water" );
		var last = career.CompleteMission();
		Assert.AreEqual( (false, true, true, 2), (last.Advanced, last.Finished, career.GameFinished, career.MissionCounter) );
		Assert.AreEqual( ThemeParkIncCompletion.None, career.CompleteMission(), "a finished game does nothing" );
	}

	[TestMethod]
	public void ASnapshotRestoresTheSameCareer()
	{
		var career = New();
		career.ChangeTheme( "science" );
		career.AddGoldenTickets( 4 );
		career.Advance();
		career.Promote( 3 );
		var saved = career.Capture();
		Assert.IsTrue( ThemeParkIncCareer.IsValid( saved ) );
		var restored = New();
		restored.Restore( JsonSerializer.Deserialize<ThemeParkIncCareerSnapshot>( JsonSerializer.Serialize( saved ) )! );
		Assert.AreEqual( JsonSerializer.Serialize( saved ), JsonSerializer.Serialize( restored.Capture() ) );
		Assert.AreEqual( ("science", 1), (restored.CurrentZone!.Name, restored.Mission.Number) );
		Assert.IsFalse( ThemeParkIncCareer.IsValid( saved with { Regions = new int[3] } ) );
		Assert.IsFalse( ThemeParkIncCareer.IsValid( saved with { CurrentZone = "water" } ) );
	}

	[TestMethod]
	public void ACareerWrittenAndLoadedBackIsTheSame()
	{
		var career = New();
		career.ChangeTheme( "science" );
		career.Advance();
		career.AddGoldenTickets( 2 );
		var session = NewSession();
		var bytes = career.ToGlobalSave( session, theme: 1, refresh: false ).Write();
		var loaded = New();
		Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Complete, loaded.Load( ThemeParkIncGlobalSave.Read( bytes ), "science" ) );
		CollectionAssert.AreEqual( bytes, loaded.ToGlobalSave( session, 1, false ).Write() );
		Assert.AreEqual( (1, 2, "science"), (loaded.MissionCounter, loaded.GoldenTickets, loaded.CurrentZone!.Name) );
	}

	[TestMethod]
	public void AStoppedLoadKeepsTheResetValuesAfterTheStop()
	{
		var career = New();
		career.Advance();
		career.ChangeTheme( "science" );
		var bytes = career.ToGlobalSave( NewSession(), 1, false ).Write();
		var tats = bytes.AsSpan().IndexOf( "TATS"u8 );
		bytes[tats] = (byte)'X';
		var loaded = New();
		Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Stopped, loaded.Load( ThemeParkIncGlobalSave.Read( bytes ), null ) );
		Assert.AreEqual( 1, loaded.MissionCounter, "the counter is before the marker" );
		Assert.AreEqual( (0, 1), (loaded.Regions[13], loaded.Regions[14]), "the regions are after it: the reset's" );
		Assert.AreEqual( 0, loaded.Zones.Count, "no zone record was read" );
	}

	/// <summary>
	/// <c>OPENTPW_TPI_SAVES</c> and <c>OPENTPW_TPI_PATH</c>: each original <c>.GMS</c> loads into a career and the career writes it back byte for byte (the session sections carried through).
	/// </summary>
	[TestMethod]
	public void OriginalGlobalSavesRoundTripThroughTheCareer()
	{
		var files = TestCareerFiles.Shipped();
		foreach ( var file in ThemeParkIncGlobalSaveTests.CorpusFiles() )
		{
			var bytes = File.ReadAllBytes( file );
			var save = ThemeParkIncGlobalSave.Read( bytes );
			var career = new ThemeParkIncCareer( files, TestCareerFiles.ShippedFiringLines );
			Assert.AreEqual( ThemeParkIncGlobalSaveOutcome.Complete, career.Load( save, null ), file );
			Assert.AreEqual( save.Mission, career.Mission.Number, $"{file}: the saved counter's mission file is loaded" );
			CollectionAssert.AreEqual( bytes, career.ToGlobalSave( save, save.Theme, save.GmiFlag != 0 ).Write(), file );
		}
	}

	/// <summary>A session whose untraced sections are the new-session bytes the tests need (any well-formed bytes do: the career carries them through).</summary>
	internal static ThemeParkIncGlobalSave NewSession() => new();
}
