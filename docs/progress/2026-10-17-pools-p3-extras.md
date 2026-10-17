## Pools P3: ladder, diving board, shower, sun lounger, lamp and underwater light (October 17, 2026)

Phase P3 of the `opentpw.pools` extension ([POOLS.md](../POOLS.md#extras-phase-p3-extpools-extras)), on top of the activity host (see the activity-host fragment): the four extras are `IPoolActivity`s, the other two decoration.

- **Objects** `[EXT:pools-extras]`: six per theme (Info.Ids 65000 to 65035), built with the content kit (`tools/blender/opentpw_pool_extras.py`, 32 x 32 textures from the cabin's generators, about 0.4 MB in all), features with no entrance and `Info.IsChoosable 0`, so guests only meet them in the pool. Placement rules: a straight pool edge (ladder, board, underwater light; the board two cells out), a changing room beside it (shower), water beside it (lounger, lamp). The water beside an extra cannot be erased; demolishing an extra aborts its activities.
- **Activities**: ladder (climb out, stand, jump back in), board (climb, walk out, dive, splash; the drawn feet follow through `FeetHeight`), shower (offered at the start of a swim, 60 percent, 3 to 5 seconds), sun lounger (two seats, lie 15 to 30 seconds, then back in the water or end the session). One swimmer per ladder, board, shower or seat; numbers and happiness bonuses in POOLS.md.
- **No new cell type**: the deck is ordinary owned ground with the extra's own slab; every pin and save is as before with the extension off or no extra placed. New pin `PoolExtrasFixedRunHash = 0x15A2596129C01546`.
- Renderer: a swimwear slot a sprite set has no art for yet falls back to treading water (`GuestSpriteAtlas.UsableSlot`). Tools: `--debug-extras`.

Not verified: the swimwear art for the activities (slots 3, 4, 5, 6, 11 are not made yet, so the guests are the treading-water art), the browser build, the objects in the original game's tools. Not done: ending a session at a ladder, the cosmetic haul-out at a plain edge, particle splashes.
