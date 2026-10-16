namespace OpenTPW;

/// <summary>
/// Register of the M3 gate's own assumptions (docs/M3-GATE.md). Each entry has a matching
/// <c>// [APPROX:GATE-NNN]</c> comment at its code site in <see cref="M3Gate"/>.
/// </summary>
public static class M3GateApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("GATE-006", "the queue walk terms (C = 27 updates per cell with w2 = 15, the join walk J(p), each head's own remaining walk and the head-not-ready bound N x C + 2 x w2) come from enumerating tp.exe's walk as OpenTPW ports it (whole-walk destination 0x510100, search 0x511420, edge test 0x4D8750; 0.59 <= s <= 2.0, every turn sequence up to 4 crossings, long shapes up to 25), an enumeration and not a proof; avoid_walls' corner branches are ported (WALK-002 is retired), but the neighbour query order and the guest states of the separation (WALK-003) are not matched to the instruction and the enumeration's separation is off or at its maximum, and only the neighbours' cells block a walk (WALK-005); the head-not-ready bound is looser than the retired GATE-005 check, accepted by the owner on 2026-10-10", "the separation's neighbour query order and guest states (WALK-003) and the cell edge flags (WALK-005) of the original traced, then C re-derived from the original's walk"),
	};
}
