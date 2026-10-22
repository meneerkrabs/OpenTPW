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
	// Time outside the load (other frames while a park is built over several, docs/BROWSER-PERFORMANCE.md): left out of every
	// section open across it, so a section still counts only its own work.
	private static long excludedTicks;
	private static long pausedAt = -1;

	// Counted amounts (files opened, bytes read, shaders compiled, docs/ENGINE-PERFORMANCE.md "Start and build hitches"): unlike the sections they are
	// counted on every thread while recording is on, so work handed to a helper thread is not lost.
	private static readonly Dictionary<string, long> counters = new();
	private static volatile bool counting;

	/// <summary>Turns recording on (clearing what was recorded) or off, for the calling thread.</summary>
	public static void Enable( bool enabled )
	{
		totals.Clear();
		lock ( counters )
			counters.Clear();
		counting = enabled;
		excludedTicks = 0;
		pausedAt = -1;
		threadId = enabled ? Environment.CurrentManagedThreadId : -1;
	}

	/// <summary>Starts a section; dispose it to record it. A no-op while recording is off.</summary>
	public static Section Measure( string name ) => threadId == Environment.CurrentManagedThreadId || FrameMarks.Enabled ? new Section( name, Stopwatch.GetTimestamp(), excludedTicks ) : default;

	/// <summary>True while a profile records (<see cref="Enable"/>).</summary>
	public static bool Recording => counting;

	/// <summary>Adds <paramref name="amount"/> to the counter <paramref name="name"/>, from any thread; a no-op while recording is off.</summary>
	public static void Count( string name, long amount = 1 )
	{
		if ( !counting )
			return;
		lock ( counters )
			counters[name] = counters.TryGetValue( name, out var total ) ? total + amount : amount;
	}

	/// <summary>The counters recorded so far (<see cref="Count"/>).</summary>
	public static IReadOnlyDictionary<string, long> Counters()
	{
		lock ( counters )
			return new Dictionary<string, long>( counters );
	}

	/// <summary>The load stops for other work until <see cref="Resume"/>; open sections do not count that time.</summary>
	public static void Pause()
	{
		if ( threadId == Environment.CurrentManagedThreadId && pausedAt < 0 )
			pausedAt = Stopwatch.GetTimestamp();
	}

	/// <summary>The load goes on after <see cref="Pause"/>.</summary>
	public static void Resume()
	{
		if ( threadId != Environment.CurrentManagedThreadId || pausedAt < 0 )
			return;
		excludedTicks += Stopwatch.GetTimestamp() - pausedAt;
		pausedAt = -1;
	}

	/// <summary>The recorded sections: total milliseconds and how often each ran.</summary>
	public static IReadOnlyDictionary<string, (double Milliseconds, int Count)> Snapshot() =>
		totals.ToDictionary( entry => entry.Key, entry => (entry.Value.Ticks * 1000d / Stopwatch.Frequency, entry.Value.Count) );

	public readonly struct Section : IDisposable
	{
		private readonly string? name;
		private readonly long start;
		private readonly long excludedAtStart;

		internal Section( string name, long start, long excludedAtStart )
		{
			this.name = name;
			this.start = start;
			this.excludedAtStart = excludedAtStart;
		}

		public void Dispose()
		{
			if ( name == null )
				return;
			if ( threadId != Environment.CurrentManagedThreadId )
			{
				// only the per-frame marks are on (the browser event-hitch harness)
				FrameMarks.Add( "load:" + name, Stopwatch.GetTimestamp() - start );
				return;
			}
			var elapsed = Stopwatch.GetTimestamp() - start - (excludedTicks - excludedAtStart);
			totals[name] = totals.TryGetValue( name, out var total ) ? (total.Ticks + elapsed, total.Count + 1) : (elapsed, 1);
			FrameMarks.Add( "load:" + name, elapsed );
		}
	}
}
