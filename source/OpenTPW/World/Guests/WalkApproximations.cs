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
		("WALK-010", "thing ids are slots of one pool of 10239 for every kind of thing, taken in the order the park builds them as tp.exe's allocator does (0x516270: ids 1, 2, 3, ... on a fresh park, the world's ten singletons first, then the gates and lights as 11 and 12; a freed slot taken again last in, first out, after the turn's deferred destroy, 0x516310; an original save's chain kept slot by slot with the free list rebuilt ascending, 0x518D43), so the guest's needs phase, reseed, prank flag and the id rules read the original's numbers; but the pool holds only what OpenTPW builds, so a thing the original builds that OpenTPW does not (the online persons and avatar, the developer prototype ride) takes no slot (the finale's End takes its slot in both game types, as the original's 0x516B00 constructs it through 0x4DB090), and a saved thing of an imported original save that OpenTPW does not rebuild holds a placeholder slot that nothing simulates or frees, where the original's thing would run on and be destroyed in time; the attraction ids the guests' histories, queues and the economy hold are the script world's counter, not the objects' thing ids — evidence needed: what else the original builds over a session (the callers of the destroy request 0x50B780 and of the thing constructors outside the online code)")
	};
}
