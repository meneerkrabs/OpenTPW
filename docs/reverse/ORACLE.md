# PowerPC differential oracle

Isolated, pure routines of the identified Mac PowerPC build run in OpenTPW's own PowerPC
interpreter. Their results are compared with the plans' reference formulas and with OpenTPW's
C# on this checkout. The oracle judges by result, not by reviewer opinion: a disagreement is a
finding to report and resolve, never something to hide.

Code: `tools/ppc-analysis/oracle/`. Tests: `test_ppc32.py` (always) and `test_oracle.py` (with the
private binary). Both run in `tools/ppc-analysis/run_evidence_checks.py`.
Section 7 describes the x86 counterpart for the Windows executables (`oracle_x86/`, Unicorn, dev-only).

## 1. Policy and how it is enforced

User decision of 2026-10-10. Pure computational routines of the identified binary
(`SimThemePark.data`, SHA-256 `04809cd4…e295f5`, Feral `bin` directory, loaded with
`tools/ppc-analysis/pef.py`) may run as a differential oracle. The hard limits:

| Limit | Enforcement |
| --- | --- |
| No OS/toolbox calls, I/O or network | Every import's transition vector points into an unmapped trap region. A call there without an explicit stub raises `UnknownCall`. `sc`, `tw`, SPRs other than LR/CTR/XER and cache ops raise `Unsupported`, which aborts the run. |
| Never the whole program | Each target lists the code ranges it may run (`allowed`). Every executed PC and every control transfer (call, tail branch, `bctr`, return) must stay inside them, or reach a documented stub. Otherwise the run aborts. |
| Explicit stubs only | Each stub is named in `targets.py` with the reason it is safe (section 4). A stub runs in place of its entry and returns through LR. |
| Bounded execution | Each call has an instruction budget (`BudgetExceeded`). Memory is only the mapped PEF sections, a scratch heap and a stack. Any other access raises `MemoryFault`, and code is read-only. |
| No new dependencies | Pure Python stdlib (3.12+, as in CI). The C# side is a console project that references `source/OpenTPW`. |
| Original bytes stay out of Git | The binary is read at test time from `OPENTPW_MAC_BIN` / `OPENTPW_PPC_BIN_ROOT`, and tests skip without it. `run_evidence_checks.py` strips both and sets both from `--mac-bin`, so a stale shell variable cannot switch the oracle on (OV-2). `oracle/disasm.py` prints listings to the terminal only. Never redirect them into the checkout. Tests pin inputs, counts and results only. |

## 2. Interpreter (`ppc32.py`)

PPC32, big-endian, user mode. Decoding uses the shared field helpers of
`lanes/formats/ppcfields.py` (`rotate_mask`, `rlwimi`, `signed16`).

Supported subset (`ppc32.SUPPORTED_SUMMARY`):

- **Integer:** `addi addis addic addic. subfic mulli cmpi cmpli cmp cmpl`; `add/subf` with the
  carrying, extended, minus-one and zero forms plus `[o][.]`; `neg mullw mulhw mulhwu divw divwu`;
  `and andc or orc nor nand xor eqv ori oris xori xoris andi. andis.`;
  `slw srw sraw srawi cntlzw extsb extsh rlwinm rlwimi rlwnm`, all with Rc.
- **Memory:** `lbz lhz lha lwz` and `stb sth stw` with the update, indexed and update-indexed
  forms; `lmw stmw lhbrx lwbrx sthbrx stwbrx`.
- **Branch:** `b bl ba bc bcl bclr bclrl bcctr bcctrl` (CTR decrement on `bc`/`bclr`); the CR
  logic ops and `mcrf`; `mfcr mtcrf mflr mtlr mfctr mtctr mfxer mtxer isync sync`.
- **Floating point:** `lfs lfd stfs stfd` with the update, indexed and update-indexed forms;
  `stfiwx`; `fadd fsub fmul fdiv fmadd fmsub fnmadd fnmsub`, each with an `s` form; `fsel fmr fneg
  fabs fnabs frsp fctiw fctiwz fcmpu fcmpo mffs mtfsf mtfsfi mtfsb0 mtfsb1`.

Floating-point semantics:

- FPRs hold raw binary64 bits.
- Arithmetic on finite operands is exact (`fractions.Fraction`). The result is rounded once to
  binary64 or, for the `s` forms, straight to binary32, as the architecture specifies. There is
  no double rounding, and `fmadds` is fused.
- Rounding follows FPSCR[RN].
- `fctiw[z]` saturates, and NaN gives `0x80000000`.
- `stfs` converts by bit selection (Book I 4.6.3), with denormals shifted.
- Special operands and exact zeros take IEEE double paths with an explicit sign rule.
- An exact-zero sum or difference is −0 under round toward −∞ unless both terms are +0, and
  `fmadd` propagates the first NaN in the order FRA, FRB, FRC (OV-6).
- Not modelled: exception flags, record forms of FP ops (`Unsupported`), `fsqrt`, `fres`,
  `frsqrte`. `divw`/`divwu` by zero and `INT_MIN / −1` are undefined in the architecture, so they
  raise `Undefined` (a subclass of `Unsupported`). A target compares only those as `UNDEFINED`;
  every other `Unsupported` is a hard error of the run (OV-5).

Mapping (`harness.py`):

- Code goes at `0x10000000 + code offset`, the same convention as the `BIN:STP-PPC:0x1008…`
  annotations.
- The data section goes at `0x20000000`, with the loader relocations applied. The TOC comes from
  the main transition vector (data `+0x8000`).
- Imports get trap transition vectors at `0x38000000`.
- Scratch heap at `0x40000000` (4 MiB) and stack at `0x60000000`.
- Writes are journalled per 4 KiB page, and each call restores pristine memory. Every call
  starts with zeroed registers, r1 = the stack top, r2 = the TOC and LR = the return sentinel.
- A *fragment* (a slice of a larger routine) runs with `stop=`. The stop may be reached by falling
  through, and may then be the exclusive end of an allowed range. A branch to it is checked like
  any other transfer, so a stop that is a branch target must lie inside `allowed` (OV-8).

Consolidation: the lanes' one-off interpreters remain as they were, such as the
ten-instruction LCG runner in `lanes/review/test_det_v1.py`. They pin facts independently, which
review lanes value, and new execution-based witnesses should use `oracle/` instead.

## 3. Running

```
python3 -I tools/ppc-analysis/oracle/differential.py FERAL_BIN [--count N] [--target KEY] [--no-csharp] [--json]
OPENTPW_PPC_BIN_ROOT=FERAL_BIN python3 -I -B -m unittest discover -s tools/ppc-analysis/oracle -t tools/ppc-analysis/oracle
python3 -I -B tools/ppc-analysis/run_evidence_checks.py --mac-bin FERAL_BIN
```

The C# bridge (`oracle/dotnet/OracleBridge.csproj`) is built on demand into
`~/.cache/opentpw-ppc/oracle-bridge/<source digest>`. The digest covers every build input with
POSIX paths: the bridge, the transitive `ProjectReference` closure of `source/OpenTPW`
(`OpenTPW.Files`, `.Common`, `.ModKit`, `.Online`; a reference outside that list fails the build
key), `EconomyTestData.cs` and `global.json` (OV-4). The build runs in a scratch copy of those
inputs, so no `bin/` or `obj/` lands in the checkout. The bridge targets net10.0: the SDK is the
first of `OPENTPW_ORACLE_DOTNET` (the runner's `--dotnet`), `OPENTPW_DOTNET`,
`~/.local/share/opentpw-dotnet10/dotnet` and `dotnet` on PATH whose `--list-sdks` shows a 10.x
SDK. Without one, or when the bridge fails to build or run, the C# comparisons are skipped with
the reason and the plan comparisons still run (OV-3). The bridge reads JSON lines, calls only
main's existing seams and reports exceptions as values. `{"fn": "capabilities"}` lists what it can
evaluate. A target is compared with C# only when its function is listed.

Each target runs 500 seeded random inputs (`random.Random('oracle-v1:<target>')`) plus explicit
boundaries. `test_oracle.EXPECTED` pins every comparison. An agreement must stay an agreement. A
finding must keep its mismatch count and first counterexample until it is fixed, and then the
table and this document change together, so gates only get stricter.

Mutation checks (`test_oracle.py`):

- Changing the LCG increment of the reference by one turns an agreement into mismatches.
- `CSharpMutation` builds the bridge against a scratch copy of `source/` with one operand flipped,
  and each turns a C# agreement into mismatches: the queue floor `4f → 5f` (60), the
  `CanChangeCellType` rule `old == 21 → old == 20` (2), rule 7 back to `lastCell` (2), the money
  test in 64 bits (2), the UNBOUNCE window `/ 200 → / 250` (39), `IsOpen` dropped from the park points
  counts-test, the loan instalment `… / months → … / months + 1` and the loan credit booked as other income instead of
  `LoanReceived` and the sign of the join excitement refusal swapped (`queue-join-gate`), the working energy rate `0.025f → 0.026f` (`staff-vitals`), the rest test `>` to `>=` (`staff-rest-seek`), the grievance `< 15` to
  `<= 15` (`staff-strike-grievance`) and the age gate `< 24` to `<= 24` (`staff-strike-month`). A no-op "mutation" fails the
  test.

## 4. Targets, stubs and results (seed `oracle-v1`, 500 random + boundaries)

| Target | Routine (allowed code) | Stubs | Inputs | vs plan | vs C# on main |
| --- | --- | --- | --- | --- | --- |
| `det-world-lcg` | `0x105328` | none | 507 | **agree** (`det_model.world_next`) | no port on main (hook `world_rng_next`, DET-I2) |
| `det-rse-rand` | fragment `0xb07f8..0xb0828` + `0x105328` + glue `0x1c5c04` | import `labs` (C `labs`, 32-bit wrap) | 509 | **agree** (`det_model.rse_rand`; bound −1 undefined on both sides) | no port (hook `rse_rand`) |
| `queue-limit` | `0xdcb74`, `0x1c3fbc` (float→uint) | `0x118018` type-record lookup (synthetic record, type id checked), `0xb678` assert (logged), `0xdd118` queue count (chosen count; records r28 = limit) | 515 | **agree** (F1 fixed in QUEUE-plan §4: 100 when HasQueue is clear) | **agree** (F1, F2 fixed in ORACLE-F) |
| `path-can-change` | `0x8408c` | none | 2548 (full 32×32×2 grid + 500) | **agree** (F3 fixed in PATH-plan §3.1 rule 7) | **agree** (F3 fixed) |
| `path-set-cell-type` | `0x82ac4`, `0x8408c`, cost getters `0x7bc58/0x7bc70`, balance `0xcbf48` | `0x859b4` ClearCell, `0x108424` game→bank, `0xd7dc8/0xd7ddc` map bookkeeping, `0xcbfdc` Spend (records the amount), queue-edited path `0xd8d20/0xb678/0xdd57c` (logged) | 511 | **agree** (F4: 32-bit money test in PATH-plan §3.2) | **agree** (F4 fixed) |
| `board-unbounce` | `0xae020` | `0x10e844` clock (returns the chosen `now`) | 508 (incl. two with start > now) | **agree** (BOARD-plan §5) | **agree** (F5 fixed: `TakeUnbounced`) |
| `board-bounce` | `0xadf40` | `0x10e844` clock (returns the chosen `now`) | 505 (incl. a full table and holes) | **agree** (BOARD-plan §5) | **agree** (OV2-6 fixed: `TakeBounceSlot`) |
| `walk-speed-setter` | `0xffe38` | none | 518 | **agree** (WALK-plan §1, incl. NaN/±inf/huge) | **agree** (`GuestSpeed.Caps`, wired in WALK-F) |
| `walk-smoothing` | `0xe6b28` + `0xffe38` | `0xffd4c`, `0xd2394` (later movement updates, no value used) | 507 | **F6**: 99 differ (98 in the last bit of `s`, 2 in the boost wrap, one input both). Fused reading: 2 (wrap) | **agree** (`GuestSpeed.Update`: fused multiply-add and the u16 product, F6 fixed in WALK-I; wired in WALK-F) |
| `walk-boost` | `0xeaaf8` (the outcome routine of one completed use; its `0xeb0cc..0xeb0f4` stores the boost), `0xdc320` (type lookup), `0xe2384` (amount byte), `0x105328` (world LCG) | `0x118018` type record (synthetic, registry and type id checked), `0xfa9a4` thing id, `0xfb7b4` thought, `0xea318` ride outcome (checked: called once), `0xe1f78`/`0xe2094` goods, `0xe1e58` charge, `0xeb6f4` happiness change, `0xe6d20` and the log `0x5fbcc` (all recorded). Appearance word `+348` 0 and the toilet flag clear: the balloon and costume blocks are not entered | 521 | **F20**: **agree** 521/521 with the reading (type `+344` 4 Sugar: boost += trunc(6 x amount byte / 100), u16; 1 Fat, 2 Salt, 3 Ice change the toilet and thirst floats; nothing else touches the boost). The old claim (nothing raises it) differs wherever the ingredient is 4 | `GuestSpeed.RaiseBoost` through `guest_ingredient_boost`: **agree** 521/521 (boost only) |
| `coast-rating-step` | `0x415b4` | none | 509 | **F7**: 440 differ with the stale term = 0. **Agree** 509/509 with the stale term = Fz. Previous force and bank are 0 in every input (OV-7, F7) | COAST-I not on main (hook `coaster_rating_step`) |
| `coast-rating-final` | `0x41900` | none | 506 | **F8**: 280 differ. **Agree** 506/506 with the corrected count | hook `coaster_rating_final` |
| `arrival-group` | `0xcb4e8` | `0x1082b4` game → park (sentinel, checked), `0xc1fb4` counted guests (chosen count; argument 20 checked), `0xc24c0` park points (chosen), `0x1083c8` thing lookup (synthetic thing, `+48` chosen: the weather's mCurrentDrops), `0x5fbcc` debug print (logged). The GameType object is marked constructed, so its constructor `0x12bb64` must not run | 515 | **F9** (factor and online cut fixed): 49 differ from GUESTS.md, all from the missing floor. **Agree** 515/515 with the routine reading | **F9**: 49 differ, the same inputs as the plan (`arrival_group`: `GuestSimulation.ArrivalGroupSize`) |
| `arrival-update` | `0xcb2ac` (the arrival update), `0xcb4e8` (group size), `0xcb028` (constructor), `0x1210f8`/`0x121098` (timer elapsed / restart), `0x10a9a4` (game pointer) | `0x109784` vehicle phase and `0x109b78` report flag (chosen per call), `0x109368` vehicle script step (logged with its frame slot and size), `0xcb698` guest creation (counted, raises the counted guests), `0xcb750` group history (logged), `0xb678` assertion (logged), and the `arrival-group` stubs. One run is a script of `0xcb2ac` calls in one sandbox | 510 scripts | **agree** (behaviour: active byte, timer start, guests created per call; GUESTS.md loop with the F9 details, group floored at 0). Routine reading **agree** 510/510 (adds the group word and the vehicle steps). **F11**: the brief's static reading (a negative group holds the state) differs on 66 scripts | no C# hook (`UpdateArrivals` has no vehicle phase) |
| `park-points` | `0xc24c0`, `0xc5828` (type count), `0xd1be8`/`0xd1c88` (park record search), `0xe4750` (turns → 100 ns units), `0x10a9a4` (game pointer), `0x1c4100` (64-bit divide) | `0x118018` type record (synthetic, registry checked), `0xb678` assert (a zero condition fails the run), `0xdc834` counts-test (chosen flag, itself the next target), `0xdc520` own age (signed timestamp difference in days). The park record table is built: `+24` the listed copies, `+28` by the `0xda874` write rule. Clock rate 15000; no build after the current turn | 507 | **F10** (fixed): GUESTS.md and the routine reading **agree** 507/507 (the 121 differences came from the counts-test, ARRIVAL-003) | `ParkEconomy.ParkPoints` through `park_points`, the input's open flag passed as `IsOpen`: **agree** 507/507 (121 before the counts-test) |
| `park-points-counts-test` | `0xdc834` (the counts-test), `0xdc988` (queue room), `0xdfe0c`/`0xdfe20` (ride state 1 / 4), `0xdff18` (open word), `0xe2fe4` (track type 3) | `0x118018` type record (`+156` chosen, registry and type id checked), `0xdd3f4` back-of-queue cell (writes the chosen u16), `0xdd118` listed guests (chosen), `0x45eac` coaster controller (chosen word). The u16 behind TOC −19648 is read as 0 (checked) | 522 (one failing condition at a time, wrapping queue room) | **Agree** 522/522 with the reading, stub call order included. The part C# implements (`implemented-subset`: choosable, not state 1 or 4, open) differs on 205 | no hook: entrance cell, queue room and coaster have no state in `ParkEconomy` |
| `loan-offers` | fragment `0xcb7a8..0xcb908` (the 8-offer loop), `0x1c3fbc` (float→uint), glue `0x1c61a4` | `0xfa718` base constructor (header only), import `pow` (MathLib, stubbed with Python `math.pow`) | 505 offer sets (8 offers each) | **F12**: **agree** 505/505 with the plan (fixed in C#; 497 sets differed from the former annuity) and with the routine reading | `LoanMath.MonthlyRepayment` through `loan_offers`: **agree** 505/505 (497 differed before) |
| `loan-month` | fragment `0xcc21c..0xcc3d0` (batch flush and the 8-record loan loop) | `0x1082b4` game → park (sentinel, checked), `0xb678` assertion and `0x5fbcc` debug print (logged) | 506 states of 1-3 months | no plan side. **Agree** 506/506 with the routine reading | no C# hook (see `loan-term`) |
| `loan-term` | the same fragment, run once a month over a whole term (withdrawals enabled, no batch) | the same | 503 terms | **F13**: **agree** 503/503 with the plan (fixed in C#; 483 differed from the former interest-on-balance arithmetic), the routine reading too | `LoanMath.Pay`, `ParkLedger.Post`, `ChargeLoanInterest` through `loan_term`: **agree** 503/503 (483 differed before; the year-profit word wraps at 32 bits on both sides) |
| `loan-payoff` | `0xccc78` (early repayment) | the same | 506 | **F13**: **agree** (payoff M × months left, year profit charged q × N, 32-bit wrap), routine reading too | `ParkEconomy.TryRepayLoan` through `loan_payoff`: **agree** |
| `loan-take` | `0xcc904` | the same | 505 | **F14**: **agree** (money in, balance, no profit, bought flag), routine reading too | `ParkEconomy.TakeLoan` through `loan_take`: **agree** |
| `queue-failure-predicate` | `0xdfe34` alone | none | 521 | **F15** (fixed in C#): 310 differ from the old claim (QUEUE-008 read VAR_BROKEN ≠ 0; the routine reads ride `+46` bit `0x1` and the float `+64`). The plan now says the routine reading: **agree** 521/521 | `RideVisitorBridge.ReliefWornOut` through `queue_failure`: **agree** 521/521 |
| `queue-ride-update` | `0xe0a8c` (the ride update: dispatch on the ride state `+408`, the head-guest step of states 1, 2 and 4) | the calls it makes are recorded, not run: `0xe1864` admission, `0xdcebc` remove from queue (ride and the u16 head id in the high half of r4 checked), `0xee804` dismiss, `0xeda04` force aboard, `0xe1758` script release; plus `0xb5758`/`0xb5be0` (VAR_LETMEON), `0xb678` assertion (logged when the condition is 0), `0xfa9a4` and `0x5fbcc` | 510 | **F15**: **agree** 510/510 (states 0..6 and −1, heads that wait or were taken, state 14 or not; `test_queue_ride_update_reading_is_sharp`) | no hook (CSHARP_PENDING): C# has the VAR_BROKEN reading only (`RideVisitorBridge.DismissHead`, `QueueTests`) |
| `queue-join-difference` | `0xe9a84`, `0xdfe34`, `0xeb7e4` (guest preference), `0xe02ec` (excitement), `0xdff70` (its formulas), `0xe1f70`/`0xe1f78`/`0xe2364` (sideshow words), `0xe2fe4` (track type 3), `0x41a14` (coaster getter, controller table built) | `0x118018` type record (registry and type id checked), `0xb5e24` script controller (script `+32` checked), `0x1ea40` pieces behind ride `+36` (the `+36` word and four distinct outputs checked), import `sqrt` (IEEE double square root, argument checked non-negative). The settings word `+164 + 12 · type` is written in the data block | 524 | **F16** (fixed in C#): 387 differ from the old claim ("ride minus preferred", `plan-static`). The plan now says the routine reading: **agree** 524/524 | `RideVisitorBridge.JoinDifference` through `queue_join_difference`: **agree** except 46 inputs that have a thing behind ride `+36` (QUEUE-004; with the thing removed from the inputs **agree** 524/524) |
| `queue-join-gate` | the fragment `0xecd6c..0xecdb0` / `0xeced0..0xecf34` of the state-10 handler `0xeccb0` (the `0xe9a84` call, `0xe02ec(ride, 1)`, the 45 test and the refusal) plus the callees above | the callees' stubs, plus `0x5fbcc` log (the message text is read from memory), `0xfb7b4` reason and `0xfb54c` thought (guest `+48` object, id and thought checked) | 524 | **F16** (fixed in C#): 155 differ in outcome from the old claim (`plan-static`). The plan now says the routine reading: **agree** 524/524 (outcome and difference) | `RideVisitorBridge.ExcitementRefusal` and `JoinDifference` through `queue_join_gate`: **agree** except 61 inputs with a thing behind ride `+36` (the same cause) |
| `shop-sale-litter` | the fragment `0xeabb8..0xeae44` of the sale routine `0xeaaf8` (type record getter `0xdc320`, the five need-meter updates with their 0..100 clamps and the "litter gone up" log) | `0x118018` type record (synthetic, registry and type id checked), `0xe6d20` (drink or food message, logged), `0x5fbcc` debug log (arguments logged, format prefix checked) | 526 | **F18**: 213 differ from the old rule (store + LitterEffect, no floor, no ceiling), every one a sum outside 0..100. **Agree** 526/526 with the routine reading (`test_shop_sale_litter_reading_is_sharp`) | `ParkEconomy.TryBuy` through `shop_sale_litter` (delta of `LitterScaled`): the same 213 (C# has no guest meter, so its delta is added to the oracle's meter) |
| `guest-litter-trigger` | the first lines of the litter test in the guest update (`0xebd20..0xebd3c`) | none | 521 | no plan side. **Agree** 521/521 with the routine reading: the meter truncated, taken as a byte, `>= 90` | no hook (CSHARP_PENDING): OpenTPW has no guest litter meter |
| `staff-training-split` | `0xfc1c4` (the month-end training split), the list walkers `0x10507c` / `0x1050a4` / `0xfa9a4`, `0xf3588` (train one member), `0x108424` / `0x1082b4` (bank and park things of the game) and the debit `0xcbfdc` with its statistics | `0xb678` assertion (a zero condition fails the run) and the debug print `0x5fbcc` (logged) | 507 | **F17**: 358 differ from the former rule (the plan). **Agree** 507/507 with the routine reading | `ParkStaff.Train` through `staff_training`: **agree** (358 differed before the fix) |
| `ride-repair-duration` | `0xd9c0c` (the mechanic's repair-state entry), `0xdfe0c`/`0xdfe88`/`0xdfe90` (ride state 1, maintenance word `+96`, state 2), `0xd2290` (sub-state setter) | `0xfa9a4` thing id, `0xdfb94` the ride's mechanic id, `0xe6454` arrival state (chosen), `0xdc320` type record (the word at `+508 + 64 · level` chosen), `0xd99ac` release (recorded), `0xb678` assertion and `0x5fbcc` log (recorded); the balance block word `+1004 + 8 · grade` is `WorkDuration` | 529 jobs | **F19** (fixed in C#): 396 differ from the old claim (a flat `WorkDuration`, `plan`; the same without the upgrade factor, `flat-repair`, differs on 352). The routine reading **agrees** 529/529 | `ParkEconomy.RepairTimer` through `repair_timer`: **agree** 529/529 |
| `ride-repair-arrival` | the same | the same | 515 | **F19**: the flow (another mechanic on the ride: log and release; still walking: let go; no broken, upgrade or maintenance state: log and release; else timer, state 13, sub-state 18) **agrees** 515/515 | no hook (CSHARP_PENDING): OpenTPW starts a mechanic at once and has no walk or release |
| `object-scrap-value` | `0xe25e8` (the scrap value getter), `0xe2424` (the age percentage), `0x10a9a4` (game pointer), `0x1c4100` (64-bit divide) | `0x118018` type record (synthetic: percentages at `+392 + 64 · level + 4 · year`, costs at `+440 + 64 · level`; registry checked), `0xe4394` the clock stamp (turn × 15000 / 4 × 10^7 as 100 ns units, the value `0xdad90` stores to thing `+20`) and the glue `0x1c5c64` (the signed 64-bit stamp subtraction, as at `0xdc520`). Thing: type `+14`, stamp `+20`, level byte `+76`, use word `+412`. No thing bought after the current turn | 572 | **F21** (fixed in C#): the old rule (catalogue costs summed through the level, no 100 %) differs on 280. The routine reading **agrees** 572/572 | `ParkEconomy.ScrapValue` through `object_scrap_value`: **agree** 572/572 (the use count passed as `ParkObjectState.TimesUsed`) |
| `staff-vitals` | `0xf44c0` (moving step), `0xf459c` (working step), `0xf3d8c` (rest step), `0xf448c` (energy byte below `RestLevel`) | `0xf4d00` (frees the rest area), the role actions `0xd99ac`, `0xd5a3c`, `0xd2b04`, `0xd4b74`, `0xf0380` (logged), `0xb678`. The settings block (`RestLevel`, the grade's recuperation rates) is written in the data section | 526 | **F22**: 427 differ from the old claim (happiness fixed at 100, no energy, no rest). **Agree** 526/526 with the routine reading | `StaffVitals.Walk`, `Work`, `Rest`, `IsBelowRestLevel` through `staff_vitals`: **agree** 526/526 |
| `staff-rest-seek` | the tail of `0xf4900` (`0xf4a60..0xf4c04`) and the world LCG `0x105328` | `0xfb54c` thought, `0xf4c18` navigation (a rest area found or not), `0xf311c` state change, `0x5fbcc` log | 509 | **F22**: 81 differ from the old claim. **Agree** 509/509 with the routine reading (energy byte against `RestLevel`, the thoughts, the one-in-sixteen happiness hit with the world draw) | `StaffVitals.SeekRest` through `staff_rest_seek` (the world draw is passed in): **agree** 509/509 |
| `staff-strike-grievance` | `0xf8410` | `0x1082b4` game -> park, the two analyser sums `0xc21f4` / `0xc2264` (chosen words, arguments checked), `0xb678`, `0x5fbcc`. One chain of staff per role, decoys in the other roles | 515 | **F22**: 313 differ from the old claim (no strikes). **Agree** 515/515 with the routine reading | `StaffStrikeBoard.Grievance` through `staff_grievance`: **agree** 515/515 |
| `staff-strike-month` | `0xf7e18` (the month-end consideration), `0xf7fe0` (the escalation), `0xf8410`, `0xf8bc0` / `0xf8bd0` (active flag), `0x1091ac` (park closed word), `0x1c4100` (64-bit divide) | `0xfc1c4` training, `0xc1fb4` guests in the park (chosen), `0xe4628` clock stamp (chosen), `0x116590` / `0x11688c` message construction and posting (logged as role and kind), the grievance stubs. One run replays 1 to 6 month ends in one sandbox | 513 scripts | **F22**: 442 differ from the old claim (no strikes). **Agree** 513/513 with the routine reading (levels, flags, turns considered, messages and every debug line) | `StaffStrikeBoard.ConsiderMonth` through `staff_strike_month`: **agree** 513/513 |

The C# comparisons use these seams:

- `ParkEconomy.TryBuy` (`shop_sale_litter`): the litter store delta of one sale.
- `RideVisitorBridge.ComputeQueueLimit`.
- `ParkCellMap.CanChangeCellType`.
- `ParkEconomy.TrySpendCell` in `ParkPathBuilder.Validate` order: refuse, then the same type
  bumps, then the money test. Settings come from `EconomyTestData`.
- `RideVisitorBridge.TakeUnbounced` and `RideVisitorBridge.TakeBounceSlot`, called by reflection because they are
  internal.
- `GuestSpeed.Caps` and `GuestSpeed.Update` (`guest_speed_limits`, `guest_speed_smoothing`), with s passed and
  returned as single bits. `GuestSpeed.RaiseBoost` (`guest_ingredient_boost`): the boost, the ingredient and the amount byte; the
  thirst and toilet words of the other ingredients have no C# model and are compared with the plan only. `CSharpMutation` flips the fused multiply-add to a plain one, the floor 655 to 654 and the Sugar factor 6 to 5 (`walk-boost`).
- `GuestSimulation.ArrivalGroupSize` (`arrival_group`) on a fresh simulation: world mode 4 sets `ArrivalsHalted`, and
  the counted guests fill the simulation's guest list (a field) by reflection. The weather thing's `+48` word is passed as
  `WeatherDropsFalling` (word > 0) and the GameType mode word 1 as `OnlineVisit` (the 500 cut).
- `ParkEconomy.ParkPoints` (`park_points`) on an economy built from the input: one synthetic object type per input type
  (kind 0 a ride, kind 2 a sideshow, any other kind a shop; attraction value, decay time and the three bonuses from the
  input) and one `RegisterExisting` object per thing. The economy clock has a read-only setter, so reflection puts each
  object's build tick and the final tick on the first tick of the input turn. The type `+0x9C` word is passed as
  `Bumper.WhichTrackType`, and the park record `+28` turn is written to the first-purchase counter after the objects are
  registered, as a loaded save restores its counters (it can come from a sold copy or be unset; `ParkPointsTests`
  covers how purchases set it). The open flag (the
  chosen `0xdc834` counts-test result) is passed as the object's open state (`IsOpen`); the test's other conditions have no
  C# state and are not in the inputs.
- `RideVisitorBridge.ReliefWornOut`, `JoinDifference` and `ExcitementRefusal` (`queue_failure`, `queue_join_difference`,
  `queue_join_gate`) on a real bridge built from the input: the type's `+316` level word, the speed word and duration byte with
  their design values (`QueueParameters`), the sideshow goods, price and win byte (`SideshowTerms`, the win byte is 100 minus the
  chance of losing), track type 3 as the coaster source (`CoasterRating.GuestExcitement`, 0 before the controller exists) and the
  relief flag with the wear gauge as a single. The type's `+1960` word (`Info.WhichUIType`) 2 maps to `Sideshow`, anything else
  to `Ride`. The thing behind ride `+36` is not sent: C# has none (QUEUE-004), so the inputs with one are the remainder.
  `test_queue_join_csharp_remainder` pins that every remaining mismatch is such an input.
- `ParkEconomy.ScrapValue` (`object_scrap_value`) on an economy with one synthetic ride type whose three upgrade levels carry the
  input's `CostOfUpgrade` and `ScrapValueYear1..4`; reflection puts the build tick and the final tick on the first tick of the input
  turns, as for `park_points`, and the thing `+412` word is passed as `ParkObjectState.TimesUsed`.

- `LoanMath.MonthlyRepayment` (`loan_offers`) for each offer of an input; `LoanMath.Pay`, `ParkLedger.Post` and
  `ParkLedger.ChargeLoanInterest` (`loan_term`) run month by month exactly as `ParkEconomy.EndMonth` does, with the
  instalment M given (the original's, as C# now computes it); the real `EndMonth` is covered by
  `ParkEconomyTests`. `ParkEconomy.TryRepayLoan` (`loan_payoff`) on an imported bank with one bought loan; `ParkEconomy.TakeLoan` (`loan_take`) on an economy with the one offer (a second take refused for
  an offer already bought); the changes of balance, year profit and the month's money-in total are compared.
- `ParkEconomy.RepairTimer` (`repair_timer`) with the work duration, the gauge as single bits and, for a ride in state 2, the upgrade duration, as
  `DispatchMechanics` sends them for a job.

- `ParkStaff.Train` (`staff_training`) on a fresh `ParkStaff` over the synthetic `Standard.sam`, with each role's five
  `PoundsPerTrainingPoint` values replaced by the input's, members added with the internal `AddImported` (reflection) at the input's
  grade and points, and the five budgets set. The members' grade and points and the amount `Train` returns are compared; the
  bridge sends only real staff (the original's list may carry other things, which neither side touches).
- `StaffVitals.Walk`, `Work`, `Rest` and `IsBelowRestLevel` (`staff_vitals`) on a staff member with the chosen grade, energy and
  happiness; the rates and the rest level come from the settings in the original and are passed as the settings' singles.
  `StaffVitals.SeekRest` (`staff_rest_seek`) with the world draw passed in. `StaffStrikeBoard.Grievance` (`staff_grievance`) and
  `ConsiderMonth` (`staff_strike_month`, one call per month end on a board restored with the input's levels, flags and turns).
  The handymen's analyser sums (`0xc21f4`, `0xc2264`) are passed for the grievance; `ParkEconomy` never supplies them.

The pricing domain is the free byte clear, `game+36 == 0` and bank `+276 ≠ 0`. Old 1 → new 3
belongs to `path-can-change`.

## 5. Findings
**F17, the month-end staff training (ECON-013, `ParkStaff.Train`).**
`staff-training-split` executes `0xfc1c4` over a staff list built in memory (the list walkers, the thing array, the settings prices,
a bank and a park thing for the debit `0xcbfdc` and its statistics). The routine reading agrees on 507/507, C# agrees after the fix,
and every part is needed (`test_training_reading_is_sharp`: six variants). The routine called per member is `0xf3588`, and
`0x10507c` / `0x1050a4` are the list walkers (first id: list header `+4`; next id: thing-array entry `+8` node `+4`; both read the low
half of the word), so everything is executed, nothing is stubbed but the assertion and the debug print. What the original does:

1. Five role counters start from a five-word table (`0xfc1cc`, TOC -19008) and get +1 for each list thing whose class byte `+2`
   is 4..8. The table is read here and never written anywhere (the only reference to that TOC slot), and is zero in the file
   (`train_binary` checks it): the "base count" of the static trace is 0. The class bytes are 5 handyman, 4 mechanic, 6 entertainer,
   7 guard, 8 researcher (SAVE-plan models), and the budget words are in `StaffType` order (the staff HQ's five words), so the
   divisor for role r is the number of **all** things of that role.
2. A second walk calls `0xf3588(thing, budget[r] / count[r])` for each of them (`divw`, signed; the remainder is not spent).
3. `0xf3588`: a member at grade 4 only logs and returns. Any other member is debited the **whole share** (`0xcbfdc(bank, share)`:
   balance, a park statistic and the profit bookkeeping; `0xf3588` also books it in a second park statistic), even when
   the share buys fewer points than it could or none. The price is the settings word per class and grade (`+868 + 16 g`, `+948 + 12 g`,
   `+1008 + 8 g`, `+1056 + 12 g`, `+1116 + 8 g` for classes 6, 5, 4, 8, 7), the points are `min(share / price, 100)` (signed divide,
   price 0 is undefined), added to the byte at `+488`. A total above 99 raises the grade (`+484`), keeps `total - 100` as the points
   and resets a float at `+500`; otherwise the total is stored. With the bank's flag `+276` clear the debit does nothing but the
   points are still given (`test_training_inactive_bank`).

The former rule (ParkStaff.Train and ECONOMY.md: only members that can still train share the budget, only the bought points are spent,
points capped at the grade boundary, reset to 0 on promotion) differs on 358 of 507 inputs, the synthetic ones. It shows which rules
differ, not how often. Examples a player would notice: a budget of 1,000 for 3 handymen (grades 0, 0 and 4) at 5 pounds per point and for
2 mechanics (grade 1, 8 pounds): the original gives each trainee handyman 333 → 66 points (the top-grade handyman's third is not
spent, 666 in all) and each mechanic 500 → 62 points, spending 500 each; 1,666 in total. OpenTPW gave the two handymen 500 each (100
points and a grade: they promoted at once, 1,000 spent) and the mechanics 62 points for 496 each (1,992 in total). A budget of 300 for
a handyman at 99 points gives grade + 1 and 59 points (OpenTPW: 1 point, grade + 1, 0 points kept, 5 spent). A budget of 4 under the
price buys nothing but still costs 4. Not shown: the log text of the promotion, the float at `+500` (reset to a constant) and
`ECON-008` itself (the 100-point threshold is the same constant, so that approximation is now read as well: it can be retired by the lead).

**Fixed in C#** (`ParkStaff.Train`, `[BIN:STP-PPC:0x100FC1C4 training split]`; C# 358 → 0 mismatches): the divisor counts every employee of
the role, each employee below the top grade is charged the whole share, gets min(share / price, 100) points and carries the points above
99 into the next grade. `ParkEconomy.EndMonth` books the returned amount as one staff cost as before. A price of 0 below the top
grade (undefined in the original, none shipped) is skipped. `ECON-013` is retired from the register. Tests: `StaffTrainingTests`, the
mechanic example in `ParkEconomyTests` (the second month now keeps 20 points). The determinism hash did not change.


**F1, queue limit polarity (QUEUE-plan §4, `RideVisitorBridge.ComputeQueueLimit`).** At
`0xdcba4..0xdcbb0` the routine runs `lhz +46`, `rlwinm. r0,r0,0,28,28` and `bc 4,2,0xdcbb8`.
The routine therefore computes the QWTC formula when ride `+46 & 0x8` is **set**, and returns
the constant 100 when it is **clear**. QUEUE-plan says "HasQueue (`+46 & 0x8`): 100", and C#
follows it. First counterexample: bit clear, QWTC 14.52, CAP 249, DUR 252. The original gives
limit 100, and plan and C# give 14. With the polarity swapped, both the plan formula (515/515)
and C# (509/515) agree. Two readings remain open:

- the bit means "has no queue path";
- the loader stores HasQueue inverted.

Resolved by the review (OV-1): the ride build `0xda874`/`0xdab6c..0xdacbc` sets `0x8` from type
record `+64`, `Info.HasQueue`, so neither reading holds and the plan had the polarity backwards.
**Fixed in ORACLE-F:** QUEUE-plan §4 and `ComputeQueueLimit` return 100 for objects **without**
HasQueue and the formula for HasQueue rides (Belly Bounce: 21). Before → after: plan 515 → 0,
C# 515 → 0 mismatches.

**F2, queue limit saturation (C#).** The six remaining mismatches are all extreme:

- Five come from a quotient of at least 2³¹: DUR 0, InitSpeed 0, a huge or infinite QWTC. The
  original's `0x1c3fbc` converts to *unsigned* and saturates at 2³² − 1. C# saturates at
  `int.MaxValue`.
- One comes from SPEED ≥ 2³¹. The original reads `+84` as unsigned, while C#'s `Speed` is a
  signed `int`, so 0x80000000 gives 465 288 096 against 4.

None of these occurs with shipped data as far as known. DUR 0 trips the debug assertion at
`0xdcc98`. **Fixed in ORACLE-F:** `ComputeQueueLimit` returns a `uint`, reads SPEED as unsigned
and saturates at 2³² − 1 (6 → 0 mismatches, counted on the swapped polarity before F1 was fixed).

**F3, queue over path (PATH-plan §3.1 rule 7, `ParkCellMap.CanChangeCellType`).** At
`0x84124..0x84150`, `new 3 / old 1` is **allowed when the word at data `0x84b2c` is zero**. The
code is `lwz` via TOC −27980, `cmpwi 0`, `bne` → refuse. A nonzero word refuses the change.
PATH-plan says the change is allowed "only while the last-cell flag (`data:0x84b2c`) is set",
and C# implements `type == 3 && old == 1 && lastCell`. Every other rule agrees over the full
32 × 32 × {0, 1} grid and 500 random byte pairs. Two readings remain open:

- the word means "not the last cell";
- its writer raises it on all cells except the last.

Resolved by the review (OV-1): the only writer, the line tool `0x84ea4`, stores 0 for every
intermediate cell and 1 for the last, so the name "last-cell flag" is right and rule 7 had the
polarity backwards. **Fixed in ORACLE-F:** PATH-plan rule 7 and `CanChangeCellType` allow queue
over path except on a line's last cell (`!lastCell`). Before → after: plan 2 → 0, C# 2 → 0.

**F4, SetCellType boundaries.** The original computes `balance − cost` in 32 bits.
Balance −2³¹ with cost 1 wraps to "affordable" and charges 1, while plan and C# refuse. C#
`TrySpendCell` throws `ArgumentOutOfRangeException` for a negative cost, and the original
refuses (balance 2³¹ − 1, cost −1 overflows to negative). All 509 other inputs agree with both:

- costs by type;
- the money test `balance − cost ≥ 0`;
- the same-type placement bump with no charge;
- refusal before any write.

**Fixed in ORACLE-F:** PATH-plan §3.2 states the 32-bit difference, `ParkEconomy.CanSpendCell`
computes it in 32 bits, and `TrySpendCell` spends a negative cost (booked as other income,
`APPROX:ECON-048`, numbered ECON-046 until REVIEW-ORACLE-2 OV2-1) instead of throwing. Before → after: plan 2 → 0, C# 2 → 0.
**WALK-F2 (REVIEW-ORACLE-2 OV2-2, OV2-3):** the path writer now refuses through the same 32-bit
`CanSpendCell` (it kept a 64-bit test), and the path and queue previews use the validator's own
test (`0x84550`: a 32-bit pending line cost, skipped at 0, refused when greater than the
balance, `ParkEconomy.CanPreviewLine`). A commit runs only SetCellType's test.

**F5, UNBOUNCE (BOARD-plan §5, not on main).** C# `TakeExpired` releases the first bouncing
guest with `Until <= now`. The original (`0xae020`) releases the first occupied slot with
`deadline < now` (strict) **and** `trunc(((now − start) mod 1000) / 200) == 0`. First
counterexample: deadline = now = 31000, start 1000. The original keeps the guest, and C#
releases it. BOARD-plan §5 itself agrees with the original on all 506 inputs, so BOARD-I can
take the plan as written. **Fixed in ORACLE-F:** each bounce slot keeps its start time, and
`RideVisitorBridge.TakeUnbounced` releases the first slot with `deadline < now` inside the 200 ms
window (32-bit whole-millisecond times, C remainder); FORCEUNBOUNCE still takes the first slot.
Before → after: C# 326 → 0 (now on 508 inputs, two of them with start > now).
**WALK-F2 (REVIEW-ORACLE-2 OV2-6):** BOUNCE (`0xadf40`) fills the first free slot of the
header's bounce-size table and stores nothing when the table is full; UNBOUNCE clears a slot in
place, so the bridge keeps the original's slot order. The C# bridge now receives the whole slot
table, free slots included.

**F6, speed smoothing precision (WALK-plan §4).** Two differences:

- `0xe6b9c` is `fmadds` (3.0 · s + sum/100, one rounding). A straightforward
  single-precision port, `3f * s + sum / 100f`, differs in the last bit of `s` on 98 of 507
  inputs. WALK-I must use `MathF.FusedMultiplyAdd(3f, s, sum / 100f) * 0.25f`, or an exact
  equivalent.
- `0xe6bbc..0xe6bd8` stores `boost · 99` as a **u16** (`sth`) before the `/100`. For a boost
  above 661 the product wraps: boost 662 gives 0, not 655. Whether a boost above 661 can occur
  depends on `0xeb0f4`'s amounts: answered in F20 (one use adds at most 15, so it cannot happen in play).

The speed setter `0xffe38` agrees with WALK-plan §1 on all 518 inputs, including NaN → 655,
±inf and negative s.

**F7, coaster rating step stale register (COAST-plan §6.3).** The term that COAST-plan calls a
"stale `f0`" is not arbitrary. At the only call site, `0x3ffa8`, `f0` still holds the last force
component stored, `sample+40` = Fz (`0x3ff8c..0x3ff90`). Nothing writes `f0` in
`0x3ff94..0x3ffa4`. When `u == 0` or `Fy ≥ 0.1`, therefore, `dU = XGForceUncomfortable·|Fx| + Fz`
(signed). With that reading the plan formula agrees bit for bit on all 509 inputs. With the
COAST-I convention (0), 440 inputs differ. COAST-I should use Fz.

The step also fixes the definition offsets the plan leaves unnamed:

| Offset | Factor |
| --- | --- |
| `+812` | GForceSickness × \|ΔF\|₁ |
| `+816` | UpsideDownSickness × u |
| `+820` | Speed × v |
| `+824` | GForceExcitement × \|F\|₁ |
| `+832` | BankExcitement × \|Δbank\| |
| `+836` | XGForceUncomfortable |
| `+840` | InvertedUncomfortable |
| `+844` / `+848` | normal / terror start |
| `+852` / `+856` | normal / terror sustain |

The scream state words are set to **1** when a scream starts (`li`), not incremented.

The target writes 0 to the previous force vector (`data:0x57784`) and the previous bank
(`data:0x57780`) in every input, so the reference's |F|₁ and |bank| stand for |ΔF|₁ and |Δbank|
(the step subtracts both in double, then `fabs`/`frsp`). That is faithful to the game: only the
reset (`0x4154c`/`0x41568`) and the step read those TOC slots, and COAST-plan says they are cleared
at reset and never written afterwards (REVIEW-ORACLE OV-7, which also measured 309/309 mismatches
for this reference and 0/309 for a Δ-aware one with nonzero previous values). A later writer of
those words would need a Δ-aware reference and inputs with nonzero previous values.

**F8, coaster final section count (COAST-plan §6.4).** `0x419cc..0x419ec` counts the sections
whose flags have **both** `0x40` and `0x80` set. Each `rlwinm.` test branches past the increment
when its bit is clear. The plan says "sections without flags 0x40 and 0x80". Sickness,
excitement (clamps included), `+268`, `+272` and `+276` agree on all 506 inputs. With the
corrected count, everything agrees.

**F9, arrival group size (GUESTS.md "Arrivals", `GuestSimulation.ArrivalGroupSize`).** GUESTS.md and C# used
`(int)(1.2f × (points + NewParkBonus)) / PointsPerVisitor`, at least MinPeople, cut to 1500 − guests and never
below 0. Executed, `0xcb4e8` differs in three places. A reading with all three agrees on 515/515 inputs, and dropping
any one of them brings mismatches back (`test_arrival_reading_is_sharp`).

- **Factor (fixed in GUESTS.md and C#, ARRIVAL-002).** `0xcb564..0xcb590` calls `0x1083c8` with the game pointer
  (`0xcb558..0xcb560` load it from the TOC game slot). That routine does `addis r4,r3,30` then `lhz r0,-22748(r4)`, so it
  reads the u16 thing index at game **`+0x1da724`** (not `+0x1da734`; 20-byte entries; the neighbour `0x108424` reads
  `+0x1da736` for the bank) and returns that ThingArray entry's object. When the object's `+48` word is **positive**
  (signed `lwz`, `cmpwi`), the factor is `0.8f` (TOC −11272, `0x3f4ccccd`). Otherwise it is `1.2f` (TOC −11268).
  What the thing and the word are:
  - *Correction.* An earlier version of this finding (and PR #39) put the index at `+0x1da734` and called the thing the
    Traffic Lights fixed item, with `mTopLeft` at `+48`, so that every park with its lights used 0.8. That was an offset
    miscalculation: `-22748` is `0x1E0000 − 0x58DC = 0x1da724`, while `+0x1da734` (`-22732`) is `mTrafficLights`, which
    `0x1083c8` never reads. The Lights, their saved record (`mTopLeft = 1`) and the placed-lights rule have no part in the
    factor. C# no longer has `TrafficLightsPlaced` or `ObjectCatalogEntry.IsTrafficLights`.
  - *The thing is the weather.* The world-variable writer `0x105d3c` saves `+0x1da724` under the name `mWeather`
    (`0x1060dc` `addi r4,r4,-22748`, name string `mWeather` read from the image); `mParkGates` and `mTrafficLights` follow at `+0x1da732`/`+0x1da734` (`0x105fc4`, `0x105fec`). The world setup
    `0x10474c` creates it with the constructor `0x10298c` (`0x104a7c`, object size 104) and stores the handle at
    `0x104a98` (`sth r0,-22748(r4)`). That constructor's serializer `0x102b1c` names the object's fields `mQuality` (`+12`),
    `mLastChanged` (`+16`), `mLastForecasted` (`+24`), `mCurrentDrops` (`+48`), `mTargetDrops` (`+52`), `mLightning`
    (`+56`), `mLightningCountdown`, `mLightningNumThisPeriod`, `mThunderCountdown`, `mLastLightningLocation`,
    `mRainSoundHandle` (`+84`), `mPType`, `mWindDir`, `mWindForce` and `mOverridden` (`+100`); this is the weather
    `ParkWeather` implements. The Windows tp.exe's own save code names game `+0x1da724` `mWeather` as well (x86 oracle,
    X3),, and its arrival routine (X3) reads that handle's `+0x30` word.
  - *The `+48` word is `mCurrentDrops`.* Its writers (static, from a scan of the weather class): the constructor
    (`0x102aa0`, zero); the drop ramp in the weather tick (`0x103300..0x103348`: `+48` moves by `SpeedOfChange` (`0x103338`
    add, `0x103344` subtract) toward `mTargetDrops` (`+52`), stops once within one step, and a target of 0 then calls
    `0x103c4c` with 2, which stores 0 at `0x103d08`); and nothing else found. The target is set by the quality converter
    `0x103910`: **both** the snow band (`0x103a90..0x103afc`) and the rain band (`0x103b18..0x103b80`) compute
    `((100 − intensity) × MaxRaindrops) / 100` from their own range, outside both bands the target is 0 (`0x103b98`). Rain
    starts the rain voice (`0x103c4c` with 0), snow stops it (with 1). So the word is positive exactly while drops are
    falling: rain or snow, from the first ramp step after a quality that has a nonzero target until the ramp reaches 0.
    Lightning alone, the clear weather and a park before its first weather tick give 0 (so 1.2).
  - *Result.* The proven rule is: the signed 32-bit word at `+48` of the `0x1083c8` thing (the weather's `mCurrentDrops`)
    positive → 0.8, else 1.2. Executed by the oracle: the routine's branch and arithmetic, with the word a chosen input
    (`obj48`). Static only: that the word is the weather's drops and which weather states make it positive (the C# model of
    the ramp itself carries its own approximations, AUDIO-025..027, 029 and 030). C# takes
    `GuestSimulation.WeatherDropsFalling`, set by `Level` as `ParkWeather.CurrentDrops > 0`; with no weather (the headless M3
    and M4 gates, or before the first weather tick) it is 1.2. The snow target was an approximation (AUDIO-028: target 0)
    that this tracing resolved, so `ParkWeather` now ramps snow drops too (the rain voice level stays rain only).
- **500 cut in online parks (ARRIVAL-002, open).** When word 0 of the GameType object (`data:0x53d98`, PPC-review "New
  binding": 0 Full Simulation, 1 online, 2 Instant Action) is 1, the group is first cut to `500 − guests`. Otherwise the
  1500 cut applies. Offline parks use 1500. C# now cuts to 500 − guests for an OpenTPW online visit (`GuestSimulation.OnlineVisit`, set by
  `Level` for a non-sandbox park visit). That a visit is the original's GameType 1 is an assumption (ARRIVAL-002); so is
  whether a hosted online park runs this routine.
- **No floor.** Both cuts compare `guests + group` with `cmplwi` (unsigned) and store `limit − guests` as is. With more
  guests counted than the limit, the group is negative: 1700 guests give −200, where C# gives 0. Statically, the caller
  `0xcb2ac` creates guests only while the count is above 0 (`0xcb3bc`). Executing it with a negative group is F11
  (ARRIVAL-004): the group does not hold the arrival state.

After the factor and the online cut 49 of the 515 inputs still differ from GUESTS.md and C# (the plan and C# pin the same
inputs). All are the missing floor (ARRIVAL-004): 36 in limit mode 1 and 12 in the other modes, where more guests are
counted than the limit and the original's group is negative while C# gives 0, plus 1 where a negative group (MinPeople −5)
wraps the unsigned compare. The 42 limit-mode-1 mismatches of the first fix fell by only 6, because most of them had
guests above 500. First counterexample: shipped values, 600 guests counted, limit mode 1: the original gives −100 and
the plan and C# give 0. Before the fix the first one was the shipped values with the word positive: the
original gave 2 (0.8 × 20 / 6) and the plan 4; 125 inputs differed. Integer and float steps agree with the plan wherever
the points above do not apply: the int → double → `fsubs` conversion is `(float)(points + bonus)` with a 32-bit
wrapping sum, `fmuls` rounds to single, `fctiwz` truncates and saturates, and `divw` truncates toward zero.

**F10, park points (GUESTS.md "Arrivals", `ParkEconomy.ParkPoints`).** GUESTS.md and C# summed, over every ride and
sideshow, `(NewBonus[age / decay] + AttractionValue) / copies of that type`, with each object aged from its own build.
Executed, `0xc24c0` differs in three places. A reading with all three agrees on 507/507 inputs, and dropping any one
of them brings mismatches back (`test_points_reading_is_sharp`).

Fixed in GUESTS.md and C# since: type `+0x9C` is `Bumper.WhichTrackType` (`0xe2fe4` tests it against 3, the coaster
value), and `ParkEconomy.ParkPoints` ages an untracked type from its first purchase (the counter
`firstturn:<Info.Id>`, set like park record `+28` and saved with the park counters) and counts every copy of a
tracked type in full. The remaining 121 differences all came from the counts-test (ARRIVAL-003): a single ride that
fails `0xdc834` gives 0 in the original and 55 in C#. `ParkEconomy` now applies the part of the test it has state for,
and the bridge passes the input's open flag as the object's open state, so all three sides **agree** on 507/507. The
flag stands for the whole test in the input, which is why the agreement does not close ARRIVAL-003 (below).

- **Counts-test (`0xdc834`).** Only things for which it returns nonzero count. Executed in the target
  `park-points-counts-test` (522 inputs, the returned word and the order of the stubbed calls agree with the reading
  below). In the order it runs:

  | # | Test | Meaning | Evidence | C# |
  | --- | --- | --- | --- | --- |
  | 1 | thing `+46` bit `0x4` | `Info.IsChoosable` (type `+60`; the build path sets the bit at `0xdabdc..0xdabf0`) | executed; the key is static (descriptor table `0x39b64`). The constructor sets the ride state to 0 when the bit is set and to 3 otherwise (`0xdad10..0xdad34`) | `EconomyObjectInfo.IsChoosable` |
  | 2 | `0xdfe0c(thing)` must be 0 | ride state `+408` is 1, BROKEN_DOWN (the log text "Setting state BROKEN_DOWN" belongs to the call with state 1, `0xe1908`) | executed; the state name static | `IsBrokenDown`, and `ParkObjectState.ScriptBroken` (the script's VAR_BROKEN, variable 7, which `0xe1864` reads to enter state 1 or 4; mirrored by the level each tick) |
  | 3 | `0xdff18(thing)` must be 0 | the word `+100` is 0: **closed**. It is 1 at construction (`0xdab5c`), set to 1 by the open routine `0xde7e4` after its can-open checks (`0xdf4a0`, `0xdf8d8`, `0xdfad4`) and cleared by closing (`0xe0de8`, `0xe1004`, `0xe1220`, "Closing...") | executed; the open/close role static | `IsOpen` |
  | 4 | `0xdfe20(thing)` must be 0 | ride state is 4, CONDEMNED (log "Setting state CONDEMNED", `0xe1954`): the breakdown path `0xe1864` picks 4 instead of 1 when the truncated life gauge (`+68`) is 0 | executed; the state name static | `IsBrokenDown` stands for it (a repaired ride is not condemned in C#; whether a condemned one stays so is untraced) |
  | 5 | `0xdd3f4(&cell, thing)` must differ from the u16 behind TOC −19648 | the back-of-queue cell: `+54` when cached, otherwise the front cell (`0xdd2b4`, the neighbour of the entrance cell `+50` in its connection direction, 0 when none) followed along the queue links; so **the entrance connects to a cell**. The compared u16 is 0 in the file and read only here | stubbed (the cell chosen); the null value is checked | missing: no entrance cell or path connection in the economy |
  | 6 | `0xdc988(thing)` must be nonzero | listed guests (`0xdd118`) below `4 × +60` (queue cells), unsigned: **the queue is not physically full** | executed with `0xdd118` stubbed | missing: no queue state in the economy |
  | 7 | `0xe2fe4` then `0x45eac` | track type 3 (a coaster) needs the controller to exist and `+320` (riders on board) to be 0. The extra "complete, not editing" part of `0x45eac` cannot change the result | executed (`0xe2fe4`); `0x45eac` stubbed, its reading static | missing: `RideOperation.Riders` is the script's VAR_ONRIDE, not shown to be the controller's `+320` |
  | 8 | type `+156` 1 or 2, thing `+40` 0 | a kart or water ride without the word `+40`. The constructor sets it to 1 (`0xda8e8`) and no other store of a ride was found; what would clear it is untraced (the queue-edit reopen `0xdd70c` tests it too) | executed | no state: always satisfied |

  ARRIVAL-003 stays open for the conditions with no state (5, 6, 7, 8) and for the reading of 4 as `IsBrokenDown`. Not in
  the test: the maintenance flag `+96` and the maintenance state 2 (`0xdfe88`, `0xdfe90`), which the open routine
  `0xde654` does check.
- **Age per type.** When type `+0x9C` (156) is 0, the age comes from the park's per-type record (`0xd1be8` on the
  table at game `+0x1d943c`, 32-byte entries keyed by type id). The object purchase routine `0xda874` writes the
  current turn (game `+0x1da70c`) to its `+28` only while `+28` is still 0 (`0xdadec..0xdae14`). Purchases at turn 0
  therefore leave it unset, and the first purchase at a later turn fixes it. All copies of the type share that age, so
  a copy bought later gets no fresh bonus. The purchase also raises `+24`, the removal path lowers it
  (`0xdbfcc..0xdbfdc`), and `0xc5828` returns it as the divisor, so a closed copy still counts in the divisor. Statically,
  no removal path writes `+28`; it is zeroed when the table is reset (`0xd1d1c`) and restored by the save loader
  (`0xd1e8c`). `0xe4750` turns the difference into 100 ns units: `(now − turn)` as an unsigned word times clock `+28`
  (15000), divided by 4, times 10^7. `0xc24c0` divides that by 864,000,000,000 (one day) with the signed 64-bit helper
  `0x1c4100` and keeps the low word.
- **Copies of other types.** When type `+0x9C` is nonzero, the age is the thing's own (`0xdc520`: the clock's
  timestamp minus the one stored at thing `+20` at purchase, `TbTimeStamp::operator-`) and the divisor is 1. Every copy
  then counts in full, so these copies do not count once.

First counterexample: one ride type (AttractionValue 25, decay 2 days, bonuses 30/20/10, `+0x9C` 0) with copies
bought at turns 1 and 73 (three park days apart), evaluated at turn 73. The original ages both from turn 1 (bracket 1:
(20 + 25) / 2 twice, 44); the plan ages the second copy as new (22 + 27 = 49). The inputs use the clock rate 15000 and
never place a build after the current turn; the unsigned turn difference in `0xe4750` would age such a build by
years, where C# clamps to 0, but no purchase can produce it. The stub for `0xdc520` uses the same day scale with a
signed difference; the routine itself is not executed.

**F11, a negative group in the arrival update (GUESTS.md "Arrivals", ARRIVAL-004, `0xcb2ac`).** The static reading of the
caller said a negative group would hold the arrival state: guests are created only while the group is above 0
(`0xcb3bc`), and `0xcb488` tests exactly 0. Executed, this is wrong. `0xcb2ac` runs from the park manager's update `0xd67f0` (via `0xcb278`) on its
arrival object (park manager `+708`: `+4` timer start, `+12` group, `+16` active byte). Its `bc 4,1` at `0xcb3c0`
branches on "not above 0" (signed), so every group at or below 0 takes `0xcb3f4`. That restarts the timer (`0x121098`
stores the world turn at `+4`), clears the active byte and starts the arrival vehicle script with size 0 (frame slot 80).
Scripts of repeated calls, with the vehicle phase and report flag chosen per call:

- **Same as 0.** With 1700 counted guests the group is 1500 − 1700 = −200 (limit mode 1 with 600 guests gives −100). The
  group is stored negative and the active byte is set (the group history `0xcb750` receives −200). At the next call
  with the vehicle phase 2 (`0x109784`, the vehicle at its stop) no guest is created, and the timer restarts at that
  turn, the active byte clears and slot 80 runs. Guests created, active byte and timer start agree with a floored group
  on all 510 scripts (169 of them start a negative group, 1348 negative starts in all). The group word stays negative
  until the next group replaces it.
- **Not held, but gated by the vehicle.** While the phase is −1 or 6 the active byte stays set for any group, and the
  vehicle script runs with size 1 (group up to 35, 60, more: 1/2/3; a negative group gives 1, as 0 does). Only phase 2
  ends it. Neither a negative nor an empty group ends earlier. What phases occur in play is not traced (ARRIVAL-001).
- **What differs.** After the group handling, with `0x109b78` nonzero and phase 2, an exactly empty group starts one more
  vehicle step with size 0 (slot 74, `0xcb488`); a negative group skips it. The two readings differ in the stored group
  word, the history value and this step on 158 scripts beyond the history values. The vehicle step with size 0 chooses
  its size from the world LCG when no vehicle is active (`0x1093f4`); whether the skipped step consumes a draw was not
  traced.
- **Other writers.** A static search for stores to the arrival object's group and active byte finds only the constructor
  `0xcb028` (called from the park manager constructor `0x104534`, executed here: timer 0, group 0, active 0, `+8` = 5,
  `+17` = 1) and the save/load serialiser `0xcb050` (timer through `0x121000`, group through `0xc7f8`, active byte and
  `+17` through `0xca20`). A saved game therefore restores the group and active byte as saved. No other writer was
  found; a store through a pointer copied elsewhere would not show up in this search (static reading).

Result: ARRIVAL-004 changes no guest or timer behaviour. The C# floor at 0 produces the same guests and timer as the
original's negative group, differing only in the stored group word and the untraced empty-group vehicle step. The
brief's static reading, a negative group holding the state, differs on 66 scripts and is refuted. The
`plan` side models the stop gate as the vehicle phase 2, where `UpdateArrivals` uses a fixed 0.4 s spacing (ARRIVAL-001). C# is not compared.

**F12, loan instalment (ECON-006, `LoanMath.MonthlyRepayment`, `0xcb7a8`; fixed in C#).** The constructor of the bank object fills
eight loan records. Its loop (`0xcb84c..0xcb904`, run as a fragment) reads amount P, APR A, months N and lender from the
settings block (`+416`, 16 bytes per offer; `+408` is the initial cash) and stores, per record (32 bytes from the bank
object): `+20` available, `+24` P, `+28` A, `+32` N, `+36` instalment M, `+40` bought 0, `+44` months repaid 0, `+48` lender.
The instalment is computed in binary64 as `trunc_u32(P × pow(1 + A/100, (N/12) × 0.5) / N)`: the constants in the TOC
are 100, 12, 1 and 0.5, `0xcb8cc` calls `pow` (import glue `0x1c61a4`) and `0x1c3fbc` converts to unsigned (negative
gives 0, NaN and 2³² and above give 2³² − 1). `pow` is the one stub that stands for code outside the binary: the
MathLib import is replaced by Python's `math.pow`, which can differ from MathLib in the last bit. A last-bit
difference changes M only when P × pow / N lies that close to an integer; at 0 % APR `pow` is exactly 1.

The formula is not an annuity. It charges P × (1 + A/100)^(N/24): compound interest on the whole principal for half the
term (a flat average-balance model), then divides by N. Executed on the eight shipped offers (PPC-economy):

| P | APR | N | original M | M × N | former C# annuity M |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 100000 | 20 | 36 | 3651 | 131436 | 3716 |
| 50000 | 20 | 36 | 1825 | 65700 | 1858 |
| 25000 | 20 | 36 | 912 | 32832 | 929 |
| 10000 | 20 | 36 | 365 | 13140 | 371 |
| 18000 | 23 | 24 | 922 | 22128 | 942 |
| 30000 | 22 | 30 | 1282 | 38460 | 1308 |
| 80000 | 18 | 48 | 2320 | 111360 | 2349 |
| 65000 | 21 | 30 | 2749 | 82470 | 2803 |

First counterexample: the first row. Against the routine reading all 505 offer sets (4040 offers) agree, including
the bank words the constructor sets (balance = initial cash, batch 0, withdrawals enabled 1, last balance 0, turn entered
red 0, year profit 0). Dropping any one part of the reading brings mismatches back (`test_loan_offers_reading_is_sharp`:
the exponent N/12, the APR divisor 1200, rounding instead of truncating, the annuity). At 0 % APR the routine
gives floor(P/N), as the Easymode save does (the only offers where the old C# annuity agreed). The initial available flag is
`amount ≤ 10000`; later code (`0xcc6d0`, `0xcc9e8`, ECON-007) sets it again, which the saves show (25,000 and 18,000
offers available in Lost Kingdom). A zero term divides by zero and saturates to 2³² − 1 (no shipped offer has one; C# throws).

Fixed in C#: `LoanMath.MonthlyRepayment` now computes the same expression in binary64 with the same float-to-unsigned
conversion, for every APR, so the `plan` and `csharp` sides of `loan-offers` agree with the original on 505/505 offer
sets (497 of 505 differed before). `OriginalEconomyImport.Verify` accepts and checks the stored M at every APR.

Independent check without the oracle: original PC saves of Standard mode store the same eight instalments. Decoded with
`SaveEconomyRecords`, the jungle `Lost Kingdom.TPWS` and `autosave.TPWS`, `Halloween World.TPWS`, `Space Zone.TPWS` and
`Wonder Land.TPWS` of the Nexus mod's "All Researched Saves" all hold 3651, 1825, 912, 365, 922, 1282, 2320 and 2749 with
the loans unbought. These values were written by the original's own code, so the formula is not just a reading of the
binary. (No Standard save with an outstanding loan was available.)

**F13, monthly loan step (ECON-006, `LoanMath.Pay`, `0xcc21c`; fixed in C#).** The monthly handler (bank message 12) first moves the
batch credit (`+16`, asserted below 1,000,000) into the balance (`+12`), the money-in word (park `+0x1FC90`) and the year
profit (`+292`). Then, for each bought record in index order, with withdrawals enabled (`+276`): the balance falls by the stored
M (the turn the balance first goes negative is stored at `+288` when the last balance `+284` was not negative, and `+284`
follows the balance), the money-out word (park `+0x1F5A0`) rises by M and the year profit falls by M. Whatever the
withdrawals flag, months repaid rise by 1, the year profit then gains M − floor((M × N − P) / N) with the subtraction in
32-bit unsigned arithmetic (so a net −floor((M × N − P) / N) with withdrawals), and when months repaid equal N the
bought flag and months repaid clear. The routine reading agrees on 506/506 random bank states (1-3 months, any batch,
withdrawal flag, red state, bought flag 0/1/2 and months repaid) and 503/503 whole terms, with every assertion and debug
message in the same order (`test_loan_month_reading_is_sharp` drops each part in turn).

Executed over a whole term, the original therefore deducts the stored M exactly N times and nothing else. There is no
remaining balance, no interest on a balance and no last-month remainder. C# used to accrue APR/12 on the remaining
balance each month, pay min(balance + interest, M) and settle the remainder in the last month; with the original's M the
two differed on 483 of 503 terms (the 20 that agreed were 0 % loans where M × N = P exactly and one- or two-month terms).
Example, 100,000 at 20 % over 36 months:

- Original: M = 3651 for 36 months, 131,436 in all. The year profit falls by 873 a month (131,436 − 100,000 = 31,436 /
  36), 31,428 in all because of the integer division.
- OpenTPW before: M = 3716 and 133,795 in all (`LoanMath.TotalPayable`). After: M = 3651, total payable M × N = 131,436,
  and early repayment after 12 months pays 3651 × 24 = 87,624 (before: the declining balance with interest).
- 0 % APR: M = floor(100000 / 36) = 2777, and the original deducts 36 × 2777 = 99,972, 28 less than was lent. C#
  now does the same (it used to settle the remainder, 100,000). The year-profit share (2777 × 36 − 100000) / 36 wraps in
  32 bits to 119,304,646 a month (the 32-bit wrap `InstalmentInterest` already reproduced).

Early repayment (`0xccc78`, executed by the `loan-payoff` target) refuses a loan that is not bought and a balance below the
payoff (an unsigned compare), then pays M × (N − months repaid) and clears bought and months repaid, so it needs no remaining
balance. It clears the months repaid before it reads them again, so the year profit is charged the share of the **whole**
term, q × N with q = (M × N − P) / N (unsigned 32-bit), even after a partial repayment. Paying off 100,000 / 36 months / 20 %
after 12 months takes 87,624 off the balance and 31,428 off the year profit. C# `TryRepayLoan` pays `RemainingBalance` =
M × (N − months repaid) and charges `LoanMath.PayoffInterest`; `plan`, routine reading and C# agree with the original on 506/506
inputs (`test_loan_payoff_100000_at_20_percent_after_12_months`, `test_loan_payoff_reading_is_sharp`; the withdrawals flag
and a negative balance are covered by the routine reading only).

**The 0 % APR quirk is original behaviour and is kept on purpose.** At 0 % APR M = floor(P / N), so M × N < P whenever N
does not divide P, and the unsigned subtraction M × N − P wraps: the share is about 119 million a month (100,000 over 36
months). Jungle Easy mode has only 0 % loans, so it swings the year profit by that much per loan and month, and the
32-bit word wraps (two loans reach −2,147,483,628 after nine payments, and a $21 cost then wraps it to +2,147,483,647, as the
original's `stw` does). Do not "fix" the share or the 32-bit wrap: the profit ticket reads this word signed
(`0xCCFA8`, `0x14048`).

Fixed in C#: `LoanAccount` stores the months repaid; `LoanMath.Pay` pays exactly M and counts a month, `TotalPayable` is
M × N, the remaining amount is derived, instalments run in offer-index order, and the monthly step keeps the original's
year-profit bookkeeping. `ParkLedger` keeps the year profit a signed 32-bit word on every update (`Post`, `ChargeLoanInterest`, restore), as the
original stores it (at 0 % APR the interest share is about 119 million, so the word wraps within a year). The balance,
money-in and money-out words are 32-bit in the original too, but the park cannot reach 2³¹ there, so C# keeps 64 bits. The `plan` and `csharp` sides of
`loan-term` agree with the original on 503/503 whole terms. The
withdrawals flag (`+276`) has no C# counterpart and is assumed on (ECON-051). Saves written by the old model (version 2
with a remaining balance) load with their months left; the old balance is replaced by M × months left.

**F14, a loan received (ECON-005, `0xcc904`).** Taking an offer that is not bought logs, then credits P to the balance
(`+12`), the money-in word (park `+0x1FC90`) and the year profit (`+292`), and takes the same P off the year profit; it
sets the bought flag to 1 and returns 1. An offer already bought logs and returns 0. The credit is the same
as `0xcbf50`, the bank's ordinary credit (balance, money in, year profit), so a loan is **money in** and leaves the year
profit unchanged. `ParkEconomy.TakeLoan` (`LoanReceived` is income, excluded from `ProfitThisYear`) agrees on all 505
inputs (balance, year profit, money in, bought flag), as does the plan of ECON-005's loan clause. The routine reading
agrees on 505/505 (including the assertion at 1,000,000). The remaining ECON-005 text (challenge prizes, scrap sales,
build and upgrade costs as other income or costs) is not a loan matter and stays open. One difference in kind:
`LedgerMonth.Profit` (monthly) charges whole instalments, where the original's year-profit word `+292` takes only the
interest share; `ProfitThisYear` is the original's field and follows it.
**F15, the ride-failure predicate 0xdfe34 (QUEUE-008, GUESTS.md "Breakdown").** Executed alone with no stubs
(`queue-failure-predicate`, 521 inputs). The routine never reads the script. It loads the ride's `+46` half-word and tests bit
`0x1`. When that is set it truncates the float `+64` with `fctiwz`, takes the low byte as an unsigned value and returns 1 when
that is below 25.0 (TOC −11108). What the two fields are:

- `+46` bit `0x1` is `UsageInfo.ProvidesRelief`: the ride build ORs it in from type `+244` (`0xdab6c..0xdab84`, REVIEW-ORACLE
  OV-1). It is the toilet flag.
- `+64` is the wear gauge. The constructor stores 100 (`0xdab1c`), full repair writes 100 (`0xdef78`) and wear subtracts from it
  (PPC-economy ECON-021..024).

So `0xdfe34` means "a relief object whose wear gauge fell below 25". QUEUE-008 (and GUESTS.md "Breakdown") read it as
`VAR_BROKEN ≠ 0`: 310 of 521 inputs differ. First counterexample: a ride with `VAR_BROKEN` set and a full gauge, where the
original gives 0 and the plan 1. The routine reading (relief bit, byte of the truncated gauge, below 25) agrees on 521/521,
and dropping the flag, the byte wrap or the limit brings mismatches back (`test_queue_failure_reading_is_sharp`). The byte wrap
matters only outside the gauge's range (wear clamps to 0..100): 256.0 wraps to 0 and counts as failed, −1.0 wraps to 255, NaN
counts as failed.

What this does to the queue (executed, `queue-ride-update`, F15 fixed in C#):

- *Guest state 11, step 3 (`0xed424`).* The original's exit "0xdfe34 set, thought 14 (feeling sick), leave" fires for a worn-out
  toilet only. For any other ride it never fires, so a broken ride (script `VAR_BROKEN ≠ 0`) does not send its queue away through
  this exit. C# did (QUEUE-008, state-11 step 3): its queue emptied through an exit the original does not have for rides.
- *What empties the queue instead: the ride update `0xe0a8c`.* It dispatches on the ride state `+408` (executed alone with its callees
  recorded, 510 inputs, agreeing with the reading): state 0 runs the admission `0xe1864`, which after `0xe1404` reads script variable 7
  and calls `SetState(1)` (or `SetState(4)`, condemned, when the truncated life gauge `+68` is a zero byte); state 3 does nothing; states
  1, 2 and 4 run the head-guest step on every update and then release the script's rider (`0xe1758`); any other state asserts.
  The head-guest step: a head that is not the guest in VAR_LETMEON (the script took it) and waits in state 14 is forced aboard
  (`0xeda04`); otherwise a non-empty queue loses its head (`0xdcebc`, clearing VAR_LETMEON when it names it) and the guest is dismissed
  (`0xee804`: "Person %d: dismissed from queue on ride %d", reason 6, no thought, happiness down by `PeepInfo.MediumHappinessChange` through
  `0xeb6f4` selector 1, the byte at settings `+28`, target id cleared, state 6).
  So **a broken ride's queue drains head first, one guest per ride update (park turn)**; the guests behind keep their places (state 11
  does not move them up while the ride is in state 1, and has no exit for it) and wait (`0xed4b0` calls `0xdfe0c`, true only in state 1, and skips the walk to the new position; C# holds them while `IsBroken`). The dismissed guests and the rest are not sent
  away "feeling sick". `SetState(1)` (`0xe0ec4`, "Object %d: Closing...") runs the same head step once, zeroes the pending visitor `+104`,
  sets script variable 6 (VAR_RIDECLOSED) to 1 and clears the open word `+100`; the `SetState(0)` callers write them back (`0xdad24` at build,
  `0xde8e4`, `0xdf4d4`, `0xdf90c`, `0xdfb08`; which one a repair uses is not traced).
  Not executed: the dismissed guest's walk afterwards (state 6, QUEUE-010 in C#), what the callers of `SetState(0)` and `SetState(2)` are.
- *Other callers of `0xdfe34`.* `0xd6618`, `0xdc104`, `0xdf5d0`, `0xe9aa4` and `0xed424`. The first three sit in ride and toilet update code
  next to the maintenance flag (`0xdfe88`) and the relief test (`0xdc0f4`); not traced further.
- *Admission (QUEUE-019).* `0xdfe34` has no part in it. `0xe1864` runs the admission `0xe1404` first and then reads script
  variable 7 itself (`cmpwi r3,0`, so `≠ 0`): the breakdown turn still admits. The script predicate the original does have is
  `0xdfed4`: it returns `VAR_BROKEN > 0` (signed; the `eqv/subfc/addze` idiom), and its only caller is the repair-state code at
  `0xd9e80`.

**Fixed in C#.** The queue state-11 exit is `RideVisitorBridge.ReliefWornOut` (`QueueJoinRules.ReliefFailed`: the relief flag and the wear
gauge as a single, truncated, low byte below 25), and a broken ride dismisses its head each park turn (`RideVisitorBridge.DismissHead`,
`GuestSimulation` `OnVisitorDismissed`: medium happiness loss, no thought). OpenTPW wears only rides, so a toilet's gauge never falls and
the exit does not fire in practice (QUEUE-008); the reading of a breakdown from `VAR_BROKEN` at once (QUEUE-019) and that the C# ride
does not write VAR_RIDECLOSED or the open word stay approximations. Before → after: the old
claim (`IsBroken`) differed from the original on 310 of 521 inputs; `ReliefWornOut` agrees on all 521.

**F16, the join excitement gate (QUEUE-004, `RideVisitorBridge.JoinQueue`).** `queue-join-difference` runs `0xe9a84` with
`0xe02ec` and `0xdff70` and their callees, `queue-join-gate` runs the state-10 fragment around the gate. The routine reading
agrees on 524/524 for each, and every part of it is needed (`test_queue_join_reading_is_sharp`: 14 variants of the gate, 10 of
the difference). What the original does, in order:

1. `0xeb7e4`: the guest's preferred excitement is the low byte of the settings word at `+164 + 12 · type`, with the type the
   byte at guest `+496` (the `PeepTypes[n].PreferredExcitement` column; the other two words are cash and boredom).
2. `0xdfe34(ride)` (F15). When it holds the difference is 100 and nothing else is computed.
3. Otherwise the ride's excitement is `0xe02ec(ride, 0)`, taken as a byte. The difference is `clamp(excitement − preferred, −50,
   50)`, with both operands bytes.
4. The gate is **not** by kind. `0xe02ec(ride, 1)` returns the low byte of the type's `UsageInfo.ExcitementLevel` (type `+316`),
   and the guest is tested only when that is nonzero: `|difference| ≥ 45` (signed compare `cmpwi 45`, `bc 4,0`) refuses. A
   negative difference (the ride is below the preference) logs "ride is not exciting enough!", reason 5 and thought 12
   (bored). Otherwise "ride is too exciting!", reason 4 and thought 15 (scared). The stubs read the message texts, and the
   thoughts match C#'s `Bored` and `Scared` with the sign flipped (the original subtracts the preference from the ride, C#
   the ride from the preference).

The excitement `0xe02ec(ride, 0)` is returned as 0 when the type's `+316` word is 0. Otherwise `0xdff70` computes it by type
(`+1960`, `Info.WhichUIType`):

| Case | Excitement |
| --- | --- |
| Sideshow (`+1960 == 2`) | `20 + trunc(0.8f · (win · sqrt(clamp(goods − price, 0, 100)) / 10))` with `goods = +388` (copied from `UsageInfo.InitCostOfGoods` at build, `0xdab38`), `price = +400` (`InitPricePerUse`) and `win` the byte `+396` (100 − `InitChanceOfLoosing`). The single-precision `0.8f` is TOC −11112 |
| Track type 3 (a coaster) | no controller (`0xb5e24` is 0): 0. Otherwise `trunc(0.5 · E + 50)` (fused single) with `E` the float at controller `+260` (`0x41a14` reads `+260`, not `+256`), as COAST-plan 6.1 says |
| Everything else | `base · clamp(speed / InitSpeed, 0.75, 1.25) · clamp(duration / InitDuration, 0.75, 1.25)`, in single precision, truncated. Speed is the word `+84` over `Upgrades.InitSpeed` of the ride's level `+76`, duration is the byte `+88` over `Upgrades.InitDuration`. `base` is the type's `ExcitementLevel` word, or, when the word `+36` (the handle passed to the ride build `0xda874`) is nonzero, `trunc(level · 60 / 100)` plus `clamp(3 · p3 + 2 · p1 + p2, 0, 40)` over three counts of `0x1ea40` (what they count is not traced), clamped to 0..100. A zero duration divides to infinity and clamps to 1.25; 0 / 0 is NaN, which truncates to `0x80000000`, a zero byte |

The plan (QUEUE-plan 4, C#) uses the type's static level for everything but coasters and gates rides and sideshows by kind.
That differs in outcome on 155 of the 524 inputs, the same ones in C# through `RideVisitorBridge.ExcitementRefusal`:

| Cause | Inputs |
| --- | --- |
| sideshow formula | 54 |
| a ride or sideshow whose `ExcitementLevel` byte is 0 is not gated | 32 |
| a shop or feature with an `ExcitementLevel` byte is gated | 32 |
| a ride off its design speed or duration, or with a `+36` thing | 22 |
| relief object with a gauge below 25 (difference 100) | 15 |

The counts depend on the inputs, which are synthetic: they show which rules differ, not how often. Examples a player would
notice (both in `test_queue_join_refusal_trace` or the edges): a sideshow with the `SideShow.sam` default level 30, a prize
25 above its price and a win chance of 50 gives excitement `20 + trunc(0.8 · 50 · 5 / 10) = 40`. A guest preferring 80 joins
in the original (difference −40) and turns away bored in C# (80 − 30 = 50). A ride of level 100 running at a tenth of its
design speed scores `100 · 0.75 = 75`, so a guest preferring 50 joins in the original (25) and turns away scared in C#
(−50). With the shipped data the kind and the byte rule coincide (`Rides.sam` defaults 60 and `SideShow.sam` 30; shops have no
level), so only the live values matter in practice. Not shown: what `+36` and its three counts are, how often the live
inputs move during play (the price and the win chance can be edited), and the other callers of `0xe02ec` and
`0xeb7e4` (`0xe9438`, `0xe9478`, `0xea348`, `0xea4e0`: the attraction score code, PPC-guests, not traced).

**Fixed in C#** (before → after: `queue-join-gate` C# 155 → 61 mismatches, all with a ride `+36` thing; `queue-join-difference` C# had no
seam, now 46 of 524, all with such a thing). `QueueJoinRules` holds the relief predicate, the sideshow, scaled and difference formulas
(single precision where the original is single, `fctiwz` saturation with NaN as `0x80000000`) and the level-byte gate;
`RideVisitorBridge` feeds them from the object's data and live state: the speed word and duration byte against `InitSpeed` and the raw
`InitDuration` (`QueueParameters.InitDuration`: a ride whose `InitDuration` lies outside `MinDuration..MaxDuration` has a duration ratio
other than 1; four shipped rides have such data and score 1.25 × their level: Brain Buster 40 → 50, Zero G 65 → 81, Bumper Bugs 70 → 87 and
Jelly Bounce 40 → 50, `InitDuration` 3 under `MinDuration` 10 or 15; `GuestAssetTests.ShippedObjectsScoreTheirLiveJoinExcitement`), the live goods, price and win chance of a
sideshow (mirrored from the economy each tick by `Level.SyncObjectEconomy`) and the coaster's `ExcitementSource`.
`GuestSimulation` passes the guest's preferred excitement instead of a difference. Examples from `QueueJoinTests`: a sideshow with the
default level 30, a prize 25 above its price and a win chance of 50 scores 40, so a guest preferring 80 joins (it turned away bored in
C# before: 80 − 30 = 50); a ride of level 100 at a tenth of its design speed scores 75, so a guest preferring 50 joins (it turned away
scared before: 50 − 100 = −50). With the shipped data rides run at their design speed and duration (level 40..95 as live value, but for Brain Buster), so the visible
changes are the sideshows (live 27 for the jungle Arcade, 33 for the Halloween Arcade and 37 for the space Arcade against their levels
30), the clamp and those four rides. The remainder: the excitement of a ride with a thing behind ride `+36` (60 % of the level plus up to 40 from the
three counts of `0x1ea40`) is not modelled (QUEUE-004), because OpenTPW has no such thing and what the counts are is not traced.
Not changed: the attraction score (`ScoreAttraction`) and the ride outcome (`ApplyVisit`) still use the static `ExcitementLevel`;
their callers `0xe9438`, `0xe9478`, `0xea348` and `0xea4e0` of `0xe02ec` and `0xeb7e4` are not traced (PPC-guests).

**F18, what a shop sale does to the guest and how much litter that is (ECON-020, `ParkEconomy.TryBuy`, `0xeaaf8`; C# unchanged).**
The static trace (APPROX-TRACE ECON-020) read `0xeaaf8` as "adds the raw per-use value to a litter float at `+0x1b4`, no division by
100", and the old C# rule was "a sale drops LitterEffect/100 litter items" (`LitterScaled += LitterEffect`, 1/100 item). The fragment
`0xeabb8..0xeae44` is executed (`shop-sale-litter`). It is the stat-update step of the sale routine, run by the guest (`r28`, the first
argument) on the shop thing (`r3`, type id at `+14`). The type record comes from `0xdc320` (`0x118018` with the id at `+14`), and the five
words from type `+0x144` on go to five guest floats:

| Type record | Key (shop `.sam`, in the file order after `InitCostOfGoods` at `+0x140`) | Guest float | Update |
| --- | --- | --- | --- |
| `+0x144` (324) | `UsageInfo.ThirstEffect` ("How much thirst to deduct") | `+0x1a4` (420) | minus, clamp 0..100 |
| `+0x148` (328) | `UsageInfo.HungerEffect` ("How much hunger to deduct") | `+0x1a8` (424) | minus, clamp 0..100 |
| `+0x14c` (332) | `UsageInfo.VomitEffect` ("How much vomit to add") | `+0x1b0` (432) | plus, clamp 0..100 |
| `+0x150` (336) | `UsageInfo.HappinessEffect` ("How much happiness to add") | `+0x19c` (412) | plus, clamp 0..100 |
| `+0x154` (340) | `UsageInfo.LitterEffect` ("How much litter to add") | `+0x1b4` (436) | plus, clamp 0..100 |

So the field that feeds the litter amount is type `+0x154`, the catalogue word 0x55 after `InitCostOfGoods` (word 0x50, `+0x140`), which is
`LitterEffect`. The order is not inferred from one comment: the key table in the data section lists `InitCostOfGoods`, `ThirstEffect`,
`HungerEffect`, `VomitEffect`, `HappinessEffect`, `LitterEffect`, `SpecialIngredient` consecutively, the shop files' comments give the same
verbs (deduct, deduct, add, add, add), the fragment subtracts exactly the first two, and the word after litter (`+0x158`) is the ingredient
switch value that the routine tests next (`0xeaf44`, `SpecialIngredient`). Each update is `clamp(float(old) +- float(effect), 0, 100)` in single precision (a NaN
meter passes the clamp), the litter step is `0xead48..0xeae14`, and when `LitterEffect > 0` the routine logs "Litter gone up by" with
the effect and the new meter as a byte (`0x5fbcc`, level 2). The routine reading agrees on 526/526 inputs, and the sign, effect slot and
clamp of every meter, the log test and the surplus clamp each change a result (`test_shop_sale_litter_reading_is_sharp`).

What the number means. The litter value is the guest's own meter `+0x1b4`, not a park total, and it is the same 0..100 scale as the other
needs. The sale is the only code that adds to `+0x1b4`; the other writers set an initial value (guest construction `0xe76b8`, `0x106fe8`), clear it to 0 (`0xe78c4`) or reset it to 0 after the drop (`0xebdf8`, `0xed1b0`, `0xed228`; the constant is 0.0). The guest update tests the meter at `0xebd20`: truncated, as a byte, `>= 90`
(`guest-litter-trigger`, 521/521). From there the guest state (`+544` in 6, 7 or 10) and a search for a place (`0xee1c4(guest, 3)`: a
radius-3 loop over the thing array for objects with `+46` bit `0x40`, probably a litter bin; static only, not executed) lead to going to
that place (state 9) or, when none is found, to a drop (`0xd8548` on a cell) and the meter reset to 0. So a sale does not drop `LitterEffect`
items and does not drop 100 times too many: it moves a 0..100 meter, and one litter event follows once it reaches 90.

The static trace's "contradicted (scaling)" is therefore a misreading of the unit, not a 100x error. Executed with the shipped
LitterEffect values (`test_shop_sale_litter_items_per_sale`), a guest who keeps buying from one shop reaches the drop test after
`ceil(90 / LitterEffect)` sales:

| Shop (shipped `LitterEffect`, all four themes) | Sales per drop (original) | Litter items per sale: original | Old C# rule |
| --- | --- | --- | --- |
| burger, fries, drinks (50) | 2 (50, then 100) | 0.5 | 0.5 |
| ice cream (25) | 4 (25, 50, 75, 100) | 0.25 | 0.25 |
| steak restaurant, restaurant, moonrock cafe (10) | 9 (the ninth sale reaches 90) | 0.111 | 0.1 |
| balloon, costume, gift (0) | never | 0 | 0 |
| category default (5, none ships it for a shop) | 18 | 0.056 | 0.05 |

Example, a burger sale (jungle Burger Shop, Info.Id 1207, `LitterEffect` 50): the old rule adds 50 to the park's store (0.5 item); the
original adds 50 to the buying guest's meter (0 to 50) and the second burger bought by that guest takes it to 100 (50 + 50) and starts the
drop, one item per two sales: the same 0.5 per sale on average. The differences are second order: the meter is per guest (a guest who
leaves with 80 never drops it), a surplus above 100 is lost (a guest at 60 buying a burger gets 100, not 110), a mixed basket drops
at 90 or more (a burger and an ice cream and another ice cream: 50 + 25 + 25 = 100), and the drop needs the right guest state and, probably, no bin in reach.

C# is **not changed**, deliberately. `ParkEconomy` keeps a park-wide store (`LitterScaled`, 1/100 item) and `TryBuy` has no guest, so the
meter, the clamp, the 90 threshold and the bin search have no home there; the guests slice has no litter meter and its needs (`Hunger`,
`Thirst`, `Toilet`) are set to 0 by `ApplyVisit` when the shop satisfies them, rather than moved by the shop's effects (so thirst, hunger and vomit
effects are also still approximations). Modelling it means a guest litter field with a snapshot and save change and a drop
event into the economy, which belongs with the guest-needs work, not with the economy constant. For a guest who keeps buying from one shop the long-run rate is within about 11 % of
the old rule for every shipped value, so ECON-020 stays an `[APPROX]` with the sharper text: the average litter per sale is right, the
per-guest meter, its clamp and the 90 threshold are not modelled. The `shop-sale-litter` plan and C# sides keep their 213 mismatches
(first counterexample: meter 60, effect 50: original 100, old rule 110) until the guest meter lands, and every one is a sum outside 0..100
(`test_shop_sale_litter_rule_differs_only_where_the_meter_clamps`).
**F19, the mechanic's job timer (ECON-021, `ParkEconomy.RepairTimer`, `0xd9c0c`; fixed in C#).** The arrival routine of the mechanic state
machine (dispatch `0xd991c` on mechanic `+412`; the generic update `0xfa9b0` runs it for every live thing each turn, PPC-review §6) was executed
with its callees stubbed (`ride-repair-duration`, `ride-repair-arrival`, 529 and 515 inputs). The mechanic's `+532` names the ride; the ride's
mechanic id (`0xdfb94`) must be the mechanic's own, else it logs and releases. With `0xe6454` (arrival, not executed) returning 0 it starts when the ride
is broken (`+408 == 1`, `0xdfe0c`), being upgraded (`+408 == 2`, `0xdfe90`) or has the maintenance word `+96` set (`0xdfe88`), writes the sub-state 18
(`0xd2290`), the timer into the mechanic's `+528` and state 13 into `+412`:

- *Upgrade (ride state 2):* `WorkDuration[grade] × word at +508 + 64 × level` of the ride's type record (balance `+1004 + 8 × grade` is
  `MechanicConstsPerGrade[grade].WorkDuration`). The ride's `+76` is still the old level (`0xdef2c` adds one on completion), and the type record's `+444`
  is `Upgrades[0].DurationOfUpgrade` (PPC-economy), so `+508 + 64 × level` is `Upgrades[level + 1].DurationOfUpgrade`: the duration of the level being installed.
  A 32-bit product, stored as an unsigned word, with no minimum (a duration of 0 gives timer 0).
- *Any other job (broken, or the maintenance word):* `WorkDuration × (100 − (fctiwz(+64) & 0xFF)) / 100`: the state of repair truncated and taken as a
  byte (the same byte as F15), a 32-bit `mullw` and the signed multiply-high division by 100 (truncating toward zero). A ride at 100 % needs no time;
  above 100 the factor goes negative and the word wraps (not reachable: wear clamps to 0..100).
- *The ride's own upgrade level does not matter for a repair.* The static reading "plus an upgrade factor at `+0x1FC`" belongs to the upgrade job only.
- *Counting.* The repair-state handler `0xd9e04` calls the working update (`0xf459c`), then decrements a nonzero timer once per call. At zero it
  calls the full repair `0xdef2c` (`+64` to 100, level + 1 in state 2), clears the mechanic's target and releases it; in ride state 1 it first
  reads the script's `VAR_BROKEN` (`0xdfea4`/`0xdfed4`) and logs when that is not above zero. Not executed here and not seen in this routine: a wait for
  a fix animation (PPC-economy says so). One call per park turn (the dispatcher cadence, static) makes the
  unit **park turns**: timer + 1 updates until completion. The old claim was game hours.

First counterexample (the plan, C# before): a grade-0 mechanic (`WorkDuration` 80, the Jungle data) on a ride at 40 % repair. The original's timer is 48;
the old claim said 80 hours. At 0 % the two agree in number (80; hours and turns differ by 4 %: a turn is 62.5 park minutes), at 100 % the original needs
no time at all, and the ride's level changes nothing (a fully upgraded ride at 40 % is also 48). The shipped upgrade durations 0 / 2 / 4 give a timer of
`WorkDuration × 2` for level 0 to 1 and `WorkDuration × 4` for level 1 to 2 (160 and 320 at grade 0; the old rule had the same factors, and took
`max(1, ·)`). Per grade (80, 60, 40, 30, 20) a repair at 40 % takes 48, 36, 24, 18, 12 turns. Not changed in C#: the walk to the ride (state 2 of
`0xe6454`) (ECON-021 stays an approximation) and the choice of job. A
saved job is written as `BusyTurns`; an older save with only `BusyTicks` (tenths of a park hour, 360 park seconds each) is converted at 3750 park seconds per turn, rounded up (800 becomes 77), by `ParkSaveFile.BusyTurnsOf` (save version unchanged).

The reading is sharp (`test_repair_duration_reading_is_sharp`): the flat claim differs on 352 inputs, dropping the byte on 17, the division by 100 on 353, the
upgrade branch on state 1 on 246.

**F20, the speed boost (WALK-001, `GuestSpeed.RaiseBoost`, `0xeaaf8`; fixed in C#).** `0xeb0f4` is not a routine but the `sth r0,196(r28)`
that ends a four-instruction block at `0xeb0cc` inside `0xeaaf8(guest, ride)`, the outcome of one completed use of an object (shop, sideshow or ride).
Scanning the whole code section for `bl 0xeaaf8` finds one call site, `0xe9ff8` in `0xe9fa4`, which `0xeec78` in the ride's release step `0xeeb30` calls
when the guest leaves the object (state 15). `0xe9fa4` calls `0xeaaf8` when the guest's byte `+497` is nonzero. `+497` is the result of the win draw `0xe2940` (`rand % 100 <= chance byte`, ride `+396`, 100 minus
`InitChanceOfLoosing`) made when the guest enters state 14 (`0xef8e0`, `0xef920`: called aboard); it is always 1 for rides and shops (chance 100, the routine asserts it
for every type but sideshows). A lost sideshow goes to `0xeb49c` (happiness down) instead. After the type's need effects (`+324` thirst and `+328` hunger subtracted, `+332` vomit and `+336` happiness added, all
clamped to 0..100), `0xeaf44` switches on type `+344` (`UsageInfo.SpecialIngredient`: 0 none, 1 Fat, 2 Salt, 3 Ice, 4 Sugar) with the amount byte of the ride
(`+404`, `mAmountOfSpecialIngredient`, getter `0xe2384`):

| `+344` | Ingredient | Effect (single precision, clamped to 0..100 unless said) |
| --- | --- | --- |
| 1 | Fat | toilet `+428` += amount |
| 2 | Salt | thirst `+420` += amount |
| 3 | Ice | thirst `+420` += trunc(type `+324` x amount / 100) |
| 4 | Sugar | **boost `+196` += trunc(6 x amount / 100)**, stored as a u16 (`0xeb0cc..0xeb0f4`; no clamp, wraps at 65,536) |
| other | | nothing |

The next block (`0xeb0f8`, the lead's list of callees `0x105360`, `0x6a730`, `0x6a7a8`, `0xb882c`, `0xe234c`) switches on `+348` (`UsageInfo.AppearanceEffect`:
1 balloon, 2 costume): the balloon and costume use (ECON-044), which draws random numbers and creates objects; it does not touch the boost.
The only other writers of `+196` are the constructor (0, `0xe4878`) and the decay of `0xe6b28`. `walk-boost` executes the whole `0xeaaf8` on 521 inputs
(ingredient 0..5, -1 and 9, amount word 0..255 and above, boost 0..65,535, thirst effect, ride and shop type words): the reading above **agrees** 521/521
(boost, thirst and toilet). It is sharp: a factor 5 or 7, an unmasked amount, a clamped sum, Sugar read as Ice or Fat as Salt each differ
(`test_walk_boost_reading_is_sharp`).

*Which event, how much.* Only a guest leaving an object whose type has Sugar, which is the Ice Cream Shop of every theme (executed data check:
`ShopType` 2, ingredient 4; the burger shop has Fat, the fries shop Salt, the drinks shop Ice, nothing else has an ingredient; `GuestAssetTests.OnlyTheIceCreamShopsCarrySugar`).
The ride build `0xdab58` stores 50 in `+404` (and 50 in the quality byte `+392`), so an ice cream adds trunc(6 x 50 / 100) = **3** to the boost. The only code
that changes `+404` is the setter `0xe237c`, called from two UI handlers (`0x1685b4`, `0x168640`; the shop screen: they also store x 50 in `+392`), whose range is
not traced; as a byte the amount is at most 255, so one use adds at most 15 (6 at an amount of 100). The boost decays by trunc(x 99 / 100) per park turn
(3 -> 2 -> 1 -> 0), and the smoothed speed `s <- (3 s + sum / 100) / 4` moves by about 0.01 (one percent of a speed of 1.0) for those turns. The "sugar rush" is
real but almost invisible.

*The F6 question (can the boost exceed 661?).* No, not in play. One use adds at most 15, so the u16 wrap of `boost x 99` (662 or more) needs 45 or more uses in
quick succession: with the decay of at least one point per turn a boost stays under 15 unless a guest leaves a Sugar shop every turn, and the walk to the next shop
takes tens of turns. (Used every single turn the boost would settle near 1,485, which is why the wrap is not formally excluded, but no guest can do that.)
No other writer of `+196` exists in the code (a scan of every `sth` to that offset).

*Not traced.* The shop screen that sets `+404` (and the range it allows), and the amount saved as `mAmountOfSpecialIngredient` (no OpenTPW field). Fat, Salt and Ice
change toilet and thirst in the original but not in OpenTPW (the visit still resets the satisfied needs to 0, GUESTS.md); they are a separate gap (the need effects of
`0xeaaf8`, `+324..+340`), not part of WALK-001.

**Fixed in C#** (before -> after: the boost stayed 0 in every game; now `ApplyVisit` adds `GuestSpeed.RaiseBoost( boost, ingredient, amount )` when a guest leaves an
object; `RideVisitorBridge.SpecialIngredient` comes from `UsageInfo.SpecialIngredient` through `OriginalObjectRuntime.CreateVisitorBridge`, the amount is the build default
50 (`[APPROX:WALK-001]` keeps the missing shop screen and save field). `walk-boost` C# 521/521; the factor 6 -> 5 mutation is detected. The determinism hash does not change: its
scenes have no Sugar shop.

**F21, the sale refund (ECON-025, `ParkEconomy.ScrapValue`; fixed in C#).** The sale `0xdbfa8` calls the getter `0xe25e8` at `0xdc05c`
and credits the result with `0xcbf50` (`0xdc070`); the same getter fills the "Scrap value" lines of the object panels
(`0x14c624`, `0x14f968`, `0x14fc64`, `0x165764`, `0x167f74`, `0x16b884`, `0x16c758`, `0x16e9f8`, `0x16ec60`) and `0xe2424` also prices
the queue-cell refund (callers `0x85dcc`, `0x85ed4`, `0x8bf20`, `0x74940`). They were found by scanning the whole code section for
`bl` to the two entries, after PPC-economy.md had read the sale statically; `object-scrap-value` now executes the getter. It agrees with
the routine reading on 572 of 572 inputs, and each part of the reading is needed (`test_scrap_reading_is_sharp`: eight variants). What
the original does:

1. **Basis.** `Upgrades[0].CostOfUpgrade` only (type record `+440`, read without the level offset: the other levels' costs sit at
   `+440 + 64 · level` and are never read). The money paid for upgrades does not enter. The refund is `cost × percentage`, a 32-bit
   `mullw`, divided by 100 as an unsigned word (`mulhwu` with `0x51EB851F`, then `srwi 5`): the product wraps at 32 bits, so a
   cost of 50,000,000 at 100 % refunds 7,050,327 (inputs with such costs are in the target; C# wraps the same way).
2. **Age.** The clock's stamp minus the thing's `+20` (the stamp `0xdad90` stores at the purchase), a signed 64-bit difference in 100 ns
   units. It is divided with the signed divide `0x1c4100` by exactly 365 days (`(73425 << 32) + 0x78C6C000` = 315,360,000,000,000), and
   only the low word of the quotient is compared: 0 is the first year, 1 and 2 the second and third, anything else (3 and up, and a
   negative quotient) the fourth. Years are therefore 365 days of park clock, 8409.6 turns of 3750 s; the boundaries are exact to the
   turn (turn 8409 is year 1, turn 8410 year 2).
3. **Row.** The percentage is `Upgrades[level].ScrapValueYearN`, the thing's current level byte (`+76`) selecting the row at record
   `+392 + 64 · level + 4 · column`. The base level's row is not used for an upgraded ride.
4. **First 30 days.** In the first year, a thing whose word `+412` is 0 and whose age divided by exactly 30 days
   (`(6034 << 32) + 0xF8648000` = 25,920,000,000,000) has the low word 0 is refunded **100 %**, provided the type record's `+392`
   (level 0's year-1 percentage, read without the level offset) is above 0. After 30 days, or after the first use, the ordinary row
   applies. The word `+412` is a lifetime use counter: the constructor clears it (`0xda960`), and the use-finished handler raises it
   together with the history record at `+420` when a guest **leaves** (the release path `0xeaa00` calls `0xe1b44` for rides, shops and
   sideshows; the boarding does not count, so while the first guest is aboard the object still counts as unused); no ride-side store resets it
   (its staff-state stores in `0xd2cbc`, `0xd9b20`, `0xf03d8` and others use the same offset for another object class).

Before → after in C# (the old rule: catalogue costs of all levels up to the current one × the current level's percentage, no 100 %): a
Belly Bounce (costs 500 / 400 / 500, the jungle `Rides.sam` rows 50 / 30 / 20 / 10, 60 / 40 / 30 / 15, 65 / 45 / 35 / 20) at level 2 that
guests used, sold when two years old, refunds 500 × 35 % = **175**; the old rule gave 1400 × 35 % = 490. A ride or shop sold within 30 days
of its purchase before any guest used it refunds its whole price (500 for the Belly Bounce; the old rule gave 250); once used, 250. On the
shipped data that is every buyable object but two: the Halloween Firework Show and the Space Laser Show have a level 0 year-1 percentage of 0 and
refund nothing from the first day (`ShippedScrapRowsGiveTheOriginalsRefunds`: 242 objects over the four themes, 76 with upgrade levels).

**Fixed in C#.** `ParkEconomy.ScrapValue` is `Upgrades[0].CostOfUpgrade × ScrapPercent / 100`; `ScrapPercent` is the whole of `0xe2424`
(the current level's row, exact 365-day years, year 4 from the third year on, the 100 % case). `ParkObjectState.TimesUsed` is the new
lifetime counter (raised by `RecordUseFinished`, which `GuestSimulation` calls when a guest is released, not on boarding; saved as an
optional field, an older save restoring it as this and last month's customers, a lower bound). An object imported from an original save
takes the saved customer count as its use count and the saved attraction time stamp as its build time (`OriginalEconomyImport`), so an
imported used or old object never gets the unused refund; that the save's customer count is `+412` and its time stamp `+20` is read from
the serializer order and the `0xdad90` store, not captured (SAVE-006 says so). `RefundQueueCell` uses the same percentage, so queue cells of a new, unused ride are refunded in
full, as the original does. Two things stay as they were and are approximations: **the park value** (ECON-026) keeps summing the old
rule (catalogue costs through the level × the percentage), because the original's valuation is a different quote (PPC-economy.md); and
**the Windows build**: this is the Mac routine, and the tp.exe counterpart was not executed (ECON-025's text now says so), as is the
mapping of `+412` to a use counter (a read of the stores and of the one increment, not a captured sale). The stubs are the type
record and the stamp conversion (the same assumption as `park-points`); the age of a thing bought after the current turn (a negative
quotient picks the fourth year in the original, the first in C#) cannot occur in a park and is outside the inputs.

**F22, staff vitals, rest and strikes (ECON-014, `StaffVitals`, `StaffStrikeBoard`).** The register said "staff start at happiness 100 and it
never changes (no strikes)". The four oracle targets above execute the routines that refute it. First a correction of what the routines are:
`0xf29fc` is the staff constructor (both vitals start at the single 100.0; it also adds the member to its role's chain), not a strike test.
The strike test is `0xf8410`; the staff happiness step is spread over `0xf44c0`, `0xf459c`, `0xf3d8c` and `0xf4900`; the strike levels live in
`0xf7e18` / `0xf7fe0`. Each reading agrees with the routine on every input (`test_staff_*_reading_is_sharp` holds the variants that do not),
and the old claim differs on 427 of 526, 81 of 509, 313 of 515 and 442 of 513 inputs.

What a staff member does (all singles, clamped to 0..100; energy is `+504`, happiness `+500`):

| Step | Routine | Rule |
| --- | --- | --- |
| Walking | `0xf44c0` | energy -= `f32(0.012 x (6 - grade))`, happiness -= `f32(0.005 x (6 - grade))` (binary64 products rounded to single) |
| Working | `0xf459c` | energy -= `0.025f x (6 - grade)` (single product), happiness -= `f32(0.01 x (6 - grade))` |
| Resting | `0xf3d8c` | energy += `PerGradeStaffConsts[g].RecuperationRate` (0.2, 0.3, 0.4, 0.5, 0.75), happiness += `HappinessRecuperationRate` (1, 2, 2, 3, 3); the rest ends when the truncated energy byte is 100 (the rest area is freed and the role's next action runs) |
| Tired | `0xf4900` tail | energy byte (the truncated low byte) at most `RestLevel` (1, signed compare): seek a rest area (thought 20) and, with one found, rest. With none, a world draw with `(draw & 15) == 0` takes `HappyHitCosNoRestArea` (2) off the happiness, one turn in sixteen. Above the level: no change (only walking-speed tiers and the thoughts 18 and 19) |
| Rest area | `0xf47a0` | any object whose `UsageInfo.ChillsYouOut` is set (type `+252`, thing `+46` bit `0x2`): in the shipped data the Staff Room of each theme only (jungle 1411, fantasy 4401, hallow 2408, space 3405). The nearest one by squared distance |

Training (`0xf3588`, static reading): a promotion (the training percentage reaching 100) sets the happiness to 100.0 (`0xf36e4`); energy is untouched.

The vitals only fall while a member walks (raw state 1 with a navigation in progress: `0xd2e20`, `0xf0530`) or works (raw state 14, researchers 15).
An idle member (raw state 0) does not drain. A grade 0 member walking from 100 reaches the rest level after 100 / 0.072 = 1389 turns (about 60 game
days of 23.04 turns), a grade 4 member after 4167; working halves that. A rest from nothing takes 500 turns at grade 0, 133 at grade 4.

The strike test `0xf8410(manager, role)` returns 1 at once when the manager's forced word (`+100`) is nonzero (`li r3,1`, any value). Otherwise,
handymen first test the ratio of two analyser sums `0xc2264(park, 1) / 0xc21f4(park, 1)` (single precision) against `0.2f` (a quotient above it,
infinite included, NaN not). Then the role's staff chain (first index at `game + 0x1da73c..` per role: 0 `-22724`, 1 `-22722`, 2 `-22720`, 3
`-22716`, 4 `-22718`; next at staff `+524`) is walked twice, energies (`+504`, debug text "fatigue") then happinesses (`+500`, "unhappiness"):
the truncated low bytes are summed unsigned, and **more than three members whose floor average is below 15** (14 or less) have a grievance. A role
outside 0..4 asserts and gives 0. Values over 255 or below 0 wrap through the byte (256.0 counts as 0, -1.0 as 255).

The month routine `0xf7e18` (the staff manager's end-of-month message) trains first, then, unless the forced word is set, is skipped (debug "park
is closed") while the park is closed (`game + 0x1da710` nonzero) **or** `0xc1fb4(park, 5)` counts a guest standing on a park cell (the guests of
`PeopleInPark`: the cell test `0xe6c6c`). Then each role with staff that was not already considered in this turn (`+44`) is marked, and:
an active strike (`+40`) is only ended; otherwise the escalation `0xf7fe0` runs: the clock stamp (`0xe4628`, 100 ns units) divided by
25.92e12 (30 days, signed 64-bit divide `0x1c4100`) must be at least 24 in its signed low word, then the strike test decides.

| Level (`+36`) | Grievance | No grievance |
| --- | --- | --- |
| 0 | level 1, warning (kind 0) | nothing |
| 1 | level 2, strike flag set, kind 2 | "called off" (kind 3), level 0 |
| 2, 3 | level + 1, strike flag set, kind 2 | level 0, flag cleared |
| 4 | stays 4, strike flag set, kind 2 | level 0, flag cleared |

The five kinds are the five groups of `TAG_SYSTEM.str` 38..62 (warning, reminder, strike, called off, over; five roles each: mechanics, cleaners, guards,
entertainers, scientists); the routine sends kinds 0, 2 and 3 only. A strike therefore lasts to the next month end, where it is ended without a
new one: a role with a standing grievance is warned at one month end, strikes at the next and then at every second month end. After a month
of contented staff the level returns to 0.

What a strike does to a member (static reading, not executed): the strike flag makes a free member that reaches `0xf4900` (idle, or after a job)
set `+392`, pick a random cell of the strike area (`FixedItemInfo.StrikeAreaStartX/Y`, `SizeX/Y`: 40, 9, 6 x 1), walk there and enter raw state 4 (broad
state "On strike") when navigation succeeds, and only while the world state `0x1091b8` is 1. A member in raw state 4 (`0xf3f64`, per turn) ends its
role's strike when the park is closed with no guest in it (`0xc3684`), and walks back to work once the role's flag is clear. A working member finishes its job
first. UITEXT 370, 376 and 382 ("UPGRADE: Mechanics are on strike!", "CLEANING: Cleaners ...", "REPAIR: Mechanics ...") and TAG_SYSTEM 152 are the object
status texts selected by `0x138b0c..0x138c40` through `0xf8bc0`.

Fixed in C# (before -> after: no energy and a happiness of 100 forever, no rest, no strike, to the table above). A hired member walks
(Patrolling) or works (a mechanic on a job) and loses energy and happiness every park turn; at the rest level it goes to rest when a Staff Room is
built and loses happiness one turn in sixteen when none is (`StaffVitals.SeekRest`); a resting member is not dispatched. At every month end
`StaffStrikeBoard.ConsiderMonth` runs with the park's state, raises a `StaffStrikeMessage` event (role, kind, the `TAG_SYSTEM` index) and a striking
role's free members go on strike (`OnStrike`: no dispatch, no research) until the next month end. Saves carry `Energy`, a float `Happiness` and the
strike board; older saves load as rested staff without strikes; imported staff keep their saved energy and happiness.
The decision of `0xf4900` runs for a free member each turn and once when a job ends, before the next job is dispatched, which skips members on strike, resting or tired with a rest area (static reading: the original's working member finishes its job and then reaches `0xf4900`). Selling the last Staff Room ends a rest at once (the original's rest-area association `+516` is freed by `0xf4d00` when the rest ends; what it does when the rest thing vanishes is not traced). The HUD message area shows the `TAG_SYSTEM` text of each message.

Example a player notices: 4 grade 0 mechanics in a park with no Staff Room: after about 1400 turns of walking they are at energy 0, lose 2
happiness about one turn in sixteen, and once the park is 24 thirty-day months old and a month end finds it open and empty of guests, the mechanics are
warned (TAG_SYSTEM 38), strike at the next month end (48) and no ride is repaired until the following one. With a Staff Room they rest 500 turns at
a time instead and never reach a grievance (OpenTPW before: happiness 100 forever, no strike).

Not changed or not established, kept in the ECON-014 register entry:

- The staff state machine: OpenTPW's Patrolling stands for every walking state (the moving drain applies each turn), Working for a job in progress and Resting
  for a member at the room, without the walk to it; idle members are not told apart, the rest search runs every turn (the original at idle and arrival), and the
  thoughts and the walking-speed tiers of `0xf4900` have no counterpart.
- The handymen's analyser statistics (`0xc21f4`, `0xc2264`: a sum over 1024 history records each; "Clean your park up"): not supplied, so the litter
  grievance never applies. The forced-strike word (a debug flag) is never set. The game-type gate of the caller is not traced.
- The clock stamp `0xe4628` goes through imports (`0x1c5c34`, `0x1c5c4c`, `0x1c5c64`); whether it counts from an absolute epoch before 2000 (then the test
  "at least 24 units" is always true) or from the park's start is not established. OpenTPW takes the latter: a strike needs a park of 24 thirty-day months.
- The second gate (a guest on a park cell skips the month) makes the strike path rare in a busy park: it runs on a month end at which the park is open and
  empty. The Mac Easy Guide's FAQ says strikes were removed; the routines are present and reachable.
- The world state `0x1091b8` that a member needs to walk out is not modelled (members go on strike at once), and the world draw of the rest-area hit
  comes from the economy's own stream (DET-015).

## 6. Adding a target or a C# hook

1. Read the routine with `oracle/disasm.py` (terminal only). Note every call it makes, and the
   code range.
2. In `targets.py`, add `binary` (memory set-up through `Sandbox`), `plan`, `inputs`, `allowed`
   and `stubs`, each stub with its justification. Keep stubs to calls whose effect is outside the
   compared result.
3. For C#, add a case to `dotnet/Program.cs` that calls the implementation, not a copy of it.
   Then set `csharp=` on the target. The C# side of DET-I2, WALK-I and COAST-I starts comparing
   as soon as the bridge lists `world_rng_next`, `rse_rand`, `guest_speed_limits`,
   `guest_speed_smoothing`, `coaster_rating_step` or `coaster_rating_final`.
   `test_every_comparison_is_pinned` then demands an `EXPECTED` entry.
4. Pin the result in `test_oracle.EXPECTED` and in section 4/5 here.

## 7. x86 oracle for the Windows executables (`oracle_x86/`)

Added 2026-10-12. The same idea as sections 1 to 6, for the two Windows executables that may
be analysed (docs/COMPLETION-PLAN.md, "Provenance of game rules"): isolated routines run in
an emulator and their results are compared with the plans and with the routine's own reading.
Code: `tools/ppc-analysis/oracle_x86/` (`x86_pe.py`, `x86_harness.py`, `x86_targets.py`,
`x86_differential.py`, `test_x86_oracle.py`, `README.md`). It is a sibling of `oracle/`, not a
subdirectory, because `run_evidence_checks.py` runs one discovery process per directory and
the x86 modules must not share a module namespace with the PowerPC `harness.py`/`targets.py`.

### 7.1 Decision and dependency

Decision of the project owner, 2026-10-12, scope **dev-only oracle tooling**: the x86 oracle
uses the **Unicorn** CPU emulator (checked with 2.1.4, `cp37-abi3-macosx_11_0_arm64` wheel from
PyPI). It is an explicit exception to the "no new pip dependencies" rule (`.github/glm/RULES.md`,
which GLM workers follow and which this change does not edit). The exception is bounded:

- Unicorn is imported in one module (`x86_harness.py`) inside `try/except ImportError`. Nothing
  under `source/` and no shipped tool imports it. It is not in any requirements file, project file,
  workflow or release artifact.
- Every test that needs it skips when `import unicorn` fails (`requires unicorn (dev-only: pip
  install unicorn==2.1.4; ...)`) or when the private executable variable is unset. The PE reader
  and the identity gate are pure Python and run everywhere. CI has neither Unicorn nor the binaries
  and reports skips.
- A developer installs it in a separate venv (`oracle_x86/README.md`).

**Licence check.** The installed wheel carries no licence or COPYING file (its `dist-info` holds
`METADATA`, `RECORD`, `WHEEL`, `top_level.txt`, `INSTALLER` and `REQUESTED`), and the bundled
`libunicorn` holds no licence text. Its `METADATA` says `License: BSD License` (also as a
classifier), while the long description, which is upstream's README, says "Distributed under free
software license GPLv2". The upstream project is GPL-2.0 (it contains QEMU code), so the PyPI
classifier is not reliable and this repository treats Unicorn as GPLv2. (Capstone, used only in
scratch analysis and by no repository code, ships a BSD `LICENSE.TXT` in its wheel.) The repository
licence is MIT (`LICENSE.md`). That does not change OpenTPW's licence: Unicorn is neither vendored,
linked into nor distributed with OpenTPW. A developer fetches it separately and the oracle tests
only `import` it at their own run time. GPL obligations attach to distributing the library or a
work combined with it, and nothing OpenTPW distributes is that. No real problem found. One
condition follows: do not put Unicorn, or an image or archive containing it, into a release, CI
image or other artifact without meeting the GPL for that artifact. That would be a decision of its
own.

### 7.2 Harness

`x86_pe.py` is a minimal PE32 reader: sections, imports and base relocations. `x86_harness.py`:

- **Identity.** `Image` refuses any file whose SHA-256 and size are not those of the two identified
  executables (`IDENTIFIED`): the decrypted TPW v2.0 `tp.exe` (3,734,528 bytes, SHA-256
  `5472f97b...d1b805`) and the TPI no-CD `Game.exe` (6,742,016 bytes, SHA-256
  `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`; the prefix matches
  docs/MTR.md). The SafeDisc disc-root `Game.exe`, `TP.ICD` and every other file fail the check.
  The paths come from `OPENTPW_TPW_EXE` and `OPENTPW_TPI_EXE` (outside the checkout); the runner
  sets or strips them from `--tpw-exe` / `--tpi-exe`, like the Mac variables.
- **Loading.** Sections at the preferred base `0x400000` (relocated when another base is asked
  for; neither executable needs it, but the TPI image lists 144,929 fixups, 16 of them past the end of its
image, which a relocated load skips and counts in `Image.skipped_relocations`). Every import address table slot points at a per-import trap
  address, keyed `dll!name` (DLL case-insensitive).
- **Sandbox.** Image sections (code read/execute, data read/write), a 16 MiB heap (the world object
  puts its RNG state about 2 MB in), a stack, an import trap page and a return sentinel page. Only
  the code ranges the target names (`allowed`, virtual addresses) may execute. A call into any other
  address, or into an import trap without a stub, raises `UnknownCall`. An instruction budget raises
  `BudgetExceeded`; unmapped access raises `MemoryFault`; an invalid instruction `Unsupported`.
- **Calls.** `call(entry, args, this=, regs=, stop=)` pushes stack arguments right to left, puts
  `this` in `ecx` and the sentinel as the return address, so cdecl, stdcall and thiscall all work.
  `Result` reports registers, executed instructions, the final `esp` relative to the argument
  base (0: callee popped, -4n: cdecl) and the raw x87 registers.
- **Pristine runs.** Pages written by the previous run (a write hook records them) are restored,
  the heap is zero, registers are zero, and the x87 (control word 0x37f, empty stack) and SSE state
  are reset. Unicorn's x87 keeps 80-bit precision (`test_x87_keeps_extended_precision_and_is_reset`).
- **Stubs.** A stub is `fn(sandbox) -> (eax, bytes the callee pops)`; it replaces the routine and
  acts as its `ret`. `sandbox.arg(i)` reads its stack arguments.

### 7.3 Targets and pins

| Target | Routine (allowed code) | Stubs | Inputs | vs plan | vs routine reading |
| --- | --- | --- | --- | --- | --- |
| `tpw-exe-world-rng` | tp.exe `0x516330`..`0x516365` (thiscall, `ecx` = object; state at `+0x1da708`) | none | 508 (500 random + 8 boundaries) | **X1**: 507 differ from the Mac model `det_model.world_next` (first input 0: original returns 1694899337 and stores 2600067959, the plan 1013904223 for both) | **agree** 508/508 |
| `tpi-exe-world-rng` | TPI `Game.exe` `0x6493e0`..`0x649415` (thiscall, `ecx` = object; state at `+0x1eab3c`) | none | 508 | **X2**: 507 differ from the Mac model (first input 0: the same pair as tp.exe, 1694899337 returned, 2600067959 stored) | **agree** 508/508 with the TPW PC reading |
| `tpw-exe-arrival-group` | tp.exe `0x4cf5b0`..`0x4cf71d` and the float-to-int helper `0x67a830` (thiscall, `ecx` unused; the Mac `0xcb4e8`) | `0x519510` game to park (sentinel, checked), `0x4c7fa0` counted guests (argument 20 checked), `0x4c8240` park points (chosen), `0x519590` thing lookup (synthetic thing, dword `+0x30` chosen), `0x5da3c0` print (logged); the GameType object is marked constructed | 733 (the Mac target's 515, 12 edges, 100 wide-range, 100 precision, 6 more), x87 control word `0x27F` | **X3**: 54 differ, all x87 arithmetic (8 products beyond 31 bits, 46 totals of 1.7 million and up); inside the reachable range **agree** | **agree** 733/733 (53-bit reading) |
| `tpw-exe-loan-offers` | tp.exe `0x4cf7c0`..`0x4cf8c6` (bank constructor, the 8-offer loop; the Mac `0xcb7a8`) plus the runtime's own `pow` (`0x67c5d0`, `0x686170`) and `0x67a830`, executed | `0x50afe0` base constructor (header only, arguments checked) | 506 offer sets (the Mac target's 505 plus one out-of-range set) | **X4**: 505/506 **agree** with the Mac plan, records and bank words included; the one set holds a zero term and a result above 32 bits | **agree** 506/506 (x87 reading, `math.pow` for the power) |
| `tpw-exe-loan-term`, `tpw-exe-loan-month` | tp.exe `0x4d0385`..`0x4d04d9` (the loan loop of the monthly handler `0x4d0370`; the Mac `0xcc21c`) | `0x519510` game to park (sentinel, checked), `0x5da3c0` print (logged by message) | 503 whole terms, 506 random bank states | **X5**: **agree** 503/503 with the Mac plan | **agree** 503/503 and 506/506, events in the same order |
| `tpw-exe-park-points` | tp.exe `0x4c8240`, park record search `0x4d3d10`/`0x4d3d80`, `0x4f88b0` (turns to 100 ns), the 64-bit divide and multiply helpers (the Mac `0xc24c0`) | `0x412e90` type record (registry checked), `0x5da3c0` assertion (a zero condition fails the run), `0x4dd920` counts-test (chosen flag), `0x4dd670` own age (signed timestamp difference in days) | 507 | **X6**: **agree** 507/507 | (the plan is the Mac routine reading) |
| `tpi-exe-arrival-group` | TPI `Game.exe` `0x5bcd60`..`0x5bcf05` and `_ftol` `0x86fab0` (thiscall, `ecx` unused; the TPW `0x4cf5b0`), the mode-object getter (`0x403a99` to `0x42a340`) executed | on the jump thunks: `0x40144c` game to park (sentinel, checked), `0x40461f` counted guests (argument 20 checked), `0x402bd5` park points (chosen), `0x4019b5` weather thing (synthetic thing, dword `+0x70` chosen), `0x403e45` print (logged); the GameType object is marked constructed | 532 (500 random + 32 boundaries), x87 control word `0x27F` | **X7**: 216 differ from the TPW / Mac rule (67 from an exit that tp.exe lacks, 149 from the factor and the floating divide) | **agree** 532/532 (53-bit reading); exact arithmetic differs on 30 (the single-precision 0.01) |
| `tpi-exe-loan-offers` | TPI `0x5bfb70`..`0x5bfc73` (bank constructor, the 8-offer loop; the TPW `0x4cf7c0`), the runtime's own `pow` (`0x871c00`, `0x87c410`..) and `_ftol`, executed | `0x403d19` base constructor (header only, arguments checked) | 506 offer sets (the TPW target's inputs, seeded by this key) | **X8**: 505/506 **agree** with the Mac plan, records and bank words included; the one set is the same out-of-range set as in tp.exe | **agree** 506/506 |
| `tpi-exe-loan-term`, `tpi-exe-loan-month` | TPI `0x5c0b25`..`0x5c0c49` (the loan loop of the monthly handler `0x5c0b10`; the TPW `0x4d0370`) | `0x40144c` game to park (sentinel, checked), `0x403e45` print (logged by message) | 503 whole terms, 506 random bank states | **X9**: **agree** 503/503 with the Mac plan | **agree** 503/503 and 506/506 (the Mac reading minus its two assertion events, which this build lacks) |
| `tpi-exe-park-points` | TPI `0x5b05d0`, park record searches `0x5cfd70`/`0x5cfcf0` and their thunks, `0x608d50` (turns to 100 ns), the 64-bit divide and multiply helpers (the TPW `0x4c8240`) | `0x40c1d0` type record (registry checked), `0x40998a` counts-test (chosen flag), `0x403bb6` own age (signed timestamp difference in days) | 507 | **X10**: **agree** 507/507 | (the plan is the Mac routine reading) |

The routine reading (own words): multiply the state by 1664525 and add 1013904223 (mod 2^32),
rotate the result right by 13 bits, store that, and return its signed absolute value (the
wrapping `neg` keeps `INT_MIN`). The single agreeing input with the plan is the state whose
successor is 0, a fixed point of the rotation. All pins live in `test_x86_oracle.EXPECTED`; the
sharpness tests cover a mutated reading (rotation 12), an overrun of the allowed range, and a
modified copy of the executable (refused by the hash).

### 7.4 Finding X1: tp.exe 0x516330 is the same logical generator as the Mac world LCG, with a different step

Question: is tp.exe `0x516330` the PC counterpart of the Mac world generator `0x105328`, or a
different generator that happens to use the same constants?

Evidence (static, read with Capstone in a scratch directory, terminal only; plus the executed
agreement above):

- Same state slot: `0x516330` reads and writes the dword at object `+0x1da708`; DET-plan §2.3 puts
  the Mac world RNG (`mRandomSeed`) at world `+0x1da708`, next to `mGameTick` at `+0x1da70c`.
  On the PC the three functions sit together in the same order as on the Mac (generator `0x516330`,
  setter `0x516370`, tick `0x516380`; Mac `0x105328`, `0x105360`, `0x1053a0`), the setter stores its
  stack argument at `+0x1da708` and the tick increments `+0x1da70c`.
- Same constants and quirk: multiplier 1664525, increment 1013904223, the successor stored twice (the
  Mac model records "one advance despite the duplicate native store"), and the same absolute value
  as return.
- Same users: 139 direct callers of `0x516330` (Mac 142), four inline copies of the advance next to
  it (`0x516753`, `0x516811`, `0x51687f`, `0x51a353`; Mac four), and eight direct callers of the
  setter, seven of which pass a zero-extended halfword read from the first field of a thing record
  (the thing-ID reseed of DET-plan §2.3; Mac 7 + 1 object field) and one a halfword it also keeps
  in an object field. The object is reached through one global (`0x80239c`). The inline copies
  project the value with a modulo or a low bit (for example modulo 3 at `0x51a381`).
- The difference: after storing the LCG successor the PC rotates it right by 13 bits and stores the
  rotated word, which is also the base of the returned absolute value. The Mac routine has no
  rotate (executed oracle `det-world-lcg`, 507/507 with the plain model). The four inline copies
  on the PC rotate as well.

Result: **the same logical generator (the shared gameplay RNG), with a platform difference**. The
PC stream is the Mac's with an extra rotation of the stored state, so from the same seed the two
platforms produce different streams (the one coincidence is a state whose successor is 0).
Executed: the function itself, 508 inputs. Static only: the callers, the setter, the tick function
and the inline copies; their roles were read, not run, and the Mac side comes from DET-plan, not
from a re-derivation here.

Consequence. DET-plan (static evidence for the Mac build, "no Windows/Patch 2 behaviour is
claimed") and its `WorldRng` class (§4.3: `s = s·1664525 + 1013904223`, no rotation) follow the
**Mac** generator, and OpenTPW has no world LCG yet (docs/DETERMINISM.md: DET-I2 is a later
step; guests and economy use their own SplitMix64 streams, DET-013 to DET-015). So nothing in the
game diverges today and nothing changed here. When DET-I2 ports the world LCG it must choose a
platform: the Mac step reproduces the Mac saves' `mRandomSeed` stream, the PC step (with the
rotation) the Windows executable, and a save, replay or trace from one platform does not continue
on the other. That choice needs its own decision, and the PC rotation must be in the plan's
`WorldRng` and `det_model` before any "original behaviour" claim is made for the Windows build.
The same check applies to the weather generator and the sound seed, which are not examined on the
PC. Whether the TPI `Game.exe` has the rotation is the next target.

### 7.4a Finding X2: the Theme Park Inc Game.exe uses the TPW PC step, with the state slot moved

Question: does the TPI `Game.exe` (the no-CD image with SHA-256
`77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`) have the Mac world
generator, the TPW PC generator (rotation by 13), or a third one?

Evidence (static reading with Capstone in a scratch directory, terminal only; plus execution):

- Search: the multiplier 1664525 and the increment 1013904223 occur together 47 times in the image.
  Six are the shared generator at `0x6493e0` and its five inline copies; the other 41 belong to
  separate generators with their own state words (around `0x7992c1`, `0x7a4260`, `0x8a3776`) and were
  not examined further (the one sampled, at `0x7992c1`, also rotates by 13).
- Generator `0x6493e0`: reads a dword from object `+0x1eab3c`, multiplies by 1664525, adds
  1013904223, stores the successor, rotates the result right by 13, stores that, and returns its
  signed absolute value (wrapping negate). This is the same step in effect as tp.exe `0x516330`
  (X1); only the slot moved (`+0x1da708` in tp.exe, `+0x1eab3c` here).
- Neighbours in the same order as X1 and the Mac: setter `0x649430` (stores its stack argument at
  `+0x1eab3c`) and tick `0x649450` (increments the dword at `+0x1eab40`, the game tick, then does
  per-tick work). All three are reached through jump thunks (`0x40a92f`, `0x403508`, `0x4055bf`; the
  image is incrementally linked), so direct `call` counts are on the thunks.
- Users: 149 calls of the generator (tp.exe 139, Mac 142), five inline copies (`0x649a87`, `0x649c5b`,
  `0x649cf9`, `0x649d67`, `0x64fcbb`; each rotates, one projects with modulo 3), seven calls of the
  setter, each passing a zero-extended halfword read from the first field of a thing record (the
  thing-ID reseed of DET-plan §2.3), through one global object pointer (`0xade6b0`). Three other
  references to the slot (`0x64a781` and `0x64c10b` take its address; one near `0x648168`) were not
  examined.
- Executed: the routine run in the sandbox on 508 inputs (seed `oracle-v1`, 500 random plus
  boundaries) agrees 508/508 with the TPW PC reading and differs from the Mac model in 507 (the one
  agreement is again the state whose successor is 0). The identification as the shared gameplay RNG
  rests on the static evidence above; the function itself is executed.

Result: **TPI uses the TPW PC step (LCG, then rotate right by 13, rotated word stored and projected),
not the Mac one and no third variant.** Both Windows executables carry the rotated generator and the
Mac build is the odd one out. Only the object layout differs between the two Windows executables
(state `+0x1da708` vs `+0x1eab3c`, tick `+0x1da70c` vs `+0x1eab40`).

Consequence for OpenTPW. DET-plan and `det_model.world_next` (no rotation) follow the Mac build and
OpenTPW has no world LCG yet (DET-I2), so the game is unchanged. If DET-I2 targets the Windows
games, the rotated step is the one to port: it serves TPW PC and, unchanged, a future TPI mode (only
an original-save import needs the other slot offset). The Mac step is correct only for Mac saves and
traces, and a save or replay from one family does not continue on the other. The platform choice
stays a decision of its own; X1 and X2 give it its inputs. The weather generator and the sound seed
are still unexamined on both Windows builds.

### 7.5 Running and adding a target

```
python3 -m venv /tmp/oracle-venv && /tmp/oracle-venv/bin/python -m pip install unicorn==2.1.4
OPENTPW_TPW_EXE=/path/tp.exe /tmp/oracle-venv/bin/python -I tools/ppc-analysis/oracle_x86/x86_differential.py
/tmp/oracle-venv/bin/python -I -B tools/ppc-analysis/run_evidence_checks.py --tpw-exe /path/tp.exe
```

To add a target: read the routine with a disassembler in a scratch directory (never in the
checkout), note every call and the code range, then add `binary`, `plan`, `inputs`, `allowed` and
`stubs` (each stub justified) to `x86_targets.py`, and pin the result in
`test_x86_oracle.EXPECTED` and in 7.3 here. Recommended next targets: the tp.exe counterparts of
the other pinned Mac targets (`loan-payoff` and `loan-take`, the counts-test, `arrival-update`, the
queue and ride predicates), and a dynamic check of the x87 control word at the time the bank is built
(X4).

### 7.6 Findings X3 to X6: the Mac-derived rules in tp.exe

Question: does the Windows TPW v2.0 `tp.exe` implement the rules OpenTPW follows from the Mac
PowerPC oracle (F9, F10, F12, F13)? Method for each: find the counterpart by constants, object offsets,
call structure and message texts (static, Capstone in a scratch directory, terminal only), then
execute it with the stubs of the Mac target and compare with the Mac reading that C# follows
(`oracle/targets.py` is imported, not copied: its plans, readings and input generators). All four
counterparts exist and have the Mac's structure. The tp.exe addresses below are virtual addresses at the
preferred base `0x400000`; no bytes or disassembly are stored here.

The x87 control word matters (X3, X4). The sandbox runs these targets with `0x27F`, a 53-bit
significand with round to nearest: that is the word the runtime's own `pow` tests for (it switches to
a safe word only when the current one differs), the Windows process default, and the imports show
DirectDraw only and no Direct3D, which is what usually lowers it. Whether the word really is `0x27F` when
the bank is built was not established dynamically (the executable was not run as a program). The
emulator's own default (`0x37F`, 64 bits) and `0x7F` (24 bits) are run in the tests to show what would change.

**X3, arrival group size (Mac `0xcb4e8`, F9): tp.exe `0x4cf5b0`; same rules, x87 arithmetic.**
Identification (high confidence): the same early exit for world mode 4 (game `+0x1da738`), the
same three settings words at the same offsets of the settings block (MinPeople, NewParkBonus,
PointsPerVisitor: `0x785310`, `0x78531c`, `0x785320`, which are the Mac's `+720`, `+732`, `+736`),
the counted guests (argument 20) and the park points called in the same order, a thing looked up
from a game-object handle whose word at `+0x30` (the Mac's `+48`) picks the factor (positive:
the single-precision constant 0.8, at `0x700368`; otherwise 1.2, at `0x700364`), the
GameType word at `0xfb3b7c` equal to 1 for the 500 cut and the 1500 cut otherwise (both unsigned
compares and no floor), and the two "capping the number of people" log lines (online, offline) plus a
"number of people" line. The handle is at game `+0x1da724` (`mWeather`), the same offset as the Mac's weather handle (an earlier reading compared it with
the Mac's `+0x1da734`, which was the Traffic Lights: see F9), and the
gates handle sits at `+0x1da722` against the Mac's `+0x1da732` (the world setup stores them in order
just before the bank constructor, as on the Mac).

Executed result: all of F9 holds. The factor test, the 500/1500 cuts, the unsigned compares and
the missing floor agree with the Mac reading `arrival_full`, so C# follows the Windows build too
(except the floor at 0, which is the same departure as for the Mac). 54 of 733 inputs differ from the
Mac model, all from the arithmetic, none from the rules:

- 8 inputs have a product beyond 31 bits (for example points 2147483392 with the 1.2 factor and a
  divisor of 1, a total no park can have): the PC keeps only the low 32 bits of the 64-bit integer
  `fistp` stores and divides that signed word (here negative, so the group becomes MinPeople), where
  the Mac's `fctiwz` saturates and the 1500 cut follows.
- 46 inputs have a total of 1,747,629 or more with factor 1.2 (5,242,881 or more with 0.8): the PC
  multiplies the exact integer by the single-precision factor in the wider x87 register and truncates, the
  Mac first rounds the sum to single and then rounds the product to single. Example, total 1,747,629,
  factor 1.2: the PC product 2,097,154.88 truncates to 2,097,154, the Mac's rounds to 2,097,155. This is a
  difference that comes from precision alone. It shows only when the group is under the 1500 cut (a
  large divisor, MinPeople 0 and so on), so no real park, whose points are tens to thousands, reaches it.
  The 53-bit and 64-bit words give the same groups on every input here; 24 bits (single) rounds the
  product like the Mac but not the sum, and differs from 53 bits on 43 inputs.

Consequence: current C# (`GuestSimulation.ArrivalGroupSize`) is right for the Windows build within
any reachable range. Nothing to change.

**X4, loan offer instalment (Mac `0xcb7a8`, F12): tp.exe `0x4cf7c0`; same formula.**
Identification (high confidence): the bank constructor, found through the settings words (initial
cash at `0x7851d8` = settings `+408`, offers from `0x7851e0` = `+416`, 16 bytes each), eight records
of 32 bytes from the object's `+0x14` with the Mac's layout (available, P, APR, N, instalment,
bought, months repaid, lender; available when P is at most 10000), the same bank words set after
the loop, the base constructor called first, and a bank built in the world setup right after the
lights handle. The monthly instalment is trunc(P × pow(1 + APR/100, N/24) / N); the shipped offers
give 3651, 1825, 912, 365, 922, 1282, 2320, 2749 at every control word.

`pow` is not an import here: it is the executable's own x87 routine (`0x67c5d0`, with `0x686170`): it
computes y·log2(x) with `fyl2x`, then 2^x with `f2xm1` and `fscale`. It is executed on the
emulator's x87 (no stub), so Unicorn's `fyl2x`/`f2xm1` stand for the hardware's and are not
guaranteed to match it in the last bit of the extended result. The reading with Python's `math.pow`
gives the same instalment on all 506 sets, so such a difference changes M only where P × pow / N
lies that close to an integer.

Differences from the Mac in how the expression is built, none visible at 53 bits: the PC computes
APR × 0.01 + 1 (multiply by the constant 0.01, subtract -1.0) where the Mac divides by 100, and
N × (1/12 as a double) × 0.5 where the Mac divides by 12; the power's two arguments are stored as
doubles for the call; the amount is read unsigned; the quotient is truncated by `fistp` and the low
word kept, where the Mac converts to unsigned with saturation. Executed: 505 of 506 offer sets
agree with the Mac plan (`LoanMath.MonthlyRepayment`), records and bank words included. The remaining
set is out of range: a zero term gives 0 on the PC (the Mac routine saturates to 2^32 − 1, the plan
throws) and 0xFFFFFFFF at 40 % over one month gives the low word 60,638,121 (the Mac saturates). No
shipped offer has either.

Precision finding: the instalment depends on the control word where P × pow / N is an exact
integer. 18000 at 20 % over 24 months is exactly 900 (pow(1.2, 1)): 900 at `0x27F` and on the Mac's
doubles, 899 at the 64-bit `0x37F` (13 of 506 offer sets change by 1 between the two words, 2 sets at 24
bits). The shipped offers do not. C# keeps the Mac's binary64 result, which equals the Windows build only if
the control word is `0x27F` when the bank is built (see above).

**X5, monthly loan step (Mac `0xcc21c`, F13): tp.exe `0x4d0370`; identical.**
Identification (high confidence): the bank's message dispatcher (`0x4d02d0`) calls it for message code 12,
as on the Mac; the handler flushes the batch (assertion below one million, then balance, the park's money-in
word `+0x1fc90` and the year profit `+0x124`) and runs the eight-record loop at `0x4d03de`. The bank fields
are at the Mac's offsets (balance `+0xc`, batch `+0x10`, withdrawals `+0x114`, last balance `+0x11c`,
turn entered red `+0x120`, year profit `+0x124`, records from `+0x14`), as are the park words, the turn word
at game `+0x1da70c` and the four message kinds (instalment line, paid-off line, the two assertions), in the
same order. The routine's entry pushes an exception frame through the thread information block, which the
sandbox does not have, so the run starts just after it (`0x4d0385`) and ends at the loop's end (`0x4d04d9`);
the tail, a bank in the red for a month, is not run.

Executed: the stored instalment M is deducted exactly N times and nothing else, the year profit gains
M − floor((M × N − P) / N) in 32-bit unsigned arithmetic (an unsigned divide, skipped for a
zero term), and the loan clears when the months repaid reach N. 503 of 503 whole terms agree
with the Mac plan, and 503/503 and 506/506 (whole terms and random bank states) with the Mac
routine reading including its events; dropping any one of the six parts brings mismatches
back (`test_loan_step_reading_is_sharp`). Example: 100,000 over 36 months at 20 %: 36 deductions of 3651
and the year profit falling by 873 a month. C# (`LoanMath.Pay`) is right for the Windows build.

**X6, park points (Mac `0xc24c0`, F10): tp.exe `0x4c8240`; identical.**
Identification (high confidence): it is the routine the arrival group calls for the points, in the same
place as the Mac's. It walks the thing list from game `+0x1da746` (the Mac's offset) through the
20-byte thing array at `0x7cfb90`; reads the type record by id from the registry at `0x7890a0` (value
at `+0x58`, decay at `+0x5c`, bonus brackets at `+0x268`, the track-type word at `+0x9c`: all the Mac's
offsets; only the kind word moved, to `+0x4ac`); counts only kinds 0 and 2 and things that pass the
counts-test (`0x4dd920`, called where the Mac calls `0xdc834`); reads the park record (game
`+0x1d943c`, 32-byte records, copies at `+0x18`, first purchase at `+0x1c`: the Mac's `+24`
and `+28`); and ages a type with track-type word 0 from the first purchase through `0x4f88b0` (the turn
difference times the clock rate at game `+0x2bc`, divided by 4, times 10,000,000, divided by the
day length 864,000,000,000, a constant that appears in both ageing paths), any other type from the thing's
own timestamp (`0x4dd670`) with a divisor of 1. The bracket is age / decay (signed) in 0 to 2, and the sum
adds (value + bracket bonus) / copies (signed). Executed: `0x4c8240`, the park record search, the turn
conversion and the 64-bit helpers; stubbed: the type registry, the counts-test (chosen flag), the own age
(signed timestamp difference, as on the Mac) and the assertion print. 507 of 507 inputs agree with the Mac
reading (`points_full`), which C# follows; the three Mac rules (track-type branch, first-purchase age,
counts-test) each change the result when dropped (`test_points_reading_is_sharp`). The counts-test
`0x4dd920` and the own age `0x4dd670` were not run: they are the counterparts of the Mac's stubbed
`0xdc834` and `0xdc520` by call position only.

Summary: the four rules are the same on the Windows build as on the Mac. The only differences found
are x87 arithmetic: a wrap beyond range and a precision effect that shows only for totals no park reaches
(X3), and a last-bit dependence on the control word for exactly-integer instalments (X4). No C# change
is needed. Not examined: loan payoff and take (`0xccc78`, `0xcc904`), the counts-test, the arrival update
loop, and which control word is live when the bank is built.

### 7.7 Findings X7 to X10: the four rules in the Theme Park Inc `Game.exe`

Question: does the TPI no-CD `Game.exe` (SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`,
the only TPI image analysed) implement the rules OpenTPW follows from the Mac oracle (F9, F10, F12, F13), and
where does it differ from TPW? Same method as 7.6 (constants, object offsets, call structure, message texts; static
reading with Capstone in a scratch directory, terminal output only), then execution with the Mac plans and readings
reused through `oracle/targets.py` and the tp.exe targets' helpers. The addresses are virtual addresses at the preferred
base `0x400000`. The image is incrementally linked, so most calls go through five-byte jump thunks; the stubs sit on the
thunk addresses and some thunks are executed through. The game object is reached through the global `0xade6b0` (the
bank code reads a second copy, `0xaa1484`). Its world words moved: RNG `+0x1eab3c`, tick `+0x1eab40`, the handle slots
from `+0x1eab54` (park `+0x1eab54`, weather `+0x1eab58`), world state `+0x1eab78`, first thing `+0x1eab88`, per-type park
records `+0x1e9618`. The settings are global words in one block (table order, below); the setting-table rows
(`0x999220` onward, 60 bytes each: a type word and the key name) give the names, and the TPI data file that fills
them is `Data/levels/Standard.sam` (`Online_Standard.sam` for online play; the four `levels/*/Standard.sam` files only
override lender names). No data was changed or copied; the values below were read.

**X7, arrival group size (TPW `0x4cf5b0`, Mac `0xcb4e8`, F9): TPI `0x5bcd60`; a different rule.**
Identification (high confidence): the same skeleton. It returns 0 while the world state word is 4 (bankrupt); counts the
guests (argument 20) and asks the park for its points; takes the divisor from PointsPerVisitor, 1 unless above 1 (signed);
adds NewParkBonus to the points (`fild`); raises the group to MinPeople (signed); then cuts it with unsigned compares and no
floor, to 500 minus the guests when the GameType word (`0xc65b80`) is 1 and to 1500 minus the guests otherwise; and
logs the same two capping lines ("Capping the number of people in online park / offline park") and a group-size line.
The three settings words sit at `0xa02230` (MinPeople), `0xa0223c` (NewParkBonus) and `0xa02240` (PointsPerVisitor), in the
settings table's order: MinPeople, TimeBetweenArrivals, FixedRate, NewParkBonus, PointsPerVisitor, and then four new keys,
`RainPercentageModifier`, `StormPercentageModifier`, `SnowPercentageModifier`, `HeatwavePercentageModifier` (`0xa02244` to
`0xa02250`).

Three differences, all executed:

1. **The factor is a weather percentage, not 0.8 / 1.2.** The thing at the game's weather handle (save name `mWeather`)
   has a state word at `+0x70`. States 1 to 4 pick, through a jump table, the Heatwave, Rain, Storm and Snow words;
   any other state (no weather, or an out-of-range value, compared unsigned) gives 100. The shipped words are Rain 90, Storm
   80, Snow 75, Heatwave 120.
2. **The arithmetic changed.** The routine multiplies the integer percentage by the single-precision constant 0.01, then
   by the exact integer total (points + bonus), divides by the divisor in floating point and truncates once (`_ftol`, which
   forces round toward zero); TPW multiplies by a single-precision factor, truncates, and then divides as integers. Because
   0.01 is not exact in single precision, a total that is an exact multiple of the divisor gives one group less than exact
   arithmetic: 80 points + 20 bonus over 25, no weather, is 3 groups, not 4 (30 of 532 inputs differ from exact arithmetic,
   each by exactly one). The result is the same at the control words `0x27F` and `0x37F` on every input; `0x7F` (24 bits)
   differs on 28.
3. **A new exit.** A dword at `+0x14` of the object at `0xa06558` equal to 3 gives 0, after the world-state exit. The
   object is a widely used global (over a hundred references); what the value 3 means was not established. tp.exe has no such
   test in this routine.

Executed result: the routine reading (x87 at 53 bits) agrees on 532/532 inputs; against the TPW / Mac rule on the same world
state 216 inputs differ (67 where the exit on the mode word 3 applies, the other 149 through the factor and the divide). Examples with the
shipped TPI settings (bonus 20, PointsPerVisitor 25, MinPeople 1) and 480 park points: 19 with no weather, 23 in a
heatwave, 17 in rain, 15 in a storm, 14 in snow; the TPW rule on the same state gives 24 without weather and 16 with any
positive weather word. With 80 points the groups are 3, 4, 3, 3 and 2.

Correction to the F9 / X3 wording (not C#): tp.exe's own save code names the handle at game `+0x1da724`, the one the
tp.exe arrival routine reads, `mWeather` (string `0x7628e0`, used where the world words are written and read), and
names `+0x1da734` `mTrafficLights` and `+0x1da732` `mParkGates`. X3's "the Mac's handle is at `+0x1da734`" was therefore a
different handle, not the same one at a shifted offset: the Windows routine reads the weather thing's `+0x30` word,
and the Mac routine, per the PPC reading, the Traffic Lights' `+48`. TPI keeps the weather source and turns the
positive-or-not test into the four percentages. Whether the Mac handle naming is right, or the PC routine really differs
from the Mac one here, needs a look at the Mac world-variable saver; nothing in C# changes from this note, but
`GuestSimulation.TrafficLightsPlaced` rests on the Mac reading only.

Shipped values. TPI `Standard.sam`: `Arrival.MinPeople` 1, `TimeBetweenArrivals` 25, `FixedRate` 15, `NewParkBonus` 20,
`PointsPerVisitor` 25, the four percentages above. `Online_Standard.sam`: MinPeople 1, TimeBetweenArrivals 150,
FixedRate 15, NewParkBonus 20, PointsPerVisitor 2, and no percentage keys (whether the four words then keep the standard
file's values or stay 0, which would give online groups of MinPeople in any weather, depends on the load order, not
examined). TPW's `Standard.sam` is not in reach as shipped; the TPW oracle's "shipped" row (MinPeople 1, bonus 20, 6 per
visitor) matches the Nexus mod file's values, which is a modified file (`TimeBetweenArrivals` 150, `InitialCash`
99999999999 there). If that is representative, TPI differs in the shipped numbers as well: 25 points per guest instead
of 6, and groups arrive every 25 timer units instead of 150.

**X8, loan offer instalment (TPW `0x4cf7c0`, Mac `0xcb7a8`, F12): TPI `0x5bfb70`; same formula.**
Identification (high confidence): the constructor loops over eight 16-byte setting rows from `0xa020c4` (InitialCash
at `0xa020bc`, the `BankAccountInfo` and `LoanInfo` rows of the settings table) and fills eight 32-byte records from
`bank + 0x1c` (the TPW layout, 8 higher, because the base class grew): available when the amount is at most 10000, the
amount, APR, months, the instalment, bought and months repaid (0), the lender; it then sets the bank words (balance
`+0x14`, batch `+0x18`, withdrawals `+0x11c`, and so on, each 8 above TPW). The instalment is
trunc(P x pow(1 + APR x 0.01, N x (1/12) x 0.5) / N): the constants 0.01, 1/12 (`0x93ddf8`, `0x93dde8`) and 0.5, a
`+1.0` where TPW subtracts -1.0 (no value change), the runtime's own x87 `pow` (`0x871c00`, the TPW `0x67c5d0` code
with the same control-word-`0x27F` test) and `_ftol`. Executed with no stub except the base constructor, on the same
506 offer sets as the TPW target: 505 agree with the Mac plan (records and bank words included), the one that does not
is the same out-of-range set (zero terms, a result above 32 bits). Shipped TPI offers (`Standard.sam`; `Online_Standard.sam`
has the same eight): 100000 / 20 % / 36, 50000 / 20 / 36, 25000 / 20 / 36, 10000 / 20 / 36, 18000 / 23 / 24, 30000 / 22 / 30,
80000 / 18 / 48, 65000 / 21 / 30, instalments 3651, 1825, 912, 365, 922, 1282, 2320, 2749 at `0x27F`, `0x37F` and
`0x7F`: the TPW values. The integer-instalment control-word effect is the same (18000 at 20 % over 24 months is 900 at
`0x27F`, 899 at `0x37F`; 16 of the 506 sets change between the two words). Other shipped words: InitialCash 50000 and InitialAdmissionFee 25 (online 20).
C# needs no change for the instalment.

**X9, monthly loan step (TPW `0x4d0370`, Mac `0xcc21c`, F13): TPI `0x5c0b10`; same state changes.**
Identification (high confidence): the bank's message dispatcher calls it for message code 12 (as the TPW and Mac
dispatchers do; code 13 clears the year profit word), and the handler has the same shape: flush the batch into the balance,
the park's money-in word (`+0x1fef8`) and the year profit (`+0x12c`); then for each of the eight records, in order, when
bought: log the instalment line ("Paying the %d'th monthly instalment of loan %d, costing %d"), and with the withdrawals
flag set take M off the balance, record the turn the balance first goes negative, raise the money-out word (`+0x1f808`)
and lower the year profit; raise months repaid; add M - floor((M x N - P) / N) (unsigned) to the year profit unless the term
is 0; and when months repaid reach N, log the paid-off line and clear bought and months repaid. Executed (the loop,
`0x5c0b25` to `0x5c0c49`; the entry sets up an exception frame the sandbox lacks, the tail for a bank in the red is not run):
503/503 whole terms agree with the Mac plan, 503/503 and 506/506 with the Mac reading. The one difference is that this
release build has no assertion calls (the TPW handler asserts the batch is below one million and the term is not 0, and so
does the Mac), so the event list holds only the two log lines; the reading used drops the Mac's two assertion events.
Example, 100,000 over 36 months at 20 %: 36 deductions of 3651 and the year profit falling by 873 a month. C#
(`LoanMath.Pay`) is right for TPI.

**X10, park points (TPW `0x4c8240`, Mac `0xc24c0`, F10): TPI `0x5b05d0`; same rule, fields moved.**
Identification (high confidence): it is the routine the arrival group calls for the points, and has the TPW routine's
structure statement for statement. It walks the thing list from game `+0x1eab88` through the 20-byte thing array at
`0xaa18b0` (next link `+0x14`, type `+0x16`, a type id, 8 above TPW); reads the type record from the registry at `0xa09ac8`
(value `+0x80`, decay `+0x84`, bonus brackets `+0x370`, track-type word `+0x14c`, kind word `+0x600`; the TPW `+0x58`,
`+0x5c`, `+0x268`, `+0x9c`, `+0x4ac`); counts only kinds 0 and 2 and things that pass the counts-test (`0x40998a`, thunk of
`0x5dfc90`); reads the park record (game `+0x1e9618`, 36-byte records, copies `+0x18`, first purchase `+0x1c`, the same
cached search); ages a type with track-type word 0 from its first purchase through the turn conversion `0x608d50` (the
turn difference times the clock rate at `+0x428`/`+0x1c`, divided by 4, times 10,000,000, divided by the day length
864,000,000,000, pushed as `0xc9` and `0x2a69c000`), any other type from the thing's own timestamp (`0x403bb6`, divisor 1);
bracket = age / decay (signed) in 0 to 2; sum += (value + bracket bonus) / copies (signed). Executed: the routine, the
two park record searches (through their thunks), the turn conversion and the 64-bit helpers; stubbed: the type registry,
the counts-test (chosen flag) and the own age. 507/507 agree with the Mac reading `points_full`, and the three Mac rules
(track-type branch, first-purchase age, counts-test) each change the result when dropped (`test_points_reading_is_sharp`).
The only difference is that this build has no assertion call on the thing array. The counts-test and the own age were not
run (call-position counterparts only, as in X6). Data: the type fields come from the per-ride and per-feature `.sam` files
(`Info.AttractionValue`, `Info.NewAttractionDecayTime`, `Attraction[0..2].NewBonus`, for example in `levels/water/rides/Rides.sam`);
the offsets-to-keys mapping is by analogy with TPW and was not traced.

Summary and what a TPI mode would need to change relative to TPW. The loan rules (X8, X9) and the park points (X10) are
the same rules as TPW and the Mac, so a TPI mode needs nothing there beyond the shipped data (offers and instalments are
the same eight; initial cash 50000). The arrival group size (X7) is the one rule that differs: a TPI mode would replace the
0.8 / 1.2 factor with the weather percentage (state 1 to 4 selects Heatwave, Rain, Storm or Snow, else 100) read from the
four new `Arrival.*PercentageModifier` keys, compute `total x (percentage x 0.01f) / divisor` as one floating division
truncated toward zero (keeping the single-precision 0.01, which makes exact multiples one group lower) instead of
truncate-then-integer-divide, add the exit for the mode object word 3 (meaning unknown), and use TPI's settings
(PointsPerVisitor 25, TimeBetweenArrivals 25 in the standard file, 2 and 150 online). The world RNG (X2) is the TPW step
at moved offsets. A TPI mode also has the shifted world-object and bank layouts above if it ever imports original saves.
Not examined: the arrival update loop and the timer settings, loan payoff and take, the counts-test, the TPI-only systems
the settings table lists, and which x87 control word is live at run time.
