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

/// <summary>An employee. <see cref="NameIndex"/> indexes the role's name table (e.g. <c>MECHANIC_NAMES.str</c>).</summary>
public sealed class StaffMember
{
	public required int Id { get; init; }
	public required StaffType Type { get; init; }
	public required int NameIndex { get; init; }
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
	/// <summary>Object instance the employee works on, 0 when none.</summary>
	public int AssignedInstanceId { get; set; }
	/// <summary>Actual host actor position in cell units; null until attached to a world. Native saves use X/Y divided by 256.</summary>
	public float? CellX { get; set; }
	public float? CellY { get; set; }
	/// <summary>Fraction of the host's next one-cell movement interval, persisted across loads.</summary>
	public double TravelSeconds { get; set; }
	public bool IsAvailable => BusyTicks == 0 && State is not (StaffState.OnStrike or StaffState.PickedUp or StaffState.Resting);
}

/// <summary>A job applicant in the hiring pool; leaves the pool at <see cref="ExpiresTick"/>.</summary>
public sealed record StaffCandidate( int Id, StaffType Type, int NameIndex, int Grade, long ExpiresTick );

/// <summary>
/// Hiring pool, employees, wages and training. Data-driven values: pool sizes, grade chances,
/// park maxima, wages (<see cref="BalanceSettings.GetMonthlyWage"/>) and <c>PoundsPerTrainingPoint</c>.
/// <b>Approximations</b> (documented in docs/ECONOMY.md): pool timing treats
/// <c>TimeBetweenStaffUpdates</c>/<c>StaffTimeoutTime</c> as seconds at normal speed, candidate grades
/// are drawn around <c>AvgGradeOf*</c>, hiring is free (the original's hire path has no debit, <c>BaseCostPerStaff</c> and
/// <c>CostPerQualityLevel</c> are never read, ORACLE.md F28), training spends a per-role monthly budget at the month's end
/// (divided over every employee of the role, ORACLE F17), and 100 training points raise one grade.
/// </summary>
public sealed class ParkStaff
{
	// [DATA:Language/English/*_NAMES.str:entry count 35]
	public const int NameTableSize = 35; // every *_NAMES.str table of the English data has 35 entries
	// [APPROX:ECON-008] 100 training points per grade (from Online_Standard.sam comments "costs 1000 to get up to grade 1") — evidence needed: capture of a training run
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

	// [APPROX:ECON-010] TimeBetweenStaffUpdates/StaffTimeoutTime are seconds at normal speed — evidence needed: capture of pool refresh timing
	public long UpdateInterval => Math.Max( 1, ParkCalendar.SecondsToTicks( settings.TimeBetweenStaffUpdates ) );
	public long CandidateLifetime => Math.Max( 1, ParkCalendar.SecondsToTicks( settings.StaffTimeoutTime ) );

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
		NextPoolUpdateTick = tick + UpdateInterval;
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
		if ( tick < NextPoolUpdateTick )
			return;
		NextPoolUpdateTick = tick + UpdateInterval;
		candidates.RemoveAll( candidate => candidate.ExpiresTick <= tick );
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

	private StaffCandidate CreateCandidate( StaffRoleSettings role, DeterministicRandom random, long tick )
	{
		// [BIN:STP-PPC:0x100F5B64 staff candidate] grade = (AvgGradeOf* + random % 3 - 1) & 0xFF, stored as 4 when above 4 (so -1 wraps to 4); ChanceToGetGreat* is never read
		var raw = (role.AverageGrade + random.Next( 3 ) - 1) & 0xFF;
		var grade = raw > BalanceSettings.GradeCount - 1 ? BalanceSettings.GradeCount - 1 : raw;
		return new StaffCandidate( NextId++, role.Type, random.Next( NameTableSize ), grade, tick + CandidateLifetime );
	}

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

	/// <summary>Adds an employee taken from an original save (not hired from the pool).</summary>
	internal StaffMember AddImported( StaffType type, int grade, int trainingPoints, float happiness, StaffState state, long hiredTick, float energy = StaffVitals.Maximum )
	{
		var member = new StaffMember { Id = AllocateId(), Type = type, NameIndex = 0, Grade = grade, TrainingPoints = trainingPoints, Happiness = happiness, Energy = energy, State = state, HiredTick = hiredTick };
		members.Add( member );
		return member;
	}

	/// <summary>Moves the pool's update and expiry times by <paramref name="ticks"/> (a new park started at a later turn).</summary>
	internal void ShiftSchedule( long ticks )
	{
		NextPoolUpdateTick += ticks;
		for ( var index = 0; index < candidates.Count; index++ )
			candidates[index] = candidates[index] with { ExpiresTick = candidates[index].ExpiresTick + ticks };
	}
}
