using System.Text.Json.Nodes;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-session] The live acceptance in the page (C2d-5b; docs/COOP.md, "The live acceptance"): the same script as natively, started by the page's export
/// (Program.CoopLive.cs) with the player's credentials in an environment of its own. Its status, evidence and the other parties' statuses live in the page's in-memory
/// folder, which the driver (tools/coop-live.mjs) relays to and from the run's evidence folder, so the barriers are the native run's. The page cannot read its output
/// back: the driver takes each picture. Completion closes no window: the page publishes the run's end once the frame that ended it has returned whole.
/// </summary>
internal sealed partial class CoopLiveTest
{
	/// <summary>The page's run: <paramref name="role"/> is <c>host</c> or <c>join</c>; <paramref name="environment"/> holds what the native run reads from its environment.</summary>
	internal static CoopLiveTest StartPage( GameFlow flow, string role, string evidenceDirectory, IReadOnlyDictionary<string, string> environment, Action<string> completedBy,
		Action<string> failedBy, Func<string, string?> levelCopyFailure )
	{
		ArgumentNullException.ThrowIfNull( flow );
		ArgumentNullException.ThrowIfNull( environment );
		var hosting = role switch
		{
			"host" => true,
			"join" => false,
			_ => throw new ArgumentException( $"The live co-op acceptance runs 'host' or 'join', not '{role}'." )
		};
		if ( !FeatureGates.Online )
			throw new InvalidOperationException( $"The live co-op acceptance needs the online extension: {FeatureGates.OnlineDisabledMessage}." );
		Directory.CreateDirectory( evidenceDirectory );
		return new CoopLiveTest( flow, evidenceDirectory, hosting, name => environment.TryGetValue( name, out var value ) ? value : null, completedBy, failedBy, page: true )
		{
			levelCopyFailure = levelCopyFailure,
		};
	}

	/// <summary>Why the page could not copy a park's files, or null (natively the files are there).</summary>
	private Func<string, string?>? levelCopyFailure;

	/// <summary>The script's frame before the game's own, as the native run orders them.</summary>
	internal void Attach( Renderer renderer )
	{
		renderer.OnUpdate -= flow.Update;
		renderer.OnUpdate += Update;
		renderer.OnUpdate += flow.Update;
	}

	/// <summary>Takes the script's frame off after a frame, never inside one; the game runs on.</summary>
	internal void Detach( Renderer renderer ) => renderer.OnUpdate -= Update;

	/// <summary>The minutes the run may take before the page fails it (the steps' own deadlines come first).</summary>
	internal double MaximumMinutes => MaximumSeconds / 60 + enduranceMinutes;

	/// <summary>The frames the run has had.</summary>
	internal int Frames => frame;

	/// <summary>The step the run waits on, or null.</summary>
	internal string? Step => waiting?.What;

	/// <summary>
	/// A throw out of the page's frame, the game's update or its drawing as much as the script's own: the run's failure, also when the script completed earlier in the same frame
	/// (its evidence and status are written again as a failure; the page publishes a pass only after the whole frame).
	/// </summary>
	internal void PageFailed( Exception error )
	{
		if ( completed )
		{
			(completed, done) = (false, false);
			status.Remove( "completed" );
		}
		Fail( error );
	}

	/// <summary>The status a page's game writes for the others, with this run's own name.</summary>
	internal static string StatusFile( string role ) => $"{role}-status.json";

	/// <summary>
	/// The files the driver may read from a page whose run is <paramref name="role"/> (<c>host</c> or <c>guest</c>): its own status and evidence. A page takes no pictures of
	/// its own (the driver does), so nothing else leaves it.
	/// </summary>
	internal static bool PageMayRead( string role, string name ) => role is "host" or "guest" && (name == StatusFile( role ) || name == $"{role}-evidence.json");

	/// <summary>The files the driver may write into a page whose run is <paramref name="role"/>: the other game's status and the driver's, never the page's own.</summary>
	internal static bool PageMayWrite( string role, string name ) => role switch
	{
		"host" => name == StatusFile( "guest" ) || name == StatusFile( DriverRole ),
		"guest" => name == StatusFile( "host" ) || name == StatusFile( DriverRole ),
		_ => false,
	};

	/// <summary>Whether <paramref name="href"/> is a page on a loopback address opened for the live acceptance (<c>?coopLive=1</c>, with none of the other harness modes).</summary>
	internal static bool IsPageAddress( string? href ) =>
		CoopJoinSmokeTest.LoopbackQuery( href ) is { } first && first( "coopLive" ) == "1" && first( "perf" ) != "1" && first( "coopSmoke" ) != "1" && first( "coopLinkSmoke" ) != "1";

	// ---- the page's input through the canvas ------------------------------------------------------

	/// <summary>The inputs this page's run asked the driver for, numbered from 1.</summary>
	private int pageInputs;

	/// <summary>
	/// Input the driver gives the page through the browser's own events on the canvas (C2d-5b): <paramref name="input"/> holds <c>keys</c> (key names, each pressed and
	/// released), <c>text</c> (typed a character at a time), <c>click</c> or <c>move</c> (a point in the game's canvas units, with the canvas's size, which the driver maps onto
	/// the page's canvas). The run publishes it numbered and waits until the driver's status says it was dispatched; the game's frames then read it as a player's.
	/// </summary>
	private IEnumerable<CoopLiveWait> PageInput( JsonObject input, string what )
	{
		var id = ++pageInputs;
		input["id"] = id;
		var canvas = flow.Context.Canvas;
		input["canvas"] = new JsonArray( canvas.Width, canvas.Height );
		Publish( "input", input );
		yield return Wait( $"the driver's {what} through the page's canvas", () => StatusOf( DriverRole, "inputs" )?[Role] is JsonValue done
			&& done.TryGetValue<int>( out var last ) && last >= id, 30 );
	}

	/// <summary>A key: natively the game's input, in the page through the canvas (<paramref name="domKey"/> is the browser's name for it).</summary>
	private IEnumerable<CoopLiveWait> KeyThrough( UiKeys key, string domKey )
	{
		if ( page )
			return PageInput( new JsonObject { ["keys"] = new JsonArray( domKey ) }, $"key {domKey}" );
		Give( UiInput.Key( key ) );
		return Array.Empty<CoopLiveWait>();
	}

	/// <summary>A line typed: natively the game's input, in the page a character at a time through the canvas.</summary>
	private IEnumerable<CoopLiveWait> TypeThrough( string line )
	{
		if ( page )
			return PageInput( new JsonObject { ["text"] = line }, "typed line" );
		Give( UiInput.Type( line ) );
		return Array.Empty<CoopLiveWait>();
	}

	/// <summary>A press and release at <paramref name="point"/>: natively the game's input, in the page the mouse through the canvas.</summary>
	private IEnumerable<CoopLiveWait> ClickThrough( System.Numerics.Vector2 point )
	{
		if ( page )
			return PageInput( new JsonObject { ["click"] = new JsonArray( point.X, point.Y ) }, "click" );
		Give( Press( point ), Release( point ) );
		return Array.Empty<CoopLiveWait>();
	}

	/// <summary>The pointer moved to <paramref name="point"/> with no button: natively the game's input, in the page the mouse through the canvas.</summary>
	private IEnumerable<CoopLiveWait> HoverThrough( System.Numerics.Vector2 point )
	{
		if ( page )
			return PageInput( new JsonObject { ["move"] = new JsonArray( point.X, point.Y ) }, "pointer move" );
		Give( UiInput.Idle( point ) );
		return Array.Empty<CoopLiveWait>();
	}

	/// <summary>A key tapped through the keyboard: natively the window's keyboard (<see cref="Tap"/>), in the page through the canvas.</summary>
	private IEnumerable<CoopLiveWait> TapThrough( Veldrid.Key key, string domKey )
	{
		if ( page )
			return PageInput( new JsonObject { ["keys"] = new JsonArray( domKey ) }, $"key {domKey}" );
		Tap( key );
		return Array.Empty<CoopLiveWait>();
	}

	/// <summary>The page's run state as JSON for the driver: the step, frames and seconds, and the session's phases so far.</summary>
	internal JsonObject PageStatus() => new()
	{
		["role"] = Role,
		["step"] = waiting?.What,
		["frames"] = frame,
		["seconds"] = Math.Round( Now, 3 ),
		["done"] = done,
		["completed"] = completed,
		["failure"] = failure,
	};
}
