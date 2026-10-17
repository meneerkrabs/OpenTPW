namespace OpenTPW;

/// <summary>
/// The score callbacks of the restored advice extension (docs/reverse/ADVISOR-SCAN.md, "Restored advice"). The 16 scan-zero rows of <see cref="Callbacks"/> replace the constant-zero stub
/// <c>0x005D60C0</c> in the background scan (<see cref="AdvisorScan"/> asks <see cref="CallbackFor"/> for a row whose callback is the stub, and gets one only while the extension is on); the four
/// state-driven rows of <see cref="PollRules"/> are rows the scan skips and nothing posts, which the extension's poll evaluates once per scan call.
/// </summary>
/// <remarks>
/// Every callback reads the park through <see cref="AdvisorScanContext.Restored"/> (a read-only view) and the <c>Advisor.sam</c> keys through <see cref="AdvisorBalance.Restored"/>. A key the settings file
/// lacks, a missing park view or a fact that does not hold scores 0, which never passes the scan's eligibility check or <c>MinScoreForConsideration</c>.
/// </remarks>
// [EXT:restored-advice] OpenTPW's own callbacks; the original's rows score 0 (0x005D60C0) or are never scored
internal static class RestoredAdviceScan
{
	/// <summary>The constant-zero stub the restored scan rows have as their callback.</summary>
	// [BIN:TPW-EXE:0x005D60C0 constant zero stub] the callbacks of 21 scanned rows (and of 41 more rows the scan skips) return 0
	public const uint ConstantZeroCallback = 0x5D60C0;

	/// <summary>The callback the extension gives a scan row, by message id; null while the extension is off or for a message it does not restore.</summary>
	public static AdvisorScoreCallback? CallbackFor( int messageId ) =>
		RestoredAdvice.Enabled && Callbacks.TryGetValue( messageId, out var callback ) ? callback : null;

	private static int Score( AdvisorScanContext context, int message ) => RestoredAdvice.ScoreOf( context.Balance, message ) ?? 0;

	private static AdvisorScoreCallback When( int message, Func<IRestoredAdviceState, RestoredAdviceSettings, bool> holds ) =>
		context => context.Restored is { } state && holds( state, context.Balance.Restored ) ? Score( context, message ) : 0;

	/// <summary>The scan-zero rows' callbacks by message id.</summary>
	public static readonly IReadOnlyDictionary<int, AdvisorScoreCallback> Callbacks = new Dictionary<int, AdvisorScoreCallback>
	{
		// [EXT:restored-advice-security] messages 24 and 25 (the guard and camera cover): the shipped file has StaffGuardCoverage next to its siblings StaffMechanicCoverage and StaffHandymanCoverage, whose rows (19, 21) score the uncovered percent x ScorePerNonCoveredPercent, at least Score, after the role's months and staff count; the guard row's callback is the stub
		[24] = context => SecurityScore( context, none: false ),
		[25] = context => SecurityScore( context, none: true ),
		// [EXT:restored-advice-queues] 58 and 59 (the bored-in-queues lines, whose captions TAG_SYSTEM 63 and 64 exist): the original's connected-queue test is not their input; the long queue is the extension's own measure
		[58] = When( 58, ( state, _ ) => state.Rides.Any( ride => ride.Open && ride.QueueLength >= RestoredAdvice.LongQueueGuests ) ),
		[59] = When( 59, ( state, _ ) => LongQueues( state ) ),
		// [EXT:restored-advice-upgrades] 65: RidesNoUpgrades (months, score) is the callback the row would have had; its trigger is a researched upgrade that was never bought
		[65] = When( 65, ( state, settings ) => state.UpgradesPossible && settings.Int( "RidesNoUpgrades.MonthsIntoGameForConsideration" ) is { } months && state.ParkMonths >= months
			&& state.UpgradesAvailable > 0 && state.UpgradesBought == 0 ),
		// [EXT:restored-advice-fame] 151 to 154: the record-sized attractions; the thresholds are the golden tickets' (GoldenTicketGlobal), the share of them that is "big" the extension's own
		[151] = When( 151, ( state, _ ) => Approaching( state, GoldenTicketKind.CoasterHeight, ParkRecordKind.CoasterHeight ) && !state.HoldsTicket( GoldenTicketKind.CoasterHeight ) ),
		[152] = When( 152, ( state, _ ) => state.HoldsTicket( GoldenTicketKind.CoasterHeight ) ),
		[153] = When( 153, ( state, _ ) => Approaching( state, GoldenTicketKind.GokartExcitement, ParkRecordKind.GokartExcitement ) ),
		[154] = When( 154, ( state, _ ) => Approaching( state, GoldenTicketKind.WaterLength, ParkRecordKind.WaterLength ) ),
		// [EXT:restored-advice-shops] 160: souvenirs before leaving
		[160] = When( 160, ( state, _ ) => state.VisitorsTwoMonths > 0 && state.RetailSalesTwoMonths >= RestoredAdvice.SouvenirMinimumSales && state.RetailSalesTwoMonths * 2 >= state.VisitorsTwoMonths ),
		// [EXT:restored-advice-land] 173: little room left, use Buy Land
		[173] = When( 173, ( state, _ ) => state.Land.Owned > 0 && state.Land.Free * 100L < state.Land.Owned * (long)RestoredAdvice.LandFreePercent ),
		// [EXT:restored-advice-exits] 265: RideExitBlocked.Score is the key of the row's would-be callback (its shipped comment is about people stranded)
		[265] = When( 265, ( state, _ ) => state.Rides.Any( ride => ride.ExitBlocked ) ),
		// [EXT:restored-advice-money] 347 and 348: the balance is still in the black (the in-the-red rows 102 to 105 and 106 cover the rest) but cannot carry the next wage bill
		[347] = context => LowBalance( context, 347, withLoan: false ),
		[348] = context => LowBalance( context, 348, withLoan: true ),
		// [EXT:restored-advice-stranded] 349 and 350: riders aboard a broken-down or a condemned ride (the new riders-stuck tracking)
		[349] = When( 349, ( state, _ ) => state.Rides.Any( ride => ride.BrokenDown && !ride.Condemned && ride.RidersAboard > 0 ) ),
		[350] = When( 350, ( state, _ ) => state.Rides.Any( ride => ride.Condemned && ride.RidersAboard > 0 ) ),
	};

	/// <summary>The rows the scan skips whose conditions are the park's state, by message id: evaluated like scan callbacks, once per scan call.</summary>
	public static readonly IReadOnlyDictionary<int, AdvisorScoreCallback> PollRules = new Dictionary<int, AdvisorScoreCallback>
	{
		// [EXT:restored-advice-toilets] 83 (no toilets) and 84 (far from toilets): FeaturesNoToilets and FeaturesFarToToilets are the keys of their getters; the original's 82 has the first's thresholds, so 83 asks for twice the people
		[83] = When( 83, ( state, settings ) => state.Toilets == 0 && settings.Int( "FeaturesNoToilets.NeedingMoreThan" ) is { } needing && settings.Int( "FeaturesNoToilets.MorePeopleThanThisNeeding" ) is { } people
			&& state.GuestsNeedingToilet( needing ) > 2 * people ),
		[84] = When( 84, ( state, settings ) => state.Toilets > 0 && settings.Int( "FeaturesNoToilets.NeedingMoreThan" ) is { } needing && settings.Int( "FeaturesNoToilets.MorePeopleThanThisNeeding" ) is { } people
			&& state.GuestsFarFromToilets( needing, RestoredAdvice.FarToiletCells ) > people ),
		// [EXT:restored-advice-tickets] 277 and 318: a profit near the year's ticket; 318's callback (0x005A0670) reads GoldTicketNearToXProfit (multiplier, score), 277's is the stub
		[277] = When( 277, ( state, settings ) => NearProfitTicket( state, settings ) is { } near && near.Profit * 100 >= near.Threshold * RestoredAdvice.ProfitHintPercent && near.Profit < near.NearProfit ),
		[318] = When( 318, ( state, settings ) => NearProfitTicket( state, settings ) is { } near && near.Profit >= near.NearProfit ),
	};

	private static bool LongQueues( IRestoredAdviceState state )
	{
		var open = state.Rides.Count( ride => ride.Open );
		var need = Math.Max( 2, Math.Min( RestoredAdvice.WorseQueueRides, (open + 1) / 2 ) );
		return state.Rides.Count( ride => ride.Open && ride.QueueLength >= RestoredAdvice.LongQueueGuests ) >= need;
	}

	private static bool Approaching( IRestoredAdviceState state, GoldenTicketKind ticket, ParkRecordKind record )
	{
		var threshold = state.TicketThreshold( ticket );
		return threshold > 0 && state.Record( record ) * 100 >= threshold * RestoredAdvice.BigRecordPercent;
	}

	private static (long Profit, long Threshold, long NearProfit)? NearProfitTicket( IRestoredAdviceState state, RestoredAdviceSettings settings )
	{
		var threshold = state.TicketThreshold( GoldenTicketKind.ProfitYear );
		if ( threshold <= 0 || state.HoldsTicket( GoldenTicketKind.ProfitYear ) || settings.Float( "GoldTicketNearToXProfit.ProfitMultiplier" ) is not { } multiplier )
			return null;
		// the multiplier is a single, and the product is rounded to one as the x87 at 24 bits rounds it
		var nearProfit = (long)(float)(threshold * (float)multiplier);
		return (state.ProfitThisYear, threshold, nearProfit);
	}

	private static int LowBalance( AdvisorScanContext context, int message, bool withLoan )
	{
		if ( context.Restored is not { } state || context.Money is not { } money || state.MonthlyWages <= 0 || money.Balance < 0 )
			return 0;
		var holds = withLoan ? money.Balance < state.MonthlyWages && money.LoanAvailable : money.Balance < 2 * state.MonthlyWages;
		return holds ? Score( context, message ) : 0;
	}

	private static int SecurityScore( AdvisorScanContext context, bool none )
	{
		var settings = context.Balance.Restored;
		if ( context.Restored is not { } state || settings.Int( "StaffSetGuardArea.MonthsIntoGameForConsideration" ) is not { } months || state.ParkMonths < months
			|| settings.Int( "StaffGuardCoverage.ScorePerNonCoveredPercent" ) is not { } perPercent || settings.Int( "StaffGuardCoverage.Score" ) is not { } floor )
			return 0;
		var coverage = state.SecurityCoverage;
		if ( coverage.PathCells == 0 )
			return 0;
		var secured = state.HiredGuards + coverage.Cameras;
		if ( none != (secured == 0) )
			return 0;
		var uncovered = none ? 100 : coverage.UncoveredPercent;
		return uncovered <= 0 ? 0 : Math.Max( floor, uncovered * perPercent );
	}
}
