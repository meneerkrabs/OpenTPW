using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The arrival walk of tp.exe's guests (WALK-008, was ARRIVAL-001, retired): a guest is made at the vehicle's stop cell (<c>0x004CF720</c>, <c>0x004FB201</c>), walks the cell map to the bus-side crossing
/// cell (state 0), waits there for the vehicle (state 1, <c>0x004FF520</c>), walks to a ticket booth (state 2, <c>0x004FF730</c>), is weighed (state 4), waits as the head of its cell
/// for the gate (state 3, <c>0x004FF7F0</c>) and walks on to the entrance (state 5, <c>0x004FFB20</c>), where it is numbered.
/// </summary>
[TestClass]
public class ArrivalWalkTests
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

	private sealed class Gate : IGuestPayments
	{
		public bool Closed;
		public bool Refuse;
		public int Paid;
		public int AdmissionFee => 0;
		public bool ParkClosed => Closed;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; Paid++; return !Refuse; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
	}

	/// <summary>
	/// The Jungle's layout in a small map: lane A runs bus stop (4, 5), crossing (9, 5), crossing (9, 9), booth (9, 13), entrance (9, 17) and lane B bus stop (20, 5), (10, 5), (10, 9), (10, 13),
	/// (10, 17); the road and the crossings are fixed walkway cells (type 0x1E, as the level's map types them), the park's path is the row y = 17 with the two entrance cells on it.
	/// </summary>
	private static (GuestSimulation Simulation, GuestPathGrid Grid, Vehicles Vehicles, Gate Gate) Rig( bool withVehicles = false )
	{
		var grid = new GuestPathGrid( 30, 24 );
		for ( var x = 4; x <= 20; x++ )
			grid.Cells.SetType( x, 5, ParkCellTypes.FixedWalkway );
		foreach ( var x in new[] { 9, 10 } )
			for ( var y = 5; y <= 16; y++ )
				grid.Cells.SetType( x, y, ParkCellTypes.FixedWalkway );
		for ( var x = 5; x <= 25; x++ )
			grid.SetPath( x, 17, true );
		var settings = GuestTests.CreateSettings();
		settings.ArrivalLaneA = new[] { (4, 5), (9, 5), (9, 9), (9, 13), (9, 17) };
		settings.ArrivalLaneB = new[] { (20, 5), (10, 5), (10, 9), (10, 13), (10, 17) };
		var vehicles = new Vehicles();
		var gate = new Gate();
		var simulation = new GuestSimulation( grid, settings, 3 ) { ArrivalsEnabled = false, ArrivalVehicles = withVehicles ? vehicles : null, Payments = gate };
		return (simulation, grid, vehicles, gate);
	}

	private static void Turns( GuestSimulation simulation, int turns )
	{
		var target = simulation.ParkTurn + turns;
		while ( simulation.ParkTurn < target )
			simulation.Tick( Tick );
	}

	private static void Until( GuestSimulation simulation, Func<bool> condition, int maximumTurns, string message )
	{
		for ( var turn = 0; turn < maximumTurns && !condition(); turn++ )
			Turns( simulation, 1 );
		Assert.IsTrue( condition(), message );
	}

	/// <summary>A guest placed by hand on a cell (centre) in a state, as a save or a test restores it.</summary>
	private static Guest At( GuestSimulation simulation, (int X, int Y) cell, GuestState state )
	{
		var guest = simulation.SpawnInPark( 9, 17 );
		(guest.Navigation.X, guest.Navigation.Y) = GuestNavigation.Centre( cell );
		(guest.CellX, guest.CellY) = cell;
		guest.State = state;
		guest.HasWaypoint = false;
		return guest;
	}

	[TestMethod]
	public void AGuestIsMadeAtTheVehiclesStopCellAndWalksToTheCrossingCellOfTheDrawnSide()
	{
		var (simulation, _, _, _) = Rig();
		var guest = simulation.SpawnArrival()!;
		Assert.AreEqual( GuestNavigation.Centre( (20, 5) ), (guest.Navigation.X, guest.Navigation.Y), "the bus stop B cell's centre (0x004CF720, 0x005100E0)" );
		Assert.AreEqual( GuestState.CrossingRoad, guest.State );
		Assert.IsTrue( guest.Navigation.Original && guest.HasWaypoint && guest.OnGrid && guest.Lane == null, "a route over the cell map" );
		Assert.IsFalse( simulation.IsOnParkCell( guest ), "the walkway is no park cell (0x004FA990)" );
		var target = (guest.Navigation.TargetX >> 16, guest.Navigation.TargetY >> 16);
		Assert.IsTrue( target is (9, 5) or (10, 5), $"the crossing cell of side A or B ({target})" );
		Assert.AreEqual( 0xC8, (guest.Navigation.TargetY >> 8) & 0xFF );

		var (ferry, _, _, _) = Rig();
		ferry.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Ferry );
		var onFerry = ferry.SpawnArrival()!;
		Assert.AreEqual( GuestNavigation.Centre( (20, 3) ), (onFerry.Navigation.X, onFerry.Navigation.Y), "the seaplane's and the Ferry's guests stand two rows up (0x004CF720: id − 0x100)" );
	}

	[TestMethod]
	public void TheStepOffWalkEndsInStateOneWhichWaitsForTheBusToLeave()
	{
		var (simulation, _, vehicles, _) = Rig( withVehicles: true );
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicles.Placed[ArrivalVehicleKind.Bus] = true;
		vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.StatusVariable] = 2;
		var guest = simulation.SpawnArrival()!;
		Until( simulation, () => guest.State == GuestState.CrossingRoadToPark, 200, "state 0 walks to the crossing cell and ends in state 1" );
		var crossing = guest.Navigation.Cell;
		Assert.IsTrue( crossing is (9, 5) or (10, 5), $"{crossing}" );
		var state = simulation.World.State;
		Turns( simulation, 30 );
		Assert.AreEqual( GuestState.CrossingRoadToPark, guest.State, "0x0051A760: the bus at status 2 holds the guests that stepped off" );
		Assert.AreEqual( state, simulation.World.State, "and state 1 takes no draw while it waits" );
		vehicles.Variables[ArrivalVehicleKind.Bus][ArrivalVehicleKind.StatusVariable] = 4;
		Turns( simulation, 2 );
		Assert.AreEqual( GuestState.GoingToTicketBooth, guest.State, "status 4 lets them walk on" );
	}

	[TestMethod]
	public void StateOneDrawsTheBoothSideAndTheTwoSubCellBytesAndAsksForTheRoute()
	{
		var (simulation, _, _, _) = Rig();
		var guest = At( simulation, (9, 5), GuestState.CrossingRoadToPark );
		var mirror = new WorldRng( simulation.World.State );
		var side = mirror.NextWord() & 1;
		var byteX = (int)(mirror.NextWord() & 0xFF);
		var byteY = (int)(mirror.NextWord() & 0xFF);
		Turns( simulation, 1 );
		Assert.AreEqual( GuestState.GoingToTicketBooth, guest.State );
		var booth = side == 0 ? (9, 13) : (10, 13);
		Assert.AreEqual( GuestNavigation.BytePoint( booth, byteX, byteY ), (guest.Navigation.TargetX, guest.Navigation.TargetY), "the side picks the booth cell (0x004D8610), the next two draws the point in it" );
		Assert.IsTrue( guest.Navigation.Original, "the route request is the original walk" );
	}

	[TestMethod]
	public void TheBoothWalkEndsInStateFourWhileTheParkIsOpenAndInStateThreeWhileItIsClosed()
	{
		var (simulation, _, _, gate) = Rig();
		var first = simulation.SpawnArrival()!;
		Until( simulation, () => first.State == GuestState.AtTicketBooth, 600, "the walk reaches the booth of an open park" );
		Assert.IsTrue( first.Navigation.Cell is (9, 13) or (10, 13), $"{first.Navigation.Cell}" );

		var (closedSimulation, _, _, closedGate) = Rig();
		closedGate.Closed = true;
		var second = closedSimulation.SpawnArrival()!;
		Until( closedSimulation, () => second.State == GuestState.WaitingAtBooth, 600, "a closed park's guest stops in state 3" );
		// 0x00501DB0 case 3: the wait is a world draw, R % 150 + 200
		Assert.IsTrue( second.BoothWait is >= 200 and <= 349, $"{second.BoothWait}" );
		Assert.IsFalse( second.Paid );
		Assert.AreEqual( 0, gate.Paid );
	}

	[TestMethod]
	public void AClosedParkSendsAGuestOfStateThreeHomeWhenItsWaitHasRunOut()
	{
		var (simulation, _, _, gate) = Rig();
		gate.Closed = true;
		var guest = At( simulation, (9, 13), GuestState.WaitingAtBooth );
		guest.BoothWait = 3;
		guest.ExitLevel = 500;
		Turns( simulation, 3 );
		Assert.AreEqual( GuestState.WaitingAtBooth, guest.State, "the wait counts down one per update first" );
		Assert.AreEqual( 0, guest.BoothWait );
		var mirror = new WorldRng( simulation.World.State );
		var side = (int)(mirror.NextWord() & 1);
		Turns( simulation, 1 );
		Assert.AreEqual( GuestState.LeavingPark, guest.State );
		Assert.AreEqual( (1, 0), (guest.Navigation.Mask, guest.ExitLevel), "+0x188 = 1 and the exit counter 0: state 18 never recalls it" );
		var gateCell = side == 0 ? (9, 9) : (10, 9);
		Assert.AreEqual( GuestNavigation.Centre( gateCell ), (guest.Navigation.TargetX, guest.Navigation.TargetY), "a draw's bit 0 picks the gate's crossing cell (0x004D86D0), the walk goes to its centre" );
		Until( simulation, () => guest.State == GuestState.CrossingRoadHome, 400, "state 18 ends at the gate in state 19" );
		Assert.AreEqual( gateCell, guest.Navigation.Cell );
	}

	[TestMethod]
	public void APaidGuestWalksInOnlyWhileItHeadsItsCellAndTheOthersFollowOneByOne()
	{
		var (simulation, _, _, _) = Rig();
		var older = At( simulation, (9, 13), GuestState.WaitingAtBooth );
		var newer = At( simulation, (9, 13), GuestState.WaitingAtBooth );
		foreach ( var guest in new[] { older, newer } )
			guest.Paid = true;
		Turns( simulation, 1 );
		Assert.AreEqual( (GuestState.WaitingAtBooth, GuestState.EnteringPark), (older.State, newer.State), "the newest guest heads the cell (the head of 0x004D91F0's list); the other waits" );
		Assert.IsTrue( new[] { (9, 17), (10, 17) }.Contains( ((int)(newer.Navigation.TargetX >> 16), (int)(newer.Navigation.TargetY >> 16)) ), "to the entrance cell of the side a draw picks (0x004D8690)" );
		Until( simulation, () => older.State == GuestState.EnteringPark, 40, "once the head has left the cell the next one is the head" );
		// each one is looked at on the turn it arrives (the first to arrive has started its wander when the other comes)
		var arrivedAt = new Dictionary<Guest, (int X, int Y)>();
		Until( simulation, () =>
		{
			foreach ( var guest in new[] { older, newer } )
				if ( guest.State == GuestState.DoingNothing && !arrivedAt.ContainsKey( guest ) )
					arrivedAt[guest] = guest.Navigation.Cell;
			return arrivedAt.Count == 2;
		}, 400, "both arrive at the entrance" );
		Assert.IsTrue( arrivedAt.Values.All( cell => cell is (9, 17) or (10, 17) ) );
	}

	[TestMethod]
	public void AnUnpaidGuestOfStateThreeReturnsToTheBoothWhenTheParkIsOpenAgain()
	{
		var (simulation, _, _, gate) = Rig();
		gate.Closed = true;
		var guest = At( simulation, (9, 13), GuestState.WaitingAtBooth );
		guest.BoothWait = 50;
		Turns( simulation, 2 );
		gate.Closed = false;
		Turns( simulation, 1 );
		// 0x004FF7F0: not paid and standing on the booth cell of side A: that cell is the one it walks to (no draw), the wait is 0, state 2
		Assert.AreEqual( (GuestState.GoingToTicketBooth, 0), (guest.State, guest.BoothWait) );
		Assert.AreEqual( GuestNavigation.Centre( (9, 13) ), (guest.Navigation.TargetX, guest.Navigation.TargetY) );
		Until( simulation, () => guest.State == GuestState.AtTicketBooth, 60, "it is weighed at the booth" );
	}

	[TestMethod]
	public void AClosedParkSendsAGuestOnItsWayInBackToABooth()
	{
		var (simulation, _, _, gate) = Rig();
		var guest = At( simulation, (9, 13), GuestState.EnteringPark );
		guest.Paid = true;
		var other = (int)(new WorldRng( simulation.World.State ).NextWord() & 1);
		gate.Closed = true;
		Turns( simulation, 1 );
		Assert.AreEqual( GuestState.GoingToTicketBooth, guest.State, "0x004FFB20: a closed park (or a gate whose script variable 1 is not 1) turns the guest back to a booth" );
		Assert.AreEqual( GuestNavigation.Centre( other == 0 ? (9, 13) : (10, 13) ), (guest.Navigation.TargetX, guest.Navigation.TargetY), "the booth of the side a draw picks" );

		var shut = Rig();
		var open = true;
		shut.Simulation.EntranceGateOpen = () => open;
		var second = At( shut.Simulation, (9, 13), GuestState.EnteringPark );
		open = false;
		Turns( shut.Simulation, 1 );
		Assert.AreEqual( GuestState.GoingToTicketBooth, second.State, "the same for a shut gate" );
	}

	[TestMethod]
	public void ArrivingAtTheEntranceNumbersTheVisitorWithTheParksCount()
	{
		var (simulation, _, _, _) = Rig();
		var first = At( simulation, (9, 17), GuestState.EnteringPark );
		var second = At( simulation, (10, 17), GuestState.EnteringPark );
		// the hand-placed guests were made on a path cell, which numbers them in the constructor (0x004FB1DF); these two are numbered at the entrance, so they start unnumbered
		var count = simulation.VisitorCount;
		first.VisitorNumber = second.VisitorNumber = 0;
		Turns( simulation, 1 );
		Assert.AreEqual( (GuestState.DoingNothing, GuestState.DoingNothing), (first.State, second.State), "0x004FFB20: the walk is done, state 6" );
		// the turn's thing sweep updates the newer guest first (0x00516380), so it takes the first number
		Assert.AreEqual( (count + 2, count + 1, count + 2), (first.VisitorNumber, second.VisitorNumber, simulation.VisitorCount), "0x0051AAF0 counts up, the guest takes the number (+0x1D8)" );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( simulation.CaptureSnapshot() ) );
		var resumed = Rig().Simulation;
		resumed.RestoreSnapshot( snapshot );
		Assert.AreEqual( (count + 2, count + 2, count + 1), (resumed.VisitorCount, resumed.Find( first.Id )!.VisitorNumber, resumed.Find( second.Id )!.VisitorNumber), "the count and the numbers are saved" );
	}

	[TestMethod]
	public void ABoothRefusalSendsTheGuestHomeOverTheMapWithTheLeavingMask()
	{
		var (simulation, _, _, gate) = Rig();
		gate.Refuse = true;   // a bankrupt park: the booth turns the guest back (ECON-030)
		var guest = simulation.SpawnArrival()!;
		Until( simulation, () => guest.State == GuestState.LeavingPark, 800, "the booth turns the guest back" );
		Assert.AreEqual( (1, 0), (guest.Navigation.Mask, guest.ExitLevel) );
		Assert.IsNull( guest.Lane );
		Until( simulation, () => guest.State == GuestState.CrossingRoadHome, 400, "it walks to the gate's crossing cell" );
		Assert.IsTrue( guest.Navigation.Cell is (9, 9) or (10, 9) );
	}

	[TestMethod]
	public void AFullArrivalGoesFromTheVehicleThroughTheBoothIntoThePark()
	{
		var (simulation, _, _, gate) = Rig();
		var guests = Enumerable.Range( 0, 6 ).Select( _ => simulation.SpawnArrival()! ).ToList();
		var states = new HashSet<GuestState>();
		for ( var tick = 0; tick < 600 * 15 && guests.Any( guest => guest.VisitorNumber == 0 ); tick++ )
		{
			simulation.Tick( Tick );
			foreach ( var guest in guests )
				states.Add( guest.State );
		}
		Assert.IsTrue( guests.All( guest => guest.VisitorNumber > 0 ), "every guest of the group enters the park" );
		Assert.AreEqual( 6, gate.Paid );
		Assert.AreEqual( 6L, simulation.Admissions );
		foreach ( var state in new[] { GuestState.CrossingRoad, GuestState.CrossingRoadToPark, GuestState.GoingToTicketBooth, GuestState.AtTicketBooth, GuestState.WaitingAtBooth, GuestState.EnteringPark } )
			Assert.IsTrue( states.Contains( state ), $"the chain passes state {state}" );
		CollectionAssert.AreEquivalent( new[] { 1, 2, 3, 4, 5, 6 }, guests.Select( guest => guest.VisitorNumber ).ToArray() );
	}

	// ---- Saves from before the arrival walk ----------------------------------------------------------------------------------

	/// <summary>A snapshot of the rig with one guest turned into a guest that walks an arrival lane off the grid, as schema 8 wrote it.</summary>
	private static (GuestSimulationSnapshot Snapshot, int Id) LaneSnapshot( GuestState state, bool reversed, int laneIndex, int lane = 0 )
	{
		var (simulation, _, _, _) = Rig();
		var guest = At( simulation, (9, 9), GuestState.DoingNothing );
		var snapshot = simulation.CaptureSnapshot();
		var cells = lane == 0 ? simulation.Settings.ArrivalLaneA! : simulation.Settings.ArrivalLaneB!;
		return (snapshot with
		{
			SchemaVersion = 8,
			Guests = snapshot.Guests.Select( item => item.Id == guest.Id ? item with
			{
				State = state, OnGrid = false, Lane = cells, LaneIndex = laneIndex, LaneReversed = reversed,
				Navigation = item.Navigation with { OffGrid = true }
			} : item ).ToArray()
		}, guest.Id);
	}

	[TestMethod]
	public void AGuestSavedOnAnArrivalLaneContinuesOnTheCellMapInTheStateItsLanePositionStoodFor()
	{
		foreach ( var (state, reversed, index, expected) in new[]
		{
			(GuestState.CrossingRoad, false, 1, GuestState.CrossingRoad),
			(GuestState.CrossingRoadToPark, false, 2, GuestState.CrossingRoadToPark),
			(GuestState.GoingToTicketBooth, false, 3, GuestState.GoingToTicketBooth),
			(GuestState.AtTicketBooth, false, 3, GuestState.AtTicketBooth),
			(GuestState.EnteringPark, false, 4, GuestState.EnteringPark),
			(GuestState.LeavingPark, true, 2, GuestState.LeavingPark),
			(GuestState.CrossingRoadHome, true, 1, GuestState.LeavingPark),
			(GuestState.WaitingToGoHome, true, 0, GuestState.WaitingToGoHome),
		} )
		{
			var (snapshot, id) = LaneSnapshot( state, reversed, index );
			var (resumed, _, _, _) = Rig();
			Assert.IsTrue( resumed.ValidateSnapshot( snapshot ).IsValid, $"{state}: {string.Join( "; ", resumed.ValidateSnapshot( snapshot ).Errors )}" );
			resumed.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) ) );
			var guest = resumed.Find( id )!;
			Assert.AreEqual( expected, guest.State, $"{state}" );
			Assert.IsTrue( guest.Lane == null && guest.OnGrid && !guest.Navigation.OffGrid, $"{state}: on the cell map" );
			Assert.IsTrue( resumed.ValidateSnapshot( resumed.CaptureSnapshot() ).IsValid );
			Assert.AreEqual( state == GuestState.EnteringPark, guest.Paid, $"{state}: only the walk to the entrance came after the booth took the fee" );
			if ( reversed && state != GuestState.WaitingToGoHome )
				Assert.AreEqual( (1, 0), (guest.Navigation.Mask, guest.ExitLevel), $"{state}: the way home with the leaving mask and no recall" );
			Turns( resumed, 800 );
			Assert.IsTrue( resumed.Find( id ) is null or { State: GuestState.DoingNothing or GuestState.WalkingAround or GuestState.WaitingToGoHome or GuestState.WalkingToVehicle or GuestState.CrossingRoadHome or GuestState.LeavingPark }, $"{state}: it carries on ({resumed.Find( id )?.State})" );
		}
	}

	[TestMethod]
	public void AGuestOfTheArrivalWalkOnAnArrivalLaneIsRefusedBySchemaNineButTakenBySchemaEight()
	{
		var (snapshot, id) = LaneSnapshot( GuestState.GoingToTicketBooth, false, 3 );
		var (simulation, _, _, _) = Rig();
		Assert.IsTrue( simulation.ValidateSnapshot( snapshot ).IsValid );
		var current = snapshot with { SchemaVersion = GuestSimulationSnapshot.CurrentSchemaVersion };
		Assert.IsFalse( simulation.ValidateSnapshot( current ).IsValid, "a schema 9 guest walks the cell map" );
		Assert.IsTrue( simulation.ValidateSnapshot( current ).Errors.Any( error => error.Contains( "arrival lane" ) && error.Contains( id.ToString() ) ) );
	}

	[TestMethod]
	public void ThePaidFlagAndTheGateWaitSurviveTheSaveAndTheWaitIsBounded()
	{
		var (simulation, _, _, _) = Rig();
		var guest = At( simulation, (9, 13), GuestState.WaitingAtBooth );
		guest.Paid = true;
		guest.BoothWait = GuestSimulation.GateWaitMaximum;
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( simulation.CaptureSnapshot() ) );
		var (resumed, _, _, _) = Rig();
		resumed.RestoreSnapshot( snapshot );
		var restored = resumed.Find( guest.Id )!;
		Assert.AreEqual( (GuestState.WaitingAtBooth, true, GuestSimulation.GateWaitMaximum), (restored.State, restored.Paid, restored.BoothWait) );
		Assert.AreEqual( simulation.ComputeStateHash(), resumed.ComputeStateHash() );
		Assert.IsFalse( resumed.ValidateSnapshot( snapshot with { Guests = snapshot.Guests.Select( item => item with { BoothWait = GuestSimulation.GateWaitMaximum + 1 } ).ToArray() } ).IsValid );
		Assert.IsFalse( resumed.ValidateSnapshot( snapshot with { VisitorCount = -1 } ).IsValid );
	}
}
