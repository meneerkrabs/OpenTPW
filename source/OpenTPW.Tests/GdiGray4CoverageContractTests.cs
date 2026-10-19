using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace OpenTPW.Tests;

/// <summary>
/// COMPAT-003 characterization of the default <see cref="CoverageModel.GdiGray4"/> rasterizer model on
/// synthetic outlines: the full 17-level byte map, exact slanted-edge coverage, empty and degenerate
/// glyphs, negative-coordinate origins and same-process determinism. Every expectation is a pinned
/// literal whose derivation is written out in the comments; nothing is recomputed from the
/// implementation's formulas at test time. This file makes no claim about native GDI output: the
/// level-to-byte palette mapping stays an approximation ([APPROX:COMPAT-003] at
/// TrueTypeRasterizer.cs) and no GDI capture is compared against here.
/// </summary>
/// <remarks>
/// Overlap with existing tests (checked before authoring): <see cref="GdiGrayCoverageTests"/> pins the
/// 16/8/4/1-sample bytes, the quarter-pixel sample centres, half-open spans, a dropped sliver and
/// 17-value set membership on a triangle; <see cref="TrueTypeRasterizerGoldenTests"/> pins scanline
/// window semantics, an empty bitmap and a composite hash in the <see cref="CoverageModel.Linear"/>
/// model; <see cref="TrueTypeFontTests"/> pins font-path behaviour and original-font hashes that need
/// game data. This file adds the per-level constructive table, exact slanted-edge bytes, multi-row
/// half-open rows, negative origins, empty-glyph paths (including the font path) and repeatability,
/// none of which are pinned elsewhere. The analytic sample-boundary fixtures use dyadic
/// coordinates, so the pinned arithmetic is exact rather than float-noise dependent.
/// </remarks>
[TestClass]
public class GdiGray4CoverageContractTests
{
	/// <summary>Pixel-space rectangle as font-space points (pixel y-down becomes font y-up), clockwise in pixel space.</summary>
	private static IReadOnlyList<TrueTypePoint> Rectangle( float left, float top, float right, float bottom )
	{
		static TrueTypePoint P( float x, float y ) => new( x, -y, true );
		return new[] { P( left, top ), P( left, bottom ), P( right, bottom ), P( right, top ) };
	}

	private static IReadOnlyList<IReadOnlyList<TrueTypePoint>> Contours( params IReadOnlyList<TrueTypePoint>[] contours ) => contours;

	private static CoverageBitmap Rasterize( IReadOnlyList<IReadOnlyList<TrueTypePoint>> outline ) =>
		TrueTypeRasterizer.Rasterize( new[] { (outline, 0f) }, 1f, 1f );

	private static CoverageBitmap Rasterize( IReadOnlyList<TrueTypePoint> contour ) => Rasterize( Contours( contour ) );

	/// <summary>
	/// A shape covering exactly <paramref name="columns"/> x <paramref name="rows"/> of the 4x4 sample
	/// grid of one pixel, plus an optional side rectangle covering <paramref name="extraColumns"/> x
	/// <paramref name="extraRows"/> more samples. Sample columns sit at x = 0.125 + 0.25k, so the span
	/// [0, 0.25n) holds exactly n of them (half-open), and likewise for the sample rows.
	/// </summary>
	private static IReadOnlyList<IReadOnlyList<TrueTypePoint>> SampleCountShape( int columns, int rows, int extraColumns, int extraRows )
	{
		var contours = new List<IReadOnlyList<TrueTypePoint>> { Rectangle( 0, 0, 0.25f * columns, 0.25f * rows ) };
		if ( extraColumns > 0 )
			contours.Add( Rectangle( 0.25f * columns, 0, 0.25f * (columns + extraColumns), 0.25f * extraRows ) );
		return contours;
	}

	/// <summary>
	/// The whole GdiGray4 byte map, one constructed shape per nonzero level: level L means L of the 16
	/// point samples inside the pixel, written as (L * 255 + 8) / 16 (integer division). Levels a single
	/// rectangle cannot produce (5, 7, 10, 11, 13, 14, 15) use the side rectangle of
	/// <see cref="SampleCountShape"/>. The mapping is asymmetric — level 8 is 128 but level 9 is 143,
	/// not 144 — which is why each byte is pinned instead of recomputed. Level 0 is the empty/dropped
	/// behaviour characterized by the empty-glyph tests below and the sliver test in
	/// <see cref="GdiGrayCoverageTests"/>.
	/// </summary>
	[DataTestMethod]
	[DataRow( 1, 1, 1, 0, 0, 16 )]
	[DataRow( 2, 2, 1, 0, 0, 32 )]
	[DataRow( 3, 3, 1, 0, 0, 48 )]
	[DataRow( 4, 1, 4, 0, 0, 64 )]
	[DataRow( 5, 1, 4, 1, 1, 80 )]
	[DataRow( 6, 1, 4, 1, 2, 96 )]
	[DataRow( 7, 1, 4, 1, 3, 112 )]
	[DataRow( 8, 2, 4, 0, 0, 128 )]
	[DataRow( 9, 2, 4, 1, 1, 143 )]
	[DataRow( 10, 2, 4, 1, 2, 159 )]
	[DataRow( 11, 2, 4, 1, 3, 175 )]
	[DataRow( 12, 3, 4, 0, 0, 191 )]
	[DataRow( 13, 3, 4, 1, 1, 207 )]
	[DataRow( 14, 3, 4, 1, 2, 223 )]
	[DataRow( 15, 3, 4, 1, 3, 239 )]
	[DataRow( 16, 4, 4, 0, 0, 255 )]
	public void EveryNonzeroSampleCountMapsToItsPinnedGrayByte( int level, int columns, int rows, int extraColumns, int extraRows, int expected )
	{
		Assert.AreEqual( level, columns * rows + extraColumns * extraRows, "the shape must cover exactly `level` of the 16 samples" );
		var bitmap = Rasterize( SampleCountShape( columns, rows, extraColumns, extraRows ) );
		Assert.AreEqual( (1, 1, 0, 0), (bitmap.Width, bitmap.Height, bitmap.OriginX, bitmap.OriginY) );
		CollectionAssert.AreEqual( new[] { (byte)expected }, bitmap.Coverage );
	}

	/// <summary>
	/// A right triangle whose hypotenuse is the line x == y (pixel space): every sub-scanline crossing
	/// lands exactly on a sample position, so the pinned arithmetic is exact. At sample row y the filled
	/// span is [0, y): pixels left of the diagonal accumulate all 4 sample columns on each of the 4
	/// sub-scanlines (16 samples -> 255), the diagonal pixel accumulates 0+1+2+3 = 6 samples
	/// (6 * 255 + 8) / 16 = 96, the pixels right of it none.
	/// </summary>
	[TestMethod]
	public void ASlantedEdgeWhoseCrossingsLandOnSamplePositionsIsPinned()
	{
		IReadOnlyList<IReadOnlyList<TrueTypePoint>> triangle = new IReadOnlyList<TrueTypePoint>[]
		{
			new[] { new TrueTypePoint( 0, 0, true ), new TrueTypePoint( 0, -4, true ), new TrueTypePoint( 4, -4, true ) }
		};
		var bitmap = Rasterize( triangle );
		Assert.AreEqual( (4, 4, 0, 0), (bitmap.Width, bitmap.Height, bitmap.OriginX, bitmap.OriginY) );
		CollectionAssert.AreEqual( new byte[] { 96, 0, 0, 0, 255, 96, 0, 0, 255, 255, 96, 0, 255, 255, 255, 96 }, bitmap.Coverage );
	}

	/// <summary>
	/// Rows [0.125, 2.375) in pixel space: a sample row y is inside when top &lt;= y &lt; bottom (the
	/// half-open edge rule), so y == top is filled and y == bottom is not. Rows 0 and 1 hold all 4
	/// sample rows (16 -> 255); row 2 holds only y = 2.125 (4 -> 64).
	/// </summary>
	[TestMethod]
	public void VerticalSampleRangeIsTopInclusiveAndBottomExclusiveAcrossRows()
	{
		var bitmap = Rasterize( Rectangle( 0, 0.125f, 1, 2.375f ) );
		Assert.AreEqual( (1, 3, 0, 0), (bitmap.Width, bitmap.Height, bitmap.OriginX, bitmap.OriginY) );
		CollectionAssert.AreEqual( new byte[] { 255, 255, 64 }, bitmap.Coverage );
	}

	/// <summary>
	/// A glyph entirely left of and above the pen origin: the sample span [-1.25, -0.75) holds one
	/// sample column each of pixels -2 and -1 (at -1.125 and -0.875), so the 2x1 bitmap is 4 samples
	/// per pixel (-> 64) and the origin lands inside it at (2, 2).
	/// </summary>
	[TestMethod]
	public void AGlyphLeftOfAndAboveTheOriginGetsPositiveOriginsAndPinnedCoverage()
	{
		var bitmap = Rasterize( Rectangle( -1.25f, -2, -0.75f, -1 ) );
		Assert.AreEqual( (2, 1, 2, 2), (bitmap.Width, bitmap.Height, bitmap.OriginX, bitmap.OriginY) );
		CollectionAssert.AreEqual( new byte[] { 64, 64 }, bitmap.Coverage );
	}

	/// <summary>
	/// Empty and degenerate outlines under the default model: no placed outlines at all, a contour with
	/// fewer than two points (skipped by flattening) and a contour whose every edge is horizontal each
	/// yield the shared <see cref="CoverageBitmap.Empty"/> instance.
	/// </summary>
	[TestMethod]
	public void EmptyAndDegenerateOutlinesProduceTheSharedEmptyBitmap()
	{
		Assert.AreEqual( CoverageBitmap.Empty,
			TrueTypeRasterizer.Rasterize( Array.Empty<(IReadOnlyList<IReadOnlyList<TrueTypePoint>>, float)>(), 1f, 1f ) );
		IReadOnlyList<IReadOnlyList<TrueTypePoint>> singlePoint = new IReadOnlyList<TrueTypePoint>[] { new[] { new TrueTypePoint( 1, -1, true ) } };
		Assert.AreEqual( CoverageBitmap.Empty, Rasterize( singlePoint ) );
		Assert.AreEqual( CoverageBitmap.Empty, Rasterize( Rectangle( 0, 2, 4, 2 ) ) );
	}

	/// <summary>
	/// The font path for an empty glyph: glyph 0 (.notdef) of a synthetic font has a zero-length loca
	/// entry, so <see cref="TrueTypeFont.GetOutline"/> returns no contours and
	/// <see cref="TrueTypeRasterizer.RasterizeGlyph"/> returns the empty bitmap whatever the scale.
	/// </summary>
	[TestMethod]
	public void AnEmptyFontGlyphRasterizesToTheEmptyBitmap()
	{
		var builder = new TrueTypeFontTests.FontBuilder();
		builder.AddSimple( 1000, 'A', TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
		var font = TrueTypeFont.Parse( builder.Build() );
		Assert.AreEqual( 0, font.GetOutline( 0 ).Count );
		Assert.AreEqual( CoverageBitmap.Empty, TrueTypeRasterizer.RasterizeGlyph( font, 0, 0.004f ) );
	}

	/// <summary>
	/// Same-process determinism: the same synthetic scene (the exact slanted triangle plus the
	/// 5-sample shape placed at pen 5) rasterized twice produces byte-identical coverage and an equal
	/// <see cref="CoverageBitmap.ComputeHash"/>. This pins repeatability only, not cross-platform float
	/// behaviour; the composite hash literal is pinned by <see cref="TrueTypeRasterizerGoldenTests"/>
	/// for the Linear model and must be generated on an exact-head CI run before being pinned here.
	/// </summary>
	[TestMethod]
	public void RepeatedRasterizationOfTheSameSceneIsByteIdentical()
	{
		IReadOnlyList<IReadOnlyList<TrueTypePoint>> triangle = new IReadOnlyList<TrueTypePoint>[]
		{
			new[] { new TrueTypePoint( 0, 0, true ), new TrueTypePoint( 0, -4, true ), new TrueTypePoint( 4, -4, true ) }
		};
		var first = TrueTypeRasterizer.Rasterize( new[] { (triangle, 0f), (SampleCountShape( 1, 4, 1, 1 ), 5f) }, 1f, 1f );
		var second = TrueTypeRasterizer.Rasterize( new[] { (triangle, 0f), (SampleCountShape( 1, 4, 1, 1 ), 5f) }, 1f, 1f );
		CollectionAssert.AreEqual( first.Coverage, second.Coverage );
		Assert.AreEqual( first.ComputeHash(), second.ComputeHash() );
		Assert.AreEqual( (6, 4, 0, 0), (first.Width, first.Height, first.OriginX, first.OriginY) );
		Assert.AreEqual( 96, first[0, 0], "the triangle's diagonal pixel" );
		Assert.AreEqual( 80, first[5, 0], "the placed 5-sample shape's pixel" );
	}

	/// <summary>
	/// Point sampling versus area coverage on one fractional shape: a rectangle spanning [0, 0.3) of a
	/// pixel column covers exactly one sample column (4 samples -> 64) under GdiGray4, while the
	/// <see cref="CoverageModel.Linear"/> model keeps the exact 30% area (77, pinned in
	/// <see cref="GdiGrayCoverageTests"/>), so the two models must disagree here.
	/// </summary>
	[TestMethod]
	public void TheGrayModelDiffersFromLinearOnFractionalCoverage()
	{
		var gray = TrueTypeRasterizer.Rasterize( new[] { (Contours( Rectangle( 0, 0, 0.3f, 1 ) ), 0f) }, 1f, 1f, CoverageModel.GdiGray4 );
		var linear = TrueTypeRasterizer.Rasterize( new[] { (Contours( Rectangle( 0, 0, 0.3f, 1 ) ), 0f) }, 1f, 1f, CoverageModel.Linear );
		CollectionAssert.AreEqual( new byte[] { 64 }, gray.Coverage );
		CollectionAssert.AreNotEqual( gray.Coverage, linear.Coverage );
	}

	/// <summary>
	/// Horizontal integer pen placement: the exact slanted triangle at penX = k (scale 1 makes
	/// (x + k) * 1f exact for integer k, so the crossing arithmetic shifts bit-for-bit). The tight
	/// bounding box shifts minX and maxX by k alike, so the dimensions stay 4 x 4, OriginX becomes
	/// -k, and the coverage is the same 16-byte literal pinned at pen 0 above, asserted here
	/// directly per shift. No claim about native GDI output.
	/// </summary>
	[DataTestMethod]
	[DataRow( -3 )]
	[DataRow( -1 )]
	[DataRow( 1 )]
	[DataRow( 3 )]
	public void AHorizontalIntegerPenShiftKeepsTheTriangleLiteral( int k )
	{
		IReadOnlyList<IReadOnlyList<TrueTypePoint>> triangle = new IReadOnlyList<TrueTypePoint>[]
		{
			new[] { new TrueTypePoint( 0, 0, true ), new TrueTypePoint( 0, -4, true ), new TrueTypePoint( 4, -4, true ) }
		};
		var bitmap = TrueTypeRasterizer.Rasterize( new[] { (triangle, (float)k) }, 1f, 1f );
		Assert.AreEqual( (4, 4, -k, 0), (bitmap.Width, bitmap.Height, bitmap.OriginX, bitmap.OriginY) );
		CollectionAssert.AreEqual( new byte[] { 96, 0, 0, 0, 255, 96, 0, 0, 255, 255, 96, 0, 255, 255, 255, 96 }, bitmap.Coverage );
	}

	/// <summary>
	/// Vertical integer placement: the pinned half-open span shape moved by +dy in pixel space via
	/// its coordinate offsets. The tight bounding box tracks the row samples, so the dimensions stay
	/// 1 x 3, OriginY becomes -dy, and the coverage is the same 3-byte literal pinned for [0.125,
	/// 2.375) above (top-inclusive first row fully sampled, bottom row exactly one sample),
	/// asserted here directly per shift. No claim about native GDI output.
	/// </summary>
	[DataTestMethod]
	[DataRow( -3 )]
	[DataRow( -1 )]
	[DataRow( 1 )]
	[DataRow( 3 )]
	public void AVerticalIntegerShiftKeepsTheRowProfileLiteral( int dy )
	{
		var bitmap = Rasterize( Contours( Rectangle( 0, 0.125f + dy, 1, 2.375f + dy ) ) );
		Assert.AreEqual( (1, 3, 0, -dy), (bitmap.Width, bitmap.Height, bitmap.OriginX, bitmap.OriginY) );
		CollectionAssert.AreEqual( new byte[] { 255, 255, 64 }, bitmap.Coverage );
	}
}
