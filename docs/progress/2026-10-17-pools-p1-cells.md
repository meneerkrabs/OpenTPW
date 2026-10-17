## Pools, phase P1: painted pool water cells as the `opentpw.pools` extension (October 17, 2026)

A new official extension, off by default (`[EXT:pools-extension]`, docs/POOLS.md): the player paints pool water cells of any size and shape from the buy window's Miscellaneous list, they cost 75 each (refunded when erased),
are saved, and are drawn as a basin dug into the ground with a tiled deck, in the style of the park's theme. There are no guests in the water yet; pool cells are not walkable, so guests route around them.

- **Save rule.** `AffectsSimulation: true` alone would tag every save once the extension is on, so `opentpw.pools` is a used-only extension: a park save records it only when the park has pool cells
  (`ParkResumeMods.UsedOnlyExtensionIds`, `Capture(active, inUse)`). A park without pool cells saves byte-identically with the extension on or off; a park with pool cells loads only with it on (`needs opentpw.pools 1.0.0`).
- **Cell type.** `ParkCellTypes.PoolWater = 0x40`, above every original type (the highest is 30). `CanChangeCellType` allows it over empty cells only; `IsWalkable` is unchanged. Walkers that leave the paths are kept out of the water
  (`WalkEdges.Blocked`, `GuestWander.OffPath`: nothing changes without pool cells). A pool cell is worth what empty land is in the park value.
- **Tool.** `PoolBuilder` (headless) and `PoolTool` (press and drag, an erase mode, undo), refusals with their own reasons (not owned, occupied, a fence post, someone standing there, terrain, too steep, money). The ground rule is a
  corner height spread of at most 0.25 engine units; the buildable land of the four themes is flat enough for it almost everywhere. Erasing refunds what was charged and nothing when the bank charged nothing.
- **Depth.** The terrain renderer treats pool cells as render-only holes while the extension is on (`OriginalTerrain.RenderHoleSource`); heights, placement and the simulation never read them. The basin has walls down to a floor
  1.75 below a level water surface, a quarter-cell deck with joints and a bevel, and translucent water that is lighter at the edge and uses two scrolling layers. The deck is opaque and lit and fogged like the park.
- **Styles.** One table (`PoolStyles`) with a row per theme (jungle planks and lagoon water, hallow mossy stone and murky glowing water, space riveted metal and glowing cyan water, fantasy candy tiles and lilac water);
  the style follows the level's theme, other worlds get the generic one. Textures are generated in code.
- **Pins.** Every existing pin is unchanged with the extension on or off; the fixed synthetic run with 17 pool cells is pinned as `0xC53F22BFFA12A843`.
- **Not done:** swimmers, changing rooms, extras and slides (P2 to P5); pools on the park map.
