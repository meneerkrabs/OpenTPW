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
	// [EXT:camera-view-cycle] the former camera button's Standard/Close/Overview view cycle is a third round button, b_camera, to the right of the panel's two; the original has none
	// [APPROX:UI-042] the panel appears without sliding out, and a left click on the camcorder button only arms it (the walk starts on the next click, as with the C key); the postcard window (8:42) is the host composer
	private static readonly LayoutTable CameraPanelTable = OriginalLayoutTables.CameraPanel;
	private static readonly string[] CameraPanelIds = { "cameraPanel", "cameraCamcorder", "cameraPostcard", "cameraViews" };
	private static readonly UiRect CameraPanelViewsRect = new( 790, 1316, 153, 153 );

	/// <summary>True while the camera button's panel is out.</summary>
	public bool CameraPanelOpen { get; private set; }

	private void BuildCameraPanel()
	{
		var root = CameraPanelTable.Root.Rect;
		var panel = new UiRect( root.X - 12, root.Y - 14, CameraPanelViewsRect.Right - root.X + 24, root.Height + 28 );
		T Add<T>( T element ) where T : UiElement
		{
			element.Anchor = UiAnchor.BottomLeft;
			element.Visible = false;
			return hud.Add( element );
		}
		Add( new UiModelImage { Id = "cameraPanel", Model = "panel", Frame = () => 1, Bounds = panel } );
		var camcorder = CameraPanelTable.Model( "b_1person" );
		var postcard = CameraPanelTable.Model( "b_postcard" );
		Add( new UiButton { Id = "cameraCamcorder", Model = camcorder.Model, Help = strings.Help( camcorder.Help ), Clicked = ArmCamcorderFromPanel, Bounds = camcorder.Rect } );
		Add( new UiButton { Id = "cameraPostcard", Model = postcard.Model, Help = strings.Help( postcard.Help ), Clicked = OpenPostcardFromPanel, Bounds = postcard.Rect } );
		Add( new UiButton { Id = "cameraViews", Model = "b_camera", Clicked = CycleCameraView, Bounds = CameraPanelViewsRect } );
	}

	/// <summary>The camera button: opens the panel, or closes it. The panel takes the slot of the message panel, which closes.</summary>
	public void ToggleCameraPanel()
	{
		CameraPanelOpen = !CameraPanelOpen;
		if ( CameraPanelOpen && OpenedMessage != null )
			CloseMessagePanel();
	}

	public void CloseCameraPanel() => CameraPanelOpen = false;

	private void UpdateCameraPanel()
	{
		if ( CameraPanelOpen && (IsRiding || level.FreeWalk != null) )
			CameraPanelOpen = false;
		foreach ( var id in CameraPanelIds )
			if ( hud.Find( id ) is { } element )
				element.Visible = CameraPanelOpen;
	}

	private void ArmCamcorderFromPanel()
	{
		CameraPanelOpen = false;
		ArmCamcorder();
	}

	private void OpenPostcardFromPanel()
	{
		CameraPanelOpen = false;
		if ( IsRiding || Stack.Screens.Count != 1 || Camera.Mode is not ParkCameraMode || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects
			|| level.CellTool.IsActive || level.IsCoasterToolActive )
			return;
		if ( !GameEditions.Capabilities.Sharing || host.OpenPostcard == null )
		{
			PostMessage( strings.Extra( OpenTpwText.NotAvailable ) );
			return;
		}
		host.OpenPostcard( Stack );
	}
}
