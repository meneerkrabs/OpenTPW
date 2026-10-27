using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>ECON-049: the exit test of the object window's status (tp.exe 0x004DE5A0, read at 0x485F60 as status 0x18, EXIT NOT CONNECTED).</summary>
[TestClass]
public class ExitConnectedTests
{
	private static (GuestPathGrid Grid, RideVisitorBridge Ride) Rig( byte exitLink = 0x04 )
	{
		var grid = new GuestPathGrid( 12, 12 );
		grid.SetPath( 1, 1, true );
		// the exit cell (5, 5) links east (bit 4) to the cell in front of it, (6, 5)
		grid.Cells.SetType( 5, 5, ParkCellTypes.Entrance );
		grid.Cells.SetQueueLink( 5, 5, exitLink );
		grid.SetPath( 6, 5, true );
		var ride = new RideVisitorBridge( 1, "Synthetic exit", RideVisitorKind.Ride, 5, 55, 30 )
		{
			EntranceCell = (1, 1), ExitCell = (6, 5), QueueFrontCell = (1, 1), QueueEntranceDirection = 2, HasCells = true, ExitAccessCell = (5, 5)
		};
		return (grid, ride);
	}

	[TestMethod]
	public void AnExitWhoseFrontCellLeadsNowhereIsNotConnected()
	{
		var (grid, ride) = Rig();
		// the cell in front has ground on its three other sides: every edge is blocked
		Assert.IsFalse( ParkGate.ExitConnected( grid, ride ), "no open edge beyond the exit" );
	}

	[TestMethod]
	public void AnExitIsConnectedWhenOneOtherEdgeOfTheFrontCellIsOpen()
	{
		foreach ( var (x, y) in new[] { (7, 5), (6, 4), (6, 6) } )
		{
			var (grid, ride) = Rig();
			grid.SetPath( x, y, true );
			Assert.IsTrue( ParkGate.ExitConnected( grid, ride ), $"a path cell at ({x}, {y})" );
		}
	}

	[TestMethod]
	public void TheEdgeBackToTheExitDoesNotCount()
	{
		// the cell in front is a path, and so is the exit cell's own side: the edge back to the exit is the one the test leaves out
		var (grid, ride) = Rig();
		grid.SetPath( 5, 5, true );
		grid.Cells.SetQueueLink( 5, 5, 0x04 );
		Assert.IsFalse( ParkGate.ExitConnected( grid, ride ), "only the direction back to the exit is open" );
	}

	[TestMethod]
	public void AnUnplacedOrUnlinkedExitKeepsThePermissiveAnswer()
	{
		var (grid, ride) = Rig( exitLink: 0 );
		Assert.IsTrue( ParkGate.ExitConnected( grid, ride ), "an exit cell with no cardinal link byte is untyped geometry" );
		var (grid2, ride2) = Rig();
		ride2.ExitAccessCell = null;
		Assert.IsTrue( ParkGate.ExitConnected( grid2, ride2 ), "a bridge without an exit cell of its own" );
	}

	[TestMethod]
	public void TheStatusBannerShowsExitNotConnectedAfterTheQueueTest()
	{
		var open = new RideStatusFacts( RideObjectStates.Idle, false, false, false, false, RideServiceProgress.Waiting, false, true, true );
		Assert.AreEqual( UIStrings.ExitNotConnected, RideWindowStatus.Of( open ) );
		Assert.AreEqual( UIStrings.QueueNotConnected, RideWindowStatus.Of( open with { QueueDetached = true } ) );
	}
}
