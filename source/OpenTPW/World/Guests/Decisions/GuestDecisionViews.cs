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

	/// <summary>The same trait for a guest known by its birth id only.</summary>
	public ulong Trait( ulong birthId, int traitIndex ) => SmarterGuestsState.Trait( ParkSeed, birthId, traitIndex );
}

/// <summary>
/// A guest as the policy sees it: a copy of the fields the decisions read, taken when the host asks. The two histories are read-only wrappers of the guest's arrays (they cannot be cast back to an
/// array), which the host does not touch while a policy call runs.
/// </summary>
/// <param name="Id">The guest's thing id.</param>
/// <param name="BirthId">The smarter-guests extension's birth id, 0 while the guest has none (the extension is off, or its first tick has not run).</param>
/// <param name="MealTurn">
/// The park turn of the guest's last meal (a use of an attraction with a hunger effect), or -1 for none. Filled by the host only for a policy that <see cref="IGuestDecisions.UsesPlanning"/>, else -1.
/// Additive, phase 5.
/// </param>
/// <param name="PlanTurns256">
/// The turns, in 1/256 park turn, before this guest could next serve a need if it committed to a ride now: the shortest expected wait plus cycle of a ride it could choose (<see cref="AttractionView.CycleTurns256"/>),
/// or, in the minor decision, those of the ride it is walking to; -1 when the host did not look (the policy does not plan, or no meter is near a level it plans for) or there is no such ride. Additive, phase 5.
/// </param>
/// <param name="ComplaintTurn">
/// The park turn of the guest's latest complaint about its path (<see cref="PathDecision"/>), or -1 for none. Filled by the host only for a policy that <see cref="IGuestDecisions.UsesPathComfort"/>, else -1. Additive, phase 6.
/// </param>
public readonly record struct GuestView( int Id, ulong BirthId, int Type, float Happiness, float Thirst, float Hunger, float Toilet, float Nausea, int Money, int CellX, int CellY,
	IReadOnlyList<int> UsedHistory, IReadOnlyList<int> TemporaryHistory, long MealTurn = -1, long PlanTurns256 = -1, long ComplaintTurn = -1 );

/// <summary>
/// An attraction as the policy sees it, built by the host for one guest: every value the original score reads, precomputed. A policy that opts into
/// <see cref="IGuestDecisions.ScoresDisconnectedQueues"/> also receives these facts for a real typed queue back without a join cell. Other disconnected views retain identity and flags only.
/// </summary>
/// <param name="HasJoinCell">Whether the queue has an attached walkable join cell; false even when an opted-in policy scores a disconnected typed back.</param>
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
/// <see cref="IGuestDecisions.UsesSpreading"/>, else 0. Also 0 in the views of the queue hooks (<see cref="IGuestDecisions.DecideQueuePatience"/>): outside a candidate loop counting the walkers is a scan of every guest, and neither queue hook reads them.
/// </param>
/// <param name="TurnsPerGuest256">
/// The attraction's observed time per guest, in 1/256 park turn (<see cref="RideThroughputs"/>: the throughput is its inverse, capacity over cycle duration): a moving average of the gaps between boardings, the cold-start value
/// without a measurement. 0 when the host was not asked, which the policy reads as "unknown".
/// </param>
/// <param name="CycleTurns256">
/// The attraction's cycle, in 1/256 park turn: its time per guest times its capacity (a ride of capacity 20 that boards a guest every 5 turns runs a cycle of 100), what a guest spends on it once it is at the front. Filled
/// with <see cref="TurnsPerGuest256"/> (a policy that <see cref="IGuestDecisions.UsesSpreading"/>), else 0. Additive, phase 5.
/// </param>
/// <param name="OnTheWay">
/// The candidate stands in the minor decision's window: a stop on the guest's way to its target (the window's rule: within 4 x 4 cells and nearer the target than the guest). Set by the host in the minor decision only, and
/// only for a policy that <see cref="IGuestDecisions.UsesPlanning"/>. Additive, phase 5.
/// </param>
public readonly record struct AttractionView( int Id, int TypeId, RideVisitorKind Kind, bool HasJoinCell, (int X, int Y) BackCell, (int X, int Y) EntranceCell, int CellModifier,
	int QueueLength, int QueueSizeInCells, uint QueueLimit, int LiveExcitement, int StaticExcitementLevel, int ThirstEffect, int HungerEffect, bool ProvidesRelief, uint AgeDays,
	bool IsIndoors, int GoldenTicketCost, int UpgradeCost, bool IsOpen, bool IsBrokenDown, bool SameTypeAsLastUsed, int Price = 0, uint Willingness = 0, int Reservations = 0, long TurnsPerGuest256 = 0,
	long CycleTurns256 = 0, bool OnTheWay = false );

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
/// <param name="BestAlternativeWait256">
/// The shortest expected wait, in 1/256 park turn, this guest would have at another ride it could actually choose next (open, working, room, admitted by its excitement gate, and not in its used or temporary history or of the type it rode last: <see cref="SmarterGuestsRules.IsUnusedAdmissibleRide"/>; <see cref="SmarterGuestsRules.ExpectedWait256"/>), or -1 when there is none
/// or the host was not asked (<see cref="IGuestDecisions.AlternativeLookAfterTurns"/>). Additive, phase 4.
/// </param>
public readonly record struct AdmissionView( AttractionView Attraction, int ExcitementLevelWord, int JoinDifference, int QueueCount, uint QueueLimit, long BestAlternativeWait256 = -1 );

/// <summary>The join's result: <see cref="QueueJoinResult.Joined"/>, or the refusal (too exciting, not exciting enough, queue too long).</summary>
public readonly record struct QueueAdmission( QueueJoinResult Result )
{
	public bool Admits => Result == QueueJoinResult.Joined;
	public static QueueAdmission Admit => new( QueueJoinResult.Joined );
}

/// <summary>
/// A queued guest on a turn the existing leave tests let it stay: when it joined, its current place (0 is the front; after a move-up the new place) and the queue it waits in. Never offered while the guest is being called forward or is in an
/// interlude.
/// </summary>
/// <param name="BestAlternativeWait256">The same as <see cref="AdmissionView.BestAlternativeWait256"/>, for a guest that has waited at least <see cref="IGuestDecisions.AlternativeLookAfterTurns"/> turns; -1 otherwise.</param>
public readonly record struct QueuePatienceView( AttractionView Attraction, long JoinTurn, int Position, int QueueCount, long BestAlternativeWait256 = -1 );

/// <summary>A guest's patience verdict: stay, or leave the queue with a thought (the host dismisses it and takes the happiness cost).</summary>
public readonly record struct QueuePatienceDecision( bool Leave, GuestThought Thought = GuestThought.None )
{
	public static QueuePatienceDecision Stay => default;
}

/// <summary>What a guest remembers when a use ends: whether the attraction enters its used history (the original always does).</summary>
public readonly record struct PostUseDecision( bool PushUsedHistory );

/// <summary>
/// One path cell as the wander's comfort rule sees it (docs/SMARTER-GUESTS.md, phase 6), counted by the host from the park's litter and its per-cell guest lists.
/// </summary>
/// <param name="X">The cell's column.</param>
/// <param name="Y">The cell's row.</param>
/// <param name="LitterUnits">The litter in the cell and the eight around it: 1 for each piece, 2 for each vomit or stinkbomb (<see cref="SmarterGuestsRules.LitterUnitsOf"/>); 0 when the park has no litter.</param>
/// <param name="Crowd">The guests standing in the cell (the asking guest included when it stands there).</param>
/// <param name="NearCrowd">The guests standing in the eight cells around it (the crowd a guest walks into on its way there).</param>
public readonly record struct PathCellView( int X, int Y, int LitterUnits, int Crowd, int NearCrowd = 0 );

/// <summary>
/// What the wander's comfort rule reads: the cell the guest stands on and the cells the original's random walk visited on its way to the proposed goal, in order, the goal itself last (a copy of cells the
/// host's search already walked: the policy chooses among candidates the host enumerated and draws nothing). A ref struct, so the cells live on the host's stack for the call.
/// </summary>
public readonly ref struct WanderPathView
{
	public WanderPathView( PathCellView here, ReadOnlySpan<PathCellView> walk )
	{
		Here = here;
		Walk = walk;
	}

	/// <summary>The cell the guest stands on.</summary>
	public PathCellView Here { get; }

	/// <summary>The walk's cells in order; the last is the original's goal (never empty when the host asks).</summary>
	public ReadOnlySpan<PathCellView> Walk { get; }
}

/// <summary>
/// The comfort rule's verdict: the index of the <see cref="WanderPathView.Walk"/> cell to walk to instead of the original's goal (-1 keeps the goal), and a complaint, if any, with the happiness it costs. The
/// host thinks the thought, takes the happiness and notes the turn (the cooldown); the sub-cell bytes of the original's goal stay.
/// </summary>
/// <param name="GoalIndex">The walk cell to go to, or -1 to keep the original's goal (the last cell).</param>
/// <param name="Complaint">What the guest complains about, or <see cref="PathComplaint.None"/>.</param>
/// <param name="Thought">The thought the complaint shows (an existing one: the original has no thought for a crowd); <see cref="GuestThought.None"/> without a complaint.</param>
/// <param name="HappinessCost">The happiness points the complaint costs, 0 without one.</param>
public readonly record struct PathDecision( int GoalIndex = -1, PathComplaint Complaint = PathComplaint.None, GuestThought Thought = GuestThought.None, float HappinessCost = 0 )
{
	public static PathDecision Keep => new( -1 );
}

/// <summary>What a guest complains about on its path (phase 6).</summary>
public enum PathComplaint : byte
{
	None = 0,
	/// <summary>Litter or vomit around it: the original's "Too messy" thought.</summary>
	Litter = 1,
	/// <summary>A crowd in its cell.</summary>
	Crowd = 2
}
