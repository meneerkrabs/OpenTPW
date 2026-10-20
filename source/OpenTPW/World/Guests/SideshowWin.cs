namespace OpenTPW;

/// <summary>
/// What a won sideshow play does to the guest inside the use routine (tp.exe <c>0x004FE1E0</c>): the prize goes into the guest's purse before the
/// effects, and the routine's tail raises the happiness by the medium step times the base-2 logarithm of the prize over the price.
/// </summary>
public static class SideshowWin
{
	// [BIN:TPW-EXE:0x004FE21D sideshow prize] type kind +0x4AC 2: the cost of goods (+0x188, 0x004E1A10) is booked through 0x004E1920 and added to the guest's purse (+0x1A0, mCash) at 0x004FE231, before the outcome 0x004FDCC0
	public static int Purse( int money, int prize ) => unchecked( money + prize );

	// [BIN:TPW-EXE:0x004FE835 sideshow win happiness] the tail (0x004FE802..0x004FE8D9) divides the cost of goods by the price (+0x194, 0x004E1A00; fidiv), takes fyl2x(ln 2, ratio) / fyl2x(ln 2, 2.0) (the base-2 logarithm), multiplies by the medium happiness step byte (0x0078505C) and truncates it with __ftol (0x0067A830: fistp qword, the low dword is the result), so a zero or infinite quotient (a free play, a prize of 0) stores the indefinite 0x8000000000000000 and answers 0
	/// <summary>The truncated happiness gain of a won play: <c>trunc(step × log2(prize / price))</c> the way the x87 and <c>__ftol</c> give it.</summary>
	public static int HappinessGain( int prize, int price, int mediumStep )
	{
		// [EXT:deterministic-math] the x87's fyl2x in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		var logarithm = price == 0 ? (prize > 0 ? double.PositiveInfinity : double.NaN) : DetMath.Log2( (double)prize / price );
		return Ftol( logarithm * (mediumStep & 0xFF) );
	}

	/// <summary><c>__ftol</c> (<c>0x0067A830</c>): truncation to a 64-bit integer, of which the low 32 bits come back; a value outside the 64-bit range or not a number stores the indefinite 0x8000000000000000, whose low dword is 0.</summary>
	public static int Ftol( double value ) =>
		double.IsNaN( value ) || value >= 9223372036854775808d || value < -9223372036854775808d ? 0 : unchecked( (int)(long)Math.Truncate( value ) );
}
