# Review COAST-V: player-built roller coasters (515925b, 02712e0)

October 10, 2026. Independent review of the coaster stack on `de96a43` (COAST-R plan):
`515925b` (COAST-R2, cherry-pick of `32dfe49`: curve, sampling, build money, motion) and
`02712e0` (COAST-I: `World/Coasters/*`, HUD tool, smoke, tests, register).

All binary results are static readings of the identified Feral `SimThemePark.data`, plus the
PPC differential oracle of `16bb38e` for the two pure rating routines. Nothing else original was
executed. Witness: `tools/ppc-analysis/lanes/review/test_coast_v1.py`. It uses `test_queue_v1`'s
PEF reader and field decoder, not the COAST lane witnesses. Open findings are expected failures
in its `Source` class.

**Verdict: REJECT in this form; ACCEPT-WITH-FIXES once CV-1 to CV-6 and CV-16 are fixed (or, where the
section says so, registered) and the branch is rebased (CV-15).** The traced core holds:
loader, validator order, blocked cells, height clamp, cost rounding, the money call sites
(delete refunds nothing), the sampler, the sine table, the Bézier, auto-bank, the rating step and
final (bit-exact under the oracle on realistic inputs), the train count, guest excitement and most of the motion arithmetic.
Seven deviations are not registered, or contradict the trace they cite. Two of them change money
(CV-1, CV-4), one changes what scripts do to moving trains (CV-2), two change the track geometry (CV-3,
CV-6) and one changes every rating (CV-16).

## Claim verification

| Claim | Verdict | Evidence (code-section offsets) |
| --- | --- | --- |
| Loader copies floats only when nonzero; cost rates and fEntranceSizeInterval unconditional | CONFIRMED (+ more unconditional fields) | `lfs`/`fcmpu 0.0`/`bt` per field; pylon +92/+96/+100 at `0x2fa6c..0x2fa80`; def +796 ← 15220 at `0x2ffb0`. Also unconditional: pylon +88 (`0x2f8b4`, must be nonzero or the load fails at `0x2f838..0x2f860`) and the car-type floats (`0x2fce8..0x2fcfc`, `0x2fdb0..0x2fdc8`) |
| Loader flags; bIsSuspendedCoaster sets 0x8 and clears 0x10 | CONFIRMED | `0x30194` clears 0x4000 / `0x301a0` sets 0x8000 (trough), `0x301f4 ori 0xA0`, 0x20, 0x10, then 0x8 and `0x30248 rlwinm 0,0,0,28,26`. C# `CoasterDefinition.cs:268-286` matches |
| Defaults and degree conversion | CONFIRMED, one exception | fWinchSpeed 5, 80/130, 15000 (`0x2d280`), def+16 = 4.0 (`0x2d2d0`), `angle × single(π/180)` bit-identical. Exception: CV-12 (LoopPylon +172) |
| Validator `0x36ba4` test order | CONFIRMED | bounds `0x36ca8..0x36cc0`; bit 0 for new pylons only `0x36cd0..0x36cec`; pylon choice; same cell `0x36e74`; d² `0x36e94..0x36ed4` (waiver `0x36ebc..0x36ed0`, COAST-007); turn window `0x36ef8..0x36ffc`; stacking `0x37064..0x37298` (other coaster `0x370e4..0x370f4`); next two links `0x37318`, `0x373b4` |
| Validator marks node + predecessor 0x4000, never refuses | CONFIRMED for the build tick, UNVERIFIED at the tool layer | set `0x48334..0x48368`, clear `0x482f8..0x4832c`. The tool engine also calls the validator (`0x70400`, `0x70604`) and stores `result == 0` in the global `0x7c50c`, which the affordability check sets as well (`0x6f678`). Its consumers (`0x70db0`, `0x7118c`, `0x712a8`) are not traced, so the click may still be refused there. C# marks both nodes (`CoasterTrack.cs:322-326`) but re-runs every link on each edit |
| Blocked = no-ground, frozen at start; other coasters' pylons block | CONFIRMED | six loads of `*0x66f78` (`0x36cd0`, `0x4daa8`, `0x4dd28`, `0x54eec`, `0x99c98`, `0x99d34`); refresh `0x54ee0 → 0x4dcb8` has one caller in `0x11b478`; bit 0 → FLT_MAX at `0x5052c..0x50538`. "Game start" for `0x11b478` itself is medium (one caller, `0x11b39c`) |
| Height clamp | CONFIRMED | `0x30f20..0x30f44` raises +104 to h0; `0x36990` has no clamp inside, the callers clamp (`0x35004..0x35038`, `0x350f8..0x3512c`, min/max `0x3509c..0x350dc`). Pylon height `0x369c8` is `fmadds` (CV-11) |
| Cost `0x4b168`, rounded up via `modf` | CONFIRMED | `÷10.0`, `fmadds` at `0x4b2e0/e4`; `0x41aa4`: fraction ≠ 0 adds 1, negatives included (−2.5 → −1), same as C# `RoundUp`. The unit term is gated by r4 (`0x4b188 addi 25,4,0`, `0x4b284 cmpwi 25,0`), see CV-1. The COAST-020 fallback is decodable now: a loop/trough pylon takes the Std length/height rates when there is no pylon below or it is not 0xC000 (`0x4b294..0x4b2b4`), or when the previous node lacks flag 0x80 (`0x4b2b8..0x4b2d4`) |
| Place trunc; move refund trunc(old), charge ceil(new); same cell free | CONFIRMED, but the C# charges the unit on moves (CV-1) | place `0x378a0` balance getter (result unused), `0x378c0` debit; drop: credits `0x4a644`/`0x4a660`, debit `0x4a674`, next node only on a complete track `0x4a5c4`, `0x4a6c4`/`0x4a6d8`; same cell `0x4a5a8..0x4a5bc` skips all money; slider credit `0x462ac`, debit `0x462c0` |
| Delete refunds nothing | **CONFIRMED** | every `bl` in `0x2d000..0x4bd64`: credit `0xcbf50` only at `0x462ac`, `0x4a644`, `0x4a660`, `0x4a6c4`; debit `0xcbfdc` only at `0x378c0`, `0x462c0`, `0x4a674`, `0x4a6d8`; no `bctrl` in the module. The accumulator calls (`0x35be0/f0`, `0x4838c/94`, `0x48c78/80`) are display only. Selling the station: scrap `0xe25e8` (credited at `0xdc070`) = scrap% × catalogue cost / 100, no track field. Money-off gate +36 at `0x378a4`, `0x46258`, `0x4a59c` |
| Closure and completion | CONFIRMED | build click `0x73a3c..0x73a9c`: action 129 == 0 → `0x37ef0(0)`, else `0x45f5c` → `0x375c4` → close `0x376cc → 0x470c4`. `0x37ef0` is **unconditional** (no 0x4000 or closure test) |
| End nodes: one with bVisibleEndPylon, two otherwise, first with 0x144 | **REFUTED in part** | `0x37cec`/`0x37e2c` test definition bit 0x20: set → 1 node with flags 0x144 (`0x37e34`), clear → 2 nodes with flags 4 (`0x37e3c`). The loader always ORs 0xA0 (`0x301f4`); no clear of 0x20 found. So the original **always** appends one end node with 0x144 (CV-3) |
| Curve: Hermite/Bézier, 0.7 height weight | CONFIRMED (precision differs, CV-11) | 0.7 at TOC −13812 (`0x32a34`), 1.0 without a neighbour (−13648), 1.7 (−13808), `/3` (−13684); Bézier `0x41b00` coefficients 3, −6, −1, −3 match `BezierComponent` op for op; `LoopKnotFactor` matches `0x32424..0x325d0` |
| Node tangents `0x31450` | **REFUTED (plan §13.3)** | CV-6 |
| Sample every 3.0, `t += 0.05` | CONFIRMED | only controller store at displacement 172 is `0x38abc` (3.0); step −13424; emission `fmadds` order matches `CoasterCurve.cs:202-232` |
| 4096 sine table | CONFIRMED | scale `fdivs(4096f, 2π_f)` in the table object +8; entries `frsp(sin(fmuls(2π_f, k/n)))`; lookup `fctiwz` & mask; cos via `fadds(π/2, a)`. C# `:249-255` equal (NaN/huge input saturates in `fctiwz`, not in .NET) |
| Banking divisor 4 | CONFIRMED | `fdivs(fdivs(fmuls(v, ±π−θ), def+16), back+fwd)`, π at −13488/−13492, θ = 0 → 0. Gaps in CV-9 |
| Rating step with Fz (F7), single-precision ops | CONFIRMED (oracle) | see "Rating and oracle"; the pass around it: CV-16 |
| Final counts sections with 0x40 **and** 0x80 (F8) | CONFIRMED (oracle) | `0x419d0`, `0x419d8` |
| Trains = min(uiMaxTrains, max(1, lap/release)) | CONFIRMED | `0x4046c..0x404e4`; NaN edge only |
| Guest excitement trunc(0.5E + 50) | **CONFIRMED** (it looks odd, but it is what the code does) | `0xdff70`: track-type-3 branch `0xe003c → 0xe2fe4`, controller `0xb5e24` (0 → excitement 0), getter `0x41a14` (r5 ← +260 excitement), `0xe0078 fmadds f0 = E × 0.5 (−11088) + 50.0 (−11128)`, `fctiwz`. C# `CoasterRating.cs:164` is the same fused form. A coaster always scores 50..100 for guests. The `+1960 == 2` branch before it (`0xdffb0`) is a different object class and was not traced |
| Motion `0x40650` | CONFIRMED, with unregistered gaps (CV-5) | dt from `0xa3e08` (1000.0 at −13496); per-car `0x40a20` `fsubs`/`fmuls`/`fmadds`, then max with the floor; brake `0x40bb8` matches `BrakeBlend` exactly; τ = d/((v + v')·0.5); partial hop `fmadds` chain; decay 0.95 below 2.0 and 0 below 0.01; `0x38250` P = 0 on → 64, P = 1 and train flag 0x80 on leaving 64; speed setting `0x3fb68` `fdivs(s, 100)`, `fmadds` → 110 at s = 60 |
| COAST commands 1–8 | CONFIRMED except COAST 4 | bridge `0xb6df4`: unsigned ≤ 8 jump table; 7 consumes one operand; 8 ignores its operand, `0x3dafc(script+8)` → script +224, speed from script +192 via `0x3de68`. **COAST 4 maps 0/1/2 to 16/64/32** (CV-2) |

## Rating and oracle

Oracle (`16bb38e` interpreter running `0x415b4`/`0x41900`) against a scratch console that calls
the real 02712e0 `CoasterRating.Step`, `FinalFor`, `TrainCount` and `GuestExcitement`, compared on
binary32 bit patterns (scratch only, outside Git):

| Target | Inputs | Mismatches | Cause |
| --- | ---: | ---: | --- |
| Step `0x415b4` | 9,297 (6,097 oracle distribution + 88 boundaries: bank ±π/2 ±1 ulp, Fy 0.1f ±1 ulp, ±0; then stress) | 120 | 90 subnormal S/E: the decay is `fmadds(S, 0.5, dS)` (`0x417bc`, `0x417cc`), unfused in C#; 30 NaN: `fcmpo`-select keeps the old peak, `MathF.Max` returns NaN |
| Final `0x41900` | 9,012 | 1 | subnormal E: `fmadds(0.5, E, 1.7·peakE)` (`0x41944`) |
| `TrainCount` (model of `0x4046c..0x404e4`) | 20,006 | 862 | all lap = 0 with release = 0: `fctiwz(NaN)` = 0x80000000, so the original gives 1 train and the C# gives uiMaxTrains |
| `GuestExcitement` | 20,005 | 0 | |

None of the mismatches falls in the realistic inputs. **F7** CONFIRMED: the only call site is
`0x3ffa8`, and the last FP op before it is `fmuls f0` → `stfs f0,40(r23)` (Fz). **F8** CONFIRMED:
`0x419d0` (0x40) and `0x419d8` (0x80) each skip the increment, matching C# `(flags & 0xC0) == 0xC0`.
The step's ops are separate singles in the C#'s order (|F|₁ = (x+y)+z, inverted ×3.0 at −13448,
0.1 at −13444, flags `& ~7`). The clamps are min 100 then max 0. **Trains** CONFIRMED: release u32
→ double → `fdivs` 1000.0, lap/that, `fctiwz`, signed max 1, unsigned min uiMaxTrains, gated on
ctl 0x1000 set and 0x4000 clear. Optional: fuse `CoasterRating.cs:108/109/155` and map NaN in
`TrainCount`.

The rating **pass** `0x3fc18` deviates (CV-16).

## Findings

Severity: **blocker** = deviates from the trace and is not registered (or contradicts the evidence
its register row cites); **major** = unregistered behaviour; **minor** = docs, precision or
dead data.

**CV-1 (blocker). Moves and the height slider charge and refund `CostPerUnit`.** Only the place
path asks `0x4b168` for the unit (`0x37864 li r4,1`). The pick-up stores (`0x4a3dc`, `0x4a3f8`),
the drop (`0x4a5dc`, `0x4a610`, `0x4a690`) and the slider (`0x46268`) pass r4 = 0. C#
`CoasterBuilder.Settle` (`CoasterBuilder.cs:142-151`) goes through `Track.NodeCost` →
`LinkCost` (`CoasterTrack.cs:486-497`), which always adds the unit. Scenario: a Std pylon
(40/10/10) is moved one cell. The original credits trunc(length + height terms) and debits ceil of
the new ones. The C# books about 40 more on each side, and its rounding differs whenever the
unit has a fraction. Fix: `NodeCost(index, chargeUnit)` / `LinkCost(..., chargeUnit)` with
`false` in `Settle`, plus a test pinning a move's ledger lines.

**CV-2 (blocker). COAST 4 is reversed for arguments 1 and 2.** `0x3de0c cmpwi r4,1`,
`0x3de18 bt eq → 0x3de50 li 64`; 2 → `0x3de44 li 32`; 0 → `0x3de38 li 16`. PPC-rides,
COAST-plan §5.3/§17.4, `CoasterRide.cs:107-112` (`ControllerStateFlag`, `PowerFactor`) and the
COAST-012 row all say 1 → 32, 2 → 64. Scenario: a station script reports a breakdown with
COAST 4 1. In the original the power factor drops to 0 and the trains coast to a stop. In the C#
they keep their floors and finish the lap. COAST 4 2 cuts the power in the C# but not in the
original. Fix: `1 => 64, 2 => 32`, P = 0 for state 1. Correct the plan, PPC-rides and the COAST-012 text.
This also answers part of COAST-012, since a scripted breakdown now has traced motion.

**CV-3 (blocker). Completion always appends one end node with flags 0x144.** See the claim table.
COAST-029 registers the two-node branch and even says that 0xA0 contains 0x20. It still lets
`bVisibleEndPylon = 0` select two nodes without the 0x144 flags
(`CoasterTrack.cs:431-445`, `CoasterDefinition.cs:268-271`). That is not defensible: it
contradicts the trace it cites. The 0x40 in 0x144 makes the end node a loop node, which changes
its knot direction (`(0, ±1, 0)`, §13.4) and the following link's pylon control. Fix: one end
node, flags 0x144, regardless of the setting. Correct COAST-plan §15. Narrow COAST-029 to the
station-tag placement only.

**CV-4 (major). Placing a pylon can be refused for money.** `Place` (`CoasterBuilder.cs:87-89`)
calls `TrySpendCoasterTrack` without `force`, so it refuses when bankrupt or when trunc(cost) is
above the balance. The original's place path reads the balance (`0xcbf48` at `0x378a0`), drops
the result and debits unconditionally. The only affordability test is in the generic tool
(`0x6f664..0x6f67c`: accumulator vs balance → `0x7c50c`), and its consumers are untraced.
Fix: register it (e.g. COAST-030 "the tool refuses a pylon the balance cannot cover; the
original's 0x7c50c consumers are not traced"), or force the debit and gate on the
ceil-accumulator against the balance like `0x6f664`.

**CV-5 (major). Unregistered motion differences in `CoasterRide` (`0x40650`).**
- The winch floor W is never halved for trains with flags 0x80 **and** 0x20 (`0x408e4`);
  `:452`. The plan says "0x80 and 0x20", and the binary needs both.
- The winch test reads the current sample's section. The binary reads the sample at
  `trunc(s_car + ½)` (`0x4098c`, `0x409f4`); `:463`.
- Friction reads the front position on every hop. The binary uses the reference car's +64 sample,
  which is written only after the hops (`0x41094`), so it is fixed for the frame; `:470`.
- The average divides by all cars and sums all of them. The binary skips inactive cars (car flag
  bit 0) in the sum. Harmless today.
- Braking start (medium): ctl+88 is the node-list tail and `v_last = sample[trunc(D_tail/3) − 1]`
  (node +252 = `__dtou` of its sample coordinate, `0x45790`/`0x457a0`). `CoasterRide.cs:180-181`
  takes the first sample with `Section == last`, one or two samples later.

Fix: port the three behaviours (W halving needs flag 0x80 from leaving mode 64, which `0x38250`
sets), or extend COAST-022 to name each one. For the braking sample, use `lastNodeSample − 1` or register it.

**CV-6 (major). The straight case of `0x31450` is inverted in the plan and the C#.** The
½-difference tangent is gated by pylon flag **0x4000** (`0x31a74 lwz 0(r26)`, `rlwinm.` 0x4000,
`0x31a7c bt eq → 0x31ba4`). Non-loop pylons and pylon-less nodes take `T+ = b/SD`
(`0x31ba4 lfs 12(r25)`). Node flag 0x80 (`0x31a84`) picks `½(â − b̂)` or `½(b̂ − â)`. Separately,
the Y component is zeroed on a **non-loop pylon next to a loop pylon** (`0x31be0..0x31c54`,
`0x31eac..0x31f20`). The plan says "loop next to loop", and the C# has neither. `ForwardTangent`
(`CoasterTrack.cs:557-579`) follows the plan: `straightDot != null && !loopPylon`. Effect: the
knot directions near straight and folded-back nodes, and at loop entries. COAST-015 covers the
pylon tag axis, not the node tangent rule. Fix: `loopPylon` in the straight branch, the 0x80
order, the Y-zeroing, and correct §13.3.

**CV-7 (major). Smaller unmodelled validator and build rules, not registered.**
- The turn test is skipped when the nodes carry 0x10000 together with 0x20000 (`0x36d7c..0x36dc0`).
- The point before the station node is the station's predecessor (`r23+104`,
  `0x36d58..0x36d60`). The C# uses `ReturnCell` (`CoasterTrack.cs:209`).
- `CheckStack` lacks the object-flag test (0x600, or 0x200 on the new-pylon branch, `0x37290`).
- `CheckClosure` (`:403-418`) adds an arrival-turn test with the entrance window at the return
  cell. `Revalidate` marks the last pylon invalid when it fails, and COAST-016 then blocks
  completion. Nothing traced does this.
- `Close` needs two pylons (`:426`), and `Move`/`SetHeight` refuse while riders are on board
  (`CoasterBuilder.cs:107`, `:127`).

Fix: register these as OpenTPW rules (one row, e.g. under COAST-028 or a new COAST-030/031),
or drop the invented ones.

**CV-8 (minor). COAST-016 should cite the evidence against it.** `0x37ef0` completes
unconditionally. Refusing completion while a node is invalid, or while the circuit is open
(`CheckComplete`), is defensible only as a tool-layer guess, because the action-129 test
(`0x80c50`) is unread. Keep it registered, but its text must say that `0x37ef0` itself tests
neither. The safer default is the traced one: let completion proceed and keep the 0x4000 marks.

**CV-9 (minor). Auto-bank gating.** Node flag 0x100 forces bank 0 (`0x3f584`). `0x3360c`
banks when node flag **0x4 is clear** (the plan says "set"). The C# uses `IsPylonless` as the
stand-in (`CoasterTrack.cs:617`) without a tag. The lengths come from the mixed-precision
`0xa3da0`. Fix: tag it under COAST-015 and correct §13.8.

**CV-10 (minor). Knot side factor.** With node flag 0x80 clear, the back side always uses K and
only the forward side tests the next node's 0x40 (`0x326dc..0x327dc`); with 0x80 set it is the
reverse (`0x32600..0x326d8`). `CoasterCurve.cs:164-165` tests both neighbours. The sampler's
`t_s < 0` case moves the sample to the previous pair and adds 1 (`0x45320..0x4535c`). The C#
keeps it on the current pair (`:217`). The ½-spacing merge at `:234-235` is OpenTPW's: the binary
duplicates the last sample. Fix: port the first two, and add the merge to the COAST-019 text.

**CV-11 (minor). Precision is not registered.** The binary fuses where the C# rounds twice:
window limits `fmadds` (`0x36fb4/b8`, `0x36fe4/e8`) vs `CoasterTrack.cs:191`; pylon height
`0x369c8` vs `:355`. It also computes the chord `0x32990` (`fmsubs` for 0.7·dy, then double
`fmadd`/`sqrt`/`frsp`) and the vector norms in mixed double/single, where the C# uses single
`Vector3.Length`. The derivative `0x41c0c` is power form with `fmadds` (2.0 at −13340), where the
C# uses unfused Bernstein; it feeds yaw and pitch. The plan claims bit-faithfulness for exactly
these routines, so register one precision row or port them. Porting is cheap for
`PylonHeight`, the window limits and the derivative.

**CV-12 (minor). LoopPylon `fMaxLoopHeightDifference` default.** The code default is the raw
25.0 (`0x2d5d4 lfs −13744`, `0x2d5d8 stfs 172(r31)`). Only a SAM value is scaled by 10·π/180
(`0x2fa60/64`). `CoasterDefinition.cs:191` stores 25·10·π/180. Nothing reads it today. Fix: `= 25`.

**CV-13 (minor). DETERMINISM.md cites the wrong register IDs.** The coaster generator is
COAST-027 and the save gap is COAST-025. The table and the save paragraph cite COAST-024 and
COAST-022 (two places, plus the `coaster_rng_state` row).

**CV-14 (minor). Dead or inconsistent data.**
- Node flag 0x80 never exists in the C#, so the F8 count (`CountedSections`, `CoasterRide.cs:178`)
  is always 0. Say so, or model 0x80 (CV-6, CV-10 and the COAST-020 fallback need it too).
- `CoasterWorld.PylonsAt` (`CoasterWorld.cs`) excludes only `IsStation`, so station end nodes
  count as pylons for `HasPylon`, other coasters' stacking test and the `Move` stack index. The
  track-local lookup excludes them (`IsPylonless`). Fix: `!node.IsPylonless`.
- Track money is posted as OtherCosts/OtherIncome and joins the station's `TotalSpent`. The
  original's debit `0xcbfdc` has its own statistics, and it does nothing when bank +276 == 0
  (`0xcbff8`). Note it in ECONOMY.md or register it.
- COAST 5 changes state only under `ctl+8 & 4` (close) or `& 0xA` (open). `CoasterRide.cs:285`
  sets `Closed` unconditionally. Note it in COAST-012.

**CV-16 (blocker). The rating force has the wrong sign, and COAST-021 says the operands are not
decoded when they are.** `0x3fcf0 fsubs f1, next.x, cur.x` and `0x3fcf8 fsubs f24, prev.x, cur.x`
(and the same for y and z at `0x3fd04..0x3fd20`): both differences are neighbour − current. The
sum is divided by the spacing (`0x3fdf4`) and multiplied by `fneg(g)` (`0x3fe04`, g = 1.0 at
data `0x3d140`). So `a = −(p₋ + p₊ − 2p)/spacing`. `CoasterRating.cs:225` computes
`+(p₋ + p₊ − 2p)/spacing`. This flips Fy and Fz into the step, which changes the `Fy < 0.1`
inverted test and the signed Fz term in dU, and so every rating. The order also differs:
the original computes `F = a·(v/10)` (`0x3ff60 fdivs`, then `fmuls`), the C# `a·v/10`. Further pass
differences, not registered:
- max height starts at 0.0 (`0x3fcbc`), not `float.MinValue` (`:208`, `:247`);
- the original gives sample 0 speed fWinchSpeed and force 0, and copies sample n−2's speed and
  force into n−1 (`0x4001c..0x4006c`). The C# copies speeds only, and differently (`:240-241`);
- trough flag 8 is also *cleared* at a climb ≤ 0.01745 (`0x3fdc4`). The C# only sets it (`:222`).

Fix: `a = ((p₋ − p) + (p₊ − p)) / spacing * −1f`, `F = a * (speed / 10f)`, the 0.0 start and the end
samples. Then retire COAST-021, or keep it only for g. The witness ratings in `CoasterTests` will move.

**CV-15 (blocker for merge, mechanical). Rebase and the schema collision.** See "Merge readiness".

## Determinism

Schema 6, pin `0x5E1198F70F3F2B55` (`DeterminismTests.cs:182`). The synthetic park has no
coaster, so the digest adds only the generator state and a count of 0. That explains why the run
itself is unchanged. The coaster digest is complete for the simulated state (track, controller,
samples, rating, sorted winch sections, trains, riders, leaving queue), and it hashes floats by bit pattern.
The `--m3-gate` determinism row passes twice with equal hashes.

**Integration need:** ANIM-I `b84bf58` also moves to schema 6, with pin `0x0BC982AB91BE9A28`
and other fields. `git merge-tree` auto-merges `WorldStateHash.cs` to "6", which is silently
wrong, and conflicts only on the pin. Whichever lands second must bump to **schema 7**,
recompute the pin on the merged tree, and add a line to the DETERMINISM.md history for each part.

## Whitespace

`git diff --check de96a43..02712e0` flags 5 lines in `SandboxSmokeTest.cs`. The file is CRLF:
545 of 546 lines before and 548 of 549 after. The only LF line (213 → 216, `var monthEnd = …`)
predates COAST-I. All five added lines end in CRLF, so the new lines add no mixed endings, and
`--check` reports the CR as trailing whitespace. No other changed file has CR. Pinned in
`test_coast_v1.Git.test_smoke_test_file_line_endings`.

## M4 gate coaster rows

M4-GATE (`27734f7`; tightened text read at `m4-gate-fix` `f00f3d2`): `coasters.construction` requires, for
**every track ride**, station built and its script running, TrackBuilt, CarRan (guests board and
are released), Stopped, Deleted (station and track cells free, scrap per DATA). `rides.rating`
requires an excitement from the built track equal to what the guest bridge uses. The headless API
can satisfy the 12 coasters as follows. `M4GateRun.TrackBuilder` has no `Level`.

1. Station: the existing build flow places the station. `OriginalObjectRuntime.Coaster` is the
   `CoasterRide` (non-null when `Bumper.WhichTrackType == 3`).
2. Builder: `new CoasterBuilder(ride, CoasterSite.FromGrid(objects.Grid), () => economy, () => instanceId)`,
   then `SetStation(CoasterBuilder.StationEnds(entry.Shape, x, y, rotation))`. The no-ground set is
   frozen here, so build after the park is set up.
3. TrackBuilt: `Place` the `CoasterTests.Circuit` offsets from the depart cell; every result must
   be `Ok`, with no `Invalid` node. `CoasterAssetTests.EveryPcCoasterBuildsRatesAndRuns` shows that
   this circuit passes for all 12. Then `Place(ReturnCell)` closes, `Complete()` must return `Ok`,
   and `ride.IsOperational` holds with `Samples.Count >= 3`. Also check the charge:
   `PaidCost == Σ trunc(PreviewCost)` and the balance drop.
4. CarRan: tick the runtime (`OriginalObjectRuntime.Advance`) with guests. The station script's
   COAST 8 sets `Initialised`, COAST 1 boards (`RidersOnBoard > 0`), a train leaves (`State` not
   `Boarding`, `Position` changes), and COAST 3 releases riders (`LeavingCount` drains).
   `CoasterAssetTests.GuestsRideACoasterBuiltHeadlesslyOnJungle` does this for one coaster.
5. Stopped: close the ride (`VAR_RIDECLOSED`, so the script issues COAST 5). Boarding stops and
   the trains reach the station (COAST-012 APPROX). Or require mode 64 (COAST 4 with argument
   **1** after CV-2) and `Speed == 0` on every train. That second form is the traced one.
6. Deleted: `builder.Delete()`, then remove the station. Check `!ride.World.Rides.Contains(ride)`,
   `!HasPylon` on every circuit cell, and **a track refund of 0**. The tightened M4 text asks for
   "the track's scrap credited per DATA". No DATA defines one, and §16 plus this review show the
   original refunds nothing, so that clause must read "station scrap only; track refund 0
   (COAST-plan §16)". Otherwise the gate requires an invented credit.
7. rides.rating: `ride.Rating.Excitement` and `ride.Visitors.ExcitementLevel ==
   CoasterRating.GuestExcitement(E)` (only after COAST 8; before it the bridge reports 0).

The other 8 track rides (4 karts, 4 water rides, types 1/2) use cell tracks and BUMP and are
outside COAST-I. So `coasters.construction` cannot pass on this branch, and the gate text should
say which part COAST-I closes.

## Merge readiness

`git merge-tree --write-tree 02712e0 <other>` (merge base `c6c19ea` for all three):

| Onto | Conflicts | Notes |
| --- | --- | --- |
| fork main `8a000d8` | `docs/DETERMINISM.md` (stream table: keep the coaster row and main's reworded sound row), `docs/FIDELITY-REGISTER.md` (regenerate), `source/OpenTPW/Hud/ParkHud.cs` (station placement callback: keep `EnterCoasterTool` before main's `pendingItem = null` path) | `WorldStateHash.cs`, `Level.cs`, `RideScriptWorld.cs`, `ParkEconomyContracts.cs`, `DeterminismTests.cs` auto-merge |
| ANIM-I `b84bf58` | `DETERMINISM.md`, `FIDELITY-REGISTER.md`, `DeterminismTests.cs` (pin), `OriginalObjectRuntime.cs` (the effects docstring; the COAST dispatch in `Perform` auto-merges) | schema 6 vs 6, see Determinism: bump to 7 |
| SAVE-I `2ddf376` | `FIDELITY-REGISTER.md`, `tools/fidelity_register.py` (`REGISTERS`: keep both `COAST` and `SAVE`) | `ParkEconomy.cs`, `SandboxSmokeTest.cs`, `DETERMINISM.md` auto-merge. COAST-025 (no coaster save) should join SAVE's object save |

All merges were trial merges with `merge-tree`. Nothing was written to any branch.

## Runs (this review, worktree at `02712e0`)

| Check | Result |
| --- | --- |
| Release build (SDK 10.0.401) | 0 errors |
| Tests without assets | 1069 passed, 269 skipped, 0 failed |
| Tests with `OPENTPW_GAME_PATH` | 1254 passed, 84 skipped, 0 failed (40.7 min on a loaded host; the 3 CoasterAssetTests pass) |
| `--m3-gate --minutes 30` (Release) | 16 pass / 0 fail / 0 unresolved, exit 0 (determinism row equal hashes `AFDCA347E564786E`) |
| Native sandbox smoke | passed, 1104 frames, exit 0 |
| Native `--load-original-level jungle --smoke-test` | unmuted: stalled after SDL audio init (killed after 8 min; another session's unmuted front-end smoke stalled at the same point). With `--mute`: **passed**, 4222 frames, exit 0. "HUD coaster tool: Temple Of Gloom with 6 pylons for $797, 143 samples, excitement 28.0, sickness 1.1, 1 train(s)"; "a train ran, the track was drawn, deleted and the station removed" |
| Native `--front-end --smoke-test` | unmuted: same audio-init stall (killed after 12 min). With `--mute`: passed, 557 frames, exit 0 |
| `run_evidence_checks.py --mac-bin --pc-data --dotnet <net8>` | OK: 16 Python suites, 1013 tests, 152 skipped; `lanes/coast` 60 ran, 0 skipped |
| `fidelity_register.py --check` / `test_fidelity_register.py` | 207 unresolved unique IDs, complete / 8 OK |
| `test_coast_v1.py` (with `OPENTPW_MAC_BIN`) | 20 tests, OK, 8 expected failures (CV-1, 2, 3, 4, 6, 12, 13, 16) |

The unmuted stall is consistent with audio-device contention between the concurrent sessions
on this host. It was not reproduced in isolation, so the unmuted native result is UNVERIFIED.
The smoke's coaster check is weak: it requires a train to leave, but not a stop, a zero delete
refund or a rider. Suggest asserting the balance is unchanged across `DeleteCoasterTrack`.

## Approximation register judgement

| ID | Registered where it deviates? | Judgement |
| --- | --- | --- |
| COAST-004 collision | yes (`CoasterTrack.cs:248`) | fine |
| COAST-007 waiver | yes (`:203`) | fine; waiver decoded (`0x36ebc..0x36ed0`), purpose still open |
| COAST-011 camera | yes | fine |
| COAST-012 stop/breakdown | yes, but states the wrong COAST 4 mapping | fix with CV-2; breakdown = mode 64 is now traced |
| COAST-013 sickness | yes | fine |
| COAST-014 Windows | yes | fine |
| COAST-015 tags | yes | fine; it does not cover CV-6/CV-9 |
| COAST-016 action 129 | yes | defensible only with CV-8's text change |
| COAST-017 roll | yes | fine |
| COAST-018 swing | yes | fine |
| COAST-019 closing pair | yes | text misses the ½-spacing merge (CV-10) |
| COAST-020 rates | yes | fallback is decodable now (claim table); port it |
| COAST-021 force | yes, but its premise is wrong | not defensible: the operands decode and the sign is the opposite (CV-16) |
| COAST-022 step | yes | incomplete: CV-5 |
| COAST-023 dispatch | yes | add "speed from script +192" to its evidence text |
| COAST-024 incomplete | yes | fine |
| COAST-025 save | yes | fine; join SAVE-I |
| COAST-026 render | yes | fine |
| COAST-027 RNG | yes | fine; DETERMINISM.md cites it as 024 (CV-13) |
| COAST-028 tool | yes | fine; could absorb CV-4/CV-7 |
| COAST-029 station | yes, but contradicts its own evidence | not defensible: CV-3 |

## Resolution (COAST-F, branch `coast-fix`)

| Finding | Resolution |
| --- | --- |
| CV-1 | Fixed: `Settle` asks `NodeCost(index, chargeUnit: false)`; `MovesAndTheHeightSliderDoNotChargeTheUnit` pins the ledger lines |
| CV-2 | Fixed: `1 => 64, 2 => 32`, other values change nothing; plan §5.3/§9.7/§17.4, PPC-rides and COAST-012 corrected |
| CV-3 | Fixed: one end node with flags 0x144 always; a pylon-less loop node's knot is vertical; COAST-029 narrowed to the placement |
| CV-4 | Fixed: the place debit is forced; the tool layer's untraced affordability test is named under COAST-028 |
| CV-5 | Ported: half-sample winch test, per-frame friction sample, strict braking test, partial-hop floor at M (found while porting, `0x40d78..0x40dc8`), braking sample `(tail +252) − 1`; named in COAST-022: winch-floor halving (train flags 0x80 + 0x20 not modelled), inactive cars, reference/braking car |
| CV-6 | Fixed: half difference only on loop pylons, `\|b\|·½(â − b̂)/SD` with 0x80 clear; Y zeroing for a non-loop pylon next to a loop pylon; plan §13.3 corrected |
| CV-7 | Registered as COAST-030 |
| CV-8 | COAST-016 text now says `0x37ef0` tests neither |
| CV-9 | Node flags 0x4/0x100 gate auto-bank; the station node's flags are tagged COAST-015; plan §13.8 corrected |
| CV-10 | Ported: knot sides and the sample record before its pair; COAST-019 names the half-spacing merge |
| CV-11 | Ported: fused pylon height, window limits, derivative, cost length and trough normalisation; the tangent and chord norms stay single precision |
| CV-12 | Fixed: default 25 |
| CV-13 | Fixed: COAST-027 and COAST-025 |
| CV-14 | `PylonsAt` excludes station end nodes; the F8 count is documented as 0; the ledger is COAST-031; COAST 5's condition is in COAST-012 |
| CV-15 | Left for the integration merge (schema 7) |
| CV-16 | Fixed: sign, `a·(v/10)`, 0.0 maxima, end samples, trough flag clear; COAST-021 retired. COAST-020 was decoded too and is ported |

Oracle after the fix (oracle-fix interpreter, scratch console on `coast-fix`): step 13,622 inputs
(7,613 from the rating passes of 39 built tracks, 6,009 from the oracle distribution, 609
subnormal/NaN stress) and final 6,651 inputs, all bit-exact. The step now decays with `fmadds`
and keeps the old peak on NaN, and the final sums with `fmadds` and clamps like `0x41950`
(NaN → 100, −0 → +0).
