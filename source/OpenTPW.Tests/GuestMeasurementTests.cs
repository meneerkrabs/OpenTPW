using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The passive guest measurement of phase 0a (docs/SMARTER-GUESTS.md): the instrumentation, <see cref="ParkScenario"/> and the guest report.</summary>
[TestClass]
public class GuestMeasurementTests
{
	/// <summary>The guests' state hash of <see cref="ScenarioRun"/>, pinned like the other determinism pins: a change moves the baseline of docs/SMARTER-GUESTS.md.</summary>
	private const ulong ScenarioFixedRunHash = 0xEB32FB89F86B1110UL;

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
			Assert.IsTrue( queue.Attempts > 100, $"seed {seed}: {queue.Attempts} attempts" );
			Assert.AreEqual( queue.Attempts, queue.Boarded + queue.Abandoned + queue.Rejected + queue.Unfinished, $"seed {seed}" );
			Assert.IsTrue( queue.IsConsistent );
			Assert.IsTrue( queue.Boarded > 0 && queue.Rejected > 0 && queue.Abandoned > 0 && queue.Unfinished > 0, $"seed {seed}: every outcome occurs in a long run" );
			Assert.AreEqual( queue.Attempts, snapshot.Attractions.Sum( item => item.Attempts ) );
			Assert.AreEqual( queue.Boarded, snapshot.Attractions.Sum( item => item.Boarded ) );
			Assert.AreEqual( queue.Abandoned, snapshot.Attractions.Sum( item => item.Abandoned ) );
			Assert.AreEqual( queue.Rejected, snapshot.Attractions.Sum( item => item.Rejected ) );
			Assert.IsTrue( queue.RejectedByReason.ContainsKey( nameof( QueueEndReason.Price ) ) || queue.RejectedByReason.ContainsKey( nameof( QueueEndReason.Full ) ) );
			Assert.IsTrue( snapshot.Attractions.Sum( item => item.Uses ) >= queue.Boarded, "uses count every boarding of the window" );
		}
	}

	[TestMethod]
	public void TheScenarioExercisesEveryRefusalReasonOfItsKind()
	{
		var park = new ParkScenario( new WorldSeed( 1 ) );
		var metrics = new GuestMetrics();
		metrics.Attach( park.Guests );
		park.Run( GuestReport.TicksForDays( 6 ) );
		metrics.BeginWindow();
		park.Run( GuestReport.TicksForDays( 60 ) );
		var reasons = metrics.EndWindow().Queue.RejectedByReason;
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
