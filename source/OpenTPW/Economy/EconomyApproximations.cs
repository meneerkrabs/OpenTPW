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
		("ECON-001", "OpenTPW's fixed 60 Hz clock is sampled into 248 ms turns (14.88 ticks per turn), without the original's catch-up cap and scheduler phases", "runtime turn timing under load and speed changes"),
		("ECON-002", "the Mac OS date conversion (LongSecondsToDate, reached through 0x101C5C4C) uses the default Gregorian calendar", "the script system of an original run"),
		("ECON-004", "Fast x2 and Fastest x4 speeds (only pause is evidenced)", "original speed controls, if any"),
		("ECON-005", "challenge prizes and scrap sales are other income; build, upgrade, goods, prizes, land are other costs; profit leaves out loans received", "the per-category ledger routines of other income and other costs (a loan received is money in and not profit: executed, ORACLE F14)"),
		("ECON-007", "repaid loan offers reopen without the original credit-eligibility gate", "implement the traced credit predicate and qualify its cross-edition behavior"),
		("ECON-008", "100 training points per grade (from Online_Standard.sam comments \"costs 1000 to get up to grade 1\")", "capture of a training run"),
		("ECON-010", "TimeBetweenStaffUpdates/StaffTimeoutTime are seconds at normal speed", "capture of pool refresh timing"),
		("ECON-012", "hiring is free; BaseCostPerStaff/CostPerQualityLevel unused", "capture of the balance before/after hiring"),
		("ECON-014", "staff start at happiness 100 and it never changes (no strikes)", "staff happiness rules (binary/captures)"),
		("ECON-015", "researchers on strike or picked up are the original's excluded states 3, 4 and 5", "the staff state values behind 0x100F4170"),
		("ECON-017", "the research table is in info-id order and the player cannot step the cursor to another item", "the table fill order (FUN_100c9064) and the next/previous control"),
		("ECON-018", "ride upgrade levels and add-on objects form the \"upgrade\" research category", "research lab capture"),
		("ECON-020", "a sale adds LitterEffect/100 litter items to a park-wide store; the original adds LitterEffect to the buying guest's 0..100 litter meter, which drops one litter event at 90 (ORACLE F18)", "a guest litter meter with the drop-at-90 and bin rules, and the park's litter count"),
		("ECON-021", "the mechanic starts at once (the original first walks to the ride, 0xe6454) and the timer counts one park turn per staff update (dispatcher cadence read statically)", "capture of a repair from arrival to completion"),
		("ECON-022", "a handyman removes one litter item per WorkDuration game minutes, park-wide", "capture of cleaning speed"),
		("ECON-023", "the ride update's state gates (ride states 1, 3 and 4 at +0x198, +0x64 == 0, +0x2e bit 0) map only to \"a mechanic is at work\"", "the names of those ride states and flags"),
		("ECON-025", "the scrap rule is the Mac build's (0xE25E8 / 0xE2424: base cost x the age percentage, 100 for an unused object in its first 30 days); the Windows build's counterpart is not executed, and the object use counter (thing +412) is ParkObjectState.TimesUsed", "the tp.exe counterpart of 0xE25E8 run in the x86 oracle, or a capture of a sale"),
		("ECON-026", "park value = sum of catalogue costs up to the current level times the age percentage (the scrap rule before ORACLE F21), not the sale refund", "capture of the park value screen"),
		("ECON-027", "the record sub-kinds 0–3 are rides, shops, sideshows and features, and every hired staff member counts", "the record field at +0x4C behind sub-kind +0x7A8 and the staff byte +3 tested by FUN_100C4064"),
		("ECON-028", "purchases need a balance covering the cost", "capture of building with too little money"),
		("ECON-030", "the simulation stops once bankrupt", "capture of the bankrupt state"),
		("ECON-031", "a park started without an original save is opened when guests attach (an imported park follows its saved mParkClosed)", "the open state of a new original park"),
		("ECON-034", "challenge type meanings come from Challenges.sam comments (shop types by ShopType/SpecialIngredient)", "challenge captures per type"),
		("ECON-035", "offers wait for accept/decline; follow-ups are offered right after completion; failed challenges count as finished", "challenge flow captures"),
		("ECON-036", "type 18 needs TargetVal items built since acceptance; the binary compares TargetVal with a per-ride value from 0x100C7264 that is not identified", "the fifth output of 0x10041A34"),
		("ECON-038", "big park uses MinCellsOwned, cameras use MinCellsCovered", "golden ticket award captures"),
		("ECON-039", "the profit ticket compares the running yearly profit (mProfitThisYear) with ProfitYear directly; the original (0x10013FDC) scales the threshold by a per-objective factor not yet tied to that key", "the caller of 0x10013FDC and its factor"),
		("ECON-040", "players start with 1 golden key and keys are not consumed by entering themes", "initial lobby and repeated theme-entry captures"),
		("ECON-041", "features-directory objects with Research.Category != 3 are fixed (non-buyable) items", "buy-menu capture"),
		("ECON-044", "guests never hold a balloon or wear a costume, so both percentages are 0; the binary gives them when a guest uses a balloon or costume shop (0x100EAAF8)", "balloon lifetime (+0x214) and costume state rules"),
		("ECON-048", "a negative path or queue cell cost (modified Costs.*) is booked as other income; balance and profit match the original's Spend of a negative amount", "the ledger category of a negative Spend"),
		("ECON-049", "the door's reopen guard (FUN_004df290) checks only VAR_BROKEN; the upgrade and condemned states and the back-of-queue path test (FUN_004de4a0) are not modelled", "the ride states that block reopening and the queue back-link test"),
		("ECON-050", "the gate census reads the imported guest grid, which preserves paths but not every original outside, approach or footprint cell type; unpreserved cells remain type 0 and can be counted as park ground", "original cell-type provenance in the guest-grid import"),
		("ECON-051", "the bank's withdrawals flag (+276) has no counterpart: loan instalments are always deducted from the balance (the original, with the flag off, skips the deduction and keeps the profit bookkeeping)", "the writer of the flag and when the original clears it"),
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
