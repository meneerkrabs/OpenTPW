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
`counts`). The exit code is 1 when any row fails, otherwise 0. The default seed
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

Level `jungle`, loaded with `OriginalPark.Load( level, includeEasymodePark: false )`
and a Full Simulation economy (`ParkEconomyRuntime.ForOriginalLevel`, standard
balance: $50,000, $20 entrance fee). The objects are `OriginalObjectRuntime`s:
the CPU half of `OriginalObject`, running its original script with no model upload.
`Level` cannot be used because it creates the terrain, sprite and HUD GPU
resources. The gate therefore repeats `Level`'s composition: `SetupObjects` /
`ConnectObjectsToGuests` / `ConnectObjectsToEconomy` / the `Update` tick order.
If `Level` changes, this copy can drift from it.

1. **Entrance**: MAP `InitialPath` cells (10), the `Standard.sam` arrival lanes A/B
   and the Gates, Lights and Bus fixed items (`ParkObjects.AddDefaultFixedItems`'
   set), registered with the economy like `Level` does.
2. **Paths**: 14 cells straight on from the end of the entrance walkway, in the
   direction of lane A's last step, each charged `Costs.PathCell` ($20) through
   `ParkEconomy.TryBuyCells`. No path-building tool exists in the runtime: the gate
   edits `GuestPathGrid` directly (**[APPROX:GATE-002]**).
3. **Objects**: the lowest-Info.Id **researched, buyable** catalog entry per role:
   the first ride with `Info.HasQueue` (Belly Bounce, 1100), the first shop that
   satisfies a need (Drinks Shop, 1203) and the first toilet (`ProvidesRelief`:
   Small Toilet, 1402). The site search tries all four rotations near the path.
   The footprint must pass `OriginalParkGrid` terrain rules and must not overlap
   paths or other objects. The entrance must open onto a built path cell. If the
   exit opens elsewhere, a connector path of at most 12 cells (shortest
   buildable route) is built and charged. Objects are bought with
   `ParkEconomy.TryBuild` and linked to the guest payment bridge.
4. **Queue**: not placeable (see results).
5. **Staff**: the first mechanic and the first handyman in the hiring pool are hired
   with `ParkEconomy.Hire`.

## Invariants and how they are decided

Every row is sampled every tick.

| Row | Pass rule | Notes |
| --- | --- | --- |
| `build.*` | the piece was placed and bought, and its entrance/exit is reachable from the park entrance | a piece the runtime cannot place is a FAIL row; nothing is faked |
| `time.monotonic` | guest time, economy tick and each object's script time strictly increase and are finite and ≥ 0; the park-clock seconds never decrease | the park clock moves in whole turns (every ~14.9 ticks), so it can only be non-decreasing |
| `economy.income-and-expenses` | at least one income and one expense category got non-zero ledger amounts **during the run** (setup costs not counted) | categories are `LedgerCategory` |
| `economy.ledger-consistent` | at every tick, balance = opening balance + Σ income − Σ expenses over every closed month and the open month | ledger history holds 144 months; the run closes about 10 |
| `guests.flow` | at least one guest arrived, was admitted, used the attraction, the shop and the toilet (entered `Using` there), and an admitted guest left the simulation | counts per stage plus `BoardedTotal` per attraction |
| `queues.no-stuck-queue` | **always unresolved** (see below) | raw max wait, mean, still-queued, longest stall per attraction |
| `paths.no-unreachable-goal` | every attraction's entrance and exit are reachable from the park entrance, and no guest stays in `GoingToRide` towards an entrance it cannot reach for two consecutive ticks | the existing give-up rule is `Confused` (`GuestSimulation.UpdateGoingToRide`); leaving guests without a reachable lane vanish (documented GuestSimulation simplification) and are counted |
| `rides.scripts-run` | no object script faults | |
| `staff.work` | wages were paid and staff did work (≥ 1 repair or litter cleaned) | staff exist only in `ParkEconomy` |
| `determinism.same-seed` | report only (the DET node owns fixes): the whole scenario runs twice in one process and both hashes are compared | see below |

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
attraction ids. These come from a process-wide counter in `OriginalObjectRuntime`,
so a second scenario in the same process gets other ids (run 1: 4 5 6; run 2: 10
11 12). The gate hash replaces attraction ids by placement index. It also covers
the economy (tick, balance, RNG state, ledger, litter, staff) and every object
script's variables and clock. A hash is also recorded every simulated minute, to
locate the first divergent minute.

## Baseline (jungle, seed `Level.GuestSeed` = 6075451861746676596, 30 minutes)

Run on October 10, 2026 against the local original data. The result is
reproducible: two separate processes gave identical hashes.

| Row | Verdict | Evidence |
| --- | --- | --- |
| build.entrance | PASS | 10 InitialPath cells, lanes A/B valid, Bus/Gates/Lights, fee $20 |
| build.paths | PASS | 14 cells (47,21)→(47,35), $280 |
| build.attraction | PASS | Belly Bounce at (46,21) rot 270, entrance (47,22), exit (42,22) via a 7-cell connector, $500, capacity 5 |
| build.shop | PASS | Drinks Shop at (48,23) rot 90, $650, $30 per drink |
| build.toilet | PASS | Small Toilet at (48,24) rot 90, $100 |
| build.queue | **FAIL** | no placeable queue path: guests never walk queue cells (`GuestPathGrid`), the queue is virtual (slots at the entrance cell, max 20) |
| build.staff | PASS | mechanic grade 1 ($150/month), handyman grade 2 ($60/month); no staff agents in the world |
| time.monotonic | PASS | 0 violations; guest time 1800.000094 s (float accumulation), economy tick 108,000 |
| economy.income-and-expenses | PASS | income: gate $35,760, shops $51,690; expenses: staff $2,100, other $34,460 (cost of goods: 1,723 drinks × $20) |
| economy.ledger-consistent | PASS | 0 violations in 108,000 samples, 10 months closed |
| guests.flow | PASS | 1,800 arrived, 1,788 admitted, 0 turned back, 290 used the ride, 928 the shop, 157 the toilet, 1,673 left, 106 in the park at the end |
| queues.no-stuck-queue | UNRESOLVED | Belly Bounce: max wait 149.95 s (reference 150 s), mean wait (all queues) 23 s, queue full 20/20, longest stall 28 s; still queued at the end ≤ 126.8 s; shop and toilet queues full (4/4) |
| paths.no-unreachable-goal | PASS | all targets reachable; 0 stuck guests; 0 Confused give-ups; 0 lane-less ejections |
| rides.scripts-run | PASS | no faults (Belly Bounce, Drinks Shop, Small Toilet waiting; Bus, Gates, Lights running) |
| staff.work | PASS | 10 wage payments ($2,100), 17 repairs, 799 litter items cleaned; Belly Bounce state of repair 55 at the end |
| determinism.same-seed | **FAIL** (report only) | raw guest hash differs in-process (634BC7756C8C6326 vs 94E148DBB92A1650); gate hash matches (B046FADB47BE93F3); no divergent minute |

Totals: 13 pass, 2 fail, 1 unresolved. With `--seed 1` (probe off) the other 15 rows get the same verdicts.

### Which gameplay area the failures point at

- **paths / guests: `build.queue`.** There is no queue-path building. Guests use
  a virtual queue at the entrance cell and never walk queue cells. Needed: a
  queue-path builder, walkable queue cells in `GuestPathGrid`, and queue geometry
  from `Info.Shape` (GUESTS.md "walking real queue cells").
- **determinism (DET node): `determinism.same-seed`.**
  `OriginalObjectRuntime.nextAttractionId` is static, so the attraction ids, and
  with them the raw guest hash, depend on how many objects were created earlier in
  the process. The simulation itself is reproducible: the normalised gate hash and
  cross-process raw hashes match. The fix belongs to the DET node (per-park id
  allocation).
- **rides / guests: `queues.no-stuck-queue` (unresolved).** All three queues
  fill to their approximated limit. Belly Bounce guests wait up to five full
  30-unit cycles. Deciding this needs an original queue-time or cycle-time
  rule, the `InitDuration` unit (`RIDES-016`) and the original queue length.

Passing rows that rest on approximations rather than original rules: arrival
rate and needs (GUESTS.md approximation table), wear/repair/cleaning (`ECON-021` to
`ECON-024`), path building (`GATE-002`), opening the park on load (`ECON-031`).

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
- Long-run limits beyond 30 minutes (for example the 144-month ledger history or the
  1,000-guest cap).
