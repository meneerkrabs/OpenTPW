## Coaster rolls, reopened circuits, the ride-view car and car poses from tp.exe (October 16, 2026)

Four coaster items were settled in tp.exe, read via REA:

- **Sample rolls from the second rail** (COAST-017 retired): a sample's roll is the angle of the second-rail point about the sample's
  tangent (`0x440c10`: the rail point turned by −yaw and −pitch with the PC table, then atan2(−x, y)); slice and car-frame rolls add the
  lerp of the nodes' tag rolls by the ordinal fraction (`0x43e6b0`), and a suspended coaster's frames lerp the swing's sample rolls
  instead (flag 8, `0x43e86e`; controller `+0x3c` bit 8 is the suspended copy, `0x4369f5`). The second rail is knot + (0, 1, 0), knot +
  2 × the tag's up row on a loop node, or (−sin a, 0, −cos a) on a model-less loop node, fitted with its own chords (`0x430ed0`,
  `0x4321c0`, `0x432840`). The node tag roll (`+0xe4`) is written only for a posed pylon without node flag `0x40`, so loop and model-less
  nodes now roll 0 rather than their bank. The knot record's `+0xc` fraction is read as 0 (no store to it was found in
  `0x430000..0x448000`).
- **Editing a circuit with riders and reopened circuits** (COAST-030 retired): no edit is refused for riders (`0x446300`, `0x446740`); in
  move mode the validator skips the turn test where the turn node and both neighbours carry both cut flags (`0x4348af..0x4348d8`), and
  re-validation stops at a cut end (`0x434e06`, `0x434ea1`). The original has a close click only on a reopened circuit (`0x435020` →
  `0x442c70`) and completes a fresh one by `0x435570`; OpenTPW's close step for a fresh circuit is labelled
  `[EXT:coaster-closing-check]`.
- **The ride-view car** (COAST-011 narrowed, UI-094 narrowed): the car comes from `0x436880`, the train with the smallest position
  (controller `+0x124`, `0x43d7dd`, the `0x43ce20` loop), then train `+0x34`: car 0, or car 1 behind a front car of another type
  (`0x436d10`, `0x436f15`). The Mac leads turned out to be the lobby islands code; no reader of FIRSTPERSONVIEWDISTANCE,
  COASTERSMOOTHNESS or `Info.FirstPersonSprites` was found.
- **Car poses** (COAST-034 narrowed): `0x43d8f0` builds the car's root matrix from identity, discarding the model's root rest (the mine
  carts, Candy cars and caterpillar have a root turned 180° about Y), so meshes and head sockets are now baked root-relative. Non-bend cars
  play `<model>m` at 0.1 × train speed (`0x43ce20` → `0x474050`), UV-only tracks on the wheel meshes; the two flume boats are the only bend
  cars, ported from `0x438ab0` and `0x438800`.

Also fixed: `Vector2.Equals(Vector2)` called itself, so `==` and `!=` overflowed the stack. The run pin is unchanged; coaster hash content
changes because sample rolls feed the ride's hash, and sample rolls are now near zero where node banks used to enter the rating step.
Saves gain no fields (samples are re-derived on load). Verified with 4464 tests passing without game data, 4912 with it, and all five
native smokes.

Still open: the wheel clip's start stamp, the bend's use of this build's sample yaw, riders not bending with their boat, the boats' own
M1..M3 clips (no player in the coaster code), the first-person view options and the hovered closing link of `0x43eb70`. The cursor node is
not in the cell's node list (`0x434060` calls `0x433bd0` only when `param_5 & 2` is clear), which settles the stack-part question from the
pylon review.
