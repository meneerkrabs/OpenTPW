namespace OpenTPW;

/// <summary>
/// What a won sideshow play does to the guest inside the use routine (tp.exe <c>0x004FE1E0</c>): the prize goes into the guest's purse before the
/// effects, and the routine's tail raises the happiness by the medium step times the base-2 logarithm of the prize over the price.
/// </summary>
public static class SideshowWin
{
	// [BIN:TPW-EXE:0x004FE21D sideshow prize] type kind +0x4AC 2: the cost of goods (+0x188, 0x004E1A10) is booked through 0x004E1920 and added to the guest's purse (+0x1A0, mCash) at 0x004FE231, before the outcome 0x004FDCC0
	public static int Purse( int money, int prize ) => unchecked( money + prize );

	// [BIN:TPW-EXE:0x004FE835 sideshow win happiness] the tail (0x004FE802..0x004FE8D9) divides the cost of goods by the price (+0x194, 0x004E1A00; fidiv), takes fyl2x(ln 2, ratio) / fyl2x(ln 2, 2.0) (the base-2 logarithm), multiplies by the medium happiness step byte (0x0078505C) and truncates (0x0067A830, __ftol); a zero or non-finite quotient (a free play, a prize of 0) gives the x87 indefinite integer 0x80000000
	/// <summary>The truncated happiness gain of a won play: <c>trunc(step × log2(prize / price))</c> the way the x87 does it.</summary>
	public static int HappinessGain( int prize, int price, int mediumStep )
	{
		var logarithm = price == 0 ? (prize > 0 ? double.PositiveInfinity : double.NaN) : Math.Log2( (double)prize / price );
		var value = logarithm * (mediumStep & 0xFF);
		return double.IsFinite( value ) && value > int.MinValue - 1d && value < int.MaxValue + 1d ? (int)Math.Truncate( value ) : int.MinValue;
	}
}
