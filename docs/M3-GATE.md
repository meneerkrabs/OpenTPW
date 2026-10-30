# M3 gate: one complete gameplay loop (evaluator and baseline)

October 10, 2026. This is the deterministic evaluator for milestone M3
([COMPLETION-PLAN.md](COMPLETION-PLAN.md), "M3 — One complete gameplay loop").
It **measures** the current runtime and changes no gameplay. A pass means the
invariant holds in OpenTPW's simulation. It does not mean the original economy
or visitor behaviour is reproduced: that needs linked original traces (see
COMPLETION-PLAN.md, "Before acceptance of M3/M4 semantics").

Code: `source/OpenTPW/Client/M3Gate.cs` (`M3Gate`, `M3GateRun`, `M3GateReport`),
`source/OpenTPW/Client/M3GateApproximations.cs` (GATE register).
Tests: `M3GateTests` (no assets), `M3GateAssetTests` (3 simulated minutes,
inconclusive without `OPENTPW_GAME_PATH`). The tests use MSTest, like the rest of
the suite.

## Running

```sh
dotnet source/OpenTPW/bin/Debug/net11.0/OpenTPW.dll --game-path '/path/to/Theme Park World' \
  --m3-gate [--level jungle] [--seed N] [--minutes 30] [--report m3-gate.json] [--no-determinism]
```

`OPENTPW_GAME_PATH` can replace `--game-path`. The command opens no window and
uses no GPU or audio. It prints one summary line per row and, with `--report`,
writes the JSON report (`rows[]` with `id`, `area`, `verdict`
pass/fail/unresolved, `firstViolationTick` and `evidence`; plus `timeMapping` and
`counts`).

Exit code: **1** when any row fails; **2** when no row fails but at least one row is
unresolved; **0** only when every row passes. **M3 is accepted only at exit code 0.**
An unresolved row is a check that has not been decided, so it can never count as
acceptance (review M3-GATE-V, B1). The default seed
is `Level.GuestSeed`, the seed the game uses. The economy uses its own fixed seed
from `ParkEconomyRuntime`. A full 30-minute run with the determinism probe takes
about 10 s on an M-series Mac (two runs of 108,000 ticks).

## Time: what "30 minutes" means

The gate runs 30 minutes of **normal-speed simulation time**: 30 × 60 s × 60 Hz =
**108,000 fixed ticks** of `FixedStepClock`. Guests, object scripts and the economy
all advance once per fixed tick, in the order `Level.Update` uses. The park
calendar does not count real minutes. `ParkCalendar` (from the Mac binary:
`BIN:STP-PPC:0x101C22E0` one park turn = 248 ms, `0x100E4394` 3,750 park-clock
seconds per turn; sampling into 60 Hz ticks is `EXT:fixed-60hz-clock`) turns
108,000 ticks into 7,258 park turns, about 315 park-clock days (Year 1, month 11,
day 11). Thirty park-clock minutes would be less than one turn (about 15 ticks),
which cannot exercise anything. Reading "in-game minutes" as simulation minutes is
OpenTPW's own harness unit, `[EXT:m3-gate-minute]` (formerly `APPROX:GATE-001`): the
original has no gate and paces play by scaled real time, a park turn every eighth 31 ms
substep (tp.exe `0x54f4c4`, `0x54f668`) and 3,750 park-clock seconds per turn (the rate
15,000 / 4, `0x4f7e80`, `0x4f8260`). The gate's registered assumption, **[APPROX:GATE-006]**,
covers its queue walk terms, which come from enumerating the steering OpenTPW runs since
WALK-I (below).

## Scripted park

Level `jungle`, loaded with `OriginalPark.Load( level, readShippedSave: ParkStart.ReadsShippedSave( ParkStartKind.FullSimulation ) )`
and a Full Simulation economy (`ParkEconomyRuntime.ForOriginalLevel`, standard
balance: $50,000, $20 entrance fee). The objects are `OriginalObjectRuntime`s:
the CPU half of `OriginalObject`, running its original script with no model upload.
`Level` cannot be used because it creates the terrain, sprite and HUD GPU
resources. The gate therefore repeats `Level`'s composition: `SetupObjects` /
`ConnectObjectsToGuests` / `ConnectObjectsToEconomy` / the `Update` tick order.
Where the HUD build flow has a rule, the gate calls the same code instead of a copy:
`ParkObjects.Check` (static overload over any grid and occupancy),
`Level.GetCentredAnchor`, `ParkEconomy.TryBuild`, `Level.RegisterWithGuests` /
`Level.ResolveVisitorCells` (the code `Level.RegisterObjectWithGuests` runs),
`ParkPathBuilder` (PATH-I: the path tool's code) and `Level.BuildQueueCell` (static
overload: the queue tool's per-cell code). The
remaining copies are listed under "Divergences from the player's build flow". If
`Level` changes, those copies can drift from it.

1. **Entrance**: MAP `InitialPath` cells (10), the `Standard.sam` arrival lanes A/B
   and the Gates, Lights and Bus fixed items (`ParkObjects.AddDefaultFixedItems`'
   set). As in `Level.ConnectObjectsToEconomy`, they are registered with the economy
   after `AttachGuests`, linked to the guest payment bridge and get their open state
   mirrored every tick.
2. **Paths**: one straight segment of 14 cells from the end of the entrance walkway, in
   the direction of lane A's last step. It is laid with PATH-I's player-facing builder,
   `ParkPathBuilder.BuildSegment(start, end)`: the line is snapped and straight, every
   cell goes through the path rules (owned land, `CanChangeCellType`, no path over a
   queue cell other than a queue end, objects, the terrain rule, money), and `Costs.PathCell`
   ($20) is posted to OtherCosts per new cell. The builder is constructed like
   `Level`'s: the shared cell map, the guests' walk grid, the park economy, the object
   build grid, and the gate's object footprints as the blocked test. The walkway's last
   cell is the segment start, so it is reported under `Existing`, not `Built`. The
   gate's own choice is the segment. The HUD click route is not exercised (no HUD
   without a GPU).
3. **Objects**: the lowest-Info.Id **researched, buyable** catalog entry per role:
   the first ride with `Info.HasQueue` (Belly Bounce, 1100), the first shop that
   satisfies a need (Drinks Shop, 1203; `M3GateOptions.ShopFilter` and `ShopRotation` narrow the shop and its
   rotation, as `ShopVisitAssetTests` does for the Burger Shop) and the first toilet (`ProvidesRelief`:
   Small Toilet, 1402). Each object goes through the HUD build flow's rules
   (`Level.PlaceObject`): the site search tries clicked cells near the path in all
   four rotations, turns each into an anchor with `Level.GetCentredAnchor` and
   keeps it only when `ParkObjects.Check` allows it. The gate's own choice is the
   site. A shop or toilet entrance must open onto a built path cell (nearest the park
   entrance first) and the exit with the shortest snap wins. For the HasQueue ride the
   entrance's outside cell must instead be **free for a queue** (`QueuePaths` refuses a
   path cell there with `NoFrontCell`), and a run of exactly N queue cells must lead from
   it to a cell beside a path. Among those sites the exit with the shortest snap wins,
   then the front nearest a path. The run is found depth-first in
   `GuestPathGrid.Directions` order, so it is deterministic. The object is bought with
   `ParkEconomy.TryBuild`, registered with guests by `Level.RegisterWithGuests`
   (attractions only; this sets `QueueFrontCell`/`QueueEntranceDirection`; a non-path
   outside cell snaps to the nearest path cell: the gate does not lay the cells a build lays, OBJECTS.md) and linked to the guest
   payment bridge.
4. **Queue**: right after the ride, as in the HUD (placing a HasQueue ride switches to
   the queue tool, `BIN:STP-PPC:0x1007497C`). N = ⌈queue limit / 4 per cell⌉, capped at
   `QueuePaths.MaximumCells` (1,000, the bound of the original's back-of-queue walk). A HasQueue ride's limit is its
   QueueWaitTimeConstant formula (QUEUE-plan §4): Belly Bounce gets trunc(130 × 5 / 30)
   = 21, so **N = 6**. With it, the data limit, not the cells, decides how many guests
   queue (`MaximumQueueLength` = min(21, 4N) = 21). Until ORACLE-F the runtime read the
   flag the wrong way round (limit 100, N = 25; ORACLE-V F1).
   Each cell goes through `Level.BuildQueueCell`, the queue tool's per-cell code
   (recompute, `QueuePaths.CheckExtend`, `ParkEconomy.TrySpendCell(Queue)` at
   `Costs.QueueCell` = $75, `QueuePaths.TryExtend`). The blocked-cell test is
   `Level.IsQueueBlocked`'s rule over the gate's park (`QUEUE-012`). The route search
   keeps the cell behind the front in line with the ride's entrance cell and the front
   (a straight front segment, the condition W3 of the boarding bound's walk terms,
   [WALK-plan.md](reverse/WALK-plan.md) §12). This is the gate's own layout choice, like
   the site; the queue tool also allows a corner there. Belly Bounce's queue runs from
   the front (42,20) straight back to (41,20), then around the ride to the back (46,22).
   Guests join from the spine cell (47,22).
5. **Staff**: the first mechanic and the first handyman in the hiring pool are hired
   with `ParkEconomy.Hire`.

## Invariants and how they are decided

Every row is sampled every tick.

| Row | Pass rule | Notes |
| --- | --- | --- |
| `build.*` | the piece was placed and bought through the player-facing build flow, and its entrance/exit is reachable from the park entrance | a piece the runtime cannot place is a FAIL row; nothing is faked |
| `build.paths` | every segment laid completely by `ParkPathBuilder` (straight, snapped); the builder's charge equals built cells × `Costs.PathCell` and equals the balance drop; no path cell exists that neither the level (MAP InitialPath) nor the builder laid; and the park entrance reaches every placed object's entrance cell and the queue's join cell | decided after the objects and the queue are placed; cells written around the builder or laid without charging fail it |
| `build.queue` | N cells laid through `Level.BuildQueueCell`, charged N × `Costs.QueueCell`, the ride's `QueueSizeInCells` = N and its cells are the ones laid, a `JoinCell` reachable from the park entrance, `MaximumQueueLength` = min(limit, 4N); **and** after the run, guests stood on the queue cells (state `Queueing` on a queue cell) and boarded from them (`GuestSimulation.QueueWaitCompleted`) | decided after the run; the HUD click route itself is not exercised (no HUD without a GPU) |
| `time.monotonic` | guest time, economy tick and each object's script time strictly increase and are finite and ≥ 0; the park-clock seconds never decrease | the park clock moves in whole turns (every ~14.9 ticks), so it can only be non-decreasing |
| `economy.income-and-expenses` | at least one income and one expense category got non-zero ledger amounts **during the run** (setup costs not counted) | categories are `LedgerCategory` |
| `economy.ledger-consistent` | at every tick, balance = opening balance + Σ income − Σ expenses over every closed month and the open month | ledger history holds 144 months; the run closes about 10 |
| `guests.flow` | at least one guest arrived, was admitted, used the attraction, the shop and the toilet (entered `Using` there), and an admitted guest left the simulation | counts per stage plus `BoardedTotal` per attraction |
| `queues.no-stuck-queue` | **FAIL** on a derived progress violation or a wait above the traced BOUNCE boarding bound (below); **PASS** when there is none and at least one wait was judged against the bound; otherwise **UNRESOLVED** | per object: waits (from `QueueWaitCompleted`, park turns × 248 ms), queue maximum, head-not-ready streak and its bound, blocked evaluations, called age; for a traced BOUNCE ride H, R, τ_max, W_max and the judged waits; objects outside that class are listed with the reason |
| `paths.no-unreachable-goal` | every attraction's entrance, exit and queue join cell are reachable from the park entrance at the end of the run (the join cell since review GATE-V3, S3), and no guest stays in `GoingToRide` towards a join cell (the back of the queue, else the entrance cell) it cannot reach for two consecutive ticks | the existing give-up rule is `Confused` (`GuestSimulation.UpdateGoingToRide`); leaving guests without a reachable lane vanish (documented GuestSimulation simplification) and are counted |
| `rides.scripts-run` | at every tick, no placed object's script is `Faulted` or `Halted`, and each ends the run `Running` or `Waiting` | the gate opens every placed object and never closes or removes it. `RideVM` reaches `Halted` only by running past its last instruction (`RideVM.RunSlice`) or through `Stop()` (`OriginalObjectRuntime.Stop`: object removed or level torn down). Closing is `VAR_RIDECLOSED` (`OriginalObjectRuntime.Close`) and leaves the script running, so the runtime has no legitimate halted state for an open object. A completed cycle is not required (RIDES-023: BOUNCE never toggles `VAR_RUNNING`) |
| `staff.work` | wages were paid, **and each staff type is hired and keeps doing its own work**: every `RideWorn`/`RideBrokeDown` event is followed by a `RideRepaired` for that ride within the repair window, and litter left from an earlier tick shrinks at every park-hour update (see "Staff windows"); also ≥ 1 repair if one was needed and some cleaning if litter existed | removing, idling, or idling after the first job fails the row (first violation tick); a repair whose window runs past the end is pending, not a failure; staff exist only in `ParkEconomy` |
| `determinism.same-seed` | the DET node owns fixes, but a FAIL still sets exit code 1: the whole scenario runs twice in one process; both final hashes, **every minute hash** and the number of minute hashes must match, so a second run that diverges and converges again by the end fails (review GATE-V3, S4) | see below |

**Queue progress (QUEUE-plan §9a).** The gate subscribes to
`RideVisitorBridge.AdmissionChecked` (one evaluation per park turn) and reads the
bridge's counters (`HeadNotReadyStreak`, `MaximumCalledAgeTurns`). The old
`AdmissionCheck.Stalled` could never become true (review QUEUE-V, S1) and is not used.
The row **fails** on either of these progress violations, for every object with a queue,
or on the boarding bound below:

- **Head not ready beyond its bound.** There is a head, but it is not standing at
  position 0 (`!AdmissionCheck.HeadAtFront`), **whether or not the gates hold**. The
  head's walk, move-up wait and interludes do not depend on the ride's gates, so an
  evaluation with the gates not holding (for example one blocked `VAR_LETMEON`) does
  not restart the count (review GATE-V3, S2). This is counted per head: the streak
  restarts only when another guest becomes head or the head reaches position 0. The
  bound comes from the guest's own state-11/12 rules (`HeadNotReadyBound`), with N the
  ride's queue cells. Like w and w₂, the walk term needs a guest at least 15 turns old
  (W2), so a head is judged from the turn it reaches that age (review WV-2): a younger
  head's evaluations do not count yet (`headNotReadyYoungHeadsDeferred`), and a head
  without a track is judged at once. The bound is:
  - the walk. A head only walks **forward**: its queue position is a list index that
    only decreases (`GetQueuePosition`). After becoming head it makes at most two walks,
    the rest of the walk it is on and the move-up to position 0. Together they cross at
    most the N cells from the join cell to the front cell. One queue walk that crosses
    k cells and ends at a slot takes at most **k × C + w₂** updates, with **C = 9** and
    w₂ = 15 (`QueueWalkTurns`; WALK-plan §15). So the walk is at most N × 9 + 2 × 15 turns;
  - the move-up wait at gap ≤ 2: trunc(1.2 × 2) turns, then the move on the next update;
  - two interludes of 11 turns. Interludes start only when the guest stands at its
    recorded position: one may be running when the guest becomes head, and one may start
    on arrival at position 0 (the guest updates before the ride in a turn). A third needs
    another 30-turn window, and the guest is called first.
  - one evaluation for the update order.

  Belly Bounce (6 cells since ORACLE-F): 6 × 27 + 2 × 15 = 192; 192 + 3 + 22 + 1 = **218 turns** (110 while C was 9, before the original walk, 0x510100).
  One-cell queues (shop, toilet): 27 + 30 = 57; 57 + 3 + 22 + 1 = **83 turns** (65 at C = 9). Before
  WALK-I the walk was N + 4 cells at OpenTPW's own 1 cell/s × 0.7 (`[APPROX:GATE-004]`,
  now retired): 84 and 50 turns with ORACLE-F's 6 cells, 194 and 50 with the 25-cell queue
  before it, 326 before S1. With 6 cells the derived bound is looser than the retired one
  (218 against 84 turns, 83 against 50); the owner accepted that on October 10, 2026 on
  the condition that it is registered (`[APPROX:GATE-006]`, at `HeadNotReadyBound`) and
  that each head is also held to its own walk:

  **The head's own limit** (`HeadNotReadyLimit`, review WV-3). Where it is smaller, the
  walk term is replaced by the head's own remaining walk (`HeadRemainingWalkTurns`), taken
  when the gate starts judging the head; the rest (3 + 22 + 1 = 26) stays. The remaining
  walk counts the walks themselves, each k × 9 + 15 from its own start, never the guest's
  last queue cell index, which a guest stepping across a folded queue lowers while its walk
  still goes the long way round (review WV-12):
  - standing at position 0 (in an interlude, say): no walk, so the limit is **26 turns**;
  - standing at an older position: the move-up from its own cell i, i × 9 + 15;
  - walking: the whole current walk as set up (k crossings, kept by the stuck reroute),
    plus, unless it walks to position 0, one more walk to the front cell from the farthest
    cell the walk may be on (the join cell counts as N);
  - called (walking to the stand point or waiting to board): the queue bound.
- **Blocked handshake.** `VAR_LETMEON` is non-zero while nobody is called, with a queue,
  on **two consecutive** evaluations (§9a's two-update rule). Only the bridge writes a
  guest id into `VAR_LETMEON` (for the called guest), and the script consumes it or the
  bridge clears it on withdrawal, so with nobody called it has no way to clear. One such
  evaluation that recovers is not a failure. Riders and `VAR_ONRIDE` are deliberately
  not part of the rule: a shop script keeps `VAR_ONRIDE` at capacity for a few
  evaluations after serving a guest the bridge never saw (mutation `stall-one-update`).

**Boarding bound (BOUNCE rides, [BOARD-plan.md](reverse/BOARD-plan.md) §7–§8).** BOARD-R
traced the host handshake and the BOUNCE script loop. For a ride in the traced class, a
guest that joins at 0-based position p boards within

`W(p) ≤ J(p) + (p + 1)·H + (⌊p/CAP⌋ + 1)·R + 1` park turns.

J(p) is the guest's own walk from the join cell to its slot (WALK-I). BOARD-plan's
induction starts once the guest stands at its slot, so the walk comes on top.

The class: the ride's script is one of the four BOUNCE scripts BOARD-R traced. The gate
pins them by the SHA-256 of the RSE file and checks that the loop WAIT and the BOUNCE
are at the traced code words. The ride must also have RunsContinuously 1 and
DUR ≤ 30 s. Every term is read at run time:

| Term | Belly Bounce | From |
| --- | --- | --- |
| T | 248 ms | `ParkCalendar.TurnMilliseconds` |
| CAP, DUR | 5, 30 s | the script's `VAR_CAPACITY`, `VAR_DURATION` |
| P (loop period) | 1 + ⌈454 / 248⌉ = **3** | the operand of the loop WAIT, read from the script, divided by the script speed as WAIT does (0xb0658): jungle Bouncy's `InitSpeed` 60 gives 0.5 + 60/100 = 1.1, so WAIT 500 lasts 454 ms (Rides register round, October 14; at speed 1 it was 4) |
| R (slot hold) | **121** | the UNBOUNCE rule: polls P·j + 1 turns after the BOUNCE; free once deadline < now and (now − start) mod 1000 < 200. For 8 ≤ DUR ≤ 30 this is 4·DUR + 1; for DUR ≤ 7 it is 29 turns, so the gate uses the rule, not the closed form |
| H₀ | 1 + 3 + 11 + 1 + 1 + P = **20** | removal, move-up wait and move, one interlude, call, notice, next admission slice (BOARD-plan §7.1; `GuestSimulation` constants) |
| w | **20** | the original's state-13 walk from slot 0 to the stand point, from its traced steering ([WALK-plan.md](reverse/WALK-plan.md) §9, `tools/ppc-analysis/lanes/walk/walk_evidence.py`): 12 with slot 0 at the entrance edge, 20 with the slot axis reversed; the link compass is not pinned, so the larger |
| w₂ | **15** | the original's state-12 move-up from slots 1–3 to slot 0 (WALK-plan §9) |
| H | 20 + 20 + 15 = **55** | H₀ + w + w₂ |
| C | **27** per queue cell | one queue walk across k cells to a slot takes at most k × C + w₂ updates: the bounded enumeration of tp.exe's walk as OpenTPW ports it (whole-walk destination 0x510100; the worst walks are the short cornered ones around a neighbour, 3 crossings 93, 4 crossings 118; a long straight queue stays near 14 per cell), `[APPROX:GATE-006]` |
| J(p) | (N − cell of p) × 27 + 15 | the join walk from the join cell to the slot of position p: **177** turns at p = 0 for N = 6, 42 at p = 20 |

The walk terms rest on the speed floor at `0xffe38` (max speed ≥ 655 = 0.01 cell per
update; the base speed `+192` is one of 60..140, so s ≥ 0.59 after 15 updates) and on
arrival within 0.32 cell (`0xfe628`). The enumeration found 0 failures. They hold under
three conditions (WALK-plan W2–W4), which the gate checks:
- **W2:** the head is at least 15 turns old. A wait that overlaps a turn with a younger
  head is counted (`notJudgedYoungHead`), not judged.
- **W3:** the entrance cell, the front cell and the next queue cell lie in a line.
- **W4:** the ride's `EntryCellStandPos` is (0.5, 0.5).

If W3 or W4 fails, no wait of that ride is judged (`notJudgedWalkScope`, with the reason
in `walkScope`).

The gate checks the bound four ways:

1. every completed wait, against its join position;
2. at every admission evaluation, the age of every queued guest against its join
   position, so a guest that gives up later is still judged;
3. the head, against its own walk beyond H's w₂ + H + R + 1 from the turn it became head.
   That walk is its remaining walk (`HeadRemainingWalkTurns`, as for rule (a)) less the w₂
   that H already holds, 0 once called: **178 turns** for a head standing in the front
   cell, 5 × 9 + 15 + 178 = 238 for a head starting its walk from the join cell of the
   6-cell queue;
4. at the end of the run, every guest still queued.

Check 3 is §7.2's induction with J = the turn the guest became head and n = 1. The
riders then on board free their slots within R, and the boarding takes at most H once a
slot is free and the head stands at its slot. Without checks 2 and 3, the jungle queue could not fail the bound. Guests
join at p ≈ 40–49, where W(p) ≈ 2,500 turns (≈ 630 s) with H = 29 and ≈ 3,800 turns with
H = 56, but they give up after about 300 s. A ride that never released its riders therefore passed: `never-release` passed
before checks 2 and 3 were added.

Waits that overlap an excluded turn (BOARD-plan A3) are counted, not judged. A turn is
excluded when the ride is not open, broken (`VAR_BROKEN`, `VAR_BREAKSTAT`), closed
(`VAR_RIDECLOSED`), or its CAP or DUR changed. Objects outside the class (shops,
toilets, other ride types, RunsContinuously 0, DUR ≥ 31) are judged by the two progress
rules only, and the row lists them with the reason.

Reported: τ_max = CAP·H + R + 1 − (DUR + 1 s)/T = **277 turns = 68.696 s** (QUEUE-plan
§9b form) and W_max = W(Qmax − 1) = 24 + 1,782 = **1,806 turns = 447.888 s**. These figures and the baseline
below were taken at script speed 1 (P 4, H 56). Since the rides register round (October 14) the loop WAIT follows the
ride's script speed, so Belly Bounce has P 3 and H 55, and τ_max is 272 turns = 67.456 s. The τ the run implies
(`τ_measured`) is evidence, not a threshold. The walk terms w and w₂ come from WALK-plan
§12 (the original's speed floor), and the GATE-003 tag (τ = 0) is gone. The two
walk-related assumptions of review GATE-V4 (B1/B2) are retired by WALK-I:
`[APPROX:GATE-004]` (the head bound walked N + 4 cells at OpenTPW's own 1 cell/s × 0.7)
and `[APPROX:GATE-005]` (W(p) and the head check left out the walk from the join cell).
Both now use C = 9 from enumerating the steering OpenTPW runs, which is registered as
`[APPROX:GATE-006]`: that steering is the traced step except for the walk register's
WALK-001 (boost increments), WALK-002 (avoid_walls' corner branches, since ported), WALK-003
(separation's neighbour set, since ported: guests and staff on one chain per cell; the enumeration runs separation off and at its maximum
against the walk), WALK-004 (OpenTPW's routes), WALK-005/006 (walkability: the walk's
own cells instead of the cell edge flags), WALK-007 (no map-edit reroute), WALK-008
(lane walks off the grid) and WALK-009 (the progress history); the bounds hold for
OpenTPW's walk and match the original's to the extent those entries do. If no wait is
judged (no traced BOUNCE ride, every wait excluded, or W3/W4 failing), the row is
**unresolved**.

**walk-stall (WALK-plan §12.4).** The traced steering finishes:
- the walk to the stand point within w = 20 updates;
- the move-up from slots 1–3 to slot 0 within w₂ = 15 updates.

The row **fails** with `walk-stall` when, for a traced ride, a guest stays in either walk
for more consecutive turns:
- OpenTPW's `Boarding` (state 13);
- `MovingUpQueue` (state 12) towards position 0 from inside the front cell.

A stall outside W2–W4 is counted (`walkStallsOutsideScope`), not judged. In the baseline,
OpenTPW's walks, now the traced steering, last at most 8 turns (stand point) and 3 turns
(move-up); before WALK-I they took 0 and 7 turns (the old stand point coincided with
slot 0).

Waits are tracked with `Guest.IsInQueue`, which includes walking up (state 12). A
guest that leaves the queue without boarding is counted only on an in-queue →
not-in-queue transition that is not a boarding. Before this change, every move-up
counted as a completed wait plus a departure.

**Staff windows.** Both come from `ParkEconomy`'s staff timing. Every turn runs at
least one park-hour update, because a turn is 3,750 park-clock seconds and an hour is
3,600.

- **Repairs.** A wear or breakdown event is raised at a day end. The next turn's hour
  update dispatches a free mechanic, and a job takes `WorkDuration` hours (at least 1).
  With one job per ride ahead of it, the repair must land within
  1 + rides × max `WorkDuration` turns of the hired mechanics' grades. For the gate's
  grade-1 mechanic: 1 + 1 × 60 = **61 turns** (~15 s). An event whose window ends after
  the run is reported as pending.
- **Litter.** Litter lies on cells (ORACLE.md 7.15). At each turn's dispatch a free handyman
  claims the first unclaimed littered cell and sweeps it for `WorkDuration[grade]` turns. Litter
  left unclaimed while a handyman is free on two turn boundaries in a row is a violation; drops
  and sweeps are counted per change through `ParkLitter.Changed`.

**Hashes.** `GuestSimulation.ComputeStateHash` (raw guest hash) includes guest
attraction ids. Since the DET node these are allocated per park by its
`RideScriptWorld`, so a second scenario in the same process gets the same ids (4 5 6
in both runs). The gate hash still replaces attraction ids by placement index. It also covers
the economy (tick, balance, RNG state, ledger, litter, staff) and every object
script's variables and clock. A hash is also recorded every simulated minute, to
locate the first divergent minute.

## Divergences from the player's build flow

What the gate still does differently from `Level` and the HUD build flow (review
M3-GATE-V, divergences D1 to D6, after the fixes above):

| # | Divergence | Effect on the rows |
| --- | --- | --- |
| D2' | The object build rule also refuses the gate's own path cells (`IsBlocked`). `ParkObjects` does not know them because no in-game path exists. | Stricter only: it can refuse a site the game allows, never the reverse. |
| D7 | The queue's blocked-cell test is `Level.IsQueueBlocked`'s rule written over the gate's park (terrain check, gate occupancy); the queue cells are clicked by the gate's route search, not by a cursor. | None for the rows: every cell goes through `Level.BuildQueueCell` like a click. |
| D6 | No `Entity.Update`, `OriginalObject.UpdateTransforms` or frame-paced `FixedStepClock` (catch-up cap, `SimulationTimeScale`). Objects are `OriginalObjectRuntime`s, not `OriginalObject`s, because `ParkObjects` creates GPU entities. | Render side only. A shared CPU-only simulation core for `Level` and the gate (review S5) would remove the copied tick order and object list. |
| — | The clicked cell is picked by the gate's site search, not by a cursor ray (`Level.TryGetGridCell`). | None for the rows: every clicked cell goes through `Level.GetCentredAnchor` like a click. |

Fixed since the review: D1 (fixed items registered after `AttachGuests`, linked and
open-state synced), D2 (`ParkObjects.Check` itself instead of a copied terrain rule),
D3 (`Level.ResolveVisitorCells`' nearest-path snap instead of a charged 7-cell
connector) and D5 (`Level.RegisterWithGuests` applies `Runtime.IsAttraction`).

## Baseline (jungle, seed `Level.GuestSeed` = 6075451861746676596, 30 minutes)

Run on October 11, 2026 against the local original data, on main (c6c19ea) with WALK-I,
ORACLE-F and WALK-F, in two separate processes: the same table and exit code **0** both times, and the JSON reports
are identical apart from `wallSeconds`.

This baseline predates the traced arrival rule (docs/GUESTS.md, "Arrivals"). With it, guests come in groups sized by
the park about every 2.5 minutes, and the same 30-minute run gives exit code **0** with 105 guests arrived and admitted,
100 using the ride, 98 the shop and 94 the toilet, and 88 leaving; the guest-driven figures below (admissions, waits,
takings, litter) are therefore those of the older, busier park.

| Row | Verdict | Evidence |
| --- | --- | --- |
| build.entrance | PASS | 10 InitialPath cells, lanes A/B valid, Bus/Gates/Lights, fee $20 |
| build.paths | PASS | `ParkPathBuilder.BuildSegment` (47,21)→(47,35): 14 built, 1 existing (the walkway end), $280 charged by the builder and from the balance, 0 stray path cells; reached Belly Bounce (47,26), Drinks Shop (47,22), Small Toilet (47,24), queue join (47,28) |
| build.attraction | PASS | Belly Bounce, clicked (49,23) → anchor (50,25) rot 180, queue front (49,26), entrance cell (47,26), exit (48,21) (snapped from (49,21)), $500, capacity 5 |
| build.queue | PASS | 6 of 6 cells (ceil(queue limit 21 / 4)) through `Level.BuildQueueCell`, charged $450 (6 × $75), front (49,26) → back (48,28), join cell (47,28) reachable, maximum queue 21; 287 guests stood on the queue cells, 267 boarded from them |
| build.shop | PASS | Drinks Shop at (46,22) rot 270, $650, $30 per drink |
| build.toilet | PASS | Small Toilet at (46,24) rot 270, $100 |
| build.staff | PASS | mechanic grade 1 ($150/month), handyman grade 3 ($80/month); no staff agents in the world |
| time.monotonic | PASS | 0 violations; guest time 1800.000094 s (float accumulation), economy tick 108,000 |
| economy.income-and-expenses | PASS | setup $1,980; income: gate $35,680, shops $18,630; expenses: staff $2,300, other $12,420 |
| economy.ledger-consistent | PASS | 0 violations in 108,000 samples, 10 months closed |
| guests.flow | PASS | 1,800 arrived, 1,784 admitted, 0 turned back, 264 used the ride, 498 the shop, 147 the toilet, 1,642 left, 125 in the park at the end |
| queues.no-stuck-queue | PASS | 0 violations; 287 waits judged (267 completed, 20 still queued at the end), 0 excluded, 0 outside W2–W4. Belly Bounce (jungle Bouncy.RSE, 6 cells): P 4, H 56 (21 + w 20 + w₂ 15), R 121, C 9, J 69 at p = 0 and 24 at p = 20; τ_max 277 turns = 68.696 s, W_max 1,806 turns = 447.888 s; closest wait 216 turns under its W(p); head to boarding ≤ 96 turns (178 for a head standing in the front cell; head walks ≤ 54); walks ≤ 7 / 3 of 20 / 15 turns, 0 walk-stalls; queue max 21/21, 267 waits, max 154.752 s, mean 130.79 s; head not ready ≤ 25 of 110 turns, own-walk limit for 285 heads, closest 15 turns under its limit, 0 young heads deferred; 0 blocked evaluations; called age ≤ 10 turns; τ measured 2.232 s. Shop/toilet (not traced BOUNCE loops: progress rules only): head not ready ≤ 15 / 12 of 65 turns (gates held or not), own-walk limits for 631 / 169 heads, closest 15 turns under, 0 young heads deferred, called age ≤ 10 / 12 turns. 0 guests left a queue without boarding |
| paths.no-unreachable-goal | PASS | all targets reachable; 0 stuck guests; 0 Confused give-ups; 0 lane-less ejections |
| rides.scripts-run | PASS | 0 faults, 0 halted ticks (Belly Bounce, Small Toilet waiting; Drinks Shop, Bus, Gates, Lights running) |
| staff.work | PASS | 10 wage payments ($2,300); mechanic: 4 repairs for 4 worn/broken events, longest 14.4 s within the 61-turn window, 0 pending; handyman: 310.5 litter items dropped, 310.5 cleaned, 0 missed hour updates |
| determinism.same-seed | PASS | raw guest hash 29788288CB1437F4 in both in-process runs; attraction ids 4 5 6 in both; gate hash A07A2871552E22E8; 30 minute hashes in each run, no divergent minute |

**WALK-I and ORACLE-F merged** (same seed): every verdict is the same as on either branch. ORACLE-F
(the oracle fixes F1–F5) changes the layout: Belly Bounce's queue limit is 21, so the gate lays 6 queue
cells ($450) instead of 25, and UNBOUNCE releases with the strict deadline and the 200 ms window. WALK-I
walks guests with the traced steering at the traced per-guest speed (0.48–1.13 cell/s at s = 0.6–1.4,
from s = 0 at spawn), and a called guest walks into the ride's entrance cell instead of standing at the
front cell's edge (QUEUE-007 closed). Against ORACLE-F alone (280 completed waits, mean 126.0 s, head not
ready ≤ 26 of 84 turns, W_max 1,782 turns, gate hash `8ADCD47C727CC3F2` on 33f8584): 268 completed waits,
mean 130.3 s, head not ready ≤ 25 of 110 turns, W_max 1,806 turns (J(20) = 24 on top). Against WALK-I
alone (25 cells): queue max 48/100 → 21/21, mean wait 215.4 → 130.3 s, head bound 281 → 110 turns. Gate
hash `84906295B3DAA515`.

**WALK-F against that merge** (b10710e, same seed): every verdict is the same. Guests now push each
other apart with the traced separation force, avoid_walls stops at the first pushing edge and the
toilet term follows the needs phase: 268 → 267 completed waits at the ride (mean 130.3 → 130.8 s),
shop boardings 626 → 621, admissions 1,783 → 1,784, closest wait 215 → 216 turns under its W(p).
Rule (a) is stricter: 285 / 631 / 169 heads were held to their own walk (+ 26) instead of 110 / 65 /
65 turns, the closest 15 turns under that limit; the queue bound itself is unchanged. Head walks
beyond H ≤ 69 → 54 turns (the walk as set up, not the queue cell index). Gate hash
`84906295B3DAA515` → `A07A2871552E22E8`.

Totals: 16 pass, 0 fail, 0 unresolved; **exit code 0**. The evaluator accepts M3 under
the approximations listed below. With `--no-determinism`: 15 pass, 0 fail, 1 unresolved,
exit 2.

### Which gameplay area the open rows point at

- **paths: `build.paths` — resolved.** PATH-I's builder lays and charges the paths.
- **paths: `build.queue` — resolved.** The queue is laid through the queue tool's code
  (QUEUE-I), charged, and walked by guests.
- **rides / guests: `queues.no-stuck-queue` — resolved** by BOARD-R's traced boarding
  bound (call to boarding and τ, BOARD-plan §7) with WALK-R's traced walk terms
  (WALK-plan §9, §12). These ride types fail it:
  - a ride that stops taking guests (`block-boarding`);
  - a head that is never ready (`never-called-head`);
  - slots held far beyond R (`hold-slots-much-longer`, `never-release`);
  - a head held beyond H (`head-delayed-170-turns`);
  - a move-up held beyond w₂ (`hold-move-up`, walk-stall).
- **determinism: `determinism.same-seed` — resolved** by the DET node.

Passing rows that rest on approximations rather than original rules:
- arrival rate and needs (GUESTS.md approximation table);
- wear, repair and cleaning (`ECON-021` to `ECON-024`), which also set the staff windows;
- opening the park at the start: a new park starts closed (tp.exe `0x51562d`), and the gate run opens it as the player's door button would;
- the entrance/exit snap (the gate does not lay the access cells a build lays, formerly `RIDES-028`);
- the build rule (`RIDES-018`);
- the queue build rules (`QUEUE-012` to `QUEUE-014`);
- the boarding bound's assumptions: the nominal 248 ms clock and per-turn cadence
  (BOARD-plan A1, A2, WALK-plan W1). Its walk terms are traced (WALK-plan §9) under
  W2–W4, which the gate checks. W3 holds because the gate lays a straight front segment.

## Mutation evidence

`tools/ppc-analysis/lanes/review/test_m3_gate_v1.py` (review M3-GATE-V) injects
faults into a copy of the gate and checks that the right row flips. Static tests
read the reviewed commit; mutation tests build the commit under test
(`OPENTPW_M3_SUBJECT`, default `HEAD`):

```sh
OPENTPW_M3_MUTATE=1 OPENTPW_GAME_PATH=/path/to/theme-park-world \
  python3 -m unittest -v tools/ppc-analysis/lanes/review/test_m3_gate_v1.py
```

Every mutation exits non-zero. `no-mechanic`, `no-handyman`, `idle-mechanic` and
`idle-handyman` fail `staff.work`; `stall-ride` and `stall-ride-from-start` fail
`rides.scripts-run`. The v1/v2 files pin the rows as they were at their reviews
(for example `block-boarding` leaving the queue row unresolved). Their baseline
expectations predate DET and GATE-UPD.

`tools/ppc-analysis/lanes/review/test_m3_gate_v3.py` (GATE-UPD) adds hooks on top of
v1/v2 and pins the rows as they were at its review. The tick numbers and bounds below were
measured before WALK-I (head-not-ready bound 194, head check 178 for every head, 25 queue
cells). WALK-I, ORACLE-F and WALK-F changed the walk, the layout and those bounds; WALK-F
reran every suite (see after the table):

| Mutation | Row | Result |
| --- | --- | --- |
| baseline | all | every row passes (the queue row on the boarding bound, 331 waits judged); exit 0 |
| `paths-direct-write` (the old stand-in: cells charged and written around the builder) | `build.paths` | **FAIL** (0 built, 14 stray path cells), the only failing row |
| `paths-no-charge` (`ParkPathBuilder` without the economy) | `build.paths` | **FAIL** (charged $0 for 14 cells) |
| `block-boarding` (v1: `VAR_LETMEON` = −1 from tick 18,000) | `queues.no-stuck-queue` | **FAIL** (blocked handshake), the only new failing row |
| `never-called-head` (the head is held in an interlude from tick 18,000) | `queues.no-stuck-queue` | **FAIL** at tick 20,639: the head is not boarded within 178 turns of becoming head; rule (a) also fails it (head not ready beyond 194 turns) |
| `stall-one-update` (shop `VAR_LETMEON` = −1 for one evaluation) | `queues.no-stuck-queue` | not failed (1 blocked evaluation); PASS since the boarding bound |
| `idle-mechanic-after-first-repair` (v2) | `staff.work` | **FAIL** (wear not repaired within 61 turns) |
| `idle-handyman-after-first-clean` (v2) | `staff.work` | **FAIL** (litter not reduced at an hour update) |
| `queue-bypass-economy` (`Level.BuildQueueCell` without the economy) | `build.queue` | **FAIL** (charged $0 for 25 cells) |
| `never-ready-head-blips` (GATE-V3: the held head, plus one blocked evaluation every 300 turns) | `queues.no-stuck-queue` | **FAIL**: rule (a) (S2; was UNRESOLVED, streak ≤ 299 of 326); the boarding bound first, at 20,639 |
| `never-ready-head-blips-150` (the same, every 150 turns: below the 194-turn bound) | `queues.no-stuck-queue` | **FAIL**: rule (a) (S2; was UNRESOLVED, streak ≤ 149); the boarding bound first, at 20,639 |
| `head-held-250-turns` (the head held in an interlude for 250 turns from tick 18,000, then released) | `queues.no-stuck-queue` | **FAIL**: rule (a), 261 > 194 turns (S1; was UNRESOLVED, 259 ≤ 326); the boarding bound first, at 20,639 |
| `hold-slots-much-longer` (every new BOUNCE slot held 120 s past its deadline, about 5R) | `queues.no-stuck-queue` | **FAIL** at tick 20,639 (head not boarded within 178 turns); rules (a) and (b) silent |
| `never-release` (BOUNCE slots never released from tick 18,000) | `queues.no-stuck-queue` | **FAIL** at tick 20,639, as above (it passed with only the join-anchored checks) |
| `head-delayed-170-turns` (each new head held 170 turns before it can be called) | `queues.no-stuck-queue` | **FAIL** at tick 20,639 (32 bound violations, head to boarding 187 > 178); head not ready ≤ 185 of 194, so rule (a) is silent |
| `hold-move-up` (the first guest moving up to slot 0 inside the front cell from tick 18,000 is held in place for 30 turns) | `queues.no-stuck-queue` | **FAIL** at tick 18,228: `walk-stall`, move-up for 16 > w₂ 15 turns; every other queue rule silent (passed on 5ce5cc4) |
| `hold-slots-longer` (+30 s, about 2R), `head-delayed-60-turns` | `queues.no-stuck-queue` | PASS (pinned): the slowdowns stay within the loose bound (head to boarding ≤ 134 of 178) |
| `stall-ride` (v1) | `queues.no-stuck-queue` | not failed: after the stop the ride is not open (excluded) and nobody queues; the gate still exits 1 through `rides.scripts-run` and `time.monotonic` |
| `cut-spine-start` (the segment's first cell (47,21) cut at tick 1,000: Belly Bounce's entrance and exit stay reachable, its join cell does not) | `paths.no-unreachable-goal` | **FAIL**, listing "Belly Bounce, Drinks Shop, Small Toilet" (S3; on 64616cd "Drinks Shop, Small Toilet") |
| `diverge-second-run` (v1: the second run diverges at tick 2,000 and converges by the end) | `determinism.same-seed` | **FAIL** at tick 3,600, minute 1 (S4; was PASS) |

**WALK-F rerun** (v1–v4, `OPENTPW_M3_MUTATE=1`, on the walk branch with ORACLE-F merged). v3 and
v4 did not build after WALK-I (review WALK-V, WV-1): their hooks wrote the read-only `Guest.X/Y`,
and after ORACLE-F they cast the BOUNCE slots to the old (guest, deadline) pairs. Both are ported
(`Navigation.X/Y` with the velocity zeroed; the slots' deadline set with `with { Until = … }`).
Every mutation that failed a row at c6c19ea still fails it. The pins that moved:
- the baseline's W_max includes J(Qmax − 1) = 24 (WALK-I; the pin predated it);
- the head bound was 110 / 65 / 65 turns at that time (218 / 83 / 83 with C = 27 since the original walk) (`[APPROX:GATE-006]`), and rule (a) holds each head
  to its own remaining walk + 26: `head-held-250-turns`, `head-delayed-170-turns` and
  `head-delayed-60-turns` now fail rule (a) itself ("bound 26 from its own walk"); at WALK-I
  the first passed rule (a) (259 ≤ 281) and the last passed the row;
- `hold-slots-longer` fails the head's wait bound by one turn (179 > 178) since WALK-I;
- `cut-exit-transient` fails `paths.no-unreachable-goal` (tick 36,001, a guest standing on
  the cut exit cell (48,21)): ORACLE-F's layout puts the exit on a walked path cell (also on
  the merge before WALK-F);
- `diverge-second-run` nudges the oldest guest's hunger by 0.001 instead of its happiness by
  1: with separation a changed decision spreads to other guests and the run never converges
  again, which the test needs.

Static checks in v3:
- `Level.BuildQueueCell` keeps its old steps in order;
- the gate lays the queue only through that code and the paths only through `ParkPathBuilder` (no `SetPath`/`TryBuyCells` in the gate);
- the queue row reads the bridge counters and never `Stalled`.

```sh
OPENTPW_M3_MUTATE=1 OPENTPW_GAME_PATH=/path/to/theme-park-world \
  python3 -m unittest -v tools/ppc-analysis/lanes/review/test_m3_gate_v3.py
```

## Not measured

- Original semantics: no original trace or comparison exists. Every PASS is a
  stability statement about OpenTPW.
- Rendering, input, HUD, the `Level` class itself, frame pacing and catch-up
  (`FixedStepClock.MaximumCatchUpTicks`), and speed changes. The gate calls the
  fixed tick directly.
- Staff as world agents (walking, picking up, strikes): they do not exist.
- Coasters, karts, tours and other ride-type controllers (TOUR/BUMP/COAST), ride
  breakdown effects on guests, sideshows, litter on paths, vomit, pranksters,
  thought bubbles.
- Loans, research progress, challenges and golden tickets are running but not
  checked (golden tickets were won during the baseline).
- Save/load in the middle of a run, other themes (`--level` runs them, but the
  scripted layout has only been checked on jungle) and Instant Action balance.
- Year-end work (`StartYear`, the yearly ledger roll-up): a 30-minute run closes
  10 months and never crosses a park year.
- Long-run limits beyond 30 minutes (for example the 144-month ledger history or the
  1,000-guest cap).
