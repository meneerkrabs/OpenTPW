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
	/// <summary>The sprite draw the variant comes from; a guard's constructor replaces the candidate's with its own world draw (0x6902F0).</summary>
	public uint SpriteDraw { get; internal set; }
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
	/// <summary>The trained ability (+0x218, <c>mKnownAbilities</c>; ABILITIES.str): 0 for none, one at a time.</summary>
	public int Ability { get; internal set; }
	/// <summary>The ability being learnt (+0x220, <c>mCurrentlyLearning</c>), 0 for none.</summary>
	public int LearningAbility { get; internal set; }
	/// <summary>The training progress (+0x224, <c>mSAProgress</c>).</summary>
	public int TrainingProgress { get; internal set; }
	/// <summary>The ability chosen to learn (+0x22C, <c>mGoingToLearn</c>), which entering training (state 8) takes.</summary>
	public int ChosenAbility { get; internal set; }
	/// <summary>The staff HQ obstacle slot the member fills (+0x228, <c>mSlotLocation</c>), -1 for none.</summary>
	public int HqSlot { get; internal set; } = -1;
	/// <summary>Listed in a research lab team (+0x274, <c>mResearching</c>): a researcher in a research room shows "Researching" only then.</summary>
	public bool Listed { get; internal set; }
	/// <summary>The research room the researcher walks to or works in (+0x27C, <c>mRRoom</c>), 0 for none.</summary>
	public int ResearchRoom { get; internal set; }
	/// <summary>The turn the researcher entered his research room (+0x278, <c>mTimeStartedResearching</c>).</summary>
	public long ResearchStamp { get; internal set; }
	/// <summary>The rest area the member walks to, waits by or rests in (+0x248): its thing, 0 for none.</summary>
	public int RestArea { get; internal set; }

	/// <summary>Hires <paramref name="candidate"/>: its role, name, experience, wage as the salary, agency and look.</summary>
	public static ThemeParkIncStaffMember Hire( ThemeParkIncHireCandidate candidate, int id ) =>
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

	/// <summary>The nine day counters, for a save.</summary>
	internal uint[] CaptureDayCounters() => (uint[])dayCounters.Clone();

	/// <summary>Restores the day counters a save holds.</summary>
	internal void RestoreDayCounters( IReadOnlyList<uint> counters )
	{
		if ( counters.Count != dayCounters.Length )
			throw new InvalidDataException( "Invalid Theme Park Inc staff day counters." );
		for ( var index = 0; index < dayCounters.Length; index++ )
			dayCounters[index] = counters[index];
	}

	/// <summary>The day's tick count in a display state.</summary>
	public uint DayTicks( DisplayState state ) => dayCounters[(int)state];

	/// <summary>One thing update's count of the member's display state.</summary>
	// [BIN:TPI-EXE:0x628050 day counters] once per update, by the display state (0x62B010, table 0x628114): idle +0x28C, looking for work +0x284, working +0x280, resting +0x288,
	// on strike +0x298, researching +0x294, training +0x290; picked up and the others count nothing
	public void CountTick( DisplayState state )
	{
		if ( state is not DisplayState.PickedUp && (int)state != UncountedDisplayState )
			dayCounters[(int)state]++;
	}

	/// <summary>The display state 6, which the counter table <c>0x628114</c> sends to no counter (like picked up); OpenTPW names no state 6.</summary>
	private const int UncountedDisplayState = 6;

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

	/// <summary>Adds <paramref name="delta"/> to happiness, clamped to 0..100.</summary>
	// [BIN:TPI-EXE:0x60D0D0 meter add] field = clamp(field + delta, 0, 100), the x87 sum compared and stored as a single
	internal void AddHappiness( float delta ) => Happiness = Clamp( (double)delta + Happiness );

	/// <summary>One update of research in a room: energy and happiness fall by the step x = 1 − experience / MaxExperience, every x87 operation rounded to a single.</summary>
	// [BIN:TPI-EXE:0x6289BD research step] fildll the experience (+0x210, unsigned, zero-extended), fidivl MaxExperience (0xA02260), fsubrl 1.0, fstps x; flds x, fmuls -0.1f, fstps the argument of
	// 0x60D0D0(&energy): flds the argument, fadds energy, the sum compared with 100.0f and 0.0f (above 100 stores 100, below 0 stores 0, else fstps); flds x, fmuls -0.05f, fadds happiness (+0x230), the same
	// clamp and store
	// [BIN:TPI-EXE:0x6BF33F control word] the game thread's x87 runs at 0x7F (24-bit significand) after DDSCL_FPUSETUP (0x851 at 0x6BF33F, 0x808 at 0x6BF5A8; docs/DETERMINISM.md), so the fidivl, fsubrl,
	// fmuls and fadds each round to a single and the clamps compare the rounded sums
	internal void ResearchStep( ThemeParkIncStaffSettings settings )
	{
		var x = SingleOneMinusRatio( (uint)Experience, settings.MaxExperience );
		var energyDelta = (float)(x * -0.1f);
		Energy = ClampSingle( (float)(energyDelta + Energy) );
		var happinessDelta = (float)(x * -0.05f);
		Happiness = ClampSingle( (float)(happinessDelta + Happiness) );
	}

	/// <summary><c>fildll</c> <paramref name="numerator"/>, <c>fidivl</c> <paramref name="denominator"/>, <c>fsubrl</c> 1.0 at the 24-bit control word: each operation rounded to a single.</summary>
	internal static float SingleOneMinusRatio( uint numerator, int denominator ) => (float)(1f - SingleQuotient( numerator, denominator ));

	/// <summary>The quotient of two integers rounded once to a single (round to nearest, ties to even), as <c>fidivl</c> leaves it at the 24-bit control word.</summary>
	internal static float SingleQuotient( uint numerator, int denominator )
	{
		if ( numerator == 0 || denominator == 0 )
			return (float)numerator / denominator;
		var divisor = (ulong)Math.Abs( (long)denominator );
		// the quotient scaled by 2^64 has at least 33 bits, so its 24 leading bits and the rest decide the rounding exactly
		var scaled = (UInt128)numerator << 64;
		var quotient = scaled / divisor;
		var rest = scaled % divisor;
		var shift = 128 - (int)UInt128.LeadingZeroCount( quotient ) - 24;
		var mantissa = quotient >> shift;
		var dropped = quotient & ((UInt128.One << shift) - 1);
		var half = UInt128.One << (shift - 1);
		if ( dropped > half || dropped == half && (rest != 0 || (mantissa & 1) != 0) )
			mantissa++;
		var value = (float)Math.ScaleB( (double)mantissa, shift - 64 );
		return denominator < 0 ? -value : value;
	}

	/// <summary>The meter clamp of <c>0x60D0D0</c> on a sum already rounded to a single: above 100 gives 100, below 0 gives 0.</summary>
	private static float ClampSingle( float sum ) => sum > 100f ? 100f : sum < 0f ? 0f : sum;

	/// <summary>Whether the member is too tired to go on: its energy byte below RestLevel.</summary>
	// [BIN:TPI-EXE:0x62B3B0 tired] (u8)trunc(energy) < RestLevel, signed
	public bool Tired( ThemeParkIncStaffSettings settings ) => ((int)Energy & 0xFF) < settings.RestLevel;

	/// <summary>The member's thing id in the park's thing pool (the handle the litter claim and the message order use); 0 until it is placed.</summary>
	public int ThingId { get; internal set; }
	/// <summary>The state word (+0x1B4): 0 idle, 1 walking (patrol), a handyman's 0x0F to litter and 0x10 sweeping, 7 to the training room and 8 training, a mechanic's 21 to the ride and 22
	/// repairing (and his training's 7 and 8), a researcher's 24 researching and 25 to his room (ThemeParkIncStaffWorld).</summary>
	public int State { get; internal set; }
	/// <summary>The idle stamp (+0x23C): the turn an idle spell after a walk began, else 0.</summary>
	public long IdleStamp { get; internal set; }
	/// <summary>The work stamp (+0x5D4): the turn the sweep began.</summary>
	public long WorkStamp { get; internal set; }
	/// <summary>The litter target (+0x5D8) as a cell word (y × 128 + x + 1); 0 for none.</summary>
	public int LitterCell { get; internal set; }
	/// <summary>A mechanic's ride (+0x5D8, the thing of the ride the search found) as the ride's economy instance id; 0 for none.</summary>
	public int RideId { get; internal set; }
	/// <summary>A mechanic's job timer (+0x5D4): the updates of work left on the ride, counted down to 0 (ThemeParkIncStaffWorld).</summary>
	public long JobTimer { get; internal set; }
	/// <summary>A gardener's plant (+0x5D8, <c>mPlantToWater</c>, the thing the chooser found) as the plant's economy instance id; 0 for none. His work stamp is <see cref="WorkStamp"/> (+0x5D4).</summary>
	public int PlantId { get; internal set; }
	/// <summary>
	/// An entertainer's or a guard's cell effect (+0xE4, the cell word 0x609E20 last saw him on) and the radius the effect went down with: the row 0 (entertainers) or 3 (guards) of the region
	/// grid is on that cell with that radius (0x5D1960, 0x5D3B40); 0 for another role.
	/// </summary>
	public int EffectCell { get; internal set; }
	/// <summary>The radius of the effect on <see cref="EffectCell"/> (the argument of 0x5D7500 at the moment it went down).</summary>
	public int EffectRadius { get; internal set; }
	/// <summary>The base speed word (+0xD4).</summary>
	public int BaseSpeed { get; internal set; }
	/// <summary>The speed bonus word (+0xD6): 25 on the walk to a job, else 0.</summary>
	public int SpeedBonus { get; internal set; }
	/// <summary>The decaying speed boost word (+0xD8).</summary>
	public ushort Boost { get; internal set; }
	/// <summary>The smoothed speed (+0xDC) the walk's caps follow.</summary>
	public float SmoothedSpeed { get; internal set; }
	/// <summary>The patrol rectangle (+0x24C..+0x258, cells, inclusive): the whole map until a patrol area is set.</summary>
	public (int X0, int Y0, int X1, int Y1) PatrolArea { get; internal set; } = (0, 0, 0x7F, 0x7F);
	/// <summary>The sprite template the member last queued on its sprite source (+0x20, through 0x5D0F90); presentation.</summary>
	public int Template { get; internal set; }
	/// <summary>The member's walk; null until it is placed.</summary>
	public GuestNavigation? Navigation { get; internal set; }
	/// <summary>The entry number the member's thing holds on its cell's chain (WALK-003); 0 while the chain holds it.</summary>
	public long CellSerial { get; internal set; }
	/// <summary>Whether the member stands in the park.</summary>
	public bool Placed => Navigation != null;
	/// <summary>The turn of the member's last thing update (bookkeeping for the smoke test and the HUD; not saved).</summary>
	internal long LastUpdateTurn { get; set; } = -1;
	/// <summary>The walk's speed caps the pre-update set this update (not saved: every update sets them before its walk).</summary>
	internal int MaxSpeed, MaxForce;
	/// <summary>Presentation: the point the member is drawn at, in cells.</summary>
	public float DrawX { get; internal set; }
	public float DrawY { get; internal set; }
	/// <summary>Presentation: the walk's heading (the direction of its last step), which turns the sprite.</summary>
	public float HeadingX { get; internal set; }
	public float HeadingY { get; internal set; } = 1f;
	/// <summary>Presentation: the walk position at the start of the member's last update and the tick it was taken, for the draw point's interpolation over the turn.</summary>
	internal int TurnFromX, TurnFromY;
	internal long TurnTick = -1;
	/// <summary>
	/// Presentation: the member's sprite wrapper and source (+0x14), playing the templates the state setters and the walk step queue (ThemeParkIncStaffWorld). Not saved: a restored member gets a fresh
	/// source in his state's template at his first update.
	/// </summary>
	public StaffSpriteState Sprite { get; } = new();

	/// <summary>The member's thing for a save; null before it is placed.</summary>
	internal ThemeParkIncStaffWorldSnapshot? CaptureWorld() => Navigation is not { } navigation ? null : Role switch
	{
		ThemeParkIncStaffRole.Mechanic => new( ThingId, State, IdleStamp, 0, 0, BaseSpeed, SpeedBonus, Boost, SmoothedSpeed, new[] { PatrolArea.X0, PatrolArea.Y0, PatrolArea.X1, PatrolArea.Y1 },
			Template, ParkSaveFile.NavigationData.Capture( navigation ), CellSerial, RideId, JobTimer ),
		ThemeParkIncStaffRole.Gardener => new( ThingId, State, IdleStamp, WorkStamp, 0, BaseSpeed, SpeedBonus, Boost, SmoothedSpeed, new[] { PatrolArea.X0, PatrolArea.Y0, PatrolArea.X1, PatrolArea.Y1 },
			Template, ParkSaveFile.NavigationData.Capture( navigation ), CellSerial, PlantId: PlantId ),
		ThemeParkIncStaffRole.Entertainer or ThemeParkIncStaffRole.Guard => new( ThingId, State, IdleStamp, WorkStamp, 0, BaseSpeed, SpeedBonus, Boost, SmoothedSpeed,
			new[] { PatrolArea.X0, PatrolArea.Y0, PatrolArea.X1, PatrolArea.Y1 }, Template, ParkSaveFile.NavigationData.Capture( navigation ), CellSerial, EffectCell: EffectCell, EffectRadius: EffectRadius ),
		_ => new( ThingId, State, IdleStamp, WorkStamp, LitterCell, BaseSpeed, SpeedBonus, Boost, SmoothedSpeed, new[] { PatrolArea.X0, PatrolArea.Y0, PatrolArea.X1, PatrolArea.Y1 }, Template,
			ParkSaveFile.NavigationData.Capture( navigation ), CellSerial )
	};

	/// <summary>The roles <see cref="ThemeParkIncStaffWorld"/> places in the park: every role.</summary>
	public static bool IsPlacedRole( ThemeParkIncStaffRole role ) => Enum.IsDefined( role );

	/// <summary>
	/// The states <see cref="ThemeParkIncStaffWorld"/> runs for a role: a handyman's 0, 1, 0x0F and 0x10, a mechanic's 0, 1, 21 and 22 and a gardener's 0, 1, 0x1F and 0x20, each with the training
	/// states 7, 8, 0x25 and 0x26 (the common handler 0x6287A0); a researcher's 0, 1, 24 and 25; an entertainer's 0, 1 and 0x17; a guard's 0 and 1 with the training states; and for all six the rest
	/// states 2 (to a rest area), 3 (resting) and 0x23 (looking for a rest area) and the strike states 4 (to the strike area) and 5 (striking).
	/// </summary>
	internal static bool IsPortedState( ThemeParkIncStaffRole role, int state ) => role switch
	{
		ThemeParkIncStaffRole.Handyman or ThemeParkIncStaffRole.Mechanic or ThemeParkIncStaffRole.Researcher or ThemeParkIncStaffRole.Gardener or ThemeParkIncStaffRole.Entertainer
			or ThemeParkIncStaffRole.Guard when state is ThemeParkIncStaffWorld.ToRestArea or ThemeParkIncStaffWorld.Resting or ThemeParkIncStaffWorld.RestSearch
				or ThemeParkIncStaffWorld.ToStrike or ThemeParkIncStaffWorld.Striking => true,
		ThemeParkIncStaffRole.Handyman => state is 0 or 1 or 0x0F or 0x10 || IsTrainingState( state ),
		ThemeParkIncStaffRole.Mechanic => state is 0 or 1 or ThemeParkIncStaffWorld.ToRide or ThemeParkIncStaffWorld.Repairing || IsTrainingState( state ),
		ThemeParkIncStaffRole.Gardener => state is 0 or 1 or ThemeParkIncStaffWorld.ToPlant or ThemeParkIncStaffWorld.Watering || IsTrainingState( state ),
		ThemeParkIncStaffRole.Researcher => state is 0 or 1 or ThemeParkIncResearch.Researching or ThemeParkIncStaffWorld.ToResearchRoom,
		ThemeParkIncStaffRole.Entertainer => state is 0 or 1 or ThemeParkIncStaffWorld.Performing,
		ThemeParkIncStaffRole.Guard => state is 0 or 1 || IsTrainingState( state ),
		_ => false
	};

	/// <summary>The training states a member of a trainable role runs (to the room, training and their suspended forms).</summary>
	private static bool IsTrainingState( int state ) => state is ThemeParkIncTraining.ToTrainingRoom or ThemeParkIncTraining.Training or ThemeParkIncTraining.TrainingSuspended
		or ThemeParkIncTraining.ToTrainingRoomSuspended;

	/// <summary>
	/// Whether a saved thing can be this model's: a ported state, a thing id of the pool, a cell word of the map, a valid walk; a mechanic's ride and job timer, a gardener's plant, a handyman's
	/// litter target, each only on its own role, and the whole map as the patrol rectangle for a mechanic and a gardener (no patrol area is set in the park, so their area test is the whole-map answer).
	/// </summary>
	internal static bool IsValidWorld( ThemeParkIncStaffRole role, ThemeParkIncStaffWorldSnapshot? world ) => world == null || IsPortedState( role, world.State )
		&& world.ThingId is >= 1 and <= ThingPool.Capacity && world.LitterCell is >= 0 and <= ParkLitter.MapSize * ParkLitter.MapSize && world.BaseSpeed is >= 0 and <= ushort.MaxValue
		&& world.SpeedBonus is >= 0 and <= ushort.MaxValue && world.Boost is >= 0 and <= ushort.MaxValue && float.IsFinite( world.SmoothedSpeed ) && world.IdleStamp >= 0 && world.WorkStamp >= 0
		&& world.PatrolArea is { Length: 4 } area && area.All( value => value is >= 0 and < ParkLitter.MapSize ) && world.CellSerial >= 0 && world.Navigation is { IsValid: true }
		&& role switch
		{
			ThemeParkIncStaffRole.Mechanic => world.RideId is >= 0 && world.JobTimer is >= 0 and <= uint.MaxValue && world.PlantId == null && world.LitterCell == 0 && world.WorkStamp == 0 && world.EffectCell == null && world.EffectRadius == null
				&& world.Template is 1 or 3 or 0xF or 0x1A or ThemeParkIncStaffWorld.StrikeTemplate && area is [0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1],
			ThemeParkIncStaffRole.Gardener => world.PlantId is >= 0 && world.RideId == null && world.JobTimer == null && world.LitterCell == 0 && world.EffectCell == null && world.EffectRadius == null && world.Template is 1 or 3 or 0xF or 0x19 or ThemeParkIncStaffWorld.StrikeTemplate
				&& area is [0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1],
			// an entertainer or a guard keeps his cell effect (0x609E20's cell word and the radius it went down with); the entertainer's performing template is 0x13..0x16, no other role has an effect
			ThemeParkIncStaffRole.Entertainer => world.RideId == null && world.JobTimer == null && world.PlantId == null && world.LitterCell == 0 && world.Template is 1 or 3 or 0xF or (>= 0x13 and <= 0x16) or ThemeParkIncStaffWorld.StrikeTemplate
				&& IsValidEffect( world ) && area is [0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1],
			ThemeParkIncStaffRole.Guard => world.RideId == null && world.JobTimer == null && world.PlantId == null && world.LitterCell == 0 && world.WorkStamp == 0 && world.Template is 1 or 2 or 3 or 0xF or ThemeParkIncStaffWorld.StrikeTemplate
				&& IsValidEffect( world ) && area is [0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1],
			_ => world.RideId == null && world.JobTimer == null && world.PlantId == null && world.EffectCell == null && world.EffectRadius == null && world.Template is 1 or 2 or 3 or 0xF or 0x19 or ThemeParkIncStaffWorld.StrikeTemplate
		};

	/// <summary>An entertainer's or a guard's cell effect: a cell word of the map and a radius that is the writer's 16-bit argument.</summary>
	private static bool IsValidEffect( ThemeParkIncStaffWorldSnapshot world ) =>
		world.EffectCell is >= 1 and <= ParkLitter.MapSize * ParkLitter.MapSize && world.EffectRadius is >= 0 and <= ushort.MaxValue;

	/// <summary>Restores the saved thing (a valid one, <see cref="IsValidWorld"/>), or leaves the member unplaced.</summary>
	internal void RestoreWorld( ThemeParkIncStaffWorldSnapshot? world )
	{
		if ( world == null )
			return;
		ThingId = world.ThingId;
		State = world.State;
		IdleStamp = world.IdleStamp;
		WorkStamp = world.WorkStamp;
		LitterCell = world.LitterCell;
		RideId = world.RideId ?? 0;
		JobTimer = world.JobTimer ?? 0;
		PlantId = world.PlantId ?? 0;
		EffectCell = world.EffectCell ?? 0;
		EffectRadius = world.EffectRadius ?? 0;
		BaseSpeed = world.BaseSpeed;
		SpeedBonus = world.SpeedBonus;
		Boost = (ushort)world.Boost;
		SmoothedSpeed = world.SmoothedSpeed;
		PatrolArea = (world.PatrolArea[0], world.PatrolArea[1], world.PatrolArea[2], world.PatrolArea[3]);
		Template = world.Template;
		Navigation = world.Navigation!.Restore();
		CellSerial = world.CellSerial;
		DrawX = Navigation.X / (float)Fixed16.One;
		DrawY = Navigation.Y / (float)Fixed16.One;
	}

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
