using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The All Attractions window, built from tp.exe's table 0x7508e0: the window4 frame, the list_all frame with the four category tabs
/// (b_sride/b_sshow/b_sshop/b_sfeature), the title box, the tick and the park information, all-staff and all-visitors buttons, and per
/// category one of the list tables 0x750ab8 (rides and shops), 0x750ba0 (sideshows) and 0x750ca0 (features) with its columns and scroll bar.
/// </summary>
// [BIN:TPW-EXE:0x00495aa0 attractions window] the builder applies table 0x7508e0, selects the category's tab, fills the list with the category's list table
// (0 rides 0x494ec0, 1 shops 0x494c70, 2 sideshows 0x494250, 3 features 0x495110), titles it UITEXT 78-81 and plays 0xbf; a category of -1 reopens the last one
// (0x7ca154), and the object windows open their own category with their object selected (0x4af91b, 0x4b0d06, 0x4b22ff, 0x4b5158)
// [BIN:TPW-EXE:0x4955e0 list refresh, 0x66403b sorted row insert] the 2000 ms refresh rewrites the cells in place and never moves a row, a new row goes in by the list's sorted insert (ListRowOrder), and the first row added selects row 0 (0x66525c)
// [EXT:list-row-pitch] the list's row pitch is the row item's height (0x4944d0: font slot 7's height scaled to the canvas + 6), taken as the list's pitch, a design choice: the original's pitch follows the screen mode's font bank (0x663718 into +0x170), which resolution-independent output sizes do not have (UI-002)
// [EXT:list-selection-by-id] the selected row follows its attraction while rows are added or removed, where the original keeps the selected position (+0x14c) — a design choice that keeps a live list from highlighting another attraction
// [BIN:TPW-EXE:0x4955e0 list refresh bars] the list's bar cells are scaled as the object windows' gauges are: ((value & 0xff) << 10) / 100 of the 0..1024 range, for the excitement (0x4e0860), repair (the __ftol of the repair), life (0x4dd6d0) and satisfaction (0x4e1e30) columns of the ride, shop and sideshow lists; OpenTPW feeds them the object window's gauge values (ObjectStatOf), the satisfaction one from the visit history as the window does
public sealed partial class ParkHud
{
	/// <summary>The round entry button of the All Attractions window in the buy and staff windows: the window's own rides tab icon.</summary>
	internal const string AllAttractionsModel = "b_srides";
	private static LayoutTable AttractionsTable => OriginalLayoutTables.AttractionsWindow;
	// [BIN:TPW-EXE:0x00495aa0 attractions tabs] category 0 selects tab 0x12c4ba, 1 0x12c4bc, 2 0x12c4bb, 3 0x12c4bd; the titles are UITEXT 78-81
	private static readonly (BuildCategory Category, uint Tab, UIStrings Title)[] AttractionCategories =
	{
		(BuildCategory.Rides, 0x12C4BAu, UIStrings.AllRides),
		(BuildCategory.Sideshows, 0x12C4BBu, UIStrings.AllSideshows),
		(BuildCategory.Shops, 0x12C4BCu, UIStrings.AllShops),
		(BuildCategory.Features, 0x12C4BDu, UIStrings.AllMiscItems),
	};

	private static LayoutTable AttractionListTable( BuildCategory category ) => category switch
	{
		BuildCategory.Sideshows => OriginalLayoutTables.SideshowList,
		BuildCategory.Features => OriginalLayoutTables.FeatureList,
		_ => OriginalLayoutTables.RideShopList,
	};
	/// <summary>The rows' region of a category's list (the list control's inner rectangle).</summary>
	private static UiRect AttractionContent( BuildCategory category ) => AttractionListTable( category ).Root.Inner!.Value;
	/// <summary>The column header rectangles of a category's list, left to right (controls 0x10, 0x11, ...).</summary>
	private static UiRect[] AttractionColumnRects( BuildCategory category ) =>
		AttractionListTable( category ).Root.Children.Where( node => node.Type == 2 && node.Id is >= 0x10 and < 0x20 ).OrderBy( node => node.Id ).Select( node => node.Rect ).ToArray();
	private static LayoutNode AttractionSlider => OriginalLayoutTables.RideShopList.Root.Children.Single( node => node.Type == 3 );

	/// <summary>Seconds between two refreshes of the rows' values (timer 0x80083, 2000 ms, armed on message 0x15 by 0x4955e0).</summary>
	public const float AttractionValueRefreshSeconds = 2f;

	private UiScreen? attractionScreen;
	private UiButton? deleteAttractionButton;
	private BuildCategory attractionCategory
	{
		get => (BuildCategory)Sorts.AttractionCategory;
		set => Sorts.AttractionCategory = (int)value;
	}
	/// <summary>The sorted column of the current category tab, remembered for the session (UI-084).</summary>
	private int attractionSortColumn
	{
		get => Sorts.AttractionColumn[(int)attractionCategory];
		set => Sorts.AttractionColumn[(int)attractionCategory] = value;
	}
	/// <summary>The sort direction of the current category tab, remembered for the session (UI-084).</summary>
	private bool attractionSortDescending
	{
		get => Sorts.AttractionDescending[(int)attractionCategory];
		set => Sorts.AttractionDescending[(int)attractionCategory] = value;
	}
	private int selectedAttractionId;
	private int attractionFirstRow;
	private float attractionValueAge;
	private float attractionRowHeight = 30f;

	public bool AttractionsWindowOpen => attractionScreen != null && Stack.Screens.Contains( attractionScreen );
	public int SelectedAttractionId => SelectedAttractionRow.AttractionId;
	public int AttractionFirstRow => attractionFirstRow;
	public BuildCategory AttractionCategory => attractionCategory;
	/// <summary>The list's row pitch in canvas units, as last drawn.</summary>
	public float AttractionRowHeight => attractionRowHeight;
	/// <summary>The rows the list shows at once.</summary>
	public int AttractionVisibleRows => Math.Max( 1, (int)MathF.Floor( AttractionContent( attractionCategory ).Height / attractionRowHeight ) );
	/// <summary>The top of the list's first row in canvas units.</summary>
	public float AttractionRowsTop => AttractionContent( attractionCategory ).Y;
	/// <summary>Live rows of the selected category in list order, the developer prototype Totem included.</summary>
	// [EXT:developer-prototype] the prototype ride lives beside the object world; Level.ListAttractions is where both sources merge
	public IReadOnlyList<AttractionListRow> AttractionItems => OrderAttractions( level.ListAttractions().Select( AttractionRow ).ToArray() );

	/// <summary>The row order the open category list holds (attraction ids); null before the list's first fill and after a category or header change, which sort it afresh.</summary>
	private List<int>? attractionOrder;

	/// <summary>The category's rows in the list's order: sorted when the list is filled or a header is pressed, afterwards kept with new rows inserted (<see cref="ListRowOrder"/>).</summary>
	private IReadOnlyList<AttractionListRow> OrderAttractions( IReadOnlyList<AttractionListRow> rows )
	{
		if ( attractionOrder == null )
		{
			var sorted = ParkAttractionRoster.Rows( rows, attractionCategory, attractionSortColumn, attractionSortDescending, AttractionCellsOf );
			attractionOrder = sorted.Select( row => row.AttractionId ).ToList();
			return sorted;
		}
		return ParkAttractionRoster.Keep( attractionOrder, rows, attractionCategory, attractionSortColumn, attractionSortDescending, AttractionCellsOf );
	}

	/// <summary>
	/// The window's rows for one frame: the screen refreshes them when it updates and every action that changes the category or order
	/// refreshes them again, so the list and scrollbar share one snapshot instead of re-projecting the park's objects for every draw call.
	/// <see cref="AttractionItems"/> stays live for callers outside the frame.
	/// </summary>
	private IReadOnlyList<AttractionListRow> attractionRows = Array.Empty<AttractionListRow>();
	/// <summary>The cells of each listed attraction, taken when the window opens, the category or order changes and every 2 s (the original's timer).</summary>
	private readonly Dictionary<int, IReadOnlyList<AttractionCell>> attractionCells = new();
	private readonly Dictionary<int, OriginalObject> attractionObjects = new();

	private IReadOnlyList<AttractionListRow> RefreshAttractionRows( bool values = false )
	{
		var listed = level.ListAttractions();
		attractionObjects.Clear();
		foreach ( var item in listed )
			attractionObjects[item.Runtime.AttractionId] = item;
		if ( values )
		{
			attractionCells.Clear();
			attractionValueAge = 0;
		}
		foreach ( var id in attractionCells.Keys.Where( id => !attractionObjects.ContainsKey( id ) ).ToArray() )
			attractionCells.Remove( id );
		return attractionRows = OrderAttractions( listed.Select( AttractionRow ).ToArray() );
	}

	private AttractionListRow SelectedAttractionRow => attractionRows.FirstOrDefault( row => row.AttractionId == selectedAttractionId );
	private int AttractionMaxFirstRow => ParkAttractionRoster.MaxFirstRow( attractionRows.Count, AttractionVisibleRows );
	private int AttractionRowIndex( int id )
	{
		var rows = attractionRows;
		for ( var index = 0; index < rows.Count; index++ )
			if ( rows[index].AttractionId == id )
				return index;
		return -1;
	}

	private AttractionListRow AttractionRow( OriginalObject item ) => new( item.Runtime.AttractionId, EntryName( item.Entry ),
		(BuildCategory)item.Entry.WhichUIType, item.IsOpen, item.Runtime.State.IsBroken,
		item.Visitors.QueueLength, item.Runtime.State.OnRide, item.Visitors.WearGauge, item.Visitors.ExcitementLevel );

	/// <summary>The cells of one row (its taken values, or new ones for a row not seen since the last refresh).</summary>
	internal IReadOnlyList<AttractionCell> AttractionCellsOf( AttractionListRow row )
	{
		if ( attractionCells.TryGetValue( row.AttractionId, out var cells ) )
			return cells;
		if ( !attractionObjects.TryGetValue( row.AttractionId, out var item ) )
			item = level.ListAttractions().FirstOrDefault( candidate => candidate.Runtime.AttractionId == row.AttractionId );
		if ( item == null )
			return Array.Empty<AttractionCell>();
		return attractionCells[row.AttractionId] = AttractionCellsFor( item, row.Category );
	}

	/// <summary>
	/// The economy record of a listed object when it may supply the business columns: a Theme Park World park of its own (no visit), the level's own
	/// economy, and a record of the object's info id and kind.
	/// </summary>
	private ParkObjectState? TrustedAttractionState( OriginalObject item, BuildCategory category )
	{
		var finance = FinanceEconomy;
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || level.IsReadOnlyVisit || finance == null || !ReferenceEquals( finance, level.Park?.Economy )
			|| item is PrototypeRide || level.GetEconomyInstance( item ) is not int instance || !finance.TryGetObject( instance, out var state ) || state.InfoId != item.Entry.InfoId )
			return null;
		var kind = category switch
		{
			BuildCategory.Rides => ParkObjectKind.Ride,
			BuildCategory.Shops => ParkObjectKind.Shop,
			BuildCategory.Sideshows => ParkObjectKind.Sideshow,
			_ => (ParkObjectKind?)null,
		};
		return kind == state.Kind ? state : null;
	}

	private IReadOnlyList<AttractionCell> AttractionCellsFor( OriginalObject item, BuildCategory category )
	{
		var columns = ParkAttractionRoster.Columns( category );
		var cells = new AttractionCell[columns.Length];
		var trusted = TrustedAttractionState( item, category );
		var state = LiveState( item );
		var economy = Status.EconomySource as ParkEconomy;
		AttractionCell Count( long? value ) => new( value?.ToString( System.Globalization.CultureInfo.InvariantCulture ), null, value );
		AttractionCell Money( long? value ) => new( value is { } amount ? FormatMoney( amount ) : null, null, value );
		AttractionCell Gauge( ObjectRowKind kind ) => ObjectStatOf( item, kind, state, economy ).Gauge is { } gauge ? new( null, gauge, gauge ) : new( null, null, null );
		for ( var column = 0; column < columns.Length; column++ )
		{
			cells[column] = columns[column].Kind switch
			{
				AttractionColumnKind.Name => new( EntryName( item.Entry ), null, null ),
				AttractionColumnKind.Users or AttractionColumnKind.Customers => Count( trusted?.CustomersLast30Days ),
				AttractionColumnKind.ProfitLastMonth => Money( trusted?.ProfitLast30Days ),
				AttractionColumnKind.TotalProfit => Money( trusted?.TotalProfit ),
				AttractionColumnKind.Excitement => Gauge( ObjectRowKind.Excitement ),
				AttractionColumnKind.Repair => Gauge( ObjectRowKind.Repair ),
				AttractionColumnKind.Life => Gauge( ObjectRowKind.Life ),
				AttractionColumnKind.Satisfaction => Gauge( ObjectRowKind.Satisfaction ),
				// [BIN:TPW-EXE:0x00494200 feature row] the number owned is the park's count of objects of the feature's info id (0x4ca8c0 with the word at +0xe)
				AttractionColumnKind.NumberOwned => Count( level.Objects.Objects.Count( other => !other.IsDeleted && other.Entry.InfoId == item.Entry.InfoId ) ),
				_ => new( null, null, null ),
			};
		}
		return cells;
	}

	/// <summary>Opens the window on <paramref name="category"/> (null: the category it showed last) with <paramref name="select"/> chosen when it is listed.</summary>
	public void OpenAttractionsWindow( BuildCategory? category, OriginalObject? select = null )
	{
		if ( AttractionsWindowOpen )
			return;
		if ( category is { } chosen )
			attractionCategory = chosen;
		attractionFirstRow = 0;
		selectedAttractionId = 0;
		attractionOrder = null;
		RefreshAttractionRows( values: true );
		// [BIN:TPW-EXE:0x00494ff1 list selection] with an object the filler selects its row (0x664d2a → 0x665739)
		if ( select != null && AttractionRowIndex( select.Runtime.AttractionId ) is >= 0 and var index )
		{
			selectedAttractionId = select.Runtime.AttractionId;
			SelectAttraction( index );
		}
		EnsureAttractionSelection();
		attractionScreen ??= CreateAttractionsScreen();
		Stack.Push( attractionScreen );
		attractionScreen.Focus( attractionScreen.Find( "attractionList" ) );
	}

	/// <summary>The buy, staff, visitors and park information windows open it on the category it showed last (0x495aa0 with -1).</summary>
	public void OpenAttractionsWindow() => OpenAttractionsWindow( null );

	public void CloseAttractionsWindow()
	{
		if ( attractionScreen == null || !AttractionsWindowOpen )
			return;
		while ( Stack.Top != attractionScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == attractionScreen )
			Stack.Pop();
	}

	/// <summary>Selects the list row <paramref name="index"/> (clamped) and scrolls it into view.</summary>
	public void SelectAttraction( int index )
	{
		var items = attractionRows;
		if ( items.Count == 0 )
		{
			selectedAttractionId = 0;
			return;
		}
		index = Math.Clamp( index, 0, items.Count - 1 );
		selectedAttractionId = items[index].AttractionId;
		attractionFirstRow = Math.Clamp( attractionFirstRow, Math.Max( 0, index - AttractionVisibleRows + 1 ), index );
	}

	public void ScrollAttractions( int rows ) => SetAttractionFirstRow( attractionFirstRow + rows );
	public void SetAttractionFirstRow( int first ) => attractionFirstRow = Math.Clamp( first, 0, AttractionMaxFirstRow );

	// [BIN:TPW-EXE:0x665a44 generic list sort, list attribute 0x10] the same column toggles its direction, a new column starts ascending; the initial sort column is 0 (ascending, the per-category sort words 0x750d5c..0x750d68 = 1), so the first press on column 0 descends, and each category keeps its word for the session (ListSortMemory).
	public void SortAttractions( int column = 0 )
	{
		attractionSortDescending = attractionSortColumn == column && !attractionSortDescending;
		attractionSortColumn = column;
		attractionOrder = null;
		RefreshAttractionRows();
		var index = AttractionRowIndex( selectedAttractionId );
		EnsureAttractionSelection();
		if ( index >= 0 )
			SelectAttraction( index );
	}

	/// <summary>The select button and Enter: selects the object in the park and leaves the list, as a park click does.</summary>
	// [EXT:overview-select-button] the go-to/select button (the shipped b_goto art has no texture in ui.wad) and Enter select the object in the park and close the window; the original's list messages are 0x400 (left click: info panel, list kept) and 0x402 (right click: centre the camera and close)
	public void SelectSelectedAttraction()
	{
		var match = level.ListAttractions().FirstOrDefault( item => item.Runtime.AttractionId == selectedAttractionId );
		if ( match == null )
			return;
		CloseAttractionsWindow();
		SelectObject( match );
	}

	/// <summary>A left click on a row: opens the object's info panel (the info arm) and keeps the list.</summary>
	// [BIN:TPW-EXE:0x6655a2 list click] a click of button 0 selects the row and posts 0x400, which opens the object's info panel and keeps the list
	// [BIN:TPW-EXE:0x634963 mouse button codes] button 0 is the primary (left) button: DirectInput's DIMOFS_BUTTON0 (dwOfs 0xc) becomes code 0xf0 (0x63497c), kept primary when Windows swaps the buttons (0x634420), and 0x486ca1 turns 0xf0 into button 0
	private void OpenSelectedAttractionInfo()
	{
		var match = level.ListAttractions().FirstOrDefault( item => item.Runtime.AttractionId == selectedAttractionId );
		if ( match != null )
			ShowInfoArmKeepingWindows( match );
	}

	/// <summary>A right click on a row: centres the park camera on the object and closes the list.</summary>
	// [BIN:TPW-EXE:0x66563d list click, 0x4867b0 locate] a click of the other button selects the row and posts 0x402, which centres the camera on the object's cell and closes the window
	private void LocateSelectedAttraction()
	{
		var match = level.ListAttractions().FirstOrDefault( item => item.Runtime.AttractionId == selectedAttractionId );
		if ( match != null && Camera.Mode is ParkCameraMode camera )
			camera.Focus( match.Position );
		CloseAttractionsWindow();
	}

	/// <summary>Sells and removes the selected attraction through the park's removal seam (confirmation when enabled).</summary>
	private void ConfirmDeleteSelectedAttraction()
	{
		var row = SelectedAttractionRow;
		var match = level.ListAttractions().FirstOrDefault( item => item.Runtime.AttractionId == row.AttractionId );
		// [EXT:online-coop-commands] no second sale of an object whose sale waits for the host (C3b)
		if ( match == null || !CanSellAttraction( match ) || RejectReadOnlyAction() )
			return;
		void Remove()
		{
			// Catalogue objects sell and unlink exactly once; the developer prototype removes through its own seam ([EXT:online-coop-commands]: one command either way).
			SellAttraction( match.Runtime.AttractionId );
			selectedAttractionId = 0;
			RefreshAttractionRows();
			EnsureAttractionSelection();
		}
		if ( GameOptions.Current.Confirmations )
			Stack.Push( UiDialogs.Message( "confirmDeleteAttraction", () => strings[DeleteLabelOf( row.Category )] + "\n" + row.Name,
				(() => strings.Value( UIStrings.Yes ), () => { Stack.Pop(); Remove(); }),
				(() => strings.Value( UIStrings.No ), Stack.Pop) ) );
		else Remove();
	}

	// [DATA:UITEXT.str:392-395] the original delete confirmations name the category.
	internal static UIStrings DeleteLabelOf( BuildCategory category ) => category switch
	{
		BuildCategory.Rides => UIStrings.DeleteRide,
		BuildCategory.Shops => UIStrings.DeleteShop,
		BuildCategory.Sideshows => UIStrings.DeleteSideshow,
		_ => UIStrings.DeleteItem,
	};

	/// <summary>Re-derives the selection from the live park: a removed attraction hands it to the first row.</summary>
	private void EnsureAttractionSelection()
	{
		selectedAttractionId = ParkAttractionRoster.HealedSelection( attractionRows, selectedAttractionId );
		SetAttractionFirstRow( attractionFirstRow );
		if ( deleteAttractionButton != null )
		{
			var selected = level.ListAttractions().FirstOrDefault( item => item.Runtime.AttractionId == selectedAttractionId );
			deleteAttractionButton.Enabled = !level.IsReadOnlyVisit && selectedAttractionId != 0 && (selected == null || CanSellAttraction( selected ));
		}
	}

	/// <summary>
	/// Whether the list's delete button sells <paramref name="item"/>: [EXT:online-coop-commands] not while this guest's sale of it waits for the host (C3b). The list never holds a fixed item
	/// (<see cref="OriginalObjectRuntime.IsAttraction"/> excludes them), so it never offers one for sale.
	/// </summary>
	private bool CanSellAttraction( OriginalObject item ) => !level.IsSaleWaiting( item.Runtime.AttractionId );

	private UiScreen CreateAttractionsScreen()
	{
		var screen = new AttractionsScreen( this );
		var table = AttractionsTable;
		void Model( string id, LayoutNode node ) => screen.Add( new UiModelImage { Id = id, Model = node.Model!, Bounds = node.Rect, Anchor = UiAnchor.Center } );
		Model( "window", table.Root );
		Model( "listFrame", table.Model( "list_all" ) );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[AttractionCategories.First( tab => tab.Category == attractionCategory ).Title],
			Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = table.Control( 0x12C4BE ).Rect, Anchor = UiAnchor.Center } );
		foreach ( var tab in AttractionCategories )
		{
			var category = tab;
			var node = table.Control( category.Tab );
			// The tab's parent is the radio group 0x12C4B9 (type 8, style 0x11), so a press plays 0xBD even when the tab is already selected.
			screen.Add( new UiButton { Id = $"category{category.Category}", Model = node.Model, Help = strings.Help( node.Help ), ClickSound = table.ClickSoundOf( node ),
				Clicked = () => SelectAttractionCategory( category.Category ), Selected = () => attractionCategory == category.Category, Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		// the headers of every category's columns; only the current category's are shown
		foreach ( var category in AttractionCategories.Select( tab => tab.Category ) )
		{
			var rects = AttractionColumnRects( category );
			var columns = ParkAttractionRoster.Columns( category );
			for ( var column = 0; column < columns.Length; column++ )
				screen.Add( new AttractionHeader( this, category, column, columns[column].Header ) { Id = column == 0 ? $"headerName{category}" : $"header{category}{column}",
					Help = strings.Help( columns[column].Help ), Bounds = rects[column], Anchor = UiAnchor.Center } );
		}
		screen.Add( new AttractionList( this ) { Id = "attractionList", Bounds = AttractionContent( BuildCategory.Rides ), Anchor = UiAnchor.Center } );
		var slider = AttractionSlider;
		screen.Add( new AttractionScrollBar( this ) { Id = "attractionScroll", Bounds = slider.Rect, Anchor = UiAnchor.Center } );
		// The shared arrows belong to RideShopList's type 3 slider (style 0x10); the sideshow and feature lists have the same parent bit. A held arrow repeats at 500 ms then 125 ms.
		void ScrollArrow( LayoutNode node, int step )
		{
			void Scroll() => ScrollAttractions( step );
			screen.Add( new UiButton { Id = step < 0 ? "scrollUp" : "scrollDown", Model = node.Model, ClickSound = OriginalLayoutTables.RideShopList.ClickSoundOf( node ),
				Clicked = Scroll, Repeat = UiButton.ScrollArrowRepeat( Scroll ), Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		ScrollArrow( slider.Children.Single( node => node.Model == "b_up" ), -1 );
		ScrollArrow( slider.Children.Single( node => node.Model == "b_down" ), 1 );
		// [BIN:TPW-EXE:0x004952b0 attractions buttons] the tick (-2) closes the window; b_pkinfo opens the park information (0x4a52a0), b_allstaff the all-staff window
		// (0x496620) and b_kids the all-visitors window (0x493530), each replacing this one
		var okay = table.Model( "b_okay" );
		screen.Add( new UiButton { Id = "exit", Model = okay.Model, Help = strings.Help( okay.Help ), ClickSound = table.ClickSoundOf( okay ),
			Clicked = CloseAttractionsWindow, Bounds = okay.Rect, Anchor = UiAnchor.Center } );
		var parkInfo = table.Model( "b_pkinfo" );
		screen.Add( new UiButton { Id = "parkInfo", Model = parkInfo.Model, Help = strings.Help( parkInfo.Help ), Bounds = parkInfo.Rect, Anchor = UiAnchor.Center,
			Clicked = () => { CloseAttractionsWindow(); OpenParkInfoWindow(); } } );
		var allStaff = table.Model( "b_allstaff" );
		screen.Add( new UiButton { Id = "allStaff", Model = allStaff.Model, Help = strings.Help( allStaff.Help ), Bounds = allStaff.Rect, Anchor = UiAnchor.Center,
			Clicked = () => { CloseAttractionsWindow(); OpenStaffWindow(); ShowEmployedStaff( true ); } } );
		var visitors = table.Model( "b_kids" );
		screen.Add( new UiButton { Id = "allVisitors", Model = visitors.Model, Help = strings.Help( visitors.Help ), Bounds = visitors.Rect, Anchor = UiAnchor.Center,
			Clicked = () => { CloseAttractionsWindow(); OpenVisitorWindow(); } } );
		// [EXT:overview-select-button] the select button selects the row's object in the park; the delete button sells it. Both sit where the original
		// window has nothing: the delete button below the column of round buttons, the select button under the list frame (the shipped b_goto art has no texture in ui.wad)
		deleteAttractionButton = screen.Add( new UiButton { Id = "deleteAttraction", Model = "b_erase", Clicked = ConfirmDeleteSelectedAttraction, Enabled = false,
			Bounds = Rect( 1866, 682, 1968, 785 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "gotoAttraction", Text = () => strings.Extra( OpenTpwText.SelectInPark ), Font = fonts => fonts.Label,
			Clicked = SelectSelectedAttraction, Bounds = Rect( 1435, 910, 1835, 995 ), Anchor = UiAnchor.Center } );
		screen.Back = CloseAttractionsWindow;
		return screen;
	}

	// A category switch resets the list position and selection, as the staff window's role filters do.
	private void SelectAttractionCategory( BuildCategory category )
	{
		attractionCategory = category;
		attractionFirstRow = 0;
		selectedAttractionId = 0;
		attractionOrder = null;
		RefreshAttractionRows( values: true );
		EnsureAttractionSelection();
	}

	/// <summary>The All Attractions window screen: the wheel scrolls the list, the selection follows the live park, the values refresh every 2 s.</summary>
	private sealed class AttractionsScreen : UiScreen
	{
		private readonly ParkHud owner;
		public AttractionsScreen( ParkHud owner ) : base( "attractions" ) => this.owner = owner;
		internal override void PreparePointerGeometry( UiContext context )
		{
			if ( Find( "attractionList" ) is { } list )
				list.Bounds = AttractionContent( owner.attractionCategory );
			foreach ( var element in Elements.OfType<AttractionHeader>() )
				element.Visible = element.Category == owner.attractionCategory;
		}

		public override bool Update( UiContext context, UiInput input )
		{
			owner.attractionValueAge += Math.Max( 0, context.Delta );
			// [BIN:TPW-EXE:0x004955e0 list refresh] every 2000 ms (timer 0x80083) the rows of rides, shops and sideshows take new values; features keep theirs
			owner.RefreshAttractionRows( values: owner.attractionValueAge >= AttractionValueRefreshSeconds && owner.attractionCategory != BuildCategory.Features );
			owner.EnsureAttractionSelection();
			PreparePointerGeometry( context );
			if ( Find( "gotoAttraction" ) is UiButton gotoButton )
				gotoButton.Enabled = owner.selectedAttractionId != 0;
			if ( input.Wheel != 0 )
			{
				owner.ScrollAttractions( input.Wheel > 0 ? -1 : 1 );
				input = input with { Wheel = 0 };
			}
			return base.Update( context, input );
		}
	}

	/// <summary>The attraction rows: the category's columns over the list frame.</summary>
	private sealed class AttractionList : UiElement, IUiRowList
	{
		private readonly ParkHud owner;
		public AttractionList( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		public int Selected => owner.AttractionRowIndex( owner.selectedAttractionId );
		public bool ArrowsSelect => true;
		public void Select( int index ) => owner.SelectAttraction( index );
		public void Scroll( int rows ) => owner.ScrollAttractions( rows );
		public bool HoverSelects => true;
		public int HoveredRow { get; private set; } = -1;
		public void Hover( int index ) => HoveredRow = index;
		// [BIN:TPW-EXE:0x665c35 list dispatcher] attribute 0x291 lists: a left click selects the row and opens its info panel and keeps the list; a right click selects the row, centres the camera on it and closes
		public bool RowClicked( int index, bool left )
		{
			Select( index );
			if ( left )
				owner.OpenSelectedAttractionInfo();
			else
				owner.LocateSelectedAttraction();
			return true;
		}
		public override void Activate() => owner.SelectSelectedAttraction();
		public override void Adjust( int direction ) => owner.SelectAttraction( Math.Max( 0, Selected ) + direction );
		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			var content = canvas.Map( AttractionContent( owner.attractionCategory ), Anchor );
			if ( !content.Contains( point ) )
				return -1;
			var visible = (int)MathF.Floor( (point.Y - content.Y) / (owner.attractionRowHeight * canvas.Scale) );
			var index = owner.attractionFirstRow + visible;
			return visible < 0 || visible >= owner.AttractionVisibleRows || index >= owner.attractionRows.Count ? -1 : index;
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var canvas = context.Canvas;
			// [BIN:TPW-EXE:0x00494508 attractions row] a row item is font slot 7's height scaled to the 1536-unit canvas, plus 6 (0x7ca158)
			owner.attractionRowHeight = context.Fonts.Small.LineHeight * canvas.TextScale / Math.Max( 0.001f, canvas.Scale ) + 6;
			var category = owner.attractionCategory;
			var content = AttractionContent( category );
			var columns = ParkAttractionRoster.Columns( category );
			var rects = AttractionColumnRects( category );
			var items = owner.attractionRows;
			var rowHeight = owner.attractionRowHeight;
			for ( var visible = 0; visible < owner.AttractionVisibleRows && owner.attractionFirstRow + visible < items.Count; visible++ )
			{
				var index = owner.attractionFirstRow + visible;
				var row = items[index];
				var y = content.Y + visible * rowHeight;
				var rect = canvas.Map( Rect( content.X, y, content.Right, y + rowHeight ), Anchor );
				var selected = row.AttractionId == owner.selectedAttractionId;
				if ( index == HoveredRow && !selected )
					context.Batch.AddRectangle( rect, new Veldrid.RgbaByte( 60, 90, 200, 60 ) );
				if ( selected )
					context.Batch.AddRectangle( rect, new Veldrid.RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				var color = selected ? UiColors.Highlight : UiColors.Text;
				var cells = owner.AttractionCellsOf( row );
				for ( var column = 0; column < columns.Length && column < cells.Count; column++ )
				{
					var origin = canvas.Map( new NVector2( rects[column].X, y ), Anchor );
					var cell = new UiRect( origin.X, origin.Y, rects[column].Width * canvas.Scale, rowHeight * canvas.Scale );
					var kind = columns[column].Kind;
					// [BIN:TPW-EXE:0x00494690 attractions cells] the name is left-aligned (0x6636b2 column 0 mode 0), counts are centred (0x48fde0), money right-aligned (0x490250)
					// and the bars are type-9 controls over ridestatbar.wct with a 0..1024 range
					if ( ParkAttractionRoster.IsGauge( kind ) )
						UiGauge.Draw( context.Batch, cell.Inflate( -cell.Height * 0.2f ), cells[column].Gauge );
					else
						context.DrawFittedText( context.Fonts.Small, cells[column].Text ?? context.Strings.Extra( OpenTpwText.NotSimulated ), cell, color,
							kind == AttractionColumnKind.Name ? UiAlign.Left : ParkAttractionRoster.IsMoney( kind ) ? UiAlign.Right : UiAlign.Center );
				}
			}
		}
	}

	/// <summary>A column header of one category's list: a press sorts by that column, a second press reverses it.</summary>
	private sealed class AttractionHeader : UiElement
	{
		private readonly ParkHud owner;
		private readonly BuildCategory category;
		private readonly int column;
		private readonly UIStrings header;
		public AttractionHeader( ParkHud owner, BuildCategory category, int column, UIStrings header )
		{
			this.owner = owner;
			this.category = category;
			this.column = column;
			this.header = header;
		}
		public BuildCategory Category => category;
		private bool Current => owner.attractionCategory == category;
		public override bool Focusable => Visible && Enabled && Current;
		public override void Activate()
		{
			if ( !Current )
				return;
			// The header's parent is that category's list (type 7, style 0x291), which has bit 0x10, so the hook plays 0xBD.
			GameAudio.PlayUi( LayoutTable.ClickSoundFor( AttractionListTable( category ).Root ) );
			owner.SortAttractions( column );
		}
		// [BIN:TPW-EXE:0x00495020 attractions headers] each column header shows its UITEXT label in font slot 8 (0x750d58) and gets its help id (0x486660)
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			if ( Current )
				context.DrawFittedText( context.Fonts.Label, owner.strings[header], ScreenRect( context.Canvas ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
		}
	}

	/// <summary>The list's scroll bar: the table's !slider track with the b_scroller knob.</summary>
	private sealed class AttractionScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public AttractionScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private static UiRect Knob => AttractionSlider.Children.Single( node => node.Model == "b_scroller" ).Rect;
		private UiRect KnobRect()
		{
			var track = AttractionSlider.Rect;
			var travel = track.Height - Knob.Height;
			var top = track.Y + (owner.AttractionMaxFirstRow == 0 ? 0 : travel * owner.attractionFirstRow / owner.AttractionMaxFirstRow);
			return Knob with { Y = top };
		}
		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			if ( rect.Height <= 0 || owner.AttractionMaxFirstRow == 0 )
				return;
			var track = AttractionSlider.Rect;
			var y = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (y - track.Y - Knob.Height / 2) / (track.Height - Knob.Height), 0, 1 );
			owner.SetAttractionFirstRow( (int)MathF.Round( fraction * owner.AttractionMaxFirstRow ) );
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( "!slider", 0, context.Canvas.Map( AttractionSlider.Rect, Anchor ) );
			context.DrawModel( "b_scroller", owner.AttractionMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}
}
