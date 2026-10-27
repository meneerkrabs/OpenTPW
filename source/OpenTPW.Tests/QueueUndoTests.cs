using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// QUEUE-015: the queue tool's stack of corner points (tp.exe <c>0x530120</c>), the undo that takes a queue back one segment at a time
/// (<c>0x52FE50</c>) and the demolition that undoes every point (<c>0x527EE0</c>).
/// </summary>
[TestClass]
public class QueueUndoTests
{
	/// <summary>A ride whose front cell (2, 1) links up to the entrance cell on the path row; the queue grows down and right.</summary>
	private static (GuestPathGrid Grid, RideVisitorBridge Ride) Rig()
	{
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		var ride = new RideVisitorBridge( 1, "Queue ride", RideVisitorKind.Ride, 5, 55, 30 )
		{
			EntranceCell = (2, 0),
			ExitCell = (2, 0),
			QueueFrontCell = (2, 1),
			QueueEntranceDirection = 0,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
		};
		return (grid, ride);
	}

	private static void Lay( GuestPathGrid grid, RideVisitorBridge ride, params (int X, int Y)[] cells )
	{
		foreach ( var (x, y) in cells )
			Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( grid, ride, x, y ), $"lay {(x, y)}" );
		ride.RecomputeQueue( grid );
	}

	private static CellBuildTool Enter( GuestPathGrid grid, RideVisitorBridge ride )
	{
		var writer = new QueueLineWriter( grid, ride, ( x, y, pending ) => QueuePaths.CheckExtend( grid, ride, x, y, pending ),
			( x, y ) => QueuePaths.TryExtend( grid, ride, x, y ),
			( x, y ) => QueuePaths.RemoveFrom( grid, ride, x, y ), () => 75 );
		ride.RecomputeQueue( grid );
		var stack = QueuePaths.CornerStack( grid, ride );
		var tool = new CellBuildTool();
		tool.Enter( writer, ride.QueueBackCell, stack, QueuePaths.StackSegments( ride, stack ) );
		return tool;
	}

	[TestMethod]
	public void TheStackHoldsTheFrontCellTheCornersAndTheBackCell()
	{
		var (grid, ride) = Rig();
		Lay( grid, ride, (2, 1), (2, 2), (2, 3) );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 3) }, QueuePaths.CornerStack( grid, ride ).ToArray(), "a straight queue: the front and the back" );
		Lay( grid, ride, (3, 3), (4, 3), (4, 4) );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 3), (4, 3), (4, 4) }, QueuePaths.CornerStack( grid, ride ).ToArray(), "every corner, then the back" );
	}

	[TestMethod]
	public void TheWalkWritesTheCounterOneOnACornerAndZeroOnEveryOtherCellItVisits()
	{
		// tp.exe 0x5302C2: the corner walk stores +0x20 through 0x5228A0: 1 for a corner (pushed), 0 for any other cell it visits
		var (grid, ride) = Rig();
		Lay( grid, ride, (2, 1), (2, 2), (2, 3), (3, 3), (4, 3), (4, 4) );
		foreach ( var (x, y) in ride.QueueCells )
			grid.Cells.SetPlacementCount( x, y, 7 );
		QueuePaths.WriteWalkCounts( grid, ride );
		var corners = QueuePaths.CornerStack( grid, ride ).ToHashSet();
		foreach ( var (x, y) in ride.QueueCells )
			Assert.AreEqual( corners.Contains( (x, y) ) && (x, y) != (2, 1) && (x, y) != (4, 4) ? (short)1 : (short)0, grid.Cells.PlacementCountAt( x, y ), $"counter of {(x, y)}" );
		Assert.AreEqual( (short)1, grid.Cells.PlacementCountAt( 2, 3 ), "a bend" );
		Assert.AreEqual( (short)1, grid.Cells.PlacementCountAt( 4, 3 ), "a bend" );
		Assert.AreEqual( (short)0, grid.Cells.PlacementCountAt( 2, 2 ), "a straight cell" );
		Assert.AreEqual( (short)0, grid.Cells.PlacementCountAt( 4, 4 ), "the back cell is no corner" );
	}

	[TestMethod]
	public void ACornerFrontCellAndAnEmptyQueueComeUpTwice()
	{
		var (grid, ride) = Rig();
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 1) }, QueuePaths.CornerStack( grid, ride ).ToArray(), "no cells yet: the walk ends on the cell outside the entrance" );
		Lay( grid, ride, (2, 1), (3, 1), (4, 1) );
		// the front links up to the entrance and right to the next cell: a corner, pushed as the first point and again as a corner
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 1), (4, 1) }, QueuePaths.CornerStack( grid, ride ).ToArray() );
	}

	[TestMethod]
	public void ASingleCellQueueHasTheFrontCellTwice()
	{
		var (grid, ride) = Rig();
		Lay( grid, ride, (2, 1) );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 1) }, QueuePaths.CornerStack( grid, ride ).ToArray() );
	}

	[TestMethod]
	public void AQueueJoinedToAPathEndsOnItsBackCell()
	{
		// the path beside the back cell turns away from the queue: the back cell is a corner, so it comes up as a corner and again as the end
		var (grid, ride) = Rig();
		grid.SetPath( 3, 3, true );
		Lay( grid, ride, (2, 1), (2, 2), (2, 3) );
		Assert.IsTrue( QueuePaths.Connect( grid, ride, (2, 3), (3, 3) ), "the queue's back is linked to the path beside it" );
		ride.RecomputeQueue( grid );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 3), (2, 3) }, QueuePaths.CornerStack( grid, ride ).ToArray(), "the walk steps onto the path and ends on the queue cell before it" );

		// the path goes on in the queue's own direction: the back cell is straight and comes up once
		(grid, ride) = Rig();
		grid.SetPath( 2, 4, true );
		Lay( grid, ride, (2, 1), (2, 2), (2, 3) );
		Assert.IsTrue( QueuePaths.Connect( grid, ride, (2, 3), (2, 4) ) );
		ride.RecomputeQueue( grid );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 3) }, QueuePaths.CornerStack( grid, ride ).ToArray() );
	}

	[TestMethod]
	public void TheCornerWalkTakesTheBackCellOffThePathItEndsOn()
	{
		var (grid, ride) = Rig();
		grid.SetPath( 3, 3, true );
		Lay( grid, ride, (2, 1), (2, 2), (2, 3) );
		Assert.IsTrue( QueuePaths.Connect( grid, ride, (2, 3), (3, 3) ) );
		Assert.IsNotNull( ride.JoinCell, "joined to the path" );
		var stackBefore = QueuePaths.CornerStack( grid, ride ).ToArray();

		Assert.IsTrue( QueuePaths.DetachFromPath( grid, ride ), "0x530379..0x5303A1: the link between the back cell and the path goes" );
		Assert.AreEqual( 0, grid.Cells.LinksAt( 2, 3 ) & 4, "the step bit on the back cell" );
		Assert.AreEqual( 0, grid.Cells.LinksAt( 3, 3 ) & 64, "the reverse bit on the path cell" );
		Assert.IsFalse( grid.AreConnected( 2, 3, 1 ), "the cells no longer connect" );
		Assert.IsNull( ride.JoinCell, "the queue's back is not connected to a path" );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 2), (2, 3) }, ride.QueueCells.ToArray(), "the queue cells stay" );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 3) }, QueuePaths.CornerStack( grid, ride ).ToArray(), "the back cell now ends the walk on its own" );
		Assert.AreEqual( 3, stackBefore.Length );

		Assert.IsFalse( QueuePaths.DetachFromPath( grid, ride ), "a second entry finds nothing joined" );

		// nothing restores the link on a cancel; a line that ends on the path again joins it (code 8)
		Assert.IsTrue( QueuePaths.Connect( grid, ride, (2, 3), (3, 3) ) );
		Assert.IsNotNull( ride.JoinCell );
	}

	[TestMethod]
	public void ANeverJoinedQueueHasNothingToTakeOffThePath()
	{
		var (grid, ride) = Rig();
		Lay( grid, ride, (2, 1), (2, 2), (2, 3) );
		Assert.IsFalse( QueuePaths.DetachFromPath( grid, ride ) );
		var (emptyGrid, emptyRide) = Rig();
		Assert.IsFalse( QueuePaths.DetachFromPath( emptyGrid, emptyRide ), "no queue cells yet" );
	}

	[TestMethod]
	public void TheUndoTakesTheQueueBackBySegmentsAndNeverTheFrontCell()
	{
		var (grid, ride) = Rig();
		Lay( grid, ride, (2, 1), (2, 2), (2, 3), (3, 3), (4, 3), (4, 4) );
		var tool = Enter( grid, ride );
		Assert.AreEqual( 4, tool.Vertices.Count );
		Assert.AreEqual( 3, tool.SegmentCount );
		Assert.AreEqual( (4, 4), tool.Start );

		Assert.IsTrue( tool.Undo() );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 2), (2, 3), (3, 3), (4, 3) }, ride.QueueCells.ToArray(), "the last segment goes: one cell" );
		Assert.AreEqual( (4, 3), tool.Start );
		Assert.IsTrue( tool.Undo() );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 2), (2, 3) }, ride.QueueCells.ToArray(), "the next segment: two cells at once" );
		Assert.AreEqual( (2, 3), tool.Start );
		Assert.IsTrue( tool.Undo() );
		CollectionAssert.AreEqual( new[] { (2, 1) }, ride.QueueCells.ToArray(), "the first segment leaves the front cell" );
		Assert.AreEqual( (2, 1), tool.Start );
		Assert.IsFalse( tool.Undo(), "a stack of one point has nothing to undo" );
		Assert.AreEqual( ParkCellType.Queue, grid.Cells.TypeAt( 2, 1 ) );
	}

	[TestMethod]
	public void ACornerFrontCellCostsAnUndoThatChangesNothing()
	{
		var (grid, ride) = Rig();
		Lay( grid, ride, (2, 1), (3, 1), (4, 1) );
		var tool = Enter( grid, ride );
		Assert.AreEqual( 3, tool.Vertices.Count );
		Assert.IsTrue( tool.Undo() );
		CollectionAssert.AreEqual( new[] { (2, 1) }, ride.QueueCells.ToArray(), "the segment past the front goes" );
		Assert.IsTrue( tool.Undo(), "the front cell's second point pops without a cell to take" );
		CollectionAssert.AreEqual( new[] { (2, 1) }, ride.QueueCells.ToArray() );
		Assert.IsFalse( tool.Undo() );
	}

	[TestMethod]
	public void TheToolKeepsLayingFromTheBackAfterAnUndo()
	{
		var (grid, ride) = Rig();
		Lay( grid, ride, (2, 1), (2, 2), (2, 3), (3, 3) );
		var tool = Enter( grid, ride );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( (2, 3), tool.Start );
		var segment = tool.Click( (2, 6) )!;
		CollectionAssert.AreEqual( new[] { (2, 4), (2, 5), (2, 6) }, segment.Built.ToArray() );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 2), (2, 3), (2, 4), (2, 5), (2, 6) }, ride.QueueCells.ToArray() );
		Assert.IsTrue( tool.Undo(), "the new segment is the stack's last one" );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 2), (2, 3) }, ride.QueueCells.ToArray() );
	}

	[TestMethod]
	public void AClickOnTheBackCellOfAnExistingQueueEndsTheTool()
	{
		var (grid, ride) = Rig();
		Lay( grid, ride, (2, 1), (2, 2), (2, 3) );
		var tool = Enter( grid, ride );
		Assert.IsNull( tool.Click( (2, 3) ), "with more than one point stored a click on the start ends the tool and lays nothing" );
		Assert.IsFalse( tool.IsActive );
	}

	[TestMethod]
	public void ADemolitionTakesEveryQueueCellIncludingTheNoModifyFrontCell()
	{
		var (grid, ride) = Rig();
		Lay( grid, ride, (2, 1), (2, 2), (2, 3), (3, 3), (4, 3) );
		grid.Cells.SetFlags( 2, 1, grid.Cells.FlagsAt( 2, 1 ) | ParkCellFlags.NoModify );
		Assert.AreEqual( 5, grid.QueueCellCount );
		var removed = QueuePaths.ClearAll( grid, ride );
		CollectionAssert.AreEqual( new[] { (2, 1), (2, 2), (2, 3), (3, 3), (4, 3) }, removed.ToArray() );
		Assert.AreEqual( 0, grid.QueueCellCount );
		Assert.IsFalse( grid.Cells.FlagsAt( 2, 1 ).HasFlag( ParkCellFlags.NoModify ), "the forced clear drops the flag with the cell" );
		Assert.AreEqual( 0, grid.Cells.LinksAt( 2, 0 ) & 0x10, "no link is left toward the removed front cell" );
		Assert.AreEqual( 0, QueuePaths.ClearAll( grid, ride ).Count, "nothing left to take" );
	}

	[TestMethod]
	public void ADemolitionRefundsEveryCellAndDebitsOneCell()
	{
		var economy = EconomyTestData.Park( initialCash: 100000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1100, out var ride ) );
		var percent = economy.ScrapPercent( ride! );
		var before = economy.Balance;
		long refunded = 0;
		for ( var cell = 0; cell < 4; cell++ )
			refunded += economy.RefundQueueCell( ride!.Id );
		var charge = economy.ChargeQueueDemolition( ride!.Id );
		Assert.AreEqual( 4 * (75 * percent / 100), refunded );
		Assert.AreEqual( 75 * percent / 100, charge, "one cell's refund, signed 32-bit division" );
		Assert.AreEqual( before + refunded - charge, economy.Balance, "four cells refund three cells' worth" );
		Assert.AreEqual( 0, economy.ChargeQueueDemolition( 12345 ), "an unknown ride charges nothing" );
	}
}
