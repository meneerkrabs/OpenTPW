namespace OpenTPW;

/// <summary>The ticket booth's verdict on the entrance fee (tp.exe <c>0x004FF5B0</c>), in the original's return values.</summary>
public enum BoothVerdictKind
{
	/// <summary>"Park far too expensive": the guest turns home.</summary>
	FarTooExpensive = 0,
	/// <summary>"Park on the expensive side": the guest waits at the booth, less happy, and asks again.</summary>
	Expensive = 1,
	/// <summary>"Park on the cheap side": the guest is happier and pays.</summary>
	Cheap = 2,
	/// <summary>"Your admission fee is about right": the guest pays.</summary>
	Fair = 3
}

/// <summary>
/// The booth weighs the entrance fee against what the park is worth (tp.exe <c>0x004FF5B0</c>, "Working out how exciting park is").
/// </summary>
public static class BoothVerdict
{
	/// <summary>The wait of an expensive verdict: 50 + R % 50 updates (tp.exe <c>0x004FF9D0</c>).</summary>
	public const int ExpensiveWaitBase = 50;

	/// <summary>
	/// Park turns from an expensive verdict to the next one: the verdict stores the wait N = 50 + R % 50 in the guest's counter
	/// without counting it down, each later state-4 update (one per park turn) decrements it, and the update that finds it at 0
	/// runs the verdict again, so N + 1 turns later.
	/// </summary>
	// [BIN:TPW-EXE:0x004FF9D0 booth wait] +0x200 = R % 50 + 50 at 0x004FFA77 and return; a nonzero counter is decremented and the update returns (0x004FFB01), so the verdict 0x004FF5B0 runs on the update after the one that reaches 0
	public static int ExpensiveWaitTurns( uint draw ) => ExpensiveWaitBase + (int)(draw % ExpensiveWaitBase) + 1;

	/// <summary>
	/// The park value GP = points / (N + R % (N / 2 + 1)) + base in unsigned integers, with N = <c>ExcitementToCostDivisor</c> and
	/// base = <c>MinimumEntryFee</c>.
	/// </summary>
	public static uint ParkValue( uint points, uint draw, GuestSettings settings )
	{
		var divisor = (uint)Math.Max( 1, settings.ExcitementToCostDivisor );
		return points / (draw % ((divisor >> 1) + 1) + divisor) + (uint)Math.Max( 0, settings.MinimumEntryFee );
	}

	/// <summary>
	/// The verdict: a fee and a park value both at or below the minimum entry fee are fair; otherwise, in single precision, a fee of
	/// at least <c>AveragePriceMultiplier</c> × GP is far too expensive from <c>ExpensivePriceMultiplier</c> × GP and expensive below it,
	/// and a lower fee is cheap up to <c>CheapPriceMultiplier</c> × GP and fair above it.
	/// </summary>
	// [BIN:TPW-EXE:0x4ff5b0 booth verdict] GP = S (0x4c8240) / (N + R % ((N >> 1) + 1)) + base, logged as "GP: %d, AF: %d" with AF the fee (bank +0x118); base < GP or base < AF selects the float bands k1 = +0x48, k2 = +0x4C, k3 = +0x44 of the settings at 0x785040
	public static BoothVerdictKind Decide( uint points, uint fee, uint draw, GuestSettings settings ) => Decide( ParkValue( points, draw, settings ), fee, settings );

	/// <summary>The perception ring the booth records its opinion in (<c>0x004C74B0</c> with category 0): the advisor's ticket price lines read its mean.</summary>
	public const int OpinionCategory = 0;

	/// <summary>The opinion byte a verdict records: far too expensive, expensive, cheap and fair take the four <c>PeepInfo.OpinionFor*</c> bytes in turn; null while the settings have none.</summary>
	// [BIN:TPW-EXE:0x004FF67E booth opinion] the four exits record the byte of 0x007850CC (verdict 0), 0x007850D0 (1), 0x007850D4 (2) and 0x007850D8 (3) in category 0 of the park (0x00519510, 0x004C74B0) before returning
	public static int? Opinion( BoothVerdictKind verdict, GuestSettings settings ) => verdict switch
	{
		BoothVerdictKind.FarTooExpensive => settings.OpinionForMuchTooHigh,
		BoothVerdictKind.Expensive => settings.OpinionForTooHigh,
		BoothVerdictKind.Cheap => settings.OpinionForTooLow,
		_ => settings.OpinionForAboutRight,
	};

	/// <summary>Whether the verdict records its opinion: the early fair exit, taken when the park value and the fee are both at or below the minimum entry fee, records nothing.</summary>
	// [BIN:TPW-EXE:0x004FF62E booth opinion] GP above the base falls through to the bands; otherwise a fee at or below the base returns 3 at 0x004FF71A without a record
	public static bool RecordsOpinion( uint value, uint fee, GuestSettings settings )
	{
		var minimum = (uint)Math.Max( 0, settings.MinimumEntryFee );
		return !(value <= minimum && fee <= minimum);
	}

	/// <summary>The verdict for a park value already worked out (<see cref="ParkValue"/>, or a policy's adjustment of it): the same bands as <see cref="Decide(uint, uint, uint, GuestSettings)"/>.</summary>
	public static BoothVerdictKind Decide( uint value, uint fee, GuestSettings settings )
	{
		var minimum = (uint)Math.Max( 0, settings.MinimumEntryFee );
		if ( value <= minimum && fee <= minimum )
			return BoothVerdictKind.Fair;
		var a = (float)value;
		var b = (float)fee;
		if ( settings.AveragePriceMultiplier * a <= b )
			return b >= settings.ExpensivePriceMultiplier * a ? BoothVerdictKind.FarTooExpensive : BoothVerdictKind.Expensive;
		return b <= settings.CheapPriceMultiplier * a ? BoothVerdictKind.Cheap : BoothVerdictKind.Fair;
	}
}
