## Rides and tours sweep: closed new rides, walk-off times, animated textures and the tour pose from tp.exe (October 16, 2026)

A sweep over the ride and tour approximations, read in tp.exe via REA:

- **New queue rides start closed** (RIDES-015 retired): the constructor sets the open word to 1 (`0x4DB3B9`), then for a ride with a
  queue logs "Object %d Closing", clears it and writes VAR_RIDECLOSED = 1 (`0x4DB712`–`0x4DB779`), so the player opens it.
- **Walk-off times** (TOUR-007 retired): `0x556F40` stores the WALKON slot and `0x5571A0` computes ftol(distance between nodes u3 and u4)
  × 100 ms, 100 ms for 0; the slot is kept per rider as an optional `WalkSlots` part. The two shipped tours stay at 1000 ms (their nodes
  are 10 apart).
- **The tour's angle** (TOUR-003 retired): TOUR 1 reads the instance angle at `+0x10` (`0x554385`), written by the constructor
  (`0x4DB0CD`) and loaded from the save's `mAngle` (`0x4DB7D0`), before any clip runs.
- **Animated textures** (RIDES-035 retired): the draw `0x580530` and the per-slot bind `0x57A420` bind the slot's current frame only while
  the ANIMATINGTEXTURES option (option 4, `0x44D9CD`) is on; the Low preset shows frame 0.
- **Narrowed:** the clip end's AnimID 14 (`0x4735D0`, RIDES-003), the object base height as (lowest + highest terrain vertex) / 2 over the
  typed footprint corners (`0x466D70`, `0x469930`, RIDES-014), the End vehicle's creation by `0x516B00` from the lab handler `0x503D40`
  (RIDES-019), the GETTIME clock (`0x551F1E`, RIDES-044), the tour pose on the engine's 4096-entry sine table (`0x4708D0`, TOUR-008),
  and RIDES-018 and RIDES-032.
- **Design choices, approved by the user:** RIDES-025 is closed (`[EXT:bonus-name-fallback]`), RIDES-023 is closed
  (`[EXT:ride-cycle-counter]`: `CompletedCycles` is OpenTPW's own diagnostic counter), TOUR-002 is closed
  (`[EXT:tour-thrown-riders-released]`: a deliberate fix of the original's bug where thrown-off riders are never released), and
  RIDES-021's ground-plane pick and RIDES-044's per-park clock origin are split out (`[EXT:build-cursor-ground-plane]`,
  `[EXT:park-clock-origin]`); what is still unread stays open.
- **New ID:** TOUR-011 registers WALKON's own walk-on time and the WALKOFF action-2 effect.

The pinned run hash is unchanged. Layout saves gain an optional `BaseHeightRule` (older saves keep the old cell-mean height) and guest
snapshots an optional `WalkSlots`. Verified with 4586 tests passing without game data, 5038 with it, and all five native smokes.

Still open: the footprint cell model (`@`, `Q`, `#`, `+`, track ends) behind RIDES-011, RIDES-031 and TOUR-005, the walk-on time of
WALKON (`ftol(dist(u1, u2)) × 100`) and the WALKOFF beam effect (TOUR-011), the ground flatten (`0x467030`), who sets the instance stop and hold
flags, and the build cursor's mouse-to-cell pick.
