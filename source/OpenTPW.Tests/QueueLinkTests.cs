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
	public void AQueueEditStampsTheBlockOfTheBackCell()
	{
		// [BIN:TPW-EXE:0x004DE1F0] the re-evaluation raises the stamp counter and writes it into the block of the back cell
		var (grid, ride) = Park();
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 2, 5 ) );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 2, 4 ) );
		var before = grid.Cells.StampCounter;
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, 2, 3 ) );
		Assert.AreEqual( (2, 3), ride.QueueBackCell );
		Assert.AreEqual( before + 2, grid.Cells.StampCounter, "the cell type write stamps once and the re-evaluation once more" );
		Assert.AreEqual( grid.Cells.StampCounter, grid.Cells.BlockStamp( 2 / 16, 3 / 16 ), "the back cell's block carries the re-evaluation's stamp" );
	}

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
	public void APathLaidOverAQueueCellEditsTheQueueOfTheRideThatOwnsIt()
	{
		// [BIN:TPW-EXE:0x0053481F] the cell type setter's queue branch (type 1 over type 3) runs the owning ride's re-evaluation 0x004DE1F0
		var (grid, ride) = Park();
		ride.OwnerWord = ParkCellMap.CellWord( 2, 0 );
		QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, 5 ).Select( index => (2, 5 - index) ) );
		Assert.AreEqual( ride.OwnerWord, grid.Cells.OwnerAt( 2, 1 ) );
		var paved = new List<ushort>();
		var paths = new ParkPathBuilder( grid.Cells, grid, (ParkEconomy?)null, new FlatGrid() );
		paths.QueueCellPaved += paved.Add;
		var result = paths.BuildSegment( (5, 1), (2, 1) );
		Assert.IsTrue( result.Built.Contains( (2, 1) ) || grid.Cells.TypeAt( 2, 1 ) == ParkCellType.Path, $"the path covers the queue end ({result.StoppedBy})" );
		CollectionAssert.AreEqual( new[] { ride.OwnerWord }, paved );
	}

	[TestMethod]
	public void EveryQueueEditRaisesTheReevaluationOfTheRide()
	{
		// tp.exe 0x004DE1F0 runs after each edit of the ride's cells; the level opens a closed, connected ride from it (QUEUE-023)
		var (grid, ride) = Park();
		var edits = 0;
		ride.QueueEdited += () => edits++;
		Assert.AreEqual( 5, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, 5 ).Select( index => (2, 5 - index) ) ), "five queue cells, each an edit" );
		Assert.AreEqual( 5, edits );
		Assert.IsFalse( ParkGate.QueueConnected( grid, ride ), "not connected yet: the level opens nothing" );
		Assert.AreEqual( QueueBuildResult.Connected, QueuePaths.TryExtend( grid, ride, 2, 0 ) );
		Assert.AreEqual( 6, edits, "ending on the path connects the queue, an edit as well" );
		Assert.IsTrue( ParkGate.QueueConnected( grid, ride ), "connected: the level opens a closed ride now" );
		var before = edits;
		Assert.IsTrue( QueuePaths.RemoveFrom( grid, ride, 2, 4 ) > 0 );
		Assert.AreEqual( before + 1, edits, "a cell clear is an edit too" );
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

	[TestMethod]
	public void AQueueWithAnyLinkByteIsNotRebuiltWhenTheRideRegisters()
	{
		var (grid, ride) = Park();
		for ( var y = 5; y >= 1; y-- )
			grid.SetQueue( 2, y, 2 );
		// A queue the player left unconnected has links on its cells; one linked cell is enough to keep the rebuild away from the rest.
		grid.Cells.SetLinks( 2, 3, 0x11 );
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		simulation.Register( ride );
		Assert.AreEqual( 0x11, grid.Cells.LinksAt( 2, 3 ) );
		Assert.AreEqual( 0, grid.Cells.LinksAt( 2, 5 ), "the front stays as saved" );
		Assert.AreEqual( 0, grid.Cells.LinksAt( 2, 1 ), "the back is not joined to the path" );
		var before = grid.Cells.Version;
		Assert.IsFalse( QueuePaths.RebuildMissingLinks( grid, ride ) );
		Assert.AreEqual( before, grid.Cells.Version, "a second rebuild writes nothing" );
	}

	[TestMethod]
	public void APathOverTheQueueEndTurnsItIntoAChargedPathCell()
	{
		var (grid, ride) = Park();
		Assert.AreEqual( 5, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, 5 ).Select( index => (2, 5 - index) ) ) );
		var economy = EconomyTestData.Park( initialCash: 50000 );
		var reported = new List<(int, int)>();
		var paths = new ParkPathBuilder( grid.Cells, grid, economy, new FlatGrid() )
		{
			Effects = new LinkEffects( QueueNeighbour: ( x, y ) => reported.Add( (x, y) ) )
		};
		Assert.IsTrue( ParkPathBuilder.IsQueueEnd( grid.Cells, 2, 1 ), "the unconnected back: one link, to a queue cell" );
		Assert.IsFalse( ParkPathBuilder.IsQueueEnd( grid.Cells, 2, 3 ), "a middle cell has two links" );
		Assert.IsFalse( ParkPathBuilder.IsQueueEnd( grid.Cells, 2, 5 ), "the front links to the ride, not a queue cell" );
		Assert.AreEqual( CellBuildResult.QueueCell, paths.CheckCell( 2, 3 ) );
		Assert.AreEqual( CellBuildResult.Ok, paths.CheckCell( 2, 1 ), "code 8 is accepted (the start check and the ghost)" );
		var balance = economy.Balance;
		var result = paths.BuildSegment( (2, 0), (2, 1) );
		Assert.IsTrue( result.Completed );
		CollectionAssert.AreEqual( new[] { (2, 1) }, result.Built.ToArray() );
		Assert.AreEqual( balance - paths.CellCost, economy.Balance, "the converted cell is charged as a path cell" );
		Assert.AreEqual( ParkCellType.Path, grid.Cells.TypeAt( 2, 1 ) );
		Assert.AreEqual( 0x00, grid.Cells.QueueLinkAt( 2, 1 ) );
		// It keeps its link to the queue (+Y) and links to the path (−Y); the path cell (2, 0) gave it the diagonals toward its own side
		// neighbours (3, 0) and (1, 0), and its link to the queue clears only the two diagonals beside +Y.
		Assert.AreEqual( 0x10 | 0x01 | 0x02 | 0x80, grid.Cells.LinksAt( 2, 1 ) );
		CollectionAssert.AreEqual( new[] { (2, 1) }, reported, "the queue cell before it links back: cat_ui 139" );
		ride.RecomputeQueue( grid );
		CollectionAssert.AreEqual( new[] { (2, 5), (2, 4), (2, 3), (2, 2) }, ride.QueueCells.ToArray(), "the ride's queue ends one cell earlier" );
		Assert.AreEqual( (2, 1), ride.JoinCell, "and is connected to the new path cell" );
		Assert.IsTrue( ParkGate.QueueConnected( grid, ride ) );
		Assert.IsTrue( grid.IsWalkable( 2, 1 ) && grid.Distance( 0, 0, 2, 1 ) >= 0 );
	}
}
