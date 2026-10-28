namespace OpenTPW;

/// <summary>
/// Approximation register of guest arrivals (docs/GUESTS.md, "Arrivals"). Each entry is tagged
/// <c>[APPROX:ARRIVAL-NNN]</c> at its code site; the arrival rule itself is traced from the Mac binary.
/// </summary>
public static class ArrivalApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("ARRIVAL-001", "VEHICLE STOP: the arrival update drives the level's bus, seaplane or Ferry as tp.exe 0x4CF3E0 does (dispatch by group size 0x51A2F0, guests only at status 2, the vehicle's own script for the drive), a guest is made at the vehicle's stop cell and walks the cell map through the booth into the park as states 0 to 5 do (0x4CF720, 0x4FB201, 0x4FF520, 0x4FF730, 0x4FF7F0, 0x4FF9D0, 0x4FFB20), and a leaving guest walks the cell map from the gate's crossing cell as the original's states 19 to 21 do (0x500AD0 waits for the vehicle's status, 0x51A760, then a world draw picks one of the four cells around BusStopA, 0x4D8650, and two more the point in it; 0x500BD0 re-picks at status 1 to 3 off the vehicle kind's two cells and boards the guest heading its cell's thing list at status 4; the pick-up test 0x51A9D0 reads those heads), but a cell's thing list holds OpenTPW's guests only (WALK-003); and an imported original save starts with no vehicle at work (its +0x1DA72A word is not decoded)"),
		("ARRIVAL-006", "BOOTH WAIT: an expensive verdict holds the guest for 50 + R % 50 state-4 updates, one per executed park turn, and the verdict runs again on the turn after (tp.exe 0x4FF9D0), N + 1 turns in all; OpenTPW counts those N + 1 park turns too, but its fixed-step loop drops none, where the original's game loop catches up at most 2000 ms in 31 ms ticks (0x54F493 clamps the clock, 0x54F4C4 adds 0x1F per tick: up to 65 ticks), takes a park turn on every 8th tick (up to eight due in one pass) and runs at most 3 of them per pass, so a pass can drop more than 3 (0x54F668 tests the tick, 0x54F680 the counter 0x879064 < 3, a due turn beyond the third is skipped, not deferred); the booth's first verdict is the guest's next update after the walk's arrival, as the original's is (0x4FF730, 0x4FF9D0)"),
		("ARRIVAL-008", "THEME PARK INC WEATHER: Theme Park Inc's group size follows its own routine (TPI Game.exe 0x5BCD60: a percentage of the weather state, 0.01 in single precision, divided by the points per visitor; oracle X7), but OpenTPW runs no Theme Park Inc weather, so the weather state is always none and the percentage is always 100, and the routine's exit for a mode object answering 3 is not modelled — evidence needed: the weather thing's state word (+0x70 of the weather thing the exe looks up through 0x4019B5) and what writes it, and what mode 3 of the object behind 0xA06558 is"),
	};
}
