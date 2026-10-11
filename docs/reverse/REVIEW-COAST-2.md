# Review COAST-V2: re-review of COAST-F (coast-fix 36a52b0 + 8f01c3d)

October 11, 2026. A focused, independent re-review of branch `coast-fix` on `02712e0`:
`36a52b0` (cherry-pick of the COAST-V review `4d902cd`) and `8f01c3d` (COAST-F, the fixes for
CV-1 to CV-16). Every claim in the resolution table of `REVIEW-COAST.md` was checked against the
disassembly of the identified Feral `SimThemePark.data`. The pure routines ran in the PPC
differential oracle (oracle-fix interpreter) against the real `coast-fix` C#. The harness was a scratch console and
Python driver under `/tmp`, outside Git. Nothing else original was executed. Witness:
`tools/ppc-analysis/lanes/review/test_coast_v2.py`. Open findings are expected failures in its
`Source` class.

**Verdict: ACCEPT-WITH-FIXES.** COAST-F fixes all of CV-1, CV-2, CV-3 (node count and flags), CV-4, CV-6
and CV-16, and the rating is bit-exact under the oracle on 57,000 step inputs. Two of its
new ports read the binary wrongly, and both change what players see:

- **CV2-1 (blocker).** The partial-hop speed floor added for CV-5 does not exist. The binary
  overwrites the floored value.
- **CV2-2 (blocker).** The vertical knot of the station end node points the wrong way, which changes every coaster's
  geometry next to the station and its rating.

Both are one-line fixes plus a test change. **CV2-3** (minor) still owes the
precision row that CV-11 asked for. The merge work (CV-15, schema) is unchanged and is listed
below.

## Claim verification

| COAST-F claim | Verdict | Evidence (code-section offsets) |
| --- | --- | --- |
| CV-1: moves and the slider pass no `CostPerUnit` | CONFIRMED | `Settle` uses `NodeCost(…, chargeUnit: false)`; `0x4b284 cmpwi r25,0` gates `lfs 92(r31)`; callers per `test_coast_v1` |
| CV-2: COAST 4 maps 0/1/2 → 16/64/32; out of range does nothing; breakdown coasts | CONFIRMED | `0x3de0c..0x3de54`: 1 → 64, 2 → 32 (`< 3`), 0 → 16 (`>= 0`); negative numbers and 3 or more branch to `0x3de58`. C# `parameter is >= 0 and <= 2`. P = 0 in mode 64 gives floors of 0, and `CanDispatch` stops departures |
| CV-3: one end node, flags 0x144 | CONFIRMED (count and flags) | `0x37e34 li r7,0x144` behind definition bit 0x20, which the loader always sets (`0x301f4`) |
| CV-3: "a pylon-less loop node's knot runs vertically" | **REFUTED in sign** | CV2-2 |
| CV-4: forced place debit; affordability test named under COAST-028 | CONFIRMED | `Spend(…, force: true)`; COAST-028 row and tag |
| CV-5: half-sample winch test | CONFIRMED | car point = trunc(fmod(s_car + ½, n)) (`0x4093c..0x4098c`), and the winch test reads that sample's node flag 0x800 (`0x40a0c..0x40a18`) |
| CV-5: friction fixed per frame | CONFIRMED | `0x40c38..0x40c78`: reference car (train +44) +64, written only at `0x41094` after the hops |
| CV-5: strict braking start | CONFIRMED | `0x409ec fcmpo s_car, ctl+308`; CR GT is extracted (`0x409f8`) and counted in r17 |
| CV-5: braking sample `(tail +252) − 1` | CONFIRMED with an edge case | `0x40070..0x400a4`; +252 = `__dtou` of the sampler coordinate (`0x45700..0x457a0`). See CV2-4 |
| CV-5: partial-hop floor at M (`0x40d78..0x40dc8`) | **REFUTED** | CV2-1 |
| CV-5: winch halving, inactive cars, reference car named in COAST-022 | CONFIRMED | register row and tag |
| CV-6: half difference only on loop pylons, `\|b\|·½(â − b̂)/SD` with 0x80 clear | CONFIRMED (line by line) | `0x31a6c..0x31a7c` gate on pylon 0x4000. In the 0x80-clear branch `0x31b18..0x31b9c`, node +52 = ½(û₁₀₄ − û₁₀₀)/SD·\|b\| (f30 = \|+100 neighbour\|). Neighbour +100 is the next node (the `b/SD` branch `0x31ba4` stores it into +52) |
| CV-6: Y zeroing on a non-loop pylon next to a loop pylon | CONFIRMED | `0x31be0..0x31c54` (own pylon present and not 0x4000; the +104 or +100 neighbour's pylon is 0x4000) zeroes +44 and +56. The C# zeroes after both the plain straight and the curve case and returns early on loop pylons, which matches |
| CV-16: force `−((p₋−p)+(p₊−p))/spacing·(v/10)` | CONFIRMED | `0x3fcf0`/`0x3fcf8` neighbour − current; `fadds` → `fdivs` spacing (`0x3fdd0..0x3fdf4`); × `fneg g` (`0x3fe04..0x3fe20`); `fdivs v,10` then `fmuls` (`0x3ff60..0x3ff90`) |
| CV-16: max height and speed start at 0.0; first and last samples | CONFIRMED | `0x3fc90 lfs f17,−13460` = 0.0 → +264/+328; sample 0 force 0 and speed fWinchSpeed (`0x4001c..0x40038`); n−1 ← n−2 (`0x4003c..0x4006c`) |
| CV-16: trough flag cleared | CONFIRMED | `0x3fdc0..0x3fdc8`. The normal is mixed precision (`0x3fd5c..0x3fd7c`), and `MixedLength` matches it |
| Speed step of the pass | CONFIRMED | uphill uses def +40, else +44 (`0x3fe30..0x3fe88`); max fWinchSpeed, friction ×+84, max again, winch flag on equality, min ctl+52. C# `NextSpeed` matches op for op |
| COAST-020 retired: cost-rate fallback ported | CONFIRMED | `0x4b294..0x4b2d4`: own pylon & 0xC000 with no pylon below, or a below pylon without 0xC000 → StdPylon (`or r31,r29`); then 0x4000 with the previous node's 0x80 clear → StdPylon; rates `96/100(r31)`, unit from the own pylon (`0x4b28c`, before the swap), height from the own pylon (`0x4b20c`). The length is `sqrt(fmadd(dx,dx,dz²))`, the same as the C# |
| CV-10: knot sides | CONFIRMED | `0x326dc..0x327dc`: back side `f27·f29` (K) always; forward K when the next node (+100) has 0x40, else 1.7 (−13808) |
| CV-10: sample record before its pair | CONFIRMED with an edge case | `0x45320..0x4535c`. See CV2-4 |
| CV-11: fused pylon height, window, derivative | CONFIRMED (oracle for the height and the derivative) | see Oracle |
| CV-11: "tangent and chord norms stay single precision" | not registered | CV2-3 |
| CV-12, CV-13, CV-14, CV-8, CV-9, CV-7 → COAST-030, COAST-031 | CONFIRMED | `test_coast_v1` (20 tests pass); register rows present |
| Rating step and final: fused decay, NaN keeps the old peak, final `fmadds` and clamp | CONFIRMED (oracle) | see Oracle |

## Oracle (this review)

The oracle-fix interpreter (`Image`/`Sandbox`) ran the original routine against a scratch .NET 10
console that references `coast-fix`'s `source/OpenTPW`. Results were compared as raw binary32 bit patterns, and
NaN inputs are canonical quiet NaNs.

| Routine | Inputs | Mismatches |
| --- | ---: | ---: |
| Step `0x415b4`, realistic | 46,407 (replays of the rated samples of 225 tracks: the 12 PC coasters × 3 circuits × 5 start heights × loop on/off) | 0 |
| Step, oracle distribution (`coast_step_inputs`) | 6,009 | 0 |
| Step, stress (±0, subnormals, FLT_MAX, ±inf, NaN in the state, speed, bank and force; bank π/2 ± 1 ulp × Fy 0.1 ± 1 ulp) | 4,479 | 0 |
| Final `0x41900`, realistic / distribution / stress | 225 / 6,006 / 3,006 | 0 |
| Derivative `0x41c0c` (leaf routine) | 3,300 | 0 (the pre-COAST-F unfused Bernstein form differs on 1,587 of 2,000) |
| Pylon height `0x369bc` | 2,000 | 0 |

The tangent `0x31450` and the motion tick `0x40650` are not isolated (they call the import glue for
`sqrt`/`acos` and the pose code), so I checked them line by line (claim table, CV2-1).

## Findings

**CV2-1 (blocker). The partial hop has no speed floor. COAST-F's port contradicts the
instructions it cites.** `0x40d78..0x40d98` stores `v_end < M ? M : v_end` into train +120. `0x40d9c..0x40dc8`
then reloads the *unfloored* v_end from `276(r1)` (not +120), selects `cap < v_end ? cap : v_end` and
stores it into train +120 again. No instruction reads +120 between the two stores. So the binary's result is
min(cap, v_end), and the first store is dead. The `02712e0` code (`speed = MathF.Min( end, SpeedCap )`)
was right. `CoasterRide.cs:513` (`end = end < minimumFloor ? minimumFloor : end;`) adds a floor
the original does not have. **Scenario:** a train leaves the station at speed 0 with P = 1 (M = fMinSpeed = 5).
The first frame's hop is partial (τ = 1.5/((0 + 5)/2) = 0.6 s against a budget of 1/60 s), so α ≈ 0.028.
The original ends the frame at v ≈ 0.14 (then × 0.95 below 2.0) and builds up speed over the
following frames. The C# jumps to 5.0 in one frame. The same happens after every decay below M. This changes dispatch
spacing, lap time and train positions, which the state hash covers. **Fix:** remove the line (back to
`speed = SpeedCap < end ? SpeedCap : end`), correct the `[BIN:…0x10040650]` tag and the docs that
repeat the floor (COASTERS.md:110, COAST-plan line 835), and add a test that a departure from 0 does not reach M
in one frame.

**CV2-2 (blocker). The station end node's knot points down, not up.** Knot direction for a
pylon-less node (`0x32dd8..0x32e08`): f29 = 0.0 (−13628), f30 = +1.0 (−13648), f31 = −1.0
(−13788). Node flag 0x40 clear → the stored direction (`0x32e0c`). 0x40 set and **0x80 clear**
→ `0x32dfc`: (0, **−1**, 0). 0x40 and 0x80 set → (0, +1, 0). The end node carries 0x144 (0x40 set,
0x80 clear), so its direction is (0, −1, 0). The C# (`CoasterTrack.cs:680`, `SVector3.UnitY`) and its
own tag ("(0, 1, 0) when its node has flag 0x40 ((0, −1, 0) with 0x80)") have the branch
polarity backwards. The test `ClosingThenCompletingAppendsTheStationEndNodes` pins the wrong value.
The direction reaches `0x32298` as `r4 = &76(r1)` and is stored unchanged as knot +40 (−u at +28),
the same convention the C# `KnotControls` uses, so the sign carries straight into the curve. **Effect**
(scratch, `PcSettings` circuit at height 40, only the end knot's direction changed): with +Y the
closing pair rises to 14.7 above the station level of 10. With −Y it dips to 3.5. Excitement
39.01 → 33.96, sickness 6.08 → 3.66, lap 46.87 s → 47.22 s, 146 → 145 samples. Every completed
coaster changes the same way. **Fix:** `-SVector3.UnitY` (and `+UnitY` only if node flag 0x80 is ever
modelled), correct the tag, the test and COAST-plan §13.4 (lines 597, 643, which carry the same inversion), and re-pin the witness ratings in `CoasterTests` and the M4 gate.

**CV2-3 (minor). CV-11's remaining precision is still unregistered.** CV-11 asked to "register
one precision row or port them". COAST-F's resolution says "the tangent and chord norms stay single
precision", but no register row or tag says so. Still unported: the chord `0x32990` (`fmsubs`
0.7·b.y − 0.7·a.y at `0x32a64`, then double `fmadd` and `sqrt`, `frsp`), the node tangent's unit vectors and lengths
(`0x3182c..0x318d8`, double `fmul`/`fadd`; lengths `fmadd` at `0x318f0`/`0x31918`), its fused
dot product (`0x31958`, `0x3196c`), the knot-control norms (`0x326dc..0x32760`), and the motion's
interpolated car points `fmadds(cur, 1 − f, next·f)` (`0x409b0..0x409e4`) against `a + (b − a)·f` in
`SampleAt`. All of them feed geometry, and through it the rating. **Fix:** one COAST row (for example
"COAST-032 PRECISION: …") with a tag at each site, or port them. `MixedLength` already exists for the norms.

**CV2-4 (minor, edge cases; tag or register).**
- `BrakingSpeedSample` returns `first − 2`, which assumes the tail's first sample lies strictly past its knot.
  When a sample falls exactly on the knot (t = 0 after the CV-10 move), node +252 truncates to that
  sample, and the original reads `first − 1`.
- `Emit` stops moving a negative-t sample at pair 0. The binary adds 1 to t even when the
  previous link (+20) is null (`0x45320..0x45350`).

Both are float-equality or first-pair edges, and none appeared in the 225 tracks. Name them in COAST-022/COAST-019 or handle them.

No new finding in money, COAST 4, the validator rules (COAST-030), the rating step/final or the
rating pass: these are as COAST-F says.

## Merge readiness (trial merges with `git merge-tree`; nothing committed)

`origin/main` fetched at `c09c6ba` (merge base `c6c19ea`):

| Onto | Conflicts |
| --- | --- |
| origin/main `c09c6ba` | `docs/DETERMINISM.md`, `docs/FIDELITY-REGISTER.md` (regenerate), `source/OpenTPW/Hud/ParkHud.cs`. `WorldStateHash.cs`, `DeterminismTests.cs`, `Level.cs`, `RideScriptWorld.cs`, `ParkEconomyContracts.cs` auto-merge |
| anim-fix `4b55686` | `DETERMINISM.md`, `FIDELITY-REGISTER.md`, `DeterminismTests.cs` (pin), `OriginalObjectRuntime.cs` |
| ctrl-impl `aebd85f` | as anim-fix plus `WorldStateHash.cs` and `tools/fidelity_register.py` (`REGISTERS`: CTRL, GATEM4) |
| walk-fix `bdf3dde` | `DETERMINISM.md`, `FIDELITY-REGISTER.md`, `DeterminismTests.cs`, `ParkHud.cs`, `WorldStateHash.cs`, `fidelity_register.py` (WALK) |
| save-fix `3e7e07e` | `DETERMINISM.md`, `FIDELITY-REGISTER.md`, `ParkHud.cs`, `fidelity_register.py` (SAVE) |
| m4-gate-fix2 `3aba19d` | `FIDELITY-REGISTER.md` only |

**Schema.** main is at 5. coast-fix and ANIM-I (`b84bf58`, `4b55686`) both claim **6**. CTRL-I
(`aebd85f`) and walk-fix (`bdf3dde`) both claim **7**. The numbers must be assigned in landing order at
integration (6, 7, 8, …), with each pin recomputed on the merged tree and a DETERMINISM.md history line. A
textual auto-merge of `WorldStateHash.cs` (as onto main and anim-fix) does not catch the collision.

**Register IDs.** COAST-F adds only `COAST-*` IDs; no RIDES/ANIM/CTRL ID is touched. No other branch
declares a COAST ID except m4-gate-fix2, which carries COAST-I's rows. COAST-030/031 were not keyed by any
doc before. COAST-020/021 are retired and still named in COASTERS.md as traced, so walk-fix's
reserved-ID rule will keep them from being reused. The known collisions (ANIM, CTRL and main on
RIDES-029/030/036–039) do not involve this branch. The `REGISTERS` table conflicts with
CTRL/WALK/SAVE are mechanical: keep every key.

**M4 gate.** Trial merge into m4-gate-fix2 `3aba19d` (throwaway worktree; register regenerated,
213 unique). `--m4-gate`: **14 pass / 14 fail / 0 unresolved**, the same as its baseline, determinism equal
(`80859F24D1C0C589`). `coasters.construction` still reports **TrackCharged 0 of 12**. The ledger
now books the traced amounts (move: OtherCosts +65, OtherIncome +70). The gate itself still computes its
*expected* move charge with `NodeCost` defaulting to `chargeUnit: true` ("expected charge 105"). That is
a gate-side update for the integration: use `chargeUnit: false` for moves. The witness ratings need new
pins after CV2-2.

## Runs (this review, worktree at `8f01c3d`)

| Check | Result |
| --- | --- |
| Release build (SDK 10) | 0 errors |
| Tests without assets | 1074 passed, 269 skipped, 0 failed |
| Tests with `OPENTPW_GAME_PATH` | 1259 passed, 84 skipped, 0 failed (19 min 56 s) |
| `--m3-gate --minutes 30` | 16 pass / 0 fail / 0 unresolved; determinism equal (gate hash `AFDCA347E564786E`) |
| `run_evidence_checks.py` (net8 SDK for the .NET lanes) | OK: 16 Python suites, 1033 tests, 160 skipped (`lanes/review` 425 ran, before `test_coast_v2`) |
| `fidelity_register.py --check` | complete, 207 unresolved unique IDs |
| CRLF / whitespace vs `02712e0` | `--stat` equal with and without `--ignore-cr-at-eol` (17 files, +1179/−291); `diff --check` clean |
| `test_coast_v1` / `test_coast_v2` (with `OPENTPW_MAC_BIN`) | 20 OK / 16 OK, 3 expected failures (CV2-1, CV2-2, CV2-3) |
| Native smokes (`-r osx-arm64`, **unmuted**, one at a time) | jungle `--load-original-level`: passed, 4338 frames, "Temple Of Gloom with 6 pylons for $797, 146 samples, excitement 38.9, sickness 5.7, 1 train(s)"; sandbox: passed, 1109 frames; `--front-end`: passed, 453 frames. Another session's jungle smoke was already running when the jungle smoke started (it was not started by this review); the sandbox and front-end smokes waited until no other smoke ran |

## Resolution (COAST-F2, branch `coast-fix`)

| Finding | Resolution |
| --- | --- |
| CV2-1 | Fixed: the partial hop ends at `min(cap, v_end)` with no floor (the floor line is gone, the `0x10040650` tag names the dead store); COAST-plan §17.2 step 4 and COASTERS.md corrected; `ADepartureFromRestDoesNotReachTheMinimumSpeedInOneFrame` fails with the floor (speed 5 after the first frame) and passes without it |
| CV2-2 | Fixed: a pylon-less node with flag 0x40 (OpenTPW sets no node flag 0x80) gets the knot direction `(0, −1, 0)` with +0.0 in X and Z, like the original's f29; the tag, the `Loop` flag doc, `ClosingThenCompletingAppendsTheStationEndNodes` and COAST-plan §13.1/§13.4 corrected |
| CV2-3 | Registered as COAST-032 (chord, node tangent unit vectors, lengths and dot product, knot-control norms) with a tag at each site. The car-point interpolation is ported instead: `SampleAt` uses `fmadds(cur, 1 − f, next·f)` (`0x409b0..0x409e4`) |
| CV2-4 | Ported: `BrakingSpeedSample` returns `first − 1` when the tail's first sample has t = 0; `Emit` adds 1 to a negative t at pair 0 without moving the record, as the binary does when the +20 link is null (the loop `0x45320..0x4535c` re-tests t) |

The witness `test_coast_v2` passes without expected failures. The oracle rerun on the fixed tree (the same
scratch harness, rebuilt against `coast-fix`, new realistic inputs from 225 tracks) is bit-exact: rating step 46,206
realistic / 6,009 distribution / 5,479 stress inputs, final 225 / 6,006 / 3,006, derivative 3,300, pylon height 2,000.
