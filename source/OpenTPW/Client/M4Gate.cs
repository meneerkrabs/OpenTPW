using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>Outcome of one M4 gate row (docs/M4-GATE.md).</summary>
public enum M4GateVerdict
{
	Pass,
	Fail,
	/// <summary>Measured, but no derived rule decides it (or it was not reached).</summary>
	Unresolved
}

/// <summary>One row of the M4 gate report with its evidence.</summary>
public sealed record M4GateRow( string Id, string Area, string Title, M4GateVerdict Verdict, JsonObject Evidence );

public sealed record M4GateOptions
{
	/// <summary>Themes whose catalogs are measured (default: all four, <see cref="ObjectCatalog.Themes"/>).</summary>
	public IReadOnlyList<string> Themes { get; init; } = ObjectCatalog.Themes;
	public WorldSeed Seed { get; init; } = WorldSeed.Default;
	/// <summary>The use window W in simulated minutes (GATEM4-001; <see cref="M4Gate.DefaultUseWindowMinutes"/>).</summary>
	public double UseWindowMinutes { get; init; } = M4Gate.DefaultUseWindowMinutes;
	/// <summary>Runs the whole scenario a second time with the same seed and compares the per-minute state hashes.</summary>
	public bool DeterminismProbe { get; init; } = true;
	/// <summary>Restricts the lifecycle to these Info.Ids (tests); null measures every supported object.</summary>
	public IReadOnlySet<int>? Objects { get; init; }
	/// <summary>Fault injection for the gate's own mutation tests; null in real runs.</summary>
	internal M4GateHooks? Hooks { get; init; }
	/// <summary>The opcode test map the vm rows read (tests replace it to check the rows can fail).</summary>
	internal IReadOnlyList<(Opcode Opcode, string Test, bool ThroughRuntimeEffects)> OpcodeTestMap { get; init; } = RideOpcodeTestMap.Entries;
}

/// <summary>Points where the gate's mutation tests tamper with one object's lifecycle (docs/M4-GATE.md, "Mutation checks").</summary>
internal sealed class M4GateHooks
{
	/// <summary>Replaces <see cref="ParkEconomy.TryBuild"/> in the build step.</summary>
	public Func<ParkEconomy, ObjectCatalogEntry, (ParkEconomy.PurchaseResult Result, ParkObjectState? Built)>? Purchase { get; init; }
	public Action<M4GateObjectContext>? AfterBuild { get; init; }
	public Action<M4GateObjectContext, long>? RunTick { get; init; }
	public Action<M4GateObjectContext, long>? StopTick { get; init; }
	/// <summary>Replaces the remove tool's removal; returns the credited scrap value.</summary>
	public Func<M4GateObjectContext, long?>? Delete { get; init; }
	/// <summary>Called before each state hash (minutes and step ends) with the run index (0, then 1 for the determinism probe).</summary>
	public Action<M4GateObjectContext, int>? BeforeHash { get; init; }
}

/// <summary>What a hook sees of the object under test.</summary>
internal sealed record M4GateObjectContext( ObjectCatalogEntry Entry, OriginalObjectRuntime Runtime, ParkEconomyRuntime Park, GuestSimulation Guests, int EconomyInstance );

public sealed record M4GateReport( M4GateOptions Options, IReadOnlyList<M4GateRow> Rows, double WallSeconds )
{
	public bool HasFailures => Rows.Any( row => row.Verdict == M4GateVerdict.Fail );

	public bool HasUnresolved => Rows.Any( row => row.Verdict == M4GateVerdict.Unresolved );

	/// <summary>Process exit code: 1 when any row fails, 2 when none fails but one is unresolved, 0 only when every row passes (M4 subset acceptance only at 0).</summary>
	public int ExitCode => HasFailures ? 1 : HasUnresolved ? 2 : 0;

	public M4GateRow this[string id] => Rows.First( row => row.Id == id );

	public JsonObject ToJsonObject()
	{
		var rows = new JsonArray();
		foreach ( var row in Rows )
		{
			rows.Add( new JsonObject
			{
				["id"] = row.Id,
				["area"] = row.Area,
				["title"] = row.Title,
				["verdict"] = row.Verdict.ToString().ToLowerInvariant(),
				["evidence"] = row.Evidence.DeepClone()
			} );
		}
		return new JsonObject
		{
			["gate"] = "M4",
			["themes"] = new JsonArray( Options.Themes.Select( theme => (JsonNode)JsonValue.Create( theme )! ).ToArray() ),
			["seed"] = Options.Seed.Value,
			["useWindowMinutes"] = Options.UseWindowMinutes,
			["wallSeconds"] = Math.Round( WallSeconds, 3 ),
			["counts"] = new JsonObject
			{
				["pass"] = Rows.Count( row => row.Verdict == M4GateVerdict.Pass ),
				["fail"] = Rows.Count( row => row.Verdict == M4GateVerdict.Fail ),
				["unresolved"] = Rows.Count( row => row.Verdict == M4GateVerdict.Unresolved )
			},
			["rows"] = rows
		};
	}

	private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
	private static readonly JsonSerializerOptions Compacted = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	public string ToJson() => ToJsonObject().ToJsonString( Indented ).Replace( "\r\n", "\n" );

	/// <summary>One line per row: verdict, id and the row's scalar evidence (per-object lists are in the JSON report).</summary>
	public string ToSummary()
	{
		var text = new StringBuilder();
		text.Append( FormattableString.Invariant( $"M4 gate: {string.Join( ", ", Options.Themes )}, seed {Options.Seed.Value}, use window {Options.UseWindowMinutes} simulated minutes, in {WallSeconds:0.0} s wall time.\n" ) );
		foreach ( var row in Rows )
		{
			var verdict = row.Verdict.ToString().ToUpperInvariant();
			var evidence = string.Join( ", ", row.Evidence.Where( pair => pair.Value is JsonValue ).Select( pair => $"{pair.Key}={pair.Value!.ToJsonString( Compacted ).Trim( '"' )}" ) );
			text.Append( $"{verdict,-10} {row.Id,-26} [{row.Area}] {row.Title}: {evidence}\n" );
		}
		text.Append( $"Totals: {Rows.Count( row => row.Verdict == M4GateVerdict.Pass )} pass, {Rows.Count( row => row.Verdict == M4GateVerdict.Fail )} fail, {Rows.Count( row => row.Verdict == M4GateVerdict.Unresolved )} unresolved.\n" );
		return text.ToString();
	}
}

/// <summary>A lifecycle step of one object: its verdict and evidence.</summary>
public sealed record M4StepResult( string Object, M4GateVerdict Verdict, string Detail, JsonObject Evidence )
{
	public JsonObject ToJson()
	{
		var item = new JsonObject { ["object"] = Object, ["verdict"] = Verdict.ToString().ToLowerInvariant(), ["detail"] = Detail };
		foreach ( var (key, value) in Evidence )
			item[key] = value?.DeepClone();
		return item;
	}
}

/// <summary>
/// Milestone M4 evaluator, subset acceptance (docs/COMPLETION-PLAN.md §M4, docs/M4-GATE.md): classifies every catalog
/// object of the four themes, drives each supported one through load, build, run, stop and delete in its own headless
/// park (player-facing build code, charged), inventories the opcodes of the original RSE corpus against the unit tests
/// that exercise them, lists the effect hooks, and reports roller-coaster construction, the ride camera and ride
/// assessment. It measures; it changes no gameplay. CPU only, no window, GPU or audio.
/// </summary>
public static class M4Gate
{
	/// <summary>Default use window W (GATEM4-001, docs/M4-GATE.md).</summary>
	public const double DefaultUseWindowMinutes = 5;

	public const string LifecycleArea = "attractions";

	/// <summary>The lifecycle steps, in order; each gets one row per theme (<c>attractions.&lt;theme&gt;.&lt;step&gt;</c>).</summary>
	public static readonly IReadOnlyList<string> Steps = new[] { "load", "build", "run", "stop", "delete" };

	/// <summary>Bumper.WhichTrackType values whose ride needs a built track (1 karts, 2 water rides, 3 coasters).</summary>
	public static readonly IReadOnlyDictionary<int, string> TrackTypes = new Dictionary<int, string> { [1] = "kart track", [2] = "water channel", [3] = "coaster track" };

	public static M4GateReport Run( M4GateOptions options )
	{
		ArgumentNullException.ThrowIfNull( options );
		if ( !double.IsFinite( options.UseWindowMinutes ) || options.UseWindowMinutes <= 0 )
			throw new ArgumentOutOfRangeException( nameof( options ), "The use window must be positive." );
		var unknownThemes = options.Themes.Where( theme => !ObjectCatalog.Themes.Contains( theme ) ).ToList();
		if ( options.Themes.Count == 0 || unknownThemes.Count > 0 )
			throw new ArgumentException( $"Themes must be among {string.Join( ", ", ObjectCatalog.Themes )}." );
		var watch = Stopwatch.StartNew();
		var rows = new List<M4GateRow>();
		try
		{
			var first = M4GateRun.Execute( options, 0 );
			rows.AddRange( first.Rows );
			var second = options.DeterminismProbe ? M4GateRun.Execute( options, 1 ) : null;
			rows.Add( DeterminismRow( first.MinuteHashes, second?.MinuteHashes ) );
		}
		catch ( Exception exception ) when ( exception is not OutOfMemoryException )
		{
			// A crash outside the per-object guards (catalog, level or corpus loading) is a failing row, so the report is still written.
			rows.Add( new M4GateRow( "gate.run", "gate", "The evaluator ran to the end", M4GateVerdict.Fail,
				new JsonObject { ["exception"] = $"{exception.GetType().Name}: {exception.Message}", ["rowsBefore"] = rows.Count } ) );
		}
		return new M4GateReport( options, rows, watch.Elapsed.TotalSeconds );
	}

	/// <summary>
	/// <c>--m4-gate [--theme jungle[,hallow…]] [--seed N] [--window MINUTES] [--report path.json] [--no-determinism]</c>.
	/// Prints the summary; returns <see cref="M4GateReport.ExitCode"/>.
	/// </summary>
	public static int RunCommand( string[] args )
	{
		var options = new M4GateOptions();
		if ( Option( args, "--theme" ) is { } themes )
			options = options with { Themes = themes.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) };
		if ( Option( args, "--seed" ) is { } seedText )
			options = options with { Seed = new WorldSeed( ulong.TryParse( seedText, out var seed ) ? seed : throw new ArgumentException( "--seed requires an unsigned integer." ) ) };
		if ( Option( args, "--window" ) is { } windowText )
		{
			if ( !double.TryParse( windowText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var window ) || !double.IsFinite( window ) || window <= 0 )
				throw new ArgumentException( "--window requires a positive number of minutes." );
			options = options with { UseWindowMinutes = window };
		}
		if ( args.Contains( "--no-determinism" ) )
			options = options with { DeterminismProbe = false };
		GameAudio.Screams.Reset();
		var report = Run( options );
		Console.Write( report.ToSummary() );
		Console.Write( GameAudio.Screams.Summary() );
		if ( Option( args, "--report" ) is { } path )
		{
			File.WriteAllText( path, report.ToJson() + "\n" );
			Console.WriteLine( $"Report: {path}" );
		}
		return report.ExitCode;
	}

	private static string? Option( string[] args, string name )
	{
		var index = Array.IndexOf( args, name );
		if ( index < 0 )
			return null;
		if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--" ) )
			throw new ArgumentException( $"{name} requires a value." );
		return args[index + 1];
	}

	// ---------------------------------------------------------------- row rules (pure, unit-tested)

	/// <summary>
	/// Classifies a catalog entry. Supported: the build panel offers it (<see cref="ObjectCatalogEntry.IsBuildable"/>,
	/// RIDES-017) and it needs no built track. Everything else is unsupported for a stated reason.
	/// </summary>
	public static (bool Supported, string Class, string Reason) Classify( ObjectCategory category, bool isFixedItem, bool isTool, bool isBuildable, int whichUiType, int trackType )
	{
		if ( isFixedItem )
			return (false, "fixed item", "placed by the level, not built by the player (Info.DontApplyOffset)");
		if ( isTool )
			return (false, "tool", "no occupied cells (Mystery ride, Buy Land, Clear Land)");
		if ( category == ObjectCategory.Upgrade )
			return (false, "add-on", "attaches to a placed ride (AddOn.UpgradesId); the build panel does not offer add-ons (RIDES-017) and track add-ons need a track");
		if ( TrackTypes.TryGetValue( trackType, out var track ) )
			return (false, "track ride", $"needs a built {track} (Bumper.WhichTrackType {trackType}); see coasters.construction");
		if ( !isBuildable )
			return (false, "not offered", $"Info.WhichUIType {whichUiType} is not a build-panel type (RIDES-017)");
		return (true, category.ToString().ToLowerInvariant(), "offered by the build panel; no track");
	}

	/// <summary>A lifecycle row: FAIL when any object fails the step, else UNRESOLVED when any is undecided or nothing was measured, else PASS.</summary>
	public static M4GateVerdict StepVerdict( IReadOnlyCollection<M4StepResult> results ) =>
		results.Any( result => result.Verdict == M4GateVerdict.Fail ) ? M4GateVerdict.Fail
		: results.Count == 0 || results.Any( result => result.Verdict == M4GateVerdict.Unresolved ) ? M4GateVerdict.Unresolved
		: M4GateVerdict.Pass;

	/// <summary>
	/// vm.opcodes: every opcode the corpus uses has a VM handler and a unit test in the opcode test map; opcodes without a
	/// handler are listed by number and name with the scripts that use them. FAIL on any untested or unknown opcode.
	/// </summary>
	public static M4GateRow OpcodeRow( IReadOnlyDictionary<ushort, int> corpusHistogram, IReadOnlyDictionary<ushort, SortedSet<string>> corpusUsers, int corpusScripts,
		IReadOnlyList<(Opcode Opcode, string Test, bool ThroughRuntimeEffects)> testMap )
	{
		var tested = testMap.GroupBy( entry => entry.Opcode ).ToDictionary( group => group.Key, group => group.Select( entry => entry.Test ).Distinct().ToList() );
		var unknown = new JsonArray();
		var untested = new List<string>();
		var table = new JsonArray();
		foreach ( var (raw, uses) in corpusHistogram.OrderBy( pair => pair.Key ) )
		{
			var name = RideScriptAnalysis.GetOpcodeName( raw );
			var status = RideScriptAnalysis.IsKnownOpcode( raw ) ? RideOpcodes.GetStatus( (Opcode)raw ) : RideOpcodeStatus.Unknown;
			var tests = RideScriptAnalysis.IsKnownOpcode( raw ) && tested.TryGetValue( (Opcode)raw, out var list ) ? list : new List<string>();
			if ( status == RideOpcodeStatus.Unknown )
			{
				var users = corpusUsers.TryGetValue( raw, out var set ) ? set : new SortedSet<string>();
				unknown.Add( $"{raw} {name}: {uses} uses in {users.Count} scripts (first {users.FirstOrDefault() ?? "none"})" );
			}
			if ( tests.Count == 0 )
				untested.Add( $"{name} ({uses})" );
			table.Add( $"{raw} {name} {status.ToString().ToLowerInvariant()} uses {uses} tests {(tests.Count == 0 ? "none" : string.Join( " ", tests ))}" );
		}
		var problems = new List<string>();
		if ( corpusScripts == 0 || corpusHistogram.Count == 0 )
			problems.Add( "no corpus scripts found" );
		if ( unknown.Count > 0 )
			problems.Add( $"{unknown.Count} opcode(s) without a VM handler" );
		if ( untested.Count > 0 )
			problems.Add( $"{untested.Count} opcode(s) without a unit test" );
		var evidence = new JsonObject
		{
			["corpusScripts"] = corpusScripts,
			["corpusOpcodes"] = corpusHistogram.Count,
			["corpusInstructions"] = corpusHistogram.Values.Sum(),
			["handled"] = corpusHistogram.Keys.Count( raw => RideScriptAnalysis.IsKnownOpcode( raw ) && RideOpcodes.GetStatus( (Opcode)raw ) != RideOpcodeStatus.Unknown ),
			["unknownOpcodes"] = unknown.Count,
			["withUnitTest"] = corpusHistogram.Count - untested.Count,
			["withoutUnitTest"] = untested.Count,
			["untested"] = string.Join( ", ", untested ),
			["unknown"] = unknown,
			["rule"] = "a unit test is an asset-free test named in RideOpcodeTestMap; RideOpcodeTestMapTests runs each named test under a RideOpcodeRecorder and fails unless it executes the opcode",
			["opcodes"] = table,
			["problems"] = problems.Count == 0 ? "none" : string.Join( "; ", problems )
		};
		return new M4GateRow( "vm.opcodes", "vm", "Every corpus opcode has a handler and a unit test; unknown opcodes are listed",
			problems.Count == 0 ? M4GateVerdict.Pass : M4GateVerdict.Fail, evidence );
	}

	/// <summary>What the scenario observed of one hooked opcode.</summary>
	public sealed record HookObservation( long Executions, bool PerformedByRuntime, long UnimplementedCalls, long Faults );

	/// <summary>
	/// vm.effect-hooks: each hooked opcode the corpus uses is resolved only when OpenTPW's game effects performed it in the
	/// scenario without recording it as unimplemented and a unit test exercises it through those effects; otherwise it is
	/// unresolved with the reason. FAIL when a scenario script faulted on a hooked opcode.
	/// </summary>
	public static M4GateRow EffectHookRow( IEnumerable<Opcode> hookedCorpusOpcodes, IReadOnlyDictionary<Opcode, HookObservation> observed,
		IReadOnlyList<(Opcode Opcode, string Test, bool ThroughRuntimeEffects)> testMap )
	{
		var effectTested = testMap.Where( entry => entry.ThroughRuntimeEffects ).Select( entry => entry.Opcode ).ToHashSet();
		var rows = new JsonArray();
		var resolved = new List<string>();
		var unresolved = new List<string>();
		var faulted = new List<string>();
		foreach ( var opcode in hookedCorpusOpcodes.Distinct().OrderBy( opcode => opcode ) )
		{
			var seen = observed.GetValueOrDefault( opcode ) ?? new HookObservation( 0, false, 0, 0 );
			var reasons = new List<string>();
			if ( seen.Executions == 0 )
				reasons.Add( "not reached in the scenario" );
			else if ( !seen.PerformedByRuntime )
				reasons.Add( $"recorded unimplemented {seen.UnimplementedCalls} time(s)" );
			if ( !effectTested.Contains( opcode ) )
				reasons.Add( "no unit test through the game effects" );
			if ( seen.Faults > 0 )
				faulted.Add( $"{opcode} ({seen.Faults})" );
			(reasons.Count == 0 ? resolved : unresolved).Add( opcode.ToString() );
			rows.Add( $"{opcode}: {(reasons.Count == 0 ? "resolved" : "unresolved: " + string.Join( "; ", reasons ))} (executed {seen.Executions}, unimplemented {seen.UnimplementedCalls})" );
		}
		var evidence = new JsonObject
		{
			["hookedOpcodes"] = resolved.Count + unresolved.Count,
			["resolved"] = resolved.Count,
			["unresolved"] = unresolved.Count,
			["resolvedOpcodes"] = string.Join( " ", resolved ),
			["faultedOnHook"] = faulted.Count == 0 ? "none" : string.Join( ", ", faulted ),
			["rule"] = "resolved = performed by OriginalObjectEffects/VisitorRideScriptEffects in the scenario, never recorded unimplemented, and a RideOpcodeTestMap entry through the game effects",
			["opcodes"] = rows
		};
		var verdict = faulted.Count > 0 ? M4GateVerdict.Fail : unresolved.Count > 0 || resolved.Count == 0 ? M4GateVerdict.Unresolved : M4GateVerdict.Pass;
		return new M4GateRow( "vm.effect-hooks", "vm", "Hooked opcodes whose game systems are incomplete stay unresolved", verdict, evidence );
	}

	/// <summary>The track-ride facts coasters.construction decides on, per catalog object.</summary>
	public sealed record TrackRideProbe( string Object, int TrackType, bool StationBuilt, bool StationScriptRuns, bool TrackBuilt, bool CarRan, bool Stopped, bool Deleted, JsonObject Evidence );

	/// <summary>coasters.construction: PASS only when every track ride gets a track, runs a car, stops and is deleted; FAIL otherwise (nothing measured: FAIL).</summary>
	public static M4GateRow CoasterRow( IReadOnlyCollection<TrackRideProbe> probes, string trackBuilder )
	{
		var failing = probes.Where( probe => !(probe.StationBuilt && probe.TrackBuilt && probe.CarRan && probe.Stopped && probe.Deleted) ).ToList();
		var byType = new JsonObject();
		foreach ( var group in probes.GroupBy( probe => probe.TrackType ).OrderBy( group => group.Key ) )
		{
			byType[$"{group.Key} {TrackTypes.GetValueOrDefault( group.Key, "track" )}"] = new JsonObject
			{
				["objects"] = group.Count(),
				["stationBuilt"] = group.Count( probe => probe.StationBuilt ),
				["stationScriptRuns"] = group.Count( probe => probe.StationScriptRuns ),
				["trackBuilt"] = group.Count( probe => probe.TrackBuilt ),
				["carRan"] = group.Count( probe => probe.CarRan ),
				["stopped"] = group.Count( probe => probe.Stopped ),
				["deleted"] = group.Count( probe => probe.Deleted )
			};
		}
		var objects = new JsonArray();
		foreach ( var probe in probes )
		{
			var item = probe.Evidence.DeepClone().AsObject();
			item["object"] = probe.Object;
			objects.Add( item );
		}
		var evidence = new JsonObject
		{
			["trackRides"] = probes.Count,
			["withTrack"] = probes.Count( probe => probe.TrackBuilt ),
			["failing"] = failing.Count,
			["trackBuilder"] = trackBuilder,
			["byTrackType"] = byType,
			["objects"] = objects
		};
		return new M4GateRow( "coasters.construction", "coasters", "Each track ride (coasters, karts, water rides): build a track, run a car, stop, delete",
			probes.Count > 0 && failing.Count == 0 ? M4GateVerdict.Pass : M4GateVerdict.Fail, evidence );
	}

	/// <summary>rides.camera: FAIL when no camera mode follows a ride; UNRESOLVED when one exists (a GPU view the headless gate cannot exercise).</summary>
	public static M4GateRow CameraRow( IReadOnlyCollection<(string Mode, bool FollowsARide)> modes )
	{
		var riding = modes.Where( mode => mode.FollowsARide ).Select( mode => mode.Mode ).ToList();
		var evidence = new JsonObject
		{
			["cameraModes"] = string.Join( ", ", modes.Select( mode => mode.Mode ) ),
			["rideCameraModes"] = riding.Count == 0 ? "none" : string.Join( ", ", riding ),
			["rule"] = "a ride camera is a CameraMode whose constructor or properties take a placed ride (OriginalObject, OriginalObjectRuntime, RideVisitorBridge, Ride)"
		};
		return new M4GateRow( "rides.camera", "rides", "Ride camera (view from a ride)", riding.Count == 0 ? M4GateVerdict.Fail : M4GateVerdict.Unresolved, evidence );
	}

	/// <summary>rides.rating: FAIL when a ride has no assessment (track rides have none without a track); PASS when every ride has one.</summary>
	public static M4GateRow RatingRow( int fixedRidesWithDataRating, int fixedRides, IReadOnlyCollection<string> ridesWithoutAssessment, string assessment )
	{
		var evidence = new JsonObject
		{
			["fixedRides"] = fixedRides,
			["fixedRidesWithDataExcitement"] = fixedRidesWithDataRating,
			["ridesWithoutAssessment"] = ridesWithoutAssessment.Count,
			["withoutAssessment"] = string.Join( ", ", ridesWithoutAssessment ),
			["assessment"] = assessment
		};
		var pass = ridesWithoutAssessment.Count == 0 && fixedRidesWithDataRating == fixedRides && fixedRides > 0;
		return new M4GateRow( "rides.rating", "rides", "Ride assessment (excitement for every ride; computed for track rides)", pass ? M4GateVerdict.Pass : M4GateVerdict.Fail, evidence );
	}

	/// <summary>determinism.same-seed: both runs give the same list of WorldStateHash values (every simulated minute and each lifecycle step's end; same length, same values); UNRESOLVED when the probe is off.</summary>
	public static M4GateRow DeterminismRow( IReadOnlyList<(string Key, ulong Hash)> first, IReadOnlyList<(string Key, ulong Hash)>? second )
	{
		var evidence = new JsonObject
		{
			["hashesRun1"] = first.Count,
			["finalRun1"] = first.Count == 0 ? "none" : $"{first[^1].Hash:X16}",
			["hash"] = "WorldStateHash of every object's park each simulated minute and at the end of build, run, stop and delete"
		};
		if ( second == null )
		{
			evidence["skipped"] = true;
			return new M4GateRow( "determinism.same-seed", "determinism", "Same seed gives the same per-minute state hashes", M4GateVerdict.Unresolved, evidence );
		}
		evidence["hashesRun2"] = second.Count;
		evidence["finalRun2"] = second.Count == 0 ? "none" : $"{second[^1].Hash:X16}";
		var divergence = Enumerable.Range( 0, Math.Min( first.Count, second.Count ) ).FirstOrDefault( index => first[index] != second[index], -1 );
		evidence["firstDivergence"] = divergence >= 0 ? first[divergence].Key : first.Count != second.Count ? "hash count differs" : "none";
		var same = divergence < 0 && first.Count == second.Count && first.Count > 0;
		return new M4GateRow( "determinism.same-seed", "determinism", "Same seed gives the same per-minute state hashes", same ? M4GateVerdict.Pass : M4GateVerdict.Fail, evidence );
	}
}
