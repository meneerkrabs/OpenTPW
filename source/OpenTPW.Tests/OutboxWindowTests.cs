using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

	private static string Marks( UiScreen screen, string title ) => Row( screen, title ).Cells[2] + "|" + Row( screen, title ).Cells[3];

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
		Assert.AreEqual( "list_outbox", Cards( screen ).Model );
		Assert.AreEqual( table.Control( 0xEE8 ).Rect, Cards( screen ).Bounds );
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
		var columns = Cards( screen ).Columns!;
		CollectionAssert.AreEqual( new[] { OnlineStrings.Ui( OnlineStrings.Title, "Title" ), OnlineStrings.Ui( OnlineStrings.DateLabel, "Date" ),
			OnlineStrings.Ui( OnlineStrings.Send, "Send" ), OnlineStrings.Ui( OnlineStrings.Delete, "Delete" ) }, columns.Select( column => column.Text() ).ToArray() );
		CollectionAssert.AreEqual( new[] { 816f - 793, 1194f - 793, 1491f - 793, 1608f - 793 }, columns.Select( column => column.Offset ).ToArray(), "the header buttons 16-19 of the table" );
		Assert.AreEqual( "Hello", Row( screen, "Hello" ).Cells[0], "the Title column holds the card's title, not its addresses" );
	} );

	[TestMethod]
	public void EveryCardStartsMarkedForSendingAndItsCellsToggleTheMarks() => WithScreens( ( screens, session, stack ) =>
	{
		Store( session, "First" );
		Store( session, "Second" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		var list = Cards( screen );
		Assert.AreEqual( "X|", Marks( screen, "First" ), "the builder adds every row with the send mark" );
		Assert.AreEqual( "X|", Marks( screen, "Second" ) );
		var first = Row( screen, "First" ).Index;
		Assert.IsFalse( list.CellClicked!( first, 0 ), "the title cell leaves the click to the list" );
		Assert.IsTrue( list.CellClicked( first, 3 ) );
		Assert.AreEqual( "|X", Marks( screen, "First" ), "the delete mark replaces the send mark" );
		Assert.AreEqual( first, list.Selected, "the clicked row becomes the current one" );
		CollectionAssert.AreEqual( new[] { "First@example.test", "Bob" }, ((UiScrollList)screen.Find( "addresses" )!).Rows().ToArray(), "the current card's addresses" );
		Assert.IsTrue( list.CellClicked( first, 3 ) );
		Assert.AreEqual( "|", Marks( screen, "First" ), "a second click takes the mark off" );
		Assert.IsTrue( list.CellClicked( first, 2 ) );
		Assert.AreEqual( "X|", Marks( screen, "First" ) );
		Assert.IsTrue( list.CellClicked( Row( screen, "Second" ).Index, 2 ) );
		Assert.AreEqual( "|", Marks( screen, "Second" ) );
	} );

	[TestMethod]
	public void AClickOnARowsDeleteCellMarksThatRow() => WithScreens( ( screens, session, stack ) =>
	{
		Store( session, "Only" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		var context = new UiContext( Strings, null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) };
		var list = Cards( screen );
		var rect = list.ScreenRect( context.Canvas );
		var scale = rect.Height / list.Bounds.Height;
		// the first row lies under the header row: the list's margin, then one row of headers
		var point = new NVector2( rect.X + (1608 - 793 + 20) * scale, rect.Y + (list.Margin + list.RowHeight * 1.5f) * scale );
		Assert.AreEqual( 0, list.RowAt( context.Canvas, point ) );
		Assert.AreEqual( 3, list.ColumnAt( context.Canvas, point ) );
		stack.Update( context, UiInput.Click( point ) );
		Assert.AreEqual( "|X", Marks( screen, "Only" ) );
		Assert.AreEqual( -1, list.RowAt( context.Canvas, new NVector2( point.X, rect.Y + (list.Margin + list.RowHeight * 0.5f) * scale ) ), "the header row is no card" );
	} );

	[TestMethod]
	public void TheBulkButtonsMarkEveryCard() => WithScreens( ( screens, session, stack ) =>
	{
		Store( session, "First" );
		Store( session, "Second" );
		screens.ShowOutbox();
		var screen = stack.Top!;
		((UiButton)screen.Find( "markAllDelete" )!).Clicked!();
		Assert.AreEqual( "|X", Marks( screen, "First" ) );
		Assert.AreEqual( "|X", Marks( screen, "Second" ) );
		((UiButton)screen.Find( "unmarkAll" )!).Clicked!();
		Assert.AreEqual( "|", Marks( screen, "First" ) );
		Assert.AreEqual( "|", Marks( screen, "Second" ) );
		((UiButton)screen.Find( "markAllSend" )!).Clicked!();
		Assert.AreEqual( "X|", Marks( screen, "First" ) );
		Assert.AreEqual( "X|", Marks( screen, "Second" ) );
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
		Assert.AreEqual( "X|", Marks( screen, "Sent" ), "the card keeps its send mark for the next tick" );
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
		Assert.AreEqual( session.Status, ((UiLabel)menu.Find( "status" )!).Text(), "the tick's own outcome replaces the earlier window's message" );
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
