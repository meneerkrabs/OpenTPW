using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// One row of the All Staff list as the row holds it between refreshes: the cells of tp.exe's row template 0x496d20 (name, current status, monthly wage, skill bar,
/// happiness bar), the bars on the original's 0..1024 scale.
/// </summary>
/// <param name="Name">The employee's name (cell 0, a text cell).</param>
/// <param name="Status">The status text (cell 1, a text cell): the localized STAFFSTATES string of the displayed status.</param>
/// <param name="Wage">The monthly wage (cell 2, a money cell).</param>
/// <param name="SkillGauge">The skill bar (cell 3, ridestatbar.wct): the skill × 1024 / 100.</param>
/// <param name="HappinessGauge">The happiness bar (cell 4, happygrad.wct): the low byte of the truncated happiness × 1024 / 100.</param>
internal readonly record struct AllStaffRowValues( string Name, string Status, long Wage, int SkillGauge, int HappinessGauge )
{
	/// <summary>The text a text column (0 or 1) shows.</summary>
	public string Text( int column ) => column switch { 0 => Name, 1 => Status, _ => "" };

	/// <summary>
	/// The values a header press or a new row compares: the text of a text column, the number of a number column
	/// (the columns' types are set by 0x6636b2 in the builder: columns 0 and 1 type 0, columns 2 to 4 type 1).
	/// </summary>
	public int CompareTo( in AllStaffRowValues other, int column ) => column switch
	{
		// [BIN:TPW-EXE:0x6628f4 list sort] a column of type 0 compares the cells' text (the list's string compare, wcscmp: UTF-16 code units, no culture), a column of type 1 their signed numbers
		0 or 1 => Math.Sign( string.CompareOrdinal( Text( column ), other.Text( column ) ) ),
		2 => Wage.CompareTo( other.Wage ),
		3 => SkillGauge.CompareTo( other.SkillGauge ),
		_ => HappinessGauge.CompareTo( other.HappinessGauge ),
	};
}

/// <summary>The All Staff window's list model (tp.exe 0x496620): the five role tabs, the five columns, the row order and the two average-happiness bars.</summary>
internal static class AllStaffRoster
{
	/// <summary>The number of role tabs and of columns.</summary>
	public const int TabCount = 5, ColumnCount = 5;

	/// <summary>
	/// The five role tabs in the original's tab order (the tab number the window keeps in 0x7ca16c): the round button's control id and model, its help (UIHELPTEXT 114-118), the
	/// label over the tab's happiness bar (UITEXT 107, 108, 110, 109, 111) and the staff thing type the tab lists (the average's type argument 5, 4, 6, 7, 8).
	/// </summary>
	// [BIN:TPW-EXE:0x4968be tab buttons] the builder checks the tab's button in this order: tab 0 control 0x323 (b_shandy), 1 0x326 (b_smech), 2 0x325 (b_senter), 3 0x327 (b_sguard), 4 0x324 (b_sresrcher); the click handler 0x4960a0 maps the controls back through the table 0x496300
	// [BIN:TPW-EXE:0x496ac2 tab labels] the label 0x32a takes UITEXT 0x6b, 0x6c, 0x6e, 0x6d, 0x6f for the tabs 0 to 4 (jump table 0x496be8)
	// [BIN:TPW-EXE:0x496b1c tab thing types] the happiness bar 0x32d reads the average of the thing type 5, 4, 6, 7, 8 for the tabs 0 to 4 (jump table 0x496bfc; 0x506490 gives the same tab for a staff thing: type 4 tab 1, 5 tab 0, 6 tab 2, 7 tab 3, 8 tab 4)
	public static readonly (StaffType Type, uint Control, string Model, int Help, UIStrings Label, int ThingType)[] Tabs =
	{
		(StaffType.Handyman, 0x323, "b_shandy", 114, UIStrings.CleanersHappiness, 5),
		(StaffType.Mechanic, 0x326, "b_smech", 115, UIStrings.MechanicsHappiness, 4),
		(StaffType.Entertainer, 0x325, "b_senter", 116, UIStrings.EntertainersHappiness, 6),
		(StaffType.Guard, 0x327, "b_sguard", 117, UIStrings.GuardsHappiness, 7),
		(StaffType.Researcher, 0x324, "b_sresrcher", 118, UIStrings.ScientistsHappiness, 8),
	};

	/// <summary>The five columns: the header's UITEXT (0x65-0x69), its help (0x77-0x7b), the cell's alignment and whether the column sorts by number.</summary>
	// [BIN:TPW-EXE:0x496620 headers] the builder titles the headers 0x10-0x14 with UITEXT 0x65-0x69 (Name, Current Status, Monthly Wage, Skill, Happiness) and the help ids 0x77-0x7b (0x486660 through 0x485b00)
	// [BIN:TPW-EXE:0x496d20 row template] the cells are two text cells (0x48f6a0, left), a money cell (0x490250) right-aligned by 0x65c428 (2, 1), and two bar cells over ridestatbar.wct (0x750e00) and happygrad.wct (0x7508c0)
	public static readonly (UIStrings Header, int Help, UiAlign Align, bool Numeric)[] Columns =
	{
		(UIStrings.NameStaff, 119, UiAlign.Left, false),
		(UIStrings.CurrentStatusStaff, 120, UiAlign.Left, false),
		(UIStrings.MonthlyWageStaff, 121, UiAlign.Right, true),
		(UIStrings.SkillStaff, 122, UiAlign.Center, true),
		(UIStrings.HappinessStaff, 123, UiAlign.Center, true),
	};

	/// <summary>The tab a staff thing of this role lists under (0x506490).</summary>
	public static int TabOf( StaffType type ) => Array.FindIndex( Tabs, tab => tab.Type == type );

	/// <summary>The tab a window opened with <paramref name="tab"/> shows: -1 is the tab the window showed last, any other number the tab itself.</summary>
	// [BIN:TPW-EXE:0x49680a tab argument] the builder's first argument is the tab; -1 keeps the remembered tab [0x7ca16c], which is stored back (0x496826); the park information, visitors, attractions and rest windows and the info button pass -1
	public static int ResolveTab( int tab, int last ) => Math.Clamp( tab == -1 ? last : tab, 0, TabCount - 1 );

	/// <summary>The sort word of a column and direction (the original's: |word| - 1 is the column, a negative word is descending).</summary>
	public static int SortWord( int column, bool descending ) => descending ? -(column + 1) : column + 1;

	/// <summary>
	/// The members a tab lists in the order the fill visits them: the tab's role in the thing chain's walk order, newest first.
	/// </summary>
	// [BIN:TPW-EXE:0x481bc0 thing fill] the fill takes the per-role chain for the tab's mask (2, 4, 0x10, 8, 0x20; heads at game +0x1da73c..+0x1da744) and calls the row adder 0x496320 for each thing, following the links at +0x210;
	// [BIN:TPW-EXE:0x519aa0 chain head insert] a new thing becomes the chain's head (0x519aa0 for the guards, 0x51965e, 0x5197ce and 0x51993e likewise), so the chain runs newest first, as the global chain does for the members of one role
	public static IReadOnlyList<StaffMember> Visit( IReadOnlyList<StaffMember> members, StaffType type, ThingPool? things ) =>
		EmployeeWindowRules.ChainOrder( members, things ).Where( member => member.Type == type ).ToArray();

	/// <summary>
	/// Adds <paramref name="item"/> to <paramref name="rows"/> as the list's sorted insert does: before the first row it sorts strictly before, at the end when none follows, so equal rows keep the order they were added in.
	/// </summary>
	// [BIN:TPW-EXE:0x66403b sorted row insert, 0x663edc compare] a list with the sort attribute (0x10) puts a new row before the first row for which "new < row" holds (ascending) or "row < new" (descending) on the sort column's cell
	public static void SortedInsert( List<int> rows, int item, Func<int, AllStaffRowValues> values, int column, bool descending )
	{
		var added = values( item );
		var at = rows.FindIndex( other =>
		{
			var order = added.CompareTo( values( other ), column );
			return descending ? order > 0 : order < 0;
		} );
		rows.Insert( at < 0 ? rows.Count : at, item );
	}

	/// <summary>
	/// A header press: the rows ordered again by the column and direction, equal rows keeping the order they have now.
	/// </summary>
	// [BIN:TPW-EXE:0x6628f4 list sort] a selection sort over the rows in their current order: each pass takes the first row that is smallest (ascending) or largest (descending) by a strict compare and appends it to the new order, so rows that compare equal stay in the order they had before the press
	public static List<int> Resort( IReadOnlyList<int> rows, Func<int, AllStaffRowValues> values, int column, bool descending )
	{
		if ( rows.Count < 2 )
			return rows.ToList();
		var ordered = descending
			? rows.OrderByDescending( row => row, new RowComparer( values, column ) )
			: rows.OrderBy( row => row, new RowComparer( values, column ) );
		return ordered.ToList();
	}

	private sealed class RowComparer( Func<int, AllStaffRowValues> values, int column ) : IComparer<int>
	{
		public int Compare( int left, int right ) => values( left ).CompareTo( values( right ), column );
	}

	/// <summary>The row values of a member, the cells of 0x496320: the status text, wage, skill and happiness bars (values from <see cref="EmployeeWindowRules.Stats"/>).</summary>
	public static AllStaffRowValues ValuesOf( string name, string status, EmployeeStats stats ) => new( name, status, stats.MonthlyWage, stats.SkillGauge, stats.HappinessGauge );

	/// <summary>
	/// The refresh of a row (the 2000 ms timer): the status, skill and happiness cells (mask 0x1a) take new values; the name and the wage stay as the row was added.
	/// </summary>
	// [BIN:TPW-EXE:0x495de0 rows refresh] the timer message rewrites the cells of every row with the mask 0x1a (columns 1, 3 and 4) through 0x6644ea; the wage cell (column 2, mask 0x4) is only written when the row is added
	public static AllStaffRowValues Refreshed( in AllStaffRowValues row, in AllStaffRowValues fresh ) => row with { Status = fresh.Status, SkillGauge = fresh.SkillGauge, HappinessGauge = fresh.HappinessGauge };

	/// <summary>
	/// The happiness bar of one role (control 0x32d): the average of the members' truncated happiness bytes, taken as whole numbers (0 without members), on the bar's scale.
	/// </summary>
	// [BIN:TPW-EXE:0x4c9cc0 role average] the sum of the low bytes of the truncated happiness (+0x1f8) of every staff thing of the type, divided by their count with an integer division (0 for none); the window scales it × 1024 / 100 (0x496b5a)
	public static int RoleHappinessGauge( IEnumerable<StaffMember> members, StaffType type )
	{
		long sum = 0;
		var count = 0;
		foreach ( var member in members )
		{
			if ( member.Type != type )
				continue;
			sum += (int)member.Happiness & 0xff;
			count++;
		}
		return count == 0 ? 0 : (int)((sum / count) << 10) / 100;
	}

	/// <summary>
	/// The happiness bar of all staff (control 0x32c): the average of every staff thing's truncated happiness byte as a fraction (0 without staff), times 1024 times the single 0.01, truncated.
	/// </summary>
	// [BIN:TPW-EXE:0x4c7cc0 all-staff average] the sum of the low bytes over the types 4 to 8, divided as a float by their count (the constant 0x70031c, 0.0, for none); 0x496a7b multiplies it by 1024.0 (0x6ff6f4) and 0.01f (0x6ff6f8) and truncates (0x67a830)
	public static int AllHappinessGauge( IEnumerable<StaffMember> members )
	{
		long sum = 0;
		var count = 0;
		foreach ( var member in members )
		{
			sum += (int)member.Happiness & 0xff;
			count++;
		}
		return count == 0 ? 0 : (int)(sum / (double)count * 1024.0 * 0.009999999776482582);
	}
}
