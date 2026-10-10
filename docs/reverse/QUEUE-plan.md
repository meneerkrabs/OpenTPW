# QUEUE-R: queue evidence and the QUEUE-I implementation plan

Lane: QUEUE-R (research). Input: the M3 gate (`f6ad99f`, `docs/M3-GATE.md`) fails
`build.queue` and leaves `queues.no-stuck-queue` unresolved. This document
consolidates what was already proved, adds new bounded traces of the Feral Mac
`SimThemePark.data` (SHA-256 `04809cd4…e295f5`), and specifies QUEUE-I.

Reproduce (private assets stay outside Git):

```sh
python3 -I tools/ppc-analysis/lanes/queue/queue_evidence.py /path/to/mac-feral/bin --pc-data /path/to/Data
python3 -I tools/ppc-analysis/run_evidence_checks.py --mac-bin /path/to/mac-feral/bin --pc-data /path/to/Data
```

Confidence uses the PPC-rides convention. **High** means an identity-pinned
instruction or relocation path whose operations were interpreted. **Medium**
means a role inferred from callers, strings or data. **Unresolved** means the
path was not recovered. "Bounded" marks a claim that holds only for the
enumerated sites. All results are static. Nothing original was executed.

## 1. What was already proved (consolidated)

| Source | Claim | Status after this lane |
| --- | --- | --- |
| PPC-guests, queue states | Jump table data `0x41314`. State 10 `0xeccb0` (heading to the ride / joining), 11 `0xed244` (standing in the queue), 12 `0xe6454` (walking to a queue position, returns to 11) | Kept. Joining, standing, admission and leave paths are now traced (sections 4 to 6) |
| PPC-guests | Queue destination `0xee604 → 0xdd144`, then `0xddc2c → 0xddcc4` and `0xe6864`. Four positions per queue cell, following a prior-cell link | Kept. The link byte, the cell type and the per-cell slot arithmetic are now identified (section 3) |
| PPC-guests | Queue acceptance `0xdcb74` reads `+436` (`QueueWaitTimeConstant`) and is "not a universal `4*capacity`" | **Completed**: full formula, HasQueue override 100, physical room `4 × cells` (section 4) |
| PPC-guests | Timeout compare `+508 + 100` at `0xed670`, then a boredom leave | **Refined**: the branch cannot be reached in game (section 5.3). It is not a usable bound |
| PPC-guests, score | Queue match `100 − q·100 / (4·max(f60,1))` with object `+60` | `+60` is now identified as the **queue size in cells** (`mQueueSizeInCells`). The score's denominator is the physical queue room |
| PPC-rides RIDES-016 | `VAR_DURATION` written raw from `InitDuration`. Unit unknown | **Partially resolved**: the native code writes the raw value too, clamped to `[MinDuration, MaxDuration]`. `BOUNCE` treats it as seconds of the scheduler clock (section 7) |
| APPROX-TRACE RIDES-016 step 1 | Hypothesis: upgrade record `+0x1b4` (436) is `InitDuration` | **Refuted**: 436 is `QueueWaitTimeConstant`. `InitDuration` is 416 (section 2) |
| PPC-ui UI-031 | The manual says the queue tool follows ride placement | Unchanged. The native build tool is only partly traced (section 3.3) |
| PPC-rides | COAST admission rings `0x3dbc0`, `0x3dd14` | Separate controller layer. Not used by the BOUNCE bound |

Current OpenTPW runtime (`RideVisitorBridge`, `GuestSimulation`) uses a
*virtual* list per attraction. Its maximum is `Capacity × QueueLengthPerCapacity`
(4), an approximation. Slots sit at the entrance cell, three abreast
(`QueueSlotSpacing` 0.28). The front guest is offered as soon as
`VAR_LETMEON == 0`. There is no queue-path tool and no queue cell in
`GuestPathGrid`.

## 2. SAM schema binding (high)

The ride descriptor table at data `0x39b64` is interpreted by the economy
lane's `schema.py` with `embedded_offset=4`. It gives these runtime offsets on
the object type record (`0x118018(type)`):

| Field | Offset | Independent anchor |
| --- | --- | --- |
| `Info.HasQueue` | 64 | build `0xdac0c` → ride flag `0x8` |
| `Info.RunsContinuously` | 72 | build `0xdaca4` → ride flag `0x100` |
| `UsageInfo.ProvidesRelief` | 244 | build `0xdab6c` → ride flag `0x1` |
| `UsageInfo.Min/MaxCapacity` | 292 / 296 | CAPACITY clamp `0xdc75c/0xdc760` (diagnostic `CAPACITY = %d`) |
| `UsageInfo.Min/MaxDuration` | 300 / 304 | DUR clamp `0xdc658/0xdc698` (diagnostic `DUR = %d`) |
| `Upgrades[L].InitCapacity` | 408 + 64·L | build `0xdad60` |
| `Upgrades[L].InitDuration` | 416 + 64·L | build `0xdad78` |
| `Upgrades[L].InitSpeed` | 424 + 64·L | build `0xdad4c`. Queue-limit divisor `0xdcbf4` |
| `Upgrades[L].QueueWaitTimeConstant` | 436 + 64·L (float, kind 7) | queue limit `0xdcc58`, diagnostic "No queue constant entered in SAM file" |

The upgrade stride of 64 bytes matches `slwi 6` of the level byte `+76` at
`0xdcbe4`. The economy lane already pins WearRate 432 and RedLine 412/428 from
the same table.

## 3. Queue geometry and data model

### 3.1 Ride (object) fields (high unless marked)

| Offset | Meaning | Evidence |
| --- | --- | --- |
| `+46` u16 | flags: `0x1` ProvidesRelief, `0x8` HasQueue, `0x100` RunsContinuously (others not needed here) | build `0xdab6c..0xdacb8` |
| `+50` u16 | entrance cell id (`1 + X + (Y << 7)`) | `0xdd2e0` |
| `+54` u16 | cached back-of-queue cell (`mBackOfQueue`, medium name) | `0xdd46c`, `0xdd4bc` |
| `+56` u16 | head guest id of the queue list | `0xdd16c`, append `0xdcda0` |
| `+60` u32 | queue size in cells (`mQueueSizeInCells`, medium name) | `0xdd4c0..0xdd4e4`, `0xdc9a4` |
| `+76` u8 | upgrade level | `0xdcbd8` |
| `+84` u32 | SPEED | setter `0xdc604` (`SPEED = %d`) |
| `+88` u8 | DUR (operating duration), script variable 3 | setter `0xdc6e8`, `0xdc6cc` |
| `+89` u8 | CAPACITY, script variable 2 | setter `0xdc810`, `0xdc7f4` |
| `+104` u16 | pending visitor ("person being loaded") | `0xe1564`, test `0xe0628` |

### 3.2 Guest fields (high for the offsets; names are analyst labels)

| Offset | Meaning | Evidence |
| --- | --- | --- |
| `+476` u16 | target object id | queue handlers |
| `+497` u8 | recorded queue position (0 = front) | `0xee688` (= list position) |
| `+500` u32 | move delay in park turns | state-11 entry `0xef8ac`, decrement `0xed4e4` |
| `+504` u32 | called-forward flag | `0xee7e8` = 1, cleared `0xed2dc` |
| `+508` u32 | park turn at the last state-11 entry | `0xef884` |
| `+520` u32 | park turn at the last idle interlude (state 8) | `0xe8c0c`, `0xefd30` |
| `+524` | happiness snapshot taken at join | `0xece58` |
| `+544` / `+548` | state / state saved across the interlude | `0xef728`, `0xe8c14` |
| `+552` / `+554` u16 | next / previous guest id in the queue list | `0xdd1c8`, `0xdce80` |

The queue is a **doubly linked list of guest ids**. Head is ride `+56`, links
are guest `+552/+554`, and the terminator is id 0. Append (`0xdcd34`) walks to
the tail. Position is the list index (`0xdd144`), and count is the position of
the terminator (`0xdd118`). The geometry below only places guests. It does not
order them.

### 3.3 Queue cells (high for the walk; bounded for building)

- A map cell is a 68-byte record (`0xdd2f4`). In it, `+8` is the cell type,
  `+12` the connection bits (`0x6e0e4`), and `+13` the **queue link
  direction** (read `0x6e228`, write `0x6e1a8`, clear `0x6e1b0`). Link values
  are `1, 4, 16, 64`.
- **Front cell** (`0xdd2b4`): the neighbour of the entrance cell (`+50`) in the
  direction of the entrance's connection bit.
- **Next cell** (`0xdda18`): a neighbour that passes `0x851ac` (type 3 or 9) and
  fails `0x851d0` (type 9), so **type 3 = queue cell**. Its link byte must point
  back toward the current cell (neighbour offsets paired with links 16, 1, 64
  and 4). The neighbour offset tables are filled at run time. Which compass
  direction each value denotes is not established.
- **Back of queue / size** (`0xdd43c`): walk from the front through
  next-cell links. Each step stores the cell in `+54` and adds one to `+60`. An
  iteration guard of 1000 prints "GetBackOfQueue() crashed!". The front cell
  counts as one, so `+60 ≥ 1` whenever a front cell exists.
- **Queue edited** (`0xdd57c`, called from the map/path build code family
  `0x70b98..0x8c7c0`): clear `+54`, recompute back and size, log "queue is
  now %d cells long". Every queued guest except the pending one re-evaluates
  (`0xee8f8`, which has "queue was shortened" and "queue edited underneath me"
  exits). Then it checks whether the back of the queue is connected
  (`0xdd744`).
- **Build tool** (bounded): 46 sites in `0x70b98..0x8c7c0` write the link byte.
  One traced site (`0x71768..0x717c4`) maps the object rotation to an
  entrance-side link: 0 → 4, 90 → 1, 180 → 64, 270 → 16. Placement validity,
  cost and the queue tool's interaction (UI-031) are **unresolved**. No queue
  length limit was found in the traced code other than the 1000-iteration
  guard.

### 3.4 Positions inside the queue (high)

`0xddcc4` (HasQueue objects, flag `0x8`): `remaining = position`, starting at
the front cell. While `remaining ≥ 4` and the cell is not the terminator,
subtract 4 and follow the next-cell link. The depth byte along the cell is
`trunc(255 × (0.25 × remaining))`, computed in single precision, giving 0, 63,
127 and 191. The lateral byte is `rand mod 28 + 114` (114..141) from the shared
RNG `0x105328`. Depth > 128 (the fourth slot) takes a branch toward the next
cell (`0xdde74`) that was not traced further. The link direction then selects
which sub-cell axis gets depth or `255 − depth`.

Objects without HasQueue use a virtual queue (`0xde02c`). An assertion
("Virtual queue problem!") requires position < 4.

## 4. Joining and capacity (state 10 at the back cell, `0xeccb0`) (high)

These checks run when the guest stands on the back-of-queue cell (`0xdd3f4`):

1. **Physical room** `0xdc988`: the guest may join only while
   `count < 4 × (+60)`. Otherwise thought 21 and the guest wanders (state 6).
2. **Excitement gate**: if `0xe02ec(ride,1)` is set and
   `|0xe9a84(guest,ride)| ≥ 45`, the guest leaves with "ride is not exciting
   enough!" or "ride is too exciting!".
3. **Limit** `0xdcb74`: the queue is full when `count ≥ limit`. Then "queue is
   too long!", thoughts 21/16, state 6. The limit is:
   - HasQueue (`+46 & 0x8`): **100**.
   - Otherwise: `trunc(max(QWTC × (G × CAP) / DUR, 4.0))`, single precision,
     where `G = SPEED / Upgrades[L].InitSpeed` (1.0 when SPEED is 0) and QWTC
     is `Upgrades[L].QueueWaitTimeConstant`. A NaN quotient becomes 4.
4. Join: `+524` = happiness, append to the list, then `0xee604`. It stores the
   position in `+497`, computes the slot and sets the destination, entering
   state 12. If the destination fails: "Couldn't get to my place in the queue,
   leaving!", the guest is removed, state 6.

So the maximum queue length is **`min(100, 4 × cells)` for HasQueue rides**
and `min(4 × cells, L)` otherwise. With one front cell and `L ≥ 4` that is 4,
which matches the virtual-queue assertion. For HasQueue rides the
`QueueWaitTimeConstant` formula is never used. QWTC only shapes queues of
objects without a queue path (shops, toilets, sideshows), and the floor of 4
plus the one-cell room make even that 4 in practice (bounded: no-HasQueue
object with more than one cell not traced).

## 5. Standing in the queue (state 11, `0xed244`) (high)

### 5.1 Entry (`0xef868`)

`+508 = turn`, `+500 = trunc(1.2f × +497)` (float `data:0x55a0`), animation 3.

### 5.2 One call per update, in order

1. **Called forward**: if `+497 == 0`, `+504 ≠ 0` and the ride's pending
   visitor is this guest (`0xe0620`), then `+504 = 0`, the destination is set to
   the entry stand point (`0xde1d8`), and the guest enters **state 13** ("being
   loaded"). If the path is lost, the guest is removed and leaves.
2. Diagnostic only ("admitted … not first in queue").
3. `0xdfe34(ride)` set → thought 14, leave.
4. Position lookup fails → "Problem with a queue", leave.
5. **Moving up**: if the list position differs from `+497`:
   - When `+500 ≠ 0` and `0 ≤ +497 − pos ≤ 2`, decrement `+500` and stay.
   - Otherwise, unless `0xdfe0c(ride)` (ride state `+408 == 1`), call `0xee604`.
     That gives the new `+497`, state 12, and then state 11 again. Re-entry
     resets `+500` and `+508`. If it fails: "Couldn't get to my intended queue
     position", leave.

   A guest that becomes the front with a small gap waits at most
   `trunc(1.2 × 2) = 2` turns.
6. Position matches: if `+497 > limit` (`0xdc9d4`, same formula), thoughts 16/21
   and leave. Thought 13 and leave also follow in two cases:
   - track type (`Bumper.WhichTrackType`, type record `+156`) is 3 and
     `0x45eac(ride)` is zero;
   - track type is 1 and ride `+40` is zero.
7. **Needs window** `0xed58c`: let `w = turn − +520` (unsigned).
   - `w > 30`: happiness `+412 > 80` starts idle interlude 5. `< 10` gives
     thought 11 and leave. `10..19` starts idle interlude 4. If none applies,
     toilet `+428 > 80` gives thought 4, and the guest leaves **unless the
     object ProvidesRelief**.
   - `w ≤ 30`: the **boredom test**. If `turn > +508 + 100`, "I'm bored and
     leaving the queue", thought 7/12, leave.
8. A 1-in-10 random facing change (±400 mod 2048 on `+28`).

The idle interlude (`0xe8b74`) sets `+520 = turn`, saves the state in `+548`
and enters state 8. State 8 restores the saved state once `turn > +520 + 10`
(`0xef6c8`). Restoring re-enters state 11 and so resets `+508`.

### 5.3 The boredom exit is unreachable (high for the arithmetic; bounded for writers)

It needs `turn − +520 ≤ 30` **and** `turn − +508 > 100`. In state 11,
`+508 ≥ +520`, because every write of `+520` is followed by state 8 and then a
state-11 re-entry that writes `+508`. That makes the two conditions
contradictory (`turn − +520 ≥ turn − +508 > 100 > 30`). The writer enumeration
covers every D-form store with displacement 508/520:

- Guest constructor `0xe7644`.
- State-6 handler `0xebbd0`.
- State-10 handler `0xeccb0` (writes 0).
- State entry `0xef700`.
- Interlude `0xe8b74`.
- Copy/restore routines `0x191ca0` and `0x1be890`, which copy both fields.
- Staff-class code `0xf29fc..0xf39f8`, which is a different object class.

No `stmw` with a base other than r1 covers these offsets. There are no
indexed stores in `0xe4000..0xf0000`. The 0x6xxxx writers of `+520` belong
to another class (`+516/+519` neighbours).

Bound: a raw write outside these sites (save loading of inconsistent values,
memcpy) is not excluded. The witness test
`test_simulated_state_11_never_bores` models the turn-by-turn logic.

**Consequence:** in the original, a guest standing in a queue has **no
time-based exit**. It leaves only through:

- admission;
- happiness < 10 (checked only outside the 30-turn window);
- toilet > 80 (except at relief objects);
- the ride or object checks in steps 3, 4 and 6;
- queue edits, or the ride closing or the guest being dismissed (`0xee8f8`
  paths, not traced in detail).

OpenTPW must not invent a boredom timeout for queues.

## 6. Admission (ride update) (high, bounded cadence)

The object dispatcher `0xfa9b0` calls the ride update `0xe0a8c` (`0xfaa78`) for
each live object on each park turn (subject to the live-list eligibility noted
in PPC-guests). When ride `+408 == 0` it calls `0xe1864 → 0xe1404`:

- Requires ride `+100 ≠ 0`.
- Requires script variable 0 (LETMEON) `== 0` and ONRIDE (var 5) `<` CAPACITY
  (var 2). Objects with track type 3, or with type record `+156 == 2`, skip the
  CAPACITY test (`0xe149c..0xe14c8`).
- Requires RUNNING (var 9) `== 0` or RunsContinuously (`+46 & 0x100`).
- Requires no pending visitor and a non-empty list.
- Requires the head to be in state 11 with `+497 == 0` (`0xee76c`).

Then: head `+504 = 1` (`0xee794`), pending `+104 = head` ("LetMeOn - person
being loaded is %d").

The guest notices on its next state-11 call (5.2 step 1) and walks to the stand
point. State 13 (`0xed7e0`) runs the price check ("too expensive, I'm leaving
the queue") and the admission method `0xe03d0`. That method writes the guest
into script variable 0 only if it is 0. State 14 waits until the script
consumed it (`0xe05c8`), then the guest is removed from the list and enters
state 16. The track-type bypass is not needed for BOUNCE rides (Belly Bounce has
track type 0).

OpenTPW differences:

- OpenTPW offers `queue[0]` when LETMEON is 0, without the ONRIDE < CAPACITY
  and RUNNING gates.
- It removes the guest from the list at offer time, not after consumption.
- It skips the walk/state-13 step.

## 7. Time units (high where stated)

- **Park turn**: counter `game+0x1da70c`, incremented once per park turn
  (`0x1053a0`). Nominal 248 ms of the scaled scheduler clock (PPC-clock). Move
  delays, the 30/100/10-turn windows and the admission cadence are in park
  turns.
- **DUR**: written raw. `Upgrades[0].InitDuration` is clamped to
  `[UsageInfo.MinDuration, MaxDuration]` (`0xdad78 → 0xdc620`) and stored in
  `+88` and script variable 3. There is no conversion by `Info.DurationUnit` in
  this path.
- **BOUNCE** (opcode 72 → `0xadf40`): the duration operand × 1000 is added to
  scheduler milliseconds (`0x10e844`) as the slot deadline. So for scripts
  `BOUNCE VAR_LETMEON VAR_DURATION` (Belly Bounce), **DUR is seconds of the
  scheduler clock**.
- **UNBOUNCE** (`0xae020`): releases a slot once `deadline < now` and
  `(now − start) mod 1000 < 200`, that is, within 1 s after the deadline when
  polled.
- Other consumers convert differently (BUMP13 ×30, TOUR8 ×1000, PPC-rides).
  Seconds are proved only for BOUNCE.
- `QueueWaitTimeConstant` is dimensionally "DUR units × riders per CAP", since
  `L = QWTC × CAP / DUR`. That reads as a designed queue wait in DUR units. It
  only applies to objects without HasQueue.

## 8. QUEUE-I implementation spec

All items carry the confidence of the section that proves them.

1. **Data**
   - Cell type `Queue` (original type 3) in the path grid, with a link
     direction (one of four).
   - Per ride: entrance cell, front cell, cached back cell, size in cells, and
     the guest list (head/next/prev) with a pending visitor.
   - Per guest: recorded position, move delay, called flag, state-11 entry
     turn, interlude turn.
2. **Building** (partly APPROX until the tool is traced)
   - A queue tool places type-3 cells. Each new cell links toward its
     predecessor. The first cell must be the entrance's neighbour in the
     entrance's connection direction.
   - After every edit, recompute the back and size like `0xdd43c` (stop at
     1000) and run the re-evaluation of `0xdd57c`.
   - The placement-validity rules, cost and maximum length are unresolved:
     keep them as `APPROX:QUEUE-BUILD`.
3. **Join** (state 10 at the back cell): room `count < 4·cells`, the
   excitement gate 45, then the limit (100 for HasQueue, the QWTC formula
   otherwise), then append and walk (state 12).
4. **Standing** (state 11): section 5.2 exactly. That includes the move delay
   `trunc(1.2f·pos)`, the gap ≤ 2 rule, the 30-turn needs window and the
   ProvidesRelief exception. Keep the boredom test **as written** (it is dead
   code), and add **no** timeout.
5. **Positions**: 4 per cell, depth bytes 0/63/127/191, lateral 114..141 from
   the shared RNG. The fourth-slot branch is unresolved
   (`APPROX:QUEUE-SLOT3`).
6. **Admission**: section 6 gates on each ride update, with the
   pending-visitor handshake. Remove the guest from the list after the script
   consumes LETMEON.
7. **Units**: convert park-turn constants with the existing park-turn clock.
   Do not use 60 Hz ticks. DUR stays raw. BOUNCE holds DUR seconds of the
   scheduler clock with release at most 1 s late.

## 9. Proposed bound for `queues.no-stuck-queue`

The original gives no wall-clock timeout (5.3), so the gate should check
liveness against the admission rule, plus a throughput bound for BOUNCE rides.

**(a) Admission progress (exact, original-derived).** Suppose all of these
hold on a ride update:

- the attraction is open and the ride update reaches admission;
- `LETMEON == 0`, `ONRIDE < CAPACITY`, and `RUNNING == 0` or RunsContinuously;
- no visitor is pending and the queue is non-empty;
- the head stands with recorded position 0.

Then the head must be called on that same update. A violation for two
consecutive updates is a stall. OpenTPW's tick-to-park-turn mapping must be
used, and the gate already samples every tick.

**(b) Wait bound for BOUNCE rides (derived from data plus one APPROX
parameter).** Take a guest at 0-based position `p` on joining, with the ride
open and unbroken throughout. Each of the CAP slots frees after DUR s plus less
than 1 s release granularity. The guest boards at the `(p+1)`-th free slot, so

`W(p) ≤ (⌊p / CAP⌋ + 1) × (DUR + 1 s + τ)`

and over a queue of at most `Qmax` guests,
`W_max = ⌈Qmax / CAP⌉ × (DUR + 1 + τ)`.

- `CAP = clamp(InitCapacity)` and `DUR = clamp(InitDuration)` come from the
  data.
- `Qmax = min(100, 4 × cells)` (HasQueue). Until QUEUE-I lands it is the
  current virtual 20.
- `τ` is the boarding latency per slot turnover: call → walk to the stand
  point → state 13 → script consumption, plus the ≤ 2-turn move delay. It is
  **not derived** (walk speed and RSE loop latency are open) and must be an
  explicit `APPROX:QUEUE-TAU` parameter reported with the result.

Belly Bounce (1100, jungle): DurationUnit 1, HasQueue (default layer),
CAP = 5, DUR = 30, InitSpeed 60, QWTC 130 (unused, because HasQueue gives
limit 100), RunsContinuously 1 (Bouncy.sam), so the RUNNING gate in section 6
is bypassed and admission runs whenever ONRIDE < 5.

- With the current virtual queue of 20: `W_max = 4 × (31 + τ) = 124 s + 4τ`.
- With QUEUE-I and N cells: `⌈min(100, 4N)/5⌉ × (31 + τ)`.

The gate's measured 149.95 s against 124 s implies τ ≈ 6.5 s per turnover
under the current OpenTPW model. Report it as a measured τ, not a pass/fail
threshold, until τ is traced.

**(c) Out of scope.** For non-BOUNCE rides (coasters with DurationUnit 0,
DurationUnit 2/3 rides, TOUR/BUMP/COAST controllers) the cycle time is
controller-driven and not derived. The row stays UNRESOLVED for them, with (a)
still checkable.

## 10. Unknown, or must stay APPROX

- Queue-tool placement rules, cost, connection to paths, maximum length, and
  the UI flow (UI-031). Only the link semantics and one rotation mapping are
  traced.
- Compass meaning of link values 1/4/16/64. The run-time neighbour offset
  tables (data `0xec52c/0xec554/0xec57c/0xec5a4`, zero in the file).
- Fourth-slot placement (`0xdde74`) and the exact sub-cell axis per link.
- `τ` boarding latency: navigation speed (PPC-guests unresolved), state 13/14
  timing, and the RSE loop between LETMEON and BOUNCE.
- Ride `+408` state names, `+100`, `0xdfe34` (relief-object condition), the
  `0xe02ec` excitement-gate predicate, and the `0xee8f8` re-evaluation exits.
- Cycle times for non-BOUNCE controllers. The DurationUnit semantics beyond
  "no conversion at the host write".
- Exact cadence: whether every live guest and ride is updated every park turn
  depends on the live-list eligibility flags (PPC-guests).
- Whether save loading can restore `+508 < +520 − 70` (only this would revive
  the boredom exit).

## 11. Witnesses and tests

The lane is `tools/ppc-analysis/lanes/queue/`: `queue_evidence.py` and
`test_queue_evidence.py`. The runner discovers it automatically.

- **Identified binary**: 33 block digests, 94 decoded field witnesses, 44 call
  targets, 4 branch/compare shapes, 3 mask decodes, 6 data constants,
  12 diagnostic texts, and 15 schema offsets.
- **Tests**: 25 in total. 20 run without assets (pure arithmetic, decoders, a
  synthetic field mutation check and the state-11 simulation). 4 need the
  identified binary, including 3 binary mutations: timeout operand 101, the
  QWTC offset 432, and the limit floor 5.0. 1 needs PC Data (Belly Bounce
  parameters and bound).
- **Mutation check**: 9 mutations of the module were each caught by at least
  one test:
  - the timeout operand;
  - the limit floor;
  - the move-delay factor;
  - positions per cell;
  - the HasQueue limit;
  - the boredom window;
  - the schema offset;
  - release granularity;
  - the BOUNCE ×1000.
