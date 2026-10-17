namespace OpenTPW;

/// <summary>
/// What every decision of <see cref="IGuestDecisions"/> may read besides its own snapshot: the simulation's settings and the current park turn. A policy never reads a clock or a
/// generator of its own.
/// </summary>
/// <param name="Settings">The guest settings (peep types, weights, booth multipliers); read only.</param>
/// <param name="ParkTurn">The park turn the decision is taken on (<see cref="GuestSimulation.ParkTurn"/>).</param>
/// <param name="Raining">Drops are falling (the weather's current drops above 0).</param>
/// <param name="ParkSeed">The seed a stateless trait derives from: the extension's saved park seed, else the simulation's seed.</param>
/// <param name="GridWidth">The park map's width in cells (0 when unknown); a wander goal must lie inside it.</param>
/// <param name="GridHeight">The park map's height in cells (0 when unknown).</param>
public readonly record struct DecisionContext( GuestSettings Settings, long ParkTurn, bool Raining, ulong ParkSeed = 0, int GridWidth = 0, int GridHeight = 0 )
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
/// <param name="Price">
/// What the guest would pay at the stand: the live price of a shop or sideshow (<see cref="PriceTerms"/>), 0 for a ride, toilet or free stand. Filled by the host only for a policy that asks for it
/// (<see cref="IGuestDecisions.UsesValueForMoney"/>), else 0.
/// </param>
/// <param name="Willingness">
/// What this guest is willing to pay for it right now (<see cref="QueueJoinRules.WillingnessToPay"/> with the guest's meters: the stand-point test's own word, computed without recording any statistic). 0 when
/// <paramref name="Price"/> is 0 or the host was not asked.
/// </param>
/// <param name="Reservations">
/// Guests other than this one walking to the attraction to join its queue (state "going to a ride" with it as their target). Filled by the host only for a policy that
/// <see cref="IGuestDecisions.UsesSpreading"/>, else 0.
/// </param>
/// <param name="TurnsPerGuest256">
/// The attraction's observed time per guest, in 1/256 park turn (<see cref="RideThroughputs"/>: the throughput is its inverse, capacity over cycle duration): a moving average of the gaps between boardings, the cold-start value
/// without a measurement. 0 when the host was not asked, which the policy reads as "unknown".
/// </param>
public readonly record struct AttractionView( int Id, int TypeId, RideVisitorKind Kind, bool HasJoinCell, (int X, int Y) BackCell, (int X, int Y) EntranceCell, int CellModifier,
	int QueueLength, int QueueSizeInCells, uint QueueLimit, int LiveExcitement, int StaticExcitementLevel, int ThirstEffect, int HungerEffect, bool ProvidesRelief, uint AgeDays,
	bool IsIndoors, int GoldenTicketCost, int UpgradeCost, bool IsOpen, bool IsBrokenDown, bool SameTypeAsLastUsed, int Price = 0, uint Willingness = 0, int Reservations = 0, long TurnsPerGuest256 = 0 );

/// <summary>
/// The booth's facts: the park points (the park's rating), the entrance fee, the draw the host took for the verdict (the park's generator is the host's) and, for a policy that asks for it
/// (<see cref="IGuestDecisions.UsesValueForMoney"/>), the number of rides and sideshows that are open and not broken (shops and toilets are not variety).
/// </summary>
public readonly record struct BoothView( uint ParkPoints, uint Fee, uint Draw, int OpenAttractions = 0 );

/// <summary>The booth's verdict on the entrance fee.</summary>
public readonly record struct BoothDecision( BoothVerdictKind Verdict );

/// <summary>
/// A guest's verdict after a decision that its price rule emptied (<see cref="IGuestDecisions.DecidePriceStall"/>): stay in the park, or go home with a thought (the host walks it to the gate and thinks the thought).
/// </summary>
public readonly record struct PriceStallDecision( bool GoHome, GuestThought Thought = GuestThought.None )
{
	public static PriceStallDecision Stay => default;
}

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
