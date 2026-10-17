using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The paint-mask shaping of tp.exe 0x5EBF10 (COMPAT-003): offset copy (0x5EA3F0), erosion (0x5EA6D0) and dilation
/// (0x5EA510) by the mask word, and the separable Gaussian (0x5EA8A0) by the parameter, checked on synthetic masks.
/// </summary>
[TestClass]
public class SignMaskShapingTests
{
	private static SignPaint Paint( int word = 0, float parameter = 0f, int dx = 0, int dy = 0 ) => new( 255, 255, 255, 255, word, parameter, dx, dy );

	[TestMethod]
	public void ShiftCopiesCoverageUnchangedAtTheOffsets()
	{
		var mask = new byte[4 * 3];
		mask[1 * 4 + 1] = 77;
		var shifted = SignMaskShaping.Shift( mask, 4, 3, 1, -1 );
		Assert.AreEqual( 77, shifted[0 * 4 + 2], "coverage is copied as it is, not made binary" );
		Assert.AreEqual( 77, shifted.Sum( value => value ) );
		Assert.AreEqual( 0, SignMaskShaping.Shift( mask, 4, 3, 4, 0 ).Sum( value => value ), "a pixel moved off the mask is lost" );
	}

	[TestMethod]
	public void DilationTakesTheWindowMaximumAlongRowsThenColumns()
	{
		var mask = new byte[5 * 5];
		mask[2 * 5 + 2] = 90;
		mask[0] = 30;
		SignMaskShaping.Dilate( mask, 5, 5, 1 );
		for ( var y = 1; y <= 3; y++ )
		for ( var x = 1; x <= 3; x++ )
			Assert.AreEqual( 90, mask[y * 5 + x], $"({x}, {y})" );
		Assert.AreEqual( 30, mask[0], "the window is clipped at the corner, not treated as empty" );
		Assert.AreEqual( 30, mask[1 * 5 + 0] );
		Assert.AreEqual( 0, mask[4 * 5 + 4] );
	}

	[TestMethod]
	public void ErosionTakesTheWindowMinimumAndClearsTheBorderBand()
	{
		var mask = Enumerable.Repeat( (byte)200, 6 * 6 ).ToArray();
		mask[3 * 6 + 3] = 50;
		SignMaskShaping.Erode( mask, 6, 6, 1 );
		for ( var i = 0; i < 6; i++ )
		{
			Assert.AreEqual( 0, mask[i], "top row" );
			Assert.AreEqual( 0, mask[5 * 6 + i], "bottom row" );
			Assert.AreEqual( 0, mask[i * 6], "left column" );
			Assert.AreEqual( 0, mask[i * 6 + 5], "right column" );
		}
		Assert.AreEqual( 50, mask[2 * 6 + 2], "the low pixel spreads over its 3x3 window" );
		Assert.AreEqual( 50, mask[4 * 6 + 4] );
		Assert.AreEqual( 200, mask[1 * 6 + 1] );
	}

	[TestMethod]
	public void KernelHasTwoAndAHalfSigmaPlusThreeTapsAndSumsToOne()
	{
		var kernel = SignMaskShaping.Kernel( 1f );
		Assert.AreEqual( 5, kernel.Length );
		// the taps are single-precision values (the original's x87 runs at 24 bits), stored as doubles
		Assert.AreEqual( 0.40261994689424746, kernel[2], 1e-7 );
		Assert.AreEqual( 0.24420134200323335, kernel[1], 1e-7 );
		Assert.AreEqual( kernel[0], kernel[4] );
		Assert.AreEqual( 1.0, kernel.Sum(), 1e-6 );
		// 3 + trunc(2.5 * 1.2) = 6 taps: an even kernel, centred on tap (6 - 1) / 2 = 2, so it reaches one further right.
		var even = SignMaskShaping.Kernel( 1.2f );
		Assert.AreEqual( 6, even.Length );
		Assert.AreEqual( even[1], even[3], 1e-15 );
		Assert.IsTrue( even[2] > even[1] && even[5] < even[0] );
	}

	[TestMethod]
	public void BlurTruncatesEachPassAndDropsTapsOffTheMask()
	{
		// Values from the native order: horizontal pass truncated to bytes, then the (one-row) vertical pass on those bytes.
		var line = new byte[] { 0, 0, 200, 0, 0 };
		SignMaskShaping.Blur( line, 5, 1, 1f );
		CollectionAssert.AreEqual( new byte[] { 4, 19, 32, 19, 4 }, line );
		var plateau = Enumerable.Repeat( (byte)255, 8 ).ToArray();
		SignMaskShaping.Blur( plateau, 8, 1, 1f );
		// Height 1: the vertical pass keeps only the centre tap, so measure the row on its own (a column of 1 always scales by the centre tap).
		var row = Enumerable.Repeat( (byte)255, 8 * 9 ).ToArray();
		SignMaskShaping.Blur( row, 8, 9, 1f );
		Assert.AreEqual( 255, row[4 * 8 + 4], "full coverage away from the edges stays full" );
		Assert.IsTrue( row[4 * 8 + 0] < 255 && row[4 * 8 + 0] == 178, $"taps off the edge are dropped without renormalizing, got {row[4 * 8]}" );
		Assert.IsTrue( plateau[0] < plateau[3] );
	}

	[TestMethod]
	public void ShapeShiftsThenGrowsOrShrinksThenBlurs()
	{
		var mask = new byte[7 * 7];
		mask[3 * 7 + 3] = 255;
		// Zero word and zero parameter: only the shift.
		var shifted = SignMaskShaping.Shape( mask, 7, 7, Paint( dx: 1, dy: 1 ) );
		Assert.AreEqual( 255, shifted[4 * 7 + 4] );
		Assert.AreEqual( 255, shifted.Sum( value => value ) );
		Assert.AreEqual( 255, mask[3 * 7 + 3], "the input stays unchanged" );
		// A positive word dilates after the shift.
		var grown = SignMaskShaping.Shape( mask, 7, 7, Paint( word: 1, dx: 1 ) );
		Assert.AreEqual( 9 * 255, grown.Sum( value => value ) );
		Assert.AreEqual( 255, grown[2 * 7 + 5] );
		// A negative word erodes: a single pixel vanishes.
		Assert.AreEqual( 0, SignMaskShaping.Shape( mask, 7, 7, Paint( word: -1 ) ).Sum( value => value ) );
		// A nonzero parameter blurs; NaN does not (the native compare treats unordered as equal to 0.0).
		Assert.IsTrue( SignMaskShaping.Shape( mask, 7, 7, Paint( parameter: 1f ) )[3 * 7 + 2] > 0 );
		CollectionAssert.AreEqual( mask, SignMaskShaping.Shape( mask, 7, 7, Paint( parameter: float.NaN ) ) );
		Assert.ThrowsException<NotSupportedException>( () => SignMaskShaping.Shape( mask, 7, 7, Paint( parameter: float.PositiveInfinity ) ) );
	}

	[TestMethod]
	public void ABlurWithoutTapsClearsTheMask()
	{
		// 3 + trunc(2.5 * -1.2) = 0 taps: the native passes run over empty ranges and write 0 everywhere.
		Assert.AreEqual( 0, SignMaskShaping.Kernel( -1.2f ).Length );
		var mask = Enumerable.Repeat( (byte)200, 4 * 3 ).ToArray();
		SignMaskShaping.Blur( mask, 4, 3, -1.2f );
		Assert.AreEqual( 0, mask.Sum( value => value ) );
		Assert.AreEqual( 0, SignMaskShaping.Shape( Enumerable.Repeat( (byte)90, 9 ).ToArray(), 3, 3, Paint( parameter: -2f ) ).Sum( value => value ) );
	}
}
