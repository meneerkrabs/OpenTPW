using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Phase 4 of the smarter-guests plan (docs/SMARTER-GUESTS.md): queue patience. A guest does not join a queue whose expected wait is above its patience when another ride would be clearly quicker, and gives up
/// after waiting too long under the same condition. The rules are stateless (the patience is a trait of the guest), so there is nothing to save; the measured effect is in the page's tables.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SmarterGuestsPatienceTests
{
	private static readonly GuestSettings Settings = GuestTests.CreateSettings();
	private static readonly SmarterGuestDecisions Smarter = new();

	private static GuestView Guest( ulong birthId = 11 ) =>
		new( 7, birthId, 0, 60, 10, 10, 10, 0, 500, 3, 3, Array.AsReadOnly( new int[4] ), Array.AsReadOnly( new int[4] ) );

	private static AttractionView Ride( int queue = 0, int reservations = 0, long interval = 30 * 256, RideVisitorKind kind = RideVisitorKind.Ride ) =>
		new( 5, 1, kind, true, (3, 4), (3, 5), 0, queue, 4, 20, 70, 1, 0, 0, kind == RideVisitorKind.Facility, 30, false, 0, 0, true, false, false, 0, 0, reservations, interval );

	/// <summary>A birth id whose patience trait gives exactly this percentage under park seed 3.</summary>
	private static ulong GuestWithPatience( int percent )
	{
		for ( ulong id = 1; id < 100_000; id++ )
			if ( SmarterGuestsRules.PatiencePercent( SmarterGuestsState.Trait( 3, id, SmarterGuestsRules.PatienceTrait ) ) == percent )
				return id;
		throw new AssertFailedException( $"no guest has a patience of {percent}" );
	}

	private static DecisionContext Context( long turn = 500 ) => new( Settings, turn, false, 3 );

	// ---- The band -----------------------------------------------------------------------------

	[TestMethod]
	public void ThePatienceBandIsSeventyToOneHundredThirtyPercentAndCoversEveryValue()
	{
		var seen = new HashSet<int>();
		for ( ulong id = 1; id <= 20_000; id++ )
		{
			var percent = SmarterGuestsRules.PatiencePercent( SmarterGuestsState.Trait( 3, id, SmarterGuestsRules.PatienceTrait ) );
			Assert.IsTrue( percent is >= 70 and <= 130, $"{percent}" );
			seen.Add( percent );
		}
		Assert.AreEqual( SmarterGuestsRules.PatienceValues, seen.Count, "every value occurs" );
		Assert.AreEqual( 70, SmarterGuestsRules.PatiencePercent( 0 ) );
		Assert.AreNotEqual( SmarterGuestsRules.PreferenceTrait, SmarterGuestsRules.PatienceTrait );
		Assert.AreNotEqual( SmarterGuestsRules.PriceSensitivityTrait, SmarterGuestsRules.PatienceTrait );
		Assert.AreEqual( SmarterGuestsRules.PatiencePercent( SmarterGuestsState.Trait( 3, 9, SmarterGuestsRules.PatienceTrait ) ), SmarterGuestsRules.PatiencePercent( SmarterGuestsState.Trait( 3, 9, SmarterGuestsRules.PatienceTrait ) ), "stable for the visit" );
	}

	[TestMethod]
	public void TheBaseIsTheMeasuredBaselineNotTheBoredomThreshold()
	{
		// the original's boarded waits in the scenario: p90 73 and p99 108 turns; the give-up lies above the baseline's p99 and above the join patience
		Assert.IsTrue( SmarterGuestsRules.JoinPatienceTurns is > 73 and < 108 );
		Assert.IsTrue( SmarterGuestsRules.GiveUpTurns > 108 );
		Assert.IsTrue( SmarterGuestsRules.GiveUpTurns > SmarterGuestsRules.JoinPatienceTurns );
		Assert.AreEqual( 90 * 256 * 130 / 100L, SmarterGuestsRules.PatienceTurns256( 90, 130 ) );
	}

	// ---- Rule 1: do not join ------------------------------------------------------------------

	private static AdmissionView Admission( AttractionView ride, long alternative ) => new( ride, 50, 10, ride.QueueLength, 100, alternative );

	[TestMethod]
	public void AGuestDoesNotJoinAQueueLongerThanItsPatienceWhenAnotherRideIsClearlyQuicker()
	{
		var guest = Guest( GuestWithPatience( 100 ) );
		var ride = Ride( queue: 4, interval: 30 * 256 ); // 120 turns for this guest, patience 90
		Assert.AreEqual( QueueJoinResult.QueueTooLong, Smarter.DecideAdmission( Context(), guest, Admission( ride, 20 * 256 ) ).Result, "the queue too long refusal and thought, as the original's own" );
		Assert.IsTrue( Smarter.DecideAdmission( Context(), guest, Admission( ride, -1 ) ).Admits, "nowhere better to go: the guest joins" );
		Assert.IsTrue( Smarter.DecideAdmission( Context(), guest, Admission( ride, 110 * 256 ) ).Admits, "an alternative that is no quicker is no reason" );
		Assert.IsTrue( Smarter.DecideAdmission( Context(), guest, Admission( Ride( queue: 2 ), 0 ) ).Admits, "60 turns is within the patience of 90" );
	}

	[TestMethod]
	public void ThePatienceBandMovesTheLimitAndWalkersOnTheirWayDoNotCountAsGuestsAhead()
	{
		var patient = Guest( GuestWithPatience( 130 ) ); // 117 turns
		var impatient = Guest( GuestWithPatience( 70 ) ); // 63 turns
		var calm = Ride( queue: 3, interval: 30 * 256 ); // 3 guests ahead: 90 turns
		Assert.IsFalse( Smarter.DecideAdmission( Context(), impatient, Admission( calm, 0 ) ).Admits, "the impatient guest's limit is 63 turns" );
		Assert.IsTrue( Smarter.DecideAdmission( Context(), patient, Admission( calm, 0 ) ).Admits );
		Assert.IsFalse( Smarter.DecideAdmission( Context(), patient, Admission( Ride( queue: 4, interval: 30 * 256 ), 0 ) ).Admits, "4 ahead is 120 turns, above 117" );
		// the guest stands on the queue's last cell when it is admitted: guests still walking to the ride will stand behind it, so they add nothing to its wait
		var walkers = Ride( queue: 3, reservations: 5, interval: 30 * 256 );
		Assert.IsTrue( Smarter.DecideAdmission( Context(), patient, Admission( walkers, 0 ) ).Admits, "5 walkers behind the guest do not lengthen its wait" );
		Assert.AreEqual( Smarter.DecideAdmission( Context(), impatient, Admission( calm, 0 ) ), Smarter.DecideAdmission( Context(), impatient, Admission( walkers, 0 ) ) );
	}

	[TestMethod]
	public void TheOriginalRefusalsComeFirstAndOnlyRidesAreConcerned()
	{
		var guest = Guest();
		var tooScary = new AdmissionView( Ride( queue: 1 ), 50, 46, 1, 100, 0 );
		Assert.AreEqual( OriginalGuestDecisions.Instance.DecideAdmission( Context(), guest, tooScary ), Smarter.DecideAdmission( Context(), guest, tooScary ), "the excitement gate is the original's" );
		var full = new AdmissionView( Ride( queue: 20 ), 50, 10, 20, 20, 0 );
		Assert.AreEqual( QueueJoinResult.QueueTooLong, Smarter.DecideAdmission( Context(), guest, full ).Result );
		foreach ( var kind in new[] { RideVisitorKind.Facility, RideVisitorKind.Shop, RideVisitorKind.Sideshow } )
			Assert.IsTrue( Smarter.DecideAdmission( Context(), guest, Admission( Ride( queue: 9, kind: kind ), 0 ) ).Admits, $"{kind}: a service the guest wants for its own reason" );
		Assert.IsTrue( Smarter.DecideAdmission( Context(), guest, Admission( Ride( queue: 9, interval: 0 ), 0 ) ).Admits, "no estimate: no judgement" );
	}

	// ---- Rule 2: give up ----------------------------------------------------------------------

	private static QueuePatienceView Queue( AttractionView ride, long joined, int position, long alternative ) => new( ride, joined, position, ride.QueueLength, alternative );

	[TestMethod]
	public void AGuestGivesUpAfterWaitingLongerThanItsPatienceWhenAnotherRideIsClearlyQuicker()
	{
		var guest = Guest( GuestWithPatience( 100 ) );
		var ride = Ride( queue: 6, interval: 20 * 256 ); // the guest's remaining wait from position 5 is 100 turns
		var before = Smarter.DecideQueuePatience( Context( 120 ), guest, Queue( ride, 0, 5, 10 * 256 ) );
		Assert.IsFalse( before.Leave, "120 turns is not above 120" );
		var after = Smarter.DecideQueuePatience( Context( 121 ), guest, Queue( ride, 0, 5, 10 * 256 ) );
		Assert.IsTrue( after.Leave );
		Assert.AreEqual( GuestThought.QueueTooLong, after.Thought, "the waited-too-long thought of the original's own set" );
		Assert.IsFalse( Smarter.DecideQueuePatience( Context( 400 ), guest, Queue( ride, 0, 5, -1 ) ).Leave, "no other ride to go to" );
		Assert.IsFalse( Smarter.DecideQueuePatience( Context( 400 ), guest, Queue( ride, 0, 5, 90 * 256 ) ).Leave, "the other ride is not clearly quicker" );
		Assert.IsFalse( Smarter.DecideQueuePatience( Context( 400 ), guest, Queue( Ride( queue: 6, interval: 20 * 256 ), 0, 1, 18 * 256 ) ).Leave, "almost at the front: the remaining wait is short, and the other ride is no quicker" );
	}

	[TestMethod]
	public void TheFrontOfTheQueueAServiceAndAGuestWithoutAJoinTurnNeverGiveUp()
	{
		var guest = Guest();
		var ride = Ride( queue: 6, interval: 20 * 256 );
		Assert.IsFalse( Smarter.DecideQueuePatience( Context( 900 ), guest, Queue( ride, 0, 0, 0 ) ).Leave, "the guest at the head is about to be called" );
		Assert.IsFalse( Smarter.DecideQueuePatience( Context( 900 ), guest, Queue( ride, -1, 5, 0 ) ).Leave, "no join turn: nothing to count from" );
		foreach ( var kind in new[] { RideVisitorKind.Facility, RideVisitorKind.Shop, RideVisitorKind.Sideshow } )
			Assert.IsFalse( Smarter.DecideQueuePatience( Context( 900 ), guest, Queue( Ride( queue: 6, kind: kind ), 0, 5, 0 ) ).Leave, $"{kind}" );
	}

	[TestMethod]
	public void ThePatienceBandMovesTheGiveUpTurn()
	{
		var ride = Ride( queue: 9, interval: 20 * 256 );
		var (patient, impatient) = (Guest( GuestWithPatience( 130 ) ), Guest( GuestWithPatience( 70 ) ));
		Assert.IsTrue( Smarter.DecideQueuePatience( Context( 90 ), impatient, Queue( ride, 0, 8, 0 ) ).Leave, "70 percent of 120 is 84 turns" );
		Assert.IsFalse( Smarter.DecideQueuePatience( Context( 90 ), patient, Queue( ride, 0, 8, 0 ) ).Leave );
		Assert.IsFalse( Smarter.DecideQueuePatience( Context( 156 ), patient, Queue( ride, 0, 8, 0 ) ).Leave, "130 percent of 120 is 156 turns" );
		Assert.IsTrue( Smarter.DecideQueuePatience( Context( 157 ), patient, Queue( ride, 0, 8, 0 ) ).Leave );
		// the host scans the park's rides for a guest only from its own give-up turn: 84 to 156 turns across the band
		Assert.AreEqual( 84, Smarter.AlternativeLookAfterTurns( Context(), impatient ) );
		Assert.AreEqual( 156, Smarter.AlternativeLookAfterTurns( Context(), patient ) );
		Assert.AreEqual( -1, ((IGuestDecisions)OriginalGuestDecisions.Instance).AlternativeLookAfterTurns( Context(), patient ) );
	}

	// ---- The host -----------------------------------------------------------------------------

	private sealed class Spy : SmarterGuestsTests.DelegatingDecisions
	{
		private readonly int look;
		public readonly List<(long Alternative, GuestState State, bool Interlude, bool Called, int Position)> Patience = new();
		public readonly List<(long Alternative, int Ride, int[] Histories)> Admissions = new();
		public Func<int, Guest?> Find = _ => null;
		public Spy( int look ) : base( new SmarterGuestDecisions() ) => this.look = look;
		public override int AlternativeLookAfterTurns( in DecisionContext context, in GuestView guest ) => look;
		/// <summary>The spy sees every call the hook could receive: it is never skipped.</summary>
		public override int QueuePatienceQuietTurns( in DecisionContext context, ulong birthId ) => -1;
		public override QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission )
		{
			Admissions.Add( (admission.BestAlternativeWait256, admission.Attraction.Id, guest.UsedHistory.Concat( guest.TemporaryHistory ).Where( id => id != 0 ).ToArray()) );
			return base.DecideAdmission( context, guest, admission );
		}
		public override QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue )
		{
			var host = Find( guest.Id );
			Patience.Add( (queue.BestAlternativeWait256, host?.State ?? GuestState.Gone, host?.InQueueInterlude ?? false, host?.QueueCalled ?? false, queue.Position) );
			return base.DecideQueuePatience( context, guest, queue );
		}
	}

	private sealed class Recorder : IGuestObserver
	{
		public readonly List<(QueueEndReason Reason, int Attraction, bool BelowLimit)> Rejected = new();
		public readonly List<(QueueEndReason Reason, int Attraction, long Waited)> Ends = new();
		public int Boarded;
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) => Rejected.Add( (reason, attraction.AttractionId, attraction.QueueLength < attraction.QueueLimit) );
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) => Boarded++;
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) => Ends.Add( (reason, attractionId, waitTurns) );
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}

	/// <summary>The smarter policy with the original's score: guests choose as the original's do (the expected wait does not steer them), so the slow ride gets its queue and the patience rules are the only thing that judges it.</summary>
	private sealed class OriginalChoice() : SmarterGuestsTests.DelegatingDecisions( new SmarterGuestDecisions() )
	{
		public override uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction ) => OriginalGuestDecisions.Instance.ScoreAttraction( context, guest, attraction );
	}

	private static (ParkScenario Park, Recorder Recorder) Run( ParkScenarioOptions options, ulong seed, int ticks, IGuestDecisions? decisions = null )
	{
		var recorder = new Recorder();
		var park = new ParkScenario( new WorldSeed( seed ), options, decisions ?? new SmarterGuestDecisions() );
		park.Guests.Observer = recorder;
		park.Run( ticks );
		return (park, recorder);
	}

	private static string Describe( Recorder recorder ) =>
		"ends " + string.Join( ",", recorder.Ends.GroupBy( end => end.Reason ).Select( group => $"{group.Key}:{group.Count()}/{group.Max( end => end.Waited )}" ) )
		+ ", rejected " + string.Join( ",", recorder.Rejected.GroupBy( reject => reject.Reason ).Select( group => $"{group.Key}:{group.Count()}" ) ) + $", boarded {recorder.Boarded}";

	private static HashSet<int> RideIds( ParkScenario park ) => park.Guests.Attractions.Where( attraction => attraction.Kind == RideVisitorKind.Ride ).Select( attraction => attraction.AttractionId ).ToHashSet();

	[TestMethod]
	public void TheHostAsksForAlternativesOnlyWhenThePolicyWantsThemAndNeverWhileCalledOrInAnInterlude()
	{
		var spy = new Spy( 0 );
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: spy );
		spy.Find = id => park.Guests.Guests.FirstOrDefault( guest => guest.Id == id );
		park.Run( 12_000 );
		Assert.IsTrue( spy.Admissions.Count > 20 && spy.Admissions.Any( admission => admission.Alternative >= 0 ), "an admission with other rides in the park sees the best of them" );
		Assert.IsTrue( spy.Patience.Count > 50 && spy.Patience.Any( call => call.Alternative >= 0 ) );
		Assert.IsTrue( spy.Patience.All( call => call.State is GuestState.Queueing or GuestState.MovingUpQueue && !call.Interlude && !call.Called ),
			"the hook runs for a waiting guest only, never called forward or in an interlude (the turn a move-up starts is the original flow's and counts as waiting)" );
		Assert.IsTrue( spy.Patience.Where( call => call.Position == 0 ).Any() && spy.Patience.Where( call => call.Position == 0 ).All( call => call.Alternative == -1 ), "nothing is scanned for the guest at the front" );

		var silent = new Spy( -1 );
		var plain = new ParkScenario( new WorldSeed( 1 ), decisions: silent );
		plain.Run( 6_000 );
		Assert.IsTrue( silent.Admissions.Count > 0 && silent.Admissions.All( admission => admission.Alternative == -1 ), "a policy that does not ask is given -1: the original path does no extra work" );
		Assert.IsTrue( silent.Patience.Count > 0 && silent.Patience.All( call => call.Alternative == -1 ) );
	}

	[TestMethod]
	public void TheQuietWindowIsTheTurnsBeforeTheGiveUpTestCanFireAndSkippingItChangesNothing()
	{
		var impatient = Guest( GuestWithPatience( 70 ) );
		var patient = Guest( GuestWithPatience( 130 ) );
		// waited x 256 > PatienceTurns256 first holds one turn after the floor of the patience in turns
		Assert.AreEqual( 85, Smarter.QueuePatienceQuietTurns( Context(), impatient.BirthId ) );
		Assert.AreEqual( 157, Smarter.QueuePatienceQuietTurns( Context(), patient.BirthId ) );
		Assert.AreEqual( -1, ((IGuestDecisions)OriginalGuestDecisions.Instance).QueuePatienceQuietTurns( Context(), patient.BirthId ), "a policy that says nothing is always asked" );
		var ride = Ride( queue: 9, interval: 20 * 256 );
		foreach ( var (guest, quiet) in new[] { (impatient, 85), (patient, 157) } )
		{
			for ( var turn = 0; turn < quiet; turn++ )
				Assert.IsFalse( Smarter.DecideQueuePatience( Context( turn ), guest, Queue( ride, 0, 8, 0 ) ).Leave, $"turn {turn} of {quiet}: inside the window the answer is the original's" );
			Assert.IsTrue( Smarter.DecideQueuePatience( Context( quiet ), guest, Queue( ride, 0, 8, 0 ) ).Leave, "the first turn outside it can give up" );
		}
		// a run in which the host never skips (a spy that always says -1) is the run in which it does
		var skipping = new ParkScenario( new WorldSeed( 2 ), decisions: new SmarterGuestDecisions() );
		skipping.Run( 20_000 );
		var asked = new ParkScenario( new WorldSeed( 2 ), decisions: new Spy( -1 ) );
		asked.Run( 20_000 );
		Assert.AreEqual( asked.Hash, skipping.Hash, "the guests" );
		Assert.AreEqual( asked.Guests.RandomState, skipping.Guests.RandomState, "the draws" );
		// and a policy that is asked less often sees fewer calls than one that is always asked
		var counting = new Spy( -1 );
		var quietSpy = new QuietSpy();
		new ParkScenario( new WorldSeed( 2 ), decisions: counting ).Run( 20_000 );
		new ParkScenario( new WorldSeed( 2 ), decisions: quietSpy ).Run( 20_000 );
		Assert.IsTrue( quietSpy.Calls > 0 && quietSpy.Calls < counting.Patience.Count / 2, $"{quietSpy.Calls} calls against {counting.Patience.Count}" );
		Assert.AreEqual( 0, quietSpy.WithWalkers, "the queue hook's view carries no walkers (counting them outside a decision scans every guest, and the hook does not read them)" );
		Assert.AreEqual( quietSpy.Calls, quietSpy.WithEstimate, "but it still carries the throughput estimate the give-up test reads" );
	}

	private sealed class QuietSpy() : SmarterGuestsTests.DelegatingDecisions( new SmarterGuestDecisions() )
	{
		public int Calls, WithWalkers, WithEstimate;
		public override QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue )
		{
			Calls++;
			WithWalkers += queue.Attraction.Reservations != 0 ? 1 : 0;
			WithEstimate += queue.Attraction.TurnsPerGuest256 > 0 ? 1 : 0;
			return base.DecideQueuePatience( context, guest, queue );
		}
	}

	[TestMethod]
	public void AnAlternativeIsARideTheGuestCouldChooseNotOneItUsedOrJustWasRefusedAt()
	{
		var spy = new Spy( 0 );
		var park = new ParkScenario( new WorldSeed( 3 ), decisions: spy );
		park.Run( 20_000 );
		var rides = RideIds( park );
		var seen = spy.Admissions.Where( admission => admission.Alternative >= 0 ).ToList();
		Assert.IsTrue( seen.Count > 10, "alternatives were found" );
		Assert.IsTrue( seen.All( admission => rides.Any( ride => ride != admission.Ride && !admission.Histories.Contains( ride ) ) ), "an alternative exists only while some other ride is in neither of the guest's histories" );
		Assert.IsTrue( spy.Admissions.Any( admission => admission.Alternative == -1 && rides.Where( ride => ride != admission.Ride ).All( admission.Histories.Contains ) ), "a guest that has been to every other ride has no alternative" );
	}

	[TestMethod]
	public void ARideTheGuestCouldChooseIsNewToItOfAnotherTypeAndOfAnExcitementItLikes()
	{
		var guest = Guest();
		var ride = Ride( interval: 30 * 256 );
		Assert.IsTrue( SmarterGuestsRules.IsUnusedAdmissibleRide( Context(), guest, ride ) );
		Assert.IsFalse( SmarterGuestsRules.IsUnusedAdmissibleRide( Context(), guest, Ride( kind: RideVisitorKind.Shop ) ), "a shop is not a ride" );
		Assert.IsFalse( SmarterGuestsRules.IsUnusedAdmissibleRide( Context(), guest, ride with { SameTypeAsLastUsed = true } ), "the type it rode last" );
		var used = new int[4];
		used[1] = ride.Id;
		Assert.IsFalse( SmarterGuestsRules.IsUnusedAdmissibleRide( Context(), guest with { UsedHistory = Array.AsReadOnly( used ) }, ride ), "in the used history" );
		Assert.IsFalse( SmarterGuestsRules.IsUnusedAdmissibleRide( Context(), guest with { TemporaryHistory = Array.AsReadOnly( used ) }, ride ), "refused lately" );
	}

	[TestMethod]
	public void ASlowRideRefusesGuestsWhoHaveAQuickerOneAndNeverAServiceOrTheLastRide()
	{
		var (park, recorder) = Run( new ParkScenarioOptions { TwinAHoldMilliseconds = 30_000, ArrivalTimeBetween = 6 }, 1, 40_000, new OriginalChoice() );
		var rides = RideIds( park );
		var patience = recorder.Rejected.Where( reject => reject.Reason == QueueEndReason.QueueLimit && reject.BelowLimit ).ToList();
		Assert.IsTrue( patience.Count > 0, "a guest was turned away by its patience while the queue had room: rule 1 acted" );
		Assert.IsTrue( patience.All( reject => rides.Contains( reject.Attraction ) ), "only rides" );
	}

	[TestMethod]
	public void AGuestWaitingTooLongForASlowRideGivesUpWhenAQuickerOneExists()
	{
		var (park, recorder) = Run( new ParkScenarioOptions { TwinAHoldMilliseconds = 30_000, ArrivalTimeBetween = 6 }, 2, 60_000, new OriginalChoice() );
		var rides = RideIds( park );
		var gaveUp = recorder.Ends.Where( end => end.Reason == QueueEndReason.Impatient ).ToList();
		Assert.IsTrue( gaveUp.Count > 0, Describe( recorder ) );
		Assert.IsTrue( gaveUp.All( end => rides.Contains( end.Attraction ) && end.Waited > 84 ), "a give-up is a ride's, after the earliest patience" );
		Assert.IsTrue( QueueEndReasons.IsAbandonment( QueueEndReason.Impatient ) );
	}

	[TestMethod]
	public void ASingleRideParkNeverTurnsAnyoneAwayByPatienceNorLetsAnyoneGiveUp()
	{
		// the level parks have one ride: with nowhere else to go the rules do nothing, even for a ride far slower than the park needs
		var (park, recorder) = Run( new ParkScenarioOptions { SingleRide = true, TwinAHoldMilliseconds = 30_000, ArrivalTimeBetween = 6 }, 1, 60_000, new OriginalChoice() );
		Assert.AreEqual( 1, RideIds( park ).Count, "one ride" );
		Assert.IsTrue( recorder.Boarded > 5, "the ride boards guests" );
		Assert.IsFalse( recorder.Rejected.Any( reject => reject.Reason == QueueEndReason.QueueLimit && reject.BelowLimit ), "no refusal while the queue has room" );
		Assert.IsFalse( recorder.Ends.Any( end => end.Reason == QueueEndReason.Impatient ), "no give-up" );
		Assert.IsTrue( recorder.Ends.Any( end => end.Waited > 84 ), Describe( recorder ) );
	}

	[TestMethod]
	public void ThePatiencePolicyIsStatelessAndTheOffPathIsTheOriginalsWithNoAlternativeLook()
	{
		Assert.AreEqual( QueuePatienceDecision.Stay, OriginalGuestDecisions.Instance.DecideQueuePatience( Context( 99_999 ), Guest(), Queue( Ride( queue: 9 ), 0, 8, 0 ) ) );
		var admission = Admission( Ride( queue: 19 ), 0 );
		Assert.AreEqual( OriginalGuestDecisions.Instance.DecideAdmission( Context(), Guest(), admission ), new SmarterGuestDecisions().DecideAdmission( Context(), Guest(), admission with { BestAlternativeWait256 = -1 } ) );
		Assert.AreEqual( 0, typeof( SmarterGuestDecisions ).GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public ).Count( field => !field.IsInitOnly ), "no mutable field" );
	}

	[TestMethod]
	public void TheMoveUpTurnAsksWithTheCurrentPlace()
	{
		var guest = Guest( GuestWithPatience( 100 ) );
		var ride = Ride( queue: 6, interval: 20 * 256 );
		// 121 turns waited at place 5: leaves; the same guest one place from the front (it has just moved up) has little left to wait and stays
		Assert.IsTrue( Smarter.DecideQueuePatience( Context( 121 ), guest, Queue( ride, 0, 5, 10 * 256 ) ).Leave );
		Assert.IsFalse( Smarter.DecideQueuePatience( Context( 121 ), guest, Queue( ride, 0, 1, 18 * 256 ) ).Leave );
	}
}
