# Review ORACLE-V2: the oracle fixes 8a72a2c (ORACLE-F)

October 11, 2026. Re-review of `8a72a2c` on `447b4c7` (the PPC oracle `16bb38e` and round 1
`2495290`, merged onto fork main `8a000d8`). Round 1 is `docs/reverse/REVIEW-ORACLE.md`.

Method:

- Each runtime fix was read against a local `llvm-mc` disassembly (`lanes/review/ppcdis.py`)
  and the oracle (`differential.py`, C# bridge on the .NET 10 SDK).
- Each C# fix was reverted on its own. After each revert the no-asset suite and the matching
  oracle target ran again.
- The M3 gate and the M3 mutation suites v1–v4 ran on `8a72a2c`, with `447b4c7` for comparison.
- Trial merges went onto fork main `f049665`, save-fix `3a66365` and walk-impl `b505706`.

Witness: `tools/ppc-analysis/lanes/review/test_oracle_v2.py`. Open findings are expected
failures in it. It pins decoded fields and model results only.

## Verdict: ACCEPT-WITH-FIXES

- F1, F3, F4 and F5 match the disassembly. Every C# bridge comparison has 0 mismatches.
- The oracle tooling fixes (OV-2..OV-8) hold. The evidence runner exits 0.
- There are no blockers. Fix OV2-1 and OV2-2 before or at the merge onto main.
- OV2-3, OV2-4 and OV2-5 are needed before the oracle and the M3 mutation suites count as gate
  evidence.

## Runtime fixes against the disassembly

| Fix | Disassembly | Oracle C# | Load-bearing (revert caught by) |
| --- | --- | --- | --- |
| F1 limit polarity | `0xdcba8` `rlwinm.` bit 0x8; `bc 4,2` skips `li 100` when set | 515/0 | 4 unit tests, oracle 515/515 |
| F2 unsigned SPEED | `0xdcbe0` SPEED through the 0x43300000 unsigned magic, InitSpeed through `xoris 0x8000` | — | 1 unit test, oracle 1 |
| F2 u32 saturation | `0x1c3fbc` | — | 1 unit test, oracle 5 |
| F3 rule 7 | `0x84134..0x8414c`: word ≠ 0 refuses; `0x84ea4` stores 0 at entry and 1 before the end cell (`0x84fbc`, `0x850e0`) | 2548/0 | 1 unit test, oracle 2 |
| F4 32-bit money test | `0x82bc8` `subf r5 = balance − cost`; `srawi`/`subfc`/`adde` → sign bit | 511/0 | **oracle only** |
| ECON-046 negative cost | — | 511/0 | **nothing** (OV2-4) |
| F5 strict deadline | `0xae088` `cmpw deadline, now`; `bc 4,0` | 508/0 | **oracle only** |
| F5 200 ms window | `0xae094..0xae0b8`: magic /1000 and /200, C remainder | 508/0 | **oracle only** |
| Bounce start in hash | — | — | **nothing** in the no-asset suite |

The polarity of the 200 ms window holds for negative elapsed times too: the C remainder gives
−199..0 → 0.

## Findings

**OV2-1 (medium, register): ECON-046 is a retired ID with another meaning.**
`docs/reverse/APPROX-TRACE.md` (lines 119, 229, 321, 382) and `docs/reverse/PPC-scenarios.md`
already use ECON-046 for "upgrades need a mechanic", now contradicted. ECON-047 is taken in
`docs/reverse/RIDE-WEAR.md`. `tools/fidelity_register.py --check` does not see the clash,
because it reads C# only. Fix: renumber to a free ID, for example ECON-048.
Witness: `test_econ_046_is_not_a_retired_id`.

**OV2-2 (medium, F4 incomplete): the path writer keeps a 64-bit money test.**
`ParkPathBuilder.WritePath` refuses on `park.Balance - park.CellCost(Path) < 0` (64-bit). It
then calls `TrySpendCell` and ignores the result. This gives two wrong cases:

- Balance −2³¹ with path cost 1: refused. The original (`0x82bc8`) accepts.
- Balance 2³¹ − 1 with cost −1: the cell is written and not charged. The original refuses it.

The oracle does not see this: its bridge rebuilds the order and calls `TrySpendCell` directly.
Fix: `if ( park != null && !park.CanSpendCell( CellPurchase.Path, 0 ) ) return false;` or use
the result of `TrySpendCell`. Witness: `test_path_writer_uses_the_32_bit_money_test`.

**OV2-3 (low, unregistered deviation): the pending-line preview is not the traced validator.**
The original preview is the validator `0x84154`:

- It adds the cell price to the pending line cost with a plain 32-bit `add` (`0x7bcc0`).
- It skips the test when the pending cost is 0 (`0x84534`).
- Otherwise it refuses when `pending > balance`, a signed compare with no subtraction
  (`0x84550`/`0x84554`).

The extension in `8a72a2c` applies SetCellType's wrapping subtraction to the preview:
`(int)Balance − (int)((pending+1)·cost) ≥ 0`. It agrees off the edges, and differs here:

- Balance −2³¹ with cost 1: the validator refuses, the C# preview accepts.
- Balance 2³¹ − 1 with cost −1: the validator accepts, the C# preview refuses.
- A zero total with a negative balance: the validator accepts, the C# preview refuses. This
  case existed before `8a72a2c`.

PATH-plan §4 says the preview and the commit are separate tests. So "previews use it to match
the commit" is not the original's design. Fix: model the preview on `0x84550` (a 32-bit pending
sum, skip at 0, refuse when pending > balance), or register an APPROX. The path preview
`ParkPathBuilder` line 169 needs the same fix. It is 64-bit and has no skip at 0.
Witness: `test_queue_preview_follows_the_traced_validator`.

**OV2-4 (low, test coverage): the new branches are guarded only by the private-binary oracle,
or not at all.**

- Reverting the 32-bit money test, the strict UNBOUNCE deadline or the 200 ms window fails no
  C# test. Only the oracle catches them, and the oracle needs the Feral binary, which CI does
  not have.
- Reverting ECON-046 (`Post(OtherCosts, cost)` with cost < 0) fails nothing. The only
  negative-cost oracle input (balance 2³¹ − 1, cost −1) is refused before the spend.
- Dropping the bounce start from the bridge hash fails nothing in the no-asset suite.

Fix: add synthetic unit tests for `TakeUnbounced` (the deadline and window edges of the oracle
inputs), `CanSpendCell` and `TrySpendCell` at ±2³¹ and with a negative cost, and a hash test
with a live bounce slot.

**OV2-5 (medium, gate evidence): the M3 mutation suites were broken by the slot shape and were
not run.** The v3/v4 hooks cast `bouncing` to `List<(int, double)>`. On `8a72a2c` this gives an
`InvalidCastException`:

- every v4 run crashes, because `MutateV4` casts on every tick;
- the three slot mutations of v3 crash.

The review commit fixes the hooks by shape only (the start is carried through). With that fix,
v1 and v2 pass, and v3/v4 have 5 differences from the expectations:

| Mutation / test | 447b4c7 | 8a72a2c | Reading |
| --- | --- | --- | --- |
| `test_head_bound_is_n_plus_four_cells` | 194 | 84 | number moved (derived, stricter; see below) |
| `head-delayed-170-turns` | FAIL by the wait bound | FAIL by rule (a), 85 > 84 | still caught; it no longer isolates the wait bound |
| `cut-exit-transient` | no extra row | also fails `paths.no-unreachable-goal` | stricter |
| `hold-slots-longer` (expected PASS) | PASS | FAIL, head 179 > W(0) 178 | number moved: release now waits for the 200 ms window; the mutation is tuned one turn from the bound |
| `hold-stand-walk` | FAIL (walk-stall 21 > 20) | **PASS** | **no longer caught** |

How `hold-stand-walk` stops biting: in the 6-cell layout the boarding guest enters state 13
about 0.01 cells from its stand point and is Using one turn later. Pinning it creates no stall.
Holding a guest that newly enters state 13 also passes. The gate's walk-stall code did not
change. The mutation no longer reaches a stand walk. Fix: aim the fault at a guest with a real
stand walk, or move the pinned guest back. Then re-pin the moved numbers.

The 194 → 84 bound change is derived and stricter. `HeadNotReadyBound` did not change; only its
input changed (N = ⌈21/4⌉ = 6). (6 + 4) / 0.7 cells/s = 14.29 s = 58 turns, and
58 + 3 + 22 + 1 = 84. The baseline max is 26.

**OV2-6 (low, pre-existing, unregistered): BOUNCE appends instead of taking the first free
slot.** BOARD-plan §5 (`0xadf40`) fills the first free slot. UNBOUNCE scans slots in index
order. The bridge appends to a compacted list, so after a hole the release order differs. There
is also no `+100` slot cap. Witness: `test_bounce_takes_the_first_free_slot`.

**OV2-7 (info, pre-existing): the jungle smoke's wall-clock Totem check.**
`SandboxSmokeTest` requires a release within 60 s of *wall* time after motion
(`MaximumSecondsUntilRelease`, Stopwatch). The same check is on `f049665`. Unloaded, the
release comes after 44 s on both `8a72a2c` (2 runs) and `f049665`. A 27 % frame-rate drop makes
it fail. Minimal non-weakening fix: measure the 60 s in the ride script's clock
(`Script.TimeMilliseconds − motionScriptMilliseconds`), the time the script actually waits.
Keep the 180 s wall-clock watchdog as the hang guard. The same applies to
`MaximumSecondsUntilMotion`.

## Witness edits

`test_queue_v1` and `test_path_v1` changed polarity only:

- every expected value is kept, and only the HasQueue / last-cell argument flips
  (`test_witness_edits_only_flip_the_has_queue_polarity`);
- nothing was weakened;
- `test_oracle_v1` lost five `expectedFailure` markers for OV-2, OV-3, OV-4 and OV-6, and those
  tests now pass.

## Runs

- Release build: 0 errors.
- Tests without game data: 1063 passed, 272 skipped.
- Tests with game data: 1250 passed and 1 failed. The failure is
  `ParkSaveAndLoadThroughTheLevelPathRestoreEveryStream`, which `f049665` fixes.
- Evidence runner (`--mac-bin`, `--dotnet` SDK 8): exit 0, 16 suites, 1031 tests.
- Oracle: all C# comparisons agree. The plan DIFFs for walk-smoothing and coast are the known
  plan readings.
- `--m3-gate`: 16 pass, 0 fail, 0 unresolved, exit 0. Queue 21/21, head not ready ≤ 26 of 84
  turns, gate hash `8ADCD47C727CC3F2`, as documented.
- Jungle smoke: passes 2 of 2 unloaded.
- `fidelity_register.py --check`: complete, 197 IDs.
- `git diff --check` vs `447b4c7`: clean, with no CRLF changes.

## Merge readiness

| Onto | Result |
| --- | --- |
| fork main `f049665` | clean. Full asset suite on the merged tree: 1251 passed, 0 failed; no-asset suite 1063 passed |
| save-fix `3a66365` | conflicts in `EconomyApproximations.cs`, `ECONOMY.md` and `FIDELITY-REGISTER.md`. They are textual: SAVE-F drops ECON-045 next to the new ECON-046 line. Keep ECON-046 (renumbered, OV2-1) and regenerate the register |
| walk-impl `b505706` | conflicts in `DETERMINISM.md`, `FIDELITY-REGISTER.md`, `M3-GATE.md` and `DeterminismTests.cs`. The clashes are semantic, see below |

The walk-impl clashes:

- Both branches bump the schema 5 → 6 with different content.
- WALK-I replaces `HeadNotReadyBound`. With N = 6 its formula gives
  6 × 9 + 2 × 15 + 3 + 22 + 1 = 110 turns, not 84 or 281.
- WALK-I's pinned queue numbers assume the limit 100 (queue max 48/100, J at p = 99, W_max).

WALK-F already resolved these in `b10710e` (schema 7, one bound), along with the harness slot
shape and the re-pins (`faa64cc`). OV2-1, OV2-2, OV2-3, OV2-6 and OV2-7 are still open on
`faa64cc`.

## Resolution on walk-fix (WALK-F2)

The witness `test_oracle_v2.py` pins the subject `8a72a2c`, so its expected failures stay
expected. The fixes on walk-fix:

| Finding | Resolution |
| --- | --- |
| OV2-1 | Renumbered to **ECON-048**. `fidelity_register.py` now lists every ID that a docs table row uses but no register declares (34 IDs, ECON-046 and ECON-047 among them) under "Retired and reserved IDs". Declaring a listed ID fails `--check`, and `--write` never drops an entry. |
| OV2-2 | `WritePath` refuses through `ParkEconomy.CanSpendCell`, SetCellType's 32-bit test. A commit runs no other money test, because the path `Validate` skips money when it commits. |
| OV2-3 | Fixed, not registered. The path and queue previews use `ParkEconomy.CanPreviewLine` (`0x84550`): a 32-bit pending sum, 0 passes, refused when the sum is greater than the balance. The queue commit (`BuildQueueCell`) no longer runs the preview test, so only `TrySpendCell` decides. |
| OV2-4 | New C# tests. Each one fails when its rule is reverted: the 32-bit money test (3 tests), ECON-048, the strict deadline, the 200 ms window, the bounce start in the bridge hash, first-free-slot BOUNCE and the 64-bit preview (2 tests). |
| OV2-5 | `hold-stand-walk` pins the boarding guest for 30 turns at the centre of the queue's front cell, one cell back from the stand point. It fails `queues.no-stuck-queue` through walk-stall again. The v4 freeze hook counts occupied slots, and the v3 hold hook skips free slots. |
| OV2-6 | Traced (`0xadf40`). BOUNCE takes the first slot with visitor 0 among the header's bounce-size slots. With every slot taken it stores nothing and returns 0. UNBOUNCE clears the slot in place. New oracle target `board-bounce`: 505 inputs agree with the binary, the plan and C#. |
| OV2-7 | The Totem motion limit (30 s) and release limit (60 s) are measured on the ride script's clock. The 180 s wall-clock watchdog still fails a run that stops advancing. |

The gate hash did not change: `A07A2871552E22E8`, with raw guest hash `29788288CB1437F4`. So the schema stays
at 7.
