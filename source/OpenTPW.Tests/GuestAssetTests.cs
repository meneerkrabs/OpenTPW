using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Guest data from the original game (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
public class GuestAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original guest sprites, balance files and Jungle save." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "esprites.wad" ) ) || !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks esprites.wad or levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	[TestMethod]
	public void TheKidAndHeadFoldersLoadInTheOriginalsOrderUpToTheLimit()
	{
		// [BIN:TPW-EXE:0x764030 kid sets loaded first] BI, KI, TA, SU, then by name; the heads are the same eight names, so a guest's draw picks the matching head
		var kids = GuestSpriteAtlas.LoadKids().Select( kid => kid.Name ).ToArray();
		var heads = GuestSpriteAtlas.LoadKidHeads().Select( head => head.Name ).ToArray();
		CollectionAssert.AreEqual( kids, heads, "the two folders ship the same sets" );
		var all = new[] { "SPR_BI", "SPR_KI", "SPR_TA", "SPR_SU", "SPR_BE", "SPR_CH", "SPR_FR", "SPR_SA" };
		foreach ( var limit in new[] { 2, 4, 6, 8 } )
		{
			CollectionAssert.AreEqual( all.Take( limit ).ToArray(), KidSetLoad.Order( kids, name => name, limit ).ToArray(), $"kids at limit {limit}" );
			CollectionAssert.AreEqual( all.Take( limit ).ToArray(), KidSetLoad.Order( heads, name => name, limit ).ToArray(), $"heads at limit {limit}" );
		}
	}

	[TestMethod]
	public void KidSpriteSetsDecodeWithTheirAnimationTables()
	{
		var kids = GuestSpriteAtlas.LoadKids();
		CollectionAssert.AreEqual( new[] { "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" }, kids.Select( kid => kid.Name ).ToArray() );
		foreach ( var kid in kids )
		{
			Assert.AreEqual( 175, kid.Frames.Length, kid.Name );
			Assert.AreEqual( $"{kid.Name}.TPS", kid.Animations.BankName );
			var walk = kid.Animations.Animations[GuestSpriteAtlas.WalkSlot];
			Assert.AreEqual( (135, 8, 5), (walk.FirstFrame, walk.FramesPerDirection, walk.Directions), kid.Name );
			var used = kid.Animations.Animations.Where( slot => !slot.IsEmpty ).SelectMany( slot => Enumerable.Range( slot.FirstFrame, slot.FrameCount ) ).OrderBy( x => x ).ToArray();
			CollectionAssert.AreEqual( Enumerable.Range( 0, 175 ).ToArray(), used, $"{kid.Name}: the slots cover every frame exactly once" );
			var walkFrames = Enumerable.Range( walk.FirstFrame, walk.FrameCount ).Select( index => kid.Frames[index] );
			// the TPC is drawn from above: the nearer foot reaches 1 to 8 pixels below the hotspot (the FPC, from a lower camera, 0 to 3)
			Assert.IsTrue( walkFrames.All( frame => frame.OriginY < 0 && -frame.OriginY <= frame.Height && -frame.OriginY >= frame.Height - 8 ), "walk-frame hotspot at the feet" );
			Assert.IsTrue( kid.Pixels.Where( ( value, index ) => index % 4 == 3 ).Any( value => value == 255 ) );
		}
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 0, 1, 1, 1, 0 }, kids.Select( kid => (int)kid.Animations.FlagB ).ToArray() );
	}

	/// <summary>
	/// Honouring the pixels-per-unit fields changes nothing for the shipped art: every frame of every guest, head, costume, balloon and thought atlas has 128 in both
	/// (scale factor exactly 1, so the quads are the fixed-scale quads) and every atlas keeps its 1024-pixel width.
	/// </summary>
	[TestMethod]
	public void EveryShippedAtlasHasUnitScaleAndTheOriginalWidth()
	{
		var atlases = GuestSpriteAtlas.LoadKids().Concat( GuestSpriteAtlas.LoadKidHeads() ).Concat( GuestSpriteAtlas.LoadBalloons() ).Concat( GuestSpriteAtlas.LoadThoughts() )
			.Concat( new[] { "Jungle", "Hallow", "Space", "Fantasy" }.SelectMany( theme => GuestSpriteAtlas.LoadCostumes( theme ).Concat( GuestSpriteAtlas.LoadCostumeHeads( theme ) ) ) ).ToList();
		Assert.IsTrue( atlases.Count >= 20, $"{atlases.Count} atlases" );
		foreach ( var atlas in atlases )
		{
			Assert.AreEqual( GuestSpriteAtlas.AtlasWidth, atlas.Width, atlas.Name );
			Assert.IsTrue( atlas.Frames.All( frame => frame.ScaleX == 1f && frame.ScaleY == 1f ), atlas.Name );
		}
	}

	/// <summary>
	/// Every set is drawn from its TPC, as the game does (0x5410D3 opens the FPC only with the sprite-detail flag, which the PC never sets), and every
	/// ESP has one. The 27 sets with byte 0x10C = 1 ship an FPC too, as do Jungle SPR_EX and Space SPR_SK with 0. Every frame's two header words
	/// (pixels per sprite unit, 0x5890E0) are 128 in both banks.
	/// </summary>
	[TestMethod]
	public void EveryEspChoosesTheBankTheGameOpens()
	{
		var full = new List<string>();
		var partialWithFpc = new List<string>();
		var checkedSets = 0;
		var entertainerBanks = 0;
		foreach ( var directory in Directories( "/esprites" ) )
		{
			var files = FileSystem.GetFiles( directory ).Select( file => Path.GetFileName( file ) ).ToArray();
			foreach ( var esp in files.Where( file => file.EndsWith( ".ESP", StringComparison.OrdinalIgnoreCase ) ) )
			{
				var stem = Path.GetFileNameWithoutExtension( esp );
				bool Has( string extension ) => files.Any( file => string.Equals( file, stem + extension, StringComparison.OrdinalIgnoreCase ) );
				using var stream = FileSystem.OpenRead( $"{directory}/{esp}" );
				var animations = new SpriteAnimationFile( stream );
				var name = $"{directory}/{stem}";
				// The performance entries (0x540D90): exactly the entertainers' banks have one, program 0 on slot 4.
				if ( directory.EndsWith( "/Entertainers", StringComparison.OrdinalIgnoreCase ) )
				{
					entertainerBanks++;
					Assert.AreEqual( 1, animations.PerformanceCount, name );
					Assert.AreEqual( (0, 4), (animations.Performances[0].Program, animations.Performances[0].Slot), name );
					Assert.IsTrue( animations.Animations[4].FramesPerDirection > 1, $"{name}: the performance slot has frames" );
				}
				else
					Assert.AreEqual( 0, animations.PerformanceCount, name );
				if ( animations.FlagA != 0 )
				{
					full.Add( name );
					Assert.IsTrue( Has( ".FPC" ), name );
				}
				else if ( Has( ".FPC" ) )
					partialWithFpc.Add( name );
				Assert.AreEqual( ".TPC", animations.BankExtension, name );
				Assert.IsTrue( Has( ".TPC" ), $"{name}: the bank the game draws" );
				checkedSets++;
				foreach ( var extension in new[] { ".TPC", ".FPC" }.Where( Has ) )
				{
					using var bank = FileSystem.OpenRead( $"{directory}/{stem}{extension}" );
					Assert.IsTrue( new SpriteBankFile( bank ).Frames.All( frame => frame.PixelsPerUnitX == 128 && frame.PixelsPerUnitY == 128 ), name + extension );
				}
				Assert.AreEqual( 16, animations.ExtraSlotBytes.Length );
			}
		}
		Assert.AreEqual( 27, full.Count, string.Join( ", ", full ) );
		CollectionAssert.AreEquivalent( new[] { "SPR_EX", "SPR_SK" }, partialWithFpc.Select( name => name[(name.LastIndexOf( '/' ) + 1)..].ToUpperInvariant() ).ToArray() );
		Assert.AreEqual( 46, checkedSets, "every ESP of esprites.wad" );
		Assert.AreEqual( 12, entertainerBanks, "four themes, three entertainers each" );
	}

	private static IEnumerable<string> Directories( string root )
	{
		yield return root;
		foreach ( var child in FileSystem.GetDirectories( root ) )
			foreach ( var nested in Directories( child.StartsWith( '/' ) ? child : $"{root}/{Path.GetFileName( child )}" ) )
				yield return nested;
	}

	[TestMethod]
	public void ThemeBalanceFileGivesGuestSettings()
	{
		var settings = GuestSettings.Load( "jungle" );
		Assert.AreEqual( 8, settings.Types.Count );
		Assert.IsTrue( settings.Types.All( type => type.PreferredExcitement is > 0 and <= 100 && type.StartingCash > 0 && type.BoredomThreshold > 0 ) );
		Assert.IsTrue( settings.ExitLevelSeconds > settings.ExitLevelVarSeconds && settings.ExitLevelVarSeconds > 0 );
		Assert.IsTrue( settings.PerfectRide > settings.GoodRide && settings.GoodRide > settings.OkRide );
		Assert.IsNotNull( settings.ArrivalLaneA );
		Assert.IsNotNull( settings.ArrivalLaneB );
		Assert.AreEqual( 5, settings.ArrivalLaneA!.Length );
	}

	/// <summary>
	/// The attraction score's inputs from the shipped data (ORACLE.md F24): every theme's PeepInfo has the weights 1, 1, 1, 2, 2, 2, 2 and the decision
	/// variables 7, 5, 5; every choosable object's thirst and hunger effects stay in 0..100, so the thirst and hunger matches never index past the
	/// original's 11 × 11 table; and the golden-ticket and upgrade costs feed the multipliers.
	/// </summary>
	[TestMethod]
	public void ShippedDataFeedsTheAttractionScoreInsideItsTables()
	{
		var golden = 0;
		var costly = 0;
		var indoors = 0;
		var inspected = 0;
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var settings = GuestSettings.Load( theme );
			Assert.AreEqual( DecisionWeights.Shipped, settings.DecisionWeights, theme );
			Assert.AreEqual( DecisionVariables.Shipped, settings.DecisionVariables, theme );
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( entry => entry.IsChoosable ) )
			{
				var bridge = OriginalObjectRuntime.CreateVisitorBridge( entry, 1 );
				Assert.IsTrue( bridge.ThirstEffect is >= 0 and <= 100, $"{theme} {entry.DisplayName} ThirstEffect {bridge.ThirstEffect}" );
				Assert.IsTrue( bridge.HungerEffect is >= 0 and <= 100, $"{theme} {entry.DisplayName} HungerEffect {bridge.HungerEffect}" );
				Assert.IsTrue( bridge.GoldenTicketCost >= 0 && bridge.UpgradeCost >= 0, $"{theme} {entry.DisplayName}" );
				Assert.AreEqual( entry.InfoId, bridge.TypeId );
				golden += bridge.GoldenTicketCost > 0 ? 1 : 0;
				costly += bridge.UpgradeCost > 3000 ? 1 : 0;
				indoors += bridge.IsIndoors ? 1 : 0;
				inspected++;
			}
		}
		Assert.IsTrue( inspected > 50, $"{inspected} choosable objects" );
		Console.WriteLine( $"{inspected} choosable objects: {golden} with a golden-ticket cost, {costly} with a first upgrade above 3000, {indoors} indoors" );
	}

	/// <summary>
	/// The constructor's starting values from the shipped balance (ORACLE.md F25): ExitLevel 120 ± 60 gives a counter of 60..179 (about two minutes, at one count per
	/// 0.992 s), cash within ±15 % of each of the eight types' StartingCash, thirst and hunger below 50, toilet below 30; and the shipped use effects keep
	/// the meters' clamps meaningful (no effect above 100).
	/// </summary>
	[TestMethod]
	public void ShippedBalanceGivesTheOriginalsStartingValuesAndEffectsStayBelowTheMeterRange()
	{
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var settings = GuestSettings.Load( theme );
			Assert.AreEqual( 120, settings.ExitLevelSeconds );
			Assert.AreEqual( 60, settings.ExitLevelVarSeconds );
			Assert.AreEqual( 15, settings.StartingCashVarPercent );
			var random = new WorldRng( 99 );
			var types = new HashSet<int>();
			for ( var count = 0; count < 4000; count++ )
			{
				var start = GuestStart.FromDraws( settings.Types, settings.ExitLevelSeconds, settings.ExitLevelVarSeconds, settings.StartingCashVarPercent, () => random.NextWord() );
				var cash = settings.Types[start.Type].StartingCash;
				Assert.IsTrue( start.ExitLevel is >= 60 and <= 179, $"{theme} exit {start.ExitLevel}" );
				Assert.IsTrue( start.Money >= cash * 85 / 100 && start.Money <= cash * 115 / 100, $"{theme} money {start.Money} of {cash}" );
				Assert.IsTrue( start.Thirst < 50 && start.Hunger < 50 && start.Toilet < 30 && start.Happiness == 50 );
				types.Add( start.Type );
			}
			Assert.AreEqual( settings.Types.Count, types.Count, "every type is drawn" );
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( entry => entry.IsChoosable ) )
			{
				var bridge = OriginalObjectRuntime.CreateVisitorBridge( entry, 1 );
				Assert.IsTrue( bridge.VomitEffect is >= 0 and <= 100 && bridge.HappinessEffect is >= 0 and <= 100, $"{theme} {entry.DisplayName}" );
			}
		}
	}

	/// <summary>
	/// The lifecycle with the shipped balance on the Easymode park (ORACLE.md F26): guests that only walk (no attractions) head home when their exit counter reads 0, one count per
	/// 0.992 s, so each leaves after about its starting counter in seconds (60..179 counts: one to three minutes), losing BigHappinessChange on the way. The test runs in state 6
	/// only (docs/reverse/WANDER-plan.md §2) and the counter reads 0 for four park turns, so a guest that spends all four walking to a wander destination or in an activity keeps
	/// a negative counter and stays.
	/// </summary>
	[TestMethod]
	public void ShippedGuestsWalkingAloneLeaveWhenTheirCounterReadsZero()
	{
		var (grid, settings) = LoadJungle();
		// OpenTPW's own boredom loss (GUEST-009) would drain a guest that never rides within a minute; it is switched off so that only the counter is measured
		settings.Types = settings.Types.Select( type => new GuestType( type.PreferredExcitement, type.StartingCash, 1_000_000 ) ).ToList();
		// and so is the small loss of every major decision that finds no attraction (state 6's "can't decide"), which in a park without rides would empty happiness first
		settings.SmallHappinessChange = 0;
		var simulation = new GuestSimulation( grid, settings, 41 ) { ArrivalsEnabled = false };
		var entrance = settings.ArrivalLaneA![^1];
		var guests = Enumerable.Range( 0, 30 ).Select( _ => simulation.SpawnInPark( entrance.X, entrance.Y ) ).ToList();
		var start = guests.ToDictionary( guest => guest.Id, guest => guest.ExitLevel );
		var leftAt = new Dictionary<int, double>();
		var happinessAtLeaving = new Dictionary<int, float>();
		var counterAtLeaving = new Dictionary<int, int>();
		for ( var tick = 0; tick < 200 * 60; tick++ )
		{
			simulation.Tick( Tick );
			foreach ( var guest in guests.Where( guest => !leftAt.ContainsKey( guest.Id ) && guest.State is GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.Gone ) )
			{
				leftAt[guest.Id] = simulation.TimeSeconds;
				happinessAtLeaving[guest.Id] = guest.Happiness;
				counterAtLeaving[guest.Id] = guest.ExitLevel;
			}
		}
		// Guests spend about three quarters of their time walking to wander destinations, so most miss the window; a fifth to a half leave by the counter
		// (the share moves with the run's draws). A guest whose needs
		// reach 100 loses happiness every needs phase (NeedsUpdate) and may leave earlier on a zero happiness byte.
		var byCounter = leftAt.Keys.Where( id => counterAtLeaving[id] == 0 ).ToList();
		Assert.IsTrue( byCounter.Count >= 5, $"{byCounter.Count} of 30 walking guests went home on their counter within 200 s" );
		foreach ( var guest in guests.Where( guest => !leftAt.ContainsKey( guest.Id ) ) )
			Assert.IsTrue( guest.ExitLevel < 0 && guest.State is GuestState.DoingNothing or GuestState.WalkingAround, $"guest {guest.Id} missed its zero counter and stays: {guest.ExitLevel}, {guest.State}" );
		foreach ( var guest in guests.Where( guest => leftAt.ContainsKey( guest.Id ) ) )
		{
			if ( counterAtLeaving[guest.Id] != 0 )
			{
				Assert.AreEqual( 0f, happinessAtLeaving[guest.Id], $"guest {guest.Id} left at counter {counterAtLeaving[guest.Id]}, so on a zero happiness byte" );
				continue;
			}
			var expected = start[guest.Id] * ParkCalendar.TurnMilliseconds * 4 / 1000.0;
			Assert.AreEqual( expected, leftAt[guest.Id], 2.5, $"guest {guest.Id} started at {start[guest.Id]} counts" );
			Assert.IsTrue( happinessAtLeaving[guest.Id] is >= 0 and <= 50 - 25 + 0.001f, $"guest {guest.Id} left at happiness {happinessAtLeaving[guest.Id]}: 50 less the {settings.BigHappinessChange} of the way out, less any full-need points" );
		}
		Assert.IsTrue( byCounter.Min( id => leftAt[id] ) >= 55 && byCounter.Max( id => leftAt[id] ) <= 180, $"{byCounter.Min( id => leftAt[id] ):F0}..{byCounter.Max( id => leftAt[id] ):F0} s" );
	}

	private static (GuestPathGrid Grid, GuestSettings Settings) LoadJungle()
	{
		var park = OriginalPark.Load( "jungle" );
		if ( park.Save == null )
			Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
		return (GuestPathGrid.FromOriginal( park.Map, park.Save ), GuestSettings.Load( "jungle" ));
	}

	[TestMethod]
	public void JunglePathsFormOneNetworkFromTheEntrances()
	{
		var (grid, settings) = LoadJungle();
		Assert.AreEqual( 78, grid.WalkableCount );
		var entrance = settings.ArrivalLaneA![^1];
		Assert.IsTrue( grid.IsWalkable( entrance.X, entrance.Y ), "Standard.sam EntranceA is an imported path cell" );
		var field = grid.GetFlowField( entrance.X, entrance.Y );
		var reachable = field.Count( distance => distance >= 0 );
		Assert.AreEqual( 78, reachable, "every Easymode path cell is reachable from the entrance" );
	}

	/// <summary>
	/// Real guests ride the original Totem.RSE headlessly (no GPU models): it fills before its 10 s time-out and releases
	/// its riders. The Totem object's animation channels answer the animation opcodes; a script without an object would
	/// wait INT32_MAX ms on its first WAITANIM, as in the original.
	/// </summary>
	[TestMethod]
	public void GuestsRideTheOriginalTotemScript()
	{
		var (grid, settings) = LoadJungle();
		var simulation = new GuestSimulation( grid, settings, 99 ) { ArrivalsEnabled = false };
		var bridge = new RideVisitorBridge( 1, "Inca Totem", RideVisitorKind.Ride, 6, 70, 25 ) { EntranceCell = (39, 21), ExitCell = (39, 21) };
		// Only the object's channels are used; its own copy of the script is never advanced.
		var totem = new OriginalObjectRuntime( ObjectCatalog.Load( "jungle" ).Entries.Single( entry => entry.ScriptPath != null && entry.ScriptPath.EndsWith( "Totem.RSE", StringComparison.OrdinalIgnoreCase ) ) );
		RideVM vm;
		using ( var stream = FileSystem.OpenRead( "/levels/jungle/rides/totem/Totem.RSE" ) )
			vm = new RideVM( stream, new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, new OriginalObjectEffects( totem ) ), Seed = 4 } );
		vm[RideVariables.VAR_CAPACITY] = 6;
		vm[RideVariables.VAR_RIDECLOSED] = 0;
		bridge.Attach( vm, () => vm[RideVariables.VAR_RIDECLOSED] == 0 );
		simulation.Register( bridge );
		GuestTests.LayStandCell( simulation.Grid, bridge );
		// sixteen guests on the paths round the entrance (eight cells, two each): which of them choose the ride at once follows the world generator's draws, and a full load of six needs a crowd
		foreach ( var cell in new[] { (39, 22), (39, 23), (40, 21), (41, 21), (39, 24), (42, 21), (39, 25), (43, 21), (39, 22), (39, 23), (40, 21), (41, 21), (39, 24), (42, 21), (39, 25), (43, 21) } )
			simulation.SpawnInPark( cell.Item1, cell.Item2 );
		var runs = new List<(double At, int Riders)>();
		var running = false;
		// The 10 s passenger time-out starts after the opening WAITANIM, which plays the Totem's AnimID 0 clip.
		double? openedAt = null;
		var waitedForTheClip = false;
		vm.InstructionExecuting = ( script, instruction ) =>
		{
			if ( waitedForTheClip )
				openedAt ??= script.TimeMilliseconds;
			waitedForTheClip |= instruction.Opcode == Opcode.WAITANIM;
		};
		var maximumRiders = 0;
		for ( var tick = 0; tick < 120 * 60; tick++ )
		{
			simulation.Tick( Tick );
			bridge.HostStep();
			vm.Advance( Tick );
			bridge.WalkUpdate();
			totem.Animator.Advance( Tick );
			maximumRiders = Math.Max( maximumRiders, vm[RideVariables.VAR_ONRIDE] );
			if ( vm[RideVariables.VAR_RUNNING] == 1 && !running )
				runs.Add( (vm.TimeMilliseconds, vm[RideVariables.VAR_ONRIDE]) );
			running = vm[RideVariables.VAR_RUNNING] == 1;
		}
		Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
		Assert.AreEqual( 6, maximumRiders, "Totem.RSE boarded a full load through HUSH/WALKON" );
		Assert.IsTrue( runs.Count > 0 );
		Assert.IsNotNull( openedAt );
		// A run starts with a full load. The first may start on the 10 s passenger time-out that starts after the opening clip: guests
		// make their first major decision only after park turn 30 (state 6: T > +0x1FC + 30, about 7.4 s) and on one turn in three, and
		// each called guest of this one-cell queue walks into the entrance cell to the traced stand point (about 8 park turns, QUEUE-007
		// closed) before the next is called.
		Assert.IsTrue( runs.Any( run => run.Riders == 6 ), string.Join( ", ", runs.Select( run => $"{run.Riders} riders at {run.At:F0} ms" ) ) );
		Assert.IsTrue( bridge.ReleasedTotal >= 6, "HOP/WALKOFF/WALKGET → VAR_LETMEOFF released the riders" );
		Assert.IsTrue( simulation.Guests.Count( guest => guest.RidesTaken > 0 ) >= 6 );
		Assert.IsTrue( simulation.Guests.All( guest => guest.State != GuestState.Using || bridge.Riders.Contains( guest.Id ) ) );
	}

	/// <summary>
	/// The join excitement of shipped objects (ORACLE.md F16): rides run at their design speed and duration, so their live excitement is
	/// their level, except Brain Buster whose InitDuration 3 lies under its MinDuration 10 (ratio 10 / 3 clamps to 1.25); sideshows score
	/// 20 + 0.8 × win × sqrt(goods − price) / 10 from their goods, price and chance of losing.
	/// </summary>
	[TestMethod]
	public void ShippedObjectsScoreTheirLiveJoinExcitement()
	{
		RideVisitorBridge Bridge( string theme, string name ) =>
			OriginalObjectRuntime.CreateVisitorBridge( ObjectCatalog.Load( theme ).Entries.First( entry => entry.DisplayName == name ), 1 );
		Assert.AreEqual( 40, Bridge( "jungle", "Belly Bounce" ).LiveExcitement, "level 40 at the design speed and duration" );
		Assert.AreEqual( 50, Bridge( "hallow", "Brain Buster" ).LiveExcitement, "level 40 × 1.25" );
		Assert.AreEqual( 27, Bridge( "jungle", "Arcade" ).LiveExcitement, "20 + trunc(0.8 × 25 × sqrt(15) / 10): goods 25, price 10, chance of losing 75" );
		Assert.AreEqual( 33, Bridge( "hallow", "Arcade" ).LiveExcitement, "goods 50, price 20, chance of losing 70" );
		Assert.AreEqual( 37, Bridge( "space", "Arcade" ).LiveExcitement, "goods 100, price 20, chance of losing 75" );
		var scaled = new List<string>();
		foreach ( var theme in ObjectCatalog.Themes )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( entry => entry.Category == ObjectCategory.Ride && entry.Settings.GetInt( "Bumper.WhichTrackType" ) != 3 && entry.Settings.Has( "UsageInfo.ExcitementLevel" ) ) )
			{
				var bridge = OriginalObjectRuntime.CreateVisitorBridge( entry, 1 );
				if ( bridge.LiveExcitement != bridge.StaticExcitementLevel )
					scaled.Add( $"{theme} {entry.DisplayName} {bridge.StaticExcitementLevel}->{bridge.LiveExcitement} duration {bridge.Parameters.Duration}/{bridge.Parameters.InitDuration}" );
			}
		// The only rides whose duration byte (the data clamped to MinDuration..MaxDuration) differs from the raw InitDuration of the level.
		CollectionAssert.AreEqual( new[]
		{
			"hallow Brain Buster 40->50 duration 10/3", "space Zero G 65->81 duration 15/3",
			"fantasy Bumper Bugs 70->87 duration 10/3", "fantasy Jelly Bounce 40->50 duration 10/3"
		}, scaled.ToArray(), string.Join( "; ", scaled ) );
	}

	/// <summary>
	/// The price test's per-type word (tp.exe 0x004D3E00 copies UsageInfo.RipOffOK, type +0x16C, into the record 0x004E2740 returns): 100 for every
	/// shipped shop (Shops.sam) and 250 for every sideshow (SideShow.sam, "%premium peeps willing to pay above 'average win'").
	/// </summary>
	[TestMethod]
	public void ShippedShopsAndSideshowsCarryTheirRipOffOk()
	{
		var words = new SortedSet<string>();
		foreach ( var theme in ObjectCatalog.Themes )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				var bridge = OriginalObjectRuntime.CreateVisitorBridge( entry, 1 );
				if ( bridge.Kind is RideVisitorKind.Shop or RideVisitorKind.Sideshow )
					words.Add( $"{bridge.Kind} {bridge.RipOffOk}" );
			}
		CollectionAssert.AreEqual( new[] { "Shop 100", "Sideshow 250" }, words.ToArray(), string.Join( "; ", words ) );
	}

	/// <summary>
	/// The special ingredient of shipped objects (<c>UsageInfo.SpecialIngredient</c>, type +344): only the Ice Cream Shop of every theme has Sugar (4),
	/// the one ingredient that raises the guests' speed boost (ORACLE.md F20); the burger, fries and drinks shops carry Fat, Salt and Ice.
	/// </summary>
	[TestMethod]
	public void OnlyTheIceCreamShopsCarrySugar()
	{
		var ingredients = new SortedSet<string>();
		foreach ( var theme in ObjectCatalog.Themes )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				var bridge = OriginalObjectRuntime.CreateVisitorBridge( entry, 1 );
				if ( bridge.SpecialIngredient != 0 )
					ingredients.Add( $"{bridge.SpecialIngredient} {entry.DisplayName}" );
				Assert.AreEqual( GuestSpeed.DefaultIngredientAmount, bridge.IngredientAmount, $"{theme} {entry.DisplayName}" );
			}
		CollectionAssert.AreEqual( new[] { "1 Burger Shop", "2 Fries Shop", "3 Drinks Shop", "4 Ice Cream Shop" }, ingredients.ToArray(), string.Join( "; ", ingredients ) );
		var iceCream = OriginalObjectRuntime.CreateVisitorBridge( ObjectCatalog.Load( "jungle" ).Entries.First( entry => entry.DisplayName == "Ice Cream Shop" ), 1 );
		var (grid, settings) = LoadJungle();
		var simulation = new GuestSimulation( grid, settings, 4 ) { ArrivalsEnabled = false };
		var entrance = settings.ArrivalLaneA![^1];
		var guest = simulation.SpawnInPark( entrance.X, entrance.Y );
		( (IRideVisitorHost)simulation ).OnVisitorReleased( iceCream, guest.Id );
		Assert.AreEqual( (ushort)3, guest.Boost, "an ice cream at the default amount 50: trunc(6 x 50 / 100)" );
	}

	/// <summary>
	/// The balloon and costume art (GUEST-017): one balloon set with four colours of a whole and a burst frame; one costume set per theme with the
	/// kids' animation slots; costume heads in Jungle and Space only. Each atlas has its own texture pack key.
	/// </summary>
	[TestMethod]
	public void BalloonAndCostumeSpritesLoad()
	{
		var balloons = GuestSpriteAtlas.LoadBalloons();
		Assert.AreEqual( "SPR_BL", balloons.Single().Name );
		var slots = balloons[0].Animations.Animations.Where( slot => !slot.IsEmpty ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, slots.Select( slot => slot.Slot ).ToArray() );
		Assert.IsTrue( slots.All( slot => slot.FramesPerDirection == 2 && slot.Directions == 0 ), "a whole and a burst frame per colour" );
		Assert.IsTrue( balloons[0].Frames.All( frame => frame.OriginY < -70 ), "the balloon floats above its anchor" );
		var keys = new HashSet<string> { balloons[0].PackKey };
		foreach ( var (theme, set, heads) in new[] { ("jungle", "SPR_TI", 1), ("fantasy", "SPR_BK", 0), ("hallow", "SPR_HK", 0), ("space", "SPR_SK", 1) } )
		{
			var costume = GuestSpriteAtlas.LoadCostumes( theme ).Single();
			Assert.AreEqual( set, costume.Name, theme );
			Assert.AreEqual( 8, costume.Animations.Animations.Count( slot => !slot.IsEmpty ), $"{theme}: the kids' slots" );
			Assert.IsTrue( costume.FramesPerDirection( GuestSpriteAtlas.WalkSlot ) == 8, theme );
			Assert.IsTrue( keys.Add( costume.PackKey ), costume.PackKey );
			StringAssert.StartsWith( costume.PackKey, $"esprites/{theme}/costumes/" );
			Assert.AreEqual( heads, GuestSpriteAtlas.LoadCostumeHeads( theme ).Count, $"{theme} costume heads" );
		}
	}

	/// <summary>The thought bubbles (GUEST-006): <c>SPR_TB</c> with one frame per slot for the guests' sixteen, then <c>SPR_TC</c> with the staff's.</summary>
	[TestMethod]
	public void ThoughtBubblesLoadInCategoryOrder()
	{
		var banks = GuestSpriteAtlas.LoadThoughts();
		CollectionAssert.AreEqual( new[] { "SPR_TB", "SPR_TC" }, banks.Select( bank => bank.Name ).ToArray() );
		Assert.AreEqual( 16, banks[0].Frames.Length );
		Assert.AreEqual( 10, banks[1].Frames.Length );
		for ( var thought = 1; thought <= 22; thought++ )
		{
			var slot = ThoughtRenderer.Slot( thought );
			var bank = banks[slot < ThoughtRenderer.FirstBankSlots ? 0 : 1];
			Assert.IsFalse( bank.Animations.Animations[slot % ThoughtRenderer.FirstBankSlots].IsEmpty, $"thought {thought} slot {slot}" );
		}
		Assert.AreEqual( 8, banks[1].GetFrame( ThoughtRenderer.Slot( 20 ) - 16, 0, 0 ), "the tired staff's sleeping bubble is SPR_TC frame 8" );
		Assert.AreEqual( 2, banks[1].GetFrame( ThoughtRenderer.Slot( 5 ) - 16, 0, 0 ), "Pleased: the thumb up, SPR_TC frame 2" );
	}

	/// <summary>
	/// Every theme's shops: only the balloon shops (ShopType 6) carry <c>UsageInfo.AppearanceEffect</c> 1 and only the costume shops (ShopType 5) carry 2
	/// (ECON-044, tp.exe <c>0x004FE1E0</c>).
	/// </summary>
	[TestMethod]
	public void OnlyBalloonAndCostumeShopsChangeTheGuestsLook()
	{
		int balloons = 0, costumes = 0;
		foreach ( var theme in ObjectCatalog.Themes )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				var shopType = entry.Settings.GetInt( "UsageInfo.ShopType" );
				var expected = shopType == 6 ? GuestAppearance.Balloon : shopType == 5 ? GuestAppearance.Costume : 0;
				Assert.AreEqual( expected, OriginalObjectRuntime.CreateVisitorBridge( entry, 1 ).AppearanceEffect, $"{theme} {entry.DisplayName} (ShopType {shopType})" );
				balloons += expected == GuestAppearance.Balloon ? 1 : 0;
				costumes += expected == GuestAppearance.Costume ? 1 : 0;
			}
		Assert.IsTrue( balloons > 0 && costumes > 0, $"{balloons} balloon and {costumes} costume shops" );
	}

	/// <summary>
	/// The original Belly Bounce (Bouncy.RSE) with a queue path laid beside the Easymode paths: guests walk the
	/// queue cells, stand four to a cell, are called forward through the traced gates and ride.
	/// </summary>
	[TestMethod]
	public void GuestsWalkABuiltQueuePathToTheBellyBounce()
	{
		var (grid, settings) = LoadJungle();
		var entry = ObjectCatalog.Load( "jungle" ).Find( 1100 );
		Assert.IsNotNull( entry, "Belly Bounce (Info.Id 1100)" );
		var parameters = OriginalObjectRuntime.CreateQueueParameters( entry );
		// [DATA:jungle Bouncy.sam + Rides.sam] HasQueue, RunsContinuously, CAP 5, DUR 30, InitSpeed 60, QWTC 130 (QUEUE-plan §9).
		Assert.AreEqual( new QueueParameters( true, true, false, 130, 60, 60, 5, 30, 30 ), parameters );
		Assert.AreEqual( 21u, RideVisitorBridge.ComputeQueueLimit( parameters ), "HasQueue: trunc(130 × (1 × 5) / 30), the QWTC formula of 0xdcb74" );

		// A straight run of four free cells off an Easymode path cell: the ride entrance sits beyond the fourth.
		var site = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) )
			.Where( cell => grid.IsWalkable( cell.X, cell.Y ) )
			.SelectMany( cell => Enumerable.Range( 0, 4 ).Select( direction => (Path: cell, Direction: direction) ) )
			.First( candidate => Enumerable.Range( 1, 5 ).All( step =>
			{
				var (dx, dy) = GuestPathGrid.Directions[candidate.Direction];
				var (x, y) = (candidate.Path.X + dx * step, candidate.Path.Y + dy * step);
				return grid.InBounds( x, y ) && !grid.IsWalkable( x, y ) && GuestPathGrid.Directions.Count( d => grid.IsWalkable( x + d.DX, y + d.DY ) ) == (step == 1 ? 1 : 0);
			} ) );
		var (sdx, sdy) = GuestPathGrid.Directions[site.Direction];
		var queue = Enumerable.Range( 1, 4 ).Select( step => (X: site.Path.X + sdx * step, Y: site.Path.Y + sdy * step) ).Reverse().ToArray();

		var runtime = new OriginalObjectRuntime( entry, seed: 3, open: true );
		var ride = runtime.Visitors;
		Assert.AreEqual( parameters, ride.Parameters );
		ride.EntranceCell = site.Path;
		ride.ExitCell = site.Path;
		ride.QueueFrontCell = queue[0];
		ride.QueueEntranceDirection = site.Direction;
		var simulation = new GuestSimulation( grid, settings, 21 ) { ArrivalsEnabled = false };
		simulation.Register( ride );
		Assert.AreEqual( 4, QueuePaths.TryExtend( grid, ride, queue ) );
		QueuePaths.TryConnect( grid, ride );
		GuestTests.LayStandCell( grid, ride );
		CollectionAssert.AreEqual( queue, ride.QueueCells.ToArray() );
		Assert.AreEqual( site.Path, ride.JoinCell );
		Assert.AreEqual( 16, ride.MaximumQueueLength, "min(21, 4 × 4 cells)" );

		var guests = Enumerable.Range( 0, 24 ).Select( _ => simulation.SpawnInPark( site.Path.X, site.Path.Y ) ).ToList();
		foreach ( var guest in guests )
		{
			guest.AttractionId = ride.AttractionId;
			guest.State = GuestState.GoingToRide;
		}
		var waits = new List<long>();
		simulation.QueueWaitCompleted += ( _, _, turns ) => waits.Add( turns );
		var queueCellsStoodOn = new HashSet<(int, int)>();
		var maximumQueue = 0;
		for ( var tick = 0; tick < 180 * 60; tick++ )
		{
			simulation.Tick( Tick );
			runtime.Simulate( Tick );
			maximumQueue = Math.Max( maximumQueue, ride.QueueLength );
			foreach ( var guest in simulation.Guests.Where( guest => guest.State == GuestState.Queueing ) )
			{
				// the traced arrival ends within 0.32 cell of the slot (QueueTests), so a guest in the first slot of the front cell may stand a hair past its edge
				var distance = MathF.Sqrt( (guest.X - guest.QueueTargetX) * (guest.X - guest.QueueTargetX) + (guest.Y - guest.QueueTargetY) * (guest.Y - guest.QueueTargetY) );
				Assert.IsTrue( grid.IsQueue( guest.Cell.X, guest.Cell.Y ) || distance < (GuestNavigation.ArrivalRadius + guest.MaxSpeed) / (float)Fixed16.One,
					$"guest {guest.Id} stands on a queue cell or at its slot, not at {guest.Cell} ({distance} cell from it)" );
				if ( grid.IsQueue( guest.Cell.X, guest.Cell.Y ) )
					queueCellsStoodOn.Add( guest.Cell );
			}
		}
		Console.WriteLine( $"Belly Bounce queue {string.Join( " ", queue )}: boarded {ride.BoardedTotal}, released {ride.ReleasedTotal}, max queue {maximumQueue}, "
			+ $"waits {waits.DefaultIfEmpty().Min()}..{waits.DefaultIfEmpty().Max()} park turns (mean {(waits.Count == 0 ? 0 : waits.Average()).ToString( "F1", System.Globalization.CultureInfo.InvariantCulture )})" );
		Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, runtime.Script.FaultMessage );
		Assert.IsTrue( queueCellsStoodOn.SetEquals( queue ), $"guests stood on every queue cell: {string.Join( " ", queueCellsStoodOn )}" );
		Assert.AreEqual( 16, maximumQueue, "the queue filled to 4 × cells" );
		Assert.IsTrue( ride.BoardedTotal >= 10 && ride.ReleasedTotal >= 5, $"boarded {ride.BoardedTotal}, released {ride.ReleasedTotal}" );
		Assert.AreEqual( ride.BoardedTotal, waits.Count, "every boarding guest came through the queue" );
		Console.WriteLine( $"Admission progress: longest head-not-ready streak {ride.MaximumHeadNotReadyStreak} evaluations, longest called-not-boarded age {ride.MaximumCalledAgeTurns} park turns" );
		runtime.Stop();
	}

	[TestMethod]
	public void JungleParkRunIsDeterministic()
	{
		var (grid, settings) = LoadJungle();
		// Groups every 15 timer units rather than the shipped 150 (about 2.5 minutes), so the two-minute run admits guests.
		settings.ArrivalTimeBetween = 15;
		ulong Run( ulong seed )
		{
			var simulation = new GuestSimulation( grid, settings, seed );
			for ( var tick = 0; tick < 120 * 60; tick++ )
				simulation.Tick( Tick );
			Assert.IsTrue( simulation.Admissions > 0 && simulation.Guests.Count > 20 );
			return simulation.ComputeStateHash();
		}
		Assert.AreEqual( Run( 1 ), Run( 1 ) );
		Assert.AreNotEqual( Run( 1 ), Run( 2 ) );
	}
}
