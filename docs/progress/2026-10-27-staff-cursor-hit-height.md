## Preserve the staff cursor's picked height (October 27, 2026)

The staff hire/carry presentation now keeps the cursor pick's full engine-space
terrain-or-model point, instead of reducing it to cell coordinates and sampling
terrain height again, so over a ride model the person in hand no longer drops to
the ground. tp.exe's tool frame (0x46C480) subtracts the constant -5.0 (0x6FE974)
from the height component of the picker point (0x7A1C90), i.e. lifts it 5 height
units; the sprite anchor adds exactly that along the engine height axis
(`ToEngine` maps model height to engine Z at `ModelScale`, the same axis and scale
as `GuestRenderer.CellToEngine`). Cell mapping, hover validity, drop rules,
pointer admission and the cell-only terrain fallback are unchanged.

Synthetic CPU regressions cover elevated tower hits in hire and carry,
model-to-terrain refresh, a miss and cancel, a model behind terrain, and
stale-height clearing on cell-only updates and tool replacement. GUEST-021 is
narrowed to the open question of where the original's cursor and sprite anchor
land on the screen (PC video 81:30).
