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

## 4. Staff (ECON-022, phase 3; high for the handyman, read in full)

The handyman's state machine is `0x4d73c0`:
- **State 0, idle:** waits until its per-grade idle time (`0x785330 + 16 × grade`) has passed since `+0x200`, then searches for a job.
- **State 1, patrol:** walks; on arrival or when stuck, it searches for a job.
- **The job search `0x4d7100`:**
  1. litter within `DetectionRange` (state 8 walk, state 9 sweep);
  2. otherwise the nearest toilet flagged by mRequestedService, or a dirty one within `DetectionRange` (state 10 walk, state 11
     clean; ORACLE.md X17);
  3. otherwise `0x4f9490`, as for guests but inside the patrol rectangle (state 1), or idle (state 0).

Each walk starts its job timer only on arrival.

Read in full for the handyman (2026-10-14):
- **The update.** Once per park turn: the pre-update `0x4d7060` (the speed step `0x4fa870`, shared with the guests; an idle handyman scans
  for litter every IdleDuration turns and searches when it finds some), then the state handler `0x4d73c0`. States 0 and 1 draw the world
  generator every update and play the idle (161) or walking (160) voice at `draw & 15 == 0`.
- **The setter `0x4d7330`.** 8 and 10 set the speed bonus `+0xc2` to 25; 9 and 11 stamp `+0x214`; 0 stamps `+0x200` with the turn after a
  patrol, else 0 (search at once), and clears the bonus. The base speed `+0xc0` follows the energy byte in the job search: 60, 80, 100,
  120 or 140 (`0x75c7f8`).
- **The litter.** A find reseeds the generator with the cell id and walks to the point `13 × clamp(r & 0x7f, 1, 9)` on each axis, claims
  the cell and sets state 8. State 8 checks the claim, walks, and on arrival with litter still there stands on the point (`0x4fa930`) and
  sweeps (state 9) until the turn passes the stamp + WorkDuration; the sweep removes the litter of the cell he stands on. A failed walk
  clears his target but leaves the claim, which only his own search takes again.
- **The toilet.** State 10 walks; stuck sets state 0; on arrival he must stand on the toilet's access cell and hold its claim, else
  state 0. State 11 cleans until the turn passes the stamp + WorkDuration.
- **The rest.** A fresh handyman runs the job search in his constructor.

Read in full for the entertainer and the guard (2026-10-14). Every staff thing is updated in list order by `0x50b360`: the type's pre-update,
then its state handler. States 2, 3 and 4–13 are the generic staff handlers (`0x505fe0` walk to rest, `0x5061d0` resting, `0x5056e0`).
The per-grade tables follow `PerGradeStaffConsts` (`0x785330`, 16 bytes a grade), each followed by four spare bytes:
`EntertainerConstsPerGrade` at `0x785398` (WorkDuration, HappinessEffectOnCell, ActivationDistance, Pounds; 16 bytes),
`HandymanConstsPerGrade` at `0x7853ec` (12), `MechanicConstsPerGrade` at `0x78542c` (8), `ResearcherConstsPerGrade` at `0x785458` (12),
`GuardConstsPerGrade` at `0x785498` (WorkDuration, Pounds; 8). The speed bonus words at `0x75c7f0` are 0, 25 and 50.
- **The entertainer.** The pre-update `0x4d4660` moves his `RegionFX[0]` (radius 3, happiness 2) from his last cell `+0xcc` to his cell
  when it changed (`0x4d8460`, `0x4d8440`; the constructor puts it down, the destructor takes it up), then the speed step. The handler
  `0x4d4810`: 0 idles (voice 165) until the turn passes IdleDuration + `+0x200`; 1 patrols (voice 164, the walk drain); 14 performs (the
  work drain) until the turn passes `+0x214` + WorkDuration, then plays cat_staff 135 at him. Each end runs the search `0x4d46d0`: the strike
  and rest gate; unless too tired, a draw divisible by 3 and a guest (any thing of type 1, `0x4c8d30`) within the square of
  ActivationDistance start a performance (a draw picks its animation; state 14, `+0x214` = the turn); otherwise on a turn divisible by
  4 a draw and idle, else a wander and, when one is found, a draw and patrol. HappinessEffectOnCell is not read here.
- **The guard.** The pre-update `0x4d6360` moves his `RegionFX[3]` (radius 3, security 10) the same way. The handler `0x4d6410`: 0 idles
  (voice 167), 1 patrols (voice 166); both then run the strike and rest gate and, on a turn whose low two bits are not both 0, the wander
  (state 1), else state 0. A prank's bus message reaches the staff HQ (thing type 9, always at cell (0, 0) in the shipped and played saves),
  whose handler `0x508a30` reads the prankster's cell's security word (`0x4d8410`, word +6): below 1 nothing happens. Otherwise
  `0x5099c0` takes the guard in state 0 or 1 nearest **the HQ** (squared distance, the first on a tie) and `0x4d6260` starts him: too
  tired, nothing; else cat_staff 136, his target `+0x212`, a route to the prankster's cell and state 16 (bonus 25, the chase timer `+0x214`
  = GuardConstsPerGrade.WorkDuration). The chase `0x4d6790` drains as work, counts the timer down (0 abandons), re-routes when he arrives
  where the prankster no longer is, abandons when held, and on its cell catches it if its state is 6–10 (`0x4fec10`): bonus 0,
  cat_staff 137, the guest held (`0x4feb10`: balloon and model gone, state 17) and a route to the ticket booth (`0x4d8610`, lane by the
  turn's bit 0) for state 18; without one he sets it down and abandons (bonus 0, state 0, target cleared). The escort `0x4d6a50` sets it
  down on arrival (`0x4feb50`: cat_kids 128, a draw for the gate, a route out and state 18, else the guest is destroyed) and routes to
  the park entrance (`0x4d8690`) for state 19, which walks there and patrols. A held guest (state 17, `0x501c94`) waits while a guard in
  state 18 has it as his target and is destroyed otherwise (`0x50b780`). Guards never spot pranksters themselves.

Read in full for the researcher and the shared states (2026-10-14):
- **The researcher.** The pre-update `0x502960` adds `ResearcherConstsPerGrade.ResearchAbility` points every 20th turn unless the display
  state is Resting, On strike or Picked up (where he stands plays no part). The handler `0x5029f0`: 0 idles (voice 169) until the turn
  passes IdleDuration + `+0x200`; 1 patrols (voice 168, the walk drain); both then run the search `0x502c70`: the gate; a draw whose low two
  bits are not both 0 and a wander patrol; else, unless too tired, state 15 with `+0x214` = the turn. State 15 drains as work and, before
  the turn passes `+0x214` + WorkDuration (`0x785458`), plays cat_staff 138 on a draw's low four bits 0; after it a wander patrols, or none
  restamps.
- **The gate `0x506a40`** (every search opens with it). While the role's strike flag is set and the park gates' `VAR_STATUS` is 1
  (`0x51a290` reads `mParkGates` at game `+0x1da732`), four draws pick a point of the strike area (`FixedItemInfo.StrikeArea*`,
  `0x7855ec`..`0x7855f8`) and a route there sets state 4 (bonus 25); without a route it goes on. Above RestLevel the energy byte
  (`+0x1fc`) sets the base speed, and a happiness byte (`+0x1f8`) above 97 costs a draw (a message). At or below it, the rest finder
  `0x506910` takes the nearest object with flag `+0x32` bit 1 (`UsageInfo.ChillsYouOut`, the Staff Room; squared distance, the first
  on a tie), a route to its access cell stores it (`+0x208`) and sets state 2; else a draw and, on its low four bits 0, happiness −=
  HappyHitCosNoRestArea.
- **State 2** `0x505fe0` walks; on arrival the rest area's script variable 0 (`VAR_STAFFIN` in every theme) + 1 and state 3 (the
  member's model goes); held, the happiness hit and state 0. **State 3** `0x5061d0` adds RecuperationRate to energy and
  HappinessRecuperationRate to happiness; at an energy byte of 100 `0x506d10` takes the variable back down, clears `+0x208`, sets state 0
  and runs the type's search. **State 4** `0x5056e0` walks (message 0x15) and on arrival or held sets state 5 (bonus 0). **State 5**
  `0x506300` turns the heading on one draw in eight, ends the role's strike when the park is closed and empty, and once the flag is clear
  routes to entrance A (`0x7855bc`): state 1, else 0.

Read in full for the mechanic (2026-10-14). The handler `0x4da490`: 0 idles (voice 163) until the turn passes IdleDuration + `+0x200`;
1 patrols (voice 162, the walk drain); both end in the search `0x4da5b0`: the gate, too tired ("Too tired to fix rides"), then the ride finder
`0x4daa90`. It walks the object list once from a cursor (on the manager at game `+0x1da71a`) that moves one object on per search, and takes
rides broken down (state 1, `0x4e0370`), upgrading (state 2, `0x4e03e0`) or with `mRequestedService` (`+0x64`, `0x4e03d0`) that are no toilet,
whose claim (`0x4e0220`: `+0x5e`, lapsing after 100 turns when the claimant is no longer on it) is free or his, by the smallest squared
distance (strictly smaller replaces). A route to its access cell claims it (`0x4e01f0`: `+0x5e`, `+0x60` = the turn) and sets state 12 (no
speed bonus; the advisor's "on the way" message for a ride neither requested nor upgrading); otherwise a wander or idle. State 12
`0x4da740` checks the claim, walks, and on arrival at a ride that still needs him sets state 13 with the timer `+0x214` (`0x4da370`: an
upgrade's DurationOfUpgrade × WorkDuration, else (100 − the repair byte) × WorkDuration / 100; MechanicConstsPerGrade at `0x78542c`); held,
it clears its ride and searches. State 13 `0x4da830` drains as work, counts the timer down, waits for a broken ride's fix animation, then
`0x4df8f0` repairs fully (gauge 100, the claim and the request cleared) or installs the upgrade, and searches.

The player's controls (2026-10-14). The ride panel's call button (`0x48ce50`; art `b_callmech`, `b_callhandy`) runs `0x4dfe30`: a call sets
`mRequestedService` (`+0x64`) and closes the ride (`mCanLoad` 0, script variable 6 = 1); the mechanics' finder takes a requested ride that is no
toilet, the handymen's toilet search a requested toilet at any distance (closer than the best so far); the repair (`0x4df8f0`) or the clean
(`0x4dfd80`) clears it, and a cancel reopens the ride when the reopen guard allows. The employee panel's patrol buttons (`0x4b5b50`) clear the
rectangle (`0x506e90`: cells (0, 0) and (127, 127), the constructor's default) or start mouse mode 0x36: the first map click stores a corner,
the second orders the two and sets `+0x20a`/`+0x20c` (`0x506e50`, from the click handler `0x524960`). The wander keeps a member inside it: from
outside, up to 30 random cells of the rectangle (two draws each) that a route reaches (`0x506f30`), else the wander without it ("I can't
reach my patrol area"); inside, the steps that leave it are removed, and with none left `0x506f30` runs again.

## 5. Plan

- **Phase 2 (GUEST-010), done.** `GuestSimulation` runs the guest states 6 and 7 per park turn as above (`GuestDecision`,
  `GuestWander`; ORACLE.md X18, X19):
  - the seconds timer and the one-cell `Wander` go;
  - the leaving test moves into state 6;
  - the major choice follows the `R % 3` / `+0x1fc + 30` cadence;
  - activities become state 8 for 10 turns;
  - the destination follows §3 with OpenTPW's walk (WALK-I) for the route.

  OpenTPW keeps its own random stream (DET-014), and the presentation-only headings (steps 5 and 6) stay out. Step 7 is phase 4.
  Oracle: the state-6 order as an x86 target with stubbed callees, and the destination step rule (§3, steps 1–4) on synthetic cells.
- **Phase 3a (ECON-020, GUEST-005), done.** Litter by cell (§6): the guest's litter meter, step 3 of state 6, the bin search and
  state 9, vomit on the cell, the handymen's sweep by cell (still without positions) and their litter grievance (ORACLE.md X20, X21).
- **Phase 3b (ECON-022), done for the handymen.** In a level the handymen run their state machine (§4, `StaffWorldSimulation`): they
  patrol with the guests' destination routine inside their rectangle, search for litter and dirty toilets from their position, and time
  a sweep or a clean from their arrival (ORACLE.md X23). Their rest and strike stay the economy's.
- **Phase 5 (ECON-022, GUEST-006, GUEST-007), done for entertainers and guards.** Entertainers patrol and perform for nearby guests
  and carry their RegionFX row; guards carry their security row and answer a prank with the chase, the catch and the escort (§4).
- **Phase 6 (ECON-022, ECON-014), done for the researchers and the shared states.** Researchers wander and research where they stand;
  every world-driven role walks to the Staff Room to rest and out through the gate to the strike area (§4).
- **Phase 7 (ECON-021, ECON-022), done.** The mechanics run their state machine in a level: from where they stand they take the nearest
  broken or upgrading ride, walk to it and repair or upgrade it from their arrival (§4). Every staff role now walks.
- **Phase 8 (ECON-049, ECON-022), done.** The player calls a mechanic or a handyman from the ride panel and draws an employee's patrol
  area with two clicks; the wander keeps staff inside it (§4).
- **Phase 4 (GUEST-007, GUEST-006), done.** Pranks on top of state 6 (§7), with the stinkbomb's and the vomit's RegionFX on the cell
  environment grid that the needs update reads (ORACLE.md X18, X22).

## 6. Litter (high unless marked)

A map cell record (`[0x8023a0]`, 0x44 bytes) holds at most one item: `+0x2c` the type (`mLitter`, 0 none), `+0x28` its sprite,
`+0x30` the handyman who claimed it (`mLitterCollector`), `+0x34` the claim's turn, `+0x38` a stinkbomb's particle.

- **Drop, "Littering cell" `0x4d93b0`.** Nothing in game type 1 (online) or on a cell that already has litter; the guest's meter
  still empties, so that litter is lost. Otherwise it creates a sprite of bank 11 (frame type − 1), reseeds the world generator
  with the cell id `y·128 + x + 1` and draws twice for the sprite's offset (`x·10 + r1 % 9 + 1`, likewise z), clears the claim and
  stores the type. Vomit (7) and stinkbomb (8) also add RegionFX 2 and 5 to the cell environment grid (`0x4d8440`), and a stinkbomb
  starts particle 0x32. Types 1..5 are ordinary litter, 6 the sprite fallback.
- **Removal, `0x4d9620`.** Only a handyman's sweep removes litter (the RegionFX of 7 and 8 go with it). Nothing decays, spreads or
  washes away; path removal leaves it.
- **The guest.** Every use adds the type's `LitterEffect` (`+0x154`) to the litter meter `+0x1b4` (`0x4fe1e0`, clamped 0..100). In
  state 6 a meter byte of 90 or more runs the bin search `0x500dc0` (objects with `+0x32` bit 0x40, `UsageInfo.HoldsLitter`;
  squared cell distance below 9 and below the best so far; a route to the object's access cell `+0x36`). A bin sets `+0x1dc` and
  state 9; without one a second draw drops `1 + R2 % 5` on the cell and the meter empties.
- **State 9, `0x4fff20`.** The walk step: arrived with a bin plays the bin's sound; stuck, or no bin id, drops `1 + rand % 5` on the
  cell. Then the meter is 0 and the state 6. `+0x1dc` is left as it is.
- **The handymen.** The finder `0x4c8ed0` scans ±DetectionRange cells, x outer and y inner, for litter claimed by nobody or by
  himself and takes the nearest by Manhattan distance; it reseeds the generator with the cell id for his sub-cell spot, claims the
  cell (`0x4d9600`) and walks (state 8). On arrival he sweeps (state 9) until the turn passes the stamp + WorkDuration[grade]. A
  route that fails leaves the claim in place (medium).
- **What litter does.** Ordinary litter changes no meter. The analyser (32 × 32 blocks of 4 × 4 cells) counts littered cells
  (`0x4c8ba0`) and path, queue and type-9 cells (`0x4c8ae0`): the handymen strike when their ratio is above 0.2 (`0x509360`), the
  thought "too messy" (THOUGHTS 7) fires when the guest's own block is above 0.2 (`0x4fc8a0`, GUEST-009), and an advisor scorer reads
  the park's litter percentage (`0x4ccdf0`). Vomit and stinkbombs act through the environment grid (phase 4).

## 7. Pranks (high unless marked)

- **The flag.** The guest constructor's eighth draw (`0x4fb114`) makes a prankster: `+0x1c0` = 100 when the draw % 100 is below
  `PeepInfo.PrankeryLikelihood` (settings word `0x7850dc`; 5 in the shipped `Standard.sam`), plus the thing id % 3; otherwise 0. So a
  prankster is 100 (stinkbombs), 101 (litter) or 102 (balloons) for life. Nothing else writes the byte but the save code (`mPrankeryIndex`).
- **The step, `0x4ff0f5`** (state 6, after the presentation turns, before the final choice): with the happiness byte below 15 (hard-coded;
  the file's `PrankHappinessThreshold` and `PrankLikelihood` have no key in tp.exe) and the flag above `R % 101`:
  - 100: when `R % 100` is below `PeepInfo.StinkbombLikelihood` (`0x7850e0`, 25) a stinkbomb (litter 8) on the cell and sound 0xcf;
  - 101: litter `R % 5 + 1` on the cell;
  - 102: the first other guest on the cell's thing list (game `+0x2b8 + 0x44 × cell id`, next at thing `+0xa`) holding a balloon
    (`+0x210`, `mBalloonScript`) has it burst (`0x4fe950`, presentation only).
  A prank (a litter prank even when its cell already holds litter) records guest event 15, sends message type 14 on the bus `0x788d3c`
  (`{vtbl 0x7007a8, 0, prankster id, id % 3}`) and adds 1.0 to happiness, clamped. The step never returns early.
- **Who hears it.** The advisor (thing type 11, `0x59b060`) queues message 0x7d, 0x7e or 0x7f (PrankSbomb, PrankLitter, PrankBalloon).
  The staff pool (`0x508a30`) reads the security word of the prankster's cell: at 0 or below it "got away with it"; otherwise the nearest
  free guard (state 0 or 1, squared distance) chases the kid (state 16), catches it on its cell (stats `mMisbehavingKids`), takes its
  balloon and escorts it to a gate cell (states 17 and 18) (medium for the distance origin).
- **The environment grid.** Vomit (RegionFX 2) and stinkbombs (RegionFX 5) add their effect while they lie (`0x4d8440`) and remove it
  with the litter (`0x4d9620`). The writer `0x4d8480` (the Mac `0xd73a0`) adds `sign × coefficient / (|dx| + |dy| + 1)`, truncated,
  to each word of every cell in the square radius, clipped to the map, without saturation. The guest's needs phase adds its cell's
  happiness, illness and hunger words (`NeedsUpdate`, oracle F25). Shipped rows: vomit radius 1 (−1, +1, −1), stinkbomb radius 3
  (−3, +3, −1).
