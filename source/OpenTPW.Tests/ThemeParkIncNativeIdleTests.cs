using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's native guest states, phase P2 (docs/reverse/TPI-GUESTS.md): idle (state 6, 0x614A40) with its spot animations, vomit, litter urge, leave gate, show and entertainer turns, prank
/// gate and switch and the four-way decision; the wander (state 7) and the walk to a litter bin (state 9); the removal message. The handlers run directly on a rig with the world generator seeded so
/// that draw A (and the draws after it) take the branch under test.
/// </summary>
[TestClass]
public sealed class ThemeParkIncNativeIdleTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	/// <summary>The park as the native guests read it: litter bins, shows, entertainers and the customers the bins counted.</summary>
	private sealed class World : IThemeParkIncGuestWorld
	{
		public readonly List<ThemeParkIncThingView> BinList = new();
		public readonly List<ThemeParkIncThingView> ShowList = new();
		public readonly List<ThemeParkIncEntertainerView> Performers = new();
		public readonly List<ushort> Customers = new();
		public IReadOnlyList<ThemeParkIncThingView> Bins() => BinList;
		public IReadOnlyList<ThemeParkIncThingView> Shows() => ShowList;
		public IReadOnlyList<ThemeParkIncEntertainerView> Entertainers() => Performers;
		public ThemeParkIncThingView? Thing( ushort thingId ) => BinList.Where( bin => bin.ThingId == thingId ).Cast<ThemeParkIncThingView?>().FirstOrDefault();
		public void RecordCustomer( ushort thingId ) => Customers.Add( thingId );
	}

	/// <summary>A path along y = 17 (x 5..25) and the walkway x = 10, 11 (y 5..16), as the phase P1 rig; the guest stands at (12, 17).</summary>
	private static (GuestSimulation Simulation, Guest Guest, ThemeParkIncGuestRecord Tpi, World World) Rig()
	{
		var grid = new GuestPathGrid( 30, 24 );
		foreach ( var x in new[] { 10, 11 } )
			for ( var y = 5; y <= 16; y++ )
				grid.Cells.SetType( x, y, ParkCellTypes.FixedWalkway );
		for ( var x = 5; x <= 25; x++ )
			grid.SetPath( x, 17, true );
		var settings = new GuestSettings
		{
			TpiGuestStart = new( Enumerable.Range( 0, 24 ).Select( i => new ThemeParkIncGuestType( 90 - i, 300 + 20 * i, 40 + i, i / 8 ) ).ToArray(), 400, 30, 15, 70, 20, 10, 10 ),
			TpiNative = ThemeParkIncNativeGuestTests.NativeSettings(),
			ArrivalTimeBetween = 5
		};
		var world = new World();
		var simulation = new GuestSimulation( grid, settings, 7 ) { ThemeParkIncNativeGuests = true, ArrivalsEnabled = false, ThemeParkIncWorld = world, Litter = new ParkLitter() };
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 10000;
		guest.Happiness = 50;
		guest.TpiPrankCode = 0;
		return (simulation, guest, guest.Tpi!, world);
	}

	/// <summary>A generator state whose draws satisfy <paramref name="predicate"/>.</summary>
	private static uint SeedWhere( Func<WorldRng, bool> predicate )
	{
		for ( uint seed = 1; ; seed++ )
			if ( predicate( new WorldRng( seed ) ) )
				return seed;
	}

	/// <summary>The generator state after <paramref name="draws"/> draws from <paramref name="seed"/>.</summary>
	private static uint After( uint seed, int draws )
	{
		var replica = new WorldRng( seed );
		for ( var draw = 0; draw < draws; draw++ )
			replica.NextWord();
		return replica.State;
	}

	/// <summary>Ticks the simulation to park turn <paramref name="turn"/> and puts the guest back to idle where it stands, happiness 50.</summary>
	private static void Advance( GuestSimulation simulation, Guest guest, long turn )
	{
		while ( simulation.ParkTurn < turn )
			simulation.Tick( Tick );
		guest.Happiness = 50;
		simulation.SetThemeParkIncState( guest, guest.Tpi!, ThemeParkIncGuestStates.Idle );
	}

	/// <summary>Runs the next park turn that is off the guest's phase (no pre-update draw), with the generator at <paramref name="seed"/> when it starts.</summary>
	private static void OffPhaseTurn( GuestSimulation simulation, Guest guest, uint seed, Action before )
	{
		while ( !simulation.NextTickStartsTurn || ((simulation.ParkTurn + 1) & 3) == (guest.Id & 3) )
			simulation.Tick( Tick );
		before();
		simulation.World.Reseed( seed );
		simulation.Tick( Tick );
	}

	/// <summary>Runs one idle update with the generator at <paramref name="seed"/>.</summary>
	private static void Idle( GuestSimulation simulation, Guest guest, uint seed )
	{
		simulation.World.Reseed( seed );
		simulation.UpdateTpiIdle( guest, guest.Tpi! );
	}

	/// <summary>A bin whose thing stands at (14, 16) with its entrance on the path at (14, 17) and the science stand point (0.5, 0.0); VAR_CAPACITY 20.</summary>
	private static ThemeParkIncThingView Bin( ushort id = 40, int contents = 0 )
	{
		var variables = new int[14];
		variables[3] = 20;
		variables[4] = contents;
		return new ThemeParkIncThingView( id, (14, 16), ThemeParkIncGuestSettings.CellWord( 14, 17 ), 0, 0.5f, 0f, variables );
	}

	// ---- Pure tables --------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheMoodAnimationFollowsGenerationAndGender()
	{
		Assert.AreEqual( 5, GuestSimulation.ThemeParkIncMoodAnimation( 0, false ) );
		Assert.AreEqual( 5, GuestSimulation.ThemeParkIncMoodAnimation( 0, true ) );
		Assert.AreEqual( (6, 7), (GuestSimulation.ThemeParkIncMoodAnimation( 1, true ), GuestSimulation.ThemeParkIncMoodAnimation( 1, false )), "0x60CDD0: 6 + (eax != 2)" );
		Assert.AreEqual( (8, 9), (GuestSimulation.ThemeParkIncMoodAnimation( 2, true ), GuestSimulation.ThemeParkIncMoodAnimation( 2, false )) );
		Assert.AreEqual( 0, GuestSimulation.ThemeParkIncMoodAnimation( 3, false ) );
	}

	[TestMethod]
	public void TheStandPointTurnsWithTheObject()
	{
		Assert.AreEqual( (127, 0), GuestSimulation.ThemeParkIncStandBytes( 0.5f, 0f, 0 ) );
		Assert.AreEqual( (0, 127), GuestSimulation.ThemeParkIncStandBytes( 0.5f, 0f, 90 ), "90: (y, 1 − x)" );
		Assert.AreEqual( (127, 255), GuestSimulation.ThemeParkIncStandBytes( 0.5f, 0f, 180 ), "180: (1 − x, 1 − y)" );
		Assert.AreEqual( (255, 127), GuestSimulation.ThemeParkIncStandBytes( 0.5f, 0f, 270 ), "270: (1 − y, x)" );
		Assert.AreEqual( (252, 127), GuestSimulation.ThemeParkIncStandBytes( 0.99f, 0.5f, 0 ), "0.99f × 255.0f rounds to a single (252.45) before the truncation" );
	}

	[TestMethod]
	public void StatesSevenAndNineRunInThisBuild()
	{
		Assert.IsTrue( ThemeParkIncGuestStates.RunsInThisBuild( ThemeParkIncGuestStates.Wandering ) );
		Assert.IsTrue( ThemeParkIncGuestStates.RunsInThisBuild( ThemeParkIncGuestStates.WalkingToBin ) );
		Assert.IsFalse( ThemeParkIncGuestStates.RunsInThisBuild( ThemeParkIncGuestStates.WalkingToBinForPrank ) );
	}

	// ---- State 6 -----------------------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void AHappyGuestPlaysItsMoodAnimationAfterAHundredTurns()
	{
		var (simulation, guest, tpi, _) = Rig();
		guest.Happiness = 90;
		tpi.SpotTurn = -200;
		var seed = SeedWhere( _ => true );
		Idle( simulation, guest, seed );
		Assert.AreEqual( ThemeParkIncGuestStates.SpotAnimation, tpi.State );
		Assert.AreEqual( (ThemeParkIncGuestStates.Idle, GuestSimulation.ThemeParkIncMoodAnimation( tpi.Generation, false )), (tpi.Saved, tpi.Mode), "0x60FB30 saves 6, the mood 0x60CDD0" );
		Assert.AreEqual( simulation.ParkTurn, tpi.SpotTurn );
		Assert.AreEqual( After( seed, 1 ), simulation.World.State, "draw A only" );

		var (recent, recentGuest, recentTpi, _) = Rig();
		recentGuest.Happiness = 90;
		Idle( recent, recentGuest, SeedWhere( rng => (rng.NextWord() & 3) == 0 ) );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, recentTpi.State, "turn − +0x230 must be above 100" );
	}

	[TestMethod]
	public void AFullIllnessVomitsOnOneDrawInThree()
	{
		var (simulation, guest, tpi, _) = Rig();
		var voices = new List<uint>();
		simulation.Voiced += ( _, voice ) => voices.Add( voice );
		guest.Nausea = 100;
		Idle( simulation, guest, SeedWhere( rng => rng.NextWord() % 3 == 0 ) );
		Assert.AreEqual( (ThemeParkIncGuestStates.SpotAnimation, GuestSimulation.ThemeParkIncVomitAnimation), (tpi.State, tpi.Mode) );
		Assert.AreEqual( 0f, guest.Nausea );
		Assert.AreEqual( LitterType.Vomit, simulation.Litter!.TypeAt( 12, 17 ) );
		CollectionAssert.Contains( voices, GuestSimulation.ThemeParkIncVomitVoice, "0x60BE90(0xCC) for every guest" );

		var (other, otherGuest, otherTpi, _) = Rig();
		otherGuest.Nausea = 100;
		Idle( other, otherGuest, SeedWhere( rng => rng.NextWord() % 3 != 0 && (rng.NextWord() & 0) == 0 ) );
		Assert.AreEqual( 100f, otherGuest.Nausea, "A % 3 nonzero keeps the guest well" );
		Assert.AreEqual( LitterType.None, other.Litter!.TypeAt( 12, 17 ) );
	}

	[TestMethod]
	public void TheLitterUrgeFindsABinAndTheWalkThereFillsIt()
	{
		var (simulation, guest, tpi, world) = Rig();
		var bin = Bin();
		world.BinList.Add( bin );
		guest.Litter = 95;
		Idle( simulation, guest, SeedWhere( rng => { rng.NextWord(); return (rng.NextWord() & 3) == 0; } ) );
		Assert.AreEqual( (ThemeParkIncGuestStates.WalkingToBin, 40), (tpi.State, tpi.Target), "0x618D10(radius 3) then setter(9)" );
		Assert.AreEqual( (14, 17), (guest.Navigation.TargetX >> 16, guest.Navigation.TargetY >> 16), "the bin's entrance cell" );
		Assert.AreEqual( (127, 0), ((guest.Navigation.TargetX >> 8) & 0xFF, (guest.Navigation.TargetY >> 8) & 0xFF), "the stand point 0x5E1300" );
		for ( var tick = 0; tick < 4000 && tpi.State == ThemeParkIncGuestStates.WalkingToBin; tick++ )
			simulation.Tick( Tick );
		Assert.AreNotEqual( ThemeParkIncGuestStates.WalkingToBin, tpi.State );
		Assert.AreEqual( 1, bin.Variable( 0 ), "VAR_USE = 1" );
		CollectionAssert.AreEqual( new ushort[] { 40 }, world.Customers, "0x5E5DA0" );
		Assert.AreEqual( 0f, guest.Litter );
		Assert.AreEqual( LitterType.None, simulation.Litter!.TypeAt( 14, 17 ) );
	}

	[TestMethod]
	public void AFullBinMakesTheGuestDropItsLitterBesideIt()
	{
		var (simulation, guest, tpi, world) = Rig();
		var bin = Bin( contents: 20 );
		world.BinList.Add( bin );
		guest.Litter = 95;
		tpi.Target = 40;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToBin );
		for ( var tick = 0; tick < 200 && tpi.State == ThemeParkIncGuestStates.WalkingToBin; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( 0, bin.Variable( 0 ), "a full bin (VAR 4 at least VAR 3) is not used" );
		CollectionAssert.AreEqual( new ushort[] { 40 }, world.Customers, "the customer is counted before the test" );
		Assert.AreNotEqual( LitterType.None, simulation.Litter!.TypeAt( guest.Navigation.Cell.X, guest.Navigation.Cell.Y ), "draw D's litter on the cell" );
		Assert.AreEqual( 0f, guest.Litter );
	}

	[TestMethod]
	public void WithoutABinTheUrgeDropsLitterOnAnEmptyCellOnly()
	{
		var (simulation, guest, tpi, _) = Rig();
		guest.Litter = 95;
		var seed = SeedWhere( rng => { rng.NextWord(); return (rng.NextWord() & 3) == 0; } );
		var replica = new WorldRng( seed );
		replica.NextWord();
		replica.NextWord();
		var type = (LitterType)(replica.NextWord() % 5 + 1);
		Idle( simulation, guest, seed );
		Assert.AreEqual( type, simulation.Litter!.TypeAt( 12, 17 ), "draw C % 5 + 1" );
		Assert.AreEqual( 0f, guest.Litter );

		var (full, fullGuest, _, _) = Rig();
		full.Litter!.Drop( 12, 17, LitterType.Vomit );
		fullGuest.Litter = 95;
		Idle( full, fullGuest, seed );
		Assert.AreEqual( 95f, fullGuest.Litter, "a littered cell keeps the urge (0x614C19)" );
	}

	[TestMethod]
	public void TheLeaveGateFallsThroughToTheDecision()
	{
		var (simulation, guest, tpi, _) = Rig();
		guest.ExitLevel = 0;
		tpi.Word224 = (int)simulation.ParkTurn;
		Idle( simulation, guest, SeedWhere( rng => (rng.NextWord() & 3) == 0 ) );
		Assert.AreEqual( ThemeParkIncGuestStates.LeavingPark, tpi.State );
		Assert.AreEqual( 50f - 25f, guest.Happiness, "BigHappinessChange" );

		var (wander, wanderGuest, wanderTpi, _) = Rig();
		wanderGuest.ExitLevel = 0;
		Idle( wander, wanderGuest, SeedWhere( rng => (rng.NextWord() & 3) == 1 ) );
		Assert.AreEqual( ThemeParkIncGuestStates.Wandering, wanderTpi.State, "A & 3 == 1 overwrites state 22 in the same update (no return at 0x614C72)" );
		Assert.IsTrue( wanderTpi.Wandered );
	}

	[TestMethod]
	public void AGuestNearAnUnusedShowTurnsToIt()
	{
		var (simulation, guest, tpi, world) = Rig();
		world.ShowList.Add( new ThemeParkIncThingView( 50, (13, 18), 0, 0, 0.5f, 0.5f, new int[2] ) );
		var seed = SeedWhere( _ => true );
		Idle( simulation, guest, seed );
		Assert.AreEqual( (ThemeParkIncGuestStates.Idle, HeadingAngle.Of( -1, 1 )), (tpi.State, tpi.Heading), "0x91E074(−dx, dy)" );
		Assert.AreEqual( After( seed, 1 ), simulation.World.State, "the turn ends the update" );

		var (used, usedGuest, usedTpi, usedWorld) = Rig();
		usedWorld.ShowList.Add( new ThemeParkIncThingView( 50, (13, 18), 0, 0, 0.5f, 0.5f, new[] { 1, 0 } ) );
		usedTpi.Word224 = (int)used.ParkTurn;
		Idle( used, usedGuest, SeedWhere( rng => (rng.NextWord() & 3) == 0 ) );
		Assert.AreEqual( 0, usedTpi.Heading, "a show whose VAR 0 is not 0 is not watched" );
	}

	/// <summary>The reaction to a performing entertainer (0x614D41), which closes the capability gate the entertainer's port left for it.</summary>
	[TestMethod]
	public void AGuestBesideAPerformingEntertainerTurnsToHimAndDoesNothingElse()
	{
		var (simulation, guest, tpi, world) = Rig();
		world.Performers.Add( new ThemeParkIncEntertainerView( 60, (13, 18), Performing: true ) );
		guest.Litter = 95;
		guest.Happiness = 5;
		guest.TpiPrankCode = 1;
		var seed = SeedWhere( rng => { var a = rng.NextWord(); return (rng.NextWord() & 3) != 0 && a % 100 < 10; } );
		Idle( simulation, guest, seed );
		Assert.AreEqual( (ThemeParkIncGuestStates.Idle, HeadingAngle.Of( -1, 1 ), 1), (tpi.State, tpi.Heading, tpi.Word1B8), "+0x2C and +0x1B8 = 1" );
		Assert.AreEqual( (5f, 95f), (guest.Happiness, guest.Litter), "no happiness, urge or illness change" );
		Assert.AreEqual( LitterType.None, simulation.Litter!.TypeAt( 12, 17 ), "the prank switch is not reached" );
		Assert.AreEqual( After( seed, 2 ), simulation.World.State, "A and the urge's B" );
	}

	[TestMethod]
	public void TheNearestEntertainerMustPerformAndAnEntertainerOnTheDiagonalIsNotFaced()
	{
		var (simulation, guest, tpi, world) = Rig();
		world.Performers.Add( new ThemeParkIncEntertainerView( 60, (13, 17), Performing: false ) );
		world.Performers.Add( new ThemeParkIncEntertainerView( 61, (11, 16), Performing: true ) );
		tpi.Word224 = (int)simulation.ParkTurn;
		Idle( simulation, guest, SeedWhere( rng => (rng.NextWord() & 3) == 0 ) );
		Assert.AreEqual( (0, 0), (tpi.Heading, tpi.Word1B8), "0x619100's nearest (13, 17) is not performing" );

		var (diagonal, diagonalGuest, diagonalTpi, diagonalWorld) = Rig();
		diagonalWorld.Performers.Add( new ThemeParkIncEntertainerView( 61, (11, 18), Performing: true ) );
		diagonalTpi.Word224 = (int)diagonal.ParkTurn;
		Idle( diagonal, diagonalGuest, SeedWhere( rng => (rng.NextWord() & 3) == 0 ) );
		Assert.AreEqual( (0, 0), (diagonalTpi.Heading, diagonalTpi.Word1B8), "dx + dy == 0 goes on to the prank gate with +0x1B8 = 0" );

		var (far, farGuest, farTpi, farWorld) = Rig();
		farWorld.Performers.Add( new ThemeParkIncEntertainerView( 61, (14, 17), Performing: true ) );
		farTpi.Word224 = (int)far.ParkTurn;
		Idle( far, farGuest, SeedWhere( rng => (rng.NextWord() & 3) == 0 ) );
		Assert.AreEqual( 0, farTpi.Heading, "an entertainer two cells away is outside 0x5B1B20's square" );
	}

	[TestMethod]
	public void AStinkbombPrankDropsItReportsItAndCheersThePrankster()
	{
		var (simulation, guest, tpi, _) = Rig();
		var posted = new List<int>();
		simulation.ThemeParkIncPrankPosted += ( _, kind ) => posted.Add( kind );
		guest.Happiness = 10;
		guest.TpiPrankCode = 1;
		tpi.Word224 = (int)simulation.ParkTurn;
		Idle( simulation, guest, SeedWhere( rng => { var a = rng.NextWord(); return a % 100 < 10 && (a & 3) == 0; } ) );
		Assert.AreEqual( LitterType.Stinkbomb, simulation.Litter!.TypeAt( 12, 17 ) );
		CollectionAssert.AreEqual( new[] { GuestSimulation.ThemeParkIncStinkbombPrank }, posted );
		Assert.AreEqual( 11f, guest.Happiness, "the tail adds 1.0" );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, tpi.State );

		var (happy, happyGuest, _, _) = Rig();
		happyGuest.Happiness = 15;
		happyGuest.TpiPrankCode = 1;
		happyGuest.Tpi!.Word224 = (int)happy.ParkTurn;
		Idle( happy, happyGuest, SeedWhere( rng => { var a = rng.NextWord(); return a % 100 < 10 && (a & 3) == 0; } ) );
		Assert.AreEqual( LitterType.None, happy.Litter!.TypeAt( 12, 17 ), "a happiness byte of PrankHappinessThreshold shuts the gate" );
	}

	[TestMethod]
	public void TheToiletPrankFillsTheToiletAndShutsTheGateForGood()
	{
		var (simulation, guest, tpi, _) = Rig();
		guest.Happiness = 10;
		guest.TpiPrankCode = 2;
		tpi.Word224 = (int)simulation.ParkTurn;
		Idle( simulation, guest, SeedWhere( rng => { var a = rng.NextWord(); return a % 100 < 10 && (a & 3) == 0; } ) );
		Assert.AreEqual( (80f, 1), (guest.Toilet, tpi.Word29C) );
		Assert.AreEqual( 10f, guest.Happiness, "case 1 goes to the decision without the tail" );
	}

	[TestMethod]
	public void TheGatedPranksFindNoTargetAndGoToTheDecision()
	{
		foreach ( var code in new[] { 3, 4, 6, 7, 8 } )
		{
			var (simulation, guest, tpi, _) = Rig();
			guest.Happiness = 10;
			guest.TpiPrankCode = code;
			tpi.Word224 = (int)simulation.ParkTurn;
			var seed = SeedWhere( rng => { var a = rng.NextWord(); return a % 100 < 10 && (a & 3) == 0; } );
			Idle( simulation, guest, seed );
			Assert.AreEqual( (ThemeParkIncGuestStates.Idle, 10f), (tpi.State, guest.Happiness), $"code {code}" );
			Assert.AreEqual( After( seed, 1 ), simulation.World.State, $"code {code} draws nothing more" );
		}
	}

	[TestMethod]
	public void AGuestThatCannotDecidePausesAndLosesTheSmallChange()
	{
		var (simulation, guest, tpi, _) = Rig();
		Advance( simulation, guest, 40 );
		tpi.Word224 = 0;
		tpi.Target = 40;
		Idle( simulation, guest, SeedWhere( rng => (rng.NextWord() & 3) == 2 ) );
		Assert.AreEqual( (ThemeParkIncGuestStates.SpotAnimation, GuestSimulation.ThemeParkIncCantDecideAnimation), (tpi.State, tpi.Mode), "0x615297: 4 with the umbrella down" );
		Assert.AreEqual( (45f, 0), (guest.Happiness, tpi.Target), "0x614460(0); 0x610080 clears +0x204 first" );
		Assert.AreEqual( (int)simulation.ParkTurn, tpi.Word224 );

		var (rested, restedGuest, restedTpi, _) = Rig();
		restedTpi.Word224 = (int)rested.ParkTurn - 30;
		Idle( rested, restedGuest, SeedWhere( rng => (rng.NextWord() & 3) == 0 ) );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, restedTpi.State, "the turn at most +0x224 + 30 returns" );
	}

	[TestMethod]
	public void TheFourthWayDrawsTwiceOnlyWhenTheFirstPasses()
	{
		var (simulation, guest, tpi, _) = Rig();
		var pass = SeedWhere( rng => (rng.NextWord() & 3) == 3 && rng.Next( 0x8000 ) < 0x3FFF );
		Idle( simulation, guest, pass );
		Assert.AreEqual( After( pass, 3 ), simulation.World.State, "A and the two world draws of 0x615318 and 0x615328" );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, tpi.State );

		var (failed, failedGuest, _, _) = Rig();
		var fail = SeedWhere( rng => (rng.NextWord() & 3) == 3 && rng.Next( 0x8000 ) >= 0x3FFF );
		Idle( failed, failedGuest, fail );
		Assert.AreEqual( After( fail, 2 ), failed.World.State, "a first draw of 0x3FFF or more returns" );
	}

	// ---- States 7 and 9, the removal ---------------------------------------------------------------------------------------------------

	[TestMethod]
	public void AWanderingGuestGoesIdleOnThreeDrawsInFour()
	{
		var (simulation, guest, tpi, _) = Rig();
		OffPhaseTurn( simulation, guest, SeedWhere( rng => (rng.NextWord() & 3) != 0 ), () => simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Wandering ) );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, tpi.State, "0x61A307" );

		var (again, againGuest, againTpi, _) = Rig();
		OffPhaseTurn( again, againGuest, SeedWhere( rng => (rng.NextWord() & 3) == 0 ), () => again.SetThemeParkIncState( againGuest, againTpi, ThemeParkIncGuestStates.Wandering ) );
		Assert.AreEqual( (ThemeParkIncGuestStates.Wandering, true), (againTpi.State, againTpi.Wandered), "0x609F70 found a destination: setter(7)" );
		Assert.IsTrue( againGuest.HasWaypoint, "the entry 0x61A9C7 asked for the walk to the target words again" );
	}

	[TestMethod]
	public void AStuckWalkToABinDropsTheLitter()
	{
		var (simulation, guest, tpi, world) = Rig();
		world.BinList.Add( Bin() );
		OffPhaseTurn( simulation, guest, 1, () =>
		{
			simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			guest.Litter = 95;
			tpi.Target = 40;
			simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToBin );
			guest.Navigation.Navigate( simulation.Grid.Cells, (40 << 16) + 0x8000, (40 << 16) + 0x8000 );
			guest.HasWaypoint = true;
			simulation.Litter!.Remove( 12, 17 );
		} );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, tpi.State, "status 2: draw E, the drop and setter(6)" );
		Assert.AreNotEqual( LitterType.None, simulation.Litter!.TypeAt( 12, 17 ) );
		Assert.AreEqual( 0, world.Customers.Count );
	}

	[TestMethod]
	public void TheRemovalMessageSendsTheGuestsWhoseTargetGoesIdle()
	{
		var (simulation, guest, tpi, _) = Rig();
		tpi.Target = 40;
		tpi.Word206 = 40;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToBin );
		simulation.ThemeParkIncObjectRemoved( 41 );
		Assert.AreEqual( ThemeParkIncGuestStates.WalkingToBin, tpi.State );
		simulation.ThemeParkIncObjectRemoved( 40 );
		Assert.AreEqual( (ThemeParkIncGuestStates.Idle, 0, 0, 45f), (tpi.State, tpi.Target, tpi.Word206, guest.Happiness), "0x60D120: SmallHappinessChange, +0x204 = 0, setter(6)" );
	}

	[TestMethod]
	public void TheWanderFlagRoundTripsAndStatesSevenAndNineRestore()
	{
		var (simulation, guest, tpi, _) = Rig();
		tpi.Wandered = true;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToBin );
		var save = simulation.CaptureSnapshot();
		Assert.IsTrue( save.Guests.Single().Tpi!.Wandered );
		var (restored, _, _, _) = Rig();
		restored.RestoreSnapshot( save );
		Assert.AreEqual( simulation.ComputeStateHash(), restored.ComputeStateHash() );
		Assert.IsTrue( restored.Guests.Single().Tpi!.Wandered );
		Assert.IsFalse( ThemeParkIncGuestSnapshot.Capture( new ThemeParkIncGuestRecord() ).Wandered );
	}
}
