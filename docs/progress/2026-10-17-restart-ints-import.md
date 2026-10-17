## The original's restart.INTS saves import (October 17, 2026)

- **Why they were refused.** The four `restart.INTS` snapshots (one per theme) hold a park nothing has updated yet. `SaveWorld.Parse` required a played calendar (month 0-11, day 1-31) and
  `SaveAttraction.ReadHistory` required a history head of 0-31, so the snapshots failed. tp.exe's own loaders read both unchecked (`0x4F7F30` calendar, `0x4E2C10` history ring); the values are
  what the constructors build.
- **Where the initial values come from.** The macro-AI constructor `0x4F7E80` (called by the world constructor `0x515540`) sets the calendar: start 2000-01-01 (via `0x5FC5C0`), month cache 0, day cache
  `0xFFFFFFFF`, year cache 0, rate 15000, and a session start of the local time at creation. The history ring constructor `0x51ADE0` sets current entry `0xFFFFFFFF`, 30 entries and no wrap.
- **What is accepted.** Exactly those two states, nothing looser: `SaveCalendar.IsInitial` (start 2000-01-01, month 0, day -1, rate 15000; the session start is the wall clock, so free) and an empty ring
  (head -1 with 30 entries and no wrap; `SaveAttraction.EmptyRingHead`). Any other negative head, a wrapped or shorter empty ring, or another calendar value is still refused. The import treats the
  calendar as the park's start (park turn 0, no mismatch note) and `DailyRing` already took head -1 as an empty ring.
- **Result.** All eleven original saves pass `OriginalSaveCorpusTests` (the INTS ones now also assert the initial calendar, turn 0, no placed objects and empty rings); two synthetic tests pin the
  two states and their boundaries.
- **Restart path (SAVE-012).** On the Mac the loader writes `restart.INTS` right after a park load (state 15, `0x1c37b4`) and state 14 reloads it. tp.exe keeps the ".INTS" and "restart" names (`0x747A20`,
  `0x747A6C`) only as unreferenced static strings, and its save loader `0x414D40` and level start `0x407E00` take a file name from the command line only, so the player-visible restart of the
  Windows build is not traced. OpenTPW's Restart Park starts a fresh park from the level and balance; it is registered as SAVE-012 rather than wired to the snapshot.
