# WALK-R: guest steering and the walk terms of the queue gate

Lane: WALK-R (research). Input: BOARD-plan derives the `queues.no-stuck-queue` wait bound
from the original except for two walk terms. w is the called head's walk from slot 0 to the stand
point (state 13). w₂ is the new head's walk up to slot 0 (state 12). BOARD-plan §6 calls them blocked,
because no minimum speed of the steering step (`0xfec9c`) was traced. BOARD-plan §8 therefore fills them
with OpenTPW's own walk model (w = 2, w₂ = 6), which needs a walk APPROX on the gate. This lane traces the
steering and speed model of the Feral Mac `SimThemePark.data` (SHA-256 `04809cd4…e295f5`). It derives w
and w₂ from that model and states when a walk can stall. It also specifies WALK-I (OpenTPW's walk) and
the gate change.

Reproduce (private assets stay outside Git):

```sh
python3 -I tools/ppc-analysis/lanes/walk/walk_evidence.py /path/to/mac-feral/bin            # witness
python3 -I tools/ppc-analysis/lanes/walk/walk_evidence.py /path/to/mac-feral/bin --derive   # + enumeration
python3 -I tools/ppc-analysis/run_evidence_checks.py --mac-bin /path/to/mac-feral/bin
```

Confidence follows QUEUE-plan. **High**: an identity-pinned instruction or table entry whose operation
was interpreted. **Medium**: a role inferred from callers, data or control flow that was read but not
pinned field by field. **Derived**: a value computed from high/medium rules by bounded enumeration.
All results are static. Nothing original was executed.

## 1. Result

- **There is a speed floor.** The speed setter `0xffe38` stores max speed `+28 =
  max(655, trunc(min(s, 2)·0.2·65536))` and max force `+24 = max(655, trunc(min(s, 2)·0.4·65536))`.
  655 is 0.01 cell per update (high). The input s is a per-guest smoothed speed,
  `s ← 0.75·s + 0.25·(+192 + +194 + +196)/100`, updated every guest update. The base `+192` is always
  one of 60, 80, 100, 120 or 140 (high). So **s ≥ 0.6·(1 − 0.75ⁿ)** after n updates of the guest's
  life, and s ≥ 0.59 once the guest is 15 updates old.
- **Arrival is a radius, not a snap.** follow_path declares arrival on the last route segment at
  octile distance < FixMul(1.6, radius) = 20971 (0.32 cell). The radius is 0.2 cell (`+4 = 13107`,
  written only by the constructor). Before arrival the desired speed is `min(+28, d/2)`, which is at
  least `min(+28, 0.143 cell)` (high for the rules).
- **No 0.7 factor.** Energy does not enter the speed. The only speed modifier besides the base is
  +0.25 (`+194 = 25`) while toilet > 80, plus a decaying boost `+196` (high).
- **Derived walk terms** (in park updates = turns under BOARD A2). They hold for guests at least
  15 updates old (s ≥ 0.59), Belly Bounce's default stand position, and a straight front segment
  (section 9). They cover both separation variants, fenced and open sides, both FixMul/FixDiv
  roundings and stale velocities:

  | Term | Slot 0 at the entrance edge (medium) | Slot axis reversed (alternative) | Gate value |
  | --- | --- | --- | --- |
  | w (state 13, slot 0 → stand point) | **12** | 20 | **20** |
  | w₂ (state 12, slots 1–3 → slot 0) | **15** | 15 | **15** |

  The gate takes the larger value, w = 20, because the slot axis is medium. It can use 12 once
  the link compass is pinned.
- **Belly Bounce** (BOARD-plan §7): H = 21 + w + w₂ = **56** turns.
  `τ_max = 102 + 5·35 = 277 turns = 68.696 s`. `W(p) ≤ (p + 1)·56 + (⌊p/5⌋ + 1)·121 + 1`.
  W_max (p = 99) = **8021 turns = 1989.208 s**; at p = 50 it is 4188 turns.
  w₂ = 15 also meets BOARD-plan's interlude condition (w₂ ≤ 15, §7.1).
- **Stall.** A walk can fail to finish under the traced rules in two cases. (a) **Slow guests,
  w₂ only**: for s ≤ 0.31 the stuck detector (6 non-improving of the last 15 steps) can fire during a
  move-up and reroute. Progress is measured against the cell-centre waypoint, not the slot point
  (section 7). (b) **Outside the straight-strip conditions**: corner fronts, a refused position
  commit, or separation with an unknown neighbour set. The gate judges these separately (section 12).

## 2. Assumptions (bounded claims)

- **W1 cadence** (BOARD A2). Every live guest runs its update once per park turn. The update does
  the speed update `0xe6b28` (called at `0xeed0c`, before the phase-gated needs and the state
  handler) and, in states 12/13, exactly one walk step `0xe6454`.
- **W2 age.** The guest has had at least 15 updates since its constructor (`0xe7644`, `+200 = 0.0`).
  Then s ≥ 0.6·(1 − 0.75¹⁵) = 0.592. Guests arrive by the bus-stop/ticket-booth lane, so a queued
  guest is in practice far older. The gate checks it (section 12).
- **W3 straight front.** The entrance cell E, the front cell Q and the cell behind it B lie in a line.
  This always holds for E and Q (the front cell is E's neighbour in the direction of its connection
  bit, QUEUE-plan §3.3). For B it is assumed. Slots 0–3 lie in Q (QUEUE-plan §3.4). The fourth-slot
  branch `0xdde74` at a corner is not covered.
- **W4 stand point.** `EntryCellStandPos` = (0.5, 0.5), which is the jungle `Rides.sam` default.
  Belly Bounce's `.sam` does not override it.
- **W5 standing state.** At the start of a walk, the guest stands within Euclidean distance
  arrival radius + 2·`+28` of its slot point. That covers the arrival update's own step plus any
  braking drift. It also stands at least a radius from the strip's sides and ends, because avoid_walls
  keeps it there. Its velocity is anything up to `+28`, because route setup does not reset `+16`.
- **W6 separation.** The separation force's direction and neighbour set are not interpreted
  (section 6.2). Only its traced on/off rule and its magnitude bound (≤ 0.1·`+24`) are used. The
  enumeration runs with separation off and with the maximum force directly against the walk.

## 3. The steering step `0xfec9c` (high)

One call per walk step: `0xe6454 → 0xffd2c → 0xfec9c`, with the navigation object at guest `+212`.

| Order | Code | Rule |
| --- | --- | --- |
| Forces | `0xfecd8` loop over the global list (data `0xecd6c`) | for each (weight, behaviour): force = vtable+8(behaviour, nav), truncated to `+24` (`0xff098`), times the weight (`0xfd580`, FixMul), summed |
| Sum | `0xfed54`, `0xfed74` | sum truncated to `+24`, divided by ONE (`0xfd704`, FixDiv; data `0xecd84` = 0x10000, set at `0x10031c`): mass 1 |
| Velocity | `0xfed94..0xfedc0` | `+16 += force`, truncated to `+28` |
| Position | `0xfedc4..0xfee84` | new = `+8 + +16`. Committed only when the map accepts the new cell for mask `+180`. Otherwise a 1 is shifted into the history `+176` |
| Progress | `0xfeec4..0xfeeec` | progress `0xff738` = ONE − FixDiv(remaining, `+160`). A 1 is shifted into `+176` when it did not increase (`+172`) |

Truncation `0xff098`: unchanged when the length is < max, else scaled by FixDiv(max, length). Length is
`trunc(sqrt)` of the squared components, computed exactly below 2¹² and otherwise in units of 2⁸ or 2¹⁸.

**Behaviour list** (`0xff99c`, called at level load `0x104d20` and cleared at `0x104e1c`). Seven
behaviours are registered (data `0x4477c`). They are looked up by name from the string pool at code
`0x1d2e8d`:

| Name | Weight | Force method (vtable +8) |
| --- | --- | --- |
| `avoid_walls` | 0x10000 − 6554 = 0.9 | `0xfdc70` |
| `follow_path` | 0x10000 − 0x8000 = 0.5 | `0xfe628` |
| `separation` | 6553 = 0.1 | `0xfd778` |

`seek`, `arrival`, `avoid_obstacle` and `repel_obstacle` are registered but not in the list.

## 4. Where walk speed comes from (high)

| Field | Writer | Value |
| --- | --- | --- |
| `+192` base | constructor `0xe48d0` (data `0x4105e[rand % 5]`); `0xd2870`, `0xf2abc/0xf2ac8`, `0xf4b70/0xf4b98` read the same table | 60, 80, 100, 120, 140 |
| `+194` bonus | constructor 0; needs block `0xef0d8/0xef0e4` every update: 25 while toilet `+428` > 80, else 0; `0xef8c4` (a state entry) 0 | 0 or 25 |
| `+196` boost | `0xeb0f4` adds an event-dependent amount; `0xe6bbc..0xe6bd8` decays it by trunc(×99/100) per update | ≥ 0 (u16) |
| `+200` s | `0xe6b28`: `(3.0·s + (+192 + +194 + +196)/100)·0.25` (data `0x550c`, `0x5508`; divisor 100 = data `0x41062`) | float |
| `+24`, `+28` | setter `0xffe38` with s (`0xe6ba8`) | see §1 |

The speed table data `0x41058` = {0, 25, 50, 60, 80, 100, 120, 140} has no writer: every reference
through its two TOC slots is a load. No traced path lowers `+192` below 60. Sums of u16 fields cannot
be negative, so 0.6 bounds the smoothed input from below. At s = 0.6 the top speed is
0.12 cell per turn = 0.48 cell/s. At s = 1.0 it is 0.2 cell per turn = 0.806 cell/s; at s = 1.4,
1.13 cell/s. Nothing reads energy here. OpenTPW's `Energy < 20 ⇒ × 0.7` has no counterpart, which
PPC-guests already noted.

## 5. Arrival, waypoints and routes

- **Destination** (`0xe6864`, high). The 8.8 destination per axis (cell << 8 | byte) is shifted left by
  8 (`0xe68cc/0xe68d0`), so a sub-cell byte b sits at **b/256** of a cell, not b/255. It is then routed
  with `0xffcfc → 0xff224`.
- **Route setup** `0xff224` (high for the stores). A routing failure sets arrived (`+96 = 1`), count 0
  and `+184 = 1` (`0xff294..0xff2cc`), so the walk step reports arrival at once. On success, at most
  5 waypoints are buffered (`0xff34c`) at cell centres `(cell << 16) + 32768`, idx `+92 = 0`,
  `+96 = 0` and `+176 = 0`. **Velocity `+16` is not reset.**
- **Route contents** (medium). The search `0x101c64` stores the current cell before each move
  (`0x101d6c`) and appends the destination on reaching it (`0x1028d4`). So the route is
  [start cell, …, destination], and a start inside the destination cell gives [destination]. The
  smoothing pass `0x101434` keeps the start cell for adjacent cells. The guest therefore first steers
  to its own cell centre and passes it at octile < 0.4 cell.
- **follow_path** `0xfe628` (high):
  - Arrived, or no route: force = −velocity (brake).
  - Last segment (idx = count − 1): the target is the exact destination `+76/+80`. Arrival is
    declared when octile < FixMul(1.6, `+4`) = 20971 (`0xfe7ac..0xfe7d8`). Desired velocity =
    `min(+28, |offset|/2)` along the offset (`0xfe938..0xfea70`).
  - Otherwise: the waypoint is passed at octile < FixMul(2.0, `+4`) = 26214 (`0xfe850`), and the
    force is `offset − velocity` (seek, not normalised).
- **Walk step** `0xe6454` (high). After the steering step, `0xffd60` returns ONE when arrived. The step
  returns 0 (arrived) when that equals data `0xec790` = 0x10000 (`0xe7478`), 2 when guest `+396` is
  set, and 1 otherwise. Arrival is tested on the position at the **start** of the update, and the
  update still moves.
- **Stand point** `0xde1d8` (high for the bytes). Byte = trunc(255·f) after rotating
  (fx, fy) by the object rotation (0/90/180/270; `0xde490..0xde528`), in the entrance cell. With
  (0.5, 0.5) every rotation gives (127, 127): the stand point is the **entrance cell's centre** (−1/256),
  not the front cell's edge.

## 6. The other forces

### 6.1 avoid_walls `0xfdc70` (high for the edge branch)

The probe is position + velocity (`0xff1c4`). The probe cell's edges are tested with `0xd768c` and mask
`+180`, in the direction of motion only (velocity sign per axis; zero counts as positive). A blocked
edge whose probe lies within the radius (0.2) gives a force of 2 × penetration, perpendicular to that
edge (`0xfde14..0xfdfe0`). Four diagonal-corner branches follow (`0xfdfe4..0xfe5e0`, via `0xfc9e0`); they
were read but not modelled. In a straight queue strip the side forces are perpendicular to the walk.
The enumeration models them with both sides fenced and with both open.

### 6.2 separation `0xfd778` (high for the gate, medium for the force)

It is **off** when (idx is the last segment, or arrived) and the octile distance to the current
waypoint is ≤ ONE + radius = 1.2 cells (`0xfd7b4..0xfd840`). Otherwise it sums a callback `0xfd8a8` over
neighbours within 2 × radius (`0x10087c`). The force goes into the sum truncated to `+24` with weight
0.1, so it is at most 0.1·`+24`. In w₂ (route [Q], target in Q) it is always off. In w it can be on
only on the first segment, toward Q's centre.

## 7. Stuck detector and stalls

`0xff810` counts the set bits among the low 15 of the history `+176`, as count/15 (`0xff8e8..0xff97c`).
follow_path reroutes (`0xff224` mode 1, which prepends the current cell) when progress ≠ ONE and that
fraction is ≥ 0.4, that is 6 of 15 (`0xfe6a4..0xfe700`). Progress (`0xff738`) is measured against
the **current waypoint**. On the last segment that is the destination cell's centre, not the target
point.

Consequence: in w₂ the target (slot 0, depth byte 0) lies on Q's edge, half a cell from Q's centre.
A guest that starts near the centre and walks to slot 0 moves *away* from the waypoint, so every step
sets a history bit. A fast guest arrives (0.32 cell short of slot 0) before 6 bits accumulate. In the
enumeration, a guest with s ≤ 0.31 does not, and reroutes (in all 8 variants; none for 0.32 ≤ s < 0.59). The reroute sends it back to Q's centre
first, then the same thing can repeat. Whether it ever finishes is not derived. This is the precise
stall condition: **move-up walks with s ≤ 0.31**. With base speed ≥ 60 that means guests younger than
3 updates (s ≥ 0.6·(1 − 0.75³) = 0.347 from n = 3). Under W2 it cannot occur.

Other conditions under which completion is not claimed:
- a refused position commit (the new cell is not walkable for mask `+180`);
- separation with a neighbour set that is not the bounded, opposing model;
- corner geometry (W3);
- guest `+396` (immediate result 2, which only shortens the walk).

## 8. Geometry used

Local frame, in cells: E is y ∈ [−1, 0), Q is y ∈ [0, 1), B is y ∈ [1, 2), with x ∈ [0, 1).

- Stand point: (127/256, −1 + 127/256).
- Slot k: (lateral/256, depth_k/256), depth ∈ {0, 63, 127, 191}, lateral ∈ 114..141. `0xddcc4` writes
  depth or 255 − depth into x or y by the cell's link byte (1 → y = depth, 16 → y = 255 − depth,
  4 → x = 255 − depth, 64 → x = depth). Slot 0 is at the edge toward the entrance if link 1 points to
  −y. The compass of the link values is not established (QUEUE-plan §3.3), so the reversed variant
  (slot 0 at Q's far edge) is also derived.
- Slot 0 to stand point: 0.504–0.507 cell. Slot 3 to slot 0: up to 0.76 cell.

## 9. Derivation of w and w₂ (derived)

`walk_evidence.walk` restates sections 3, 5 and 6 in 16.16 fixed point, one call per update. It runs:

1. avoid_walls on the strip sides, fenced or open;
2. follow_path, exactly;
3. separation by the traced on/off rule, either absent or at its maximum against the walk;
4. per-force and total truncation to `+24`, then the `+28` velocity truncation;
5. the position update;
6. the progress history and the 6-of-15 reroute.

A run fails if the guest leaves the strip, reroutes, or exceeds 5000 updates.
`worst_walk` enumerates, per speed s:

- standing starts on a 9 × 9 grid within W5's disc;
- 17 stale velocities (zero, and 8 directions at full and half `+28`);
- target laterals 114, 127 and 141, with origin laterals 114 and 141 for w₂.

`derive_bounds` repeats this over s = 0.59 … 2.00 in steps of 0.01, and over the 8 variants
(separation off/oppose, fences on/off, FixMul/FixDiv rounding to nearest or floor).

| Slot axis | w max | w₂ max | Failures (s ≥ 0.59) |
| --- | --- | --- | --- |
| entrance edge | 12 | 15 | 0 |
| reversed | 20 | 15 | 0 |

Below s = 0.59, w completes for every s down to the floor: at most 74 updates (separation off and
opposing, fenced, nearest rounding; the reversed axis was not enumerated below 0.59). w₂ reroutes for
s ≤ 0.31 in every variant. For 0.32 ≤ s ≤ 0.58 it completes in every sampled case, within 24 updates over all 8 variants (section 7).

A second check, `varying_speed_check`, re-derives the caps every update while s follows the traced
smoothing with random inputs (minimum 0.592, up to 3.0). 400 walks (200 seeds × w, w₂) all
stay within 20/15. Because s changes slowly, it adds no worse case.

This is a bounded enumeration, not a closed-form proof. The analytic fact behind it (high for the
rules): on the last segment, with separation off and no wall force along the walk, the velocity
relaxes toward a desired speed ≥ min(`+28`, 0.143 cell) with factor ½ per update
(`v' = v + ½(desired − v)`). A guest therefore cannot stall there.

## 10. OpenTPW compared with the traced model

| Aspect | Original (traced) | OpenTPW (`source/OpenTPW/World/Guests`) |
| --- | --- | --- |
| Speed source | per guest: base 60–140 (+25 toilet, + boost) / 100, smoothed 0.75/0.25 per update; cap 0.2·s cell/update, floor 0.01 | `GuestSettings.WalkSpeedCellsPerSecond` 1.0 ("approximation: no original source", `GuestSettings.cs:47`) |
| 0.7 factor | none | × 0.7 while Energy < 20 (`GuestSimulation.cs:347`) |
| Cadence | one steering step per park turn (248 ms) | kinematic `Move` per 60 Hz tick (`GuestSimulation.cs:350`) |
| Motion | velocity with acceleration (≤ `+24`, weight 0.5), carried over between walks; arrival ramp min(vmax, d/2) | constant speed, heading snaps, no carry-over |
| Arrival | octile < 0.32 cell on the last segment; waypoints passed at octile < 0.4 | exact snap to the waypoint |
| Waypoints | cell centres from the start cell | cell centres plus random offsets, cell by cell |
| Forces | avoid_walls 0.9, separation 0.1 | none |
| Stuck handling | 6 of 15 non-improving steps → reroute | none |
| Sub-cell bytes | byte/256 | byte/255 (`QueuePositionPoint`, `GuestSimulation.cs:761`) |
| Stand point | entrance cell at EntryCellStandPos (Belly: its centre) | front cell's edge toward the ride (`StandPoint`, `GuestSimulation.cs:787`, QUEUE-007) |

OpenTPW's walk terms (BOARD-plan §8: w = 2, w₂ = 6) are below the traced 20 and 15, so OpenTPW waits
stay under the traced bound.

## 11. WALK-I implementation spec

Goal: replace `WalkSpeedCellsPerSecond` and the 0.7 factor with the traced model. Scope is guest walks
(paths and queues). Rendering interpolates between park turns.

1. **Per-guest speed state.** `BaseSpeed` (u16, from {60, 80, 100, 120, 140} by `random.Next(5)` at
   creation), `ToiletBonus` (25 while Toilet > 80, recomputed each update), `Boost` (u16, ×99/100
   truncated per update; increments stay `[APPROX:WALK-001]` until `0xeb0f4` is traced), and
   `SmoothedSpeed` (float, 0 at creation). Each park turn, before the state logic, set
   `SmoothedSpeed = (3·SmoothedSpeed + sum/100)·0.25`. Then `MaxSpeed = max(655, trunc(min(s, 2)·0.2·65536))`
   and `MaxForce = max(655, trunc(min(s, 2)·0.4·65536))`.
2. **Navigation state** in 16.16 ints: position, velocity (never reset by a new destination), radius
   13107, arrived, waypoint index, and the buffered cell-centre waypoints (route includes the start
   cell; ≤ 5 buffered, re-route on exhaustion), history bits, progress and total.
3. **Steering step** once per park turn in walking states, in list order avoid_walls (0.9),
   follow_path (0.5), separation (0.1). Truncation, FixMul/FixDiv and length are exactly as
   `walk_evidence.py`. Use FixMul rounding to nearest (Toolbox) and keep one fixed-point helper.
   The position commit checks the cell's walkability for the guest.
4. **follow_path, arrival and the reroute** exactly as section 5 and section 7. The walk step returns
   arrived / still walking. A routing failure is arrived (as `0xff224`).
5. **avoid_walls** edge branch as section 6.1. The corner branches and the separation callback stay
   `[APPROX:WALK-002]`/`[APPROX:WALK-003]` (evidence needed: `0xfc9e0`, `0xfd8a8`, `0x10087c`).
6. **Geometry**: sub-cell bytes /256. Stand point = entrance cell (`+50`) at
   trunc(255·EntryCellStandPos) after rotation (`0xde1d8`). This closes QUEUE-007. QUEUE-006's axis
   keeps its tag until the link compass is pinned.
7. **Remove** `GuestSettings.WalkSpeedCellsPerSecond`, `GuestSimulation.Speed` and the 0.7 factor.
   `HeadNotReadyBound` (`M3Gate.cs:1120`) must then use the traced per-walk bounds: w₂ per move-up,
   and per queue cell walked at most ⌈1.2/0.118⌉ + 3 updates. That cell term needs its own
   enumeration in WALK-I.
8. **Tests**: port `test_walk_evidence` cases. Required:
   - speed caps (655 floor, cap at 2.0);
   - smoothing;
   - arrival radius 20971;
   - the w = 10 / w₂ = 14 worst cases at s = 0.59 on the 7-grid;
   - the w₂ reroute at s = 0.2;
   - velocity carry-over.

## 12. Gate change for `queues.no-stuck-queue`

This applies to BOARD-plan §8 and does not need WALK-I.

1. Set **w = 20** and **w₂ = 15** (`WalkTerms` constants citing WALK-plan §9; use w = 12 once the slot
   axis is pinned). This replaces `⌈d / (0.7·v·T)⌉ + 1` from `WalkSpeedCellsPerSecond`, so H = 21 + 35 =
   **56**. Report τ_max = `CAP·H + R + 1 − (DUR + 1 s)/T` = 277 turns = 68.696 s and W_max = 8021
   turns = 1989.208 s.
2. **Drop the walk APPROX.** Do not introduce BOARD-plan's narrowed tag. `[APPROX:GATE-003]` goes away
   with BOARD's spec, and no walk tag replaces it. The derivation string cites
   `walk_evidence.py` (SHA-pinned binary) and conditions W2–W4.
3. **Scope conditions** join A3's excluded-turn rule. A wait is not judged (counted and reported) when,
   during it:
   - a boarding guest (a called head or a new head) was younger than **15 park turns** since spawn (W2);
   - the front segment is not straight: E, Q and the next queue cell are not in a line (W3);
   - the ride's EntryCellStandPos ≠ (0.5, 0.5) (W4).
4. **Stall, judged separately.** A guest in the queue-walk states (OpenTPW `WaitingToBoard` walking to
   the stand point, `MovingUpQueue`) for more than w (20) or w₂ (15) consecutive turns, within the
   scope conditions, is a **FAIL** labelled `walk-stall`. The traced model completes those walks
   within these bounds. Outside the conditions it is reported, not judged.
5. Unchanged: BOARD-plan §8 assertions 1–6, the baseline check (p = 50 bound 4188 turns ≥ the
   measured 254.4 s = 1026 turns).

## 13. Unresolved

- The link-value compass (which edge depth 0 is on). It is medium here, and the gate uses the larger w.
- The separation callback and neighbour query (`0xfd8a8`, `0x10087c`), and the corner branches of
  avoid_walls (`0xfc9e0`).
- The route search `0x101c64` beyond its route-store points, and the `+164/+168` bookkeeping. The
  enumeration uses the segment-sum form of the remaining distance.
- The `+196` boost increments (`0xeb0f4` caller) and the `+194` writer at `0x1a95fc`. These only add
  speed.
- Whether the state-11 handler also steps (W5 allows 2·`+28` of drift either way).
- The cadence (BOARD A2) and real-clock jitter (A1).

## 14. Witnesses and tests

Lane `tools/ppc-analysis/lanes/walk/`: `walk_evidence.py` and `test_walk_evidence.py`. The runner
discovers it.

- **Identified binary**:
  - 24 block digests;
  - 80 decoded D-form fields and 5 `rlwinm`;
  - 36 call targets (including 10 through FixMul/FixDiv/sqrt glue, with 3 glue imports resolved by name);
  - 8 data constants and the speed table;
  - 5 TOC globals;
  - 3 behaviour names, objects, vtables and methods.
- **Tests**: 23 in total. 17 run without assets:
  - speed caps and smoothing;
  - radii and fixed-point helpers;
  - geometry and routes;
  - walk results (pinned worst cases, all 8 variants at s = 0.59, the slow-guest reroute, the
    arrival-factor sensitivity, varying speed);
  - the gate arithmetic;
  - a synthetic decoder with 4 mutations.

  6 need the identified binary; 5 of them are binary mutations: the speed floor 655 → 654, the
  arrival factor low half, the follow_path weight, the base-speed table entry 60 → 59, and the 0.2
  speed factor.
- **Module mutation check** (temporary copies outside Git): 12 of 12 mutations caught:
  - speed floor;
  - arrival factor;
  - ramp divisor;
  - smoothing;
  - slot depth;
  - stand scale;
  - a route without the start cell;
  - octile;
  - the follow_path weight;
  - the reroute rule;
  - the separation on/off rule;
  - the base-speed table slice.
