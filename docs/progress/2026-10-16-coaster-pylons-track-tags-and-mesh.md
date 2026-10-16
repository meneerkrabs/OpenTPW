## Coaster pylons, track tags and mesh (October 16, 2026)

The coaster's pylons and track mesh read in tp.exe and built (COAST-015, -033, -026 and -035 narrowed, COAST-028 narrowed by one clause):

- **Knots from the pylon models' posed track tags** (`0x430ed0`, tag tables `0x4344c0` / `0x430590`): the knot direction is the tag's
  forward row (`0x4321c0`), a turned-back pylon's tag is turned by −π first, the tag roll comes from the up row, the lower nodes of a stack
  get no bank and the three-tag table (`0x432d50`), the sampler walks every knot (`0x440ad0`) and the end node's knot is S0's track dummy
  (`0x435570`). `fTrackHeightAdjust` only lowers the drawn cross section (`0x42d6d0`); the Big Dripper's track was lowered twice before.
- **Pylon stack parts** show or hide by the pylon's place in its stack (`0x432df0`), and the half-turn flag `0x10000000` lives in the node
  (`0x431f06`); the PC slerps its rotations (`0x4742a0`).
- **The track mesh** is built as the original emits it: slice lists of 11 values per link (`0x43eb70`, `0x43f030`), one shared vertex ring
  per slice, single-sided strips wound like `0x43f4e0`, crossings and the cursor link drawn in the invalid cross section (`0x42d5a0`) with
  the appended marker textures; the build cursor's ribbon is gone (blue, or red on a refused cell).
- **Review fixes**: an open chain's link ends at the last node's first knot (`0x43eb70` loops only while a next node exists), so a bare
  station or a cursor chain no longer gets a stub of the last node's tag pairs; the triangle parity (`0x7923e4`) carries across the chains of
  one rebuild; a knot's coordinate scales the rest by the cosine to the curve point at the new t (`0x4410e0`, `_isnan` guard `0x67b410`);
  the closing sample has roll 0 (`0x440c10`). COAST-017 now covers only the other samples' rolls (the second rail is not built;
  `CoasterKnot.TagRoll` is read by tests only).

Still open: the cross-section runs and blends (`0x4402d0`), self-illuminated lighting and the Add-mode UV animation, the model shadow, and
the clamped height parameter's write-back. The coaster hash content changes for catalogue coasters (docs/DETERMINISM.md); the run pin is
unchanged.
