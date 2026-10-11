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
		("ARRIVAL-002", "MODE: the 500-guest limit and the 0.8 factor of the original's other states are not reproduced; groups use 1.2 and 1500"),
		("ARRIVAL-003", "POINTS: rides and sideshows count, aged from their build turn; the type kind, the second age source and the open-state test are not traced"),
	};
}
