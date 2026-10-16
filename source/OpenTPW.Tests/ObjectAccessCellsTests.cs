using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The cells outside an entrance and an exit (RIDES-028): tp.exe's placement 0x528A70 validates them (ops 0x204 and 0x201) and lays them itself, a queue cell outside a
/// queue ride's entrance and path cells outside its exit and outside any other object's entrance, all NoModify and free. Synthetic dummy-only models: no GPU.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectAccessCellsTests
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
		root = Path.Combine( Path.GetTempPath(), "opentpw-access-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "synthetic" ) );
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "synthetic/model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "synthetic/script.RSE" ), new RideVMTests.Asm().I( Opcode.END ).Build( Enum.GetNames<RideVariables>() ) );
		FileSystem = new BaseFileSystem( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() ) entity.Delete();
		Entity.All = previousEntities; FileSystem = previousFiles; Log = previousLog;
		Directory.Delete( root, true );
	}

	private ObjectCatalogEntry Entry( int id, int priority, bool hasQueue, params string[] shape )
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name Synthetic{id}\nInfo.IsChoosable 1\nInfo.OverwritePriority {priority}\n" + (hasQueue ? "Info.HasQueue 1\n" : ""), $"synthetic{id}.sam" ) } );
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/synthetic", settings, new ObjectShape( shape ), "/synthetic/model.MD2", null, "/synthetic/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), FileSystem, null );
	}

	/// <summary>A park the way <see cref="Level"/> wires it: the cell map types footprints, the build check reserves by cell type and validates the access cells.</summary>
	private sealed class Rig
	{
		public readonly ParkCellMap Cells = new( ParkPlacement.TileCount, ParkPlacement.TileCount );
		public readonly ParkObjects Objects;
		public bool Laying;

		public Rig( params ObjectCatalogEntry[] entries )
		{
			var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "jungle", entries }, null )!;
			Objects = new ParkObjects( catalog, new SandboxParkGrid() )
			{
				IsReserved = ( x, y ) => Cells.RawTypeAt( x, y ) is var type && type is not (0 or ParkCellTypes.Object or ParkCellTypes.Entrance or ParkCellTypes.Exit)
					&& !(type == (byte)ParkCellType.Path && !Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.NoModify )),
				IsReservedForPath = ( x, y ) => Cells.RawTypeAt( x, y ) is var type && type is not (0 or (byte)ParkCellType.Path),
				IsUnowned = ( x, y ) => Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ),
				AccessCells = ObjectAccessCells.CellsToCheck
			};
			Objects.ObjectPlaced += item => Cells.WriteFootprint( item.Cells, Level.OwnerWord( item ), item.Placement.Rotation );
			Objects.ObjectRemoved += item =>
			{
				Cells.ClearFootprint( item.Cells );
				ObjectAccessCells.ClearQueue( Cells, Level.OwnerWord( item ) );
			};
			Objects.ObjectPlaced += item =>
			{
				if ( Laying )
					ObjectAccessCells.Lay( Cells, item.AccessPoints, Level.OwnerWord( item ), item.Entry.HasQueue );
			};
		}

		public OriginalObject? Place( ObjectCatalogEntry entry, int x, int y, int rotation = 0 )
		{
			Laying = true;
			try
			{
				return Objects.TryPlace( entry, x, y, rotation, out _ );
			}
			finally
			{
				Laying = false;
			}
		}
	}

	// The shape "*2S" on one row: entrance '2' opens towards −Y (outside cell (x + 1, y − 1)), exit 'S' towards +Y (outside cell (x + 2, y + 1)).

	[TestMethod]
	public void AQueueRideLaysAQueueCellOutsideItsEntranceAndAPathCellOutsideItsExit()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var rig = new Rig( ride );
		var item = rig.Place( ride, 4, 5 )!;
		Assert.IsNotNull( item );
		var owner = ParkCellMap.CellWord( 4, 5 );
		var cells = rig.Cells;
		// the entrance (5, 5): type 9, linked towards its outside cell (bit 1) with the same queue link
		Assert.AreEqual( (byte)9, cells.RawTypeAt( 5, 5 ) );
		Assert.AreEqual( 0x01, cells.LinksAt( 5, 5 ) );
		Assert.AreEqual( 0x01, cells.QueueLinkAt( 5, 5 ) );
		// the queue cell (5, 4): linked back (bit 16), owner and NoModify
		Assert.AreEqual( (byte)ParkCellType.Queue, cells.RawTypeAt( 5, 4 ) );
		Assert.AreEqual( 0x10, cells.LinksAt( 5, 4 ) );
		Assert.AreEqual( 0x10, cells.QueueLinkAt( 5, 4 ) );
		Assert.AreEqual( owner, cells.OwnerAt( 5, 4 ) );
		Assert.AreEqual( ParkCellFlags.NoModify, cells.FlagsAt( 5, 4 ) );
		// the exit (6, 5): type 10, its queue link the rotated direction byte (0x10); the path cell outside it (6, 6)
		Assert.AreEqual( (byte)10, cells.RawTypeAt( 6, 5 ) );
		Assert.AreEqual( 0x10, cells.QueueLinkAt( 6, 5 ) );
		Assert.AreEqual( (byte)ParkCellType.Path, cells.RawTypeAt( 6, 6 ) );
		Assert.AreEqual( ParkCellFlags.NoModify, cells.FlagsAt( 6, 6 ) );
		Assert.AreEqual( 0, cells.OwnerAt( 6, 6 ), "a path cell has no owner" );
		Assert.AreEqual( 0x01, cells.LinksAt( 6, 6 ) & 0x01, "the path cell links to the exit cell, whose queue link points at it" );
	}

	[TestMethod]
	public void AShopLaysOnlyAPathCellOutsideItsEntrance()
	{
		var shop = Entry( 1203, 5, false, "*2S" );
		var rig = new Rig( shop );
		rig.Place( shop, 4, 5 );
		Assert.AreEqual( (byte)ParkCellType.Path, rig.Cells.RawTypeAt( 5, 4 ) );
		Assert.AreEqual( ParkCellFlags.NoModify, rig.Cells.FlagsAt( 5, 4 ) );
		Assert.AreEqual( 0x10, rig.Cells.LinksAt( 5, 4 ) & 0x10, "the path cell links to the entrance cell, whose queue link (the rotated 0x10) leads away from it" );
		Assert.AreEqual( 0, rig.Cells.RawTypeAt( 6, 6 ), "without the queue option the exit gets nothing" );
		Assert.AreEqual( 0x10, rig.Cells.QueueLinkAt( 5, 5 ), "the entrance's own queue link is its rotated direction byte" );
	}

	[TestMethod]
	public void ThePlacementReplacesAPlainPathCellOutsideTheEntranceAndUnlinksIt()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var rig = new Rig( ride );
		var cells = rig.Cells;
		for ( var x = 3; x <= 7; x++ )
		{
			cells.SetType( x, 4, ParkCellType.Path );
			cells.SetLinks( x, 4, (byte)((x > 3 ? 0x40 : 0) | (x < 7 ? 0x04 : 0)) );
		}
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( ride, 4, 5, 0 ), "an entrance may face a path cell without NoModify" );
		rig.Place( ride, 4, 5 );
		Assert.AreEqual( (byte)ParkCellType.Queue, cells.RawTypeAt( 5, 4 ) );
		Assert.AreEqual( 0, cells.LinksAt( 4, 4 ) & 0x04, "the path cell beside the queue cell loses its link to it" );
		Assert.AreEqual( 0, cells.LinksAt( 6, 4 ) & 0x40 );
		Assert.AreEqual( 0x10, cells.LinksAt( 5, 4 ), "only the link back to the entrance" );
	}

	[TestMethod]
	public void AFootprintGivesWayToAPlainPathCellButNotToANoModifyOne()
	{
		var ride = Entry( 1100, 5, true, "***", "*2S" );
		var rig = new Rig( ride );
		rig.Cells.SetType( 4, 6, ParkCellType.Path );
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( ride, 4, 5, 0 ), "a plain path cell under the footprint is cleared by the commit" );
		rig.Cells.SetFlags( 4, 6, ParkCellFlags.NoModify );
		Assert.AreEqual( OriginalPlacementResult.Occupied, rig.Objects.Check( ride, 4, 5, 0 ), "the validator refuses NoModify for every object type (0x5356A6)" );
	}

	[TestMethod]
	public void TheOutsideCellsAreValidatedByTheirRule()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var shop = Entry( 1203, 5, false, "*2S" );
		var rig = new Rig( ride, shop );
		var cells = rig.Cells;
		// the entrance's outside cell (5, 4) of the queue ride is validated as an object cell: a NoModify path refuses it
		cells.SetType( 5, 4, ParkCellType.Path );
		cells.SetFlags( 5, 4, ParkCellFlags.NoModify );
		Assert.AreEqual( OriginalPlacementResult.Occupied, rig.Objects.Check( ride, 4, 5, 0 ) );
		// the shop's entrance cell is validated by the path rule: a NoModify path cell passes
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( shop, 4, 5, 0 ) );
		// an object cell outside the exit refuses (the path rule), where an object cell of lower priority outside the entrance gives way
		cells.SetType( 5, 4, ParkCellType.Empty );
		cells.SetFlags( 5, 4, ParkCellFlags.None );
		var bush = Entry( 1401, 1, false, "*" );
		var withBush = new Rig( ride, bush );
		withBush.Objects.Add( bush, 5, 4, 0 );
		Assert.AreEqual( OriginalPlacementResult.Allowed, withBush.Objects.Check( ride, 4, 5, 0, out var gives ), "a bush in front of the entrance gives way" );
		Assert.AreEqual( 1, gives.Count );
		withBush.Objects.Remove( withBush.Objects.FindAt( 5, 4 )! );
		withBush.Objects.Add( bush, 6, 6, 0 );
		Assert.AreEqual( OriginalPlacementResult.Occupied, withBush.Objects.Check( ride, 4, 5, 0 ), "an object outside the exit refuses, whatever its priority" );
		// off the map and unowned land refuse
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, rig.Objects.Check( ride, 4, 0, 0 ), "the entrance's outside cell lies off the map (0x529744)" );
		rig.Cells.SetFlags( 6, 6, ParkCellFlags.Unowned );
		Assert.AreEqual( OriginalPlacementResult.NotOwned, rig.Objects.Check( ride, 4, 5, 0 ) );
	}

	[TestMethod]
	public void ARotatedObjectLaysItsCellsOnTheRotatedSide()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var rig = new Rig( ride );
		var item = rig.Place( ride, 10, 10, 90 )!;
		var points = item.AccessPoints.ToDictionary( point => point.Kind );
		Assert.AreEqual( (byte)ParkCellType.Queue, rig.Cells.RawTypeAt( points[ObjectCellKind.Entrance].OutsideX, points[ObjectCellKind.Entrance].OutsideY ) );
		Assert.AreEqual( (byte)ParkCellType.Path, rig.Cells.RawTypeAt( points[ObjectCellKind.Exit].OutsideX, points[ObjectCellKind.Exit].OutsideY ) );
		// rotation 90 turns the entrance symbol's direction byte 0x10 into 0x04 (0x4D8C20)
		Assert.AreEqual( 0x04, ObjectFootprint.RotateDirectionByte( 0x10, 90 ) );
		Assert.AreEqual( 0x01, ObjectFootprint.RotateDirectionByte( 0x10, 180 ) );
		Assert.AreEqual( 0x40, ObjectFootprint.RotateDirectionByte( 0x10, 270 ) );
		Assert.AreEqual( 0x40, ObjectFootprint.RotateDirectionByte( 0x01, 90 ) );
	}

	// ---------------------------------------------------------------- add-ons (RIDES-011, TRACK-008) and track ends

	/// <summary>The eight-tile water loop of the circuit tests on a rig's cell map: station (10, 10), corners (14, 10), (14, 12), (8, 12), (8, 10); the straights (12, 12) and (10, 12) lie between the corners.</summary>
	private static (TrackBuilder Builder, TrackStation Station, int Owner) Loop( ParkCellMap cells )
	{
		var builder = new TrackBuilder( cells, TrackFamily.Water, () => null, new SandboxParkGrid() );
		var station = TrackStation.FromPlacement( 10, 10, 0 );
		builder.PlaceStation( station );
		var owner = builder.OwnerOf( station );
		var writer = new TrackLineWriter( builder, null, owner );
		writer.Commit( (14, 10), (14, 12) );
		writer.Commit( (14, 12), (8, 12) );
		writer.Commit( (8, 12), (8, 10) );
		return (builder, station, owner);
	}

	[TestMethod]
	public void AnAddOnCellStandsOnAStraightUnflaggedTileOfItsRidesTrack()
	{
		var rig = new Rig();
		var (builder, station, owner) = Loop( rig.Cells );
		foreach ( var (x, y) in TrackStation.TileCellsOf( 12, 12 ) )
			Assert.IsTrue( builder.CanStandAddOn( x, y, owner ), $"({x}, {y}) of the straight tile (12, 12)" );
		Assert.IsFalse( builder.CanStandAddOn( 12, 12, owner + 1 ), "another ride's track (the head's owner word, 0x535998)" );
		Assert.IsFalse( builder.CanStandAddOn( 14, 12, owner ), "a corner (0x53AE00)" );
		Assert.IsFalse( builder.CanStandAddOn( 20, 20, owner ), "free ground holds no track cell (0x53AD30)" );
		rig.Cells.SetFlags( 13, 13, ParkCellFlags.AddOn );
		Assert.IsFalse( builder.CanStandAddOn( 13, 13, owner ), "a cell flagged 0x10 already carries an add-on (0x535961)" );
		Assert.IsTrue( builder.CanStandAddOn( 12, 12, owner ), "the other cells of the tile still pass" );
	}

	[TestMethod]
	public void AnAddOnIsPlacedOnItsTrackFlagsItAndTheWalkReadsTheFlagIntoItsSections()
	{
		var jump = Entry( 1500, 3, false, "++", "++" );
		var rig = new Rig( jump );
		var (builder, station, owner) = Loop( rig.Cells );
		Assert.AreEqual( OriginalPlacementResult.Occupied, rig.Objects.Check( jump, 12, 12, 0 ), "without the add-on rule the cells are ordinary object cells, and track refuses them" );
		rig.Objects.AddOnCellRule = ( x, y ) => builder.CanStandAddOn( x, y, owner );
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( jump, 12, 12, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Blocked, rig.Objects.Check( jump, 13, 12, 0 ), "half on the corner tile" );
		Assert.AreEqual( OriginalPlacementResult.Blocked, rig.Objects.Check( jump, 20, 20, 0 ), "on free ground" );
		var circuitBefore = TrackCircuitWalker.Walk( builder, station );
		Assert.IsTrue( circuitBefore.IsValid );
		Assert.AreEqual( 9, circuitBefore.Steps.Single( step => (step.X, step.Y) == (12, 12) ).Kind, "a plain straight is kind 9" );
		var item = rig.Place( jump, 12, 12 );
		Assert.IsNotNull( item );
		foreach ( var (x, y) in TrackStation.TileCellsOf( 12, 12 ) )
		{
			Assert.AreEqual( TrackCell.FlagAddOn, rig.Cells.Tracks.Get( x, y ).Flags, $"track cell ({x}, {y})" );
			Assert.AreEqual( ParkCellFlags.AddOn, rig.Cells.FlagsAt( x, y ) );
		}
		Assert.AreEqual( OriginalPlacementResult.Blocked, rig.Objects.Check( jump, 12, 12, 0 ), "a second add-on does not fit on the flagged tile" );
		var circuit = TrackCircuitWalker.Walk( builder, station );
		Assert.IsTrue( circuit.IsValid );
		Assert.AreEqual( 12, circuit.Steps.Single( step => (step.X, step.Y) == (12, 12) ).Kind, "the flagged straight builds the add-on kind 12 (0x52AC68)" );
		Assert.AreEqual( 9, circuit.Steps.Single( step => (step.X, step.Y) == (10, 12) ).Kind );
		rig.Objects.Remove( item! );
		foreach ( var (x, y) in TrackStation.TileCellsOf( 12, 12 ) )
			Assert.AreEqual( 0, rig.Cells.Tracks.Get( x, y ).Flags & TrackCell.FlagAddOn, "selling the add-on takes the flag off the tile" );
		Assert.AreEqual( 9, TrackCircuitWalker.Walk( builder, station ).Steps.Single( step => (step.X, step.Y) == (12, 12) ).Kind );
	}

	[TestMethod]
	public void ATrackEndNeedsTheCellBeyondItOnTheMap()
	{
		// [BIN:TPW-EXE:0x005295A8 track end beyond cell] a coaster station "<*>" on one row: '<' opens towards −X, '>' towards +X
		var station = Entry( 1185, 5, true, "<2>" );
		var rig = new Rig( station );
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( station, 10, 10, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, rig.Objects.Check( station, 0, 10, 0 ), "no cell beyond the '<' at x = 0" );
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, rig.Objects.Check( station, ParkPlacement.TileCount - 3, 10, 0 ), "none beyond the '>' on the last column" );
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( station, 1, 10, 0 ) );
		// rotated by 90 the ends open towards ±Y
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, rig.Objects.Check( station, 10, ParkPlacement.TileCount - 1, 90 ) );
	}

	[TestMethod]
	public void DemolishingARideClearsItsQueueSoItCanBeRebuiltOnTheSameSite()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var rig = new Rig( ride );
		var item = rig.Place( ride, 4, 5 )!;
		// the queue tool lays two more cells out of the laid one, as the owner word marks them
		var cells = rig.Cells;
		foreach ( var x in new[] { 4, 3 } )
		{
			cells.SetType( x, 4, ParkCellType.Queue );
			cells.SetOwner( x, 4, Level.OwnerWord( item ) );
		}
		cells.SetType( 10, 10, ParkCellType.Queue );
		cells.SetOwner( 10, 10, ParkCellMap.CellWord( 20, 20 ) );
		Assert.AreEqual( OriginalPlacementResult.Occupied, rig.Objects.Check( ride, 4, 5, 0 ), "the laid queue cell and the footprint are in the way" );
		rig.Objects.Remove( item );
		Assert.AreEqual( 0, cells.RawTypeAt( 5, 4 ), "the laid queue cell goes with the ride" );
		Assert.AreEqual( 0, cells.RawTypeAt( 4, 4 ) | cells.RawTypeAt( 3, 4 ) );
		Assert.AreEqual( (byte)ParkCellType.Queue, cells.RawTypeAt( 10, 10 ), "another ride's queue stays" );
		Assert.AreEqual( (byte)ParkCellType.Path, cells.RawTypeAt( 6, 6 ), "the path stub outside the exit stays" );
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( ride, 4, 5, 0 ), "the freed site can be built on again" );
		Assert.IsNotNull( rig.Place( ride, 4, 5 ) );
		Assert.AreEqual( (byte)ParkCellType.Queue, cells.RawTypeAt( 5, 4 ) );
	}
}
