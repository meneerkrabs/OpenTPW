## Pools, phase P1: painted pool water cells as the `opentpw.pools` extension (October 17, 2026)

A new official extension, off by default (`[EXT:pools-extension]`, docs/POOLS.md): the player paints pool water cells of any size and shape from the buy window's Miscellaneous list, they cost 75 each (refunded when erased), render with an
animated water surface and auto-joined coping and walls, and are saved. There are no guests in the water yet; pool cells are not walkable, so guests route around them.

- **Save rule.** `AffectsSimulation: true` alone would tag every save once the extension is on, so `opentpw.pools` is a used-only extension: a park save records it only when the park has pool cells
  (`ParkResumeMods.UsedOnlyExtensionIds`, `Capture(active, inUse)`). A park without pool cells saves byte-identically with the extension on or off; a park with pool cells loads only with it on (`needs opentpw.pools 1.0.0`).
- **Cell type.** `ParkCellTypes.PoolWater = 0x40`, above every original type (the highest is 30). `CanChangeCellType` allows it over empty cells only; `IsWalkable` is unchanged. The canonical hash and the layout `Cells` carry it with no schema change.
- **Tool.** `PoolBuilder` (headless) and `PoolTool` (press and drag, an erase mode, undo), refusals with their own reasons (not owned, occupied, terrain, too steep, money). The ground rule is a corner height spread of at most 0.25 engine
  units; the buildable land of the four themes is flat enough for it almost everywhere (4 of about 28,000 cells refused).
- **Rendering.** `PoolMeshBuilder` (CPU, unit-tested for all 256 neighbour masks, seams, counts, normals and UVs) with a generated tile texture set and `pool-water.shader` (adapted from the unused `water.shader`). Because the terrain cannot be dug, the pool is a raised
  basin: water 0.15 above the ground, coping 0.1 above the water. GPU paths are untested; a visual check is still to do.
- **Pins.** Every existing pin is unchanged with the extension on or off; the fixed synthetic run with 17 pool cells is pinned as `0x51B905DC5CF06BC6`.
- **Not done:** swimmers, changing rooms, extras and slides (P2 to P5).
