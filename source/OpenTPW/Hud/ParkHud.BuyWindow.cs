using System.Globalization;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>One label/value row of the buy window's stats frame; a null <see cref="Gauge"/> and <see cref="Text"/> is an empty value.</summary>
public readonly record struct BuyStat( UIStrings Label, string? Text, float? Gauge );

/// <summary>
/// The original buy window (Mac buy table <c>data:0x4cd94</c>, 32 controls, authored 2048x1536 coordinates): the <c>w_big</c>
/// window with the preview frame, the item stats frame, the <c>buyitem</c> list with its scroll bar, the four category
/// buttons and exit. Every element sits at the table's rectangle; the catalogue, selection and purchase logic is the HUD's.
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>Rows of the list that fit the content region (1037,427)-(1727,871) at <see cref="BuyRowHeight"/>.</summary>
	public const int BuyVisibleRows = BuyListModel.VisibleRows;
	// [BIN:TPW-EXE:0x754cf8 buy window table] the list (type 7, 0x1f8) gives the content region (1037,427)-(1727,871); its row height is not in the table: the list class takes it from its first row control (0x663718 stores that control's height measured by 0x65d865 at +0x170; the row under a point is (y - top) / +0x170, 0x66552e)
	// [BIN:TPW-EXE:0x4ad240 buy list row] the buy list's row factory makes each row (0x65bcad) as high as font slot 7's line height (BF4 header byte 5, 0x6b05e0) × 1536 / screen height + 6, computed once (0x7cc200): 42 at 512×384, 41 at 640×480, 39 at 800×600, 34 at 1024×768
	// [DATA:video 17:20 buy list] ten rows with no scrolling: a text pitch of 175 px / 9 = 19.4 px at 720p, 41.4 +- 1 virtual units; the first band starts at about 423 and the tenth ends at about 837, leaving about 34 units of the region empty
	// [EXT:list-row-pitch] the pitch is a fixed 41.4 units (the video's measure; the original's 640×480 pitch is 41), a design choice: the original's pitch follows the font and the screen height (34 to 42 units by the formula above), which OpenTPW's resolution-independent output sizes cannot copy; the wheel steps one row
	public const float BuyRowHeight = 41.4f;
	/// <summary>The side of the i_boxtick model in canvas units (its frames span 664..715 by 51..102).</summary>
	private const float TickBoxSize = 51f;
	// [BIN:TPW-EXE:0x665a44 buy list sort] list 504 has attribute 0x91 including 0x10 (header sort): the same column toggles its direction, a new column starts ascending, and the sort word [0x755140] starts at 1 (column 0, ascending; 0x4ad057 → 0x6659af) and is written back on a change (0x4ac9dd), so the order survives reopening the window for the session (ListSortMemory); the table only gives column header buttons 16/17 without a model

	// [DATA:Mac buy table 0x4cd94:507,506,509,508] category buttons with their help ids (command_17 140, 142, 141, 143)
	private static readonly (BuildCategory Category, string Model, UIStrings Title, int Help, UiRect Rect)[] BuyCategories =
	{
		(BuildCategory.Rides, "b_srides", UIStrings.BuyRide, 140, Rect( 1283, 195, 1386, 297 )),
		(BuildCategory.Sideshows, "b_sshow", UIStrings.BuySideshow, 142, Rect( 1402, 195, 1504, 297 )),
		(BuildCategory.Shops, "b_sshop", UIStrings.BuyShop, 141, Rect( 1521, 195, 1623, 297 )),
		(BuildCategory.Features, "b_sfeature", UIStrings.BuyMiscItems, 143, Rect( 1640, 195, 1742, 297 )),
	};

	// [DATA:Mac buy table 0x4cd94:504] content region and the three columns (command_10/command_11)
	private static readonly UiRect BuyContent = Rect( 1037, 427, 1727, 871 );
	// [DATA:video 1:50 and 30:00 buy window] the third column (1673,1724) holds the i_boxtick model per row; the control 491 is the footprint thumbnail (BuyFootprint)
	private static readonly (float Left, float Right)[] BuyColumns = { (1039, 1447), (1460, 1664), (1673, 1724) };
	// [DATA:Mac buy table 0x4cd94:500,499,497,502,503 and 501,494,498,495,496] label/value rows of the stats frame (493)
	private static readonly (UiRect Label, UiRect Value)[] BuyStatRows =
	{
		(Rect( 283, 634, 679, 679 ), Rect( 722, 634, 957, 679 )),
		(Rect( 283, 685, 679, 730 ), Rect( 722, 685, 957, 730 )),
		(Rect( 283, 736, 679, 781 ), Rect( 722, 736, 957, 781 )),
		(Rect( 283, 787, 679, 832 ), Rect( 722, 787, 957, 832 )),
		(Rect( 283, 839, 679, 883 ), Rect( 722, 839, 957, 883 )),
	};
	private static readonly UiRect BuyScrollTrack = Rect( 1743, 383, 1802, 803 );
	private static readonly UiRect BuyScrollKnob = Rect( 1735, 610, 1802, 677 );

	// [DATA:UITEXT.str:125-133] stats-frame labels per category (the enum names these by their other uses)
	private const UIStrings RideOwned = (UIStrings)125, RideExcitement = (UIStrings)126, RideCapacity = (UIStrings)127, RideReliability = (UIStrings)128, RideLife = (UIStrings)129;
	private const UIStrings ShopOwned = (UIStrings)130, SideshowOwned = (UIStrings)131, SideshowExcitement = (UIStrings)132, FeatureOwned = (UIStrings)133;

	private const string BuyWindowModel = "w_big";
	private const string BuyPanelModel = "!frame";
	private const string BuyListFrameModel = "f_buyitem";
	private const string BuyScrollTrackModel = "!slider";
	private const string BuyUpModel = "b_up";
	private const string BuyDownModel = "b_down";
	private const string BuyScrollKnobModel = "b_scroller";
	private const string BuyExitModel = "b_exit";
	private const string BuyStaffModel = "b_allstaff";

	internal static IEnumerable<string> BuyWindowImagePaths( UiContext context )
	{
		var single = new[] { 0 };
		var buttons = new[] { UiButton.NormalFrame, UiButton.DisabledFrame, UiButton.HighlightFrame, UiButton.DownFrame };
		var models = new[] { BuyWindowModel, BuyPanelModel, BuyListFrameModel, BuyScrollTrackModel }
			.Select( model => (Model: model, Frames: single) )
			.Concat( new[] { BuyUpModel, BuyDownModel, BuyScrollKnobModel, BuyExitModel, BuyStaffModel }
				.Concat( BuyCategories.Select( category => category.Model ) ).Select( model => (Model: model, Frames: buttons) ) );
		return models.SelectMany( entry => context.Models.Get( entry.Model ) is { } model
			? entry.Frames.SelectMany( frame => model.GetFrame( frame ).Parts ) : Enumerable.Empty<UiModelPart>() )
			.Select( context.TextureFor ).Select( texture => texture.ImagePath ).Where( path => path != null )
			.Cast<string>().Distinct( StringComparer.Ordinal );
	}

	private UiScreen? buyScreen;
	private BuyListModel buy = null!;

	public bool BuyWindowOpen => buyScreen != null && Stack.Screens.Contains( buyScreen );
	public BuildCategory Category => buy.Category;
	public BuildItem? SelectedBuyItem => buy.SelectedItem;
	/// <summary>The item whose model, footprint and name the preview frame shows: the selection once it has rested for <see cref="BuyPreviewDelay"/> seconds.</summary>
	public BuildItem? BuyPreviewItem { get; private set; }
	private BuildItem? buyPendingPreview, buyLastSelection;
	private float buyPendingSince;
	/// <summary>How long a selection must rest before the preview object is made (seconds).</summary>
	// [BIN:TPW-EXE:0x4ac42e buy preview delay] the window's update makes the pending preview (0x4ab1b0) only when the clock (0x65968e) is more than 0x1f4 = 500 ms past the moment the selection last changed ([0x7cc1f0], written by 0x4aca2a); until then the old preview keeps turning
	public const float BuyPreviewDelay = 0.5f;
	public int SelectedBuyIndex => buy.SelectedIndex;
	public int BuyFirstRow => buy.FirstRow;
	public BuySortColumn BuySort => buy.Sort;
	public bool BuySortDescending => buy.SortDescending;
	public int BuyMaxFirstRow => buy.MaxFirstRow;
	/// <summary>Items of the current category in list order (by name, as the original list inserts its rows, unless a header sorted them otherwise).</summary>
	public IReadOnlyList<BuildItem> BuyItems => buy.Items;

	/// <summary>Opens the buy window over the park; a pending placement is cancelled.</summary>
	public void OpenBuyWindow()
	{
		if ( BuyWindowOpen || RejectReadOnlyAction() )
			return;
		SetInfoArm( false );
		level.CellTool.Cancel();
		LeaveBuildTool();
		buyScreen ??= CreateBuyScreen();
		buy.EnsureSelection();
		// [BIN:TPW-EXE:0x4acd72 buy window build] the window starts with no preview and no pending one (the ids [0x7cc1e4], [0x7cc1e8] and the time [0x7cc1f0] are set to -1); the first selection arms the preview like any other
		(BuyPreviewItem, buyPendingPreview, buyLastSelection) = (null, null, null);
		Stack.Push( buyScreen );
		buyScreen.Focus( buyScreen.Find( "buyList" ) );
		UpdateBuyScrollVisibility( buyScreen );
	}

	/// <summary>Whether the list's scroll bar and its arrows show: only while the list has more rows than fit.</summary>
	// [BIN:TPW-EXE:0x664495 list scroller] with more items than visible rows the scroller is shown and gets the range 0 .. count - visible and the first row as its value; otherwise it is hidden (0x65d9dd(0)) with the range 0..0
	// [DATA:video 1:50 buy window] four rides and no scroll bar, track or arrows; at 17:20 the longer features list shows them
	public static bool BuyScrollShown( int maxFirstRow ) => maxFirstRow > 0;

	private static readonly string[] BuyScrollIds = { "buyScroll", "scrollUp", "scrollDown" };

	private void UpdateBuyScrollVisibility( UiScreen screen )
	{
		var shown = BuyScrollShown( BuyMaxFirstRow );
		foreach ( var id in BuyScrollIds )
			if ( screen.Find( id ) is { } element )
				element.Visible = shown;
	}

	public void CloseBuyWindow()
	{
		if ( buyScreen == null || !BuyWindowOpen )
			return;
		while ( Stack.Top != buyScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == buyScreen )
			Stack.Pop();
	}

	/// <summary>Follows the selection with the preview: a change arms it, and it is made once the selection has rested for <see cref="BuyPreviewDelay"/> (called once per frame with the UI clock).</summary>
	// [BIN:TPW-EXE:0x4ac9e8 buy list selection] the list's selection message 0x401 (posted by 0x66525c on every change of the selected row) updates the stats frame at once (0x4abb00) and arms the preview when the row's type differs from the pending one ([0x7cc1e4], time [0x7cc1f0]); a selection of nothing (-1) does nothing
	internal void UpdateBuyPreview( float time )
	{
		var selected = buy.SelectedItem;
		if ( selected != null && !Equals( selected, buyLastSelection ) )
		{
			buyLastSelection = selected;
			buyPendingPreview = selected;
			buyPendingSince = time;
		}
		if ( buyPendingPreview != null && time - buyPendingSince > BuyPreviewDelay )
		{
			BuyPreviewItem = buyPendingPreview;
			buyPendingPreview = null;
			// [BIN:TPW-EXE:0x4689f0 preview clip] each new preview object starts its clip from the beginning (0x4732a0); an icon not made yet starts
			// its clip at its first draw
			if ( BuyPreviewModelItem is { } shown && icons?.GetValueOrDefault( shown.Id ) is { } icon )
				icon.StartBuyAnimation( time );
		}
	}

	public void SelectCategory( BuildCategory category ) => buy.SelectCategory( category );
	/// <summary>Selects the list row <paramref name="index"/> (clamped) and scrolls it into view.</summary>
	public void SelectBuyItem( int index ) => buy.Select( index );
	public void ScrollBuyList( int rows ) => buy.Scroll( rows );
	public void SetBuyFirstRow( int first ) => buy.SetFirstRow( first );
	public void SortBuyList( BuySortColumn column ) => buy.SortBy( column );

	/// <summary>Starts placing the selected item (the same checks as <see cref="BeginPlacing"/>); the window closes when placement starts.</summary>
	public void BuySelected()
	{
		if ( buy.SelectedItem is { } item )
			BeginPlacing( item );
	}

	/// <summary>The name a buy list row shows and sorts by: UITEXT 137 for a row that costs golden tickets, else the type's name.</summary>
	// [BIN:TPW-EXE:0x4ab034 buy list row] a type with GoldenTicketCost (+0xc4) above 0 that 0x4d4b70 (the player's bought-with-tickets set) does not hold gets the name 0x485b00(0x89) = UITEXT 137 and the value -cost; any other row its type's name and its price (+4)
	public string ListName( BuildItem item ) => IsMysteryRow( item ) ? strings[UIStrings.MysteryRide] : ItemName( item );

	/// <summary>Whether <paramref name="item"/> is a golden-ticket row the player has not bought yet: the list, the title and the preview show it as the Mystery ride.</summary>
	public bool IsMysteryRow( BuildItem item ) => !IsLandRow( item ) && Status.TicketCostOf( item ) != null;

	/// <summary>The item whose model the preview frame turns: the Mystery ride for an unbought golden-ticket row, so the surprise stays hidden, else the previewed item.</summary>
	// [BIN:TPW-EXE:0x4ab1b0 buy preview] for such a row the preview is the type-100 object, the footprint control 0x1eb is only cleared (0x5f9a80 with colour 0) and the name control 0x1ec gets UITEXT 0x89 (0x485b00); the stats frame (0x4abb00) has no such test and shows the ride's own values
	public BuildItem? BuyPreviewModelItem => BuyPreviewItem is { } item && IsMysteryRow( item ) ? Catalog.MysteryPreview : BuyPreviewItem;

	/// <summary>The price cell of a buy list row.</summary>
	// [BIN:TPW-EXE:0x490650 buy price cell] a negative value (the ticket rows) is formatted with UITEXT 0x1c7 (OpenTPW 454, "%d gt") on its magnitude, a positive one with 0x1c1 "$ " (a magnitude above 0xeeeeeee uses the offset special cases 0x1c1/0x1c2 of the same routine)
	public string PriceText( BuildItem item ) => BuyPrice( item ) < 0
		? TicketPriceText( strings, -BuyPrice( item ) )
		: string.Format( CultureInfo.InvariantCulture, "{0}{1:#,0}", strings[UIStrings.Dollar], BuyPrice( item ) ).Replace( "  ", " " );

	internal static string TicketPriceText( UiStringTable strings, long tickets ) => strings.Format( UiTextRecord.GoldenTicketPrice, tickets.ToString( CultureInfo.InvariantCulture ) );

	public int NumberOwned( BuildItem item ) => level.Objects.Objects.Count( owned => !owned.IsDeleted && owned.Entry.InfoId == item.InfoId );

	/// <summary>TPW buy-list model frame: owned tick wins over the recent-research star; auxiliary tool rows have neither.</summary>
	public int BuyItemState( BuildItem item ) => IsLandRow( item ) ? 0 : NumberOwned( item ) > 0 ? 1
		: Status.EconomySource is ParkEconomy economy && economy.Research.IsRecentlyResearched( (int)item.Category, item.InfoId ) ? 2 : 0;

	/// <summary>The buy window's working-life gauge: the value 100 of 1024 it gets when the window is built (tp.exe 0x4abae0) and keeps.</summary>
	public const float RideWorkingLifeGauge = 100 / 1024f;

	/// <summary>Stat rows of <paramref name="item"/> for its category: the UITEXT labels 125-129 (rides), 130 (shops), 131-132 (sideshows), 133 (features).</summary>
	public IReadOnlyList<BuyStat> BuyStats( BuildItem? item )
	{
		if ( item == null || IsLandRow( item ) )
			return Array.Empty<BuyStat>();
		var owned = NumberOwned( item ).ToString( CultureInfo.InvariantCulture );
		// [DATA:UITEXT.str:125-133] the labels per category (rides list five values, as the five rows of frame 493)
		switch ( item.Category )
		{
			case BuildCategory.Rides:
				return RideBuyStats( item, owned );
			case BuildCategory.Shops:
				return new[] { new BuyStat( ShopOwned, owned, null ) };
			case BuildCategory.Sideshows:
				return new[] { new BuyStat( SideshowOwned, owned, null ), new BuyStat( SideshowExcitement, null, item.DefaultExcitement is { } excitement ? UiGauge.OfPercent( excitement ) : null ) };
			default:
				return new[] { new BuyStat( FeatureOwned, owned, null ) };
		}
	}

	/// <summary>The five stat rows of a ride in the buy window (<c>0x4abb00</c>): owned, excitement, safe capacity, reliability and working life.</summary>
	internal static IReadOnlyList<BuyStat> RideBuyStats( BuildItem item, string owned )
	{
		// [BIN:TPW-EXE:0x4abb00 buy gauges] a gauge value is the record field × 1024 / 100 in integer steps (0x66d885 clamps it to 0..1024), not the exact share
		float? Percent( int? value ) => value is { } known ? UiGauge.OfPercent( known ) : null;
		var baseLevel = item.Entry?.Upgrades.FirstOrDefault( level => level.Level == 0 );
		// [BIN:TPW-EXE:0x4abb00 buy stats] on a selection the rides' row-1 gauge (0x1ee) is the type record's +0x13c (UsageInfo.ExcitementLevel) and the row-3 gauge (0x1ef) its +0x1b0 (Upgrades[0].WearRate), both × 1024 / 100, and the
		// capacity cell (0x1f2) its +0x19c (Upgrades[0].RedLineCapacity) as "%d"; the record offsets are the key table's (0x7456e8, 60-byte records: UsageInfo key 30 at +0x13c, the Upgrades keys 57..65 from +0x198 in steps of 4: InitCapacity, RedLineCapacity +0x19c, InitDuration, RedLineDuration, InitSpeed, RedLineSpeed +0x1ac, WearRate +0x1b0, QueueWaitTimeConstant, CostOfUpgrade +0x1b8)
		// the working-life gauge (0x1f0) is only set when the window is built, to 100 of the gauge's 1024 (0x4abae0), and keeps that for every ride
		// [DATA:video 1:50 buy window] Levensduur shows a short red stub (about a tenth) and Betrouwbaarheid an even shorter one: WearRate 5 is 51 of 1024, the working life 100
		return new[]
		{
			new BuyStat( RideOwned, owned, null ),
			new BuyStat( RideExcitement, null, Percent( item.DefaultExcitement ) ),
			new BuyStat( RideCapacity, (baseLevel?.GetInt( "RedLineCapacity" ) ?? 0).ToString( CultureInfo.InvariantCulture ), null ),
			new BuyStat( RideReliability, null, Percent( baseLevel?.GetInt( "WearRate" ) ) ),
			new BuyStat( RideLife, null, RideWorkingLifeGauge ),
		};
	}

	private UiScreen CreateBuyScreen()
	{
		var screen = new BuyScreen( this );
		void Model( string id, string model, UiRect rect ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = rect, Anchor = UiAnchor.Center } );
		// [DATA:Mac buy table 0x4cd94:489,490,493,504] w_big root window4, two !frame panels and the f_buyitem list frame at their authored rectangles
		Model( "window", BuyWindowModel, Rect( 186, 30, 2018, 1007 ) );
		Model( "previewFrame", BuyPanelModel, Rect( 408, 179, 822, 593 ) );
		Model( "statsFrame", BuyPanelModel, Rect( 259, 621, 971, 902 ) );
		Model( "listFrame", BuyListFrameModel, Rect( 1009, 179, 1813, 902 ) );

		// [DATA:Mac buy table 0x4cd94:510] title text; the category's UITEXT 119-122
		screen.Add( new UiLabel
		{
			Id = "title", Text = () => strings[BuyCategories.First( entry => entry.Category == buy.Category ).Title], Font = fonts => fonts.Title, Color = UiColors.Title,
			Align = UiAlign.Center, Fit = true, Bounds = Rect( 805, 74, 1277, 153 ), Anchor = UiAnchor.Center
		} );
		// [BIN:TPW-EXE:0x4acdae buy window control 512] a type-1 label at (1297,75)-(1769,154) (table record 0x755114) shows the park's cash balance (0x7cf83c -> 0x5195d0 -> 0x6ad810, the HUD cash accessor) as UITEXT 0x1ca "Cash $ N" (0x4ace20), or 0x1cb with the magnitude when it is negative (0x4acdec); 0x4ac2fa refreshes it. The same label text as the hire window's control 9366.
		screen.Add( new UiLabel { Id = "buyCash", Text = () => BuyCashText, Font = fonts => fonts.Label, Color = UiColors.Value,
			Align = UiAlign.Right, Fit = true, Bounds = Rect( 1297, 75, 1769, 154 ), Anchor = UiAnchor.Center } );
		// [DATA:Mac buy table 0x4cd94:492] item name line at the top of the preview frame
		screen.Add( new UiLabel
		{
			Id = "itemName", Text = () => BuyPreviewItem == null ? "" : ListName( BuyPreviewItem ), Font = fonts => fonts.Label, Color = UiColors.Text,
			Align = UiAlign.Center, Fit = true, Bounds = Rect( 437, 198, 797, 243 ), Anchor = UiAnchor.Center
		} );
		// [DATA:Mac buy table 0x4cd94:490] preview content region (437,202)-(797,565)
		screen.Add( new BuyPreview( this ) { Id = "preview", Bounds = Rect( 437, 202, 797, 565 ), Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x754d50 buy control 491] the footprint thumbnail (control 0x1eb, 0x4ab1b0) at (440,407)-(594,561), over the preview frame's lower left
		screen.Add( new BuyFootprint( this ) { Id = "footprint", Bounds = Rect( 440, 407, 594, 561 ), Anchor = UiAnchor.Center } );

		for ( var row = 0; row < BuyStatRows.Length; row++ )
		{
			var index = row;
			screen.Add( new UiLabel
			{
				Id = $"statLabel{index}", Text = () => index < BuyStats( buy.SelectedItem ).Count ? strings[BuyStats( buy.SelectedItem )[index].Label] : "",
				Font = fonts => fonts.Label, Color = UiColors.Text, Fit = true, Bounds = BuyStatRows[index].Label, Anchor = UiAnchor.Center
			} );
			screen.Add( new BuyStatValue( this, index ) { Id = $"statValue{index}", Bounds = BuyStatRows[index].Value, Anchor = UiAnchor.Center } );
		}

		buyList = screen.Add( new BuyItemList( this ) { Id = "buyList", Help = strings.Help( 151 ), Bounds = Rect( 1009, 179, 1813, 902 ), Anchor = UiAnchor.Center } );
		var scrollBar = screen.Add( new BuyScrollBar( this ) { Id = "buyScroll", Bounds = Rect( 1735, 383, 1802, 803 ), Anchor = UiAnchor.Center } );
		_ = scrollBar;
		// [DATA:Mac buy table 0x4cd94:16,17] column header buttons (name, price) without a model; the third (18) has no text and no action
		screen.Add( new BuyHeader( this, BuySortColumn.Name, () => strings[UIStrings.NameAttr] ) { Id = "headerName", Bounds = Rect( 1032, 317, 1454, 422 ), Anchor = UiAnchor.Center } );
		screen.Add( new BuyHeader( this, BuySortColumn.Price, () => strings[UIStrings.Price] ) { Id = "headerPrice", Bounds = Rect( 1458, 317, 1664, 422 ), Anchor = UiAnchor.Center } );
		// [DATA:Mac buy table 0x4cd94:1,2] b_up / b_down at their rectangles. The buy table 0x754cf8 is not generated; its list is type 7 style 0x91 and its slider is type 3 style 0x10, so the arrows and the headers play cat_ui 0xBD (parent style bit 0x10, hook 0x485780 at 0x4857cb). A held arrow repeats (500 ms, then 125 ms) and the release after that plays nothing.
		void ScrollArrow( string id, string model, UiRect bounds, int step )
		{
			void Scroll() => ScrollBuyList( step );
			screen.Add( new UiButton { Id = id, Model = model, ClickSound = GameAudio.SelectClickEvent, Clicked = Scroll,
				Repeat = UiButton.ScrollArrowRepeat( Scroll ), Bounds = bounds, Anchor = UiAnchor.Center } );
		}
		ScrollArrow( "scrollUp", BuyUpModel, Rect( 1742, 318, 1803, 378 ), -1 );
		ScrollArrow( "scrollDown", BuyDownModel, Rect( 1742, 809, 1803, 869 ), 1 );
		foreach ( var entry in BuyCategories )
		{
			var category = entry;
			// The four category buttons are the radio group (type 8, style 0x11) of table 0x754cf8, so a press plays 0xBD even when the tab is already selected.
			screen.Add( new UiButton
			{
				Id = $"category{category.Category}", Model = category.Model, Help = strings.Help( category.Help ), ClickSound = GameAudio.SelectClickEvent,
				Clicked = () => SelectCategory( category.Category ), Selected = () => buy.Category == category.Category, Bounds = category.Rect, Anchor = UiAnchor.Center
			} );
		}
		// [DATA:Mac buy table 0x4cd94:-2,511] b_exit (help 2) closes; b_allstaff (help 153) opens the staff window.
		screen.Add( new UiButton { Id = "exit", Model = BuyExitModel, Help = strings.Help( 2 ), Clicked = CloseBuyWindow, Bounds = Rect( 1889, 818, 1972, 901 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "allStaff", Model = BuyStaffModel, Help = strings.Help( 153 ), Clicked = OpenStaffWindow, Bounds = Rect( 1866, 583, 1968, 686 ), Anchor = UiAnchor.Center } );
		// [EXT:overview-entry-buttons] a round button above b_allstaff opens the All Attractions window (its rides tab,
		// UITEXT 78 'All Rides', shown as its popup help); the shipped b_allthinginfo button art cannot be used — its model's
		// texture 'b_allthginfo' is absent from ui.wad — so it wears the window's own rides tab icon b_srides, round like its
		// neighbours (a text label does not fit a button this narrow). The original entry point is not decoded.
		// The rectangle ends above b_allstaff's top edge so no click band of the two buttons overlaps.
		screen.Add( new UiButton { Id = "allAttractions", Model = AllAttractionsModel, Help = strings[UIStrings.AllRides],
			Clicked = OpenAttractionsWindow, Bounds = Rect( 1866, 458, 1968, 577 ), Anchor = UiAnchor.Center } );
		screen.Back = CloseBuyWindow;
		screen.Updating = _ => UpdateBuyScrollVisibility( screen );
		return screen;
	}

	private BuyItemList? buyList;

	/// <summary>The buy window screen: the mouse wheel anywhere over it scrolls the list.</summary>
	private sealed class BuyScreen : UiScreen
	{
		private readonly ParkHud owner;

		public BuyScreen( ParkHud owner ) : base( "buy" ) => this.owner = owner;

		public override bool Update( UiContext context, UiInput input )
		{
			if ( input.Wheel != 0 )
			{
				owner.ScrollBuyList( input.Wheel > 0 ? -1 : 1 );
				input = input with { Wheel = 0 };
			}
			var consumed = base.Update( context, input );
			owner.UpdateBuyPreview( context.Time );
			return consumed;
		}
	}

	/// <summary>The turning model preview of the selected item.</summary>
	private sealed class BuyPreview : UiElement
	{
		private readonly ParkHud owner;
		public BuyPreview( ParkHud owner ) => this.owner = owner;

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			if ( owner.BuyPreviewModelItem is not { } item )
				return;
			// [BIN:TPW-EXE:0x468e50 buy preview turn] each update adds the clock's elapsed milliseconds (0x785970, 0x4031e0) x 0.0004 to the preview's angle (+0x48), and a
			// new preview keeps the angle modulo 2 pi (0x468e2f), so the turn runs on across selections, as a clock-derived angle does
			// [BIN:TPW-EXE:0x4ab1b0 buy preview] a selection makes the preview object (0x486410) in the inner rectangle (+0x2c, 0x65d4f6) of the preview frame 0x1ea, (437,202)-(797,565)
			owner.GetIcon( item )?.DrawBuyPreview( context.Batch, ScreenRect( context.Canvas ), owner.DebugBuyYaw ?? context.Time * PreviewIcon.BuyTurnRate, context.Canvas.Scale, context.Time );
		}
	}

	/// <summary>A value cell of the stats frame: a number, or a gauge bar.</summary>
	private sealed class BuyStatValue : UiElement
	{
		private readonly ParkHud owner;
		private readonly int row;
		public BuyStatValue( ParkHud owner, int row ) { this.owner = owner; this.row = row; }

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var stats = owner.BuyStats( owner.buy.SelectedItem );
			if ( row >= stats.Count )
				return;
			var stat = stats[row];
			var rect = ScreenRect( context.Canvas );
			if ( stat.Text != null )
				context.DrawFittedText( context.Fonts.Label, stat.Text, rect, UiColors.Value, UiAlign.Right );
			var gaugeRow = owner.buy.SelectedItem?.Category == BuildCategory.Rides ? row is 1 or 3 or 4 : row == 1;
			if ( !gaugeRow )
				return;
			// [DATA:video 1:50 buy window] the type-9 value cells are the ridestatbar.wct gauges (UiGauge); the bar fills the cell less 10 units top and bottom (0x4ab9dd)
			UiGauge.Draw( context.Batch, UiGauge.BarRect( rect, context.Canvas ), stat.Gauge );
		}
	}

	/// <summary>A column header: its text, sorting the list when clicked.</summary>
	private sealed class BuyHeader : UiElement
	{
		private readonly ParkHud owner;
		private readonly BuySortColumn column;
		private readonly Func<string> text;

		public BuyHeader( ParkHud owner, BuySortColumn column, Func<string> text )
		{
			this.owner = owner;
			this.column = column;
			this.text = text;
		}

		public override bool Focusable => Visible && Enabled;
		// The header is a child of the sortable list (type 7, style 0x91). That parent receives 0x100, so the hook plays 0xBD (0x4857cb).
		public override void Activate()
		{
			GameAudio.PlayUi( GameAudio.SelectClickEvent );
			owner.SortBuyList( column );
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			// The header shows only its text; the original's sort state has no known marker.
			context.DrawFittedText( context.Fonts.Label, text(), ScreenRect( context.Canvas ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
		}
	}

	/// <summary>The scroll ball's authored rectangle for the first visible row <paramref name="firstRow"/> of <paramref name="maxFirstRow"/>.</summary>
	// [BIN:TPW-EXE:0x66b088 slider ball] a vertical slider puts its ball (child 3) at top + (min - value) × (height - ball height) / (min - max) in integer table units, so with
	// the list's range 0 .. count - visible (0x664495) the ball's top runs from the track's top for the first row to a ball height above its bottom for the last
	internal static UiRect BuyScrollKnobRect( int firstRow, int maxFirstRow ) => ListScrollKnobRect( BuyScrollTrack, BuyScrollKnob, firstRow, maxFirstRow );

	/// <summary>The ball rectangle of a list's vertical slider on <paramref name="track"/> (the same 0x66b088 rule for every list).</summary>
	internal static UiRect ListScrollKnobRect( UiRect track, UiRect knob, int firstRow, int maxFirstRow )
	{
		var travel = (int)track.Height - (int)knob.Height;
		var top = (int)track.Y + (maxFirstRow == 0 ? 0 : travel * firstRow / maxFirstRow);
		return new UiRect( knob.X, top, knob.Width, knob.Height );
	}

	/// <summary>The vertical scroll bar: the <c>!slider</c> track with the <c>b_scroller</c> ball; dragging or clicking sets the first visible row.</summary>
	private sealed class BuyScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public BuyScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;

		/// <summary>Authored ball rectangle for the current first row: its top runs from the track's top to a ball above the track's bottom.</summary>
		public UiRect KnobRect() => BuyScrollKnobRect( owner.buy.FirstRow, owner.BuyMaxFirstRow );

		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			if ( rect.Height <= 0 || owner.BuyMaxFirstRow == 0 )
				return;
			var authoredY = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var travel = BuyScrollTrack.Height - BuyScrollKnob.Height;
			var fraction = Math.Clamp( (authoredY - BuyScrollTrack.Y - BuyScrollKnob.Height / 2) / travel, 0, 1 );
			owner.SetBuyFirstRow( (int)MathF.Round( fraction * owner.BuyMaxFirstRow ) );
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( BuyScrollTrackModel, 0, context.Canvas.Map( BuyScrollTrack, Anchor ) );
			context.DrawModel( BuyScrollKnobModel, owner.BuyMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame, context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}

	/// <summary>The three-column item list inside the <c>f_buyitem</c> frame.</summary>
	private sealed class BuyItemList : UiElement, IUiRowList
	{
		private readonly ParkHud owner;
		public BuyItemList( ParkHud owner ) => this.owner = owner;

		public override bool Focusable => Visible && Enabled;
		public int Selected => owner.SelectedBuyIndex;
		public bool ArrowsSelect => true;

		public void Select( int index ) => owner.SelectBuyItem( index );
		public void Scroll( int rows ) => owner.ScrollBuyList( rows );
		public override void Activate() => owner.BuySelected();

		public bool HoverSelects => true;

		/// <summary>The pointer is over the row <paramref name="index"/> (-1: off the rows); a pointer move onto a row selects it.</summary>
		// [BIN:TPW-EXE:0x6656a0 list hover] the list has attribute 0x91, which includes 0x80: every pointer move over the list (messages 0x10001 and 0x10003, 0x665c35) selects the row under it (0x66552e → 0x66525c: the row index is (y - top) / row height; a row past the last one leaves the selection, as attribute 0x100 is not set), the same selection as a click or an arrow key, so the stats frame follows at once and the preview after 500 ms
		// [BIN:TPW-EXE:0x665c35 list leave] the list handles no message for the pointer leaving (0x10002 goes to the base control, 0x65f6d1, which only informs the pointer manager), so the selection stays on the last hovered row
		// [BIN:TPW-EXE:0x658840 pointer move] the messages are sent only when the pointer position changes (UiScreen), so the arrow keys and the wheel keep their selection under a resting pointer, and any pointer move, also within the same row, selects the row under it again
		public void Hover( int index, bool pointerMoved )
		{
			if ( pointerMoved && index >= 0 )
				owner.SelectBuyItem( index );
		}

		/// <summary>A left click selects the row and posts the list's message 0x400, which buys; the other button posts 0x402, which the window ignores, so it backs out.</summary>
		// [BIN:TPW-EXE:0x6655a2 list click] with attribute 0x80 a press selects the row (0x66552e) and posts 0x400 to the window at once (-1 < selected), and the release posts it again (0x6656fa); the buy window's handler for 0x400 (0x4ac4f9) is the purchase: cash and golden ticket checks, then the placing of the type and the window's close (0x658d9f, message 4)
		// [BIN:TPW-EXE:0x66563d list click] the other button posts 0x402, which the buy window does not handle (0x4ac9c9 handles 0x401 and 0x407)
		public bool RowClicked( int index, bool left )
		{
			if ( !left )
				return false;
			owner.SelectBuyItem( index );
			owner.BuySelected();
			return true;
		}
		public override void Adjust( int direction ) => owner.SelectBuyItem( Math.Max( 0, owner.SelectedBuyIndex ) + direction );

		private UiRect RowRect( UiCanvas canvas, int visibleIndex ) =>
			canvas.Map( new UiRect( BuyContent.X, BuyContent.Y + visibleIndex * BuyRowHeight, BuyContent.Width, BuyRowHeight ), Anchor );

		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			var content = canvas.Map( BuyContent, Anchor );
			if ( !content.Contains( point ) )
				return -1;
			var visible = (int)MathF.Floor( (point.Y - content.Y) / (BuyRowHeight * canvas.Scale) );
			var index = owner.buy.FirstRow + visible;
			return visible < 0 || visible >= BuyVisibleRows || index >= owner.BuyItems.Count ? -1 : index;
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var canvas = context.Canvas;
			var items = owner.BuyItems;
			var selected = owner.SelectedBuyIndex;
			for ( var visible = 0; visible < BuyVisibleRows && owner.buy.FirstRow + visible < items.Count; visible++ )
			{
				var index = owner.buy.FirstRow + visible;
				var item = items[index];
				var row = RowRect( canvas, visible );
				var available = owner.IsBuyAvailable( item );
				if ( index == selected )
					context.Batch.AddRectangle( row, new RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				var color = index == selected ? UiColors.Highlight : !available ? UiColors.Disabled : UiColors.Text;
				UiRect Cell( int column )
				{
					var (left, right) = BuyColumns[column];
					var origin = canvas.Map( new NVector2( left, BuyContent.Y + visible * BuyRowHeight ), Anchor );
					return new UiRect( origin.X, origin.Y, (right - left) * canvas.Scale, BuyRowHeight * canvas.Scale );
				}
				context.DrawFittedText( context.Fonts.Label, owner.ListName( item ), Cell( 0 ), color, UiAlign.Left );
				context.DrawFittedText( context.Fonts.Label, owner.PriceText( item ), Cell( 1 ), color, UiAlign.Right );
				// The numeric third cell selects frame n directly: 0 empty, 1 owned tick, 2 recent-research star.
				// Ownership masks history without clearing it. Keep the model's own 51-unit size.
				var boxCell = Cell( 2 );
				var box = TickBoxSize * canvas.Scale;
				context.DrawModel( "i_boxtick", owner.BuyItemState( item ), new UiRect( boxCell.X + (boxCell.Width - box) / 2, boxCell.Y + (boxCell.Height - box) / 2, box, box ) );
			}
		}
	}
}
