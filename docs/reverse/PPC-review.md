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
python3 -I tools/ppc-analysis/lanes/review/round2_evidence.py /Users/sander/server/game-assets/mac-feral/bin \
  --mac-hfs /Users/sander/server/game-assets/mac-feral/hfs.img \
  --pc-speech /Users/sander/server/game-assets/theme-park-world/Data/global/Speech
python3 -I tools/ppc-analysis/lanes/review/round3_evidence.py /Users/sander/server/game-assets/mac-feral/bin
python3 -I tools/ppc-analysis/lanes/review/round4_evidence.py /Users/sander/server/game-assets/mac-feral/bin
OPENTPW_PPC_BIN_ROOT=/Users/sander/server/game-assets/mac-feral/bin \
  python3 -I -m unittest discover -s tools/ppc-analysis/lanes/review -p 'test_*.py'
```

`round2_evidence.py` (section 4 onwards) uses the same pinned `Binary` and
fails closed in the same way. Its optional asset check prints only sizes,
SHA-256 values and image offsets. `round3_evidence.py` (section 12 onwards)
additionally pins `engine_shared.data` and `ltms_shared.data`; `round4_evidence.py`
(section 18 onwards) reuses those pins. With the binary variable set, all 65 lane
tests run, with no skips. Without it, 62 pass and the three original-file
witnesses skip.

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
