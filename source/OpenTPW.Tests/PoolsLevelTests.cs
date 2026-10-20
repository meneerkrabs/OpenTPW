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
		// the park's asset manifest as the real catalogue builds it with the extension's content roots in place: it lists the changing rooms' files, which a park without a room does not save
		var poolsAssets = PoolsManifest();
		Assert.IsTrue( poolsAssets.Keys.Any( key => key.StartsWith( "mod:opentpw.pools:", StringComparison.Ordinal ) ), "the manifest lists the extension's files" );
		var savedAssets = assets;
		assets = new Dictionary<string, string>( savedAssets.Concat( poolsAssets.Where( pair => pair.Key.StartsWith( "mod:opentpw.pools:", StringComparison.Ordinal ) ) ) );
		try
		{
			RealCapturesWithoutPoolCellsAreByteIdentical();
		}
		finally
		{
			assets = savedAssets;
		}
	}

	/// <summary>The asset manifest of a stand-in jungle game folder with the pools content roots on.</summary>
	private static IReadOnlyDictionary<string, string> PoolsManifest()
	{
		var folder = Directory.CreateTempSubdirectory( "opentpw-pools-level-" ).FullName;
		var previous = FileSystem;
		try
		{
			var totem = Path.Combine( folder, "game", "levels", "jungle", "rides", "totem" );
			Directory.CreateDirectory( totem );
			File.WriteAllText( Path.Combine( totem, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
			File.WriteAllBytes( Path.Combine( totem, "totem.MD2" ), Array.Empty<byte>() );
			FileSystem = new BaseFileSystem( Path.Combine( folder, "game" ) );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			PoolContent.DirectoryOverride = RepositoryPoolsFolder();
			using var scope = ModContentRoots.Use( PoolsTests.PoolsSet( true ), FileSystem );
			return ParkResumeAssetManifest.Capture( ObjectCatalog.Load( "jungle" ), "jungle", easy: false );
		}
		finally
		{
			PoolContent.DirectoryOverride = null;
			FileSystem = previous;
			Directory.Delete( folder, true );
		}
	}

	private static string RepositoryPoolsFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = Path.Combine( directory.FullName, "content", "pools" );
			if ( File.Exists( Path.Combine( candidate, "objects.json" ) ) )
				return candidate;
		}
		throw new FileNotFoundException( "content/pools not found above the test binaries." );
	}

	private void RealCapturesWithoutPoolCellsAreByteIdentical()
	{
		var off = With( false, () => ParkResumeSaveFile.Serialize( Capture() ) );
		var on = With( true, () => ParkResumeSaveFile.Serialize( Capture() ) );
		Assert.IsFalse( Encoding.UTF8.GetString( on ).Contains( "mod:opentpw.pools", StringComparison.Ordinal ), "none of the extension's files are in the saved manifest" );
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

	// ---- The swim session in the level ---------------------------------------------------------------------------------

	/// <summary>Makes the fixture's placed object a changing room whose pool-side cell is (12, 12) in a pool of 3 x 3 cells, registered with the guests like any placed object.</summary>
	private void MakeChangingRoom()
	{
		Assert.AreEqual( 9, With( true, () => level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (12, 12), (14, 14) ) ) ).Cells.Count );
		one.Visitors.IsChangingRoom = true;
		one.Visitors.PoolAccessCandidates = new[] { (12, 12) };
	}

	[TestMethod]
	public void TheLevelsPoolEraseGuardsHoldTheWaterUnderASwimmerAndBesideTheChangingRoomsBack()
	{
		MakeChangingRoom();
		With( true, () =>
		{
			var access = level.PoolEraseBlock( 12, 12 );
			Assert.AreEqual( PoolBuildResult.ChangingRoomAccess, access, "the cell beside the back of a changing room" );
			Assert.AreEqual( PoolBuildResult.ChangingRoomAccess, level.PoolBuilder!.Erase( 12, 12 ) );
			Assert.AreEqual( ParkCellTypes.PoolWater, Cells.RawTypeAt( 12, 12 ), "the water stayed" );
			Assert.IsNull( level.PoolEraseBlock( 14, 14 ), "control: a cell with nobody in it and no room beside it" );
			Assert.AreEqual( 1, level.Guests!.DebugStartSwimmers( one.Visitors, 1 ), "a guest swims in the pool" );
			var swimmer = level.Guests.Guests.Single( guest => guest.IsSwimming );
			var under = swimmer.Cell;
			Assert.AreEqual( PoolBuildResult.Swimmers, level.PoolEraseBlock( under.X, under.Y ), "the water under a swimmer" );
			Assert.AreEqual( PoolBuildResult.Swimmers, level.PoolBuilder.CheckErase( under.X, under.Y ) );
			Assert.AreEqual( PoolBuildResult.Swimmers, level.PoolBuilder.Erase( under.X, under.Y ) );
			Assert.AreEqual( ParkCellTypes.PoolWater, Cells.RawTypeAt( under.X, under.Y ), "the water stayed" );
			var away = Enumerable.Range( 12, 3 ).SelectMany( x => Enumerable.Range( 12, 3 ).Select( y => (X: x, Y: y) ) ).First( cell => cell != under && cell != (12, 12) );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Erase( away.X, away.Y ), "a cell with nobody in it goes" );
			return 0;
		} );
	}

	[TestMethod]
	public void DemolishingAChangingRoomThroughTheLevelStartsNoSwimForTheGuestsChangingInside()
	{
		MakeChangingRoom();
		With( true, () =>
		{
			level.Guests!.Grid.SetPath( 6, 6, true );
			var guest = level.Guests.SpawnInPark( 6, 6 );
			guest.AttractionId = one.Visitors.AttractionId;
			guest.State = GuestState.Using;
			one.Visitors.RestoreSnapshot( one.Visitors.CaptureSnapshot() with { OnRide = new[] { guest.Id }, BoardedTotal = 1 } );
			var swimmer = level.Guests.Guests.Count( other => other.InSwimSession );
			var random = level.Guests.CaptureSnapshot().RandomState;
			// the real path: ParkObjects.Remove -> OriginalObject.Delete (OnDelete -> Runtime.Stop -> ReleaseAll) -> ObjectRemoved -> GuestSimulation.Unregister
			Assert.IsTrue( level.Objects.Remove( one ) );
			Assert.AreEqual( swimmer, level.Guests.Guests.Count( other => other.InSwimSession ), "no swim started for the guest that was changing" );
			Assert.IsFalse( guest.InSwimSession );
			// the removal (0x004FB360) sets a rider of any attraction down in state 6, without ExitRide or a use
			Assert.AreEqual( GuestState.DoingNothing, guest.State, "it stepped out like a rider of any removed attraction" );
			Assert.AreEqual( 0, guest.AttractionId );
			Assert.AreEqual( random, level.Guests.CaptureSnapshot().RandomState, "and the world generator was not drawn for one" );
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

	// ---- The pool extras through the level ---------------------------------------------------------------------

	private ObjectCatalogEntry ExtraEntry( int id, PoolExtraKind kind, params string[] shape )
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name Extra{id}\nInfo.WhichUIType 3\nInfo.OverwritePriority 0\nPools.Extra {(int)kind}\n", $"extra{id}.sam" ) } );
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/extra", settings, new ObjectShape( shape ), "/model.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), one.Entry.FileSystem, null );
	}

	[TestMethod]
	public void RealExtrasThroughTheLevelAreOfferedAbortedWhenErasedKeepTheirWaterAndSaveMidActivity()
	{
		With( true, () =>
		{
			// a pool of 4 x 3 cells (x 4 to 7, y 1 to 3) whose steps are (4, 2), and the room `one` at (1, 1) with its pool-side cell there
			Assert.AreEqual( 12, level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (4, 1), (7, 3) ) ).Cells.Count );
			one.Visitors.IsChangingRoom = true;
			one.Visitors.PoolAccessCandidates = new[] { (4, 2) };
			level.RegisterPoolActivities();
			Call( level, "PrepareThings" );
			CollectionAssert.AreEquivalent( new[] { "ambient.water", "attraction.jets", "attraction.river", "attraction.ropeswing", "attraction.spa", "extra.board", "extra.ladder", "extra.lounger", "extra.shower", "slide.ride", "waves.ring" }, level.Guests!.PoolActivities.Select( activity => activity.Id ).ToArray(), "registered when the level's guests are made" );
			var (ladderEntry, showerEntry, loungerEntry) = (ExtraEntry( 66001, PoolExtraKind.Ladder, "*" ), ExtraEntry( 66003, PoolExtraKind.Shower, "*" ), ExtraEntry( 66004, PoolExtraKind.Lounger, "**" ));
			// placement through the level's own hooks
			Assert.AreEqual( OriginalPlacementResult.NeedsPoolEdge, level.Objects.Check( ladderEntry, 3, 1, 90 ), "the water cell beyond is a corner of the pool" );
			Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( ladderEntry, 3, 2, 90 ), "a straight edge" );
			Assert.AreEqual( OriginalPlacementResult.NeedsPoolEdge, level.Objects.Check( showerEntry, 6, 6, 0 ), "no changing room beside it" );
			Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( showerEntry, 2, 1, 0 ), "beside the changing room" );
			var ladder = level.Objects.TryPlace( ladderEntry, 3, 2, 90, out _ );
			var shower = level.Objects.TryPlace( showerEntry, 2, 1, 0, out _ );
			var lounger = level.Objects.TryPlace( loungerEntry, 5, 4, 180, out _ );
			Assert.IsTrue( ladder != null && shower != null && lounger != null, "all three were built" );
			Assert.IsTrue( ladder!.ThingId != 0 && shower!.ThingId != 0 && lounger!.ThingId != 0 && new[] { ladder.ThingId, shower.ThingId, lounger.ThingId }.Distinct().Count() == 3 );
			var sites = level.Guests.PoolExtraSites!();
			CollectionAssert.AreEquivalent( new[] { PoolExtraKind.Ladder, PoolExtraKind.Shower, PoolExtraKind.Lounger }, sites.Select( site => site.Kind ).ToArray() );
			// the water an extra needs cannot be erased
			Assert.AreEqual( PoolBuildResult.ExtraNeedsWater, level.PoolEraseBlock( 5, 3 ), "beside the lounger" );
			Assert.AreEqual( PoolBuildResult.ExtraNeedsWater, level.PoolBuilder.Erase( 5, 3 ) );
			Assert.AreEqual( ParkCellTypes.PoolWater, Cells.RawTypeAt( 5, 3 ), "the water stayed" );
			Assert.IsNull( level.PoolEraseBlock( 7, 3 ), "control: a corner of the pool no extra stands beside" );
			// a swimmer takes the ladder; erasing it mid-activity aborts the swimmer's activity at once and it leaves the session
			Assert.AreEqual( 1, level.Guests.DebugStartSwimmers( one.Visitors, 1 ) );
			var swimmer = level.Guests.Guests.Single( guest => guest.IsSwimming );
			Assert.IsTrue( level.Guests.DebugStartActivity( swimmer, "extra.ladder" ) );
			Assert.AreEqual( ladder.ThingId, swimmer.PoolActivityTarget, "the target is the thing id of the object that provides the activity" );
			Assert.AreEqual( PoolBuildResult.Swimmers, level.PoolEraseBlock( swimmer.Cell.X, swimmer.Cell.Y ), "the water under a guest in an activity too" );
			Assert.IsTrue( level.Objects.Remove( ladder ) );
			Assert.IsFalse( swimmer.InPoolActivity, "aborted with the object" );
			Assert.IsFalse( swimmer.InSwimSession, "and its session ended" );
			// the extras list is read again after a removal: a removed shower is not offered
			Assert.IsTrue( level.Guests.PoolExtraSites!().Any( site => site.Kind == PoolExtraKind.Shower ) );
			Assert.IsTrue( level.Objects.Remove( shower ) );
			Assert.IsFalse( level.Guests.PoolExtraSites!().Any( site => site.Kind == PoolExtraKind.Shower ), "no ghost shower in the same turn" );
			Assert.IsFalse( level.Guests.PoolExtraSites!().Any( site => site.Kind == PoolExtraKind.Ladder ) );
			// a shower placed again, a swimmer in it and a save in the middle: the restore hashes the same and goes on the same
			var again = level.Objects.TryPlace( showerEntry, 2, 1, 0, out _ );
			Assert.IsNotNull( again );
			Assert.AreEqual( 1, level.Guests.DebugStartSwimmers( one.Visitors, 1 ) );
			var showerer = level.Guests.Guests.Last( guest => guest.IsSwimming );
			Assert.IsTrue( level.Guests.DebugStartActivity( showerer, "extra.shower" ) );
			for ( var tick = 0; tick < 60 * 6 && ((showerer.PoolActivityPhase & 15) != 1 || showerer.PoolActivityTurns < 2); tick++ )
				level.Guests.Tick( 1f / FixedStepClock.TicksPerSecond );
			Assert.AreEqual( 1, showerer.PoolActivityPhase & 15, "under the shower" );
			var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( level.Guests.CaptureSnapshot() ) );
			var hasher = new StateHasher();
			level.Guests.AddCanonicalState( hasher );
			var before = hasher.Value;
			for ( var tick = 0; tick < 60 * 3; tick++ )
				level.Guests.Tick( 1f / FixedStepClock.TicksPerSecond );
			var later = new StateHasher();
			level.Guests.AddCanonicalState( later );
			level.Guests.RestoreSnapshot( snapshot );
			var restored = new StateHasher();
			level.Guests.AddCanonicalState( restored );
			Assert.AreEqual( before, restored.Value, "the restore hashes as the save" );
			for ( var tick = 0; tick < 60 * 3; tick++ )
				level.Guests.Tick( 1f / FixedStepClock.TicksPerSecond );
			var again2 = new StateHasher();
			level.Guests.AddCanonicalState( again2 );
			Assert.AreEqual( later.Value, again2.Value, "and goes on exactly as the running game did" );
			return 0;
		} );
	}
}
