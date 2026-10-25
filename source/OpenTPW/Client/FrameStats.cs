using System.Diagnostics;

namespace OpenTPW;

/// <summary>
/// <c>--frame-stats</c>: logs frame timing, garbage collections and allocation rate every few seconds, so
/// render changes can be compared run against run (use <c>--no-vsync</c> to see frames above the display rate).
/// </summary>
internal sealed class FrameStats : IDisposable
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds( 5 );

	/// <summary>True while a <see cref="FrameStats"/> run is on; the phase timers below cost nothing otherwise.</summary>
	public static bool Active { get; private set; }

	/// <summary>Stopwatch ticks of the current window spent in the frame's phases: the update (the simulation included), the simulation alone, the world drawing, the interface overlay, submit plus present, the GPU wait for the frame slot and the whole of Renderer.Update.</summary>
	private static long updateTicks, simulationTicks, renderTicks, overlayTicks, submitTicks, fenceTicks, totalTicks;

	/// <summary>The timestamp to hand to the phase methods, or 0 when no frame statistics are on.</summary>
	public static long Begin() => Active ? Stopwatch.GetTimestamp() : 0;

	public static void EndUpdate( long start ) { if ( start != 0 ) updateTicks += Stopwatch.GetTimestamp() - start; }
	public static void EndSimulation( long start ) { if ( start != 0 ) simulationTicks += Stopwatch.GetTimestamp() - start; }
	public static void EndRender( long start ) { if ( start != 0 ) renderTicks += Stopwatch.GetTimestamp() - start; }
	public static void EndOverlay( long start ) { if ( start != 0 ) overlayTicks += Stopwatch.GetTimestamp() - start; }
	public static void EndTotal( long start ) { if ( start != 0 ) totalTicks += Stopwatch.GetTimestamp() - start; }
	public static void EndSubmit( long start ) { if ( start != 0 ) submitTicks += Stopwatch.GetTimestamp() - start; }
	public static void EndFence( long start ) { if ( start != 0 ) fenceTicks += Stopwatch.GetTimestamp() - start; }

	private static double Milliseconds( long ticks, int frames ) => Stopwatch.GetElapsedTime( 0, ticks ).TotalMilliseconds / frames;

	private readonly List<double> frameMilliseconds = new();
	private long windowStart = Stopwatch.GetTimestamp();
	private long lastFrame = Stopwatch.GetTimestamp();
	private long allocatedAtStart = GC.GetTotalAllocatedBytes();
	private int gen0AtStart = GC.CollectionCount( 0 );
	private int gen1AtStart = GC.CollectionCount( 1 );
	private int gen2AtStart = GC.CollectionCount( 2 );
	private TimeSpan cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime;

	public FrameStats()
	{
		Active = true;
		(updateTicks, simulationTicks, renderTicks, overlayTicks, submitTicks, fenceTicks, totalTicks) = (0, 0, 0, 0, 0, 0, 0);
	}

	/// <summary>Switches the phase timers off.</summary>
	public void Dispose() => Active = false;

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
		var cpuSeconds = (cpu - cpuAtStart).TotalSeconds;
		Log.Trace( $"Frame stats: {count / seconds:F1} fps, frame avg {average:F2} ms, p99 {p99:F2} ms, max {max:F2} ms; " +
			$"CPU {cpuSeconds / seconds:F2} cores ({cpuSeconds * 1000 / count:F2} ms per frame); " +
			$"allocated {(allocated - allocatedAtStart) / seconds / 1e6:F1} MB/s ({(allocated - allocatedAtStart) / 1024.0 / count:F0} KB per frame); " +
			$"GC gen0/1/2 {GC.CollectionCount( 0 ) - gen0AtStart}/{GC.CollectionCount( 1 ) - gen1AtStart}/{GC.CollectionCount( 2 ) - gen2AtStart}; " +
			$"per frame update {Milliseconds( updateTicks, count ):F2} ms (simulation {Milliseconds( simulationTicks, count ):F2}), world render {Milliseconds( renderTicks, count ):F2}, interface overlay {Milliseconds( overlayTicks, count ):F2}, submit+present {Milliseconds( submitTicks, count ):F2}, GPU wait {Milliseconds( fenceTicks, count ):F2}, whole update call {Milliseconds( totalTicks, count ):F2}; " +
			$"model parts per frame {(double)ModelEntity.DrawnParts / count:F0} drawn, {(double)ModelEntity.CulledParts / count:F0} outside the view, {(double)WorldPass.FrameRecords / count:F0} translucent records in the pass." );
		WorldPass.FrameRecords = 0;
		ModelEntity.DrawnParts = 0;
		ModelEntity.CulledParts = 0;
		(updateTicks, simulationTicks, renderTicks, overlayTicks, submitTicks, fenceTicks, totalTicks) = (0, 0, 0, 0, 0, 0, 0);

		frameMilliseconds.Clear();
		windowStart = now;
		allocatedAtStart = allocated;
		cpuAtStart = cpu;
		gen0AtStart = GC.CollectionCount( 0 );
		gen1AtStart = GC.CollectionCount( 1 );
		gen2AtStart = GC.CollectionCount( 2 );
	}
}
