## Queue rules: condemned rides, closed parks, edited coasters and the queue room from tp.exe (October 16, 2026)

Four queue items were settled in tp.exe, read via REA:

- **Broken versus condemned** (QUEUE-019 retired): `0x4E14E0` reads the life gauge (`fld [ride+0x48]`, `__ftol`) and sets state 1 (broken)
  for a nonzero low byte, state 4 (condemned) otherwise; `0x4E0E60` runs the head step for states 1, 2 and 4 alike, writing open word 0
  and VAR_RIDECLOSED 1, and a queued guest's move-up holds only in state 1 (`0x4E0370`). The queue bridge now mirrors the life gauge and
  keeps state 4 apart, so behind a condemned ride's dismissed head the guests walk on.
- **A closed park** (GUEST-011 narrowed): `0x519EF0` only closes the openable objects (`0x4DF300`); a closed ride's admission
  (`0x4E1220`) runs only the head step `0x4E0450`, one head per update, and state 6 sends the guest home. OpenTPW's park-close pull-out is
  gone; a guest walking to a ride while the park is closed drops its target and goes to state 6 (`0x4FFEB9`, "The park has closed
  underneath me").
- **An edited coaster** (COAST-024 retired): the counts test `0x441970` is called only by `0x4DD920`, `0x4DE1F0`, `0x4DF290`,
  `0x4DF390`, `0x4DF8F0`, `0x4DFE30`, `0x4E0050` and `0x4FFFF0`, never by the join (`0x4FFBC0`) or the admission (`0x4E1220`). The
  editing flag (`0x790FEC`, set through `0x430AC0` by `0x447520`) now joins the ride's acceptance; an open ride whose coaster refuses
  guests still calls its head forward, and queued guests leave through state 11.
- **The queue room** (QUEUE-005 narrowed): `0x501390` dismisses only guests beyond the room (position ≥ 4 × cells, not in state 14);
  OpenTPW no longer re-walks the guests that stay, which is left to state 11 (`0x501160`).

The determinism pin is unchanged. The ride snapshot gains an optional `Condemned` flag (valid only with `BrokenDown`, hashed only while
set); older saves load as state 1. Verified with 4453 tests passing without game data, 4897 with it, and all five native smokes.

Still open: a guest whose cell left the queue still leaves Confused, Angry is chosen by guest id rather than thing id (WALK-010), event 6
(a debug-only ring) and the dismissal sound; the entrance gate's variable 1 at the booth (`0x51A290`), AdmitPerson's refusal at a closed
ride (`0x4E0900`), whether shops carry the openable flag, and state 10's twelfth-update call to `0x4FD570`.
