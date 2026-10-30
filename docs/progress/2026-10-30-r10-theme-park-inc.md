## Theme Park Inc readings (October 30, 2026)

- **Umbrellas, from the no-CD Theme Park Inc `Game.exe`.** UMB-002 is ported and retired:
  the scheduling and the carrying out of an umbrella change stand down for a guest on a kart
  or water track cell (`0x67F890`, `0x685680`), and a shown umbrella there is closed
  (`0x60AF32`); `UmbrellaRules.OnTrackCell` is tested over every record type. UMB-003: the
  inside words are read (`0x61A540` state `0x14` sets `+0x3C` for every attraction and `+0x38`
  for a type with a nonzero `+0x1C4`, state `0x13` clears both), so "inside" is the state
  Using, plus the pools extension's own swim states; the clock scale is read (15000 from
  `0x607EB9`); only the seconds-based age remains. UMB-004: `YAdjustCamera` is 0.1
  (`levels/Standard.sam`). UMB-001: the C runtime's `rand()` is seeded once by
  `srand(GetTickCount())` (`0x6A151B`) and shared with 96 call sites, so the original's
  delay is not reproducible even by the original; the hash stays, for the user to decide.
