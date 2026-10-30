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

	/// <summary><c>_ftol</c> (0x0067A830): the truncation toward zero stored as a 64-bit integer, of which the caller takes the low dword; a value beyond the 64-bit range, NaN or infinity is the 64-bit integer indefinite 0x8000000000000000, whose low dword is 0.</summary>
	public static int Truncate( float value ) => float.IsNaN( value ) || value >= 9.223372E18f || value < -9.223372E18f ? 0 : unchecked((int)(long)value);

	private static (ulong Mantissa, int Exponent) Split( double value )
	{
		var bits = BitConverter.DoubleToInt64Bits( Math.Abs( value ) );
		var exponent = (int)((bits >> 52) & 0x7FF);
		var fraction = (ulong)(bits & 0xFFFFFFFFFFFFFL);
		return exponent == 0 ? (fraction, -1074) : (fraction | (1UL << 52), exponent - 1075);
	}
}
