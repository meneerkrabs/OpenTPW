namespace OpenTPW;

/// <summary>
/// Simulation speed: the factor of the simulation clock. The original has one clock scale for everything that runs on its
/// scheduler (park turns and with them the guests, rides and economy, ride scripts, animation): 1.0 at the start, stepped by
/// 1.25 within 0.25 .. 2.0 by three key actions (ORACLE.md F32). The four speeds here are OpenTPW's buttons; each sets the
/// scale (<see cref="GameSpeedScale"/>), which <c>Level</c> applies to the whole simulation. Pause is UITEXT 403 "PAUSED".
/// </summary>
public enum GameSpeed
{
	Paused = 0,
	Normal = 1,
	// [BIN:TPW-EXE:0x40c3b0 speed actions] the key table at 0x7480a0 maps VK_SUBTRACT (0x6D) to 0x40c3b0 (rate × 0.8, 0x403340), VK_ADD (0x6B) to 0x40c3c0 (rate × 1.25, 0x4032f0) and VK_MULTIPLY (0x6A) to 0x40c3d0 (rate 1.0, 0x4032d0); both steps clamp to 0.25..2.0 (0x6fd648 / 0x6fd650) on the clock 0x785970 +0x20
	// [EXT:speed-buttons] Fast ×2 and Fastest ×4 are OpenTPW's speeds: the original has no fixed speeds and no on-screen speed control, only the numpad keys of the game key table (0x7480a0) that scale its clock rate by 0.8 and 1.25 within 0.25..2 and reset it to 1
	Fast = 2,
	Fastest = 4
}

/// <summary>The simulation clock's scale for a <see cref="GameSpeed"/>.</summary>
public static class GameSpeedScale
{
	/// <summary>0 when paused, else 1, 2 or 4: the factor applied to the elapsed real time of the whole simulation (guests, rides, scripts and economy alike).</summary>
	// [BIN:STP-PPC:0x10E844 scheduler clock] the scheduler adds each raw millisecond gap times its scale (+24, 1.0 by default) to its accumulator (0x127CD0); park turns, script waits and animation all read it, so a scale of 2.0 doubles every one of them (oracle F32)
	public static float Of( GameSpeed speed ) => speed == GameSpeed.Paused ? 0f : (int)speed;
}

/// <summary>The date fields the original's calendar keeps from its last update: month 0-based (+16), day 1-based (+20) and year (+24).</summary>
public readonly record struct CalendarFields( int Month, int Day, int Year );

/// <summary>What a calendar update found changed.</summary>
[Flags]
public enum CalendarChange
{
	None = 0,
	Day = 1,
	Month = 2,
	Year = 4
}

/// <summary>A point on the park calendar (1-based year, month and day; hour 0–23).</summary>
public readonly record struct ParkDate( int Year, int Month, int Day, int Hour )
{
	public override string ToString() => $"Year {Year}, month {Month}, day {Day}, {Hour:00}:00";
}

/// <summary>
/// The park calendar, as in the Mac binary: the world counts park turns (<c>mGameTick</c>), one per
/// 248 ms of scaled time, and the date is 2000-01-01 00:00 plus turn × 15000 / 4 = 3750 seconds,
/// converted through the operating system's (Gregorian) calendar. A game day is therefore 23.04
/// turns, about 5.7 s at normal speed, and months have their civil lengths. OpenTPW samples turns
/// from its fixed 60 Hz clock. The conversion and the day, month and year detection run in the
/// PowerPC oracle (docs/reverse/ORACLE.md F30: <c>calendar-date</c>, <c>calendar-update</c>).
/// </summary>
public static class ParkCalendar
{
	public const int TicksPerSecond = FixedStepClock.TicksPerSecond;
	// [BIN:STP-PPC:0x101C22E0 scheduler] eight 31 ms substeps make one park turn (248 ms of scaled time); the turn counter is world +0x1DA70C (0x10105398)
	public const int TurnMilliseconds = 248;
	// [BIN:STP-PPC:0x100E4394 calendar conversion] seconds = turn × mFunnySecsPerRealSec (15000, set by 0x100E3C90) / 4
	public const long SecondsPerTurn = 15000 / 4;
	// [BIN:STP-PPC:0x100E4348 calendar start] TbTimeStamp::SetTime( 2000, 1, 1, 0, 0, 0, 0 )
	public static readonly DateTime Epoch = new( 2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified );
	// [BIN:TPW-EXE:0x54e360 park frame] case 10: the backlog over the scaled millisecond clock (0x4031e0) is cut to 2000 ms, each substep adds 31 ms, every eighth substep is a park turn (0x516380, 8 × 31 = 248 ms) while fewer than three ran this frame (DAT_00879064, reset after the loop)
	// [BIN:TPW-EXE:0x5f5e80 clock route] the raw millisecond clock (0x5f5f10) reads QueryPerformanceCounter, (count − base) / counts per ms + the base time, whenever the constructor found QueryPerformanceFrequency / 1000 in 10..32,000,000 counts per ms, else timeGetTime
	// [EXT:fixed-60hz-clock] the simulation runs on OpenTPW's deterministic fixed 60 Hz clock, sampled into 248 ms park turns (4.03 a second at normal speed), not on the wall clock's 31 ms substeps: the same inputs give the same run on every machine (docs/DETERMINISM.md), and a wall-clock-driven turn count would make a replay depend on the frame rate
	// [EXT:fixed-catch-up-cap] under load OpenTPW's fixed clock runs at most 16 ticks (266.7 ms) of a frame and drops the rest, where the original's loop catches up a backlog of up to 2000 ms in 31 ms substeps with at most three park turns a frame (0x54E360 case 10; further due turns in that frame are skipped, not deferred): the cap is the deterministic clock's guard against a spiral of ever longer frames on a slow machine, and 16 ticks span at most two park turns, so the three-turn rule is never reached
	private const long TickTurnNumerator = 1000;
	private const long TickTurnDenominator = (long)TicksPerSecond * TurnMilliseconds;
	// [BIN:STP-PPC:0x101C5C4C TbTimeStamp::GetTime] the hour comes from the OS LongDateRec hour field (0–23)
	public const int HoursPerDay = 24;
	public const int MonthsPerYear = 12;
	public const long SecondsPerHour = 3600;
	public const long SecondsPerDay = 86400;

	/// <summary>The park turn reached at a fixed tick.</summary>
	public static long Turn( long tick ) => tick * TickTurnNumerator / TickTurnDenominator;

	/// <summary>
	/// The latest turn a clock whose turns are cut a phase apart from the ticks' own can be on at <paramref name="tick"/> (the guests' park turn after
	/// <see cref="GuestSimulation.AlignTurns"/>, <c>Turn( tick + phase ) - Turn( phase )</c>, for a phase a snapshot does not hold): the tick's
	/// <see cref="Turn"/>, or one more, which some phase gives unless a turn boundary falls exactly at the tick.
	/// </summary>
	public static long LatestPhasedTurn( long tick ) => Turn( tick ) + (tick * TickTurnNumerator % TickTurnDenominator == 0 ? 0 : 1);

	/// <summary>The first fixed tick at which <paramref name="turn"/> is reached.</summary>
	public static long TickOfTurn( long turn ) => (turn * TickTurnDenominator + TickTurnNumerator - 1) / TickTurnNumerator;

	/// <summary>Park-clock seconds since the 2000-01-01 epoch at a fixed tick.</summary>
	public static long Seconds( long tick ) => Turn( tick ) * SecondsPerTurn;

	/// <summary>The first fixed tick whose park-clock time is at least <paramref name="seconds"/>.</summary>
	public static long TickAtSeconds( long seconds ) => TickOfTurn( (seconds + SecondsPerTurn - 1) / SecondsPerTurn );

	// [BIN:TPW-EXE:0x4f85a0 park date] the date is the FILETIME of 2000-01-01 (0x4f7e80, rate 15000 at +0x1c) plus floor(turn × 15000 / 4) × 10^7, decoded by FileTimeToSystemTime (0x5fc660, IAT 0x6fd0bc): the proleptic Gregorian calendar in UTC, with no locale calendar and no time zone
	// [BIN:STP-PPC:0x100E4394 calendar conversion] the date of a park turn is the 2000-01-01 start plus floor(turn x 15000 / 4) seconds (oracle F30: 647 turns and rates agree)
	public static DateTime DateAtTurn( long turn ) => Epoch.AddSeconds( turn * SecondsPerTurn );

	public static DateTime DateTimeAt( long tick ) => DateAtTurn( Turn( tick ) );

	/// <summary>The date fields at a park turn, as the original's calendar stores them after an update.</summary>
	public static CalendarFields FieldsAtTurn( long turn )
	{
		var date = DateAtTurn( turn );
		return new CalendarFields( date.Month - 1, date.Day, date.Year );
	}

	/// <summary>
	/// The calendar update (0x100E3F0C): compares the day of the month, the month (0-based) and the year of the date at
	/// <paramref name="turn"/> with the stored ones, stores the new fields and reports which of them differ. It posts a
	/// single message per kind however many days have passed, so it is called once per turn.
	/// </summary>
	// [BIN:STP-PPC:0x100E3F0C calendar update] day, month and year messages when the extracted field differs from the stored one; the original's constructor stores day -1 and year 0, so its first update also posts a day and a year message
	// [BIN:TPW-EXE:0x004F7E80 calendar constructor] a new park's calendar stores day cache -1; the world init 0x005156A0 syncs only the month and year caches to turn 0 (0x004D7AF0 -> 0x004F85A0), so the first update 0x004F8260 posts one day message (0x0040F4C0) whatever the date (the comparison at 0x004F8321)
	// [BIN:TPW-EXE:0x004F7F30 calendar loader] the loader restores the saved caches unchecked, so a restart save that still holds day -1 posts the same first day message; ParkEconomy.PendingInitialDay owns that pending message for a new park, for a restart import and for an own save made before the first turn
	public static CalendarChange Update( ref CalendarFields stored, long turn )
	{
		var now = FieldsAtTurn( turn );
		var changes = CalendarChange.None;
		if ( now.Day != stored.Day )
			changes |= CalendarChange.Day;
		if ( now.Month != stored.Month )
			changes |= CalendarChange.Month;
		if ( now.Year != stored.Year )
			changes |= CalendarChange.Year;
		stored = now;
		return changes;
	}

	public static long HourIndex( long tick ) => Seconds( tick ) / SecondsPerHour;
	public static long DayIndex( long tick ) => Seconds( tick ) / SecondsPerDay;

	public static long MonthIndex( long tick )
	{
		var date = DateTimeAt( tick );
		return (date.Year - Epoch.Year) * MonthsPerYear + date.Month - 1;
	}

	/// <summary>The first fixed tick of a calendar day (0 = 2000-01-01).</summary>
	public static long TickAtDay( long dayIndex ) => TickAtSeconds( dayIndex * SecondsPerDay );

	/// <summary>The first fixed tick of a calendar month (0 = January 2000).</summary>
	public static long TickAtMonth( long monthIndex )
	{
		var start = Epoch.AddMonths( checked((int)monthIndex) );
		return TickAtSeconds( (long)(start - Epoch).TotalSeconds );
	}

	/// <summary>Fixed ticks that <paramref name="hours"/> park-clock hours take at normal speed (at least one).</summary>
	public static long TicksForHours( double hours ) => Math.Max( 1, (long)Math.Round( hours * SecondsPerHour / SecondsPerTurn * TurnMilliseconds * TicksPerSecond / 1000 ) );

	public static ParkDate ToDate( long tick )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( tick );
		var date = DateTimeAt( tick );
		return new ParkDate( date.Year - Epoch.Year + 1, date.Month, date.Day, date.Hour );
	}

	/// <summary>Converts a duration the original files give in seconds to ticks at normal speed.</summary>
	public static long SecondsToTicks( double seconds ) => (long)Math.Round( seconds * TicksPerSecond );
}

/// <summary>
/// SplitMix64: a small deterministic generator whose whole state is one saved integer. The simulation's draws come from <see cref="WorldRng"/>, the original's one world generator; this one
/// serves the weather ([EXT:weather-stream], its own stream) and presentation (staff voices, the weather's visuals).
/// </summary>
public sealed class DeterministicRandom
{
	public DeterministicRandom( ulong state ) => State = state;

	public ulong State { get; private set; }

	internal void Restore( ulong state ) => State = state;

	public ulong NextUInt64()
	{
		var z = State += 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return z ^ (z >> 31);
	}

	/// <summary>Uniform integer in [0, <paramref name="exclusiveMaximum"/>).</summary>
	public int Next( int exclusiveMaximum )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( exclusiveMaximum );
		return (int)(NextUInt64() % (ulong)exclusiveMaximum);
	}

	/// <summary>True with probability <paramref name="percent"/>/100.</summary>
	public bool Chance( int percent ) => Next( 100 ) < percent;
}
