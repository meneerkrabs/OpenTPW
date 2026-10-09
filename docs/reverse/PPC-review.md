# PPC lane review (independent ninth reviewer)

2026-10-09. Static review only. No original program was executed or emulated.
No binary bytes, expanded sections or disassembly are stored here. The
verifier prints addresses, decoded operand fields, interpreted constants and
conclusions only. Binary identities are the three SHA-256 values in
[FINDINGS.md](FINDINGS.md). The `.data` files are Feral's Mac PowerPC port, so
Mac facts transfer to the PC executable only when separate PC evidence
supports them (see "Mac/PC transfer" below).

## Reproduce

```sh
python3 -I -m unittest discover -s tools/ppc-analysis/lanes/review -p 'test_*.py' -v
python3 -I tools/ppc-analysis/lanes/review/review_evidence.py /Users/sander/server/game-assets/mac-feral/bin \
  --pc-save /Users/sander/server/game-assets/theme-park-world/Data/levels/jungle/Easymode.TPWI
python3 -I tools/ppc-analysis/lanes/review/reloc_audit.py /Users/sander/server/game-assets/mac-feral/bin/*.data \
  /Users/sander/server/game-assets/mac-feral/bin/libraries/*.data
```

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

## Other lanes at review time

At this review's commit, no domain lane had committed. Uncommitted evidence
files were seen in guests, economy, rides, ui and advisor; they were read but
not edited. Economy's `evidence.py` correctly states that PC SAM inputs do
not prove PC arithmetic. Its saturating-conversion model differs as noted in
§2. The rides lane contains a .NET witness project whose `bin/` and `obj/`
outputs are git-ignored. At integration, root should check that it adds no
package dependencies. Further lane findings need a follow-up review round.
