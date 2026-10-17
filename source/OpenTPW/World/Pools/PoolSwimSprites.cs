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

	/// <summary>Loads every swim set (sorted by name), or none when the folder or the content is missing (in the browser also while the files are still being downloaded, <see cref="PoolSpriteDelivery"/>).</summary>
	public static List<GuestSpriteAtlas> Load() => PoolSpriteDelivery.IsComplete( PoolSpriteDelivery.SwimGroup ) ? PoolGuestSprites.LoadFolder( Directory, "swim" ) : new List<GuestSpriteAtlas>();
}
