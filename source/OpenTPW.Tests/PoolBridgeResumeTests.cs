using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenTPW.Mods;
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
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( rig.Grid, rig.Ride, 4, 4 ) );
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
	[TestMethod]
	public void NoBridgeEnvelopeBytesStayIdenticalWithThePoolsExtensionEnabledOrDisabled()
	{
		var cells = new ParkCellMap( 20, 20 );
		var envelope = ParkResumeSaveFileTests.Envelope();
		var without = envelope with { Mods = ParkResumeMods.Capture( PoolsTests.PoolsSet( false ), ParkResumeMods.InUse( cells ) ) };
		var before = ParkResumeJson.Serialize( without );
		var world = new PoolBridgeWorld( cells );
		world.RestoreSnapshot( null );
		var withEmptyLayer = envelope with
		{
			PoolBridges = world.CaptureSnapshot(),
			Mods = ParkResumeMods.Capture( PoolsTests.PoolsSet( true ), ParkResumeMods.InUse( cells ) )
		};
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( withEmptyLayer ) );
		Assert.IsFalse( Encoding.UTF8.GetString( before ).Contains( "PoolBridges" ), "A legacy envelope gains no bridge field." );
	}

	[TestMethod]
	public void ABridgeWithoutAnyPoolObjectsStillRecordsThePoolsExtensionThroughItsWater()
	{
		var cells = new ParkCellMap( 20, 20 );
		cells.SetType( 1, 3, ParkCellType.Path );
		cells.SetType( 9, 3, ParkCellType.Path );
		for ( var x = 4; x <= 6; x++ ) cells.SetType( x, 3, ParkCellType.PoolWater );
		var world = new PoolBridgeWorld( cells );
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( 2, 3, 1 ) );
		var used = ParkResumeMods.InUse( cells, poolsObjects: false );
		Assert.IsTrue( used.Contains( OfficialExtensions.Pools.Id ) );
		var recorded = ParkResumeMods.Capture( PoolsTests.PoolsSet( true ), used );
		Assert.IsTrue( recorded!.Any( mod => mod.Id == OfficialExtensions.Pools.Id ) );
		Assert.IsNull( PoolBridgeWorld.ShapeProblem( world.CaptureSnapshot(), recorded ) );
	}

	[TestMethod]
	public void EmptyBridgeLayerPreservesEnvelopeBytesWithUmbrellasEnabledAndInUse()
	{
		foreach ( var umbrellasEnabled in new[] { false, true } )
		foreach ( var umbrellasInUse in new[] { false, true } )
		{
			if ( umbrellasInUse && !umbrellasEnabled ) continue;
			var cells = new ParkCellMap( 20, 20 );
			ModSet Active( bool pools ) => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
				new ModSettings { Enabled = new Dictionary<string, bool>
				{
					[OfficialExtensions.Pools.Id] = pools,
					[OfficialExtensions.Umbrellas.Id] = umbrellasEnabled
				} }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
			var used = ParkResumeMods.InUse( cells, poolsObjects: false, umbrellas: umbrellasInUse );
			var envelope = ParkResumeSaveFileTests.Envelope() with { Mods = ParkResumeMods.Capture( Active( false ), used ) };
			var world = new PoolBridgeWorld( cells );
			var withEmptyLayer = envelope with
			{
				PoolBridges = world.CaptureSnapshot(),
				Mods = ParkResumeMods.Capture( Active( true ), used )
			};
			CollectionAssert.AreEqual( ParkResumeJson.Serialize( envelope ), ParkResumeJson.Serialize( withEmptyLayer ) );
			Assert.AreEqual( umbrellasInUse, withEmptyLayer.Mods?.Any( mod => mod.Id == OfficialExtensions.Umbrellas.Id ) == true );
			Assert.IsFalse( withEmptyLayer.Mods?.Any( mod => mod.Id == OfficialExtensions.Pools.Id ) == true );
			Assert.AreEqual( 0, ParkResumeMods.Unmet( withEmptyLayer.Mods, Active( false ) ).Count );
			Assert.AreEqual( 0, ParkResumeMods.Unmet( withEmptyLayer.Mods, Active( true ) ).Count );
		}
	}

	[TestMethod]
	public void BridgeWaterAndUmbrellasRecordBothUsedOnlyExtensionsWithoutPoolObjects()
	{
		var cells = new ParkCellMap( 20, 20 );
		cells.SetType( 1, 3, ParkCellType.Path );
		cells.SetType( 9, 3, ParkCellType.Path );
		for ( var x = 4; x <= 6; x++ ) cells.SetType( x, 3, ParkCellType.PoolWater );
		var world = new PoolBridgeWorld( cells );
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( 2, 3, 1 ) );
		var active = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool>
		{
			[OfficialExtensions.Pools.Id] = true,
			[OfficialExtensions.Umbrellas.Id] = true
		} }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		var recorded = ParkResumeMods.Capture( active, ParkResumeMods.InUse( cells, poolsObjects: false, umbrellas: true ) );
		Assert.IsTrue( recorded!.Any( mod => mod.Id == OfficialExtensions.Pools.Id ) );
		Assert.IsTrue( recorded.Any( mod => mod.Id == OfficialExtensions.Umbrellas.Id ) );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( recorded, active ).Count );
		Assert.IsNull( PoolBridgeWorld.ShapeProblem( world.CaptureSnapshot(), recorded ) );
	}

	[TestMethod]
	public void LateRestoredCoasterPylonsCannotOverlapBridgeDecksOrEndConnections()
	{
		foreach ( var anchorY in new[] { 20, 21 } )
		{
			var cells = new ParkCellMap( 64, 64 );
			var bridge = new PoolBridge( 24, anchorY, 2, 0 );
			foreach ( var i in new[] { -1, PoolBridge.Length } )
			{
				var end = bridge.Cell( i );
				cells.SetType( end.X, end.Y, ParkCellType.Path );
			}
			for ( var i = 2; i <= 4; i++ )
			{
				var span = bridge.Cell( i );
				cells.SetType( span.X, span.Y, ParkCellType.PoolWater );
			}
			var world = new PoolBridgeWorld( cells );
			Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( bridge.X, bridge.Y, bridge.Direction ) );
			var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Restore collision" );
			var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
			CoasterTests.PlaceStation( builder, (20, 20) );
			Level.ValidateResumeBridgeCoasterGeometry( world, new[] { ride } );
			// A restored nonstation node appears only after the bridge placement validation already ran.
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
			Assert.ThrowsException<InvalidDataException>( () => Level.ValidateResumeBridgeCoasterGeometry( world, new[] { ride } ) );
			Level.ValidateResumeBridgeCoasterGeometry( null, new[] { ride } );
		}
	}

}
