using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The Jungle Easymode save's queue cells come back with the map as tp.exe's load reads them (the cell base serializer 0x4D0B30 reads mDirection, mFlags, mNeighbours, mParentID
/// and mType of every cell), so the imported Belly Bounce keeps its four-cell queue: the typed front its entrance link selects (0x4DE040) is a queue cell, the back walk reaches the
/// saved back cell, and guests join from the path attached to it and board. Regression for #540, whose typed front no longer fell back to a path cell while the queue cells were
/// not imported. Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ImportedQueueCellsTests
{
	private const int BellyBounceInfoId = 1100;
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	private sealed record Jungle( OriginalPark Park, ObjectCatalog Catalog, GuestPathGrid Grid, SaveObject Record, ObjectCatalogEntry Entry );

	/// <summary>The jungle cell map the way the level imports it: the save's cells, then every placed object's footprint and the saved link bytes of its entrance and exit.</summary>
	private static Jungle LoadJungle()
	{
		var park = OriginalPark.Load( "jungle" );
		if ( park.Save == null )
			Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
		var catalog = ObjectCatalog.Load( "jungle" );
		var cells = ParkCellMap.FromOriginal( park.Map, park.Save, park.Heightfield );
		foreach ( var record in park.Save.PlacedObjects.Select( item => item.Record ) )
		{
			if ( catalog.Find( record.InfoId ) is not { IsTool: false, IsFixedItem: false } entry )
				continue;
			var footprint = ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation ).ToList();
			var owner = ParkCellMap.CellWord( record.X, record.Y );
			cells.WriteFootprint( footprint, owner, record.Rotation );
			cells.TakeSavedAccessLinks( footprint, owner, park.Save.Cells );
		}
		var belly = park.Save.PlacedObjects.Select( item => item.Record ).Single( record => record.InfoId == BellyBounceInfoId );
		return new Jungle( park, catalog, new GuestPathGrid( cells ), belly, catalog.Find( BellyBounceInfoId )! );
	}

	private static OriginalObjectRuntime Register( Jungle jungle, GuestSimulation simulation, RideScriptWorld world )
	{
		var record = jungle.Record;
		var points = ObjectFootprint.GetAccessPoints( jungle.Entry.Shape, record.X, record.Y, record.Rotation ).ToArray();
		var owner = ParkCellMap.CellWord( record.X, record.Y );
		var runtime = new OriginalObjectRuntime( jungle.Entry, world, seed: 3, open: true );
		Assert.IsTrue( Level.RegisterWithGuests( runtime, points, simulation, record.Rotation, owner, ownExitCell: true,
			queueEntryResolver: () => Level.ResolveQueueEntrance( points, simulation.Grid.Cells, owner ) ), "the Belly Bounce registers" );
		return runtime;
	}

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the Jungle Easymode save." );
		previousLog = Log;
		Log = new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous;
		Log = previousLog;
	}

	private BaseFileSystem previous = null!;
	private Logger previousLog = null!;

	[TestMethod]
	public void TheBellyBounceKeepsItsSavedFourCellQueue()
	{
		var jungle = LoadJungle();
		var cells = jungle.Grid.Cells;
		var save = jungle.Park.Save!;
		var owner = ParkCellMap.CellWord( jungle.Record.X, jungle.Record.Y );
		var saved = Enumerable.Range( 0, save.Cells.CountX * save.Cells.CountY )
			.Select( index => (X: index % save.Cells.CountX, Y: index / save.Cells.CountX) )
			.Where( cell => save.Cells[cell.X, cell.Y].MapType == (uint)ParkCellType.Queue ).ToArray();
		Assert.AreEqual( 4, saved.Length, "Easymode saves the Belly Bounce's four queue cells" );
		Assert.AreEqual( (short)1, save.Cells[52, 22].OverlapCounter, "the front cell's saved mOverlapCounter" );
		foreach ( var (x, y) in saved )
		{
			var record = save.Cells[x, y];
			Assert.AreEqual( (byte)ParkCellType.Queue, cells.RawTypeAt( x, y ), $"({x}, {y}) is a queue cell" );
			Assert.AreEqual( (byte)record.PathConnections, cells.LinksAt( x, y ), $"({x}, {y}) link byte +0x0C" );
			Assert.AreEqual( record.Direction, cells.QueueLinkAt( x, y ), $"({x}, {y}) queue link +0x0D" );
			Assert.AreEqual( owner, cells.OwnerAt( x, y ), $"({x}, {y}) owner word +0x10" );
			Assert.AreEqual( record.OverlapCounter, cells.PlacementCountAt( x, y ), $"({x}, {y}) placement counter +0x20" );
			Assert.AreEqual( (ParkCellFlags)record.MapFlags & ParkCellFlags.NoModify, cells.FlagsAt( x, y ) & ParkCellFlags.NoModify, $"({x}, {y}) NoModify" );
		}

		var simulation = new GuestSimulation( jungle.Grid, GuestSettings.Load( "jungle" ), 7 ) { ArrivalsEnabled = false };
		var bridge = Register( jungle, simulation, new RideScriptWorld( 11 ) ).Visitors;
		var attraction = save.Attractions.Single( item => item.InfoId == BellyBounceInfoId );
		var back = ((attraction.BackOfQueueCell - 1) % 128, (attraction.BackOfQueueCell - 1) / 128);
		Assert.AreEqual( (52, 22), bridge.QueueFrontCell, "the entrance's link byte selects the front (0x4DE040)" );
		Assert.AreEqual( attraction.QueueSizeInCells, bridge.QueueSizeInCells, "the saved queue size" );
		Assert.AreEqual( back, bridge.QueueCells[^1], "the saved back-of-queue cell" );
		CollectionAssert.AreEquivalent( saved, bridge.QueueCells.ToArray() );
		Assert.IsNotNull( bridge.JoinCell, "the back cell is attached to a path" );
		Assert.IsTrue( jungle.Grid.IsWalkable( bridge.JoinCell.Value.X, bridge.JoinCell.Value.Y ) );
	}

	[TestMethod]
	public void GuestsBoardTheImportedBellyBounce() => AssertGuestsBoard( LoadJungle() );

	/// <summary>Sends guests to the Belly Bounce from its join cell, as the native jungle smoke does, and runs the ride's real script until one boards.</summary>
	private static void AssertGuestsBoard( Jungle jungle )
	{
		var world = new RideScriptWorld( 11 );
		var simulation = new GuestSimulation( jungle.Grid, GuestSettings.Load( "jungle" ), 7 ) { ArrivalsEnabled = false };
		var runtime = Register( jungle, simulation, world );
		var bridge = runtime.Visitors;
		Assert.AreEqual( 4, bridge.QueueSizeInCells, "the saved four-cell queue" );
		var join = bridge.JoinCell ?? throw new AssertFailedException( "the imported queue has no join cell" );
		for ( var index = 0; index < 8; index++ )
		{
			var guest = simulation.SpawnInPark( join.X, join.Y );
			guest.AttractionId = bridge.AttractionId;
			guest.State = GuestState.GoingToRide;
		}
		for ( var tick = 0; tick < 180 * FixedStepClock.TicksPerSecond && bridge.BoardedTotal == 0; tick++ )
		{
			simulation.Tick( Tick );
			runtime.Simulate( Tick );
		}
		Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, runtime.Script.FaultMessage );
		Assert.IsTrue( bridge.BoardedTotal > 0, $"guests board the imported Belly Bounce through its queue (queue {bridge.QueueLength}, cells {bridge.QueueSizeInCells})" );
	}

	private static (int X, int Y)[] SavedQueueCells( SaveCellGrid saved ) =>
		Enumerable.Range( 0, saved.CountX * saved.CountY ).Select( index => (X: index % saved.CountX, Y: index / saved.CountX) )
			.Where( cell => saved[cell.X, cell.Y].MapType == (uint)ParkCellType.Queue ).ToArray();

	/// <summary>
	/// [EXT:legacy-save-queue-cells] A park saved by OpenTPW before the import took the queue cells holds empty ground there (the cell map is saved and resumed as it was); the load lays
	/// them again from the shipped save, and the resumed Belly Bounce is joinable and boards guests.
	/// </summary>
	[TestMethod]
	public void AParkSavedBeforeTheQueueImportResumesWithAJoinableBellyBounce()
	{
		var jungle = LoadJungle();
		var cells = jungle.Grid.Cells;
		var save = jungle.Park.Save!;
		var queue = SavedQueueCells( save.Cells );
		// the cells such a save holds: the map start's empty ground, no NoModify flag, no owner or links
		foreach ( var (x, y) in queue )
		{
			cells.SetType( x, y, ParkCellType.Empty );
			cells.SetFlags( x, y, cells.FlagsAt( x, y ) & ~ParkCellFlags.NoModify );
		}
		var record = jungle.Record;
		var points = ObjectFootprint.GetAccessPoints( jungle.Entry.Shape, record.X, record.Y, record.Rotation ).ToArray();
		var owner = ParkCellMap.CellWord( record.X, record.Y );
		Assert.AreEqual( (52, 22), ObjectAccessCells.EmptyQueueFront( cells, points, owner ), "the entrance links into empty ground" );
		var reads = 0;
		var laid = Level.RestoreLegacySavedQueue( cells, points, BellyBounceInfoId, record.Rotation, hasQueue: true, owner,
			() => { reads++; return OriginalPark.ReadShippedSave( "jungle", jungle.Park.Map ); } );
		Assert.AreEqual( 4, laid );
		Assert.AreEqual( 1, reads );
		foreach ( var (x, y) in queue )
		{
			var saved = save.Cells[x, y];
			Assert.AreEqual( (byte)ParkCellType.Queue, cells.RawTypeAt( x, y ) );
			Assert.AreEqual( (byte)saved.PathConnections, cells.LinksAt( x, y ) );
			Assert.AreEqual( saved.Direction, cells.QueueLinkAt( x, y ) );
			Assert.AreEqual( owner, cells.OwnerAt( x, y ) );
			Assert.AreEqual( saved.OverlapCounter, cells.PlacementCountAt( x, y ) );
			Assert.AreEqual( (ParkCellFlags)saved.MapFlags & ParkCellFlags.NoModify, cells.FlagsAt( x, y ) & ParkCellFlags.NoModify );
		}
		Assert.IsNull( ObjectAccessCells.EmptyQueueFront( cells, points, owner ), "a second load finds nothing to restore" );
		AssertGuestsBoard( jungle );
	}

	/// <summary>[EXT:legacy-save-queue-cells] A park saved after the import holds the queue cells: the load reads no shipped save and writes no cell.</summary>
	[TestMethod]
	public void AParkSavedWithTheQueueCellsIsLeftAlone()
	{
		var jungle = LoadJungle();
		var cells = jungle.Grid.Cells;
		var record = jungle.Record;
		var points = ObjectFootprint.GetAccessPoints( jungle.Entry.Shape, record.X, record.Y, record.Rotation ).ToArray();
		var owner = ParkCellMap.CellWord( record.X, record.Y );
		var version = cells.Version;
		var reads = 0;
		Assert.AreEqual( 0, Level.RestoreLegacySavedQueue( cells, points, BellyBounceInfoId, record.Rotation, hasQueue: true, owner, () => { reads++; return null; } ) );
		Assert.AreEqual( 0, reads, "nothing is read for a park that holds its queue" );
		Assert.AreEqual( version, cells.Version, "no cell is written" );
		// a newer save whose queue the player removed: the removal unlinks the cells on both sides (0x5367A0), so the entrance links into nothing and stays without a queue
		foreach ( var (x, y) in SavedQueueCells( jungle.Park.Save!.Cells ) )
			jungle.Grid.ClearQueue( x, y );
		Assert.IsNull( ObjectAccessCells.EmptyQueueFront( cells, points, owner ) );
		version = cells.Version;
		Assert.AreEqual( 0, Level.RestoreLegacySavedQueue( cells, points, BellyBounceInfoId, record.Rotation, hasQueue: true, owner, () => { reads++; return jungle.Park.Save; } ) );
		Assert.AreEqual( 0, reads );
		Assert.AreEqual( version, cells.Version );
	}

	/// <summary>[EXT:legacy-save-queue-cells] A queue restored only in part (a cell of it holds the player's path now) keeps no link toward a neighbour that does not link back.</summary>
	[TestMethod]
	public void APartlyRestoredQueueKeepsNoOneSidedLink()
	{
		var jungle = LoadJungle();
		var cells = jungle.Grid.Cells;
		foreach ( var (x, y) in SavedQueueCells( jungle.Park.Save!.Cells ) )
		{
			cells.SetType( x, y, ParkCellType.Empty );
			cells.SetFlags( x, y, cells.FlagsAt( x, y ) & ~ParkCellFlags.NoModify );
		}
		// the player's unlinked path cell where the queue's second cell lay
		cells.SetType( 51, 22, ParkCellType.Path );
		cells.SetLinks( 51, 22, 0 );
		var record = jungle.Record;
		var points = ObjectFootprint.GetAccessPoints( jungle.Entry.Shape, record.X, record.Y, record.Rotation ).ToArray();
		var owner = ParkCellMap.CellWord( record.X, record.Y );
		Assert.AreEqual( (52, 22), ObjectAccessCells.EmptyQueueFront( cells, points, owner ) );
		var version = cells.Version;
		var reads = 0;
		Assert.AreEqual( 0, Level.RestoreLegacySavedQueue( cells, points, BellyBounceInfoId, record.Rotation + 90, hasQueue: true, owner, () => { reads++; return jungle.Park.Save; } ), "another angle is not the shipped object" );
		Assert.AreEqual( 1, reads, "the rotation check must inspect the source" );
		Assert.AreEqual( version, cells.Version );
		Assert.AreEqual( 1, Level.RestoreLegacySavedQueue( cells, points, BellyBounceInfoId, record.Rotation, hasQueue: true, owner, () => jungle.Park.Save ) );
		Assert.AreEqual( (byte)ParkCellType.Queue, cells.RawTypeAt( 52, 22 ) );
		Assert.AreEqual( (byte)0x10, cells.LinksAt( 52, 22 ), "the front keeps its link to the entrance, which links back, and drops the -X link the path does not return" );
		Assert.AreEqual( (byte)0, cells.LinksAt( 51, 22 ), "the player's path is not written" );
	}

	/// <summary>
	/// [EXT:legacy-save-queue-cells] The repair through the level's real resume branch (<c>Level.ConnectObjectsToGuests</c> with a resume state, an <see cref="OriginalPark"/> loaded without its
	/// shipped save as a resume loads it, and the lazily read shipped save): the Jungle layout goes through the park save's cell codec with the four queue cells emptied, as a save made before the
	/// import holds them, and the resumed Belly Bounce has its four-cell queue. A layout that holds the cells is resumed unchanged. (A whole <c>new Level( snapshot, … )</c> needs the GPU for the
	/// imported objects' models; this drives the same branch on a headless level, as <c>ObjectAccessCellsTests</c> does for the access links.)
	/// </summary>
	[TestMethod]
	public void TheLevelsResumeRestoresTheQueueOfAParkSavedBeforeTheImport()
	{
		var jungle = LoadJungle();
		var empty = new ParkObjects( jungle.Catalog, new SandboxParkGrid() );
		var layout = ParkLayoutSnapshot.CaptureSnapshot( jungle.Grid.Cells, empty );
		var queue = SavedQueueCells( jungle.Park.Save!.Cells ).ToHashSet();
		Assert.AreEqual( 4, layout.Cells.Cells.Count( cell => queue.Contains( (cell.X, cell.Y) ) ), "a newer save holds the four queue cells" );
		var old = layout with { Cells = layout.Cells with { Cells = layout.Cells.Cells.Where( cell => !queue.Contains( (cell.X, cell.Y) ) ).ToList() } };

		(ParkCellMap Cells, RideVisitorBridge Bridge, Level Level) Resume( ParkLayoutSnapshot.Data saved )
		{
			saved = ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( saved ) );
			var cells = ParkLayoutSnapshot.RestoreCellMap( saved );
			var objects = new ParkObjects( jungle.Catalog, new SandboxParkGrid() );
			// the imported Belly Bounce as the resume restores it: its placement and runtime (its models are presentation, which needs the GPU)
			var item = (OriginalObject)RuntimeHelpers.GetUninitializedObject( typeof( OriginalObject ) );
			const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof( OriginalObject ).GetField( "<Placement>k__BackingField", Private )!.SetValue( item, new ObjectPlacement( jungle.Record.X, jungle.Record.Y, jungle.Record.Rotation, default, 0 ) );
			typeof( OriginalObject ).GetField( "<Runtime>k__BackingField", Private )!.SetValue( item, new OriginalObjectRuntime( jungle.Entry, objects.ScriptWorld, seed: 3 ) );
			((List<OriginalObject>)objects.Objects).Add( item );
			var simulation = new GuestSimulation( new GuestPathGrid( cells ), GuestSettings.Load( "jungle" ), 7 ) { ArrivalsEnabled = false };
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			typeof( Level ).GetField( "<Objects>k__BackingField", Private )!.SetValue( level, objects );
			typeof( Level ).GetField( "<Guests>k__BackingField", Private )!.SetValue( level, simulation );
			typeof( Level ).GetField( "<PathVariants>k__BackingField", Private )!.SetValue( level, new PathTileVariants() );
			typeof( Level ).GetField( "<OriginalPark>k__BackingField", Private )!.SetValue( level, OriginalPark.Load( "jungle", readShippedSave: false ) );
			typeof( Level ).GetField( "resumeState", Private )!.SetValue( level, RuntimeHelpers.GetUninitializedObject( typeof( ParkResumeSnapshot ) ) );
			typeof( Level ).GetMethod( "ConnectObjectsToGuests", Private )!.Invoke( level, null );
			Assert.IsNull( typeof( Level ).GetField( "legacyQueueSave", Private )!.GetValue( level ), "the shipped save read for the repair is released" );
			return (cells, item.Visitors, level);
		}

		var resumed = Resume( old );
		Assert.AreEqual( (byte)ParkCellType.Queue, resumed.Cells.RawTypeAt( 52, 22 ) );
		Assert.AreEqual( 4, resumed.Bridge.QueueSizeInCells );
		Assert.IsNotNull( resumed.Bridge.JoinCell );

		var newer = Resume( layout );
		Assert.AreEqual( 4, newer.Bridge.QueueSizeInCells );
		var restoredFromNewer = ParkLayoutSnapshot.CaptureSnapshot( newer.Cells, empty );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( layout.Cells.Cells ), ParkResumeJson.Serialize( restoredFromNewer.Cells.Cells ), "a newer save's cells are resumed unchanged" );
	}
}
