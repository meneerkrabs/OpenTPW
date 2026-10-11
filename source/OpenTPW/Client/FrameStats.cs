using System.Diagnostics;

namespace OpenTPW;

/// <summary>
/// <c>--frame-stats</c>: logs frame timing, garbage collections and allocation rate every few seconds, so
/// render changes can be compared run against run (use <c>--no-vsync</c> to see frames above the display rate).
/// </summary>
internal sealed class FrameStats
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds( 5 );

	private readonly List<double> frameMilliseconds = new();
	private long windowStart = Stopwatch.GetTimestamp();
	private long lastFrame = Stopwatch.GetTimestamp();
	private long allocatedAtStart = GC.GetTotalAllocatedBytes();
	private int gen0AtStart = GC.CollectionCount( 0 );
	private int gen1AtStart = GC.CollectionCount( 1 );
	private int gen2AtStart = GC.CollectionCount( 2 );
	private TimeSpan cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime;

	/// <summary>Call once per presented frame.</summary>
	public void FrameEnded()
	{
		var now = Stopwatch.GetTimestamp();
		frameMilliseconds.Add( Stopwatch.GetElapsedTime( lastFrame, now ).TotalMilliseconds );
		lastFrame = now;
		var elapsed = Stopwatch.GetElapsedTime( windowStart, now );
		if ( elapsed < Interval || frameMilliseconds.Count == 0 )
			return;

		frameMilliseconds.Sort();
		var count = frameMilliseconds.Count;
		var average = frameMilliseconds.Average();
		var p99 = frameMilliseconds[Math.Min( count - 1, (int)(count * 0.99) )];
		var max = frameMilliseconds[^1];
		var seconds = elapsed.TotalSeconds;
		var allocated = GC.GetTotalAllocatedBytes();
		var cpu = Process.GetCurrentProcess().TotalProcessorTime;
		Log.Trace( $"Frame stats: {count / seconds:F1} fps, frame avg {average:F2} ms, p99 {p99:F2} ms, max {max:F2} ms; " +
			$"CPU {(cpu - cpuAtStart).TotalSeconds / seconds:F2} cores; allocated {(allocated - allocatedAtStart) / seconds / 1e6:F1} MB/s; " +
			$"GC gen0/1/2 {GC.CollectionCount( 0 ) - gen0AtStart}/{GC.CollectionCount( 1 ) - gen1AtStart}/{GC.CollectionCount( 2 ) - gen2AtStart}." );

		frameMilliseconds.Clear();
		windowStart = now;
		allocatedAtStart = allocated;
		cpuAtStart = cpu;
		gen0AtStart = GC.CollectionCount( 0 );
		gen1AtStart = GC.CollectionCount( 1 );
		gen2AtStart = GC.CollectionCount( 2 );
	}
}
