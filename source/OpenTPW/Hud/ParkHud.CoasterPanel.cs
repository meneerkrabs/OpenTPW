using NVector2 = System.Numerics.Vector2;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Hud;

/// <summary>
/// The coaster tool's panel (COAST-028): the slide-out panel with the nine original buttons beside the main panel, the pointer handling of every sub-mode (press, drag, release) and the build
/// cursor's cost. The sub-modes themselves are <see cref="CoasterToolPanel"/>.
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>The panel's table rectangles are relative to the panel window; the window sits this far from them on the 2048 x 1536 canvas (measured from the original's video, 73:20).</summary>
	// [BIN:TPW-EXE:0x751530 coaster panel table] root 0x17 (859, 916)-(1534, 1190) with !f_plain boxes 0x22 (859..1003) and 0x18 (1037..1407) and the nine buttons 0x19..0x21; the table holds no window position
	// [APPROX:COAST-028] the offset (-372, +232) puts the left box 2.3 cells right of the main panel as the video shows it; the table 0x751530 holds no window position, its handler is 0x4982a0 and its builder 0x498790 hands the table to the window code 0x65fd0e; the panel appears at once instead of sliding out — evidence needed: that window code's placement and slide
	private const float CoasterPanelDx = -372, CoasterPanelDy = 232;

	private static UiRect CoasterRect( float left, float top, float right, float bottom ) => Rect( left + CoasterPanelDx, top + CoasterPanelDy, right + CoasterPanelDx, bottom + CoasterPanelDy );

	/// <summary>The green arm the panel slides out of (the main panel's drawer art) and the two blue boxes on it.</summary>
	private static readonly UiRect CoasterArmRect = Rect( 405, 1090, 1360, 1445 );
	private static readonly UiRect CoasterLeftBox = CoasterRect( 859, 916, 1003, 1190 );
	private static readonly UiRect CoasterMiddleBox = CoasterRect( 1037, 916, 1407, 1190 );

	/// <summary>One of the panel's buttons: the control id, the model, the table rectangle, the tooltip help (UIHELPTEXT index) and the help the click shows (<c>0x486b00</c>), or -1.</summary>
	private readonly record struct CoasterButtonSpec( CoasterToolButton Button, int Control, string Model, UiRect Rect, int Help, int ToolHelp );

	// [BIN:TPW-EXE:0x4982a0 panel click handler] control 0x19 → button 8 (tilt), 0x1a → 0x10 (height), 0x1b → 4 (move), 0x1c → 0x20 (rotate), 0x1d → 0x100 (stack down), 0x1e → 0x80 (stack up), 0x1f → 1 (build), 0x20 → 2 (loop), 0x21 → 0x40 (demolish); the click shows help 0x125 (build), 0x126 or 0x127 (loop), 0x121 (move), 0x124 (demolish)
	// [BIN:TPW-EXE:0x751570 coaster panel buttons] the models of the table's controls: cb_incline, cb_loft, cb_move, cb_rotate, cb_swapdown, cb_swapup, cb_track, cb_loop (control 0x20 names no model of its own: the one left over), cb_dellast; the help ids are 569, 297, 296, 300, 303, 302, 564, 565 and 304
	private static readonly CoasterButtonSpec[] CoasterButtons =
	[
		new( CoasterToolButton.Build, 0x1f, "cb_track", CoasterRect( 877, 944, 979, 1046 ), 564, 0x125 ),
		new( CoasterToolButton.Loop, 0x20, "cb_loop", CoasterRect( 877, 1065, 979, 1168 ), 565, 0x126 ),
		new( CoasterToolButton.Move, 0x1b, "cb_move", CoasterRect( 1057, 944, 1159, 1046 ), 296, 0x121 ),
		new( CoasterToolButton.Height, 0x1a, "cb_loft", CoasterRect( 1170, 944, 1272, 1046 ), 297, -1 ),
		new( CoasterToolButton.StackUp, 0x1e, "cb_swapup", CoasterRect( 1282, 944, 1385, 1046 ), 302, -1 ),
		new( CoasterToolButton.Rotate, 0x1c, "cb_rotate", CoasterRect( 1057, 1065, 1159, 1168 ), 300, -1 ),
		new( CoasterToolButton.Tilt, 0x19, "cb_incline", CoasterRect( 1170, 1065, 1272, 1168 ), 569, -1 ),
		new( CoasterToolButton.StackDown, 0x1d, "cb_swapdown", CoasterRect( 1282, 1065, 1385, 1168 ), 303, -1 ),
		new( CoasterToolButton.Demolish, 0x21, "cb_dellast", CoasterRect( 1432, 944, 1534, 1046 ), 304, 0x124 )
	];

	private readonly List<UiElement> coasterPanelElements = new();
	/// <summary>The cell under the pointer was pressed on a pylon the sub-mode picked up: the release belongs to the tool.</summary>
	private bool coasterPress;

	/// <summary>
	/// [EXT:online-coop-commands] The press of this gesture was made while a guest's request of the panel waited for the host, at its frame's start or when it was taken (C3c-2): its release is no
	/// click and no drag's release, whatever came back meanwhile.
	/// </summary>
	private bool coasterPressWhileWaiting;

	private void BuildCoasterPanel()
	{
		// the panel's backdrop takes the clicks of the area it covers, so a click beside a button does not reach the park
		var shield = hud.Add( new UiLabel { Id = "coasterPanelShield", Text = () => "", Bounds = CoasterArmRect, Anchor = UiAnchor.BottomLeft, Visible = false } );
		foreach ( var spec in CoasterButtons )
		{
			var button = hud.Add( new UiButton
			{
				Id = $"coaster{spec.Button}",
				Model = spec.Model,
				Help = strings.Help( spec.Help ),
				// Coaster panel table 0x751530 is not generated. These nine toggles are its radio group (type 8, style 0x11), so they play 0xBD.
				ClickSound = GameAudio.SelectClickEvent,
				Clicked = () => SelectCoasterButton( spec ),
				Selected = () => level.CoasterPanel?.Selected == spec.Button,
				Bounds = spec.Rect,
				Anchor = UiAnchor.BottomLeft,
				Visible = false
			} );
			coasterPanelElements.Add( button );
		}
		coasterPanelElements.Add( shield );
	}

	/// <summary>Shows or hides the panel with the tool and sets which buttons are enabled (the enable mask <c>0x445910</c>).</summary>
	private void UpdateCoasterPanelElements()
	{
		// harnesses that build the HUD without its constructor have no panel buttons
		if ( coasterPanelElements == null || coasterPanelElements.Count != CoasterButtons.Length + 1 )
			return;
		var panel = level.IsCoasterToolActive ? level.CoasterPanel : null;
		ReportCoasterPanelClosed( panel != null );
		panel?.Refresh();
		coasterPanelElements[^1].Visible = panel != null;
		for ( var index = 0; index < CoasterButtons.Length; index++ )
		{
			var element = coasterPanelElements[index];
			var spec = CoasterButtons[index];
			// the loop button is hidden below ride level 1 (0x49880F) and on a coaster without a loop pylon
			element.Visible = panel != null && (spec.Button != CoasterToolButton.Loop || panel.LoopUnlocked);
			element.Enabled = panel != null && panel.IsEnabled( spec.Button );
		}
	}

	private void SelectCoasterButton( CoasterButtonSpec spec )
	{
		if ( level.CoasterPanel is not { } panel )
			return;
		if ( !panel.Select( spec.Button ) )
		{
			// [EXT:online-coop-commands] a guest's panel takes no button while its request waits: the outcome finds the selection its action left (C3c-2)
			if ( panel.IsWaiting )
				level.NoteWaitingClick();
			return;
		}
		if ( spec.ToolHelp >= 0 )
		{
			// [BIN:TPW-EXE:0x4982a0 loop help] the loop button shows help 0x127 instead of 0x126 when the mask has bit 0x200 (definition flag 0x100: the flume coasters)
			var help = spec.Button == CoasterToolButton.Loop && (panel.Builder.Ride.Definition.Flags & CoasterDefinition.TroughFlag) != 0 ? 0x127 : spec.ToolHelp;
			PostMessage( strings.Help( help ) );
			// the same ids are the advisor messages the panel posts through 0x486b00 on the press: 0x121 (289, move), 0x124 (292, demolish), 0x125 (293, build), 0x126 (294, loop) or 0x127 (295, flume pylons)
			PostToolHelp( help );
		}
	}

	private bool coasterPanelShown;

	/// <summary>
	/// The panel's close message (0x1001D8, when the tool ends and the panel goes) withdraws the five help messages its buttons post.
	/// </summary>
	// [BIN:TPW-EXE:0x4982a0 panel close] message 0x1001D8 resets the sub-mode and withdraws 0x125, 0x127, 0x126, 0x121 and 0x124 (293, 295, 294, 289, 292) through 0x486b40 (0x4986ce-0x498702)
	private void ReportCoasterPanelClosed( bool shown )
	{
		if ( coasterPanelShown && !shown )
			foreach ( var message in AdvisorToolHelp.CoasterPanelMessages )
				host?.HelpWithdrawn?.Invoke( message );
		coasterPanelShown = shown;
	}

	/// <summary>The panel's backdrop: the green arm out of the main panel and the two blue boxes, under the HUD's buttons.</summary>
	private void DrawCoasterPanelBackground( UiContext context )
	{
		if ( !level.IsCoasterToolActive )
			return;
		var canvas = context.Canvas;
		context.DrawModel( "panel", 0, canvas.Map( CoasterArmRect, UiAnchor.BottomLeft ) );
		context.DrawModel( "!f_plain", 0, canvas.Map( CoasterLeftBox, UiAnchor.BottomLeft ) );
		context.DrawModel( "!f_plain", 0, canvas.Map( CoasterMiddleBox, UiAnchor.BottomLeft ) );
	}

	/// <summary>The pointer in the original's pixels: 1024 across the screen, which the drag rates are meant for.</summary>
	private static NVector2 OriginalPixels( UiContext context, NVector2 mouse ) => mouse * (1024f / Math.Max( 1f, context.Canvas.Width ));

	/// <summary>
	/// The pylon under the pointer: the pylon on the hovered cell, in a stack the piece whose projected position is nearest the pointer (the original picks the pylon model under the pointer).
	/// </summary>
	private int? PickCoasterNode( CoasterToolPanel panel, UiContext context, NVector2 mouse )
	{
		if ( cursorX < 0 )
			return null;
		var nodes = panel.NodesAt( cursorX, cursorY );
		if ( nodes.Count <= 1 )
			return nodes.Count == 1 ? nodes[0] : null;
		var viewport = new NVector2( context.Canvas.Width, context.Canvas.Height );
		var track = panel.Track;
		int? best = null;
		var bestDistance = float.MaxValue;
		foreach ( var index in nodes )
		{
			var position = track.NodePosition( panel.Builder.Site, track.Nodes[index] );
			if ( !level.TryProjectCoasterPoint( position, viewport, out var screen ) )
				continue;
			var distance = NVector2.DistanceSquared( screen, mouse );
			if ( distance < bestDistance )
				(best, bestDistance) = (index, distance);
		}
		return best ?? nodes[0];
	}

	/// <summary>
	/// The coaster tool's HUD side: the build buttons place pylons on a click and show the cursor, the other buttons pick a pylon up on a press, follow the pointer while it is held and settle
	/// on the release; a right click or Escape leaves.
	/// </summary>
	// [EXT:coaster-tool-controls] Backspace deletes the last pylon (0x444f30 without a refund): the original reaches that routine only through its undo list (0x52fe50 for tool mode 0x15, called by the ride removal 0x527ee0) and no key or button was found that calls it; the click on the chain start closes the circuit and a second click completes it (EXT coaster-completion-click, CoasterBuilder.Complete)
	private bool UpdateCoasterTool( UiContext context, UiInput input, bool inPark, bool paused )
	{
		CoasterGhost = null;
		level.CoasterPreview = null;
		if ( !level.IsCoasterToolActive || level.CoasterTool is not { } builder || level.CoasterPanel is not { } panel )
			return false;
		if ( paused )
		{
			// a drag that is let go while the game is paused ends there (a move goes back, a height settles its money); [EXT:online-coop-commands] a guest's press that waits keeps the release
			// for the drag its outcome starts (C3c-2)
			if ( input.LeftReleased )
				EndCoasterGesture();
			return false;
		}
		// Backspace waits for the drag: it would delete the pylon the drag holds ([EXT:online-coop-commands] each one a command; a guest's goes to the host one at a time, C3c-2)
		for ( var index = 0; index < input.Backspaces && !panel.IsDragging && level.CoasterToolStation is { } station; index++ )
		{
			if ( !panel.SendsToHost )
				level.Commands.Issue( new RemoveLastCoasterPylonCommand( station.Runtime.AttractionId ) );
			else if ( !level.SendCoasterBackspace() )
				break;
		}
		if ( parkRightClick )
		{
			level.ExitCoasterTool();
			return true;
		}
		var pointer = OriginalPixels( context, input.Mouse );
		var consumed = false;
		if ( inPark )
			panel.Hover( cursorX, cursorY );
		else
			panel.HoverNothing();
		// [EXT:online-coop-commands] a press made while a guest's request of the panel waited, at this frame's start (before its answers) or now (before the press sends a drag's own begin): its
		// release is nobody's (C3c-2), wherever it is let go; a press in the park while the panel still waits asks the player to wait
		if ( input.LeftPressed )
		{
			coasterPressWhileWaiting = frameCoasterWaiting || panel.IsWaiting;
			if ( inPark && panel.IsWaiting )
				level.NoteWaitingClick();
		}
		if ( panel.IsDragging || panel.PressWaits )
		{
			// [BIN:TPW-EXE:0x443371 tilt drag yaw] the camera yaw (0x790a38) minus the node's yaw turns the tilt drag; [EXT:online-coop-commands] a guest's press that waits keeps the pointer for
			// the drag its outcome starts (C3c-2)
			panel.Drag( pointer, level.CoasterCameraYaw, inPark ? (cursorX, cursorY) : null );
			if ( inPark && input.LeftPressed )
				coasterPress = true;
			consumed = true;
		}
		else if ( inPark && input.LeftReleased && panel.IsBuilding )
		{
			if ( !coasterPressWhileWaiting )
			{
				// [BIN:TPW-EXE:0x5263df coaster build click] a build click is the tool object's release (0x524960, mode 0x15): the pylon is placed, the click sounds and the help follows, at the cell under the pointer then; the press does nothing
				var closing = level.CoasterTool is { } tool && tool.Track.IsChainStart( cursorX, cursorY ) && !tool.Track.IsClosed;
				// [EXT:online-coop-commands] a guest's click goes to the host, and its result is said when it comes back (C3c-2); a click while its request waits went nowhere
				var clicked = true;
				if ( panel.SendsToHost )
					clicked = level.SendCoasterClick( cursorX, cursorY, closing );
				else
					ReportCoasterResult( level.CoasterClick( cursorX, cursorY ), closing );
				if ( clicked )
				{
					// [BIN:TPW-EXE:0x52644c coaster tool click] the tool's click handler 0x524960 in mode 0x15 posts help 0xCD (205) after every click's action (the release 0x446300 with 0 for the edit buttons)
					PostToolHelp( AdvisorToolHelp.CoasterToolClick );
					PlayCoasterToolClick( cursorX, cursorY );
				}
			}
			consumed = true;
		}
		else if ( inPark && input.LeftPressed )
		{
			if ( panel.IsBuilding )
			{
				// the build buttons' click is the release's; the press is taken so no other tool or pick answers it
				coasterPress = true;
				consumed = true;
			}
			else
			{
				// the move, height, rotate and tilt buttons pick the pylon up now; the stack and demolish buttons act on the release
				if ( !coasterPressWhileWaiting && PickCoasterNode( panel, context, input.Mouse ) is int node )
					panel.Press( node, pointer, cursorX, cursorY );
				coasterPress = true;
				consumed = true;
			}
		}
		if ( input.LeftReleased && coasterPressWhileWaiting )
		{
			// [EXT:online-coop-commands] the release of a press made while the panel waited is nobody's (C3c-2)
			consumed = true;
		}
		else if ( input.LeftReleased && (panel.IsDragging || (coasterPress && !panel.IsBuilding)) )
		{
			var node = inPark ? PickCoasterNode( panel, context, input.Mouse ) : null;
			var result = panel.Release( node, inPark ? (cursorX, cursorY) : null );
			if ( result == CoasterPanelResult.Reopened )
				PostMessage( strings.Help( 0x125 ) );
			// [EXT:online-coop-commands] a guest's swap or cut while another request of its panel waits went nowhere (C3c-2)
			if ( result == CoasterPanelResult.Waiting )
				level.NoteWaitingClick();
			if ( inPark )
			{
				PostToolHelp( AdvisorToolHelp.CoasterToolClick );
				PlayCoasterToolClick( cursorX, cursorY );
			}
			consumed = true;
		}
		// [BIN:TPW-EXE:0x446300 coaster edit release] in the edit modes the release only settles what a press picked up (0x446300(0)): one that picked nothing, a press off the grid let go on it, is the tool's and no click of the level's
		if ( input.LeftReleased && !panel.IsBuilding )
			consumed = true;
		if ( input.LeftReleased )
			coasterPress = coasterPressWhileWaiting = false;
		if ( panel.IsBuilding && panel.Cursor is { } cursor )
		{
			CoasterGhost = (cursor.X, cursor.Y, cursor.Result, cursor.Cost);
			// the build cursor's pending link: the track through a cursor node at the cell (a click on the chain start closes instead and shows none)
			if ( !cursor.Closing )
				level.CoasterPreview = new CoasterPreview( cursor.X, cursor.Y, cursor.Result == CoasterPlaceResult.Ok, builder.Height, builder.LoopMode, builder.FirstOfPair );
		}
		return consumed;
	}

	/// <summary>
	/// The park's left gesture ends without its release reaching the tool (a pause's release, the pointer suspended, a release that was lost): a drag settles where it stands (a move goes back, a
	/// height settles its money), [EXT:online-coop-commands] a guest's press that waits keeps a release over nothing for the drag its outcome starts, and a press made while the panel waited ends
	/// with no release at all (C3c-2).
	/// </summary>
	private void EndCoasterGesture()
	{
		if ( level.CoasterPanel is { } panel && (panel.IsDragging || panel.PressWaits) && !coasterPressWhileWaiting )
			panel.Release( null, null );
		coasterPress = coasterPressWhileWaiting = false;
	}

	/// <summary>
	/// The coaster tool's click sound: the path tool's laying cue (<see cref="GameAudio.CoasterToolClickEvent"/>) at the clicked cell, for every release in tool mode 0x15
	/// (a build click and the end of an edit-button action alike), before the action's result is known.
	/// </summary>
	// [BIN:TPW-EXE:0x526423 coaster click sound] 0x524960 submits cat_ui 0x65 at (x*10, 0, z*10) in mode 0x15 right before the coaster release 0x446300 and the help 0xCD
	private static void PlayCoasterToolClick( int cellX, int cellY ) => GameAudio.PlayUiAt( GameAudio.CoasterToolClickEvent, GameAudio.CellSoundPosition( cellX, cellY ) );

	private void ReportCoasterResult( CoasterPlaceResult result, bool closing = false )
	{
		if ( result == CoasterPlaceResult.Ok )
			return;
		// [EXT:coaster-closing-check] a circuit that stays open says why: a pylon still marked invalid (a closing or completing click), or a closing link from the last pylon that fails
		PostMessage( strings.Extra( result switch
		{
			CoasterPlaceResult.NotEnoughMoney => OpenTpwText.NotEnoughMoney,
			CoasterPlaceResult.InvalidNodes => OpenTpwText.CoasterInvalidPylons,
			_ when closing => OpenTpwText.CoasterCannotClose,
			_ => OpenTpwText.CannotBuildHere,
		} ) );
	}

	/// <summary>The coaster tool's cost beside the cursor (the placement area and the cursor cell are the blueprint markers, <see cref="UpdateBlueprint"/>).</summary>
	private void DrawCoasterGhost( UiContext context )
	{
		if ( !level.IsCoasterToolActive || !Status.HasEconomy )
			return;
		var viewport = new NVector2( context.Canvas.Width, context.Canvas.Height );
		long? cost = level.CoasterPanel?.DragCost;
		if ( cost == null && CoasterGhost is { } ghost && ghost.Cost > 0 )
			cost = ghost.Cost;
		if ( cost is not { } amount || amount == 0 )
			return;
		var cell = level.CoasterPanel is { DraggedNode: int dragged } drag ? (drag.Track.Nodes[dragged].X, drag.Track.Nodes[dragged].Y) : CoasterGhost is { } at ? (at.X, at.Y) : ((int, int)?)null;
		if ( cell is not { } position || !level.TryProjectCell( position.Item1, position.Item2, viewport, out var centre ) )
			return;
		var size = 14 * context.Canvas.TextScale;
		var text = string.Format( System.Globalization.CultureInfo.InvariantCulture, "{0}{1:#,0}", amount < 0 ? strings[UIStrings.NegativeDollar] : strings[UIStrings.Dollar], Math.Abs( amount ) ).Replace( "  ", " " );
		context.DrawText( context.Fonts.Small, text, new UiRect( centre.X + size, centre.Y - size * 2, 300 * context.Canvas.TextScale, size * 4 ), UiColors.Highlight, UiAlign.Left );
	}
}
