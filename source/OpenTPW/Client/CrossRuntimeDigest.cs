using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>
/// The with-data cross-runtime digest (docs/DETERMINISM.md, "Browser and native"): the same simulation work run on the desktop
/// (<c>--determinism-digest</c>) and in the browser (<c>Program.PerfDeterminismDigest</c>, tools/web-perf scenario <c>determinism</c>), so the
/// two outputs can be compared value by value. Per theme: the fixed park run of the browser digest (economy and guests, seed 2026, 3000
/// ticks at the fastest speed) with one employee of every role hired from the pool first; the weather of the theme's level for two park
/// years; and the M4 gate's scenario (docs/M4-GATE.md: every supported ride, shop, sideshow and feature built in its own park, run with
/// guests, stopped and deleted, with every object script, and the track rides' stations), as the list of its state hashes. Each part is
/// timed. Needs the original game files.
/// </summary>
public static class CrossRuntimeDigest
{
	/// <summary>The four themes in the order the digest reports them.</summary>
	public static readonly IReadOnlyList<string> Themes = ObjectCatalog.Themes;

	/// <summary>One theme's digest: { theme, fixedRun, weather, m4: { digest, count, hashes } } plus the milliseconds of each part.</summary>
	public static JsonObject Compute( string theme, bool includeM4 = true, bool listHashes = true )
	{
		var result = new JsonObject { ["theme"] = theme };
		var watch = Stopwatch.StartNew();
		result["fixedRun"] = FixedParkRun( theme, hireStaff: false ).ToString( "X16" );
		result["fixedRunWithStaff"] = FixedParkRun( theme, hireStaff: true ).ToString( "X16" );
		result["fixedRunMs"] = watch.ElapsedMilliseconds;
		watch.Restart();
		result["weather"] = WeatherRun( theme ).ToString( "X16" );
		result["weatherMs"] = watch.ElapsedMilliseconds;
		if ( includeM4 )
		{
			watch.Restart();
			var run = M4GateRun.Execute( new M4GateOptions { Themes = new[] { theme }, DeterminismProbe = false }, 0 );
			var m4 = new JsonObject { ["digest"] = HashList( run.MinuteHashes ).ToString( "X16" ), ["count"] = run.MinuteHashes.Count };
			if ( listHashes )
				m4["hashes"] = new JsonArray( run.MinuteHashes.Select( item => (JsonNode?)new JsonArray( item.Key, item.Hash.ToString( "X16" ) ) ).ToArray() );
			result["m4"] = m4;
			result["m4Ms"] = watch.ElapsedMilliseconds;
		}
		return result;
	}

	/// <summary>
	/// The browser digest's fixed park run (tools/web-perf scenario <c>digest</c>): the theme's park from its shipped save with its guests from seed
	/// 2026, 3000 ticks at the fastest speed, reduced to its world state hash. With <paramref name="hireStaff"/> the first candidate of every
	/// role in the pool at tick 600 (the pool has filled by then) is hired, so the staff's own updates join the run.
	/// </summary>
	public static ulong FixedParkRun( string theme, bool hireStaff )
	{
		var seed = new WorldSeed( 2026 );
		var original = OriginalPark.Load( theme );
		var park = ParkEconomyRuntime.ForOriginalLevel( original, ParkStartKind.OriginalSaveReference, seed );
		var guests = new GuestSimulation( GuestPathGrid.FromOriginal( original.Map, original.Save, original.Heightfield ), GuestSettings.Load( theme ), seed.GuestStream );
		park.AttachGuests( guests );
		var streams = new ParkWorldStreams( seed, park, guests, new RideScriptWorld( seed.ScriptStream, RideScriptWorld.FixedClock ), new SoundEventSystem( new AudioMixer( null ), seed.SoundStream ) ).LinkGenerator();
		park.Economy.Speed = GameSpeed.Fastest;
		for ( var tick = 0; tick < 3000; tick++ )
		{
			if ( hireStaff && tick == HireTick && park.Economy.Rules.Staff )
				foreach ( var candidate in park.Economy.Staff.Candidates.GroupBy( candidate => candidate.Type ).Select( group => group.First() ).ToList() )
					park.Economy.Hire( candidate.Id );
			guests.Tick( 1f / FixedStepClock.TicksPerSecond );
			park.Economy.Advance( (int)park.Economy.Speed );
		}
		return streams.ComputeStateHash();
	}

	private const int HireTick = 600;

	/// <summary>The weather of the theme's level (its Standard.sam, the level's weather stream of seed 2026) for two park years, a snapshot hashed every park day.</summary>
	public static ulong WeatherRun( string theme )
	{
		var seed = new WorldSeed( 2026 );
		var original = OriginalPark.Load( theme );
		var park = ParkEconomyRuntime.ForOriginalLevel( original, ParkStartKind.OriginalSaveReference, seed );
		var grid = GuestPathGrid.FromOriginal( original.Map, original.Save, original.Heightfield );
		var weather = new ParkWeather( park.Economy.Settings.Standard, seed.WeatherStream, grid.CountX, grid.CountY );
		var hash = 14695981039346656037UL;
		const long TicksPerDay = 343; // 23.04 turns of 14.88 ticks
		for ( long day = 0; day < 2 * 365; day++ )
		{
			weather.AdvanceTo( day * TicksPerDay );
			hash = Fnv( hash, JsonSerializer.SerializeToUtf8Bytes( weather.CaptureSnapshot() ) );
		}
		return hash;
	}

	/// <summary>FNV-1a over every (key, hash) of a list.</summary>
	public static ulong HashList( IEnumerable<(string Key, ulong Hash)> items )
	{
		var hash = 14695981039346656037UL;
		foreach ( var (key, value) in items )
			hash = Fnv( Fnv( hash, Encoding.UTF8.GetBytes( key ) ), BitConverter.GetBytes( value ) );
		return hash;
	}

	private static ulong Fnv( ulong hash, ReadOnlySpan<byte> bytes )
	{
		foreach ( var value in bytes )
			hash = (hash ^ value) * 1099511628211UL;
		return hash;
	}

	/// <summary>
	/// <c>--determinism-digest [jungle,hallow,space,fantasy] [--no-m4] [--out path.json]</c>: computes the digest of each theme on this runtime and prints it as
	/// JSON (the browser's twin is tools/web-perf's <c>determinism</c> scenario). Returns 0.
	/// </summary>
	public static int RunCommand( string[] args )
	{
		var index = Array.IndexOf( args, "--determinism-digest" );
		var themes = index + 1 < args.Length && !args[index + 1].StartsWith( "--", StringComparison.Ordinal )
			? args[index + 1].Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
			: Themes.ToArray();
		var report = new JsonObject { ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, ["os"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier };
		var results = new JsonArray();
		foreach ( var theme in themes )
		{
			var digest = Compute( theme, includeM4: !args.Contains( "--no-m4" ) );
			Console.WriteLine( $"{theme}: fixed run {digest["fixedRun"]}, with staff {digest["fixedRunWithStaff"]}, weather {digest["weather"]}, m4 {digest["m4"]?["digest"]} ({digest["m4"]?["count"]} hashes)" );
			results.Add( digest );
		}
		report["themes"] = results;
		var outIndex = Array.IndexOf( args, "--out" );
		if ( outIndex >= 0 && outIndex + 1 < args.Length )
		{
			File.WriteAllText( args[outIndex + 1], report.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) + "\n" );
			Console.WriteLine( $"Report: {args[outIndex + 1]}" );
		}
		return 0;
	}
}
