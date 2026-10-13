# WANDER: the guest's decision state (6), its walk (7) and the staff patrol

Lane: WANDER (research and plan, 2026-10-13). This lane covers GUEST-010 ("OpenTPW has no separate state 6") and the handyman
positions behind ECON-022. Before this lane OpenTPW let a guest decide on a seconds timer (`DecisionIntervalSeconds`) and wander one
random neighbour cell at a time, and it tested the leaving rule for every walking guest on every park turn. The original does none of that:
a guest alternates between a decision state (6, "Doing nothing") and a walk to a chosen destination (7, "Walking around"). Staff
patrol with the same destination routine, restricted to a rectangle.

Source: the decrypted Windows v2.0 `tp.exe` (SHA-256 `5472f97b…1b805`), read with REA (Ghidra) and capstone, statically. The Mac
build has the same routines; the Mac addresses are not mapped here. Confidence as in QUEUE-plan:
- **High:** read from the instructions, field by field.
- **Medium:** a role inferred from callers, data or strings.

The x86 oracle executes both routines (ORACLE.md section 7.14): `tpw-exe-guest-state6` (X18) and `tpw-exe-wander-destination`
(X19) agree with this description on every input.

## 1. The guest state dispatcher

`0x5019f0` runs once per guest update and dispatches on the state word `+0x220` (KIDSTATES indices; the state setter is
`0x501db0`). For this lane:

| State | Handler | Behaviour |
| --- | --- | --- |
| 6 Doing nothing | `0x4fec90` | the decision tree of §2 (high) |
| 7 Walking around | inline | `0x4fa2a0` walks one step. It returns 0 on arrival, 1 while walking, 2 when stuck. On 0 or 2: if `rand & 3 == 0` and the wander destination `0x4f9490` (§3) succeeds, stay in 7, else state 6 (high) |
| 8 (activity) | inline | when the turn passes `+0x208 + 10` (unsigned, `0x501d26`), `0x4fc890` ends the activity and returns to the state saved at `+0x224` (high) |
| 9 Throwing away litter | `0x4fff20` | walks to a bin (not read in detail) |
| 10 Going to a ride | `0x4ffbc0` | the walk to the chosen ride (read for GUEST-011) |

`rand` is the world LCG `0x516330` (multiply by 1664525, add 1013904223, rotate right by 13, signed absolute value; ORACLE.md X1).

The two stamps are words the queue already uses:
- `+0x1fc` is the turn of the last entry into state 11 (the setter writes it there; the Mac `+508`, OpenTPW's `QueueStandingSinceTurn`).
  State 6 writes it after a failed decision or wander, and an excitement refusal in state 10 clears it (`0x4ffcf6`).
- `+0x208` is the interlude stamp (the Mac `+520`, OpenTPW's `InterludeTurn`). Every activity (`0x4fc800`) and every entry into state 8
  write the turn there.

The guest constructor zeroes both (`0x4faf75`, `0x4faf87`).

## 2. State 6: `0x4fec90` (high unless marked)

One draw `R = rand()` at entry; later tests use `R % 3`, `R % 100`, `R % 101` and `R % 5` of that same draw. T is the park turn
(`[0x80239c] + 0x1da70c`). Bytes are `_ftol` of the float, low byte. Each branch that changes the state returns, so the handler makes
at most one change per call. In order:

1. **Cheering.** If `T − +0x208 > 100` (unsigned) and the happiness byte is above 80: activity 5 (`0x4fc800(5)`). An activity
   stamps `+0x208 = T`, saves the state in `+0x224` and enters state 8 for 10 turns.
2. **Vomiting.** If the illness byte is exactly 100 and `R % 3 == 0`, the guest does these and returns:
   - activity 7;
   - litter type 7 on its cell (`0x4d93b0(7)`);
   - guest event 18;
   - a sound;
   - illness = 0.
3. **Litter.** If the guest's litter meter byte (`+0x1b4`) is at least 90 and its state is 6, 7 or 10:
   - The nearest object with flag `+0x32` bit 0x40 (a bin, medium) within radius 3 that a route reaches becomes the target, and the
     guest goes to state 9 and returns.
   - Otherwise it draws `rand()` again, drops litter of type `1 + R2 % 5` on its cell, sets the litter meter to 0 and goes on.
4. **Leaving gate.** If the happiness byte is 0, the exit counter `+0x1bc` is 0, or the park is closed (`+0x1da710`):
   - happiness drops by the big step (`0x4fea70(2)`);
   - the guest tries a route to the two gate cells, `(+0x7855cc, +0x7855d0)` and `(+0x7855d4, +0x7855d0)`, in an order by `R & 1`,
     twice;
   - the first route found sets state 18 (leaving) and returns;
   - if neither is found, it goes on.

   This is the rule of `GuestLifecycle.ShouldLeave` (ORACLE.md F26), tested only here.
5. **Facing a show.** The nearest unoccupied object with flag 0x80 within squared distance 5 (medium: features or entertainers) turns
   the guest towards it. The heading is written to `+0x1c`, and the handler returns. This is presentation only.
6. **Facing a performer.** If an object of kind 6 stands within radius 1 and the nearest guest-list object is in state 14, the guest
   turns towards it and returns (medium; presentation).
7. **Pranks.** With the happiness byte below 15 and the prank byte `+0x1c0` above `R % 101`:
   - prank byte 100, when `R % 100` is below the settings word `0x7850e0`: litter type 8 and a sound;
   - prank byte 101: litter type `R % 5 + 1`;
   - prank byte 102: it bursts the balloon of another guest on its cell.

   A prank records event 15 and adds 1.0 to happiness (GUEST-007).
8. **Decision, by `R % 3`:**
   - **0, the major choice,** only when `T > +0x1fc + 30` (unsigned). `0x4fcb10` (the choice loop, ORACLE.md X16) picks a ride:
     - If it finds one: event 2, state 10, and `+0x1fc` stays.
     - If not: event 1, activity 4 (a "can't decide" pause), the small happiness drop, and `+0x1fc = T`.
   - **1, the walk:** if `0x4f9490` (§3) sets a destination, state 7; otherwise `+0x1fc = T`.
   - **2:** nothing.

Settings words: `0x785058`/`5c`/`60` are the small/medium/big happiness changes (key order, medium). `0x7850e0` is not identified.
`0x70048c` is the constant 6 (litter types 1..5 plus one).

## 3. The wander destination `0x4f9490` (high)

It returns 1 when a route to a destination is set.
- A guest marked stranded (`+0x198`, a time stamp) that still fails `0x4fa770` plays animation 0x11 and returns 0.
- Staff (thing types 4..8) outside their patrol rectangle (`+0x20a` top-left, `+0x20c` bottom-right cell ids; `0x506ed0`) walk to a
  random cell inside it (`0x506f30`).

**On a path cell** (the cell has connections, `0x522810`), it takes `rand % 5 + 1` steps (a do-while; one more each time the last
step ends on the start cell). Each step:
1. The candidate neighbours are those the current cell is connected to, in the order +Y (link 16), +X (4), −Y (1), −X (64).
2. It removes the candidates that are not allowed:
   - for staff, a neighbour outside the patrol rectangle;
   - when standing on a path cell (type 1), any queue neighbour;
   - when standing on a queue cell (type 3), the direction of its own link;
   - any neighbour of cell type 10.
3. If fewer than two candidates remain, it takes the first one left in that order. If none remain, the guest is stranded (state
   unchanged, return 0); staff try the patrol rectangle instead.
4. Otherwise it starts at `rand & 3` and takes the first candidate that is not the way back (the reverse of the previous step).

The destination is the last cell, at sub-cell bytes `x, y` = `rand & 0x7f`, each clamped to 5..123 of the cell's 256: always in its
first quarter. `0x510100` (on the steering state `+0xd4`) asks the path-finder for a route there. The step directions come from the
table `0x7cdba0..`, which `0x4d9a65` fills at start-up: link 1 is (0, −1), 4 is (+1, 0), 16 is (0, +1), 64 is (−1, 0).

**Off the paths,** it searches the eight directions at distances 0..3 for a path cell that a route reaches. If none is found, it tries
five random cells within ±5 cells.

## 4. Staff (ECON-022, phase 3; medium)

The handyman's state machine is `0x4d73c0`:
- **State 0, idle:** waits until its per-grade idle time (`0x785330 + 16 × grade`) has passed since `+0x200`, then searches for a job.
- **State 1, patrol:** walks; on arrival or when stuck, it searches for a job.
- **The job search `0x4d7100`:**
  1. litter within `DetectionRange` (state 8 walk, state 9 sweep);
  2. otherwise the nearest toilet flagged by mRequestedService, or a dirty one within `DetectionRange` (state 10 walk, state 11
     clean; ORACLE.md X17);
  3. otherwise `0x4f9490`, as for guests but inside the patrol rectangle (state 1), or idle (state 0).

Each walk starts its job timer only on arrival.

## 5. Plan

- **Phase 2 (GUEST-010), done.** `GuestSimulation` runs the guest states 6 and 7 per park turn as above (`GuestDecision`,
  `GuestWander`; ORACLE.md X18, X19):
  - the seconds timer and the one-cell `Wander` go;
  - the leaving test moves into state 6;
  - the major choice follows the `R % 3` / `+0x1fc + 30` cadence;
  - activities become state 8 for 10 turns;
  - the destination follows §3 with OpenTPW's walk (WALK-I) for the route.

  OpenTPW keeps its own random stream (DET-014), and the presentation-only headings (steps 5 and 6) stay out. The litter meter
  (`+0x1b4`, GUEST-005) and bins are not modelled yet, so step 3 waits. Step 7 is phase 4. Oracle: the state-6 order as an x86
  target with stubbed callees, and the destination step rule (§3, steps 1–4) on synthetic cells.
- **Phase 3 (ECON-022).** Staff patrol with the same destination inside their rectangle. The handyman's job search runs from its
  position, and job timers start on arrival. Litter by cell needs ECON-020 first.
- **Phase 4 (GUEST-007).** Pranks, on top of state 6.
