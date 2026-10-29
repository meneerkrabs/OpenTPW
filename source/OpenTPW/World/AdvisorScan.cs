namespace OpenTPW;

/// <summary>
/// One of the bank's eight loan records (tp.exe <c>bank + 0x20 + 0x20 × i</c>): bought flag (+0x8), term in months (+0x0), months repaid (+0xC) and instalment (+0x4).
/// What the record still owes is <c>(months − repaid) × instalment</c>, a signed 32-bit product (<c>0x004D0970</c>).
/// </summary>
internal readonly record struct AdvisorLoanSlot( bool Bought, int Months, int MonthsRepaid, int MonthlyRepayment )
{
	/// <summary>The bank keeps eight loan records.</summary>
	public const int Count = 8;

	/// <summary>What the record owes (0 for a record that is not bought).</summary>
	// [BIN:TPW-EXE:0x004D0970 loan repayment] a record whose bought flag (+0x28) is clear gives 0, else (term − months repaid) × instalment (imul, 32 bits)
	public int Owed => Bought ? unchecked( (Months - MonthsRepaid) * MonthlyRepayment ) : 0;
}

/// <summary>
/// The money statistics the advisor's score callbacks read from the park (the bank and the monthly sample rings), as the original's helpers
/// <c>0x6AD810</c>, <c>0x4D0690</c>, <c>0x4D0810</c>, <c>0x4D0A00</c>, <c>0x4D09A0</c>, <c>0x4D0970</c>, <c>0x4D0260</c> and <c>0x4F8800</c> return them.
/// </summary>
internal interface IAdvisorMoneyState
{
	/// <summary>The bank's balance (<c>bank + 0xC</c>, a signed 32-bit word).</summary>
	int Balance { get; }
	/// <summary>The bank's eight loan records.</summary>
	IReadOnlyList<AdvisorLoanSlot> LoanSlots { get; }
	/// <summary>Whether a loan can still be taken (<c>0x004D0810</c>).</summary>
	bool LoanAvailable { get; }
	/// <summary>Whole 30-day months the park has been in the red (<c>0x004D0260</c> divided by 2.592e13; 0 in the black).</summary>
	int MonthsInRed { get; }
	/// <summary>Whole 30-day months of the park's clock (<c>0x004F8800</c> divided by 2.592e13).</summary>
	int ParkMonths { get; }
	/// <summary>The staff-wages sample ring (<c>park + 0x1F7F0</c>), the newest closed month first (at most 144).</summary>
	IReadOnlyList<int> WageMonths { get; }
	/// <summary>The money-in sample ring (<c>park + 0x1FC90</c>), the newest closed month first (at most 144).</summary>
	IReadOnlyList<int> IncomeMonths { get; }
}

/// <summary>What a score callback sees: the <c>Advisor.sam</c> object it is called on and the park's money statistics.</summary>
internal sealed class AdvisorScanContext
{
	public AdvisorScanContext( AdvisorBalance balance, IAdvisorMoneyState? money, IAdvisorParkState? park = null )
	{
		Balance = balance ?? throw new ArgumentNullException( nameof( balance ) );
		Money = money;
		Park = park;
	}

	public AdvisorBalance Balance { get; }
	/// <summary>The park's money statistics; null without a park (every money callback then scores 0).</summary>
	public IAdvisorMoneyState? Money { get; }
	/// <summary>[EXT:restored-advice] The read-only park view the extension's callbacks read; null while the extension is off or without a park (they then score 0).</summary>
	public IRestoredAdviceState? Restored { get; init; }
	/// <summary>The park's rides, cleanliness and guest statistics; null without a park (every park callback then scores 0).</summary>
	public IAdvisorParkState? Park { get; }

	/// <summary>A flat configured score (the object's word at the key's offset); 0 when the settings file lacks the key.</summary>
	public int Score( string key ) => Balance.Scores.TryGetValue( key, out var value ) ? value : 0;

	/// <summary>A configured threshold (<see cref="AdvisorScan.ParameterKeys"/>); null when the settings file lacks the key.</summary>
	public double? Parameter( string key ) => Balance.ScanParameters.TryGetValue( key, out var value ) ? value : null;
}

/// <summary>A score callback of the descriptor table: the object it reads is the context, the result is the score (0 or less: no advice).</summary>
internal delegate int AdvisorScoreCallback( AdvisorScanContext context );

/// <summary>What one scan call picked: the winning message (<see cref="AdvisorScan.NoMessage"/> when none), its score and the row it scored.</summary>
internal readonly record struct AdvisorScanResult( int MessageId, int Score, int ScoredRow );

/// <summary>
/// The advisor's background scan (tp.exe <c>0x0059C680</c>), called once per advisor update by <c>0x0059A550</c> with a count of 1: it advances a cursor over the
/// 351 descriptors of the table <c>0x0076E300</c>, skips the rows game code posts (first word nonzero) without counting them, applies the row's game-mode selector,
/// calls its score callback and keeps the message when the score is positive and the eligibility check passes. A callback OpenTPW has not ported scores 0, as the
/// original's constant-zero stub <c>0x005D60C0</c> does, so it can never pass.
/// </summary>
internal sealed class AdvisorScan
{
	/// <summary>The number of descriptors; the cursor wraps to 0 when it reaches this.</summary>
	// [BIN:TPW-EXE:0x0059C6A8 cursor wrap] CMP [cursor], 0x15F; JL: a cursor at 351 or above restarts at 0 before the row is read
	public const int DescriptorCount = 351;
	/// <summary>The "no message" result the scan returns (0x160), which has no descriptor.</summary>
	public const int NoMessage = 0x160;

	/// <summary>The <c>Advisor.sam</c> thresholds the ported callbacks read (beyond their flat scores): integers and single-precision floats.</summary>
	public static readonly IReadOnlyList<string> IntegerParameterKeys = new[] { "WealthHigh.MoreMoneyThan", "WagesHigh.AverageOverMonths" }.Concat( AdvisorParkCallbacks.IntegerKeys ).Concat( AdvisorStaffCallbacks.IntegerKeys ).Concat( AdvisorQueuesCallbacks.IntegerKeys ).Concat( AdvisorChallengeCallbacks.IntegerKeys ).Concat( AdvisorInputsCallbacks.IntegerKeys ).ToArray();
	public static readonly IReadOnlyList<string> FloatParameterKeys = new[] { "WealthHighRepayLoan.Margin", "WagesHigh.WagesTimesHigherThanIncome" }.Concat( AdvisorParkCallbacks.FloatKeys ).Concat( AdvisorChallengeCallbacks.FloatKeys ).ToArray();
	public static IEnumerable<string> ParameterKeys => IntegerParameterKeys.Concat( FloatParameterKeys );

	private readonly IReadOnlyList<AdvisorDescriptorRow> rows;
	private readonly IReadOnlyDictionary<uint, AdvisorScoreCallback> callbacks;

	// [EXT:restored-advice] asked for the callback of a row whose own is the constant-zero stub; null (the default) leaves such rows scoring 0, as the original's do
	private readonly Func<int, AdvisorScoreCallback?>? restored;

	public AdvisorScan( IReadOnlyList<AdvisorDescriptorRow> rows, IReadOnlyDictionary<uint, AdvisorScoreCallback> callbacks, Func<int, AdvisorScoreCallback?>? restored = null )
	{
		this.rows = rows ?? throw new ArgumentNullException( nameof( rows ) );
		this.callbacks = callbacks ?? throw new ArgumentNullException( nameof( callbacks ) );
		this.restored = restored;
	}

	/// <summary>The scan's cursor (the first word of the <c>Advisor.sam</c> object): the next row to look at, 0 to 351.</summary>
	public int Cursor { get; private set; }

	/// <summary>Whether the table has all 351 rows (it is empty when the shipped file is missing, which leaves the scan idle).</summary>
	public bool IsComplete => rows.Count == DescriptorCount;

	public void RestoreCursor( int cursor )
	{
		if ( cursor is < 0 or > DescriptorCount )
			throw new InvalidDataException( $"The advisor scan cursor {cursor} is outside 0 to {DescriptorCount}." );
		Cursor = cursor;
	}

	/// <summary>The score of a row as the scan reads it: -1 when the game-mode selector excludes it, else its callback's score (0 when not ported).</summary>
	// [BIN:TPW-EXE:0x0059C730 mode selector] word +0xC: 2 always calls the callback, 0 only while the game-mode global 0xFB3B7C is 0, 1 only while it is 2; anything else scores -1
	public int ScoreOf( AdvisorDescriptorRow row, AdvisorScanContext context, int gameType )
	{
		if ( !AdvisorTables.GameModeAccepts( row.GameMode, gameType ) )
			return -1;
		// [BIN:TPW-EXE:0x005D60C0 constant zero stub] the callbacks of 21 scanned rows (and of every one OpenTPW has not ported) return 0
		if ( callbacks.TryGetValue( row.Callback, out var callback ) )
			return callback( context );
		// [EXT:restored-advice] a scan-zero row may get the extension's callback while it is on
		return row.Callback == RestoredAdviceScan.ConstantZeroCallback && restored?.Invoke( row.MessageId ) is { } restoredCallback ? restoredCallback( context ) : 0;
	}

	/// <summary>
	/// One scan call with count 1: looks at rows from the cursor until one that the scan scores (first word zero) has been scored, leaving the cursor after it.
	/// <paramref name="eligible"/> is the eligibility check <c>0x0059ABC0</c> for a message and its score.
	/// </summary>
	// [BIN:TPW-EXE:0x0059C6A8 scan loop] the counter at [esp+0x10] is incremented only at 0x59C822, reached for a scored row; a row with a nonzero first word jumps to 0x59C826 (cursor + 1) without counting, so the call goes on to the next row
	// [BIN:TPW-EXE:0x0059C7DD best score] a score strictly above the best so far (0 at the start) that passes 0x59ABC0 becomes the result
	public AdvisorScanResult Step( AdvisorScanContext context, int gameType, Func<int, int, bool> eligible )
	{
		ArgumentNullException.ThrowIfNull( context );
		ArgumentNullException.ThrowIfNull( eligible );
		if ( !IsComplete )
			return new AdvisorScanResult( NoMessage, 0, -1 );
		var best = 0;
		var result = NoMessage;
		var scoredRow = -1;
		for ( var scored = 0; scored < 1; )
		{
			if ( Cursor >= DescriptorCount )
				Cursor = 0;
			var row = rows[Cursor];
			if ( row.Scan )
			{
				var score = ScoreOf( row, context, gameType );
				if ( score > best && eligible( row.MessageId, score ) )
				{
					best = score;
					result = row.MessageId;
				}
				scoredRow = Cursor;
				scored++;
			}
			Cursor++;
		}
		return new AdvisorScanResult( result, best, scoredRow );
	}
}

/// <summary>The score callbacks OpenTPW has ported, by the address of the original's callback.</summary>
internal static class AdvisorScanCallbacks
{
	public const uint WealthHigh = 0x59E8D0;
	public const uint WealthHighRepayLoan = 0x59E910;
	public const uint TakeLoan = 0x59E9B0;
	public const uint InTheRedSixMonths = 0x59E9F0;
	public const uint InTheRedThreeMonths = 0x59EA70;
	public const uint InTheRedMonthLeft = 0x59EAF0;
	public const uint WagesHigh = 0x59EB80;

	/// <summary>The callbacks by address (the descriptor's word +0x10); an address that is not here scores 0.</summary>
	public static readonly IReadOnlyDictionary<uint, AdvisorScoreCallback> Implemented = new Dictionary<uint, AdvisorScoreCallback>
	{
		[WealthHigh] = WealthHighScore,
		[WealthHighRepayLoan] = WealthHighRepayLoanScore,
		[TakeLoan] = TakeLoanScore,
		[InTheRedSixMonths] = context => InTheRed( context, "InTheRedSixMonths.Score", months => months < 3 ),
		[InTheRedThreeMonths] = context => InTheRed( context, "InTheRedThreeMonths.Score", months => months is >= 3 and < 5 ),
		[InTheRedMonthLeft] = context => InTheRed( context, "InTheRedMonthLeft.Score", months => months == 5 ),
		[WagesHigh] = WagesHighScore,
	}.Concat( AdvisorParkCallbacks.Callbacks ).Concat( AdvisorStaffCallbacks.Callbacks ).Concat( AdvisorQueuesCallbacks.Callbacks ).Concat( AdvisorChallengeCallbacks.Callbacks ).Concat( AdvisorInputsCallbacks.Callbacks ).ToDictionary( entry => entry.Key, entry => entry.Value );

	/// <summary>Loans taken: the bought records among the eight (<c>0x004D0690</c>).</summary>
	public static int LoansTaken( IAdvisorMoneyState money ) => money.LoanSlots.Count( slot => slot.Bought );

	/// <summary>Everything the bought records owe, in a 32-bit word (<c>0x004D0A00</c>).</summary>
	public static int TotalDebt( IAdvisorMoneyState money )
	{
		var total = 0;
		foreach ( var slot in money.LoanSlots )
			total = unchecked( total + slot.Owed );
		return total;
	}

	/// <summary>The bought record that owes least, as an index, or -1 (<c>0x004D09A0</c>): owed amounts compare as unsigned words, zero is skipped, the first of equals wins.</summary>
	// [BIN:TPW-EXE:0x004D09A0 cheapest loan] over the eight records: amount = (term − repaid) × instalment for a bought one, 0 otherwise; JBE skips 0, JB takes a strictly smaller unsigned amount or the first found
	public static int CheapestLoan( IAdvisorMoneyState money )
	{
		var index = -1;
		var smallest = 0u;
		for ( var slot = 0; slot < money.LoanSlots.Count; slot++ )
		{
			var owed = unchecked( (uint)money.LoanSlots[slot].Owed );
			if ( owed == 0 )
				continue;
			if ( index < 0 || owed < smallest )
			{
				index = slot;
				smallest = owed;
			}
		}
		return index;
	}

	// [BIN:TPW-EXE:0x0059E8D0 WealthHigh] balance (0x6AD810) greater than the word at +0x330 (signed) and no loan taken (0x4D0690 == 0): the word at +0x334
	private static int WealthHighScore( AdvisorScanContext context )
	{
		if ( context.Money is not { } money || context.Parameter( "WealthHigh.MoreMoneyThan" ) is not { } threshold )
			return 0;
		if ( money.Balance <= (int)threshold || LoansTaken( money ) != 0 )
			return 0;
		return context.Score( "WealthHigh.Score" );
	}

	// [BIN:TPW-EXE:0x0059E910 WealthHighRepayLoan] diff = balance − total debt (0x4D0A00), a signed word; the cheapest loan (0x4D09A0) must exist (≥ 0); its repayment (0x4D0970, read as an unsigned word) × the single at +0x338 must be below diff converted to a single: the word at +0x33C
	private static int WealthHighRepayLoanScore( AdvisorScanContext context )
	{
		if ( context.Money is not { } money || context.Parameter( "WealthHighRepayLoan.Margin" ) is not { } margin )
			return 0;
		var difference = unchecked( money.Balance - TotalDebt( money ) );
		var cheapest = CheapestLoan( money );
		if ( cheapest < 0 )
			return 0;
		var repayment = unchecked( (uint)money.LoanSlots[cheapest].Owed );
		// the x87 runs at 24 bits (DirectDraw's FPU setup, ADVISOR-026): fild dword / fstp dword rounds the difference to a single and the fmul rounds the product to one
		return (float)(repayment * margin) < (float)difference ? context.Score( "WealthHighRepayLoan.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059E9B0 TakeLoan] balance < 0 (signed) and a loan can still be taken (0x4D0810): the word at +0x340
	private static int TakeLoanScore( AdvisorScanContext context ) =>
		context.Money is { } money && money.Balance < 0 && money.LoanAvailable ? context.Score( "TakeLoan.Score" ) : 0;

	// [BIN:TPW-EXE:0x0059E9F0 InTheRed] 103, 104 and 105 share the shape: balance < 0 and no loan available, then the months in the red (0x4D0260 divided by 0x1792F8648000 with __alldiv): below 3 gives +0x344, 3 or 4 gives +0x348 (CMP 3 JL, CMP 5 JGE), exactly 5 gives +0x34C
	private static int InTheRed( AdvisorScanContext context, string key, Func<int, bool> months ) =>
		context.Money is { } money && money.Balance < 0 && !money.LoanAvailable && months( money.MonthsInRed ) ? context.Score( key ) : 0;

	// [BIN:TPW-EXE:0x0059EB80 WagesHigh] the park clock must have run 3 months (0x4F8800 / 0x1792F8648000 ≥ 3); n = the word at +0x358 months, cut to what the ring holds; the average (truncated) of the n newest months of the wages ring (+0x1F7F0) must exceed the same average of the money-in ring (+0x1FC90) × the single at +0x354: the word at +0x35C
	private static int WagesHighScore( AdvisorScanContext context )
	{
		if ( context.Money is not { } money || context.Parameter( "WagesHigh.AverageOverMonths" ) is not { } months || context.Parameter( "WagesHigh.WagesTimesHigherThanIncome" ) is not { } factor )
			return 0;
		if ( money.ParkMonths < 3 )
			return 0;
		var (wages, count) = RingAverage( money.WageMonths, (int)months );
		if ( count == 0 )
			return 0;
		var (income, incomeCount) = RingAverage( money.IncomeMonths, (int)months );
		// 0x59ED8D: an empty income ring scores 0 as well
		if ( incomeCount == 0 )
			return 0;
		return (float)(income * factor) < (double)wages ? context.Score( "WagesHigh.Score" ) : 0;
	}

	/// <summary>
	/// The average of the <paramref name="requested"/> newest months of a sample ring, cut to the months it holds; the sum is added in the x87 stack and the quotient truncated
	/// (<c>__ftol</c>), every step rounded to a single as the x87 does at 24 bits. A request of 0 or less averages nothing (0, and the count says so).
	/// </summary>
	/// <returns>The average and the number of months the callback tests for being zero.</returns>
	public static (int Average, int Count) RingAverage( IReadOnlyList<int> newestFirst, int requested )
	{
		var count = requested > newestFirst.Count ? newestFirst.Count : requested;
		if ( count <= 0 )
			return (0, count);
		var sum = 0f;
		for ( var back = 0; back < count; back++ )
			sum = (float)(sum + newestFirst[back]);
		return ((int)(float)(sum / count), count);
	}
}

/// <summary>The park's money statistics for the advisor's callbacks, read from the economy without changing it.</summary>
internal sealed class EconomyMoneyState : IAdvisorMoneyState
{
	private readonly ParkEconomy economy;

	public EconomyMoneyState( ParkEconomy economy ) => this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );

	public int Balance => unchecked( (int)economy.Balance );
	public IReadOnlyList<AdvisorLoanSlot> LoanSlots => economy.LoanSlots();
	public bool LoanAvailable => economy.CanTakeAnyLoan();
	public int MonthsInRed => economy.MonthsSinceEnteringRed();
	// [BIN:TPW-EXE:0x004F8800 park time] the park clock in 100 ns units is the turn count × the calendar rate (15000 / 4 seconds per turn); divided by 30 days with __alldiv
	public int ParkMonths => (int)Math.Min( int.MaxValue, ParkCalendar.Seconds( economy.Tick ) / ParkEconomy.SecondsPerMonthInRed );
	// [BIN:TPW-EXE:0x004C7740 month close] the park's month close pushes the four accumulators (money out +0x1F5A0, wages +0x1F7F0, +0x1FA40, money in +0x1FC90) into 144-entry rings and clears them: the ledger's closed months, kept to the same 144
	public IReadOnlyList<int> WageMonths => economy.Ledger.History.Reverse().Select( month => unchecked( (int)month[LedgerCategory.StaffCosts] ) ).ToArray();
	public IReadOnlyList<int> IncomeMonths => economy.Ledger.History.Reverse().Select( month => unchecked( (int)month.MoneyIn ) ).ToArray();
}
