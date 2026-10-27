using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The camera button's slide-out panel: the original opens a panel with the camcorder button (b_1person, help 479, key C) and the postcard button
/// (b_postcard, help 480, Ctrl-P) from the HUD's camera button (help 474, "use camcorder mode or send a postcard").
/// </summary>
public sealed partial class ParkHud
{
	// [BIN:TPW-EXE:0x751720 camera panel] panel id 0x62 (466,1316)-(785,1469) with b_1person 0x63 (474,1316)-(627,1469) help 479 and b_postcard 0x64 (632,1316)-(785,1469) help 480
	// [DATA:video 8:20-8:40] the green panel right of the HUD shows those two buttons at the table's positions
	// [BIN:TPW-EXE:0x751720 no view button] the panel's table holds only the panel and its two buttons; no control of the HUD arm (0x7529dc), the camera panel, the ride, shop, sideshow, visitor or employee windows (0x755150, 0x755908, 0x755e80, 0x7575e0, 0x756f48) changes the zoom or the yaw: the locate jumps (0x4867b0, 0x486860, 0x4868d0, the map click 0x5f1050) call 0x42aab0 with the zoom and yaw arguments null and move only the target, so the original has no view-cycle button and OpenTPW has none either
	// [APPROX:UI-042] a left click on the camcorder button only arms it (the walk starts on the next click, as with the C key); the postcard window (8:42) is the host composer
	private static readonly LayoutTable CameraPanelTable = OriginalLayoutTables.CameraPanel;
	private static readonly string[] CameraPanelIds = { "cameraPanel", "cameraCamcorder", "cameraPostcard" };

	/// <summary>The camera panel's advisor help (0x131): posted when the panel opens, withdrawn when it is put away.</summary>
	// [BIN:TPW-EXE:0x498bb0 camera open] the camera button's press (HUD case 0x27 with the toggle down, 0x4a0840) puts panel 33 into the arm (0x4a2590) and posts help 0x131 through 0x486b00;
	// its release (0x498bd0) puts the panel away (0x4a25f0) and withdraws the help (0x486b40), as do the panel's camcorder (99) and postcard (100) buttons (0x498ad0)
	public const int CameraPanelHelp = 0x131;

	/// <summary>True while the camera button's panel is out or sliding out.</summary>
	public bool CameraPanelOpen { get; private set; }

	/// <summary>The panel's slide: 0 when it is in, <see cref="ArmSlide.Steps"/> when it is out.</summary>
	internal ArmSlide CameraSlide => cameraSlide ??= new();
	private ArmSlide? cameraSlide;

	private void BuildCameraPanel()
	{
		var root = CameraPanelTable.Root.Rect;
		var panel = new UiRect( root.X - 12, root.Y - 14, root.Width + 24, root.Height + 28 );
		// [BIN:TPW-EXE:0x4a15c8 arm panel depth] after attaching the panel to the arm (0x65f447) the drawer lowers the panel's depth by 3 (0x65f16b), so it slides out
		// from under the HUD; OpenTPW adds its elements under the main panel, which also keeps the HUD's buttons on top of a panel that is still sliding
		var mainPanel = hud.Find( "mainPanel" );
		T Add<T>( T element ) where T : UiElement
		{
			element.Anchor = UiAnchor.BottomLeft;
			element.Visible = false;
			return hud.InsertBefore( mainPanel, element );
		}
		Add( new UiModelImage { Id = "cameraPanel", Model = "panel", Frame = () => 1, Bounds = panel } );
		var camcorder = CameraPanelTable.Model( "b_1person" );
		var postcard = CameraPanelTable.Model( "b_postcard" );
		Add( new UiButton { Id = "cameraCamcorder", Model = camcorder.Model, Help = strings.Help( camcorder.Help ), Clicked = ArmCamcorderFromPanel, Bounds = camcorder.Rect } );
		Add( new UiButton { Id = "cameraPostcard", Model = postcard.Model, Help = strings.Help( postcard.Help ), Clicked = OpenPostcardFromPanel, Bounds = postcard.Rect } );
		cameraPanelRects = CameraPanelIds.Select( id => hud.Find( id )!.Bounds ).ToArray();
	}

	private UiRect[]? cameraPanelRects;

	/// <summary>The camera button: opens the panel, or closes it. The panel takes the slot of the message panel, which closes.</summary>
	public void ToggleCameraPanel()
	{
		if ( CameraPanelOpen )
		{
			CloseCameraPanel();
			return;
		}
		CameraPanelOpen = true;
		if ( OpenedMessage != null )
			CloseMessagePanel();
		CameraSlide.Open();
		host.HelpPosted?.Invoke( CameraPanelHelp );
	}

	public void CloseCameraPanel()
	{
		if ( !CameraPanelOpen )
			return;
		CameraPanelOpen = false;
		CameraSlide.Close();
		host.HelpWithdrawn?.Invoke( CameraPanelHelp );
	}

	private void UpdateCameraPanel( float delta )
	{
		if ( CameraPanelOpen && (IsRiding || level.FreeWalk != null) )
			CloseCameraPanel();
		CameraSlide.Update( delta );
		var offset = CameraPanelOffset( CameraSlide.Step );
		var rects = cameraPanelRects ?? Array.Empty<UiRect>();
		for ( var index = 0; index < CameraPanelIds.Length && index < rects.Length; index++ )
		{
			if ( hud.Find( CameraPanelIds[index] ) is not { } element )
				continue;
			var rect = rects[index];
			element.Bounds = rect with { X = rect.X + offset };
			element.Visible = CameraSlide.Step > 0 && ArmChildShown( element.Bounds );
		}
	}

	/// <summary>The panel's horizontal offset from its table place at slide step <paramref name="step"/>: it starts a panel width and 50 units left of the arm's left edge.</summary>
	// [BIN:TPW-EXE:0x4a165d arm panel start] the drawer places a new panel left of the arm: its left at the arm's left - the panel's width - 50 (0x4a1631); the slide then moves it a
	// tenth of the travel each step
	// [BIN:TPW-EXE:0x4a1631 panel gap] the 50 is a horizontal gap and the width is the panel's own: left = the arm's left - the panel's width - 50, centred vertically on the arm (0x4a163b-0x4a1647); the arm's tick then moves it right by (the HUD window's right - the arm's left + the panel's width + 100) / 10 each step (0x4a16e5, 0x4a19e7)
	// [APPROX:UI-042] the slide ends at the table rectangle and keeps the table's vertical place: the end of the original's slide and the runtime rectangles of the HUD window and the arm are not resolved
	internal static float CameraPanelOffset( int step )
	{
		var table = CameraPanelTable.Root.Rect;
		var start = HudArmLeft - table.Width - 50;
		var travel = table.X - start;
		return -travel * (ArmSlide.Steps - step) / ArmSlide.Steps;
	}

	/// <summary>The left edge of the HUD arm (control 0x21, table 0x7529dc).</summary>
	private static readonly float HudArmLeft = OriginalLayoutTables.HudArm.Control( 0x21 ).Rect.X;

	/// <summary>Whether a panel control is drawn: only once its right edge has passed the arm's left edge, so nothing shows behind the HUD.</summary>
	// [BIN:TPW-EXE:0x4a1440 arm children] each child of the sliding panel is shown only while its right edge is right of the arm's left edge (0x4a1485)
	internal static bool ArmChildShown( UiRect rect ) => rect.Right > HudArmLeft;

	private void ArmCamcorderFromPanel()
	{
		CloseCameraPanel();
		ArmCamcorder();
	}

	private UiScreen AlertBox()
	{
		UiScreen? screen = null;
		screen = UiDialogs.OriginalMessageBox( AlertBoxScreen, () => OnlineStrings.Error( 0x4E, strings.Extra( OpenTpwText.NotAvailable ) ),
			() => Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) ),
			(UiDialogs.MessageBoxButton.Exit, null, null) );
		return screen;
	}

	private void OpenPostcardFromPanel()
	{
		CloseCameraPanel();
		if ( IsRiding || Stack.Screens.Count != 1 || Camera.Mode is not ParkCameraMode || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects
			|| level.CellTool.IsActive || level.IsCoasterToolActive )
			return;
		if ( !FeatureGates.Sharing || host.OpenPostcard == null )
		{
			// [BIN:TPW-EXE:0x4a8c89 postcard refusal] when the composer 0x4A8C00 cannot be built (the check 0x5B6460 returns 0) it queues ERRORMSG 0x4E through 0x47F410: the alert queue (0x47F620 -> 0x47F020, one button) shows it and, like every generic message box, pauses the park while it is open
			Stack.Push( AlertBox() );
			return;
		}
		host.OpenPostcard( Stack );
	}
}

/// <summary>
/// The HUD arm's slide (the drawer procedure of arm control 0x21): a panel goes out or in over ten steps of a 20 ms timer, with the drawer sounds.
/// </summary>
// [BIN:TPW-EXE:0x4a14f0 arm drawer] message 10 starts a slide out (state 2, ten steps in +0x130, timer 0x80080 every 0x14 ms, 0x4a1556/0x4a15a7) and plays cat_ui 0x1C (0x4a1742);
// message 0xB starts a slide in (state 3) and plays cat_ui 0x1C (0x4a187a); each timer message moves the arm a step until the ten are done (0x4a19d2), and a finished slide in
// hides the panel and plays cat_ui 0xC2 (0x4a1947); the arm control's procedure is set by the HUD builder (0x4a2382)
internal sealed class ArmSlide
{
	public const int Steps = 10;
	public const float StepSeconds = 0.02f;
	private float clock;

	/// <summary>0 when the panel is in, <see cref="Steps"/> when it is out.</summary>
	public int Step { get; private set; }
	public bool Out { get; private set; }

	public void Open()
	{
		if ( Out )
			return;
		Out = true;
		clock = 0;
		GameAudio.PlayUi( GameAudio.DrawerOpenEvent );
	}

	public void Close()
	{
		if ( !Out )
			return;
		Out = false;
		clock = 0;
		GameAudio.PlayUi( GameAudio.DrawerOpenEvent );
	}

	/// <summary>Advances the slide by <paramref name="delta"/> seconds; true when a slide in finished in this update.</summary>
	public bool Update( float delta )
	{
		var target = Out ? Steps : 0;
		if ( Step == target )
			return false;
		clock += Math.Max( 0, delta );
		while ( clock >= StepSeconds && Step != target )
		{
			clock -= StepSeconds;
			Step += Out ? 1 : -1;
		}
		if ( Step != target )
			return false;
		clock = 0;
		if ( Out )
			return false;
		GameAudio.PlayUi( GameAudio.DrawerCloseEvent );
		return true;
	}
}
