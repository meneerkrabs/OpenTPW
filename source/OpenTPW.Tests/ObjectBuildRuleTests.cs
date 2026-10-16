using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The object build rules of tp.exe's cell validator <c>0x535670</c> (docs/OBJECTS.md "Build rules"): objects over plain path cells, which the
/// placement clears, the overwrite priority between objects and for the path and queue tools, and the rules of the access cells. Placed
/// objects are real <see cref="OriginalObject"/>s over a synthetic dummy-only model, wired to a cell map the way <see cref="Level"/> wires them.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectBuildRuleTests
{
	private string root = null!;
	private BaseFileSystem previousFiles = null!;
	private List<Entity> previousEntities = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousFiles = FileSystem; previousEntities = Entity.All; previousLog = Log;
		Entity.All = new(); Log = new Logger();
		root = Path.Combine( Path.GetTempPath(), "opentpw-build-rules-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "synthetic" ) );
		var model = Md2ModelFileTests.CreateGeometry();
		// A valid single dummy root with no mesh, as in ThemeParkIncLayoutCheckpointTests.
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } )
			BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "synthetic/model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "synthetic/script.RSE" ), new RideVMTests.Asm().I( Opcode.END ).Build( Enum.GetNames<RideVariables>() ) );
		FileSystem = new BaseFileSystem( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Entity.All = previousEntities; FileSystem = previousFiles; Log = previousLog;
		Directory.Delete( root, true );
	}

	private ObjectCatalogEntry Entry( int id, ObjectCategory category, string info, params string[] shape ) =>
		new( "jungle", category, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name Synthetic{id}\n{info}", $"synthetic{id}.sam" ) } ),
			new ObjectShape( shape ), "/synthetic/model.MD2", null, "/synthetic/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(),
			Array.Empty<string>(), FileSystem, null );

	/// <summary>A bush: one cell, overwrite priority 1 (small scenery).</summary>
	private ObjectCatalogEntry Bush() => Entry( 1401, ObjectCategory.Feature, "Info.OverwritePriority 1\n", "*" );

	/// <summary>A pillar: 2 × 2, overwrite priority 0 (other features).</summary>
	private ObjectCatalogEntry Pillar() => Entry( 1423, ObjectCategory.Feature, "Info.OverwritePriority 0\n", "**", "**" );

	/// <summary>A ride with a queue (priority 5): 3 × 3, entrance '2' at local (1, 0) opening to −V, exit 'S' at (1, 2) opening to +V.</summary>
	private ObjectCatalogEntry Ride() => Entry( 1110, ObjectCategory.Ride, "Info.OverwritePriority 5\nInfo.HasQueue 1\n", "*S*", "***", "*2*" );

	/// <summary>A shop (priority 5, no queue): 2 × 2, entrance '2' at local (0, 0) opening to −V.</summary>
	private ObjectCatalogEntry Shop() => Entry( 1203, ObjectCategory.Shop, "Info.OverwritePriority 5\n", "**", "2*" );

	/// <summary>A park as the level wires it: cell map, path builder, objects with the cell map's hooks and the footprint typing.</summary>
	private sealed class Park
	{
		public readonly GuestPathGrid Grid = new( ParkPlacement.TileCount, ParkPlacement.TileCount );
		public readonly ParkObjects Objects;
		public readonly ParkPathBuilder Paths;

		public Park()
		{
			Objects = new ParkObjects( null!, new SandboxParkGrid() )
			{
				IsReserved = ( x, y ) => Grid.Cells.RawTypeAt( x, y ) is not (0 or (byte)ParkCellType.Path or ParkCellTypes.Object or ParkCellTypes.Entrance or ParkCellTypes.Exit),
				IsUnowned = ( x, y ) => Grid.Cells.InBounds( x, y ) && Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ),
				PathAt = ( x, y ) => Level.ObjectPathAt( Grid.Cells, x, y ),
				ClearPath = ( x, y ) => Paths!.Remove( x, y ),
				PathRuleAllows = ( x, y ) => Paths!.CheckPathRule( x, y ) is CellBuildResult.Ok or CellBuildResult.Existing
			};
			Paths = new ParkPathBuilder( Grid.Cells, Grid, (ParkEconomy?)null, Objects.Grid, ( x, y ) => Objects.FindAt( x, y ) != null )
			{
				ObjectGivesWay = ( x, y ) => Objects.GivesWay( x, y, ParkObjects.PathOverwritePriority ),
				DemolishObject = Demolish
			};
			Objects.ObjectPlaced += item => Grid.Cells.WriteFootprint( item.Cells );
			Objects.ObjectRemoved += item => Grid.Cells.ClearFootprint( item.Cells );
		}

		public void Demolish( int x, int y )
		{
			if ( Objects.FindAt( x, y ) is { } item )
				Objects.Remove( item );
		}

		public bool QueueGivesWay( int x, int y ) => Objects.GivesWay( x, y, ParkObjects.QueueOverwritePriority );

		public bool QueueBlocked( int x, int y ) => Objects.FindAt( x, y ) != null && !QueueGivesWay( x, y );

		public void Path( (int X, int Y) from, (int X, int Y) to ) => Assert.IsTrue( Paths.TryLayLine( from, to, out var result ), $"{result.StoppedBy}" );

		public void Fix( int x, int y ) => Grid.Cells.SetFlags( x, y, Grid.Cells.FlagsAt( x, y ) | ParkCellFlags.NoModify );
	}

	// ---- Objects over paths ------------------------------------------------------------------------

	[TestMethod]
	public void AnObjectClearsAPlainPathCellAndRefusesAFixedOne()
	{
		var park = new Park();
		park.Path( (2, 5), (8, 5) );
		var bush = Bush();
		Assert.AreEqual( OriginalPlacementResult.Allowed, park.Objects.Check( bush, 5, 5, 0, out var plan ), "tp.exe 0x535D21: type 4 over a type-1 cell is allowed" );
		CollectionAssert.AreEqual( new[] { (5, 5) }, plan.ClearedPaths.ToArray() );
		Assert.IsNotNull( park.Objects.TryPlace( bush, 5, 5, 0, out _ ) );
		Assert.AreEqual( ParkCellTypes.Object, park.Grid.Cells.RawTypeAt( 5, 5 ), "the path is cleared before the footprint is typed" );
		Assert.IsFalse( park.Grid.AreConnected( 4, 5, GuestPathGrid.DirectionBetween( 4, 5, 5, 5 ) ), "the cleared cell's links go on both sides" );
		Assert.IsTrue( park.Grid.IsWalkable( 4, 5 ) && park.Grid.IsWalkable( 6, 5 ), "the rest of the path stays" );
		// A NoModify path (the map start's initial path, flag 0x20) refuses an object before any other test (0x5356B5).
		park.Fix( 3, 5 );
		Assert.AreEqual( OriginalPlacementResult.Path, park.Objects.Check( bush, 3, 5, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, park.Objects.Check( bush, 2, 5, 0 ) );
		// Without a live cell map the terrain rule's path cells still refuse (the headless callers).
		var bare = new ParkObjects( null!, new PathTerrain() );
		Assert.AreEqual( OriginalPlacementResult.Path, bare.Check( bush, 1, 1, 0 ) );
		bare.PathAt = ( _, _ ) => ObjectPathCell.None;
		Assert.AreEqual( OriginalPlacementResult.Allowed, bare.Check( bush, 1, 1, 0 ), "a live map without a path there overrides a stale terrain path" );
	}

	/// <summary>A grid whose every cell is a path in the terrain rule (as the save's path cells are).</summary>
	private sealed class PathTerrain : IParkGrid
	{
		public int Width => 8;
		public int Height => 8;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Path;
		public float GetCellHeight( int x, int y ) => 0;
	}

	[TestMethod]
	public void GuestsOnAClearedPathCellStepOff()
	{
		var park = new Park();
		park.Path( (2, 5), (8, 5) );
		var guests = new GuestSimulation( park.Grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		park.Objects.ObjectPlaced += item => guests.SetDownOffCells( item.Cells.Select( cell => (cell.X, cell.Y) ).ToHashSet() );
		var standing = guests.SpawnInPark( 5, 5 );
		var beside = guests.SpawnInPark( 6, 5 );
		Assert.IsNotNull( park.Objects.TryPlace( Bush(), 5, 5, 0, out _ ) );
		Assert.AreNotEqual( (5, 5), standing.Cell );
		Assert.IsTrue( park.Grid.IsWalkable( standing.Cell.X, standing.Cell.Y ), $"set down on a path cell, not {standing.Cell}" );
		Assert.AreEqual( 1, Math.Abs( standing.Cell.X - 5 ) + Math.Abs( standing.Cell.Y - 5 ), "the nearest path cell" );
		Assert.AreEqual( (6, 5), beside.Cell, "guests elsewhere stay" );
	}

	// ---- Overwrite priority ------------------------------------------------------------------------

	[TestMethod]
	public void AHigherOverwritePriorityDemolishesTheLowerObject()
	{
		var park = new Park();
		var bush = park.Objects.TryPlace( Bush(), 11, 11, 0, out _ )!;
		Assert.AreEqual( OriginalPlacementResult.Occupied, park.Objects.Check( Bush(), 11, 11, 0 ), "an equal priority refuses" );
		var ride = Ride();
		Assert.AreEqual( OriginalPlacementResult.Allowed, park.Objects.Check( ride, 10, 10, 0, out var plan ) );
		CollectionAssert.AreEqual( new[] { bush }, plan.Overwritten.ToArray() );
		var placed = park.Objects.TryPlace( ride, 10, 10, 0, out _ )!;
		Assert.IsTrue( bush.IsDeleted );
		CollectionAssert.AreEqual( new[] { placed }, park.Objects.Objects.ToArray() );
		Assert.AreEqual( ParkCellTypes.Object, park.Grid.Cells.RawTypeAt( 11, 11 ) );
		Assert.AreEqual( OriginalPlacementResult.Occupied, park.Objects.Check( Shop(), 10, 11, 0 ), "a ride does not give way to a shop (5 against 5)" );
		Assert.AreEqual( OriginalPlacementResult.Occupied, park.Objects.Check( Bush(), 11, 11, 0 ) );
	}

	// ---- The access cells -----------------------------------------------------------------------

	[TestMethod]
	public void ARideChecksItsEntranceAsAnObjectCellAndItsExitByThePathRule()
	{
		var park = new Park();
		var ride = Ride();
		// The ride at anchor (10, 10): entrance (11, 10) opens to (11, 9), exit (11, 12) to (11, 13).
		var points = ObjectFootprint.GetAccessPoints( ride.Shape, 10, 10, 0 ).ToArray();
		CollectionAssert.AreEquivalent( new[] { (11, 9), (11, 13) }, points.Select( point => (point.OutsideX, point.OutsideY) ).ToArray() );
		park.Path( (8, 9), (14, 9) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, park.Objects.Check( ride, 10, 10, 0, out var plan ), "a plain path before the entrance" );
		Assert.AreEqual( 0, plan.ClearedPaths.Count, "the access cell's path stays (OpenTPW lays no queue cell there, RIDES-028)" );
		park.Fix( 11, 9 );
		Assert.AreEqual( OriginalPlacementResult.AccessBlocked, park.Objects.Check( ride, 10, 10, 0 ), "the object rule refuses a NoModify path (op 0x204)" );
		// The exit's cell takes the path rule: a NoModify path is fine there, a queue cell or an unowned cell is not.
		Assert.AreEqual( OriginalPlacementResult.Allowed, park.Objects.Check( ride, 10, 6, 0 ), "the exit on the NoModify path (11, 9)" );
		park.Grid.Cells.SetType( 11, 22, ParkCellType.Queue );
		Assert.AreEqual( OriginalPlacementResult.AccessBlocked, park.Objects.Check( ride, 10, 19, 0 ) );
		park.Grid.Cells.SetFlags( 20, 22, ParkCellFlags.Unowned );
		Assert.AreEqual( OriginalPlacementResult.AccessBlocked, park.Objects.Check( ride, 19, 19, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, park.Objects.Check( ride, 10, 29, 0 ), "an exit off the map (0x5299E2)" );
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, park.Objects.Check( ride, 10, 0, 0 ), "an entrance off the map (0x529744)" );
		// A lower-priority object on an access cell gives way; a ride there refuses.
		var bush = park.Objects.TryPlace( Bush(), 21, 9, 0, out _ )!;
		Assert.AreEqual( OriginalPlacementResult.Allowed, park.Objects.Check( ride, 20, 10, 0, out plan ) );
		CollectionAssert.AreEqual( new[] { bush }, plan.Overwritten.ToArray() );
		park.Objects.TryPlace( ride, 20, 10, 0, out _ );
		Assert.IsTrue( bush.IsDeleted, "the commit's op 0x87 on the access cell demolishes it" );
		Assert.AreEqual( OriginalPlacementResult.AccessBlocked, park.Objects.Check( ride, 20, 13, 0 ), "an entrance facing the other ride's exit cell (21, 12)" );
	}

	[TestMethod]
	public void AShopChecksItsEntranceByThePathRuleOnly()
	{
		var park = new Park();
		var shop = Shop();
		park.Path( (8, 9), (14, 9) );
		park.Fix( 10, 9 );
		Assert.AreEqual( OriginalPlacementResult.Allowed, park.Objects.Check( shop, 10, 10, 0 ), "without Info.HasQueue the entrance's cell takes the path rule (0x5298B9)" );
		park.Grid.Cells.SetType( 15, 9, ParkCellType.Queue );
		Assert.AreEqual( OriginalPlacementResult.AccessBlocked, park.Objects.Check( shop, 15, 10, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, park.Objects.Check( shop, 3, 0, 0 ) );
	}

	// ---- The path and queue tools ---------------------------------------------------------------

	[TestMethod]
	public void APathCoversSceneryButNoRide()
	{
		var park = new Park();
		var bush = park.Objects.TryPlace( Bush(), 6, 5, 0, out _ )!;
		var pillar = park.Objects.TryPlace( Pillar(), 9, 5, 0, out _ )!;
		Assert.AreEqual( CellBuildResult.Ok, park.Paths.CheckCell( 6, 5 ), "priority 1 is below the path tool's 2" );
		var segment = park.Paths.BuildSegment( (4, 5), (12, 5) );
		Assert.IsTrue( segment.Completed, $"{segment.StoppedBy}" );
		Assert.IsTrue( bush.IsDeleted && pillar.IsDeleted, "the line's objects are demolished whole" );
		Assert.IsTrue( Enumerable.Range( 4, 9 ).All( x => park.Grid.IsWalkable( x, 5 ) ) );
		Assert.AreEqual( 0, park.Grid.Cells.RawTypeAt( 9, 6 ), "the pillar's cells off the line are emptied" );
		park.Objects.TryPlace( Ride(), 6, 10, 0, out _ );
		Assert.AreEqual( CellBuildResult.Occupied, park.Paths.CheckCell( 7, 11 ), "a ride (priority 5) refuses a path" );
	}

	[TestMethod]
	public void AQueueCoversScenery()
	{
		var park = new Park();
		var bush = park.Objects.TryPlace( Bush(), 5, 5, 0, out _ )!;
		var ride = new RideVisitorBridge( 11, "Queue ride", RideVisitorKind.Ride, 1, 0, 10 )
		{
			EntranceCell = (5, 6),
			QueueFrontCell = (5, 5),
			QueueEntranceDirection = 2,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 1, 30 )
		};
		Assert.AreEqual( QueueBuildResult.Blocked, QueuePaths.CheckExtend( park.Grid, ride, 5, 5, park.QueueBlocked ), "without the priority test the bush blocks" );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.CheckExtend( park.Grid, ride, 5, 5, park.QueueBlocked, park.QueueGivesWay ) );
		Assert.AreEqual( QueueBuildResult.Ok, QueuePaths.TryExtend( park.Grid, ride, 5, 5, park.QueueBlocked, park.QueueGivesWay, park.Demolish ) );
		Assert.IsTrue( bush.IsDeleted );
		Assert.IsTrue( park.Grid.IsQueue( 5, 5 ) );
		var shop = park.Objects.TryPlace( Shop(), 5, 3, 0, out _ );
		Assert.IsNotNull( shop );
		Assert.AreEqual( QueueBuildResult.Blocked, QueuePaths.CheckExtend( park.Grid, ride, 5, 4, park.QueueBlocked, park.QueueGivesWay ), "a shop (5) is above the queue tool's 3" );
	}
}
