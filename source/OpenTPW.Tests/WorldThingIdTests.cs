using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park's one thing pool as the placed objects, the staff and a park-resume save use it (tp.exe 0x004DB090, 0x00504B10 and 0x00516270 → the base constructor 0x0050AFE0; WALK-010): an object's and a
/// staff member's thing id is the slot their constructor took, a removal is carried out at the turn's end, and a save keeps the pool.
/// </summary>
[TestClass]
[DoNotParallelize]
public class WorldThingIdTests
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
		root = Path.Combine( Path.GetTempPath(), "opentpw-thing-ids-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "synthetic" ) );
		var model = Md2ModelFileTests.CreateGeometry();
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

	private ObjectCatalogEntry Bush() => Entry( 1401, ObjectCategory.Feature, "Info.OverwritePriority 1\n", "*" );

	private ObjectCatalogEntry Entry( int id, ObjectCategory category, string info, params string[] shape ) =>
		new( "jungle", category, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name Synthetic{id}\n{info}", $"synthetic{id}.sam" ) } ),
			new ObjectShape( shape ), "/synthetic/model.MD2", null, "/synthetic/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(),
			Array.Empty<string>(), FileSystem, null );

	private static ParkObjects Park( ThingPool? pool ) => new( null!, new SandboxParkGrid() ) { Things = pool };

	// ---- Placed objects --------------------------------------------------------------------------------

	[TestMethod]
	public void TheFixedItemsOfANewParkAreThings11And12AfterTheWorldsTen()
	{
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		var objects = Park( pool );
		var gates = objects.Add( Bush(), 0, 0, 0 );
		var lights = objects.Add( Entry( 1402, ObjectCategory.Feature, "Info.OverwritePriority 1\n", "*" ), 1, 0, 0 );
		Assert.AreEqual( (11, 12), (gates.ThingId, lights.ThingId), "the shipped jungle save lists them as things 11 and 12" );
		Assert.AreEqual( SaveThingModel.Attraction, pool.ModelOf( 11 ) );
		Assert.AreEqual( 13, pool.NextId );
	}

	/// <summary>The shipped jungle catalog's default fixed items come as Gates then Lights (the order <c>AddDefaultFixedItems</c> places them in), so they are things 11 and 12 behind the world's ten singletons, as the shipped save lists them (Info.Id 1601, 1603).</summary>
	[TestMethod]
	public void TheJungleCatalogsGatesComeBeforeItsLights()
	{
		var previous = ObjectCatalogCorpusTests.UseOriginalData( out var saved );
		if ( previous == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped jungle objects." );
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var entries = ObjectCatalog.Load( "jungle" ).Entries.Where( entry => entry.IsFixedItem && entry.SettingsName is "Gates" or "Lights" ).Select( entry => entry.InfoId ).ToArray();
			CollectionAssert.AreEqual( new[] { 1601, 1603 }, entries );
		}
		finally { FileSystem = saved; }
	}

	[TestMethod]
	public void ARemovedObjectsSlotIsFreedAtTheTurnsEndAndTakenByTheNextObject()
	{
		var pool = new ThingPool();
		var objects = Park( pool );
		var first = objects.Add( Bush(), 0, 0, 0 );
		var second = objects.Add( Bush(), 2, 0, 0 );
		var third = objects.Add( Bush(), 4, 0, 0 );
		Assert.AreEqual( new[] { 1, 2, 3 }.Sum(), first.ThingId + second.ThingId + third.ThingId );
		Assert.IsTrue( objects.Remove( first ) );
		Assert.IsTrue( objects.Remove( third ) );
		Assert.IsTrue( pool.IsUsed( 1 ) && pool.IsUsed( 3 ), "the destruction is asked for, and carried out at the turn's end" );
		var fresh = objects.Add( Bush(), 6, 0, 0 );
		Assert.AreEqual( 4, fresh.ThingId, "an object built in the same turn takes a new slot" );
		CollectionAssert.AreEqual( new[] { 3, 1 }, pool.DestroyQueued().ToArray(), "the queue's head, the demolition asked for last, is freed first" );
		Assert.AreEqual( 1, objects.Add( Bush(), 8, 0, 0 ).ThingId, "the demolition asked for first is the next object's slot" );
	}

	[TestMethod]
	public void AParkThatClearsItselfFreesTheSlotsAtOnce()
	{
		var pool = new ThingPool();
		var objects = Park( pool );
		objects.Add( Bush(), 0, 0, 0 );
		objects.Add( Bush(), 2, 0, 0 );
		objects.Clear();
		Assert.AreEqual( 0, pool.Count );
		Assert.AreEqual( 0, pool.QueuedCount );
	}

	[TestMethod]
	public void ARestoredObjectKeepsItsSavedSlotAndARefusedOneRestoresNothing()
	{
		var pool = new ThingPool();
		var objects = Park( pool );
		var kept = objects.AddRestored( Bush(), 0, 0, 0, true, 5, 9, thingId: 40 );
		Assert.AreEqual( 40, kept.ThingId );
		Assert.AreEqual( SaveThingModel.Attraction, pool.ModelOf( 40 ) );
		Assert.AreEqual( 1, pool.NextId, "the slots below it stay free" );
		Assert.ThrowsException<InvalidDataException>( () => objects.AddRestored( Bush(), 2, 0, 0, true, 6, 10, thingId: 40 ), "a slot an object holds cannot be named twice" );
		pool.Occupy( 41, SaveThingModel.Guest );
		Assert.ThrowsException<InvalidDataException>( () => objects.AddRestored( Bush(), 2, 0, 0, true, 6, 10, thingId: 41 ), "a guest's slot is not an attraction's" );
		var unnumbered = objects.AddRestored( Bush(), 4, 0, 0, true, 7, 11 );
		Assert.AreEqual( 1, unnumbered.ThingId, "an object of a save from before the pool takes the next free slot" );
	}

	[TestMethod]
	public void AnObjectOfAParkWithoutAPoolHasNoThingId()
	{
		var item = Park( null ).Add( Bush(), 0, 0, 0 );
		Assert.AreEqual( 0, item.ThingId );
	}

	[TestMethod]
	public void TheLayoutKeepsTheThingIdsAndValidatesThem()
	{
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		var objects = Park( pool );
		var bush = Bush();
		objects.Add( bush, 0, 0, 0 );
		objects.Add( bush, 2, 0, 0 );
		var cells = new GuestPathGrid( ParkPlacement.TileCount, ParkPlacement.TileCount ).Cells;
		var snapshot = ParkLayoutSnapshot.CaptureSnapshot( cells, objects );
		CollectionAssert.AreEqual( new[] { 11, 12 }, snapshot.Objects.Select( record => record.ThingId ).ToArray() );
		Assert.AreEqual( 0, ParkLayoutSnapshot.CaptureSnapshot( cells, Park( null ) ).Objects.Count );
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "jungle", new[] { bush } }, null )!;
		ParkLayoutSnapshot.ValidateSnapshot( snapshot, catalog );
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.ValidateSnapshot( snapshot with { Objects = snapshot.Objects.Select( record => record with { ThingId = 11 } ).ToList() }, catalog ), "two objects share a thing id" );
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.ValidateSnapshot( snapshot with { Objects = snapshot.Objects.Select( record => record with { ThingId = ThingPool.Capacity + 1 } ).ToList() }, catalog ), "a thing id outside the pool" );
	}

	// ---- Staff -------------------------------------------------------------------------------------------

	[TestMethod]
	public void AHiredMemberTakesASlotOfItsRolesModelAndAFiredOneFreesItAtTheTurnsEnd()
	{
		var economy = EconomyTestData.Park();
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		economy.Staff.AttachThings( pool );
		var handyman = EconomyTestData.HireBest( economy, StaffType.Handyman );
		var guard = EconomyTestData.HireBest( economy, StaffType.Guard );
		Assert.AreEqual( (11, 12), (handyman.ThingId, guard.ThingId) );
		Assert.AreEqual( (SaveThingModel.Handyman, SaveThingModel.Guard), (pool.ModelOf( 11 ), pool.ModelOf( 12 )) );
		Assert.AreEqual( 11, handyman.PhaseId, "the thing-keyed rules read the thing id" );
		economy.Fire( handyman.Id );
		Assert.IsTrue( pool.IsUsed( 11 ) );
		pool.DestroyQueued();
		Assert.IsFalse( pool.IsUsed( 11 ) );
		Assert.AreEqual( 11, EconomyTestData.HireBest( economy, StaffType.Mechanic ).ThingId );
	}

	[TestMethod]
	public void AnEconomyWithoutAPoolKeepsItsRosterIdsAsPhaseIds()
	{
		var economy = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( economy, StaffType.Handyman );
		Assert.AreEqual( 0, member.ThingId );
		Assert.AreEqual( member.Id, member.PhaseId );
	}

	[TestMethod]
	public void MembersAlreadyEmployedTakeSlotsWhenAPoolIsAttached()
	{
		var economy = EconomyTestData.Park();
		var older = EconomyTestData.HireBest( economy, StaffType.Entertainer );
		var pool = new ThingPool();
		pool.Occupy( 1, SaveThingModel.Guest );
		economy.Staff.AttachThings( pool );
		Assert.AreEqual( 2, older.ThingId, "a member without a thing id takes the next free slot" );
		Assert.AreEqual( SaveThingModel.Entertainer, pool.ModelOf( 2 ) );
		var shared = new ThingPool();
		shared.Occupy( 2, SaveThingModel.Attraction );
		Assert.ThrowsException<InvalidDataException>( () => economy.Staff.AttachThings( shared ), "the member's slot is held by an attraction" );
	}

	[TestMethod]
	public void TheEconomySaveKeepsAMembersThingIdAndRefusesAnOutOfRangeOne()
	{
		var economy = EconomyTestData.Park();
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		economy.Staff.AttachThings( pool );
		var member = EconomyTestData.HireBest( economy, StaffType.Researcher );
		var json = ParkSaveFile.Serialize( economy );
		var data = ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) );
		Assert.AreEqual( 11, data.Staff.Members.Single( item => item.Id == member.Id ).ThingId );
		StringAssert.DoesNotMatch( ParkSaveFile.Serialize( EconomyTestData.Park() ), new System.Text.RegularExpressions.Regex( "ThingId" ), "an economy without things writes no thing id" );
		var restored = ParkSaveFile.Restore( data, economy.Settings, economy.Catalog );
		Assert.AreEqual( 11, restored.Staff.Members.Single().ThingId );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json.Replace( "\"ThingId\": 11", $"\"ThingId\": {ThingPool.Capacity + 1}" ) ) ), economy.Settings, economy.Catalog ) );
	}
}
