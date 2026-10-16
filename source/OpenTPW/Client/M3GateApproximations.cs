namespace OpenTPW;

/// <summary>
/// Register of the M3 gate's own assumptions (docs/M3-GATE.md). Each entry has a matching
/// <c>// [APPROX:GATE-NNN]</c> comment at its code site in <see cref="M3Gate"/>.
/// </summary>
public static class M3GateApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("GATE-006", "the queue walk terms (C = 9 updates per cell with w2 = 15, the join walk J(p), each head's own remaining walk and the head-not-ready bound N x C + 2 x w2) come from enumerating OpenTPW's own port of the traced steering, including WALK-002 (corner branches), WALK-003 (separation neighbours), WALK-004 (routes) and the walkability rule (WALK-005/006), enumerated while queue walks were route walks (they now run the original search and edge test, and C has not been enumerated again); the head-not-ready bound is looser than the retired GATE-005 check, accepted by the owner on 2026-10-10", "the original's avoid_walls corner branches, cell edge flags and route search traced, then C re-derived from the original's walk"),
	};
}
