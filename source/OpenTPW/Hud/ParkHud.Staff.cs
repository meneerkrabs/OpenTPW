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
	/// <summary>The hire window's money samples, taken when the window opens (null without an economy).</summary>
	private (long MoneyIn, long StaffCosts, long MoneyOut)? hireBalanceSample;
	private StaffType? staffFilter = StaffType.Handyman;
	private bool showingEmployedStaff;
	// [BIN:TPW-EXE:0x49b85e hire list sort word 0x7523e4] the hire window writes its sort word back on a sort change, so the order outlives the window for the session (UI-084)
	private bool staffSortByWage { get => Sorts.StaffByWage; set => Sorts.StaffByWage = value; }
	private bool staffSortDescending { get => Sorts.StaffDescending; set => Sorts.StaffDescending = value; }
	private int selectedStaffId;
	private int staffFirstRow;
	private Dictionary<StaffType, string[]>? staffNames;
	private string[]? staffTypes;
	private string[]? staffStates;
	// [BIN:TPW-EXE:0x49bbb0 hire list row] the hire window's list (control 0x248e) makes each row (0x49c870 → 0x49bbb0, 0x65bcad) as high as font slot 7's line height ([0x7523dc] holds 7) × 1536 / screen height + 6, computed once ([0x7ca314]), the formula and slot of the buy list's rows (0x4ad240), so the staff list takes the buy list's pitch (BuyRowHeight, `EXT:list-row-pitch` for its value) and shows (bottom - top) / pitch whole rows (0x663786): 470 / 41.4 = 11
	// [BIN:TPW-EXE:0x665c35 list wheel] the list forwards a wheel message (0x1000d, one notch = WM_MOUSEWHEEL delta / 120 at 0x46b600) to its scroll bar child as position - notches, so the wheel moves one row per notch
	public const float StaffRowHeight = BuyRowHeight;
	public const int StaffVisibleRows = (int)(470f / StaffRowHeight);

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
		hireBalanceSample = FinanceEconomy is { } economy ? HireBalanceSample( economy.Ledger.History ) : null;
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
	// reached by the labelled "staffViewExt" button, is an OpenTPW extension; like the All Staff list (0x400 → 0x4b6080 at 0x496088) a click
	// on an employee opens the per-employee window (ParkHud.EmployeeWindow.cs), which holds the dismiss, pick-up and patrol buttons.
	public void ShowEmployedStaff( bool employed )
	{
		showingEmployedStaff = employed;
		staffFirstRow = 0;
		selectedStaffId = 0;
		EnsureStaffSelection();
	}

	// [BIN:TPW-EXE:0x665a44 generic list sort, list attribute 0x10] the same column toggles its direction, a new column starts ascending; the initial sort column is 0 (ascending), so the first press on column 0 descends.
	// [BIN:TPW-EXE:0x7523e4 hire list sort word] the word starts at 1 (column 0, ascending) and is written back on a change (0x49b85e); the session keeps it (ListSortMemory)
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
	/// exists only after a click on a valid cell (docs/STAFF-PLACEMENT.md). A level without the Place tool (no guest world) hires nobody.
	/// </summary>
	// [BIN:TPW-EXE:0x507bf0 hire] the hire creates no object, it builds the place tool; [BIN:TPW-EXE:0x49b9dd hire help] the handler closes the window (0x658d9f, message 4) and then posts advisor message 0x135
	// [BIN:TPW-EXE:0x46c890 place tool end] the advisor help 0x135 (309) is posted after the window closes and withdrawn when the Place tool ends (ParkHud.StaffPlacementHelp); no message-area line
	public bool HireSelectedStaff()
	{
		if ( showingEmployedStaff || RejectReadOnlyAction() || Status.EconomySource is not ParkEconomy economy || economy.IsBankrupt )
			return false;
		var candidate = SelectedCandidate;
		if ( candidate == null || !economy.Staff.CanHire( candidate.Type ) || economy.Staff.Candidates.All( item => item.Id != candidate.Id ) )
			return false;
		// [EXT:online-coop-commands] the hire goes through the Place tool's commands (the tool exists wherever a park view does)
		if ( level.StaffPlacement is not { } placement || !ReferenceEquals( level.Park?.Economy, economy ) || !placement.BeginHire( candidate.Id ) )
			return false;
		level.EndBuildTools();
		CloseStaffWindow();
		selectedStaffId = 0;
		EnsureStaffSelection();
		return true;
	}

	/// <summary>Feedback for a click that cannot hire (bankrupt, role full, read-only, no applicant): a message instead of a silent select.</summary>
	// [BIN:TPW-EXE:0x49b988 hire row] the hire window's row activation (0x400) calls the hire 0x507bf0 (which returns 0 for an empty slot and builds the staff tool otherwise), closes the window and posts advisor help 0x135; no direct call chain from the window procedure 0x49b650 reaches a cat_ui sound, so the refusal plays no error sound (the message is OpenTPW's)
	internal void RefuseHire()
	{
		if ( showingEmployedStaff )
			return;
		PostMessage( strings.Extra( OpenTpwText.NotAvailable ) );
	}

	/// <summary>Opens the per-employee window on the employee view's selected employee (a click on the row, as the All Staff list does).</summary>
	public bool OpenSelectedEmployee() => showingEmployedStaff && SelectedStaff is { } member && OpenEmployeeWindow( member );

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
	}

	// [BIN:TPW-EXE:0x00504DE0 staff serializer] a member's name is the inline mName[i] (+0x1A0) of the staff thing; a member from an original save keeps the saved one
	private string StaffName( StaffMember member ) => member.Name ?? StaffName( member.Type, member.NameIndex, member.Id );
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
	private string CandidateStat( StaffCandidate candidate, int row ) => row switch
	{
		0 => CandidateName( candidate ),
		1 => CandidateWage( candidate ),
		2 => $"{20 * candidate.Grade}%",
		_ => ""
	};

	/// <summary>
	/// The hire window's mini balance (control 9349) for <paramref name="candidate"/>: the last closed month's money in, its staff costs with the candidate's monthly wage
	/// added, its other costs (money out minus staff costs) and what is left once the wage is paid. The month is the one the window found when it opened.
	/// </summary>
	// [BIN:TPW-EXE:0x49b927 hire window mini balance] on a selection the staff-costs row (0x248c) shows [0x7ca2fc] + the candidate's wage (0x5077f0) and the balance row (0x2487) [0x7ca2f0] - wage - [0x7ca2fc] - [0x7ca300]; other costs [0x7ca300] are money out minus staff costs (0x49c705)
	// [BIN:TPW-EXE:0x49c53d hire window samples] the builder 0x49bdd0 copies the newest entry of the money-in (+0x1fc94, index +0x1fed4), staff-costs (+0x1f7f4, index +0x1fa34) and money-out (+0x1f5a4, index +0x1f7e4) sample rings into those globals, 0 while a ring is still empty (index -1, not wrapped); a ring entry is written only by the month close 0x4c7720, which pushes each month's accumulator (+0x1fc90, +0x1f7f0, +0x1f5a0) through 0x4ce290 and zeroes it, so the window reads the last closed month as it was when the window opened
	// [DATA:video 12:00 hire window] Inkomsten, - Werknemerskosten, - Overige kosten and Saldo; Saldo follows the selected candidate and the two cost rows are red
	public (long Income, long StaffCosts, long OtherCosts, long Balance)? CandidateBalance( StaffCandidate? candidate )
	{
		if ( candidate == null || hireBalanceSample is not { } sample )
			return null;
		return HireBalance( sample, CandidateWageAmount( candidate ) );
	}

	/// <summary>The hire window's three samples (money in, staff costs, money out) of the last closed month in <paramref name="closedMonths"/>; zeros before the first month closes.</summary>
	public static (long MoneyIn, long StaffCosts, long MoneyOut) HireBalanceSample( IReadOnlyList<LedgerMonth> closedMonths )
	{
		if ( closedMonths.Count == 0 )
			return (0, 0, 0);
		var month = closedMonths[^1];
		return (month.MoneyIn, month[LedgerCategory.StaffCosts], month.MoneyOut);
	}

	/// <summary>The four mini-balance rows from the window's samples and a candidate's monthly wage.</summary>
	public static (long Income, long StaffCosts, long OtherCosts, long Balance) HireBalance( (long MoneyIn, long StaffCosts, long MoneyOut) sample, long wage ) =>
		(sample.MoneyIn, sample.StaffCosts + wage, sample.MoneyOut - sample.StaffCosts, sample.MoneyIn - wage - sample.MoneyOut);

	private string BalanceRowText( int row )
	{
		if ( CandidateBalance( SelectedCandidate ) is not { } balance )
			return "";
		return FormatMoney( row switch { 0 => balance.Income, 1 => balance.StaffCosts, 2 => balance.OtherCosts, _ => balance.Balance } );
	}

	/// <summary>The hire window's skill gauge (control 9346) as a share of the full bar: the candidate's skill byte (its grade) × 1024 / 5 of the gauge's 0..1024 range.</summary>
	// [BIN:TPW-EXE:0x49b8c3 skill gauge] the gauge is set to the skill byte (candidate +8, the grade 0..4 that 0x507600 writes as AvgGradeOf* + R % 3 - 1, a wrapped 0xff stored as 4) × 1024 / 5 (0x49b8e2..0x49b8f5), so grade 4 fills four fifths of the bar and grade 0 none; the video's guard candidates (12:00, wages $60 $75 $105 = Easy_Standard grades 1 2 3 at multiplier 15) show a fifth for the cheapest, as grade 1 does
	public static float? SkillGauge( StaffCandidate? candidate ) => candidate == null ? null : Math.Clamp( (candidate.Grade << 10) / 5, 0, UiGauge.FullValue ) / (float)UiGauge.FullValue;

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
	/// <summary>The buy window's cash label (control 512): the same UITEXT 0x1ca/0x1cb text as the hire window's.</summary>
	public string BuyCashText => StaffCashText;
	private string CashLabel( long balance )
	{
		var template = strings[balance < 0 ? UIStrings.CashNegativeDollar : UIStrings.CashDollar];
		var number = (balance < 0 ? -(decimal)balance : balance).ToString( CultureInfo.InvariantCulture );
		return template.Contains( "%d" ) ? template.Replace( "%d", number ) : template + number;
	}
	private (StaffType Type, int Variant)? portraitKey;
	private float portraitStart;

	/// <summary>
	/// The figure in the hire window's portrait box: the selected candidate's (or, in the employee view, employee's) role and variant walking on
	/// the spot, restarting whenever another role or variant is selected.
	/// </summary>
	private UiSpriteFrame? PortraitFrame()
	{
		(StaffType Type, int Variant)? key = showingEmployedStaff
			? SelectedStaff is { } member ? (member.Type, StaffSpriteRules.KeyOf( member )) : null
			: SelectedCandidate is { } candidate ? (candidate.Type, StaffSpriteRules.KeyOf( candidate )) : null;
		if ( key != portraitKey )
		{
			portraitKey = key;
			portraitStart = Time.Now;
		}
		return key is { } chosen ? level.StaffRenderer?.PortraitFrame( chosen.Type, chosen.Variant, Time.Now - portraitStart ) : null;
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
		// [DATA:video 12:00 hire window] the applicant view shows no name or grade: the strip under the portrait is the Vaardigheid (skill) gauge (control 9346); the employee view (EXT) keeps name and grade there
		screen.Add( new UiLabel { Id = "staffName", Text = () => showingEmployedStaff ? SelectedName : strings[UIStrings.SkillStaff],
			Font = fonts => fonts.Label, Color = UiColors.Text, Align = showingEmployedStaff ? UiAlign.Center : UiAlign.Left, Fit = true,
			Bounds = Rect( 283, 536, 679, 575 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "staffGrade", Text = () => showingEmployedStaff ? SelectedGrade : "",
			Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Right, Fit = true,
			Bounds = Rect( 722, 536, 957, 575 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiGaugeBar { Id = "skillGauge", Value = () => showingEmployedStaff ? null : SkillGauge( SelectedCandidate ),
			Bounds = Rect( 722, 538, 957, 573 ), Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x0049BA00 hire figure] when the selected candidate's role (+0) or variant byte (+9) changes, the old figure goes (0x00475550) and a new one of thing type 5, 6, 4, 7 or 8 for the roles 0..4 (handyman, entertainer, mechanic, guard, researcher) with that variant is created in control 9348's rectangle (0x00475A10 with the script 0x0074F758), and its register 8 is set to 8.0 (0x004755D0)
		// [APPROX:UI-082] nominal inner-rectangle geometry and 2.4 scale are traced; native delayed FRAME/start/close/invalid-selection phase,
		// global rotation/depth and viewport/raster parity remain qualified (docs/proposals/ui-082-verify-staff-portrait-scaling.md).
		// [EXT:staff-employee-view] the employed tab deliberately shares this hire-box layout and the existing role/variant walking loop.
		screen.Add( new UiSpriteFigure { Id = "portraitFigure", Frame = PortraitFrame, Bounds = HirePortraitGeometry.Inner,
			FeetInCanvas = HirePortraitGeometry.FeetInCanvas, CanvasUnitsPerPixel = HirePortraitGeometry.CanvasUnitsPerPixel, Anchor = UiAnchor.Center } );
		for ( var row = 0; row < StaffStatRows.Length; row++ )
		{
			var index = row;
			var entry = StaffStatRows[row];
			// the applicant view's four rows are the mini balance (control 9349): UITEXT 356 money in, 357 staff costs, 358 other costs, 360 balance; the two cost rows are red
			var balanceLabels = new[] { UIStrings.CashIn, UIStrings.StaffCosts, UIStrings.OtherCosts, UIStrings.Balance };
			screen.Add( new UiLabel { Id = $"statLabel{row}", Text = () => strings[showingEmployedStaff ? entry.Label : balanceLabels[index]], Font = fonts => fonts.Label,
				ColorSource = () => !showingEmployedStaff && index is 1 or 2 ? UiColors.Negative : UiColors.Text, Fit = true, Bounds = entry.LabelRect, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"statValue{row}", Text = () => showingEmployedStaff ? SelectedStat( index ) : BalanceRowText( index ),
				Font = fonts => fonts.Label, ColorSource = () => showingEmployedStaff ? UiColors.Value : index is 1 or 2 ? UiColors.Negative : UiColors.Text, Align = UiAlign.Right, Fit = true,
				Bounds = entry.ValueRect, Anchor = UiAnchor.Center } );
		}
		// [DATA:Mac staff table 0x4a1e0:9360-9364] Five buttons with help 154-158 select applicants by role.
		// The hire table 0x751fa8 and the All Staff table 0x750e10 are not generated. These role buttons are the radio group (type 8, style 0x11), and the employed view reuses them, so they play cat_ui 0xBD (hook 0x485780, choice 0x4857cb).
		foreach ( var role in StaffRoles )
		{
			var item = role;
			screen.Add( new UiButton { Id = $"role{item.Type}", Model = item.Model, Help = strings.Help( item.Help ), ClickSound = GameAudio.SelectClickEvent,
				Clicked = () => FilterStaff( item.Type ), Selected = () => staffFilter == item.Type,
				Bounds = item.Rect, Anchor = UiAnchor.Center } );
		}
		// [DATA:Mac staff table 0x4a1e0:16,17] Help 159/160 sorts the name/wage columns.
		screen.Add( new StaffHeader( this, false ) { Id = "headerName", Help = strings.Help( 159 ), Bounds = Rect( 1039, 331, 1460, 436 ), Anchor = UiAnchor.Center } );
		screen.Add( new StaffHeader( this, true ) { Id = "headerWage", Help = strings.Help( 160 ), Bounds = Rect( 1471, 331, 1731, 436 ), Anchor = UiAnchor.Center } );
		screen.Add( new StaffList( this ) { Id = "staffList", Help = strings.Help( 161 ), Bounds = StaffContent, Anchor = UiAnchor.Center } );
		screen.Add( new StaffScrollBar( this ) { Id = "staffScroll", Bounds = StaffScrollTrack, Anchor = UiAnchor.Center } );
		// The list slider is type 3 style 0x10, so its arrows play 0xBD. A held arrow repeats (500 ms, then 125 ms, ctor 0x6688b3) and a release after that plays nothing (0x668eff).
		void ScrollArrow( string id, UiRect bounds, int step )
		{
			void Scroll() => ScrollStaff( step );
			screen.Add( new UiButton { Id = id, Model = step < 0 ? "b_up" : "b_down", ClickSound = GameAudio.SelectClickEvent, Clicked = Scroll,
				Repeat = UiButton.ScrollArrowRepeat( Scroll ), Bounds = bounds, Anchor = UiAnchor.Center } );
		}
		ScrollArrow( "scrollUp", Rect( 1742, 331, 1803, 391 ), -1 );
		ScrollArrow( "scrollDown", Rect( 1742, 850, 1803, 910 ), 1 );
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
		// [DATA:Mac staff-button table 0x4e45c:2397-2404] Locate-role and locate-visitor buttons at the authored HUD rectangles.
		Model( "staffLocateFrame", "!f_plain", Rect( 526, 1191, 893, 1439 ) );
		Model( "visitorLocateFrame", "!f_plain", Rect( 12, 1191, 379, 1439 ) );
		// [EXT:overview-entry-buttons] the shipped b_svisit button art (the map window's visitors layer uses the same
		// art) opens the All Visitors window from the visitor locate corner; the original opens the overview windows
		// with the A/S/V keys and cross-navigation buttons (UI-909), whose rectangles in this window are not decoded.
		screen.Add( new UiButton { Id = "allVisitors", Model = "b_svisit", Clicked = OpenVisitorWindow, Enabled = level.Guests != null,
			Bounds = Rect( 24, 1207, 127, 1310 ), Anchor = UiAnchor.Center } );
		// The original staff window navigates to the visitor and attractions windows (UI-MAP 0x1014b124:
		// messages 0x32f and 0x330 open them with the last used category); this entry keeps the All
		// Attractions window reachable where the buy window is closed (a read-only visit disables buy).
		// [EXT:overview-entry-buttons] the rides tab icon b_srides with 'All Rides' as popup help: the shipped b_allthinginfo art has no texture in ui.wad.
		screen.Add( new UiButton { Id = "allAttractions", Model = AllAttractionsModel, Help = strings[UIStrings.AllRides],
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
		public bool HoverSelects => true;
		public int HoveredRow { get; private set; } = -1;
		// [BIN:TPW-EXE:0x6656a0 list hover] the applicant list selects the row under the pointer (the details, the skill gauge and the mini balance follow it, video 12:00); the employee view (EXT) only highlights, as a click opens the employee under the pointer
		public void Hover( int index )
		{
			// only a pointer that moved to another row selects it, so the arrow keys keep the selection while the pointer rests
			var moved = index != HoveredRow;
			HoveredRow = index;
			if ( moved && index >= 0 && !owner.showingEmployedStaff && index < owner.StaffCandidates.Count )
				owner.selectedStaffId = owner.StaffCandidates[index].Id;
		}
		// [BIN:TPW-EXE:0x6655a2 list click, 0x49b843 hire window] the applicant list (9358, attribute 0x291) posts 0x400 on a click of button 0 (0x6655a2 on a list with attribute 0x80, and 0x6656fa on the release); the window routes it to 0x49b988: the row's item, hire 0x507bf0, advisor message 0x135, close. There is no hire button in the window, and the original's advisor says so when it opens ("click on the name of the person you want to hire", video 11:54): a left click on a name hires and the click starts the Place tool here
		// [BIN:TPW-EXE:0x66563d list click] the other button posts 0x402, which the hire window does not handle: a right click does nothing and backs out as everywhere
		public bool RowClicked( int index, bool left )
		{
			if ( !left )
				return false;
			Select( index );
			// [BIN:TPW-EXE:0x496056 all staff row] the All Staff list routes its 0x400 to the per-employee window (0x4b6080), the employee view's stand-in does the same
			if ( owner.showingEmployedStaff )
				return owner.OpenSelectedEmployee();
			if ( !owner.HireSelectedStaff() )
				owner.RefuseHire();
			return true;
		}
		// [EXT:staff-enter-hire] Enter hires the selected applicant too (no original key emits 0x400); it is the keyboard analogue of the click, and in the employee view it opens the employee's window.
		public override void Activate()
		{
			if ( owner.showingEmployedStaff )
				owner.OpenSelectedEmployee();
			else
				owner.HireSelectedStaff();
		}
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
				if ( index == HoveredRow && id != owner.selectedStaffId )
					context.Batch.AddRectangle( row, new RgbaByte( 60, 90, 200, 60 ) );
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
		// The header is a child of the sortable list (type 7, style 0x91 / 0x291). That parent receives 0x100, so the hook plays 0xBD.
		public override void Activate()
		{
			GameAudio.PlayUi( GameAudio.SelectClickEvent );
			owner.SortStaff( byWage );
		}
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
