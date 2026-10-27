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
  `<= 15` (`staff-strike-grievance`), the age gate `< 24` to `<= 24` (`staff-strike-month`), the seconds per park turn 3750 → 3751
  (`calendar-date`), the day detection `!=` → `>` (`calendar-update`, which misses 31 → 1), the ticket remainder 0 → 1 (`golden-ticket-cadence`), the fixed tick 1/60 → 1/54 s (`park-turn-cadence-frames`), the score's distance divisor `450 → 449`, its new-ride test `<= → <`,
  the ride outcome's good band `< 15 → <= 15`, the choice's tie parity swapped, the needs growth mask `15 → 7`, the full-need penalty `== 100 → >= 100`,
  the thirst and hunger start modulus `50 → 49`, the use effect of hunger read from the thirst word and the exit counter compared `<= 0` instead of `== 0`. A no-op "mutation" fails the
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
| `shop-sale-litter` | the fragment `0xeabb8..0xeae44` of the sale routine `0xeaaf8` (type record getter `0xdc320`, the five need-meter updates with their 0..100 clamps and the "litter gone up" log) | `0x118018` type record (synthetic, registry and type id checked), `0xe6d20` (drink or food message, logged), `0x5fbcc` debug log (arguments logged, format prefix checked) | 526 | **F18**: 213 differ from the old rule (store + LitterEffect, no floor, no ceiling), every one a sum outside 0..100. **Agree** 526/526 with the routine reading (`test_shop_sale_litter_reading_is_sharp`) | `MeterMath.Add` through `shop_sale_litter` (the guest's litter meter, `GuestSimulation.ApplyVisit` since §7.15): **agree** 526/526 (before §7.15 the park-wide store gave the same 213); `UseEffects.Apply` through `shop_sale_meters` (the four guest meters): **agree** 526/526 |
| `guest-litter-trigger` | the first lines of the litter test in the guest update (`0xebd20..0xebd3c`) | none | 521 | no plan side. **Agree** 521/521 with the routine reading: the meter truncated, taken as a byte, `>= 90` | no hook (CSHARP_PENDING): OpenTPW has no guest litter meter |
| `staff-training-split` | `0xfc1c4` (the month-end training split), the list walkers `0x10507c` / `0x1050a4` / `0xfa9a4`, `0xf3588` (train one member), `0x108424` / `0x1082b4` (bank and park things of the game) and the debit `0xcbfdc` with its statistics | `0xb678` assertion (a zero condition fails the run) and the debug print `0x5fbcc` (logged) | 507 | **F17**: 358 differ from the former rule (the plan). **Agree** 507/507 with the routine reading | `ParkStaff.Train` through `staff_training`: **agree** (358 differed before the fix) |
| `ride-repair-duration` | `0xd9c0c` (the mechanic's repair-state entry), `0xdfe0c`/`0xdfe88`/`0xdfe90` (ride state 1, maintenance word `+96`, state 2), `0xd2290` (sub-state setter) | `0xfa9a4` thing id, `0xdfb94` the ride's mechanic id, `0xe6454` arrival state (chosen), `0xdc320` type record (the word at `+508 + 64 · level` chosen), `0xd99ac` release (recorded), `0xb678` assertion and `0x5fbcc` log (recorded); the balance block word `+1004 + 8 · grade` is `WorkDuration` | 529 jobs | **F19** (fixed in C#): 396 differ from the old claim (a flat `WorkDuration`, `plan`; the same without the upgrade factor, `flat-repair`, differs on 352). The routine reading **agrees** 529/529 | `ParkEconomy.RepairTimer` through `repair_timer`: **agree** 529/529 |
| `ride-repair-arrival` | the same | the same | 515 | **F19**: the flow (another mechanic on the ride: log and release; still walking: let go; no broken, upgrade or maintenance state: log and release; else timer, state 13, sub-state 18) **agrees** 515/515 | no hook (CSHARP_PENDING): OpenTPW starts a mechanic at once and has no walk or release |
| `ride-may-open` | `0xde654` (the reopening guard) with the state tests `0xdfe0c` / `0xdfe20` / `0xdfe88` / `0xdfe90` and `0xe2fe4` (track type 3) | `0xdd744` back of the queue (chosen word), `0x45eac` coaster circuit (chosen word), `0x118018` type record (`+156` chosen, registry and type id checked), `0xb678` | 517 | **F27** (fixed in C#): the plan is now the routine reading, **agree** 517/517; the former rule (`VAR_BROKEN == 0`) differs on 264 | `RideOpening.MayOpen` through `ride_may_open`: **agree** 517/517 |
| `ride-set-state` | `0xe0c3c` (`CObject::SetState`) | the script accessor `0xb5758`, `0xb5be0` (VAR_LETMEON) and the writer `0xb57d4` (logged), `0xdcebc` remove from queue, `0xee804` dismiss, `0xeda04` force aboard, `0x52aa8` effect, the closing message constructors `0x116410` / `0x1163e8` / `0x116438` and queue push `0x11688c`, `0xfa9a4`, `0x5fbcc`, `0xb678`; the message list global (TOC −30100) is non-null so `0x1c4794` is not reached | 509 | **F27**: **agree** 509/509 with the routine reading; "entering a state only stores it" differs on 248 | no hook (CSHARP_PENDING): OpenTPW has no state word; `RideVisitorBridge.EnterBrokenState` ports states 1 and 4 (`QueueTests`) |
| `ride-repair` | `0xdef2c`, the branch of a ride that is not in state 2, with `0xe0c3c` (state 0), `0xdfe0c`..`0xdfe90` and `0xe2fe4` | the script calls as above (variables 7 and 4 read; 8, 4 and 6 written), `0x52d98` / `0x52aa8` effects, `0xdd744` and `0x45eac` (chosen words), `0x118018`, `0xfa9a4`, `0x5fbcc` (the non-openable warning is counted), `0xb678`. The constant behind TOC −11016 is checked to be 100.0 | 512 | **F27**: **agree** 512/512 with the routine reading; "a repair only restores the gauge" differs on 423 | `RideRepair.FullGauge` / `OpensAfterRepair` through `ride_repair` (gauge and open word): **agree** 512/512 |
| `ride-breakdown-update` | the fragment `0xe0800..0xe0970` of the ride update `0xe077c` (state dispatch, breakdown check, relief test) with `0xe09f4` (announce) and `0xe0a40` (worn) | `0xdec2c` wear step (recorded), `0xb5758`, `0xb5be0` (VAR_BREAKSTAT chosen), `0xb57d4` (logged), `0x5fbcc`. The world turn (`+0x1da70c`) is chosen | 530 | **F27**: **agree** 530/530 with the routine reading; the former `UpdateRides` rule differs on 280 | `RideBreakdownRules` through `ride_breakdown_update`: **agree** 530/530 |
| `staff-wage-month` | `0xf3364` (the monthly wage, bank message 12), the wage `0xf46bc`, `0x108424` / `0x1082b4` and the debit `0xcbfdc` with its statistics | `0xb678` assertion (a zero condition fails the run) | 511 | **F28**: **agree** 511/511 with the plan and the routine reading (ECON-043 confirmed: PayMultiplier × BaseWage, a 32-bit product) | `ParkEconomy.EndMonth` through `staff_wage_month` (balance and year profit): **agree** 511/511 |
| `staff-dismiss-charge` | the tail `0xf3518..0xf3568` of the dismissal routine `0xf33ec` (wage, bank debit, wages statistic `+0x1f7f0`) | as above | 511 | **F28** (fixed in C#): **agree** 511/511 with the plan and the routine reading; "dismissing is free" differs on 506 | `ParkEconomy.Fire` through `staff_dismiss`: **agree** 511/511 (506 differed before) |
| `park-rating` | `0xc7b24` with the count helpers `0xc5864` (objects), `0xc4064` (staff), `0xc3684` (the census), the list walkers `0x10507c` / `0x1050a4` / `0xfa9a4`, the game getters, the guest cell test `0xe6c6c` and the cell predicates `0x85190..0x85258` over a real cell array | `0x118018` type record (Info.WhichUIType at `+1960`), `0xb678`. The three list-end words (TOC −20404, −20336, −20300) are checked to be zero | 519 | **F29**: **agree** 519/519 with the routine reading; the former rule (directory kind, guests in the park by state, every hired staff member) differs on 249 | `ParkEconomy.ParkRating` through `park_rating`: 134 differ, every one a guest whose cell type and in-park state disagree (crowds of 50 or more) or a staff member with the byte `+3` set (`test_park_rating_csharp_remainder`: ECON-027) |
| `park-value` | the bodies of the cell loop (`0xc1dd4..0xc1e84`: the mask test `0x84a74`, the predicates `0x85174..0x85418`, the cell record helpers `0xcd324` / `0x8b2a0` / `0x8b224`) and of the thing loop (`0xc1ef0..0xc1f68`: `0xdfe20`, the level costs, the repair scaling); the whole `0xc1d4c` once in `test_park_value_whole_function` (16,384 cells, the thing list, the cache `0xc9020`) | `0x118018` type record (the costs at `+440 + 64 · level`), `0xb678`. The "no link" id behind TOC −21032 is read from the file | 526 | **F29**: **agree** 526/526 with the routine reading; the former rule (the sum of scrap values) differs on 355 | `ParkValueRules` through `park_value_thing`: **agree** on the 371 thing inputs (the cell term has no C# counterpart) |
| `object-scrap-value` | `0xe25e8` (the scrap value getter), `0xe2424` (the age percentage), `0x10a9a4` (game pointer), `0x1c4100` (64-bit divide) | `0x118018` type record (synthetic: percentages at `+392 + 64 · level + 4 · year`, costs at `+440 + 64 · level`; registry checked), `0xe4394` the clock stamp (turn × 15000 / 4 × 10^7 as 100 ns units, the value `0xdad90` stores to thing `+20`) and the glue `0x1c5c64` (the signed 64-bit stamp subtraction, as at `0xdc520`). Thing: type `+14`, stamp `+20`, level byte `+76`, use word `+412`. No thing bought after the current turn | 572 | **F21** (fixed in C#): the old rule (catalogue costs summed through the level, no 100 %) differs on 280. The routine reading **agrees** 572/572 | `ParkEconomy.ScrapValue` through `object_scrap_value`: **agree** 572/572 (the use count passed as `ParkObjectState.TimesUsed`) |
| `staff-vitals` | `0xf44c0` (moving step), `0xf459c` (working step), `0xf3d8c` (rest step), `0xf448c` (energy byte below `RestLevel`) | `0xf4d00` (frees the rest area), the role actions `0xd99ac`, `0xd5a3c`, `0xd2b04`, `0xd4b74`, `0xf0380` (logged), `0xb678`. The settings block (`RestLevel`, the grade's recuperation rates) is written in the data section | 526 | **F22**: 427 differ from the old claim (happiness fixed at 100, no energy, no rest). **Agree** 526/526 with the routine reading | `StaffVitals.Walk`, `Work`, `Rest`, `IsBelowRestLevel` through `staff_vitals`: **agree** 526/526 |
| `staff-rest-seek` | the tail of `0xf4900` (`0xf4a60..0xf4c04`) and the world LCG `0x105328` | `0xfb54c` thought, `0xf4c18` navigation (a rest area found or not), `0xf311c` state change, `0x5fbcc` log | 509 | **F22**: 81 differ from the old claim. **Agree** 509/509 with the routine reading (energy byte against `RestLevel`, the thoughts, the one-in-sixteen happiness hit with the world draw) | `StaffVitals.SeekRest` through `staff_rest_seek` (the world draw is passed in): **agree** 509/509 |
| `staff-strike-grievance` | `0xf8410` | `0x1082b4` game -> park, the two analyser sums `0xc21f4` / `0xc2264` (chosen words, arguments checked), `0xb678`, `0x5fbcc`. One chain of staff per role, decoys in the other roles | 515 | **F22**: 313 differ from the old claim (no strikes). **Agree** 515/515 with the routine reading | `StaffStrikeBoard.Grievance` through `staff_grievance`: **agree** 515/515 |
| `staff-strike-month` | `0xf7e18` (the month-end consideration), `0xf7fe0` (the escalation), `0xf8410`, `0xf8bc0` / `0xf8bd0` (active flag), `0x1091ac` (park closed word), `0x1c4100` (64-bit divide) | `0xfc1c4` training, `0xc1fb4` guests in the park (chosen), `0xe4628` clock stamp (chosen), `0x116590` / `0x11688c` message construction and posting (logged as role and kind), the grievance stubs. One run replays 1 to 6 month ends in one sandbox | 513 scripts | **F22**: 442 differ from the old claim (no strikes). **Agree** 513/513 with the routine reading (levels, flags, turns considered, messages and every debug line) | `StaffStrikeBoard.ConsiderMonth` through `staff_strike_month`: **agree** 513/513 |
| `profit-ticket` | `0xd31d0` (the six local golden tickets; ticket 4 is `0xd33dc..0xd3410`), the bank year profit `0xccfa8`, the earned flag `0x128d6c` / `0x12a438` | `0x1082b4` and `0x108424` (game checked; synthetic park and bank), `0xd3664` (the award, logged) | 523 | **F23**: 154 differ from the former rule (`>=`), every one with profit equal to the threshold. **Agree** 523/523 with the routine reading (`test_profit_ticket_reading_is_sharp`) | `ParkObjectives.CheckGoldenTickets` through `profit_ticket`: **agree** (154 differed before the fix) |
| `profit-ticket-hint` | `0x13fdc` (the advisor's "near the profit ticket" hint), `0xccfa8`, `0x128d6c` / `0x12a438` | `0x108424` (game checked; synthetic bank) | 621 | no plan side. **Agree** 621/621 with the routine reading | none: OpenTPW has no advisor hints (`CSHARP_PENDING`) |
| `local-tickets` | all six arms of `0xd31d0`, `0x128d6c` / `0x12a438` (earned flags) | `0xc3b7c`, `0xc3684`, `0xc19e4` (float), `0xc5510`, `0xc3b88` (month count checked), `0xe4628` / `0x1c4100` (the age in months), `0x1082b4`, `0x108424`, award `0xd3664` (logged) | 541 | **F23**: 458 differ from the former rule (every `>=`, average truncated). **Agree** 541/541 with the routine reading | `ParkObjectives.CheckGoldenTickets` through `local_tickets`: **agree** |
| `global-tickets` | `0xd34c0` (four statistics against `+1900`, `+1904`, `+1908`, `+1916`) | `0x1082b4`, `0xc73bc`, `0xc6cec`, `0xc710c`, `0xc8cc0`, award `0xd3748` (logged) | 515 | no plan side. **Agree** 515/515 with the routine reading | `CheckGoldenTickets` through `global_tickets`: **agree** |
| `calendar-date` | `0xe4394` (the world turn → date), `0x10a9a4` (game pointer), `0x1c4100` (signed 64-bit divide), the import glue | the bullfrog time-stamp API by import name: `operator+` (64-bit add of the time difference) and `GetTime` (Python proleptic-Gregorian decomposition, the Mac OS default calendar; null pointers skipped) | 652 (every month boundary of 2000-2003, the leap days of 2000, 2004 and 2400, the missing one of 2100, random turns to 6 × 10^7) | **F30**: **agree** (start + floor(turn × 15000 / 4) s); rates other than 15000 floor the seconds, not round (`test_calendar_rate_floors_the_seconds`) | `ParkCalendar.DateAtTurn` through `calendar_date`: **agree** |
| `calendar-update` | `0xe3f0c` (the calendar update), `0xe4394`, the message constructors `0x116488` / `0x1164a4` / `0x1164c0` | the time-stamp API as above, the dispatcher `0x11688c` (the message type is read from its vtable), the day notification `0x18f4cc` and the month hooks `0x15bbd8` / `0x151698` / `0x155234` (logged with their arguments) | 579 scripts of 1-24 turns from a synchronised calendar | **F30**: **agree** (messages, their order day → month → year, and the stored month / day / year); routine reading agrees | `ParkEconomy.Advance` events and `ParkCalendar.FieldsAtTurn` through `calendar_update`: **agree** |
| `calendar-first-update` | the constructor `0xe3c90` with `0xe4348` (start 2000-01-01), then `0xe3f0c` | as `calendar-update`, plus `GetLocalTime` (a fixed host time) and `SetTime` | 503 scripts from a constructed calendar | **F30**: 503 differ (the stored day is -1, the year 0, the month 0: the first update posts a day and a year message, and a month message outside January). Routine reading **agrees** 503/503 | no hook: OpenTPW starts synchronised (CSHARP_PENDING) |
| `park-turn-cadence` | the world callback's catch-up fragment `0x1c22b0..0x1c24d0` (the registers its prologue loads from the TOC run first), the scheduler clock `0x10e844` → `0x11a588` → `0x10ed54` → `0x117d74` → `0x127cd0`, `0x1c3fbc` (float → u32), the mode wrapper `0x10565c` | the library clock `LbTime_GetClock` (the chosen raw millisecond), the script passes `0xb2838` / `0x9f748` / `0x20688`, the even-substep passes `0x63dd0` / `0xb7ac0`, the park turn `0x10536c` (counted), `0xb5da4`, `0x1aa614` and the six calls of the every-32nd-substep body (all logged) | 508 scripts of raw-clock gaps | **F30**: 57 differ by 1 ms (the clock accumulates gap × scale with one fused multiply-add in binary64 and truncates: ten 16 ms gaps at scale 0.8 give 127, not 128). Routine reading **agrees** 508/508 | no hook: OpenTPW samples turns from a 60 Hz clock (CSHARP_PENDING) |
| `golden-ticket-cadence` | `0xd67f0` (the world's park-turn update) and `0xd2f1c` (the ticket dispatcher), including the unsigned remainder by 100 and the game type word (the lazy object marked constructed) | the bank update `0xcb278`, the staff pool `0xf6e24`, the calendar update `0xe3f0c` and the three ticket checks `0xd31d0` / `0xd34c0` / `0xd35b4`, each logged with its object argument checked | 507 scripts of turn updates (every multiple of 100 and its neighbours, the top of the 32-bit range, game type words 0, 1, 2, 3, -1 and random) | **F31**: **agree** (a check on every 100th turn, unsigned, for game type 0 only: 3 checks after the calendar update). The order differed in C# (checks first, 148 scripts) and a signed remainder differs on 5 | `ParkEconomy.RunsGoldenTicketCheck` through `golden_check_turns`: **agree** |
| `park-turn-cadence-frames` | as `park-turn-cadence` | as `park-turn-cadence` | 506 frame loops with every gap within 150 ms of scaled time, at scales 0.25 to 4 (OpenTPW's x4 included) | **F32**: as `park-turn-cadence` (53 differ by 1 ms); routine reading **agrees** 506/506 | `FixedStepClock` fed with each frame's time × the scale, and `ParkCalendar.Turn` of its tick count, through `park_turns`: **agree** as a window (never ahead of the original, at most one turn behind at every frame) |
| `clock-speed-scale` | the three speed-key release callbacks `0x11315c` / `0x113184` / `0x1131ac`, the clamping setters `0x127c88` / `0x127c48`, the direct setter `0x127c40`, and the scheduler clock read `0x10e844` (→ `0x11a588` → `0x10ed54` → `0x117d74` → `0x127cd0`, `0x1c3fbc`) | the library clock `LbTime_GetClock` (the chosen raw millisecond) | 509 scripts of key presses and clock reads (start scales 0, 0.1, 0.25, 0.8, 1, 2, 3, 4 and random; a wrapping raw counter) | **F32**: the keys step the scale by 1.25 within 0.25..2.0 and the clock adds gap × scale: routine reading **agrees** 509/509; the plan's exact scaled time differs by 1 ms (1 script) | no hook: OpenTPW has four buttons, not three stepping keys (CSHARP_PENDING); the scale's path into OpenTPW is `park-turn-cadence-frames` |
| `attraction-score` | `0xe9164` (the whole attraction score), the excitement callees as in F16 (`0xe02ec`, `0xdff70`, `0xeb7e4` and theirs), `0xd7310` (cell record), `0xdc320` (type record getter), `0xfa9a4` (id getter); the weights are written into the settings block (`+80..+88`, `+112..+136`) | `0xdd3f4` back-of-queue cell and `0xdd118` queue count (chosen), `0xdc520` own age in days (chosen), `0x1083c8` weather thing (`+48` chosen), `0x118018` type record, `0xb678` assertion (a zero condition fails the run), `0x5fbcc` log. The synthetic map holds the cell record `+8` word | 593 | **F24**: 592 differ from the former additive rule (`plan`). **Agree** 593/593 with the routine reading (`test_attraction_score_reading_is_sharp`: 17 variants) | `AttractionScore.TryCompute` through `attraction_score` (excitement from a real `RideVisitorBridge`): **agree** 593/593 |
| `ride-outcome` | `0xea318` (what a use does to happiness and illness) and the excitement callees as above | the same | 561 | **F24**: 498 differ from the former `ApplyVisit`. **Agree** 561/561 with the routine reading (4 variants) | `RideOutcome.Apply` through `ride_outcome`: **agree** 561/561 |
| `attraction-choice` | `0xe8ff4` (the choice loop over the thing list) | `0xdd3f4` back cell, `0xe9164` score, `0xdc834` counts-test, `0xe67c8` reachability (all chosen, calls logged), `0xb678` | 515 | **F24**: 99 differ from the former `ChooseAttraction`. **Agree** 515/515 with the routine reading, call order included (4 variants) | `AttractionChoice.Pick` through `attraction_choice`: **agree** 515/515 |
| `guest-needs-turn` | the whole needs function `0xeece4` (one guest, one park turn), `0xfa9a4` (id getter), `0xd7310` (cell record), `0xefd84` (queue-state predicate), `0xea2b8` (history decay), `0x105328` (world generator) | `0xe6b28` speed update, `0xe6c6c` cell predicate (chosen), `0xe8c58` thought, `0xeb544` bubble clear, `0xfb508` thought object update (all logged), `0xb678` assertion (a zero condition fails the run). The synthetic map holds the three cell words | 529 | **F25**: 529 differ from the former per-second rates (`plan`). **Agree** 529/529 with the routine reading, stub call order included (`test_guest_needs_readings_are_sharp`: 9 variants) | `NeedsUpdate.Apply` and `NeedsUpdate.DecayHistory` through `needs_turn`: **agree** 529/529 |
| `guest-constructor` | the guest constructor `0xe7644` from its entry to `0xe7958`, `0x105328` (eight world draws), `0xfa9a4` | `0xe4810` person constructor and `0x1c4a34` array initialiser glue (logged; the glue saves the caller's TOC at `20(r1)` as the real one does) | 517 | **F25**: 517 differ from the former rule (happiness 60, needs 0). **Agree** 517/517 with the routine reading (3 variants); ExitLevelVar 0 is `UNDEFINED` | `GuestStart.FromDraws` through `guest_start` on the same eight draws: **agree** 517/517 on the compared values |
| `guest-exit-test` | the fragment `0xebdfc..0xebe30` / `0xebef0` of the state-6 handler `0xebbd0` (happiness byte, exit counter, closing word), `0x1091ac` (the closing word getter) | none | 513 | **F26**: 181 differ from the former test (`ExitLevel <= 0 \|\| Happiness <= 10`). **Agree** 513/513 with the routine reading (4 variants) | `GuestLifecycle.ShouldLeave` through `guest_leaves`: **agree** 513/513 |

The C# comparisons use these seams:

- `ParkEconomy.TryBuy` (`shop_sale_litter`): the litter store delta of one sale.
- `ParkObjectives.CheckGoldenTickets` (`local_tickets`, `global_tickets`): the won ticket indices for chosen statistics and limits; the guest
  statistics are a stub, the year profit comes from `ImportOriginalBank`, the records from `ParkCounters.Max`.
- `ParkObjectives.CheckGoldenTickets` (`profit_ticket`): whether the yearly profit ticket is newly won, on a park whose bank holds the
  given year profit and whose Standard.sam has the given `GoldenTicketLocal.ProfitYear`.
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

- `AttractionScore.TryCompute` (`attraction_score`), `RideOutcome.Apply` (`ride_outcome`) and `AttractionChoice.Pick` (`attraction_choice`): the
  score's excitement words (live excitement, whether the type's `ExcitementLevel` byte is nonzero) come from a real `RideVisitorBridge` built as for
  the join targets (the thing behind ride `+36` is not sent and is 0 in these inputs); the other words come from the request (cells, queue, needs,
  weights, age, weather, histories). A zero weight sum gives `UNDEFINED` (the original's `divwu` by zero), a vomit divisor of 0 the exception
  that the bridge reports. The choice hook logs the scores, counts-tests and reachability tests that ran.
- `NeedsUpdate.Apply` with `NeedsUpdate.DecayHistory` (`needs_turn`), `GuestStart.FromDraws` (`guest_start`) and `UseEffects.Apply` (`shop_sale_meters`, a second C# side of
  `shop-sale-litter`): the needs hook passes the three cell words the original reads (OpenTPW passes 0) and compares the meters, the exit counter, the toilet speed term and the temporary history
  (C# has no thought, bubble or world generator); the start hook feeds the eight world draws that the Python side computes with `det_model` and compares happiness, money, the three needs, illness, litter, the
  exit counter and the type (not the prankery byte or the creation turn); the effects hook compares the four meters (the litter meter stays in the park's store).
- `GuestLifecycle.ShouldLeave` (`guest_leaves`) on the happiness meter (single bits), the exit counter and the closing word.
- `ParkStaff.Train` (`staff_training`) on a fresh `ParkStaff` over the synthetic `Standard.sam`, with each role's five
  `PoundsPerTrainingPoint` values replaced by the input's, members added with the internal `AddImported` (reflection) at the input's
  grade and points, and the five budgets set. The members' grade and points and the amount `Train` returns are compared; the
  bridge sends only real staff (the original's list may carry other things, which neither side touches).
- `StaffVitals.Walk`, `Work`, `Rest` and `IsBelowRestLevel` (`staff_vitals`) on a staff member with the chosen grade, energy and
  happiness; the rates and the rest level come from the settings in the original and are passed as the settings' singles.
  `StaffVitals.SeekRest` (`staff_rest_seek`) with the world draw passed in. `StaffStrikeBoard.Grievance` (`staff_grievance`) and
  `ConsiderMonth` (`staff_strike_month`, one call per month end on a board restored with the input's levels, flags and turns).
  The handymen's analyser sums (`0xc21f4`, `0xc2264`) are passed for the grievance; `ParkEconomy` never supplies them.

- `ParkCalendar.DateAtTurn` (`calendar_date`): the date of a park turn at the default clock rate 15000 and the 2000-01-01 start. The
  calendar's other rates are compared with the plan only (`test_calendar_rate_floors_the_seconds`). `ParkEconomy.Advance`
  (`calendar_update`): a park started at the turn before the first step through the internal `StartAtOriginalTurn` (reflection) and
  advanced turn by turn; the `DayEnded`, `MonthEnded` and `YearEnded` events of each turn, in order, are compared with the messages
  `0xe3f0c` posts, and `ParkCalendar.FieldsAtTurn` with the stored fields.

- `ParkEconomy.RunsGoldenTicketCheck` (`golden_check_turns`): the predicate `AdvanceTurn` asks per turn; the game type word 0 is Full
  Simulation, any other word Instant Action (no tickets on either side). The order inside the turn is the plan reading
  `plan-tickets-before-calendar` against the executed order and is covered by `ParkEconomyTests`.

- `FixedStepClock` and `ParkCalendar.Turn` (`park_turns`): each frame's elapsed milliseconds times the speed scale go to
  `FixedStepClock.Advance` exactly as `Level.Update` feeds it (`Time.Delta × SimulationTimeScale`), and the turns so far are read after each frame.
  The oracle's loop runs a turn when the scaled time passes 248 j − 31 ms, OpenTPW at the first 60 Hz tick of 248 j ms, so the comparison is a
  window: the C# count is never ahead and at most one turn behind at every frame (`cadence_frames_equal`).

- `RideOpening.MayOpen` (`ride_may_open`), `RideRepair.FullGauge` and `RideRepair.OpensAfterRepair` (`ride_repair`) and `RideBreakdownRules` in the routine's order
  (`ride_breakdown_update`: the wear step is a recorded call, the announcement and the worn flag are the VAR_BREAKSTAT and VAR_WORN writes, the life gauge comes back as
  single bits). `ParkGate.MayOpen`, `ParkEconomy.UpdateRides` and `Level.OpenAfterRepair` apply them (`RideStateRulesTests`, `ParkGateTests`).

- `ParkEconomy.EndMonth` (`staff_wage_month`, reached by reflection: it is private) and `ParkEconomy.Fire` (`staff_dismiss`) on a one-employee park whose `PerGradeStaffConsts.BaseWage` and
  `PerTypeStaffConsts.PayMultiplier` words are replaced in the synthetic `Standard.sam` (the employee is added with the internal `AddImported`); the balance and the year profit are
  compared. The bank's flag `+276` and the park statistics have no C# counterpart and are not compared.

- `ParkEconomy.ParkRating` (`park_rating`) on a synthetic economy: one type per (`Info.WhichUIType`, directory kind) pair of the input with one object per input object at its level (`Level` is a public setter), the
  staff counts per role (the internal `AddImported`, reflection) and a fixed number of guests in the park by state. `ParkValueRules.CountsForValue` and `ParkValueRules.ObjectValue` (`park_value_thing`) for the costs of
  the levels `0..level` and the state of repair as single bits; cell inputs are skipped (null).

The pricing domain is the free byte clear, `game+36 == 0` and bank `+276 ≠ 0`. Old 1 → new 3
belongs to `path-can-change`.

## 5. Findings
**F23, the yearly profit golden ticket (ECON-039, `ParkObjectives.CheckGoldenTickets`, `0xd33dc`; fixed in C#).** The register said the
profit ticket compares the year's profit with `ProfitYear` directly, while the original (`0x13fdc`) "scales the threshold by a per-objective
factor". Both routines are executed (`profit-ticket`, `profit-ticket-hint`) and they are not the same rule:

- The award is the local ticket check `0xd31d0` (called from `0xd2f1c`, game type 0, every 100 turns). For each local ticket it asks the
  earned flags (`0x128d6c(flag set, index)` over the byte `flags[index]`) and, when the ticket is not earned, compares a park statistic
  with a word of the Standard.sam block (TOC `-30084`). Ticket 4 is `0xd33dc..0xd3410`: `0x108424(game)` is the bank, `0xccfa8` is its
  `+292` word (the year profit), and `cmpw` (signed) of that word against the word at `+1888` (`GoldenTicketLocal.ProfitYear`) with a
  branch on "not greater" decides; the award is `0xd3664(this, 4)`. **There is no factor, and equal is not enough.** The executed
  routine agrees with that reading on 523/523 inputs, and a `>=` compare, an unsigned compare and the 0.9 multiplier each break it
  (`test_profit_ticket_reading_is_sharp`).
- `0x13fdc` is a virtual method of the advisor's rule object (a large vtable of rule methods; the neighbours `0x13de8`, `0x13ec4` do the
  same for people and happiness). Its fields are the advisor's rule table: `this+1492` is `GoldTicketNearToXProfit.ProfitMultiplier` (the
  layout of the Advisor.sam record table puts it 12 bytes lower than the object offset, and the same shift fits the people and happiness
  rules), `this+1496` is that rule's `.Score`. It computes `trunc(single(single(ProfitYear) * multiplier))`, tests the same bank word
  `>` that value (signed) and, while the ticket is not earned (`0x128d6c(flag set, 4)`), returns the score, else 0. With the shipped
  Advisor.sam (multiplier 0.9, score 100) that is the advisor's "you are close to the profit golden ticket" message at 90 % of
  `ProfitYear`. The routine reading agrees with the executed routine on 621/621 inputs, and the missing multiplier, the missing earned
  test and a `>=` compare each break it. This is the "per-objective factor" ECON-039 found: it belongs to the advisor, not to the award.

Before the fix OpenTPW compared `profit >= ProfitYear`; the original is `profit > ProfitYear`. The two differ only when the year's profit is
exactly the threshold: with the shipped jungle value 15000, a year profit of exactly 15000 won the ticket in OpenTPW and wins nothing in the
original (it needs 15001). The oracle's former-rule side and C# both showed 154 mismatches, every one with profit equal to the threshold
(first counterexample: profit 15000, threshold 15000: original no award, old rule ticket 4), and C# now agrees on 523/523.
**Fixed in C#:** `profitThisYear > rules.ProfitYear`; the `[APPROX:ECON-039]` is gone. The statistic was already the right one
(`mProfitThisYear`, the bank's `+292`, a signed 32-bit word). Extension: every other ticket (`local-tickets`, `global-tickets`, executed arm by arm; the earned flags are the real `0x128d6c`, the park
statistics are stubbed). All are `cmpw` and a branch on "not greater" (signed, strictly greater, no factor), with these exact reads:

| Ticket | Original (`0xd31d0` / `0xd34c0`) | OpenTPW before | OpenTPW now |
| --- | --- | --- | --- |
| Visitors | `0xc3b7c(park) > [1872]` | `>=` | `>` |
| People in park | `0xc3684(park) > [1876]` | `>=` | `>` |
| Happiness | single `0xc19e4(park, 0)` (average happiness, `fcmpo`) `> single([1880])` **and** `0xc3684(park) > [1884]` | the average truncated to an integer, `>=`, plus a count of guests at least that happy `>=` `AtLeastThisManyHappyPeople` | the single average `> Happiness` and the guests in the park `> AtLeastThisManyHappyPeople`; nobody is filtered by happiness (`IParkGuestStatistics.AverageHappiness` is now a float: the mean of each in-park guest's `(u8)trunc(happiness)`, 0 while the park is closed, as `0xc19e4` computes it per PPC-scenarios; the routine itself is stubbed here) |
| All researched and built | `0xc5510(park)` low byte nonzero | same | same (no C# input in the oracle) |
| Profit | bank `+292 > [1888]` | `>=` | `>` |
| Recent visitors | `0xc3b88(park, [1896]) > [1892]` **and** the park age in 30-day months `> [1896]` | the last N closed months `>=` | lifetime visitors minus the snapshot at the close N back (the open month included, N + 1 closes needed) `>`, and the park clock's age in exact 30-day months `> N`, as tp.exe `0x4c94d0` / `0x4d4bc0` read (ECON-052 retired) |
| Coaster height, go-kart excitement, water length | `0xc73bc` / `0xc6cec` / `0xc710c` `> [1900]` / `[1904]` / `[1908]` | `>=` | `>` |
| Big park | `0xc8cc0 > [1916]` (`MinCellsCovered`) | `>=` `MinCellsOwned` | `>` `MinCellsOwned` (key mapping is ECON-038) |

The global tickets are a small routine of their own (`0xd34c0`, four statistics against four words, each award `0xd3748` tests its own earned
flag), so they are executed too. The secret camera ticket (`0xd35b4`: `0xc8b08 == 100` percent covered) and the never-awarded all-land
ticket are not run, and C# keeps its `>= MinCellsCovered` records for them. Wherever the ticket checks differ from C# only at the limit
itself, the shipped files give limits of 100 visitors, 200 and 150 guests, average happiness 75, 15000 profit, 350 recent visitors and the
global 105, 90, 50 and 2000 / 3000, so for example a park with exactly 100 admissions no longer wins the visitors ticket (it needs 101) and
a go-kart excitement of exactly 90 no longer wins (91). Not compared with C#: the age gate of the recent-visitors ticket (the original
counts 30-day months of park age, OpenTPW closed calendar months; the oracle inputs keep the age at least three months away from the
month count, and the boundary is executed in `test_local_tickets_arms_and_boundaries`) and the big-park key. Inputs with a negative
`RecentVisitors` limit also differ (a short park passes in C#); no shipped file has one.

The gate around the
routine (game type 0, every 100 turns) is not executed, and the advisor hint has no C# counterpart (the advisor slice is unused).

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
window (32-bit whole-millisecond times, C remainder); FORCEUNBOUNCE still takes the first slot. (Corrected October 17, 2026 from tp.exe: FORCEUNBOUNCE `0x00555842` drops only the deadline compare of UNBOUNCE `0x005557A3`; the 200 ms window of each second still gates it, `RideVisitorBridge.TakeUnbounced`.)
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
- **500 cut in online parks (ARRIVAL-002; the GameType part settled on the PC).** When word 0 of the GameType object (`data:0x53d98`, PPC-review "New
  binding": 0 Full Simulation, 1 online, 2 Instant Action) is 1, the group is first cut to `500 − guests`. Otherwise the
  1500 cut applies. Offline parks use 1500. C# now cuts to 500 − guests for an OpenTPW online visit (`GuestSimulation.OnlineVisit`, set by
  `Level` for a non-sandbox park visit). tp.exe settles both former assumptions: the park-info dialog's completion
  `0x4c0830` calls SetGameType(1) when a published park is entered, and the main loop runs the park turn for GameType 1
  through `0x5166b0` (call at `0x54f760`), so the visited park runs the arrival step with the 500 cut. ARRIVAL-002 keeps
  the visit's inputs: the original loads `online_Standard.sam` for GameType 1 (`0x51573c`), OpenTPW reads `Standard.sam`,
  and a read-only visit counts no park points.
- **No floor (fixed in C#).** Both cuts compare `guests + group` with `cmplwi` (unsigned) and store `limit − guests` as is.
  With more guests counted than the limit, the group is negative: 1700 guests give −200. C# gave 0 until the PC twin
  confirmed the rule (tp.exe `0x4cf6e5`, `jbe` at `0x4cf690`/`0x4cf6ee`) and every reader of the word was found to treat a
  negative group as 0; it now keeps the negative group and compares unsigned. Statically, the caller `0xcb2ac` creates guests
  only while the count is above 0 (`0xcb3bc`). Executing it with a negative group is F11: the group does not hold the
  arrival state.

After the factor and the online cut 49 of the 515 inputs still differed from GUESTS.md and C# (the plan and C# pinned the
same inputs). All were the missing floor (the former ARRIVAL-004): 36 in limit mode 1 and 12 in the other modes, where more
guests are counted than the limit and the original's group is negative while C# gave 0, plus 1 where a negative group
(MinPeople −5) wraps the unsigned compare. With the floor removed and the compare unsigned, the plan and C# agree on all 515. The 42 limit-mode-1 mismatches of the first fix fell by only 6, because most of them had
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
  ends it. Neither a negative nor an empty group ends earlier. The phases come from the vehicle's level script (`bus.RSE`: 1 driving in, 2 at the stop, 3 and 4 after the trigger, 0, 5 and 6 driving off).
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

Result: the floor changed no guest or timer behaviour. The C# floor at 0 produced the same guests and timer as the
original's negative group, differing only in the stored group word and the empty-group vehicle step. On the PC that step
(`0x4cf4de`) only writes the vehicle script's variable 0 again (`0x51a2f0` with a vehicle current, `0x51a649`), and the other
readers of the word, the boarding gate `0x51a760` (`cmp 0xa` at `0x51a83d`) and the advisor score `0x59f220` over the group
history, treat a negative group as 0; C# now keeps the negative word (the former ARRIVAL-004 is retired). The
brief's static reading, a negative group holding the state, differs on 66 scripts and is refuted. The
`plan` side models the stop gate as the vehicle phase 2. `UpdateArrivals` steps once per park turn from the turn after a group starts, because tp.exe's twin `0x4cf3e0` runs once per world turn (at the end of `0x516380`, through `0x4d7b20`); the vehicle's drive to the stop is now its own level script's (`GuestSimulation.ArrivalVehicles`, GUESTS.md "Arrivals"). C# is not compared.

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
does not write VAR_RIDECLOSED or the open word were approximations that F27 settles (executed `SetState`, the admission on the notice turn, the callers of `SetState(0)`). Before → after: the old
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

(Superseded: section 7.15 gives the guests the litter meter, the bin search and litter by cell; the C# hook now agrees.) At the time C# was **not changed**, deliberately. `ParkEconomy` keeps a park-wide store (`LitterScaled`, 1/100 item) and `TryBuy` has no guest, so the
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
1 balloon, 2 costume): the balloon and costume use (ECON-044, modelled from tp.exe `0x4fe1e0`, docs/GUESTS.md "Balloons and costumes"), which reseeds
the world generator and creates the balloon's object; it does not touch the boost.
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

*Since traced (PC).* The shop window's ingredient slider (0..100, tp.exe `0x4b06d2`, shown for ingredients 1..4 by `0x4b0950`) sets the amount through the window's apply `0x4b0aa0` (apply-all `0x4b0ae0`); OpenTPW keeps it in the economy record, saves it and imports `mAmountOfSpecialIngredient` (`0x4dcdc1`). Fat, Salt and Ice
change toilet and thirst in the original but not in OpenTPW (the visit still resets the satisfied needs to 0, GUESTS.md); they are a separate gap (the need effects of
`0xeaaf8`, `+324..+340`), not part of WALK-001.

**Fixed in C#** (before -> after: the boost stayed 0 in every game; now `ApplyVisit` adds `GuestSpeed.RaiseBoost( boost, ingredient, amount )` when a guest leaves an
object; `RideVisitorBridge.SpecialIngredient` comes from `UsageInfo.SpecialIngredient` through `OriginalObjectRuntime.CreateVisitorBridge`, the amount is the build default
50 (the shop window's slider and the save field have since been added, WALK-001 retired). `walk-boost` C# 521/521; the factor 6 -> 5 mutation is detected. The determinism hash does not change: its
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
full, as the original does. Two things stay as they were and are approximations: **the park value** (ECON-026) kept summing the old
rule (catalogue costs through the level × the percentage), because the original's valuation is a different quote (PPC-economy.md; since F29 the park value follows the
original's `0xc1d4c` instead); and
**the Windows build**: this was the Mac routine only; the tp.exe counterpart `0x4e2400` is now executed and identical (section 7.9, X13,
which also finds the use counter's clear and its one increment at `+0x1a0`; ECON-025 is retired). The stubs are the type
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

**F30, the park calendar and the cadence of park turns (ECON-001, ECON-002, ECON-003, UI-023; `ParkCalendar`, `ParkEconomy.AdvanceTurn`; C# gains a
calendar API, its behaviour is unchanged).** The static trace called the ECON-001 claim contradicted: the calendar is not driven by a fixed
tick count per day, it converts the world turn (game `+0x1da70c`) with the clock rate at calendar `+28`. `ParkCalendar` already did that
(turn × 15000 / 4 = 3750 s from 2000-01-01, 248 ms turns, `[BIN:STP-PPC:0x100E4394]`); what had not been executed was the conversion, the
change detection and the real-time rate of the turn counter. Four targets now run them.

1. **`0xe4394`, the conversion** (`calendar-date`). A 64-bit product turn × rate (both `u32`: `mulhwu` / `mullw` pairs), a signed 64-bit divide by 4
   (`0x1c4100`), a multiplication by 10^7 and an addition to the start time stamp (`operator+` of the bullfrog `TbTimeStamp`, the 100 ns unit
   since 1601) give the new time stamp, which `GetTime` decomposes through the operating system's calendar (`LongSecondsToDate`, proleptic
   Gregorian) into year, month, day, hour, minute and second. Executed with those two library calls stubbed by import name (Python civil
   arithmetic) the routine agrees with `2000-01-01 + floor(turn × 15000 / 4) s` on all 652 inputs (every month boundary of 2000-2003, the leap days of 2000,
   2004 and 2400, the missing one of 2100, random turns to 6 × 10^7), and C# `ParkCalendar.DateAtTurn` agrees too. With another saved
   rate the division floors (5 turns at rate 3 are 3 s, not 4). Turn 24 is the first of 2 January (23.04 turns make a day), turn 715 the
   first of February, turn 8,433 the first of 2001 (00:22:30; the leap year has 8,432.64 turns).
2. **`0xe3f0c`, the update** (`calendar-update`, `calendar-first-update`). Once per park turn (`0xd67f0`, which runs the bank update `0xcb278`, the staff pool
   `0xf6e24`, this update and then the golden-ticket check) it decomposes the date of the current turn and compares the day (1-based), the month (0-based) and the
   year with the fields stored at `+20`, `+16` and `+24`. Each difference posts one message: a day message (type 11) followed by a notification
   call `0x18f4cc` with the date's second, minute, hour, day, month and year − 1900, a month message (type 12) followed by three hooks that take the
   new month (1-based), a year message (type 13). The order is day, month, year. The dispatcher `0x11688c` delivers a message to every registered
   thing at once (a loop over the subscribers calling `0xfab68`; read, not executed), so the bank's year handler and the monthly loan and training handlers run
   inside the update, before the golden-ticket check of the same turn (F31). Then the three fields are stored. At most one message of each
   kind comes however many days have passed; a turn is 3750 s, so one day at the most does. The executed update agrees with the rule C#
   implements (`ParkEconomy.AdvanceTurn`, now through `ParkCalendar.Update`) on 579 scripts, and the C# events (`DayEnded`, `MonthEnded`,
   `YearEnded` of `ParkEconomy.Advance`) agree with the messages on every turn, including 31 January → 1 February, the leap day, the end of
   2100's February and the year change (all three at turn 8,433). The one difference is the start: the constructor `0xe3c90` stores
   month 0, day -1 and year 0, so the **first update of a new calendar posts a day and a year message** (and a month message outside
   January) whatever the date, 503 of 503 constructed inputs. A loaded park save keeps the calendar's stored date, so a new park shows it (and, below, a loaded
   restart save), and its effect on a new park is a day message and a year message at the first turn; the bank's year handler resets a profit that is 0 anyway. New OpenTPW parks start with the original's pending first day (ECON-063, retired: `ParkEconomy.PendingInitialDay`). Supported TPW initial-calendar imports at turn 0 retain a pending day message: the first actual turn rolls the object histories and accumulates the running daily-average challenge before raising `DayEnded`; own saves preserve the pending state. No imported year cache is inferred. On PC (tp.exe)
   the world init `0x5156A0` syncs the month and year caches to turn 0 (`0x4D7AF0` → `0x4F85A0`) but leaves the day at -1, so a new park's
   first update posts a day message only; the loader `0x4F7F30` restores the saved caches, and the restart saves (`restart.INTS`), written
   before the first update, keep day -1, so their first update after a load posts the day message too.
3. **`0x1c22b0..0x1c24d0`, how real time becomes park turns** (`park-turn-cadence`). The world callback keeps a scheduled time and samples the scheduler clock
   `0x10e844` (executed down to the library clock `LbTime_GetClock`, a millisecond counter). The scheduler adds each raw gap times its scale
   (`+24`, 1.0 from the constructor `0x127c08`) to a binary64 accumulator with one fused multiply-add and truncates it (`0x127cd0`,
   `0x1c3fbc`). The loop then drops a backlog above 2000 ms (`previous = now − 2000`) and, while `now > previous`, advances `previous` by 31,
   counts a substep and runs its body: a script pass, on every second substep two more passes, on every eighth substep **the park turn** (at most 3
   in one call; the surplus phases are skipped, not queued) and on every 32nd a slow body; with application flag 8 clear and gameplay flag 1 set
   the bodies are skipped. Executed over 508 scripts the loop agrees with that reading (the 57 differences with the plan are the 1 ms of
   the fused rounding at non-dyadic scales). In real time, with frames of 16, 17 or 33 ms alike (`test_park_turns_per_real_second_by_scale`):

   | Clock scale | Park turns per real second | A game day (23.04 turns) | A 30-day month | A 365.25-day year |
   | --- | --- | --- | --- | --- |
   | 0.25 (the lowest) | 1.00 | 23 s | 11.5 min | 140 min |
   | 1.0 (the default) | 4.03 | 5.7 s | 2.9 min | 34.8 min |
   | 1.25 / 1.5625 / 1.953125 | 5.04 / 6.30 / 7.87 | 4.6 / 3.7 / 2.9 s | 2.3 / 1.8 / 1.5 min | 28 / 22 / 18 min |
   | 2.0 (the highest) | 8.06 | 2.9 s | 1.4 min | 17.4 min |

   A gap of 2000 ms or more (or a stall of 5000 ms, 3000 of which are dropped first) runs 65 substeps and 3 park turns and ends 15 ms ahead, so a
   frame rate below about 4 a second loses park time. The turn arrives when the scaled time passes 248 j − 31 ms (the substep ceiling), up to 31
   ms before OpenTPW's 60 Hz sampling reaches 248 j ms; the average rate is the same.

**What bounds the rate.** The rate is exact only if the library clock is wall-clock milliseconds. `LbTime_GetClock` is `UTimer::GetAbsolute() / 1000`
(`SamsUtilities::UMicroseconds`); the route that call takes (a function pointer, a flag-selected hardware route with the constants 60.15 and
60,000,000, or the Toolbox `Microseconds`) is chosen at run time and none of them is executed, so the real-time rate stays bounded (turn =
248 ms of the library clock × the scale), not measured. The scale starts at 1.0 and the only writers of `+24` are the clock constructor and the three speed
callbacks (F32). The 100,000 µs interval of the application's own timer is an allocator guard, not a game rate (PPC-clock.md).

**C# and players.** `ParkCalendar` gains `DateAtTurn`, `FieldsAtTurn` and `Update` (the original's comparison of the stored month, day and
year) and `ParkEconomy.AdvanceTurn` raises its day, month and year events through them. Nothing changes for a player: at normal speed a game day
is 5.7 s (it was before), January is 177 s and the leap year 2000 34.9 minutes; the oracle now shows these figures are the original's and that
the day, month and year events fall on the same turns. ECON-001 stays an `[APPROX]` with its text narrowed to what is left: the 60 Hz sampling,
the catch-up limits of the loop above, the substep phases and the clock route. ECON-002 stays (the OS calendar is a Python stand-in for
`LongSecondsToDate`, the default Gregorian calendar). Not changed: by the listing the original keeps running the calendar when the park is bankrupt (the turn update
skips only the thing loop at world mode 4, `0x1053a8`; read, not executed), where OpenTPW stops (ECON-030).

**F31, when the golden tickets are checked (ECON-033, `ParkEconomy.RunsGoldenTicketCheck`, `0xd67f0`, `0xd2f1c`; the cadence was right, the order inside the turn is fixed).**
The static trace called "checked at month end" contradicted: the tickets are checked each time the world turn is a multiple of 100, in game
type 0 only. `ParkEconomy` already did that (`[BIN:STP-PPC:0x100D67F0]`); `golden-ticket-cadence` now executes it. `0xd67f0` is the park-turn
update of the world object (called once per turn from `0x10536c`). It runs the bank update `0xcb278` (world `+708`), the staff pool `0xf6e24`,
the calendar update `0xe3f0c` (world `+672`, F30) and then tests the world turn (game `+0x1da70c`, unsigned): the remainder by 100 is
`turn − 100 × (turn / 100)` with the quotient from a multiply-high by `0x51eb851f` and a shift by 5, so 2^32 − 96 is a multiple and a signed
remainder is wrong at the top of the range. A multiple of 100 calls the dispatcher `0xd2f1c` (world `+704`), which constructs the game type
object lazily (`0x12bb64`) and runs the three checks `0xd31d0`, `0xd34c0` and `0xd35b4` (read as the local, global and secret conditions; here stubs that
are logged) only while that object's word 0 is 0. Executed over 507 scripts the plan (a check on every 100th turn in Full Simulation, as C#) agrees on every call,
and so does C# (`ParkEconomy.RunsGoldenTicketCheck`, 0 mismatches); the game type words 1, 2, 3 and -1 run no check (OpenTPW has two modes: word 0 is
Full Simulation, any other word has no tickets). A check every 100 turns is 4.34 game days apart (the 4.34 of PPC-scenarios), 24.8 s of real time at
the default clock scale; tickets are not month-end events.

**Fixed in C#:** the calls inside one turn run in the other order. The original runs the calendar update first, and the dispatcher delivers its
day, month and year messages to their listeners at once (F30), so the checks see the closed month's admissions and the year's profit after the
year message reset it. `ParkEconomy.AdvanceTurn` ran the check first. Example (`TheGoldenTicketCheckSeesTheStateAfterTheCalendarUpdateOfItsTurn`):
turn 648,000 is the first turn of 2077 and a multiple of 100; with a year's profit of 20,000 (the jungle's `ProfitYear` is 15,000) the old order
awarded the profit ticket from the old year's profit, the original and the new order find none. At any other multiple of 100 nothing differs;
a month or year change that is also a multiple of 100 is rare (1 in 100). The determinism hash did not change. The oracle pins the old order as
`plan-tickets-before-calendar` (148 of 507 scripts differ).

**F32, the speed scale and what it scales (ECON-004, UI-022; `GameSpeed`, `GameSpeedScale`, `ParkEconomy.AdvanceFixedTick`, `Level.Update`).**
OpenTPW's speed buttons sped up the economy clock only ("faster speeds only speed up the economy clock, not rides/guests", UI-022), a design
choice with no evidence. The original has **one clock scale**, and it multiplies everything that runs on the scheduler.

1. **The keys** (`clock-speed-scale`). The game input group has three speed records, ids 6, 7 and 8 (tokens `0x6d00`, `0x6b00`, `0x6a00`; their physical names are
   not qualified). Their release callbacks `0x11315c`, `0x113184` and `0x1131ac` act on the scheduler object behind TOC -30168: divide its scale (`+24`) by
   1.25, multiply it by 1.25, and store 1.0 (through the direct setter `0x127c40`). The first two clamp the result to 0.25..2.0, so from 1.0 the
   scales are 1.25, 1.5625, 1.953125, 2.0 and, downwards, 0.8, 0.64, 0.512, 0.4096, 0.32768, 0.262144, 0.25; a scale above 2.0 (only a direct store gives
   one) steps back to 2.0 on the next key, and each callback returns 1. The constructor `0x127c08` (one caller) stores 1.0. These four routines are the only callers of the scale setters
   (a scan of the branch-and-link sites; a direct store to `+24` by other code, such as a clock restore, is not excluded). Executed over 509 scripts the reading agrees. There is no x4.
2. **What the scale scales** (`clock-speed-scale`, `test_scaled_clock_callers`). The scheduler clock `0x10e844` returns the sum of every raw millisecond gap
   times the scale at that read (a fused multiply-add into a binary64 accumulator, truncated to a `u32`; a scale change applies to the whole gap since the previous read:
   1000 raw ms at 1.0, then a faster key, then 1000 raw ms give 1000 and 2250). It has 60 call sites: the world callback (the substeps and
   so the park turns, F30), the RSE interpreter (GETTIME `0xaf6d8`, WAIT `0xb0674`, WAITABS `0xb0764`, SETTIMER `0xb1fec`, GETTIMER `0xb200c`), the
   board bounce code (`0xad350..0xae600`) and the animation updater `0xa6f70` (`0xa6fa0`). Only 11 sites read the unscaled clock `0x10e864`, among them the
   advisor's speech chain and the animation channels flagged `0x40` (`0xa6fd8`). Ride scripts, guests (through the park turn and the animations) and
   the economy therefore all run at the scale; the UI and the advisor's lip-sync do not. "The economy only" is contradicted.
3. **No speed control in the UI.** `UITEXT 426 "Speed :"` has no code reference, and the only speed strings are debug text (PPC-ui.md); no button or menu entry that
   calls the key callbacks was found. The three keys exist, so the scale is a real original feature, but OpenTPW's pause / x1 / x2 / x4 buttons are its own,
   and x4 exceeds the original's maximum of 2.0 (the clock itself does not clamp; only the keys do).

**Changed in C#.** A speed is now the clock scale of the whole simulation. `GameSpeedScale.Of` maps pause, x1, x2 and x4 to 0, 1, 2 and 4, the HUD's `TimeScale`
carries it (also for a level without an economy), `Level.Update` feeds the fixed clock `Time.Delta × scale` (as before), and the economy runs one tick per
fixed tick (`AdvanceFixedTick` used to run `Speed` ticks, so at x2 the economy's turn ran twice as fast as the guests' own turn and the rides). Before → after
for a player at x2: the park clock and the economy ran at x2 (a day 2.9 s) while guests walked and rides cycled at x1; now guests, rides, scripts and the
economy all run at x2 and stay on one park turn. Real time per game period (23.04 turns a day, 4.03 turns a second at x1):

| Speed | Park turns a second | A day | A 30-day month | A 365.25-day year |
| --- | --- | --- | --- | --- |
| x1 | 4.03 | 5.7 s | 171 s | 34.8 min |
| x2 (the original's maximum scale 2.0) | 8.06 | 2.9 s | 86 s | 17.4 min |
| x4 (OpenTPW only) | 16.1 | 1.4 s | 43 s | 8.7 min |

The fixed clock accepts 16 ticks (267 ms of scaled time) per frame, so x4 keeps up down to 15 frames a second; below that it loses time (the original
carries a backlog of 2000 ms, ECON-001). The determinism hash did not change (its fixtures advance the economy four ticks a step themselves, as the
old Fastest did); `ParkSaveFile` keeps storing the speed, and an old save at x2 now runs everything at x2. ECON-004 and UI-022 stay open with narrower texts:
the keys' physical names, whether the shipped game shows a speed control, and the x4 and the 1.25 steps that OpenTPW does not follow.

**F24, the attraction choice (GUESTS.md "Decision", `GuestSimulation.ScoreAttraction` / `ChooseAttraction` / `ApplyVisit`; fixed in C#).**
Three targets execute the guest's decision and what a use does: `attraction-score` runs the whole score `0xe9164` (with the excitement callees of F16
executed too), `ride-outcome` the use outcome `0xea318`, `attraction-choice` the choice loop `0xe8ff4`. In each the routine reading agrees on every input,
every part of it is needed (`test_attraction_score_reading_is_sharp`: 17 variants, `test_ride_outcome_and_choice_readings_are_sharp`: 8), and C#
(`AttractionScore`, `RideOutcome`, `AttractionChoice`) agrees after the change (before it had no seam: the additive formula was inline).

*The weights and where they come from.* The settings block (data `0x54860`, TOC -30084) holds the `PeepInfo.*` values as words, four bytes per key in
the order of the keys in the file's schema (60-byte records from data `0x34d4c`: `ExitLevel`, `ExitLevelVar`, `StartingCashVarPc`, the three happiness
changes, `PerfectRide`, `GoodRide`, `OKRide`, `RideVomitDivisor`, `ToiletDesparate`, `VomitCapacity`, the five entry-fee keys, `DecisionVariable1..8`, the seven
`DecisionVar*Weight` keys, the four `OpinionFor*`, `PrankeryLikelihood`, `StinkbombLikelihood`), starting at word `+12`: position `p` (1 for `ExitLevel`) is word
`8 + 4 p`. The loader's store itself (a generic reader that walks the schema records) is not traced; the binding rests on the order and on independent readers that
agree with it: the guest constructor reads `+12` and `+16` (`ExitLevel`, `ExitLevelVar`), `+20` (`StartingCashVarPc`) and `+156` (`PrankeryLikelihood`: the byte `+448` becomes
100 when a draw modulo 100 is below it), the happiness routine `0xeb6f4` reads `+24 / +28 / +32` (Small, Medium, Big), the outcome `0xea318` reads `+36 / +40 / +44`
(Perfect, Good, OK) and `+48` (`RideVomitDivisor`), and the score reads `+80 / +84 / +88` as three decision variables, the very ones the `.sam` describes (days a ride is new,
the new-ride multiplier, the indoor-in-rain multiplier), and `+112..+136` as the seven weights in the key order distance, queue, excitement, thirst, hunger, toilet, illness.
The shipped values are 1, 1, 1, 2, 2, 2, 2 and 7, 5, 5 in every theme (`GuestAssetTests.ShippedDataFeedsTheAttractionScoreInsideItsTables`).

*The score.* In order: the guest's last used attraction, when it still exists and is of the same type (`+14`) as the candidate, scores the candidate 0 (`0xe92d4..0xe931c`). The seven
matches are
(1) distance `100 - min(100, 100 (dx² + dy²) / 450)` for the squared displacement of the guest's cell and the candidate's back-of-queue cell (`0xdd3f4`), divided (unsigned) by the signed
cell record word `+8` at that cell when it is nonzero (the environment grid has component 4 there, which only the fireworks' row 7 writes; C# reads it since GUEST-001 was retired);
(2) queue `100 - 100 count / (4 max(cells, 1))` in unsigned 32 bits, only when the squared distance is at most 8 (else the match and its weight are 0), **not clamped**: a queue above four per cell wraps, and the
whole sum wraps with it (count 9 in 1 cell gives -125 as a word);
(3) excitement `2 (50 - min(50, |preferred - live excitement|))` over the bytes of `0xeb7e4` and `0xe02ec(ride, 0)`, its weight counted only when the low byte of the type's `ExcitementLevel` (`0xe02ec(ride, 1)`) is nonzero;
(4, 5) thirst and hunger `trunc(sqrt(effect / 10) need²)` for the need (`+420`, `+424`) and the type's `ThirstEffect` / `HungerEffect` (`+324`, `+328`, low byte) in tens: the original indexes an 11 × 11 byte
table (data `0x41222`, TOC -27104) and the table is exactly this formula on all 121 entries (`test_attraction_score_tables`), so C# computes it and stores no table (need 50 / effect 50 gives 17, 100 / 100 gives 100);
(6, 7) toilet and illness `curve[(byte(meter) + 4) / 5]` with the 21-byte curve at data `0x4129b` (nine zeros, then 1, 2, 5, 10, 20, 30, 40, 50, 60, 75, 90, 100), only for a relief object (thing `+46` bit 0); the curve has no formula and is
written into `AttractionScore` as 21 gameplay constants (the one table of original bytes in this slice: say so if it should instead be read from the user's game).
The matches are combined as `sum(weight · match) / sum(all seven weights)` (`divwu`, so a zero sum is undefined): **all** weights count, also those of needs the guest does not have, so a perfect ride for a guest
without needs scores `300 / 11 = 27`, not 100. Then, in this order: an age (`0xdc520`, days) up to `DecisionVariable1` multiplies by `DecisionVariable2` (a new ride); drops falling (weather `+48 > 0`) and an indoor type
(`+276`, `UsageInfo.ISIndoors`) multiply by `DecisionVariable3`; a golden-ticket cost (`+196 > 0`) multiplies by `1 + (cost + 1) / 10` (double precision); otherwise a nonzero `+440` (the first upgrade's cost,
`Upgrades[0].CostOfUpgrade`: the type record's words follow the schema order too, four bytes per key from `+16` with the block-marker records taking none, which gives the known `+316` `ExcitementLevel`, `+324..+340` the five effects, `+424` `InitSpeed`, `+436` `QueueWaitTimeConstant`, and `+196`, `+276`, `+440` as `GoldenTicketCost`, `ISIndoors` and the key after `QueueWaitTimeConstant`) above 3000 multiplies by `1 + q / 10` with `q = cost / 3000f` (single precision; 23 of the 129
shipped choosable objects exceed it, 9 have a golden-ticket cost, 43 are indoors); finally signed divisions by 5, 4, 3, 2 for the first match in the four used-history slots (guest `+480..+486`) and for each match in the four temporary-history
slots (`+488..+494`). Used history is pushed when a use ends (`0xe9fd4`, `0xea268`), the temporary history when a guest turns away from a join (queue too long `0xece18`, the two excitement refusals `0xecf3c`, `0xea290`), and a
0 shifts into every guest's temporary history on every park turn that is a multiple of 20 (`0xef21c`, `0xea2b8`; not phase-gated), so a refusal is forgotten after 80 turns (about 20 s). The used slot 0 never divides an existing
attraction (it scores 0 through the type test), so the effective used divisors are 4, 3 and 2.

*The choice.* `0xe8ff4` (called from the state-6 handler `0xebbd0` at `0xec4c0` when a three-way draw (a value modulo 3, not traced to its source) is 0 and the park turn exceeds the guest's `+508` by more than 30) walks the thing list from
game `+0x1da746`; a thing with `+46` bit 0x4 (`Info.IsChoosable`) and a back-of-queue cell is scored; a score below 10 (unsigned) is ignored; one that beats the best so far (signed, starting at 0) or ties it on an odd world turn
(game `+0x1da70c` bit 0) becomes the target if it passes the counts-test `0xdc834` (F10) and the reachability test `0xe67c8`. There is no random bonus and no "positive only" rule; an unsuccessful decision lowers happiness by
`PeepInfo.SmallHappinessChange` (`0xeb6f4(guest, 0)`) and starts an idle interlude (`0xe8b74(guest, 4)`, state 8), and the guest tries again after 30 more turns. A **minor decision** (`0xe9b2c`) rescores the cells within two cells
of the target attraction's back cell for attractions of class 3 and may switch the target; it is not traced further.

*The outcome (`0xea318`).* Nothing happens when the live excitement byte is 0 (shops, relief objects). Otherwise the difference `|preferred - excitement|` (bytes) gives `PerfectRide` below 5, `GoodRide` below 15, `OKRide` below 40 and nothing at 40
or above (strict compares; **no penalty branch**), added to happiness and clamped to 0..100, and the illness gains `trunc(excitement / RideVomitDivisor) · trunc((100 - byte(trunc(hunger))) / 20)` (signed, clamped to 0..100): 35 for excitement 70 at hunger 0,
7 at hunger 80, 0 from hunger 81. The former C# had inclusive bands (a difference of 5, 15 or 40 counted one band better), a penalty of `SmallHappinessChange` beyond 40, no hunger factor, an illness meter that decayed and was reset with a
Medium happiness loss at `VomitCapacity`, and read the static `ExcitementLevel`. (`0xea318` is called from the shop and use routine `0xeaaf8`, after the F18 effects fragment's start.)

*What changes for a player.* A ride next to a guest who prefers its excitement scores 27 (the former rule gave, for an `Info.AttractionValue` of 40, 65 plus a random 0..9; first counterexample, plan 65 against 27);
bought less than 8 days ago it scores 135, an attraction of the type the guest just used 0, one it used one, two or three rides before the last a quarter, a third or a half, and one it was turned away from a fifth for about 20 s. A ride 3 columns away (9 squares) loses its queue weight and scores
19 instead of 27; at a squared distance of 8 a queue of 6 in 3 cells gives 22. A guest riding at excitement 70 with a preference of 65 gains `GoodRide` (the former rule gave `PerfectRide`) and a bored or scared guest no longer loses
happiness from the ride itself (a refused join costs no happiness either: tp.exe `0x4ffbc0` changes happiness only on a stuck walk or a closed park; GUEST-004).

*Pins.* `attraction-score` 593 inputs (592 differ from the plan; first counterexample: the shipped weights, a perfect ride, original 27 against 65), `ride-outcome` 561 (498; original `{happiness 75, illness 30}` against `{75, 6}`),
`attraction-choice` 515 (99; the score 9 is not a candidate in the original). Mutation checks: `450 → 449`, `<= → <` of the new-ride test, the good band `<= 15` and the swapped tie parity are caught.

*Not changed / open.* The decision cadence (C# decides every 3 s; the original every 30+ turns in state 6 behind a draw modulo 3, with the idle interlude and the happiness loss of an empty decision), the minor decision, the
thing-list order and the counts-test `0xdc834` conditions (ARRIVAL-003, GUEST-002), the environment grid and its component 4 (GUEST-001, retired: only fireworks write it), price eligibility (GUEST-003), the shop and toilet effects on the meters (GUEST-005, F18) and the thought
selection `0xe8c58` (single needs above 99, both hunger and thirst above 90, happiness above 99 or below 10 and a litter ratio: thresholds still 70 in C#) stay approximations. The Windows tp.exe was not compared.

**F25, the guests' needs and starting values (GUESTS.md "Needs", `NeedsUpdate`, `GuestStart`, `UseEffects`; fixed in C#).** The lead for this finding was another fork's static
notes on the Windows tp.exe ("Q85": the constructor stores 50.0 in happiness, a draw modulo 50 in thirst and hunger, a draw modulo 30 in toilet, 0 in illness and litter, and a prankery byte
`100 + id % 3`); they are credited as the lead and nothing was taken from them. The Mac routines are executed here: `guest-needs-turn` runs the whole needs function `0xeece4` over one guest and one
park turn, `guest-constructor` runs the constructor `0xe7644` up to the prankery byte (`0xe7958`). The routine readings agree on every input, every part is needed
(`test_guest_needs_readings_are_sharp`: 9 variants of the needs function, 3 of the constructor), and C# agrees after the change (before it had no seam).

*The needs function (`0xeece4`, called for every guest in every state each park turn, before its state handler).* After the speed update `0xe6b28`, **everything below runs only when
the low two bits of the world turn (game `+0x1da70c`) equal those of the guest's 16-bit thing id** (`0xeed24..0xeed40`; the rest of the function, the queue-link check and the history
decay, is outside the gate):

1. the exit counter `+444` loses 1 (a signed word, no floor);
2. three signed 16-bit words of the guest's cell (`0xd7310`: map `+0x1b1104`, ten bytes per cell, id `1 + x + (y << 7)`) are added to happiness (`+412`, word `+0`), illness (`+432`, `+2`) and hunger (`+424`, `+4`), each
   result clamped to 0..100 in single precision. They are written by the region effects (entertainers, vomit, toilets, guards: PPC-guests, Phase 2); OpenTPW has none (GUEST-006);
3. on a turn that is also a multiple of 16 (`0xeeebc`), toilet gains 1.0, then hunger 2.0, then thirst 2.0 (floats at TOC -10812 and -10828), clamped. A multiple of 16 has its low two bits 0, so only a guest
   whose id is a multiple of four ever gets this: **three in four guests have no periodic growth at all** (the needs of phase 1..3 guests change only through the cell words and what they use). A
   guest of phase 0 gains 2 hunger and thirst every 16 turns (3.97 s, 0.5 per second) and 1 toilet;
4. each of illness, hunger, thirst and toilet (in that order) whose truncated low byte is **exactly** 100 costs 1.0 happiness (TOC -10832), clamped;
5. the toilet speed term `+194` becomes 25 when the toilet byte is above 80, else 0 (`TOC -27224`, `0xef0bc..0xef0e8`);
6. outside the states 16 and 17, when the cell predicate `0xe6c6c` holds, one draw of the world generator modulo 10 may pick a thought (`0xe8c58`) and the thought countdown `+532` runs (`0xeb544` clears the bubble
   at 0); `0xfb508` updates the thought object. Not modelled (GUEST-006).

The tail of every call runs `0xefd84` (the guest is in the queue states 11..14, or in state 8 coming from one), asserts the queue links `+552/+554` clear otherwise, and calls `0xea2b8`, which on a turn that is a
multiple of 20 shifts a 0 into the temporary history (F24).

*The unit of the exit counter.* One count per needs phase is one per four park turns, 4 × 248 ms = **0.992 s**, whatever the guest: the `.sam` comment "starting value for the ExitLevel counter, in SECONDS" is
right, the counter ticks in every state (also while queueing, riding or leaving), and `test_guest_exit_counter_counts_one_per_four_park_turns` executes 400 consecutive turns (it loses exactly 100). OpenTPW's
former exit timer counted real seconds, only while walking, and paused while riding.

*The constructor (`0xe7644`).* It takes eight draws from the world generator `0x105328` (the absolute value of the state, `det-world-lcg`) in this order and every meter starts as follows (readings agree on
517/517, `guest-constructor`):

| Draw | Use |
| --- | --- |
| 1 | the exit counter: `PeepInfo.ExitLevel` (word `+12`) plus the signed remainder of the draw by `2 · ExitLevelVar` (`+16`) minus `ExitLevelVar`: 60..179 for 120 ± 60 |
| 2 | the type: the draw modulo the number of `PeepTypes` rows (`+404`), stored in the byte `+496` |
| 3 | the cash `StartingCash · (100 + r − StartingCashVarPc) / 100` with `r` the draw modulo `2 · StartingCashVarPc + 1` (`+20`), at least 0 |
| 4, 5 | thirst and hunger: the draw modulo 50 (unsigned), as floats |
| 6 | toilet: the draw modulo 30 |
| 7 | dropped |
| 8 | the prankery byte `+448`: `100 + id % 3` when the draw modulo 100 is below `PrankeryLikelihood` (`+156`), else 0 (GUEST-007) |

Happiness (`+412`) is **50.0** (TOC -10864), illness and litter 0, the creation turn is stored at `+468`. A zero `ExitLevelVar` or type count would divide by zero (the oracle reports `UNDEFINED`; C# adds nothing for a zero variation).
OpenTPW draws these words from its own guest stream (DET-014), not the world generator: the distribution is the same, the stream is not.

*Use effects (the guest side of F18).* The four meter updates of the sale routine (thirst and hunger minus the type's effect, illness and happiness plus it, each clamped) are now applied to the guest
(`UseEffects`), after the outcome of F24; `shop-sale-litter` compares them 526/526. A shop that used to empty a need now lowers it by its effect: a guest at hunger 80 visiting a shop with a hunger effect of 50 goes to 30, not to 0.

*What changes for a player.* New guests arrive with thirst and hunger of 0..49, toilet 0..29, happiness 50 (not 60) and 60..179 counts of exit counter (the former timer: 120 ± 60 real seconds, paused while riding); the
counter loses one per second in every state. Needs no longer grow for every guest at a fixed rate (hunger +0.35/s, thirst +0.45/s, toilet +0.3/s): a quarter of the guests gain 0.5, 0.5 and 0.25 per second and
the rest nothing, so within a typical stay of about two minutes most guests never get hungry (a phase-0 guest that starts at 25 reaches 75 after 100 s); a guest whose need sits at exactly 100 loses a happiness point per second
for each such need. A thirsty guest at 90 who reaches a drinks shop with a thirst effect of 100 is emptied; illness no longer decays by itself (the former -0.2 per second) and no longer resets at 100 with a happiness
penalty. First counterexamples: `guest-needs-turn`, id 4 on turn 0, needs 10 and counter 120: original thirst 12, hunger 12, toilet 11, counter 119, the former rule thirst 10.11 and counter 119.752;
`guest-constructor`, world state 0: type 2, money 528, thirst 12, hunger 47, toilet 4, counter 109 and happiness 50, against type 7, money 522, needs 0, counter 122 and happiness 60.

*Not changed / open.* The environment words of the cells (GUEST-006), the thought pick and its thresholds (since modelled from tp.exe `0x4fc8a0`, GUEST-006; the Mac `0xe8c58` takes hunger and thirst both above 90, a single need above 99 or happiness above 99
or below 10), the prankery byte (GUEST-007), the energy meter (since matched to tp.exe's unnamed float +0x1B8: 0 at creation, lowered only by a completed use's FatigueEffect, `0x4fd970`, and read only by the save; GUEST-008 retired), the guest's thing id against OpenTPW's guest id for the phase (WALK-010), the litter meter and the toilet branch of the use routine
(GUEST-005). The Windows executables were not compared (the x86 oracle was not run for this finding).

**F26, the guest lifecycle constants (GUESTS.md "Decision", `GuestLifecycle`, `GuestSimulation.StartLeaving`; fixed in C#).** Three of OpenTPW's unsourced numbers are settled by the Mac
binary: the starting happiness, the leaving test and the unit of the exit counter. `guest-exit-test` executes the leaving test of the state-6 handler `0xebbd0` (fragment `0xebdfc`, with the
closing getter `0x1091ac`); the starting happiness and the counter's unit come from the constructor and needs targets of F25 (`guest-constructor`, `guest-needs-turn`,
`test_guest_exit_counter_counts_one_per_four_park_turns`). The routine reading agrees on all 513 inputs and every part is needed (`test_guest_exit_test_reading_is_sharp`: 4 variants).

- **Starting happiness is 50.0** (the float at TOC -10864, stored at `+412` by the constructor `0xe7644`, F25), not OpenTPW's 60.
- **The exit counter** `+444` (`mExitLevel`, saved as such at `0xe8048`) is set by the constructor to `ExitLevel + (draw mod 2 · ExitLevelVar) - ExitLevelVar` (60..179 with the shipped 120 ± 60), loses 1 on every needs phase of the
  guest (every fourth park turn: 4 × 248 ms = **0.992 s**, so "SECONDS" in the `.sam` comment is right), in every state, and runs negative after 0. Its only other writers are the constructor and the zeroing at three entries
  of state 18 (`0xecacc`, `0xecbbc`, `0xecc3c`: the ticket booth and the unhappy exits); nothing adds to it, so **a ride does not extend a visit** (the former C# added the happiness gain, an invention).
- **The leaving test** (state 6 only, `0xebdfc..0xebe30`): the guest leaves when the happiness meter truncated and taken as a byte is 0 (happiness below 1: **not "at most 10"**), **or the counter is exactly 0** (`cmpwi 0; beq`:
  the counter passes 0 once, for four park turns, and a guest that is in another state then, queueing or riding for example, keeps a negative counter and never leaves by it), **or the park is closed**
  (`0x1091ac` reads game `+0x1da710`, the world variable `mParkClosed`; `0x108ef8` sets it). It then loses `BigHappinessChange` (`0xeb6f4(guest, 2)`) and goes to state 18. The state-18 handler `0xedc98` lets a
  guest whose counter is above 0, happiness byte nonzero, park open and cell predicate true turn back to state 6.
  Other leaving routes (the ticket booth when the fee cannot be paid, the unhappy exits of states 4 and others, the queue handler of F16/F15) are separate and unchanged.
- Observation for the happiness meter: nothing in this routine tests "at most 10". Ten is the queue handler's idle/leave threshold (`0xed58c`, "below 10 leaves"), which OpenTPW already has as `LeaveHappiness`.

*What changes for a player.* Guests no longer leave because their happiness fell to 10 or 5 or 3: they carry on until it is below 1, and a guest that is unhappy but under its counter wanders (it still loses happiness to the
needs penalties and to an empty decision). A guest leaves at the moment its counter reads 0 if it is walking around then; one that is in a queue or on a ride at that moment never leaves by the counter (about half of a guest's
time in the former model). Closing the park (the economy's open/closed switch) now sends every guest that is walking around home, each losing `BigHappinessChange`. Riding no longer extends a visit. First counterexample:
happiness 1.0 and a counter of 120: the original stays, the former rule leaves (`happiness <= 10`); 181 of 513 inputs differ.

*Not changed / open.* OpenTPW had no state 6 or 7 separately: the test ran on every park turn for guests that were walking around or doing nothing (GUEST-010), more often than the original's dwell in state 6. Since the
WANDER lane (section 7.14, X18) it runs in state 6 only, as in the original;
the state-18 recall is implemented (the leaving handler's opening test, `0xedc98`: counter above 0, happiness byte nonzero, park open — back to deciding, so a closed-park dismissal unwinds when the park reopens), but its cell
predicate `0xe6c6c` (the guest's cell record through `0x10a9b0` and `0x85190..0x851d0`) is untraced, so OpenTPW approximates it by the state boundary: only a guest still walking the paths to the exit recalls, and the road ends
it — the booth refusal enters on a lane and is exempt, like the original's zeroed counter `0xecacc` (GUEST-012);
what the original does with queued or riding guests at `mParkClosed` is not traced — OpenTPW also pulls guests out of queues and drops `GoingToRide` reservations on closure, or a closed park would strand every guest
whose ride holds admission (no queue state reads the closing word, and a queued guest's happiness rarely sinks below the queue's leave level); guests on a ride finish it (GUEST-011);
the decision cadence (F24); the happiness a guest on a lane or at the booth carries; and the reading that a negative counter never leaves is an observation of the instruction (`beq`), not of a played game.

**F27, ride breakdown, repair and reopening (ECON-049, ECON-024, ECON-023, QUEUE-019; `RideOpening`, `RideBreakdownRules`, `RideRepair`, `ParkGate.MayOpen`).**
Four targets execute the routines of a ride's life (`ride-may-open`, `ride-breakdown-update`, `ride-set-state`, `ride-repair`). All four agree with the
routine reading on every input, and each part of the reading is needed (`test_ride_readings_are_sharp`: 16 variants). The ride's state word `+408` takes
the values 0 idle (the admission runs), 1 broken down, 2 upgrading, 3 not an attraction (the build routine `0xdad24` / `0xdad34` sets 0 for objects with type
`+46` bit 0x4 and 3 for the others) and 4 condemned. What the original does, in the order a ride lives through it:

1. *Breakdown check, `0xe077c` (`ride-breakdown-update`).* A ride in state 3 or 4 is not updated at all. Otherwise, **except in state 1**, with the open
   word `+100` not zero and the world turn a multiple of 8, the wear step `0xdec2c` runs and then: a ride that is not a relief object (`+46` bit 0x1) and whose
   VAR_BREAKSTAT (variable 4) is 0 breaks down when its life gauge `+68` or its wear gauge `+64`, truncated to a byte, is 0. `0xe09f4` ("requested breakdown")
   writes VAR_BREAKSTAT = 1; when it was the wear gauge that ran out, the life gauge first loses 5 (single precision, clamped to 0..100). There is no random roll.
   Last, a relief object whose wear gauge byte is below 25 gets VAR_WORN = 1 (state 1 still runs this test; 3 and 4 do not). So **a closed ride (open word 0)
   neither wears nor breaks down**, state 2 (which clears the open word) neither, and a mechanic at work on a ride in state 0 (a repair of a worn ride) does
   not stop the update.
2. *Noticing it, `0xe1864` (QUEUE-019, F15).* In state 0 the ride update runs the admission `0xe1404` **first**, then reads VAR_BROKEN (variable 7) and enters
   state 1, or 4 when the life gauge byte is 0.
3. *Entering a state, `0xe0c3c` (`ride-set-state`).* State 0 and 3 only store the word. States 1, 2 and 4 run the head-guest step once (F15), clear the
   open word `+100` and the pending guest `+104` (the one the admission has just called), write **VAR_RIDECLOSED = 1** (variable 6) and send the effect 1;
   other values assert and are stored. So the open word and VAR_RIDECLOSED always move together: every writer of one writes the other (`SetState`,
   close `0xde704`, open `0xde7e4`, maintenance on / off `0xdf674`, upgrade start / end `0xdf928`, repair `0xdef2c`). VAR_BROKEN is the script's own variable: the host
   only reads it (`0xe1864`, `0xdfed4` for "> 0", signed).
4. *Who reopens a ride.* A ride leaves state 1, 2 or 4 only through a `SetState(0)` caller: the open routine `0xde7e4` (the park door's walk `0x108f40`, the open
   buttons and `0xdd6b8`, each behind the guard `0xde654`), maintenance off (`0xdf674`), an upgrade's end (`0xdf928`) and the repair. The guard **`0xde654`**
   (`ride-may-open`, PC `FUN_004df290`) refuses state 1 (broken down), 4 (condemned) and 2 (upgrading), the maintenance flag `+96`, a back of the queue that no
   longer joins a path (`0xdd744`, not executed) and a coaster whose circuit is not complete (`0xe2fe4` and `0x45eac`). It never reads VAR_BROKEN: the old rule
   (`ParkGate.MayOpen`: VAR_BROKEN == 0) differs on 264 of 517 inputs, in both directions (a ride in state 1 whose script variable is back to 0 stays closed; a
   state-0 ride with VAR_BROKEN ≠ 0 is not refused by the guard).
5. *What a repair restores, `0xdef2c` (`ride-repair`; the upgrade branch for state 2 is not executed).* The wear gauge `+64` is written **100.0** (the float at TOC −11016,
   checked in the binary: the "fixed full-condition constant" of the static trace, ECON-024 settled), VAR_WORN is cleared, the mechanic reservation `+90` and the
   maintenance flag `+96` are cleared, VAR_BREAKSTAT is read and cleared (VAR_BROKEN is read and not used), and then the ride is **opened whatever the guard says**:
   open word 1, VAR_RIDECLOSED 0, `SetState(0)`. The guard inlined before it still sees the old state, so the repair of a ride in state 1 or 4 logs the warning "opening
   non-openable ride (non-fatal, can ignore)" five times and opens it anyway. A coaster without a complete circuit is closed again at the end (open word 0, pending guest
   cleared, VAR_RIDECLOSED 1). The life gauge `+68` is not touched: a condemned ride (life gauge 0) is idle after its repair and breaks down again at its next ride
   update. The mechanic job `0xd9e04` (static) clears VAR_BREAKSTAT for a ride in state 1 and **waits** while VAR_BROKEN > 0 (the script must clear it first), and repairs a ride
   in any other state at once; the repair of a worn ride that the player had closed reopens it.

**Fixed in C#** (before → after: `ride-may-open` 264 → 0, `ride-breakdown-update` 280 → 0, `ride-repair` 423 → 0 on the compared fields). `RideOpening.MayOpen` is the guard of
`ParkGate.MayOpen`: the state is the ride's one state word (`RideStateWord`, shared by the economy record and the visitor bridge: 1 or 4 written by the ride update that read VAR_BROKEN, cleared by every opener since round 9), a purchased upgrade not yet installed (2) and the coaster's circuit;
`Level` gives the gate the economy record of each object, and the HUD's open button asks the same guard. `ParkEconomy.UpdateRides` applies `RideBreakdownRules`: a broken or
upgrading or closed ride is skipped (it kept wearing, and a closed ride with an empty gauge broke down in a closed park), a mechanic at work on a ride in state 0 does not stop it.
`ParkEconomy.FinishJob` writes `RideRepair.FullGauge`, and `Level.OpenAfterRepair` opens the ride on `RideRepaired` (a coaster with an incomplete circuit stays closed).
`RideVisitorBridge` runs the admission on the turn it first sees VAR_BROKEN and then `EnterBrokenState` (head dismissed, pending visitor withdrawn, VAR_RIDECLOSED = 1), so the ride
counts as closed from then on. Examples a player would notice: the door opens in the morning while the Totem is broken down in the economy: it stays closed (it reopened before)
and opens when the mechanic has finished; a ride the player closed and that a mechanic then services opens again; a ride with an empty life gauge stays whole overnight with the park
closed and breaks at the first update after the door opens (it broke at night before); a ride under a mechanic's preventive repair keeps wearing for the job's duration.
`ECON-049` keeps only the maintenance button and the queue link test, `ECON-023` the relief objects, the derived state word and a breakdown only the script reports.
After the review of PR #64, `Level` also closes a ride when its upgrade is bought and reopens it when the upgrade is installed (`0xdf928` start and end, read statically: SetState(2)
and SetState(0)), and the door and the reopenings follow the economy an in-place load restores. The economy's breakdown still does not reach the ride script (below).

Still open: the upgrade branch of `0xdef2c` (state 2: the level, the open word and the queue), `0xdd744` and the maintenance flag's player action (`0xdf674` callers `0x1452a8`, `0x147174`:
UI code not traced), what the script does with VAR_BREAKSTAT (the scripts set VAR_BROKEN themselves: OpenTPW's economy does not write VAR_BREAKSTAT, so a ride that breaks down in the
economy does not close or stop for guests, `RideBrokeDown` stays an economy event), and state 4 in the bridge. The F19 repair duration and mechanic arrival (`0xd9c0c`) are not part of this finding.

**F28, what a staff member costs (ECON-012, ECON-043; `ParkEconomy.Fire`, `BalanceSettings.GetMonthlyWage`).** The static trace of round 3 (APPROX-TRACE "refuted → not_found") said the
original charges the bank when a staff member is hired. The Mac binary does not. What it charges is found by executing the two routines that debit the staff wage, and by showing that the hire path has no debit:

1. *The wage, `0xf46bc` (ECON-043, executed inside both targets).* The thing's class byte `+2` (4 mechanic, 5 handyman, 6 entertainer, 7 guard, 8 researcher) selects `PerTypeStaffConsts.PayMultiplier`
   (balance `+832 + 4 · type`) and the grade `+484` selects `PerGradeStaffConsts.BaseWage` (`+748 + 16 · grade`); the wage is their **32-bit** product (`mullw`: 65,536 × 65,536 wraps to 0). The C# product was 64-bit;
   `BalanceSettings.GetMonthlyWage` now wraps too.
2. *The month's end, `0xf3364` (`staff-wage-month`).* The staff thing's message handler `0xf2b5c` calls it on the bank's monthly message 12 (the same message that runs the loan step, F13). It debits the wage with
   the bank's Spend `0xcbfdc` (balance, year profit, the park's cost statistic) and adds it to the staff wages statistic at park `+0x1f7f0`, also when the bank's flag `+276` stops the debit (executed,
   511/511). `ParkEconomy.EndMonth` already did this (the plan and C# agree).
3. *Dismissal, `0xf33ec` (`staff-dismiss-charge`).* The routine the UI's dismiss button reaches (`0x16e0cc`, UI-MAP: "the dismissal routine 0x100f33ec", behind the confirmation box UITEXT 397) debits **one more wage** with
   the same two steps before it plays the effect `0xe66b0` and removes the thing `0xfad64`. There is no affordability check. The fragment from `0xf3518` is executed (the first part, which queues commands, is not): 511/511 agree
   with the reading, and "dismissing is free" (`ParkEconomy.Fire` before) differs on 506.
4. *Hiring (not found).* The hire button's handler (`0x151d5c`) calls the candidate routine `0xf61d0`, which builds a 28-byte command with `0x6bfa0` (type 5, 4, 6, 7 or 8 for the roles in StaffType order, with the candidate's
   bytes and word) and queues it; the command executor `0x6c034` creates the thing through the factory `0xb882c`, and the completion `0x6c1f4` removes the candidate from the pool (`0xf6b58`). Three tests pin what shows no
   charge: the bank's Spend `0xcbfdc` has exactly sixteen direct callers and three of them are in the staff code (the wage, the dismissal and the month-end training `0xf3588`; no data word holds the routine's address,
   so there is no indirect call), none of those functions is on the hire path, and staff code contains no inline update of a bank balance (a `lwz` / `stw` pair at `+12`). The keys `StaffPoolInfo.BaseCostPerStaff` (balance `+700`)
   and `CostPerQualityLevel` (`+704`) are never loaded from the settings object (the scan finds the pool timing words `+708` / `+712` as its control). So hiring is free in the Mac binary; the two
   keys are dead data.

**Fixed in C#** (`ParkEconomy.Fire` posts the employee's monthly wage as a staff cost and raises `StaffFired` with it; `GetMonthlyWage` is a 32-bit product). Examples a player would notice: a grade-2 mechanic costs 2,100 (multiplier 3 ×
base wage 700) to dismiss in the test balance and nothing to hire, and a dismissed employee is not paid at the month's end any more; before, a dismissal was free. **ECON-012 is retired**: free hiring is the Mac behaviour (static
evidence on the hire path, executed evidence on the charges). Not executed: the hire path itself (the thing factory and staff constructors are reached through indirect calls), the confirmation dialog and the removal of the thing.

**F29, the park rating and the park value (ECON-027, ECON-026; `ParkEconomy.ParkRating`, `ParkEconomy.ParkValue`, `ParkValueRules`).** Two routines the static trace had read only in part are executed.

1. *The rating, `0xc7b24` (`park-rating`).* It walks the global thing list eleven times and adds eleven capped counts, without a final normalisation (the maximum is 100). A count below its cap is
   recounted by a helper that runs the same loop, so the inline count and the recount agree (the lane's "stable snapshot"). Guests: the things of class 1 whose cell (thing `+4`, `+6` bytes, the
   cell `y << 7 + x + 1`) has the type 0, 1, 3, 9 or 10 (`0xe6c6c`; **the census**, the same predicate as the park door's guest census), cut at 1,000, times 20 / 1000 truncated. Objects (class 3) by the
   type record's `+1960` word, **`Info.WhichUIType`**: rides (0) × 3 / 2 up to 20, shops (1) × 2 up to 10, sideshows (2) × 2 up to 10, features (3) up to 10, and rides whose level byte `+76` is at least 2, up to 10. Staff of the
   classes 5, 4, 6, 7 and 8 (handyman, mechanic, entertainer, guard, researcher) with the **byte `+3` clear**, up to 4 each. 519/519 inputs agree with the reading, and each part is needed
   (`test_park_rating_reading_is_sharp`: census, both caps, UI type, level 2, the byte, the staff cap). The previous rule (the object's directory, guests in the park by state, every hired staff member) differs on 249 of 519.
   *What a player would notice:* the three add-ons the shipped data has per theme (the karts' jumps and tunnels, the racers' teleport: `Info.WhichUIType` 0 in the `upgrades` directory) count as rides
   (three of them are worth 4 points; before, nothing), and every shipped object's UI type is its directory's otherwise (a test over all 4 themes), so the sub-kind mapping of ECON-027 is settled. C# now counts by
   `EconomyObjectInfo.RatingType` (the data's `Info.WhichUIType`). Still approximate: the visitor term counts guests in the park by state where the original counts the census (the imported grid lacks the outside
   cell types, ECON-050: its unpreserved cells are type 0, so applying the census would count guests that still stand outside the gate), and C# has no staff byte `+3` (its writers are not traced).
2. *The value, `0xc1d4c` (`park-value`).* The routine returns, and caches in `0xc9020` (valid within the same four-turn window; the monthly roll-over pushes it into the Park value series), the sum of two loops.
   **Cells:** over all 128 × 128 cells without the unowned flag `0x40` in the u16 at cell `+14`: 2 points for type 0, 3 for type 1, 5 for type 3 or 9 (and 1 more for 9), 1 for type 2, 1 for type 10, and 50
   for a cell whose record (the 40-byte records after the cell array; for a type 12 or 17 record the record its link `+16` names) has a type in 11, 13, 16, 18 or 25. A predicate that would add 10 never holds. **Objects:** per object that is not in
   state 4, the costs of its levels 0 up to the current one (`Upgrades[n].CostOfUpgrade`, a 32-bit sum) times the state of repair truncated to a byte (a 32-bit product), divided by 100 toward zero. The bodies of both loops are executed per
   input (526/526 agree) and the whole routine once on a map (an empty map of owned cells is worth 2 × 16,384 = 32,768; `test_park_value_whole_function`). The old rule (the sum of the scrap values: the costs times a percentage by the object's age,
   no map, no repair factor) differs on 355. *What a player would notice:* the park value follows the state of repair, a condemned ride adds nothing, a new ride is worth its whole cost (no scrap percentage), and a ride at level 2 counts all three levels.
   C# now has the object part (`ParkValueRules`); the cell term is not added because the cell map has no ownership flag (PATH-007): ECON-026 keeps that. The finance graph's Park value line (`0x1014FE1C`...) reads the series
   +0x1F350, which `0x100C163C` fills with this value every month (APPROX-TRACE), so OpenTPW's monthly sample is the object part until the cell part has data.

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
| `tpw-exe-coast-rating-step` | tp.exe `0x43dc50`..`0x43df8f` (cdecl; reset `0x43dbf0`; the Mac `0x415b4`) | none (the routine calls nothing); static state block `0x7923a8`..`0x7923dc` written before the call | 509 (the Mac target's inputs) | **X11**: 469 differ from the Mac routine (F7, the stale term = Fz, what C# follows); 229 from COAST-plan 6.3 (term 0). With the Windows term (u) in the Mac plan: 190 left, rounding only (at most 3 ulps) | **agree** 509/509 (x87 reading, 53-bit sums) |
| `tpw-exe-coast-rating-final` | tp.exe `0x43df90`..`0x43e0a7` (cdecl; the Mac `0x41900`) | none | 506 | **X12**: 23 differ from the Mac routine (F8, what C# follows), all in the last bit of the excitement; 315 from COAST-plan 6.4 (section count). **Agree** 506/506 at the single-precision word `0x7F` | **agree** 506/506 |
| `tpi-exe-coast-rating-step` | TPI `Game.exe` `0x480630`..`0x4808ff` (cdecl; reset `0x4803a0`) | none; static block `0xa191f0`..`0xa19224` | 509 | **X11**: 470 differ from the Mac routine; 217 from COAST-plan 6.3; 181 left with the Windows term (rounding, at most 3 ulps) | **agree** 509/509 (Game.exe's own rounding points) |
| `tpi-exe-coast-rating-final` | TPI `Game.exe` `0x4809c0`..`0x480ac9` (cdecl) | none | 506 | **X12**: 30 differ from the Mac routine, all in the excitement (last bit; more ulps only where the two terms nearly cancel); 271 from COAST-plan 6.4. Identical to tp.exe's final on all 506 | **agree** 506/506 |
| `tpw-exe-counts-test` | tp.exe `0x4dd920`..`0x4dda1f` (thiscall, `ecx` = thing; the Mac `0xdc834`) | `0x4de130` back of queue (chosen, checked for the thing), `0x4ddf50` list length (checked to be asked for guest 0), `0x412e90` type record (track type chosen), `0x441970` coaster controller test (cdecl, chosen); stack balance checked | 515 (15 edges, one per condition, 500 random) | **X15**: **agree** 515/515 with the routine reading | the C# rule transcribed (`CountsForParkPoints` with `QueueCountsForParkPoints`): 29 differ, all coasters, karts and water rides (ARRIVAL-003); for every other track type **agree**. The back cell and list length are stubbed, so this checks the routine's logic, not OpenTPW's queue geometry |
| `tpw-exe-toilet-wear` | tp.exe `0x4e2440`..`0x4e2646` (thiscall, `ecx` = the relief object, argument the guest's toilet byte), offline game type, and `_ftol` | `0x4d8460` / `0x4d8440` dirty-toilet messages (recorded with their cell), `0x5da3c0` log (counted); the GameType object marked constructed; stack balance checked; x87 control word `0x27F` | 528 (28 edges around gauge 25 and 0, 500 random) | **X17**: **agree** 528/528 (the stored single, the messages and the log); with a double −0.05 one input differs in the last bit | `ParkEconomy.UseRelief`, unit-tested (`ToiletTests`) |
| `tpw-exe-price-test` | tp.exe `0x4fde50`..`0x4fe181` (thiscall, `ecx` = guest, argument the ride), the price, cost-of-goods and win-byte getters and `_ftol` | `0x4dd4e0` type record (synthetic), `0x4e2740` park type record (word `+0xc` chosen), `0x519510` park, `0x4c74b0` statistic (recorded); stack balance checked; x87 control word `0x27F` | 530 (30 boundary inputs, among them meters that round to the next integer as float32; 500 random) | **X16**: **agree** 530/530 with the reading, decision and price statistics; without the money test 56 differ | `QueueJoinRules.TooExpensive` / `WillingnessToPay`: unit-tested on seven willingness values the routine produced (`PriceTestTests`) |
| `tpw-exe-guest-state6` | tp.exe `0x4fec90`..`0x4ff4d7` (thiscall, `ecx` = guest; its SEH frame through `fs`), `_ftol` and the debug print | the draw (scripted), `0x4fc800` activity, `0x4d93b0` litter, `0x50c100` event, `0x4faa00` sound, `0x4fea70` happiness change, `0x51a280` closed word, `0x4d86d0` gate cells, `0x4fa530` gate route, `0x501020` / `0x4c8eb0` presentation look-ups (none), `0x4fcb10` choice (`+0x1dc` chosen), `0x4f9490` wander (chosen), `0x500dc0` bin search (`+0x1dc` chosen), `0x501db0` state (all recorded and checked for their object); stack balance checked; x87 control word `0x27F` | 526 (78 edges at the cheering, vomiting, litter-meter, prank and 30-turn boundaries, 500 random) | **X18**: **agree** 578/578 with WANDER-plan §2 as `GuestDecision` runs it, each branch's original effects included (the litter step since §7.15, the pranks since §7.16) | (the plan is the routine reading) |
| `tpw-exe-wander-destination` | tp.exe `0x4f9490`..`0x4f9e3d` (thiscall, `ecx` = guest; SEH frame), the cell readers `0x522770` / `0x522810` / `0x522850` / `0x536310`..`0x53635b`, the map bounds test `0x4d8300`, the counter `0x4d8c50` | the draw (scripted), `0x4fa670`, `0x510100` route request (on the steering state `+0xd4`; always found, its point recorded), `0x50be80` stranded animation, `0x50b350` thing id; the staff calls `0x506ed0` / `0x506f30` and the stranded test `0x4fa770` must not run. Synthetic cell records (`[0x8023a0]`, type `+8`, links `+0xc`, queue link `+0xd`) and the direction table as `0x4d9a65` writes it | 527 (7 corridors, 20 sparse grids with unlinked or queue-only starts, 500 random 7 × 7 patches, some links one-sided) | **X19**: **agree** 527/527 with `GuestWander.Pick`: cell, sub-cell bytes and the number of draws | a step that also follows a neighbour's link back (OpenTPW's `AreConnected`): 164 differ |
| `tpw-exe-litter-drop` | tp.exe `0x4d93b0`..`0x4d95d0` (thiscall, `ecx` = cell record, argument the type), the world generator step `0x516330` and reseed `0x516370`, `_ftol` | `0x475a10` sprite (descriptor and bank recorded), `0x475480` placement (x, z recorded), `0x4d8440` RegionFX add, `0x521e60` particle; the GameType object marked constructed, word 0 from the input | 526 (26 edges: every type on three cells, an occupied cell, an online park; 500 random) | **X20**: **agree** 526/526 with `ParkLitter.Drop` and `SpriteOffset` (type, collector, sprite offset, the generator's state after the reseed) | (the plan is the routine reading) |
| `tpw-exe-bin-walk` | tp.exe `0x4fff20`..`0x4fffe4` (thiscall, `ecx` = guest) | the walk step `0x4fa2a0` (0, 1 or 2 chosen), the bin sound `0x55a070` / `0x55a0b0`, the draw, the litter drop `0x4d93b0` (type and cell recorded), `0x501db0` state | 518 (18 edges, 500 random) | **X21**: **agree** 518/518 with the state-9 handler of `GuestSimulation` | (the plan is the routine reading) |
| `tpw-exe-region-effect` | tp.exe `0x4d8480`..`0x4d8607` (thiscall, `ecx` = cell manager; add flag, effect index, cell id) | none; a synthetic cell manager with the effect table at `+0x1d9104` and the grid at `+0x1b1104` | 520 (random tables up to radius 9, words near the 16-bit limits, cells at the map's corners) | **X22**: **agree** 520/520 with `ParkEnvironment.Apply` (the 19 × 19 window around the source) | (the plan is the routine reading) |
| `tpw-exe-guard-choice` | tp.exe `0x5099c0`..`0x509ade` (thiscall, `ecx` = the receiving thing; out, prankster cell id) and the state test `0x4d6330` | none but the debug print; synthetic guard things in the thing table `0x7cfb90` behind game word `+0x1da744` | 520 (up to six guards in all states, equal distances, the receiver at (0, 0) and elsewhere) | **X24**: **agree** 520/520 with `StaffWorldSimulation.ChooseGuard`; measuring from the prankster or giving a tie to the later guard disagrees | (the plan is the routine reading) |
| `tpw-exe-litter-finder` | tp.exe `0x4c8ed0`..`0x4c9033` (thiscall, `ecx` = park; out, collector id, cell id, range) | none but the debug print; synthetic cell records at `[0x8023a0]` and the park's scratch buffer at `+0x1f020` | 520 (ranges 0..5, claims by others and by him, cells at the map's corners) | **X23**: **agree** 520/520 with `StaffWorldSimulation.FindLitter` | (the plan is the routine reading) |
| `tpw-exe-walk-edge-test`, `tpw-exe-walk-commit` | tp.exe edge test `0x4d8750` and step commit `0x510510` (cdecl), with the cell readers `0x522770` / `0x522850` / `0x536300`..`0x53645f`, the object test `0x5363f0` through `0x4d0af0` and `0x53ad20`, the bounds test `0x4d8300` | none but the debug print; a synthetic 128 × 128 map at `[0x7cf6ec]` (cells `0x44` bytes from `+0x294`, id = y × 128 + x + 1; type `+8`, link `+0xc`, queue link `+0xd`, flags `+0xe`), no objects | 501 maps each (park-like windows of path corridors with queue, walkway and other cells, random links and queue links, the `0x400` flag; the map's corners), 12 edges or 12 steps of up to two cells per map, masks 0, 1 and 2 | **X25**: **agree** 501/501 with `WalkEdges.Blocked` and `WalkEdges.Commit`; a reading that ignores the mask differs on 226 | (the plan is the routine reading, `walk_reading.py`) |
| `tpw-exe-walk-search` | tp.exe `0x511420` and its line search `0x511470`, tracers `0x511c90`, shortcuts `0x5110d0`, refinement `0x5108a0`, the edge test and commit above, the library `memmove` `0x67bb90` | none but the debug print; the same synthetic maps | 500 (start on a path cell, goal on a path cell or anywhere in the window; 120 fail) | **X25**: **agree** 500/500 with `WalkSearch.Find` (the waypoint list, or the failure `0x70000000`); ignoring the mask, 220 differ | (the plan is the routine reading) |
| `tpw-exe-walk-avoid-walls` | tp.exe avoid_walls `0x50e4a0` (out, walk; `ret 8`) with the ray test `0x50cc20`, the fixed-point multiply `0x50df70`, divide `0x50e080`, length and `_ftol` | none but the debug print; the same maps, a walk object with radius `+4`, position `+8`, velocity `+0x10`, mask `+0xb4` | 500 (a quarter beside a queue cell that cuts a corner, positions near cell edges, zero velocities) | **X25**: **agree** 500/500 with `GuestNavigation.AvoidWallsOriginal` (the force) | (the plan is the routine reading) |
| `tpw-exe-walk-steering` | tp.exe set-up `0x50f8e0`, steering step `0x50f3b0` (truncate `0x50f600`, follow_path `0x50ed10`, avoid_walls, separation `0x50e0e0`), progress `0x50fd40`, the block stamps `0x4d8c50` / `0x4d8cd0` | the debug print, and separation's neighbour query `0x5102f0` (no neighbours); the behaviour list (avoid_walls `0xe666`, follow_path `0x8000`, separation `0x1999`) written as the registration `0x50fdf0` leaves it; the block stamps at game `+0x2d8 + 0x1b0000` | 40 whole walks of 80 steering steps (92 set-ups, 23 map edits that stamp their block; 499 arrived, 1746 walking and 1047 stuck results) | **X25**: **agree** 40/40 with `GuestNavigation.Navigate` / `StepOriginal`: every walk field the step reads after each set-up and step, the buffered centres, and the walk step's result (`0x4fa2a0`) | (the plan is the routine reading) |
| `tpw-exe-tqi-matrix` | tp.exe `0x6710c0`..`0x671191` (cdecl: matrix, quantiser) and `_ftol` `0x67a830` | none | every quantiser 0..107 | **X26**: **agree** 108/108 with `TqiDecoder.BuildMatrix` | (the plan is the routine reading) |
| `tpw-exe-tqi-transform-pass` | tp.exe first transform pass `0x67673c`..`0x67687a` (register call: `esi` input row, `edi` output column, stride `0x24`) | none | 500 coefficient rows (\|x\| < 2^26, zero-shortcut rows included), leaving out rows whose odd-part products end within 0.1 of a half | **X26**: **agree** with `TqiDecoder.Butterfly` away from half-way odd-part roundings (those rows are left out, so tie rounding rests on the IEEE semantics of `fadd`/`fstp`, not on this target); the decoder `0x6747d0` over 51 sampled frames of all nine movies matched in all but one sample, a half-way odd-part rounding that Unicorn's x87 rounds away from zero (about one in twenty such cases, which no IEEE precision does) | (the plan is the routine reading) |
| `tpw-exe-terrain-ramp` | tp.exe `0x55e710`..`0x55e77f` (cdecl: table, centre, width) | none | every pair of the edge values (0, 1, 2, 3, 127, 128, 129, 253, 254, 255) and 500 random pairs | **X27**: **agree** 600/600 with the ramp formula (0 below `c - (w + 1) / 2`, 255 above `c + (w + 1) / 2`, `255 * (i - low) / (w + 1)` between) | (the plan is the routine reading) |
| `tpw-exe-terrain-weights` | tp.exe `0x55e780`..`0x55e974` (cdecl: grid record, cell x, file row, weight 1..3, plane record) | none | 500 weight planes at N = 16, 32, 64 on constant, checkerboard, ramp and random 13 x 13 grids | **X27**: **agree** 500/500 with the 16.16 forward differencing of `ProceduralTerrainTexturer` | (the plan is the routine reading) |
| `tpw-exe-terrain-build` | tp.exe `0x55e710`..`0x560fff` (thiscall, `ecx` = the level object; destination, cell x, cell z, floor descriptor NULL), with the ramp, weight plane and the two and three layer blends | none; a synthetic level object (parameters, 13 x 13 weight grid, 13 images of the set), the ramp tables from the original's own `0x55e710`, the weight plane records and the build buffer set up as the loader `0x560ff0` does | 48 cells of synthetic levels (sizes 16, 32 and 64), one layer mix after the other (C, B, A, BA, CB, CA, CBA, random masks) and invalid cells | **X27**: **agree** 48/48 with the Python reading `terrain_reading.py` that `ProceduralTerrainTexturer` follows; a reading without the three layer quirk differs on 12 of 48 | (the plan is the routine reading) |
| `tpw-exe-terrain-compose` | tp.exe `0x55e710`..`0x560fff` (thiscall, `ecx` = the level object; destination, cell x, cell z and a floor descriptor `{pixels, width, height, bytes per pixel, word}`): the composite tail from `0x5608b0` | none; the level object of `tpw-exe-terrain-build`, the descriptor and the art in sandbox memory | 56 cells of synthetic levels with random art: all eight orientation words and words with other bits (`0x281`, `0x18`, `0x30`, `0x58`, `0x78`), 32-bit art and 4444 art, art sizes that are, are not and are smaller than multiples of N, alpha from 0 to 255 | **X33**: **agree** 56/56 with the Python reading `terrain_reading.compose` that `ProceduralTerrainTexturer.Compose` follows; readings that test the orientation bits in another order or blend with a division by 255 differ | (the plan is the routine reading) |
| `tpw-exe-skirt-mask` | tp.exe `0x52c420` (cdecl: shape; the footprint edge mask at `0x8186f0`) | none; a synthetic shape object (width, height, 8 byte cells at `+8 + (row * 20 + column) * 8`, zero behind them) | 510 (10 filled blocks up to 20 x 20, 500 random shapes up to 20 x 20 over the nine cell types of the 19 symbols, width 20 and height 20 in every seventh and eleventh) | **X28**: **agree** 510/510 with the package F plan (section 3.1), including the mask bytes the wide shapes write behind the 16 x 16 mask (over the stored width and height words) | `skirt_reading.mask` (the plan's reading) |
| `tpw-exe-skirt-uv` | tp.exe `0x467310` (cdecl: model, node, eps, mask, vertices, `&logged`) with modf `0x67b280` (and `_controlfp` `0x686080`, the `frndint` wrapper `0x6855b0`) and `_ftol` `0x67a830`, executed | `0x45b9d0` allocator (zeroed block, size 0 gives NULL) and `0x45ba00` free replaced by sandbox versions (they only call GlobalAlloc / GlobalFree); `0x5da3c0` (a bare `ret` in this build) recorded; x87 control word `0x7f` | 500 one node meshes: floors over 1 to 12 cells, UVs near integers (noise of 0.0199 to 0.0201, 0.9799 to 0.9801, 0.98), negative, shared and separate corners, lifted cells (y 0.09, 0.11, 3), skewed faces, dense, sparse and no mask, slot words with and without bit 0x8000, a few other eps | **X29**: **agree** 500/500 with `skirt_reading.uv_normalise` (snap, per face shift, wrap bits 0x30 cleared, log flag) | (the plan is the routine reading) |
| `tpw-exe-skirt-flags` | tp.exe `0x46aa10` (cdecl: object, shape, width, height, header 0x80), with the mask `0x52c420`, the node transform `0x4628b0` / `0x4700f0`, `0x4679d0` and the UV normaliser `0x467310`, `_ftol`, modf, `__chkstk` `0x67b110`, executed | the allocator, free and print as above; synthetic object, base record (`+4` model, `+0x18` entries, `+0x2c` ground node), model (`+0x50` slot table, `+0x78` root), node (counts, vertex blocks, UV blocks, groups, faces, corner map, matrix, parents), world `[0x7a0854] + 0x6c` landscape cell sizes, scratch vertex array `[0x7aecd4]` | 400 floors over the footprint of random shapes up to 6 x 6, axis aligned and skewed, UV squares rotated and mirrored, node matrices (identity, quarter turn with a parent, general), header bit 0x80 on and off, shared corners and slots, lifted cells, extra faces outside the footprint, vertical walls, three cell sizes | **X30**: **agree** 400/400 with `skirt_reading.flags`: the entries, the slot words (0x800 and 0x30), the normalised UVs, the base record's first dword, the flipped counts and the two messages | the 53 bit reading (`x87-53bit`, control words `0x27f` / `0x37f`) differs on 31 of 400 |
| `tpw-exe-ground-conform` | tp.exe `0x469940` (cdecl: instance, world, base height, flags) and `0x469a80` apply mode with `0x4702d0`, `0x46fc00`, the inverse `0x470040` (LU `0x46fe20`, `0x46fd40`), identity `0x46f150`, modf and `_ftol`, executed | none; synthetic instance (`+4` instance model with `+0x9c` template, `+0x78` root, `+0x30` flags, `+0x44` node count 0; `+8` base record with the vertex list at `+0x14`), template and instance trees (flags, sibling `+8`, child `+0xc`, matrix `+0x10`, vertices `+0x60`, bounds `+0x78`), world `+0x6c` landscape (origin, cell sizes, counts, corner heights `+0x28`, cell flag bytes `+0x2c`) | 400 parks: heightfields of 2 x 2 to 7 x 7 cells with random corners, flat ones included, cell sizes 10 and others, origins off zero; roots at the four quarter turns with whole and fractional placement; ground nodes with 0x1000 lists (on, near and off the grid lines, past the last corner, left of the origin), a second list node, 0x200 and plain nodes, drape nodes (all four triangle cases), lists that end early or start late | **X31**: **agree** 400/400 with `skirt_reading.conform` (every conformed vertex and widened bound as float bits, every node's flags) | `x87-53bit` differs on 104 of 400, `exact-inverse` (a plain double inverse rounded to a float) on 62 |
| `tpw-exe-base-height` | tp.exe `0x466d70` (cdecl: box x, box z, shape, width, height, flags, rotation; result in ST0) with the corner mapper `0x454050`, `0x469930`, `_ftol`, and the box origin `0x466f90` | none; synthetic landscape (counts at `+0x18` / `+0x1c`, corner heights at `+0x28`) behind `[0x7a0854] + 0x6c`, synthetic shape | 500 (shapes up to 8 x 8, heights up to 14 x 14 cells, the four rotations, the footprint kept on the map, shape NULL and flag 0x10 variants) | **X32**: **agree** 500/500 with `skirt_reading.base_height` | **agree** 500/500 with the rule `ParkObjects.GetBaseHeight` states (`skirt_reading.base_height_csharp`): the four corners of every cell with a non-zero type nibble put on the grid by `ObjectFootprint.ToGrid`, midpoint of the lowest and highest height |
| `tpw-exe-terrain-spans` | tp.exe `0x56b940`..`0x56dd5f` (the ray, hull, clip and rasteriser), `0x578d20`..`0x578fef` (ray rotation), `__ftol` (thiscall, `ecx` = the landscape object; the camera, field of view, aspect word and maximum distance in the globals `0x87af58`, `0x8bcbcc`, `0x8bd4c0` and the camera pointer `0x87afe8`) | `0x578ca0` (the farthest corner distance, logged), `0x67bb90` (the hull insertion move) | the camera, heights, rectangle and cell size of a synthetic landscape; the single precision control word of DDSCL_FPUSETUP | 406 (random cameras with a yaw, a pitch from 10 degrees up to straight down, a roll, a mirrored matrix, a field of view, an aspect, heights, a maximum distance and a field; scenes for a vanishing width, a ray with no height, a hull outside the field and rays beyond the half distance) | **X27**: **agree** 406/406 with the Python reading `terrain_spans.py` that `TerrainVisibleSpans` follows operation by operation (rows, the 128 start and 128 end bytes, the farthest corner distance); every hull branch is reached | (the plan is the routine reading) |
| `tpw-exe-terrain-cache` | tp.exe `0x572440`..`0x57262a` (thiscall, `ecx` = the cache: level, N, the 128 x 128 key table, the head node at `+0x1000c`; the key array, the cell records of 0x14 bytes and the count) | `0x55f780` (the build; the cell it builds is logged), `0x590d40`, `0x590da0`, `0x590f90`, `0x58c9f0`, `0x58dc50` (page and device calls) | a ring of 3 to 12 nodes as `0x572110` links them, the texture mode that takes the byte-order path | 100 random sequences of 3 to 16 frames of up to the capacity plus 3 cells (a drifting window, some hits marked dirty) | **X27**: **agree** 100/100 (and 300/300 on a second seed) with `terrain_cache.py` that `ProceduralTextureCache` follows: the slot of each key, the cells built, the ring from the head and the key table after every frame | (the plan is the routine reading) |
| `tpw-exe-model-drape` | tp.exe `0x469940`..`0x469a75` (the instance wrapper), `0x469a80`..`0x46a549` (the node walk), the chain `0x4702d0`, the point transform `0x46fc00`, the inverse `0x470040` with `0x46fe20` / `0x46fd40`, the identity `0x46f150`, `modf` `0x67b280` and `_ftol` `0x67a830` with the helpers they call, all executed | none | 400 (synthetic parks) | **agree** 400/400 (24-bit reading, node space bit for bit); the 53 and 64-bit control words differ on 268 of 400 |
| `tpw-exe-model-placement` | tp.exe `0x467030`..`0x4671ec` (the root placement), `0x46f650` (the yaw into the root's own matrix), the sine table fill `0x4708d0` (N = 4096, run first) and `_ftol` `0x67a830` | none | 200 (the four yaws, root matrices of the identity and the signed permutations, cells, heights) | **agree** 200/200 (matrix bits; the table cosine of 90 degrees is -8.74e-8) | |
| `tpw-exe-queue-reevaluate` | tp.exe `0x501390`..`0x5014d1` (thiscall, `ecx` = guest; the Mac `0xee8f8`) | `0x50b350` guest id, `0x4ddf50` queue position (chosen), `0x4754e0` table read (outputs unused), `0x50be80` animation, `0x4ddd20` unlink, `0x5012f0` dismissal, `0x501db0` state (each checked for its object), `0x5da3c0` log; the whole call sequence, the ride word and the stack balance are checked | 548 (48 edges around 4 × cells for states 11 to 14, 500 random; guests in the queue list) | **X14**: **agree** 548/548 with the routine reading and with the leave-for-room rule of `GuestSimulation.ReevaluateQueue`; without the state-14 exemption 70 differ | (the plan is the routine reading) |
| `tpw-exe-scrap-value` | tp.exe `0x4e2400` (thiscall, `ecx` = thing; the Mac `0xe25e8`), the percentage `0x4e2290` (the Mac `0xe2424`), the clock stamp `0x4f8690` with the stamp add / subtract `0x5fc530` / `0x5fc550`, the 64-bit divide and multiply helpers | `0x412e90` type record (registry and type id checked), `0x5fc660` FileTimeToSystemTime wrapper (the eight field pointers null and the status pointer set, checked; the status 0 is written to a slot the caller does not read) | 572 (the Mac target's inputs, each with a clock base of 0, below 2^32 or anywhere in 64 bits) | **X13**: **agree** 572/572 with the Mac routine reading (F21, what C# follows) | readings with all levels' costs, without the first-month refund or with the base level's row differ on 221, 57 and 181 |

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
### 7.8 Findings X11 and X12: the coaster rating on tp.exe and on the Theme Park Inc Game.exe

(Numbers X7 to X10 are the Theme Park Inc arrival, loan and park-points findings of section 7.7.)

Question: do the Windows executables run the Mac coaster rating that C# follows (`CoasterRating.Step` and
`FinalFor`: the step `0x415b4` with the stale term Fz, F7, and the final `0x41900` with the both-flags section count, F8)? A
Theme Park Inc mode is a coaster game, so this is the rule that matters most for it. Method as in 7.6: find the routines by
constants, record layout and call structure (static, Capstone in a scratch directory, terminal only), execute them with the
inputs of the Mac targets (`oracle/targets.py`: plans, inputs and equality are imported, not copied) and compare. Addresses are virtual
addresses at the preferred base.

**Identification (high confidence for both executables).** The step, the reset and the final sit next to each other, as on the Mac
(Mac reset `0x41510`, step `0x415b4`, final `0x41900`):

| | tp.exe (TPW v2.0) | Game.exe (TPI no-CD) |
| --- | --- | --- |
| reset | `0x43dbf0` | `0x4803a0` |
| step (per sample) | `0x43dc50` | `0x480630` |
| final | `0x43df90` | `0x4809c0` |
| static state block (S, E, peaks, previous force and bank, scream words) | `0x7923a8`..`0x7923dc` | `0xa191f0`..`0xa19224` |

- The step reads exactly the Mac's definition offsets in the Mac's roles: `+0x32c` g-force sickness, `+0x330` upside-down
  sickness, `+0x334` speed, `+0x338` g-force excitement, `+0x340` bank excitement, `+0x344` X-g uncomfortableness, `+0x348` inverted
  uncomfortableness, `+0x34c`/`+0x350` normal and terror scream start, `+0x354`/`+0x358` their sustain; `+0x33c` (the
  unread upside-down excitement weight) is not touched. The reset reads `+0x320`/`+0x324` (initial sickness and excitement) for S, E and the previous
  excitement and zeroes the rest.
- The constants are the Mac's: pi/2 and 0.1 and 3.0 as singles, the decay 1/2 (in tp.exe as a negated half), 1.7, 100.0 and 0.0
  in the final, and the 1.0 of `u = U + (U != 0)`.
- The controller layout is the Mac's: definition `+4`, samples `+0x9c` (56-byte records, flags at sample `+0`, forces `+32..+40`, bank
  `+24`, speed `+28` as arguments), the section list head `+0x54` with next at `+0x64` and flags at `+8`, and the final's
  results at `+0x100` sickness, `+0x104` excitement, `+0x108` counted sections, `+0x10c` length (`+0xa8` times `+0xac`),
  `+0x110` max Y, `+0x114` max speed. Two getters read the result fields by controller id (tp.exe `0x43e0b0` and `0x43e0e0`).
- Call structure (the rating pass, tp.exe near `0x43c4bb`; Game.exe near `0x47e605`): the reset once, then the step for samples 1 to n-2
  with the speed, bank and force just computed, then the final with the maximum Y and speed. In each executable the static block is
  referenced only by the reset, the step and the final (scan of absolute operands), so the previous force and bank words are read
  by the step, zero from the reset and never written: the Mac's OV-7 situation, and "magnitude equals change" holds on the PC.

**X11, the rating step differs in one rule and in rounding.** Executed on the Mac target's 509 inputs; both executables give the same
rule, so the numbers are for both.

- *The rule.* When the inverted arm does not apply (u = 0, or Fy is 0.1 or more) the Mac adds the stale register f0 = Fz to the
  uncomfortableness (F7). The Windows step adds **u itself**, the single stored for 1 + U (U the part of |bank| beyond pi/2; 0 when
  U is 0), in both executables. Where the inverted arm applies (u not 0 and Fy below 0.1: u times the inverted weight, tripled for
  negative Fy) the rules agree. So an ordinary upright sample (u = 0) loses nothing on the PC where the Mac subtracts Fz, and a track
  that is upside down while pitched upright (Fy at least 0.1) loses u (above 1) where the Mac loses Fz. Neither equals the term 0
  of COAST-plan 6.3. Example, default weights (0.5, 3.0, 0.1, 1.0, 5.0, 1.0, 2.0), force (0.4, 1.3, 0.5), speed 20, bank 0.2, E = 1:
  Windows E = 5.3 (executed on both), Mac 4.8; with force (0.4, 0.5, 0.5) and bank 3.0: Windows 16.0708, Mac 18.0; with
  Fy = 0.05 or -0.5 (inverted arm): both 13.19 and 3.92. S is unaffected (S 1.1 / 7.9876 in all of these). Of the 509 inputs 445 (tp.exe) and 439
  (Game.exe) take the non-inverted arm, and u differs from Fz in all but one of them; 469 and 470 inputs differ from C# in S or E or in what follows (peaks, scream
  flags and states: 25 flag words differ on tp.exe).
- *Rounding (precision only).* With the Windows term put into the Mac plan 190 (tp.exe) and 181 (Game.exe) inputs are left, all within 3
  ulps in S, E and the peaks and with no scream decision changed on these inputs. The x87 builds the sums in a different order
  (g-force part, then bank, then speed) in the wider register and rounds to single at different points than the Mac's single-by-single
  chain, and the two Windows builds round differently from each other: tp.exe rounds the g-force sickness term to single and never the
  excitement sum, Game.exe rounds the whole sickness delta and the excitement sum to single and not the g-force sickness term. On the same
  inputs 89 of 509 step results differ between tp.exe and Game.exe (last bits of S and E), the final not at all. Control words `0x27F` and `0x37F` give the same results
  on every input; the 24-bit word `0x7F` changes only the rounding. These are routine-reading differences, not rule differences, and each
  reading agrees with its own executable on 509/509.

**X12, the final is the Mac's rule with a wider sum.** Sickness = clamp(S + peak S), excitement = clamp(1/2 E + 1.7 peak E), 0 to 100, the section count at `+0x108` of
sections with **both** flags `0x40` and `0x80` (F8), the length `+0xa8 * +0xac`, max Y and max speed stored unchanged. All of
it is identical to the Mac routine and to C# except the excitement's last bit: the x87 adds the exact product 1.7 * peak E to E/2 and rounds once, the Mac rounds the
product to single before its fused add. 23 of 506 (tp.exe) and 30 of 506 (Game.exe) differ, by 1 ulp
where the result is large (7.6e-6 at most in absolute value); the Game.exe maximum of 78 ulps is a near-cancellation (result 0.092, off by 5.8e-7). At the
single-precision word `0x7F` all 506 agree. Example: S 30, peak S 40, E 20, peak E 30 gives sickness 70 and excitement 61 on both executables and on the Mac. No third rating (intensity) is stored by
either final: TPW's excitement and sickness are what the Windows executables compute, uncomfortableness folded into excitement. The
documented COAST-plan 6.4 count (neither flag set) differs from both executables, 315 and 271 inputs, as it does from the Mac (F8).

**What a Theme Park Inc coaster mode would need.** The rating step and final exist in TPI with the same record layout and definition offsets
as TPW, so the machinery C# already has (`CoasterRating.Step` and `FinalFor`, the 56-byte samples, the definition fields) carries over; no TPI-only rating
term was found. Two decisions follow. First, which step rule: C# follows the Mac (Fz); both Windows executables add u, so a mode that
reproduces the Windows games (and a TPW PC mode) must change that one term in the non-inverted arm, which does not exist as a decision yet and changes the
excitement of every sample with a non-zero Fz. Second, precision: the Windows builds round at different points and differ from each other
and from the Mac's single arithmetic in the last bits of S and E (the final's excitement too); a "bit-exact" claim for one executable
needs that executable's rounding points (the readings in `x86_targets.py`), and the two Windows builds differ from each other. Not examined: the
rest of the rating pass (the speed model and force computation that feed the step; the Windows counterpart of `0x3fc18`), the defaults of the
definition weights in TPI (its coaster definition files and loader at `0x46d6bc`/`0x48044e` carry the same offsets but their values were not read),
the TPI build rules and curve fit, any TPI-specific rating (for example a separate intensity or an effect of the new coaster types), and which control
word is live while the game runs. No C# behaviour was changed.

### 7.9 Finding X13: the sale refund in tp.exe is the Mac rule

Question: does the Windows build refund a sold object by the Mac rule that C# follows (`ParkEconomy.ScrapValue` /
`ScrapPercent`, the Mac `0xe25e8` / `0xe2424`, F21)? ECON-025 asked for exactly this execution.

**Identification (high confidence).** The two 64-bit divisors of the Mac percentage occur together once in tp.exe: 365 days in
100 ns units (`0x11ED1:0x78C6C000`) only at `0x4e22ce`, and 30 days (`0x1792:0xF8648000`) right after it, both inside
`0x4e2290`. Its one value-returning caller with the price is `0x4e2400`; the sale `0x4dd0a0` computes the same product inline
and credits it. The routines were located with REA (a Ghidra MCP front end, run locally on the identified tp.exe; Ghidra's
output is read, not committed) and then executed here.

**Executed.** `0x4e2400`, `0x4e2290`, the clock stamp `0x4f8690` (clock `+0` plus turn × rate / 4 × 10^7, the Mac `0xe4394`
with its epoch), the 64-bit add / subtract `0x5fc530` / `0x5fc550` and the divide and multiply helpers. Stubs: the type record
lookup `0x412e90` (synthetic record, registry and type id checked) and `0x5fc660`, which converts the stamp with
FileTimeToSystemTime into eight field pointers (all null here, checked) and writes a status word into one of `0x4f8690`'s own
argument slots, which nothing reads; the stub writes the success status 0 there. The clock base is random (0, below 2^32, anywhere in
64 bits) and stored in both stamps, so the test also shows that it cancels.

**Result: identical.** All 572 inputs (the Mac target's edges and random inputs) agree with the Mac routine reading:

1. the refund is the percentage × record `+0x1b8` (the Mac `+440`, `Upgrades[0].CostOfUpgrade`), a 32-bit product divided by
   100 unsigned (`0x51EB851F`, shift 5);
2. the age is the signed 64-bit difference of the clock stamp and thing `+0x18` (the Mac `+20`); the low word of its quotient
   by 365 days picks the column (0, 1, 2, else the fourth);
3. the row is record `+0x188 + 64 · level + 4 · column` (the Mac `+392`), the level byte at thing `+0x50` (the Mac `+76`);
4. in the first year, the low word of the quotient by 30 days is 0, the thing's word `+0x1a0` (the Mac `+412`) is 0 and record
   `+0x188` is positive give **100**.

The three wrong readings in the target (every level's cost summed, no first-month refund, the base level's row) differ on 221, 57 and 181 inputs.

**The use counter.** Thing `+0x1a0` is cleared, with `+0x19c..+0x1a8`, by the object constructor `0x4db090`, and the only
increment the scan below finds is `0x4e1690`, which raises it together with `+0x1a8` (the Mac `0xe1b44` raises `+412` with the history word `+420`).
`0x4e1690` has one direct caller, `0x4fd970`, the guest's leave handler (it records the object in the guest's history, counts the visit by
kind and logs "Person lost this sideshow"), with `ecx` the object. So the counter counts guests who left, not boardings, as on the Mac.
The other stores to `+0x1a0` belong to other structures: the guest constructor `0x4faec0` (happiness 50.0 at `+0x19c`, a computed
word at `+0x1a0`) and library code from `0x6460c0` on (`0x646ee0` stores an allocated block there). This part is static: a byte scan
for register-plus-`0x1a0` stores (`mov`, `inc`, `add 1`, `mov 0`) and a read of each hit, not an execution; other encodings were not scanned.

ECON-025 is retired; C# was already the rule. Not examined: the TPI `Game.exe` counterpart, and the save field that fills the counter on load
(SAVE-006).

### 7.10 Finding X14: the queue re-evaluation has one exit

Question (QUEUE-005): when a queue is edited, which guests leave? C# followed the Mac plan. The plan read two exits from the strings of
`0xee8f8`: "queue was shortened" and "queue edited underneath me" (a guest whose standing cell left the queue).

**Identification (high confidence).** In tp.exe the queue edit `0x4de1f0` logs "Object's queue is now %d cells long" and "Telling people
in queue to reevaluate". It then walks the ride's queue list (`+0x3c`, next guest `+0x228`) and calls `0x501390` for every guest but the
called one (ride `+0x6c`). `0x501390` carries both strings.

**Executed: one exit.** `0x501390` first logs "Person %d: Queue edited underneath me while Standing In Queue (I'm in state %d)" for every
guest it is called for, and nothing depends on it. It then asks the ride for the guest's position (`0x4ddf50`, the list walk) and
compares it, unsigned, with 4 × the ride's queue cells (`+0x40`). Only when the position is at or beyond that room and the guest is not in
state 14 (waiting to be taken) does it leave, in this order:
1. the log "The queue was shortened and there's no room for me any more";
2. animation 13 for a guest whose id is a multiple of 3;
3. the unlink `0x4ddd20`;
4. the dismissal `0x5012f0` (which logs "dismissed from queue", records event 6 for the ride, plays a sound for one guest in eight,
   lowers happiness by the medium step through `0x4fea70` and clears the queue words; C# applies the happiness drop since X16's review);
5. ride word `+0x1dc` = 0 and state 6.

Every other guest is left as it is; there is no exit for a guest whose cell left the queue. Across 548 inputs (states 11 to 14, positions
around 4 × cells) the routine agrees with that reading; without the state-14 exemption 70 differ.

**C#.** `GuestSimulation.ReevaluateQueue` sends away a non-called guest in state 13 beyond the room too, as the original does; state 14
is still never visited, and the leave-for-room rule agrees on all inputs. Two differences remain, now named by QUEUE-005. First, the
original changes nothing for a guest that stays. C# keeps its own recovery: a guest whose cell left the queue leaves (Confused), because
OpenTPW's queue walk cannot route it back from a removed cell, and the guests that stay are given their walk again. Second, the departure's
effects differ: C# sets the QueueTooLong thought and plays no animation.

Also seen: after re-evaluating, `0x4de1f0` reopens a ride that is closed but openable (not broken or condemned, back of queue connected,
and for coasters, karts and water rides their extra conditions); this is not compared here.

### 7.11 Finding X15: the park points counts-test, queue included

Question (ARRIVAL-003): which attractions count for park points (and so for arrivals)? C# applied only three of the counts-test's
conditions: IsChoosable, not broken down, and open. The Mac trace named the rest but OpenTPW had no state for them.

**Executed.** tp.exe `0x4dd920` (the Mac `0xdc834`, same tests at the PC offsets) returns 1 only when all of these hold:
1. flag word `+0x32` bit 4 (IsChoosable);
2. state `+0x19c` is not 1 (broken down) and not 4 (condemned);
3. the open word `+0x68` is nonzero;
4. the back of the queue `0x4de130` gives a cell. That routine walks the queue from the front cell at the entrance and counts its cells
   into `+0x40`, so an attraction without queue cells has no back cell;
5. `0x4ddf50`, asked for guest 0, walks the whole queue list and returns its length, which must be below 4 × the cells (unsigned);
6. by track type: a coaster (3) also needs the controller test `0x441970`, and a kart (1) or water ride (2) needs the word `+0x2c`.

It agrees with that reading on 515 inputs.

**Fixed in C#.** `QueueJoinRules.QueueCountsForParkPoints` counts the real queue cells among the attraction's queue cells. OpenTPW's
one-cell fallback on a path cell (RIDES-028) does not count, just as the original's back-of-queue walk finds no cell there. The rule then
requires the queue list to be shorter than 4 × those cells. The level, the M3 and M4 gate loops and the prototype ride sync the result
into `ParkObjectState.QueueHasRoom` each tick, and `CountsForParkPoints` requires it. A ride without a queue, or with a full one, no
longer counts. The flag is not saved; it is true until the first sync after a load or a purchase.

**Fixed in C# (track types).** `CountsForParkPoints` now applies condition 6 too: a coaster counts only while
`ParkObjectState.CoasterAcceptsGuests`, which the level syncs from `CoasterRide.CountsForParkPoints` (no controller fails, as the
original's lookup `0x436860` returns 0), holds; the coaster test `0x441970` reads the controller's `+0x140` (its self-intersection
count, which `0x439c40` increments for each crossing of the track with itself; not a rider count) and the `+0x3c` bits complete and
closed. A kart or water ride counts only with `TrackValid`, the circuit walk's verdict; other types ignore both. The park points sum
`0x4c8240` filters by the type's `Info.WhichUIType` (`+0x4ac`) 0 or 2, which C# now reads (`EconomyObjectInfo.RatingType`) instead
of the object kind. Transcribed, the C# rule agrees on every input (`csharp-rule`). A subsequent static inspection of the complete shared helper
`0x441970`–`0x4419af` confirms the edited-controller exclusion (`0x790fec`) and no sample-count or controller-state term.
The economic consumer now uses the flags/intersection/edit predicate without requiring three samples; actual guest admission
retains that host safety guard. The edited-controller term was already wired through `BeingEdited`. This correction is static
evidence plus synthetic coverage, not another native execution. ARRIVAL-003 is retired: the kart/water track word now starts at 1 as the original's constructor stores it (`0x4db0df`; the PC constructor 0x4DB0A0), and only a circuit walk or a track edit's clear rewrites it (0x524D1B, 0x52694F, 0x526BE1, 0x526F13, 0x5251B8, 0x5257FB).

### 7.12 Finding X16: guests test the price when they are called forward

Question (GUEST-003): does the original filter attractions by price? C# let a guest choose only an attraction it could afford.

**Identification.** The choice loop `0x4fcb10` (called from the state-6 handler `0x4fec90`) and the scoring `0x4fcc30` have no money
term. A candidate needs:
- flag `+0x32` bit 4;
- a back-of-queue cell;
- a score above 9 that beats the best so far (ties on an odd turn);
- the counts-test `0x4dd920` and a route.

The price is tested in the state-13 handler `0x5006b0`. When the called guest reaches its stand point, `0x4fde50` runs before the
ride takes the guest (AdmitPerson). If it fails, the guest logs "Object %d is too expensive, I'm leaving the queue" and plays
animation 6. The ride's walk-away count (`mNumWalkAways` `+0x1a4`, through `0x4e1670`) rises, and the guest leaves the queue for
state 6. The ride's words come from the ride serializer `0x4db7d0`: `mCostOfGoods +0x188`, `mChanceOfWinning +0x190` (a byte),
`mPricePerUse +0x194`. The type record's words +320..+336 are the UsageInfo keys InitCostOfGoods, ThirstEffect, HungerEffect,
VomitEffect and HappinessEffect, in the order of tp.exe's key table. +324/+328/+332 were already known from the Mac (PPC-guests).

**Executed.** `0x4fde50` returns 0 for a price of 0. Otherwise, with the guest's happiness h, thirst t, hunger u and illness i taken as
bytes of their truncated floats:

    S = HappinessEffect·(100 − h)/100 + ThirstEffect·t/100 + HungerEffect·u/100 − VomitEffect·i/100 + 100      (signed divisions)
    W = S × (InitCostOfGoods·115/100), unsigned /100
    W = (W + [sideshow] cost of goods × win byte / 100) × (park type word +0xc + 100), unsigned /100
    W = W × (h + 100), unsigned /100

The guest refuses when the price exceeds W or its money (both unsigned). A shop (kind 1) also records min(100, (W + 5)·10 − price·10)
in the price statistic of its product category (`+0x158`: 1..4) and of `+0x15c` (6 or 7). A sideshow (kind 2) records it in
category 9. Across 530 inputs the routine agrees with this reading, statistics included (the meters are float32 words, so a value such as
1.99999999 is read as 2); without the money test 56 differ.

**Fixed in C#.** The choice no longer filters on price (`ChooseAttraction`). At the stand point, `QueueJoinRules.TooExpensive` runs on
the live price, cost of goods and win byte and on the type's InitCostOfGoods and effects; a guest that refuses leaves the queue with its
thought unchanged. `PriceTestTests` pins seven willingness values the routine produced, each at the price that is still paid and the
one that is refused. A check against the shipped data: in all four themes, each shop's InitPricePerUse equals or is within 2 of
the willingness of a guest whose needs are all 20. For example, a burger shop at price 30 gives W = 30, 39 and 48 at needs 20, 50 and 80.
The rule therefore leaves the default prices just acceptable and lets needier guests pay more.

A refusal lowers happiness twice by the medium step (`0x4fea70` with 1, for the refusal and again in the dismissal `0x5012f0`).
The per-type word is not 0: `0x4d3e00` (new-game setup `0x5156a0` through `0x4d3de0`) fills the record `0x4e2740` returns from the
type's `UsageInfo.RipOffOK` (type `+0x16c`), 100 for every shipped shop and 250 for every sideshow, so the willingness above is
multiplied by 2 or 3.5 before the happiness factor; OpenTPW passes it since the GUEST-003 fix (the willingness figures above are the
word-0 values). The ride's walk-away counters (`0x4e1670`, read by the advisor's per-ShopType test `0x4caae0`) and the price
statistic `0x4c74b0` are kept since; GUEST-003 keeps the dismissal's sound; the guest's event records (`0x50c100`, events 10 and 6)
feed only a debug dump.

### 7.13 Finding X17: toilets wear by use, handymen clean them

Question (QUEUE-008): does a toilet's gauge fall, and who restores it? OpenTPW wore only rides, so the worn-toilet exit of the queue
(`0xdfe34`, F15) never fired.

**Executed: the wear step.** The use routine `0x4fe1e0` calls `0x4e2440` for an object with the relief flag (`+0x32` bit 0). It
passes the guest's toilet meter as a byte, then empties the meter and clears an illness byte above 90. In the offline game type,
`0x4e2440` computes `(int64)toilet × −0.05f + gauge` in x87 precision, keeps it within 0..100 and stores it as a single. A gauge byte
that falls below 25 logs "Toilet has become dirty and smelly" and posts messages 1 and 6 for the toilet's cell. Across 528 inputs the
routine agrees with that reading. In the online game type, a toilet that is dirty cleans itself back to 100 at once; that branch is not
compared.

**Read: the cleaning job.** The handyman's state machine is `0x4d73c0`. A free handyman's job search `0x4d7100` takes litter within
`DetectionRange` first. Otherwise `0x4d7880` picks the nearest toilet flagged by the ride's `mRequestedService` (`+0x64`), or one below
25 within `DetectionRange` cells, and walks there (state 10). On arrival it cleans (state 11) until the turn passes its start +
`WorkDuration[grade]`. `0x4dfd80` then sets the gauge back to 100.0 and clears `mRequestedService`. The grade table `0x7853ec` holds
`HandymanConstsPerGrade {WorkDuration, DetectionRange, PoundsPerTrainingPoint}`, in the order of tp.exe's key table.

**C#.** `ParkEconomy.UseRelief` wears the toilet with the same arithmetic (exact in double precision) and raises `ToiletDirty` on the
crossing; `GuestSimulation.ApplyVisit` calls it through `IGuestPayments.ReliefUsed` and clears illness above 90. Every park turn,
`DispatchToiletCleaning` gives each free handyman the dirtiest unclaimed toilet below 25. The handyman is busy for WorkDuration + 1
staff updates, and `FinishJob` sets the gauge back to 100 and raises `ToiletCleaned`. The level and the M3/M4 gate loops mirror the
gauge into the visitor bridge, so the worn-toilet exit now fires.

Handymen have no position (ECON-022), so the walk, the litter-first rule, DetectionRange and the nearest-toilet choice are not
modelled. QUEUE-008 keeps the online branch and the handlers of the two messages.

### 7.14 Findings X18 and X19: the guest's decision state and its wander walk

Question (GUEST-010): when does a guest decide, and where does it walk? OpenTPW decided every 3 s (a seconds timer) and wandered one
random neighbour cell at a time, and it tested the leaving rule for every walking guest on every park turn. The research and the
plan are in docs/reverse/WANDER-plan.md.

**X18, state 6 (`0x4fec90`).** Executed with the callees stubbed and recorded. On 518 inputs the handler does what WANDER-plan §2
describes, in this order, each branch returning after its state change:
1. cheering: activity 5 when the turn is more than 100 after `+0x208` (unsigned) and the happiness byte is above 80;
2. vomiting: activity 7, litter 7, event 18, sound 0xcc and illness 0, when the illness byte is 100 and `R % 3 == 0`;
3. the leaving gate: on happiness byte 0, an exit counter of 0 or the closed word, the big happiness change and then the gate routes
   (both cells, twice); a route sets state 18, none goes on;
4. `R % 3`: 0 → the choice when the turn is above `+0x1fc + 30` (event 2 and state 10, or event 1, activity 4, the small change and
   `+0x1fc` = turn); 1 → the wander (state 7, or `+0x1fc` = turn); 2 → nothing.

The litter step (meter `+0x1b4` at 90 or more), the presentation turns and the pranks are passed over in these inputs (meter and prank
byte 0); WANDER-plan §2 describes them from the instructions.

**X19, the wander destination (`0x4f9490`).** Executed with the cell readers, on synthetic cell records. On 527 inputs the destination
cell, the sub-cell bytes and the number of draws agree with `GuestWander.Pick`: `rand % 5 + 1` steps (one more each time the last
step ends on the start), candidates the current cell's own links in the order 16, 4, 1, 64 (+Y, +X, −Y, −X) — a neighbour's link
back does not count (164 inputs with one-sided links differ when it does) — the first one left when fewer than two remain,
else the first from `rand & 3` that is not the way back, and bytes `rand & 0x7f` clamped to 5..123 (the first quarter of the cell).
A path cell without links walks to its own centre; one whose links lead only to queue cells is stranded (no destination). The
direction table `0x7cdba0..` is filled at start-up by `0x4d9a65`: link 1 is (0, −1), 4 is (+1, 0), 16 is (0, +1), 64 is (−1, 0).

**Two fields the queue already had.** The state-6 stamps are words OpenTPW kept for the queue: `+0x1fc` is the state-11 entry turn
(the state setter `0x501db0` writes it on entering 11; the Mac `+508`, `QueueStandingSinceTurn`), and `+0x208` is the interlude stamp
(`+520`, `InterludeTurn`), which every activity and every entry into state 8 rewrite. The guest constructor zeroes both (`0x4faf75`, `0x4faf87`), so the first major
decision of a guest in a new park waits for turn 31. An excitement refusal in state 10 clears `+0x1fc` (`0x4ffcf6`).

**Fixed in C#.** `GuestSimulation` runs states 6 and 7 once per park turn (`GuestDecision`, `GuestWander`); the activity is state 8
for ten turns (`Guest.Activity`); the seconds timer and the one-cell walk are gone. The leaving test runs in state 6 only, so a guest
that walks through the four turns its counter reads 0 keeps a negative counter and stays. A decision that finds no attraction costs
the small happiness change. The harness gives routines with a frame handler an `fs` segment over a zeroed TIB page.

A walk whose route ahead loses a cell or a link to a grid edit ends where the guest stands, as the original's walk step reports "stuck". A park
without arrival lanes lets its leaving guests vanish, as before.

GUEST-010 keeps:
- the litter step and the vomit's litter (GUEST-005, ECON-020);
- the presentation turns towards shows and performers;
- the pranks (GUEST-007);
- the gate routes, tested as a path to an arrival lane's end;
- the walk's "stuck" result and the off-path search;
- the stranded mark (`+0x198`, animation 0x11).

### 7.15 Findings X20 and X21: litter by cell

Question (ECON-020, GUEST-005): where does litter go? OpenTPW kept a park-wide count that every sale raised by `LitterEffect`/100
and every handyman lowered each park hour. The research and the plan are in docs/reverse/WANDER-plan.md §6.

**The guest's side.** The use routine `0x4fe1e0` adds the type's `LitterEffect` (`+0x154`) to the guest's litter meter `+0x1b4`.
State 6 checks the meter after vomiting and before the leaving gate (X18, now with the litter step): a byte of 90 or more runs the
bin search `0x500dc0` (radius 3, squared cell distance below 9, the nearest reachable bin), and a bin sends the guest to state 9;
without one a second draw drops litter `1 + R2 % 5` on the guest's cell and the meter empties. Vomiting drops type 7 (X18).

**X20, littering a cell (`0x4d93b0`).** Executed with the world generator. On 526 inputs the stored type, the claim, the sprite's
position and the generator's state agree with `ParkLitter.Drop` and `ParkLitter.SpriteOffset`: nothing online or on a littered cell
(one item per cell); otherwise the generator is reseeded with the cell id `y·128 + x + 1` and drawn twice for the sprite's offset
`x·10 + r1 % 9 + 1`, z likewise. Vomit and stinkbomb also add RegionFX 2 and 5 (`0x4d8440`), recorded here; OpenTPW adds them with
the environment grid (phase 4). The reseed is not applied to OpenTPW's own streams (DET-014): the sprite offset is a function of the
cell alone.

**X21, the walk to a bin (`0x4fff20`).** On 518 inputs: walking on changes nothing; arriving at a bin plays its sound; arriving
without a bin or stuck drops `1 + rand % 5` on the cell; then the meter is 0 and the state 6. The original leaves `+0x1dc` (the bin)
in place, where OpenTPW clears its bin id; nothing reads the word before the next choice writes it.

**Fixed in C#.** `ParkLitter` keeps the litter by cell for the economy and its save; `GuestSimulation` fills the meter, runs the
litter step, the bin search and state 9, and drops vomit. A sale no longer adds litter to the park. A free handyman claims the first
unclaimed littered cell in the finder's order (`0x4c8ed0`, x outer and y inner), park-wide because he has no position yet, and
sweeps it for WorkDuration park turns (`0x4d76a0`); litter comes before toilets (`0x4d7100`). The handymen's strike grievance now
reads the littered share of the path and queue cells (`0x509360`).

ECON-020 keeps the generator reseed and the analyser's cache lag; ECON-022 the handymen's positions, walks and DetectionRange;
GUEST-005 the access cell of a bin and a bin removed during the walk.

### 7.16 Findings X18 (pranks) and X22: pranks and the environment grid

Question (GUEST-007, GUEST-006): what do pranksters do, and how do vomit and stinkbombs act on guests? OpenTPW had no prankery flag and
passed 0 for the guest's cell environment words. The research is in docs/reverse/WANDER-plan.md §7.

**The flag.** The constructor's eighth draw (`0x4fb114`): 100 when it is below `PrankeryLikelihood` modulo 100 (`0x7850dc`), plus the thing
id modulo 3. Read in the disassembly; `GuestStart.PrankFlag` transcribes it.

**X18, the prank step (`0x4ff0f5`).** The state-6 target now runs nonzero flags with a synthetic thing list on the guest's cell, the
balloon burst `0x4fe950` and the message bus `0x40fb10` recorded. On 578 inputs (78 edges) the handler agrees with `GuestDecision`: below
a happiness byte of 15 and with the flag above `R % 101`, a stinkbomb (when `R % 100` is below `StinkbombLikelihood`, with sound 0xcf),
litter `R % 5 + 1`, or the first other guest with a balloon; then event 15, the type-14 message `{0x7007a8, 0, id, id % 3}` and happiness
+1 (clamped), and the step falls through to the final choice. The prank test reads happiness after the leaving gate: a gate without a
way out has just taken the big change, which can make an unhappy enough guest prank (the stub of `0x4fea70` applies the change; testing the
earlier value, 2 inputs differ).

**X22, the region writer (`0x4d8480`).** Executed without stubs on a synthetic cell manager. On 520 inputs the window around the source
agrees with `ParkEnvironment.Apply`: the square radius clipped to the map, `sign × coefficient / (|dx| + |dy| + 1)` truncated toward zero,
wrapping 16-bit words. The PC writer is the Mac `0xd73a0` (docs/reverse/PPC-guests.md phase 2) with the same table layout.

**Fixed in C#.** `GuestSettings` loads `PrankeryLikelihood` and `StinkbombLikelihood`; a new guest gets its prankery flag; state 6 runs the
prank step; `ParkLitter` spreads vomit's and a stinkbomb's `RegionFX` on `ParkEnvironment` while they lie, and the needs update reads the
guest's cell words.

GUEST-007 keeps the balloons (no guest carries one), the advisor's prank lines, the guards' chase and escort, and event 15. GUEST-006
keeps the other environment sources (entertainers, toilets, guards, cameras, fireworks) and the thought pick.

### 7.17 Finding X23: the handymen search from where they stand

Question (ECON-022): where do handymen go? OpenTPW's handymen had no position: a free one took the first unclaimed littered cell park-wide
and its job time started at once. The state machine is read in full in docs/reverse/WANDER-plan.md §4.

**X23, the litter finder (`0x4c8ed0`).** Executed without stubs. On 520 inputs the cell it returns agrees with `StaffWorldSimulation.FindLitter`:
the square of DetectionRange cells around the handyman, clipped to the map, the cells with litter claimed by nobody or by him, scanned x
outer and y inner, the smallest Manhattan distance, the first on a tie.

**Fixed in C#.** In a level `StaffWorldSimulation` runs the handyman's update once per park turn: the speed step, the idle litter scan,
the states 0, 1, 8, 9, 10 and 11, the setter's stamps and speed bonus, and the job search in the original order (strike and rest, too
tired, litter, toilets, patrol). He walks with the guests' steering (WALK-I) and patrols with their destination routine, kept to his
patrol rectangle; a sweep or a clean is timed from his arrival. His idle and walking voices come from the same draws. The park save
keeps his state, stamps, speed and walk. A headless economy keeps its own dispatch.

ECON-022 keeps the walks to the rest and strike areas, the 100-turn toilet claim lapse, the player's maintenance request and patrol
rectangle controls, the separation among staff, and a toilet's access cell.

### 7.18 Finding X24: the guard nearest the staff HQ answers a prank

Question (GUEST-007, ECON-022): which guard does a prank call? The prankster's bus message reaches the staff HQ thing, whose handler
`0x508a30` checks the security word of the prankster's cell and asks `0x5099c0` for a guard (docs/reverse/WANDER-plan.md §4).

**X24, the guard choice (`0x5099c0`).** Executed with only the debug print stubbed. On 520 inputs the guard it writes agrees with
`StaffWorldSimulation.ChooseGuard`: in list order, the guards in state 0 or 1, the smallest squared distance from **the receiver's own cell**,
the first on a tie. The prankster's cell, passed as the routine's second argument, is never read. The receiver is the staff HQ, which sits at
cell (0, 0) in the shipped Easymode park and in four played saves, so the free guard nearest the map's corner goes, however far from the
prankster. Two wrong readings fail: measuring from the prankster (56 differ) and a tie to the later guard (65 differ).

**Fixed in C#.** In a level the guards walk and answer pranks (`StaffWorldSimulation.ReportPrank`, the chase, the catch and the escort of
WANDER-plan §4); entertainers patrol and perform. The guard list's order is the hire order (ECON-022).

### 7.19 Finding X25: guests walk with the original's search, cells and steering

Question (WALK-002, WALK-004 .. WALK-009): how does a guest's walk find its way over the park's cells? OpenTPW walked a cell route
from its own grid search, refilled from that route and measured progress in a five-waypoint window (docs/GUESTS.md, "Walking").

**X25, the walk (`0x4d8750`, `0x510510`, `0x511420`, `0x50e4a0`, `0x50f8e0` / `0x50f3b0`).** Five targets execute the routines on
synthetic park-like maps; `tools/ppc-analysis/oracle_x86/walk_reading.py` states them in Python and each agrees with it on every input
(table above), and `WalkSearch` / `GuestNavigation` port the same reading. The edge test is the only cell rule: a step between two path
cells is open whatever their links, a queue boundary needs the target's link back, mask 1 also opens a step from a path cell to ground,
mask 2 refuses the `0x1e` walkway. The search draws a four-connected line to the goal; at a blocked edge two tracers follow the wall, one
turning each way, until one may leave towards the goal; then shortcuts over up to four waypoints and a refinement. A step commits one
cell at most, a diagonal through either L-shaped route. avoid_walls pushes back from the first edge the probe penetrates, else from the
corner of the motion's quadrant whose diagonal cell cuts it. The steering sums the three behaviours with the original's multiply
(rounding towards −∞) and divide (towards 0); a failed search leaves the walk arrived and stuck, and a stamp change in a waypoint's block
makes the walk search again. A reading that ignores the walk's mask differs on 226 edge inputs and 220 searches.

**Fixed in C#.** Guests walking the park's cells use this walk (WALK-002 and WALK-009 retired; WALK-004 .. WALK-008 narrowed, WALK-006 later retired). Its
limits: the oracle maps hold no objects or things (WALK-005), separation runs without neighbours here (WALK-003), and a park resumed from an old save has the link byte of its entrance and exit cells laid at load (`[EXT:legacy-save-access-links]`; WALK-006 retired), so a refused stand-point search is the original's dismissal; walks restored from older saves keep the route walk (`[EXT:legacy-save-route-walk]`).

### 7.20 Finding X27: the procedural terrain texturer is exactly the algorithm in docs/LND.md

Question: does `ProceduralTerrainTexturer` (docs/LND.md) reproduce tp.exe's procedural ground texture builder `0x55f780` byte for byte? Targets `tpw-exe-terrain-ramp`,
`tpw-exe-terrain-weights` and `tpw-exe-terrain-build` (section 7.3) run the original ramp, weight and build routines in the sandbox with no stub, on synthetic levels; the plan is
`oracle_x86/terrain_reading.py`, the same reading as the C# texturer. All three agree on every input (600, 500 and 48). The synthetic cells reach every run mode (C, B, A, BA, CB, CA, CBA) and the grey fallback
(`test_terrain_build_inputs_reach_every_run_mode_and_the_grey_fallback`); a reading in which a three layer run takes weight 1 per texel (the original's `0x55eaa0` never advances that pointer) differs
on 12 of the 48 cells, and one that writes rows top-down differs too (`test_terrain_build_readings_are_sharp`).

Real data: `terrain_golden.py` runs the original build routine on the shipped `base.lnd` of the four themes for a seeded sample (297 cells, sizes 16, 32 and 64, corners and invalid cells included) and
prints the SHA-256 of each result; `ProceduralTerrainCorpusTests` compares the C# texturer with those hashes (with `OPENTPW_GAME_PATH`). All 297 agree. The research behind the port compared the Python
reading with the emulated routine on 33,820 cells (every cell of the four themes at N = 16, 300 at N = 32 and 80 at N = 64 per theme): no mismatch.

Dev-only, as the rest of section 7: no original byte or image is committed, only hashes. The 16-bit pixel paths, the floor-art tail of `0x55f780` (floor descriptor not NULL) and the cache `0x572440` are not run.

### 7.21 Findings X28 to X32: the procedural object base ("skirt") of tp.exe (issue 829, package F)

Question: does tp.exe do what the package F plan reads in its static analysis (the footprint edge mask, the UV normalisation, the skirt entries and opaque slot flags at type load, the
edge vertex conform at instance creation, the base height)? Targets `tpw-exe-skirt-mask`, `tpw-exe-skirt-uv`, `tpw-exe-skirt-flags`, `tpw-exe-ground-conform` and `tpw-exe-base-height`
(section 7.3) run the original routines in the sandbox on synthetic meshes, shapes and heightfields; the plan side is `oracle_x86/skirt_reading.py` (pure Python, `[BIN:TPW-EXE:...]` tags),
the reading the OpenTPW port follows. Every routine agrees with its reading on every input (510, 500, 400, 400 and 500), where the reading is the plan's rule corrected by the findings
below. `skirt_synthetic_golden.py` prints the C# golden vectors (`ObjectSkirtSyntheticGolden.cs`, 38 mask, 40 UV, 42 skirt, 40 conform and 40 base height cases; the reading agreed on all of them).

**Arithmetic.** tp.exe's thread runs the x87 at the control word **0x7f** (24 bit significand): DDSCL_FPUSETUP and the Direct3D immediate-mode device start lower it, and the type load and instance construction run after
the device start (repo evidence: COAST-032, `X87Precision` in `OpenTPW.Files/Formats/Font/X87.cs`). The sandbox targets of this section therefore run at `0x7f` and `skirt_reading.py` rounds EVERY x87 intermediate to a float (`q` is `f32`, a float
operation per step, so a C# `float` transcription in the same operation order is exact). The 53 bit variant (`double_precision()`, the control words `0x27f` and `0x37f`, which give identical results) is kept as the alternative
`x87-53bit` in 7.3: it differs from the original on 31 of 400 skirt inputs and 104 of 400 conform inputs, and never (0 of 300) on an axis aligned floor with an identity matrix and UVs that are multiples of 1/64.
`test_skirt_flags_and_conform_at_other_control_words` pins that the original at `0x27f` / `0x37f` equals the 53 bit reading. The mask, the UV normaliser and the base height give the same results at every control word on these inputs.
The formulas below say "in double" where the x87 keeps an intermediate in a register between two memory stores; at `0x7f` that register is a float, so read each such step as one float operation (operation order as written).

**X28, the edge mask `0x52c420`: as in the plan.** A shape cell of type 4, 9, 10 or `0x17` gets mask[column * 16 + row] = 1 unless all four neighbours (row - 1 >= 0, column - 1 >= 0, column + 1 <= width,
row + 1 <= height) have such a type. The bounds are inclusive, so the right neighbour of the last column and the lower neighbour of the last row are read: a shape of width 20 reads column 20 as column 0 of the next
row (the grid is 20 cells wide, 8 bytes a cell) and a shape of height 20 reads zeros behind the grid. The routine stores the width and height words at `0x8187f0` / `0x8187f4`, directly behind the 16 x 16 mask,
and only then clears the 256 mask bytes, so a shape wider or higher than 16 writes its 1 bytes over those words and on to byte 323; the mask is a static array at `0x8186f0` that the next call overwrites.
The other symbols (`.`, `@`, `Q`, `+`, `#`: types 0, 1, 3, 11, 16) count as no neighbour. The routine returns the mask address.

**X29, the UV normaliser `0x467310`: as in the plan, three precisions to keep.**
1. Snap: the first test compares the exact fraction with the float of -(1 - 0.02f), which is -0.98000002; the other two compare the float of the fraction with +-0.02f (strictly inside: 0.02f itself is not snapped)
   and with the same 0.98000002. So `0.98f` stays and `0.9801` snaps to 1; fractions snap to -1, 0 or 1 and the whole part is the truncation (modf, with the rounding set to chop by
   `_controlfp` for the duration of the call, restored afterwards).
2. Per face shift: the smallest u and v of the three corners are taken from the snapped UVs of ALL faces first (a shift never changes a later face's minimum), the shift is the truncation, one lower when the
   fraction is negative (so -2.0 stays, -1.5 gives -2), and a corner is shifted once, by the first face (in face order) that visits it.
3. Wrap bits: the slot's 0x30 are cleared unless it has 0x8000, when no corner of any face of the group fails "centroid |y| < 0.1 (strict) and the mask cell is 1". The mask index is
   `ftol(cx * 0.1) * 16 - ftol(cz * -0.1)`, equal to x * 16 + z. The centroid is not the same sum on every axis: x is (c + a) + b, y (b + a) + c, z (c + b) + a (a, b, c the face's first, second and third
   corner), each times the float 0.33333334 and stored as a float. An empty group, or a group without a ground face, counts as passing and is cleared.
The out of range UV message appears once, only for corners that pass the mask test (every corner when mask or vertices are NULL). `0x5da3c0` (the debug print) is a bare `ret` in this build and `0x45b9d0` / `0x45ba00` only wrap GlobalAlloc /
GlobalFree, so neither affects a result.

**X30, the skirt entries `0x46aa10`: the plan's rule, with these exact details (the plan was right in every rule; it left the precisions out).**
- Centroid cell (also for the 0x800 decision): x = ftol(((x0 + x1) + x2) * 0.33333334f / cellSizeX), the sum, product and quotient in double (not rounded to a float), z = ftol(float((z2 + float(z0 + z1)) * 0.33333334f) / cellSizeZ):
  z0 + z1 and the product ARE rounded to a float. The cell sizes are the landscape's `+0x10` / `+0x14` (no origin). Slot word: `&= ~0x800` when the header bit 0x80 is clear and mask[x * 16 + z] is 0 (out of range indexes read
  the bytes behind the mask, then zeros), else `|= 0x800`; last face wins.
- Edge directions: d = next - this in double, L = float(sqrt((dx * dx + dy * float(dy)) + dz * dz)), the stored direction (float(float(dx) / L), float(float(dy) / L), float(dz / L)) (dz is not rounded before the divide). The best x edge replaces
  the previous one when its unrounded |dx / L| is strictly greater than the float kept so far, the best z edge when float(|dz / L|) is strictly greater (the first edge wins a tie, except where a quotient rounds down: then the later edge of the same quotient wins).
  The cell record is replaced when 0.9f < mx, 0.9f < mz and (record mx < mx or record mz < mz); records of the footprint start {0, 0, -1, -1}, others (a face outside the footprint, index x + z * 20) are written without being initialised
  and never read; a face whose cell index leaves 0..399 writes on the stack (not modelled).
- The UV normaliser runs between the two passes (on the transformed vertices, the mask and eps 0.02f), so the entries use the NORMALISED UVs.
- Missing corner: the parallelogram completion is computed in double and stored as a float, in this operand order: case 0 (u3 - u2) + u1, case 1 (u2 - u3) + u0, case 2 (u3 - u0) + u1, case 3 (u2 - u1) + u0 (the plan's `uv0 - uv3 + uv2` and
  `uv0 - uv1 + uv2` are the same sum in another order; the results differ only where the rounding differs).
- Orientation: dist = du * float(du) + dv * dv in double, `dist < best` with best the float of the smallest so far (start FLT_MAX), so the first corner wins unless a later distance is below the float rounding of an earlier one;
  code 0x80 / 0x88 / 0x90 / 0xA0; flipped (0x40) when |u_k - u_next| > |v_k - v_next| (doubles of float differences). A cell gets an entry when the header bit 0x80 is set or (mask[col * 16 + row] != 0 and the shape cell is not 0);
  a missing best face prints the error once and leaves {1, 10000}; the base record's first dword gets `|= 0x40` in every case. The ground node is the record's `+0x2c`, else the model's root.
Not modelled: `0x4679d0` with a NULL node (the whole-model walk), which `0x46aa10` never calls.

**X31, the edge vertex conform `0x469940` / `0x469a80`: the plan's rule is right except for the matrix, the inverse and the precisions.**
- `0x469940` is called with (instance object, world object, base height, flags): instance `+4` is the instance model (template at `+0x9c`, root `+0x78`, header flags `+0x30`), instance `+8` the base record whose `+0x14` is the list pointer,
  world `+0x6c` the landscape. **`0x46f150` builds an IDENTITY matrix** (zeroes the 16 floats, sets the diagonal); the plan's "M = the instance root's placement matrix" is the root node's OWN stored matrix, which the walk multiplies in like any node's
  (chain = node matrix * parent chain, from the identity: `0x4702d0`, one float store per element). Numerically the same; the placement therefore reaches the vertices only through the root node.
- The positions are read from the TEMPLATE tree (the type's model, `+0x60` blocks) and written to the instance's copy, so the conform is idempotent; the walk visits the instance's nodes in pre-order (first child `+0xc`, next sibling `+8`) and
  after the nodes it sets bit 0x10000 on the root and 0x40000 on the model header. Conditions as in the plan: a node with 0x200 takes no vertex (its children are still walked); a non-drape node without 0x1000 takes none; a 0x1000 node takes a
  vertex when its index equals the next list entry, the list pointer then advances one entry; the list reads as zero past its end. A node that "returns" (a corner past the last one) ends its own walk and its subtree but NOT the parent's walk of its
  siblings, and the entries it did not consume stay in front of the list for the next node.
- Per vertex: w = p * M (`0x46fc00`: x' = ((x m0 + m8 z) + m4 y) + m12, y' = ((m9 z + m5 y) + m1 x) + m13, z' = ((m10 z + m6 y) + m2 x) + m14, a float store each; note the vertex transform of `0x4628b0` adds in another order: ((m8 z + m4 y) + m0 x) + m12).
  Per axis (x with origin `+8` / cell `+0x10`, z with `+0xc` / `+0x14`): q = (w - origin) / cell in double, t = float(q) (0 when q < 0), i = trunc(t), f = float(t - i); f >= 0.5: corner i + 1, the node returns when i + 1 >= cells + 1;
  otherwise corner i, returns when i >= cells + 1. If |f| < 0.01f or |f - 1| < 0.01f, w = float(((i + [f >= 0.5]) * cell) + origin) (double sum). y = the corner height `H[(cellsX + 1) * cornerZ + cornerX]` as stored (no interpolation, the base height is
  not used). The result is w * M^-1 (`0x46fc00` again), and the node's bounds `+0x78..` widen (a value below the minimum replaces it, else one above the maximum does).
- **The inverse (`0x470040`) is a float LU decomposition, not an exact inverse.** `0x46fe20` scales every row by float(1 / largest |element|), decomposes by columns with partial pivoting (the pivot with the largest |sum| * scale, the last row among equals, `>=`),
  stores floats, replaces a zero pivot by 1e-20 and multiplies the column below the pivot by float(1 / pivot); `0x46fd40` solves for the four unit vectors (forward, then back substitution, each sum in double, one float store per element, the final divide by
  the stored pivot). `skirt_reading.lu_decompose` / `lu_solve` / `inverse` are exact against it (also for matrices with scales and skews). It is **not exact even for a quarter turn with a translation**: for a turn about y and a translation of (10, 0, 30)
  the inverse holds 0.99999994 instead of 1 and 29.999998 instead of 30 (also at `0x7f`). Consequence for the plan's F2 test ("flat ground leaves the mesh bit-identical"): conforming vertices that already lie on the grid at the right height does NOT leave
  them bit-identical in general. Of the 100 placements at a quarter turn (the four angles) and a whole translation of 0 to 40 in x and z that were tried (at `0x7f`), 68 return four such vertices unchanged and 32 move them. A port that wants the original's exact numbers
  needs `lu_decompose` / `lu_solve` / `inverse` as they stand; a plain double inverse rounded to a float (`exact-inverse`) differs on 62 of 400 inputs.
- Drape nodes (0x20000, every vertex, no snap and no return): the cell is trunc(t) per axis (fx, fz the floats of the fractions), the four corners e (x, z), d4 (x + 1, z), dc (x, z + 1), d8 (x + 1, z + 1) of the corner table. Two forms, both in double with one float store:
  A = ((1 - fx) H[e] + H[d4] fx) + fz F2 and B = (((1 - fx) H[dc] + H[d8] fx) + (1 - fz) (-F2)). The cell's flag byte (landscape `+0x2c`, four bytes a cell, bit 4 = the other diagonal) and the fractions choose: bit clear and fx >= fz: A with F2 = H[d8] - H[d4];
  bit clear and fx < fz: B with F2 = H[dc] - H[e]; bit set and fx < 1 - fz: A with F2 = H[dc] - H[e]; bit set and fx >= 1 - fz: B with F2 = H[d8] - H[d4]. The new y is (form - base height) + the y of w = p * M. All four cases are in the inputs.
- Not run: the second pass (`0x469940` loops over the model's node array `+0x70` (count `+0x44`, 0xa0 bytes each) and walks nodes whose `+0x98` is not NULL with the tolerance 0.1 and the interpolating branch). The plan names it "node flag 0x40"; the loop is keyed on the
  node's `+0x98` pointer. No shipped node has one (plan), so the port can leave it out.

**X32, the base height `0x466d70`: the C# rule is right.** The routine takes the box origin (`0x466f90`: the anchor itself at 0, (x, z + 1 - width) at 90, (x + 1 - width, z + 1 - height) at 180, (x + 1 - height, z) at 270), steps the width and height in
float accumulators of 1 / width and 1 / height, maps each corner of every cell whose type has a non-zero low nibble (`0x469930`; every cell for a NULL shape or flag 0x10) by `0x454050` to a grid vertex (the position, turned by the rotation, plus 0.1, truncated)
and returns (lowest + highest height) * 0.5 in ST0 (a double, exact for float heights), 0 when no cell counts. `ParkObjects.GetBaseHeight` (corners put on the grid by `ObjectFootprint.ToGrid`) agrees on all 500 inputs, the four rotations, the NULL shape and the
flag 0x10 included; a reading with the rotation 90 anchor off by one cell, or one that counts the zero cells, differs. The original does not clamp: a footprint beyond the map reads outside the height table.

Reproduce: `python3 -I skirt_synthetic_golden.py > source/OpenTPW.Tests/ObjectSkirtSyntheticGolden.cs` (needs Unicorn and the identified tp.exe), `x86_differential.py --target tpw-exe-skirt-flags` for any one target. Dev-only as the rest of section 7: no original byte or image
is committed.
The visible cells (E4): `tpw-exe-terrain-spans` runs `0x56bb00` with the routines under it on 406 cameras and `tpw-exe-terrain-cache` runs `0x572440` on 100 random frame sequences; both agree with their Python readings (`terrain_spans.py`, `terrain_cache.py`), whose outputs `terrain_spans_golden.py` and `terrain_cache_golden.py` turn into
`TerrainVisibleSpansGolden.cs` and `ProceduralTextureCacheGolden.cs`, so CI replays them without the executable. The spans reading has one detail worth knowing: an end column is the edge plus 0.999999 in single precision, which rounds up to the next whole cell from 32 on (the single nearest to v + 0.999999 is v + 1 only for v of 32 or more) (`test_spans_reach_the_last_column_and_row_of_a_level_field_but_not_of_a_tiny_one`).

Dev-only, as the rest of section 7: no original byte or image is committed, only hashes. The 16-bit pixel paths of the page store (`0x562f70`) are not run; the floor-art tail of `0x55f780` is X33 (section 7.22).

### 7.22 Finding X33: the floor composite tail of `0x55f780` is the plan's reading (issue 829, package F)

With a floor descriptor the build routine `0x55f780` does not copy the base buffer: from `0x5608b0` it reads the base in one of eight orientations picked by the descriptor word's bits (0x40 first, then 0x08,
0x10, 0x20) and writes the art blended over it. `tpw-exe-terrain-compose` runs it with the level object of `tpw-exe-terrain-build` and a descriptor over art in sandbox memory (the art buffer is what `0x590da0`
returns, so the routine's input; the device copy and the row turn of `0x590da0` are not emulatable and are decided by data, `docs/OBJECTS.md` OPEN-F1). The original and `terrain_reading.compose` agree on 56 of
56 cases (32-bit art and the 4444 variant, art that is not a multiple of N, smaller than N, alpha 0 to 255), including the pointer arithmetic: the start pixel, texel step and row step per orientation
(0: 0, +1, +N; 0x08: N² − N, −N, +1; 0x10: N² − 1, −1, −N; 0x20: N − 1, +N, −1; 0x40: 0, +N, +1; 0x48: N² − N, +1, −N; 0x50: N² − 1, −N, −1; 0x60: N − 1, −1, +N), the integer divisions of the art strides
(`width / N`, `height / N`), and the blend `(byte)(base + (((art − base) · alpha) >> 8))` (the product is unsigned in the routine; its low byte is the same). There is no floating point in the tail, so the
control word does not matter. `terrain_compose_golden.py` writes 42 cases (three sizes, 14 words, the five art sizes and five alpha modes in turn) as hashes for `ProceduralTerrainComposeGolden.cs`, which CI checks
`ProceduralTerrainTexturer.Compose` against without game data (`ObjectFloorBaseTests`).

### 7.23 Finding X34: the model drape of the queue models and the track pieces is the node walk 0x469a80, in single precision

Question: how does tp.exe put a placed model (the queue models of `0x5229e0`, and the kart and water pieces of PR 833) onto the landscape, and does `ModelDrape` / `QueueModelDrape` (docs/PATHS.md, "Queue models on the
terrain") reproduce it? Target `tpw-exe-model-drape` (section 7.3) runs `0x469940` (the wrapper of the placement `0x463060`'s creation flag 8) in the sandbox with the routines under it, no stub: the walk `0x469a80`
visits the instance's nodes in pre-order with the chain matrix `0x4702d0`, and for a node with the flag `0x20000` (the loader's drape flag, `0x4629d0` flag `0x40`, which `0x522900` and the kit loaders give every mesh node) moves
each vertex through the chain (`0x46fc00`), sets its height to `(landscape - base height) + height` and sends it back through the inverse (`0x470040`). The landscape value is the cell's triangle chosen by the cell word's bit `0x4`,
always split, read by index arithmetic over the `(cells + 1)` wide height array. The plan is `oracle_x86/model_drape_reading.py`. The synthetic parks have landscapes of 2 x 2 to 7 x 7 cells (corner heights from integers,
random floats, flat and hillside ramps; cell sizes 10 and others; random cell words), a placement at a quarter turn about the vertical axis with the cell's translation and a base height (the cell corners' midrange in half the
cases), mesh nodes with a signed-permutation matrix and a translation as the queue models carry, vertices on the grid lines, inside the cells, on the far edge and left of the origin, a grandchild now and then, nodes with `0x200`
(skipped) and without `0x20000` (untouched).

Result: all 400 inputs agree bit for bit (the vertices in each node's own space, the node flags with the root's `0x10000`); `test_inputs_reach_every_triangle_clamp_edge_and_node_kind` shows the inputs reach all four triangles
(two diagonals, both halves), the negative clamp, the far edge and both node kinds. The x87 precision matters: the same routine at the control words `0x27f` and `0x37f` disagrees with the single-precision reading on 268 of 400
(`test_model_drape_readings_are_sharp`, which also shows that a reading with the other diagonal bit or without the base height differs). `ModelDrapeGolden.cs` (generated by `model_drape_golden.py`, 64 small parks) replays the
reading's grid-frame vertices, bit for bit, in `ModelDrapeTests` without the executable. Not run: the second pass for nodes with their own list (`+0x98`, header flag `0x40`, which only the objects' skirt reads) and the node bounds the
walk widens. The float inverse of the chain differs from an exact inverse in the last bit of some translations (hence the copy of the LU reading), which is why the golden compares the grid-frame vertex and not the node-space one.

Part two (the placement the chain starts from): `tpw-exe-model-placement` runs `0x4708d0(4096)` (the sine table at `0x7b0968`) and then `0x467030` with the creation flag `0x20`. The yaw is multiplied into the root node's own
matrix by `0x46f650` (each row's x and z elements: `x' = x cos + z sin`, `z' = z cos - x sin`, with the table's cosine and sine, entry `i` being the single `sin(single(i / 4096) * 2 pi)`: the cosine of 90 degrees is
entry 2048, `-8.74e-8`) and only the translation words are replaced. All 200 inputs agree bit for bit (`model_drape_reading.placement`); `ModelDrapeGolden.Placements` replays 64 of them against `QueueModelDrape.Placement`.
The first version of the port dropped the root node's own matrix (the Space bends and ends carry a quarter turn in it), which this target and the Space captures would have shown.

### 7.24 Findings X35 and X36: the park fence and the cells a demolition leaves (round 9, package H)

Code: `oracle_x86/fence_oracle.py` (the sandbox), `fence_golden.py` and `validator_golden.py` (generators of `ParkFenceGolden.cs` and `QueueCellValidatorGolden.cs`). The sandbox runs the executable's own routines on a synthetic 128 x 128 map (the cells at `G + 0x294 + id * 0x44`, the structure records at `G + (id + 0x6cde) * 0x28`, `id = y * 128 + x + 1`); only the callees that draw, play or pay are stubbed (`0x5365D0`, `0x53AFA0`, `0x53C8D0`, `0x450D10`, `0x5DA3C0`, `0x452AE0`, `0x536CA0`, `0x527940`, `0x4D01F0`, and the bank `0x5195D0`).

**X35, the fence (PATH-007, ECON-026).** The click handler's mode 0x19 (`0x524960`) runs the line walker `0x53AAC0` with the passes 0x19, 0x80, 0x82, 0x85, 0x86 and 0x81 (step `0x5370E0`); the set-up `0x5323F0` then marks (`0x531040`), fills (`0x5311C0`), unmarks, walks the ring (`0x52BA50`) and clears flag 0x40 on the two rows of the gate strip; Buy Land (`0x5327E0`) lays five clicks, walks, marks, fills and unmarks. The oracle reproduces the four `restart.INTS` saves of the original cell for cell (every cell flag and every structure record, checked outside the repository), and 20 golden cases (the last two lay 12 rectangles each round the lobes, where the pocket grid `0x81EEB0` that `0x532960` rebuilds decides; the sandbox runs that routine for real before the validator, as the click handler does at `0x525D15`) (rectangles, L, U and lobed shapes, the six-click fence, water and no-terrain cells on the fence, merging purchases) are replayed by `ParkFenceGoldenTests`. Results: the fill scans right of its heading with a sticky edge flag on the north-heading scan only; the walk types the three cells east, south and south-east of every post as its extensions (parent = the post), so `0x5363F0` counts a post's whole 2 x 2 block (50 points per cell with its own flag 0x40 clear; the cell points of the saves' own cells equal the oracle's `0x4C7DB0` on all 12 corpus saves, `ParkFenceCorpusTests`); **a purchase in front of the gate that leaves the old fence outside the walk's ring makes the fill read the cell beyond the map's edge (a null cell, a crash at `0x53A536`) and a post-less cell makes the walk loop without end**: OpenTPW refuses such a purchase and charges nothing (PATH-007).

**X36, the cells a demolition leaves and the cell validator (ECON-050, QUEUE-012).** The demolition `0x527EE0` clears the queue first, then pass 1 (the NoModify flag of the path cell in front of an entrance or exit, unless two entrance or exit cells link to it or the MAP marks it an initial path) and pass 2 (flag 0x20 off, the cell remover `0x5367A0(0, 0)`, `0x53B280`). The remover empties a cell of type 1, 3, 4, 9, 10 or 0x15 entirely (links, queue link, owner, the whole flag word, the type) and unlinks its neighbours first (types 1 and 10 all eight bits, 3 and 9 the cardinal ones); a type-3 cell refunds while its owner cell is not empty; the other types are not written. The vectors are in `ParkDemolitionTests`. The validator `0x535670` is run on every combination of mode, cell type, flags, structure record, links, owner and latch state (14,256 cases, all replayed by `QueueCellValidatorTests`) and `QueueCellValidatorTests` replays a sample: the flag 0x40 refuses first, the types 4, 9 and 10 are accepted only below the tool's overwrite priority, a post or an extension of one is refused on owned land too, a corner or junction track tile is refused, and the validator reads no MAP terrain bit.
