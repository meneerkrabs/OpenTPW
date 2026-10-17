## Restart Park reloads the restart snapshot (October 17, 2026)

- **What changed.** Restart Park no longer starts a fresh level. Every original park writes a restart snapshot when it starts or is loaded (`opentpw-<theme>-restart.json` in the save folder, OpenTPW's park-resume format), and
  Restart Park reloads it. A park built from scratch restarts to its start; a loaded park (own save, automatic continue, a shipped level save) restarts to the loaded state; the reload takes no new snapshot.
- **Original.** tp.exe's park state machine `0x54E360` writes `<save>\restart.INTS` in state 15 through `0x550B30` unless the GameType word `0xFB3B7C` is 1 (the online visit, which OpenTPW's read-only visit
  matches: no snapshot, no restart). A restart request (code 1 from the confirm callbacks `0x48B5B0` and `0x48B620` through `0x5508B0`) takes state 10 to 13, which only advances to 14; state 14 runs `0x5996D0(1)` (stops the simulation,
  clears the world and, with the argument, destroys it), loads the file through `0x5AC5F0` and `0x414D40`, and returns to state 10 without a new snapshot (`0x550371`, request cleared at `0x55037b`).
- **Full Simulation.** The original's pause menu builder `0x48C150` lists Restart Park for every game type except the online visit, with no mode test, so Full Simulation parks now offer it too. The golden-ticket ledger is
  player-wide in the original (the ticket object `0xF7C58C` lives from park setup to the park exit and with the player slot; the restart states, the loader `0x414D40` and the teardown `0x5996D0` never touch it), so a restart keeps it,
  as OpenTPW's ledger file does: tickets and keys earned before the restart stay earned, and the restarted park can earn them again without a second ledger entry. The snapshot was taken after the entry cost was paid, so a restart pays nothing.
- **What a player sees in the original.** The writer saves only when `restart.INTS` cannot be opened (`0x550C14`), and nothing deletes it between parks (the only deleters reach it through the whole player folder, when a player is
  created over a slot, `0x5C7F40`). So the first park started or loaded in a theme folder (normally a new park) fixes the snapshot: every later Restart Park in that theme goes back to that first state, also after other new or
  loaded parks. OpenTPW rewrites its snapshot at every start and load instead, so a restart returns to the start or load of the park being played (SAVE-012 keeps the difference).
- **Relaunch.** The snapshot is a file, so a restart after quitting and relaunching reads it, as the original's file survives.
- **Cost.** The capture runs on the main thread at the end of the park start or load (about 250 to 300 ms, mostly the asset manifest hash); a load reuses the manifest it already hashed for its validation. A restart takes no snapshot.
- **Camera.** The restart rebuilds the park from the snapshot like a load: the camera starts at the park's home view (the saved camera block is not applied, SAVE-010).
- **Determinism.** The capture only reads the park; the pinned hash of an unrestarted run is unchanged.
