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
		("ECON-005", "challenge prizes and scrap sales are other income; build, upgrade, goods, prizes, land are other costs; profit leaves out loans received", "the per-category ledger routines and the annual profit field"),
		("ECON-006", "APR > 0 repayment is an annuity at APR/12 per month, rounded down; interest accrues monthly on the balance", "standard-mode save or capture with an outstanding loan"),
		("ECON-007", "repaid loan offers reopen without the original credit-eligibility gate", "implement the traced credit predicate and qualify its cross-edition behavior"),
		("ECON-008", "100 training points per grade (from Online_Standard.sam comments \"costs 1000 to get up to grade 1\")", "capture of a training run"),
		("ECON-009", "candidate grade = average + 2 when \"great\", else average +-1", "hiring pool captures (grade distribution)"),
		("ECON-010", "TimeBetweenStaffUpdates/StaffTimeoutTime are seconds at normal speed", "capture of pool refresh timing"),
		("ECON-012", "hiring is free; BaseCostPerStaff/CostPerQualityLevel unused", "capture of the balance before/after hiring"),
		("ECON-013", "training budget is spent evenly over a role at month end", "capture of training budget effects"),
		("ECON-014", "staff start at happiness 100 and it never changes (no strikes)", "staff happiness rules (binary/captures)"),
		("ECON-015", "researchers on strike or picked up are the original's excluded states 3, 4 and 5", "the staff state values behind 0x100F4170"),
		("ECON-017", "the research table is in info-id order and the player cannot step the cursor to another item", "the table fill order (FUN_100c9064) and the next/previous control"),
		("ECON-018", "ride upgrade levels and add-on objects form the \"upgrade\" research category", "research lab capture"),
		("ECON-019", "Instant Action research runs at one grade-2 researcher without staff", "Instant Action capture"),
		("ECON-020", "a sale drops LitterEffect/100 litter items", "capture of litter after sales"),
		("ECON-021", "a repair takes WorkDuration game hours (x DurationOfUpgrade for upgrades); mechanics are dispatched instantly", "capture of repair duration per grade"),
		("ECON-022", "a handyman removes one litter item per WorkDuration game minutes, park-wide", "capture of cleaning speed"),
		("ECON-023", "an open ride loses WearRate state of repair per game day; breakdown at 0", "capture of state of repair over time"),
		("ECON-024", "a repair restores state of repair to 100", "capture after a repair"),
		("ECON-025", "scrap value basis = catalogue cost of all levels up to the current one; a scrap year is 365 park-clock days", "capture of scrap value"),
		("ECON-026", "park value = sum of scrap values", "capture of the park value screen"),
		("ECON-027", "the record sub-kinds 0–3 are rides, shops, sideshows and features, and every hired staff member counts", "the record field at +0x4C behind sub-kind +0x7A8 and the staff byte +3 tested by FUN_100C4064"),
		("ECON-028", "purchases need a balance covering the cost", "capture of building with too little money"),
		("ECON-030", "the simulation stops once bankrupt", "capture of the bankrupt state"),
		("ECON-031", "imported parks are opened on load (open state not decoded)", "park-open flag in the save"),
		("ECON-034", "challenge type meanings come from Challenges.sam comments (shop types by ShopType/SpecialIngredient)", "challenge captures per type"),
		("ECON-035", "offers wait for accept/decline; follow-ups are offered right after completion; failed challenges count as finished", "challenge flow captures"),
		("ECON-036", "build challenges with TargetVal 0 need one item; type 28 needs level 3", "challenge captures"),
		("ECON-038", "big park uses MinCellsOwned, cameras use MinCellsCovered", "golden ticket award captures"),
		("ECON-039", "the profit ticket compares the running yearly profit (mProfitThisYear) with ProfitYear directly; the original (0x10013FDC) scales the threshold by a per-objective factor not yet tied to that key", "the caller of 0x10013FDC and its factor"),
		("ECON-040", "players start with 1 golden key and keys are not consumed by entering themes", "initial lobby and repeated theme-entry captures"),
		("ECON-041", "features-directory objects with Research.Category != 3 are fixed (non-buyable) items", "buy-menu capture"),
		("ECON-044", "balloon/costume percentages are 0 (guests carry no items yet)", "guests slice item state"),
		("ECON-045", "loan/challenge record locators use plausibility bounds (one fixture)", "a second TPWS/TPWI fixture"),
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
