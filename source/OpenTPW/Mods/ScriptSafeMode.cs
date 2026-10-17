using System.Diagnostics;
using OpenTPW.Online;

namespace OpenTPW.Mods;

/// <summary>
/// Safe mode for script mods (docs/SCRIPT-MODS.md): a run with script mods enabled writes a marker file in the configuration folder before its first
/// frame and removes it after a clean start. If the marker is still there at the next start, the previous run did not get through its start-up, so
/// that run starts with every script mod off, without reading, hashing or compiling its scripts, and says so. <c>--safe-mode</c> does the same on
/// demand. The marker is removed as soon as it has been seen, so only the one run is in safe mode; switching the mod off on the mods screen is the
/// lasting fix. The browser has no marker and no safe mode (it ships one script mod, off by default, and has no community mods yet).
/// </summary>
// [EXT:script-mods] OpenTPW's own scripted mods; the original game had no mods
public static class ScriptSafeMode
{
	public const string Option = "--safe-mode";
	public const string MarkerFileName = "script-mods-starting.marker";
	/// <summary>Seconds of running frames after which a start counts as clean and the marker is removed (a normal exit removes it too).</summary>
	public const double CleanStartSeconds = 15;

	/// <summary>Whether this run has script mods switched off.</summary>
	public static bool Active { get; private set; }
	/// <summary>Why script mods are off in this run (an English line for the log), or null when they are not.</summary>
	public static string? Reason { get; private set; }

	public static string MarkerPath( string configDirectory ) => Path.Combine( configDirectory, MarkerFileName );

	/// <summary>
	/// Decides at startup, before any mod is resolved: <c>--safe-mode</c> or a marker left by an unfinished start switches script mods off for this
	/// run. A marker that was seen is removed, so the next start is normal. Smoke tests neither read nor write the marker (they must not interfere
	/// with a real one) but honour <c>--safe-mode</c>.
	/// </summary>
	public static bool Enter( string[] args, string configDirectory, bool smokeTest )
	{
		var flag = args.Contains( Option );
		var marker = MarkerPath( configDirectory );
		var crashed = !smokeTest && File.Exists( marker );
		if ( crashed )
			Remove( marker );
		Active = flag || crashed;
		Reason = crashed
			? "The last start with script mods did not finish; script mods are off for this run (safe mode). Switch the mod off on the Extensions and mods screen."
			: flag ? $"{Option}: script mods are off for this run." : null;
		return Active;
	}

	/// <summary>
	/// Writes the marker when this run has script mods, and returns the handle that removes it after <see cref="CleanStartSeconds"/> of frames or at
	/// a normal exit. Returns null (nothing written) without script mods, in safe mode and in smoke tests. Call it before the first frame.
	/// </summary>
	public static StartMarker? Begin( ModSet set, string configDirectory, bool smokeTest, Func<double>? clock = null )
	{
		if ( smokeTest || Active || !set.Mods.Any( mod => mod.Scripts != null ) )
			return null;
		var path = MarkerPath( configDirectory );
		try
		{
			Directory.CreateDirectory( configDirectory );
			AtomicFile.Write( path, System.Text.Encoding.UTF8.GetBytes( "script mods are starting\n" ) );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return null;
		}
		return new StartMarker( path, clock );
	}

	internal static void Remove( string marker )
	{
		try { File.Delete( marker ); }
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException ) { }
	}

	/// <summary>The marker of a start in progress. <see cref="Update"/> runs once per frame; <see cref="Clear"/> is idempotent.</summary>
	public sealed class StartMarker
	{
		private readonly string path;
		private readonly Func<double> clock;
		private readonly double started;
		private bool cleared;

		internal StartMarker( string path, Func<double>? clock )
		{
			this.path = path;
			var watch = Stopwatch.StartNew();
			this.clock = clock ?? ( () => watch.Elapsed.TotalSeconds );
			started = this.clock();
			// a normal exit, including a headless command that returns early, is a clean start; an unhandled exception or a crash does not run this
			AppDomain.CurrentDomain.ProcessExit += ( _, _ ) => Clear();
		}

		public bool Cleared => cleared;

		public void Update()
		{
			if ( !cleared && clock() - started >= CleanStartSeconds )
				Clear();
		}

		public void Clear()
		{
			if ( cleared )
				return;
			cleared = true;
			Remove( path );
		}
	}
}
