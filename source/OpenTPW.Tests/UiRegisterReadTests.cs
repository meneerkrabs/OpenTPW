using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>UI register items settled by reading tp.exe: the rules the reads gave, without game data.</summary>
[TestClass]
public class UiRegisterReadTests
{
	[TestMethod]
	public void TheBuyScrollBallRunsFromTheTrackTopToABallAboveItsBottomInWholeUnits()
	{
		// track (1743,383)-(1802,803), ball 67 high: 353 units of travel (tp.exe 0x66b088)
		Assert.AreEqual( 383f, ParkHud.BuyScrollKnobRect( 0, 7 ).Y );
		Assert.AreEqual( 803f - 67f, ParkHud.BuyScrollKnobRect( 7, 7 ).Y );
		Assert.AreEqual( 383f + 353 * 3 / 7, ParkHud.BuyScrollKnobRect( 3, 7 ).Y, "integer division, as the original's short arithmetic" );
		Assert.AreEqual( 383f, ParkHud.BuyScrollKnobRect( 0, 0 ).Y );
	}

	[TestMethod]
	public void TheBuyScrollBarShowsOnlyWhileTheListScrolls()
	{
		// tp.exe 0x664495 hides the scroller when the list has no more items than rows
		Assert.IsFalse( ParkHud.BuyScrollShown( 0 ) );
		Assert.IsTrue( ParkHud.BuyScrollShown( 1 ) );
	}

	[TestMethod]
	public void MapOverlayCellsUseTheOriginalGridOnTheThemeImage()
	{
		// tp.exe 0x5f2380: 15 + column × trunc(480 / width), 42 + row × trunc(425 / height)
		var first = ParkMapGeometry.OverlayCellRect( 96, 85, true, 0, 0 );
		var last = ParkMapGeometry.OverlayCellRect( 96, 85, true, 95, 84 );
		Assert.AreEqual( (15f, 42f, 5f, 5f), (first.X, first.Y, first.Width, first.Height) );
		Assert.AreEqual( (495f, 467f), (last.Right, last.Bottom) );
		Assert.AreEqual( 4f, ParkMapGeometry.OverlayCellRect( 96, 100, true, 0, 0 ).Height, "the cell size is truncated" );
		Assert.AreEqual( ParkMapGeometry.CellImageRect( 32, 32, false, 3, 4 ), ParkMapGeometry.OverlayCellRect( 32, 32, false, 3, 4 ), "the sandbox keeps its own grid" );
	}

	[TestMethod]
	public void TheMapStartsWithOnlyTheRidesLayer()
	{
		// the layer word starts as 1, the bit of control 2439 (b_sride)
		CollectionAssert.AreEquivalent( new[] { BuildCategory.Rides }, ParkHud.InitialMapCategories.ToArray() );
	}
}
