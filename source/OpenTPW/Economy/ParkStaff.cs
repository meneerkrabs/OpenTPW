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
	// [APPROX:ECON-014] staff start at happiness 100 and it never changes (no strikes) — evidence needed: staff happiness rules (binary/captures)
	public int Happiness { get; set; } = 100;
	public StaffState State { get; set; } = StaffState.Idle;
	/// <summary>Remaining ticks of the current job (repair, cleaning), 0 when free.</summary>
	public long BusyTicks { get; set; }
	/// <summary>Object instance the employee works on, 0 when none.</summary>
	public int AssignedInstanceId { get; set; }
	public bool IsAvailable => BusyTicks == 0 && State != StaffState.OnStrike && State != StaffState.PickedUp;
}

/// <summary>A job applicant in the hiring pool; leaves the pool at <see cref="ExpiresTick"/>.</summary>
public sealed record StaffCandidate( int Id, StaffType Type, int NameIndex, int Grade, long ExpiresTick );

/// <summary>
/// Hiring pool, employees, wages and training. Data-driven values: pool sizes, grade chances,
/// park maxima, wages (<see cref="BalanceSettings.GetMonthlyWage"/>) and <c>PoundsPerTrainingPoint</c>.
/// <b>Approximations</b> (documented in docs/ECONOMY.md): pool timing treats
/// <c>TimeBetweenStaffUpdates</c>/<c>StaffTimeoutTime</c> as seconds at normal speed, candidate grades
/// are drawn around <c>AvgGradeOf*</c>, hiring is free (the meaning of <c>BaseCostPerStaff</c> and
/// <c>CostPerQualityLevel</c> is unknown), training spends a per-role monthly budget evenly at the
/// month's end, and 100 training points raise one grade.
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
		// [APPROX:ECON-009] candidate grade = average + 2 when "great", else average +-1 — evidence needed: hiring pool captures (grade distribution)
		var grade = random.Chance( role.ChanceToGetGreat ) ? role.AverageGrade + 2 : role.AverageGrade + random.Next( 3 ) - 1;
		return new StaffCandidate( NextId++, role.Type, random.Next( NameTableSize ), Math.Clamp( grade, 0, BalanceSettings.GradeCount - 1 ), tick + CandidateLifetime );
	}

	public bool CanHire( StaffType type ) => OfType( type ).Count() < settings[type].MaximumInPark;

	// [APPROX:ECON-012] hiring is free; BaseCostPerStaff/CostPerQualityLevel unused — evidence needed: capture of the balance before/after hiring
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

	/// <summary>Spends each role's training budget evenly over its employees; returns the amount spent.</summary>
	// [APPROX:ECON-013] training budget is spent evenly over a role at month end — evidence needed: capture of training budget effects
	public long Train()
	{
		long spent = 0;
		foreach ( var role in settings.Roles )
		{
			var trainees = OfType( role.Type ).Where( member => member.Grade < BalanceSettings.GradeCount - 1 && role.PoundsPerTrainingPoint[member.Grade] > 0 ).ToList();
			if ( trainees.Count == 0 || trainingBudget[(int)role.Type] <= 0 )
				continue;
			var share = trainingBudget[(int)role.Type] / trainees.Count;
			foreach ( var member in trainees )
			{
				var price = role.PoundsPerTrainingPoint[member.Grade];
				var points = (int)Math.Min( share / price, TrainingPointsPerGrade - member.TrainingPoints );
				spent += (long)points * price;
				member.TrainingPoints += points;
				if ( member.TrainingPoints >= TrainingPointsPerGrade )
				{
					member.Grade++;
					member.TrainingPoints = 0;
				}
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
}
