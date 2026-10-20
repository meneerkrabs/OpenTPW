using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>The hire window's figure (tp.exe 0x0049BA00 → 0x00475A10 with the script 0x0074F758).</summary>
[TestClass]
public class StaffPortraitTests
{
	[TestMethod]
	public void TheFigureWalksFramesZeroToSevenFacingDirectionFive()
	{
		var start = StaffSpriteRules.Portrait( 0f, 5 );
		Assert.AreEqual( (1, 3, true, 0), start, "a five-direction bank shows script direction 5 as 3 mirrored (0x00542010: 8 − 5)" );
		Assert.AreEqual( (1, 5, false, 0), StaffSpriteRules.Portrait( 0f, 8 ), "a bank with every direction shows it as stored" );
		var steps = Enumerable.Range( 0, 10 ).Select( tick => StaffSpriteRules.Portrait( tick / StaffSpriteRules.FramesPerSecond + 0.01f, 5 ).Step ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1 }, steps, "frames 0..7, then the script starts again" );
	}

	[TestMethod]
	public void TheGenericFeetRemainAtTheCentreAQuarterAboveTheBottom()
	{
		// Generic rectangle-relative placement stays unchanged; the actual hire widget opts into its integer inner hotspot.
		Assert.AreEqual( new NVector2( 616, 377 ), UiSpriteFigure.Feet( new UiRect( 484, 179, 264, 264 ) ) );
	}

	[TestMethod]
	public void AFrameIsLaidOutAroundItsHotspotAndMirroredAboutIt()
	{
		var frame = new UiSpriteFrame( null!, 1024, 512, 100, 50, 20, 40, -8, -38, false );
		var (rect, uv0, uv1) = UiSpriteFigure.Layout( frame, new NVector2( 600, 400 ), 2f );
		Assert.AreEqual( new UiRect( 584, 324, 40, 80 ), rect );
		Assert.AreEqual( (new NVector2( 100 / 1024f, 50 / 512f ), new NVector2( 120 / 1024f, 90 / 512f )), (uv0, uv1) );
		var (mirrored, m0, m1) = UiSpriteFigure.Layout( frame with { Mirror = true }, new NVector2( 600, 400 ), 2f );
		Assert.AreEqual( new UiRect( 576, 324, 40, 80 ), mirrored, "the left edge becomes −(origin + width)" );
		Assert.AreEqual( (uv1.X, uv0.X), (m0.X, m1.X), "the texture runs backwards" );
	}
}
