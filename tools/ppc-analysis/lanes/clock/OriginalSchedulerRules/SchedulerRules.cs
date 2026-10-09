namespace OpenTPW.Evidence.Clock;

public readonly record struct SchedulerState(
	uint PreviousScheduledMilliseconds, uint SubstepPhase, uint ScriptPassCounter, uint WorldTickCounter );

/// <summary>
/// ClockWord is the observed selected clock, after any scale/pause wrapper.
/// Flag names preserve binary masks; their complete runtime meanings are unresolved.
/// NumericMode is the literal selector: 0 suppresses ticks, 1 uses a wrapper, 2 advances directly.
/// </summary>
public readonly record struct SchedulerInput(
	uint ClockWord, bool ApplicationFlag8Set, bool GameplayFlag1Set, int NumericMode );

public enum UnsupportedBoundary
{
	None,
	NumericMode,
	SignedTimestampBoundary,
	ScheduledTimestampWrap
}

public readonly record struct SchedulerAdvance(
	SchedulerState State, int Substeps, int ScriptManagerPasses, int EligibleWorldPhases,
	int WorldTicks, int DroppedWorldPhases, int ModeSuppressedWorldPhases,
	uint DroppedClockMilliseconds, UnsupportedBoundary UndefinedBoundary )
{
	public bool IsDefined => UndefinedBoundary == UnsupportedBoundary.None;
}

/// <summary>
/// Pure finite scheduler slice from code:0x1c22b4–0x1c24cc and 0x105398–0x1053a0.
/// Unsupported timestamp boundaries return the original state, never guessed work.
/// Per-callback turn work begins at zero; caller binding must establish the normal reset path.
/// </summary>
public static class SchedulerRules
{
	public const uint SubstepMilliseconds = 31;
	public const uint MaximumBacklogMilliseconds = 2000;
	public const int WorldPhasePeriod = 8;
	public const int MaximumWorldPhasesPerCallback = 3;

	public static SchedulerAdvance Advance( SchedulerState state, SchedulerInput input )
	{
		if ( input.NumericMode is < 0 or > 2 )
			return Undefined( state, UnsupportedBoundary.NumericMode );
		var previous = state.PreviousScheduledMilliseconds;
		var now = input.ClockWord;
		if ( ((now ^ previous) & 0x80000000u) != 0 )
			return Undefined( state, UnsupportedBoundary.SignedTimestampBoundary );

		var delta = unchecked((int)(now - previous));
		uint dropped = 0;
		if ( delta > MaximumBacklogMilliseconds )
		{
			dropped = (uint)delta - MaximumBacklogMilliseconds;
			previous = unchecked(now - MaximumBacklogMilliseconds);
		}
		if ( unchecked((int)now) <= unchecked((int)previous) )
			return new( state, 0, 0, 0, 0, 0, 0, dropped, UnsupportedBoundary.None );

		var gap = (ulong)now - previous;
		var steps = (int)((gap + SubstepMilliseconds - 1) / SubstepMilliseconds);
		var nextScheduled = (ulong)previous + (uint)steps * SubstepMilliseconds;
		if ( nextScheduled > uint.MaxValue )
			return Undefined( state, UnsupportedBoundary.ScheduledTimestampWrap );
		if ( (((uint)nextScheduled ^ previous) & 0x80000000u) != 0 )
			return Undefined( state, UnsupportedBoundary.SignedTimestampBoundary );

		var phase = state.SubstepPhase;
		var pass = state.ScriptPassCounter;
		var world = state.WorldTickCounter;
		var scriptPasses = 0;
		var eligible = 0;
		var ticks = 0;
		var droppedPhases = 0;
		var suppressedByMode = 0;
		var worldAllowed = !input.ApplicationFlag8Set && !input.GameplayFlag1Set;
		for ( var step = 0; step < steps; ++step )
		{
			phase = unchecked(phase + 1);
			if ( !worldAllowed )
				continue;
			pass = unchecked(pass + 1);
			++scriptPasses;
			if ( (phase & (WorldPhasePeriod - 1)) != 0 )
				continue;
			++eligible;
			if ( eligible > MaximumWorldPhasesPerCallback )
			{
				++droppedPhases;
				continue;
			}
			if ( input.NumericMode == 0 )
			{
				++suppressedByMode;
				continue;
			}
			world = unchecked(world + 1);
			++ticks;
		}
		return new( new( (uint)nextScheduled, phase, pass, world ), steps, scriptPasses,
			eligible, ticks, droppedPhases, suppressedByMode, dropped, UnsupportedBoundary.None );
	}

	/// <summary>Manager counter increments before this test; script flag +184 bypasses it.</summary>
	public static bool IsOrdinaryScriptEligible( uint scriptId, uint managerPassCounter ) =>
		(scriptId & 7) == (managerPassCounter & 7);

	private static SchedulerAdvance Undefined( SchedulerState state, UnsupportedBoundary boundary ) =>
		new( state, 0, 0, 0, 0, 0, 0, 0, boundary );
}
