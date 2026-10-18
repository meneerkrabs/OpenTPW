namespace OpenTPW;

/// <summary>The guest meters the needs update writes: happiness and the four needs, each 0..100 (<see cref="MeterMath"/>).</summary>
public readonly record struct GuestMeters( float Happiness, float Thirst, float Hunger, float Toilet, float Illness );

/// <summary>
/// The original's needs update of one park turn (<c>0xeece4</c>, docs/reverse/ORACLE.md F25, docs/GUESTS.md "Needs"). Everything in it is gated on the
/// guest's phase: only on a park turn whose low two bits equal those of the guest's thing id does the guest's exit counter lose 1, its cell's environment
/// words change happiness, illness and hunger, and (on a turn that is also a multiple of 16) toilet grow by 1 and hunger and thirst by 2. A guest with a
/// need at exactly 100 pays 1 happiness per such need. The meters therefore do not rise on a per-second rate: a guest of phase 0 (one in four) gets
/// hungry at 0.5 and thirsty at 0.5 per second, the others change only by the environment and by what they use.
/// </summary>
public static class NeedsUpdate
{
	/// <summary>The needs block runs when <c>turn &amp; 3 == id &amp; 3</c> (<c>0xeed34..0xeed40</c>).</summary>
	public const int PhaseMask = 3;
	/// <summary>The fixed growth runs when the turn is also a multiple of 16 (<c>0xeeebc</c>), which is only ever a guest of phase 0.</summary>
	public const int GrowthMask = 15;
	/// <summary>The toilet's growth per fixed step (the float at TOC −10812).</summary>
	public const float ToiletGrowth = 1f;
	/// <summary>Thirst's and hunger's growth per fixed step (the float at TOC −10828).</summary>
	public const float ThirstHungerGrowth = 2f;
	/// <summary>The happiness cost of each need whose truncated byte is exactly 100 (the float at TOC −10832).</summary>
	public const float FullNeedPenalty = 1f;
	/// <summary>A 0 is shifted into the temporary history on every park turn that is a multiple of this (<c>0xea2d4..0xea2e0</c>).</summary>
	public const int HistoryPeriod = 20;

	/// <summary>Whether the guest's needs block runs on this turn (<c>0xeed24..0xeed40</c>): the low two bits of the turn and of the thing id agree.</summary>
	// [BIN:STP-PPC:0x100EED24 needs phase] cmplw of (id & 3) and (game +0x1da70c & 3); the id is the 16-bit thing id (0xfa9a4)
	// [BIN:TPW-EXE:0x00501676 needs phase] the tick's low two bits are compared with the low byte of the thing id (0x0050B350) taken from the pool (0x00516270); the guest's id is its thing id (ThingPool)
	public static bool IsPhase( uint turn, int guestId ) => (guestId & PhaseMask) == (int)(turn & PhaseMask);

	/// <summary>
	/// How many fixed growth steps (<see cref="GrowthMask"/>) the needs block of guest <paramref name="guestId"/> takes on the park turns <c>fromTurn + 1 .. fromTurn + turns</c>: the growth runs on a turn that is a
	/// multiple of 16 and on the guest's phase (<see cref="IsPhase"/>), and a multiple of 16 has its low two bits at 0, so only a guest whose thing id is a multiple of 4 ever grows; the others change only by what they
	/// use. Each step adds <see cref="ToiletGrowth"/> to toilet and <see cref="ThirstHungerGrowth"/> to hunger and thirst. Pure arithmetic over <see cref="Apply"/>'s own rule, used by the smarter guests' planning
	/// (docs/SMARTER-GUESTS.md, phase 5) to look ahead; it changes nothing.
	/// </summary>
	public static int GrowthSteps( int guestId, long fromTurn, long turns )
	{
		if ( turns <= 0 || (guestId & PhaseMask) != 0 )
			return 0;
		// floor division, so a turn before 0 (a park that has not started) counts its multiples of 16 as the others do
		static long Floor( long value ) => value >= 0 ? value / (GrowthMask + 1) : -((-value + GrowthMask) / (GrowthMask + 1));
		return (int)Math.Min( int.MaxValue, Math.Max( 0, Floor( fromTurn + turns ) - Floor( fromTurn ) ) );
	}

	/// <summary>
	/// The result of a needs update: the meters, the exit counter and the toilet speed term (<c>+194</c>, 25 or 0), which is rewritten only on the guest's phase
	/// (null otherwise).
	/// </summary>
	public readonly record struct Result( GuestMeters Meters, int ExitLevel, int? ToiletSpeedTerm );

	/// <summary>
	/// One park turn of a guest. <paramref name="cellHappiness"/>, <paramref name="cellIllness"/> and <paramref name="cellHunger"/> are the signed 16-bit words
	/// at offsets 0, 2 and 4 of the environment record of the guest's cell (entertainers, vomit, toilets and the like); OpenTPW has no such grid and passes 0
	/// (GUEST-006).
	/// </summary>
	// [BIN:STP-PPC:0x100EECE4 needs update] executed by the PowerPC oracle (guest-needs-turn, 529 inputs; C# agrees): order exit counter, cell words (happiness, illness, hunger), growth (toilet, hunger, thirst), full-need penalties (illness, hunger, thirst, toilet), toilet speed term
	public static Result Apply( GuestMeters meters, int exitLevel, uint turn, int guestId, short cellHappiness = 0, short cellIllness = 0, short cellHunger = 0 )
	{
		if ( !IsPhase( turn, guestId ) )
			return new Result( meters, exitLevel, null );
		exitLevel = unchecked( exitLevel - 1 );
		var happiness = MeterMath.Add( meters.Happiness, cellHappiness );
		var illness = MeterMath.Add( meters.Illness, cellIllness );
		var hunger = MeterMath.Add( meters.Hunger, cellHunger );
		var thirst = meters.Thirst;
		var toilet = meters.Toilet;
		if ( (turn & GrowthMask) == 0 )
		{
			toilet = MeterMath.Add( toilet, ToiletGrowth );
			hunger = MeterMath.Add( hunger, ThirstHungerGrowth );
			thirst = MeterMath.Add( thirst, ThirstHungerGrowth );
		}
		foreach ( var need in new[] { illness, hunger, thirst, toilet } )
		{
			if ( MeterMath.Byte( need ) == 100 )
				happiness = MeterMath.Add( happiness, -FullNeedPenalty );
		}
		var speedTerm = MeterMath.Byte( toilet ) > GuestSpeed.ToiletBonusLevel ? GuestSpeed.ToiletBonus : 0;
		return new Result( new GuestMeters( happiness, thirst, hunger, toilet, illness ), exitLevel, speedTerm );
	}

	/// <summary>
	/// The temporary history's decay (<c>0xea2b8</c>, called at the end of every guest's needs function, outside the phase): on a turn that is a multiple of 20
	/// a 0 is shifted in, so a refusal is forgotten after four such turns.
	/// </summary>
	public static void DecayHistory( int[] temporaryHistory, uint turn )
	{
		if ( turn % HistoryPeriod != 0 )
			return;
		for ( var slot = temporaryHistory.Length - 1; slot > 0; slot-- )
			temporaryHistory[slot] = temporaryHistory[slot - 1];
		temporaryHistory[0] = 0;
	}
}
