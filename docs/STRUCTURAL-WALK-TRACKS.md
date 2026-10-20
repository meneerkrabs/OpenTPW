# Track records in walk edges

The shared walk edge test now blocks supported ground track origins (11, 13, 16,
18 and 25) whose low flag nibble is zero. A type-12 extension uses its valid,
nonzero parent origin. The check occupies the already documented structural
check position: earlier path, queue and pool rules retain their priority, and a
nonzero nibble continues through the existing remaining rules rather than
unconditionally allowing a crossing.

Actual kart/water placement and bridge refresh provide the record and nibble.
The change does not introduce another track layer or change bridge production.
Original navigation queries the same edge rules during live movement, so a
track-only flag change is checked even without a map-version change.

WALK-005 remains open for type-17 records, zero or invalid extension parents,
the type-9 owner predicate for mask 2, and underground links. Unsupported records
retain their previous behavior. TRACK-010's path-underlay and bridge-production
limitations remain separate. This is not a complete original-game walk oracle.

`StructuralWalkTrackTests` uses synthetic maps, actual TrackBuilder producers,
edge/commit calls and live GuestNavigation. It covers ground/bridge origin and
extension behavior, all supported types and nibble bits, unsupported references,
earlier branch priority and a bridge flag change during an existing walk. C#
execution and existing-suite regression results are required before claiming
runtime validation.
