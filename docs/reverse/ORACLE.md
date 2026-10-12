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
  `LoanReceived` and the sign of the join excitement refusal swapped (`queue-join-gate`). A no-op "mutation" fails the
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
| `coast-rating-step` | `0x415b4` | none | 509 | **F7**: 440 differ with the stale term = 0. **Agree** 509/509 with the stale term = Fz. Previous force and bank are 0 in every input (OV-7, F7) | COAST-I not on main (hook `coaster_rating_step`) |
| `coast-rating-final` | `0x41900` | none | 506 | **F8**: 280 differ. **Agree** 506/506 with the corrected count | hook `coaster_rating_final` |
| `arrival-group` | `0xcb4e8` | `0x1082b4` game → park (sentinel, checked), `0xc1fb4` counted guests (chosen count; argument 20 checked), `0xc24c0` park points (chosen), `0x1083c8` thing lookup (synthetic thing, `+48` chosen: the Traffic Lights' mTopLeft), `0x5fbcc` debug print (logged). The GameType object is marked constructed, so its constructor `0x12bb64` must not run | 515 | **F9** (factor and online cut fixed): 49 differ from GUESTS.md, all from the missing floor. **Agree** 515/515 with the routine reading | **F9**: 49 differ, the same inputs as the plan (`arrival_group`: `GuestSimulation.ArrivalGroupSize`) |
| `arrival-update` | `0xcb2ac` (the arrival update), `0xcb4e8` (group size), `0xcb028` (constructor), `0x1210f8`/`0x121098` (timer elapsed / restart), `0x10a9a4` (game pointer) | `0x109784` vehicle phase and `0x109b78` report flag (chosen per call), `0x109368` vehicle script step (logged with its frame slot and size), `0xcb698` guest creation (counted, raises the counted guests), `0xcb750` group history (logged), `0xb678` assertion (logged), and the `arrival-group` stubs. One run is a script of `0xcb2ac` calls in one sandbox | 510 scripts | **agree** (behaviour: active byte, timer start, guests created per call; GUESTS.md loop with the F9 details, group floored at 0). Routine reading **agree** 510/510 (adds the group word and the vehicle steps). **F11**: the brief's static reading (a negative group holds the state) differs on 66 scripts | no C# hook (`UpdateArrivals` has no vehicle phase) |
| `park-points` | `0xc24c0`, `0xc5828` (type count), `0xd1be8`/`0xd1c88` (park record search), `0xe4750` (turns → 100 ns units), `0x10a9a4` (game pointer), `0x1c4100` (64-bit divide) | `0x118018` type record (synthetic, registry checked), `0xb678` assert (a zero condition fails the run), `0xdc834` counts-test (chosen flag, itself the next target), `0xdc520` own age (signed timestamp difference in days). The park record table is built: `+24` the listed copies, `+28` by the `0xda874` write rule. Clock rate 15000; no build after the current turn | 507 | **F10** (fixed): GUESTS.md and the routine reading **agree** 507/507 (the 121 differences came from the counts-test, ARRIVAL-003) | `ParkEconomy.ParkPoints` through `park_points`, the input's open flag passed as `IsOpen`: **agree** 507/507 (121 before the counts-test) |
| `park-points-counts-test` | `0xdc834` (the counts-test), `0xdc988` (queue room), `0xdfe0c`/`0xdfe20` (ride state 1 / 4), `0xdff18` (open word), `0xe2fe4` (track type 3) | `0x118018` type record (`+156` chosen, registry and type id checked), `0xdd3f4` back-of-queue cell (writes the chosen u16), `0xdd118` listed guests (chosen), `0x45eac` coaster controller (chosen word). The u16 behind TOC −19648 is read as 0 (checked) | 522 (one failing condition at a time, wrapping queue room) | **Agree** 522/522 with the reading, stub call order included. The part C# implements (`implemented-subset`: choosable, not state 1 or 4, open) differs on 205 | no hook: entrance cell, queue room and coaster have no state in `ParkEconomy` |
| `loan-offers` | fragment `0xcb7a8..0xcb908` (the 8-offer loop), `0x1c3fbc` (float→uint), glue `0x1c61a4` | `0xfa718` base constructor (header only), import `pow` (MathLib, stubbed with Python `math.pow`) | 505 offer sets (8 offers each) | **F12**: **agree** 505/505 with the plan (fixed in C#; 497 sets differed from the former annuity) and with the routine reading | `LoanMath.MonthlyRepayment` through `loan_offers`: **agree** 505/505 (497 differed before) |
| `loan-month` | fragment `0xcc21c..0xcc3d0` (batch flush and the 8-record loan loop) | `0x1082b4` game → park (sentinel, checked), `0xb678` assertion and `0x5fbcc` debug print (logged) | 506 states of 1-3 months | no plan side. **Agree** 506/506 with the routine reading | no C# hook (see `loan-term`) |
| `loan-term` | the same fragment, run once a month over a whole term (withdrawals enabled, no batch) | the same | 503 terms | **F13**: **agree** 503/503 with the plan (fixed in C#; 483 differed from the former interest-on-balance arithmetic), the routine reading too | `LoanMath.Pay`, `ParkLedger.Post`, `ChargeLoanInterest` through `loan_term`: **agree** 503/503 (483 differed before; the year-profit word wraps at 32 bits on both sides) |
| `loan-payoff` | `0xccc78` (early repayment) | the same | 506 | **F13**: **agree** (payoff M × months left, year profit charged q × N, 32-bit wrap), routine reading too | `ParkEconomy.TryRepayLoan` through `loan_payoff`: **agree** |
| `loan-take` | `0xcc904` | the same | 505 | **F14**: **agree** (money in, balance, no profit, bought flag), routine reading too | `ParkEconomy.TakeLoan` through `loan_take`: **agree** |
| `queue-failure-predicate` | `0xdfe34` alone | none | 521 | **F15**: 310 differ (QUEUE-008 reads VAR_BROKEN ≠ 0; the routine reads ride `+46` bit `0x1` and the float `+64`). **Agree** 521/521 with the routine reading | no hook: `RideVisitorBridge.IsBroken` needs a live `RideVM` (CSHARP_PENDING); the plan side is that claim |
| `queue-join-difference` | `0xe9a84`, `0xdfe34`, `0xeb7e4` (guest preference), `0xe02ec` (excitement), `0xdff70` (its formulas), `0xe1f70`/`0xe1f78`/`0xe2364` (sideshow words), `0xe2fe4` (track type 3), `0x41a14` (coaster getter, controller table built) | `0x118018` type record (registry and type id checked), `0xb5e24` script controller (script `+32` checked), `0x1ea40` pieces behind ride `+36` (the `+36` word and four distinct outputs checked), import `sqrt` (IEEE double square root, argument checked non-negative). The settings word `+164 + 12 · type` is written in the data block | 524 | **F16**: 387 differ from the claimed "ride minus preferred" (clamp ±50, the relief case 100, live excitement). **Agree** 524/524 with the routine reading | no hook (CSHARP_PENDING): C# has no clamped difference |
| `queue-join-gate` | the fragment `0xecd6c..0xecdb0` / `0xeced0..0xecf34` of the state-10 handler `0xeccb0` (the `0xe9a84` call, `0xe02ec(ride, 1)`, the 45 test and the refusal) plus the callees above | the callees' stubs, plus `0x5fbcc` log (the message text is read from memory), `0xfb7b4` reason and `0xfb54c` thought (guest `+48` object, id and thought checked) | 524 | **F16**: 155 differ in outcome. **Agree** 524/524 with the routine reading (outcome and difference) | **F16**: 155 differ, the same inputs as the plan (`RideVisitorBridge.ExcitementRefusal`) |

The C# comparisons use these seams:

- `RideVisitorBridge.ComputeQueueLimit`.
- `ParkCellMap.CanChangeCellType`.
- `ParkEconomy.TrySpendCell` in `ParkPathBuilder.Validate` order: refuse, then the same type
  bumps, then the money test. Settings come from `EconomyTestData`.
- `RideVisitorBridge.TakeUnbounced` and `RideVisitorBridge.TakeBounceSlot`, called by reflection because they are
  internal.
- `GuestSpeed.Caps` and `GuestSpeed.Update` (`guest_speed_limits`, `guest_speed_smoothing`), with s passed and
  returned as single bits. `CSharpMutation` flips the fused multiply-add to a plain one and the floor 655 to 654.
- `GuestSimulation.ArrivalGroupSize` (`arrival_group`) on a fresh simulation: world mode 4 sets `ArrivalsHalted`, and
  the counted guests fill the simulation's guest list (a field) by reflection. The thing `+48` word is passed as
  `TrafficLightsPlaced` (word > 0) and the GameType mode word 1 as `OnlineVisit` (the 500 cut).
- `ParkEconomy.ParkPoints` (`park_points`) on an economy built from the input: one synthetic object type per input type
  (kind 0 a ride, kind 2 a sideshow, any other kind a shop; attraction value, decay time and the three bonuses from the
  input) and one `RegisterExisting` object per thing. The economy clock has a read-only setter, so reflection puts each
  object's build tick and the final tick on the first tick of the input turn. The type `+0x9C` word is passed as
  `Bumper.WhichTrackType`, and the park record `+28` turn is written to the first-purchase counter after the objects are
  registered, as a loaded save restores its counters (it can come from a sold copy or be unset; `ParkPointsTests`
  covers how purchases set it). The open flag (the
  chosen `0xdc834` counts-test result) is passed as the object's open state (`IsOpen`); the test's other conditions have no
  C# state and are not in the inputs.
- `RideVisitorBridge.ExcitementRefusal` (`queue_join_gate`, internal static, called by reflection): the gate of `JoinQueue` on
  its own. The bridge builds the difference as `GuestSimulation.UpdateGoingToRide` does, preferred excitement minus the ride's
  `ExcitementLevel`: the type's `.sam` value (`UsageInfo.ExcitementLevel`, type `+316`), or `CoasterRating.GuestExcitement` for a
  coaster whose controller exists (0 before). The type's `+1960` word (`Info.WhichUIType`) 0 maps to `Ride`, 2 to `Sideshow`,
  anything else to `Facility`.

- `LoanMath.MonthlyRepayment` (`loan_offers`) for each offer of an input; `LoanMath.Pay`, `ParkLedger.Post` and
  `ParkLedger.ChargeLoanInterest` (`loan_term`) run month by month exactly as `ParkEconomy.EndMonth` does, with the
  instalment M given (the original's, as C# now computes it); the real `EndMonth` is covered by
  `ParkEconomyTests`. `ParkEconomy.TryRepayLoan` (`loan_payoff`) on an imported bank with one bought loan; `ParkEconomy.TakeLoan` (`loan_take`) on an economy with the one offer (a second take refused for
  an offer already bought); the changes of balance, year profit and the month's money-in total are compared.

The pricing domain is the free byte clear, `game+36 == 0` and bank `+276 ≠ 0`. Old 1 → new 3
belongs to `path-can-change`.

## 5. Findings

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
  depends on `0xeb0f4`'s amounts (not traced).

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

- **Factor (fixed in GUESTS.md and C#, ARRIVAL-002).** `0xcb564..0xcb590` calls `0x1083c8`. That routine returns the
  thing whose ThingArray index is the u16 at game `+0x1da734` (20-byte entries; the neighbour `0x108424` reads
  `+0x1da736` for the bank). When that thing's `+48` word is **positive**, the factor is `0.8f` (TOC −11272,
  `0x3f4ccccd`). Otherwise it is `1.2f` (TOC −11268). What the thing and the word are:
  - *The thing is the Traffic Lights fixed item.* The world setup `0x10474c` looks two fixed items up by name in the
    loaded object list (the UTF-16 names `gates` and `lights`, built by the static constructor at `0x10a7xx`). It creates
    each through `0xda874` and stores the thing id at `+0x1da732` (gates, `0x104bf4`) and `+0x1da734` (lights,
    `0x104cb0`, with the log line "Found the LIGHTSNAME WAS HERE (BR 990809) feature"). The world-variable writer
    `0x105d3c` saves the two words as `mParkGates` and `mTrafficLights` in that order (`0x105fc4`, `0x105fec`; the name
    strings are read from the image). A missing lights type asserts and stores handle 0 (`0x104cbc..0x104d0c`).
  - *The `+48` word is `mTopLeft`, with `mEntryPos` at `+50`.* The object serializer `0xdaf04` pairs `mTopLeft` with
    `+48`, `mEntryPos` with `+50` (QUEUE-plan: entrance cell id `1 + X + (Y << 7)`), `mExitPos` with `+52`,
    `mBackOfQueue` with `+54` and `mFirstInQ` with `+56`. The constructor `0xda874` zeroes them (`0xda8f4`) and then
    computes them at `0xdaaa0`, `0xdaadc` and `0xdab18` as the placement cell id plus the type's rotated cell offsets
    (`0xd8fe0`). The other halfword stores at `+48`/`+50` that a whole-code scan finds (`0xd87e8`, `0xd886c`, `0xd892c`,
    `0xd8a4c`, `0xbc138`, `0xbe97c`, `0xfb25c` and others) sit in other structures or in constructors, and the save loader
    reads the pair back; none was found that rewrites it on a placed attraction (static, not exhaustive for indirect
    writers). The word is `+48` in the high half, so it is positive when `+48` is below `0x8000` and the pair is not
    both zero. For the lights it is exactly the fixed-item placement: the setup passes cell id 1 (`0x104c74`), so
    `mTopLeft = mEntryPos = mExitPos = 1`.
  - *Saved data agrees.* The Jungle `Easymode.TPWI` Lights record (Info.Id 1603, thing 12; the Lights ids are 1603, 2603,
    4603 and 3603 for jungle, hallow, fantasy and space; the Gates record 1601 and the Bus 1600 alike) holds `mTopLeft = mEntryPos = mExitPos = 1`, read from the decoded attraction records (offset 196 of
    the body, a throwaway read that stays out of the repository).
  - *Result.* The proven rule is: the signed 32-bit word at `+48` of the `0x1083c8` thing positive → 0.8, else 1.2. The
    routine reads the word without checking that the thing exists. With the lights placed, the word is 1 (saved record,
    and the setup arithmetic), and the static writer search found no later store, so the original uses 0.8 in a park
    with its lights. What it does without the lights (handle 0) is not established: the oracle only tested a present
    synthetic thing with a non-positive word. C# takes `TrafficLightsPlaced` (set by `Level`, `M3Gate` and `M4GateRun` from
    the placed fixed items); null or false uses 1.2, which is OpenTPW's own fallback, not an original rule.
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
the plan and C# give 0. Before the fix the first one was the shipped values with the lights' word 1: the
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

What this does to the queue (static, not executed):

- *Guest state 11, step 3 (`0xed424`).* The original's exit "0xdfe34 set, thought 14 (feeling sick), leave" fires for a worn-out
  toilet only. For any other ride it never fires, so a broken ride (script `VAR_BROKEN ≠ 0`) does not send its queue away through
  this exit. C# does (QUEUE-008, state-11 step 3): its queue empties through an exit the original does not have for rides.
  What else empties the original's queue is below.
- *Other callers.* `0xd6618`, `0xdc104`, `0xdf5d0`, `0xe9aa4` and `0xed424`. The first three sit in ride and toilet update code
  next to the maintenance flag (`0xdfe88`) and the relief test (`0xdc0f4`); not traced further.
- *What a breakdown does instead.* `SetState(1)` (`0xe0c3c`, the `BROKEN_DOWN` block `0xe0ec4..0xe10dc`) logs "Closing...",
  clears the open word `+100`, zeroes the pending visitor `+104`, sets script variable 6 to 1 and handles only the head guest of
  the queue (`0xdcebc`/`0xee804`, or `0xeda04` when that guest is in state 14). State 11 then refuses to move guests up while
  `0xdfe0c` (state 1) holds. Whether the rest of the queue leaves because the ride is closed is not traced, so C#'s emptying
  queue may still be right in effect and wrong in cause and thought. QUEUE-018's treatment of a broken ride as closed fits the
  cleared open word.
- *Admission (QUEUE-019).* `0xdfe34` has no part in it. `0xe1864` runs the admission `0xe1404` first and then reads script
  variable 7 itself (`cmpwi r3,0`, so `≠ 0`): the breakdown turn still admits. The script predicate the original does have is
  `0xdfed4`: it returns `VAR_BROKEN > 0` (signed; the `eqv/subfc/addze` idiom), and its only caller is the repair-state code at
  `0xd9e80`.

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

C# was changed only by extracting the gate into `RideVisitorBridge.ExcitementRefusal` (the behaviour is unchanged, QueueTests
pins it) and by sharpening the QUEUE-004, QUEUE-008 and QUEUE-019 comments. Recommendation, not done because each needs state
C# does not have or a decision about the queue after a breakdown: (a) give sideshows and rides their live excitement
(`ExcitementSource` already exists for coasters), (b) gate on the `ExcitementLevel` byte, and (c) replace the broken-ride exit by
the original's ride-closed handling once `SetState(1)`'s effect on the queue is traced, keeping `0xdfe34` for toilets
(whose wear gauge C# does not simulate).

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

### 7.3 First target and pins

| Target | Routine (allowed code) | Stubs | Inputs | vs plan | vs routine reading |
| --- | --- | --- | --- | --- | --- |
| `tpw-exe-world-rng` | tp.exe `0x516330`..`0x516365` (thiscall, `ecx` = object; state at `+0x1da708`) | none | 508 (500 random + 8 boundaries) | **X1**: 507 differ from the Mac model `det_model.world_next` (first input 0: original returns 1694899337 and stores 2600067959, the plan 1013904223 for both) | **agree** 508/508 |

| `tpi-exe-world-rng` | TPI `Game.exe` `0x6493e0`..`0x649415` (thiscall, `ecx` = object; state at `+0x1eab3c`) | none | 508 | **X2**: 507 differ from the Mac model (first input 0: the same pair as tp.exe, 1694899337 returned, 2600067959 stored) | **agree** 508/508 with the TPW PC reading |

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
the Mac targets already pinned (`arrival-group`, loans, park points), where a PC-vs-Mac divergence
would matter most.
