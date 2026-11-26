using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's native guest states, phase P3b (docs/reverse/TPI-GUESTS.md): the visit of an attraction (states 16, 17, 20 and 19), the use routine 0x6118D0 with its payment step 0x6126D0 and its
/// outcome 0x612740, the ride's force 0x6182A0, the removal message's rider branch and the balloon prank (case 5 of state 6). The expected numbers of the use routine are the oracle's reading
/// (tools/ppc-analysis/oracle_x86/tpi_guest_visit_oracle.py, port_use, which agrees with the executable).
/// </summary>
[TestClass]
public sealed class ThemeParkIncNativeVisitTests
{
	private const ushort Thing = 40;

	/// <summary>A payer that reports the thing words the visit reads and records the bookings.</summary>
	private sealed class Payments : IGuestPayments
	{
		public ThemeParkIncThingWords Words = new( 12, 4, 50, 100, 50 );
		public readonly List<string> Log = new();
		public readonly List<(int Category, int Value)> Records = new();
		public int Declined;
		public int AdmissionFee => 0;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public ThemeParkIncThingWords? ThemeParkIncThing( IRideVisitorBridge attraction ) => Words;
		public void ThemeParkIncTakings( IRideVisitorBridge attraction, int fee ) => Log.Add( $"takings {fee}" );
		public void ThemeParkIncCustomer( IRideVisitorBridge attraction ) => Log.Add( "customer" );
		public void ThemeParkIncCostBooked( IRideVisitorBridge attraction, int amount ) => Log.Add( $"cost {amount}" );
		public int ThemeParkIncGoodsCost( IRideVisitorBridge attraction ) => 7;
		public void ThemeParkIncRideAdmitted( IRideVisitorBridge attraction ) => Log.Add( "admitted" );
		public void PriceTested( int category, int value ) => Records.Add( (category, value) );
		public void PriceVerdict( IRideVisitorBridge attraction, bool cheap, bool expensive, bool declined ) => Declined += declined ? 1 : 0;
	}

	private sealed class World( RideVisitorBridge ride ) : IThemeParkIncGuestWorld
	{
		public ThemeParkIncScoreTerms Terms = new( 0, 0, false, 0, 0, 0, 0, 100, 100, 100 );
		public ThemeParkIncUseTerms Use;
		public IReadOnlyList<ThemeParkIncThingView> Bins() => [];
		public IReadOnlyList<ThemeParkIncThingView> Shows() => [];
		public IReadOnlyList<ThemeParkIncEntertainerView> Entertainers() => [];
		public ThemeParkIncThingView? Thing( ushort thingId ) => null;
		public void RecordCustomer( ushort thingId ) { }
		public IReadOnlyList<ThemeParkIncAttractionView> Attractions() => [new ThemeParkIncAttractionView( ThemeParkIncNativeVisitTests.Thing, (2, 6), ride, Terms ) { Use = Use }];
	}

	private static (QueueTests.Rig Rig, World World, Payments Payments) Rig( bool takesGuests = true )
	{
		var rig = new QueueTests.Rig( takesGuests: takesGuests, configure: settings =>
		{
			settings.TpiGuestStart = new( Enumerable.Range( 0, 24 ).Select( i => new ThemeParkIncGuestType( 55, 300 + 20 * i, 40 + i, i / 8 ) ).ToArray(), 400, 30, 15, 70, 20, 10, 10 );
			settings.TpiNative = ThemeParkIncNativeGuestTests.NativeSettings() with { DecisionVariables = new( 30, 5, 0 ), DecisionWeights = DecisionWeights.Shipped };
		} );
		var world = new World( rig.Ride );
		var payments = new Payments();
		rig.Simulation.ThemeParkIncNativeGuests = true;
		rig.Simulation.ThemeParkIncWorld = world;
		rig.Simulation.Payments = payments;
		return (rig, world, payments);
	}

	private static (Guest Guest, ThemeParkIncGuestRecord Tpi) Native( QueueTests.Rig rig, int x = 2, int y = 0 )
	{
		var guest = rig.Simulation.SpawnInPark( x, y );
		(guest.Happiness, guest.ExitLevel, guest.TpiPrankCode) = (50, 100000, 0);
		(guest.Hunger, guest.Thirst, guest.Toilet, guest.Nausea, guest.Litter) = (0, 0, 0, 0, 0);
		return (guest, guest.Tpi!);
	}

	private static bool RunUntil( QueueTests.Rig rig, Func<bool> done, int turns = 2000 )
	{
		for ( var turn = 0; turn < turns; turn++ )
		{
			if ( done() )
				return true;
			rig.StepTurn();
			foreach ( var guest in rig.Simulation.Guests )
				(guest.Hunger, guest.Thirst, guest.Toilet, guest.Nausea, guest.Litter) = (0, 0, 0, 0, 0);
		}
		return done();
	}

	private static void SendToRide( QueueTests.Rig rig, Guest guest, ThemeParkIncGuestRecord tpi )
	{
		rig.Simulation.ThemeParkIncMajorDecision( guest, tpi, rig.Simulation.Settings.TpiNative! );
		Assert.AreEqual( Thing, tpi.Target );
		rig.Simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToMajor );
	}

	[TestMethod]
	public void TheCallRecordsTheFeeAndTheExitPaysIt()
	{
		var (rig, _, payments) = Rig();
		var (guest, tpi) = Native( rig );
		guest.Money = 100;
		SendToRide( rig, guest, tpi );
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.BeingLoaded ) );
		Assert.AreEqual( 12, tpi.Fee, "OnAdmittance 0x619670: +0x1B4 = the thing's price +0x94" );
		Assert.AreEqual( 100, guest.Money, "nothing is paid at the call" );
		var states = new List<int>();
		Assert.IsTrue( RunUntil( rig, () =>
		{
			if ( states.Count == 0 || states[^1] != tpi.State )
				states.Add( tpi.State );
			return tpi.State == ThemeParkIncGuestStates.BeingUnloaded;
		} ) );
		CollectionAssert.IsSubsetOf( new[] { 16, 17, 20, 19 }, states, "being loaded, waiting for the script, under its control, being unloaded" );
		Assert.AreEqual( (88, 12), (guest.Money, tpi.FeesPaid), "0x6126D0: the purse falls and +0x1C4 rises by the fee" );
		CollectionAssert.AreEqual( new[] { "admitted", "takings 12", "customer" }, payments.Log, "state 20's events, then the payment step and the customer" );
		Assert.AreEqual( (1, 1), (tpi.Byte21B, tpi.Words1EC[0]), "a win byte of 100 always wins the roll; a ride (WhichUIType 0) raises +0x1EC" );
		Assert.IsTrue( tpi.UseSucceeded );
		CollectionAssert.AreEqual( new[] { (int)Thing, 0, 0, 0 }, guest.UsedHistory );
	}

	[TestMethod]
	public void TheChanceRollWritesTheScriptsParamAndThePlaceByte()
	{
		var (rig, _, payments) = Rig();
		payments.Words = payments.Words with { WinByte = 40 };
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.BeingLoaded ) );
		var seed = rig.Simulation.World.State;
		var expected = new WorldRng( seed ).NextWord() % 100 <= 40 ? 1 : 0;
		tpi.Byte21B = 9;
		rig.Simulation.World.Reseed( seed );
		rig.Simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingToBeAdmitted );
		Assert.AreEqual( (expected, expected), (tpi.Byte21B, rig.Ride.Param), "0x5E7A00: one draw, R % 100 against the win byte, into +0x21B and VAR 11" );
		Assert.AreEqual( 3, tpi.Mode );
	}

	/// <summary>A balloon shop's sale (WhichUIType 1, AppearanceEffect 1, Salt): the oracle's reading of the use routine for these inputs.</summary>
	[TestMethod]
	public void TheUseRoutineOfABalloonShopMatchesTheOraclesReading()
	{
		var (rig, world, payments) = Rig();
		world.Terms = world.Terms with { AppearanceEffect = 1 };
		world.Use = new ThemeParkIncUseTerms( 1, 0, 2, false, false, 30, 10, 0, 10, 5, 0, 2, 20, 0, 0, 0 );
		var (guest, tpi) = Native( rig );
		Assert.AreEqual( 55, rig.Ride.LiveExcitement & 0xFF, "the rating the oracle's case reads" );
		(guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea, guest.Litter, guest.Money) = (50, 60, 50, 20, 10, 0, 100);
		(tpi.Fee, tpi.Byte21B, tpi.PreferredExcitement, tpi.Generation) = (12, 1, 55, 1);
		tpi.Word234 = BitConverter.SingleToInt32Bits( 50f );
		var view = world.Attractions()[0];
		rig.Simulation.World.Reseed( 12345 );
		rig.Simulation.ThemeParkIncUseRoutine( guest, tpi, view );
		Assert.AreEqual( (100f, 40f, 40f, 20f, 70f, 55f), (guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea, guest.Litter) );
		Assert.AreEqual( (88, 12), (guest.Money, tpi.FeesPaid) );
		CollectionAssert.AreEqual( new[] { 35f, 35f, 0f, 0f, 15f }, tpi.Words2D0.Select( BitConverter.Int32BitsToSingle ).ToArray(), "+0x2D0 .. +0x2E0" );
		Assert.AreEqual( (true, 127u, 2), (tpi.HoldsBalloon, tpi.BalloonCountdown, tpi.BalloonColour), "the balloon: 50 × 255 / 100 and the colour draw" );
		Assert.AreEqual( (true, 0, 1), (tpi.UseSucceeded, tpi.Words1EC[0], tpi.Words1EC[1]) );
		CollectionAssert.AreEqual( new[] { "takings 12", "customer", "cost 7" }, payments.Log );
		CollectionAssert.AreEqual( new[] { (0xC, 200), (0x10, 200) }, payments.Records, "3 × (100 − 50) + 50 by the ingredient (Salt: 0xC) and the appearance (a balloon: 0x10)" );
		Assert.AreEqual( 0, payments.Declined );
		Assert.AreEqual( (ThemeParkIncGuestStates.SpotAnimation, 7), (tpi.State, tpi.Mode), "the mood above HappyDiffForVGood: a male parent's animation 7 (0x60FB30)" );
		Assert.AreEqual( 2053832126u, rig.Simulation.World.State, "two small-portion draws, the colour and the quality draw" );
	}

	[TestMethod]
	public void ALostUseCostsTheMediumStepAndNoOutcome()
	{
		var (rig, world, payments) = Rig();
		world.Use = new ThemeParkIncUseTerms( 0, 0, 2, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 );
		var (guest, tpi) = Native( rig );
		guest.Money = 100;
		(tpi.Fee, tpi.Byte21B) = (12, 0);
		rig.Simulation.World.Reseed( 12345 );
		rig.Simulation.ThemeParkIncUseRoutine( guest, tpi, world.Attractions()[0] );
		Assert.AreEqual( (35f, false, 88), (guest.Happiness, tpi.UseSucceeded, guest.Money), "0x611B0B: −MediumHappinessChange, +0x2E4 = 0, and still the fee" );
		Assert.AreEqual( 12345u, rig.Simulation.World.State, "no draw" );
		CollectionAssert.AreEqual( new[] { "takings 12", "customer" }, payments.Log );
	}

	[TestMethod]
	public void AWonPlayGainsTheLogarithmOfThePrizeOverThePrice()
	{
		Assert.AreEqual( 15, GuestSimulation.ThemeParkIncPlayGain( 40, 20, 15 ), "ln(2) / ln 2 × 15" );
		Assert.AreEqual( 0, GuestSimulation.ThemeParkIncPlayGain( 40, 0, 15 ), "a free play: an infinity, whose _ftol is 0" );
		Assert.AreEqual( 0, GuestSimulation.ThemeParkIncPlayGain( 0, 20, 15 ), "no prize: −infinity, 0" );
		Assert.AreEqual( -15, GuestSimulation.ThemeParkIncPlayGain( 10, 20, 15 ) );
	}

	[TestMethod]
	public void TheRidesHeadStepForcesAnAdmittedGuestOntoTheRide()
	{
		var (rig, _, _) = Rig( takesGuests: false );
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.WaitingToBeAdmitted ) );
		Assert.AreEqual( (guest.Id, guest.Id), (rig.Ride.QueueHead, rig.Ride.LetMeOn), "17 stays the list's head while VAR_LETMEON names it" );
		// the script takes VAR_LETMEON, then the ride breaks down before the guest's own update
		rig.Script[RideVariables.VAR_LETMEON] = 0;
		IRideVisitorHost host = rig.Simulation;
		Assert.IsTrue( host.ForcesAdmittedHead( rig.Ride, guest.Id ), "0x5E3CE9: forced onto the ride (0x6182A0)" );
		Assert.AreEqual( (ThemeParkIncGuestStates.UnderObjectControl, 0), (tpi.State, rig.Ride.QueueLength) );
	}

	[TestMethod]
	public void TheRemovalMessageGivesARiderItsModelBackAndMakesItIdle()
	{
		var (rig, _, _) = Rig();
		var (guest, tpi) = Native( rig );
		SendToRide( rig, guest, tpi );
		Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.UnderObjectControl ) );
		var happiness = guest.Happiness;
		rig.Simulation.ThemeParkIncObjectRemoved( Thing );
		Assert.AreEqual( (ThemeParkIncGuestStates.Idle, 0), (tpi.State, tpi.Target), "0x60D14B: the rider is idle with no target" );
		Assert.AreEqual( MeterMath.Add( happiness, -5 ), guest.Happiness, "only SmallHappinessChange: a rider is not dismissed from a queue" );
	}

	[TestMethod]
	public void ABalloonPrankBurstsTheBalloonOfAGuestOnTheCell()
	{
		var (rig, _, _) = Rig( takesGuests: false );
		var (prankster, ptpi) = Native( rig, 5, 0 );
		var (victim, vtpi) = Native( rig, 5, 0 );
		(vtpi.HoldsBalloon, vtpi.BalloonCountdown) = (true, 100);
		var posted = new List<int>();
		rig.Simulation.ThemeParkIncPrankPosted += ( _, kind ) => posted.Add( kind );
		prankster.Happiness = 10;
		prankster.TpiPrankCode = 6;
		ptpi.Word224 = (int)rig.Simulation.ParkTurn;
		var victimHappiness = victim.Happiness;
		uint seed = 1;
		while ( !(new WorldRng( seed ).NextWord() is var a && a % 100 < 10 && (a & 3) == 1) )
			seed++;
		rig.Simulation.World.Reseed( seed );
		rig.Simulation.UpdateTpiIdle( prankster, ptpi );
		Assert.AreEqual( (false, 0u), (vtpi.HoldsBalloon, vtpi.BalloonCountdown), "0x613D00(victim, 1): the balloon goes" );
		Assert.AreEqual( MeterMath.Add( victimHappiness, -15 ), victim.Happiness, "the victim loses MediumHappinessChange" );
		CollectionAssert.AreEqual( new[] { 6 }, posted, "the tail with kind 6" );
		Assert.AreEqual( 11f, prankster.Happiness, "the tail adds 1.0" );
	}
}
