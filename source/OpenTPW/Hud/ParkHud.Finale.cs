using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The winning screen of the end-of-park finale (docs/reverse/INSTANT-ACTION-FINALE.md): the camera rides End's camera seat, the HUD is hidden, the language's
/// "congratulations" banner squeezes, rises, spins and stretches over the park, and only Escape or a late right click leave it.
/// </summary>
public sealed partial class ParkHud
{
	private UiScreen? finaleScreen;
	private float finaleOpenedAt;
	/// <summary>Whether this frame's finale update has already observed the camera for the ride view's static.</summary>
	private bool finaleObservedCamera;

	/// <summary>The name of the Instant Action completion message's box (it pauses the park while open, as the original's message boxes do).</summary>
	public const string WinningMessageScreen = "winningMessage";

	/// <summary>The finale owns input until it is dismissed, without stopping the simulation that runs its looping script.</summary>
	// [BIN:TPW-EXE:0x0048AE70 winning screen] exits the prior camera mode (0x42AE70, 0x42A190), rides End's camera seat (0x4E15B0), disables the key tables (0x40CFA0), hides the HUD and
	// camcorder windows (message 6), constructs the full-screen window 0x7472 with the banner table 0x74FAF0 and installs 0x48A970 as its focused handler
	private bool UpdateResearchFinale( UiContext context, UiInput input, bool? mouseCaptured, bool? keyboardCaptured, bool? textCaptured )
	{
		finaleObservedCamera = false;
		if ( !level.ResearchFinaleActive )
		{
			if ( finaleScreen == null )
				return false;
			LeaveWinningScreen();
			return true; // an externally removed End does not leak this frame's click to the park
		}
		// A restored End (a park saved while its winning screen was open) enters the same screen as a new one: the original never saves its handle (0x7CF4F4 is written only by 0x516B00 and 0x516C10)
		if ( finaleScreen == null || finaleScreen.IsRemoved )
			OpenWinningScreen( context );
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
			input = input with { Mouse = new System.Numerics.Vector2( -1 ), LeftDown = false, LeftPressed = false, LeftReleased = false, RightPressed = false, RightReleased = false, RightClicked = false, Wheel = 0 };
		if ( keyboardCaptured.Value || textCaptured.Value )
			input = input with { Pressed = UiKeys.None, Text = "", Backspaces = 0 };
		// [BIN:TPW-EXE:0x0048A99E winning Escape] key 0x1B with modifier bits 0xFF0000 clear immediately calls 0x516C10.
		// [BIN:TPW-EXE:0x0048AADB winning right click] RMBCancelOn and elapsed milliseconds strictly above 2000 gate the same cleanup.
		var back = Input.ModifierMaskDown == 0 ? input.Pressed : input.Pressed & ~UiKeys.Back;
		Stack.Update( context, input with { Pressed = back, RightPressed = FinaleRightClickAllowed( input.RightPressed, GameOptions.Current.RmbCancel, context.Time - finaleOpenedAt ) } );
		UpdateCursor( input, overUi: true, mouseCaptured.Value );
		if ( finaleScreen != null )
		{
			// [BIN:TPW-EXE:0x0054FAF7 static countdown] the winning camera is the seat camera (mode 4), so entering it shows the eye camera's static like a ride does
			PrepareRideStatic( context );
			finaleObservedCamera = true;
		}
		// All input belongs to this screen, including Pause, the park-door shortcuts and the click that dismisses it.
		return true;
	}

	internal static bool FinaleRightClickAllowed( bool pressed, bool enabled, float elapsed ) => pressed && enabled && elapsed > 2f;

	private void OpenWinningScreen( UiContext context )
	{
		StopRide();
		CancelCamcorder();
		SyncRideScreen(); // the camcorder window goes with the ride view it served
		RetireArmedTools();
		StopResearchSounds();
		SetInfoArm( false );
		CloseCameraPanel();
		if ( ParkFinale.Find( level.Objects ) is { } end )
			level.EnterWinningView( end );
		finaleOpenedAt = context.Time;
		finaleScreen = BuildFinaleScreen( context );
		Stack.Push( finaleScreen );
	}

	private UiScreen BuildFinaleScreen( UiContext context )
	{
		var screen = new UiScreen( "researchFinale" ) { Modal = true, HidesBelow = true };
		void Dismiss()
		{
			if ( screen.IsRemoved || !ReferenceEquals( Stack.Top, screen ) )
				return;
			var instantAction = level.Park?.Economy.Mode == ParkGameMode.InstantAction;
			// a refused exit (no finale left to dismiss) leaves the screen to the next update, which retires it if End is gone, and shows no message
			if ( !level.Commands.Issue( new DismissFinaleCommand() ).Accepted )
				return;
			LeaveWinningScreen();
			// [BIN:TPW-EXE:0x0048AA41 Instant Action message] after the cleanup the handler shows UITEXT 471 (OpenTPW's CongratulationsInstantAction) in a message box (0x47F020) whose only button is the cross, without an action; in game type 0 it shows nothing
			if ( instantAction )
				Stack.Push( WinningMessage() );
		}
		screen.Back = Dismiss;
		var banner = OriginalLayoutTables.WinningScreen.Root;
		// [BIN:TPW-EXE:0x0074FAF0 winning banner] the window 0x321 at (102,536)-(1946,940) draws the model s_congrats, the language folder's congrats.MD2; 0x48AF63 disables it (virtual +0x14 = 0x65DA8D(0), flag 2; its model has only the root frame, so nothing changes in its drawing) and 0x48AFA7 starts its animation
		screen.Add( new UiAnimatedModel
		{
			Id = "winningBanner",
			Model = banner.Model!,
			Bounds = banner.Rect,
			Anchor = UiAnchor.Center,
			Pivot = UiAnimatedModel.PivotOf( banner.Rect ),
			Animation = new UiWindowAnimation( UiWindowAnimation.WinningBanner, UiWindowAnimation.WinningBannerInterval, (long)Math.Floor( context.Time * 1000.0 ) )
		} );
		// the park view draws the eye camera's static beneath the windows
		screen.DrawBackground = DrawRideStatic;
		return screen;
	}

	/// <summary>The Instant Action completion message: the original's message box with the text and the cross button, which closes it.</summary>
	private UiScreen WinningMessage()
	{
		UiScreen? screen = null;
		screen = UiDialogs.OriginalMessageBox( WinningMessageScreen, () => strings[UIStrings.CongratulationsInstantAction],
			() => Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) ),
			(UiDialogs.MessageBoxButton.Exit, null, null) );
		return screen;
	}

	/// <summary>
	/// Closes the winning screen and leaves the winning camera: the dismissal's cleanup and an End removed in another way alike.
	/// </summary>
	// [BIN:TPW-EXE:0x0048A9CE winning dismissal] the handler calls 0x516C10 (End destroyed, its handle cleared, the seat camera left through 0x42A190), shows the HUD again and keeps the camcorder hidden (message 6),
	// re-enables the key tables (0x40CF60), clears the winning flag 0x7C24D0 and gives the focus back (0x4862A0); no player record is written here (0x5B0CA0 ran at the completion)
	private void LeaveWinningScreen()
	{
		RemoveFinaleScreen();
		if ( level.RideView?.Ride is { } ride && (ride.IsDeleted || ParkFinale.IsFinaleObject( ride )) )
			StopRide();
	}

	private void RemoveFinaleScreen()
	{
		var screen = finaleScreen;
		finaleScreen = null;
		if ( screen != null )
			Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) );
	}
}
