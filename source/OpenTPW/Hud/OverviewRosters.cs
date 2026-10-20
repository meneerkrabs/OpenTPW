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
/// The All Attractions window's list model: filtering, ordering, status text, the per-category columns of
/// the original lists (UITEXT 82-99) and selection healing.
/// </summary>
internal static class ParkAttractionRoster
{
	/// <summary>
	/// The status of a row (the original list has no status column; the smoke tests read it): the original status strings name only the abnormal states
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

	/// <summary>The rows of an open category list in the order it already holds: new rows go in by the list's sorted insert, nobody is re-sorted (<see cref="ListRowOrder"/>).</summary>
	public static IReadOnlyList<AttractionListRow> Keep( List<int> order, IEnumerable<AttractionListRow> attractions, BuildCategory category, int column, bool descending,
		Func<AttractionListRow, IReadOnlyList<AttractionCell>> cells )
	{
		double Key( AttractionListRow row ) => cells( row ) is { } values && column < values.Count && values[column].Key is { } key ? key : double.NegativeInfinity;
		return ListRowOrder.Keep( order, attractions.Where( row => row.Category == category ), row => row.AttractionId, ( added, row ) =>
		{
			if ( column <= 0 )
			{
				var compare = string.CompareOrdinal( added.Name, row.Name );
				return descending ? compare > 0 : compare < 0;
			}
			return descending ? Key( row ) < Key( added ) : Key( added ) < Key( row );
		} );
	}

	/// <summary>The selected attraction id after the park changed: the current one while it is still listed, else the first row, 0 when empty.</summary>
	public static int HealedSelection( IReadOnlyList<AttractionListRow> rows, int selectedId ) =>
		rows.Any( row => row.AttractionId == selectedId ) ? selectedId : rows.Count > 0 ? rows[0].AttractionId : 0;

	/// <summary>First scrollable row of a list of <paramref name="count"/> rows (never negative).</summary>
	public static int MaxFirstRow( int count, int visibleRows ) => Math.Max( 0, count - visibleRows );

	/// <summary>The columns of a category's list, left to right: what each shows, its header (UITEXT 82-99) and the header's help id.</summary>
	// [BIN:TPW-EXE:0x00494ec0 rides list] the rides list is table 0x750ab8 with headers 82-86 (help 105-109); shops (0x494c70) use the same table with headers
	// 87-91 (help 105, 584-587), sideshows (0x494250) table 0x750ba0 with headers 92-97 (help 105, 584, 107, 585-587) and features (0x495110) table 0x750ca0 with
	// headers 98-99 (help 105, 588); the row adders give rides users, excitement, repair and life (0x493cd0), shops customers, profit last month, total profit and
	// satisfaction (0x493f30), sideshows customers, excitement, profit last month, total profit and satisfaction (0x494070), features the number owned of their type (0x4941e0)
	public static (AttractionColumnKind Kind, UIStrings Header, int Help)[] Columns( BuildCategory category ) => category switch
	{
		BuildCategory.Rides => new[]
		{
			(AttractionColumnKind.Name, UIStrings.NameRideAttr, 105), (AttractionColumnKind.Users, UIStrings.UsersLastMonthAttr, 106),
			(AttractionColumnKind.Excitement, UIStrings.ExcitementRideAttr, 107), (AttractionColumnKind.Repair, UIStrings.StateOfRepairAttr, 108),
			(AttractionColumnKind.Life, UIStrings.RemainingLifeAttr, 109),
		},
		BuildCategory.Shops => new[]
		{
			(AttractionColumnKind.Name, UIStrings.NameShopAttr, 105), (AttractionColumnKind.Customers, UIStrings.CustomersLastMonthShop, 584),
			(AttractionColumnKind.ProfitLastMonth, UIStrings.ProfitLastMonthShop, 585), (AttractionColumnKind.TotalProfit, UIStrings.TotalProfitShop, 586),
			(AttractionColumnKind.Satisfaction, UIStrings.CustomerSatisfactionShop, 587),
		},
		BuildCategory.Sideshows => new[]
		{
			(AttractionColumnKind.Name, UIStrings.NameGameAttr, 105), (AttractionColumnKind.Customers, UIStrings.CustomersLastMonthGame, 584),
			(AttractionColumnKind.Excitement, UIStrings.ExcitementGame, 107), (AttractionColumnKind.ProfitLastMonth, UIStrings.ProfitLastMonthGame, 585),
			(AttractionColumnKind.TotalProfit, UIStrings.TotalProfitGame, 586), (AttractionColumnKind.Satisfaction, UIStrings.CustomerSatisfactionGame, 587),
		},
		_ => new[] { (AttractionColumnKind.Name, UIStrings.NameItemAttr, 105), (AttractionColumnKind.NumberOwned, UIStrings.NumberOwnedAttr, 588) },
	};

	/// <summary>Whether a column draws a red-to-green bar (the rows' type-9 cells with ridestatbar.wct, range 0..1024).</summary>
	public static bool IsGauge( AttractionColumnKind kind ) => kind is AttractionColumnKind.Excitement or AttractionColumnKind.Repair or AttractionColumnKind.Life or AttractionColumnKind.Satisfaction;

	/// <summary>Whether a column shows money (formatter 0x490250, right-aligned); counts are centred (0x48fde0) and the name left-aligned.</summary>
	public static bool IsMoney( AttractionColumnKind kind ) => kind is AttractionColumnKind.ProfitLastMonth or AttractionColumnKind.TotalProfit;

	/// <summary>
	/// Rows of one category ordered by <paramref name="column"/>: the name column by name (ordinal), the others by their cell's value with
	/// unknown values first; ties keep the attraction id order.
	/// </summary>
	public static IReadOnlyList<AttractionListRow> Rows( IEnumerable<AttractionListRow> attractions, BuildCategory category, int column, bool descending,
		Func<AttractionListRow, IReadOnlyList<AttractionCell>> cells )
	{
		var listed = attractions.Where( row => row.Category == category ).OrderBy( row => row.AttractionId ).ToArray();
		if ( column <= 0 )
			return Rows( listed, category, descending );
		double Key( AttractionListRow row ) => cells( row ) is { } values && column < values.Count && values[column].Key is { } key ? key : double.NegativeInfinity;
		return (descending ? listed.OrderByDescending( Key ) : listed.OrderBy( Key )).ThenBy( row => row.AttractionId ).ToArray();
	}
}

/// <summary>
/// The row order of a sorted overview list between header presses.
/// </summary>
// [BIN:TPW-EXE:0x0066403b sorted row insert, 0x00663edc compare] a row added to a list with the sort attribute (0x10) goes before the first row it sorts strictly before, the compare being
// "new < row" ascending and "row < new" descending on the sort column's cell (ties go after equal rows), and at the end when no row follows it; the refresh handlers
// (0x4955e0, 0x493230) rewrite the cells in place with 0x6644ea, which redraws the rows (0x663324) but never moves one, so the order stays until a header press (0x665a44) sorts the list again
internal static class ListRowOrder
{
	/// <summary>
	/// Brings <paramref name="order"/> (ids in list order) up to date with <paramref name="current"/>: rows that left the list drop out, the others keep their place
	/// and each new row (in the given sequence) is inserted where the sorted insert puts it. Returns the rows in list order.
	/// </summary>
	/// <param name="before">Whether the first row sorts strictly before the second under the list's column and direction.</param>
	public static IReadOnlyList<T> Keep<T>( List<int> order, IEnumerable<T> current, Func<T, int> id, Func<T, T, bool> before )
	{
		var rows = new Dictionary<int, T>();
		foreach ( var row in current )
			rows[id( row )] = row;
		order.RemoveAll( key => !rows.ContainsKey( key ) );
		var known = order.ToHashSet();
		foreach ( var key in rows.Keys.Where( key => !known.Contains( key ) ).OrderBy( key => key ).ToArray() )
		{
			var row = rows[key];
			var at = order.FindIndex( other => before( row, rows[other] ) );
			order.Insert( at < 0 ? order.Count : at, key );
		}
		return order.Select( key => rows[key] ).ToArray();
	}
}

/// <summary>What one column of the All Attractions list shows.</summary>
public enum AttractionColumnKind { Name, Users, Excitement, Repair, Life, Customers, ProfitLastMonth, TotalProfit, Satisfaction, NumberOwned }

/// <summary>One list cell: its text (a count or money), or a 0..1 bar; <see cref="Key"/> orders the column, null when unknown.</summary>
public readonly record struct AttractionCell( string? Text, float? Gauge, double? Key );

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
		("headerRides", UIStrings.RidesRiddenVisitor, 132, UiAlign.Left),
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

	/// <summary>The rows of an open list in the order it already holds: new visitors are inserted by the list's sorted insert, nobody is re-sorted (<see cref="ListRowOrder"/>).</summary>
	public static IReadOnlyList<Guest> Keep( List<int> order, IEnumerable<Guest> guests, Func<Guest, VisitorRowValues> values, int column, bool descending )
	{
		column = Math.Clamp( column, 0, Columns.Length - 1 );
		return ListRowOrder.Keep( order, guests, guest => guest.Id, ( added, row ) => descending
			? values( row ).SortKey( column ) < values( added ).SortKey( column )
			: values( added ).SortKey( column ) < values( row ).SortKey( column ) );
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
	public static VisitorRowValues ValuesOf( Guest guest, bool refreshed ) => new( guest.VisitorNumber, guest.Money, Hours( guest, refreshed ), guest.RidesTaken,
		guest.Thought == GuestThought.None ? -1 : (int)guest.Thought - 1, (((int)guest.Happiness & 0xff) << 10) / 100 );

	/// <summary>The hours of the time-in-park cell from the park turns since admission.</summary>
	public static long DisplayHours( long turns, bool refreshed )
	{
		var steps = turns >> 2;
		return ParkCalendar.SecondsPerTurn * (refreshed ? steps << 2 : steps) / ParkCalendar.SecondsPerHour;
	}

	// [BIN:TPW-EXE:0x004fd950] retained display age reproduces unsigned current-turn minus creation stamp, including the four-turn quantization above.
	// [APPROX:GUEST-920] older own-format saves lack that integer provenance and retain their previous seconds-derived fallback. Roster membership is unchanged and unqualified.
	private static long? Hours( Guest guest, bool refreshed ) => guest.TimeInParkSeconds is { } elapsed
		? DisplayHours( guest.DisplayTimeInParkTurns ?? (long)Math.Floor( elapsed * 1000 / ParkCalendar.TurnMilliseconds ), refreshed ) : null;

	/// <summary>The UITEXT record of a time-in-park cell (<see cref="UiTextRecord"/>).</summary>
	public static int DurationRecord( long hours ) => hours > 48 ? UiTextRecord.Days : hours > 24 ? UiTextRecord.Day : UiTextRecord.Hours;

	/// <summary>The number the time-in-park cell shows: whole days above 24 hours, else the hours.</summary>
	public static long DurationValue( long hours ) => hours > 24 ? hours / 24 : hours;
}
