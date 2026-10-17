using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// Golden coverage for the rasterizer's scanline window semantics: half-open sub-scanline sampling
/// (all 16 sample positions), row-boundary starts/ends, equal-X crossing ties, shared vertices,
/// horizontal edges, non-zero winding across overlapping placed glyphs and both contour orientations,
/// and the maximum-dimension guard. Everything goes through the public <see cref="TrueTypeRasterizer"/>
/// entry points; expectations are pinned bytes and hashes, never recomputed formulas.
/// </summary>
[TestClass]
public class TrueTypeRasterizerGoldenTests
{
	private const float Scale = 1f; // With scale 1, font-unit coordinates are pixel coordinates (Y is negated).

	/// <summary>Returns pixel-space rectangle corners as font-space points (pixel y-down becomes font y-up).</summary>
	private static IReadOnlyList<TrueTypePoint> PixelRectangle( float left, float top, float right, float bottom, bool clockwise = true )
	{
		// Clockwise in pixel space (y down): down the left edge, right along the bottom, up the right edge.
		return clockwise
			? new[] { P( left, top ), P( left, bottom ), P( right, bottom ), P( right, top ) }
			: new[] { P( left, top ), P( right, top ), P( right, bottom ), P( left, bottom ) };
		static TrueTypePoint P( float x, float y ) => new( x, -y, true );
	}

	private static CoverageBitmap Rasterize( params (IReadOnlyList<IReadOnlyList<TrueTypePoint>> Outline, float PenX)[] placed ) =>
		TrueTypeRasterizer.Rasterize( placed, Scale, Scale, CoverageModel.Linear );

	private static IReadOnlyList<IReadOnlyList<TrueTypePoint>> Contours( params IReadOnlyList<TrueTypePoint>[] contours ) => contours;

	[TestMethod]
	public void ARectangleOnExactSubScanlineBoundariesCoversOnlyTheSamplesInsideIt()
	{
		// Vertical extent [3/32, 29/32) hits sample positions exactly: y1 == top is inside (half-open
		// [top, bottom)), y14 == bottom is outside. Samples 1..13 of 16 cover the row: 13/16 * 255 -> 207.
		var bitmap = Rasterize( (Contours( PixelRectangle( 1, 3f / 32, 3, 29f / 32 ) ), 0f) );
		Assert.AreEqual( (2, 1, -1, 0), (bitmap.Width, bitmap.Height, bitmap.OriginX, bitmap.OriginY) );
		CollectionAssert.AreEqual( new byte[] { 207, 207 }, bitmap.Coverage );
	}

	[TestMethod]
	public void ASliverOfExactlyOneSubScanlineContributesOneSixteenth()
	{
		// [1/32, 3/32): sample y0 == top is inside, y1 == bottom is outside -> exactly one sample -> 255/16 rounds to 16.
		var bitmap = Rasterize( (Contours( PixelRectangle( 1, 1f / 32, 3, 3f / 32 ) ), 0f) );
		CollectionAssert.AreEqual( new byte[] { 16, 16 }, bitmap.Coverage );
	}

	[TestMethod]
	[DataRow( 0.03125f, 0.09375f )]
	[DataRow( 0.09375f, 0.15625f )]
	[DataRow( 0.15625f, 0.21875f )]
	[DataRow( 0.21875f, 0.28125f )]
	[DataRow( 0.28125f, 0.34375f )]
	[DataRow( 0.34375f, 0.40625f )]
	[DataRow( 0.40625f, 0.46875f )]
	[DataRow( 0.46875f, 0.53125f )]
	[DataRow( 0.53125f, 0.59375f )]
	[DataRow( 0.59375f, 0.65625f )]
	[DataRow( 0.65625f, 0.71875f )]
	[DataRow( 0.71875f, 0.78125f )]
	[DataRow( 0.78125f, 0.84375f )]
	[DataRow( 0.84375f, 0.90625f )]
	[DataRow( 0.90625f, 0.96875f )]
	[DataRow( 0.96875f, 1f )]
	public void ASliverBetweenAdjacentSamplePositionsCoversExactlyThatSample( float top, float bottom )
	{
		// Every one of the 16 sample positions must be the only one inside its own [yk, yk+1) sliver.
		var bitmap = Rasterize( (Contours( PixelRectangle( 1, top, 3, bottom ) ), 0f) );
		Assert.AreEqual( (2, 1), (bitmap.Width, bitmap.Height), $"sliver [{top}, {bottom})" );
		CollectionAssert.AreEqual( new byte[] { 16, 16 }, bitmap.Coverage, $"sliver [{top}, {bottom})" );
	}

	[TestMethod]
	public void EdgesStartingAndEndingInsideARowAreCountedPerSample()
	{
		// [5/64, 61/64) lies strictly between sample positions: samples 1..14 of 16 -> 14/16 * 255 -> 223.
		var bitmap = Rasterize( (Contours( PixelRectangle( 1, 5f / 64, 3, 61f / 64 ) ), 0f) );
		CollectionAssert.AreEqual( new byte[] { 223, 223 }, bitmap.Coverage );
	}

	[TestMethod]
	public void EdgesOnExactRowBoundariesFillOnlyTheirOwnRow()
	{
		// One contour in [0, 1] and one in [1, 2]: the shared y == 1 boundary belongs to neither twice
		// (half-open rows), and an edge ending exactly at a row boundary never paints the next row.
		var bitmap = Rasterize( (Contours( PixelRectangle( 0, 0, 2, 1 ), PixelRectangle( 4, 1, 6, 2 ) ), 0f) );
		Assert.AreEqual( (6, 2), (bitmap.Width, bitmap.Height) );
		CollectionAssert.AreEqual( new byte[] { 255, 255, 0, 0, 0, 0 }, bitmap.Coverage[..6] );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 0, 0, 255, 255 }, bitmap.Coverage[6..] );
	}

	[TestMethod]
	public void CoincidentEdgesOfOppositeOrientationCancelUnderNonZeroWinding()
	{
		// Two identical rectangles with opposite orientations produce equal-X crossing ties on every
		// sample; the windings cancel, so nothing is painted regardless of tie order.
		var band = (Top: 1f / 4, Bottom: 3f / 4);
		var bitmap = Rasterize( (Contours( PixelRectangle( 2, band.Top, 4, band.Bottom ), PixelRectangle( 2, band.Top, 4, band.Bottom, clockwise: false ) ), 0f) );
		Assert.AreEqual( (2, 1), (bitmap.Width, bitmap.Height) );
		CollectionAssert.AreEqual( new byte[] { 0, 0 }, bitmap.Coverage );
	}

	[TestMethod]
	public void OverlappingSameOrientationContoursStayFullAndRoundMidpointsAwayFromZero()
	{
		// Rectangles [2, 4] and [3, 5] over [1/4, 3/4): samples 4..11 (8 of 16) fill every column —
		// the doubly-wound overlap [3, 4] stays covered under non-zero winding. 8/16 * 255 = 127.5 -> 128.
		var band = (Top: 1f / 4, Bottom: 3f / 4);
		var bitmap = Rasterize( (Contours( PixelRectangle( 2, band.Top, 4, band.Bottom ), PixelRectangle( 3, band.Top, 5, band.Bottom ) ), 0f) );
		Assert.AreEqual( (3, 1), (bitmap.Width, bitmap.Height) );
		CollectionAssert.AreEqual( new byte[] { 128, 128, 128 }, bitmap.Coverage );
	}

	[TestMethod]
	public void OverlappingPlacedGlyphsWindAcrossGlyphBoundaries()
	{
		// Two placed copies of one square outline, the second reversed: the overlap cancels to a hole,
		// the non-overlap stays solid — the whole-string placement keeps winding across glyphs.
		var square = Contours( PixelRectangle( 0, 0, 4, 4 ) );
		var bitmap = Rasterize( (square, 0f), (Contours( PixelRectangle( 0, 0, 4, 4, clockwise: false) ), 2f) );
		Assert.AreEqual( (6, 4), (bitmap.Width, bitmap.Height) );
		for ( var y = 0; y < 4; y++ )
			CollectionAssert.AreEqual( new byte[] { 255, 255, 0, 0, 255, 255 }, bitmap.Coverage[(y * 6)..((y + 1) * 6)], $"row {y}" );
	}

	[TestMethod]
	public void BothContourOrientationsRasterizeIdentically()
	{
		var forward = Rasterize( (Contours( PixelRectangle( 0, 0, 4, 4 ) ), 0f) );
		var reversed = Rasterize( (Contours( PixelRectangle( 0, 0, 4, 4, clockwise: false ) ), 0f) );
		Assert.IsTrue( forward.Coverage.All( value => value == 255 ) );
		Assert.AreEqual( forward.ComputeHash(), reversed.ComputeHash() );
	}

	[TestMethod]
	public void HorizontalOnlyOutlinesProduceTheEmptyBitmap()
	{
		// Every edge of a degenerate flat contour is horizontal and dropped; shared vertices of the
		// remaining nothing leave no edges at all.
		var flat = Rasterize( (Contours( PixelRectangle( 0, 2, 4, 2 ) ), 0f) );
		Assert.AreEqual( CoverageBitmap.Empty, flat );
		var none = TrueTypeRasterizer.Rasterize( Array.Empty<(IReadOnlyList<IReadOnlyList<TrueTypePoint>>, float)>(), Scale, Scale, CoverageModel.Linear );
		Assert.AreEqual( CoverageBitmap.Empty, none );
	}

	[TestMethod]
	public void TheMaximumDimensionGuardStillApplies()
	{
		// 8192 in either dimension is in-format; one pixel more is rejected before allocating coverage.
		var widest = Rasterize( (Contours( PixelRectangle( 0, 0, 8192, 1 ) ), 0f) );
		Assert.AreEqual( (8192, 1), (widest.Width, widest.Height) );
		var tallest = Rasterize( (Contours( PixelRectangle( 0, 0, 1, 8192 ) ), 0f) );
		Assert.AreEqual( (1, 8192), (tallest.Width, tallest.Height) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Rasterize( (Contours( PixelRectangle( 0, 0, 8193, 1 ) ), 0f) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Rasterize( (Contours( PixelRectangle( 0, 0, 1, 8193 ) ), 0f) ) );
	}

	/// <summary>
	/// A composite of everything at once — slanted edges with per-sample interpolation, a sliver that
	/// starts and ends mid-row, a reversed inner contour (hole) and an overlapping placed glyph — pinned
	/// by the hash of the pre-optimization rasterizer.
	/// </summary>
	[TestMethod]
	public void CompositeSceneHashIsPinned()
	{
		// A diamond (four slanted edges meeting at shared vertices), rows 0..4.
		var diamond = new IReadOnlyList<TrueTypePoint>[] { new[] { new TrueTypePoint( 1, -3, true ), new TrueTypePoint( 3, -1, true ), new TrueTypePoint( 5, -3, true ), new TrueTypePoint( 3, -5, true ) } };
		// A mid-row sliver in row 2: [2 + 5/64, 2 + 61/64).
		var sliver = Contours( PixelRectangle( 4, 2 + 5f / 64, 6, 2 + 61f / 64 ) );
		// A square with a reversed inner square: a hole under non-zero winding.
		var holed = Contours( PixelRectangle( 7, 0, 11, 4 ), PixelRectangle( 8, 1, 10, 3, clockwise: false ) );
		// The same square placed overlapping the holed one, solid orientation.
		var overlap = Contours( PixelRectangle( 0, 0, 4, 4 ) );
		var bitmap = Rasterize( (Contours( diamond ), 0f), (sliver, 0f), (holed, 0f), (overlap, 9f) );
		Assert.AreEqual( (12, 5), (bitmap.Width, bitmap.Height) );
		Assert.AreEqual( 0, bitmap[0, 0], "row 0 above the diamond stays empty" );
		Assert.AreEqual( 255, bitmap[6, 0], "the holed square's ring is solid" );
		Assert.AreEqual( 0, bitmap[7, 1], "the reversed inner contour cuts a hole the overlap glyph does not cover" );
		Assert.AreEqual( 255, bitmap[8, 1], "the overlap glyph refills the hole where it overlaps it" );
		Assert.AreEqual( 223, bitmap[4, 2], "the mid-row sliver covers samples 1..14 of row 2" );
		Assert.AreEqual( 255, bitmap[10, 2], "the placed overlap glyph is solid" );
		Assert.AreEqual( "9DEEB53E0660D8F7800379D0B25054AC899B56DAA53EB4E600AA1E468B3D3BEB", bitmap.ComputeHash() );
	}
}
