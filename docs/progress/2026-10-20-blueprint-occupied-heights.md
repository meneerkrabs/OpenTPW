## Bounded TPW occupied blueprint heights (October 20, 2026)

Implemented a read-only preview-height adapter for proven live TPW object owners (primary types 4/9/10), using
existing rotated height-map sampling. Added native signed quantization, raised tops, neighbour-stepped side walls,
and stable height snapshots so changed occupancy rebuilds topology while ripple-only updates preserve it.
[PC evidence and remaining coverage](../reverse/BLUEPRINT.md#bounded-occupied-cell-height-implementation-october-20-2026).

Synthetic tests cover signed/fractional boundaries, non-square maps in all four rotations with a nonzero base height,
wall directions and texture policy, terrain/ripple preservation, map boundaries, height-snapshot changes and conservative
unsupported surfaces. No original media or execution is used. Local C# build/tests are unverified because restore was
blocked on unauthorized metadata access; no retry or bypass. Published CI results must be checked separately.

BLUEPRINT-001 stays open for unsupported/missing-map and secondary surfaces and TPI. No placement, marker-selection,
track-priority, economy or save behavior changes, and no approximation retirement.
