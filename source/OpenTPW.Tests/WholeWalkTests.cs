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
}
