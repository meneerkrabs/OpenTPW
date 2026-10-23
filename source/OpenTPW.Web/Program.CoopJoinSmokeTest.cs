using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-snapshot-join] The co-op join smoke's phases in the page (docs/COOP.md, "The browser's join" and "The browser's host"; tools/web-coop-join.mjs). The join phase:
/// the harness copies a join recorded on the desktop (the native smoke's host phase) into <c>/coop-smoke</c>, starts the join from the front end and reads the outcome and the
/// evidence back. The host phase (C2c-2b-4): the harness has the page copy the host's level, starts the phase from the front end and reads the recording it writes into
/// <c>/coop-smoke</c> back, for a desktop join phase to replay. Only in the page's co-op smoke mode (<c>?coopSmoke=1</c> on a loopback address), which unlike the performance mode
/// changes no park outside the commands; one run a page, since the builds it measures must be the runtime's first.
/// </summary>
public static partial class Program
{
	private const string CoopSmokeDirectory = "/coop-smoke";
	/// <summary>The evidence's limits in memory: a recorded join is about 1.5 MB (the host's final save the largest file).</summary>
	private const int CoopSmokeFileBytes = 16 << 20;
	private const long CoopSmokeTotalBytes = 64L << 20;

	private static bool coopSmokeMode;
	private static long coopSmokeBytes;
	private static CoopJoinSmokeTest? coopSmoke;
	private static string coopSmokeState = "idle";
	private static string? coopSmokeMessage;
	/// <summary>The summary of a run that completed in the current frame: published as its pass once the whole frame has returned.</summary>
	private static string? coopSmokeCompleted;
	private static int coopSmokeFrames;
	private static string? coopSmokePosition;

	/// <summary>The page's co-op smoke mode, chosen before the game starts: the smoke's exports work only in it.</summary>
	[JSExport]
	public static void CoopSmokeMode()
	{
		if ( renderer != null || flow != null )
			throw new InvalidOperationException( "The co-op smoke mode is chosen before the game starts." );
		// the performance mode's hooks change parks outside the commands, which a shared park refuses
		if ( CoopSession.ChangesParkOutsideCommands )
			throw new InvalidOperationException( "The co-op smoke does not run in the performance mode." );
		// the page's own address, which no script can change, not the word of the script that calls this
		using var location = JSHost.GlobalThis.GetPropertyAsJSObject( "location" );
		if ( !CoopJoinSmokeTest.IsCoopSmokePageAddress( location?.GetPropertyAsString( "href" ) ) )
			throw new InvalidOperationException( "The co-op smoke mode is only for a page on a loopback address opened with ?coopSmoke=1." );
		coopSmokeMode = true;
	}

	/// <summary>Stores one file of the recorded join (a flat name) in the smoke's folder, which the page does not keep; before the run starts.</summary>
	[JSExport]
	public static void CoopSmokeAddFile( string name, byte[] contents )
	{
		RequireCoopSmokeMode();
		if ( coopSmokeState != "idle" )
			throw new InvalidOperationException( "The recorded join is copied before the co-op join smoke starts." );
		if ( !CoopJoinSmokeTest.IsEvidenceFileName( name ) )
			throw new ArgumentException( $"'{name}' is not a file name of the recorded join." );
		var path = Path.Combine( CoopSmokeDirectory, name );
		var replaced = File.Exists( path ) ? new FileInfo( path ).Length : 0;
		if ( contents.Length > CoopSmokeFileBytes || coopSmokeBytes - replaced + contents.Length > CoopSmokeTotalBytes )
			throw new ArgumentException( $"The recorded join's files are limited to {CoopSmokeFileBytes >> 20} MiB each and {CoopSmokeTotalBytes >> 20} MiB together." );
		Directory.CreateDirectory( CoopSmokeDirectory );
		File.WriteAllBytes( path, contents );
		coopSmokeBytes += contents.Length - replaced;
	}

	/// <summary>Whether the host phase's level is in the page, asking the page to copy it the first time: the phase's level start reads its files at once.</summary>
	[JSExport]
	public static bool CoopSmokeLevelReady()
	{
		RequireCoopSmokeMode();
		return GameFlow.LevelDataReady?.Invoke( CoopJoinSmokeTest.HostLevel ) != false;
	}

	/// <summary>
	/// Starts a phase from the front end: the join phase (<c>join</c>, the adversarial one, or <c>measure</c>) over the recorded join copied in, or the host phase (<c>host</c>), which
	/// records a join for a desktop join phase once its level is in the page (<see cref="CoopSmokeLevelReady"/>).
	/// </summary>
	[JSExport]
	public static void CoopJoinSmokeStart( string mode )
	{
		RequireCoopSmokeMode();
		var (hosting, measuring) = mode switch
		{
			"join" => (false, false),
			"measure" => (false, true),
			"host" => (true, false),
			_ => throw new ArgumentException( $"The co-op join smoke runs 'join', 'measure' or 'host', not '{mode}'." )
		};
		if ( coopSmokeState != "idle" )
			throw new InvalidOperationException( "One co-op join smoke a page: its builds must be the runtime's first. Load the page again for another." );
		if ( renderer == null || flow == null || flow.Level != null || flow.Menu == null )
			throw new InvalidOperationException( "The co-op join smoke starts from the front end." );
		if ( hosting && GameFlow.LevelDataReady?.Invoke( CoopJoinSmokeTest.HostLevel ) == false )
			throw new InvalidOperationException( "The host phase starts once its level is in the page (CoopSmokeLevelReady)." );
		coopSmokeState = "running";
		try
		{
			coopSmoke = hosting
				? CoopJoinSmokeTest.StartPageHost( flow, CoopSmokeDirectory,
					completedBy: summary => coopSmokeCompleted = summary,
					failedBy: line => (coopSmokeState, coopSmokeMessage) = ("failed", line) )
				: CoopJoinSmokeTest.StartPageJoin( flow, CoopSmokeDirectory, measuring,
					completedBy: summary => coopSmokeCompleted = summary,
					failedBy: line => (coopSmokeState, coopSmokeMessage) = ("failed", line) );
		}
		catch ( Exception error )
		{
			(coopSmokeState, coopSmokeMessage) = ("failed", $"The co-op join smoke could not start: {error.Message}");
			throw;
		}
		coopSmoke.Attach( renderer );
	}

	/// <summary>
	/// Where the co-op join smoke stands, as JSON: its state (<c>idle</c>, <c>running</c>, <c>passed</c>, <c>failed</c>), its frames and step, its summary or failure, and whether the
	/// pools extension's sprite sets are still on their way (the harness starts the run once they are not, so no fetch overlaps the frames it measures).
	/// </summary>
	[JSExport]
	public static string CoopJoinSmokeStatus()
	{
		var pending = PoolSpriteDelivery.IsPending( PoolSpriteDelivery.SwimGroup ) || PoolSpriteDelivery.IsPending( PoolSpriteDelivery.LandGroup );
		return new JsonObject
		{
			["mode"] = coopSmokeMode,
			["started"] = flow != null,
			["frontEnd"] = flow is { Level: null, Menu: not null },
			["state"] = coopSmokeState,
			["frames"] = coopSmoke?.Frames ?? coopSmokeFrames,
			["step"] = coopSmoke?.Position ?? coopSmokePosition,
			["message"] = coopSmokeMessage,
			["poolSprites"] = pending ? "pending" : "settled",
		}.ToJsonString();
	}

	private static void RequireCoopSmokeMode()
	{
		if ( !coopSmokeMode )
			throw new InvalidOperationException( "The co-op join smoke runs only in the page's co-op smoke mode (?coopSmoke=1 on a loopback address)." );
	}

	/// <summary>
	/// A frame while the smoke runs: the whole call timed for the smoke's frame, a throw out of it the smoke's failure (the page then stops its loop), and once the smoke has ended its
	/// handlers taken off after the frame, the game running on.
	/// </summary>
	private static bool CoopSmokeFrame( Renderer running, CoopJoinSmokeTest smoke )
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			return running.Frame();
		}
		catch ( Exception error )
		{
			smoke.PageFailed( error );
			throw;
		}
		finally
		{
			smoke.PageFrameEnded( Stopwatch.GetElapsedTime( started ).TotalMilliseconds );
			if ( smoke.Ended )
			{
				// the evidence written again with the last frame's time; a pass only once the frame that completed the run returned whole (a throw after the completion made it a
				// failure above) and its evidence is written
				var unwritten = smoke.PageEnded();
				if ( coopSmokeState == "running" && coopSmokeCompleted != null )
					(coopSmokeState, coopSmokeMessage) = unwritten == null ? ("passed", coopSmokeCompleted) : ("failed", $"The run's evidence could not be written: {unwritten}");
				(coopSmokeFrames, coopSmokePosition) = (smoke.Frames, smoke.Position);
				smoke.Detach( running );
				smoke.Dispose();
				coopSmoke = null;
			}
		}
	}
}
