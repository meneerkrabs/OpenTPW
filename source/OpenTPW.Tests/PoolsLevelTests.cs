using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The opentpw.pools extension against a real <see cref="Level"/> (docs/POOLS.md): the park-resume capture with and without pool cells, sharing, and the placements of objects, access cells,
/// track tiles and coaster pylons over water, all through the level's own hooks. The level is the one the upgrade-catalogue tests assemble without a GPU.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolsLevelTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private Level level = null!;
	private ParkEconomy economy = null!;
	private OriginalObject one = null!;
	private Dictionary<string, string> assets = null!;

	private static object? Call( object target, string name, params object[] args ) => target.GetType().GetMethod( name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public )!.Invoke( target, args );

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
		var type = typeof( UpgradeCatalogueDefaultsTests );
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
		assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
		typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		(level, economy, one) = (world.Level, world.Economy, world.One);
		Call( level, "SetupPaths" );
		level.ConfigureObjectHooks();
		Assert.IsNotNull( level.PoolBuilder, "the level builds its pool tool" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture?.Cleanup();
		renderer?.Dispose();
	}

	private ParkCellMap Cells => level.Guests!.Grid.Cells;

	private static T With<T>( bool pools, Func<T> action )
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( pools ) );
		return action();
	}

	private ParkResumeSnapshot Capture() => level.CaptureResumeSnapshot( null, "pools", assets, new DateTimeOffset( 2026, 10, 17, 12, 0, 0, TimeSpan.Zero ) );

	// ---- Saves -------------------------------------------------------------------------------------------

	[TestMethod]
	public void RealCapturesWithoutPoolCellsAreByteIdenticalWithTheExtensionOnAndOff()
	{
		var off = With( false, () => ParkResumeSaveFile.Serialize( Capture() ) );
		var on = With( true, () => ParkResumeSaveFile.Serialize( Capture() ) );
		CollectionAssert.AreEqual( off, on, "a park with no pool cell saves the same bytes with the extension on or off" );
		Assert.IsFalse( Encoding.UTF8.GetString( on ).Contains( "opentpw.pools", StringComparison.Ordinal ) );
		Assert.IsNull( With( true, () => Capture().Mods ) );
		// a park whose pool cells were all erased again records nothing either (its cell map keeps the stamps of the writes, so the other bytes differ from a park that never had water)
		With( true, () =>
		{
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder!.Paint( 12, 12 ) );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Erase( 12, 12 ) );
			return 0;
		} );
		Assert.IsNull( With( true, () => Capture().Mods ) );
		Assert.IsFalse( Encoding.UTF8.GetString( With( true, () => ParkResumeSaveFile.Serialize( Capture() ) ) ).Contains( "opentpw.pools", StringComparison.Ordinal ) );
	}

	[TestMethod]
	public void ARealCaptureWithPoolCellsRecordsTheExtensionAndRefusesToBeMadeWithItOff()
	{
		With( true, () => level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (10, 10), (12, 11) ) ) );
		var snapshot = With( true, Capture );
		CollectionAssert.AreEqual( new[] { "opentpw.pools" }, snapshot.Mods!.Select( mod => mod.Id ).ToArray() );
		Assert.AreEqual( 6, snapshot.Layout.Cells.Cells.Count( cell => cell.Type == ParkCellTypes.PoolWater ) );
		var bytes = ParkResumeSaveFile.Serialize( snapshot );
		StringAssert.Contains( Encoding.UTF8.GetString( bytes ), "opentpw.pools" );
		// the file round-trips, and loads only with the extension on
		var loaded = ParkResumeSaveFile.Deserialize( bytes );
		CollectionAssert.AreEqual( bytes, ParkResumeSaveFile.Serialize( loaded ) );
		With( true, () => ParkLayoutSnapshot.RestoreCellMap( loaded.Layout ).CountOf( ParkCellType.PoolWater ) );
		With( false, () =>
		{
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( loaded.Mods, OpenTpwExtensions.Active ) ).Message, "needs opentpw.pools 1.0.0" );
			return 0;
		} );
		// a level with water cannot even be captured with the extension off: the capture is validated like a load
		With( false, () =>
		{
			Assert.ThrowsException<InvalidDataException>( () => Capture() );
			return 0;
		} );
	}

	[TestMethod]
	public void SharingIsRefusedForAParkWithPoolsAndOnlyThen()
	{
		With( true, () =>
		{
			Assert.IsNull( ParkSharing.ModsRefusal( level ), "the extension alone blocks nothing" );
			level.PoolBuilder!.Paint( 14, 14 );
			var refusal = ParkSharing.ModsRefusal( level );
			StringAssert.Contains( refusal, "opentpw.pools 1.0.0" );
			var exception = Assert.ThrowsException<InvalidOperationException>( () => ParkSharing.ExportLevel( level, "Park", "Me" ) );
			StringAssert.Contains( exception.Message, "cannot be exported" );
			level.PoolBuilder.Erase( 14, 14 );
			Assert.IsNull( ParkSharing.ModsRefusal( level ), "erased again: shareable again" );
			return 0;
		} );
	}

	// ---- Placements over water ---------------------------------------------------------------------------------

	private ObjectCatalogEntry Entry( int id, bool hasQueue, params string[] shape )
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name Synthetic{id}\nInfo.IsChoosable 1\nInfo.OverwritePriority 5\n" + (hasQueue ? "Info.HasQueue 1\n" : ""), $"synthetic{id}.sam" ) } );
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/synthetic", settings, new ObjectShape( shape ), "/model.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), one.Entry.FileSystem, null );
	}

	[TestMethod]
	public void ObjectsAndTheirAccessCellsAreRefusedOverWaterThroughTheLevelsHooks()
	{
		var shop = Entry( 1201, false, "**", "**" );
		var ride = Entry( 1202, true, "*2S" );
		With( true, () =>
		{
			Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( shop, 20, 20, 0 ), "control: open ground" );
			Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( ride, 4, 15, 0 ), "control: a ride with its access cells outside" );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder!.Paint( 21, 21 ) );
			Assert.AreEqual( OriginalPlacementResult.Occupied, level.Objects.Check( shop, 20, 20, 0 ), "one footprint cell of four is water" );
			Assert.IsNull( level.Objects.TryPlace( shop, 20, 20, 0, out _ ), "and the placement refuses" );
			// the ride's entrance '2' opens towards -Y: its outside cell is (x + 1, y - 1); its exit 'S' opens towards +Y: (x + 2, y + 1)
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Paint( 5, 14 ) );
			Assert.AreNotEqual( OriginalPlacementResult.Allowed, level.Objects.Check( ride, 4, 15, 0 ), "the entrance's outside cell is water" );
			level.PoolBuilder.Erase( 5, 14 );
			Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( ride, 4, 15, 0 ) );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Paint( 6, 16 ) );
			Assert.AreNotEqual( OriginalPlacementResult.Allowed, level.Objects.Check( ride, 4, 15, 0 ), "the exit's outside cell is water" );
			return 0;
		} );
		// the other way round: no pool is painted on a placed object, nor on the cells an object's access leaves to paths
		With( true, () =>
		{
			var placed = level.Objects.TryPlace( shop, 25, 25, 0, out _ );
			Assert.IsNotNull( placed );
			Cells.WriteFootprint( placed!.Cells );
			Assert.AreEqual( PoolBuildResult.Occupied, level.PoolBuilder!.Check( 25, 25 ), "the object's own cell" );
			return 0;
		} );
	}

	[TestMethod]
	public void TrackTilesAndCoasterPylonsAreRefusedOverWater()
	{
		With( true, () =>
		{
			var builder = new TrackBuilder( Cells, TrackFamily.Kart, () => null, level.Objects.Grid );
			Assert.AreEqual( CellBuildResult.Ok, builder.CheckTile( 8, 8 ), "control" );
			level.PoolBuilder!.Paint( 9, 9 );   // one of the tile's four cells
			Assert.AreEqual( CellBuildResult.Occupied, builder.CheckTile( 8, 8 ), "a tile over water" );
			Assert.AreEqual( CellBuildResult.Ok, builder.CheckTile( 14, 8 ) );
			// the coaster's pylon sites: a cell holding nothing but ground
			Assert.IsTrue( (bool)Call( level, "IsCoasterCellFree", 27, 27 )!, "control" );
			level.PoolBuilder.Paint( 27, 27 );
			Assert.IsFalse( (bool)Call( level, "IsCoasterCellFree", 27, 27 )!, "a pylon cannot stand in the pool" );
			// and the other way: water is not painted over a track tile
			Cells.Tracks.Set( 16, 16, new TrackCell { Type = TrackCellTypes.WaterOrigin, Parent = -1 } );
			Assert.AreEqual( PoolBuildResult.Occupied, level.PoolBuilder.Check( 16, 16 ) );
			return 0;
		} );
	}

	// ---- Who stands where ------------------------------------------------------------------------------------------

	[TestMethod]
	public void NoWaterIsPaintedUnderAGuestOrAnEmployee()
	{
		With( true, () =>
		{
			level.Guests!.Grid.SetPath( 6, 6, true );
			var guest = level.Guests.SpawnInPark( 6, 6 );
			Cells.SetType( 6, 6, ParkCellType.Empty );   // set down on open ground: the path under the guest is gone
			Assert.AreEqual( PoolBuildResult.Standing, level.PoolBuilder!.Check( guest.Cell.X, guest.Cell.Y ), "a guest stands there" );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Check( guest.Cell.X + 4, guest.Cell.Y ), "control: the cell beside is free" );
			var handyman = EconomyTestData.HireBest( economy, StaffType.Handyman );
			handyman.CellX = 18.4f;
			handyman.CellY = 7.9f;
			Assert.AreEqual( PoolBuildResult.Standing, level.PoolBuilder.Check( 18, 7 ), "an employee stands there (the cell their position falls in)" );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Check( 18, 8 ) );
			Assert.AreEqual( 0, level.PoolBuilder.PaintCells( new[] { (18, 7) } ).Cells.Count );
			Assert.AreEqual( ParkCellType.Empty, Cells.TypeAt( 18, 7 ) );
			return 0;
		} );
	}

	[TestMethod]
	public void NoWaterIsPaintedOnAFencePost()
	{
		var ring = new[] { (4, 4), (6, 4), (8, 4), (10, 4), (10, 6), (10, 8), (10, 10), (8, 10), (6, 10), (4, 10), (4, 8), (4, 6) };
		Cells.SetLand( new ParkLand( (4, 4), (4, 6), ring ) );
		With( true, () =>
		{
			Assert.IsTrue( Cells.Land!.IsPost( 6, 4 ) );
			Assert.AreEqual( PoolBuildResult.Occupied, level.PoolBuilder!.Check( 6, 4 ), "a post" );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Check( 7, 5 ), "control: the cell beside a post" );
			return 0;
		} );
	}

	// ---- The ground mesh's holes -----------------------------------------------------------------------------------------

	[TestMethod]
	public void TheLevelsGroundMeshSkipsPoolCellsOnlyWhileTheExtensionIsOn()
	{
		With( true, () => level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (3, 12), (4, 13) ) ) );
		bool Hole( bool on, int x, int y ) => PoolRenderHoles.IsHole( Cells, x, y, on );
		Assert.IsTrue( Hole( true, 3, 12 ) && Hole( true, 4, 13 ) );
		Assert.IsFalse( Hole( true, 5, 12 ), "a cell beside the pool" );
		Assert.IsFalse( Hole( false, 3, 12 ), "with the extension off nothing is a hole" );
		// the holes are the renderer's; the simulation's heights are not read from them (see the terrain test), and painting is the only thing that creates them
		var hash = level.ComputeStateHash();
		_ = PoolRenderHoles.IsHole( Cells, 3, 12, true );
		Assert.AreEqual( hash, level.ComputeStateHash(), "asking changes no state" );
	}
}
