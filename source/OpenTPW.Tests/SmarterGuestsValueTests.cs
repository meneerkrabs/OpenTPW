using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Phase 2 of the smarter-guests plan (docs/SMARTER-GUESTS.md): value for money. The rules in isolation, the host's price facts, the run of price-emptied decisions with its save section and validation, the guest that goes home, and
/// the off path. The measured effect is in the page's tables and in the compare test of <see cref="SmarterGuestsTests"/>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SmarterGuestsValueTests
{
	private static readonly GuestSettings Settings = GuestTests.CreateSettings();

	private static GuestView Guest( ulong birthId = 11, int money = 500, float happiness = 60, float thirst = 90 ) =>
		new( 7, birthId, 0, happiness, thirst, 10, 10, 0, money, 3, 3, Array.AsReadOnly( new int[4] ), Array.AsReadOnly( new int[4] ) );

	private static AttractionView Stand( RideVisitorKind kind = RideVisitorKind.Shop, int price = 100, uint willingness = 300 ) =>
		new( 5, 1, kind, true, (3, 4), (3, 5), 0, 0, 4, 20, 50, 0, 80, 0, false, 30, false, 0, 0, true, false, false, price, willingness );

	// ---- The rules in isolation ---------------------------------------------------------------

	[TestMethod]
	public void ThePriceSensitivityStaysInItsDocumentedBandAndCoversIt()
	{
		var values = Enumerable.Range( 0, 5000 ).Select( id => SmarterGuestsRules.SensitivityPercent( SmarterGuestsState.Trait( 9, (ulong)id, SmarterGuestsRules.PriceSensitivityTrait ) ) ).ToArray();
		Assert.AreEqual( 90, values.Min() );
		Assert.AreEqual( 110, values.Max() );
		Assert.AreEqual( SmarterGuestsRules.SensitivityValues, values.Distinct().Count(), "every value of the band occurs" );
		Assert.AreEqual( 90, SmarterGuestsRules.SensitivityMinimumPercent );
		// the same guest has the same sensitivity for its whole visit, whoever asks
		var context = new DecisionContext( Settings, 5, false, 9 );
		Assert.AreEqual( SmarterGuestsRules.SensitivityPercent( context.Trait( Guest( 11 ), 0 ) ), SmarterGuestsRules.SensitivityPercent( (context with { ParkTurn = 900 }).Trait( Guest( 11 ), 0 ) ) );
	}

	[TestMethod]
	public void WithNeutralSensitivityThePredictionIsTheStandPointTest()
	{
		foreach ( var (price, cost, ripOff) in new[] { (30, 40, 0), (220, 60, 10), (90, 100, 20), (1, 5, 0) } )
			foreach ( var money in new[] { 0, 10, 100, 1000 } )
				foreach ( var meter in new[] { 0f, 40f, 90f } )
				{
					var terms = new PriceTerms( price, 0, 0, false, cost, 80, 0, 0, 5, ripOff );
					var willingness = QueueJoinRules.WillingnessToPay( terms, 60 - meter / 3, meter, 20, 0 );
					var view = Stand( price: price, willingness: willingness );
					Assert.AreEqual( QueueJoinRules.TooExpensive( terms, 60 - meter / 3, meter, 20, 0, money ), SmarterGuestsRules.WouldRefuse( view, money, 100 ), $"price {price}, cost {cost}, money {money}, thirst {meter}" );
				}
	}

	[TestMethod]
	public void SensitivityMovesTheExpectedWillingnessWithinItsBandAndMoneyAlwaysCounts()
	{
		var view = Stand( price: 95, willingness: 100 );
		Assert.IsFalse( SmarterGuestsRules.WouldRefuse( view, 500, 90 ), "a guest who thinks stands cheap expects to pay 111" );
		Assert.IsFalse( SmarterGuestsRules.WouldRefuse( view, 500, 100 ) );
		Assert.IsTrue( SmarterGuestsRules.WouldRefuse( view, 500, 110 ), "a sensitive guest expects 90" );
		Assert.IsTrue( SmarterGuestsRules.WouldRefuse( view, 94, 90 ), "price above the purse" );
		Assert.IsFalse( SmarterGuestsRules.WouldRefuse( view, 95, 90 ), "the purse covers it exactly" );
		Assert.IsTrue( SmarterGuestsRules.WouldRefuse( Stand( price: 1, willingness: 0 ), 500, 100 ), "a willingness of 0 buys nothing" );
		Assert.IsFalse( SmarterGuestsRules.WouldRefuse( Stand( price: 5, willingness: uint.MaxValue ), -1, 100 ), "a negative purse reads as a huge unsigned one, as in the original's test" );
	}

	[TestMethod]
	public void OnlyAPricedShopOrSideshowCanBeZeroedAndTheScoreOtherwiseIsTheOriginals()
	{
		var smarter = new SmarterGuestDecisions();
		var original = OriginalGuestDecisions.Instance;
		var context = new DecisionContext( Settings, 12, false, 3 );
		var guest = Guest();
		var affordable = Stand( price: 100, willingness: 300 );
		var baseScore = original.ScoreAttraction( context, guest, affordable );
		Assert.IsTrue( baseScore >= AttractionScore.CandidateFloor, $"the fixture scores {baseScore}, above the choice floor" );
		Assert.AreEqual( baseScore, smarter.ScoreAttraction( context, guest, affordable ), "a stand the guest can pay for scores as the original scores it" );
		Assert.AreEqual( 0u, smarter.ScoreAttraction( context, guest, Stand( price: 400, willingness: 300 ) ), "above the willingness: refused at the stand, so skipped" );
		Assert.AreEqual( 0u, smarter.ScoreAttraction( context, Guest( money: 50 ), affordable ), "above the purse" );
		Assert.AreEqual( 0u, smarter.ScoreAttraction( context, guest, Stand( RideVisitorKind.Sideshow, 400, 300 ) ), "a sideshow too" );
		Assert.AreEqual( original.ScoreAttraction( context, guest, Stand( RideVisitorKind.Ride, 400, 300 ) ), smarter.ScoreAttraction( context, guest, Stand( RideVisitorKind.Ride, 400, 300 ) ), "rides cost nothing per use: no price rule" );
		Assert.AreEqual( original.ScoreAttraction( context, guest, Stand( price: 0, willingness: 0 ) ), smarter.ScoreAttraction( context, guest, Stand( price: 0, willingness: 0 ) ), "a free stand is never refused" );
		// PriceTurnedAway: only a stand the rule zeroed that the original would have chosen
		Assert.IsTrue( smarter.PriceTurnedAway( context, guest, Stand( price: 400, willingness: 300 ) ) );
		Assert.IsFalse( smarter.PriceTurnedAway( context, guest, affordable ) );
		Assert.IsFalse( smarter.PriceTurnedAway( context, guest, Stand( RideVisitorKind.Ride, 400, 300 ) ) );
		Assert.IsFalse( smarter.PriceTurnedAway( context, guest, default ) );
		Assert.IsFalse( ((IGuestDecisions)original).PriceTurnedAway( context, guest, Stand( price: 400, willingness: 300 ) ), "the original never turns a stand away" );
		Assert.IsFalse( ((IGuestDecisions)original).DecidePriceStall( context, guest, 99 ).GoHome );
		Assert.IsFalse( smarter.DecidePriceStall( context, guest, 0 ).GoHome );
		Assert.IsTrue( smarter.DecidePriceStall( context, guest, SmarterGuestsRules.StallsBeforeGoingHome ).GoHome );
		Assert.AreEqual( GuestThought.Dissatisfied, smarter.DecidePriceStall( context, guest, SmarterGuestsRules.StallsBeforeGoingHome ).Thought, "the original's thought 6" );
		// the trait scales the willingness: across many guests the same borderline stand is skipped by some and visited by others
		var borderline = Stand( price: 100, willingness: 100 );
		var outcomes = Enumerable.Range( 1, 400 ).Select( id => smarter.ScoreAttraction( context, Guest( (ulong)id ), borderline ) != 0 ).ToArray();
		Assert.IsTrue( outcomes.Count( visits => visits ) is > 100 and < 300, $"{outcomes.Count( visits => visits )} of 400 guests visit it" );
	}

	[TestMethod]
	public void TheBoothScalesOnlyTheRatingPartOfTheParkAndNeverBelowTheOriginal()
	{
		var minimum = (uint)Settings.MinimumEntryFee;
		Assert.AreEqual( minimum, SmarterGuestsRules.BoothParkValue( minimum, 5, Settings ), "no rating: the original value" );
		Assert.AreEqual( 0u, SmarterGuestsRules.BoothParkValue( 0, 5, Settings ), "below the minimum entry fee: untouched" );
		var value = minimum + 1000;
		Assert.AreEqual( value, SmarterGuestsRules.BoothParkValue( value, 0, Settings ), "no open ride or sideshow: the original value, never harsher" );
		Assert.AreEqual( value, SmarterGuestsRules.BoothParkValue( value, -4, Settings ) );
		Assert.AreEqual( minimum + 1030, SmarterGuestsRules.BoothParkValue( value, 1, Settings ), "3 percent kinder per open ride or sideshow" );
		Assert.AreEqual( minimum + 1150, SmarterGuestsRules.BoothParkValue( value, 5, Settings ), "115 percent at five" );
		Assert.AreEqual( SmarterGuestsRules.BoothParkValue( value, 5, Settings ), SmarterGuestsRules.BoothParkValue( value, 60, Settings ), "no more variety beyond five" );
		Assert.AreEqual( uint.MaxValue, SmarterGuestsRules.BoothParkValue( uint.MaxValue, 5, Settings ), "no overflow" );
	}

	[TestMethod]
	public void TheBoothNeverGetsHarsherThanTheOriginalAndMovesSomeVerdicts()
	{
		static int Harshness( BoothVerdictKind verdict ) => verdict switch { BoothVerdictKind.FarTooExpensive => 3, BoothVerdictKind.Expensive => 2, BoothVerdictKind.Fair => 1, _ => 0 };
		var smarter = new SmarterGuestDecisions();
		var context = new DecisionContext( Settings, 10, false, 3 );
		var guest = Guest();
		var moved = 0;
		foreach ( var points in new[] { 0u, 200u, 800u, 3000u } )
			foreach ( var draw in new[] { 0u, 7u, 99u } )
				for ( uint fee = 0; fee <= 1200; fee += 3 )
				{
					var original = OriginalGuestDecisions.Instance.DecideBooth( context, guest, new BoothView( points, fee, draw ) ).Verdict;
					Assert.AreEqual( original, smarter.DecideBooth( context, guest, new BoothView( points, fee, draw, 0 ) ).Verdict, "no open ride or sideshow is the original's verdict" );
					var previous = Harshness( original );
					for ( var open = 1; open <= 6; open++ )
					{
						var verdict = smarter.DecideBooth( context, guest, new BoothView( points, fee, draw, open ) ).Verdict;
						Assert.IsTrue( Harshness( verdict ) <= previous, $"points {points}, fee {fee}, draw {draw}: {open} open is harsher than {open - 1}" );
						previous = Harshness( verdict );
					}
					moved += original != smarter.DecideBooth( context, guest, new BoothView( points, fee, draw, 5 ) ).Verdict ? 1 : 0;
				}
		Assert.IsTrue( moved > 20, $"{moved} verdicts move with the park's rides" );
	}

	// ---- The run of price-emptied decisions ---------------------------------------------------

	[TestMethod]
	public void ARunCountsPerGuestEndsOnReset()
	{
		var stalls = new PriceStalls();
		Assert.AreEqual( 1, stalls.Increment( 9 ) );
		Assert.AreEqual( 2, stalls.Increment( 9 ) );
		Assert.AreEqual( 1, stalls.Increment( 3 ) );
		Assert.AreEqual( 1, stalls.Increment( 0 ), "a guest without a birth id has no run" );
		Assert.AreEqual( 2, stalls.Count );
		CollectionAssert.AreEqual( new[] { new PriceStall( 3, 1 ), new PriceStall( 9, 2 ) }, stalls.Entries.ToArray(), "birth-id order" );
		Assert.AreEqual( 2, stalls.Of( 9 ) );
		Assert.AreEqual( 0, stalls.Of( 4 ) );
		stalls.Reset( 9 );
		stalls.Reset( 77 );
		Assert.AreEqual( 0, stalls.Of( 9 ) );
		Assert.AreEqual( 1, stalls.Count );
		for ( var i = 0; i < 1000; i++ )
			stalls.Increment( 3 );
		Assert.AreEqual( PriceStalls.MaximumRun, stalls.Of( 3 ), "a run saturates" );
		stalls.Clear();
		Assert.AreEqual( 0, stalls.Count );
	}

	/// <summary>The shipped rules, but a guest goes home only after three price-emptied decisions in a row: exercises the counter the shipped limit of 1 never keeps.</summary>
	private sealed class ThreeStallsDecisions : SmarterGuestsTests.DelegatingDecisions
	{
		public ThreeStallsDecisions() : base( new SmarterGuestDecisions() ) { }
		public int Asked, WentHome;
		public int MostSeen;
		public override PriceStallDecision DecidePriceStall( in DecisionContext context, in GuestView guest, int consecutive )
		{
			Asked++;
			MostSeen = Math.Max( MostSeen, consecutive );
			var decision = consecutive >= 3 ? new PriceStallDecision( true, GuestThought.Dissatisfied ) : PriceStallDecision.Stay;
			WentHome += decision.GoHome ? 1 : 0;
			return decision;
		}
		public override bool PriceTurnedAway( in DecisionContext context, in GuestView guest, in AttractionView attraction ) => ((IGuestDecisions)new SmarterGuestDecisions()).PriceTurnedAway( context, guest, attraction );
	}

	private sealed class LeavingRecorder : IGuestObserver
	{
		public int Without;
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) { }
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) => Without += cause == DepartureCause.None ? 1 : 0;
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}

	[TestMethod]
	public void AGuestWhosePriceRuleEmptiesItsChoiceEnoughTimesInARowGoesHome()
	{
		var policy = new ThreeStallsDecisions();
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: policy );
		var state = park.Guests.SmarterGuests!;
		var recorder = new LeavingRecorder();
		park.Guests.Observer = recorder;
		var most = 0;
		for ( var tick = 0; tick < 40_000; tick++ )
		{
			park.Run( 1 );
			if ( tick % 20 != 0 )
				continue;
			most = Math.Max( most, state.Stalls.Count );
			var births = park.Guests.Guests.Select( guest => guest.BirthId ).ToHashSet();
			Assert.IsTrue( state.Stalls.Entries.All( entry => births.Contains( entry.BirthId ) ), $"tick {tick}: a run of a guest who left" );
			Assert.IsTrue( state.Stalls.Entries.All( entry => entry.Count is >= 1 and < 3 ), $"tick {tick}: {string.Join( ", ", state.Stalls.Entries )}" );
		}
		Assert.IsTrue( most > 0 && policy.MostSeen >= 3, $"runs were counted (most seen {policy.MostSeen})" );
		Assert.IsTrue( policy.WentHome > 0 && recorder.Without >= policy.WentHome, $"{policy.WentHome} guests went home, {recorder.Without} leaving reports without a predicate" );
	}

	[TestMethod]
	public void TheShippedRuleSendsAGuestHomeAtOnceAndKeepsNoRun()
	{
		Assert.AreEqual( 1, SmarterGuestsRules.StallsBeforeGoingHome );
		var recorder = new LeavingRecorder();
		var plainRecorder = new LeavingRecorder();
		var plain = new ParkScenario( new WorldSeed( 1 ) );
		plain.Guests.Observer = plainRecorder;
		plain.Run( 30_000 );
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		park.Guests.Observer = recorder;
		park.Run( 30_000 );
		Assert.IsTrue( recorder.Without > plainRecorder.Without + 5, $"{recorder.Without} guests left over prices, against {plainRecorder.Without}" );
		Assert.AreEqual( 0, park.Guests.SmarterGuests!.Stalls.Count, "with a limit of one nothing stays counted" );
	}

	[TestMethod]
	public void AParkWithARatingMovesTheBoothOfTheScenarioAndNothingElseDoesWithoutOne()
	{
		var options = new ParkScenarioOptions { ParkPoints = 400, AdmissionFee = 120 };
		var plain = new ParkScenario( new WorldSeed( 1 ), options );
		plain.Run( 12_000 );
		var identity = new ParkScenario( new WorldSeed( 1 ), options, new SmarterGuestsTests.IdentityOnlyDecisions() );
		identity.Run( 12_000 );
		Assert.AreEqual( plain.Hash, identity.Hash, "the identity state alone changes no guest" );
		var spy = new ViewSpy( asks: true, state: true );
		var counted = new ParkScenario( new WorldSeed( 1 ), options, spy );
		counted.Run( 12_000 );
		Assert.IsTrue( spy.Booths.Count > 3 && spy.Booths.All( booth => booth.ParkPoints == 400 && booth.OpenAttractions == 4 ), "the host passes the rating and the four rides" );
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var smarter = new ParkScenario( new WorldSeed( 1 ), options );
		smarter.Run( 12_000 );
		Assert.AreNotEqual( plain.Hash, smarter.Hash );
	}

	// ---- The host's facts and the off path ----------------------------------------------------

	/// <summary>Reads every view; asks for the price facts or not.</summary>
	private sealed class ViewSpy( bool asks, bool state = false ) : SmarterGuestsTests.DelegatingDecisions( OriginalGuestDecisions.Instance )
	{
		public override bool UsesValueForMoney => asks;
		public override bool UsesExtensionState => state;
		public readonly List<AttractionView> Views = new();
		public readonly List<BoothView> Booths = new();
		public override uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction ) { Views.Add( attraction ); return base.ScoreAttraction( context, guest, attraction ); }
		public override BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth ) { Booths.Add( booth ); return base.DecideBooth( context, guest, booth ); }
	}

	private sealed class CountingPayments : IGuestPayments
	{
		public int Tested;
		public int AdmissionFee => 0;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public void PriceTested( int category, int value ) => Tested++;
	}

	[TestMethod]
	public void TheOffPathFillsNoPriceFactCountsNoAttractionAndKeepsNoState()
	{
		var spy = new ViewSpy( asks: false );
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: spy );
		park.Run( 15_000 );
		Assert.IsTrue( spy.Views.Count > 100 && spy.Booths.Count > 5, "the policy was asked" );
		Assert.IsTrue( spy.Views.All( view => view.Price == 0 && view.Willingness == 0 ), "no price fact for a policy that does not ask" );
		Assert.IsTrue( spy.Booths.All( booth => booth.OpenAttractions == 0 ), "no count either" );
		Assert.IsNull( park.Guests.SmarterGuests );
		var plain = new ParkScenario( new WorldSeed( 1 ) );
		plain.Run( 15_000 );
		Assert.AreEqual( plain.Hash, park.Hash, "reading views changes nothing" );
		Assert.IsNull( SmarterGuestsSave.Capture( plain.Guests ) );
	}

	[TestMethod]
	public void ThePriceFactsAreTheHostsAndTheStandPointStatisticsAreRecordedOnceAsBefore()
	{
		var counts = new List<int>();
		var hashes = new List<ulong>();
		ViewSpy? asked = null;
		foreach ( var asks in new[] { false, true } )
		{
			var spy = new ViewSpy( asks );
			var payments = new CountingPayments();
			var park = new ParkScenario( new WorldSeed( 2 ), decisions: spy );
			park.Guests.Payments = payments;
			// the stand-point test records a perception statistic for a shop with a special ingredient (Salt changes nothing else about a guest)
			foreach ( var shop in park.Attractions.Where( item => item.Bridge.Kind == RideVisitorKind.Shop ) )
				shop.Bridge.SpecialIngredient = 2;
			park.Run( 20_000 );
			counts.Add( payments.Tested );
			hashes.Add( park.Hash );
			if ( asks )
				asked = spy;
		}
		Assert.IsTrue( counts[0] > 20, $"the stand-point test records its statistic ({counts[0]})" );
		Assert.AreEqual( counts[0], counts[1], "asking for the facts records nothing more" );
		Assert.AreEqual( hashes[0], hashes[1], "and changes nothing" );
		var views = asked!.Views;
		var shops = views.Where( view => view.Kind == RideVisitorKind.Shop && view.HasJoinCell ).ToArray();
		Assert.IsTrue( shops.Any( view => view.Price == 220 ) && shops.Any( view => view.Price == 30 ), "the priced stands carry their price" );
		Assert.IsTrue( shops.Where( view => view.Price != 0 ).All( view => view.Willingness > 0 ), "and the guest's willingness" );
		Assert.IsTrue( views.Where( view => view.Kind == RideVisitorKind.Ride ).All( view => view.Price == 0 && view.Willingness == 0 ), "rides cost nothing per use" );
		Assert.IsTrue( asked.Booths.All( booth => booth.OpenAttractions == 4 ), "the scenario's four rides are open; its shops and toilet are not counted" );
	}

	[TestMethod]
	public void TheSmarterRulesAreExercisedByThePinnedScenarioRun()
	{
		var off = new ParkScenario( new WorldSeed( 1 ) );
		var offReasons = new EndRecorder();
		off.Guests.Observer = offReasons;
		off.Run( 12_000 );
		var onReasons = new EndRecorder();
		using ( OpenTpwExtensions.Use( SmarterGuestsTests.On() ) )
		{
			var on = new ParkScenario( new WorldSeed( 1 ) );
			Assert.IsInstanceOfType( on.Guests.Decisions, typeof( SmarterGuestDecisions ) );
			on.Guests.Observer = onReasons;
			on.Run( 12_000 );
		}
		var offPrice = offReasons.Count( QueueEndReason.Price );
		var onPrice = onReasons.Count( QueueEndReason.Price );
		Assert.IsTrue( offPrice > 10, $"the default run sends {offPrice} guests away at the dear shop" );
		Assert.IsTrue( onPrice * 4 < offPrice, $"the smarter run: {onPrice}" );
	}

	private sealed class EndRecorder : IGuestObserver
	{
		private readonly Dictionary<QueueEndReason, int> reasons = new();
		public int Count( QueueEndReason reason ) => reasons.GetValueOrDefault( reason );
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) => reasons[reason] = reasons.GetValueOrDefault( reason ) + 1;
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}

	// ---- The section --------------------------------------------------------------------------

	private static ParkScenario ParkWithStalls( out SmarterGuestsSaveData section )
	{
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: new ThreeStallsDecisions() );
		park.Run( 5000 );
		var state = park.Guests.SmarterGuests!;
		var guests = park.Guests.Guests.Where( guest => guest.BirthId != 0 ).OrderBy( guest => guest.BirthId ).Take( 3 ).ToArray();
		Assert.AreEqual( 3, guests.Length );
		state.Stalls.Clear();
		state.Stalls.Increment( guests[0].BirthId );
		state.Stalls.Increment( guests[0].BirthId );
		state.Stalls.Increment( guests[2].BirthId );
		section = SmarterGuestsSave.Capture( park.Guests )!;
		return park;
	}

	private static string Json( SmarterGuestsSaveData section ) => Encoding.UTF8.GetString( ParkResumeJson.Serialize( section ) );

	private static ulong Hash( ParkScenario scenario ) => WorldStateHash.Compute( new WorldStateSources { Seed = scenario.Seed, Guests = scenario.Guests } );

	[TestMethod]
	public void ThePriceStallsRoundTripThroughTheSectionAndAreHashed()
	{
		var park = ParkWithStalls( out var section );
		Assert.AreEqual( 2, section.Stalls!.Count );
		var again = ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( section ) );
		CollectionAssert.AreEqual( section.Stalls.ToArray(), again.Stalls!.ToArray() );
		Assert.IsNull( SmarterGuestsSave.SectionProblem( again, park.Guests.Guests.Select( guest => guest.Id ) ) );
		var rebuilt = new ParkScenario( park.Seed, decisions: new ThreeStallsDecisions() );
		rebuilt.Things.RestoreSnapshot( park.Things.CaptureSnapshot() );
		rebuilt.Guests.RestoreSnapshot( park.Guests.CaptureSnapshot() );
		Assert.AreEqual( 0, rebuilt.Guests.SmarterGuests!.Stalls.Count, "a restore from the guest snapshot alone starts without runs" );
		SmarterGuestsSave.Restore( rebuilt.Guests, again );
		CollectionAssert.AreEqual( park.Guests.SmarterGuests!.Stalls.Entries.ToArray(), rebuilt.Guests.SmarterGuests.Stalls.Entries.ToArray() );
		Assert.AreEqual( Json( section ), Json( SmarterGuestsSave.Capture( rebuilt.Guests )! ), "capturing the restored park gives the same section" );
		Assert.AreEqual( Hash( park ), Hash( rebuilt ), "the restored park hashes like the saved one" );
		var before = Hash( park );
		park.Guests.SmarterGuests.Stalls.Increment( section.Stalls[0].BirthId );
		Assert.AreNotEqual( before, Hash( park ), "the runs are part of the world hash" );
	}

	[TestMethod]
	public void NoRunsLeaveTheSectionAndTheHashAsTheyWere()
	{
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: new SmarterGuestsTests.IdentityOnlyDecisions() );
		park.Run( 4000 );
		var section = SmarterGuestsSave.Capture( park.Guests )!;
		Assert.IsNull( section.Stalls );
		Assert.IsFalse( Json( section ).Contains( "Stalls" ), "no member while there is nothing counted" );
		Assert.IsNull( SmarterGuestsSave.SectionProblem( section with { Stalls = Array.Empty<PriceStall>() }, park.Guests.Guests.Select( guest => guest.Id ) ), "an empty list is also fine" );
		Assert.IsNull( ParkResumeJson.Deserialize<SmarterGuestsSaveData>( Encoding.UTF8.GetBytes( Json( section ) ) ).Stalls, "a section from before the runs reads as none" );
	}

	[TestMethod]
	public void TheRunsAreValidatedStrictly()
	{
		var park = ParkWithStalls( out var section );
		var ids = park.Guests.Guests.Select( guest => guest.Id ).ToArray();
		var good = section.Stalls!.ToArray();
		string? Problem( params PriceStall[] stalls ) => SmarterGuestsSave.SectionProblem( section with { Stalls = stalls }, ids );
		Assert.IsNull( Problem( good ) );
		StringAssert.Contains( Problem( good[0] with { BirthId = 987654 } )!, "whom the section does not hold" );
		StringAssert.Contains( Problem( good[0] with { Count = 0 } )!, "outside 1..255" );
		StringAssert.Contains( Problem( good[0] with { Count = 256 } )!, "outside 1..255" );
		Assert.IsNull( Problem( good[0] with { Count = 255 } ) );
		StringAssert.Contains( Problem( good[1], good[0] )!, "strictly ascending", "out of order" );
		StringAssert.Contains( Problem( good[0], good[0] )!, "strictly ascending", "a guest twice" );
		var restoreInto = new ParkScenario( park.Seed, decisions: new ThreeStallsDecisions() );
		restoreInto.Things.RestoreSnapshot( park.Things.CaptureSnapshot() );
		restoreInto.Guests.RestoreSnapshot( park.Guests.CaptureSnapshot() );
		var bad = Assert.ThrowsException<InvalidDataException>( () => SmarterGuestsSave.Restore( restoreInto.Guests, section with { Stalls = [good[0] with { Count = 0 }] } ) );
		StringAssert.Contains( bad.Message, "inconsistent" );
		Assert.AreEqual( 0, restoreInto.Guests.SmarterGuests!.Stalls.Count, "a refused section changes nothing" );
		var withExtra = Json( section ).Replace( "\"Count\"", "\"Extra\":1,\"Count\"" );
		Assert.ThrowsException<System.Text.Json.JsonException>( () => ParkResumeJson.Deserialize<SmarterGuestsSaveData>( Encoding.UTF8.GetBytes( withExtra ) ) );
	}

	/// <summary>The section, the first-tick bootstrap and the world hash on the shipped policy, which the identity-only policy of the other tests stands in for where they compare guests to the off run.</summary>
	[TestMethod]
	public void TheSectionTheBootstrapAndTheHashHoldOnTheShippedPolicyToo()
	{
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		Assert.IsInstanceOfType( park.Guests.Decisions, typeof( SmarterGuestDecisions ) );
		park.Run( 9000 );
		Assert.IsTrue( park.Guests.Guests.Count > 3 && park.Guests.Guests.All( guest => guest.BirthId != 0 ) );
		var section = SmarterGuestsSave.Capture( park.Guests )!;
		Assert.IsNull( SmarterGuestsSave.SectionProblem( section, park.Guests.Guests.Select( guest => guest.Id ) ) );
		var fresh = SmarterGuestsTests.Fresh( park );
		SmarterGuestsSave.Restore( fresh.Guests, ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( section ) ) );
		Assert.AreEqual( Hash( park ), Hash( fresh ), "the restored park hashes like the saved one" );
		Assert.AreEqual( Json( section ), Json( SmarterGuestsSave.Capture( fresh.Guests )! ) );
		// a restore from the guest snapshot alone has no ids: the first tick gives them in thing-list order, the same twice
		ulong[] Bootstrapped()
		{
			var again = SmarterGuestsTests.Fresh( park );
			Assert.IsTrue( again.Guests.Guests.All( guest => guest.BirthId == 0 ) );
			again.Run( 1 );
			Assert.IsTrue( again.Guests.SmarterGuests!.Bootstrapped && again.Guests.Guests.All( guest => guest.BirthId != 0 ) );
			return again.Guests.Guests.OrderBy( guest => guest.Id ).Select( guest => guest.BirthId ).ToArray();
		}
		CollectionAssert.AreEqual( Bootstrapped(), Bootstrapped() );
		Assert.AreNotEqual( Hash( park ), WorldStateHash.Compute( new WorldStateSources { Seed = park.Seed, Guests = new ParkScenario( park.Seed ).Guests } ) );
	}
}
