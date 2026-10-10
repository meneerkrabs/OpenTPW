# Review QUEUE-V: queue cells, admission and charging (cfae05b, 85f0163, c213e81)

October 10, 2026. Independent review of the queue stack on `12992e8`:
`cfae05b` (QUEUE-R research, docs/reverse/QUEUE-plan.md and `lanes/queue/`),
`85f0163` (QUEUE-I implementation, tests, docs) and `c213e81` (queue cells in
the shared `ParkCellMap`, per-cell charge, traced refund). Main moved during the
review. The merge checks were made against `8a88379` (M3 gate) and again against
`5ec2622` (DET merged: `WorldSeed`, `WorldStateHash`, source guard).

All binary results are static. Nothing original was executed. Claims are
bounded to the decoded sites, the scratch harnesses and the runs listed here.
The scratch merges and harnesses live outside the repository.
Witness: `tools/ppc-analysis/lanes/review/test_queue_v1.py`. It has its own PEF
reader, pattern-data unpacker, PowerPC field decoder and float32 models. It does
not import `pef.py`, `timer_evidence.py` or `queue_evidence.py`. Its code
section equals the repository loader's byte for byte. Its data section equals
the loader's first 339,516 bytes (the initialised size; the loader then pads
zeros up to the section's total size).

## Merge blockers

**B1. The stack does not merge onto main `5ec2622`.** Four files conflict:
`docs/FIDELITY-REGISTER.md`, `tools/fidelity_register.py`,
`source/OpenTPW/World/Level.Objects.cs` and
`source/OpenTPW/World/Guests/RideVisitorBridge.cs`. The bridge conflict is
semantic. DET's `RideVisitorBridge.AddCanonicalState` hashes `offered` and the
`queue` list, and `c213e81` removes both. Exact fix (rebase onto `5ec2622`):

1. `RideVisitorBridge.AddCanonicalState`:
   - replace `hash.Add( offered )` with `hash.Add( called )`;
   - replace `AddList( queue )` with the head-to-tail list (`AddList( Queue.ToArray() )`);
   - add the queue state listed in section 3 (`letMeOnWritten`,
     `lastAdmissionTurn`, `QueueFrontCell`, `QueueEntranceDirection`,
     `queueCells`, `JoinCell`, `QueueEditCount`, `StalledAdmissionChecks`).
     `MaximumQueueLength` is now derived from these; keeping it is harmless.
2. `GuestSimulation.AddCanonicalState`: add the 14 guest queue fields of
   section 3 and the `ParkCellMap` cells (`RawTypeAt` and `QueueLinkAt` per
   cell, in index order). The queue cells are player-edited world state, and
   nothing hashes them today.
3. `Level.Objects.cs`:
   - keep main's `new ParkObjects( …, grid, Seed )`, with `c213e81`'s
     `IsReserved` lambda (`IsReservedByPrototype || Grid.IsQueue`);
   - move the `QueueFrontCell` / `QueueEntranceDirection` assignment into main's
     shared `Level.RegisterWithGuests`, so the HUD build flow and the M3 gate set
     them in one place:
     `var entrance = accessPoints.First( p => p.Kind == ObjectCellKind.Entrance ); … QueueFrontCell = (entrance.OutsideX, entrance.OutsideY); QueueEntranceDirection = GuestPathGrid.DirectionBetween( entrance.OutsideX, entrance.OutsideY, entrance.X, entrance.Y );`.
4. `tools/fidelity_register.py`:
   - keep `DET`, `GATE` and `QUEUE` in `REGISTERS`;
   - the text becomes "nine configured C# registers";
   - run `--write`. The result is 173 unresolved unique APPROX IDs (`--check` OK).
5. Re-pin `DeterminismTests.HashOfAFixedRunIsTheSameInEveryProcess`. A re-pin
   is unavoidable. The synthetic park runs a capacity-1 shop, and its call order
   changes, because `ONRIDE < CAPACITY` now gates the call (0xe1404). In the
   scratch merge:
   - with only the item-1 renames, the value is `0x22C4150D31D187DE`;
   - with the full field set of items 1 and 2, it is `0x87E026CA0ADA3D0B`;
   - the old value was `0x0D8B481BB19391DC`.

   Pin whatever the final field set yields.

With exactly this resolution (scratch, full field set), the build has 0
errors, and both DET source-guard tests pass
(`SimulationCodeUsesNoProcessWideRandomnessOrClock`,
`SourceGuardCatchesEachForbiddenForm`). The test numbers are in section 5.
Against `8a88379` only three files conflict. There the register and
`Level.Objects.cs` are resolved the same way, and `RideVisitorBridge.cs` merges
cleanly.

No other blockers. The findings below should be fixed before or with GATE-UPD,
but they lose no guest and break no invariant.

## Verdicts

| Check | Verdict |
| --- | --- |
| 1. Cited operands, decoded independently (limit with +436/+424, single precision and NaN; 1.2 × position; admission gates with the track-type skip; depths 0/63/127/191; boredom branch; refund) | **Confirmed**: all six groups, plus the lateral `rand mod 28 + 114` |
| 1. Boredom-unreachable labelled bounded | **Confirmed** ("high for the arithmetic; bounded for writers"). The writer enumeration misses 6 sites (S4); by context none is a guest writer |
| 2. Guests stay on queue cells; joined = boarded + left + queued; edits mid-run | **Confirmed** in 10 seeded scratch runs (0 violations) |
| 2. Ride removal, closing, breakdown | Removal and closing: **confirmed** (queue → 0). Breakdown: **partly**. Each standing guest leaves by the rule, but the queue does not empty, because guests keep choosing and joining a broken ride (S2) |
| 2. Changed existing tests | **Justified, not weakened** (same events in the traced order; a specific refusal reason replaces a removed setter) |
| 3. Determinism rules in QUEUE code | **Confirmed**: no unseeded or target-typed `Random`, no `GetHashCode`/`HashCode`, no mutable static numerics, no clock; both dictionaries are only used through keyed lookups |
| 3. Author's list of new state fields complete | **No**: the hash covers 2 of 14 new guest fields and none of the bridge or cell-map queue state (section 3) |
| 4. M3 gate after merge | Runs; deterministic across two runs; 13 pass / 2 fail / 1 unresolved on `5ec2622` (same as main). Belly Bounce queue 20 → 4 as predicted; the queue row's wait numbers are distorted (GATE-UPD item 3) |
| 5. Build, tests, evidence runner, register, CRLF, whitespace, `as_posix` | **Confirmed**: author's numbers reproduced exactly (section 5) |

## 1. Operands (independent decode)

Every operand below is asserted by `Binary` in `test_queue_v1.py`. That class
needs `OPENTPW_MAC_BIN`, and `SimThemePark.data` must have SHA-256 `04809cd4…e295f5`.

- **Limit `0xdcb74`.**
  - `lhz +46` with the `rlwinm` mask `0x8`, then `li 100`.
  - Otherwise: `lwz +84` (SPEED). Zero gives G = `lfs` toc `0x54ec` = 1.0.
  - Else `lbz +76` `slwi 6` (stride 64) and `lwz 424` (InitSpeed), with G by
    `fsubs`/`fdivs` (single). SPEED converts unsigned (`0x4330000000000000`),
    InitSpeed signed (`…80000000`).
  - Then `lfs 436` (QWTC), `lbz 89` (CAP) and `lbz 88` (DUR), followed by
    `fmuls` G×CAP, `fmuls` QWTC×…, `fdivs` /DUR (all opcode 59).
  - `fcmpo` against 4.0, then `bf gt` to `fmr f1,4.0`. So both NaN and ≤ 4 give 4.
  - `0x1c3fbc` truncates (`fctiwz`) and saturates at 2³².
  - Model checks: Belly Bounce (HasQueue) = 100; 130·5/30 → 21; 0/0 → 4.

  Note: G is also stored to a global (`sec1+0xec654`); no consequence.
- **Move delay `0xef868`.**
  - `stw +508` = turn, `lbz +497`, `lfs` toc `0x55a0` = 1.2f (`0x3f99999a`),
    `fmuls`, `0x1c3fbc`, `stw +500`. So `trunc(1.2f·p)`: 0, 1, 2, 3, 4, 6 … and
    118 at p = 99.
  - Gap rule: `cmplwi gap, 2` with `bf gt` to the decrement at `0xed4e4`. The
    compare is unsigned, so a negative gap moves at once. The port matches
    (`gap is >= 0 and <= 2`).
- **Admission `0xe1404`.** The script variables read are
  2 (log), 0 LETMEON, 2 CAPACITY, 5 ONRIDE and 9 RUNNING.
  - `ONRIDE < CAPACITY` (`cmpw`/`blt`) skips the bypass test. Otherwise the
    call goes ahead only if `0xe2fe4` (type record `+156 == 3`: `subfic 3`,
    `cntlzw`) holds or `+156 == 2`. That confirms "skipped for track types 2/3".
  - RUNNING == 0, or `+46 & 0x100` (RunsContinuously).
  - Pending `+104` and head `+56` are compared with the sentinel.
  - `0xee76c`: head state 11 and `+497 == 0`. Then `+104 = head`.
  - The `+100 ≠ 0` precondition is not modelled in the port (unnamed field).
- **Depths `0xddcc4`.** `addi −4` / `cmplwi 4` walk four per cell. Toc `0x54d8`
  = 0.25 and `0x54dc` = 255.0, two `fmuls` and `fctiwz` give
  0/63/127/191. Depth `> 128` branches to the untraced `0xdde74`.
  - Lateral: `bl 0x105328`, magic `0x24924925` = mod 28 (verified over 2²⁰
    values), `addi 114`.
- **Boredom.**
  - `0xed5a0` `cmplwi w, 30`, then `bf gt` to `0xed66c`: `lwz +508`,
    `addi 100`, `cmplw`. So the boredom leave is only reachable inside the
    30-turn window, where `+520 ≤ +508` makes it contradictory (model exhaustively
    checked).
  - Writer scan (whole code section; every D-form store, base not r1, whose
    bytes overlap +508..511 or +520..523): 28 sites, no `stmw`.
  - Six of them are not named by QUEUE-plan §5.3 (S4).
- **Refund (ClearCell).** Both queue paths, `0x85dcc` and `0x85ed4`, do the same:
  - `bl 0xe2424` (age-based scrap percentage: timestamp difference, year
    bucket), then `bl 0x7bc70`, a global getter;
  - `mullw`, then `mulhwu` with `0x51EB851F` and `srwi 5`, which is unsigned
    division by 100 (exhaustively equal to `scrap·cost // 100`);
  - `bl 0xcbf50`.

  `RefundQueueCell` computes `CellCost(Queue) × ScrapPercent / 100` in the
  same order.

  Bound: that `0x7bc70` returns `Costs.QueueCell`, and that `0xe2424`'s year
  buckets equal ECON-025's 365-day years, rest on the author's PATH-plan
  reading. I did not trace them further.

Port deviations found while decoding (not blockers):

- The state-11 needs window compares `trunc(happiness)` and `trunc(toilet)` as
  bytes (`fctiwz`, `clrlwi 24`). So "> 80" means ≥ 81. The port compares floats
  (80.5 > 80) (S5).
- `CheckAdmission` falls back to the bridge capacity when `VAR_CAPACITY ≤ 0`.
  This affects synthetic scripts only.

## 2. Behaviour (scratch harness, not committed)

A copy of `c213e81` got one extra MSTest class (outside Git). The setup:

- a 14×14 grid, path row y = 0, a 6-cell queue up column x = 2, and a capacity-3 shop script;
- 60 guests sent per phase;
- seeds 1, 7, 42, 1234 and 99991, each with RunsContinuously on and off.

The phases:

1. 200 turns;
2. extend by one cell (3,1);
3. remove the middle cell (2,4) and everything behind it (5 cells);
4. re-lay 4 cells;
5. `VAR_BROKEN = 1` for 60 turns;
6. 200 turns;
7. close for 5 turns;
8. 200 turns;
9. `Unregister` plus `ReleaseAll`.

Checked on every tick:

- the list members equal the guests with `IsInQueue` at that ride;
- every queued guest's cell is a queue cell or the join cell, and standing
  guests are inside their cell;
- joins = boards + lefts + still queued.

| Result (all 10 runs) | Value |
| --- | --- |
| Count-invariant violations / list–state mismatches / off-cell | 0 / 0 / 0 |
| Joins = boards + lefts (seed 1) | 247 = 126 + 121 (+0 queued at the end) |
| After closing / after removal | queue 24 → 0 / 24 → 0 |
| Breakdown, 60 turns | 23 → 16..23. Sampled every 4 turns, the queue cycles 2 → 24 (guests join, walk up, leave, rejoin) |
| `QueuePosition ≥ 0` on guests not queued after removal | 24 (S3) |
| Same seed twice | 12,797 identical `ComputeStateHash` values |
| Admission (seed 3, 600 turns) | 601 checks, 486 with the gates held, 115 calls, 0 `Stalled`, 371 with the head not yet standing (longest run 28 turns), longest called-to-boarded 1 turn |

**Changed tests.**

- `QueueFeedsLetMeOnInOrder…`: the new event list is a permutation of the old
  one, with `release 7` before `offer 8`. This is exactly the
  `ONRIDE < CAPACITY` gate decoded at `0xe149c` for a capacity-1 shop. The
  assertion now also prints the events.
- `QueueRespectsItsLimit…`: the removed `MaximumQueueLength = 2` setter no
  longer exists, because the length is now derived. The test asserts the
  derived 4 (room `4 × 1 cell`, limit floor 4) and the specific `NoRoom`
  reason, and keeps the closing and turn-away assertions. The assert count did
  not drop.

Both are justified and not weakened (`Git.test_changed_guest_tests_keep_their_assertions`).

## 3. Determinism and the canonical hash

The QUEUE additions to `source/OpenTPW/World` contain none of the forbidden
forms (witness `test_queue_code_obeys_the_determinism_rules`):

- no `new Random(`;
- no target-typed `Random r = new()`;
- no `GetHashCode` or `HashCode.`;
- no mutable static numerics;
- no clock.

`links` and `seenQueueEdits` are dictionaries, but every decision walks the
linked list (`head` → `Next`) or the attraction list. The lateral byte and the
facing draw come from the simulation's `GuestRandom`. On the `5ec2622` merge, DET's
guard (which also covers `Level*.cs`) passes.

The author lists the gate observables in the commit message, but there is no
complete state inventory. State that decides future behaviour or is player-made,
and that no hash covers yet:

- **Guest** (only `QueuePosition` and `QueueMoveDelay` are hashed, in the
  legacy `ComputeStateHash`):
  - `QueueJoinTurn`, `LastQueueWaitTurns`, `QueueCalled`;
  - `QueueStandingSinceTurn`, `InterludeTurn`, `InQueueInterlude`, `QueueJoinHappiness`;
  - `QueueCellIndex`, `QueueTargetIndex`, `QueueTargetX`, `QueueTargetY`, `LastQueueUpdateTurn`.
- **RideVisitorBridge:**
  - the list order (`head`/`tail`/links), `called`, `letMeOnWritten`, `lastAdmissionTurn`;
  - `QueueFrontCell`, `QueueEntranceDirection`, `queueCells`, `JoinCell`;
  - `QueueEditCount`, `StalledAdmissionChecks`.
- **ParkCellMap:** type, queue link (and links/flags/counter if PATH-I uses
  them) per cell.
- **GuestSimulation:** `seenGridVersion` and `seenQueueEdits` are edge
  detectors. After a load they must be re-derived from `Grid.Version` and
  `QueueEditCount`, not hashed.

B1 adds these to `WorldStateHash` through the two `AddCanonicalState` methods,
and that re-pins the fixed-run hash. Save/load of queue state is outside both
stacks (guests are not in `ParkSaveFile`).

## 4. M3 gate interplay

The scratch merge of `c213e81` onto `5ec2622` was resolved as in B1 (full
field set). In a Release build, `--m3-gate` was run twice. The rows were
identical in both runs (exit 1). The baseline is unmerged `5ec2622`.

| Row | main 5ec2622 | merged | Changed evidence |
| --- | --- | --- | --- |
| build.entrance | PASS | PASS | |
| build.paths | FAIL | FAIL | |
| build.attraction | PASS | PASS | |
| build.shop | PASS | PASS | |
| build.toilet | PASS | PASS | |
| build.queue | FAIL | FAIL | maximumQueueLength 20 → 4 |
| build.staff | PASS | PASS | |
| time.monotonic | PASS | PASS | |
| economy.income-and-expenses | PASS | PASS | ShopTakings 51,870 → 28,500; balanceAtEnd 99,420 → 91,630 |
| economy.ledger-consistent | PASS | PASS | |
| guests.flow | PASS | PASS | attractionBoarded 295 → 290, shopBoarded 1,729 → 950, usedShop 955 → 785, usedAttraction 286 → 266, departed 1,672 → 1,693 |
| queues.no-stuck-queue | UNRESOLVED | UNRESOLVED | attractionMaxQueue 20/20 → 4/4; maxWait 149.95 → 26.93 s; meanWait 23.07 → 3.95 s; completedWaits 2,184 → 3,988; leftQueueWithoutBoarding 0 → 2,588; referenceBound 150 → 60 s; attraction longest stall 27.98 → 24.48 s; shop longest stall 0.98 → 4.25 s |
| paths.no-unreachable-goal | PASS | PASS | |
| rides.scripts-run | PASS | PASS | |
| staff.work | PASS | PASS | |
| determinism.same-seed | PASS | PASS | run1 = run2 hashes; attraction ids 4 5 6 both runs |
| **Totals** | 13 / 2 / 1 | 13 / 2 / 1 | |

On `8a88379`, both trees give 12 / 3 / 1. The same queue and flow numbers
appear, and determinism fails there on main as well (attraction ids 4 5 6 vs
10 11 12). The fall in shop boardings is the traced capacity gate, plus the walk
to the stand point, at capacity 1.

The queue row's
`completedWaits` and `leftQueueWithoutBoarding` are artefacts. The gate's queue
test (`Queueing or WaitingToBoard or Boarding`) does not know state 12
`MovingUpQueue`. So every move-up ends one "wait" and counts it as leaving.

### GATE-UPD specification

1. **Registration.** After B1, `Level.RegisterWithGuests` sets
   `QueueFrontCell`/`QueueEntranceDirection`, so the gate needs no code of its
   own for this.

   In the current layout, the Belly Bounce entrance's outside cell (47,22) is
   itself a path cell. Then `QueuePaths.CheckExtend` answers `NoFrontCell`, and
   the queue stays the one-cell fallback (room 4).

   GATE-UPD must make that cell a non-path cell. Either place the ride, or end
   the scripted path, so that N ≥ 1 empty cells lie between the entrance's
   outside cell and the path. Report the N it uses.
2. **build.queue through the queue tool's code.** Extract the body of
   `Level.BuildQueueCell` into a static helper, in the same way
   `RegisterWithGuests` was extracted. The helper takes the grid, the ride, the
   economy and the blocked-cell test, so the HUD click and the gate share it:
   - `CheckExtend`;
   - then `ParkEconomy.TrySpendCell( CellPurchase.Queue )`;
   - then `QueuePaths.TryExtend` for each cell.

   PASS evidence:
   - `cellsLaid = N`;
   - cost `N × Costs.QueueCell` (75), booked per cell;
   - `QueueSizeInCells == N`;
   - `JoinCell` not null and reachable from the park entrance;
   - `MaximumQueueLength == min(100, 4N)`.

   Optionally remove the last cell and add it back, to check the
   `RefundQueueCell` amount.

   Label the click route itself as not exercised (no HUD without a GPU), and
   drop the "runtimeQueue=virtual" text.
3. **Wait tracking.** Use `guest.IsInQueue`, which includes state 12, or
   subscribe to `GuestSimulation.QueueWaitCompleted` (waits in park turns ×
   248 ms). Count `leftQueueWithoutBoarding` only for a transition
   in-queue → not-in-queue that is not a boarding. Record each guest's join
   position p (`QueuePosition` at `BeginQueue`) for item 5.
4. **Progress check (QUEUE-plan §9a).** Subscribe to
   `RideVisitorBridge.AdmissionChecked`. Do **not** rely on `Stalled` or
   `StalledAdmissionChecks`: they cannot become true (S1). Compute two signals
   from the fields:
   - the streak of evaluations with `ConditionsHold && HeadGuest ≠ 0 && !HeadAtFront`
     (the head is not ready);
   - the age in turns of `CalledGuest ≠ 0` until it boards or is withdrawn.

   Each signal fails when it exceeds an explicit bound, a new GATE APPROX entry.
   The scratch run gives longest values of 28 turns and 1 turn. Report both
   maxima. If S1 is fixed in the bridge, read the new counters instead.
5. **Belly Bounce bound (§9b).** Use
   `W(p) ≤ (⌊p/CAP⌋ + 1) × (DUR + 1 + τ)` and
   `W_max = ⌈Qmax/CAP⌉ × (DUR + 1 + τ)`, with:
   - CAP = 5 and DUR = 30 from `Parameters` (clamped);
   - Qmax = `MaximumQueueLength` = min(100, 4N);
   - τ an explicit `APPROX:GATE-NNN` parameter.

   Report `τ_measured = max_p (W(p) / (⌊p/CAP⌋ + 1)) − 31 s` next to the
   parameter. Replace `referenceBoundDerivation`'s "queue length 4 x capacity"
   with the new derivation. Per §9 the row stays UNRESOLVED until τ is traced,
   unless the project accepts a τ parameter. Either way, item 4 must pass.
6. **Determinism.** No gate change. The B1 hash fields cover the queue.

## 5. Build and tests

SDK `/Users/sander/.local/share/opentpw-dotnet10/dotnet` 10.0.401 (the
non-symlinked path), Release.

| Tree | Build | OpenTPW.Tests without assets | with `OPENTPW_GAME_PATH` |
| --- | --- | --- | --- |
| `c213e81` (this review) | 0 errors | 931 pass / 244 skip / 0 fail | 1104 / 71 / 0 |
| merge onto `8a88379` (scratch) | 0 errors | 933 / 245 / 0 | 1107 / 71 / 0 |
| merge onto `5ec2622`, B1 resolution (scratch) | 0 errors | 945 / 247 / 0 | 1121 / 71 / 0 |

- The `c213e81` numbers equal the author's.
- Evidence runner, with `--mac-bin`, `--pc-data` and
  `--dotnet <SDK>` (`DOTNET_ROLL_FORWARD=Major`): OK, 11 Python suites, 695
  tests, 81 skipped (queue lane 25 ran, 0 skipped). With this review's file added: 717 tests, 89 skipped, 0 failed. Seven .NET harnesses passed;
  the others are not-run for documented reasons.
- `fidelity_register.py --check`: 165 unresolved unique APPROX IDs (`c213e81`).
- `git diff 12992e8 --ignore-cr-at-eol --stat` = `--stat`: 32 files, +3,426 / −177.
- `git diff 12992e8 --check`: clean.
- The queue lane writes `witness_source` with `relative_to(REPO).as_posix()`.
  Its other `str(Path)` uses are `sys.path` entries only.
- `test_queue_v1.py`: 22 tests, all pass with `OPENTPW_MAC_BIN`; 8 skip without it.

## Should fix (non-blocking)

- **S1. `AdmissionCheck.Stalled` is structurally false.** `CheckAdmission`
  calls the head (`calledNow = head`, with `head ≠ 0` inside `conditions`)
  whenever `conditions && atFront`, so `Stalled` can never be true.
  GUESTS.md says so ("by construction it stays 0"). But the asset test's
  `Assert.AreEqual( 0, ride.StalledAdmissionChecks )` cannot fail, and
  `85f0163`'s Directive tells the gate to take its progress check from it.

  Fix:
  - drop that assertion and the Directive wording;
  - add `HeadNotReadyStreak` (consecutive evaluations with
    `ConditionsHold && head ≠ 0 && !atFront`) and `CalledAgeTurns` counters;
  - or define `Stalled` from them with an explicit bound.
- **S2. Breakdown does not empty the queue.** `ChooseAttraction` and
  `JoinQueue` ignore `IsBroken`, so guests keep joining a broken ride, walk
  up, and leave on their first state-11 turn. `CheckAdmission` also ignores the
  breakdown (the ride `+408` state is untraced), so the head can still be
  called, because step 1 precedes step 3.

  Fix: skip broken attractions in `ChooseAttraction`, and return `Closed` from
  `JoinQueue` while `IsBroken`, both tagged APPROX. Alternatively, document the
  churn as the untraced original behaviour.
- **S3. Stale queue fields after ride removal.** `GuestSimulation.Unregister`
  and the orphaned `WaitingToBoard` path call `ReturnToPath` without
  `ClearQueueState`. Guests keep `QueuePosition`, `QueueJoinTurn` and
  `QueueCalled` (24 guests in the harness), and `QueuePosition` is hashed.
  Call `ClearQueueState` there.
- **S4. QUEUE-plan §5.3 writer list is incomplete.** It says it "covers every
  D-form store with displacement 508/520". The full-section scan finds these
  more:
  - `0x5ec80` (`stfs +508`, an 84-byte record initialiser in `0x5eb20`);
  - `0xf3ac0` (`stw 0,+508` in `0xf39f8`, called from `0xf8dc4`; the cited
    staff range ends at `0xf39f8`);
  - `0xf4e54`/`0xf4e8c` (`sth +520`, setters of `+518/+520`);
  - `0x1461b0`/`0x14636c` (`sth +510`, overlapping +508).

  By context they belong to other classes, so the conclusion stands as
  bounded. Amend the list and the sentence.
- **S5. Needs-window thresholds.** Compare truncated values
  (`(int)Happiness > 80`, `(int)Toilet > 80`), as the original does after
  `fctiwz`.
- **S6. `Level.BuildQueueCell` charges before `TryExtend`'s recompute.** A
  stale queue (grid edited, `SyncQueues` not yet run) can pass `CheckExtend`,
  get charged, and then be refused. Call `ride.RecomputeQueue( grid )` before
  `CheckExtend`, or refund when the result is not `Ok`.

## Not tested

- Native jungle smoke (the author's 4,279 frames; the Totem screenshot).
- The asset test's own scenario beyond its pass in the suite.
- The HUD queue-tool clicks.
- Windows/Linux.
- `0x7bc70`/`0xe2424` beyond the call shape.
- The untraced `+100`, `0xdfe34`, `0xe02ec` and `0xee8f8` paths.
- Save/load of queue state (not stored by either stack).
