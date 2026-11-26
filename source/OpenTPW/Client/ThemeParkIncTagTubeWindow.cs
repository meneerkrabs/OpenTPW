using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>What the tag tube window reads and changes of the park: the tube, and its delete and read.</summary>
internal interface IThemeParkIncTagTubeWorld
{
	/// <summary>The park's tag tube.</summary>
	ThemeParkIncTagTube Tube { get; }
	/// <summary>The delete <c>0x636A40</c>.</summary>
	void Delete( ThemeParkIncTagTubeRow row );
	/// <summary>The row read of <c>0x553480</c>; false when the row was read.</summary>
	bool Read( ThemeParkIncTagTubeRow row );
}

/// <summary>What survives the tag tube window between openings: the window object at <c>0xA60188</c> is static.</summary>
internal sealed class ThemeParkIncTagTubeSession
{
	/// <summary>
	/// <c>+0x68</c>: the list's sort key, the sort column plus one, negative for descending (<c>0x857797</c>): -2 at start (the date, newest first); a header press stores the new key
	/// (list message 0x406, <c>0x5516E0</c>).
	/// </summary>
	// [BIN:TPI-EXE:0x551CA5 tube sort key] the constructor 0x551C60 writes +0x68 = -2
	public int SortKey { get; set; } = -2;
}

/// <summary>
/// Theme Park Inc's tag tube window, the "Message Centre" (window 3000, table <c>0x9BBC84</c>, docs/reverse/TPI-TAG-TUBE.md): the inbox list of the tube's rows (subject and date, sorted by
/// the session's key), the details of the selected row (From, To, Subject, the sender's picture and the body), Delete, the row's go-to and the empty-list label. Selecting a row marks it
/// read after 2.5 seconds, or when another row is shown or the window closes after half a second; no row click opens the letter window.
/// </summary>
// [BIN:TPI-EXE:0x551CF0 tube window] the builder: the HUD's window closes (0x5013C0), +0x1C = 0, +0x20 = -1, +0x59 = 0, +0x24 = 0, +0x28 = -1, CreateWindow 0x84DEA0(0x9BBC84) with the procedure
// 0x5517B0 on the root and 0x5516E0 on the panel 3005, then the controls, the list (0x552650) and its first refresh
internal sealed class ThemeParkIncTagTubeWindow
{
	/// <summary>The screen's name on the HUD stack.</summary>
	public const string ScreenName = "tpiTagTube";

	// [BIN:TPI-EXE:0x551DA1 tube texts] UITEXT 0x35D (title), 0x35C (the list header), 0x35B (the detail header), 0x358/0x359/0x357 (From:, Subject:, To:), 0xD3 (the empty list), 0xD1/0xD2
	// (the column headers, 0x5526E6)
	public const int TitleText = 861, InboxText = 860, DetailsText = 859, FromLabel = 856, SubjectLabel = 857, ToLabel = 855, NoMessages = 211, SubjectHeader = 209, ReceivedHeader = 210;

	/// <summary>The read timer: a shown row is read this many milliseconds after it was selected (<c>+0x60</c>).</summary>
	// [BIN:TPI-EXE:0x551C97 tube timers] the constructor writes +0x60 = 2500 and +0x64 = 2000; the read 0x553480 needs now + 2000 > the deadline (selected + 2500): shown for more than 500 ms
	public const int ReadDelay = 2500, ReadGrace = 2000;

	/// <summary>The flash period of an unread row of a category above 0 (<c>0x552A69</c>).</summary>
	public const int FlashPeriod = 300;

	private readonly UiStringTable strings;
	private readonly IThemeParkIncTagTubeWorld world;
	private readonly ThemeParkIncTagTube tube;
	private readonly ThemeParkIncTagTubeSession session;
	private readonly Func<ThemeParkIncTagTubeRow, ThemeParkIncLetterWindow.Letter> compose;
	private readonly Action close;
	private readonly Action<ThemeParkIncTagTubeRow> goTo;
	private readonly List<ThemeParkIncTagTubeRow> listed = new();

	/// <summary>One empty tab-split cell per listed row: <see cref="DrawCell"/> draws the row itself; the list reads only the count.</summary>
	private IReadOnlyList<string> rowCells => cellCache.Count == listed.Count ? cellCache : cellCache = Enumerable.Repeat( "\t", listed.Count ).ToArray();

	private IReadOnlyList<string> cellCache = Array.Empty<string>();
	private readonly Dictionary<ThemeParkIncTagTubeRow, (bool Lit, double Next)> flash = new( ReferenceEqualityComparer.Instance );
	private readonly Dictionary<ThemeParkIncTagTubeRow, ThemeParkIncLetterWindow.Letter> letters = new( ReferenceEqualityComparer.Instance );
	private UiScreen? screen;
	private UiScrollList? list;
	private UiButton? deleteButton, gotoButton;
	private UiLabel? emptyLabel;
	private UiModelImage? mugshot;
	private int seenRevision = -1;
	private bool refreshing, deleting, deletePending;
	private ThemeParkIncTagTubeRow? pendingDelete;

	public ThemeParkIncTagTubeWindow( UiStringTable strings, IThemeParkIncTagTubeWorld world, ThemeParkIncTagTubeSession session, Func<ThemeParkIncTagTubeRow, ThemeParkIncLetterWindow.Letter> compose,
		Action close, Action<ThemeParkIncTagTubeRow> goTo )
	{
		this.strings = strings;
		this.world = world;
		tube = world.Tube;
		this.session = session;
		this.compose = compose;
		this.close = close;
		this.goTo = goTo;
	}

	/// <summary>The window's millisecond clock (<c>0x41B760</c>), advanced by its updates.</summary>
	public double Now { get; private set; }

	/// <summary><c>+0x1C</c>: the selected row (the list's last selection), null for none.</summary>
	public ThemeParkIncTagTubeRow? Selected { get; private set; }

	/// <summary><c>+0x20</c>: the row the details show, null for none.</summary>
	public ThemeParkIncTagTubeRow? Shown { get; private set; }

	/// <summary><c>+0x5C</c>: the shown row's read deadline, 0 for none.</summary>
	public double ReadDeadline { get; private set; }

	/// <summary>The rows in the list's order.</summary>
	public IReadOnlyList<ThemeParkIncTagTubeRow> ListedRows => listed;

	/// <summary>Delete (3004) is enabled.</summary>
	public bool DeleteEnabled => deleteButton?.Enabled ?? false;

	/// <summary>The go-to button (3019) shows.</summary>
	public bool GotoShown => gotoButton?.Visible ?? false;

	/// <summary>The empty-list label (3020) shows.</summary>
	public bool EmptyShown => emptyLabel?.Visible ?? false;

	/// <summary>The details: From, To, Subject and the body of the shown row (empty without one).</summary>
	public (string From, string To, string Subject, string Body) Details { get; private set; } = ("", "", "", "");

	/// <summary>The sender's picture frame the details show, -1 when the picture is hidden.</summary>
	public int Picture { get; private set; } = -1;

	/// <summary>Builds the window over its layout table, then its first refresh.</summary>
	public UiScreen Build()
	{
		var table = ThemeParkIncLayoutTables.TagTube;
		screen = new UiScreen( ScreenName ) { Back = Close };
		// [EXT:ui-font-tier-by-scale] which shipped font serves font slots 6, 7 and 8 (0x500DE0) is OpenTPW's choice: the label role for 6 and 8, the small role for 7, as the mission window
		Func<UiFonts, FontAtlas> slot6 = fonts => fonts.Label, slot7 = fonts => fonts.Small, slot8 = fonts => fonts.Label;
		var white = new RgbaByte( 0xFF, 0xFF, 0xFF, 0xFF );
		UiModelImage Image( string id, uint control, Func<int>? frame = null ) => screen.Add( new UiModelImage
		{
			Id = id,
			Model = table.Control( control ).Model!,
			Frame = frame ?? (() => 0),
			Bounds = table.Control( control ).Rect,
			Anchor = UiAnchor.Center
		} );
		UiLabel Label( string id, UiRect bounds, Func<string> text, Func<UiFonts, FontAtlas> font, RgbaByte colour, UiAlign align = UiAlign.Left, bool wrap = false, bool fit = false ) =>
			screen.Add( new UiLabel { Id = id, Text = text, Font = font, Color = colour, Align = align, Wrap = wrap, Fit = fit, Bounds = bounds, Anchor = UiAnchor.Center } );

		// [BIN:TPI-EXE:0x9BBC84 tube table] the !window 3000 (196,38)-(2028,1318) carries every control; the !f_plain panel 3005
		Image( "window", 0xBB8 );
		Image( "panel", 0xBBD );
		// [BIN:TPI-EXE:0x551DB5 tube title] the title control 3003 (0x501D50, as the letter's) with UITEXT 861
		screen.Add( new UiLabel
		{
			Id = "title",
			Text = () => strings.Entry( TitleText ),
			Font = fonts => fonts.Title,
			Shadow = true,
			ShadowAlpha = UiBatch.TpiTitleCopyAlpha,
			Color = UiColors.Title,
			Fit = true,
			Bounds = table.Control( 0xBBB ).Rect,
			Anchor = UiAnchor.Center
		} );
		// [BIN:TPI-EXE:0x551E14 tube headers] 3007 and 3008 in font slot 8, white (0x84A6BB(0xFF, 0xFF, 0xFF, 0xFF)): UITEXT 860 and 859
		Label( "inbox", table.Control( 0xBBF ).Rect, () => strings.Entry( InboxText ), slot8, white );
		Label( "details", table.Control( 0xBC0 ).Rect, () => strings.Entry( DetailsText ), slot8, white );

		// [BIN:TPI-EXE:0x552650 tube list] the list 3006 (type 7, attribute 0x211: sorted) with a text column 0 and a number column 1 (0x8551BE), the "hilight" bar, the session's sort key
		// (0x857797(+0x68)) and the header texts UITEXT 209 and 210 on its children 16 and 17 (0x503090, font slot 8)
		var listNode = table.Control( 0xBBE );
		var inner = listNode.Inner ?? listNode.Rect;
		var subjectHeader = listNode.Children.First( child => child.Id == 0x10 );
		var dateHeader = listNode.Children.First( child => child.Id == 0x11 );
		Image( "list", 0xBBE );
		list = screen.Add( new UiScrollList
		{
			Id = "list",
			Rows = () => rowCells,
			Columns = new (float, Func<string>)[] { (0, () => ""), (dateHeader.Rect.X - inner.X, () => "") },
			HeaderRow = false,
			Background = false,
			PositionBar = false,
			Margin = 0,
			DrawCell = DrawCell,
			SelectionChanged = Show,
			Bounds = inner,
			Anchor = UiAnchor.Center
		} );
		foreach ( var (header, column, text) in new[] { (subjectHeader, 0, SubjectHeader), (dateHeader, 1, ReceivedHeader) } )
		{
			screen.Add( new UiButton { Id = $"header{column}", Clicked = () => PressHeader( column ), Bounds = header.Rect, Anchor = UiAnchor.Center } );
			Label( $"headerText{column}", header.Rect, () => strings.Entry( text ), slot8, white, UiAlign.Center, fit: true );
		}
		// the list's scroll parts: the arrows scroll one row, the ball shows and sets the first row
		var track = listNode.Children.First( child => child.Type == 3 );
		var ball = track.Children.First( child => child.Model == "b_vscroller" );
		screen.Add( new UiListScrollBar( list, track.Rect, ball.Rect, null, "b_vscroller" ) { Id = "listScroll", Bounds = track.Rect, Anchor = UiAnchor.Center } );
		foreach ( var part in track.Children.Where( child => child.Model is "b_vscrollup" or "b_vscrolldn" ) )
		{
			var up = part.Model == "b_vscrollup";
			screen.Add( new UiButton { Id = up ? "listUp" : "listDown", Model = part.Model, Clicked = () => list.Scroll( up ? -1 : 1 ), Bounds = part.Rect, Anchor = UiAnchor.Center } );
		}

		// [BIN:TPI-EXE:0x551F16 tube detail frame] the !frame 3009 (+0x38): its text centred and wrapped (0x84A577(1, 1, 3)), the colour (0xE1, 0xE1, 0xFF, 0xFF), font slot 7, empty
		var frame = table.Control( 0xBC1 );
		Image( "detailFrame", 0xBC1 );
		Label( "body", frame.Inner ?? frame.Rect, () => Details.Body, slot7, new RgbaByte( 0xE1, 0xE1, 0xFF, 0xFF ), wrap: true );
		// the body's scroll text (0x551AF0 message 0x800 with the slider 3017) is not ported: the body shows from its top, and the slider is drawn without moving it, as in the mission window
		foreach ( var part in table.Control( 0xBC9 ).Children )
			screen.Add( new UiModelImage { Id = "bodyScroll" + part.Model, Model = part.Model!, Bounds = part.Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPI-EXE:0x552012 tube picture] the mugshot 3010 (+0x3C) hidden until a row with a picture is shown
		mugshot = Image( "mugshot", 0xBC2, () => Picture );
		mugshot.Visible = false;
		// [BIN:TPI-EXE:0x55206E tube fields] each label in font slot 6 with the colour (0xEA, 0xEF, 0x66, 0xFF), each value (3016 +0x44, 3012 +0x48, 3014 +0x4C) in font slot 6, empty
		var labelColour = new RgbaByte( 0xEA, 0xEF, 0x66, 0xFF );
		void Field( string id, uint labelControl, int label, uint valueControl, Func<string> value )
		{
			Label( id + "Label", table.Control( labelControl ).Rect, () => strings.Entry( label ), slot6, labelColour );
			Label( id, table.Control( valueControl ).Rect, value, slot6, white, fit: true );
		}
		Field( "from", 0xBC3, FromLabel, 0xBC4, () => Details.From );
		Field( "subject", 0xBC5, SubjectLabel, 0xBC6, () => Details.Subject );
		Field( "to", 0xBC7, ToLabel, 0xBC8, () => Details.To );
		// [BIN:TPI-EXE:0x5522FB tube go-to] the b_goto 3019 (+0x2C, +0x90 = 0xAF) hidden, with the frame's procedure 0x551AF0; its footer 3018 carries no text
		gotoButton = Button( "goto", table.Control( 0xBCB ), PressGoto );
		gotoButton.Visible = false;
		// [BIN:TPI-EXE:0x55232D tube delete] the b_delete 3004 (+0x30) disabled; help 78
		deleteButton = Button( "delete", table.Control( 0xBBC ), PressDelete );
		deleteButton.Enabled = false;
		// [BIN:TPI-EXE:0x5523BF tube empty label] 3020 (+0x40) in font slot 8, white, UITEXT 211, hidden
		emptyLabel = Label( "empty", table.Control( 0xBCC ).Rect, () => strings.Entry( NoMessages ), slot8, white, UiAlign.Center );
		emptyLabel.Visible = false;
		// [BIN:TPI-EXE:0x551977 tube close] the b_close 3001 and the b_okay 3002 read the shown row (0x553480), unregister the window (0x501CD0(0)) and destroy it
		var okay = Button( "ok", table.Control( 0xBBA ), Close );
		Button( "close", table.Control( 0xBB9 ), Close );
		// [BIN:TPI-EXE:0x551A14 tube help] the b_help 3021 calls 0x503A40(0x1AF), the advisor panel's help, which is not ported
		Button( "help", table.Control( 0xBCD ), null );
		Refresh();
		screen.Updating += context => Tick( context.Delta * 1000 );
		screen.Focus( okay );
		return screen;

		UiButton Button( string id, LayoutNode node, Action? clicked ) => screen!.Add( new UiButton
		{
			Id = id,
			Model = node.Model,
			Help = node.Help >= 0 ? strings.Help( node.Help ) : null,
			Clicked = clicked,
			Bounds = node.Rect,
			Anchor = UiAnchor.Center
		} );
	}

	/// <summary>
	/// The window procedure's tick (<c>0x5517B0</c> message 0x1E): a pending delete runs, the shown row is read once its deadline has come, and a changed tube refreshes the list.
	/// </summary>
	// [BIN:TPI-EXE:0x55186D tube tick] +0x59 set: the pending row (+0x24) still listed (0x856917) and unflagged: +0x15 = 1, +0x5C = 0, +0x1C = 0, +0x28 = -1, 0x636A40(row), +0x15 = 0; then
	// +0x59 = 0, +0x24 = 0 and Delete enabled; +0x5C set and <= now: 0x553480(+0x1C); the dirty byte set: the refresh 0x552CF0
	internal void Tick( double milliseconds )
	{
		Now += milliseconds;
		if ( deletePending )
		{
			if ( pendingDelete is { } row && listed.Contains( row ) && !row.Flagged )
			{
				deleting = true;
				ReadDeadline = 0;
				Selected = null;
				world.Delete( row );
				deleting = false;
			}
			deletePending = false;
			pendingDelete = null;
			deleteButton!.Enabled = true;
		}
		if ( ReadDeadline != 0 && ReadDeadline <= Now )
			Read( Selected );
		if ( tube.Revision != seenRevision )
			Refresh();
	}

	/// <summary>
	/// The refresh (<c>0x552CF0</c>): the list is cleared and filled with the tube's unflagged rows in sort order; a non-empty list enables Delete, hides the empty label and scrolls the
	/// previously selected row (or its index, clamped) into view, without selecting it; an empty list disables Delete, shows the empty label and clears the details.
	/// </summary>
	// [BIN:TPI-EXE:0x552CF0 tube refresh] +0x14 set: nothing; dirty byte = 0, +0x14 = 1; the selected item (0x158) is remembered; the list is cleared (0x856517: selection -1, first row 0);
	// 0x636AD0 adds the rows through 0x552C60; count > 0: the remembered row's index (0x856917), else the old index clamped to count - 1, or 0 with no selection; Delete on (+0x30, 1), the empty
	// label hidden, 0x857513(index) scrolls to it; count 0: Delete off, the empty label shown, 0x553550(-1) and list message 0x401(-1, -1); +0x14 = 0
	public void Refresh()
	{
		if ( refreshing || list == null )
			return;
		seenRevision = tube.Revision;
		refreshing = true;
		var oldIndex = list.Selected;
		var oldRow = oldIndex >= 0 && oldIndex < listed.Count ? listed[oldIndex] : null;
		listed.Clear();
		flash.Clear();
		list.Selected = -1;
		list.Scroll( -listed.Count - 1_000_000 );
		foreach ( var row in tube.Listed )
			Add( row );
		if ( listed.Count > 0 )
		{
			var index = 0;
			if ( oldRow != null )
			{
				index = listed.IndexOf( oldRow );
				if ( index == -1 && oldIndex != -1 )
					index = Math.Min( oldIndex, listed.Count - 1 );
			}
			deleteButton!.Enabled = true;
			emptyLabel!.Visible = false;
			if ( index >= 0 )
				list.Scroll( index - list.FirstRow );
		}
		else
		{
			deleteButton!.Enabled = false;
			emptyLabel!.Visible = true;
			// the show runs inside the refresh (+0x14 set), so the read it makes of the shown row does nothing but clear the deadline
			Show( -1 );
		}
		refreshing = false;
	}

	/// <summary>The row add (<c>0x552C60</c>) into the sorted list (<c>0x855B60</c>): before the first row the comparator puts it ahead of, else at the end.</summary>
	// [BIN:TPI-EXE:0x855A01 list comparator] the sort column +0x174 and direction +0x176: a number column (type 1) puts the new item first when it is smaller (ascending) or larger (descending);
	// a text column when the node's text is null or wcscmp (0x8717B8) orders the new text first; equal items keep their insert order
	private void Add( ThemeParkIncTagTubeRow row )
	{
		flash[row] = (false, Now);
		var (column, descending) = SortOf( session.SortKey );
		var at = listed.Count;
		if ( column >= 0 )
			for ( var index = 0; index < listed.Count; index++ )
			{
				var order = column == 1 ? row.Date.CompareTo( listed[index].Date ) : string.CompareOrdinal( Subject( row ), Subject( listed[index] ) );
				if ( descending ? order > 0 : order < 0 )
				{
					at = index;
					break;
				}
			}
		listed.Insert( at, row );
	}

	/// <summary>The sort column and direction of a key (<c>0x857797</c>): the column |key| - 1 (-1: unsorted), descending when the key is negative.</summary>
	public static (int Column, bool Descending) SortOf( int key ) => (Math.Abs( key ) - 1, key < 0);

	/// <summary>
	/// A header press (<c>0x85782C</c>): the same column turns its direction, another column sorts ascending; the list sorts again, the session keeps the key and the selection's index is
	/// announced again (the row now at it is shown).
	/// </summary>
	// [BIN:TPI-EXE:0x857872 header press] the sorted list's header child 0x10 + column: the sort column's own header toggles the direction (0x857735(-1)), another sets direction 0 (0x857735(0))
	// and the column (0x8576F7); each change re-sorts and posts 0x406 with the key (0x8576A1), which 0x5516E0 stores in +0x68; then a selection of 0 or more is announced again (0x401)
	internal void PressHeader( int column )
	{
		var (current, descending) = SortOf( session.SortKey );
		var key = current == column ? (column + 1) * (descending ? 1 : -1) : column + 1;
		session.SortKey = key;
		var rows = listed.ToList();
		listed.Clear();
		foreach ( var row in rows )
			Add( row );
		if ( list is { Selected: >= 0 } selection )
			Show( selection.Selected );
	}

	/// <summary>Selects the row at <paramref name="index"/> in the list, as a click on it does (the list announces it with message 0x401).</summary>
	internal void SelectRow( int index ) => list?.Select( index );

	/// <summary>
	/// A selection (<c>0x553550</c>, list message 0x401): the row at <paramref name="index"/> becomes the selected row; a different row than the shown one fills the details, shows its picture,
	/// reads the previously shown row and becomes the shown row; the read deadline starts.
	/// </summary>
	// [BIN:TPI-EXE:0x553550 tube show] +0x1C = 0, +0x28 = -1; the item's row at the index (0x85685E); +0x1C = row; row != +0x20: 0x553100(row), the mugshot shown at +0xE02 - 1 when +0xE02 != 0
	// (else hidden), 0x553480(+0x20), +0x20 = row; then +0x28 = index and +0x5C = now + +0x60
	internal void Show( int index )
	{
		var row = index >= 0 && index < listed.Count ? listed[index] : null;
		Selected = row;
		if ( !ReferenceEquals( row, Shown ) )
		{
			Fill( row );
			if ( row is { Picture: not 0 } )
			{
				Picture = row.Picture - 1;
				mugshot!.Visible = true;
			}
			else
			{
				Picture = -1;
				mugshot!.Visible = false;
			}
			Read( Shown );
			Shown = row;
		}
		ReadDeadline = Now + ReadDelay;
	}

	/// <summary>The details (<c>0x553100</c>): To, From, Subject and the body of <paramref name="row"/>; the go-to shows when it has a target; Delete is enabled; no row empties them.</summary>
	// [BIN:TPI-EXE:0x553100 tube details] a row: +0x44 = +0xA06, +0x48 = +0x808, +0x4C = +0xC04, the body +0x8 (0x552E30); +0xE10 != 0 shows the go-to 3019 and the footer 3018 and queues
	// advisor message 0x211 (0x71A930, 0x718820), else hides both; Delete on. No row: the three fields "" (0xA0B714), no body, the go-to hidden, Delete off
	// [EXT:tpi-capability-gates] the advisor's message 0x211 (descriptor 0x9E5B80, group 1, response 862) is not posted: Theme Park Inc's advisor queue takes only the forecasts and the letters so far
	private void Fill( ThemeParkIncTagTubeRow? row )
	{
		if ( row == null )
		{
			Details = ("", "", "", "");
			gotoButton!.Visible = false;
			deleteButton!.Enabled = false;
			return;
		}
		var letter = Letter( row );
		Details = (letter.From, letter.To, letter.Subject, letter.Body);
		gotoButton!.Visible = row.GotoKind != 0;
		deleteButton!.Enabled = true;
	}

	/// <summary>
	/// The read (<c>0x553480</c>): with a deadline set and the row shown for longer than half a second (now + 2000 past the deadline), no refresh or delete running and an unread row, the row
	/// is marked read and its category counts down; the deadline is cleared in every case.
	/// </summary>
	// [BIN:TPI-EXE:0x553480 tube read] +0x5C == 0 or now + +0x64 <= +0x5C: +0x5C = 0, nothing; else +0x5C = 0; +0x14 or +0x15 set, no row or -1, or +0xE0C set: nothing; else +0xE0C = 1,
	// 0x636820(+0xE0E), the list redrawn (0x8543D4(-1), 0x854E0A) and +0x58 = 1
	internal void Read( ThemeParkIncTagTubeRow? row )
	{
		if ( ReadDeadline == 0 || Now + ReadGrace <= ReadDeadline )
		{
			ReadDeadline = 0;
			return;
		}
		ReadDeadline = 0;
		if ( refreshing || deleting || row == null )
			return;
		world.Read( row );
	}

	/// <summary>
	/// Delete (command 3004, or the Delete key): Delete is disabled and the selected row waits for the next tick; with no row selected Delete is enabled again at once.
	/// </summary>
	// [BIN:TPI-EXE:0x5519C7 tube delete press] Delete off (+0x30 vtable +0x14(0)); +0x24 = +0x1C; 0 or -1: +0x24 = 0, +0x59 = 0, Delete on; else +0x59 = 1
	// [BIN:TPI-EXE:0x55193D tube delete key] message 0x1000A with 0x2E00 posts command 3004 to the window
	internal void PressDelete()
	{
		deleteButton!.Enabled = false;
		pendingDelete = Selected;
		if ( Selected == null )
		{
			deletePending = false;
			deleteButton.Enabled = true;
		}
		else
			deletePending = true;
	}

	/// <summary>The go-to (3019): the shown row is read, the window closes and the row's go-to runs (<c>0x5511B0</c>).</summary>
	// [BIN:TPI-EXE:0x551B77 tube go-to] 0x553480(+0x1C), 0x501CD0(0), the window destroyed, then 0x5511B0(the list's selected row); a result of 2 posts Delete, which no go-to kind of a ported
	// letter returns (2, 0xB, 0xC and 0x12 all return 0)
	internal void PressGoto()
	{
		if ( list is not { Selected: >= 0 and var index } || index >= listed.Count )
			return;
		var row = listed[index];
		Read( Selected );
		close();
		goTo( row );
	}

	/// <summary>Closes the window: the shown row is read (<c>0x553480</c>) and the window is destroyed.</summary>
	// [BIN:TPI-EXE:0x551838 tube close message] message 5 (the HUD's window closing, 0x5013C0) and the buttons 3001/3002 and command -2: 0x553480(+0x1C), 0x501CD0(0), destroyed
	public void Close()
	{
		Read( Selected );
		close();
	}

	private ThemeParkIncLetterWindow.Letter Letter( ThemeParkIncTagTubeRow row )
	{
		if ( !letters.TryGetValue( row, out var letter ) )
			letters[row] = letter = compose( row );
		return letter;
	}

	/// <summary>A row's subject, the list's column 0 (<c>+0xC04</c>).</summary>
	public string Subject( ThemeParkIncTagTubeRow row ) => Letter( row ).Subject;

	/// <summary>A row's date, the list's column 1, printed as <c>0x515890</c> prints it (UITEXT 956).</summary>
	public string Date( ThemeParkIncTagTubeRow row ) => Letter( row ).Date;

	/// <summary>
	/// A row's text colour (<c>0x5528B0</c>): a read row grey (0xC0), an unread row white, and an unread row of a category above 0 flashing every 300 ms between white and the category's colour
	/// (1 blue, 2 red, 3 green).
	/// </summary>
	// [BIN:TPI-EXE:0x5528B0 tube row colour] message 0x403 (the item's row): +0xE0C set: (0xC0, 0xC0, 0xC0); else white, user words 0 = 0 and 1 = now; message 0x1E: an unread row with +0xE0E > 0
	// and now past word 1: word 0 was 1 gives white, else category 1 (0, 0, 0xFF), 2 (0xFF, 0, 0), 3 (0, 0xFF, 0); word 0 = !word 0, word 1 = now + 300
	public RgbaByte RowColour( ThemeParkIncTagTubeRow row )
	{
		var white = new RgbaByte( 0xFF, 0xFF, 0xFF, 0xFF );
		if ( row.Read )
			return new RgbaByte( 0xC0, 0xC0, 0xC0, 0xFF );
		if ( row.Category <= 0 || !flash.TryGetValue( row, out var state ) )
			return white;
		if ( Now > state.Next )
			flash[row] = state = (!state.Lit, Now + FlashPeriod);
		return !state.Lit ? white : row.Category switch
		{
			1 => new RgbaByte( 0, 0, 0xFF, 0xFF ),
			2 => new RgbaByte( 0xFF, 0, 0, 0xFF ),
			_ => new RgbaByte( 0, 0xFF, 0, 0xFF )
		};
	}

	// [BIN:TPI-EXE:0x552B20 tube cells] column 0 a text in font slot 7 (+0xC), left (0x84A4D2(0, 1, 3)); column 1 the date text class 0x515670 in font slot 7, right (0x84A4D2(2, 1, 3))
	private bool DrawCell( UiContext context, int index, int column, UiRect cell )
	{
		if ( index < 0 || index >= listed.Count )
			return true;
		var row = listed[index];
		// column 0 ends where column 1 starts (its header 16 ends left of header 17)
		var width = column == 0 && list is { Columns: [_, var date] } && list.Bounds.Width > 0 ? cell.Width * date.Offset / list.Bounds.Width : cell.Width;
		context.DrawText( context.Fonts.Small, column == 0 ? Subject( row ) : Date( row ), new UiRect( cell.X, cell.Y, width, cell.Height ), RowColour( row ),
			column == 0 ? UiAlign.Left : UiAlign.Right );
		return true;
	}
}
