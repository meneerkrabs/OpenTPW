using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's camera flight (the cutscene thing, docs/reverse/TPI-MISSIONS.md §5) over the shipped Science camera path and obstacles: the start, the script's own end, the skip, the clock rate,
/// message 0x29, the zone arrival's flyover and mission 0 from its window's button to mission 1. The obstacle things run headless runtimes. Needs OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncCutsceneTests
{
	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	/// <summary>The prebuilt Science park's obstacle things as headless runtimes, by their saved thing ids; the views the flights ask for.</summary>
	private sealed class Flights : IThemeParkIncCutsceneWorld
	{
		public Dictionary<ushort, OriginalObjectRuntime> Things { get; } = new();
		public List<string> Views { get; } = new();
		public RideVM? ScriptOf( ushort thing ) => Things.TryGetValue( thing, out var runtime ) ? runtime.Script : null;
		public int? TypeOf( ushort thing ) => Things.TryGetValue( thing, out var runtime ) ? runtime.Entry.InfoId : null;
		public void EnterView( ushort thing ) => Views.Add( $"enter {thing}" );
		public void LeaveView() => Views.Add( "leave" );
		public void Simulate( double seconds )
		{
			foreach ( var runtime in Things.Values )
				runtime.Simulate( seconds );
		}
	}

	private static Flights ScienceFlights()
	{
		ThemeParkIncObstacleTests.UseTpiData();
		var save = OriginalPark.Load( "science" ).Save!.ThemeParkIncWorld!;
		var catalog = ObjectCatalog.Load( "science" );
		var scripts = new RideScriptWorld();
		var flights = new Flights();
		foreach ( var attraction in save.Attractions.Where( attraction => attraction.InfoId is 1700 or 1703 or 1798 ) )
			flights.Things[(ushort)attraction.Id] = new OriginalObjectRuntime( catalog.Get( attraction.InfoId ), scripts, open: true );
		return flights;
	}

	private const double Step = 1.0 / 60;

	/// <summary>Runs the things' scripts and the cutscene thing's update every <paramref name="updateEvery"/> steps until the flight ends or <paramref name="seconds"/> pass; answers the seconds run.</summary>
	private static double FlyUntilEnd( ThemeParkIncCutscene cutscene, Flights flights, double seconds, int updateEvery = 15 )
	{
		var elapsed = 0.0;
		for ( var step = 1; cutscene.Flying && elapsed < seconds; step++ )
		{
			flights.Simulate( Step );
			elapsed += Step;
			if ( step % updateEvery == 0 )
				cutscene.Update();
		}
		return elapsed;
	}

	[TestMethod]
	public void TheCameraPathFliesItsClipAndEndsWithMessageZero()
	{
		var flights = ScienceFlights();
		var ends = new List<ThemeParkIncCameraEnd>();
		var arrivals = 0;
		var cutscene = new ThemeParkIncCutscene( ends.Add ) { World = flights, ArrivalEnded = () => arrivals++, ViewedZone = () => 1 };
		Assert.IsTrue( cutscene.Play( 32, 1, ThemeParkIncCutscene.ArrivalMode ) );
		Assert.AreEqual( 1, flights.ScriptOf( 32 )!.Variables[0], "the start writes the trigger into variable 0" );
		Assert.AreEqual( (true, true, 1.0, true), (cutscene.Flying, cutscene.Skippable, cutscene.ClockRate!.Value, cutscene.OverlayOpen) );
		CollectionAssert.AreEqual( new[] { "enter 32" }, flights.Views );
		Assert.IsFalse( cutscene.Play( 29, 2, 0 ), "a second flight is refused while one plays" );

		var seconds = FlyUntilEnd( cutscene, flights, 30 );
		Assert.IsFalse( cutscene.Flying );
		// cameram.MD2 lasts 500 ticks (30 a second): WAITANIM waits (16 667 - 300) ms, then the update that sees variable 1 ends the flight
		Assert.IsTrue( seconds is > 16.0 and < 17.2, $"the flight lasted {seconds:F2} s" );
		Assert.AreEqual( new ThemeParkIncCameraEnd( 32, 0, ThemeParkIncCutscene.ArrivalMode ), ends.Single(), "the script cleared variable 0 with variable 1: message 0x29 carries 0" );
		Assert.AreEqual( (0, 0), (flights.ScriptOf( 32 )!.Variables[0], flights.ScriptOf( 32 )!.Variables[1]), "the end clears both variables" );
		CollectionAssert.AreEqual( new[] { "enter 32", "leave" }, flights.Views );
		Assert.IsNull( cutscene.ClockRate, "the clock is back at the player's speed" );
		Assert.AreEqual( 1, arrivals, "a mode-2 end runs the controller's arrival briefing" );
		Assert.AreEqual( (2, 0), (cutscene.WindowCountdown, cutscene.MissionWindowRequests), "the countdown is 1 at the end and 2 after the same update" );
		cutscene.Update();
		Assert.AreEqual( (0, 1), (cutscene.WindowCountdown, cutscene.MissionWindowRequests), "the mission window opens one update after the end" );
		cutscene.Update();
		Assert.AreEqual( 1, cutscene.MissionWindowRequests );
	}

	[TestMethod]
	public void ASkipEndsTheArrivalAndCarriesTheTrigger()
	{
		var flights = ScienceFlights();
		var ends = new List<ThemeParkIncCameraEnd>();
		var cutscene = new ThemeParkIncCutscene( ends.Add ) { World = flights };
		Assert.IsTrue( cutscene.Play( 32, 1, ThemeParkIncCutscene.ArrivalMode ) );
		FlyUntilEnd( cutscene, flights, 2 );
		cutscene.RequestSkip();
		Assert.IsTrue( cutscene.Flying, "a skip ends the flight on the next update" );
		cutscene.Update();
		Assert.AreEqual( new ThemeParkIncCameraEnd( 32, 1, ThemeParkIncCutscene.ArrivalMode ), ends.Single(), "after a skip 0x29 carries the trigger" );
		// the script plays on and sets variable 1 later; nothing flies, so no second end comes
		for ( var step = 0; step < 60 * 20; step++ )
		{
			flights.Simulate( Step );
			if ( step % 15 == 0 )
				cutscene.Update();
		}
		Assert.AreEqual( 1, ends.Count );

		var unskippable = new ThemeParkIncCutscene( ends.Add ) { World = flights };
		Assert.IsTrue( unskippable.Play( 29, 2, 0 ) );
		Assert.IsFalse( unskippable.Skippable );
		unskippable.RequestSkip();
		unskippable.Update();
		Assert.IsTrue( unskippable.Flying, "a mode-0 flight takes no skip" );
		unskippable.ForceSkip();
		unskippable.Update();
		Assert.IsFalse( unskippable.Flying, "the intro window's end works in any mode" );
		Assert.AreEqual( new ThemeParkIncCameraEnd( 29, 2, 0 ), ends.Last() );
		Assert.AreEqual( 0, unskippable.MissionWindowRequests, "only a mode-2 end counts down to the mission window" );
	}

	[TestMethod]
	public void TheClockRateFollowsTheModeAndTheLakeAsksForItsPanel()
	{
		var flights = ScienceFlights();
		var cutscene = new ThemeParkIncCutscene( _ => { } ) { World = flights };
		Assert.IsTrue( cutscene.Play( 30, 1, ThemeParkIncCutscene.IntroMode ) );
		Assert.AreEqual( (double)0.7f, cutscene.ClockRate );
		Assert.IsFalse( cutscene.Skippable );
		CollectionAssert.AreEqual( new[] { 0x26D }, cutscene.PanelRequests, "the 1703 lake's start opens panel 0x26D" );
		cutscene.ForceSkip();
		cutscene.Update();
		Assert.IsNull( cutscene.ClockRate );
		Assert.IsTrue( cutscene.Play( 29, 1, 0 ) );
		Assert.AreEqual( 1.0, cutscene.ClockRate );
		Assert.AreEqual( 1, cutscene.PanelRequests.Count );
		var headless = new ThemeParkIncCutscene( _ => { } );
		Assert.IsTrue( headless.Play( 5, 1, 0 ), "without a world the flight keeps its state" );
		headless.Update();
		Assert.IsTrue( headless.Flying, "a thing without a script never ends its flight by itself" );
	}

	[TestMethod]
	public void TheArrivalFliesTheZonesCameraPathOnTheFirstDayOnly()
	{
		var played = new List<(ushort, int)>();
		ushort Find( int type ) => type switch { 1798 => 32, 2798 => 40, 3798 => 41, 4798 => 42, _ => 0 };
		Assert.AreEqual( (int?)3798, ThemeParkIncCutscene.ArrivalCameraType( 0 ) );
		Assert.AreEqual( (int?)1798, ThemeParkIncCutscene.ArrivalCameraType( 1 ) );
		Assert.AreEqual( (int?)2798, ThemeParkIncCutscene.ArrivalCameraType( 2 ) );
		Assert.AreEqual( (int?)4798, ThemeParkIncCutscene.ArrivalCameraType( 3 ) );
		var cutscene = new ThemeParkIncCutscene( _ => { } );
		Assert.IsFalse( cutscene.PlayArrival( 1, 1, Find ), "a park past its first day has no flyover" );
		Assert.IsTrue( cutscene.PlayArrival( 1, 0, Find ) );
		Assert.AreEqual( ((ushort)32, ThemeParkIncCutscene.ArrivalMode), (cutscene.AnimThing, cutscene.Mode) );
		var intro = new ThemeParkIncCutscene( _ => { } );
		Assert.IsFalse( intro.PlayArrival( 3, 0, Find, introPlayed: true ) );
		Assert.IsTrue( intro.PlayArrival( 3, 5, Find ), "the intro does not test the day" );
		Assert.AreEqual( ((ushort)42, ThemeParkIncCutscene.IntroMode), (intro.AnimThing, intro.Mode) );
		Assert.IsFalse( new ThemeParkIncCutscene( _ => { } ).PlayArrival( 1, 0, _ => 0 ), "no camera path, no flight" );
	}

	/// <summary>The prebuilt's attraction things and land as the mission controller's world, opening its obstacles on the cells.</summary>
	private sealed class PrebuiltWorld( ThemeParkIncSaveWorld save, ParkCellMap cells, IReadOnlyDictionary<int, ThemeParkIncObstacleType> types ) : IThemeParkIncMissionWorld
	{
		private readonly ThemeParkIncObstacleTests.PrebuiltThings things = new( save );
		public ushort CurrentObstacle { get; private set; } = save.Variables.CurrentObstacle;
		public List<(ushort Thing, ThemeParkIncObstacleFate Fate)> Opened { get; } = new();
		public void FindCurrentObstacle( int type )
		{
			if ( type == 0 )
				CurrentObstacle = 0;
			foreach ( var (thing, live) in things.Live.OrderBy( pair => pair.Key ) )
				if ( live == type )
					CurrentObstacle = thing;
		}
		public ushort FindAttraction( int type ) => things.Live.Where( pair => pair.Value == type ).Select( pair => pair.Key ).DefaultIfEmpty().Max();
		public bool OpensAutomatically( int type ) => types.TryGetValue( type, out var obstacle ) && obstacle.OpenedAutomatically;
		public void OpenObstacle( ushort thing ) => Opened.Add( (thing, ThemeParkIncObstacles.OpenThing( cells, types, things, thing )) );
		public void DestroyThing( ushort thing ) => things.Destroy( thing );
		public void LoadExtent( int index ) => Assert.IsTrue( ThemeParkIncLand.ApplyExtent( cells, index ) );
	}

	[TestMethod]
	public void MissionZeroFliesToTheGateOpensItAndAdvancesAtTheFlightsEnd()
	{
		var flights = ScienceFlights();
		var park = OriginalPark.Load( "science" );
		var save = park.Save!.ThemeParkIncWorld!;
		var cells = Level.OriginalCellMap( park, GuestSettings.Load( "science" ), null );
		var economy = ParkEconomyRuntime.ForThemeParkIncLevel( park ).Economy;
		var world = new PrebuiltWorld( save, cells, ThemeParkIncObstacleTypes.Load( FileSystem, "science" ) );
		economy.TpiMissionWorld = world;
		economy.TpiCutsceneWorld = flights;
		var cutscene = economy.TpiCutscene!;
		var career = economy.TpiCareer!;
		var missions = economy.TpiMissions!;
		void Run( double seconds )
		{
			for ( var elapsed = 0.0; elapsed < seconds; elapsed += Step )
			{
				flights.Simulate( Step );
				economy.Advance( 1 );
			}
		}

		// a new game's prebuilt park is on its first day: the Science camera path flies, skippable, and its end posts 0x29 and the arrival briefing
		Assert.IsTrue( economy.StartThemeParkIncArrival() );
		Assert.AreEqual( ((ushort)32, ThemeParkIncCutscene.ArrivalMode), (cutscene.AnimThing, cutscene.Mode) );
		Run( 18 );
		Assert.IsFalse( cutscene.Flying );
		Assert.AreEqual( new ThemeParkIncCameraEnd( 32, 0, ThemeParkIncCutscene.ArrivalMode ), economy.TpiCameraEnds.Single() );
		Assert.AreEqual( (1, true), (economy.TpiIntroductionLetters, career.ObjectiveLetterPosted), "mission 0's first briefing (0x5F1760)" );
		Assert.AreEqual( 1, cutscene.MissionWindowRequests, "the mission window opens one update after the flyover's end" );
		Assert.AreEqual( ThemeParkIncMissionState.Idle, missions.State, "a camera end in state 0 only resets" );

		// the window's complete button (the mission window calls it), refused until the requirements are met: miss00's 1700 gate
		Assert.IsFalse( economy.PressThemeParkIncMissionComplete() );
		economy.GuestStatistics = new GuestCensus( 40 );
		Run( 12 );
		Assert.IsTrue( economy.ThemeParkIncMissionMet );
		Assert.IsTrue( economy.PressThemeParkIncMissionComplete(), "a mission without an obstacle runs OnMissionComplete" );
		Assert.AreEqual( ((ushort)29, 2, 0), (cutscene.AnimThing, flights.ScriptOf( 29 )!.Variables[0], cutscene.Mode), "the camera flies the gate's clip 2 in mode 0" );
		Assert.AreEqual( 1.0, cutscene.ClockRate );
		Assert.AreEqual( 0, career.MissionCounter, "nothing completes before the flight's end" );
		Run( 40 );
		Assert.IsFalse( cutscene.Flying, "the gate's script ends its own flight" );
		Assert.AreEqual( new ThemeParkIncCameraEnd( 29, 0, 0 ), economy.TpiCameraEnds.Last() );
		Assert.AreEqual( ((ushort)29, ThemeParkIncObstacleFate.Stays), world.Opened.Single(), "message 0x29 opens the gate, which stays" );
		Assert.AreEqual( (byte)5, cells.RawTypeAt( 41, 49 ), "the gate's AfterShape is on the cells" );
		Assert.AreEqual( (1, 1703), (career.MissionCounter, career.Mission.ObstacleType), "mission 1: the lake" );
		Assert.AreEqual( 1, cutscene.MissionWindowRequests, "a mode-0 end opens no mission window" );
	}

	[TestMethod]
	public void TheRemovedLakeFliesItsDrainAndBecomesTheDrainedLakeAtTheFlightsEnd()
	{
		var flights = ScienceFlights();
		var park = OriginalPark.Load( "science" );
		var save = park.Save!.ThemeParkIncWorld!;
		var cells = Level.OriginalCellMap( park, GuestSettings.Load( "science" ), null );
		var economy = ParkEconomyRuntime.ForThemeParkIncLevel( park ).Economy;
		var world = new PrebuiltWorld( save, cells, ThemeParkIncObstacleTypes.Load( FileSystem, "science" ) );
		economy.TpiMissionWorld = world;
		economy.TpiCutsceneWorld = flights;
		var cutscene = economy.TpiCutscene!;
		var missions = economy.TpiMissions!;
		var career = economy.TpiCareer!;
		// mission 1 (the lake) as the cheats' Skip Mission reaches it: the completion tick advances and finds the lake as the current obstacle
		missions.CompleteMission( economy.Turn );
		Assert.AreEqual( (1, 1703, (ushort)30), (career.MissionCounter, career.Mission.ObstacleType, world.CurrentObstacle) );
		var lake = ThemeParkIncObstacleTypes.Load( FileSystem, "science" )[1703];
		var (column, row) = Enumerable.Range( 0, lake.Shape!.Height ).SelectMany( r => Enumerable.Range( 0, lake.Shape.Width ).Select( c => (c, r) ) )
			.First( cell => lake.Shape[cell.c, cell.r].Value == 8 && lake.AfterShape![cell.c, cell.r].Value != ThemeParkIncMapGrid.Untouched );
		var lakeCell = (X: lake.ShapeOffsetX + column, Y: lake.ShapeOffsetY + row);
		var drained = (byte)lake.AfterShape![column, row].Value;
		Assert.IsTrue( cells.ObstacleFlagAt( lakeCell.X, lakeCell.Y ) && cells.RawTypeAt( lakeCell.X, lakeCell.Y ) == 8, "the lake's water is a hole with the obstacle flag" );

		// message 0x24 (the remove button or the window's complete button): state 2, the camera on the lake with its clip 2, panel 0x26D
		missions.RemoveObstacle( world.CurrentObstacle );
		Assert.AreEqual( (ThemeParkIncMissionState.ObstacleRemoved, (ushort)30, 2), (missions.State, cutscene.AnimThing, flights.ScriptOf( 30 )!.Variables[0]) );
		CollectionAssert.AreEqual( new[] { 0x26D }, cutscene.PanelRequests );
		for ( var elapsed = 0.0; cutscene.Flying && elapsed < 60; elapsed += Step )
		{
			flights.Simulate( Step );
			economy.Advance( 1 );
		}
		Assert.IsFalse( cutscene.Flying, "the lake's script ends its flight" );
		Assert.AreEqual( new ThemeParkIncCameraEnd( 30, 0, 0 ), economy.TpiCameraEnds.Last() );
		Assert.AreEqual( ((ushort)30, ThemeParkIncObstacleFate.Replaced), world.Opened.Single(), "the lake is replaced" );
		Assert.AreEqual( (ushort)0, world.FindAttraction( 1703 ) );
		Assert.AreNotEqual( (ushort)0, world.FindAttraction( 1704 ), "the drained lake stands in the world" );
		Assert.IsFalse( cells.ObstacleFlagAt( lakeCell.X, lakeCell.Y ) );
		Assert.AreEqual( drained, cells.RawTypeAt( lakeCell.X, lakeCell.Y ), "the drained water takes its AfterShape value" );
		Assert.AreEqual( (ushort)0, cells.OwnerAt( lakeCell.X, lakeCell.Y ) );
		Assert.AreEqual( 2, career.MissionCounter, "the completion tick ran at the flight's end" );
	}

	private sealed class GuestCensus( int census ) : IParkGuestStatistics
	{
		public int Census { get; set; } = census;
		public int PeopleInPark => Census;
		public float AverageHappiness => 0;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	[TestMethod]
	public void AFlightInTheSaveComesBackWithoutItsViewAndEndsByItsScript()
	{
		var economy = ThemeParkIncChallengeTests.Economy();
		var cutscene = economy.TpiCutscene!;
		Assert.IsTrue( cutscene.Play( 32, 1, ThemeParkIncCutscene.ArrivalMode ) );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiCutscene\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( ((ushort)32, 0, false, false, (double?)null), (restored.TpiCutscene!.AnimThing, restored.TpiCutscene.Mode, restored.TpiCutscene.Skippable, restored.TpiCutscene.OverlayOpen, restored.TpiCutscene.ClockRate),
			"only the thing is saved (0x5D0160)" );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node.Remove( "TpiCutscene" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ) );
		Assert.AreEqual( ParkSaveFile.CutsceneMissing, older.Message, "a save from before the flight (and the obstacles) is refused by its validation, before any asset comparison" );
		Assert.IsFalse( ParkSaveFile.Serialize( new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7 ) )
			.Contains( "TpiCutscene", StringComparison.Ordinal ), "a Theme Park World save writes no cutscene" );

		// a restored flight with a script ends when the script sets variable 1, without a view to leave: the camera path's script runs its clip from the trigger its saved state holds in variable 0
		var flights = ScienceFlights();
		var ends = new List<ThemeParkIncCameraEnd>();
		var loaded = new ThemeParkIncCutscene( ends.Add ) { World = flights };
		loaded.Restore( new ThemeParkIncCutsceneSnapshot( 32 ) );
		flights.ScriptOf( 32 )!.Variables[0] = 1;
		loaded.Update();
		Assert.AreEqual( (true, false, false, (double?)null), (loaded.Flying, loaded.Skippable, loaded.SkipPending, loaded.ClockRate), "the restored flight waits for its script" );
		var seconds = FlyUntilEnd( loaded, flights, 30 );
		Assert.IsFalse( loaded.Flying );
		Assert.IsTrue( seconds is > 16.0 and < 17.2, $"the script's own end came after its clip ({seconds:F2} s)" );
		Assert.AreEqual( new ThemeParkIncCameraEnd( 32, 0, 0 ), ends.Single(), "the script cleared variable 0 with variable 1; the restored mode is 0" );
		Assert.AreEqual( 0, flights.Views.Count, "no input window: the end leaves no view" );
	}
}
