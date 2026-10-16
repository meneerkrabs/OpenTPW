using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The Load Park screen in tp.exe's load dialog table 0x7523f0 (built by 0x49efb0).</summary>
[TestClass]
public class LoadDialogTableTests
{
	private static ParkLoadEntry[] Entries( int count ) => Enumerable.Range( 0, count )
		.Select( index => new ParkLoadEntry( "theme" + index, -1, false, "/save/park" + index + ".json", "Park " + index ) ).ToArray();

	private static UiScreen Screen( int count ) => FrontEndMenu.CreateLoadScreen( new UiScreenStack(), OriginalUiTests.FakeStrings(), Entries( count ), _ => { } );

	[TestMethod]
	public void TheScreenUsesTheTableRectangles()
	{
		var screen = Screen( 3 );
		Assert.AreEqual( "window2", ((UiModelImage)screen.Find( "window" )!).Model );
		Assert.AreEqual( new UiRect( 248, 30, 1552, 977 ), screen.Find( "window" )!.Bounds );
		Assert.AreEqual( new UiRect( 746, 83, 472, 79 ), screen.Find( "title" )!.Bounds );
		Assert.AreEqual( new UiRect( 341, 203, 1273, 569 ), screen.Find( "listFrame" )!.Bounds );
		Assert.AreEqual( new UiRect( 371, 229, 1140, 504 ), screen.Find( "parks" )!.Bounds, "the rows fill the list's content region" );
		Assert.AreEqual( new UiRect( 1671, 818, 83, 83 ), screen.Find( "back" )!.Bounds, "b_exit" );
		Assert.AreEqual( new UiRect( 605, 811, 738, 84 ), screen.Find( "loadSelected" )!.Bounds, "the host load button takes the hidden name field's place" );
	}

	[DataTestMethod]
	[DataRow( 12, false )]
	[DataRow( 13, true )]
	public void TheScrollerShowsOnlyWhileTheRowsDoNotFit( int count, bool shown )
	{
		var screen = Screen( count );
		foreach ( var id in new[] { "loadScroll", "scrollUp", "scrollDown" } )
			Assert.AreEqual( shown, screen.Find( id )!.Visible, id );
	}

	[TestMethod]
	public void TheArrowsScrollTheList()
	{
		var screen = Screen( 20 );
		var list = (UiScrollList)screen.Find( "parks" )!;
		screen.Find( "scrollDown" )!.Activate();
		Assert.AreEqual( 1, list.FirstRow );
		screen.Find( "scrollUp" )!.Activate();
		Assert.AreEqual( 0, list.FirstRow );
	}
}
