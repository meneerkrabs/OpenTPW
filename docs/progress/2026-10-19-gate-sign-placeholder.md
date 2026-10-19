## The park gate's sign slots no longer fall back to the shipped placeholder (October 19, 2026)

A report showed the Jungle park gate's board with the shipped placeholder "SIGN1 SIGN2" on every new park start, in the browser with the Dutch game files. The cause was not reproduced: the browser build
(headless, muted, interpreter publish, the Dutch CD layout `theme park 2`, no screenshots) builds the gate sign every time and binds it to the gate object, in a new Jungle park, after Exit To Lobby and a second start, from kept
(OPFS) files, with the night look, and with Dutch chosen at start-up ("Verloren Koninkrijk", 11,943 text pixels, YOUNIA__.TTF); the desktop build agrees. The most likely cause is a page from before October 11 (the gate bound the
shipped sign art then, commits 520eaaabc and 3d9d547da), for instance a cached older build.

What changed so the placeholder cannot come back through the paths found:
- One rule for every `sign1`/`sign2` slot (`SignSlotPlan`): the gate (found by its archive, with the sign of its own level) shows the park name, or a plain brown board when its sign is missing, failed or not yet built (the order of a park built
  over frames); any other fixed item draws its own sign or the board; a placed object draws its own sign. The board is one shared texture.
- Creating the gate sign no longer stops a park for the unexpected read failures (arguments, platform support, state, access), and a half-built gate sign entity no longer breaks its own deletion.
- An object type's sign that could not be read (I/O, access) or whose archive had no files yet (the browser copies a theme's files when a park starts) is no longer remembered as missing for the session; failures that repeat stay cached.
- Coaster pieces and tour cars draw their object's sign on `sign1`/`sign2` instead of the shipped art.

Not changed: a placed object without any `.sgn` (a mod's) keeps the art it ships; that and the drawing itself (pixels) are not covered by the tests, which check the choice of texture source, the cache rule and the failure filter.
