using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic regressions for the current single-precision host contract, not evidence of native FPU precision.</summary>
[TestClass]
public class ShopGoodsCostPrecisionTests
{
	[TestMethod]
	public void ByteSettingsAndIngredientClassesMatchAnExactRationalOracle()
	{
		int[] settings = { int.MinValue, -257, -256, -1, 0, 1, 49, 50, 51, 100, 149, 150, 151, 255, 256, 257, 306, 511, int.MaxValue };
		int[] ingredients = { -1, 0, 1, 2, 3, 4, 257, 259 };
		foreach (var quality in settings)
		foreach (var amount in settings)
		foreach (var ingredient in ingredients)
			Check( 16_777_217u, quality, amount, ingredient );
	}

	[TestMethod]
	public void UnsignedCostBoundariesAndClampedTermsMatchTheOracle()
	{
		uint[] costs = { 0, 1, 7, 200, 333, (1u << 24) - 1, 1u << 24, (1u << 24) + 1,
			(1u << 24) + 3, (1u << 29) - 1, 1u << 29, (1u << 29) + 1,
			(1u << 31) - 1, 1u << 31, (1u << 31) + 1, uint.MaxValue - 1, uint.MaxValue };
		int[] settings = { 0, 49, 50, 51, 100, 149, 150, 151, 255 };
		foreach (var cost in costs)
		foreach (var quality in settings)
		foreach (var amount in settings)
		foreach (var ingredient in new[] { 1, 2, 3, 4 })
			Check( cost, quality, amount, ingredient );
	}

	[TestMethod]
	public void HalfwayProductsRoundToEvenBeforeTruncationAndSignedWrapping()
	{
		// Factor one makes the uint itself a binary32 midpoint. Test both even endpoints.
		Assert.AreEqual( 16_777_216, ShopGoodsCost.Of( 16_777_217, 50, 50, 2 ) );
		Assert.AreEqual( 16_777_220, ShopGoodsCost.Of( 16_777_219, 50, 50, 2 ) );
		Assert.AreEqual( int.MinValue, ShopGoodsCost.Of( int.MaxValue, 50, 50, 2 ) );
		Assert.AreEqual( 0, ShopGoodsCost.Of( -1, 50, 50, 2 ) );
		Assert.AreEqual( 8, ShopGoodsCost.Of( 7, 100, 50, 2 ) );
	}

	[TestMethod]
	public void ExactIntegerCostMustNotBeRoundedBeforeMultiplication()
	{
		AssertDiscriminator( 16_777_217u, 51, 50, 2, 16_861_104, 16_861_102 );
		AssertDiscriminator( 16_777_217u, 77, 13, 1, 22_145_926, 22_145_924 );
		AssertDiscriminator( 16_777_217u, 100, 50, 2, 20_971_522, 20_971_520 );
		AssertDiscriminator( 16_777_219u, 77, 13, 1, 22_145_928, 22_145_930 );
	}

	private static void AssertDiscriminator( uint cost, int quality, int amount, int ingredient, int expected, int prematurelyRounded )
	{
		var factor = Factor( quality, amount, ingredient );
		Assert.AreEqual( expected, ToBookedInt( RoundSingle( new Rational( cost ) * factor ) ) );
		Assert.AreEqual( prematurelyRounded, ToBookedInt( RoundSingle( RoundSingle( new Rational( cost ) ) * factor ) ) );
		Assert.AreNotEqual( expected, prematurelyRounded );
		Assert.AreEqual( expected, ShopGoodsCost.Of( unchecked((int)cost), quality, amount, ingredient ) );
	}

	private static void Check( uint cost, int quality, int amount, int ingredient )
	{
		var expected = ToBookedInt( RoundSingle( new Rational( cost ) * Factor( quality, amount, ingredient ) ) );
		Assert.AreEqual( expected, ShopGoodsCost.Of( unchecked((int)cost), quality, amount, ingredient ),
			$"cost={cost}, quality={quality}, amount={amount}, ingredient={ingredient}" );
	}

	private static Rational Factor( int quality, int amount, int ingredient )
	{
		var ingredientTerm = Term( amount );
		if ( ingredient == 1 || ingredient == 3 ) ingredientTerm = -ingredientTerm;
		return RoundSingle( RoundSingle( Term( quality ) + ingredientTerm ) + new Rational( 1 ) );
	}

	private static Rational Term( int setting )
	{
		// Exact rational value of binary32 0.005 (0x3BA3D70A), not decimal 1/200.
		var value = RoundSingle( new Rational( (setting & 255) - 50 ) * new Rational( 5_368_709, 1_073_741_824 ) );
		var half = new Rational( 1, 2 );
		return Compare( value, -half ) < 0 ? -half : Compare( value, half ) > 0 ? half : value;
	}

	private static int ToBookedInt( Rational value )
	{
		var integer = value.Numerator / value.Denominator;
		var modulus = BigInteger.One << 32;
		var wrapped = integer % modulus;
		if ( wrapped >= (BigInteger.One << 31) ) wrapped -= modulus;
		return (int)wrapped;
	}

	// Independent rational quantizer: find a power-of-two interval and divide by its
	// binary32 spacing. No float/double arithmetic, bit decoding or production helpers.
	// All nonzero values in this suite are normal finite binary32 values.
	private static Rational RoundSingle( Rational value )
	{
		if ( value.Numerator.Sign < 0 ) return -RoundSingle( -value );
		if ( value.Numerator.IsZero ) return value;
		var exponent = 0;
		while ( Compare( value, PowerOfTwo( exponent ) ) < 0 ) exponent--;
		while ( Compare( value, PowerOfTwo( exponent + 1 ) ) >= 0 ) exponent++;
		var spacing = PowerOfTwo( exponent - 23 );
		var numerator = value.Numerator * spacing.Denominator;
		var denominator = value.Denominator * spacing.Numerator;
		var kept = BigInteger.DivRem( numerator, denominator, out var remainder );
		var comparison = (remainder * 2).CompareTo( denominator );
		if ( comparison > 0 || comparison == 0 && !kept.IsEven ) kept++;
		return new Rational( kept ) * spacing;
	}

	private static Rational PowerOfTwo( int exponent ) => exponent >= 0
		? new Rational( BigInteger.One << exponent ) : new Rational( BigInteger.One, BigInteger.One << -exponent );
	private static int Compare( Rational a, Rational b ) => (a.Numerator * b.Denominator).CompareTo( b.Numerator * a.Denominator );

	private readonly struct Rational
	{
		public readonly BigInteger Numerator;
		public readonly BigInteger Denominator;
		public Rational( BigInteger numerator ) : this( numerator, BigInteger.One ) { }
		public Rational( BigInteger numerator, BigInteger denominator )
		{
			var divisor = BigInteger.GreatestCommonDivisor( BigInteger.Abs( numerator ), denominator );
			Numerator = numerator / divisor;
			Denominator = denominator / divisor;
		}
		public static Rational operator -( Rational value ) => new Rational( -value.Numerator, value.Denominator );
		public static Rational operator +( Rational a, Rational b ) => new Rational( a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator );
		public static Rational operator *( Rational a, Rational b ) => new Rational( a.Numerator * b.Numerator, a.Denominator * b.Denominator );
	}
}
