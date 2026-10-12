namespace OpenTPW;

/// <summary>The five staff messages the original has for each role (TAG_SYSTEM.str 38–62, five roles per kind).</summary>
public enum StrikeMessageKind
{
	/// <summary>"Your mechanics aren't very happy … threatening to go on strike" (38–42).</summary>
	Warning = 0,
	/// <summary>"Remember, your mechanics will strike at the end of this month" (43–47). The month routine never sends it.</summary>
	Reminder = 1,
	/// <summary>"Your mechanics have walked off the job … they're on strike" (48–52).</summary>
	Strike = 2,
	/// <summary>"… pleased that things have improved and have decided not to strike" (53–57).</summary>
	CalledOff = 3,
	/// <summary>"Your mechanics' strike is over" (58–62). The month routine never sends it.</summary>
	Over = 4
}

/// <summary>A strike message for one role.</summary>
public readonly record struct StrikeMessage( StaffType Role, StrikeMessageKind Kind )
{
	/// <summary>
	/// The message's index in <c>TAG_SYSTEM.str</c>: 38 + 5 × kind + the role's position in the file (mechanics, cleaners, guards,
	/// entertainers, scientists). The original builds the message from the role and the kind (0x100116590); the order of the five texts is
	/// read from their wording.
	/// </summary>
	public int TagSystemIndex => 38 + 5 * (int)Kind + RoleOrder[(int)Role];

	// Handyman, Mechanic, Entertainer, Guard, Researcher -> position inside each group of five
	private static readonly int[] RoleOrder = { 1, 0, 3, 2, 4 };
}

/// <summary>A handyman's two analyser sums (0x100C21F4 and 0x100C2264, both called with (park, 1)); their meaning is not traced.</summary>
public readonly record struct LitterStatistic( int Denominator, int Numerator );

/// <summary>
/// Staff strikes (ECON-014, docs/reverse/ORACLE.md F22), one level per role. Once a month (the original's month-end message, 0x100F7E18)
/// a role with staff is considered: an active strike simply ends; otherwise, if the park is old enough and the role has a grievance, the
/// level climbs 0 → 1 (a warning), then 2, 3 and 4 (a strike each time, ended at the next month end) and stays at 4; without a grievance the
/// level drops to 0 (a "called off" message from level 1). The oracle target <c>staff-strike-month</c> executes the routine.
/// </summary>
public sealed class StaffStrikeBoard
{
	public const int Roles = BalanceSettings.StaffTypeCount;
	/// <summary>A role needs more than three members to have a grievance.</summary>
	public const int MinimumMembers = 4;
	/// <summary>An average of the truncated vitals below this is a grievance.</summary>
	public const int GrievanceBelow = 15;
	/// <summary>The escalation waits for the clock stamp to reach this many 30-day units (0x100F8058).</summary>
	public const int EscalationUnits = 24;
	/// <summary>One 30-day unit in 100 ns stamp units (the divisor 6034 &lt;&lt; 32 | 0xF8648000 of 0x100F8050..0x100F8054).</summary>
	public const long UnitStamp = 25_920_000_000_000L;
	/// <summary>Stamp units per second (a TbTimeStamp counts 100 ns).</summary>
	public const long StampUnitsPerSecond = 10_000_000L;

	private readonly int[] levels = new int[Roles];
	private readonly bool[] active = new bool[Roles];
	private readonly uint[] lastConsidered = new uint[Roles];

	/// <summary>The warning/strike level of a role, 0..4.</summary>
	public int Level( StaffType role ) => levels[(int)role];

	/// <summary>True while a role's strike is active (the manager's flag at +40 + 12 × role).</summary>
	public bool IsActive( StaffType role ) => active[(int)role];

	/// <summary>The park turn a role was last considered at (the original compares it with the current turn and skips a second look).</summary>
	public uint LastConsidered( StaffType role ) => lastConsidered[(int)role];

	/// <summary>Ends a role's strike (0x100F8BD0): a striking member that sees a closed, empty park does this.</summary>
	public void End( StaffType role ) => active[(int)role] = false;

	/// <summary>Sets the five roles' records (a loaded save, tests).</summary>
	public void Restore( IReadOnlyList<int> savedLevels, IReadOnlyList<bool> savedActive, IReadOnlyList<uint> savedTurns )
	{
		for ( var role = 0; role < Roles; role++ )
		{
			levels[role] = savedLevels[role];
			active[role] = savedActive[role];
			lastConsidered[role] = savedTurns[role];
		}
	}

	/// <summary>Whether the role has a grievance (0x100F8410).</summary>
	/// <param name="role">Which role's chain of staff <paramref name="members"/> is.</param>
	/// <param name="members">The role's staff.</param>
	/// <param name="forced">The manager's forced-strike word (a debug flag) is set: a grievance without looking.</param>
	/// <param name="litter">Handymen only: the two analyser sums; a quotient above 0.2f is a grievance (null: not known).</param>
	// [BIN:STP-PPC:0x100F8410 strike test] more than three members whose truncated low-byte energies (+504) average below 15 (unsigned floor of the sum over the count), or whose happinesses (+500) do; handymen first test the analyser ratio above the single 0.2
	// Approximation (ECON-014, registered at ParkEconomy.UpdateStaffVitals): the handymen's two analyser statistics (0x100C21F4 / 0x100C2264, a sum over 1024 history records each) are not modelled: OpenTPW passes none, so the litter grievance never applies
	public static bool Grievance( StaffType role, IReadOnlyList<StaffVital> members, bool forced = false, LitterStatistic? litter = null )
	{
		if ( forced )
			return true;
		if ( role == StaffType.Handyman && litter is { } statistic && LitterGrievance( statistic ) )
			return true;
		return Unhappy( members, member => member.Energy ) || Unhappy( members, member => member.Happiness );
	}

	private static bool LitterGrievance( LitterStatistic statistic )
	{
		// single precision: both sums converted to single, divided as singles; 0 / 0 is NaN (not above), x / 0 is infinite
		var quotient = (float)statistic.Numerator / (float)statistic.Denominator;
		return quotient > 0.2f;
	}

	private static bool Unhappy( IReadOnlyList<StaffVital> members, Func<StaffVital, float> vital )
	{
		if ( members.Count < MinimumMembers )
			return false;
		var sum = 0u;
		foreach ( var member in members )
			sum = unchecked( sum + (uint)StaffVitals.Byte( vital( member ) ) );
		return sum / (uint)members.Count < GrievanceBelow;
	}

	/// <summary>
	/// The month-end consideration (0x100F7E18 and the escalation 0x100F7FE0): the strike messages it sends.
	/// </summary>
	/// <param name="turn">The park turn (a role is not considered twice in one turn).</param>
	/// <param name="forced">The manager's forced-strike word: no park gate, a grievance everywhere.</param>
	/// <param name="parkOpen">The park is open.</param>
	/// <param name="guestsInPark">Guests standing on park cells (0x100C1FB4(park, 5)); any guest skips the month.</param>
	/// <param name="stampUnits">The clock's time stamp in 100 ns units, divided by 30 days it must reach <see cref="EscalationUnits"/>.</param>
	/// <param name="roster">A role's staff, with their vitals.</param>
	/// <param name="litter">The handymen's analyser sums, when known.</param>
	// [BIN:STP-PPC:0x100F7E18 month consideration] skipped (debug text "park is closed") while the park is closed (word 0x1DA710) or 0x100C1FB4(park, 5) counts a guest, unless the forced word is set; then per role with staff not yet considered this turn: an active strike ends, else the escalation runs
	// [BIN:STP-PPC:0x100F7FE0 strike escalation] stamp / 25.92e12 (signed low word) below 24 ends it; a grievance: level 0 → 1 with the warning (kind 0), 1 → 2, 2 → 3, 3 → 4, 4 → 4 each with the active flag and the strike message (kind 2); no grievance: level 1 sends "called off" (kind 3), the level and the flag clear
	// Approximation (ECON-014, registered at ParkEconomy.UpdateStaffVitals): the clock stamp (0x100E4628) goes through imports whose epoch is not traced: if it counts from an absolute epoch before 2000 the age test is always met; OpenTPW counts the park clock from its start, so a strike needs a park of 24 thirty-day months
	public IReadOnlyList<StrikeMessage> ConsiderMonth( long turn, bool forced, bool parkOpen, int guestsInPark, long stampUnits,
		Func<StaffType, IReadOnlyList<StaffVital>> roster, LitterStatistic? litter = null )
	{
		var messages = new List<StrikeMessage>();
		if ( !forced && (!parkOpen || guestsInPark != 0) )
			return messages;
		var turnWord = unchecked( (uint)turn );
		for ( var index = 0; index < Roles; index++ )
		{
			var role = (StaffType)index;
			var members = roster( role );
			if ( members.Count == 0 || lastConsidered[index] == turnWord )
				continue;
			lastConsidered[index] = turnWord;
			if ( active[index] )
			{
				active[index] = false;
				continue;
			}
			if ( unchecked( (int)(stampUnits / UnitStamp) ) < EscalationUnits )
				continue;
			if ( Grievance( role, members, forced, litter ) )
			{
				switch ( levels[index] )
				{
					case 0:
						levels[index] = 1;
						messages.Add( new StrikeMessage( role, StrikeMessageKind.Warning ) );
						break;
					case 1:
					case 2:
					case 3:
						levels[index]++;
						active[index] = true;
						messages.Add( new StrikeMessage( role, StrikeMessageKind.Strike ) );
						break;
					case 4:
						active[index] = true;
						messages.Add( new StrikeMessage( role, StrikeMessageKind.Strike ) );
						break;
				}
			}
			else
			{
				if ( levels[index] == 1 )
					messages.Add( new StrikeMessage( role, StrikeMessageKind.CalledOff ) );
				levels[index] = 0;
				active[index] = false;
			}
		}
		return messages;
	}
}
