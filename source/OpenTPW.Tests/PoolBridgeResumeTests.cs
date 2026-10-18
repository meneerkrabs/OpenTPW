using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class PoolBridgeResumeTests
{
	[TestMethod]
	public void RestoringBridgeCoverageRefreshesQueueJoinsBeforeValidatingSavedGeometry()
	{
		var rig = new QueueTests.Rig( queueCells: 0, gridSize: 20 );
		var cells = rig.Grid.Cells;
		rig.Ride.QueueFrontCell = (4, 4);
		rig.Ride.QueueEntranceDirection = 0;
		rig.Grid.SetQueue( 4, 4, 0 );
		rig.Grid.SetPath( 4, 5, true );
		Assert.IsTrue( QueuePaths.Connect( rig.Grid, rig.Ride, (4, 4), (4, 5) ) );
		Assert.AreEqual( (4, 5), rig.Ride.JoinCell );
		for ( var x = 6; x <= 8; x++ ) cells.SetType( x, 5, ParkCellType.PoolWater );
		rig.Grid.SetPath( 3, 5, true );
		rig.Grid.SetPath( 11, 5, true );
		var world = new PoolBridgeWorld( cells );
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( 4, 5, 1 ) );
		rig.Ride.RecomputeQueue( rig.Grid );
		Assert.IsNull( rig.Ride.JoinCell );
		var bridgeData = world.CaptureSnapshot();
		var saved = rig.Simulation.CaptureSnapshot() with { SeenGridVersion = rig.Grid.Version };
		var version = rig.Grid.Version;

		// Reproduce staging: the ordinary cells and queue exist before the elevated bridge records are restored.
		world.RestoreSnapshot( null );
		rig.Ride.RecomputeQueue( rig.Grid );
		Assert.AreEqual( (4, 5), rig.Ride.JoinCell );
		Assert.ThrowsException<InvalidDataException>( () => Level.ValidateResumeQueueGeometry( saved, rig.Simulation ),
			"The previous ordering rejects a valid saved null join by looking only at the ground path." );

		Level.RestoreResumeRoutingGeometry( saved, bridgeData, rig.Simulation );
		Assert.IsNull( rig.Ride.JoinCell );
		Assert.AreEqual( version, rig.Grid.Version, "Restoring routing must not change the saved cell-map version." );
		rig.Simulation.RestoreSnapshot( saved );
		Assert.IsNull( rig.Ride.JoinCell );
	}

	[TestMethod]
	public void AnEmptyBridgeRecordPreservesOrdinaryQueueValidationAndMapVersion()
	{
		var rig = new QueueTests.Rig();
		var world = new PoolBridgeWorld( rig.Grid.Cells );
		var saved = rig.Simulation.CaptureSnapshot() with { SeenGridVersion = rig.Grid.Version };
		var join = rig.Ride.JoinCell;
		var version = rig.Grid.Version;
		Level.RestoreResumeRoutingGeometry( saved, null, rig.Simulation );
		Assert.AreEqual( join, rig.Ride.JoinCell );
		Assert.AreEqual( version, rig.Grid.Version );
		Assert.AreEqual( 0, world.Count );
	}
}
