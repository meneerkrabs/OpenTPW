using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The All Visitors window: the park's live visitors in a list with the original visitor labels
/// (UITEXT 112-118: visitor number, cash remaining, time in park, rides ridden, happiness) and the
/// original key table's All Visitors key (V). No original layout table is decoded for this window,
/// so its placement follows the proven staff/buy window geometry.
/// </summary>
// [APPROX:UI-907] window layout, list columns and detail placement follow the staff/buy table geometry; the decoded table (visitors 0x7506c8, six columns) is not ported, its list behaviour (attribute 0x291) is — evidence needed: porting it
public sealed partial class ParkHud
{
	private static readonly UiRect VisitorContent = Rect( 1043, 441, 1729, 911 );
	private static readonly UiRect VisitorScrollTrack = Rect( 1743, 398, 1802, 843 );
	private static readonly UiRect VisitorScrollKnob = Rect( 1735, 651, 1802, 718 );
	// [EXT:overview-detail-rows] six equal detail rows fill the stats frame's area (five original rows plus the live thought row); the original window has no detail rows.
	private const int VisitorDetailRowCount = 6;

	private UiScreen? visitorScreen;
	private bool visitorSortDescending { get => Sorts.VisitorDescending; set => Sorts.VisitorDescending = value; }
	private int selectedVisitorId;
	// the visitor whose info a row click opened (0x400 → 0x4b79b0), 0 while none is open
	private int visitorInfoId;
	private int visitorFirstRow;
	private string[]? kidStates;
	private string[]? thoughts;

	public bool VisitorWindowOpen => visitorScreen != null && Stack.Screens.Contains( visitorScreen );
	public int SelectedVisitorId => SelectedVisitor?.Id ?? 0;
	public int VisitorFirstRow => visitorFirstRow;
	/// <summary>Live in-park visitors in list order (by visitor number).</summary>
	public IReadOnlyList<Guest> VisitorItems => ParkVisitorRoster.Rows(
		level.Guests?.Guests ?? Array.Empty<Guest>(), guest => level.Guests?.IsInPark( guest ) ?? false, false, visitorSortDescending );

	/// <summary>
	/// The window's rows for one frame: the screen refreshes them when it updates and every action that
	/// changes the order refreshes them again, so the labels, list, scrollbar and locators share one
	/// snapshot instead of re-filtering and re-sorting the roster for every draw call (a full park holds
	/// hundreds of visitors). <see cref="VisitorItems"/> stays live for callers outside the frame.
	/// </summary>
	private IReadOnlyList<Guest> visitorRows = Array.Empty<Guest>();
	private IReadOnlyList<Guest> RefreshVisitorRows() => visitorRows = VisitorItems;
	private Guest? SelectedVisitor => visitorRows.FirstOrDefault( guest => guest.Id == selectedVisitorId );
	/// <summary>The visitor whose info a left click on a row opened, while that visitor is still listed (the original's visitor info window and its thing [0x7c2658]).</summary>
	private Guest? VisitorInfoGuest => visitorInfoId == 0 ? null : visitorRows.FirstOrDefault( guest => guest.Id == visitorInfoId );
	private int VisitorMaxFirstRow => ParkAttractionRoster.MaxFirstRow( visitorRows.Count, StaffVisibleRows );
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
		visitorFirstRow = 0;
		selectedVisitorId = 0;
		visitorInfoId = 0;
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
		visitorFirstRow = Math.Clamp( visitorFirstRow, Math.Max( 0, index - StaffVisibleRows + 1 ), index );
	}

	public void ScrollVisitors( int rows ) => SetVisitorFirstRow( visitorFirstRow + rows );
	public void SetVisitorFirstRow( int first ) => visitorFirstRow = Math.Clamp( first, 0, VisitorMaxFirstRow );

	// [BIN:TPW-EXE:0x665a44 generic list sort, list attribute 0x10] the same column toggles its direction, a new column starts ascending; the initial sort column is 0 (ascending, sort word 0x7508bc = 1), so the first press on column 0 descends, and the word is kept for the session (ListSortMemory).
	public void SortVisitors()
	{
		visitorSortDescending = !visitorSortDescending;
		RefreshVisitorRows();
		var index = VisitorRowIndex( selectedVisitorId );
		EnsureVisitorSelection();
		if ( index >= 0 )
			SelectVisitor( index );
	}

	/// <summary>A right click on a row (and Enter): focuses the visitor's simulated position and leaves the list, like the locator buttons.</summary>
	// [BIN:TPW-EXE:0x66563d list click, 0x4867b0 locate] a click of the other button selects the row and posts 0x402, which centres the camera on the visitor's cell and closes the window; Enter has no original counterpart ([EXT:overview-enter-locate], the keyboard analogue)
	public bool LocateSelectedVisitor() => LocateVisitor( SelectedVisitor );

	/// <summary>A left click on a row: selects the visitor and plays the click; the window and its detail rows (the visitor's info panel) stay.</summary>
	// [BIN:TPW-EXE:0x6655a2 list click] a click of button 0 selects the row and posts 0x400, which opens the visitor's info panel and keeps the list (button 0 = left is medium confidence, [APPROX:UI-910])
	private void OpenSelectedVisitorInfo()
	{
		visitorInfoId = selectedVisitorId;
		GameAudio.PlayVisitorClick();
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

	private string[] KidStates => kidStates ??= GameLanguage.Current.LoadStrings( "KIDSTATES.str" ).Entries;
	private string[] Thoughts => thoughts ??= GameLanguage.Current.LoadStrings( "THOUGHTS.str" ).Entries;

	/// <summary>The selected visitor's detail rows: the original labels, elapsed simulation duration (unknown stays an em dash), then the live thought row.</summary>
	// [EXT:overview] the current thought is a live OpenTPW row with a supplementary label (no original label is decoded).
	private (string Label, string Value)[] VisitorDetailRows( Guest? guest )
	{
		if ( guest == null )
			return Array.Empty<(string, string)>();
		var notSimulated = strings.Extra( OpenTpwText.NotSimulated );
		var rows = ParkVisitorRoster.StatRows( guest, KidStates, FormatMoney )
			.Select( stat => (strings[stat.Label], Value: stat.Value ?? notSimulated) )
			.ToList();
		rows.Add( (strings.Extra( OpenTpwText.VisitorThought ), ParkVisitorRoster.ThoughtOf( guest, Thoughts )) );
		return rows.ToArray();
	}

	private UiScreen CreateVisitorScreen()
	{
		var screen = new VisitorScreen( this );
		void Model( string id, string model, UiRect rect ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = rect, Anchor = UiAnchor.Center } );
		// [APPROX:UI-907] the staff/buy window frames at their authored rectangles.
		Model( "window", "w_big", Rect( 186, 30, 2018, 1007 ) );
		Model( "statsFrame", "f_balance", Rect( 259, 650, 971, 930 ) );
		Model( "listFrame", "hirestaff", Rect( 1017, 179, 1813, 930 ) );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.AllVisitors], Font = fonts => fonts.Title,
			Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = Rect( 805, 74, 1277, 153 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "visitorNumberLabel", Text = () => strings[UIStrings.VisitorNumber], Font = fonts => fonts.Label,
			Color = UiColors.Text, Fit = true, Bounds = Rect( 283, 536, 679, 575 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "visitorNumber", Text = () => SelectedVisitorId == 0 ? "" : SelectedVisitorId.ToString( System.Globalization.CultureInfo.InvariantCulture ),
			Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Right, Fit = true,
			Bounds = Rect( 722, 536, 957, 575 ), Anchor = UiAnchor.Center } );
		for ( var row = 0; row < VisitorDetailRowCount; row++ )
		{
			var index = row;
			var top = 660 + row * 44f;
			screen.Add( new UiLabel { Id = $"statLabel{row}", Text = () => DetailRow( index ).Label, Font = fonts => fonts.Label,
				Color = UiColors.Text, Fit = true, Bounds = Rect( 283, top, 679, top + 42 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"statValue{row}", Text = () => DetailRow( index ).Value,
				Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Right, Fit = true,
				Bounds = Rect( 722, top, 957, top + 42 ), Anchor = UiAnchor.Center } );
		}
		(string Label, string Value) DetailRow( int index )
		{
			var rows = VisitorDetailRows( SelectedVisitor );
			return index < rows.Length ? rows[index] : ("", "");
		}
		screen.Add( new VisitorHeader( this ) { Id = "headerNumber", Bounds = Rect( 1039, 331, 1330, 436 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "headerStatus", Text = () => strings[UIStrings.CurrentStatus], Font = fonts => fonts.Label,
			Color = UiColors.Text, Align = UiAlign.Center, Fit = true, Bounds = Rect( 1335, 331, 1456, 436 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "headerHappiness", Text = () => strings[UIStrings.HappinessVisitor], Font = fonts => fonts.Label,
			Color = UiColors.Text, Align = UiAlign.Center, Fit = true, Bounds = Rect( 1471, 331, 1731, 436 ), Anchor = UiAnchor.Center } );
		screen.Add( new VisitorList( this ) { Id = "visitorList", Bounds = VisitorContent, Anchor = UiAnchor.Center } );
		screen.Add( new VisitorScrollBar( this ) { Id = "visitorScroll", Bounds = VisitorScrollTrack, Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "scrollUp", Model = "b_up", Clicked = () => ScrollVisitors( -1 ),
			Bounds = Rect( 1742, 331, 1803, 391 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "scrollDown", Model = "b_down", Clicked = () => ScrollVisitors( 1 ),
			Bounds = Rect( 1742, 850, 1803, 910 ), Anchor = UiAnchor.Center } );
		// [EXT:overview-select-button] the go-to button locates the selected visitor, like a right click on the row (the shipped b_goto art has no texture in ui.wad)
		screen.Add( new UiButton { Id = "locateVisitor", Model = "b_gopeep", Clicked = () => LocateSelectedVisitor(), Enabled = false,
			Bounds = Rect( 1866, 583, 1968, 686 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "exit", Model = "b_exit", Help = strings.Help( 2 ), Clicked = CloseVisitorWindow,
			Bounds = Rect( 1889, 818, 1972, 901 ), Anchor = UiAnchor.Center } );
		screen.Back = CloseVisitorWindow;
		return screen;
	}

	/// <summary>The All Visitors window screen: the wheel scrolls the list, the selection follows the live simulation.</summary>
	private sealed class VisitorScreen : UiScreen
	{
		private readonly ParkHud owner;
		public VisitorScreen( ParkHud owner ) : base( "visitors" ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			owner.RefreshVisitorRows();
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

	/// <summary>The visitor rows: number, status and happiness columns over the staff list frame.</summary>
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
		// [BIN:TPW-EXE:0x665c35 list dispatcher] attribute 0x291 lists: a left click selects the row and opens its info panel (the window's detail rows) and keeps the list; a right click selects the row, centres the camera on the visitor and closes
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
			var visible = (int)MathF.Floor( (point.Y - content.Y) / (StaffRowHeight * canvas.Scale) );
			var index = owner.visitorFirstRow + visible;
			return visible < 0 || visible >= StaffVisibleRows || index >= owner.visitorRows.Count ? -1 : index;
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var canvas = context.Canvas;
			var items = owner.visitorRows;
			for ( var visible = 0; visible < StaffVisibleRows && owner.visitorFirstRow + visible < items.Count; visible++ )
			{
				var index = owner.visitorFirstRow + visible;
				var guest = items[index];
				var y = VisitorContent.Y + visible * StaffRowHeight;
				var row = canvas.Map( Rect( VisitorContent.X, y, VisitorContent.Right, y + StaffRowHeight ), Anchor );
				var selected = guest.Id == owner.selectedVisitorId;
				if ( index == HoveredRow && !selected )
					context.Batch.AddRectangle( row, new Veldrid.RgbaByte( 60, 90, 200, 60 ) );
				if ( selected )
					context.Batch.AddRectangle( row, new Veldrid.RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				var color = selected ? UiColors.Highlight : UiColors.Text;
				UiRect Cell( float left, float right )
				{
					var origin = canvas.Map( new NVector2( left, y ), Anchor );
					return new UiRect( origin.X, origin.Y, (right - left) * canvas.Scale, StaffRowHeight * canvas.Scale );
				}
				context.DrawFittedText( context.Fonts.Label, guest.Id.ToString( System.Globalization.CultureInfo.InvariantCulture ), Cell( 1043, 1250 ), color, UiAlign.Right );
				context.DrawFittedText( context.Fonts.Label, ParkVisitorRoster.StatusOf( guest, owner.KidStates ), Cell( 1255, 1456 ), color, UiAlign.Left );
				context.DrawFittedText( context.Fonts.Label, $"{ParkVisitorRoster.HappinessPercent( guest.Happiness )}%", Cell( 1477, 1724 ), color, UiAlign.Right );
			}
		}
	}

	/// <summary>The visitor number column header: repeated presses reverse the sort order.</summary>
	private sealed class VisitorHeader : UiElement
	{
		private readonly ParkHud owner;
		public VisitorHeader( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		public override void Activate() => owner.SortVisitors();
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			context.DrawFittedText( context.Fonts.Label, owner.strings[UIStrings.VisitorNumber],
				ScreenRect( context.Canvas ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
	}

	/// <summary>The vertical scroll bar, drawn and dragged like the staff window's.</summary>
	private sealed class VisitorScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public VisitorScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private UiRect KnobRect()
		{
			var travel = VisitorScrollTrack.Height - VisitorScrollKnob.Height;
			var top = VisitorScrollTrack.Y + (owner.VisitorMaxFirstRow == 0 ? 0 : travel * owner.visitorFirstRow / owner.VisitorMaxFirstRow);
			return VisitorScrollKnob with { Y = top };
		}
		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			if ( rect.Height <= 0 || owner.VisitorMaxFirstRow == 0 )
				return;
			var y = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (y - VisitorScrollTrack.Y - VisitorScrollKnob.Height / 2) / (VisitorScrollTrack.Height - VisitorScrollKnob.Height), 0, 1 );
			owner.SetVisitorFirstRow( (int)MathF.Round( fraction * owner.VisitorMaxFirstRow ) );
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( "!slider", 0, context.Canvas.Map( VisitorScrollTrack, Anchor ) );
			context.DrawModel( "b_scroller", owner.VisitorMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}
}
