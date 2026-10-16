using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The whole-walk destinations of states 10 and 18 (WALK-004, tp.exe <c>0x004FFBC0</c>, <c>0x004FCB10</c>, <c>0x004FA530</c>): one route request to the ride's back cell or the
/// gate cell, then the walk steps until it ends, where OpenTPW used to ask for the next path cell every time a cell was reached.
/// </summary>
[TestClass]
public class WholeWalkTests
{
	private static (int X, int Y) CentreOf( (int X, int Y) cell ) => GuestNavigation.BytePoint( cell, GuestWander.CentreByte, GuestWander.CentreByte );

	[TestMethod]
	public void TheMajorDecisionRequestsOneRouteToTheBackCellAndTheGuestWalksItAllTheWay()
	{
		// tp.exe 0x004FCB10: the route request 0x004FA530 to the back-of-queue cell (0x004DE110, centre bytes 0x80, 0x80); state 10 (0x004FFBC0) only steps the walk
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guest = rig.Simulation.SpawnInPark( 2, 0 );
		guest.Happiness = 60;
		Assert.AreEqual( 1, rig.Simulation.Choose( guest )?.AttractionId );
		var back = rig.Ride.QueueCells[^1];
		Assert.AreEqual( (2, 1), back );
		Assert.AreEqual( CentreOf( back ), (guest.Navigation.TargetX, guest.Navigation.TargetY), "the destination is the back cell's centre, not a path cell beside it" );
		Assert.IsTrue( guest.HasWaypoint && guest.Navigation.Original && !guest.Navigation.Stuck );
		guest.AttractionId = 1;
		guest.State = GuestState.GoingToRide;
		var target = (guest.Navigation.TargetX, guest.Navigation.TargetY);
		var searches = guest.Navigation.Reroutes;
		var cells = new List<(int X, int Y)>();
		for ( var turn = 0; turn < 80 && guest.State == GuestState.GoingToRide; turn++ )
		{
			rig.RunTurns( 1 );
			if ( guest.State == GuestState.GoingToRide )
				Assert.AreEqual( target, (guest.Navigation.TargetX, guest.Navigation.TargetY), "no new destination is set on the way" );
			cells.Add( guest.Navigation.Cell );
		}
		Assert.AreEqual( GuestState.MovingUpQueue, guest.State, "the guest joined" );
		Assert.AreEqual( 0, guest.Navigation.Reroutes - searches, "no reroute on an open way" );
		Assert.IsTrue( cells.Distinct().Count() <= 2, "from the path cell one step into the back cell" );
		Assert.AreEqual( 1, rig.Ride.QueueLength );
	}

	[TestMethod]
	public void AGuestJoinsOnlyOnTheBackCellItself()
	{
		// tp.exe 0x004FFBC0: the guest's cell must be the back-of-queue cell (0x004DE110); on the path cell beside it the guest keeps walking
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guest = rig.Send();
		Assert.AreEqual( (2, 0), guest.Navigation.Cell );
		rig.RunTurns( 1 );
		Assert.AreEqual( GuestState.GoingToRide, guest.State, "standing beside the back cell does not join" );
		Assert.AreEqual( 0, rig.Ride.QueueLength );
		Assert.IsTrue( guest.HasWaypoint, "a route request from the path cell to the back cell sets the walk" );
		Assert.AreEqual( CentreOf( (2, 1) ), (guest.Navigation.TargetX, guest.Navigation.TargetY) );
	}

	[TestMethod]
	public void WhenTheBackOfTheQueueMovesTheGuestAsksForTheNewBackCell()
	{
		// tp.exe 0x004FFBC0: "The back of the queue has moved while I was walking": the back cell is asked for again and a route keeps the guest in state 10
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guest = rig.Send();
		rig.RunTurns( 2 );
		Assert.AreEqual( GuestState.GoingToRide, guest.State );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( rig.Grid, rig.Ride, 3, 1 ) );
		QueuePaths.TryConnect( rig.Grid, rig.Ride );
		rig.RunTurns( 1 );
		Assert.AreEqual( (3, 1), rig.Ride.QueueCells[^1], "the queue now ends beside the path at (3, 0)" );
		var joinedAt = (-1, -1);
		for ( var turn = 0; turn < 80 && guest.State == GuestState.GoingToRide; turn++ )
		{
			rig.RunTurns( 1 );
			if ( guest.State != GuestState.GoingToRide )
				joinedAt = guest.Navigation.Cell;
		}
		Assert.AreEqual( GuestState.MovingUpQueue, guest.State );
		Assert.IsTrue( joinedAt == (3, 1) || joinedAt == (3, 2) || guest.QueuePosition == 0, $"joined at {joinedAt}" );
		Assert.AreEqual( 1, rig.Ride.QueueLength );
		Assert.AreEqual( 0, guest.QueuePosition );
	}

	[TestMethod]
	public void AGuestWhoseRouteFailsAtArrivalGoesBackToDeciding()
	{
		// tp.exe 0x004FFBC0: no route to the moved back cell: event 0x16 and state 6, without a thought or a happiness change
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guest = rig.Send();
		rig.RunTurns( 2 );
		// the queue loses its back cells: the new back (2, 3) touches no path and the empty cells between are closed to a walk without the mask
		QueuePaths.RemoveFrom( rig.Grid, rig.Ride, 2, 2 );
		for ( var turn = 0; turn < 60 && guest.State == GuestState.GoingToRide; turn++ )
			rig.RunTurns( 1 );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( 0, guest.AttractionId );
		Assert.AreEqual( GuestThought.None, guest.Thought );
		Assert.AreEqual( 0, rig.Ride.QueueLength );
	}

	[TestMethod]
	public void AWalkThatBecomesStuckCostsTheBigStepAndEndsInState6()
	{
		// tp.exe 0x004FFBC0 result 2: "The person has become stuck on the way", the big happiness change (0x004FEA70(2)), the target cleared and state 6
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		rig.Grid.SetPath( 10, 10, true );
		var guest = rig.Simulation.SpawnInPark( 10, 10 );
		guest.Happiness = 60;
		guest.AttractionId = 1;
		guest.State = GuestState.GoingToRide;
		var (x, y) = CentreOf( (2, 1) );
		Assert.IsFalse( guest.Navigation.Navigate( rig.Grid.Cells, x, y ), "no route from an isolated path cell" );
		guest.HasWaypoint = true;
		rig.RunTurns( 1 );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( 0, guest.AttractionId );
		Assert.AreEqual( MeterMath.Add( 60, -rig.Simulation.Settings.BigHappinessChange ), guest.Happiness );
	}

	[TestMethod]
	public void ARideTheGuestCannotReachIsNotChosen()
	{
		// tp.exe 0x004FCB10: a candidate whose route request fails is not the winner, whatever its score
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.Grid.SetPath( 10, 10, true );
		var guest = rig.Simulation.SpawnInPark( 10, 10 );
		guest.Happiness = 60;
		Assert.IsNull( rig.Simulation.PeekChoice( guest ), "an isolated path cell reaches no ride" );
		Assert.IsNull( rig.Simulation.Choose( guest ) );
		Assert.IsTrue( guest.Navigation.Stuck, "the failed request leaves the walk stuck, as the original's SetDest does" );
	}

	// ---- State 18: the walk to the gate's crossing cell ---------------------------------------------------------

	/// <summary>
	/// A path row y = 10 (x 0..24) with the two gate lanes going up from it as walkway cells (type 0x1E): lane A at x = 5, lane B at x = 18, each
	/// [bus stop, bus-side crossing, park-side crossing, booth, entrance] at y = 4, 6, 8, 9 and 10.
	/// </summary>
	private static (GuestSimulation Simulation, GuestPathGrid Grid) GateRig( bool laneCellsAreWalkways = true )
	{
		var grid = new GuestPathGrid( 30, 14 );
		for ( var x = 0; x < 25; x++ )
			grid.SetPath( x, 10, true );
		var settings = GuestTests.CreateSettings();
		settings.ArrivalLaneA = new[] { (5, 4), (5, 6), (5, 8), (5, 9), (5, 10) };
		settings.ArrivalLaneB = new[] { (18, 4), (18, 6), (18, 8), (18, 9), (18, 10) };
		if ( laneCellsAreWalkways )
		{
			foreach ( var lane in new[] { settings.ArrivalLaneA, settings.ArrivalLaneB } )
				foreach ( var (x, y) in lane.Take( 4 ) )
					grid.Cells.SetType( x, y, ParkCellTypes.FixedWalkway );
		}
		return (new GuestSimulation( grid, settings, 11 ) { ArrivalsEnabled = false }, grid);
	}

	[TestMethod]
	public void TheLeavingGateRequestsOneRouteToTheCrossingCellAndTheGuestWalksItWhole()
	{
		// tp.exe 0x004FEC90: the guest's own route requests to the gate's crossing cells (0x004D86D0) in the order the draw's bit 0 gives; the first found is state 18's walk
		var (simulation, grid) = GateRig();
		var guest = simulation.SpawnInPark( 14, 10 );
		guest.Happiness = 50;
		Assert.IsTrue( simulation.RouteToGate( guest, 0 ) );
		Assert.AreEqual( (0, CentreOf( (5, 8) )), (guest.ExitSide, (guest.Navigation.TargetX, guest.Navigation.TargetY)), "an even draw asks for gate cell 0, side A's crossing, first" );
		Assert.IsTrue( simulation.RouteToGate( guest, 1 ) );
		Assert.AreEqual( (1, CentreOf( (18, 8) )), (guest.ExitSide, (guest.Navigation.TargetX, guest.Navigation.TargetY)), "an odd draw asks for side B's crossing first (its X on side A's row)" );
		Assert.AreEqual( 0, guest.Navigation.Mask, "the first pass runs with the guest's own walk mask" );

		guest.State = GuestState.LeavingPark;
		guest.ExitLevel = 0;
		var target = (guest.Navigation.TargetX, guest.Navigation.TargetY);
		var seen = new HashSet<(int, int)>();
		for ( var tick = 0; tick < 4000 && guest.Lane == null && guest.State == GuestState.LeavingPark; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			if ( guest.Lane == null )
				Assert.AreEqual( target, (guest.Navigation.TargetX, guest.Navigation.TargetY), "no destination is set on the way" );
			seen.Add( guest.Navigation.Cell );
		}
		Assert.IsNotNull( guest.Lane, "it reached the gate and took the lane" );
		Assert.AreEqual( (18, 10), guest.Lane![^1], "side B's lane" );
		Assert.IsTrue( seen.Contains( (18, 9) ) || seen.Contains( (18, 8) ), "it walked the walkway cells" );
		Assert.IsTrue( grid.Cells.RawTypeAt( 18, 9 ) == ParkCellTypes.FixedWalkway );
	}

	[TestMethod]
	public void ASecondPassWithTheMaskOneReachesAGateCellOffTheWalkways()
	{
		// tp.exe 0x004FEE87: two passes of two requests; the walk mask +0x188 is 1 for the second pass and stays 1 when it finds a route, 0 when none does
		var (simulation, grid) = GateRig( laneCellsAreWalkways: false );
		var guest = simulation.SpawnInPark( 14, 10 );
		Assert.IsTrue( simulation.RouteToGate( guest, 0 ) );
		Assert.AreEqual( 1, guest.Navigation.Mask, "empty ground is reached only by a walker that may leave the paths" );
		Assert.AreEqual( 0, guest.ExitSide );
		var isolated = new GuestPathGrid( 30, 14 );
		isolated.SetPath( 14, 10, true );
		for ( var dy = -1; dy <= 1; dy++ )
			for ( var dx = -1; dx <= 1; dx++ )
				if ( dx != 0 || dy != 0 )
					isolated.Cells.SetType( 14 + dx, 10 + dy, ParkCellTypes.NoTerrain );
		var settings = GuestTests.CreateSettings();
		settings.ArrivalLaneA = new[] { (5, 4), (5, 6), (5, 8), (5, 9), (14, 10) };
		settings.ArrivalLaneB = null;
		var lonely = new GuestSimulation( isolated, settings, 12 ) { ArrivalsEnabled = false };
		var stranded = lonely.SpawnInPark( 14, 10 );
		Assert.IsFalse( lonely.RouteToGate( stranded, 0 ), "the cells around it are outside the terrain: no mask opens them" );
		Assert.AreEqual( 0, stranded.Navigation.Mask, "a failed gate walk leaves the mask 0" );
		Assert.IsNotNull( grid );
	}

	[TestMethod]
	public void AGuestOnAWalkwayCellCannotChangeItsMindButOneOnAPathCanBeRecalled()
	{
		// tp.exe 0x00500A50 / 0x004FA990: the recall needs the guest's own cell of type 0, 1, 3, 9 or 10; the gate's walkway cells (0x1E) refuse it
		var (simulation, grid) = GateRig();
		var recalled = simulation.SpawnInPark( 14, 10 );
		recalled.Happiness = 50;
		recalled.ExitLevel = 100;
		Assert.IsTrue( simulation.RouteToGate( recalled, 0 ) );
		recalled.State = GuestState.LeavingPark;
		var committed = simulation.SpawnInPark( 14, 10 );
		committed.Happiness = 50;
		committed.ExitLevel = 100;
		Assert.IsTrue( simulation.RouteToGate( committed, 0 ) );
		committed.State = GuestState.LeavingPark;
		var (x, y) = GuestNavigation.Centre( (5, 8) );
		(committed.Navigation.X, committed.Navigation.Y) = (x, y);
		for ( var tick = 0; tick < 70; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			if ( recalled.State != GuestState.LeavingPark )
				break;
		}
		Assert.AreEqual( GuestState.DoingNothing, recalled.State, "on a path cell with a counter, happiness and an open park, the guest changes its mind" );
		Assert.AreNotEqual( GuestState.DoingNothing, committed.State, "standing on the gate's walkway it walks on" );
		Assert.AreEqual( 0, recalled.Navigation.Mask, "the recall clears the walk mask" );
		Assert.IsNotNull( grid );
	}

	[TestMethod]
	public void AGuardsSetDownRoutesToTheGateCellItsDrawPicks()
	{
		// tp.exe 0x004FEB50: the mask 1, one route request to the gate cell the draw's bit 0 picks, state 18; no route destroys the guest
		var (simulation, grid) = GateRig();
		IGuardGuests guards = simulation;
		var guest = simulation.SpawnInPark( 14, 10 );
		guards.Hold( guest.Id );
		Assert.IsTrue( guards.Release( guest.Id, (14, 10), 1 ) );
		Assert.AreEqual( GuestState.LeavingPark, guest.State );
		Assert.AreEqual( CentreOf( (18, 8) ), (guest.Navigation.TargetX, guest.Navigation.TargetY), "side B's crossing cell, the cell the original's 0x004D86D0(1) gives" );
		Assert.AreEqual( 1, guest.Navigation.Mask );
		Assert.IsNotNull( grid );
	}
}
