namespace OpenTPW;

/// <summary>How often a history ring of a Theme Park Inc attraction takes a new entry: the game calendar sends a new-day message (11) when the day of the month changes and a new-month message (12) when the month changes.</summary>
public enum ThemeParkIncHistoryPeriod
{
	/// <summary>Message 11, sent by the calendar step 0x6083d0 when the day differs from <c>mDayAtLastUpdate</c>; the attraction's handler is 0x5df19f. 30 entries.</summary>
	Day,
	/// <summary>Message 12, sent by the same step when the month differs from <c>mMonthAtLastUpdate</c>; the attraction's handler is 0x5defff. 144 entries.</summary>
	Month,
}

/// <summary>
/// The 19 history rings of a Theme Park Inc attraction in the order the serializer 0x5ea020 saves them (13 daily rings of 30 entries, then 6 monthly rings of 144). The loader's field log
/// names a ring's members (<c>mCurrentEntry</c>, <c>mNumEntries</c>, <c>mWrappedAround</c>, <c>mTemp</c>, <c>mData[i]</c>) but never the ring itself; the names here describe what
/// the game adds to each ring's running value <c>mTemp</c> (the code that does it is in <see cref="ThemeParkIncAttractionRingInfo.Evidence"/>). The Theme Park World attraction stores the
/// customer count, the takings and the costs as plain words; these rings replace them, so the profit shown for a month is the profit ring and no word of the body holds it.
/// </summary>
public enum ThemeParkIncAttractionRing
{
	/// <summary>Takings added by each payment.</summary>
	DailyTakings = 0,
	/// <summary>Costs charged to the attraction (the cost of the goods of a sale, the cost of a ride's use).</summary>
	DailyCosts = 1,
	/// <summary>Guests who used the attraction (finished a ride, bought a ware).</summary>
	DailyCustomers = 2,
	/// <summary>Guests who gave up the queue (the attraction's walk-away counter <c>mNumWalkAways</c> is incremented with it).</summary>
	DailyWalkAways = 3,
	/// <summary>Visits whose outcome the guest applied to its mood without having lost a sideshow.</summary>
	DailyRatedVisits = 4,
	/// <summary>Visits whose score was above the top mood threshold (the guest thinks thought 5).</summary>
	DailyVeryHappyGuests = 5,
	/// <summary>Visits whose score was above the second mood threshold but not above the top (thought 5).</summary>
	DailyHappyGuests = 6,
	/// <summary>Visits whose score fell below the fourth mood threshold but not below the lowest (thought 6).</summary>
	DailyUnhappyGuests = 7,
	/// <summary>Visits whose score fell below the lowest mood threshold (thought 6).</summary>
	DailyVeryUnhappyGuests = 8,
	/// <summary>Guests who turned the attraction down: the guest's own refusal, a refused price at a category-2 attraction, or the failed chance of a special effect.</summary>
	DailyDeclinedGuests = 9,
	/// <summary>Guests for whom the check of the attraction's hunger or thirst effect failed: the effect (info +0x204 HungerEffect or +0x200 ThirstEffect) plus a random 0 to 7 plus the ingredient byte (+0x98) was below 30, which costs the guest score; a pass raises the guest's satisfied meters by 35.</summary>
	DailyNeedChecksFailed = 10,
	/// <summary>Guests who found the price cheap (below <c>PcOfGoodPriceIsCheap</c> percent of the fair value) and could pay it.</summary>
	DailyCheapPriceGuests = 11,
	/// <summary>Guests who refused the price (above the fair value or more than their money).</summary>
	DailyExpensivePriceGuests = 12,
	/// <summary>The monthly twin of <see cref="DailyTakings"/> (it takes the same additions).</summary>
	MonthlyTakings = 13,
	/// <summary>The monthly twin of <see cref="DailyCosts"/>.</summary>
	MonthlyCosts = 14,
	/// <summary>Total costs of the month: <see cref="MonthlyCosts"/> plus <see cref="MonthlyRunningCost"/>, computed at the month change.</summary>
	MonthlyTotalCosts = 15,
	/// <summary>The running cost (<c>m_runningCost</c>) of the month, or 0 while the attraction's state is 1 or 4.</summary>
	MonthlyRunningCost = 16,
	/// <summary>The profit of the month: <see cref="MonthlyTakings"/> minus <see cref="MonthlyTotalCosts"/>, computed at the month change (a signed ring).</summary>
	MonthlyProfit = 17,
	/// <summary>The monthly twin of <see cref="DailyCustomers"/>.</summary>
	MonthlyCustomers = 18,
}

/// <summary>One history ring: which it is, its period, its capacity, where it sits in the in-memory history object (allocated with 0x14c8 bytes when the attraction has money flows) and the code that feeds it.</summary>
public sealed record ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing Ring, ThemeParkIncHistoryPeriod Period, int Capacity, int MemoryOffset, string Evidence );

public sealed partial record ThemeParkIncAttraction
{
	/// <summary>The history rings in serializer order; <see cref="Histories"/>[<c>(int)ring</c>] is the ring of the same index.</summary>
	// [BIN:TPI-EXE:0x5ea020 history serializer] the rings in memory at +0, +0x88, +0x110 ... +0x660 (13 of 30 entries, 0x88 bytes each) and +0x6e8, +0x938, +0xb88, +0xdd8, +0x1028, +0x1278 (6 of 144, 0x250 each); the constructor 0x5e9dc0 (called by the attraction constructor, 0x5db5d8) sets mCurrentEntry to -1 and mNumEntries to the capacity, so a ring always saves its full capacity
	// [BIN:TPI-EXE:0x5df19f daily roll] on message 11 each daily ring takes its running value as the next entry (current entry + 1 modulo the capacity, wrapped on the first return to 0) and the value restarts at 0
	// [BIN:TPI-EXE:0x5defff monthly roll] on message 12 the running cost word, the total costs (cost plus running cost) and the profit (takings minus total costs) are computed, each monthly ring takes its value and restarts at 0; ring 17 goes through 0x5ba2b0, the signed variant
	public static IReadOnlyList<ThemeParkIncAttractionRingInfo> HistoryRings { get; } = new[]
	{
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyTakings, ThemeParkIncHistoryPeriod.Day, 30, 0x000, "0x5e5de0 adds a payment to +0 and +0x6e8 (the guest's payment 0x6125b0)" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyCosts, ThemeParkIncHistoryPeriod.Day, 30, 0x088, "0x5e60e0 adds a cost to +0x88 and +0x938 (cost of goods 0x5e6220 for a shop, 0x5e63e0 for a ride; the guest step 0x612740)" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyCustomers, ThemeParkIncHistoryPeriod.Day, 30, 0x110, "0x5e5da0 increments +0x110 and +0x1278 (called by the guest's end of visit 0x6118d0)" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyWalkAways, ThemeParkIncHistoryPeriod.Day, 30, 0x198, "0x5e5d70 increments mNumWalkAways (+0xa4) and +0x198" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyRatedVisits, ThemeParkIncHistoryPeriod.Day, 30, 0x220, "0x5e61d0 increments +0x220, called at the end of 0x6118d0 after the happiness change" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyVeryHappyGuests, ThemeParkIncHistoryPeriod.Day, 30, 0x2a8, "0x61358e in the guest's outcome step 0x612740: score above the top threshold 0xa01b1c" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyHappyGuests, ThemeParkIncHistoryPeriod.Day, 30, 0x330, "0x61368a: score at or below 0xa01b1c, not below 0xa01b28 and above 0xa01b20" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyUnhappyGuests, ThemeParkIncHistoryPeriod.Day, 30, 0x3b8, "0x6136bb: score not below 0xa01b28 and below 0xa01b24" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyVeryUnhappyGuests, ThemeParkIncHistoryPeriod.Day, 30, 0x440, "0x613651: score below 0xa01b28" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyDeclinedGuests, ThemeParkIncHistoryPeriod.Day, 30, 0x4c8, "0x611eeb (the guest's choice), 0x612488 (price refused, info category 2), 0x612fb9 and 0x613164 (failed chance of a special effect)" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyNeedChecksFailed, ThemeParkIncHistoryPeriod.Day, 30, 0x550, "0x612b5b and 0x612c3d in 0x612740: HungerEffect (info +0x204) or ThirstEffect (+0x200) + random(0..7) + the ingredient byte (+0x98) below 30" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyCheapPriceGuests, ThemeParkIncHistoryPeriod.Day, 30, 0x5d8, "0x61245d in the guest's price test 0x6120c0: price below the 0xa01b18 percent of the fair value, the guest can pay" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.DailyExpensivePriceGuests, ThemeParkIncHistoryPeriod.Day, 30, 0x660, "0x612473 in 0x6120c0: price above the fair value or above the guest's money (+0x1c0); the test then returns 1" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.MonthlyTakings, ThemeParkIncHistoryPeriod.Month, 144, 0x6e8, "0x5e5de0 (with the daily ring)" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.MonthlyCosts, ThemeParkIncHistoryPeriod.Month, 144, 0x938, "0x5e60e0 (with the daily ring)" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.MonthlyTotalCosts, ThemeParkIncHistoryPeriod.Month, 144, 0xb88, "0x5df08a: +0xdd8 plus +0x938" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.MonthlyRunningCost, ThemeParkIncHistoryPeriod.Month, 144, 0xdd8, "0x5df070: m_runningCost (+0x84), 0 in state 1 or 4" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.MonthlyProfit, ThemeParkIncHistoryPeriod.Month, 144, 0x1028, "0x5df0a4: +0x6e8 minus +0xb88" ),
		new ThemeParkIncAttractionRingInfo( ThemeParkIncAttractionRing.MonthlyCustomers, ThemeParkIncHistoryPeriod.Month, 144, 0x1278, "0x5e5da0 (with the daily ring)" ),
	};

	/// <summary>The ring <paramref name="ring"/>; null when the attraction has no history object.</summary>
	public SaveAttractionHistory? Ring( ThemeParkIncAttractionRing ring ) => Histories.Count == HistoryRings.Count ? Histories[(int)ring] : null;

	/// <summary>
	/// The entries of a ring from the newest to the oldest, as the game reads them back (0x5eb090: the entry <c>n</c> periods ago is at <c>(current - n)</c> modulo the capacity, and only
	/// entries that were written exist: <c>current + 1</c> of them until the ring wraps, all of them after). Empty while nothing was written.
	/// </summary>
	public static IReadOnlyList<int> NewestFirst( SaveAttractionHistory ring )
	{
		var capacity = ring.Entries.Count;
		var count = ring.WrappedAround ? capacity : ring.CurrentEntry + 1;
		if ( count <= 0 || capacity == 0 )
			return Array.Empty<int>();
		var result = new int[count];
		for ( var age = 0; age < count; age++ )
		{
			var index = ring.CurrentEntry - age;
			result[age] = ring.Entries[index >= 0 ? index : index + capacity];
		}
		return result;
	}
}
