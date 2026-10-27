using System.Text.Json.Nodes;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-undo] The live acceptance's undo scenario (C5-1; docs/COOP.md, "The live acceptance"): after the baseline's lines the guest lays a path line of two segments through the
/// HUD and takes it back with two Backspaces, each a take-back the host decides and every park takes; then it lays a line with its tool kept on, the host lays a line across one of its
/// cells, and the guest's Backspace is refused as changed on both parks, with its note; the host takes its own line back with its Backspace, and the guest's next Backspace takes its line
/// back. Both games keep every take-back record they applied, and the driver compares them.
/// </summary>
internal sealed partial class CoopLiveTest
{
	/// <summary>A take-back as a park applied it: its record, its player, the step it named, the host's verdict and whether it was taken.</summary>
	private static JsonObject TakeBackEvidence( long? record, TakeBackCommand takeBack, bool accepted ) => new()
	{
		["record"] = record,
		["player"] = takeBack.Player,
		["step"] = takeBack.Step,
		["verdict"] = takeBack.Verdict.ToString(),
		["accepted"] = accepted,
	};

	private static JsonArray CellsEvidence( IEnumerable<(int X, int Y)> cells ) => new( cells.Select( cell => (JsonNode?)new JsonArray( cell.X, cell.Y ) ).ToArray() );

	/// <summary>Whether every one of <paramref name="cells"/> is a path cell of this game's park.</summary>
	private bool ArePaths( IEnumerable<(int X, int Y)> cells ) => cells.All( cell => ThePark.Guests!.Grid.Cells.RawTypeAt( cell.X, cell.Y ) == (byte)ParkCellType.Path );

	/// <summary>Whether none of <paramref name="cells"/> is a path cell of this game's park.</summary>
	private bool AreFree( IEnumerable<(int X, int Y)> cells ) => cells.All( cell => ThePark.Guests!.Grid.Cells.RawTypeAt( cell.X, cell.Y ) != (byte)ParkCellType.Path );

	/// <summary>Where a click reaches <paramref name="cell"/> in the view, when it does.</summary>
	private bool PointOf( (int X, int Y) cell, out System.Numerics.Vector2 point )
	{
		var canvas = flow.Context.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		return ThePark.TryProjectCell( cell.X, cell.Y, viewport, out point ) && !TheHud.Stack.Covers( canvas, point )
			&& ThePark.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;
	}

	/// <summary>Whether this game's HUD said <paramref name="text"/>, shown or still queued.</summary>
	private bool Said( OpenTpwText text ) => TheHud.Messages.Concat( TheHud.PendingMessages ).Contains( flow.Strings.Extra( text ) );

	/// <summary>
	/// A guest's path line of <paramref name="segments"/> segments laid through the HUD (C5-1): a press on a free owned cell starts the path tool, then a click at each segment's end, each
	/// going to the host and laid at its record; the tool stays on. Each segment's cells and record go to <paramref name="laid"/>.
	/// </summary>
	private IEnumerable<CoopLiveWait> LaySegments( ICollection<(int X, int Y)> avoid, int segments, List<((int X, int Y)[] Cells, long Record)> laid )
	{
		var level = ThePark;
		var tool = level.CellTool;
		var requests = TheSession.GuestCommands!;
		((int X, int Y) Start, (int X, int Y) End)? line = null;
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
				line = (found.Value.Start, found.Value.End);
			else
			{
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
		var (start, end) = line!.Value;
		var (dx, dy) = (Math.Sign( end.X - start.X ), Math.Sign( end.Y - start.Y ));
		var from = start;
		for ( var segment = 1; segment <= segments; segment++ )
		{
			var reach = LineLength * segment / segments;
			var to = (X: start.X + dx * reach, Y: start.Y + dy * reach);
			Require( PointOf( to, out var point ), $"the segment's end {to} is in reach of a click" );
			foreach ( var step in HoverThrough( point ) )
				yield return step;
			yield return Wait( "the segment's ghost", () => InputGiven && tool.Ghost != null, 10 );
			var before = requests.LastSubmitted;
			foreach ( var step in ClickThrough( point ) )
				yield return step;
			yield return Wait( "the segment sent to the host", () => InputGiven && !ReferenceEquals( requests.LastSubmitted, before ), 10 );
			var request = requests.LastSubmitted!;
			Require( request.Command is LayLineCommand, $"the click sent the segment ({request.Command.Kind})" );
			yield return Wait( "the segment laid at its record", () => !request.IsOpen && tool.Waiting == null, 120 );
			var cells = Enumerable.Range( 0, Math.Abs( to.X - from.X ) + Math.Abs( to.Y - from.Y ) + 1 ).Select( index => (from.X + dx * index, from.Y + dy * index) ).ToArray();
			Require( request.Accepted && tool.LastCommit is { Completed: true } && tool.IsActive && ArePaths( cells ),
				$"the segment was laid whole and the tool stays on ({request.State}, {request.Result?.Reason ?? request.Refusal?.ToString()})" );
			laid.Add( (cells, request.Record) );
			from = to;
		}
	}

	/// <summary>
	/// Backspace in this game's tool as a player presses it: a guest's take-back goes to the host and its outcome comes back to the tool, which <paramref name="outcome"/> hears; the host's
	/// is decided at once, its record kept by the observer.
	/// </summary>
	private IEnumerable<CoopLiveWait> TakeBackThrough( Action<CoopGuestCommands.Request>? outcome = null )
	{
		var tool = ThePark.CellTool;
		yield return Wait( "the park's base screen alone, the tool on", () => TheHud.Stack.Screens.Count == 1 && tool.IsActive && !tool.IsWaiting && InputGiven, 30 );
		if ( hosting )
		{
			var log = ThePark.Commands.Log.Count;
			foreach ( var step in BackspaceThrough() )
				yield return step;
			yield return Wait( "the host's take-back decided", () => InputGiven && ThePark.Commands.Log.Skip( log ).Any( entry => entry.Command is TakeBackCommand ), 10 );
			yield break;
		}
		var requests = TheSession.GuestCommands!;
		var before = requests.LastSubmitted;
		foreach ( var step in BackspaceThrough() )
			yield return step;
		yield return Wait( "the take-back sent to the host", () => InputGiven && !ReferenceEquals( requests.LastSubmitted, before ), 10 );
		var request = requests.LastSubmitted!;
		Require( request.Command is TakeBackCommand, $"Backspace sent a take-back ({request.Command.Kind})" );
		yield return Wait( "the take-back's outcome", () => !request.IsOpen && tool.WaitingUndo == null, 120 );
		outcome?.Invoke( request );
	}

	/// <summary>A guest's take-back request as its park applied it.</summary>
	private static JsonObject RequestEvidence( CoopGuestCommands.Request request ) =>
		TakeBackEvidence( request.State == CoopGuestRequestState.Applied ? request.Record : null, ((TakeBackCommand)request.Command) with
		{
			Verdict = request.Accepted ? TakeBackVerdict.Taken : TakeBackCommand.VerdictOf( request.Result?.Reason ) ?? TakeBackVerdict.Undecided
		}, request.Accepted );

	/// <summary>The guest's side of the undo scenario (C5-1).</summary>
	private IEnumerable<CoopLiveWait> GuestUndo( ICollection<(int X, int Y)> avoid )
	{
		var takeBacks = new JsonArray();
		var evidence = new JsonObject { ["takeBacks"] = takeBacks };
		// a line of two segments, taken back by two Backspaces, the newest first
		var laid = new List<((int X, int Y)[] Cells, long Record)>();
		foreach ( var step in LaySegments( avoid, 2, laid ) )
			yield return step;
		evidence["segments"] = new JsonArray( laid.Select( segment => (JsonNode?)new JsonObject { ["record"] = segment.Record, ["cells"] = CellsEvidence( segment.Cells ) } ).ToArray() );
		for ( var index = laid.Count - 1; index >= 0; index-- )
		{
			var segment = laid[index];
			CoopGuestCommands.Request? request = null;
			foreach ( var step in TakeBackThrough( outcome => request = outcome ) )
				yield return step;
			Require( request!.Accepted && ((TakeBackCommand)request.Command).Step == segment.Record, $"the take-back of the segment of record {segment.Record} was taken ({request.Result?.Reason})" );
			// the shared corner of the two segments stays a path until the first segment goes
			Require( AreFree( index == 0 ? segment.Cells : segment.Cells.Skip( 1 ) ), "the segment's cells are free again" );
			takeBacks.Add( RequestEvidence( request ) );
		}
		Require( ThePark.CellTool is { IsActive: true, SegmentCount: 0 }, "the tool is on, its segments gone" );
		var checks = TheSession.GuestStream!.Checks;
		yield return Wait( "checkpoints compared after the take-backs", () => TheSession.GuestStream is { Stopped: false } stream && stream.Checks >= checks + 2, 60 );
		Give( UiInput.Key( UiKeys.Back ) );
		yield return Wait( "Escape ends the path tool", () => InputGiven && !ThePark.CellTool.IsActive, 10 );
		Publish( "undone", true );

		// a line with the tool kept on; the host lays a line across its middle cell
		var kept = new List<((int X, int Y)[] Cells, long Record)>();
		foreach ( var step in LaySegments( avoid, 1, kept ) )
			yield return step;
		var line = kept.Single();
		evidence["kept"] = new JsonObject { ["record"] = line.Record, ["cells"] = CellsEvidence( line.Cells ) };
		Publish( "kept", evidence["kept"]!.DeepClone() );
		yield return Wait( "the host's line across the guest's", () => Other( "crossed" ) is JsonObject, 300 );
		foreach ( var cell in Cells( Other( "crossed" )!["cells"] ) )
			avoid.Add( cell );
		var crossed = Cells( Other( "crossed" )!["cells"] );
		yield return Wait( "the host's line in the guest's park", () => ArePaths( crossed ), 60 );
		// refused: the host's line still crosses its cell; nothing changes, the note says why, the tool keeps the line
		CoopGuestCommands.Request? refused = null;
		foreach ( var step in TakeBackThrough( outcome => refused = outcome ) )
			yield return step;
		Require( refused!.State == CoopGuestRequestState.Applied && !refused.Accepted && TakeBackCommand.VerdictOf( refused.Result?.Reason ) == TakeBackVerdict.Changed,
			$"the take-back was refused as changed ({refused.Result?.Reason})" );
		Require( ArePaths( line.Cells ) && ThePark.CellTool is { IsActive: true, SegmentCount: 1 }, "nothing taken back, the tool keeps the line" );
		yield return Wait( "the refusal's note", () => Said( OpenTpwText.CoopUndoChanged ), 10 );
		takeBacks.Add( RequestEvidence( refused ) );
		Publish( "refusedChanged", true );
		// the host takes its own line back; then the guest's Backspace takes its line back
		yield return Wait( "the host took its line back", () => OtherSays( "hostUndone" ), 300 );
		yield return Wait( "the host's take-back in the guest's park", () => AreFree( crossed.Where( cell => !line.Cells.Contains( cell ) ) ), 60 );
		CoopGuestCommands.Request? taken = null;
		foreach ( var step in TakeBackThrough( outcome => taken = outcome ) )
			yield return step;
		Require( taken!.Accepted && AreFree( line.Cells ), $"the line was taken back ({taken.Result?.Reason})" );
		takeBacks.Add( RequestEvidence( taken ) );
		checks = TheSession.GuestStream!.Checks;
		yield return Wait( "checkpoints compared after the last take-back", () => TheSession.GuestStream is { Stopped: false } stream && stream.Checks >= checks + 2, 60 );
		Give( UiInput.Key( UiKeys.Back ) );
		yield return Wait( "Escape ends the path tool", () => InputGiven && !ThePark.CellTool.IsActive, 10 );
		evidence["applied"] = AppliedTakeBacks();
		Keep( "undo", evidence );
		Publish( "undoDone", true );
	}

	/// <summary>Every take-back this game's park applied, in its log's order: its player, the step it named and the host's verdict.</summary>
	private JsonArray AppliedTakeBacks() => new( ThePark.Commands.Log.Select( entry => entry.Command ).OfType<TakeBackCommand>()
		.Select( takeBack => (JsonNode?)new JsonObject { ["player"] = takeBack.Player, ["step"] = takeBack.Step, ["verdict"] = takeBack.Verdict.ToString() } ).ToArray() );

	/// <summary>
	/// The host's line across the guest's kept line (C5-1): through one of its cells, from a free cell beside it to a free cell beyond, as a player lays it through the HUD, the tool kept on.
	/// Its cells (the crossed one included) go to <paramref name="cells"/>.
	/// </summary>
	private IEnumerable<CoopLiveWait> CrossLine( IReadOnlyList<(int X, int Y)> guestLine, ICollection<(int X, int Y)> avoid, List<(int X, int Y)> cells )
	{
		var level = ThePark;
		var tool = level.CellTool;
		var (dx, dy) = (Math.Sign( guestLine[^1].X - guestLine[0].X ), Math.Sign( guestLine[^1].Y - guestLine[0].Y ));
		var (px, py) = (-dy, dx);
		((int X, int Y) Start, (int X, int Y) End, System.Numerics.Vector2 StartPoint, System.Numerics.Vector2 EndPoint)? found = null;
		// a line across one of the guest's inner cells, two free cells on each side; else, by the park's edge, a line of four from beside one of them, which links into it: either changes
		// the guest's cell, and ends on a free cell, so the host's tool stays on
		var candidates = guestLine.Skip( 1 ).Take( guestLine.Count - 2 ).Select( cross => (Cross: cross, From: -2, To: 2) )
			.Concat( guestLine.Skip( 1 ).Take( guestLine.Count - 2 ).SelectMany( cross => new[] { (Cross: cross, From: 1, To: 4), (Cross: cross, From: -1, To: -4) } ) );
		foreach ( var (cross, from, to) in candidates )
		{
			var start = (X: cross.X + from * px, Y: cross.Y + from * py);
			var end = (X: cross.X + to * px, Y: cross.Y + to * py);
			var line = ParkPathBuilder.LayLine( start, end ).ToArray();
			if ( !line.Where( cell => cell != cross ).All( cell => level.Paths!.CheckCell( cell.X, cell.Y ) == CellBuildResult.Ok && !avoid.Contains( cell ) ) )
				continue;
			if ( !(PointOf( start, out _ ) && PointOf( end, out _ )) )
				Look( (cross.X + 0.5f, cross.Y + 0.5f) );
			if ( PointOf( start, out var startPoint ) && PointOf( end, out var endPoint ) )
			{
				found = (start, end, startPoint, endPoint);
				cells.AddRange( line );
				break;
			}
		}
		Require( found != null, "a free line across or into the guest's line is in reach of a click" );
		foreach ( var cell in cells )
			avoid.Add( cell );
		yield return Wait( "the park's base screen alone, no tool armed", () => TheHud.Stack.Screens.Count == 1 && !tool.IsActive && InputGiven, 30 );
		foreach ( var step in ClickThrough( found!.Value.StartPoint ) )
			yield return step;
		yield return Wait( "the path tool's start", () => InputGiven && tool.Mode == CellToolMode.Path && tool.Start == found.Value.Start, 10 );
		foreach ( var step in HoverThrough( found.Value.EndPoint ) )
			yield return step;
		yield return Wait( "the line's ghost", () => InputGiven && tool.Ghost != null, 10 );
		var commit = tool.LastCommit;
		foreach ( var step in ClickThrough( found.Value.EndPoint ) )
			yield return step;
		yield return Wait( "the line laid", () => InputGiven && !ReferenceEquals( tool.LastCommit, commit ) && tool.LastCommit is { Completed: true }, 10 );
		Require( ArePaths( cells ) && tool.IsActive, "the host's line crosses or joins the guest's and the tool stays on" );
	}

	/// <summary>The host's side of the undo scenario (C5-1): the guest's take-backs seen at their records, its own line across the guest's, taken back by its own Backspace.</summary>
	private IEnumerable<CoopLiveWait> HostUndo( int seat, ICollection<(int X, int Y)> avoid )
	{
		var level = ThePark;
		var takeBacks = new JsonArray();
		void Observe( ParkCommandLogEntry entry, ParkCommandResult result )
		{
			if ( entry.Command is TakeBackCommand takeBack )
				takeBacks.Add( TakeBackEvidence( TheSession.HostStream?.LastRecord, takeBack, result.Accepted ) );
		}
		level.Commands.Applied += Observe;
		var evidence = new JsonObject { ["seat"] = seat, ["takeBacks"] = takeBacks };
		try
		{
			yield return Wait( "the guest took its two segments back", () => OtherSays( "undone" ), 300 );
			yield return Wait( "the guest's line to cross", () => Other( "kept" ) is JsonObject, 300 );
			var guestLine = Cells( Other( "kept" )!["cells"] );
			yield return Wait( "the guest's line in the host's park", () => ArePaths( guestLine ), 60 );
			var crossing = new List<(int X, int Y)>();
			foreach ( var step in CrossLine( guestLine, avoid, crossing ) )
				yield return step;
			evidence["crossed"] = CellsEvidence( crossing );
			Publish( "crossed", new JsonObject { ["cells"] = CellsEvidence( crossing ) } );
			yield return Wait( "the guest's refused take-back", () => OtherSays( "refusedChanged" ), 300 );
			// the host's own Backspace takes its line back; the guest's cell it crossed stays the guest's path
			foreach ( var step in TakeBackThrough() )
				yield return step;
			Require( AreFree( crossing.Where( cell => !guestLine.Contains( cell ) ) ) && ArePaths( guestLine ), "the host's line taken back, the guest's line whole" );
			Give( UiInput.Key( UiKeys.Back ) );
			yield return Wait( "Escape ends the path tool", () => InputGiven && !level.CellTool.IsActive, 10 );
			Publish( "hostUndone", true );
			yield return Wait( "the guest took its line back", () => OtherSays( "undoDone" ), 300 );
			Require( AreFree( guestLine ), "the guest's line is gone from the host's park" );
		}
		finally
		{
			level.Commands.Applied -= Observe;
		}
		evidence["applied"] = AppliedTakeBacks();
		Keep( "undo", evidence );
	}
}
