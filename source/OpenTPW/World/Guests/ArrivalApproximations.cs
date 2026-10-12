namespace OpenTPW;

/// <summary>
/// Approximation register of guest arrivals (docs/GUESTS.md, "Arrivals"). Each entry is tagged
/// <c>[APPROX:ARRIVAL-NNN]</c> at its code site; the arrival rule itself is traced from the Mac binary.
/// </summary>
public static class ArrivalApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("ARRIVAL-001", "STEP-OFF: a group's guests appear every 0.4 s at the arrival lanes instead of at the stops of a bus sized by the group"),
		("ARRIVAL-002", "FACTOR: groups always use 1.2; the original uses 0.8 while the +48 word of the thing at game +0x1DA734 is positive (oracle F9); the 500 cut is online-only"),
		("ARRIVAL-003", "POINTS: every ride and sideshow counts; the original counts only things passing 0xDC834 (ride state, coaster riders and editing, a built kart or water track, untraced tests), and kind 0/2 is read as ride/sideshow (oracle F10)"),
		("ARRIVAL-004", "FLOOR: a group cut by the 1500 limit stops at 0; the original keeps 1500 minus guests, negative above the limit (oracle F9). Executed (oracle F11), a negative group ends at the next stop like 0: no guest, timer restart, active byte cleared. Only the stored group word and one arrival-vehicle step (slot 74, 'group == 0', effect untraced) differ"),
		("ARRIVAL-005", "FIRST PURCHASE: imported and fixed objects set a type's first-purchase turn to the current turn; the original restores park record +28 from its save"),
	};
}
