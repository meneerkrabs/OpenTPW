using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The word-pair arithmetic of <see cref="Ext"/> (docs/BROWSER-PERFORMANCE.md, "Event hitches") against the 128-bit form it replaced, kept here
/// verbatim, and the blur loops' zero-byte skip against the loops without it.
/// </summary>
[TestClass]
public class X87FastPathTests
{
	// the former Make, Add and multiply (UInt128 arithmetic), on (negative, mantissa, exponent) triples
	private static (bool Negative, ulong Mantissa, int Exponent) LegacyMake( bool negative, UInt128 magnitude, int exponent, bool sticky, int precision )
	{
		if ( magnitude == 0 )
			return (false, 0, 0);
		var top = 127 - (int)UInt128.LeadingZeroCount( magnitude );
		var shift = top - (precision - 1);
		UInt128 kept;
		if ( shift > 0 )
		{
			var mask = ((UInt128)1 << shift) - 1;
			var dropped = magnitude & mask;
			kept = magnitude >> shift;
			var half = (UInt128)1 << (shift - 1);
			if ( dropped > half || (dropped == half && (sticky || (kept & 1) == 1)) )
			{
				kept++;
				if ( kept >> precision != 0 )
				{
					kept >>= 1;
					shift++;
				}
			}
		}
		else
			kept = magnitude << -shift;
		var mantissa = (ulong)(kept << (64 - precision));
		return (negative, mantissa, mantissa == 0 ? 0 : exponent + shift - (64 - precision));
	}

	private static (bool Negative, ulong Mantissa, int Exponent) LegacyAdd( (bool Negative, ulong Mantissa, int Exponent) a, (bool Negative, ulong Mantissa, int Exponent) b, int precision )
	{
		if ( a.Mantissa == 0 && b.Mantissa == 0 )
			return (false, 0, 0);
		if ( a.Mantissa == 0 )
			return LegacyMake( b.Negative, b.Mantissa, b.Exponent, false, precision );
		if ( b.Mantissa == 0 )
			return LegacyMake( a.Negative, a.Mantissa, a.Exponent, false, precision );
		var (big, small) = (a, b);
		if ( a.Exponent < b.Exponent || (a.Exponent == b.Exponent && a.Mantissa < b.Mantissa) )
			(big, small) = (b, a);
		var distance = big.Exponent - small.Exponent;
		var bigMagnitude = (UInt128)big.Mantissa << 62;
		var smallMagnitude = (UInt128)small.Mantissa << 62;
		var sticky = false;
		if ( distance >= 126 )
		{
			sticky = true;
			smallMagnitude = 0;
		}
		else if ( distance > 0 )
		{
			sticky = (smallMagnitude & (((UInt128)1 << distance) - 1)) != 0;
			smallMagnitude >>= distance;
		}
		var exponent = big.Exponent - 62;
		if ( big.Negative == small.Negative )
			return LegacyMake( big.Negative, bigMagnitude + smallMagnitude, exponent, sticky, precision );
		var difference = bigMagnitude - smallMagnitude - (sticky ? (UInt128)1 : 0);
		if ( difference == 0 && !sticky )
			return (false, 0, 0);
		return LegacyMake( big.Negative, difference, exponent, sticky, precision );
	}

	private static double RandomValue( Random random )
	{
		// mantissas with many trailing zeros (exact ties), near-equal values and values many binades apart
		var kind = random.Next( 6 );
		var mantissa = kind switch
		{
			0 => (double)random.Next( 1, 1 << 20 ),
			1 => Math.ScaleB( random.Next( 1, 1 << 24 ), -random.Next( 0, 60 ) ),
			2 => 1.0 + random.Next( 0, 4 ) * Math.Pow( 2, -random.Next( 20, 52 ) ),
			_ => random.NextDouble() + 0.5,
		};
		var value = Math.ScaleB( mantissa, random.Next( -140, 140 ) );
		return random.Next( 2 ) == 0 ? -value : value;
	}

	[TestMethod]
	public void AddAndMultiplyEqualTheirUInt128Form()
	{
		var random = new Random( 87 );
		foreach ( var precision in new[] { 24, 53, 64 } )
			X87Precision.With( precision, () =>
			{
				for ( var i = 0; i < 60000; i++ )
				{
					var a = Ext.FromDouble( RandomValue( random ) );
					var b = Ext.FromDouble( RandomValue( random ) );
					// a partner within a few ulps of a, where cancellation and sticky bits happen
					var near = a + a.Scale( -random.Next( 18, 70 ) ) * Ext.FromInt( random.Next( 1, 8 ) );
					foreach (var other in new[] { b, near, a.Negate() })
					{
						Assert.AreEqual( LegacyAdd( a.Parts, other.Parts, precision ), (a + other).Parts, $"add at {precision} bits" );
						Assert.AreEqual( LegacyAdd( a.Parts, other.Negate().Parts, precision ), (a - other).Parts, $"subtract at {precision} bits" );
						var product = a.IsZero || other.IsZero ? (false, 0UL, 0) : LegacyMake( a.Parts.Negative != other.Parts.Negative, (UInt128)a.Parts.Mantissa * other.Parts.Mantissa, a.Parts.Exponent + other.Parts.Exponent, false, precision );
						Assert.AreEqual( product, (a * other).Parts, $"multiply at {precision} bits" );
					}
				}
				return 0;
			} );
	}

	[TestMethod]
	public void FarApartAndEqualMagnitudeOperandsRoundAsBefore()
	{
		foreach ( var precision in new[] { 24, 53, 64 } )
			X87Precision.With( precision, () =>
			{
				var one = Ext.One;
				foreach ( var power in new[] { 0, 1, 23, 24, 25, 52, 53, 54, 62, 63, 64, 65, 100, 125, 126, 127, 200 } )
				{
					var small = one.Scale( -power );
					Assert.AreEqual( LegacyAdd( one.Parts, small.Parts, precision ), (one + small).Parts, $"1 + 2^-{power} at {precision}" );
					Assert.AreEqual( LegacyAdd( one.Parts, small.Negate().Parts, precision ), (one - small).Parts, $"1 - 2^-{power} at {precision}" );
				}
				Assert.IsTrue( (one - one).IsZero );
				return 0;
			} );
	}

	[TestMethod]
	public void ByteValuesAreFromInt()
	{
		for ( var value = 0; value < 256; value++ )
			Assert.AreEqual( Ext.FromInt( value ).Parts, Ext.FromByte( (byte)value ).Parts );
	}

	[TestMethod]
	public void MaskBlurEqualsTheBlurThatAddsEveryTap()
	{
		var random = new Random( 5 );
		foreach ( var sigma in new[] { 0.7f, 1.5f, 3.2f, 6f } )
		{
			var (width, height) = (37, 29);
			var mask = new byte[width * height];
			// mostly empty, a few glyph-like blocks and noise
			for ( var y = 5; y < 20; y++ )
				for ( var x = 8; x < 30; x++ )
					mask[y * width + x] = (x + y) % 5 == 0 ? (byte)0 : (byte)random.Next( 1, 256 );
			var expected = (byte[])mask.Clone();
			ReferenceBlur( expected, width, height, sigma );
			var actual = (byte[])mask.Clone();
			SignMaskShaping.Blur( actual, width, height, sigma );
			CollectionAssert.AreEqual( expected, actual, $"sigma {sigma}" );
		}
	}

	[TestMethod]
	public void ScaledTapsAreTheProductsTheBlurFormed()
	{
		var random = new Random( 9 );
		foreach ( var precision in new[] { 24, 53 } )
			X87Precision.With( precision, () =>
			{
				var taps = Ext.GaussianTaps( 2.4f, Enumerable.Range( 0, 9 ).Select( k => (k - 4) * (k - 4) ).ToArray() );
				var scaled = new ScaledTaps( taps );
				var first = scaled.For( 255 );
				// more distinct values than the table keeps: the later ones are made each time and equal too
				for ( var i = 0; i < 400; i++ )
				{
					var value = (byte)random.Next( 1, 256 );
					var products = scaled.For( value );
					for ( var tap = 0; tap < taps.Length; tap++ )
						Assert.AreEqual( (Ext.FromInt( value ) * taps[tap]).Parts, products[tap].Parts );
				}
				Assert.AreSame( first, scaled.For( 255 ), "a value met early stays in the table" );
				return 0;
			} );
	}

	[TestMethod]
	public void ReliefTermsReusedForRepeatedPixelsEqualAFreshShade()
	{
		// a fresh relief computes every term of its first pixel; the shared one reuses the normal and strip terms of the pixels it met before
		var random = new Random( 11 );
		foreach ( var parameters in new[] { new[] { 3f, 0f, 0.35f, 0.8f, 0.6f, 12f, 30f, 45f }, new[] { 6f, 0f, 0.2f, 1.1f, 0.9f, 40f, 60f, 135f } } )
			foreach ( var precision in new[] { 24, 53 } )
				X87Precision.With( precision, () =>
				{
					var shared = new SignRelief( parameters );
					for ( var i = 0; i < 600; i++ )
					{
						// few distinct differences and bytes, as a sign's shape and strip give, so most pixels repeat terms
						var (dx, dy) = (random.Next( -6, 7 ) * 9, random.Next( -6, 7 ) * 9);
						var (a, b, c) = ((byte)(random.Next( 8 ) * 32), (byte)(random.Next( 8 ) * 32), (byte)(random.Next( 8 ) * 32));
						Assert.AreEqual( new SignRelief( parameters ).Shade( dx, dy, a, b, c ), shared.Shade( dx, dy, a, b, c ), $"pixel {i}, precision {precision}" );
					}
					return 0;
				} );
		// a relief reuses nothing it made at another precision control: one that met a pixel at 24 bits shades it at 64 as one that never met it
		var light = new[] { 3f, 0f, 0.35f, 0.8f, 0.6f, 12f, 30f, 45f };
		var used = new SignRelief( light );
		var unused = new SignRelief( light );
		used.Shade( 40, -30, 200, 100, 50 );
		Assert.AreEqual( X87Precision.With( 64, () => unused.Shade( 40, -30, 200, 100, 50 ) ), X87Precision.With( 64, () => used.Shade( 40, -30, 200, 100, 50 ) ) );
	}

	/// <summary>The mask blur as written before the zero skip: every tap added, bytes through <see cref="Ext.FromInt"/>.</summary>
	private static void ReferenceBlur( byte[] mask, int width, int height, float sigma )
	{
		var kernel = Array.ConvertAll( SignMaskShaping.Kernel( sigma ), Ext.FromDouble );
		var centre = (kernel.Length - 1) / 2;
		byte Convolve( byte[] line, int at, int length )
		{
			var sum = Ext.Zero;
			for ( var i = Math.Max( 0, at - centre ); i <= Math.Min( length - 1, at + kernel.Length - centre - 1 ); i++ )
				sum += Ext.FromInt( line[i] ) * kernel[i - at + centre];
			return unchecked( (byte)sum.Truncate() );
		}
		var line = new byte[Math.Max( width, height )];
		for ( var y = 0; y < height; y++ )
		{
			Array.Copy( mask, y * width, line, 0, width );
			for ( var x = 0; x < width; x++ )
				mask[y * width + x] = Convolve( line, x, width );
		}
		for ( var x = 0; x < width; x++ )
		{
			for ( var y = 0; y < height; y++ )
				line[y] = mask[y * width + x];
			for ( var y = 0; y < height; y++ )
				mask[y * width + x] = Convolve( line, y, height );
		}
	}
}
