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

	/// <summary>The queue row's head bound follows the state-11/12 rules: walk 2N + 2 cells at 0.7 x walk speed, the gap-2 move-up wait, two interludes and one update.</summary>
	[TestMethod]
	public void HeadNotReadyBoundFollowsTheQueueRules()
	{
		// 25 cells: 52 cells / 0.7 cells/s = 74.29 s = 299.5 turns of 248 ms -> 300; + trunc(1.2 x 2) + 1 = 3; + 2 x 11; + 1.
		Assert.AreEqual( 326L, M3GateRun.HeadNotReadyBound( 25, 1.0f ).Turns );
		// One cell: 4 / 0.7 = 5.71 s = 23.04 turns -> 24; + 3 + 22 + 1.
		Assert.AreEqual( 50L, M3GateRun.HeadNotReadyBound( 1, 1.0f ).Turns );
		Assert.AreEqual( 50L, M3GateRun.HeadNotReadyBound( 0, 1.0f ).Turns );
		// A faster walk only shortens the walk term.
		Assert.AreEqual( 26L + 150, M3GateRun.HeadNotReadyBound( 25, 2.0f ).Turns );
		StringAssert.Contains( M3GateRun.HeadNotReadyBound( 25, 1.0f ).Derivation, "2 interludes x 11" );
	}
}

/// <summary>Short M3 gate run on original data (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
public class M3GateAssetTests
{
	[TestInitialize]
	public void Init()
	{
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

	/// <summary>Three simulated minutes: the evaluator builds the park, samples every row and stays internally consistent.</summary>
	[TestMethod]
	public void ShortJungleGateRunReportsEveryInvariant()
	{
		var report = M3Gate.Run( new M3GateOptions { Minutes = 3 } );
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
		// The unresolved queue row keeps the gate at exit 2: M3 is not accepted.
		Assert.AreEqual( 2, report.ExitCode, report.ToSummary() );
		// The queue is laid through the queue tool's code, charged per cell, and walked: 25 cells for the 100-guest limit.
		var queue = report["build.queue"];
		Assert.AreEqual( M3GateVerdict.Pass, queue.Verdict, report.ToSummary() );
		Assert.AreEqual( 25, (int)queue.Evidence["cellsLaid"]! );
		Assert.AreEqual( 25L * 75, (long)queue.Evidence["charged"]! );
		Assert.AreEqual( 100, (int)queue.Evidence["maximumQueueLength"]! );
		Assert.IsTrue( (int)queue.Evidence["boardedFromQueueCells"]! > 0, report.ToSummary() );
		// No derived progress violation; the call-to-boarding time and tau are untraced, so the row stays unresolved.
		Assert.AreEqual( M3GateVerdict.Unresolved, report["queues.no-stuck-queue"].Verdict, report.ToSummary() );
		Assert.AreEqual( M3GateVerdict.Pass, report["staff.work"].Verdict, report.ToSummary() );
		Assert.IsTrue( (long)report["guests.flow"].Evidence["admitted"]! > 0, report.ToSummary() );
		Assert.IsNotNull( JsonNode.Parse( report.ToJson() ) );
	}
}
