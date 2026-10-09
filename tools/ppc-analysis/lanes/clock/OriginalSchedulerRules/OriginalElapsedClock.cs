namespace OpenTPW.Evidence.Clock;

public enum ClockControl
{
	TogglePause,
	Faster,
	Slower
}

/// <summary>Caller must supply the input edge and the unresolved host-state binding explicitly.</summary>
public readonly record struct ClockControlInput(
	ClockControl Control, uint RawMilliseconds, bool IsRelease, bool PauseHostStateIsOne );

/// <summary>
/// Finite arithmetic contract for the identified Mac elapsed-clock source and pause wrapper.
/// This is standalone evidence code. No platform clock, UI, or live game is accessed.
/// </summary>
public sealed class OriginalElapsedClock
{
	private uint previousRaw;
	private uint pauseStart;
	private uint pauseOffset;

	public double AccumulatedMilliseconds { get; private set; }
	public double Scale { get; private set; } = 1;
	public bool IsPaused { get; private set; }

	public OriginalElapsedClock( uint initialRawMilliseconds, double initialAccumulatedMilliseconds = 0 )
	{
		RequireFinite( initialAccumulatedMilliseconds );
		previousRaw = initialRawMilliseconds;
		AccumulatedMilliseconds = initialAccumulatedMilliseconds;
	}

	/// <summary>code:0x1c3fbc, qualified for finite input only.</summary>
	public static uint SaturatingWord( double milliseconds )
	{
		RequireFinite( milliseconds );
		if ( milliseconds < 0 )
			return 0;
		if ( milliseconds >= 4294967296d )
			return uint.MaxValue;
		return (uint)Math.Truncate( milliseconds );
	}

	/// <summary>
	/// code:0x127cd0 and 0x117d74. Raw subtraction wraps; the source's returned word saturates.
	/// A paused read returns the stored elapsed word and does not sample the source.
	/// </summary>
	public uint Sample( uint rawMilliseconds ) => IsPaused
		? unchecked(pauseStart - pauseOffset)
		: unchecked(SampleSource( rawMilliseconds ) - pauseOffset);

	public void Pause( uint rawMilliseconds )
	{
		if ( IsPaused )
			return;
		pauseStart = SampleSource( rawMilliseconds );
		IsPaused = true;
	}

	public void Resume( uint rawMilliseconds )
	{
		if ( !IsPaused )
			return;
		pauseOffset = unchecked(pauseOffset + SampleSource( rawMilliseconds ) - pauseStart);
		IsPaused = false;
	}

	/// <summary>
	/// Key records invoke these actions on release. Pause additionally requires host field +60 == 1.
	/// Scale changes do not sample raw time first: the next source delta uses the new scale.
	/// </summary>
	public bool ApplyControl( ClockControlInput input )
	{
		if ( !input.IsRelease )
			return false;
		switch ( input.Control )
		{
			case ClockControl.Faster:
				Scale = Math.Clamp( Scale * 1.25, 0.25, 2 );
				return true;
			case ClockControl.Slower:
				Scale = Math.Clamp( Scale / 1.25, 0.25, 2 );
				return true;
			case ClockControl.TogglePause when input.PauseHostStateIsOne:
				if ( IsPaused )
					Resume( input.RawMilliseconds );
				else
					Pause( input.RawMilliseconds );
				return true;
			case ClockControl.TogglePause:
				return false;
			default:
				throw new ArgumentOutOfRangeException( nameof( input ) );
		}
	}

	private uint SampleSource( uint rawMilliseconds )
	{
		var delta = unchecked(rawMilliseconds - previousRaw);
		AccumulatedMilliseconds = Math.FusedMultiplyAdd( delta, Scale, AccumulatedMilliseconds );
		previousRaw = rawMilliseconds;
		return SaturatingWord( AccumulatedMilliseconds );
	}

	private static void RequireFinite( double value )
	{
		if ( !double.IsFinite( value ) )
			throw new ArgumentOutOfRangeException( nameof( value ), "FP exceptions and nonfinite values are outside this contract." );
	}
}
