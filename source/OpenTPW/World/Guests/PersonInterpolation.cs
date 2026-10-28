namespace OpenTPW;

/// <summary>
/// Where a person (a guest, a staff member or the online avatar) is drawn between two park turns: the point the sub-turn function of tp.exe gives the sprite each frame. The turn's
/// start point is the pilot position the person's update saves at the start of every turn (+0x190/+0x194, 0x004FA870), the end is its current pilot position, and the factor is the time since the
/// park turn started over its 248 ms. Pure rules: no assets, no clock.
/// </summary>
public static class PersonInterpolation
{
	/// <summary>A park turn in milliseconds (the factor's divisor, 0x00700F94 = 1 / 248).</summary>
	public const float TurnMilliseconds = ParkCalendar.TurnMilliseconds;

	/// <summary>The balloon's trailing delay in turns (<c>0x0075C910</c>, 0.35).</summary>
	public const float TrailTurns = 0.35f;

	/// <summary>
	/// <c>0x004F9F00</c>: the point at <paramref name="t"/> between the start and the current pilot position, in 16.16 units: start + t × (current − start) with the product and the sum rounded
	/// to single precision (the thread runs at 24 mantissa bits, X87Precision) and the result truncated to an integer (<c>_ftol</c>). With <paramref name="clamped"/> the factor is held to 0..1,
	/// without it to −1..2.
	/// </summary>
	// [BIN:TPW-EXE:0x004F9F00 sub-turn point] flag set: t below 0 gives 0, above 1 gives 1 (0x007006CC, 0x007006C8); flag clear: below −1 gives −1, above 2 gives 2 (0x007006D0, 0x007006D4); then per axis fild(current − start), fmul t, fiadd start and _ftol (0x0067A830); the point is drawn at that × 10 × 2^-16 world units (0x00700698, 0x007006D8), cells × 10, height 0, and the facing is the person's own angle +0x1C
	public static (int X, int Y) Point( int startX, int startY, int currentX, int currentY, float t, bool clamped )
	{
		t = Clamp( t, clamped );
		return (Axis( startX, currentX, t ), Axis( startY, currentY, t ));
	}

	/// <summary>The factor held to the function's range: 0..1 with the flag, −1..2 without (NaN stays NaN and is held to the lower bound as the comparisons fall).</summary>
	public static float Clamp( float t, bool clamped )
	{
		if ( clamped )
			return !(t >= 0f) ? 0f : t > 1f ? 1f : t;
		return !(t >= -1f) ? -1f : t > 2f ? 2f : t;
	}

	/// <summary>One axis: <c>fild</c>(current − start), <c>fmul</c> t, <c>fiadd</c> start, <c>_ftol</c>, each x87 step rounded to a 24-bit mantissa.</summary>
	public static int Axis( int start, int current, float t )
	{
		var difference = unchecked( current - start );
		float product;
		if ( t == 0f || difference == 0 )
			product = 0f;
		else
			product = Product( difference, t );
		// the sum with the integer start, rounded once to 24 bits (the double holds both operands exactly), truncated toward zero
		var sum = (float)((double)product + start);
		return Single24.Truncate( sum );
	}

	/// <summary>The exact integer times the single, rounded once to a 24-bit mantissa (<c>fild</c>, <c>fmul</c>).</summary>
	private static float Product( int difference, float t )
	{
		// the product of the exact integer and the single t, rounded once to 24 bits: the exact integer product of the difference and t's 24-bit significand, converted once, scaled by the power of two
		var bits = BitConverter.SingleToInt32Bits( t );
		var exponent = (bits >> 23) & 0xFF;
		var fraction = bits & 0x7FFFFF;
		var significand = exponent == 0 ? fraction : fraction | 0x800000;
		var power = (exponent == 0 ? 1 : exponent) - 127 - 23;
		var exact = (long)difference * significand * (bits < 0 ? -1 : 1);
		return MathF.ScaleB( (float)exact, power );
	}
}
