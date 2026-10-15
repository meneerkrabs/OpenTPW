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
}
