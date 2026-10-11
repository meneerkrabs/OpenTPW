namespace OpenTPW;

/// <summary>
/// Register of the original-save import mappings that are not traced (docs/reverse/SAVE-plan.md §8.2,
/// docs/SAVE-CONTAINER.md, "Import into OpenTPW"). Each entry has a matching <c>// [APPROX:SAVE-NNN]</c>
/// comment at its code site. Values the import drops are also reported in <see cref="OriginalEconomyImport.Evidence"/>.
/// </summary>
public static class SaveApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("SAVE-001", "the save's world generator state (mRandomSeed) is reported but not imported: OpenTPW's economy, guest and script streams keep their world-seed derivation", "DET-I2 port of the world LCG (DET-005 limits the restore to this one field)"),
		("SAVE-002", "the saved guests (model 1) are not imported; OpenTPW's guest simulation starts with its own arrivals", "decoded person/guest fields and an OpenTPW guest save target"),
		("SAVE-003", "saved staff are imported with their state enum read in STAFFSTATES.str order, their inline name replaced by name index 0, their hire time stamp rounded down to its park turn and no job, patrol region, rest area, energy, idle tick, role suffix or position", "the staff state values and an inline-name field in OpenTPW's staff record"),
		("SAVE-004", "the macro-AI hiring pool is not decoded; the new park draws its own pool, with its timers moved to the imported turn", "the candidate-pool serializer 0xf5f1c widths"),
		("SAVE-005", "the challenge system's next-offer time, current challenge, three switch bytes, Value and ActualThing are not imported; the first offer day is counted from the imported day", "the unit of the challenge system's next event time and the meaning of its switch bytes, Value and ActualThing"),
		("SAVE-006", "an attraction's saved state enum, chance of winning, third gauge, histories, totals, name lines, operating capacity, duration and speed, quality of goods, queue size, customer and walk-away counts, time stamp and mesh instance ID are not imported (open/broken state comes from its script, the queue from the laid queue cells)", "the attraction state values, the chance-of-winning/losing relation and OpenTPW counterparts of the operating and count fields"),
		("SAVE-007", "research projects and groups are not imported; research starts from the level settings", "the research project and group field widths"),
		("SAVE-008", "bank words without an OpenTPW counterpart (batch balance, last balance, withdrawals flag, turn entered red) and the loan offers' Available flags are not imported (OpenTPW offers every offer not taken); the ledger month opens at the current balance because mLastBalance, the candidate opening-balance word, is the balance after the last debit (withdrawal 0x100CBFDC), not a month opening; a bought loan's outstanding balance is its monthly repayment times the months left", "a save with a bought loan or a negative balance, the original's month-opening balance and the writer of the offers' Available flags"),
		("SAVE-009", "the staff HQ's five training budgets, force-strike word, picked-up word, five strike levels and trained byte are not imported (the budgets' role order is not traced); OpenTPW's budgets start at 0", "the role order of the budget loop 0xfc108 and the strike-level fields"),
		("SAVE-010", "the opaque blocks (scripts, particles, messages, clock words, cadence, track/flying rides, RSSE, camera, coasters, advisor presentation, sound, cheats, advisor scoring, UI), the weather, park analyser, tag system, mechanic HQ and UI message receiver things, the world state, arrival variables and arrival bytes, the calendar session start and the advisor controller's time signature, sound, three raw words, current record, mRequestedLastTutorial and mMessagePlaying are not imported, nor are its pending advice and history when no automatic advisor is attached; of the cell map only path cells, occupancy, MAP attributes and each path cell's texture and turn are read, and a saved path tile is drawn only while the cell's path neighbours equal the saved ones and its turn is 0/90/180/270 (otherwise the tile is chosen from the neighbours); object scripts restart from their archives", "decoded block layouts and the original's post-load reconstruction"),
		("SAVE-011", "the delimiters of blocks whose width is not traced are located by their bytes: a payload that splits into its blocks in more than one way, or holds a delimiter's bytes more than MaximumDelimiterOccurrences times, is rejected, while the count-based original loader reads each block by its own width", "the serialized widths of the remaining blocks"),
	};
}
