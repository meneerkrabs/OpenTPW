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
// [APPROX:UI-092] the portrait frame (0x511) shows the role's name where the original plays a live figure of the employee (0x4b5330: 0x486530 with the frame's figure id + 1 at speed 8.0); a click on the title does not rename the employee (0x48d2e0, help 85); the help 0xd4 the window posts while the employee has a patrol area (0x4b5ab0) is not posted; the arrows cycle the economy's member list, which stands for the thing list (0x7cf56c, newest first) — evidence needed: drawing the figure, porting the rename dialog, and the thing-list order of staff loaded from a save
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
		Stack.Push( employeeScreen );
		employeeScreen.Focus( employeeScreen.Find( "exit" ) );
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
		var next = EmployeeWindowRules.Cycle( StaffMembers, member.Id, forward );
		if ( next == null || next.Id == member.Id )
			return false;
		employeeId = next.Id;
		RefreshEmployeeStats();
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
		if ( !placement.PickUp( member ) )
			return false;
		level.EndBuildTools();
		CloseEmployeeWindow();
		return true;
	}

	/// <summary>Whether the patrol button acts: clearing an employee's own area, or setting one with the patrol tool.</summary>
	public bool CanPressEmployeePatrol => Employee is { } member && !level.IsReadOnlyVisit && (member.HasPatrolArea || level.PatrolTool != null);

	/// <summary>The patrol toggle (b_route): checked while the employee has a patrol area; pressing it sets one (the patrol tool) or clears it.</summary>
	// [BIN:TPW-EXE:0x004b5e34 patrol toggle] control 0x510 (b_route, attribute 0x11: a toggle, help 86 / 87) passes its new state to 0x4b5b50: 1 holds the member (0x48c950),
	// sets mouse mode 0x36 (0x46c580), closes the window and posts help 0xc1; 0 clears the area (0x506e90) and the window stays; the refresh 0x4b5ab0 checks it when 0x506eb0
	// finds an own area (corner words +0x20a/+0x20c other than 1 and 0x4000)
	public bool PressEmployeePatrol()
	{
		if ( !CanPressEmployeePatrol || Employee is not { } member )
			return false;
		return member.HasPatrolArea ? ClearPatrolArea( member ) : SetPatrolArea( member );
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
		return true;
	}

	private bool ClearPatrolArea( StaffMember member )
	{
		if ( RejectReadOnlyAction() )
			return false;
		member.ClearPatrolArea();
		return true;
	}

	/// <summary>Dismisses the window's employee through the park economy and closes the window.</summary>
	public bool DismissEmployee() => Employee is { } member && DismissStaff( member.Id );

	private bool DismissStaff( int staffId )
	{
		if ( RejectReadOnlyAction() || Status.EconomySource is not ParkEconomy economy || economy.Staff.Members.All( item => item.Id != staffId ) )
			return false;
		economy.Fire( staffId );
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
		if ( !GameOptions.Current.Confirmations )
		{
			DismissStaff( member.Id );
			return;
		}
		var staffId = member.Id;
		CloseEmployeeWindow();
		Stack.Push( UiDialogs.Message( "confirmDismissStaff", () => strings[UIStrings.DismissEmployee],
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
		if ( Employee is not { } member || Camera.Mode is not ParkCameraMode || !level.TryGetStaffWorldPosition( member.Id, out _ ) )
			return false;
		lastLocatedStaff[member.Type] = member.Id;
		OpenLocator( LocatorMode.Staff, LocatorModel.RoleOf( member.Type ), member.Id );
		return true;
	}

	private string EmployeeRole => Employee is { } member ? StaffRole( member.Type ) : "";

	private UiScreen CreateEmployeeScreen()
	{
		var screen = new EmployeeScreen( this );
		var table = EmployeeTable;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		var portrait = table.Control( 0x511 );
		var stats = table.Control( 0x512 );
		screen.Add( new UiModelImage { Id = "portraitFrame", Model = portrait.Model!, Bounds = portrait.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiModelImage { Id = "statsFrame", Model = stats.Model!, Bounds = stats.Rect, Anchor = UiAnchor.Center } );
		// [APPROX:UI-092] the portrait frame shows the role's name, not the live figure
		screen.Add( new UiLabel { Id = "portraitText", Text = () => EmployeeRole, Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
			Bounds = portrait.Rect, Anchor = UiAnchor.Center } );
		var title = table.Control( 0x520 );
		screen.Add( new UiLabel { Id = "title", Text = () => Employee is { } member ? StaffName( member ) : "", Help = strings.Help( title.Help ), Font = fonts => fonts.Title,
			Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = title.Rect, Anchor = UiAnchor.Center } );
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
		void Bar( string id, uint control, Func<EmployeeStats, int> value ) => screen.Add( new UiGaugeBar { Id = id,
			Value = () => Employee == null ? null : value( employeeStats ) / (float)EmployeeWindowRules.GaugeFull, Bounds = table.Control( control ).Rect, Anchor = UiAnchor.Center } );
		Bar( "skillBar", 0x514, stats => stats.SkillGauge );
		Bar( "happinessBar", 0x513, stats => stats.HappinessGauge );
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
		screen.Add( new UiButton { Id = "patrol", Model = route.Model, Clicked = () => PressEmployeePatrol(), Selected = () => Employee is { HasPatrolArea: true },
			HelpSource = () => strings.Help( Employee is { HasPatrolArea: true } ? route.AltHelp : route.Help ), Bounds = route.Rect, Anchor = UiAnchor.Center } );
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
				locate.Enabled = Camera.Mode is ParkCameraMode && owner.level.TryGetStaffWorldPosition( owner.employeeId, out _ );
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
	/// The employee the arrows move to from <paramref name="currentId"/>: <paramref name="forward"/> the next older one, wrapping to the newest, else the next
	/// newer one, wrapping to the oldest. <paramref name="members"/> is in hire order, the reverse of the thing list.
	/// </summary>
	// [BIN:TPW-EXE:0x004824d0 next thing] mask 1 walks the thing list (head 0x7cf56c; 0x516270 puts a new thing at the head, so it runs newest first) from the
	// member's next node to the first staff thing (0x481f80: types 4-8), and 0x483740 starts again at the head when none follows; 0x482da0 walks the previous
	// nodes and 0x483770 starts again at the tail
	public static StaffMember? Cycle( IReadOnlyList<StaffMember> members, int currentId, bool forward )
	{
		if ( members.Count == 0 )
			return null;
		var index = -1;
		for ( var position = 0; position < members.Count; position++ )
			if ( members[position].Id == currentId )
				index = position;
		if ( index < 0 )
			return forward ? members[^1] : members[0];
		return forward ? members[index > 0 ? index - 1 : members.Count - 1] : members[index < members.Count - 1 ? index + 1 : 0];
	}
}
