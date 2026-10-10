namespace OpenTPW.Hud;

/// <summary>Column of the buy window list a header button sorts by.</summary>
public enum BuySortColumn { Default, Name, Price }

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
	private IReadOnlyList<BuildItem>? items;

	public BuyListModel( IBuildCatalog catalog, Func<BuildItem, string> name, Func<BuildItem, long> price )
	{
		this.catalog = catalog;
		this.name = name;
		this.price = price;
	}

	public BuildCategory Category { get; private set; } = BuildCategory.Rides;
	public BuildItem? SelectedItem { get; private set; }
	public int FirstRow { get; private set; }
	public BuySortColumn Sort { get; private set; }
	public bool SortDescending { get; private set; }
	public int MaxFirstRow => Math.Max( 0, Items.Count - VisibleRows );
	public int SelectedIndex => SelectedItem == null ? -1 : Items.ToList().IndexOf( SelectedItem );

	/// <summary>Items of the current category: catalogue order (Info.Id) unless a header sorted them.</summary>
	public IReadOnlyList<BuildItem> Items => items ??= Sorted();

	/// <summary>Forgets the cached order (the catalogue or a name changed).</summary>
	public void Invalidate() => items = null;

	private IReadOnlyList<BuildItem> Sorted()
	{
		var all = catalog.GetItems( Category );
		if ( Sort == BuySortColumn.Default )
			return all;
		var sorted = Sort == BuySortColumn.Name
			? all.OrderBy( name, StringComparer.CurrentCultureIgnoreCase ).ThenBy( item => item.InfoId )
			: all.OrderBy( price ).ThenBy( item => item.InfoId );
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
		items = null;
		Select( Math.Max( 0, SelectedIndex ) );
	}
}

/// <summary>
/// The balance change shown beside the balance (Mac HUD table control 48): the sum of the balance changes within
/// <see cref="Seconds"/> of the last one.
/// </summary>
// [APPROX:UI-046] the change text stays 4 s after the last balance change and sums the changes within that time — evidence needed: capture of the original cash trend display
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
