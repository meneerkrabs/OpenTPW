using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Phase 2 of the smarter-guests plan (docs/SMARTER-GUESTS.md): value for money. The rules in isolation, the host's price facts, the price memory with its expiry, its save section and its validation, and
/// the off path. The measured effect is in the page's tables and in the compare test of <see cref="SmarterGuestsTests"/>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SmarterGuestsValueTests
{
	private static readonly GuestSettings Settings = GuestTests.CreateSettings();

	private static GuestView Guest( ulong birthId = 11, int money = 500, float happiness = 60, float thirst = 90 ) =>
		new( 7, birthId, 0, happiness, thirst, 10, 10, 0, money, 3, 3, Array.AsReadOnly( new int[4] ), Array.AsReadOnly( new int[4] ) );

	private static AttractionView Stand( RideVisitorKind kind = RideVisitorKind.Shop, int price = 100, uint willingness = 300, bool refused = false ) =>
		new( 5, 1, kind, true, (3, 4), (3, 5), 0, 0, 4, 20, 50, 0, 80, 0, false, 30, false, 0, 0, true, false, false, price, willingness, refused );

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
		Assert.AreEqual( 0u, smarter.ScoreAttraction( context, guest, Stand( refused: true ) ), "remembered refusal" );
		Assert.AreEqual( 0u, smarter.ScoreAttraction( context, guest, Stand( RideVisitorKind.Sideshow, 400, 300 ) ), "a sideshow too" );
		foreach ( var kind in new[] { RideVisitorKind.Ride } )
			Assert.AreEqual( original.ScoreAttraction( context, guest, Stand( kind, 400, 300, refused: true ) ), smarter.ScoreAttraction( context, guest, Stand( kind, 400, 300, refused: true ) ), "rides cost nothing per use: no price rule" );
		Assert.AreEqual( original.ScoreAttraction( context, guest, Stand( price: 0, willingness: 0, refused: true ) ), smarter.ScoreAttraction( context, guest, Stand( price: 0, willingness: 0, refused: true ) ), "a free stand is never refused" );
		// the trait scales the willingness: across many guests the same borderline stand is skipped by some and visited by others
		var borderline = Stand( price: 100, willingness: 100 );
		var outcomes = Enumerable.Range( 1, 400 ).Select( id => smarter.ScoreAttraction( context, Guest( (ulong)id ), borderline ) != 0 ).ToArray();
		Assert.IsTrue( outcomes.Count( visits => visits ) is > 100 and < 300, $"{outcomes.Count( visits => visits )} of 400 guests visit it" );
	}

	[TestMethod]
	public void TheBoothScalesOnlyTheRatingPartOfThePark()
	{
		var minimum = (uint)Settings.MinimumEntryFee;
		Assert.AreEqual( minimum, SmarterGuestsRules.BoothParkValue( minimum, 0, Settings ), "no rating: the original value" );
		Assert.AreEqual( 0u, SmarterGuestsRules.BoothParkValue( 0, 6, Settings ), "below the minimum entry fee: untouched" );
		var value = minimum + 1000;
		Assert.AreEqual( minimum + 850, SmarterGuestsRules.BoothParkValue( value, 0, Settings ), "85 percent with no open attraction" );
		Assert.AreEqual( minimum + 1000, SmarterGuestsRules.BoothParkValue( value, 3, Settings ), "neutral at three" );
		Assert.AreEqual( minimum + 1150, SmarterGuestsRules.BoothParkValue( value, 6, Settings ), "115 percent at six" );
		Assert.AreEqual( SmarterGuestsRules.BoothParkValue( value, 6, Settings ), SmarterGuestsRules.BoothParkValue( value, 60, Settings ), "no more variety beyond six" );
		Assert.AreEqual( SmarterGuestsRules.BoothParkValue( value, 0, Settings ), SmarterGuestsRules.BoothParkValue( value, -4, Settings ) );
		Assert.AreEqual( uint.MaxValue, SmarterGuestsRules.BoothParkValue( uint.MaxValue, 6, Settings ), "no overflow" );
	}

	[TestMethod]
	public void TheBoothNeverGetsHarsherWithMoreOpenAttractionsAndMovesSomeVerdicts()
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
					var few = smarter.DecideBooth( context, guest, new BoothView( points, fee, draw, 0 ) ).Verdict;
					var many = smarter.DecideBooth( context, guest, new BoothView( points, fee, draw, 6 ) ).Verdict;
					Assert.IsTrue( Harshness( many ) <= Harshness( few ), $"points {points}, fee {fee}, draw {draw}: {few} with none open, {many} with six" );
					moved += few != many ? 1 : 0;
					Assert.AreEqual( OriginalGuestDecisions.Instance.DecideBooth( context, guest, new BoothView( points, fee, draw ) ).Verdict,
						smarter.DecideBooth( context, guest, new BoothView( points, fee, draw, 3 ) ).Verdict, "three open attractions are neutral" );
				}
		Assert.IsTrue( moved > 20, $"{moved} verdicts move with the park's variety" );
	}

	// ---- The memory ---------------------------------------------------------------------------

	[TestMethod]
	public void ARefusalIsRememberedUntilItsTurnAndThenForgotten()
	{
		var memory = new PriceMemory();
		memory.Remember( 5, 40, 100 );
		Assert.IsTrue( memory.Refused( 5, 40, 99 ) );
		Assert.IsFalse( memory.Refused( 5, 40, 100 ), "the memory holds while the turn is below the expiry" );
		Assert.IsFalse( memory.Refused( 5, 41, 50 ), "another attraction" );
		Assert.IsFalse( memory.Refused( 6, 40, 50 ), "another guest" );
		Assert.IsFalse( memory.Refused( 0, 40, 50 ), "a guest without a birth id remembers nothing" );
		memory.Remember( 0, 40, 100 );
		Assert.AreEqual( 1, memory.Count );
		memory.Remember( 5, 40, 90 );
		Assert.IsTrue( memory.Refused( 5, 40, 99 ), "an earlier expiry never shortens a memory" );
		memory.Remember( 5, 40, 150 );
		Assert.AreEqual( 1, memory.Count, "the same refusal again extends the memory" );
		Assert.IsTrue( memory.Refused( 5, 40, 149 ) );
		memory.Prune( 149 );
		Assert.AreEqual( 1, memory.Count );
		memory.Prune( 150 );
		Assert.AreEqual( 0, memory.Count, "pruned on the expiry turn" );
	}

	[TestMethod]
	public void TheMemoryIsBoundedPerGuestKeepsCanonicalOrderAndForgetsADepartedGuest()
	{
		var memory = new PriceMemory();
		foreach ( var (guest, attraction, expires) in new[] { (9UL, 30, 100L), (3UL, 31, 100), (9UL, 20, 90), (3UL, 11, 100), (9UL, 25, 110), (9UL, 26, 120), (9UL, 27, 130) } )
			memory.Remember( guest, attraction, expires );
		CollectionAssert.AreEqual( memory.Entries.OrderBy( entry => entry.BirthId ).ThenBy( entry => entry.AttractionId ).ToArray(), memory.Entries.ToArray(), "canonical order" );
		var nine = memory.Entries.Where( entry => entry.BirthId == 9 ).ToArray();
		Assert.AreEqual( SmarterGuestsRules.MaxRefusalsPerGuest, nine.Length, "at most four per guest" );
		Assert.IsFalse( nine.Any( entry => entry.AttractionId == 20 ), "the refusal that ended soonest went first" );
		Assert.AreEqual( 2, memory.Entries.Count( entry => entry.BirthId == 3 ) );
		memory.Forget( 9 );
		Assert.IsTrue( memory.Entries.All( entry => entry.BirthId == 3 ) );
		memory.Forget( 77 );
		Assert.AreEqual( 2, memory.Count );
		memory.Clear();
		Assert.AreEqual( 0, memory.Count );
	}

	/// <summary>Skips a stand the guest remembers a refusal at, and does nothing else: the memory alone, with the stand-point test as the only teacher.</summary>
	private sealed class MemoryOnlyDecisions : SmarterGuestsTests.DelegatingDecisions
	{
		public MemoryOnlyDecisions() : base( OriginalGuestDecisions.Instance ) { }
		public override bool UsesExtensionState => true;
		public override bool UsesValueForMoney => true;
		public readonly List<AttractionView> Stands = new();
		public override uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction )
		{
			if ( attraction.Kind == RideVisitorKind.Shop && attraction.Price != 0 )
				Stands.Add( attraction );
			return attraction.RecentlyRefused ? 0 : base.ScoreAttraction( context, guest, attraction );
		}
	}

	[TestMethod]
	public void TheHostRemembersWhereAGuestWasRefusedForTheTurnsTheRuleSetsAndForgetsItAfterwards()
	{
		var policy = new MemoryOnlyDecisions();
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: policy );
		var state = park.Guests.SmarterGuests!;
		var dear = park.Attractions.Single( item => item.Role == "shop-dear" ).Bridge.AttractionId;
		PriceRefusal? first = null;
		long rememberedAt = 0;
		for ( var tick = 0; tick < 40_000 && first == null; tick++ )
		{
			park.Run( 1 );
			if ( state.Memory.Count > 0 )
			{
				first = state.Memory.Entries[0];
				rememberedAt = park.Guests.ParkTurn;
			}
		}
		Assert.IsNotNull( first, "a guest was refused at the dear shop and the host remembered it" );
		var entry = first.Value;
		Assert.AreEqual( dear, entry.AttractionId, "the dear shop refuses; the cheap one does not" );
		Assert.IsTrue( entry.ExpiresTurn is > 0 && entry.ExpiresTurn - rememberedAt <= SmarterGuestsRules.RefusalMemoryTurns && entry.ExpiresTurn > rememberedAt, $"expires on {entry.ExpiresTurn}, remembered on turn {rememberedAt}" );
		Assert.IsTrue( park.Guests.Guests.Any( guest => guest.BirthId == entry.BirthId ), "the memory is of a guest in the park" );
		// while it holds, the policy is told, with the same stand every time
		var seenRefused = false;
		var sawStandAfterExpiry = false;
		while ( park.Guests.ParkTurn < entry.ExpiresTurn + 2 )
		{
			policy.Stands.Clear();
			park.Run( 1 );
			var guest = park.Guests.Guests.SingleOrDefault( item => item.BirthId == entry.BirthId );
			if ( guest == null )
				break;
			seenRefused |= park.Guests.ParkTurn < entry.ExpiresTurn && policy.Stands.Any( stand => stand.Id == dear && stand.RecentlyRefused );
			if ( park.Guests.ParkTurn >= entry.ExpiresTurn )
				sawStandAfterExpiry |= policy.Stands.Any( stand => stand.Id == dear );
			if ( park.Guests.ParkTurn >= entry.ExpiresTurn )
				Assert.IsFalse( state.Memory.Entries.Any( other => other == entry ), "the entry is gone once its turn has come" );
		}
		Assert.IsFalse( state.Memory.Entries.Any( other => other == entry ), "the entry is gone once its turn has come" );
		Assert.IsTrue( seenRefused || park.Guests.Guests.All( guest => guest.BirthId != entry.BirthId ), "the memory reached the policy while it held" );
		_ = sawStandAfterExpiry;
	}

	[TestMethod]
	public void TheMemoryNeverHoldsMoreThanTheGuestsInTheParkCanCarry()
	{
		var park = new ParkScenario( new WorldSeed( 3 ), decisions: new MemoryOnlyDecisions() );
		var state = park.Guests.SmarterGuests!;
		var most = 0;
		for ( var tick = 0; tick < 60_000; tick++ )
		{
			park.Run( 1 );
			if ( tick % 40 != 0 )
				continue;
			most = Math.Max( most, state.Memory.Count );
			var births = park.Guests.Guests.Select( guest => guest.BirthId ).ToHashSet();
			Assert.IsTrue( state.Memory.Entries.All( entry => births.Contains( entry.BirthId ) ), $"tick {tick}: an entry of a guest who left" );
			Assert.IsTrue( state.Memory.Count <= SmarterGuestsRules.MaxRefusalsPerGuest * park.Guests.Guests.Count );
			Assert.IsTrue( state.Memory.Entries.All( entry => entry.ExpiresTurn > park.Guests.ParkTurn ), $"tick {tick}: an expired entry stayed" );
		}
		Assert.IsTrue( most > 0, "the memory was used" );
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
	public void TheOffPathFillsNoPriceFactCountsNoAttractionAndKeepsNoMemory()
	{
		var spy = new ViewSpy( asks: false );
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: spy );
		park.Run( 15_000 );
		Assert.IsTrue( spy.Views.Count > 100 && spy.Booths.Count > 5, "the policy was asked" );
		Assert.IsTrue( spy.Views.All( view => view.Price == 0 && view.Willingness == 0 && !view.RecentlyRefused ), "no price fact for a policy that does not ask" );
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
		foreach (var asks in new[] { false, true })
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
		Assert.IsTrue( asked.Booths.All( booth => booth.OpenAttractions == 7 ), "the scenario's seven attractions are open" );
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

	private static ParkScenario ParkWithMemory( out SmarterGuestsSaveData section )
	{
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: new MemoryOnlyDecisions() );
		park.Run( 5000 );
		var state = park.Guests.SmarterGuests!;
		var guests = park.Guests.Guests.Where( guest => guest.BirthId != 0 ).Take( 3 ).ToArray();
		Assert.AreEqual( 3, guests.Length );
		state.Memory.Clear();
		state.Memory.Remember( guests[0].BirthId, 8, park.Guests.ParkTurn + 30 );
		state.Memory.Remember( guests[0].BirthId, 9, park.Guests.ParkTurn + 40 );
		state.Memory.Remember( guests[2].BirthId, 8, park.Guests.ParkTurn + 12 );
		section = SmarterGuestsSave.Capture( park.Guests )!;
		return park;
	}

	private static string Json( SmarterGuestsSaveData section ) => Encoding.UTF8.GetString( ParkResumeJson.Serialize( section ) );

	[TestMethod]
	public void ThePriceMemoryRoundTripsThroughTheSectionAndIsHashed()
	{
		var park = ParkWithMemory( out var section );
		Assert.AreEqual( 3, section.Refusals!.Count );
		var again = ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( section ) );
		CollectionAssert.AreEqual( section.Refusals.ToArray(), again.Refusals!.ToArray() );
		Assert.IsNull( SmarterGuestsSave.SectionProblem( again, park.Guests.Guests.Select( guest => guest.Id ), park.Guests.ParkTurn ) );

		var rebuilt = new ParkScenario( park.Seed, decisions: new MemoryOnlyDecisions() );
		rebuilt.Things.RestoreSnapshot( park.Things.CaptureSnapshot() );
		rebuilt.Guests.RestoreSnapshot( park.Guests.CaptureSnapshot() );
		Assert.AreEqual( 0, rebuilt.Guests.SmarterGuests!.Memory.Count, "a restore from the guest snapshot alone starts without a memory" );
		SmarterGuestsSave.Restore( rebuilt.Guests, again );
		CollectionAssert.AreEqual( park.Guests.SmarterGuests!.Memory.Entries.ToArray(), rebuilt.Guests.SmarterGuests.Memory.Entries.ToArray() );
		Assert.AreEqual( Json( section ), Json( SmarterGuestsSave.Capture( rebuilt.Guests )! ), "capturing the restored park gives the same section" );
		ulong Hash( ParkScenario scenario ) => WorldStateHash.Compute( new WorldStateSources { Seed = scenario.Seed, Guests = scenario.Guests } );
		Assert.AreEqual( Hash( park ), Hash( rebuilt ), "the restored park hashes like the saved one" );
		var before = Hash( park );
		park.Guests.SmarterGuests.Memory.Remember( section.Refusals[0].BirthId, 77, park.Guests.ParkTurn + 5 );
		Assert.AreNotEqual( before, Hash( park ), "the memory is part of the world hash" );
		park.Guests.SmarterGuests.Memory.Forget( section.Refusals[0].BirthId );
		Assert.AreNotEqual( before, Hash( park ) );
	}

	[TestMethod]
	public void AnEmptyMemoryLeavesTheSectionAndTheHashAsTheyWere()
	{
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: new SmarterGuestsTests.IdentityOnlyDecisions() );
		park.Run( 4000 );
		var section = SmarterGuestsSave.Capture( park.Guests )!;
		Assert.IsNull( section.Refusals );
		Assert.IsFalse( Json( section ).Contains( "Refusals" ), "no member while there is nothing to remember" );
		Assert.IsNull( SmarterGuestsSave.SectionProblem( section with { Refusals = Array.Empty<PriceRefusal>() }, park.Guests.Guests.Select( guest => guest.Id ) ), "an empty list is also fine" );
		// a section from before the memory (no member at all) is read as an empty memory
		Assert.IsNull( ParkResumeJson.Deserialize<SmarterGuestsSaveData>( Encoding.UTF8.GetBytes( Json( section ) ) ).Refusals );
	}

	[TestMethod]
	public void TheMemoryIsValidatedStrictly()
	{
		var park = ParkWithMemory( out var section );
		var ids = park.Guests.Guests.Select( guest => guest.Id ).ToArray();
		var turn = park.Guests.ParkTurn;
		var good = section.Refusals!.ToArray();
		var first = good[0];
		string? Problem( params PriceRefusal[] refusals ) => SmarterGuestsSave.SectionProblem( section with { Refusals = refusals }, ids, turn );
		Assert.IsNull( Problem( good ) );
		StringAssert.Contains( Problem( good[0] with { BirthId = 987654 } )!, "whom the section does not hold" );
		StringAssert.Contains( Problem( first with { AttractionId = 0 } )!, "outside 1..10239" );
		StringAssert.Contains( Problem( first with { AttractionId = 10240 } )!, "outside 1..10239" );
		StringAssert.Contains( Problem( first with { ExpiresTurn = 0 } )!, "before turn 1" );
		StringAssert.Contains( Problem( first with { ExpiresTurn = turn + SmarterGuestsRules.RefusalMemoryTurns + 1 } )!, "turns after turn" );
		Assert.IsNull( Problem( first with { ExpiresTurn = turn + SmarterGuestsRules.RefusalMemoryTurns } ), "the longest a memory can last" );
		StringAssert.Contains( Problem( good[1], good[0] )!, "strictly ascending", "out of order" );
		StringAssert.Contains( Problem( good[0], good[0] )!, "strictly ascending", "a duplicate" );
		// more than four for one guest, in canonical order
		var many = Enumerable.Range(1, SmarterGuestsRules.MaxRefusalsPerGuest + 1).Select( attraction => new PriceRefusal( first.BirthId, attraction, turn + 10 ) ).ToArray();
		StringAssert.Contains( Problem( many )!, "more than 4 refusals" );
		Assert.IsNull( Problem( many.Take( SmarterGuestsRules.MaxRefusalsPerGuest ).ToArray() ) );
		// a restore applies the same checks, and the strict codec refuses an unknown member
		var restoreInto = new ParkScenario( park.Seed, decisions: new MemoryOnlyDecisions() );
		restoreInto.Things.RestoreSnapshot( park.Things.CaptureSnapshot() );
		restoreInto.Guests.RestoreSnapshot( park.Guests.CaptureSnapshot() );
		var bad = Assert.ThrowsException<InvalidDataException>( () => SmarterGuestsSave.Restore( restoreInto.Guests, section with { Refusals = [first with { ExpiresTurn = turn + 500 }] } ) );
		StringAssert.Contains( bad.Message, "inconsistent" );
		Assert.AreEqual( 0, restoreInto.Guests.SmarterGuests!.Memory.Count, "a refused section changes nothing" );
		var withExtra = Json( section ).Replace( "\"AttractionId\"", "\"Extra\":1,\"AttractionId\"" );
		Assert.ThrowsException<System.Text.Json.JsonException>( () => ParkResumeJson.Deserialize<SmarterGuestsSaveData>( Encoding.UTF8.GetBytes( withExtra ) ) );
	}
}
