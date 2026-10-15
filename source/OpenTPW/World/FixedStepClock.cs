namespace OpenTPW;

/// <summary>
/// The clock's own-format state for a park save: the tick count, the fraction of the next tick already
/// banked and the diagnostics total of dropped time. Deep-copied on capture; validated before restore.
/// </summary>
public sealed record FixedStepClockSnapshot( long TickCount, double PendingSeconds, double DroppedSeconds );

public sealed class FixedStepClock
{
	public const int TicksPerSecond = 60;
	public const int MaximumCatchUpTicks = 16;
	public const double TickDuration = 1d / TicksPerSecond;
	public long TickCount { get; private set; }
	public double PendingSeconds { get; private set; }
	public double DroppedSeconds { get; private set; }

	public int Advance( double elapsedSeconds, Action<float> simulate )
	{
		ArgumentNullException.ThrowIfNull( simulate );
		if ( !double.IsFinite( elapsedSeconds ) || elapsedSeconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( elapsedSeconds ) );
		var acceptedSeconds = Math.Min( elapsedSeconds, TickDuration * MaximumCatchUpTicks );
		DroppedSeconds = Math.Min( double.MaxValue, DroppedSeconds + elapsedSeconds - acceptedSeconds );
		PendingSeconds += acceptedSeconds;
		var ticks = 0;
		while ( ticks < MaximumCatchUpTicks && PendingSeconds + TickDuration * 1e-9 >= TickDuration )
		{
			simulate( (float)TickDuration );
			PendingSeconds = Math.Max( 0, PendingSeconds - TickDuration );
			++TickCount;
			++ticks;
		}
		return ticks;
	}

	public void Reset()
	{
		TickCount = 0;
		PendingSeconds = 0;
		DroppedSeconds = 0;
	}

	/// <summary>The clock's state for a park save; a restore continues the tick remainder exactly where it stopped.</summary>
	public FixedStepClockSnapshot CaptureSnapshot() => new( TickCount, PendingSeconds, DroppedSeconds );

	/// <summary>
	/// Checks a snapshot against the clock's invariants without touching this clock: at rest the pending
	/// remainder is a fraction of one tick (<see cref="Advance"/> always drains below it) and dropped time
	/// is a non-negative total.
	/// </summary>
	public void ValidateSnapshot( FixedStepClockSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Fixed-step clock snapshot is inconsistent: {message}." );
		}
		Require( snapshot.TickCount >= 0, "negative tick count" );
		Require( double.IsFinite( snapshot.PendingSeconds ) && snapshot.PendingSeconds >= 0 && snapshot.PendingSeconds < TickDuration,
			"the pending remainder must be a fraction of one tick" );
		Require( double.IsFinite( snapshot.DroppedSeconds ) && snapshot.DroppedSeconds >= 0, "dropped seconds" );
	}

	/// <summary>Continues this clock (fresh or <see cref="Reset"/>) from a validated snapshot.</summary>
	public void RestoreSnapshot( FixedStepClockSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		TickCount = snapshot.TickCount;
		PendingSeconds = snapshot.PendingSeconds;
		DroppedSeconds = snapshot.DroppedSeconds;
	}
}
