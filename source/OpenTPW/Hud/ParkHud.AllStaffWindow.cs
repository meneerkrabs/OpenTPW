using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The All Staff window, built from tp.exe's table 0x750e10: the window4 frame, the list_allstaff list with its five sortable columns (name, current status, monthly wage,
/// skill bar, happiness bar), the five role tabs (b_shandy, b_smech, b_senter, b_sguard, b_sresrcher), the scroll bar, the title box, the frame with the two average-happiness
/// bars, and the park information, all-visitors and all-attractions buttons and the tick.
/// </summary>
// [BIN:TPW-EXE:0x00496620 all staff window] the builder (arguments: the tab, -1 for the last one, and a staff thing to select) records the window as the last info window (0x4a0810 with 2 and 4, the HUD info button's memory),
// applies table 0x750e10 (handler 0x495da0, call 0x65fd0e), gives list 0x321 the row template 0x496c10 / 0x496d20 and five columns (0x6636b2: the two text columns 0 and 1, the number columns 2 to 4), the highlight
// model "hilight" and the sort word 0x75118c, titles the headers 0x10-0x14 with UITEXT 0x65-0x69 (help 0x77-0x7b), checks the tab's button, fills the list from the tab's role (0x481bc0 with the mask 2, 4, 0x10, 8 or 0x20
// and the row adder 0x496320), selects the thing it was given (0x664d2a, 0x665739), builds the average-happiness frame 0x329 (labels 0x32a and 0x32b, bars 0x32d and 0x32c over happygrad.wct), arms the 2000 ms timer
// 0x80083 and posts help 0x11d (285); message 0x15 titles 0x328 with UITEXT 0x64, message 0x14 withdraws the help
// [BIN:TPW-EXE:0x00496620 callers] the callers are the park information window's b_allstaff (0x4a4a5d), the visitors and attractions windows (0x493448, 0x495301), the HUD info button when the last info window was this one
// (0x4a09a8), the employee window (0x4b5de6: the member's tab and the member selected), the rest feature window (0x4b5173: its own tab; OpenTPW has no such window) and the S key (0x481734: left to the host camera, see ParkHud.ScreenShortcuts.cs)
// [BIN:TPW-EXE:0x00495da0 window procedure] the window has no delete button and no delete flow: its buttons are the tick, park information (0x32e), visitors (0x32f), attractions (0x330) and the role tabs; a row click opens
// the employee window (0x400 → 0x4b6080), a right click centres the camera on the member (0x402 → 0x50b350, 0x4867b0) and closes the window; messages 5 and -2 close it
// [EXT:list-selection-by-id] the selected row follows its member while rows are added or removed, where the original keeps the selected position (+0x14c): the original list has no add or remove path (the thing-removed hook 0x4818c0 acts on guests only, thing kind +2 = 1, and a dismissal goes through the employee
// window, which closes this one first), so a member who leaves while the window is open (a co-op host's dismissal) takes his row with him and the selection moves to the first row
public sealed partial class ParkHud
{
	/// <summary>The name the window's screen has (the advisor's help 285 is keyed by it, <see cref="AdvisorUiHelp"/>).</summary>
	public const string AllStaffScreenName = "allStaff";

	private static LayoutTable AllStaffTable => OriginalLayoutTables.AllStaffWindow;
	private static LayoutNode AllStaffListNode => AllStaffTable.Control( 0x321 );
	/// <summary>The rows' region (the list control's inner rectangle).</summary>
	private static UiRect AllStaffContent => AllStaffListNode.Inner!.Value;
	private static LayoutNode AllStaffSlider => AllStaffListNode.Children.Single( node => node.Type == 3 );
	/// <summary>The five column headers, left to right (controls 0x10-0x14).</summary>
	private static UiRect[] AllStaffColumnRects => allStaffColumnRects ??= AllStaffListNode.Children.Where( node => node.Type == 2 && node.Id is >= 0x10 and < 0x20 )
		.OrderBy( node => node.Id ).Select( node => node.Rect ).ToArray();
	private static UiRect[]? allStaffColumnRects;

	/// <summary>Seconds between two refreshes of the rows' values (timer 0x80083, 2000 ms).</summary>
	public const float AllStaffValueRefreshSeconds = 2f;

	private UiScreen? allStaffScreen;
	private int allStaffTab { get => Sorts.AllStaffTab; set => Sorts.AllStaffTab = value; }
	private int allStaffSortColumn { get => Sorts.AllStaffColumn; set => Sorts.AllStaffColumn = value; }
	private bool allStaffSortDescending { get => Sorts.AllStaffDescending; set => Sorts.AllStaffDescending = value; }
	/// <summary>The member ids in the list's order.</summary>
	private List<int>? allStaffOrderStore;
	private List<int> allStaffOrder => allStaffOrderStore ??= new();
	/// <summary>The cells of each listed member, as its row holds them between refreshes.</summary>
	private Dictionary<int, AllStaffRowValues>? allStaffValueStore;
	private Dictionary<int, AllStaffRowValues> allStaffValues => allStaffValueStore ??= new();
	private int selectedAllStaffId;
	private int allStaffFirstRow;
	private float allStaffValueAge;
	/// <summary>The wage cells' text, formatted once with the row (the wage stays as the row was added), not per frame.</summary>
	private Dictionary<int, string>? allStaffWageTextStore;
	private Dictionary<int, string> allStaffWageText => allStaffWageTextStore ??= new();
	// what the roster looked like when the rows were last synchronised: the members of the tab's role and a sum of their ids (checked every frame without allocating)
	private int allStaffRosterCount = -1;
	private long allStaffRosterSum;
	private float allStaffRowHeight = 30f;
	private int allStaffRoleGauge, allStaffAllGauge;

	public bool AllStaffWindowOpen => allStaffScreen != null && Stack.Screens.Contains( allStaffScreen );
	/// <summary>The role tab shown (0-4: cleaners, mechanics, entertainers, guards, scientists), kept for the session (0x7ca16c).</summary>
	public int AllStaffTab => allStaffTab;
	/// <summary>The sorted column (0-4) and its direction.</summary>
	public (int Column, bool Descending) AllStaffSort => (allStaffSortColumn, allStaffSortDescending);
	public int SelectedAllStaffId => selectedAllStaffId;
	public int AllStaffFirstRow => allStaffFirstRow;
	/// <summary>The list's row pitch in canvas units, as last drawn.</summary>
	public float AllStaffRowHeight => allStaffRowHeight;
	/// <summary>The rows the list shows at once.</summary>
	public int AllStaffVisibleRows => Math.Max( 1, (int)MathF.Floor( AllStaffContent.Height / allStaffRowHeight ) );
	/// <summary>The two bars of the frame under the list: the tab's role and all staff, on the 0..1024 scale, as the window last took them.</summary>
	public (int Role, int All) AllStaffHappinessBars => (allStaffRoleGauge, allStaffAllGauge);
	/// <summary>The members listed, in list order.</summary>
	public IReadOnlyList<StaffMember> AllStaffItems
	{
		get
		{
			var byId = StaffMembers.ToDictionary( member => member.Id );
			return allStaffOrder.Where( byId.ContainsKey ).Select( id => byId[id] ).ToArray();
		}
	}
	/// <summary>The cells of a listed member's row.</summary>
	internal AllStaffRowValues? AllStaffRowOf( int memberId ) => allStaffValues.TryGetValue( memberId, out var values ) ? values : null;
	/// <summary>The label over the role's bar.</summary>
	public string AllStaffRoleLabel => strings[AllStaffRoster.Tabs[allStaffTab].Label];

	private int AllStaffMaxFirstRow => ParkAttractionRoster.MaxFirstRow( allStaffOrder.Count, AllStaffVisibleRows );
	private int AllStaffRowIndex( int id ) => allStaffOrder.IndexOf( id );

	/// <summary>The wage cell's text, formatted the first time the row is drawn and kept (the wage stays as the row was added).</summary>
	private string WageText( int id, long wage )
	{
		if ( !allStaffWageText.TryGetValue( id, out var text ) )
			allStaffWageText[id] = text = FormatMoney( wage );
		return text;
	}

	private void StoreAllStaffRow( StaffMember member )
	{
		allStaffValues[member.Id] = AllStaffValuesFor( member );
		allStaffWageText.Remove( member.Id );
	}

	/// <summary>The cells of a member's row: the name, the status text, the wage and the two bars (the row adder 0x496320).</summary>
	// [BIN:TPW-EXE:0x00496320 row adder] the cells: the staff name (0x506860), the status 0x5064f0 as the STAFFSTATES string (0 instead of 1 without a patrol area of its own, 0x506eb0), the wage 0x506870, the skill 0x506550 × 1024 / 100 and the low byte of the truncated happiness (+0x1f8) × 1024 / 100
	private AllStaffRowValues AllStaffValuesFor( StaffMember member )
	{
		var economy = Status.EconomySource as ParkEconomy;
		var stats = EmployeeWindowRules.Stats( member, economy?.Tick ?? 0, economy?.Staff.MonthlyWage( member ) ?? 0 );
		return AllStaffRoster.ValuesOf( StaffName( member ), StaffStatus( stats.Status ), stats );
	}

	private ThingPool? StaffThings => (Status.EconomySource as ParkEconomy)?.Staff.Things;

	/// <summary>Opens the window on <paramref name="tab"/> (-1: the tab it showed last) with <paramref name="select"/> chosen when it is listed.</summary>
	public void OpenAllStaffWindow( int tab = -1, StaffMember? select = null )
	{
		if ( AllStaffWindowOpen )
			return;
		RememberInfoWindow( InfoWindowKind.AllStaff );
		allStaffTab = AllStaffRoster.ResolveTab( tab, allStaffTab );
		allStaffValueAge = 0;
		FillAllStaffRows();
		// [BIN:TPW-EXE:0x0049687f selected thing] a thing handed to the builder is found among the rows (0x664d2a) and selected (0x665739)
		if ( select != null && AllStaffRowIndex( select.Id ) is >= 0 and var index )
			SelectAllStaff( index );
		allStaffScreen ??= CreateAllStaffScreen();
		Stack.Push( allStaffScreen );
		allStaffScreen.Focus( allStaffScreen.Find( "allStaffList" ) );
	}

	public void CloseAllStaffWindow()
	{
		if ( allStaffScreen == null || !AllStaffWindowOpen )
			return;
		while ( Stack.Top != allStaffScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == allStaffScreen )
			Stack.Pop();
	}

	/// <summary>
	/// Clears the list and adds the tab's members in the fill's order through the sorted insert, selects the first row and takes the frame's two bars (the builder and every tab press do these).
	/// </summary>
	// [BIN:TPW-EXE:0x4960df tab press] a tab's button records the tab (0x7ca16c), clears the list (0x6649d5), fills it from the tab's role (0x481bc0), redraws it (0x663324), sets the label 0x32a and the bar 0x32d; the first row added is chosen (0x66403b → 0x66525c)
	// [BIN:TPW-EXE:0x00496a8f bars (0x496b71 for the role's)] the frame's bars are written when the window is built and by a tab press only: the 2000 ms timer (0x495de0) refreshes the rows alone
	private void FillAllStaffRows()
	{
		allStaffOrder.Clear();
		allStaffValues.Clear();
		allStaffWageText.Clear();
		var type = AllStaffRoster.Tabs[allStaffTab].Type;
		foreach ( var member in AllStaffRoster.Visit( StaffMembers, type, StaffThings ) )
		{
			StoreAllStaffRow( member );
			AllStaffRoster.SortedInsert( allStaffOrder, member.Id, id => allStaffValues[id], allStaffSortColumn, allStaffSortDescending );
		}
		allStaffFirstRow = 0;
		selectedAllStaffId = allStaffOrder.Count > 0 ? allStaffOrder[0] : 0;
		allStaffRoleGauge = AllStaffRoster.RoleHappinessGauge( StaffMembers, type );
		allStaffAllGauge = AllStaffRoster.AllHappinessGauge( StaffMembers );
	}

	/// <summary>Presses a role tab: the list is filled again from that role.</summary>
	public void SelectAllStaffTab( int tab )
	{
		if ( tab is < 0 or >= AllStaffRoster.TabCount )
			return;
		allStaffTab = tab;
		FillAllStaffRows();
	}

	/// <summary>
	/// Keeps the rows and the selection in step with the roster: a member who left drops out, a new one goes in through the sorted insert, and the values refresh when the 2 s timer is due.
	/// </summary>
	private void RefreshAllStaffRows( bool values )
	{
		var type = AllStaffRoster.Tabs[allStaffTab].Type;
		// every frame only the roster's signature is looked at; the rows are rebuilt when it changed and the values when the 2 s timer is due
		var count = 0;
		long sum = 0;
		var members = StaffMembers;
		for ( var index = 0; index < members.Count; index++ )
			if ( members[index].Type == type )
			{
				count++;
				sum += members[index].Id * 2654435761L + 1;
			}
		var changed = count != allStaffRosterCount || sum != allStaffRosterSum || allStaffOrder.Count != count;
		if ( !changed && !values )
			return;
		allStaffRosterCount = count;
		allStaffRosterSum = sum;
		var current = members.Where( member => member.Type == type ).ToDictionary( member => member.Id );
		if ( allStaffOrder.Count != current.Count || allStaffOrder.Any( id => !current.ContainsKey( id ) ) )
		{
			foreach ( var gone in allStaffOrder.Where( id => !current.ContainsKey( id ) ).ToArray() )
			{
				allStaffOrder.Remove( gone );
				allStaffValues.Remove( gone );
				allStaffWageText.Remove( gone );
			}
			foreach ( var member in AllStaffRoster.Visit( StaffMembers, type, StaffThings ) )
			{
				if ( allStaffValues.ContainsKey( member.Id ) )
					continue;
				StoreAllStaffRow( member );
				AllStaffRoster.SortedInsert( allStaffOrder, member.Id, id => allStaffValues[id], allStaffSortColumn, allStaffSortDescending );
			}
		}
		if ( values )
		{
			// [BIN:TPW-EXE:0x00495de0 rows refresh] the timer message rewrites the status, skill and happiness cells of every row in place (mask 0x1a) and never moves a row
			allStaffValueAge = 0;
			foreach ( var member in current.Values )
				allStaffValues[member.Id] = AllStaffRoster.Refreshed( allStaffValues[member.Id], AllStaffValuesFor( member ) );
		}
		EnsureAllStaffSelection();
	}

	/// <summary>Re-derives the selection from the roster: a member who left hands it to the first row.</summary>
	private void EnsureAllStaffSelection()
	{
		if ( !allStaffOrder.Contains( selectedAllStaffId ) )
			selectedAllStaffId = allStaffOrder.Count > 0 ? allStaffOrder[0] : 0;
		SetAllStaffFirstRow( allStaffFirstRow );
	}

	/// <summary>Selects the list row <paramref name="index"/> (clamped) and scrolls it into view.</summary>
	public void SelectAllStaff( int index )
	{
		if ( allStaffOrder.Count == 0 )
		{
			selectedAllStaffId = 0;
			return;
		}
		index = Math.Clamp( index, 0, allStaffOrder.Count - 1 );
		selectedAllStaffId = allStaffOrder[index];
		allStaffFirstRow = Math.Clamp( allStaffFirstRow, Math.Max( 0, index - AllStaffVisibleRows + 1 ), index );
	}

	public void ScrollAllStaff( int rows ) => SetAllStaffFirstRow( allStaffFirstRow + rows );
	public void SetAllStaffFirstRow( int first ) => allStaffFirstRow = Math.Clamp( first, 0, AllStaffMaxFirstRow );

	// [BIN:TPW-EXE:0x665a44 generic list sort, list attribute 0x10] the same column toggles its direction, a new column starts ascending; the sort word 0x75118c starts at 1 (column 0, ascending), so the first press on column 0 descends
	// [BIN:TPW-EXE:0x00495fe0 sort word] message 0x406 writes the list's new sort word back to 0x75118c, so the order is kept for the session (ListSortMemory); the press sorts the rows in place (0x6628f4) and the selected position stays
	public void SortAllStaff( int column = 0 )
	{
		column = Math.Clamp( column, 0, AllStaffRoster.ColumnCount - 1 );
		allStaffSortDescending = allStaffSortColumn == column && !allStaffSortDescending;
		allStaffSortColumn = column;
		var index = AllStaffRowIndex( selectedAllStaffId );
		var sorted = AllStaffRoster.Resort( allStaffOrder, id => allStaffValues[id], column, allStaffSortDescending );
		allStaffOrder.Clear();
		allStaffOrder.AddRange( sorted );
		EnsureAllStaffSelection();
		if ( index >= 0 )
			SelectAllStaff( index );
	}

	private StaffMember? SelectedAllStaffMember => StaffMembers.FirstOrDefault( member => member.Id == selectedAllStaffId );

	/// <summary>A left click on a row: the per-employee window opens on the member and gives way to this list.</summary>
	// [BIN:TPW-EXE:0x6655a2 list click, 0x496056 row message] a click of button 0 selects the row and posts 0x400, which the window routes to 0x4b6080 with the row's thing; that opener closes the open window (0x48cea0), so the list gives way
	public bool OpenSelectedAllStaffEmployee() => SelectedAllStaffMember is { } member && OpenEmployeeWindow( member );

	/// <summary>A right click on a row (and Enter): the camera centres on the member and the window closes.</summary>
	// [BIN:TPW-EXE:0x66563d list click, 0x49600d locate] a click of the other button selects the row and posts 0x402: the member's cell (0x50b350) is the camera's centre (0x4867b0), and the window closes (0x658d9f with message 4) whether or not a camera moved; Enter has no original counterpart ([EXT:overview-enter-locate], the keyboard analogue)
	public bool LocateSelectedAllStaffEmployee()
	{
		if ( SelectedAllStaffMember is { } member && Camera.Mode is ParkCameraMode camera && level.TryGetStaffWorldPosition( member.Id, out var position ) )
		{
			camera.Focus( position );
			lastLocatedStaff[member.Type] = member.Id;
		}
		CloseAllStaffWindow();
		return true;
	}

	private UiScreen CreateAllStaffScreen()
	{
		var screen = new AllStaffScreen( this );
		var table = AllStaffTable;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiModelImage { Id = "listFrame", Model = AllStaffListNode.Model!, Bounds = AllStaffListNode.Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x00495f0d title (message 0x15)] message 0x15 sets the text of 0x328 to UITEXT 0x64 (All Staff)
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.AllStaff], Font = fonts => fonts.Title,
			Shadow = true, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = table.Control( 0x328 ).Rect, Anchor = UiAnchor.Center } );
		var rects = AllStaffColumnRects;
		for ( var column = 0; column < AllStaffRoster.ColumnCount; column++ )
			screen.Add( new AllStaffHeader( this, column ) { Id = $"header{column}", Help = strings.Help( AllStaffRoster.Columns[column].Help ),
				Bounds = rects[column], Anchor = UiAnchor.Center } );
		screen.Add( new AllStaffList( this ) { Id = "allStaffList", Help = strings.Help( AllStaffListNode.Help ), Bounds = AllStaffContent, Anchor = UiAnchor.Center } );
		var slider = AllStaffSlider;
		screen.Add( new AllStaffScrollBar( this ) { Id = "allStaffScroll", Bounds = slider.Rect, Anchor = UiAnchor.Center } );
		// The arrows' parent is the type 3 slider, style 0x10, so they play 0xBD. A held arrow repeats at 500 ms then 125 ms.
		void ScrollArrow( LayoutNode node, int step )
		{
			void Scroll() => ScrollAllStaff( step );
			screen.Add( new UiButton { Id = step < 0 ? "scrollUp" : "scrollDown", Model = node.Model, ClickSound = table.ClickSoundOf( node ),
				Clicked = Scroll, Repeat = UiButton.ScrollArrowRepeat( Scroll ), Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		ScrollArrow( slider.Children.Single( node => node.Model == "b_up" ), -1 );
		ScrollArrow( slider.Children.Single( node => node.Model == "b_down" ), 1 );
		// the role tabs are the radio group 0x322 (type 8, style 0x11): a press plays 0xBD even on the tab already chosen
		for ( var tab = 0; tab < AllStaffRoster.TabCount; tab++ )
		{
			var index = tab;
			var node = table.Control( AllStaffRoster.Tabs[tab].Control );
			screen.Add( new UiButton { Id = $"tab{index}", Model = node.Model, Help = strings.Help( node.Help ), ClickSound = table.ClickSoundOf( node ),
				Clicked = () => SelectAllStaffTab( index ), Selected = () => allStaffTab == index, Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		// [BIN:TPW-EXE:0x004968e4 happiness frame] the frame 0x329 holds the label 0x32a (the tab's UITEXT) over the bar 0x32d and the label 0x32b (UITEXT 0x6a) over the bar 0x32c, both bars over happygrad.wct (0x7508c0), the labels in font slot 6
		var frame = table.Control( 0x329 );
		screen.Add( new UiModelImage { Id = "happinessFrame", Model = frame.Model!, Bounds = frame.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "roleLabel", Text = () => AllStaffRoleLabel, Font = fonts => fonts.Label, Color = UiColors.Text, Fit = true,
			Bounds = table.Control( 0x32A ).Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "allLabel", Text = () => strings[UIStrings.AvgHappinessStaff], Font = fonts => fonts.Label, Color = UiColors.Text, Fit = true,
			Bounds = table.Control( 0x32B ).Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiGaugeBar { Id = "roleBar", Art = UiGauge.HappinessArtPath, Value = () => allStaffRoleGauge / (float)EmployeeWindowRules.GaugeFull,
			Bounds = table.Control( 0x32D ).Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiGaugeBar { Id = "allBar", Art = UiGauge.HappinessArtPath, Value = () => allStaffAllGauge / (float)EmployeeWindowRules.GaugeFull,
			Bounds = table.Control( 0x32C ).Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x00495f45 buttons] the tick (-2) closes the window; b_pkinfo (0x32e) opens the park information (0x4a52a0), b_kids (0x32f) the All Visitors window (0x493530) and b_allthings (0x330)
		// the All Attractions window on its last category (0x495aa0 with -1), each replacing this one
		var okay = table.Model( "b_okay" );
		screen.Add( new UiButton { Id = "exit", Model = okay.Model, Help = strings.Help( okay.Help ), ClickSound = table.ClickSoundOf( okay ),
			Clicked = CloseAllStaffWindow, Bounds = okay.Rect, Anchor = UiAnchor.Center } );
		var parkInfo = table.Model( "b_pkinfo" );
		screen.Add( new UiButton { Id = "parkInfo", Model = parkInfo.Model, Help = strings.Help( parkInfo.Help ), Bounds = parkInfo.Rect, Anchor = UiAnchor.Center,
			Clicked = () => { CloseAllStaffWindow(); OpenParkInfoWindow(); } } );
		var visitors = table.Model( "b_kids" );
		screen.Add( new UiButton { Id = "allVisitors", Model = visitors.Model, Help = strings.Help( visitors.Help ), Bounds = visitors.Rect, Anchor = UiAnchor.Center,
			Clicked = () => { CloseAllStaffWindow(); OpenVisitorWindow(); } } );
		var allThings = table.Model( "b_allthings" );
		screen.Add( new UiButton { Id = "allAttractions", Model = allThings.Model, Help = strings.Help( allThings.Help ), Bounds = allThings.Rect, Anchor = UiAnchor.Center,
			Clicked = () => { CloseAllStaffWindow(); OpenAttractionsWindow(); } } );
		screen.Back = CloseAllStaffWindow;
		return screen;
	}

	/// <summary>The All Staff window screen: the wheel scrolls the list, the rows follow the roster, the values refresh every 2 s.</summary>
	private sealed class AllStaffScreen : UiScreen
	{
		private readonly ParkHud owner;
		public AllStaffScreen( ParkHud owner ) : base( AllStaffScreenName ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			owner.allStaffValueAge += Math.Max( 0, context.Delta );
			owner.RefreshAllStaffRows( values: owner.allStaffValueAge >= AllStaffValueRefreshSeconds );
			if ( input.Wheel != 0 )
			{
				owner.ScrollAllStaff( input.Wheel > 0 ? -1 : 1 );
				input = input with { Wheel = 0 };
			}
			return base.Update( context, input );
		}
	}

	/// <summary>The staff rows: the five columns under their headers.</summary>
	private sealed class AllStaffList : UiElement, IUiRowList
	{
		private readonly ParkHud owner;
		public AllStaffList( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		public int Selected => owner.AllStaffRowIndex( owner.selectedAllStaffId );
		public bool ArrowsSelect => true;
		public void Select( int index ) => owner.SelectAllStaff( index );
		public void Scroll( int rows ) => owner.ScrollAllStaff( rows );
		public bool HoverSelects => true;
		public int HoveredRow { get; private set; } = -1;
		public void Hover( int index ) => HoveredRow = index;
		// [BIN:TPW-EXE:0x665c35 list dispatcher] attribute 0x291 lists: a left click selects the row and posts 0x400 (the employee window); a right click selects the row and posts 0x402 (centre the camera, close)
		public bool RowClicked( int index, bool left )
		{
			Select( index );
			return left ? owner.OpenSelectedAllStaffEmployee() : owner.LocateSelectedAllStaffEmployee();
		}
		// Enter is the keyboard analogue of the right click, as in the All Visitors list ([EXT:overview-enter-locate]); it plays the click sound of the list's rows as there
		public override void Activate()
		{
			GameAudio.PlayVisitorClick();
			owner.LocateSelectedAllStaffEmployee();
		}
		public override void Adjust( int direction ) => owner.SelectAllStaff( Math.Max( 0, Selected ) + direction );
		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			var content = canvas.Map( AllStaffContent, Anchor );
			if ( !content.Contains( point ) )
				return -1;
			var visible = (int)MathF.Floor( (point.Y - content.Y) / (owner.allStaffRowHeight * canvas.Scale) );
			var index = owner.allStaffFirstRow + visible;
			return visible < 0 || visible >= owner.AllStaffVisibleRows || index >= owner.allStaffOrder.Count ? -1 : index;
		}
		private UiRect Cell( UiCanvas canvas, UiRect[] columns, int column, float y, float rowHeight )
		{
			var origin = canvas.Map( new NVector2( columns[column].X, y ), Anchor );
			return new UiRect( origin.X, origin.Y, columns[column].Width * canvas.Scale, rowHeight * canvas.Scale );
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var canvas = context.Canvas;
			// [BIN:TPW-EXE:0x00496c26 staff row height] a row item is font slot 7's height scaled to the 1536-unit canvas, plus 6 (0x7ca170), as the attractions rows are
			owner.allStaffRowHeight = context.Fonts.Small.LineHeight * canvas.TextScale / Math.Max( 0.001f, canvas.Scale ) + 6;
			var content = AllStaffContent;
			var columns = AllStaffColumnRects;
			var rowHeight = owner.allStaffRowHeight;
			var order = owner.allStaffOrder;
			for ( var visible = 0; visible < owner.AllStaffVisibleRows && owner.allStaffFirstRow + visible < order.Count; visible++ )
			{
				var index = owner.allStaffFirstRow + visible;
				var id = order[index];
				if ( !owner.allStaffValues.TryGetValue( id, out var values ) )
					continue;
				var y = content.Y + visible * rowHeight;
				var row = canvas.Map( Rect( content.X, y, content.Right, y + rowHeight ), Anchor );
				var selected = id == owner.selectedAllStaffId;
				if ( index == HoveredRow && !selected )
					context.Batch.AddRectangle( row, new Veldrid.RgbaByte( 60, 90, 200, 60 ) );
				if ( selected )
					context.Batch.AddRectangle( row, new Veldrid.RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				var color = selected ? UiColors.Highlight : UiColors.Text;
				context.DrawFittedText( context.Fonts.Small, values.Name, Cell( canvas, columns, 0, y, rowHeight ), color, AllStaffRoster.Columns[0].Align );
				context.DrawFittedText( context.Fonts.Small, values.Status, Cell( canvas, columns, 1, y, rowHeight ), color, AllStaffRoster.Columns[1].Align );
				context.DrawFittedText( context.Fonts.Small, owner.WageText( id, values.Wage ), Cell( canvas, columns, 2, y, rowHeight ), color, AllStaffRoster.Columns[2].Align );
				// [BIN:TPW-EXE:0x00496d20 bar cells] the skill cell is a type-9 control over ridestatbar.wct (0x750e00), the happiness cell one over happygrad.wct (0x7508c0), both on the 0..1024 scale
				var skill = Cell( canvas, columns, 3, y, rowHeight );
				UiGauge.Draw( context.Batch, skill.Inflate( -skill.Height * 0.2f ), values.SkillGauge / (float)EmployeeWindowRules.GaugeFull );
				var happiness = Cell( canvas, columns, 4, y, rowHeight );
				UiGauge.Draw( context.Batch, happiness.Inflate( -happiness.Height * 0.2f ), values.HappinessGauge / (float)EmployeeWindowRules.GaugeFull, UiGauge.HappinessArtPath );
			}
		}
	}

	/// <summary>A column header: a press sorts by its column, a second press reverses the order.</summary>
	private sealed class AllStaffHeader : UiElement
	{
		private readonly ParkHud owner;
		private readonly int column;
		public AllStaffHeader( ParkHud owner, int column ) { this.owner = owner; this.column = column; }
		public override bool Focusable => Visible && Enabled;
		// The header's parent is list 0x321 (type 7, style 0x291), which has bit 0x10, so the hook plays 0xBD.
		public override void Activate()
		{
			GameAudio.PlayUi( LayoutTable.ClickSoundFor( AllStaffListNode ) );
			owner.SortAllStaff( column );
		}
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			context.DrawFittedText( context.Fonts.Label, owner.strings[AllStaffRoster.Columns[column].Header],
				ScreenRect( context.Canvas ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
	}

	/// <summary>The vertical scroll bar: the table's slider track and knob.</summary>
	private sealed class AllStaffScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public AllStaffScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private static UiRect Knob => AllStaffSlider.Children.Single( node => node.Model == "b_scroller" ).Rect;
		private UiRect KnobRect()
		{
			var track = AllStaffSlider.Rect;
			var travel = track.Height - Knob.Height;
			var top = track.Y + (owner.AllStaffMaxFirstRow == 0 ? 0 : travel * owner.allStaffFirstRow / owner.AllStaffMaxFirstRow);
			return Knob with { Y = top };
		}
		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			if ( rect.Height <= 0 || owner.AllStaffMaxFirstRow == 0 )
				return;
			var track = AllStaffSlider.Rect;
			var y = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (y - track.Y - Knob.Height / 2) / (track.Height - Knob.Height), 0, 1 );
			owner.SetAllStaffFirstRow( (int)MathF.Round( fraction * owner.AllStaffMaxFirstRow ) );
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( AllStaffSlider.Model2 ?? "!slider", 0, context.Canvas.Map( AllStaffSlider.Rect, Anchor ) );
			context.DrawModel( "b_scroller", owner.AllStaffMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}
}
