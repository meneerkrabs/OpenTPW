## UI window fidelity round 8 (October 18, 2026)

The type-9 gauge bars of the buy, ride, shop, sideshow, hire, visitor and employee windows now draw the original art instead of video-read colours:
`ridestatbar.wct` (top half the bar cut off at value / 1024, bottom half the track; `0x477ff0`, `0x478400`) and, for the happiness bars (the visitor
windows, the employee window's 0x513 and the local-happiness rows of the shop and sideshow windows; the string `0x7508c0` is handed to the gauge builder
at `0x493bc5`, `0x4b7205`, `0x4b56f5`, `0x4b018c`, `0x4b1681`), `happygrad.wct` (blue through orange to yellow). UI-046 is retired.

The hire window's skill gauge is the candidate's grade byte x 1024 / 5 (`0x49b8e2`), not (grade + 1) / 5: the video's guard candidates ($60, $75, $105 are
the Easy_Standard grades 1, 2, 3 at multiplier 15) show a fifth for the cheapest, which is grade 1. UI-081 is retired. The All Attractions list's bars use
the same `(value << 10) / 100` scale as the object windows (`0x4955e0`), which retires UI-905.

The buy list's owned column draws the model `i_boxtick` (frame 0 the box, 1 the tick) instead of strokes; the star state (frame 2, the three most
recent items of a category, ring read by `0x4caa70`) is not drawn (UI-050 narrowed). The finance graph draws one-pixel lines and, in the 1 and 3 year
views only, a hollow 3 x 3 pixel mark at each sample end (`0x47fe90`, `0x480460`; UI-060 narrowed to the pixel size and the grid). UI-064 is corrected
(a track press moves the slider by its step toward the pointer, `0x66b8aa`), UI-092's patrol help is already posted, UI-907 is narrowed to the text
control's alignment byte (+0xd8).

The pinned run hash and saves are unchanged. EXT candidates awaiting a decision: UI-041 (the Load Park game type, a consequence of `[EXT:no-player-profiles]`
and `[EXT:lobby-load]`), UI-051 (a player-wide ticket ledger), UI-096 (pausing on application deactivation: the original pauses with `0x4092a0` at `0x46b74c`).
