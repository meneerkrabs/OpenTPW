## Interface sweep A: the Load Park date, the lobby enter sequence and the window title colour from tp.exe (October 16, 2026)

A sweep over the first half of the interface approximations, read in tp.exe via REA:

- **The Load Park date column** (UI-013): each saved park row shows its local save date and time, right-aligned, as `0x49ec80` builds it
  (the file's last-write time to local, UITEXT `0x1c0` filled with day, month and year by `0x5fc660`, then `" %02d:%02d"`; the row
  layout of the cell factory `0x49f170`). Shipped parks and the sandbox have no file time and show no date.
- **The lobby enter sequence** (UI-018): starting a park from the island menu hides the lobby UI and plays `0x5e0490` states 1 and 2:
  the camera turns to the island angle + π at 0.05 × step per frame, the gate plays M1 (`0x5e06e4`), then the radius shrinks by 0.07 ×
  step × radius and the height by 0.6 × step × height per frame while the screen fades to black (`0x5d8cf2`), and the park starts below
  radius 8 (`0x5e1e50`).
- **The window title colour** (UI-006) is (234, 239, 102) from `0x492d20`, shared by 15 window builders, instead of a video estimate.
- **Narrowed:** UI-036 (the options change handler `0x4a2e90`: resolution (v × 2) / 100, quality (k × v) / 100, raw 0..100 volumes),
  UI-007 (the single glyph draw entry `0x6ae0f0`; no shadow in the font art), UI-026 (the preview matrix `0x468e50`, x × 0.75, depth ×
  0.1), UI-027 (the original picks along a cursor ray, `0x45bf90`, `0x45cdc0`, `0x45d090`) and UI-004 (the UI depth range of the
  display object, `0x5833c0`).
- **Design choices, approved by the user:** UI-002 (`[EXT:ui-font-tier-by-scale]`), UI-005 (`[EXT:ui-linear-filtering]`) and UI-012
  (`[EXT:ui-modal-right-click-back]`) are closed; UI-013, UI-025, UI-026, UI-032, UI-036 and UI-039 are split into an extension and a
  narrowed remainder.

The pinned run hash and saves are unchanged. Verified with 4579 tests passing without game data, 5027 with it, and all five native
smokes.

Still open: the UI model draw code (UI-003, UI-034), the text tint consumer of `+0x78` (UI-037), the front-end buttons (UI-008, needs a
capture), the ride settings window (UI-024, UI-028), the message queue behind UI-025, the coaster and track tool sub-modes (UI-031), a
ray pick against object geometry (UI-027), and the lobby's cloud layer, fog and key award (UI-018).
