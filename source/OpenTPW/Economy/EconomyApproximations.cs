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
		("ECON-001", "one game day = 240 fixed ticks (4 s at normal speed)", "capture of the original clock against wall time"),
		("ECON-002", "every month has 30 days, 12 months per year", "original calendar (binary or captured date display)"),
		("ECON-003", "24 hours per day (Clock.RSE only shows HOUR is used mod 12)", "original HOUR range (binary or Clock.RSE trace)"),
		("ECON-004", "Fast x2 and Fastest x4 speeds (only pause is evidenced)", "original speed controls, if any"),
		("ECON-005", "challenge prizes and scrap sales are other income; build, upgrade, goods, prizes, land are other costs; loans received are not money in", "captured financial screen after these transactions"),
		("ECON-006", "APR > 0 repayment is an annuity at APR/12 per month, rounded down; interest accrues monthly on the balance", "standard-mode save or capture with an outstanding loan"),
		("ECON-008", "100 training points per grade (from Online_Standard.sam comments \"costs 1000 to get up to grade 1\")", "capture of a training run"),
		("ECON-009", "candidate grade = average + 2 when \"great\", else average +-1", "hiring pool captures (grade distribution)"),
		("ECON-010", "TimeBetweenStaffUpdates/StaffTimeoutTime are seconds at normal speed", "capture of pool refresh timing"),
		("ECON-011", "each pool slot above the minimum is filled with 50 % chance per update", "hiring pool captures"),
		("ECON-012", "hiring is free; BaseCostPerStaff/CostPerQualityLevel unused", "capture of the balance before/after hiring"),
		("ECON-013", "training budget is spent evenly over a role at month end", "capture of training budget effects"),
		("ECON-014", "staff start at happiness 100 and it never changes (no strikes)", "staff happiness rules (binary/captures)"),
		("ECON-015", "each researcher adds ResearchAbility points per game day, split by effort", "capture of research progress over time"),
		("ECON-016", "group g opens when PercentageForThisTech % of group g-1 of the same category is researched", "capture of new research groups appearing"),
		("ECON-017", "items are researched cheapest first within open groups", "capture of research order"),
		("ECON-018", "ride upgrade levels and add-on objects form the \"upgrade\" research category", "research lab capture"),
		("ECON-019", "Instant Action research runs at one grade-2 researcher without staff", "Instant Action capture"),
		("ECON-020", "a sale drops LitterEffect/100 litter items", "capture of litter after sales"),
		("ECON-021", "a repair takes WorkDuration game hours (x DurationOfUpgrade for upgrades); mechanics are dispatched instantly", "capture of repair duration per grade"),
		("ECON-022", "a handyman removes one litter item per WorkDuration game minutes, park-wide", "capture of cleaning speed"),
		("ECON-023", "an open ride loses WearRate state of repair per game day; breakdown at 0", "capture of state of repair over time"),
		("ECON-024", "a repair restores state of repair to 100", "capture after a repair"),
		("ECON-025", "scrap value basis = catalogue cost of all levels up to the current one", "capture of scrap value"),
		("ECON-026", "park value = sum of scrap values", "capture of the park value screen"),
		("ECON-027", "park rating = (2 x happiness + attractions/3 + cleanliness) / 4", "park rating formula (binary/captures)"),
		("ECON-028", "purchases need a balance covering the cost", "capture of building with too little money"),
		("ECON-029", "golden tickets are spent when buying items with GoldenTicketCost", "capture of ticket count after such a purchase"),
		("ECON-030", "the simulation stops once bankrupt", "capture of the bankrupt state"),
		("ECON-031", "imported parks are opened on load (open state not decoded)", "park-open flag in the save"),
		("ECON-032", "the prototype ride is registered uncharged", "replace with a real catalogue purchase (rides slice)"),
		("ECON-033", "golden tickets are checked at each month end", "capture of the award timing"),
		("ECON-034", "challenge type meanings come from Challenges.sam comments (shop types by ShopType/SpecialIngredient)", "challenge captures per type"),
		("ECON-035", "offers wait for accept/decline; follow-ups are offered right after completion; failed challenges count as finished", "challenge flow captures"),
		("ECON-036", "build challenges with TargetVal 0 need one item; type 28 needs level 3", "challenge captures"),
		("ECON-037", "staff skill % = grade x 25 + training points / 4", "staff skill display capture"),
		("ECON-038", "big park uses MinCellsOwned, cameras use MinCellsCovered", "golden ticket award captures"),
		("ECON-039", "profit year = profit of the last 12 closed months", "golden ticket award capture"),
		("ECON-040", "players start with 1 golden key and keys are not consumed by entering themes", "initial lobby and repeated theme-entry captures"),
		("ECON-041", "features-directory objects with Research.Category != 3 are fixed (non-buyable) items", "buy-menu capture"),
		("ECON-042", "sideshow InitCostOfGoods is the cost of a prize paid per win", "sideshow panel capture"),
		("ECON-043", "monthly wage = BaseWage[grade] x PayMultiplier[type]", "staff list capture with grades"),
		("ECON-044", "balloon/costume percentages are 0 (guests carry no items yet)", "guests slice item state"),
		("ECON-045", "loan/challenge record locators use plausibility bounds (one fixture)", "a second TPWS/TPWI fixture"),
		("ECON-046", "upgrades need at least one employed mechanic to be bought", "capture (TAG_SYSTEM 151 suggests it)"),
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
