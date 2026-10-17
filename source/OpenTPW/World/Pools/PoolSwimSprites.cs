namespace OpenTPW;

/// <summary>
/// The swimwear sprite sets of the pools extension (<c>content/pools/esprites/Swim/SPR_xx.ESP</c> and <c>.TPC</c>, docs/POOLS.md): one set for each of the eight Theme Park World kid sets, in swimwear
/// at twice the pixel size, holding the slots of <see cref="PoolSwimSlots"/> (standing, walking, the ladder, the dive, lying, the shower, the slide, the three strokes in the slots 8 to 10, the splash-down,
/// the spa, the ring and the rope swing) with five directions, as <c>tools/blender/opentpw_guest_sprites.py --part pool</c> renders them (docs/CONTENT-KIT.md).
/// They are loaded at level setup while the extension is on (decoded on a worker) and drawn by the swimmers' own renderer, whichever kid sets the land guests use; the set at index n belongs to the guests
/// of type n, like the kid sets (both lists are in name order). In a Theme Park Inc park they still are the Theme Park World sets (a known mismatch: its four kid sets have no swim art yet).
/// </summary>
// [EXT:pools-render] OpenTPW's own swimmer art; the original game has no pools
public static class PoolSwimSprites
{
	/// <summary>The folder of the swim sets below the pools content.</summary>
	public static string? Directory => PoolContent.Directory is { } folder ? Path.Combine( folder, "esprites", "Swim" ) : null;

	/// <summary>Loads every swim set (sorted by name), or none when the folder or the content is missing.</summary>
	public static List<GuestSpriteAtlas> Load() => PoolGuestSprites.LoadFolder( Directory, "swim" );
}

/// <summary>
/// The animation slots of a swim set (docs/CONTENT-KIT.md, "Swimwear slots"), for the activities of the swim session to choose from. Every slot has five directions (the other three are mirrored by the
/// renderer); the hotspot of the slots 0, 1 and 6 is the floor between the feet, of 3, 4, 5, 7 and 14 the centre of the torso, of 8 to 13 the centre of the torso at the water line. The slots 2 and 15 are empty
/// in the shipped sets, and an empty slot falls back to standing (<see cref="GuestSpriteAtlas.GetFrame"/>).
/// </summary>
// [EXT:pools-render] OpenTPW's own swimwear slot table; the original game has no pools
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
	public const int Tread = 10;
	/// <summary>Landing in the water with a splash: the body sinks, a crown of drops, growing rings (4 frames).</summary>
	public const int SplashDown = 11;
	/// <summary>Sitting in the water, knees up, an arm on the rim (2 frames).</summary>
	public const int SpaSitting = 12;
	/// <summary>Floating in an inflatable ring, paddling (4 frames).</summary>
	public const int RingFloating = 13;
	/// <summary>Swinging on a rope with both hands overhead (4 frames).</summary>
	public const int RopeSwing = 14;
	/// <summary>Spare, empty.</summary>
	public const int Spare = 15;

	/// <summary>Frames per direction of each slot in the shipped sets (0 for the empty ones).</summary>
	public static readonly IReadOnlyList<int> FramesPerDirection = new[] { 1, 8, 0, 4, 4, 2, 4, 2, 8, 8, 4, 4, 2, 4, 4, 0 };
}
