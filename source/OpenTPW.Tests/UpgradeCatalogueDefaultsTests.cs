using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

// Actual mapped economy completion and restore-stage catalogue metadata controls.
[TestClass, DoNotParallelize]
public class UpgradeCatalogueDefaultsTests
{
	private string root = null!;
	private List<Entity> previous = null!;
	private Logger logger = null!;
	private GameEdition edition;
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object o, string f, object v ) => o.GetType().GetField( f, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, v );
	private static object? Call( object o, string name, params object[] args ) => o.GetType().GetMethod( name, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( o, args );
	[TestInitialize]
	public void Setup()
	{
		previous = Entity.All; Entity.All = new(); logger = Log; Log = new(); edition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld );
		root = Path.Combine( Path.GetTempPath(), "opentpw-tier-audit-" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( root );
		var bytes = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
		foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), bytes );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.COPY, RideVMTests.V( (int)RideVariables.VAR_CAPACITY ), 9 ).I( Opcode.COPY, RideVMTests.V( (int)RideVariables.VAR_DURATION ), 77 ).I( Opcode.COPY, RideVMTests.V( (int)RideVariables.VAR_ONRIDE ), 1 ).I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
	}
	[TestCleanup]
	public void Cleanup() { foreach ( var item in Entity.All.ToArray() ) item.Delete(); Entity.All = previous; Log = logger; GameEditions.Select( edition ); Directory.Delete( root, true ); }
	private ObjectCatalogEntry Entry( string extra = "", bool sparse = false )
	{
		var sam = "Info.Id 1100\nInfo.Name tier-test\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nInfo.HasQueue 1\nUsageInfo.MinCapacity 1\nUsageInfo.MaxCapacity 100\nUsageInfo.MinDuration 1\nUsageInfo.MaxDuration 200\nUpgrades[0].InitCapacity 1\nUpgrades[0].InitDuration 10\nUpgrades[0].InitSpeed 50\nUpgrades[0].QueueWaitTimeConstant 130\nUpgrades[1].InitCapacity 3\nUpgrades[1].InitDuration 20\nUpgrades[1].InitSpeed 100\nUpgrades[1].QueueWaitTimeConstant 200\n";
		if ( sparse ) sam = string.Join( "\n", sam.Split( '\n' ).Where( line => !line.StartsWith( "Upgrades[1]", StringComparison.Ordinal ) ) ); sam += "\n" + extra;
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/tier-test", new ObjectSettings( new[] { new ObjectSettingsFile( sam, "tier-test.sam" ) } ), new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
	}
	private (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) World( string extra = "", int initialTier = 0, bool sparse = false )
	{
		var level = Empty<Level>(); var economy = EconomyTestData.Park(); var park = ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ); var objects = new ParkObjects( null!, new SandboxParkGrid() );
		Set( level, "<Park>k__BackingField", park ); Set( level, "<Objects>k__BackingField", objects ); Set( level, "economyInstances", new Dictionary<OriginalObject, int>() );
		var first = new OriginalObject( Entry( extra, sparse ), new ObjectPlacement( 1, 1, 0, System.Numerics.Vector2.Zero, 0 ), objects.ScriptWorld, seed: 1 ^ objects.ObjectScriptKey );
		var second = new OriginalObject( first.Entry, new ObjectPlacement( 3, 3, 0, System.Numerics.Vector2.Zero, 0 ), objects.ScriptWorld, seed: 2 ^ objects.ObjectScriptKey ); ((List<OriginalObject>)objects.Objects).AddRange( new[] { first, second } ); Set( objects, "nextSeed", 3 );
		var state = economy.RegisterExisting( 1100 ); state.Level = initialTier; var other = economy.RegisterExisting( 1100 ); Call( level, "LinkEconomy", first, state.Id ); Call( level, "LinkEconomy", second, other.Id ); Call( level, "SetupParkGate" );
		return (level, economy, first, second, state);
	}
	private static void Complete( (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) w, int targetTier = 1 )
	{
		EconomyTestData.HireBest( w.Economy, StaffType.Researcher ); w.Economy.SetResearchEffort( ResearchCategory.Ride, 0 );
		for ( var i = 0; i < 100 && !w.Economy.Research.IsAvailable( 1100, targetTier ); i++ ) w.Economy.AdvanceDays( 1 );
		Assert.IsTrue( w.Economy.Research.IsAvailable( 1100, targetTier ) ); EconomyTestData.HireBest( w.Economy, StaffType.Mechanic );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, w.Economy.TryBuyUpgrade( w.State.Id ) ); Assert.IsFalse( w.One.IsOpen, "Actual Level UpgradeBought hook closes only the selected owner." );
		for ( var i = 0; i < 20 && w.State.Level < targetTier; i++ ) w.Economy.AdvanceDays( 1 );
		Assert.AreEqual( targetTier, w.State.Level ); Assert.AreEqual( 0, w.State.PendingLevel ); Assert.IsTrue( w.One.IsOpen, "Actual UpgradeCompleted hook reopens the same runtime." );
	}
	[TestMethod]
	public void CompletedMappedTierMustReachQueueDefaults()
	{
		var w = World(); Complete( w ); var p = w.One.Visitors.Parameters; Console.WriteLine( $"economyLevel={w.State.Level}; runtime defaults={p}; fallbackCapacity={w.One.Visitors.Capacity}" );
		Assert.AreEqual( 200f, p.QueueWaitTimeConstant, "Economy Level1 completed, but runtime QWTC remains the tier0 value130." );
		Assert.AreEqual( 100, p.InitSpeed ); Assert.AreEqual( 20, p.InitDuration ); Assert.AreEqual( 30u, RideVisitorBridge.ComputeQueueLimit( p ) );
	}
	[TestMethod]
	public void CompletedMappedTierMustReachFallbackCapacity()
	{
		var w = World(); Complete( w ); Assert.AreEqual( 3, w.One.Visitors.Capacity, "Fallback admission capacity remains tier0 although the mapped economy instance is tier1." );
	}
	[TestMethod]
	public void RebuiltBindingFromSavedCompletedTierMustDeriveTierDefaults()
	{
		var w = World(); Complete( w ); var saved = ParkSaveFile.Serialize( w.Economy ); var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( saved ) ), EconomyTestData.Settings(), EconomyTestData.Catalog() );
		Assert.IsTrue( restored.TryGetObject( w.State.Id, out var state ) ); Assert.AreEqual( 1, state.Level );
		var level = Empty<Level>(); var objects = new ParkObjects( null!, new SandboxParkGrid() ); Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( restored, ParkStartKind.FullSimulation ) ); Set( level, "<Objects>k__BackingField", objects ); Set( level, "economyInstances", new Dictionary<OriginalObject, int>() );
		var candidate = new OriginalObject( w.One.Entry, new ObjectPlacement( 1, 1, 0, System.Numerics.Vector2.Zero, 0 ), objects.ScriptWorld ); ((List<OriginalObject>)objects.Objects).Add( candidate ); Call( level, "LinkEconomy", candidate, state.Id );
		Assert.AreEqual( 200f, candidate.Visitors.Parameters.QueueWaitTimeConstant, "Actual restored instance binding does not consume the saved Level1." );
	}
	[TestMethod]
	public void CompletionWritesTheLevelsCapacityAndDurationOverScriptValuesAndKeepsHeldSeats()
	{
		var w = World(); w.One.Runtime.Simulate( .001 ); Assert.AreEqual( 9, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 77, w.One.Runtime.GetVariable( RideVariables.VAR_DURATION ) );
		var heads = RideHeadSpace.Of( new[] { 0, 0 }, 1 ); heads.Add( 42 ); w.One.Visitors.Heads = heads;
		w.One.Visitors.RecomputeQueue( new GuestPathGrid( 3, 3 ) );
		var visitors = w.One.Visitors.CaptureSnapshot(); w.One.Visitors.RestoreSnapshot( visitors with { OnRide = new[] { 42 }, BoardedTotal = 1 } );
		var before = heads.CaptureSnapshot(); Complete( w );
		Assert.AreEqual( 3, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 20, w.One.Runtime.GetVariable( RideVariables.VAR_DURATION ) ); Assert.AreSame( heads, w.One.Visitors.Heads ); CollectionAssert.AreEqual( before.Visitors, heads.CaptureSnapshot().Visitors ); CollectionAssert.AreEqual( new[] { 42 }, w.One.Visitors.Riders.ToArray() );
	}
	/// <summary>QUEUE-011: the completion 0x4df8f0 writes CAP through SetCapacity 0x4dd7f0 (clamped to Min/MaxCapacity) and DUR clamped to Min/MaxDuration, each only when the level's value is above 0.</summary>
	[TestMethod]
	public void CompletionClampsTheLevelsCapacityAndDurationAndKeepsSettingsTheLevelLeavesAtZero()
	{
		var w = World( "Info.DurationUnit 1\nUpgrades[2].InitCapacity 250\nUpgrades[2].InitDuration 0\nUpgrades[2].InitSpeed 0\nUpgrades[2].QueueWaitTimeConstant 300\n" );
		Complete( w );
		Assert.AreEqual( new RideSettingWords( 100, 3, 20 ), w.One.Runtime.Settings );
		w.One.Runtime.ApplyRideWindow( new RideSettingWords( 77, 2, 15 ) );
		w.One.Runtime.Simulate( .001 );
		Complete( w, 2 );
		Assert.AreEqual( 100, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ), "InitCapacity 250 is clamped to MaxCapacity 100" );
		Assert.AreEqual( new RideSettingWords( 77, 100, 77 ), w.One.Runtime.Settings with { Duration = w.One.Runtime.GetVariable( RideVariables.VAR_DURATION ) },
			"InitSpeed and InitDuration 0 write nothing: the player's speed stays; DUR is the script's own 77" );
		Assert.AreEqual( 15, w.One.Runtime.Settings.Duration, "the DUR word keeps the player's setting" );
	}
	/// <summary>WALK-001: the shop sale reads the object's amount of the special ingredient, which the level mirrors from its economy record each tick.</summary>
	[TestMethod]
	public void TheLevelMirrorsTheEconomyRecordsIngredientAmountToTheBridge()
	{
		var w = World(); Assert.AreEqual( GuestSpeed.DefaultIngredientAmount, w.One.Visitors.IngredientAmount );
		w.State.IngredientAmount = 80; w.State.QualityOfGoods = 100; Call( w.Level, "SyncObjectEconomy" ); Assert.AreEqual( 80, w.One.Visitors.IngredientAmount );
		Assert.AreEqual( 100, w.One.Visitors.QualityOfGoods, "ECON-044: the balloon's life reads the record's quality of goods" );
		Assert.AreEqual( GuestSpeed.DefaultIngredientAmount, w.Two.Visitors.IngredientAmount, "only the record's own object" );
	}
	/// <summary>The completion's common entry (0x4df8f0) sets the state of repair to 100.0 before its upgrade branch.</summary>
	[TestMethod]
	public void AnUpgradeCompletionRestoresTheStateOfRepair()
	{
		var w = World(); w.State.Repair = 62.5; Complete( w ); Assert.AreEqual( 100.0, w.State.Repair );
	}
	[TestMethod]
	public void SameCatalogueOtherOwnerStaysAtTierZero()
	{
		var w = World(); Complete( w ); Assert.AreEqual( 0, w.Economy.Objects.Single( s => s.Id == w.Level.GetEconomyInstance( w.Two ) ).Level ); Assert.AreEqual( 130f, w.Two.Visitors.Parameters.QueueWaitTimeConstant ); Assert.AreEqual( 1, w.Two.Visitors.Capacity );
	}
	[TestMethod]
	public void SavedVmValuesOverrideConstructorDefaultsDuringRuntimeRestore()
	{
		var entry = Entry(); var world = new RideScriptWorld(); var source = new OriginalObjectRuntime( entry, world, seed: 7 ); source.Simulate( .001 );
		var runtime = source.CaptureSnapshot(); var scripts = world.CaptureSnapshot(); var restoredWorld = new RideScriptWorld(); var candidate = OriginalObjectRuntime.CreateForRestore( entry, restoredWorld, 7, source.IsOpen, runtime.AttractionId );
		Assert.AreEqual( 1, candidate.GetVariable( RideVariables.VAR_CAPACITY ) ); candidate.RestoreSnapshot( runtime ); restoredWorld.RestoreSnapshot( scripts, new Dictionary<int, RideVM> { { source.Script!.ScriptId, candidate.Script! } } );
		Assert.AreEqual( 9, candidate.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 77, candidate.GetVariable( RideVariables.VAR_DURATION ) ); Assert.AreEqual( 1, candidate.GetVariable( RideVariables.VAR_ONRIDE ) );
	}

	private const string Tier2 = "Upgrades[2].InitCapacity 5\nUpgrades[2].InitDuration 30\nUpgrades[2].InitSpeed 150\nUpgrades[2].QueueWaitTimeConstant 300\n";
	/// <summary>RIDES-033: upgrade completion 0x4df8f0 writes Upgrades[level].InitSpeed (when above 0) to the script's speed bias, like ride init does for level 0.</summary>
	[TestMethod]
	public void CompletedTierSetsTheScriptSpeedBiasToThatLevelsInitSpeed()
	{
		var w = World( Tier2 );
		Assert.AreEqual( (50, 1f), (w.One.Script!.SpeedBias, w.One.Script.ScriptSpeed) );
		Complete( w );
		Assert.AreEqual( (100, 1.5f), (w.One.Script.SpeedBias, w.One.Script.ScriptSpeed) );
		w.One.Runtime.Simulate( .001 );
		Complete( w, 2 );
		Assert.AreEqual( 150, w.One.Script.SpeedBias );
		Assert.AreEqual( 50, w.Two.Script!.SpeedBias, "only the upgraded owner changes" );
	}
	[TestMethod]
	public void CompletedTierWithoutInitSpeedKeepsTheScriptSpeedBias()
	{
		var w = World( "Upgrades[2].InitCapacity 5\nUpgrades[2].QueueWaitTimeConstant 300\n" );
		Complete( w );
		Assert.AreEqual( 100, w.One.Script!.SpeedBias );
		w.One.Runtime.Simulate( .001 );
		Complete( w, 2 );
		Assert.AreEqual( 100, w.One.Script.SpeedBias, "a level whose InitSpeed is not above 0 writes nothing" );
	}
	[TestMethod]
	public void FreshAndImportedBindingsSeedRequestedTierBeforeExecution()
	{
		var w = World( Tier2, initialTier: 2 ); Assert.AreEqual( 0L, w.One.Script!.ExecutedInstructions ); Assert.AreEqual( 5, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 30, w.One.Runtime.GetVariable( RideVariables.VAR_DURATION ) ); Assert.AreEqual( 2, w.One.Runtime.CatalogueTier ); Assert.AreEqual( 150, w.One.Visitors.Parameters.InitSpeed ); Assert.AreEqual( 1, w.Two.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) );
	}
	[TestMethod]
	public void TwoActualCompletedTiersPreserveLiveSlotsAndDoNotReseedAtZeroInstructions()
	{
		var w = World( Tier2 ); Complete( w ); Assert.AreEqual( 3, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ), "the completion writes the level's CAP and DUR (0x4df8f0), even before the first slice" ); Assert.AreEqual( 20, w.One.Runtime.GetVariable( RideVariables.VAR_DURATION ) );
		w.One.Runtime.Simulate( .001 ); Complete( w, 2 ); Assert.AreEqual( 2, w.One.Runtime.CatalogueTier ); Assert.AreEqual( 5, w.One.Visitors.Capacity ); Assert.AreEqual( 300f, w.One.Visitors.Parameters.QueueWaitTimeConstant ); Assert.AreEqual( 5, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 30, w.One.Runtime.GetVariable( RideVariables.VAR_DURATION ) );
		var parameters = w.One.Visitors.Parameters; var vars = w.One.Script!.Variables.ToArray(); var flags = w.One.Script.Flags; Call( w.Level, "ApplyCompletedCatalogueTier", w.State.Id ); Assert.AreSame( parameters, w.One.Visitors.Parameters ); CollectionAssert.AreEqual( vars, w.One.Script.Variables ); Assert.AreEqual( flags, w.One.Script.Flags );
	}
	[TestMethod]
	public void SavedTierTwoMetadataIsDerivedBeforeSavedVmSlotsOverwriteSeeds()
	{
		var w = World( Tier2 ); w.One.Runtime.Simulate( .001 ); Complete( w ); Complete( w, 2 ); var runtime = w.One.Runtime.CaptureSnapshot(); var scripts = w.Level.Objects.ScriptWorld.CaptureSnapshot(); var data = ParkSaveFile.Capture( w.Economy ); var restored = ParkSaveFile.Restore( data, EconomyTestData.Settings(), EconomyTestData.Catalog() );
		var level = Empty<Level>(); var objects = new ParkObjects( null!, new SandboxParkGrid() ); Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( restored, ParkStartKind.FullSimulation ) ); Set( level, "<Objects>k__BackingField", objects ); Set( level, "economyInstances", new Dictionary<OriginalObject, int>() );
		var one = new OriginalObject( w.One.Entry, w.One.Placement, objects.ScriptWorld, 1 ^ objects.ObjectScriptKey, true, runtime.AttractionId ); var two = new OriginalObject( w.Two.Entry, w.Two.Placement, objects.ScriptWorld, 2 ^ objects.ObjectScriptKey, true, w.Two.Runtime.AttractionId ); ((List<OriginalObject>)objects.Objects).AddRange( new[] { one, two } ); Call( level, "LinkEconomy", one, w.State.Id ); Call( level, "LinkEconomy", two, w.Level.GetEconomyInstance( w.Two )!.Value );
		Assert.AreEqual( 5, one.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 2, one.Runtime.CatalogueTier ); one.Runtime.RestoreSnapshot( runtime ); objects.ScriptWorld.RestoreSnapshot( scripts, new Dictionary<int, RideVM> { { w.One.Script!.ScriptId, one.Script! }, { w.Two.Script!.ScriptId, two.Script! } } );
		Assert.AreEqual( 5, one.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 30, one.Runtime.GetVariable( RideVariables.VAR_DURATION ) ); Assert.AreEqual( 300f, one.Visitors.Parameters.QueueWaitTimeConstant ); Assert.AreEqual( 5, one.Visitors.Capacity );
	}
	[TestMethod]
	public void SparseRowsNeverFallBackAndNonzeroReconstructionFailsExplicitly()
	{
		var w = World( Tier2, sparse: true ); var before = w.One.Visitors.Parameters; Assert.IsFalse( w.One.Runtime.TryApplyCatalogueTierDefaults( 1, out var reason ) ); StringAssert.Contains( reason, "no exact SAM" ); Assert.AreSame( before, w.One.Visitors.Parameters ); Assert.AreEqual( 0, w.One.Runtime.CatalogueTier ); Assert.IsTrue( w.One.Runtime.TryApplyCatalogueTierDefaults( 2, out _ ) ); Assert.AreEqual( 5, w.One.Visitors.Capacity );
		Assert.ThrowsException<InvalidDataException>( () => OriginalObjectRuntime.CreateQueueParameters( w.One.Entry, 1 ) );
		var failed = Assert.ThrowsException<TargetInvocationException>( () => World( Tier2, initialTier: 1, sparse: true ) ); Assert.IsInstanceOfType( failed.InnerException, typeof( InvalidDataException ) );
	}
	[DataTestMethod, DataRow( "equalCustom" ), DataRow( "custom" ), DataRow( "stop" ), DataRow( "tpi" ), DataRow( "negative" ), DataRow( "missing" )]
	public void UnsupportedRuntimeUpdatesRetainAllExistingState( string gate )
	{
		var w = World(); if ( gate == "equalCustom" ) w.One.Visitors.Parameters = w.One.Visitors.Parameters with { }; if ( gate == "custom" ) w.One.Visitors.Parameters = w.One.Visitors.Parameters with { Speed = 123 }; if ( gate == "stop" ) w.One.Runtime.Stop(); if ( gate == "tpi" ) GameEditions.Select( GameEdition.ThemeParkInc );
		var vars = w.One.Script!.Variables.ToArray(); var p = w.One.Visitors.Parameters; var capacity = w.One.Visitors.Capacity; Assert.IsFalse( w.One.Runtime.TryApplyCatalogueTierDefaults( gate == "negative" ? -1 : gate == "missing" ? 10 : 1, out var reason ) ); Assert.IsTrue( reason.Length > 0 ); Assert.AreSame( p, w.One.Visitors.Parameters ); Assert.AreEqual( capacity, w.One.Visitors.Capacity ); CollectionAssert.AreEqual( vars, w.One.Script.Variables );
	}
	[TestMethod]
	public void RemovedAmbiguousAndMismatchedMappingsCannotUpdateAnyOwner()
	{
		var w = World(); w.State.Level = 1; var before = w.One.Visitors.Parameters; w.One.Delete(); Call( w.Level, "ApplyCompletedCatalogueTier", w.State.Id ); Assert.AreSame( before, w.One.Visitors.Parameters );
		var another = World(); another.State.Level = 1; var map = (Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( another.Level )!; map[another.Two] = another.State.Id; Call( another.Level, "ApplyCompletedCatalogueTier", another.State.Id ); Assert.AreEqual( 0, another.One.Runtime.CatalogueTier ); Assert.AreEqual( 0, another.Two.Runtime.CatalogueTier );
		var mismatch = World(); var shop = mismatch.Economy.RegisterExisting( 1203 ); var error = Assert.ThrowsException<TargetInvocationException>( () => Call( mismatch.Level, "LinkEconomy", mismatch.One, shop.Id ) ); Assert.IsInstanceOfType( error.InnerException, typeof( InvalidDataException ) ); Assert.AreEqual( mismatch.State.Id, mismatch.Level.GetEconomyInstance( mismatch.One ) );
	}

	[TestMethod]
	public void CommonFactoryCompatibilityIsUnchangedAndNullScriptRefusesBinding()
	{
		var entry = Entry(); Assert.AreEqual( OriginalObjectRuntime.CreateQueueParameters( entry ), OriginalObjectRuntime.CreateQueueParameters( entry, 0 ) );
		var noScript = new ObjectCatalogEntry( entry.Theme, entry.Category, entry.ArchivePath, entry.Settings, entry.Shape, entry.ModelPath, null, null, entry.Scripts, entry.Animations, entry.AuxiliaryModels, entry.Models, entry.FileSystem, null ); var runtime = new OriginalObjectRuntime( noScript ); Assert.IsFalse( runtime.TryInitializeCatalogueTier( 1, out _ ) ); Assert.IsFalse( runtime.TryApplyCatalogueTierDefaults( 1, out _ ) ); Assert.IsNull( runtime.CatalogueTier );
	}
	/// <summary>A coaster binds its economy tier like any ride and its installed level reaches its queue defaults and words (tp.exe 0x4df8f0 has no track-type branch).</summary>
	[TestMethod]
	public void ACoasterBindsItsTierAndItsCompletedLevelReachesItsDefaults()
	{
		var w = World( "Bumper.WhichTrackType 3\n" ); Assert.IsNotNull( w.One.Runtime.Coaster ); Assert.AreEqual( 0, w.One.Runtime.CatalogueTier );
		EconomyTestData.HireBest( w.Economy, StaffType.Researcher ); w.Economy.SetResearchEffort( ResearchCategory.Ride, 0 );
		for ( var i = 0; i < 100 && !w.Economy.Research.IsAvailable( 1100, 1 ); i++ ) w.Economy.AdvanceDays( 1 );
		EconomyTestData.HireBest( w.Economy, StaffType.Mechanic ); Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, w.Economy.TryBuyUpgrade( w.State.Id ) );
		for ( var i = 0; i < 20 && w.State.Level < 1; i++ ) w.Economy.AdvanceDays( 1 );
		Assert.AreEqual( 1, w.State.Level ); Assert.AreEqual( 1, w.One.Runtime.CatalogueTier ); Assert.AreEqual( 200f, w.One.Visitors.Parameters.QueueWaitTimeConstant ); Assert.AreEqual( 3, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) );
		// the installation's reopen (0x4df8f0's tail) keeps a track ride (WhichTrackType 3) closed while its coaster takes no guests (0x441970): this one has no track
		Assert.IsFalse( w.One.IsOpen );
	}
	/// <summary>A coaster rebuilt from a save of its upgraded record takes the saved tier's defaults, as an ordinary ride does (<see cref="RebuiltBindingFromSavedCompletedTierMustDeriveTierDefaults"/>).</summary>
	[TestMethod]
	public void ARebuiltCoasterDerivesItsSavedTier()
	{
		var w = World( "Bumper.WhichTrackType 3\n" ); w.State.Level = 1;
		var saved = ParkSaveFile.Serialize( w.Economy ); var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( saved ) ), EconomyTestData.Settings(), EconomyTestData.Catalog() );
		Assert.IsTrue( restored.TryGetObject( w.State.Id, out var state ) ); Assert.AreEqual( 1, state.Level );
		var level = Empty<Level>(); var objects = new ParkObjects( null!, new SandboxParkGrid() ); Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( restored, ParkStartKind.FullSimulation ) ); Set( level, "<Objects>k__BackingField", objects ); Set( level, "economyInstances", new Dictionary<OriginalObject, int>() );
		var candidate = new OriginalObject( w.One.Entry, new ObjectPlacement( 1, 1, 0, System.Numerics.Vector2.Zero, 0 ), objects.ScriptWorld ); ((List<OriginalObject>)objects.Objects).Add( candidate ); Call( level, "LinkEconomy", candidate, state.Id );
		Assert.IsNotNull( candidate.Runtime.Coaster ); Assert.AreEqual( 1, candidate.Runtime.CatalogueTier ); Assert.AreEqual( 200f, candidate.Visitors.Parameters.QueueWaitTimeConstant );
	}
	[TestMethod]
	public void FirstInvalidBindingAbortsBeforeMappingButUnsupportedContextsSkipMetadata()
	{
		var w = World(); var map = (Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( w.Level )!; map.Remove( w.One ); var shop = w.Economy.RegisterExisting( 1203 ); Assert.ThrowsException<TargetInvocationException>( () => Call( w.Level, "LinkEconomy", w.One, shop.Id ) ); Assert.IsFalse( map.ContainsKey( w.One ) );
		Set( w.Level, "<Park>k__BackingField", null! ); Call( w.Level, "LinkEconomy", w.One, shop.Id ); Assert.AreEqual( shop.Id, map[w.One] ); Assert.AreEqual( 0, w.One.Runtime.CatalogueTier );
	}
	[TestMethod]
	public void PendingTierDoesNotSeedAndLateInitializationDoesNotOverwriteLiveVmState()
	{
		var w = World(); w.State.PendingLevel = 1; Call( w.Level, "ApplyCompletedCatalogueTier", w.State.Id ); Assert.AreEqual( 0, w.One.Runtime.CatalogueTier ); Assert.AreEqual( 1, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) );
		var independent = new OriginalObjectRuntime( Entry() ); independent.Simulate( .001 ); var vars = independent.Script!.Variables.ToArray(); Assert.IsFalse( independent.TryInitializeCatalogueTier( 1, out _ ) ); CollectionAssert.AreEqual( vars, independent.Script.Variables ); Assert.IsNull( independent.CatalogueTier );
	}
	[TestMethod]
	public void ReadOnlyAndPrototypeOwnersCannotApplyCompletedMetadata()
	{
		var w = World(); w.State.Level = 1; Set( w.Level, "<Visit>k__BackingField", Empty<ParkVisitInfo>() ); Call( w.Level, "ApplyCompletedCatalogueTier", w.State.Id ); Assert.AreEqual( 0, w.One.Runtime.CatalogueTier );
		var p = World(); p.State.Level = 1; var prototype = Empty<PrototypeRide>(); typeof( OriginalObject ).GetField( "<Runtime>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( prototype, p.One.Runtime ); var objects = (List<OriginalObject>)p.Level.Objects.Objects; objects.Remove( p.One ); objects.Add( prototype ); var map = (Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( p.Level )!; map.Remove( p.One ); map[prototype] = p.State.Id; Call( p.Level, "ApplyCompletedCatalogueTier", p.State.Id ); Assert.AreEqual( 0, p.One.Runtime.CatalogueTier );
	}

	[TestMethod]
	public void CompletedMetadataLeavesTheEntireActiveWaitVmSnapshotUnchanged()
	{
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.COPY, RideVMTests.V( (int)RideVariables.VAR_CAPACITY ), 1 ).I( Opcode.WAIT, 5000 ).I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		var w = World(); w.One.Runtime.Simulate( .001 ); var before = System.Text.Json.JsonSerializer.Serialize( w.One.Script!.CaptureSnapshot() ); Assert.IsTrue( w.One.Runtime.TryApplyCatalogueTierDefaults( 1, out _ ) ); var after = System.Text.Json.JsonSerializer.Serialize( w.One.Script.CaptureSnapshot() ); Assert.AreEqual( before, after, "Even an authored value equal to the prior seed and all wait/flag state must stay unchanged." ); Assert.AreEqual( 1, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 3, w.One.Visitors.Capacity );
	}

	[TestMethod]
	public void SnapshotEligibilityPreservesLiveOperatingValuesAndSupportsStoppedMatchingOwners()
	{
		var w = World(); Complete( w ); Assert.AreEqual( 3, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 3, w.One.Visitors.Capacity ); Assert.IsTrue( w.One.Runtime.CanCaptureCatalogueDefaults( 1, out _ ) ); w.One.Runtime.Stop(); Assert.IsTrue( w.One.Runtime.CanCaptureCatalogueDefaults( 1, out _ ) );
	}
	[TestMethod]
	public void SnapshotEligibilityRefusesUnrepresentableMetadataWithoutRepairingIt()
	{
		var w = World(); w.One.Visitors.Parameters = w.One.Visitors.Parameters with { }; var p = w.One.Visitors.Parameters; Assert.IsFalse( w.One.Runtime.CanCaptureCatalogueDefaults( 0, out _ ) ); Assert.AreSame( p, w.One.Visitors.Parameters );
		var mismatch = World(); mismatch.State.Level = 1; Assert.IsFalse( mismatch.One.Runtime.CanCaptureCatalogueDefaults( 1, out _ ) ); Assert.AreEqual( 0, mismatch.One.Runtime.CatalogueTier ); Assert.AreEqual( 1, mismatch.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) );
	}
	[TestMethod]
	public void LegacyUnboundTierZeroDefaultsRemainSnapshotCompatible()
	{
		var original = Entry(); var noRows = new ObjectCatalogEntry( original.Theme, original.Category, original.ArchivePath, new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\nInfo.IsChoosable 1\nInfo.WhichUIType 0\n", "legacy.sam" ) } ), original.Shape, original.ModelPath, null, original.ScriptPath, original.Scripts, original.Animations, original.AuxiliaryModels, original.Models, original.FileSystem, null ); var runtime = new OriginalObjectRuntime( noRows ); Assert.IsFalse( runtime.TryInitializeCatalogueTier( 0, out _ ) ); Assert.IsTrue( runtime.CanCaptureCatalogueDefaults( 0, out _ ) ); Assert.IsFalse( runtime.CanCaptureCatalogueDefaults( 1, out _ ) );
	}

	private static Dictionary<string, string> PrepareCapture( (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) w )
	{
		var catalog = (ObjectCatalog)typeof( ObjectCatalog ).GetConstructor( BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof( string ), typeof( IReadOnlyList<ObjectCatalogEntry> ) }, null )!.Invoke( new object[] { "jungle", new[] { w.One.Entry } } ); Set( w.Level.Objects, "<Catalog>k__BackingField", catalog );
		Set( w.Level, "<OriginalPark>k__BackingField", Empty<OriginalPark>() ); Set( w.Level, "<LevelName>k__BackingField", "jungle" ); Set( w.Level, "<Seed>k__BackingField", WorldSeed.Default ); Set( w.Level, "simulationClock", new FixedStepClock() );
		var grid = new GuestPathGrid( 32, 32 ); var guests = new GuestSimulation( grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false }; Set( w.Level, "<Guests>k__BackingField", guests ); Call( w.Level, "LinkWorldGenerator" );
		foreach ( var item in new[] { w.One, w.Two } ) { item.Visitors.EntranceCell = (item.Placement.X, item.Placement.Y); item.Visitors.ExitCell = item.Visitors.EntranceCell; item.Visitors.HasCells = true; grid.SetPath( item.Placement.X, item.Placement.Y, true ); item.Visitors.RecomputeQueue( grid ); guests.Register( item.Visitors ); }
		return new Dictionary<string, string> { { "/model.MD2", Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( w.One.Entry.FileSystem.GetAbsolutePath( "/model.MD2" ) ) ) ).ToLowerInvariant() }, { "/script.RSE", Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( w.One.Entry.FileSystem.GetAbsolutePath( "/script.RSE" ) ) ) ).ToLowerInvariant() } };
	}
	[TestMethod]
	public void WholeParkCaptureAcceptsRepresentableCompletedAndStoppedMetadata()
	{
		var w = World(); Complete( w ); var assets = PrepareCapture( w ); var captured = w.Level.CaptureResumeSnapshot( null, "tier1", assets ); Assert.AreEqual( 1, captured.Economy.Objects.Single( o => o.Id == w.State.Id ).Level ); Assert.AreEqual( 3, w.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ) ); Assert.AreEqual( 3, w.One.Visitors.Capacity ); ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( captured ) ); w.One.Runtime.Stop(); w.Level.CaptureResumeSnapshot( null, "stopped", assets );
	}
	[DataTestMethod, DataRow( "custom" ), DataRow( "refused" ), DataRow( "sparse" )]
	public void WholeParkCaptureRefusesUnrepresentableDefaultsBeforeReplacingPriorFile( string change )
	{
		var w = World( Tier2, sparse: change == "sparse" ); var assets = PrepareCapture( w ); var baseline = w.Level.CaptureResumeSnapshot( null, "baseline", assets ); var path = Path.Combine( root, "prior-world.json" ); ParkResumeSaveFile.Save( path, baseline ); var prior = File.ReadAllBytes( path );
		if ( change == "custom" ) w.One.Visitors.Parameters = w.One.Visitors.Parameters with { }; else { if ( change == "refused" ) w.One.Visitors.Parameters = w.One.Visitors.Parameters with { Speed = 123 }; Complete( w ); }
		Assert.ThrowsException<NotSupportedException>( () => ParkResumeSaveFile.Save( path, w.Level.CaptureResumeSnapshot( null, "unrepresentable", assets ) ) ); CollectionAssert.AreEqual( prior, File.ReadAllBytes( path ) ); Assert.AreEqual( 1, w.One.Visitors.Capacity );
	}
	/// <summary>Ride init writes the clamped low byte of InitDuration and the ride's InitSpeed as the script speed bias (RIDES-016, RIDES-033; PC 0x4db51c..0x4db64f).</summary>
	[TestMethod]
	public void RideInitClampsDurationAndSetsTheScriptSpeedBias()
	{
		var plain = World();
		Assert.AreEqual( 10, plain.One.Runtime.GetVariable( RideVariables.VAR_DURATION ) );
		Assert.AreEqual( 50, plain.One.Script!.SpeedBias ); Assert.AreEqual( 1f, plain.One.Script.ScriptSpeed );
		var clamped = World( "UsageInfo.MinDuration 12\nUsageInfo.MaxDuration 15\nUpgrades[0].InitSpeed 60\n" );
		Assert.AreEqual( 12, clamped.One.Runtime.GetVariable( RideVariables.VAR_DURATION ), "below MinDuration" );
		Assert.AreEqual( 60, clamped.One.Script!.SpeedBias ); Assert.AreEqual( 1.1f, clamped.One.Script.ScriptSpeed, 1e-6f );
		var high = World( "UsageInfo.MaxDuration 8\n" );
		Assert.AreEqual( 8, high.One.Runtime.GetVariable( RideVariables.VAR_DURATION ), "above MaxDuration" );
		var wrapped = World( "Upgrades[0].InitDuration 266\nUsageInfo.MaxDuration 255\n" );
		Assert.AreEqual( 10, wrapped.One.Runtime.GetVariable( RideVariables.VAR_DURATION ), "266 keeps its low byte 10" );
		Assert.IsNull( OriginalObjectRuntime.ClampInitDuration( plain.One.Entry.Settings, 0 ), "nothing is written for 0" );
		Assert.IsNull( OriginalObjectRuntime.ClampInitDuration( plain.One.Entry.Settings, -5 ) );
	}

}
