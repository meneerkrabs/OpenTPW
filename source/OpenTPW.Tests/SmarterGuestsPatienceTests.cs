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
	public void ThePatienceBandMovesTheLimitAndTheWalkersCountAsGuestsAhead()
	{
		var queue = Ride( queue: 3, reservations: 1, interval: 30 * 256 ); // 4 guests ahead: 120 turns
		var patient = Guest( GuestWithPatience( 130 ) ); // 117 turns: still refused
		var impatient = Guest( GuestWithPatience( 70 ) ); // 63 turns
		var calm = Ride( queue: 3, interval: 30 * 256 ); // 90 turns: the middle guest accepts, the impatient does not
		Assert.IsFalse( Smarter.DecideAdmission( Context(), patient, Admission( queue, 0 ) ).Admits );
		Assert.IsFalse( Smarter.DecideAdmission( Context(), impatient, Admission( calm, 0 ) ).Admits );
		Assert.IsTrue( Smarter.DecideAdmission( Context(), patient, Admission( calm, 0 ) ).Admits );
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
		Assert.IsTrue( SmarterGuestsRules.AlternativeLookAfterTurns <= 84, "the host looks before the earliest possible give-up" );
	}

	// ---- The host -----------------------------------------------------------------------------

	private sealed class Spy : SmarterGuestsTests.DelegatingDecisions
	{
		private readonly int look;
		public readonly List<(long Alternative, GuestState State, bool Interlude, bool Called)> Patience = new();
		public readonly List<long> Admissions = new();
		public Spy( int look ) : base( new SmarterGuestDecisions() ) => this.look = look;
		public Func<int, Guest?> Find = _ => null;
		public override int AlternativeLookAfterTurns => look;
		public override QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission )
		{
			Admissions.Add( admission.BestAlternativeWait256 );
			return base.DecideAdmission( context, guest, admission );
		}
		public override QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue )
		{
			var host = Find( guest.Id );
			Patience.Add( (queue.BestAlternativeWait256, host?.State ?? GuestState.Gone, host?.InQueueInterlude ?? false, host?.QueueCalled ?? false) );
			return base.DecideQueuePatience( context, guest, queue );
		}
	}

	private sealed class Recorder : IGuestObserver
	{
		public readonly List<(QueueEndReason Reason, int Attraction, bool BelowLimit)> Rejected = new();
		public readonly List<(QueueEndReason Reason, int Attraction, long Waited)> Ends = new();
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) => Rejected.Add( (reason, attraction.AttractionId, attraction.QueueLength < attraction.QueueLimit) );
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) => Ends.Add( (reason, attractionId, waitTurns) );
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}

	[TestMethod]
	public void TheHostAsksForAlternativesOnlyWhenThePolicyWantsThemAndNeverWhileCalledOrInAnInterlude()
	{
		var spy = new Spy( 0 );
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: spy );
		spy.Find = id => park.Guests.Guests.FirstOrDefault( guest => guest.Id == id );
		park.Run( 12_000 );
		Assert.IsTrue( spy.Admissions.Count > 20 && spy.Admissions.Any( wait => wait >= 0 ), "an admission with other rides in the park sees the best of them" );
		Assert.IsTrue( spy.Patience.Count > 50 && spy.Patience.Any( call => call.Alternative >= 0 ) );
		Assert.IsTrue( spy.Patience.All( call => call.State is GuestState.Queueing or GuestState.MovingUpQueue && !call.Interlude && !call.Called ), string.Join( ", ", spy.Patience.GroupBy( call => (call.State, call.Interlude, call.Called) ).Select( g => $"{g.Key}:{g.Count()}" ) ) + " the hook runs for a waiting guest only, never called forward or in an interlude" );

		var silent = new Spy( -1 );
		var plain = new ParkScenario( new WorldSeed( 1 ), decisions: silent );
		plain.Run( 6_000 );
		Assert.IsTrue( silent.Admissions.Count > 0 && silent.Admissions.All( wait => wait == -1 ), "a policy that does not ask is given -1: the original path does no extra work" );
		Assert.IsTrue( silent.Patience.All( call => call.Alternative == -1 ) );
	}

	[TestMethod]
	public void ARefusalIsAQueueLimitRefusalAndAGiveUpAnImpatientExitOnRidesOnly()
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var recorder = new Recorder();
		var park = new ParkScenario( new WorldSeed( 1 ) );
		park.Guests.Observer = recorder;
		park.Run( 40_000 );
		Assert.IsInstanceOfType( park.Guests.Decisions, typeof( SmarterGuestDecisions ) );
		Assert.IsTrue( recorder.Rejected.Any( reject => reject.Reason == QueueEndReason.QueueLimit && reject.BelowLimit ), "a guest turned away by its patience while the queue had room: rule 1 acted" );
		var rides = park.Guests.Attractions.Where( attraction => attraction.Kind == RideVisitorKind.Ride ).Select( attraction => attraction.AttractionId ).ToHashSet();
		Assert.IsTrue( recorder.Rejected.Where( reject => reject.BelowLimit && reject.Reason == QueueEndReason.QueueLimit ).All( reject => rides.Contains( reject.Attraction ) ), "never a shop or the toilet" );
		Assert.IsTrue( recorder.Ends.Where( end => end.Reason == QueueEndReason.Impatient ).All( end => rides.Contains( end.Attraction ) && end.Waited > 84 ), "a give-up is a ride's, after the shortest patience" );
	}

	[TestMethod]
	public void ASingleRideParkNeverTurnsAnyoneAwayByPatience()
	{
		// the level parks have one ride: with nowhere else to go, patience does nothing and the queue behaves as the earlier phases' (the measurement's level rows)
		var spy = new Spy( 0 );
		var options = new ParkScenarioOptions();
		var park = new ParkScenario( new WorldSeed( 2 ), options, spy );
		var rides = park.Guests.Attractions.Where( attraction => attraction.Kind == RideVisitorKind.Ride ).ToList();
		Assert.IsTrue( rides.Count > 1, "the scenario has several rides: the single-ride case is the rule-level test below" );
		var guest = Guest( GuestWithPatience( 70 ) );
		Assert.IsTrue( Smarter.DecideAdmission( Context(), guest, Admission( Ride( queue: 19, interval: 30 * 256 ), -1 ) ).Admits );
		Assert.IsFalse( Smarter.DecideQueuePatience( Context( 5_000 ), guest, Queue( Ride( queue: 19, interval: 30 * 256 ), 0, 15, -1 ) ).Leave );
	}

	[TestMethod]
	public void ThePatiencePolicyIsStatelessAndTheOffPathIsTheOriginalsWithNoAlternativeLook()
	{
		Assert.AreEqual( -1, ((IGuestDecisions)OriginalGuestDecisions.Instance).AlternativeLookAfterTurns );
		Assert.AreEqual( SmarterGuestsRules.AlternativeLookAfterTurns, ((IGuestDecisions)Smarter).AlternativeLookAfterTurns );
		Assert.AreEqual( QueuePatienceDecision.Stay, OriginalGuestDecisions.Instance.DecideQueuePatience( Context( 99_999 ), Guest(), Queue( Ride( queue: 9 ), 0, 8, 0 ) ) );
		var admission = Admission( Ride( queue: 19 ), 0 );
		Assert.AreEqual( OriginalGuestDecisions.Instance.DecideAdmission( Context(), Guest(), admission ), new SmarterGuestDecisions().DecideAdmission( Context(), Guest(), admission with { BestAlternativeWait256 = -1 } ) );
		Assert.AreEqual( 0, typeof( SmarterGuestDecisions ).GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public ).Count( field => !field.IsInitOnly ), "no mutable field" );
	}
}
