# Review WALK-V2: walk-fix after WALK-F and WALK-F2 (2079e79)

October 11, 2026. Independent focused re-review of the walk-fix branch at `2079e79`:

- `b10710e` merges ORACLE-F (`8a72a2c`) into WALK-I (`b505706`);
- `faa64cc` (WALK-F) closes WV-1..WV-13 of review WALK-V (`7380ab4`, `docs/reverse/REVIEW-WALK.md`);
- `2079e79` (WALK-F2) closes OV2-1..OV2-7 of review ORACLE-V2 (`2db81a0`, `docs/reverse/REVIEW-ORACLE-2.md`).

Method:

- The ported routines were read against a local `llvm-mc` disassembly (`lanes/review/ppcdis.py`).
- The isolated pure routines `0xffe38`, `0xe6b28`, `0xadf40` and the separation callback `0xfd5f4` ran in OpenTPW's
  own PPC interpreter (tools/ppc-analysis/oracle). Nothing else of the original was executed.
- Gate, tests, mutation suites, evidence runner and smokes ran on a detached worktree of `2079e79`.
- Each C# rule that WALK-F2 says a new test guards was reverted on its own in a scratch copy.
- Trial merges (not committed) went onto fork main `c09c6ba` and save-fix `3e7e07e`.

Witness: `tools/ppc-analysis/lanes/review/test_walk_v2.py`. Open findings are expected failures in it. It pins decoded
fields and model results only.

## Verdict: ACCEPT-WITH-FIXES

Every claim checked holds:

- The gate change follows the owner's rule of October 10, 2026. GATE-006 is registered and tagged, C = 9 is labelled as
  an enumeration of OpenTPW's own port, and the real gaps (WV-2, WV-3, WV-12) are closed stricter.
- Every mutation that failed the gate at `c6c19ea` still fails at `2079e79`, with the same new failing rows.
- Separation, avoid_walls' first pushing edge, the toilet phase, BOUNCE's first free slot and the preview validator
  match the disassembly. The oracle comparisons have 0 mismatches.

There are no blockers on walk-fix itself. WV2-1 must be fixed **at the merge onto main**: the merged tree fails
`fidelity_register.py --check` until then. WV2-2..WV2-5 are low or nit.

## The gate (GATE-006)

| Claim | Check | Result |
| --- | --- | --- |
| Queue bound 110 / 65 / 65 | 6 × 9 + 2 × 15 + 3 + 22 + 1 = 110; 1 × 9 + 30 + 26 = 65 | holds; the run reports 110 / 65 / 65 |
| Per-head limit min(bound, own walk + 26) | `HeadNotReadyLimit`, set once at the first judged evaluation | holds; 285 / 631 / 169 heads on their own walk, closest 15 turns under the limit |
| WV-2: no permanent young-head exemption | `HeadDeferred`: judged from 15 turns old; a head without a track is judged at once | fixed; 0 heads deferred in the jungle run |
| WV-3: a stuck head at position 0 is caught by rule (a) | `head-held-250-turns`, `head-delayed-60/170-turns` fail "from its own walk" | fixed (stricter) |
| WV-12: walks, not the cell index | `HeadRemainingWalkTurns` reads `WalkCrossings` and the walk's cells, never `QueueCellIndex` | fixed |
| GATE-006 registered | `M3GateApproximations`, tags at `HeadNotReadyBound`, `QueueCellWalkTurns` and `HeadRemainingWalkTurns`, the JSON derivation string, M3-GATE.md | holds |
| C = 9 honestly labelled | "the bounded enumeration of OpenTPW's port of the traced step"; the WALK register entries it rests on are listed; "the bounds hold for OpenTPW's walk and match the original's to the extent those entries do" | holds |

The derived bound is looser than the retired GATE-005 check (110 against 84 turns, 65 against 50). That is the owner's
decision of October 10, 2026, and it is recorded as such in the register entry and in M3-GATE.md. The per-head limit
makes rule (a) stricter than before for most heads.

**C = 9 under the ported separation.** The enumeration's "oppose" mode is now one virtual neighbour 0.1 cell ahead.
The step truncates the summed push to the max force, so five neighbours cannot push harder than one. A sideways push,
which could drive a guest into a wall, was not covered. This review reran the full enumeration
(`QueueWalkTermFullEnumeration`) with the virtual neighbour turned by 0°, 90° and 270° in a scratch copy:

| Neighbour angle | Worst updates for k = 0, 1, 2, 3, 4, 5, 8, 12, 16, 20, 25 crossings | Failures |
| --- | --- | --- |
| 0° (as committed) | 8, 18, 25, 33, 42, 50, 76, 109, 143, 176, 218 | 0 |
| 90° | 8, 19, 25, 33, 42, 50, 75, 109, 143, 176, 218 | 0 |
| 270° | 8, 18, 25, 33, 42, 50, 75, 109, 143, 176, 218 | 0 |

The bound is k × 9 + 15: 15, 24, 33, 42, 51, 60, 87, 123, 159, 195 and 240. The reduced grid also passed at 45°, 135°,
180°, 225° and 315°.

**Mutations, compared independently of the moved pins.** The four M3 suites as of `c6c19ea` ran against `c6c19ea`,
and the suites as of `2079e79` (plus `test_walk_v1`) ran against `2079e79`. A recorder kept every gate run's exit
code and failing rows. Result:

- 55 mutation runs exist on both sides;
- 42 fail on both sides, and none loses a failing row;
- three fail now and did not at `c6c19ea`, all stricter:
  - `cut-exit-transient` fails `paths.no-unreachable-goal` on ORACLE-F's layout;
  - `head-delayed-60-turns` fails rule (a) on its own walk;
  - `hold-slots-longer` fails the head's wait bound by one turn.

The early `diverge-second-run` mutation changed from happiness − 1 to hunger + 0.001. Since separation, one guest's
changed decisions spread to the others, and the run no longer converges. The suite still asserts what it was built
for: a divergence in minute 1 that converges by the end (the final hashes match) fails through the minute hashes.
The happiness − 1 fault is still caught; it now also changes the final hash.

The re-aimed `hold-stand-walk` (OV2-5) pins the boarding guest at the centre of the queue's front cell. In the
6-cell layout a guest enters state 13 about 0.01 cell from its stand point, so pinning it where it stands could not
create a stall. The re-aimed fault fails walk-stall (w = 20) again.

## The walk against the disassembly

- **Neighbour query `0x10087c`.**
  - The range is the guest's radius `+4` × 2 (`0xfd86c`, `0xfd870`): 26,214 = 0.4 cell.
  - The cell span is ⌈range / ONE⌉ = 1, so 3 × 3 cells, rows (y) outside and columns (x) inside, each cell checked
    by `0xd70d8`.
  - Thing types 1, 4, 5, 6, 7, 8 and 18 are kept (`0x10097c..0x1009f8`); self is skipped (`+212`).
  - The squared distance in 1/256 cells must be nonzero and below (range >> 8)² = 102², strictly (`0x100a3c`).
    Self is read from its 16.16 position `+8/+12` >> 8, others from their thing `+4/+6`.
  - The query stops after five callbacks (`0x100a50..0x100a58`).
- **Callback `0xfd5f4`.** off = self − other (`0xfcb8c` subtracts the second argument); d = |x| + |y| − min / 2. It adds
  FixDiv(off, d) (`0xfd704`) when d < r_self + r_other + `[0xecd84]` and d ≠ `[0xecd88]`. Both words are 0 in the
  static image; the initialiser `0x1002f4` stores ONE (`lis 0,1`) and 0. The port's `Radius + Radius + Fixed16.One`
  and `distance != 0` are therefore right.
- **Callback in the oracle.** The binary is compared with the port's per-neighbour step, with FixDiv stubbed as
  `Fixed16.Div`. The inputs include the boundary at 2r + ONE and a self call.
  - The review run: 605 inputs, 0 mismatches, 331 of them with a nonzero push.
  - The comparison discriminates. With the bound changed to `<=` it finds the boundary case; with a max-norm distance
    it finds 414 mismatches.
  - The witness reruns 305 of these inputs.
- **On/off rule `0xfd778`.** Separation is off on the last segment, or once arrived, within ONE + radius of the
  waypoint (`[0xecd84]` = ONE again). Otherwise the sum at `0xecd58` is the force. This matches `Step`.
- **avoid_walls.** The four edge branches end with `b 0xfe5f0` (`0xfde78`, `0xfdee4`, `0xfdf64`, `0xfdfe0`). A
  blocked edge that the probe does not penetrate falls through to the next edge. This matches
  `wallX == 0 && …` / `else if ( wallX == 0 )`.
- **Toilet term.** `0xeed34..0xeed40` compares (thing id & 3) with (counter & 3), so it runs on the guest's phase. In
  the block, trunc(`+428`) `clrlwi 24` `cmplwi 80` (`0xef0cc`, `0xef0d0`): the low byte, unsigned. This matches; the
  thing id is WALK-010.
- **Occupant lists.** They are rebuilt at the start of every park turn from the guest list, before any guest steps,
  and guests step only on turn ticks. So separation reads only state that the world hash covers.
- **GuestSpeed oracle (rerun).**
  - `walk-speed-setter`: 518 inputs, 0 mismatches against plan and C#.
  - `walk-smoothing`: 507 inputs, 0 mismatches against C#. The plan DIFFs (99, fused 2) are the known plan readings
    (F6).

## WALK-F2 (ORACLE-V2 findings) against the disassembly

| Finding | Check | Result |
| --- | --- | --- |
| OV2-1 ECON-048 | the register check lists 34 doc-only IDs; declaring one fails | holds, see WV2-2 for its reach |
| OV2-2 WritePath | `CanSpendCell`: 32-bit `(int)Balance − (int)cost ≥ 0` (`0x82bc8`) | holds |
| OV2-3 preview | `0x84530..0x84554`: a pending cost of 0 skips; else `cmpw pending, balance`, refuse when greater; the price is added before (`0x7bcc0`, plain `add`) | `CanPreviewLine` matches; the queue commit passes no economy to `CheckQueueCell` |
| OV2-4 tests | each rule reverted alone in a scratch copy, no-asset suite run | every revert fails its new test (table below) |
| OV2-5 hold-stand-walk | see the gate section | caught again |
| OV2-6 BOUNCE | `0xadf40`: ctr = `+100`; the first slot with visitor 0 takes visitor, `+4`, deadline, start; `+108` goes up; a full table or ctr 0 returns 0 | `TakeBounceSlot` matches; oracle `board-bounce` 505 inputs (holes, full table), 0 mismatches against plan and C# |
| OV2-7 smoke clock | the Totem motion (30 s) and release (60 s) limits use `Script.TimeMilliseconds`; the one-shot 180 s watchdog still fails a run whose script clock stalls | not a weakening |

Reverts (each alone; the five other failures in every scratch run read `docs/` and `content/`, which the scratch copy
does not have):

| Reverted rule | Failing new test |
| --- | --- |
| 32-bit money test → 64-bit | `TheWriteUsesSetCellTypes32BitMoneyTest`, `TheCellMoneyTestIs32BitAndANegativeCostIsBookedAsOtherIncome`, `TheQueuePreviewUsesTheValidatorAndTheCommitOnlySetCellType` |
| WritePath 64-bit test | `TheWriteUsesSetCellTypes32BitMoneyTest` |
| ECON-048 (negative cost as a cost) | `TheCellMoneyTestIs32BitAndANegativeCostIsBookedAsOtherIncome` |
| strict deadline → `<=` | `UnbounceNeedsAPassedDeadlineInsideTheFirst200MsOfEachSecond` |
| 200 ms window dropped | `UnbounceNeedsAPassedDeadlineInsideTheFirst200MsOfEachSecond` |
| first free slot → append | `BounceFillsTheFirstFreeSlotOfTheScriptsTable` |
| bounce start dropped from the bridge hash | `TheBridgeHashCoversEveryFieldOfABounceSlot` |
| preview in 64 bits | `TheQueuePreviewUsesTheValidatorAndTheCommitOnlySetCellType` |
| preview without the skip at 0 | `ThePreviewUsesTheValidatorsPendingCostTest`, the queue preview test |
| queue commit runs the preview again | `TheQueuePreviewUsesTheValidatorAndTheCommitOnlySetCellType` |

## Findings

**WV2-1 (medium, merge onto main): RIDES-029 collides with a retired ID.**

- Fork main's Theme Park Inc viewer (`6820470`) declares RIDES-029 for the shape symbol `@`.
- `docs/reverse/APPROX-TRACE.md` already uses RIDES-029 for the retired developer Totem prototype (lines 150 and 260).
- walk-fix's new guard (OV2-1) lists it as retired. After the merge onto `c09c6ba`, `fidelity_register.py --check`
  fails: "RIDES-029: declared, but docs/FIDELITY-REGISTER.md lists it as retired or reserved".
- The guard works as designed; the defect is on main. It becomes a merge blocker.
- Fix at the merge: renumber main's entry to RIDES-031 (the first free ID) in `ObjectShape.cs`,
  `RidesApproximations.cs`, `docs/OBJECTS.md` and `docs/THEME-PARK-INC.md`, then regenerate. In the trial merge this
  gives: register 208 IDs, tests without assets 1120 / 277 / 0, `--m3-gate` 16 / 0 / 0, gate hash `A07A2871552E22E8`.
- Witness: `test_rides_029_is_free_on_main`.

**WV2-2 (low, register): the retired-ID guard sees only docs table rows.**

- GATE-004 and GATE-005 are retired in M3-GATE.md prose ("`[APPROX:GATE-004]` … now retired"). GATE-002 and GATE-003
  are retired in PATH-plan and BOARD-plan prose. None of them is in the list.
- So a new GATE-005 with another meaning passes `--check`, which is what OV2-1 was meant to prevent.
- Fix: a hand-kept list of retired IDs in `fidelity_register.py` that `--write` merges into the section (at least
  GATE-002..GATE-005), or read `[APPROX:ID]` mentions in docs prose that no register declares.
- Witness: `test_prose_retired_gate_ids_are_protected`.

**WV2-3 (nit, register wording): WALK-003 does not say that non-guest things are left out.**
`0x10087c` also keeps thing types 4–8 and 18. WALK-plan §16 says so, but the register line ("separation's neighbours
are the guests outside rides and shops …") does not say that other things never push. Fix: add "thing types 4–8 and
18 are not neighbours". Witness: `test_walk_003_says_which_thing_types_are_left_out`.

**WV2-4 (low, unregistered detail): BOUNCE reads the clock twice.**

- `0xadf40` computes the deadline from one call of the clock `0x10e844` (`0xadfb0`) and the start from a second call
  (`0xadfcc`). `TakeBounceSlot` takes one `now` for both.
- They differ only when the millisecond clock ticks between the two calls. The start then lies 1 ms after the
  deadline's base, which can move the 200 ms UNBOUNCE window by 1 ms.
- The oracle's clock stub returns a constant, so it cannot see this.
- Fix: say it in the BIN label (or register it). Modelling it is not worth it.
- Witness: `test_bounce_start_reads_the_clock_again`.

**WV2-5 (nit, docs and process).**

- ORACLE.md §4's seam list still calls `TakeUnbounced` private (it is internal) and does not list `TakeBounceSlot`.
- The WALK-F2 commit message has no Tested / Not-tested lines, unlike `b10710e` and `faa64cc`. This review's reruns
  stand in for them.

## Merge readiness (trial merges, not committed)

| Onto | Conflicts | After resolving |
| --- | --- | --- |
| fork main `c09c6ba` (fetched) | `docs/FIDELITY-REGISTER.md` (generated); `DeterminismTests.cs`, one line (`saved.SoundSeed + 1`, take walk-fix's `unchecked(…)`) | `--check` fails on RIDES-029 (WV2-1). With RIDES-031: register 208, build 0 errors, tests without assets 1120 / 277 / 0, `--m3-gate` 16 / 0 / 0, gate hash `A07A2871552E22E8` |
| save-fix `3e7e07e` | `ECONOMY.md` and `EconomyApproximations.cs`: save-fix drops ECON-045 beside ECON-048, so keep ECON-048 only; `tools/fidelity_register.py` `REGISTERS`: keep WALK and SAVE; walk-fix's derived register-count sentence; `DeterminismTests.cs`, the same one line; `FIDELITY-REGISTER.md` (generated) | register 214, "twelve configured C# registers" (WV-10 closed by the derived sentence), build 0 errors, tests without assets 1081 / 274 / 0, `--m3-gate` 16 / 0 / 0, same gate hash |

**Overlap with save-fix (SAVE-M is merging it onto main now).**

- Shared files: `docs/DETERMINISM.md`, `docs/ECONOMY.md`, `docs/FIDELITY-REGISTER.md`, `SandboxSmokeTest.cs`,
  `EconomyApproximations.cs`, `ParkEconomy.cs` and `tools/fidelity_register.py`.
- `ParkEconomy.cs` and `SandboxSmokeTest.cs` merge cleanly and do not interact. save-fix changes save import and
  month-end wages there; walk-fix changes the cell money tests and the Totem clock.
- Schema: save-fix keeps `SchemaVersion` 5, so walk-fix's 7 and its pin `0x082000C95B7EB3FE` stand on the combined
  tree. The pin test passes there.
- Whichever lands second takes the resolution above. If save-fix reaches main first, main gains the SAVE register, and
  walk-fix's merge must keep both register lines.

## Runs

| Check | Result |
| --- | --- |
| Release build (SDK 10) | 0 errors |
| Tests without assets | 1094 / 273 / 0 |
| Tests with assets (`OPENTPW_GAME_PATH`) | 1282 / 85 / 0 (11 min 25 s) |
| `--m3-gate` × 2 | 16 / 0 / 0, exit 0; JSON identical apart from `wallSeconds`; gate hash `A07A2871552E22E8`, guest hash `29788288CB1437F4`; head not ready ≤ 25 / 15 / 12 of 110 / 65 / 65 |
| M3 mutation suites (`OPENTPW_M3_MUTATE=1`) | v1 26, v2 10, v3 25, v4 15, walk_v1 6: all OK |
| Mutation verdicts `c6c19ea` → `2079e79` | 55 runs; 42 failing on both, 0 lost, 3 newly failing (stricter) |
| Oracle (`differential.py`, C# bridge) | `board-bounce` 505 / 0 / 0, `walk-speed-setter` 518 / 0 / 0, `walk-smoothing` C# 507 / 0 |
| Separation callback `0xfd5f4` in the oracle | 605 inputs, 0 mismatches |
| Evidence runner (`--mac-bin`, `--pc-data …/theme-park-world/Data`, `--dotnet` SDK 8) | exit 0: 16 Python suites, 1086 tests, 117 skipped; .NET lanes passed or not-run as registered |
| Native smokes (`scripts/run.sh`, osx-arm64, one at a time, no `--mute`) | front end 448 frames, sandbox 1109 frames, `--load-original-level jungle` 5146 frames (Totem started at script time 23.9 s): all passed |
| `fidelity_register.py --check` | complete, 205 IDs; `test_fidelity_register` 10 OK |
| CRLF | `git diff --stat` equals `--ignore-cr-at-eol --stat` against `7380ab4` and `8a72a2c` |
| `git diff --check` (blank-at-eol, blank-at-eof, space-before-tab, cr-at-eol) | clean against both |
