namespace OpenTPW;

/// <summary>
/// The render-only holes of the <c>opentpw.pools</c> extension (docs/POOLS.md, "Depth"): while the extension is on, the terrain renderer leaves the ground mesh out under a pool cell, so the basin
/// can be drawn into the ground. The heightfield itself is untouched: height lookups (<see cref="LandscapeSampler"/>, the corner heights), placement, the cell map and every other part of the
/// simulation never read this rule, so the pins do not depend on it.
/// </summary>
// [EXT:pools-render] OpenTPW's own pool rendering; the original game has no pools
public static class PoolRenderHoles
{
	/// <summary>Whether the ground mesh skips cell (<paramref name="x"/>, <paramref name="y"/>): it is a pool cell and the extension is on.</summary>
	public static bool IsHole( ParkCellMap cells, int x, int y, bool extensionOn ) =>
		extensionOn && cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater;
}
