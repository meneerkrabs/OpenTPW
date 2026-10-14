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
	public static BoothVerdictKind Decide( uint points, uint fee, uint draw, GuestSettings settings )
	{
		var value = ParkValue( points, draw, settings );
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
