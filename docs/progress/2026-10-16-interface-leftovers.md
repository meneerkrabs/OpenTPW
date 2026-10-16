## Interface leftovers: the employee figure, finance graph colours, the research knob and map colours from tp.exe (October 16, 2026)

A second pass over the interface approximations the first sweeps left open, read in tp.exe via REA:

- **The staff list's row pitch** (UI-080 retired): the hire list's row factory `0x49bbb0` uses font slot 7 and the buy list's
  `lineHeight × 1536 / screenHeight + 6` rule, so the staff list now uses that pitch and shows 11 rows.
- **The employee window's figure** (UI-092): the portrait frame plays the employee's walking figure (`0x4b5330`), as the hire window does.
- **The finance graph** (UI-060): the eight series colours are the original's records from `0x499f30`, in toggle row order (table
  `0x751dac`); bank balance and park value may show together, and either clears the six cash-flow series and the other way round
  (`0x49a2e0`, masks `0xfc` and `0x03`).
- **The research knob** (UI-064): a drag that starts on the ball moves it by the pointer's movement, clamped to the track (`0x66b771`).
- **The staff dismiss box pauses the park** (UI-096; `0x4b5eac` is a caller of the message box `0x47f020`).
- **The map's cell colours** (UI-086): paths, queues and unowned land use the original's records (classes 10–12 of `0x5f2050`, filled by
  `0x5f2380`); the full class table, the layer bits and the 0..100 ramp are read.
- **The happiness gauge** (UI-901 narrowed): the gauge now takes the original's value, `ftol(mean x 1024 x 0.01)` of the byte-truncated
  mean of the in-park visitors (so a full mean gives 1023 of 1024), 0 while the park is closed, resampled every two seconds
  (`0x4a1cd0`, `0x4c7bb0`; the HUD builder is `0x4a1d70`).
- **Closed as design choices (user-approved):** UI-044 (`EXT:list-row-pitch`, the buy, loans and load lists keep one fixed pitch
  where the original's follows the screen height) and UI-942 (`EXT:online-globe-audio`, the online globe's music and ambience);
  UI-091 is split: the visitor info window that Ctrl+V tests is `EXT:visitor-info-window`, the camera focus (`0x486860` → `0x42aab0`)
  and the per-frame blend (`0x42b1c0`) stay open.
- **Narrowed with new reads:** UI-003, UI-004, UI-025 (the message tube has 8 slots with priority eviction, `0x59a940`, `0x59abc0`),
  UI-031, UI-034, UI-042, UI-047, UI-050, UI-051, UI-082, UI-091 and UI-901.

The pinned run hash and saves are unchanged. Verified with the full test suite with and without game data and the native smokes.

Still open: the UI model triangle draw (UI-003, UI-004, UI-034), the message tube's per-type caps (filled at run time), the employee
rename dialog, the graph's line style, the object cells' map value `+0x44`, the gauge art and the sprite list that turns the figure scale
into pixels.
