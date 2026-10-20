namespace OpenTPW;

/// <summary>
/// Money categories of the original financial screens: "Money in" = gate/shop/sideshow takings
/// (UITEXT 164–167), "Money out" = staff costs (169), other costs (358) and loan payments (359).
/// <see cref="OtherIncome"/> (challenge prizes, scrap sales) and <see cref="LoanReceived"/> have no
/// original label. Loans received count as money in but not as profit.
/// </summary>
public enum LedgerCategory
{
	GateTakings,
	ShopTakings,
	SideshowTakings,
	OtherIncome,
	StaffCosts,
	OtherCosts,
	LoanPayments,
	LoanReceived
}

/// <summary>Totals of one closed month (or the open current month).</summary>
public sealed record LedgerMonth( long MonthIndex, IReadOnlyDictionary<LedgerCategory, long> Totals, long OpeningBalance, long ClosingBalance, int ParkRating, long ParkValue )
{
	// [BIN:STP-PPC:0x100CC904 loan deposit] loan proceeds are added to the "Money in" accumulator (+0x1FC90, graph toggle 0x12279 "Money in") like every other credit; instalments go to the money-out accumulator (+0x1F5A0)
	public long MoneyIn => Sum( LedgerCategory.GateTakings, LedgerCategory.ShopTakings, LedgerCategory.SideshowTakings, LedgerCategory.OtherIncome, LedgerCategory.LoanReceived );
	public long MoneyOut => Sum( LedgerCategory.StaffCosts, LedgerCategory.OtherCosts, LedgerCategory.LoanPayments );
	public long Profit => MoneyIn - this[LedgerCategory.LoanReceived] - MoneyOut;
	public long this[LedgerCategory category] => Totals.TryGetValue( category, out var value ) ? value : 0;
	private long Sum( params LedgerCategory[] categories ) => categories.Sum( category => this[category] );
}

/// <summary>
/// Integer-dollar bank account with per-category monthly totals and a bounded month history (the
/// original graph shows up to 12 years). Amounts are always positive; the category decides the sign.
/// </summary>
public sealed class ParkLedger
{
	public const int MaximumHistoryMonths = 12 * ParkCalendar.MonthsPerYear;
	private readonly Dictionary<LedgerCategory, long> current = new();
	private readonly List<LedgerMonth> history = new();

	public ParkLedger( long openingBalance )
	{
		Balance = openingBalance;
		CurrentOpeningBalance = openingBalance;
	}

	public long Balance { get; private set; }
	public long CurrentOpeningBalance { get; private set; }
	public long CurrentMonthIndex { get; private set; }
	public IReadOnlyList<LedgerMonth> History => history;
	public IReadOnlyDictionary<LedgerCategory, long> CurrentTotals => current;

	// [BIN:TPW-EXE:0x49A7B0 finance graph] the original keeps rows only for the gate (+0x1FEE0, admission 0x4D0600), shop and sideshow takings (+0x20130 / +0x20380 by Info.WhichUIType, 0x4E16B0) and the staff wages (+0x1F7F0, month wage 0x504C70, dismissal 0x505790); every other credit reaches money in (+0x1FC90, 0x4D0190) and every other debit money out (+0x1F5A0, 0x4D01F0), as MoneyIn and MoneyOut sum here
	// [BIN:TPW-EXE:0x4D0370 bank month] the month adds the bank's mBatchBalance (+0x10, serialized by 0x4CF920) to the balance and money in, but nothing adds to it in play: the constructor 0x4CF7C0, the reset message 0x13/10 (0x4D02D0) and this routine only clear it, and no caller of the bank getter 0x5195D0 writes it, so the month credits nothing without a row
	// [EXT:ECON-LEDGER-SPLIT] OpenTPW books the other credits and debits under its own categories (challenge prizes and scrap sales as other income; build, upgrade, goods, prizes, land and training as other costs; loans apart) and keeps a monthly Profit; they are trace figures that sum to the original's totals and no window shows them
	public static bool IsIncome( LedgerCategory category ) => category is LedgerCategory.GateTakings or LedgerCategory.ShopTakings or LedgerCategory.SideshowTakings or LedgerCategory.OtherIncome or LedgerCategory.LoanReceived;

	/// <summary>The year's running profit (<c>mProfitThisYear</c>): reset at each new calendar year.</summary>
	public long ProfitThisYear { get; private set; }

	// [BIN:STP-PPC:0x100CBF50 bank deposit] every credit raises the bank's mProfitThisYear (+0x124) and every debit (0x100CBFDC) lowers it; a loan deposit (0x100CC904) adds and removes its amount, and an instalment (0x100CC21C) nets to minus its interest share
	public void Post( LedgerCategory category, long amount )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( amount );
		if ( amount == 0 )
			return;
		Balance = checked(IsIncome( category ) ? Balance + amount : Balance - amount);
		current[category] = checked(current.GetValueOrDefault( category ) + amount);
		if ( category is not (LedgerCategory.LoanReceived or LedgerCategory.LoanPayments) )
			ProfitThisYear = Wrap32( checked(IsIncome( category ) ? ProfitThisYear + amount : ProfitThisYear - amount) );
	}

	/// <summary>
	/// The year-profit word (+292, read by the profit ticket as a signed value) is 32 bits wide in the original (lwz, subf or
	/// add, stw: 0xCC060, 0xCBF50, 0xCBFDC), so every update wraps; at 0 % APR a loan's interest share is about 119 million a
	/// month and the word crosses the boundary within a year (jungle Easy mode's 0 % loans). The balance, money-in and
	/// money-out words are 32-bit in the original too, but the park cannot reach 2^31 there (not wrapped, not demonstrable).
	/// </summary>
	private static long Wrap32( long value ) => unchecked((int)value);

	/// <summary>Lowers the year's profit by a loan instalment's interest share, or by an early payoff's whole-term share.</summary>
	public void ChargeLoanInterest( long interest ) => ProfitThisYear = Wrap32( ProfitThisYear - interest );

	// [BIN:STP-PPC:0x100CC120 bank message 13] the year event (type 13, from the calendar's year change 0x100E4148) sets mProfitThisYear to 0
	public void StartYear() => ProfitThisYear = 0;

	internal void RestoreProfitThisYear( long profit ) => ProfitThisYear = Wrap32( profit );

	public LedgerMonth CurrentMonth( int rating, long parkValue ) => new( CurrentMonthIndex, new Dictionary<LedgerCategory, long>( current ), CurrentOpeningBalance, Balance, rating, parkValue );

	/// <summary>Closes the current month into the history and starts <paramref name="nextMonthIndex"/>.</summary>
	public LedgerMonth CloseMonth( long nextMonthIndex, int rating, long parkValue )
	{
		var closed = CurrentMonth( rating, parkValue );
		history.Add( closed );
		if ( history.Count > MaximumHistoryMonths )
			history.RemoveAt( 0 );
		current.Clear();
		CurrentOpeningBalance = Balance;
		CurrentMonthIndex = nextMonthIndex;
		return closed;
	}

	/// <summary>Sum of closed months of one calendar year (0-based year index).</summary>
	public LedgerMonth? SummariseYear( long yearIndex )
	{
		var months = history.Where( month => month.MonthIndex / ParkCalendar.MonthsPerYear == yearIndex ).ToList();
		if ( months.Count == 0 )
			return null;
		var totals = Enum.GetValues<LedgerCategory>().ToDictionary( category => category, category => months.Sum( month => month[category] ) );
		return new LedgerMonth( yearIndex * ParkCalendar.MonthsPerYear, totals, months[0].OpeningBalance, months[^1].ClosingBalance, months[^1].ParkRating, months[^1].ParkValue );
	}

	internal void Restore( long balance, long openingBalance, long monthIndex, IReadOnlyDictionary<LedgerCategory, long> totals, IEnumerable<LedgerMonth> months )
	{
		Balance = balance;
		CurrentOpeningBalance = openingBalance;
		CurrentMonthIndex = monthIndex;
		current.Clear();
		foreach ( var (category, value) in totals )
			current[category] = value;
		history.Clear();
		history.AddRange( months );
	}
}

/// <summary>
/// An outstanding loan. The original keeps no declining balance: a bought loan is its instalment M, its term N and the
/// months repaid (bank record words, 0xcc21c / 0xccc78), so the amount still owed is derived as M × (N − months repaid).
/// </summary>
public sealed record LoanAccount( int OfferIndex, int LenderNameIndex, long OriginalAmount, int AprPercent, int Months, long MonthlyRepayment, int MonthsRepaid )
{
	public int MonthsRemaining => Months - MonthsRepaid;

	/// <summary>What early repayment costs (0xccc78): M × the months left.</summary>
	public long RemainingBalance => MonthlyRepayment * MonthsRemaining;
}

/// <summary>
/// Loan arithmetic, as the original does it (executed in the PowerPC oracle, docs/reverse/ORACLE.md F12 and F13).
/// The instalment is <c>trunc_u32(P × pow(1 + APR/100, (N/12) × 0.5) / N)</c> (0xcb7a8), paid unchanged for N months
/// (0xcc21c) with no interest on a balance and no final remainder; an early repayment (0xccc78) pays M × the months left.
/// </summary>
public static class LoanMath
{
	/// <summary>
	/// The interest share the original takes off the year's profit per instalment: (M × N − P) / N with the
	/// subtraction in unsigned 32-bit arithmetic, as 0x100CC21C computes it (so it wraps when M × N &lt; P).
	/// </summary>
	public static long InstalmentInterest( long monthlyRepayment, int months, long principal )
	{
		if ( months <= 0 )
			return 0;
		var difference = (uint)unchecked(monthlyRepayment * months - principal);
		return difference / (uint)months;
	}

	/// <summary>
	/// What an early payoff takes off the year's profit (0xCCDC0..0xCCDD0): the instalment's share q = (M × N − P) / N times
	/// the whole term N (32-bit), whatever the months already repaid; the payoff itself only leaves the balance.
	/// </summary>
	public static long PayoffInterest( long monthlyRepayment, int months, long principal ) =>
		unchecked((uint)(InstalmentInterest( monthlyRepayment, months, principal ) * months));

	/// <summary>The original's float-to-unsigned conversion (0x1c3fbc): negative gives 0, NaN and 2^32 or more give 2^32 - 1, else truncation.</summary>
	internal static long TruncateUnsigned( double value )
	{
		if ( double.IsNaN( value ) )
			return uint.MaxValue;
		if ( value < 0.0 )
			return 0;
		return value >= 4294967296.0 ? uint.MaxValue : (long)value;
	}

	/// <summary>
	/// The instalment M of an offer, 0xcb7a8 in binary64. At 0 % APR the power is 1 and M is floor(P / N), the value every
	/// original save stores; the original's M × N then falls short of P by the division remainder (100,000 over 36 months
	/// repays 99,972) and OpenTPW follows it.
	/// </summary>
	public static long MonthlyRepayment( long amount, int aprPercent, int months )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( months );
		ArgumentOutOfRangeException.ThrowIfNegative( amount );
		// [EXT:deterministic-math] the C runtime's pow in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		var power = DetMath.Pow( 1.0 + aprPercent / 100.0, months / 12.0 * 0.5 );
		return TruncateUnsigned( amount * power / months );
	}

	/// <summary>What the loan costs in all: M × N.</summary>
	public static long TotalPayable( long amount, int aprPercent, int months ) => MonthlyRepayment( amount, aprPercent, months ) * months;

	public static LoanAccount Open( LoanOffer offer ) => new( offer.Index, offer.LenderNameIndex, offer.Amount, offer.AprPercent, offer.Months,
		MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ), 0 );

	/// <summary>One month (0xcc21c): exactly M is paid and one more month counts as repaid.</summary>
	public static (LoanAccount Account, long Paid) Pay( LoanAccount account )
	{
		if ( account.MonthsRemaining <= 0 )
			return (account, 0);
		return (account with { MonthsRepaid = account.MonthsRepaid + 1 }, account.MonthlyRepayment);
	}
}
