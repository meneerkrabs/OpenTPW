namespace OpenTPW;

/// <summary>
/// Where one fixed 60 Hz simulation tick stands in the original's 31 ms substeps (tp.exe main loop <c>0x0054F4BF</c>): a substep runs the bump module, the script pass and, on even passes, the tour cars; every eighth is a park turn.
/// The park's substep pass counter (<c>0x877D34</c>, raised before the substep's work, saved with the park) is <c>tick * 1000 / 1860</c> of the park's own tick count, so the turn's substep is the one whose counter is a multiple of
/// eight and the turn counter is the pass counter divided by eight (<see cref="ParkCalendar.Turn"/>).
/// </summary>
// [BIN:TPW-EXE:0x54F4D3 substep counter] inc [0x877D34] at the start of each substep, before the inactive-application test (a substep skipped there still counts); the game pause 0x4092A0 freezes the clock object 0x785970 that drives the substep loop (0x54F47F..0x54F4AF), so a paused park runs no substep and its counter stands, as OpenTPW's clock does while paused; 0x54F5D7 tests its bit 0 for the tour cars, 0x54F668 its low three bits for the park turn
// [BIN:TPW-EXE:0x54F4D4 substep gate] the substep body is skipped (0x54F8CD) only while the application is inactive (0x7A1A14 bit 3 clear; the main window procedure 0x46B600 sets it on activation, 0x46B6F5, and clears it on deactivation, 0x46B722) and option flag 0x7A1A8C bit 0 is set (set by default: 0x449E1C stores 0xC0E15, or the saved options word 0x78D950 at 0x449E73; no other code changes the bit); the park pause instead stops the clock, so no substep is due (see the counter above); OpenTPW's simulation does not depend on window focus
// [EXT:fixed-60hz-clock] a 60 Hz tick is 16.667 ms, so at most one 31 ms substep ends in a tick; the substep runs in the tick that crosses its time and the 1.86 ticks between substeps run nothing of the original's
internal readonly record struct SimulationSubstep( bool Running, bool Due, long Pass, long ClockUnits )
{
	/// <summary>One substep in clock units (a unit is 1/60000 s: a tick is 1000, a substep 1860).</summary>
	public const long SubstepUnits = 1860;
	public const long TickUnits = 1000;
	/// <summary>The substep's length in script milliseconds (<c>0x54F4C4</c>: the scaled clock gains 0x1F per substep).</summary>
	public const double Milliseconds = 31;

	/// <summary>The substep state of the tick that starts from park tick <paramref name="tick"/>; a paused park runs none and keeps its counter.</summary>
	public static SimulationSubstep ForTick( long tick, bool running )
	{
		var before = tick * TickUnits / SubstepUnits;
		if ( !running )
			return new SimulationSubstep( false, false, before, tick * TickUnits );
		var end = (tick + 1) * TickUnits;
		var pass = end / SubstepUnits;
		return new SimulationSubstep( true, pass != before, pass, end );
	}

	/// <summary>The tour cars run on the even passes only (0x54F5D7).</summary>
	public bool TourDue => Due && (Pass & 1) == 0;

	/// <summary>Seconds since the last substep of the bump module (its tick is every substep).</summary>
	public double SecondsSinceModuleStep => (ClockUnits - Pass * SubstepUnits) / 60000d;

	/// <summary>Seconds since the last tour step (every second substep).</summary>
	public double SecondsSinceTourStep => (ClockUnits - (Pass & ~1L) * SubstepUnits) / 60000d;
}
