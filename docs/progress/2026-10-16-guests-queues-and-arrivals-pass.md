## Guests, queues and arrivals: the minor ride decision, queue auto-open and the booth wait from tp.exe (October 16, 2026)

A pass over the guest, queue and arrival approximations, read in tp.exe via REA:

- **The minor ride decision** (GUEST-018 retired, GUEST-019 new): a guest walking to a ride counts its updates (`+0x2C`) and every twelfth
  update (`0x4FFEF2`) runs the minor decision `0x4FD570`: rides on the 4 × 4 cells round the guest that lie nearer to the target's back
  cell and pass the counts test are scored, and the best one replaces the target when its route (`0x4D8B40`) is shorter. No random draw.
- **Queue auto-open** (QUEUE-023 narrowed): every queue edit re-evaluates the ride as `0x4DE1F0` does, opening a closed ride with no
  breakdown, upgrade or maintenance call whose queue connects (`0x4DE4A0`), a coaster that passes `0x441970` and a kart or water ride
  with a valid track. With new queue rides starting closed, connecting their queue now opens them.
- **No queue cell cap** (QUEUE-013 retired): the queue tool compares no cell count; the bound is the back-of-queue walk's 1000 cells
  (`0x4DE130`).
- **The booth wait** (ARRIVAL-006 narrowed) is counted in executed park turns, N + 1 in all.
- **Bumper families** (QUEUE-004 retired): every shipped bumper, kart and water ride has a controller template of its own family
  (`0x529E10` → `0x545890`, loader `0x543560`).
- **Narrowed:** GUEST-006, GUEST-012, GUEST-013, GUEST-014 (with GUEST-020 split out for the flagged folders' bank limits) and GUEST-015;
  PATH-013 is retired into PATH-017 (the P window's input layer); TRACK-032 is new (the saved-ride controller handle).
- **Design choices, approved by the user:** QUEUE-020 (`[EXT:bounce-single-clock-read]`), QUEUE-022 (`[EXT:synthetic-ride-zero-guard]`)
  and ARRIVAL-002 (`[EXT:online-visit-settings]`) are closed; GUEST-006 (`[EXT:online-bubbles-on]`), GUEST-017
  (`[EXT:empty-costume-heads-keep-kid-head]`, avoiding the original's division by zero) and PATH-007 (`[EXT:no-land-maps]`) are split.

The pinned run hash moves with the minor decision and the booth wait (docs/DETERMINISM.md). Guests gain the optional saved fields
`RideWalkCount` and `BoothWait`; older saves load.

Still open: the original also re-evaluates a ride's queue after placement, path edits and object removal; the minor decision's placement
by the cell the ride's thing stands on (GUEST-019); and the booth's turn-dropping loop and closed-park countdown.
