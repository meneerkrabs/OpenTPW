## Initial track placement respects co-op claims (October 27, 2026)

A fresh kart or water station checks its adjacent entry and exit operations before purchase or registration. The preflight covers objects the demolition would remove, claimed track owners on offset origins and extension cells, and path cells whose clearing could unlink another ride's queue. A refusal leaves the complete placement untouched, rather than skipping individual operations after buying the station. Guests' fixed-object checks still run when no ride has an active claim.

The preflight shares the track writer's existing demolition gates: an existing origin without an add-on skips the tile, and neighboring station cells remain protected. Authorized scenery demolition remains unchanged. The optional last-line flag and legacy track-undo decoding are unchanged.

Added synthetic regression cases for kart and water rides in all four rotations, both adjacent tiles, claimed objects, fixed objects without claims, queue joins, offset track owners, atomic refusal, and an accepted placement beside a claimed station. These C# tests have not been executed in the authoring environment because the .NET SDK is unavailable; compilation and runtime validation remain required.
