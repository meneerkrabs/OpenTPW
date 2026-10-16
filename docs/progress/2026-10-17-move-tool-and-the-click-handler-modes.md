## The Move tool and the click handler's modes (October 17, 2026)

The tool click handler `0x524960` (about 1,200 lines of pseudocode) was read mode by mode in tp.exe via REA; the modes are tabulated in `docs/UI.md`.

**Built**
- **The Move tool** (UI-104 retired): the object windows' `b_move` (`0x3e2c`, `0xc080`, `0xa0a8`) picks the object up through the erase of the demolition (`0x48cfa0` runs
  `0x524960` in mode 0x33, which runs `0x527ee0`, the very call of the erase button without its confirmation) and arms the same type at its angle in mode 0x3B,
  which places like mode 4 (`0x5253d5`). Only the type and the angle survive (`0x81b3ac` and `0x81b14c` are never read), the new object starts afresh, and cancelling leaves the object gone.
  The ride is paid by the constructor (`0x4db4e2`), so a move costs the whole price again less the pick-up's scrap credit; the flag `0x8186d4` frees only the path and queue cells the placement lays,
  and mode 4 sets it too. The ride window enables the button with `0x52c2b0`: always, except for a kart or water ride, whose two tiles beside the station must hold one arm each (`Level.MoveEnabled`).
  The earlier reading, that the flag made the placement free, was wrong.

**Narrowed or settled**
- **TRACK-018 narrowed**: the Crypt Karts' bridge clips (ten pieces) carry one texture-frame track whose 20 keys all name frame 0 of a one-frame slot, so there is nothing to draw
  (`TrackPieceClipDataTests`). Left: the track edit sub-modes 0xE and 0x13 (a two-click cut of a closed circuit, the continuation of an open one from its last tile, `0x530830`), decoded in `docs/TRACKS.md` but not built.
- **TRACK-014**: the readers of the step number are the edit sub-modes only. **ECON-026**: the hoarding line is clicked through mode 0x19 and the line walker `0x53aac0`; the per-step writes of `0x5370e0` are open.
- **UI-031**: `+0x29c` is the map type word of the queue's start cell (type 1, a path, ends the tool); the coaster sub-mode takes the same tail into the queue tool.
- **UI-095**: the link writes of `0x524960` are on the second layer's cells, never an object's map cell. **QUEUE-014**: the first click of the path and queue tools is read. **QUEUE-023**: the four call sites of
  the re-evaluation `0x4de1f0` in the click handler are listed. **AUDIO-009**: the 13 sound call sites of the build tools' code are listed.

**Determinism.** No pin change: the Move tool only reuses the build path; no new simulation state, random draw or save field.

**Verification.** See the PR description for the test, register, evidence and smoke runs.
