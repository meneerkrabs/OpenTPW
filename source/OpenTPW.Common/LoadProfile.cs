using System.Diagnostics;

namespace OpenTPW;

/// <summary>
/// Named timings of a load, for the browser performance harness (docs/BROWSER-PERFORMANCE.md): each section adds its wall time
/// and a count. Sections nest, so a parent's time includes its children's. Off unless the harness turns it on, then a section
/// costs two clock reads and a dictionary update; the game behaves the same either way. Single-threaded use (the page's thread);
/// sections on other threads are not counted.
/// </summary>
public static class LoadProfile
{
	private static readonly Dictionary<string, (long Ticks, int Count)> totals = new();
	private static int threadId = -1;

	/// <summary>Turns recording on (clearing what was recorded) or off, for the calling thread.</summary>
	public static void Enable( bool enabled )
	{
		totals.Clear();
		threadId = enabled ? Environment.CurrentManagedThreadId : -1;
	}

	/// <summary>Starts a section; dispose it to record it. A no-op while recording is off.</summary>
	public static Section Measure( string name ) => threadId == Environment.CurrentManagedThreadId ? new Section( name, Stopwatch.GetTimestamp() ) : default;

	/// <summary>The recorded sections: total milliseconds and how often each ran.</summary>
	public static IReadOnlyDictionary<string, (double Milliseconds, int Count)> Snapshot() =>
		totals.ToDictionary( entry => entry.Key, entry => (entry.Value.Ticks * 1000d / Stopwatch.Frequency, entry.Value.Count) );

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
			if ( name == null || threadId != Environment.CurrentManagedThreadId )
				return;
			var elapsed = Stopwatch.GetTimestamp() - start;
			totals[name] = totals.TryGetValue( name, out var total ) ? (total.Ticks + elapsed, total.Count + 1) : (elapsed, 1);
		}
	}
}
