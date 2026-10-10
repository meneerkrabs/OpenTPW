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
	// [APPROX:UI-043] row height: the table gives the content region (444 units high), not the row pitch; ten equal rows fill it — evidence needed: capture of the original buy list
	public const float BuyRowHeight = 444f / BuyVisibleRows;
	// [APPROX:UI-044] a header click sorts by that column (ascending, again for descending); the table only gives column header buttons 16/17 without a model — evidence needed: the original header handler

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
	// [APPROX:UI-049] the third column (1673,1724) has no known content and stays empty; the unnamed controls 491 and 512 are not drawn — evidence needed: capture of the original buy window
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

	private UiScreen? buyScreen;
	private BuyListModel buy = null!;

	public bool BuyWindowOpen => buyScreen != null && Stack.Screens.Contains( buyScreen );
	public BuildCategory Category => buy.Category;
	public BuildItem? SelectedBuyItem => buy.SelectedItem;
	public int SelectedBuyIndex => buy.SelectedIndex;
	public int BuyFirstRow => buy.FirstRow;
	public BuySortColumn BuySort => buy.Sort;
	public bool BuySortDescending => buy.SortDescending;
	public int BuyMaxFirstRow => buy.MaxFirstRow;
	/// <summary>Items of the current category in list order (catalogue order by Info.Id unless a header sorted them).</summary>
	public IReadOnlyList<BuildItem> BuyItems => buy.Items;

	/// <summary>Opens the buy window over the park; a pending placement is cancelled.</summary>
	public void OpenBuyWindow()
	{
		if ( BuyWindowOpen || RejectReadOnlyAction() )
			return;
		SetInfoArm( false );
		if ( pendingItem != null || level.BuildEntry != null )
		{
			pendingItem = null;
			level.IsPlacing = false;
			level.BuildEntry = null;
		}
		buyScreen ??= CreateBuyScreen();
		buy.EnsureSelection();
		Stack.Push( buyScreen );
		buyScreen.Focus( buyScreen.Find( "buyList" ) );
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

	public string PriceText( BuildItem item ) =>
		string.Format( CultureInfo.InvariantCulture, "{0}{1:#,0}", strings[UIStrings.Dollar], Status.PriceOf( item ) ?? item.Cost ).Replace( "  ", " " );

	public int NumberOwned( BuildItem item ) => level.Objects.Objects.Count( owned => !owned.IsDeleted && owned.Entry.InfoId == item.InfoId );

	/// <summary>Stat rows of <paramref name="item"/> for its category: the UITEXT labels 125-129 (rides), 130 (shops), 131-132 (sideshows), 133 (features).</summary>
	public IReadOnlyList<BuyStat> BuyStats( BuildItem? item )
	{
		if ( item == null )
			return Array.Empty<BuyStat>();
		var owned = NumberOwned( item ).ToString( CultureInfo.InvariantCulture );
		// [DATA:UITEXT.str:125-133] the labels per category (rides list five values, as the five rows of frame 493)
		float? Percent( int? value ) => value is { } known ? Math.Clamp( known / 100f, 0, 1 ) : null;
		switch ( item.Category )
		{
			case BuildCategory.Rides:
				var capacity = item.Entry?.InitialCapacity ?? 0;
				// [APPROX:UI-045] gauge rows: excitement is UsageInfo.ExcitementLevel as a share of 100, reliability is 1 - Upgrades[0].WearRate / 10 (wear rate is 'out of 10'), working life has no catalogue value and stays empty; safe capacity is Upgrades[0].InitCapacity — evidence needed: capture of the original buy window
				float? reliability = item.Entry?.Upgrades.FirstOrDefault( level => level.Level == 0 ) is { } baseLevel ? Math.Clamp( 1 - baseLevel.GetInt( "WearRate" ) / 10f, 0, 1 ) : null;
				return new[]
				{
					new BuyStat( RideOwned, owned, null ),
					new BuyStat( RideExcitement, null, Percent( item.DefaultExcitement ) ),
					new BuyStat( RideCapacity, capacity > 0 ? capacity.ToString( CultureInfo.InvariantCulture ) : null, null ),
					new BuyStat( RideReliability, null, reliability ),
					new BuyStat( RideLife, null, null ),
				};
			case BuildCategory.Shops:
				return new[] { new BuyStat( ShopOwned, owned, null ) };
			case BuildCategory.Sideshows:
				return new[] { new BuyStat( SideshowOwned, owned, null ), new BuyStat( SideshowExcitement, null, Percent( item.DefaultExcitement ) ) };
			default:
				return new[] { new BuyStat( FeatureOwned, owned, null ) };
		}
	}

	private UiScreen CreateBuyScreen()
	{
		var screen = new BuyScreen( this );
		void Model( string id, string model, UiRect rect ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = rect, Anchor = UiAnchor.Center } );
		// [DATA:Mac buy table 0x4cd94:489,490,493,504] w_big root window4, two !frame panels and the f_buyitem list frame at their authored rectangles
		Model( "window", "w_big", Rect( 186, 30, 2018, 1007 ) );
		Model( "previewFrame", "!frame", Rect( 408, 179, 822, 593 ) );
		Model( "statsFrame", "!frame", Rect( 259, 621, 971, 902 ) );
		Model( "listFrame", "f_buyitem", Rect( 1009, 179, 1813, 902 ) );

		// [DATA:Mac buy table 0x4cd94:510] title text; the category's UITEXT 119-122 (control 512 beside it is unnamed and stays empty, [APPROX:UI-047])
		screen.Add( new UiLabel
		{
			Id = "title", Text = () => strings[BuyCategories.First( entry => entry.Category == buy.Category ).Title], Font = fonts => fonts.Title, Color = UiColors.Title,
			Align = UiAlign.Center, Fit = true, Bounds = Rect( 805, 74, 1277, 153 ), Anchor = UiAnchor.Center
		} );
		// [DATA:Mac buy table 0x4cd94:492] item name line at the top of the preview frame
		screen.Add( new UiLabel
		{
			Id = "itemName", Text = () => buy.SelectedItem == null ? "" : ItemName( buy.SelectedItem ), Font = fonts => fonts.Label, Color = UiColors.Text,
			Align = UiAlign.Center, Fit = true, Bounds = Rect( 437, 198, 797, 243 ), Anchor = UiAnchor.Center
		} );
		// [DATA:Mac buy table 0x4cd94:490] preview content region (437,202)-(797,565)
		screen.Add( new BuyPreview( this ) { Id = "preview", Bounds = Rect( 437, 202, 797, 565 ), Anchor = UiAnchor.Center } );

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
		// [DATA:Mac buy table 0x4cd94:1,2] b_up / b_down at their rectangles
		screen.Add( new UiButton { Id = "scrollUp", Model = "b_up", Clicked = () => ScrollBuyList( -1 ), Bounds = Rect( 1742, 318, 1803, 378 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "scrollDown", Model = "b_down", Clicked = () => ScrollBuyList( 1 ), Bounds = Rect( 1742, 809, 1803, 869 ), Anchor = UiAnchor.Center } );
		foreach ( var entry in BuyCategories )
		{
			var category = entry;
			screen.Add( new UiButton
			{
				Id = $"category{category.Category}", Model = category.Model, Help = strings.Help( category.Help ), Clicked = () => SelectCategory( category.Category ),
				Selected = () => buy.Category == category.Category, Bounds = category.Rect, Anchor = UiAnchor.Center
			} );
		}
		// [DATA:Mac buy table 0x4cd94:-2,511] b_exit (help 2) closes; b_allstaff (help 153) opens the staff list, which OpenTPW does not have yet
		screen.Add( new UiButton { Id = "exit", Model = "b_exit", Help = strings.Help( 2 ), Clicked = CloseBuyWindow, Bounds = Rect( 1889, 818, 1972, 901 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "allStaff", Model = "b_allstaff", Help = strings.Help( 153 ), Enabled = false, Bounds = Rect( 1866, 583, 1968, 686 ), Anchor = UiAnchor.Center } );
		screen.Back = CloseBuyWindow;
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
			return base.Update( context, input );
		}
	}

	/// <summary>The turning model preview of the selected item.</summary>
	private sealed class BuyPreview : UiElement
	{
		private readonly ParkHud owner;
		public BuyPreview( ParkHud owner ) => this.owner = owner;

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			if ( owner.buy.SelectedItem is not { } item )
				return;
			var rect = ScreenRect( context.Canvas );
			var size = Math.Min( rect.Width, rect.Height );
			var square = new UiRect( rect.X + (rect.Width - size) / 2, rect.Y + (rect.Height - size) / 2, size, size ).Inflate( -size * 0.06f );
			// [APPROX:UI-026] icon turn speed 0.8 rad/s — evidence needed: capture of the original build menu
			owner.GetIcon( item )?.Draw( context.Batch, square, context.Time * 0.8f );
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
			// [APPROX:UI-045] the type-9 value cells are drawn as a plain bar gauge; the original gauge art is not identified
			var track = rect.Inflate( -rect.Height * 0.2f );
			context.Batch.AddRectangle( track, new RgbaByte( 16, 24, 60, 200 ) );
			if ( stat.Gauge is { } fraction )
				context.Batch.AddRectangle( track with { Width = track.Width * fraction }, UiColors.Value );
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
		public override void Activate() => owner.SortBuyList( column );

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rect = ScreenRect( context.Canvas );
			var text = this.text();
			if ( owner.buy.Sort == column )
				text += owner.buy.SortDescending ? " -" : " +";
			context.DrawFittedText( context.Fonts.Label, text, rect, focused || owner.buy.Sort == column ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
		}
	}

	/// <summary>The vertical scroll bar: the <c>!slider</c> track with the <c>b_scroller</c> ball; dragging or clicking sets the first visible row.</summary>
	private sealed class BuyScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public BuyScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;

		/// <summary>Authored ball rectangle for the current first row: its top runs from the track's top to a ball above the track's bottom.</summary>
		// [APPROX:UI-048] ball travel: top at the track top for the first row, a ball height above the track bottom for the last — evidence needed: capture of the original scroll bar ends
		public UiRect KnobRect()
		{
			var travel = BuyScrollTrack.Height - BuyScrollKnob.Height;
			var max = owner.BuyMaxFirstRow;
			var top = BuyScrollTrack.Y + (max == 0 ? 0 : travel * owner.buy.FirstRow / max);
			return new UiRect( BuyScrollKnob.X, top, BuyScrollKnob.Width, BuyScrollKnob.Height );
		}

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
			context.DrawModel( "!slider", 0, context.Canvas.Map( BuyScrollTrack, Anchor ) );
			context.DrawModel( "b_scroller", owner.BuyMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame, context.Canvas.Map( KnobRect(), Anchor ) );
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
				var available = owner.Status.IsAvailable( item );
				if ( index == selected )
					context.Batch.AddRectangle( row, new RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				var color = index == selected ? UiColors.Highlight : !available ? UiColors.Disabled : UiColors.Text;
				UiRect Cell( int column )
				{
					var (left, right) = BuyColumns[column];
					var origin = canvas.Map( new NVector2( left, BuyContent.Y + visible * BuyRowHeight ), Anchor );
					return new UiRect( origin.X, origin.Y, (right - left) * canvas.Scale, BuyRowHeight * canvas.Scale );
				}
				context.DrawFittedText( context.Fonts.Label, owner.ItemName( item ), Cell( 0 ), color, UiAlign.Left );
				context.DrawFittedText( context.Fonts.Label, owner.PriceText( item ), Cell( 1 ), color, UiAlign.Right );
			}
		}
	}
}
