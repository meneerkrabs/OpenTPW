using System.Diagnostics;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>
/// Named work per frame, for the browser event-hitch harness (docs/BROWSER-PERFORMANCE.md, "Event hitches"): while the
/// harness has it on, every <see cref="Measure"/> section adds its wall time to the current frame's entry for its name, and
/// the page reads the entries of the frames it finds slow. Sections nest (a parent includes its children). Off, a section is
/// a boolean test; the game behaves the same either way. The desktop's <c>--hitch-report</c> uses the same marks
/// (docs/ENGINE-PERFORMANCE.md). Only the thread that turned recording on records; sections closed on other threads are left out.
/// </summary>
public static class FrameMarks
{
	private static readonly Dictionary<string, (long Ticks, int Count)> frame = new();
	private static int threadId = -1;

	/// <summary>True while the harness records; set through <see cref="Enable"/>.</summary>
	public static bool Enabled { get; private set; }

	/// <summary>Turns recording on (dropping what was recorded) or off.</summary>
	public static void Enable( bool enabled )
	{
		frame.Clear();
		threadId = enabled ? Environment.CurrentManagedThreadId : -1;
		Enabled = enabled;
	}

	/// <summary>Starts a new frame's entries; call once at the start of each frame.</summary>
	public static void BeginFrame()
	{
		if ( Enabled && threadId == Environment.CurrentManagedThreadId )
			frame.Clear();
	}

	/// <summary>Starts a section; dispose it to record it. A no-op while recording is off.</summary>
	public static Section Measure( string name ) => Enabled ? new Section( name, Stopwatch.GetTimestamp() ) : default;

	/// <summary>Adds a measured time to <paramref name="name"/> in the current frame.</summary>
	public static void Add( string name, long ticks )
	{
		if ( Enabled && threadId == Environment.CurrentManagedThreadId )
			frame[name] = frame.TryGetValue( name, out var total ) ? (total.Ticks + ticks, total.Count + 1) : (ticks, 1);
	}

	/// <summary>The current frame's entries as name → [milliseconds, count], largest first; the frame's entries are kept.</summary>
	public static JsonObject Snapshot()
	{
		var result = new JsonObject();
		foreach ( var (name, (ticks, count)) in frame.OrderByDescending( entry => entry.Value.Ticks ) )
			result[name] = new JsonArray( Math.Round( Stopwatch.GetElapsedTime( 0, ticks ).TotalMilliseconds, 2 ), count );
		return result;
	}

	public readonly struct Section : IDisposable
	{
		private readonly string? name;
		private readonly long start;

		internal Section( string name, long start )
		{
			this.name = name;
			this.start = start;
		}

		public void Dispose()
		{
			if ( name != null )
				Add( name, Stopwatch.GetTimestamp() - start );
		}
	}
}
