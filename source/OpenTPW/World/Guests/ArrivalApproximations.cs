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
		("ARRIVAL-003", "POINTS: every ride and sideshow counts, aged from its own build and divided by its type's count; the original counts only things passing 0xDC834, ages a type with +0x9C == 0 from its first purchase and counts every copy of other types in full (oracle F10)"),
		("ARRIVAL-004", "FLOOR: a group cut by the 1500 limit stops at 0; the original keeps 1500 minus guests, negative above the limit (oracle F9)"),
	};
}
