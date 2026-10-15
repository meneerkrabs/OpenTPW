using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// One placed attraction projected for the All Attractions window: the live values of
/// <see cref="OriginalObject"/> the window lists (identity, door and breakdown state, queue,
/// riders, wear, excitement). A plain record so the list logic stays testable without a level.
/// </summary>
public readonly record struct AttractionListRow(
	int AttractionId, string Name, BuildCategory Category, bool IsOpen, bool IsBroken,
	int QueueLength, int Riders, double WearPercent, int? Excitement );

/// <summary>
/// The All Attractions window's list model: filtering, ordering, status text, the detail rows of the
/// original per-category statistic labels (UITEXT 78-97) and selection healing. A null detail value
/// means "not simulated"; the window substitutes its own note for those.
/// </summary>
internal static class ParkAttractionRoster
{
	/// <summary>
	/// The status column of a row: the original status strings name only the abnormal states
	/// (UITEXT 364 'Closed', 365 'Broken down'; 366-388 name upgrade/cleaning/repair/track states that
	/// OpenTPW does not simulate), so an open working attraction shows no status text.
	/// </summary>
	// [DATA:UITEXT.str:364,365] the original status labels; which status the original list showed per state — evidence needed: the original all-attractions list
	public static UIStrings StatusOf( in AttractionListRow row ) =>
		row.IsBroken ? UIStrings.BrokenDown : !row.IsOpen ? UIStrings.Closed : UIStrings.Blank;

	/// <summary>Rows of one category (null: every category) in list order: by name, ties by attraction id.</summary>
	public static IReadOnlyList<AttractionListRow> Rows( IEnumerable<AttractionListRow> attractions, BuildCategory? category, bool descending )
	{
		var listed = attractions.Where( row => category == null || row.Category == category );
		return (descending
				? listed.OrderByDescending( row => row.Name, StringComparer.Ordinal )
				: listed.OrderBy( row => row.Name, StringComparer.Ordinal ))
			.ThenBy( row => row.AttractionId ).ToArray();
	}

	/// <summary>The selected attraction id after the park changed: the current one while it is still listed, else the first row, 0 when empty.</summary>
	public static int HealedSelection( IReadOnlyList<AttractionListRow> rows, int selectedId ) =>
		rows.Any( row => row.AttractionId == selectedId ) ? selectedId : rows.Count > 0 ? rows[0].AttractionId : 0;

	/// <summary>First scrollable row of a list of <paramref name="count"/> rows (never negative).</summary>
	public static int MaxFirstRow( int count, int visibleRows ) => Math.Max( 0, count - visibleRows );

	/// <summary>
	/// Detail rows of <paramref name="row"/> for its category, from the original per-category statistic
	/// labels: rides UITEXT 83/85/86, shops 88-91, sideshows 93-97 (features have none of their own).
	/// Values are live simulation values where OpenTPW simulates them; null means "not simulated".
	/// </summary>
	// [EXT:overview-detail-rows] the original attractions window has no detail rows; ride monthly users, remaining life and customer satisfaction remain unavailable, the HUD supplies existing mapped shop/sideshow customer and profit counters, state of repair is the live wear gauge and excitement the bridge's live level
	public static (UIStrings Label, string? Value)[] StatRows( in AttractionListRow row )
	{
		string? Percent( double value ) => $"{Math.Round( Math.Clamp( value, 0, 100 ) )}%";
		return row.Category switch
		{
			BuildCategory.Rides => new[]
			{
				(UIStrings.UsersLastMonthAttr, (string?)null),
				(UIStrings.StateOfRepairAttr, Percent( row.WearPercent )),
				(UIStrings.RemainingLifeAttr, null),
			},
			BuildCategory.Shops => new[]
			{
				(UIStrings.CustomersLastMonthShop, (string?)null),
				(UIStrings.ProfitLastMonthShop, null),
				(UIStrings.TotalProfitShop, null),
				(UIStrings.CustomerSatisfactionShop, null),
			},
			BuildCategory.Sideshows => new[]
			{
				(UIStrings.CustomersLastMonthGame, (string?)null),
				(UIStrings.ExcitementGame, row.Excitement is { } excitement ? Percent( excitement ) : null),
				(UIStrings.ProfitLastMonthGame, null),
				(UIStrings.TotalProfitGame, null),
				(UIStrings.CustomerSatisfactionGame, null),
			},
			_ => Array.Empty<(UIStrings, string?)>(),
		};
	}
}

/// <summary>The cells of one visitor row, as the row holds them between refreshes (bar values on the original's 0..1024 scale).</summary>
/// <param name="Hours">Whole park hours in the park; null for a visitor whose admission duration is unknown (an old save).</param>
/// <param name="ThoughtFrame">The ti_hungry frame of the visitor's thought (thought − 1); -1 for none (the original's 999).</param>
internal readonly record struct VisitorRowValues( int Number, int Cash, long? Hours, int Rides, int ThoughtFrame, int HappinessGauge )
{
	/// <summary>The value the list sorts a column by (the original's row values; no thought sorts as 999).</summary>
	public long SortKey( int column ) => column switch
	{
		0 => Number,
		1 => Cash,
		2 => Hours ?? -1,
		3 => Rides,
		4 => ThoughtFrame < 0 ? ParkVisitorRoster.NoThought : ThoughtFrame,
		_ => HappinessGauge,
	};
}

/// <summary>The All Visitors window's list model (tp.exe 0x493530): the six columns, the row values, the sort and selection healing.</summary>
internal static class ParkVisitorRoster
{
	/// <summary>The row value of a visitor without a thought.</summary>
	public const int NoThought = 999;

	/// <summary>The six columns: the header's element id, its UITEXT (0x71-0x76), its help (0x81-0x86) and the text cells' alignment.</summary>
	// [BIN:TPW-EXE:0x004939d0 row template] the number and time cells are centred (0x65c428 with 1, 1), the money cell right-aligned (2, 1)
	public static readonly (string Id, UIStrings Header, int Help, UiAlign Align)[] Columns =
	{
		("headerNumber", UIStrings.VisitorNumber, 129, UiAlign.Center),
		("headerCash", UIStrings.CashRemainingVisitor, 130, UiAlign.Right),
		("headerTime", UIStrings.TimeInParkVisitor, 131, UiAlign.Center),
		("headerRides", UIStrings.RidesRiddenVisitor, 132, UiAlign.Center),
		("headerThought", (UIStrings)117, 133, UiAlign.Center),
		("headerHappiness", UIStrings.HappinessVisitor, 134, UiAlign.Center),
	};

	/// <summary>In-park visitors in list order: by the column's row value, ties by visitor number.</summary>
	public static IReadOnlyList<Guest> Rows( IEnumerable<Guest> guests, Func<Guest, bool> isInPark, Func<Guest, VisitorRowValues> values, int column, bool descending )
	{
		column = Math.Clamp( column, 0, Columns.Length - 1 );
		var listed = guests.Where( isInPark );
		return (descending ? listed.OrderByDescending( guest => values( guest ).SortKey( column ) ) : listed.OrderBy( guest => values( guest ).SortKey( column ) ))
			.ThenBy( guest => guest.Id ).ToArray();
	}

	/// <summary>The selected visitor id after the simulation changed: the current one while still in the park, else the first row, 0 when empty.</summary>
	public static int HealedSelection( IReadOnlyList<Guest> rows, int selectedId ) =>
		rows.Any( guest => guest.Id == selectedId ) ? selectedId : rows.Count > 0 ? rows[0].Id : 0;

	/// <summary>
	/// A row's values: the visitor number (+0x1d8), the cash (+0x1a0), the hours in the park, the rides ridden (+0x1c4), the thought frame (+0x30) and
	/// the happiness bar (the low byte of the truncated +0x19c × 1024 / 100). <paramref name="refreshed"/>: the refresh's whole 4-turn steps, else a new
	/// row's quarter of them.
	/// </summary>
	// [BIN:TPW-EXE:0x00493800 row add] and [BIN:TPW-EXE:0x0049326c rows refresh] the hours are (turn − stamp +0x1d4, 0x4fd950) >> 2, shifted back << 2 by the refresh only,
	// times the clock rate 15000 / 4 seconds a turn (0x4f8730, × 10^7) over 36,000,000,000 (one hour, 0x67c320)
	public static VisitorRowValues ValuesOf( Guest guest, bool refreshed ) => new( guest.Id, guest.Money, Hours( guest, refreshed ), guest.RidesTaken,
		guest.Thought == GuestThought.None ? -1 : (int)guest.Thought - 1, (((int)guest.Happiness & 0xff) << 10) / 100 );

	/// <summary>The hours of the time-in-park cell from the park turns since admission.</summary>
	public static long DisplayHours( long turns, bool refreshed )
	{
		var steps = turns >> 2;
		return ParkCalendar.SecondsPerTurn * (refreshed ? steps << 2 : steps) / ParkCalendar.SecondsPerHour;
	}

	// [APPROX:GUEST-920] the turns since admission are the visitor's simulated seconds in the park over the turn's 248 ms, where the original counts world turns since its stamp +0x1d4
	private static long? Hours( Guest guest, bool refreshed ) => guest.TimeInParkSeconds is { } elapsed
		? DisplayHours( (long)Math.Floor( elapsed * 1000 / ParkCalendar.TurnMilliseconds ), refreshed ) : null;

	/// <summary>The UITEXT record of a time-in-park cell (<see cref="UiTextRecord"/>).</summary>
	public static int DurationRecord( long hours ) => hours > 48 ? UiTextRecord.Days : hours > 24 ? UiTextRecord.Day : UiTextRecord.Hours;

	/// <summary>The number the time-in-park cell shows: whole days above 24 hours, else the hours.</summary>
	public static long DurationValue( long hours ) => hours > 24 ? hours / 24 : hours;
}
