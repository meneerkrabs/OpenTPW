## Saves import: queues and leaving guests (October 29, 2026)

- **A saved queue now comes back as the original left it (SAVE-002, QUEUE-003).** tp.exe's loader
  reads an attraction's head `mFirstInQ` (`+0x3C`) and called guest `mPersonBeingLoaded` (`+0x6C`)
  and each guest's `mQNext`/`mQPrev` (`+0x228`/`+0x22A`) as saved and relinks nothing
  (`0x4DB7D0`, `0x4FB530`). The import follows: guests in states 11 to 14, and in the idle
  interlude of state 11 (state 8 with `mSavedState` 11, `0x4FC800`), keep their place in the
  chain with the saved position and move-up delay; a guest moving up or called forward routes to
  its saved accurate destination as every loaded person does (`0x4F9407`), and a head in state 14
  waits for the script (`0x4E0A70`: the script's first variable against `mFirstInQ`). A chain
  that is not a list of guests of that ride in the queue states is refused with its reason.
- **Guests leaving a ride (state 15) are imported** and walk on to their saved destination.
- Still dropped, with their reasons reported: guests using an attraction (16) and held by a guard
  (17).
