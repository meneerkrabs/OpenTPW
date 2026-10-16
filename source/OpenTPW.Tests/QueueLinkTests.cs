using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The queue cells' link bytes (map cell <c>+0x0C</c>) as tp.exe's queue line writes them (link writer <c>0x5348D0</c> mode 3):
/// the chain, the connection to the path a queue line ends on, the unlinking of removed cells, the join cell that follows
/// the links (<c>0x4DE840</c>), the back-of-queue link test (<c>0x4DE4A0</c>), the queue-neighbour sound and the links
/// rebuilt for queues saved before they existed.
/// </summary>
[TestClass]
public class QueueLinkTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 12;
		public int Height => 12;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) =>
			x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>A path row at y = 0 and a ride whose queue front is (2, 5), entrance toward +Y.</summary>
	private static (GuestPathGrid Grid, RideVisitorBridge Ride) Park()
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
		return (grid, ride);
	}

	private static QueueLineWriter Writer( GuestPathGrid grid, RideVisitorBridge ride ) =>
		new( grid, ride, ( x, y, pending ) => QueuePaths.CheckExtend( grid, ride, x, y, pending ), ( x, y ) => QueuePaths.TryExtend( grid, ride, x, y ),
			( x, y ) => QueuePaths.RemoveFrom( grid, ride, x, y ), () => 0 );

	[TestMethod]
	public void AQueueLineEndingOnAPathConnectsTheQueueToIt()
	{
		var (grid, ride) = Park();
		var tool = new CellBuildTool();
		tool.Enter( Writer( grid, ride ), (2, 5) );
		var result = tool.Click( (2, 0) );
		Assert.IsNotNull( result );
		CollectionAssert.AreEqual( new[] { (2, 5), (2, 4), (2, 3), (2, 2), (2, 1) }, ride.QueueCells.ToArray(), "the path cell is not laid" );
		Assert.AreEqual( 0x11, grid.Cells.LinksAt( 2, 1 ), "the back links to the cell before it and to the path" );
		Assert.AreEqual( 0x10, grid.Cells.LinksAt( 2, 0 ) & 0x10, "the path links back to the queue" );
		Assert.AreEqual( (2, 0), ride.JoinCell );
		Assert.IsFalse( tool.IsActive, "a line ending on a path ends the tool" );
	}

	[TestMethod]
	public void AQueueLineStoppingShortOfThePathLeavesItUnconnected()
	{
		var (grid, ride) = Park();
		var tool = new CellBuildTool();
		tool.Enter( Writer( grid, ride ), (2, 5) );
		tool.Click( (2, 1) );
		Assert.AreEqual( (2, 1), ride.QueueBackCell );
		Assert.IsNull( ride.JoinCell, "touching the path does not connect: only a line ending on it does" );
		Assert.IsFalse( ParkGate.QueueConnected( grid, ride ), "the back cell's link byte equals its queue link" );
		// A later path laid beside the back does not connect it either (mode 1 never links a queue cell).
		var paths = new ParkPathBuilder( grid.Cells, grid, (ParkEconomy?)null, new FlatGrid() );
		paths.BuildSegment( (3, 1), (5, 1) );
		ride.RecomputeQueue( grid );
		Assert.IsNull( ride.JoinCell );
	}

	[TestMethod]
	public void TheBackOfQueueLinkTestFollowsTheConnection()
	{
		var (grid, ride) = Park();
		Assert.IsTrue( ParkGate.QueueConnected( grid, ride ), "without real queue cells the ride is taken as connected (ECON-049)" );
		Assert.AreEqual( 5, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, 6 ).Select( index => (2, 5 - index) ) ) );
		Assert.IsTrue( ParkGate.QueueConnected( grid, ride ), "the run ended on the path, which connected it" );
		var paths = new ParkPathBuilder( grid.Cells, grid, (ParkEconomy?)null, new FlatGrid() );
		Assert.AreEqual( CellBuildResult.Ok, paths.Remove( 2, 0 ) );
		ride.RecomputeQueue( grid );
		Assert.AreEqual( 0x10, grid.Cells.LinksAt( 2, 1 ), "removing the join path unlinks the back cell" );
		Assert.IsNull( ride.JoinCell );
		Assert.IsFalse( ParkGate.QueueConnected( grid, ride ), "a queue whose back has lost its path cannot be opened" );
		// Laying the path again does not reconnect it.
		paths.BuildSegment( (2, 0), (2, 0) );
		Assert.IsFalse( ParkGate.QueueConnected( grid, ride ) );
	}

	[TestMethod]
	public void RemovingQueueCellsUnlinksTheirNeighbours()
	{
		var (grid, ride) = Park();
		Assert.AreEqual( 5, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, 6 ).Select( index => (2, 5 - index) ) ) );
		Assert.AreEqual( 2, QueuePaths.RemoveFrom( grid, ride, 2, 2 ) );
		Assert.AreEqual( 0x11, grid.Cells.LinksAt( 2, 4 ) & 0x11, "(2, 4) keeps its links to (2, 5) and loses none it had" );
		Assert.AreEqual( 0x10, grid.Cells.LinksAt( 2, 3 ), "the new back lost its bit toward the removed cell" );
		Assert.AreEqual( 0x00, grid.Cells.LinksAt( 2, 0 ) & 0x10, "the path lost its bit toward the removed back" );
	}

	[TestMethod]
	public void LayingAPathOverTheJoinCellPlaysTheQueueNeighbourSound()
	{
		var (grid, ride) = Park();
		QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, 6 ).Select( index => (2, 5 - index) ) );
		var reported = new List<(int, int)>();
		var paths = new ParkPathBuilder( grid.Cells, grid, (ParkEconomy?)null, new FlatGrid() )
		{
			Effects = new LinkEffects( QueueNeighbour: ( x, y ) => reported.Add( (x, y) ) )
		};
		paths.BuildSegment( (0, 0), (4, 0) );
		CollectionAssert.AreEqual( new[] { (2, 0) }, reported, "only the join cell has a queue neighbour linking back" );
		Assert.AreEqual( new System.Numerics.Vector3( 20, 0, 20 ), GameAudio.QueueNeighbourSoundPosition( 2, 0 ), "tp.exe places it at (10x, 0, 10x)" );
	}

	[TestMethod]
	public void AQueueSavedWithoutLinksGetsThemWhenTheRideRegisters()
	{
		var (grid, ride) = Park();
		// A save from before the links: the queue cells carry only their queue links.
		for ( var y = 5; y >= 1; y-- )
			grid.SetQueue( 2, y, 2 );
		Assert.IsTrue( Enumerable.Range( 1, 5 ).All( y => grid.Cells.LinksAt( 2, y ) == 0 ) );
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		simulation.Register( ride );
		Assert.AreEqual( 0x11, grid.Cells.LinksAt( 2, 5 ) );
		Assert.AreEqual( 0x11, grid.Cells.LinksAt( 2, 3 ) );
		Assert.AreEqual( 0x11, grid.Cells.LinksAt( 2, 1 ), "the back is linked to the path it used to be joined from" );
		Assert.AreEqual( (2, 0), ride.JoinCell );
		// A queue that already has links is left as it is.
		var before = grid.Cells.Version;
		Assert.IsFalse( QueuePaths.RebuildMissingLinks( grid, ride ) );
		Assert.AreEqual( before, grid.Cells.Version );
	}
}
