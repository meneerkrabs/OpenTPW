namespace OpenTPW;

/// <summary>
/// Register of the M3 gate's own assumptions (docs/M3-GATE.md). Each entry has a matching
/// <c>// [APPROX:GATE-NNN]</c> comment at its code site in <see cref="M3Gate"/>.
/// </summary>
public static class M3GateApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("GATE-001", "an M3 in-game minute is 60 s of normal-speed simulation (3,600 fixed 60 Hz ticks), not a park-clock minute", "the original's notion of elapsed play time for the M3 gate"),
		("GATE-004", "the head-not-ready bound walks its N + 4 cells at OpenTPW's walk speed (WalkSpeedCellsPerSecond x 0.7, itself without an original source); the original has no 0.7 factor and walks as slowly as 0.12 cell per turn", "a traced per-cell walk term for state 11/12 queue walking (WALK-plan section 11.7, WALK-I)"),
		("GATE-005", "the boarding bound W(p) and the head check W(0) = H + R + 1 assume a new head already stands at its slot (BOARD-plan H); the walk from the join cell into an empty or short queue (up to 24 cells) is not a term, so these checks are stricter than the derivation for small p", "a traced bound on the join-cell-to-slot walk (WALK-plan section 11.7) added to W(p) for heads that joined an empty queue"),
	};
}
