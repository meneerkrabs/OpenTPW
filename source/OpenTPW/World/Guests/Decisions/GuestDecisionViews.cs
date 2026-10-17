namespace OpenTPW;

/// <summary>
/// What every decision of <see cref="IGuestDecisions"/> may read besides its own snapshot: the simulation's settings and the current park turn. A policy never reads a clock or a
/// generator of its own.
/// </summary>
/// <param name="Settings">The guest settings (peep types, weights, booth multipliers); read only.</param>
/// <param name="ParkTurn">The park turn the decision is taken on (<see cref="GuestSimulation.ParkTurn"/>).</param>
/// <param name="Raining">Drops are falling (the weather's current drops above 0).</param>
/// <param name="ParkSeed">The seed a stateless trait derives from: the extension's saved park seed, else the simulation's seed.</param>
public readonly record struct DecisionContext( GuestSettings Settings, long ParkTurn, bool Raining, ulong ParkSeed = 0 )
{
	/// <summary>A stateless trait of <paramref name="guest"/> (<see cref="SmarterGuestsState.Trait(ulong, ulong, int)"/>): the same for the whole visit, no random number taken.</summary>
	public ulong Trait( in GuestView guest, int traitIndex ) => SmarterGuestsState.Trait( ParkSeed, guest.BirthId, traitIndex );
}

/// <summary>
/// A guest as the policy sees it: a copy of the fields the decisions read, taken when the host asks. The two histories are read-only wrappers of the guest's arrays (they cannot be cast back to an
/// array), which the host does not touch while a policy call runs.
/// </summary>
/// <param name="Id">The guest's thing id.</param>
/// <param name="BirthId">The smarter-guests extension's birth id, 0 while the guest has none (the extension is off, or its first tick has not run).</param>
public readonly record struct GuestView( int Id, ulong BirthId, int Type, float Happiness, float Thirst, float Hunger, float Toilet, float Nausea, int Money, int CellX, int CellY,
	IReadOnlyList<int> UsedHistory, IReadOnlyList<int> TemporaryHistory );

/// <summary>
/// An attraction as the policy sees it, built by the host for one guest: every value the original score reads, precomputed. An attraction without a join cell (<see cref="HasJoinCell"/> false)
/// carries only its identity and flags.
/// </summary>
/// <param name="BackCell">The back-of-queue cell the guest would walk to (the last queue cell).</param>
/// <param name="CellModifier">Component 4 of the environment record at <see cref="BackCell"/> (the fireworks' region effect), 0 elsewhere.</param>
/// <param name="SameTypeAsLastUsed">The attraction the guest used last still exists and is of this one's type.</param>
public readonly record struct AttractionView( int Id, int TypeId, RideVisitorKind Kind, bool HasJoinCell, (int X, int Y) BackCell, (int X, int Y) EntranceCell, int CellModifier,
	int QueueLength, int QueueSizeInCells, uint QueueLimit, int LiveExcitement, int StaticExcitementLevel, int ThirstEffect, int HungerEffect, bool ProvidesRelief, uint AgeDays,
	bool IsIndoors, int GoldenTicketCost, int UpgradeCost, bool IsOpen, bool IsBrokenDown, bool SameTypeAsLastUsed );

/// <summary>The booth's facts: the park points, the entrance fee and the draw the host took for the verdict (the park's generator is the host's).</summary>
public readonly record struct BoothView( uint ParkPoints, uint Fee, uint Draw );

/// <summary>The booth's verdict on the entrance fee.</summary>
public readonly record struct BoothDecision( BoothVerdictKind Verdict );

/// <summary>
/// The facts of a join at the back of a queue after the host's physical checks (open, not listed, room): the type's excitement word, the host's join difference, the queue's count and its
/// limit.
/// </summary>
public readonly record struct AdmissionView( AttractionView Attraction, int ExcitementLevelWord, int JoinDifference, int QueueCount, uint QueueLimit );

/// <summary>The join's result: <see cref="QueueJoinResult.Joined"/>, or the refusal (too exciting, not exciting enough, queue too long).</summary>
public readonly record struct QueueAdmission( QueueJoinResult Result )
{
	public bool Admits => Result == QueueJoinResult.Joined;
	public static QueueAdmission Admit => new( QueueJoinResult.Joined );
}

/// <summary>
/// A queued guest on a turn the existing leave tests let it stay: when it joined, its place and the queue it waits in. Never offered while the guest is being called forward or is in an
/// interlude.
/// </summary>
public readonly record struct QueuePatienceView( AttractionView Attraction, long JoinTurn, int Position, int QueueCount );

/// <summary>A guest's patience verdict: stay, or leave the queue with a thought (the host dismisses it and takes the happiness cost).</summary>
public readonly record struct QueuePatienceDecision( bool Leave, GuestThought Thought = GuestThought.None )
{
	public static QueuePatienceDecision Stay => default;
}

/// <summary>What a guest remembers when a use ends: whether the attraction enters its used history (the original always does).</summary>
public readonly record struct PostUseDecision( bool PushUsedHistory );
