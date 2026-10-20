using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The passive guest measurement of phase 0a (docs/SMARTER-GUESTS.md): the instrumentation, <see cref="ParkScenario"/> and the guest report.</summary>
[TestClass]
[DoNotParallelize]
public class GuestMeasurementTests
{
	/// <summary>The guests' state hash of <see cref="ScenarioRun"/>, pinned like the other determinism pins: a change moves the baseline of docs/SMARTER-GUESTS.md.</summary>
	private const ulong ScenarioFixedRunHash = 0xA7A17A97F4D7F7D7UL;

	private static ParkScenario ScenarioRun( ulong seed, IGuestObserver? observer = null, int ticks = 6000 )
	{
		var park = new ParkScenario( new WorldSeed( seed ) );
		park.Guests.Observer = observer;
		park.Run( ticks );
		return park;
	}

	private static GuestReportOptions SmallReport() => new() { Seeds = new ulong[] { 1, 2 }, WarmupDays = 1, MeasuredDays = 4, Performance = false };

	[TestMethod]
	public void TheObserverIsNullByDefault()
	{
		Assert.IsNull( new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 ).Observer );
		Assert.IsNull( new ParkScenario( new WorldSeed( 1 ) ).Guests.Observer );
	}

	[TestMethod]
	public void ParkScenarioHasTheRepresentativeContents()
	{
		var park = new ParkScenario( new WorldSeed( 1 ) );
		RideVisitorBridge Of( string role ) => park.Attractions.Single( item => item.Role == role ).Bridge;
		var (twinA, twinB) = (Of( "twin-a" ), Of( "twin-b" ));
		Assert.AreNotEqual( twinA.AttractionId, twinB.AttractionId );
		Assert.AreEqual( twinA.ExcitementLevel, twinB.ExcitementLevel, "two identical rides" );
		Assert.AreEqual( twinA.Capacity, twinB.Capacity );
		Assert.IsTrue( Of( "thrill" ).ExcitementLevel > twinA.ExcitementLevel && Of( "calm" ).ExcitementLevel < twinA.ExcitementLevel, "a high and a low excitement ride" );
		Assert.IsTrue( Of( "shop-dear" ).Price > Of( "shop-cheap" ).Price && Of( "shop-cheap" ).Price > 0, "two shops at different prices" );
		Assert.IsTrue( Of( "toilet" ).ProvidesRelief );
		Assert.AreEqual( 7, park.Attractions.Count );
		var gate = park.Settings.ArrivalLaneA![^1];
		var near = park.Grid.Distance( gate.X, gate.Y, twinA.EntranceCell.X, twinA.EntranceCell.Y );
		var far = park.Grid.Distance( gate.X, gate.Y, Of( "toilet" ).EntranceCell.X, Of( "toilet" ).EntranceCell.Y );
		Assert.IsTrue( near > 0 && far > 2 * near, $"a short walk to the rides ({near}) and a long one to the toilet ({far})" );
		foreach ( var item in park.Attractions )
			Assert.IsNotNull( item.Bridge.JoinCell, $"{item.Role} can be queued for" );
	}

	[TestMethod]
	public void ParkScenarioIsDeterministic()
	{
		Assert.AreEqual( ScenarioRun( 1 ).Hash, ScenarioRun( 1 ).Hash );
		Assert.AreNotEqual( ScenarioRun( 1 ).Hash, ScenarioRun( 2 ).Hash );
		var park = ScenarioRun( 1 );
		Assert.IsTrue( park.Guests.Admissions > 0 && park.Attractions.Sum( item => item.Bridge.ReleasedTotal ) > 0, "guests arrive, queue and ride" );
	}

	[TestMethod]
	public void ParkScenarioFixedRunHashIsPinned()
	{
		var hash = ScenarioRun( 1, null, 12_000 ).Hash;
		Assert.AreEqual( ScenarioFixedRunHash, hash, $"0x{hash:X16}" );
	}

	/// <summary>
	/// The scenario's guests with <c>opentpw.smarter-guests</c> 1.4.0 on (docs/SMARTER-GUESTS.md, phases 2 to 5; this scenario has no food shop and nobody near a need level at a decision, so phase 5 does not act in it; the pin has held since 1.2.0, because the queue patience rules of 1.3.0 do not act in this 12,000-tick run either). The scenario's dear shop (value for money) and its four rides (the queue estimate, the preference, the queue patience and the
	/// hysteresis) are where the rules act, so unlike the synthetic park's pin this one moves with the rules: it is the on-path pin of the behaviour. A deliberate change of a rule constant changes this value (and bumps the extension's version).
	/// </summary>
	private const ulong ScenarioSmarterFixedRunHash = 0xBDC91FF7BA167AFFUL;

	/// <summary>The same run with the path rules of 1.5.0 (phase 6) on, the shipped extension. Moved from <see cref="ScenarioSmarterFixedRunHash"/> (1.4.0's, which the rule sets without the path rules still reproduce) because the scenario's guests crowd the street and the rules move wander goals and complain; the stronger rule of the final sweep acts in this 12,000-tick run, which the first version did not.</summary>
	private const ulong ScenarioSmarterPathsFixedRunHash = 0x37A002F5FE807C35UL;

	[TestMethod]
	public void ParkScenarioFixedRunHashWithTheSmarterGuestsExtensionOnIsPinned()
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var park = ScenarioRun( 1, null, 12_000 );
		Assert.IsInstanceOfType( park.Guests.Decisions, typeof( SmarterGuestDecisions ) );
		Assert.AreNotEqual( ScenarioFixedRunHash, park.Hash, "the rules change the scenario's guests" );
		Assert.AreEqual( ScenarioSmarterPathsFixedRunHash, park.Hash, $"0x{park.Hash:X16}" );
		// the phase 5 rules switched off are version 1.3.0's guests, so the pin of 1.3.0 holds for them as it was set
		var phase4 = new ParkScenario( new WorldSeed( 1 ), decisions: new SmarterGuestDecisions( SmarterRuleSet.None ) );
		phase4.Run( 12_000 );
		Assert.AreEqual( ScenarioSmarterFixedRunHash, phase4.Hash, $"0x{phase4.Hash:X16}" );
	}

	/// <summary>
	/// The scenario with its food shop (<see cref="ParkScenarioOptions.FoodShop"/>) and the extension on (1.5.0, the path rules included, so the pin moved from main's 1.4.0 value with them): guests eat, so the meal rule (docs/SMARTER-GUESTS.md, phase 5) acts. The on-path pin of phase 5, which the scenario
	/// pin above cannot be: it did not move, because the rules of phase 5 need a meal or a meter near a level. A deliberate change of a phase 5 constant changes this value (and bumps the extension's version).
	/// </summary>
	private const ulong ScenarioFoodSmarterFixedRunHash = 0x04442F9535C03C94UL;

	[TestMethod]
	public void ParkScenarioWithAFoodShopAndTheSmarterGuestsExtensionOnIsPinned()
	{
		using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var park = new ParkScenario( new WorldSeed( 1 ), new ParkScenarioOptions { FoodShop = true } );
		park.Run( 12_000 );
		Assert.IsInstanceOfType( park.Guests.Decisions, typeof( SmarterGuestDecisions ) );
		var phase4 = new ParkScenario( new WorldSeed( 1 ), new ParkScenarioOptions { FoodShop = true }, new SmarterGuestDecisions( SmarterRuleSet.None ) );
		phase4.Run( 12_000 );
		Assert.AreNotEqual( phase4.Hash, park.Hash, "the phase 5 rules change the run once guests eat" );
		Assert.IsTrue( park.Guests.SmarterGuests!.Meals.Count > 0 && phase4.Guests.SmarterGuests!.Meals.Count == 0, "the meal times are kept by 1.4.0 and not by 1.3.0's rules" );
		var hash = WorldStateHash.Compute( new WorldStateSources { Seed = park.Seed, Guests = park.Guests } );
		Assert.AreEqual( ScenarioFoodSmarterFixedRunHash, hash, $"0x{hash:X16} (the guests' own hash is 0x{park.Hash:X16})" );
	}

	/// <summary>
	/// The scenario's litter variant with four litter bins on the street (<see cref="ParkScenarioOptions.Bins"/>, a litter effect of 25 and a handyman proxy every 400 turns: the guests fill their litter meters) with the extension
	/// on (1.6.0: the bin rule, phase 6b) and with 1.5.0's rules, which look three cells around for a bin where 1.6.0 looks eight. The bins stand beyond three cells of where the guests litter, so 1.5.0's guests drop everything; the pin of the
	/// shipped rules is where the bin rule is held. A deliberate change of the radius changes it (and bumps the extension's version). Moved by nothing else: no other scenario, level or save has a bin the rule can reach.
	/// </summary>
	private const ulong ScenarioBinsSmarterFixedRunHash = 0x7539BB3BB9729317UL, ScenarioBinsPhase6FixedRunHash = 0xD2AF869CDF9A7D3DUL;

	[TestMethod]
	public void ParkScenarioWithBinsAndTheSmarterGuestsExtensionOnIsPinned()
	{
		var options = new ParkScenarioOptions { FoodShop = true, Litter = true, Bins = true, LitterEffect = 25, HandymanTurns = 400, ArrivalTimeBetween = 10 };
		(ulong Hash, long Binned) Run( SmarterRuleSet rules )
		{
			var park = new ParkScenario( new WorldSeed( 1 ), options, new SmarterGuestDecisions( rules ) );
			var binned = 0L;
			park.Guests.LitterBinned += ( _, _ ) => binned++;
			park.Run( 24_000 );
			return (park.Hash, binned);
		}
		var shipped = Run( SmarterRuleSet.All );
		var before = Run( SmarterRuleSet.Phase6 );
		Assert.IsTrue( shipped.Binned > 0 && before.Binned == 0, "only the bin rule reaches the bins" );
		Assert.AreEqual( ScenarioBinsSmarterFixedRunHash, shipped.Hash, $"0x{shipped.Hash:X16}" );
		Assert.AreEqual( ScenarioBinsPhase6FixedRunHash, before.Hash, $"0x{before.Hash:X16}" );
		// bins beyond the original's reach change nothing for the original's radius: the same run without any bin
		var withoutBins = new ParkScenario( new WorldSeed( 1 ), new ParkScenarioOptions { FoodShop = true, Litter = true, LitterEffect = 25, HandymanTurns = 400, ArrivalTimeBetween = 10 }, new SmarterGuestDecisions( SmarterRuleSet.Phase6 ) );
		withoutBins.Run( 24_000 );
		Assert.AreEqual( before.Hash, withoutBins.Hash, "1.5.0's guests never reach these bins" );
	}

	[TestMethod]
	public void TurningTheObserverOnDoesNotChangeTheHashesOfAFixedRun()
	{
		Assert.AreEqual( DeterminismTests.FixedRunHash, DeterminismTests.RunFixedPark( null ) );
		var metrics = new GuestMetrics();
		metrics.BeginWindow();
		Assert.AreEqual( DeterminismTests.FixedRunHash, DeterminismTests.RunFixedPark( metrics ), "the pinned fixed run is unchanged with an observer" );
		Assert.IsTrue( metrics.EndWindow().Admissions > 0, "the observer saw the run" );
		foreach ( var seed in new ulong[] { 1, 5 } )
		{
			var plain = ScenarioRun( seed, null, 8000 );
			var scenarioMetrics = new GuestMetrics();
			var observed = new ParkScenario( new WorldSeed( seed ) );
			scenarioMetrics.Attach( observed.Guests );
			observed.Run( 3000 );
			scenarioMetrics.BeginWindow();
			observed.Run( 5000 );
			scenarioMetrics.EndWindow();
			Assert.AreEqual( plain.Hash, observed.Hash, $"seed {seed}: an observer and a measured window leave the guests' state hash alone" );
			Assert.AreEqual( plain.Guests.Admissions, observed.Guests.Admissions );
			Assert.AreEqual( plain.Guests.Departed, observed.Guests.Departed );
		}
	}

	[TestMethod]
	public void EveryQueueAttemptEndsAsExactlyOneOutcome()
	{
		long boarded = 0, rejected = 0, abandoned = 0, unfinished = 0;
		foreach ( var seed in new ulong[] { 1, 2, 3 } )
		{
			var park = new ParkScenario( new WorldSeed( seed ) );
			var metrics = new GuestMetrics();
			metrics.Attach( park.Guests );
			park.Run( GuestReport.TicksForDays( 6 ) );
			metrics.BeginWindow();
			park.Run( GuestReport.TicksForDays( 40 ) );
			var snapshot = metrics.EndWindow();
			var queue = snapshot.Queue;
			(boarded, rejected, abandoned, unfinished) = (boarded + queue.Boarded, rejected + queue.Rejected, abandoned + queue.Abandoned, unfinished + queue.Unfinished);
			Assert.IsTrue( queue.Attempts > 100, $"seed {seed}: {queue.Attempts} attempts" );
			Assert.AreEqual( 0, queue.Superseded, $"seed {seed}: no attempt ends unreported" );
			Assert.AreEqual( queue.Attempts, queue.Boarded + queue.Abandoned + queue.Rejected + queue.Unfinished + queue.Superseded, $"seed {seed}" );
			Assert.IsTrue( queue.IsConsistent );
			Assert.AreEqual( queue.Attempts, snapshot.Attractions.Sum( item => item.Attempts ) );
			Assert.AreEqual( queue.Boarded, snapshot.Attractions.Sum( item => item.Boarded ) );
			Assert.AreEqual( queue.Abandoned, snapshot.Attractions.Sum( item => item.Abandoned ) );
			Assert.AreEqual( queue.Rejected, snapshot.Attractions.Sum( item => item.Rejected ) );
			Assert.IsTrue( queue.RejectedByReason.ContainsKey( nameof( QueueEndReason.Price ) ) || queue.RejectedByReason.ContainsKey( nameof( QueueEndReason.Full ) ) );
			Assert.IsTrue( snapshot.Attractions.Sum( item => item.Uses ) >= queue.Boarded, "uses count every boarding of the window" );
		}
		// an attempt still running when the window ends (Unfinished) is a matter of timing, so every outcome is asked of the three long runs together
		Assert.IsTrue( boarded > 0 && rejected > 0 && abandoned > 0 && unfinished > 0, $"every outcome occurs in the long runs ({boarded}, {rejected}, {abandoned}, {unfinished})" );
	}

	[TestMethod]
	public void TheScenarioExercisesEveryRefusalReasonOfItsKind()
	{
		var reasons = new Dictionary<string, long>();
		foreach ( var seed in new ulong[] { 1, 2, 3, 4 } )
		{
			var park = new ParkScenario( new WorldSeed( seed ) );
			var metrics = new GuestMetrics();
			metrics.Attach( park.Guests );
			park.Run( GuestReport.TicksForDays( 6 ) );
			metrics.BeginWindow();
			park.Run( GuestReport.TicksForDays( 60 ) );
			foreach ( var (reason, count) in metrics.EndWindow().Queue.RejectedByReason )
				reasons[reason] = reasons.GetValueOrDefault( reason ) + count;
		}
		foreach ( var reason in new[] { QueueEndReason.Full, QueueEndReason.Price, QueueEndReason.TooScary, QueueEndReason.TooBoring } )
			Assert.IsTrue( reasons.GetValueOrDefault( reason.ToString() ) > 0, $"{reason} occurs" );
	}

	[TestMethod]
	public void WaitPercentilesCoverOnlyBoardedAttemptsAndTheOtherOutcomesStaySeparate()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 );
		var ride = new RideVisitorBridge( 1, "ride", RideVisitorKind.Ride, 1, 70, 30 ) { EntranceCell = (10, 9), ExitCell = (10, 9) };
		var metrics = new GuestMetrics();
		metrics.BeginWindow();
		var guests = Enumerable.Range( 1, 6 ).Select( id => new Guest( id, 0 ) ).ToArray();
		foreach ( var (guest, wait) in guests.Take( 3 ).Zip( new long[] { 10, 20, 30 } ) )
		{
			metrics.QueueJoined( guest, ride );
			metrics.QueueBoarded( guest, ride, wait );
		}
		metrics.QueueJoined( guests[3], ride );
		metrics.QueueEnded( guests[3], 1, QueueEndReason.Unhappy, 500 );
		metrics.QueueRejected( guests[4], ride, QueueEndReason.Full );
		metrics.QueueJoined( guests[5], ride );
		metrics.QueueEnded( guests[5], 1, QueueEndReason.Price, 7 );
		metrics.QueueJoined( guests[0], ride );   // an attempt still open at the end
		var queue = metrics.EndWindow().Queue;
		Assert.AreEqual( 7, queue.Attempts );
		Assert.AreEqual( (3, 1, 2, 1), (queue.Boarded, queue.Abandoned, queue.Rejected, queue.Unfinished) );
		Assert.IsTrue( queue.IsConsistent );
		Assert.AreEqual( 20, queue.WaitP50 );
		Assert.AreEqual( 30, queue.WaitP99, "the abandoned wait of 500 turns is not among the boarded waits" );
		Assert.AreEqual( 500, queue.MeanAbandonedWait );
		Assert.AreEqual( 1, queue.RejectedByReason[nameof( QueueEndReason.Full )] );
		Assert.AreEqual( 1, queue.RejectedByReason[nameof( QueueEndReason.Price )] );
		_ = simulation;
	}

	[TestMethod]
	public void AnAttemptThatEndsUnreportedIsCountedApartAndBreaksTheConsistencyCheck()
	{
		var ride = new RideVisitorBridge( 1, "ride", RideVisitorKind.Ride, 1, 70, 30 );
		var metrics = new GuestMetrics();
		metrics.BeginWindow();
		var guest = new Guest( 1, 0 );
		metrics.QueueJoined( guest, ride );
		metrics.QueueJoined( guest, ride );   // joined again with the first attempt still open
		var other = new Guest( 2, 0 );
		metrics.QueueJoined( other, ride );
		metrics.Departed( other );            // departed with an attempt open
		var queue = metrics.EndWindow().Queue;
		Assert.AreEqual( 2, queue.Superseded );
		Assert.AreEqual( 1, queue.Unfinished );
		Assert.IsFalse( queue.IsConsistent, "a superseded attempt is never consistent" );
	}

	[TestMethod]
	public void UnfinishedAttemptsReportTheirAgeAndTheCensoredWaitsIncludeThem()
	{
		var park = new ParkScenario( new WorldSeed( 2 ) );
		var metrics = new GuestMetrics();
		metrics.Attach( park.Guests );
		park.Run( GuestReport.TicksForDays( 6 ) );
		metrics.BeginWindow();
		park.Run( GuestReport.TicksForDays( 40 ) );
		var queue = metrics.EndWindow().Queue;
		Assert.IsTrue( queue.Unfinished > 0 && queue.UnfinishedAgeMax is >= 0 );
		Assert.IsTrue( queue.CensoredWaitP99 >= queue.WaitP99, "the unfinished ages can only raise the wait percentiles" );
	}

	[TestMethod]
	public void SeedsStayDistinctOverTheExtendedWindowAndDuplicatesAreFlagged()
	{
		foreach ( var days in new[] { 40, 80 } )
		{
			var hashes = Enumerable.Range( 1, 8 ).Select( seed => ScenarioRun( (ulong)seed, null, (int)GuestReport.TicksForDays( days ) ).Hash ).ToList();
			Assert.AreEqual( 8, hashes.Distinct().Count(), $"8 distinct runs after {days} days" );
		}
		var result = GuestReport.Run( SmallReport() with { Seeds = new ulong[] { 1, 2, 1 } } );
		var scenario = result.Scenarios.Single();
		Assert.AreEqual( 2, scenario.DistinctSeeds );
		Assert.AreEqual( 1, scenario.DuplicateSeeds.Count, "the seed given twice ran the same park twice" );
		Assert.IsTrue( scenario.Spread.All( item => item.Count <= 2 ), "the spread is over the distinct seeds" );
		StringAssert.Contains( GuestReport.ToTable( result ), "WARNING: seed 1 ended in the same state as seed 1" );
		StringAssert.Contains( GuestReport.ToJson( result ), "\"duplicateSeeds\"" );
	}

	[TestMethod]
	public void ASaveAndRestoreInsideTheWindowKeepsTheTalliesWhole()
	{
		var plain = new ParkScenario( new WorldSeed( 3 ) );
		var plainMetrics = new GuestMetrics();
		plainMetrics.Attach( plain.Guests );
		var restored = new ParkScenario( new WorldSeed( 3 ) );
		var restoredMetrics = new GuestMetrics();
		restoredMetrics.Attach( restored.Guests );
		foreach ( var (park, metrics) in new[] { (plain, plainMetrics), (restored, restoredMetrics) } )
		{
			park.Run( GuestReport.TicksForDays( 6 ) );
			metrics.BeginWindow();
			park.Run( GuestReport.TicksForDays( 12 ) );
		}
		restored.Guests.RestoreSnapshot( restored.Guests.CaptureSnapshot() );
		plain.Run( GuestReport.TicksForDays( 12 ) );
		restored.Run( GuestReport.TicksForDays( 12 ) );
		Assert.AreEqual( plain.Hash, restored.Hash, "the restore changes nothing the scenario holds" );
		var (a, b) = (plainMetrics.EndWindow(), restoredMetrics.EndWindow());
		Assert.IsTrue( b.Queue.IsConsistent );
		Assert.AreEqual( a.ToJsonObject().ToJsonString(), b.ToJsonObject().ToJsonString(), "the same attempts, departures and happiness as without the restore" );
	}

	[TestMethod]
	public void GuestTimePerRideBoardingIsAGuardedHeadlineMetric()
	{
		var park = new ParkScenario( new WorldSeed( 3 ) );
		var metrics = new GuestMetrics();
		metrics.Attach( park.Guests );
		park.Run( GuestReport.TicksForDays( 6 ) );
		metrics.BeginWindow();
		park.Run( GuestReport.TicksForDays( 12 ) );
		var snapshot = metrics.EndWindow();
		var boardings = snapshot.Attractions.Where( use => use.Kind == "Ride" ).Sum( use => use.Uses );
		Assert.IsTrue( boardings > 0 );
		Assert.AreEqual( snapshot.GuestSeconds / boardings, snapshot.GuestSecondsPerRideBoarding, 1e-9, "guest time over the rides' boardings: a wait cut bought with refusals cannot hide here" );
		var headline = snapshot.Headline().ToDictionary( item => item.Name, item => item.Value );
		Assert.AreEqual( snapshot.GuestSecondsPerRideBoarding, headline["use.guestSecondsPerRide"] );
		Assert.AreEqual( -1, GuestReportCompare.Direction( "use.guestSecondsPerRide" ), "more guest time per ride is a regression" );
		Assert.IsNotNull( snapshot.ToJsonObject()["use"]!["guestSecondsPerRide"] );
	}

	[TestMethod]
	public void AttemptsStartedInTheWarmUpAreNotCounted()
	{
		var ride = new RideVisitorBridge( 1, "ride", RideVisitorKind.Ride, 1, 70, 30 );
		var metrics = new GuestMetrics();
		var early = new Guest( 1, 0 );
		metrics.QueueJoined( early, ride );
		metrics.BeginWindow();
		metrics.QueueBoarded( early, ride, 99 );
		var late = new Guest( 2, 0 );
		metrics.QueueJoined( late, ride );
		metrics.QueueBoarded( late, ride, 5 );
		var queue = metrics.EndWindow().Queue;
		Assert.AreEqual( (1L, 1L), (queue.Attempts, queue.Boarded) );
		Assert.AreEqual( 5, queue.WaitP50 );
	}

	[TestMethod]
	public void DeparturesAreRecordedWithTheLeavingPredicate()
	{
		var park = new ParkScenario( new WorldSeed( 4 ) );
		var metrics = new GuestMetrics();
		metrics.Attach( park.Guests );
		park.Run( GuestReport.TicksForDays( 10 ) );
		metrics.BeginWindow();
		park.Run( GuestReport.TicksForDays( 50 ) );
		var departures = metrics.EndWindow().Departures;
		Assert.IsTrue( departures.Total > 5, $"{departures.Total} departures" );
		Assert.IsTrue( departures.HappinessZero > 0 && departures.ExitCounterZero > 0, "both guest predicates occur" );
		Assert.AreEqual( 0, departures.Other, "every departure carries a predicate" );
		Assert.AreEqual( 0, departures.ParkClosed, "the scenario's park stays open" );
		Assert.IsTrue( departures.HappinessZero + departures.ExitCounterZero + departures.BoothRefused + departures.Guard >= departures.Total );
		Assert.AreEqual( departures.Total, departures.Admitted + departures.NeverAdmitted );
	}

	[TestMethod]
	public void HappinessIsKeptPerAdmissionCohortAndWeightedByGuestTime()
	{
		var report = GuestReport.RunScenario( 1, new GuestReportOptions { WarmupDays = 8, MeasuredDays = 30 } );
		Assert.IsTrue( report.Cohorts.Count > 5 );
		Assert.IsTrue( report.Cohorts.Any( cohort => cohort.Cohort == "carryOver" ), "guests admitted before the window are their own cohort" );
		Assert.AreEqual( report.Admissions, report.Cohorts.Where( cohort => cohort.Cohort != "carryOver" ).Sum( cohort => cohort.Admitted ) );
		Assert.AreEqual( report.GuestSeconds, report.Cohorts.Sum( cohort => cohort.GuestSeconds ), 0.01 );
		Assert.IsTrue( report.GuestTimeHappiness is > 0 and <= 100 );
		Assert.IsTrue( report.Cohorts.Any( cohort => cohort.Departed > 0 && cohort.MeanHappinessAtDeparture is < 30 ), "the guests that left are far less happy than the survivors" );
	}

	[TestMethod]
	public void GiniAndPercentileHelpers()
	{
		Assert.AreEqual( 0, GuestMetrics.Gini( new double[] { 5, 5, 5, 5 } ), 1e-12 );
		Assert.AreEqual( 0.75, GuestMetrics.Gini( new double[] { 0, 0, 0, 12 } ), 1e-12 );
		Assert.AreEqual( 0, GuestMetrics.Gini( new double[] { 7 } ) );
		Assert.AreEqual( 0, GuestMetrics.Gini( new double[] { 0, 0 } ) );
		Assert.IsNull( GuestMetrics.Percentile( Array.Empty<double>(), 0.5 ) );
		var ascending = Enumerable.Range( 1, 100 ).Select( value => (double)value ).ToList();
		Assert.AreEqual( 50, GuestMetrics.Percentile( ascending, 0.5 ) );
		Assert.AreEqual( 90, GuestMetrics.Percentile( ascending, 0.9 ) );
		Assert.AreEqual( 99, GuestMetrics.Percentile( ascending, 0.99 ) );
		Assert.AreEqual( 1, GuestMetrics.Percentile( ascending, 0 ) );
	}

	[TestMethod]
	public void TheWindowOfAnAutoBeginHoldsExactlyTheTicksAfterTheMark()
	{
		var park = new ParkScenario( new WorldSeed( 1 ) );
		var metrics = new GuestMetrics { AutoBeginAtTick = 500 };
		metrics.Attach( park.Guests );
		park.Run( 1500 );
		Assert.AreEqual( 1000, metrics.EndWindow().Ticks );
	}

	[TestMethod]
	public void ARecalledGuestLosesItsAnnouncedDeparture()
	{
		var metrics = new GuestMetrics();
		metrics.BeginWindow();
		var guest = new Guest( 1, 0 );
		metrics.Leaving( guest, DepartureCause.HappinessZero );
		metrics.Recalled( guest );
		metrics.Departed( guest );
		Assert.AreEqual( 1, metrics.EndWindow().Departures.Other, "the later departure is classed on its own cause, here none" );
	}

	[TestMethod]
	public void CapacityNormalisedGiniIsReportedBesideTheRawOne()
	{
		var snapshot = GuestReport.RunScenario( 1, new GuestReportOptions { WarmupDays = 6, MeasuredDays = 30 } );
		Assert.IsTrue( snapshot.GiniRides > 0 && snapshot.GiniRidesPerCapacity >= 0 );
		Assert.IsTrue( JsonNode.Parse( snapshot.ToJsonObject().ToJsonString() )!["use"]!.AsObject().ContainsKey( "giniRidesPerCapacity" ) );
	}

	[TestMethod]
	public void ComparingReportsFlagsRegressionsAndMissedTargetsInStandardErrors()
	{
		var baseline = JsonNode.Parse( GuestReport.ToJson( GuestReport.Run( new GuestReportOptions { Seeds = Enumerable.Range( 1, 8 ).Select( seed => (ulong)seed ).ToArray(), WarmupDays = 4, MeasuredDays = 30, Performance = false } ) ) )!.AsObject();
		var same = GuestReportCompare.Compare( baseline, baseline, new[] { "queue.wait.p90" } );
		Assert.IsTrue( same.Rows.All( row => row.Verdict is "same" or "undefined" ), "a report against itself changes nothing" );
		Assert.IsTrue( same.Failed, "a named target that did not improve fails" );
		Assert.IsFalse( GuestReportCompare.Compare( baseline, baseline, Array.Empty<string>() ).Failed );
		// a candidate whose p90 wait fell by far more than the band and whose boarded share fell too
		var candidate = JsonNode.Parse( baseline.ToJsonString() )!.AsObject();
		foreach ( var item in candidate["scenarios"]![0]!["spread"]!.AsArray() )
		{
			var metric = (string)item!["metric"]!;
			if ( metric == "queue.wait.p90" )
				item["mean"] = (double)item["mean"]! * 0.05;
			if ( metric == "queue.boarded.rate" )
				item["mean"] = (double)item["mean"]! * 0.5;
		}
		var result = GuestReportCompare.Compare( baseline, candidate, new[] { "queue.wait.p90" } );
		Assert.AreEqual( "improved", result.Rows.Single( row => row.Metric == "queue.wait.p90" ).Verdict );
		Assert.AreEqual( "regressed", result.Rows.Single( row => row.Metric == "queue.boarded.rate" ).Verdict );
		Assert.IsTrue( result.Failed );
		// the band: 3 standard errors of a difference of 8 and 8 seeds = 3 · unit · sqrt(2/8); a spread of 0 still has the floor
		Assert.AreEqual( 3 * 2.0 * 0.5, GuestReportCompare.Band( "queue.wait.p90", 100, 2, 8, 3 ), 1e-9 );
		Assert.AreEqual( 3 * 1.0 * 0.5, GuestReportCompare.Band( "queue.wait.p90", 0, 0, 8, 3 ), 1e-9, "a zero mean and spread use the absolute floor" );
		Assert.IsTrue( double.IsNaN( GuestReportCompare.Band( "queue.wait.p90", 1, 1, 0, 3 ) ) );
	}

	[TestMethod]
	public void GuestsWithNoChoiceForLongAreCountedOnce()
	{
		var metrics = new GuestMetrics( new GuestMetricsOptions { NoChoiceTurns = 0 } );
		metrics.BeginWindow();
		var guest = new Guest( 1, 0 );
		metrics.Decided( guest, false );
		metrics.Decided( guest, false );
		var snapshot = metrics.EndWindow();
		Assert.AreEqual( 2, snapshot.NoChoiceEvents );
		Assert.AreEqual( 0, snapshot.StuckGuests, "both events fall in the same turn: no span yet" );
	}

	[TestMethod]
	public void TheReportJsonIsStableForFixedSeeds()
	{
		var first = GuestReport.ToJson( GuestReport.Run( SmallReport() ) );
		var second = GuestReport.ToJson( GuestReport.Run( SmallReport() ) );
		Assert.AreEqual( first, second );
		var root = JsonNode.Parse( first )!.AsObject();
		Assert.AreEqual( "opentpw.guest-report/1", (string?)root["schema"] );
		Assert.IsFalse( root.ContainsKey( "performance" ), "the machine-dependent section is left out when not asked for" );
		var scenario = root["scenarios"]!.AsArray().Single()!.AsObject();
		Assert.AreEqual( "park-scenario", (string?)scenario["name"] );
		Assert.AreEqual( 2, scenario["seeds"]!.AsArray().Count );
		var metrics = scenario["seeds"]![0]!["metrics"]!.AsObject();
		foreach ( var key in new[] { "queue", "happiness", "departures", "use", "revenue", "noChoice", "admissions" } )
			Assert.IsTrue( metrics.ContainsKey( key ), key );
		Assert.IsTrue( scenario["spread"]!.AsArray().Any( item => (string?)item!["metric"] == "queue.wait.p50" ) );
		var other = GuestReport.ToJson( GuestReport.Run( SmallReport() with { Seeds = new ulong[] { 3, 4 } } ) );
		Assert.AreNotEqual( first, other, "other seeds give another report" );
		StringAssert.Contains( GuestReport.ToTable( GuestReport.Run( SmallReport() ) ), "queue.boarded.rate" );
	}

	[TestMethod]
	public void ThePerformanceSectionIsReportedWhenAsked()
	{
		var options = SmallReport() with { Performance = true, Seeds = new ulong[] { 1 } };
		var result = GuestReport.Run( options );
		Assert.IsNotNull( result.Performance );
		Assert.IsTrue( result.Performance.ObserverOffMicrosecondsPerTick > 0 && result.Performance.ObserverOnBytesPerTick >= 0 );
		Assert.IsTrue( JsonNode.Parse( GuestReport.ToJson( result ) )!.AsObject().ContainsKey( "performance" ) );
	}
}

/// <summary>The instrumentation on the level gate's parks (needs the game's data): the real objects, queues and scripts.</summary>
[TestClass]
public class GuestMeasurementAssetTests
{
	[TestInitialize]
	public void Init()
	{
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !System.IO.Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original level objects and balance files." );
		var dataPath = System.IO.Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( System.IO.Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !System.IO.File.Exists( System.IO.Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestMethod]
	public void LevelParkAttemptsAreConsistentAndTheObserverLeavesTheHashAlone()
	{
		var options = new GuestReportOptions { WarmupDays = 2, MeasuredDays = 30, LevelArrivalTimeBetween = 5 };
		foreach ( var level in new[] { "jungle", "hallow", "fantasy", "space" } )
		{
			var observed = GuestReport.RunLevel( level, 1, options );
			var queue = observed.Snapshot.Queue;
			Assert.IsTrue( queue.Attempts > 20, $"{level}: {queue.Attempts} attempts" );
			Assert.IsTrue( queue.IsConsistent, $"{level}: attempts {queue.Attempts} = {queue.Boarded} + {queue.Abandoned} + {queue.Rejected} + {queue.Unfinished}, superseded {queue.Superseded}" );
			ulong plainHash = 0;
			var gate = new M3GateOptions { Level = level, Seed = new WorldSeed( 1 ).GuestStream, WorldSeed = new WorldSeed( 1 ), DeterminismProbe = false, SweptTurns = true, ArrivalTimeBetween = 5, BeforeTeardown = guests => plainHash = guests.ComputeStateHash() };
			M3GateRun.Execute( gate, GuestReport.TicksForDays( options.WarmupDays + options.MeasuredDays ) );
			Assert.IsTrue( queue.Unfinished > 0, $"{level}: guests are still queued when the window closes" );
			Assert.AreEqual( plainHash, observed.StateHash, $"{level}: the observer leaves the guests' state hash alone" );
		}
	}
}
