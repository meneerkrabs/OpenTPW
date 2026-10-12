namespace OpenTPW;

/// <summary>
/// Register of every economy value or rule not taken from original data. Each entry has a matching
/// <c>// [APPROX:ECON-NNN]</c> comment at its code site and a row in docs/ECONOMY.md
/// ("Approximation register"). <see cref="LogOnce"/> warns about all of them at startup.
/// </summary>
public static class EconomyApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("ECON-001", "OpenTPW's fixed 60 Hz clock is sampled into 248 ms park turns (4.03 a second at normal speed, the rate the original's scheduler executes at its default clock scale 1.0 over a millisecond clock), without the original's 31 ms substeps, its limit of three park turns per call and its drop of a backlog above 2000 ms (ORACLE F30)", "the clock route a real run selects (the library's millisecond clock is a microsecond API on every route read, none executed) and the original's behaviour under load"),
		("ECON-002", "the Mac OS date conversion (LongSecondsToDate, reached through 0x101C5C4C) uses the default Gregorian calendar", "the script system of an original run"),
		("ECON-004", "Fast x2 and Fastest x4 are OpenTPW's buttons: the original steps its clock scale by 1.25 within 0.25..2 (x4 exceeds its maximum) with key actions whose physical keys are unnamed", "the key tokens' names and whether the shipped game shows a speed control"),
		("ECON-005", "challenge prizes and scrap sales are other income; build, upgrade, goods, prizes, land are other costs; profit leaves out loans received", "the per-category ledger routines of other income and other costs (a loan received is money in and not profit: executed, ORACLE F14)"),
		("ECON-007", "repaid loan offers reopen without the original credit-eligibility gate", "implement the traced credit predicate and qualify its cross-edition behavior"),
		("ECON-008", "100 training points per grade (from Online_Standard.sam comments \"costs 1000 to get up to grade 1\")", "capture of a training run"),
		("ECON-010", "TimeBetweenStaffUpdates/StaffTimeoutTime are seconds at normal speed", "capture of pool refresh timing"),
		("ECON-014", "staff vitals and strikes follow the traced routines but not the staff state machine: walking members drain every turn, working means a job in progress, resting needs no walk, idle members are not told apart and the rest search runs every turn; the handymen's analyser statistics, the forced-strike word, the game-type gate and the clock epoch of the 24-unit age test (the park clock counted from its start here) are not modelled", "the staff state machine, the analyser statistics 0x100C21F4/0x100C2264 and the time stamp epoch behind 0x100E4628"),
		("ECON-015", "researchers on strike or picked up are the original's excluded states 3, 4 and 5", "the staff state values behind 0x100F4170"),
		("ECON-017", "the research table is in info-id order and the player cannot step the cursor to another item", "the table fill order (FUN_100c9064) and the next/previous control"),
		("ECON-018", "ride upgrade levels and add-on objects form the \"upgrade\" research category", "research lab capture"),
		("ECON-020", "a sale adds LitterEffect/100 litter items to a park-wide store; the original adds LitterEffect to the buying guest's 0..100 litter meter, which drops one litter event at 90 (ORACLE F18)", "a guest litter meter with the drop-at-90 and bin rules, and the park's litter count"),
		("ECON-021", "the mechanic starts at once (the original first walks to the ride, 0xe6454) and the timer counts one park turn per staff update (dispatcher cadence read statically)", "capture of a repair from arrival to completion"),
		("ECON-022", "a handyman removes one litter item per WorkDuration game minutes, park-wide", "capture of cleaning speed"),
		("ECON-023", "the wear step and the breakdown check do not know relief objects (toilets are not simulated, +0x2E bit 0), and the ride's state is derived from the economy (breakdown flag, pending upgrade) and the script's VAR_BROKEN instead of a state word", "the callers that set state 2 for an upgrade and what feeds toilets' wear"),
		("ECON-025", "the scrap rule is the Mac build's (0xE25E8 / 0xE2424: base cost x the age percentage, 100 for an unused object in its first 30 days); the Windows build's counterpart is not executed, and the object use counter (thing +412) is ParkObjectState.TimesUsed", "the tp.exe counterpart of 0xE25E8 run in the x86 oracle, or a capture of a sale"),
		("ECON-026", "the map's cell points are not added: the cell map has no ownership flag (PATH-007) and its types are incomplete (ECON-050), so the park value is the object part only, and the scrap values are no longer part of it", "the ownership of the starting park's cells and the original cell types"),
		("ECON-027", "the visitor term counts the guests in the park by state, where the original counts the guests standing on a cell of type 0, 1, 3, 9 or 10 (the census 0xc3684); every hired staff member counts, where the original skips a staff thing whose byte +3 is set", "cell-type provenance for the census (ECON-050) and the writers of the staff byte +3"),
		("ECON-028", "purchases need a balance covering the cost", "capture of building with too little money"),
		("ECON-030", "the simulation stops once bankrupt", "capture of the bankrupt state"),
		("ECON-031", "a park started without an original save is opened when guests attach (an imported park follows its saved mParkClosed)", "the open state of a new original park"),
		("ECON-034", "challenge type meanings come from Challenges.sam comments (shop types by ShopType/SpecialIngredient)", "challenge captures per type"),
		("ECON-035", "offers wait for accept/decline; follow-ups are offered right after completion; failed challenges count as finished", "challenge flow captures"),
		("ECON-036", "type 18 needs TargetVal items built since acceptance; the binary compares TargetVal with a per-ride value from 0x100C7264 that is not identified", "the fifth output of 0x10041A34"),
		("ECON-038", "big park uses MinCellsOwned, cameras use MinCellsCovered", "golden ticket award captures"),
		("ECON-040", "players start with 1 golden key and keys are not consumed by entering themes", "initial lobby and repeated theme-entry captures"),
		("ECON-041", "features-directory objects with Research.Category != 3 are fixed (non-buyable) items", "buy-menu capture"),
		("ECON-044", "guests never hold a balloon or wear a costume, so both percentages are 0; the binary gives them when a guest uses a balloon or costume shop (0x100EAAF8)", "balloon lifetime (+0x214) and costume state rules"),
		("ECON-048", "a negative path or queue cell cost (modified Costs.*) is booked as other income; balance and profit match the original's Spend of a negative amount", "the ledger category of a negative Spend"),
		("ECON-049", "the maintenance flag (the original's player request, +96) and the back-of-queue link test (0xdd744, PC FUN_004de4a0) are not modelled: OpenTPW has no maintenance button and takes the queue as connected", "the callers of 0xdd744 and the cell test it makes"),
		("ECON-050", "the gate census reads the imported guest grid, which preserves paths but not every original outside, approach or footprint cell type; unpreserved cells remain type 0 and can be counted as park ground", "original cell-type provenance in the guest-grid import"),
		("ECON-051", "the bank's withdrawals flag (+276) has no counterpart: loan instalments are always deducted from the balance (the original, with the flag off, skips the deduction and keeps the profit bookkeeping)", "the writer of the flag and when the original clears it"),
		("ECON-052", "the recent-visitors ticket sums the last RecentVisitorMonths closed calendar months and needs that many closed months; the original compares the visitor counter minus its history value RecentVisitorMonths back and needs the park age in 30-day months to exceed RecentVisitorMonths", "the history value behind 0x100C3B88 and the age in calendar months"),
	};

	private static bool logged;

	public static void LogOnce()
	{
		if ( logged )
			return;
		logged = true;
		foreach ( var (id, assumption, evidence) in All )
			Log.Warning( $"[APPROX:{id}] {assumption} — evidence needed: {evidence}" );
	}
}
