## Restart Park reloads the restart snapshot (October 17, 2026)

- **What changed.** Restart Park no longer starts a fresh level. Every original park writes a restart snapshot when it starts or is loaded (`opentpw-<theme>-restart.json` in the save folder, OpenTPW's park-resume format), and
  Restart Park reloads it. A park built from scratch restarts to its start; a loaded park (own save, automatic continue, a shipped level save) restarts to the loaded state; the reload takes no new snapshot.
- **Original.** tp.exe's park state machine `0x54E360` writes `<save>\restart.INTS` in state 15 through `0x550B30` unless the GameType word `0xFB3B7C` is 1 (the online visit, which OpenTPW's read-only visit
  matches: no snapshot, no restart). A restart request (code 1 from the confirm dialog callback `0x48B620` through `0x5508B0`) takes state 10 to 13: `0x5996D0(1)` ends the running park (stops the simulation,
  clears the world and, with the argument, destroys it), `0x5AC5F0` loads the file through `0x414D40`, and the machine returns to state 10 without a new snapshot (`0x550371`, request cleared at `0x55037b`).
- **Relaunch.** The snapshot is a file, so a restart after quitting and relaunching reads it, as the original's file survives. The original's writer saves only when the file cannot be opened (`0x550C14`); OpenTPW rewrites
  its snapshot at every start and load, because no deleter of `restart.INTS` was found (SAVE-012 keeps that question).
- **Still not offered.** A Full Simulation park keeps the entry disabled (`[EXT:pause-menu-restart]`): OpenTPW's progress ledger is its own and a reload would not undo it.
- **Determinism.** The capture only reads the park; the pinned hash of an unrestarted run is unchanged.
