using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private UiScreen? finaleScreen;
	private float finaleOpenedAt;

	/// <summary>The finale owns input until it is dismissed, without stopping the simulation that runs its looping script.</summary>
	// [BIN:TPW-EXE:0x0048AE70 winning screen] exits the prior camera mode, hides the HUD, constructs a full-screen window and installs 0x48A970 as its focused handler.
	// RIDES-019 (see ParkFinale): the bounded screen hides the HUD and blocks park tools/camera input, but does not reproduce the winning-camera follow or original panel; the Resume Game button is an explicit OpenTPW fallback using an existing localized label.
	private bool UpdateResearchFinale( UiContext context, UiInput input, bool? mouseCaptured, bool? keyboardCaptured, bool? textCaptured )
	{
		if ( !level.ResearchFinaleActive )
		{
			if ( finaleScreen == null )
				return false;
			RemoveFinaleScreen();
			return true; // an externally removed End does not leak this frame's click to the park
		}
		if ( finaleScreen == null || finaleScreen.IsRemoved )
		{
			StopRide();
			CancelCamcorder();
			LeaveBuildTool();
			level.IsPlacing = level.IsRemovingObjects = false;
			if ( level.IsCoasterToolActive ) level.ExitCoasterTool();
			level.StaffPlacement?.Cancel();
			level.PatrolTool?.Cancel();
			level.BuyLandTool?.Cancel();
			level.AreaClearTool?.Cancel();
			level.PoolTool?.Cancel();
			level.CellTool.Cancel();
			StopResearchSounds();
			SetInfoArm( false );
			CloseCameraPanel();
			finaleOpenedAt = context.Time;
			finaleScreen = BuildFinaleScreen();
			Stack.Push( finaleScreen );
		}
		// Pending dialogs cannot keep a shortcut or a purchase control above the winning screen.
		Stack.RemoveWhere( screen => screen != hud && screen != finaleScreen );
		if ( Camera.Mode is ParkCameraMode camera )
		{
			camera.UiCapturesKeyboard = true;
			camera.TextEntryKeepsPointer = false;
			camera.EdgeScrollOverUi = false;
			camera.ModalOpen = true;
		}
		Input.TextEntryActive = false;
		if ( mouseCaptured == null || keyboardCaptured == null || textCaptured == null )
		{
			var io = ImGuiNET.ImGui.GetIO();
			mouseCaptured ??= io.WantCaptureMouse;
			keyboardCaptured ??= io.WantCaptureKeyboard;
			textCaptured ??= io.WantTextInput;
		}
		if ( mouseCaptured.Value )
			input = input with { Mouse = new System.Numerics.Vector2( -1 ), LeftDown = false, LeftPressed = false, LeftReleased = false, RightPressed = false, Wheel = 0 };
		if ( keyboardCaptured.Value || textCaptured.Value )
			input = input with { Pressed = UiKeys.None, Text = "", Backspaces = 0 };
		// [BIN:TPW-EXE:0x0048A99E winning Escape] key 0x1B with modifier bits 0xFF0000 clear immediately calls 0x516C10.
		// [BIN:TPW-EXE:0x0048AADB winning right click] RMBCancelOn and elapsed milliseconds strictly above 2000 gate the same cleanup.
		var back = Input.ModifierMaskDown == 0 ? input.Pressed : input.Pressed & ~UiKeys.Back;
		Stack.Update( context, input with { Pressed = back, RightPressed = FinaleRightClickAllowed( input.RightPressed, GameOptions.Current.RmbCancel, context.Time - finaleOpenedAt ) } );
		// All input belongs to this screen, including Pause, the park-door shortcuts and the click that dismisses it.
		return true;
	}

	internal static bool FinaleRightClickAllowed( bool pressed, bool enabled, float elapsed ) => pressed && enabled && elapsed > 2f;

	private UiScreen BuildFinaleScreen()
	{
		var screen = new UiScreen( "researchFinale" ) { Modal = true, HidesBelow = true };
		void Dismiss()
		{
			if ( screen.IsRemoved || !ReferenceEquals( Stack.Top, screen ) )
				return;
			level.Commands.Issue( new DismissFinaleCommand() );
			RemoveFinaleScreen();
		}
		screen.Back = Dismiss;
		// [EXT:instant-action-finale-dismissal] a visible, localized exit while the original winning panel remains unported; its normal UI button also supports Enter.
		screen.Add( new UiButton
		{
			Id = "resumePark", Text = () => strings[UIStrings.ResumeGame], Clicked = Dismiss,
			Bounds = new UiRect( 780, 1400, 488, 84 ), Anchor = UiAnchor.Bottom
		} );
		return screen;
	}

	private void RemoveFinaleScreen()
	{
		var screen = finaleScreen;
		finaleScreen = null;
		if ( screen != null )
			Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) );
	}
}
