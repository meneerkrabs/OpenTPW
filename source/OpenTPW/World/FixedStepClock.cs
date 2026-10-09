namespace OpenTPW;

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
}
