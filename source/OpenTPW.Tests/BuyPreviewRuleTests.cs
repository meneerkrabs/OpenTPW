using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>The buy window's turning preview (tp.exe 0x4689f0 fit, 0x468e50 turn and tilt).</summary>
[TestClass]
public class BuyPreviewRuleTests
{
	[TestMethod]
	public void TheTurnAndTiltAreTheOriginals()
	{
		Assert.AreEqual( 0.4f, PreviewIcon.BuyTurnRate, "0.0004 rad a millisecond" );
		Assert.AreEqual( MathF.PI / 4, PreviewIcon.BuyTilt );
	}

	[TestMethod]
	public void TheFitTakesTheSmallerOfTheFootprintAndHeightTerms()
	{
		// a 4 x 4 footprint (half-diagonal 2.83) and 3 high: the width term 360 / 2 / 2.83 = 63.6, the height term 360 / |(4, 3, 4)| = 57.1
		var min = new NVector3( -2, 0, -2 );
		var max = new NVector3( 2, 3, 2 );
		Assert.AreEqual( 360f / MathF.Sqrt( 41 ), PreviewIcon.BuyScale( 360, 360, min, max ), 1e-3 );
		// a wide frame leaves the height term; a flat model in a narrow frame takes the width term
		Assert.AreEqual( 360f / 2 / MathF.Sqrt( 8 ), PreviewIcon.BuyScale( 360, 1000, min, new NVector3( 2, 0.1f, 2 ) ), 1e-3 );
	}
}
