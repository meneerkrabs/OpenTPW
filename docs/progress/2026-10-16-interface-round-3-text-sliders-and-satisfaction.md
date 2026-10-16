## Interface round 3: the text formatter, the options slider, the pause-menu ramp and the satisfaction history (October 16, 2026)

A third pass over the interface approximations, read in tp.exe via REA:

- **Text wrapping** (UI-032 retired): the original's formatter flags only space and tab as break opportunities (`0x6aeab0`);
  every text control wraps through `0x6ae930`, `0x6aeb10` and `0x6aec80`. OpenTPW's glyph classes now match. The small-font
  fallback stays an `[EXT:fit-button-text]` choice.
- **The options slider** (UI-036 retired): the slider class grabs the thumb on a press and moves one page toward a click on
  the track, as `0x66b088`, `0x66af19` and `0x66b8aa` do; the options page uses the range 0..100. The step snapping stays
  an `[EXT:options-slider-steps]` choice.
- **Option label size** (UI-039 retired, judgement call for review): the options labels use font slot 6 of the bank and their
  setup (`0x4a43b0`) does no fitting, so the original has no fit rule to compare with. What the original does with an
  overflowing label is covered by UI-032's formatter. The fit rule stays an `[EXT:option-label-fit]` choice.
- **The pause-menu hover ramp** (UI-013): a line's colour follows tp.exe's line procedure `0x48b2a0` and its step `0x48b220`.
  Presentation only.
- **The customer satisfaction history** (UI-024, ECON-057): a finished use averages 3 x the guest's happiness byte change into
  the object's satisfaction ring (`0x4fd970` -> `0x4e1e00`); the shop and sideshow windows and the All Attractions list draw
  the gauge from it (`0x4e1e30`). Saves are unchanged: an empty ring still reads 50.
- **The buy window's footprint thumbnail** (UI-026): control `0x1eb` (record `0x754d50`) shows the selected item's shape as
  8 x 8 half-transparent cells coloured by cell class (`0x52c5b0`, `0x4ab1b0`).
- **Narrowed evidence notes**: UI-006 (survey of the `0x65c5d5` colour call sites), UI-007 (the control's `+0xdb` is no shadow
  flag, and `0x588c20` releases sprites rather than drawing), UI-008 (tp.exe holds no `purple_button` string), UI-018 (the
  original's lobby update has no globe branch), UI-027 (the sprite-pick leads), UI-037 (the `0x588c20` correction).

Determinism: the fixed-run pin moved `0x0487112FF9FF20DB` -> `0x01946A39A53F851E` (main after #271) because the economy state now
holds the satisfaction rings' values (`docs/DETERMINISM.md`). The branch alone had moved `0x205523C08061A05D` -> `0xAEB43E01FFE5FEE0`.
No draws were added to the world or guest random streams.

Split off UI-013 (user-approved): the pause-menu line's hover ramp driven by OpenTPW's pointer focus is now
`[EXT:pause-menu-hover-focus]`; UI-013 stays open for the measure `0x4929f0` and the online state's top of the menu lines.
