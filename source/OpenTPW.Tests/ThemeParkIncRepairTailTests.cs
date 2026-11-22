using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The Theme Park Inc level's side of the mechanics (docs/reverse/TPI-STAFF.md §11): a maintenance request closes the ride (0x5E3400), a finished repair runs the closing tail 0x5E2C2E
/// (Level.OpenAfterThemeParkIncRepair) and a worn relief object's VAR_WORN reaches its script (0x5E3990). Each test fails when its branch is dropped or swapped with another. The level's
/// object holds a synthetic ride; the economy is the Science economy with three synthetic types beside the catalogue's, so it needs OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncRepairTailTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	/// <summary>The Giant Robot Swing: a ride (Info.WhichUIType 0) without UsageInfo.SpecialIngredient.</summary>
	private const int RobotSwing = 1101;
	/// <summary>The Giant Hot dog Kiosk: UsageInfo.SpecialIngredient 1, a shop (Info.WhichUIType 1).</summary>
	private const int HotDogKiosk = 1207;
	private const int Relief = 9901, Bin = 9902;

	private string root = null!;
	private List<Entity> entities = null!;
	private BaseFileSystem previous = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Setup()
	{
		entities = Entity.All;
		Entity.All = new();
		previous = FileSystem;
		previousLog = Log;
		root = Directory.CreateTempSubdirectory( "opentpw-tpi-repair-tail-" ).FullName;
		var bytes = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
		foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), bytes );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var item in Entity.All.ToArray() ) item.Delete();
		Entity.All = entities;
		FileSystem = previous;
		Log = previousLog;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Directory.Delete( root, true );
	}

	/// <summary>The Science catalogue with a relief object and a bin beside it (the three worlds' catalogues declare neither).</summary>
	private sealed class WithSynthetic( IEconomyObjectCatalog inner, params EconomyObjectInfo[] extra ) : IEconomyObjectCatalog
	{
		public IReadOnlyCollection<EconomyObjectInfo> Objects { get; } = inner.Objects.Concat( extra ).ToList();
		public bool TryGet( int infoId, out EconomyObjectInfo info )
		{
			info = extra.FirstOrDefault( item => item.InfoId == infoId )!;
			return info != null || inner.TryGet( infoId, out info );
		}
	}

	private static EconomyObjectInfo Synthetic( int id, bool relief = false, bool litter = false ) =>
		new( id, "synthetic", ParkObjectKind.Shop, ResearchCategory.Shop, 0, new[] { new UpgradeLevelInfo( 0, 0, 0, 0, 0, Array.Empty<int>() ) }, null, null, null, 0, 0, 0, 0, 0, 0, "",
			ProvidesRelief: relief, HoldsLitter: litter );

	private sealed record Fixture( Level Level, ParkEconomy Economy );

	/// <summary>A level with only what the ride events touch: the park, its economy links and the Theme Park Inc wiring.</summary>
	private Fixture World()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		Log = new();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		var catalog = new WithSynthetic( EconomyObjectCatalog.Load( "science", easy: false ), Synthetic( Relief, relief: true ), Synthetic( Bin, litter: true ) );
		var economy = new ParkEconomy( BalanceSettings.LoadThemeParkInc( "science" ), catalog, ParkGameMode.FullSimulation, 1, EconomyRules.ThemeParkIncMoney );
		Assert.IsNotNull( economy.TpiStaff );
		// the Theme Park Inc park opens at its start (ParkEconomyRuntime.ForThemeParkIncLevel: StartsOpen)
		economy.OpenPark();
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		Set( level, "<Objects>k__BackingField", new ParkObjects( null!, new SandboxParkGrid() ) );
		Set( level, "economyInstances", new Dictionary<OriginalObject, int>() );
		typeof( Level ).GetMethod( "SetupThemeParkIncRideEvents", Private )!.Invoke( level, null );
		return new Fixture( level, economy );
	}

	private static void Set( object target, string field, object? value ) => target.GetType().GetField( field, Private )!.SetValue( target, value );

	/// <summary>A synthetic ride placed in the level and linked to a new economy record of <paramref name="infoId"/>.</summary>
	private (OriginalObject Ride, ParkObjectState State) Place( Fixture world, int infoId, string extra = "" )
	{
		var entry = CheatsRideVehicleTests.SyntheticRide( root, infoId, "ride" + infoId, extra );
		var ride = new OriginalObject( entry, new ObjectPlacement( 1, 1, 0, System.Numerics.Vector2.Zero, 0 ), world.Level.Objects.ScriptWorld );
		var state = world.Economy.RegisterExisting( infoId );
		((Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", Private )!.GetValue( world.Level )!)[ride] = state.Id;
		state.ScriptHosted = true;
		state.IsOpen = ride.Runtime.IsOpen;
		return (ride, state);
	}

	/// <summary>A ride closed for its repair, which the mechanic then finishes (0x5E2910 raises the repair's open and tail).</summary>
	private static void Repair( Fixture world, OriginalObject ride, ParkObjectState state )
	{
		ride.Runtime.Close();
		state.IsOpen = false;
		state.Repair = 3.0;
		world.Economy.FinishThemeParkIncRepair( state );
		Assert.AreEqual( 100.0, state.Repair );
	}

	private static void Run( Fixture world, int type )
	{
		var slot = world.Economy.TpiChallenges!.Slots[0];
		slot.Type = type;
		slot.Status = ThemeParkIncChallengeStatus.Running;
	}

	private static void Weather( ParkEconomy economy, int state ) =>
		typeof( ThemeParkIncWeatherSpells ).GetField( "active", Private )!.SetValue( economy.TpiWeather, new WeatherSpell( 0, state, 0, 1 ) );

	// ---- The maintenance request -------------------------------------------------------------------

	[TestMethod]
	public void AMaintenanceRequestClosesTheRideButNotABin()
	{
		// [BIN:TPI-EXE:0x5E3400 maintenance request] +0x78 = 1; a bin or a plant returns, any other object closes (the open word, VAR_RIDECLOSED and the close model call)
		var world = World();
		var (ride, state) = Place( world, RobotSwing );
		Assert.IsTrue( ride.Runtime.IsOpen );
		world.Economy.RequestThemeParkIncMaintenance( state );
		Assert.AreEqual( (false, false, true), (ride.Runtime.IsOpen, state.IsOpen, state.MaintenanceRequested), "the request closes the ride" );
		var (bin, binState) = Place( world, Bin );
		world.Economy.RequestThemeParkIncMaintenance( binState );
		Assert.AreEqual( (true, true), (bin.Runtime.IsOpen, binState.MaintenanceRequested), "a bin is called for and stays open" );
	}

	// ---- The repair tail ---------------------------------------------------------------------------

	[TestMethod]
	public void ARepairOpensTheRide()
	{
		var world = World();
		var (ride, state) = Place( world, RobotSwing );
		Repair( world, ride, state );
		Assert.AreEqual( (true, true, false), (ride.Runtime.IsOpen, state.IsOpen, ride.Runtime.ClosedDueToWeather), "0x5E1DC0 opens it and no guard closes it again" );
	}

	[TestMethod]
	public void ARepairInAClosedParkClosesTheRideAgain()
	{
		// [BIN:TPI-EXE:0x5E2C35 repair guard] the park closed (world +0x1EAB44) reaches the close at 0x5E2D34
		var world = World();
		var (ride, state) = Place( world, RobotSwing );
		world.Economy.ClosePark();
		Repair( world, ride, state );
		Assert.AreEqual( (false, false), (ride.Runtime.IsOpen, state.IsOpen) );
	}

	[TestMethod]
	public void ARunningChallenge42ClosesAnObjectOfSpecialIngredient1Again()
	{
		// [BIN:TPI-EXE:0x5C8B70 challenge 42] a running challenge of type 42 while the type's UsageInfo.SpecialIngredient (+0x218) is 1 fails the guard; challenge 46 does not hold a shop
		var world = World();
		var (kiosk, state) = Place( world, HotDogKiosk );
		Run( world, 42 );
		Repair( world, kiosk, state );
		Assert.IsFalse( kiosk.Runtime.IsOpen, "challenge 42 closes the kiosk" );
		var other = World();
		var (open, openState) = Place( other, HotDogKiosk );
		Run( other, 46 );
		Repair( other, open, openState );
		Assert.IsTrue( open.Runtime.IsOpen, "challenge 46 neither closes the kiosk nor is it 42" );
		var swing = World();
		var (ride, rideState) = Place( swing, RobotSwing );
		Run( swing, 42 );
		Repair( swing, ride, rideState );
		Assert.IsTrue( ride.Runtime.IsOpen, "challenge 42 leaves a type whose SpecialIngredient is not 1 open" );
	}

	[TestMethod]
	public void ARunningChallenge46HoldsARideOpenInAClosedPark()
	{
		// [BIN:TPI-EXE:0x5C8BE0 challenge 46] a running challenge of type 46 while the type's Info.WhichUIType (+0x70) is 0 skips the close at 0x5E2D34
		var world = World();
		var (ride, state) = Place( world, RobotSwing );
		world.Economy.ClosePark();
		Run( world, 46 );
		Repair( world, ride, state );
		Assert.AreEqual( (true, true), (ride.Runtime.IsOpen, state.IsOpen), "challenge 46 holds the ride open" );
		var other = World();
		var (closed, closedState) = Place( other, RobotSwing );
		other.Economy.ClosePark();
		Run( other, 42 );
		Repair( other, closed, closedState );
		Assert.IsFalse( closed.Runtime.IsOpen, "challenge 42 holds nothing open" );
		var shop = World();
		var (kiosk, kioskState) = Place( shop, HotDogKiosk );
		shop.Economy.ClosePark();
		Run( shop, 46 );
		Repair( shop, kiosk, kioskState );
		Assert.IsFalse( kiosk.Runtime.IsOpen, "challenge 46 holds only a type whose WhichUIType is 0" );
	}

	[TestMethod]
	public void ARepairInASpellThatClosesTheRideClosesItForTheWeather()
	{
		// [BIN:TPI-EXE:0x5E2DE8 repair weather close] the spell's ClosedDuring word 1 closes the ride again and sets mClosedDueToWeather (+0x41), even where challenge 46 held it through the guard
		var world = World();
		var (ride, state) = Place( world, RobotSwing, "WeatherModifier.ClosedDuringRain 1\n" );
		Weather( world.Economy, 2 );
		Repair( world, ride, state );
		Assert.AreEqual( (false, false, true), (ride.Runtime.IsOpen, state.IsOpen, ride.Runtime.ClosedDueToWeather), "rain closes the ride and marks it closed for the weather" );
		var held = World();
		var (heldRide, heldState) = Place( held, RobotSwing, "WeatherModifier.ClosedDuringRain 1\n" );
		Weather( held.Economy, 2 );
		Run( held, 46 );
		Repair( held, heldRide, heldState );
		Assert.AreEqual( (false, true), (heldRide.Runtime.IsOpen, heldRide.Runtime.ClosedDueToWeather), "the weather close follows the guard's hold" );
		var dry = World();
		var (dryRide, dryState) = Place( dry, RobotSwing, "WeatherModifier.ClosedDuringRain 1\n" );
		Weather( dry.Economy, 3 );
		Repair( dry, dryRide, dryState );
		Assert.AreEqual( (true, false), (dryRide.Runtime.IsOpen, dryRide.Runtime.ClosedDueToWeather), "a storm the type does not name leaves it open" );
	}

	[TestMethod]
	public void ARepairedCoasterThatRefusesGuestsClosesAgainAfterTheHold()
	{
		// [BIN:TPI-EXE:0x5E2EEB repair coaster close] track type 3 with the coaster test 0x485BB0 false closes the ride once more after the weather close, whatever the guard decided
		var world = World();
		var (coaster, state) = Place( world, RobotSwing, "Bumper.WhichTrackType 3\n" );
		Assert.IsTrue( coaster.Runtime.Coaster is { AcceptsGuests: false }, "a coaster without a track refuses guests" );
		Run( world, 46 );
		Repair( world, coaster, state );
		Assert.AreEqual( (false, false, false), (coaster.Runtime.IsOpen, state.IsOpen, coaster.Runtime.ClosedDueToWeather), "the coaster test closes it again" );
	}

	[TestMethod]
	public void ARepairOfARideWhoseQueueIsCutOffClosesItAgain()
	{
		// [BIN:TPI-EXE:0x5E07B0 queue link] the queue branch (tp.exe's 0x004DE4A0): the back queue cell's link byte equal to its queue link fails the guard, which the repair tail closes at 0x5E2D34
		foreach ( var connected in new[] { false, true } )
		{
			var world = World();
			var (ride, state) = Place( world, RobotSwing );
			var grid = new GuestPathGrid( 12, 12 );
			for ( var x = 0; x < 10; x++ )
				grid.SetPath( x, 0, true );
			var visitors = ride.Visitors;
			visitors.EntranceCell = (2, 0);
			visitors.ExitCell = (2, 0);
			visitors.QueueFrontCell = (2, 5);
			visitors.QueueEntranceDirection = 2;
			var writer = new QueueLineWriter( grid, visitors, ( x, y, pending ) => QueuePaths.CheckExtend( grid, visitors, x, y, pending ), ( x, y ) => QueuePaths.TryExtend( grid, visitors, x, y ),
				( x, y ) => QueuePaths.RemoveFrom( grid, visitors, x, y ), () => 0 );
			var tool = new CellBuildTool();
			tool.Enter( writer, (2, 5) );
			// a line ending on the path connects the queue; one stopping a cell short leaves its back unlinked
			tool.Click( connected ? (2, 0) : (2, 1) );
			Assert.AreEqual( connected, ParkGate.QueueConnected( grid, visitors ) );
			Set( world.Level, "<Guests>k__BackingField", new GuestSimulation( grid, GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false } );
			Repair( world, ride, state );
			Assert.AreEqual( (connected, connected), (ride.Runtime.IsOpen, state.IsOpen), connected ? "a connected queue lets the ride open" : "a cut-off queue closes it again" );
		}
	}

	// ---- The object list's things without an economy record ------------------------------------

	[TestMethod]
	public void AnObstacleReplacedOnItsOpeningLeavesTheListWithTheReplacementAtTheHead()
	{
		// [BIN:TPI-EXE:0x5E8D80 obstacle replacement] ReplaceWithID constructs the replacement (0x5DB1B0, which puts it at the list's head through 0x64F3E0 at 0x5DB28C), then destroys the obstacle
		// (0x637C40 -> 0x5DE780 -> 0x64F430 unlinks it); neither has an economy record, so both are their thing ids in ParkEconomy.ThemeParkIncObjectList
		var world = World();
		var lake = CheatsRideVehicleTests.SyntheticRide( root, 1703, "lake", "" );
		var drained = CheatsRideVehicleTests.SyntheticRide( root, 1704, "dlake", "" );
		var catalog = (ObjectCatalog)typeof( ObjectCatalog ).GetConstructor( Private, null, new[] { typeof( string ), typeof( IReadOnlyList<ObjectCatalogEntry> ) }, null )!
			.Invoke( new object[] { "science", new[] { lake, drained } } );
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		var objects = new ParkObjects( catalog, new SandboxParkGrid() ) { Things = pool };
		Set( world.Level, "<Objects>k__BackingField", objects );
		typeof( Level ).GetMethod( "ConnectObjectsToEconomy", Private )!.Invoke( world.Level, null );
		var obstacle = objects.Add( lake, 3, 3, 0 );
		world.Economy.AddThemeParkIncThing( obstacle.ThingId );
		var (_, ride) = Place( world, RobotSwing );
		CollectionAssert.AreEqual( new[] { ride.Id, -obstacle.ThingId }, world.Economy.ThemeParkIncObjectList.ToArray() );
		var things = (IThemeParkIncObstacleThings)Activator.CreateInstance( typeof( Level ).GetNestedType( "ThemeParkIncObstacleThings", BindingFlags.NonPublic )!, world.Level )!;
		var type = new ThemeParkIncObstacleType( 1703, "lake", null, ThemeParkIncMapGrid.Parse( new[] { "." }, "after" ), 0, 0, true, 0, false, 0, false, false, 5, 5, 0, 0, false, 1704, false );
		Assert.AreEqual( ThemeParkIncObstacleFate.Replaced,
			ThemeParkIncObstacles.OpenThing( new ParkCellMap( 128, 128 ), new Dictionary<int, ThemeParkIncObstacleType> { [1703] = type }, things, (ushort)obstacle.ThingId ) );
		var replacement = objects.Objects.Single( item => item.Entry.InfoId == 1704 );
		Assert.IsTrue( obstacle.IsDeleted );
		CollectionAssert.AreEqual( new[] { -replacement.ThingId, ride.Id }, world.Economy.ThemeParkIncObjectList.ToArray(), "the replacement at the head, the obstacle gone" );
		// the continuation save takes the list as the game left it
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( world.Economy ) ) ), world.Economy.Settings, world.Economy.Catalog );
		CollectionAssert.AreEqual( world.Economy.ThemeParkIncObjectList.ToArray(), restored.ThemeParkIncObjectList.ToArray() );
		// a thing id taken again enters once: the earlier entry goes first
		world.Economy.AddThemeParkIncThing( replacement.ThingId );
		CollectionAssert.AreEqual( new[] { -replacement.ThingId, ride.Id }, world.Economy.ThemeParkIncObjectList.ToArray() );
	}

	// ---- The relief object's worn report --------------------------------------------------------

	[TestMethod]
	public void AReliefObjectBelowTwentyFiveWritesVarWornToItsScriptOnce()
	{
		// [BIN:TPI-EXE:0x5E3990 relief worn] a relief object (+0x42 bit 1) whose repair byte is below 25.0 writes VAR_WORN = 1 (0x5E49C0) on every update; the write is skipped while the script holds 1
		var world = World();
		var (toilet, state) = Place( world, Relief );
		var writes = 0;
		world.Economy.EventRaised += item => writes += item.Kind == ParkEventKind.RideScriptVariable && item.InstanceId == state.Id && item.InfoId == (int)RideVariables.VAR_WORN ? 1 : 0;
		state.Repair = 25.0;
		world.Economy.UpdateRide( state, 1 );
		Assert.AreEqual( (0, 0), (toilet.Runtime.GetVariable( RideVariables.VAR_WORN ), writes), "a byte of 25 is not worn" );
		state.Repair = 24.9;
		world.Economy.UpdateRide( state, 2 );
		Assert.AreEqual( (1, 1, 1), (toilet.Runtime.GetVariable( RideVariables.VAR_WORN ), state.ScriptWorn, writes), "a byte of 24 writes VAR_WORN to the script" );
		world.Economy.UpdateRide( state, 3 );
		Assert.AreEqual( 1, writes, "the script holds 1 already" );
		var (ride, rideState) = Place( world, RobotSwing );
		rideState.Repair = 24.9;
		rideState.IsOpen = false;
		world.Economy.UpdateRide( rideState, 4 );
		Assert.AreEqual( 0, ride.Runtime.GetVariable( RideVariables.VAR_WORN ), "a ride is no relief object" );
	}
}
