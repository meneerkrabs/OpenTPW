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
dotnet source/OpenTPW/bin/Debug/net10.0/OpenTPW.dll --game-path '/path/to/Theme Park World' \
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
seconds per turn; sampling into 60 Hz ticks is `APPROX:ECON-001`) turns
108,000 ticks into 7,258 park turns, about 315 park-clock days (Year 1, month 11,
day 11). Thirty park-clock minutes would be less than one turn (about 15 ticks),
which cannot exercise anything. Reading "in-game minutes" as simulation minutes is
**[APPROX:GATE-001]**.

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
`Level.GetCentredAnchor`, `ParkEconomy.TryBuild` and `Level.RegisterWithGuests` /
`Level.ResolveVisitorCells` (the code `Level.RegisterObjectWithGuests` runs). The
remaining copies are listed under "Divergences from the player's build flow". If
`Level` changes, those copies can drift from it.

1. **Entrance**: MAP `InitialPath` cells (10), the `Standard.sam` arrival lanes A/B
   and the Gates, Lights and Bus fixed items (`ParkObjects.AddDefaultFixedItems`'
   set). As in `Level.ConnectObjectsToEconomy`, they are registered with the economy
   after `AttachGuests`, linked to the guest payment bridge and get their open state
   mirrored every tick.
2. **Paths**: 14 cells straight on from the end of the entrance walkway, in the
   direction of lane A's last step, each charged `Costs.PathCell` ($20) through
   `ParkEconomy.TryBuyCells`. **No in-game path builder exists**: there is no HUD path
   tool and no player-facing path API, and nothing outside the gate calls
   `GuestPathGrid.SetPath`. The gate lays the cells by editing `GuestPathGrid`
   directly (**[APPROX:GATE-002]**) only as a scripted substitute so that the
   simulation rows can be measured. `build.paths` therefore **fails** until a path
   builder exists; the gate must then build through it.
3. **Objects**: the lowest-Info.Id **researched, buyable** catalog entry per role:
   the first ride with `Info.HasQueue` (Belly Bounce, 1100), the first shop that
   satisfies a need (Drinks Shop, 1203) and the first toilet (`ProvidesRelief`:
   Small Toilet, 1402). Each object goes through the HUD build flow's rules
   (`Level.PlaceObject`): the site search tries clicked cells near the path in all
   four rotations, turns each into an anchor with `Level.GetCentredAnchor` and
   keeps it only when `ParkObjects.Check` allows it. The gate's own choice is the
   site: the entrance must open onto a built path cell (nearest the park entrance
   first) and the exit with the shortest snap wins. The object is bought with
   `ParkEconomy.TryBuild`, registered with guests by `Level.RegisterWithGuests`
   (attractions only; a non-path outside cell snaps to the nearest path cell,
   `RIDES-028`) and linked to the guest payment bridge. No connector path is
   built: the Belly Bounce exit (42,22) snaps to the path like it does in the game.
4. **Queue**: not placeable (see results).
5. **Staff**: the first mechanic and the first handyman in the hiring pool are hired
   with `ParkEconomy.Hire`.

## Invariants and how they are decided

Every row is sampled every tick.

| Row | Pass rule | Notes |
| --- | --- | --- |
| `build.*` | the piece was placed and bought through the player-facing build flow, and its entrance/exit is reachable from the park entrance | a piece the runtime cannot place is a FAIL row; nothing is faked |
| `build.paths`, `build.queue` | **always FAIL today** | no player-facing path or queue-path builder exists; the gate's scripted spine (GATE-002) does not count as building paths |
| `time.monotonic` | guest time, economy tick and each object's script time strictly increase and are finite and ≥ 0; the park-clock seconds never decrease | the park clock moves in whole turns (every ~14.9 ticks), so it can only be non-decreasing |
| `economy.income-and-expenses` | at least one income and one expense category got non-zero ledger amounts **during the run** (setup costs not counted) | categories are `LedgerCategory` |
| `economy.ledger-consistent` | at every tick, balance = opening balance + Σ income − Σ expenses over every closed month and the open month | ledger history holds 144 months; the run closes about 10 |
| `guests.flow` | at least one guest arrived, was admitted, used the attraction, the shop and the toilet (entered `Using` there), and an admitted guest left the simulation | counts per stage plus `BoardedTotal` per attraction |
| `queues.no-stuck-queue` | **always unresolved** (see below) | raw max wait, mean, still-queued, longest stall per attraction |
| `paths.no-unreachable-goal` | every attraction's entrance and exit are reachable from the park entrance, and no guest stays in `GoingToRide` towards an entrance it cannot reach for two consecutive ticks | the existing give-up rule is `Confused` (`GuestSimulation.UpdateGoingToRide`); leaving guests without a reachable lane vanish (documented GuestSimulation simplification) and are counted |
| `rides.scripts-run` | at every tick, no placed object's script is `Faulted` or `Halted`, and each ends the run `Running` or `Waiting` | the gate opens every placed object and never closes or removes it. `RideVM` reaches `Halted` only by running past its last instruction (`RideVM.RunSlice`) or through `Stop()` (`OriginalObjectRuntime.Stop`: object removed or level torn down). Closing is `VAR_RIDECLOSED` (`OriginalObjectRuntime.Close`) and leaves the script running, so the runtime has no legitimate halted state for an open object. A completed cycle is not required (RIDES-023: BOUNCE never toggles `VAR_RUNNING`) |
| `staff.work` | wages were paid, **and each staff type is hired and does its own work**: the mechanic made ≥ 1 repair if a ride needed one (a `RideWorn` or `RideBrokeDown` event, the jobs `ParkEconomy.DispatchMechanics` takes), and the handyman cleaned litter if litter existed (peak > 0 or any litter dropped) | removing or idling either staff type fails the row; staff exist only in `ParkEconomy` |
| `determinism.same-seed` | the DET node owns fixes, but a FAIL still sets exit code 1: the whole scenario runs twice in one process and both hashes are compared | see below |

**Queue bound.** No original rule for queue or ride-cycle time has been traced.
The values the runtime has are all approximations: queue length 4 × capacity
(`GuestSettings.QueueLengthPerCapacity`, GUESTS.md), `VAR_DURATION` written raw
from `Upgrades[0].InitDuration` (`RIDES-016`), and BOUNCE holding a guest
`VAR_DURATION` seconds (an inferred unit). A pass threshold built from them would be
invented, so the row is **unresolved** and reports raw values. As a reference only,
not a bound, it also reports `(ceil(max queue / capacity) + 1) × InitDuration`. For
Belly Bounce that is (20 / 5 + 1) × 30 = 150 s. A queue that is non-empty but has no
boarding is measured as the "longest stall".

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
| D4 | Paths are laid by editing `GuestPathGrid` (GATE-002). | `build.paths` FAILS for it. The other rows are measured on these scripted paths. |
| D6 | No `Entity.Update`, `OriginalObject.UpdateTransforms` or frame-paced `FixedStepClock` (catch-up cap, `SimulationTimeScale`). Objects are `OriginalObjectRuntime`s, not `OriginalObject`s, because `ParkObjects` creates GPU entities. | Render side only. A shared CPU-only simulation core for `Level` and the gate (review S5) would remove the copied tick order and object list. |
| — | The clicked cell is picked by the gate's site search, not by a cursor ray (`Level.TryGetGridCell`). | None for the rows: every clicked cell goes through `Level.GetCentredAnchor` like a click. |

Fixed since the review: D1 (fixed items registered after `AttachGuests`, linked and
open-state synced), D2 (`ParkObjects.Check` itself instead of a copied terrain rule),
D3 (`Level.ResolveVisitorCells`' RIDES-028 snap instead of a charged 7-cell
connector) and D5 (`Level.RegisterWithGuests` applies `Runtime.IsAttraction`).

## Baseline (jungle, seed `Level.GuestSeed` = 6075451861746676596, 30 minutes)

Run on October 10, 2026 against the local original data, in two separate processes:
the same table and exit code **1** both times, and the JSON reports are identical
apart from `wallSeconds`.

| Row | Verdict | Evidence |
| --- | --- | --- |
| build.entrance | PASS | 10 InitialPath cells, lanes A/B valid, Bus/Gates/Lights, fee $20 |
| build.paths | **FAIL** | no in-game path builder. Scripted substitute: 14 cells (47,21)→(47,35), $280 |
| build.attraction | PASS | Belly Bounce, clicked (44,22) → anchor (46,21) rot 270, entrance (47,22), exit (42,22) snapped to (47,22), $500, capacity 5 |
| build.shop | PASS | Drinks Shop at (48,23) rot 90, $650, $30 per drink |
| build.toilet | PASS | Small Toilet at (48,24) rot 90, $100 |
| build.queue | **FAIL** | no placeable queue path: guests never walk queue cells (`GuestPathGrid`), the queue is virtual (slots at the entrance cell, max 20) |
| build.staff | PASS | mechanic grade 1 ($150/month), handyman grade 2 ($60/month); no staff agents in the world |
| time.monotonic | PASS | 0 violations; guest time 1800.000094 s (float accumulation), economy tick 108,000 |
| economy.income-and-expenses | PASS | setup $1,530; income: gate $35,760, shops $51,870; expenses: staff $2,100, other $34,580 |
| economy.ledger-consistent | PASS | 0 violations in 108,000 samples, 10 months closed |
| guests.flow | PASS | 1,800 arrived, 1,788 admitted, 0 turned back, 286 used the ride, 955 the shop, 158 the toilet, 1,672 left, 105 in the park at the end |
| queues.no-stuck-queue | UNRESOLVED | Belly Bounce: max wait 149.95 s (reference 150 s), mean wait (all queues) 23.07 s, queue full 20/20, longest stall 28 s; still queued at the end ≤ 126.8 s; shop and toilet queues full (4/4) |
| paths.no-unreachable-goal | PASS | all targets reachable; 0 stuck guests; 0 Confused give-ups; 0 lane-less ejections |
| rides.scripts-run | PASS | 0 faults, 0 halted ticks (Belly Bounce, Drinks Shop, Small Toilet waiting; Bus, Gates, Lights running) |
| staff.work | PASS | 10 wage payments ($2,100); mechanic: 17 repairs for 17 worn/broken events; handyman: 810.5 litter items dropped, 810.5 cleaned; Belly Bounce state of repair 55 at the end |
| determinism.same-seed | PASS | raw guest hash 00E874A92846742F in both in-process runs; attraction ids 4 5 6 in both; gate hash C4D8D348D84FF042 |

Totals: 13 pass, 2 fail, 1 unresolved; **exit code 1**. With `--no-determinism`:
12 pass, 2 fail, 2 unresolved, exit 1.

### Which gameplay area the failures point at

- **paths: `build.paths`.** There is no player-facing path builder. Needed: a path
  tool in the HUD build flow (with the original path rules and costs; GATE-002 lists
  the missing evidence). The gate must then build through it.
- **paths / guests: `build.queue`.** There is no queue-path building. Guests use
  a virtual queue at the entrance cell and never walk queue cells. Needed: a
  queue-path builder, walkable queue cells in `GuestPathGrid`, and queue geometry
  from `Info.Shape` (GUESTS.md "walking real queue cells").
- **determinism (DET node): `determinism.same-seed` — resolved.** The static
  `OriginalObjectRuntime.nextAttractionId` made raw guest hashes depend on earlier
  objects in the process; the DET node allocates attraction ids per park and derives
  every simulation stream from one world seed (docs/DETERMINISM.md), so the row now
  passes.
- **rides / guests: `queues.no-stuck-queue` (unresolved).** All three queues
  fill to their approximated limit. Belly Bounce guests wait up to five full
  30-unit cycles. Deciding this needs an original queue-time or cycle-time
  rule, the `InitDuration` unit (`RIDES-016`) and the original queue length. Even
  with every other row passing, this row alone keeps the exit code at 2: a ride that
  stops taking guests while its script runs (review mutation `block-boarding`) is
  not accepted.

Passing rows that rest on approximations rather than original rules: arrival
rate and needs (GUESTS.md approximation table), wear/repair/cleaning (`ECON-021` to
`ECON-024`), opening the park on load (`ECON-031`), the entrance/exit snap
(`RIDES-028`) and the build rule (`RIDES-018`).

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
`rides.scripts-run`; `block-boarding` adds no failing row (the queue row stays
unresolved) but cannot exit 0.

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
