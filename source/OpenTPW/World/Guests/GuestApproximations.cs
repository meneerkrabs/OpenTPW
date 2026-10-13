namespace OpenTPW;

/// <summary>
/// Approximation register of the guests' core decisions (docs/GUESTS.md "Decision", "Needs", docs/reverse/ORACLE.md F24..F26). Each entry is
/// tagged <c>[APPROX:GUEST-NNN]</c> at its code site; the rules themselves are executed in the PowerPC oracle.
/// </summary>
public static class GuestApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("GUEST-001", "CELL MODIFIER: the environment record's component 4 at the attraction's cell divides the distance match (0xE9388); OpenTPW has no region effects (entertainers, vomit, toilets, guards), so it is 0 (oracle F24)"),
		("GUEST-002", "BACK CELL AND QUEUE: the attraction's back-of-queue cell (0xDD3F4) is the bridge's last queue cell and its queue count the guests in OpenTPW's queue list; candidates run in attraction-id order, not the original's thing-list order (oracle F24)"),
		("GUEST-003", "PRICE: the original's price test at the stand point is applied (tp.exe 0x4FDE50); its per-type park word is taken as 0 and the refusal's animation, event record, walk-away count and price statistic are not modelled"),
		("GUEST-004", "FULL QUEUE ROOM: a physically full queue (count at 4 per cell) refuses like the queue-too-long case for thought and happiness but pushes no history; its original path is the dismissal 0xEE804 (oracle F24)"),
		("GUEST-005", "USE EFFECTS: a relief object sets the toilet meter to 0 and the litter effect stays in the park's store (ECON-020); the original's toilet branch (0xEB2B0, 0xEB32C clears illness above 90), the ingredient and appearance effects and the guest litter meter are not modelled (oracle F18)"),
		("GUEST-006", "ENVIRONMENT AND THOUGHTS: the three words of the guest's cell environment record (happiness, illness, hunger, written by entertainers, toilets, vomit and guards) are 0 in the needs update, and the thought pick (a draw modulo 10) and thought countdown that follow it are not modelled (oracle F25)"),
		("GUEST-007", "PRANKERY: the constructor's eighth draw sets the guest byte +448 (100 + id modulo 3 with PrankeryLikelihood percent); OpenTPW has no pranksters (oracle F25)"),
		("GUEST-008", "ENERGY: OpenTPW's energy meter has no original counterpart (the original has no energy field and nothing here reads it); its drain and recovery rates are invented"),
		("GUEST-009", "THOUGHTS AND BOREDOM: the thought thresholds (a need from NeedThoughtLevel 70) and the boredom loss of SmallHappinessChange per decision after BoredomThreshold seconds have no original source; the original's thoughts (0xE8C58: hunger and thirst both above 90, a single need above 99, happiness above 99 or below 10, litter) are picked on a draw each needs phase"),
		("GUEST-010", "LEAVING TEST: the original tests leaving in its state 6 (a guest standing at a cell, deciding), once per park turn it spends there; OpenTPW has no separate state 6 and tests every guest that is walking around or doing nothing on every park turn (oracle F26)"),
		("GUEST-011", "CLOSURE: OpenTPW pulls guests out of queues and drops GoingToRide reservations when the park closes; in the original no queue state and no ride routine reads the closed word (tp.exe), so its queues keep running, while state 10 (going to a ride) does drop the reservation"),
		("GUEST-012", "STATE-18 RECALL: the leaving handler's recall (0xedc98) also demands the guest's cell pass 0xe6c6c (its cell record through 0x10a9b0 and 0x85190..0x851d0), which is untraced; OpenTPW approximates the predicate by the state boundary — only a guest still walking the paths to the exit recalls, and the road ends it (the booth refusal enters on a lane and is exempt, like the original's zeroed counter 0xecacc) (oracle F26)"),
		("GUEST-920", "Visitor elapsed simulation seconds start at successful admission or zero for seeded guests; the admission tick and travel home are excluded, and native timing is untraced"),
	};
}
