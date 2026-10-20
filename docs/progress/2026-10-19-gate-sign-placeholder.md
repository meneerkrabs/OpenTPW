## The park gate's sign slots no longer fall back to the shipped placeholder (October 19, 2026)

A report showed the Jungle park gate's board with the shipped placeholder "SIGN1 SIGN2" on every new park start, in the browser with the Dutch game files. The cause was not reproduced: the browser build
(headless, muted, interpreter publish, the Dutch CD layout `theme park 2`, no screenshots) builds the gate sign every time and binds it to the gate object, in a new Jungle park, after Exit To Lobby and a second start, from kept
(OPFS) files, with the night look, and with Dutch chosen at start-up ("Verloren Koninkrijk", 11,943 text pixels, YOUNIA__.TTF); the desktop build agrees. The most likely cause is a page from before October 11 (the gate bound the
shipped sign art then, commits 520eaaabc and 3d9d547da), for instance a cached older build.

What changed so the placeholder cannot come back through the paths found:
- One rule for every `sign1`/`sign2` slot (`SignSlotPlan`): the gate (found by the level's `ParkName.GateObjectId`, with the sign of its own level, which is made before the level's objects)
  shows the park name, or a plain brown board when its sign is missing or failed; any other fixed item draws its own sign or the board; a placed object draws its own sign. The board is one
  shared texture that is never released.
- Creating the gate sign no longer stops a park for the unexpected read failures (arguments, platform support, state, access), and a half-built gate sign entity no longer breaks its own deletion.
- An object type's sign that could not be read (I/O, access) is tried again up to three times (logged once) and one whose archive had no files yet (the browser copies a theme's files when a
  park starts) is not remembered as missing for the session; failures that repeat (bad data, a missing key or a bad argument in the sign data) stay cached.
- Track pieces, vehicles, coaster cars and tour cars are unchanged: none of the shipped ones has a sign face (`LobbySignCorpusTests`).

Not changed: a placed object without any `.sgn` (a mod's) keeps the art it ships; that and the drawing itself (pixels) are not covered by the tests, which check the choice of texture source, the cache rule and the failure filter.
