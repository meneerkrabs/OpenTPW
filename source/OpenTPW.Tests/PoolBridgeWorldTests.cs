using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

[TestClass]
public class PoolBridgeWorldTests
{
	private static ParkCellMap Crossing()
	{
		var cells = new ParkCellMap( 16, 12 );
		cells.SetType( 1, 3, ParkCellType.Path );
		cells.SetType( 9, 3, ParkCellType.Path );
		for ( var x = 4; x <= 6; x++ ) cells.SetType( x, 3, ParkCellType.PoolWater );
		return cells;
	}

	private static ulong CellHash( ParkCellMap cells )
	{
		var hash = new StateHasher();
		cells.AddCanonicalState( hash );
		return hash.Value;
	}

	private static ulong BridgeHash( PoolBridgeWorld world )
	{
		var hash = new StateHasher();
		world.AddCanonicalState( hash );
		return hash.Value;
	}

	[TestMethod]
	public void LevelBridgeSiteRejectsForbiddenTerrainAndExistingCoasterNodes()
	{
		var terrain = new PoolsTests.FlatGrid( 64, 64 );
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Bridge collision" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		var coasters = new[] { ride };
		var occupied = ride.Track.Nodes[0];
		Assert.IsTrue( Level.IsPoolBridgeSiteReserved( terrain, coasters, occupied.X, occupied.Y ) );
		Assert.IsFalse( Level.IsPoolBridgeSiteReserved( terrain, coasters, 2, 3 ) );
		terrain.Terrain = ( _, _ ) => OriginalPlacementResult.NoTerrainSurface;
		Assert.IsTrue( Level.IsPoolBridgeSiteReserved( terrain, coasters, 2, 3 ) );
		terrain.Terrain = ( _, _ ) => OriginalPlacementResult.Allowed;
		Assert.IsTrue( Level.IsPoolBridgeSiteReserved( terrain, coasters, 64, 3 ) );
		Assert.IsTrue( Level.IsPoolBridgeSiteReserved( terrain, coasters, -1, 3 ) );
	}

	[TestMethod]
	public void CoasterAreaCannotPlacePylonsOnBridgeApproachesOrEndConnections()
	{
		var cells = Crossing();
		var world = new PoolBridgeWorld( cells );
		Assert.IsTrue( Level.IsCoasterAreaCell( cells, 2, 3 ) );
		Assert.IsTrue( Level.IsCoasterAreaCell( cells, 1, 3 ) );
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( 2, 3, 1 ) );
		for ( var x = 1; x <= 9; x++ ) Assert.IsFalse( Level.IsCoasterAreaCell( cells, x, 3 ) );
		Assert.IsTrue( Level.IsCoasterAreaCell( cells, 2, 4 ) );
		Assert.AreEqual( PoolBridgeResult.Ok, world.RemoveAt( 2, 3 ) );
		Assert.IsTrue( Level.IsCoasterAreaCell( cells, 2, 3 ) );
		Assert.IsTrue( Level.IsCoasterAreaCell( cells, 1, 3 ) );
	}

	[TestMethod]
	public void FlatBridgePreservesUnderlyingCellsAndOnlyTouchesVersionOnMutation()
	{
		var cells = Crossing();
		var world = new PoolBridgeWorld( cells );
		var before = CellHash( cells );
		var version = cells.Version;
		Assert.AreEqual( PoolBridgeResult.Ok, world.Check( 2, 3, 1 ) );
		Assert.AreEqual( version, cells.Version );
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( 2, 3, 1 ) );
		Assert.AreEqual( version + 1, cells.Version );
		Assert.AreEqual( before, CellHash( cells ) );
		Assert.AreEqual( 1, world.Count );
		for ( var x = 2; x <= 8; x++ ) Assert.IsTrue( world.Covers( x, 3 ) );
		Assert.IsTrue( world.Reserves( 1, 3 ) );
		Assert.IsTrue( world.Reserves( 9, 3 ) );
		Assert.AreEqual( PoolBridgeResult.Occupied, world.TryAdd( 2, 3, 1 ) );
		Assert.AreEqual( version + 1, cells.Version );
		Assert.AreEqual( PoolBridgeResult.Ok, world.RemoveAt( 5, 3 ) );
		Assert.AreEqual( version + 2, cells.Version );
		Assert.AreEqual( before, CellHash( cells ) );
		Assert.AreEqual( 0, world.Count );
	}

	[TestMethod]
	public void IncorrectWaterPathsOwnershipAndTerrainAreRefusedWithoutWrites()
	{
		var cells = Crossing();
		var world = new PoolBridgeWorld( cells );
		cells.SetType( 5, 3, ParkCellType.Empty );
		Assert.AreEqual( PoolBridgeResult.NotOverPool, world.Check( 2, 3, 1 ) );
		cells.SetType( 5, 3, ParkCellType.PoolWater );
		cells.SetType( 9, 3, ParkCellType.Empty );
		Assert.AreEqual( PoolBridgeResult.Occupied, world.Check( 2, 3, 1 ) );
		cells.SetType( 9, 3, ParkCellType.Path );
		cells.SetFlags( 3, 3, ParkCellFlags.Unowned );
		Assert.AreEqual( PoolBridgeResult.NotOwned, world.Check( 2, 3, 1 ) );
		cells.SetFlags( 3, 3, 0 );
		var steep = new PoolBridgeWorld( cells, ground: ( x, _ ) => x * 0.1f );
		Assert.AreEqual( PoolBridgeResult.TooSteep, steep.Check( 2, 3, 1 ) );
		var invalid = new PoolBridgeWorld( cells, ground: ( _, _ ) => float.NaN );
		Assert.AreEqual( PoolBridgeResult.TooSteep, invalid.Check( 2, 3, 1 ) );
		Assert.AreEqual( PoolBridgeResult.OutsideTerrain, world.Check( 2, 3, 4 ) );
		Assert.AreEqual( PoolBridgeResult.OutsideTerrain, world.Check( 0, 3, 1 ) );
		Assert.AreEqual( 0, world.Count );
	}

	[TestMethod]
	public void ReservedCellsAndPeopleBlockPlacementAndPeopleBlockRefundUntilClear()
	{
		var cells = Crossing();
		var reserved = true;
		var standing = false;
		var economy = EconomyTestData.Park( initialCash: 2400 );
		var world = new PoolBridgeWorld( cells, reserved: ( x, y ) => reserved && x == 4 && y == 3,
			occupied: ( x, y ) => standing && x == 5 && y == 3, economy: () => economy );
		Assert.AreEqual( PoolBridgeResult.Occupied, world.TryAdd( 2, 3, 1 ) );
		reserved = false;
		standing = true;
		Assert.AreEqual( PoolBridgeResult.Standing, world.TryAdd( 2, 3, 1 ) );
		Assert.AreEqual( 2400L, economy.Balance );
		standing = false;
		Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( 2, 3, 1 ) );
		Assert.AreEqual( 1200L, economy.Balance );
		standing = true;
		Assert.AreEqual( PoolBridgeResult.Standing, world.CheckRemove( 2, 3 ) );
		Assert.AreEqual( PoolBridgeResult.Standing, world.RemoveAt( 2, 3 ) );
		Assert.AreEqual( 1200L, economy.Balance );
		Assert.AreEqual( 1, world.Count );
		standing = false;
		Assert.AreEqual( PoolBridgeResult.Ok, world.RemoveAt( 8, 3 ) );
		Assert.AreEqual( 2400L, economy.Balance );
		Assert.AreEqual( PoolBridgeResult.NotBridge, world.RemoveAt( 8, 3 ) );
		Assert.AreEqual( 2400L, economy.Balance, "Repeated removal must not refund twice" );
	}

	[TestMethod]
	public void InsufficientMoneyAndDisabledExtensionDoNotMutateOrCharge()
	{
		var cells = Crossing();
		var economy = EconomyTestData.Park( initialCash: 1199 );
		var enabled = true;
		var world = new PoolBridgeWorld( cells, economy: () => economy, enabled: () => enabled );
		var version = cells.Version;
		Assert.AreEqual( PoolBridgeResult.NotEnoughMoney, world.TryAdd( 2, 3, 1 ) );
		enabled = false;
		Assert.AreEqual( PoolBridgeResult.ExtensionOff, world.Check( 2, 3, 1 ) );
		Assert.AreEqual( PoolBridgeResult.ExtensionOff, world.TryAdd( 2, 3, 1 ) );
		Assert.AreEqual( PoolBridgeResult.ExtensionOff, world.RemoveAt( 2, 3 ) );
		Assert.AreEqual( version, cells.Version );
		Assert.AreEqual( 1199L, economy.Balance );
		Assert.IsNull( world.CaptureSnapshot() );
	}

	[TestMethod]
	public void EmptyBridgeWorldAddsNoSaveSectionOrCanonicalBytes()
	{
		var cells = Crossing();
		var before = CellHash( cells );
		var version = cells.Version;
		var world = new PoolBridgeWorld( cells );
		Assert.IsNull( world.CaptureSnapshot() );
		Assert.AreEqual( new StateHasher().Value, BridgeHash( world ) );
		world.RestoreSnapshot( null );
		Assert.AreEqual( before, CellHash( cells ) );
		Assert.AreEqual( version, cells.Version );
		Assert.AreEqual( new StateHasher().Value, BridgeHash( world ) );
	}

	[TestMethod]
	public void SnapshotRoundTripKeepsCellVersionHashAndSupportsIdenticalContinuation()
	{
		var sourceCells = Crossing();
		var source = new PoolBridgeWorld( sourceCells, ground: ( _, _ ) => 1.25f );
		Assert.AreEqual( PoolBridgeResult.Ok, source.TryAdd( 2, 3, 1 ) );
		var bytes = ParkResumeJson.Serialize( source.CaptureSnapshot() );
		var data = ParkResumeJson.Deserialize<PoolBridgesSaveData>( bytes );
		var targetCells = Crossing();
		targetCells.RestoreVersion( sourceCells.Version );
		var target = new PoolBridgeWorld( targetCells, ground: ( _, _ ) => 1.25f );
		var version = targetCells.Version;
		target.RestoreSnapshot( data );
		Assert.AreEqual( version, targetCells.Version, "Restoration must not look like new building" );
		Assert.AreEqual( CellHash( sourceCells ), CellHash( targetCells ) );
		Assert.AreEqual( BridgeHash( source ), BridgeHash( target ) );
		CollectionAssert.AreEqual( bytes, ParkResumeJson.Serialize( target.CaptureSnapshot() ) );
		Assert.AreEqual( source.RemoveAt( 5, 3 ), target.RemoveAt( 5, 3 ) );
		Assert.AreEqual( sourceCells.Version, targetCells.Version );
		Assert.AreEqual( source.TryAdd( 2, 3, 1 ), target.TryAdd( 2, 3, 1 ) );
		Assert.AreEqual( sourceCells.Version, targetCells.Version );
		Assert.AreEqual( BridgeHash( source ), BridgeHash( target ) );
	}

	[TestMethod]
	public void RestoreSkipsChargesAndCurrentPeopleButRejectsChangedWaterOrGround()
	{
		var cells = Crossing();
		var source = new PoolBridgeWorld( cells );
		source.TryAdd( 2, 3, 1 );
		var snapshot = source.CaptureSnapshot()!;
		var economy = EconomyTestData.Park( initialCash: 0 );
		var target = new PoolBridgeWorld( cells, occupied: ( _, _ ) => true, economy: () => economy );
		target.RestoreSnapshot( snapshot );
		Assert.AreEqual( 1, target.Count );
		Assert.AreEqual( 0L, economy.Balance );
		var raised = new PoolBridgeWorld( cells, ground: ( _, _ ) => 1f );
		Assert.ThrowsException<InvalidDataException>( () => raised.RestoreSnapshot( snapshot ) );
		cells.SetType( 5, 3, ParkCellType.Empty );
		Assert.ThrowsException<InvalidDataException>( () => target.RestoreSnapshot( snapshot ) );
	}

	[TestMethod]
	public void ShapeValidationRejectsMalformedMissingModOverlappingAndOffMapRecords()
	{
		var mods = new[] { new ParkModRecord( OfficialExtensions.Pools.Id, "1.0.0", ModSet.NoFilesSha256 ) };
		var bridge = new PoolBridge( 2, 3, 1, 0 );
		string? Problem( params PoolBridge[] bridges ) => PoolBridgeWorld.ShapeProblem( new PoolBridgesSaveData( bridges ), mods );
		Assert.IsNull( PoolBridgeWorld.ShapeProblem( null, null ) );
		Assert.IsNull( Problem( bridge ) );
		Assert.IsNotNull( PoolBridgeWorld.ShapeProblem( new PoolBridgesSaveData( new[] { bridge } ), null ) );
		Assert.IsNotNull( PoolBridgeWorld.ShapeProblem( new PoolBridgesSaveData( null! ), mods ) );
		Assert.IsNotNull( Problem() );
		Assert.IsNotNull( Problem( null! ) );
		Assert.IsNotNull( Problem( bridge, bridge ) );
		Assert.IsNotNull( Problem( bridge with { Direction = 4 } ) );
		Assert.IsNotNull( Problem( bridge with { X = -1 } ) );
		Assert.IsNotNull( Problem( bridge with { X = 1022 } ) );
		Assert.IsNotNull( Problem( bridge with { GroundMillimetres = 100001 } ) );
		Assert.IsNotNull( Problem( Enumerable.Repeat( bridge, 257 ).ToArray() ) );
	}
}
