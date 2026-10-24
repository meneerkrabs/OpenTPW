## Kart and water track pieces drape onto the terrain (October 24, 2026)

Part of #829 ("big gaps in the JurassicKart track"). Each tile of a kart or water track was drawn as one rigid piece at the midrange height of its 3 × 3 landscape vertices, so tiles on a slope left steps and slits at their joints. tp.exe loads every one of these pieces with a drape flag and moves each mesh vertex onto the landscape.

**The original's mechanism.**
- The kart and water kit loaders (`0x522AA0`, `0x522C94`) call the model loader `0x4629D0` with the load flags `0x10422`. Its bit `0x2` becomes loader flag `0x40` (`0x462A4A`); `0x461F10` then ORs `0x20000` into every mesh node (`0x462610`..`0x462631`) and sets header bit `0x80` (`0x4625F4`).
- The instancer (`0x522B90`) creates each tile through `0x463060` with flags `0x33A`. Flag 8 calls `0x469940` → `0x469A80`, which sets every vertex of a node with `0x20000` to its model height plus the landscape under the vertex minus the instance height. The landscape value is the cell's triangle chosen by cell-word bit `0x4`, always split: no `0x800` planar shortcut, no bounds test. There is no grid-corner snap (`0x46A5A0` needs loader bit 1).
- The original never levels land under a track tile (no landscape writes in `0x539760`, `0x5370E0`, `0x538FC0`), so building on slopes is allowed.

**Port.**
- `TrackDrape` and `LandscapeSampler.SampleDrape` (new `IParkGrid.GetDrapeHeight`): every placed tile gets its own draped vertices per part; arm masks are applied before, as before. A tile playing its water clip animates a copy of its own draped vertices.
- Vehicle surface: pieces without a surface record are sampled in mode 1 at the probe (landscape at the probe + header +0xA0), not at the tile's instance height. The loader's drape flag gives every kit piece header bit `0x80`, so the Splish Splash and Ooze Crooz `Q` pieces (`+0xA0` = 0, no record) are now mode 1 with offset 0, where they were mode 4.
- Pieces with a surface record (`H`, `H_U`, `H_D` in every kit) are mode 2: `0x44A870` sets the instance block's bit 8, which `0x450EA0` tests, for exactly the models with a dummy record of type bit 2. The faces they read are the tile's draped copy (`0x450F20` reads the instance's own vertices). This settles the part of TRACK-001 that said the block's bit 8 was not read.
- `TileHeight` is unchanged (cell lookup of `.hmp`, bounding boxes, the tile's instance height).

**Not ported, with evidence.**
- The original recomputes face normals after the drape (`0x473C30` → `0x473A40`, for every instance of `0x463060`); draped meshes keep their stored normals.
- Queue models (`0x522900`, flags `0x12423` / `0x112423`, which also carry the drape bit) are already lifted per vertex onto the ground by `OriginalTerrain.BuildQueueModels` (PATH-015). It uses the planar-shortcut sampler and an extra lift of 0.01 cell, and does not port the `.hmp` ground deformation of loader bit 1; this is a separate follow-up in the path layer.

**Verified.**
- New tests (`TrackDrapeTests`): the drape sampler against `InCell`, the far edge and negative points; the shared joint of neighbouring tiles at different instance heights; mode 1 at the probe; mode 2 bridge faces on the draped surface.
- Captures of `--load-original-level jungle --debug-kart-track` before and after: the track walls and deck no longer step at the tile joints.
