using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-session] The live co-op acceptance's page (C2d-5b; docs/COOP.md, "The live acceptance"; tools/coop-live.mjs). In the page's live mode
/// (<c>?coopLive=1</c> on a loopback address) the game starts as a player's does, without automatic advice. Once the front end shows, the driver starts the run with the
/// player's credentials as the call's arguments (never in the address or the page's stores). The run's status and evidence live in the page's in-memory folder; the driver
/// relays them out, and the other game's status and its own in, through <see cref="CoopLiveRead"/> and <see cref="CoopLiveWrite"/>. One run a page.
/// </summary>
public static partial class Program
{
	/// <summary>The run's folder in the page's file system, never the player's.</summary>
	private const string CoopLiveDirectory = "/coop-live";
	/// <summary>The most a status written into the page may hold.</summary>
	private const int CoopLiveStatusCharacters = 1 << 20;

	private static bool coopLiveMode;
	private static CoopLiveTest? coopLive;
	private static string coopLiveRole = "";
	private static string coopLiveState = "idle";
	private static string? coopLiveMessage;
	/// <summary>The summary of a run that completed in the current frame: published as its pass once the whole frame has returned.</summary>
	private static string? coopLiveCompleted;
	private static JsonObject? coopLiveLast;
	private static System.Threading.Timer? coopLiveWatchdog;

	/// <summary>The page's live mode, chosen before the game starts: the acceptance's exports work only in it.</summary>
	[JSExport]
	public static void CoopLiveMode()
	{
		if ( renderer != null || flow != null || coopSmokeMode || coopLinkMode || CoopSession.ChangesParkOutsideCommands )
			throw new InvalidOperationException( "The live co-op acceptance's mode is chosen before the game or another harness mode starts." );
		// the page's own address, which no script can change, not the word of the script that calls this
		using var location = JSHost.GlobalThis.GetPropertyAsJSObject( "location" );
		if ( !CoopLiveTest.IsPageAddress( location?.GetPropertyAsString( "href" ) ) )
			throw new InvalidOperationException( "The live co-op acceptance is only for a page on a loopback address opened with ?coopLive=1." );
		coopLiveMode = true;
	}

	/// <summary>
	/// Starts the run from the front end: <paramref name="role"/> is <c>host</c> or <c>join</c>; the player logs in to the server that serves this page (its origin, which
	/// forwards the API); <paramref name="minutes"/> is the endurance scenario's, <paramref name="code"/> the guest's session code (empty for the host).
	/// </summary>
	[JSExport]
	public static void CoopLiveStart( string role, string name, string password, string scenario, string minutes, string code )
	{
		RequireCoopLiveMode();
		if ( coopLiveState != "idle" )
			throw new InvalidOperationException( "One live co-op acceptance a page: load the page again for another." );
		if ( renderer == null || flow == null || flow.Level != null || flow.Menu == null )
			throw new InvalidOperationException( "The live co-op acceptance starts from the front end." );
		using var location = JSHost.GlobalThis.GetPropertyAsJSObject( "location" );
		var origin = location?.GetPropertyAsString( "origin" );
		if ( string.IsNullOrEmpty( origin ) )
			throw new InvalidOperationException( "The page has no origin to log in to." );
		var environment = new Dictionary<string, string>
		{
			["OPENTPW_COOP_LIVE_SERVER"] = origin,
			["OPENTPW_COOP_LIVE_NAME"] = name,
			["OPENTPW_COOP_LIVE_PASSWORD"] = password,
			["OPENTPW_COOP_LIVE_SCENARIO"] = scenario,
			["OPENTPW_COOP_LIVE_MINUTES"] = minutes,
			["OPENTPW_COOP_LIVE_CODE"] = code,
		};
		coopLiveState = "running";
		try
		{
			coopLive = CoopLiveTest.StartPage( flow, role, CoopLiveDirectory, environment,
				completedBy: summary => coopLiveCompleted = summary,
				failedBy: line => (coopLiveState, coopLiveMessage) = ("failed", line) );
		}
		catch ( Exception error )
		{
			(coopLiveState, coopLiveMessage) = ("failed", $"The live co-op acceptance could not start: {error.Message}");
			throw;
		}
		coopLiveRole = coopLive.Role;
		coopLive.Attach( renderer );
		// a run whose frames stop fails, not hangs: the page's timers run between frames (the steps' own deadlines come first, and the driver keeps its own)
		var live = coopLive;
		coopLiveWatchdog = new System.Threading.Timer( _ => live.Fail( new TimeoutException( $"the run stopped after {live.MaximumMinutes:0} minutes at frame {live.Frames} ({live.Step ?? "between steps"})" ) ),
			null, TimeSpan.FromMinutes( live.MaximumMinutes ), System.Threading.Timeout.InfiniteTimeSpan );
	}

	/// <summary>
	/// Where the run stands, as JSON: the mode, whether the game started and shows its front end, the state (<c>idle</c>, <c>running</c>, <c>passed</c>, <c>failed</c>) with its
	/// summary or failure, and the run's role, step, frames and seconds.
	/// </summary>
	[JSExport]
	public static string CoopLiveState() => new JsonObject
	{
		["mode"] = coopLiveMode,
		["started"] = flow != null,
		["frontEnd"] = flow is { Level: null, Menu: not null },
		["state"] = coopLiveState,
		["message"] = coopLiveMessage,
		["run"] = (coopLive?.PageStatus() ?? coopLiveLast)?.DeepClone(),
	}.ToJsonString();

	/// <summary>A file of the run for the driver, as its JSON text (its own status and evidence only), or null while it has none.</summary>
	[JSExport]
	public static string? CoopLiveRead( string name )
	{
		RequireCoopLiveMode();
		if ( !CoopLiveTest.PageMayRead( coopLiveRole, name ) )
			throw new ArgumentException( $"'{name}' is not a file the driver reads from this page." );
		var path = Path.Combine( CoopLiveDirectory, name );
		return File.Exists( path ) ? File.ReadAllText( path ) : null;
	}

	/// <summary>Writes the other game's status or the driver's (JSON text) into the run's folder, whole, then renamed into place as the native games write theirs.</summary>
	[JSExport]
	public static void CoopLiveWrite( string name, string text )
	{
		RequireCoopLiveMode();
		if ( !CoopLiveTest.PageMayWrite( coopLiveRole, name ) )
			throw new ArgumentException( $"'{name}' is not a file the driver writes into this page." );
		if ( text.Length > CoopLiveStatusCharacters )
			throw new ArgumentException( $"A status written into the page is at most {CoopLiveStatusCharacters} characters." );
		Directory.CreateDirectory( CoopLiveDirectory );
		var path = Path.Combine( CoopLiveDirectory, name );
		File.WriteAllText( path + ".tmp", text );
		File.Move( path + ".tmp", path, overwrite: true );
	}

	private static void RequireCoopLiveMode()
	{
		if ( !coopLiveMode )
			throw new InvalidOperationException( "The live co-op acceptance runs only in the page's live mode (?coopLive=1 on a loopback address)." );
	}

	/// <summary>
	/// A frame while the run goes on: a throw out of it is the run's failure (the page then stops its loop), and once the run has ended its pass is published, only now that the
	/// frame that completed it has returned whole, and its frame taken off; the game runs on.
	/// </summary>
	private static bool CoopLiveFrame( Renderer running, CoopLiveTest live )
	{
		try
		{
			return running.Frame();
		}
		catch ( Exception error )
		{
			live.PageFailed( error );
			throw;
		}
		finally
		{
			if ( live.Done )
			{
				if ( coopLiveState == "running" && coopLiveCompleted != null && live.Completed )
					(coopLiveState, coopLiveMessage) = ("passed", coopLiveCompleted);
				coopLiveLast = live.PageStatus();
				coopLiveWatchdog?.Dispose();
				coopLiveWatchdog = null;
				live.Detach( running );
				live.Dispose();
				coopLive = null;
			}
		}
	}
}
