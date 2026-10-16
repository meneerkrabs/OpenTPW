using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Editing an existing ride's queue: a left click on a queue cell (the park view's help 444, "Left-click to
/// edit this queue") names the HasQueue ride that owns the cell, and the queue tool then extends that queue
/// (PATH-plan §4.1, §8). The owner lookup is asset-free; the tool start itself needs a running <see cref="Level"/>.
/// </summary>
[TestClass]
public class QueueEditTests
{
	private static (GuestPathGrid Grid, RideVisitorBridge Ride) QueuedRide()
	{
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		var ride = new RideVisitorBridge( 1, "Queue ride", RideVisitorKind.Ride, 5, 55, 30 )
		{
			EntranceCell = (2, 0),
			ExitCell = (2, 0),
			QueueFrontCell = (2, 5),
			QueueEntranceDirection = 2,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
		};
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 2, 5 ) );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 2, 4 ) );
		return (grid, ride);
	}

	[TestMethod]
	public void BackspaceTakesBackPathAndQueueCellsOnly()
	{
		// [EXT:queue-backtrack] tp.exe's backtrack key takes only path cells; OpenTPW also takes a queue back from the cursor.
		var (grid, ride) = QueuedRide();
		Assert.AreEqual( Level.BacktrackTarget.Path, Level.BacktrackTargetAt( grid.Cells, 2, 0 ), "a path cell: one placement off (the original's backtrack)" );
		Assert.AreEqual( Level.BacktrackTarget.Queue, Level.BacktrackTargetAt( grid.Cells, 2, 5 ), "the front queue cell" );
		Assert.AreEqual( Level.BacktrackTarget.Queue, Level.BacktrackTargetAt( grid.Cells, 2, 4 ), "the back queue cell" );
		Assert.AreEqual( Level.BacktrackTarget.None, Level.BacktrackTargetAt( grid.Cells, 5, 5 ), "grass: the key does nothing" );
		Assert.AreEqual( Level.BacktrackTarget.None, Level.BacktrackTargetAt( grid.Cells, -1, 40 ), "outside the map" );

		// The queue goes from the cell under the cursor to its back, as the remove tool takes it (Level.RemoveQueueCell).
		Assert.AreEqual( 1, QueuePaths.RemoveFrom( grid, ride, 2, 4 ) );
		CollectionAssert.AreEqual( new[] { (2, 5) }, ride.QueueCells.ToArray() );
		Assert.AreEqual( Level.BacktrackTarget.None, Level.BacktrackTargetAt( grid.Cells, 2, 4 ), "the taken cell is grass again" );
	}

	[TestMethod]
	public void QueueCellNamesItsHasQueueOwner()
	{
		var (grid, ride) = QueuedRide();
		var attractions = new[] { ride };
		Assert.AreSame( ride, Level.QueueRideAt( grid, attractions, 2, 5 ), "the front queue cell" );
		Assert.AreSame( ride, Level.QueueRideAt( grid, attractions, 2, 4 ), "the back queue cell" );
	}

	[TestMethod]
	public void NonQueueCellsHaveNoQueueOwner()
	{
		var (grid, ride) = QueuedRide();
		var attractions = new[] { ride };
		Assert.IsNull( Level.QueueRideAt( grid, attractions, 3, 0 ), "a path cell is not a queue" );
		Assert.IsNull( Level.QueueRideAt( grid, attractions, 7, 7 ), "an empty cell is not a queue" );
	}

	[TestMethod]
	public void RideWithoutHasQueueDoesNotOwnQueueCells()
	{
		var (grid, _) = QueuedRide();
		var shop = new RideVisitorBridge( 9, "Shop", RideVisitorKind.Shop, 1, 0, 10 )
		{
			EntranceCell = (2, 0),
			QueueFrontCell = (2, 5),
			Parameters = new QueueParameters( false, false, false, 130, 60, 60, 1, 30 )
		};
		Assert.IsNull( Level.QueueRideAt( grid, new[] { shop }, 2, 5 ), "only HasQueue rides own queue cells" );
	}

	[TestMethod]
	public void EveryQueueCellOfARideIsOwnedByIt()
	{
		var (grid, ride) = QueuedRide();
		foreach ( var cell in ride.QueueCells )
			Assert.AreSame( ride, Level.QueueRideAt( grid, new[] { ride }, cell.X, cell.Y ), $"queue cell {cell}" );
		Assert.AreEqual( ride.QueueCells.Count, ride.QueueCells.Distinct().Count() );
	}
}
