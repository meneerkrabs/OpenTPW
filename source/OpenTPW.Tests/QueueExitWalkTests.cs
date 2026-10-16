using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Leaving a queue (QUEUE-010, QUEUE-005): the exit writes no position (tp.exe <c>0x005004AA</c>), so the guest decides in state 6 where it stood, and a guest whose cell is no
/// longer in the queue is routed to its place from there by the walk search (<c>0x00501160</c>) instead of being sent away.
/// </summary>
[TestClass]
public class QueueExitWalkTests
{
	private static List<Guest> Queue( QueueTests.Rig rig, int count )
	{
		var guests = Enumerable.Range( 0, count ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 120 );
		return QueueTests.Rig.InQueueOrder( guests );
	}

	[TestMethod]
	public void ADismissedHeadStaysOnItsQueueCellInStateSixAndDecidesFromThere()
	{
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guests = Queue( rig, 3 );
		var head = guests[0];
		Assert.AreEqual( (2, 5), head.Cell, "the head stands on the queue's front cell" );
		var position = (head.Navigation.X, head.Navigation.Y);
		rig.Script[RideVariables.VAR_BROKEN] = 1;   // the broken ride dismisses its head (0x004E1220 -> 0x004E0450 -> 0x005012F0)
		var turn = rig.Simulation.ParkTurn;
		while ( head.IsInQueue && rig.Simulation.ParkTurn < turn + 5 )
			rig.Step();
		Assert.IsFalse( head.IsInQueue );
		Assert.AreEqual( GuestState.DoingNothing, head.State );
		Assert.IsTrue( rig.Grid.IsQueue( head.Navigation.Cell.X, head.Navigation.Cell.Y ), "no position is written: the guest has not moved to the join cell" );
		Assert.AreEqual( (2, 5), head.Cell );
		Assert.AreEqual( position.X, head.Navigation.X, 20000, "and it has not moved within the cell either" );
	}

	[TestMethod]
	public void AGuestThatLeftTheQueueWalksOffItFromItsCell()
	{
		// tp.exe 0x004F9490 from a queue cell: its links except its own queue link (toward the front), so the guest walks back along the queue; a ride choice routes from it by the search
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guests = Queue( rig, 1 );
		var guest = guests[0];
		Assert.IsTrue( rig.Ride.AcceptsJoins );
		rig.Script[RideVariables.VAR_BROKEN] = 1;
		var cells = new HashSet<(int, int)>();
		var walked = false;
		rig.RunTurns( 400, () =>
		{
			cells.Add( guest.Navigation.Cell );
			walked |= guest.State == GuestState.WalkingAround;
		} );
		Assert.IsTrue( walked, "a wander destination was found from the queue cell" );
		Assert.IsTrue( cells.Count > 1, string.Join( " ", cells ) );
	}

	[TestMethod]
	public void AGuestWhoseCellLeftTheQueueIsRoutedToItsPlaceNotSentAway()
	{
		// tp.exe 0x00501390 changes nothing for a guest that stays; state 11 (0x004FFFF0) routes a guest whose place changed with the generic search from wherever it stands (0x00501160)
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guests = Queue( rig, 3 );
		var straggler = guests[2];
		// the straggler stands on the path beside the back of the queue (its cell is not a queue cell)
		var (x, y) = GuestNavigation.Centre( (3, 0) );
		(straggler.Navigation.X, straggler.Navigation.Y) = (x, y);
		(straggler.CellX, straggler.CellY) = (3, 0);
		var happiness = straggler.Happiness;
		rig.Ride.LeaveQueue( guests[0].Id );   // everyone behind moves up one place
		var states = new HashSet<GuestState>();
		rig.RunTurns( 120, () => states.Add( straggler.State ) );
		Assert.IsTrue( straggler.IsInQueue, $"the guest kept its place: {straggler.State}" );
		Assert.IsTrue( states.Contains( GuestState.MovingUpQueue ), "it walked to its place" );
		Assert.AreEqual( happiness, straggler.Happiness, "no dismissal: no medium happiness step" );
		Assert.AreEqual( GuestThought.None, straggler.Thought, "and no Confused thought" );
		Assert.IsTrue( rig.Grid.IsQueue( straggler.Navigation.Cell.X, straggler.Navigation.Cell.Y ) );
		Assert.AreEqual( 1, straggler.QueuePosition );
	}

	[TestMethod]
	public void AGuestWhoseRouteToItsPlaceFailsLeavesWithoutAThought()
	{
		// tp.exe 0x004FFFF0 at the move-up: "Couldn't get to my intended queue position", then 0x004DDD20, 0x005012F0 and state 6: no thought
		var rig = new QueueTests.Rig { KeepNeedsEmpty = true };
		rig.HoldAdmission();
		var guests = Queue( rig, 3 );
		var straggler = guests[2];
		rig.Grid.SetPath( 11, 8, true );   // an isolated path cell: no route leads from it to the queue
		var (x, y) = GuestNavigation.Centre( (11, 8) );
		(straggler.Navigation.X, straggler.Navigation.Y) = (x, y);
		(straggler.CellX, straggler.CellY) = (11, 8);
		rig.Ride.LeaveQueue( guests[0].Id );
		rig.RunTurns( 60 );
		Assert.IsFalse( straggler.IsInQueue );
		Assert.AreEqual( GuestThought.None, straggler.Thought );
	}
}
