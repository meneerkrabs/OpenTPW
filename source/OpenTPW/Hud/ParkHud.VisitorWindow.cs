using System.Globalization;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The All Visitors window, built from tp.exe's table 0x7506c8: the window4 frame, the list_kids list with its six sortable columns
/// (visitor number, cash remaining, time in park, rides ridden, thought icon, happiness bar), its scroll bar, the title box, and the
/// park information, all-staff, all-attractions and tick buttons.
/// </summary>
// [BIN:TPW-EXE:0x00493530 visitors window] the builder applies table 0x7506c8 (handler 0x493230), gives list 0x1e497 the row template 0x4938c0 / 0x4939d0
// (rows 0x38 = 56 units high; cells 0x490c30, 0x490250, 0x490e60, 0x48fde0, the ti_hungry model and a happygrad.wct bar), makes all six columns sortable,
// applies the sort word 0x7508bc, adds a row for every guest thing (0x481bc0 with mask 0x40 → 0x493800), selects the visitor it was opened on, titles the
// headers 0x10-0x15 with UITEXT 0x71-0x76 (help 0x81-0x86), arms the 2000 ms timer 0x80083 and posts help 0x11e; message 0x15 titles 0x1e498 with UITEXT 0x70
// [EXT:list-selection-by-id] the selected row follows its visitor while rows are added or removed, where the original keeps the selected position (+0x14c, 0x66525c) — a design choice that keeps a live list from highlighting another visitor
// [BIN:TPW-EXE:0x6644ea row refresh, 0x66403b sorted row insert] the 2000 ms refresh rewrites the cells in place and never moves a row, a new row is inserted by the list's sorted insert (ListRowOrder), and the first row added selects row 0 (0x66403b → 0x66525c)
// [BIN:TPW-EXE:0x48fe28 rides cell alignment] the rides cell's constructor 0x48fde0 stores 0 (left) in the alignment byte +0xd8 (0x48fe28); the row factory 0x4939d0 sends the alignment
// call 0x65c428 with 1, 2 and 1 (centred, right, centred) to the number, cash and time cells (0x490c30, 0x490250, 0x490e60) and none to the rides cell it makes at 0x493b38, so the rides
// column is left-aligned (ParkVisitorRoster.Columns, VisitorRidesAlignmentTests)
public sealed partial class ParkHud
{
	private static LayoutTable VisitorTable => OriginalLayoutTables.VisitorsWindow;
	private static LayoutNode VisitorListNode => VisitorTable.Control( 0x1E497 );
	/// <summary>The rows' region (the list control's inner rectangle).</summary>
	private static UiRect VisitorContent => VisitorListNode.Inner!.Value;
	private static LayoutNode VisitorSlider => VisitorListNode.Children.Single( node => node.Type == 3 );
	/// <summary>The six column headers, left to right (controls 0x10-0x15).</summary>
	private static UiRect[] VisitorColumnRects => VisitorListNode.Children.Where( node => node.Type == 2 && node.Id is >= 0x10 and < 0x20 )
		.OrderBy( node => node.Id ).Select( node => node.Rect ).ToArray();

	/// <summary>The row pitch: the row template's height (0x4938c0 stores 0x38 at 0x7ca140 and builds the row with it).</summary>
	public const float VisitorRowHeight = 0x38;
	/// <summary>Seconds between two refreshes of the rows' values (timer 0x80083, 2000 ms).</summary>
	public const float VisitorValueRefreshSeconds = 2f;
	/// <summary>The rows the list shows at once.</summary>
	public static int VisitorVisibleRows => (int)MathF.Floor( VisitorContent.Height / VisitorRowHeight );
	/// <summary>The top of the list's first row in canvas units.</summary>
	public static float VisitorRowsTop => VisitorContent.Y;

	private UiScreen? visitorScreen;
	private int visitorSortColumn { get => Sorts.VisitorColumn; set => Sorts.VisitorColumn = value; }
	private bool visitorSortDescending { get => Sorts.VisitorDescending; set => Sorts.VisitorDescending = value; }
	private int selectedVisitorId;
	// the visitor whose info a row click opened (0x400 → 0x4b79b0), 0 while none is open
	private int visitorInfoId;
	private int visitorFirstRow;
	private float visitorValueAge;
	/// <summary>The cells of each listed visitor, as the original's rows hold them between refreshes.</summary>
	private Dictionary<int, VisitorRowValues>? visitorValueStore;
	private Dictionary<int, VisitorRowValues> visitorValues => visitorValueStore ??= new();

	public bool VisitorWindowOpen => visitorScreen != null && Stack.Screens.Contains( visitorScreen );
	public int SelectedVisitorId => SelectedVisitor?.Id ?? 0;
	public int VisitorFirstRow => visitorFirstRow;
	/// <summary>The sorted column (0-5) and its direction.</summary>
	public (int Column, bool Descending) VisitorSort => (visitorSortColumn, visitorSortDescending);
	/// <summary>
	/// Every live guest thing, whatever cell it stands on.
	/// </summary>
	// [BIN:TPW-EXE:0x4fb2a5 roster add] the guest constructor posts message 0x1C (ctor 0x40f8b0, vtable 0x6fd840) to the bus [0x788d3c] and the generic thing deletion 0x50b780 posts 0x1B (ctor 0x40f900, vtable 0x6fd848); the UI receiver adds (0x481920) and removes (0x4818c0) a row for each, with no in-park test; the first fill 0x481bc0 takes every type-1 thing (flag 0x40, 0x493611)
	private IEnumerable<Guest> ListedGuests => (level.Guests?.Guests ?? Array.Empty<Guest>()).Where( guest => guest.State != GuestState.Gone );
	/// <summary>Live visitors in list order.</summary>
	public IReadOnlyList<Guest> VisitorItems => OrderVisitors( ListedGuests.ToArray() );

	/// <summary>The row order the open list holds (visitor ids); null before the window's first fill and after a header press, which sort it afresh.</summary>
	private List<int>? visitorOrder;

	/// <summary>The listed visitors in the list's order: sorted when the list is filled or a header is pressed, afterwards kept with new rows inserted (<see cref="ListRowOrder"/>).</summary>
	private IReadOnlyList<Guest> OrderVisitors( Guest[] listed )
	{
		if ( visitorOrder == null )
		{
			var sorted = ParkVisitorRoster.Rows( listed, _ => true, VisitorValuesOf, visitorSortColumn, visitorSortDescending );
			visitorOrder = sorted.Select( guest => guest.Id ).ToList();
			return sorted;
		}
		return ParkVisitorRoster.Keep( visitorOrder, listed, VisitorValuesOf, visitorSortColumn, visitorSortDescending );
	}

	/// <summary>
	/// The window's rows for one frame: the screen refreshes them when it updates and every action that changes the order refreshes them again,
	/// so the list and scrollbar share one snapshot instead of re-filtering and re-sorting the roster for every draw call (a full park holds
	/// hundreds of visitors). <see cref="VisitorItems"/> stays live for callers outside the frame.
	/// </summary>
	private IReadOnlyList<Guest> visitorRows = Array.Empty<Guest>();

	private IReadOnlyList<Guest> RefreshVisitorRows( bool values = false )
	{
		var listed = ListedGuests.ToArray();
		if ( values )
		{
			// [BIN:TPW-EXE:0x0049326c rows refresh] timer 0x80083 rewrites all six cells of every row (mask 0x3f) with the visitor's values (time in park in whole 4-turn steps)
			visitorValueAge = 0;
			visitorValues.Clear();
			foreach ( var guest in listed )
				visitorValues[guest.Id] = ParkVisitorRoster.ValuesOf( guest, refreshed: true );
		}
		else
		{
			var present = listed.Select( guest => guest.Id ).ToHashSet();
			foreach ( var id in visitorValues.Keys.Where( id => !present.Contains( id ) ).ToArray() )
				visitorValues.Remove( id );
		}
		return visitorRows = OrderVisitors( listed );
	}

	/// <summary>The cells of a listed visitor: the ones its row holds, or a new row's for a visitor not seen since the last refresh.</summary>
	// [BIN:TPW-EXE:0x00493800 row add] a new row takes the visitor's values with the time in park from (turn − stamp) >> 2 turns without the refresh's << 2, a quarter of it until the next refresh
	internal VisitorRowValues VisitorValuesOf( Guest guest )
	{
		if ( !visitorValues.TryGetValue( guest.Id, out var values ) )
			visitorValues[guest.Id] = values = ParkVisitorRoster.ValuesOf( guest, refreshed: false );
		return values;
	}

	private Guest? SelectedVisitor => visitorRows.FirstOrDefault( guest => guest.Id == selectedVisitorId );
	private int VisitorMaxFirstRow => ParkAttractionRoster.MaxFirstRow( visitorRows.Count, VisitorVisibleRows );
	private int VisitorRowIndex( int id )
	{
		var rows = visitorRows;
		for ( var index = 0; index < rows.Count; index++ )
			if ( rows[index].Id == id )
				return index;
		return -1;
	}

	public void OpenVisitorWindow()
	{
		if ( VisitorWindowOpen )
			return;
		RememberInfoWindow( InfoWindowKind.Visitors );
		visitorFirstRow = 0;
		selectedVisitorId = 0;
		visitorValues.Clear();
		visitorValueAge = 0;
		visitorInfoId = 0;
		visitorOrder = null;
		RefreshVisitorRows();
		EnsureVisitorSelection();
		visitorScreen ??= CreateVisitorScreen();
		Stack.Push( visitorScreen );
		visitorScreen.Focus( visitorScreen.Find( "visitorList" ) );
	}

	public void CloseVisitorWindow()
	{
		if ( visitorScreen == null || !VisitorWindowOpen )
			return;
		while ( Stack.Top != visitorScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == visitorScreen )
			Stack.Pop();
	}

	/// <summary>Selects the list row <paramref name="index"/> (clamped) and scrolls it into view.</summary>
	public void SelectVisitor( int index )
	{
		var items = visitorRows;
		if ( items.Count == 0 )
		{
			selectedVisitorId = 0;
			return;
		}
		index = Math.Clamp( index, 0, items.Count - 1 );
		selectedVisitorId = items[index].Id;
		visitorFirstRow = Math.Clamp( visitorFirstRow, Math.Max( 0, index - VisitorVisibleRows + 1 ), index );
	}

	public void ScrollVisitors( int rows ) => SetVisitorFirstRow( visitorFirstRow + rows );
	public void SetVisitorFirstRow( int first ) => visitorFirstRow = Math.Clamp( first, 0, VisitorMaxFirstRow );

	// [BIN:TPW-EXE:0x665a44 generic list sort, list attribute 0x10] the same column toggles its direction, a new column starts ascending; the initial sort column is 0 (ascending, sort word 0x7508bc = 1), so the first press on column 0 descends
	// [BIN:TPW-EXE:0x00493475 sort word] message 0x406 writes the list's new sort word back to 0x7508bc, so the order is kept for the session (ListSortMemory)
	public void SortVisitors( int column = 0 )
	{
		visitorSortDescending = visitorSortColumn == column && !visitorSortDescending;
		visitorSortColumn = column;
		visitorOrder = null;
		RefreshVisitorRows();
		var index = VisitorRowIndex( selectedVisitorId );
		EnsureVisitorSelection();
		if ( index >= 0 )
			SelectVisitor( index );
	}

	/// <summary>A right click on a row (and Enter): focuses the visitor's simulated position and leaves the list, like the locator buttons.</summary>
	// [BIN:TPW-EXE:0x66563d list click, 0x4934c5 locate] a click of the other button selects the row and posts 0x402, which centres the camera on the visitor's cell (0x4867b0) and closes the window; Enter has no original counterpart ([EXT:overview-enter-locate], the keyboard analogue)
	public bool LocateSelectedVisitor() => LocateVisitor( SelectedVisitor );

	/// <summary>A left click on a row: the per-visitor window opens on the visitor and gives way to this list.</summary>
	// [BIN:TPW-EXE:0x6655a2 list click] a click of button 0 selects the row and posts 0x400, which the window routes to the row's visitor's info window (0x49351e → 0x4b79b0, ParkHud.VisitorInfoWindow.cs); that opener closes the open window, so the list gives way
	// [BIN:TPW-EXE:0x634963 mouse button codes] button 0 is the primary (left) button: DirectInput's DIMOFS_BUTTON0..3 (dwOfs 0xc..0xf) become the codes 0xf0..0xf3 (0x63497c), 0x634420 swaps 0xf0 and 0xf1 back when Windows swaps the buttons (SwapMouseButton)
	private void OpenSelectedVisitorInfo()
	{
		if ( SelectedVisitor is { } guest )
			OpenVisitorInfoWindow( guest );
	}

	/// <summary>Whether locating can run at all: the park camera over an original park's terrain.</summary>
	public bool CanLocateVisitors => Camera.Mode is ParkCameraMode && level.OriginalPark != null;

	/// <summary>Focuses a specific visitor's simulated position on the terrain (the locator flow of the staff window's visitor button).</summary>
	// the row's right click (0x402, 0x4867b0) focuses the visitor's simulated position; the locator panels are ParkHud.LocatorPanel.cs
	public bool LocateVisitor( Guest? guest )
	{
		if ( guest == null || Camera.Mode is not ParkCameraMode camera || level.OriginalPark is not { } park ||
			!float.IsFinite( guest.X ) || !float.IsFinite( guest.Y ) )
			return false;
		var position = GuestRenderer.CellToEngine( park.Heightfield, guest.X, guest.Y );
		camera.Focus( new Vector3( position.X, position.Y, position.Z ) );
		lastLocatedVisitorId = guest.Id;
		CloseVisitorWindow();
		return true;
	}

	/// <summary>Re-derives the selection from the live simulation: a visitor who left hands it to the first row.</summary>
	private void EnsureVisitorSelection()
	{
		selectedVisitorId = ParkVisitorRoster.HealedSelection( visitorRows, selectedVisitorId );
		SetVisitorFirstRow( visitorFirstRow );
	}

	/// <summary>The text of a visitor's cell in a text column (0-3).</summary>
	internal string VisitorCellText( VisitorRowValues values, int column ) => column switch
	{
		// [BIN:TPW-EXE:0x00490dc0 number cell] "%d", or UITEXT 0x1c9 (OpenTPW 456, "New Visitor") for 0
		0 => values.Number == 0 ? strings[UIStrings.NewVisitor] : values.Number.ToString( CultureInfo.InvariantCulture ),
		// [BIN:TPW-EXE:0x004903e0 money cell] UITEXT 0x1c1 "$ %d" (OpenTPW 448), or 0x1c2 "-$ %d" (449) with the magnitude below 0
		1 => FormatMoney( values.Cash ),
		2 => values.Hours is { } hours ? VisitorDuration( hours ) : "—",
		// [BIN:TPW-EXE:0x0048ff70 count cell] "%d"
		3 => values.Rides.ToString( CultureInfo.InvariantCulture ),
		_ => "",
	};

	/// <summary>The time-in-park cell: hours up to a day, then whole days (with the singular up to two days).</summary>
	// [BIN:TPW-EXE:0x00490ff0 duration cell] above 48 hours "%d days" (UITEXT 0x1bb) of hours / 24, above 24 "%d day" (0x1ba) of hours / 24 (so 48 hours read "2 day"), else "%d hours" (0x1b9)
	internal string VisitorDuration( long hours ) => strings.Format( ParkVisitorRoster.DurationRecord( hours ), ParkVisitorRoster.DurationValue( hours ).ToString( CultureInfo.InvariantCulture ) );

	private UiScreen CreateVisitorScreen()
	{
		var screen = new VisitorScreen( this );
		var table = VisitorTable;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiModelImage { Id = "listFrame", Model = VisitorListNode.Model!, Bounds = VisitorListNode.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.AllVisitors], Font = fonts => fonts.Title,
			Shadow = true, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = table.Control( 0x1E498 ).Rect, Anchor = UiAnchor.Center } );
		var rects = VisitorColumnRects;
		for ( var column = 0; column < ParkVisitorRoster.Columns.Length; column++ )
			screen.Add( new VisitorHeader( this, column ) { Id = ParkVisitorRoster.Columns[column].Id, Help = strings.Help( ParkVisitorRoster.Columns[column].Help ),
				Bounds = rects[column], Anchor = UiAnchor.Center } );
		screen.Add( new VisitorList( this ) { Id = "visitorList", Help = strings.Help( VisitorListNode.Help ), Bounds = VisitorContent, Anchor = UiAnchor.Center } );
		var slider = VisitorSlider;
		screen.Add( new VisitorScrollBar( this ) { Id = "visitorScroll", Bounds = slider.Rect, Anchor = UiAnchor.Center } );
		// The arrows' parent is the type 3 slider, style 0x10, so they play 0xBD (LayoutTable.ClickSoundOf). A held arrow repeats at 500 ms then 125 ms and a release after that is silent.
		void ScrollArrow( LayoutNode node, int step )
		{
			void Scroll() => ScrollVisitors( step );
			screen.Add( new UiButton { Id = step < 0 ? "scrollUp" : "scrollDown", Model = node.Model, ClickSound = table.ClickSoundOf( node ),
				Clicked = Scroll, Repeat = UiButton.ScrollArrowRepeat( Scroll ), Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		ScrollArrow( slider.Children.Single( node => node.Model == "b_up" ), -1 );
		ScrollArrow( slider.Children.Single( node => node.Model == "b_down" ), 1 );
		// [BIN:TPW-EXE:0x004933dc visitors buttons] the tick (-2) closes the window; b_pkinfo (0x1e499) opens the park information (0x4a52a0), b_allstaff (0x1e49a)
		// the All Staff window (0x496620) and b_allthings (0x1e49b) the attractions window (0x495aa0), each with the last category and replacing this one
		var okay = table.Model( "b_okay" );
		// The tick's parent is the window (style 0x1, no bit 0x10), so it plays the ordinary 0x1F.
		screen.Add( new UiButton { Id = "exit", Model = okay.Model, Help = strings.Help( okay.Help ), ClickSound = table.ClickSoundOf( okay ),
			Clicked = CloseVisitorWindow, Bounds = okay.Rect, Anchor = UiAnchor.Center } );
		var parkInfo = table.Model( "b_pkinfo" );
		screen.Add( new UiButton { Id = "parkInfo", Model = parkInfo.Model, Help = strings.Help( parkInfo.Help ), Bounds = parkInfo.Rect, Anchor = UiAnchor.Center,
			Clicked = () => { CloseVisitorWindow(); OpenParkInfoWindow(); } } );
		var allStaff = table.Model( "b_allstaff" );
		screen.Add( new UiButton { Id = "allStaff", Model = allStaff.Model, Help = strings.Help( allStaff.Help ), Bounds = allStaff.Rect, Anchor = UiAnchor.Center,
			Clicked = () => { CloseVisitorWindow(); OpenAllStaffWindow(); } } );
		var allThings = table.Model( "b_allthings" );
		screen.Add( new UiButton { Id = "allAttractions", Model = allThings.Model, Help = strings.Help( allThings.Help ), Bounds = allThings.Rect, Anchor = UiAnchor.Center,
			Clicked = () => { CloseVisitorWindow(); OpenAttractionsWindow(); } } );
		// [EXT:overview-select-button] the go-to button locates the selected visitor, like a right click on the row; it sits below the column of round buttons,
		// where the original window has nothing (the shipped b_goto art has no texture in ui.wad)
		screen.Add( new UiButton { Id = "locateVisitor", Model = "b_gopeep", Clicked = () => LocateSelectedVisitor(), Enabled = false,
			Bounds = Rect( 1866, 682, 1968, 785 ), Anchor = UiAnchor.Center } );
		screen.Back = CloseVisitorWindow;
		return screen;
	}

	/// <summary>The All Visitors window screen: the wheel scrolls the list, the selection follows the live simulation, the values refresh every 2 s.</summary>
	private sealed class VisitorScreen : UiScreen
	{
		private readonly ParkHud owner;
		public VisitorScreen( ParkHud owner ) : base( "visitors" ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			owner.visitorValueAge += Math.Max( 0, context.Delta );
			owner.RefreshVisitorRows( values: owner.visitorValueAge >= VisitorValueRefreshSeconds );
			owner.EnsureVisitorSelection();
			if ( Find( "locateVisitor" ) is UiButton locate )
				locate.Enabled = owner.SelectedVisitorId != 0 && owner.CanLocateVisitors;
			if ( input.Wheel != 0 )
			{
				owner.ScrollVisitors( input.Wheel > 0 ? -1 : 1 );
				input = input with { Wheel = 0 };
			}
			return base.Update( context, input );
		}
	}

	/// <summary>The visitor rows: the six columns under their headers.</summary>
	private sealed class VisitorList : UiElement, IUiRowList
	{
		private readonly ParkHud owner;
		public VisitorList( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		public int Selected => owner.VisitorRowIndex( owner.selectedVisitorId );
		public bool ArrowsSelect => true;
		public void Select( int index ) => owner.SelectVisitor( index );
		public void Scroll( int rows ) => owner.ScrollVisitors( rows );
		public bool HoverSelects => true;
		public int HoveredRow { get; private set; } = -1;
		public void Hover( int index ) => HoveredRow = index;
		// [BIN:TPW-EXE:0x665c35 list dispatcher] attribute 0x291 lists: a left click selects the row and posts 0x400; a right click selects the row and posts 0x402 (centre the camera on the visitor, close)
		public bool RowClicked( int index, bool left )
		{
			Select( index );
			if ( left )
			{
				owner.OpenSelectedVisitorInfo();
				return true;
			}
			return owner.LocateSelectedVisitor();
		}
		// Enter keeps the keyboard analogue of the right click (no original counterpart, [EXT:overview-enter-locate]); it plays the visitor click as before.
		public override void Activate()
		{
			GameAudio.PlayVisitorClick();
			owner.LocateSelectedVisitor();
		}
		public override void Adjust( int direction ) => owner.SelectVisitor( Math.Max( 0, Selected ) + direction );
		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			var content = canvas.Map( VisitorContent, Anchor );
			if ( !content.Contains( point ) )
				return -1;
			var visible = (int)MathF.Floor( (point.Y - content.Y) / (VisitorRowHeight * canvas.Scale) );
			var index = owner.visitorFirstRow + visible;
			return visible < 0 || visible >= VisitorVisibleRows || index >= owner.visitorRows.Count ? -1 : index;
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var canvas = context.Canvas;
			var items = owner.visitorRows;
			var columns = VisitorColumnRects;
			var content = VisitorContent;
			var thoughts = context.Models.Get( "ti_hungry" );
			for ( var visible = 0; visible < VisitorVisibleRows && owner.visitorFirstRow + visible < items.Count; visible++ )
			{
				var index = owner.visitorFirstRow + visible;
				var guest = items[index];
				var values = owner.VisitorValuesOf( guest );
				var y = content.Y + visible * VisitorRowHeight;
				var row = canvas.Map( Rect( content.X, y, content.Right, y + VisitorRowHeight ), Anchor );
				var selected = guest.Id == owner.selectedVisitorId;
				if ( index == HoveredRow && !selected )
					context.Batch.AddRectangle( row, new Veldrid.RgbaByte( 60, 90, 200, 60 ) );
				if ( selected )
					context.Batch.AddRectangle( row, new Veldrid.RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				var color = selected ? UiColors.Highlight : UiColors.Text;
				UiRect Cell( int column ) => canvas.Map( Rect( columns[column].X, y, columns[column].Right, y + VisitorRowHeight ), Anchor );
				for ( var column = 0; column < 4; column++ )
					context.DrawFittedText( context.Fonts.Label, owner.VisitorCellText( values, column ), Cell( column ), color, ParkVisitorRoster.Columns[column].Align );
				// [BIN:TPW-EXE:0x00493b81 thought cell] the ti_hungry model shows the thought's frame (thought − 1); 999 (no thought) draws nothing
				if ( values.ThoughtFrame is >= 0 and var frame && thoughts != null && frame < thoughts.Frames.Count )
				{
					var cell = Cell( 4 );
					var size = MathF.Min( cell.Width, cell.Height );
					context.DrawModel( "ti_hungry", frame, new UiRect( cell.X + (cell.Width - size) / 2, cell.Y, size, size ) );
				}
				var bar = Cell( 5 );
				UiGauge.Draw( context.Batch, bar.Inflate( -bar.Height * 0.2f ), values.HappinessGauge / (float)EmployeeWindowRules.GaugeFull, UiGauge.HappinessArtPath );
			}
		}
	}

	/// <summary>A column header: a press sorts by its column, a second press reverses the order.</summary>
	private sealed class VisitorHeader : UiElement
	{
		private readonly ParkHud owner;
		private readonly int column;
		public VisitorHeader( ParkHud owner, int column ) { this.owner = owner; this.column = column; }
		public override bool Focusable => Visible && Enabled;
		// The header's parent is list 0x1E497 (type 7, style 0x291), which has bit 0x10, so the hook plays 0xBD.
		public override void Activate()
		{
			GameAudio.PlayUi( LayoutTable.ClickSoundFor( VisitorListNode ) );
			owner.SortVisitors( column );
		}
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			context.DrawFittedText( context.Fonts.Label, owner.strings[ParkVisitorRoster.Columns[column].Header],
				ScreenRect( context.Canvas ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
	}

	/// <summary>The vertical scroll bar: the table's slider track and knob.</summary>
	private sealed class VisitorScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public VisitorScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private static UiRect Knob => VisitorSlider.Children.Single( node => node.Model == "b_scroller" ).Rect;
		private UiRect KnobRect()
		{
			var track = VisitorSlider.Rect;
			var travel = track.Height - Knob.Height;
			var top = track.Y + (owner.VisitorMaxFirstRow == 0 ? 0 : travel * owner.visitorFirstRow / owner.VisitorMaxFirstRow);
			return Knob with { Y = top };
		}
		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			if ( rect.Height <= 0 || owner.VisitorMaxFirstRow == 0 )
				return;
			var track = VisitorSlider.Rect;
			var y = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (y - track.Y - Knob.Height / 2) / (track.Height - Knob.Height), 0, 1 );
			owner.SetVisitorFirstRow( (int)MathF.Round( fraction * owner.VisitorMaxFirstRow ) );
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( VisitorSlider.Model2 ?? "!slider", 0, context.Canvas.Map( VisitorSlider.Rect, Anchor ) );
			context.DrawModel( "b_scroller", owner.VisitorMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}
}
