using System;
using System.Collections.Generic;
using System.IO;
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
}
