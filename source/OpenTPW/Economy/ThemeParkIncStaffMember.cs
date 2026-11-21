namespace OpenTPW;

/// <summary>
/// A hired Theme Park Inc staff member's economy state (docs/reverse/TPI-STAFF.md): experience, salary and agency instead of Theme Park World's grade, the three meters,
/// and the wage shortfall and wage-demand day the wage review keeps.
/// </summary>
// [BIN:TPI-EXE:0x626190 staff hire] the member constructor copies the candidate's salary to +0x214, its experience to +0x210 and its agency to +0x244 (0x6261DA..0x626205); both
// constructors end in 0x626390, which sets the meters +0x230 (happiness), +0x234 (motivation) and +0x238 (energy) to 100.0 (0x6263D6..0x6263E7)
public sealed class ThemeParkIncStaffMember
{
	public ThemeParkIncStaffMember( int id, ThemeParkIncStaffRole role, int nameIndex, int experience, int salary, int agency, uint spriteDraw )
	{
		Id = id;
		Role = role;
		NameIndex = nameIndex;
		Experience = experience;
		Salary = salary;
		Agency = agency;
		SpriteDraw = spriteDraw;
	}

	public int Id { get; }
	public ThemeParkIncStaffRole Role { get; }
	public int NameIndex { get; }
	public uint SpriteDraw { get; }
	/// <summary><c>+0x210</c>.</summary>
	public int Experience { get; internal set; }
	/// <summary><c>mSalary</c> (+0x214), the wage paid every month.</summary>
	public int Salary { get; internal set; }
	/// <summary>The staff agency (+0x244), a row of <c>StaffAgencies</c>.</summary>
	public int Agency { get; }
	/// <summary><c>+0x230</c>.</summary>
	public float Happiness { get; internal set; } = 100f;
	/// <summary><c>+0x234</c>.</summary>
	public float Motivation { get; internal set; } = 100f;
	/// <summary><c>+0x238</c>.</summary>
	public float Energy { get; internal set; } = 100f;
	/// <summary>The expected wage less the salary after the last review that found the salary short (+0x270), 0 otherwise.</summary>
	public int WageShortfall { get; internal set; }
	/// <summary>The day of the month the member will demand a rise on (+0x26C), 0 for none.</summary>
	public int WageDemandDay { get; internal set; }

	/// <summary>Hires <paramref name="candidate"/>: its role, name, experience, wage as the salary, agency and look.</summary>
	public static ThemeParkIncStaffMember Hire( ThemeParkIncStaffCandidate candidate, int id ) =>
		new( id, candidate.Role, candidate.NameIndex, candidate.Experience, candidate.Wage, candidate.Agency, candidate.SpriteDraw );

	/// <summary>A meter after adding <paramref name="value"/>: above 100 it is 100, below 0 it is 0 (the x87 sum stored as a single).</summary>
	// [BIN:TPI-EXE:0x62CED1 meter clamp] fcom 100.0 (0x93AC24) with C0|C3 clear stores 100.0, fcom 0.0 (0x93A038) with C0 set stores 0, else the sum is stored as a single
	private static float Clamp( double sum ) => sum > 100.0 ? 100f : sum < 0.0 ? 0f : (float)sum;

	/// <summary>
	/// The wage review after each payday and salary change: a salary below the expected wage costs motivation and happiness and records the shortfall (and, at low
	/// motivation, a wage-demand day); a fair salary raises both.
	/// </summary>
	// [BIN:TPI-EXE:0x62CDA0 wage review] expected = ExpectedWage(exp, MaxWage of the kind's role, 0 for another kind; inline 0x62CE35..0x62CE7A) plus the raw InitialWageModifier of the
	// agency (0x62CE88). Salary below it (signed): x = UnHappyWageVal × (MotivationLossModifier + 1), or UnHappyWageVal when either is 0; motivation += -(x / 2) (toward zero),
	// happiness += x × -0.25f, both clamped; shortfall = expected - salary. Otherwise shortfall and demand day = 0, y = HappyWageVal × (MotivationGainModifier + 1) (or HappyWageVal),
	// motivation += y and happiness += y, clamped. Then, with a shortfall and trunc(motivation) & 0xFF at most LowestWageHappiness, the demand day is rand() % 27 + 1 (0x62D016..0x62D046)
	// [EXT:tpi-staff-crt-rand] user-approved 2026-11-21: the demand day's rand() (0x870D38, seeded from the clock by srand(GetTickCount()) at 0x6A151B) is drawn from the park's world generator
	public void ReviewWage( ThemeParkIncStaffSettings settings, WorldRng world )
	{
		var expected = settings.ExpectedWage( Experience, settings.Roles[Role].MaxWage );
		var agency = Agency >= 0 && Agency < settings.Agencies.Count ? settings.Agencies[Agency] : null;
		expected = unchecked( expected + (agency?.InitialWageModifier ?? 0) );
		if ( Salary < expected )
		{
			var x = settings.UnhappyWageValue;
			if ( agency is { MotivationLossModifier: not 0 } && x != 0 )
				x = unchecked( x * (agency.MotivationLossModifier + 1) );
			Motivation = Clamp( (double)-(x / 2) + Motivation );
			Happiness = Clamp( (double)x * -0.25f + Happiness );
			WageShortfall = unchecked( expected - Salary );
		}
		else
		{
			WageShortfall = 0;
			WageDemandDay = 0;
			var y = settings.HappyWageValue;
			if ( agency is { MotivationGainModifier: not 0 } && y != 0 )
				y = unchecked( (agency.MotivationGainModifier + 1) * y );
			Motivation = Clamp( (double)y + Motivation );
			Happiness = Clamp( (double)y + Happiness );
		}
		if ( WageShortfall != 0 && ((int)Motivation & 0xFF) <= settings.LowestWageHappiness )
			WageDemandDay = (int)world.Next( 27u ) + 1;
	}

	/// <summary>The display states a member's day counts (STAFFSTATES.str rows): the seven tick counters +0x280..+0x298.</summary>
	public enum DisplayState { Idle = 0, LookingForWork = 1, Working = 2, Resting = 3, OnStrike = 4, PickedUp = 5, Researching = 7, Training = 8 }

	private readonly uint[] dayCounters = new uint[9];

	/// <summary>The day's tick count in a display state.</summary>
	public uint DayTicks( DisplayState state ) => dayCounters[(int)state];

	/// <summary>One thing update's count of the member's display state.</summary>
	// [BIN:TPI-EXE:0x628050 day counters] once per update, by the display state (0x62B010, table 0x628114): idle +0x28C, looking for work +0x284, working +0x280, resting +0x288,
	// on strike +0x298, researching +0x294, training +0x290; picked up and the others count nothing
	public void CountTick( DisplayState state )
	{
		if ( state is not DisplayState.PickedUp )
			dayCounters[(int)state]++;
	}

	/// <summary>The daily experience gain from the share of the day spent working or researching, then the day's counters start again.</summary>
	// [BIN:TPI-EXE:0x629650 experience gain] f = (working + researching) / (all seven counters), rounded to a single (NaN for an empty day gives 0); gain = trunc(ExperiencePerMonth[0] × f)
	// (the gardener takes the handyman's); with the agency's ExperienceGainModifier and a gain both nonzero, gain = trunc(modifier × f + gain); experience += gain and an unsigned
	// experience at or above MaxExperience becomes MaxExperience - 1; [BIN:TPI-EXE:0x62D1A0 day reset] the next call of the day handler stores the counters' history rings and
	// clears the seven (the history rings, kept for the staff graphs, are not ported)
	// [BIN:TPI-EXE:0x5BCD60 precision] the x87 sums are read at 53 bits, as the economy's other Theme Park Inc routines
	public void EndDay( ThemeParkIncStaffSettings settings )
	{
		var working = unchecked( dayCounters[(int)DisplayState.Working] + dayCounters[(int)DisplayState.Researching] );
		var all = unchecked( dayCounters[(int)DisplayState.OnStrike] + dayCounters[(int)DisplayState.Training] + dayCounters[(int)DisplayState.Idle]
			+ dayCounters[(int)DisplayState.Resting] + dayCounters[(int)DisplayState.LookingForWork] + working );
		var share = all == 0 ? float.NaN : (float)((double)working / all);
		var gain = float.IsNaN( share ) ? 0 : (int)(settings.Roles[Role].ExperiencePerMonth * (double)share);
		var modifier = Agency >= 0 && Agency < settings.Agencies.Count ? settings.Agencies[Agency].ExperienceGainModifier : 0;
		if ( modifier != 0 && gain != 0 )
			gain = (int)(modifier * (double)share + gain);
		Experience = unchecked( Experience + gain );
		if ( (uint)Experience >= (uint)settings.MaxExperience )
			Experience = settings.MaxExperience - 1;
		Array.Clear( dayCounters );
	}

	/// <summary>The meters' cost of a step walked or worked: less with more experience.</summary>
	// [BIN:TPI-EXE:0x62B3E0 staff step] moving and working (0x62B4D0) are identical: d = 1 - experience (unsigned) / MaxExperience; energy += d × -0.1f, happiness += d × -0.05f, each
	// clamped to 0..100 (strict) and stored as a single; no agency modifier applies
	public void Step( ThemeParkIncStaffSettings settings )
	{
		var d = 1.0 - (double)(uint)Experience / settings.MaxExperience;
		Energy = Clamp( d * -0.1f + Energy );
		Happiness = Clamp( d * -0.05f + Happiness );
	}

	/// <summary>One update of rest: energy, and happiness and motivation alike, recover faster with more experience; true when energy is full and the rest ends.</summary>
	// [BIN:TPI-EXE:0x62A1C0 rest] f = experience (unsigned) / MaxExperience; e = MinRecuperationRate + f, kept as a single, plus e × RecuperationModifier × 0.01f when both are nonzero,
	// at most MaxRecuperationRate (the unrounded sum compared, strict); h = f × 10.0f × MinHappinessRecuperationRate plus h × HappinessRecuperationModifier × 0.01f, at most
	// MaxHappinessRecuperationRate; energy += e, happiness and motivation += h, clamped; the rest ends (0x62C230) when the energy byte is 100
	public bool Rest( ThemeParkIncStaffSettings settings )
	{
		var agency = Agency >= 0 && Agency < settings.Agencies.Count ? settings.Agencies[Agency] : null;
		var f = (double)(uint)Experience / settings.MaxExperience;
		var e = settings.MinRecuperationRate + f;
		var single = (float)e;
		if ( agency is { RecuperationModifier: not 0 } && e != 0.0 )
		{
			e += agency.RecuperationModifier * e * 0.01f;
			single = (float)e;
		}
		if ( e > settings.MaxRecuperationRate )
			single = settings.MaxRecuperationRate;
		var h = f * 10.0f * settings.MinHappinessRecuperationRate;
		if ( agency is { HappinessRecuperationModifier: not 0 } && h != 0.0 )
			h += agency.HappinessRecuperationModifier * h * 0.01f;
		if ( h > settings.MaxHappinessRecuperationRate )
			h = settings.MaxHappinessRecuperationRate;
		Energy = Clamp( (double)single + Energy );
		Happiness = Clamp( h + Happiness );
		Motivation = Clamp( h + Motivation );
		return ((int)Energy & 0xFF) == 100;
	}

	/// <summary>Whether the member is too tired to go on: its energy byte below RestLevel.</summary>
	// [BIN:TPI-EXE:0x62B3B0 tired] (u8)trunc(energy) < RestLevel, signed
	public bool Tired( ThemeParkIncStaffSettings settings ) => ((int)Energy & 0xFF) < settings.RestLevel;

	/// <summary>Sets the salary (at least MinWage) and reviews the wage when it changed.</summary>
	// [BIN:TPI-EXE:0x62D160 salary setter] the new salary is clamped to MinWage and the wage review runs only when the salary changed
	public void SetSalary( ThemeParkIncStaffSettings settings, int salary, WorldRng world )
	{
		var clamped = Math.Max( salary, settings.MinWage );
		if ( clamped == Salary )
			return;
		Salary = clamped;
		ReviewWage( settings, world );
	}
}
