using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The shape a sign relief effect shades (COMPAT-003, tp.exe 0x5EB820): binary ink, eroded once per step of the effect's mask word
/// (0x5EB270 with the step 0x5EA1F0), then blurred by the first parameter where the glyphs have coverage (0x5EB3E0), on synthetic masks.
/// </summary>
[TestClass]
public class SignEffectShapeTests
{
	private static byte[] Block( int width, int height, int left, int top, int right, int bottom, byte value = 255 )
	{
		var mask = new byte[width * height];
		for ( var y = top; y <= bottom; y++ )
		for ( var x = left; x <= right; x++ )
			mask[y * width + x] = value;
		return mask;
	}

	[TestMethod]
	public void ZeroStepsGiveTheBinaryInkOfTheCoverage()
	{
		var coverage = Block( 6, 6, 1, 1, 3, 3, 7 );
		coverage[4 * 6 + 4] = 255;
		var shape = SignEffectShape.Build( coverage, 6, 6, 0, 0f );
		for ( var i = 0; i < shape.Length; i++ )
			Assert.AreEqual( coverage[i] != 0 ? 255 : 0, shape[i], $"pixel {i}" );
	}

	[TestMethod]
	public void EachErosionStepKeepsASmoothstepLevelOnThePixelsItRemoves()
	{
		// 11x11 with a 7x7 block at 2..8: two steps remove the outer ring (level 0: u = 1 gives _ftol(-256), low byte 0)
		// and the next ring (u = 0.5 gives _ftol(256 * (0.25 - 0.75)) = -128, low byte 128); the 3x3 core survives as 255.
		var coverage = Block( 11, 11, 2, 2, 8, 8 );
		var shape = SignEffectShape.Build( coverage, 11, 11, 2, 0f );
		Assert.AreEqual( 0, shape[2 * 11 + 2], "outer ring" );
		Assert.AreEqual( 0, shape[5 * 11 + 2], "outer ring edge" );
		Assert.AreEqual( 128, shape[3 * 11 + 3], "second ring" );
		Assert.AreEqual( 128, shape[3 * 11 + 5], "second ring edge" );
		Assert.AreEqual( 255, shape[4 * 11 + 4], "core" );
		Assert.AreEqual( 255, shape[6 * 11 + 6], "core corner" );
		Assert.AreEqual( 0, shape[0], "outside the glyphs" );
	}

	[TestMethod]
	public void TheLevelOfAStepIsTheLowByteOfTheTruncatedSmoothstep()
	{
		// N = 4: u = 0.75 gives 256 * (2 * 0.421875 - 3 * 0.5625) = -216 (low byte 40), u = 0.5 gives -128 (128), u = 0.25 gives -40 (216).
		var coverage = Block( 21, 21, 2, 2, 18, 18 );
		var shape = SignEffectShape.Build( coverage, 21, 21, 4, 0f );
		Assert.AreEqual( 0, shape[2 * 21 + 10], "step 0: u = 1" );
		Assert.AreEqual( 40, shape[3 * 21 + 10], "step 1: u = 0.75" );
		Assert.AreEqual( 128, shape[4 * 21 + 10], "step 2: u = 0.5" );
		Assert.AreEqual( 216, shape[5 * 21 + 10], "step 3: u = 0.25" );
		Assert.AreEqual( 255, shape[6 * 21 + 10], "the core" );
	}

	[TestMethod]
	public void ImageEdgesDoNotErodeAndTheLastTwoColumnsNeverLookRight()
	{
		// Neighbours outside the image are skipped, so a fully covered mask keeps all its ink.
		Assert.IsTrue( SignEffectShape.Build( Block( 8, 8, 0, 0, 7, 7 ), 8, 8, 3, 0f ).All( value => value == 255 ) );
		// One uncovered pixel in the last column: its left neighbour (x = w - 2) does not look right, the pixels above and below it (x = w - 1) do look down and up.
		var coverage = Block( 6, 6, 0, 0, 5, 5 );
		coverage[2 * 6 + 5] = 0;
		var shape = SignEffectShape.Build( coverage, 6, 6, 1, 0f );
		Assert.AreEqual( 255, shape[2 * 6 + 4], "the pixel left of the hole never looks right" );
		Assert.AreEqual( 255, shape[1 * 6 + 4], "its up-right neighbour is not looked at either" );
		Assert.AreEqual( 0, shape[1 * 6 + 5], "the pixel above the hole erodes" );
		Assert.AreEqual( 0, shape[3 * 6 + 5], "the pixel below the hole erodes" );
	}

	[TestMethod]
	public void TheLastTwoRowsNeverLookDown()
	{
		var coverage = Block( 6, 6, 0, 0, 5, 5 );
		coverage[5 * 6 + 2] = 0;
		var shape = SignEffectShape.Build( coverage, 6, 6, 1, 0f );
		Assert.AreEqual( 255, shape[4 * 6 + 2], "row h - 2 does not look at the row below" );
		Assert.AreEqual( 255, shape[4 * 6 + 1] );
	}

	[TestMethod]
	public void TheKernelIsASquareGaussianNormalisedToOne()
	{
		var kernel = SignEffectShape.Kernel( 1f, out var size );
		Assert.AreEqual( 5, size, "3 + trunc(2.5 sigma)" );
		Assert.AreEqual( 25, kernel.Length );
		Assert.AreEqual( 1.0, kernel.Sum(), 1e-6, "single-precision taps" );
		Assert.AreEqual( kernel[0], kernel[4] );
		Assert.AreEqual( kernel[2 * 5 + 2], kernel.Max() );
		var even = SignEffectShape.Kernel( 1.2f, out var evenSize );
		Assert.AreEqual( 6, evenSize );
		Assert.AreEqual( 36, even.Length );
		Assert.IsTrue( even[2 * 6 + 2] > even[2 * 6 + 3] || even[2 * 6 + 2] == even[2 * 6 + 3], "an even kernel leans right: the centre is tap (n - 1) / 2" );
		Assert.AreEqual( 0, SignEffectShape.Kernel( -1.2f, out var empty ).Length );
		Assert.AreEqual( 0, empty );
		Assert.ThrowsException<NotSupportedException>( () => SignEffectShape.Kernel( float.PositiveInfinity, out _ ) );
	}

	[TestMethod]
	public void TheBlurRunsOnlyForAPositiveParameterAndOnlyWhereTheGlyphsHaveCoverage()
	{
		var coverage = Block( 12, 12, 4, 4, 7, 7 );
		var bevel = SignEffectShape.Build( coverage, 12, 12, 1, 0f );
		CollectionAssert.AreEqual( bevel, SignEffectShape.Build( coverage, 12, 12, 1, -1f ), "a negative parameter skips the blur" );
		CollectionAssert.AreEqual( bevel, SignEffectShape.Build( coverage, 12, 12, 1, float.NaN ), "NaN skips the blur" );
		var blurred = SignEffectShape.Build( coverage, 12, 12, 1, 1f );
		for ( var i = 0; i < blurred.Length; i++ )
			if ( coverage[i] == 0 )
				Assert.AreEqual( bevel[i], blurred[i], $"pixel {i} has no coverage and keeps its shape" );
		Assert.IsTrue( blurred[5 * 12 + 5] != bevel[5 * 12 + 5], "a covered pixel takes the blurred sum" );
		Assert.IsTrue( blurred[4 * 12 + 4] < 255, "the corner of the block mixes in the zero outside" );
	}

	[TestMethod]
	public void TheBlurClampsCoordinatesToTheNearestEdgeAndReadsThePreBlurShape()
	{
		// Every pixel is covered and 255: clamping keeps every window full, so no pixel loses weight at the edges (a dropped tap would).
		var coverage = Block( 7, 7, 0, 0, 6, 6 );
		var shape = SignEffectShape.Build( coverage, 7, 7, 0, 1f );
		Assert.IsTrue( shape.All( value => value >= 254 ), "full windows everywhere: the sum of the taps is 1 up to rounding, truncated to 254 or 255" );
		Assert.AreEqual( shape[0], shape[6], "left and right corners agree" );
		Assert.AreEqual( shape[0], shape[6 * 7 + 3], "top and bottom edges agree" );
		// A single bright pixel spreads symmetrically from the pre-blur data, not from already blurred neighbours.
		var spike = new byte[9 * 9];
		spike[4 * 9 + 4] = 255;
		var spikeShape = SignEffectShape.Build( spike, 9, 9, 0, 1f );
		var centre = spikeShape[4 * 9 + 4];
		Assert.IsTrue( centre > 0 );
		Assert.AreEqual( 0, spikeShape[4 * 9 + 5], "no coverage there: not written" );
	}
}
