namespace OpenTPW;

/// <summary>How a queue attempt of a guest ended (<see cref="IGuestObserver.QueueEnded"/>): every reason is either an abandonment or a rejection (<see cref="QueueEndReasons.IsAbandonment"/>).</summary>
public enum QueueEndReason
{
	// ---- rejected: the join test, the ride or the world said no (the guest did not choose to leave) ----
	/// <summary>The queue had no room left (count at 4 x the queue cells).</summary>
	Full,
	/// <summary>The ride is too exciting for the guest's type ("too scary").</summary>
	TooScary,
	/// <summary>The ride is not exciting enough for the guest's type ("too boring").</summary>
	TooBoring,
	/// <summary>The queue was at its data limit at the join, or beyond the limit after a shortening.</summary>
	QueueLimit,
	/// <summary>The attraction took no joiners (closed, torn down, or the guest was already listed).</summary>
	NotAccepting,
	/// <summary>The guest was called to the stand point and refused the price of a shop or sideshow.</summary>
	Price,
	/// <summary>A closed ride sent its queue head away.</summary>
	RideClosed,
	/// <summary>A broken ride dismissed its queue head.</summary>
	RideBroken,
	/// <summary>A queue edit left the guest beyond the room.</summary>
	QueueShortened,
	/// <summary>The queue cells under the guest were removed.</summary>
	QueueRemoved,
	/// <summary>The guest lost its place or its route to the queue position.</summary>
	NoPlace,
	/// <summary>A toilet's wear gauge was too low to serve the guest.</summary>
	ReliefWornOut,
	/// <summary>A coaster or track ride refused guests.</summary>
	TrackRefuses,
	/// <summary>The attraction was removed from the park.</summary>
	AttractionRemoved,
	// ---- abandoned: the guest left of its own accord (happiness, toilet and boredom tests of the queue) ----
	/// <summary>Happiness fell below <see cref="GuestSimulation.LeaveHappiness"/> while queueing.</summary>
	Unhappy,
	/// <summary>The toilet meter rose above the leave level while queueing for something that is not a toilet.</summary>
	NeedToilet,
	/// <summary>The boredom test of the queue (the original's unreachable one).</summary>
	Bored
}

public static class QueueEndReasons
{
	/// <summary>True for the reasons where the guest left of its own accord; the others are rejections.</summary>
	public static bool IsAbandonment( QueueEndReason reason ) => reason is QueueEndReason.Unhappy or QueueEndReason.NeedToilet or QueueEndReason.Bored;
}

/// <summary>
/// The predicates that sent a guest home (docs/GUESTS.md "Decision", <see cref="GuestLifecycle.ShouldLeave"/>), as flags: the happiness byte is 0, the exit counter is 0 or the
/// park is closed, all three evaluated when the guest decided to leave; the booth and a guard send a guest home without that test.
/// </summary>
[Flags]
public enum DepartureCause
{
	None = 0,
	/// <summary>The truncated happiness byte was 0.</summary>
	HappinessZero = 1,
	/// <summary>The exit counter was exactly 0.</summary>
	ExitCounterZero = 2,
	/// <summary>The park was closed (including a gate that stayed shut at the booth).</summary>
	ParkClosed = 4,
	/// <summary>The ticket booth turned the guest away (the entrance fee, a bankrupt park, an expensive verdict at happiness 0).</summary>
	BoothRefused = 8,
	/// <summary>A guard set the guest down at the gate.</summary>
	Guard = 16
}

/// <summary>
/// A passive view on a <see cref="GuestSimulation"/> for measurement (docs/SMARTER-GUESTS.md "Measurement"). It is null by default, so a park without one pays a null test per
/// event and nothing else. An observer must never change the simulation: no guest or attraction write, no <see cref="WorldRng"/> draw, and nothing it keeps is saved or hashed.
/// </summary>
public interface IGuestObserver
{
	/// <summary>A guest paid the entrance fee and entered the park (<paramref name="fee"/> as booked).</summary>
	void Admitted( Guest guest, int fee );

	/// <summary>A guest paid <paramref name="amount"/> (the entrance fee for <paramref name="attractionId"/> 0, else a ride, shop or sideshow), as <see cref="GuestSimulation.MoneySpent"/> reports it.</summary>
	void Spent( Guest guest, int amount, int attractionId );

	/// <summary>A guest arrived at the back of an attraction's queue and the join test refused it (<paramref name="reason"/> is a rejection).</summary>
	void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason );

	/// <summary>A guest passed the join test and entered the queue (a queue attempt that is still open).</summary>
	void QueueJoined( Guest guest, IRideVisitorBridge attraction );

	/// <summary>A queued guest boarded: <paramref name="waitTurns"/> park turns from joining, or -1 when the guest's join was not seen (a resumed save).</summary>
	void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns );

	/// <summary>A queued guest left the queue without boarding, after <paramref name="waitTurns"/> park turns.</summary>
	void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns );

	/// <summary>A major decision ended: <paramref name="chose"/> is true when the guest picked an attraction, false when it found none (the "cannot decide" thought).</summary>
	void Decided( Guest guest, bool chose );

	/// <summary>A guest started walking home (state 18) for <paramref name="cause"/>; a recalled guest may announce it again.</summary>
	void Leaving( Guest guest, DepartureCause cause );

	/// <summary>A guest is about to be removed from the park (the last step of leaving).</summary>
	void Departed( Guest guest );

	/// <summary>One fixed tick of the simulation is over; the guests are in their end-of-tick state.</summary>
	void Ticked( GuestSimulation simulation, float deltaSeconds );
}
