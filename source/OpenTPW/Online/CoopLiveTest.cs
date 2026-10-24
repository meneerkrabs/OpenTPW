using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>A condition the live acceptance waits for, with its own deadline in seconds of wall time (<see cref="CoopLiveTest"/>).</summary>
internal sealed record CoopLiveWait( string What, Func<bool> Done, double Seconds );

/// <summary>A step of the live acceptance as its evidence keeps it: what it waited for, the frame it ended at, and when it began and ended (seconds of wall time).</summary>
internal sealed record CoopLiveStep( string What, int Frame, double Started, double Ended );

/// <summary>
/// [EXT:online-coop-session] The live co-op acceptance (C2d-5; docs/COOP.md, "The live acceptance"): one game of a live shared park, the host or the guest, through the real server and the
/// game's own screens, with the original game's data. <c>--coop-live host|join --coop-live-evidence &lt;folder&gt;</c>; the driver (tools/coop-live.mjs) gives the server, the player's name
/// and password, and the guest's code in the environment (<c>OPENTPW_COOP_LIVE_SERVER</c>, <c>_NAME</c>, <c>_PASSWORD</c>, <c>_CODE</c>), never in arguments or files, and the
/// scenario (<c>OPENTPW_COOP_LIVE_SCENARIO</c>: <c>baseline</c>, <c>recovery</c> or <c>endurance</c>, with <c>OPENTPW_COOP_LIVE_MINUTES</c>).
/// <para>
/// The run is a script of steps (<see cref="HostSteps"/>, <see cref="GuestSteps"/>), each waiting for a condition with a deadline of wall time. It acts on the top screen only, as a
/// player does: an element is found by its id, of its type, visible and enabled, and a button is activated and a field typed into as the screen's own input would. Keys, the chat
/// line and the park's clicks go through the game's input. The two games and the driver meet at barriers: each publishes its status (<c>host-status.json</c>,
/// <c>guest-status.json</c>; the driver's <c>driver-status.json</c>) in the evidence folder and waits on the others'. Both games run their frames as the window gives them; wall time,
/// frames and the park's ticks are kept.
/// </para>
/// </summary>
internal sealed partial class CoopLiveTest : IDisposable
{
	/// <summary>A run that stops getting frames fails, not hangs (the steps' own deadlines come first).</summary>
	private const double MaximumSeconds = 3600;
	/// <summary>The other parties' status is read at most this often, in seconds.</summary>
	private const double StatusReadSeconds = 0.25;
	/// <summary>The driver's status file is <c>driver-status.json</c>.</summary>
	internal const string DriverRole = "driver";

	private readonly GameFlow flow;
	private readonly string evidenceDirectory;
	private readonly bool hosting;
	private readonly string server;
	private readonly string playerName;
	private readonly string password;
	private readonly string? code;
	/// <summary>The scenario: the baseline, a server restart in the session (recovery), or minutes at the fastest speed (endurance).</summary>
	private readonly string scenario;
	/// <summary>Minutes of wall time the endurance scenario shares the park at the fastest speed.</summary>
	private readonly double enduranceMinutes;
	private readonly Action<string> completedBy;
	private readonly Action<string> failedBy;
	private readonly Stopwatch wall = Stopwatch.StartNew();
	private readonly IEnumerator<CoopLiveWait> steps;
	private readonly List<CoopLiveStep> log = new();
	private readonly JsonObject status = new();
	private readonly JsonObject evidence = new();
	private readonly Dictionary<string, (JsonObject? Status, double ReadAt)> statuses = new();
	private CoopLiveWait? waiting;
	private double waitingSince;
	private int frame;
	private bool done;
	private bool completed;
	private string? failure;
	/// <summary>Input for the game's next frames (a key, a typed line, a click), one a frame, handed to the flow just before its update.</summary>
	private readonly Queue<UiInput> inputs = new();
	/// <summary>Keys pressed for one frame through the keyboard (<see cref="Input.InjectKeyDown"/>), released when the run ends if no frame took them.</summary>
	private readonly List<IDisposable> keys = new();

	internal CoopLiveTest( GameFlow flow, string evidenceDirectory, bool hosting, Func<string, string?> environment, Action<string> completedBy, Action<string> failedBy,
		Func<CoopLiveTest, IEnumerable<CoopLiveWait>>? script = null )
	{
		this.flow = flow;
		this.evidenceDirectory = evidenceDirectory;
		this.hosting = hosting;
		this.completedBy = completedBy;
		this.failedBy = failedBy;
		string Required( string name ) => environment( name ) is { Length: > 0 } value ? value : throw new ArgumentException( $"--coop-live needs {name} in the environment." );
		server = Required( "OPENTPW_COOP_LIVE_SERVER" );
		playerName = Required( "OPENTPW_COOP_LIVE_NAME" );
		password = Required( "OPENTPW_COOP_LIVE_PASSWORD" );
		code = hosting ? null : Required( "OPENTPW_COOP_LIVE_CODE" );
		scenario = environment( "OPENTPW_COOP_LIVE_SCENARIO" ) is { Length: > 0 } chosen ? chosen : "baseline";
		if ( scenario is not ("baseline" or "recovery" or "endurance") )
			throw new ArgumentException( $"OPENTPW_COOP_LIVE_SCENARIO is 'baseline', 'recovery' or 'endurance', not '{scenario}'." );
		enduranceMinutes = scenario != "endurance" ? 0
			: double.TryParse( environment( "OPENTPW_COOP_LIVE_MINUTES" ), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes )
				&& minutes > 0 && double.IsFinite( minutes ) ? minutes
			: throw new ArgumentException( "The endurance scenario needs OPENTPW_COOP_LIVE_MINUTES, a positive number of minutes." );
		steps = (script?.Invoke( this ) ?? (hosting ? HostSteps() : GuestSteps())).GetEnumerator();
		status["role"] = Role;
		evidence["role"] = Role;
		evidence["scenario"] = scenario;
		evidence["player"] = playerName;
		evidence["enduranceMinutes"] = enduranceMinutes;
	}

	internal string Role => hosting ? "host" : "guest";
	private string OtherRole => hosting ? "guest" : "host";
	/// <summary>Whether the run is over (completed or failed).</summary>
	internal bool Done => done;
	/// <summary>Whether the run completed every step.</summary>
	internal bool Completed => completed;
	/// <summary>Why the run failed, or null.</summary>
	internal string? Failure => failure;

	/// <summary>Root's CLI hook: <c>--coop-live host|join</c> with <c>--coop-live-evidence</c>.</summary>
	internal static void Run( GameFlow flow, string role, string evidenceDirectory )
	{
		ArgumentNullException.ThrowIfNull( flow );
		ArgumentException.ThrowIfNullOrWhiteSpace( evidenceDirectory );
		var hosting = role switch
		{
			"host" => true,
			"join" => false,
			_ => throw new ArgumentException( $"--coop-live expects 'host' or 'join', not '{role}'." )
		};
		if ( !FeatureGates.Online )
			throw new InvalidOperationException( $"--coop-live needs the online extension: {FeatureGates.OnlineDisabledMessage}." );
		Directory.CreateDirectory( evidenceDirectory );
		using var test = new CoopLiveTest( flow, Path.GetFullPath( evidenceDirectory ), hosting, Environment.GetEnvironmentVariable,
			completedBy: _ => Render.Window.SdlWindow.Close(), failedBy: _ => Environment.Exit( 1 ) );
		using var watchdog = new System.Threading.Timer( _ =>
		{
			test.Fail( new TimeoutException( $"the run stopped after {MaximumSeconds} s at frame {test.frame} ({test.waiting?.What ?? "between steps"})" ) );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( MaximumSeconds + test.enduranceMinutes * 60 ), System.Threading.Timeout.InfiniteTimeSpan );
		Render.CaptureOutput = true;
		// the script's step first, then the game's own frame with the input the step gave it
		Render.OnUpdate += test.Update;
		Render.OnUpdate += flow.Update;
		Render.OnRender += flow.Render;
		Render.Run();
		Render.OnUpdate -= test.Update;
		Render.OnUpdate -= flow.Update;
		Render.OnRender -= flow.Render;
		if ( !test.completed )
			throw new InvalidOperationException( $"The live co-op acceptance's {test.Role} did not complete: {test.failure ?? "the window closed"}." );
	}

	/// <summary>One frame of the script: the step's condition (the next step once it holds, a failure once its deadline passed), then the input for the game's frame.</summary>
	internal void Update()
	{
		if ( done )
			return;
		frame++;
		try
		{
			Observe();
			while ( !done && (waiting == null || waiting.Done()) )
			{
				if ( waiting != null )
					log.Add( new CoopLiveStep( waiting.What, frame, Math.Round( waitingSince, 3 ), Math.Round( Now, 3 ) ) );
				waiting = null;
				if ( !steps.MoveNext() )
				{
					Complete();
					return;
				}
				waiting = steps.Current;
				waitingSince = Now;
				Publish( "step", waiting.What );
			}
			if ( !done && waiting != null && Now - waitingSince > waiting.Seconds )
				throw new TimeoutException( $"{waiting.What} did not happen within {waiting.Seconds} s" );
			if ( inputs.TryDequeue( out var input ) )
				flow.InjectedInput = input;
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Fail( error );
		}
	}

	private double Now => wall.Elapsed.TotalSeconds;

	internal static CoopLiveWait Wait( string what, Func<bool> done, double seconds ) => new( what, done, seconds );

	/// <summary>A wait of <paramref name="seconds"/> of wall time (a figure's walk, the frames a check needs).</summary>
	internal CoopLiveWait Pause( string what, double seconds )
	{
		var until = Now + seconds;
		return Wait( what, () => Now >= until, seconds + 30 );
	}

	/// <summary>Input queued for the game's next frames, one a frame, as the window's input would give it.</summary>
	private void Give( params UiInput[] given )
	{
		foreach ( var input in given )
			inputs.Enqueue( input );
	}

	/// <summary>Whether the input queued so far reached the game.</summary>
	private bool InputGiven => inputs.Count == 0;

	/// <summary>A key pressed and released through the keyboard (the frame after this one reads it held, the next released), as a player taps it.</summary>
	private void Tap( Veldrid.Key key ) => keys.Add( Input.InjectKeyDown( key ) );

	// ---- the screens -------------------------------------------------------------------------

	private UiScreenStack? Stack => flow.Hud?.Stack ?? flow.Menu?.Stack;

	private bool Top( string screen ) => Stack?.Top?.Name == screen;

	/// <summary>The element <paramref name="id"/> of the top screen <paramref name="screen"/>, of its type, visible and enabled, as a player could use it.</summary>
	private T Element<T>( string screen, string id ) where T : UiElement
	{
		var top = Stack?.Top;
		Require( top?.Name == screen, $"the top screen is {screen} (it is {top?.Name ?? "none"})" );
		var element = top!.Find( id );
		Require( element is T, $"{screen} has a {typeof( T ).Name} {id}" );
		Require( element!.Visible && element.Enabled, $"{screen}'s {id} is visible and enabled" );
		return (T)element;
	}

	/// <summary>Whether the top screen <paramref name="screen"/> shows <paramref name="id"/>, of its type, visible and enabled.</summary>
	private bool Usable<T>( string screen, string id ) where T : UiElement => Stack?.Top is { } top && top.Name == screen && top.Find( id ) is T { Visible: true, Enabled: true };

	/// <summary>Activates a button of the top screen, as its click or its key does.</summary>
	private void Press( string screen, string id ) => Element<UiButton>( screen, id ).Activate();

	/// <summary>Activates a line of the top screen's menu, as its click or its key does.</summary>
	private void Choose( string screen, string id ) => Element<UiMenuItem>( screen, id ).Activate();

	/// <summary>Types <paramref name="text"/> into a field of the top screen in place of what it holds, as the keyboard does.</summary>
	private void TypeInto( string screen, string id, string text )
	{
		var field = Element<UiTextField>( screen, id );
		field.Type( "", field.Text.Length );
		field.Type( text, 0 );
		Require( field.Text == text, $"{screen}'s {id} took the text typed" );
	}

	/// <summary>The text a label of the top screen shows.</summary>
	private string LabelText( string screen, string id ) => Element<UiLabel>( screen, id ).Text();

	/// <summary>
	/// The online login as a player gives it, from a screen with a Log in button (<paramref name="screen"/>): the server, the name and the password typed into the login screen, then
	/// Log in; the login screen closes itself once logged in.
	/// </summary>
	private IEnumerable<CoopLiveWait> LogIn( string screen )
	{
		Press( screen, "login" );
		yield return Wait( "the login screen", () => Top( "onlineLogin" ), 30 );
		TypeInto( "onlineLogin", "server", server );
		TypeInto( "onlineLogin", "name", playerName );
		TypeInto( "onlineLogin", "password", password );
		Press( "onlineLogin", "login" );
		yield return Wait( "the login", () => flow.Online.IsLoggedIn && Top( screen ), 90 );
		Require( flow.Online.Settings.PlayerName == playerName, $"logged in as {playerName}" );
		WatchLinks();
	}

	/// <summary>The shared parks' link as this game saw it: its states with when they came, and its session resumes (a lost socket, a server's restart).</summary>
	private readonly JsonArray linkStates = new();
	private int linkResumes;
	private CoopLink? watchedLink;

	private void WatchLinks()
	{
		if ( watchedLink != null )
			return;
		watchedLink = flow.Online.Coop;
		watchedLink.StateChanged += OnLinkState;
		watchedLink.Resumed += OnLinkResumed;
	}

	private void OnLinkState( CoopLinkState state ) => linkStates.Add( new JsonObject { ["state"] = state.ToString(), ["at"] = Math.Round( Now, 3 ) } );

	private void OnLinkResumed( Online.Coop.CoopSessionState state ) => linkResumes++;

	/// <summary>How often the chat connection came back after a loss, as its own notice says it in the chat lines.</summary>
	private int ChatReconnects => watchedLink == null ? 0 : flow.Online.ChatLines.Count( line => line == OnlineStrings.Get( OnlineLabel.ChatReconnected ) );

	private JsonObject LinkEvidence() => new()
	{
		["states"] = linkStates.DeepClone(),
		["resumes"] = linkResumes,
		["chatReconnects"] = ChatReconnects,
	};

	// ---- the other parties ---------------------------------------------------------------------

	/// <summary>Publishes a value of this game's status for the other game and the driver (written whole, then renamed into place).</summary>
	internal void Publish( string key, JsonNode? value )
	{
		status[key] = value?.DeepClone();
		status["frame"] = frame;
		status["at"] = Math.Round( Now, 3 );
		WriteJson( $"{Role}-status.json", status );
	}

	/// <summary>A value of <paramref name="role"/>'s status, or null before it says it (each status file is read at most every <see cref="StatusReadSeconds"/>).</summary>
	internal JsonNode? StatusOf( string role, string key )
	{
		if ( !statuses.TryGetValue( role, out var known ) || Now - known.ReadAt >= StatusReadSeconds )
		{
			var path = Path.Combine( evidenceDirectory, $"{role}-status.json" );
			try
			{
				known = (File.Exists( path ) ? JsonNode.Parse( File.ReadAllText( path ) ) as JsonObject : known.Status, Now);
			}
			catch ( Exception error ) when ( error is IOException or JsonException or UnauthorizedAccessException )
			{
				// a status being renamed into place is read again next time
				known = (known.Status, Now);
			}
			statuses[role] = known;
		}
		return known.Status?[key];
	}

	/// <summary>A value of the other game's status, or null before it says it.</summary>
	private JsonNode? Other( string key ) => StatusOf( OtherRole, key );

	private bool OtherSays( string key ) => Other( key ) is JsonValue value && value.TryGetValue<bool>( out var said ) && said;

	/// <summary>The other game failed: the run fails now, not at its own deadline.</summary>
	private void OtherFailed()
	{
		if ( Other( "failed" ) is JsonValue failed )
			throw new InvalidOperationException( $"the {OtherRole} failed: {failed}" );
	}

	private void WriteJson( string name, JsonNode node )
	{
		var path = Path.Combine( evidenceDirectory, name );
		var temporary = path + $".{Environment.ProcessId}.tmp";
		File.WriteAllText( temporary, node.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
		File.Move( temporary, path, overwrite: true );
	}

	// ---- evidence ------------------------------------------------------------------------------

	/// <summary>
	/// The session as this game saw it frame by frame: its phases (<c>host hosting</c>, <c>guest following</c>, <c>guest resyncing</c>, <c>guest ended</c>…) and the guest's
	/// streams (a resync starts a new one). The session is watched past its end, which takes it from the park.
	/// </summary>
	private readonly List<string> phases = new();
	private CoopSession? watched;
	private CoopGuestStream? lastStream;
	private int streams;

	private void Observe()
	{
		OtherFailed();
		if ( StatusOf( DriverRole, "abort" ) is JsonValue abort )
			throw new InvalidOperationException( $"the driver stopped the run: {abort}" );
		if ( flow.Level?.CoopSession is { } current )
			watched = current;
		if ( watched is not { } session )
			return;
		var role = session.Role == CoopRole.Host ? "host" : "guest";
		var phase = session.EndedWith != null ? $"{role} ended" : session.Role == CoopRole.Guest ? $"guest {session.Phase.ToString().ToLowerInvariant()}" : "host hosting";
		if ( phases.Count == 0 || phases[^1] != phase )
		{
			phases.Add( phase );
			if ( session.EndedWith != null )
				evidence["endCause"] = session.EndCause.ToString();
		}
		if ( session.GuestStream is { } stream && !ReferenceEquals( stream, lastStream ) )
		{
			lastStream = stream;
			streams++;
		}
	}

	/// <summary>Keeps <paramref name="value"/> in the evidence as <paramref name="key"/>.</summary>
	internal void Keep( string key, JsonNode? value ) => evidence[key] = value?.DeepClone();

	internal void Fail( Exception error )
	{
		if ( done )
			return;
		done = true;
		failure = $"{error.GetType().Name}: {error.Message}";
		ReleaseKeys();
		try
		{
			WriteEvidence( completed: false );
			Publish( "failed", failure );
		}
		catch ( Exception writeError ) when ( writeError is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Live co-op acceptance: writing the failure evidence failed too: {writeError.Message}" );
		}
		var line = $"Live co-op acceptance ({Role}) failed at {waiting?.What ?? "a step"}: {failure}";
		Console.Error.WriteLine( line );
		Log.Error( $"{line}\n{error.StackTrace}" );
		failedBy( line );
	}

	private void Complete()
	{
		done = completed = true;
		ReleaseKeys();
		WriteEvidence( completed: true );
		Publish( "completed", true );
		var line = $"Live co-op acceptance ({Role}) passed: {log.Count} steps in {Now:0.0} s, {frame} frames.";
		Log.Info( line );
		Console.WriteLine( line );
		completedBy( line );
	}

	private void WriteEvidence( bool completed )
	{
		evidence["completed"] = completed;
		evidence["failure"] = failure;
		evidence["environment"] = JsonSerializer.SerializeToNode( CoopSmokeEnvironment.Of( flow ) );
		evidence["frames"] = frame;
		evidence["seconds"] = Math.Round( Now, 3 );
		evidence["ticks"] = flow.Level?.TickCount;
		evidence["phases"] = new JsonArray( phases.Select( phase => (JsonNode?)phase ).ToArray() );
		evidence["streams"] = streams;
		evidence["steps"] = JsonSerializer.SerializeToNode( log );
		evidence["links"] = LinkEvidence();
		WriteJson( $"{Role}-evidence.json", evidence );
	}

	private void ReleaseKeys()
	{
		foreach ( var key in keys )
			key.Dispose();
		keys.Clear();
	}

	internal static void Require( bool condition, string what )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Live co-op acceptance: {what} failed." );
	}

	public void Dispose()
	{
		ReleaseKeys();
		steps.Dispose();
		if ( watchedLink != null )
		{
			watchedLink.StateChanged -= OnLinkState;
			watchedLink.Resumed -= OnLinkResumed;
		}
	}
}
