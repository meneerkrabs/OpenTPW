using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Layout snapshots (save/load milestone): the deep cell-map capture/restore with raw bytes and the exact version,
/// pure validation of the object records against the real Jungle catalog — the Easymode layout as the honest
/// accept case, duplicate rides with distinct rotations, and corrupt, missing or exhausted records as rejects —
/// a JSON round-trip of the DTOs, the pristine-candidate requirement of the restore, and the explicit-id CPU
/// factory (<see cref="OriginalObjectRuntime.CreateForRestore"/>) that keeps saved attraction ids without
/// replaying dead ones. Synthetic cell tests need no game data; catalog tests skip without OPENTPW_GAME_PATH.
/// Restoring objects for real constructs OriginalObjects (GPU resources), so that path is the integration gate's
/// to exercise; these tests cover everything up to and including the pre-placement checks of RestoreObjects.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkLayoutSnapshotTests
{
	/// <summary>A ParkObjects with no catalog needs (nothing is placed or looked up) for the synthetic cell tests.</summary>
	private static ParkObjects EmptyObjects() => new( null!, new SandboxParkGrid() );

	// --- cell map: capture and restore are exact, including deleted cells and the version ---

	[TestMethod]
	public void CellMapRoundTripsRawBytesCountersAndExactVersion()
	{
		var cells = new ParkCellMap( 16, 12 );
		cells.SetType( 3, 4, ParkCellType.Path );
		cells.SetLinks( 3, 4, 0x0F );
		cells.SetFlags( 3, 4, ParkCellFlags.NoModify );
		cells.SetType( 4, 4, ParkCellType.Path );
		cells.SetLinks( 4, 4, 0x05 );
		cells.SetPlacementCount( 4, 4, 2 );
		cells.SetType( 4, 5, ParkCellType.Queue );
		cells.SetQueueLink( 4, 5, 64 );
		cells.SetType( 5, 6, (byte)21 );
		cells.SetFlags( 5, 6, (ParkCellFlags)0x40 );
		var deleted = (X: 6, Y: 7);
		cells.SetType( deleted.X, deleted.Y, ParkCellType.Path );
		cells.SetPlacementCount( deleted.X, deleted.Y, 3 );
		cells.SetType( deleted.X, deleted.Y, (byte)ParkCellType.Empty );
		cells.Touch();
		cells.Touch();

		var snapshot = ParkLayoutSnapshot.CaptureSnapshot( cells, EmptyObjects() );
		Assert.AreEqual( (16, 12), (snapshot.Cells.Width, snapshot.Cells.Height) );
		Assert.AreEqual( cells.Version, snapshot.Cells.Version );
		Assert.AreEqual( 4, snapshot.Cells.Cells.Count, "only the four non-empty cells are recorded" );
		Assert.IsFalse( snapshot.Cells.Cells.Any( cell => cell.X == deleted.X && cell.Y == deleted.Y ), "a cell emptied again is not recorded" );
		Assert.AreEqual( (byte)64, snapshot.Cells.Cells.Single( cell => cell.X == 4 && cell.Y == 5 ).QueueLink );
		Assert.AreEqual( WorldSeed.Default.ObjectScriptKey, snapshot.ObjectScriptKey );
		Assert.AreEqual( 1, snapshot.NextSeed );
		Assert.AreEqual( 1, snapshot.NextAttractionId );

		var restored = ParkLayoutSnapshot.RestoreCellMap( snapshot );
		Assert.AreEqual( cells.Version, restored.Version, "the version is restored exactly, Touch bumps included" );
		for ( var y = 0; y < cells.Height; y++ )
			for ( var x = 0; x < cells.Width; x++ )
				Assert.AreEqual(
					(cells.RawTypeAt( x, y ), cells.FlagsAt( x, y ), cells.PlacementCountAt( x, y ), cells.LinksAt( x, y ), cells.QueueLinkAt( x, y )),
					(restored.RawTypeAt( x, y ), restored.FlagsAt( x, y ), restored.PlacementCountAt( x, y ), restored.LinksAt( x, y ), restored.QueueLinkAt( x, y )),
					$"cell ({x}, {y})" );
		Assert.AreEqual( cells.CountOf( ParkCellType.Empty ), restored.CountOf( ParkCellType.Empty ) );
		Assert.AreEqual( cells.CountOf( ParkCellType.Path ), restored.CountOf( ParkCellType.Path ) );
		Assert.AreEqual( cells.CountOf( ParkCellType.Queue ), restored.CountOf( ParkCellType.Queue ) );
		Assert.AreEqual( cells.CountOf( (ParkCellType)21 ), restored.CountOf( (ParkCellType)21 ) );
	}

	[TestMethod]
	public void CellRestoreRejectsCorruptCellRecords()
	{
		ParkLayoutSnapshot.Data WithCells( int width, int height, int version, params ParkLayoutSnapshot.CellData[] records ) =>
			new()
			{
				Cells = new ParkLayoutSnapshot.CellMapData { Width = width, Height = height, Version = version, Cells = records.ToList() },
				Objects = new List<ParkLayoutSnapshot.PlacedObjectData>(),
				NextSeed = 1,
				NextAttractionId = 1,
				ObjectScriptKey = WorldSeed.Default.ObjectScriptKey
			};
		var cell = new ParkLayoutSnapshot.CellData { X = 3, Y = 4, Type = 1, Flags = 0, Placements = 0, Links = 0, QueueLink = 0 };
		// each message whole: a check's message is formatted only when it fails (CheckMessage), and reads as it did when every check formatted its own
		void Rejects( ParkLayoutSnapshot.Data data, string message ) =>
			Assert.AreEqual( $"Layout snapshot is inconsistent: {message}.", Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( data ) ).Message );
		Rejects( WithCells( 16, 12, 0, cell, cell with { Links = 1 } ), "cell (3, 4) is recorded twice" );
		Rejects( WithCells( 16, 12, 0, cell, cell with { X = 16 } ), "cell (16, 4) is outside the map" );
		Rejects( WithCells( 16, 12, 0, cell with { Type = 3, QueueLink = 3 } ), "cell (3, 4) has queue link 3" );
		Rejects( WithCells( 16, 12, 0, cell with { QueueLink = 4 } ), "cell (3, 4) has a queue link but type 1" );
		Rejects( WithCells( 16, 12, 0, cell with { Placements = -1 } ), "cell (3, 4) has placement count -1" );
		Rejects( WithCells( 0, 12, 0 ), "cell map is 0×12" );
		Rejects( WithCells( 2000, 12, 0 ), "cell map is 2000×12" );
		Rejects( WithCells( 16, 12, -1 ), "cell map version is negative" );
		Rejects( WithCells( 1, 1, 0, cell, cell with { X = 0, Y = 0 } ), "more cell records than cells" );
	}

	// --- object records: validation against the real Jungle catalog ---

	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the layout snapshot corpus." );
		originalFileSystem = previous;
		initialized = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	private static (OriginalPark Park, ObjectCatalog Catalog, OriginalParkGrid Grid) Jungle()
	{
		var park = OriginalPark.Load( "jungle" );
		return (park, ObjectCatalog.Load( "jungle" ), new OriginalParkGrid( park ));
	}

	/// <summary>The layout a live import of the Easymode save places, as snapshot records (ImportOriginal's order, seeds and ids).</summary>
	private static ParkLayoutSnapshot.Data EasymodeLayout( OriginalPark park, ObjectCatalog catalog, OriginalParkGrid grid )
	{
		var objects = new ParkObjects( catalog, grid, WorldSeed.Default );
		var cells = ParkCellMap.FromOriginal( park.Map, park.Save );
		var key = objects.ObjectScriptKey;
		var ordinal = 1;
		var records = new List<ParkLayoutSnapshot.PlacedObjectData>();
		foreach ( var record in park.Save!.PlacedObjects.Select( item => item.Record ).Concat( park.Save.UnresolvedObjects ).Concat( park.Save.FixedItems ) )
		{
			var entry = catalog.Find( record.InfoId );
			if ( entry == null || entry.IsTool )
				continue;
			var (x, y, rotation) = entry.IsFixedItem && !GameEditions.IsThemeParkInc ? (0, 0, 0) : (record.X, record.Y, record.Rotation);
			records.Add( new ParkLayoutSnapshot.PlacedObjectData
			{
				InfoId = record.InfoId,
				Archive = entry.ArchivePath,
				X = x,
				Y = y,
				Rotation = rotation,
				Open = true,
				CreationSeed = ordinal ^ key,
				AttractionId = ordinal,
				BaseHeight = objects.GetBaseHeight( entry, x, y, rotation ), BaseHeightRule = 1,
				IsFixedItem = entry.IsFixedItem
			} );
			ordinal++;
		}
		return ParkLayoutSnapshot.CaptureSnapshot( cells, objects ) with { Objects = records, NextSeed = ordinal, NextAttractionId = ordinal };
	}

	[TestMethod]
	public void ValidationAcceptsTheEasymodeLayoutAndItsCellsRoundTrip()
	{
		var (park, catalog, grid) = Jungle();
		var snapshot = EasymodeLayout( park, catalog, grid );
		Assert.IsTrue( snapshot.Objects.Any( record => record.IsFixedItem ), "the Easymode save places fixed items" );
		Assert.IsTrue( snapshot.Objects.Count >= 10, "the Easymode save places its rides, shops and fixed items" );
		ParkLayoutSnapshot.ValidateSnapshot( snapshot, catalog );

		var original = ParkCellMap.FromOriginal( park.Map, park.Save );
		var restored = ParkLayoutSnapshot.RestoreCellMap( snapshot );
		Assert.AreEqual( original.Version, restored.Version );
		for ( var y = 0; y < original.Height; y++ )
			for ( var x = 0; x < original.Width; x++ )
				Assert.AreEqual(
					(original.RawTypeAt( x, y ), original.FlagsAt( x, y ), original.PlacementCountAt( x, y ), original.LinksAt( x, y ), original.QueueLinkAt( x, y )),
					(restored.RawTypeAt( x, y ), restored.FlagsAt( x, y ), restored.PlacementCountAt( x, y ), restored.LinksAt( x, y ), restored.QueueLinkAt( x, y )),
					$"cell ({x}, {y})" );
	}

	[TestMethod]
	public void ValidationAcceptsDuplicateRidesWithDistinctRotationsAndRejectsTheirOverlap()
	{
		var (park, catalog, grid) = Jungle();
		var snapshot = EasymodeLayout( park, catalog, grid );
		var key = snapshot.ObjectScriptKey;
		var totem = catalog.Get( 1110 );
		ParkLayoutSnapshot.PlacedObjectData Copy( int x, int y, int rotation, int ordinal ) => new()
		{
			InfoId = totem.InfoId,
			Archive = totem.ArchivePath,
			X = x,
			Y = y,
			Rotation = rotation,
			Open = false,
			CreationSeed = ordinal ^ key,
			AttractionId = ordinal,
			BaseHeight = 0,
			BaseHeightRule = 1,
			IsFixedItem = false
		};
		var ordinal = snapshot.NextSeed;
		var rotated = snapshot with
		{
			Objects = [.. snapshot.Objects, Copy( 20, 62, 0, ordinal ), Copy( 40, 62, 90, ordinal + 1 )],
			NextSeed = ordinal + 2,
			NextAttractionId = ordinal + 2
		};
		ParkLayoutSnapshot.ValidateSnapshot( rotated, catalog );

		var overlapping = rotated with { Objects = [.. rotated.Objects, Copy( 20, 62, 180, ordinal + 2 )], NextSeed = ordinal + 3, NextAttractionId = ordinal + 3 };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.ValidateSnapshot( overlapping, catalog ) ).Message, "objects overlap" );
	}

	[TestMethod]
	public void ValidationRejectsCorruptObjectRecords()
	{
		var (park, catalog, grid) = Jungle();
		var snapshot = EasymodeLayout( park, catalog, grid );
		var key = snapshot.ObjectScriptKey;
		void Rejects( ParkLayoutSnapshot.Data data, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.ValidateSnapshot( data, catalog ) ).Message, message );
		Rejects( snapshot with { Objects = [.. snapshot.Objects, snapshot.Objects[0] with { InfoId = 999999 }] }, "unknown Info.Id 999999" );
		Rejects( snapshot with { Objects = [.. snapshot.Objects, snapshot.Objects[0] with { InfoId = 100 }] }, "is a tool" );
		Rejects( snapshot with { Objects = [.. snapshot.Objects, snapshot.Objects[0] with { Archive = "/levels/jungle/rides/nowhere" }] }, "not the saved /levels/jungle/rides/nowhere" );
		Rejects( snapshot with { Objects = [.. snapshot.Objects, snapshot.Objects.First( record => !record.IsFixedItem ) with { Rotation = 45 }] }, "rotation 45" );
		Rejects( snapshot with { Objects = [.. snapshot.Objects, snapshot.Objects.First( record => !record.IsFixedItem ) with { IsFixedItem = true }] }, "not the saved True" );
		// Replacing the first fixed item's attraction id with an earlier object's breaks placement order (strictly
		// increasing ids also make reuse impossible).
		var firstFixed = snapshot.Objects.FindIndex( record => record.IsFixedItem );
		Rejects( snapshot with
		{
			Objects = snapshot.Objects.Select( ( record, index ) => index == firstFixed ? record with { AttractionId = snapshot.Objects[0].AttractionId } : record ).ToList()
		}, "does not follow" );
		// Seed corruptions replace a record in place: appending at a taken anchor would trip the overlap check first.
		Rejects( snapshot with { Objects = snapshot.Objects.Select( ( record, index ) => index == 0 ? record with { CreationSeed = snapshot.NextSeed ^ key } : record ).ToList() }, "not the 0-keyed ordinal" );
		Rejects( snapshot with { Objects = snapshot.Objects.Select( ( record, index ) => index == 1 ? record with { CreationSeed = snapshot.Objects[0].CreationSeed } : record ).ToList() }, "share one placement ordinal" );
		Rejects( snapshot with { Objects = snapshot.Objects.Select( ( record, index ) => index == 0 ? record with { BaseHeight = float.NaN } : record ).ToList() }, "non-finite base height" );
		Rejects( snapshot with { Objects = snapshot.Objects.Select( ( record, index ) => index == 0 ? record with { BaseHeightRule = 2 } : record ).ToList() }, "unknown base height rule 2" );
		// a save from before the vertex rule carries no rule and validates (it restores with the earlier mean-height rule)
		ParkLayoutSnapshot.ValidateSnapshot( snapshot with { Objects = snapshot.Objects.Select( record => record with { BaseHeightRule = null } ).ToList() }, catalog );
		// A second fixed item needs its own unused ordinal, so the allocator grows with it.
		var first = snapshot.Objects.First( record => record.IsFixedItem );
		var duplicated = snapshot with
		{
			Objects = [.. snapshot.Objects.Where( record => record != first ), first, first with { CreationSeed = snapshot.NextSeed ^ key, AttractionId = 900 }],
			NextSeed = snapshot.NextSeed + 1
		};
		Rejects( duplicated, "placed more than once" );
	}

	[TestMethod]
	public void ValidationKeepsRemovedObjectsIdentityHolesAndRejectsRenumbering()
	{
		var (park, catalog, grid) = Jungle();
		var snapshot = EasymodeLayout( park, catalog, grid );
		var key = snapshot.ObjectScriptKey;
		// A park that lost objects: the survivors keep their placement ordinals and attraction ids (independent holes —
		// e.g. a prototype ride burned attraction id 1, while a removed object burned placement ordinal 2).
		var survivors = snapshot.Objects.Take( 3 ).Select( ( record, index ) => record with
		{
			AttractionId = new[] { 2, 4, 5 }[index],
			CreationSeed = new[] { 1, 3, 6 }[index] ^ key
		} ).ToList();
		var holes = snapshot with { Objects = survivors, NextSeed = 7, NextAttractionId = 7 };
		ParkLayoutSnapshot.ValidateSnapshot( holes, catalog );

		void Rejects( ParkLayoutSnapshot.Data data, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.ValidateSnapshot( data, catalog ) ).Message, message );
		// Ids must increase with placement order and stay inside the allocator; the renumbered-to-1..N variant of a
		// holed park is rejected at restore time, where every object must land on its saved id.
		Rejects( holes with { Objects = survivors.Select( ( record, index ) => record with { AttractionId = new[] { 3, 2, 5 }[index] } ).ToList() }, "does not follow 3" );
		Rejects( holes with { NextAttractionId = 5 }, "outside the saved allocator" );
		Rejects( holes with { Objects = survivors.Select( ( record, index ) => record with { CreationSeed = new[] { 3, 3, 6 }[index] ^ key } ).ToList() }, "share one placement ordinal" );
	}

	// --- missing records and exhausted allocators are rejected before anything is placed ---

	[TestMethod]
	public void ValidationRejectsMissingRecordsAndExhaustedAllocators()
	{
		var (park, catalog, grid) = Jungle();
		var snapshot = EasymodeLayout( park, catalog, grid );
		void Rejects( ParkLayoutSnapshot.Data data, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.ValidateSnapshot( data, catalog ) ).Message, message );
		Rejects( snapshot with { Cells = null! }, "no cell map" );
		Rejects( snapshot with { Objects = null! }, "no object list" );
		Rejects( snapshot with { Cells = snapshot.Cells with { Cells = null! } }, "no cell records" );
		Rejects( snapshot with { Cells = snapshot.Cells with { Cells = [.. snapshot.Cells.Cells, null!] } }, $"cell record {snapshot.Cells.Cells.Count} is missing" );
		Rejects( snapshot with { Objects = [.. snapshot.Objects, null!] }, $"object record {snapshot.Objects.Count} is missing" );
		Rejects( snapshot with { NextSeed = int.MaxValue }, "placement-seed allocator is exhausted" );
		Rejects( snapshot with { NextAttractionId = int.MaxValue }, "attraction-id allocator is exhausted" );
		// The same corruption is rejected by the restore path before a single cell is written.
		var missingRecord = snapshot with { Cells = snapshot.Cells with { Cells = [.. snapshot.Cells.Cells, null!] } };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( missingRecord ) ).Message, "is missing" );
	}

	// --- RestoreObjects pre-placement checks (the placement itself needs GPU; the integration gate covers it) ---

	[TestMethod]
	public void RestoreObjectsValidatesBeforePlacingAndNeverTouchesTheCandidate()
	{
		var (park, catalog, grid) = Jungle();
		var snapshot = EasymodeLayout( park, catalog, grid );
		var candidate = new ParkObjects( catalog, grid, WorldSeed.Default );

		var corrupt = snapshot with { Objects = [.. snapshot.Objects, snapshot.Objects[0] with { InfoId = 999999 }] };
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreObjects( corrupt, candidate ) );
		Assert.AreEqual( 0, candidate.Objects.Count, "a rejected snapshot places nothing" );
		Assert.AreEqual( 1, candidate.NextPlacementSeed, "a rejected snapshot does not move the allocator" );

		var otherSeed = new ParkObjects( catalog, grid, new WorldSeed( 0x0BAD_C0DE_5EED_0001 ) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreObjects( snapshot, otherSeed ) ).Message, "object-script key" );
		Assert.AreEqual( 0, otherSeed.Objects.Count );
	}

	[TestMethod]
	public void RestoreObjectsRejectsACandidateWhoseScriptWorldAlreadyAllocated()
	{
		var (park, catalog, grid) = Jungle();
		var empty = ParkLayoutSnapshot.CaptureSnapshot( new ParkCellMap( 4, 4 ), new ParkObjects( catalog, grid, WorldSeed.Default ) );

		// What default fixed items leave behind after Clear(): objects gone, the allocators still advanced.
		var cleared = new ParkObjects( catalog, grid, WorldSeed.Default );
		cleared.ScriptWorld.AllocateAttractionId();
		var frontier = Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreObjects( empty, cleared ) ).Message;
		StringAssert.Contains( frontier, "next attraction id is already 2" );
		StringAssert.Contains( frontier, "clearing them afterwards leaves the allocators advanced" );
		Assert.AreEqual( 0, cleared.Objects.Count, "the rejection places nothing" );
		Assert.AreEqual( 2, cleared.ScriptWorld.NextAttractionId, "the rejection advances nothing" );

		// A script registered without an attraction allocation is refused just the same.
		var scripted = new ParkObjects( catalog, grid, WorldSeed.Default );
		var scriptedEntry = catalog.Entries.First( entry => entry.ScriptPath != null );
		using ( var stream = scriptedEntry.FileSystem.OpenRead( scriptedEntry.ScriptPath! ) )
			_ = new RideVM( stream, new RideVMOptions { World = scripted.ScriptWorld, Seed = 1, SourceName = scriptedEntry.ScriptPath } );
		StringAssert.Contains(
			Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreObjects( empty, scripted ) ).Message,
			$"already holds {scripted.ScriptWorld.Scripts.Count}" );
	}

	[TestMethod]
	public void RestoreObjectsContinuesTheAllocatorsOfTheSavedPark()
	{
		var (park, catalog, grid) = Jungle();
		var empty = ParkLayoutSnapshot.CaptureSnapshot( new ParkCellMap( 4, 4 ), new ParkObjects( catalog, grid, WorldSeed.Default ) );
		var candidate = new ParkObjects( catalog, grid, WorldSeed.Default );

		Assert.AreEqual( 0, ParkLayoutSnapshot.RestoreObjects( empty with { NextSeed = 7, NextAttractionId = 9 }, candidate ).Count );
		Assert.AreEqual( 7, candidate.NextPlacementSeed, "the next placement draws the saved next seed, not 1" );
		Assert.AreEqual( 9, candidate.ScriptWorld.NextAttractionId, "ids of removed objects stay burned: a guest or economy link to 9 keeps meaning id 9" );

		// A frontier far above the survivors (a park that removed a near-billion objects) is adopted, not replayed:
		// constant-time restore — this returns immediately where a dead-id burn loop would never finish.
		var sparse = new ParkObjects( catalog, grid, WorldSeed.Default );
		Assert.AreEqual( 0, ParkLayoutSnapshot.RestoreObjects( empty with { NextSeed = 7, NextAttractionId = 1_000_000_000 }, sparse ).Count );
		Assert.AreEqual( 1_000_000_000, sparse.ScriptWorld.NextAttractionId );
		Assert.AreEqual( 1_000_000_000, sparse.ScriptWorld.AllocateAttractionId(), "allocation continues from the adopted frontier" );
	}

	[TestMethod]
	public void RestoreStepsCheckTheWholeSnapshotInTheirFirstStepAndPlaceNothingThere()
	{
		var (park, catalog, grid) = Jungle();
		var snapshot = EasymodeLayout( park, catalog, grid );
		var candidate = new ParkObjects( catalog, grid, WorldSeed.Default );
		var placed = new List<OriginalObject>();

		// a record the catalogue does not know, last in the saved order: the first step refuses the snapshot before its first object
		var corrupt = snapshot with { Objects = [.. snapshot.Objects, snapshot.Objects[0] with { InfoId = 999999 }] };
		using var steps = ParkLayoutSnapshot.RestoreObjectSteps( corrupt, candidate, placed ).GetEnumerator();
		Assert.ThrowsException<InvalidDataException>( () => steps.MoveNext() );
		Assert.AreEqual( (0, 0), (candidate.Objects.Count, placed.Count), "a rejected snapshot places nothing" );
		Assert.AreEqual( 1, candidate.NextPlacementSeed, "a rejected snapshot does not move the allocator" );

		// an accepted one: the checks end their step with nothing placed (the first object is the next step's, ObjectAccessCellsTests)
		var accepted = new ParkObjects( catalog, grid, WorldSeed.Default );
		using var restore = ParkLayoutSnapshot.RestoreObjectSteps( snapshot, accepted, placed ).GetEnumerator();
		Assert.IsTrue( restore.MoveNext() );
		Assert.AreEqual( (0f, 0, 0), (restore.Current, accepted.Objects.Count, placed.Count), "the checks' step places nothing" );
	}

	[TestMethod]
	public void RestoreStepsOfALayoutWithoutObjectsCheckItThenRestoreTheAllocators()
	{
		var (park, catalog, grid) = Jungle();
		var empty = ParkLayoutSnapshot.CaptureSnapshot( new ParkCellMap( 4, 4 ), new ParkObjects( catalog, grid, WorldSeed.Default ) );
		var candidate = new ParkObjects( catalog, grid, WorldSeed.Default );
		var placed = new List<OriginalObject>();

		var shares = new List<float>();
		foreach ( var share in ParkLayoutSnapshot.RestoreObjectSteps( empty with { NextSeed = 7, NextAttractionId = 9 }, candidate, placed ) )
		{
			shares.Add( share );
			Assert.AreEqual( share == 1, (candidate.NextPlacementSeed, candidate.ScriptWorld.NextAttractionId) == (7, 9), "the step that ends the restore restores the allocators, the checks' step not" );
		}
		CollectionAssert.AreEqual( new[] { 0f, 1f }, shares );
		Assert.AreEqual( 0, placed.Count );
	}

	// --- the DTOs are JSON data: a serialize/deserialize round-trip keeps them valid and exact ---

	[TestMethod]
	public void SnapshotRoundTripsThroughJsonAndStillValidatesAndRestores()
	{
		var (park, catalog, grid) = Jungle();
		var snapshot = EasymodeLayout( park, catalog, grid );

		var revived = JsonSerializer.Deserialize<ParkLayoutSnapshot.Data>( JsonSerializer.Serialize( snapshot ) )!;
		Assert.AreEqual( (snapshot.Cells.Width, snapshot.Cells.Height, snapshot.Cells.Version), (revived.Cells.Width, revived.Cells.Height, revived.Cells.Version) );
		Assert.AreEqual( snapshot.Cells.Cells.Count, revived.Cells.Cells.Count );
		for ( var index = 0; index < snapshot.Cells.Cells.Count; index++ )
			Assert.AreEqual( snapshot.Cells.Cells[index], revived.Cells.Cells[index], $"cell record {index}" );
		Assert.AreEqual( snapshot.Objects.Count, revived.Objects.Count );
		for ( var index = 0; index < snapshot.Objects.Count; index++ )
			Assert.AreEqual( snapshot.Objects[index], revived.Objects[index], $"object record {index}" );
		Assert.AreEqual( (snapshot.NextSeed, snapshot.NextAttractionId, snapshot.ObjectScriptKey), (revived.NextSeed, revived.NextAttractionId, revived.ObjectScriptKey) );

		ParkLayoutSnapshot.ValidateSnapshot( revived, catalog );
		var fromJson = ParkLayoutSnapshot.RestoreCellMap( revived );
		var fromLive = ParkLayoutSnapshot.RestoreCellMap( snapshot );
		Assert.AreEqual( fromLive.Version, fromJson.Version );
		for ( var y = 0; y < fromLive.Height; y++ )
			for ( var x = 0; x < fromLive.Width; x++ )
				Assert.AreEqual(
					(fromLive.RawTypeAt( x, y ), fromLive.FlagsAt( x, y ), fromLive.PlacementCountAt( x, y ), fromLive.LinksAt( x, y ), fromLive.QueueLinkAt( x, y )),
					(fromJson.RawTypeAt( x, y ), fromJson.FlagsAt( x, y ), fromJson.PlacementCountAt( x, y ), fromJson.LinksAt( x, y ), fromJson.QueueLinkAt( x, y )),
					$"cell ({x}, {y})" );

		// The required members are load-bearing: JSON without them is rejected, not defaulted to a valid park.
		Assert.ThrowsException<JsonException>( () => JsonSerializer.Deserialize<ParkLayoutSnapshot.Data>( "{}" ) );
	}

	// --- the explicit-id CPU factory: saved attraction ids without allocation or dead-id replay ---

	[TestMethod]
	public void ExplicitIdRuntimeFactoryPreservesSavedAttractionIdsWithoutDrawingTheStream()
	{
		var (park, catalog, grid) = Jungle();
		var world = new RideScriptWorld( WorldSeed.Default.ScriptStream );
		var entry = catalog.Get( 1110 );
		var streamState = world.RandomState;

		var first = OriginalObjectRuntime.CreateForRestore( entry, world, seed: 11, open: false, attractionId: 7 );
		Assert.AreEqual( 7, first.AttractionId );
		Assert.AreEqual( 7, first.Visitors.AttractionId );
		Assert.AreEqual( 8, world.NextAttractionId, "the frontier adopts the saved id (the hole below it is skipped)" );
		Assert.IsFalse( first.IsOpen, "the saved open flag reaches the script's VAR_RIDECLOSED" );

		// Explicit ids never rewind the frontier — the layout's strictly-increasing validation is what keeps ids
		// unique — and construction with an explicit seed never draws the world's seed stream.
		var second = OriginalObjectRuntime.CreateForRestore( entry, world, seed: 12, open: true, attractionId: 3 );
		Assert.AreEqual( 3, second.AttractionId );
		Assert.AreEqual( 8, world.NextAttractionId );
		Assert.AreEqual( streamState, world.RandomState, "no hidden draws: the saved park's seed stream continues where it was captured" );

		// A later normal allocation continues from the adopted frontier, holes included.
		var normal = new OriginalObjectRuntime( entry, world, seed: 13 );
		Assert.AreEqual( 8, normal.AttractionId );
		Assert.AreEqual( 9, world.NextAttractionId );
	}
}
