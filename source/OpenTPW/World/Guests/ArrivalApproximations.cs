namespace OpenTPW;

/// <summary>
/// Approximation register of guest arrivals (docs/GUESTS.md, "Arrivals"). Each entry is tagged
/// <c>[APPROX:ARRIVAL-NNN]</c> at its code site; the arrival rule itself is traced from the Mac binary.
/// </summary>
public static class ArrivalApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("ARRIVAL-001", "STEP-OFF: one guest steps off per park turn from the turn after the group starts, at the arrival lanes, with no vehicle. The original's arrival update (tp.exe 0x4CF3E0, once per world turn) dispatches a vehicle when a group starts (0x51A2F0: a bus for a group up to 35, a seaplane up to 60, a Ferry above, 0x4CF48C/0x4CF49A) and creates a guest only on a turn its script variable 1 reads 2, the stop (0x4CF46C via 0x51A690; 6, the vehicle gone, dispatches the next). The time the vehicle's level script (bus, seaplane or Ferry .RSE) takes to reach 2 is data, not decoded here, so OpenTPW's first guest comes that much earlier, always as from a bus (vehicle choice: RIDES-019)"),
		("ARRIVAL-002", "ONLINE VISIT GROUP: the cut matches tp.exe 0x4CF5B0 (counted guests plus group at most 500 while the GameType word 0xFB3B7C is 1, else 1500): a visit of a published park sets GameType 1 (0x4C0830, the park-info dialog's completion), and the visited park runs the arrival step (main loop 0x54F760 -> 0x5166B0 -> 0x516380 -> 0x4CF3E0); the factor matches too (0.8 while the weather thing's +0x30 word, mCurrentDrops, is positive, 0x4CF60E; the drop ramp is AUDIO-025..027, 029, 030). What differs on a visit: OpenTPW reads the group's Arrival.* keys from Standard.sam where the original loads data\\levels\\online_Standard.sam and the level's online_Standard.sam for GameType 1 (0x51573C, 0x515755; Arrival.PointsPerVisitor 2 instead of 6), and a read-only visit has no economy, so its groups count 0 park points"),
		("ARRIVAL-003", "POINTS: the counts-test (0xDC834, tp.exe 0x4DD920) is applied except the coaster controller test and the kart track word (a water ride's track-valid flag is applied, docs/TRACKS.md), and kind 0/2 is read as ride/sideshow"),
		("ARRIVAL-004", "FLOOR: a group cut by the 1500 limit stops at 0; the original keeps 1500 minus guests, negative above the limit (oracle F9). Executed (oracle F11), a negative group ends at the next stop like 0: no guest, timer restart, active byte cleared. Only the stored group word and one arrival-vehicle step (slot 74, 'group == 0', effect untraced) differ"),
		("ARRIVAL-005", "FIRST PURCHASE ON IMPORT: an object imported from an original save sets its type's first-purchase turn to the import turn while it is 0; the original restores every type's record (+0x1C first purchase, +0x18 copies) verbatim from the save's object-control block (the world loader 0x5179C0 reads it with 0x4D3AA0 at 0x5181E7: 150 records of 32 bytes, the count and the search key), adds zeroed records for types missing from it (0x4D3E00 after the load) and stamps a turn only into a record still at 0 (0x4DB690); OpenTPW keeps that block opaque. Fixed items of a new park agree: the park gates and traffic lights are built after the turn is set to 0 (0x515865)"),
		("ARRIVAL-006", "BOOTH WAIT: the booth's expensive wait counts park turns of the fixed clock (248 ms each) rather than the guest updates of state 4, and its draws come from the guest stream (DET-014)"),
		("ARRIVAL-007", "PURSE: OpenTPW charges the entrance fee from the guest's purse and turns back a guest who cannot pay; the original's booth books the fee (0x004D0600) without reading the guest's cash"),
	};
}
