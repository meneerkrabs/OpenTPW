# Wandering out of linked access cells

This bounded #517 slice lets the shared wander picker start on linked entrance
or exit cells (raw types 9 and 10). The existing footprint and saved-access-link
writers supply those cells; original guest import retains the saved position.
An imported deciding guest can therefore choose a path destination and use the
existing navigation search to leave that access cell.

The change only broadens the start predicate. Candidate filtering, draw order,
patrol-area checks, bridge rules and navigation edge checks remain in force.
An unlinked access cell still follows the caller's existing off-path handling.
Staff patrol uses the same picker and receives the same linked-start behavior.
No staff lifecycle or animation ownership changes are included.

Round 10 supersedes the gate: the picker starts on a cell of any type that has links and keeps every link the four removals of `0x4F9490` leave, to a cell of any
type (docs/GUESTS.md, retired GUEST-010; X19 in docs/reverse/ORACLE.md, `WanderGolden`). It still changes no walk mask or track-record edge rule (WALK-005). The
synthetic producer/caller tests (LinkedAccessWanderTests) ran in exact-head CI
before #517 merged; no original execution or rendered visual result is claimed.
