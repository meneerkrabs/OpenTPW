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
`$RIDES_CHECKOUT` a checkout of the rides lane, and `$INTEGRATION_CHECKOUT` the
integration checkout under review.

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
OPENTPW_PPC_BIN_ROOT=$FERAL_BIN \
OPENTPW_PC_DATA=$TPW_DATA \
  python3 -I -m unittest discover -s tools/ppc-analysis/lanes/review -p 'test_*.py'
# Every ppc-analysis Python suite, one lane per process (section 44):
python3 -I -B tools/ppc-analysis/run_evidence_checks.py --mac-bin $FERAL_BIN \
  [--pc-data $TPW_DATA] [--require-fixtures] [--dotnet $DOTNET8/dotnet]
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
