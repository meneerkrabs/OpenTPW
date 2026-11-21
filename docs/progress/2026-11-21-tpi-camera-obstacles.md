## Theme Park Inc: obstacles as things of the world and the camera flight (November 21, 2026)

- **Obstacles as things.** The object catalogue reads Theme Park Inc's `obstacles` folders (`ObjectCategory.Obstacle`). The prebuilt Science park now builds
  its obstacles and camera path (1702, 1701, 1700, 1703, 1750, 1798) as fixed items with their models, scripts and saved thing ids; the lake 1703, the one
  attraction without a scene record, comes from its thing. A loaded Theme Park Inc park's objects keep their saved thing ids where the slot is free, so the
  World variable `CurrentObstacle` (thing 29) names the 1700 gate's object.
- **Obstacle cells.** `ParkCellMap` keeps Theme Park Inc's obstacle cell flag (map square `+0x24`); the prebuilt saves it as the last dword of each 56-byte
  map cell. The level takes the prebuilt's obstacle cells (type 8 or 5, the anchor's owner word, the flag), and a layout snapshot keeps the flags
  (`TpiObstacles`). `ThemeParkIncObstacles` ports the placement rule (`0x647BF0`, tested against the prebuilt's saved cells) and the opening (`0x5E8BD0`:
  `AfterShape`, the map-edge rule, `ReplaceWithID`, `DeleteAfterUse`, gates untouched) with Theme Park Inc's square writer (`0x67CB70`, type word only).
- **The lake.** `[EXT:tpi-lake-drained]` is gone: the level no longer draws `terrain.wad`'s `dlake.MD2`; the lake is 1703's model and after its opening
  1704's.
- **Correction.** The setter of a script variable, `0x6B0780`, has 89 call sites through its thunk `0x4028EC` (the camera flight among them); the reading of
  November 10 that nothing calls it (RIDES-045's retirement) missed the thunk. The declaration in `ObjectCatalog` says so; which slots the other callers write
  is still to be traced.
