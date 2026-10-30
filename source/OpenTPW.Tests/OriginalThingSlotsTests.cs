using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// An imported original save keeps the slots of the things OpenTPW does not rebuild (tp.exe's load 0x00518D43 keeps every saved thing in its slot and rebuilds the free list from the
/// rest), so the next thing built gets the id the original would hand out (WALK-010).
/// </summary>
[TestClass]
public class OriginalThingSlotsTests
{
	private BaseFileSystem? originalFileSystem;

	[TestInitialize]
	public void Setup()
	{
		originalFileSystem = FileSystem;
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = originalFileSystem!;

	private static ThingPool OriginalsLoad( System.Collections.Generic.IEnumerable<SaveThing> chain )
	{
		var pool = new ThingPool();
		pool.Restore( chain.Select( thing => new ThingSlot( thing.Id, thing.Model ) ) );
		return pool;
	}

	private static System.Collections.Generic.HashSet<int> Ids( System.Collections.Generic.IEnumerable<int> ids ) => ids.ToHashSet();

	[TestMethod]
	public void UnbuiltGuestsObjectsAndStaffKeepTheirSlotsAndTheSingletonsAreLeftAlone()
	{
		var pool = new ThingPool();
		pool.Restore( new[]
		{
			new ThingSlot( 20, SaveThingModel.Guest ), new ThingSlot( 15, SaveThingModel.Attraction ), new ThingSlot( 14, SaveThingModel.Mechanic ), new ThingSlot( 13, SaveThingModel.Guest ),
			new ThingSlot( 12, SaveThingModel.Attraction ), new ThingSlot( 11, SaveThingModel.Researcher ), new ThingSlot( 3, SaveThingModel.Weather ), new ThingSlot( 1, SaveThingModel.StaffHq )
		} );
		var expected = new ThingPool();
		expected.Restore( pool.Capture() );
		var held = OriginalThingSlots.HoldUnbuilt( pool, Ids( new[] { 12 } ), Ids( new[] { 13 } ), Ids( new[] { 11 } ) );
		CollectionAssert.AreEqual( new[] { 20, 15, 14 }, held.ToArray(), "the guest, object and staff member nothing took, newest first" );
		CollectionAssert.AreEqual( new[] { 14, 15, 20 }, pool.HeldIds().ToArray() );
		Assert.IsFalse( pool.IsHeld( 3 ) || pool.IsHeld( 1 ), "the world's singletons are kept as they are" );
		Assert.AreEqual( expected.Count, pool.Count );
		CollectionAssert.AreEqual( expected.FreeInOrder().Take( 12 ).ToArray(), pool.FreeInOrder().Take( 12 ).ToArray() );
		Assert.AreEqual( 2, pool.Allocate( SaveThingModel.Guest ), "the original's next id: the lowest slot the save leaves free" );
		Assert.AreEqual( 4, pool.Allocate( SaveThingModel.Guest ) );
		Assert.AreEqual( 3, pool.HeldCount, "the two guests built since are not held" );
	}

	/// <summary>The shipped Easymode save: after the import the next thing built gets the id the original's load leaves next (43), the free list is the original's, and every saved thing still holds its slot.</summary>
	[TestMethod]
	public void AfterTheEasymodeImportTheNextIdIsTheOriginals()
	{
		var (save, chain) = ReadEasymode();
		var original = OriginalsLoad( chain );
		Assert.AreEqual( 43, original.NextId, "the original's load rebuilds the free list ascending above the 42 saved things" );
		var pool = OriginalsLoad( chain );
		var simulation = new GuestSimulation( GuestPathGrid.FromOriginal( save.Map, save ), GuestSettings.Load( "jungle" ), 99 ) { ArrivalsEnabled = false };
		simulation.AttachThings( pool );
		simulation.ImportOriginal( save.Guests, save.World.Variables.GameTick, _ => 0, _ => 0 );
		Assert.AreEqual( 13, simulation.Guests.Count );
		OriginalThingSlots.HoldUnbuilt( pool, Ids( save.Attractions.Select( item => item.Id ) ), Ids( simulation.Guests.Select( guest => guest.Id ) ), Ids( save.Staff.Select( item => item.Id ) ) );
		Assert.AreEqual( 0, pool.HeldCount, "Easymode's 42 things are all rebuilt, so nothing is held" );
		Assert.AreEqual( original.Count, pool.Count );
		Assert.AreEqual( original.NextId, pool.NextId );
		CollectionAssert.AreEqual( original.FreeInOrder().Take( 20 ).ToArray(), pool.FreeInOrder().Take( 20 ).ToArray() );
		Assert.AreEqual( original.Allocate( SaveThingModel.Guest ), pool.Allocate( SaveThingModel.Guest ) );
		Assert.AreEqual( 43, pool.UsedNewestFirst().First().Id, "the thing built next is the newest of the used list" );
	}

	/// <summary>
	/// The same save with guests in states the import drops, a guest of an unknown type, a guest outside the map, an unresolved object and a staff member that is not employed: each keeps its slot, so the
	/// next ids are the ones the original's load gives (43, 44, ...), not the freed slots taken last in, first out.
	/// </summary>
	[TestMethod]
	public void DroppedThingsOfTheEasymodeSaveKeepTheirSlots()
	{
		var (save, chain) = ReadEasymode();
		var original = OriginalsLoad( chain );
		var pool = OriginalsLoad( chain );
		var simulation = new GuestSimulation( GuestPathGrid.FromOriginal( save.Map, save ), GuestSettings.Load( "jungle" ), 99 ) { ArrivalsEnabled = false };
		simulation.AttachThings( pool );
		var guests = save.Guests.Select( guest => guest.Id switch
		{
			29 => guest with { State = 16 },
			31 => guest with { State = 12 },
			34 => guest with { PersonType = 200 },
			40 => guest with { State = 16 },
			_ => guest
		} ).ToList();
		var report = simulation.ImportOriginal( guests, save.World.Variables.GameTick, _ => 0, _ => 0 );
		Assert.AreEqual( 9, simulation.Guests.Count, string.Join( "; ", report ) );
		var objects = save.Attractions.Select( item => item.Id ).Where( id => id != 24 );
		var staff = save.Staff.Select( item => item.Id ).Where( id => id != 30 );
		OriginalThingSlots.HoldUnbuilt( pool, Ids( objects ), Ids( simulation.Guests.Select( guest => guest.Id ) ), Ids( staff ) );
		CollectionAssert.AreEqual( new[] { 24, 29, 30, 31, 34, 40 }, pool.HeldIds().ToArray(), "four dropped guests, the unresolved object and the staff member nobody employs" );
		Assert.AreEqual( original.Count, pool.Count, "every saved thing still holds a slot" );
		CollectionAssert.AreEqual( original.UsedNewestFirst().ToArray(), pool.UsedNewestFirst().ToArray() );
		Assert.AreEqual( 43, pool.NextId );
		CollectionAssert.AreEqual( new[] { 43, 44, 45 }, Enumerable.Range( 0, 3 ).Select( _ => pool.Allocate( SaveThingModel.Guest ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 43, 44, 45 }, Enumerable.Range( 0, 3 ).Select( _ => original.Allocate( SaveThingModel.Guest ) ).ToArray(), "the original hands out the same ids" );
		// the held things survive the park's own save
		var snapshot = pool.CaptureSnapshot();
		CollectionAssert.AreEqual( new[] { 24, 29, 30, 31, 34, 40 }, snapshot.Held!.ToArray() );
		var resumed = new ThingPool();
		resumed.RestoreSnapshot( snapshot );
		Assert.AreEqual( pool.NextId, resumed.NextId );
		CollectionAssert.AreEqual( pool.HeldIds().ToArray(), resumed.HeldIds().ToArray() );
	}

	private (OriginalParkImport Save, System.Collections.Generic.IReadOnlyList<SaveThing> Chain) ReadEasymode()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ) );
		var save = OriginalParkImport.Import( OriginalParkImportTests.ReadEasymodePayload(), map );
		return (save, save.World.Things);
	}
}
