## Theme Park Inc: obstacles as things of the world and the camera flight (November 21, 2026)

- **Obstacles as things.** The object catalogue reads Theme Park Inc's `obstacles` folders (`ObjectCategory.Obstacle`). The prebuilt Science park now builds
  its obstacles and camera path (1702, 1701, 1700, 1703, 1750, 1798) as fixed items with their models, scripts and saved thing ids; the lake 1703, the one
  attraction without a scene record, comes from its thing. A loaded Theme Park Inc park's objects keep their saved thing ids where the slot is free, so the
  World variable `CurrentObstacle` (thing 29) names the 1700 gate's object.
- **Obstacle cells.** `ParkCellMap` keeps Theme Park Inc's obstacle cell flag (map square `+0x24`); the prebuilt saves it as the last dword of each 56-byte
  map cell. The level takes the prebuilt's obstacle cells (type 8 or 5, the anchor's owner word, the flag), and a layout snapshot keeps the flags
  (`TpiObstacles`). `ThemeParkIncObstacles` ports the placement rule (`0x647BF0`, tested against the prebuilt's saved cells) and the opening (`0x5E8BD0`:
  `AfterShape`, the map-edge rule, `ReplaceWithID`, `DeleteAfterUse`, gates untouched) with Theme Park Inc's square writer (`0x67CB70`, type word only).
- **Camera flight.** `ParkEconomy.TpiCutscene` ports the cutscene thing (`0x5D0400`, `0x5D02F0`, `0x5D0840`): the ride view on the target's seat 1, the trigger in
  script variable 0, the end when the script sets variable 1 (the Science camera path flies its 500-tick clip in 16.4 s), the clock at 1.0 (0.7 in mode 1), the
  HUD hidden under the input window, the mode-2 skip by Escape or the right button's release, message 0x29 with 0 or the trigger, the arrival briefing
  (`0x5F1760`) and the mission window's countdown. The controller's cameras now fly and its completion comes at the flight's real end. A new game's Science
  park opens with the zone arrival's flyover (`0x6A43D8`, first day only). The continuation save keeps the flight's thing. For the mission window package:
  `PressThemeParkIncMissionComplete` (the complete button, `0x55BD79`) and `MissionWindowRequests`; the 1750 gate's zone switch is recorded in
  `ZoneSwitchRequests`.
- **The lake.** `[EXT:tpi-lake-drained]` is gone: the level no longer draws `terrain.wad`'s `dlake.MD2`; the lake is 1703's model and after its opening
  1704's.
- **Correction.** The setter of a script variable, `0x6B0780`, has 89 call sites through its thunk `0x4028EC` (the camera flight among them); the reading of
  November 10 that nothing calls it (RIDES-045's retirement) missed the thunk. The declaration in `ObjectCatalog` says so; which slots the other callers write
  is still to be traced.

Not ported yet: the placement in a world built without the prebuilt (the object-info order `0x647BF0` walks is not read, so Water and Arabian build no
obstacles or camera path), the pick and cursor help over obstacle cells, the tile refresh of type 8, the flight's fog range and camera words, the
advice classes admitted during a flight, and the 1750 gate's zone switch (recorded for the zone package). The mission window and its Proceed are a
separate package; completion in play still needs it.

Verified with these checks:

- The Theme Park Inc, catalogue, layout, cell map, resume, park save and ride view filters pass with the retail data. New tests cover the placement rule
  against the prebuilt's saved cells, the level's obstacle cells and thing ids, every opening variant (stays, deleted, replaced, gate, the map edge), the
  layout round trip of the flags, the flight's natural end and skip with their 0x29 payloads, the clock rate, the first-day arrival, the save round trip,
  mission 0 from the window's button to mission 1 with the gate's opening, and the lake's drain to 1704 at its flight's end.
- The silent, offscreen Science smoke exits 0; it now checks the obstacles and the camera path in the world, flies the zone arrival (the ride view on the
  camera path's seat, the HUD hidden, the clock at 1.0) and skips it with Escape.
- The fidelity register stays at 76 unresolved IDs (no new approximation or extension label). The browser build with the determinism pins builds.

The original game was not launched, and no binary or data is included.
