namespace OpenTPW;

/// <summary>
/// Simulation speed. Only <see cref="Paused"/> is evidenced by the original data (UITEXT 403
/// "PAUSED", <c>paused.MD2</c>); the faster speeds are an OpenTPW convenience.
/// </summary>
public enum GameSpeed
{
	Paused = 0,
	Normal = 1,
	// [APPROX:ECON-004] Fast x2 and Fastest x4 speeds (only pause is evidenced) — evidence needed: original speed controls, if any
	Fast = 2,
	Fastest = 4
}

/// <summary>A point on the park calendar (1-based year, month and day; hour 0–23).</summary>
public readonly record struct ParkDate( int Year, int Month, int Day, int Hour )
{
	public override string ToString() => $"Year {Year}, month {Month}, day {Day}, {Hour:00}:00";
}

/// <summary>
/// The park calendar. The original data counts in days (challenge targets, weather, ride age),
/// months (wages are paid "at the end of the month", loans, bankruptcy) and years (scrap value,
/// end-of-year summary) and its clock script reads hours (<c>Clock.RSE</c>: <c>HOUR</c> mod 12).
/// The lengths are <b>approximations</b>: the original day length and month lengths are unknown, so
/// a day is <see cref="TicksPerDay"/> fixed 60 Hz ticks (4 s at normal speed) and every month has
/// <see cref="DaysPerMonth"/> days.
/// </summary>
public static class ParkCalendar
{
	public const int TicksPerSecond = FixedStepClock.TicksPerSecond;
	// [APPROX:ECON-001] one game day = 240 fixed ticks (4 s at normal speed) — evidence needed: capture of the original clock against wall time
	public const int TicksPerDay = 4 * TicksPerSecond;
	// [APPROX:ECON-003] 24 hours per day (Clock.RSE only shows HOUR is used mod 12) — evidence needed: original HOUR range (binary or Clock.RSE trace)
	public const int HoursPerDay = 24;
	public const int TicksPerHour = TicksPerDay / HoursPerDay;
	// [APPROX:ECON-002] every month has 30 days, 12 months per year — evidence needed: original calendar (binary or captured date display)
	public const int DaysPerMonth = 30;
	public const int MonthsPerYear = 12;
	public const int DaysPerYear = DaysPerMonth * MonthsPerYear;

	public static long DayIndex( long tick ) => tick / TicksPerDay;
	public static long MonthIndex( long tick ) => DayIndex( tick ) / DaysPerMonth;

	public static ParkDate ToDate( long tick )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( tick );
		var day = DayIndex( tick );
		var month = day / DaysPerMonth;
		return new ParkDate( (int)(month / MonthsPerYear) + 1, (int)(month % MonthsPerYear) + 1, (int)(day % DaysPerMonth) + 1, (int)(tick % TicksPerDay / TicksPerHour) );
	}

	/// <summary>Converts a duration the original files give in seconds to ticks at normal speed.</summary>
	public static long SecondsToTicks( double seconds ) => (long)Math.Round( seconds * TicksPerSecond );
}

/// <summary>SplitMix64: a small deterministic generator whose whole state is one saved integer.</summary>
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
