## Footprint removal layer-isolation regressions (October 22, 2026)

Added three asset-free tests for the existing ParkCellMap.ClearFootprint host contract:
192 add-on removal combinations (+/#, six track map types, all sixteen bridge-bit masks),
eight mismatched-type cases also exercising four out-of-bounds coordinates and a free
shape cell, and a real WriteFootprint-to-ClearFootprint '@' path-link lifecycle.

The free-cell fixture uses an empty map cell with nonzero metadata, so removing the
free-type guard would clear it and fail the assertions; the out-of-bounds inputs verify
no-op behavior, not an independent mutation test of the bounds guard.

The add-on matrix asserts all retained TrackCell fields, map metadata, unrelated map
flags, adjacent sentinels, type counts and track counts. Repeated removal must not write
the track layer when its AddOn bit is already absent. The '@' case checks reciprocal
unlinking, clearing the removed map cell's owner/links/count, preserving its independent
flags, and leaving the second-layer track record untouched. These deliberately synthetic
removal-time states specify isolation of existing host operations; they do not claim that
every seeded metadata combination is produced by original-game placement.

Production code is unchanged. Existing ParkCellOwnerTests covers basic add-on flag
removal and track survival; these tests additionally protect exact preservation of the
rest of each record and guarded no-op cases. Local work consisted of source inspection
and static checks. C# compilation and test execution have not run; CI remains required.
No .NET process, OpenCode provider, browser, original executable or game media was used.

ECON-050 and proposal #513 remain open. The native demolition helpers' per-type writes
are not established by these synthetic tests, and no approximation is retired.
