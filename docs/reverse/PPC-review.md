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
OPENTPW_PPC_BIN_ROOT=/Users/sander/server/game-assets/mac-feral/bin \
  python3 -I -m unittest discover -s tools/ppc-analysis/lanes/review -p 'test_*.py'
```

`round2_evidence.py` (section 4 onwards) uses the same pinned `Binary` and
fails closed in the same way. Its optional asset check prints only sizes,
SHA-256 values and image offsets. With the binary variable set, all 28 lane
tests run, with no skips. Without it, 27 run and the original-file witness
skips.

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
