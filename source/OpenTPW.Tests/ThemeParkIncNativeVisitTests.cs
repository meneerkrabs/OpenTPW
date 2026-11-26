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

	/// <summary>The queue rig with a native guest world; <paramref name="walk"/> (default <see cref="ThemeParkIncNativeGuestTests.NativeWalkByDefault"/>) walks with Theme Park Inc's navigator.</summary>
	private static (QueueTests.Rig Rig, World World, Payments Payments) Rig( bool takesGuests = true, bool? walk = null, GuestNeeds satisfies = GuestNeeds.None )
	{
		var rig = new QueueTests.Rig( takesGuests: takesGuests, satisfies: satisfies, kind: RideVisitorKind.Ride, configure: settings =>
		{
			settings.TpiGuestStart = new( Enumerable.Range( 0, 24 ).Select( i => new ThemeParkIncGuestType( 55, 300 + 20 * i, 40 + i, i / 8 ) ).ToArray(), 400, 30, 15, 70, 20, 10, 10 );
			settings.TpiNative = ThemeParkIncNativeGuestTests.NativeSettings() with { DecisionVariables = new( 30, 5, 0 ), DecisionWeights = DecisionWeights.Shipped };
		} );
		if ( walk ?? ThemeParkIncNativeGuestTests.NativeWalkByDefault )
			rig.Grid.Cells.EdgeRules = WalkEdgeRules.ThemeParkInc;
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

	/// <summary>
	/// State 19's entry for a teleporting ride (<c>0x61A69D</c>): the sprite placement <c>0x60AE00(0)</c> at <c>0x61A70B</c>, before the beam, raises the stamp counter and clears a stranded mark above
	/// the raised value (<c>0x60AFFD</c>, <c>0x60B002</c>) with Theme Park Inc's navigator; Theme Park World's walk counts the edits only.
	/// </summary>
	[TestMethod]
	public void ATeleportingRidesUnloadingRaisesTheStampCounterWithTheNativeWalk()
	{
		foreach ( var walk in new[] { true, false } )
		{
			var (rig, world, _) = Rig( walk: walk );
			world.Use = new ThemeParkIncUseTerms( 0, 0, 0, true, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 );
			var (guest, tpi) = Native( rig );
			SendToRide( rig, guest, tpi );
			Assert.IsTrue( RunUntil( rig, () => tpi.State == ThemeParkIncGuestStates.UnderObjectControl ) );
			var cells = rig.Grid.Cells;
			var beams = new List<int>();
			rig.Simulation.Beamed += _ => beams.Add( cells.StampCounter );
			var counter = cells.StampCounter;
			guest.StrandedMark = counter + 3;
			rig.Simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.BeingUnloaded );
			if ( walk )
			{
				Assert.AreEqual( (counter + 1, 0), (cells.StampCounter, guest.StrandedMark), "0x5D81F0 raised once; the raised value below the mark clears it" );
				CollectionAssert.AreEqual( new[] { counter + 1 }, beams, "the placement comes before the beam (0x61A716)" );
			}
			else
				Assert.AreEqual( (counter, counter + 3), (cells.StampCounter, guest.StrandedMark), "Theme Park World's walk counts the edits only" );
		}
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

	/// <summary>
	/// The use routine's golden cases from the oracle's reading (<c>port_use</c>, which agrees with the executable over the oracle's rounds) for the rig's inputs: the rating 55, the thing words
	/// (price 12, prize 4, quality 50, win 100, amount 50), the goods cost 7, the shipped PeepInfo words, a fee of 12, a generation-1 male guest whose preference is 55, happiness 10, thirst 60, hunger 50,
	/// toilet 20 and illness 10, the world generator reseeded with 12345.
	/// </summary>
	private static (QueueTests.Rig Rig, World World, Payments Payments, Guest Guest, ThemeParkIncGuestRecord Tpi) UseCase( ThemeParkIncUseTerms use, int appearance = 0,
		GuestNeeds satisfies = GuestNeeds.None, Action<Guest, ThemeParkIncGuestRecord>? setup = null, ThemeParkIncThingWords? words = null, double wear = 100 )
	{
		var (rig, world, payments) = Rig( satisfies: satisfies );
		payments.Words = words ?? payments.Words;
		rig.Ride.WearGauge = wear;
		world.Terms = world.Terms with { AppearanceEffect = appearance };
		world.Use = use;
		rig.Simulation.TpiCostumeBankFemale = [false, true, false];
		rig.Simulation.TpiSpriteBankCounts = [4, 4, 4];
		var (guest, tpi) = Native( rig );
		Assert.AreEqual( 55, rig.Ride.LiveExcitement & 0xFF, "the rating the oracle's cases read" );
		(guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea, guest.Litter, guest.Money) = (10, 60, 50, 20, 10, 0, 100);
		(tpi.Fee, tpi.Byte21B, tpi.PreferredExcitement, tpi.Generation) = (12, 1, 55, 1);
		tpi.Word234 = BitConverter.SingleToInt32Bits( 50f );
		setup?.Invoke( guest, tpi );
		rig.Simulation.World.Reseed( 12345 );
		rig.Simulation.ThemeParkIncUseRoutine( guest, tpi, world.Attractions()[0] );
		return (rig, world, payments, guest, tpi);
	}

	private static float[] Words2D0( ThemeParkIncGuestRecord tpi ) => tpi.Words2D0.Select( BitConverter.Int32BitsToSingle ).ToArray();

	/// <summary>A sideshow (WhichUIType 2) won: the prize booked and handed over, the gain ln(40 / 12) / ln 2 × 15 = 26, the record 0x14 and thought 5.</summary>
	[TestMethod]
	public void AWonSideshowMatchesTheOraclesReading()
	{
		var (rig, _, log, guest, tpi) = UseCase( new ThemeParkIncUseTerms( 2, 0, 2, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ), words: new( 12, 40, 50, 100, 50 ) );
		Assert.AreEqual( (61f, 20f, 128, 12), (guest.Happiness, guest.Nausea, guest.Money, tpi.FeesPaid), "10 + 25 (a perfect match) + 26 (the play's gain); the prize 40 in the purse" );
		CollectionAssert.AreEqual( new[] { 0f, 0f, 0f, 50f, 15f }, Words2D0( tpi ) );
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 1 }, tpi.Words1EC );
		CollectionAssert.AreEqual( new[] { (0x14, 83) }, log.Records );
		Assert.AreEqual( 12345u, rig.Simulation.World.State, "no draw" );
	}

	/// <summary>A sideshow lost: the medium step, the use flag cleared, <c>+0x2DC</c> − 35 (from 0, clamped) and thought 6; no record, no draw.</summary>
	[TestMethod]
	public void ALostSideshowMatchesTheOraclesReading()
	{
		var (rig, _, log, guest, tpi) = UseCase( new ThemeParkIncUseTerms( 2, 0, 2, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ), setup: ( _, tpi ) => tpi.Byte21B = 0 );
		Assert.AreEqual( (0f, 10f, 88, false), (guest.Happiness, guest.Nausea, guest.Money, tpi.UseSucceeded) );
		CollectionAssert.AreEqual( new[] { 0f, 0f, 0f, 0f, 0f }, Words2D0( tpi ) );
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 0 }, tpi.Words1EC );
		Assert.AreEqual( 0, log.Records.Count );
		Assert.AreEqual( 12345u, rig.Simulation.World.State );
	}

	/// <summary>A costume shop (AppearanceEffect 2) puts a costume on: category 6, the variant drawn over the three costume banks, a draw nothing reads, the judgement, <c>+0x2D8</c> = 33.</summary>
	[TestMethod]
	public void ACostumeShopPutsACostumeOnAsTheOracleReadsIt()
	{
		var (rig, _, log, guest, tpi) = UseCase( new ThemeParkIncUseTerms( 1, 0, 2, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ), appearance: 2 );
		Assert.AreEqual( (6, 0, true), (tpi.SpriteCategory, tpi.SpriteVariant, guest.InCostume) );
		Assert.AreEqual( (85f, 70f, 50f, 88), (guest.Happiness, guest.Nausea, guest.Litter, guest.Money) );
		CollectionAssert.AreEqual( new[] { 0f, 0f, 33f, 0f, 15f }, Words2D0( tpi ) );
		CollectionAssert.AreEqual( new[] { (0x11, 155) }, log.Records );
		Assert.AreEqual( 0, log.Declined );
		Assert.AreEqual( (ThemeParkIncGuestStates.SpotAnimation, 7), (tpi.State, tpi.Mode), "costume bank 0 is a male's: animation 7" );
		Assert.AreEqual( 3337293350u, rig.Simulation.World.State );
	}

	/// <summary>A costume shop takes a costume off: category 0 and the variant drawn after a reseed with the guest's id (a kid's bank, bug-compatible).</summary>
	[TestMethod]
	public void ACostumeShopTakesACostumeOffAsTheOracleReadsIt()
	{
		var (rig, _, log, guest, tpi) = UseCase( new ThemeParkIncUseTerms( 1, 0, 2, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ), appearance: 2,
			setup: ( _, tpi ) => (tpi.SpriteCategory, tpi.SpriteVariant) = (6, 2) );
		Assert.AreEqual( 1, guest.Id, "the oracle's case reseeds with the id 1" );
		Assert.AreEqual( (0, 3, false), (tpi.SpriteCategory, tpi.SpriteVariant, guest.InCostume) );
		Assert.AreEqual( (85f, 88), (guest.Happiness, guest.Money) );
		CollectionAssert.AreEqual( new[] { 0f, 0f, 0f, 0f, 15f }, Words2D0( tpi ) );
		CollectionAssert.AreEqual( new[] { (0x11, 155) }, log.Records );
		Assert.AreEqual( 3412190274u, rig.Simulation.World.State );
	}

	/// <summary>An umbrella shop (AppearanceEffect 3) opens an umbrella: the colour draw, the umbrella up (state 4) with its speed term 0, the judgement (quality 10 below 20 + R % 10: declined), 33.</summary>
	[TestMethod]
	public void AnUmbrellaShopMatchesTheOraclesReading()
	{
		var (rig, _, log, guest, tpi) = UseCase( new ThemeParkIncUseTerms( 1, 0, 2, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ), appearance: 3, words: new( 12, 4, 10, 100, 50 ) );
		Assert.AreEqual( (UmbrellaRules.StateOpen, 0, 0), (guest.UmbrellaState, guest.UmbrellaType, guest.ToiletBonus) );
		Assert.AreEqual( (85f, 70f, 50f, 88), (guest.Happiness, guest.Nausea, guest.Litter, guest.Money) );
		CollectionAssert.AreEqual( new[] { 0f, 0f, 33f, 0f, 15f }, Words2D0( tpi ) );
		CollectionAssert.AreEqual( new[] { (0x12, 155) }, log.Records );
		Assert.AreEqual( 1, log.Declined );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, tpi.State, "the umbrella up: no mood animation" );
		Assert.AreEqual( 339536374u, rig.Simulation.World.State );
	}

	/// <summary>A relief object's visit (the thing flag <c>+0x42</c> bit 1): half the toilet byte gained, a worn one −20, an illness byte above 90 cured for −50; the toilet 0 and the speed term 25.</summary>
	[TestMethod]
	public void AReliefVisitMatchesTheOraclesReading()
	{
		var plain = new ThemeParkIncUseTerms( 0, 0, 2, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 );
		foreach ( var (name, wear, illness, happiness, nausea, words4) in new[] { ("fresh", 100.0, 10f, 70f, 20f, 15f), ("worn", 10.0, 10f, 15f, 20f, 15f), ("ill", 100.0, 95f, 0f, 0f, 20f) } )
		{
			var (rig, _, _, guest, tpi) = UseCase( plain, satisfies: GuestNeeds.Toilet, wear: wear, setup: ( guest, _ ) => (guest.Toilet, guest.Nausea) = (70, illness) );
			Assert.IsTrue( rig.Ride.ProvidesRelief );
			Assert.AreEqual( (happiness, nausea, 0f, 88), (guest.Happiness, guest.Nausea, guest.Toilet, guest.Money), name );
			Assert.AreEqual( GuestSpeed.ToiletBonus, guest.ToiletBonus, name );
			Assert.AreEqual( words4, Words2D0( tpi )[4], name );
			Assert.AreEqual( 12345u, rig.Simulation.World.State, name );
		}
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

	/// <summary>
	/// The won play's ratio is <c>fyl2x</c>'s full-precision ln of the quotient over ln 2, rounded once to a single (<c>fdivp</c> at the control word <c>0x7F</c>); the port rounds DetMath.Log2's double.
	/// Every positive single from 2^-32 to 2^32 (every quotient a fidiv of two 32-bit words can give) has its double at least 3 double ulps from a midpoint between two singles, so the two
	/// roundings agree; the two closest quotients round to the singles an 80-digit reference gives.
	/// </summary>
	[TestMethod]
	public void TheWonPlaysRatioRoundsAsTheExtendedQuotientForEveryQuotient()
	{
		var low = BitConverter.SingleToInt32Bits( MathF.ScaleB( 1f, -32 ) );
		var high = BitConverter.SingleToInt32Bits( MathF.ScaleB( 1f, 32 ) );
		const int Parts = 64;
		var closest = new long[Parts];
		System.Threading.Tasks.Parallel.For( 0, Parts, part =>
		{
			var nearest = long.MaxValue;
			var size = (high - low + Parts - 1) / Parts;
			for ( var bits = low + part * size; bits < Math.Min( high, low + (part + 1) * size ); bits++ )
			{
				var ratio = DetMath.Log2( BitConverter.Int32BitsToSingle( bits ) );
				if ( ratio == 0 )
					continue;
				var single = (float)ratio;
				foreach ( var neighbour in new[] { MathF.BitIncrement( single ), MathF.BitDecrement( single ) } )
					nearest = Math.Min( nearest, Math.Abs( BitConverter.DoubleToInt64Bits( ratio ) - BitConverter.DoubleToInt64Bits( ((double)single + neighbour) / 2 ) ) );
			}
			closest[part] = nearest;
		} );
		Assert.AreEqual( 3, closest.Min(), "no quotient's logarithm lies within 3 double ulps of a single's rounding boundary" );
		// the two quotients at 3 ulps (0x40207AB9, 0x3EA07AB9): log2 = 1.32624417543411195085... and -1.67375582456588804914... (80 digits)
		Assert.AreEqual( 1.3262441158294678f, (float)DetMath.Log2( BitConverter.Int32BitsToSingle( 0x40207AB9 ) ) );
		Assert.AreEqual( -1.6737558841705322f, (float)DetMath.Log2( BitConverter.Int32BitsToSingle( 0x3EA07AB9 ) ) );
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

	/// <summary>
	/// The burst balloon's litter (<c>0x613D00(1)</c> through <c>0x5D8C80</c>): the type by the colour (<c>0x9D0614</c>) on the victim's cell, the world generator reseeded with the cell word and drawn
	/// twice; a cell that holds litter takes none and the generator is not touched.
	/// </summary>
	[TestMethod]
	public void ABalloonPranksLitterReseedsTheWorldGeneratorWithTheVictimsCell()
	{
		foreach ( var littered in new[] { false, true } )
		{
			var (rig, _, _) = Rig( takesGuests: false );
			var simulation = rig.Simulation;
			var litter = new ParkLitter { ThemeParkInc = true, World = () => simulation.World };
			simulation.Litter = litter;
			var (prankster, ptpi) = Native( rig, 5, 0 );
			var (victim, vtpi) = Native( rig, 5, 0 );
			(vtpi.HoldsBalloon, vtpi.BalloonCountdown, vtpi.BalloonColour) = (true, 100, 1);
			if ( littered )
				Assert.IsTrue( litter.Drop( 5, 0, (LitterType)2 ) );
			prankster.Happiness = 10;
			prankster.TpiPrankCode = 6;
			ptpi.Word224 = (int)simulation.ParkTurn;
			uint seed = 1;
			while ( !(new WorldRng( seed ).NextWord() is var a && a % 100 < 10 && (a & 3) == 1) )
				seed++;
			simulation.World.Reseed( seed );
			var afterTheDraw = new WorldRng( seed );
			afterTheDraw.NextWord();
			uint? atThePrank = null;
			simulation.ThemeParkIncPrankPosted += ( _, _ ) => atThePrank = simulation.World.State;
			simulation.UpdateTpiIdle( prankster, ptpi );
			Assert.IsFalse( vtpi.HoldsBalloon );
			if ( littered )
			{
				Assert.AreEqual( (LitterType)2, litter.TypeAt( 5, 0 ), "+0x30 nonzero (0x5D8CC7): the cell keeps its litter" );
				Assert.AreEqual( afterTheDraw.State, atThePrank, "nothing reseeded, nothing drawn" );
			}
			else
			{
				Assert.AreEqual( LitterType.BurstBalloon4, litter.TypeAt( 5, 0 ), "colour 1: the type 0xC (0x9D0614)" );
				var mirror = new WorldRng( 0 );
				ParkLitter.ThemeParkIncSpriteOffset( mirror, 5, 0 );
				Assert.AreEqual( mirror.State, atThePrank, "the reseed 0x649430(cell word) and the draws 0x5D8E14, 0x5D8E42" );
			}
		}
	}
}
