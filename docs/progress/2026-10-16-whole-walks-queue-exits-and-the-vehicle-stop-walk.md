## Whole walks, queue exits and the vehicle stop walk from tp.exe (October 16, 2026)

Follow-up to the path and queue work, read in tp.exe via REA:

- **One destination per walk** (WALK-004 retired): a guest walking to a ride (state 10) or out of the park (state 18) now sets one
  destination for the whole walk, as `0x4FFBC0` → `0x4FA530` → `0x4FA5F0` → SetDest `0x510100` (search `0x511420`, edge test
  `0x4D8750`, commit `0x510510`) and `0x500A50` (walk mask 1) do, instead of a fresh search from every cell.
- **Leaving a queue** (QUEUE-010, QUEUE-005): a guest leaving a queue stays where it stood (`0x5004AA` writes no position) until the
  state-6 handler `0x4FEC90` starts its next walk, and the original route search brings a guest whose cell left the queue back instead
  of sending it away Confused.
- **The vehicle stop walk** (ARRIVAL-001, WALK-008 narrowed): at vehicle status 1–3 a leaving guest walks to the vehicle's own stop
  cells (state 20, `0x500BD0`, `0x51AAD0`, status gating `0x51A760`/`0x51A9D0`/`0x51A690`), and the arrival lanes walk like every guest.
- **The guard's set-down** (GUEST-010 narrowed) walks to the drawn side's gate cell (`0x4D86D0`, walk mask 1).
- **The M3 queue gate** (GATE-006 narrowed) was re-enumerated over tp.exe's own walk (about 40 million walks): the per-cell time C moves
  from 9 to 27 updates with w₂ = 15, because the original reroutes short cornered walks around a neighbour's cell; long straight queues
  stay near 14 per cell.
- **Design choices, approved by the user:** WALK-003's per-cell lists (`[EXT:guest-cell-lists]`) and QUEUE-021's click at the back cell
  (`[EXT:queue-click-at-back-cell]`) are split out.

The pinned run hash moves (docs/DETERMINISM.md, `0x9DB7A7AAEA8D1CE2` after merging the one world generator, #235; before that `0x7AED41D19433D4B2`). The stop walk's draws
(`0x500AD0`, `0x500BD0`: the cell pick and the two sub-cell bytes) come from the shared world generator in the original's order. Saves gain no fields; state 20 is a new
accepted guest state, states 19 and 21 on the cell map carry no lane, and older saves load unchanged. A walk to the stop that ends stuck stays in state 20, as
`0x5019F0` leaves it. Verified with the full test run with and without the game data and the five native smokes.

Still open: the queue tool's line writer and per-cell owner word (QUEUE-014, `0x532FC0`, validator `0x535670`), the avoid-walls corner
branches (WALK-002) and the neighbour query order (WALK-003).
