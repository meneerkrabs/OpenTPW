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
/// opened the screen windows directly. OpenTPW wires the keys whose windows exist; a key whose screen is not
/// implemented (camcorder) stays unhandled, as do the keys the host camera pans with
/// (All Attractions A, All Staff S: Input's WASD axes).
/// Postcard ingress follows its own full-frame input ownership checks.
/// </summary>
public sealed partial class ParkHud
{
	// [APPROX:UI-900] a second press of a window key closes that window (toggle); the original's key handler for an open window is not traced — evidence needed: the original key dispatch while a screen window is open
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

	/// <summary>The speed steps of the OpenTPW speed control, slowest first.</summary>
	internal static readonly GameSpeed[] SpeedSteps = { GameSpeed.Paused, GameSpeed.Normal, GameSpeed.Fast, GameSpeed.Fastest };

	/// <summary>
	/// The speed after stepping the clock-scale model by <paramref name="steps"/> places (negative steps down);
	/// the result clamps at paused and fastest.
	/// </summary>
	// [APPROX:UI-902] the original's time keys step its clock scale by 1.25 within 0.25..2 (ECON-004/UI-022); OpenTPW's four named speeds take their place — evidence needed: the original key tokens' names and handler
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
		var windowKeysAllowed = WindowShortcutsAllowed( parkShortcutsAllowed, Stack.Top?.Name );
		foreach ( var shortcut in AllShortcuts )
		{
			if ( shortcut == HudScreenShortcut.None || !(shortcut == HudScreenShortcut.ToggleHelpBar ? helpPressed : Pressed( shortcut )) )
				continue;
			// Window keys may also close their own window; every other key follows the plain park gate.
			if ( !(OpensWindow( shortcut ) ? windowKeysAllowed : parkShortcutsAllowed) )
				continue;
			ApplyShortcut( shortcut );
		}

		static bool Pressed( HudScreenShortcut shortcut ) => shortcut switch
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
	}

	/// <summary>The plain V window edge; the registered Ctrl+V locator chord owns its raw V match.</summary>
	internal static bool VisitorWindowShortcutPressed() => Input.Pressed( InputButton.AllVisitors ) && !Input.Down( InputButton.VisitorLocator );

	private void ApplyShortcut( HudScreenShortcut shortcut )
	{
		switch ( shortcut )
		{
			case HudScreenShortcut.ToggleHelpBar:
				helpBarVisibility = !HelpBarVisible;
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
				Status.Speed = SteppedSpeed( Status.Speed, 1 );
				break;
			case HudScreenShortcut.TimeSlower:
				Status.Speed = SteppedSpeed( Status.Speed, -1 );
				break;
			case HudScreenShortcut.TimeNormal:
				Status.Speed = GameSpeed.Normal;
				break;
			case HudScreenShortcut.TurnBlueprintLeft:
				RotateBlueprint( -90 );
				break;
			case HudScreenShortcut.TurnBlueprintRight:
				RotateBlueprint( 90 );
				break;
			case HudScreenShortcut.LocateNextStaff:
				LocateNextStaffRole();
				break;
			case HudScreenShortcut.LocateNextVisitor:
				LocateVisitor();
				break;
		}
	}

	private bool TryOpenPostcard( bool inputAllowed )
	{
		if ( !inputAllowed || !Input.Pressed( InputButton.SendEmailPostcard ) || IsRiding || Stack.Screens.Count != 1 ||
			Camera.Mode is not ParkCameraMode || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects ||
			level.CellTool.IsActive || level.IsCoasterToolActive )
			return false;
		if ( !GameEditions.Capabilities.Sharing || host.OpenPostcard == null )
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

	// [APPROX:UI-903] blueprint turning uses the level's 90-degree build-rotation steps; the original comma/full-stop handler's step and smoothing are not traced — evidence needed: the original blueprint-turn key handler
	private void RotateBlueprint( int degrees )
	{
		// Level.RotateBuild owns the rotation and steps +90 degrees; a left turn is three of its steps.
		if ( level.BuildEntry == null )
			return;
		var steps = ((degrees / 90) % 4 + 4) % 4;
		for ( var step = 0; step < steps; step++ )
			level.RotateBuild();
	}

	// [APPROX:UI-904] the staff locator cycles roles in the staff window's role order; the original Ctrl+S cycling order is not traced — evidence needed: the original staff-locator key handler
	private static readonly StaffType[] LocatorRoles = { StaffType.Handyman, StaffType.Mechanic, StaffType.Entertainer, StaffType.Guard, StaffType.Researcher };
	private StaffType? lastLocatedRole;
	private void LocateNextStaffRole()
	{
		// Without a previous role the cycle starts at the first (handyman); afterwards it starts after it.
		var start = lastLocatedRole is { } previous ? Math.Max( 0, Array.IndexOf( LocatorRoles, previous ) ) : -1;
		for ( var offset = 1; offset <= LocatorRoles.Length; offset++ )
		{
			var role = LocatorRoles[(start + offset) % LocatorRoles.Length];
			if ( CanLocateStaff( role ) && LocateStaff( role ) )
			{
				lastLocatedRole = role;
				return;
			}
		}
	}
}
