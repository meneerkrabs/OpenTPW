using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OpenTPW;

/// <summary>
/// [EXT:perf-run] <c>--startup-profile</c> (docs/ENGINE-PERFORMANCE.md, "Start and build hitches"): records the start of the game up to the lobby with
/// <see cref="LoadProfile"/> (named sections and the counted file opens, bytes read, shaders and textures) and logs the phases once the lobby has drawn
/// its first frames. Measurement only: the game does the same work with it on or off.
/// </summary>
internal static class StartupProfile
{
	private static long start;
	private static double runtimeMilliseconds;

	/// <summary>True when the run was started with <c>--startup-profile</c>.</summary>
	public static bool Active { get; private set; }

	/// <summary>Starts recording, on the thread that runs the game (the render thread).</summary>
	public static void Begin()
	{
		Active = true;
		start = Stopwatch.GetTimestamp();
		// the runtime's own start (loading the runtime, the first JIT) before Main, as far as the process can tell
		try
		{
			runtimeMilliseconds = Math.Max( 0, (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds );
		}
		catch ( Exception exception ) when ( exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception )
		{
			runtimeMilliseconds = double.NaN;
		}
		LoadProfile.Enable( true );
	}

	/// <summary>A named phase of the start (<c>startup.&lt;name&gt;</c>); a no-op unless <see cref="Active"/>.</summary>
	public static LoadProfile.Section Phase( string name ) => LoadProfile.Measure( "startup." + name );

	/// <summary>Milliseconds since <see cref="Begin"/>.</summary>
	public static double Elapsed => Stopwatch.GetElapsedTime( start ).TotalMilliseconds;

	/// <summary>The report's lines: the time to the first lobby frame, the slowest frames after it, the sections and the counters.</summary>
	public static IEnumerable<string> Report( double firstLobbyFrame, IReadOnlyList<double> frames )
	{
		yield return string.Create( CultureInfo.InvariantCulture, $"Startup profile: {runtimeMilliseconds:F0} ms from the process start to Main, {firstLobbyFrame:F0} ms from Main to the lobby's first frame; the next {frames.Count} frames took {frames.Sum():F0} ms (slowest {(frames.Count == 0 ? 0 : frames.Max()):F0} ms)." );
		foreach ( var line in Sections() )
			yield return line;
	}

	/// <summary>The recorded sections, slowest first, and the counters.</summary>
	public static IEnumerable<string> Sections()
	{
		foreach ( var (name, (milliseconds, count)) in LoadProfile.Snapshot().OrderByDescending( entry => entry.Value.Milliseconds ) )
			yield return string.Create( CultureInfo.InvariantCulture, $"  {name,-36} {milliseconds,9:F1} ms {count,6}x" );
		var counters = LoadProfile.Counters();
		if ( counters.Count > 0 )
			yield return "  counted: " + string.Join( ", ", counters.OrderBy( entry => entry.Key, StringComparer.Ordinal ).Select( entry => $"{entry.Key} {entry.Value}" ) );
	}
}

/// <summary>
/// [EXT:perf-run] <c>--hitch-report &lt;milliseconds&gt;</c> (docs/ENGINE-PERFORMANCE.md, "Start and build hitches"): logs every frame that took at least that
/// long with the work recorded in it (<see cref="FrameMarks"/>, the load sections as <c>load:&lt;name&gt;</c>) and what it counted (files, bytes, shaders,
/// textures). <see cref="Label"/> names the frame in progress and the next one and logs them whatever their length. Measurement only.
/// </summary>
internal sealed class HitchReport
{
	private readonly double thresholdMilliseconds;
	private long frameStart;
	private int frame;
	private IReadOnlyDictionary<string, long> countersAtStart = new Dictionary<string, long>();
	private string? label;
	private string? pendingLabel;

	public HitchReport( double thresholdMilliseconds )
	{
		this.thresholdMilliseconds = thresholdMilliseconds;
		if ( !LoadProfile.Recording )
			LoadProfile.Enable( true );
		FrameMarks.Enable( true );
	}

	/// <summary>Names the frame in progress and the one after it (work a step starts may be done in the next frame's draw); both are logged whatever their length.</summary>
	public void Label( string name )
	{
		label = label == null ? name : label + " + " + name;
		pendingLabel = name + ", next frame";
	}

	/// <summary>Call at the start of every frame: ends (and maybe logs) the frame before and starts the next.</summary>
	public void FrameStarted()
	{
		var now = Stopwatch.GetTimestamp();
		var counters = LoadProfile.Counters();
		if ( frameStart != 0 )
		{
			var milliseconds = Stopwatch.GetElapsedTime( frameStart, now ).TotalMilliseconds;
			if ( milliseconds >= thresholdMilliseconds || label != null )
				Log.Trace( Describe( milliseconds, counters ) );
		}
		label = pendingLabel;
		pendingLabel = null;
		countersAtStart = counters;
		FrameMarks.BeginFrame();
		frameStart = now;
		frame++;
	}

	private string Describe( double milliseconds, IReadOnlyDictionary<string, long> counters )
	{
		var text = new StringBuilder();
		text.Append( CultureInfo.InvariantCulture, $"Hitch report: frame {frame}{(label == null ? "" : $" ({label})")} took {milliseconds:F1} ms" );
		var marks = FrameMarks.Snapshot().Where( entry => entry.Value is System.Text.Json.Nodes.JsonArray { Count: > 0 } values && values[0]!.GetValue<double>() >= 0.5 ).Take( 16 ).ToList();
		if ( marks.Count > 0 )
			text.Append( "; " ).Append( string.Join( ", ", marks.Select( entry => $"{entry.Key} {entry.Value![0]} ms/{entry.Value[1]}" ) ) );
		var deltas = counters.Select( entry => (entry.Key, Value: entry.Value - (countersAtStart.TryGetValue( entry.Key, out var before ) ? before : 0)) ).Where( entry => entry.Value != 0 ).ToList();
		if ( deltas.Count > 0 )
			text.Append( "; counted " ).Append( string.Join( ", ", deltas.OrderBy( entry => entry.Key, StringComparer.Ordinal ).Select( entry => $"{entry.Key} {entry.Value}" ) ) );
		return text.Append( '.' ).ToString();
	}
}
