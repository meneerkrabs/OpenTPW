using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The input rules of the original's list control (dispatcher 0x665c35, mouse input 0x65f6d1): a list with attribute 0x80 selects the hovered
/// row, a left click posts 0x400, a right click 0x402, and a release is a click only within 500 ms of its press and 6 px of movement.
/// </summary>
[TestClass]
public class UiListInputTests
{
	private sealed class FakeList : UiElement, IUiRowList
	{
		public readonly List<string> Log = new();
		public bool HoverSelectsRows = true;
		public bool HandleRight = true;
		public override bool Focusable => true;
		public int Selected { get; private set; } = -1;
		public bool ArrowsSelect => true;
		public bool HoverSelects => HoverSelectsRows;
		public void Select( int index ) { Selected = index; Log.Add( $"select{index}" ); }
		public int Hovered = -1;
		public void Hover( int index ) => Hovered = index;
		public void Scroll( int rows ) { }
		public int RowAt( UiCanvas canvas, System.Numerics.Vector2 point )
		{
			var rect = ScreenRect( canvas );
			return rect.Contains( point ) ? (int)((point.Y - rect.Y) / (rect.Height / 4)) : -1;
		}
		public bool RowClicked( int index, bool left )
		{
			Log.Add( left ? $"left{index}" : $"right{index}" );
			return left || HandleRight;
		}
		public override void Draw( UiContext context, bool focused, bool pressed ) { }
		public override void Activate() => Log.Add( "activate" );
	}

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) )
	{ Canvas = new UiCanvas( 1024, 768 ) };

	private static (UiScreen Screen, FakeList List, UiContext Context, Func<int, System.Numerics.Vector2> Row, List<string> Backs) Build()
	{
		var context = Context();
		var screen = new UiScreen( "test" );
		var backs = new List<string>();
		screen.Back = () => backs.Add( "back" );
		var list = screen.Add( new FakeList { Bounds = new UiRect( 100, 100, 400, 400 ) } );
		var rect = list.ScreenRect( context.Canvas );
		return (screen, list, context, row => new System.Numerics.Vector2( rect.X + 10, rect.Y + (row + 0.5f) * rect.Height / 4 ), backs);
	}

	[TestMethod]
	public void HoveringOnlyHighlightsAndNeverChangesTheSelection()
	{
		var (screen, list, context, row, _) = Build();
		list.Select( 0 );
		screen.Update( context, UiInput.Idle( row( 2 ) ) );
		Assert.AreEqual( 2, list.Hovered, "the pointer highlights the row under it" );
		Assert.AreEqual( 0, list.Selected, "buttons beside the list keep acting on the selected row" );
		screen.Update( context, UiInput.Idle( new System.Numerics.Vector2( 900, 700 ) ) );
		Assert.AreEqual( -1, list.Hovered, "leaving the list clears the highlight" );
	}

	[TestMethod]
	public void AListWithoutAttribute0x80DoesNotSelectOnHover()
	{
		var (screen, list, context, row, _) = Build();
		list.HoverSelectsRows = false;
		screen.Update( context, UiInput.Idle( row( 1 ) ) );
		Assert.AreEqual( -1, list.Hovered );
	}

	[TestMethod]
	public void ALeftClickPostsTheRowClickAndSkipsTheDefaultSelectAndActivate()
	{
		var (screen, list, context, row, _) = Build();
		screen.Update( context, new UiInput( row( 1 ), true, true, false, false, UiKeys.None ) );
		screen.Update( context, UiInput.Click( row( 1 ) ) );
		CollectionAssert.Contains( list.Log, "left1" );
		CollectionAssert.DoesNotContain( list.Log, "activate", "the list handled the click itself" );
	}

	[TestMethod]
	public void AReleaseThatIsNotAClickDoesNothing()
	{
		var (screen, list, context, row, _) = Build();
		screen.Update( context, new UiInput( row( 1 ), true, true, false, false, UiKeys.None ) );
		list.Log.Clear();
		screen.Update( context, UiInput.Click( row( 1 ) ) with { NotClick = true } );
		CollectionAssert.DoesNotContain( list.Log, "left1", "a press longer than 500 ms or moved beyond 6 px is no click" );
	}

	[TestMethod]
	public void ARightClickHandledByTheListDoesNotAlsoBackOut()
	{
		var (screen, list, context, row, backs) = Build();
		screen.Update( context, UiInput.Idle( row( 2 ) ) with { RightPressed = true } );
		CollectionAssert.Contains( list.Log, "right2" );
		Assert.AreEqual( 0, backs.Count, "the list's right-click action replaces the back-out" );
	}

	[TestMethod]
	public void ARightClickTheListIgnoresStillBacksOut()
	{
		var (screen, list, context, row, backs) = Build();
		list.HandleRight = false;
		screen.Update( context, UiInput.Idle( row( 2 ) ) with { RightPressed = true } );
		Assert.AreEqual( 1, backs.Count );
	}

	[TestMethod]
	public void TheClickRuleIs500MillisecondsAndSixPixels()
	{
		Assert.AreEqual( 500, UiInputSource.ClickMilliseconds );
		Assert.AreEqual( 6f, UiInputSource.ClickSlop );
		Assert.IsFalse( UiInputSource.IsNotClick( 1000, 1500, false ), "exactly 500 ms still clicks" );
		Assert.IsTrue( UiInputSource.IsNotClick( 1000, 1501, false, 30 ), "longer than 500 ms over many frames does not" );
		Assert.IsFalse( UiInputSource.IsNotClick( 1000, 1900, false, 2 ), "one long frame (a hitch) between press and release still clicks" );
		Assert.IsTrue( UiInputSource.IsNotClick( 1000, 1100, true ), "a press marked moved does not, however short" );
	}

	[TestMethod]
	public void TheSourceMarksAReleaseAfterALongPressOrAMoveAsNotAClick()
	{
		var previous = Input.Mouse;
		try
		{
			long now = 0;
			var source = new UiInputSource { Clock = () => now };
			UiInput Frame( bool left, float x, float y )
			{
				Input.Mouse = new Input.MouseInfo { Left = left, Position = new System.Numerics.Vector2( x, y ) };
				return source.Poll( 1, 1 );
			}
			Frame( true, 50, 50 );
			now = 200;
			Assert.IsFalse( Frame( false, 53, 54 ).NotClick, "a short press with a 3 px drift is a click" );
			now = 1000;
			Frame( true, 50, 50 );
			Frame( true, 50, 50 );
			Frame( true, 50, 50 );
			now = 1700;
			Assert.IsTrue( Frame( false, 50, 50 ).NotClick, "a 700 ms press held over several frames is not" );
			now = 1800;
			Frame( true, 50, 50 );
			now = 2500;
			Assert.IsFalse( Frame( false, 50, 50 ).NotClick, "a 700 ms hitch frame between press and release still clicks" );
			now = 4000;
			Frame( true, 50, 50 );
			now = 4100;
			Frame( true, 58, 50 );
			Assert.IsTrue( Frame( false, 58, 50 ).NotClick, "a move of 8 px on one axis is not" );
			now = 5000;
			Frame( true, 50, 50 );
			now = 5100;
			Frame( true, 56, 44 );
			Assert.IsFalse( Frame( false, 56, 44 ).NotClick, "6 px on both axes is still a click" );
		}
		finally
		{
			Input.Mouse = previous;
		}
	}
}
