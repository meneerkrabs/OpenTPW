namespace OpenTPW;

/// <summary>
/// The pure decision layer of the guests (docs/SMARTER-GUESTS.md, "A pure decision layer"). The host (<see cref="GuestSimulation"/>) keeps everything with an effect: routing, queue insertion and
/// removal, payments, statistics, thoughts, happiness changes and every <see cref="WorldRng"/> draw, so no policy can take, skip or reorder one: the interface has no generator and no effectful callback (a
/// policy never receives a delegate that routes, draws or edits). A policy only decides: each method takes snapshots or views and returns a typed decision or a comparison, reads no generator, no clock and no
/// simulation state of its own that the views do not carry, and changes nothing. A draw the original takes inside logic that moved here stays in the host, which passes the drawn value in (the booth's
/// draw, <see cref="BoothView.Draw"/>) or runs the drawing procedure itself and offers the result for the policy to accept or replace (the wander goal). The host checks every result it gets
/// (<see cref="GuestSimulation"/> throws <see cref="InvalidOperationException"/> naming the policy for an admission, a booth verdict or a wander goal that cannot be acted on).
/// </summary>
public interface IGuestDecisions
{
	/// <summary>
	/// The host may score a disconnected real typed queue back, and offer it to the major decision before testing its route. This includes newly built queues and removed connections.
	/// False preserves the existing join-cell filter and identity-only view for custom and smarter policies. The faithful policy opts in; wrappers forward their base policy's choice.
	/// This does not change <see cref="AttractionView.HasJoinCell"/>, the minor decision or the extension's planning filters.
	/// </summary>
	bool ScoresDisconnectedQueues => false;

	/// <summary>
	/// The policy belongs to the <c>opentpw.smarter-guests</c> extension: the simulation then keeps the extension's park state (<see cref="SmarterGuestsState"/>: birth ids and the park seed).
	/// A policy that wraps another forwards it.
	/// </summary>
	bool UsesExtensionState => false;

	/// <summary>
	/// The policy weighs prices (docs/SMARTER-GUESTS.md, phase 2): the host then fills <see cref="AttractionView.Price"/>, <see cref="AttractionView.Willingness"/> and
	/// counts the open rides and sideshows into <see cref="BoothView.OpenAttractions"/>, tracks the guest's run of price-emptied decisions in the extension's state and asks <see cref="PriceTurnedAway"/> and
	/// <see cref="DecidePriceStall"/>. A policy that
	/// ignores prices leaves it false and the host does none of this, so the original path does no extra work. A policy that wraps another forwards it.
	/// </summary>
	bool UsesValueForMoney => false;

	/// <summary>
	/// The policy spreads guests over the attractions (docs/SMARTER-GUESTS.md, phase 3; additive): the host then tracks each attraction's throughput in the extension's state
	/// (<see cref="SmarterGuestsState.Throughputs"/>), fills <see cref="AttractionView.Reservations"/> and <see cref="AttractionView.TurnsPerGuest256"/>, and asks <see cref="SwitchesTarget"/> in the minor decision.
	/// A policy that ignores them leaves it false and the host does none of this, so the original path does no extra work. A policy that wraps another forwards it.
	/// </summary>
	bool UsesSpreading => false;

	/// <summary>
	/// The policy plans the guest's needs (docs/SMARTER-GUESTS.md, phase 5; additive): the host then fills <see cref="GuestView.MealTurn"/> (from the extension's state, which it also writes when a guest eats),
	/// <see cref="GuestView.PlanTurns256"/> (only for a guest with a meter near a level the policy plans for, so the scan of the park's rides is not paid for the others) and, in the minor decision,
	/// <see cref="AttractionView.OnTheWay"/>. Needs <see cref="UsesSpreading"/> (the expected waits and cycles). A policy that ignores them leaves it false and the host does none of this, so the original path
	/// does no extra work. A policy that wraps another forwards it.
	/// </summary>
	bool UsesPlanning => false;

	/// <summary>
	/// The planning rule itself is on (phase 5): the host then also looks at the rides for <see cref="GuestView.PlanTurns256"/>. A policy with only the meal or detour rule leaves it false and pays nothing for the scan.
	/// Needs <see cref="UsesPlanning"/>. A policy that wraps another forwards it.
	/// </summary>
	bool UsesPlanHorizon => false;

	/// <summary>
	/// The policy weighs the comfort of the path (docs/SMARTER-GUESTS.md, phase 6; additive): the host then keeps the cells of the wander's random walk, counts the litter and the crowd on them and asks <see cref="ChoosePath"/>
	/// before <see cref="ChooseWanderGoal"/>, and fills <see cref="GuestView.ComplaintTurn"/> from the extension's state. A policy that does not leaves it false and the host does none of this, so the original path does no
	/// extra work. A policy that wraps another forwards it.
	/// </summary>
	bool UsesPathComfort => false;

	/// <summary>
	/// The policy carries litter further to a bin (phase 6b; additive): the host then asks <see cref="BinSearchRadius"/> when a guest whose litter meter is full looks for a bin. A policy that does not leaves it false
	/// and the search keeps the original's radius (<see cref="GuestSimulation.BinRadius"/>). A policy that wraps another forwards it.
	/// </summary>
	bool UsesBinCarry => false;

	/// <summary>The distance in cells within which a guest with litter looks for a bin (the original's: <see cref="GuestSimulation.BinRadius"/>, exclusive). Asked only of a policy that <see cref="UsesBinCarry"/>; the host accepts 0 to <see cref="GuestSimulation.MaxBinRadius"/>.</summary>
	int BinSearchRadius( in DecisionContext context, in GuestView guest ) => GuestSimulation.BinRadius;

	/// <summary>
	/// The longest route, in cells, to a bin the guest may walk to when the bin lies beyond the original's reach (squared distance of <see cref="GuestSimulation.BinRadius"/> squared or more): the straight-line radius says nothing
	/// about a bin across a hedge or a river. A longer route (or none) skips the candidate before any route request, so it has no side effect. A bin within the original's reach is never skipped. Asked only of a policy that
	/// <see cref="UsesBinCarry"/>; the default is no bound.
	/// </summary>
	int BinMaxRouteCells( in DecisionContext context, in GuestView guest ) => int.MaxValue;

	/// <summary>
	/// The queue patience rules look at the alternatives (additive, phase 4): -1 (the default) means the host does not look for this guest. Otherwise the host fills <see cref="AdmissionView.BestAlternativeWait256"/> at every
	/// admission and <see cref="QueuePatienceView.BestAlternativeWait256"/> once the guest, not at the front of its queue, has waited at least this many park turns (the policy returns the turn its own give-up test starts, so the
	/// scan of the park's rides is not paid before it can matter). Needs <see cref="UsesSpreading"/> (the estimates). A policy that wraps another forwards it.
	/// </summary>
	int AlternativeLookAfterTurns( in DecisionContext context, in GuestView guest ) => -1;

	/// <summary>
	/// How many park turns a queued guest must have waited before <see cref="DecideQueuePatience"/> can answer anything but what <see cref="OriginalGuestDecisions"/> answers (additive, phase 6 release work): the host skips the
	/// hook, and the views it would have built, for a guest that has waited fewer turns (a guest whose join turn is not recorded is always asked). -1, the default, is "never skip". The answer may depend on the
	/// park seed and the guest's birth id only: the host asks every turn of every queued guest and the policy keeps no state. A policy that wraps another forwards it, or answers -1 while a hook of its own may answer earlier.
	/// </summary>
	int QueuePatienceQuietTurns( in DecisionContext context, ulong birthId ) => -1;

	/// <summary>
	/// The minor decision's hysteresis (additive, phase 3): whether the best window candidate (<paramref name="candidate"/>, scoring <paramref name="candidateScore"/>) replaces the target the guest is walking to
	/// (<paramref name="target"/>, scoring <paramref name="targetScore"/> for this guest now). The host asks only a policy that <see cref="UsesSpreading"/>, after its comparison rule picked the candidate and before the
	/// route tests. The default always switches, as the original does.
	/// </summary>
	bool SwitchesTarget( in DecisionContext context, in GuestView guest, in AttractionView target, uint targetScore, in AttractionView candidate, uint candidateScore ) => true;

	/// <summary>
	/// The host calls this at the start of every simulation tick (<see cref="GuestSimulation.Tick"/> and the swept tick), before any decision of the tick. The faithful and the smarter policy
	/// hold no state and ignore it; a policy that meters work per tick (the script policy's step budget) starts its tick here. A policy that wraps another forwards it.
	/// </summary>
	void BeginTick() { }

	/// <summary>
	/// The host brackets each candidate loop of a major or minor decision with <see cref="BeginDecision"/> and <see cref="EndDecision"/> (also when the loop throws), so a policy that meters work can treat
	/// the scores of one decision as a unit instead of mixing two scales in one comparison. The script policy decides at <see cref="BeginDecision"/> whether a mod runs for the whole decision.
	/// </summary>
	void BeginDecision() { }

	/// <summary>The end of the candidate loop opened by <see cref="BeginDecision"/>.</summary>
	void EndDecision() { }

	/// <summary>
	/// Whether the policy left this candidate out of a major decision because of its price although the guest would otherwise have chosen it (additive, phase 2; the host asks only a policy that
	/// <see cref="UsesValueForMoney"/>, for a candidate that scored below the choice floor and is open, working and has room). The host counts a decision that found nothing and had such a candidate.
	/// </summary>
	bool PriceTurnedAway( in DecisionContext context, in GuestView guest, in AttractionView attraction ) => false;

	/// <summary>
	/// After a major decision that price emptied, with the number of such decisions in a row (additive, phase 2): whether the guest goes home. The host walks it to the gate with the thought, and resets
	/// the count when a guest chooses something or goes home. The default keeps the guest in the park.
	/// </summary>
	PriceStallDecision DecidePriceStall( in DecisionContext context, in GuestView guest, int consecutive ) => PriceStallDecision.Stay;

	/// <summary>The booth's verdict on the entrance fee (tp.exe <c>0x004FF9D0</c>); the host draws the verdict's word first and the expensive wait afterwards.</summary>
	BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth );

	/// <summary>The score of one attraction for one guest (<c>0xe9164</c>); the host asks for attractions with a join cell, or a real typed queue back when <see cref="ScoresDisconnectedQueues"/> is true.</summary>
	uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction );

	/// <summary>
	/// The major decision's comparison rule (<c>0xe8ff4</c>): whether a candidate with <paramref name="score"/> replaces the best so far (<paramref name="best"/>, initially 0). The original ignores a score below
	/// <see cref="AttractionScore.CandidateFloor"/> and otherwise takes a higher score, or an equal one on an odd park turn. The **host** runs the choice loop: it enumerates the candidates in the original order,
	/// scores each through <see cref="ScoreAttraction"/>, asks this rule, makes the route request itself (the one effect, and only for a candidate that beats the best) and keeps the result. No callback reaches the policy.
	/// </summary>
	bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best );

	/// <summary>
	/// The minor decision's comparison rule (<c>0x004FD570</c>) over the host's window candidates, in the host's order and with the same scores: the original takes a higher score (unsigned), or an equal one on an
	/// odd park turn. The host then tests the routes and retargets.
	/// </summary>
	bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best );

	/// <summary>Whether a guest at the back of a queue joins it (<c>0xeccb0</c>, after the room test): the excitement gate, then the queue limit.</summary>
	QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission );

	/// <summary>
	/// Queue patience, a hook the host calls after the existing leave tests of a waiting guest; the original stays. A leaving guest is dismissed by the host with the decision's thought.
	/// </summary>
	QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue );

	/// <summary>What a guest remembers of a finished use (<c>0xe9fa4</c>).</summary>
	PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride );

	/// <summary>
	/// The wander goal: the host ran the original destination search (<see cref="GuestWander.Pick(GuestPathGrid, (int X, int Y), Func{uint}, Func{int, int, bool}?)"/>, with its draws) and offers
	/// the result; the policy returns the goal the guest walks to (the original: the proposal).
	/// </summary>
	GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal );

	/// <summary>
	/// The comfort of the wander's goal (additive, phase 6; asked only of a policy that <see cref="UsesPathComfort"/>, after the original's search and its draws, before <see cref="ChooseWanderGoal"/>): the cell the guest
	/// stands on and the cells the random walk visited. The policy may name another cell of the walk as the goal and may make the guest complain; the host performs both. The default keeps the goal and complains of nothing.
	/// </summary>
	PathDecision ChoosePath( in DecisionContext context, in GuestView guest, in WanderPathView path ) => PathDecision.Keep;
}
