namespace OpenTPW;

/// <summary>
/// Register of the M3 gate's own assumptions (docs/M3-GATE.md). Each entry has a matching
/// <c>// [APPROX:GATE-NNN]</c> comment at its code site in <see cref="M3Gate"/>.
/// </summary>
public static class M3GateApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("GATE-006", "the queue walk terms (C = 27 updates per cell with w2 = 15, the join walk J(p), each head's own remaining walk and the head-not-ready bound N x C + 2 x w2) come from enumerating tp.exe's walk as OpenTPW ports it (whole-walk destination 0x510100, search 0x511420, edge test 0x4D8750; 0.59 <= s <= 2.0, every turn sequence up to 4 crossings, long shapes up to 25), an enumeration and not a proof; avoid_walls' corner branches are ported (WALK-002 is retired), and the separation's neighbours come in the original's chain order, guests and staff alike, the riding and held guests at their last walked point (WALK-003 is retired) but the enumeration's separation is off or at its maximum, and only the neighbours' cells block a walk (WALK-005); the head-not-ready bound is looser than the retired GATE-005 check, accepted by the owner on 2026-10-10", "the track records of the edge test (WALK-005) of the original traced, then C re-derived from the original's walk with its neighbours (at most five of any kind in the cells around, 0x5102F0)"),
	};
}
