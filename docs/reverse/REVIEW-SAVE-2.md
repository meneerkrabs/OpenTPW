# Review SAVE-V2: original-save import, correction round (save-fix 3a66365)

Scope: `899cc50` (merge of fork main `f049665` into SAVE-I `2ddf376`) and `3a66365` (SAVE-F fixes
for round-1 findings SV-1 to SV-6). Report only; no production code changed. The lane test
`source/OpenTPW.Tests/SavePayloadLayoutFuzzTests.cs` is added on the review commit.

**Verdict: ACCEPT-WITH-FIXES.** SV-1, SV-2, SV-4, SV-5 and SV-6 are resolved. SV-3 is only partly
resolved: several parsed values are still neither imported nor reported (SV2-1 to SV2-5).

## Round-1 findings

| Finding | Status |
| --- | --- |
| SV-1 duplicate-delimiter check | **Resolved.** See "Split search" below. Docs overstate what is accepted (SV2-6). |
| SV-2 silent clamps | **Resolved.** Happiness is clamped to 0–100 (`0x100F3D8C`), grade 100 becomes 99, an unknown state becomes Idle and an out-of-calendar hire time becomes tick 0; each is a `[APPROX:SAVE-003] Adjusted on import` line. A negative cost of goods keeps the attraction's defaults and is listed, and a negative grade percentage is rejected by the reader. Test: `ReportsEveryStaffAndAttractionValueItAdjusts`. |
| SV-3 unreported values | **Partly resolved.** World values, arrival bytes, calendar session start, the advisor controller words, pending-record fields and the MechanicHq/UIMsgReceiver things are now reported. Remaining gaps: SV2-1 to SV2-5. |
| SV-4 mLastBalance | **Resolved.** `docs/reverse/PPC-economy.md:870` says withdrawal `0xcbfdc` updates LastBalance after each debit at `0xcc044`, so it is not a month opening. Line 1095 uses it for the elapsed time in the red, not as a ledger opening. Opening at the current balance and reporting the word under SAVE-008 is correct. |
| SV-5 stale docs | **Resolved**, except SV2-6. |
| SV-6 merge | **Resolved.** See "Merge `899cc50`" below. |

## Split search (SAVE-011)

**Termination and cost.** `Count` is memoized per (delimiter index, block start). A block start is
either the recording end or an occurrence plus 4, so there are at most 17 × 257 states. Each state
scans at most 256 ends, about 1.1 M width checks in the worst case. Occurrence collection is 17
linear `IndexOf` passes.

Measured worst cases (each delimiter flooded 256 times, interleaved; and a 64 MiB payload):

| Payload | Time |
| --- | --- |
| Synthetic floods | ≤ 1 ms |
| 64 MiB payload | 37 ms |
| Easymode mutations | ≤ 37 ms each |

**Reject, not guess.** On a genuine save the true split satisfies every width rule, so `Count` ≥ 1.
Any other valid split makes `Count` = 2, and the walk then throws at the first delimiter with more
than one viable end. So an accepted split is always the true one. This depends on the width rules
holding for real data (Clock 8, VanillaTime 4, Camera 40, empty Coasters 16).

A genuine save is rejected only in the registered SAVE-011 case: a delimiter's bytes form a second
complete split. The most exposed case is any `CSDA` (ADSC) byte run in the trailing UI block, because
nothing follows it to break the alternative split.

**The 256 cap.** It is justified as a bound. Easymode holds each delimiter exactly once, and a random
4-byte run in a 1.6 MB payload is expected about 0.0004 times.

**Fuzz** (`/tmp` harness against the Easymode payload, seed 12345; 8,775 payloads):

| Mutation | Payloads |
| --- | --- |
| Delimiter bytes inserted into variable blocks (truth known) | 3,000 |
| Delimiter bytes overwritten anywhere | 3,000 |
| Truncations (random and ±5 bytes around every block end) | 2,209 |
| Delimiter swaps and block swaps | 272 |
| Random garbage | 300 |

| Outcome | Result |
| --- | --- |
| Accepted / rejected | 4,261 / 4,514 |
| Wrong split accepted | 0 |
| Exceptions other than `InvalidDataException` | 0 |
| Full imports of accepted payloads (48) | 41 ok, 7 rejected, 0 crashes |

The committed lane test repeats this on synthetic payloads: 2,000 injections, every truncation, all
136 delimiter swaps, and the flood cases.

## Merge `899cc50`

Of the files both sides changed (`DETERMINISM.md`, `ECONOMY.md`, `TPWS-PAYLOAD.md`, `SaveCellGrid.cs`,
`EconomyApproximations.cs`, `FrontEndSmokeTest.cs`), `f049665..899cc50` carries exactly the branch's
own delta (`c6c19ea..2ddf376`). The exception is `FIDELITY-REGISTER.md`, which is regenerated.

Every branch-only file equals `2ddf376`, every main-only file equals `f049665`, and deletions agree on
both sides. No main change is lost. `fidelity_register.py --check` gives 204 IDs at `899cc50` and 205
at `3a66365` (+SAVE-011).

## Findings

**SV2-1 (medium, SV-3) — attraction values read but not named.** `SaveAttraction`
(`SaveThingRecords.cs:24-29`) exposes these fields, which are neither imported nor reported:
- `NameLine1`/`NameLine2`
- `OperatingCapacity`, `OperatingDuration`, `OperatingSpeed`
- `QualityOfGoods`, `QueueSizeInCells`
- `CustomerCount`, `WalkAwayCount`
- `Timestamp`, `MeshInstanceId`

The SAVE-006 line (`OriginalEconomyImport.cs:183`) and its register text name only the state, chance
of winning, third gauge, histories and totals. Fix: list them, with values, under SAVE-006.

**SV2-2 (medium) — unresolved objects lose their saved economy values silently.**
`ParkObjects.ImportOriginal` places `save.UnresolvedObjects` (`ParkObjects.cs:201`). However,
`ImportObjects` iterates only placed objects and fixed items (`OriginalEconomyImport.cs:155`), and
`Level.ConnectObjectsToEconomy` (`Level.Objects.cs:440`) then registers fresh records with catalogue
defaults. The saved price, cost of goods, level, repair and life are dropped, and no evidence line
counts these objects. This is latent: Easymode and the corpus have none. Fix: import or report them.

**SV2-3 (low) — staff HQ and staff words.**
- `SaveStaffHq.ForceStrike`, `PickedUp`, `StrikeLevels` and `Trained` appear in no evidence line.
- SAVE-009 mentions strike levels only in its register text. Its evidence line is emitted only for
  nonzero budgets.
- The all-zero line "training budgets are all 0, as in a new park" reads as an import.
- `SaveStaffRecord.IdleTick`, `RoleBytes` and `RoleNextId` are not named.
- The hire time is floored to a whole turn without a note.

**SV2-4 (low) — bank and challenge words.**
- Loan offers' `Available` flag is neither verified, imported nor reported. Easymode has only offer 3
  available (`PPC-economy.md:868`).
- The challenge system's `Switch1-3`, `Value` and `ActualThing` are not named; SAVE-005 covers only
  the next event time and the current challenge.

**SV2-5 (low) — advisor import path.** The saved pending advice and history are imported only inside
`AutomaticAdvisor.AttachLevel`, which is gated at `GameFlow.cs:208`. With `--no-advisor`, or when the
advisor is unavailable, they are dropped without a line. The ADVISOR-022 report goes to `Log.Trace`,
not to `OriginalEconomyImport.Evidence`.

**SV2-6 (low, docs) — the split rule is overstated.** The `SavePayloadLayout` summary and
`docs/TPWS-PAYLOAD.md` say that delimiter bytes "inside other blocks' data are accepted". In fact
block *k*'s delimiter bytes inside any later variable block are rejected whenever the intervening
delimiters still follow in order. `SavePayloadLayoutTests` itself asserts that SPSC inside Particles
is rejected, and so is any ADSC run in the UI block. Fix: state the actual rule.

**SV2-7 (info; from main `3ecd737`, outside this branch).** `PathTiles.ForCell` reads the saved
`PathTexture` and `PathRotation` and silently replaces them when the neighbours differ. Meanwhile the
SAVE-010 text says that only "path cells, occupancy and MAP attributes" are read from the cell map.

## Runs (SDK 10.0.401, Release, `3a66365`)

| Check | Result |
| --- | --- |
| Build | 0 errors |
| Tests without assets | 1045 / 273 / 0 |
| Tests with `OPENTPW_GAME_PATH` | 1234 / 84 / 0 |
| New lane (`SavePayloadLayout*`) | 9 pass, 1 asset skip |
| `--m3-gate` | 16 pass / 0 fail / 0 unresolved, exit 0 |
| Native smokes (osx-arm64, Metal) | front-end 421 frames, sandbox 1109 frames, `--load-original-level jungle` 4325 frames: all pass |
| `run_evidence_checks.py` (`--mac-bin`, `--pc-data`, `--pc-fixture`, net8 `--dotnet`) | OK: 16 Python suites, 970 tests, 147 skipped; `lanes/save` 14/0/0 |
| `fidelity_register.py --check` and its tests | 205 IDs, OK |
| Whitespace and line endings vs `f049665` | `git diff --check` flags only the CRLF lines of three files that are already CRLF on main; no LF lines added to them; no other trailing whitespace |
