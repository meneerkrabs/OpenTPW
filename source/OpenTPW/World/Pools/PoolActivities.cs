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
/// [EXT:pools-activities] Something that acts on the free swimmers of a pool each park turn without being offered: the push of a wave machine, the spray of a jet. A registered
/// <see cref="IPoolActivity"/> may implement it too. The host calls <see cref="Ambient"/> once per park turn for each swimmer in state 22 (<see cref="GuestState.Swimming"/>) that took no
/// offer that turn, before its stroke. It must not draw from any random stream: its effects are deterministic from the turn and the swimmer's cell (and it keeps no per-guest state of its own;
/// what it changes lives in the guest's saved fields). It must not move the guest: it runs before the stroke, which starts from where the guest is.
/// </summary>
// [EXT:pools-activities] OpenTPW's own extension point; the original game has no pools
public interface IPoolAmbient
{
	/// <summary>One park turn of the effect on one free swimmer of the region.</summary>
	void Ambient( GuestSimulation simulation, Guest guest, PoolSwimRegion region );
}

/// <summary>
/// One usable instance of an activity offered to a swimmer: the thing id (<c>OriginalObject.ThingId</c>) of the object that provides it, for every activity (0 for none, such as a river or a spot of water), and the cell
/// where the swimmer leaves the water for it (or does it).
/// </summary>
public readonly record struct PoolActivityOffer( int TargetId, int X, int Y );

/// <summary>
/// The reference heights (engine units) of a swimmer's point for <see cref="IPoolActivity.FeetHeight"/>.
/// </summary>
/// <param name="Default">The default feet height: the water surface on a pool cell, otherwise the terrain.</param>
/// <param name="Water">The water surface of the guest's pool region, or <paramref name="Default"/> when it has none.</param>
/// <param name="Deck">The rim or deck (terrain) height at the guest's point.</param>
public readonly record struct PoolFeetHeights( float Default, float Water, float Deck );

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

	/// <summary>
	/// The sprite slot (<see cref="PoolSwimSlots"/>) the guest is drawn with this turn. Defaults to treading water, which every swim set has art for; a slot a set has no art for is drawn as tread water, then breaststroke, then crawl (<see cref="GuestSpriteAtlas.UsableSlot"/>).
	/// </summary>
	int SpriteSlot( Guest guest ) => PoolSwimSlots.TreadWater;

	/// <summary>
	/// The engine height of the guest's feet this frame, or null for the default (<see cref="PoolFeetHeights.Default"/>: the water surface on a pool cell, otherwise the terrain). The reference heights of the guest's
	/// point are in <paramref name="heights"/>. Presentation only: it is never read by the simulation. A slide returns its track height; a ladder interpolates between <see cref="PoolFeetHeights.Water"/> and
	/// <see cref="PoolFeetHeights.Deck"/>, a board or a swing returns <see cref="PoolFeetHeights.Deck"/> or <see cref="PoolFeetHeights.Default"/> plus its lift.
	/// </summary>
	float? FeetHeight( GuestSimulation simulation, Guest guest, PoolFeetHeights heights ) => null;

	/// <summary>
	/// 0 for an activity offered during the swim (the host's cadence, <see cref="PoolActivitySettings.OfferIntervalTurns"/>), or 1..100: the percent chance that the guest does it as the swim starts, before it
	/// enters the water (a shower). An activity with a start chance is offered at the start only.
	/// </summary>
	int StartOfferPercent => 0;

	/// <summary>Ends it at once (the pool or the target is gone, the room is demolished, the park closes): the activity lets go of what it holds; the host ends the session.</summary>
	void Abort( GuestSimulation simulation, Guest guest );
}

/// <summary>The swim-session numbers the host keeps for activities (docs/POOLS.md, "Activities"; OpenTPW's own, no original value).</summary>
// [EXT:pools-activities] OpenTPW's own numbers; the original game has no pools
public static class PoolActivitySettings
{
	/// <summary>On average one offer is taken every this many park turns while an offer exists (a draw of the swimmer's own stream each turn, only while one exists).</summary>
	public const int OfferIntervalTurns = 90;
	/// <summary>No offer is taken when the swim has fewer turns left than this, so the session does not end in the middle of a ladder.</summary>
	public const int OfferMinimumTurns = 24;
	/// <summary>The most park turns an activity may count in <see cref="Guest.PoolActivityTurns"/>: the validator refuses a save above it, so an activity stays below it (it keeps its own phases short).</summary>
	public const int MaximumTurns = 2400;
}

/// <summary>The sprite slots of the swimwear sets (docs/POOLS.md, "Swimwear slots"). A slot without art in a set is drawn as tread water, then breaststroke, then crawl in the swimmers' renderer.</summary>
// [EXT:pools-render] OpenTPW's own swimmer art; the original game has no pools
public static class PoolSwimSlots
{
	public const int Stand = 0;
	public const int Walk = 1;
	public const int Run = 2;
	public const int LadderClimb = 3;
	public const int Dive = 4;
	public const int Lying = 5;
	public const int Shower = 6;
	public const int Sliding = 7;
	public const int Crawl = 8;
	public const int Breaststroke = 9;
	public const int TreadWater = 10;
	public const int SplashDown = 11;
	public const int SittingInWater = 12;
	public const int FloatingInRing = 13;
	public const int RopeSwing = 14;
	public const int Spare = 15;
}

/// <summary>The activities a park's swim sessions offer (<see cref="GuestSimulation.PoolActivities"/>); Level registers these while the pools extension is on.</summary>
// [EXT:pools-activities] OpenTPW's own extension point; the original game has no pools
public static class PoolActivityRegistry
{
	/// <summary>The built-in activities. Each phase of the pools work adds its own to this list; a registry holding none changes nothing.</summary>
	public static IEnumerable<IPoolActivity> Create()
	{
		yield break;
	}
}
