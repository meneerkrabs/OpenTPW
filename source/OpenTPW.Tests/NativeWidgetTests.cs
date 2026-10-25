using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>Text fields and scroll lists of the original-style UI, driven without a GPU.</summary>
[TestClass]
public class NativeWidgetTests
{
	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) )
	{
		Canvas = new UiCanvas( 1024, 768 )
	};

	private static NVector2 Centre( UiContext context, UiElement element ) => element.ScreenRect( context.Canvas ).Center;

	/// <summary>
	/// A focused text field sets <see cref="Input.TextEntryActive"/> in a screen's update, and the game flow clears it each frame: here the cleanup does, for the tests after it (a flag left set
	/// shuts the park input gates of later HUD tests, such as the wave machine window of <see cref="PoolsAttractionsLevelTests"/>).
	/// </summary>
	[TestCleanup]
	public void Cleanup() => Input.TextEntryActive = false;

	[TestMethod]
	public void TypedFolderActionsAreDisabledUntilInspectionCompletes()
	{
		var stack = new UiScreenStack();
		var pending = new System.Threading.Tasks.TaskCompletionSource<InstallationDiscoveryResult>();
		var screen = GameFilesScreen.Create( stack, OriginalUiTests.FakeStrings(), Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString( "N" ), "setup.json" ),
			( _, _ ) => pending.Task );
		stack.Push( screen );
		((UiButton)screen.Find( "gameType" )!).Clicked!();
		var field = (UiTextField)stack.Top!.Find( "path" )!;
		field.Text = "/synthetic/pending";
		field.Submitted!();
		screen.Updating!( Context() );
		Assert.IsFalse( screen.Find( "gameType" )!.Enabled );
		Assert.IsFalse( screen.Find( "cdType" )!.Enabled );
		pending.SetResult( new( Array.Empty<InstallationReport>(), false, "synthetic refusal" ) );
		screen.Updating!( Context() );
		Assert.IsTrue( screen.Find( "gameType" )!.Enabled );
		Assert.IsTrue( screen.Find( "cdType" )!.Enabled );
		stack.Clear();
	}

	[TestMethod]
	public void TextReadbackUsesOnlyTheSelectedDrawGlyphs()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 4, new FontAtlasTests.SyntheticGlyph(
			'A', 2, 3, 0, 0, 3, Enumerable.Repeat( (byte)15, 6 ).ToArray() ) ) );
		var layout = TextLayout.Create( font, "A" );
		var batch = new UiBatch();
		batch.AddText( font, layout, 10, 12, UiColors.Title, text: "underlying" );
		batch.AddText( font, layout, 10, 10, UiColors.Title, text: "selected" );
		var draw = batch.TextDraws.Last();
		Assert.AreEqual( "selected", draw.Text );
		Assert.AreEqual( 1, draw.FirstGlyph );
		Assert.AreEqual( 1, draw.GlyphCount );
		Assert.AreEqual( 10, batch.Glyphs.Skip( draw.FirstGlyph ).Take( draw.GlyphCount ).Single().Y );
		batch.Clear();
		Assert.AreEqual( 0, batch.TextDraws.Count );
	}

	[TestMethod]
	public void GameFolderScreenAlwaysOffersTypedManualRecovery()
	{
		var stack = new UiScreenStack();
		var path = Path.Combine( Path.GetTempPath(), "opentpw-unused-" + Guid.NewGuid().ToString( "N" ), "setup.json" );
		var screen = GameFilesScreen.Create( stack, OriginalUiTests.FakeStrings(), path );
		stack.Push( screen );
		((UiButton)screen.Find( "gameType" )!).Clicked!();
		Assert.AreEqual( "folderEntry", stack.Top!.Name );
		Assert.IsNotNull( stack.Top.Find( "path" ) );
		stack.Pop();
		((UiButton)screen.Find( "cdType" )!).Clicked!();
		Assert.AreEqual( "folderEntry", stack.Top!.Name );
		stack.Clear();
	}

	[TestMethod]
	public void StackRemovalReleasesScreenOwnedPendingWorkOnce()
	{
		var stack = new UiScreenStack();
		var closed = 0;
		stack.Push( new UiScreen( "first" ) { Removed = () => closed++ } );
		stack.Push( new UiScreen( "second" ) { Removed = () => closed++ } );
		stack.Pop();
		Assert.AreEqual( 1, closed );
		stack.Clear();
		Assert.AreEqual( 2, closed );
		stack.Clear();
		stack.Pop();
		Assert.AreEqual( 2, closed );
	}

	[TestMethod]
	public void TextFieldTypesUpToItsLimitAndSubmitsOnEnter()
	{
		var context = Context();
		var screen = new UiScreen( "login" );
		var submitted = 0;
		var name = screen.Add( new UiTextField { Id = "name", Bounds = new UiRect( 100, 100, 600, 80 ), MaximumLength = 16, Submitted = () => submitted++ } );
		screen.Update( context, UiInput.Click( Centre( context, name ) ) );
		screen.Update( context, UiInput.Type( "Alice the park builder" ) );
		Assert.AreEqual( "Alice the park b", name.Text, "The original login fields hold 16 characters." );
		screen.Update( context, UiInput.Type( "", backspaces: 3 ) );
		Assert.AreEqual( "Alice the par", name.Text );
		screen.Update( context, UiInput.Type( "x\u0007y" ) );
		Assert.AreEqual( "Alice the parxy", name.Text, "Control characters are dropped." );

		// Space types a space instead of pressing the focused element.
		screen.Update( context, new UiInput( new NVector2( -1, -1 ), false, false, false, false, UiKeys.Accept | UiKeys.Space, 0, " " ) );
		Assert.AreEqual( "Alice the parxy ", name.Text );
		Assert.AreEqual( 0, submitted );
		screen.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 1, submitted );
	}

	[TestMethod]
	public void FieldKeepsFocusOnHoverAndTabMovesBetweenFields()
	{
		var context = Context();
		var screen = new UiScreen( "login" );
		var name = screen.Add( new UiTextField { Id = "name", Bounds = new UiRect( 100, 100, 600, 80 ) } );
		var button = screen.Add( new UiButton { Id = "go", Bounds = new UiRect( 100, 300, 300, 80 ) } );
		var password = screen.Add( new UiTextField { Id = "password", Bounds = new UiRect( 100, 500, 600, 80 ), Password = true } );
		screen.Update( context, UiInput.Click( Centre( context, name ) ) );
		screen.Update( context, UiInput.Idle( Centre( context, button ) ) );
		Assert.AreSame( name, screen.Focused, "Passing over a button does not steal the typing focus." );
		screen.Update( context, UiInput.Key( UiKeys.Tab ) );
		Assert.AreSame( password, screen.Focused, "Tab skips the button and goes to the next field." );
		screen.Update( context, UiInput.Type( "secret" ) );
		Assert.AreEqual( "secret", password.Text );
		Assert.AreEqual( "", name.Text );
	}

	[TestMethod]
	public void FocusedFieldClaimsTheKeyboardAndStillBacksOutOnRightClick()
	{
		var context = Context();
		var screen = new UiScreen( "login" );
		var backs = 0;
		screen.Back = () => backs++;
		var name = screen.Add( new UiTextField { Id = "name", Bounds = new UiRect( 100, 100, 600, 80 ) } );
		Input.TextEntryActive = false;
		screen.Update( context, UiInput.Click( Centre( context, name ) ) );
		screen.Update( context, UiInput.Type( "wasd" ) );
		Assert.IsTrue( Input.TextEntryActive, "The park camera must not see the typed WASD keys." );
		Assert.AreEqual( "wasd", name.Text );
		screen.Update( context, new UiInput( new NVector2( -1, -1 ), false, false, false, true, UiKeys.None ) );
		Assert.AreEqual( 1, backs, "Right click backs out of a modal screen even while a field has the focus." );
	}

	[TestMethod]
	public void ScrollListSelectsWithClicksKeysAndScrollsWithTheWheel()
	{
		var context = Context();
		var screen = new UiScreen( "parks" );
		var rows = new List<string>();
		for ( var index = 0; index < 30; index++ )
			rows.Add( $"Park {index}" );
		var activated = -1;
		var list = screen.Add( new UiScrollList { Id = "parks", Bounds = new UiRect( 100, 100, 800, 448 ), RowHeight = 40, Margin = 24, Rows = () => rows, RowActivated = index => activated = index } );
		Assert.AreEqual( 10, list.VisibleRows );

		var rect = list.ScreenRect( context.Canvas );
		var scale = rect.Height / list.Bounds.Height;
		var thirdRow = new NVector2( rect.Center.X, rect.Y + (24 + 40 * 2.5f) * scale );
		screen.Update( context, UiInput.Click( thirdRow ) );
		Assert.AreEqual( 2, list.Selected );
		Assert.AreEqual( -1, activated );
		screen.Update( context, UiInput.Click( thirdRow ) );
		Assert.AreEqual( 2, activated, "A second click on the selected row opens it." );

		screen.Update( context, UiInput.Key( UiKeys.Right ) );
		Assert.AreEqual( 3, list.Selected );
		screen.Update( context, new UiInput( rect.Center, false, false, false, false, UiKeys.None, -1 ) );
		Assert.AreEqual( 3, list.RowAt( context.Canvas, thirdRow ), "The wheel scrolled the view by one row." );
	}

	/// <summary>A list with column headers (Find Parks, Other Parks, the outbox) draws its headers in the first row's place and its rows below them.</summary>
	private static UiScrollList ColumnList( List<string> rows, Func<int, int, bool>? cellClicked = null ) => new()
	{
		Id = "columns", Bounds = new UiRect( 100, 100, 800, 448 ), RowHeight = 40, Margin = 24, Rows = () => rows,
		Columns = new (float Offset, Func<string> Text)[] { (24, () => "Name"), (400, () => "Mark") },
		CellClicked = cellClicked,
	};

	[TestMethod]
	public void AListWithColumnHeadersFindsTheRowsDrawnBelowTheHeaders()
	{
		var context = Context();
		var list = ColumnList( new List<string> { "a\t1", "b\t2", "c\t3" } );
		Assert.AreEqual( 9, list.VisibleRows, "the header row takes one of the ten rows' places" );
		var rect = list.ScreenRect( context.Canvas );
		var scale = rect.Height / list.Bounds.Height;
		NVector2 At( float x, float rows ) => new( rect.X + x * scale, rect.Y + (24 + 40 * rows) * scale );
		Assert.AreEqual( -1, list.RowAt( context.Canvas, At( 50, 0.5f ) ), "the header row is no row" );
		Assert.AreEqual( 0, list.RowAt( context.Canvas, At( 50, 1.5f ) ), "the first row is drawn below the headers" );
		Assert.AreEqual( 2, list.RowAt( context.Canvas, At( 50, 3.5f ) ) );
		Assert.AreEqual( -1, list.RowAt( context.Canvas, At( 50, 4.5f ) ), "below the last row" );
		Assert.AreEqual( -1, list.ColumnAt( context.Canvas, At( 10, 1.5f ) ), "left of the first column" );
		Assert.AreEqual( 0, list.ColumnAt( context.Canvas, At( 390, 1.5f ) ) );
		Assert.AreEqual( 1, list.ColumnAt( context.Canvas, At( 410, 1.5f ) ) );
		Assert.AreEqual( -1, new UiScrollList { Bounds = list.Bounds }.ColumnAt( context.Canvas, At( 410, 1.5f ) ), "a list without columns has none" );
	}

	[TestMethod]
	public void ACellThatActsLeavesTheRowDefaultOutAndOtherCellsSelectTheRow()
	{
		var context = Context();
		var screen = new UiScreen( "cells" );
		var clicked = new List<(int Row, int Column)>();
		var list = screen.Add( ColumnList( new List<string> { "a\t1", "b\t2" }, ( row, column ) => { clicked.Add( (row, column) ); return column == 1; } ) );
		var rect = list.ScreenRect( context.Canvas );
		var scale = rect.Height / list.Bounds.Height;
		NVector2 Cell( int row, float x ) => new( rect.X + x * scale, rect.Y + (24 + 40 * (row + 1.5f)) * scale );
		screen.Update( context, UiInput.Click( Cell( 1, 500 ) ) );
		CollectionAssert.AreEqual( new[] { (1, 1) }, clicked );
		Assert.AreEqual( -1, list.Selected, "a cell that acted leaves the select-and-activate default out" );
		screen.Update( context, UiInput.Click( Cell( 1, 100 ) ) );
		CollectionAssert.AreEqual( new[] { (1, 1), (1, 0) }, clicked );
		Assert.AreEqual( 1, list.Selected, "a cell that did not act leaves the click to the default" );
		screen.Update( context, new UiInput( Cell( 0, 500 ), false, false, false, true, UiKeys.None ) );
		Assert.AreEqual( 2, clicked.Count, "a right click is no cell click" );
	}

	private sealed class DrawProbe : UiElement
	{
		public int Draws;
		public override void Draw( UiContext context, bool focused, bool pressed ) => Draws++;
	}

	[TestMethod]
	public void AScreenHiddenWhenCoveredDrawsOnlyWhileItIsOnTop()
	{
		var context = Context();
		var stack = new UiScreenStack();
		DrawProbe Screen( string name, bool hiddenWhenCovered = false )
		{
			var screen = new UiScreen( name ) { HiddenWhenCovered = hiddenWhenCovered };
			stack.Push( screen );
			return screen.Add( new DrawProbe { Bounds = new UiRect( 0, 0, 10, 10 ) } );
		}
		int BackdropVertices()
		{
			context.Batch.Clear();
			stack.Draw( context, false );
			return context.Batch.Draws.Sum( draw => draw.Vertices.Count( vertex => vertex.Color == UiColors.Backdrop ) );
		}
		var below = Screen( "below" );
		var menu = Screen( "menu", hiddenWhenCovered: true );
		var oneBackdrop = BackdropVertices();
		Assert.IsTrue( oneBackdrop > 0, "the menu dims the screen below it" );
		Assert.AreEqual( (1, 1), (below.Draws, menu.Draws), "on top the menu draws" );
		var window = Screen( "window" );
		Assert.AreEqual( oneBackdrop, BackdropVertices(), "the window dims once: the menu it covers adds no backdrop of its own" );
		Assert.AreEqual( (2, 1, 1), (below.Draws, menu.Draws, window.Draws), "a window over the menu takes its place; the screens below still draw" );
		stack.Pop();
		BackdropVertices();
		Assert.AreEqual( (3, 2), (below.Draws, menu.Draws), "back on top the menu draws again" );
	}
}
