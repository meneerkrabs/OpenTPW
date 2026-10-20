## TRACK-032 structural decoding (October 20, 2026)

Added `SaveTrackRideBlock` and synthetic malformed-input/field-preservation tests. Native evidence corrects two earlier descriptions:
chunk 3 is 40 serialized bytes (0xd0 is the native ride stride), and chunk 6 selects from a distinct 0x1c waypoint chain reconstructed
from chunks 3/4, not the pool-ordered raw 0xac chunk-5 records. See the [updated proposal](../proposals/track-032-restore-saved-controller-semantics.md)
and [wire contract](../TPWS-PAYLOAD.md#trackrides-structural-reader-track-032-remains-open).

The reader preserves opaque data, stream order, duplicates and repeated reference requests. Nonzero-owner waypoint keys stay unresolved.
Typed payload lengths enforce the native writer's shape, explicitly stronger than the native loader's malformed-input handling.
No `Level.Tracks`, controller restoration, fresh-walk replacement or runtime identity policy changed; TRACK-032 remains open.

Validation: whitespace/diff checks performed locally. No local .NET execution was attempted because of the known IPC restriction;
compilation and synthetic test execution require exact-head CI. Static native evidence was reviewed independently before implementation;
the implementation requires its own source review before publication.
