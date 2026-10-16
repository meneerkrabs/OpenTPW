## Per-visitor window, three camera and buy-list features, and interface readings (October 16, 2026)

**Built**
- **UI-910 narrowed, the per-visitor window.** Table `0x7575e0` is generated from tp.exe. The window shows cash, time in park, the four use counters, the
  happiness bar, the `KIDSTATES.str` status and the `THOUGHTS.str` text with its icon. It refreshes every 4 s (timer `0x80080`) and has the cycle arrows,
  `b_locate`, `b_kids` and help 302. A list row click and a world press both open it (the list gives way); Ctrl+V locates its visitor. Still open: the
  portrait figure (`0x4b6b90`) is empty, and the arrow order (newest first) is not proven the same as the original's thing list.
- **UI-102, UI-103 and UI-105 retired.** The park camera's middle-button turn, the map window's edge scroll and the buy list's golden-ticket rows are built
  from the original's routines.
- **Use counters (shared with the saves branch).** The guest's counters `+0x1c4`, `+0x1c8`, `+0x1cc` and `+0x1d0` are `Guest.RidesTaken`, `ShopsVisited`,
  `SideshowsVisited` and `SideshowsWon`, the same fields and optional save keys as the parallel `wave3/saves` branch, so one set of counters has one save
  representation. A finished visit counts by the type's `WhichUIType` (`0x4fd9b1`): a shop or a sideshow is no ride any more. The All Visitors rides
  column reads `RidesTaken`. GUEST-005 no longer lists `+0x1d0 += 1` as unfollowed.

**Narrowed**
- **UI-104** (Move tool): the placement runs through `0x528a70` with flag `0x8186d4` set, so it is free; the pick-up erase `0x527ee0` credits scrap percent
  of the stored price, and whether that applies to a move is open.
- **UI-095, UI-101, UI-106, UI-930:** the readings of the cell link byte writers, the projection builder (90 degrees for both axes, Home pose), the gauge art
  decode (the sprite setup writes no vertex colour; tp.exe holds no D4 filter constants as floats) and the visitor pick circle (pointer mapping, radius formula).
- **UI-942:** `0x521e60` is the particle creator, not a sound; the enter-park routine starts screen-space particle effect `0x62` over the key counter. `docs/AUDIO.md`
  is corrected. The ID itself became `[EXT:online-globe-audio]` on main.

**Determinism.** The pin moves from `0xDB3EA9A7AF384B5B` to `0x0487112FF9FF20DB`: shop and sideshow visits no longer count as rides, and the three new
counters join the guest digest. This is the value `wave3/saves` reaches for the same two causes, so the second merge keeps one pin.

**Verification.** See the PR description for the test, register, evidence and smoke runs.
