namespace OpenTPW;

/// <summary>
/// Money categories of the original financial screens: "Money in" = gate/shop/sideshow takings
/// (UITEXT 164–167), "Money out" = staff costs (169), other costs (358) and loan payments (359).
/// <see cref="OtherIncome"/> (challenge prizes, scrap sales) and <see cref="LoanReceived"/> have no
/// original label; loans received change the balance but are not counted as money in.
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
	public long MoneyIn => Sum( LedgerCategory.GateTakings, LedgerCategory.ShopTakings, LedgerCategory.SideshowTakings, LedgerCategory.OtherIncome );
	public long MoneyOut => Sum( LedgerCategory.StaffCosts, LedgerCategory.OtherCosts, LedgerCategory.LoanPayments );
	public long Profit => MoneyIn - MoneyOut;
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

	// [APPROX:ECON-005] challenge prizes and scrap sales are other income; build, upgrade, goods, prizes, land are other costs; loans received are not money in — evidence needed: captured financial screen after these transactions
	public static bool IsIncome( LedgerCategory category ) => category is LedgerCategory.GateTakings or LedgerCategory.ShopTakings or LedgerCategory.SideshowTakings or LedgerCategory.OtherIncome or LedgerCategory.LoanReceived;

	public void Post( LedgerCategory category, long amount )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( amount );
		if ( amount == 0 )
			return;
		Balance = checked(IsIncome( category ) ? Balance + amount : Balance - amount);
		current[category] = checked(current.GetValueOrDefault( category ) + amount);
	}

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

/// <summary>An outstanding loan.</summary>
public sealed record LoanAccount( int OfferIndex, int LenderNameIndex, long OriginalAmount, int AprPercent, int Months, long MonthlyRepayment, int MonthsRemaining, long RemainingBalance );

/// <summary>
/// Loan arithmetic. With 0 % APR the monthly repayment is <c>floor(amount / months)</c>: the
/// Easymode save stores exactly these values for all eight easy-mode offers (docs/ECONOMY.md).
/// For a positive APR the original formula is unknown; OpenTPW uses a standard annuity (monthly
/// rate APR/12) rounded down — an <b>approximation</b>. The final payment settles the remainder.
/// </summary>
public static class LoanMath
{
	public static long MonthlyRepayment( long amount, int aprPercent, int months )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( months );
		ArgumentOutOfRangeException.ThrowIfNegative( amount );
		if ( aprPercent <= 0 )
			// [DATA:levels/jungle/Easymode.TPWI:loan table repayments = floor(amount/months) at 0 % APR]
			return amount / months;
		// [APPROX:ECON-006] APR > 0 repayment is an annuity at APR/12 per month, rounded down; interest accrues monthly on the balance — evidence needed: standard-mode save or capture with an outstanding loan
		var rate = aprPercent / 1200.0;
		return (long)Math.Floor( amount * rate / (1 - Math.Pow( 1 + rate, -months )) );
	}

	public static long MonthlyInterest( long balance, int aprPercent ) => aprPercent <= 0 ? 0 : (long)Math.Round( balance * (aprPercent / 1200.0), MidpointRounding.AwayFromZero );

	public static long TotalPayable( long amount, int aprPercent, int months )
	{
		var account = Open( new LoanOffer( 0, amount, aprPercent, months, 0 ) );
		long total = 0;
		while ( account.MonthsRemaining > 0 )
		{
			var (next, paid) = Pay( account );
			total += paid;
			account = next;
		}
		return total;
	}

	public static LoanAccount Open( LoanOffer offer ) => new( offer.Index, offer.LenderNameIndex, offer.Amount, offer.AprPercent, offer.Months,
		MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ), offer.Months, offer.Amount );

	/// <summary>One month: interest accrues on the remaining balance, then the repayment (or the remainder in the last month) is paid.</summary>
	public static (LoanAccount Account, long Paid) Pay( LoanAccount account )
	{
		if ( account.MonthsRemaining <= 0 )
			return (account, 0);
		var owed = account.RemainingBalance + MonthlyInterest( account.RemainingBalance, account.AprPercent );
		var paid = account.MonthsRemaining == 1 ? owed : Math.Min( owed, account.MonthlyRepayment );
		return (account with { MonthsRemaining = account.MonthsRemaining - 1, RemainingBalance = owed - paid }, paid);
	}
}
