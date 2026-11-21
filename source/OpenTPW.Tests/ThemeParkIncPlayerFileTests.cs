using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// <c>OPENTPW_TPI_PATH</c>: the career in a game's park (the zone record, a won challenge's promotion and counts) and the player file's flows in the game flow (docs/reverse/TPI-CAREER.md §3.4).
/// The game files are the retail data; the save folder is a temporary one.
/// </summary>
[TestClass]
public class ThemeParkIncPlayerFileTests
{
	private BaseFileSystem? previousFiles, previousSaves;
	private Logger? previousLog;
	private string root = "";

	[TestInitialize]
	public void Initialize()
	{
		var shipped = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( shipped ) || !Directory.Exists( shipped ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( shipped! ).FirstOrDefault( entry => Path.GetFileName( entry ).Equals( "Data", StringComparison.OrdinalIgnoreCase ) ) ?? shipped!;
		(previousFiles, previousSaves, previousLog) = (FileSystem, SaveFileSystem, Log);
		root = Directory.CreateTempSubdirectory( "opentpw-tpi-player-" ).FullName;
		FileSystem = new BaseFileSystem( data );
		SaveFileSystem = new BaseFileSystem( root );
		Log = new Logger();
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( root.Length == 0 )
			return;
		(FileSystem, SaveFileSystem, Log) = (previousFiles!, previousSaves!, previousLog!);
		Directory.Delete( root, recursive: true );
	}

	private static ParkEconomy Science() => new( BalanceSettings.LoadThemeParkInc( "science" ), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7,
		EconomyRules.ThemeParkIncMoney );

	private static GameFlow Flow()
	{
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		flow.IncNewSession = () => ThemeParkIncSession.New( ParkEconomy.GameCareerFiles.Instance.Read, "Theme Park Inc." );
		return flow;
	}

	private static void NextTurn( ParkEconomy economy ) => economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );

	[TestMethod]
	public void AGamesParkHasItsZoneRecordAndAWonChallengePromotesAndCounts()
	{
		var economy = Science();
		var career = economy.TpiCareer!;
		Assert.IsFalse( economy.TpiHeadlessCareer );
		Assert.AreEqual( ("science", 1), (career.CurrentZone!.Name, career.CurrentZone.ParkNumber) );
		var challenges = economy.TpiChallenges!;
		economy.OpenPark();
		NextTurn( economy );
		Assert.AreEqual( 0, economy.DebugOfferThemeParkIncChallenge( 0 ) );
		economy.AcceptThemeParkIncChallenge( 0 );
		var slot = challenges.Slots[0];
		slot.PromoGiven = 3;
		foreach ( var other in challenges.Slots.Skip( 2 ) )
			other.Type = 0;
		career.FiringLine = 1;
		challenges.AddEvent( slot.Type, slot.TargetVal, 0, false );
		NextTurn( economy );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Won, slot.Status );
		Assert.AreEqual( (3, 5), (career.Rank, career.FiringLine), "PromoGiven 3 promotes; the line rises to FiringLineForRank[3]" );
		Assert.AreEqual( (1, (byte)0), (career.CurrentZone.ChallengesDone, career.CurrentZone.Data.AllChallengesDone), "slot 1 still holds a challenge" );
		challenges.Slots[1].Type = 0;
		slot.Status = ThemeParkIncChallengeStatus.Offered;
		economy.AcceptThemeParkIncChallenge( 0 );
		challenges.AddEvent( slot.Type, slot.TargetVal, 0, false );
		NextTurn( economy );
		Assert.AreEqual( (2, (byte)1), (career.CurrentZone.ChallengesDone, career.CurrentZone.Data.AllChallengesDone), "every slot empty or won marks all done" );
	}

	[TestMethod]
	public void SaveSkipLeaveAndLoadPairTheParkWithItsCareer()
	{
		var flow = Flow();
		var economy = Science();
		var order = new List<string>();
		string? park = null;
		void SaveScience()
		{
			// SaveThemeParkIncScience's order: the player file, then the park save
			flow.WriteThemeParkIncPlayerFile( economy, "science" );
			order.Add( "player file" );
			park = ParkSaveFile.Serialize( economy );
			order.Add( "park" );
		}
		SaveScience();
		Assert.IsTrue( File.Exists( GameFlow.ThemeParkIncPlayerFilePath() ) );
		economy.TpiCareer!.CompleteMission();
		Assert.AreEqual( 1, economy.TpiCareer.MissionCounter );
		order.Clear();
		flow.SaveThemeParkIncOnLeave( economy, "science", SaveScience );
		CollectionAssert.AreEqual( new[] { "player file", "park" }, order, "leaving runs the full save: the global save, then the park" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( park! ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( 1, restored.TpiCareer!.MissionCounter, "the park save carries the career of the leave" );
		Flow().AttachThemeParkIncPlayerCareer( restored, "science" );
		Assert.AreEqual( (1, 1, "science"), (restored.TpiCareer.MissionCounter, restored.TpiCareer.Mission.Number, restored.TpiCareer.CurrentZone!.Name), "Load A Game pairs them" );
	}

	[TestMethod]
	public void LoadingALayoutOrContinuingRetiresTheParkWithoutTheLeavesSave()
	{
		// both loads replace the park through the helper that holds incReplacingPark, so neither overwrites the Science slot or the player file
		const string relative = "source/OpenTPW/Client/GameFlow.ThemeParkIncLayout.cs";
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, relative ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, $"{relative} was not found above {AppContext.BaseDirectory}" );
		var source = File.ReadAllText( Path.Combine( directory.FullName, relative ) );
		Assert.AreEqual( 2, System.Text.RegularExpressions.Regex.Matches( source, @"\bTearDownReplacingThemeParkIncPark\(\);" ).Count, "Continue and Load layout" );
		Assert.AreEqual( 1, System.Text.RegularExpressions.Regex.Matches( source, @"\bTearDown\(" ).Count, "only the helper tears down" );
	}

	[TestMethod]
	public void LeavingWritesThePlayerFileOutsideScienceAndNothingWhileALoadReplacesTheParkOrWhenLost()
	{
		var flow = Flow();
		var economy = Science();
		flow.incReplacingPark = true;
		flow.SaveThemeParkIncOnLeave( economy, "water", null );
		Assert.IsFalse( File.Exists( GameFlow.ThemeParkIncPlayerFilePath() ), "a load replacing the park discards it" );
		flow.incReplacingPark = false;
		economy.LoseThemeParkIncPark( 0 );
		flow.SaveThemeParkIncOnLeave( economy, "science", () => Assert.Fail( "a lost park is not saved" ) );
		Assert.IsFalse( File.Exists( GameFlow.ThemeParkIncPlayerFilePath() ) );
		flow.SaveThemeParkIncOnLeave( Science(), "water", null );
		Assert.AreEqual( 2, ThemeParkIncGlobalSave.Read( File.ReadAllBytes( GameFlow.ThemeParkIncPlayerFilePath() ) ).Theme, "a zone without an OpenTPW park slot writes the player file" );
	}

	[TestMethod]
	public void ThePlayerFilesCareerWinsAndEnterGameBuildsTheParkWithIt()
	{
		var economy = Science();
		var park = ParkSaveFile.Serialize( economy );
		economy.TpiCareer!.CompleteMission();
		economy.TpiCareer.AddGoldenTickets( 3 );
		Flow().WriteThemeParkIncPlayerFile( economy, "science" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( park ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( 0, restored.TpiCareer!.MissionCounter );
		Flow().AttachThemeParkIncPlayerCareer( restored, "science" );
		Assert.AreEqual( (1, 3), (restored.TpiCareer.MissionCounter, restored.TpiCareer.GoldenTickets), "the player file's career wins over the park save's" );
		var career = Flow().LoadThemeParkIncPlayerCareer( "science" )!;
		using ( ParkEconomy.EnterThemeParkIncCareer( career ) )
			Assert.AreSame( career, Science().TpiCareer, "Enter Game's park is built with the player file's career" );
		Assert.AreNotSame( career, Science().TpiCareer, "outside the scope a park builds a new career" );
	}

	[TestMethod]
	public void ThePlayerFileStaysInOpenTpwsOwnFolderAndNeverInTheGamesSaveFolder()
	{
		var game = Path.Combine( root, "Theme Park Inc" );
		var configuration = Path.Combine( root, "config" );
		Assert.AreEqual( Path.Combine( configuration, "save", GameFlow.ThemeParkIncPlayerFolder ), GameFlow.ThemeParkIncPlayerRoot( Path.Combine( game, "save" ), game, configuration ),
			"a save folder in the game folder is the original's own: the player file goes to the configuration's save folder" );
		Assert.AreEqual( Path.Combine( root, "own", GameFlow.ThemeParkIncPlayerFolder ), GameFlow.ThemeParkIncPlayerRoot( Path.Combine( root, "own" ), game, configuration ),
			"OPENTPW_SAVE_DIR and the other own folders keep it" );
		Assert.AreEqual( Path.Combine( root, "own", GameFlow.ThemeParkIncPlayerFolder ), GameFlow.ThemeParkIncPlayerRoot( Path.Combine( root, "own" ), null, configuration ) );
		Assert.AreEqual( Path.Combine( root, GameFlow.ThemeParkIncPlayerFolder, "users", "1New Player", "New Player.GMS" ), GameFlow.ThemeParkIncPlayerFilePath(),
			"the test's save folder is its own temporary one" );
		Flow().WriteThemeParkIncPlayerFile( Science(), "science" );
		Assert.IsTrue( File.Exists( GameFlow.ThemeParkIncPlayerFilePath() ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( root, "users" ) ), "nothing is written where the original keeps its players" );
		Assert.AreEqual( 0, Directory.EnumerateFiles( root, "*.tmp", SearchOption.AllDirectories ).Count(), "no temporary file is left" );
	}

	[TestMethod]
	public void ASaveFolderReachedThroughASymbolicLinkIsStillInsideTheGameFolder()
	{
		var game = Path.Combine( root, "Theme Park Inc" );
		Directory.CreateDirectory( Path.Combine( game, "save" ) );
		var linkedGame = Path.Combine( root, "linked game" );
		Directory.CreateSymbolicLink( linkedGame, game );
		var configuration = Path.Combine( root, "config" );
		var expected = Path.Combine( configuration, "save", GameFlow.ThemeParkIncPlayerFolder );
		Assert.AreEqual( expected, GameFlow.ThemeParkIncPlayerRoot( Path.Combine( linkedGame, "save" ), game, configuration ), "a save folder reached through a linked game folder" );
		Assert.AreEqual( expected, GameFlow.ThemeParkIncPlayerRoot( Path.Combine( game, "save" ), linkedGame, configuration ), "a game path that is itself a link" );
		var real = GameFlow.RealPath( Path.Combine( linkedGame, "save", "new" ) );
		Assert.AreEqual( GameFlow.RealPath( Path.Combine( game, "save", "new" ) ), real, "the link is resolved" );
		StringAssert.EndsWith( real, Path.Combine( "Theme Park Inc", "save", "new" ), "parts that do not exist yet are kept" );
	}

	[TestMethod]
	public void APlayerFileWhoseZoneHasAParkNumberOutsideTheTableIsRefusedOnLoad()
	{
		Flow().WriteThemeParkIncPlayerFile( Science(), "science" );
		var path = GameFlow.ThemeParkIncPlayerFilePath();
		var save = ThemeParkIncGlobalSave.Read( File.ReadAllBytes( path ) );
		save.Zones!.Single( zone => zone.Name == "science" ).ParkNumber = 9;
		File.WriteAllBytes( path, save.Write() );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Flow().LoadThemeParkIncPlayerCareer( "science" ) ).Message, "park number 9",
			"refused while the Personnel File is still up, before the park's zone entry would refuse it" );
	}

	[TestMethod]
	public void AnOversizedPlayerFileIsRefusedUnread()
	{
		var path = GameFlow.ThemeParkIncPlayerFilePath();
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		using ( var file = File.Create( path ) )
			file.SetLength( GameFlow.MaxThemeParkIncPlayerFileBytes + 1L );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Flow().LoadThemeParkIncPlayerCareer( "science" ) ).Message, "larger than" );
	}

	[TestMethod]
	public void ARestoredCareerTakesTheParksZoneAndARecordOutsideTheZoneTableIsRefused()
	{
		var economy = Science();
		var node = System.Text.Json.Nodes.JsonNode.Parse( ParkSaveFile.Serialize( economy ) )!.AsObject();
		node["TpiCareer"]!["CurrentZone"] = null;
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( ("science", 1), (restored.TpiCareer!.CurrentZone!.Name, restored.TpiCareer.CurrentZone.ParkNumber), "the load makes the park's zone current (0x74D110), whatever the save says" );
		foreach ( var zone in node["TpiCareer"]!["Zones"]!.AsArray() )
			zone!["ParkNumber"] = 9;
		var error = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
		StringAssert.Contains( error.Message, "outside the zone table", "a won challenge would find no record (0x74F4B0)" );
	}

	[TestMethod]
	public void APlayerFileThatFailsToLoadIsRefused()
	{
		Assert.IsNull( Flow().LoadThemeParkIncPlayerCareer( "science" ), "no file: no career" );
		var path = GameFlow.ThemeParkIncPlayerFilePath();
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllBytes( path, new byte[] { 1, 0, 0, 0, 11, 0, 0, 0 } );
		// EnterThemeParkIncZone and the Load A Game queue show this message on the Personnel File (ShowLoadFailure)
		var error = Assert.ThrowsException<InvalidDataException>( () => Flow().LoadThemeParkIncPlayerCareer( "science" ) );
		StringAssert.Contains( error.Message, "version 11" );
	}

	[TestMethod]
	public void AThemeParkWorldSaveMayNotCarryTheThemeParkIncCheats()
	{
		var world = new ParkEconomy( EconomyTestData.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		var json = System.Text.Json.Nodes.JsonNode.Parse( ParkSaveFile.Serialize( world ) )!.AsObject();
		using ( OpenTPW.Mods.OpenTpwExtensions.Use( CheatsTests.On() ) )
		{
			var cheated = Science();
			Assert.IsTrue( cheated.QueueCheat( new CheatAction( CheatKind.SkipMission ) ) );
			NextTurn( cheated );
			json["Cheats"] = System.Text.Json.Nodes.JsonNode.Parse( ParkSaveFile.Serialize( cheated ) )!["Cheats"]!.DeepClone();
		}
		var error = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ), world.Settings, world.Catalog ) );
		StringAssert.Contains( error.Message, "Theme Park Inc state" );
	}
}
