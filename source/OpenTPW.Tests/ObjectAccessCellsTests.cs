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
		public readonly ObjectCatalog Catalog;
		public bool Laying;

		public Rig( params ObjectCatalogEntry[] entries )
		{
			var catalog = Catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "jungle", entries }, null )!;
			Objects = new ParkObjects( catalog, new SandboxParkGrid() )
			{
				IsReserved = ( x, y ) => Cells.RawTypeAt( x, y ) is var type && type is not (0 or (byte)ParkCellType.Path or ParkCellTypes.Object or ParkCellTypes.Entrance or ParkCellTypes.Exit),
				IsUnowned = ( x, y ) => Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ),
				PathAt = ( x, y ) => Level.ObjectPathAt( Cells, x, y )
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

	[DataTestMethod, DataRow( 0 ), DataRow( 90 ), DataRow( 180 ), DataRow( 270 )]
	public void TypedQueueRegistrationUsesFirstOwnedEntranceAndRebindsAfterCellSave( int rotation )
	{
		var entry = Entry( 1235, 0, true, "*22" );
		var rig = new Rig( entry );
		var item = rig.Place( entry, 8, 8, rotation );
		Assert.IsNotNull( item );
		var entrances = item!.AccessPoints.Where( point => point.Kind == ObjectCellKind.Entrance ).ToArray();
		Assert.AreEqual( 2, entrances.Length );
		var first = entrances[0]; // 0x4DE040 reads the first entrance's link byte (the constructor's +0x36)
		var grid = new GuestPathGrid( rig.Cells );
		grid.SetPath( 1, 1, true );
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		Assert.IsTrue( Level.RegisterWithGuests( item.Runtime, item.AccessPoints, simulation, rotation,
			Level.OwnerWord( item ), ownExitCell: true,
			queueEntryResolver: () => Level.ResolveQueueEntrance( rig.Objects, rig.Cells, Level.OwnerWord( item ) ) ) );
		Assert.AreEqual( (first.OutsideX, first.OutsideY), item.Visitors.QueueFrontCell!.Value );
		Assert.AreNotEqual( (entrances[1].OutsideX, entrances[1].OutsideY), item.Visitors.QueueFrontCell.Value );
		var saved = ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( ParkLayoutSnapshot.CaptureSnapshot( rig.Cells, rig.Objects ) ) );
		var restoredCells = ParkLayoutSnapshot.RestoreCellMap( saved );
		item.Visitors.QueueEntryResolver = () => Level.ResolveQueueEntrance( rig.Objects, restoredCells, Level.OwnerWord( item ) );
		item.Visitors.QueueFrontCell = (1, 1);
		var restoredGrid = new GuestPathGrid( restoredCells );
		restoredGrid.SetPath( 1, 1, true );
		item.Visitors.RecomputeQueue( restoredGrid );
		// [BIN:TPW-EXE:0x004DE040 first entrance] the placement keeps the last entrance (0x5293A1) and links that one (0x5297BD), the selector reads the first one's link byte (+0x36): a two-entrance shape has no front
		Assert.IsTrue( item.Visitors.IsZeroFront, "the first entrance carries no link byte" );
		Assert.AreEqual( 0, item.Visitors.QueueSizeInCells );
		restoredCells.SetOwner( first.X, first.Y, 0 );
		Assert.IsNull( Level.ResolveQueueEntrance( rig.Objects, restoredCells, Level.OwnerWord( item ) ), "ownership validation belongs to entry reconstruction" );
	}

	[TestMethod]
	public void APlacedRideWithAQueueIsEvaluatedTwiceAndItsFrontBlockStamped()
	{
		// tp.exe: 0x4DE1F0 runs inside the placement commit when the type has a queue (0x529890) and again at the click's tail (0x52537A, the Move tool's 0x5259AE); each raises the stamp counter and stamps the back cell's block
		var queued = Entry( 1236, 0, true, "*2S" );
		var shop = Entry( 1237, 0, false, "*2S" );
		var rig = new Rig( queued, shop );
		var item = rig.Place( queued, 8, 8 )!;
		var grid = new GuestPathGrid( rig.Cells );
		grid.SetPath( 1, 1, true );
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		Assert.IsTrue( Level.RegisterWithGuests( item.Runtime, item.AccessPoints, simulation, 0, Level.OwnerWord( item ), ownExitCell: true,
			queueEntryResolver: () => Level.ResolveQueueEntrance( rig.Objects, rig.Cells, Level.OwnerWord( item ) ) ) );
		var front = item.Visitors.QueueFrontCell!.Value;
		Assert.IsTrue( grid.IsQueue( front.X, front.Y ) );
		var before = rig.Cells.StampCounter;
		Level.ReevaluatePlacedQueue( simulation, item );
		Assert.AreEqual( before + 2, rig.Cells.StampCounter, "two evaluations" );
		Assert.AreEqual( before + 2, rig.Cells.BlockStamp( front.X / 16, front.Y / 16 ), "the second stamps the front cell's block" );

		// a type without a queue takes neither (0x529890 is the queue option's branch, and the tail is the entrance-bearing placement's)
		var other = rig.Place( shop, 20, 20 )!;
		Assert.IsTrue( Level.RegisterWithGuests( other.Runtime, other.AccessPoints, simulation, 0, Level.OwnerWord( other ), ownExitCell: true ) );
		var counter = rig.Cells.StampCounter;
		Level.ReevaluatePlacedQueue( simulation, other );
		Assert.AreEqual( counter, rig.Cells.StampCounter );
	}

	[TestMethod]
	public void AClearedPathCellBesideAnOwnedEntranceReportsItsRideAndAQueueCellDoesNot()
	{
		// tp.exe 0x5367A0 at 0x53694B: the path case (type 1) re-evaluates the ride of a type-9 neighbour that carries an owner word, on a cardinal bit; the queue case (types 3 and 9) calls nothing
		var shop = Entry( 1238, 0, false, "*2S" );
		var queued = Entry( 1239, 0, true, "*2S" );
		var rig = new Rig( shop, queued );
		var item = rig.Place( shop, 8, 8 )!;
		var entrance = item.AccessPoints.Single( point => point.Kind == ObjectCellKind.Entrance );
		var outside = (X: entrance.OutsideX, Y: entrance.OutsideY);
		Assert.AreEqual( (byte)ParkCellType.Path, rig.Cells.RawTypeAt( outside.X, outside.Y ) );
		Assert.AreNotEqual( 0, rig.Cells.LinksAt( outside.X, outside.Y ) & PathLinkWriter.Opposite( (byte)(rig.Cells.LinksAt( entrance.X, entrance.Y ) & 0x55) ), "the path cell is linked to the entrance" );
		var seen = new List<ushort>();
		PathLinkWriter.Unlink( rig.Cells, outside.X, outside.Y, cardinalOnly: false, new LinkEffects( null, null, owner => seen.Add( owner ) ) );
		CollectionAssert.AreEqual( new[] { Level.OwnerWord( item ) }, seen );

		var ride = rig.Place( queued, 20, 20 )!;
		var gate = ride.AccessPoints.Single( point => point.Kind == ObjectCellKind.Entrance );
		Assert.AreEqual( (byte)ParkCellType.Queue, rig.Cells.RawTypeAt( gate.OutsideX, gate.OutsideY ) );
		seen.Clear();
		PathLinkWriter.Unlink( rig.Cells, gate.OutsideX, gate.OutsideY, cardinalOnly: true, new LinkEffects( null, null, owner => seen.Add( owner ) ) );
		Assert.AreEqual( 0, seen.Count, "a queue cell's clear re-evaluates nothing" );
	}

	[TestMethod]
	public void EveryRouteThatClearsAPathCellBesideAnOwnedEntranceRunsTheReevaluation()
	{
		// tp.exe 0x5367A0 at 0x53694B is the one clear every route reaches: the guest grid's queue setter and the footprint clear report the ride as the bulldozer's clear does
		var shop = Entry( 1238, 0, false, "*2S" );
		var rig = new Rig( shop );
		var item = rig.Place( shop, 8, 8 )!;
		var entrance = item.AccessPoints.Single( point => point.Kind == ObjectCellKind.Entrance );
		var outside = (X: entrance.OutsideX, Y: entrance.OutsideY);
		var seen = new List<ushort>();
		rig.Cells.EntranceReevaluation = owner => seen.Add( owner );
		var grid = new GuestPathGrid( rig.Cells );
		grid.SetQueue( outside.X, outside.Y, -1 );
		CollectionAssert.AreEqual( new[] { Level.OwnerWord( item ) }, seen, "a queue set over the path cell re-evaluates the ride beside it" );
	}

	[TestMethod]
	public void ATrackTileOverAPathCellBesideAnOwnedEntranceReevaluatesItsRide()
	{
		// tp.exe 0x538FC0 / 0x538DC2 clear a path cell through 0x5367A0 (0x539190, 0x53919B): the entrance's ride is re-evaluated
		var shop = Entry( 1238, 0, false, "*2S" );
		var rig = new Rig( shop );
		var item = rig.Place( shop, 8, 8 )!;
		var entrance = item.AccessPoints.Single( point => point.Kind == ObjectCellKind.Entrance );
		var seen = new List<ushort>();
		rig.Cells.EntranceReevaluation = owner => seen.Add( owner );
		var builder = new TrackBuilder( rig.Cells, TrackFamily.Kart, () => null, new SandboxParkGrid() );
		typeof( TrackBuilder ).GetMethod( "ClearPath", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( builder, new object[] { entrance.OutsideX, entrance.OutsideY } );
		CollectionAssert.AreEqual( new[] { Level.OwnerWord( item ) }, seen );
	}

	[DataTestMethod, DataRow( 0 ), DataRow( 90 ), DataRow( 180 ), DataRow( 270 )]
	public void RideExitUsesTheEntranceOwnerExitAndDirectionByteAfterRealPlacement( int rotation )
	{
		var entry = Entry( 1234, 0, true, "*2S" );
		var rig = new Rig( entry );
		var item = rig.Place( entry, 8, 8, rotation );
		Assert.IsNotNull( item );
		var entrance = item!.AccessPoints.Single( point => point.Kind == ObjectCellKind.Entrance );
		var exit = item.AccessPoints.Single( point => point.Kind == ObjectCellKind.Exit );
		Assert.AreEqual( (entrance.X, entrance.Y), Level.ResolveQueueEntrance( rig.Objects, rig.Cells, Level.OwnerWord( item ) )!.Value );
		var expectedDirection = rig.Cells.QueueLinkAt( exit.X, exit.Y );
		Assert.AreNotEqual( (byte)0, expectedDirection, "the real footprint writer supplies the exit direction" );
		var importedCells = new ParkCellMap( rig.Cells.Width, rig.Cells.Height );
		importedCells.WriteFootprint( item.Cells, Level.OwnerWord( item ), rotation );
		Assert.AreEqual( (exit.X, exit.Y, expectedDirection), Level.ResolveRideExit( rig.Objects, importedCells, entrance.X, entrance.Y )!.Value, "the original import's footprint reconstruction supplies the same exit direction" );
		var snapshot = ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( ParkLayoutSnapshot.CaptureSnapshot( rig.Cells, rig.Objects ) ) );
		var restoredCells = ParkLayoutSnapshot.RestoreCellMap( snapshot );
		Assert.AreEqual( (exit.X, exit.Y, expectedDirection), Level.ResolveRideExit( rig.Objects, restoredCells, entrance.X, entrance.Y )!.Value, "a snapshot restores the direction independently of neighbours" );
		var owner = rig.Cells.OwnerAt( entrance.X, entrance.Y );
		rig.Cells.SetOwner( entrance.X, entrance.Y, 0 );
		Assert.IsNull( Level.ResolveRideExit( rig.Objects, rig.Cells, entrance.X, entrance.Y ), "unowned entrance has no ride" );
		rig.Cells.SetOwner( entrance.X, entrance.Y, ParkCellMap.CellWord( 20, 20 ) );
		Assert.IsNull( Level.ResolveRideExit( rig.Objects, rig.Cells, entrance.X, entrance.Y ), "an unmatched owner cannot select a nearby ride" );
		rig.Cells.SetOwner( entrance.X, entrance.Y, owner );
		// Neighbour links deliberately disagree: recenter reads +0x0D, never +0x0C.
		rig.Cells.SetLinks( exit.X, exit.Y, 0x55 );
		Assert.AreEqual( (exit.X, exit.Y, expectedDirection), Level.ResolveRideExit( rig.Objects, rig.Cells, entrance.X, entrance.Y )!.Value );
		Assert.IsNull( Level.ResolveRideExit( rig.Objects, rig.Cells, item.Placement.X, item.Placement.Y ), "the anchor is not an entrance" );
		Assert.IsNull( Level.ResolveRideExit( rig.Objects, rig.Cells, exit.X, exit.Y ), "the exit is not the lookup entrance" );

		var field = new ModelHeightfield( 32, 32, 10, 10, 0, 0, Array.Empty<uint>(), new float[33 * 33], new uint[32 * 32] );
		var level = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var park = (OriginalPark)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		var guests = (GuestSimulation)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( GuestSimulation ) );
		void Set( object target, string property, object value ) => target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
		Set( park, "Heightfield", field ); Set( level, "OriginalPark", park ); Set( level, "Objects", rig.Objects );
		Set( guests, "Grid", new GuestPathGrid( rig.Cells ) ); Set( level, "Guests", guests );
		var start = OriginalParkPlacement.GetCellCenter( field, entrance.X, entrance.Y );
		var destination = OriginalParkPlacement.GetCellCenter( field, exit.X, exit.Y );
		var camera = new ParkCameraMode();
		camera.SetPose( new Vector3( start.X, start.Y, start.Z ), 90, -135 );
		level.RecentreParkCamera( camera );
		Assert.AreEqual( new Vector3( destination.X, destination.Y, destination.Z ), camera.Target );
		var (dx, dy) = GuestPathGrid.Directions[Level.RideExitDirection( expectedDirection )];
		var ahead = OriginalParkPlacement.GetCellCenter( field, exit.X + dx, exit.Y + dy );
		var expected = new ParkCameraMode(); expected.SetPose( camera.Target, 90, -135 ); expected.LookAlong( new System.Numerics.Vector2( ahead.X - destination.X, ahead.Y - destination.Y ) );
		Assert.AreEqual( expected.Yaw, camera.Yaw );
		foreach ( byte direction in new byte[] { 0, 5, 0x55 } )
		{
			rig.Cells.SetQueueLink( exit.X, exit.Y, direction );
			camera.SetPose( new Vector3( start.X, start.Y, start.Z ), 90, -135 );
			var yaw = camera.Yaw;
			level.RecentreParkCamera( camera );
			Assert.AreEqual( new Vector3( destination.X, destination.Y, destination.Z ), camera.Target );
			Assert.AreEqual( yaw, camera.Yaw, "zero or ambiguous cardinal direction preserves yaw" );
		}
		rig.Objects.Remove( item );
		Assert.IsNull( Level.ResolveRideExit( rig.Objects, rig.Cells, entrance.X, entrance.Y ) );
		Assert.AreEqual( (byte)0, rig.Cells.QueueLinkAt( exit.X, exit.Y ), "removal clears the exit direction" );
	}

	[TestMethod]
	public void RideExitWithoutAnExitDoesNotInventTheAnchorAsDestination()
	{
		var entry = Entry( 1235, 0, true, "*2*" );
		var rig = new Rig( entry );
		var item = rig.Place( entry, 8, 8 );
		Assert.IsNotNull( item );
		var entrance = item!.AccessPoints.Single( point => point.Kind == ObjectCellKind.Entrance );
		Assert.IsNull( Level.ResolveRideExit( rig.Objects, rig.Cells, entrance.X, entrance.Y ) );
		Assert.IsNull( Level.ResolveRideExit( rig.Objects, rig.Cells, -1, -1 ) );
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
		Assert.AreEqual( OriginalPlacementResult.Path, rig.Objects.Check( ride, 4, 5, 0 ), "the validator refuses NoModify for every object type (0x5356A6)" );
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
		Assert.AreEqual( OriginalPlacementResult.AccessBlocked, rig.Objects.Check( ride, 4, 5, 0 ) );
		// the shop's entrance cell is validated by the path rule: a NoModify path cell passes
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( shop, 4, 5, 0 ) );
		// an object cell outside the exit refuses (the path rule), where an object cell of lower priority outside the entrance gives way
		cells.SetType( 5, 4, ParkCellType.Empty );
		cells.SetFlags( 5, 4, ParkCellFlags.None );
		var bush = Entry( 1401, 1, false, "*" );
		var withBush = new Rig( ride, bush );
		withBush.Objects.Add( bush, 5, 4, 0 );
		Assert.AreEqual( OriginalPlacementResult.Allowed, withBush.Objects.Check( ride, 4, 5, 0, out var gives ), "a bush in front of the entrance gives way" );
		Assert.AreEqual( 1, gives.Overwritten.Count );
		withBush.Objects.Remove( withBush.Objects.FindAt( 5, 4 )! );
		withBush.Objects.Add( bush, 6, 6, 0 );
		Assert.AreEqual( OriginalPlacementResult.Allowed, withBush.Objects.Check( ride, 4, 5, 0 ), "a lower-priority object outside the exit gives way too (0x535D66 comes before the path rule)" );
		var other = Entry( 1402, 5, false, "*" );
		var withRide = new Rig( ride, other );
		withRide.Objects.Add( other, 6, 6, 0 );
		Assert.AreEqual( OriginalPlacementResult.AccessBlocked, withRide.Objects.Check( ride, 4, 5, 0 ), "an object of equal priority outside the exit refuses" );
		// off the map and unowned land refuse
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, rig.Objects.Check( ride, 4, 0, 0 ), "the entrance's outside cell lies off the map (0x529744)" );
		rig.Cells.SetFlags( 6, 6, ParkCellFlags.Unowned );
		Assert.AreEqual( OriginalPlacementResult.AccessBlocked, rig.Objects.Check( ride, 4, 5, 0 ) );
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

	// ---------------------------------------------------------------- add-ons (RIDES-011, docs/TRACKS.md "Add-ons") and track ends

	/// <summary>The eight-tile water loop of the circuit tests on a rig's cell map (station placed at (10, 9), whose first tile is (10, 10)), corners (14, 10), (14, 12), (8, 12), (8, 10); the straights (12, 12) and (10, 12) lie between the corners.</summary>
	private static (TrackBuilder Builder, TrackStation Station, int Owner) Loop( ParkCellMap cells )
	{
		var builder = new TrackBuilder( cells, TrackFamily.Water, () => null, new SandboxParkGrid() );
		var station = TrackStation.FromPlacement( 10, 9, 0 );
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
	public void AnAddOnCellThatAnotherObjectHoldsIsRefused()
	{
		var jump = Entry( 1500, 3, false, "++", "++" );
		var ride = Entry( 1150, 3, false, "**", "**" );
		var rig = new Rig( jump, ride );
		var (builder, _, owner) = Loop( rig.Cells );
		rig.Objects.AddOnCellRule = ( x, y ) => builder.CanStandAddOn( x, y, owner );
		// the original's track test 0x53AD30 counts the station tiles (types 13 and 18) as track, so the add-on rule alone passes the first station tile (10, 10)
		Assert.IsTrue( builder.CanStandAddOn( 10, 10, owner ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( jump, 10, 10, 0 ) );
		// [EXT:addon-not-on-station] the ride's own footprint holds its station cells: an add-on may not take them
		var holder = rig.Objects.AddRestored( ride, 10, 10, 0, true, 1, 1 );
		Assert.AreSame( holder, rig.Objects.FindAt( 10, 10 ) );
		Assert.AreEqual( OriginalPlacementResult.Occupied, rig.Objects.Check( jump, 10, 10, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( jump, 12, 12, 0 ), "a straight that no object holds still takes it" );
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
	[DataRow( 0 )]
	[DataRow( 90 )]
	[DataRow( 180 )]
	[DataRow( 270 )]
	public void TrackEndpointsCheckRotatedBeyondOccupancyAndDirectionlessD( int rotation )
	{
		foreach ( var symbol in new[] { '<', '>', 'D' } )
		{
			var entry = Entry( 1185, 5, false, symbol.ToString() );
			var rig = new Rig( entry );
			rig.Objects.TrackEndCellRule = ( x, y ) => CoasterCellMarkers.AllowsOrdinaryGround( rig.Cells, x, y );
			if ( symbol == 'D' )
			{
				Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, rig.Objects.Check( entry, 10, 10, rotation ) );
				continue;
			}
			var (dx, dy) = ObjectFootprint.RotateDirection( rotation, symbol == '>' ? 1 : -1, 0 );
			var (bx, by) = (10 + dx, 10 + dy);
			Assert.AreEqual( OriginalPlacementResult.Allowed, rig.Objects.Check( entry, 10, 10, rotation ) );
			rig.Cells.SetType( bx, by, 21 );
			rig.Cells.SetOwner( bx, by, ParkCellMap.CellWord( 10, 10 ) );
			var version = rig.Cells.Version;
			Assert.AreEqual( OriginalPlacementResult.Occupied, rig.Objects.Check( entry, 10, 10, rotation ), "Station mode4 has no same-owner type21 exemption." );
			Assert.AreEqual( version, rig.Cells.Version );
			rig.Cells.SetType( bx, by, ParkCellType.Empty );
			rig.Cells.Tracks.Set( bx, by, new TrackCell { Type = 11 } );
			Assert.AreEqual( OriginalPlacementResult.Occupied, rig.Objects.Check( entry, 10, 10, rotation ) );
			var edgeX = dx < 0 ? 0 : dx > 0 ? ParkPlacement.TileCount - 1 : 10;
			var edgeY = dy < 0 ? 0 : dy > 0 ? ParkPlacement.TileCount - 1 : 10;
			Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, rig.Objects.Check( entry, edgeX, edgeY, rotation ) );
		}
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

	/// <summary>The cells of an old OpenTPW save: the same park with every entrance and exit cell's link byte (map cell +0x0C) zero, the queue link and the outside cells as they were saved.</summary>
	private static ParkCellMap OldSave( Rig rig, params OriginalObject[] items )
	{
		var snapshot = ParkLayoutSnapshot.CaptureSnapshot( rig.Cells, rig.Objects );
		var access = items.SelectMany( item => item.Cells ).Where( cell => cell.Cell.CellType is ObjectShapeSymbols.TypeEntrance or ObjectShapeSymbols.TypeExit ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
		var stripped = snapshot with { Cells = snapshot.Cells with { Cells = snapshot.Cells.Cells.Select( cell => access.Contains( (cell.X, cell.Y) ) ? cell with { Links = 0 } : cell ).ToList() } };
		var old = ParkLayoutSnapshot.RestoreCellMap( stripped );
		foreach ( var (x, y) in access )
			Assert.AreEqual( 0, old.LinksAt( x, y ), $"the old save holds no link byte on ({x}, {y})" );
		return old;
	}

	private static byte[] Links( ParkCellMap cells ) => Enumerable.Range( 0, cells.Width * cells.Height ).Select( index => cells.LinksAt( index % cells.Width, index / cells.Width ) ).ToArray();

	[TestMethod]
	public void TheLinkBytesAnOldSaveLacksAreLaidAsAPlacementLaysThem()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var shop = Entry( 1203, 5, false, "*2S" );
		var rig = new Rig( ride, shop );
		var rideItem = rig.Place( ride, 4, 5 )!;
		var shopItem = rig.Place( shop, 10, 12 )!;
		var placed = Links( rig.Cells );
		Assert.IsTrue( new[] { (5, 5), (6, 5), (11, 12) }.All( cell => rig.Cells.LinksAt( cell.Item1, cell.Item2 ) != 0 ), "a placement lays the byte on the queue ride's entrance and exit and the shop's entrance (a shop's exit gets no outside cell)" );

		var old = OldSave( rig, rideItem, shopItem );
		var events = new List<(int, int)>();
		var repaired = 0;
		foreach ( var item in new[] { rideItem, shopItem } )
			repaired += ObjectAccessCells.RepairMissingLinks( old, item.AccessPoints, item.Entry.HasQueue, new LinkEffects( ( x, y ) => events.Add( (x, y) ) ) );
		Assert.AreEqual( 3, repaired );
		CollectionAssert.AreEqual( placed, Links( old ), "after the repair every link byte equals what a fresh placement lays" );
		Assert.IsTrue( events.Count > 0, "the repair refreshes tiles as the placement does" );
	}

	[TestMethod]
	public void ACellThatHoldsALinkByteIsLeftAlone()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var rig = new Rig( ride );
		var item = rig.Place( ride, 4, 5 )!;
		var before = Links( rig.Cells );
		var version = rig.Cells.Version;
		Assert.AreEqual( 0, ObjectAccessCells.RepairMissingLinks( rig.Cells, item.AccessPoints, item.Entry.HasQueue ) );
		CollectionAssert.AreEqual( before, Links( rig.Cells ) );
		Assert.AreEqual( version, rig.Cells.Version, "a save that already holds the bytes is not written to" );

		var old = OldSave( rig, item );
		Assert.AreEqual( 2, ObjectAccessCells.RepairMissingLinks( old, item.AccessPoints, item.Entry.HasQueue ) );
		Assert.AreEqual( 0, ObjectAccessCells.RepairMissingLinks( old, item.AccessPoints, item.Entry.HasQueue ), "a second repair finds nothing missing" );
	}

	[TestMethod]
	public void AnOutsideCellThePlayerRemovedLeavesTheEntranceUnlinked()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var rig = new Rig( ride );
		var item = rig.Place( ride, 4, 5 )!;
		var old = OldSave( rig, item );
		old.SetType( 5, 4, ParkCellType.Empty );
		old.SetLinks( 5, 4, 0 );
		Assert.AreEqual( 1, ObjectAccessCells.RepairMissingLinks( old, item.AccessPoints, item.Entry.HasQueue ), "only the exit, whose outside path cell is still there" );
		Assert.AreEqual( 0, old.LinksAt( 5, 5 ), "no queue cell outside the entrance to link to" );
	}
	/// <summary>
	/// A layout's restore over steps (a co-op join's build): the checks, then one object a step in the saved order, the allocators in the last, and every object as its record holds it: its place, its base
	/// height, its attraction id past the hole a removed object left, its thing id in a pool whose first slot is another thing's, and its seed. The call restores the same park.
	/// </summary>
	[TestMethod]
	public void TheRestoreOverStepsPlacesOneObjectAStepAsTheLayoutSavedIt()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var shop = Entry( 1203, 5, false, "*2S" );
		var rig = new Rig( ride, shop );
		rig.Objects.Things = new ThingPool();
		Assert.AreEqual( 1, rig.Objects.Things.Allocate( SaveThingModel.Guest ), "the pool's first slot is a guest's" );
		rig.Place( ride, 4, 5 );
		var removed = rig.Place( shop, 10, 12 )!;
		rig.Place( shop, 20, 12 );
		rig.Place( shop, 20, 20 );
		Assert.IsTrue( rig.Objects.Remove( removed ) );
		var layout = ParkLayoutSnapshot.CaptureSnapshot( rig.Cells, rig.Objects );
		CollectionAssert.AreEqual( new[] { 1, 3, 4 }, layout.Objects.Select( record => record.AttractionId ).ToArray(), "the removed object's id stays a hole" );
		CollectionAssert.AreEqual( new[] { 2, 4, 5 }, layout.Objects.Select( record => record.ThingId ).ToArray(), "the thing ids are the pool's, not the objects' places" );
		Assert.AreEqual( 5, layout.NextAttractionId );

		var stepped = new ParkObjects( rig.Catalog, new SandboxParkGrid() ) { Things = new ThingPool() };
		var placed = new List<OriginalObject>();
		var shares = new List<float>();
		foreach ( var share in ParkLayoutSnapshot.RestoreObjectSteps( layout, stepped, placed ) )
		{
			shares.Add( share );
			Assert.AreEqual( Math.Min( shares.Count - 1, 3 ), stepped.Objects.Count, "the checks, then one object a step" );
			Assert.AreEqual( share == 1, stepped.NextAttractionId == layout.NextAttractionId && stepped.NextPlacementSeed == layout.NextSeed, "the allocators are restored in the last step only" );
		}
		CollectionAssert.AreEqual( new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }, shares );
		Assert.AreEqual( 3, placed.Count );
		for ( var index = 0; index < layout.Objects.Count; index++ )
		{
			var (record, item) = (layout.Objects[index], placed[index]);
			Assert.AreEqual( record.InfoId, item.Entry.InfoId, $"object {index}'s type" );
			Assert.AreEqual( (record.X, record.Y, record.Rotation, record.BaseHeight), (item.Placement.X, item.Placement.Y, item.Placement.Rotation, item.Placement.BaseHeight), $"object {index}'s placement" );
			Assert.AreEqual( (record.AttractionId, record.ThingId, (int?)record.CreationSeed), (item.Visitors.AttractionId, item.ThingId, item.CreationSeed), $"object {index}'s ids and seed" );
			Assert.AreEqual( SaveThingModel.Attraction, stepped.Things!.ModelOf( record.ThingId ) );
		}
		Assert.AreEqual( 5, stepped.ScriptWorld.AllocateAttractionId(), "the next object takes the saved next id" );

		var atOnce = new ParkObjects( rig.Catalog, new SandboxParkGrid() ) { Things = new ThingPool() };
		CollectionAssert.AreEqual( placed.Select( item => (item.Placement, item.Visitors.AttractionId, item.ThingId, item.CreationSeed) ).ToArray(),
			ParkLayoutSnapshot.RestoreObjects( layout, atOnce ).Select( item => (item.Placement, item.Visitors.AttractionId, item.ThingId, item.CreationSeed) ).ToArray(), "the call restores the same park" );
	}

	/// <summary>A step whose object does not stand as it was saved clears the candidate, as the call does, with the objects of the steps before it.</summary>
	[TestMethod]
	public void ARestoreStepWhoseObjectStandsElsewhereClearsTheCandidate()
	{
		var shop = Entry( 1203, 5, false, "*2S" );
		var rig = new Rig( shop );
		rig.Place( shop, 10, 12 );
		rig.Place( shop, 20, 12 );
		var layout = ParkLayoutSnapshot.CaptureSnapshot( rig.Cells, rig.Objects );
		var moved = layout with { Objects = [layout.Objects[0], layout.Objects[1] with { BaseHeight = layout.Objects[1].BaseHeight + 1 }] };

		var candidate = new ParkObjects( rig.Catalog, new SandboxParkGrid() );
		var placed = new List<OriginalObject>();
		using var steps = ParkLayoutSnapshot.RestoreObjectSteps( moved, candidate, placed ).GetEnumerator();
		Assert.IsTrue( steps.MoveNext(), "the checks" );
		Assert.IsTrue( steps.MoveNext() );
		Assert.AreEqual( 1, candidate.Objects.Count, "the first object stands as saved" );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => steps.MoveNext() ).Message, "base height" );
		Assert.AreEqual( 0, candidate.Objects.Count, "the failure removes the objects of the steps before it too" );
	}

	/// <summary>
	/// The whole repair through a real resume: a park with a queue ride and a shop is captured, its entrance and exit link bytes are stripped as a save made before the import took them
	/// holds them, the layout goes through the save codec, and <c>Level.ConnectObjectsToGuests</c> (the resume branch of Level.Objects.cs) repairs the bytes; a guest called from the queue
	/// then walks into the entrance cell through the search, and the same resume of an unstripped save writes nothing.
	/// </summary>
	[TestMethod]
	public void AnOldSaveResumedThroughTheLevelIsRepairedAndACalledGuestWalksIn()
	{
		var ride = Entry( 1100, 5, true, "*2S" );
		var shop = Entry( 1203, 5, false, "*2S" );
		var rig = new Rig( ride, shop );
		var rideItem = rig.Place( ride, 4, 5 )!;
		var shopItem = rig.Place( shop, 10, 12 )!;
		// the player's path network: a row above the queue (joined to its back cell the way the queue tool does)
		var grid = new GuestPathGrid( rig.Cells );
		for ( var x = 0; x <= 12; x++ )
			grid.SetPath( x, 3, true );
		var queued = new RideVisitorBridge( 99, "joiner", RideVisitorKind.Ride, 5, 55, 30 ) { EntranceCell = (5, 3), QueueFrontCell = (5, 4), QueueEntranceDirection = 2 };
		Assert.IsTrue( QueuePaths.TryConnect( grid, queued ), "the queue's back cell is joined to the path row" );
		var placed = Links( rig.Cells );
		var placedTypes = Enumerable.Range( 0, rig.Cells.Width * rig.Cells.Height ).Select( index => rig.Cells.RawTypeAt( index % rig.Cells.Width, index / rig.Cells.Width ) ).ToArray();

		var layout = ParkLayoutSnapshot.CaptureSnapshot( rig.Cells, rig.Objects );
		var access = new[] { rideItem, shopItem }.SelectMany( item => item.Cells ).Where( cell => cell.Cell.CellType is ObjectShapeSymbols.TypeEntrance or ObjectShapeSymbols.TypeExit ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
		Assert.AreEqual( 4, access.Count, "the ride and the shop each have an entrance and an exit cell; the shop exit holds no byte after the placement" );
		var oldLayout = layout with { Cells = layout.Cells with { Cells = layout.Cells.Cells.Select( cell => access.Contains( (cell.X, cell.Y) ) ? cell with { Links = 0 } : cell ).ToList() } };
		oldLayout = ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( oldLayout ) );

		LevelResumed Resume( ParkLayoutSnapshot.Data saved )
		{
			var cells = ParkLayoutSnapshot.RestoreCellMap( saved );
			var objects = new ParkObjects( rig.Catalog, new SandboxParkGrid() );
			ParkLayoutSnapshot.RestoreObjects( saved, objects );
			var simulation = new GuestSimulation( new GuestPathGrid( cells ), GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
			var level = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof( Level ).GetField( "<Objects>k__BackingField", Private )!.SetValue( level, objects );
			typeof( Level ).GetField( "<Guests>k__BackingField", Private )!.SetValue( level, simulation );
			typeof( Level ).GetField( "<PathVariants>k__BackingField", Private )!.SetValue( level, new PathTileVariants() );
			typeof( Level ).GetField( "resumeState", Private )!.SetValue( level, System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( ParkResumeSnapshot ) ) );
			typeof( Level ).GetMethod( "ConnectObjectsToGuests", Private )!.Invoke( level, null );
			return new LevelResumed( level, cells, simulation, objects );
		}

		// a save that holds the bytes: the resume writes nothing
		var intact = ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( layout ) );
		var untouched = Resume( intact );
		CollectionAssert.AreEqual( placed, Links( untouched.Cells ), "a save that holds the bytes keeps them" );

		var cellsBefore = ParkLayoutSnapshot.RestoreCellMap( oldLayout );
		foreach ( var (x, y) in access )
			Assert.AreEqual( 0, cellsBefore.LinksAt( x, y ), $"the old save holds no link byte on ({x}, {y})" );
		var resumed = Resume( oldLayout );
		CollectionAssert.AreEqual( placed, Links( resumed.Cells ), "the resume repairs every byte to what the placement laid" );
		CollectionAssert.AreEqual( placedTypes, Enumerable.Range( 0, resumed.Cells.Width * resumed.Cells.Height ).Select( index => resumed.Cells.RawTypeAt( index % resumed.Cells.Width, index / resumed.Cells.Width ) ).ToArray(), "no cell type changes" );

		// a guest called from the queue walks into the entrance cell through the search and edge-test walk, and is not dismissed
		var restoredRide = resumed.Objects.Objects.Single( item => item.Entry.HasQueue );
		var bridge = restoredRide.Runtime.Visitors;
		var script = GuestTests.CreateShopScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ) );
		bridge.Attach( script, () => true );
		bridge.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		bridge.RecomputeQueue( resumed.Simulation.Grid );
		var tick = 1f / FixedStepClock.TicksPerSecond;
		script.Advance( tick );
		var guest = resumed.Simulation.SpawnInPark( 0, 3 );
		guest.AttractionId = bridge.AttractionId;
		guest.State = GuestState.GoingToRide;
		guest.Happiness = 60;
		var walkedIn = false;
		for ( var turn = 0; turn < 3000 && guest.RidesTaken == 0; turn++ )
		{
			resumed.Simulation.Tick( tick );
			bridge.HostStep();
			script.Advance( tick );
			bridge.WalkUpdate();
			walkedIn |= guest.State == GuestState.Boarding && guest.HasWaypoint && guest.Navigation.Original;
		}
		Assert.IsTrue( walkedIn, "the called guest walked the search into the repaired entrance cell" );
		Assert.AreEqual( 1, guest.RidesTaken, $"the guest rode ({guest.State})" );
	}

	[TestMethod]
	public void TheStandCellOfAHarnessRideIsLaidAsAPlacementLaysItAndTakenBackWhole()
	{
		var queue = new Rig( Entry( 1100, 5, true, "*2S" ) );
		var path = new Rig( Entry( 1203, 5, false, "*2S" ) );
		// the cell a placement lays beside a queue cell (a queue ride) and beside a path cell (a shop) is what the stand cell takes
		foreach ( var (rig, hasQueue) in new[] { (queue, true), (path, false) } )
		{
			var entry = rig.Catalog.Entries.Single();
			var placed = rig.Place( entry, 4, 5 )!;
			var expectedLinks = rig.Cells.LinksAt( 5, 5 );
			var expectedQueueLink = rig.Cells.QueueLinkAt( 5, 5 );
			var outside = placed.AccessPoints.Single( point => point.Kind == ObjectCellKind.Entrance );
			rig.Cells.SetType( 5, 5, ParkCellType.Empty );
			rig.Cells.SetLinks( 5, 5, 0 );
			var before = Links( rig.Cells );
			var beforeTypes = Enumerable.Range( 0, rig.Cells.Width * rig.Cells.Height ).Select( index => rig.Cells.RawTypeAt( index % rig.Cells.Width, index / rig.Cells.Width ) ).ToArray();
			Assert.IsTrue( ObjectAccessCells.LayStandCell( rig.Cells, 5, 5 ), hasQueue ? "beside the queue cell" : "beside the path cell" );
			Assert.AreEqual( ParkCellTypes.Entrance, rig.Cells.RawTypeAt( 5, 5 ) );
			if ( hasQueue )
			{
				Assert.AreEqual( expectedLinks, rig.Cells.LinksAt( 5, 5 ), "the link byte a placement lays toward the queue cell" );
				Assert.AreEqual( expectedQueueLink, rig.Cells.QueueLinkAt( 5, 5 ) );
			}
			Assert.IsFalse( ObjectAccessCells.LayStandCell( rig.Cells, 5, 5 ), "a typed cell is left as it is" );
			ObjectAccessCells.ClearStandCell( rig.Cells, 5, 5 );
			Assert.AreEqual( 0, rig.Cells.RawTypeAt( 5, 5 ) );
			Assert.AreEqual( 0, rig.Cells.LinksAt( 5, 4 ) & 0x10, "the cell outside loses its link to the cleared cell, as the cell clear unlinks (0x5367A0)" );
			var after = Links( rig.Cells );
			before[4 * rig.Cells.Width + 5] = (byte)(before[4 * rig.Cells.Width + 5] & ~0x10);
			CollectionAssert.AreEqual( before, after, "taking the cell back unlinks only its neighbour" );
			CollectionAssert.AreEqual( beforeTypes, Enumerable.Range( 0, rig.Cells.Width * rig.Cells.Height ).Select( index => rig.Cells.RawTypeAt( index % rig.Cells.Width, index / rig.Cells.Width ) ).ToArray() );
			Assert.IsNotNull( outside );
		}
	}

	private sealed record LevelResumed( Level Level, ParkCellMap Cells, GuestSimulation Simulation, ParkObjects Objects );
}
