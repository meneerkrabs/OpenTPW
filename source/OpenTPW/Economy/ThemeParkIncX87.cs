using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Theme Park Inc's x87 arithmetic at the game thread's control word <c>0x7F</c> (24-bit significand, round to nearest even): every <c>fadd</c>, <c>fmul</c> and <c>fdiv</c>
/// rounds its exact result once to a single, while the loads (<c>fild</c>, <c>flds</c>) are exact. Two single operands combine through C# <see cref="float"/> arithmetic,
/// which rounds the same way; these helpers take an integer operand, whose 32 or 64 bits need not fit a single's significand, and round the exact result once.
/// </summary>
// [BIN:TPI-EXE:0x6BF33F control word] the CRT start-up (_cinit 0x87043A through the pointer 0x9F6F18 to 0x86F430, then 0x8759B5: _controlfp(_PC_53, _MCW_PC)) leaves 53 bits; DirectDraw's
// SetCooperativeLevel with DDSCL_FPUSETUP and without DDSCL_FPUPRESERVE (0x851 at 0x6BF33F, 0x808 at 0x6BF5A8) lets the Direct3D device creation set the control word 0x7F and keep it, as tp.exe's
// 0x563914/0x563B92. Nothing on the game thread puts 53 bits back: _fpreset (0x880B57) and _control87 (0x880B0C) have no caller but the start-up's _controlfp; the renderer's pair 0x7071E0
// (precision cleared) and 0x707210 (the saved word back) brackets each frame (0x6DD38B/0x6DD7FC, 0x6DDB03/0x6DDF7A), and its extended-precision restore 0x707230 runs only when bit 1 of the render
// state 0xBE2328 is set, which no writer sets (initial value 0x3000141 at 0x6F2705, as tp.exe's 0x8BD508); the renderer's check-and-set 0x707160 (through the thunk 0x40B2DF) has no caller;
// the Bullfrog screen library's guard 0x812560 (0x7D5A98) changes only the exception masks, and its own SetCooperativeLevel (0x7D5AAD: 0x51 or 0x08, with 0x800 only under a flag, never
// FPUPRESERVE) creates no Direct3D device; 0x47AC30 sets rounding toward zero and restores the word at 0x47ADA2, 0x86DF20 masks the exceptions (0x3F) and restores it at 0x86E364, and the C
// runtime's other control-word writers (_ftol 0x86FAB0, 0x86B720 and the math helpers 0x87A70F..0x87C545) restore the word they found
// [EXT:hardware-d3d-path] OpenTPW models the hardware Direct3D path only: a device without DrawPrimitives2 would leave the CRT's 53 bits (COMPAT-018)
internal static class ThemeParkIncX87
{
	// The common operands take allocation-free paths: an integer of at most 2^24 is itself a single, and for +, - and / of two singles the double result rounded to a single is the single
	// result (53 >= 2 x 24 + 2, so the double rounding is innocuous); a product of a 29-bit integer and a 24-bit significand is exact in a double; a quotient with a 32-bit divisor is decided
	// in 128-bit integers. Only the rare larger operands take BigInteger.
	private const long SingleInteger = 1L << 24;
	private const long ExactProductInteger = 1L << 29;

	/// <summary><c>fild</c> <paramref name="numerator"/>, <c>fidiv</c> <paramref name="denominator"/>: the quotient rounded once to a single.</summary>
	public static float Quotient( long numerator, long denominator )
	{
		if ( numerator == 0 || denominator == 0 || Math.Abs( numerator ) <= SingleInteger && Math.Abs( denominator ) <= SingleInteger )
			return (float)((double)numerator / denominator);
		var negative = numerator < 0 != denominator < 0;
		if ( numerator != long.MinValue && Math.Abs( denominator ) <= uint.MaxValue )
		{
			var value = WideQuotient( (ulong)Math.Abs( numerator ), (ulong)Math.Abs( denominator ) );
			return negative ? -value : value;
		}
		return Round( BigInteger.Abs( numerator ), BigInteger.Abs( denominator ), 0, negative );
	}

	/// <summary>The exact <paramref name="integer"/> (<c>fild</c>, <c>fimul</c>) times <paramref name="single"/>, rounded once to a single.</summary>
	public static float Product( long integer, float single )
	{
		if ( integer == 0 || single == 0f || !float.IsFinite( single ) || Math.Abs( integer ) < ExactProductInteger )
			return (float)(integer * (double)single);
		var (significand, exponent) = Decompose( single );
		return Round( BigInteger.Abs( integer ) * BigInteger.Abs( significand ), BigInteger.One, exponent, integer < 0 != significand < 0 );
	}

	/// <summary>The exact <paramref name="integer"/> (<c>fild</c>, <c>fiadd</c>) plus <paramref name="single"/>, rounded once to a single.</summary>
	public static float Sum( long integer, float single )
	{
		if ( integer == 0 || single == 0f || !float.IsFinite( single ) || Math.Abs( integer ) <= SingleInteger )
			return (float)(integer + (double)single);
		var (significand, exponent) = Decompose( single );
		var total = exponent >= 0 ? new BigInteger( integer ) + (new BigInteger( significand ) << exponent) : (new BigInteger( integer ) << -exponent) + significand;
		if ( total.IsZero )
			return 0f;
		return Round( BigInteger.Abs( total ), BigInteger.One, Math.Min( exponent, 0 ), total.Sign < 0 );
	}

	/// <summary><paramref name="single"/> divided by the exact <paramref name="integer"/> (<c>fidiv</c>), rounded once to a single.</summary>
	public static float Divide( float single, long integer )
	{
		if ( integer == 0 || single == 0f || !float.IsFinite( single ) || Math.Abs( integer ) <= SingleInteger )
			return (float)(single / (double)integer);
		var (significand, exponent) = Decompose( single );
		return Round( BigInteger.Abs( significand ), BigInteger.Abs( integer ), exponent, integer < 0 != significand < 0 );
	}

	/// <summary>The exact product of an integer and a double (<c>fild</c>, <c>fmull</c>), rounded once to a single (round to nearest even), where C#'s double arithmetic would round twice.</summary>
	public static float ProductOfDouble( long integer, double value )
	{
		if ( integer == 0 || value == 0 || double.IsNaN( value ) || double.IsInfinity( value ) )
			return (float)(integer * value);
		var bits = BitConverter.DoubleToInt64Bits( value );
		var exponent = (int)((bits >> 52) & 0x7FF);
		var mantissa = bits & 0xFFFFFFFFFFFFFL;
		if ( exponent == 0 )
			exponent = 1;
		else
			mantissa |= 1L << 52;
		var negative = (bits < 0) ^ (integer < 0);
		var magnitude = (UInt128)(ulong)Math.Abs( integer ) * (ulong)mantissa;
		// value = magnitude × 2^(exponent − 1075); round magnitude to 24 significant bits (nearest even)
		var length = 128 - (int)UInt128.LeadingZeroCount( magnitude );
		var shift = length - 24;
		var scale = exponent - 1075;
		if ( shift > 0 )
		{
			var half = (UInt128)1 << (shift - 1);
			var remainder = magnitude & (((UInt128)1 << shift) - 1);
			magnitude >>= shift;
			if ( remainder > half || remainder == half && (magnitude & 1) != 0 )
				magnitude++;
			scale += shift;
		}
		var result = Math.ScaleB( (double)(ulong)magnitude, scale );
		return (float)(negative ? -result : result);
	}

	/// <summary>
	/// <c>_ftol</c> (<c>0x86FAB0</c>) as its callers read it: <c>fistp qword</c> with truncation and the low dword in eax; a value outside the 64-bit range, an infinity or a NaN is the
	/// integer indefinite <c>0x8000000000000000</c>, whose low dword is 0.
	/// </summary>
	public static int Ftol( float value ) =>
		float.IsFinite( value ) && Math.Abs( value ) < 9223372036854775808f ? unchecked( (int)(long)value ) : 0;

	/// <summary><c>_ftol</c> of an extended value (<see cref="Ftol(float)"/>): truncation toward zero, the low dword of the 64-bit result; outside the 64-bit range, an infinity or a NaN gives 0.</summary>
	public static int Ftol( double value ) =>
		double.IsFinite( value ) && Math.Abs( value ) < 9.2233720368547758E18 ? unchecked( (int)(long)Math.Truncate( value ) ) : 0;

	/// <summary><paramref name="numerator"/> / <paramref name="divisor"/> (a divisor below 2^32) rounded to a single, ties to even, in 128-bit integers.</summary>
	private static float WideQuotient( ulong numerator, ulong divisor )
	{
		// the quotient scaled by 2^64 has at least 33 bits, so its 24 leading bits and the rest decide the rounding exactly
		var scaled = (UInt128)numerator << 64;
		var quotient = scaled / divisor;
		var rest = scaled % divisor;
		var shift = 128 - (int)UInt128.LeadingZeroCount( quotient ) - 24;
		var significand = quotient >> shift;
		var dropped = quotient & ((UInt128.One << shift) - 1);
		var half = UInt128.One << (shift - 1);
		if ( dropped > half || dropped == half && (rest != 0 || (significand & 1) != 0) )
			significand++;
		return (float)Math.ScaleB( (double)significand, shift - 64 );
	}

	/// <summary>A finite nonzero single as <c>significand × 2^exponent</c>, the significand a signed integer of at most 24 bits.</summary>
	private static (long Significand, int Exponent) Decompose( float single )
	{
		var bits = BitConverter.SingleToInt32Bits( single );
		var field = (bits >> 23) & 0xFF;
		long significand = bits & 0x7FFFFF;
		int exponent;
		if ( field == 0 )
			exponent = -149;
		else
		{
			significand |= 0x800000;
			exponent = field - 150;
		}
		return (bits < 0 ? -significand : significand, exponent);
	}

	/// <summary><c>numerator / denominator × 2^exponent</c> (both positive) rounded to a 24-bit significand, ties to even.</summary>
	private static float Round( BigInteger numerator, BigInteger denominator, int exponent, bool negative )
	{
		// scale the quotient to at least 26 bits, so its 24 leading bits, the next and the remainder decide the rounding exactly
		var shift = 26 - (int)(numerator.GetBitLength() - denominator.GetBitLength());
		if ( shift > 0 )
			numerator <<= shift;
		else
			denominator <<= -shift;
		exponent -= shift;
		var quotient = BigInteger.DivRem( numerator, denominator, out var remainder );
		var drop = (int)quotient.GetBitLength() - 24;
		var significand = quotient >> drop;
		var dropped = quotient - (significand << drop);
		var half = BigInteger.One << (drop - 1);
		if ( dropped > half || dropped == half && (!remainder.IsZero || !significand.IsEven) )
			significand += 1;
		var value = (float)Math.ScaleB( (double)significand, exponent + drop );
		return negative ? -value : value;
	}
}
