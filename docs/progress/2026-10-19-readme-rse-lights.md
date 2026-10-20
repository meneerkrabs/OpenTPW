## README: the ride scripts' light opcodes are in the game (October 19, 2026)

The README's file-format table still listed "the four light opcodes" as not yet in the game, although `ENABLELIGHT`, `DISABLELIGHT`, `SETLIGHT` and `COLOURLIGHT` have been served by the object
lights (`[EXT:object-lights]`, docs/LIGHTS.md) since the lights work: they switch, dim and colour a model's light dummies, and no shipped script or model uses them (a mod's can). The row now says so,
and its "Not yet in the game" cell names what is actually still open for the ride scripts: Full Simulation's end-of-park finale around `End.RSE` (`APPROX:RIDES-019`; Instant Action already creates and
dismisses it). docs/RSE-VM.md's sentence that `End.RSE` is never created is corrected the same way. Documentation only.
