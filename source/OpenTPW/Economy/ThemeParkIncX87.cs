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
// state 0xBE2328 is set, which no writer sets (initial value 0x3000141 at 0x6F2705, as tp.exe's 0x8BD508); the Bullfrog screen library's guard 0x812560 (0x7D5A98) changes only the exception masks,
// and the C runtime's other control-word writers (_ftol 0x86FAB0, 0x86B720 and the math helpers 0x87A70F..0x87C545) restore the word they found
// [EXT:hardware-d3d-path] OpenTPW models the hardware Direct3D path only: a device without DrawPrimitives2 would leave the CRT's 53 bits (COMPAT-018)
internal static class ThemeParkIncX87
{
	/// <summary><c>fild</c> <paramref name="numerator"/>, <c>fidiv</c> <paramref name="denominator"/>: the quotient rounded once to a single.</summary>
	public static float Quotient( long numerator, long denominator )
	{
		if ( numerator == 0 || denominator == 0 )
			return (float)((double)numerator / denominator);
		var negative = numerator < 0 != denominator < 0;
		return Round( BigInteger.Abs( numerator ), BigInteger.Abs( denominator ), 0, negative );
	}

	/// <summary>The exact <paramref name="integer"/> (<c>fild</c>, <c>fimul</c>) times <paramref name="single"/>, rounded once to a single.</summary>
	public static float Product( long integer, float single )
	{
		if ( integer == 0 || single == 0f || !float.IsFinite( single ) )
			return (float)(integer * (double)single);
		var (significand, exponent) = Decompose( single );
		return Round( BigInteger.Abs( integer ) * BigInteger.Abs( significand ), BigInteger.One, exponent, integer < 0 != significand < 0 );
	}

	/// <summary>The exact <paramref name="integer"/> (<c>fild</c>, <c>fiadd</c>) plus <paramref name="single"/>, rounded once to a single.</summary>
	public static float Sum( long integer, float single )
	{
		if ( integer == 0 || single == 0f || !float.IsFinite( single ) )
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
		if ( integer == 0 || single == 0f || !float.IsFinite( single ) )
			return (float)(single / (double)integer);
		var (significand, exponent) = Decompose( single );
		return Round( BigInteger.Abs( significand ), BigInteger.Abs( integer ), exponent, integer < 0 != significand < 0 );
	}

	/// <summary>
	/// <c>_ftol</c> (<c>0x86FAB0</c>) as its callers read it: <c>fistp qword</c> with truncation and the low dword in eax; a value outside the 64-bit range, an infinity or a NaN is the
	/// integer indefinite <c>0x8000000000000000</c>, whose low dword is 0.
	/// </summary>
	public static int Ftol( float value ) =>
		float.IsFinite( value ) && Math.Abs( value ) < 9223372036854775808f ? unchecked( (int)(long)value ) : 0;

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
