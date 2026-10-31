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

WALK-005 is retired (round 11 package E). The edge test reads the target's structure record as `0x4D89C3` does: `0x5363F0` (kinds 11, 13, 16, 18 and 25, an extension through
its parent word, none when it is missing), then the flag nibble of the record `0x4D89E4..0x4D8A0F` takes. The parent word is the cell index plus one, so cell (0, 0) is a real
parent. The fence is part of the second layer: a post (kind 25) and the three cells beside it block a step onto them unless the post's flag nibble is set, whatever the walker
stands on. Kind 17 has no writer in tp.exe (every caller of the type op `0x538FC0` passes 12, a tool's type or 0). The cell types the tests read are the map cell's own
(`ParkCellMap.PrimaryTypeAt`), which a track tile never changes. The underground link byte `+0x22` is read only while the land tools' flag `0x81B4CC` is set, inside straight-line
land routines that run no walk. Mask 2 is the camera's alone (`0x42C093`, `0x42C290`, UI-094/UI-095), with its rule for a type-9 target ported. TRACK-010's path-underlay and
bridge-production limitations remain separate. This is not a complete original-game walk oracle.

`StructuralWalkTrackTests` uses synthetic maps, actual TrackBuilder producers,
edge/commit calls and live GuestNavigation. It covers ground/bridge origin and
extension behavior, all supported types and nibble bits, unsupported references,
earlier branch priority and a bridge flag change during an existing walk. C#
execution and existing-suite regression results are required before claiming
runtime validation.
