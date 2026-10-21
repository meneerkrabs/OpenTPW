using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-lockstep] The park after a fixed tick depends on the ticks and the commands only, never on how the ticks fall into frames (docs/DETERMINISM.md, "Frame batching";
/// docs/COOP.md, "Known limits"). A scripted run of the asset-free level (the command layer's level: objects, paths, guests, staff, the speed setting) is driven one tick a frame,
/// sixteen ticks a frame and in seeded random frames through the level's whole frame step (<see cref="Level.AdvanceFrame"/>: the path layer, the pools, the evening, the snow, the scene
/// clock, the simulation and every entity's update), and through the simulation alone; its edits are issued live between frames or replayed from the recorded log inside the frames.
/// Every run gives the same world hash after every tick and the same park save at the end. The with-data twin is the park-resume smoke's restore phase, which continues a Jungle park
/// in seeded random frames and compares every tick with the capture phase's single-tick frames (docs/PARK-SAVES.md).
/// </summary>
[TestClass]
[DoNotParallelize]
public class FrameBatchingTests
{
	/// <summary>The scripted run's length in ticks (about 13 simulated seconds: a few park turns at every speed it sets).</summary>
	private const int RunTicks = 780;

	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private List<Entity>? entities;

	private sealed record Park( Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, Dictionary<string, string> Assets );

	[TestInitialize]
	public void Initialize()
	{
		// the entities of other tests stay in the global list; this run updates only its own
		entities = Entity.All;
		Entity.All = new List<Entity>();
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture?.Cleanup();
		renderer?.Dispose();
		if ( entities != null )
			Entity.All = entities;
	}

	private Park NewPark()
	{
		var type = typeof( UpgradeCatalogueDefaultsTests );
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
		var assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
		var level = world.Level;
		typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null );
		typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null );
		typeof( Level ).GetMethod( "SetupPaths", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null );
		level.ConfigureObjectHooks();
		// an uninitialised level has no field initialisers run: the line tool, the scene clock and the HUD's time scale the park view holds
		typeof( Level ).GetField( "<CellTool>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, new CellBuildTool() );
		typeof( Level ).GetField( "<PathVariants>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, new PathTileVariants() );
		typeof( Level ).GetField( "<SceneClock>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, new SceneClock() );
		level.SimulationTimeScale = 1f;
		world.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1_000_000 );
		// a path the guests and the staff walk, the two objects at its ends
		var grid = level.Guests!.Grid;
		for ( var x = 1; x <= 12; x++ )
			grid.SetPath( x, 1, true );
		for ( var y = 1; y <= 3; y++ )
			grid.SetPath( 3, y, true );
		foreach ( var item in new[] { world.One, world.Two } )
			item.Visitors.RecomputeQueue( grid );
		foreach ( var (x, y) in new[] { (1, 1), (4, 1), (6, 1), (8, 1), (10, 1), (3, 2) } )
			level.Guests.SpawnInPark( x, y );
		// a mechanic and a handyman of the hiring pool, not yet on the map: the first tick puts them on the path
		EconomyTestData.HireBest( world.Economy, StaffType.Mechanic );
		EconomyTestData.HireBest( world.Economy, StaffType.Handyman );
		return new Park( level, world.Economy, world.One, world.Two, assets );
	}

	/// <summary>The scripted edits: the tick before which each is issued, and the command.</summary>
	private static IReadOnlyList<(long Tick, ParkCommand Command)> Script( Park park ) => new (long, ParkCommand)[]
	{
		(7, new SetResearchEffortCommand( ResearchCategory.Ride, 60 )),
		(23, park.Level.BuildCommand( park.One.Entry, 20, 12, 90 )),
		(23, new LayLineCommand( CellToolMode.Path, -1, 5, 20, 12, 20 )),
		(61, new SetGameSpeedCommand( GameSpeed.Fastest )),
		(97, new SetAttractionOpenCommand( park.Two.Runtime.AttractionId, false )),
		(150, new RemovePathCellCommand( 6, 20 )),
		(211, new SetGameSpeedCommand( GameSpeed.Paused )),
		(260, new SetAttractionOpenCommand( park.Two.Runtime.AttractionId, true )),
		(261, new SetGameSpeedCommand( GameSpeed.Fast )),
		(333, new SetEntranceFeeCommand( 5 )),
		(420, new RemoveAtCommand( 20, 12 )),
		(500, new SetGameSpeedCommand( GameSpeed.Normal )),
		(640, new SetTrainingBudgetCommand( StaffType.Handyman, 200 )),
	};

	/// <summary>What happens to the park between two ticks without a command: a member is hired (the HUD's direct hire) and a member is left picked up with no hand holding him (a loaded save).</summary>
	private static void BetweenTicks( Park park, long ticks )
	{
		if ( ticks == 120 )
			EconomyTestData.HireBest( park.Economy, StaffType.Guard );
		if ( ticks == 300 && park.Economy.Staff.Members.FirstOrDefault( member => member.Type == StaffType.Handyman ) is { } handyman )
			handyman.State = StaffState.PickedUp;
	}

	private enum Driver { SimulationOnly, Frames }

	/// <summary>One run of the script: the hash after every tick, the command log and the park save at the end.</summary>
	private sealed record Run( List<ulong> Hashes, IReadOnlyList<ParkCommandLogEntry> Log, byte[] Save );

	/// <summary>
	/// Runs the script on a fresh park. <paramref name="frame"/> gives each frame's tick count; live, a frame also ends before a scripted edit, which is issued between the two frames as the
	/// HUD does; replayed (<paramref name="replay"/>), the recorded log is scheduled up front and each command applies inside its frame, before its tick.
	/// </summary>
	private Run Drive( Func<int> frame, Driver driver, IReadOnlyList<ParkCommandLogEntry>? replay = null )
	{
		var park = NewPark();
		var level = park.Level;
		var hashes = new List<ulong>();
		var ticksInFrame = 0;
		level.TickCompleted = ticks =>
		{
			ticksInFrame++;
			hashes.Add( level.ComputeStateHash() );
			BetweenTicks( park, ticks );
		};
		var script = Script( park );
		if ( replay != null )
			foreach ( var entry in replay )
				level.Commands.Schedule( entry.Command, entry.Tick );
		var next = 0;
		while ( level.Commands.NextTick < RunTicks )
		{
			var now = level.Commands.NextTick;
			var ticks = Math.Min( frame(), RunTicks - (int)now );
			if ( replay == null )
			{
				while ( next < script.Count && script[next].Tick == now )
				{
					var result = level.Commands.Issue( script[next++].Command );
					Assert.IsTrue( result.Accepted, $"the scripted {script[next - 1].Command.Kind} before tick {now}: {result.Reason}" );
				}
				if ( next < script.Count )
					ticks = Math.Min( ticks, (int)(script[next].Tick - now) );
			}
			ticksInFrame = 0;
			if ( driver == Driver.Frames )
			{
				// the entities' updates after the simulation read the frame's delta, as the renderer feeds it
				Time.Update( (float)(ticks * FixedStepClock.TickDuration) );
				level.AdvanceFrame( ticks * FixedStepClock.TickDuration );
			}
			else
				for ( var tick = 0; tick < ticks; tick++ )
					level.AdvanceSimulation( FixedStepClock.TickDuration );
			Assert.AreEqual( ticks, ticksInFrame, $"the frame from tick {now} ran its ticks" );
		}
		level.TickCompleted = null;
		// the clock's banked fraction of a tick is the frame time's rounding (n ticks' time less n ticks leaves up to 5e-17 s in double), not the batching: it and the dropped-time
		// total, which a real frame's time feeds the same way, are left out
		var snapshot = level.CaptureResumeSnapshot( null, "frame batching", park.Assets );
		var save = ParkResumeSaveFile.Serialize( snapshot with { Clock = snapshot.Clock with { PendingSeconds = 0, DroppedSeconds = 0 } } );
		return new Run( hashes, level.Commands.Log.ToArray(), save );
	}

	private static Func<int> Fixed( int ticks ) => () => ticks;

	private static Func<int> Seeded( ulong seed )
	{
		// SplitMix64: a seeded stream of frame sizes from 1 to the clock's catch-up limit
		var state = seed;
		return () =>
		{
			var z = state += 0x9E3779B97F4A7C15UL;
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			return 1 + (int)((z ^ (z >> 31)) % FixedStepClock.MaximumCatchUpTicks);
		};
	}

	private static void AssertSame( Run expected, Run actual, string name )
	{
		Assert.AreEqual( expected.Hashes.Count, actual.Hashes.Count, $"{name}: ticks" );
		for ( var tick = 0; tick < expected.Hashes.Count; tick++ )
			if ( expected.Hashes[tick] != actual.Hashes[tick] )
				Assert.Fail( $"{name}: the world hash differs after tick {tick + 1} ({expected.Hashes[tick]:X16} against {actual.Hashes[tick]:X16})" );
		CollectionAssert.AreEqual( expected.Log.ToArray(), actual.Log.ToArray(), $"{name}: the same commands before the same ticks" );
		Assert.IsTrue( expected.Save.AsSpan().SequenceEqual( actual.Save ), $"{name}: the park save at the end differs" );
	}

	[TestMethod]
	public void TheParkAfterEveryTickIsTheSameHoweverTheTicksFallIntoFramesLiveOrReplayed()
	{
		// the reference: one tick a frame through the simulation alone, as the command replay and the smoke harnesses drive it
		var reference = Drive( Fixed( 1 ), Driver.SimulationOnly );
		Assert.AreEqual( RunTicks, reference.Hashes.Count );
		Assert.AreEqual( 13, reference.Log.Count, "every scripted edit was accepted and logged" );
		Assert.IsTrue( reference.Hashes.Distinct().Count() > RunTicks / 2, "the park changes from tick to tick" );
		var runs = new (string Name, Func<int> Frame)[]
		{
			("one tick a frame", Fixed( 1 )),
			("sixteen ticks a frame", Fixed( FixedStepClock.MaximumCatchUpTicks )),
			("seeded random frames", Seeded( 0xF4A3_E5BA_7C11_0001UL )),
			("other seeded random frames", Seeded( 0xF4A3_E5BA_7C11_0002UL )),
		};
		foreach ( var (name, frame) in runs )
			AssertSame( reference, Drive( frame, Driver.Frames ), $"live, {name}" );
		foreach ( var (name, frame) in runs )
			AssertSame( reference, Drive( frame, Driver.Frames, reference.Log ), $"replayed, {name}" );
		AssertSame( reference, Drive( Fixed( FixedStepClock.MaximumCatchUpTicks ), Driver.SimulationOnly, reference.Log ), "replayed through the simulation alone, sixteen ticks a call" );
	}

	[TestMethod]
	public void TheStaffHostsBookkeepingRunsInsideTheTick()
	{
		var park = NewPark();
		var level = park.Level;
		var members = park.Economy.Staff.Members;
		Assert.IsTrue( members.All( member => member.CellX == null ), "hired from the pool, not yet on the map" );
		// a frame without a tick (a scene pause) moves nothing of the park
		level.SimulationTimeScale = 0f;
		var before = level.ComputeStateHash();
		level.AdvanceFrame( FixedStepClock.TickDuration );
		Assert.AreEqual( before, level.ComputeStateHash() );
		Assert.IsTrue( members.All( member => member.CellX == null ), "a frame's start no longer places anyone" );
		Assert.IsFalse( level.TryGetStaffWorldPosition( members[0].Id, out _ ), "the HUD's locator reads; it does not place" );
		Assert.IsTrue( members.All( member => member.CellX == null ) );
		// the tick does, after its commands
		level.SimulationTimeScale = 1f;
		long? placedAt = null;
		level.TickCompleted = ticks => placedAt ??= members.All( member => member.CellX != null ) ? ticks : null;
		level.AdvanceFrame( FixedStepClock.TickDuration );
		Assert.AreEqual( 1L, placedAt, "the first tick puts the members on the path" );
		// a member left picked up with no hand holding him goes back to idle at the next tick, not at the next frame
		var handyman = members.First( member => member.Type == StaffType.Handyman );
		StaffState? seen = null;
		level.TickCompleted = ticks =>
		{
			if ( ticks == 3 )
				handyman.State = StaffState.PickedUp;
			if ( ticks == 4 )
				seen = handyman.State;
		};
		level.AdvanceFrame( 3 * FixedStepClock.TickDuration );
		Assert.IsNotNull( seen );
		Assert.AreNotEqual( StaffState.PickedUp, seen, "the tick after the stranded pick-up released him" );
		level.TickCompleted = null;
		// a member a pick-up command put in a hand (co-op part 3a: the hands are the level's state) stays picked up through the ticks' bookkeeping, in one frame of many ticks
		var mechanic = members.First( member => member.Type == StaffType.Mechanic );
		var pickUp = level.Commands.Issue( new PickUpStaffCommand( mechanic.Id ) );
		Assert.IsTrue( pickUp.Accepted, pickUp.Reason );
		level.AdvanceFrame( FixedStepClock.MaximumCatchUpTicks * FixedStepClock.TickDuration );
		Assert.AreEqual( StaffState.PickedUp, mechanic.State, "the tick's reconcile leaves a member in a hand alone" );
	}

	/// <summary>
	/// The entities whose per-frame update (<see cref="Entity.Update"/>, after the simulation in <see cref="Level.AdvanceFrame"/>) was audited as presentation only: each reads the park
	/// and writes only its own drawing state (docs/DETERMINISM.md, "Frame batching"). A new entity with an update must be audited and added here.
	/// </summary>
	private static readonly string[] AuditedEntityUpdates =
	{
		// OriginalObject: the ride hoarding's rise and fade (#721, tp.exe 0x0044E410 -> 0x004548F0) advances its level by real frame time; the simulation only sets the
		// hoarding's state, and nothing it hashes or reads uses the level, which a save keeps like the particles (docs/DETERMINISM.md, "Frame batching").
		"OpenTPW.CoasterRenderer", "OpenTPW.OriginalObject", "OpenTPW.ParticleRenderer", "OpenTPW.PoolBridgeRenderer", "OpenTPW.PoolRenderer", "OpenTPW.PoolSlideRenderer",
		"OpenTPW.UI.RootPanel", "OpenTPW.WeatherForecastPresenter", "OpenTPW.WeatherRenderer",
	};

	[TestMethod]
	public void EveryEntityUpdateIsAnAuditedPresentationUpdate()
	{
		var updating = typeof( Entity ).Assembly.GetTypes()
			.Where( type => typeof( Entity ).IsAssignableFrom( type ) && type != typeof( Entity ) )
			.Where( type => type.GetMethod( "OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic )?.DeclaringType == type )
			.Select( type => type.FullName! )
			.OrderBy( name => name, StringComparer.Ordinal )
			.ToArray();
		CollectionAssert.AreEqual( AuditedEntityUpdates.OrderBy( name => name, StringComparer.Ordinal ).ToArray(), updating,
			$"entities with a per-frame update: {string.Join( ", ", updating )}" );
	}
}
