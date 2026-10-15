using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
public class FontSmoothingTests
{
	private static FontAtlas Block( int width, int height ) => new( FontAtlasTests.CreateFont( 8,
		new FontAtlasTests.SyntheticGlyph( 'A', width, height, 0, 0, width + 1, Enumerable.Repeat( (byte)15, width * height ).ToArray() ),
		new FontAtlasTests.SyntheticGlyph( 'B', width, height, 0, 0, width + 1, Enumerable.Repeat( (byte)15, width * height ).ToArray() ) ) );

	[TestMethod]
	public void ScaleOneKeepsTheOriginalCoverage()
	{
		var atlas = Block( 4, 4 );
		Assert.AreEqual( 1, FontSmoothing.Factor( atlas, 1 ) );
		Assert.AreSame( atlas.Alpha, FontSmoothing.Coverage( atlas, 1 ) );
	}

	[TestMethod]
	public void DoubledGlyphKeepsItsInsideAndRoundsItsCorners()
	{
		var atlas = Block( 4, 4 );
		var factor = FontSmoothing.Factor( atlas, 2 );
		Assert.AreEqual( 2, factor );
		var coverage = FontSmoothing.Coverage( atlas, factor );
		var stride = atlas.Width * 2;
		Assert.AreEqual( stride * atlas.Height * 2, coverage.Length );
		var glyph = atlas.Glyphs['A'];
		byte At( int x, int y ) => coverage[(glyph.Y * 2 + y) * stride + glyph.X * 2 + x];
		for ( var y = 2; y < 6; y++ )
			for ( var x = 2; x < 6; x++ )
				Assert.AreEqual( 255, At( x, y ), $"inside pixel {x},{y}" );
		// Doubling would keep the corner pixel opaque; smoothing rounds it but keeps it mostly covered.
		Assert.IsTrue( At( 0, 0 ) is > 128 and < 255, $"corner {At( 0, 0 )}" );
		Assert.IsTrue( At( 0, 0 ) < At( 0, 3 ), "corner is lighter than the edge beside it" );
	}

	[TestMethod]
	public void GlyphsDoNotBleedIntoTheirNeighboursOrPadding()
	{
		var atlas = Block( 3, 3 );
		var coverage = FontSmoothing.Coverage( atlas, 2 );
		var stride = atlas.Width * 2;
		var inside = new bool[coverage.Length];
		foreach ( var glyph in atlas.Glyphs.Values )
			for ( var y = glyph.Y * 2; y < (glyph.Y + glyph.Height) * 2; y++ )
				for ( var x = glyph.X * 2; x < (glyph.X + glyph.Width) * 2; x++ )
					inside[y * stride + x] = true;
		for ( var index = 0; index < coverage.Length; index++ )
			if ( !inside[index] )
				Assert.AreEqual( 0, coverage[index], $"pixel {index % stride},{index / stride} outside every glyph" );
	}

	[TestMethod]
	public void LargeAtlasesUseASmallerFactor()
	{
		var atlas = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2100, 1, 0, 0, 2101 ) ) );
		Assert.AreEqual( 1, FontSmoothing.Factor( atlas, 2 ) );
		Assert.AreEqual( 1, FontSmoothing.Factor( atlas, 1 ) );
	}
}
