using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The online login dialog (tp.exe table 0x7583e8): its controls come from the table, OpenTPW's own controls sit in a panel of their own under the window and cover nothing.</summary>
[TestClass]
public class LoginFormLayoutTests
{
	private static readonly LayoutTable Table = OriginalLayoutTables.LoginWindow;

	private static bool Contains( UiRect outer, UiRect inner ) =>
		inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

	private static bool Overlaps( UiRect a, UiRect b ) =>
		a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

	private static UiRect[] OriginalControls() => new[] { Table.Control( 0x49 ).Rect, Table.Control( 0x4B ).Rect, Table.Control( 0x4D ).Rect,
		Table.Model( "b_okay" ).Rect, Table.Model( "b_exit" ).Rect };

	private static UiRect[] WindowControls() => OriginalControls().Append( OnlineScreens.LoginStatusBounds ).ToArray();

	private static UiRect[] PanelControls() => new[] { OnlineScreens.LoginServerLabelBounds, OnlineScreens.LoginServerBounds, OnlineScreens.LoginCodeLabelBounds,
		OnlineScreens.LoginCodeBounds, OnlineScreens.LoginRegisterBounds, OnlineScreens.LoginForgotBounds };

	[TestMethod]
	public void TheTableHoldsTheTickAndTheCrossAsModels()
	{
		Assert.AreEqual( "wdialogw", Table.Root.Model );
		Assert.AreEqual( "b_okay", Table.Model( "b_okay" ).Model );
		Assert.AreEqual( "b_exit", Table.Model( "b_exit" ).Model );
		Assert.AreEqual( "!frame", Table.Control( 0x49 ).Model );
	}

	[TestMethod]
	public void EveryWindowControlLiesInsideTheWindow()
	{
		var window = Table.Root.Rect;
		foreach ( var rect in WindowControls() )
			Assert.IsTrue( Contains( window, rect ), $"{rect} lies inside the window {window}" );
	}

	[TestMethod]
	public void EveryOwnControlLiesInsideItsPanelWhichLiesBesideTheWindow()
	{
		var panel = OnlineScreens.LoginPanelBounds;
		foreach ( var rect in PanelControls() )
			Assert.IsTrue( Contains( panel, rect ), $"{rect} lies inside the panel {panel}" );
		Assert.IsFalse( Overlaps( panel, Table.Root.Rect ), "the panel does not cover the original window" );
		Assert.IsTrue( Contains( new UiRect( 0, 0, UiCanvas.VirtualWidth, UiCanvas.VirtualHeight ), panel ), "the panel lies on the canvas" );
	}

	[TestMethod]
	public void NoControlOverlapsAnother()
	{
		foreach ( var group in new[] { WindowControls(), PanelControls() } )
			for ( var first = 0; first < group.Length; first++ )
				for ( var second = first + 1; second < group.Length; second++ )
					Assert.IsFalse( Overlaps( group[first], group[second] ), $"{group[first]} and {group[second]} overlap" );
	}

	[TestMethod]
	public void TheBackButtonLiesInsideAWindowOfAnySize()
	{
		foreach ( var size in new[] { (1150f, 1300f), (1400f, 1300f), (1500f, 1450f) } )
		{
			var window = UiDialogs.CenteredWindow( size.Item1, size.Item2 );
			Assert.IsTrue( Contains( window, OnlineScreens.BackBounds( window ) ), $"the back button lies inside the {size} window" );
		}
	}
}
