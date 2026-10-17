using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenTPW.Online;

namespace OpenTPW.Mods;

/// <summary>
/// Safe mode for script mods (docs/SCRIPT-MODS.md): a run with script mods enabled writes a marker file in the configuration folder as soon as
/// the mod folders are known, before any script is read or compiled and long before the first frame, and removes it only after a clean start.
/// If the marker is still there at the next start and the process that wrote it is gone, the previous run did not get through its start-up
/// (a crash, a hang, or an exception), so that run starts with every script mod off, without reading, hashing or compiling its scripts, and says so.
/// <c>--safe-mode</c> does the same on demand. The marker is removed as soon as it has been seen, so only the one run is in safe mode; switching
/// the mod off on the mods screen is the lasting fix. The browser has no marker and no safe mode (no community mods yet).
/// </summary>
/// <remarks>
/// The marker is cleared only on success: after <see cref="CleanStartSeconds"/> of running frames, or by <see cref="CleanShutdown"/> when
/// <c>Game.Run</c> returned normally. An exception that <c>Program.Main</c> catches is a failed start and leaves the marker; nothing clears it
/// from a process-exit handler. The marker holds the writer's process id and start time, so a second instance that starts while the first is
/// still running neither treats the first one's marker as a crash nor deletes it.
/// </remarks>
// [EXT:script-mods] OpenTPW's own scripted mods; the original game had no mods
public static class ScriptSafeMode
{
	public const string Option = "--safe-mode";
	public const string MarkerFileName = "script-mods-starting.marker";
	/// <summary>Seconds of running frames, counted from the first frame, after which a start counts as clean and the marker is removed.</summary>
	public const double CleanStartSeconds = 15;

	/// <summary>Whether this run has script mods switched off.</summary>
	public static bool Active { get; private set; }
	/// <summary>Why script mods are off in this run (an English line for the log), or null when there is nothing to tell.</summary>
	public static string? Reason { get; private set; }
	/// <summary>The marker this run wrote, or null.</summary>
	public static StartMarker? Current { get; private set; }

	public static string MarkerPath( string configDirectory ) => Path.Combine( configDirectory, MarkerFileName );

	/// <summary>
	/// Decides at startup, once the enabled mods are known: <c>--safe-mode</c>, or a marker left by an unfinished start (its process is gone), switches
	/// script mods off for this run. A stale marker is always deleted, but it is reported (<see cref="Reason"/>) only when an enabled mod has
	/// scripts, so a leftover marker of a mod that is gone costs nothing. A marker whose process is still running belongs to another instance and is left
	/// alone. Smoke tests neither read nor write the marker but honour <c>--safe-mode</c>.
	/// </summary>
	public static bool Enter( string[] args, string configDirectory, bool smokeTest, bool scriptModsEnabled = true, Func<int, DateTime?>? processStart = null )
	{
		var flag = args.Contains( Option );
		var marker = MarkerPath( configDirectory );
		var crashed = false;
		if ( !smokeTest && File.Exists( marker ) )
		{
			if ( !IsLive( marker, processStart ?? ProcessStart ) )
			{
				crashed = true;
				Remove( marker );
			}
		}
		Active = flag || (crashed && scriptModsEnabled);
		Reason = !scriptModsEnabled ? null
			: crashed ? "The last start with script mods did not finish; script mods are off for this run (safe mode). Switch the mod off on the Extensions and mods screen."
			: flag ? $"{Option}: script mods are off for this run." : null;
		return Active;
	}

	/// <summary>
	/// Writes the marker (process id and start time) when script mods are enabled for this run, and keeps it as <see cref="Current"/>. Writes nothing
	/// in safe mode, in smoke tests, or when another running instance's marker exists. Call it before any script is read.
	/// </summary>
	public static StartMarker? Begin( bool scriptModsEnabled, string configDirectory, bool smokeTest, Func<double>? clock = null )
	{
		Current = null;
		if ( smokeTest || Active || !scriptModsEnabled )
			return null;
		var path = MarkerPath( configDirectory );
		var content = Content();
		try
		{
			if ( File.Exists( path ) )
				return null;
			Directory.CreateDirectory( configDirectory );
			AtomicFile.Write( path, Encoding.UTF8.GetBytes( content ) );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return null;
		}
		return Current = new StartMarker( path, content, clock );
	}

	/// <summary>The game returned normally from its run: a clean start (a failed one threw, and left the marker).</summary>
	public static void CleanShutdown() => Current?.Clear();

	private static string Content()
	{
		using var self = Process.GetCurrentProcess();
		var started = ProcessStart( self.Id ) ?? DateTime.UtcNow;
		return string.Create( CultureInfo.InvariantCulture, $"script mods are starting\npid {self.Id}\nstarted {started.Ticks}\n" );
	}

	private static DateTime? ProcessStart( int pid )
	{
		try
		{
			using var process = Process.GetProcessById( pid );
			return process.StartTime.ToUniversalTime();
		}
		catch ( Exception )
		{
			return null;
		}
	}

	/// <summary>Whether the process that wrote the marker is still running (same id and start time within two seconds).</summary>
	private static bool IsLive( string marker, Func<int, DateTime?> processStart )
	{
		try
		{
			long pid = -1, ticks = -1;
			foreach ( var line in File.ReadAllLines( marker ) )
			{
				if ( line.StartsWith( "pid ", StringComparison.Ordinal ) )
					long.TryParse( line.AsSpan( 4 ), NumberStyles.None, CultureInfo.InvariantCulture, out pid );
				else if ( line.StartsWith( "started ", StringComparison.Ordinal ) )
					long.TryParse( line.AsSpan( 8 ), NumberStyles.None, CultureInfo.InvariantCulture, out ticks );
			}
			if ( pid <= 0 || pid > int.MaxValue || ticks < 0 || ticks > DateTime.MaxValue.Ticks )
				return false;
			return processStart( (int)pid ) is { } start && Math.Abs( (start - new DateTime( ticks, DateTimeKind.Utc )).TotalSeconds ) < 2;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return false;
		}
	}

	internal static void Remove( string marker )
	{
		try { File.Delete( marker ); }
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException ) { }
	}

	/// <summary>The marker of a start in progress. <see cref="Update"/> runs once per frame; <see cref="Clear"/> is idempotent and removes only this run's own marker.</summary>
	public sealed class StartMarker
	{
		private readonly string path;
		private readonly string content;
		private readonly Func<double> clock;
		private double? firstFrame;

		internal StartMarker( string path, string content, Func<double>? clock )
		{
			this.path = path;
			this.content = content;
			var watch = Stopwatch.StartNew();
			this.clock = clock ?? ( () => watch.Elapsed.TotalSeconds );
		}

		public bool Cleared { get; private set; }

		/// <summary>Counts from the first frame: loading before it does not count towards a clean start.</summary>
		public void Update()
		{
			if ( Cleared )
				return;
			firstFrame ??= clock();
			if ( clock() - firstFrame.Value >= CleanStartSeconds )
				Clear();
		}

		public void Clear()
		{
			if ( Cleared )
				return;
			Cleared = true;
			try
			{
				// only our own marker: another instance may have written one since
				if ( File.Exists( path ) && File.ReadAllText( path ) == content )
					File.Delete( path );
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException ) { }
		}
	}
}
