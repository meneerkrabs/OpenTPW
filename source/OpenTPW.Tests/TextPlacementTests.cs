using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Block and line placement of tp.exe's text library (0x6aeb10 block box, 0x6ae550 line offsets).</summary>
[TestClass]
public class TextPlacementTests
{
	private static FontAtlas Atlas() => new( FontAtlasTests.CreateFont( 10,
		new FontAtlasTests.SyntheticGlyph( 'i', 1, 5, 0, 2, 2 ),
		new FontAtlasTests.SyntheticGlyph( 'W', 5, 5, 0, 2, 6 ),
		new FontAtlasTests.SyntheticGlyph( ' ', 0, 0, 0, 0, 3 ) ) );

	[TestMethod]
	public void BlockIsCentredWithSignedTruncatingDivision()
	{
		Assert.AreEqual( 10 + (100 - 31) / 2, TextPlacement.BlockLeft( TextAlignment.Center, 10, 110, 31 ) );
		Assert.AreEqual( 44, TextPlacement.BlockLeft( TextAlignment.Center, 10, 110, 31 ) );
		// a block wider than the box is still centred, its overhang truncated toward zero (not floored)
		Assert.AreEqual( 10 + -7 / 2, TextPlacement.BlockLeft( TextAlignment.Center, 10, 110, 107 ) );
		Assert.AreEqual( 7, TextPlacement.BlockLeft( TextAlignment.Center, 10, 110, 107 ) );
		Assert.AreEqual( 110 - 31, TextPlacement.BlockLeft( TextAlignment.Right, 10, 110, 31 ) );
		Assert.AreEqual( 10, TextPlacement.BlockLeft( TextAlignment.Left, 10, 110, 31 ) );
	}

	[TestMethod]
	public void VerticalBlockCentresEvenWhenTallerThanTheBox()
	{
		Assert.AreEqual( 20 + (40 - 11) / 2, TextPlacement.BlockTopCentered( 20, 60, 11 ) );
		Assert.AreEqual( 20 + (40 - 55) / 2, TextPlacement.BlockTopCentered( 20, 60, 55 ) );
		Assert.AreEqual( 13, TextPlacement.BlockTopCentered( 20, 60, 55 ), "-15 / 2 truncates to -7" );
		Assert.AreEqual( 60 - 11, TextPlacement.BlockTopBottom( 60, 11 ) );
	}

	[TestMethod]
	public void LinesMoveInsideTheBlockByHalfOrAllOfTheDifference()
	{
		Assert.AreEqual( 0, TextPlacement.LineOffset( TextAlignment.Left, 20, 5 ) );
		Assert.AreEqual( 7, TextPlacement.LineOffset( TextAlignment.Center, 20, 5 ), "15 >> 1" );
		Assert.AreEqual( 15, TextPlacement.LineOffset( TextAlignment.Right, 20, 5 ) );
		Assert.AreEqual( 0, TextPlacement.LineOffset( TextAlignment.Center, 20, 20 ) );
	}

	[TestMethod]
	public void LayoutReportsPerLineWidths()
	{
		var layout = TextLayout.Create( Atlas(), "WW\ni" );
		CollectionAssert.AreEqual( new[] { 12, 2 }, layout.LineWidths.ToArray() );
		Assert.AreEqual( 12, layout.Width );
		CollectionAssert.AreEqual( new[] { 0, 0, 1 }, layout.Glyphs.Select( glyph => glyph.Line ).ToArray() );
	}

	[TestMethod]
	public void WrappedLineWidthDropsTheSpacesWhereTheLineBroke()
	{
		var layout = TextLayout.Create( Atlas(), "WW WW", 14 );
		Assert.AreEqual( 2, layout.LineCount );
		CollectionAssert.AreEqual( new[] { 12, 12 }, layout.LineWidths.ToArray() );
	}

	[TestMethod]
	public void CentredLinesAreShiftedAndLeftAlignedLinesAreNot()
	{
		var layout = TextLayout.Create( Atlas(), "WW\ni" );
		Assert.AreSame( layout, layout.AlignLines( TextAlignment.Left ) );
		var centred = layout.AlignLines( TextAlignment.Center );
		var right = layout.AlignLines( TextAlignment.Right );
		Assert.AreEqual( 0, centred.Glyphs[0].X );
		Assert.AreEqual( layout.Glyphs[2].X + 5, centred.Glyphs[2].X, "(12 - 2) >> 1" );
		Assert.AreEqual( layout.Glyphs[2].X + 10, right.Glyphs[2].X );
		Assert.AreEqual( layout.Width, centred.Width );
		Assert.AreEqual( layout.Height, centred.Height );
	}

	[TestMethod]
	public void SingleLineTextIsNotShifted()
	{
		var layout = TextLayout.Create( Atlas(), "WiW" );
		Assert.AreSame( layout, layout.AlignLines( TextAlignment.Center ) );
	}
}
