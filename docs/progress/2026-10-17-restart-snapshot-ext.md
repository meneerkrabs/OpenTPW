## Restart snapshot per start is an approved EXT (October 17, 2026)

- **What changed.** Labels and docs only; behaviour is unchanged. The part of SAVE-012 that says OpenTPW retakes the restart snapshot at every park start and load is now
  `[EXT:restart-snapshot-per-start]` at `GameFlow.TakeRestartSnapshot`, with a row in the EXT table of `docs/UI.md`. The user approved it on 2026-10-17.
- **Original.** `0x550B30` opens `restart.INTS` for reading (`0x550C0F`) and saves only when that fails (`0x550C14`), and nothing deletes the file between parks, so the original keeps the first park of
  a theme folder as the restart point forever. OpenTPW's Restart Park returns to the park being played instead.
- **SAVE-012 narrowed, not retired.** What stays open is the snapshot's format (OpenTPW's park-resume file instead of the original's `restart.INTS`); the camera block that is not restored belongs to SAVE-010.
