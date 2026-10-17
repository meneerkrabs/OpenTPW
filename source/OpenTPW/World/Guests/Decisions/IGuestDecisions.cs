namespace OpenTPW;

/// <summary>
/// The pure decision layer of the guests (docs/SMARTER-GUESTS.md, "A pure decision layer"). The host (<see cref="GuestSimulation"/>) keeps everything with an effect: routing, queue insertion and
/// removal, payments, statistics, thoughts, happiness changes and every <see cref="WorldRng"/> draw, each exactly once and in the original order. A policy only decides: each method takes
/// snapshots or views and returns a typed decision, reads no generator, no clock and no simulation state of its own that the views do not carry, and changes nothing. A draw the original
/// takes inside logic that moved here stays in the host, which passes the drawn value in (the booth's draw, <see cref="BoothView.Draw"/>) or runs the drawing procedure itself and offers the
/// result for the policy to accept or replace (the wander goal).
/// </summary>
public interface IGuestDecisions
{
	/// <summary>
	/// The policy belongs to the <c>opentpw.smarter-guests</c> extension: the simulation then keeps the extension's park state (<see cref="SmarterGuestsState"/>: birth ids and the park seed).
	/// A policy that wraps another forwards it.
	/// </summary>
	bool UsesExtensionState => false;

	/// <summary>The booth's verdict on the entrance fee (tp.exe <c>0x004FF9D0</c>); the host draws the verdict's word first and the expensive wait afterwards.</summary>
	BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth );

	/// <summary>The score of one attraction for one guest (<c>0xe9164</c>); the host asks for attractions that have a join cell only.</summary>
	uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction );

	/// <summary>
	/// The major decision (<c>0xe8ff4</c>): scores the host's candidates and picks one. The candidates are enumerated lazily in the host's order; <see cref="MajorCandidate.Reach"/> is the host's
	/// route request and may be called only for a candidate the policy would pick.
	/// </summary>
	MajorDecision DecideMajor( in DecisionContext context, in GuestView guest, IEnumerable<MajorCandidate> candidates );

	/// <summary>
	/// The minor decision while walking to a ride (<c>0x004FD570</c>): the best of the host's window candidates (rides nearer to the target's back cell than the guest, open and with room), in the
	/// host's order. The host then tests the routes and retargets.
	/// </summary>
	MinorDecision DecideMinor( in DecisionContext context, in GuestView guest, in AttractionView target, IReadOnlyList<AttractionView> window );

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
