## BF4 text placement follows the original's text library (October 17, 2026)

tp.exe's text library places a text block in its box and each line inside the block with fixed integer rules (`0x6aeb10`, `0x6ae550`), and
its common glyph draw (`0x6b4aa0`) puts a bitmap at pen plus the record's signed offsets with the pen y at the line top, which confirms
OpenTPW's reading of the BF4 metrics. `UiContext.DrawText` now uses the same rules (`TextPlacement`, `TextLayoutResult.AlignLines`): a centred or
right aligned multi-line text moves each line inside the widest one by half or all of the difference (before, every line stayed left aligned
in the block), the block is placed in whole pixels with signed truncating centring instead of float rounding, and a block taller than its box is
centred like the original does (its bitmap cuts it off at both ends) instead of being put at the box top. Single-line text moves at most one pixel (truncating instead of rounding). The wrapped fallback of the `[EXT:fit-button-text]` fitting (a label that fits at no size) keeps its previous placement on purpose, so the approved fitting does not move; OpenTPW does not clip an overflowing block to its box as the original's control bitmap does.

Also read: the measure `0x4929f0` returns the block box (widest line by lines times line height, nothing wraps without the wrap flag), which is
what OpenTPW's layout box already is, so that part of `UI-013` is closed; the text bitmap is in the display mode's pixel format and the 5-5-5 glyph
blitter blends linearly in signed 5-bit arithmetic with no gamma (`UI-007`/`UI-037` now say what the sprite draw still has to answer).
Details in `docs/BF4-FONTS.md`.
