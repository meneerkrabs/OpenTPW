## Rides, coaster and track reading round (October 15, 2026)

Twenty-nine ride, coaster and track approximations were read again in tp.exe:

- **A build click becomes the anchor the original's way** (`0x528b44`: the click lands on the footprint's local (W/2, H/2)), so even-sided
  objects no longer land one cell off (RIDES-021 narrowed).
- **The coaster build cursor** keeps its last cell beyond 300 units from the last node (`0x435e2a`; COAST-035 narrowed).
- **Track tiles become bridges only over paths and queues** (`0x538b0b`), not over terrain water, which map setup turns into type 0 inside
  the park (TRACK-010 narrowed).
- RIDES-012 (entrance and exit outside cells, `0x529323`, `0x529239`) and RIDES-013 (the footprint stamping is a rigid rotation about the
  anchor) are retired; 16 more items are narrowed, among them the blueprint ghost (`0x532fc0`, a draped, rippling marker) and the
  coaster cost's x87 arithmetic (`0x447830`).
