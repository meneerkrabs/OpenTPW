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
	internal sealed class SyntheticPark
	{
		private readonly GuestEconomyBridge payments;
		private readonly RideVisitorBridge shop;
		private readonly RideVM shopScript;
		private readonly RideVisitorBridge? room;
		private readonly RideVM? roomScript;
		private readonly RideVisitorBridge? brolly;
		private readonly RideVM? brollyScript;
		private readonly List<(RideVisitorBridge Bridge, RideVM Script)> winter = new();
		private readonly List<RideVM> dice = new();

		public SyntheticPark( WorldSeed seed, IGuestObserver? observer = null, Action<ParkCellMap>? prepareCells = null, bool changingRoom = false, Action<GuestSimulation>? prepareGuests = null, IGuestDecisions? decisions = null, bool brollyShop = false, int brollyKidScore = 75, bool winterShops = false,
			IReadOnlyList<WinterTuningMeasureTests.ShopPrice>? winterPrices = null )
		{
			Seed = seed;
			Economy = EconomyTestData.Park( seed: seed.EconomyStream );
			Guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), seed.GuestStream, decisions ) { Observer = observer };
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
			// [EXT:umbrellas-extension] a brolly shop near the top end of the path column (10, 1), paid through the economy like the drinks shop, and a weather that rains for forty seconds and clears for forty, again and again:
			// guests buy umbrellas in the rain, keep them through rides closed and let them go when it clears (docs/UMBRELLAS.md, docs/DETERMINISM.md). brollyKidScore defaults to the shipped
			// GenerationModifier.KidScorePercentage, 75, Theme Park Inc's value per the owner's decision, so a run that does not pass one mirrors the shops' .sam data.
			if ( brollyShop )
			{
				brolly = new RideVisitorBridge( Scripts.AllocateAttractionId(), "Brolly Shop", RideVisitorKind.Shop, 1, 60, 30, GuestNeeds.None )
				{
					EntranceCell = (10, 1), ExitCell = (10, 1), AppearanceEffect = UmbrellaRules.ShopAppearanceEffect, UmbrellaTerms = new UmbrellaShopTerms( 200, 200, 150, 5, brollyKidScore )
				};
				brollyScript = GuestTests.CreateShopScript( new VisitorRideScriptEffects( brolly, UnimplementedRideScriptEffects.Instance ), Scripts, seed: null );
				brolly.Attach( brollyScript, () => true );
				Guests.Register( brolly );
				Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, Economy.TryBuild( 1203, out var boughtBrolly ) );
				payments.Link( brolly.AttractionId, boughtBrolly!.Id );
				Guests.UmbrellaWeatherState = () => Guests.TickCount is >= 1200 and < 4800 or >= 8400 and < 12000 ? UmbrellaRules.WeatherRain : UmbrellaRules.WeatherNone;
			}
			// [EXT:winter-extension] a winter kiosk at (10, 1) and a glühwein stall at (10, 8) on the path column, paid through the economy like the drinks shop, and a snowfall in two spells with a May that turns into December half way
			// (docs/WINTER.md, docs/DETERMINISM.md, "Winter"): guests buy a head item and then mittens, and drinks that are warm
			if ( winterShops )
			{
				var kioskTerms = new WinterShopTerms( 0, 0, 200, 150, 60, 20, 60 );
				var stallTerms = new WinterShopTerms( 130, 130, 250, 150, 60, 20, 60 );
				var shopIndex = 0;
				foreach ( var (name, cell, appearance, terms, ingredient) in new[] { ("Winter Kiosk", (10, 1), WinterRules.KioskAppearanceEffect, kioskTerms, 0), ("Gluehwein Stall", (10, 8), 0, stallTerms, 3) } )
				{
					var bridge = new RideVisitorBridge( Scripts.AllocateAttractionId(), name, RideVisitorKind.Shop, 1, 60, 30, ingredient == 0 ? GuestNeeds.None : GuestNeeds.Thirst )
					{
						EntranceCell = cell, ExitCell = cell, AppearanceEffect = appearance, WinterTerms = terms, SpecialIngredient = ingredient
					};
					var script = GuestTests.CreateShopScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Scripts, seed: null );
					bridge.Attach( script, () => true );
					Guests.Register( bridge );
					Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, Economy.TryBuild( 1203, out var boughtWinter ) );
					payments.Link( bridge.AttractionId, boughtWinter!.Id );
					// [EXT:winter-tuning] the measurement's price terms (docs/WINTER.md, "Price"): the shop's shipped .sam words the price test reads, and the economy's live price mirrored over the bridge as the level does;
					// without them (the pins) the shops are free visits that no guest refuses
					if ( winterPrices?[shopIndex] is { } price )
					{
						Economy.SetPrices( boughtWinter.Id, price.Price, price.InitCostOfGoods );
						bridge.Sideshow = new SideshowTerms( price.InitCostOfGoods, price.Price, 0 );
						(bridge.InitCostOfGoods, bridge.ThirstEffect, bridge.HungerEffect, bridge.VomitEffect, bridge.HappinessEffect, bridge.RipOffOk) =
							(price.InitCostOfGoods, price.ThirstEffect, price.HungerEffect, price.VomitEffect, price.HappinessEffect, price.RipOffOk);
					}
					shopIndex++;
					winter.Add( (bridge, script) );
				}
				Guests.UmbrellaWeatherState = () => Guests.TickCount is >= 1200 and < 4800 or >= 8400 and < 12000 ? UmbrellaRules.WeatherSnow : UmbrellaRules.WeatherNone;
				Guests.WinterMonth = () => Guests.TickCount < 6000 ? 5 : 12;
			}
			// [EXT:pools-attractions] a fixture that registers pool activities of its own (the water attractions pin)
			prepareGuests?.Invoke( Guests );
		}

		public WorldSeed Seed { get; }
		public RideVisitorBridge? Room => room;
		public RideVisitorBridge? Brolly => brolly;
		public IReadOnlyList<RideVisitorBridge> WinterShops => winter.Select( shop => shop.Bridge ).ToList();
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
		/// <summary>Run every tick through <see cref="GuestSimulation.Tick"/>, its turn branch included, instead of the hand-driven sweep of Level's turn.</summary>
		public bool ThroughGuestsTick { get; init; }

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
				// the shop's ride update, the turn's destroys and the arrivals (tp.exe 0x0054F4BF, 0x00516380); the ticks between turns only count the guests' seconds and move their drawn points
				// (with ThroughGuestsTick every tick, the turn's too, goes through GuestSimulation.Tick: the production path of a standalone simulation)
				var swept = !ThroughGuestsTick && Guests.NextTickStartsTurn && Economy.NextTickStartsTurn;
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
				if ( brolly != null )
				{
					brolly.DefersAdmission = swept;
					brolly.HostStep();
				}
				foreach ( var (bridge, _) in winter )
				{
					bridge.DefersAdmission = swept;
					bridge.HostStep();
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
				park.brolly?.UpdateAdmission();
				foreach ( var (bridge, _) in park.winter )
					bridge.UpdateAdmission();
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

		public ulong Hash
		{
			get
			{
				var sources = new WorldStateSources { Seed = Seed, Economy = Economy, Guests = Guests, Scripts = Scripts };
				return GuestRefusedTargetTests.VerifyRetainedTargetHash( Guests, () => WorldStateHash.Compute( sources ), sources );
			}
		}
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

	/// <summary>
	/// [EXT:evening] The evening and night extension is presentation only: a park run with it on, with the day cycle stepping beside the simulation the way Level.Update does, ends with the
	/// same world hash as the run without it, and as the plain run every other pin here is made with.
	/// </summary>
	[TestMethod]
	public void TheEveningExtensionChangesNoWorldHashOfAParkRun()
	{
		ulong Run( bool extensionOn )
		{
			using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( EveningTests.Set( extensionOn ) );
			var level = EveningTests.BareLevel();
			var park = new SyntheticPark( WorldSeed.Default );
			for ( var chunk = 0; chunk < 2 * HalfRun / 60; chunk++ )
			{
				park.Run( 60 );
				// a frame of presentation beside every second of simulation; half the frames are paused, which the cycle also survives
				level.AdvanceEvening( FeatureGates.Evening, EveningPace.Minutes10, 0.25, playing: chunk % 2 == 0 );
			}
			Assert.AreEqual( extensionOn, level.EveningDayClock != null );
			Assert.IsTrue( park.Guests.Admissions > 10, "guests arrived and paid" );
			return park.Hash;
		}
		var off = Run( extensionOn: false );
		Assert.AreEqual( off, Run( extensionOn: true ), "the evening extension leaves the world hash alone" );
		Assert.AreEqual( RunToEnd( WorldSeed.Default ), off, "and the run is the plain run every other pin is made with" );
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
	internal static (ulong Hash, int MostSwimming, int Swims, int Admissions) RunFixedParkWithPoolsAndAChangingRoom( bool fullRegistry = false )
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var park = new SyntheticPark( WorldSeed.Default, null, cells =>
		{
			var builder = new PoolBuilder( cells, () => null, new PoolsTests.FlatGrid( cells.Width, cells.Height ) );
			Assert.IsTrue( builder.PaintCells( PoolBuilder.Rectangle( (3, 8), (8, 9) ) ).Completed );
		}, changingRoom: true, prepareGuests: fullRegistry ? guests =>
		{
			// [EXT:pools-attractions] the registry the level fills while the extension is on, with no water attraction in the park and no river: nothing offers, nothing is drawn
			foreach ( var activity in PoolActivityRegistry.Create( new PoolAttractionSites( () => Array.Empty<PoolAttractionSite>() ) ) )
				guests.RegisterPoolActivity( activity );
		}
		: null );
		var most = 0;
		for ( var chunk = 0; chunk < PinnedRunTicks / 60; chunk++ )
		{
			park.Run( 60 );
			most = Math.Max( most, park.Guests.Guests.Count( guest => guest.IsSwimming ) );
		}
		return (park.Hash, most, park.Room!.ReleasedTotal, (int)park.Guests.Admissions);
	}

	/// <summary>[EXT:pools-swim] The pinned hash of <see cref="RunFixedParkWithPoolsAndAChangingRoom"/> (docs/DETERMINISM.md, "Pools").</summary>
	internal const ulong PoolsChangingRoomFixedRunHash = 0xA794F0319F569CE2UL;

	[TestMethod]
	public void FixedRunWithAPoolAndAChangingRoomIsTheSameInEveryProcessAndGuestsSwim()
	{
		var (hash, most, swims, admissions) = RunFixedParkWithPoolsAndAChangingRoom();
		Assert.IsTrue( swims >= 5, $"the room was used {swims} times ({admissions} guests admitted)" );
		Assert.IsTrue( most >= 1, $"swimmers were in the water ({most} at most at once)" );
		Assert.AreEqual( hash, RunFixedParkWithPoolsAndAChangingRoom().Hash, "repeatable in the process" );
		Assert.AreEqual( PoolsChangingRoomFixedRunHash, hash, $"0x{hash:X16} ({swims} swims, {most} at once, {admissions} admitted)" );
	}

	/// <summary>
	/// [EXT:umbrellas-extension] The fixed run on the cross park with a brolly shop at (10, 1), near the top of the path column and a weather that rains from the 20th to the 80th second and from the 140th to the 200th and is clear in between (docs/UMBRELLAS.md,
	/// docs/DETERMINISM.md, "Umbrellas"): guests buy umbrellas in the rain, close them in every attraction, open them again on qualifying days and let them go when it clears. Returns the hash, the guests who bought one, the
	/// umbrella states seen (at one-second chunks) and the balloon-free sales the shop paid.
	/// </summary>
	internal static (ulong Hash, int Buyers, HashSet<int> States, int Admissions) RunFixedParkWithABrollyShopInTheRain()
	{
		var park = new SyntheticPark( WorldSeed.Default, brollyShop: true );
		Assert.AreEqual( 75, park.Brolly!.UmbrellaTerms!.Value.KidScorePercentage, "the synthetic brolly shop mirrors the shipped KidScorePercentage" );
		var buyers = new HashSet<int>();
		var states = new HashSet<int>();
		for ( var chunk = 0; chunk < PinnedRunTicks / 60; chunk++ )
		{
			park.Run( 60 );
			foreach ( var guest in park.Guests.Guests.Where( guest => guest.UmbrellaState != 0 ) )
			{
				buyers.Add( guest.Id );
				states.Add( guest.UmbrellaState );
			}
		}
		return (park.Hash, buyers.Count, states, (int)park.Guests.Admissions);
	}

	/// <summary>[EXT:umbrellas-extension] The pinned hash of <see cref="RunFixedParkWithABrollyShopInTheRain"/> (docs/DETERMINISM.md, "Umbrellas").</summary>
	internal const ulong UmbrellasFixedRunHash = 0x7F084940237B577FUL;

	[TestMethod]
	public void FixedRunWithABrollyShopInTheRainIsTheSameInEveryProcessAndGuestsCarryUmbrellas()
	{
		var (hash, buyers, states, admissions) = RunFixedParkWithABrollyShopInTheRain();
		Assert.IsTrue( buyers >= 5, $"{buyers} guests bought an umbrella ({admissions} admitted)" );
		CollectionAssert.IsSubsetOf( new[] { 1, 2, 3, 4 }, states.ToArray(), "every state of the machine was seen: " + string.Join( ", ", states.OrderBy( state => state ) ) );
		Assert.AreEqual( hash, RunFixedParkWithABrollyShopInTheRain().Hash, "repeatable in the process" );
		Assert.AreNotEqual( FixedRunHash, hash, "the shop, the weather and the colour draws change the run" );
		Assert.AreEqual( UmbrellasFixedRunHash, hash, $"0x{hash:X16} ({buyers} buyers, {admissions} admitted)" );
	}

	/// <summary>
	/// [EXT:winter-extension] The fixed run on the cross park with a winter kiosk at (10, 1) and a glühwein stall at (10, 8) on its path column, a snowfall from the 20th to the 80th second and from the 140th to the 200th and a calendar
	/// that is May for the first 100 seconds and December after it (docs/WINTER.md, docs/DETERMINISM.md, "Winter"): guests buy a hat or earmuffs and then mittens, the kiosk sells all year at the season's percentage (summer-like 60 in May, 150 in December, 200 in
	/// the snow) and the stall through its own percentages. Returns the hash, the guests who bought an item, the item combinations seen (every tick, since a guest who buys its mittens late may leave within a second), the guests with a complete outfit and the guests admitted.
	/// </summary>
	internal static (ulong Hash, int Buyers, HashSet<int> Items, int Dressed, int Admissions, int[] Sales) RunFixedParkWithWinterShopsInTheSnow()
	{
		var park = new SyntheticPark( WorldSeed.Default, winterShops: true );
		var shopIds = park.WinterShops.Select( shop => shop.AttractionId ).ToArray();
		var sales = new int[shopIds.Length];
		park.Guests.MoneySpent += ( guest, amount, attraction ) =>
		{
			var index = Array.IndexOf( shopIds, attraction );
			if ( index >= 0 )
				sales[index]++;
		};
		var buyers = new HashSet<int>();
		var items = new HashSet<int>();
		var dressed = new HashSet<int>();
		// sampled every tick: a guest who buys its mittens late may leave the park within a second
		for ( var tick = 0; tick < PinnedRunTicks; tick++ )
		{
			park.Run( 1 );
			foreach ( var guest in park.Guests.Guests.Where( guest => guest.WarmItems != 0 ) )
			{
				buyers.Add( guest.Id );
				items.Add( guest.WarmItems );
				if ( WinterRules.SlotsFull( guest.WarmItems ) )
					dressed.Add( guest.Id );
			}
		}
		return (park.Hash, buyers.Count, items, dressed.Count, (int)park.Guests.Admissions, sales);
	}

	/// <summary>[EXT:winter-extension] The pinned hash of <see cref="RunFixedParkWithWinterShopsInTheSnow"/> (docs/DETERMINISM.md, "Winter").</summary>
	internal const ulong WinterFixedRunHash = 0x55C6A3E48DC56D9CUL;

	[TestMethod]
	public void FixedRunWithWinterShopsInTheSnowIsTheSameInEveryProcessAndGuestsWearTheirPurchases()
	{
		var (hash, buyers, items, dressed, admissions, sales) = RunFixedParkWithWinterShopsInTheSnow();
		Assert.IsTrue( sales[0] >= 5 && sales[1] >= 5, $"the kiosk sold {sales[0]} times and the stall {sales[1]}" );
		Assert.IsTrue( buyers >= 5, $"{buyers} guests bought a warm item ({admissions} admitted)" );
		Assert.IsTrue( items.Any( combination => (combination & WinterRules.HeadItems) != 0 ) && items.Any( combination => (combination & WinterRules.Mittens) != 0 ), "head items and mittens were bought: " + string.Join( ", ", items.OrderBy( combination => combination ) ) );
		Assert.IsTrue( items.All( WinterRules.IsValidItems ) );
		Assert.AreEqual( hash, RunFixedParkWithWinterShopsInTheSnow().Hash, "repeatable in the process" );
		Assert.AreNotEqual( FixedRunHash, hash, "the shops, the weather and the purchases change the run" );
		Assert.AreEqual( WinterFixedRunHash, hash, $"0x{hash:X16} ({buyers} buyers, {dressed} fully dressed, {admissions} admitted, kiosk {sales[0]} and stall {sales[1]} sales)" );
	}

	/// <summary>The park day the December run starts on: day 320 is mid-November (December starts on day 335), so the run's 35 days cross the first December's rolls and their snow.</summary>
	private const int DecemberRunStartDay = 320;

	/// <summary>
	/// [EXT:winter-weather] The fixed run on the cross park with a brolly shop and the rain mood rule on, driven by the jungle's real weather instead of a hand-made schedule: a <see cref="ParkWeather"/> (seed 21, the jungle's shipped bands) is stepped from the park's
	/// day 320 (mid-November) over the run's 35 days, so it crosses the first December, with <paramref name="wintryDecembers"/> (the gate of the Wintry Decembers option) off or on. Off, December's rain bands are the original's thunderstorms and rain; on,
	/// they are snow, which the umbrella rules and the rain mood read as snow (docs/DETERMINISM.md, "Wintry Decembers"). Returns the hash, the guests who bought an umbrella, the guests the rain mood changed and the weather state of every park turn, for the test to look at.
	/// </summary>
	internal static (ulong Hash, int Buyers, int Affected, string States) RunFixedParkWithABrollyShopAndRainMoodInTheJungleWeatherAcrossDecember( bool wintryDecembers, bool alwaysWinter = false )
	{
		var weather = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10, wintryDecembers, alwaysWinter );
		var startTurn = (long)DecemberRunStartDay * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn;
		// the park's clock starts at the epoch: the weather steps from there to the start day (as Level steps it with the economy's tick), then follows the run
		weather.AdvanceTo( ParkCalendar.TickOfTurn( 0 ) );
		weather.AdvanceTo( ParkCalendar.TickOfTurn( startTurn ) );
		var states = new System.Text.StringBuilder();
		var park = new SyntheticPark( WorldSeed.Default, brollyShop: true, prepareGuests: guests =>
		{
			guests.RainMood = RainMoodSettings.Default;
			guests.UmbrellaWeatherState = () =>
			{
				weather.AdvanceTo( ParkCalendar.TickOfTurn( startTurn + ParkCalendar.Turn( guests.TickCount ) ) );
				return UmbrellaRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow );
			};
		} );
		var buyers = new HashSet<int>();
		var affected = new HashSet<int>();
		for ( var chunk = 0; chunk < PinnedRunTicks / 60; chunk++ )
		{
			park.Run( 60 );
			states.Append( UmbrellaRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow ) );
			foreach ( var guest in park.Guests.Guests )
			{
				if ( guest.UmbrellaState != 0 )
					buyers.Add( guest.Id );
				if ( guest.RainMoodDay != 0 )
					affected.Add( guest.Id );
			}
		}
		return (park.Hash, buyers.Count, affected.Count, states.ToString());
	}

	/// <summary>[EXT:winter-weather] The pinned hash of <see cref="RunFixedParkWithABrollyShopAndRainMoodInTheJungleWeatherAcrossDecember"/> with the option off: the original's weather across December (docs/DETERMINISM.md, "Wintry Decembers").</summary>
	internal const ulong JungleDecemberOffFixedRunHash = 0xE5E0857EEA5A0DE1UL;

	/// <summary>[EXT:winter-weather] The pinned hash of the same run with the option on: December's rain bands are snow for the umbrellas and the rain mood (docs/DETERMINISM.md, "Wintry Decembers").</summary>
	internal const ulong JungleDecemberWintryFixedRunHash = 0x8B66871BC4BF3F33UL;

	[TestMethod]
	public void UmbrellasAndRainMoodUnderTheJungleWeatherAcrossDecemberArePinnedWithWintryDecembersOffAndOn()
	{
		var off = RunFixedParkWithABrollyShopAndRainMoodInTheJungleWeatherAcrossDecember( false );
		var on = RunFixedParkWithABrollyShopAndRainMoodInTheJungleWeatherAcrossDecember( true );
		Assert.AreEqual( off.Hash, RunFixedParkWithABrollyShopAndRainMoodInTheJungleWeatherAcrossDecember( false ).Hash, "repeatable in the process" );
		Assert.IsFalse( off.States.Contains( (char)('0' + UmbrellaRules.WeatherSnow) ), "the original's jungle never snows: " + off.States );
		Assert.IsTrue( on.States.Contains( (char)('0' + UmbrellaRules.WeatherSnow) ), "with the option on December snows: " + on.States );
		Assert.IsTrue( off.States.Contains( (char)('0' + UmbrellaRules.WeatherRain) ) || off.States.Contains( (char)('0' + UmbrellaRules.WeatherStorm) ), "and it rains before it: " + off.States );
		Assert.AreNotEqual( off.Hash, on.Hash, "the guests meet a different sky, so the run differs" );
		Assert.IsTrue( off.Buyers >= 1 && off.Affected >= 1, $"off: {off.Buyers} buyers, {off.Affected} affected by the rain mood" );
		Assert.AreEqual( JungleDecemberOffFixedRunHash, off.Hash, $"off: 0x{off.Hash:X16} ({off.Buyers} buyers, {off.Affected} affected; {off.States})" );
		Assert.AreEqual( JungleDecemberWintryFixedRunHash, on.Hash, $"on: 0x{on.Hash:X16} ({on.Buyers} buyers, {on.Affected} affected; {on.States})" );
	}

	[TestMethod]
	public void EveryExistingPinIsUnchangedWithTheWinterExtensionAndTheWinterWeatherOnOrOffAndNoWinterShop()
	{
		foreach ( var winter in new[] { true, false } )
			foreach ( var weather in new[] { true, false } )
			{
				using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( OpenTPW.Mods.ModSet.Resolve( Array.Empty<OpenTPW.Mods.DiscoveredMod>(),
					new OpenTPW.Mods.ModSettings { Enabled = new Dictionary<string, bool> { [OpenTPW.Mods.OfficialExtensions.Winter.Id] = winter, [OpenTPW.Mods.OfficialExtensions.WinterWeather.Id] = weather } }, null, GameEdition.ThemeParkWorld, OpenTPW.Mods.OfficialExtensions.All ) );
				Assert.AreEqual( (winter, weather), (FeatureGates.Winter, FeatureGates.WinterWeather) );
				Assert.AreEqual( FixedRunHash, RunFixedPark(), $"the fixed run, winter {winter}, wintry Decembers {weather}" );
				Assert.AreEqual( PoolsChangingRoomFixedRunHash, RunFixedParkWithPoolsAndAChangingRoom().Hash, $"the changing room run, winter {winter}, wintry Decembers {weather}" );
				Assert.AreEqual( UmbrellasFixedRunHash, RunFixedParkWithABrollyShopInTheRain().Hash, $"the umbrellas run, winter {winter}, wintry Decembers {weather}" );
				Assert.AreEqual( RainMoodFixedRunHash, RunFixedParkInTheRainWithRainMood( brollyShop: false ).Hash, $"the rain mood run, winter {winter}, wintry Decembers {weather}" );
				Assert.AreEqual( RainMoodWithUmbrellasFixedRunHash, RunFixedParkInTheRainWithRainMood( brollyShop: true ).Hash, $"the rain mood run with umbrellas, winter {winter}, wintry Decembers {weather}" );
				// the guests under real jungle weather across a December, the weather built with the option's gate as Level builds it: the option off is the original's weather, on is the wintry one
				var december = RunFixedParkWithABrollyShopAndRainMoodInTheJungleWeatherAcrossDecember( FeatureGates.WinterWeather );
				Assert.AreEqual( weather ? JungleDecemberWintryFixedRunHash : JungleDecemberOffFixedRunHash, december.Hash, $"umbrellas and rain mood across a December, winter {winter}, wintry Decembers {weather}" );
			}
	}

	/// <summary>The park day the always winter run starts on: day 121 is the first of May (the calendar starts on 2000-01-01, a leap year).</summary>
	private const int AlwaysWinterRunStartDay = 121;

	/// <summary>Weather turns per park turn of the always winter run: the run's 42 days of park turns walk the weather over 168 days, from May to mid-October.</summary>
	private const int AlwaysWinterRunTurnScale = 4;

	/// <summary>
	/// [EXT:winter-always] The fixed run on the cross park with a brolly shop, the winter shops (a kiosk and a glühwein stall) and the rain mood rule on, driven by the jungle's real weather (seed 21, the shipped bands) from the first of May
	/// over 168 days, four weather turns to each park turn, so the guests meet May to mid-October: the months that never snow in the original. The winter shops read the weather's own calendar month, and with
	/// <paramref name="alwaysWinter"/> (the gate of the Always winter option) the weather rolls December's row all year and its rain bands are snow, and the shops sell at their winter demand (docs/DETERMINISM.md, "Always winter").
	/// Returns the hash, the guests who bought an umbrella, the guests who bought a warm item, the guests the rain mood changed, the weather state of every second and the months the run crossed.
	/// </summary>
	internal static (ulong Hash, int Buyers, int Dressed, int Affected, string States, SortedSet<int> Months) RunFixedParkWithShopsAndRainMoodInTheJungleWeatherFromMay( bool alwaysWinter, bool wintryDecembers = false )
	{
		var weather = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10, wintryDecembers, alwaysWinter );
		var startTurn = (long)AlwaysWinterRunStartDay * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn;
		weather.AdvanceTo( ParkCalendar.TickOfTurn( 0 ) );
		weather.AdvanceTo( ParkCalendar.TickOfTurn( startTurn ) );
		var months = new SortedSet<int>();
		var park = new SyntheticPark( WorldSeed.Default, brollyShop: true, winterShops: true, prepareGuests: guests =>
		{
			long Sync()
			{
				var turn = startTurn + ParkCalendar.Turn( guests.TickCount ) * AlwaysWinterRunTurnScale;
				weather.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
				return turn;
			}
			guests.RainMood = RainMoodSettings.Default;
			guests.WinterAlways = alwaysWinter;
			guests.UmbrellaWeatherState = () =>
			{
				Sync();
				return UmbrellaRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow );
			};
			guests.WinterWeatherState = () =>
			{
				Sync();
				return WinterRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow );
			};
			guests.WinterMonth = () => ParkCalendar.ToDate( ParkCalendar.TickOfTurn( Sync() ) ).Month;
		} );
		var states = new System.Text.StringBuilder();
		var buyers = new HashSet<int>();
		var dressed = new HashSet<int>();
		var affected = new HashSet<int>();
		for ( var chunk = 0; chunk < PinnedRunTicks / 60; chunk++ )
		{
			park.Run( 60 );
			states.Append( UmbrellaRules.WeatherStateOf( weather.Rain, weather.Lightning, weather.Snow ) );
			months.Add( park.Guests.WinterMonth!() );
			foreach ( var guest in park.Guests.Guests )
			{
				if ( guest.UmbrellaState != 0 )
					buyers.Add( guest.Id );
				if ( guest.WarmItems != 0 )
					dressed.Add( guest.Id );
				if ( guest.RainMoodDay != 0 )
					affected.Add( guest.Id );
			}
		}
		return (park.Hash, buyers.Count, dressed.Count, affected.Count, states.ToString(), months);
	}

	/// <summary>[EXT:winter-always] The pinned hash of <see cref="RunFixedParkWithShopsAndRainMoodInTheJungleWeatherFromMay"/> with the Always winter option on (docs/DETERMINISM.md, "Always winter").</summary>
	internal const ulong WinterAlwaysFixedRunHash = 0x9E14F4733B0DE59EUL;

	[TestMethod]
	public void ShopsAndRainMoodUnderTheJungleWeatherFromMayArePinnedWithAlwaysWinterOn()
	{
		var off = RunFixedParkWithShopsAndRainMoodInTheJungleWeatherFromMay( false );
		var on = RunFixedParkWithShopsAndRainMoodInTheJungleWeatherFromMay( true );
		var snow = (char)('0' + UmbrellaRules.WeatherSnow);
		CollectionAssert.IsSubsetOf( new[] { 5, 6, 7, 8, 9 }, on.Months.ToArray(), "the run crosses May to September: " + string.Join( ",", on.Months ) );
		Assert.IsFalse( off.States.Contains( snow ), "the original's jungle never snows: " + off.States );
		Assert.IsTrue( on.States.Count( state => state == snow ) * 2 > on.States.Length, "with the option on it snows most of the time, in the summer too: " + on.States );
		Assert.IsFalse( on.States.Contains( (char)('0' + UmbrellaRules.WeatherRain) ) || on.States.Contains( (char)('0' + UmbrellaRules.WeatherStorm) ), "and every rain band is snow: " + on.States );
		Assert.AreEqual( on.Hash, RunFixedParkWithShopsAndRainMoodInTheJungleWeatherFromMay( true ).Hash, "repeatable in the process" );
		Assert.AreEqual( on.Hash, RunFixedParkWithShopsAndRainMoodInTheJungleWeatherFromMay( true, wintryDecembers: true ).Hash, "the option implies the wintry Decembers: both on is the same run" );
		Assert.AreNotEqual( off.Hash, on.Hash, "the guests meet a different sky and the shops a winter demand, so the run differs" );
		Assert.IsTrue( on.Buyers >= 1 && on.Dressed >= 1 && on.Affected >= 1, $"on: {on.Buyers} umbrella buyers, {on.Dressed} warm item buyers, {on.Affected} affected by the rain mood" );
		Assert.AreEqual( WinterAlwaysFixedRunHash, on.Hash, $"on: 0x{on.Hash:X16} ({on.Buyers} umbrella buyers, {on.Dressed} warm item buyers, {on.Affected} affected; off 0x{off.Hash:X16}; {on.States})" );
	}

	[TestMethod]
	public void EveryExistingPinIsUnchangedWithTheAlwaysWinterOptionOff()
	{
		foreach ( var enabled in new[] { false, true } )
		{
			using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( OpenTPW.Mods.ModSet.Resolve( Array.Empty<OpenTPW.Mods.DiscoveredMod>(),
				new OpenTPW.Mods.ModSettings { Enabled = new Dictionary<string, bool> { [OpenTPW.Mods.OfficialExtensions.WinterAlways.Id] = enabled } }, null, GameEdition.ThemeParkWorld, OpenTPW.Mods.OfficialExtensions.All ) );
			Assert.AreEqual( enabled, FeatureGates.WinterAlways );
			// the synthetic parks set their weather by hand, so the switch alone changes none of these pins
			Assert.AreEqual( FixedRunHash, RunFixedPark(), $"the fixed run, always winter {enabled}" );
			Assert.AreEqual( UmbrellasFixedRunHash, RunFixedParkWithABrollyShopInTheRain().Hash, $"the umbrellas run, always winter {enabled}" );
			Assert.AreEqual( RainMoodWithUmbrellasFixedRunHash, RunFixedParkInTheRainWithRainMood( brollyShop: true ).Hash, $"the rain mood run with umbrellas, always winter {enabled}" );
			Assert.AreEqual( WinterFixedRunHash, RunFixedParkWithWinterShopsInTheSnow().Hash, $"the winter run, always winter {enabled}" );
			// the guests under the real jungle weather, built with the option's gate as Level builds it: off is every existing pin of that run
			var december = RunFixedParkWithABrollyShopAndRainMoodInTheJungleWeatherAcrossDecember( false, FeatureGates.WinterAlways );
			var wintry = RunFixedParkWithABrollyShopAndRainMoodInTheJungleWeatherAcrossDecember( true, FeatureGates.WinterAlways );
			if ( enabled )
			{
				Assert.AreNotEqual( JungleDecemberOffFixedRunHash, december.Hash, "on: November and December are wintry" );
				Assert.AreEqual( december.Hash, wintry.Hash, "on, the wintry Decembers add nothing: no double effect" );
			}
			else
			{
				Assert.AreEqual( JungleDecemberOffFixedRunHash, december.Hash, "off: the original's weather" );
				Assert.AreEqual( JungleDecemberWintryFixedRunHash, wintry.Hash, "off: the wintry Decembers alone, unchanged" );
			}
		}
	}

	/// <summary>
	/// [EXT:rain-mood-extension] The fixed run on the cross park in a weather that rains from the 20th to the 80th second and from the 140th to the 200th and is clear in between, with the rain mood rule on (docs/UMBRELLAS.md,
	/// docs/DETERMINISM.md, "Rain mood"), with or without the brolly shop of <see cref="RunFixedParkWithABrollyShopInTheRain"/> (its own weather is replaced by the same rain). Returns the hash and how many guests the rule changed.
	/// </summary>
	internal static (ulong Hash, int Affected) RunFixedParkInTheRainWithRainMood( bool brollyShop )
	{
		var park = new SyntheticPark( WorldSeed.Default, brollyShop: brollyShop, prepareGuests: guests =>
		{
			guests.RainMood = RainMoodSettings.Default;
			guests.UmbrellaWeatherState = () => guests.TickCount is >= 1200 and < 4800 or >= 8400 and < 12000 ? UmbrellaRules.WeatherRain : UmbrellaRules.WeatherNone;
		} );
		var affected = new HashSet<int>();
		for ( var chunk = 0; chunk < PinnedRunTicks / 60; chunk++ )
		{
			park.Run( 60 );
			foreach ( var guest in park.Guests.Guests.Where( guest => guest.RainMoodDay != 0 ) )
				affected.Add( guest.Id );
		}
		return (park.Hash, affected.Count);
	}

	/// <summary>[EXT:rain-mood-extension] The pinned hash of <see cref="RunFixedParkInTheRainWithRainMood"/> without a brolly shop (docs/DETERMINISM.md, "Rain mood").</summary>
	internal const ulong RainMoodFixedRunHash = 0x43818BD4AC22950EUL;

	/// <summary>[EXT:rain-mood-extension] The pinned hash of <see cref="RunFixedParkInTheRainWithRainMood"/> with the brolly shop: the umbrellas and the rain mood rule both on (docs/DETERMINISM.md, "Rain mood").</summary>
	internal const ulong RainMoodWithUmbrellasFixedRunHash = 0xA6343896042E628DUL;

	[TestMethod]
	public void FixedRunInTheRainWithRainMoodIsTheSameInEveryProcessAndMovesTheRun()
	{
		var (hash, affected) = RunFixedParkInTheRainWithRainMood( brollyShop: false );
		Assert.IsTrue( affected >= 5, $"{affected} guests were changed by the rule" );
		Assert.AreEqual( hash, RunFixedParkInTheRainWithRainMood( brollyShop: false ).Hash, "repeatable in the process" );
		Assert.AreNotEqual( FixedRunHash, hash, "the rule changes the run" );
		Assert.AreEqual( RainMoodFixedRunHash, hash, $"0x{hash:X16} ({affected} affected)" );
	}

	[TestMethod]
	public void FixedRunInTheRainWithRainMoodAndUmbrellasIsTheSameInEveryProcess()
	{
		var (hash, affected) = RunFixedParkInTheRainWithRainMood( brollyShop: true );
		Assert.IsTrue( affected >= 5, $"{affected} guests were changed by the rule" );
		Assert.AreEqual( hash, RunFixedParkInTheRainWithRainMood( brollyShop: true ).Hash, "repeatable in the process" );
		Assert.AreNotEqual( RunFixedParkInTheRainWithRainMood( brollyShop: false ).Hash, hash, "the umbrellas change the run again" );
		Assert.AreNotEqual( UmbrellasFixedRunHash, hash, "the rule changes the umbrellas run" );
		Assert.AreEqual( RainMoodWithUmbrellasFixedRunHash, hash, $"0x{hash:X16} ({affected} affected)" );
	}

	[TestMethod]
	public void EveryExistingPinIsUnchangedWhateverTheRainMoodSwitchWhileTheSyntheticParksSetTheRuleByHand()
	{
		foreach ( var enabled in new[] { false, true } )
		{
			using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( OpenTPW.Mods.ModSet.Resolve( Array.Empty<OpenTPW.Mods.DiscoveredMod>(),
				new OpenTPW.Mods.ModSettings { Enabled = new Dictionary<string, bool> { [OpenTPW.Mods.OfficialExtensions.RainMood.Id] = enabled } }, null, GameEdition.ThemeParkWorld, OpenTPW.Mods.OfficialExtensions.All ) );
			Assert.AreEqual( enabled, FeatureGates.RainMood );
			// the synthetic parks set the rule on their guests by hand (Level does it from the gate: RainMoodTests.TheLevelLeavesTheRuleOffByDefault...), so this checks that the switch alone changes none of these pins, not the level's wiring
			Assert.AreEqual( FixedRunHash, RunFixedPark(), $"the fixed run, rain mood {enabled}" );
			Assert.AreEqual( PoolsChangingRoomFixedRunHash, RunFixedParkWithPoolsAndAChangingRoom().Hash, $"the changing room run, rain mood {enabled}" );
			Assert.AreEqual( UmbrellasFixedRunHash, RunFixedParkWithABrollyShopInTheRain().Hash, $"the umbrellas run, rain mood {enabled}" );
		}
	}

	/// <summary>
	/// [EXT:winter-cold] The fixed run on the cross park in a weather that snows from the 20th to the 80th second and from the 140th to the 200th and is clear in between, with the cold rule on (docs/WINTER.md, docs/DETERMINISM.md,
	/// "Winter cold"). With <paramref name="everything"/> the rain mood, the brolly shop and the winter shops (a kiosk and a glühwein stall, December) are on as well: the cold rule replaces the rain mood's snow happiness, the stall warms
	/// and the kiosk dresses guests. Returns the hash, how many guests the cold rule changed and how many bought a warm item or a glühwein.
	/// </summary>
	internal static (ulong Hash, int Affected, int Dressed) RunFixedParkInTheSnowWithCold( bool everything )
	{
		var park = new SyntheticPark( WorldSeed.Default, brollyShop: everything, winterShops: everything, prepareGuests: guests =>
		{
			guests.WinterCold = WinterColdSettings.Default;
			guests.WinterDay = () => ParkCalendar.DayIndex( guests.TickCount );
			if ( everything )
				guests.RainMood = RainMoodSettings.Default;
			guests.UmbrellaWeatherState = () => guests.TickCount is >= 1200 and < 4800 or >= 8400 and < 12000 ? UmbrellaRules.WeatherSnow : UmbrellaRules.WeatherNone;
		} );
		var affected = new HashSet<int>();
		var dressed = new HashSet<int>();
		for ( var chunk = 0; chunk < PinnedRunTicks / 60; chunk++ )
		{
			park.Run( 60 );
			foreach ( var guest in park.Guests.Guests )
			{
				if ( guest.ColdMoodDay != 0 )
					affected.Add( guest.Id );
				if ( guest.HoldsWinter )
					dressed.Add( guest.Id );
			}
		}
		return (park.Hash, affected.Count, dressed.Count);
	}

	/// <summary>[EXT:winter-cold] The pinned hash of <see cref="RunFixedParkInTheSnowWithCold"/> with only the cold rule on (docs/DETERMINISM.md, "Winter cold").</summary>
	internal const ulong WinterColdFixedRunHash = 0x290320BF12153E75UL;

	/// <summary>[EXT:winter-cold] The pinned hash of <see cref="RunFixedParkInTheSnowWithCold"/> with the cold rule, the rain mood, the brolly shop and the winter shops all on (docs/DETERMINISM.md, "Winter cold").</summary>
	internal const ulong WinterColdEverythingFixedRunHash = 0xB5FF8D87B78B5547UL;

	[TestMethod]
	public void FixedRunInTheSnowWithColdIsTheSameInEveryProcessAndMovesTheRun()
	{
		var (hash, affected, _) = RunFixedParkInTheSnowWithCold( everything: false );
		Assert.IsTrue( affected >= 5, $"{affected} guests were changed by the rule" );
		Assert.AreEqual( hash, RunFixedParkInTheSnowWithCold( everything: false ).Hash, "repeatable in the process" );
		Assert.AreNotEqual( FixedRunHash, hash, "the rule changes the run" );
		Assert.AreEqual( WinterColdFixedRunHash, hash, $"0x{hash:X16} ({affected} affected)" );
	}

	[TestMethod]
	public void FixedRunInTheSnowWithColdRainMoodUmbrellasAndWinterShopsIsTheSameInEveryProcess()
	{
		var (hash, affected, dressed) = RunFixedParkInTheSnowWithCold( everything: true );
		Assert.IsTrue( affected >= 5, $"{affected} guests were changed by the rule" );
		Assert.IsTrue( dressed >= 1, $"{dressed} guests wore an item or were warmed" );
		Assert.AreEqual( hash, RunFixedParkInTheSnowWithCold( everything: true ).Hash, "repeatable in the process" );
		Assert.AreNotEqual( RunFixedParkInTheSnowWithCold( everything: false ).Hash, hash, "the other extensions change the run again" );
		Assert.AreEqual( WinterColdEverythingFixedRunHash, hash, $"0x{hash:X16} ({affected} affected, {dressed} dressed)" );
	}

	[TestMethod]
	public void EveryExistingPinIsUnchangedWhateverTheColdSwitchWhileTheSyntheticParksSetTheRuleByHand()
	{
		foreach ( var enabled in new[] { false, true } )
		{
			using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( OpenTPW.Mods.ModSet.Resolve( Array.Empty<OpenTPW.Mods.DiscoveredMod>(),
				new OpenTPW.Mods.ModSettings { Enabled = new Dictionary<string, bool> { [OpenTPW.Mods.OfficialExtensions.WinterCold.Id] = enabled } }, null, GameEdition.ThemeParkWorld, OpenTPW.Mods.OfficialExtensions.All ) );
			Assert.AreEqual( enabled, FeatureGates.WinterCold );
			// the synthetic parks set the rule on their guests by hand (Level does it from the gate: WinterColdTests), so this checks that the switch alone changes none of these pins
			Assert.AreEqual( FixedRunHash, RunFixedPark(), $"the fixed run, cold {enabled}" );
			Assert.AreEqual( PoolsChangingRoomFixedRunHash, RunFixedParkWithPoolsAndAChangingRoom().Hash, $"the changing room run, cold {enabled}" );
			Assert.AreEqual( UmbrellasFixedRunHash, RunFixedParkWithABrollyShopInTheRain().Hash, $"the umbrellas run, cold {enabled}" );
			Assert.AreEqual( RainMoodFixedRunHash, RunFixedParkInTheRainWithRainMood( brollyShop: false ).Hash, $"the rain mood run, cold {enabled}" );
		}
	}

	[TestMethod]
	public void EveryExistingPinIsUnchangedWithTheUmbrellasExtensionOnOrOffAndNoBrollyShop()
	{
		foreach ( var enabled in new[] { true, false } )
		{
			using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( OpenTPW.Mods.ModSet.Resolve( Array.Empty<OpenTPW.Mods.DiscoveredMod>(),
				new OpenTPW.Mods.ModSettings { Enabled = new Dictionary<string, bool> { [OpenTPW.Mods.OfficialExtensions.Umbrellas.Id] = enabled } }, null, GameEdition.ThemeParkWorld, OpenTPW.Mods.OfficialExtensions.All ) );
			Assert.AreEqual( enabled, FeatureGates.Umbrellas );
			Assert.AreEqual( FixedRunHash, RunFixedPark(), $"the fixed run, umbrellas {enabled}" );
			Assert.AreEqual( PoolsChangingRoomFixedRunHash, RunFixedParkWithPoolsAndAChangingRoom().Hash, $"the changing room run, umbrellas {enabled}" );
		}
	}

	/// <summary>
	/// [EXT:pools-extras] The same run as <see cref="RunFixedParkWithPoolsAndAChangingRoom"/> with the four pool activities registered and extras placed around the pool (x 3 to 8, y 8 to 9): a ladder at (4, 7) and a diving board at (7, 7)
	/// facing south, a shower at (4, 6) beside the room and a two-seat lounger at (4, 10) and (5, 10) facing north (docs/POOLS.md, "Extras"). Returns the hash and how many guests did each activity (seen at one-second chunks).
	/// </summary>
	internal static (ulong Hash, IReadOnlyDictionary<string, int> Activities, int Swims) RunFixedParkWithPoolExtras()
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var park = new SyntheticPark( WorldSeed.Default, null, cells =>
		{
			var builder = new PoolBuilder( cells, () => null, new PoolsTests.FlatGrid( cells.Width, cells.Height ) );
			Assert.IsTrue( builder.PaintCells( PoolBuilder.Rectangle( (3, 8), (8, 9) ) ).Completed );
		}, changingRoom: true );
		foreach ( var activity in PoolActivityRegistry.Create() )
			park.Guests.RegisterPoolActivity( activity );
		var sites = new[]
		{
			new PoolExtraSite( 9001, PoolExtraKind.Ladder, 0, new[] { (4, 7) } ),
			new PoolExtraSite( 9002, PoolExtraKind.DivingBoard, 0, new[] { (7, 7) } ),
			new PoolExtraSite( 9003, PoolExtraKind.Shower, 0, new[] { (4, 6) } ),
			new PoolExtraSite( 9004, PoolExtraKind.Lounger, 180, new[] { (5, 10), (4, 10) } )
		};
		park.Guests.PoolExtraSites = () => sites;
		var seen = new Dictionary<string, HashSet<int>>();
		for ( var chunk = 0; chunk < PinnedRunTicks / 60; chunk++ )
		{
			park.Run( 60 );
			foreach ( var guest in park.Guests.Guests.Where( guest => guest.InPoolActivity ) )
			{
				if ( !seen.TryGetValue( guest.PoolActivityId!, out var ids ) )
					seen[guest.PoolActivityId!] = ids = new HashSet<int>();
				ids.Add( guest.Id );
			}
		}
		return (park.Hash, seen.OrderBy( pair => pair.Key, StringComparer.Ordinal ).ToDictionary( pair => pair.Key, pair => pair.Value.Count ), park.Room!.ReleasedTotal);
	}

	/// <summary>[EXT:pools-extras] The pinned hash of <see cref="RunFixedParkWithPoolExtras"/> (docs/DETERMINISM.md, "Pools").</summary>
	internal const ulong PoolExtrasFixedRunHash = 0x50092F36C43AC693UL;

	[TestMethod]
	public void FixedRunWithPoolExtrasIsTheSameInEveryProcessAndGuestsUseThem()
	{
		var (hash, activities, swims) = RunFixedParkWithPoolExtras();
		var summary = string.Join( ", ", activities.Select( pair => $"{pair.Key} {pair.Value}" ) );
		Assert.IsTrue( swims >= 5, $"the room was used {swims} times" );
		Assert.IsTrue( activities.Count >= 3, $"swimmers used the extras: {summary}" );
		Assert.AreEqual( hash, RunFixedParkWithPoolExtras().Hash, "repeatable in the process" );
		Assert.AreNotEqual( PoolsChangingRoomFixedRunHash, hash, "the extras change the run" );
		Assert.AreEqual( PoolExtrasFixedRunHash, hash, $"0x{hash:X16} ({swims} swims; {summary})" );
	}

	[TestMethod]
	public void FixedRunWithAPoolAndAChangingRoomIsUnchangedByTheFullActivityRegistryWithNothingToOffer()
	{
		var (hash, _, swims, _) = RunFixedParkWithPoolsAndAChangingRoom( fullRegistry: true );
		Assert.IsTrue( swims >= 5 );
		Assert.AreEqual( PoolsChangingRoomFixedRunHash, hash, "the registry of P5 offers nothing in a park without river cells or water attractions, draws nothing and hashes nothing" );
	}

	/// <summary>
	/// [EXT:pools-attractions] The fixed run with a changing room beside a lazy river channel of 10 cells round an island of land (x 6 to 9, y 7 to 9), a wave machine's settings on its access cell (medium
	/// height, short interval), and a rope swing, water jets and a spa pool beside the water (the room's back cells (5, 8) and (6, 8) are in the middle row) (given as sites, as the level derives them from placed objects): the swim sessions use all of them.
	/// Returns the hash, the activities started by id, the river rides and the happiness the waves and the spray paid (docs/POOLS.md, docs/DETERMINISM.md, "Pools").
	/// </summary>
	internal static (ulong Hash, Dictionary<string, int> Started, int Swims) RunFixedParkWithWaterAttractions()
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		// the pool is a channel one cell wide round an island of land (x 7 and 8 of row 8), the hard case of a lazy river: its bends cut the corner squares that are coping; the room's back cell (6, 8) is in it
		var sites = new PoolAttractionSites( () => new[]
		{
			new PoolAttractionSite( 9001, PoolAttractionKind.RopeSwing, new[] { (5, 7) }, (6, 7) ),
			new PoolAttractionSite( 9002, PoolAttractionKind.Jets, new[] { (8, 6) }, (8, 7) ),
			new PoolAttractionSite( 9003, PoolAttractionKind.Spa, new[] { (8, 10) }, (8, 9) ),
			new PoolAttractionSite( 9004, PoolAttractionKind.WaveMachine, new[] { (10, 8) }, (9, 8) )
		} );
		var park = new SyntheticPark( WorldSeed.Default, null, cells =>
		{
			var builder = new PoolBuilder( cells, () => null, new PoolsTests.FlatGrid( cells.Width, cells.Height ) );
			var island = new HashSet<(int X, int Y)> { (7, 8), (8, 8) };
			var lane = PoolBuilder.Rectangle( (6, 7), (9, 9) ).Where( cell => !island.Contains( cell ) ).ToArray();
			Assert.IsTrue( builder.PaintCells( lane, river: true ).Completed );
			Assert.AreEqual( 1, PoolRiverMap.Of( cells ).Rings.Count );
			Assert.AreEqual( 10, PoolRiverMap.Of( cells ).Rings[0].Cells.Count );
			PoolWaveSettings.SetAt( cells, (9, 8), new PoolWaveSettings( true, 2, 0 ) );
		}, changingRoom: true, prepareGuests: guests =>
		{
			foreach ( var activity in PoolActivityRegistry.Create( sites ) )
				guests.RegisterPoolActivity( activity );
		} );
		var started = new Dictionary<string, int>();
		var doing = new Dictionary<int, string?>();
		for ( var chunk = 0; chunk < PinnedRunTicks / 30; chunk++ )
		{
			park.Run( 30 );
			// a start is a guest seen in an activity it was not in at the last look (the shortest activity is longer than a chunk)
			foreach ( var guest in park.Guests.Guests )
			{
				var id = guest.State == GuestState.PoolActivity ? guest.PoolActivityId : null;
				if ( id != null && doing.GetValueOrDefault( guest.Id ) != id )
					started[id] = started.GetValueOrDefault( id ) + 1;
				doing[guest.Id] = id;
			}
		}
		return (park.Hash, started, park.Room!.ReleasedTotal);
	}

	/// <summary>[EXT:pools-attractions] The pinned hash of <see cref="RunFixedParkWithWaterAttractions"/> (docs/DETERMINISM.md, "Pools").</summary>
	internal const ulong PoolsAttractionsFixedRunHash = 0x824DA576B3644CA7UL;

	[TestMethod]
	public void FixedRunWithWaterAttractionsIsTheSameInEveryProcessAndSwimmersUseThem()
	{
		var (hash, started, swims) = RunFixedParkWithWaterAttractions();
		Assert.IsTrue( swims >= 5, $"the room was used {swims} times" );
		Assert.AreEqual( hash, RunFixedParkWithWaterAttractions().Hash, "repeatable in the process" );
		var summary = string.Join( ", ", started.OrderBy( pair => pair.Key ).Select( pair => $"{pair.Key} {pair.Value}" ) );
		Assert.IsTrue( started.GetValueOrDefault( "attraction.ropeswing" ) >= 1 && started.GetValueOrDefault( "attraction.jets" ) >= 1 && started.GetValueOrDefault( "attraction.spa" ) >= 1 && started.GetValueOrDefault( "attraction.river" ) >= 1,
			$"every kind of activity was used: {summary} ({swims} swims)" );
		Assert.AreEqual( PoolsAttractionsFixedRunHash, hash, $"0x{hash:X16} ({swims} swims; {summary})" );
	}

	/// <summary>[EXT:pools-save] The pinned hash of <see cref="RunFixedParkWithPools"/> (docs/DETERMINISM.md, "Pools").</summary>
	internal const ulong PoolsFixedRunHash = 0xA31B7485202FE636UL;

	/// <summary>The pinned hash of <see cref="TheTurnBranchOfGuestsTickIsPinned"/>'s run (docs/DETERMINISM.md, "The guests' update is the turn's sweep").</summary>
	internal const ulong GuestsTickRunHash = 0xC50A9583B4D05226UL;

	/// <summary>
	/// The turn branch of <see cref="GuestSimulation.Tick"/> (the sweep newest first, the arrival update) as the pooled Level's turn runs it when the economy does not sweep: the synthetic park attaches a shared pool,
	/// so the destroys are the pool owner's and the owned-pool path (<c>things.DestroyQueued()</c> in Tick) is not in this run; the other pins drive the sweep by hand as Level does. This run goes through Tick on
	/// every tick, so a change of that branch moves a pin. The standalone path (no AttachThings) is asserted by <c>GuestThingIdTests.AStandaloneSimulationFreesItsDestroysAtTheTurnTickOnly</c>.
	/// </summary>
	[TestMethod]
	public void TheTurnBranchOfGuestsTickIsPinned()
	{
		ulong Run()
		{
			var park = new SyntheticPark( WorldSeed.Default ) { ThroughGuestsTick = true };
			park.Run( 2 * HalfRun );
			Assert.IsTrue( park.Guests.Admissions > 10, "guests arrived and paid" );
			return park.Hash;
		}
		var hash = Run();
		Assert.AreEqual( hash, Run(), "repeatable in the process" );
		Assert.AreEqual( GuestsTickRunHash, hash, $"0x{hash:X16}" );
	}

	/// <summary>The pinned hash of <see cref="HashOfAFixedRunIsTheSameInEveryProcess"/>'s run (docs/DETERMINISM.md).</summary>
	internal const ulong FixedRunHash = 0xB4AEC10551DF2738UL;

	/// <summary>
	/// The pinned hash of the same run with <c>opentpw.smarter-guests</c> on (docs/SMARTER-GUESTS.md): in this run, whose one drinks shop is cheap enough that no guest is refused and which has no second attraction to choose
	/// between, the smarter rules of phases 2 to 5 (1.4.0) change no guest: <see cref="GuestSimulation.ComputeStateHash"/> equals the off run's. What moved from 1.1.0's value is the extension's own state, which joins the world hash: the
	/// shop's throughput estimate (phase 3: its average, samples and silence clock) next to the birth ids. The scenario's pin (GuestMeasurementTests) is where the rules act. 1.5.0 (phase 6, clean and quiet paths) moved this pin from main's value for the 1.4.0 rules: the cross park's few path cells crowd (five guests in one cell), so guests complain of the crowd and the wander goal moves to a quieter candidate, which changes happiness and walks, and the complaint times join the hash (docs/DETERMINISM.md). A deliberate change of the
	/// extension's state, of the rules or of a constant changes this value (and bumps the extension's version).
	/// </summary>
	internal const ulong SmarterGuestsFixedRunHash = 0xFD4DAA23C3C5C5A8UL;

	/// <summary>
	/// The pinned hash of the head-hang draw of a theme without costume heads (docs/DETERMINISM.md): a script world with <see cref="RideScriptWorld.HeadHangReseeds"/> on runs a bump ride's vehicle creation and a load, a tour
	/// boarding and ADDHEAD and WALKON hangs; each hang reseeds the world generator with the rider and draws once (tp.exe 0x44B410 → 0x4FCAC0). No asset is read, so CI pins it on every runtime.
	/// </summary>
	internal const ulong HeadHangFixedRunHash = 0xE998936EFFF3BAE5UL;

	[TestMethod]
	public void HeadHangDrawFixedRunHashIsTheSameInEveryProcess()
	{
		ulong Run( bool reseeds )
		{
			var scripts = new RideScriptWorld( 7 ) { HeadHangReseeds = reseeds };
			var loop = new BumpControllerTests.Loop( -5, scripts.Bumpers );
			loop.Controller.SeatHeadSource = _ => new[] { true, true };
			loop.Controller.Command( 7, 0 );
			loop.Controller.Command( 1, 21 );
			loop.Controller.Command( 1, 22 );
			loop.Controller.Command( 4, 0 );
			loop.Controller.Command( 4, 0 );
			loop.Controller.Command( 1, 23 );
			loop.Controller.Command( 12, 0 );
			loop.Run( 600 );
			var tour = new TourController( TourStation.Default, scripts.NextWorldDraw, scripts.Tours ) { HeadNodes = 3 };
			tour.Board( 31 );
			tour.Board( 32 );
			var space = RideHeadSpace.Of( new[] { 3, 4, 5 }, scripts.NextWorldDraw );
			space.HeadHung = scripts.HeadHung;
			space.Add( 41 );
			space.Hang( 2, 42 );
			return WorldStateHash.Compute( new WorldStateSources { Scripts = scripts } );
		}
		var hash = Run( reseeds: true );
		Assert.AreEqual( hash, Run( reseeds: true ), "the run repeats" );
		Assert.AreNotEqual( hash, Run( reseeds: false ), "the head-hang draws are in the hash" );
		Assert.AreEqual( HeadHangFixedRunHash, hash, $"0x{hash:X16}" );
	}

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
	internal const ulong ScriptModsFixedRunHash = 0xB673E778DB9A7BDBUL;

	/// <summary>
	/// The pinned hash of a run with a test mod that implements all four hooks, keeps counters in its state and draws with <c>rng</c> (the example uses neither <c>scoreAttraction</c>, <c>wanderGoal</c> nor
	/// <c>rng</c>): the cross-platform pin of everything the example does not reach.
	/// </summary>
	internal const ulong ScriptModsAllHooksFixedRunHash = 0xD606ED998555A7E8UL;

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

	/// <summary>In the synthetic park the rules of phases 2 to 5 never act, so the guests and the economy are the off run's, tick for tick; only the world hash differs, by the extension's state.</summary>
	[TestMethod]
	public void SmarterGuestsPhaseFiveRulesLeaveEveryGuestVisibleValueAsItWasOff()
	{
		var off = new SyntheticPark( WorldSeed.Default );
		var on = new SyntheticPark( WorldSeed.Default, decisions: new SmarterGuestDecisions( SmarterRuleSet.Phase5 ) );
		AssertSameGuests( off, on );
		Assert.AreEqual( FixedRunHash, off.Hash );
		Assert.AreNotEqual( off.Hash, on.Hash, "the world hash carries the extension's state" );
		Assert.IsNotNull( on.Guests.SmarterGuests );
		Assert.IsNull( off.Guests.SmarterGuests );
	}

	private sealed class PathCounter : IGuestObserver
	{
		public int Litter, Crowd, Moved;
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) { }
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
		public void PathComplained( Guest guest, PathComplaint complaint ) { if ( complaint == PathComplaint.Litter ) Litter++; else Crowd++; }
		public void WanderGoalMoved( Guest guest ) => Moved++;
	}

	/// <summary>The path rules (phase 6) act in the synthetic cross park, where its few path cells crowd: the counters prove the pinned run exercises the crowd complaint and the goal move (it has no litter map worth the name).</summary>
	[TestMethod]
	public void SmarterGuestsPathRulesActInTheSyntheticParkAndItsPinnedRunDiffersFromPhaseFive()
	{
		var counter = new PathCounter();
		var all = new SyntheticPark( WorldSeed.Default, counter, decisions: new SmarterGuestDecisions( SmarterRuleSet.All ) );
		all.Run( PinnedRunTicks );
		Assert.IsTrue( counter.Crowd > 0 && counter.Moved > 0, $"crowd complaints {counter.Crowd}, goals moved {counter.Moved}" );
		var phase5 = new SyntheticPark( WorldSeed.Default, decisions: new SmarterGuestDecisions( SmarterRuleSet.Phase5 ) );
		phase5.Run( PinnedRunTicks );
		Assert.AreNotEqual( phase5.Hash, all.Hash, "the path rules change this run" );
		Assert.AreEqual( SmarterGuestsFixedRunHash, all.Hash, $"0x{all.Hash:X16}" );
	}

	private static void AssertSameGuests( SyntheticPark off, SyntheticPark on )
	{
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

	/// <summary>
	/// Forbidden in simulation code because their results depend on the runtime (docs/DETERMINISM.md, "Browser and native"): the elementary
	/// functions of <see cref="Math"/>, <see cref="MathF"/> and the generic-math statics of double and float, which call the platform's C
	/// library (use <see cref="DetMath"/> and <see cref="DetMathF"/>); the System.Numerics builders that call them (rotations, quaternion
	/// slerp); <see cref="Guid.NewGuid"/>; and <c>Parallel</c> or PLINQ, whose order is not fixed. Square root, fused multiply-add, rounding
	/// and the other correctly rounded operations are allowed: IEEE 754 fixes their results.
	/// </summary>
	internal static readonly Regex ForbiddenPlatformMathInSimulation = new(
		@"\b(Math|MathF|double|float|Double|Single)\s*\.\s*(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Sinh|Cosh|Tanh|Asinh|Acosh|Atanh|SinCos|SinPi|CosPi|TanPi|SinCosPi|AsinPi|AcosPi|AtanPi|Atan2Pi|Exp|Exp2|Exp10|ExpM1|Exp2M1|Exp10M1|Log|Log2|Log10|LogP1|Log2P1|Log10P1|Pow|Cbrt|Hypot|RootN)\s*\("
		+ @"|\bQuaternion\s*\.\s*(Slerp|CreateFromYawPitchRoll|CreateFromAxisAngle)\b|\bMatrix(4x4|3x2)\s*\.\s*(CreateRotation[XYZ]?|CreateFromYawPitchRoll|CreateFromAxisAngle)\b"
		+ @"|\bGuid\s*\.\s*NewGuid\b|\bParallel\s*\.|\.\s*AsParallel\s*\(",
		RegexOptions.CultureInvariant );

	/// <summary>
	/// Presentation inside the simulation folders: whole files and folders that only draw, animate the camera or build meshes and textures.
	/// A single presentation line in a simulation file carries the marker <c>[DET:presentation]</c> with its reason instead.
	/// </summary>
	internal static readonly string[] PresentationMathFiles =
	{
		"World/Lobby/", "World/Particles/", "World/Evening/",
		"World/Advisor.cs", "World/AdvisorRig.cs", "World/Camera.cs", "World/FreeWalkCameraMode.cs", "World/GlobeCameraMode.cs", "World/LobbyCameraMode.cs",
		"World/ParkCameraMode.cs", "World/RideCameraMode.cs", "World/Sky.cs", "World/Sun.cs", "World/OriginalProjection.cs",
		"World/Coasters/CoasterCarBend.cs", "World/Coasters/CoasterCarRig.cs", "World/Coasters/CoasterToolPanel.cs", "World/Coasters/CoasterTrackMesh.cs",
		"World/Coasters/CoasterRenderer.cs", "World/Coasters/CoasterPreview.cs",
		"World/Guests/GuestRenderer.cs", "World/Guests/GuestSpriteAtlas.cs", "World/Guests/HeadPresentation.cs", "World/Guests/RideHeadRenderer.cs",
		"World/Guests/SpriteScale.cs", "World/Guests/BalloonRenderer.cs", "World/Guests/ThoughtRenderer.cs",
		"World/Original/BlueprintMarkers.cs", "World/Original/ProceduralTerrain.cs", "World/Original/TerrainListGate.cs", "World/Original/TerrainVisibleSpans.cs", "World/Pools/PoolTextures.cs", "World/Pools/PoolMeshBuilder.cs", "World/Pools/PoolRenderer.cs",
		"World/Pools/Slides/PoolSlideMesh.cs", "World/Tracks/TrackRenderer.cs", "World/Tours/TourRenderer.cs", "World/Staff/StaffRenderer.cs",
		"World/Winter/SnowCover.cs", "World/Winter/WinterAnchors.cs", "World/Winter/WarmItemRenderer.cs", "World/Umbrellas/UmbrellaRenderer.cs",
		"World/Weather/WeatherRenderer.cs",
	};

	private const string PresentationMarker = "[DET:presentation]";

	/// <summary>A temporary file name for an atomic write (the save files): never part of the park's state.</summary>
	private static readonly Regex TemporaryFileName = new( @"Guid\.NewGuid\(\)\.ToString\(\s*""N""\s*\)\s*\+\s*""\.tmp""", RegexOptions.CultureInvariant );

	internal static bool IsPresentationMath( string relativePath, string line )
	{
		var path = relativePath.Replace( '\\', '/' );
		return PresentationMathFiles.Any( entry => entry.EndsWith( '/' ) ? path.StartsWith( entry, StringComparison.Ordinal ) : path == entry )
			|| line.Contains( PresentationMarker, StringComparison.Ordinal )
			|| TemporaryFileName.IsMatch( line ) && !ForbiddenPlatformMathInSimulation.IsMatch( TemporaryFileName.Replace( line, "" ) );
	}

	/// <summary>
	/// The simulation (the VM, the scripts, the economy and every World folder) uses no runtime-dependent mathematics, so the browser and every
	/// desktop compute the same bits (docs/DETERMINISM.md, "Browser and native"); presentation files and marked lines are listed above.
	/// </summary>
	[TestMethod]
	public void SimulationCodeUsesNoPlatformMathGuidsOrParallelism()
	{
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "docs", "DETERMINISM.md" ) ) )
			root = root.Parent;
		if ( root == null )
			Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		var source = Path.Combine( root.FullName, "source", "OpenTPW" );
		var files = new[] { "VM", "Scripting", "Economy", "World" }.SelectMany( folder => Directory.EnumerateFiles( Path.Combine( source, folder ), "*.cs", SearchOption.AllDirectories ) ).ToList();
		Assert.IsTrue( files.Count > 300, $"{files.Count} files scanned" );
		foreach ( var entry in PresentationMathFiles )
			Assert.IsTrue( entry.EndsWith( '/' ) ? Directory.Exists( Path.Combine( source, entry ) ) : File.Exists( Path.Combine( source, entry ) ), $"{entry} is listed as presentation but does not exist" );
		var hits = files.SelectMany( path => File.ReadLines( path ).Select( ( line, index ) => (path, line, index) ) )
			.Where( item => ForbiddenPlatformMathInSimulation.IsMatch( StripComment( item.line ) ) && !IsPresentationMath( Path.GetRelativePath( source, item.path ), item.line ) )
			.Select( item => $"{Path.GetRelativePath( root.FullName, item.path )}:{item.index + 1}: {item.line.Trim()}" ).ToList();
		Assert.AreEqual( 0, hits.Count, "Use DetMath/DetMathF, or list presentation code:" + Environment.NewLine + string.Join( Environment.NewLine, hits ) );
	}

	/// <summary>The code of a line without a trailing // comment (a mention of Math.Sin in a comment is not a call).</summary>
	private static string StripComment( string line )
	{
		var index = line.IndexOf( "//", StringComparison.Ordinal );
		return index < 0 ? line : line[..index];
	}

	/// <summary>Each forbidden form is caught and the deterministic forms next to them are not (the guard's own mutation check).</summary>
	[TestMethod]
	public void PlatformMathGuardCatchesEachForbiddenForm()
	{
		var forbidden = new[]
		{
			"var s = Math.Sin( a );", "var c = MathF.Cos(a);", "var t = Math.Atan2( y, x );", "var a = MathF.Acos( d );", "var p = Math.Pow( b, e );",
			"var l = Math.Log2( r );", "var e = Math.Exp( -k );", "var (s, c) = MathF.SinCos( a );", "var s = double.Sin( a );", "var s = float.Atan2( y, x );",
			"var r = Math.Cbrt( v );", "var h = Math.Hypot( x, y );", "var m = Matrix4x4.CreateRotationY( a );", "var q = Quaternion.Slerp( a, b, t );",
			"var q = Quaternion.CreateFromYawPitchRoll( y, p, r );", "var id = Guid.NewGuid();", "Parallel.For( 0, n, Step );", "var all = items.AsParallel().ToList();",
			"var t = Math . Tan ( a );",
		};
		var allowed = new[]
		{
			"var s = DetMath.Sin( a );", "var c = DetMathF.Cos( a );", "var t = DetMath.Atan2( y, x );", "var r = Math.Sqrt( v );", "var r = MathF.Sqrt( v );",
			"var f = MathF.FusedMultiplyAdd( a, b, c );", "var n = Math.Round( v );", "var a = Math.Abs( v );", "var m = Math.Max( a, b );", "var p = Math.PI;",
			"var c = Math.Clamp( v, 0, 1 );", "var f = Math.Floor( v );", "var m = Matrix4x4.CreateTranslation( v );", "var s = Math.ScaleB( v, 3 );",
		};
		foreach ( var line in forbidden )
			Assert.IsTrue( ForbiddenPlatformMathInSimulation.IsMatch( line ), line );
		foreach ( var line in allowed )
			Assert.IsFalse( ForbiddenPlatformMathInSimulation.IsMatch( line ), line );
		Assert.IsTrue( IsPresentationMath( "World/Lobby/GlobeScene.cs", "var s = MathF.Sin( a );" ) );
		Assert.IsTrue( IsPresentationMath( "World/Tracks/TrackSurface.cs", "var p = MathF.Atan2( a, b ); // [DET:presentation] the drawn pose" ) );
		Assert.IsFalse( IsPresentationMath( "World/Tracks/TrackSurface.cs", "var p = MathF.Atan2( a, b );" ) );
		Assert.IsFalse( IsPresentationMath( "World/Lobby.cs", "var s = MathF.Sin( a );" ), "a folder entry is not a file prefix" );
		Assert.IsTrue( IsPresentationMath( "Economy/ParkSaveFile.cs", "var temporary = destination + \".\" + Guid.NewGuid().ToString( \"N\" ) + \".tmp\";" ) );
		Assert.IsFalse( IsPresentationMath( "Economy/ParkSaveFile.cs", "var id = Guid.NewGuid();" ) );
		Assert.IsFalse( IsPresentationMath( "Economy/ParkSaveFile.cs", "var name = Guid.NewGuid().ToString( \"N\" ) + \".tmp\" + Math.Sin( a );" ) );
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
