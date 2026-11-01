using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The leaving guests' last walk (was ARRIVAL-001, retired): at the gate's crossing cell (state 19, tp.exe <c>0x00500AD0</c>) a guest waits until the arrival vehicle lets it
/// walk, picks one of the four cells around the stop with world draws and walks there over the cell map (state 20), then waits (state 21, <c>0x00500BD0</c>) and boards when the
/// vehicle reports status 4 and it heads its cell's thing list.
/// </summary>
[TestClass]
public class StopWalkTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	private sealed class Vehicles : IArrivalVehicleHost
	{
		public readonly bool[] Placed = new bool[3];
		public readonly int[][] Variables = { new int[2], new int[2], new int[2] };
		public bool Exists( int kind ) => Placed[kind];
		public bool Create( int kind ) => Placed[kind] = true;
		public int GetVariable( int kind, int index ) => Placed[kind] ? Variables[kind][index] : 0;
		public void SetVariable( int kind, int index, int value ) => Variables[kind][index] = value;
	}

	/// <summary>The gate rig of <see cref="WholeWalkTests"/>: lane A at x = 5 with the bus stop at (5, 3), a two-cell-wide walkway strip from y = 1 to y = 9, the park path along y = 10.</summary>
	private static (GuestSimulation Simulation, GuestPathGrid Grid, Vehicles Vehicles) Rig( bool withVehicles = true )
	{
		var grid = new GuestPathGrid( 30, 14 );
		for ( var x = 0; x < 25; x++ )
			grid.SetPath( x, 10, true );
		var settings = GuestTests.CreateSettings();
		settings.ArrivalLaneA = new[] { (5, 3), (5, 5), (5, 8), (5, 9), (5, 10) };
		settings.ArrivalLaneB = new[] { (18, 3), (18, 5), (18, 8), (18, 9), (18, 10) };
		foreach ( var x in new[] { 5, 6, 18, 19 } )
			for ( var y = 1; y <= 9; y++ )
				grid.Cells.SetType( x, y, ParkCellTypes.FixedWalkway );
		var vehicles = new Vehicles();
		var simulation = new GuestSimulation( grid, settings, 3 ) { ArrivalsEnabled = false, ArrivalVehicles = withVehicles ? vehicles : null };
		return (simulation, grid, vehicles);
	}

	private static Guest AtTheGate( GuestSimulation simulation )
	{
		var guest = simulation.SpawnInPark( 5, 10 );
		guest.ExitLevel = 0;
		guest.Happiness = 0;
		Assert.IsTrue( simulation.RouteToGate( guest, 0 ) );
		guest.State = GuestState.LeavingPark;
		for ( var tick = 0; tick < 4000 && guest.State == GuestState.LeavingPark; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( GuestState.CrossingRoadHome, guest.State );
		return guest;
	}

	private static void Turns( GuestSimulation simulation, int turns )
	{
		var target = simulation.ParkTurn + turns;
		while ( simulation.ParkTurn < target )
			simulation.Tick( Tick );
	}

	[TestMethod]
	public void AtTheGateAGuestWaitsWhileTheBusUnloadsThenWalksToOneOfTheStopCells()
	{
		var (simulation, _, vehicles) = Rig();
		var guest = AtTheGate( simulation );
		Assert.AreEqual( (5, 8), guest.Navigation.Cell );
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicles.Placed[ArrivalVehicleKind.Bus] = true;
		foreach ( var status in new[] { 3, 1, 2 } )
		{
			vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.StatusVariable] = status;
			Turns( simulation, 3 );
			Assert.AreEqual( GuestState.CrossingRoadHome, guest.State, $"0x0051A760: the bus at status {status} keeps the guest at the gate" );
		}
		vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.StatusVariable] = 4;
		Turns( simulation, 2 );
		Assert.AreEqual( GuestState.WalkingToVehicle, guest.State, "status 4 lets it walk" );
		var cells = new[] { (5, 3), (6, 3), (5, 1), (6, 1) };
		var (targetX, targetY) = (guest.Navigation.TargetX >> 16, guest.Navigation.TargetY >> 16);
		Assert.IsTrue( cells.Contains( (targetX, targetY) ), $"the destination ({targetX}, {targetY}) lies in one of the four cells around the stop" );
		Assert.IsTrue( guest.HasWaypoint && guest.Navigation.Original, "the walk is a route over the cell map" );
		for ( var turn = 0; turn < 400 && guest.State == GuestState.WalkingToVehicle; turn++ )
			Turns( simulation, 1 );
		Assert.AreEqual( GuestState.WaitingToGoHome, guest.State );
		Assert.IsNull( guest.Lane );
		Assert.IsTrue( cells.Contains( guest.Navigation.Cell ), $"it waits in {guest.Navigation.Cell}" );
	}

	[TestMethod]
	public void AGuestWithoutAVehicleOrWithTheFerryWalksAtOnce()
	{
		// 0x0051A760 returns 1 for no vehicle at work and for any vehicle but the bus
		var (simulation, _, vehicles) = Rig();
		var first = AtTheGate( simulation );
		Turns( simulation, 2 );
		Assert.AreEqual( GuestState.WalkingToVehicle, first.State, "no vehicle at work" );

		var (ferrySimulation, _, ferryVehicles) = Rig();
		var second = AtTheGate( ferrySimulation );
		ferrySimulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Ferry );
		ferryVehicles.Placed[ArrivalVehicleKind.Ferry] = true;
		ferryVehicles.Variables[ArrivalVehicleKind.Ferry][ArrivalVehicleKind.StatusVariable] = 2;
		Turns( ferrySimulation, 2 );
		Assert.AreEqual( GuestState.WalkingToVehicle, second.State, "the Ferry at its stop" );
		Assert.IsNotNull( vehicles );
	}

	[TestMethod]
	public void TheGuestThatHeadsItsCellBoardsAtStatusFour()
	{
		var (simulation, _, vehicles) = Rig();
		var guest = AtTheGate( simulation );
		Turns( simulation, 2 );
		for ( var turn = 0; turn < 400 && guest.State == GuestState.WalkingToVehicle; turn++ )
			Turns( simulation, 1 );
		Assert.AreEqual( GuestState.WaitingToGoHome, guest.State );
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicles.Placed[ArrivalVehicleKind.Bus] = true;
		vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.StatusVariable] = 2;
		// 0x00500BD0: at status 1 to 3 a guest off the bus's two stop cells (the upper two are the seaplane's and the Ferry's) walks to one of them
		for ( var turn = 0; turn < 400 && !(guest.State == GuestState.WaitingToGoHome && guest.Navigation.Cell is (5, 3) or (6, 3)); turn++ )
			Turns( simulation, 1 );
		Assert.AreEqual( GuestState.WaitingToGoHome, guest.State );
		Assert.IsTrue( guest.Navigation.Cell is (5, 3) or (6, 3), $"{guest.Navigation.Cell}" );
		Turns( simulation, 2 );
		Assert.IsTrue( simulation.Guests.Contains( guest ), "status 2: the guest waits where it stands, on a stop cell" );
		vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.StatusVariable] = 4;
		Turns( simulation, 2 );
		Assert.IsFalse( simulation.Guests.Contains( guest ), "status 4: it boards and is gone" );
	}

	[TestMethod]
	public void OnlyTheHeadOfACellBoardsEachTurnAndTheOthersAfterIt()
	{
		var (simulation, _, vehicles) = Rig();
		var guests = new List<Guest>();
		for ( var index = 0; index < 3; index++ )
		{
			var guest = simulation.SpawnInPark( 5, 10 );
			var (x, y) = GuestNavigation.BytePoint( (5, 3), 60 + 20 * index, 60 );
			(guest.Navigation.X, guest.Navigation.Y) = (x, y);
			(guest.CellX, guest.CellY) = (5, 3);
			guest.State = GuestState.WaitingToGoHome;
			guest.HasWaypoint = false;
			guests.Add( guest );
		}
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicles.Placed[ArrivalVehicleKind.Bus] = true;
		vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.StatusVariable] = 4;
		var left = new List<int>();
		for ( var turn = 0; turn < 8; turn++ )
		{
			Turns( simulation, 1 );
			left.Add( guests.Count( guest => !simulation.Guests.Contains( guest ) ) );
		}
		Assert.IsTrue( left.Zip( left.Skip( 1 ), ( a, b ) => b - a ).All( step => step is 0 or 1 ), $"one guest per cell and turn: {string.Join( " ", left )}" );
		Assert.AreEqual( 3, left[^1], "all of them board in the end" );
	}

	[TestMethod]
	public void AStopCellNoRouteReachesLeavesTheWalkStuckInStateTwenty()
	{
		// 0x00500AD0 does not read the route request's result and 0x005019F0 logs "Got stuck walking around outside" and stays in state 20: the guest takes no other way home
		var (simulation, grid, _) = Rig();
		for ( var y = 1; y <= 7; y++ )
			foreach ( var x in new[] { 5, 6 } )
				grid.Cells.SetType( x, y, ParkCellTypes.NoTerrain );
		var guest = AtTheGate( simulation );
		Turns( simulation, 3 );
		Assert.AreEqual( GuestState.WalkingToVehicle, guest.State, "no route to the stop: the walk stays in state 20" );
		Assert.IsTrue( guest.Navigation.Stuck );
		Turns( simulation, 40 );
		Assert.AreEqual( GuestState.WalkingToVehicle, guest.State );
		Assert.IsNull( guest.Lane );
	}

	[TestMethod]
	public void TheVehiclePickUpTestReadsTheHeadOfTheFourStopCells()
	{
		var (simulation, _, vehicles) = Rig();
		var guest = simulation.SpawnInPark( 5, 10 );
		var (x, y) = GuestNavigation.BytePoint( (6, 1), 100, 100 );
		(guest.Navigation.X, guest.Navigation.Y) = (x, y);
		guest.State = GuestState.WaitingToGoHome;
		guest.HasWaypoint = false;
		Turns( simulation, 1 );
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicles.Placed[ArrivalVehicleKind.Bus] = true;
		vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.StatusVariable] = 4;
		vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.TriggerVariable] = 0;
		Turns( simulation, 1 );
		Assert.AreEqual( 0, vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.TriggerVariable], "a leaving guest heads the cell two rows up: the bus is held at status 4" );
		Assert.IsFalse( simulation.Guests.Contains( guest ) );
	}

	[TestMethod]
	public void AWalkThatIsStuckOnTheWayToTheStopStaysInState20()
	{
		// 0x005019F0 case 0x14: the walk step's answer 2 logs "Got stuck walking around outside" and leaves the guest in state 20; it does not go on to state 21
		var (simulation, grid, vehicles) = Rig();
		var guest = AtTheGate( simulation );
		Turns( simulation, 2 );
		for ( var turn = 0; turn < 400 && guest.State == GuestState.WalkingToVehicle; turn++ )
			Turns( simulation, 1 );
		Assert.AreEqual( GuestState.WaitingToGoHome, guest.State );
		// the guest waits on one of the four cells; ask for the vehicle kind whose cells are the other pair and wall the rows between
		var onUpperPair = guest.Navigation.Cell.Y == 1;
		var kind = onUpperPair ? ArrivalVehicleKind.Bus : ArrivalVehicleKind.Ferry;
		foreach ( var x in new[] { 5, 6 } )
			foreach ( var y in onUpperPair ? new[] { 2, 3 } : new[] { 1, 2 } )
				grid.Cells.SetType( x, y, ParkCellTypes.NoTerrain );
		simulation.RestoreArrivalVehicleForTest( kind );
		vehicles.Placed[kind] = true;
		vehicles.Variables[kind][ArrivalVehicleKind.StatusVariable] = 2;
		Turns( simulation, 2 );
		Assert.AreEqual( GuestState.WalkingToVehicle, guest.State, "the vehicle's cells are off the guest's cells: it asks for a route there" );
		Turns( simulation, 60 );
		Assert.AreEqual( GuestState.WalkingToVehicle, guest.State, "no route: it stays stuck in state 20" );
		Assert.IsTrue( guest.HasWaypoint );
	}

	[TestMethod]
	public void GuestsOnTheWayToTheVehicleSurviveASaveAndLoad()
	{
		// state 20 is a guest state a save can hold; the states 19 and 21 on the cell map carry no lane and are valid too
		var (simulation, _, _) = Rig();
		var guest = AtTheGate( simulation );
		var gate = simulation.CaptureSnapshot();
		Assert.IsTrue( simulation.ValidateSnapshot( gate ).IsValid, string.Join( "; ", simulation.ValidateSnapshot( gate ).Errors ) );
		Turns( simulation, 2 );
		Assert.AreEqual( GuestState.WalkingToVehicle, guest.State );
		var walking = simulation.CaptureSnapshot();
		Assert.AreEqual( GuestState.WalkingToVehicle, walking.Guests.Single().State );
		Assert.IsTrue( simulation.ValidateSnapshot( walking ).IsValid, string.Join( "; ", simulation.ValidateSnapshot( walking ).Errors ) );
		Turns( simulation, 4 );
		var (restored, _, _) = Rig();
		restored.RestoreSnapshot( walking );
		Assert.AreEqual( GuestState.WalkingToVehicle, restored.Guests.Single().State );
		Assert.IsTrue( restored.Guests.Single().HasWaypoint, "the walk in progress is kept" );
		Turns( restored, 4 );
		Assert.AreEqual( guest.Navigation.X, restored.Guests.Single().Navigation.X, "the restored walk goes on exactly as the live one" );
		Assert.AreEqual( guest.Navigation.Y, restored.Guests.Single().Navigation.Y );
		for ( var turn = 0; turn < 400 && restored.Guests.Single().State == GuestState.WalkingToVehicle; turn++ )
			Turns( restored, 1 );
		Assert.AreEqual( GuestState.WaitingToGoHome, restored.Guests.Single().State );
		var waiting = restored.CaptureSnapshot();
		Assert.IsTrue( restored.ValidateSnapshot( waiting ).IsValid, string.Join( "; ", restored.ValidateSnapshot( waiting ).Errors ) );
	}

	// ---- original data ---------------------------------------------------------------------------------------------

	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	[TestMethod]
	public void OnTheJungleMapAGuestWalksFromTheEntranceOverTheWalkwayToTheBusStop()
	{
		// the original level's map init types the gate and bus-stop cells 0x1E (no terrain, MAP 0x80): a leaving guest reaches the crossing, the stop cells and boards over the real cells
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !System.IO.Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the Jungle level." );
		Log = new();
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
		var park = OriginalPark.Load( "jungle" );
		if ( park.Save == null )
			Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
		var grid = new GuestPathGrid( ParkCellMap.FromOriginal( park.Map, park.Save, park.Heightfield ) );
		var settings = GuestSettings.Load( "jungle" );
		var vehicles = new Vehicles();
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false, ArrivalVehicles = vehicles };
		var entrance = settings.ArrivalLaneA![^1];
		Assert.AreEqual( ParkCellTypes.FixedWalkway, grid.Cells.RawTypeAt( 47, 9 ), "the crossing cell is a walkway" );
		var guest = simulation.SpawnInPark( entrance.X, entrance.Y );
		guest.ExitLevel = 0;
		guest.Happiness = 0;
		Assert.IsTrue( simulation.RouteToGate( guest, 0 ), "a route from the entrance to the crossing" );
		Assert.AreEqual( 0, guest.Navigation.Mask, "the walkway is open to the guest's own mask" );
		guest.State = GuestState.LeavingPark;
		var states = new List<GuestState>();
		for ( var tick = 0; tick < 60 * 600 && guest.State != GuestState.WaitingToGoHome; tick++ )
		{
			simulation.Tick( Tick );
			if ( states.Count == 0 || states[^1] != guest.State )
				states.Add( guest.State );
		}
		CollectionAssert.AreEqual( new[] { GuestState.LeavingPark, GuestState.CrossingRoadHome, GuestState.WalkingToVehicle, GuestState.WaitingToGoHome }, states );
		Assert.IsTrue( new[] { (42, 5), (43, 5), (42, 3), (43, 3) }.Contains( guest.Navigation.Cell ), $"{guest.Navigation.Cell}" );
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicles.Placed[ArrivalVehicleKind.Bus] = true;
		vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.StatusVariable] = 4;
		for ( var turn = 0; turn < 400 && simulation.Guests.Contains( guest ); turn++ )
			Turns( simulation, 1 );
		// at status 4 the guest on an upper cell (the seaplane's) still boards: the head of its cell's list boards whatever the kind
		Assert.IsFalse( simulation.Guests.Contains( guest ), "it boarded" );
	}
}
