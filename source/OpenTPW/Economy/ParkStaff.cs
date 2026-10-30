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
	/// <summary>
	/// The member's thing id, the slot of the park's thing pool its person constructor took when the member was placed (tp.exe 0x0050AFE0 → 0x00516270), 0 in an economy without a pool.
	/// <see cref="Id"/> stays the roster's own counter.
	/// </summary>
	public int ThingId { get; internal set; }
	/// <summary>The id the thing-keyed rules read: the member's thing id, or the roster id where there is no pool.</summary>
	public int PhaseId => ThingId != 0 ? ThingId : Id;
	public required StaffType Type { get; init; }
	public required int NameIndex { get; init; }
	/// <summary>The sprite variant the member was hired with (the candidate's, tp.exe member +0x20); -1 for a member of an older OpenTPW park save from before the variant was kept (an original save's member takes the saved mSpriteID, tp.exe 0x4F8B10), whose bank derives from its name and id.</summary>
	public int Variant { get; init; } = -1;
	/// <summary>
	/// The sprite draw as the original stores it (tp.exe member <c>+0x20</c>, the person's <c>mSpriteID</c>): <c>(R &gt;&gt; 2)</c> reduced by the category's LOADED bank count when the candidate was made (0x00541F70) and
	/// again by the count of every load (0x004F8B10 at 0x004F93AA), so a park made on a preset that loads fewer banks keeps only those; raw while no banks are loaded (a headless run), and -1 for a member of a
	/// save from before it was kept. Presentation only: not hashed, so peers with other presets keep one world; the bank drawn is this modulo the banks loaded now.
	/// </summary>
	public int SpriteDraw { get; internal set; } = -1;
	/// <summary>
	/// The member's own name, stored inline in an original save (the staff suffix's <c>mName[i]</c>, +0x1A0, which the employee panels show); null
	/// for a member hired in OpenTPW, whose name is <see cref="NameIndex"/> in the role's name table.
	/// </summary>
	public string? Name { get; internal set; }
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
	/// <summary>The entry number the member's thing held on its cell's chain when a save was taken (<see cref="StaffCellChain"/>), which the world gives it when it files the thing again; 0 when none.</summary>
	internal long CellSerial { get; set; }

	// Presentation of the walk (not hashed): the point at the start of the member's last turn update (+0x190/+0x194, 16.16) and the tick it was taken, and the drawn point between the turns
	// (tp.exe 0x004F9F00)
	internal int TurnFromX, TurnFromY;
	internal long TurnTick = -1;
	/// <summary>Where the member is drawn, in cells; NaN until the staff world has updated him once.</summary>
	internal float DrawX = float.NaN;
	internal float DrawY = float.NaN;
	/// <summary>
	/// The member's sprite wrapper and source (tp.exe: the person's words +0xC to +0x14, <see cref="StaffSpriteState"/>): the walk step and the state setters queue templates and intervals on it, the
	/// pre-update applies them and the sprite scheduler (the even 31 ms substeps) steps the source. Saved in the park's resume file only (the staff world's actors), not hashed.
	/// </summary>
	internal StaffSpriteState Sprite { get; } = new();
	/// <summary>The direction of the member's last step (unit vector in cells), as the sprite's facing reads it.</summary>
	internal float HeadingX, HeadingY = 1;
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
	private int baseSpeed = 60;
	/// <summary>Whether a constructor or saved world state supplied the speed; legacy members without either take the grade fallback once when placed.</summary>
	internal bool HasInitializedBaseSpeed { get; private set; }
	/// <summary>Base speed [+0xc0], supplied by the constructor or saved world state; creating a navigation state must not replace it.</summary>
	public int BaseSpeed
	{
		get => baseSpeed;
		set { baseSpeed = value; HasInitializedBaseSpeed = true; }
	}
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
	/// <summary>
	/// The second world draw the entertainer's search took when it started a performance (tp.exe <c>0x004D47B5</c>): the performance's script is 13 + (draw mod (n - 1)) of the bank's n entries
	/// (13 when n is 1, <c>0x004D47CC</c>). Saved only while it is not 0.
	/// </summary>
	public uint PerformanceDraw { get; set; }
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
	/// <summary>
	/// The walk mask (+0x188, saved as the navigation's nav_mode) the member is routed again with: 1 lets the walk leave the paths, as a guard's escort to the ticket booth does (0x004D6977); null: 0.
	/// Taken with <see cref="ResumeDestination"/> when a world places the member.
	/// </summary>
	public int? ResumeMask { get; set; }
	/// <summary>The turn an original save's claim of this member's ride or toilet was made (<c>+0x60</c>); the world that places him takes the claim again with it. Not saved: a member placed later claims at the current turn.</summary>
	public long? ResumeClaimTurn { get; set; }
}

/// <summary>A job applicant in the hiring pool; leaves the pool at <see cref="ExpiresTick"/>.</summary>
/// <param name="Variant">The sprite variant (tp.exe candidate +9, <c>StaffSpriteRules.BankIndex</c> picks the bank): drawn when the candidate is made, or the saved sub-type of an original save's pool; -1 for a candidate of a save from before the variant was kept, whose bank derives from its name and id.</param>
/// <param name="SpriteDraw">The draw as the original stores it, reduced by the loaded bank count (<see cref="StaffMember.SpriteDraw"/>); null for a candidate of a save from before it was kept. Not hashed.</param>
public sealed record StaffCandidate( int Id, StaffType Type, int NameIndex, int Grade, long ExpiresTick, int Variant = -1, [property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] int? SpriteDraw = null );

/// <summary>
/// Hiring pool, employees, wages and training. Data-driven values: pool sizes, grade chances,
/// park maxima, wages (<see cref="BalanceSettings.GetMonthlyWage"/>) and <c>PoundsPerTrainingPoint</c>.
/// Pool timing counts <c>TimeBetweenStaffUpdates</c>/<c>StaffTimeoutTime</c> in units of four park turns (tp.exe 0x5084f0).
/// <b>Approximations</b> (documented in docs/ECONOMY.md): candidate grades
/// are drawn around <c>AvgGradeOf*</c>, hiring is free (the original's hire path has no debit, <c>BaseCostPerStaff</c> and
/// <c>CostPerQualityLevel</c> are never read, ORACLE.md F28), training spends a per-role monthly budget at the month's end
/// (divided over every employee of the role, ORACLE F17), and 100 training points raise one grade.
/// </summary>
public sealed partial class ParkStaff
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

	private ThingPool? things;

	/// <summary>The host clock sample the sprite sources are created and stepped at (<see cref="GuestSimulation.BodySpriteSampleAtTick"/>, set by the level every tick; 0 without a level).</summary>
	internal int SpriteClockMilliseconds { get; set; }

	/// <summary>The sprite banks a performance template is resolved against and the stored sprite draws are reduced by (the level's loaded staff sprites); null in a headless run.</summary>
	internal IStaffSpriteBanks? SpriteBanks { get; private set; }

	/// <summary>
	/// The loaded banks of this park's level: the stored sprite draws of the members and the pool take the loaded count of their category again, as every load does (tp.exe <c>0x004F8B10</c> reduces the id
	/// read from a save by <c>0x00541F60</c>, the category's loaded count). The level runs it when it loads the banks and again when a load replaced the economy.
	/// </summary>
	// [BIN:TPW-EXE:0x004F93AA sprite id reduction] the person serializer stores mSpriteID % the category's loaded bank count (0x00541F60 reads the table entry 0x00763F90 that 0x00541E7D filled from the loader's result): a count below the one the park was made at wraps the draw for good
	internal void AttachSpriteBanks( IStaffSpriteBanks? banks )
	{
		SpriteBanks = banks;
		if ( banks == null )
			return;
		foreach ( var member in members )
			if ( member.SpriteDraw >= 0 && banks.BankCount( member.Type ) is > 0 and var count )
				member.SpriteDraw %= count;
		for ( var index = 0; index < candidates.Count; index++ )
			if ( candidates[index] is { SpriteDraw: >= 0 } candidate && banks.BankCount( candidate.Type ) is > 0 and var count )
				candidates[index] = candidate with { SpriteDraw = candidate.SpriteDraw % count };
	}

	/// <summary>The sprite draw a world draw gives: <c>R &gt;&gt; 2</c> reduced by the category's loaded bank count (raw while no banks are loaded).</summary>
	// [BIN:TPW-EXE:0x00541F70 sprite draw] (R >> 2) % the category's loaded count (the divisor is the table entry 0x00763F90 of 0x00541F60), returned to the constructors, which store it (0x004FB1B5 for a guest, 0x00507600 for a candidate)
	private int SpriteDrawFrom( StaffType type, uint draw ) =>
		SpriteBanks?.BankCount( type ) is > 0 and var count ? (int)((draw >> 2) % (uint)count) : (int)(draw >> 2);

	/// <summary>The park's thing pool, once attached (<see cref="AttachThings"/>): a hired member takes a slot as its thing id and a dismissed one's slot is freed at the turn's end.</summary>
	public ThingPool? Things => things;

	/// <summary>The thing model of a role's person thing (the saved model numbers 4 to 8).</summary>
	public static SaveThingModel ThingModelOf( StaffType type ) => type switch
	{
		StaffType.Mechanic => SaveThingModel.Mechanic,
		StaffType.Handyman => SaveThingModel.Handyman,
		StaffType.Entertainer => SaveThingModel.Entertainer,
		StaffType.Guard => SaveThingModel.Guard,
		_ => SaveThingModel.Researcher
	};

	/// <summary>
	/// Attaches the park's thing pool. Each member already employed keeps the thing id it has (its slot is taken in the pool when the pool does not hold it yet) or, with none, takes the
	/// next free slot.
	/// </summary>
	/// <exception cref="InvalidDataException">A member's thing id is held by a thing of another model, or by another member.</exception>
	internal void AttachThings( ThingPool pool )
	{
		ArgumentNullException.ThrowIfNull( pool );
		things = pool;
		var seen = new HashSet<int>();
		foreach ( var member in members )
		{
			AdoptThing( member );
			if ( member.ThingId != 0 && !seen.Add( member.ThingId ) )
				throw new InvalidDataException( $"Staff thing id {member.ThingId} belongs to two members." );
		}
	}

	private void AdoptThing( StaffMember member )
	{
		if ( things == null )
			return;
		var model = ThingModelOf( member.Type );
		if ( member.ThingId == 0 )
		{
			member.ThingId = things.Allocate( model );
			return;
		}
		if ( things.IsUsed( member.ThingId ) ? things.ModelOf( member.ThingId ) != model : !things.Occupy( member.ThingId, model ) )
			throw new InvalidDataException( $"Staff member {member.Id}'s thing id {member.ThingId} is not a free {model} slot of the thing pool." );
	}

	/// <summary>The five roles' strike levels, flags and last-considered turns (the staff manager's per-role records, ECON-014).</summary>
	public StaffStrikeBoard Strikes { get; } = new();

	/// <summary>Renames this exact current member without changing its name-table index, variant or simulation state.</summary>
	// [BIN:TPW-EXE:0x506830 staff name] the inline field accepts up to 32 UTF-16 units, including an explicit blank name.
	public bool Rename( StaffMember member, string name )
	{
		if ( !members.Contains( member ) || !IsValidName( name ) ) return false;
		member.Name = name;
		return true;
	}

	/// <summary>Whether <paramref name="name"/> is a name <see cref="Rename"/> takes: at most 32 UTF-16 units, no control character and no lone surrogate. It writes nothing.</summary>
	// [BIN:TPW-EXE:0x506830 staff name] the inline field accepts up to 32 UTF-16 units, including an explicit blank name.
	public static bool IsValidName( string? name )
	{
		if ( name == null || name.Length > StaffMember.MaximumNameLength ) return false;
		for ( var index = 0; index < name.Length; index++ )
		{
			if ( char.IsControl( name[index] ) ) return false;
			if ( !char.IsSurrogate( name[index] ) ) continue;
			if ( !char.IsSurrogatePair( name, index ) ) return false;
			index++;
		}
		return true;
	}

	public IReadOnlyList<StaffMember> Members => members;
	public IReadOnlyList<StaffCandidate> Candidates => candidates;
	public int NextId { get; private set; } = 1;
	public long NextPoolUpdateTick { get; private set; }
	/// <summary>
	/// The candidate whose Place tool is running (the original's "being placed" flag, candidate <c>+0xb</c>, tp.exe 0x507bf0): it stays in the pool,
	/// is not hired yet and does not expire while the player places it. Not saved: a pending hire is only a flag. The local player's (seat 0) of <see cref="PendingHires"/>.
	/// </summary>
	public int PendingPlacementId { get => PendingHireOf( 0 ); set => SetPendingHire( 0, value ); }

	/// <summary>
	/// [EXT:online-coop-commands] Every seat's pending hire, lowest seat first (C4c): the candidate each player's Place tool places, reserved for that seat, so no other seat may begin
	/// or place it and the pool keeps it. A game of one holds seat 0's alone (<see cref="PendingPlacementId"/>). Not saved; a join snapshot and the session digest carry them.
	/// </summary>
	public IReadOnlyDictionary<int, int> PendingHires => pendingHires;
	private readonly SortedDictionary<int, int> pendingHires = new();

	/// <summary>[EXT:online-coop-commands] The candidate seat <paramref name="seat"/> places, or 0 (C4c).</summary>
	public int PendingHireOf( int seat ) => pendingHires.TryGetValue( seat, out var candidate ) ? candidate : 0;

	/// <summary>[EXT:online-coop-commands] Sets the candidate seat <paramref name="seat"/> places; 0 or less ends it without a tool exit (C4c).</summary>
	internal void SetPendingHire( int seat, int candidate )
	{
		if ( candidate > 0 )
			pendingHires[seat] = candidate;
		else
			pendingHires.Remove( seat );
	}

	/// <summary>[EXT:online-coop-commands] The seat whose pending hire is candidate <paramref name="candidate"/>, or null (C4c).</summary>
	public int? ReservedBy( int candidate )
	{
		foreach ( var (seat, placing) in pendingHires )
			if ( placing == candidate )
				return seat;
		return null;
	}

	/// <summary>[EXT:online-coop-commands] Restores the pending hires a join snapshot carries, seat by seat (C4c): they replace every seat's.</summary>
	internal void RestorePendingHires( IEnumerable<(int Seat, int Candidate)> rows )
	{
		pendingHires.Clear();
		foreach ( var (seat, candidate) in rows )
			SetPendingHire( seat, candidate );
	}

	/// <summary>
	/// A candidate an original save held with its on-pointer byte set (<c>+0xB</c>) and no carry tool to end it: it does not expire and stays in the pool until the next tool exit clears the flag
	/// (<see cref="EndPlacement"/>). 0 when none. Not saved with an OpenTPW park: a resumed park keeps the candidate as an ordinary one.
	/// </summary>
	// [BIN:TPW-EXE:0x5084f0 staff pool update] the expiry loop skips a candidate whose +0xB is set (it tests +0xB == 0 before it clears the valid byte), and the census counts it as valid; [BIN:TPW-EXE:0x5083b0 tool exit] every flagged candidate loses the flag, and with a 0 argument (the placement was made) its valid byte too
	public int StaleOnPointerId { get; internal set; }

	/// <summary>
	/// A placement tool ended (<c>0x5083b0</c>): the pending candidate is no longer being placed and a candidate the load left flagged loses its flag; when <paramref name="placed"/> the flagged candidate is
	/// removed from the pool (the placed one is hired by its caller). The local player's (seat 0) tool.
	/// </summary>
	public void EndPlacement( bool placed ) => EndPlacement( 0, placed );

	/// <summary>
	/// [EXT:online-coop-commands] Seat <paramref name="seat"/>'s placement tool ended (C4c): its pending candidate is no longer being placed, and the flag of a candidate the load left flagged goes as at any
	/// tool exit; when <paramref name="placed"/> the flagged candidate leaves the pool unless another seat places it, whose own end then frees it as any.
	/// </summary>
	public void EndPlacement( int seat, bool placed )
	{
		var placing = PendingHireOf( seat );
		pendingHires.Remove( seat );
		// the candidate being hired goes through the hire path, not this one
		if ( StaleOnPointerId != 0 && placed && StaleOnPointerId != placing && ReservedBy( StaleOnPointerId ) == null )
			candidates.RemoveAll( candidate => candidate.Id == StaleOnPointerId );
		StaleOnPointerId = 0;
	}

	/// <summary>Park turns per unit of <c>TimeBetweenStaffUpdates</c> and <c>StaffTimeoutTime</c>: the pool compares elapsed turns with four times them.</summary>
	public const int TurnsPerPoolUnit = 4;
	// [BIN:TPW-EXE:0x5084f0 staff pool update] a candidate whose turns since its stamp (+0xc, 0x41a970) exceed 4 × its timeout (+0x10) leaves unless it is being placed; the pool refills once the turns since its own stamp exceed 4 × TimeBetweenStaffUpdates (0x785304)
	/// <summary>Ticks from one pool refill to the next: the refill comes on the first turn past 4 × <c>TimeBetweenStaffUpdates</c> turns.</summary>
	public long UpdateInterval => ParkCalendar.TickOfTurn( TurnsPerPoolUnit * (long)Math.Max( 0, settings.TimeBetweenStaffUpdates ) + 1 );

	/// <summary>The first tick of the turn after <paramref name="units"/> pool units from the turn of <paramref name="tick"/>.</summary>
	private static long TickAfterUnits( long tick, long units ) => ParkCalendar.TickOfTurn( ParkCalendar.Turn( tick ) + TurnsPerPoolUnit * units + 1 );

	public IEnumerable<StaffMember> OfType( StaffType type ) => members.Where( member => member.Type == type );

	public long GetTrainingBudget( StaffType type ) => trainingBudget[(int)type];

	/// <summary>
	/// Whether any training budget has ever been set above 0 (the staff HQ's <c>mHaveEverTrained</c>, the byte at <c>+0x20</c> of the thing <c>0x519450</c> returns). It stays set once set: setting a budget back to 0 leaves it,
	/// and only the advisor's "train your staff" line reads it (<c>0x59FB10</c>).
	/// </summary>
	public bool HaveEverTrained { get; private set; }

	// [BIN:TPW-EXE:0x004B25D0 training budget window] the window stores the budget (a negative one as 0) and sets the HQ's byte +0x20 to 1 only when the value is above 0
	public void SetTrainingBudget( StaffType type, long monthlyBudget )
	{
		trainingBudget[(int)type] = Math.Max( 0, monthlyBudget );
		if ( monthlyBudget > 0 )
			HaveEverTrained = true;
	}

	/// <summary>Restores the flag of a loaded park: from the original save's staff HQ (<c>mHaveEverTrained</c>) or from an OpenTPW save.</summary>
	internal void RestoreHaveEverTrained( bool haveEverTrained ) => HaveEverTrained = haveEverTrained;

	/// <summary>Skill shown for an employee: 20 × (grade + training percentage / 100), truncated, in single precision as in the original (0x100F41DC).</summary>
	public static int StaffSkill( StaffMember member ) => (int)(20f * (float)(member.Grade + member.TrainingPoints / 100.0));

	public long MonthlyWage( StaffMember member ) => settings.GetMonthlyWage( member.Type, member.Grade );

	public long TotalMonthlyWages => members.Sum( MonthlyWage );

	/// <summary>Fills the initial pool with <c>StaffPoolInfo.BeginningNumberOf*</c> candidates per role.</summary>
	public void CreateInitialPool( WorldRng random, long tick )
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
	public void UpdatePool( WorldRng random, long tick )
	{
		// [EXT:online-coop-commands] every seat's pending hire stays, as the one being placed does (C4c)
		candidates.RemoveAll( candidate => candidate.ExpiresTick <= tick && ReservedBy( candidate.Id ) == null && candidate.Id != StaleOnPointerId );
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

	private StaffCandidate CreateCandidate( StaffRoleSettings role, WorldRng random, long tick )
	{
		// [BIN:TPW-EXE:0x0050761C staff candidate draws] the constructor 0x00507600 draws, in order: the sprite variant (0x00541F70: (R >> 2) mod the role category's loaded bank count, candidate +9; here ParkStaff.SpriteDrawFrom takes the loaded count of the level's banks, the raw draw in a headless run, and VariantFrom the folder's count for the hashed variant), one draw it discards (0x0050762D), the grade (0x00507638), the name (0x00507580) and the timeout
		// [BIN:TPW-EXE:0x00541F70 sprite variant] (draw >> 2) mod the role category's LOADED bank count (table entry +8, filled by 0x541E7D from 0x541310 after the limit): StaffCandidate.SpriteDraw, handed on to the member it becomes (Hire) and reduced again by the loaded count of every load (AttachSpriteBanks); the hashed Variant below is the same draw modulo the folder's own bank count (StaffSprites.Banks), so peers with other presets keep one world hash
		var variantDraw = random.NextWord();
		random.NextWord();
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
		return new StaffCandidate( NextId++, role.Type, name, grade, TickAfterUnits( tick, timeout ), VariantFrom( role.Type, variantDraw ), SpriteDrawFrom( role.Type, variantDraw ) );
	}

	/// <summary>The variant a draw gives: (draw >> 2) modulo the static bank count of the role's folder, not the loaded count: the hashed variant (the stored draw, which the loaded count reduces, is <see cref="SpriteDrawFrom"/>).</summary>
	private int VariantFrom( StaffType type, uint draw ) => StaffSpriteRules.Banks( type, settings.Theme )?.Banks.Count is > 0 and var count ? (int)((draw >> 2) % (uint)count) : -1;

	/// <summary>Whether a pool candidate or an employee of the role already has the name (tp.exe <c>0x005083F0</c>).</summary>
	private bool NameTaken( StaffType type, int nameIndex ) =>
		candidates.Any( candidate => candidate.Type == type && candidate.NameIndex == nameIndex ) || members.Any( member => member.Type == type && member.NameIndex == nameIndex );

	public bool CanHire( StaffType type ) => OfType( type ).Count() < settings[type].MaximumInPark;

	// [BIN:STP-PPC:0x100F61D0 hiring is free] the hire button's handler queues a command (0xf61d0, executor 0x6c034, thing factory 0xb882c) and nothing on that path debits the bank: the 16 direct callers of Spend 0xcbfdc include no hire routine, and BaseCostPerStaff (+700) and CostPerQualityLevel (+704) are never read; the wage is paid at the month's end and dismissing one costs one more (ParkEconomy.Fire, ORACLE.md F28)
	public StaffMember Hire( int candidateId, long tick, WorldRng? random = null )
	{
		var candidate = candidates.FirstOrDefault( item => item.Id == candidateId ) ?? throw new InvalidOperationException( $"No staff candidate {candidateId}." );
		if ( !CanHire( candidate.Type ) )
			throw new InvalidOperationException( $"The park already employs the maximum of {settings[candidate.Type].MaximumInPark} {candidate.Type} staff." );
		// [BIN:TPW-EXE:0x0046C8E0 staff placement] the hire's thing exists only after the placing click: the role's person constructor (0x4D9EB0, 0x4D6B60, 0x4D4340, 0x4D5D40, 0x502600) takes its thing id from the pool (0x504B10 → 0x4F8940 → 0x50AFE0)
		var thingId = things?.Allocate( ThingModelOf( candidate.Type ) ) ?? 0;
		if ( things != null && thingId == 0 )
			throw new InvalidOperationException( $"The thing pool is full ({ThingPool.Capacity} things); no staff member can be placed." );
		// [BIN:TPW-EXE:0x004F89E1 staff base speed draw] the person base constructor 0x004F8940, which every staff constructor (0x00504B10) runs right after taking the thing id, draws R mod 5 from the world generator; the draw is consumed and not kept: the staff constructor's follow-up 0x00504B90 overwrites +0xC0 with 60 below grade 3 and 100 from grade 3 (0x00504C20..0x00504C42), and the entertainer constructor then sets 60 (0x004D43EE); no staff constructor reseeds (0x004F7B7E is the online avatar's)
		// [BIN:TPW-EXE:0x004D5E02 guard variant] the guard constructor draws its own sprite variant ((R >> 2) mod the guard bank count, 0x00541F70) instead of taking the candidate's
		// a pool-less (headless) economy has no person things, so it takes no draw
		var baseSpeed = candidate.Type == StaffType.Entertainer || candidate.Grade < 3 ? GuestSpeed.BaseSpeeds[0] : GuestSpeed.BaseSpeeds[2];
		var variant = candidate.Variant;
		var spriteDraw = candidate.SpriteDraw ?? -1;
		if ( things != null && random != null )
		{
			random.NextWord();
			if ( candidate.Type == StaffType.Guard )
			{
				var guardDraw = random.NextWord();
				variant = VariantFrom( StaffType.Guard, guardDraw );
				spriteDraw = SpriteDrawFrom( StaffType.Guard, guardDraw );
			}
		}
		MaterializeRoleOrder();
		candidates.Remove( candidate );
		var member = new StaffMember { Id = candidate.Id, ThingId = thingId, Type = candidate.Type, NameIndex = candidate.NameIndex, Grade = candidate.Grade, HiredTick = tick, Variant = variant, SpriteDraw = spriteDraw };
		if ( things != null && random != null )
			member.BaseSpeed = baseSpeed;
		members.Add( member );
		roleOrder![(int)member.Type].Insert( 0, member.Id );
		if ( member.Type == StaffType.Researcher )
			ResearchersChanged();
		return member;
	}

	private bool labActive;

	/// <summary>The research lab's active byte (<c>mLabActive</c>, the ResearchLab thing's <c>+0x1468</c>): set while the park employs a researcher, as the researchers' hires and removals leave it.</summary>
	public bool LabActive => labActive;

	/// <summary>
	/// Raised when <see cref="LabActive"/> changes through a hire or a removal, with the message the original posts: 5 when the lab becomes active, 6 when it stops. The original delivers the message at once
	/// to the things subscribed to its type (the advisor subscribes to 5 only, <c>0x00599EDB</c>; nobody subscribes to 6); OpenTPW's advisor has no reaction to it.
	/// </summary>
	public event Action<int>? LabMessage;

	/// <summary>
	/// The researchers' flag update (tp.exe <c>0x005041F0</c>), run by a researcher's constructor after its search (<c>0x00502742</c>) and by its removal (<c>0x00502952</c>): with a researcher in the list
	/// (the word game +0x1DA742) the byte is set and message 5 posted when it was clear (<c>0x00504090</c>), with none it is cleared and message 6 posted when it was set (<c>0x00504140</c>). It takes no draw.
	/// </summary>
	// [BIN:TPW-EXE:0x005041F0 researchers flag] the byte +0x1468 of the ResearchLab thing (the object 0x519550 returns) is tested first, so the message goes out on a change only: 0x40F630 builds a plain message of the type 5 or 6 and 0x40FB10 delivers it through the lazily created bus 0x788D3C
	private void ResearchersChanged()
	{
		var any = members.Any( member => member.Type == StaffType.Researcher );
		if ( any && !labActive )
		{
			labActive = true;
			LabMessage?.Invoke( 5 );
		}
		else if ( !any && labActive )
		{
			labActive = false;
			LabMessage?.Invoke( 6 );
		}
	}

	public StaffMember Fire( int staffId )
	{
		var member = members.FirstOrDefault( item => item.Id == staffId ) ?? throw new InvalidOperationException( $"No employee {staffId}." );
		MaterializeRoleOrder();
		members.Remove( member );
		roleOrder![(int)member.Type].Remove( member.Id );
		// [BIN:TPW-EXE:0x00505790 staff removal] a dismissed member's thing is destroyed through the destroy request (0x0050B780 → 0x00516310): its slot is freed at the turn's end
		if ( things != null && member.ThingId != 0 )
			things.QueueDestroy( member.ThingId );
		if ( member.Type == StaffType.Researcher )
			ResearchersChanged();
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
		pendingHires.Clear();
		if ( things != null )
			foreach ( var old in members )
				if ( old.ThingId != 0 )
					things.Release( old.ThingId );
		members.Clear();
		members.AddRange( restoredMembers );
		// a restored park's lab flag is what its researchers make of it (the original's flag byte agrees with the list after every hire and removal)
		labActive = members.Any( member => member.Type == StaffType.Researcher );
		// Legacy saves resolve against the current pool only when read; attachment never replaces an explicit order.
		roleOrder = null;
		if ( things != null )
			AttachThings( things );
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

	/// <summary>Adds an employee taken from an original save (not hired from the pool); <paramref name="variant"/> is the sprite variant its person record saves reduced by the folder's bank count and <paramref name="spriteDraw"/> the id as saved (-1: none).</summary>
	internal StaffMember AddImported( StaffType type, int grade, int trainingPoints, float happiness, StaffState state, long hiredTick, float energy = StaffVitals.Maximum, string? name = null, int thingId = 0, int variant = -1, int spriteDraw = -1 )
	{
		var member = new StaffMember { Id = AllocateId(), Variant = variant, SpriteDraw = spriteDraw, ThingId = thingId, Type = type, NameIndex = 0, Name = name, Grade = grade, TrainingPoints = trainingPoints, Happiness = happiness, Energy = energy, State = state, HiredTick = hiredTick };
		AdoptThing( member );
		MaterializeRoleOrder();
		members.Add( member );
		roleOrder![(int)member.Type].Insert( 0, member.Id );
		labActive |= member.Type == StaffType.Researcher;
		return member;
	}

	/// <summary>
	/// Replaces the pool with an original save's candidates (role, name index, grade, the turn it was made and its timeout in units of four turns) and
	/// sets the next refill from the turn of the pool's last one; each candidate leaves on the first turn past its timeout.
	/// </summary>
	internal void RestoreOriginalPool( IEnumerable<(StaffType Type, int NameIndex, int Grade, long MadeTurn, long Timeout, int SubType)> saved, long lastRefillTurn, IReadOnlyList<bool>? onPointer = null )
	{
		candidates.Clear();
		pendingHires.Clear();
		StaleOnPointerId = 0;
		var position = 0;
		foreach ( var (type, name, grade, made, timeout, subType) in saved.Take( PoolSlots ) )
		{
			// [BIN:TPW-EXE:0x00507600 staff candidate] the sub-type (+9) is the sprite variant the placement object takes when the candidate is picked up (0x507BF0); one past the role's banks is no variant this install can draw
			var banks = StaffSpriteRules.Banks( type, settings.Theme )?.Banks.Count ?? 0;
			var variant = subType >= 0 && subType < banks ? subType : -1;
			candidates.Add( new StaffCandidate( AllocateId(), type, name, grade, TickAfterUnits( ParkCalendar.TickOfTurn( made ), timeout ), variant, variant < 0 ? null : SpriteBanks?.BankCount( type ) is > 0 and var loaded ? variant % loaded : variant ) );
			// the carry tool holds one candidate: the first flagged one is kept by the flag
			if ( StaleOnPointerId == 0 && onPointer != null && position < onPointer.Count && onPointer[position] )
				StaleOnPointerId = candidates[^1].Id;
			position++;
		}
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
