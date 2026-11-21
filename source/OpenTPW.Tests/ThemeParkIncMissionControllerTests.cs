using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>A park and world for the mission controller with settable answers; it records what the controller asked of it.</summary>
internal sealed class TestMissionHost : IThemeParkIncMissionHost
{
	public int ViewedZone { get; set; } = 1;
	public int BankBalance { get; set; }
	public int OutstandingLoans { get; set; }
	public int GuestsInPark { get; set; }
	public ushort CurrentObstacle { get; set; }
	public Dictionary<int, ushort> Attractions { get; } = new();
	public HashSet<int> Automatic { get; } = new();
	public bool? ObstacleStaffSlotsFilled { get; set; }
	public List<string> Calls { get; } = new();
	public List<ThemeParkIncCameraRequest> Cameras { get; } = new();
	public int Broadcasts { get; private set; }

	public void FindCurrentObstacle( int type )
	{
		Calls.Add( $"find {type}" );
		if ( type == 0 )
			CurrentObstacle = 0;
		if ( Attractions.TryGetValue( type, out var thing ) )
			CurrentObstacle = thing;
	}

	public ushort FindAttraction( int type ) => Attractions.TryGetValue( type, out var thing ) ? thing : (ushort)0;
	public bool OpensAutomatically( int type ) => Automatic.Contains( type );
	public void OpenObstacle( ushort thing ) => Calls.Add( $"open {thing}" );
	public void DestroyThing( ushort thing ) => Calls.Add( $"destroy {thing}" );
	public void LoadExtent( int index ) => Calls.Add( $"extent {index}" );
	public void PlayCamera( ThemeParkIncCameraRequest request ) => Cameras.Add( request );
	public void EnsureObstacleResearch() => Calls.Add( "research" );
	public void PostObjectiveLetter( int completed ) => Calls.Add( $"letter {completed}" );
	public void StatusChanged() => Broadcasts++;
}

[TestClass]
public class ThemeParkIncMissionControllerTests
{
	/// <summary>The park clock's turn at which <paramref name="days"/> whole days have passed since turn 0 (a turn is 3750 s).</summary>
	private static long TurnAtDays( int days ) => (long)Math.Ceiling( days * 86400.0 / 3750.0 );

	private static TestCareerFiles Files( string miss00, string miss01 = "Info.Number 1\n", string miss02 = "Info.Number 2\nCompletion.EndOfGame 1\n" ) => new TestCareerFiles()
		.With( "/missions/Miss00.sam", miss00 )
		.With( "/missions/Miss01.sam", miss01 )
		.With( "/missions/Miss02.sam", miss02 )
		.With( "/levels/arabian/global.sam", "ParkInfo.AvailableFromStart 0\n" )
		.With( "/levels/science/global.sam", "ParkInfo.AvailableFromStart 1\n" )
		.With( "/levels/water/global.sam", "ParkInfo.AvailableFromStart 0\n" );

	private static ThemeParkIncCareer Career( TestCareerFiles files )
	{
		var career = new ThemeParkIncCareer( files, TestCareerFiles.ShippedFiringLines );
		career.ChangeTheme( "science" );
		return career;
	}

	private static (ThemeParkIncMissionController Controller, ThemeParkIncCareer Career, TestMissionHost Host) Controller( string miss00, TestMissionHost? host = null,
		string miss01 = "Info.Number 1\n" )
	{
		var career = Career( Files( miss00, miss01 ) );
		host ??= new TestMissionHost();
		return (new ThemeParkIncMissionController( career, host, 0 ), career, host);
	}

	/// <summary>Runs the controller's update on every turn from <paramref name="from"/> through <paramref name="to"/>.</summary>
	private static void Run( ThemeParkIncMissionController controller, long from, long to )
	{
		for ( var turn = from; turn <= to; turn++ )
			controller.Update( turn );
	}

	private static string Statistic( int index, int type, int target, int zone = 1, int time = 0 ) =>
		$"ExitStatistic[{index}].Type {type}\nExitStatistic[{index}].TargetValue {target}\nExitStatistic[{index}].InTheme {zone}\nExitStatistic[{index}].TargetTime {time}\n";

	[TestMethod]
	public void EachExitTypeIsMeasuredFromThePark()
	{
		var host = new TestMissionHost { BankBalance = 500, OutstandingLoans = 200, GuestsInPark = 37 };
		var text = Statistic( 0, 1, 1 ) + Statistic( 1, 2, 1 ) + Statistic( 2, 3, 1 ) + Statistic( 3, 5, 1 ) + Statistic( 4, 8, 1 );
		var (controller, career, _) = Controller( text, host );
		career.ZoneRecord( 0 )!.Data.ChallengesDone = 2;
		career.ZoneRecord( 2 )!.Data.ChallengesDone = 3;
		CollectionAssert.AreEqual( new int?[] { 1, 300, 5, 37, 37 }, Enumerable.Range( 0, 5 ).Select( controller.Measure ).ToArray(),
			"1 cash less loans above 0, 2 cash less loans, 3 challenges won in every zone, 5 and 8 the guests in the park" );
		host.OutstandingLoans = 500;
		Assert.AreEqual( 0, controller.Measure( 0 ), "type 1 is 0 at a zero balance after the loans" );
		host.BankBalance = int.MinValue;
		host.OutstandingLoans = 1;
		Assert.AreEqual( (1, int.MaxValue), (controller.Measure( 0 ), controller.Measure( 1 )), "32-bit arithmetic wraps as the original's" );

		var (unmeasured, _, _) = Controller( Statistic( 0, 4, 10 ) + Statistic( 1, 6, 6 ) + Statistic( 2, 7, 51 ) + Statistic( 3, 9, 1 ), host );
		CollectionAssert.AreEqual( new int?[] { null, null, null, 0, 0 }, Enumerable.Range( 0, 5 ).Select( unmeasured.Measure ).ToArray(),
			"4 (monthly profit), 6 (awards) and 7 (shares) are not measurable; any other type measures 0" );
	}

	[TestMethod]
	public void TheStartTakesEachTypesBaselineInTheViewedZoneOnly()
	{
		var host = new TestMissionHost { BankBalance = 1000, GuestsInPark = 12 };
		var text = Statistic( 0, 2, 5 ) + Statistic( 1, 5, 50 ) + Statistic( 2, 8, 40, zone: 2 ) + Statistic( 3, 8, 40, zone: 4 ) + Statistic( 4, 4, 10 )
			+ "TechLevel.Theme 1\nTechLevel.NewMaxGroup 3\n";
		var (_, career, _) = Controller( text, host );
		CollectionAssert.AreEqual( new[] { 1, 1, 0, 1, 0 }, Enumerable.Range( 0, 5 ).Select( index => career.StatusWord( index, 0 ) ).ToArray(),
			"the science entries and the any-zone entry start; water waits for its zone, type 4 is not measurable" );
		Assert.AreEqual( (1000, 12, 0), (career.StatusWord( 0, 9 ), career.StatusWord( 1, 9 ), career.StatusWord( 3, 9 )), "cash, guests and zero baselines" );
		Assert.AreEqual( ThemeParkIncChallenges.Now( 0 ), career.StatusStart( 0 ) );
		Assert.AreEqual( 3, career.ZoneRecord( 1 )!.CurrentTechLevel, "the tech level rises" );

		host.ViewedZone = 2;
		host.BankBalance = 7;
		var water = new ThemeParkIncMissionController( career, host, 100 );
		Assert.AreEqual( (1, 1000), (career.StatusWord( 2, 0 ), career.StatusWord( 0, 9 )), "a start in water starts its entry and leaves the running ones" );
		Assert.AreEqual( ThemeParkIncChallenges.Now( 100 ), career.StatusStart( 2 ) );
		_ = water;
	}

	[TestMethod]
	public void TheStatisticsAreTestedOnceADayOnEveryFourthTick()
	{
		var host = new TestMissionHost { GuestsInPark = 10 };
		var (controller, career, _) = Controller( Statistic( 0, 8, 40 ), host );
		Run( controller, 1, 3 );
		Assert.AreEqual( (0, 0), (career.StatusWord( 0, 6 ), career.StatusWord( 0, 7 )), "turns 1 to 3 skip the statistics" );
		controller.Update( 4 );
		Assert.AreEqual( 0, career.StatusWord( 0, 6 ), "day 0 equals the stored days: nothing is stored" );
		var dayOne = TurnAtDays( 1 );
		var firstFour = (dayOne + 3) / 4 * 4;
		host.GuestsInPark = 39;
		Run( controller, 5, firstFour );
		Assert.AreEqual( (39, 1, 1), (career.StatusWord( 0, 6 ), career.StatusWord( 0, 7 ), career.StatusWord( 0, 0 )), "day 1's value is stored, below the target" );
		host.GuestsInPark = 45;
		Run( controller, firstFour + 1, TurnAtDays( 2 ) - 1 );
		Assert.AreEqual( (39, 1), (career.StatusWord( 0, 6 ), career.StatusWord( 0, 0 )), "within day 1 the guests are not looked at again" );
		Run( controller, TurnAtDays( 2 ), TurnAtDays( 2 ) + 4 );
		Assert.AreEqual( (45, 2), (career.StatusWord( 0, 6 ), career.StatusWord( 0, 0 )), "day 2's 45 guests reach 40: achieved" );
		host.GuestsInPark = 0;
		Run( controller, TurnAtDays( 3 ), TurnAtDays( 3 ) + 4 );
		Assert.AreEqual( (45, 2), (career.StatusWord( 0, 6 ), career.StatusWord( 0, 0 )), "an achieved statistic is not measured again" );
	}

	[TestMethod]
	public void APercentageTargetAndADeadlineAreTestedAsTheOriginalDoes()
	{
		var host = new TestMissionHost { GuestsInPark = 30 };
		var (controller, career, _) = Controller( Statistic( 0, 5, 50, time: 2 ) + Statistic( 1, 2, 1000, time: 2 ), host );
		host.GuestsInPark = 44;
		Run( controller, 1, TurnAtDays( 1 ) + 4 );
		Assert.AreEqual( 1, career.StatusWord( 0, 0 ), "before the deadline nothing is decided" );
		Run( controller, TurnAtDays( 1 ) + 5, TurnAtDays( 2 ) + 4 );
		Assert.AreEqual( (3, 3), (career.StatusWord( 0, 0 ), career.StatusWord( 1, 0 )), "44 is below 30 raised by 50% (45), and the balance is short: both fail at the deadline" );

		var (relative, relativeCareer, relativeHost) = Controller( Statistic( 0, 5, 50 ), new TestMissionHost { GuestsInPark = 30 } );
		relativeHost.GuestsInPark = 45;
		Run( relative, 1, TurnAtDays( 1 ) + 4 );
		Assert.AreEqual( 2, relativeCareer.StatusWord( 0, 0 ), "(50 + 100) × 30 / 100 = 45" );
	}

	[TestMethod]
	public void AnUnmeasurableStatisticIsNeverStoredOrDecided()
	{
		var (controller, career, _) = Controller( Statistic( 0, 6, 6 ) + Statistic( 1, 7, 51, time: 1 ) );
		Assert.AreEqual( (1, 1), (career.StatusWord( 0, 0 ), career.StatusWord( 1, 0 )), "awards and shares start with a zero baseline" );
		Run( controller, 1, TurnAtDays( 3 ) );
		Assert.AreEqual( (1, 1, 0, 0), (career.StatusWord( 0, 0 ), career.StatusWord( 1, 0 ), career.StatusWord( 0, 7 ), career.StatusWord( 1, 7 )) );
		Assert.IsFalse( controller.IsMissionComplete() );
	}

	[TestMethod]
	public void TheMissionCompletesOnItsStatisticsChallengesRankObstacleAndTickets()
	{
		var host = new TestMissionHost { GuestsInPark = 50 };
		var (statistics, career, _) = Controller( Statistic( 0, 8, 40 ) + "Completion.EndingTheme 1\n", host );
		Assert.IsFalse( statistics.IsMissionComplete(), "running, not achieved" );
		Run( statistics, 1, TurnAtDays( 1 ) + 4 );
		Assert.IsTrue( statistics.IsMissionComplete() && statistics.MetRequirements() );

		var (challenges, challengeCareer, _) = Controller( "RequiredChallenges.Theme 2\nRequiredChallenges.Number 2\n" );
		challengeCareer.ZoneRecord( 1 )!.Data.ChallengesDone = 5;
		challengeCareer.ZoneRecord( 2 )!.Data.ChallengesDone = 1;
		Assert.IsFalse( challenges.IsMissionComplete(), "the wins are counted in the required zone (water) only" );
		challengeCareer.ZoneRecord( 2 )!.Data.ChallengesDone = 2;
		Assert.IsTrue( challenges.IsMissionComplete() );

		var (rank, rankCareer, _) = Controller( "Promotion.Number 2\n" );
		Assert.IsFalse( rank.IsMissionComplete(), "rank 1 is below the required 2" );
		rankCareer.Promote( 2 );
		Assert.IsTrue( rank.IsMissionComplete() );

		var (tickets, ticketCareer, _) = Controller( "Obstacle.GoldenTickets 3\n" );
		ticketCareer.AddGoldenTickets( 2 );
		Assert.IsFalse( tickets.IsMissionComplete() );
		ticketCareer.AddGoldenTickets( 1 );
		Assert.IsTrue( tickets.IsMissionComplete() );

		var obstacleHost = new TestMissionHost { ObstacleStaffSlotsFilled = true };
		var (obstacle, obstacleCareer, _) = Controller( "Obstacle.Type 1703\nObstacle.GoldenTickets 1\nCompletion.EndingTheme 1\n", obstacleHost );
		Assert.AreEqual( 1, obstacle.ObstacleState() );
		Assert.IsFalse( obstacle.IsMissionComplete() || obstacle.MetRequirements(), "not researched" );
		obstacleCareer.DiscoverObstacleAbilities();
		obstacleCareer.AddGoldenTickets( 1 );
		Assert.AreEqual( 2, obstacle.ObstacleState() );
		Assert.IsTrue( obstacle.MetRequirements(), "researched, slots filled, tickets: the remove button is enabled" );
		Assert.IsFalse( obstacle.IsMissionComplete(), "but the mission is complete only once the obstacle is removed" );
		obstacleHost.ObstacleStaffSlotsFilled = null;
		Assert.IsFalse( obstacle.MetRequirements(), "unmodelled staff slots are not filled" );
		obstacleCareer.MarkObstacleRemoved();
		Assert.AreEqual( 4, obstacle.ObstacleState() );
		Assert.IsTrue( obstacle.IsMissionComplete() );

		var (noResearch, _, _) = Controller( "Obstacle.Type 1700\nInfo.NoResearch 1\n" );
		Assert.AreEqual( 0, noResearch.ObstacleState(), "a mission without research has no obstacle to remove" );
		Assert.IsTrue( noResearch.IsMissionComplete() );
		obstacleHost.Automatic.Add( 1703 );
		var (automatic, _, _) = Controller( "Obstacle.Type 1703\nCompletion.EndingTheme 1\n", obstacleHost );
		Assert.AreEqual( 3, automatic.ObstacleState(), "an obstacle that opens automatically in the ending zone" );
	}

	[TestMethod]
	public void TheWrongZoneRefusesTheCompletion()
	{
		var host = new TestMissionHost();
		host.Attractions[1700] = 42;
		var (controller, career, _) = Controller( "Completion.EndingTheme 2\nCompletion.ObstacleOpened 1700\n", host );
		Assert.IsFalse( controller.MetRequirements(), "the mission ends in water; the career is in science" );
		Assert.IsFalse( controller.OnMissionComplete( 0 ) );
		Assert.AreEqual( (ThemeParkIncMissionState.Idle, 0, 0), (controller.State, host.Cameras.Count, career.MissionCounter) );
		var (anywhere, _, anywhereHost) = Controller( "Completion.EndingTheme 3\n" );
		Assert.IsTrue( anywhere.MetRequirements() && anywhere.OnMissionComplete( 0 ), "EndingTheme 3 ends anywhere" );
		Assert.AreEqual( 0, anywhereHost.Cameras.Count );
	}

	[TestMethod]
	public void AnOpenedObstacleFliesTheCameraAndItsEndOpensItAndAdvances()
	{
		var host = new TestMissionHost { BankBalance = 900, GuestsInPark = 3 };
		host.Attractions[1700] = 42;
		host.Attractions[1703] = 77;
		var (controller, career, _) = Controller( "Completion.ObstacleOpened 1700\nCompletion.LoadNewHoardings 1\nCompletion.EndingTheme 1\nObstacle.GoldenTickets 2\n"
			+ "Obstacle.Type 1700\nInfo.NoResearch 1\n", host,
			miss01: "Info.Number 1\nObstacle.Type 1703\n" + Statistic( 0, 2, 5000 ) );
		career.AddGoldenTickets( 5 );
		Assert.IsTrue( controller.OnMissionComplete( 10 ) );
		Assert.AreEqual( (ThemeParkIncMissionState.ObstacleOpened, (ushort)42), (controller.State, controller.CutsceneObject) );
		Assert.AreEqual( new ThemeParkIncCameraRequest( 42, 2, 0 ), host.Cameras.Single() );
		Assert.AreEqual( 0, career.MissionCounter, "the mission completes only at the camera's end" );
		controller.CameraFinished( 20 );
		CollectionAssert.AreEqual( new[] { "open 42", "extent 0", "find 1703" }, host.Calls, "the obstacle opens, extent.000 (the old counter) loads, the next obstacle is found" );
		Assert.AreEqual( (1, 3, (ushort)77), (career.MissionCounter, career.GoldenTickets, host.CurrentObstacle), "the counter advances and two tickets are spent" );
		Assert.AreEqual( (1, 900, ThemeParkIncChallenges.Now( 20 )), (career.StatusWord( 0, 0 ), career.StatusWord( 0, 9 ), career.StatusStart( 0 )), "the new mission's statistics start" );
		Assert.AreEqual( (ThemeParkIncMissionState.Idle, (ushort)0), (controller.State, controller.CutsceneObject) );
		Assert.IsFalse( career.ObjectiveLetterPosted );
		controller.Update( 21 );
		Assert.AreEqual( "letter 0", host.Calls.Last(), "the next update posts the objective letter of mission 0" );
		Assert.IsTrue( career.ObjectiveLetterPosted );
		controller.Update( 22 );
		Assert.AreEqual( 1, host.Calls.Count( call => call.StartsWith( "letter", StringComparison.Ordinal ) ), "once" );
	}

	[TestMethod]
	public void WithoutTheShownThingTheMissionCompletesAtOnceAndACutsceneThingIsDestroyed()
	{
		var (missing, career, host) = Controller( "Completion.ObstacleOpened 1700\nCompletion.ThemeOpened 1\nCompletion.ThemeNumber 2\nCompletion.EndingTheme 1\n" );
		Assert.IsFalse( career.ZoneRecord( 2 )!.Available, "water's record exists and is closed" );
		Assert.IsTrue( missing.OnMissionComplete( 0 ) );
		Assert.AreEqual( (0, 1, true), (host.Cameras.Count, career.MissionCounter, career.ZoneRecord( 2 )!.Available), "no 1700 thing: the tick runs at once and opens water" );

		var cutsceneHost = new TestMissionHost();
		cutsceneHost.Attractions[1798] = 9;
		var (cutscene, cutsceneCareer, _) = Controller( "Completion.CutsceneObject 1798\nCompletion.EndingTheme 1\n", cutsceneHost );
		Assert.IsTrue( cutscene.OnMissionComplete( 0 ) );
		Assert.AreEqual( (ThemeParkIncMissionState.CutsceneObject, (ushort)0), (cutscene.State, cutscene.CutsceneObject), "state 4 does not record the thing" );
		cutscene.CameraFinished( 1 );
		Assert.AreEqual( ("destroy 0", 1), (cutsceneHost.Calls[0], cutsceneCareer.MissionCounter), "the end destroys the recorded thing (none) and completes" );
	}

	[TestMethod]
	public void TheObstacleMessagesDriveTheStateMachine()
	{
		var host = new TestMissionHost { CurrentObstacle = 55, ObstacleStaffSlotsFilled = true };
		var (controller, career, _) = Controller( "Obstacle.Type 1703\nObstacleStaff[0].Ability 10\nObstacleStaff[0].Number 1\nCompletion.EndingTheme 1\n", host );
		Assert.IsTrue( controller.RegisterObstacleResearch );
		controller.Update( 1 );
		Assert.IsFalse( controller.RegisterObstacleResearch );
		Assert.AreEqual( "research", host.Calls.Single() );
		controller.ObstacleResearched();
		Assert.IsTrue( controller.ObstacleResearchComplete );
		controller.Update( 2 );
		Assert.AreEqual( (ThemeParkIncMissionState.ObstacleResearched, (byte)1, true), (controller.State, career.Abilities[10], career.ObstacleResearched) );
		Assert.AreEqual( new ThemeParkIncCameraRequest( 55, 1, 0 ), host.Cameras.Single() );
		controller.CameraFinished( 3 );
		Assert.AreEqual( (ThemeParkIncMissionState.Idle, 0), (controller.State, career.MissionCounter), "the researched camera's end only resets" );
		Assert.IsTrue( controller.MetRequirements() );

		controller.RemoveObstacle( 55 );
		Assert.AreEqual( (ThemeParkIncMissionState.ObstacleRemoved, (ushort)55, true), (controller.State, controller.CutsceneObject, career.ObstacleRemoved) );
		Assert.AreEqual( new ThemeParkIncCameraRequest( 55, 2, 0 ), host.Cameras[1] );
		controller.CameraFinished( 4 );
		Assert.AreEqual( "open 55", host.Calls[1] );
		Assert.AreEqual( (1, false), (career.MissionCounter, career.ObstacleRemoved), "removed and advanced" );

		controller.Promotion( 3 );
		Assert.AreEqual( 3, career.Rank );
		var broadcasts = host.Broadcasts;
		controller.ChallengeWon();
		Assert.AreEqual( broadcasts + 1, host.Broadcasts, "a won challenge only broadcasts the status" );
	}

	[TestMethod]
	public void AFinishedGameStopsTheController()
	{
		var (controller, career, host) = Controller( "Completion.EndOfGame 1\nCompletion.LoadNewHoardings 1\n" );
		Assert.AreEqual( new ThemeParkIncCompletion( 0, false, true ), controller.CompleteMission( 1 ) );
		Assert.IsTrue( career.GameFinished );
		Assert.AreEqual( "extent 0", host.Calls.Single() );
		Assert.AreEqual( ThemeParkIncCompletion.None, controller.CompleteMission( 2 ) );
		controller.Update( 4 );
		Assert.IsFalse( controller.IsMissionComplete() );
		Assert.AreEqual( 1, host.Calls.Count, "no research, no letter, no statistics" );
	}

	[TestMethod]
	public void TheControllerSavesItsStateAndTheLoadReRegistersTheResearch()
	{
		var economy = ThemeParkIncChallengeTests.Economy();
		var missions = economy.TpiMissions!;
		Assert.IsNotNull( missions );
		missions.RemoveObstacle( 31 );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiMissions\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( (ThemeParkIncMissionState.ObstacleRemoved, (ushort)31, true, false),
			(restored.TpiMissions!.State, restored.TpiMissions.CutsceneObject, restored.TpiMissions.RegisterObstacleResearch, restored.TpiMissions.ObstacleResearchComplete) );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );

		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node["TpiMissions"]!["State"] = 9;
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
		node.Remove( "TpiMissions" );
		var older = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( ThemeParkIncMissionState.Idle, older.TpiMissions!.State, "a save from before the controller continues with it idle" );
		Assert.IsFalse( ParkSaveFile.Serialize( new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7 ) )
			.Contains( "TpiMissions", StringComparison.Ordinal ), "a Theme Park World save writes no controller" );
	}

	[TestMethod]
	public void AnAttachedCareerKeepsTheParksControllerFields()
	{
		var economy = ThemeParkIncChallengeTests.Economy();
		economy.TpiMissions!.RemoveObstacle( 12 );
		var career = new ThemeParkIncCareer( new TestCareerFiles(), TestCareerFiles.ShippedFiringLines );
		economy.AttachThemeParkIncCareer( career );
		Assert.AreSame( career, economy.TpiMissions!.Career );
		Assert.AreEqual( (ThemeParkIncMissionState.ObstacleRemoved, (ushort)12, true), (economy.TpiMissions.State, economy.TpiMissions.CutsceneObject, economy.TpiMissions.RegisterObstacleResearch),
			"Load A Game: the player file's career replaces the park save's, the park's controller keeps its OBJH fields" );
	}

	[TestMethod]
	public void TheEconomyRunsTheControllerAndRoutesThePromotion()
	{
		var economy = ThemeParkIncChallengeTests.Economy();
		Assert.IsTrue( economy.TpiMissions!.RegisterObstacleResearch );
		economy.Advance( ParkCalendar.TickOfTurn( 8 ) - economy.Tick );
		Assert.IsFalse( economy.TpiMissions.RegisterObstacleResearch, "the controller updates in the thing pass" );
		Assert.IsFalse( economy.ThemeParkIncMissionMet, "a headless economy has no mission file" );
	}
}
