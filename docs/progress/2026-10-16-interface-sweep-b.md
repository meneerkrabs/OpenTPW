## Interface sweep B: list order, the trend arrow, the cash pointer and modal pauses from tp.exe (October 16, 2026)

A sweep over the second half of the interface approximations, read in tp.exe via REA:

- **Retired:** UI-093 (key events go to exactly one window, `0x6698e6`; the ride view takes the focus, `0x48ac40`, so the HUD keys are
  unreachable while riding, which OpenTPW already matched), UI-099 (the staff tools' queue face is type 1, red.tga in the texture table
  `0x763b38`) and UI-080 (a wheel notch moves one row, `0x46b600`, `0x665c35`; the row pitch is `[EXT:list-row-pitch]`).
- **List order** (UI-905, UI-907): the refresh handlers (`0x493230`, `0x4955e0`) rewrite cells in place and never move a row; a new row
  goes in through the sorted insert (`0x66403b`, compare `0x663edc`), and only a header press re-sorts. OpenTPW re-sorted on every
  refresh and now keeps the order.
- **The cash trend arrow** (UI-047, UI-051): at each date update (`0x4a0e30`) the arrow shows down when the newest month's money out
  exceeds its money in (rings `+0x1f5a4`, `+0x1fc94`, one entry per month close `0x4c7720`), replacing an invented 4-second window.
- **The cash pointer** (UI-100) follows the blueprint price against the balance (`0x523662`) and the path, queue and track line's money
  stop (`0x53593f`).
- **Modal pauses** (UI-096): the postcard and the delete confirmations (`0x48cd10`, the message box `0x47f020`) pause the park.
- **Read, text only:** the gauge crop (`0x477ff0`, UI-046), the slider ball's drag offset (`0x66bb9b`, UI-064), the candidate byte
  writer (`0x507600`, UI-081), the cell colours (`0x5f0700`, `0x5f2380`, UI-086) and the render-pass pick (`0x7b05a8`, UI-930).
- **Design choices, approved by the user:** UI-061 (`[EXT:entry-price-window]`), UI-100's pointer art (`[EXT:software-pointers]`) and
  UI-940 (`[EXT:lobby-fixed-step]`) are closed; UI-041, UI-905, UI-907 and UI-941 are split.

The pinned run hash and saves are unchanged. Verified with 4579 tests passing without game data, 5027 with it, and all five native
smokes.

Still open: the link and end pointers and the crosshair art (UI-100), the other message-box callers and the pause on app deactivation
(UI-096), the 30-entry history sums and satisfaction bars (UI-905), the gauge's red start, the per-visitor window `0x4b79b0`, the
finance graph and the ride settings windows.
