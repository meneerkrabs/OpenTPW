namespace OpenTPW.Hud;

/// <summary>Column of the buy window list a header button sorts by.</summary>
public enum BuySortColumn { Name, Price }

/// <summary>
/// List state of the buy window: the category, its items in list order, the selection and the first visible row.
/// Independent of the level so it can be tested without a game (the window and purchase flow live in <see cref="ParkHud"/>).
/// </summary>
public sealed class BuyListModel
{
	/// <summary>Rows of the list that fit the content region (1037,427)-(1727,871) at the row height.</summary>
	public const int VisibleRows = 10;

	private readonly IBuildCatalog catalog;
	private readonly Func<BuildItem, string> name;
	private readonly Func<BuildItem, long> price;
	private readonly Func<BuildCategory, IEnumerable<BuildItem>>? extraRows;
	private readonly Func<BuildItem, bool>? listed;
	private IReadOnlyList<BuildItem>? items;

	private readonly ListSortMemory memory;

	/// <param name="memory">The session's remembered sort of this list (a fresh one starts by name ascending).</param>
	/// <param name="extraRows">Rows a category lists besides its catalogue items (the Miscellaneous list's Buy Land and Clear Land).</param>
	/// <param name="listed">Whether a catalogue item gets a row: the original lists only the types its control record marks available (null: all).</param>
	public BuyListModel( IBuildCatalog catalog, Func<BuildItem, string> name, Func<BuildItem, long> price, ListSortMemory? memory = null,
		Func<BuildCategory, IEnumerable<BuildItem>>? extraRows = null, Func<BuildItem, bool>? listed = null )
	{
		this.listed = listed;
		this.catalog = catalog;
		this.name = name;
		this.price = price;
		this.extraRows = extraRows;
		this.memory = memory ?? new ListSortMemory();
		Sort = this.memory.BuyColumn;
		SortDescending = this.memory.BuyDescending;
	}

	public BuildCategory Category { get; private set; } = BuildCategory.Rides;
	public BuildItem? SelectedItem { get; private set; }
	public int FirstRow { get; private set; }
	public BuySortColumn Sort { get; private set; }
	public bool SortDescending { get; private set; }
	public int MaxFirstRow => Math.Max( 0, Items.Count - VisibleRows );
	public int SelectedIndex => SelectedItem == null ? -1 : Items.ToList().IndexOf( SelectedItem );

	/// <summary>Items of the current category in list order: by name unless a header sorted them otherwise.</summary>
	public IReadOnlyList<BuildItem> Items => items ??= Sorted();

	/// <summary>Forgets the cached order (the catalogue or a name changed).</summary>
	public void Invalidate() => items = null;

	private IReadOnlyList<BuildItem> Sorted()
	{
		// [BIN:TPW-EXE:0x4aaff0 buy list filler] a type gets a row only when its model loaded, its UI type is the tab's, it is no add-on and its control record's byte +0x10 (0x4d3d10: set when the type is researched or available at the start) is nonzero (0x4ab023)
		var all = catalog.GetItems( Category ).Where( item => listed?.Invoke( item ) != false ).Concat( extraRows?.Invoke( Category ) ?? Enumerable.Empty<BuildItem>() );
		// [BIN:STP-PPC:0x10178768 list insert] the buy list (504, attribute 0x91 with the sorted-insert bit 0x10) inserts each row before the first row whose name is greater (wcscmp, 0x10178654); its sort word starts at 1 (column 0, the name, ascending), so equal names keep catalogue order
		var sorted = Sort == BuySortColumn.Name
			? all.OrderBy( name, StringComparer.Ordinal )
			: all.OrderBy( price ).ThenBy( name, StringComparer.Ordinal );
		return (SortDescending ? sorted.Reverse() : sorted).ToArray();
	}

	/// <summary>Shows another category and selects its first item.</summary>
	public void SelectCategory( BuildCategory category )
	{
		Category = category;
		items = null;
		FirstRow = 0;
		Select( 0 );
	}

	/// <summary>Selects row <paramref name="index"/> (clamped) and scrolls it into view; an empty list clears the selection.</summary>
	public void Select( int index )
	{
		var list = Items;
		if ( list.Count == 0 )
		{
			SelectedItem = null;
			return;
		}
		index = Math.Clamp( index, 0, list.Count - 1 );
		SelectedItem = list[index];
		if ( index < FirstRow )
			FirstRow = index;
		else if ( index >= FirstRow + VisibleRows )
			FirstRow = index - VisibleRows + 1;
	}

	/// <summary>Keeps the selection when it is still listed, else selects the first row.</summary>
	public void EnsureSelection()
	{
		items = null;
		if ( SelectedItem == null || !Items.Contains( SelectedItem ) )
			Select( 0 );
	}

	public void Scroll( int rows ) => SetFirstRow( FirstRow + rows );

	public void SetFirstRow( int first ) => FirstRow = Math.Clamp( first, 0, MaxFirstRow );

	/// <summary>A click on a column header: sort by it, or reverse the order when it already sorts.</summary>
	public void SortBy( BuySortColumn column )
	{
		SortDescending = Sort == column && !SortDescending;
		Sort = column;
		memory.BuyColumn = Sort;
		memory.BuyDescending = SortDescending;
		items = null;
		Select( Math.Max( 0, SelectedIndex ) );
	}
}

/// <summary>
/// The balance change shown beside the balance (Mac HUD table control 48): the sum of the balance changes within
/// <see cref="Seconds"/> of the last one.
/// </summary>
// [APPROX:UI-047] the change text stays 4 s after the last balance change and sums the changes within that time — evidence needed: capture of the original cash trend display
public sealed class CashChangeTracker
{
	public const float Seconds = 4f;
	private long last;
	private float remaining;
	private long sum;

	public CashChangeTracker( long balance ) => last = balance;

	/// <summary>The change currently shown; 0 when none.</summary>
	public long Change => remaining > 0 ? sum : 0;

	public void Update( long balance, float elapsed )
	{
		if ( balance != last )
		{
			sum = (remaining > 0 ? sum : 0) + (balance - last);
			last = balance;
			remaining = Seconds;
		}
		else if ( remaining > 0 )
			remaining = Math.Max( 0, remaining - elapsed );
	}
}
