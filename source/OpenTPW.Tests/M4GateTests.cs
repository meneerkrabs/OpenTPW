using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>M4 gate report and row rules (asset-free; docs/M4-GATE.md).</summary>
[TestClass]
public class M4GateTests
{
	private static M4GateRow Row( string id, M4GateVerdict verdict ) => new( id, "test", id, verdict, new JsonObject { ["value"] = 1 } );

	[TestMethod]
	public void ExitCodeIsOneOnAnyFailureTwoOnUnresolvedAndZeroOnlyWhenEveryRowPasses()
	{
		var pass = Row( "a", M4GateVerdict.Pass );
		var unresolved = Row( "b", M4GateVerdict.Unresolved );
		var fail = Row( "c", M4GateVerdict.Fail );
		Assert.AreEqual( 1, new M4GateReport( new M4GateOptions(), new[] { pass, unresolved, fail }, 0 ).ExitCode );
		Assert.AreEqual( 1, new M4GateReport( new M4GateOptions(), new[] { pass, fail }, 0 ).ExitCode );
		Assert.AreEqual( 2, new M4GateReport( new M4GateOptions(), new[] { pass, unresolved }, 0 ).ExitCode );
		Assert.AreEqual( 0, new M4GateReport( new M4GateOptions(), new[] { pass }, 0 ).ExitCode );
		var report = new M4GateReport( new M4GateOptions(), new[] { pass, unresolved, fail }, 1.5 );
		var json = JsonNode.Parse( report.ToJson() )!;
		Assert.AreEqual( 3, json["rows"]!.AsArray().Count );
		Assert.AreEqual( "unresolved", (string)json["rows"]![1]!["verdict"]! );
		Assert.AreEqual( 1, (int)json["counts"]!["fail"]! );
		var summary = report.ToSummary().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
		Assert.AreEqual( 3 + 2, summary.Length );
		StringAssert.StartsWith( summary[3], "FAIL" );
	}

	/// <summary>Supported means offered by the build panel without a track; every other entry gets its reason.</summary>
	[TestMethod]
	public void ClassificationSeparatesTheBuildPanelFromFixedItemsToolsAddOnsAndTrackRides()
	{
		Assert.IsTrue( M4Gate.Classify( ObjectCategory.Ride, false, false, true, 0, 0 ).Supported );
		Assert.IsTrue( M4Gate.Classify( ObjectCategory.Feature, false, false, true, 3, -1 ).Supported );
		Assert.AreEqual( "fixed item", M4Gate.Classify( ObjectCategory.Feature, true, false, false, 4, -1 ).Class );
		Assert.AreEqual( "tool", M4Gate.Classify( ObjectCategory.Feature, false, true, false, 4, -1 ).Class );
		Assert.AreEqual( "add-on", M4Gate.Classify( ObjectCategory.Upgrade, false, false, false, 0, 1 ).Class );
		foreach ( var track in M4Gate.TrackTypes.Keys )
		{
			var (supported, kind, reason) = M4Gate.Classify( ObjectCategory.Ride, false, false, true, 0, track );
			Assert.IsFalse( supported );
			Assert.AreEqual( "track ride", kind );
			StringAssert.Contains( reason, $"WhichTrackType {track}" );
		}
		Assert.AreEqual( "not offered", M4Gate.Classify( ObjectCategory.Feature, false, false, false, 7, -1 ).Class );
	}

	[TestMethod]
	public void StepVerdictFailsOnAnyFailureAndIsUndecidedWithoutResults()
	{
		M4StepResult Result( M4GateVerdict verdict ) => new( "object", verdict, "detail", new JsonObject() );
		Assert.AreEqual( M4GateVerdict.Unresolved, M4Gate.StepVerdict( Array.Empty<M4StepResult>() ) );
		Assert.AreEqual( M4GateVerdict.Pass, M4Gate.StepVerdict( new[] { Result( M4GateVerdict.Pass ), Result( M4GateVerdict.Pass ) } ) );
		Assert.AreEqual( M4GateVerdict.Unresolved, M4Gate.StepVerdict( new[] { Result( M4GateVerdict.Pass ), Result( M4GateVerdict.Unresolved ) } ) );
		Assert.AreEqual( M4GateVerdict.Fail, M4Gate.StepVerdict( new[] { Result( M4GateVerdict.Unresolved ), Result( M4GateVerdict.Fail ) } ) );
	}

	private static readonly (Opcode Opcode, string Test, bool ThroughRuntimeEffects)[] Map =
	{
		(Opcode.ADD, "RideVMTests.A", false), (Opcode.WAIT, "RideVMTests.B", false), (Opcode.HOP, "GuestTests.C", false), (Opcode.HOP, "GuestTests.C", true)
	};

	/// <summary>vm.opcodes passes only when every corpus opcode has a handler and a mapped test; an unknown opcode is listed by number with its scripts.</summary>
	[TestMethod]
	public void OpcodeRowListsUnknownOpcodesAndFailsOnAnyUntestedOne()
	{
		var histogram = new Dictionary<ushort, int> { [(ushort)Opcode.ADD] = 5, [(ushort)Opcode.WAIT] = 2, [(ushort)Opcode.HOP] = 1 };
		var users = histogram.Keys.ToDictionary( key => key, key => new SortedSet<string> { $"script{key}.rse" } );
		var pass = M4Gate.OpcodeRow( histogram, users, 3, Map );
		Assert.AreEqual( M4GateVerdict.Pass, pass.Verdict, pass.Evidence.ToJsonString() );
		Assert.AreEqual( histogram.Count, (int)pass.Evidence["withUnitTest"]! );
		// Mutation: dropping the only WAIT test fails the row and names the opcode.
		var dropped = M4Gate.OpcodeRow( histogram, users, 3, Map.Where( entry => entry.Opcode != Opcode.WAIT ).ToArray() );
		Assert.AreEqual( M4GateVerdict.Fail, dropped.Verdict );
		StringAssert.Contains( (string)dropped.Evidence["untested"]!, "WAIT" );
		// An opcode beyond the name table is reported with its number, name and first script, and fails the row.
		const ushort unknown = 200;
		var withUnknown = new Dictionary<ushort, int>( histogram ) { [unknown] = 7 };
		var usersWithUnknown = new Dictionary<ushort, SortedSet<string>>( users ) { [unknown] = new SortedSet<string> { "odd.rse" } };
		var failing = M4Gate.OpcodeRow( withUnknown, usersWithUnknown, 4, Map );
		Assert.AreEqual( M4GateVerdict.Fail, failing.Verdict );
		Assert.AreEqual( 1, (int)failing.Evidence["unknownOpcodes"]! );
		var listed = (string)failing.Evidence["unknown"]!.AsArray()[0]!;
		StringAssert.StartsWith( listed, $"{unknown} {RideScriptAnalysis.GetOpcodeName( unknown )}" );
		StringAssert.Contains( listed, "odd.rse" );
		Assert.AreEqual( M4GateVerdict.Fail, M4Gate.OpcodeRow( new Dictionary<ushort, int>(), new Dictionary<ushort, SortedSet<string>>(), 0, Map ).Verdict );
	}

	/// <summary>A hooked opcode is resolved only when the game effects performed it and a test exercises it through them; a hook fault fails the row.</summary>
	[TestMethod]
	public void EffectHookRowResolvesOnlyPerformedAndTestedHooks()
	{
		var performed = new Dictionary<Opcode, M4Gate.HookObservation> { [Opcode.HOP] = new( 10, true, 0, 0 ) };
		Assert.AreEqual( M4GateVerdict.Pass, M4Gate.EffectHookRow( new[] { Opcode.HOP }, performed, Map ).Verdict );
		var withUntested = M4Gate.EffectHookRow( new[] { Opcode.HOP, Opcode.HUSH }, performed, Map );
		Assert.AreEqual( M4GateVerdict.Unresolved, withUntested.Verdict );
		Assert.AreEqual( 1, (int)withUntested.Evidence["unresolved"]! );
		var unimplemented = new Dictionary<Opcode, M4Gate.HookObservation> { [Opcode.HOP] = new( 10, false, 3, 0 ) };
		var recorded = M4Gate.EffectHookRow( new[] { Opcode.HOP }, unimplemented, Map );
		Assert.AreEqual( M4GateVerdict.Unresolved, recorded.Verdict );
		StringAssert.Contains( recorded.Evidence["opcodes"]!.AsArray()[0]!.ToString(), "recorded unimplemented 3" );
		Assert.AreEqual( M4GateVerdict.Unresolved, M4Gate.EffectHookRow( new[] { Opcode.HOP }, performed, Map.Where( entry => !entry.ThroughRuntimeEffects ).ToArray() ).Verdict );
		var faulted = new Dictionary<Opcode, M4Gate.HookObservation> { [Opcode.HOP] = new( 10, true, 0, 1 ) };
		Assert.AreEqual( M4GateVerdict.Fail, M4Gate.EffectHookRow( new[] { Opcode.HOP }, faulted, Map ).Verdict );
	}

	[TestMethod]
	public void CoasterCameraAndRatingRowsPassOnlyWhenEveryPartExists()
	{
		M4Gate.TrackRideProbe Probe( bool track ) => new( "coaster", 3, true, true, track, track, track, true, new JsonObject() );
		Assert.AreEqual( M4GateVerdict.Fail, M4Gate.CoasterRow( Array.Empty<M4Gate.TrackRideProbe>(), "none" ).Verdict );
		Assert.AreEqual( M4GateVerdict.Fail, M4Gate.CoasterRow( new[] { Probe( true ), Probe( false ) }, "none" ).Verdict );
		Assert.AreEqual( M4GateVerdict.Pass, M4Gate.CoasterRow( new[] { Probe( true ) }, "builder" ).Verdict );
		Assert.AreEqual( M4GateVerdict.Fail, M4Gate.CameraRow( new[] { ("ParkCameraMode", false) } ).Verdict );
		Assert.AreEqual( M4GateVerdict.Unresolved, M4Gate.CameraRow( new[] { ("ParkCameraMode", false), ("RideCameraMode", true) } ).Verdict );
		Assert.AreEqual( M4GateVerdict.Fail, M4Gate.RatingRow( 3, 3, new[] { "coaster" }, "data" ).Verdict );
		Assert.AreEqual( M4GateVerdict.Fail, M4Gate.RatingRow( 2, 3, Array.Empty<string>(), "data" ).Verdict );
		Assert.AreEqual( M4GateVerdict.Pass, M4Gate.RatingRow( 3, 3, Array.Empty<string>(), "data" ).Verdict );
	}

	/// <summary>Determinism compares every per-minute hash and their number; a probe that is off leaves the row unresolved.</summary>
	[TestMethod]
	public void DeterminismRowComparesEveryMinuteHash()
	{
		var first = new List<(string, ulong)> { ("a minute 1", 1UL), ("a minute 2", 2UL), ("a deleted", 3UL) };
		Assert.AreEqual( M4GateVerdict.Pass, M4Gate.DeterminismRow( first, first.ToList() ).Verdict );
		var diverged = first.ToList();
		diverged[1] = ("a minute 2", 9UL);
		var row = M4Gate.DeterminismRow( first, diverged );
		Assert.AreEqual( M4GateVerdict.Fail, row.Verdict );
		Assert.AreEqual( "a minute 2", (string)row.Evidence["firstDivergence"]! );
		Assert.AreEqual( M4GateVerdict.Fail, M4Gate.DeterminismRow( first, first.Take( 2 ).ToList() ).Verdict );
		Assert.AreEqual( M4GateVerdict.Unresolved, M4Gate.DeterminismRow( first, null ).Verdict );
	}
}

/// <summary>Short M4 gate runs on original data (inconclusive without OPENTPW_GAME_PATH), including the gate's mutation checks.</summary>
[TestClass]
[DoNotParallelize]
public class M4GateAssetTests
{
	private const int BellyBounce = 1100, DrinksShop = 1203, SmallToilet = 1402, ChacAtak = 1185;
	private const double Window = 2;

	[TestInitialize]
	public void Init()
	{
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original catalogs, levels and scripts." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	private static M4GateReport Run( IEnumerable<int> objects, M4GateHooks? hooks = null, bool determinism = false ) => M4Gate.Run( new M4GateOptions
	{
		Themes = new[] { "jungle" },
		Objects = objects.ToHashSet(),
		UseWindowMinutes = Window,
		DeterminismProbe = determinism,
		Hooks = hooks
	} );

	private static JsonObject Result( M4GateReport report, string step, int infoId ) =>
		report[$"attractions.jungle.{step}"].Evidence["results"]!.AsArray().Select( node => node!.AsObject() ).Single( item => ((string)item["object"]!).StartsWith( $"{infoId} " ) );

	/// <summary>
	/// A short jungle run over a ride, a shop, a toilet, a scenery feature and a coaster: every row is reported, each selected
	/// object has one result per lifecycle step, charges and scrap credits follow the data, and the run is deterministic.
	/// </summary>
	[TestMethod]
	public void ShortJungleRunReportsEveryRowAndKeepsItsInvariants()
	{
		var scenery = ObjectCatalog.Load( "jungle" ).Buildable.Where( entry => entry.Category == ObjectCategory.Feature && !entry.IsChoosable ).Min( entry => entry.InfoId );
		var selected = new[] { BellyBounce, DrinksShop, SmallToilet, scenery, ChacAtak };
		var report = Run( selected, determinism: true );
		var expected = new[] { "attractions.inventory" }.Concat( M4Gate.Steps.Select( step => $"attractions.jungle.{step}" ) )
			.Concat( new[] { "vm.opcodes", "vm.effect-hooks", "coasters.construction", "rides.camera", "rides.rating", "determinism.same-seed" } ).ToArray();
		CollectionAssert.AreEqual( expected, report.Rows.Select( row => row.Id ).ToArray(), report.ToSummary() );
		Assert.AreEqual( M4GateVerdict.Pass, report["attractions.inventory"].Verdict, report.ToSummary() );
		Assert.AreEqual( M4GateVerdict.Pass, report["determinism.same-seed"].Verdict, report.ToSummary() );
		// The coaster is a track ride: classified unsupported, probed by coasters.construction, which fails without a track builder.
		var supported = selected.Where( id => id != ChacAtak ).ToArray();
		foreach ( var step in M4Gate.Steps )
			Assert.AreEqual( supported.Length, (int)report[$"attractions.jungle.{step}"].Evidence["objects"]!, step );
		Assert.AreEqual( M4GateVerdict.Fail, report["coasters.construction"].Verdict );
		Assert.AreEqual( 1, (int)report["coasters.construction"].Evidence["trackRides"]! );
		foreach ( var id in supported )
		{
			var build = Result( report, "build", id );
			Assert.AreEqual( "pass", (string)build["verdict"]!, build.ToJsonString() );
			Assert.AreEqual( (long)build["expectedCash"]!, (long)build["charged"]! );
			var delete = Result( report, "delete", id );
			Assert.AreEqual( "pass", (string)delete["verdict"]!, delete.ToJsonString() );
			Assert.AreEqual( (long)delete["expectedCredit"]!, (long)delete["credit"]! );
		}
		// The corpus scan finds every opcode a handler (no unknown opcodes); vm.opcodes counts each corpus opcode once.
		var opcodes = report["vm.opcodes"].Evidence;
		Assert.AreEqual( 0, (int)opcodes["unknownOpcodes"]! );
		Assert.AreEqual( (int)opcodes["corpusOpcodes"]!, (int)opcodes["withUnitTest"]! + (int)opcodes["withoutUnitTest"]! );
		Assert.AreEqual( report.HasFailures ? 1 : report.HasUnresolved ? 2 : 0, report.ExitCode );
		Assert.IsNotNull( JsonNode.Parse( report.ToJson() ) );
	}

	// ---- mutation checks: each injected fault flips its row to FAIL (the baseline object passes the same step).

	[TestMethod]
	public void MutationChargingTwiceFailsTheBuildRow()
	{
		var report = Run( new[] { DrinksShop }, new M4GateHooks
		{
			Purchase = ( economy, entry ) =>
			{
				var result = economy.TryBuild( entry.InfoId, out var built );
				economy.TryBuild( entry.InfoId, out _ );
				return (result, built);
			}
		} );
		Assert.AreEqual( M4GateVerdict.Fail, report["attractions.jungle.build"].Verdict, report.ToSummary() );
		StringAssert.Contains( (string)Result( report, "build", DrinksShop )["detail"]!, "charged" );
	}

	[TestMethod]
	public void MutationStoppingTheScriptFailsTheRunRow()
	{
		var report = Run( new[] { DrinksShop }, new M4GateHooks { RunTick = ( context, tick ) => { if ( tick == 100 ) context.Runtime.Stop(); } } );
		Assert.AreEqual( M4GateVerdict.Fail, report["attractions.jungle.run"].Verdict, report.ToSummary() );
		StringAssert.Contains( (string)Result( report, "run", DrinksShop )["detail"]!, "halted" );
	}

	[TestMethod]
	public void MutationReopeningAfterCloseFailsTheStopRow()
	{
		var report = Run( new[] { BellyBounce }, new M4GateHooks { StopTick = ( context, _ ) => context.Runtime.Open() } );
		Assert.AreEqual( M4GateVerdict.Fail, report["attractions.jungle.stop"].Verdict, report.ToSummary() );
	}

	[TestMethod]
	public void MutationRemovingWithoutScrapCreditFailsTheDeleteRow()
	{
		var report = Run( new[] { DrinksShop }, new M4GateHooks
		{
			Delete = context =>
			{
				context.Runtime.Stop();
				context.Guests.Unregister( context.Runtime.Visitors );
				context.Park.Economy.Remove( context.EconomyInstance );
				return null;
			}
		} );
		Assert.AreEqual( M4GateVerdict.Fail, report["attractions.jungle.delete"].Verdict, report.ToSummary() );
		StringAssert.Contains( (string)Result( report, "delete", DrinksShop )["detail"]!, "credited nothing" );
	}

	[TestMethod]
	public void MutationDivergingTheSecondRunFailsDeterminism()
	{
		var report = Run( new[] { DrinksShop }, new M4GateHooks
		{
			BeforeHash = ( context, run ) => { if ( run == 1 ) context.Runtime.Script!.Variables[^1] += 1; }
		}, determinism: true );
		Assert.AreEqual( M4GateVerdict.Fail, report["determinism.same-seed"].Verdict, report.ToSummary() );
	}

	[TestMethod]
	public void MutationDroppingAnOpcodeTestFailsTheOpcodeRow()
	{
		var baseline = M4Gate.Run( new M4GateOptions { Themes = new[] { "jungle" }, Objects = new HashSet<int>(), DeterminismProbe = false } );
		// COPY: a corpus opcode (RSE-VM.md) with exactly one map entry.
		const Opcode tested = Opcode.COPY;
		Assert.AreEqual( 1, RideOpcodeTestMap.Entries.Count( entry => entry.Opcode == tested ) );
		var mutated = M4Gate.Run( new M4GateOptions
		{
			Themes = new[] { "jungle" },
			Objects = new HashSet<int>(),
			DeterminismProbe = false,
			OpcodeTestMap = RideOpcodeTestMap.Entries.Where( entry => entry.Opcode != tested ).ToArray()
		} );
		Assert.AreEqual( (int)baseline["vm.opcodes"].Evidence["withoutUnitTest"]! + 1, (int)mutated["vm.opcodes"].Evidence["withoutUnitTest"]! );
		StringAssert.Contains( (string)mutated["vm.opcodes"].Evidence["untested"]!, tested.ToString() );
		Assert.AreEqual( M4GateVerdict.Fail, mutated["vm.opcodes"].Verdict );
	}
}
