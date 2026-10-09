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

/// <summary>
/// Receives visitor events from an attraction. Implemented by <see cref="GuestSimulation"/>.
/// Calls arrive from the fixed simulation tick in a deterministic order.
/// </summary>
public interface IRideVisitorHost
{
	/// <summary>The attraction offered the guest to its script (VAR_LETMEON = guest id).</summary>
	void OnVisitorOffered( IRideVisitorBridge ride, int guestId );
	/// <summary>The script took the guest (HUSH/WALKON/LIMBO/BOUNCE, or it cleared VAR_LETMEON).</summary>
	void OnVisitorBoarded( IRideVisitorBridge ride, int guestId );
	/// <summary>The script released the guest at the exit (VAR_LETMEOFF = guest id), or the ride was removed.</summary>
	void OnVisitorReleased( IRideVisitorBridge ride, int guestId );
	/// <summary>The guest left the queue without riding (ride closed or removed).</summary>
	void OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId );
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
	int MaximumQueueLength { get; }
	IRideVisitorHost? Host { get; set; }

	bool TryJoinQueue( int guestId );
	/// <summary>0-based position in the queue, or −1.</summary>
	int GetQueuePosition( int guestId );
	bool LeaveQueue( int guestId );
}
