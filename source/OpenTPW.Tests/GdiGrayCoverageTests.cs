using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The GDI ANTIALIASED_QUALITY model of <see cref="TrueTypeRasterizer"/> (<see cref="CoverageModel.GdiGray4"/>, COMPAT-003):
/// 4x4 point samples per pixel (17 levels) written as (level * 255 + 8) / 16. Expectations are pinned bytes.
/// </summary>
[TestClass]
public class GdiGrayCoverageTests
{
	private static IReadOnlyList<IReadOnlyList<TrueTypePoint>> Rectangle( float left, float top, float right, float bottom )
	{
		// Pixel space is y down: font y is the negated pixel y. Clockwise in pixel space, as the rasterizer's winding expects.
		static TrueTypePoint P( float x, float y ) => new( x, -y, true );
		return new[] { new[] { P( left, top ), P( left, bottom ), P( right, bottom ), P( right, top ) } };
	}

	private static CoverageBitmap Rasterize( IReadOnlyList<IReadOnlyList<TrueTypePoint>> outline ) =>
		TrueTypeRasterizer.Rasterize( new[] { (outline, 0f) }, 1f, 1f );

	[TestMethod]
	public void TheDefaultModelIsTheGdiGrayModel()
	{
		var outline = Rectangle( 0, 0, 1.5f, 1 );
		CollectionAssert.AreEqual(
			TrueTypeRasterizer.Rasterize( new[] { (outline, 0f) }, 1f, 1f, CoverageModel.GdiGray4 ).Coverage,
			Rasterize( outline ).Coverage );
	}

	[TestMethod]
	public void LevelsAreSampleCountsOutOfSixteenMappedToBytes()
	{
		// Pixel (0,0): a full pixel is 16 samples (255); the left half of pixel 1 is 2 columns x 4 rows = 8 samples -> (8 * 255 + 8) / 16 = 128.
		var half = Rasterize( Rectangle( 0, 0, 1.5f, 1 ) );
		CollectionAssert.AreEqual( new byte[] { 255, 128 }, half.Coverage );
		// One sub-scanline of four: 4 samples -> (4 * 255 + 8) / 16 = 64.
		CollectionAssert.AreEqual( new byte[] { 64 }, Rasterize( Rectangle( 0, 0, 1, 0.25f ) ).Coverage );
		// One sample of sixteen -> (255 + 8) / 16 = 16.
		CollectionAssert.AreEqual( new byte[] { 16 }, Rasterize( Rectangle( 0, 0, 0.25f, 0.25f ) ).Coverage );
	}

	[TestMethod]
	public void SamplesSitAtQuarterPixelCentresAndSpansAreHalfOpen()
	{
		// Sample columns of pixel 0 are at x = 0.125, 0.375, 0.625, 0.875; [0.125, 0.375) holds the first only (and all four rows: 4 samples -> 64).
		CollectionAssert.AreEqual( new byte[] { 64 }, Rasterize( Rectangle( 0.125f, 0, 0.375f, 1 ) ).Coverage );
		// A sliver between two sample columns covers none of them, so the model drops it (point sampling, no dropout control).
		var sliver = TrueTypeRasterizer.Rasterize( new[] { (Rectangle( 0.13f, 0, 0.37f, 1 ), 0f) }, 1f, 1f );
		Assert.AreEqual( 0, sliver.Coverage.Count( value => value != 0 ) );
	}

	[TestMethod]
	public void OnlySeventeenByteValuesOccur()
	{
		// A diagonal edge crosses every sample count; every output byte must be one of the 17 level values.
		static TrueTypePoint P( float x, float y ) => new( x, -y, true );
		var triangle = new IReadOnlyList<TrueTypePoint>[] { new[] { P( 0, 0 ), P( 0, 9 ), P( 11, 9 ) } };
		var bitmap = Rasterize( triangle );
		var allowed = Enumerable.Range( 0, 17 ).Select( level => (byte)((level * 255 + 8) / 16) ).ToHashSet();
		Assert.IsTrue( bitmap.Coverage.All( allowed.Contains ) );
		Assert.IsTrue( bitmap.Coverage.Distinct().Count() > 8, "a slanted edge produces many different levels" );
	}

	[TestMethod]
	public void TheLinearModelStaysAvailableAndExact()
	{
		// The linear model keeps exact fractional coverage: 1/4 of a pixel is 64 (63.75 rounded), not a sampled count.
		var bitmap = TrueTypeRasterizer.Rasterize( new[] { (Rectangle( 0, 0, 0.3f, 1 ), 0f) }, 1f, 1f, CoverageModel.Linear );
		CollectionAssert.AreEqual( new byte[] { 77 }, bitmap.Coverage );
	}
}
