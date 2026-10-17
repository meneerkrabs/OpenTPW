namespace OpenTPW;

/// <summary>What an activity does after one park turn (<see cref="IPoolActivity.Update"/>).</summary>
public enum PoolActivityStep
{
	/// <summary>The activity goes on next turn.</summary>
	Continue,
	/// <summary>The guest is back in the water at its current position, which the activity left inside the swim region: the host resumes the swim.</summary>
	BackInWater,
	/// <summary>The session is over: the host sends the guest to climb out at its changing room.</summary>
	EndSession
}

/// <summary>
/// One usable instance of an activity offered to a swimmer: the attraction or object that provides it (0 for none, such as a spot of water), and the cell
/// where the swimmer leaves the water for it (or does it).
/// </summary>
public readonly record struct PoolActivityOffer( int TargetId, int X, int Y );

/// <summary>
/// [EXT:pools-activities] Something a swimmer does during its swim session besides swimming (docs/POOLS.md, "Activities"): a pool extra (ladder, diving board,
/// shower, lounger), a water slide, or a water attraction that lands the guest back in the pool. Activities are offered and run by the host's swim session
/// (<see cref="GuestSimulation"/>), which keeps the guest's state, money, statistics and happiness rules; an activity only decides where the guest goes and
/// when it is done.
/// </summary>
/// <remarks>
/// Rules every activity keeps, so parks without it are unchanged and runs stay deterministic:
/// <list type="bullet">
/// <item>It draws only from the swimmer's own stream (the <c>random</c> argument), never from the world generator.</item>
/// <item><see cref="Offer"/> is pure: it reads the simulation and returns an offer or null, and changes nothing.</item>
/// <item>Its per-guest state lives in the guest's generic activity fields (<see cref="Guest.PoolActivityPhase"/>, <see cref="Guest.PoolActivityTurns"/>,
/// <see cref="Guest.PoolActivityX"/>, <see cref="Guest.PoolActivityY"/>), which the host saves, restores and hashes; it keeps no state of its own per guest.</item>
/// <item>Its <see cref="Id"/> is saved with the guest: never rename one that has shipped.</item>
/// </list>
/// </remarks>
// [EXT:pools-activities] OpenTPW's own extension point; the original game has no pools
public interface IPoolActivity
{
	/// <summary>The stable id saved with a guest that is doing it, such as <c>extra.ladder</c> or <c>slide.body</c>.</summary>
	string Id { get; }

	/// <summary>An instance of this activity the swimmer can use now in its swim region, or null. Pure and deterministic.</summary>
	PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region );

	/// <summary>Starts the offer for the guest (the host has set its state to <see cref="GuestState.PoolActivity"/> and its target). False declines: the guest swims on.</summary>
	bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random );

	/// <summary>One park turn of the activity.</summary>
	PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random );

	/// <summary>Ends it at once (the pool or the target is gone, the room is demolished, the park closes): the activity lets go of what it holds; the host ends the session.</summary>
	void Abort( GuestSimulation simulation, Guest guest );
}
