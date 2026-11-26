namespace OpenTPW;

/// <summary>
/// Theme Park Inc's wage negotiation (docs/reverse/TPI-WAGES.md): the decision the results dialog makes on the player's offer against a member's demand, and the choice the player has when the
/// offer was too low.
/// </summary>
public static class ThemeParkIncWageNegotiation
{
	/// <summary>The decision's results (<c>+0x10</c> of the dialog <c>0xA60408</c>): the offer was too low, enough for now, or the whole demand.</summary>
	public const int Refused = 0, Grudging = 1, Accepted = 2;

	/// <summary>The choice of a refused offer: the dialog's two toggles of the panel <c>0xB28C</c>.</summary>
	public enum Choice
	{
		/// <summary><c>0xB28D</c>, "Accept the demand" (UITEXT 0x102), the toggle the dialog selects (<c>0x85AD05(0xB28D)</c>).</summary>
		AcceptDemand = 0xB28D,
		/// <summary><c>0xB28E</c>, "Let them leave" (UITEXT 0x103): the member is dismissed (<c>0x629040</c>).</summary>
		LetThemLeave = 0xB28E
	}

	/// <summary>
	/// The decision (<c>0x559E00</c>) on an offer: o = salary + offer and d = salary + demand; o at least d accepts (2); else the ratio o / d × 100 at least 100 − PercentageForBadWageDecision is a
	/// grudging yes (1); below it the offer is refused (0).
	/// </summary>
	// [BIN:TPI-EXE:0x559F6F wage decision] the salary (0x62B620, +0x214) plus the offer (+8) and plus the demand (+0xC) as 32-bit sums; cmp, jl: o >= d (signed) gives 2 (string 0xFF into the body
	// buffer 0xA4E56C); else fildl o, fidivl d, fmuls 100.0f (0x93AC24) against fildl (100 - PercentageForBadWageDecision (0xA0228C)): fcompp, C0 set (below) gives 0 (string 0x101 with the demand),
	// else 1 (string 0x100)
	// [BIN:TPI-EXE:0x6BF33F control word] the game thread's x87 runs at the 24-bit control word (DDSCL_FPUSETUP; docs/DETERMINISM.md), so the quotient and the product each round to a single; the
	// compared integer is exact
	public static int Decide( int salary, int offer, int demand, int percentageForBadWageDecision )
	{
		var o = unchecked( salary + offer );
		var d = unchecked( salary + demand );
		if ( o >= d )
			return Accepted;
		var ratio = (float)(SingleQuotient( o, d ) * 100f);
		return ratio < unchecked( 100 - percentageForBadWageDecision ) ? Refused : Grudging;
	}

	/// <summary><c>fildl</c> <paramref name="numerator"/>, <c>fidivl</c> <paramref name="denominator"/> at the 24-bit control word: the quotient of two signed integers rounded once to a single.</summary>
	internal static float SingleQuotient( int numerator, int denominator )
	{
		if ( numerator == 0 || denominator == 0 )
			return (float)numerator / denominator;
		var magnitude = ThemeParkIncStaffMember.SingleQuotient( (uint)Math.Abs( (long)numerator ), denominator );
		return numerator < 0 ? -magnitude : magnitude;
	}
}
