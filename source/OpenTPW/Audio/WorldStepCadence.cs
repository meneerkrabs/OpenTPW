namespace OpenTPW;

/// <summary>
/// The original main loop's world-step gate for its sound updates: the loop runs one 31 ms step whenever the game timer is past the
/// next step's time (a backlog over 2000 ms is dropped), counts the steps, and on every 32nd step sends the music and crowd parameters.
/// <see cref="Advance"/> takes the game timer's elapsed milliseconds (0 while it is paused) and returns how many of those updates fell
/// due; the count starts at 0 when a park is entered.
/// </summary>
// [BIN:TPW-EXE:0x0054F493 world step loop] the step time [0x878C74] is held at most 2000 ms behind the timer [0x785970] (0x54F493..0x54F4A1), each pass while the timer is past it adds 31 (0x54F4C4) and raises the step counter [0x877D34] (0x54F4D3), which 0x54F443 zeroes when the loop starts
// [BIN:TPW-EXE:0x0054F82D sound updates] `test [0x877D34], 0x1F` gates the music parameter (code 4, 0x51E790) and the crowd update (0x51E7B0, the count within four cells capped at 100 at 0x54F89E) to the steps whose count is a multiple of 32
public sealed class WorldStepCadence
{
	public const int StepMilliseconds = 31;
	public const int StepsPerSoundUpdate = 32;
	public const int MaximumBacklogMilliseconds = 2000;

	// The timer's lead over the next step's time.
	private double lead;

	/// <summary>Steps run since the park was entered (the original's [0x877D34]).</summary>
	public long Steps { get; private set; }

	/// <summary>Starts counting again (a park is entered).</summary>
	public void Reset()
	{
		lead = 0;
		Steps = 0;
	}

	/// <summary>Runs the steps <paramref name="elapsedMilliseconds"/> of the game timer allow and returns how many sound updates fell due.</summary>
	public int Advance( double elapsedMilliseconds )
	{
		if ( !double.IsFinite( elapsedMilliseconds ) || elapsedMilliseconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( elapsedMilliseconds ) );
		lead = Math.Min( lead + elapsedMilliseconds, MaximumBacklogMilliseconds );
		var due = 0;
		while ( lead > 0 )
		{
			lead -= StepMilliseconds;
			Steps++;
			if ( Steps % StepsPerSoundUpdate == 0 )
				due++;
		}
		return due;
	}
}
