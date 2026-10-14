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
