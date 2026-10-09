using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
public class TextBatchTests
{
	[TestMethod]
	public void CompositeBlendsAtlasCoverageOverSolidRectangle()
	{
		var atlas = new FontAtlas( FontAtlasTests.CreateFont( 4,
			new FontAtlasTests.SyntheticGlyph( 'a', 2, 1, 1, 1, 3, new byte[] { 15, 5 } ) ) );
		var batch = new TextBatch();
		batch.AddRectangle( 0, 0, 4, 3, new RgbaByte( 0, 0, 100, 255 ) );
		batch.AddText( atlas, TextLayout.Create( atlas, "a" ), 0, 0, new RgbaByte( 255, 200, 0, 255 ) );
		batch.AddRectangle( 0, 0, 0, 5, new RgbaByte( 255, 255, 255, 255 ) );
		Assert.AreEqual( 2, batch.Quads.Count, "Empty rectangles are skipped." );
		var glyph = batch.Quads[1];
		Assert.AreEqual( (1, 1, 2, 1, atlas.Glyphs['a'].X, atlas.Glyphs['a'].Y), (glyph.X, glyph.Y, glyph.Width, glyph.Height, glyph.AtlasX, glyph.AtlasY) );

		var image = Enumerable.Repeat( (byte)7, 4 * 3 * 4 ).ToArray();
		batch.Composite( image, 4, 3 );
		CollectionAssert.AreEqual( new byte[] { 100, 0, 0, 255 }, image[0..4], "Opaque rectangle replaces BGRA destination." );
		CollectionAssert.AreEqual( new byte[] { 0, 200, 255, 255 }, image[20..24], "Coverage 15 is fully opaque." );
		CollectionAssert.AreEqual( new byte[] { 67, 67, 85, 198 }, image[24..28], "Coverage 5 is alpha 85/255." );
		CollectionAssert.AreEqual( new byte[] { 100, 0, 0, 255 }, image[28..32] );

		var clipped = new TextBatch();
		clipped.AddRectangle( -2, -2, 3, 3, new RgbaByte( 1, 2, 3, 255 ) );
		var small = new byte[2 * 2 * 4];
		clipped.Composite( small, 2, 2 );
		CollectionAssert.AreEqual( new byte[] { 3, 2, 1, 255, 0, 0, 0, 0 }, small[0..8], "Quads are clipped to the image." );
	}
}
