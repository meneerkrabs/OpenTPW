using System.Globalization;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>The staff window from the Mac layout at <c>data:0x4a1e0</c>, in authored 2048x1536 coordinates.</summary>
public sealed partial class ParkHud
{
	private static readonly UiRect StaffContent = Rect( 1043, 441, 1729, 911 );
	private static readonly UiRect StaffScrollTrack = Rect( 1743, 398, 1802, 843 );
	private static readonly UiRect StaffScrollKnob = Rect( 1735, 651, 1802, 718 );
	private static readonly (StaffType Type, string Model, int Help, UiRect Rect)[] StaffRoles =
	{
		(StaffType.Handyman, "b_shandy", 154, Rect( 1164, 195, 1267, 297 )),
		(StaffType.Mechanic, "b_smech", 155, Rect( 1283, 195, 1386, 297 )),
		(StaffType.Entertainer, "b_senter", 156, Rect( 1402, 195, 1504, 297 )),
		(StaffType.Guard, "b_sguard", 157, Rect( 1521, 195, 1623, 297 )),
		(StaffType.Researcher, "b_sresrcher", 158, Rect( 1640, 195, 1742, 297 )),
	};
	private static readonly (UIStrings Label, UiRect LabelRect, UiRect ValueRect)[] StaffStatRows =
	{
		(UIStrings.CurrentStatusStaff, Rect( 283, 714, 679, 759 ), Rect( 722, 714, 957, 759 )),
		(UIStrings.MonthlyWageStaff, Rect( 283, 765, 679, 810 ), Rect( 722, 765, 957, 810 )),
		(UIStrings.SkillStaff, Rect( 283, 816, 679, 861 ), Rect( 722, 816, 957, 861 )),
		(UIStrings.HappinessStaff, Rect( 283, 868, 679, 912 ), Rect( 722, 868, 957, 912 )),
	};

	private UiScreen? staffScreen;
	private UiButton? dismissStaffButton;
	private UiButton? pickUpStaffButton;
	private StaffType? staffFilter = StaffType.Handyman;
	private bool showingEmployedStaff;
	private bool staffSortByWage;
	private bool staffSortDescending;
	private int selectedStaffId;
	private int staffFirstRow;
	private Dictionary<StaffType, string[]>? staffNames;
	private string[]? staffTypes;
	private string[]? staffStates;
	// [APPROX:UI-080] ten equal staff rows fill the table's 470-unit content area; wheel movement is one row — evidence needed: original staff-list capture.
	public const int StaffVisibleRows = 10;
	public const float StaffRowHeight = 470f / StaffVisibleRows;

	public bool StaffWindowOpen => staffScreen != null && Stack.Screens.Contains( staffScreen );
	public StaffType? StaffFilter => staffFilter;
	public bool ShowingEmployedStaff => showingEmployedStaff;
	public int SelectedStaffId => SelectedStaff?.Id ?? 0;
	public int SelectedCandidateId => SelectedCandidate?.Id ?? 0;
	public int StaffFirstRow => staffFirstRow;
	public IReadOnlyList<StaffMember> StaffItems
	{
		get
		{
			var members = (Status.EconomySource as ParkEconomy)?.Staff.Members
				.Where( member => staffFilter == null || member.Type == staffFilter ) ?? Enumerable.Empty<StaffMember>();
			return staffSortByWage
				? (staffSortDescending ? members.OrderByDescending( StaffWageAmount ) : members.OrderBy( StaffWageAmount )).ThenBy( member => member.Id ).ToArray()
				: (staffSortDescending ? members.OrderByDescending( StaffName ) : members.OrderBy( StaffName )).ThenBy( member => member.Id ).ToArray();
		}
	}
	public IReadOnlyList<StaffCandidate> StaffCandidates
	{
		get
		{
			var candidates = (Status.EconomySource as ParkEconomy)?.Staff.Candidates
				.Where( candidate => staffFilter == null || candidate.Type == staffFilter ) ?? Enumerable.Empty<StaffCandidate>();
			return staffSortByWage
				? (staffSortDescending ? candidates.OrderByDescending( CandidateWageAmount ) : candidates.OrderBy( CandidateWageAmount )).ThenBy( candidate => candidate.Id ).ToArray()
				: (staffSortDescending ? candidates.OrderByDescending( CandidateName ) : candidates.OrderBy( CandidateName )).ThenBy( candidate => candidate.Id ).ToArray();
		}
	}
	public StaffMember? SelectedStaff => StaffItems.FirstOrDefault( member => member.Id == selectedStaffId );
	public StaffCandidate? SelectedCandidate => StaffCandidates.FirstOrDefault( candidate => candidate.Id == selectedStaffId );
	private int StaffListCount => showingEmployedStaff ? StaffItems.Count : StaffCandidates.Count;
	private int StaffMaxFirstRow => Math.Max( 0, StaffListCount - StaffVisibleRows );

	public void OpenStaffWindow()
	{
		if ( StaffWindowOpen )
			return;
		staffFilter = StaffType.Handyman;
		ShowEmployedStaff( false );
		staffScreen ??= CreateStaffScreen();
		Stack.Push( staffScreen );
		staffScreen.Focus( staffScreen.Find( "staffList" ) );
	}

	public void CloseStaffWindow()
	{
		if ( staffScreen == null || !StaffWindowOpen )
			return;
		while ( Stack.Top != staffScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == staffScreen )
			Stack.Pop();
	}

	public void FilterStaff( StaffType? type )
	{
		staffFilter = type;
		staffFirstRow = 0;
		selectedStaffId = 0;
		EnsureStaffSelection();
	}

	/// <summary>Switches between the original applicant list and a view of existing employees.</summary>
	// [EXT:staff-employee-view] The original hire window only lists applicants; its employee list is the separate All Staff window
	// (table 0x750e10, screen 0x11d, S key), which OpenTPW does not port. This view of existing employees inside the hire window,
	// reached by the labelled "staffViewExt" button, is an OpenTPW extension that also hosts the dismiss button.
	public void ShowEmployedStaff( bool employed )
	{
		showingEmployedStaff = employed;
		staffFirstRow = 0;
		selectedStaffId = 0;
		EnsureStaffSelection();
	}

	// [BIN:TPW-EXE:0x665a44 generic list sort, list attribute 0x10] the same column toggles its direction, a new column starts ascending; the initial sort column is 0 (ascending), so the first press on column 0 descends.
	// [APPROX:UI-084] the initial column state of each list (column 0 ascending here) — evidence needed: the initial state of each original list
	public void SortStaff( bool byWage )
	{
		staffSortDescending = staffSortByWage == byWage && !staffSortDescending;
		staffSortByWage = byWage;
		EnsureStaffSelection();
		var selectedIndex = showingEmployedStaff
			? StaffItems.ToList().FindIndex( member => member.Id == selectedStaffId )
			: StaffCandidates.ToList().FindIndex( candidate => candidate.Id == selectedStaffId );
		if ( selectedIndex >= 0 )
			SelectStaff( selectedIndex );
	}

	/// <summary>
	/// Hires the still-available selected applicant: the window closes and the Place tool starts with the applicant still in the pool; the employee
	/// exists only after a click on a valid cell (docs/STAFF-PLACEMENT.md). Without a park view the economy hires at once.
	/// </summary>
	// [BIN:TPW-EXE:0x507bf0 hire] the hire creates no object, it builds the place tool; [BIN:TPW-EXE:0x49b9dd hire handler] the handler posts advisor message 0x135 and closes the window
	// [APPROX:UI-097] the advisor message 0x135 (309, "place your new staff member") is not in OpenTPW's advisor table (its text is filled at runtime and its row is unbound) and the queue cannot withdraw advice: the hint is an OpenTPW line of the message area, shown when the tool starts — evidence needed: the descriptor row and text of message 309
	public bool HireSelectedStaff()
	{
		if ( showingEmployedStaff || RejectReadOnlyAction() || Status.EconomySource is not ParkEconomy economy || economy.IsBankrupt )
			return false;
		var candidate = SelectedCandidate;
		if ( candidate == null || !economy.Staff.CanHire( candidate.Type ) || economy.Staff.Candidates.All( item => item.Id != candidate.Id ) )
			return false;
		if ( level.StaffPlacement is { } placement && ReferenceEquals( level.Park?.Economy, economy ) )
		{
			if ( !placement.BeginHire( candidate.Id ) )
				return false;
			PostMessage( strings.Extra( OpenTpwText.PlaceStaff ) );
			CloseStaffWindow();
		}
		else
			economy.Hire( candidate.Id );
		selectedStaffId = 0;
		EnsureStaffSelection();
		return true;
	}

	/// <summary>Whether the selected employee may be picked up now (tp.exe <c>CanBePickedUp</c> 0x505bd0, read each tick by the window).</summary>
	public bool CanPickUpSelectedStaff => showingEmployedStaff && !level.IsReadOnlyVisit && SelectedStaff is { } member
		&& level.StaffPlacement != null && level.Guests?.Grid.Cells is { } cells && StaffPlacementRules.CanBePickedUp( member, cells );

	/// <summary>The pick-up button: puts the selected employee in the player's hand, closes the window and starts the Carry tool.</summary>
	// [BIN:TPW-EXE:0x4b5ed9 pick-up button] control 0x50e of the staff info window runs 0x505c50 and the window closes
	public bool PickUpSelectedStaff()
	{
		if ( !showingEmployedStaff || RejectReadOnlyAction() || SelectedStaff is not { } member || level.StaffPlacement is not { } placement )
			return false;
		if ( !placement.PickUp( member ) )
			return false;
		CloseStaffWindow();
		return true;
	}

	/// <summary>Dismisses the selected employee through the existing park economy operation.</summary>
	public bool DismissSelectedStaff()
	{
		if ( !showingEmployedStaff || RejectReadOnlyAction() || Status.EconomySource is not ParkEconomy economy )
			return false;
		var member = SelectedStaff;
		if ( member == null || economy.Staff.Members.All( item => item.Id != member.Id ) )
			return false;
		economy.Fire( member.Id );
		selectedStaffId = 0;
		EnsureStaffSelection();
		return true;
	}

	private void ConfirmDismissSelectedStaff()
	{
		if ( !showingEmployedStaff || SelectedStaff == null || RejectReadOnlyAction() )
			return;
		Stack.Push( UiDialogs.Message( "confirmDismissStaff", () => strings[UIStrings.DismissEmployee],
			(() => strings.Value( UIStrings.Yes ), () => { Stack.Pop(); DismissSelectedStaff(); }),
			(() => strings.Value( UIStrings.No ), Stack.Pop) ) );
	}

	public void SelectStaff( int index )
	{
		var count = StaffListCount;
		if ( count == 0 )
		{
			selectedStaffId = 0;
			return;
		}
		index = Math.Clamp( index, 0, count - 1 );
		selectedStaffId = showingEmployedStaff ? StaffItems[index].Id : StaffCandidates[index].Id;
		staffFirstRow = Math.Clamp( staffFirstRow, Math.Max( 0, index - StaffVisibleRows + 1 ), index );
	}

	public void ScrollStaff( int rows ) => SetStaffFirstRow( staffFirstRow + rows );
	public void SetStaffFirstRow( int first ) => staffFirstRow = Math.Clamp( first, 0, StaffMaxFirstRow );

	private void EnsureStaffSelection()
	{
		var ids = showingEmployedStaff ? StaffItems.Select( member => member.Id ) : StaffCandidates.Select( candidate => candidate.Id );
		var visibleIds = ids.ToArray();
		if ( visibleIds.Length == 0 )
			selectedStaffId = 0;
		else if ( !visibleIds.Contains( selectedStaffId ) )
			selectedStaffId = visibleIds[0];
		SetStaffFirstRow( staffFirstRow );
		if ( dismissStaffButton != null )
			dismissStaffButton.Enabled = showingEmployedStaff && selectedStaffId != 0;
		if ( pickUpStaffButton != null )
			pickUpStaffButton.Enabled = CanPickUpSelectedStaff;
	}

	private string StaffName( StaffMember member ) => StaffName( member.Type, member.NameIndex, member.Id );
	private string CandidateName( StaffCandidate candidate ) => StaffName( candidate.Type, candidate.NameIndex, candidate.Id );
	private string StaffName( StaffType type, int nameIndex, int id )
	{
		// [DATA:Language/*/*_NAMES.str] NameIndex is the role-specific original name-table index.
		staffNames ??= LoadStaffNames();
		var names = staffNames[type];
		return nameIndex >= 0 && nameIndex < names.Length && !string.IsNullOrWhiteSpace( names[nameIndex] )
			? names[nameIndex] : $"{type} {id}";
	}

	private static Dictionary<StaffType, string[]> LoadStaffNames()
	{
		var language = GameLanguage.Current;
		return new Dictionary<StaffType, string[]>
		{
			[StaffType.Handyman] = language.LoadStrings( "HANDYMAN_NAMES.str" ).Entries,
			[StaffType.Mechanic] = language.LoadStrings( "MECHANIC_NAMES.str" ).Entries,
			[StaffType.Entertainer] = language.LoadStrings( "ENTERTAINER_NAMES.str" ).Entries,
			[StaffType.Guard] = language.LoadStrings( "GUARD_NAMES.str" ).Entries,
			[StaffType.Researcher] = language.LoadStrings( "RESEARCHER_NAMES.str" ).Entries,
		};
	}

	private long StaffWageAmount( StaffMember member ) => (Status.EconomySource as ParkEconomy)?.Staff.MonthlyWage( member ) ?? 0;
	private long CandidateWageAmount( StaffCandidate candidate ) => (Status.EconomySource as ParkEconomy)?.Settings.GetMonthlyWage( candidate.Type, candidate.Grade ) ?? 0;
	private string StaffWage( StaffMember member ) => FormatMoney( StaffWageAmount( member ) );
	private string CandidateWage( StaffCandidate candidate ) => FormatMoney( CandidateWageAmount( candidate ) );
	private string StaffRole( StaffType type )
	{
		staffTypes ??= GameLanguage.Current.LoadStrings( "STAFF_TYPES.str" ).Entries;
		return (int)type < staffTypes.Length && !string.IsNullOrWhiteSpace( staffTypes[(int)type] ) ? staffTypes[(int)type] : type.ToString();
	}
	private string StaffStatus( StaffState state )
	{
		staffStates ??= GameLanguage.Current.LoadStrings( "STAFFSTATES.str" ).Entries;
		return (int)state < staffStates.Length && !string.IsNullOrWhiteSpace( staffStates[(int)state] ) ? staffStates[(int)state] : state.ToString();
	}

	private string StaffStat( StaffMember member, int row ) => row switch
	{
		0 => StaffStatus( member.State ),
		1 => StaffWage( member ),
		2 => $"{ParkStaff.StaffSkill( member )}%",
		3 => $"{member.Happiness}%",
		_ => ""
	};
	// [APPROX:UI-081] applicant summary fields use the economy's expected monthly wage and grade-derived skill as text; the original shows a skill gauge (9346 = skill byte × 1024/5) and a mini balance (9349) and no grade or wage text — evidence needed: porting them
	private string CandidateStat( StaffCandidate candidate, int row ) => row switch
	{
		0 => CandidateName( candidate ),
		1 => CandidateWage( candidate ),
		2 => $"{20 * candidate.Grade}%",
		_ => ""
	};
	private string SelectedName => showingEmployedStaff
		? SelectedStaff is { } member ? StaffName( member ) : ""
		: SelectedCandidate is { } candidate ? CandidateName( candidate ) : "";
	private string SelectedGrade => showingEmployedStaff
		? SelectedStaff is { } member ? (member.Grade + 1).ToString( CultureInfo.InvariantCulture ) : ""
		: SelectedCandidate is { } candidate ? (candidate.Grade + 1).ToString( CultureInfo.InvariantCulture ) : "";
	private string SelectedRole => showingEmployedStaff
		? SelectedStaff is { } member ? StaffRole( member.Type ) : ""
		: SelectedCandidate is { } candidate ? StaffRole( candidate.Type ) : "";
	private string SelectedStat( int row ) => showingEmployedStaff
		? SelectedStaff is { } member ? StaffStat( member, row ) : ""
		: SelectedCandidate is { } candidate ? CandidateStat( candidate, row ) : "";
	/// <summary>The hire window's cash label (control 9366): UITEXT 0x1ca "Cash $ N" or 0x1cb "Cash -$ N" with the bare balance magnitude.</summary>
	public string StaffCashText => !Status.HasEconomy ? "" : CashLabel( Status.Money );
	private string CashLabel( long balance )
	{
		var template = strings[balance < 0 ? UIStrings.CashNegativeDollar : UIStrings.CashDollar];
		var number = (balance < 0 ? -(decimal)balance : balance).ToString( CultureInfo.InvariantCulture );
		return template.Contains( "%d" ) ? template.Replace( "%d", number ) : template + number;
	}
	private string HireTitle => strings[(UIStrings)( (int)UIStrings.HireCleaners + (int)( staffFilter ?? StaffType.Handyman ) )];
	private string ListName( int index ) => showingEmployedStaff ? StaffName( StaffItems[index] ) : CandidateName( StaffCandidates[index] );
	private string ListWage( int index ) => showingEmployedStaff ? StaffWage( StaffItems[index] ) : CandidateWage( StaffCandidates[index] );

	private UiScreen CreateStaffScreen()
	{
		var screen = new StaffScreen( this );
		void Model( string id, string model, UiRect rect ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = rect, Anchor = UiAnchor.Center } );
		// [DATA:Mac staff table 0x4a1e0:9343,9345,9348,9349,9358] Resolved ui.wad root model hashes.
		Model( "window", "w_big", Rect( 186, 30, 2018, 1007 ) );
		Model( "staffInfo", "f_staffinfo", Rect( 259, 521, 971, 600 ) );
		Model( "staffPicture", "f_staffpic", Rect( 484, 179, 748, 443 ) );
		Model( "statsFrame", "f_balance", Rect( 259, 702, 971, 930 ) );
		Model( "listFrame", "hirestaff", Rect( 1017, 179, 1813, 930 ) );
		screen.Add( new UiLabel { Id = "title", Text = () => showingEmployedStaff ? strings[UIStrings.AllStaff] : HireTitle, Font = fonts => fonts.Title,
			Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = Rect( 805, 74, 1277, 153 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "staffName", Text = () => SelectedName,
			Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
			Bounds = Rect( 283, 536, 679, 575 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "staffGrade", Text = () => SelectedGrade,
			Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Right, Fit = true,
			Bounds = Rect( 722, 536, 957, 575 ), Anchor = UiAnchor.Center } );
		// [APPROX:UI-082] the selected role and grade are text in f_staffpic; the original draws a 3D candidate preview in control 9348's rectangle (0x49ba00) — evidence needed: that renderer
		screen.Add( new UiLabel { Id = "portraitText", Text = () => SelectedGrade.Length > 0 ? $"{SelectedRole}  {SelectedGrade}" : "",
			Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
			Bounds = Rect( 513, 202, 723, 415 ), Anchor = UiAnchor.Center } );
		for ( var row = 0; row < StaffStatRows.Length; row++ )
		{
			var index = row;
			var entry = StaffStatRows[row];
			screen.Add( new UiLabel { Id = $"statLabel{row}", Text = () => !showingEmployedStaff && index == 3 ? "" : strings[!showingEmployedStaff && index == 0 ? UIStrings.CandidateName : entry.Label], Font = fonts => fonts.Label,
				Color = UiColors.Text, Fit = true, Bounds = entry.LabelRect, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"statValue{row}", Text = () => SelectedStat( index ),
				Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Right, Fit = true,
				Bounds = entry.ValueRect, Anchor = UiAnchor.Center } );
		}
		// [DATA:Mac staff table 0x4a1e0:9360-9364] Five buttons with help 154-158 select applicants by role.
		foreach ( var role in StaffRoles )
		{
			var item = role;
			screen.Add( new UiButton { Id = $"role{item.Type}", Model = item.Model, Help = strings.Help( item.Help ),
				Clicked = () => FilterStaff( item.Type ), Selected = () => staffFilter == item.Type,
				Bounds = item.Rect, Anchor = UiAnchor.Center } );
		}
		// [DATA:Mac staff table 0x4a1e0:16,17] Help 159/160 sorts the name/wage columns.
		screen.Add( new StaffHeader( this, false ) { Id = "headerName", Help = strings.Help( 159 ), Bounds = Rect( 1039, 331, 1460, 436 ), Anchor = UiAnchor.Center } );
		screen.Add( new StaffHeader( this, true ) { Id = "headerWage", Help = strings.Help( 160 ), Bounds = Rect( 1471, 331, 1731, 436 ), Anchor = UiAnchor.Center } );
		screen.Add( new StaffList( this ) { Id = "staffList", Help = strings.Help( 161 ), Bounds = StaffContent, Anchor = UiAnchor.Center } );
		screen.Add( new StaffScrollBar( this ) { Id = "staffScroll", Bounds = StaffScrollTrack, Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "scrollUp", Model = "b_up", Clicked = () => ScrollStaff( -1 ),
			Bounds = Rect( 1742, 331, 1803, 391 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "scrollDown", Model = "b_down", Clicked = () => ScrollStaff( 1 ),
			Bounds = Rect( 1742, 850, 1803, 910 ), Anchor = UiAnchor.Center } );
		// [DATA:Mac staff table 0x4a1e0:9365] b_allthings returns to the buy window.
		screen.Add( new UiButton { Id = "allThings", Model = "b_allthings", Help = strings.Help( 162 ), Clicked = CloseStaffWindow,
			Bounds = Rect( 1866, 583, 1968, 686 ), Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x49b650 control 9366 is a plain cash label: handler 0x49b697 and builder 0x49bf75 format UITEXT 0x1ca "Cash $ N" or 0x1cb "Cash -$ N"]
		screen.Add( new UiLabel { Id = "staffCash", Text = () => StaffCashText, Font = fonts => fonts.Label, Color = UiColors.Value,
			Align = UiAlign.Right, Fit = true, Bounds = Rect( 1297, 74, 1769, 153 ), Anchor = UiAnchor.Center } );
		// [EXT:staff-employee-view] labelled button between the staff info and stat frames toggling the employee view.
		screen.Add( new UiButton { Id = "staffViewExt", Text = () => showingEmployedStaff ? HireTitle : strings[UIStrings.AllStaff],
			Clicked = () => ShowEmployedStaff( !showingEmployedStaff ), Font = fonts => fonts.Label,
			Bounds = Rect( 320, 610, 909, 690 ), Anchor = UiAnchor.Center } );
		// [APPROX:UI-092] an explicit dismiss button with a confirmation below the portrait; the original dismisses with b_fire (1292) on the per-employee panel (0x4b6080, table 0x756f48, jump 0x4b5e5d) and confirms (UITEXT 0x18d) only when [0x78d90f] is set — evidence needed: porting that panel
		dismissStaffButton = new UiButton { Id = "dismissStaff", Text = () => strings[UIStrings.DismissEmployee].Split( '\n' )[0].Trim(),
			Clicked = ConfirmDismissSelectedStaff, Font = fonts => fonts.Label, Enabled = false,
			Bounds = Rect( 320, 449, 909, 518 ), Anchor = UiAnchor.Center };
		screen.Add( dismissStaffButton );
		// [APPROX:UI-098] the pick-up button of the employee's info window (control 0x50e of the window handled at 0x4b5cb0, a window OpenTPW does not port) sits beside the portrait of the employee view with an OpenTPW label; its label and rectangle are untraced — evidence needed: the 0x50e control's text and rectangle
		pickUpStaffButton = new UiButton { Id = "pickUpStaff", Text = () => strings.Extra( OpenTpwText.PickUpStaff ),
			Clicked = () => PickUpSelectedStaff(), Font = fonts => fonts.Label, Enabled = false, Visible = true,
			Bounds = Rect( 770, 300, 990, 400 ), Anchor = UiAnchor.Center };
		screen.Add( pickUpStaffButton );
		// [DATA:Mac staff-button table 0x4e45c:2397-2404] Locate-role and locate-visitor buttons at the authored HUD rectangles.
		Model( "staffLocateFrame", "!f_plain", Rect( 526, 1191, 893, 1439 ) );
		Model( "visitorLocateFrame", "!f_plain", Rect( 12, 1191, 379, 1439 ) );
		// [APPROX:UI-909] the shipped b_svisit button art (the map window's visitors layer uses the same
		// art) opens the All Visitors window from the visitor locate corner; its original handler and
		// rectangle in this window are not decoded.
		screen.Add( new UiButton { Id = "allVisitors", Model = "b_svisit", Clicked = OpenVisitorWindow, Enabled = level.Guests != null,
			Bounds = Rect( 24, 1207, 127, 1310 ), Anchor = UiAnchor.Center } );
		// The original staff window navigates to the visitor and attractions windows (UI-MAP 0x1014b124:
		// messages 0x32f and 0x330 open them with the last used category); this entry keeps the All
		// Attractions window reachable where the buy window is closed (a read-only visit disables buy).
		// [EXT:overview-entry-buttons] a text button: the shipped b_allthinginfo art has no texture in ui.wad.
		screen.Add( new UiButton { Id = "allAttractions", Text = () => strings[UIStrings.AllRides], Font = fonts => fonts.Label,
			Clicked = OpenAttractionsWindow, Bounds = Rect( 24, 1320, 127, 1422 ), Anchor = UiAnchor.Center } );
		// [PROVISIONAL:LOCATE] Locate controls focus live actors; role lookup prefers the selected employee, otherwise cycles IDs.
		var locate = new (string Model, int Help, UiRect Rect)[]
		{
			("b_shandy", 482, Rect( 546, 1207, 649, 1310 )),
			("b_smech", 483, Rect( 659, 1207, 761, 1310 )),
			("b_senter", 484, Rect( 602, 1320, 705, 1422 )),
			("b_sguard", 485, Rect( 771, 1207, 873, 1310 )),
			("b_sresrcher", 486, Rect( 715, 1320, 817, 1422 )),
			("b_skids", 489, Rect( 144, 1263, 247, 1366 )),
		};
		for ( var index = 0; index < locate.Length; index++ )
		{
			var button = locate[index];
			var role = index < 5 ? StaffRoles[index].Type : (StaffType?)null;
			screen.Add( new UiButton { Id = $"locate{index}", Model = button.Model, Help = strings.Help( button.Help ),
				Enabled = role is { } type ? CanLocateStaff( type ) : CanLocateVisitor, Clicked = () => { if ( role is { } type ) LocateStaff( type ); else LocateVisitor(); }, Bounds = button.Rect, Anchor = UiAnchor.Center } );
		}
		screen.Add( new UiButton { Id = "exit", Model = "b_exit", Help = strings.Help( 2 ), Clicked = CloseStaffWindow,
			Bounds = Rect( 1889, 818, 1972, 901 ), Anchor = UiAnchor.Center } );
		screen.Back = CloseStaffWindow;
		return screen;
	}

	private sealed class StaffScreen : UiScreen
	{
		private readonly ParkHud owner;
		public StaffScreen( ParkHud owner ) : base( "staff" ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			owner.EnsureStaffSelection();
			if ( Find( "locate5" ) is UiButton visitor ) visitor.Enabled = owner.CanLocateVisitor;
			for ( var index = 0; index < StaffRoles.Length; index++ )
				if ( Find( $"locate{index}" ) is UiButton staff ) staff.Enabled = owner.CanLocateStaff( StaffRoles[index].Type );
			if ( input.Wheel != 0 )
			{
				owner.ScrollStaff( input.Wheel > 0 ? -1 : 1 );
				input = input with { Wheel = 0 };
			}
			return base.Update( context, input );
		}
	}

	private sealed class StaffList : UiElement, IUiRowList
	{
		private readonly ParkHud owner;
		public StaffList( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		public int Selected => owner.showingEmployedStaff
			? owner.StaffItems.ToList().FindIndex( member => member.Id == owner.selectedStaffId )
			: owner.StaffCandidates.ToList().FindIndex( candidate => candidate.Id == owner.selectedStaffId );
		public bool ArrowsSelect => true;
		public void Select( int index ) => owner.SelectStaff( index );
		public void Scroll( int rows ) => owner.ScrollStaff( rows );
		// [APPROX:UI-083] Enter or a second row click hires the selected applicant; the original's list message 0x400 hires (0x49b988 → 0x507bf0), closes and posts 0x135 — evidence needed: the gesture producing 0x400 (input 0x11006/0x11007)
		public override void Activate() => owner.HireSelectedStaff();
		public override void Adjust( int direction ) => owner.SelectStaff( Math.Max( 0, Selected ) + direction );
		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			var content = canvas.Map( StaffContent, Anchor );
			if ( !content.Contains( point ) )
				return -1;
			var visible = (int)MathF.Floor( (point.Y - content.Y) / (StaffRowHeight * canvas.Scale) );
			var index = owner.staffFirstRow + visible;
			return visible < 0 || visible >= StaffVisibleRows || index >= owner.StaffListCount ? -1 : index;
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var canvas = context.Canvas;
			for ( var visible = 0; visible < StaffVisibleRows && owner.staffFirstRow + visible < owner.StaffListCount; visible++ )
			{
				var index = owner.staffFirstRow + visible;
				var id = owner.showingEmployedStaff ? owner.StaffItems[index].Id : owner.StaffCandidates[index].Id;
				var y = StaffContent.Y + visible * StaffRowHeight;
				var row = canvas.Map( Rect( StaffContent.X, y, StaffContent.Right, y + StaffRowHeight ), Anchor );
				if ( id == owner.selectedStaffId )
					context.Batch.AddRectangle( row, new RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				var color = id == owner.selectedStaffId ? UiColors.Highlight : UiColors.Text;
				UiRect Cell( float left, float right )
				{
					var origin = canvas.Map( new NVector2( left, y ), Anchor );
					return new UiRect( origin.X, origin.Y, (right - left) * canvas.Scale, StaffRowHeight * canvas.Scale );
				}
				context.DrawFittedText( context.Fonts.Label, owner.ListName( index ), Cell( 1043, 1456 ), color, UiAlign.Left );
				context.DrawFittedText( context.Fonts.Label, owner.ListWage( index ), Cell( 1477, 1724 ), color, UiAlign.Right );
			}
		}
	}

	private sealed class StaffHeader : UiElement
	{
		private readonly ParkHud owner;
		private readonly bool byWage;
		public StaffHeader( ParkHud owner, bool byWage ) { this.owner = owner; this.byWage = byWage; }
		public override bool Focusable => Visible && Enabled;
		public override void Activate() => owner.SortStaff( byWage );
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			context.DrawFittedText( context.Fonts.Label, owner.strings[byWage ? UIStrings.MonthlyWageStaff : UIStrings.NameAttr],
				ScreenRect( context.Canvas ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
	}

	private sealed class StaffScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public StaffScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private UiRect KnobRect()
		{
			var travel = StaffScrollTrack.Height - StaffScrollKnob.Height;
			var top = StaffScrollTrack.Y + (owner.StaffMaxFirstRow == 0 ? 0 : travel * owner.staffFirstRow / owner.StaffMaxFirstRow);
			return StaffScrollKnob with { Y = top };
		}
		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			if ( rect.Height <= 0 || owner.StaffMaxFirstRow == 0 )
				return;
			var y = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (y - StaffScrollTrack.Y - StaffScrollKnob.Height / 2) / (StaffScrollTrack.Height - StaffScrollKnob.Height), 0, 1 );
			owner.SetStaffFirstRow( (int)MathF.Round( fraction * owner.StaffMaxFirstRow ) );
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( "!slider", 0, context.Canvas.Map( StaffScrollTrack, Anchor ) );
			context.DrawModel( "b_scroller", owner.StaffMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}
}
