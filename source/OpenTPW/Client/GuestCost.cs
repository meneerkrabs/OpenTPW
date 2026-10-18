using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OpenTPW;

/// <summary>
/// The cost of the smarter-guests extension against the original guests (docs/SMARTER-GUESTS.md, phase 6 "CPU gate"): <c>--guest-report --guest-cost</c> runs the same <see cref="ParkScenario"/> for each variant (the original guests and
/// the extension with some rule sets), several seeds, several repeats each, <see cref="ParkScenario.RunTimed"/>, and prints the median of the total time per run, the median of the guests' own update time, the guests simulated and the
/// guest update time per guest-turn (a guest in the park for one park turn). The extension keeps more guests in the park than the original (happier guests stay longer), so the total ratio and the per-guest-turn ratio differ; both are printed.
/// The variants are interleaved inside each repeat, so a change of the machine's load during the run moves them alike; the process CPU time is printed beside the wall time.
/// </summary>
public static class GuestCost
{
	/// <summary>One measured run: wall milliseconds of the guests' update, process CPU milliseconds of the whole run, guest-turns, turns, wall milliseconds of the whole run, bytes allocated by the thread during the run.</summary>
	private readonly record struct Sample( double GuestMs, double CpuMs, long GuestTurns, long Turns, double TotalMs, double AllocatedBytes );

	private sealed record Variant( string Name, Func<ulong, ParkScenario> Build );

	public static int RunCommand( string[] args )
	{
		static string? Option( string[] a, string name ) { var i = Array.IndexOf( a, name ); return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith( "--" ) ? a[i + 1] : null; }
		int Int( string name, int fallback ) => Option( args, name ) is { } text && int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) && value > 0 ? value : fallback;
		var days = Int( "--guest-cost-days", 250 );
		var seeds = Int( "--guest-cost-seeds", 3 );
		var repeats = Int( "--guest-cost-repeats", 7 );
		var options = GuestReport.ScenarioOptions( args );
		var only = Option( args, "--guest-cost-variants" )?.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		var variants = new List<Variant> { new( "original", seed => new ParkScenario( new WorldSeed( seed ), options ) ) };
		foreach (var (name, rules) in new[] { ("1.3.0", SmarterRuleSet.None), ("1.4.0", SmarterRuleSet.Phase5), ("1.5.0", SmarterRuleSet.Phase6), ("1.6.0", SmarterRuleSet.All) } )
			variants.Add( new Variant( name, seed => new ParkScenario( new WorldSeed( seed ), options, new SmarterGuestDecisions( rules ) ) ) );
		if ( only != null )
			variants = variants.Where( variant => only.Contains( variant.Name ) ).ToList();
		var ticks = GuestReport.TicksForDays( days );
		// the Run and RunTimed loops are the same calls, so the end state of a timed run is the untimed one's
		var reference = new ParkScenario( new WorldSeed( 1 ), options );
		reference.Run( 3000 );
		var check = new ParkScenario( new WorldSeed( 1 ), options );
		check.RunTimed( 3000, Stopwatch.GetTimestamp );
		if ( reference.Hash != check.Hash )
			throw new InvalidOperationException( "RunTimed does not run the scenario that Run does." );
		var results = variants.ToDictionary( variant => variant.Name, _ => new List<Sample>() );
		// one unmeasured round warms the JIT
		foreach ( var variant in variants )
			_ = Measure( variant.Build( 1 ), Math.Min( ticks, 4000 ) );
		for ( var repeat = 0; repeat < repeats; repeat++ )
		{
			for ( ulong seed = 1; seed <= (ulong)seeds; seed++ )
			{
				// the order of the variants rotates, so none always runs just after another
				for ( var step = 0; step < variants.Count; step++ )
				{
					var variant = variants[(step + repeat + (int)seed) % variants.Count];
					results[variant.Name].Add( Measure( variant.Build( seed ), ticks ) );
				}
			}
		}
		var table = new StringBuilder();
		table.AppendLine( $"Guest cost: {days} in-game days ({ticks} ticks), {seeds} seeds x {repeats} repeats = {seeds * repeats} runs per variant, medians{(options.FoodShop ? ", scenario with the food shop" : "")}{(options.Litter ? ", litter variant" : "")}{(options.Bins ? ", with litter bins" : "")}" );
		table.AppendLine( $"{"variant",-9} {"total ms",9} {"cpu ms",9} {"guest ms",9} {"guests/turn",11} {"ns/guest-turn",13} {"B/guest-turn",12}  {"total x",8} {"cpu x",8} {"guest x",8} {"per guest-turn x",16}" );
		var baseline = Median( results[variants[0].Name] );
		foreach ( var variant in variants )
		{
			var m = Median( results[variant.Name] );
			var perTurn = m.GuestMs * 1e6 / Math.Max( 1, m.GuestTurns );
			var basePerTurn = baseline.GuestMs * 1e6 / Math.Max( 1, baseline.GuestTurns );
			table.AppendLine( $"{variant.Name,-9} {m.TotalMs,9:0.0} {m.CpuMs,9:0.0} {m.GuestMs,9:0.0} {(double)m.GuestTurns / Math.Max( 1, m.Turns ),11:0.0} {perTurn,13:0.0} {m.AllocatedBytes / Math.Max( 1, m.GuestTurns ),12:0.0}  {m.TotalMs / baseline.TotalMs,8:0.000} {m.CpuMs / baseline.CpuMs,8:0.000} {m.GuestMs / baseline.GuestMs,8:0.000} {perTurn / basePerTurn,16:0.000}" );
		}
		table.AppendLine( "total = whole run (ride scripts included); cpu = process CPU time of the run; guest = the guests' own update (RunTimed); x = ratio to the original guests" );
		Console.Write( table.ToString() );
		return 0;
	}

	private static Sample Measure( ParkScenario park, long ticks )
	{
		GC.Collect();
		var process = Process.GetCurrentProcess();
		var cpu = process.TotalProcessorTime;
		var allocated = GC.GetAllocatedBytesForCurrentThread();
		var watch = Stopwatch.StartNew();
		park.RunTimed( ticks, Stopwatch.GetTimestamp );
		watch.Stop();
		var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
		var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds;
		return new Sample( park.GuestWatchTicks * 1000.0 / Stopwatch.Frequency, cpuMs, park.GuestTurns, park.TimedTurns, watch.Elapsed.TotalMilliseconds, bytes );
	}

	private static Sample Median( List<Sample> samples )
	{
		double Of( Func<Sample, double> pick ) { var sorted = samples.Select( pick ).Order().ToArray(); return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2; }
		return new Sample( Of( s => s.GuestMs ), Of( s => s.CpuMs ), (long)Of( s => s.GuestTurns ), (long)Of( s => s.Turns ), Of( s => s.TotalMs ), Of( s => s.AllocatedBytes ) );
	}
}
