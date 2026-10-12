# PowerPC differential oracle

Isolated, pure routines of the identified Mac PowerPC build run in OpenTPW's own PowerPC
interpreter. Their results are compared with the plans' reference formulas and with OpenTPW's
C# on this checkout. The oracle judges by result, not by reviewer opinion: a disagreement is a
finding to report and resolve, never something to hide.

Code: `tools/ppc-analysis/oracle/`. Tests: `test_ppc32.py` (always) and `test_oracle.py` (with the
private binary). Both run in `tools/ppc-analysis/run_evidence_checks.py`.

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
  test in 64 bits (2) and the UNBOUNCE window `/ 200 → / 250` (39). A no-op "mutation" fails the
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
| `arrival-group` | `0xcb4e8` | `0x1082b4` game → park (sentinel, checked), `0xc1fb4` counted guests (chosen count; argument 20 checked), `0xc24c0` park points (chosen), `0x1083c8` thing lookup (synthetic thing, `+48` chosen), `0x5fbcc` debug print (logged). The GameType object is marked constructed, so its constructor `0x12bb64` must not run | 515 | **F9**: 125 differ from GUESTS.md. **Agree** 515/515 with the routine reading | **F9**: 125 differ, the same inputs as the plan (`arrival_group`: `GuestSimulation.ArrivalGroupSize`) |
| `arrival-update` | `0xcb2ac` (the arrival update), `0xcb4e8` (group size), `0xcb028` (constructor), `0x1210f8`/`0x121098` (timer elapsed / restart), `0x10a9a4` (game pointer) | `0x109784` vehicle phase and `0x109b78` report flag (chosen per call), `0x109368` vehicle script step (logged with its frame slot and size), `0xcb698` guest creation (counted, raises the counted guests), `0xcb750` group history (logged), `0xb678` assertion (logged), and the `arrival-group` stubs. One run is a script of `0xcb2ac` calls in one sandbox | 510 scripts | **agree** (behaviour: active byte, timer start, guests created per call; GUESTS.md loop with the F9 details, group floored at 0). Routine reading **agree** 510/510 (adds the group word and the vehicle steps). **F11**: the brief's static reading (a negative group holds the state) differs on 66 scripts | no C# hook (`UpdateArrivals` has no vehicle phase) |
| `park-points` | `0xc24c0`, `0xc5828` (type count), `0xd1be8`/`0xd1c88` (park record search), `0xe4750` (turns → 100 ns units), `0x10a9a4` (game pointer), `0x1c4100` (64-bit divide) | `0x118018` type record (synthetic, registry checked), `0xb678` assert (a zero condition fails the run), `0xdc834` counts-test (chosen flag), `0xdc520` own age (signed timestamp difference in days). The park record table is built: `+24` the listed copies, `+28` by the `0xda874` write rule. Clock rate 15000; no build after the current turn | 507 | **F10**: 214 differ from GUESTS.md. **Agree** 507/507 with the routine reading | hook `park_points` not wired (`ParkEconomy.ParkPoints`) |

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
  the counted guests fill the simulation's guest list (a field) by reflection. The thing `+48` word and the GameType
  mode word have no C# counterpart and are not passed.

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

**F9, arrival group size (GUESTS.md "Arrivals", `GuestSimulation.ArrivalGroupSize`).** GUESTS.md and C#
use `(int)(1.2f × (points + NewParkBonus)) / PointsPerVisitor`, at least MinPeople, cut to 1500 − guests and never
below 0. Executed, `0xcb4e8` differs in three places. A reading with all three agrees on 515/515 inputs, and dropping
any one of them brings mismatches back (`test_arrival_reading_is_sharp`).

- **Factor.** `0xcb564..0xcb590` calls `0x1083c8`. That routine returns the thing whose ThingArray index is the u16
  at game `+0x1da734` (20-byte entries; the neighbour `0x108424` reads `+0x1da736` for the bank). When that thing's
  `+48` word is **positive**, the factor is `0.8f` (TOC −11272, `0x3f4ccccd`). Otherwise it is `1.2f` (TOC −11268).
  What the `+48` word counts is not traced (ARRIVAL-002).
- **500 cut in online parks.** When word 0 of the GameType object (`data:0x53d98`, PPC-review "New binding": 0 Full
  Simulation, 1 online, 2 Instant Action) is 1, the group is first cut to `500 − guests`. Otherwise the 1500 cut
  applies. Offline parks therefore use 1500 as C# does. OpenTPW's online visits run no simulation, so the 500 cut has
  no OpenTPW counterpart yet.
- **No floor.** Both cuts compare `guests + group` with `cmplwi` (unsigned) and store `limit − guests` as is. With more
  guests counted than the limit, the group is negative: 1700 guests give −200, where C# gives 0. Statically, the caller
  `0xcb2ac` creates guests only while the count is above 0 (`0xcb3bc`). Executing it with a negative group is F11
  (ARRIVAL-004): the group does not hold the arrival state.

First counterexample: shipped values (NewParkBonus 20, PointsPerVisitor 6, MinPeople 1), no points, no guests, thing
`+48` = 1. The original gives 2 (0.8 × 20 / 6) and the plan gives 4. Integer and float steps agree with the plan
wherever the three points above do not apply: the int → double → `fsubs` conversion is `(float)(points + bonus)` with a
32-bit wrapping sum, `fmuls` rounds to single, `fctiwz` truncates and saturates, and `divw` truncates toward zero.

**F10, park points (GUESTS.md "Arrivals", `ParkEconomy.ParkPoints`).** GUESTS.md and C# sum, over every ride and
sideshow, `(NewBonus[age / decay] + AttractionValue) / copies of that type`, with each object aged from its own build.
Executed, `0xc24c0` differs in three places. A reading with all three agrees on 507/507 inputs, and dropping any one
of them brings mismatches back (`test_points_reading_is_sharp`).

- **Counts-test.** Only things for which `0xdc834` returns nonzero count. It tests thing `+46` bit 0x4, then
  `0xdfe0c`, `0xdff18`, `0xdfe20`, `0xdd3f4` (against a u16 behind TOC −19648), `0xdc988` and `0xe2fe4`/`0x45eac`,
  and finally rejects a thing whose type `+0x9C` is 1 or 2 while thing `+40` is 0 (`0xdc918..0xdc964`). What these
  stand for is not traced (ARRIVAL-003).
- **Age per type.** When type `+0x9C` (156) is 0, the age comes from the park's per-type record (`0xd1be8` on the
  table at game `+0x1d943c`, 32-byte entries keyed by type id). The object purchase routine `0xda874` writes the
  current turn (game `+0x1da70c`) to its `+28` only while `+28` is still 0 (`0xdadec..0xdae14`). Purchases at turn 0
  therefore leave it unset, and the first purchase at a later turn fixes it. All copies of the type share that age, so
  a copy bought later gets no fresh bonus. The purchase also raises `+24`, the removal path lowers it
  (`0xdbfcc..0xdbfdc`), and `0xc5828` returns it as the divisor. Statically, no removal path writes `+28`; it is
  zeroed when the table is reset (`0xd1d1c`) and restored by the save loader (`0xd1e8c`). `0xe4750` turns the
  difference into 100 ns units: `(now − turn)` as an unsigned word times clock `+28` (15000), divided by 4, times
  10^7. `0xc24c0` divides that by 864,000,000,000 (one day) with the signed 64-bit helper `0x1c4100` and keeps the low
  word.
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
