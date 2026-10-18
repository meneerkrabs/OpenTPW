namespace OpenTPW;

/// <summary>Why a guest turned down an attraction (<see cref="GuestRefusalCounters"/>); the five reasons the guest-insights extension shows.</summary>
public enum RefusalReason
{
	/// <summary>The guest was called to the stand point of a shop or sideshow and refused the price.</summary>
	TooExpensive,
	/// <summary>The queue had no room left or was at its data limit.</summary>
	QueueTooLong,
	/// <summary>The ride is too exciting for the guest's type.</summary>
	TooScary,
	/// <summary>The ride is not exciting enough for the guest's type.</summary>
	TooBoring,
	/// <summary>A closed or broken ride sent the guest away from the head of its queue.</summary>
	RideUnavailable
}

/// <summary>
/// The refusal-reason counters of the <c>opentpw.guest-insights</c> extension: a passive <see cref="IGuestObserver"/> that tallies, since the park was built or loaded, how many times a guest
/// was turned away or turned an attraction down, by <see cref="RefusalReason"/>. It only reads: it takes no draw and writes no guest, and nothing it keeps is saved or hashed (a loaded park
/// starts again from zero).
/// </summary>
// [EXT:guest-insights] OpenTPW's own counters; the original game keeps no refusal statistics
public sealed class GuestRefusalCounters : IGuestObserver
{
	private readonly long[] counts = new long[Enum.GetValues<RefusalReason>().Length];

	/// <summary>The times <paramref name="reason"/> has happened.</summary>
	public long Count( RefusalReason reason ) => counts[(int)reason];

	/// <summary>All five counts together.</summary>
	public long Total => counts.Sum();

	/// <summary>
	/// The reason a queue attempt's end counts as, or null for the ends that are not refusals of the attraction (the guest's own leaving tests, a queue edit, a removed attraction, a worn toilet and so on).
	/// </summary>
	// the join test's refusals (0x4FFBC0 ... 0x4FFD7E: too exciting, not exciting enough, queue too long, no room), the price refusal at the stand point and a closed or broken ride's dismissal of the queue head; NotAccepting
	// (closed, torn down or already listed at the join) is left out because it cannot tell a closed ride from a double join
	public static RefusalReason? Classify( QueueEndReason reason ) => reason switch
	{
		QueueEndReason.Price => RefusalReason.TooExpensive,
		QueueEndReason.Full or QueueEndReason.QueueLimit => RefusalReason.QueueTooLong,
		QueueEndReason.TooScary => RefusalReason.TooScary,
		QueueEndReason.TooBoring => RefusalReason.TooBoring,
		QueueEndReason.RideClosed or QueueEndReason.RideBroken => RefusalReason.RideUnavailable,
		_ => null
	};

	private void Note( QueueEndReason reason )
	{
		if ( Classify( reason ) is { } refusal )
			counts[(int)refusal]++;
	}

	public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) => Note( reason );
	public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) => Note( reason );

	public void Admitted( Guest guest, int fee ) { }
	public void Spent( Guest guest, int amount, int attractionId ) { }
	public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
	public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
	public void Decided( Guest guest, bool chose ) { }
	public void Leaving( Guest guest, DepartureCause cause ) { }
	public void Departed( Guest guest ) { }
	public void Recalled( Guest guest ) { }
	public void Restored( GuestSimulation simulation ) { }
	public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
}

/// <summary>Two observers behind the simulation's one slot (the extension's counters and a measurement collector); each event goes to both, first then second.</summary>
public sealed class GuestObserverChain : IGuestObserver
{
	private readonly IGuestObserver first, second;

	/// <summary>The observer that gets each event first.</summary>
	public IGuestObserver First => first;

	/// <summary>The observer that gets each event second.</summary>
	public IGuestObserver Second => second;

	public GuestObserverChain( IGuestObserver first, IGuestObserver second ) => (this.first, this.second) = (first, second);

	public void Admitted( Guest guest, int fee ) { first.Admitted( guest, fee ); second.Admitted( guest, fee ); }
	public void Spent( Guest guest, int amount, int attractionId ) { first.Spent( guest, amount, attractionId ); second.Spent( guest, amount, attractionId ); }
	public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { first.QueueRejected( guest, attraction, reason ); second.QueueRejected( guest, attraction, reason ); }
	public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { first.QueueJoined( guest, attraction ); second.QueueJoined( guest, attraction ); }
	public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { first.QueueBoarded( guest, attraction, waitTurns ); second.QueueBoarded( guest, attraction, waitTurns ); }
	public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) { first.QueueEnded( guest, attractionId, reason, waitTurns ); second.QueueEnded( guest, attractionId, reason, waitTurns ); }
	public void Decided( Guest guest, bool chose ) { first.Decided( guest, chose ); second.Decided( guest, chose ); }
	public void Leaving( Guest guest, DepartureCause cause ) { first.Leaving( guest, cause ); second.Leaving( guest, cause ); }
	public void Departed( Guest guest ) { first.Departed( guest ); second.Departed( guest ); }
	public void Recalled( Guest guest ) { first.Recalled( guest ); second.Recalled( guest ); }
	public void Restored( GuestSimulation simulation ) { first.Restored( simulation ); second.Restored( simulation ); }
	public void Ticked( GuestSimulation simulation, float deltaSeconds ) { first.Ticked( simulation, deltaSeconds ); second.Ticked( simulation, deltaSeconds ); }
	public void Vomited( Guest guest ) { first.Vomited( guest ); second.Vomited( guest ); }
	public void Thought( Guest guest, GuestThought thought ) { first.Thought( guest, thought ); second.Thought( guest, thought ); }
	public void WanderGoalMoved( Guest guest ) { first.WanderGoalMoved( guest ); second.WanderGoalMoved( guest ); }

	public void LitterDropped( Guest guest ) { first.LitterDropped( guest ); second.LitterDropped( guest ); }

	public void LitterBinned( Guest guest ) { first.LitterBinned( guest ); second.LitterBinned( guest ); }
	public void PathComplained( Guest guest, PathComplaint complaint ) { first.PathComplained( guest, complaint ); second.PathComplained( guest, complaint ); }
}
