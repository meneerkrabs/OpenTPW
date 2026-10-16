## One world generator, as in tp.exe (October 16, 2026)

OpenTPW's separate random streams (per guest, per script, the economy's, the bump rides') are replaced by the original's one world
generator, read in tp.exe via REA:

- **The generator** (`WorldRng`): `0x516330`, s = rotr(s × 1664525 + 1013904223, 13), the rotated value the new state and the result its
  absolute value, plus the reseed `0x516370`. Its state is the world's `mRandomSeed` (`+0x1DA708`, written at `0x517123`, read at
  `0x517DFC`, label `0x762924`), so an original save's generator state is now imported (SAVE-001 retired). Test vectors come from
  emulating the routine, including the `0x80000000` edge.
- **Scripts, rides and heads** (DET-013 narrowed, HEADS-001 retired): RAND (`0x55398A`: `shr 1; abs; idiv (int16 + 1); abs`, reading
  the operand's low 16 bits, so `RAND V0, V1` uses the variable's index, bug-compatible), FINDSCRIPTRAND (`0x5560CB`), ADDHEAD
  (`0x554CB5`), the kart and boat draws (`0x54A040` family) and the tours (`0x55A770`, `0x55DB41`) draw from it; placed object scripts no
  longer carry per-placement seeds.
- **Guests** (DET-014 narrowed): creation draws in the original order (base speed `0x4F89E1`, the eight starting values
  `0x4FAFF8..0x4FB114`, the reseed `0x4FB19E`, sprite variant `0x541F85`, an arrival's entrance side and sideways byte `0x4FB201`,
  `0x4FB21C`), the balloon and costume reseeds (`0x4FE6A1`, `0x4FE6FC`) and the thought pick (`0x501919`).
- **Economy and staff** (DET-015 narrowed): the staff pool, staff walks, the rest gate, the no-rest-area hit, the guard set-down, the
  handymen and the litter reseeds (`0x4D94E2`, `0x4D71CA`). Weather keeps its own stream (`[EXT:weather-stream]`); presentation (audio,
  particles, weather visuals) stays off the generator, which a test enforces.

All 139 direct draw sites and 4 inline copies were mapped to their consumers. The pinned run hash moves from `0x09894103FAFB8D45`
(schema 19) to `0x7AED41D19433D4B2` (schema 20); two path digests move with it (docs/DETERMINISM.md). Saves gain an optional
`WorldGenerator` state, carried as a marker by the guest and script snapshots and checked for agreement on resume; saves from before the
port derive it deterministically from the saved world seed or the old economy state (`[EXT:legacy-generator-state]`). Verified with 4609
tests passing without game data, 5057 with it, and all five native smokes.

Still open: the original's update order (the substep scheduler with the thing sweep newest first, the economy month end against it), the
reseed by the original's thing id rather than OpenTPW's guest id (WALK-010), the entrance cells as world words `0x7855DC..E4`, guest
state-handler draw sites with no OpenTPW counterpart, hired staff's person-constructor draws, and the advisor pick `0x59A550`.
