using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Phase 3 of the smarter-guests plan (docs/SMARTER-GUESTS.md): spreading. The throughput estimator with its cold start, closure, breakdown, upgrade and zero-throughput rules, the queue estimate in the
/// score, the per-guest preference band, the hysteresis of the minor decision, the host's facts, the section and the off path. The measured effect is in the page's tables.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SmarterGuestsSpreadingTests
{
	private static readonly GuestSettings Settings = GuestTests.CreateSettings();

	private static GuestView Guest( ulong birthId = 11 ) =>
		new( 7, birthId, 0, 60, 10, 10, 10, 0, 500, 3, 3, Array.AsReadOnly( new int[4] ), Array.AsReadOnly( new int[4] ) );

	/// <summary>A ride next to the guest, with a queue estimate; its excitement suits the guest type.</summary>
	private static AttractionView Ride( int id = 5, int queue = 0, int reservations = 0, long interval = 30 * 256, RideVisitorKind kind = RideVisitorKind.Ride ) =>
		new( id, 1, kind, true, (3, 4), (3, 5), 0, queue, 4, 20, 70, 1, 0, 0, false, 30, false, 0, 0, true, false, false, 0, 0, reservations, interval );

	private static int Turns( long wait256 ) => (int)(wait256 / 256);

	// ---- The estimator ------------------------------------------------------------------------

	[TestMethod]
	public void ColdStartIsTheCatalogueCapacityOverADefaultCycleAndNeverBelowOneTurn()
	{
		Assert.AreEqual( RideThroughputs.DefaultCycleTurns * 256L, RideThroughputs.ColdStartInterval256( 1 ) );
		Assert.AreEqual( RideThroughputs.DefaultCycleTurns * 256L / 4, RideThroughputs.ColdStartInterval256( 4 ), "a ride of four serves a guest in a quarter of its cycle" );
		Assert.AreEqual( RideThroughputs.DefaultServiceTurns * 256L, RideThroughputs.ColdStartInterval256( 1, RideVisitorKind.Shop ), "a shop is quicker than a ride" );
		Assert.AreEqual( RideThroughputs.ColdStartInterval256( 1 ), RideThroughputs.ColdStartInterval256( 0 ), "a capacity below 1 reads as 1" );
		Assert.AreEqual( RideThroughputs.ColdStartInterval256( 1 ), RideThroughputs.ColdStartInterval256( -3 ) );
		Assert.AreEqual( RideThroughputs.MinimumInterval256, RideThroughputs.ColdStartInterval256( 100_000 ), "never below a quarter turn: zero throughput cannot be asked for in reverse either" );
		var estimates = new RideThroughputs();
		Assert.AreEqual( RideThroughputs.ColdStartInterval256( 3 ), estimates.IntervalFor( 9, RideVisitorKind.Ride, 3, 40, 100, 5 ), "an attraction nobody has boarded yet reads as the cold start" );
		Assert.AreEqual( 0, estimates.Count, "reading creates no entry" );
	}

	[TestMethod]
	public void TheFirstBoardingStartsAGapAndTheSecondOneWithDemandFoldsItIn()
	{
		var estimates = new RideThroughputs();
		var prior = RideThroughputs.ColdStartInterval256( 1 );
		estimates.Boarded( 4, RideVisitorKind.Ride, 1, 40, 100, queueLengthAfter: 3 );
		Assert.IsTrue( estimates.TryGet( 4, out var first ) );
		Assert.AreEqual( new RideThroughput( 4, 1, 40, prior, 0, 100, 100, true ), first, "no gap yet: the cold start stays and the gap starts" );
		estimates.Boarded( 4, RideVisitorKind.Ride, 1, 40, 110, queueLengthAfter: 2 );
		estimates.TryGet( 4, out var second );
		Assert.AreEqual( 1, second.Samples );
		Assert.AreEqual( prior + (10 * 256 - prior) / 2, second.Interval256, "the cold start counts as one sample: the first gap moves the average half way" );
		Assert.AreEqual( 110L, second.LastBoardTurn );
		estimates.Boarded( 4, RideVisitorKind.Ride, 1, 40, 130, queueLengthAfter: 0 );
		estimates.TryGet( 4, out var third );
		Assert.AreEqual( 2, third.Samples );
		Assert.AreEqual( second.Interval256 + (20 * 256 - second.Interval256) / 3, third.Interval256, "weight 1/(samples + 2)" );
		Assert.IsFalse( third.Demand, "nobody was left in the queue: the next gap says nothing about the ride" );
		estimates.Boarded( 4, RideVisitorKind.Ride, 1, 40, 500, queueLengthAfter: 1 );
		estimates.TryGet( 4, out var fourth );
		Assert.AreEqual( third with { LastBoardTurn = 500, ClockTurn = 500, Demand = true }, fourth, "a gap without demand is the arrival rate, not the throughput: it is not folded in" );
	}

	[TestMethod]
	public void TheAverageConvergesOnASteadyGapAndWeighsALongHistoryByOneSixteenth()
	{
		var estimates = new RideThroughputs();
		var turn = 0L;
		for ( var board = 0; board < 400; board++ )
		{
			estimates.Boarded( 2, RideVisitorKind.Ride, 1, 0, turn, 5 );
			turn += 12;
		}
		estimates.TryGet( 2, out var steady );
		Assert.IsTrue( Math.Abs( steady.Interval256 - 12 * 256 ) <= 16, $"{steady.Interval256 / 256.0} turns per guest for a gap of 12" );
		Assert.AreEqual( 399, steady.Samples );
		estimates.Boarded( 2, RideVisitorKind.Ride, 1, 0, turn - 12 + 112, 5 );
		estimates.TryGet( 2, out var spike );
		Assert.AreEqual( steady.Interval256 + (112 * 256 - steady.Interval256) / RideThroughputs.MaximumWeightDivisor, spike.Interval256, "one slow gap moves a long history by a sixteenth only" );
	}

	[TestMethod]
	public void AGapIsClampedAndTheSamplesSaturate()
	{
		var estimates = new RideThroughputs();
		estimates.Boarded( 1, RideVisitorKind.Ride, 1, 0, 0, 5 );
		estimates.Boarded( 1, RideVisitorKind.Ride, 1, 0, 1_000_000, 5 );
		estimates.TryGet( 1, out var entry );
		Assert.IsTrue( entry.Interval256 <= RideThroughputs.MaximumInterval256, "a very long gap is clamped to the maximum" );
		Assert.AreEqual( RideThroughputs.ColdStartInterval256( 1 ) + (RideThroughputs.MaximumInterval256 - RideThroughputs.ColdStartInterval256( 1 )) / 2, entry.Interval256 );
		var turn = 1_000_000L;
		for ( var board = 0; board < RideThroughputs.MaximumSamples + 50; board++ )
			estimates.Boarded( 1, RideVisitorKind.Ride, 1, 0, turn += 3, 5 );
		estimates.TryGet( 1, out var saturated );
		Assert.AreEqual( RideThroughputs.MaximumSamples, saturated.Samples );
		Assert.IsTrue( saturated.Interval256 >= RideThroughputs.MinimumInterval256, "never below a quarter turn" );
		estimates.Boarded( 1, RideVisitorKind.Ride, 1, 0, turn + 1, 5 );
		estimates.Boarded( 1, RideVisitorKind.Ride, 1, 0, turn + 1, 5 );
		estimates.TryGet( 1, out var sameTurn );
		Assert.AreEqual( RideThroughputs.MaximumSamples, sameTurn.Samples );
		Assert.IsTrue( sameTurn.Interval256 >= RideThroughputs.MinimumInterval256, "two boardings in one turn are not a gap" );
	}

	[TestMethod]
	public void AClosedBrokenOrIdleAttractionStartsANewGapInsteadOfFoldingInTheDowntime()
	{
		var estimates = new RideThroughputs();
		estimates.Boarded( 6, RideVisitorKind.Ride, 2, 40, 100, 4 );
		estimates.Boarded( 6, RideVisitorKind.Ride, 2, 40, 110, 4 );
		estimates.TryGet( 6, out var before );
		Assert.IsTrue( before.Demand );
		// open and working with a queue: nothing changes
		estimates.Observe( 6, RideVisitorKind.Ride, 2, 40, working: true, queueLength: 4, turn: 111 );
		estimates.TryGet( 6, out var unchanged );
		Assert.AreEqual( before, unchanged );
		// closed or broken: the flag goes, the average stays
		estimates.Observe( 6, RideVisitorKind.Ride, 2, 40, working: false, queueLength: 4, turn: 112 );
		estimates.TryGet( 6, out var closed );
		Assert.AreEqual( before with { Demand = false }, closed );
		estimates.Boarded( 6, RideVisitorKind.Ride, 2, 40, 900, 3 );
		estimates.TryGet( 6, out var reopened );
		Assert.AreEqual( closed.Interval256, reopened.Interval256, "the 790 turns of downtime are not a gap" );
		Assert.AreEqual( closed.Samples, reopened.Samples );
		Assert.IsTrue( reopened.Demand );
		// an empty queue at the turn start ends the gap as well
		estimates.Observe( 6, RideVisitorKind.Ride, 2, 40, working: true, queueLength: 0, turn: 901 );
		estimates.TryGet( 6, out var idle );
		Assert.IsFalse( idle.Demand );
		// a queue forming again starts the silence clock at that turn, and the first boarding after it is a wait, not a gap
		estimates.Observe( 6, RideVisitorKind.Ride, 2, 40, working: true, queueLength: 2, turn: 950 );
		estimates.TryGet( 6, out var forming );
		Assert.AreEqual( idle with { Demand = true, ClockTurn = 950 }, forming );
		estimates.Boarded( 6, RideVisitorKind.Ride, 2, 40, 960, 1 );
		estimates.TryGet( 6, out var first );
		Assert.AreEqual( idle.Interval256, first.Interval256, "the first boarding after a queue formed measures the wait" );
		Assert.AreEqual( idle.Samples, first.Samples );
	}

	[TestMethod]
	public void ARepairedRideWithGuestsStillQueuedWhoseHeadNeverBoardsIsReadAsStalled()
	{
		var estimates = new RideThroughputs();
		estimates.Boarded( 6, RideVisitorKind.Ride, 1, 40, 100, 3 );
		for ( var turn = 101; turn < 110; turn++ )
			estimates.Observe( 6, RideVisitorKind.Ride, 1, 40, working: true, queueLength: 3, turn: turn );
		estimates.TryGet( 6, out var running );
		var interval = running.Interval256;
		// breakdown with the queue still there, then the repair at turn 300: demand is back (queue and working) with a clock at 300
		estimates.Observe( 6, RideVisitorKind.Ride, 1, 40, working: false, queueLength: 3, turn: 200 );
		estimates.Observe( 6, RideVisitorKind.Ride, 1, 40, working: true, queueLength: 3, turn: 300 );
		estimates.TryGet( 6, out var repaired );
		Assert.IsTrue( repaired.Demand );
		Assert.AreEqual( 300L, repaired.ClockTurn );
		Assert.AreEqual( 100L, repaired.LastBoardTurn );
		Assert.AreEqual( interval, estimates.IntervalFor( 6, RideVisitorKind.Ride, 1, 40, 301, 3 ), "just repaired: the average" );
		// nobody boards for 200 turns although guests wait: the silence is the estimate
		Assert.AreEqual( 200 * 256L, estimates.IntervalFor( 6, RideVisitorKind.Ride, 1, 40, 500, 3 ) );
		Assert.AreEqual( interval, estimates.IntervalFor( 6, RideVisitorKind.Ride, 1, 40, 500, 0 ), "an empty queue is idle, not stalled" );
	}

	[TestMethod]
	public void ARideThatNeverBoardsAnybodyGetsAnEntryWhenItsQueueFormsAndIsReadAsStalled()
	{
		var estimates = new RideThroughputs();
		estimates.Observe( 12, RideVisitorKind.Ride, 1, 40, working: true, queueLength: 0, turn: 10 );
		estimates.Observe( 12, RideVisitorKind.Ride, 1, 40, working: false, queueLength: 5, turn: 10 );
		Assert.AreEqual( 0, estimates.Count, "no queue, or not working: no entry" );
		estimates.Observe( 12, RideVisitorKind.Ride, 1, 40, working: true, queueLength: 2, turn: 20 );
		Assert.IsTrue( estimates.TryGet( 12, out var born ) );
		Assert.AreEqual( new RideThroughput( 12, 1, 40, RideThroughputs.ColdStartInterval256( 1 ), 0, -1, 20, true ), born );
		Assert.AreEqual( RideThroughputs.ColdStartInterval256( 1 ), estimates.IntervalFor( 12, RideVisitorKind.Ride, 1, 40, 60, 2 ), "inside two nominal cycles: the cold start" );
		Assert.AreEqual( 500 * 256L, estimates.IntervalFor( 12, RideVisitorKind.Ride, 1, 40, 520, 2 ), "500 turns of silence with guests waiting" );
		// a second observation of the same queue changes nothing
		estimates.Observe( 12, RideVisitorKind.Ride, 1, 40, working: true, queueLength: 3, turn: 30 );
		estimates.TryGet( 12, out var again );
		Assert.AreEqual( born, again );
	}

	[TestMethod]
	public void TwoBoardingsInOneParkTurnAreAGapOfZeroAndTakeTheAverageTowardsTheMinimum()
	{
		var estimates = new RideThroughputs();
		estimates.Boarded( 3, RideVisitorKind.Ride, 4, 40, 100, 6 );
		var steady = 0L;
		for ( var board = 0; board < 12; board++ )
		{
			estimates.Boarded( 3, RideVisitorKind.Ride, 4, 40, 100, 6 );
			estimates.TryGet( 3, out var now );
			Assert.IsTrue( now.Interval256 < (board == 0 ? RideThroughputs.ColdStartInterval256( 4 ) : steady), "each same-turn boarding lowers the average" );
			steady = now.Interval256;
		}
		estimates.TryGet( 3, out var end );
		Assert.AreEqual( 12, end.Samples );
		Assert.IsTrue( end.Interval256 >= RideThroughputs.MinimumInterval256 );
	}

	[TestMethod]
	public void AnUpgradeThatChangesCapacityOrSpeedRestartsTheEstimateAtTheColdStartOfTheNewCapacity()
	{
		var estimates = new RideThroughputs();
		for ( var board = 0; board < 20; board++ )
			estimates.Boarded( 3, RideVisitorKind.Ride, 2, 40, board * 8, 5 );
		estimates.TryGet( 3, out var learned );
		Assert.IsTrue( learned.Samples > 5 && learned.Interval256 < 20 * 256 );
		// the policy never reads the old average for the new ride, even before the turn-start look
		Assert.AreEqual( RideThroughputs.ColdStartInterval256( 4 ), estimates.IntervalFor( 3, RideVisitorKind.Ride, 4, 40, 200, 5 ) );
		Assert.AreEqual( RideThroughputs.ColdStartInterval256( 2 ), estimates.IntervalFor( 3, RideVisitorKind.Ride, 2, 60, 200, 5 ), "a faster ride restarts too" );
		estimates.Observe( 3, RideVisitorKind.Ride, 4, 40, true, 5, 210 );
		estimates.TryGet( 3, out var restarted );
		Assert.AreEqual( new RideThroughput( 3, 4, 40, RideThroughputs.ColdStartInterval256( 4 ), 0, -1, 210, true ), restarted, "restarted at the cold start of the new capacity; the queue starts a new silence clock" );
		// a boarding after an upgrade restarts as well
		for ( var board = 0; board < 6; board++ )
			estimates.Boarded( 3, RideVisitorKind.Ride, 4, 40, 300 + board * 3, 5 );
		estimates.Boarded( 3, RideVisitorKind.Ride, 5, 40, 400, 5 );
		estimates.TryGet( 3, out var again );
		Assert.AreEqual( new RideThroughput( 3, 5, 40, RideThroughputs.ColdStartInterval256( 5 ), 0, 400, 400, true ), again );
	}

	[TestMethod]
	public void AZeroCapacityReadsAsOneAndAStalledRideIsReadAsItsSilence()
	{
		var estimates = new RideThroughputs();
		estimates.Boarded( 8, RideVisitorKind.Ride, 0, 0, 10, 2 );
		estimates.TryGet( 8, out var zero );
		Assert.AreEqual( 1, zero.Capacity, "a capacity of 0 is stored as 1" );
		Assert.AreEqual( RideThroughputs.ColdStartInterval256( 1 ), estimates.IntervalFor( 8, RideVisitorKind.Ride, 0, 0, 10, 2 ) );
		// the average says 10 turns per guest; with guests waiting and no boarding for far longer than two cycles, the silence is the estimate
		var entry = new RideThroughput( 8, 1, 0, 10 * 256, 12, 100, 100, true );
		Assert.AreEqual( 10 * 256L, RideThroughputs.EffectiveInterval256( entry, 105, 3 ), "inside a cycle: the average" );
		Assert.AreEqual( 10 * 256L, RideThroughputs.EffectiveInterval256( entry, 120, 3 ), "exactly two cycles: still the average" );
		Assert.AreEqual( 50 * 256L, RideThroughputs.EffectiveInterval256( entry, 150, 3 ), "50 turns of silence with guests waiting" );
		Assert.AreEqual( 10 * 256L, RideThroughputs.EffectiveInterval256( entry, 150, 0 ), "nobody waits: the ride is not stalled, it is idle" );
		Assert.AreEqual( 10 * 256L, RideThroughputs.EffectiveInterval256( entry with { Demand = false }, 150, 3 ), "after a closure the silence is not the ride's" );
		Assert.AreEqual( RideThroughputs.MaximumInterval256, RideThroughputs.EffectiveInterval256( entry, 1_000_000, 3 ), "capped" );
		var wide = new RideThroughput( 8, 4, 0, 5 * 256, 12, 100, 100, true );
		Assert.AreEqual( 5 * 256L, RideThroughputs.EffectiveInterval256( wide, 130, 3 ), "a ride of four: 30 turns is inside two cycles of 20" );
		Assert.AreEqual( 40 * 256L, RideThroughputs.EffectiveInterval256( wide, 100 + 160, 3 ), "160 turns of silence over a cycle of four guests is 40 turns per guest" );
		// a corrupt interval is clamped on the way out
		Assert.AreEqual( RideThroughputs.MinimumInterval256, RideThroughputs.EffectiveInterval256( entry with { Interval256 = 0 }, 100, 1 ) );
	}

	[TestMethod]
	public void TheEstimatesStayInAttractionIdOrderAndForgetARemovedAttraction()
	{
		var estimates = new RideThroughputs();
		foreach ( var id in new[] { 9, 2, 5, 7, 1 } )
			estimates.Boarded( id, RideVisitorKind.Shop, 1, 0, 10, 0 );
		CollectionAssert.AreEqual( new[] { 1, 2, 5, 7, 9 }, estimates.Entries.Select( entry => entry.AttractionId ).ToArray() );
		estimates.Remove( 5 );
		estimates.Remove( 4 );
		CollectionAssert.AreEqual( new[] { 1, 2, 7, 9 }, estimates.Entries.Select( entry => entry.AttractionId ).ToArray() );
		estimates.Clear();
		Assert.AreEqual( 0, estimates.Count );
	}

	// ---- The queue estimate in the score ------------------------------------------------------

	[TestMethod]
	public void TheExpectedWaitIsTheGuestsAheadAndTheGuestsWalkingThereTimesTheTimePerGuest()
	{
		Assert.IsNull( SmarterGuestsRules.ExpectedWait256( Ride( interval: 0 ) ), "no estimate (a host that was not asked): unknown" );
		Assert.AreEqual( 0L, SmarterGuestsRules.ExpectedWait256( Ride( queue: 0, reservations: 0 ) ), "an empty attraction with nobody on the way: no wait" );
		Assert.AreEqual( 5 * 30 * 256L, SmarterGuestsRules.ExpectedWait256( Ride( queue: 3, reservations: 2, interval: 30 * 256 ) ) );
		Assert.AreEqual( 30 * 256L, SmarterGuestsRules.ExpectedWait256( Ride( queue: 0, reservations: 1 ) ), "a reservation counts as a guest ahead" );
		Assert.AreEqual( 3 * 12 * 256L, SmarterGuestsRules.ExpectedWait256( Ride( queue: 3, interval: 12 * 256 ) ), "throughput is the inverse of the time per guest: a ride that serves a guest in 12 turns" );
		Assert.AreEqual( 0L, SmarterGuestsRules.ExpectedWait256( Ride( queue: -4, reservations: -1 ) ), "a negative count reads as none" );
		Assert.IsTrue( SmarterGuestsRules.ExpectedWait256( Ride( queue: int.MaxValue, reservations: int.MaxValue, interval: long.MaxValue / 4 ) ) > 0, "huge values saturate instead of wrapping" );
	}

	[TestMethod]
	public void TheQueueMatchFallsFromAHundredToZeroAtTheScale()
	{
		Assert.AreEqual( 100u, SmarterGuestsRules.QueueMatchOf( 0 ) );
		Assert.AreEqual( 50u, SmarterGuestsRules.QueueMatchOf( SmarterGuestsRules.QueueWaitScaleTurns * 256L / 2 ) );
		Assert.AreEqual( 0u, SmarterGuestsRules.QueueMatchOf( SmarterGuestsRules.QueueWaitScaleTurns * 256L ) );
		Assert.AreEqual( 0u, SmarterGuestsRules.QueueMatchOf( long.MaxValue / 1000 ), "clamped, never negative" );
		var previous = 101u;
		for ( var wait = 0L; wait < SmarterGuestsRules.QueueWaitScaleTurns * 256L * 2; wait += 997 )
		{
			var match = SmarterGuestsRules.QueueMatchOf( wait );
			Assert.IsTrue( match <= previous && match <= 100, "monotone" );
			previous = match;
		}
	}

	[TestMethod]
	public void AnAttractionWithoutAnEstimateOrAJoinCellIsScoredByTheOriginalAlone()
	{
		var smarter = new SmarterGuestDecisions();
		var original = OriginalGuestDecisions.Instance;
		var context = new DecisionContext( Settings, 12, false, 3 );
		var guest = Guest();
		var none = Ride( interval: 0 );
		Assert.AreEqual( original.ScoreAttraction( context, guest, none ), smarter.ScoreAttraction( context, guest, none ) );
		var noCell = none with { HasJoinCell = false, TurnsPerGuest256 = 30 * 256 };
		Assert.AreEqual( original.ScoreAttraction( context, guest, noCell ), smarter.ScoreAttraction( context, guest, noCell ), "no join cell: the host never asks, and the original reads it as it does" );
	}

	[TestMethod]
	public void ALongerExpectedWaitLowersTheScoreAndTheQueueCountsAtAnyDistance()
	{
		var smarter = new SmarterGuestDecisions();
		var context = new DecisionContext( Settings, 12, false, 3 );
		var guest = Guest();
		var empty = smarter.ScoreAttraction( context, guest, Ride( queue: 0 ) );
		var some = smarter.ScoreAttraction( context, guest, Ride( queue: 2 ) );
		var many = smarter.ScoreAttraction( context, guest, Ride( queue: 4 ) );
		Assert.IsTrue( empty > some && some >= many, $"{empty}, {some}, {many}" );
		Assert.IsTrue( smarter.ScoreAttraction( context, guest, Ride( queue: 1, reservations: 3 ) ) < smarter.ScoreAttraction( context, guest, Ride( queue: 1 ) ), "guests on the way count as guests ahead" );
		Assert.IsTrue( smarter.ScoreAttraction( context, guest, Ride( queue: 3, interval: 4 * 256 ) ) > smarter.ScoreAttraction( context, guest, Ride( queue: 3, interval: 40 * 256 ) ), "the same queue is shorter at a faster attraction" );
		// the original counts the queue within 2 cells only; the expected wait counts everywhere
		var far = Guest() with { CellX = 60, CellY = 3 };
		var farEmpty = smarter.ScoreAttraction( context, far, Ride( queue: 0 ) with { BackCell = (3, 4) } );
		var farBusy = smarter.ScoreAttraction( context, far, Ride( queue: 4, reservations: 6 ) with { BackCell = (3, 4) } );
		var nearByOriginal = OriginalGuestDecisions.Instance.ScoreAttraction( context, far, Ride( queue: 0 ) with { BackCell = (3, 4) } );
		Assert.IsTrue( farEmpty >= nearByOriginal || farEmpty < AttractionScore.CandidateFloor, "fixture sanity" );
		Assert.AreEqual( OriginalGuestDecisions.Instance.ScoreAttraction( context, far, Ride( queue: 0 ) with { BackCell = (3, 4) } ), OriginalGuestDecisions.Instance.ScoreAttraction( context, far, Ride( queue: 4 ) with { BackCell = (3, 4) } ), "the original does not see the far queue" );
		Assert.IsTrue( farBusy <= farEmpty );
	}

	[TestMethod]
	public void ALongWaitNeverTakesACandidateTheOriginalWouldHaveChosenBelowTheChoiceFloor()
	{
		var smarter = new SmarterGuestDecisions();
		var context = new DecisionContext( Settings, 12, false, 3 );
		var guest = Guest();
		var original = OriginalGuestDecisions.Instance.ScoreAttraction( context, guest, Ride( queue: 0, interval: 0 ) );
		Assert.IsTrue( original >= AttractionScore.CandidateFloor, $"the fixture scores {original}" );
		var hopeless = Ride( queue: 4, reservations: 50, interval: 400 * 256 );
		Assert.IsTrue( smarter.ScoreAttraction( context, guest, hopeless ) >= AttractionScore.CandidateFloor, "a hopeless queue is still a candidate" );
		// find rides the original chooses only thanks to its near-queue bonus or its excitement, whose expected wait would take them under the floor
		var protectedCases = 0;
		foreach ( var excitement in Enumerable.Range( 0, 21 ).Select( step => step * 5 ) )
			foreach ( var column in new[] { 3, 8, 20, 40 } )
			{
				var view = Ride( queue: 4, reservations: 50, interval: 400 * 256 ) with { LiveExcitement = excitement, BackCell = (column, 4), EntranceCell = (column, 5) };
				var originalScore = OriginalGuestDecisions.Instance.ScoreAttraction( context, guest, view with { TurnsPerGuest256 = 0 } );
				var input = OriginalGuestDecisions.ScoreInputOf( context, guest, view, 0 );
				var raw = AttractionScore.Compute( input, Settings.DecisionWeights, Settings.DecisionVariables );
				var smart = smarter.ScoreAttraction( context, guest, view );
				if ( originalScore >= AttractionScore.CandidateFloor && raw * 94 / 100 < AttractionScore.CandidateFloor )
				{
					Assert.IsTrue( smart is >= AttractionScore.CandidateFloor and <= AttractionScore.CandidateFloor + 1, $"{smart}: excitement {excitement}, column {column}: the original scores {originalScore}, the wait alone {raw}" );
					protectedCases++;
				}
				var admissible = AttractionScore.ExcitementMatch( Settings.Types[guest.Type].PreferredExcitement, excitement ) > 10;
				if ( originalScore < AttractionScore.CandidateFloor && !admissible )
					Assert.IsTrue( smart < AttractionScore.CandidateFloor || raw >= AttractionScore.CandidateFloor, $"excitement {excitement}, column {column}: a ride the original skips and the gate would refuse is not lifted by the floor" );
			}
		Assert.IsTrue( protectedCases > 0, "the sweep reached the floor protection" );
	}

	[TestMethod]
	public void TheCandidatesKeptAtTheFloorKeepTheOrderOfTheirOwnScores()
	{
		var smarter = new SmarterGuestDecisions();
		var context = new DecisionContext( Settings, 12, false, 3 );
		var guest = Guest();
		// the same far ride with a growing expected wait: the lifted score never rises with the wait, and reaches the floor only
		uint? previous = null;
		var distinct = new HashSet<uint>();
		foreach ( var reservations in new[] { 0, 1, 2, 4, 8, 16, 40, 100 } )
		{
			var view = Ride( queue: 2, reservations: reservations, interval: 20 * 256 ) with { BackCell = (40, 4), EntranceCell = (40, 5) };
			var score = smarter.ScoreAttraction( context, guest, view );
			distinct.Add( score );
			Assert.IsTrue( previous == null || score <= previous, $"{score} after {previous}" );
			previous = score;
		}
		Assert.IsTrue( distinct.Count >= 2, "a longer wait still ranks lower after the floor" );
	}

	[TestMethod]
	public void AnUnusedRideTheGuestWouldBeAdmittedToIsKeptAtTheFloorWhereTheOriginalScoresItBelow()
	{
		var smarter = new SmarterGuestDecisions();
		var context = new DecisionContext( Settings, 12, false, 3 );
		var found = 0;
		foreach ( var column in new[] { 20, 30, 40, 60 } )
			foreach ( var excitement in new[] { 40, 55, 70, 85 } )
			{
				var view = Ride( id: 9, queue: 1, interval: 20 * 256 ) with { LiveExcitement = excitement, BackCell = (column, 4), EntranceCell = (column, 5) };
				var guest = Guest();
				var type = Settings.Types[guest.Type];
				var admissible = AttractionScore.ExcitementMatch( type.PreferredExcitement, excitement ) > 10;
				var original = OriginalGuestDecisions.Instance.ScoreAttraction( context, guest, view );
				if ( original >= AttractionScore.CandidateFloor )
					continue;
				var fresh = smarter.ScoreAttraction( context, guest, view );
				Assert.AreEqual( admissible, fresh >= AttractionScore.CandidateFloor, $"column {column}, excitement {excitement}: original {original}, smarter {fresh}" );
				if ( admissible )
				{
					found++;
					var used = guest with { UsedHistory = Array.AsReadOnly( new[] { 0, 9, 0, 0 } ) };
					Assert.IsTrue( smarter.ScoreAttraction( context, used, view ) < AttractionScore.CandidateFloor, "a ride it used lately is not lifted" );
					var temporary = guest with { TemporaryHistory = Array.AsReadOnly( new[] { 9, 0, 0, 0 } ) };
					Assert.IsTrue( smarter.ScoreAttraction( context, temporary, view ) < AttractionScore.CandidateFloor, "nor one it turned away from" );
					Assert.IsTrue( smarter.ScoreAttraction( context, guest, view with { SameTypeAsLastUsed = true } ) < AttractionScore.CandidateFloor, "nor one of the type it used last" );
				}
			}
		Assert.IsTrue( found > 0, "the sweep reached the fallback" );
	}

	// ---- The preference -----------------------------------------------------------------------

	[TestMethod]
	public void ThePreferenceStaysInItsDocumentedBandCoversItAndIsStable()
	{
		var values = new List<int>();
		for ( ulong guest = 1; guest <= 400; guest++ )
			for ( var attraction = 1; attraction <= 20; attraction++ )
				values.Add( SmarterGuestsRules.PreferencePercent( SmarterGuestsState.Trait( 9, guest, SmarterGuestsRules.PreferenceTrait ), attraction ) );
		Assert.AreEqual( 94, values.Min() );
		Assert.AreEqual( 106, values.Max() );
		Assert.AreEqual( SmarterGuestsRules.PreferenceValues, values.Distinct().Count(), "every value of the band occurs" );
		Assert.AreEqual( 94, SmarterGuestsRules.PreferenceMinimumPercent );
		Assert.AreEqual( 13, SmarterGuestsRules.PreferenceValues );
		var counts = values.GroupBy( value => value ).ToDictionary( group => group.Key, group => group.Count() );
		Assert.IsTrue( counts.Values.All( count => count > 8000 / 13 / 2 && count < 8000 / 13 * 2 ), $"roughly even: {string.Join( ", ", counts.OrderBy( pair => pair.Key ).Select( pair => $"{pair.Key}:{pair.Value}" ) )}" );
		// stable: the same guest and attraction give the same percent whoever asks and whenever
		var context = new DecisionContext( Settings, 5, false, 9 );
		var guest7 = Guest( 7 );
		Assert.AreEqual( SmarterGuestsRules.PreferencePercent( context.Trait( guest7, SmarterGuestsRules.PreferenceTrait ), 3 ), SmarterGuestsRules.PreferencePercent( (context with { ParkTurn = 4000, Raining = true }).Trait( guest7, SmarterGuestsRules.PreferenceTrait ), 3 ) );
		// it is its own trait: not the price sensitivity
		Assert.AreNotEqual( SmarterGuestsRules.PriceSensitivityTrait, SmarterGuestsRules.PreferenceTrait );
	}

	[TestMethod]
	public void EqualRidesGetDifferentGuestsFavouritesAndTheSameGuestKeepsItsFavourite()
	{
		var context = new DecisionContext( Settings, 12, false, 21 );
		var favourites = new Dictionary<int, int>();
		for ( ulong birth = 1; birth <= 200; birth++ )
		{
			var guest = Guest( birth );
			var a = SmarterGuestsRules.PreferencePercent( context.Trait( guest, SmarterGuestsRules.PreferenceTrait ), 1 );
			var b = SmarterGuestsRules.PreferencePercent( context.Trait( guest, SmarterGuestsRules.PreferenceTrait ), 2 );
			var pick = a == b ? 0 : a > b ? 1 : 2;
			favourites[pick] = favourites.GetValueOrDefault( pick ) + 1;
		}
		Assert.IsTrue( favourites[1] > 60 && favourites[2] > 60, $"two identical rides share the guests: {string.Join( ", ", favourites.OrderBy( pair => pair.Key ) )}" );
		// through the policy: with the rides equal, the favourite scores higher, and a guest without a favourite scores them alike
		var smarter = new SmarterGuestDecisions();
		var scored = 0;
		for ( ulong birth = 1; birth <= 200; birth++ )
		{
			var guest = Guest( birth );
			var a = SmarterGuestsRules.PreferencePercent( context.Trait( guest, SmarterGuestsRules.PreferenceTrait ), 1 );
			var b = SmarterGuestsRules.PreferencePercent( context.Trait( guest, SmarterGuestsRules.PreferenceTrait ), 2 );
			var (scoreA, scoreB) = (smarter.ScoreAttraction( context, guest, Ride( id: 1 ) ), smarter.ScoreAttraction( context, guest, Ride( id: 2 ) ));
			if ( a > b )
				Assert.IsTrue( scoreA >= scoreB );
			else if ( b > a )
				Assert.IsTrue( scoreB >= scoreA );
			else
				Assert.AreEqual( scoreA, scoreB );
			if ( scoreA != scoreB )
				scored++;
		}
		Assert.IsTrue( scored > 20, $"the preference breaks the tie for {scored} of 200 guests" );
	}

	[TestMethod]
	public void ThePreferenceScalesAScoreByItsPercentAndKeepsZeroAtZero()
	{
		Assert.AreEqual( 0u, SmarterGuestsRules.ApplyPreference( 0, 106 ) );
		Assert.AreEqual( 50u, SmarterGuestsRules.ApplyPreference( 50, 100 ) );
		Assert.AreEqual( 53u, SmarterGuestsRules.ApplyPreference( 50, 106 ) );
		Assert.AreEqual( 47u, SmarterGuestsRules.ApplyPreference( 50, 94 ) );
		Assert.AreEqual( uint.MaxValue, SmarterGuestsRules.ApplyPreference( uint.MaxValue, 106 ), "saturates" );
	}

	// ---- Hysteresis ---------------------------------------------------------------------------

	[TestMethod]
	public void TheMarginIsTwentyPercentOfTheTargetsScoreAndAtLeastTwoPoints()
	{
		Assert.AreEqual( 20, SmarterGuestsRules.HysteresisPercent );
		Assert.AreEqual( 2, SmarterGuestsRules.HysteresisMinimumPoints );
		Assert.IsTrue( SmarterGuestsRules.BeatsTargetByMargin( 5, 5 ), "a target below the choice floor holds nothing: any candidate wins" );
		Assert.IsTrue( SmarterGuestsRules.BeatsTargetByMargin( 0, 0 ) );
		// 10: 20% is 2, so 12 wins and 11 does not
		Assert.IsFalse( SmarterGuestsRules.BeatsTargetByMargin( 10, 11 ) );
		Assert.IsTrue( SmarterGuestsRules.BeatsTargetByMargin( 10, 12 ) );
		// 60: 20% is 12
		Assert.IsFalse( SmarterGuestsRules.BeatsTargetByMargin( 60, 71 ) );
		Assert.IsTrue( SmarterGuestsRules.BeatsTargetByMargin( 60, 72 ) );
		Assert.IsFalse( SmarterGuestsRules.BeatsTargetByMargin( 60, 30 ), "a worse candidate never wins" );
		Assert.IsFalse( SmarterGuestsRules.BeatsTargetByMargin( 40, 40 ), "an equal one does not" );
		Assert.IsFalse( SmarterGuestsRules.BeatsTargetByMargin( uint.MaxValue, uint.MaxValue ), "no overflow at the top" );
	}

	[TestMethod]
	public void OnlyARideThatBeatsARideByTheMarginTakesAWalkingGuestAndImpulseStopsStayAsInTheOriginal()
	{
		var smarter = new SmarterGuestDecisions();
		var context = new DecisionContext( Settings, 12, false, 3 );
		var guest = Guest();
		var ride = Ride();
		var shop = Ride( id: 8, kind: RideVisitorKind.Shop );
		Assert.IsFalse( smarter.SwitchesTarget( context, guest, ride, 40, ride with { Id = 6 }, 44 ), "a better ride, but not by the margin" );
		Assert.IsTrue( smarter.SwitchesTarget( context, guest, ride, 40, ride with { Id = 6 }, 48 ) );
		Assert.IsFalse( smarter.SwitchesTarget( context, guest, ride, 40, ride with { Id = 6 }, 20 ), "a worse ride never takes the guest" );
		Assert.IsTrue( smarter.SwitchesTarget( context, guest, ride, 40, shop, 12 ), "a shop on the way is an impulse stop: the original's rule" );
		Assert.IsTrue( smarter.SwitchesTarget( context, guest, shop, 40, ride, 12 ), "a guest heading for a shop may still be taken by a ride in reach" );
		Assert.IsTrue( smarter.SwitchesTarget( context, guest, ride, 3, ride with { Id = 6 }, 1 ), "a target below the floor holds nothing" );
		Assert.IsTrue( ((IGuestDecisions)OriginalGuestDecisions.Instance).SwitchesTarget( context, guest, ride, 40, ride with { Id = 6 }, 1 ), "the original always switches" );
		Assert.IsFalse( ((IGuestDecisions)OriginalGuestDecisions.Instance).UsesSpreading );
		Assert.IsTrue( smarter.UsesSpreading );
	}

	// ---- The host -----------------------------------------------------------------------------

	private sealed class ViewSpy( bool spreading ) : SmarterGuestsTests.DelegatingDecisions( OriginalGuestDecisions.Instance )
	{
		public override bool UsesSpreading => spreading;
		public override bool UsesExtensionState => spreading;
		public readonly List<AttractionView> Views = new();
		public int Switches;
		public override uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction ) { Views.Add( attraction ); return base.ScoreAttraction( context, guest, attraction ); }
		public override bool SwitchesTarget( in DecisionContext context, in GuestView guest, in AttractionView target, uint targetScore, in AttractionView candidate, uint candidateScore ) { Switches++; return base.SwitchesTarget( context, guest, target, targetScore, candidate, candidateScore ); }
	}

	[TestMethod]
	public void TheOffPathFillsNoSpreadingFactTracksNoThroughputAndAsksNoHysteresis()
	{
		var spy = new ViewSpy( spreading: false );
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: spy );
		park.Run( 15_000 );
		Assert.IsTrue( spy.Views.Count > 100, "the policy was asked" );
		Assert.IsTrue( spy.Views.All( view => view.Reservations == 0 && view.TurnsPerGuest256 == 0 ), "no spreading fact for a policy that does not ask" );
		Assert.AreEqual( 0, spy.Switches );
		Assert.IsNull( park.Guests.SmarterGuests );
		Assert.AreEqual( 0, park.Guests.TargetsKept + park.Guests.TargetsSwitched );
		var plain = new ParkScenario( new WorldSeed( 1 ) );
		plain.Run( 15_000 );
		Assert.AreEqual( plain.Hash, park.Hash, "reading views changes nothing" );
		// the identity-only policy keeps the extension state but no estimate, and its section and hash are as they were
		var identity = new ParkScenario( new WorldSeed( 1 ), decisions: new SmarterGuestsTests.IdentityOnlyDecisions() );
		identity.Run( 15_000 );
		Assert.AreEqual( 0, identity.Guests.SmarterGuests!.Throughputs.Count );
		Assert.IsNull( SmarterGuestsSave.Capture( identity.Guests )!.Throughputs );
		Assert.IsFalse( Encoding.UTF8.GetString( ParkResumeJson.Serialize( SmarterGuestsSave.Capture( identity.Guests )! ) ).Contains( "Throughputs" ) );
	}

	[TestMethod]
	public void ThePolicyThatSpreadsGetsReservationsAndTimesPerGuestAndTheHostTracksThroughput()
	{
		var spy = new ViewSpy( spreading: true );
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: spy );
		park.Run( 15_000 );
		var joinable = spy.Views.Where( view => view.HasJoinCell ).ToArray();
		Assert.IsTrue( joinable.Length > 100 );
		Assert.IsTrue( joinable.All( view => view.TurnsPerGuest256 >= RideThroughputs.MinimumInterval256 ), "every candidate has an estimate, the cold start at least" );
		Assert.IsTrue( joinable.Any( view => view.Reservations > 0 ), "guests walking to an attraction are counted" );
		Assert.IsTrue( joinable.Any( view => view.TurnsPerGuest256 != RideThroughputs.ColdStartInterval256( 1, view.Kind ) ), "measured attractions leave the cold start" );
		var estimates = park.Guests.SmarterGuests!.Throughputs;
		Assert.IsTrue( estimates.Count >= 4, $"{estimates.Count} attractions have boarded a guest" );
		Assert.IsTrue( estimates.Entries.Count( entry => entry.Samples > 0 ) >= 3, "and most have been measured" );
		CollectionAssert.AreEqual( estimates.Entries.Select( entry => entry.AttractionId ).Order().ToArray(), estimates.Entries.Select( entry => entry.AttractionId ).ToArray() );
		// the host's boardings are the estimator's input: per attraction, the samples cannot exceed the boardings
		foreach ( var entry in estimates.Entries )
			Assert.IsTrue( entry.Samples < park.Attractions.Single( item => item.Bridge.AttractionId == entry.AttractionId ).Bridge.BoardedTotal, $"attraction {entry.AttractionId}" );
		Assert.IsTrue( spy.Switches > 0 || park.Guests.TargetsKept + park.Guests.TargetsSwitched == 0 );
	}

	[TestMethod]
	public void AGuestsOwnReservationIsNotCountedAgainstTheAttractionItIsWalkingTo()
	{
		var spy = new ViewSpy( spreading: true );
		var park = new ParkScenario( new WorldSeed( 2 ), decisions: spy );
		park.Run( 12_000 );
		var walking = park.Guests.Guests.Where( guest => guest.State == GuestState.GoingToRide ).ToArray();
		Assert.IsTrue( walking.Length >= 2, "the fixture has guests on their way" );
		var target = walking[0].AttractionId;
		var others = walking.Count( guest => guest.AttractionId == target && guest != walking[0] );
		var bridge = park.Attractions.Single( item => item.Bridge.AttractionId == target ).Bridge;
		spy.Views.Clear();
		park.Guests.ScoreAttraction( walking[0], bridge );
		Assert.AreEqual( others, spy.Views.Single().Reservations, "the other guests on the way, not the asking guest" );
		spy.Views.Clear();
		var stranger = park.Guests.Guests.First( guest => guest.State != GuestState.GoingToRide || guest.AttractionId != target );
		park.Guests.ScoreAttraction( stranger, bridge );
		Assert.AreEqual( others + 1, spy.Views.Single().Reservations, "a guest not heading there counts every walker, the first one included" );
	}

	[TestMethod]
	public void AChangeOfTheLiveCapacityFromTheRideSettingsRestartsTheEstimate()
	{
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		var estimates = park.Guests.SmarterGuests!.Throughputs;
		bool Learned( ScenarioAttraction item ) => item.Bridge.Kind == RideVisitorKind.Ride && estimates.TryGet( item.Bridge.AttractionId, out var entry ) && entry.Samples > 0;
		// run until a ride has learned from a boarding with demand throughout (how soon depends on the run's queues)
		for ( var run = 0; run < 30 && !park.Attractions.Any( Learned ); run++ )
			park.Run( 2_000 );
		var ride = park.Attractions.First( Learned );
		Assert.AreEqual( ride.Bridge.Capacity, ride.Bridge.LiveCapacity, "no settings change yet" );
		estimates.TryGet( ride.Bridge.AttractionId, out var learned );
		Assert.AreEqual( ride.Bridge.LiveCapacity, learned.Capacity );
		// the ride settings change the script's capacity variable only (OriginalObjectRuntime.RideSettings), not the catalogue capacity
		ride.Script[RideVariables.VAR_CAPACITY] = ride.Bridge.LiveCapacity + 2;
		Assert.AreNotEqual( ride.Bridge.Capacity, ride.Bridge.LiveCapacity );
		park.Run( 40 );
		estimates.TryGet( ride.Bridge.AttractionId, out var restarted );
		Assert.AreEqual( ride.Bridge.LiveCapacity, restarted.Capacity );
		Assert.IsTrue( restarted.Samples < learned.Samples, "the average restarted at the cold start of the new capacity" );
	}

	[TestMethod]
	public void ARemovedAttractionTakesItsEstimateWithIt()
	{
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		park.Run( 12_000 );
		var estimates = park.Guests.SmarterGuests!.Throughputs;
		var before = estimates.Count;
		Assert.IsTrue( before > 2 );
		var removed = park.Attractions[0].Bridge;
		Assert.IsTrue( estimates.TryGet( removed.AttractionId, out _ ) );
		park.Guests.Unregister( removed );
		Assert.IsFalse( estimates.TryGet( removed.AttractionId, out _ ) );
		Assert.AreEqual( before - 1, estimates.Count );
	}

	[TestMethod]
	public void TheScriptPolicyForwardsTheSpreadingMembersOfItsBase()
	{
		using var mods = new TempScriptMods().Add( "community.pass", "function scoreAttraction(guest, c, prev)\n  return prev\nend\n", "scoreAttraction" );
		var runtime = new ScriptModRuntime( mods.Resolve().Mods );
		var context = new DecisionContext( Settings, 12, false, 3 );
		var ride = Ride();
		var overSmarter = new ScriptGuestDecisions( new SmarterGuestDecisions(), runtime );
		Assert.IsTrue( overSmarter.UsesSpreading, "over the smarter policy" );
		Assert.IsFalse( overSmarter.SwitchesTarget( context, Guest(), ride, 40, ride with { Id = 6 }, 42 ), "the base policy's hysteresis" );
		var overOriginal = new ScriptGuestDecisions( OriginalGuestDecisions.Instance, runtime );
		Assert.IsFalse( overOriginal.UsesSpreading, "over the original" );
		Assert.IsTrue( overOriginal.SwitchesTarget( context, Guest(), ride, 40, ride with { Id = 6 }, 42 ) );
		// a park run under the wrapper over the smarter policy tracks throughput and still runs the mod
		var park = new ParkScenario( new WorldSeed( 1 ), null, overSmarter );
		park.Run( 9000 );
		Assert.IsTrue( park.Guests.SmarterGuests!.Throughputs.Count > 2 );
		Assert.IsTrue( runtime.Mods[0].Stats().Calls > 50 );
	}

	// ---- The section --------------------------------------------------------------------------

	private static ParkScenario RunWithEstimates( out SmarterGuestsSaveData section )
	{
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: new SmarterGuestDecisions() );
		park.Run( 9000 );
		section = SmarterGuestsSave.Capture( park.Guests )!;
		Assert.IsTrue( section.Throughputs is { Count: > 2 }, "the run has measured attractions" );
		return park;
	}

	private static ulong Hash( ParkScenario scenario ) => WorldStateHash.Compute( new WorldStateSources { Seed = scenario.Seed, Guests = scenario.Guests } );

	[TestMethod]
	public void TheEstimatesRoundTripThroughTheSectionAndAreHashed()
	{
		var park = RunWithEstimates( out var section );
		var json = Encoding.UTF8.GetString( ParkResumeJson.Serialize( section ) );
		Assert.IsTrue( json.Contains( "Throughputs" ) );
		var again = ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( section ) );
		CollectionAssert.AreEqual( section.Throughputs!.ToArray(), again.Throughputs!.ToArray() );
		var ids = park.Guests.Guests.Select( guest => guest.Id );
		var attractions = park.Guests.Attractions.Select( attraction => attraction.AttractionId );
		Assert.IsNull( SmarterGuestsSave.SectionProblem( again, ids, attractions ) );
		var rebuilt = new ParkScenario( park.Seed, decisions: new SmarterGuestDecisions() );
		rebuilt.Things.RestoreSnapshot( park.Things.CaptureSnapshot() );
		rebuilt.Guests.RestoreSnapshot( park.Guests.CaptureSnapshot() );
		Assert.AreEqual( 0, rebuilt.Guests.SmarterGuests!.Throughputs.Count, "a restore from the guest snapshot alone starts without estimates" );
		SmarterGuestsSave.Restore( rebuilt.Guests, again );
		CollectionAssert.AreEqual( park.Guests.SmarterGuests!.Throughputs.Entries.ToArray(), rebuilt.Guests.SmarterGuests.Throughputs.Entries.ToArray() );
		Assert.AreEqual( json, Encoding.UTF8.GetString( ParkResumeJson.Serialize( SmarterGuestsSave.Capture( rebuilt.Guests )! ) ), "capturing the restored park gives the same section" );
		Assert.AreEqual( Hash( park ), Hash( rebuilt ), "the restored park hashes like the saved one" );
		var before = Hash( park );
		park.Guests.SmarterGuests.Throughputs.Boarded( park.Attractions[0].Bridge.AttractionId, RideVisitorKind.Ride, 1, 0, 99_999, 3 );
		Assert.AreNotEqual( before, Hash( park ), "the estimates are part of the world hash" );
	}

	[TestMethod]
	public void NoEstimatesLeaveTheSectionAndTheHashAsTheyWere()
	{
		var park = new ParkScenario( new WorldSeed( 1 ), decisions: new SmarterGuestsTests.IdentityOnlyDecisions() );
		park.Run( 4000 );
		var withNone = Hash( park );
		var section = SmarterGuestsSave.Capture( park.Guests )!;
		Assert.IsNull( section.Throughputs );
		var ids = park.Guests.Guests.Select( guest => guest.Id );
		StringAssert.Contains( SmarterGuestsSave.SectionProblem( section with { Throughputs = Array.Empty<RideThroughput>() }, ids )!, "empty throughput list", "an empty list is not written, so it is refused" );
		Assert.IsNull( ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( section ) ).Throughputs, "a section from before the estimates reads as none" );
		park.Guests.SmarterGuests!.Throughputs.Boarded( 1, RideVisitorKind.Ride, 1, 0, 5, 1 );
		Assert.AreNotEqual( withNone, Hash( park ) );
		park.Guests.SmarterGuests.Throughputs.Clear();
		Assert.AreEqual( withNone, Hash( park ), "back to none: the hash is as it was" );
	}

	[TestMethod]
	public void TheEstimatesAreValidatedStrictly()
	{
		var park = RunWithEstimates( out var section );
		var ids = park.Guests.Guests.Select( guest => guest.Id ).ToArray();
		var attractions = park.Guests.Attractions.Select( attraction => attraction.AttractionId ).ToArray();
		var good = section.Throughputs!.ToArray();
		string? Problem( params RideThroughput[] entries ) => SmarterGuestsSave.SectionProblem( section with { Throughputs = entries }, ids, attractions );
		Assert.IsNull( Problem( good ) );
		StringAssert.Contains( Problem( good[0] with { AttractionId = 987654 } )!, "does not hold" );
		StringAssert.Contains( Problem( good[0] with { AttractionId = 0 } )!, "outside 1.." );
		StringAssert.Contains( Problem( good[1], good[0] )!, "strictly ascending", "out of order" );
		StringAssert.Contains( Problem( good[0], good[0] )!, "strictly ascending", "an attraction twice" );
		StringAssert.Contains( Problem( good[0] with { Capacity = 0 } )!, "below 1" );
		StringAssert.Contains( Problem( good[0] with { Interval256 = RideThroughputs.MinimumInterval256 - 1 } )!, "throughput interval" );
		StringAssert.Contains( Problem( good[0] with { Interval256 = RideThroughputs.MaximumInterval256 + 1 } )!, "throughput interval" );
		Assert.IsNull( Problem( good[0] with { Interval256 = RideThroughputs.MaximumInterval256 } ) );
		StringAssert.Contains( Problem( good[0] with { Samples = -1 } )!, "samples" );
		StringAssert.Contains( Problem( good[0] with { Samples = RideThroughputs.MaximumSamples + 1 } )!, "samples" );
		StringAssert.Contains( Problem( good[0] with { LastBoardTurn = -2 } )!, "below -1" );
		StringAssert.Contains( Problem( good[0] with { ClockTurn = -1, LastBoardTurn = -1, Demand = true } )!, "without a silence clock" );
		StringAssert.Contains( Problem( good[0] with { LastBoardTurn = 50, ClockTurn = 40 } )!, "before its last boarding" );
		Assert.IsNull( Problem( good[0] with { LastBoardTurn = -1, ClockTurn = -1, Demand = false } ) );
		// without the park's attraction list the check cannot name unknown attractions, but the shape rules still apply
		Assert.IsNull( SmarterGuestsSave.SectionProblem( section with { Throughputs = [good[0] with { AttractionId = 987654 }] }, ids ), "an unknown attraction is only refused when the list is known" );
		var restoreInto = new ParkScenario( park.Seed, decisions: new SmarterGuestDecisions() );
		restoreInto.Things.RestoreSnapshot( park.Things.CaptureSnapshot() );
		restoreInto.Guests.RestoreSnapshot( park.Guests.CaptureSnapshot() );
		var bad = Assert.ThrowsException<InvalidDataException>( () => SmarterGuestsSave.Restore( restoreInto.Guests, section with { Throughputs = [good[0] with { Capacity = 0 }] } ) );
		StringAssert.Contains( bad.Message, "inconsistent" );
		var future = Assert.ThrowsException<InvalidDataException>( () => SmarterGuestsSave.Restore( restoreInto.Guests, section with { Throughputs = [good[0] with { LastBoardTurn = restoreInto.Guests.ParkTurn + 5, ClockTurn = restoreInto.Guests.ParkTurn + 5 }] } ) );
		StringAssert.Contains( future.Message, "after the park's turn" );
		Assert.AreEqual( 0, restoreInto.Guests.SmarterGuests!.Throughputs.Count, "a refused section changes nothing in the estimates" );
		var withExtra = Encoding.UTF8.GetString( ParkResumeJson.Serialize( section ) ).Replace( "\"Samples\"", "\"Extra\":1,\"Samples\"" );
		Assert.ThrowsException<System.Text.Json.JsonException>( () => ParkResumeJson.Deserialize<SmarterGuestsSaveData>( Encoding.UTF8.GetBytes( withExtra ) ) );
	}

	[TestMethod]
	public void AnAttractionIdTheEnvelopesGuestSnapshotDoesNotHoldIsRefusedAtTheFileLevel()
	{
		var park = RunWithEstimates( out var section );
		var ids = park.Guests.Guests.Select( guest => guest.Id );
		var problem = SmarterGuestsSave.ShapeProblem( section with { Throughputs = [section.Throughputs![0] with { AttractionId = 424242 }] },
			ids, [new ParkModRecord( "opentpw.smarter-guests", "1.6.0", ModSet.NoFilesSha256 )], false, park.Guests.Attractions.Select( attraction => attraction.AttractionId ) );
		StringAssert.Contains( problem!, "424242" );
	}

	// ---- The rules are exercised by the pinned run --------------------------------------------

	[TestMethod]
	public void TheSpreadingRulesActInTheScenarioRunAndTheOffRunDoesNotSeeThem()
	{
		var off = new ParkScenario( new WorldSeed( 1 ) );
		off.Run( 16_000 );
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var on = new ParkScenario( new WorldSeed( 1 ) );
		Assert.IsInstanceOfType( on.Guests.Decisions, typeof( SmarterGuestDecisions ) );
		on.Run( 16_000 );
		var estimates = on.Guests.SmarterGuests!.Throughputs;
		Assert.IsTrue( estimates.Count >= 4, "the estimator ran for the rides and the shops" );
		Assert.IsTrue( estimates.Entries.Any( entry => entry.Samples >= 10 ), "and learned" );
		Assert.AreNotEqual( off.Hash, on.Hash );
		Assert.AreEqual( 0, off.Guests.TargetsKept + off.Guests.TargetsSwitched, "the original never asks for hysteresis" );
		Assert.IsNull( off.Guests.SmarterGuests );
		// the hysteresis acts in a run of 40 days of three seeds: it keeps guests on their ride and lets others go
		long kept = 0, switched = 0;
		foreach ( var seed in new ulong[] { 1, 2, 3 } )
		{
			var park = new ParkScenario( new WorldSeed( seed ) );
			park.Run( GuestReport.TicksForDays( 40 ) );
			kept += park.Guests.TargetsKept;
			switched += park.Guests.TargetsSwitched;
		}
		Assert.IsTrue( kept > 0 && switched > 0, $"kept {kept}, switched {switched}" );
	}
}
