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
	/// The policy belongs to the <c>opentpw.smarter-guests</c> extension: the simulation then keeps the extension's park state (<see cref="SmarterGuestsState"/>: birth ids and the park seed).
	/// A policy that wraps another forwards it.
	/// </summary>
	bool UsesExtensionState => false;

	/// <summary>
	/// The host calls this at the start of every simulation tick (<see cref="GuestSimulation.Tick"/> and the swept tick), before any decision of the tick. The faithful and the smarter policy
	/// hold no state and ignore it; a policy that meters work per tick (the script policy's step budget) starts its tick here. A policy that wraps another forwards it.
	/// </summary>
	void BeginTick() { }

	/// <summary>The booth's verdict on the entrance fee (tp.exe <c>0x004FF9D0</c>); the host draws the verdict's word first and the expensive wait afterwards.</summary>
	BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth );

	/// <summary>The score of one attraction for one guest (<c>0xe9164</c>); the host asks for attractions that have a join cell only.</summary>
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
}
