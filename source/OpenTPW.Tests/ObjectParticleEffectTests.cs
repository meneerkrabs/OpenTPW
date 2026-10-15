using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// PTCL-908: the build, demolish, repair and upgrade effects every category .sam names (<c>Info.CreateParticleEffect</c> and its three siblings) start at
/// the object's footprint middle, sized to the footprint, as tp.exe 0x558e7c, 0x5590f7, 0x556DE0 and 0x55A100 do. Synthetic object; the starter is a stub.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectParticleEffectTests
{
	private string root = null!;
	private List<Entity> previousEntities = null!;
	private Func<ParticleEventRequest, int>? previousStarter;
	private Action<int>? previousKiller;
	private readonly List<ParticleEventRequest> started = new();
	private readonly List<int> killed = new();
	private int nextHandle = 100;

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		previousEntities = Entity.All;
		Entity.All = new();
		previousStarter = OriginalObjectRuntime.ParticleStarter;
		previousKiller = OriginalObjectRuntime.ParticleKiller;
		OriginalObjectRuntime.ParticleStarter = request => { started.Add( request ); return nextHandle++; };
		OriginalObjectRuntime.ParticleKiller = handle => killed.Add( handle );
		root = Directory.CreateTempSubdirectory( "opentpw-object-effects-" ).FullName;
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } )
			BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Entity.All = previousEntities;
		OriginalObjectRuntime.ParticleStarter = previousStarter;
		OriginalObjectRuntime.ParticleKiller = previousKiller;
		Directory.Delete( root, true );
	}

	private ObjectCatalogEntry Entry( string info, params string[] shape ) =>
		new( "jungle", ObjectCategory.Ride, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\n" + info, "synthetic.sam" ) } ),
			new ObjectShape( shape ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );

	private const string RideKeys = "Info.CreateParticleEffect 79\nInfo.DestroyParticleEffect 75\nInfo.RepairParticleEffect 51\nInfo.UpgradeParticleEffect 89\n";

	private static OriginalObject Placed( ObjectCatalogEntry entry ) => new( entry, new ObjectPlacement( 3, 4, 0, new N.Vector2( -7, 11 ), 9 ) );

	[TestMethod]
	public void TheCategorySamNamesTheFourEffects()
	{
		var ride = Entry( RideKeys, "*" );
		Assert.AreEqual( (79, 75, 51, 89), (ride.CreateParticleEffect, ride.DestroyParticleEffect, ride.RepairParticleEffect, ride.UpgradeParticleEffect) );
		var feature = Entry( "Info.CreateParticleEffect 82\nInfo.DestroyParticleEffect 78\n", "*" );
		Assert.AreEqual( (82, 78, 0, 0), (feature.CreateParticleEffect, feature.DestroyParticleEffect, feature.RepairParticleEffect, feature.UpgradeParticleEffect), "a feature has no repair or upgrade key: 0 starts nothing" );
	}

	[TestMethod]
	public void BuildDemolishAndUpgradeStartAtTheFootprintMiddleSizedToIt()
	{
		var placed = Placed( Entry( RideKeys, "***", "***" ) );
		var middle = placed.FootprintMiddle();
		foreach ( var (effect, id) in new[] { (ObjectParticleEffect.Create, 79), (ObjectParticleEffect.Destroy, 75), (ObjectParticleEffect.Upgrade, 89) } )
		{
			started.Clear();
			Assert.AreNotEqual( 0, placed.Runtime.StartObjectEffect( effect ), effect.ToString() );
			var request = started.Single();
			Assert.AreEqual( id, request.EffectId );
			Assert.AreEqual( 1000, request.Control );
			Assert.AreEqual( 1, request.Type );
			Assert.AreEqual( middle, request.EnginePosition, "the box middle at the object's base height" );
			Assert.AreEqual( (3 * 5120, 0, 2 * 5120), request.VolumeExtent, "width and depth in original units (10 per cell) times 512, height 0" );
		}
	}

	[TestMethod]
	public void ARotatedFootprintSwapsItsExtent()
	{
		var entry = Entry( RideKeys, "***", "***" );
		var placed = new OriginalObject( entry, new ObjectPlacement( 3, 4, 90, new N.Vector2( -7, 11 ), 9 ) );
		placed.Runtime.StartObjectEffect( ObjectParticleEffect.Create );
		var (x, _, z) = started.Single().VolumeExtent!.Value;
		Assert.AreEqual( 2 * 5120, x );
		Assert.AreEqual( 3 * 5120, z );
	}

	[TestMethod]
	public void NothingStartsWithoutAKeyAFootprintOrAStarter()
	{
		Assert.AreEqual( 0, Placed( Entry( "", "*" ) ).Runtime.StartObjectEffect( ObjectParticleEffect.Create ), "no key: 0" );
		Assert.AreEqual( 0, Placed( Entry( "Info.CreateParticleEffect 0\n", "*" ) ).Runtime.StartObjectEffect( ObjectParticleEffect.Create ) );
		var fixedItem = Placed( Entry( "Info.CreateParticleEffect 79\nInfo.DontApplyOffset 1\n", "*" ) );
		Assert.AreEqual( 0, fixedItem.Runtime.StartObjectEffect( ObjectParticleEffect.Create ), "a fixed item has no footprint box (0x466B70 fails)" );
		OriginalObjectRuntime.ParticleStarter = null;
		Assert.AreEqual( 0, Placed( Entry( RideKeys, "*" ) ).Runtime.StartObjectEffect( ObjectParticleEffect.Create ), "a headless host starts nothing" );
		Assert.AreEqual( 0, started.Count );
	}

	[TestMethod]
	public void AStoppedObjectStartsNothing()
	{
		var placed = Placed( Entry( RideKeys, "*" ) );
		placed.Runtime.Stop();
		Assert.AreEqual( 0, placed.Runtime.StartObjectEffect( ObjectParticleEffect.Create ) );
		Assert.AreEqual( 0, started.Count );
	}

	private static void RepairEffect( OriginalObjectRuntime owner, int value )
	{
		using var data = new MemoryStream( new RideVMTests.Asm().I( Opcode.REPAIREFFECT, value ).I( Opcode.ENDSLICE ).Build( Array.Empty<string>() ) );
		var vm = new RideVM( data, new RideVMOptions { Effects = owner.Script!.Effects } );
		vm.Advance( FixedStepClock.TickDuration );
		Assert.AreNotEqual( RideVMState.Faulted, vm.State );
		vm.Stop();
	}

	[TestMethod]
	public void RepairEffectSwitchesTheTypesRepairEffectOnAndOff()
	{
		var placed = Placed( Entry( RideKeys, "*" ) );
		RepairEffect( placed.Runtime, 1 );
		var request = started.Single();
		Assert.AreEqual( 51, request.EffectId );
		Assert.AreEqual( placed.FootprintMiddle(), request.EnginePosition );
		Assert.IsNull( request.VolumeExtent, "the repair effect keeps its template's volume (0x556DE0 does not size it)" );
		var handle = placed.Runtime.RepairParticleHandle;
		Assert.AreNotEqual( 0, handle );
		RepairEffect( placed.Runtime, 0 );
		CollectionAssert.AreEqual( new[] { handle }, killed, "value 0 ends the effect (life -2)" );
		Assert.AreEqual( 0, placed.Runtime.RepairParticleHandle );
		RepairEffect( placed.Runtime, 0 );
		Assert.AreEqual( 1, killed.Count, "nothing left to end" );
	}

	[TestMethod]
	public void ARepairEffectStillRunningEndsWithTheObject()
	{
		var placed = Placed( Entry( RideKeys, "*" ) );
		RepairEffect( placed.Runtime, 1 );
		var handle = placed.Runtime.RepairParticleHandle;
		placed.Runtime.Stop();
		CollectionAssert.AreEqual( new[] { handle }, killed, "the script's destruction (0x55854b) ends it" );
	}

	[TestMethod]
	public void AnObjectWithoutARepairKeyStartsNoRepairEffect()
	{
		var placed = Placed( Entry( "Info.CreateParticleEffect 82\n", "*" ) );
		RepairEffect( placed.Runtime, 1 );
		Assert.AreEqual( 0, started.Count );
		Assert.AreEqual( 0, placed.Runtime.RepairParticleHandle );
	}

	[TestMethod]
	public void TheShippedCategoriesNameTheirEffects()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var previous = FileSystem;
		var bonus = ObjectCatalog.BonusDataRoot;
		try
		{
			FileSystem = new BaseFileSystem( data );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			ObjectCatalog.BonusDataRoot = null;
			var catalog = ObjectCatalog.Load( "jungle" );
			var rides = catalog.Entries.Where( entry => entry.Category == ObjectCategory.Ride && entry.CreateParticleEffect != 0 ).ToList();
			Assert.IsTrue( rides.Count > 0, "jungle rides name a build effect" );
			Assert.IsTrue( rides.All( entry => (entry.CreateParticleEffect, entry.DestroyParticleEffect, entry.RepairParticleEffect, entry.UpgradeParticleEffect) == (79, 75, 51, 89) ), "rides: 79 / 75 / 51 / 89" );
			Assert.IsTrue( catalog.Entries.Where( entry => entry.Category == ObjectCategory.Shop && entry.CreateParticleEffect != 0 ).All( entry => (entry.CreateParticleEffect, entry.DestroyParticleEffect) == (81, 77) ), "shops: 81 / 77" );
			Assert.IsTrue( catalog.Entries.Where( entry => entry.Category == ObjectCategory.Sideshow ).All( entry => (entry.CreateParticleEffect, entry.DestroyParticleEffect) == (80, 76) ), "sideshows: 80 / 76" );
			Assert.IsTrue( catalog.Entries.Where( entry => entry.Category == ObjectCategory.Upgrade ).All( entry => (entry.CreateParticleEffect, entry.DestroyParticleEffect) == (80, 76) ), "upgrades: 80 / 76" );
			Assert.IsTrue( catalog.Entries.Where( entry => entry.Category is ObjectCategory.Shop or ObjectCategory.Sideshow or ObjectCategory.Feature or ObjectCategory.Upgrade )
				.All( entry => entry.RepairParticleEffect == 0 && entry.UpgradeParticleEffect == 0 ), "only rides have a repair and an upgrade key" );
			Assert.IsTrue( catalog.Entries.Where( entry => entry.Category == ObjectCategory.Feature && entry.CreateParticleEffect != 0 ).All( entry => entry.CreateParticleEffect is >= 79 and <= 82 && entry.DestroyParticleEffect is >= 75 and <= 78 ), "features name 79-82 / 75-78" );
		}
		finally
		{
			FileSystem = previous;
			ObjectCatalog.BonusDataRoot = bonus;
		}
	}
}
