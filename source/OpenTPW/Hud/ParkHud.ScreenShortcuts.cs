using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>The HUD action an original keyboard shortcut performs (the key table the InputButton defaults document).</summary>
internal enum HudScreenShortcut
{
	None,
	BuyWindow,
	ParkInfo,
	FinanceWindow,
	ResearchWindow,
	MapWindow,
	LoansWindow,
	StaffCosts,
	EntryPrice,
	StaffApplicants,
	VisitorWindow,
	ToggleHelpBar,
	TimeFaster,
	TimeSlower,
	TimeNormal,
	TurnBlueprintLeft,
	TurnBlueprintRight,
	LocateNextStaff,
	LocateNextVisitor,
}

/// <summary>
/// The original keyboard shortcuts of the HUD screens: the game's key table (the <see cref="InputButton"/>
/// default keys and their documented actions; the buy and map buttons' own help rows name B/"(R)"/"(Space)")
/// opened the screen windows directly. OpenTPW wires the keys whose windows exist; the keys the host camera pans with remain separate
/// (All Attractions A, All Staff S: Input's WASD axes).
/// Camcorder terrain-walk and postcard ingress follow their own full-frame input ownership checks.
/// </summary>
public sealed partial class ParkHud
{
	// [EXT:window-key-toggle] a second press of a window key closes that window (toggle) and A/S stay with the camera pan; the original closes with Esc and re-opens on a repeat key (0x488ba0, key group "shortcuts" 0x748378), its callbacks sit in the release-side slot (OpenTPW fires on press), and A (allitems) and S (allstaff) are real keys — OpenTPW design decisions, no evidence needed
	/// <summary>Non-modal HUD windows a window key may close when one is the top screen.</summary>
	internal static readonly HashSet<string> TogglableWindowNames = new()
	{
		"buy", "staff", "map", "finance", "loans", "research", "parkInfo", "entryPrice", "staffCosts", "attractions", "visitors"
	};

	/// <summary>The action the pressed original shortcut key performs; None for keys without a wired screen.</summary>
	internal static HudScreenShortcut ShortcutFor( InputButton button ) => button switch
	{
		InputButton.BuyAttractions => HudScreenShortcut.BuyWindow,
		InputButton.ParkInfo => HudScreenShortcut.ParkInfo,
		InputButton.FinancialInfo => HudScreenShortcut.FinanceWindow,
		InputButton.Research => HudScreenShortcut.ResearchWindow,
		InputButton.MapScreen => HudScreenShortcut.MapWindow,
		InputButton.Loans => HudScreenShortcut.LoansWindow,
		InputButton.StaffBudgets => HudScreenShortcut.StaffCosts,
		InputButton.EntryPrice => HudScreenShortcut.EntryPrice,
		InputButton.HireStaff => HudScreenShortcut.StaffApplicants,
		InputButton.AllVisitors => HudScreenShortcut.VisitorWindow,
		InputButton.ToggleHelpBar => HudScreenShortcut.ToggleHelpBar,
		// All Staff (S) and All Attractions (A) stay unhandled: the host camera pans with S and A
		// (Input's WASD axes, documented in the sandbox panel), and a key press cannot be both —
		// re-wire them once the pan no longer claims those keys. The attractions window opens through
		// the buy window's all-things button instead.
		InputButton.TimeSpeedUp => HudScreenShortcut.TimeFaster,
		InputButton.TimeSlowDown => HudScreenShortcut.TimeSlower,
		InputButton.TimeReset => HudScreenShortcut.TimeNormal,
		InputButton.TurnBlueprintLeft => HudScreenShortcut.TurnBlueprintLeft,
		InputButton.TurnBlueprintRight => HudScreenShortcut.TurnBlueprintRight,
		InputButton.StaffLocator => HudScreenShortcut.LocateNextStaff,
		InputButton.VisitorLocator => HudScreenShortcut.LocateNextVisitor,
		_ => HudScreenShortcut.None,
	};

	/// <summary>
	/// Whether a window key may act while the given screen is on top: the base park-shortcut gate decides when no
	/// window is open, and a HUD window the keys toggle (buy, staff, map, finance, loans, research, management,
	/// park info) also accepts its own key so the same key closes it again. Dialogs and the pause menu never do.
	/// </summary>
	internal static bool WindowShortcutsAllowed( bool parkShortcutsAllowed, string? topScreenName ) =>
		parkShortcutsAllowed || topScreenName is not null && TogglableWindowNames.Contains( topScreenName );

	/// <summary>
	/// Whether a pressed shortcut acts: window keys over their own window, the locator chords also over the window whose
	/// person they locate (the employee window for Ctrl+S, the visitor window for Ctrl+V), every other key on the plain park gate.
	/// </summary>
	// [BIN:TPW-EXE:0x481790 locator key] Ctrl+S tests the open employee window [0x7cc3c0] and locates its employee (0x4b3f60), so the key acts while that window is open
	internal static bool ShortcutAllowed( HudScreenShortcut shortcut, bool parkShortcutsAllowed, string? topScreenName ) => shortcut switch
	{
		HudScreenShortcut.LocateNextStaff => parkShortcutsAllowed || topScreenName == "employee",
		HudScreenShortcut.LocateNextVisitor => parkShortcutsAllowed || topScreenName == "visitorInfo",
		_ when OpensWindow( shortcut ) => WindowShortcutsAllowed( parkShortcutsAllowed, topScreenName ),
		_ => parkShortcutsAllowed,
	};

	/// <summary>The speed steps of the OpenTPW speed control, slowest first.</summary>
	internal static readonly GameSpeed[] SpeedSteps = { GameSpeed.Paused, GameSpeed.Normal, GameSpeed.Fast, GameSpeed.Fastest };

	/// <summary>
	/// The speed after stepping the clock-scale model by <paramref name="steps"/> places (negative steps down);
	/// the result clamps at paused and fastest.
	/// </summary>
	// [EXT:time-key-speeds] the original's time keys are numpad − / + (clock scale ×0.8 / ×1.25, clamped 0.25-2.0) and * (= 1.0) (0x40c3b0/0x40c3c0/0x40c3d0); OpenTPW steps its four named speeds instead (an OpenTPW decision)
	internal static GameSpeed SteppedSpeed( GameSpeed current, int steps )
	{
		var index = Array.IndexOf( SpeedSteps, current );
		if ( index < 0 )
			index = Array.IndexOf( SpeedSteps, GameSpeed.Normal );
		return SpeedSteps[Math.Clamp( index + steps, 0, SpeedSteps.Length - 1 )];
	}

	/// <summary>Whether the shortcut opens a HUD window (those also act when their own window is on top).</summary>
	internal static bool OpensWindow( HudScreenShortcut shortcut ) => shortcut is HudScreenShortcut.BuyWindow
		or HudScreenShortcut.ParkInfo or HudScreenShortcut.FinanceWindow or HudScreenShortcut.ResearchWindow
		or HudScreenShortcut.MapWindow or HudScreenShortcut.LoansWindow or HudScreenShortcut.StaffCosts
		or HudScreenShortcut.EntryPrice or HudScreenShortcut.StaffApplicants or HudScreenShortcut.VisitorWindow;

	private static readonly HudScreenShortcut[] AllShortcuts = Enum.GetValues<HudScreenShortcut>();

	/// <summary>Applies the pressed original window/screen shortcut keys (see <see cref="ShortcutFor"/>).</summary>
	private void HandleScreenShortcuts( bool parkShortcutsAllowed )
	{
		var helpPressed = SampleHelpShortcutPressed();
		// The ride view owns the park input: window keys wait until the visitor has left the ride.
		if ( IsRiding )
			return;
		// F1 without a modifier in a help window repeats the last tutorial advice (the default window procedure's key 0x70 row)
		if ( AdvisorUiHelp.RepeatsTutorialOnHelpKey( Stack.Top?.Name ) && Input.Pressed( InputButton.Help ) && Input.ModifierMaskDown == 0 )
			host?.HelpKeyPressed?.Invoke();
		foreach ( var shortcut in AllShortcuts )
		{
			if ( shortcut == HudScreenShortcut.None || !(shortcut == HudScreenShortcut.ToggleHelpBar ? helpPressed : Pressed( shortcut )) )
				continue;
			if ( !ShortcutAllowed( shortcut, parkShortcutsAllowed, Stack.Top?.Name ) )
				continue;
			ApplyShortcut( shortcut );
		}

		static bool Pressed( HudScreenShortcut shortcut ) => PressedNow( shortcut );
	}

	/// <summary>
	/// Whether the shortcut's key edge is down this frame. A plain shortcut needs that no Ctrl is held, so a KEYBOARD.str letter that equals a
	/// Ctrl chord's letter (Dutch research O and Ctrl+O) cannot fire both; the Ctrl chords (help, locators) are exempt. Nor may Alt be held: Alt+R and Alt+G are cheat keys, not the research and entry price windows.
	/// </summary>
	// [BIN:TPW-EXE:0x40C990 key dispatch] an entry matches only when both the key and the modifier mask (0x46B420: Shift 3, Ctrl 0xC, Alt 0x30) are equal, so a plain shortcut (mask 0) never fires with Alt held and the cheat context's Alt keys (mask 0x30) reach their own table
	internal static bool PressedNow( HudScreenShortcut shortcut )
	{
		var chord = shortcut is HudScreenShortcut.ToggleHelpBar or HudScreenShortcut.LocateNextStaff or HudScreenShortcut.LocateNextVisitor;
		var ctrl = Input.IsFrameKeyDown( Key.ControlLeft ) || Input.IsFrameKeyDown( Key.ControlRight );
		var alt = Input.IsFrameKeyDown( Key.AltLeft ) || Input.IsFrameKeyDown( Key.AltRight );
		return (chord || !ctrl && !alt) && RawPressed( shortcut );
	}

	private static bool RawPressed( HudScreenShortcut shortcut ) => shortcut switch
		{
			HudScreenShortcut.BuyWindow => Input.Pressed( InputButton.BuyAttractions ),
			HudScreenShortcut.ParkInfo => Input.Pressed( InputButton.ParkInfo ),
			HudScreenShortcut.FinanceWindow => Input.Pressed( InputButton.FinancialInfo ),
			HudScreenShortcut.ResearchWindow => Input.Pressed( InputButton.Research ),
			HudScreenShortcut.MapWindow => Input.Pressed( InputButton.MapScreen ),
			HudScreenShortcut.LoansWindow => Input.Pressed( InputButton.Loans ),
			HudScreenShortcut.StaffCosts => Input.Pressed( InputButton.StaffBudgets ),
			HudScreenShortcut.EntryPrice => Input.Pressed( InputButton.EntryPrice ),
			HudScreenShortcut.StaffApplicants => Input.Pressed( InputButton.HireStaff ) && !HelpChordDown,
			HudScreenShortcut.VisitorWindow => VisitorWindowShortcutPressed(),
			HudScreenShortcut.TimeFaster => Input.Pressed( InputButton.TimeSpeedUp ),
			HudScreenShortcut.TimeSlower => Input.Pressed( InputButton.TimeSlowDown ),
			HudScreenShortcut.TimeNormal => Input.Pressed( InputButton.TimeReset ),
			HudScreenShortcut.TurnBlueprintLeft => Input.Pressed( InputButton.TurnBlueprintLeft ),
			HudScreenShortcut.TurnBlueprintRight => Input.Pressed( InputButton.TurnBlueprintRight ),
			HudScreenShortcut.LocateNextStaff => Input.Pressed( InputButton.StaffLocator ),
			HudScreenShortcut.LocateNextVisitor => Input.Pressed( InputButton.VisitorLocator ),
			_ => false,
		};

	/// <summary>The plain V window edge; the registered Ctrl+V locator chord owns its raw V match.</summary>
	internal static bool VisitorWindowShortcutPressed() => Input.Pressed( InputButton.AllVisitors ) && !Input.Down( InputButton.VisitorLocator );

	private void ApplyShortcut( HudScreenShortcut shortcut )
	{
		switch ( shortcut )
		{
			case HudScreenShortcut.ToggleHelpBar:
				ToggleGlobalHelp();
				break;
			case HudScreenShortcut.BuyWindow:
				ToggleWindow( BuyWindowOpen, OpenBuyWindow, CloseBuyWindow );
				break;
			case HudScreenShortcut.ParkInfo:
				ToggleNamedScreen( "parkInfo", OpenParkInfoWindow );
				break;
			case HudScreenShortcut.FinanceWindow:
				ToggleWindow( FinanceWindowOpen, OpenFinanceWindow, CloseFinanceWindow );
				break;
			case HudScreenShortcut.ResearchWindow:
				ToggleWindow( ResearchWindowOpen, OpenResearchWindow, CloseResearchWindow );
				break;
			case HudScreenShortcut.MapWindow:
				ToggleWindow( MapWindowOpen, OpenMapWindow, CloseMapWindow );
				break;
			case HudScreenShortcut.LoansWindow:
				ToggleWindow( LoansWindowOpen, OpenLoansWindow, CloseLoansWindow );
				break;
			case HudScreenShortcut.StaffCosts:
				ToggleNamedScreen( "staffCosts", OpenStaffCostsWindow );
				break;
			case HudScreenShortcut.EntryPrice:
				ToggleNamedScreen( "entryPrice", OpenEntryPriceWindow );
				break;
			case HudScreenShortcut.StaffApplicants:
				ToggleStaffWindow();
				break;
			case HudScreenShortcut.VisitorWindow:
				ToggleWindow( VisitorWindowOpen, OpenVisitorWindow, CloseVisitorWindow );
				break;
			case HudScreenShortcut.TimeFaster:
				SetUserGameSpeed( SteppedSpeed( Status.Speed, 1 ) );
				break;
			case HudScreenShortcut.TimeSlower:
				SetUserGameSpeed( SteppedSpeed( Status.Speed, -1 ) );
				break;
			case HudScreenShortcut.TimeNormal:
				SetUserGameSpeed( GameSpeed.Normal );
				break;
			case HudScreenShortcut.TurnBlueprintLeft:
			case HudScreenShortcut.TurnBlueprintRight:
				RotateBlueprint( shortcut );
				break;
			case HudScreenShortcut.LocateNextStaff:
				OpenStaffLocator();
				break;
			case HudScreenShortcut.LocateNextVisitor:
				OpenVisitorLocator();
				break;
		}
	}

	private bool TryOpenPostcard( bool inputAllowed )
	{
		if ( !inputAllowed || !Input.Pressed( InputButton.SendEmailPostcard ) || IsRiding || Stack.Screens.Count != 1 ||
			Camera.Mode is not ParkCameraMode || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects ||
			level.CellTool.IsActive || level.IsCoasterToolActive )
			return false;
		if ( !FeatureGates.Sharing || host.OpenPostcard == null )
		{
			PostMessage( strings.Extra( OpenTpwText.NotAvailable ) );
			return false;
		}
		host.OpenPostcard( Stack );
		return true;
	}

	/// <summary>Opens the window, or closes it when it is already open (UI-900).</summary>
	internal static void ToggleWindow( bool open, Action openAction, Action closeAction )
	{
		if ( open )
			closeAction();
		else
			openAction();
	}

	private void ToggleNamedScreen( string name, Action openAction )
	{
		if ( Stack.Top?.Name == name )
			Stack.Pop();
		else
			openAction();
	}

	private void ToggleStaffWindow()
	{
		if ( Stack.Top?.Name == "staff" )
		{
			CloseStaffWindow();
			return;
		}
		OpenStaffWindow();
	}

	/// <summary>
	/// How many <see cref="Level.RotateBuild"/> steps (each +90 degrees) a blueprint-turn key takes: comma adds 90 degrees (one step), full stop adds 270
	/// (three steps). Level.BuildRotation is the angle the build check and placement use, as the original's global is.
	/// </summary>
	// [BIN:TPW-EXE:0x40c120 turn key ','] game key group row 1 (0x74803c) sets the build angle (0x81d7a4, getter 0x52f1f0, setter 0x52f1b0) to (angle + 0x5a) mod 0x168; [BIN:TPW-EXE:0x40c150 turn key '.'] row 2 (0x748050) adds 0x10e. The angle goes unchanged to the placement check and commit 0x528a70 (0x524c51..0x524c63), as Level.BuildRotation does to PlaceObject (ObjectFootprint: rotation 90 maps (u, v) to (X+v, Y-u), verified on Easymode saves)
	internal static int BlueprintTurnSteps( HudScreenShortcut shortcut ) => shortcut switch
	{
		HudScreenShortcut.TurnBlueprintLeft => 1,
		HudScreenShortcut.TurnBlueprintRight => 3,
		_ => 0,
	};

	private void RotateBlueprint( HudScreenShortcut shortcut )
	{
		if ( level.BuildEntry == null )
			return;
		for ( var step = 0; step < BlueprintTurnSteps( shortcut ); step++ )
			level.RotateBuild();
	}
}
