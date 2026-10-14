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
		("ECON-014", "staff vitals and strikes follow the traced routines but not the staff state machine: walking members drain every turn, working means a job in progress, resting needs no walk, idle members are not told apart and the rest search runs every turn; the handymen's litter share counts the live map (ECON-020), the forced-strike word, the game-type gate and the clock epoch of the 24-unit age test (the park clock counted from its start here) are not modelled", "the staff state machine, the analyser statistics 0x100C21F4/0x100C2264 and the time stamp epoch behind 0x100E4628"),
		("ECON-017", "the research table is in info-id order and the player cannot step the cursor to another item", "the table fill order (FUN_100c9064) and the next/previous control"),
		("ECON-018", "ride upgrade levels and add-on objects form the \"upgrade\" research category", "research lab capture"),
		("ECON-020", "litter lies on map cells (one item per cell, dropped by guests' full litter meters, vomit and bins they cannot reach) as in the original, but the drop's sprite reseeds the original's world generator with the cell id (0x004D94E2), which OpenTPW's separate streams do not follow (DET-014), and the handymen's strike share counts the live map where the analyser caches 4 × 4 blocks for a few turns", "none (read): the reseed is DET-014's stream decision, the cache lag 0x004C8BA0 / 0x004C8AE0"),
		("ECON-021", "the mechanic starts at once (the original first walks to the ride, 0xe6454) and the timer counts one park turn per staff update (dispatcher cadence read statically)", "capture of a repair from arrival to completion"),
		("ECON-022", "handymen in a level walk and search as the original (WANDER-plan §4, StaffWorldSimulation), but their rest and strike are the economy's without the walks to the rest or strike area, a toilet claim ends when the walk to it fails instead of lapsing after 100 turns, the player's maintenance request (mRequestedService) and patrol rectangle have no control, a handyman steps without the guests' separation, and a toilet's access cell is its join or entrance cell; a headless economy without a world keeps its own dispatch: the first unclaimed littered cell in the finder's order, park-wide, else the dirtiest toilet, timed from the dispatch", "none (read, tp.exe 0x004D73C0 / 0x004D7100); the rest and strike walks and the controls are their own items"),
		("ECON-023", "the wear step and the breakdown check do not know relief objects (toilets are not simulated, +0x2E bit 0), and the ride's state is derived from the economy (breakdown flag, pending upgrade) and the script's VAR_BROKEN instead of a state word; a breakdown that only the script reports (VAR_BROKEN) gets no mechanic, because the economy does not write VAR_BREAKSTAT and the job could not wait for the script as 0xd9e04 does", "the callers that set state 2 for an upgrade, what feeds toilets' wear and the script side of VAR_BREAKSTAT"),
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
		("ECON-049", "the maintenance flag (the ride's mRequestedService, +0x64 on the PC) and the back-of-queue link test (tp.exe 0x4DE4A0: connection bits beyond the link) are not modelled: OpenTPW has no maintenance button and takes the queue as connected", "the writers of mRequestedService and the queue tool's connection writes"),
		("ECON-050", "the gate census reads the imported guest grid, which preserves paths but not every original outside, approach or footprint cell type; unpreserved cells remain type 0 and can be counted as park ground", "original cell-type provenance in the guest-grid import"),
		("ECON-051", "the bank's withdrawals flag (+276) has no counterpart: loan instalments are always deducted from the balance (the original, with the flag off, skips the deduction and keeps the profit bookkeeping)", "the writer of the flag and when the original clears it"),
		("ECON-052", "the original's staff states 13 and 18 (the guard escort states CanBePickedUp 0x505bd0 refuses) and the state 6 clip have no OpenTPW state: the pick-up refusal and the guard carry clip read StaffMember.Escorting, which no simulation sets yet", "the guard escort states (0x508A30) and what state 6 is"),
		("ECON-053", "a staff member carried at save time is saved dropped on its old cell and the tool ends (the original saves it in state 7 with the pool's mStaffMemberPickedUp, and nothing re-creates the carry tool after a load, so it probably stays stuck)", "a load of an original save made while carrying"),
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
