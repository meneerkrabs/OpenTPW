using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Numerics;

namespace OpenTPW.Tests;

/// <summary>The emulated x87 registers (<see cref="Ext"/>): each operation rounds to the precision control as the instruction does.</summary>
[TestClass]
public class X87Tests
{
	private static float Random( System.Random random, float scale ) => (float)((random.NextDouble() * 2 - 1) * scale * Math.Pow( 2, random.Next( -8, 8 ) ));

	[TestMethod]
	public void SinglePrecisionOperationsEqualFloatArithmeticOnFloatOperands()
	{
		var random = new System.Random( 24 );
		X87Precision.With( 24, () =>
		{
			for ( var i = 0; i < 20000; i++ )
			{
				var a = Random( random, 100f );
				var b = Random( random, 100f );
				var x = Ext.FromFloat( a );
				var y = Ext.FromFloat( b );
				Assert.AreEqual( a + b, (x + y).ToFloat(), $"{a} + {b}" );
				Assert.AreEqual( a - b, (x - y).ToFloat(), $"{a} - {b}" );
				Assert.AreEqual( a * b, (x * y).ToFloat(), $"{a} * {b}" );
				Assert.AreEqual( a / b, (x / y).ToFloat(), $"{a} / {b}" );
				var m = Math.Abs( a );
				Assert.AreEqual( MathF.Sqrt( m ), Ext.Sqrt( Ext.FromFloat( m ) ).ToFloat(), $"sqrt {m}" );
			}
			return 0;
		} );
	}

	[TestMethod]
	public void DoublePrecisionOperationsEqualDoubleArithmetic()
	{
		var random = new System.Random( 53 );
		X87Precision.With( 53, () =>
		{
			for ( var i = 0; i < 20000; i++ )
			{
				var a = random.NextDouble() * 1000 - 500;
				var b = random.NextDouble() * 10 - 5;
				var x = Ext.FromDouble( a );
				var y = Ext.FromDouble( b );
				Assert.AreEqual( a + b, (x + y).ToDouble(), $"{a} + {b}" );
				Assert.AreEqual( a * b, (x * y).ToDouble(), $"{a} * {b}" );
				Assert.AreEqual( a / b, (x / y).ToDouble(), $"{a} / {b}" );
				Assert.AreEqual( Math.Sqrt( Math.Abs( a ) ), Ext.Sqrt( Ext.FromDouble( Math.Abs( a ) ) ).ToDouble(), $"sqrt {a}" );
			}
			return 0;
		} );
	}

	[TestMethod]
	public void ExtendedPrecisionKeepsSixtyFourBitsThatDoubleLoses()
	{
		X87Precision.With( 64, () =>
		{
			var third = Ext.One / Ext.FromInt( 3 );
			var back = third * Ext.FromInt( 3 );
			Assert.AreEqual( 0, back.CompareTo( Ext.One ), "1/3*3 rounds back to 1 at 64 bits" );
			var tiny = Ext.One + Ext.FromDouble( Math.Pow( 2, -60 ) );
			Assert.IsTrue( tiny.CompareTo( Ext.One ) > 0, "2^-60 survives at 64 bits" );
			return 0;
		} );
		X87Precision.With( 53, () =>
		{
			var tiny = Ext.One + Ext.FromDouble( Math.Pow( 2, -60 ) );
			Assert.AreEqual( 0, tiny.CompareTo( Ext.One ), "2^-60 is lost at 53 bits" );
			return 0;
		} );
	}

	[TestMethod]
	public void CancellationRoundsToNearestEvenAndTruncationMovesTowardZero()
	{
		X87Precision.With( 24, () =>
		{
			// 2^24 + 1 is a tie between 2^24 and 2^24 + 2 at 24 bits: even wins
			var tie = Ext.FromInt( 1 << 24 ) + Ext.One;
			Assert.AreEqual( 1 << 24, tie.Truncate() );
			var above = Ext.FromInt( (1 << 24) + 2 ) + Ext.One;
			Assert.AreEqual( (1 << 24) + 4, above.Truncate() );
			Assert.AreEqual( 0, (Ext.One - Ext.One).CompareTo( Ext.Zero ) );
			Assert.AreEqual( -2, Ext.FromDouble( -2.9 ).Truncate() );
			Assert.AreEqual( 2, Ext.FromDouble( 2.5 ).RoundToInteger().Truncate() );
			Assert.AreEqual( 4, Ext.FromDouble( 3.5 ).RoundToInteger().Truncate() );
			return 0;
		} );
	}

	[TestMethod]
	public void PowAgreesWithTheLibraryToWithinSinglePrecision()
	{
		var random = new System.Random( 7 );
		X87Precision.With( 24, () =>
		{
			for ( var i = 0; i < 2000; i++ )
			{
				var x = (float)(random.NextDouble() * 0.99 + 0.01);
				var y = (float)(random.NextDouble() * 60);
				var power = Ext.Pow( Ext.FromFloat( x ), Ext.FromFloat( y ) ).ToDouble();
				var expected = Math.Pow( x, y );
				Assert.IsTrue( Math.Abs( power - expected ) <= expected * 1e-6 + 1e-30, $"pow({x}, {y}) = {power}, expected {expected}" );
			}
			return 0;
		} );
	}

	/// <summary>An exact value m * 2^e, as the independent integer reference below sees it.</summary>
	private readonly record struct Exact( BigInteger Mantissa, int Exponent )
	{
		public Ext ToExt() => Ext.FromInt( (long)Mantissa ).Scale( Exponent );
		public (BigInteger Numerator, BigInteger Denominator) Rational() => Exponent >= 0 ? (Mantissa << Exponent, BigInteger.One) : (Mantissa, BigInteger.One << -Exponent);
	}

	/// <summary>The positive rational <paramref name="numerator"/>/<paramref name="denominator"/> rounded to nearest-even at <paramref name="bits"/> mantissa bits, found with plain integer arithmetic.</summary>
	private static Exact RoundReference( BigInteger numerator, BigInteger denominator, int bits, bool squareRoot = false )
	{
		var low = BigInteger.One << (bits - 1);
		var high = BigInteger.One << bits;
		var shift = bits - 1 - (int)(numerator.GetBitLength() - denominator.GetBitLength()) / (squareRoot ? 2 : 1);
		BigInteger Scaled( int s, out BigInteger scaledNumerator, out BigInteger scaledDenominator )
		{
			var factor = squareRoot ? 2 * s : s;
			scaledNumerator = factor >= 0 ? numerator << factor : numerator;
			scaledDenominator = factor >= 0 ? denominator : denominator << -factor;
			if ( squareRoot )
			{
				var q = scaledNumerator / scaledDenominator;
				var root = (BigInteger)Math.Sqrt( (double)q );
				while ( root * root > q ) root--;
				while ( (root + 1) * (root + 1) <= q ) root++;
				return root;
			}
			return scaledNumerator / scaledDenominator;
		}
		var n = Scaled( shift, out var sn, out var sd );
		while ( n >= high ) n = Scaled( --shift, out sn, out sd );
		while ( n < low ) n = Scaled( ++shift, out sn, out sd );
		bool up;
		if ( squareRoot )
		{
			// compare (n + 1/2)^2 with sn / sd: (2n + 1)^2 * sd against 4 * sn
			var left = (2 * n + 1) * (2 * n + 1) * sd;
			var right = 4 * sn;
			up = left < right || (left == right && !n.IsEven);
		}
		else
		{
			var twice = 2 * (sn - n * sd);
			up = twice > sd || (twice == sd && !n.IsEven);
		}
		if ( up ) n++;
		return new Exact( n, -shift );
	}

	[TestMethod]
	public void RegisterWidthOperandsRoundToTheCurrentPrecisionLikeAnIntegerReference()
	{
		var random = new System.Random( 64 );
		Exact Operand()
		{
			var mantissa = (BigInteger)(random.NextInt64( 1L << random.Next( 30, 62 ), 1L << 62 ) | 1);
			return new Exact( random.Next( 4 ) == 0 ? -mantissa : mantissa, random.Next( -40, 40 ) );
		}
		void Check( Ext actual, Exact expected, bool negative, string what )
		{
			var wanted = negative ? expected.ToExt().Negate() : expected.ToExt();
			Assert.AreEqual( 0, actual.CompareTo( wanted ), what );
		}
		foreach ( var bits in new[] { 24, 53 } )
		X87Precision.With( bits, () =>
		{
			for ( var i = 0; i < 4000; i++ )
			{
				var a = Operand();
				var b = Operand();
				var (an, ad) = a.Rational();
				var (bn, bd) = b.Rational();
				var x = a.ToExt();
				var y = b.ToExt();
				var what = $"{bits} bits: {a} {b}";
				// a * b
				var product = an * bn;
				Check( x * y, RoundReference( BigInteger.Abs( product ), ad * bd, bits ), product.Sign < 0, "mul " + what );
				// a / b
				Check( x / y, RoundReference( BigInteger.Abs( an ) * bd, ad * BigInteger.Abs( bn ), bits ), an.Sign != bn.Sign, "div " + what );
				// a + b and a - b
				foreach ( var subtract in new[] { false, true } )
				{
					var sum = an * bd + (subtract ? -bn * ad : bn * ad);
					var denominator = ad * bd;
					if ( sum.IsZero )
					{
						Assert.AreEqual( 0, (subtract ? x - y : x + y).CompareTo( Ext.Zero ), "cancel " + what );
						continue;
					}
					Check( subtract ? x - y : x + y, RoundReference( BigInteger.Abs( sum ), denominator, bits ), sum.Sign < 0, (subtract ? "sub " : "add ") + what );
				}
				// sqrt |a|
				Check( Ext.Sqrt( x.IsNegative ? x.Negate() : x ), RoundReference( BigInteger.Abs( an ), ad, bits, squareRoot: true ), false, "sqrt " + what );
			}
			return 0;
		} );
	}
}
