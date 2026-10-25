using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Online.Packages;
using OpenTPW.UI;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>Unsent Postcards in tp.exe's outbox window, table 0x758878 (built by 0x4bc710, handled by 0x4bbc20).</summary>
[TestClass, DoNotParallelize]
public class OutboxWindowTests
{
	private static readonly UiStringTable Strings = OriginalUiTests.FakeStrings();

	private static void WithScreens( Action<OnlineScreens, OnlineSession, UiScreenStack> test )
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-outbox-window-" ).FullName;
		var previousFiles = OpenTPW.Common.GlobalNamespace.FileSystem;
		try
		{
			OpenTPW.Common.GlobalNamespace.FileSystem = new BaseFileSystem( root );
			var folders = new OnlineFolders( root );
			folders.SaveSettings( new OnlineSettings( null, "sender" ) );
			using var session = new OnlineSession( folders );
			var stack = new UiScreenStack();
			stack.Push( new UiScreen( "hud" ) );
			var screens = new OnlineScreens( stack, Strings, new UiModels( name => throw new FileNotFoundException( name ) ), new OnlineHost { Session = session, Level = () => null } );
			test( screens, session, stack );
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previousFiles; Directory.Delete( root, recursive: true ); }
	}

	private static string Store( OnlineSession session, string title ) =>
		session.Folders.Store( session.Folders.Outbox, Postcard.Create( "sender", new[] { $"{title}@example.test", "Bob" }, title, "Greetings", "English" ) );

	private static UiScrollList Cards( UiScreen screen ) => (UiScrollList)screen.Find( "cards" )!;

	/// <summary>The row of the card titled <paramref name="title"/>, split into its Title/Date/Send/Delete cells.</summary>
	private static (int Index, string[] Cells) Row( UiScreen screen, string title )
	{
		var rows = Cards( screen ).Rows();
		var index = Enumerable.Range( 0, rows.Count ).Single( row => rows[row].Split( '\t' )[0] == title );
		return (index, rows[index].Split( '\t' ));
	}

	/// <summary>The Send and Delete cells of a row: the type-6 cells' values, 1 for a set mark and 0 for none (tp.exe 0x4bbfbc).</summary>
	private static string Marks( UiScreen screen, string title ) => Row( screen, title ).Cells[2] + "|" + Row( screen, title ).Cells[3];
	private const string Send = "1|0", Delete = "0|1", None = "0|0";

	private static UiContext Context( UiModels? models = null, UiFonts? fonts = null ) =>
		new( Strings, fonts!, models ?? new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = name => name };

	/// <summary>Fonts of two synthetic glyphs, 'W' and the space, each 10 pixels wide.</summary>
	private static UiFonts Fonts()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'W', 10, 6, 0, 0, 10 ), new FontAtlasTests.SyntheticGlyph( ' ', 0, 0, 0, 0, 10 ) ) );
		return new UiFonts( _ => font );
	}

	[TestMethod]
	public void TheWindowFollowsTheTableAndKeepsEveryControlInsideIt() => WithScreens( ( screens, _, stack ) =>
	{
		screens.ShowOutbox();
		var screen = stack.Top!;
		Assert.AreEqual( "outbox", screen.Name );
		var table = OriginalLayoutTables.OutboxWindow;
		Assert.AreEqual( "window4", ((UiModelImage)screen.Find( "window" )!).Model );
		Assert.AreEqual( table.Control( 0xEE9 ).Rect, screen.Find( "title" )!.Bounds );
		Assert.AreEqual( "!frame", ((UiModelImage)screen.Find( "picture" )!).Model );
		Assert.AreEqual( table.Control( 0xEE4 ).Rect, screen.Find( "picture" )!.Bounds );
		Assert.AreEqual( "list_emailaddr", ((UiModelImage)screen.Find( "addressFrame" )!).Model );
		Assert.AreEqual( table.Control( 0xEE2 ).Inner, screen.Find( "addresses" )!.Bounds, "the addresses sit inside the frame's inner rectangle, clear of its slider" );
		Assert.AreEqual( "list_outbox", ((UiModelImage)screen.Find( "listFrame" )!).Model );
		Assert.AreEqual( table.Control( 0xEE8 ).Rect, screen.Find( "listFrame" )!.Bounds );
		Assert.AreEqual( table.Control( 0xEE8 ).Inner, Cards( screen ).Bounds, "the rows fill the list's content rectangle, below the header buttons" );
		Assert.IsFalse( Cards( screen ).Background, "the frame art is drawn once, by the list frame" );
		for ( var column = 0; column < 4; column++ )
		{
			var header = (UiLabel)screen.Find( $"header{column}" )!;
			Assert.AreEqual( table.Control( (uint)(0x10 + column) ).Rect, header.Bounds, "the header buttons 16-19 keep their rectangles" );
			Assert.IsNull( header.Help, "the help texts 391-394 describe the sort, which is not ported yet" );
		}
		var slider = table.Control( 0xEE8 ).Children.Single( node => node.Type == 3 );
		Assert.AreEqual( slider.Rect, screen.Find( "cardsScroll" )!.Bounds );
		Assert.AreEqual( "b_up", ((UiButton)screen.Find( "cardsUp" )!).Model );
		Assert.AreEqual( "b_down", ((UiButton)screen.Find( "cardsDown" )!).Model );
		foreach ( var (id, control, model, help) in new[] { ("markAllSend", 0xEE5u, "b_okall", 397), ("markAllDelete", 0xEE6u, "b_delall", 398), ("unmarkAll", 0xEE7u, "b_nuall", 399) } )
		{
			var button = (UiButton)screen.Find( id )!;
			Assert.AreEqual( model, button.Model, $"{id} draws the original art, not a captioned button" );
			Assert.AreEqual( Strings.Help( help ), button.Help );
			Assert.AreEqual( table.Control( control ).Rect, button.Bounds );
			Assert.IsNull( button.Text );
		}
		var ok = (UiButton)screen.Find( "ok" )!;
		Assert.AreEqual( "b_okay", ok.Model, "the corner button is the tick (-1), help 400 'Left-click to continue'" );
		Assert.AreEqual( Strings.Help( 400 ), ok.Help );
		Assert.AreEqual( table.Model( "b_okay" ).Rect, ok.Bounds );
		var window = table.Root.Rect;
		foreach ( var element in screen.Elements )
			Assert.IsTrue( element.Bounds.X >= window.X && element.Bounds.Y >= window.Y && element.Bounds.Right <= window.Right && element.Bounds.Bottom <= window.Bottom,
				$"{element.Id} {element.Bounds} lies inside the window {window}" );
		foreach ( var gone in new[] { "status", "close", "sendAll", "delete", "markSend", "markDelete", "markAllSendCaption" } )
			Assert.IsNull( screen.Find( gone ), $"{gone} is not part of the original window" );
	} );

	[TestMethod]
	public void TheColumnsAreTheCardsTitleAndDateWithTheOriginalHeaders() => WithScreens( ( screens, session, stack ) =>
	{
		Store( session, "Hello" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		var list = Cards( screen );
		CollectionAssert.AreEqual( new[] { OnlineStrings.Ui( OnlineStrings.Title, "Title" ), OnlineStrings.Ui( OnlineStrings.DateLabel, "Date" ),
			OnlineStrings.Ui( OnlineStrings.Send, "Send" ), OnlineStrings.Ui( OnlineStrings.Delete, "Delete" ) },
			Enumerable.Range( 0, 4 ).Select( column => ((UiLabel)screen.Find( $"header{column}" )!).Text() ).ToArray() );
		Assert.IsFalse( list.HeaderRow, "the header buttons stand above the rows; the list draws no header row of its own" );
		CollectionAssert.AreEqual( new[] { 0f, 1194f - 826, 1491f - 826, 1608f - 826 }, list.Columns!.Select( column => column.Offset ).ToArray(),
			"the title is the row's own text from its left; the other cells start at the header buttons 17-19" );
		Assert.AreEqual( ParkHud.BuyRowHeight, list.RowHeight, "the row factory 0x4bcbc0 has the buy list's rule, so the rows take its pitch" );
		Assert.AreEqual( 14, list.VisibleRows, "(904 - 294) / 41.4 rows" );
		var row = Row( screen, "Hello" ).Cells;
		Assert.AreEqual( "Hello", row[0], "the Title column holds the card's title, not its addresses" );
		var created = session.Folders.List( session.Folders.Outbox ).Single().Card!.Manifest.CreatedUtc.LocalDateTime;
		Assert.AreEqual( Strings.FormatByType( UiTextRecord.SaveDate, new Dictionary<int, string> { [16] = created.Day.ToString(), [17] = created.Month.ToString(), [18] = created.Year.ToString() } ),
			row[1], "the date is UITEXT 447 with the card's day, month and year, as the save list writes it" );
	} );

	[TestMethod]
	public void EveryCardStartsMarkedForSendingAndItsCellsToggleTheMarks() => WithScreens( ( screens, session, stack ) =>
	{
		Store( session, "First" );
		Store( session, "Second" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		var list = Cards( screen );
		Assert.AreEqual( Send, Marks( screen, "First" ), "the builder adds every row with the send mark" );
		Assert.AreEqual( Send, Marks( screen, "Second" ) );
		var first = Row( screen, "First" ).Index;
		Assert.IsFalse( list.CellClicked!( first, 0 ), "the title cell leaves the click to the list" );
		Assert.IsTrue( list.CellClicked( first, 3 ) );
		Assert.AreEqual( Delete, Marks( screen, "First" ), "the delete mark replaces the send mark" );
		Assert.AreEqual( first, list.Selected, "the clicked row becomes the current one" );
		CollectionAssert.AreEqual( new[] { "First@example.test", "Bob" }, ((UiScrollList)screen.Find( "addresses" )!).Rows().ToArray(), "the current card's addresses" );
		Assert.IsTrue( list.CellClicked( first, 3 ) );
		Assert.AreEqual( None, Marks( screen, "First" ), "a second click takes the mark off" );
		Assert.IsTrue( list.CellClicked( first, 2 ) );
		Assert.AreEqual( Send, Marks( screen, "First" ) );
		Assert.IsTrue( list.CellClicked( Row( screen, "Second" ).Index, 2 ) );
		Assert.AreEqual( None, Marks( screen, "Second" ) );
	} );

	[TestMethod]
	public void AClickOnARowsDeleteCellMarksThatRow() => WithScreens( ( screens, session, stack ) =>
	{
		Store( session, "Only" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		var context = Context();
		var list = Cards( screen );
		var rect = list.ScreenRect( context.Canvas );
		var scale = rect.Height / list.Bounds.Height;
		// the first row starts at the top of the content rectangle, below the header buttons
		var point = new NVector2( rect.X + (1608 - 826 + 20) * scale, rect.Y + list.RowHeight * 0.5f * scale );
		Assert.AreEqual( 0, list.RowAt( context.Canvas, point ) );
		Assert.AreEqual( 3, list.ColumnAt( context.Canvas, point ) );
		stack.Update( context, UiInput.Click( point ) );
		Assert.AreEqual( Delete, Marks( screen, "Only" ) );
		// the strip right of the content holds the list's slider: a click there is no Delete cell
		var strip = new NVector2( rect.X + (1760 - 826) * scale, point.Y );
		Assert.AreEqual( -1, list.ColumnAt( context.Canvas, strip ) );
		stack.Update( context, UiInput.Click( strip ) );
		Assert.AreEqual( Delete, Marks( screen, "Only" ), "a click on the slider strip leaves the marks alone" );
	} );

	[TestMethod]
	public void TheBulkButtonsMarkEveryCard() => WithScreens( ( screens, session, stack ) =>
	{
		Store( session, "First" );
		Store( session, "Second" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		((UiButton)screen.Find( "markAllDelete" )!).Clicked!();
		Assert.AreEqual( Delete, Marks( screen, "First" ) );
		Assert.AreEqual( Delete, Marks( screen, "Second" ) );
		((UiButton)screen.Find( "unmarkAll" )!).Clicked!();
		Assert.AreEqual( None, Marks( screen, "First" ) );
		Assert.AreEqual( None, Marks( screen, "Second" ) );
		((UiButton)screen.Find( "markAllSend" )!).Clicked!();
		Assert.AreEqual( Send, Marks( screen, "First" ) );
		Assert.AreEqual( Send, Marks( screen, "Second" ) );
	} );

	[TestMethod]
	public void TheTickDeletesTheMarkedCardsAndClosesWithNothingToSend() => WithScreens( ( screens, session, stack ) =>
	{
		var deleted = Store( session, "Deleted" );
		var kept = Store( session, "Kept" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		var list = Cards( screen );
		list.CellClicked!( Row( screen, "Deleted" ).Index, 3 );
		list.CellClicked( Row( screen, "Kept" ).Index, 2 );
		((UiButton)screen.Find( "ok" )!).Clicked!();
		Assert.AreEqual( "hud", stack.Top!.Name, "the tick closes the window" );
		Assert.IsFalse( File.Exists( deleted ), "the card marked for deletion is deleted" );
		Assert.IsTrue( File.Exists( kept ), "an unmarked card stays in the outbox" );
	} );

	[TestMethod]
	public void TheTickAsksForALoginBeforeItSendsAndKeepsTheWindow() => WithScreens( ( screens, session, stack ) =>
	{
		var deleted = Store( session, "Deleted" );
		var sent = Store( session, "Sent" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		Cards( screen ).CellClicked!( Row( screen, "Deleted" ).Index, 3 );
		((UiButton)screen.Find( "ok" )!).Clicked!();
		Assert.IsFalse( session.IsLoggedIn );
		Assert.AreEqual( "onlineLogin", stack.Top!.Name, "a card to send needs the login: OpenTPW's server stands where the original checked the account's service setting" );
		Assert.IsTrue( stack.Screens.Contains( screen ), "the outbox stays under the login dialog" );
		Assert.IsFalse( File.Exists( deleted ), "the marks are applied first, deletions included" );
		Assert.IsTrue( File.Exists( sent ) );
		Assert.AreEqual( Send, Marks( screen, "Sent" ), "the card keeps its send mark for the next tick" );
	} );

	[TestMethod]
	public void TheOnlineMenuGivesWayToTheOutboxOpenedFromIt() => WithScreens( ( screens, session, stack ) =>
	{
		Store( session, "Card" );
		screens.ShowMenu();
		var menu = stack.Top!;
		Assert.IsTrue( menu.HiddenWhenCovered, "the menu's buttons do not show around a window opened from it" );
		// a message an earlier window left on the menu's status line
		typeof( OnlineScreens ).GetField( "localStatus", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( screens, "Saved to an earlier place" );
		((UiButton)menu.Find( "outbox" )!).Clicked!();
		var screen = stack.Top!;
		Assert.AreEqual( "outbox", screen.Name );
		((UiButton)screen.Find( "unmarkAll" )!).Clicked!();
		((UiButton)screen.Find( "ok" )!).Clicked!();
		Assert.AreSame( menu, stack.Top, "with nothing marked the tick closes the window" );
		var status = (UiLabel)menu.Find( "status" )!;
		// with nothing to report the line shows what it shows idle (here why Publish park is greyed)
		var idle = status.Text();
		Assert.IsFalse( idle.Contains( "Saved to an earlier place" ), "the tick's own outcome replaces the earlier window's message" );
		// while a request runs, the status line shows the progress text it set (the original's progress message), then its outcome
		var release = new System.Threading.Tasks.TaskCompletionSource<string?>();
		session.Run( () => release.Task );
		Assert.AreEqual( "...", status.Text(), "no progress text yet" );
		typeof( OnlineSession ).GetMethod( "SetProgress", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( session, new object[] { "Sending mails from outbox, please wait ..." } );
		Assert.AreEqual( "Sending mails from outbox, please wait ...", status.Text() );
		release.SetResult( "" );
		var until = DateTime.UtcNow + TimeSpan.FromSeconds( 10 );
		while ( session.Busy > 0 && DateTime.UtcNow < until )
			session.Pump();
		Assert.AreEqual( 0, session.Busy );
		Assert.AreEqual( idle, status.Text(), "the send's progress closes with no count (0x4bc660): the line has nothing more to report" );
	} );

	[TestMethod]
	public void TheMarksDrawTheOriginalTickBoxInTheirColumns() => WithScreens( ( screens, session, stack ) =>
	{
		Store( session, "First" );
		Store( session, "Second" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		var list = Cards( screen );
		var first = Row( screen, "First" ).Index;
		var second = Row( screen, "Second" ).Index;
		list.CellClicked!( first, 3 );
		var context = Context( TickBoxModels() );
		context.Batch.Clear();
		Assert.IsTrue( list.DrawCell!( context, first, 2, CellRect( list, context, first, 2 ) ) );
		Assert.IsTrue( list.DrawCell( context, first, 3, CellRect( list, context, first, 3 ) ) );
		Assert.IsTrue( list.DrawCell( context, second, 3, CellRect( list, context, second, 3 ) ) );
		var draws = context.Batch.Draws.Where( draw => draw.Texture.ImagePath?.StartsWith( "frame-", StringComparison.Ordinal ) == true ).ToArray();
		CollectionAssert.AreEqual( new[] { "frame-0", "frame-1", "frame-0" }, draws.Select( draw => draw.Texture.ImagePath ).ToArray(),
			"i_boxtick frame 0 is the empty box of an unset mark, frame 1 the tick of a set one (0x66da4d): First is marked for deletion, Second still for sending" );
		// each box keeps the model's 51-unit size, centred in its header's column and in its row
		foreach ( var (draw, header) in new[] { (draws[0], 0x12u), (draws[1], 0x13u) } )
		{
			var xs = draw.Vertices.Select( vertex => vertex.Position.X ).ToArray();
			var ys = draw.Vertices.Select( vertex => vertex.Position.Y ).ToArray();
			var column = OriginalLayoutTables.OutboxWindow.Control( header ).Rect;
			Assert.AreEqual( 51f, xs.Max() - xs.Min(), 0.01f );
			Assert.AreEqual( column.X + column.Width / 2, (xs.Max() + xs.Min()) / 2, 0.01f );
			Assert.AreEqual( 294 + (first + 0.5f) * ParkHud.BuyRowHeight, (ys.Max() + ys.Min()) / 2, 0.01f );
		}
	} );

	[TestMethod]
	public void ALongTitleStaysInsideItsColumn() => WithScreens( ( screens, session, stack ) =>
	{
		// eight seven-letter words: 63 characters, 630 pixels on one line at this scale
		var title = string.Join( ' ', Enumerable.Repeat( "WWWWWWW", 8 ) );
		session.Folders.Store( session.Folders.Outbox, Postcard.Create( "sender", new[] { "Bob" }, title, "Greetings", "English" ) );
		screens.ShowOutbox();
		var list = Cards( stack.Top! );
		var context = Context( fonts: Fonts() );
		context.Batch.Clear();
		Assert.IsTrue( list.DrawCell!( context, 0, 0, CellRect( list, context, 0, 0 ) ) );
		var glyphs = context.Batch.Glyphs;
		Assert.IsTrue( glyphs.Count > 0, "the title is drawn" );
		var titleColumnEnd = list.ScreenRect( context.Canvas ).X + (1194 - 826);
		Assert.IsTrue( glyphs.Max( glyph => glyph.X + glyph.Width ) <= titleColumnEnd + 0.5f,
			"a long title is fitted into its column, as the buy list fits its names, instead of running over the date and the marks" );
	} );

	[TestMethod]
	public void TheSliderAndItsArrowsShowOnlyWhileTheRowsDoNotFit() => WithScreens( ( screens, session, stack ) =>
	{
		for ( var card = 0; card < 14; card++ )
			Store( session, $"Card{card}" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		var scrollers = new[] { "cardsScroll", "cardsUp", "cardsDown" };
		Assert.IsTrue( scrollers.All( id => !screen.Find( id )!.Visible ), "fourteen cards fit" );
		var marked = Row( screen, "Card3" ).Index;
		Cards( screen ).CellClicked!( marked, 3 );
		// a card that arrives while the window is open (the outbox is re-read every second) brings the slider with the next frame
		Store( session, "Card14" );
		var context = Context();
		context.Delta = 1.5f;
		screen.Updating!( context );
		Assert.AreEqual( 15, Cards( screen ).Rows().Count );
		Assert.IsTrue( scrollers.All( id => screen.Find( id )!.Visible ), "the fifteenth does not fit (0x664495)" );
		Assert.AreEqual( Delete, Marks( screen, "Card3" ), "a mark keeps to its card across the re-read" );
		((UiButton)screen.Find( "cardsDown" )!).Clicked!();
		Assert.AreEqual( 1, Cards( screen ).FirstRow );
	} );

	[TestMethod]
	public void EscapeClosesWithoutApplyingTheMarks() => WithScreens( ( screens, session, stack ) =>
	{
		var card = Store( session, "Card" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		Cards( screen ).CellClicked!( Row( screen, "Card" ).Index, 3 );
		screen.Back!();
		Assert.AreEqual( "hud", stack.Top!.Name );
		Assert.IsTrue( File.Exists( card ), "Escape applies nothing" );
	} );

	/// <summary>A cell as the list lays it out: the column's offset from the content's left, the row's band.</summary>
	private static UiRect CellRect( UiScrollList list, UiContext context, int row, int column )
	{
		var rect = list.ScreenRect( context.Canvas );
		var scale = rect.Height / list.Bounds.Height;
		return new UiRect( rect.X + list.Columns![column].Offset * scale, rect.Y + (row - list.FirstRow) * list.RowHeight * scale,
			(list.Bounds.Width - list.Columns[column].Offset) * scale, list.RowHeight * scale );
	}

	/// <summary>A synthetic i_boxtick: three 51-unit frames whose parts name their frame, so a draw tells which frame it is.</summary>
	private static UiModels TickBoxModels() => new( name =>
	{
		Assert.AreEqual( "i_boxtick", name );
		var model = new UiModel( new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) ), name );
		var vertices = new[] { new UiModelVertex( NVector2.Zero, 0, NVector2.Zero ), new UiModelVertex( new NVector2( 51, 0 ), 0, NVector2.Zero ), new UiModelVertex( new NVector2( 0, 51 ), 0, NVector2.Zero ) };
		typeof( UiModel ).GetField( "<Frames>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( model, Enumerable.Range( 0, 3 )
			.Select( frame => new UiModelFrame( $"synthetic-{frame}", new[] { new UiModelPart( $"frame-{frame}", vertices, new[] { 0, 1, 2 } ) }, 0, 0, 51, 51 ) ).ToArray() );
		return model;
	} );

	[TestMethod]
	public void ACellClickTogglesOneMarkAndGivesTheOtherWay()
	{
		Assert.AreEqual( OutboxMark.None, OnlineScreens.ToggledMark( OutboxMark.Send, OutboxMark.Send ) );
		Assert.AreEqual( OutboxMark.Delete, OnlineScreens.ToggledMark( OutboxMark.Send, OutboxMark.Delete ) );
		Assert.AreEqual( OutboxMark.Send, OnlineScreens.ToggledMark( OutboxMark.Delete, OutboxMark.Send ) );
		Assert.AreEqual( OutboxMark.Send, OnlineScreens.ToggledMark( OutboxMark.None, OutboxMark.Send ) );
		Assert.AreEqual( OutboxMark.None, OnlineScreens.ToggledMark( OutboxMark.Delete, OutboxMark.Delete ) );
	}
}
