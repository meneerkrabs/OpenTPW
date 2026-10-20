## The lobby's islands show their theme's gate sign instead of the placeholder (October 19, 2026)

Entering a park from the lobby showed the shipped magenta placeholder "SIGN1 SIGN2" on the theme island's gate during the zoom. The lobby and globe scenes (`OriginalModelScene`) bound every
material by name, so the four lobby models with `sign1`/`sign2` faces (`Jun_isle`, `Hal_isle`, `Fan_gate`, `Spa_gate`) drew the shipped art.

What the original does (tp.exe, read statically): the lobby builds each island with two explicit lines that `0x409B10` takes from the theme gate's OBJECT_NAMES row (gates 1601, 2601, 3601,
4601: the lines a new park's gate starts with, never a saved park name), and the island constructor `0x5DFA60` hands them to both its island and its gate model, whose sign generator
`0x467A80` draws them with the model's own `lobby/terrain/<model>.sgn`.

What changed:
- `LobbySigns` composes those lines on each sign model's `.sgn` and keeps the halves for the session; each lobby scene makes its own textures from them and releases them with itself. The
  signs are composed one per frame from the lobby's second frame on (its first frame is drawn at once; a sign costs about 0.2 s in the browser today), the selected island's first, and none
  while the camera zooms into a park; the faces show the shared plain board until then or when one cannot be drawn (a read that may pass is tried again, up to three times). The gates'
  OBJECT_NAMES texts are read once per lobby build. A lobby built again (after a park) binds the kept signs at once. The globe and every other front-end model never show the placeholder either (`SignSlotPlan.ForScene`).
- Review fixes to the gate-sign branch: the gate is found by the level's `ParkName.GateObjectId`, so a failed gate sign keeps the board instead of drawing the default lines; the shared board is
  never released (`Texture.SignBoard`); a disposed object or a null argument while the gate sign is built is no longer hidden; a sign read that fails with an I/O or access error is logged
  once and given up after three attempts (the park HUD asks on every preview draw), while a missing key or a bad argument in the sign data (`KeyNotFoundException`, `ArgumentException`) is
  cached like bad data.
- A fixed item other than the gate draws its own type sign when it has one, as every object the original's object builder makes reaches the sign generator (tp.exe `0x4629D0` ->
  `0x467A80`, tagged at `SignSlotPlan.For`); among the shipped fixed items only the gates have sign faces, so this matters for mods.
- The coaster-piece and tour-car sign change of the gate-sign branch is dropped: no shipped track piece, vehicle, coaster car, pylon or tour car has a `sign1`/`sign2` face (the corpus scan in
  `LobbySignCorpusTests`; the ride archives' sign faces are all on the station and preview models, which `OriginalObject` draws), and the coaster assets released the shared sign textures.

Tests: `SignSlotPlanTests` (the lobby's choice never resolves to the shipped placeholder, the gate by its id, the failure filter and cache rules) and `LobbySignCorpusTests` with the game data
(the four lobby sign models compose their theme's lines with text pixels, the lobby gates are the levels' gates, no renderer-loaded ride mesh has sign faces). `--front-end --smoke-test` now
requires, in the lobby and again after Exit To Lobby, that no sign is pending and that every sign slot of the four models is bound to its runtime sign (not the board, not the shipped
art); in Dutch it composed "Verloren Koninkrijk", "Halloween Wereld", "Wonderland" and "Space Zone".
