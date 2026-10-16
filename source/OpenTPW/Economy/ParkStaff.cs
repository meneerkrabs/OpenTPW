namespace OpenTPW;

/// <summary>Staff states in <c>STAFFSTATES.str</c> order.</summary>
public enum StaffState
{
	Idle = 0,
	Patrolling = 1,
	Working = 2,
	Resting = 3,
	OnStrike = 4,
	PickedUp = 5
}

/// <summary>
/// A world-driven member's own state (tp.exe word <c>+0x19c</c>; the handlers <c>0x004D73C0</c> handyman, <c>0x004D4810</c> entertainer,
/// <c>0x004D6410</c> guard); <see cref="StaffMember.State"/> is its display name (<c>0x005064F0</c>: 0 Idle, 1 Patrolling, 2–3 Resting,
/// 4–5 On strike, 7 Picked up, the others Working). 0–7 are shared by every role; the others are the role's own.
/// </summary>
public enum StaffJob : byte
{
	Idle = 0,
	Patrol = 1,
	ToRestArea = 2,
	Resting = 3,
	ToStrike = 4,
	Striking = 5,
	PickedUp = 7,
	ToLitter = 8,
	Sweeping = 9,
	ToToilet = 10,
	Cleaning = 11,
	/// <summary>A mechanic walks to a ride that needs him (state 12).</summary>
	ToRide = 12,
	/// <summary>A mechanic repairs or upgrades the ride (state 13).</summary>
	Repairing = 13,
	/// <summary>An entertainer's performance (state 14).</summary>
	Performing = 14,
	/// <summary>A researcher at work (state 15).</summary>
	Researching = 15,
	/// <summary>A guard runs after a prankster (state 16).</summary>
	Chasing = 16,
	/// <summary>A guard takes a caught prankster to the ticket booth (state 18).</summary>
	Escorting = 18,
	/// <summary>A guard walks back from the escort (state 19).</summary>
	ReturningFromEscort = 19
}

/// <summary>An employee. <see cref="NameIndex"/> indexes the role's name table (e.g. <c>MECHANIC_NAMES.str</c>).</summary>
public sealed class StaffMember
{
	public required int Id { get; init; }
	public required StaffType Type { get; init; }
	public required int NameIndex { get; init; }
	/// <summary>
	/// The member's own name, stored inline in an original save (the staff suffix's <c>mName[i]</c>, +0x1A0, which the employee panels show); null
	/// for a member hired in OpenTPW, whose name is <see cref="NameIndex"/> in the role's name table.
	/// </summary>
	public string? Name { get; init; }
	/// <summary>The longest inline name an original save holds (33 UTF-16 units including the terminator).</summary>
	public const int MaximumNameLength = 32;
	public required int Grade { get; set; }
	/// <summary>Training progress towards the next grade, 0–99 ("raise the training level by 1%").</summary>
	public int TrainingPoints { get; set; }
	public long HiredTick { get; init; }
	// [BIN:STP-PPC:0x100F29FC staff constructor] happiness (+500) and energy (+504) are singles that start at 100.0; ECON-014 / ORACLE.md F22 (StaffVitals) moves them every park turn
	/// <summary>Happiness, 0–100, a single as in the original (+500): it falls while the member walks or works and without a rest area, and rises while resting.</summary>
	public float Happiness { get; set; } = StaffVitals.Maximum;
	/// <summary>Energy, 0–100, a single (+504): it falls while the member walks or works and rises while resting; at the rest level the member seeks a rest area.</summary>
	public float Energy { get; set; } = StaffVitals.Maximum;
	public StaffState State { get; set; } = StaffState.Idle;
	/// <summary>Remaining ticks of the current job (repair, cleaning), 0 when free.</summary>
	public long BusyTicks { get; set; }
	/// <summary>A new live-world mechanic job is reserved but its work timer has not started. Legacy jobs default false.</summary>
	public bool AwaitingJobArrival { get; set; }
	/// <summary>Object instance the employee works on, 0 when none.</summary>
	public int AssignedInstanceId { get; set; }
	/// <summary>The littered cell a handyman sweeps (tp.exe <c>+0x218</c>, <c>mTargetLitterCell</c>), null when his job is not a sweep.</summary>
	public (int X, int Y)? LitterCell { get; set; }
	/// <summary>Actual host actor position in cell units; null until attached to a world. Native saves use X/Y divided by 256.</summary>
	public float? CellX { get; set; }
	public float? CellY { get; set; }
	/// <summary>Fraction of the host's next one-cell movement interval, persisted across loads.</summary>
	public double TravelSeconds { get; set; }
	public bool IsAvailable => BusyTicks == 0 && State is not (StaffState.OnStrike or StaffState.PickedUp or StaffState.Resting);

	// A handyman, entertainer or guard in a world (IStaffWorld): the original's state machine and walk (docs/reverse/WANDER-plan.md §4; tp.exe offsets in brackets).
	/// <summary>The member's own state [+0x19c].</summary>
	public StaffJob Job { get; set; }
	/// <summary>The turn an idle spell started [+0x200] (0: search at once).</summary>
	public long IdleStamp { get; set; }
	/// <summary>The turn a sweep, a clean or a performance started [+0x214]; a guard's chase counts down here.</summary>
	public long JobStamp { get; set; }
	/// <summary>The toilet a handyman walks to or cleans [+0x21a], 0 when none.</summary>
	public int ToiletId { get; set; }
	/// <summary>The patrol rectangle, inclusive cells [+0x20a, +0x20c]; the whole map until the player draws one.</summary>
	public (int X0, int Y0, int X1, int Y1) PatrolArea { get; set; } = WholeMap;

	/// <summary>The patrol rectangle of a member without one: the whole map (the constructor's corner cell ids 1 and 0x4000).</summary>
	public static readonly (int X0, int Y0, int X1, int Y1) WholeMap = (0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1);

	/// <summary>The player draws the member's patrol rectangle between two corner cells (tp.exe <c>0x00506E50</c>), clamped to the map.</summary>
	// [BIN:TPW-EXE:0x00506E50 set patrol area] stores the two corner cell ids at +0x20A and +0x20C; the area test 0x00506ED0 reads the first as the low corner
	// [BIN:TPW-EXE:0x00524960 patrol tool] mouse mode 0x36: the first click on the map stores a corner cell, the second orders the two (the smaller x and y first) and sets the selected member's area, then the tool ends (0x0052F200)
	public void SetPatrolArea( (int X, int Y) a, (int X, int Y) b )
	{
		static int Clamp( int value ) => Math.Clamp( value, 0, ParkLitter.MapSize - 1 );
		PatrolArea = (Clamp( Math.Min( a.X, b.X ) ), Clamp( Math.Min( a.Y, b.Y ) ), Clamp( Math.Max( a.X, b.X ) ), Clamp( Math.Max( a.Y, b.Y ) ));
	}

	/// <summary>The player clears the member's patrol rectangle (tp.exe <c>0x00506E90</c>): the whole map again.</summary>
	// [BIN:TPW-EXE:0x00506E90 clear patrol area] +0x20A = 1 and +0x20C = 0x4000, cells (0, 0) and (127, 127); the employee panel's button 0x004B5B50 with 0
	public void ClearPatrolArea() => PatrolArea = WholeMap;

	/// <summary>Whether the member has a patrol rectangle of its own.</summary>
	public bool HasPatrolArea => PatrolArea != WholeMap;
	/// <summary>Base speed [+0xc0]: 60, 80, 100, 120 or 140 by energy.</summary>
	public int BaseSpeed { get; set; } = 60;
	/// <summary>Speed bonus [+0xc2]: 25 while walking to a job.</summary>
	public int SpeedBonus { get; set; }
	/// <summary>The smoothed speed [+200] (<see cref="GuestSpeed.Update"/>).</summary>
	public float SmoothedSpeed { get; set; }
	/// <summary>The steering state [+0xd4] while a world moves the member; null before.</summary>
	public GuestNavigation? Navigation { get; set; }
	/// <summary>The guest a guard chases or escorts [+0x212], 0 when none.</summary>
	public int TargetGuestId { get; set; }
	/// <summary>The cell where an entertainer's or a guard's <c>RegionFX</c> row lies [+0xcc], null before a world placed it.</summary>
	public (int X, int Y)? EffectCell { get; set; }
	/// <summary>The ride a mechanic walks to or works on [+0x218], 0 when none.</summary>
	public int RideId { get; set; }
	/// <summary>The member's thought (its thought object at <c>+0x30</c>, tp.exe <c>0x0050BE80</c>): 0, or the staff ids 18 (very happy), 19 (unhappy), 20 (tired),
	/// 21 (going on strike) and 22 (no destination).</summary>
	public int Thought { get; set; }
	/// <summary>The thought the member's bubble shows, 0 without a bubble (see <see cref="GuestThoughts"/>).</summary>
	public int BubbleThought { get; set; }
	/// <summary>The park turn the member's last bubble appeared.</summary>
	public long BubbleTurn { get; set; }
	/// <summary>The rest area (an object with <c>UsageInfo.ChillsYouOut</c>) a member walks to or rests in [+0x208], 0 when none.</summary>
	public int RestAreaId { get; set; }
	/// <summary>
	/// The points a member still walks off the path grid after its current walk (the road outside the park to and from the strike area): each a cell and
	/// its sub-cell bytes. Empty on the paths.
	/// </summary>
	public List<(int X, int Y, int ByteX, int ByteY)> OffGridLeg { get; set; } = new();
	/// <summary>
	/// The point (1/256 cell) a member loaded from an original save walks on to: the person's <c>mAccurateDestX</c>/<c>mAccurateDestY</c>, to which the
	/// original's person loader routes again once it has read the person (tp.exe <c>0x004FA5F0</c> at <c>0x004F9407</c>). A world takes it when it first
	/// places the member; null otherwise.
	/// </summary>
	public (int X, int Y)? ResumeDestination { get; set; }
}

/// <summary>A job applicant in the hiring pool; leaves the pool at <see cref="ExpiresTick"/>.</summary>
public sealed record StaffCandidate( int Id, StaffType Type, int NameIndex, int Grade, long ExpiresTick );

/// <summary>
/// Hiring pool, employees, wages and training. Data-driven values: pool sizes, grade chances,
/// park maxima, wages (<see cref="BalanceSettings.GetMonthlyWage"/>) and <c>PoundsPerTrainingPoint</c>.
/// Pool timing counts <c>TimeBetweenStaffUpdates</c>/<c>StaffTimeoutTime</c> in units of four park turns (tp.exe 0x5084f0).
/// <b>Approximations</b> (documented in docs/ECONOMY.md): candidate grades
/// are drawn around <c>AvgGradeOf*</c>, hiring is free (the original's hire path has no debit, <c>BaseCostPerStaff</c> and
/// <c>CostPerQualityLevel</c> are never read, ORACLE.md F28), training spends a per-role monthly budget at the month's end
/// (divided over every employee of the role, ORACLE F17), and 100 training points raise one grade.
/// </summary>
public sealed class ParkStaff
{
	// [DATA:Language/English/*_NAMES.str:entry count 35]
	public const int NameTableSize = 35; // every *_NAMES.str table of the English data has 35 entries
	// [BIN:TPW-EXE:0x505a10 CStaff::Train] the points are min(share / price, 100) and a grade is reached at 100 (the carry is points − 100, happiness set to 100.0); the Standard.sam comment "costs 1000 to get up to grade 1" is not what the code charges (100 points at price 5 cost 500)
	public const int TrainingPointsPerGrade = 100;
	private readonly BalanceSettings settings;
	private readonly List<StaffMember> members = new();
	private readonly List<StaffCandidate> candidates = new();
	private readonly long[] trainingBudget = new long[BalanceSettings.StaffTypeCount];

	public ParkStaff( BalanceSettings settings ) => this.settings = settings;

	/// <summary>The five roles' strike levels, flags and last-considered turns (the staff manager's per-role records, ECON-014).</summary>
	public StaffStrikeBoard Strikes { get; } = new();

	public IReadOnlyList<StaffMember> Members => members;
	public IReadOnlyList<StaffCandidate> Candidates => candidates;
	public int NextId { get; private set; } = 1;
	public long NextPoolUpdateTick { get; private set; }
	/// <summary>
	/// The candidate whose Place tool is running (the original's "being placed" flag, candidate <c>+0xb</c>, tp.exe 0x507bf0): it stays in the pool,
	/// is not hired yet and does not expire while the player places it. Not saved: a pending hire is only a flag.
	/// </summary>
	public int PendingPlacementId { get; set; }

	/// <summary>Park turns per unit of <c>TimeBetweenStaffUpdates</c> and <c>StaffTimeoutTime</c>: the pool compares elapsed turns with four times them.</summary>
	public const int TurnsPerPoolUnit = 4;
	// [BIN:TPW-EXE:0x5084f0 staff pool update] a candidate whose turns since its stamp (+0xc, 0x41a970) exceed 4 × its timeout (+0x10) leaves unless it is being placed; the pool refills once the turns since its own stamp exceed 4 × TimeBetweenStaffUpdates (0x785304)
	/// <summary>Ticks from one pool refill to the next: the refill comes on the first turn past 4 × <c>TimeBetweenStaffUpdates</c> turns.</summary>
	public long UpdateInterval => ParkCalendar.TickOfTurn( TurnsPerPoolUnit * (long)Math.Max( 0, settings.TimeBetweenStaffUpdates ) + 1 );

	/// <summary>The first tick of the turn after <paramref name="units"/> pool units from the turn of <paramref name="tick"/>.</summary>
	private static long TickAfterUnits( long tick, long units ) => ParkCalendar.TickOfTurn( ParkCalendar.Turn( tick ) + TurnsPerPoolUnit * units + 1 );

	public IEnumerable<StaffMember> OfType( StaffType type ) => members.Where( member => member.Type == type );

	public long GetTrainingBudget( StaffType type ) => trainingBudget[(int)type];

	public void SetTrainingBudget( StaffType type, long monthlyBudget ) => trainingBudget[(int)type] = Math.Max( 0, monthlyBudget );

	/// <summary>Skill shown for an employee: 20 × (grade + training percentage / 100), truncated, in single precision as in the original (0x100F41DC).</summary>
	public static int StaffSkill( StaffMember member ) => (int)(20f * (float)(member.Grade + member.TrainingPoints / 100.0));

	public long MonthlyWage( StaffMember member ) => settings.GetMonthlyWage( member.Type, member.Grade );

	public long TotalMonthlyWages => members.Sum( MonthlyWage );

	/// <summary>Fills the initial pool with <c>StaffPoolInfo.BeginningNumberOf*</c> candidates per role.</summary>
	public void CreateInitialPool( DeterministicRandom random, long tick )
	{
		foreach ( var role in settings.Roles )
		{
			for ( var count = 0; count < Math.Min( role.BeginningNumberInPool, Math.Max( role.MaximumInPool, role.MinimumInPool ) ); count++ )
				candidates.Add( CreateCandidate( role, random, tick ) );
		}
		NextPoolUpdateTick = TickAfterUnits( tick, settings.TimeBetweenStaffUpdates );
	}

	/// <summary>The original hiring pool has 32 slots.</summary>
	public const int PoolSlots = 32;

	/// <summary>
	/// Removes expired candidates, adds up to <c>MaxNumberOfStaffPerUpdate</c> candidates with roles drawn
	/// in proportion to each role's shortfall below <c>Max*</c> (roles at <c>Max*InPark</c> are skipped), then
	/// adds candidates until hired plus pooled staff reach <c>Min*InPool</c> for every role.
	/// </summary>
	// [BIN:STP-PPC:0x100F6E24 staff pool update] draw = rand() % total shortfall picks the role whose share it falls in; each added candidate lowers that role's shortfall and the per-update cap (also limited by the 32 free slots); 0x100F65E8 drops roles whose hired count reached Max*InPark; 0x100F6790 then tops every role up to Min*InPool counting hired and pooled staff
	public void UpdatePool( DeterministicRandom random, long tick )
	{
		candidates.RemoveAll( candidate => candidate.ExpiresTick <= tick && candidate.Id != PendingPlacementId );
		if ( tick < NextPoolUpdateTick )
			return;
		NextPoolUpdateTick = TickAfterUnits( tick, settings.TimeBetweenStaffUpdates );
		var roles = settings.Roles;
		var shortfall = roles.Select( role => OfType( role.Type ).Count() >= role.MaximumInPark ? 0 : role.MaximumInPool - candidates.Count( candidate => candidate.Type == role.Type ) ).ToArray();
		var cap = Math.Min( settings.MaxNumberOfStaffPerUpdate, PoolSlots - candidates.Count );
		for ( var total = shortfall.Sum(); cap > 0 && total > 0 && candidates.Count < PoolSlots; total-- )
		{
			var draw = random.Next( total );
			var index = -1;
			for ( var role = 0; role < shortfall.Length && index < 0; role++ )
			{
				if ( draw < shortfall[role] )
					index = role;
				else
					draw -= shortfall[role];
			}
			if ( index < 0 )
				break;
			candidates.Add( CreateCandidate( roles[index], random, tick ) );
			cap--;
			shortfall[index]--;
		}
		bool added;
		do
		{
			added = false;
			foreach ( var role in roles )
			{
				if ( candidates.Count < PoolSlots && OfType( role.Type ).Count() + candidates.Count( candidate => candidate.Type == role.Type ) < role.MinimumInPool )
				{
					candidates.Add( CreateCandidate( role, random, tick ) );
					added = true;
				}
			}
		}
		while ( added );
	}

	/// <summary>The most name draws a candidate makes before it keeps a name already in use (tp.exe <c>0x00507580</c>).</summary>
	public const int MaximumNameDraws = 15;

	private StaffCandidate CreateCandidate( StaffRoleSettings role, DeterministicRandom random, long tick )
	{
		// [BIN:TPW-EXE:0x0050761C staff candidate draws] the constructor 0x00507600 draws, in order: the sprite variant (0x00541F70: (R >> 2) mod the role category's bank count, candidate +9), one draw it discards (0x0050762D), the grade (0x00507638), the name (0x00507580) and the timeout
		// [APPROX:GUEST-014] the variant's draw is made in its place but its value is not kept: the sprite bank still derives from the name index and id
		random.NextUInt64();
		random.NextUInt64();
		// [BIN:STP-PPC:0x100F5B64 staff candidate] grade = (AvgGradeOf* + random % 3 - 1) & 0xFF, stored as 4 when above 4 (so -1 wraps to 4); ChanceToGetGreat* is never read
		var raw = (role.AverageGrade + random.Next( 3 ) - 1) & 0xFF;
		var grade = raw > BalanceSettings.GradeCount - 1 ? BalanceSettings.GradeCount - 1 : raw;
		// [BIN:TPW-EXE:0x00507580 candidate name] R mod 35, drawn again while 0x005083F0 finds the name's text on a pool candidate (the 32 slots with +0xA set) or a hired member (thing types 4-8), at most 15 draws; the last one is kept even when it is taken
		// [DATA:Language/English/*_NAMES.str:no text twice] no name text repeats within or across the five tables (English and Dutch), so the role and index stand for the text
		int name;
		var draws = 0;
		do
		{
			name = random.Next( NameTableSize );
			draws++;
		}
		while ( NameTaken( role.Type, name ) && draws < MaximumNameDraws );
		// [BIN:TPW-EXE:0x507600 staff candidate] the timeout (+0x10) is StaffTimeoutTime + R % (StaffTimeoutTime >> 1)
		var timeout = settings.StaffTimeoutTime + (settings.StaffTimeoutTime >> 1 > 0 ? random.Next( settings.StaffTimeoutTime >> 1 ) : 0);
		return new StaffCandidate( NextId++, role.Type, name, grade, TickAfterUnits( tick, timeout ) );
	}

	/// <summary>Whether a pool candidate or an employee of the role already has the name (tp.exe <c>0x005083F0</c>).</summary>
	private bool NameTaken( StaffType type, int nameIndex ) =>
		candidates.Any( candidate => candidate.Type == type && candidate.NameIndex == nameIndex ) || members.Any( member => member.Type == type && member.NameIndex == nameIndex );

	public bool CanHire( StaffType type ) => OfType( type ).Count() < settings[type].MaximumInPark;

	// [BIN:STP-PPC:0x100F61D0 hiring is free] the hire button's handler queues a command (0xf61d0, executor 0x6c034, thing factory 0xb882c) and nothing on that path debits the bank: the 16 direct callers of Spend 0xcbfdc include no hire routine, and BaseCostPerStaff (+700) and CostPerQualityLevel (+704) are never read; the wage is paid at the month's end and dismissing one costs one more (ParkEconomy.Fire, ORACLE.md F28)
	public StaffMember Hire( int candidateId, long tick )
	{
		var candidate = candidates.FirstOrDefault( item => item.Id == candidateId ) ?? throw new InvalidOperationException( $"No staff candidate {candidateId}." );
		if ( !CanHire( candidate.Type ) )
			throw new InvalidOperationException( $"The park already employs the maximum of {settings[candidate.Type].MaximumInPark} {candidate.Type} staff." );
		candidates.Remove( candidate );
		var member = new StaffMember { Id = candidate.Id, Type = candidate.Type, NameIndex = candidate.NameIndex, Grade = candidate.Grade, HiredTick = tick };
		members.Add( member );
		return member;
	}

	public StaffMember Fire( int staffId )
	{
		var member = members.FirstOrDefault( item => item.Id == staffId ) ?? throw new InvalidOperationException( $"No employee {staffId}." );
		members.Remove( member );
		return member;
	}

	/// <summary>
	/// Month-end training: each role's budget is divided by the role's number of employees (the original adds a base count that is
	/// zero in the shipped build), and every employee below the top grade is trained with that share. Returns the amount spent.
	/// </summary>
	// [BIN:STP-PPC:0x100FC1C4 training split] ECON-013, ORACLE F17 (executed, 507 inputs): share = budget / all employees of the role, including those already at the top grade, whose share is not spent; each other employee costs the whole share, even where it buys fewer points than it could, and gets min(share / price, 100) points; the points above 99 carry into the next grade (0x100F3588)
	public long Train()
	{
		long spent = 0;
		foreach ( var role in settings.Roles )
		{
			var employees = OfType( role.Type ).ToList();
			if ( employees.Count == 0 )
				continue;
			var share = trainingBudget[(int)role.Type] / employees.Count;
			foreach ( var member in employees )
			{
				if ( member.Grade >= BalanceSettings.GradeCount - 1 )
					continue;
				var price = role.PoundsPerTrainingPoint[member.Grade];
				if ( price <= 0 ) // the original divides by it (undefined at 0); no shipped grade below the top has a zero price
					continue;
				spent += share;
				var total = member.TrainingPoints + (int)Math.Min( share / price, TrainingPointsPerGrade );
				if ( total >= TrainingPointsPerGrade )
				{
					member.Grade++;
					member.TrainingPoints = total - TrainingPointsPerGrade;
					// [BIN:STP-PPC:0x100F36E4 training promotion] a promotion stores the single 100.0 into the member's happiness (+500); the energy is untouched
					member.Happiness = StaffVitals.Maximum;
				}
				else
					member.TrainingPoints = total;
			}
		}
		return spent;
	}

	internal void Restore( IEnumerable<StaffMember> restoredMembers, IEnumerable<StaffCandidate> restoredCandidates, IReadOnlyList<long> budgets, int nextId, long nextPoolUpdate )
	{
		PendingPlacementId = 0;
		members.Clear();
		members.AddRange( restoredMembers );
		candidates.Clear();
		candidates.AddRange( restoredCandidates );
		for ( var index = 0; index < trainingBudget.Length; index++ )
			trainingBudget[index] = index < budgets.Count ? budgets[index] : 0;
		NextId = nextId;
		NextPoolUpdateTick = nextPoolUpdate;
	}

	internal int AllocateId() => NextId++;

	/// <summary>The display state of a member's own state word +0x19c (tp.exe <c>0x005064F0</c>): 0 Idle, 1 Patrolling, 2–3 Resting, 4–5 On strike, 7 Picked up, any other Working.</summary>
	// [BIN:TPW-EXE:0x5064f0 display state] switch on +0x19c: 0 → 0, 1 → 1, 2 and 3 → 3, 4 and 5 → 4, 7 → 5, default → 2
	public static StaffState DisplayState( uint word ) => word switch
	{
		0 => StaffState.Idle,
		1 => StaffState.Patrolling,
		2 or 3 => StaffState.Resting,
		4 or 5 => StaffState.OnStrike,
		7 => StaffState.PickedUp,
		_ => StaffState.Working
	};

	/// <summary>Adds an employee taken from an original save (not hired from the pool).</summary>
	internal StaffMember AddImported( StaffType type, int grade, int trainingPoints, float happiness, StaffState state, long hiredTick, float energy = StaffVitals.Maximum, string? name = null )
	{
		var member = new StaffMember { Id = AllocateId(), Type = type, NameIndex = 0, Name = string.IsNullOrEmpty( name ) ? null : name, Grade = grade, TrainingPoints = trainingPoints, Happiness = happiness, Energy = energy, State = state, HiredTick = hiredTick };
		members.Add( member );
		return member;
	}

	/// <summary>
	/// Replaces the pool with an original save's candidates (role, name index, grade, the turn it was made and its timeout in units of four turns) and
	/// sets the next refill from the turn of the pool's last one; each candidate leaves on the first turn past its timeout.
	/// </summary>
	internal void RestoreOriginalPool( IEnumerable<(StaffType Type, int NameIndex, int Grade, long MadeTurn, long Timeout)> saved, long lastRefillTurn )
	{
		candidates.Clear();
		PendingPlacementId = 0;
		foreach ( var (type, name, grade, made, timeout) in saved.Take( PoolSlots ) )
			candidates.Add( new StaffCandidate( AllocateId(), type, name, grade, TickAfterUnits( ParkCalendar.TickOfTurn( made ), timeout ) ) );
		NextPoolUpdateTick = TickAfterUnits( ParkCalendar.TickOfTurn( lastRefillTurn ), settings.TimeBetweenStaffUpdates );
	}

	/// <summary>Moves the pool's update and expiry times by <paramref name="ticks"/> (a new park started at a later turn).</summary>
	internal void ShiftSchedule( long ticks )
	{
		NextPoolUpdateTick += ticks;
		for ( var index = 0; index < candidates.Count; index++ )
			candidates[index] = candidates[index] with { ExpiresTick = candidates[index].ExpiresTick + ticks };
	}
}
