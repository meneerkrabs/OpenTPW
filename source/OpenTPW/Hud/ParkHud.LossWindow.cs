using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The lost park's screens (ECON-030): the bank's bankruptcy message makes the world state 4 and runs the loss window, which rides the camera seat of the park's gate under a full-screen window with the
/// language's "bankrupt" banner; Escape or a late right click leave it for the hidden-HUD overlay, where the park camera is the player's again, the HUD stays hidden and Escape opens the pause menu. A lost park
/// that is loaded takes the overlay at once, without the camera.
/// </summary>
public sealed partial class ParkHud
{
	private enum LossPhase { None, Window, Overlay }

	private LossPhase lossPhase;
	private UiScreen? lossScreen;
	private float lossOpenedAt;
	/// <summary>The time of the frame's context, for the dismissal that a key or a click runs from inside the screen stack.</summary>
	private float lossNow;
	/// <summary>The bank handler lost the park since the last frame: the loss window opens (a lost park that was loaded or restored raises no such event and takes the overlay).</summary>
	private bool lossPending;
	/// <summary>Whether this frame's loss window update has already observed the camera for the ride view's static.</summary>
	private bool lossObservedCamera;

	/// <summary>The names of the loss screens: the full-screen window of the loss and the overlay that replaces it.</summary>
	public const string LossWindowScreen = "lossWindow", LossOverlayScreen = "lossOverlay";

	private void HookLossEvents()
	{
		if ( level.Park != null )
			level.Park.EventRaised += OnLossEvent;
	}

	private void OnLossEvent( ParkEvent item )
	{
		if ( item.Kind == ParkEventKind.Bankrupt )
			lossPending = true;
	}

	/// <summary>
	/// The loss window and its overlay are this park's own, local presentation: not in a shared park, whose HUD keeps what it shows now, nor in a visit, which runs no park.
	/// [EXT:online-coop-money] a shared park and a visit keep the economy's refusals (ParkEconomy.IsBankrupt) and get no loss scene, so that every peer decides alike; a solo park gets the scene (user-approved 2026-10-30, ECON-030's co-op and visit part).
	/// </summary>
	private bool LossPresentationAllowed => level.Park != null && !level.IsReadOnlyVisit && level.CoopRole == CoopRole.None
		// Theme Park Inc has no bankrupt.MD2 banner (its levels carry none), so its lost park keeps what it did before; its own exe's loss scene is not read
		&& !level.Park.Economy.Rules.ThemeParkInc;

	/// <summary>The loss window is up (the gate camera and the full-screen banner).</summary>
	public bool LossWindowOpen => lossPhase == LossPhase.Window;

	/// <summary>The hidden-HUD overlay is up (the loss window was dismissed, or the park was loaded lost).</summary>
	public bool LossOverlayOpen => lossPhase == LossPhase.Overlay;

	/// <summary>The overlay is the top screen: the park camera takes the pointer and the keys, and no click reaches the park or the HUD.</summary>
	private bool LossOverlayOnTop => lossPhase == LossPhase.Overlay && lossScreen != null && ReferenceEquals( Stack.Top, lossScreen );

	/// <summary>
	/// Brings the loss screens in step with the park: a loss opens the window, a lost park found on the first frame (loaded) opens the overlay, a park that is no longer lost (another park was loaded into this level)
	/// closes them. True while the loss window owns the frame.
	/// </summary>
	private bool UpdateLossScene( UiContext context, UiInput input, bool? mouseCaptured, bool? keyboardCaptured, bool? textCaptured )
	{
		lossObservedCamera = false;
		lossNow = context.Time;
		var lost = LossPresentationAllowed && level.Park!.Economy.IsBankrupt && !level.ResearchFinaleActive;
		if ( !lost )
		{
			lossPending = false;
			if ( lossPhase != LossPhase.None )
			{
				LeaveLossScene();
				return true; // an externally replaced park does not leak this frame's click to the new one
			}
			return false;
		}
		if ( lossPhase == LossPhase.None )
		{
			// [BIN:TPW-EXE:0x005168F0 park lost] the bank's message sets the world state 4 and calls 0x481B50 -> 0x48AC40, the loss window; [BIN:TPW-EXE:0x004815E0 level load] a loaded park whose world state is 4 runs 0x48ADB0 instead, the overlay without the camera
			var window = lossPending;
			lossPending = false;
			if ( !window )
			{
				OpenLossOverlay( context.Time );
				return false;
			}
			OpenLossWindow( context );
		}
		if ( lossPhase != LossPhase.Window )
			return false;
		if ( lossScreen == null || lossScreen.IsRemoved )
		{
			OpenLossOverlay( context.Time );
			return false;
		}
		// Pending dialogs cannot keep a shortcut or a purchase control above the loss window.
		Stack.RemoveWhere( screen => screen != hud && screen != lossScreen );
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
		// [BIN:TPW-EXE:0x0048A8A7 loss Escape] key 0x1B with modifier bits 0xFF0000 clear leaves the window (message 0x1000B, the same test as the winning handler's 0x48A99E).
		// [BIN:TPW-EXE:0x0048A7BD loss right click] RMBCancelOn ([0x78D911]), the window's pointer [0x7C24CC] not 0 and the clock (0x65968E) strictly more than 0x7D0 ms past the stamp of the window's creation message 0x15 (0x48A789) run the same exit on message 0x10006, button 1.
		var back = Input.ModifierMaskDown == 0 ? input.Pressed : input.Pressed & ~UiKeys.Back;
		var rightExit = FinaleRightClickAllowed( input.RightPressed, GameOptions.Current.RmbCancel, context.Time - lossOpenedAt );
		// [BIN:TPW-EXE:0x4A26DA P window over the loss window] a deactivation while the loss window is up builds the P window (0x4092A0(1)), which takes the focus and the pointer from it: the window's Escape and right-click exits get nothing,
		// and the P window's own procedure (0x4A2706, with the key tables disabled by 0x40CFA0) handles only P, which resumes (0x409350) and destroys the backdrop
		if ( PauseBackdropHoldsFocus )
		{
			back &= ~UiKeys.Back;
			rightExit = false;
			if ( input.Has( UiKeys.Pause ) )
				ToggleUserPause();
		}
		Stack.Update( context, input with { Pressed = back, RightPressed = rightExit } );
		UpdateCursor( input, overUi: true, mouseCaptured.Value );
		if ( lossPhase == LossPhase.Window )
		{
			// [BIN:TPW-EXE:0x0054FAF7 static countdown] the loss camera is a seat camera (mode 4) too, so entering it shows the eye camera's static like a ride does
			PrepareRideStatic( context );
			lossObservedCamera = true;
		}
		// All input belongs to this window, including Pause, the park-door shortcuts and the click that dismisses it.
		return true;
	}

	/// <summary>
	/// The common start of both loss screens: what the full-screen window of the winning screen also does first, with the pause menu left to the overlay's Escape.
	/// </summary>
	// [BIN:TPW-EXE:0x0048AC40 loss window] exits the prior camera mode (0x42AE70, 0x42A190), rides the gate's camera seat (0x4E15B0), disables the key tables (0x40CFA0), shows the HUD again when it was hidden (0x4A29D0), closes the HUD's own window [0x7C24C8] (message 5) and hides the HUD and camcorder windows (message 6)
	private void PrepareLossScene()
	{
		StopRide();
		CancelCamcorder();
		SyncRideScreen(); // the camcorder window goes with the ride view it served
		RetireArmedTools();
		StopResearchSounds();
		SetInfoArm( false );
		CloseCameraPanel();
		Stack.RemoveWhere( screen => screen != hud && screen != lossScreen );
	}

	private void OpenLossWindow( UiContext context )
	{
		lossPhase = LossPhase.Window;
		lossOpenedAt = context.Time;
		PrepareLossScene();
		level.EnterLossView();
		lossScreen = BuildLossBannerScreen( LossWindowScreen, context.Time, modal: true, back: DismissLossWindow );
		Stack.Push( lossScreen );
	}

	/// <summary>
	/// The window's exit: the gate camera is left, the HUD stays hidden and the window's banner moves into the overlay.
	/// </summary>
	// [BIN:TPW-EXE:0x0048A7DB loss dismissal] the handler destroys its window (message 4), leaves the seat camera (0x42A190), hides the HUD with the overlay (0x4A29D0), builds the banner again from the same table as a child of the overlay (0x65FD0E(0x74FB20, 0x48A740) with the overlay as the parent) with the same animation (0x65E72D(0x74FC38, 0x23)),
	// enables the key tables again (0x40CF60) and clears the flag 0x7C24D0; the pointer [0x7C24CC] was cleared by the destroyed window's message 0x14, so the new banner has no exit of its own
	private void DismissLossWindow()
	{
		if ( lossScreen == null || lossScreen.IsRemoved || lossPhase != LossPhase.Window )
			return;
		OpenLossOverlay( lossNow );
	}

	private void OpenLossOverlay( float now )
	{
		RemoveLossScreen();
		lossPhase = LossPhase.Overlay;
		PrepareLossScene();
		if ( level.RideView is { } view && Level.IsGateObject( view.Ride ) )
			level.ExitRideView();
		level.LossOverlayActive = true;
		lossOpenedAt = now;
		// [BIN:TPW-EXE:0x004A2840 overlay procedure] in the world state 4 only a key with no modifier bits and the code 0x1B (0x4A2933, 0x4A2938) does anything of its own: the pause menu 0x48C830(0), whose lost-park entries ParkHud.OpenPauseMenu builds; the keys that show the HUD again are not reached (0x4A2925 skips them), the camera input 0x42A760 is called for every message (0x4A287D)
		lossScreen = BuildLossBannerScreen( LossOverlayScreen, now, modal: false, back: () =>
		{
			if ( Input.ModifierMaskDown == 0 )
				OpenPauseMenu();
		} );
		Stack.Push( lossScreen );
	}

	private UiScreen BuildLossBannerScreen( string name, float now, bool modal, Action back )
	{
		var screen = new UiScreen( name ) { Modal = modal, HidesBelow = true };
		screen.Back = back;
		var banner = OriginalLayoutTables.LossScreen.Root;
		// [BIN:TPW-EXE:0x0074FB20 loss banner] the window 0x5A at (174,500)-(1874,1000) draws the model s_bkrupt, the language folder's bankrupt.MD2; its attributes 3 carry the disabled bit 2, so the hit test 0x65DB25 skips it and a click falls through to the window under it;
		// the animation starts at the table 0x74FC38 with the interval 0x23 (0x65E72D) and the pivot is half the window's right and bottom edges (0x65F345)
		screen.Add( new UiAnimatedModel
		{
			Id = "lossBanner",
			Model = banner.Model!,
			Bounds = banner.Rect,
			Anchor = UiAnchor.Center,
			Pivot = UiAnimatedModel.PivotOf( banner.Rect ),
			Animation = new UiWindowAnimation( UiWindowAnimation.LossBanner, UiWindowAnimation.LossBannerInterval, (long)Math.Floor( now * 1000.0 ) )
		} );
		if ( modal )
			screen.DrawBackground = DrawRideStatic; // the park view draws the eye camera's static beneath the windows
		return screen;
	}

	private void RemoveLossScreen()
	{
		var screen = lossScreen;
		lossScreen = null;
		if ( screen != null )
			Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) );
	}

	/// <summary>Closes the loss screens and leaves the gate camera: the park is no longer lost, or this HUD goes.</summary>
	private void LeaveLossScene()
	{
		RemoveLossScreen();
		lossPhase = LossPhase.None;
		level.LossOverlayActive = false;
		if ( level.RideView is { } view && Level.IsGateObject( view.Ride ) )
			level.ExitRideView();
	}
}
