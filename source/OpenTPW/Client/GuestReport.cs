using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>The scale and scope of a <see cref="GuestReport"/> run.</summary>
public sealed record GuestReportOptions
{
	/// <summary>The fixed seeds of every scenario (8 by default: world seeds 1 to 8).</summary>
	public IReadOnlyList<ulong> Seeds { get; init; } = Enumerable.Range( 1, 8 ).Select( index => (ulong)index ).ToArray();
	/// <summary>In-game days run before the window opens (a day is about 23 park turns, 343 fixed ticks on average).</summary>
	public int WarmupDays { get; init; } = 2;
	/// <summary>In-game days measured.</summary>
	public int MeasuredDays { get; init; } = 10;
	/// <summary>Theme levels of the original game to run through the level gate's park builder, besides the synthetic scenario (needs the game's data).</summary>
	public IReadOnlyList<string> Levels { get; init; } = Array.Empty<string>();
	/// <summary>Seeds of each level park (the first of <see cref="Seeds"/>; all 8 by default).</summary>
	public int LevelSeeds { get; init; } = 8;
	/// <summary>Arrival.TimeBetweenArrivals of the level parks (the themes' 150 timer units would bring no group within a few days).</summary>
	public int LevelArrivalTimeBetween { get; init; } = 20;
	public ParkScenarioOptions Scenario { get; init; } = new();
	public GuestMetricsOptions Metrics { get; init; } = new();
	/// <summary>Measure CPU and allocation per tick with the observer off and on; machine-dependent, so left out of the stable report text when false.</summary>
	public bool Performance { get; init; } = true;
}

public sealed record SeedResult( ulong Seed, GuestMetricsSnapshot Snapshot );

public sealed record MetricSpread( string Name, int Count, double Mean, double StandardDeviation, double Min, double Max );

public sealed record ScenarioReport( string Name, string Kind, IReadOnlyList<SeedResult> Seeds, IReadOnlyList<MetricSpread> Spread );

public sealed record PerformanceReport( int Ticks, double ObserverOffMicrosecondsPerTick, double ObserverOnMicrosecondsPerTick, double ObserverOffBytesPerTick, double ObserverOnBytesPerTick );

public sealed record GuestReportResult( GuestReportOptions Options, IReadOnlyList<ScenarioReport> Scenarios, PerformanceReport? Performance, IReadOnlyList<string> Skipped );

/// <summary>
/// The headless guest report (<c>--guest-report</c>, docs/SMARTER-GUESTS.md "Measurement"): fixed seeds times the representative <see cref="ParkScenario"/>, and with game data the
/// level gate's park of each theme, each run with a warm-up and a measured window of in-game days, summarised by a passive <see cref="GuestMetrics"/>. It changes nothing in the
/// simulation: a run with the observer gives the same guest hash as one without.
/// </summary>
public static class GuestReport
{
	public const string ScenarioName = "park-scenario";

	/// <summary>
	/// The extended scale (<c>--guest-scale extended</c>): a day is 343 fixed ticks (5.7 s at normal speed) and a guest's exit counter runs about 120 s, so the plan's 2 + 10 days see
	/// few departures and few queue attempts; 20 + 60 days reach a steady park.
	/// </summary>
	public const int ExtendedWarmupDays = 20, ExtendedMeasuredDays = 60;

	/// <summary>Ticks from the start to the end of in-game day <paramref name="days"/>.</summary>
	public static long TicksForDays( int days ) => ParkCalendar.TickAtDay( days );

	/// <summary>Runs one synthetic park to the end of the window and returns its snapshot; <paramref name="hash"/> is the guests' state hash at the end.</summary>
	public static GuestMetricsSnapshot RunScenario( ulong seed, GuestReportOptions options, out ulong hash )
	{
		var park = new ParkScenario( new WorldSeed( seed ), options.Scenario );
		var metrics = new GuestMetrics( options.Metrics );
		metrics.Attach( park.Guests );
		var warmup = TicksForDays( options.WarmupDays );
		park.Run( warmup );
		metrics.BeginWindow();
		park.Run( TicksForDays( options.WarmupDays + options.MeasuredDays ) - warmup );
		hash = park.Hash;
		return metrics.EndWindow();
	}

	public static GuestMetricsSnapshot RunScenario( ulong seed, GuestReportOptions options ) => RunScenario( seed, options, out _ );

	/// <summary>One level park built by the level gate's builder (docs/M3-GATE.md), run for the same days.</summary>
	internal static GuestMetricsSnapshot RunLevel( string level, ulong seed, GuestReportOptions options )
	{
		var metrics = new GuestMetrics( options.Metrics ) { AutoBeginAtTick = TicksForDays( options.WarmupDays ) };
		var gate = new M3GateOptions { Level = level, Seed = new WorldSeed( seed ).GuestStream, WorldSeed = new WorldSeed( seed ), DeterminismProbe = false, SweptTurns = true, ArrivalTimeBetween = options.LevelArrivalTimeBetween, GuestObserver = metrics };
		var run = M3GateRun.Execute( gate, TicksForDays( options.WarmupDays + options.MeasuredDays ) );
		metrics.Attach( run.Simulation );
		return metrics.EndWindow();
	}

	public static GuestReportResult Run( GuestReportOptions options, Action<string>? progress = null )
	{
		var scenarios = new List<ScenarioReport>();
		var skipped = new List<string>();
		var seeds = new List<SeedResult>();
		foreach ( var seed in options.Seeds )
		{
			progress?.Invoke( $"{ScenarioName} seed {seed}" );
			seeds.Add( new SeedResult( seed, RunScenario( seed, options ) ) );
		}
		scenarios.Add( new ScenarioReport( ScenarioName, "synthetic", seeds, Spread( seeds ) ) );
		foreach ( var level in options.Levels )
		{
			var levelSeeds = new List<SeedResult>();
			try
			{
				foreach ( var seed in options.Seeds.Take( Math.Max( 1, options.LevelSeeds ) ) )
				{
					progress?.Invoke( $"level {level} seed {seed}" );
					levelSeeds.Add( new SeedResult( seed, RunLevel( level, seed, options ) ) );
				}
				scenarios.Add( new ScenarioReport( $"level-{level}", "level", levelSeeds, Spread( levelSeeds ) ) );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or DirectoryNotFoundException or FileNotFoundException )
			{
				skipped.Add( $"level-{level}: {exception.Message}" );
			}
		}
		return new GuestReportResult( options, scenarios, options.Performance ? MeasurePerformance( options ) : null, skipped );
	}

	private static IReadOnlyList<MetricSpread> Spread( IReadOnlyList<SeedResult> seeds )
	{
		var spread = new List<MetricSpread>();
		if ( seeds.Count == 0 )
			return spread;
		var tables = seeds.Select( seed => seed.Snapshot.Headline() ).ToList();
		for ( var index = 0; index < tables[0].Count; index++ )
		{
			var values = tables.Select( table => table[index].Value ).Where( double.IsFinite ).ToList();
			if ( values.Count == 0 )
			{
				spread.Add( new MetricSpread( tables[0][index].Name, 0, double.NaN, double.NaN, double.NaN, double.NaN ) );
				continue;
			}
			var mean = values.Average();
			var deviation = values.Count < 2 ? 0 : Math.Sqrt( values.Sum( value => (value - mean) * (value - mean) ) / (values.Count - 1) );
			spread.Add( new MetricSpread( tables[0][index].Name, values.Count, mean, deviation, values.Min(), values.Max() ) );
		}
		return spread;
	}

	/// <summary>CPU and allocation per tick of the measured window of the scenario's first seed, with the observer off (the production path) and on; the best of five timings each, after a warm-up pair.</summary>
	public static PerformanceReport MeasurePerformance( GuestReportOptions options )
	{
		var seed = options.Seeds.Count > 0 ? options.Seeds[0] : 1;
		var warmup = TicksForDays( options.WarmupDays );
		var measured = TicksForDays( options.WarmupDays + options.MeasuredDays ) - warmup;
		(double Micros, double Bytes) Measure( bool observed )
		{
			var best = double.MaxValue;
			var bytes = 0d;
			var park = new ParkScenario( new WorldSeed( seed ), options.Scenario );
			if ( observed )
				new GuestMetrics( options.Metrics ).Attach( park.Guests );
			park.Run( warmup );
			GC.Collect();
			var allocated = GC.GetAllocatedBytesForCurrentThread();
			var watch = Stopwatch.StartNew();
			park.Run( measured );
			watch.Stop();
			bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)measured;
			best = watch.Elapsed.TotalMilliseconds * 1000 / measured;
			return (best, bytes);
		}
		// one unmeasured pair warms the JIT; then the pairs alternate, and each side keeps its best time
		_ = Measure( false );
		_ = Measure( true );
		double offMicros = double.MaxValue, onMicros = double.MaxValue, offBytes = 0, onBytes = 0;
		for ( var repeat = 0; repeat < 5; repeat++ )
		{
			var off = Measure( false );
			var on = Measure( true );
			offMicros = Math.Min( offMicros, off.Micros );
			onMicros = Math.Min( onMicros, on.Micros );
			(offBytes, onBytes) = (off.Bytes, on.Bytes);
		}
		return new PerformanceReport( (int)measured, offMicros, onMicros, offBytes, onBytes );
	}

	public static JsonObject ToJsonObject( GuestReportResult result )
	{
		var options = result.Options;
		static JsonNode? N( double value ) => double.IsFinite( value ) ? JsonValue.Create( GuestMetrics.Round( value ) ) : null;
		var scenarios = new JsonArray();
		foreach ( var scenario in result.Scenarios )
		{
			scenarios.Add( new JsonObject
			{
				["name"] = scenario.Name,
				["kind"] = scenario.Kind,
				["seeds"] = new JsonArray( scenario.Seeds.Select( seed => (JsonNode)new JsonObject { ["seed"] = seed.Seed, ["metrics"] = seed.Snapshot.ToJsonObject() } ).ToArray() ),
				["spread"] = new JsonArray( scenario.Spread.Select( item => (JsonNode)new JsonObject
				{
					["metric"] = item.Name,
					["n"] = item.Count,
					["mean"] = N( item.Mean ),
					["sd"] = N( item.StandardDeviation ),
					["min"] = N( item.Min ),
					["max"] = N( item.Max )
				} ).ToArray() )
			} );
		}
		var root = new JsonObject
		{
			["schema"] = "opentpw.guest-report/1",
			["protocol"] = new JsonObject
			{
				["seeds"] = new JsonArray( options.Seeds.Select( seed => (JsonNode)JsonValue.Create( seed ) ).ToArray() ),
				["warmupDays"] = options.WarmupDays,
				["measuredDays"] = options.MeasuredDays,
				["warmupTicks"] = TicksForDays( options.WarmupDays ),
				["measuredTicks"] = TicksForDays( options.WarmupDays + options.MeasuredDays ) - TicksForDays( options.WarmupDays ),
				["noChoiceTurns"] = options.Metrics.NoChoiceTurns,
				["levelSeeds"] = options.LevelSeeds,
				["levelArrivalTimeBetween"] = options.LevelArrivalTimeBetween,
				["scenario"] = new JsonObject
				{
					["arrivalTimeBetween"] = options.Scenario.ArrivalTimeBetween,
					["admissionFee"] = options.Scenario.AdmissionFee,
					["cheapPrice"] = options.Scenario.CheapPrice,
					["dearPrice"] = options.Scenario.DearPrice
				}
			},
			["scenarios"] = scenarios,
			["skipped"] = new JsonArray( result.Skipped.Select( item => (JsonNode)JsonValue.Create( item )! ).ToArray() )
		};
		if ( result.Performance is { } performance )
		{
			root["performance"] = new JsonObject
			{
				["note"] = "machine-dependent: not part of the stable comparison",
				["ticks"] = performance.Ticks,
				["observerOffMicrosecondsPerTick"] = N( performance.ObserverOffMicrosecondsPerTick ),
				["observerOnMicrosecondsPerTick"] = N( performance.ObserverOnMicrosecondsPerTick ),
				["observerOffBytesPerTick"] = N( performance.ObserverOffBytesPerTick ),
				["observerOnBytesPerTick"] = N( performance.ObserverOnBytesPerTick )
			};
		}
		return root;
	}

	private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	public static string ToJson( GuestReportResult result ) => ToJsonObject( result ).ToJsonString( Indented ).Replace( "\r\n", "\n" );

	/// <summary>The short table: one block per scenario with each metric's mean and standard deviation over its seeds.</summary>
	public static string ToTable( GuestReportResult result )
	{
		var text = new StringBuilder();
		var options = result.Options;
		text.AppendLine( $"Guest report: {options.Seeds.Count} seeds, {options.WarmupDays} warm-up days + {options.MeasuredDays} measured days ({TicksForDays( options.MeasuredDays )} ticks)" );
		string F( double value, string format = "0.###" ) => double.IsFinite( value ) ? value.ToString( format, CultureInfo.InvariantCulture ) : "-";
		foreach ( var scenario in result.Scenarios )
		{
			text.AppendLine();
			text.AppendLine( $"== {scenario.Name} ({scenario.Seeds.Count} seeds) ==" );
			text.AppendLine( $"{"metric",-28} {"mean",12} {"sd",12} {"min",12} {"max",12}" );
			foreach ( var item in scenario.Spread )
				text.AppendLine( $"{item.Name,-28} {F( item.Mean ),12} {F( item.StandardDeviation ),12} {F( item.Min ),12} {F( item.Max ),12}" );
			var queue = scenario.Seeds.Select( seed => seed.Snapshot.Queue ).ToList();
			var rejected = queue.SelectMany( item => item.RejectedByReason ).GroupBy( item => item.Key ).OrderBy( group => group.Key, StringComparer.Ordinal ).Select( group => $"{group.Key} {group.Sum( item => item.Value )}" );
			var abandoned = queue.SelectMany( item => item.AbandonedByReason ).GroupBy( item => item.Key ).OrderBy( group => group.Key, StringComparer.Ordinal ).Select( group => $"{group.Key} {group.Sum( item => item.Value )}" );
			text.AppendLine( $"rejected by reason (all seeds): {string.Join( ", ", rejected )}" );
			text.AppendLine( $"abandoned by reason (all seeds): {string.Join( ", ", abandoned )}" );
		}
		foreach ( var skipped in result.Skipped )
			text.AppendLine( $"skipped: {skipped}" );
		if ( result.Performance is { } performance )
		{
			text.AppendLine();
			text.AppendLine( $"per tick over {performance.Ticks} ticks: observer off {F( performance.ObserverOffMicrosecondsPerTick, "0.00" )} us, {F( performance.ObserverOffBytesPerTick, "0" )} B; observer on {F( performance.ObserverOnMicrosecondsPerTick, "0.00" )} us, {F( performance.ObserverOnBytesPerTick, "0" )} B" );
		}
		return text.ToString();
	}

	/// <summary>True when the command line asks for level parks, which need the game's data (so the command is dispatched after the data is set up).</summary>
	public static bool WantsLevels( string[] args ) => Levels( args ).Count > 0;

	private static string? Option( string[] args, string name )
	{
		var index = Array.IndexOf( args, name );
		if ( index < 0 )
			return null;
		if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--" ) )
			throw new ArgumentException( $"{name} requires a value." );
		return args[index + 1];
	}

	private static int IntOption( string[] args, string name, int minimum, int fallback )
	{
		if ( Option( args, name ) is not { } text )
			return fallback;
		return int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) && value >= minimum ? value : throw new ArgumentException( $"{name} requires an integer of at least {minimum}." );
	}

	/// <summary>The level parks the command line selects: <c>--guest-levels</c> names them (or <c>all</c>, or <c>none</c>); by default all four themes when game data is set up.</summary>
	private static IReadOnlyList<string> Levels( string[] args )
	{
		var named = Option( args, "--guest-levels" );
		if ( named == "none" )
			return Array.Empty<string>();
		if ( named is { } list && list != "all" )
			return list.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		var hasData = Option( args, "--game-path" ) != null || !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" ) );
		return hasData ? new[] { "jungle", "hallow", "fantasy", "space" } : Array.Empty<string>();
	}

	/// <summary>
	/// <c>--guest-report</c> with <c>--guest-seeds N</c> (default 8), <c>--guest-warmup-days N</c> (2), <c>--guest-days N</c> (10), <c>--guest-levels a,b|all|none</c> (all four
	/// themes when OPENTPW_GAME_PATH or --game-path is set), <c>--guest-level-seeds N</c> (8), <c>--guest-arrivals N</c> (level parks' arrival timer, 20),
	/// <c>--guest-report-json path</c>, <c>--guest-scale plan|extended</c> (the days above, or 20 + 60) and <c>--guest-no-perf</c>. Prints the table; 0 on success.
	/// </summary>
	public static int RunCommand( string[] args )
	{
		var extended = Option( args, "--guest-scale" ) is { } scale ? scale == "extended" ? true : scale == "plan" ? false : throw new ArgumentException( "--guest-scale is 'plan' or 'extended'." ) : false;
		var seedCount = IntOption( args, "--guest-seeds", 1, 8 );
		var options = new GuestReportOptions
		{
			Seeds = Enumerable.Range( 1, seedCount ).Select( index => (ulong)index ).ToArray(),
			WarmupDays = IntOption( args, "--guest-warmup-days", 0, extended ? ExtendedWarmupDays : 2 ),
			MeasuredDays = IntOption( args, "--guest-days", 1, extended ? ExtendedMeasuredDays : 10 ),
			Levels = Levels( args ),
			LevelSeeds = IntOption( args, "--guest-level-seeds", 1, 8 ),
			LevelArrivalTimeBetween = IntOption( args, "--guest-arrivals", 1, 20 ),
			Performance = !args.Contains( "--guest-no-perf" )
		};
		// the level parks log their clock and script lines; the report's own output stays clean
		var output = Console.Out;
		GuestReportResult result;
		Console.SetOut( TextWriter.Null );
		try
		{
			result = Run( options, step => Console.Error.WriteLine( step ) );
		}
		finally
		{
			Console.SetOut( output );
		}
		Console.Write( ToTable( result ) );
		if ( Option( args, "--guest-report-json" ) is { } path )
		{
			File.WriteAllText( path, ToJson( result ) + "\n" );
			Console.WriteLine( $"Report: {path}" );
		}
		return 0;
	}
}
