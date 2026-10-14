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
		("GUEST-005", "USE EFFECTS AND LITTER: a visit fills the litter meter, a relief visit wears the toilet, empties the toilet meter and clears illness above 90 (tp.exe 0x4FE1E0), and a full meter goes to a bin within reach or onto the cell (0x4FEC90, 0x500DC0, 0x4FFF20), but the visit's guest records and the ingredient and appearance effects beyond the speed boost are not modelled, a bin's access cell is its entrance's outside cell or first path neighbour, and a bin removed during the walk counts as none"),
		("GUEST-006", "ENVIRONMENT AND THOUGHTS: the three words of the guest's cell environment record (happiness, illness, hunger, written by entertainers, toilets, vomit and guards) are 0 in the needs update, and the thought pick (a draw modulo 10) and thought countdown that follow it are not modelled (oracle F25)"),
		("GUEST-007", "PRANKS: the prankery flag is not modelled; in the original an unhappy guest (happiness below 15) in state 6 drops litter or picks on another guest by its flag (tp.exe 0x4FEC90)"),
		("GUEST-008", "ENERGY: OpenTPW's energy meter has no original counterpart (the original has no energy field and nothing here reads it); its drain and recovery rates are invented"),
		("GUEST-009", "THOUGHTS AND BOREDOM: the thought thresholds (a need from NeedThoughtLevel 70) and the boredom loss of SmallHappinessChange per decision after BoredomThreshold seconds have no original source; the original's thoughts (0xE8C58: hunger and thirst both above 90, a single need above 99, happiness above 99 or below 10, litter) are picked on a draw each needs phase"),
		("GUEST-010", "STATES 6 AND 7: OpenTPW runs the original's decision state and wander walk (x86 X18, X19) without the litter step and the vomit's litter (no litter model), the presentation turns towards shows and performers, the walk's stuck result, the off-path destination search and the stranded mark; the leaving gate's route test asks for a path to an arrival lane's end (none: the guest leaves when the park has no lanes), and a step's candidates are the current cell's links to path cells (the original removes only queue and type-10 neighbours)"),
		("GUEST-011", "CLOSURE: OpenTPW pulls guests out of queues and drops GoingToRide reservations when the park closes; in the original no queue state and no ride routine reads the closed word (tp.exe), so its queues keep running, while state 10 (going to a ride) does drop the reservation"),
		("GUEST-012", "STATE-18 RECALL: the leaving handler's recall (0xedc98) also demands the guest's cell pass 0xe6c6c (its cell record through 0x10a9b0 and 0x85190..0x851d0), which is untraced; OpenTPW approximates the predicate by the state boundary — only a guest still walking the paths to the exit recalls, and the road ends it (the booth refusal enters on a lane and is exempt, like the original's zeroed counter 0xecacc) (oracle F26)"),
		("GUEST-920", "Visitor elapsed simulation seconds start at successful admission or zero for seeded guests; the admission tick and travel home are excluded, and native timing is untraced"),
	};
}
