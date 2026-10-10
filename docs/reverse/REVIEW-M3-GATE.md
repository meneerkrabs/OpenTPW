# Review M3-GATE-V: the headless `--m3-gate` evaluator (commit f6ad99f)

October 10, 2026. Independent review of `f6ad99f` ("Measure the M3 gameplay loop
with a headless --m3-gate evaluator", parent `ae886cf`), merged for testing onto
fork main `efc090d`. Scope: `source/OpenTPW/Client/M3Gate.cs`,
`M3GateApproximations.cs` (GATE-001/002), docs/M3-GATE.md, `M3GateTests` and
`M3GateAssetTests`, the `Program.Main` exit-code change and the register update.
The gate is the fixed acceptance check for milestone M3 (docs/COMPLETION-PLAN.md
§M3). This review therefore asks two questions: can each row fail, and does a
pass mean what the plan asks for? Nothing here says anything about how the
original game behaves. No original code was executed.

Evidence: `tools/ppc-analysis/lanes/review/test_m3_gate_v1.py`.
- 11 static tests read the subject straight from the git object store.
- 12 mutation tests need `OPENTPW_M3_MUTATE=1` and `OPENTPW_GAME_PATH`. They
  extract `f6ad99f` with `git archive` into a temporary folder (not a worktree),
  add fault hooks to `M3Gate.cs` that the `M3GATE_MUTATION` environment variable
  selects, build once and run `--m3-gate` once per fault.
- Result: 23 of 23 pass in 50 s.

## Merge blockers

**B1. An unresolved row exits 0, so "no stuck queues" can be accepted without
ever being checked.** `RunCommand` returns `report.HasFailures ? 1 : 0`.
`queues.no-stuck-queue` is `Unresolved` by construction (correct, see check 4),
and `Unresolved` does not count as a failure. Today the exit code is 1 only
because `build.queue` fails. Once a queue builder lands and DET fixes the
attraction-id counter, the gate will exit 0 with the plan's "no stuck queues"
clause never decided.

The mutation `block-boarding` shows what that allows. From minute 5 the Belly
Bounce script keeps running and the ride stays open, but the ride never takes the
offered guest again. The queue stays full (20/20) and nobody boards for 1,500 s,
the longest guest is still queued after 1,621.7 s at the end, and the gate adds
no failing row. Only the baseline `build.queue` fails (13 pass, 1 fail,
2 unresolved with the probe off). An acceptance check must not report success
there.

Exact fix: in `M3Gate.RunCommand`,

```csharp
return report.HasFailures ? 1 : report.Rows.Any( row => row.Verdict == M3GateVerdict.Unresolved ) ? 2 : 0;
```

Then document in docs/M3-GATE.md ("Running") and docs/RUNNING.md: "exit 2: no row
fails but at least one row is unresolved; M3 is accepted only at exit 0". Add an
assertion to `ReportListsEveryRowAndFailsOnAnyFailure` (rows 0–1, Pass plus
Unresolved, give exit 2). `Program.Main` already passes `Environment.ExitCode`
through. No other command writes it (check 6).

**B2 (mechanical).** `git merge-tree --write-tree efc090d f6ad99f` conflicts
only in `docs/FIDELITY-REGISTER.md`, which is generated. Resolve it with
`git checkout --theirs docs/FIDELITY-REGISTER.md && python3 tools/fidelity_register.py --write`.
That gives 142 unresolved IDs (efc090d has 140, plus GATE-001/002), 158 APPROX
and 69 DATA occurrences. The `--check` and `test_fidelity_register.py` runs pass.

Everything else below is a strengthening request (S*) or a nit (N*), not a
blocker.

## Verdicts

| Check | Verdict |
| --- | --- |
| 1. Assembly fidelity against `Level` | **Confirmed, with 6 divergences**, none of which flips a row today. A shared CPU-only core is recommended (S5). |
| 2. Rows can fail | **8 of 10 invariant rows can fail.** Proven by mutation for `time.monotonic`, `economy.ledger-consistent`, `paths.no-unreachable-goal` (end-of-run and per-guest sampler), `guests.flow`, `build.staff` and `determinism.same-seed`. The queue row is unresolved by design. `staff.work` and `rides.scripts-run` are weak (S1, S2). No invented pass threshold. |
| 3. GATE-001 time mapping | **Confirmed.** Numbers and citations are right. The reading fits the plan, but GATE-001 is a plan interpretation, not an original-fidelity APPROX (N2). |
| 4. Queue bound | **Confirmed unresolved.** The 150 s value is evidence only. No 15-minute (or other) bound exists anywhere. See B1 for the exit-code consequence. |
| 5. Determinism diagnosis | **Confirmed by code, by two-process versus one-process runs, and causally** (resetting the counter makes the raw hashes match). |
| 6. Merge readiness | **Ready after B1 and B2.** Build clean, both suites green, `--m3-gate` reproducible, exit-code change safe. The new files are CRLF (N4). |

## 1. Assembly fidelity (gate versus `Level`)

Compared line by line: `Level` constructor, `SetupEntities`, `SetupObjects`,
`SetupGuests`, `ConnectObjectsToGuests`, `ConnectObjectsToEconomy`,
`SyncObjectEconomy` and `Update`, plus `GameFlow.StartLevel` and
`ParkObjects.Add/Check/Simulate`.

What matches (pinned by static tests):

- **Mode.** The front end starts original levels with
  `ParkGameMode.FullSimulation` (`GameFlow` lines 131 and 144), so `Level` calls
  `OriginalPark.Load( …, includeEasymodePark: false )` and
  `ForOriginalLevel( park, FullSimulation )`. The gate does the same. With no
  save, `GuestPathGrid.FromOriginal( map, park.Save )` gets `null` in both, and
  `ObjectCatalog.Load( theme, IsEasy )` is the same.
- **Tick order.** `Level.Update`'s fixed step runs `SyncObjectEconomy`,
  `Guests.Tick`, `PlacedRide?.Simulate`, `Objects.Simulate`, `Park.FixedTick`.
  The gate runs the open-state sync, `guests.Tick`, fixed items, placed objects,
  `runtime.FixedTick`, with no prototype ride (the front end places none).
- **Object order and seeds.** `ParkObjects.Simulate` walks objects in insertion
  order: `AddDefaultFixedItems` (Gates/Lights/Bus) first, then built objects. Both
  count seeds from 1 per park and share one `RideScriptWorld`.
- **Attach order.** `AttachGuests`, which opens the park (ECON-031), runs before
  the attractions are registered in both. Purchase, register and link follow the
  order of `Level.PlaceObject`.

Divergences:

| # | Divergence | Can it make a row pass that fails in the game (or the reverse)? |
| --- | --- | --- |
| D1 | Fixed items are registered with the economy *before* `AttachGuests` (Level does it after) and are never `Link`ed or `IsOpen`-synced. | No. `ParkEconomy` reads `IsOpen` only for Ride, Shop and Sideshow kinds (static test). Fixed items have no guest bridge registration. The economy ids come out the same (1–3, then built objects). |
| D2 | Sites are checked with the gate's own `CanLayPath` (terrain, not a path, not occupied), not `ParkObjects.Check`. | Not today: the gate's rule is stricter. Drift risk: if `ParkObjects.Check` gets a slope or land rule (RIDES-018), the gate can keep placing objects the game would refuse, and `build.*` would pass wrongly. |
| D3 | Entrance and exit cells: Level snaps a non-walkable outside cell to the nearest walkable cell (`WalkableNear`, RIDES-028). The gate accepts only sites whose entrance opens onto the spine, and builds a charged connector (7 cells, $140) for the Belly Bounce exit. | Quantitative only: exit position, walking distance and the $140. In the game, the Belly Bounce exit (42,22) would snap to a spine cell and guests would teleport to it. |
| D4 | Paths are laid by editing `GuestPathGrid` directly (GATE-002). The game has no path tool. | **Yes, for `build.paths`.** The gate builds paths the player cannot build, and passes the row. See S3: it is inconsistent with `build.queue`, which fails for the same reason. |
| D5 | Every placed object is registered with guests without Level's `Runtime.IsAttraction` check. | No, for the three chosen objects (Ride, Shop, Facility). It could diverge if the role filters change. |
| D6 | The gate never runs `Entity.Update`, `OriginalObject.UpdateTransforms` or the frame-paced `FixedStepClock` (catch-up cap, `SimulationTimeScale`). | No for the measured rows (render-side, documented in "Not measured"). The economy runs `(int)Speed` ticks per fixed tick, and the gate relies on the default `Normal`. `time.monotonic`'s economy-tick check would catch `Paused`. |

**Recommendation (S5).** Replace the copy with a shared CPU-only core, for example
`LevelSimulation` holding `OriginalPark`, `ParkEconomyRuntime`, `GuestSimulation`,
a runtime-level object list and `FixedTick( dt )`. `Level` would wrap it with GPU
objects, and the gate would drive it directly. `ParkObjects` currently holds
`OriginalObject` (a GPU entity), so it needs an object factory or a runtime-only
list. That is a medium refactor, not a merge blocker. Until it lands, the static
tests `test_tick_order_matches_level_update`,
`test_level_object_order_is_fixed_items_then_built_objects` and
`test_divergence_*` fail when either side changes. The commit's own Directive
already says to keep them in step.

## 2. Can each row fail? (mutation results, jungle, 30 min, probe off unless noted)

| Mutation (injected in the scratch copy) | Row flips | First tick | Notes |
| --- | --- | --- | --- |
| `zero-step`: skip `guests.Tick` once at tick 1000 | `time.monotonic` → FAIL | 1000 | strict-increase check works |
| `negative-script-step`: `Simulate( -Tick )` at tick 1000 | process exits 1, **no report** | n/a | `GuestSimulation.Tick` and the ride VM throw `ArgumentOutOfRangeException(deltaSeconds)`. The negative branch of `time.monotonic` is defence in depth (N3). |
| `ledger-skew`: ledger `Balance += 1` (private setter) at tick 2000 | `economy.ledger-consistent` → FAIL | 2000 | `income-and-expenses` still PASS |
| `cut-path`: remove spine cell (47,21) at tick 1000 | `paths.no-unreachable-goal` → FAIL (all three unreachable); `guests.flow` → FAIL | n/a | |
| `cut-entrance-transient`: remove the Belly Bounce entrance cell at tick 36000, restore it at 36600 | `paths.no-unreachable-goal` → FAIL **through the per-guest sampler** | 36001 | the end-of-run target check passes again; 184 guest-ticks, 5 Confused give-ups |
| `close-toilet`: `Close()` the toilet at tick 1 | `guests.flow` → FAIL (`missingStages=toilet`) | n/a | |
| `stall-ride`: `Runtime.Stop()` the ride at tick 18000 | `time.monotonic` → FAIL (script clock frozen) | 18001 | caught only indirectly. Guests leave the queue (21 turned away). `rides.scripts-run` stays PASS with the script `Halted` (S2). |
| `stall-ride-from-start`: `Stop()` at tick 1 | `time.monotonic` and `guests.flow` → FAIL | 2 | |
| `block-boarding`: ride open and script running, `VAR_LETMEON` held at −1 from tick 18000 | **none** | n/a | B1: stall 1,500 s, still queued 1,621.7 s at the end, queue 20/20 |
| `no-handyman` | `build.staff` → FAIL; **`staff.work` stays PASS** on 17 repairs; litter peaks at 861.5 items, 0 cleaned | n/a | S1 |
| `no-mechanic` | `build.staff` → FAIL; **`staff.work` stays PASS** on 799 litter items; 0 repairs; ride state of repair 0, guests board 295 times (same as baseline) | n/a | S1; ride wear has no guest effect (documented under "Not measured") |
| `reset-ids` (probe on): reset `nextAttractionId` to 1 before run 2 | `determinism.same-seed` → PASS | n/a | check 5 |

Thresholds, as found in the code:
- `build.paths` passes at ≥ `SpineLength / 2` = 7 cells. That is a build
  heuristic, not a semantic bound.
- The per-guest unreachable rule allows one tick of grace, taken from
  `GuestSimulation`'s next-tick Confused rule.
- `guests.flow` and `staff.work` are existence checks (≥ 1).

None of these is an invented original-semantics threshold.

Strengthening (not blockers, but needed before the gate is used for acceptance):

- **S1. `staff.work` is an OR across staff types.** Make it per hired type:
  repairs > 0 when a mechanic is hired, litter cleaned > 0 when a handyman is
  hired. The mutation tests show either one alone passes today.
- **S2. `rides.scripts-run` passes a stopped script.** It checks only "no fault",
  so `Halted` passes. Also require every placed script to end in `Running` or
  `Waiting`. Do not require a completed cycle: Belly Bounce's `CompletedCycles`
  is 0 in the baseline, because RIDES-023 counts VAR_RUNNING 1→0 and BOUNCE does
  not toggle it.
- **S3. `build.paths` passes and `build.queue` fails, yet neither has an in-game
  builder.** Give both the same verdict rule (both FAIL, or both a PASS that
  names GATE-002 as a scripted substitute), so the M3 bullet "Build paths…" is
  not shown as met.
- **S4. Catch exceptions in `M3GateRun.Execute`.** Turn them into a FAIL row
  (`gate.run`, with the tick), so a crash still writes the JSON report.

## 3. GATE-001: what "30 minutes" means

The plan says "30 minutes of accelerated headless running" (no "in-game"). The
gate reads it as 30 minutes of normal-speed simulation time, run as fast as
possible: 30 × 60 × 60 = 108,000 fixed ticks, 3.5 s wall time per probe pair.
This is the natural reading, because "accelerated" qualifies the running. The
other reading, 30 wall-clock minutes of accelerated running, would be about
1,000 times more simulation (1.75 s per 30 simulated minutes, so hundreds of
park years). That is a stress test, not what the plan describes.

It also agrees with the binary. The original's normal speed is one park turn per
248 ms (`BIN:STP-PPC:0x101C22E0`), so 30 real minutes of original play is
1,800,000 / 248 = 7,258 turns. That is exactly what the gate runs.

Numbers re-derived from `ParkCalendar.cs`'s constants (static test):
- 7,258 turns.
- 7,258 × 3,750 s (`0x100E4394`, 15000/4) = 27,217,500 park-clock seconds, which
  is 315 days.
- The end date is 2000-11-11 (Year 1, month 11, day 11; 2000 is a leap year).
- 30 park-clock minutes (1,800 s) are less than one 3,750 s turn.

The citation is right: the two BIN addresses are the ones `ParkCalendar.cs`
annotates, and ECON-001 is the APPROX for sampling 60 Hz ticks into 248 ms
turns.

- **N1.** A 30-minute run closes 10 months and never crosses a park year. Year-end
  work (`StartYear`, yearly ledger roll-up) is never exercised. Add this to
  "Not measured".
- **N2.** GATE-001 interprets OpenTPW's own plan. Its "evidence needed: the
  original's notion of elapsed play time" cannot be satisfied by original
  evidence, yet the register lists it under "Original-fidelity area". Reword it
  as a plan decision that cites the 248 ms turn, or move it out of the
  original-fidelity count.

## 4. The queue bound

- `AddQueueRow` contains only `M3GateVerdict.Unresolved`.
- `referenceBoundSeconds = (ceil(20/5) + 1) × 30 = 150` is written only into the
  evidence.
- `maxWait` is never compared with anything.

The baseline reports max wait 149.95 s with "reference 150 s" and verdict
UNRESOLVED. The static tests pin all three points.

No "15 minutes" (or 900 s) bound exists in `M3Gate.cs`, docs/M3-GATE.md,
COMPLETION-PLAN.md or the tests. Other numeric constants (spine length 14,
connector ≤ 12) shape the scripted park, not a verdict. The consequence of an
always-unresolved row is B1.

If a fail condition without an invented number is wanted later, a liveness rule
fits better than a time bound. For example: "an open, running attraction whose
queue is non-empty and that boards nobody for the rest of the run after some
tick". It still needs a horizon, so it would need its own GATE APPROX entry. It
is not proposed here as a pass rule.

## 5. Determinism

**Code.** `OriginalObjectRuntime` has
`private static int nextAttractionId = 1`, incremented with `Interlocked`.
`GuestSimulation.ComputeStateHash` adds `guest.AttractionId`. Attractions are
sorted by id. A monotone counter keeps their relative order, so only the hash
input changes, not behaviour.

**Runs** (merged Release build, two separate processes):

| | run 1 raw guest hash | run 2 (same process) raw guest hash | gate hash |
| --- | --- | --- | --- |
| process A | `634BC7756C8C6326` | `94E148DBB92A1650` | `B046FADB47BE93F3` both |
| process B | `634BC7756C8C6326` | `94E148DBB92A1650` | `B046FADB47BE93F3` both |

- The ids are 4 5 6 in run 1 and 10 11 12 in run 2.
- The JSON reports of both processes are byte-identical apart from `wallSeconds`.
- With `reset-ids` (the counter reset to 1 before run 2), the raw hashes match
  and the row passes.

The diagnosis is confirmed. The fix belongs to DET (per-park id allocation).

**N5.** The row is labelled "report only", but its FAIL sets exit code 1 through
`HasFailures`. Either exclude it from the exit code or drop the label.

## 6. Merge readiness onto efc090d

All in a scratch clone (`/tmp`, not a worktree), merge commit with the register
regenerated (B2). SDK 10.0.401, Release.

| Step | Result |
| --- | --- |
| Build `source/OpenTPW.sln` | 0 errors; no warning in the new files |
| `OpenTPW.Tests` without assets | **866 pass / 241 skip / 0 fail**. Main is 864/240/0: +2 asset-free M3 tests, +1 skipped asset test. |
| `OpenTPW.Tests` with `OPENTPW_GAME_PATH` | **1,036 pass / 71 skip / 0 fail**. The three M3 tests run and pass (asset test 3 s). Main efc090d with assets is 1,033/71/0, so the merge adds exactly +3 passes. |
| `--m3-gate` twice (separate processes) | same table: 13 pass, 2 fail (`build.queue`, `determinism.same-seed`), 1 unresolved; exit 1 both; identical JSON |
| `fidelity_register.py --check` and its unit tests | pass (142 IDs) |
| `git diff --stat` versus `--ignore-cr-at-eol --stat` | identical (10 files, +1297 −6): no line-ending-only churn in existing files |
| `git diff --check` | reports every line of the three new CRLF files and the 6 added `Game.cs` lines (Game.cs is CRLF already). No space or tab trailing whitespace (0 lines). CI does not run `--check`. |
| `Program.Main` returning `Environment.ExitCode` | safe. The only writer in `source/` is the `--m3-gate` branch of `Game.Run`. The smoke tests use `Environment.Exit( 1 )`, which is unaffected. Every other path keeps 0, and exceptions still return 1. |

- **N4.** The three new C# files are CRLF. 119 of the last 125 `.cs` files added
  on main are LF. Converting them would keep `git diff --check` clean.
- **Build note for reviewers on macOS.** Build from a resolved path
  (`/private/tmp/...`, `/private/var/...`). A solution path through the
  `/tmp → /private/tmp` symlink makes NuGet restore each project under two
  identities. The resulting `OpenTPW.deps.json` then lacks the project
  references, and `OpenTPW.dll` fails with `FileNotFoundException: OpenTPW.Files`.
  This is a pre-existing environment trap, not caused by this commit. The test
  harness resolves the path.

## Reproduce

```sh
python3 -m unittest tools/ppc-analysis/lanes/review/test_m3_gate_v1.py          # static, ~8 s
OPENTPW_M3_MUTATE=1 OPENTPW_GAME_PATH=/path/to/theme-park-world \
  python3 -m unittest -v tools/ppc-analysis/lanes/review/test_m3_gate_v1.py    # + 12 mutations, ~50 s
```

---

# Round 2: GATE-FIX (ea09cda, stacked on efc090d: 8fa931f, bb30770, ea09cda)

Re-check of the fixes for B1, B2, the weak rows and divergences D1 to D6.
SDK 10.0.401 (`~/.local/share/opentpw-dotnet10/dotnet`, non-symlinked path),
Release. New tests: `tools/ppc-analysis/lanes/review/test_m3_gate_v2.py`
(4 static, 6 mutations; it reuses the v1 hooks and adds its own).

## Round 2 merge blockers

**R2-B1. The stack does not compile once merged onto current main.** `origin/main`
is now 12992e8 (advisor e28fbe9 and the scenarios merge 12992e8 are in). The
textual merge has one conflict (below), and the merged tree then fails to build:

```
M3Gate.cs(273,44): error CS1739: The best overload for 'Load' does not have a parameter named 'includeEasymodePark'
M3Gate.cs(274,56): error CS1503: Argument 2: cannot convert from 'OpenTPW.ParkGameMode' to 'OpenTPW.ParkStartKind'
```

The scenarios lane renamed `OriginalPark.Load(..., includeEasymodePark)` to
`readShippedSave` and made `ParkEconomyRuntime.ForOriginalLevel` take a
`ParkStartKind`. Exact fix in `M3GateRun.Build` (the call `Level` makes for a new
Full Simulation park, `Level.cs:63-65`):

```csharp
park = OriginalPark.Load( options.Level, readShippedSave: ParkStart.ReadsShippedSave( ParkStartKind.FullSimulation ) );
runtime = ParkEconomyRuntime.ForOriginalLevel( park, ParkStartKind.FullSimulation );
```

and in `docs/M3-GATE.md:52` replace `OriginalPark.Load( level, includeEasymodePark: false )`
with the same `readShippedSave: false` wording. `ReadsShippedSave( FullSimulation )`
is false, so the park is loaded exactly as before. Verified on a trial merge
(origin/main + ea09cda + these two lines, register regenerated): 0 errors;
tests without assets **915 / 244 / 0** (main + the gate's tests);
with assets ****1088 / 71 / 0****; `--m3-gate` twice: exit 1, identical
JSON, and every row's verdict and evidence is identical to ea09cda alone (12 pass,
3 fail, 1 unresolved). The rename has no behavioural effect on the gate.

**R2-B2 (mechanical). `docs/FIDELITY-REGISTER.md` conflicts** (main 149 IDs vs the
stack's 142 + GATE). Fix: take either side, then `python3 tools/fidelity_register.py --write`;
the result is 151 IDs and `--check` passes. `docs/RUNNING.md` and `Game.cs`
merge cleanly.

No blocker remains inside the stack itself: on efc090d it is correct.

## Round 2 verdicts

| Item | Verdict | Evidence |
| --- | --- | --- |
| B1 exit code | **fixed** | `ExitCode => HasFailures ? 1 : HasUnresolved ? 2 : 0`; `block-boarding` (round-1 mutation, built from ea09cda) exits 1; with path/queue builders it would exit 2 (unit test only, as the author states). |
| B2 register | **fixed on efc090d**, recurs on current main (R2-B2) | `fidelity_register.py --check` passes at ea09cda (142 IDs). |
| staff.work per type | **fixed, still lenient** (W1) | `no-mechanic`, `no-handyman`, `idle-mechanic`, `idle-handyman` fail. The "when needed" conditions are real: `no-wear-no-litter` (state of repair held at 100, litter zeroed, both types idle) passes with 0 needed / 0 repairs / 0 litter, so a zero-breakdown run does not fail the mechanic. |
| rides.scripts-run | **fixed** | `stall-ride` fails from tick 18000; `halt-last-tick` (Stop() on tick 108000) fails with `firstViolationTick` 108000 and `notRunningAtEnd` Belly Bounce. RideVMState has only Running, Waiting, Halted, Faulted, so Running/Waiting is attainable (baseline: all three Waiting). |
| build.paths | **fixed** | always FAIL, evidence `inGameBuilder` "no in-game path builder ...". |
| D1 fixed items | **fixed** | `ConnectFixedItemsToEconomy`: RegisterExisting + Link + per-tick open sync, after AttachGuests. |
| D2/D3/D5 build flow | **fixed** | `PlaceObject` calls `Level.GetCentredAnchor` → `ParkObjects.Check` → `economy.TryBuild` → `Level.RegisterWithGuests` → `Link`, the same order as `Level.PlaceObject` → `Objects.TryPlace` → `ObjectPlaced` → `RegisterObjectWithGuests`, then `LinkEconomy`. Belly Bounce exit snaps (42,22) → (47,22) through `WalkableNear`, as in the game. Setup cost $1,530 reproduced. |
| Level / ParkObjects refactor | **behaviour-preserving** | Diff read line by line: the old body is split into `RegisterWithGuests` + `ResolveVisitorCells` with the same guards (Guests null, not attraction, no entrance, exit defaults to entrance, either cell unwalkable → nothing registered), `item.Visitors` is `Runtime.Visitors` (`OriginalObject.cs:86`), and the instance `Check` forwards its own `Grid`/`IsOccupied`. Asset suite 1036 / 71 / 0 at ea09cda (same as the author). Pinned by the v2 static tests. |
| LF for new files | **consistent** | 85 of 97 `.cs` files in `Client/` and `OpenTPW.Tests/` are LF; `Level.Objects.cs` and `ParkObjects.cs` were and stay LF. `Game.cs` is CRLF (555/555 lines) and its 6 added lines are CRLF too. |
| Exit snap / setup cost | **matches the build flow** | see D2/D3/D5. |

### W1 (should fix before M3 is accepted; not a merge blocker today). One unit of work satisfies a staff type.

`staff.work` needs `repairs ≥ 1` when any repair was needed and `litterCleaned > 0`
when any litter existed. Mutations on ea09cda:

| Mutation | Evidence | staff.work |
| --- | --- | --- |
| `idle-mechanic-after-first-repair` (mechanic PickedUp after the first `RideRepaired`) | 1 repair for 17+ needed | **pass** |
| `idle-handyman-after-first-clean` (handyman PickedUp after the first cleaned tick) | < 2 items cleaned of > 100 dropped | **pass** |

`docs/M3-GATE.md` says "removing or idling either staff type fails the row"; that
holds only for idling the whole run. Today the gate cannot exit 0 (build.paths,
build.queue, determinism fail), so W1 cannot cause a false M3 acceptance yet; once
those rows pass, it could. Exact fix (keeps a zero-breakdown run passing):

- mechanic: keep the instance id of each `RideWorn`/`RideBrokeDown`; fail when an
  instance still has a need without a later `RideRepaired` for it, unless the need
  arose within one maximum repair time of the end
  (`max(MechanicConstsPerGrade[*].WorkDuration)` game hours plus one `UpdateHour`);
- handyman: fail when litter stayed above 0 for longer than one cleaning interval
  (`HandymanConstsPerGrade[grade].WorkDuration` game minutes) while a handyman was
  hired, or equivalently require `litterCleaned ≥ litterDropped − litterAtEnd` with
  `litterAtEnd` below one interval's drop.

Either rule makes both mutations above fail and `no-wear-no-litter` still pass.

### Notes

- **N6 (edge, accepted).** `late-litter` (no litter all run, one item added in the
  last tick) fails `staff.work` although no handyman could have cleaned it. The
  baseline drops litter all run (810.5 items), so this cannot fail a legitimate
  30-minute run; the W1 grace period above would also remove it.
- **N7.** A fixed item whose script halts (`halt-gate`: the Gates script stopped at
  tick 1000) is judged by no row; it only shows as `Gates Halted` in the
  `rides.scripts-run` evidence. Correct for M3 (the row is about the player's
  placed objects; fixed items are the level's), but the evidence line is the only
  trace. Optional: a separate report-only note.
- **N8.** `git diff --check efc090d ea09cda` still lists the 6 CRLF lines added to
  `Game.cs` (a CRLF file); with `core.whitespace=cr-at-eol` it is clean. The
  commit message's "diff --check clean" holds only with that setting.
- N5 from round 1 is resolved (the determinism row is no longer called "report only").

## Round 2 numbers (independent)

| Check | Result |
| --- | --- |
| Build `source/OpenTPW.sln` Release at ea09cda | 0 errors |
| `OpenTPW.Tests` without assets | **866 / 241 / 0** |
| `OpenTPW.Tests` with `OPENTPW_GAME_PATH` | **1036 / 71 / 0** |
| `--m3-gate` twice (separate processes) | exit 1 both; identical table and JSON apart from `wallSeconds`; 12 pass, 3 fail (`build.paths`, `build.queue`, `determinism.same-seed`), 1 unresolved (`queues.no-stuck-queue`); 286 riders, setup $1,530, 17/17 repairs, 810.5/810.5 litter |
| `fidelity_register.py --check` and its unit tests | pass (142 IDs) |
| `run_evidence_checks.py` | OK: 9 Python suites, 566 tests, 115 skipped |
| `git diff efc090d --stat` vs `--ignore-cr-at-eol --stat` | identical |
| `git diff --check` | clean with `cr-at-eol`; only `Game.cs` CRLF lines otherwise (N8) |
| `test_m3_gate_v1.py` mutations against ea09cda | **25 / 25** |
| `test_m3_gate_v2.py` | **10 / 10** (4 static, 6 mutations) |
| `git merge-tree --write-tree origin/main(12992e8) ea09cda` | 1 conflict: `docs/FIDELITY-REGISTER.md`; merged tree does not compile (R2-B1) |
| Trial merge + R2-B1 fix + register regenerated | 0 errors; 915 / 244 / 0 without assets; **1088 / 71 / 0** with assets; gate identical to ea09cda |

## Round 2 merge readiness

**Not merge-ready onto current main as is** (R2-B1 build break, R2-B2 register
conflict). Both fixes are mechanical and listed above; with them the merge is
ready. W1 should be fixed before any run is taken as M3 acceptance.

```sh
python3 -m unittest tools/ppc-analysis/lanes/review/test_m3_gate_v2.py          # static
OPENTPW_M3_MUTATE=1 OPENTPW_GAME_PATH=/path/to/theme-park-world \
  python3 -m unittest -v tools/ppc-analysis/lanes/review/test_m3_gate_v2.py    # + 6 mutations
```
