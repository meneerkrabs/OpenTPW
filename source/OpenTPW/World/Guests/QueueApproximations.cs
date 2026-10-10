namespace OpenTPW;

/// <summary>
/// The queue slice's approximation register (docs/GUESTS.md, "Queues"; docs/reverse/QUEUE-plan.md §10). Each
/// entry is tagged <c>[APPROX:QUEUE-NNN]</c> at its code site. Everything else in the queue model is traced
/// from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class QueueApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("QUEUE-001", "queue link values 1/4/16/64 map to −Y/+X/+Y/−X like the saved connection bits"),
		("QUEUE-002", "the next queue cell is searched in grid direction order"),
		("QUEUE-003", "guests step onto the back of a queue from its first walkable neighbour (the join cell)"),
		("QUEUE-004", "the join excitement gate applies to rides and sideshows with |preferred − excitement| ≥ 45"),
		("QUEUE-005", "queue edits re-evaluate guests: beyond 4 × cells or off the queue leave, others walk to their position"),
		("QUEUE-006", "queue positions: depth from the cell's front edge, lateral across it; the fourth position stays in its cell"),
		("QUEUE-007", "the entry stand point is the front cell's edge toward the ride"),
		("QUEUE-008", "the ride-failure exit is VAR_BROKEN ≠ 0"),
		("QUEUE-009", "the queue facing change's sign comes from the 1-in-10 draw"),
		("QUEUE-010", "guests leaving a queue reappear on its join cell at once"),
		("QUEUE-011", "objects use upgrade level 0 for the queue limit inputs"),
		("QUEUE-012", "queue cells need allowed terrain and no object footprint"),
		("QUEUE-013", "a queue has at most 25 cells"),
		("QUEUE-014", "queues are laid cell by cell from the entrance's outside cell, each touching the back cell"),
		("QUEUE-015", "removing a queue cell removes every cell behind it"),
		("QUEUE-016", "rides evaluate admission and queued guests run state 11 once per park turn"),
		("QUEUE-017", "guests do not choose a broken attraction (VAR_BROKEN ≠ 0)"),
		("QUEUE-018", "a broken attraction refuses joins as if closed"),
		("QUEUE-019", "a broken attraction skips admission (VAR_BROKEN ≠ 0 read as ride +408 ≠ 0)")
	};
}
