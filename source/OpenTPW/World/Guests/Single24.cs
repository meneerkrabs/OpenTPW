using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The x87 as tp.exe's thread runs it, precision control 24 (CW 0x007F, X87Precision): a product or a sum of a single and an exact operand is rounded once to a 24-bit mantissa. The exact
/// operand is a double constant or an integer; the rounding is the nearest, ties to even, so a result is what a store to a single would give. Pure rules.
/// </summary>
internal static class Single24
{
	/// <summary><c>fld</c> a single, <c>fmul</c> a double: the exact product rounded once to a 24-bit mantissa.</summary>
	public static float Product( float a, double b )
	{
		if ( a == 0f || b == 0d || float.IsNaN( a ) || double.IsNaN( b ) || float.IsInfinity( a ) || double.IsInfinity( b ) )
			return (float)((double)a * b);
		var (ma, ea) = Split( (double)a );
		var (mb, eb) = Split( b );
		var negative = (a < 0f) != (b < 0d);
		var product = (UInt128)ma * mb;
		var exponent = ea + eb;
		var length = 128 - (int)UInt128.LeadingZeroCount( product );
		var shift = length - 24;
		if ( shift > 0 )
		{
			var remainder = product & (((UInt128)1 << shift) - 1);
			var half = (UInt128)1 << (shift - 1);
			product >>= shift;
			exponent += shift;
			if ( remainder > half || remainder == half && (product & 1) != 0 )
				product++;
		}
		var value = MathF.ScaleB( (float)(ulong)product, exponent );
		return negative ? -value : value;
	}

	/// <summary><c>_ftol</c> (0x0067A830): the truncation toward zero; a value beyond the 32-bit range, or NaN, is the integer indefinite 0x80000000.</summary>
	public static int Truncate( float value ) => float.IsNaN( value ) || value >= 2147483648f || value < -2147483648f ? int.MinValue : (int)value;

	private static (ulong Mantissa, int Exponent) Split( double value )
	{
		var bits = BitConverter.DoubleToInt64Bits( Math.Abs( value ) );
		var exponent = (int)((bits >> 52) & 0x7FF);
		var fraction = (ulong)(bits & 0xFFFFFFFFFFFFFL);
		return exponent == 0 ? (fraction, -1074) : (fraction | (1UL << 52), exponent - 1075);
	}
}
