## Initial track placement respects co-op claims (October 27, 2026)

A fresh kart or water station checks its adjacent entry and exit operations before purchase or registration. The preflight covers objects the demolition would remove, claimed track owners on offset origins and extension cells, and path cells whose clearing could unlink another ride's queue. A refusal leaves the complete placement untouched, rather than skipping individual operations after buying the station.

The preflight shares the track writer's existing demolition gates: an existing origin without an add-on skips the tile, and neighboring station cells remain protected. Authorized scenery demolition remains unchanged. The optional last-line flag and legacy track-undo decoding are unchanged.

Fixed park items retain their existing empty footprint and identity-based guest-removal protection: they are not adjacent occupied-cell victims. A separate regression checks that a guest's sale of a fixed item is refused without active claims, while an accepted station placement leaves that item registered, standing and unoccupied. The no-claims fast path is unchanged.

Added synthetic regression cases for kart and water rides in all four rotations, both adjacent tiles, claimed objects, queue joins, offset track owners, atomic refusal, and an accepted placement beside a claimed station. The first native CI run compiled but exposed an invalid fixed-item occupancy assumption in the test fixture; the corrected fixture preserves all claimed-object occupancy and atomicity assertions. The corrected C# tests have not been executed in the authoring environment because the .NET SDK is unavailable; compilation and runtime validation of the correction remain required.
