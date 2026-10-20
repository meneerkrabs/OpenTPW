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
/// [EXT:pools-waves] A current of the water that carries a body in it (the waves of a wave machine): its push for this park turn, in 16.16 cells, which the free swimmers' step adds after the stroke and the lazy
/// river's floaters after their float step (along their lane) where the water and the other bodies leave room (<see cref="PoolSwimming.Drift"/>, <see cref="GuestSimulation.PoolCurrent"/>). Integers only; a pool
/// without one pushes nothing and hashes nothing new.
/// </summary>
public interface IPoolCurrent
{
	/// <summary>The push on one body in the water of the region this turn (a free swimmer or a floater; zero for none).</summary>
	(int X, int Y) Current( GuestSimulation simulation, Guest guest, PoolSwimRegion region );
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
	/// <see cref="PoolFeetHeights.Deck"/>, a board or a swing returns <see cref="PoolFeetHeights.Deck"/> or <see cref="PoolFeetHeights.Default"/> plus its lift. It is where the body rests whatever the slot:
	/// the renderer draws the slots whose hotspot is the torso (<see cref="PoolSwimSlots.BottomAnchored"/>) with their bottom edge at it, the others with their hotspot.
	/// </summary>
	float? FeetHeight( GuestSimulation simulation, Guest guest, PoolFeetHeights heights ) => null;

	/// <summary>
	/// 0 for an activity offered during the swim (the host's cadence, <see cref="PoolActivitySettings.OfferIntervalTurns"/>), or 1..100: the percent chance that the guest does it as the swim starts, before it
	/// enters the water (a shower). An activity with a start chance is offered at the start only.
	/// </summary>
	int StartOfferPercent => 0;

	/// <summary>
	/// Whether the guest, in this activity, is a body in the water this turn: free swimmers steer clear of it and keep <see cref="PoolSwimSettings.MinimumDistance"/> from it (docs/POOLS.md, "Activities"). True while it floats,
	/// stands under a jet, swims to something or splashes down; false on the deck, a ladder, a board, a platform or a lounger. Default false. Pure: it reads the guest's saved fields only.
	/// </summary>
	bool InWater( GuestSimulation simulation, Guest guest ) => false;

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

/// <summary>
/// The sprite slots of the swimwear sets (docs/POOLS.md, "Swimwear slots"; docs/CONTENT-KIT.md, "Swimwear slots"), for the activities of the swim session to choose from. Every slot has five directions
/// (the other three are mirrored by the renderer); the hotspot of the slots 0, 1 and 6 is the floor between the feet, of 3, 4, 5, 7 and 14 the centre of the torso, of 8 to 13 the centre of the torso at the
/// water line. The slots 2 and 15 are empty in the shipped sets. A slot without art in a set is drawn as tread water, then breaststroke, then crawl in the swimmers' renderer.
/// </summary>
// [EXT:pools-render] OpenTPW's own swimmer art; the original game has no pools
public static class PoolSwimSlots
{
	/// <summary>Standing on the deck in swimwear (1 frame), the shower queue.</summary>
	public const int Stand = 0;
	/// <summary>Walking on the deck (8 frames).</summary>
	public const int Walk = 1;
	/// <summary>Running: unused, empty.</summary>
	public const int Run = 2;
	/// <summary>Climbing a pool ladder, facing it (4 frames).</summary>
	public const int LadderClimb = 3;
	/// <summary>Diving or jumping into the water: crouch, leap, arc, head-first entry (4 frames).</summary>
	public const int Dive = 4;
	/// <summary>Lying on the back on a lounger (2 frames), the feet towards the heading.</summary>
	public const int Lying = 5;
	/// <summary>Standing under a shower with falling drops (4 frames).</summary>
	public const int Shower = 6;
	/// <summary>Sliding down a slide on the back, feet first (2 frames).</summary>
	public const int Sliding = 7;
	/// <summary>Front crawl (8 frames).</summary>
	public const int Crawl = 8;
	/// <summary>Breaststroke (8 frames).</summary>
	public const int Breaststroke = 9;
	/// <summary>Treading water (4 frames).</summary>
	public const int TreadWater = 10;
	/// <summary>Landing in the water with a splash: the body sinks, a crown of drops, growing rings (4 frames).</summary>
	public const int SplashDown = 11;
	/// <summary>Sitting in the water, knees up, an arm on the rim (2 frames): the spa.</summary>
	public const int SittingInWater = 12;
	/// <summary>Floating in an inflatable ring, paddling (4 frames): the lazy river and the wave pool's ring floaters.</summary>
	public const int FloatingInRing = 13;
	/// <summary>Swinging on a rope with both hands overhead (4 frames).</summary>
	public const int RopeSwing = 14;
	/// <summary>Spare, empty.</summary>
	public const int Spare = 15;

	/// <summary>Frames per direction of each slot in the shipped sets (0 for the empty ones).</summary>
	public static readonly IReadOnlyList<int> FramesPerDirection = new[] { 1, 8, 0, 4, 4, 2, 4, 2, 8, 8, 4, 4, 2, 4, 4, 0 };

	/// <summary>The slots drawn turned in their own plane by the residual of the 45 degree direction snap (<see cref="GuestRenderer.RotatedSlots"/>): the flat poses only, never the upright ones.</summary>
	public static readonly IReadOnlySet<int> Rotated = new HashSet<int> { Sliding };

	/// <summary>
	/// The slots whose hotspot is the centre of the torso out of the water (the ladder, the dive, lying, sliding and the rope swing): the swimmers' renderer draws them with their bottom edge at the guest's
	/// point (<see cref="GuestRenderer.BottomAnchoredSlots"/>), so <see cref="IPoolActivity.FeetHeight"/> always gives where the body rests. The strokes, the splash, the spa and the ring keep their water-line
	/// hotspot, and standing, walking and the shower their feet.
	/// </summary>
	public static readonly IReadOnlySet<int> BottomAnchored = new HashSet<int> { LadderClimb, Dive, Lying, Sliding, RopeSwing };

	/// <summary>
	/// [EXT:pools-render] The slots whose hotspot is the feet on the ground or the deck (standing, walking, the shower): the swimmers' renderer draws them with the depth pull of a visitor's sprite on
	/// land (<see cref="GuestRenderer.DepthPulledSlots"/>), so their feet show as the kids' do. The water-line and torso slots keep their depth, so a swimmer is not drawn over a pool's near wall.
	/// </summary>
	public static readonly IReadOnlySet<int> FeetAnchored = new HashSet<int> { Stand, Walk, Shower };

	/// <summary>
	/// [EXT:pools-render] Seconds each frame of the lying slot is held. Its two frames are two resting poses (the arms at the sides, the hands behind the head; tools/blender/opentpw_pool_poses.py,
	/// <c>lying</c>), not the steps of a cycle: played at the activities' six frames a second the guest flapped its arms the whole lie.
	/// </summary>
	public const double LyingPoseSeconds = 7.0;

	/// <summary>
	/// [EXT:pools-render] The animation step the swimmers' renderer draws for a guest in a pool activity at <paramref name="seconds"/> of simulation time (presentation only): six frames a second, offset
	/// by the guest's id; the lying slot changes its pose only every <see cref="LyingPoseSeconds"/> (offset per guest, so two guests on one lounger do not change together).
	/// </summary>
	public static int ActivityStep( int slot, double seconds, int guestId ) =>
		slot == Lying ? (int)(seconds / LyingPoseSeconds + guestId * 0.37) : (int)(seconds * 6) + guestId;
}

/// <summary>The activities a park's swim sessions offer (<see cref="GuestSimulation.PoolActivities"/>); Level registers these while the pools extension is on.</summary>
// [EXT:pools-activities] OpenTPW's own extension point; the original game has no pools
public static class PoolActivityRegistry
{
	/// <summary>The built-in activities. Each phase of the pools work adds its own to this list; a registry holding none changes nothing.</summary>
	public static IEnumerable<IPoolActivity> Create( PoolAttractionSites? attractions = null )
	{
		// [EXT:pools-extras] P3: the pool extras' activities
		yield return new LadderActivity();
		yield return new DivingBoardActivity();
		yield return new ShowerActivity();
		yield return new LoungerActivity();
		yield return new PoolSlideActivity();
		// [EXT:pools-attractions] [EXT:pools-river] the water attractions (P5) and the lazy river
		if ( attractions != null )
		{
			yield return new RopeSwingActivity( attractions );
			yield return new JetsActivity( attractions );
			yield return new SpaActivity( attractions );
			// [EXT:pools-waves] ring floaters in a wave pool, offered only while its waves run
			yield return new WaveRingActivity( attractions );
		}
		yield return new RiverActivity();
		yield return new PoolWaterEffects( attractions );
	}
}
