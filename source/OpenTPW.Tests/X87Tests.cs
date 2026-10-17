using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

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
}
