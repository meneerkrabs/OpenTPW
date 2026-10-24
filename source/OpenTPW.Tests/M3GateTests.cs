using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>M3 gate report and time mapping (asset-free; docs/M3-GATE.md).</summary>
[TestClass]
public class M3GateTests
{
	[TestMethod]
	public void GateMinutesAreNormalSpeedSimulationMinutes()
	{
		Assert.AreEqual( 108_000, M3Gate.TicksForMinutes( 30 ) );
		var mapping = M3Gate.DescribeTimeMapping( M3Gate.TicksForMinutes( 30 ) );
		Assert.AreEqual( 1800.0, (double)mapping["simulatedSeconds"]! );
		// 108,000 ticks / 14.88 ticks per 248 ms turn; 3,750 park-clock seconds per turn.
		Assert.AreEqual( 7258L, (long)mapping["parkTurns"]! );
		Assert.AreEqual( 315L, (long)mapping["parkClockDays"]! );
	}

	[TestMethod]
	public void ReportListsEveryRowAndFailsOnAnyFailure()
	{
		var rows = new[]
		{
			new M3GateRow( "time.monotonic", "economy", "Time", M3GateVerdict.Pass, null, new JsonObject { ["violations"] = 0 } ),
			new M3GateRow( "queues.no-stuck-queue", "rides", "Queue", M3GateVerdict.Unresolved, null, new JsonObject { ["maxWaitSeconds"] = 12.5 } ),
			new M3GateRow( "build.queue", "paths", "Queue path", M3GateVerdict.Fail, 7, new JsonObject { ["placeable"] = false } )
		};
		var report = new M3GateReport( new M3GateOptions { Minutes = 1 }, M3Gate.DescribeTimeMapping( 3600 ), rows, 0.5 );
		Assert.IsTrue( report.HasFailures );
		Assert.IsFalse( new M3GateReport( new M3GateOptions(), new JsonObject(), rows.Take( 2 ).ToArray(), 0 ).HasFailures );
		// Exit codes: any Fail gives 1; Pass plus Unresolved gives 2; M3 is accepted only at 0 (every row passes).
		Assert.AreEqual( 1, report.ExitCode );
		Assert.AreEqual( 1, new M3GateReport( new M3GateOptions(), new JsonObject(), new[] { rows[0], rows[2] }, 0 ).ExitCode );
		Assert.AreEqual( 2, new M3GateReport( new M3GateOptions(), new JsonObject(), rows.Take( 2 ).ToArray(), 0 ).ExitCode );
		Assert.AreEqual( 0, new M3GateReport( new M3GateOptions(), new JsonObject(), rows.Take( 1 ).ToArray(), 0 ).ExitCode );
		var json = JsonNode.Parse( report.ToJson() )!;
		Assert.AreEqual( 3, json["rows"]!.AsArray().Count );
		Assert.AreEqual( "unresolved", (string)json["rows"]![1]!["verdict"]! );
		Assert.AreEqual( 7L, (long)json["rows"]![2]!["firstViolationTick"]! );
		Assert.AreEqual( 1, (int)json["counts"]!["fail"]! );
		var summary = report.ToSummary().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
		Assert.AreEqual( rows.Length + 2, summary.Length );
		StringAssert.StartsWith( summary[3], "FAIL" );
		StringAssert.Contains( summary[3], "first violation tick 7" );
	}

	/// <summary>
	/// The queue row's head bound follows the state-11/12 rules: two queue walks over N cells (N × C + 2 × w₂, WALK-I's
	/// per-cell term), the gap-2 move-up wait, two interludes and one update.
	/// </summary>
	[TestMethod]
	public void HeadNotReadyBoundFollowsTheQueueRules()
	{
		// WALK-I replaced the old walk term (N + 4 cells at OpenTPW's former 1 cell/s x 0.7, 194 turns for 25 cells and 50 for
		// one cell, GATE-004) with the traced steering's: 25 x 27 + 2 x 15 = 705; + trunc(1.2 x 2) + 1 = 3; + 2 x 11; + 1 (C = 27 since the original walk, GATE-006).
		Assert.AreEqual( 731L, M3GateRun.HeadNotReadyBound( 25 ).Turns );
		StringAssert.Contains( M3GateRun.HeadNotReadyBound( 25 ).Derivation, "25 x C 27 + 2 x w2 15 = 705 turns" );
		// One cell (shops, toilets): 27 + 30 = 57; + 3 + 22 + 1. An empty queue counts as one cell.
		Assert.AreEqual( 83L, M3GateRun.HeadNotReadyBound( 1 ).Turns );
		Assert.AreEqual( 83L, M3GateRun.HeadNotReadyBound( 0 ).Turns );
		StringAssert.Contains( M3GateRun.HeadNotReadyBound( 25 ).Derivation, "2 interludes x 11" );
	}

	/// <summary>The join walk and the head's own walk (WALK-I): one queue walk over k cells takes at most k × 27 + 15 updates.</summary>
	[TestMethod]
	public void QueueWalkTermsFollowTheEnumeratedPerCellTerm()
	{
		Assert.AreEqual( 27L, M3GateRun.QueueCellWalkTurns );
		Assert.AreEqual( 15L, M3GateRun.QueueWalkTurns( 0 ) );
		Assert.AreEqual( 42L, M3GateRun.QueueWalkTurns( 1 ) );
		// Joining a 25-cell queue: position 0 is in the front cell, 25 crossings from the join cell; position 50 in cell 12
		// (13 crossings); position 99 in the back cell (1 crossing).
		Assert.AreEqual( 690L, M3GateRun.JoinWalkTurns( 0, 25 ) );
		Assert.AreEqual( 366L, M3GateRun.JoinWalkTurns( 50, 25 ) );
		Assert.AreEqual( 42L, M3GateRun.JoinWalkTurns( 99, 25 ) );
		Assert.AreEqual( 42L, M3GateRun.JoinWalkTurns( 3, 1 ) );
		// W(p) with the join walk (GATE-005 closed): 690 + 178 = 868 at the head, 366 + 4,188 at p = 50, 42 + 8,021 at 99.
		Assert.AreEqual( 868L, M3GateRun.BoardingWaitBound( 0, 5, 56, 121, M3GateRun.JoinWalkTurns( 0, 25 ) ) );
		Assert.AreEqual( 4554L, M3GateRun.BoardingWaitBound( 50, 5, 56, 121, M3GateRun.JoinWalkTurns( 50, 25 ) ) );
		Assert.AreEqual( 8063L, M3GateRun.BoardingWaitBound( 99, 5, 56, 121, M3GateRun.JoinWalkTurns( 99, 25 ) ) );
	}

	/// <summary>A five-cell queue up the column x = 2: front (2, 5) before the entrance cell (2, 6), back (2, 1), join cell (2, 0).</summary>
	private static RideVisitorBridge FiveCellQueue()
	{
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		var ride = new RideVisitorBridge( 1, "Queue ride", RideVisitorKind.Ride, 5, 55, 30, GuestNeeds.None )
		{
			EntranceCell = (2, 0),
			ExitCell = (2, 0),
			QueueFrontCell = (2, 5),
			QueueEntranceDirection = 2,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
		};
		Assert.AreEqual( 5, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, 5 ).Select( index => (2, 5 - index) ) ) );
		QueuePaths.TryConnect( grid, ride );
		Assert.AreEqual( ((int, int)?)(2, 0), ride.JoinCell );
		return ride;
	}

	private static Guest HeadAt( (int X, int Y) cell, GuestState state, int position )
	{
		var head = new Guest( 1, 0 ) { State = state, QueuePosition = position };
		(head.Navigation.X, head.Navigation.Y) = GuestNavigation.Centre( cell );
		return head;
	}

	/// <summary>
	/// The head's own remaining walk (review WV-3, WV-12): counted on its walks, each k × 27 + 15 from its own start, never on
	/// <see cref="Guest.QueueCellIndex"/>; rule (a) holds a head to it plus 26 where that is below the queue bound.
	/// </summary>
	[TestMethod]
	public void HeadRemainingWalkCountsTheWalksThemselves()
	{
		var ride = FiveCellQueue();
		Assert.AreEqual( 26L, M3GateRun.HeadWaitTurns, "move-up wait 3 + 2 interludes x 11 + 1" );
		Assert.AreEqual( 5 * 27 + 30 + 26L, M3GateRun.HeadNotReadyBound( 5 ).Turns );
		// Standing at position 0: no walk left, so a head held there (an interlude that never ends) fails after 26 turns.
		var head = HeadAt( (2, 5), GuestState.Queueing, 0 );
		head.InQueueInterlude = true;
		Assert.AreEqual( 0L, M3GateRun.HeadRemainingWalkTurns( head, ride ) );
		Assert.AreEqual( 0L, M3GateRun.HeadWalkTurns( head, ride ) );
		Assert.AreEqual( 26L, M3GateRun.HeadNotReadyLimit( head, ride, M3GateRun.HeadNotReadyBound( 5 ).Turns ) );
		// Standing at an older position: the move-up from its own cell, i x 27 + 15 (H already holds one w2).
		head = HeadAt( (2, 5), GuestState.Queueing, 2 );
		Assert.AreEqual( 15L, M3GateRun.HeadRemainingWalkTurns( head, ride ) );
		Assert.AreEqual( 0L, M3GateRun.HeadWalkTurns( head, ride ) );
		head = HeadAt( (2, 3), GuestState.Queueing, 9 );
		Assert.AreEqual( 69L, M3GateRun.HeadRemainingWalkTurns( head, ride ) );
		Assert.AreEqual( 54L, M3GateRun.HeadWalkTurns( head, ride ) );
		// Walking from the join cell to position 0: the whole walk, 5 crossings.
		head = HeadAt( (2, 0), GuestState.MovingUpQueue, 0 );
		head.Navigation.SetDestination( 0, 0, [(2, 0), (2, 1), (2, 2), (2, 3), (2, 4), (2, 5)] );
		Assert.AreEqual( 150L, M3GateRun.HeadRemainingWalkTurns( head, ride ) );
		// A guest that stepped across a fold has a small QueueCellIndex while its walk is long: the walk is charged (WV-12).
		head.QueueCellIndex = 0;
		(head.Navigation.X, head.Navigation.Y) = GuestNavigation.Centre( (2, 4) );
		Assert.AreEqual( 150L, M3GateRun.HeadRemainingWalkTurns( head, ride ) );
		Assert.AreEqual( 135L, M3GateRun.HeadWalkTurns( head, ride ) );
		// Walking to a later position: that walk, then one more from the farthest cell it may be on (the join cell, 5).
		head = HeadAt( (2, 0), GuestState.MovingUpQueue, 6 );
		head.Navigation.SetDestination( 0, 0, [(2, 0), (2, 1), (2, 2), (2, 3), (2, 4)] );
		Assert.AreEqual( 4 * 27 + 15 + 5 * 27 + 15L, M3GateRun.HeadRemainingWalkTurns( head, ride ) );
		Assert.AreEqual( M3GateRun.HeadNotReadyBound( 5 ).Turns, M3GateRun.HeadNotReadyLimit( head, ride, M3GateRun.HeadNotReadyBound( 5 ).Turns ), "never above the queue bound" );
		// Called: H holds the stand walk; rule (a) keeps the queue bound.
		head.State = GuestState.Boarding;
		Assert.AreEqual( -1L, M3GateRun.HeadRemainingWalkTurns( head, ride ) );
		Assert.AreEqual( 0L, M3GateRun.HeadWalkTurns( head, ride ) );
		Assert.AreEqual( 101L, M3GateRun.HeadNotReadyLimit( head, ride, 101 ) );
		Assert.AreEqual( 101L, M3GateRun.HeadNotReadyLimit( null, ride, 101 ) );
	}

	/// <summary>The BOUNCE boarding bound (docs/reverse/BOARD-plan.md §7, §8; WALK-plan §9 walk terms) from its traced rules: P, R, H₀, w, w₂ and W(p).</summary>
	[TestMethod]
	public void BounceBoardingBoundFollowsTheTracedRules()
	{
		// P = 1 + ceil(WAIT 500 / 248) = 4.
		Assert.AreEqual( 4L, M3GateRun.BounceLoopPeriodTurns( 500 ) );
		Assert.AreEqual( 3L, M3GateRun.BounceLoopPeriodTurns( 496 ) );
		Assert.AreEqual( 3L, M3GateRun.BounceLoopPeriodTurns( RideAnimationTiming.TriggerDelay( 500, 0.5f + 60 / 100f ) ), "WAIT 500 at InitSpeed 60 is 454 ms" );
		// R from the UNBOUNCE poll rule: 4 DUR + 1 for 8 <= DUR <= 30 (121 for Belly Bounce, 41 for DUR 10); 29 for DUR <= 7.
		Assert.AreEqual( 121L, M3GateRun.BounceHoldTurns( 30, 4 ) );
		Assert.AreEqual( 41L, M3GateRun.BounceHoldTurns( 10, 4 ) );
		Assert.AreEqual( 33L, M3GateRun.BounceHoldTurns( 8, 4 ) );
		Assert.AreEqual( 29L, M3GateRun.BounceHoldTurns( 1, 4 ) );
		// H0 = 1 removal + 3 move-up + 11 interlude + 1 call + 1 notice + P.
		Assert.AreEqual( 21L, M3GateRun.BoardingHostTurns( 4 ) );
		// w = 20 and w2 = 15 from the traced steering (WALK-plan section 9), so H = 21 + 20 + 15 = 56.
		Assert.AreEqual( 56L, M3GateRun.BoardingHostTurns( 4 ) + M3GateRun.StandWalkTurns + M3GateRun.MoveUpWalkTurns );
		// W(p) = (p + 1) H + (floor(p / CAP) + 1) R + 1: 178 at the head, 4,188 at p = 50, 8,021 at Qmax - 1 = 99 (WALK-plan section 1).
		Assert.AreEqual( 178L, M3GateRun.BoardingWaitBound( 0, 5, 56, 121 ) );
		Assert.AreEqual( 4188L, M3GateRun.BoardingWaitBound( 50, 5, 56, 121 ) );
		Assert.AreEqual( 8021L, M3GateRun.BoardingWaitBound( 99, 5, 56, 121 ) );
		Assert.AreEqual( 4, M3GateRun.TracedBounceScripts.Count );
	}
}

/// <summary>Short M3 gate run on original data (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
public class M3GateAssetTests
{
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Jungle level, objects and balance files." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	/// <summary>Three simulated minutes: the evaluator builds the park, samples every row and stays internally consistent.</summary>
	[TestMethod]
	public void ShortJungleGateRunReportsEveryInvariant()
	{
		// A group after every timer unit (about a second) instead of the shipped 150 (about 2.5 minutes), so guests arrive from
		// the start and the earliest have used the ride, shop and toilet and left within three minutes.
		var report = M3Gate.Run( new M3GateOptions { Minutes = 3, ArrivalTimeBetween = 1 } );
		var expected = new[] { "build.entrance", "build.paths", "build.attraction", "build.shop", "build.toilet", "build.queue", "build.staff",
			"time.monotonic", "economy.income-and-expenses", "economy.ledger-consistent", "guests.flow", "queues.no-stuck-queue",
			"paths.no-unreachable-goal", "rides.scripts-run", "staff.work", "determinism.same-seed" };
		CollectionAssert.AreEquivalent( expected, report.Rows.Select( row => row.Id ).ToArray(), report.ToSummary() );
		Assert.AreEqual( 3 * 3600L, (long)report.TimeMapping["ticks"]! );
		// Stability invariants the evaluator must establish on any healthy run.
		foreach ( var id in new[] { "build.entrance", "build.attraction", "build.shop", "build.toilet", "time.monotonic", "economy.ledger-consistent", "rides.scripts-run" } )
			Assert.AreEqual( M3GateVerdict.Pass, report[id].Verdict, report.ToSummary() );
		// Paths are laid by the player-facing builder (ParkPathBuilder): 14 cells, each charged $20, every entrance reached.
		var paths = report["build.paths"];
		Assert.AreEqual( M3GateVerdict.Pass, paths.Verdict, report.ToSummary() );
		Assert.AreEqual( 14, (int)paths.Evidence["cellsBuilt"]! );
		Assert.AreEqual( 14L * 20, (long)paths.Evidence["charged"]! );
		Assert.AreEqual( 0, (int)paths.Evidence["strayPathCells"]! );
		// Every row passes (the queue row on the traced boarding bound, BOARD-plan and WALK-plan): exit 0.
		Assert.AreEqual( 0, report.ExitCode, report.ToSummary() );
		// The queue is laid through the queue tool's code, charged per cell, and walked: 6 cells for Belly Bounce's limit
		// trunc(130 × 5 / 30) = 21 (QUEUE-plan §4).
		var queue = report["build.queue"];
		Assert.AreEqual( M3GateVerdict.Pass, queue.Verdict, report.ToSummary() );
		Assert.AreEqual( 6, (int)queue.Evidence["cellsLaid"]! );
		Assert.AreEqual( 6L * 75, (long)queue.Evidence["charged"]! );
		Assert.AreEqual( 21, (int)queue.Evidence["maximumQueueLength"]! );
		Assert.IsTrue( (int)queue.Evidence["boardedFromQueueCells"]! > 0, report.ToSummary() );
		// No progress violation and no wait beyond W(p) with H = 55 (the loop WAIT divided by the ride's script speed); at
		// least one wait judged, so the row passes.
		var stuck = report["queues.no-stuck-queue"];
		Assert.AreEqual( M3GateVerdict.Pass, stuck.Verdict, report.ToSummary() );
		Assert.AreEqual( 55L, (long)stuck.Evidence["attraction"]!["waitBound"]!["latencyTurns"]! );
		Assert.AreEqual( 0L, (long)stuck.Evidence["attraction"]!["waitBound"]!["walkStalls"]! );
		Assert.AreEqual( M3GateVerdict.Pass, report["staff.work"].Verdict, report.ToSummary() );
		Assert.IsTrue( (long)report["guests.flow"].Evidence["admitted"]! > 0, report.ToSummary() );
		Assert.IsNotNull( JsonNode.Parse( report.ToJson() ) );
	}
}
