namespace OpenTPW;

public sealed partial class GuestSimulation
{
	// 60 Hz / 248 ms reduces to 25 turns per 372 fixed ticks. The display phase is retained across a restore (and is hashed state while a weather extension reads the age);
	// the host still owns AlignTurns and the unsaved gameplay turnPhase. Arbitrary clock resets do not invent historical age.
	internal const int DisplayTurnPhasePeriodTicks = 372;
	private int displayTurnPhaseTicks;

	/// <summary>Whether an extension that reads the integer turn age (the umbrella day gate, the rain mood, the winter cold) is at work; only then are the display phase and the ages part of the hashed state.</summary>
	private bool ReadsDisplayAge => umbrellaHolders > 0 || RainMood != null || WinterCold != null;

	/// <summary>Advance existing display ages before a turn's callbacks can create a new visitor at age zero.</summary>
	// [BIN:TPW-EXE:0x004fafdb creation stamp; 0x004fd950 age] one unsigned increment at each existing host turn boundary
	// telescopes to current world turn minus the creation/import stamp. The seconds and stay accounting are untouched; the umbrella day gate reads this age (UMB-003).
	private void AdvanceDisplayAges()
	{
		var previous = (TickCount - 1) % DisplayTurnPhasePeriodTicks + displayTurnPhaseTicks;
		if ( ParkCalendar.Turn( previous + 1 ) == ParkCalendar.Turn( previous ) )
			return;
		foreach ( var guest in guests )
			if ( CountsParkTime( guest ) && guest.TimeInParkSeconds != null && guest.DisplayTimeInParkTurns is { } turns )
				guest.DisplayTimeInParkTurns = unchecked((uint)turns + 1u);
	}

	/// <summary>Shared pure validation for standalone guest snapshots and the own-format world envelope.</summary>
	internal static IEnumerable<string> DisplayAgeProblems( GuestSimulationSnapshot snapshot )
	{
		if ( snapshot.DisplayTurnPhaseTicks is { } phase && (phase < 0 || phase >= DisplayTurnPhasePeriodTicks) )
			yield return $"Display turn phase {phase} is outside 0..{DisplayTurnPhasePeriodTicks - 1}.";
		if ( snapshot.Guests == null )
			yield break;
		foreach ( var guest in snapshot.Guests )
		{
			if ( guest?.DisplayTimeInParkTurns is not { } turns )
				continue;
			if ( turns < 0 || turns > uint.MaxValue )
				yield return $"Guest {guest.Id} display age {turns} is not an unsigned 32-bit turn count.";
			if ( guest.TimeInParkSeconds == null || snapshot.DisplayTurnPhaseTicks == null )
				yield return $"Guest {guest.Id} exact display age lacks its known duration or display clock phase.";
		}
	}
}
