namespace OpenTPW;

/// <summary>
/// The guest walk's approximation register (docs/GUESTS.md, "Walking"; docs/reverse/WALK-plan.md §11, §13). Each entry is
/// tagged <c>[APPROX:WALK-NNN]</c> at its code site. The speed model is traced from the Mac binary; a guest's walk on the park's
/// cells is tp.exe's (GuestNavigation.Navigate: the route search 0x511420, the edge test 0x4d8750 and commit 0x510510, avoid_walls
/// 0x50e4a0 with its corner blocks, follow_path 0x50ed10 with the block stamps, the progress 0x50fd40 and the steering step 0x50f3b0
/// with its own arithmetic), checked against the original in the x86 oracle (docs/reverse/ORACLE.md X25); separation is the Mac's (BIN labels at the code sites).
/// Retired: WALK-003 (separation's neighbours: one thing chain per cell for guests and staff interleaved by entry time, every state, tp.exe 0x5102F0, 0x4D91F0, 0x50B6A0), WALK-002 (avoid_walls' corner blocks and the position's cell), WALK-004 (the whole-walk destinations of state 10, one route to the ride's back cell, and
/// state 18, one route to the gate's crossing cell), WALK-008 (the arrival chain, states 0 to 5, and the way home, over the cell map) and WALK-009 (the progress measure and its resets).
/// </summary>
public static class WalkApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("WALK-005", "supported track origins and nonzero type-12 parents now consult the low flag nibble in ParkCellMap.Tracks; type-17 and zero/invalid-parent records retain the earlier fallthrough. the mask-2 rule for a type-9 target (the owner cell's ride must be rideable and have CannotRide 0, 0x4D88BC..0x4D8B28) is ported but no simulated walker uses mask 2 (its only callers are the camera's, 0x42C093 and 0x42C290), and the underground link byte (+0x22) is read only inside the land tools' calls (flag 0x81B4CC, 0x532408..0x532622); the host track layer retains TRACK-010 bridge/underlay limitations"),
		("WALK-010", "the attraction ids that the guests' histories, queues and the economy hold are the script world's attraction counter, not the objects' thing ids (ThingPool slots 11, 12, ...), as far as OpenTPW holds them; the pool itself follows tp.exe's allocator (0x516270: ids 1, 2, 3, ... on a fresh park, the ten singletons of the world init 0x5156A0 first, then the gates and lights as 11 and 12; a freed slot taken again last in, first out, after the turn's deferred destroy, 0x516310; an original save's chain kept slot by slot with the free list rebuilt ascending, 0x518D43; every thing constructor ends in the base constructor 0x50AFE0, which has twelve callers: the world init, the person base 0x4F8940 and the object base 0x4DB090), and the things the original builds that OpenTPW does not are approved design choices, not part of this ID: the developer prototype ride ([EXT:developer-prototype]), the online persons and avatars, which OpenTPW draws as presence figures ([EXT:online-visit-presence], [EXT:online-coop-presence]), and the placeholder slot an imported original save's not-yet-rebuilt thing keeps (user-approved 2026-10-17, [EXT:resume-exact-thing-pool]; what such a thing stands for is tracked by the SAVE IDs); evidence needed: which id tp.exe's guest histories, queue words and economy records hold for an attraction (the thing id or a counter of their own)"),
	};
}
