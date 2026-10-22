## The end-of-park finale, as the original plays it (October 22, 2026)

The finale around `End.RSE` is now the original's in both game types, and `APPROX:RIDES-019` is
retired ([the end-of-park finale](../reverse/INSTANT-ACTION-FINALE.md)).

- **The player's record list.** The original player's file keeps a record per park with a byte
  `mAllResearchCompleted` (`0x5AFF50`, `0x5B0D70`, `+0xB9`); choosing a player gives every theme
  a record (`0x5C83B0`). When a park has nothing left to research, the completion handler sets the
  park's byte in every game type (`0x5B0CA0`), and the Full Simulation starts the finale (and
  advice 167) only when every park's byte is set (`0x5B0C40`). OpenTPW keeps the list in the one
  player's own progress file (`ResearchCompleted`, optional: older files load with none), as the
  player has no profiles (`[EXT:no-player-profiles]`); Instant Action marks its park there too.
- **The winning camera.** The winning screen rides End's camera seat (seat 1, node 38 of every
  `End.MD2`), the ride window's Ride it! routine `0x4E15B0` → `0x42A560`, with the ride view's free
  look and its static on entry.
- **The winning panel.** The bounded screen and its Resume Game button are gone. The screen is the
  original's full-screen window with the language's `s_congrats` banner (table `0x74FAF0`,
  `congrats.MD2` of the language folder, which the UI registry now takes beside `ui.wad` as
  `0x4893C0` does), animated by the window property table `0x74FCF8` (`UiWindowAnimation`: it
  squeezes, rises, turns once clockwise about (973, 470), falls and stretches, every step after
  more than 35 ms). It has no button: Escape and the late right click are its exits.
- **After the dismissal.** End is destroyed and the seat camera left (`0x516C10`), the HUD
  returns, and Instant Action shows UITEXT 471 in the original's generic message box (`0x47F020`,
  table `0x74F920`), which pauses the park until it closes. Nothing is written to the player's
  file at the dismissal.
- **Resume.** The original never saves the winning handle; a park OpenTPW restores with End enters
  the same winning screen as a new finale.
- End takes its thing slot in both game types (WALK-010 narrowed). Theme Park Inc (its twin of
  `0x5B0CA0` has no caller) and read-only visits still never start the flow.

No pin moves: the finale needs the player's records in the Full Simulation, which no pinned run
has. Tests: `ParkFinaleTests`, `PlayerResearchRecordTests`, `UiWindowAnimationTests`, and with the
game data `ParkFinaleCorpusTests`. No original game files are included.
