## Track pieces: arm masks, water clips and the add-on reading from tp.exe (October 16, 2026)

Three track items were settled in tp.exe, read via REA:

- **The circuit walk posts no message** (TRACK-006 retired, OpenTPW already matched): the walk `0x52A970` and the walk sites of the tool
  handler `0x524960` never call the message builder `0x59B590`. The four ids the register cited are tool help, not walk messages: `0xCC`
  is the kart/water tool's help for a click with no line start (`0x526D41`, reached only after a Ctrl-held closing click with no queue to
  follow, UI-031), and `0xB8`, `0xD6` and `0x151` belong to the path and queue tools (`0x5272F5`, `0x527351`, `0x527620`).
- **Arm masks and water clips** (TRACK-018 narrowed): the water and kart instancers `0x522D50`/`0x522B90` pass the low byte of the piece
  table's arm entry (`0x7634E0`) to `0x463060` → `0x44B140`, which hides every dummy node whose third word shares a bit with it, so a
  straight shows no end caps and a tee keeps only its missing arm's cap. BUMP 17 (`0x5549ED`) compares its value with the script word
  `+0xE6` (−1 on attach `0x558C71` and on load `0x559F29`) and otherwise walks the pieces from the exit along the flow (`0x52A490`) and
  from the entry against it (`0x52A700`), triggering clip 5 or 12 with flags 7 on each; the placement walk (`0x524D0A`) starts the pieces
  on a valid circuit and the track-line walk (`0x526F02`) stops them; a piece chosen again gets a new instance with no clip (`0x53AFA0`).
  The space kart script issues BUMP 17 too.
- **Track add-ons** (TRACK-008 narrowed, read only): placement `0x528A70` needs footprint codes `0xB`/`0x10` on kart or water track and
  writes cell flag `0x10` (`0x53A510`), the walk builds sections of kind 12/13 (`0x52AC68`), and the vehicle hook `0x548CA5` → `0x548E80`
  (the Space Racers Teleport, `0xDAC`) hides a vehicle within ±`0x4B0` of the first tile. Nothing was built, because where the original
  sells a ride's add-ons and the cell writes of `0x532FC0` are not traced yet.

Presentation only: the water command is neither saved nor hashed (a restore resets it to −1 as the original's load does), the piece
clips run on a presentation clock, and the determinism pin is unchanged. Verified with 4457 tests passing without game data, 4904 with
it, and all five native smokes.

Still open: the edit sub-modes `0xE`/`0x13` (`0x526BD0`), texture-frame piece clips (the Crypt Karts' bridge fires), the add-ons' sale
and placement writes, and the backward piece walk that starts one cell off the grid at rotation 90 (related to TRACK-005).
