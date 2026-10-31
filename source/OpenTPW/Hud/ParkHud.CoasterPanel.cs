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
	/// <summary>The panel window's root rectangle of table 0x751530 (control 0x17) and the two boxes on it; the HUD arm's drawer carries the whole panel out from its table place (<see cref="ArmDrawer.Shift"/>).</summary>
	// [BIN:TPW-EXE:0x751530 coaster panel table] root 0x17 (859, 916)-(1534, 1190) with !f_plain boxes 0x22 (859..1003) and 0x18 (1037..1407) and the nine buttons 0x19..0x21; the table holds no window position
	// [BIN:TPW-EXE:0x4988c0 coaster panel slide] the tool's entry (0x4988c0, from the ride window's track button 0x4af200) puts the panel into the HUD arm like the camera and message panels: 0x4a25f0(1), then 0x4a2590 (message 0xb retracts the panel the arm carries, message 10 starts the slide out of the panel window 0x7ca1c8 that 0x498790 built from the table with the handler 0x4982a0); the drawer 0x4a14f0 places it at the arm's left less its width less 50, centred on the arm, and moves it right by (the HUD window's right - the arm's left + its width + 100) / 10 per 20 ms tick, so the 675 x 274 panel ends at left 486, top 1145 (the -372, +232 measured from the video are these -373, +229 within a pixel or three); the tool's exit (0x4989d0) retracts it (0x4a25f0(0))
	// [BIN:TPW-EXE:0x4a17dc arm takeover] a slide out that starts while the arm carries another panel first sends that panel message 0x1001d8, which for the coaster panel ends the tool (0x4982a0: 0x446fa0(0, 4), 0x497f00, 0x4474d0(0, 0)): another panel taking the arm ends the coaster tool at once (YieldCoasterPanelToArm)
	private static readonly UiRect CoasterRoot = Rect( 859, 916, 1534, 1190 );
	private static readonly UiRect CoasterLeftBox = Rect( 859, 916, 1003, 1190 );
	private static readonly UiRect CoasterMiddleBox = Rect( 1037, 916, 1407, 1190 );

	/// <summary>The panel's slide: 0 when it is in, <see cref="ArmSlide.Steps"/> when it is out.</summary>
	internal ArmSlide CoasterSlide => coasterSlide ??= new();
	private ArmSlide? coasterSlide;
	private bool coasterLoopButtonShown;
	/// <summary>The arm's retract button put the coaster panel away while the tool stays open (0x4a187a: message 0xb only slides the panel in; it does not end the tool).</summary>
	private bool coasterPanelRetracted;
	private readonly List<UiElement> coasterArmElements = new();
	private readonly List<UiElement> coasterBoxElements = new();

	private static UiRect CoasterRect( float left, float top, float right, float bottom ) => Rect( left, top, right, bottom );

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
		// [BIN:TPW-EXE:0x4a15c8 arm panel depth] as the camera panel (BuildCameraPanel): the panel lies under the main panel while the drawer carries it out, and the arm's own art grows out from under the HUD
		var mainPanel = hud.Find( "mainPanel" );
		T Add<T>( T element ) where T : UiElement
		{
			element.Anchor = UiAnchor.BottomLeft;
			element.Visible = false;
			return mainPanel == null ? hud.Add( element ) : hud.InsertBefore( mainPanel, element );
		}
		// the panel's backdrop takes the clicks of the area it covers, so a click beside a button does not reach the park: the arm's bar under the panel
		var shield = Add( new UiLabel { Id = "coasterPanelShield", Text = () => "", Bounds = ArmPieces( (int)CoasterRoot.Width, 0 ).Bar } );
		var (bar, pane, handle) = ArmPieces( (int)CoasterRoot.Width, 0 );
		coasterArmElements.Add( Add( new UiModelImage { Id = "coasterArm", Model = "panel", Frame = () => 0, Bounds = bar } ) );
		coasterArmElements.Add( Add( new UiModelImage { Id = "coasterArmPane", Model = ArmPaneModel, Frame = () => 0, Bounds = pane } ) );
		coasterArmElements.Add( Add( new UiModelImage { Id = "coasterArmHandle", Model = ArmHandleModel, Frame = () => 0, Bounds = handle } ) );
		coasterBoxElements.Add( Add( new UiModelImage { Id = "coasterLeftBox", Model = "!f_plain", Frame = () => 0, Bounds = CoasterLeftBox } ) );
		coasterBoxElements.Add( Add( new UiModelImage { Id = "coasterMiddleBox", Model = "!f_plain", Frame = () => 0, Bounds = CoasterMiddleBox } ) );
		foreach ( var spec in CoasterButtons )
		{
			var button = Add( new UiButton
			{
				Id = $"coaster{spec.Button}",
				Model = spec.Model,
				Help = strings.Help( spec.Help ),
				// Coaster panel table 0x751530 is not generated. These nine toggles are its radio group (type 8, style 0x11), so they play 0xBD.
				ClickSound = GameAudio.SelectClickEvent,
				Clicked = () => SelectCoasterButton( spec ),
				Selected = () => level.CoasterPanel?.Selected == spec.Button,
				Bounds = spec.Rect
			} );
			coasterPanelElements.Add( button );
		}
		coasterPanelElements.Add( shield );
	}

	/// <summary>
	/// The arm takes another panel (the camera, message or locator panel starts to slide out): the panel it carries gets message 0x1001d8, which ends the coaster tool (0x4a17dc, 0x4982a0).
	/// </summary>
	private void YieldCoasterPanelToArm()
	{
		if ( level.IsCoasterToolActive )
			level.ExitCoasterTool();
	}

	/// <summary>Shows or hides the panel with the tool and sets which buttons are enabled (the enable mask <c>0x445910</c>); the arm's drawer slides it out and in.</summary>
	private void UpdateCoasterPanelElements( float delta )
	{
		// harnesses that build the HUD without its constructor have no panel buttons
		if ( coasterPanelElements == null || coasterPanelElements.Count != CoasterButtons.Length + 1 )
			return;
		var panel = level.IsCoasterToolActive ? level.CoasterPanel : null;
		// another panel that opened while the coaster panel is out takes the arm and ends the tool (0x4a17dc)
		if ( panel != null && CoasterSlide.Out && !coasterPanelRetracted && (OpenedMessage != null || CameraPanelOpen || Locator.Mode != LocatorMode.None) )
		{
			YieldCoasterPanelToArm();
			panel = null;
		}
		ReportCoasterPanelClosed( panel != null );
		panel?.Refresh();
		if ( panel == null )
			coasterPanelRetracted = false;
		// a closed panel whose slide rests at step 0 has nothing to move: its elements were hidden by the last update
		if ( panel == null && coasterPanelIdle && CoasterSlide.Step == 0 )
			return;
		// [BIN:TPW-EXE:0x4988c0 panel open] the tool's entry puts the panel into the arm (0x4a2590), which retracts the panel it carried (message 0xb); the tool's exit retracts it (0x4a25f0(0)), and so does the arm's retract button (message 0xb)
		if ( panel != null && !coasterPanelRetracted )
		{
			if ( !CoasterSlide.Out )
			{
				CloseMessagePanel();
				CloseCameraPanel();
				CloseLocator();
			}
			CoasterSlide.Open();
		}
		else
			CoasterSlide.Close();
		CoasterSlide.Update( delta );
		var step = CoasterSlide.Step;
		var (shiftX, shiftY) = ArmDrawer.Shift( CoasterRoot, step );
		UiRect Moved( UiRect rect ) => rect with { X = rect.X + shiftX, Y = rect.Y + shiftY };
		// the arm's own pieces grow with the slide (ArmDrawer.ArmRight); the shield is the bar under the panel
		var (bar, pane, handle) = ArmPieces( (int)CoasterRoot.Width, step );
		var arm = new[] { bar, pane, handle };
		for ( var index = 0; index < coasterArmElements.Count; index++ )
		{
			coasterArmElements[index].Bounds = arm[index];
			coasterArmElements[index].Visible = step > 0 && ArmChildShown( arm[index] );
		}
		for ( var index = 0; index < coasterBoxElements.Count; index++ )
		{
			coasterBoxElements[index].Bounds = Moved( CoasterBoxes[index] );
			coasterBoxElements[index].Visible = step > 0 && ArmChildShown( coasterBoxElements[index].Bounds );
		}
		coasterPanelIdle = panel == null && step == 0;
		coasterPanelElements[^1].Bounds = bar;
		coasterPanelElements[^1].Visible = step > 0 && panel != null;
		if ( panel != null )
			coasterLoopButtonShown = panel.LoopButtonShown;
		for ( var index = 0; index < CoasterButtons.Length; index++ )
		{
			var element = coasterPanelElements[index];
			var spec = CoasterButtons[index];
			element.Bounds = Moved( spec.Rect );
			// the loop button is hidden below ride level 1 (0x49880F, 0x4987f0) and nowhere else: a coaster without a loop pylon shows it too
			element.Visible = step > 0 && ArmChildShown( element.Bounds ) && (spec.Button != CoasterToolButton.Loop || coasterLoopButtonShown);
			element.Enabled = panel != null && panel.IsEnabled( spec.Button );
			// [BIN:TPW-EXE:0x4987f0 loop button art] the panel's refresh swaps the loop button's model for cb_trough (ui.wad) and its two hover help ids from the table's 565 to 0x134 (308) when the mask has
			// bit 0x200 (definition flag 0x100: the flume coasters), else to 0x132 (306): "select loop pylons" or "select splashdown pylons" with their pair notes
			if ( spec.Button == CoasterToolButton.Loop && panel != null && element is UiButton loopButton )
			{
				var trough = (panel.Builder.Ride.Definition.Flags & CoasterDefinition.TroughFlag) != 0;
				loopButton.Model = trough ? "cb_trough" : spec.Model;
				loopButton.Help = strings.Help( trough ? 0x134 : 0x132 );
			}
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

	private bool coasterPanelIdle;
	private static readonly UiRect[] CoasterBoxes = { CoasterLeftBox, CoasterMiddleBox };

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

	/// <summary>The pointer in whole pixels of the original's default 640 x 480 display mode, which the drag rates count in.</summary>
	// [BIN:TPW-EXE:0x42a760 mouse pixels] the mouse handler stores the pointer as integers of the selected display mode (0x790ac8 = hiword x * W / 0x800, 0x790acc = loword y * H / 0x600, W and H the words 0xfaa5c4 and 0xfaa5c0 that 0x46b600 takes from the screen mode: 160 x 120 up to 2048 x 1536, 640 x 480 by default) converted to floats (0x42a855..0x42a863); the coaster drags copy them (0x441b00, 0x441d68, 0x4420bd)
	// [EXT:coaster-drag-reference-mode] OpenTPW has no display mode, so the drags count the pixels of the original's default 640 x 480 mode on every screen: the canvas is scaled to 640 x 480 and the fraction dropped
	internal static NVector2 OriginalPixels( UiContext context, NVector2 mouse ) =>
		new( MathF.Truncate( mouse.X * 640f / Math.Max( 1f, context.Canvas.Width ) ), MathF.Truncate( mouse.Y * 480f / Math.Max( 1f, context.Canvas.Height ) ) );

	/// <summary>
	/// The pylon under the pointer: the pylon on the hovered cell, in a stack the piece whose projected position is nearest the pointer (the original picks the pylon model under the pointer).
	/// </summary>
	private int? PickCoasterNode( CoasterToolPanel panel, UiContext context, NVector2 mouse )
	{
		// the pylon model under the pointer; a coaster without drawn pylon models (no game data) is picked by its cell
		if ( level.PickCoasterPylon( panel.Builder.Ride, mouse, new NVector2( context.Canvas.Width, context.Canvas.Height ) ) is { Modelled: true } picked )
			return picked.Node is int model && model < panel.Track.Nodes.Count && !panel.Track.Nodes[model].IsPylonless ? model : null;
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

	private CoasterRide? coasterMarkedRide;

	/// <summary>Shows the tool's range marks on its ride and takes them off the ride that had them before.</summary>
	private void SetCoasterRangeMark( CoasterRide? ride, CoasterRangeMark? mark )
	{
		if ( coasterMarkedRide != null && coasterMarkedRide != ride )
			coasterMarkedRide.RangeMark = null;
		coasterMarkedRide = mark == null ? null : ride;
		if ( ride != null )
			ride.RangeMark = mark;
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
		{
			SetCoasterRangeMark( null, null );
			return false;
		}
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
			if ( panel.SendsToHost )
			{
				// [EXT:online-coop-undo] a guest's is the take-back of its own last pylon; with none of its own nothing goes, and it says so (C5-3)
				var sent = level.SendCoasterBackspace();
				if ( sent == CoasterBackspace.NotOwn )
					SayTakeBackNote( TakeBackCommand.NotOwnReason );
				if ( sent != CoasterBackspace.Sent )
					break;
			}
			else if ( panel.TakesBack )
			{
				// [EXT:online-coop-undo] the host's in a shared park takes back its own last pylon, decided by its journal as a guest's is (C5-3): a refusal keeps the pylon and says why, and one
				// refused for the claim ends the tool in the claim's words (the claim's end left the coaster's tool state already)
				if ( panel.TakeBackOwnPylon() is not { } taken )
				{
					SayTakeBackNote( TakeBackCommand.NotOwnReason );
					break;
				}
				if ( taken.Accepted )
					continue;
				if ( taken.Reason == CoopBuildRights.ClaimLostReason )
				{
					level.CloseCoasterToolLocally();
					SayGuestNote( OpenTpwText.CoopClaimLost );
					return true;
				}
				SayTakeBackNote( taken.Reason );
				break;
			}
			else
				level.Commands.Issue( new RemoveLastCoasterPylonCommand( station.Runtime.AttractionId ) );
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
		// [BIN:TPW-EXE:0x443340 demolish preview] a pending cut shows the range up to the hovered pylon, picked each frame while the second end is awaited
		if ( panel.CutStart != null )
			panel.HoverCut( inPark ? PickCoasterNode( panel, context, input.Mouse ) : null );
		SetCoasterRangeMark( level.CoasterToolStation?.Runtime.Coaster, panel.RangeMark );
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
