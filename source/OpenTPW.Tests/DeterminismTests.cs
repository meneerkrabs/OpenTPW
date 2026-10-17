using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Scripting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>Reproducible runs (docs/DETERMINISM.md): world seed, per-park state, park-save streams and the canonical hash.</summary>
[TestClass]
[DoNotParallelize]
public class DeterminismTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private const int HalfRun = 6000;
	/// <summary>Six calendar months of park turns (183 days of 23.04 turns, 14.88 ticks each).</summary>
	private const int SixMonthTicks = 62700;

	/// <summary>
	/// Economy, guests, a shop script and an unseeded RAND script wired like Level: guests pay into the
	/// economy and every stream comes from one <see cref="WorldSeed"/>. Ticks run in Level.Update's order.
	/// </summary>
	private sealed class SyntheticPark
	{
		private readonly GuestEconomyBridge payments;
		private readonly RideVisitorBridge shop;
		private readonly RideVM shopScript;
		private readonly RideVisitorBridge? room;
		private readonly RideVM? roomScript;
		private readonly List<RideVM> dice = new();

		public SyntheticPark( WorldSeed seed, IGuestObserver? observer = null, Action<ParkCellMap>? prepareCells = null, bool changingRoom = false )
		{
			Seed = seed;
			Economy = EconomyTestData.Park( seed: seed.EconomyStream );
			Guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), seed.GuestStream ) { Observer = observer };
			// [EXT:pools-save] a fixture that lays cells of its own before the first tick (the pool pin)
			prepareCells?.Invoke( Guests.Grid.Cells );
			// the park's one thing pool as Level builds it: the world's ten singletons, then the gates and the lights (things 1 to 12), so the first guest is thing 13 (WALK-010)
			Things = new ThingPool();
			Things.AllocateWorldSetup();
			Things.Allocate( SaveThingModel.Attraction );
			Things.Allocate( SaveThingModel.Attraction );
			Guests.AttachThings( Things );
			Scripts = new RideScriptWorld( seed.ScriptStream );
			// the park's one world generator, as Level links it: the economy's, which the guests and the scripts draw from
			Guests.AttachWorld( Economy.World );
			Scripts.AttachWorld( Economy.World );
			Sound = new SoundEventSystem( new AudioMixer( null ), seed.SoundStream );
			payments = new GuestEconomyBridge( () => Economy, Guests );
			Guests.Payments = payments;
			Economy.GuestStatistics = payments;
			Economy.RideOperations = payments;
			Economy.OpenPark();
			shop = new RideVisitorBridge( Scripts.AllocateAttractionId(), "Drinks Shop", RideVisitorKind.Shop, 1, 70, 30, GuestNeeds.Thirst ) { EntranceCell = (10, 9), ExitCell = (10, 9) };
			shopScript = GuestTests.CreateShopScript( new VisitorRideScriptEffects( shop, UnimplementedRideScriptEffects.Instance ), Scripts, seed: null );
			shop.Attach( shopScript, () => true );
			Guests.Register( shop );
			Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, Economy.TryBuild( 1203, out var bought ) );
			payments.Link( shop.AttractionId, bought!.Id );
			AddDice();
			// [EXT:pools-swim] a changing room hanging below the cross park's path cell (5, 5), whose back cells (5, 8) and (6, 8) lie in the pool the fixture painted: the swim session joins the run
			if ( changingRoom )
			{
				room = new RideVisitorBridge( Scripts.AllocateAttractionId(), "Changing Room", RideVisitorKind.Facility, 2, 70, 30, GuestNeeds.None )
				{
					EntranceCell = (5, 5), ExitCell = (5, 5), QueueFrontCell = (5, 5), QueueEntranceDirection = 2, HasCells = true,
					IsChangingRoom = true, PoolAccessCandidates = new[] { (5, 8), (6, 8) }
				};
				roomScript = GuestTests.CreateShopScript( new VisitorRideScriptEffects( room, UnimplementedRideScriptEffects.Instance ), Scripts, seed: null );
				room.Attach( roomScript, () => true );
				Guests.Register( room );
				// the entrance cell below the path cell, as a placement lays it
				GuestTests.LayStandCell( Guests.Grid, room );
			}
		}

		public WorldSeed Seed { get; }
		public RideVisitorBridge? Room => room;
		public ThingPool Things { get; }
		public ParkEconomy Economy { get; private set; }
		public GuestSimulation Guests { get; }
		public RideScriptWorld Scripts { get; }
		/// <summary>A device-less sound chooser, as GameAudio.EnterPark seeds it.</summary>
		public SoundEventSystem Sound { get; }
		/// <summary>The same capture, restore and hash code Level's SavePark/LoadPark use; no park runtime here.</summary>
		public ParkWorldStreams Streams => new( Seed, null, Guests, Scripts, Sound );
		public int AttractionId => shop.AttractionId;
		public long Ticks { get; private set; }

		/// <summary>An unseeded script: its seed comes from the park's script stream.</summary>
		public void AddDice() => dice.Add( Script( new Asm()
			.Label( "loop" ).I( Opcode.RAND, V( 0 ), 1000 )
			.I( Opcode.ADD, V( 1 ), V( 0 ) )
			.I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ), new RideVMOptions { World = Scripts } ) );

		public void Run( int ticks )
		{
			for ( var tick = 0; tick < ticks; tick++ )
			{
				// Level.AdvanceSimulation's order: the tick that starts a park turn runs the substep's scripts and then the economy's turn, whose thing sweep updates the guests, the economy's things,
				// the shop's ride update, the turn's destroys and the arrivals (tp.exe 0x0054F4BF, 0x00516380); the ticks between turns update the guests first and free nothing
				var swept = Guests.NextTickStartsTurn && Economy.NextTickStartsTurn;
				// the substep of this tick (tp.exe 0x54F4BF): the script pass runs every script newest first on its own cadence (0x54F56B, 0x5516B0), before the objects' own ticks
				var substep = SimulationSubstep.ForTick( Economy.Tick, Economy.Speed != GameSpeed.Paused );
				if ( swept )
					Guests.BeginSweepTick( Tick );
				else
					Guests.Tick( Tick );
				shop.DefersAdmission = swept;
				if ( substep.Due )
					Scripts.RunPass( SimulationSubstep.Milliseconds );
				shop.HostStep();
				if ( room != null )
				{
					room.DefersAdmission = swept;
					room.HostStep();
				}
				Economy.ThingSweep = swept ? new HarnessSweep( this ) : null;
				try
				{
					Economy.AdvanceFixedTick();
				}
				finally
				{
					Economy.ThingSweep = null;
				}
				// Both halves of a save/load run reach this tick, so the script stream is consumed after a load.
				if ( ++Ticks == HalfRun * 3 / 2 )
					AddDice();
			}
		}

		/// <summary>The park turn of Level's sweep with what this fixture holds: the guests by the pool's list, the economy's own things, the shop and the turn's end.</summary>
		private sealed class HarnessSweep( SyntheticPark park ) : IThingSweep
		{
			public void Run( ParkEconomy economy, long turn, bool lost )
			{
				foreach ( var slot in park.Things.Capture() )
					if ( !park.Things.IsQueued( slot.Id ) )
						park.Guests.SweepGuest( slot.Id );
				park.Guests.FinishSweep();
				economy.UpdateThings( turn );
				park.shop.UpdateAdmission();
				park.room?.UpdateAdmission();
				park.Things.DestroyQueued();
				park.Guests.UpdateTurnEndArrivals();
			}
		}

		public WorldRandomState RandomState => Streams.CaptureRandomState();

		public void Save( string path ) => ParkSaveFile.Save( path, Economy, RandomState );

		public void Load( string path )
		{
			var (economy, world) = ParkSaveFile.LoadState( path, ( theme, easy ) => (Economy.Settings, Economy.Catalog) );
			Economy = economy;
			Economy.GuestStatistics = payments;
			Assert.IsNotNull( world );
			Assert.AreEqual( Seed.Value, world.Seed );
			// the loaded economy holds its own generator object: the guests and the scripts continue it
			Guests.AttachWorld( Economy.World );
			Scripts.AttachWorld( Economy.World );
			Streams.RestoreRandomState( world );
		}

		public ulong Hash => WorldStateHash.Compute( new WorldStateSources { Seed = Seed, Economy = Economy, Guests = Guests, Scripts = Scripts } );
	}

	private static RideVM Script( Asm asm, RideVMOptions options ) =>
		new( new RideScriptFile( new MemoryStream( asm.Build( new[] { "V0", "V1" } ) ) ), options );

	private static ulong RunToEnd( WorldSeed seed )
	{
		var park = new SyntheticPark( seed );
		park.Run( 2 * HalfRun );
		Assert.IsTrue( park.Guests.Admissions > 10, "guests arrived and paid" );
		return park.Hash;
	}

	[TestMethod]
	public void DefaultWorldSeedKeepsThePreviousStreamSeeds()
	{
		var seed = WorldSeed.Default;
		Assert.AreEqual( Level.GuestSeed, seed.GuestStream, "guests keep Level.GuestSeed" );
		Assert.AreEqual( 1UL, seed.EconomyStream, "the economy keeps ParkEconomy.CreateForTheme's seed 1" );
		Assert.AreEqual( 0, seed.ObjectScriptKey, "placed objects keep script seeds 1, 2, 3, …" );
		var other = new WorldSeed( 1 );
		Assert.AreNotEqual( seed.GuestStream, other.GuestStream );
		Assert.AreNotEqual( seed.EconomyStream, other.EconomyStream );
		Assert.AreNotEqual( seed.ScriptStream, other.ScriptStream );
		Assert.AreNotEqual( seed.SoundStream, other.SoundStream );
		Assert.AreNotEqual( 0, other.ObjectScriptKey );
	}

	[TestMethod]
	public void AttractionIdsArePerParkNotPerProcess()
	{
		var first = new RideScriptWorld();
		var second = new RideScriptWorld();
		Assert.AreEqual( 1, first.AllocateAttractionId() );
		Assert.AreEqual( 2, first.AllocateAttractionId() );
		Assert.AreEqual( 1, second.AllocateAttractionId(), "a second park in the same process starts at 1 again" );
		Assert.AreEqual( new SyntheticPark( WorldSeed.Default ).AttractionId, new SyntheticPark( WorldSeed.Default ).AttractionId );
	}

	[TestMethod]
	public void UnseededScriptsDrawFromTheirWorldStream()
	{
		int[] Draws( ulong worldSeed )
		{
			var world = new RideScriptWorld( worldSeed );
			var vm = Script( new Asm().Label( "loop" ).I( Opcode.RAND, V( 0 ), 1000 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ), new RideVMOptions { World = world } );
			return Enumerable.Range( 0, 16 ).Select( _ =>
			{
				vm.Advance( RideVMTests.Tick );
				return vm.Variables[0];
			} ).ToArray();
		}
		CollectionAssert.AreEqual( Draws( 5 ), Draws( 5 ) );
		CollectionAssert.AreNotEqual( Draws( 5 ), Draws( 6 ) );
	}

	[TestMethod]
	public void SameSeedTwiceInOneProcessGivesTheSameHash()
	{
		Assert.AreEqual( RunToEnd( WorldSeed.Default ), RunToEnd( WorldSeed.Default ) );
		Assert.AreEqual( RunToEnd( new WorldSeed( 77 ) ), RunToEnd( new WorldSeed( 77 ) ) );
	}

	[TestMethod]
	public void DifferentSeedGivesADifferentHash()
	{
		Assert.AreNotEqual( RunToEnd( WorldSeed.Default ), RunToEnd( new WorldSeed( 77 ) ) );
		Assert.AreNotEqual( RunToEnd( new WorldSeed( 77 ) ), RunToEnd( new WorldSeed( 78 ) ) );
	}

	/// <summary>
	/// Pins the hash of a fixed run, so it is checked in every test process: equal values in separate
	/// processes and machines mean no process state (counters, Random.Shared, string hashing) leaks in.
	/// A deliberate change to the simulation or to the hashed fields changes this value.
	/// </summary>
	[TestMethod]
	public void HashOfAFixedRunIsTheSameInEveryProcess()
	{
		var park = new SyntheticPark( WorldSeed.Default );
		park.Run( PinnedRunTicks );
		Assert.AreEqual( FixedRunHash, park.Hash, $"0x{park.Hash:X16}" );
	}

	/// <summary>The ticks of the pinned run: 14,400 ticks are 968 park turns, 42 days of the park's calendar, so the run crosses a month end.</summary>
	internal const int PinnedRunTicks = 14400;

	/// <summary>
	/// Six game months of the swept park turn: guests keep arriving, paying and leaving, the pool holds exactly the living guests after each turn's end, nothing waits for a destroy once a
	/// turn has ended, and the park is not lost.
	/// </summary>
	[TestMethod]
	public void SixGameMonthsOfSweptTurnsKeepTheThingsAndTheEconomyConsistent()
	{
		var park = new SyntheticPark( WorldSeed.Default );
		var turn = park.Economy.Turn;
		var turnsSwept = 0;
		var peak = 0;
		for ( var tick = 0; tick < SixMonthTicks; tick++ )
		{
			park.Run( 1 );
			peak = Math.Max( peak, park.Guests.Guests.Count );
			if ( park.Economy.Turn == turn )
				continue;
			turn = park.Economy.Turn;
			turnsSwept++;
			Assert.AreEqual( 0, park.Things.QueuedCount, $"turn {turn}: the turn's end carried out every destroy" );
			CollectionAssert.AreEquivalent( park.Guests.Guests.Select( guest => guest.Id ).ToArray(), park.Things.UsedNewestFirst().Where( slot => slot.Model == SaveThingModel.Guest ).Select( slot => slot.Id ).ToArray(),
				$"turn {turn}: the pool's guest slots are the living guests" );
		}
		Assert.IsTrue( park.Economy.Date.Month >= 6 || park.Economy.Date.Year > 2000, $"six game months passed ({park.Economy.Date})" );
		Assert.IsTrue( turnsSwept > 1200, $"{turnsSwept} turns" );
		Assert.IsTrue( park.Guests.Admissions > 100 && park.Guests.Departed > 20, $"{park.Guests.Admissions} admitted, {park.Guests.Departed} left" );
		Assert.IsTrue( peak < 400, $"the crowd stays bounded ({peak})" );
		Assert.IsFalse( park.Economy.IsBankrupt );
	}

	/// <summary>The hash of <see cref="HashOfAFixedRunIsTheSameInEveryProcess"/>'s run, for tests that repeat it under other conditions.</summary>
	internal static ulong RunFixedPark( IGuestObserver? observer = null )
	{
		var park = new SyntheticPark( WorldSeed.Default, observer );
		park.Run( PinnedRunTicks );
		return park.Hash;
	}

	/// <summary>
	/// [EXT:pools-save] The fixed run of <see cref="HashOfAFixedRunIsTheSameInEveryProcess"/> on a cross park that also has pool cells, painted through the pool builder with
	/// the extension on: a 4 x 2 block and a 3 x 3 block clear of the paths and the shop (docs/POOLS.md, docs/DETERMINISM.md).
	/// </summary>
	internal static ulong RunFixedParkWithPools()
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var park = new SyntheticPark( WorldSeed.Default, null, cells =>
		{
			var builder = new PoolBuilder( cells, () => null, new PoolsTests.FlatGrid( cells.Width, cells.Height ) );
			Assert.IsTrue( builder.PaintCells( PoolBuilder.Rectangle( (12, 7), (15, 8) ).Concat( PoolBuilder.Rectangle( (2, 1), (4, 3) ) ) ).Completed );
			Assert.AreEqual( 17, cells.CountOf( ParkCellType.PoolWater ) );
		} );
		park.Run( PinnedRunTicks );
		return park.Hash;
	}

	/// <summary>
	/// [EXT:pools-swim] The fixed run on the cross park with a pool of 6 x 2 cells (x 3 to 8, y 8 to 9) and a changing room below the path row, its entrance cell (5, 6) reached from the path cell (5, 5) and its back cells (5, 8) and (6, 8) in the pool: guests choose the room like the shop,
	/// change, swim, climb out and walk on (docs/POOLS.md, docs/DETERMINISM.md, "Pools"). Returns the hash, how many guests were in the water at the busiest moment and how many swims the room released.
	/// </summary>
	internal static (ulong Hash, int MostSwimming, int Swims, int Admissions) RunFixedParkWithPoolsAndAChangingRoom()
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var park = new SyntheticPark( WorldSeed.Default, null, cells =>
		{
			var builder = new PoolBuilder( cells, () => null, new PoolsTests.FlatGrid( cells.Width, cells.Height ) );
			Assert.IsTrue( builder.PaintCells( PoolBuilder.Rectangle( (3, 8), (8, 9) ) ).Completed );
		}, changingRoom: true );
		var most = 0;
		for ( var chunk = 0; chunk < PinnedRunTicks / 60; chunk++ )
		{
			park.Run( 60 );
			most = Math.Max( most, park.Guests.Guests.Count( guest => guest.IsSwimming ) );
		}
		return (park.Hash, most, park.Room!.ReleasedTotal, (int)park.Guests.Admissions);
	}

	/// <summary>[EXT:pools-swim] The pinned hash of <see cref="RunFixedParkWithPoolsAndAChangingRoom"/> (docs/DETERMINISM.md, "Pools").</summary>
	internal const ulong PoolsChangingRoomFixedRunHash = 0xB5B033AE20AD9BA0UL;

	[TestMethod]
	public void FixedRunWithAPoolAndAChangingRoomIsTheSameInEveryProcessAndGuestsSwim()
	{
		var (hash, most, swims, admissions) = RunFixedParkWithPoolsAndAChangingRoom();
		Assert.IsTrue( swims >= 5, $"the room was used {swims} times ({admissions} guests admitted)" );
		Assert.IsTrue( most >= 1, $"swimmers were in the water ({most} at most at once)" );
		Assert.AreEqual( hash, RunFixedParkWithPoolsAndAChangingRoom().Hash, "repeatable in the process" );
		Assert.AreEqual( PoolsChangingRoomFixedRunHash, hash, $"0x{hash:X16} ({swims} swims, {most} at once, {admissions} admitted)" );
	}

	/// <summary>[EXT:pools-save] The pinned hash of <see cref="RunFixedParkWithPools"/> (docs/DETERMINISM.md, "Pools").</summary>
	internal const ulong PoolsFixedRunHash = 0x4EEF5812C3D8023DUL;

	/// <summary>The pinned hash of <see cref="HashOfAFixedRunIsTheSameInEveryProcess"/>'s run (docs/DETERMINISM.md).</summary>
	internal const ulong FixedRunHash = 0xA91A607272718E93UL;

	/// <summary>
	/// The pinned hash of the same run with <c>opentpw.smarter-guests</c> on (docs/SMARTER-GUESTS.md): in this run, whose one drinks shop is cheap enough that no guest is refused and which has no second attraction to choose
	/// between, the smarter rules (1.2.0) change no guest: <see cref="GuestSimulation.ComputeStateHash"/> equals the off run's. What moved from 1.1.0's value is the extension's own state, which joins the world hash: the
	/// shop's throughput estimate (phase 3: its average, samples and silence clock) next to the birth ids. The scenario's pin (GuestMeasurementTests) is where the rules act. A deliberate change of the
	/// extension's state, of the rules or of a constant changes this value (and bumps the extension's version).
	/// </summary>
	internal const ulong SmarterGuestsFixedRunHash = 0xC8534726E08D0EC2UL;

	[TestMethod]
	public void SmarterGuestsFixedRunHashIsTheSameInEveryProcess()
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		Assert.IsTrue( FeatureGates.SmarterGuests );
		var park = new SyntheticPark( WorldSeed.Default );
		Assert.IsInstanceOfType( park.Guests.Decisions, typeof( SmarterGuestDecisions ) );
		park.Run( PinnedRunTicks );
		Assert.AreEqual( SmarterGuestsFixedRunHash, park.Hash, $"0x{park.Hash:X16}" );
	}

	/// <summary>
	/// The pinned hash of the same run with the shipped example script mod <c>opentpw.example.patient-guests</c> on (docs/SCRIPT-MODS.md): the world hash carries the mod's state, counters and flag next to the
	/// guest identity state. CI runs it on Windows, macOS and Linux, so an equal value means the interpreter, the stateless <c>rng</c> and the hook wiring give the same park everywhere. A deliberate change
	/// of the example, of the language, of a limit or of the hook wiring changes this value.
	/// </summary>
	internal const ulong ScriptModsFixedRunHash = 0xD7388BB84BBA0676UL;

	/// <summary>
	/// The pinned hash of a run with a test mod that implements all four hooks, keeps counters in its state and draws with <c>rng</c> (the example uses neither <c>scoreAttraction</c>, <c>wanderGoal</c> nor
	/// <c>rng</c>): the cross-platform pin of everything the example does not reach.
	/// </summary>
	internal const ulong ScriptModsAllHooksFixedRunHash = 0xC3190E46B1E70633UL;

	[TestMethod]
	public void ScriptModsFixedRunHashIsTheSameInEveryProcess()
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( TempScriptMods.Example() );
		var park = new SyntheticPark( WorldSeed.Default );
		Assert.IsInstanceOfType( park.Guests.Decisions, typeof( ScriptGuestDecisions ) );
		park.Run( PinnedRunTicks );
		var stats = park.Guests.ScriptMods!.Stats().Single();
		Assert.IsTrue( stats.Calls > 50 && stats.RuntimeErrors + stats.BudgetFailures + stats.InvalidResults == 0, $"{stats.Calls} calls" );
		Assert.AreEqual( ScriptModsFixedRunHash, park.Hash, $"0x{park.Hash:X16}" );
	}

	[TestMethod]
	public void ScriptModsAllHooksFixedRunHashIsTheSameInEveryProcess()
	{
		using var mods = new TempScriptMods().Add( "community.counter", ScriptModsSaveTests.Counter, ScriptModsSaveTests.CounterHooks );
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( mods.Resolve() );
		var park = new SyntheticPark( WorldSeed.Default );
		park.Run( PinnedRunTicks );
		var mod = park.Guests.ScriptMods!.Mods.Single();
		foreach ( var key in new[] { "s", "j", "l", "w" } )
			Assert.IsTrue( mod.State.TryGet( key, out var count ) && count.AsInt > 0, $"the {key} hook ran: {ScriptState.Serialize( mod.State )}" );
		Assert.AreEqual( 0, mod.Failures, mod.LastFailure );
		Assert.AreEqual( ScriptModsAllHooksFixedRunHash, park.Hash, $"0x{park.Hash:X16}" );
	}

	/// <summary>A script mod that only answers <c>prev</c> leaves every guest value, and so the economy, as the off run's; only the world hash differs, by the identity and script state.</summary>
	[TestMethod]
	public void ScriptModsThatAnswerPrevLeaveTheSyntheticParkAsItWasOff()
	{
		using var mods = new TempScriptMods().Add( "community.pass", "function scoreAttraction(guest, c, prev)\n  return prev\nend\nfunction joinQueue(guest, a, prev)\n  return prev\nend\n", "scoreAttraction", "joinQueue" );
		SyntheticPark on;
		using ( OpenTPW.Mods.OpenTpwExtensions.Use( mods.Resolve() ) )
			on = new SyntheticPark( WorldSeed.Default );
		var off = new SyntheticPark( WorldSeed.Default );
		for ( var chunk = 0; chunk < PinnedRunTicks / 1200; chunk++ )
		{
			off.Run( 1200 );
			on.Run( 1200 );
			Assert.AreEqual( off.Guests.ComputeStateHash(), on.Guests.ComputeStateHash(), $"guest state hash after {off.Ticks} ticks" );
			Assert.AreEqual( off.Guests.RandomState, on.Guests.RandomState, $"world generator after {off.Ticks} ticks" );
			Assert.AreEqual( ParkSaveJson( off ), ParkSaveJson( on ), $"economy after {off.Ticks} ticks" );
		}
		Assert.AreEqual( FixedRunHash, off.Hash );
		Assert.AreNotEqual( off.Hash, on.Hash );
		Assert.IsTrue( on.Guests.ScriptMods!.Stats().Single().Calls > 50 );
	}

	/// <summary>In the synthetic park the shipped rules never act, so the guests and the economy are the off run's, tick for tick; only the world hash differs, by the extension's state.</summary>
	[TestMethod]
	public void SmarterGuestsOnWithTheDelegatingPolicyLeavesEveryGuestVisibleValueAsItWasOff()
	{
		var off = new SyntheticPark( WorldSeed.Default );
		SyntheticPark on;
		using ( OpenTPW.Mods.OpenTpwExtensions.Use( SmarterGuestsTests.On() ) )
			on = new SyntheticPark( WorldSeed.Default );
		for ( var chunk = 0; chunk < PinnedRunTicks / 1200; chunk++ )
		{
			off.Run( 1200 );
			on.Run( 1200 );
			Assert.AreEqual( off.Guests.ComputeStateHash(), on.Guests.ComputeStateHash(), $"guest state hash after {off.Ticks} ticks" );
			Assert.AreEqual( off.Guests.RandomState, on.Guests.RandomState, $"world generator after {off.Ticks} ticks" );
			CollectionAssert.AreEqual( off.Guests.Guests.Select( guest => (guest.Id, guest.State, guest.X, guest.Y, guest.Happiness, guest.QueuePosition, guest.AttractionId) ).ToArray(),
				on.Guests.Guests.Select( guest => (guest.Id, guest.State, guest.X, guest.Y, guest.Happiness, guest.QueuePosition, guest.AttractionId) ).ToArray(), $"guests after {off.Ticks} ticks" );
			Assert.AreEqual( System.Text.Json.JsonSerializer.Serialize( off.Guests.CaptureSnapshot() ), System.Text.Json.JsonSerializer.Serialize( on.Guests.CaptureSnapshot() ), $"guest snapshot after {off.Ticks} ticks" );
			Assert.AreEqual( ParkSaveJson( off ), ParkSaveJson( on ), $"economy after {off.Ticks} ticks" );
		}
		Assert.AreEqual( FixedRunHash, off.Hash );
		Assert.AreNotEqual( off.Hash, on.Hash, "the world hash carries the extension's state" );
		Assert.IsNotNull( on.Guests.SmarterGuests );
		Assert.IsNull( off.Guests.SmarterGuests );
	}

	private static string ParkSaveJson( SyntheticPark park ) => System.Text.Json.JsonSerializer.Serialize( ParkSaveFile.Capture( park.Economy ) );

	/// <summary>
	/// Switching only the <c>opentpw.online</c> extension (docs/MODS.md) leaves the pinned digests unchanged: this fixed run,
	/// the guest pathing of a cross park and the empty mod set's digest.
	/// </summary>
	[TestMethod]
	public void TogglingOnlyTheOnlineExtensionKeepsThePinnedDigests()
	{
		ulong? pathing = null;
		foreach ( var online in new[] { true, false } )
		{
			var set = OnlineExtensionGateTests.WithOnline( online );
			using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( set );
			Assert.AreEqual( online, FeatureGates.Online );
			Assert.AreEqual( "", set.Digest, "no files-based mods" );
			var park = new SyntheticPark( WorldSeed.Default );
			park.Run( PinnedRunTicks );
			Assert.AreEqual( FixedRunHash, park.Hash, $"0x{park.Hash:X16}" );
			var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
			for ( var tick = 0; tick < 120 * 60; tick++ )
				simulation.Tick( Tick );
			pathing ??= simulation.ComputeStateHash();
			Assert.AreEqual( pathing, simulation.ComputeStateHash(), "the guest pathing hash does not depend on the online extension" );
		}
	}

	[TestMethod]
	public void SaveAndLoadMidRunGivesTheSameFinalHashAsAnUninterruptedRun()
	{
		var seed = new WorldSeed( 2026 );
		var reference = new SyntheticPark( seed );
		reference.Run( HalfRun );
		var economyStreamAtHalf = reference.Economy.World.State;
		reference.Run( HalfRun );
		Assert.AreNotEqual( economyStreamAtHalf, reference.Economy.World.State, "the world generator is drawn after the save point" );

		var resumed = new SyntheticPark( seed );
		resumed.Run( HalfRun );
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-det-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			var path = Path.Combine( directory, "park.json" );
			resumed.Save( path );
			var atSave = resumed.Hash;
			var streams = resumed.RandomState;
			// Move the world generator away from the save point by drawing, not through the restore path under
			// test (the economy's staff pool draws from it; the guests are covered by
			// LoadRestoresEveryStreamDrawnAfterTheSave, because their ticks also move the unsaved guests of this legacy economy-only save).
			resumed.Economy.Advance( 50_000 );
			resumed.Scripts.World.NextWord();
			Assert.AreNotEqual( atSave, resumed.Hash );
			resumed.Load( path );
			Assert.AreEqual( streams, resumed.RandomState );
			Assert.AreEqual( atSave, resumed.Hash, "the load restores the hashed state of the save point" );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
		resumed.Run( HalfRun );
		Assert.AreEqual( reference.Hash, resumed.Hash, "a save/load mid-run continues exactly like the uninterrupted run" );
	}

	/// <summary>
	/// Every saved stream is moved by drawing from it (guest ticks, a script seed) or, for the sound chooser,
	/// whose draws never store their successor, by reseeding it; never through RestoreRandomState, so a
	/// restore that does nothing fails here.
	/// </summary>
	[TestMethod]
	public void LoadRestoresEveryStreamDrawnAfterTheSave()
	{
		Log ??= new();
		var park = new SyntheticPark( new WorldSeed( 2026 ) );
		park.Run( HalfRun );
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-det-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			var path = Path.Combine( directory, "park.json" );
			park.Sound.Draw();
			park.Save( path );
			var saved = park.RandomState;
			Assert.AreEqual( park.Sound.Seed, saved.SoundSeed, "the chooser's advanced state is what the save holds ([EXT:sound-chooser-seed]: the original's chooser is clock-seeded and unsaved)" );
			Assert.AreNotEqual( park.Seed.SoundStream, saved.SoundSeed, "the draw before the save advanced the chooser (tp.exe 0x6bc84c stores every draw's state)" );
			for ( var tick = 0; tick < 600; tick++ )
				park.Guests.Tick( Tick );
			var afterGuests = park.RandomState.Generator;
			park.Scripts.World.NextWord();
			park.Sound.Draw();
			park.Sound.Reseed( saved.SoundSeed + 1 );
			var moved = park.RandomState;
			Assert.AreNotEqual( saved.Generator, afterGuests, "guest ticks drew from the world generator" );
			Assert.AreNotEqual( afterGuests, moved.Generator, "a script draw moved the same generator" );
			Assert.AreNotEqual( saved.SoundSeed, moved.SoundSeed );
			park.Load( path );
			Assert.AreEqual( saved.Generator, park.Guests.RandomState, "the guests' generator" );
			Assert.AreEqual( saved.Generator, park.Scripts.RandomState, "the scripts' generator" );
			Assert.AreSame( park.Guests.World, park.Scripts.World, "one generator for everyone after the load" );
			Assert.AreEqual( saved.SoundSeed, park.Sound.Seed, "sound seed" );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	/// <summary>The sound chooser advances on UI clicks and audio timing, so it is saved but not hashed.</summary>
	[TestMethod]
	public void SoundDrawsDoNotChangeTheCanonicalHash()
	{
		var park = new SyntheticPark( WorldSeed.Default );
		park.Run( 600 );
		var before = park.Streams.ComputeStateHash();
		park.Sound.Draw();
		Assert.AreNotEqual( park.Seed.SoundStream, park.Sound.Seed, "the draw advanced the chooser" );
		Assert.AreEqual( before, park.Streams.ComputeStateHash() );
		Assert.AreEqual( before, new ParkWorldStreams( park.Seed, null, park.Guests, park.Scripts, null ).ComputeStateHash(), "a headless run hashes like a run with sound" );
	}

	[TestMethod]
	public void ParkSaveRoundTripsTheWorldStreams()
	{
		var park = EconomyTestData.Park();
		var state = new WorldRandomState( 0xFFFF_FFFF_FFFF_FFFF, 0x8000_0001, 0xFFFF_FFFF );
		var json = ParkSaveFile.Serialize( park, state );
		Assert.AreEqual( state, ParkSaveFile.ToWorldState( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ) ) );
		Assert.AreEqual( json, ParkSaveFile.Serialize( ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog ), state ) );
		// Saves without the section (economy-only callers, older files) load with no world streams.
		var economyOnly = ParkSaveFile.Serialize( park );
		Assert.IsNull( ParkSaveFile.ToWorldState( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( economyOnly ) ) ) );
		var older = Regex.Replace( economyOnly, @",\s*""World"": null", "" );
		Assert.AreNotEqual( economyOnly, older, "the section was removed for the older-save case" );
		Assert.IsNull( ParkSaveFile.ToWorldState( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( older ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.Replace( "\"SoundSeed\"", "\"Unknown\"" ) ) ) );
	}

	/// <summary>
	/// Forbidden in simulation code: unseeded or shared <see cref="Random"/> (also target-typed <c>new()</c>),
	/// the wall clock, mutable static numeric fields and properties (process-wide counters), and
	/// <c>GetHashCode</c>/<c>HashCode</c> (string hash codes are randomized per process in .NET).
	/// </summary>
	internal static readonly Regex ForbiddenInSimulation = new(
		@"new\s+(System\.)?Random\s*\(\s*\)|\bRandom\??\s+\w+\s*=\s*new\s*\(\s*\)|Random\.Shared|RandomVector3|Environment\.TickCount|DateTime(Offset)?\.(Utc)?Now|Stopwatch"
		+ @"|^(?!.*\b(readonly|const)\b).*\bstatic\s+(volatile\s+)?(s?byte|u?short|u?int|u?long|nu?int|float|double|decimal|(System\.)?(U?Int(16|32|64)|S?Byte|Single|Double|Decimal))\??\s+\w+\s*([;{]|=(?!>))"
		+ @"|\.GetHashCode\s*\(|\bHashCode\.\w+|new\s+HashCode\s*\(",
		RegexOptions.CultureInvariant );

	private const string ApprovedCivilTimeProvider = "civilTime = GameEditions.Current == GameEdition.ThemeParkWorld ? clock ?? (() => DateTime.Now) : null;";

	internal static bool IsApprovedCivilTimeProvider( string relativePath, string line ) =>
		relativePath.Replace( '\\', '/' ) == "VM/RideScriptWorld.cs" && line.Trim() == ApprovedCivilTimeProvider;

	/// <summary>Top-level World files that are presentation (cameras, sky, advisor UI, sandbox file I/O); every other World file is scanned.</summary>
	private static readonly string[] PresentationWorldFiles =
	{
		"Advisor.cs", "AdvisorController.cs", "AdvisorResponses.cs", "AdvisorScoreQueue.cs", "Camera.cs", "CameraMode.cs",
		"GlobeCameraMode.cs", "LobbyCameraMode.cs", "ParkCameraMode.cs", "SandboxSave.cs", "Sky.cs", "Sun.cs"
	};

	/// <summary>Simulation folders reject uncontrolled sources except one explicitly qualified host-civil provider boundary.</summary>
	[TestMethod]
	public void SimulationCodeUsesNoProcessWideRandomnessOrClock()
	{
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "docs", "DETERMINISM.md" ) ) )
			root = root.Parent;
		if ( root == null )
			Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		var source = Path.Combine( root.FullName, "source", "OpenTPW" );
		var folders = new[] { "VM", "Scripting", "Economy", Path.Combine( "World", "Guests" ), Path.Combine( "World", "Objects" ), Path.Combine( "World", "Coasters" ) };
		var world = Directory.EnumerateFiles( Path.Combine( source, "World" ), "*.cs", SearchOption.TopDirectoryOnly )
			.Where( path => !PresentationWorldFiles.Contains( Path.GetFileName( path ) ) ).ToList();
		CollectionAssert.IsSubsetOf( new[] { "Level.cs", "Level.Objects.cs", "FixedStepClock.cs", "PrototypeRide.cs", "Ride.cs" }, world.Select( Path.GetFileName ).ToArray() );
		var lines = folders.SelectMany( folder => Directory.EnumerateFiles( Path.Combine( source, folder ), "*.cs", SearchOption.AllDirectories ) )
			.Concat( world )
			.SelectMany( path => File.ReadLines( path ).Select( ( line, index ) => (path, line, index) ) ).ToList();
		Assert.AreEqual( 1, lines.Count( item => IsApprovedCivilTimeProvider( Path.GetRelativePath( source, item.path ), item.line ) ),
			"Only the exact single TPW-gated provider boundary is approved (the host clock input, [EXT:vm-host-time])." );
		var hits = lines.Where( item => ForbiddenInSimulation.IsMatch( item.line )
			&& !IsApprovedCivilTimeProvider( Path.GetRelativePath( source, item.path ), item.line ) )
			.Select( item => $"{Path.GetRelativePath( root.FullName, item.path )}:{item.index + 1}: {item.line.Trim()}" ).ToList();
		Assert.AreEqual( 0, hits.Count, string.Join( Environment.NewLine, hits ) );
	}

	/// <summary>Each forbidden form is caught, and the allowed forms next to them are not (the guard's own mutation check).</summary>
	[TestMethod]
	public void SourceGuardCatchesEachForbiddenForm()
	{
		var forbidden = new[]
		{
			"var random = new Random();",
			"private readonly System.Random random = new System.Random( );",
			"System.Random leak = new();",
			"private Random? random = new();",
			"var x = Random.Shared.Next();",
			"var seed = Environment.TickCount;",
			"var now = DateTime.UtcNow;",
			"var now = DateTimeOffset.Now;",
			"var watch = Stopwatch.StartNew();",
			"private static int nextAttractionId = 1;",
			"private static long attractionCounter;",
			"static ulong state = 5;",
			"internal static volatile uint ticks;",
			"private static float scale;",
			"public static System.Int64 Count;",
			"public static int NextId { get; set; }",
			"var bucket = name.GetHashCode() % 7;",
			"var bucket = string.GetHashCode( name );",
			"var bucket = StringComparer.Ordinal.GetHashCode( name );",
			"var key = HashCode.Combine( name, id );",
		};
		var allowed = new[]
		{
			"var random = new Random( seed );",
			"private readonly Random random = new( seed );",
			"private static readonly int Limit = 3;",
			"private readonly static int Limit = 3;",
			"private const int Limit = 3;",
			"private static bool logged;",
			"public static int Clamp( int value ) => value;",
			"private static int Limit => 3;",
			"public override int GetHashCode() => Id;",
			"private int nextId;",
		};
		foreach ( var line in forbidden )
			Assert.IsTrue( ForbiddenInSimulation.IsMatch( line ), line );
		foreach ( var line in allowed )
			Assert.IsFalse( ForbiddenInSimulation.IsMatch( line ), line );
	}

	[TestMethod]
	public void CivilTimeBoundaryDoesNotExemptOtherClockExpressionsOrFiles()
	{
		Assert.IsTrue( IsApprovedCivilTimeProvider( "VM/RideScriptWorld.cs", ApprovedCivilTimeProvider ) );
		Assert.IsTrue( ForbiddenInSimulation.IsMatch( ApprovedCivilTimeProvider ), "The underlying guard still detects the clock." );
		Assert.IsFalse( IsApprovedCivilTimeProvider( "Economy/RideScriptWorld.cs", ApprovedCivilTimeProvider ) );
		Assert.IsFalse( IsApprovedCivilTimeProvider( "VM/Other.cs", ApprovedCivilTimeProvider ) );
		foreach ( var line in new[] { "var leak = DateTime.Now;", "var leak = DateTime.UtcNow;", "var leak = DateTimeOffset.Now;",
			"var leak = DateTimeOffset.UtcNow;", ApprovedCivilTimeProvider + " var leak = DateTime.Now;",
			ApprovedCivilTimeProvider.Replace( "GameEditions.Current == GameEdition.ThemeParkWorld", "true" ) } )
		{
			Assert.IsTrue( ForbiddenInSimulation.IsMatch( line ), line );
			Assert.IsFalse( IsApprovedCivilTimeProvider( "VM/RideScriptWorld.cs", line ), line );
		}
	}
}

/// <summary>Per-park attraction ids with the original objects; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class DeterminismAssetTests
{
	/// <summary>
	/// Level.SavePark/LoadPark delegate to <see cref="ParkWorldStreams"/>; this runs that path with the real
	/// jungle park runtime and guests (Level itself needs a renderer). Every stream is moved by drawing, except the
	/// sound chooser, whose draws never store their successor (0x100118C8 is its only store), which is reseeded.
	/// </summary>
	[TestMethod]
	public void ParkSaveAndLoadThroughTheLevelPathRestoreEveryStream()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the jungle park." );
		Log ??= new();
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-det-{Guid.NewGuid():N}" );
		try
		{
			var seed = new WorldSeed( 2026 );
			var original = OriginalPark.Load( "jungle" );
			var park = ParkEconomyRuntime.ForOriginalLevel( original, ParkStartKind.OriginalSaveReference, seed );
			var guests = new GuestSimulation( GuestPathGrid.FromOriginal( original.Map, original.Save ), GuestSettings.Load( "jungle" ), seed.GuestStream );
			park.AttachGuests( guests );
			var streams = new ParkWorldStreams( seed, park, guests, new RideScriptWorld( seed.ScriptStream ), new SoundEventSystem( new AudioMixer( null ), seed.SoundStream ) ).LinkGenerator();
			void Run( int ticks )
			{
				for ( var tick = 0; tick < ticks; tick++ )
				{
					guests.Tick( 1f / FixedStepClock.TicksPerSecond );
					park.Economy.Advance( (int)park.Economy.Speed );
				}
			}
			park.Economy.Speed = GameSpeed.Fastest;
			Run( 3000 );
			streams.Scripts.World.NextWord();
			streams.Sound!.Draw();
			Directory.CreateDirectory( directory );
			var path = Path.Combine( directory, "park.json" );
			streams.SavePark( path );
			var saved = streams.CaptureRandomState();
			var economy = ParkSaveFile.Serialize( park.Economy );
			Run( 3000 );
			streams.Scripts.World.NextWord();
			// A draw stores the chooser's advanced state (tp.exe 0x6bc84c), and the park saves it so a resume continues the same draws.
			Assert.AreEqual( saved.SoundSeed, streams.CaptureRandomState().SoundSeed, "no draw since the save" );
			streams.Sound.Draw();
			streams.Sound.Reseed( unchecked(saved.SoundSeed + 1) );
			var moved = streams.CaptureRandomState();
			Assert.IsTrue( saved.Generator != moved.Generator && saved.SoundSeed != moved.SoundSeed, "every stream was drawn after the save" );
			Assert.AreNotEqual( economy, ParkSaveFile.Serialize( park.Economy ) );
			streams.LoadPark( path );
			Assert.AreEqual( saved, streams.CaptureRandomState() );
			Assert.AreEqual( economy, ParkSaveFile.Serialize( park.Economy ), "the economy (and its stream) is the saved one" );
			Assert.AreSame( park.Guests, park.Economy.GuestStatistics, "the loaded economy keeps the guest statistics" );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void AParkWithACivilClockObjectHashesTheSameUnderAFixedClock()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the Halloween clock tower." );
		try
		{
			// Haunted Tower (Info.Id 2418) spawns Clock.RSE, which reads HOUR.
			var tower = ObjectCatalog.Load( "hallow" ).Find( 2418 ) ?? throw new AssertFailedException( "Haunted Tower is missing from the Halloween catalog." );
			ulong Run( Func<DateTime> clock )
			{
				var world = new RideScriptWorld( 7, clock );
				var runtime = new OriginalObjectRuntime( tower, world, seed: 1, open: true );
				for ( var tick = 0; tick < 120 * FixedStepClock.TicksPerSecond; tick++ )
					runtime.Simulate( FixedStepClock.TickDuration );
				var hash = WorldStateHash.Compute( new WorldStateSources { Scripts = world } );
				Assert.IsTrue( world.Scripts.Count > 0, "the tower's scripts are live when hashed" );
				runtime.Stop();
				return hash;
			}
			var reads = 0;
			var fixedRun = Run( () => { reads++; return RideScriptWorld.FixedCivilTime; } );
			Assert.IsTrue( reads > 0, "the tower's scripts read the civil clock" );
			Assert.AreEqual( fixedRun, Run( RideScriptWorld.FixedClock ), "a fixed civil time gives the same run" );
			Assert.AreNotEqual( Run( () => new DateTime( 2000, 1, 1, 3, 0, 0 ) ), Run( () => new DateTime( 2000, 1, 1, 16, 0, 0 ) ),
				"the hour reaches the world state, so a headless run that pins a hash must fix the clock" );
			// MIN and SEC are host time too (tp.exe 0x5566FF, 0x55674E), so a two-run comparison with a real clock can flake on them as well.
			// In this 120 s run the minute and second do not reach the hash (only the hour does).
			Assert.AreEqual( Run( () => new DateTime( 2000, 1, 1, 3, 10, 20 ) ), Run( () => new DateTime( 2000, 1, 1, 3, 40, 50 ) ) );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void OriginalObjectAttractionIdsRestartInEachPark()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the object corpus." );
		var bonus = ObjectCatalog.BonusDataRoot;
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var entries = ObjectCatalog.Load( "jungle" ).Entries.Where( entry => entry.ScriptPath != null ).Take( 4 ).ToArray();
			(int[] Ids, ulong Hash) Park()
			{
				var seed = WorldSeed.Default;
				var world = new RideScriptWorld( seed.ScriptStream );
				var runtimes = entries.Select( ( entry, index ) => new OriginalObjectRuntime( entry, world, (index + 1) ^ seed.ObjectScriptKey ) ).ToArray();
				for ( var tick = 0; tick < 10 * FixedStepClock.TicksPerSecond; tick++ )
					foreach ( var runtime in runtimes )
						runtime.Simulate( FixedStepClock.TickDuration );
				return (runtimes.Select( runtime => runtime.Visitors.AttractionId ).ToArray(), WorldStateHash.Compute( new WorldStateSources { Scripts = world } ));
			}
			var first = Park();
			var second = Park();
			CollectionAssert.AreEqual( Enumerable.Range( 1, entries.Length ).ToArray(), first.Ids );
			CollectionAssert.AreEqual( first.Ids, second.Ids, "a second park in the same process gets the same ids" );
			Assert.AreEqual( first.Hash, second.Hash );
		}
		finally
		{
			ObjectCatalog.BonusDataRoot = bonus;
			FileSystem = previous;
		}
	}
}
