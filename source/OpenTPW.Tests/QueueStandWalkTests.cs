using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The called guest's last walk, into the ride's entrance cell for the stand point (state 13, tp.exe 0x004FFFF0 asks the route 0x004FA5F0 to the entry cell 0x004DEDF0), is the search and
/// edge-test walk of every other guest where the cell map lets it in, and a queue edit that takes the front cells leaves the guests that walk or wait there handled (WALK-006, QUEUE-015).
/// </summary>
[TestClass]
public class QueueStandWalkTests
{
	/// <summary>The entrance cell as a placement lays it: type 9 with a link and a queue link toward the front queue cell (ObjectAccessCells.Lay).</summary>
	private static void LayEntrance( QueueTests.Rig rig )
	{
		var cells = rig.Grid.Cells;
		var (x, y) = (2, 6);
		cells.SetType( x, y, ParkCellTypes.Entrance );
		cells.SetLinks( x, y, GuestPathGrid.LinkValue( 0 ) );
		cells.SetQueueLink( x, y, GuestPathGrid.LinkValue( 0 ) );
	}

	[TestMethod]
	public void ACalledGuestWalksTheOriginalWalkIntoALinkedEntranceCell()
	{
		var rig = new QueueTests.Rig( takesGuests: true, capacity: 1, parameters: new QueueParameters( true, false, false, 600, 60, 60, 1, 30 ) );
		LayEntrance( rig );
		var guests = Enumerable.Range( 0, 3 ).Select( _ => rig.Send() ).ToList();
		var walked = new HashSet<int>();
		rig.RunTurns( 600, () =>
		{
			foreach ( var guest in guests.Where( guest => guest.State == GuestState.Boarding && guest.HasWaypoint ) )
			{
				Assert.IsTrue( guest.Navigation.Original, "the walk into the entrance cell is the search and edge-test walk" );
				Assert.IsFalse( guest.Navigation.Stuck, "the edge test lets it into the linked entrance cell" );
				walked.Add( guest.Id );
			}
		} );
		Assert.IsTrue( walked.Count >= 1, "a guest was called and walked into the entrance cell" );
		Assert.IsTrue( guests.All( guest => guest.RidesTaken >= 1 ), string.Join( ", ", guests.Select( guest => $"{guest.State} {guest.RidesTaken}" ) ) );
	}

	[TestMethod]
	public void AnEntranceCellTheEdgeTestRefusesKeepsTheRouteWalk()
	{
		// imports and restores keep no link byte on the entrance cell, so the search cannot enter it (WALK-006): the guest still walks in and boards
		var rig = new QueueTests.Rig( takesGuests: true, capacity: 1, parameters: new QueueParameters( true, false, false, 600, 60, 60, 1, 30 ) );
		var guests = Enumerable.Range( 0, 2 ).Select( _ => rig.Send() ).ToList();
		var routeWalks = 0;
		rig.RunTurns( 600, () => { routeWalks += guests.Count( guest => guest.State == GuestState.Boarding && guest.HasWaypoint && !guest.Navigation.Original ); } );
		Assert.IsTrue( routeWalks > 0, "the called guest walked the route into the entrance cell" );
		Assert.IsTrue( guests.All( guest => guest.RidesTaken >= 1 ), string.Join( ", ", guests.Select( guest => $"{guest.State} {guest.RidesTaken}" ) ) );
	}

	[TestMethod]
	public void ACalledGuestWhoseQueueIsEditedAwayStandsInTheFallbackEntranceAndIsNotStuck()
	{
		var rig = new QueueTests.Rig();
		rig.HoldAdmission();
		var ride = rig.Ride;
		var owner = ParkCellMap.CellWord( 2, 6 );
		ride.OwnerWord = owner;
		foreach ( var cell in ride.QueueCells )
			rig.Grid.Cells.SetOwner( cell.X, cell.Y, owner );
		rig.Grid.Cells.SetFlags( 2, 5, ParkCellFlags.NoModify );
		var points = new[] { new ObjectAccessPoint( ObjectCellKind.Entrance, 2, 6, 2, 5 ) };
		LayEntrance( rig );
		var guests = Enumerable.Range( 0, 3 ).Select( _ => rig.Send() ).ToList();
		rig.RunTurns( 150, () => { } );
		Assert.IsTrue( guests.Any( guest => guest.IsInQueue ), "guests stand in the queue when the front goes" );

		Assert.AreEqual( 5, QueuePaths.RemoveFrom( rig.Grid, ride, 2, 5, removeFixed: true ) );
		var emptied = new HashSet<(int X, int Y)> { (2, 5), (2, 4), (2, 3), (2, 2), (2, 1) };
		Level.RefreshVisitorCells( new[] { ((IEnumerable<ObjectAccessPoint>)points, ride) }, rig.Grid, emptied );
		rig.Simulation.SetDownOffQueueCells( emptied );
		Assert.AreEqual( ride.EntranceCell, ride.QueueCells[0], "the queue fell back to the path cell as its one-cell front" );
		Assert.IsTrue( rig.Grid.IsWalkable( ride.QueueCells[0].X, ride.QueueCells[0].Y ) );
		Assert.AreEqual( ride.QueueCells[0], GuestSimulation.StandCellOf( ride ), "guests stand in the fallback entrance cell itself, not in the emptied cell beyond it" );

		rig.Open = true;
		rig.Script[RideVariables.VAR_RUNNING] = 0;
		var late = rig.Send();
		rig.RunTurns( 1500, () => { } );
		foreach ( var guest in guests.Append( late ) )
		{
			Assert.IsFalse( guest.Navigation.Stuck, $"guest {guest.Id} (state {guest.State}) is not stuck" );
			if ( guest.OnGrid && !guest.Navigation.OffGrid )
				Assert.IsTrue( rig.Grid.IsWalkable( guest.CellX, guest.CellY ) || rig.Grid.IsQueue( guest.CellX, guest.CellY ), $"guest {guest.Id} (state {guest.State}) stands on {(guest.CellX, guest.CellY)}" );
		}
	}
}
