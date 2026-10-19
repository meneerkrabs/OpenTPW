using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Independent mathematical fixtures only; no original files, graphics device, clock or random stream.</summary>
[TestClass]
public class StaffSpriteMotionMathTests
{
	// N02/N06: nine axes/zero, four points at each of eight reflected boundaries, two ratio steps and six extremes.
	// These are mathematically calculated expectations, not a copied executable lookup table.
	private static readonly (int Dx, int Dy, int? Angle, int? Heading)[] movementCases =
	{
		( 0, 1000, 1024, 0 ),
		( 1000, 1000, 1280, 1 ),
		( 1000, 0, 1536, 2 ),
		( 1000, -1000, 1792, 3 ),
		( 0, -1000, 0, 4 ),
		( -1000, -1000, 256, 5 ),
		( -1000, 0, 512, 6 ),
		( -1000, 1000, 768, 7 ),
		( 0, 0, 0, 4 ),
		( 414213, 1000000, 1151, 0 ),
		( 414214, 1000000, 1151, 0 ),
		( 106, 256, 1151, 0 ),
		( 107, 256, 1153, 1 ),
		( 1000000, 414213, 1409, 2 ),
		( 1000000, 414214, 1409, 2 ),
		( 256, 106, 1409, 2 ),
		( 256, 107, 1407, 1 ),
		( 1000000, -414213, 1663, 2 ),
		( 1000000, -414214, 1663, 2 ),
		( 256, -106, 1663, 2 ),
		( 256, -107, 1665, 3 ),
		( 414213, -1000000, 1921, 4 ),
		( 414214, -1000000, 1921, 4 ),
		( 106, -256, 1921, 4 ),
		( 107, -256, 1919, 3 ),
		( -414213, -1000000, 127, 4 ),
		( -414214, -1000000, 127, 4 ),
		( -106, -256, 127, 4 ),
		( -107, -256, 129, 5 ),
		( -1000000, -414213, 385, 6 ),
		( -1000000, -414214, 385, 6 ),
		( -256, -106, 385, 6 ),
		( -256, -107, 383, 5 ),
		( -1000000, 414213, 639, 6 ),
		( -1000000, 414214, 639, 6 ),
		( -256, 106, 639, 6 ),
		( -256, 107, 641, 7 ),
		( -414213, 1000000, 897, 0 ),
		( -414214, 1000000, 897, 0 ),
		( -106, 256, 897, 0 ),
		( -107, 256, 895, 7 ),
		( 1, 257, 1024, 0 ),
		( 1, 256, 1025, 0 ),
		( 16777215, 16777215, 1280, 1 ),
		( 16777216, 16777216, 1536, 2 ),
		( int.MinValue, 0, null, null ),
		( 0, int.MinValue, null, null ),
		( int.MinValue, 1, 1024, 0 ),
		( int.MinValue, int.MinValue, 1536, 2 )
	};

	[TestMethod]
	public void N02AllFortyNineMovementOraclesIncludeReflectedQuantizationBoundariesAndInvalidAxes()
	{
		Assert.AreEqual( 49, movementCases.Length );
		foreach ( var expected in movementCases )
		{
			var message = $"movement ({expected.Dx}, {expected.Dy})";
			var valid = StaffSpriteMotionMath.TryMovementAngle( 0, 0, expected.Dx, expected.Dy, out var angle );
			Assert.AreEqual( expected.Angle.HasValue, valid, message );
			if ( !valid )
				continue;
			Assert.AreEqual( expected.Angle!.Value, angle, message );
			Assert.AreEqual( expected.Heading!.Value, StaffSpriteMotionMath.Heading( angle ), message );
		}
	}

	[TestMethod]
	public void N05All257GeneratedAnglesMatchIndependentDecimalSeriesAndMachinPi()
	{
		// Decimal series is independent of the production double Math.Atan/Math.PI generator.
		var pi = 16 * DecimalAtanSeries( 1m / 5 ) - 4 * DecimalAtanSeries( 1m / 239 );
		Assert.AreEqual( 3.141592653589793238462643m, decimal.Round( pi, 24 ) );
		for ( var i = 0; i <= 256; i++ )
		{
			var x = i / 256m;
			// Argument reduction keeps the alternating series inside [-1/3, 1/2].
			var radians = x > 0.5m ? pi / 4 + DecimalAtanSeries( (x - 1) / (x + 1) ) : DecimalAtanSeries( x );
			var expected = i == 256 ? 256 : (int)decimal.Floor( radians * 1024 / pi );
			Assert.IsTrue( StaffSpriteMotionMath.TryMovementAngle( 0, 0, -256, i, out var angle ) );
			Assert.AreEqual( expected, angle - 512, $"mathematically generated ratio entry {i}" );
		}
	}

	private static decimal DecimalAtanSeries( decimal x )
	{
		var sum = x;
		var power = x;
		for ( var n = 1; n <= 100; n++ )
		{
			power *= -x * x;
			var next = sum + power / (2 * n + 1);
			if ( next == sum )
				return sum;
			sum = next;
		}
		Assert.Fail( "The bounded decimal atan series did not converge." );
		return 0;
	}

	[TestMethod]
	public void N02All2048PublishedAnglesMatchIndependentSectorRanges()
	{
		// Explicit sector ranges, independent of the implementation's subtract/mask/shift expression.
		for ( var angle = 0; angle < 2048; angle++ )
		{
			var expected = angle switch
			{
				< 128 => 4,
				< 384 => 5,
				< 640 => 6,
				< 896 => 7,
				< 1152 => 0,
				< 1408 => 1,
				< 1664 => 2,
				< 1920 => 3,
				_ => 4
			};
			Assert.AreEqual( expected, StaffSpriteMotionMath.Heading( angle ), $"angle {angle}" );
			Assert.AreEqual( expected, StaffSpriteMotionMath.Heading( angle - 2048 ), $"negative period {angle}" );
			Assert.AreEqual( expected, StaffSpriteMotionMath.Heading( angle + 2048 ), $"positive period {angle}" );
			Assert.AreEqual( expected, StaffSpriteMotionMath.Heading( unchecked( int.MinValue + angle ) ), $"wrapped period {angle}" );
		}
	}

	[TestMethod]
	public void N06CoordinateSubtractionAndRatioShiftRemainWrappedInt32Operations()
	{
		Assert.IsTrue( StaffSpriteMotionMath.TryMovementAngle( int.MaxValue, 0, int.MinValue, 0, out var angle ) );
		Assert.AreEqual( 1536, angle, "wrapped dx is positive one" );
		Assert.IsTrue( StaffSpriteMotionMath.TryMovementAngle( int.MinValue, 0, int.MaxValue, 0, out angle ) );
		Assert.AreEqual( 512, angle, "wrapped dx is negative one" );
		Assert.IsTrue( StaffSpriteMotionMath.TryMovementAngle( 0, int.MaxValue, 0, int.MinValue, out angle ) );
		Assert.AreEqual( 1024, angle, "wrapped dy is positive one" );
		Assert.IsTrue( StaffSpriteMotionMath.TryMovementAngle( 0, int.MinValue, 0, int.MaxValue, out angle ) );
		Assert.AreEqual( 0, angle, "wrapped dy is negative one" );
		Assert.IsTrue( StaffSpriteMotionMath.TryMovementAngle( 0, 0, 16777215, 16777215, out angle ) );
		Assert.AreEqual( 1280, angle );
		Assert.IsTrue( StaffSpriteMotionMath.TryMovementAngle( 0, 0, 16777216, 16777216, out angle ) );
		Assert.AreEqual( 1536, angle, "a widened shift would incorrectly retain the diagonal angle" );
	}

	[TestMethod]
	public void N06OnlyMinimumIntAxisPairsFailAndMovingZeroIsValid()
	{
		var components = new[] { int.MinValue, int.MinValue + 1, -16777216, -256, -1, 0, 1, 256, 16777216, int.MaxValue };
		foreach ( var dx in components )
		{
			foreach ( var dy in components )
			{
				var invalidAxis = dx == int.MinValue && dy == 0 || dx == 0 && dy == int.MinValue;
				var valid = StaffSpriteMotionMath.TryMovementAngle( 0, 0, dx, dy, out var angle );
				Assert.AreEqual( !invalidAxis, valid, $"movement ({dx}, {dy})" );
				if ( valid )
					Assert.IsTrue( angle is >= 0 and <= 2047 );
			}
		}
		Assert.IsTrue( StaffSpriteMotionMath.TryMovementAngle( int.MinValue, int.MaxValue, int.MinValue, int.MaxValue, out var zero ) );
		Assert.AreEqual( 0, zero, "a continuing moving return overwrites its angle even with zero displacement" );
		Assert.AreEqual( 4, StaffSpriteMotionMath.Heading( zero ) );
	}

	// N01: complete twelve-row finite acceptance set. The chosen arithmetic is nearest p53/p64-compatible;
	// these tests do not establish the original process's precision, rounding mode or exception masks.
	[DataTestMethod]
	[DataRow( 0, 0, 26214, 0, 687173796, 61, 30 )]
	[DataRow( 0, 0, 8704, 0, 75759616, 185, 92 )]
	[DataRow( 0, 0, 4352, 0, 18939904, 250, 185 )]
	[DataRow( 0, 0, 100, 0, 10000, 250, 250 )]
	[DataRow( 0, 0, 8704, 8704, 151519232, 131, 65 )]
	[DataRow( 0, 0, 50000, 50000, 705032704, 60, 30 )]
	[DataRow( 0, 0, 65537, 0, 131073, 250, 250 )]
	[DataRow( 0, 0, 32767, 32768, 2147418113, 34, 17 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0 )]
	[DataRow( 0, 0, 50000, 0, -1794967296, 0, 0 )]
	[DataRow( 0, 0, 65536, 0, 0, 0, 0 )]
	[DataRow( int.MaxValue, 0, int.MinValue, 0, 1, 250, 250 )]
	public void N01AllTwelveIntervalVectorsPreserveWrappedSumsAndDoubleQuotients( int beforeX, int beforeY, int afterX, int afterY, int sum, int run, int walk )
	{
		var dx = unchecked( afterX - beforeX );
		var dy = unchecked( afterY - beforeY );
		Assert.AreEqual( sum, unchecked( dx * dx + dy * dy ) );
		Assert.AreEqual( run, StaffSpriteMotionMath.IntervalForStep( beforeX, beforeY, afterX, afterY, true ) );
		Assert.AreEqual( walk, StaffSpriteMotionMath.IntervalForStep( beforeX, beforeY, afterX, afterY, false ) );
		Assert.AreEqual( run, GuestSimulation.BodyIntervalForStep( beforeX, beforeY, afterX, afterY, true ), "guest semantics stay unchanged" );
		Assert.AreEqual( walk, GuestSimulation.BodyIntervalForStep( beforeX, beforeY, afterX, afterY, false ), "guest semantics stay unchanged" );
		if ( sum > 0 )
		{
			Assert.IsTrue( run is >= 34 and <= 250 );
			Assert.IsTrue( walk is >= 17 and <= 250 );
		}
	}

	[TestMethod]
	public void N01MinimumIntAxisFailuresStillHaveSafeZeroIntervals()
	{
		foreach ( var run in new[] { false, true } )
		{
			Assert.AreEqual( 0, StaffSpriteMotionMath.IntervalForStep( 0, 0, int.MinValue, 0, run ) );
			Assert.AreEqual( 0, StaffSpriteMotionMath.IntervalForStep( 0, 0, 0, int.MinValue, run ) );
		}
	}

	[TestMethod]
	public void StrikeUsesOnlyItsExistingWordAndUnsignedClampRatherThanModulo()
	{
		Assert.AreEqual( 2047, StaffSpriteMotionMath.StrikeAngle( 0, 0 ), "negative unsigned result saturates to 2047" );
		Assert.AreEqual( 2047, StaffSpriteMotionMath.StrikeAngle( 2040, 248 ), "positive overflow saturates to 2047" );
		Assert.AreEqual( 392, StaffSpriteMotionMath.StrikeAngle( 512, 8 ) );
		Assert.AreEqual( 512, StaffSpriteMotionMath.StrikeAngle( 512, 128 ) );
		// Enumerate every semantic angle and low byte; unrelated high bits must have no effect.
		for ( var angle = 0; angle < 2048; angle++ )
		{
			for ( var random = 0u; random < 256; random++ )
			{
				var candidate = angle + (int)random - 128;
				var expected = random % 8 != 0 ? angle : candidate < 0 || candidate > 2047 ? 2047 : candidate;
				Assert.AreEqual( expected, StaffSpriteMotionMath.StrikeAngle( angle, random ) );
				Assert.AreEqual( expected, StaffSpriteMotionMath.StrikeAngle( angle, random | 0xabcdef00 ) );
			}
		}
	}
}
