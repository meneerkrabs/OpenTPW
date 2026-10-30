namespace OpenTPW;

/// <summary>
/// Approximation register of guest arrivals (docs/GUESTS.md, "Arrivals"). Each entry is tagged
/// <c>[APPROX:ARRIVAL-NNN]</c> at its code site; the arrival rule itself is traced from the Mac binary.
/// </summary>
public static class ArrivalApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("ARRIVAL-001", "VEHICLE STOP: the arrival update drives the level's bus, seaplane or Ferry as tp.exe 0x4CF3E0 does (dispatch by group size 0x51A2F0, guests only at status 2, the vehicle's own script for the drive), a guest is made at the vehicle's stop cell and walks the cell map through the booth into the park as states 0 to 5 do (0x4CF720, 0x4FB201, 0x4FF520, 0x4FF730, 0x4FF7F0, 0x4FF9D0, 0x4FFB20), and a leaving guest walks the cell map from the gate's crossing cell as the original's states 19 to 21 do (0x500AD0 waits for the vehicle's status, 0x51A760, then a world draw picks one of the four cells around BusStopA, 0x4D8650, and two more the point in it; 0x500BD0 re-picks at status 1 to 3 off the vehicle kind's two cells and boards the guest heading its cell's thing list at status 4, a staff member that entered the cell later heading it instead; the pick-up test 0x51A9D0 reads those heads, WALK-003 retired), but an imported original save starts with no vehicle at work (its +0x1DA72A word is not decoded)"),
		("ARRIVAL-008", "THEME PARK INC WEATHER: Theme Park Inc's group size follows its own routine (TPI Game.exe 0x5BCD60: a percentage of the weather state, 0.01 in single precision, divided by the points per visitor; oracle X7), and the weather state is the running spell's kind (+0x70, 1 heatwave, 2 rain, 3 storm, 4 snow; 0x642A80) that the economy now rolls as 0x642770 does ([EXT:tpi-weather-spell-seed]: the original seeds the C runtime rand() with GetTickCount() at 0x6A151B, so its draws cannot be reproduced and the economy's world stream stands in); left: (a) the routine's other exit (the mode object 0xA06558 answering 3, 0x5BCD7A) is not modelled: the object holds a mode 0..3 that a radio group of the options dialog sets (0x498940..0x4989C7) and that is 3 by default, but what the modes are is not read (evidence needed: its setter 0x42A360, getter 0x42A340, the readers 0x4B0352, 0x4B036C, 0x6A32F1, 0x6A4F76); (b) the three words after the Seasons table (row 4 of the roll table at 0xA024A0) are the count and two online-only keys that the offline data does not set, read as 4, 0, 0; (c) the spell's own effects (thunder countdown, wind, rain) are not simulated"),
	};
}
