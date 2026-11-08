## UI page-grouping frontier recorded (November 8, 2026)

Documented the page orchestration that still stands between the ported pure helpers and a runtime change: the frame driver `0x58F940` that clears six width-class counters and creates a new page from the first texture that fits nowhere (rounded up to 8), and the manager `0x58F190` with its page table (`6 x 256` slots), per-class limit/divisor/used arrays, shrink-on-misfit and class-ratio choice, merge-plus-allocate, placement sort and release paths.

The note states plainly why this cannot be ported as pure code yet: `0x58B330`, `0x590350`, `0x590FF0` and friends are the device side that creates and fills the page surface, and OpenTPW has no page surfaces at all. It also records that a page's size comes from the first non-fitting texture rather than from a fixed 256/128/64 choice, which corrects the older wording.

Also corrected two allocator notes from the previous review: the `0x57CED0` comment no longer calls the routine `OriginalDepthSort` (it is a separate routine that shares the partition structure), and the free array now has the native capacity of 128 records. No allocator behaviour changes and its goldens still pass. UI-004 stays open; no APPROX/EXT, save field or determinism pin changes.
