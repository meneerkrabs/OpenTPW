using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The All Attractions window: the park's placed attractions in a live list with the original
/// category tabs (<c>b_srides</c>/<c>b_sshow</c>/<c>b_sshop</c>/<c>b_sfeature</c>, the buy window's
/// tab art) and the original per-category statistic labels (UITEXT 78-97) and status strings
/// (UITEXT 364-395). No original layout table is decoded for this window, so its placement follows
/// the proven staff/buy window geometry; the window itself is an original feature (the key table's
/// All Attractions key A, which the host camera's WASD pan currently owns).
/// </summary>
// [APPROX:UI-905] window layout, list columns and detail placement follow the staff/buy table geometry; the decoded tables (attractions 0x7508e0, per-category lists 0x750ab8/0x750ba0/0x750ca0, list id 400 at [266,318,1825,876]) are not ported, their list behaviour (attribute 0x291) is — evidence needed: porting them
public sealed partial class ParkHud
{
	private static readonly UiRect AttractionContent = Rect( 1043, 441, 1729, 911 );
	private static readonly UiRect AttractionScrollTrack = Rect( 1743, 398, 1802, 843 );
	private static readonly UiRect AttractionScrollKnob = Rect( 1735, 651, 1802, 718 );
	// [DATA:UITEXT.str:78-81] the four category titles of this window; the tab art is the buy window's.
	private static readonly (BuildCategory Category, string Model, UIStrings Title, UiRect Rect)[] AttractionCategories =
	{
		(BuildCategory.Rides, "b_srides", UIStrings.AllRides, Rect( 1283, 195, 1386, 297 )),
		(BuildCategory.Sideshows, "b_sshow", UIStrings.AllSideshows, Rect( 1402, 195, 1504, 297 )),
		(BuildCategory.Shops, "b_sshop", UIStrings.AllShops, Rect( 1521, 195, 1623, 297 )),
		(BuildCategory.Features, "b_sfeature", UIStrings.AllMiscItems, Rect( 1640, 195, 1742, 297 )),
	};
	// [EXT:overview-detail-rows] seven equal detail rows fill the stats frame's area (the sideshow category has
	// five original rows plus the two live queue rows) — evidence needed: the original detail layout.
	private const int AttractionDetailRowCount = 7;

	private UiScreen? attractionScreen;
	private UiButton? deleteAttractionButton;
	private BuildCategory attractionCategory = BuildCategory.Rides;
	/// <summary>The sort direction of the current category tab, remembered for the session (UI-084).</summary>
	private bool attractionSortDescending
	{
		get => Sorts.AttractionDescending[(int)attractionCategory];
		set => Sorts.AttractionDescending[(int)attractionCategory] = value;
	}
	private int selectedAttractionId;
	private int attractionFirstRow;

	public bool AttractionsWindowOpen => attractionScreen != null && Stack.Screens.Contains( attractionScreen );
	public int SelectedAttractionId => SelectedAttractionRow.AttractionId;
	public int AttractionFirstRow => attractionFirstRow;
	/// <summary>Live rows of the selected category in list order (by name), the developer prototype Totem included.</summary>
	// [EXT:developer-prototype] the prototype ride lives beside the object world; Level.ListAttractions is where both sources merge
	public IReadOnlyList<AttractionListRow> AttractionItems =>
		ParkAttractionRoster.Rows( level.ListAttractions().Select( AttractionRow ), attractionCategory, attractionSortDescending );

	/// <summary>
	/// The window's rows for one frame: the screen refreshes them when it updates and every action that
	/// changes the category or order refreshes them again, so the labels, list and scrollbar share one
	/// snapshot instead of re-projecting the park's objects for every draw call.
	/// <see cref="AttractionItems"/> stays live for callers outside the frame.
	/// </summary>
	private IReadOnlyList<AttractionListRow> attractionRows = Array.Empty<AttractionListRow>();
	private readonly Dictionary<int, AttractionBusinessStats> attractionBusinessStats = new();
	private readonly record struct AttractionBusinessStats( long CustomersLastMonth, long ProfitLastMonth, long TotalProfit );

	private IReadOnlyList<AttractionListRow> RefreshAttractionRows()
	{
		attractionBusinessStats.Clear();
		var finance = FinanceEconomy;
		var economy = GameEditions.Current == GameEdition.ThemeParkWorld && !level.IsReadOnlyVisit
			&& ReferenceEquals( finance, level.Park?.Economy ) ? finance : null;
		AttractionListRow Project( OriginalObject item )
		{
			var row = AttractionRow( item );
			if ( economy != null && row.Category == attractionCategory && item is not PrototypeRide && level.GetEconomyInstance( item ) is int instance
				&& economy.TryGetObject( instance, out var state ) && state.InfoId == item.Entry.InfoId
				&& (row.Category == BuildCategory.Shops && state.Kind == ParkObjectKind.Shop
					|| row.Category == BuildCategory.Sideshows && state.Kind == ParkObjectKind.Sideshow) )
				attractionBusinessStats[row.AttractionId] = new( state.CustomersLastMonth, state.ProfitLastMonth, state.TotalProfit );
			return row;
		}
		return attractionRows = ParkAttractionRoster.Rows( level.ListAttractions().Select( Project ), attractionCategory, attractionSortDescending );
	}
	private AttractionListRow SelectedAttractionRow => attractionRows.FirstOrDefault( row => row.AttractionId == selectedAttractionId );
	private int AttractionMaxFirstRow => ParkAttractionRoster.MaxFirstRow( attractionRows.Count, StaffVisibleRows );
	private int AttractionRowIndex( int id )
	{
		var rows = attractionRows;
		for ( var index = 0; index < rows.Count; index++ )
			if ( rows[index].AttractionId == id )
				return index;
		return -1;
	}

	// [EXT:overview-detail-rows] the projected live values of one placed attraction (wear, queue, riders, excitement); the original window has no detail rows
	private AttractionListRow AttractionRow( OriginalObject item ) => new( item.Runtime.AttractionId, EntryName( item.Entry ),
		(BuildCategory)item.Entry.WhichUIType, item.IsOpen, item.Runtime.State.IsBroken,
		item.Visitors.QueueLength, item.Runtime.State.OnRide, item.Visitors.WearGauge, item.Visitors.ExcitementLevel );

	public void OpenAttractionsWindow()
	{
		if ( AttractionsWindowOpen )
			return;
		attractionCategory = BuildCategory.Rides;
		attractionFirstRow = 0;
		selectedAttractionId = 0;
		RefreshAttractionRows();
		EnsureAttractionSelection();
		attractionScreen ??= CreateAttractionsScreen();
		Stack.Push( attractionScreen );
		attractionScreen.Focus( attractionScreen.Find( "attractionList" ) );
	}

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
		attractionFirstRow = Math.Clamp( attractionFirstRow, Math.Max( 0, index - StaffVisibleRows + 1 ), index );
	}

	public void ScrollAttractions( int rows ) => SetAttractionFirstRow( attractionFirstRow + rows );
	public void SetAttractionFirstRow( int first ) => attractionFirstRow = Math.Clamp( first, 0, AttractionMaxFirstRow );

	// [BIN:TPW-EXE:0x665a44 generic list sort, list attribute 0x10] the same column toggles its direction, a new column starts ascending; the initial sort column is 0 (ascending, the per-category sort words 0x750d5c..0x750d68 = 1), so the first press on column 0 descends, and each category keeps its word for the session (ListSortMemory).
	public void SortAttractions()
	{
		attractionSortDescending = !attractionSortDescending;
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
	// [BIN:TPW-EXE:0x6655a2 list click] a click of button 0 selects the row and posts 0x400, which opens the object's info panel and keeps the list (button 0 = left is medium confidence, [APPROX:UI-910])
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
		if ( match == null || RejectReadOnlyAction() )
			return;
		void Remove()
		{
			// Catalogue objects sell and unlink exactly once; the developer prototype removes through its own seam.
			level.RemoveAttraction( match );
			selectedAttractionId = 0;
			RefreshAttractionRows();
			EnsureAttractionSelection();
		}
		if ( GameOptions.Current.Confirmations )
			Stack.Push( UiDialogs.Message( "confirmDeleteAttraction", () => strings[DeleteLabelOf( row.Category )],
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
			deleteAttractionButton.Enabled = !level.IsReadOnlyVisit && selectedAttractionId != 0;
	}

	/// <summary>The selected attraction's detail rows: its category's original labels, then the live queue rows.</summary>
	// [EXT:overview] queue length and riders are live OpenTPW rows with supplementary labels (no original label is decoded).
	private (string Label, string Value)[] AttractionDetailRows( in AttractionListRow row )
	{
		var notSimulated = strings.Extra( OpenTpwText.NotSimulated );
		var statistics = ParkAttractionRoster.StatRows( row );
		if ( attractionBusinessStats.TryGetValue( row.AttractionId, out var business ) )
			for ( var index = 0; index < statistics.Length; index++ )
			{
				var label = statistics[index].Label;
				var value = label switch
				{
					UIStrings.CustomersLastMonthShop or UIStrings.CustomersLastMonthGame => business.CustomersLastMonth.ToString( System.Globalization.CultureInfo.InvariantCulture ),
					UIStrings.ProfitLastMonthShop or UIStrings.ProfitLastMonthGame => FormatMoney( business.ProfitLastMonth ),
					UIStrings.TotalProfitShop or UIStrings.TotalProfitGame => FormatMoney( business.TotalProfit ),
					_ => statistics[index].Value
				};
				statistics[index] = (label, value);
			}
		var rows = statistics.Select( stat => (strings[stat.Label], Value: stat.Value ?? notSimulated) ).ToList();
		rows.Add( (strings.Extra( OpenTpwText.QueueLength ), row.QueueLength.ToString( System.Globalization.CultureInfo.InvariantCulture )) );
		rows.Add( (strings.Extra( OpenTpwText.RidingNow ), row.Riders.ToString( System.Globalization.CultureInfo.InvariantCulture )) );
		return rows.ToArray();
	}

	private UiScreen CreateAttractionsScreen()
	{
		var screen = new AttractionsScreen( this );
		void Model( string id, string model, UiRect rect ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = rect, Anchor = UiAnchor.Center } );
		// [APPROX:UI-905] the staff/buy window frames at their authored rectangles; `list_all` is the shipped all-list frame art.
		Model( "window", "w_big", Rect( 186, 30, 2018, 1007 ) );
		Model( "statsFrame", "f_balance", Rect( 259, 650, 971, 930 ) );
		Model( "listFrame", "list_all", Rect( 1017, 179, 1813, 930 ) );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[AttractionCategories.First( tab => tab.Category == attractionCategory ).Title],
			Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = Rect( 805, 74, 1277, 153 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "attractionName", Text = () => SelectedAttractionRow.Name,
			Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
			Bounds = Rect( 283, 536, 679, 575 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "attractionStatus", Text = () => SelectedAttractionRow.AttractionId == 0 ? "" : strings[ParkAttractionRoster.StatusOf( SelectedAttractionRow )],
			Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Right, Fit = true,
			Bounds = Rect( 722, 536, 957, 575 ), Anchor = UiAnchor.Center } );
		for ( var row = 0; row < AttractionDetailRowCount; row++ )
		{
			var index = row;
			var top = 660 + row * 38f;
			screen.Add( new UiLabel { Id = $"statLabel{row}", Text = () => DetailRow( index ).Label, Font = fonts => fonts.Label,
				Color = UiColors.Text, Fit = true, Bounds = Rect( 283, top, 679, top + 36 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"statValue{row}", Text = () => DetailRow( index ).Value,
				Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Right, Fit = true,
				Bounds = Rect( 722, top, 957, top + 36 ), Anchor = UiAnchor.Center } );
		}
		(string Label, string Value) DetailRow( int index )
		{
			var rows = AttractionDetailRows( SelectedAttractionRow );
			return index < rows.Length ? rows[index] : ("", "");
		}
		foreach ( var tab in AttractionCategories )
		{
			var category = tab;
			screen.Add( new UiButton { Id = $"category{category.Category}", Model = category.Model, Clicked = () => SelectAttractionCategory( category.Category ),
				Selected = () => attractionCategory == category.Category, Bounds = category.Rect, Anchor = UiAnchor.Center } );
		}
		screen.Add( new AttractionHeader( this ) { Id = "headerName", Bounds = Rect( 1039, 331, 1460, 436 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "headerStatus", Text = () => strings[UIStrings.CurrentStatus], Font = fonts => fonts.Label,
			Color = UiColors.Text, Align = UiAlign.Center, Fit = true, Bounds = Rect( 1471, 331, 1731, 436 ), Anchor = UiAnchor.Center } );
		screen.Add( new AttractionList( this ) { Id = "attractionList", Bounds = AttractionContent, Anchor = UiAnchor.Center } );
		screen.Add( new AttractionScrollBar( this ) { Id = "attractionScroll", Bounds = AttractionScrollTrack, Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "scrollUp", Model = "b_up", Clicked = () => ScrollAttractions( -1 ),
			Bounds = Rect( 1742, 331, 1803, 391 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "scrollDown", Model = "b_down", Clicked = () => ScrollAttractions( 1 ),
			Bounds = Rect( 1742, 850, 1803, 910 ), Anchor = UiAnchor.Center } );
		// [EXT:overview-select-button] the select button selects the row's object in the park; the delete button sells it (the shipped b_goto art has no texture in ui.wad)
		// (the original buttons of this window are not decoded; the shipped b_goto art has no texture in ui.wad).
		screen.Add( new UiButton { Id = "gotoAttraction", Text = () => strings.Extra( OpenTpwText.SelectInPark ), Font = fonts => fonts.Label,
			Clicked = SelectSelectedAttraction, Bounds = Rect( 1866, 583, 1968, 686 ), Anchor = UiAnchor.Center } );
		deleteAttractionButton = screen.Add( new UiButton { Id = "deleteAttraction", Model = "b_erase", Clicked = ConfirmDeleteSelectedAttraction, Enabled = false,
			Bounds = Rect( 1866, 471, 1968, 574 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "exit", Model = "b_exit", Help = strings.Help( 2 ), Clicked = CloseAttractionsWindow,
			Bounds = Rect( 1889, 818, 1972, 901 ), Anchor = UiAnchor.Center } );
		screen.Back = CloseAttractionsWindow;
		return screen;
	}

	// A category switch resets the list position and selection, as the staff window's role filters do.
	private void SelectAttractionCategory( BuildCategory category )
	{
		attractionCategory = category;
		attractionFirstRow = 0;
		selectedAttractionId = 0;
		RefreshAttractionRows();
		EnsureAttractionSelection();
	}

	/// <summary>The All Attractions window screen: the wheel scrolls the list, the selection follows the live park.</summary>
	private sealed class AttractionsScreen : UiScreen
	{
		private readonly ParkHud owner;
		public AttractionsScreen( ParkHud owner ) : base( "attractions" ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			owner.RefreshAttractionRows();
			owner.EnsureAttractionSelection();
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

	/// <summary>The attraction rows: name and status columns over the all-list frame.</summary>
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
			var content = canvas.Map( AttractionContent, Anchor );
			if ( !content.Contains( point ) )
				return -1;
			var visible = (int)MathF.Floor( (point.Y - content.Y) / (StaffRowHeight * canvas.Scale) );
			var index = owner.attractionFirstRow + visible;
			return visible < 0 || visible >= StaffVisibleRows || index >= owner.attractionRows.Count ? -1 : index;
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var canvas = context.Canvas;
			var items = owner.attractionRows;
			for ( var visible = 0; visible < StaffVisibleRows && owner.attractionFirstRow + visible < items.Count; visible++ )
			{
				var index = owner.attractionFirstRow + visible;
				var row = items[index];
				var y = AttractionContent.Y + visible * StaffRowHeight;
				var rect = canvas.Map( Rect( AttractionContent.X, y, AttractionContent.Right, y + StaffRowHeight ), Anchor );
				var selected = row.AttractionId == owner.selectedAttractionId;
				if ( selected )
					context.Batch.AddRectangle( rect, new Veldrid.RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				var color = selected ? UiColors.Highlight : UiColors.Text;
				UiRect Cell( float left, float right )
				{
					var origin = canvas.Map( new NVector2( left, y ), Anchor );
					return new UiRect( origin.X, origin.Y, (right - left) * canvas.Scale, StaffRowHeight * canvas.Scale );
				}
				context.DrawFittedText( context.Fonts.Label, row.Name, Cell( 1043, 1456 ), color, UiAlign.Left );
				context.DrawFittedText( context.Fonts.Label, context.Strings[ParkAttractionRoster.StatusOf( row )], Cell( 1477, 1724 ), color, UiAlign.Right );
			}
		}
	}

	/// <summary>The name column header: repeated presses reverse the sort order.</summary>
	private sealed class AttractionHeader : UiElement
	{
		private readonly ParkHud owner;
		public AttractionHeader( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		public override void Activate() => owner.SortAttractions();
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			context.DrawFittedText( context.Fonts.Label, owner.strings[UIStrings.NameAttr],
				ScreenRect( context.Canvas ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
	}

	/// <summary>The vertical scroll bar, drawn and dragged like the staff window's.</summary>
	private sealed class AttractionScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public AttractionScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private UiRect KnobRect()
		{
			var travel = AttractionScrollTrack.Height - AttractionScrollKnob.Height;
			var top = AttractionScrollTrack.Y + (owner.AttractionMaxFirstRow == 0 ? 0 : travel * owner.attractionFirstRow / owner.AttractionMaxFirstRow);
			return AttractionScrollKnob with { Y = top };
		}
		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			if ( rect.Height <= 0 || owner.AttractionMaxFirstRow == 0 )
				return;
			var y = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (y - AttractionScrollTrack.Y - AttractionScrollKnob.Height / 2) / (AttractionScrollTrack.Height - AttractionScrollKnob.Height), 0, 1 );
			owner.SetAttractionFirstRow( (int)MathF.Round( fraction * owner.AttractionMaxFirstRow ) );
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( "!slider", 0, context.Canvas.Map( AttractionScrollTrack, Anchor ) );
			context.DrawModel( "b_scroller", owner.AttractionMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}
}
