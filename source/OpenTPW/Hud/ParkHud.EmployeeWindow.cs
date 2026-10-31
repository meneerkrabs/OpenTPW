using System.Globalization;
using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The per-employee window, built from tp.exe's table 0x756f48: the window1 frame, the employee's name as the title, the portrait frame,
/// the stats frame (employed for, monthly wage, skill, happiness and energy bars, current status with its icon), the dismiss (b_fire),
/// pick-up (b_pick) and patrol (b_route) buttons, the cycle arrows (b_arup, b_ardown), the all-staff and locate buttons and the tick.
/// </summary>
// [BIN:TPW-EXE:0x004b6080 employee window] the opener stores the member, builds table 0x756f48 through 0x48cea0 (handler 0x4b5cb0, frames 0x511 and 0x512) and
// posts help 0xc0; 0x48cea0 first closes the window that is open (0x485b40), so the All Staff list (0x496088, its row message 0x400) gives way to it
// [BIN:TPW-EXE:0x00486920 thing info] a click on a staff thing (types 4-8) opens this window too; OpenTPW opens it from the staff list (no staff click in the park)
// [APPROX:UI-092] the portrait is the original's independent clone (full copy 0x486530 at the window's refresh, partial copy 0x486610 on every update, stepped by the sprite scheduler); the staff's sprite placement 0x475480 is not ported, so the clone's heading is the member's current sector, and the window depth word, native depth/occlusion and viewport/raster parity remain open; employee-only nominal geometry is 2.4 with authored feet (526,476).
public sealed partial class ParkHud
{
	private static LayoutTable EmployeeTable => OriginalLayoutTables.EmployeeWindow;

	/// <summary>Seconds between two refreshes of the stats frame (timer 0x80080, 4000 ms, armed on message 0x15 at 0x4b5d29).</summary>
	public const float EmployeeStatsRefreshSeconds = 4f;

	private UiScreen? employeeScreen;
	private int employeeId;
	private bool employeeArrows;
	private float employeeStatsAge;
	private EmployeeStats employeeStats;

	public bool EmployeeWindowOpen => employeeScreen != null && Stack.Screens.Contains( employeeScreen );
	/// <summary>The employee the window shows (tp.exe's selected staff 0x7c2658), null when it is closed or the member left.</summary>
	public StaffMember? Employee => EmployeeWindowOpen ? StaffMembers.FirstOrDefault( member => member.Id == employeeId ) : null;
	/// <summary>Whether the cycle arrows show (more than one employee when the window opened).</summary>
	public bool EmployeeArrowsVisible => employeeArrows;
	/// <summary>The stats frame as last refreshed.</summary>
	public EmployeeStats EmployeeStatsShown => employeeStats;

	private IReadOnlyList<StaffMember> StaffMembers => (Status.EconomySource as ParkEconomy)?.Staff.Members ?? (IReadOnlyList<StaffMember>)Array.Empty<StaffMember>();

	/// <summary>Opens the window on <paramref name="member"/>; the staff window gives way to it.</summary>
	public bool OpenEmployeeWindow( StaffMember member )
	{
		if ( StaffMembers.All( item => item.Id != member.Id ) )
			return false;
		CloseStaffWindow();
		if ( EmployeeWindowOpen )
			CloseEmployeeWindow();
		employeeId = member.Id;
		// [BIN:TPW-EXE:0x004b5a60 employee window init] the staff count of mask 1 (every staff thing, 0x48ca00) below 2 hides the arrows 0x50d and 0x50f; the title 0x520 takes the name
		employeeArrows = StaffMembers.Count >= 2;
		RefreshEmployeeStats();
		employeeScreen ??= CreateEmployeeScreen();
		CloneEmployeePortrait( member );
		Stack.Push( employeeScreen );
		employeeScreen.Focus( employeeScreen.Find( "exit" ) );
		PostPatrolAreaHelp( member );
		return true;
	}

	public void CloseEmployeeWindow()
	{
		if ( employeeScreen == null || !EmployeeWindowOpen )
			return;
		while ( Stack.Top != employeeScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == employeeScreen )
			Stack.Pop();
	}

	/// <summary>The window's refresh posts help 212 for an employee with an own patrol area.</summary>
	// [BIN:TPW-EXE:0x4b5ab0 patrol help] the refresh posts help 0xd4 (0x486b00) when 0x506eb0 is non-zero, i.e. unless the area words +0x20a / +0x20c hold the default 1 / 0x4000, and sets the toggle 0x510 to the same state
	// [BIN:TPW-EXE:0x004b5ab0 employee refresh] the refresh (vtable +4, run as the window is built by 0x48cea0 and by the cycle buttons) asks 0x506eb0 whether the selected staff has an own area (corner words +0x20a/+0x20c other than 1 and 0x4000) and then posts help 0xd4 (212) through 0x486b00 (0x4b5b03); the window's close handler withdraws it next to 0xc0 (0x4b5d05)
	private void PostPatrolAreaHelp( StaffMember member )
	{
		if ( member.HasPatrolArea )
			host?.HelpPosted?.Invoke( AdvisorToolHelp.PatrolAreaSet );
	}

	private void RefreshEmployeeStats()
	{
		employeeStatsAge = 0;
		var economy = Status.EconomySource as ParkEconomy;
		var member = StaffMembers.FirstOrDefault( item => item.Id == employeeId );
		employeeStats = member == null ? default : EmployeeWindowRules.Stats( member, economy?.Tick ?? 0, economy?.Staff.MonthlyWage( member ) ?? 0 );
	}

	/// <summary>The arrows: the next (older) or previous (newer) employee, wrapping, and the window shows it.</summary>
	// [BIN:TPW-EXE:0x004b5cb0 employee buttons] 0x50f (b_ardown, help 91) runs 0x48caf0 → 0x483740 and 0x50d (b_arup, help 92) 0x48cbe0 → 0x483770 with mask 1
	// (the window's kind word +4 is -1, set by the constructor 0x4b5310); a different member becomes the selected staff and the window refreshes (vtable +4, 0x4b5ab0)
	public bool CycleEmployee( bool forward )
	{
		if ( Employee is not { } member )
			return false;
		var next = EmployeeWindowRules.Cycle( StaffMembers, member.Id, forward, (Status.EconomySource as ParkEconomy)?.Staff.Things );
		if ( next == null || next.Id == member.Id )
			return false;
		employeeId = next.Id;
		RefreshEmployeeStats();
		CloneEmployeePortrait( next );
		PostPatrolAreaHelp( next );
		return true;
	}

	/// <summary>Whether the pick-up button is enabled (tp.exe <c>CanBePickedUp</c> 0x505bd0, read on every message 0x1e).</summary>
	// [BIN:TPW-EXE:0x004b5d4d pick-up enable] message 0x1e enables control 0x50e when 0x505bd0 accepts the selected staff, and disables it otherwise
	public bool CanPickUpEmployee => Employee is { } member && !level.IsReadOnlyVisit && level.StaffPlacement != null
		&& level.Guests?.Grid.Cells is { } cells && StaffPlacementRules.CanBePickedUp( member, cells );

	/// <summary>The pick-up button: the employee goes into the player's hand, the window closes and the Carry tool starts.</summary>
	// [BIN:TPW-EXE:0x004b5ed9 pick-up button] control 0x50e (b_pick, help 88) stores the member as the held staff (0x48c950), runs the pick-up 0x505c50 and closes the window (vtable +0x2c → 0x48cf70)
	public bool PickUpEmployee()
	{
		if ( RejectReadOnlyAction() || Employee is not { } member || level.StaffPlacement is not { } placement )
			return false;
		// [EXT:online-coop-commands] a guest's pick-up asks the session's rights before the park's own check, and goes to the host as a request (C4c-2)
		if ( GuestStaff is { } guestStaff )
			return GuestPickUp( guestStaff, member );
		if ( !placement.PickUp( member ) )
			return false;
		level.EndBuildTools();
		CloseEmployeeWindow();
		return true;
	}

	/// <summary>Whether the patrol button acts: clearing an employee's own area, or setting one with the patrol tool.</summary>
	public bool CanPressEmployeePatrol => Employee is { } member && !level.IsReadOnlyVisit && (DrawnHasPatrolArea( member ) || level.PatrolTool != null);

	/// <summary>The patrol toggle (b_route): checked while the employee has a patrol area; pressing it sets one (the patrol tool) or clears it.</summary>
	// [BIN:TPW-EXE:0x004b5e34 patrol toggle] control 0x510 (b_route, attribute 0x11: a toggle, help 86 / 87) passes its new state to 0x4b5b50: 1 holds the member (0x48c950),
	// sets mouse mode 0x36 (0x46c580), closes the window and posts help 0xc1; 0 clears the area (0x506e90) and the window stays; the refresh 0x4b5ab0 checks it when 0x506eb0
	// finds an own area (corner words +0x20a/+0x20c other than 1 and 0x4000)
	public bool PressEmployeePatrol()
	{
		if ( !CanPressEmployeePatrol || Employee is not { } member )
			return false;
		// [EXT:online-coop-commands] a guest's toggle asks the session's rights before it acts, and reads the area it asked for (C4c-2)
		if ( GuestManagement != null && RefusedByRights( new SetPatrolAreaCommand( member.Id, 0, 0, 0, 0, Clear: DrawnHasPatrolArea( member ) ) ) )
			return false;
		return DrawnHasPatrolArea( member ) ? ClearPatrolArea( member ) : SetPatrolArea( member );
	}

	private bool SetPatrolArea( StaffMember member )
	{
		if ( RejectReadOnlyAction() || level.PatrolTool is not { } tool )
			return false;
		level.EndBuildTools();
		level.StaffPlacement?.Cancel();
		if ( !tool.Begin( member ) )
			return false;
		PostMessage( strings.Extra( OpenTpwText.PatrolAreaHint ) );
		CloseEmployeeWindow();
		// [BIN:TPW-EXE:0x004b5b50 patrol toggle] the tool set (0x46c580(0x36)) and the window closed (vtable +0x2c), it posts help 0xc1 (193) through 0x486b00; the interface mode change withdraws it (0x52f200)
		PostToolHelp( AdvisorToolHelp.PatrolTool );
		return true;
	}

	private bool ClearPatrolArea( StaffMember member )
	{
		if ( RejectReadOnlyAction() )
			return false;
		// [EXT:online-coop-commands] a guest's clear goes to the host, the toggle drawing it until it comes back (C4c-2)
		return IssueManagement( ManagementTarget.Staff( member.Id ), ManagementKnob.Patrol, new SetPatrolAreaCommand( member.Id, 0, 0, 0, 0, Clear: true ) );
	}

	/// <summary>Dismisses the window's employee through the park economy and closes the window.</summary>
	public bool DismissEmployee() => Employee is { } member && DismissStaff( member.Id );

	private bool DismissStaff( int staffId )
	{
		if ( RejectReadOnlyAction() || Status.EconomySource is not ParkEconomy economy || economy.Staff.Members.All( item => item.Id != staffId ) )
			return false;
		// [EXT:online-coop-commands] a guest's dismissal goes to the host, after the session's rights; the window closes when the employee leaves the roster (C4c-2)
		if ( GuestManagement != null )
			return IssueManagement( ManagementTarget.Staff( staffId ), null, new FireStaffCommand( staffId ) );
		var fired = level.Commands.Issue( new FireStaffCommand( staffId ) );
		if ( !fired.Accepted )
		{
			// [EXT:online-coop-commands] the host's dismissal of an employee a guest carries says why (C4c-2)
			if ( fired.Reason == FireStaffCommand.BeingMovedReason )
				PostMessage( strings.Extra( OpenTpwText.CoopStaffBeingMoved ) );
			return false;
		}
		CloseEmployeeWindow();
		return true;
	}

	/// <summary>The dismiss button: at once without the Confirmations option, else the window closes and the yes/no box asks first.</summary>
	// [BIN:TPW-EXE:0x004b5e5d dismiss] b_fire (control 0x50c, help 89): with the Confirmations option (options byte 0x78d90f) a yes/no box with UITEXT 0x18d (OpenTPW 396) whose yes runs 0x4b6070 → 0x505790 on the member;
	// without it 0x505790 at once; either way the window closes (vtable +0x2c)
	public void ConfirmDismissEmployee()
	{
		if ( Employee is not { } member || RejectReadOnlyAction() )
			return;
		// [EXT:online-coop-permissions] a guest without the staff right hears the rights' note, and the window stays (C4c-2)
		if ( GuestManagement != null && RefusedByRights( new FireStaffCommand( member.Id ) ) )
			return;
		if ( !GameOptions.Current.Confirmations )
		{
			DismissStaff( member.Id );
			return;
		}
		var staffId = member.Id;
		CloseEmployeeWindow();
		// [EXT:online-coop-commands] a guest's confirmation waits for the host's answer after its Yes, as a loan's does (C4c-2)
		if ( GuestManagement != null )
		{
			ConfirmGuestDismissal( staffId );
			return;
		}
		Stack.Push( UiDialogs.MessageOfOriginalText( "confirmDismissStaff", () => strings[UIStrings.DismissEmployee],
			(() => strings.Value( UIStrings.Yes ), () => { Stack.Pop(); DismissStaff( staffId ); }),
			(() => strings.Value( UIStrings.No ), Stack.Pop) ) );
	}

	/// <summary>The all-staff button: the staff window's list of employees (the stand-in for the All Staff window) on the employee's role.</summary>
	// [BIN:TPW-EXE:0x004b5dd9 all staff] control 0x50b (b_allstaff, help 94) opens the All Staff window (0x496620) on the member's role (0x506490)
	public void ShowAllStaffOfEmployee()
	{
		var role = Employee?.Type ?? StaffType.Handyman;
		CloseEmployeeWindow();
		OpenStaffWindow();
		ShowEmployedStaff( true );
		FilterStaff( role );
	}

	/// <summary>The locate button: the staff locator panel opens on the employee (its role chosen, the camera following) and the window closes.</summary>
	// [BIN:TPW-EXE:0x004b5f07 locate] control 0x521 (b_locate, help 93) opens the staff locator panel on the member (0x4b3f60 with [0x7c2658]), which closes the open window
	public bool LocateEmployee()
	{
		// Retain the host tour-camera boundary; actor selection does not require resolvable coordinates.
		if ( Employee is not { } member || Camera.Mode is not ParkCameraMode )
			return false;
		lastLocatedStaff[member.Type] = member.Id;
		OpenLocator( LocatorMode.Staff, LocatorModel.RoleOf( member.Type ), member.Id );
		return true;
	}

	/// <summary>The portrait's frame: the clone of the employee's sprite (<see cref="PortraitSprites"/>), whatever became of the employee since it was made.</summary>
	private UiSpriteFrame? EmployeePortraitFrame() => level.StaffRenderer?.SpriteFrame( level.Portraits.StaffClone );

	/// <summary>The portrait's full copy of the selected employee's sprite, made as the window is built and by every refresh (the cycle arrows).</summary>
	// [BIN:TPW-EXE:0x004b5330 employee portrait] the refresh's vtable +0x18 takes the portrait control's rectangle (0x65d4f6) and calls 0x486530 with the selected staff, the control's depth word (+0xE0) + 1 and scale 8.0: the window's one clone is replaced, and a member without a sprite (+0x0C is 0, a resting member) leaves none
	private void CloneEmployeePortrait( StaffMember member )
	{
		var table = EmployeeTable.Control( 0x511 );
		var inner = table.Inner ?? table.Rect;
		var placement = PortraitSprites.Placement( (int)inner.X, (int)inner.Y, (int)inner.Right, (int)inner.Bottom, PortraitSprites.EmployeeScale, Stack.Screens.Count + 1 );
		level.Portraits.CloneStaff( member, placement, PortraitSprites.EmployeeScale );
	}

	private UiScreen CreateEmployeeScreen()
	{
		var screen = new EmployeeScreen( this );
		// [BIN:TPW-EXE:0x6FFE70 employee window vtable +0x20] the close hook is 0x4b6b80, which jumps to 0x486630: the clone goes with the window
		screen.Removed = () =>
		{
			CloseEmployeeRenameDialog();
			level.Portraits.Close();
		};
		var table = EmployeeTable;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		var portrait = table.Control( 0x511 );
		// [BIN:TPW-EXE:0x65beb4 type-1 inner initialization;0x65d4f6 inner getter] no explicit inner override: inherit the outer rectangle.
		var inner = portrait.Inner ?? portrait.Rect;
		var stats = table.Control( 0x512 );
		screen.Add( new UiModelImage { Id = "portraitFrame", Model = portrait.Model!, Bounds = portrait.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiModelImage { Id = "statsFrame", Model = stats.Model!, Bounds = stats.Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x4b5330 employee figure] 0x486530 clones the employee's live sprite at scale 8.0, with draw depth control +0xE0 + 1 (UI-092)
		screen.Add( new UiSpriteFigure { Id = "portraitFigure", Frame = EmployeePortraitFrame, Bounds = inner, Anchor = UiAnchor.Center,
			FeetInCanvas = EmployeePortraitGeometry.Feet( inner ), CanvasUnitsPerPixel = EmployeePortraitGeometry.CanvasUnitsPerPixel } );
		var title = table.Control( 0x520 );
		// [EXT:online-coop-commands] a guest's title draws the name it asked for until it comes back (C4c-2)
		screen.Add( new UiLabel { Id = "title", Clicked = OpenEmployeeRenameDialog, Text = () => Employee is { } member ? DrawnStaffName( member ) ?? StaffName( member ) : "", Help = strings.Help( title.Help ), Font = fonts => fonts.Title,
			Shadow = true, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = title.Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x004b53c0 stats frame] the labels take UITEXT 64 (0x51a), 65 (0x51d), 67 (0x519), 69 (0x518), 68 (0x51c) and 66 (0x516); the value 0x51b is text,
		// 0x51e a money cell (0x490250), the bars 0x513 (happygrad.wct), 0x514 and 0x515 (ridestatbar.wct)
		void Label( uint id, int text )
		{
			var node = table.Control( id );
			screen.Add( new UiLabel { Id = $"label{id:x}", Text = () => strings[(UIStrings)text], Font = fonts => fonts.Label, Color = UiColors.Text, Fit = true,
				Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		Label( 0x51A, 64 );
		Label( 0x51D, 65 );
		Label( 0x519, 67 );
		Label( 0x518, 69 );
		Label( 0x51C, 68 );
		Label( 0x516, 66 );
		void Value( string id, uint control, Func<string> text )
		{
			screen.Add( new UiLabel { Id = id, Text = text, Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Right, Fit = true,
				Bounds = table.Control( control ).Rect, Anchor = UiAnchor.Center } );
		}
		Value( "employedFor", 0x51B, () => Employee == null ? "" : strings.Format( UiTextRecord.Days, employeeStats.EmployedDays.ToString( CultureInfo.InvariantCulture ) ) );
		Value( "monthlyWage", 0x51E, () => Employee == null ? "" : FormatMoney( employeeStats.MonthlyWage ) );
		Value( "status", 0x517, () => Employee == null ? "" : StaffStatus( employeeStats.Status ) );
		void Bar( string id, uint control, Func<EmployeeStats, int> value, string art = UiGauge.ArtPath ) => screen.Add( new UiGaugeBar { Id = id, Art = art,
			Value = () => Employee == null ? null : value( employeeStats ) / (float)EmployeeWindowRules.GaugeFull, Bounds = table.Control( control ).Rect, Anchor = UiAnchor.Center } );
		Bar( "skillBar", 0x514, stats => stats.SkillGauge );
		Bar( "happinessBar", 0x513, stats => stats.HappinessGauge, UiGauge.HappinessArtPath );
		Bar( "energyBar", 0x515, stats => stats.EnergyGauge );
		var icon = table.Control( 0x51F );
		screen.Add( new FrameIcon { Id = "statusIcon", Model = icon.Model!, Frame = () => Employee == null ? -1 : EmployeeWindowRules.StatusIcon( employeeStats.Status ),
			Bounds = icon.Rect, Anchor = UiAnchor.Center } );
		UiButton Button( string id, string model, Action clicked )
		{
			var node = table.Model( model );
			return screen.Add( new UiButton { Id = id, Model = node.Model, Help = strings.Help( node.Help ), Clicked = clicked, Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		Button( "dismissStaff", "b_fire", ConfirmDismissEmployee );
		Button( "pickUpStaff", "b_pick", () => PickUpEmployee() );
		var route = table.Model( "b_route" );
		screen.Add( new UiButton { Id = "patrol", Model = route.Model, Clicked = () => PressEmployeePatrol(), Selected = () => Employee is { } member && DrawnHasPatrolArea( member ),
			HelpSource = () => strings.Help( Employee is { } member && DrawnHasPatrolArea( member ) ? route.AltHelp : route.Help ), Bounds = route.Rect, Anchor = UiAnchor.Center } );
		Button( "previousEmployee", "b_arup", () => CycleEmployee( false ) );
		Button( "nextEmployee", "b_ardown", () => CycleEmployee( true ) );
		Button( "allStaff", "b_allstaff", ShowAllStaffOfEmployee );
		Button( "locateEmployee", "b_locate", () => LocateEmployee() );
		Button( "exit", "b_okay", CloseEmployeeWindow );
		screen.Back = CloseEmployeeWindow;
		return screen;
	}

	/// <summary>The window's screen: closes when the employee leaves, refreshes the stats every 4 s and the button states every frame.</summary>
	private sealed class EmployeeScreen : UiScreen
	{
		private readonly ParkHud owner;
		public EmployeeScreen( ParkHud owner ) : base( "employee" ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			if ( owner.Employee == null )
			{
				owner.CloseEmployeeWindow();
				return true;
			}
			// [BIN:TPW-EXE:0x004b5d4d window update] message 0x1E, the HUD's pass of every frame (0x54FA03 -> 0x4813b0 -> 0x658f6c), enables the pick-up button and then refreshes the portrait's clone (0x486610)
			owner.level.Portraits.Refresh();
			owner.employeeStatsAge += Math.Max( 0, context.Delta );
			// [BIN:TPW-EXE:0x004b5d97 stats refresh] message 0x10 of timer 0x80080 refreshes the stats frame (0x4b58b0)
			if ( owner.employeeStatsAge >= EmployeeStatsRefreshSeconds )
				owner.RefreshEmployeeStats();
			foreach ( var id in new[] { "previousEmployee", "nextEmployee" } )
				if ( Find( id ) is { } arrow )
					arrow.Visible = owner.employeeArrows;
			if ( Find( "pickUpStaff" ) is { } pickUp )
				pickUp.Enabled = owner.CanPickUpEmployee;
			if ( Find( "patrol" ) is { } patrol )
				patrol.Enabled = owner.CanPressEmployeePatrol;
			if ( Find( "dismissStaff" ) is { } dismiss )
				dismiss.Enabled = !owner.level.IsReadOnlyVisit;
			if ( Find( "locateEmployee" ) is { } locate )
				locate.Enabled = Camera.Mode is ParkCameraMode;
			if ( Find( "title" ) is { } title )
				title.Enabled = !owner.level.IsReadOnlyVisit;
			return base.Update( context, input );
		}
	}

	/// <summary>A model frame chosen by a value; nothing is drawn for a frame the model does not have.</summary>
	private sealed class FrameIcon : UiElement
	{
		public string Model { get; init; } = "";
		public Func<int> Frame { get; init; } = () => 0;
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var frame = Frame();
			if ( frame >= 0 && context.Models.Get( Model ) is { } model && frame < model.Frames.Count )
				context.DrawModel( Model, frame, ScreenRect( context.Canvas ) );
		}
	}
}

/// <summary>The stats frame of the per-employee window as refreshed (bar values on the original's 0..1024 scale).</summary>
public readonly record struct EmployeeStats( long EmployedDays, long MonthlyWage, StaffState Status, int SkillGauge, int HappinessGauge, int EnergyGauge );

/// <summary>The per-employee window's rules (tp.exe 0x4b58b0, 0x485ec0, 0x483740 / 0x483770).</summary>
public static class EmployeeWindowRules
{
	/// <summary>A full bar.</summary>
	public const int GaugeFull = 1024;

	/// <summary>The stats frame of <paramref name="member"/> at fixed tick <paramref name="tick"/> with its monthly wage.</summary>
	// [BIN:TPW-EXE:0x004b58b0 stats refresh] 0x51b: "%d days" (UITEXT 0x1bb) of 0x505b70, the game clock (0x4f8690 on [0x80239c] + 0x2a0) minus the time the
	// staff constructor 0x504b90 stored at +0x1f0, over 864,000,000,000 ticks (one day; 0x67c320); 0x51e: the wage 0x506870; 0x514: the skill 0x506550 × 1024 / 100;
	// 0x513 and 0x515: the low byte of the truncated happiness (+0x1f8) and energy (+0x1fc) × 1024 / 100; 0x517 and 0x51f: the status 0x5064f0, 0 instead of 1 without a patrol area of its own (0x506eb0)
	public static EmployeeStats Stats( StaffMember member, long tick, long wage ) => new(
		EmployedDays( tick, member.HiredTick ), wage, DisplayStatus( member ),
		ParkStaff.StaffSkill( member ) * GaugeFull / 100, ByteGauge( member.Happiness ), ByteGauge( member.Energy ) );

	/// <summary>Whole park days between the hire and <paramref name="tick"/>.</summary>
	public static long EmployedDays( long tick, long hiredTick ) => (ParkCalendar.Seconds( tick ) - ParkCalendar.Seconds( hiredTick )) / ParkCalendar.SecondsPerDay;

	/// <summary>A 0-100 single on the bar scale: its truncated low byte × 1024 / 100.</summary>
	public static int ByteGauge( float value ) => (((int)value & 0xff) << 10) / 100;

	/// <summary>The status the window shows: patrolling without an own patrol area shows as idle.</summary>
	public static StaffState DisplayStatus( StaffMember member ) => member.State == StaffState.Patrolling && !member.HasPatrolArea ? StaffState.Idle : member.State;

	/// <summary>The frame of the staffstat icon for a status (0x485ec0): idle and patrolling 0, working 1, resting 2, on strike 3, else 9.</summary>
	public static int StatusIcon( StaffState status ) => status switch
	{
		StaffState.Idle or StaffState.Patrolling => 0,
		StaffState.Working => 1,
		StaffState.Resting => 2,
		StaffState.OnStrike => 3,
		_ => 9,
	};

	/// <summary>
	/// Down walks the pool's newest-first used chain, up walks it backwards. A pool-free fixture retains reverse roster order.
	/// Unpooled/mismatched members follow valid pool members in reverse roster order; queued and placeholder slots never qualify.
	/// </summary>
	// [BIN:TPW-EXE:0x004824d0 next thing] mask 1 accepts types 4-8 (0x481f80), following older links +8 from head 0x7cf56c;
	// 0x483740 wraps from the head, 0x483770 from the tail. Thing ids are reusable slots, never chronology.
	public static StaffMember? Cycle( IReadOnlyList<StaffMember> members, int currentId, bool forward, ThingPool? things = null )
	{
		var ordered = new List<StaffMember>();
		var included = new HashSet<StaffMember>();
		if ( things != null )
		{
			var claims = members.Where( member => member.ThingId != 0 ).ToLookup( member => member.ThingId );
			foreach ( var slot in things.UsedNewestFirst() )
			{
				if ( things.IsQueued( slot.Id ) || things.IsHeld( slot.Id ) ) continue;
				var matches = claims[slot.Id].Where( member => ParkStaff.ThingModelOf( member.Type ) == slot.Model ).ToArray();
				// Duplicate claims are malformed: leave them to the stable host fallback rather than inventing pool order.
				if ( matches.Length == 1 && included.Add( matches[0] ) ) ordered.Add( matches[0] );
			}
		}
		for ( var index = members.Count - 1; index >= 0; index-- )
		{
			var member = members[index];
			if ( (things == null || !things.IsQueued( member.ThingId ) && !things.IsHeld( member.ThingId )) && included.Add( member ) )
				ordered.Add( member );
		}
		if ( ordered.Count == 0 ) return null;
		var current = ordered.FindIndex( member => member.Id == currentId );
		if ( current < 0 ) return forward ? ordered[0] : ordered[^1];
		return ordered[(current + (forward ? 1 : ordered.Count - 1)) % ordered.Count];
	}
}
