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
		("SAVE-003", "saved staff are imported with their state enum read in STAFFSTATES.str order, their inline name replaced by name index 0 and no job, patrol region, rest area or position", "the staff state values and an inline-name field in OpenTPW's staff record"),
		("SAVE-004", "the macro-AI hiring pool is not decoded; the new park draws its own pool, with its timers moved to the imported turn", "the candidate-pool serializer 0xf5f1c widths"),
		("SAVE-005", "the challenge system's next-offer time and current challenge are not imported; the first offer day is counted from the imported day", "the unit of the challenge system's next event time"),
		("SAVE-006", "an attraction's saved state enum, chance of winning, third gauge, histories and totals are not imported (open/broken state comes from its script)", "the attraction state values and the chance-of-winning/losing relation"),
		("SAVE-007", "research projects and groups are not imported; research starts from the level settings", "the research project and group field widths"),
		("SAVE-008", "bank words without an OpenTPW counterpart (batch balance, last balance, withdrawals flag, turn entered red) are not imported; a bought loan's outstanding balance is its monthly repayment times the months left", "a save with a bought loan or a negative balance"),
		("SAVE-009", "the staff HQ's five training budgets are imported only when all are zero (their role order is not traced); strike levels are not imported", "the role order of the budget loop 0xfc108"),
		("SAVE-010", "the opaque blocks (scripts, particles, messages, clock words, cadence, track/flying rides, RSSE, camera, coasters, advisor presentation, sound, cheats, advisor scoring, UI) and the weather, park analyser and tag system things are not imported, and of the cell map only path cells, occupancy and MAP attributes are read; object scripts restart from their archives", "decoded block layouts and the original's post-load reconstruction"),
	};
}
