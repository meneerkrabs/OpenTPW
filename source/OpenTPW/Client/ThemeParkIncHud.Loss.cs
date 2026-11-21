using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// Theme Park Inc's lost park (docs/reverse/TPI-BANK-MAIL.md): closing a "Game Over" letter loses the park and opens the loss window,
/// Theme Park World's loss scene with a text caption ("BANKRUPT!" or "Fired!") in place of the bankrupt banner; Escape or a late right click
/// leave it for the hidden-HUD overlay, whose Escape opens the lost park's four-line menu. A lost park that is loaded takes the overlay at once.
/// </summary>
public sealed partial class ThemeParkIncHud
{
	private enum LossPhase { None, Window, Overlay }

	/// <summary>The names of the loss screens and the lost park's menu.</summary>
	public const string LossWindowScreen = "tpiLossWindow", LossOverlayScreen = "tpiLossOverlay", LostMenuScreen = "tpiLostMenu";

	// [BIN:TPI-EXE:0x50B0BB loss caption] UITEXT 921 "BANKRUPT!" (reason 0) and 922 "Fired!" (reason 1)
	internal const int BankruptCaption = 921, FiredCaption = 922;
	// [BIN:TPI-EXE:0x50D3F3 lost menu lines] the lost park's menu: Load Game (UITEXT 768, command 0x18), Resume Game (771, 0x17), Select A New Player (774, 0x1A) and Quit Game (772, 0x1B)
	internal const int LostLoadGame = 768, LostResumeGame = 771, LostSelectNewPlayer = 774, LostQuitGame = 772;

	private LossPhase lossPhase;
	private UiScreen? lossScreen;
	private float lossOpenedAt;
	private bool lossPending;
	private bool lossHooked;
	private string lossCaption = "";

	/// <summary>The park is lost (the world state 4).</summary>
	public bool Lost => level.Park?.Economy.IsBankrupt == true;

	/// <summary>The loss window is up (the gate camera and the caption).</summary>
	public bool LossWindowOpen => lossPhase == LossPhase.Window;

	/// <summary>The hidden-HUD overlay is up (the loss window was dismissed, or the park was loaded lost).</summary>
	public bool LossOverlayOpen => lossPhase == LossPhase.Overlay;

	/// <summary>The loss caption as shown (tests).</summary>
	internal string LossCaption => lossCaption;

	private void HookLossEvents()
	{
		if ( lossHooked || level.Park == null )
			return;
		lossHooked = true;
		level.Park.EventRaised += item =>
		{
			if ( item.Kind == ParkEventKind.Bankrupt )
				lossPending = true;
		};
	}

	/// <summary>Brings the loss screens in step with the park; true while the loss window owns the frame.</summary>
	private bool UpdateLossScene( UiContext context, UiInput input )
	{
		HookLossEvents();
		if ( !Lost )
		{
			lossPending = false;
			if ( lossPhase != LossPhase.None )
				LeaveLossScene();
			return false;
		}
		if ( lossPhase == LossPhase.None )
		{
			var window = lossPending;
			lossPending = false;
			if ( window )
				OpenLossWindow( context.Time );
			else
				// [BIN:TPI-EXE:0x4F855D lost park load] a loaded park in the world state 4 runs 0x50B1C0: the HUD hidden behind the overlay and the caption
				// "BANKRUPT!" (the reason is not saved, so the fired branch is unreachable, 0x50B1DE), with no camera and no loss window
				OpenLossOverlay( context.Time, BankruptCaption );
			if ( lossPhase == LossPhase.Overlay )
				return false;
		}
		if ( lossPhase != LossPhase.Window )
			return false;
		if ( lossScreen == null || lossScreen.IsRemoved )
		{
			OpenLossOverlay( context.Time, null );
			return false;
		}
		Stack.RemoveWhere( screen => screen != hud && screen != lossScreen );
		if ( Camera.Mode is ParkCameraMode camera )
		{
			camera.UiCapturesKeyboard = true;
			camera.ModalOpen = true;
		}
		// [BIN:TPI-EXE:0x50ACE0 loss window procedure] Escape (message 0x1000B, key 0x1B without modifier bits) or button 1 (message 0x10006) while RMBCancelOn
		// (the settings byte 0xA1021D) is set and more than 0x7D0 ms have passed since the creation message 21 leave the window, as in Theme Park World
		var back = Input.ModifierMaskDown == 0 ? input.Pressed : input.Pressed & ~UiKeys.Back;
		var rightExit = OpenTPW.Hud.ParkHud.FinaleRightClickAllowed( input.RightPressed, GameOptions.Current.RmbCancel, context.Time - lossOpenedAt );
		Stack.Update( context, input with { Pressed = back, RightPressed = rightExit } );
		return true;
	}

	/// <summary>The common start of both loss screens: every tool and window of the HUD goes.</summary>
	// [BIN:TPI-EXE:0x50AFC0 loss window] like tp.exe's 0x48AC40 it closes the HUD's windows (message 5 to 0xA568BC, message 6 to the HUD and camcorder windows) and disables the key tables (0x431230)
	private void PrepareLossScene()
	{
		DisarmAll();
		if ( buildOpen )
			CloseBuildWindow();
		Stack.RemoveWhere( screen => screen != hud && screen != lossScreen );
	}

	private void OpenLossWindow( float now )
	{
		lossPhase = LossPhase.Window;
		lossOpenedAt = now;
		var reason = level.Park!.Economy.ThemeParkIncLossReason;
		lossCaption = strings.Entry( reason == 1 ? FiredCaption : BankruptCaption );
		PrepareLossScene();
		// [BIN:TPI-EXE:0x5E5B20 loss camera] the window rides the gate's camera seat, through the seat camera Theme Park World's 0x4E15B0 also reaches (0x4668C0 = tp.exe 0x42A560);
		// the Theme Park Inc seat routine's extra writes (the gate's +0x5C byte and its script variable 0xC = 1) are not ported
		level.EnterLossView();
		lossScreen = BuildLossScreen( LossWindowScreen, modal: true, back: DismissLossWindow );
		Stack.Push( lossScreen );
	}

	/// <summary>The window's exit: the gate camera is left and the HUD stays hidden behind the overlay, under the same caption.</summary>
	// [BIN:TPI-EXE:0x50ACE0 loss dismissal] the procedure destroys its window (message 4), leaves the seat camera (0x4657F0), hides the HUD behind the overlay (0x550BD0,
	// the full-screen window 0xDDF22), enables the key tables again (0x4311E0) and clears 0xA568C4; the caption object 0xA5EA24 is not touched, so it stays under the overlay
	private void DismissLossWindow()
	{
		if ( lossPhase != LossPhase.Window )
			return;
		OpenLossOverlay( lossOpenedAt, null );
	}

	private void OpenLossOverlay( float now, int? caption )
	{
		RemoveLossScreen();
		lossPhase = LossPhase.Overlay;
		if ( caption is { } entry )
			lossCaption = strings.Entry( entry );
		PrepareLossScene();
		if ( level.RideView is { } view && Level.IsGateObject( view.Ride ) )
			level.ExitRideView();
		level.LossOverlayActive = true;
		lossOpenedAt = now;
		// [BIN:TPI-EXE:0x550AD6 overlay keys] in the world state 4 only a key with no modifier bits and the code 0x1B does anything of its own: the pause menu 0x50DC90(0)
		lossScreen = BuildLossScreen( LossOverlayScreen, modal: false, back: () =>
		{
			if ( Input.ModifierMaskDown == 0 )
				OpenLostMenu();
		} );
		Stack.Push( lossScreen );
	}

	/// <summary>
	/// A loss screen: the full-canvas window that hides the HUD, with the caption centred on the canvas.
	/// </summary>
	// [BIN:TPI-EXE:0x53EE30 loss caption] the caption object builds a window over the one-control table 0x9B96D8 and a text control in font slot 0 (0x500DE0(0)), centred both ways
	// (0x84A4D2(1, 1, 3)), sized to the text and centred on (1024, 768) (0x53EED7-0x53EF35); no colour call is made, so it keeps the text class's white (0x5123A3), and it neither moves,
	// fades nor expires (its procedure 0x53EDE0 does nothing)
	// [EXT:ui-font-tier-by-scale] which shipped font serves font slot 0 (the MENUSMALL/MENUMED/MENUBIG family) is OpenTPW's tier choice: the menu role
	private UiScreen BuildLossScreen( string name, bool modal, Action back )
	{
		var screen = new UiScreen( name ) { Modal = modal, HidesBelow = true, Back = back };
		screen.Add( new UiLabel
		{
			Id = "lossCaption",
			Text = () => lossCaption,
			Font = fonts => fonts.Menu,
			Color = new Veldrid.RgbaByte( 0xFF, 0xFF, 0xFF, 0xFF ),
			Align = UiAlign.Center,
			Bounds = new UiRect( 0, 0, UiCanvas.VirtualWidth, UiCanvas.VirtualHeight ),
			Anchor = UiAnchor.Center
		} );
		return screen;
	}

	/// <summary>
	/// The lost park's menu: Load Game, Resume Game, Select A New Player and Quit Game, centred on the canvas; opening it pauses nothing.
	/// </summary>
	// [BIN:TPI-EXE:0x518FF0 lost menu line] each line is made by 0x518FF0 (the steps of tp.exe's 0x492F60) in font slot 0, so it draws as the pause menu's lines do
	// [EXT:tpi-front-end-phase1] Load Game continues the local Science slot instead of opening the original save picker (0x548570(1, 0)), as the Personnel File's Load A Game does
	internal void OpenLostMenu()
	{
		if ( Stack.Screens.Any( screen => screen.Name == LostMenuScreen ) )
			return;
		UiScreen? menu = null;
		var items = new List<(string Id, Func<string> Text, Action Clicked, bool Enabled)>
		{
			("loadGame", () => strings.Entry( LostLoadGame ), () => { if ( host.LoadPark != null ) ConfirmParkLoad(); }, host.LoadPark != null),
			("resumeGame", () => strings.Entry( LostResumeGame ), CloseLostMenu, true),
			("selectNewPlayer", () => strings.Entry( LostSelectNewPlayer ), () => host.SelectNewPlayer?.Invoke(), host.SelectNewPlayer != null),
			("quitGame", () => strings.Entry( LostQuitGame ), host.Quit, true)
		};
		menu = UiDialogs.TextMenu( LostMenuScreen, items, CloseLostMenu, "resumeGame", centred: true );
		Stack.Push( menu );

		void CloseLostMenu()
		{
			if ( ReferenceEquals( Stack.Top, menu ) )
				Stack.Pop();
		}
	}

	private void RemoveLossScreen()
	{
		var screen = lossScreen;
		lossScreen = null;
		if ( screen != null )
			Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) );
	}

	/// <summary>Closes the loss screens and leaves the gate camera: the park is no longer lost.</summary>
	private void LeaveLossScene()
	{
		RemoveLossScreen();
		Stack.RemoveWhere( screen => screen.Name == LostMenuScreen );
		lossPhase = LossPhase.None;
		lossCaption = "";
		level.LossOverlayActive = false;
		if ( level.RideView is { } view && Level.IsGateObject( view.Ride ) )
			level.ExitRideView();
	}
}
