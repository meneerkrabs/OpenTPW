# PPC lane review (independent ninth reviewer)

2026-10-09. Static review only. No original program was executed or emulated.
No binary bytes, expanded sections or disassembly are stored here. The
verifier prints addresses, decoded operand fields, interpreted constants and
conclusions only. Binary identities are the three SHA-256 values in
[FINDINGS.md](FINDINGS.md). The `.data` files are Feral's Mac PowerPC port, so
Mac facts transfer to the PC executable only when separate PC evidence
supports them (see "Mac/PC transfer" below).

## Reproduce

Placeholders: `$FERAL_BIN` is the directory holding the extracted Feral `.data`
files (with `libraries/` below it), `$FERAL_HFS` the Mac disc image, `$TPW_DATA`
and `$PATCH2_DATA` the `Data` directories of a retail and a Patch 2 install, and
`$RIDES_CHECKOUT` a checkout of the rides lane, `$INTEGRATION_CHECKOUT` the
integration checkout under review, `$TPI_DATA` the `Data` directory of the
supplied TPI retail install, and `$ROOT_CHECKOUT` the root repository.

```sh
python3 -I -m unittest discover -s tools/ppc-analysis/lanes/review -p 'test_*.py' -v
python3 -I tools/ppc-analysis/lanes/review/review_evidence.py $FERAL_BIN \
  --pc-save $TPW_DATA/levels/jungle/Easymode.TPWI
python3 -I tools/ppc-analysis/lanes/review/reloc_audit.py $FERAL_BIN/*.data \
  $FERAL_BIN/libraries/*.data
python3 -I tools/ppc-analysis/lanes/review/round2_evidence.py $FERAL_BIN \
  --mac-hfs $FERAL_HFS \
  --pc-speech $TPW_DATA/global/Speech
python3 -I tools/ppc-analysis/lanes/review/round3_evidence.py $FERAL_BIN
python3 -I tools/ppc-analysis/lanes/review/round4_evidence.py $FERAL_BIN
python3 -I tools/ppc-analysis/lanes/review/round5_evidence.py $FERAL_BIN \
  --pc-data $TPW_DATA \
  --pc-data $PATCH2_DATA \
  --rides-root $RIDES_CHECKOUT
python3 -I tools/ppc-analysis/lanes/review/round6_evidence.py \
  --bin-root $FERAL_BIN \
  --pc-data $TPW_DATA \
  --pc-data $PATCH2_DATA
python3 -I tools/ppc-analysis/lanes/review/round7_evidence.py \
  --bin-root $FERAL_BIN \
  --repo $INTEGRATION_CHECKOUT
python3 -I tools/ppc-analysis/lanes/review/round9_evidence.py \
  --bin-root $FERAL_BIN \
  --pc-save $TPW_DATA/levels/jungle/Easymode.TPWI \
  --tpi-save $TPI_DATA/PreBuilt/prebuilt.TPWS \
  --git $ROOT_CHECKOUT
python3 -I tools/ppc-analysis/lanes/review/round10_evidence.py \
  --bin-root $FERAL_BIN \
  --pc-save $TPW_DATA/levels/jungle/Easymode.TPWI
OPENTPW_PPC_BIN_ROOT=$FERAL_BIN \
OPENTPW_PC_DATA=$TPW_DATA \
  python3 -I -m unittest discover -s tools/ppc-analysis/lanes/review -p 'test_*.py'
# Every ppc-analysis Python suite, one lane per process (section 44):
python3 -I -B tools/ppc-analysis/run_evidence_checks.py --mac-bin $FERAL_BIN \
  [--pc-data $TPW_DATA] [--pc-fixture $TPW_DATA/levels/jungle/Easymode.TPWI] \
  [--require-fixtures] [--dotnet $DOTNET8/dotnet]
```

`round2_evidence.py` (section 4 onwards) uses the same pinned `Binary` and
fails closed in the same way. Its optional asset check prints only sizes,
SHA-256 values and image offsets. `round3_evidence.py` (section 12 onwards)
additionally pins `engine_shared.data` and `ltms_shared.data`; `round4_evidence.py`
(section 18 onwards) reuses those pins, and `round5_evidence.py` (section 24
onwards) adds `sound_shared.data`. Its optional `--pc-data` check reads LoanInfo
numbers from the install at run time, and `--rides-root` compares a rides
checkout's dispatch table with that lane's contract JSON. `round6_evidence.py`
(section 32 onwards) reuses the `SimThemePark.data` pin for the rides selector
routines and reads SDT entry headers from each `--pc-data` install, printing
counts only. With both variables set, all 95 lane tests run, with no skips.
Without them, 89 pass and the six original-file witnesses skip.

`review_evidence.py` exits non-zero if any identity, relocation, glue stub,
operand or constant differs from the reviewed value. Unlike
`timer_evidence.glue_import`, it requires the complete six-word CFM glue
pattern before naming an import. `ppcdis.py` is a local review aid that
prints `llvm-mc` output to the terminal. The checked-in verifier does not
depend on it, and its output must not be committed. The 14 synthetic tests
target interpretation hazards: they distinguish competing readings rather
than restating expected constants. They cover operand order of `subc`
extended mnemonics, unsigned versus signed compare immediates, partial glue
matches, ambiguous relocation repeats, the two loan-record readings, operation
order of the loan exponent and saturating conversion edge cases.

## 1. Audit of existing PEF/timer evidence

| Claim (FINDINGS.md) | Verdict | Reason |
| --- | --- | --- |
| PEF reader parses all 16 containers; 38,072 relocated words | **Accepted (corpus-bounded)** | Every section-relative addend lies inside its target section, and both words of every exported transition vector are relocated. The corpus contains **no** `RelocSmRepeat`/`RelocLgRepeat` instructions (counts: Incr 4,091, BySectDWithSkip 4,063, Run 3,205, LgByImport 1,473, SmIndex 1,461, SetPosition 17). `pef.py` replays repeat blocks by *instruction units*, whereas the specification counts 2-byte chunks. The two readings differ only for blocks containing 32-bit or nested instructions. That path is therefore untested by real data, though it cannot affect current results. `reloc_audit.py` reports ambiguous blocks if a future container has them. |
| `LbTime_GetClock` = low 32 bits of `GetAbsolute / 1000` | **Accepted** | The wrapper passes `r1+80` as the result buffer and reloads both words from it. The helper ends `adde r4,r4,r4; adde r3,r3,r3; blr`, so it returns the shifted dividend: the **quotient**, not the remainder kept in r7:r8. The function immediately after it (`0x59e88`) begins by testing and negating sign bits, so it is the **signed** sibling and `0x59d98` is the unsigned one. |
| `Microseconds` fallback copies microseconds unchanged | **Accepted, conditional** | The previous verifier did not check the missing links. Here, `mr r31,r3` at entry makes r31 the caller's result pointer, and `addi r3,r1,72` before the call makes `r1+0x48` the `UnsignedWide` that is reloaded and stored. Route selection is still runtime-dependent. |
| `SetRate` copies interval/flag without scaling | **Accepted** | The function is exactly five moves followed by `blr` at `+0x14`. |
| Application `SetRate(100000, 1)` at `this+0x5c` | **Accepted. Role still unknown** | Reconfirmed. The same constructor also stores **500,000** (`lis 8; addi -24288`) at `this+0x70`. That value was unreported and its role is unknown. `UTimer::HasTicked` (`0xa698`) ticks when `elapsed > interval`, using an unsigned 64-bit comparison. With flag ≠ 0 it resynchronises `last = now`, so missed periods are **not** caught up. With flag 0 it adds `last += interval`. Flag 1 here therefore means drop-missed-ticks behaviour. No consumer of `this+0x5c` has been traced. |
| Doubles 1000.0, 60.15, 60,000,000.0 | **Accepted as literals only** | No rate claim is derived from them, which is correct. |
| TOC base of SimThemePark is `0x8000` | **Accepted** | Read from the main transition vector's relocated TOC word. |
| Heuristic `analyze.py` boundaries/names | **Remain untrusted** | This review never uses them. The function boundary of the payoff/constructor spans was read from prologues and call sites, not from traceback heuristics. |

There is a weakness in the existing `timer_evidence.glue_import`: it accepts
any call target whose first word is `lwz r12,d(r2)`. All glue targets cited
in FINDINGS.md pass the full six-word check here, so no reported result
changes. Lanes that reuse `glue_import` should still not rely on it alone.

## 2. Economy lead: loan arithmetic (constructor `0xcb7a8`)

**Verdict: accepted, with corrections and stronger field identities.** The
loan arithmetic is
`monthly = u32sat(trunc(amount × pow(1 + APR/100, (months/12)×0.5) / months))`.
The lead's "months/24" is bit-identical: multiplying by 0.5 is exact, and a
test covers 1–600 months.

Field identities are **not** inferred from the arithmetic. The bank serializer
(`0xcba54`, labels from TOC slot `0x3100` → code `0x1cc8ce`) passes each
field address together with the developer's member-name string. Here,
`loan+N` means `bank + 32·i + N`:

| Offset | Serializer label | Constructor source |
| --- | --- | --- |
| `this+0x0c` | `mBalance` | settings `+0x198` |
| `this+0x10` | `mBatchBalance` | 0 |
| `this+0x114` | `mWithdrawalsEnabled` | 1 |
| `this+0x118` | `mAdmissionFee` | via `0xcc51c` with settings `+0x19c` (not traced) |
| `this+0x11c` / `0x120` / `0x124` | `mLastBalance` / `mTurnEnteredRed` / `mProfitThisYear` | 0 |
| `loan+0x14` | `loan_available` | `amount <= 10000` (unsigned) |
| `loan+0x18` | `amount_available` | settings `+0x1a0 + 16i` |
| `loan+0x1c` | `APR_in_percent` | `+0x1a4 + 16i` |
| `loan+0x20` | `repayment_period_in_months` | `+0x1a8 + 16i` |
| `loan+0x24` | `monthly_repayment` | the formula |
| `loan+0x28` / `0x2c` | `loan_bought` / `months_repaid` | 0 |
| `loan+0x30` | `lenderNameIndex` | `+0x1ac + 16i` |

The settings global (`sec1+0x54860`, only via TOC slot `0xa7c`) is zero in
the static image, so it is populated at runtime. The settings schema lists
`InitialCash, InitialAdmissionFee, …, LoanAmount, APRInPercent,
RepaymentPeriodInMonths, Lendername, LoanInfo[10]` in the same order as the
copies, but the `.sam` parser that fills the global was **not traced**. The
link from `.sam` keys to `+0x198…` is therefore **consistent but needs
evidence**. The schema bounds `LoanInfo` at 10 entries; the bank uses 8.

Corrections and additions:

- `0x1c3fbc` is a **saturating** double→u32 conversion. It returns 0 for x<0
  and `0xffffffff` for x≥2³², +∞ and NaN, and truncates toward zero
  otherwise. A 0-month offer therefore yields `0xffffffff`, not a trap.
  Economy's `monthly_payment()` raises on `months <= 0` and maps negatives
  differently. That is acceptable as a guard, but it is not the original
  behaviour.
- Only two `pow` callers exist (`0xab4bc` and this one), so the repayment is
  computed once, at construction. Whether construction precedes or follows
  `.sam` loading was not traced, and determines which inputs apply.
- Early payoff (`0xccc78`): `amount = monthly × (term − months_repaid)`
  (accepted). Two operand-level edge cases: the affordability test is
  `cmplw` (**unsigned**), so a negative balance passes it, and the debit
  happens only if `mWithdrawalsEnabled ≠ 0`, while the loan is cleared in
  either case. Anyone modelling payoff should treat both as original Mac
  behaviour, not as bugs to normalise silently.

### PC save cross-check: critical objection to `SaveEconomyRecords`

The serializer writes 4-byte little-endian words in its call order
(`mAdmissionFee, mBalance, mBatchBalance, mWithdrawalsEnabled, mLastBalance,
mTurnEnteredRed, mProfitThisYear`, then 8 × `available, amount, APR, months,
monthly, bought, months_repaid, lender`). Every reader helper uses
`LbFile_Read` of 4 bytes followed by `stwbrx`. Applied to the PC
`Easymode.TPWI` payload, this layout matches uniquely at payload offset
**1,411,362**, which is 32 bytes before OpenTPW's "loan table" offset
1,411,394:

- The bank block is `25, 87987, 0, 1, 87787, 0, −12013`: admission fee 25,
  balance 87,987, batch 0, withdrawals enabled, last balance 87,787,
  entered-red tick 0, profit this year −12,013. This resolves the "money not
  reconciled" words in [ECONOMY.md](../ECONOMY.md): the "i64 87987" is
  `mBalance` plus `mBatchBalance = 0`, and the "i64 87787" is `mLastBalance`
  plus `mTurnEnteredRed = 0`. Balance = 100,000 − 12,013 is consistent with
  profit-this-year. Why 12,013 is odd is still unexplained.
- `loan_available` is 1 only for the 10,000 loan, as the Mac constructor rule
  predicts. The "unknown last word 0,0,1,0,0,0,0,6" is the **next record's**
  `loan_available` (plus the following field after record 7). It is not part
  of the record.
- `SaveEconomyRecords` reads `amount` as an **i64**. The high word is really
  `APR_in_percent`, which is 0 in this easy save. For any APR > 0 save the
  "amount" becomes `APR·2³² + amount` > 100,000,000, so `IsLoanRecord`
  rejects it and import fails with "no loan table". Its locator also requires
  `lenderNameIndex == record index`, which holds only because the easy
  balance names lenders 0–7. Themes that rename `LoanInfo[n].Lendername`
  would also fail. These are the record-reading part of **ECON-045**. The
  synthetic test `test_positive_apr_separates_the_readings` demonstrates the
  divergence. Owner: root/economy integration. This reviewer did not edit
  the source.

## 3. Calendar lead (constructor `0xe3c90`, conversion `0xe4394`)

**Accepted:**

- `this+0x1c = 15000`, labelled `mFunnySecsPerRealSec`. The other labels are
  `+0x0 mFunnyTimeStart`, `+0x8 mSessionStart`, `+0x10 mMonthAtLastUpdate`
  and `+0x14 mDayAtLastUpdate`.
- `mFunnyTimeStart` = **2000-01-01 00:00:00.000** (`0xe4348`).
- The conversion is `funny = start + floor(tick × mFunnySecsPerRealSec / 4)`
  seconds, built as a `TbTimeDiff` in 100 ns units. The unit is verified:
  `TbTimeDiff::Set` (bullfrog `0xe618`) builds milliseconds with constants
  60,000, 3,600,000 and 86,400,000, then multiplies by 10,000.

**Corrections:**

- The divide at `0x1c4100` is the **signed** 64-bit helper. Its code range is
  identical to bullfrog `0x59e88`; the unsigned copy is at `0x1c4010`. The
  inputs are non-negative, so the result is unchanged.
- The "turn" counter at world `+0x1da70c` is labelled **`mGameTick`** by the
  `CWorld` serializer (labels at code `0x1d369c + 1158`). There are only two
  direct stores: a reset to 0 at `0x104900` and `++` at `0x105398`, at the
  start of function `0x10536c` (called "world tick" below). Loading a save
  also writes the field through the serializer's reader.

**New cadence lead. Needs clock-lane confirmation; not yet behaviour proof:**

- The main loop (`0x1c22b4…0x1c24cc`) is a fixed-step accumulator. It reads
  a game clock, clamps lag to 2,000 units, and then loops
  `while (now > acc) { acc += 31; step++; …; if ((step & 7) == 0) dispatch world tick }`.
- The world tick is reached directly in mode 2 (`0x1c23b8`) or via `0x10565c`
  in mode 1.
- The game clock (`0x10e844 → … → 0x127cd0`) accumulates
  `(LbTime_GetClock ms delta) × rate`, where the rate is a double at
  `clock+0x18`, and saturates to u32. It also has a frozen branch, `+0x2c` →
  `+0x30`, a likely pause mechanism (unconfirmed).
- At rate 1.0 this gives **248 ms per world tick (≈4.03 Hz)**. That is
  consistent with the calendar's `/4`, which assumes 4 ticks per real second.
  It also gives 3,750 funny-seconds per tick, so one game day ≈ 23.04 ticks
  ≈ 5.71 s.
- Before these numbers replace any approximation, the following need
  evidence: the initial and speed-setting values of `rate`; which mode value
  single-player uses; the gating inside `0x10536c` (it tests world
  `+0x1da738 == 4`, and there is a `mGameTick % 30` branch); and how
  months/days are derived from `mMonthAtLastUpdate`/`mDayAtLastUpdate`.
- The current OpenTPW approximation "1 day = 240 ticks (4 s)"
  ([ECONOMY.md](../ECONOMY.md)) conflicts with this lead. This review does
  not change it.
- The 100 ms `UTimer` (`this+0x5c`) is **not** on this path. It must not be
  cited as the simulation rate.

## Mac/PC transfer

The PC `TP.ICD` (SafeDisc-wrapped) contains none of these serializer or
schema strings in its stored form, so PC code identity cannot be checked
statically here. What transfers is limited to:

- **Save layout:** accepted as cross-variant for the bank block. A PC save
  matches the Mac serializer order field-by-field, and the stored
  `monthly_repayment` values equal the Mac formula.
- **APR > 0 arithmetic, availability updates after start, payoff and
  calendar cadence on PC:** need evidence. The only PC save has 0 % APR, for
  which every candidate formula reduces to `floor(P/N)`. A PC standard-mode
  save with an outstanding APR > 0 loan, or a PC runtime capture, is
  required.

## Classification summary

| Item | Status |
| --- | --- |
| PEF reader on this corpus; timer wrapper ÷1000 unsigned quotient; SetRate copy; Microseconds fallback (conditional) | Accepted |
| `HasTicked` semantics; 500,000 at app `this+0x70` (role unknown) | Accepted (new) |
| Loan formula, field names, `u32sat` conversion, availability rule, payoff amount and edge cases (Mac) | Accepted |
| `.sam` keys → settings `+0x198…` mapping | Needs evidence (parser untraced) |
| Bank save layout on PC; `SaveEconomyRecords` i64/lender-locator misread | Accepted / **critical objection** |
| Loan APR>0 arithmetic on PC | Needs evidence |
| Calendar constant, labels, epoch, conversion, 100 ns unit; `mGameTick` identity | Accepted |
| "unsigned divide" in calendar conversion | Rejected (signed; no numeric effect) |
| 248 ms / ≈4 Hz world tick and 5.71 s game day | Lead; needs evidence (rate, mode, gating) |
| 100 ms UTimer as simulation clock | Rejected as unsupported |
| Relocation repeat semantics | Untested (no instances in corpus) |

## Other lanes at round-1 time

At round 1, no domain lane had committed. Round 2 below reviews their commits.

# Round 2: committed domain lanes

Reviewed commits: clock `126ee3e`, economy `14dff87`, guests `334c61d` +
`f7041da`, rides `51425bd`, advisor `c08762e`. UI (`ppc-ui`) and formats are
uncommitted, so their claims below are **tentative**. Scenarios has no files.
Every committed helper exits 0 on the pinned binaries. Every lane's tests pass,
including the original-file tests when their environment variable is set.
Uncommitted edits seen in the economy, guests and rides trees were not reviewed
as findings. None of them addresses the objections below. Nothing outside this
lane was edited.

Every address below was re-read from decoded instructions. Witnessed items
are pinned by `round2_evidence.py`. Function extents come from prologues,
`blr` and explicit branch targets. No heuristic boundary was used.

## 4. Clock lane (`126ee3e`)

**Accepted:** the catch-up loop is reached as game-state case 10 (`0x1c135c`
bound 15, jump table `data:0x52cc8[10]` → `0x1c2264`). Its register globals
come from the single prologue: there is no loop back to the dispatch `bctr`.

- Backlog: signed `now − previous > 2000` sets `previous = now − 2000`.
- Each substep runs `previous += 31` and `counter += 1`.
- Even substeps call `0x63dd0`/`0xb7ac0`. Every substep where `counter & 7 == 0`
  allows at most three park turns per callback. The cap counter `0x15c6b4` is
  reset at `0x1c27a8`. Verified: no branch in `0x1c24d0–0x1c27a8` escapes,
  so the reset runs on every case-10 path.
- Forced stepping: `1000/32` with `divwu` = 31. Requested when global
  `0x15c490 ≠ 0`; otherwise `0x10ecc0` ends forced mode.
- Scale: unsigned ms delta. Initial value 1.0 (`data:0x5710`), steps ×/÷ 1.25
  (`0x5708`), clamp [0.25, 2.0] (`0x5700`/`0x56f8`).
- Animation: frame = `(now−start)·30/1000·speed`, channel flag `0x40` selects
  the unscaled clock. Clip duration `(end−start)·1000/30` and
  `end·1000/30` (multiply by 1000, magic-divide by 30).
- RSE manager: budget `+148→+152` is checked **before** each opcode, the
  ID/pass phase filter is low 3 bits, and `+184` bypasses it.
  WAIT/WAITABS/SETTIMER/GETTIMER arithmetic as stated. Speed =
  `0.5 + int16(+192)/100`.
- Opcode names are native, not OpenTPW's enum. The binary holds a 106-entry
  (name, argument-spec) table at `data:0x3ef20`. Indices 1/2/6/7/44/45/95/96
  are CRIT_LOCK, CRIT_UNLOCK, ENDSLICE, GETTIME, WAIT, WAITABS, SETTIMER,
  GETTIMER. This matches the dispatch range 0..105.

**Additions and corrections:**

1. **Unreported skip gate.** At `0x1c22f4–0x1c2308`, when
   `!(flags 0x52e44 & 8) && (flags 0x52e40 & 1)`, the substep does **no**
   work. However, `previous` and the substep counter have *already* advanced,
   so the eight-phase alignment continues through skipped substeps. What the
   two flag words mean is unresolved.
2. **Turn increments before the mode-4 exit.** In `0x10536c`, `mGameTick++`
   (`0x1053a0`) precedes the `world+0x1da738 == 4` early exit (`0x1053a8`).
   In that mode the calendar, which reads the tick, keeps advancing while no
   thing is updated. My round-1 phrase "mode 2 direct" is corrected: modes 0
   **and** 2 call `0x10536c` directly, mode 1 uses `0x10565c`, and other modes
   skip park work.
3. **Zero or negative header budget executes no opcodes.** The manager checks
   `budget > 0` before the first dispatch (`0xb28e8 → 0xb290c`). This closes
   the clock lane's "zero/negative budget" boundary for the Mac build.
4. The loop condition, LIP and mouth deadlines are **signed** (`cmpw`);
   WAIT and WAITABS use unsigned `cmplw`. Wrap behaviour after 2³¹ ms is therefore
   Mac-specific. This is not a practical concern, but it should not be
   modelled as unsigned.
5. With the clock lane's proofs the cadence lead from round 1 becomes
   **accepted for the Mac build at scale 1.0**: 248 ms of scaled clock per
   park turn, 3,750 calendar seconds per turn, and ≈ 5.71 s per game day. It
   still depends on the scale (0.25–2.0), forced stepping, the skip gate and
   the mode-4 tick. It is not a PC claim.

## 5. Economy lane (`14dff87`)

**Accepted** (re-derived): skill, research, wear, scrap, game over and event
routing.

- **Skill** `trunc(20·(grade + pct/100))`. Because the routine computes in
  single precision, I checked every grade 0–5 with percentage 0–255; the
  result equals `floor((100·grade + pct)/5)` on all of them (synthetic test).
- **Research** every `turn % 20 == 0` (`0xcccccccd` magic) outside
  states 3/4/5.
- **Wear** is entered on `turn & 7 == 0`, with an inner `turn % 64` gate.
  These are global-turn tests, not per-object phases: the world tick calls
  the type dispatcher `0xfa9b0` for every live thing each turn.
- **Scrap** divisors are exact: 365 days (`0x11ED178C6C000` × 100 ns) and
  30 days (`0x1792F8658000`). The signed quotient's low word is compared, and
  negative ages fall into the "other years" bucket.
- **Game over** happens when `(now − timestamp(mTurnEnteredRed)) / 30 days ≥ 6`
  at a month event while the balance and last balance are negative.
- **Event routing:** the message types come from native class names and
  getters (`0x41048/0x41024/0x41000` → `CMsgEndOfDay` 11, `CMsgEndOfMonth` 12,
  `CMsgEndOfYear` 13). The calendar emits them at `0xe3fb0/0xe4088/0xe4148`.
  The bank routes 12 to `0xcc21c` and resets `mProfitThisYear` on 13.
- **Loan apply** (`0xcc904`) and the payoff amount are as stated.

**Critical objection: the yearly-profit rule is wrong whenever `M·N < P`.**
At `0xcc370–0xcc390` the month handler computes
`profit += M − divwu(M·N − P, N)` in 32-bit arithmetic. Combined with the
`−M` withdrawal, the net is `−divwu(M·N − P, N)`. `divwu` is **unsigned**, so
whenever `M·N < P` the dividend wraps. The lane's "reduces yearly profit by
`floor((M×N − P)/N)`" is correct only when `M·N ≥ P`.

`M·N < P` occurs for every 0 % APR offer whose principal is not a multiple of
its term. PC `Easy_Standard.sam` (`855ca41c…`) is 0 % for all eight offers,
and six of them wrap:

- 100,000, 50,000, 25,000 and 10,000 over 36 months each give
  −119,304,646 per month.
- 80,000 over 48 months gives −89,478,484.
- 65,000 over 30 months gives −143,165,575.
- 18,000 over 24 and 30,000 over 30 are exact, so they give 0.

This is static Mac behaviour (an apparent original bug), not a PC claim.
Whether `mProfitThisYear` is displayed or used beyond the year reset was not
traced. Any Mac-backed model must keep the u32 wrap explicit or record a
documented deviation. The independent model is
`round2_evidence.loan_month_profit_delta`.

Further additions:

- **Batch flush before the loan loop.** The month handler first moves
  `mBatchBalance` (`+16`, assert < 1,000,000) into balance, the game
  statistic and `mProfitThisYear`, then zeroes it (`0xcc24c–0xcc2a0`). The
  lane omits this.
- **Withdrawals disabled.** The flag gates only the balance debit and the
  `−M` profit write. `months_repaid` still increments and the `+M − q`
  adjustment still applies, so profit *rises* each month.
- **Payoff profit.** Because `months_repaid` is cleared before the
  adjustment, payoff changes profit by exactly `−q·N`. That is the whole
  loan's interest, regardless of how many months were already charged
  (synthetic test). The lane flagged the ordering; this quantifies it. The
  same unsigned wrap applies.
- **Repayment termination is equality-only** (`cmplw repaid, N; bne`). With
  N = 0, the saturated `M = 0xffffffff` debits −1 per month forever.
  Ordinary SAMs do not trigger this.

## 6. Guests lane (`334c61d`, `f7041da`)

**Accepted:**

- **Nested needs guard.** The ID-phase mismatch at `0xeed40` exits to `0xef164`,
  past the mod-16 block at `0xeeeb0`. Fixed increments are toilet +1 and
  hunger/thirst +2 (floats `data:0x55c4/0x55b4`), clamped to [0,100]. They
  therefore need `turn % 16 == 0 ∧ id % 4 == 0`.
- **Sequential IDs.** The allocator initialises IDs 1..10239 in ascending
  order (two per iteration, 5,119 iterations, plus the last at `0x104f28`)
  and pops from the head of the free list.
- **Distance, speed and RNG domains.** The distance divisor is exactly signed
  ÷450 (`0x91A2B3C5`, `srawi 8`). The speed setter caps at 2.0, scales by
  0.4/0.2 × 65,536 and floors at 655. The RNG start domains are as stated.

**Additions:**

- **Overfull queues give a negative match.** At `0xe9400` the queue match is
  `100 − divwu(queue·100, 4·max(f60,1))`. Within `0xe9404–0xe96d8` the only
  other writer of r22 is the far-distance zeroing at `0xe9414`. So when
  `queue > 4·f60` the match is negative, and the weighted sum is divided with
  `divwu` (`0xe9714`). A negative total therefore becomes a huge score
  instead of a low one. Whether a queue can exceed `4·f60` depends on the
  acceptance limit (the flagged case uses 100). Needs evidence before modelling.
- **Update order is newest first.** The allocator pushes each new slot at the
  live-list head (`0x1052c0`), and the world tick walks from the head. This
  replaces the open "all guests processed in list order" assumption with a
  concrete Mac order. Save-loaded rebuild order was not traced.
- The speed update `0xe6b28` (the lane's 99/100 decay; value not re-derived
  here) is called at `0xeed0c`, every turn, before the ID-phase guard.

## 7. Rides lane (`51425bd`)

**Accepted** (re-decoded):

- COPY writes the accumulator.
- DIV and MOD with a zero divisor set the accumulator to 0. Otherwise they
  use signed `divw` and the C remainder, and the result is stored only for a
  variable destination.
- Literal operands are sign-extended to 16 bits.
- BUMP13 multiplies by 30 and BUMP14 negates.
- The entrance coordinate is ×255.0 (`data:0x54c0`) truncated to a byte.
- The per-mille TRIGANIMSPEED passes `script_speed × op / 1000`.
- The .NET witness has only a project reference, no packages.

**Additions:**

- **Literal-destination COPY does not "discard".** It returns before fetching
  its source operand (`0xaf604 → 0xb2354`). The next dispatch reads that
  operand as an opcode word and fails the `0x80` tag test (`0xaf5b0`). The
  script PC then becomes −10000 and the script stops. Before relying on any
  generic literal-destination rule, scan the corpus for this form.
- **RAND's bound is not resolved.** `0xb07f8` sign-extends the raw operand
  word and never calls the resolver, so a variable bound uses its encoding
  bits. RAND computes `labs(labs(rng>>1) % (bound+1))`. A bound of −1 divides
  by zero (undefined on PPC).
- **Animation waits use `max(ret − 300, 300)`.** TRIGANIM `0xafb20`, WAITANIM
  `0xafc78` and TRIGANIMSPEED `0xb007c` all apply this. TRIGANIMSPEED's
  deadline is `now + acc·1000/op` with signed `divw`, using the operand only
  and not the script speed. It also stores the operand as **int16** at
  `+228`. Neither the rides nor the clock lane states the 300 ms adjustment.
  What `0xa6cc0` returns, and in what units, needs evidence before modelling.

## 8. Advisor lane (`c08762e`)

**Accepted:** LIP mark ÷1000 (signed magic `0x10624dd3`, `srawi 6`). At
most one toggle per update, when the current time is strictly greater than
the mark deadline. Mouth `rand() % count + 1` with a deadline of now + 100,
so Normal is reachable. The maximum and minimum scans use strict comparisons,
so ties keep the earliest slot.

**Upgraded.** The lane pairs the Mac response table with the *PC* speech
bank. The Mac `hfs.img` contains exactly one contiguous byte-identical copy
each of PC `speechHD.SDT` (25,205,152 B), `lips.wad` and `cat_speechSFX.map`
(`speech_identity`). The global sample/LIP numbering is therefore the same
data on both platforms. Which path the Mac loader opens was not traced.

**Needs evidence:** the mouth count comes from `+272`; the value 5 depends
on all five names resolving.

## 9. UI lane (uncommitted, tentative)

The scale routines and clamp are confirmed (section 4). **Missing:** a third
callback record `data:0x452f8` → vector `0x7518` → `0x1131ac` *sets* the
scale to 1.0 (`data:0x56d8`) through `0x127c40`. So the original has
step-down, step-up **and reset** controls. Key binding and widget mapping
remain unresolved.

## 10. Round-2 classification

| Item | Status |
| --- | --- |
| Clock: case-10 loop, 2000 backlog, 31 ms substep, 8-phase, cap 3 per callback, forced step 31, scale 1.0/1.25/[0.25,2], 30 fps formula, RSE budget/phase/WAIT/WAITABS/SETTIMER/GETTIMER, native opcode names | Accepted (Mac) |
| Skip gate advances counter; tick increments in mode 4; modes 0/2 direct; zero budget executes nothing | New, accepted (Mac) |
| 248 ms/turn, 5.71 s/day at scale 1.0 | Accepted (Mac, scale 1.0); PC needs evidence |
| Economy skill/research/wear/scrap/game-over/event types/apply/payoff amount | Accepted (Mac) |
| Economy yearly-profit rule `floor((M·N−P)/N)` | **Rejected for M·N < P** (unsigned wrap; affects 6 of 8 PC Easy offers) |
| Batch flush, withdrawal-disabled profit gain, payoff = −total interest, equality-only termination | New, accepted (Mac) |
| Guests nested guard, sequential IDs, ÷450, speed setter | Accepted (Mac) |
| Negative queue match → unsigned score | New; boundary reachability needs evidence |
| Newest-first update order | New, accepted (Mac live list) |
| Rides COPY/DIV/MOD/literal/BUMP/×255/TRIGANIMSPEED | Accepted (Mac) |
| Literal-destination COPY aborts script; RAND raw bound; 300 ms anim adjustment | New, accepted (Mac); `0xa6cc0` units need evidence |
| Advisor ÷1000, single toggle, mouth rand, strict ties | Accepted (Mac) |
| Global speech bank identical Mac/PC | Accepted (asset identity) |
| UI speed reset callback | New, accepted (Mac); UI binding needs evidence |
| PC runtime equivalence of any item above | Needs evidence |

## 11. Hand-offs (no source edited by this lane)

- **Economy:** amend the profit rule for `M·N < P` and add the batch flush and
  the withdrawal-disabled branch. Keep the u32 wrap explicit in any
  Mac-behaviour model.
- **Root/economy:** the round-1 `SaveEconomyRecords` misread is still open.
  It needs a separate patch.
- **Guests:** bound the queue-count domain (the acceptance-limit consumer)
  before modelling attraction scores.
- **Rides:** run a corpus scan for COPY with a literal destination and for
  non-literal RAND bounds. Recover the units of `0xa6cc0`'s return.
- **Clock:** label the flags `0x52e40`/`0x52e44` and the world `+0x1da738`
  mode.
- **Integration:** keep every item above tagged Mac-static. The PC
  `TP.ICD` was not inspected, and the PEF repeat-relocation path remains
  untested because the corpus has no instances.

# Round 3: shared-reader and source fixes, formats and scenarios

Reviewed commits: clock `3b9b322`, `068a076`, `dd903c6`, `3995687`,
`dafbdb1`, `a5263bb`; economy `7b63c41`, `ab3c74d`; rides `15313d2`,
`b3d14f9`; guests `5e6e346`, `00a6393`; advisor `3acb53d`, `3ae2dd9`; UI
`8c70ff5`, `67e7d93`; formats `b0935b4`; scenarios `b8602bd`. Uncommitted
Phase 3 files in the advisor, economy, formats (`source/` model files) and UI
trees are **not** reviewed here. Source commits were exported with
`git archive` into scratch directories for building, so no lane tree was
touched. New address claims are pinned by `round3_evidence.py`, with synthetic
regressions in `test_round3.py`.

Runs: every lane's Python suite passes at its HEAD with the Mac paths set
(clock 21, economy 8, guests 11, rides 15, advisor 26, UI 24 with private corpus
paths and no skips, formats 24, scenarios 11). The shared toolkit at clock HEAD
passes 31 tests. .NET: economy/save filter 42 pass and 0 skip
(`OPENTPW_GAME_PATH` set), ride VM filter 34 pass. Standalone helpers: clock 33,
guests 18, advisor 21. No helper project restores packages. The clock one
clears all NuGet sources, and the rides one references only
`source/OpenTPW.Files`. The rides PC witness JSON files contain counts and
SHA-256 digests only.

## 12. Shared toolkit fixes (clock lane, root-owned files)

**Boundary:** `pef.py`, `test_pef.py`, `timer_evidence.py` and
`test_timer_evidence.py` are shared toolkit files. The lane brief forbids
child edits to them. The content is accepted below, but root must take
ownership and merge them as one unit before any lane that imports them.

- **PEF repeat (`068a076`): accepted.** Apple's Mac OS Runtime Architectures
  (RTArch-98) states that both repeat forms repeat "the preceding blockCount
  relocation blocks". It defines relocation blocks as 2-byte units, prohibits
  nesting, and stores SmRepeat counts −1 and LgRepeat's repeatCount as the
  actual value. The reader now selects a block window and rejects split
  instructions and nesting. Round-1 wording already said the spec counts
  2-byte chunks, so the fix aligns the reader with that and no review claim is
  reversed. Independent checks:
  - Main's unpatched reader and the fixed reader both reproduce all 16
    relocation digests in `relocation_baseline.json`.
  - The new `test_pef.py` fails against main's reader (8 failures, 5 errors),
    so the regressions discriminate.
  - Residual: whether the original instructions run once plus repeatCount
    more times is not settled by the text or by the corpus. The corpus still
    contains no repeats.
- **Glue (`3995687`): accepted.** The six-word encodings were checked by hand
  (`mtctr r0` = `0x7C0903A6`, `bctr` = `0x4E800420`). An independent
  byte-pattern scan finds exactly **2,209** standard stubs in 16 containers,
  equal to the lane count.

## 13. Source fixes

- **Save loans (`ab3c74d`): parser accepted.** The parser now reads eight
  32-bit words and a 28-byte bank prefix, matching round 1. Lender is a field,
  not the record index. The formats lane shows that the Mac HFS
  `Easymode.TPWI` is byte-identical to the PC fixture (`6d89303d…`), so the Mac
  serializer field order applies to this exact file.
  **Medium, need-more-evidence:** for APR > 0, `OriginalEconomyImport` still
  requires `MonthlyRepayment == LoanMath.MonthlyRepayment(...)`, an OpenTPW
  annuity approximation (`[APPROX:ECON-006]`). For 10,000 at 10 % over 24
  months the round-1 Mac reading gives **458** and the annuity **461**. An
  original positive-APR save would therefore throw "differs from LoanInfo"
  rather than "no loan table". That is not a regression, but positive-APR
  import is still not supported. The cross-check test builds its expected
  value with the same `LoanMath` call, so it is self-consistent rather than
  evidence. Recommendation: compare repayments only at 0 % APR, or report a
  mismatch for APR > 0 without failing, until a PC positive-APR fixture
  exists.
- **VM (`b3d14f9`): accepted except one low item.**
  - COPY: a literal destination exits at `0xaf604` without fetching the
    source. The next dispatch requires opcode tag `0x80`
    (`0xaf5a4–0xaf5b0`). RSE operand kinds are tagged
    `0x00/0x10/0x20/0x40`, so a parsed source operand can never pass, and the
    immediate `Abort` (PC −10000) is equivalent. COPY sets the accumulator.
  - DIV/MOD zero → 0 was re-confirmed in round 2.
  - **Low: RAND negative bounds.** `0xb0804–0xb0820` computes
    `labs(x − trunc(x/(b+1))·(b+1))` with `x = gen >> 1` and `b = extsh(raw)`.
    Because the remainder uses `divw`/`mullw`/`subf`, **b = −1 returns x
    (0…2³¹−1)** whatever `divw` yields. This corrects round 2's "undefined".
    For b ≤ −2 the result is 0…(−b−2). OpenTPW throws on any negative bound.
    The corpus has none (56 RAND, all non-negative literals; lane test), so
    only mods are affected. Implement the defined Mac result or keep the
    throw labelled as an unqualified domain.

## 14. Formats lane (`b0935b4`, new)

Witness exits 0 on the pinned containers. `corpus_check.py` gives
byte-identical output on the PC baseline and Patch 2.

- **MD2 gating: accepted.** `r27 = flags & 1` at `0x3f9f0` and `flags & 2` at
  `0x3fcec`. The loader import has exactly two direct callers, `0x58f04`
  (flags 0) and `0x99ad8` (6 or 2), so mask `0x1` is never set and major-207
  meshes return DeadMesh. **Low:** the doc's "bit 1"/"bit 2" means masks
  `0x1`/`0x2` (LSB-0 bits 0/1). Restate the gates and trailer/option "bits"
  as masks before register or code use.
- **TPWS trailing delimiters: accepted.** `0x11cc28` calls the World writer
  (`0x105d3c`). Only then does `0x11cc58/0x11cc60` build `0x57524C44` (WRLD),
  which `LbFile_Write` stores swapped as `DLRW`. This contradicts
  `docs/TPWS-PAYLOAD.md`'s section attribution. Economy `ab3c74d` also edits
  that file; the bank prefix and loan table are inside the World block under
  either reading. Merge economy first, then rewrite the section table in one
  root change.
- **BF4 blend: accepted (Mac).** `0x88888889` comes from
  `addis 5,0,-30583; addi 27,5,-30583`. Then `mulhwu`, `>>3`, add and a byte
  truncate (`0xb574–0xb584`) give 255 → 17 at full coverage for C < D, and
  `floor(n(C−D)/15)` for C ≥ D. The PC render path is unknown.
- **MTR absent from the Mac build: accepted (bounded).** An independent scan
  of 8.1 MB of section data finds no `AF 15 59 2E` in either byte order and no
  `.mtr`. The same scan does find `.mov` 9 times.
- **Other claims: accepted as Mac facts, not re-derived.** These are the
  30 ticks/s animation clock (consistent with round 2), MAP ignored words and
  axes, cell schema 84/94, easing scale, key search and Bézier. The doc keeps
  PC equivalence open.

## 15. Scenarios lane (`b8602bd`, new)

Witness (374 checks) and tests pass. Strengths: the GameType 0/1/2 table,
strict `>` ticket predicates, keys not consumed, only researcher staff produce
points, and challenge activation.

- **Offsets: accepted, with independent confirmation.** I ran economy's
  descriptor walker (`schema.fields`, table `data:0x34d10`) independently. It
  yields Visitors…RecentVisitorMonths at 1872–1896, CoasterHeight…
  MinCellsCovered at 1900–1916, `DaysUntilFirstChallenge` 1928 and
  `ResearcherConstsPerGrade.ResearchAbility` 1052 (stride 12). So the
  big-park ticket reading **MinCellsCovered (1916)** is layout-proven, not
  only "medium-high".
- **Medium: research threshold index.** The same walk puts
  `ResearchTech[0].PercentageForThisTech` at **+1276**: descriptor 165 inside
  array 164–166, stride 4, capacity 10, with a trailing count word. The
  opening loop reads `balance+1280+4g` (`0xf1670–0xf167c`), so the threshold
  for opening g+1 is **`ResearchTech[g+1]`**. The percentage uses `divwu` and
  the compare is `cmplw`, both unsigned. With the shipped `0,0,80,85,85`,
  group 1 opens at 0 %, group 2 at 80 %, group 3 at 85 % and group 4 at 85 %.
  A same-index reading (`threshold[g] = ResearchTech[g]`) would open group 2
  immediately. ECON-016 must use g+1. Opening group 5 depends on
  `ResearchTech[5]`'s parser default, which is unverified.
- **Low-medium: ticket counter.** `world+0x1E0000−22772` is `mGameTick`, the
  field incremented at `0x1053a0` (round 1). It is not only "zeroed at world
  init": save loading restores it (formats: 755 in Easymode). The `== 4` test
  at `0x1053a8` reads `world+0x1E0000−22728`, a world-state field, not
  GameType (`data:0x53d98`). Do not merge these "modes".
- **Calendar dependency: resolved from round 1.** `calendar+0x1c`
  = 15000 (`mFunnySecsPerRealSec`), so a game day is 23.04 ticks, a 30-day
  month 691.2 ticks, and `DaysUntilFirstChallenge` 540 ≈ 12,442 ticks. The
  ticket check every 100 ticks runs about every 4.34 game days (24.8 s at
  Mac speed 1.0). This holds only if no `.sam` or save overrides `+0x1c`,
  which has not been traced.
- **Low: happiness ticket wording.** The witness proves only that the second
  test calls the same statistic as Local 1 (`0xc3684`) against
  `AtLeastThisManyHappyPeople` (1884). "People in park" is inferred from
  Local 1's field name, and the doc itself lists `0xc3684` semantics as not
  established. Phrase it as "same statistic as Local 1".

## 16. Other lanes, delta since round 2

- **Clock:** accepted. My round-2 additions are absorbed: the exclusion gates
  `0x52e44&8`/`0x52e40&1`, the cap dropping park turns, and `mGameTick` at
  `0x1053a0`. **Retracted in round 4 (section 18):** the gate polarity and
  mode routing in `a5263bb` were wrong, and this line accepted them without
  re-checking against my own round-2 witness, which had both right. The widget clock is raw milliseconds through the unsigned
  divider. Zero time-slice behaviour stays open (conservative).
- **Economy `7b63c41`:** accepted. The unsigned `(M·N−P)/N` wrap is
  documented (119,304,646). The descriptor-to-runtime proof is the anchor
  used in section 15.
- **Guests `00a6393`:** accepted. `QueueTerm` is
  `100 − divwu(queue·100, 4·max(cap,1))`, matching round 2. It is isolated,
  with no production wiring. **Boundary:** it edits `docs/GUESTS.md`
  (outside the lane).
- **Advisor `3acb53d`, `3ae2dd9`; UI `8c70ff5`, `67e7d93`:** suites pass. No
  new contradiction was found in what was spot-checked. Their newer
  uncommitted files are tentative.
- **Test hygiene (low):** `OPENTPW_MAC_APP` names a *file* in rides but a
  directory elsewhere. Setting it to the bin directory makes two rides tests
  error.

## 17. Round-3 classification and integration order

| Item | Status | Severity |
| --- | --- | --- |
| PEF repeat block fix, glue validation, relocation baseline | Accepted; out-of-lane files | Boundary |
| Save parser 8-word loans + 7-word bank | Accepted | — |
| Importer APR > 0 repayment equality vs annuity | Need more evidence; self-consistent test | Medium |
| VM COPY abort, DIV/MOD 0, raw RAND bound | Accepted (Mac) | — |
| VM negative RAND bound throws | Rejected as stated; Mac result defined, corpus-unreachable | Low |
| MD2 gating, TPWS trailing tags, BF4 blend, MTR absence | Accepted (Mac) | — |
| Formats "bit n" notation | Restate as masks | Low |
| Scenarios ticket/challenge offsets incl. MinCellsCovered | Accepted, layout-proven | — |
| Scenarios research threshold = `ResearchTech[g+1]`, unsigned | Correction | Medium |
| Ticket counter = `mGameTick` (save-restored); state 4 ≠ GameType | Correction | Low-medium |
| Calendar multiplier for park age | Resolved via round 1 (override untraced) | — |
| Happiness ticket "people in park" | Overstated | Low |
| Any PC/Patch 2 runtime equivalence | Needs evidence | — |

Recommended safe order (root):

1. Shared toolkit as one unit: `068a076` → `dd903c6` → `3995687`.
2. Clock lane files (`3b9b322`, `dafbdb1`, `a5263bb`).
3. Rides `51425bd` → `15313d2` → `b3d14f9`, with the RAND-negative note
   recorded.
4. Economy `14dff87` → `7b63c41` → `ab3c74d`, then an importer follow-up
   for APR > 0.
5. Formats `b0935b4` (lane-only), then one root change to
   `TPWS-PAYLOAD.md`/`SavePayloadLayout` for trailing tags, after step 4.
6. Scenarios `b8602bd`, with register updates using the section 15
   corrections.
7. Guests, advisor and UI lane commits (independent).

Uncommitted source edits (formats `ModelAnimation*`, economy loan rules) need
their own commit and review round before integration.

# Round 4: clock branch repair, loan model, MD2 tracks, scenarios, advisor, UI, TPI

Reviewed: the clock repair on top of `a5263bb` (doc, contract,
`native_scheduler_branches.py`, `NativeBranchRules.json`), reviewed uncommitted and
since committed as `bbc89e5`. Its `tools/ppc-analysis` tree is byte-identical to
what I reviewed. `c2a0930` adds only a doc section on the rides VM, consistent
with section 13. Also reviewed: economy `6f0a0ea`;
formats `2439695`; scenarios `089ca5f`; advisor `8b256a3`; UI `643f132`; TPI
`873cb71`. Guests (`00a6393`) and rides (`b3d14f9`) have no new commits since
round 3. Every lane was run from a `git archive` export or an rsync copy in
`/tmp`, so no lane tree was written. New address claims are pinned in
`round4_evidence.py` (exit 0), with 22 regressions in `test_round4.py` that
tell the competing readings apart.

Runs: clock contract 35 checks, clock Python 21, and my regenerated branch JSON
matches the lane's `NativeBranchRules.json` exactly. Economy model 15 groups and
5,437 assertions. Formats witness exit 0, Python 26, MD2 C# filter 69 pass and
1 skip (the ISO/MTR case, which predates this commit). Corpus output is
byte-identical on the PC baseline and Patch 2. Scenarios witness exit 0 and 15
tests. Advisor 46 C# checks, 27 Python tests and the controller witness exit 0.
UI 19 synthetic cases, and the private verification (export written to `/tmp`)
covers 55 tables and 934 controls. TPI 9 C# fixture cases and 8 Python tests.

## 18. Clock branch repair (`bbc89e5`): accepted

I decoded the routes myself from the BO/BI fields, without reusing the lane's
JSON:

- `0x1c22f8 rlwinm. &8` and `0x1c22fc bc 4,2 → 0x1c230c`: flag 8 set jumps
  **into** the work block.
- With flag 8 clear, `0x1c2304 &1` and `0x1c2308 bc 4,2 → 0x1c24c0`: flag 1
  set skips. Work therefore runs when **flag8 OR NOT flag1**. Scheduled time
  (`+31`) and the phase (`+1`) advance first, at `0x1c22e0/0x1c22ec`.
- `0x1c2388 bc 12,2 → 0x1c23b4`: mode 0 takes the direct tick, and mode 2
  falls through to it. Mode 1 reaches the wrapper `0x10565c`, which rechecks
  `== 1` (`0x1056a0`) and calls `0x10536c`. Any other value skips.
- **New binding:** the mode word is the **GameType** object. r30 is loaded from
  TOC → data `0x53d98` at `0x1c1228`, and no integer write to r30 happens before
  `0x1c2380`. The wrapper uses the same slot. Using the scenarios lane's
  enumeration, Full Simulation (0) and Instant Action (2) tick directly and
  online (1) goes through the wrapper. Under the old contract, offline Full
  Simulation would never have advanced a turn. A regression shows that reading
  BO 12 as branch-if-false reproduces the superseded mode-0 claim exactly. The
  clock README's "names unresolved" can now cite this binding (low).
- **FMA: accepted.** `0x127d14` is `fmadd f0 = f2·f1 + f0` with delta, scale
  (`+24`) and the prior accumulator (`+16`). The delta is converted **unsigned**:
  the `0x4330` high word, minus the 2⁵² bias at `data:0x56f0`. One binary64
  rounding matches `Math.FusedMultiplyAdd(delta, Scale, Acc)`. Limit: the
  native FPSCR rounding and NI modes are not established.
- **Self-correction:** my round-2 witness already encoded both facts (skip only
  when `!(flag8) && flag1`, modes 0 and 2 direct). My round-3 "absorbed" line
  was a rubber stamp and is retracted. **Integration rule:** never merge
  `a5263bb` without `bbc89e5`.

## 19. Economy `6f0a0ea` (standalone loan model): accepted

The model matches my decode of `0xcc21c` and `0xccc78`:

- **Monthly installment:** the debit is skipped only when withdrawals are
  disabled. Repaid is incremented in every case. The `0xb678` assert comes
  first, then the `months == 0` guard (`0xcc368/0xcc36c`), then
  `profit += M − divwu(M·N − P, N)`. Completion is equality-only.
- **Payoff:** affordability uses `cmplw`. The routine clears bought/repaid
  (`0xccd74/0xccd78`) **before** reloading repaid for `q·(N − 0)`, which
  confirms the whole-term interest charge.
- **Constructor FP:** `fdiv`, `fmul`, `fadd`, `pow`, `fmul`, `fdiv`. There is
  **no fused operation** in `0xcb7a8–0xcb910`, so the host `pow` is the only
  libm dependency.
- The eight shipped `Standard.sam` offers sit at least **0.148** from an
  integer before truncation, so host-versus-original `pow` ulp differences
  cannot change them. Edge inputs (NaN, ∞, zero term) stay qualified.

**Still open (medium):** this commit does not repair the round-3 importer
objection. `ab3c74d` still demands annuity equality for APR > 0. Recommended
fix: use the Mac formula, which is safe for shipped values, or skip the
equality check for APR > 0.

## 20. Formats `2439695` (MD2 tracks, parser in `source/`): accepted with limits

- **10:10:10 layout, independently derived.** Engine `0x41cac–0x41d04`
  repacks each file word with `rlwimi`: X from `b0 | (b1&3)<<8` goes to memory
  bits 22–31, Y from `(b2&0xF)<<6 | b1>>2` to 12–21, Z from
  `(b3&0x3F)<<4 | b2>>4` to 2–11, and bits 0–1 are cleared. The application
  (`0xa43bc/0xa43cc/0xa43e8` + `srawi 22`) reads exactly those fields. My
  composite model reproduces the lane's little-endian 0–9/10–19/20–29 reading
  on 3,000+ words. A plain byte-swap reading fails.
- **Dequantise and lerp.** `fmadds q·scale + offset` (`0xa446c`; q is an exact
  int→float conversion), then `fmuls next·t` and
  `fmadds cur·(1−t) + that`, all binary32 with one rounding each. The C#
  `MathF.FusedMultiplyAdd` matches, and a regression shows that an unfused
  version differs.
- **Group 0.** `lower = (lerp − scale) − 0.25` (two `fsubs`) and
  `upper = 0.25 + (scale + lerp)`. The padding constant is 0.25 at TOC
  `0x519c`. The rounding order is observable and C# follows it.
- **Gates.** Trailer bit 0x2 set **and** option 0x8 clear enable texture frames
  (`0xa5768` `bt`, `0xa5778` `bf`). Option 0x2 **clear** selects slerp
  (`0xa824c`). The linear blend is fused (`0xa827c`).
- **Rotation partner:** first suspected and then cleared. `0xa820c` reads
  `count−1` from global `+16412`, not from the record. The only writer is
  `0xa50b4`, which stores the record's `+16` count just before. A scan shows
  exactly those two users, so `min(i+1, count−1)` stands.
- **Dispatcher.** `0xa4ad0`: cursors reset, and the static group applies, only
  while node flag 0x00800000 is clear. Group 0 is sampled on both paths. The
  cursor advances while `ticks[c+1] < time`, so an exact key keeps the earlier
  segment with t = 1. The fraction is `fdivs`. Toggles take `|v|` (`neg`, then
  `extsh`) unsigned against the tick, the last match wins, and positive clears
  0x10. The record is skipped when time is strictly greater than the clip
  duration.

**Objections:**
- **Medium (integration):** `ModelVertexGroup.FindKey` searches from 0 on every
  call. The original keeps its cursor while 0x00800000 is set, and who clears
  that flag is untraced. A looping clip whose time goes backwards would
  therefore extrapolate from the last segment natively. The C# doc states the
  non-decreasing-time precondition, so `ObjectAnimator` wiring must carry it
  explicitly or trace the reset first.
- **Low:** the commit message says the C# and Python checks "agree on 713,683".
  Python actually reports 713,695 over all 1,736 blocks against the raw
  group-0 box. C# reports 713,683 over the 1,735 paired blocks against the
  padded box. Both find 0 outside the box, so only the wording is wrong.
- **Low (unreported writes):** `0xa4f44` sets node-state 0x00010000 after every
  vertex pass, and `0xa5070` sets instance `+48` 0x00040000 after the vertex
  call. Neither consumer is traced.
- **Limit:** native FPSCR/NI mode and G3/G4 conformance of `fmadds` are
  assumed to follow the architecture. PC rendering is unknown.

## 21. Scenarios `089ca5f`: round-3 research objection resolved

- **Resolved.** The parser-derived layout puts `ResearchTech[i]` at 1276 + 4i
  (17 anchors), so `[1280+4g]` = `ResearchTech[g+1]`. The prose and ECON-016
  now say this. MinCellsCovered 1916 is unchanged.
- **Profit year: accepted.** `0xccfa8` returns `+292` and is compared with a
  **signed** `cmpw` at `0xd33fc`. Cross-lane, the round-2 0 %-APR wrap cannot
  reach this ticket with shipped data. The `Easy_` overlay (the only 0 % loans)
  applies to Instant Action, which runs no tickets, and the Full Simulation
  `Standard.sam` loans are 18–23 % APR. Low, mods only: two 0 % 100,000/36
  loans in GameType 0 would wrap yearly profit positive within 10 months and
  award the ticket spuriously (regression added).
- **Wage: accepted.** `0xf4760` multiplies `PayMultiplier[type]` (+832)
  by `BaseWage[grade]` (+748, ×16). The kind-to-index switch maps 5/4/6/7/8 to
  0–4, and anything else asserts and uses 5.
- **Still open (low):** the ticket counter text still says "zeroed at world
  init", when it is `mGameTick` and is restored from saves. The Local 2
  "people in park" wording is still inferred from Local 1's field name.

## 22. Advisor `8b256a3`, UI `643f132`, TPI `873cb71`: accepted

- **Advisor:** busy `cmplw now, start+dur`; repeat `cmplwi 0` sentinel, then
  `cmplw` with equality passing; revalidation `cmpw score ≥ min`; cyclic
  variant `cmpw` signed; duplicates `cmpw count < max`. All confirmed. The C#
  types are `uint` for the unsigned gates.
- **UI:** the factory checks `cmplwi 13` and dispatches through a 14-entry
  relocated table at `data:0x50098`. Entry 0 is `0x18020c` (`li r3,0`, returns
  null), which confirms the type-0 rejection. **Limit:** the C# reader's
  934-control agreement is with the same lane's Python decoder. That is
  agreement between two implementations, not an independent proof of
  semantics. The exporter refuses Git destinations, and its slices stay
  outside Git.
- **TPI:** the disc-root `Game.exe` (`17adfde8…`) is not the no-CD copy
  (`77ea0c41…`, 6,742,016 bytes in `WIN10FIX+NOCDFIX`). The evidence uses
  retail CABs and the root executable only. It makes no engine-equivalence
  claim, and its build output is ignored by Git.

## 23. Round-4 classification and integration order

| Item | Status | Severity |
| --- | --- | --- |
| Clock gate flag8 OR NOT flag1; GameType 0/2 direct, 1 wrapper | Accepted (`bbc89e5`); `a5263bb` alone rejected | Critical if split |
| Clock mode word = GameType `0x53d98` | New binding | — |
| My round-3 clock acceptance | Retracted | Self-correction |
| Scaled clock binary64 FMA, unsigned delta | Accepted (Mac) | — |
| Economy standalone loan model | Accepted (Mac) | — |
| Importer APR > 0 annuity equality (`ab3c74d`) | Still open | Medium |
| MD2 10:10:10, fused dequantise/lerp, group-0 order, gates, partner | Accepted (Mac) | — |
| Stateless `FindKey` vs native persistent cursor | Integration precondition | Medium |
| 713,683 vs 713,695 wording; two unreported flag writes | Correction/addition | Low |
| Scenarios research g+1, profit-year signed YTD, wage | Accepted / resolved | — |
| Ticket counter "zeroed", Local 2 wording | Still open | Low |
| Advisor queue edges; UI factory and reader; TPI boundaries | Accepted | — |
| Any PC/Patch 2 runtime equivalence | Needs evidence | — |

Safe order (root), updating section 17:

1. Shared toolkit as one unit (`068a076` → `dd903c6` → `3995687`).
2. Clock `3b9b322` → `dafbdb1` → `a5263bb` → `bbc89e5` → `c2a0930` as one unit.
   Never stop at `a5263bb`.
3. Rides `51425bd` → `15313d2` → `b3d14f9`.
4. Economy `14dff87` → `7b63c41` → `ab3c74d` → `6f0a0ea`, then the importer
   APR > 0 follow-up before any positive-APR import claim.
5. Formats `b0935b4` → `2439695` (parser only), then root's `TPWS-PAYLOAD.md`
   change. Wire `ObjectAnimator` later, carrying the cursor precondition.
6. Scenarios `b8602bd` → `089ca5f`, with the register updates (ECON-016 g+1,
   ECON-038, ECON-039 YTD).
7. Guests, advisor (→ `8b256a3`) and UI (→ `643f132`), independent of each
   other.
8. TPI `873cb71`: docs/tools only, kept separate from TPW fidelity claims.

**Not reviewed (in flight when this round closed):** formats has uncommitted
runtime wiring (`ObjectAnimator`, `ObjectRenderParts`, `OriginalObject`,
`Model.cs`, `ModelFile.cs`, `ModelVertexAnimation.cs`, new
`ObjectVertexAnimationTests`). Scenarios has uncommitted `progression_evidence.py`
and `contract/`. The formats wiring must answer the section 20 cursor
precondition before integration.

## 24. Root `main` `99fa14a` and the staged economy tree: verified

- **Clock merge.** `99fa14a` merges `126ee3e…c72c01f` onto `1441ead`. Its
  `tools/` tree is byte-identical to the `bbc89e5` tree I accepted in section
  18 (`git diff bbc89e5 99fa14a -- tools/` is empty). The extra docs are the
  clock lane's reviews of rides (`c2a0930`) and formats (`c72c01f`). My own
  results below agree with both. The FMA cancellation example there is
  independently reproduced with exact rationals: one binary32 rounding of
  `3·f32(0.1) + f32(−0.3)` gives −2⁻²⁷, and separate roundings give 0.
- **Staged economy.** The root index (tree `bcbdfd2`) equals `99fa14a` plus
  exactly `6a69e3f..c7bedf1`; the two diffs are identical apart from index
  lines. `974f546` (park-rating rules) is **not** staged and not reviewed here.
- **`c7bedf1` resolves my round-3/4 importer objection.** Amount, APR, term and
  lender must still match exactly. A 0 % APR repayment must equal integer
  `amount / months`, and positive-APR repayments are kept and marked
  `[APPROX:ECON-006] … unverified` without comparing them to any formula. The
  regression uses a fixed 458 against the annuity's 461, so it does not depend
  on `LoanMath`.
- **The locator still admits real positive-APR tables.** `IsLoanRecord`
  requires `M·N ≥ P − N` and `M·N ≤ 4P`. All 48 shipped offers (24 per
  install, 32 with positive APR) pass with both the Mac formula and the
  annuity. The largest total/amount is 1.409. Mod-only, low: a 100 % APR,
  600-month offer exceeds 4× and would make the locator miss the table.
- **Low.** A negative APR (mod) takes the "positive-APR unverified" message.
  `RepaymentPeriodInMonths 0` with 0 % APR throws `DivideByZeroException`. The
  previous code threw `ArgumentOutOfRangeException` from `LoanMath`, and
  neither is caught by `ParkEconomyRuntime`.
- **Low (link).** Staged `docs/ECONOMY.md:177` links `reverse/PPC-review.md`,
  which root does not have until this lane is integrated. Merge this lane's
  docs with or before economy, or cite the commit instead.
- **Validation on a `/private/tmp` snapshot of the staged tree:**
  - .NET suites filtered to Economy/SaveEconomy/Loan: 49 pass, 0 skipped.
  - Economy standalone harness: 15/15 groups (5,437 assertions).
  - Clock standalone: 35/35.
  - Python: 31 shared, 21 clock and 8 economy tests.
  - This lane's 84 tests also pass against root's newer `pef.py`, and the
    branch merges into `main` without conflicts.

## 25. Rides `722fe4c` (controller dispatch): accepted

- **Table.** `round5_evidence.py --rides-root` parses every
  `(Opcode.F, ids) => CommandBehavior.X` arm and compares it with my mapping
  of `controller-contracts.json` (accumulator class × parameter class). All 39
  roles are equal. This covers preserve/resolved, preserve/ignored and
  result/ignored (including BUMP5 host field +40), plus required variable
  input/output, optional output and original input (BUMP13/14). COAST7 is a
  no-op with no controller call.
- **Semantics checked in source:**
  - A non-literal command or an unreviewed ID faults. This is explicit policy.
  - Required-variable gates return before the effect and leave flags alone.
  - An optional output with a literal destination still sets flags: the
    `Operand.Value` setter discards writes to non-variables.
  - OriginalInput sets flags from the value read **before** the effect.
  - `TryEffect` detects an unsupported effect from an increase in the
    per-opcode `RecordUnimplementedEffect` counter.
  - Production routes every TOUR/BUMP/COAST call to
    `UnimplementedRideScriptEffects`, via `OriginalObjectEffects` and
    `VisitorRideScriptEffects` (neither handles them). Production queries
    therefore keep their prior flags and outputs instead of taking a fake 0.
- **Reproduced (`/private/tmp` snapshots, cached packages only):**

  | Run | Result |
  | --- | --- |
  | Before (`b7df1a0`) | 34 pass. Report SHA-256 `c9c303d5…`, which equals the clock lane's pinned `b3d14f9` report |
  | After, baseline | 41 pass. Default 23,768,180 instructions, 216 running / 47 waiting. Chaos 17,065,708, 202 / 61 |
  | After, Patch 2 corpus | 3 pass. Default 23,768,063, chaos 17,062,282 |

  These are exactly the commit's numbers. In the default run, unimplemented
  BUMP calls fall from 749,680 to 718,044 and COAST calls from 678,504 to
  542,808. Preserving flags changes branch paths even under the
  production-equivalent effects, although the final states stay the same.
- **Qualifier (low).** Preserving prior flags on an unsupported query is
  itself host policy. The next branch then follows unrelated earlier flags,
  which is no more original than the old zero. The docs say so; keep it out
  of any fidelity claim.
- **Independence.** `b7df1a0` (standalone animation helper, docs/tools only,
  no `source/` change) is **not reviewed**. `722fe4c` applies cleanly onto
  `b3d14f9` without it, so root can take the dispatch fix alone.

## 26. UI `864f82d` (root-name binder): accepted with one qualification

- **Hash.** `0x1709b8`: h = 0; for each nonzero byte, `extsb`, `xor`, then
  `mullw` by r4. The registration at `0x17387c/0x173880` passes `li r4,47`.
  Independently, `b_buy` → 557179197 and byte 0xFF → −47. A reading with
  unsigned bytes differs.
- **Shadow exclusion.** At `0x13c164–0x13c194` the loader calls the imported
  `strcmp(entry+32, "w_small_shadow.md2")`. Equal returns null **before**
  registration, so the code is real. **Qualification (medium-low):**
  - `entry+32` is the name copied out of the directory enumeration (callers
    `0x13c464/0x13c650`; enumerator `0x1221d8/0x121a0c/0x122364`).
  - The PC `ui.wad` and the Mac `ui.wad` (the UI lane's HFS copy in `/tmp`)
    both store **`w_small_shadow.MD2`**, while `strcmp` is case-sensitive.
    The exclusion fires only if the enumerator lowercases names, which is
    untraced.
  - The Mac directory lists `w_small_shadow` before `shadow1`; PC lists
    `shadow1` first. If the exclusion never fires and the registry keeps the
    first duplicate, the Mac would bind `wshadow1` to `w_small_shadow`.
  - The C# result (`shadow1`) holds if the exclusion fires, or with PC order
    and first-wins. The doc's "resolves to `strcmp` and its matching branch
    returns" should add "name canonicalisation untraced".
- **No-break check (throwaway dump test, `/tmp` only).** All 278 assets: 79
  have filename ≠ root, but no filename equals another asset's root.
  `Get(filename)` returns the same asset for all 278, and no filenames differ
  only by case. The only shared root is `wshadow1` (`shadow1` and
  `w_small_shadow`). Patch 2 `ui.wad` is byte-identical. Existing filename
  requests therefore bind as before. 66 UI-filtered tests pass, 0 skipped.
- **Low.** `bindingFailure` is sticky. A single duplicate root among ordinary
  assets, for example a mod adding a file with an existing root, makes every
  `Get` throw `UiModelBindingException`, which `Get` deliberately does not
  catch. Shipped data has none. Because mods are a supported extension,
  surface this at load rather than on the first draw.

## 27. Advisor `a479cff` and audio `58538df`: accepted

- **`a479cff` (completion tuple).**
  - Native `0x89f4` passes `r29` (variant) as r5 to wrapper `0xba54`.
    `0x8a00/0x8a04` skip everything on a zero return, `0x8a10` reserves span
    + 1000, and `0x8a3c` stores `r29` at history +228 (`0x8a4c` sets +232).
  - A scan of `0x89e4–0x8a3c` finds no write to r29.
  - The helper now rejects any changed slot, advice, variant or response ID
    before changing state, and stores the begin-time tuple. 52 helper checks
    and 27 Python tests pass at `a479cff`.
- **`58538df` (Layer I), checked against ISO 11172-3:**
  - The 4-bit allocation (15 rejected) and the intensity bound
    `(ext+1)·4` with shared allocation and per-channel scalefactors are
    correct.
  - Samples use `alloc+1` bits and `(code+1−2ⁿ)·2^(2−sf/3)/(2ⁿ⁺¹−1)`. The
    frame size is `(12·br/fs + pad)·4`, with MPEG-1 rates doubled.
  - The Layer II refactor keeps the `(ch·3+idx)·32` slice offsets.
- **Tables are independent of the host libm.** All 882 factors at
  `sound_shared` (SHA-256 `7132c2f1…`; base from `addi r27,r2,−21128` at
  `0x2d2c`) equal the double-then-float formula, and the SF63 column is 0.0.
  Every double sits at least **65,552** binary64 ulps from a binary32 rounding
  midpoint, so differences between platform `pow` implementations cannot
  change any table entry.
- **Policy and limits.**
  - SF63 (original mutes) and Layer I de-emphasis are rejected. CRC is skipped.
  - `MP2File.SampleRate` (hardcoded 22050) has no production consumer;
    `SpeechAudioPlayer` uses the decoded `Mp2Audio.SampleRate`.
  - 89 focused MPEG/LIP/advisor tests pass with the private fixtures and 0
    skipped. These include the whole Layer I UI bank, the error clip against
    its independent reference and the Layer II speech bank. As requested, the
    roughly 10-minute FFmpeg corpus comparison was not rerun.

## 28. Guests `00a6393` (temporary history): blocked, HIGH

I confirmed the clock lane's `24f5456` finding with my own decode:

- **Used history (+480..+486).** Each match divides and then branches to the
  join at `0xe9978`, so only the first match counts.
- **Temporary history (+488..+494).** Each `bc 4,2` skips only its own
  division and there is no branch to the join. Every match divides
  cumulatively by 5, 4, 3 and 2.
- **Divisions.** The magic constants `0x66666667 >> 1` and `0x55555556`, and
  `srawi` + `addze`, all truncate toward zero. This is checked on 5,000+
  int32 values including both extremes.
- **Counterexample.** Score 600, ID 7, used `[0,0,0,0]`, temporary
  `[7,7,7,7]`: native 5, helper 120.
- **Why tests didn't catch it.** Both readings agree whenever temporary IDs
  are unique, so a narrower domain would need proof from the writer. Simpler
  fix: apply every match in the temporary array. The 28 guest tests pass but
  do not exercise this.

## 29. Formats `11c1003` and its uncommitted repair: hold

- **Allocation objection.** The `c72c01f` HIGH objection still applies to
  committed `2439695`/`11c1003`.
- **Tentative review of the uncommitted repair.** Every table is checked
  (`Probe`) and charged against a budget of 4 × payload before its array
  exists. Keys × vertices is a `long`, and the group table is checked before
  `groups` is allocated. The design is right. After commit, re-review it with
  tests for the u16 × u16 product near 2³² and for reused spans exceeding the
  budget.
- **Wiring vs my round-4 cursor objection.**
  - Clip bind `0xa594c–0xa5a38` selects `lis 128` (0x00800000) when header
    flag 0x4 is set and `lis 244` (0x00F40000) otherwise. It then applies
    `not r5,r26` and `and`, so the cursor/static flag is cleared at every
    rebind.
  - A fresh search from key 0 is therefore native-equivalent for
    non-decreasing time since the bind.
  - At a loop wrap the docs now explicitly *assume* a rebind. This is
    accepted as a documented precondition and stays medium until the loop
    policy is traced.
- **Question (low).** `meshByNode` keeps only the last mesh per node. If a
  node owns several meshes, only one gets the vertex track; check the corpus.
- **Not reviewed:** the render-buffer path (`Model.cs`, `ObjectRenderParts`).

## 30. TPI `f81313a` / `7f2a6b2`: metadata accepted; trim the artifact

- **Content.** The JSON holds hashes, counts, member paths and per-key value
  hashes, with no raw bytes or string dumps. It keeps COS metadata-only, SHPI
  header alignment and the music-bank directory/payload conflict as bounded
  observations, and claims no engine equivalence.
- **Size.** `full-evidence.json` is 2,745,817 bytes (79k lines) and fully
  regenerable with the README command. Commit a compact summary instead (per
  inventory/intersection counts plus a manifest SHA-256) and regenerate the
  full file to `/tmp` or a CI artifact.
- **Staleness.** It also pins `opcodeSourceHashes` of the VM handler files.
  `Visitors.cs` changes with rides `722fe4c`, so that hash goes stale. Nothing
  tests it, and TOUR/BUMP/COAST stay "Hooked".
- **Not reviewed:** uncommitted `pal8.py`.

## 31. Round-5 classification and integration order

| Item | Status | Severity / owner |
| --- | --- | --- |
| Root `99fa14a` clock + toolkit | Verified (tools = reviewed tree) | — |
| Staged economy `…c7bedf1` | Accepted; importer objection resolved | Low: link, mod edges / economy |
| Rides `722fe4c` dispatch | Accepted (39/39, reports reproduced) | Low: preserve-flags is policy / rides |
| UI `864f82d` binder | Accepted; no existing binding changes | Medium-low: shadow canonicalisation wording; low: sticky failure / UI |
| Advisor `a479cff` tuple | Accepted (native r29) | — |
| Audio `58538df` Layer I | Accepted (ISO + 882 factors, libm-independent) | SF63/CRC/de-emphasis are policy |
| Guests `00a6393` history | **Blocked** | HIGH / guests |
| Formats `11c1003` | **Hold** until repair committed and re-reviewed | HIGH (allocation) / formats |
| Formats loop wrap = rebind | Documented assumption | Medium / formats |
| TPI `f81313a`/`7f2a6b2` | Accepted as metadata | Low: 2.7 MB artifact / TPI |
| Rides `b7df1a0`, economy `974f546`, scenarios `b093ee3` | Not reviewed this round | — |
| Any PC/Patch 2 runtime equivalence | Needs evidence | — |

Merge order from current root:

1. Commit the staged economy (`6a69e3f..c7bedf1`) together with this lane
   (`ppc-review-lane`, docs/tools only), so that the ECONOMY.md link resolves.
2. Rides `722fe4c` (cherry-pick onto `b3d14f9` without `b7df1a0`, or take
   `b7df1a0` only after it is reviewed). `b3d14f9` itself is still unmerged:
   `51425bd` → `15313d2` → `b3d14f9` first.
3. Advisor `3ae2dd9` → `8b256a3` → `58538df` → `a479cff`. The helpers are
   standalone, and the Layer I decoder is production.
4. UI `643f132` → `864f82d`, after rewording the shadow claim (no code change
   is needed for shipped data).
5. Formats `b0935b4` → `2439695` → `11c1003` → allocation-repair commit,
   only after that commit is re-reviewed.
6. Guests after the temporary-history fix.
7. TPI docs/tools, preferably with the compact artifact.

Scenarios' uncommitted `GameFlow.cs`/`ParkEconomy.cs` edits, formats'
uncommitted repair and TPI's `pal8.py` are tentative and outside this verdict.

## 32. Root `main` `9e40f52`: combined tree verified

`9e40f52` merges `7b6b46e` into `75a3380`. The second parent adds only
`lanes/clock/review-native-contracts.md`. `75a3380` already carried the
temporary-history repair inside the guest merge, so `main`'s
`lanes/guests/rules`, `GUESTS.md` and `PPC-guests.md` are identical to
`7b6b46e` (`git diff --quiet 7b6b46e 9e40f52 -- …` is clean). The two guest
lineages (`00a6393` and its rebased copy `be42615`) write the same paths, so no
duplicated helper exists. Every C# type name defined twice is either a partial
class or lives in a separate assembly (`CompatibilityFlags` in Online and in
Compat, unchanged since `6a69e3f`). The only cross-tree compilation is the
advisor's `Layer1Corpus.csproj`, which compiles production `Mp2Decoder*.cs` and
`Layer1TestFrames.cs` by link. That is intended, and the project builds with
zero warnings. No lane helper is referenced from `source/`.

Run on a `git archive` export of `9e40f52`:

| Check | Result |
| --- | --- |
| `dotnet test source/OpenTPW.Tests -c Release` with `OPENTPW_GAME_PATH`, `OPENTPW_LANGUAGE_DATA`, `OPENTPW_BONUS_DATA` | 864 passed, 0 failed, 12 skipped (all opt-in native-shader tests), 22.5 min |
| Scheduler / guests / advisor / rides animation / UI layout helpers (`dotnet run -c Release`) | 35 / 23 / 52 / 11 / 19, all pass |
| Economy `OriginalLoanRules.Tests.csproj` (`dotnet run`; `dotnet test` discovers nothing) | 15/15 groups, 5,437 assertions |
| Python: `tools/ppc-analysis` and each lane (`OPENTPW_PPC_BIN_ROOT` set) | 222 run, 11 skipped (lane-specific env vars), 0 failed |
| `Layer1Corpus`, rides `CorpusWitness`, `PatchAnalysis` builds | 0 errors |

## 33. Rides: `56a9d26` blocked (HIGH); `b7df1a0` accepted with limits

**`56a9d26`, HIGH (both items decoded by me and pinned in `round6_evidence.py`).**
`PassengerRing.QueryRoom` and `PlanAllowance` take a minimum where the native
code takes a maximum.

- **Room query (`0x3dd14`):** diff = config `+184` − (`+188` + `+192`), using
  32-bit wrapping arithmetic. `cmpw global,diff` / `bf lt` keeps the global's
  address unless global < diff. The result is therefore `max(global, diff)`.
  With global 100, configured 2 and 3 queued, native returns 100 and the helper
  returns −1.
- **Capacity plan (`0x3df24`):** the first select is the same max over
  (global, request). Two min selects follow, against definition `+792` and
  against the train limit. `0x3ea74` is called only when the result differs from
  `+240`. With (10, 100, 100, 100), native gives 100 and the helper gives 10. The
  lane's 3/3/4 split test passes only because of that min.

Medium findings:

- The builder at `0x3eb1c` substitutes train definition `+8` when the total is
  0, whereas the helper returns zeros.
- `main`'s `PPC-rides.md` already states both min readings, at the `0x3dd14`
  paragraph and in the COAST6 sentence. It needs correcting.

Production is unaffected, because COAST still goes through the
unimplemented-effects path.

Confirmed by the delegated decode: the BUMP guard (`0x24364`) is a signed,
strict less-than against `+100` and against 64. Ring append uses an unsigned
compare and wraps the cursor with an unsigned divide. Low findings not modelled:
the fallback ring at `+208`, and the car-eligibility counting rule. The helper
builds, 11/11 checks pass, the JSON regenerates identically, and merge-tree
with `main` is clean.

**`b7df1a0` (already merged): accepted with limits.** It touches only the
standalone rides lane and its doc. VM and animator behaviour is unchanged, so
"WAITANIM unsupported" describes the helper, not production. The four trigger
sites take a signed `max(ret−300, 300)`. WAITANIM (`0xafc78`) compares unsigned
after the division, so marking it unsupported is right. Low: opcode 16's
accumulator and variable writes are not modelled. Rides/VM tests on `main`:
113 passed, 0 skipped with `OPENTPW_GAME_PATH`.

## 34. Scenarios `b093ee3` accepted; `bac3428` accepted with one medium gap

Natively confirmed on `SimThemePark.data`:

- Mode values: lazy constructor `0x12bb64` → `SetGameType` `0x12bbf4`, which
  maps startup flags to 2/1/0.
- Feature gates:
  - tickets: `0xd2f1c` runs only when GameType == 0, and its caller only when
    `tick % 100 == 0`;
  - challenges: `0xcfef4`;
  - bank panel: `0x154aa0`;
  - research panel text 468: `0x161910`;
  - upgrades: `0x165a0c`;
  - `Easy_` balance layer: `0x10474c`.
- Keys = `mExtraKeys` + earned ÷ 3 (`0x128b60`). The new-player award gives
  response 394 for GameType 2, and otherwise increments `mExtraKeys` and gives
  response 393 (`0x128f3c`).
- The Easymode copy is the only code reference to "easymode" (`0x137600`), and
  it is gated on the easy flag.

Answers to the open concerns:

- **(a) Not inferred.** The mode comes from the explicit front-end button
  (`FrontEndMenu` → `GameFlow.StartPark` → `ParkStart.FromFrontEnd`). File
  presence decides only the `Easy_` layer and the seed, matching the gates
  above.
- **The original `.TPWI` has no mode field.** GameType is not serialized; only
  `mEasyModeUser` is saved. OpenTPW's own save stores `Mode`.
- **(b) Saved researchers.** Researchers restored from an OpenTPW save research
  in both modes.
- **(c) Not covered.** The Instant Action start has unit and asset tests only;
  there is no native smoke run.

Findings:

- **MEDIUM:** in production, Instant Action research never progresses. The
  seed's researcher is not imported, the staffless rate (ECON-019) is no longer
  called, and nothing can hire staff (`Hire` is called only from the sandbox
  smoke test). A delegated scratch test ran 365 days with zero research
  progress. Natively, the seeded researcher (identified by economy `6305d32`,
  section 35) would research. Register this as "seed staff not imported" or
  block it in the UI.
- **LOW–MEDIUM:** `FIDELITY-REGISTER.md`, `EconomyApproximations.cs` and the
  runtime log still describe ECON-019 as active, though the code path is now
  dead.
- **LOW:** the start record goes stale after loading a save in a different
  mode. Only the sandbox can reach this.
- **LOW:** `FIDELITY-REGISTER.md` conflicts with `main`. The conflict is only
  regenerated line numbers; `fidelity_register.py --write` and then `--check`
  pass.

Merged tree with the register regenerated: 827 passed, 0 failed, 54 skipped.
Lane Python: 19 OK. Contract: 23/23. `scenario_evidence.py`: 874 checks pass.

## 35. Economy `974f546`, `1c53e14`, `6305d32`: accepted (standalone)

**`974f546`.** All 11 cap compares in `0xc7b24` are signed `cmpwi` against the
literals 1000/20/10/10/10/10/4/4/4/4/4. Visitor arithmetic is cap × 20, then a
signed truncating ÷ 1000; ride arithmetic is × 3, then a truncating ÷ 2. The
skill chain `0xf41dc` runs in this order:

1. `fdiv` (double) for the percentage ÷ 100;
2. single-precision grade;
3. double add, then `frsp`;
4. `fmuls` × 20, with no fused multiply-add;
5. `0x1c3fbc`: `fctiwz` with unsigned saturation.

Inputs that separate the readings, all checked against the helper:

| Input | Native and helper | Competing reading |
| --- | --- | --- |
| skill(−1, 115) | 3 | 2 (double port) |
| skill(16777217, 0) | 335544320 | 335544340 (double) |
| skill(−1, 105) | 1 | 0 (all-single) |
| rides(−3) | −4 | −5 (floor) |
| visitors(−49) | 0 | −1 (floor) |

**`1c53e14`.** The save is `jungle/Easymode.TPWI`; Patch 2's copy is
identical. The witness checks the world prefix at 1179, bank 8 and tick 755.
Tick 755 maps to 2000-02-02, which agrees with the saved month and day caches.
Limit: bank offset 1411362 is pinned rather than parsed.

**`6305d32`.** This is a real next-ID chain walk (42 → … → 30), not a pattern
match. The researcher's ID agrees with the world FirstResearcher field. The walk
stays aligned past the researcher, and the header pattern occurs only once.
Native `0xf2ca0`/`0xf2e60` store `fctiwz`, then the low byte, then f32. Only the
name's SHA is committed. Limit: the start offset 1,385,521 comes from the formats
lane's map and was not re-derived. These commits claim no production import.

Low: `evidence.py` needs its sibling `schema` module on `sys.path` and fails
under `python3 -I`; it passes with `-E -s`. Python: 16 OK at `6305d32`. Loan
helper at `1c53e14`: 25/25 groups, 11,183 assertions. Merge-tree with `main` is
clean, touching lane paths only.

## 36. Advisor `2b8e4bf` (`MP2File` format metadata): accepted

`MP2File` now reports the rate and channels of the first complete frame instead
of a fixed 22050. `round6_evidence.py --pc-data` checks this with an MPEG header
reader written from ISO 11172-3/13818-3, independent of `Mp2Decoder`:

- PC and Patch 2 have identical banks: 47 banks, 3,739 entries.
- Every entry has a complete supported first frame; the fallback is reached 0
  times.
- Exactly 5 entries are Layer I 44.1 kHz mono, and the fixed 22050 was wrong
  for exactly those 5.

**Low:** when the frame is invalid, `SampleRate` falls back to the legacy reader's
int16 at entry `+24`, which reads 44100 as −21436. Prefer 0 ("unknown").

Merge-tree with `main` is clean. On the merged tree, the
`MP2FileMetadata|Layer1Decoder|Mp2Decoder|LipSyncTimeline` tests give 66
passed, 0 skipped, and `Layer1Corpus` builds with `-warnaserror`. The advisor's
uncommitted `audio-events/` work is not reviewed.

## 37. UI `68be372` / `0c5739b` and clock `7f8ef64`: accepted with limits

**UI `68be372`.**

- PC has 84 `.sgn` members in 81 WADs. Each is a 17-byte header, then two
  font+effect pairs, then 20-byte paint records, then exactly two 16×128×4
  bitmaps. Trailing bytes are zero.
- 23 version-101 files add a 256×128 wavelet.
- Mac `lobby.wad` is byte-identical to PC, so the Mac lobby check adds nothing.
  A scan of every WAD in `hfs.img` found 94 Mac signs with the same layout.
- The reader `0xabac0` and the relief function's base/diffuse/specular
  coefficients are confirmed.
- The coordinator's "1717 / 39244" is not a count. The nearest figures are file
  sizes: 17,337 bytes for 60 signs, and 39,842 for the largest.
- Low: the parser's minimum size rose from 885 to 889 bytes. No shipped file is
  affected.

**UI `0c5739b`.**

- `alphablt`/`colourblt` compute `d + ((a·(s−d)) >> 8)` with signed `srawi`,
  and alpha is max(src, dst).
- Mode 1 byte-reverses through `swizzle_for_gimex`. Mode 2 packs nibbles R:G:B:A
  as a big-endian `sth`. Destination rows are reversed.
- With zero iterations the Gaussian loop never runs, so the binary ink passes
  through unchanged.
- The `descimate` step is a signed `divw`.
- The reference matches all of these.

Remaining UI items, all LOW:

- Duplicate root names are still detected at the first draw, not at load.
- `test_phase2`/`test_witness` raise `KeyError` when only `UI_EVIDENCE_BIN_ROOT`
  is set.

UI Python with all `UI_EVIDENCE_*` set: 43 OK, 0 skipped. The round-5
shadow-name qualification is already in `main`, through `ae788a3`.

**Clock `7f8ef64`.**

- The RSE handlers at `0xb2144`, `0xb2194`, `0xb21e8` and `0xb2238` call
  `time` and then `localtime` through complete glue stubs. The C runtime backs
  `time` with `InterfaceLib:GetDateTime` plus 126,144,000. So these handlers
  read host civil time, not the park calendar.
- World state 4 increments the world counter, skips the first object loop and
  still reaches `0xd67f0` → `0xe3f0c`.
- Low: the commit message says state 4 skips "thing iteration", but a second
  list loop (`0x105470`–`0x105618`) still runs. The doc's narrower wording is
  correct.
- The `SchedulerRules.cs` change adds comments only. 35/35 checks pass, and 30
  Python tests pass with no skips.

## 38. TPI `11203fe`: accepted with limits

`18b09c5` replaces the 2.7 MB `full-evidence.json` with a 95 KB compact audit
that pins the full report's size and SHA. `11203fe` commits PAL8:

- index plane plus raw palette records for formats 0x7b/0xfb;
- the same 7,283 occurrences and 5,568 unique files in an independent scan;
- 60 sampled images agree with an independent RefPack decode.

The JSON holds hashes, counts and sizes only, and exports refuse to write into a
git repository.

- **MEDIUM:** local `main` merged `18b09c5` (not `11203fe`) in `ee373cd`, so
  `7f2a6b2`'s 2,745,817-byte blob is now in `main`'s unpushed history. It
  contains metadata only, so this is a size decision, not a content one.
  Rewriting is possible only before the push.
- **LOW–MEDIUM:** the pinned `Visitors.cs`/`Math.cs` hashes are stale against
  `main`, but nothing checks them. Regeneration shows 0 opcode-status
  differences.

24 Python tests OK; `--self-test` 14/14; merge-tree with `main` is clean. No
engine-equivalence claim is made.

## 39. Formats `dbcb859` + `6382736`: HIGH resolved; accepted with limits

**Allocation (round-5 HIGH): resolved.** At `6382736`, every array in
`ModelAnimation.Decode` and `ModelVertexAnimation.Decode` whose size comes from
the file is checked before it is allocated. Each check covers offset ≥ `0xB8`,
offset ≤ trailer, and 64-bit length ≤ trailer − offset, and each charges the
4×payload budget. The trailer is pinned to `length − 72`, and members are capped
at 16 MiB. A delegated scratch test set every counted table to 65535 and used
wrapping pointers `0xFFFFFFF0`/`0xFFFFFFFF` and a vertex block running into the
trailer. All 14 hostile inputs threw `InvalidDataException` after allocating
about 20 KB. The corpus still parses: 1,735 paired blocks, 1,278 clips and
6,010,956 table bytes, on both baseline and Patch 2.

Low: the budget counts table bytes, not managed objects. Records sharing one
rotation key with ease 32767 allocate about 22× the file size before rejection,
which extrapolates to about 370 MB for a 16 MiB member. That is bounded and
linear. A single slab array for the curves would remove it.

**Loop replay (`6382736`): confirmed by independent decode.**

- `0xa7960` passes r4 to `0xa7190` as r27. The wrap is `fcmpo`/`bf gt`, so it
  wraps only when AnimFrame > total, and only for deferred clip ≠ 12 with object
  `+4 & 0x18 == 0`. With channel bit 0x1 set, it calls `0xa67d8` with the same
  clip and flags `(r27 ? 0 : 8) | 1`.
- `0xa67d8` carries AnimFrame − total (`fsubs`) and binds through `0xa5894` only
  when `flags & 0xC == 0`. Replaying the same clip copies nothing back
  (`andc` = 0).
- The object-list update `0x4d354` calls with (r4 = 0, flags 8). Its samples
  then depend on object flag `0x00400000`.
- The copy-back gate matches bit for bit: (header +48 & 0x4) ∨ (option bit 0
  clear ∧ ¬(obj & 0x8 == 0 ∧ obj & 0x00100000)).
- Relative models rebind with clip 0. `0xa772c` then recomputes face normals and
  clears `0x00010000`.

**Exact end:** native code holds the last key at time == length, whereas the C#
`tick % duration` samples tick 0. This is documented as not modelled, not
claimed.

Open, all LOW:

- There is no register entry or `[APPROX:]` tag for the copy-back choice or the
  exact-end wrap. The lane hands this to the root.
- Doc nits:
  - copy-back moves six bounds words (+120..+140), not four;
  - the `0x10000` copy is `(n+3)/4 × 32` bytes;
  - the bind also clears node bit 0x10 (the toggle bit) when object flag 0x8 is
    set.
- Closed: the round-5 "more than one mesh per node" case cannot occur, because
  mesh *i* is node *i*.

Runs:

- Full suite at `6382736`: 769 passed, 0 failed, 55 skipped.
- Merged with `ee373cd`: 842 passed, 0 failed, 55 skipped.
- MD2 subset: 77/0/2 on both corpora.
- Lane Python: 26 OK. `format_witness.py` and `corpus_check.py`: exit 0.
- Merge-tree is clean.

The lane's uncommitted `ObjectAnimator.cs` and test edits are not reviewed.

## 40. Newer heads seen during this round (not reviewed)

These appeared while round 6 was running: rides `f5c77fe`, clock `f95c694` and
`d66857e`, advisor `be46ebe`, scenarios merge `0f5492a` (plus 15 uncommitted
paths), formats' uncommitted animator edits, and TPI `11203fe` (PAL8, which is
not in `main`). Root `main` also advanced to `f468f03`, which merges TPI
`18b09c5`; see section 41.

## 41. Root `main` `f468f03` and the diverged `origin/main` (second push)

`f468f03` (`9e40f52` → TPI `18b09c5` → progress note) changes no `source/`
files. TPI tests (15) and `fidelity_register.py --check` (141 unresolved) pass
on an export.

**Local `main` and `origin/main` (`778ea5d`) have diverged: 57 commits only on
local `main`, 53 only on `origin/main`.** `merge-tree` reports conflicts in
`README.md`, `FIDELITY-REGISTER.md`, `LIPS.md`, `PROGRESS.md`, `MP2File.cs` and
`Mp2Decoder.cs`. Upstream pins .NET 10 (`global.json` 10.0.401). Only SDK 8 is
installed here, so **the combined tree has not been built or tested**; nothing
below is a combined-tree pass. The delegated net8 probes used local `main` with
either side's decoder.

| Risk | Severity | What a correct resolution keeps |
| --- | --- | --- |
| Two Layer I decoders (upstream `f080218` in `Mp2Decoder.cs`, local `58538df` in `Mp2Decoder.Layer1.cs`) | HIGH | Local hunks. Delete upstream `DecodeLayerOneFrame`/`Requantize`/`Mpeg1LayerOneBitrates`; keep upstream tests. Naive resolutions fail to compile (duplicate or missing members), so this is loud rather than a silent alias. Semantics differ: upstream rounds the requantizer twice in float (local folds once in double, matching the 882 Mac factors) and ignores emphasis. Two local Layer I tests fail against the upstream decoder. |
| Two game-mode models (upstream `3286a6c` `ParkGameMode?` + `Easy_<object>.sam` overlay; scenarios `bac3428` `ParkStart`/`ParkStartKind`) | HIGH when scenarios lands | One model (`ParkStart`), feeding upstream's catalog/balance `easy` flag from game type 2, not from layer presence. Research policy must be chosen once: upstream keeps automatic Instant Action research, while scenarios is staff-only (section 34 medium). Scenarios + upstream conflicts in 13 files. |
| `ObjectAnimator.TicksPerSecond` became an instance property upstream (`47c1554`); formats `6382736` tests use it statically | MED (inferred CS0120) | Upstream instance property; update the formats tests. |
| SDT header widths (upstream `28864fa`: UInt16 rate, byte bits/type, `RawSampleField`) vs advisor `2b8e4bf`/`be46ebe` | MED | Upstream constructor body, plus the advisor's `Channels` and `TryReadFrameFormat`. With `28864fa`, the advisor's container fallback (section 36 low) becomes correct. Stale text: `MP2File` doc comment, `PPC-advisor.md` "hardcoded at 22,050". |
| net8 lane tools under the .NET 10 pin | MED (inferred) | `CorpusWitness.csproj` and `TpiCompare.csproj` reference `OpenTPW.Files` and need retargeting (NU1201). Other lane tools still need a net8 runtime to run. None are in the solution, so CI will not notice. |
| Upstream policies: English-only repo files (`2d3ce02`), no home paths or `.omx` (`b612f0c`) | MED | Translate the Dutch `PROGRESS.md` section added in `f468f03`. Replace home-directory paths in `PPC-advisor/economy/guests/rides.md`, `TPI-COMPARISON.md`, `tools/tpi-compare/README.md` and `full-evidence.json` (this doc is fixed in this round). The `.omx` files drop out cleanly. |
| Fidelity register | LOW | Resolve sources first, then `--write`. That gives 135 unresolved with no ID collisions. Upstream retired ECON-007/032, RIDES-020/027/029 and UI-033, and unmerged lanes still carry some of those tags. |
| CI Python discovery | LOW | `unittest discover -s tools/ppc-analysis` finds none of the 17 lane test files (no `__init__.py`). Run each lane explicitly. |

## 42. Round-6 classification and integration order

| Item | Status | Severity / owner |
| --- | --- | --- |
| Root `9e40f52` / `f468f03` combined local tree | Verified (864/0/12 full suite; helpers; Python) | — |
| Local `main` vs `origin/main` | **Hold the push** until a real merge is resolved and built on .NET 10 | HIGH: Layer I duplicate / root |
| Formats `dbcb859` + `6382736` | Accepted with limits; HIGH resolved | Low: object amplification, register entry, doc nits / formats; MED test fix after upstream merge |
| Rides `56a9d26` | **Blocked**: QueryRoom and PlanAllowance take a min where native takes a max | HIGH / rides; MED: `PPC-rides.md` on `main` states min |
| Rides `b7df1a0` (in `main`) | Accepted with limits (helper only) | Low |
| Scenarios `b093ee3` | Accepted | — |
| Scenarios `bac3428` | Accepted with limits | MED: Instant Action research stalls / scenarios; low–med: stale ECON-019; HIGH merge interaction with upstream `3286a6c` |
| Economy `974f546`, `1c53e14`, `6305d32` | Accepted (standalone) | Low: `-I` import, pinned offsets |
| Advisor `2b8e4bf` | Accepted | Low: fallback value; resolve with upstream `28864fa` |
| UI `68be372`, `0c5739b` | Accepted with limits | Low: lazy duplicate detection, test skip guards |
| Clock `7f8ef64` | Accepted | Low: commit wording |
| TPI `18b09c5` (in `main`) / `11203fe` | Accepted with limits | MED: 2.7 MB blob in unpushed history |
| Rides `f5c77fe`, clock `f95c694`/`d66857e`, advisor `be46ebe`, scenarios `0f5492a`, formats uncommitted edits | Not reviewed | — |
| Any PC/Patch 2 runtime equivalence | Needs evidence | — |

Merge order:

1. Resolve local `main` with `origin/main` first, since every lane below must
   then rebase onto .NET 10 and the upstream decoder, header and mode work. Keep
   local Layer I. Take upstream SDT widths and the animator instance clock.
   Translate the Dutch section, replace home paths, regenerate the register, and
   build and test on SDK 10.0.401. This lane (`ppc-review-lane`) merges without
   conflict either way.
2. Economy `974f546` → `1c53e14` → `6305d32` (lane paths only).
3. Clock `7f8ef64`; UI `68be372` → `0c5739b` (lane paths only).
4. Advisor `2b8e4bf`, resolved against upstream `28864fa` as in section 41.
5. Formats `b0935b4` → … → `6382736`, with the static `TicksPerSecond` test use
   updated after step 1, plus a register entry for copy-back and exact-end.
6. Scenarios `bac3428`, only after it is unified with upstream `ParkGameMode`
   into one model and the Instant Action research gap is registered or closed.
7. Rides `56a9d26`, after the max/min correction (and a `PPC-rides.md` fix on
   `main`).

## 43. Round 7: integration tree and lane repairs (state at review time)

Brief: verify each lane against the original Mac PEF and PC data independently
and keep every qualification. A lane's self-consistent tests are not evidence of
original runtime behavior. This round was read-only on peer trees, with no root
merges or pushes. Only this section and `round7_evidence.py` /
`test_round7.py` are new.

**Integration (`integration-remote`) is not committed.** It is still mid-merge
(`MERGE_HEAD` = `origin/main` `778ea5d`, 108 staged files, 4 unstaged edits).
Nothing below is a review of a committed integration result. Release build
0 errors, Metal 222 frames and the full test suite were reported by the
integration owner. This review did not rerun them, and the full suite was still
running when reported.

Fixed or no longer applicable (round 6 items):

| Item | Status |
| --- | --- |
| Merge conflict markers | None remain in the staged tree. |
| Duplicate Layer I | One decoder (`Layer1TestFrames`/`Layer1DecoderTests` local); no duplicate class. |
| `CorpusWitness.csproj`, `TpiCompare.csproj` net8 → net10 | Retargeted (unstaged). |
| Rides max/min (HIGH) | Code repaired in the rides working tree, **uncommitted**. See below. |
| Home paths in this document | Fixed on this lane (section 41). The integration tree still has `main`'s older copy until this lane merges. |

Re-verified against `SimThemePark.data` (independent decode, `round7_evidence.py`;
round 6 already covers the selectors):

- **Rides selectors.** `0x3dd48/4c` keeps the global value when
  `global >= room`, and `0x3df4c/54` does the same for `global >= request`.
  Both are maxima. `0x3df78/7c` (+792) and `0x3dfac/b0` (train +8) are minima.
  The rides working tree's `PassengerRing.QueryRoom` = `max(global, room)` and
  `PlanAllowance` = `min(min(max(request, global), def), train)` agree. With a
  net10 override in a scratch copy, the 16 controller reference tests pass.
  Rides Python (20 tests) passes against the identified data fork, with zero
  skips. That run needs `OPENTPW_MAC_APP` pointing at `SimThemePark.data`; the
  MacBinary `.bin` fails identity. Without the variable, 5 witnesses skip
  silently. Final doc wording was mid-edit and is not reviewed.
- **Clock `d66857e`.** `0xa3e08` returns +16400 for argument 0 and +16408
  otherwise. Coaster tick `0x406bc` passes literal 0 to it at `0x406c4`.
  Accepted. Clock Python: 16 pass, 1 skip (identified PC save not supplied).
- **Advisor `be46ebe`.** `EVENT` sets `r7 = 1000` (`0xaf9e4`) before
  `0xae930`. `EVENT_EXT` passes an operand in `r7`. `SPAWNSOUND` stores the
  child ID at parent +20 (`0xb12a0`). Accepted for those operands. The 11
  advisor Python tests are synthetic only; they do not touch the binary.
  Catalog and EventMap counts were not re-measured.
- **Formats (uncommitted).** `keepsPoseOnClipChange` for fixed items follows
  the bind `0xa5894` reading in section 39. It is uncommitted and not
  re-tested here.
- **Scenarios (uncommitted).** The save already required `Mode` by name in
  version 1 (`767f05b`, string enums, integers rejected), so the "never
  defaulted" claim holds. ECON-019 is kept and re-described rather than
  removed. Uncommitted, so not accepted.

Remaining blockers in the integration tree (actual, verified at review time):

| Blocker | Severity | Evidence |
| --- | --- | --- |
| `GameFlow.LoadPark` starts saved parks without `gameMode` | HIGH | `source/OpenTPW/Client/GameFlow.cs` `LoadPark` calls `StartLevel(entry.Level, original: true, …)`; `ParkLoadEntry` has no mode. An Instant Action park loads with Full Simulation rules. |
| ECON-007 retired, with `[BIN]` claiming "offers it again when the credit test passes" | HIGH (fidelity claim) | `AvailableLoans` has no net-worth/credit test anywhere in `ParkEconomy`. The re-offer is unconditional, so the label claims more than the code does. Keep an APPROX entry until the credit test is implemented. |
| Seven net8 lane harnesses under the SDK 10.0.401-only environment | MED | `OriginalSchedulerRules` fails restore (NU1100, `Microsoft.NETCore.App.Ref 8.0.31`). The others listed by `round7_evidence.py` are the same kind: advisor LipDriver/Layer1Corpus, economy loan rules, guests rules, rides animation, UI layout. Unmerged rides controllers and advisor `AudioEventAssets` are also net8. Their recorded results cannot be reproduced in this environment. |
| CI Python discovery | MED | `unittest discover -s tools/ppc-analysis` runs 31 tests. 15 lane test files are unreachable (no `__init__.py`), so CI does not guard lane witnesses. |
| Setup wizard footer | MED | Quit 90 + 2×150 + 24 logical px (plus padding) overlaps below roughly 430 px, and no minimum window size is set. The DPI recompute runs only when pixel size changes. The owner reported fixing both; that fix is not in the staged tree. |
| Home paths in English docs | LOW | 35 lines across `SETUP.md`, `TPI-COMPARISON.md`, `PPC-advisor/economy/guests/rides.md`, plus `PPC-review.md` from `main`. Advisor `be46ebe` and rides add more, including the old `opentpw-dotnet` SDK path. |
| `LIPS.md` unstaged edit | LOW | Cites the static Mac consumer trace and keeps runtime integration, geometry and device timing open. Acceptable wording, but it depends on `PPC-advisor.md` content being merged in the same push. |

Pending (owner still editing; re-review after commit): rides `f5c77fe`/`73ec987`
plus the max/min working-tree repair, formats animator edits, scenarios
uncommitted mode/save/ECON-019 edits, wizard repairs, and the integration merge
commit itself.

Unresolved objections carried forward: no PC or Patch 2 runtime equivalence for
any Mac-derived rule; the Instant Action research policy (section 34) is a Mac
static reading only; SDT bank remap is needed before clip names (advisor);
TPI history blob (section 38).

## 44. Round 8: reproducible lane discovery and the .NET 8 harness strategy

Brief: as in round 7. Verify each lane against the original Mac PEF and PC data
independently, and keep every qualification. A self-consistent test is not
evidence of original runtime behavior. This round touched only this section,
the reproduce block above, `tools/ppc-analysis/run_evidence_checks.py` (new)
and `lanes/review/test_round8.py` (new). No workflow or source file changed, no
peer tree was written, no asset directory was listed or scanned, and the Layer I
corpus run was not repeated.

**Integration is still not committed** (`integration-remote` at `f468f03` with
`MERGE_HEAD` present). Mode persistence, ECON-007, the wizard DPI/width fix and
the stalled installation scan are the integration owner's open work. None of it
is re-reviewed or counted as fixed here. Rides `8f27b00` (max room, max
request/global, then min definition/train) is detached and not on any branch.
Round 7 already confirmed its selectors. Its tree passes 20/20 rides Python
tests with zero skips under the runner below, but it is still **pending merge**,
together with the untracked `save_evidence.py` in that tree and the root
`PPC-rides.md` wording fix.

### Python discovery

`unittest discover -s tools/ppc-analysis` reaches only the two top-level test
files (31 tests). The lane directories have no `__init__.py`. Advisor, economy,
guests and rides each import a different module named `evidence`, so the lanes
cannot share one interpreter either. In this tree, 18 lane test files
(214 tests) are outside CI's discovery. The integration tree has a different
count (15 files in round 7) because it lacks this lane's review files.

`run_evidence_checks.py` starts one `python -I -B -m unittest discover -s <dir>
-t <dir>` process per directory: the root and each `lanes/<lane>` that has test
files. It fails if any `test_*.py` under `tools/ppc-analysis` is not directly in
one of those directories, if a suite runs zero tests, or (with
`--require-fixtures`) if any test is skipped. Fixture variables come only from
flags. Inherited `OPENTPW_*` values are stripped, so a stale shell variable
cannot change a result. `--mac-bin` is the Feral `bin` directory. It sets
`OPENTPW_PPC_BIN_ROOT` to that directory and `OPENTPW_MAC_APP` to the
`SimThemePark.data` file inside it. The two variables have different shapes:
rides wants the file, the other lanes want the directory. Swapping them gives
`IsADirectoryError` (rides, 3 errors) or `NotADirectoryError` (guests), so a
wrong value fails loudly rather than skipping. The runner rejects both shapes up
front, and also rejects a directory without the data fork (the MacBinary `.bin`
fails identity). Only those two paths are stat'ed.

Measured on this tree (Python 3.14.2):

| Fixtures | Suites | Tests | Skipped | Remaining skips |
| --- | --- | --- | --- | --- |
| none | 8 | 245 | 19 | Mac binary witnesses, PC data, private UI corpus |
| `--mac-bin $FERAL_BIN` | 8 | 245 | 9 | `OPENTPW_PC_DATA` (1, not supplied: no asset scan this round); `UI_EVIDENCE_*` (8, private UI corpus) |

With `--mac-bin`, all Mac-binary witnesses in guests, rides and review ran and
passed against the identified `SimThemePark.data`. These are static operand and
identity checks only. Running the advisor, clock and economy Python lanes needs
no binary: they are synthetic.

### .NET 8 harnesses under the .NET 10 pin

All 8 lane harnesses are `net8.0`. With `--dotnet`, the runner copies
each registered self-test harness to a temporary directory (no `global.json`
applies, and no `bin`/`obj` lands in the checkout), runs it, and classifies a
failure. It does not run the corpus harnesses (`Layer1Corpus`, `CorpusWitness`).
It reports a harness whose `ProjectReference` targets a newer framework as an
incompatible reference, before anything else. Scratch reproductions (copies
outside every checkout):

| Case | Result | Class |
| --- | --- | --- |
| SDK 8.0.425 (retained root), 6 self-test harnesses | All pass: advisor 52, clock 35, economy 15 groups / 5437 assertions, guests 23, rides animation 11, UI 19 | — |
| SDK 10.0.401 only, same 6 harnesses, packs cached | Build succeeds; every run fails (`Microsoft.NETCore.App` 8.0 not installed; only 10.0.12) | missing runtime |
| SDK 10.0.401, empty `NUGET_PACKAGES`, no package source | `NU1100` for `Microsoft.NETCore.App.Ref`, `AspNetCore.App.Ref`, `App.Host.osx-arm64` 8.0.31 | restore: targeting pack unavailable |
| SDK 8.0.425, same empty cache and no source | Build succeeds (packs ship with SDK 8) | — |
| net8 `CorpusWitness` → net10 `OpenTPW.Files` (integration copy), SDK 10 | "targets 'net10.0'. It cannot be referenced by a project that targets net8.0" | incompatible reference |
| Same, SDK 8 | `NETSDK1045` | SDK too old |
| SDK 8 with the working directory inside a checkout pinned to 10.0.401 | `sdk-not-found` (`global.json`) | — |

Conclusions:

- Round 7's `NU1100` was a real restore failure, but it was environmental. SDK 10
  needs the 8.0 targeting packs from a package source or the cache, and
  neither was available then. It is not a project incompatibility. **Side
  effect of this round:** one SDK 10 scratch build downloaded
  `microsoft.netcore.app.ref` 8.0.31 into the user NuGet cache (23:04). Since
  then, `NU1100` no longer reproduces from that cache, and the next failure
  for SDK-10-only runs is the missing 8.0 runtime.
- The standalone harnesses (advisor driver, clock, economy, guests, rides
  animation, UI reader) need no upgrade. The retained SDK 8 root runs them all
  when invoked from outside the pinned checkout. Retargeting them to net10
  would add churn and prove nothing new.
- Only harnesses with a `ProjectReference` into `source/` must follow the
  source framework. Of the harnesses in this tree, that is just
  `CorpusWitness`, which integration has already retargeted (still unstaged).
  `tools/patch-analysis` is the same kind, but it is outside the lanes and is
  built by CI.
- Passing harnesses are self-consistent. They do not show original runtime
  behavior.

### CI handoff (integration owner; not applied here)

Once this lane is merged, replace line 23 of `.github/workflows/build.yml`
(`python -m unittest discover -s tools/ppc-analysis -p "test_*.py" -v`) with:

```sh
python -I -B tools/ppc-analysis/run_evidence_checks.py
```

CI has no fixtures, so expect skips there and do not pass
`--require-fixtures`. For the lane harnesses, an optional step is to install
both SDK bands and pass the resulting host:

```yaml
- uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9
  with:
    dotnet-version: |
      8.0.x
      10.0.x
- run: python -I -B tools/ppc-analysis/run_evidence_checks.py --dotnet "$(command -v dotnet)"
```

This combined single-root layout is **inferred, not run**. Locally the two SDKs
live in separate roots, and only the explicit SDK 8 root was exercised. The
runner's scratch directory has no `global.json`, so the newest SDK builds the
net8 harnesses using the 8.0 packs installed by the 8.0 band, and runs them on
the 8.0 runtime. Locally, the verified command is:

```sh
python3 -I -B tools/ppc-analysis/run_evidence_checks.py --mac-bin $FERAL_BIN --dotnet $DOTNET8/dotnet
```

where `$DOTNET8` is the retained SDK 8 root. Expect 245 tests with 9 skips and
6 harnesses passing.

### Status

| Item | Status |
| --- | --- |
| CI Python discovery (round 7 MED) | Runner and tests provided on this lane. The CI change is **pending** with the integration owner. |
| net8 harnesses under SDK 10 (round 7 MED) | Reclassified: standalone harnesses run with the retained SDK 8 (verified). `NU1100` is environmental. Only project-reference harnesses must retarget. A CI step for net8 is **pending** and its layout is untested. |
| Integration merge commit, mode persistence, ECON-007, wizard, installation scan | **Pending** (uncommitted; not reviewed this round) |
| Rides `8f27b00` | Accepted (round 7 selectors; 20/20 with fixtures). **Pending merge** and root doc fix |
| Formats, scenarios | Still in progress with their owners; not reviewed |
| UI private-corpus witnesses, `OPENTPW_PC_DATA` SDT audit | Not run this round (private inputs; no asset scan) |

Unresolved objections carried forward: no PC or Patch 2 runtime equivalence for
any Mac-derived rule. The Instant Action research policy (section 34) is a Mac
static reading only. The SDT bank remap is needed before clip names (advisor).
The TPI history blob (section 38) remains.

## 45. Round 9: formats clock and pose, economy scientist, rides save boundary, TPI header

Brief: as in rounds 7 and 8. This round wrote only this section, the reproduce
block above, `lanes/review/round9_evidence.py` and `lanes/review/test_round9.py`.
No peer tree, runner, workflow or source file was written. Peer suites ran from
scratch copies (`git archive` or `rsync` without `bin`/`obj`) or in place with
`python -I -B`. Asset access was limited to the files named below, read by exact
path. No codec run took longer than a few seconds.

### Formats `c546a24` (channel clock): accepted, Mac static, speed 1.0 only

Independent decode (`round9_evidence.py channel_clock`):

- `0xa6484`: `frame = speed(+12) × (30 × (float)(u32)(now(+20) − start(+16)) / 1000)`.
  Every step is single precision, and speed is applied last. The bias at TOC
  `0x51a8` is the unsigned 2^52 pattern, and the constants are 30.0 and 1000.0.
- `0xa7360`: `fcmpo frame, duration` and a branch on "not greater". So a
  frame equal to the duration is not past the end.
- `0xa6398` (carry path): the carry is capped at the duration, then
  `1000 × c / 30 / speed` in single precision, then the truncating unsigned
  converter `0x1c3fbc` (`fctiwz`, top half via `addis 0x8000`). Finally
  `start = now − ms`.

`ObjectAnimator.GetFrame` / `GetCarryMilliseconds` match this at speed 1.0. The
review's float32 model (`Channel`) reproduces the lane's 30 Hz cases:
999/1000/1100 ms give 29.97/30/3, one 2,500 ms update gives 30, and a 10-tick
clip at 334/667 ms gives 0/9.99.

Qualifications kept:
- Speed ≠ 1.0 and the object-list update `0x4d354` are unsupported, as the lane
  states.
- **New:** on a fresh bind, `0xa6398` takes the start time from the global
  block at +16408 when channel flag 0x40 is set and at +16400 otherwise. Those
  are the two clocks of round 7's `0xa3e08`. OpenTPW gives every channel one
  animator clock. Which clock the caller passes as `now` to `0xa6484` was not
  traced. This matters for game speeds other than 1×.
- `RIDES-001` still says "original tick rate not verified". The Mac constant is
  now proven statically, so the entry should be re-described as "30 in the Mac
  channel clock; PC unverified" rather than left as an open question.
  Register edit (root owner).

### Formats `2760acb` + `2d3442f` (copy-back gate, fixed-item pose): accepted with one limit

Re-decoded:
- Bind `0xa5894` loads the option word +16396 and object flags +4. Flag 0x8
  set skips to the per-mesh 0x10 clear loop. With 0x8 clear and 0x00100000
  set, it executes `ori r27,r27,1` (keep the pose). Header flag 0x4 is tested
  next.
- The option word's only store is at `0xa7eec`, and setup `0x54c08` calls it
  with `r3 = 0`.
- Builder `0x594c8` maps caller 0x200 to ride 0x100 and caller 0x40000 to
  ride 0x1000. Loader `0x58a3c` maps ride 0x100 to object 0x8 and ride 0x1000
  to object 0x00100000.
- Catalog loader `0x119a60`: descriptor +56 ≠ 0 gives caller flags 0x50c00
  (0x40000 set, 0x200 clear), plus 0x400000 when +132 ≠ 0. It then calls
  `0x594c8` at `0x119ac0`.

`IsFixedItem` is `GetInt("Info.DontApplyOffset") != 0`, which equals the
native non-zero test. The `+56 = DontApplyOffset` schema layout (`0x16f4c`)
was not re-derived here. It is accepted on the lane's 113 checks.

**Limit (kept, not a blocker):** the gate reads flags from the object passed to
the bind. Only the catalog-built object is shown to carry 0x00100000. How a
placed instance's bind object relates to it was not traced. OpenTPW applies the
catalog entry's value to each placed instance. Also, a bulk write that sets
option-word bit 0 is not excluded.

### Formats round-6 working tree (instance rate): pending, uncommitted

The formats tree is at `2d3442f` plus an uncommitted edit to `ObjectAnimator`,
`PrototypeRide`, `Md2AnimationTests` and `ObjectVertexAnimationTests`. Tested
snapshot: diff SHA-256 `59ac2d84…` over all four files, copied at the same moment. The
edit makes the constructor
`ObjectAnimator(ModelFile, float ticksPerSecond = 30, bool keepsPoseOnClipChange = false)`
and substitutes the instance rate for both 30s.

**Constructor collision (real, repaired only in the working tree):**
- `origin/main` `778ea5d` (and the staged integration) has
  `ObjectAnimator(ModelFile, float ticksPerSecond)` and tests `new ObjectAnimator(model, 15)`.
- Committed `2d3442f` has `ObjectAnimator(ModelFile, bool keepsPoseOnClipChange)`
  and the positional call `new ObjectAnimator(model, entry.IsFixedItem)` at
  `ObjectVertexAnimationTests.cs:276`.
- Merging `2d3442f` as committed gives a compile error (`bool` does not convert
  to `float`) or a silently dropped parameter, depending on how the conflict is
  resolved.
- The working tree uses named `keepsPoseOnClipChange:` everywhere, and
  `OriginalObjectRuntime` passes no rate (30).

**Integration must take the reconciled commit, not `2d3442f`.**

New 15 Hz tests:
- 2,000 ms is the endpoint and 2,100 ms replays from a carry of 1.5 ticks =
  100 ms.
- One 5,000 ms update lands at 30.
- 10 ticks at 667 ms truncate to a 0 ms carry.
- A fixed item finishes only past 2,000 ms and keeps its pose through a
  matrix-only clip.

`clock_cases` recomputes every one of these from the decoded formula with 15 in
place of both constants, and they agree. They are **self-consistency of an
OpenTPW extension**: the original has no rate parameter. Affected .NET tests on
the scratch copy (SDK 8, lane pin 8.0.425) passed 12, failed 0, skipped 3
(asset-bound). Re-review after commit.

### Economy `abbb52c` (standalone scientist snapshot): accepted

Serializer re-decode (`scientist_tail`):
- The staff tail `0xf2c7c..` writes, in order, these runtime fields with these
  name strings (from the code string table at TOC `0x34ec`):

  | Runtime field | Name / content | Width |
  | --- | --- | --- |
  | +484 | `mCurrentPayGrade` | 4 |
  | +500 | happiness | 4 |
  | +512 | `mJobsDone` | 4 |
  | +416 | `mName[i]` | 33 × 2 |
  | +518, +520 | `mPatrolRegionBL/TR` | 2 + 2 |
  | +488 | `mPercentageThroughGrade` | 1 |
  | +516 | `mRestArea` | 2 |
  | +412 | `mState` | 4 |
  | +508 | `mTimeStartedIdling` | 4 |
  | +492 | `mTimeHired` | 8 |
  | +504 | energy | 4 |

  The total is 105 bytes.
- The researcher tail writes +528 `mTimeStartedResearching` (4) and +524
  `mNext` (2).
- Happiness and energy go through `fctiwz` → low byte → float before the
  write. Keeping the saved bits is therefore correct, and the reader must not
  truncate again.

Every reader offset (0/4/8/12/78/80/82/83/85/89/93/101, then 105/109) matches.

Fixture walk (`scientist_walk`, stdlib zlib, independent of the lane's C#):
- World offset 1179, FirstResearcher 30.
- From head offset 1,385,521: 12 guests, then scientist 30 at header 1,391,921,
  body 1,391,929..1,392,430.
- Grade 2, state 1, research tick 697, next researcher 0, next used actor 29.

The lane's harness on a scratch copy (SDK 8, Release, actual fixture) passed
10/10 groups and 1,157 assertions.

Cycle, truncation and ID bounds:
- Back-links and self-links in the used chain are rejected before the next
  header is read.
- Header and body truncation are checked against the exact remaining length.
- The model allowlist is {1, 8}, records are limited to 1..64, and payloads to
  8 MiB.
- The researcher self-link is rejected.
- A caller boundary never gains the fixture qualification.

Kept limits:
- Cycles entirely inside the unparsed suffix are not detected.
- Head offset 1,385,521 is the lane's fixture constant (formats map-end
  evidence), not re-derived here. Reaching FirstResearcher 30 from it is
  corroboration only.
- The harness is net8 and is not in `run_evidence_checks.py` `SELF_TESTS`, so
  the runner reports it but does not run it. See the handoff below.

### Rides `3d9a01c` (coaster save boundary, byte 12): accepted, empty fixture only

Re-decoded (`coaster_save`):
- Load: compare with `EMAK` (`addis −0x4b41` / `cmplwi 0x4d45`), loader
  `0x39f98`, `LbFile_Read`, compare with `SAOC` (`addis −0x434f`).
- Save: saver `0x394b8`, then `0x434f4153` is built and written with
  `LbFile_Write`.
- So SAOC is a trailing marker.
- Byte 12: `clrlwi` and `cmplwi 255` at `0x377bc` (255 takes a computed value),
  then `r10` → `0x36020 r28` (the only assignment) → `0x36514 r6` →
  `0x34d90 r26`. The value lands in `section+4` at `0x34f74`, with the cell
  array +52 [ordinal] = section, section +256 = cell, cell +48 incremented and
  shifted sections renumbered at `0x34f48`.
- It is an insertion ordinal, not Direction.

PC fixture (`coaster_body`): one EMAK, one SAOC, body 1,606,446..1,606,462,
16 bytes, words 0/0/0/1, body SHA `741939cc…`. The lane's suite on a
`git archive` of `3d9a01c` passed 23/23 with `OPENTPW_MAC_APP` and
`OPENTPW_PC_FIXTURE`.

Kept: no nonempty track or train fixture, no codec, and the 34-byte record
layout is not checked beyond the lane's 132 pins.

Pending:
- The rides tree has further uncommitted work (`save_control_evidence.py`,
  edits to `save_evidence.py`, the JSON and the doc; 24 tests pass in place).
  Not reviewed.
- **Runner gap:** the new skip uses a fourth variable, `OPENTPW_PC_FIXTURE`
  (a file). `run_evidence_checks.py` neither sets nor strips it. After this
  merges, `--require-fixtures` fails with one skip, and a stale shell value
  would pass through. Resolve it either way:
  - rides reads `$OPENTPW_PC_DATA/levels/jungle/Easymode.TPWI`, or
  - the runner derives `OPENTPW_PC_FIXTURE` from `--pc-data` and adds it to
    `FIXTURE_VARIABLES`.

### TPI `0930182` (shifted header, edition identities): accepted with qualifications

`tpi_header` on the identified TPI `prebuilt.TPWS`:
- magic 500, byte 4 = 1, label length 19, shift 24.
- Shifted version byte 133, offline byte 0, BILZ at `0x625`.
- At the legacy fixed offsets: `0x608` = 0, `0x609` = 0, no BILZ at `0x60d`.

The committed `SaveReader` (root `f468f03`) checks `container[0x608]` before
BILZ. It therefore rejects this file as **"Unsupported save container version 0;
expected 133"**. Rejecting it is correct, but the stated reason is wrong: the
inner version is 133.

Recommendation (MED, root owner): check the length-prefixed profile before the
version byte and report "unsupported TPI length-prefixed profile". Otherwise
setup and import messages misdiagnose TPI saves.

Allowlist qualification:
- `KNOWN_STANDARD` / `KNOWN_SAVE` are two exact hashes per supplied family. The
  doc correctly says they are not a universal allowlist.
- The recommendation "TPW setup requires positive TPW identity" would, if
  applied in production, reject every other TPW edition, locale or
  modification (Mac, other retail prints).
- It must stay a corpus witness until each of those has its own identity
  profile.
- `theme-park-world-nl` here is a language overlay without `Standard.sam`, so
  it gives no evidence either way.
- `byte4 == 1` is a heuristic profile switch. A legacy save whose first banner
  byte is 1 would be parsed as labelled and then fail the label checks. It
  fails closed, so the outcome is acceptable.

29/29 TPI Python cases pass in place.

LOW: `tools/tpi-compare/README.md` adds a home-directory path, the same class
as the round-7 item.

### Root docs: min/max correction (committed trees only)

`rides_doc_status`:

| Ref | `PPC-rides.md` capacity wording |
| --- | --- |
| root `main` `f468f03` | **old `min(global_admission_limit, …)` (wrong)** |
| `origin/main` `78dfb5e` | no `PPC-rides.md` |
| rides `8f27b00`, `3d9a01c` | corrected: max(request, global), then min(definition, train) |

The correction exists only on detached rides commits. It is not on any root
branch.

### Status

| Item | State |
| --- | --- |
| Formats `c546a24`, `2760acb`, `2d3442f` | Accepted (Mac static, speed 1.0; placed-instance flags and per-channel clock source open) |
| Formats instance-rate / constructor reconciliation | **Pending** (uncommitted; scratch run 12 passed / 3 skipped) |
| Economy `abbb52c` | Accepted (standalone; not registered in the runner) |
| Rides `3d9a01c` | Accepted (empty PC body only). Further rides edits **pending** |
| TPI `0930182` | Accepted as a corpus witness. `SaveReader` diagnosis MED; allowlist must not become a setup gate |
| Root `PPC-rides.md` min/max | **Not fixed** on any committed root ref |
| Integration | **Pending.** `integration-remote` is still `f468f03` + `MERGE_HEAD` `778ea5d`, 116 changed paths, and `origin/main` is now 4 commits past that (`127d9e6..78dfb5e`), so the merge base is stale |
| CI runner switch (round 8) | **Pending** with the integration owner, plus the two runner gaps above (`OPENTPW_PC_FIXTURE`, scientist harness) |

Fixed since round 8: none on a committed root ref. Stale: round 8's
"245 tests". This tree now runs 258 Python tests (8 skipped with fixtures,
24 without).

Concrete remaining blockers:
1. The formats reconciliation commit (constructor collision).
2. Root `PPC-rides.md` min/max wording.
3. Integration merge onto current `origin/main`, with mode persistence,
   ECON-007 and the wizard (round 7).
4. The runner's `OPENTPW_PC_FIXTURE` handling before `--require-fixtures` is
   used in CI.

Unresolved objections carried forward: no PC or Patch 2 runtime equivalence for
any Mac-derived rule; Instant Action policy (section 34); SDT bank remap
(advisor); TPI history blob (section 38).

## 46. Round 10: clock saved-script graph, rides topology gates, UI catalog events, scientist envelope

Brief: as in rounds 7 to 9. This round wrote only this section, one reproduce
line, `lanes/review/round10_evidence.py` and `lanes/review/test_round10.py`. No
peer tree, runner, workflow or source file was written. The native UI process
lifecycle is under separate review and is not repeated here. Peer suites ran in
place with `python3 -I -B`. The economy .NET harness ran from a `git archive`
copy in `/tmp` with SDK 8.0.425. No codec run and no mount scan were done.

### Clock `21bd7ef` (saved-script graph): accepted, static and one-fixture

Independent re-decode of reader `0xb4818` (sole caller `0x11bea8`) and writer
`0xb3868` (`round10_evidence.py saved_script`):

- **Framing.** The reader checks the `RSSE` magic (`0xb48d8/0xb48dc`). It then
  reads a declared header size straight into the manager (`r5 = r26`,
  `r6` = wire size). The five `PAD_` words and the count and width words are
  read with no compare between them. The fixed record is read with the saved
  width (`372(r1)`) into a 244-byte allocation. The lane's guards (header 20,
  `PAD_`, width 244) are therefore stricter than native, and the lane says so
  ("evidence-tool policy").
- **Head insertion.** The restored head word +16 is stored (`0xb49a8`) and then
  overwritten with zero (`0xb49ac`). Each script is inserted at the head
  (`0xb4b1c..0xb4b38`). The fresh links captured in r20/r21 (`0xb4b48/0xb4b50`)
  overwrite the saved tokens (`0xb4d80/0xb4d90`). The rebuilt order is the
  reverse of the serialized order, as the lane states.
- **ID resolution.** Lookup `0xb5758` walks from manager +16 and compares +8.
  The child (+12), parent (+16) and secondary (+20) fields are numeric IDs at
  the creation and cleanup sites the lane pins. Agreed.
- **Count projection.** The writer stores header +12 as-is (`0xb3930`). The
  record count is a separate list traversal (`0xb3b20..0xb3b3c`), and the
  reader's outer loop is bounded by that wire count (`380(r1)`, `0xb5654`).
  The reader restores +12 (`0xb4980`) and then adds 1 per insertion
  (`0xb4b0c..0xb4b18`).
- **Untraced mutation, narrowed.** The two calls that load the manager global
  (`0xb23a0` free, `0xb2760` init) run before the count restore. None of the
  21 direct callees after `0xb4980` loads the manager global within its
  first-`blr` extent (21 load sites exist in the binary). The 14 + 14 = 28
  projection therefore holds through direct-callee depth. Deeper callees and
  code after the caller's return are still untraced. The lane's wording ("a
  projection excluding untraced callee mutation, not a runtime bug") is
  correct and should stay.

Independent fixture walk (`script_walk`: stdlib zlib/struct only, native read
order):

- `RSSE` at 1,595,542, header size 20, initialized 1, pass 6055, nextID 16,
  count 14.
- 14 records with IDs `15..6,4,3,2,1`. The walk ends at **1,606,398**, where
  the next word is `ESSR`.
- 3 object bindings.
- Nonzero deadlines: ID 9 WAIT 114,377,145, ID 8 WAIT 114,377,133, ID 4
  animation 114,193,871, ID 3 WAIT 114,374,867.

Every value equals the lane's.

**New qualification (LOW, doc only).** After the name block, the reader also
rewrites three fixed fields:
- +212 is recreated via `0x18d100`/`0xbc0e0` when nonzero (`0xb54bc`).
- +230 is forced to `0xffff` (`0xb54c8`).
- +176 is cleared and then rebuilt from the `OBJ ` list (`0xb54d8`).

The graph witness does not expose these fields, so it is not wrong. But
"preserve the raw words" must not extend to +212/+230 in a future restore. Add
them to the clock doc's list of load-time rewrites.

Lane tests: 52/52 in place with `OPENTPW_PPC_BIN_ROOT` and
`OPENTPW_PPC_SAVE_PATH`, zero skips.

### Rides `0973fbe` (topology gates): accepted; one prerequisite to state

Re-decoded (`topology_order`) and the lane's JSON reproduced byte-for-byte:
- **Ordinal, not type.** The saver's predicate is section +0 == 2 (`0x3998c`).
  The auxiliary block is that node's own link count (+176) and the linked
  sections' +0 ordinals (via the +180 pointers).
- **Filter after auxiliary.** The flag-0x10 filter (`0x39a90/0x39a94`) comes
  after the auxiliary write in the same node iteration.
- **Third node on load.** The loader reads the auxiliary block into
  controller +84 → +100 → +100, the **third** list node (`0x3a344..0x3a368`).
  It does so after the initial topology (`0x366bc`) and before the 34-byte
  record loop (`0x3a440/0x3a444`). Both arms of the `0x3a330` header-flag-2
  branch reach `0x3a36c`.
- **Link resolution.** `0x41dcc` walks controller +84 by ordinal, so an
  ordinal is a list position.
- **Failure paths.** The nine short-I/O paths return r3 = 0 to the epilogue.
  The load-mode global is set at `0x3a1c0` and reset only at `0x3a72c`. The
  nested helpers' returns are untested at `0x39d84` and `0x3aa38`. The lane's
  wording ("no rollback proved", "does not prove the application ignores
  failures") is correct.

**Prerequisite to state (MED for any decoder, not a defect in `0973fbe`).** The
saver makes **one** list walk per controller: auxiliary check, then filter,
then record. The loader reads the auxiliary block **before every** record. The
streams therefore align only if:
- every node before the ordinal-2 node is flag-0x10 filtered (or ordinal 2
  heads the list), and
- exactly one ordinal-2 node exists.

Otherwise the saver writes records ahead of the auxiliary block that the
loader expects first. The lane's "valid initial-node/filter invariants"
covers this only implicitly. Write it out as an explicit stream-order
invariant before a nonempty fixture is decoded.

Tests: 24/24 in place with `OPENTPW_MAC_APP` and `OPENTPW_PC_FIXTURE`, zero
skips. Without them: 16 pass, 8 skip. The empty fixture still corroborates
none of the nonempty branches.

### UI `917eb36` (catalog events): accepted

Re-decoded (`catalog_delivery`):
- **Synchronous delivery.** `0x170f98` calls `0x181398` only for a live target
  (byte 0 == 0) and returns −1 otherwise.
- **Queued delivery.** `0x170ec8` → `0x16fd80` writes the 16-byte record
  {target, event, source, payload} at ring index +12, which counts down
  (`0x16fdf4`).
- **Row ID.** Accessor `0x17991c` walks from +352 via `0x17835c`. It reads the
  record word +4 from the +324 buffer and fails for an ordinal < 0, an
  ordinal ≥ +336, or a null buffer.
- **Activation.** The path takes the low 16 bits (`0x1639a4`) and requires a
  value > 0.
- **Distinct keys.** The sort selector, the row catalog ID and the
  root-name drawing key are distinct, as the lane states.

Two precision notes (LOW, not claimed by the lane and not contradicted by it):
1. **Queue drops.** An occupied ring slot skips the write and returns 0
   (`0x16fd98` → `0x16fe14`), so queued 1024/1025 can be dropped under
   overflow. A port must not promise delivery of every activation.
2. **Unchecked row-ID status.** Both root call sites (`0x163998`, `0x163d74`)
   read the accessor's out slot without testing r3. Native behaviour on an
   out-of-range ordinal therefore uses an unwritten stack slot. A port should
   reject rather than invent an ID.

The sort sign is negative only when +362 == 1 exactly (`0x17a4c0`), which
agrees with "direction bit".

Tests: 9/9 catalog tests with `UI_EVIDENCE_BIN_ROOT`. Without it, 3 pass and
6 skip.

### Economy `95fd383` (scientist JSON envelope): accepted with one LOW

Committed while this round ran, and the tree was clean when inspected. The
harness ran from a scratch copy (SDK 8.0.425, Release, actual fixture) and
passed **16/16 groups, 1,648 assertions**. The envelope is standalone and
never touches `ParkSaveFile`. It is shape-checked at every level, and both
`SourcePayloadRevalidated` and `CanRestoreRuntimeStaff` are always false.
Floats are stored as raw bits and the u64 hire time as 16 hex digits.

**LOW: the fixture qualification survives a reshaped prefix.** Probe (scratch
only, not committed): take the actual fixture envelope and keep only the last
guest and the scientist, with offsets made contiguous from the head (scientist
header 1,386,058). `Deserialize` accepts it, and it still carries
`IdentifiedPcEasymodeFixture`. The identified fixture's shape is fully known:
13 rows, scientist header 1,391,921, tail 215,879, person SHA. Either pin that
shape for this qualification, or name it a persisted claim on load. The doc
already says provenance is a claim, so this is not a correctness blocker.

Still open: the harness is not registered in `run_evidence_checks.py`
`SELF_TESTS`. This is unchanged since round 9, and registration waits until
the source is accepted.

### Formats `ab444e3` (constructor reconciliation): seen, not re-tested

The constructor is `ObjectAnimator(ModelFile, float ticksPerSecond = 30, bool
keepsPoseOnClipChange = false)`. This adds a trailing parameter to main's
`(model, ticksPerSecond = DefaultTicksPerSecond)`. Call sites in `ab444e3` use
the default rate or `15`. This matches the round-9 working tree, and the
constructor collision (round-9 blocker 1) is resolved **in a commit**. Its
.NET tests were not re-run in this round.

### Status

| Item | State |
| --- | --- |
| Clock `21bd7ef` | Accepted (static, one fixture). +212/+230 rewrite note LOW. Count projection holds through direct callees |
| Rides `0973fbe` | Accepted. Stream-order invariant must be explicit before a decoder |
| UI `917eb36` | Accepted. Queue-drop and unchecked row-ID notes LOW |
| Economy `95fd383` | Accepted (standalone). Fixture-qualification shape LOW. Not registered in the runner |
| Formats `ab444e3` | Collision resolved in a commit. Tests not re-run this round |
| Integration | **Pending** (`ppc_advisor`). `integration-remote` is still `f468f03` + `MERGE_HEAD` `778ea5d`. `origin/main` is now `e31c804`, past `78dfb5e`. The source-freeze verification is still pending |
| Runner `OPENTPW_PC_FIXTURE` | **Handoff, not merged** (round 9) |

Not repeated as fixed: the root `PPC-rides.md` min/max wording, the TPI
`SaveReader` diagnosis and ECON-007. None of them is on a committed root ref
reviewed here, and the owners' uncommitted fixes are not counted.

Review tests: 132 with both variables set, zero skips. Without them: 113 pass,
19 skip.

Unresolved objections carried forward: no PC or Patch 2 runtime equivalence for
any Mac-derived rule; formats per-channel clock source (`0xa6398` +16400/+16408)
unmodelled; Instant Action policy (section 34); SDT bank remap (advisor); TPI
history blob (section 38).

## 47. Round 11: origin `e31c804` against the committed integration `0d58bd4`

Scope: the eight origin commits `778ea5d..e31c804`: texture pack (`127d9e6`,
`88edd7d`, `5c592c9`), world capture (`78dfb5e`), text fields and scrolling
(`4b21e61`), online screens (`1e31596`), game and CD folders (`05a25e8`) and
input focus (`e31c804`). They are checked against the root side
(`1441ead..f468f03`) and the owner's committed merge. During this round the
owner committed the `778ea5d` merge as **`0d58bd4`** (parents `f468f03`,
`778ea5d`). `integration-remote` now has `MERGE_HEAD` `e31c804` with two
unmerged paths. That pending resolution is **not accepted** here. All results
come from committed objects (`git show` and `git merge-tree`) plus a scratch
copy in `/tmp`. Nothing was written to the owner's tree. Native UI process
cleanup and the PATH/picker lifecycle belong to the Native UI lane and are not
re-audited.

### Merge shape

`git merge-tree 0d58bd4 e31c804` conflicts in exactly two files:
`docs/FIDELITY-REGISTER.md` and `source/OpenTPW/Client/Setup/SetupWizard.cs`.
The six README, LIPS, PROGRESS and MP2 conflicts from the `778ea5d` merge are
gone because `0d58bd4` resolved them. The new commits touch none of those files.

### Findings

1. **HIGH: the SetupWizard conflict compiles with neither side.** The auto-merge
   takes `05a25e8`'s `enum Page { Welcome, GameFolder, Done }` and removes the
   `cdPath` field. Non-conflicting lines from `0d58bd4`'s bounded inspection
   still use the CD path: `inspectingCd ? cdPath : gamePath`,
   `if ( inspectingCd ) cdReport = report;` and
   `page == Page.Cd ? cdReport?.Path`.
   - Taking "ours" brings back `Page.Cd`, `DrawCd` and `SetCdPath`.
   - Taking "theirs" keeps those stale lines. It also brings back the in-process
     `Directory.Exists( dropped )` that `0d58bd4` deliberately removed ("File/directory
     inspection happens in the bounded child").

   Verified resolution, compiled in a scratch copy:
   - hunk 1: `Task<InstallationDiscoveryResult>? picker` without `cdReport`;
   - hunk 2: ours (`pendingInspection = (false, gamePath)`);
   - hunk 3: `var folder = dropped;` then `SetGamePath`/`Page.GameFolder`, with no CD branch;
   - hunks 4 and 5: theirs (`Done` and `savedCd`);
   - hunk 6: drop `SetCdPath`.

   Then remove `inspectingCd` (field and assignment), its two uses, and the
   `Page.Cd` branch in Browse. Result: OpenTPW.Tests builds with 0 errors. The
   full suite has 972 tests: 750 pass, 222 skip without original assets, 0 fail.
   The 6 affected classes (NativeWidget, OriginalUi, TexturePack,
   UiModelBinding, Setup/Installation, Online/ParkSharing) have 150 tests:
   135 pass, 15 asset skips.
2. **MEDIUM: the new in-game Game files screen bypasses the bounded child.**
   `GameFilesScreen.cs` (`05a25e8`) calls `GameInstallation.Inspect( path )`
   twice, synchronously on the render/update thread. It also calls
   `Task.Run( () => FolderPicker.Pick(...) )` in-process. On `0d58bd4`,
   `InstallationDiscovery` provides `InspectAsync`/`PickAsync` (a child process,
   with `DefaultTimeout` of 3 s) for exactly this case. The setup window uses
   them; the options screen would not. This is a textual no-conflict, so it
   merges silently. It is handed off to the Native UI owner to bind to
   `InspectAsync`/`PickAsync`. Its lifecycle is not audited here.
3. **LOW: regenerate the register instead of hand-merging it.**
   `FIDELITY-REGISTER.md` is generated with line numbers. On the scratch merge,
   `tools/fidelity_register.py --write` followed by `--check` passes with 136
   unresolved APPROX IDs. The new `EXT:SETUP`, `EXT:texture-pack` and
   `EXT:world-capture` rows need no declaration edits.

### Checked, no hazard

- **Input focus:** `GameFlow.Update` sets `Input.TextEntryActive = false`
  before `Hud.Update` or `Menu.Stack.Update`. A focused `UiTextField` sets the
  flag and returns `true`. `Input` WASD (4 sites) and `Bindings` (`isPressed = !TextEntryActive`)
  read the previous frame's flag, which is a one-frame lag only.
  `Renderer`'s `EditorToggle` goes through `Bindings`. ParkHud's
  `P` pause toggle runs only when `!Paused`, and online screens push onto the
  stack, so `Paused` is true while typing. No root-side commit adds raw keyboard reads.
- **Read-only visits:** Publish is enabled only for
  `level != null && !level.IsReadOnlyVisit`, and `ParkSharing.ExportLevel`
  still throws on a visit. The new Visit goes through `StartLevel(..., visit)`.
- **UI models:** the root (`ae788a3`/`864f82d`) moves `UiModels` to
  `UiModels.cs`, with root-name keys and strict collisions. Origin still has
  the old class in `UiWidgets.cs`. The deletion auto-merges and leaves no
  duplicate type. New callers use `Context.Models.Get(name)`, whose
  case-insensitive alias fallback still resolves `f_text1`. Tests that use the
  throwing loader get `null` (fallback drawing) and never hit `Register`
  collisions. Not qualified: whether any new widget name exactly equals a
  *different* asset's root name. That needs ui.wad.
- **Render ownership:** `WorldCapture` reads `ResolveColorTexture` (`Own(...)`,
  `B8_G8_R8_A8_UNorm` on `0d58bd4`) through its own `using` staging texture.
  The texture pack only redirects `UpdateFromWct` to `UpdateFromStb` for an
  existing PNG. The builder skips interface textures. The pack is built locally
  from the player's install, is never shipped, and is off by default. Original
  assets are untouched.
- **Economy/catalog:** the new commits do not touch economy, VM or save files.

### Test handoff

`tools/ppc-analysis/lanes/review/test_round11.py` has six git-object bindings,
covering the conflict set, the both-sides-fail evidence, the GameFiles bypass,
focus gating, the read-only publish gate and the UiModels move. They need no
assets and skip when the commits are absent. Supplementary tests for the owner
(generated fixtures, no originals):
- GameFilesScreen through a stubbed `InspectAsync` that never completes, to
  show the UI stays responsive;
- a `UiTextField` focused in ParkHud while `P` and `Escape` are pressed;
- `Input.TextEntryActive` reset after a Visit transition.

`OPENTPW_PC_FIXTURE` and the scientist runner registration are still a future
handoff after the lanes merge.

### Status

| Item | State |
| --- | --- |
| `0d58bd4` (`778ea5d` merge) | Committed by owner. Not re-tested here beyond the scratch merge |
| `e31c804` merge | **Pending**. Two unmerged paths. HIGH 1 must be hand-resolved |
| GameFilesScreen bounded inspection | Handoff to Native UI (MEDIUM 2) |
| Register | Regenerate (LOW 3) |

Unresolved objections carried forward unchanged: no PC or Patch 2 runtime
equivalence for Mac-derived rules; the formats per-channel clock source is
unmodelled; TPI history blob qualification (section 38); SDT bank remap;
Instant Action policy.

Review tests: 138 without the fixture variables (119 pass, 19 skip).

## 48. Round 12: `--pc-fixture` and scientist harness registration in the runner

Scope: only `tools/ppc-analysis/run_evidence_checks.py`, the new
`lanes/review/test_round12.py` and this file. No production source, workflow or
peer tree was written. No asset directory was listed. The only original file
read is the one supplied `Easymode.TPWI` (38,479 bytes, SHA-256
`6d89303d…`, the identity that both the rides lane and the scientist harness
pin). Root `main` is still `0d58bd4`, and its runner is byte-identical to this
lane's round-9 runner, so this round applies to it unchanged. The integration
owner has not committed the `e31c804` resolution, so that work is not inspected.
Native UI lifecycle work (final `Dispose` stack cancellation, Linux manual path
entry) belongs to the Native UI reviewer and is not duplicated here.

### Runner changes

- **`--pc-fixture PATH`** sets `OPENTPW_PC_FIXTURE`, which is now the fourth
  entry in `FIXTURE_VARIABLES`. An inherited value is always stripped, so a
  stale shell value never reaches a Python lane or a .NET harness. Before this
  round, `run_dotnet` copied the full `os.environ`. It now receives the
  stripped fixture environment.
- **Never inferred.** `--pc-data` does not imply the fixture, even when
  `levels/jungle/Easymode.TPWI` exists below it.
- **Shape check only, with no hashing in the runner.** The path must be a
  regular `.TPWI` file outside the checkout, so original assets stay out of
  Git. Its size must be greater than `0x629` (the container header) and at
  most 8 MiB (the scientist reader's bound). Identity stays with the lanes. A
  failed check exits with code 2 before any lane runs.
- **Scientist registration.** `lanes/economy/OriginalScientistSnapshot.Tests.csproj`
  (net8.0) is in `SELF_TESTS`. `FIXTURE_ARGUMENTS` appends `--fixture PATH` to
  its command only when `--pc-fixture` is given. As before, it runs from a
  temporary copy outside the SDK 10 `global.json`.
- **Honest coverage.**
  - A registered harness that the checkout lacks is reported as `absent` (no
    coverage claimed). This is what happens for the scientist on `main`.
  - A harness with no registration stays `not-run` (for example the rides
    `ControllersWitness.csproj`).
  - Without `--dotnet`, every harness is still listed.
  - A fixture flag counts as consumed only when the target checkout actually
    reads it: a ppc-analysis source must read one of the flag's variables
    through `environ[...]`, `environ.get(...)` or `getenv(...)`, or a present
    harness must take the flag. Naming the variable in synthetic data does not
    count. A supplied flag that nothing reads is printed as `note`.
- **`--require-fixtures`** now fails on:
  - any Python skip, as before;
  - a harness `NOT RUN:` fixture line (new status `fixture-skipped`);
  - a supplied fixture flag that nothing reads.

  Without `--require-fixtures`, a fixture that was deliberately left out never
  fails CI. A registered harness that is absent never fails either, because a
  missing project is not a skipped fixture.

### Tests (`test_round12.py`, 14, synthetic, with no assets or SDK)

- Stale-value stripping, both with and without the flag.
- `--pc-data` does not derive the fixture.
- Rejection of a missing path, a directory, the wrong suffix, a header-only
  file, an oversized file and a path inside the checkout.
- `main` exits 2 on a missing path.
- Registration invariants, and arguments added only when the fixture is
  supplied.
- A registered project is reported absent until it exists in the checkout.
- Consumer detection, including a helper-module read, and synthetic dict
  literals not counted.
- A fake POSIX `dotnet`:
  - shows that a stale `os.environ` value does not reach the harness;
  - gives `fixture-skipped` only under `--require-fixtures`;
  - receives `--fixture <resolved path>` when the fixture is supplied.
- `main` exit codes for an unread fixture (`note` → 0, required → 1).
- `--mac-bin` counts as consumed when either of its two variables is read.

The fake-`dotnet` class is skipped on Windows.

### Results (stale `OPENTPW_PC_FIXTURE=/stale/nowhere.TPWI` exported for every run)

| Target | Command | Result |
| --- | --- | --- |
| This tree | CI (no flags) | OK, 290 tests, 31 skipped |
| This tree | `--mac-bin --pc-data --pc-fixture --dotnet` SDK 8 | OK, 290 tests, 8 skipped (UI corpus). Review 152/152. `--pc-fixture` note: nothing here reads it |
| `main` `0d58bd4` | CI | OK, 230 tests, 16 skipped. Scientist `absent` |
| `main` `0d58bd4` | `--mac-bin --require-fixtures` | **FAILED**: UI 8 skips (`UI_EVIDENCE_*` not supplied). Everything else is 0 skips |
| `main` `0d58bd4` | `--pc-data --pc-fixture --require-fixtures` | **FAILED**: nothing on `main` reads `OPENTPW_PC_DATA` or `OPENTPW_PC_FIXTURE` |
| `ppc-economy` `62c0a2e` | `--pc-fixture --require-fixtures --dotnet` SDK 8.0.425 | OK. Scientist **16/16 groups, 1,658 assertions** with the actual fixture. Loans 25/25 |
| `ppc-economy` `62c0a2e` | `--require-fixtures --dotnet` (no fixture) | **FAILED**, as intended: scientist `fixture-skipped` |
| `ppc-economy` `62c0a2e` | `--dotnet` (no fixture) | OK. Scientist passes its synthetic groups and reports `NOT RUN` |
| `ppc-rides` `84fe814` | `--mac-bin --pc-fixture --require-fixtures` | OK, rides 24/24, 0 skipped |

Not covered, listed honestly:
- `main` has no scientist project and no rides `OPENTPW_PC_FIXTURE` reader yet.
- The UI corpus variables still pass through without a flag.
- `Layer1Corpus` and `CorpusWitness` remain `not-run` (private corpus).
- The rides `ControllersWitness` has no registered self-test.
- No combined scratch merge of economy and rides onto `main` was run.

The scientist pass is a self-consistency check against one identified PC
fixture. It is not evidence of original runtime restoration (both
`SourcePayloadRevalidated` and `CanRestoreRuntimeStaff` remain false).

### Handoff to the integration owner

When the economy (`62c0a2e`) and rides (`84fe814`) lanes merge, take this
runner as a whole. CI stays without fixture flags. The private command is:

```sh
UI_EVIDENCE_BIN_ROOT=… UI_EVIDENCE_MAC_UI=… UI_EVIDENCE_MAC_UITEXT=… \
UI_EVIDENCE_MAC_UIHELP=… UI_EVIDENCE_MAC_MBTOUNI=… UI_EVIDENCE_RESIDX=… UI_EVIDENCE_PC_UI=… \
python3 -I -B tools/ppc-analysis/run_evidence_checks.py --mac-bin $FERAL_BIN \
  --pc-data $TPW_DATA --pc-fixture $TPW_DATA/levels/jungle/Easymode.TPWI \
  --require-fixtures --dotnet $DOTNET8/dotnet
```

On today's `main`, leave out `--pc-data` and `--pc-fixture`: with
`--require-fixtures` they correctly fail as unread.

Unresolved objections carried forward unchanged: no PC or Patch 2 runtime
equivalence for Mac-derived rules; the formats per-channel clock source is
unmodelled; SDT bank remap; TPI history blob qualification (section 38);
Instant Action policy.

Review tests: 152 (133 pass and 19 skip without fixture variables; 152 pass
with `--mac-bin --pc-data`).

## 49. Round 13: runner `0f4ae55` fixture trust, formats `ab444e3`, clock selector and snapshot state

Scope: own files only (`lanes/review/round13_evidence.py`, `test_round13.py`
and this section). No production, workflow or peer tree was written. The only
original files read were the two supplied fixtures, each by stat and hash
(`SimThemePark.data` 2,274,758 bytes; `Easymode.TPWI` 38,479 bytes, SHA-256
`6d89303d…`). No asset directory was listed. Root `main` is still `0d58bd4`,
and no integration commit after it exists, so the **final integration snapshot
is pending**. Its checks below were run on `0d58bd4` and the lane heads. Native
UI lifecycle work (mixed CD remnants, bounded `GameFilesScreen`, `Dispose`
cancellation, manual path actions, busy-state typed actions) belongs to the
Native UI reviewer and is not duplicated here.

### Runner `0f4ae55`: one MEDIUM, one LOW (latent), the rest holds

**MEDIUM: `--require-fixtures` passes with a fixture nothing consumed when
`--dotnet` is omitted.** `fixture_consumers` counts a *present* fixture-argument
harness as a consumer, whether or not it runs. Probe: scratch `git archive` of
`ppc-economy` `62c0a2e`, stale `OPENTPW_PC_FIXTURE` exported, then
`--pc-fixture <actual> --require-fixtures` with no `--dotnet`. Result: **exit
0**. The scientist harness is `not-run no --dotnet`, no Python source in that
tree reads the variable, and `unconsumed_fixtures` is empty. With `--dotnet`
the same tree is correct: the harness passes with `--fixture` (round 12), or
fails as `fixture-skipped`. Fix options (runner owner's choice):
- under `--require-fixtures`, fail when `--pc-fixture` is supplied without
  `--dotnet` and no Python reader exists; or
- count a harness as a consumer only after it reports `passed` with the
  fixture argument.

`round13_evidence.vacuous_fixture_consumers(report, FIXTURE_ARGUMENTS)` flags
this case from a `--json` report. It flags `OPENTPW_PC_FIXTURE` for the
`62c0a2e` probe and nothing once a Python reader or a passing fixture run
exists.

**LOW (latent): linked harness items fail in the scratch copy.** `run_dotnet`
copies only `project.parent`. Rides `84fe814`'s unregistered
`controllers/ControllersWitness.csproj` has `None Include="../controller-contracts.json"`
with `CopyToOutputDirectory`. Probe: register it in memory only, then run it
with SDK 8.0.425. Result: `MSB3030 Could not copy … controller-contracts.json`,
classified as generic `failed`. This fails closed, but it is misleading. If the
owner registers that witness, the copy must preserve the lane-relative layout,
or the runner must report `linked-outside-copy`. `Layer1Corpus` (links
`source/…` files) and `CorpusWitness` (references `OpenTPW.Files`) are
`NEEDS_CORPUS` and never copied. `linked_outside_copy` was applied to every
present `SELF_TESTS` project in `0d58bd4`, `62c0a2e`, `84fe814`, `1d3b8d4` and
`6d1b8d9`: no include climbs out. A test pins this for the current checkout.

Holds under realistic absences:
- Registered projects missing from a tree are `absent`, never coverage and
  never consumers. This was checked on the economy-only archive: seven absent,
  and the scientist is counted only when present.
- Stale `OPENTPW_PC_FIXTURE` is stripped for both Python lanes and harness
  processes.
- The fixture is never inferred from `--pc-data`. The review lane's own
  round-10 walk derives the path from `OPENTPW_PC_DATA`, but pins both the
  container and payload SHA-256, so it cannot pass on another file.
- No SDK 8 registered harness references a newer project. `CorpusWitness`:
  rides `84fe814` only adds a `Compile Remove` line, and main's `net10.0` hunk
  does not overlap it, so a 3-way merge keeps `net10.0` and no
  `incompatible-reference` failure appears.
- Scenarios `1d3b8d4` adds `OriginalProgressionContract.Tests.csproj`, which is
  unregistered and therefore `not-run`. Coverage is not claimed for it.

### Caller metadata and original-source reauthentication (economy `62c0a2e`)

`PersistedOriginalScientistSnapshot.SourcePayloadRevalidated` and
`CanRestoreRuntimeStaff` are constant `false`. No JSON field can set them, and
unknown members are rejected. So caller metadata **cannot claim reauthentication**
through the wrapper. Round 10's LOW is fixed: the identified label now requires
the exact 13-row prefix, scientist record and tail 215,879.

Residual qualification (not a blocker): under `IdentifiedPcEasymodeFixture`,
`Validate` pins provenance and shape but not the scalars. `GradeWord`,
`RawStateWord`, happiness/energy bits, ticks, `PersonRecordSha256` and the
inline name are not pinned. The name only has to match its own digest. So a
caller envelope with altered scalars keeps the identified label on
`persisted.Snapshot.Provenance.Qualification`. That inner object has the same
type a direct reader produces. Consumers must gate on the wrapper, not on the
inner label. This is a static finding: no SDK build was run for it. Since the
label names one file, the cheap fix is to pin every scalar to the values that
`ActualFixture` already asserts. The alternative is to downgrade the label on
load.

### Formats `ab444e3` (per-animator rate): accepted with qualifications

- **Rate compatibility.** The constructor is `(model, ticksPerSecond = 30,
  keepsPoseOnClipChange = false)`, a superset of main's `(model, ticksPerSecond
  = 30)`. Main's rate test is carried verbatim. A positional `bool` cannot bind
  to `float`, so old call sites fail at compile time instead of silently
  changing. `git merge-tree 0d58bd4 ab444e3` leaves **one conflict** in
  `ObjectAnimator.cs`: the constructor line and the `keepsPoseOnClipChange`
  assignment. The resolution takes `ab444e3`. The merged `GetFrame` and carry
  use the instance rate once, and `Play` and the player use it consistently.
  At 30 the single-precision steps match the `0xa6484`/`0xa6398` formulas.
- **15 ticks/s test scope.** I recomputed the arithmetic by hand:
  - endpoint 2000 ms → 30, not past the end;
  - +100 ms → carry 1.5 ticks → 100 ms (the 30-rate formula would give 50 ms);
  - a far-past update caps at the duration;
  - 10 ticks at 667 ms → 0.333 ms truncates to 0;
  - the fixed-item pose holds through a matrix-only clip.

  This is internal consistency of an OpenTPW extension, not original
  evidence. The doc says so. A rate other than 30 must not be presented as
  game-speed emulation. The original scales the selected clock's milliseconds
  and keeps `1000 × carry / 30`, so truncation happens in different units.
- **Two channel clocks: limitation preserved, not modelled.** `0xa6398` picks
  the bind start from +16408 (channel flag 0x40) or +16400. One animator clock
  per object remains, and which clock feeds `now` in `0xa6484` is still
  untraced.
- **Docs/register freshness.**
  - `OBJECTS.md` still says "30 ticks/s (unverified)", while `MD2-MODELS.md`
    says Mac speed 1.0 is proven. `RIDES-001` still reads "original tick rate
    not verified". These carry forward from round 9.
  - `fidelity_register.py --check` fails on `ab444e3` (RIDES-001/002/003 lines
    move to 25/256/198), and already failed on its parent `2d3442f` (older
    base). `main` `0d58bd4` passes (136 IDs).
  - CI runs `--check`, so the integration must regenerate the register after
    merging formats.

### Clock selector1 correction: pending (not committed)

`ppc-clock` is clean at `6d1b8d9`. That commit still says "Both visible paths
can reach saved-word/state reading". `ApplyPostLoadHook(existing, saved, bases,
1)` returns `existing` but takes `saved` words that a source early-success path
would never read. Pending owner edits were not inspected or accepted. To accept
the committed correction:
- it cites the early-success branch, with a hash-pinned region;
- the doc sentence is replaced;
- the selector1 contract no longer implies the SSEM/RSE words were read;
- a test covers the no-load path;
- "selector1 is not GameType1" and "full lifecycle unqualified" stay.

### Integration snapshot checks (on `0d58bd4`; final snapshot pending)

- **Original assets.** Neither fixture's git blob hash (`245a6743…`,
  `816de5d1…`) exists in the object database, and no blob has either exact
  size. No asset-extension paths are in `0d58bd4`, `62c0a2e`, `84fe814`,
  `ab444e3`, `6d1b8d9`, `1d3b8d4` or `0f4ae55`. The only tree blob over 1 MB is
  upstream `content/textures/test.png` (2022).
- **Source aliases.** `0d58bd4` uses only the declared `[BIN:STP-PPC` alias (9
  sites), and the lane heads add none.
- **Register.** `0d58bd4` is fresh. `ab444e3` needs regeneration on merge, as
  above.

### Results

| Target | Command | Result |
| --- | --- | --- |
| This tree | CI (no flags, stale fixture exported) | OK, 295 tests, 31 skipped; review 157 (19 skip) |
| This tree | `--mac-bin --pc-data` | OK, 295 tests, 8 skipped (UI corpus); review 157/157 |
| `62c0a2e` archive | `--pc-fixture --require-fixtures` (no `--dotnet`) | **exit 0**, `vacuous_fixture_consumers = [OPENTPW_PC_FIXTURE]` (MEDIUM) |
| `84fe814` archive | `ControllersWitness` in-memory registration, SDK 8.0.425 | `failed` (MSB3030 linked file) (LOW, latent) |

Not run: a full codec rerun, a combined economy+rides+formats scratch merge
build, the UI corpus, original runtime and PC/Patch 2 parity.

Unresolved objections carried forward unchanged:
- no PC or Patch 2 runtime equivalence for Mac-derived rules;
- the formats per-channel clock source (two clocks) is unmodelled;
- SDT bank remap;
- TPI history blob qualification (section 38);
- Instant Action policy.

### Handoff

1. Runner owner: close the MEDIUM gap before relying on
   `--require-fixtures --pc-fixture` without `--dotnet`. Until then, always pass
   `--dotnet $DOTNET8/dotnet` with `--pc-fixture`. Take the round-12 runner
   whole when economy and rides merge, as before.
2. Integration owner, after merging formats `ab444e3`:
   - resolve the `ObjectAnimator.cs` constructor conflict toward `ab444e3`;
   - run `python3 tools/fidelity_register.py --write`;
   - re-describe RIDES-001 as "30 in the Mac channel clock at speed 1.0; PC and
     clock selection unverified".
3. Economy owner: optionally pin the identified-label scalars, or downgrade the
   label on load.
4. Clock owner: commit the selector1 correction with the criteria above. Review
   will accept it only from a commit.
5. Re-run this section's snapshot checks (asset blobs, aliases,
   `fidelity_register.py --check`) on the final integration commit.

Review tests: 157 (138 pass and 19 skip without fixture variables; 157 pass
with `--mac-bin --pc-data`).

## 50. Round 14: runner fixture consumers closed, `34c75be` host selection, final checkpoint containment

Scope: the round-13 MEDIUM runner gap (this round the review lane owns
`run_evidence_checks.py`), plus review tests and this section. No production,
workflow or peer tree was written. No original asset was read: the actual
fixture paths were not supplied this round, so every fixture probe used a
zero-filled synthetic `Easymode.TPWI` of valid shape outside the checkout. No
asset directory was listed and no filesystem-wide search was run.

Reviewed checkpoint: `46c5b6c` (last executable snapshot, contains remote
`f4482c4`) → `0b67f02` (only `docs/reverse/APPROX-TRACE.md`, +58) → `34c75be`
(Windows native-testhost fix; root `main`, pushed). Remote `255cc7b` descends
from `34c75be` with 5 commits of parallel gameplay work (advisor, economy,
research; 10 files). That is **later source, not the reviewed checkpoint**, and
nothing below accepts it.

### Runner: MEDIUM closed

- **Consumers are what actually used the fixture.** `fixture_consumers` now
  counts Python sources that read the variable (every Python suite always runs)
  and fixture-argument harnesses whose result is `passed`, carries the fixture
  option in `arguments`, and has no `NOT RUN:` line (`used_fixture`). A harness
  that is present but not run (no `--dotnet`), absent, skipped, failed or
  `fixture-skipped` is not a consumer. Consumers are computed after the .NET
  pass, so the report reflects the run, not the tree.
- **Dependency-copy qualification.** `linked_outside_copy` (moved from the
  round-13 aid into the runner) lists `Compile`, `ProjectReference`, `None`,
  `Content` and `EmbeddedResource` includes that climb out of the project
  directory, including `$(MSBuildThisFileDirectory)`/`$(MSBuildProjectDirectory)`
  prefixes, backslashes and multi-line elements. A *registered* harness with
  such an item is `linked-outside-copy` (a failure) and is never built, so no
  misleading MSB3030 `failed` appears. An *unregistered* one stays `not-run`,
  with the reason qualified: rides `84fe814`
  `controllers/ControllersWitness.csproj` reports `no registered self-test;
  scratch copy would lack None ../controller-contracts.json`. It should stay
  unregistered until the scratch copy can carry its external JSON resource.
- **Kept:** per-lane discovery processes, stale-variable stripping (Python and
  harness processes), no inference from `--pc-data`, `absent` for registered
  projects missing from a tree, SDK 8 scratch copies outside the checkout with
  no `bin`/`obj` written back.
- Round-12 and round-13 tests that pinned the old presence-based count now pin
  the closed behaviour. `round13_evidence.vacuous_fixture_consumers` still flags
  a `0f4ae55`-shaped report.

New `test_round14.py` (11 tests; a POSIX fake `dotnet` logs project, working
directory, arguments and the inherited `OPENTPW_PC_FIXTURE`). Before the fix:
7 failures and 4 errors. After: all pass. They cover:
- no `--dotnet`: exit 1 under `--require-fixtures`, only a note without it;
- a passing harness with `--fixture <resolved path>` is the consumer;
- a `NOT RUN:` or failed harness is not; `--require-fixtures` turns the
  `NOT RUN:` case into `fixture-skipped`;
- a Python reader still consumes without `--dotnet`;
- an absent harness is never run or counted;
- each harness runs from its own scratch directory outside the checkout, with
  no stale fixture inherited;
- linked-include parsing; ControllersWitness qualified both with and without
  `--dotnet`; an in-memory registration fails closed before any build;
- no registered self-test in this checkout links outside its copy.

### `34c75be` `ChildStartForHost`: accepted

- **Identity.** `applicationHost` is `GetEntryAssembly() == typeof(Program).Assembly`.
  Cases:
  - application apphost (any name): host = own executable, no assembly
    argument;
  - `dotnet` muxer (`dotnet OpenTPW.dll`, or a testhost run via `dotnet`): host =
    that muxer, assembly path prepended;
  - native testhost (Windows `testhost.exe`): host = `DOTNET_HOST_PATH`, else
    `DOTNET_ROOT/dotnet[.exe]`, else `dotnet` on `PATH`, assembly path
    prepended.

  A null entry assembly falls to the managed path, which is the safe side.
- **Arguments.** Everything goes through `ArgumentList`: no quoting, folders
  with spaces survive, and the picker's empty `initial` stays one empty argument.
  `UseShellExecute` is false and stdout/stderr are redirected, as before.
- **CI.** Run `37998226690` (head `34c75be5f8…`): ubuntu, windows and macOS
  all succeed. Windows `OpenTPW.Tests`: 757 passed, 0 failed, 230 skipped,
  which includes the two new regressions. The codec was not rerun.
- **Residual, LOW and informational only.** A muxer renamed away from
  `dotnet` (for example `dotnet-x64`) under the application entry assembly would
  be treated as the apphost. A single-file publish has an empty
  `Assembly.Location`, but it is always an application host, so the managed
  path is never taken there. Neither case is a supported launch path today.

### Final checkpoint containment (git objects only)

| Check | `46c5b6c` | `0b67f02` | `34c75be` | `255cc7b` (later, not reviewed) |
| --- | --- | --- | --- | --- |
| Fixture blob prefixes `245a6743`/`816de5d1` in the object DB | absent | absent | absent | absent |
| Blob of size 2,274,758 or 38,479 | none | none | none | none |
| Original-asset extension paths | 0 | 0 | 0 | 0 |
| Blobs > 1 MB | upstream `content/textures/test.png` only | same | same | same |
| `[BIN:` aliases in `source/` | 9, all `STP-PPC` | 9 | 9 | 13, all `STP-PPC` |
| `fidelity_register.py --check` | — | — | exit 0, 136 APPROX IDs | exit 0, 135 |

Every bracket label in `source/` at `34c75be` is `APPROX`, `DATA`, `EXT`,
`BIN:STP-PPC`, or a C# `[global::…]`/`[assembly:…]` attribute. There is no
undeclared alias (`BINARIES = ("STP-PPC", "TPI-EXE")`).

### Carried, not proven

- **Clock owner: calendar host-date conflict.** In `0b67f02` `APPROX-TRACE.md`,
  ECON-002 says the park clock "starts from the real local date and time read at
  clock construction (`FUN_100e3c90`)". That claim was Haiku search text: the
  skeptic was "refuted → contradicted" and there was no lead review. Section 37
  (clock `7f8ef64`) established only that the RSE handlers at
  `0xb2144`…`0xb2238` read host civil time, not the park calendar. Whether the
  park calendar is seeded from host time is a hypothesis for the clock owner,
  and it conflicts with any fixed-epoch reading. The search text is not
  evidence, and no fidelity claim may cite it.
- No PC or Patch 2 runtime equivalence, no channel-clock source, and no
  SDT-device claims. All objections from round 13 carry forward unchanged.

### Results

| Target | Command | Result |
| --- | --- | --- |
| This tree | `run_evidence_checks.py` (no flags) | OK, 306 tests, 31 skipped; review 168 (19 skip) |
| This tree | `--dotnet $DOTNET8/dotnet` (8.0.425) | OK; 6 registered harnesses pass in scratch copies; scientist `absent` |
| `62c0a2e` archive + new runner | `--pc-fixture <synthetic> --require-fixtures`, stale var exported | **exit 1**: `FAIL --pc-fixture … nothing that ran … used it` (was exit 0) |
| `62c0a2e` archive + new runner | same + `--dotnet` SDK 8 | exit 1: scientist rejects the synthetic file (15/16 groups) and is not counted; loans 25/25 |
| `84fe814` archive + new runner | `--dotnet` SDK 8 | OK; ControllersWitness `not-run … would lack None ../controller-contracts.json`; AnimationWitness passes |
| `84fe814` | ControllersWitness in-memory registration, `run_dotnet` SDK 8 | `linked-outside-copy`, no build (was MSB3030 `failed`) |
| `34c75be` | CI run `37998226690` | success on all three OS |

Not run: the actual-fixture positive path (`--pc-fixture <actual> --dotnet`),
because no fixture path was supplied (round 12 recorded 16/16 with the actual
file). Also not run: a codec rerun, UI corpus, original runtime and PC/Patch 2
parity.

### Handoff

1. Integration: take `run_evidence_checks.py` from this lane whole. The
   round-13 advice to always pass `--dotnet` with `--pc-fixture` is no longer
   needed for correctness, but it is still the only way to get a consumer from
   the scientist harness.
2. Rides owner: keep ControllersWitness unregistered until the runner's copy
   can carry `../controller-contracts.json`. One option is to copy the lane
   directory and run the project at its relative path.
3. Clock owner: settle the ECON-002 host-date seeding with a hash-pinned trace
   of `FUN_100e3c90`, or reject it.
4. Re-review `255cc7b` (or its successor) as a new checkpoint; it is not
   covered here.

## 51. Round 15: batch 2 partial integration, runner save-path variable, unmerged UI/rides/economy tips

Scope: `integration-batch2` as committed at **`ef6ff14`** (base `f635468`;
formats `ab444e3` → `2175677`, clock `b52dcff` → `5cb584c`, economy `62c0a2e` →
`76e7700`, rides `84fe814` → `ef6ff14`), plus unmerged tips UI `f51cc05` and
`b7bead5`, rides `c6b0906` and economy `60d86de`. The UI merge of `917eb36` was
still in progress (four files with conflict markers: `COMPATIBILITY.md`,
`SignCanvas.cs`, `SignFile.cs`, `SignFileTests.cs`), and review `4e173cb` was not
merged. Those resolutions are **pending**, not reviewed. The sign conflict audit
belongs to the UI owner and is not repeated here. Every check ran on `git archive`
copies under `/tmp`. Nothing was built or written in the batch-2 worktree.

### Committed batch 2 resolutions

| Merge | Conflict | Verdict |
| --- | --- | --- |
| `2175677` formats | `ObjectAnimator` constructor | Correct. It keeps main's `ticksPerSecond` finite/positive validation and `TicksPerSecond` assignment, together with the format lane's `keepsPoseOnClipChange` parameter, field and `RelativeAnimationFlag` mask. `OriginalObjectRuntime` passes `entry.IsFixedItem`. |
| `5cb584c` clock | none | Clean. |
| `76e7700` economy | none | Clean. No lab reader (`60d86de`) or RNG ancestry. Scientist harness is 16 groups / 1,658 assertions here, not 22 / 8,305. |
| `ef6ff14` rides | `PPC-rides.md` integration note | Correct. The rewritten note says that the helper through `84fe814` includes the capacity correction. `CoasterVehicleState.PlanAllowance` is `min(min(max(requested, globalMin), definition), train)`, and `PassengerRing.QueryRoom` floors by the global minimum, as the note says. |

`git diff --check f635468 ef6ff14` is clean. No later clock, RNG or lab commit is
an ancestor of `ef6ff14`.

### New finding R15-1 (corrected in this lane): clock real-save witnesses were never run by the runner

Four clock tests (`test_clock_epoch`, `test_calendar_epoch`, `test_save_phase`,
`test_saved_script_graph`) read the identified save as
**`OPENTPW_PPC_SAVE_PATH`**. The runner neither set that variable from
`--pc-fixture` nor stripped it from the environment. So on `ef6ff14`, the run
with `--pc-fixture <actual>` skipped all four (69 ran, 4 skipped). With
`--require-fixtures` the run would fail. If a stale shell value was present, the
tests ran against whatever file it named. Run directly with the actual
`Easymode.TPWI`, they pass: 69/69 with no skips. The witnesses were correct, but
the runner never exercised them.

Fix: `--pc-fixture` now sets both `OPENTPW_PC_FIXTURE` and
`OPENTPW_PPC_SAVE_PATH` from the one shape-checked path. Both are in
`FIXTURE_VARIABLES`, so both are stripped when the flag is absent. Consumer
accounting now credits clock. `test_round15.py` covers set, strip, the
`--require-fixtures` skip failure and the mapping; it fails 4/4 against the
`4e173cb` runner. The two round-12 message assertions now name both variables.
`UI_EVIDENCE_*` remains a documented pass-through and is unchanged.

### Unmerged lane tips

- **UI `f51cc05` stable catalog sort.** `linked_sort` does a strict
  first-extreme head selection, then strict insertion, recomputing the ordinal
  predecessor after each relink. I compared it against Python's stable `sorted`
  in 4,000 random trials: 0–12 rows, both directions, duplicate signed and text
  keys, item IDs −2/−1/0/5/7 and shuffled links. Every result matched. Marker −1
  versus item ID −1/−2 stays separate, and hierarchical rows and high-bit UTF-16
  are rejected. Accepted as a flat-root reference only.
- **UI `b7bead5` key/mouse/queue/invalid-row witnesses.** `QueueReference` is
  FIFO on descending indexes. It rejects a full slot without overwriting it.
  `emit_normal` reports success independently of enqueue, so a reported success
  is not a delivery. An invalid nonempty ordinal returns the head, and the false
  path yields `None`, not an invented ID. Native witnesses with only
  `UI_EVIDENCE_BIN_ROOT=<Feral bin>`: `test_catalog_sort`, `test_catalog_input`
  and `test_catalog_events` give 26 tests, 0 skipped. The full UI lane without the
  private ui.wad/residx/str extractions gives 69 tests, 27 skipped. The commit's
  "zero skips" holds only with those private paths, which were not located here.
  No filesystem scan was done to find them.
- **Rides `c6b0906` RNG streams.** The context LCG (1664525/1013904223, wrapping
  signed abs), stdlib (1103515245/12345, 15-bit) and COAST (214013/2531011, high
  16 bits) generators stay separate. The RSE projection does a logical >>1, then
  a remainder by signed16 bound + 1. The −2/−3/−32768 rows of `rand-native.json`
  follow from the C# arithmetic. Bound −1 (divisor 0) and controller count 0 are
  rejected as unqualified. TOUR on the signed minimum gives −48. Only reference
  helpers and witnesses change; there is no production RNG. The seed source is
  recorded as `time(NULL)` and `UTimer` with clock ownership, and seed, reset,
  interleaving and restore are listed as unresolved. ControllersWitness, run with
  the lane-relative `controller-contracts.json` under SDK 8 Debug and Release:
  25/25. Rides Python with `OPENTPW_MAC_APP` and the actual fixture: 26/26. This
  is a projection reference, **not** seed or stream restoration.
- **Economy `60d86de` lab reader.** `ReadIdentifiedPcCandidate` gates on the
  container SHA, decoded length, payload SHA and fixed world-reference words
  before the three local frames. It returns
  `LocallyFramedIdentifiedPcCandidate`, and `IsCompleteWorldSnapshot` and
  `CanRestoreLiveResearch` are both constant `false`. Harness under SDK 8 with
  `--fixture <actual>`: 22/22 groups, 8,305 assertions, in both Debug and
  Release. Without the fixture: 21/21 groups, 8,140 assertions, with a
  `NOT RUN: actual PC fixture` line. The qualification is explicit and accepted.

### Freshness (committed `ef6ff14` only; final batch commit pending)

| Check | `ef6ff14` |
| --- | --- |
| Fixture blob prefixes `245a6743`/`816de5d1` in object DB | absent |
| Blob of size 2,274,758 or 38,479 | none |
| Original-asset extension paths | 0 |
| Blobs > 1 MB | upstream `content/textures/test.png` only |
| `[BIN:` aliases in `source/` | 29, all `STP-PPC` |
| `fidelity_register.py --check` | **exit 1, stale** (base `f635468` exit 0, 129 IDs) |

The stale register is line drift only. `--write` on a copy changes the RIDES-001…007,
RIDES-016, RIDES-023 and `.sam` DATA row line numbers, which moved with
`ObjectAnimator.cs`/`OriginalObjectRuntime.cs` in `2175677`. The ID rows are
identical, and `--check` then passes with 129 IDs. This is the same class as the
earlier `ab444e3` finding. `origin/main` has since moved to `0829614`, which
changes production `ParkResearch`/`ParkEconomy` and the CD UITEXT numbering.
Batch 2 is based on `f635468`, so that drift must be merged or explicitly deferred.

### ECON-002 host-date claim: source refutation present, correction pending

`ef6ff14` `docs/reverse/APPROX-TRACE.md:289` still says the funny clock starts
from host local time at `FUN_100e3c90` and adds tick × 15000. The merged clock
section "Calendar epoch reconciliation" (`b52dcff`) contradicts both claims:

- `0xe4348` sets `+0` `mFunnyTimeStart` to fixed 2000/1/1. The host
  `GetLocalTime` writes only `+8` `mSessionStart`.
- Conversion `0xe4394` divides rate × counter by 4, which gives 3,750 virtual
  seconds per turn.

`FUN_100e3c90` is probably the same Mac constructor `0xe3c90` at a 0x10000000
image base. This is an inference and is not verified here. The planned
APPROX-TRACE correction is **not in `ef6ff14`**. It is a blocker for the final
batch commit, not a corrected finding. It covers the Mac PEF only. No PC or
Patch 2 calendar claim follows from it.

### Old findings status

- Corrected earlier and confirmed again: round-14 fixture consumers. The batch-2
  `ef6ff14` runner is still pre-`4e173cb`, so it lands only when review is merged.
- Corrected this round: R15-1 (save-path variable).
- Pending: the UI merge resolution, merging review `4e173cb`+R15-1, regenerating
  the register, the ECON-002 correction, and the final-commit count/alias/register/
  containment rerun. `origin/main` `0829614` drift is also pending.
- Carried unchanged: no PC/Patch 2 runtime equivalence, no channel-clock source,
  and no SDT-device claim. Self-tests are not runtime parity.
  ControllersWitness stays unregistered.

### Results

| Target | Command | Result |
| --- | --- | --- |
| `ef6ff14` archive | `dotnet test` SDK 10.0.401, filter Md2/ObjectVertexAnimation/ObjectCatalog | build OK; 88 pass, 3 skip (ISO/bonus/GPU), 0 fail |
| `ef6ff14` archive | 4e173cb runner, `--mac-bin --pc-fixture <actual> --dotnet` SDK 8 | exit 0; 6 harnesses pass, scientist 16/16 with fixture; clock 4 skipped |
| `ef6ff14` archive | R15 runner, same + `--require-fixtures`, `OPENTPW_PPC_SAVE_PATH=/nonexistent` exported | clock 69/0 skip (stale value stripped); exit 1 only for UI's 8 private-corpus skips |
| `ef6ff14` archive | same with SDK 10 `--dotnet` | harnesses `missing-runtime` (net8.0); SDK 8 is required for lane harnesses |
| review lane | `test_round12..15` | 34 OK; `test_round15` against `4e173cb` runner: 4 failures |
| `b7bead5`, `c6b0906`, `60d86de` | as above | UI native 26/0; rides 26 + Controllers 25; lab 22/8,305 |

Not run: the full codec or corpus reruns, the UI private corpus, the original
runtime, PC/Patch 2 parity, and any filesystem or mount scan.

### Handoff

1. Batch 2 integrator (`nativeppc_guests`): finish the `917eb36` merge under the
   UI owner's sign audit. Merge review with this round's runner taken whole.
   Run `tools/fidelity_register.py --write` after the last source merge. Land the
   ECON-002 APPROX-TRACE correction, limited to the Mac PEF. Then hand the final
   SHA back for the count/alias/register/containment rerun.
2. Run the runner with `--dotnet` on SDK 8 (`opentpw-dotnet`), not SDK 10. Lane
   harnesses are net8.0.
3. UI owner: either record the private UI paths needed for "zero skips", or
   qualify the claim as "with private extractions".
4. Decide on merging or deferring `origin/main` `0829614` (production research
   cadence) before batch 2 is pushed.

## 52. Round 16: origin `7bc6c59` start-up movies, formats `f443475` clock selector, scenarios `202fd60` profile snapshot

Scope: the committed `origin/main` start-up movie delta (`aa4674b`, `0b8577f`,
`7bc6c59`), and the unmerged lane tips formats `f443475` and scenarios `202fd60`.
The owner merges `7bc6c59` as a separate checkpoint, so nothing here touches batch 2.
Batch 2 final checks stay **pending** until its final SHA is supplied. The sign audit
belongs to the UI owner and is not repeated. All checks ran on `git archive`
copies under `/tmp`.

### `7bc6c59` start-up movies: control flow (no blocker)

| Path | Verdict |
| --- | --- |
| Setup child IPC | `InstallationDiscovery.RunChild` returns in `Program.Main` before `Game.Run`. No intro, no window. |
| Wizard closed | `ResolveGameFolder` returns null before any renderer or intro is created. |
| `--play-movie` | Headless and windowed both return before `frontEndRun` is computed. Intro never plays. |
| `--front-end --smoke-test`, plain `--smoke-test`, `--sandbox`, `--load-original-level`, `--visit-park`, `--advisor-say` | `playIntro` is false. Unchanged behaviour. |
| Default and plain `--front-end` | Intro plays, then the front end. `--no-intro` or any non-empty `OPENTPW_NO_INTRO` skips it (including `0`; worth a line in `RUNNING.md`). |
| No `Movies` folder / missing `.tgq` | `DirectoryNotFoundException` / `FileNotFoundException` are caught in `TryOpen`, logged, and the movie is skipped. The front end follows. |
| Window closed mid-intro | `Render.Run` returns. The intro (`using`) and flow are disposed. `Completed` never fires. |
| Handoff | `Render.OnUpdate` is a delegate field, so removing and adding handlers inside `Completed` works on an invocation snapshot. `DiscardHeldInput` stops a held Esc, Space or click from reaching the lobby. |
| Register | Code tag `[APPROX:UI-035]` matches the `UiApproximations`, UI.md and FIDELITY-REGISTER rows. `0b8577f` moved the tag off the taken UI-033. |

Findings:

- **R16-1 (medium, origin owner).** Only open-time errors are caught. Frame and
  audio decoding run lazily in `MoviePlayback.Update` (`DecodeVideoFrame`,
  `DecodeAudioBlock` through `FillAudio`). That is called from
  `IntroSequence.Update`, which has no catch. So a truncated or damaged `.tgq`
  that opens cleanly throws from inside `Render.Run`. `Program.Main` then exits
  with code 1 before the front end appears. This contradicts the class comment
  ("a missing or unreadable movie is skipped silently"). Fix: wrap
  `current.Update()` in the same exception filter, dispose the movie and continue.
- **R16-2 (low).** `MovieScreen`'s constructor opens the SDL audio output before
  `new MoviePlayback`. If that throws (`NotSupportedException` for a missing or
  out-of-range frame rate, or an `ArgumentException` sample-rate mismatch), the
  intro catches it but the output is never disposed. `UnauthorizedAccessException`
  (not an `IOException`) is not caught.
- **R16-3 (low).** `--capture-world` with the default front end now starts
  counting frames during the intro. Add `capturePath == null` to `playIntro`, or
  document `--no-intro`.

Fidelity: the Mac order (`bf`, then `day % 8` into an eight-entry table) is
recorded as UI-035, with the PC order assumed. This round did not re-trace
`0x101C0F40`.

### Formats `f443475`: channel clock selector

`format_witness.py` exits 0 on the identified `SimThemePark.data`
(`04809cd4…`). Lane tests `test_clip_clock_reference` and `test_formats_witness`
pass 24/24. The new `test_round16.py` decodes the nine words at
`0xa63e8..0xa63fc`, `0xa7288..0xa729c` and `0xa7070..0xa7084` with its own field
split. It finds `lwz rX,0(ch)`, then `rlwinm. rX,rX,0,25,25` (0x40), then `beq`
to the `+16400` load (A), with a fall-through `+16408` load (B) and a `b` over it.
Both clocks load from the same base. Swapping the constants fails 3/3. The doc's
wording holds:

- "rate is not a ticks-per-second value" is shown only by the reference model's
  tests.
- `TicksPerSecond` is called a separate, unproven choice.
- The model makes no Global.Time wiring or PC claim.

Not independently re-checked: the claim that the advisor is the only
`use_b = 1` caller, the 252-byte instance copy, the pause wrapper callers, and
the "channel 0 tested once per channel" quirk. These remain the witness's own.
The `b52` fixed-2000 epoch and the separate host session are unaffected. Actor
graph, PC arithmetic and device equivalence stay qualified.

### Scenarios `202fd60`: synthetic gms.dat snapshot

`test_profile_snapshot` passes 31/31 with `OPENTPW_MAC_BIN`.
`scenario_evidence.py` exits 0. Synthetic probes:

- A complete v12 file round-trips byte-identically.
- Negative counts cycle under `mac-partial` and give `negative-count` under strict.
- Version `0xFFFFFFFF` is accepted with an issue under `mac-partial`.
- A name length of `0xFFFFFFFF` stops at `theme name` (offset 25) without
  allocating.
- A partial snapshot whose envelope is forged to `complete: true` is refused by
  the serializer (settings, then count checks).

The commit and the module make no real-gms or PC parser claim.

- **R16-4 (low).** `from_envelope` checks schema, version and policy only. Other
  members are not type-checked (a string `version` is accepted). A missing member
  raises `KeyError`, not `ValueError`. `complete` and `failed_at` are not
  cross-checked. The serializer still refuses the inconsistent cases tried.
- **R16-5 (low, wording).** The envelope's `source` field names the Mac executable
  SHA and the static trace for every snapshot, including ones read from synthetic
  bytes. It describes where the layout comes from, not where the bytes come from.
  Rename it to `layout_source`, or add a caller-supplied `bytes_source`, so an
  envelope is not read as proof of authenticity.
- **R16-6 (low, efficiency).** The repeated-rideId check is `ride in mystery` on a
  list, so it is quadratic. 20,000 ids take 1.1 s. Real files are tiny, but a set
  alongside the list keeps hostile input linear.

### Results

| Target | Command | Result |
| --- | --- | --- |
| `7bc6c59` archive | `dotnet test` SDK 10.0.401, filter IntroPlaylist/MoviePlayback/UiApproximation | 26 pass, 4 skip (original movies not supplied), 0 fail |
| `f443475` archive | `format_witness.py <mac bin>`; lane tests | exit 0; 24/24 |
| `202fd60` archive | `scenario_evidence.py <mac bin>`; `test_profile_snapshot` | exit 0; 31/31 |
| review lane | `test_round12..16`; `test_round16` with `OPENTPW_PPC_BIN_ROOT` | OK; 1/1, skipped without the variable |

Not run: the full codec or corpus reruns, the original-movie headless tests, the
runtime intro on a window, PC parity, and any filesystem scan.

### Handoff

1. Origin owner: R16-1 before or with the `7bc6c59` checkpoint merge. R16-2 and
   R16-3 can follow.
2. Scenarios owner: R16-5 wording. R16-4 and R16-6 are optional hardening.
3. Formats `f443475`: no change needed from this review.
4. Batch 2: pending its final SHA for the count/alias/register/containment rerun.

## 53. Round 17: batch 2 final `3f87c6b` (intro lifecycle), scenarios `1dacb25` comparator and short reads, final containment

Scope: the committed SHAs `3f87c6b` (integration-batch2, parent `6ec5d28`) and
`1dacb25` (ppc-scenarios). Uncommitted scenarios edits for R16-4/5/6 were not read
or run. Every run used a `git archive` copy under `/tmp`. The Mac bin is the
identified `SimThemePark.data` (`04809cd4…95f5`). No PC save, original movie or
filesystem scan was needed.

### Merge blockers

- **R17-1 (blocker for clean merge, no behaviour impact). `3f87c6b` converts
  `source/OpenTPW/Client/Game.cs` from CRLF to LF.** At `6ec5d28` the file has 419
  CRLF line ends. At `3f87c6b` it has 0 CRLF and 419 LF, with tab indentation
  unchanged. `git diff -w --ignore-cr-at-eol` shows **one** changed line (the
  `playIntro` line now calls `IntroPlaylist.ShouldPlay`). So the 838-line diff is
  418 lines of line-ending churn plus that one line. It is the only line-ending
  change in all of batch 2 against `origin/main` `7bc6c59`. `.editorconfig` asks
  for CRLF in `*.cs`, and there is no `.gitattributes` normalisation. Game.cs was
  one of the 19 CRLF `.cs` files in the index. Left as is, every lane that edits
  Game.cs hits a whole-file conflict, and `git blame` loses history. Fix: amend or
  follow up with Game.cs back in CRLF and only the one-line change. Pinned by
  `test_round17.Batch2LineEndings` (needs `OPENTPW_REVIEW_REPO`).
  `diff --check` with `blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol`
  reports nothing for `6ec5d28..3f87c6b` or `origin/main..3f87c6b`.

No other blocker was found.

### `3f87c6b` against R16-1/2/3

| Finding | Status | Evidence |
| --- | --- | --- |
| R16-1 post-open decode failure aborts startup | **Closed** | `Update` now wraps `Skip/Update/IsFinished` in the same recoverable filter as opening (`IOException`, `UnauthorizedAccessException`, `NotSupportedException`, `InvalidDataException`, `ArgumentException`). It releases the current movie and moves on to the next one. Review probe: four header-valid synthetic 16x16 TGQs with corrupt bitstreams (empty, all-ones ×1, all-ones ×64, zero). Each opens, and then the real `MoviePlayback.Update` → `DecodeVideoFrame` throws `InvalidDataException` ("bitstream is truncated", "DC size code is invalid" ×2, "coefficient code is invalid"). `IntroSequence` disposes the broken movie once, plays and disposes the next one, and completes. `Draw` is not wrapped, but `MovieScreen.Render` only draws and does not decode. |
| R16-2 constructor audio leak / `UnauthorizedAccessException` | **Closed** | `CreatePlayback` disposes the audio output if `MoviePlayback` construction (or the `Gain` initializer) throws, and the playback owns it after success. A `MoviePresenter` failure disposes the playback, which disposes the audio. `UnauthorizedAccessException` is in the filter. GPU allocation failure inside the presenter is not tested (the commit says so). |
| R16-3 `--capture-world` counted intro frames | **Closed** | `IntroPlaylist.ShouldPlay` returns false for `--capture-world`. `WorldCapture` is registered after `playIntro` is decided. |
| `OPENTPW_NO_INTRO` docs | Closed | RUNNING.md says any nonempty value, including `0`, disables intros, and so does `--capture-world`. The Game.cs comment at line 192 still lists only smoke/`--no-intro`/env (nit). |
| Dispose | Correct | Sets `completed`, releases the current movie, restores world scaling, and does not raise `Completed`. Game.cs's `using var intro` scope matches. |

Qualified: `ArgumentException` in the filter also covers `ArgumentOutOfRangeException`,
which `MoviePlayback.Update` throws for a non-finite `Time.Delta`. That programming
error would be logged as a movie failure, not surfaced. This is low severity, and
the open path already filtered the same type before this commit.
`ProgrammingFailureIsNotHiddenAsAnUnreadableMovie` covers only
`InvalidOperationException`.

### Scenarios `1dacb25`

- **Comparator and writer order: confirmed.** As traced, `operator<` (unsigned
  `strncmp` over the shorter length, shorter string less on an equal prefix)
  matches Python `bytes` order for all 400×400 pairs over
  `{01,41,61,7f,80,e9,ff}`, lengths 0..3 (`test_round17`). It also matched on 20,000
  random pairs. The earlier operand confirmation (the eqv/subfc/addze sign idiom)
  stands. `mac_writer_order` sorts deduplicated map keys and the mystery set,
  ascending unsigned u16.
- **Short-read correction: confirmed for the model.** `short_import(0, b'\x7f')` =
  `0x7f000000`. `short_import(-1, b'\x12\x34')` = `0x1234ffff`. A cut one byte into
  `mExtraKeys` gives `0x7f000000` from both `read_mac_player_file` and
  `read_profile_snapshot`. Only `mSpentTickets`/`mExtraKeys` are i32 player
  members, so the scalar `record[key] = short_import(…)` path never sees a list.
  "FSRead stores the bytes before EOF" is still an assumption about Mac OS and is
  labelled as one.
- **R17-2 (low, synthetic only).** `player_file_evidence.read_mac_player_file`
  still keys themes by the raw name, embedded NUL included. Two themes
  `a\0x`, `a\0y` are both inserted, with `ok` true. `read_profile_snapshot`
  (`mac-partial`) stops at `duplicate theme` with key `a`. 1dacb25's own evidence
  (strlen + strcpy constructor) says the map key ends at the first NUL, so the
  older reader is now the inconsistent one. Key it by `name.split(b'\0',1)[0]` or
  note the gap.
- Lane reruns on the archive: `scenario_evidence.py <mac bin>` exit 0 with 2,090
  instruction checks. `unittest discover lanes/scenarios` with `OPENTPW_MAC_BIN`:
  88 tests OK, 0 skipped. Claims stay synthetic/static-Mac. No PC or real-gms claim
  was made or tested.

### Batch 2 final containment (`3f87c6b`, git objects only)

| Check | `3f87c6b` |
| --- | --- |
| Commits ahead of `origin/main` `7bc6c59` | 56 (`origin/main`, `0829614`, review `4e173cb` and `2bef824` are ancestors) |
| Fixture blob prefixes `245a6743`/`816de5d1` reachable | absent |
| Blob of size 2,274,758 or 38,479 reachable | none |
| Original-asset extension paths at tip | 0 |
| Blobs > 1 MB introduced by batch 2 (`^origin/main`) | none. Reachable from upstream history: `content/textures/test.png`, `export.bin` (deleted upstream in `a108ee7`), `tools/tpi-compare/full-evidence.json` (`f81313a`/`18b09c5`, already on `origin/main`) |
| `[BIN:` aliases in `source/` | 36, all `STP-PPC`; every bracket label is `APPROX`/`DATA`/`EXT`/`BIN:STP-PPC` |
| `fidelity_register.py --check` | exit 0, 129 unresolved APPROX IDs (the round-15 staleness is resolved) |
| ECON-002 host-date claim | corrected: APPROX-TRACE row is `contradicted`, with 2000-01-01 at `+0` and 3,750 s per turn, and no "real local date" text is left |
| Line endings vs `origin/main` | one file changed: Game.cs CRLF→LF (R17-1) |

### Results

| Target | Command | Result |
| --- | --- | --- |
| `3f87c6b` archive | SDK 10.0.401 `dotnet test -c Release`, filter IntroSequence/Round17 probe/MoviePlayback/IntroPlaylist | 35 pass, 4 inconclusive (original movie corpus not supplied), 0 fail. Includes the 5 commit regressions and 4 review probes |
| `1dacb25` archive | `scenario_evidence.py`; lane `unittest discover` | exit 0, 2,090 checks; 88/88 |
| review lane | `test_round12..17`; `test_round16` with `OPENTPW_PPC_BIN_ROOT`; `test_round17` with `OPENTPW_REVIEW_REPO` | OK (35 + 4); 1/1; 4/4 |

The C# probe is kept at `tools/ppc-analysis/lanes/review/round17/Round17IntroDecodeProbeTests.cs`.
It is not compiled here, because it needs `3f87c6b`'s injected `IntroSequence`
constructor. SDK 8 was not needed: no SDK 8 harness changed in `3f87c6b`. Not run:
runtime intro in a window, original-movie tests, presenter GPU allocation failure,
and PC parity.

### Handoff

1. Batch 2 owner: R17-1. Restore CRLF on Game.cs, keeping the one-line change, then
   re-run `diff --ignore-cr-at-eol --stat` (expect 1 line) before pushing. Optional:
   the Game.cs comment nit.
2. Scenarios owner: R17-2 is optional, and so are R16-4/5/6 (in progress).
3. After R17-1, the batch-2 containment above carries over if only Game.cs line
   endings change.

## 54. Round 18: batch 2 fix `ba70ea5`, formats `8f0e048` scene clock A and normals, scenarios `ffa87c6` envelope v2

Scope: the committed SHAs `ba70ea5` (integration-batch2, parent `3f87c6b`),
`8f0e048` (ppc-formats) and `ffa87c6` (ppc-scenarios). Every run used a
`git archive` copy under `/tmp`. The Mac bin is the identified
`SimThemePark.data` (`04809cd4…95f5`). Nothing original was executed, and no
filesystem scan was run. The formats corpus counts were rerun with the lane's own
`corpus_check.py` on the two documented PC Data paths. Those runs are a rerun of
the lane's own check, not an independent decode.

### Merge blockers

None for batch 2 at `ba70ea5`. None of the findings below blocks the formats or
scenarios lanes. Both are evidence/reference lanes with no runtime change.

### Batch 2 `ba70ea5`: R17-1 resolved, merge-ready

- Game.cs has 419 CRLF out of 419 line ends (as at `6ec5d28`), and the BOM is kept.
  `git diff 6ec5d28 ba70ea5 -- Game.cs` is 2+/2− with no `--ignore-cr-at-eol`
  needed: the comment line (adds `--capture-world`) and the `playIntro` line
  (`IntroPlaylist.ShouldPlay`). `ba70ea5` changes only Game.cs against `3f87c6b`.
- Line-ending style of all 46 files that batch 2 modifies against `origin/main`
  `7bc6c59`: no change (was 1 at `3f87c6b`).
- Small wording nit, not a blocker: the commit says "whitespace diff --check
  clean". That holds only with `core.whitespace=cr-at-eol`. Plain `--check`
  flags the CR on the two new lines, as it does on every CRLF line of this file.
- `test_round17` (still pins the `3f87c6b` LF conversion) passes. `test_round18`
  `Batch2CrlfRestored` pins the fix.

Containment at `ba70ea5` (git objects only, 57 commits ahead of `origin/main`):

| Check | `ba70ea5` |
| --- | --- |
| Fixture blob prefixes `245a6743`/`816de5d1` reachable | absent (7,052 reachable objects) |
| Blob of size 2,274,758 or 38,479 reachable | none |
| Original-asset extension paths at tip | 0 |
| Blobs > 1 MB introduced by batch 2 (`^origin/main`) | none |
| `[BIN:` aliases in `source/` | 36, all `STP-PPC` (unchanged) |
| `fidelity_register.py --check` | exit 0, 129 unresolved APPROX IDs |
| SDK 10.0.401 `dotnet test -c Release`, filter IntroSequence/Round17/MoviePlayback/IntroPlaylist (with the round-17 probe copied in) | 35 pass, 4 skipped (original movies not supplied), 0 fail. Same as `3f87c6b` |

**Verdict: batch 2 at `ba70ea5` is merge-ready** on the checks above. Not run:
the full test suite, the native smoke test, and runtime intro in a window (no
behaviour change since `3f87c6b`).

### Formats `8f0e048`: scene clock A, hold, pause, normal routes

Independent decode (`test_round18.SceneClockAndNormals`, 7 tests) uses its own
field split. The clamp is checked by a small interpreter that executes the
step routines' own words (lfd/fmul/fdiv/fcmpo/bc/b/fmr/stfd/blr).

- **Constants.** TOC doubles at −10504/−10496/−10488/−10480 are 2.0/0.25/1.25/1.0.
  The reset handler 0x1131ac loads 1.0 (−10536). The init 0x127c08 (called from
  0x10ea78 in 0x10ea5c) stores 1.0 at +0x18. **Confirmed.**
- **Steps and clamp.** Executing 0x127c48 four times from 1.0 gives
  1.25, 1.5625, 1.953125, 2.0. 0x127c88 from 2.0 gives 1.6, and from 0.3 gives
  0.25. **Confirmed.**
- **R18-1 (low, model error, not a game path).** For NaN the binary keeps the
  rate unchanged and stores NaN. fcmpo on NaN sets only the unordered bit, so
  `bf LT` (0x127c64) and `bf GT` (0x127c74) both branch, ending in
  `fmr f0,f1`. The commit, the witness text (`NaN -> 0.25`) and
  `clip_clock_reference.clamp_rate` all say it becomes 0.25. The "NaN clamp"
  mutation test therefore pins the wrong behaviour. NaN cannot arise through the
  three handlers (1.0 start, finite ×/÷1.25, reset to 1.0), so no traced game
  route is affected. Fix: `clamp_rate` keeps NaN (or refuses it), and the
  witness and doc text are corrected.
- **Handlers.** 0x11315c/0x113184/0x1131ac load r3 from TOC −0x75d8 and call
  0x127c88/0x127c48/0x127c40. **Confirmed.** The key-table entries 6–8 were not
  re-decoded.
- **Hold.** 0x10ec60 returns if +0x2c ≠ 0. Otherwise it stores
  `divwu(1000, n)` at +0x34 (r31 = r4) and sets +0x2c. The step 0x10ed10 skips
  when the 0x117d6c getter returns non-zero, else `+0x30 += +0x34`. The
  main-loop call is 0x1104d8. Both hold sites load `li r4,32` (0x1c0b34 before
  0x1c0b64; 0x1c2298 before 0x1c229c), so the step is 31 ms. **Confirmed.**
  That 0x117d6c is "A paused" is taken from the lane, not re-derived here.
- **0x10000000 writer.** A full-image scan finds exactly four `oris …,0x1000`
  (0x31d68, 0x55b10, 0x58d38, 0x19aef0) and no `addis`/`lis` of 0x1000. The
  other three store to r24+8, object +0x50 and a stack slot. 0x19aef0 sets flags
  word 0 of `*(selected+4)`, gated by `and.` with r9 = `lis 4; addi 0x40` =
  0x40040. The ride loader calls 0x19adf0 at 0x59274. **Confirmed**, with one
  label caveat: the scan cannot see a 0x10000000 built any other way (shifts,
  data loads). The lane's "one writer" is an immediate-operand scan.
- **Placement.** 0x59f64 is `li r5,0x211f`, and 0x59f6c ORs in 0x200 under
  placement flag 0x800 (the doc says so at line 491). 0x543f4 is
  `rlwinm r26,r5,0,21,19`, which clears only 0x800 before the 0x53004 call. 0x5a48c
  ORs 0x00010000 into the records (160-byte stride, 0x5a494), 0x5a4a0 sets
  header +0x30 bit 0x40000, and 0x5a4b4 calls 0xa78ec. **Confirmed.** "Every
  successful path" was not re-walked.
- Corpus counts (lane tool rerun): PC base and Patch 2 both give 2,452 table
  entries, 248 with 0x40040, 28 members, 4,914/4,914 records with 0x10000000
  clear, and 838 geometry headers.
- **Labelling.** The doc names the clock-object route scan "a bounded witness,
  not a data-flow proof", and the commit marks "only route" claims as medium
  confidence (straight-line scan, no branch following). **Adequate.** No
  custom-TPS or PC-speed claim is made. The doc twice says a per-animator
  ticks-per-second value is not equivalent, and that PC keys and bindings are
  unknown. `8f0e048` touches only docs and `lanes/formats` tools (6 files).
- Mutation check of `test_round18`: 7 single-word patches of the image
  (`bf LT→bt`, `bf GT→bt`, `li 1000→1024`, hold `n 32→16`, writer `stw` offset,
  `0x211f→0x201f`, `fmul→fdiv`) each fail at least one test.
- Lane reruns on the archive: `format_witness.py` exit 0. `lanes/formats`
  unittest: 43 OK. Shared `ppc-analysis` tests: 16 OK.

### Scenarios `ffa87c6`: envelope v2 (R16-4/5/6)

Synthetic probes only (`/tmp` probe scripts plus `test_round18.ScenarioEnvelopeV2`,
6 tests):

- **R16-4 (strict validation): resolved.** Refused with ValueError: bool as
  version, bool in a ticket list, float as an i32, `"2"` or `True` as
  `envelope_version`, a missing player member, an unknown theme field, a changed
  `layout_source`, forged `issues` on a complete record, uppercase hex, repeated
  JSON members (top level and nested), `NaN`, `1e400`, and a 5,000-digit integer.
  A fuzz of 24,000 single-field mutations (wrong-type values or deleted keys)
  over 8 base envelopes (7 `mac-partial` cuts and one `strict-host` read) raised
  no exception other than ValueError. 1,497 were accepted. The 405 accepted
  complete records were all self-consistent: their own bytes read back to the
  same record. One residual: a 200,000-deep `[[[…]]]` gives `RecursionError`
  from `json.loads`, not ValueError (R18-2, low). The doc says "never a KeyError
  or TypeError", which still holds.
- **R16-5 (provenance): resolved.** v2 replaces `source` with `layout_source`,
  which says the snapshot bytes are unauthenticated. v1, and v2 with a `source`
  field, are refused.
- **R16-6 (linear repeats): resolved.** 60,000 rideIds over 7 values: read 0.016 s,
  envelope roundtrip 0.054 s, 59,993 repeat issues, record equal after the
  roundtrip. `strict-host` rejects it.
- **No forged "complete" from a partial record.** Flipping a partial record to
  `complete: true` (and clearing `failed_at`/`failed_offset`), relabelling it
  `strict-host`, or adding trailing bytes are all refused. A complete record must
  survive serialize → read under its own policy. A `mac-partial` complete record
  relabelled `strict-host` is accepted only when those bytes are a valid
  strict-host read (no issues). That is the same record, not a forgery.
- **R18-3 (low, strictness gap in the partial path).** The failing member is
  allowed any value, because "the failing member may keep short-read bytes". That
  holds only for the i32 members and the ticket byte arrays. The decoder accepts
  a partial record failing at `mEasyModeUser` with value 7 (a u8 read delivers no
  bytes, so it must stay reset). It also accepts `mSpentTickets = 0x12345678` at
  a cut inside that member (fewer than 4 delivered bytes leave the low-order
  byte at its reset value, 0). `failed_offset` is also unchecked; the doc says so.
  None of this lets a partial record pass as complete. Pinned as two
  `expectedFailure` tests that flip when it is fixed.
- **R17-2** (`read_mac_player_file` keys themes by the full NUL-containing name)
  is still open and optional. `player_file_evidence.py` is unchanged in
  `ffa87c6`.
- Lane reruns on the archive: `lanes/scenarios` unittest: 98 OK with
  `OPENTPW_MAC_BIN`, and 85 run + 13 skipped without it. `scenario_evidence.py`
  exit 0, 2,090 instruction checks. No real gms.dat and no PC claim.

### Results

| Target | Command | Result |
| --- | --- | --- |
| `ba70ea5` archive | SDK 10.0.401 `dotnet test -c Release`, intro/movie filter | 35 pass, 4 skip, 0 fail |
| `ba70ea5` archive | `fidelity_register.py --check` | exit 0, 129 |
| `8f0e048` archive | `format_witness.py`; `lanes/formats` unittest; shared tests; `corpus_check.py` ×2 | exit 0; 43 OK; 16 OK; counts as above |
| `ffa87c6` archive | lane unittest with / without bin; `scenario_evidence.py` | 98 OK; 85 + 13 skip; exit 0, 2,090 |
| review lane | `unittest discover` with all three env vars | 191 run, OK (3 skipped, 2 expected failures) |
| review lane | `unittest discover` with no env vars | 191 run, OK (35 skipped) |

### Handoff

1. Batch 2: merge-ready at `ba70ea5`. Optional: reword "diff --check clean" in
   future commits to name `cr-at-eol`.
2. Formats: R18-1. Make `clamp_rate` keep NaN (as the binary does) or refuse it,
   fix the `NaN -> 0.25` witness text and the doc line, and flip the mutation
   test. Optional: label the 0x10000000 "one writer" as an immediate-operand scan.
3. Scenarios: R18-3 (optional): restrict the failing member to values a short
   read can produce (u8 members reset; i32 low-order bytes reset; ticket arrays
   a prefix). R18-2 (optional): catch `RecursionError` in `loads_envelope`.
   R17-2 is still optional.

## 55. Advisor lane review A1: ppc-advisor `a479cff..526e2bf` (phases 6–10)

Scope: the five commits `2b8e4bf`, `be46ebe`, `00d5c9c`, `1af929b` and `526e2bf`
on ppc-advisor, with merge-base `a479cff` against integration. Every build and test
used a `git archive` copy under `/tmp`. The Mac containers are the identified
`SimThemePark.data` (`04809cd4…95f5`) and `sound_shared.data` (`7132c2f1…b94f`).
The PC data is the supplied `Data` tree. The Mac data is the UI lane's existing
read-only copy in `/tmp/ppc-advisor-mac-Data`. No original code was executed, no
filesystem or mount was scanned, and the other worktrees were only read.

### Merge blockers

- **A1-1 (blocker: post-merge build break).** `be46ebe` adds
  `tools/ppc-analysis/lanes/advisor/audio-events/AudioEventAssets.csproj`, which
  targets `net8.0` and has a `ProjectReference` to `source/OpenTPW.Files`. At
  integration `ba70ea5`, `OpenTPW.Files` targets `net10.0`. `0d58bd4` says
  "source-project references must follow net10". On a merged copy (`ba70ea5` plus
  the 526e2bf advisor lane), SDK 10.0.401 fails with `NU1201: Project OpenTPW.Files
  is not compatible with net8.0`. Changing only the TFM is not enough: the tool's
  `TreatWarningsAsErrors` then turns the NuGet audit warnings for `OpenTPW.Files`'
  transitive `Newtonsoft.Json` 9.0.1 (NU1903) and `Zio` 0.17.0 (NU1901) into errors.
  **Fix (verified):** use `net10.0` and add
  `<WarningsNotAsErrors>NU1901;NU1903</WarningsNotAsErrors>`. The build then
  succeeds, and the 28-EventMap report is byte-identical to the SDK 8 run on the
  `526e2bf` tree. Also update the phase-7 command to the SDK 10 `dotnet`. The
  standalone `OriginalAdvisorLipDriver.csproj` is self-contained, so it can stay
  net8 (52/52 under SDK 8.0.425).
- **A1-2 (blocker: conflicts).** `git merge-tree --write-tree ba70ea5 526e2bf`
  conflicts in `source/OpenTPW.Files/Formats/Sound/MP2File.cs` and
  `docs/reverse/PPC-advisor.md`. The cause is that `2b8e4bf`'s production change is
  already on integration. `Mp2Decoder.cs` and `MP2FileMetadataTests.cs` are
  identical at `526e2bf` and `ba70ea5`. `MP2File.cs` on integration is a superset:
  `28864fa` renames `Samples` to `RawSampleField`, takes `BitsPerSample` from the
  entry, and keeps the `TryReadFrameFormat` override. **Resolution:** take
  `ba70ea5`'s `MP2File.cs` as is. Keep `ba70ea5`'s phase-5 paragraph. Keep the
  phase 6–10 appendix, but correct phase 6's sentence about the "legacy
  `SoundFile` reads signed Int16 … 44,100 wrap negative". That is stale after
  `28864fa`, which reads the unsigned packed fields. The cleanest route is to drop
  `2b8e4bf` and rebase `be46ebe..526e2bf` onto `ba70ea5`.
- **Scope note.** `2b8e4bf` is the only commit that touches anything outside the
  lane: three production C# files. `be46ebe..526e2bf` touches only
  `tools/ppc-analysis/lanes/advisor/**` and `docs/reverse/PPC-advisor.md`. No root
  docs changed. After A1-2 is resolved, the range adds no production change.

### For the production port of `OriginalAdvisorScoreQueue`/`LipDriver`

No defect was found in the recovered helper rules. The range does not modify
`OriginalAdvisorScoreQueue.cs`, `OriginalAdvisorLipDriver.cs` or their tests. The
only change is a `Compile Remove="audio-events/**"` line. The CMsgEvent → advice →
response → speech mapping for events 0/2/3/4 and 10 checks out independently (see
`be46ebe` below). **There is one porting hazard.** The queue returns
`ResponseId = FirstResponseId + variant`, which is a stored **ID**. Native
`0x6b7c` resolves it by searching table `0x18ff4` on word 0 (stride 32, until
sentinel 9999), not by row position. From row 393 on, 216 of the 610 rows have an
ID that differs from their position. Events 0/2/3/4 use rows below 393, so they
are unaffected. The event-0 mode-2 advice 323, however, gives first response 587,
which resolves **by ID** to row 584: speech/LIP 606. Indexing **by position** would
give row 587: response 590, speech 638, LIP 0. A port that indexes the response
array directly would play the wrong clip for 323 and for any later response.
`test_advisor_a1.NativeAdvisor` pins this.

### Per-commit verdicts

| Commit | Verdict | Independently decoded (sample) | Reruns |
| --- | --- | --- | --- |
| `2b8e4bf` metadata | Code correct, **superseded**: already on integration (A1-2). Out of lane. | `TryReadFrameFormat` reuses `Header.Parse` and requires a complete first frame. The only constructor caller is `SoundFile.GetFile`. No production consumer reads `MP2File.SampleRate`: `SpeechAudioPlayer` uses decoded `Mp2Audio`. | Focused MPEG/LIP/metadata filter with `OPENTPW_GAME_PATH`: 61 pass, 6 inconclusive (other-language tests outside the commit's filter). `MP2FileMetadataTests`, including both private clips: pass. Full `OpenTPW.Tests` on the 526e2bf tree: **791 pass, 54 skip, 0 fail** (SDK 8.0.425). |
| `be46ebe` identities | **Confirmed.** Blocker A1-1. | CMsgEvent ctor `0x116540` `stw r4,8(r3)`. Receiver `0xad40` `lwz r4,8(r29)` → `bl 0x94dc`. `cmpli r4,10`. TOC slot `0x2354` → data `0x1e0f4`. Table: 0→`0x9524`, 2→`0x98b0`, 3→`0x9a64`, 4→`0x9c18`, 10→`0x9dcc` (loads a TOC object, adds 122 and calls `0xa90`; the doc's earlier history-clear reading was not re-derived), other entries → `0x9e84`. Literals 0/106/128/129 → `bl 0xb6d8`. Advice 323 at `0x9708` behind `cmpi r0,2`. Response rows 1 / 274–275 / 308–311 / 587 → speech 1 / 424–425 / 342–345 / 606 by ID. | `audio_event_evidence.py`: exit 0. `AudioEventAssets`: 28 EventMaps. Corpus: 31 catalogs and 1,267 IDs, as stated. |
| `00d5c9c` bank ordinals | **Confirmed.** | `0x16af4` `addi r0,r28,-1`, `lwz r3,44(r31)`, `slwi 2`, `lwzx`, `sth r0,12(r25)`. `0x6fa4` `addi r0,r4,-1`. New bank `0x15b8c–0x15b94` increments +52. `0x15c24–0x15c30` `stwx` into vector +44. | `bank_remap_evidence.py`: exit 0. 3,631 PC and 1,105 Mac choices resolved. |
| `1af929b` selection | **Confirmed.** Bounded: these are explicit-input algebra helpers, not a scheduler. | Event chooser `0xff40`: LCG `0x19660d`/`0x3c6ef35f`, `>>16`, `cmpl sum,draw` + `blt` continues, so the first sum ≥ draw is chosen. Exhaustion `0x10130` stores the first event. Count-1 bypass. Anti-repeat `cmpli count,2`/`ble` skips, history +80. Sample chooser `0xfcb4`: threshold +4, the same ≥ rule, exhaustion → null, history +81. Branch bounds `0x19364–0x19378` are inclusive. `divwu` by the eligible weight sum at `0x193c4`. Parameter value `mulhwu 0x51eb851f`, `>>5` = unsigned /100. Masks `0xf2e0`: +24 → byte +5, +28 → byte +6. Differencing `0x16654–0x16660`. | `sound_selection_evidence.py`: exit 0. Counts as stated: 26/31 cumulative, 201/73 arrays below the draw domain, 0 nonmonotone. |
| `526e2bf` seed ownership | **Confirmed. The qualification is correct.** | All nine `addi rX,r2,0x42e4` sites match. A forward scan from each site to the first kill or branch finds only one store, the initializer's `0x118cc`. Initializer `0x118b4` has a single caller at `0x3c`. The neighbouring data exports are `mChannelState` at `0xc2d8`, a 4-byte float at `0xc2e0`, and `mpSoundManager` at `0xc2e8`, so no adjacent object spans `0xc2e4`. | `sound_seed_evidence.py`: exit 0 over 16 identified containers. |

All lane claims stay bounded. None asserts PC or whole-runtime parity. Weights
come from the disk catalogs plus the native differencing rule. The seed is the
clock value, and no sequence is invented. Phase 10 explicitly leaves external
mutation of the exported seed open.

### Lower-severity notes

- **A1-3 (low, `2b8e4bf`, only relevant at `a479cff`).** The invalid-frame fallback
  changed from a constant 22,050 to the legacy signed-`Int16` container rate. The
  commit's own doc says that rate wraps 44,100 negative. This has no consumer
  impact, and it is moot on `ba70ea5`, where `SoundFile` reads the unsigned field.
- **A1-4 (low, test coverage).** `audio_event_evidence.py` pins the advice-323
  literal and constructor, but not its response/speech row. The doc's 587 → 606 is
  correct only because the native lookup is by ID (see above). That lookup is now
  pinned in the review test.
- **A1-5 (nit).** `InspectEventMaps` looks for a `.git` ancestor. From an archive
  copy, it throws "Write the interpreted corpus report outside the repository"
  even when the output is outside. It fails closed, but the message is misleading.
- **A1-6 (nit).** For the branching sentence, zero eligible links branch away at
  `0x19398` before the division. Only nonzero links with zero total weight reach
  `divwu`. The helper (`None` with no eligible links, `ValueError` for a zero sum)
  matches this. The phase 9 wording could say so.
- **Containment (git objects only).** The largest added blob is 87 KB (the
  advisor doc). Hex content is limited to SHA-256 identities, code/data offsets
  and synthetic test words. No original stream, PCM, script bytes or disassembly
  listing is added. The new lane C# files and `MP2FileMetadataTests.cs` are CRLF,
  which matches `.editorconfig`. Plain `git diff --check` reports them, while the
  Python/Markdown diff is clean.

### Results

| Target | Command | Result |
| --- | --- | --- |
| `526e2bf` (worktree at HEAD, read-only) | lane `unittest discover` with bin/PC/Mac fixtures; without fixtures | 73 OK, 0 skipped; 73 OK, 7 skipped |
| `526e2bf` | shared toolkit `unittest discover tools/ppc-analysis` | 16 OK |
| `526e2bf` | the four evidence scripts | exit 0 |
| `526e2bf` archive, SDK 8.0.425 | `OriginalAdvisorLipDriver`; `AudioEventAssets` build+run | 52/52; 28 EventMaps |
| merged copy, SDK 10.0.401 | `AudioEventAssets` as-is; with net10 + NU1901/NU1903 fix | NU1201 fail; build OK, identical report |
| review lane | `test_advisor_a1` with `OPENTPW_PPC_BIN_ROOT` and `OPENTPW_REVIEW_REPO`; without | 10 OK; 1 OK, 9 skipped |

Not run: original audio or advisor runtime, PC executable comparison, Windows
seed policy, and a real merge commit (only `merge-tree` was run).

### Handoff

1. Advisor owner: fix A1-1 (TFM plus audit-warning carve-out, and the doc command).
   Resolve A1-2 by dropping `2b8e4bf` and rebasing onto `ba70ea5`, then update
   phase 6's stale `SoundFile` sentence.
2. Production port owner: resolve `ResponseId` by stored ID (native `0x6b7c`), not
   by array index. Advice 323 is the in-range witness.
3. Optional: A1-4 to A1-6.

### Resolution on integration

The range was rebased onto integration `6c58be0` as `be46ebe`, `00d5c9c`,
`1af929b` and `526e2bf`; `2b8e4bf` was dropped, and integration's `MP2File.cs`
is unchanged. On merge into main (after rounds 17 and 18 landed as sections 53
and 54) this section was renumbered from 54 to 55.

- A1-1: `AudioEventAssets` targets `net10.0` with
  `<WarningsNotAsErrors>NU1901;NU1903</WarningsNotAsErrors>`, the phase-7 command
  uses the SDK 10 `dotnet`, and the evidence runner registers the tool as a
  private-corpus harness (reported as not-run).
- A1-2: phase 6 now describes `28864fa`'s unsigned packed-field reader.
- A1-3: moot. It applied only to `2b8e4bf`'s fallback, which was not brought in.
- A1-4: `audio_event_evidence.py` resolves responses with native first-match
  stored-ID search and requires advice 323 → response 587 → speech/LIP 606.
- A1-5: `InspectEventMaps` reports a missing checkout separately from an output
  inside the repository.
- A1-6: phase 9 separates the no-eligible-link exit at `0x19398` from the
  zero-sum `divwu`.

## 56. Advisor runtime review V1: production `f8375b2` (automatic advisor)

Scope: `f8375b2` ("Let the advisor speak by itself …", parent `be05bb1`) from the
advisor-runtime worktree, judged against fork main `f51e874`. The merge is clean
(`git merge-tree --write-tree f51e874 f8375b2` exits 0). Builds and tests used a
`git clone --shared` scratch copy under `/tmp` with `f8375b2` merged onto `f51e874`.
The Mac container is the identified `SimThemePark.data` (`04809cd4…95f5`). The PC
data is the supplied `Data` tree (`Advisor/Advisor.sam`, `e905df3d…0df3`). No
original code was executed, no filesystem or mount was scanned, and the subject
worktree was only read. `test_advisor_v1.py` pins every operand cited below.

### Merge blockers

- **V1-1 (blocker: speech plays when muted).** `--mute` only sets
  `GameAudio.Enabled = false`. Then `GameAudio.EnsureStarted()` returns false and
  `AudioMixer.Current` stays null. `Advisor.Play` (`Advisor.cs:295`) falls back to
  `new SpeechAudioPlayer( audio )`, which passes `openDevice: AudioMixer.Current == null`
  (`SpeechAudioPlayer.cs:44`) and so opens its own SDL device. Before this commit,
  that path was reachable only from `--advisor-say`/`--advisor-response`. Now every
  park start reaches it: a muted game says the welcome aloud. `--smoke-test
  --load-original-level` also opens a device unless `--no-advisor` is given.
  **Fix:** at `Advisor.cs:295`, make the fallback
  `new SpeechAudioPlayer( audio, null, openDevice: GameAudio.Enabled && AudioMixer.Current == null )`.
  It keeps the wall clock (ADVISOR-011). Add a test that sets `GameAudio.Enabled = false`,
  says a clip and asserts `ClockSource` is the wall clock.
- **V1-2 (blocker: playback success does not follow the traced wrapper; ADVISOR-021 is
  answerable).** The controller update calls only wrapper `0xba54`: its single caller is
  `0x89fc`, while `0xb7d8` has no direct caller. `0xba54` fails in only two cases: an
  invalid record (`0xba84`–`0xba8c`, return 0 at `0xbbf8`) and the missing-descriptor
  response 614 (`0xbb54`, return 0 at `0xbbe4`). Otherwise it calls player `0x6b7c`
  (`0xbb60`) and stores its result **only as the span** (`stw r3,0(r25)` at `0xbb64`;
  `r25` is the out-parameter, `0xba68`). `mr r3,r26` at `0xbb68` overwrites that result
  without testing it, and `0xbbf0` returns 1. The controller then saves the history and
  reserves span + 1000 (`0x8a00`–`0x8a10`). The player `0x6b7c` first reads options byte
  `+0x34` of the object at TOC −30268 (data `0x120a14`, `0x6b84`/`0x6bb4`) and
  returns 0 when it is clear (`0x6bc0` → epilogue `0x7054`). A response missing from
  the ID search also returns 0 (`0x6c20`–`0x6c28`). The eligibility check reads byte
  `+0x35` of the same object (`0x9010`/`0x9038`). So with the Advisor option off, the
  original still picks and consumes the best advice. It marks the advice played,
  advances the variant and reserves 1000 clock units, all without speech. The port
  instead returns before `Controller.Update` (`AutomaticAdvisor.cs:102`). The pending
  advice then waits, and when the option is switched on later it plays stale advice,
  for example the welcome in the middle of a game. Port failures (response not in the
  table, I/O error, no presentation) likewise return `null` and skip the history and
  reservation, where the original records success with span 0. The `0xb7d8` revalidation
  (`AdvisorController.cs:159`) is not on this path. It has no effect today, because a
  selected score is already strictly above the minimum. **Fix:**
  1. In `AdvisorController.Update`, drop the revalidation and complete with
     `playbackSucceeded: true` and `span ?? 0`. The only failure is a missing
     descriptor, which the bound rows cannot produce.
  2. In `AutomaticAdvisor.Update`, when `!GameOptions.Current.Advisor`, silence and
     then run `Controller.Update( () => AdvisorClock, () => GameTick, _ => 0u )`.
     Cite `[BIN:STP-PPC:0x10006BB4]` and `[BIN:STP-PPC:0x1000BBF0]`.
  3. Replace `FailedPlaybackConsumesTheAdviceWithoutHistoryOrReservation` with a test
     that expects the history to be saved and the 1000 reservation to apply.
  4. Reword ADVISOR-021 to the remaining question only: that byte `+0x34` is the Game
     Options Advisor switch. This is the same object-identity question as ADVISOR-015.
- **V1-3 (blocker: cited operand contradicts the port).** `ClearHistory() => history.Clear()`
  (`AdvisorScoreQueue.cs:81`, tagged `[BIN:STP-PPC:0x10009DCC]`) also zeroes the saved
  tick. The event-10 body (`0x9dd8`–`0x9e80`: `ctr` 43 × 8 records from `r31` =
  controller, then the remainder up to `0x15f` = 351) stores only at
  `+0xe4` (variant −1), `+0xe8` (played 0) and `+0xec` (slap count 0). It never stores at
  `+0xe0`. `+0xe0` is the saved tick: `0x8a20` passes controller `+0xe0 + 16·message` to
  setter `0x121098`, which stores world `+0x1da70c` (`0x1210a0`–`0x1210a8`), and the
  eligibility check reads the same base (`0x9090`). `0x8e28`–`0x8e30` (slap increment)
  and `0x9198` (limit check) confirm `+0xec`. The `0xa90` call at `0x9dd4` only spills
  arguments. Effect: after event 10 the original keeps the repeat delay. The test
  `RepeatIntervalUsesQuarterTicksAndEventTenClearsHistory` asserts the opposite
  (Eligible). **Fix:** keep `SavedGameTick` and reset the other three fields:
  `foreach ( var id in history.Keys.ToArray() ) history[id] = AdvisorMessageHistory.Empty with { SavedGameTick = history[id].SavedGameTick };`.
  Expect `RepeatDelay` after event 10 in that test. Reword "clears history" in
  `PPC-advisor.md` (lines 61, 86, 1005) and `LIPS.md:241`. The practical effect today
  is small, because the welcome is saved at turns 0–3, which is the zero-quarter
  sentinel.

### Author's claims

| Claim | Verdict | Evidence |
| --- | --- | --- |
| Every park start raises 10 then 0 → advice 0 → response 1 → level bank sample 1 | **Confirmed** (mapping). The runtime glue raises them in `AttachLevel`. | `0x1c2104` `li r4,10`, `0x1c2108` → `0x116528`; `0x1c216c` `li r4,0`, `0x1c2174` → `0x116528`. Descriptor 0: group 0, limit 1, first 1, count 1. `advisor-responses.toml` id 1 → sample 1, local. |
| Instant Action also gives advice 323 → 587 → global 606; `0x96F0` and `0x13781C` read the same global, set to 2 for Instant Action | **Confirmed.** | Both load TOC −30136 (`0x94e8` into `r25`, `0x13782c` into `r29`) → data `0x53d98`. `0x96f0` `lwz r0,0(r25)`, `0x96f4` `cmpwi r0,2`. The selector keeps 1 (online, `0x137954`), otherwise reads `mEasyModeUser` (`0x128f4c` `lbz r3,0x24(r3)`) and stores 2 (`0x1379b4`) or 0 (`0x1379e0`) through `0x12bbf4` (`stw r30,0(r29)` at `0x12bc40`). The port derives game type 2 from `ParkGameMode.InstantAction`, which rests on the existing UI-015. Descriptor 323: group 1, first 587, count 1. ID 587 → sample/LIP 606, while row 587 is ID 590 (sample 638). |
| Event 2 → 274/275 alternating "as in the original" | **Alternation confirmed. Producer equivalence remains ADVISOR-020.** | Mode word +40 is 2 for all 351 descriptors. `0x8974` `lwz r3,0xe4` → `0x8978` `+1`. `0x89d4` count (`0xd5ec`) → `0x89d8`–`0x89e0` reset to 0. `0x8a3c` stores the variant on success. Descriptor 106: first 274, count 2. Event 10 resets the variant, so each level starts with 274. The port's queue alternates correctly (`BankruptcyCyclesThroughItsTwoResponses`). In a live park, however, the economy raises `Bankrupt` once: the day loop stops at `IsBankrupt` (`ParkEconomy.cs`). So only 274 is said until a reload, and that reload's event 10 resets the variant again. |
| Events 3/4 → 308–311, but score 20 < minimum 25, so they never play | **Confirmed.** | `Advisor.sam`: `ParkNowOpen.Score 20`, `ParkNowClosed.Score 20`, `MinScoreForConsideration 25`. Selection needs strictly greater (`AdvisorScoreQueue.SelectNext`, native `0x8850`). The records stay queued: duplicate limit 1 keeps one of each. |
| Responses are looked up by stored ID; regression test for 587 | **Confirmed.** | `AdvisorResponses.Table` is a dictionary keyed by `id`, and the 610 rows have no duplicates. `PrebuiltParkResolvesItsStoredResponseIdNotATableRow` pins 587 versus row 590. |

### Checks

1. **Queue port equals the reviewed helper.** After normalization (comments, names,
   access modifiers, wrapping), every helper member is textually equal in the port. The
   port adds only `IsAttemptOutstanding`, `HasDescriptor` and `ClearHistory` (V1-3). A
   scratch differential harness compiled both classes into one assembly and ran
   identical random operation streams: 2,000 seeds × 400 steps = 800,000 operations,
   covering descriptors with wrap-edge intervals and slap limits, prior histories,
   overrides, tutorial flags, full queues, begin/complete with success and failure, and
   clocks at the busy boundary. Every result tuple, slot, history, busy flag and
   reservation matched. Sensitivity: each of the mutations `<` → `<=` (minimum scan),
   `<` → `<=` (busy) and variant `+1` → `+2` was caught within 360 steps. The 31 lane
   cases are kept in `AdvisorScoreQueueTests`.
2. **Operands decoded independently:** `0x96f0`/`0x13781c` (shared TOC −30136), event
   10 (`0x9dcc`), the variant cycle, wrapper `0xba54`, the player option gate (`0x6bb4`)
   and the main loop's 10/0. See above.
3. **APPROX coverage.**
   - ADVISOR-015, 016, 018 and 020 are honest open questions.
   - ADVISOR-016's sum agrees with native: `0x6b7c` adds 1000 at `0x7048` and the
     controller adds another 1000 at `0x8a10`.
   - **ADVISOR-017** (wall clock, not pause-aware) is correctly an approximation:
     neither the helper nor the lane traces the clock's freeze or compensation.
   - **ADVISOR-019** is correctly an approximation, not a defect. The helper does not
     apply `+24`/`+28`. In a bounded scan, none of the 13 functions called with the
     balance object (`0x53dc0`, sole TOC slot −30116) in `r3` loads `+0x18`/`+0x1c`, and
     neither does any direct TOC load of it within 600 instructions. Group intervals
     come from `0xd8f8` (`+0x24 + 12·group`). This does not prove that no reader exists
     elsewhere.
   - **ADVISOR-021** is not an untraced behaviour (V1-2).
   - No invented threshold or timing was found. The game tick is the economy turn,
     world `+0x1da70c`, as in the register.
4. **Runtime safety.**
   - Attach and detach follow `StartLevel`/`TearDown` (front end, load, reload) and
     `Dispose`.
   - `ParkEconomyRuntime.Load` re-subscribes `ForwardEvent`. `Level.Park` is set only in
     the constructor, so a loaded save stays connected.
   - Neither the queue nor the history is saved. Whether the original saves them (the
     record serializer is at `0xbc10`) was not established here. They are not
     registered.
   - Precedence: `--no-advisor`, `--advisor-say` and `--advisor-response` each disable
     the automatic advisor (`Game.cs:190`). The manual paths are unchanged.
   - Missing `Advisor.sam`: `TryCreate` logs and disables. Missing speech: `Play` logs
     and fails (see V1-2 for the success semantics).
   - Muted: fails (V1-1).
   - Non-original sandbox levels (`--sandbox`, plain `--smoke-test`, and the sandbox
     load entry) also attach the advisor. Only visits are excluded. The welcome there
     is response 1 from the `jungle` level bank. Plain `--smoke-test` captures were not
     re-run here; the author ran only `--load-original-level jungle`.
   - Headless movie paths are unaffected.
5. **Build and tests** (SDK 10.0.401, Release, merged copy):
   - Build: 0 errors.
   - `OpenTPW.Tests` without assets: **895 passed / 233 skipped / 0 failed**, which is
     main's 853/232/0 plus 42 passed and 1 skipped.
   - With `OPENTPW_GAME_PATH`: **1065 passed / 63 skipped / 0 failed** (8 min 25 s).
   - `fidelity_register.py --check`: 145 unresolved unique APPROX IDs, exit 0.
   - Evidence runner (`--mac-bin`, `--pc-data`, SDK 8 host for the net8 harnesses):
     OK, 9 Python suites, 536 tests, 34 skipped (review lane 214/6/0). With the SDK 10
     host, the net8 harnesses report `missing-runtime`, as in section 52.
   - Line endings: `git diff --stat` equals `--ignore-cr-at-eol --stat` (16 files,
     +1440/−39). `Advisor.cs`, `GameFlow.cs`, `ParkEconomyRuntime.cs`, `AdvisorTests.cs`,
     `GameOptions.cs` and the docs are LF before and after. `Game.cs` stays CRLF. The
     five new C# files are CRLF.
   - The commit adds no Python, so the `as_posix` rule does not apply. The new review
     test builds no path strings for output.

### Handoff

1. Runtime owner: fix V1-1, V1-2 and V1-3 as above, then re-run the advisor tests and
   `fidelity_register.py --check`.
2. Optional: decide whether non-original sandbox levels should get the automatic
   advisor (an `[EXT]` choice), and run plain `--smoke-test` with assets.
3. Open (not blocking): whether the original saves the advisor queue/history.

### 56.1 Round 2: fixes in `a626cbf`

Scope: the stack `efc090d..a626cbf`. `b0562dd` is the cherry-pick of `f8375b2`, `99b23fc`
adds this review's round 1, and `a626cbf` holds the fixes. `efc090d` is fork main
(`origin/main`) at review time, so `a626cbf` fast-forwards it, and
`git merge-tree --write-tree origin/main a626cbf` exits 0. Builds and tests used
`git clone --shared` scratch copies under `/tmp` at `a626cbf`. No original code was
executed. `test_advisor_v2.py` pins the source witnesses below. The native operands
behind the fixes are still pinned by `test_advisor_v1.py`.

**Verdict: merge-ready.** All three blockers are fixed. One fix has a test gap (V1-2b
below), which is not a blocker.

#### Blockers

| Finding | Verdict | Fix and evidence | Mutation (fix reverted in a scratch copy) |
| --- | --- | --- | --- |
| V1-1 `--mute` | **Fixed** | `Advisor.CreatePlayer` (`Advisor.cs:304`): without a mixer, it opens a device only when `GameAudio.Enabled && AudioMixer.Current == null`. Native: `--smoke-test --mute --load-original-level jungle` logs `clock: wall clock (no audio device)` and no `Sound: SDL` line. Without `--mute`, the same run uses `game mixer (SDL audio queue)`. In the full run, `MutedSpeechKeepsTheWallClockWithoutOpeningADevice` **Passed**, not Inconclusive. | `openDevice: AudioMixer.Current == null` → that test fails |
| V1-2a wrapper success | **Fixed** | `AdvisorController.Update` calls `play` unconditionally and completes with `playbackSucceeded: true` and `span ?? 0`. The unused `0xb7d8` revalidation is gone from the controller. `RevalidatedPlaybackScoreAccepts` stays on the queue, unchanged against the helper. | `span.HasValue` → `UnplayedResponseStillRecordsHistoryAndReservesTheMinimumAction` fails |
| V1-2b Advisor option off | **Fixed, untested in OpenTPW.Tests** | `AutomaticAdvisor.Update`: with the option off, it silences and then runs `Controller.Update( …, _ => 0u )`, matching `0x6bb4`–`0x6bc0` and `0xbbf0`. ADVISOR-021 is narrowed to the object-identity question. `AdvisorOptionOffConsumesTheAdviceSilently` drives the **controller** with a 0-returning callback. It does not exercise `AutomaticAdvisor`. | Deleting the `Controller.Update( …, _ => 0u )` line leaves **all tests green**. `test_advisor_v2.py` now pins the line as a source witness. |
| V1-3 event 10 | **Fixed** | `ClearHistory` resets the variant (−1), the played flag and the slap count, and keeps `SavedGameTick`. This matches the stores at `+0xe4`/`+0xe8`/`+0xec` and the absence of a store at `+0xe0`. The test now expects `RepeatDelay` after event 10, and `Eligible` one tick later. Docs reworded in `PPC-advisor.md` (61, 86, 1014) and `LIPS.md:241`. | `history.Clear()` → `RepeatIntervalUsesQuarterTicksAndEventTenKeepsTheSavedTick` fails |

The mutations ran against the `Advisor|GameFlow|Sandbox` test filter (98 tests). The
`[EXT:sandbox]` restriction (`original &&` in `GameFlow.StartLevel`) is also not covered
by a unit test: removing it leaves the filter green. It is an extension choice, and the
native sandbox smokes below show its effect.

#### Queue port regression

The round-1 differential harness was rebuilt against the `a626cbf` port and the
unchanged helper (`f51e874`, byte-identical). It ran 2,000 seeds × 400 steps =
800,000 operations, with one change: about 1 in 9 steps is now event 10. The port calls
`ClearHistory()`. The helper side calls a review-only model of the `0x9dd8`–`0x9e80` loop,
written independently: for every descriptor it sets `(saved tick kept, −1, false, 0)`.
**All 800,000 operations matched.** This covers every result tuple, slot, history entry,
busy flag, reservation and revalidation answer. The V1-2 semantics change only the
controller's argument to `CompletePlaybackAttempt`. The queue API is unchanged, so the
harness still drives both success and failure. Sensitivity: putting `history.Clear()`
back into the port fails at seed 0, step 2. The member diff (`members()` from
`test_advisor_v1.py`) still shows every helper member textually equal in the port.
The only additions remain `ClearHistory`, `HasDescriptor` and `IsAttemptOutstanding`.

#### Sandbox and the manual advisor

`Game.CreateAdvisor` builds the manual `--advisor-say`/`--advisor-response` presentation
from the arguments alone, outside `GameFlow`. The `original` flag does not reach it.
Native results (macOS arm64, SDK 10, `scripts/run.sh`, scratch copy):

| Command | Result |
| --- | --- |
| `--smoke-test --advisor-say 1` (sandbox) | Passed. `sp_001`, 4.23 s, game mixer. No automatic advisor lines. |
| `--smoke-test --sandbox --advisor-response 1` | Passed. Response 1 → `/levels/jungle/Speech/speechHD.SDT` sample 1, 28.63 s. |
| `--smoke-test --mute --sandbox` and plain `--smoke-test` | Sandbox smoke passed. No `Advisor scoring`/`game event`/`says` lines. |
| `--smoke-test --mute --load-original-level jungle` | Passed. Event 0 → response 1, `clock: wall clock (no audio device)`, no SDL audio. |
| `--smoke-test --load-original-level jungle` | Passed. Event 0 → response 1, `game mixer (SDL audio queue)`. |

#### Cherry-pick `b0562dd`

`f8375b2` and `b0562dd` touch the same 16 files. For every file except
`docs/FIDELITY-REGISTER.md`, the changed lines are identical. `git range-diff` shows
only register hunks: counts 140→147 instead of 138→145, COMPAT 15/16, and Game.cs line
shifts from the TPI-FSH base. `fidelity_register.py --check` passes at `b0562dd` (147) and
at `a626cbf` (148, with ADVISOR-022 added). The resolution is correct.

#### Checks (independent)

- Build: `OpenTPW.sln` Release, SDK 10.0.401 — 0 errors.
- `OpenTPW.Tests` without assets: **908 passed / 241 skipped / 0 failed**.
- With `OPENTPW_GAME_PATH`: **1078 / 71 / 0** (10 min 41 s).
- Both test results match the author's.
- `fidelity_register.py --check`: 148 unresolved unique APPROX IDs, exit 0.
- Evidence runner (`--mac-bin`, `--pc-data`, SDK 8 host): OK, 9 Python suites, 555 tests,
  39 skipped. The review lane has 233 tests, 11 skipped. The runner does not set
  `OPENTPW_REVIEW_REPO`, so the git classes skip there. Run directly with all three
  variables, `test_advisor_v1`+`test_advisor_v2` give 24 tests, OK.
- Line endings: `git diff efc090d a626cbf --stat` equals `--ignore-cr-at-eol --stat`
  (19 files, +2003/−45). Every file changed by `a626cbf` keeps its style:
  `AdvisorControllerTests.cs`, `AutomaticAdvisor.cs`, `AdvisorController.cs` and
  `AdvisorScoreQueue.cs` are CRLF; the docs, `Advisor.cs`, `AdvisorTests.cs` and
  `GameFlow.cs` are LF.
- `git -c core.whitespace=cr-at-eol diff --check efc090d a626cbf`: clean.

#### Low notes

- **Stale `LIPS.md` APPROX rows (pre-existing).** The rows for ADVISOR-003, 004, 005, 006, 007,
  008, 010, 011, 012 and 014 cite a line that is not their APPROX tag. The set is identical
  on `efc090d`. The current lines are:

  | ID | Cited | Current |
  | --- | --- | --- |
  | ADVISOR-003 | `Advisor.cs:74` | `Advisor.cs:88` |
  | ADVISOR-004 | `Advisor.cs:58` | `Advisor.cs:67` |
  | ADVISOR-005 | `Advisor.cs:247` | `Advisor.cs:345` |
  | ADVISOR-006 | `Advisor.cs:94` | `Advisor.cs:112` |
  | ADVISOR-007 | `Advisor.cs:118` | `Advisor.cs:136` |
  | ADVISOR-008 | `Advisor.cs:235` | `Advisor.cs:333` |
  | ADVISOR-010 | `SpeechAudioPlayer.cs:34` | `SpeechAudioPlayer.cs:39` |
  | ADVISOR-011 | `SpeechAudioPlayer.cs:31` | `SpeechAudioPlayer.cs:36` |
  | ADVISOR-012 | `SpeechAudioPlayer.cs:62` | `SpeechAudioPlayer.cs:77` |
  | ADVISOR-014 | `Mp2Decoder.cs:55` | `Mp2Decoder.cs:49` |

  The rows for ADVISOR-015–022 are all current. `test_advisor_v2.py` pins that this
  set is unchanged.
- **V1-2b and `[EXT:sandbox]` have no unit test.** A test seam would need an
  `AutomaticAdvisor` with an injectable controller or presentation. This is optional.
- `--mute` now also puts the manual `--advisor-say` on the wall clock. That is
  consistent with RUNNING.md ("opens no audio device").

#### Handoff

1. Merge `a626cbf` (fast-forward of `efc090d`) with this review commit on top.
2. Optional: an `AutomaticAdvisor` test for the option-off path, and the stale LIPS rows.
