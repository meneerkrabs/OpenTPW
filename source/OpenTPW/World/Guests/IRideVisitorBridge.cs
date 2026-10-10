namespace OpenTPW;

/// <summary>What a guest gets from an attraction besides the ride itself.</summary>
[Flags]
public enum GuestNeeds
{
	None = 0,
	Hunger = 1,
	Thirst = 2,
	Toilet = 4
}

public enum RideVisitorKind
{
	Ride,
	Shop,
	Sideshow,
	Facility
}

/// <summary>Outcome of the joining checks at the back of a queue (state 10, <c>0xeccb0</c>).</summary>
public enum QueueJoinResult
{
	Joined,
	/// <summary>The attraction is closed (or the guest id is invalid).</summary>
	Closed,
	AlreadyQueued,
	/// <summary>count ≥ 4 × queue cells.</summary>
	NoRoom,
	/// <summary>|preferred − excitement| ≥ 45 with the ride below the guest's preference.</summary>
	NotExcitingEnough,
	/// <summary>|preferred − excitement| ≥ 45 with the ride above the guest's preference.</summary>
	TooExciting,
	/// <summary>count ≥ the data limit (100, or the QueueWaitTimeConstant formula).</summary>
	QueueTooLong
}

/// <summary>
/// One admission evaluation of a ride update (one per park turn). <see cref="Stalled"/> is the M3 gate's
/// progress signal: the admission gates held and the head stood at position 0, yet nobody was called.
/// </summary>
public readonly record struct AdmissionCheck( long Turn, bool ConditionsHold, int HeadGuest, bool HeadAtFront, int CalledGuest )
{
	public bool Stalled => ConditionsHold && HeadAtFront && CalledGuest == 0;
}

/// <summary>
/// Queue rules of one object from its .sam layers (docs/reverse/QUEUE-plan.md §2): <c>Info.HasQueue</c>,
/// <c>Info.RunsContinuously</c>, the capacity-test bypass (<c>Bumper.WhichTrackType</c> 2 or 3) and the inputs of
/// the queue limit: <c>Upgrades[L].QueueWaitTimeConstant</c>, SPEED and <c>Upgrades[L].InitSpeed</c>, CAP and DUR.
/// </summary>
public sealed record QueueParameters( bool HasQueue, bool RunsContinuously, bool SkipsCapacityTest, float QueueWaitTimeConstant, int Speed, int InitSpeed, int Capacity, int Duration )
{
	/// <summary>A queue-less object without data: the limit is the floor of 4.</summary>
	public static readonly QueueParameters Default = new( false, false, false, 0, 0, 0, 0, 0 );
}

/// <summary>
/// Receives visitor events from an attraction. Implemented by <see cref="GuestSimulation"/>.
/// Calls arrive from the fixed simulation tick in a deterministic order.
/// </summary>
public interface IRideVisitorHost
{
	/// <summary>The attraction called the front guest forward (ride <c>+104</c> = guest; the guest walks to the stand point).</summary>
	void OnVisitorOffered( IRideVisitorBridge ride, int guestId );
	/// <summary>The script took the guest (HUSH/WALKON/LIMBO/BOUNCE, or it cleared VAR_LETMEON).</summary>
	void OnVisitorBoarded( IRideVisitorBridge ride, int guestId );
	/// <summary>The script released the guest at the exit (VAR_LETMEOFF = guest id), or the ride was removed.</summary>
	void OnVisitorReleased( IRideVisitorBridge ride, int guestId );
	/// <summary>The guest left the queue without riding (ride closed or removed).</summary>
	void OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId );
	/// <summary>The park turn (<see cref="ParkCalendar.Turn"/>); attractions evaluate admission once per turn. −1: every step.</summary>
	long ParkTurn => -1;
	/// <summary>True when called guests walk to the stand point and present themselves (<see cref="RideVisitorBridge.PresentForBoarding"/>).</summary>
	bool WalksToBoard => false;
	/// <summary>The guest stands in the queue (state 11) at recorded position 0.</summary>
	bool IsStandingAtFront( IRideVisitorBridge ride, int guestId ) => true;
}

/// <summary>
/// The guest-facing side of one placed attraction. The guests slice drives queues through it; the
/// rides/objects slice supplies the cells and metadata from the object catalog (.sam <c>Info.Shape</c>
/// entrance '2' / exit 'S' cells, <c>UsageInfo.ExcitementLevel</c>, <c>Info.AttractionValue</c>, capacity).
/// <see cref="RideVisitorBridge"/> implements it on top of an RSE script.
/// </summary>
public interface IRideVisitorBridge
{
	/// <summary>Stable, unique, positive id per placed attraction (used for deterministic ordering).</summary>
	int AttractionId { get; }
	string Name { get; }
	RideVisitorKind Kind { get; }
	bool IsOpen { get; }
	int Capacity { get; }
	/// <summary>.sam <c>UsageInfo.ExcitementLevel</c> (0–100).</summary>
	int ExcitementLevel { get; }
	/// <summary>.sam <c>Info.AttractionValue</c>.</summary>
	int AttractionValue { get; }
	/// <summary>Needs a visit satisfies (shops, toilets).</summary>
	GuestNeeds Satisfies { get; }
	/// <summary>Price per visit charged to the guest (0 for rides: TPW charges admission).</summary>
	int Price { get; }
	/// <summary>Walkable path cell where guests join the queue.</summary>
	(int X, int Y) EntranceCell { get; }
	/// <summary>Walkable path cell where released guests reappear.</summary>
	(int X, int Y) ExitCell { get; }
	int QueueLength { get; }
	/// <summary>min(4 × queue cells, the data limit).</summary>
	int MaximumQueueLength { get; }
	/// <summary>The data limit: 100 with <c>Info.HasQueue</c>, else the QueueWaitTimeConstant formula.</summary>
	int QueueLimit { get; }
	bool HasQueue { get; }
	bool ProvidesRelief { get; }
	/// <summary>The ride reports a failure; queueing guests leave.</summary>
	bool IsBroken { get; }
	/// <summary>Queue cells from the front (index 0) to the back.</summary>
	IReadOnlyList<(int X, int Y)> QueueCells { get; }
	int QueueSizeInCells { get; }
	/// <summary>Walkable path cell where guests step onto the back of the queue; null when the queue is not connected.</summary>
	(int X, int Y)? JoinCell { get; }
	/// <summary>Incremented when a recompute changed the queue cells.</summary>
	int QueueEditCount { get; }
	/// <summary>The guest called forward, 0 when none.</summary>
	int CalledGuest { get; }
	IRideVisitorHost? Host { get; set; }

	bool TryJoinQueue( int guestId );
	QueueJoinResult JoinQueue( int guestId, int excitementDifference );
	/// <summary>0-based position in the queue, or −1.</summary>
	int GetQueuePosition( int guestId );
	bool LeaveQueue( int guestId );
	/// <summary>Recomputes the queue cells from the grid; true when they changed.</summary>
	bool RecomputeQueue( GuestPathGrid grid );
	/// <summary>Direction from queue cell <paramref name="index"/> toward its predecessor (toward the ride entrance for the front cell).</summary>
	int DirectionTowardsFront( int index );
	/// <summary>The called guest reached the stand point; true once its id is in VAR_LETMEON.</summary>
	bool PresentForBoarding( int guestId );
}
