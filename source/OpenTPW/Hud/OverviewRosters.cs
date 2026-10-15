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

/// <summary>What one column of the All Attractions list shows.</summary>
public enum AttractionColumnKind { Name, Users, Excitement, Repair, Life, Customers, ProfitLastMonth, TotalProfit, Satisfaction, NumberOwned }

/// <summary>One list cell: its text (a count or money), or a 0..1 bar; <see cref="Key"/> orders the column, null when unknown.</summary>
public readonly record struct AttractionCell( string? Text, float? Gauge, double? Key );

/// <summary>
/// The All Visitors window's list model: in-park ordering by id or happiness, the visitor detail rows
/// of the original labels (UITEXT 66, 113-118) and selection healing. A null detail value means
/// "not simulated".
/// </summary>
internal static class ParkVisitorRoster
{
	/// <summary>In-park visitors in list order: by id (default) or happiness, ties by id.</summary>
	public static IReadOnlyList<Guest> Rows( IEnumerable<Guest> guests, Func<Guest, bool> isInPark, bool byHappiness, bool descending )
	{
		var listed = guests.Where( isInPark );
		IOrderedEnumerable<Guest> ordered = byHappiness
			? descending ? listed.OrderByDescending( guest => guest.Happiness ) : listed.OrderBy( guest => guest.Happiness )
			: descending ? listed.OrderByDescending( guest => guest.Id ) : listed.OrderBy( guest => guest.Id );
		return ordered.ThenBy( guest => guest.Id ).ToArray();
	}

	/// <summary>The selected visitor id after the simulation changed: the current one while still in the park, else the first row, 0 when empty.</summary>
	public static int HealedSelection( IReadOnlyList<Guest> rows, int selectedId ) =>
		rows.Any( guest => guest.Id == selectedId ) ? selectedId : rows.Count > 0 ? rows[0].Id : 0;

	/// <summary>The status text of a guest's state: <c>KIDSTATES.str</c> of its state index, the state's name when the entry is empty.</summary>
	// [DATA:Language/*/*/KIDSTATES.str] GuestState values are the table's indices (state 255 is not an entry).
	public static string StatusOf( Guest guest, string[] kidStates ) =>
		(int)guest.State >= 0 && (int)guest.State < kidStates.Length && !string.IsNullOrWhiteSpace( kidStates[(int)guest.State] )
			? kidStates[(int)guest.State] : guest.State.ToString();

	/// <summary>The text of a guest's current thought: <c>THOUGHTS.str</c> of its thought index; 'None' and missing entries are empty.</summary>
	// [DATA:Language/*/*/THOUGHTS.str] GuestThought values are the table's indices.
	public static string ThoughtOf( Guest guest, string[] thoughts ) =>
		guest.Thought != GuestThought.None && (int)guest.Thought < thoughts.Length && !string.IsNullOrWhiteSpace( thoughts[(int)guest.Thought] )
			? thoughts[(int)guest.Thought] : "";

	/// <summary>
	/// Detail rows of <paramref name="guest"/> from the original visitor labels (UITEXT 66 status,
	/// 114-118 cash/time/rides/happiness): live values where simulated, null = "not simulated".
	/// </summary>
	// [EXT:overview-detail-rows] the original visitors window has no detail rows; time in park displays OpenTPW simulated elapsed HH:MM:SS (unknown is an em dash); the original's "Time In Park" list column shows whole hours (ticks → seconds / 36e9)
	public static (UIStrings Label, string? Value)[] StatRows( Guest guest, string[] kidStates, Func<long, string> money ) => new[]
	{
		(UIStrings.CurrentStatus, StatusOf( guest, kidStates )),
		(UIStrings.CashRemainingVisitor, money( guest.Money )),
		(UIStrings.TimeInParkVisitor, TimeInPark( guest )),
		(UIStrings.RidesRiddenVisitor, guest.RidesTaken.ToString( System.Globalization.CultureInfo.InvariantCulture )),
		(UIStrings.HappinessVisitor, $"{HappinessPercent( guest.Happiness )}%"),
	};

	/// <summary>Elapsed simulation time, floored to whole seconds; old-save unknown durations stay unknown.</summary>
	public static string TimeInPark( Guest guest )
	{
		if ( guest.TimeInParkSeconds is not { } elapsed )
			return "—";
		var seconds = (long)Math.Floor( elapsed );
		return FormattableString.Invariant( $"{seconds / 3600:D2}:{seconds / 60 % 60:D2}:{seconds % 60:D2}" );
	}

	/// <summary>Happiness as a whole percent, clamped 0..100 (like the staff window's happiness row).</summary>
	public static int HappinessPercent( float happiness ) => (int)Math.Round( Math.Clamp( happiness, 0, 100 ) );
}
