namespace OpenTPW;

/// <summary>
/// Approximation register of guest arrivals (docs/GUESTS.md, "Arrivals"). Each entry is tagged
/// <c>[APPROX:ARRIVAL-NNN]</c> at its code site; the arrival rule itself is traced from the Mac binary.
/// </summary>
public static class ArrivalApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("ARRIVAL-008", "THEME PARK INC WEATHER: Theme Park Inc's group size follows its own routine (TPI Game.exe 0x5BCD60: a percentage of the weather state, 0.01 in single precision, divided by the points per visitor; oracle X7), and the weather state is the running spell's kind (+0x70, 1 heatwave, 2 rain, 3 storm, 4 snow; 0x642A80) that the economy now rolls as 0x642770 does ([EXT:tpi-weather-spell-seed]: the original seeds the C runtime rand() with GetTickCount() at 0x6A151B, so its draws cannot be reproduced and the economy's world stream stands in); the routine's other exit is read and ported: the object 0xA06558 is the theme selection of the startup dialog (Arabian 0, Science 1, Water 2, Intro World 3; setter 0x42A360 called by the radio group 0x498940..0x4989CA, -1 becomes 3 at 0x6A32D7) and the Intro World (levels/wIntro, which OpenTPW's front end does not run) has no arrivals (GuestSimulation.IntroWorld); the three words after the Seasons rows are read (the loader writes the array count, 0x419FB4, here 4, and the two online-only keys stay 0 offline); left: the weather thing's own per-turn effects (the sky quality float +0x74 read at 0x687665, drops, lightning and thunder: WeatherEffects.* and Weather.SpeedOfChange, Min/MaxDaysFor*WeatherSpell) are not simulated, and no shipped Theme Park Inc object sets ClosedDuring* (the attractions' own closing in a spell, 0x5DEEBF, is not ported: its data is only in a comment of Rides.sam); still unread: the weather thing's per-turn update and what it writes (+0x74 and the effect counters), the guests' weather reactions that are not in the umbrellas and rain-mood extensions (0x6103AC, 0x6109D6, 0x616DD2, 0x6178DF), and the attractions' weather closing (0x5DEEBF) for a mod that sets ClosedDuring*"),
	};
}
