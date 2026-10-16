namespace OpenTPW;

/// <summary>
/// Register of the M3 gate's own assumptions (docs/M3-GATE.md). Each entry has a matching
/// <c>// [APPROX:GATE-NNN]</c> comment at its code site in <see cref="M3Gate"/>.
/// </summary>
public static class M3GateApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("GATE-006", "the queue walk terms (C = 27 updates per cell with w2 = 15, the join walk J(p), each head's own remaining walk and the head-not-ready bound N x C + 2 x w2) come from enumerating tp.exe's walk as OpenTPW ports it (whole-walk destination 0x510100, search 0x511420, edge test 0x4D8750; 0.59 <= s <= 2.0, every turn sequence up to 4 crossings, long shapes up to 25), an enumeration and not a proof; the corner branches of avoid_walls (WALK-002) and the neighbour query order of the separation (WALK-003) are not matched to the instruction, and only the neighbours' cells block a walk; the head-not-ready bound is looser than the retired GATE-005 check, accepted by the owner on 2026-10-10", "the original's avoid_walls corner branches and the separation's neighbour query order traced, then C re-derived from the original's walk"),	};
}
