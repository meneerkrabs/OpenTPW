using System.Text.Json.Nodes;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-session] What both games of the live acceptance do in the shared park (docs/COOP.md, "The live acceptance"): seeing the other player's figure and name in the view,
/// moving the view, laying a path line through the HUD, and saying a chat line through the chat mode's entry.
/// </summary>
internal sealed partial class CoopLiveTest
{
	/// <summary>A test line runs this many cells from its start to its end.</summary>
	private const int LineLength = 5;
	/// <summary>How near a figure stands to a place to be there, in cells on each axis.</summary>
	private const float NearCells = 1.5f;
	/// <summary>How far a view moves for the other game's check of a moving figure, in cells: past the figures' own jump (<see cref="ParkPresenceState.ViewJumpCells"/>).</summary>
	private const float MoveCells = ParkPresenceState.ViewJumpCells + 3;
	/// <summary>How many lines a test tries before it gives up on a press that met something else (a visitor's window, a figure).</summary>
	private const int LineTries = 4;

	private readonly JsonArray sightings = new();

	private Level ThePark => flow.Level ?? throw new InvalidOperationException( "Live co-op acceptance: no park is open." );
	private ParkHud TheHud => flow.Hud ?? throw new InvalidOperationException( "Live co-op acceptance: the park has no HUD." );
	private ParkPresence? Presence => flow.CoopPresence;
	private CoopSession TheSession => ThePark.CoopSession ?? throw new InvalidOperationException( "Live co-op acceptance: the park is in no session." );

	/// <summary>The other player's figure in this game's park, by name.</summary>
	private PresenceAvatar? Figure( string name ) => Presence?.State.Others.FirstOrDefault( other => other.Name == name );

	private static bool Near( PresenceAvatar? figure, (float X, float Y) place ) =>
		figure != null && MathF.Abs( figure.X - place.X ) <= NearCells && MathF.Abs( figure.Y - place.Y ) <= NearCells;

	private static JsonArray Cell( float x, float y ) => new( (JsonNode?)Math.Round( x, 2 ), Math.Round( y, 2 ) );

	/// <summary>Moves the overview to <paramref name="place"/> (cells), as a player scrolls there; the player's own figure follows the view.</summary>
	private void Look( (float X, float Y) place )
	{
		Require( Camera.Mode is ParkCameraMode && ThePark.OriginalPark?.Heightfield != null, $"the park's overview is the view (it is {Camera.Mode?.GetType().Name ?? "none"})" );
		var engine = GuestRenderer.CellToEngine( ThePark.OriginalPark!.Heightfield!, place.X, place.Y );
		((ParkCameraMode)Camera.Mode).Focus( new Vector3( engine.X, engine.Y, engine.Z ) );
		Camera.Update();
	}

	/// <summary>Where the view's centre is, in cells (<see cref="GameFlow.ViewCentre"/>).</summary>
	private (float X, float Y) ViewCentre() => GameFlow.ViewCentre( ThePark ) ?? throw new InvalidOperationException( "Live co-op acceptance: the view has no centre in the park." );

	/// <summary>
	/// The cells a figure can stand on (its ground, C2d-4: the park's own land, paths, queues and the entrance) at least <see cref="MoveCells"/> from <paramref name="from"/>,
	/// nearest first (ties by row, then column): far enough that the figure jumps there, near enough that the view stays over the park.
	/// </summary>
	private IEnumerable<(float X, float Y)> CellsAway( (float X, float Y) from )
	{
		var cells = ThePark.Guests?.Grid.Cells ?? throw new InvalidOperationException( "Live co-op acceptance: the park has no cells." );
		var ground = new ParkPresence.PresenceGround( cells );
		var found = new List<(float Distance, int Y, int X)>();
		for ( var y = 0; y < cells.Height; y++ )
			for ( var x = 0; x < cells.Width; x++ )
			{
				var distance = MathF.Sqrt( (x + 0.5f - from.X) * (x + 0.5f - from.X) + (y + 0.5f - from.Y) * (y + 0.5f - from.Y) );
				if ( distance >= MoveCells && ground.IsPassable( x, y ) )
					found.Add( (distance, y, x) );
			}
		return found.OrderBy( cell => cell.Distance ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ).Select( cell => (cell.X + 0.5f, cell.Y + 0.5f) );
	}

	/// <summary>
	/// Moves the view far away and waits until this game's own figure stands there: it jumps when the view moved far (C2d-4), and the other game sees it there. The place is the
	/// nearest of <see cref="CellsAway"/> the view centres on (the camera keeps a view near the park's edge over the park instead); <paramref name="moved"/> hears it.
	/// </summary>
	private IEnumerable<CoopLiveWait> MoveViewAway( Action<(float X, float Y)> moved )
	{
		var from = ViewCentre();
		(float X, float Y)? place = null;
		foreach ( var candidate in CellsAway( from ).Take( 400 ) )
		{
			Look( candidate );
			if ( GameFlow.ViewCentre( ThePark ) is { } centre && MathF.Abs( centre.X - candidate.X ) <= NearCells && MathF.Abs( centre.Y - candidate.Y ) <= NearCells )
			{
				place = candidate;
				break;
			}
		}
		Require( place != null, $"the view centres on ground {MoveCells} cells from ({from.X:0.#}, {from.Y:0.#})" );
		var (x, y) = place!.Value;
		moved( (x, y) );
		var own = Presence?.State.Own;
		Keep( "lastMove", new JsonObject { ["from"] = Cell( from.X, from.Y ), ["to"] = Cell( x, y ), ["ownBefore"] = own is { } before ? Cell( before.X, before.Y ) : null,
			["frame"] = frame } );
		yield return Wait( $"the own figure at ({x:0.#}, {y:0.#})", () => Presence is { } presence && MathF.Abs( presence.State.Own.X - x ) <= NearCells
			&& MathF.Abs( presence.State.Own.Y - y ) <= NearCells && !presence.State.Walking, 30 );
	}

	/// <summary>
	/// Sees <paramref name="name"/>'s figure as a player does (C2d-4): once it stands near <paramref name="at"/> (anywhere when null), the overview moves to it and the figure and its
	/// name plate are drawn in the frames that follow. Kept as a sighting, with a picture of the view in the evidence folder.
	/// </summary>
	private IEnumerable<CoopLiveWait> See( string name, string what, (float X, float Y)? at = null )
	{
		yield return Wait( at is { } place ? $"{name}'s figure near ({place.X:0.#}, {place.Y:0.#})" : $"{name}'s figure", () => Figure( name ) is { } seen && (at == null || Near( seen, at.Value )), 90 );
		var figure = Figure( name )!;
		Look( (figure.X, figure.Y) );
		yield return Wait( $"{name}'s figure and name drawn in the view", () => Figure( name ) != null && TheHud.DrawnNames.Contains( name ) && (ThePark.GuestRenderer?.DrawnFigures ?? 0) >= 2, 30 );
		var picture = $"{Role}-{what}.png";
		JsonArray? size = null;
		if ( !page )
		{
			var (_, width, height) = SandboxSmokeTest.CaptureOutputFrameTo( Path.Combine( evidenceDirectory, picture ) );
			size = new JsonArray( width, height );
		}
		var drawn = Figure( name )!;
		sightings.Add( new JsonObject
		{
			["what"] = what,
			["name"] = name,
			["cell"] = Cell( drawn.X, drawn.Y ),
			["own"] = Presence is { } presence ? Cell( presence.State.Own.X, presence.State.Own.Y ) : null,
			["drawnNames"] = new JsonArray( TheHud.DrawnNames.Select( drawnName => (JsonNode?)drawnName ).ToArray() ),
			["drawnFigures"] = ThePark.GuestRenderer?.DrawnFigures,
			["frame"] = frame,
			["at"] = Math.Round( Now, 3 ),
			["picture"] = picture,
			// a native picture's size as captured; a page's picture is the driver's, which knows its size
			["pictureSize"] = size,
		} );
		Keep( "sightings", sightings );
		// the page cannot read its output back (C2d-5b): the driver takes the canvas's picture while the view stays, and says so in its status
		if ( page )
		{
			Publish( "picture", picture );
			yield return Wait( $"the driver's picture {picture}", () => StatusOf( DriverRole, "pictures" ) is JsonArray taken
				&& taken.Any( item => item is JsonValue value && value.TryGetValue<string>( out var name ) && name == picture ), 30 );
		}
	}

	/// <summary>What a line laid through the HUD did (<see cref="LayLine"/>).</summary>
	private sealed class LineResult
	{
		public (int X, int Y)[] Cells { get; set; } = Array.Empty<(int X, int Y)>();
		public JsonObject Evidence { get; } = new();
	}

	/// <summary>
	/// A path line laid through the HUD as a player lays it (the native smoke's path check, docs/PATH.md): a press on a free owned cell starts the path tool, the pointer over the end
	/// shows its ghost, and a press on the end lays the line. The host's line is laid and charged, and its tool ended with a press on the end again; a guest's is refused by the
	/// session before the host lets it build, its park unchanged, and its tool ended with Escape; a <paramref name="granted"/> guest's goes to the host and is laid at its record
	/// ([EXT:online-coop-commands], C3a-2), its tool ended with a press on the end again. <paramref name="avoid"/> keeps the line clear of cells used before.
	/// </summary>
	private IEnumerable<CoopLiveWait> LayLine( bool host, ICollection<(int X, int Y)> avoid, LineResult result, bool granted = false )
	{
		var level = ThePark;
		var tool = level.CellTool;
		((int X, int Y) Start, (int X, int Y) End, System.Numerics.Vector2 StartPoint, System.Numerics.Vector2 EndPoint)? line = null;
		for ( var attempt = 0; attempt < LineTries && line == null; attempt++ )
		{
			yield return Wait( "the park's base screen alone, no tool armed", () => TheHud.Stack.Screens.Count == 1 && !tool.IsActive && InputGiven, 30 );
			var found = FreeLine( avoid );
			Require( found != null, "the park has a free owned line in reach of a click" );
			foreach ( var cell in LineCells( found!.Value.Start, found.Value.End ) )
				avoid.Add( cell );
			foreach ( var step in ClickThrough( found.Value.StartPoint ) )
				yield return step;
			yield return Wait( "the press on the line's start", () => InputGiven, 10 );
			yield return Pause( "the path tool's start", 0.3 );
			if ( tool.Mode == CellToolMode.Path && tool.Start == found.Value.Start )
				line = found;
			else
			{
				// the press met something else (a visitor's window): it is closed as a player closes it, and the next line is tried
				Log.Info( $"Live co-op acceptance: the press at {found.Value.Start} did not start the path tool (top screen {TheHud.Stack.Top?.Name}); trying another line." );
				while ( TheHud.Stack.Screens.Count > 1 || tool.IsActive || TheHud.InfoArmOpen )
				{
					Give( UiInput.Key( UiKeys.Back ) );
					yield return Wait( "Escape closes what the press opened", () => InputGiven, 10 );
					yield return Pause( "the window's close", 0.3 );
				}
			}
		}
		Require( line != null, $"a press on a free cell started the path tool within {LineTries} lines" );
		var (start, end, _, endPoint) = line!.Value;
		result.Cells = LineCells( start, end );
		foreach ( var step in HoverThrough( endPoint ) )
			yield return step;
		yield return Wait( "the line's ghost", () => InputGiven && tool.Ghost is { Built.Count: LineLength + 1 }, 10 );
		var refusals = level.SessionRefusals;
		var commit = tool.LastCommit;
		var submitted = level.CoopSession?.GuestCommands?.LastSubmitted;
		foreach ( var step in ClickThrough( endPoint ) )
			yield return step;
		if ( host )
		{
			yield return Wait( "the line laid", () => InputGiven && !ReferenceEquals( tool.LastCommit, commit ) && tool.LastCommit is { Completed: true }, 10 );
			Require( result.Cells.All( cell => level.Guests!.Grid.Cells.RawTypeAt( cell.X, cell.Y ) == (byte)ParkCellType.Path ), "the line's cells are paths" );
			result.Evidence["record"] = TheSession.HostStream?.LastRecord;
			result.Evidence["charged"] = tool.LastCommit!.Charged;
			Require( tool.LastCommit.Charged > 0, "the line was charged" );
			// a press on the line's end again ends the tool, laying nothing
			foreach ( var step in ClickThrough( endPoint ) )
				yield return step;
			yield return Wait( "the path tool's end", () => InputGiven && !tool.IsActive, 10 );
		}
		else if ( granted )
		{
			// the line goes to the host and waits; the HUD completes the tool when its record applies in this park. The request is the one the click submitted, followed by its
			// identity: a page's input is acknowledged late, by when the request may have resolved and the tool taken its outcome
			var requests = level.CoopSession?.GuestCommands;
			Require( requests != null, "the guest's park sends requests" );
			yield return Wait( "the line sent to the host", () => InputGiven && !ReferenceEquals( requests!.LastSubmitted, submitted ), 10 );
			var request = requests!.LastSubmitted!;
			Require( request.Command is LayLineCommand or QueueSegmentCommand, $"the click sent the line ({request.Command.Kind})" );
			yield return Wait( "the line laid at its record", () => !request.IsOpen && tool.Waiting == null, 120 );
			Require( request.State == CoopGuestRequestState.Applied && request.Accepted, $"the host took the line and laid it ({request.State}, {request.Result?.Reason ?? request.Refusal?.ToString()})" );
			Require( tool.LastCommit is { Completed: true } && level.SessionRefusals == refusals, "the line was laid whole, with no refusal" );
			Require( result.Cells.All( cell => level.Guests!.Grid.Cells.RawTypeAt( cell.X, cell.Y ) == (byte)ParkCellType.Path ), "the line's cells are paths" );
			result.Evidence["record"] = request.Record;
			result.Evidence["charged"] = tool.LastCommit!.Charged;
			// a press on the line's end again ends the tool, laying nothing
			foreach ( var step in ClickThrough( endPoint ) )
				yield return step;
			yield return Wait( "the path tool's end", () => InputGiven && !tool.IsActive, 10 );
		}
		else
		{
			yield return Wait( "the session's refusal", () => InputGiven && level.SessionRefusals > refusals, 10 );
			Require( result.Cells.All( cell => level.Paths!.CheckCell( cell.X, cell.Y ) == CellBuildResult.Ok ), "the refused line left its cells free" );
			result.Evidence["refusalsBefore"] = refusals;
			result.Evidence["refusalsAfter"] = level.SessionRefusals;
			if ( tool.IsActive )
			{
				Give( UiInput.Key( UiKeys.Back ) );
				yield return Wait( "Escape ends the path tool", () => InputGiven && !tool.IsActive, 10 );
			}
		}
		result.Evidence["cells"] = new JsonArray( result.Cells.Select( cell => (JsonNode?)new JsonArray( cell.X, cell.Y ) ).ToArray() );
		result.Evidence["frame"] = frame;
		result.Evidence["at"] = Math.Round( Now, 3 );
	}

	private static UiInput Press( System.Numerics.Vector2 point ) => new( point, true, true, false, false, UiKeys.None );

	private static UiInput Release( System.Numerics.Vector2 point ) => new( point, false, false, true, false, UiKeys.None );

	private static (int X, int Y)[] LineCells( (int X, int Y) start, (int X, int Y) end ) => Enumerable.Range( 0, LineLength + 1 )
		.Select( step => (start.X + Math.Sign( end.X - start.X ) * step, start.Y + Math.Sign( end.Y - start.Y ) * step) ).ToArray();

	/// <summary>
	/// A free owned line of <see cref="LineLength"/> + 1 cells whose ends a click reaches in the middle of the view, clear of <paramref name="avoid"/>; the view moves over it, as a player
	/// scrolls to free land (the native smoke's search, SandboxSmokeTest.Paths.cs). Null when the park has none.
	/// </summary>
	private ((int X, int Y) Start, (int X, int Y) End, System.Numerics.Vector2 StartPoint, System.Numerics.Vector2 EndPoint)? FreeLine( ICollection<(int X, int Y)> avoid )
	{
		var level = ThePark;
		var hud = TheHud;
		var canvas = flow.Context.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out point )
			&& point.X > canvas.Width * 0.3f && point.X < canvas.Width * 0.7f && point.Y > canvas.Height * 0.2f && point.Y < canvas.Height * 0.6f
			&& !hud.Stack.Covers( canvas, point )
			&& level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;
		var grid = level.Guests!.Grid;
		for ( var index = 0; index < grid.CountX * grid.CountY; index++ )
		{
			var cell = (X: index % grid.CountX, Y: index / grid.CountX);
			foreach ( var (dx, dy) in GuestPathGrid.Directions )
			{
				var last = (X: cell.X + dx * LineLength, Y: cell.Y + dy * LineLength);
				var cells = LineCells( cell, last );
				if ( !cells.All( each => level.Paths!.CheckCell( each.X, each.Y ) == CellBuildResult.Ok && !avoid.Contains( each ) ) )
					continue;
				if ( !(Clickable( cell, out _ ) && Clickable( last, out _ )) )
					Look( (cell.X + dx * LineLength / 2f + 0.5f, cell.Y + dy * LineLength / 2f + 0.5f) );
				if ( Clickable( cell, out var startPoint ) && Clickable( last, out var endPoint ) )
					return (cell, last, startPoint, endPoint);
			}
		}
		return null;
	}

	/// <summary>
	/// A chat line said as a player says it in the shared park (C2d-4): Enter over the park opens the chat mode with its entry focused, the line is typed and Enter says it; the
	/// room's echo shows it was said.
	/// </summary>
	private IEnumerable<CoopLiveWait> Say( string line )
	{
		var hud = TheHud;
		yield return Wait( "the park's base screen alone", () => hud.Stack.Screens.Count == 1 && InputGiven, 30 );
		foreach ( var step in KeyThrough( UiKeys.Accept, "Enter" ) )
			yield return step;
		yield return Wait( "Enter over the park opens the chat entry", () => InputGiven && hud.ChatMode && hud.ChatEntryFocused, 10 );
		foreach ( var step in TypeThrough( line ) )
			yield return step;
		yield return Wait( "the line typed into the entry", () => InputGiven && hud.VisitChatPanel?.Entry.Text == line, 10 );
		foreach ( var step in KeyThrough( UiKeys.Accept, "Enter" ) )
			yield return step;
		yield return Wait( "the line said in the shared park's room", () => Heard( line ), 30 );
		Require( hud.VisitChatPanel!.Entry.Text.Length == 0 && hud.ChatEntryFocused, "Enter empties the entry and keeps it" );
	}

	/// <summary>Whether this game's chat lines hold <paramref name="line"/>.</summary>
	private bool Heard( string line ) => flow.Online.ChatLines.Any( said => said.Contains( line, StringComparison.Ordinal ) );

	/// <summary>F4 through the keyboard: the chat mode and the HUD change places (C2d-4), as <paramref name="chatMode"/> says the result is.</summary>
	private IEnumerable<CoopLiveWait> ToggleChatMode( bool chatMode )
	{
		var hud = TheHud;
		yield return Wait( "the park's base screen alone", () => hud.Stack.Screens.Count == 1 && InputGiven, 30 );
		foreach ( var step in TapThrough( Veldrid.Key.F4, "F4" ) )
			yield return step;
		yield return Wait( chatMode ? "F4 opens the chat mode" : "F4 goes back to the HUD", () => hud.ChatMode == chatMode && hud.Stack.Screens.Count == 1
			&& (chatMode || ReferenceEquals( hud.Stack.Screens[0], hud.Screen )), 10 );
	}

	/// <summary>The session's state as this game's link has it.</summary>
	private JsonObject SessionEvidence()
	{
		var state = flow.Online.Coop.Session;
		return new JsonObject
		{
			["id"] = state?.Id,
			["code"] = state?.Code,
			["title"] = state?.Title,
			["listed"] = state?.Listed,
			["maximumPlayers"] = state?.MaximumPlayers,
			["members"] = state == null ? null : new JsonArray( state.Members.OrderBy( member => member.Slot )
				.Select( member => (JsonNode?)new JsonObject { ["name"] = member.Name, ["slot"] = member.Slot, ["connected"] = member.Connected, ["host"] = member.Id == state.Host } ).ToArray() ),
		};
	}

	/// <summary>The session's stream as this game has it: the host's records and frames, or the guest's records, frames, compared checkpoints and join builds.</summary>
	private JsonObject StreamEvidence()
	{
		var session = ThePark.CoopSession;
		var evidence = new JsonObject { ["ticks"] = flow.Level?.TickCount };
		if ( session?.HostStream is { } host )
		{
			evidence["lastRecord"] = host.LastRecord;
			evidence["lastFrame"] = host.LastFrame;
			evidence["capturesTaken"] = host.CapturesTaken;
		}
		if ( session?.GuestStream is { } guest )
		{
			evidence["generation"] = guest.Generation;
			evidence["lastRecord"] = guest.LastRecord;
			evidence["lastFrame"] = guest.LastFrame;
			evidence["checks"] = guest.Checks;
			evidence["recordChecks"] = guest.RecordChecks;
			evidence["desyncs"] = guest.Desyncs;
			evidence["stopped"] = guest.Stopped;
		}
		if ( session != null )
		{
			evidence["failedJoins"] = session.FailedJoins;
			evidence["joinBuilds"] = session.JoinTimings.Count;
			evidence["refusals"] = ThePark.SessionRefusals;
		}
		evidence["streams"] = streams;
		return evidence;
	}
}
