using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The pause menu's lines (tp.exe 0x48c150 → 0x492f60) and colour (0x48b2a0).</summary>
[TestClass]
public class PauseMenuLayoutTests
{
	[TestMethod]
	public void LinesAreTheMeasuredTextPlusMarginsStackedFromTheTop()
	{
		// MENUMED (BF4 line height 48) at 640x480: 1536 / 480 canvas units per pixel
		var scale = 480f / UiCanvas.VirtualHeight;
		var lines = UiDialogs.TextMenuLines( new[] { (199, 48), (99, 48) }, 1, scale );
		Assert.AreEqual( 10f, lines[0].Y, "the first line's top is 10" );
		Assert.AreEqual( (48 + 5) / scale, lines[0].Height, 1e-3, "text height + 5 pixels" );
		Assert.AreEqual( (199 + 1) / scale, lines[0].Width, 1e-3, "text width + 1 pixel" );
		Assert.AreEqual( UiCanvas.VirtualWidth / 2f, lines[0].Center.X, 1e-3, "centred" );
		Assert.AreEqual( lines[0].Bottom + 5, lines[1].Y, 1e-3, "the next line starts 5 units below" );
		Assert.AreEqual( (99 + 1) / scale, lines[1].Width, 1e-3 );
	}

	[TestMethod]
	public void TheTextScaleWidensTheMarginsWithTheGlyphs()
	{
		var lines = UiDialogs.TextMenuLines( new[] { (100, 40) }, 2, 1f );
		Assert.AreEqual( 202f, lines[0].Width );
		Assert.AreEqual( 90f, lines[0].Height );
	}

	[TestMethod]
	public void LinesAreTheOriginalCyan() => Assert.AreEqual( new Veldrid.RgbaByte( 0, 175, 190, 255 ), UiColors.MenuItem );
}
