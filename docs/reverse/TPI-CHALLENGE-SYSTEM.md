# Theme Park Inc challenge system: static trace

Executable: English no-CD TPI `Game.exe`, SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`.
Method: static reading of the executable (a disassembly listing and the file bytes); the game was never run. No listing or byte dump is included here.
Data: the shipped `Data/Challenges.sam`, `Data/levels/Standard.sam` and `Data/levels/science/Standard.sam`.

Conventions: "slot" = one 0x90-byte challenge record; offsets are slot-relative unless written `sys+`. "day" = 864 000 000 000
= `0xC9_2A69C000` units of the game clock (100 ns ticks; every day conversion in the module pushes `0x2A69C000, 0xC9` to
`__alldiv 0x871A30` / `__allmul 0x871E20`). "now" = `0x6089C0(out, 0,0,0,0,0,0,0,0)` on the clock at `[0xAA131C]+0x428` (or
`[0xADE6B0]+0x428`, same object family): clock base + world `+0x1EAB40` × clock `+0x1C` / 4 × 10 000 000 (`0x6089C0`–`0x608A0C`).

---

## 1. Object layouts

### 1.1 Challenge system object (`sys`)

Constructor `0x5C7400` (thunk `0x40312A`, called from `0x6485A9`). It is a world "thing" (base ctor `0x637100`, kind arg
`0x13`), subscribes to message types `0x20`, `0x0B`, `0x11`, `0x07` (`0x5C7455`–`0x5C749E`, via `0x440830`/`0x436AC0`), then
loads the slots (`0x5C74C5` → `0x5C7630`).

| Offset | Width | Name (serializer `0x5C7C70`) | Meaning / evidence |
|---|---|---|---|
| `+0x18 + i*0x90`, i = 0..9 | 0x90 ×10 | (slot records, `0x5C2320` per slot at `0x5C7CC1`/`0x5C7E06`) | `GetChallenge(i)` = `sys+0x18+i*0x90` (`0x5C7FE0`) |
| `+0x5B8` | 8 | `mNextChallengeDelayTime` (`0x5C7CEF`, size 8 checked `0x5C7D19`) | earliest time of the next offer; 0 at construction (`0x5C744F`/`0x5C7457`) |
| `+0x5C0` | 1 | `mSystemSwitchedOn` (`0x5C7D42`, size 1) | latched to 1 by `0x5C7977` (also `0x5C8110` in the unreferenced `0x5C80A0`); never cleared except ctor `0x5C745E` |
| `+0x5C1` | 1 | — (not serialized) | set to 1 on every offer (`0x5C824A`), cleared by ctor (`0x5C7464`); no reader found in `0x5C1000`–`0x5C9300` |
| `+0x5C2 + 2i` | 2 ×10 | `mActualThing[i]` (`0x5C7D81`.., size 2) | cleared by ctor (`0x5C74A5`–`0x5C74BD`) and set to 0 when slot i is lost (`0x5C7A34`, `0x5C885C`, `0x5C8916`); only reader is the unreferenced `0x5C89C0` |

### 1.2 Slot record (0x90 bytes)

Field names come from the slot serializer `0x5C2320` (string at the `push`, field address at the following `lea`, size as
the pushed width). Fill-from-data mapping comes from the record constructor `0x5C21E0` (24 stack args) and the loader
`0x5C76C8`–`0x5C779C`.

| Off | W | Serializer name | Source `Challenges.sam` key | Notes |
|---|---|---|---|---|
| 0x00 | 4 | `mType` (`0x5C234F`) | `Type` | 0 = empty slot (loader `0x5C77A5`, `0x5C7516`) |
| 0x04 | 4 | `mTargetTime` (`0x5C239D`) | `TargetTime` | days of the timed phase (`0x5C575B`, `0x5C85A5`) |
| 0x08 | 4 | `mTargetVal` (`0x5C23EA`) | `TargetVal` | |
| 0x0C | 4 | `mTargetVal2` (`0x5C2437`) | `TargetVal2` | |
| 0x10 | 2 | `mTargetObj` (`0x5C2484`) | `TargetObj` (word) | thing/ride type id |
| 0x12 | 2 | `mTargetObj2` (`0x5C24D4`) | `TargetObj2` (word) | no state-logic reader found |
| 0x14 | 4 | `mTargetStaffType` (`0x5C2524`) | `TargetStaffType` | no state-logic reader found |
| 0x18 | 4 | `mCashPrize` (`0x5C2571`) | `CashPrize` | |
| 0x1C | 4 | `mGTPrize` (`0x5C2AAA`) | `GTPrize` | ctor clamps to ≥ 1 (`0x5C22C0`–`0x5C22CC`), matching the data comment "minimum value of 1" |
| 0x20 | 4 | `mSharePrize` (`0x5C2AF7`) | `SharePrize` | |
| 0x24 | 4 | `mSharePriceEffectIfWon` (`0x5C2B44`) | `SharePriceEffectWon` | percent |
| 0x28 | 4 | `mSharePriceEffectIfLost` (`0x5C2B91`) | `SharePriceEffectLost` | percent |
| 0x2C | 4 | `mDoubleOrNothingFollowup` (`0x5C25BE`) | `FollowupType` | only read by the load-time filter `0x5C75D0` |
| 0x30 | 1 | `mCheckAtEndOnly` (`0x5C260B`) | `CheckAtEndOnly != 0` (`0x5C777B` `setne`) | |
| 0x34 | 4 | `mGracePeriodLimit` (`0x5C2832`) | `GracePeriodLimit` | days of grace (state 2) |
| 0x38 | 4 | `mTriggerData` (`0x5C2A5D`) | `TriggerData` | availability threshold |
| 0x3C | 4 | `mGraceTargetVal` (`0x5C287F`) | `GraceTargetVal` | no state-logic reader found |
| 0x40 | 4 | `mMinDaysTilTrigger` (`0x5C2798`) | `MinDaysTilTrigger` | |
| 0x44 | 4 | `mMaxDaysTilTrigger` (`0x5C27E5`) | `MaxDaysTilTrigger` | |
| 0x48 | 4 | `mDelay` (`0x5C274B`) | `DaysDelay` | |
| 0x4C | 4 | `mSender` (`0x5C2BDE`) | `Sender` | index into the 36-byte table at `0x9CD000` |
| 0x50 | 4 | `mScriptIndex` (`0x5C2C2B`) | `ScriptIndex` | |
| 0x54 | 4 | `mMissonNeeded` (`0x5C2C78`) | `MissionNeededToActivate` | |
| 0x58 | 4 | `mPromoGiven` (`0x5C2CC5`) | `PromoGiven` | |
| 0x5C | 4 | `mChallengeLost` (`0x5C265B`) | — (0) | loss count of this slot |
| 0x60 | 1 | `mOfferTimeSet` (`0x5C28CC`) | — (0) | availability latched |
| 0x68 | 8 | `mOfferTime` (`0x5C2921`) | — (0) | time from which the slot may be offered |
| 0x70 | 4 | `mValue` (`0x5C29C3`) | — | progress value |
| 0x74 | 4 | `mValue2` (`0x5C2A10`) | — | second progress value |
| 0x78 | 8 | — (not serialized) | — | start of the current phase (`0x5C55DF`, `0x5C574D`, `0x5C8597`) |
| 0x80 | 8 | `mFinishTIme` (`0x5C296E`) | — | end of the current phase |
| 0x88 | 4 | `mCurrentStatus` (`0x5C26A8`) | — (0) | state, see §4 |
| 0x8C | 2 | `mThingInFocus` (`0x5C26F8`) | — | out-parameter of the type 25/46 value functions (`0x5C50CC`, `0x5C50F1`) |
| 0x8E | 1 | `mSentTimeWarning` (`0x5C2D12`) | — | only ever written 0 in the module (`0x5C4B49`, `0x5C5713`, `0x5C5874`, `0x5C7AB1`, `0x5C856B`, `0x5C87AA`); read by `0x5C8A90` |

Unnamed holes: 0x31–0x33, 0x61–0x67, 0x8F (padding as far as read).
`Independent` (schema field 14) is parsed but not passed to the slot (`0x5C76C8`–`0x5C779C` loads 24 of the 25 row fields).

Serializer note (uncertain): the read branch of `0x5C2320` (taken when its first argument ≠ 0, version check 0..1 at
`0x5C2338`–`0x5C2343`) records `mOfferTime` and `mFinishTIme` with size 4 (`0x5C2926`, `0x5C2973`, `push %ebp` = 4), while the
write branch uses the 8-byte writer `0x5BC220` (`0x5C3052`, `0x5C3071`). Whether this loses the high dwords on load or the
chunk reader `0x7CC960` tolerates it was not read.

---

## 2. Where the settings land

`Standard.sam` is parsed into the global parser object `0xA01A60` (static ctor `0x41B320`; vtable `0x93A048`, schema accessor
`0x41B350` → records at `0x9966C0` + i·0x3C; data base accessor `0x41B380` = object+8). The generic layout builder `0x418F60`
assigns each value record a dword at `data + 4·k` with k starting at 1, groups of type-2…type-3 records become arrays of
`count` (+0x34) rows, and each array is followed by one count dword (max index + 1, written by `0x419520`). Reproducing that
walk gives:

| Address | Key | Evidence of use |
|---|---|---|
| `0xA028EC` | `Challenges.DaysBetweenOffers` | `0x5C8250` (next-offer delay) |
| `0xA028F0` | `Challenges.MissionNumberForFirstChallenge` | `0x5C796D`, `0x5C8106` (switch-on gate) |
| `0xA028F4` | `Challenges.ShortTimeLeftWarningAt` | **no reference anywhere in `full.s`** |
| `0xA028F8`–`0xA0290C` | `FiringLineForRank[0..5].Limit` | `0x74C46A` (promotion), `0x5C8B4B` (unreferenced getter) |
| `0xA02910` | FiringLineForRank count | — |
| `0xA02914`–`0xA02960` | `ChallengesInThisLevel[0..19].ChallengeType` | `0x5C76B8`–`0x5C77D2` reads **only [0..9]** (`cmp $0xA0293C`) |
| `0xA02964` | ChallengesInThisLevel count | — |
| `0xA02968` | `SpamBalance.YearsBeforeStarting` | (spam system, not traced) |
| `0xA0296C` | `SpamBalance.SpamEmailsPerYear` | (spam system, not traced) |
| `0xA02168` | `ShareInfo.CompanySharesIssued` | `0x621CFC` (share prize, §6) |

The layout is consistent with every direct reference above (mission gate at `0xA028F0`, interval at `0xA028EC`, firing table at
`0xA028F8`, level list at `0xA02914`). Retail values (global `Standard.sam` lines 1060–1069): DaysBetweenOffers 20,
MissionNumberForFirstChallenge 0, ShortTimeLeftWarningAt 20, FiringLineForRank 255,5,5,5,5,5.

Parse order (`0x6481FD`–`0x6482A8`): global `data\levels\Standard.sam` with flag 0 (edi = 0 at `0x648096`) — flag 0 makes
`0x419CD0` reset every value to defaults first (`0x419CE2`–`0x419CE6` → `0x419830`); then `<level>\Standard.sam` with flag 1
(ebp = 1 at `0x648091`), overlaying without reset; then, only when `[0xC65B80] == 2`, a third `"%s\%s%s"` file (`0x648276`–`0x6482A8`).
The `online_Standard.sam` pair is parsed instead when `[0xC65B80]` equals ebp (= 1) (`0x6481A2`–`0x6481E7`); the meaning of the
mode values in `[0xC65B80]` was not decoded beyond that.

---

## 3. Loading slots (`0x5C7630`, thunk `0x403099`; called by the ctor and by debug menu `0x53547C`)

1. All ten slots get `mType = 0` (`0x5C765C`–`0x5C766F`).
2. `data/challenges.sam` (`0x5C768A`) is parsed into a stack parser (vtable `0x93DE6C`; schema `0x98E878` + i·0x3C via
   `0x5C78A0`; data base `0x5C78D0` = object+8). Rows `Challenges[0..59]` are 25 dwords (100 bytes) in schema order: Type,
   FollowupType, TargetTime, TargetVal, TargetVal2, TargetObj, TargetObj2, TargetStaffType, CashPrize, GTPrize, SharePrize,
   SharePriceEffectWon, SharePriceEffectLost, CheckAtEndOnly, Independent, MinDaysTilTrigger, MaxDaysTilTrigger, DaysDelay,
   TriggerData, GracePeriodLimit, GraceTargetVal, Sender, ScriptIndex, MissionNeededToActivate, PromoGiven (records
   `0x98E8B4`…`0x98EE54`, array record `Challenges` count 0x3C at `0x98EE90`). Row n starts at parser+0xC+100n
   (confirmed by the constructor's field placement, e.g. GTPrize ≥ 1 clamp lands on the GTPrize row field). A parse failure
   only prints `0x9CD516` and continues.
3. For each of the first **ten** `ChallengesInThisLevel[k].ChallengeType` values n (`0x5C76BD`): if n ≤ 0 (signed `jle`) skip;
   else build a record from row **`Challenges[n]`** (the value is a row index, not a challenge Type) with `0x5C21E0` and copy it
   (`rep movsl`, 0x24 dwords, `0x5C77C9`) into the **first slot whose mType is 0** (`0x5C77A5`); if none is free it is dropped.
   No range check on n.
4. Follow-up filter `0x5C75C0`: a slot whose `mDoubleOrNothingFollowup` ≠ 0 is emptied (`mType = 0`) unless another occupied
   slot has `mType` equal to it. Retail data has no FollowupType, so no effect.

Defaults: missing keys are 0 (`0x419830` reset), so e.g. `MinDaysTilTrigger` 0, `MaxDaysTilTrigger` 0 for rows that omit it.
Record ctor zeroes 0x5C, 0x60, 0x68–0x88, 0x8C/0x8E (`0x5C228A`–`0x5C22B9`).

Science level (`Data/levels/science/Standard.sam` 153–164): list `2,4,5,14,13,18,21,8,20,22` → rows: 2 sell hot dogs (Type 2),
4 sideshow satisfaction (35), 5 staff happy (25), 14 safety review (46), 13 avg happiness (36), 18 no strikes (41),
21 Steaming Log Mill (56), 8 Waterwheel (17), 20 Boating Lake (48), 22 Mech Bird (17). Slot order = list order.

---

## 4. Slot state machine

States (`mCurrentStatus`, +0x88):

| Value | Meaning | Set by |
|---|---|---|
| 0 | idle / not yet offered | record ctor `0x5C22B3` |
| 1 | offered, awaiting the player — **and also "lost"** | offer `0x5C8244`; lost `0x5C4815`; (`0x5C4AC0`, unreferenced) |
| 2 | accepted, grace period running | accept `0x5C864C`; (`0x5C4C1F`, `0x5C55AA`, unreferenced copies) |
| 3 | accepted, timed phase running | accept with GracePeriodLimit 0 `0x5C8561`; grace end `0x5C5709`; (`0x5C4B3F`, `0x5C586A` unreferenced) |
| 4 | won | `0x5C428D` |

No code moves a slot from 1 back to 0 or to any other state except accept (1 → 2/3), and none leaves 4. There is no offer
expiry, no decline, and no decline counter.

### 4.1 Update cadence

`sys` is a thing in the world's update list: the world loop `0x649540`… calls `0x6375E0` per thing (`0x6495C1`), which switches
on the thing kind and calls the challenge update `0x5C7930` (`0x637767`, thunk `0x4073FB`) — once per world update pass, not
per day. Skipped when `[0xC65B80] == 1` (`0x637759`). `0x5C7930` itself returns at once unless `[0xC65B80] == 0`
(`0x5C7938` → `0x570F40` returns `&0xC65B80`; `cmpl $0,(%eax)` at `0x5C793D`). Messages reach `0x5C8010` (`0x637A75`).

### 4.2 `0x5C7930` (per update)

1. Switch-on (`0x5C7942`–`0x5C7977`): if `+0x5C0` is 0, it becomes 1 only when the park is open
   (`0x5B1D30`: world `+0x1EAB44 == 0`; `0x64F924`–`0x64F935` prints "closed" when that word is nonzero) **and**
   career `+0x105C` ≥ `[0xA028F0]` (signed `jl` skip). Career object = `0x42A3B0` (thunk `0x40B528`) singleton `0xBFC484`.
   While off nothing else happens (both loops then return).
2. If any slot has state 2 or 3 (`0x5C7986`–`0x5C799C`), process **every** slot (`0x5C79D2`–`0x5C7A46`):
   - **State 2 (grace)**: `UpdateCurrentValue 0x5C4DD0`; `d = 0x5C54E0(1)` = (`mFinishTIme` − now) / day, signed 64-bit
     truncating division; if `d == 0` → `0x5C5680` (start timed phase). Then, **if the park is closed → lost** (`0x5C7A08`–`0x5C7A1C`).
   - **State 3 (running)**: `UpdateCurrentValue`; `d` as above (`0x5C7A5C`–`0x5C7AA6`).
     - `d == 0` (final day): `+0x8E = 0`; `0x5C73D0` (Type 42 → reopen shops, §6); `0x5C2150(mSender)` (§6);
       `DidThisWinIt(1)` `0x5C3F60` → won `0x5C88B0` (log `0x9CD580`, `0x5C4250`) else lost `0x5C88F0` (log `0x9CD59C`,
       `0x5C47D0`, `mActualThing[i] = 0`). The failure test `0x5C3DB0` is **not** run on this pass.
     - `d ≠ 0`: if `HasFailed 0x5C3DB0` → lost (log `0x9CD59C`, `0x5C47D0`, `mActualThing[i]=0`, `0x5C7A1E`–`0x5C7A34`);
       else if park closed → lost; else if `DidThisWinIt(0)` → won (log, `0x5C4250`, `0x5C7B14`–`0x5C7B36`).
   - Consequence of the arithmetic (not a separate rule): if a single update ever moves now past `mFinishTIme` by ≥ 1 day,
     `d` stays negative and nonzero and the slot never times out.
3. Else (no slot in 2/3, `0x5C7B40`): `CalcAvailTime 0x5C36D0` for every slot with `mType ≠ 0` and state 0; then if
   now > `+0x5B8` (`0x7D8F30`: unsigned 64-bit, strict) → `Offer 0x5C8140(0)`.

### 4.3 Availability: `CalcAvailTime 0x5C36D0`

Only while `mOfferTimeSet == 0` (`0x5C36D9`). `ok = trigger(type)` (jump table `0x5C3BB8`/byte index `0x5C3C14`, type 1..58; see
§7), then with `days = 0x608C70()/day` (game time minus a calendar origin built by `0x7D91E0` from all-zero fields;
whether that origin is the park start is **unread**):
`ok = (ok && days > MinDaysTilTrigger) || (MaxDaysTilTrigger != 0 && days > MaxDaysTilTrigger)` (both unsigned `seta`,
`0x5C3AF5`, `0x5C3B30`); then `ok = ok && MissionNeededToActivate <= career+0x105C` (unsigned `setbe`, `0x5C3B45`).
If ok: `mOfferTime = now + DaysDelay·day` (DaysDelay zero-extended, `0x5C3B4C`–`0x5C3BAD`) and `mOfferTimeSet = 1`. The latch is
never cleared, so the trigger is evaluated until it first holds, once.

### 4.4 Offer: `0x5C8140(arg)`

Called from `0x5C7BA9` and the debug menu id `0x186A1` (`0x53523B`). Returns at once if any slot is in state 2/3 or now <
`+0x5B8` (`0x7D8ED0`, strict). Otherwise picks the **lowest slot index** with `mType ≠ 0`, state 0, `mOfferTimeSet` and now >
`mOfferTime` (strict, `0x5C81B2`–`0x5C81FA`), posts the offer message (`0x5C8320`: message `0x1B` kind 0, §6), logs
`"Challenge %d is being offered!"` (`0x5C8230`, slot index), sets state 1 and `+0x5C1 = 1`, and sets
`+0x5B8 = now + DaysBetweenOffers·day` (`0x5C8250`–`0x5C82AD`, sign-extended). At most one offer per call. Offers continue every
DaysBetweenOffers while earlier offers sit unanswered in state 1; only states 2/3 block offering. `arg` is unused in the read code.

### 4.5 Accept: `0x5C84C0(index)`

Called from the challenge-mail UI on control id `0xC1202` (`0x52A657`–`0x52A66B`, index from `[0xA59F4C]`). The mail view
enables the control when the slot's state is 1 and the park is open (`0x52C2B2`, `0x52C2D9`). Other ids (`0xC1200`, `0xC11FC`,
-2; `0x52A59A`–`0x52A61D`) close the dialog without touching the system. Accept does not check the state or other slots.
Steps: `0x718990(table0x9CD000[Sender].dword1)` on the advisor thing (stop that advice if playing); sound `0xC6`;
`mValue = mValue2 = 0` (`0x5C8520`, `0x5C852A`); mark mail entries with kind `0x91` and this ScriptIndex handled
(`0x64EA60` → `0x636DB0`); then
- `GracePeriodLimit == 0`: state 3, `+0x8E = 0`, `+0x78 = now`, `mFinishTIme = now + TargetTime·day` (sign-extended);
  Type 42 → `0x650800(1,0)` (close kind-1 shops); sound `0xC6` (`0x5C8557`–`0x5C8631`).
- else: state 2, `+0x78 = now`, `mFinishTIme = now + GracePeriodLimit·day` (`0x5C8642`–`0x5C86E7`).
The slot method `0x5C4AE0` is an unreferenced copy of the same logic.

### 4.6 Grace → timed phase: `0x5C5680`

Posts message `0x1B` kind 1; state 3; `+0x8E = 0`; `+0x78 = now`; `mFinishTIme = now + TargetTime·day`; Type 42 →
`0x650800(1,0)`; sound `0xC6` (`0x5C569C`–`0x5C57E4`). `mValue/mValue2` are **not** reset, so event counts from the grace
period carry over. No win or failure test runs during grace.

### 4.7 Time warning

`ShortTimeLeftWarningAt` (`0xA028F4`) has no reference, and no store of a nonzero value into slot `+0x8E` exists in the module.
The only reader `0x5C8A90` ("any running slot with +0x8E set") is used by UI code at `0x54EFB7`. **No time-warning logic was
found in TPI.**

---

## 5. Offer schedule summary

- System switches on: park open and career `+0x105C` ≥ MissionNumberForFirstChallenge (0 in retail) — checked each update.
- Per slot: availability = trigger(type) after MinDaysTilTrigger days, or unconditionally after MaxDaysTilTrigger days (if
  nonzero), gated by MissionNeededToActivate ≤ career `+0x105C`; offer time = availability time + DaysDelay days.
- First offer: `+0x5B8` starts at 0, so the first due slot is offered on the first update after its offer time.
- Next offers: no sooner than DaysBetweenOffers (20) days after the previous offer; lowest slot index first.
- **No random number generator is used** anywhere in the traced challenge code (`0x5C3610`–`0x5C92A0`).
- Meaning of career `+0x105C`: compared with "mission" settings in both places; promotions are separate (`+0x1060` rank).
  What increments `+0x105C` is **unread**.

---

## 6. Outcomes

### 6.1 Won: `0x5C4250` (from `0x5C7B36`, `0x5C88D5`, `0x5C8832`)

1. Sound `0xC6` (`0x652860(0,[0xAE0124],0xC6,0,0,0)`; `0x652860` calls the audio interface `[0xADF080]` vtable +8).
2. State 4.
3. `CashPrize > 0` (signed): bank thing (world `+0x1EAB5A`) `0x5C0760(CashPrize)`: bank `+0x14 += P`, park income history
   slot `+0x1FEF8 += P`, bank `+0x12C += P`; then sound `0xC6` again (`0x5C4285`–`0x5C42C0`).
4. `GTPrize > 0`: career `+0x1074 += GTPrize` (`0x74C890`).
5. Shares object `0xAA0128`: `0x621CE0(SharePrize)` — if > 0, owned shares `+= SharePrize` and ownership % = owned·100 /
   CompanySharesIssued (`0xA02168`); `0x621D30(SharePriceEffectIfWon)` — if price and pct nonzero, price += price·pct/100
   (signed, truncated; stored only if > 0).
6. Message `0x1B` (class `0x93A394`, fields slot ptr, kind 2) posted to queue `[0xA09728]` (`0x5C4357`–`0x5C43AA`).
7. `PromoGiven > 0` (unsigned): message `0x25` (class `0x93A3DC`, PromoGiven, SharePrize) and a type-`0x19` wrapper of
   notification object `0x5CF4D0` (notification id `0xE`). Message `0x25` is handled at `0x5F10F7` ("Promotion Message
   Received", case 2 of table `0x5F12F4`, base `0x23`) → `0x74C450(PromoGiven)`: rank `+0x1060 = PromoGiven`, firing line raised
   to `FiringLineForRank[rank]` if lower.
8. Level record (career `+0x4C` → `+0x64` → `0x74F4B0`): `+0xF0 += 1`; if every slot is empty or won, also `+0xF4 = 1`
   (`0x5C4514`–`0x5C45FC`) — plausibly the `mChallengesDone`/`mAllChallengesDone` fields (strings `0x9E95BC`/`0x9E95A4`; link
   not proven).
9. Message `0x26` (class `0x93A3E8`, ScriptIndex). Its handler was not found (case 3 of `0x5F12F4` is a no-op).
No reset of shops for Type 42 on an early (`d ≠ 0`) win; Type 42 is CheckAtEndOnly in retail data.

### 6.2 Lost: `0x5C47D0` (thunk `0x402126`; from `0x5C7A2F`, `0x5C8911`, `0x5C8851`)

1. Sound `0xAF`.
2. State 1.
3. If `mChallengeLost == 0`: career firing line (dword 0 of `0xBFC484`) −= 1; `0x621D30(−SharePriceEffectIfLost)`.
4. `mChallengeLost += 1`.
5. If firing line > 0 (signed): message `0x1B` kind 3 (`0x5C49A3`). Else: notification `0x5CEE50(7)` wrapped in a type-`0x19`
   message, and advice `0x1A3` on the advisor thing (`0x71A930`/`0x718820`); the kind-3 message is not posted in this branch.
Caller side: `mActualThing[i] = 0`. On an early loss (`d ≠ 0`) `0x5C73D0` (Type 42 shop reopening) and `0x5C2150` are not called.

### 6.3 Other side effects

- `0x5C2150(Sender)` at time-out: copies 9 dwords at `0x9CD000 + 36·Sender`, calls advisor `0x718990(dword1)`, which clears the
  advisor's active flag (+0x100) when its current advice (+0xF8) is that id. Table rows 2..6 hold e.g. `0x35,0x239,…` (row 2);
  rows 0,1,7,8 are placeholders `0x285`.
- Type 42: `0x650800(kind,open)` walks the world shop list (`+0x1EAB88`), for things whose record `+0x218 == kind` calls
  `0x5E1DC0` (open) or `0x5E1CD0` (close). Start: `(1,0)`; time-out `0x5C73D0`: `(1,1)`. `0x5C8B70` reports "kind-1 shop while a
  Type 42 runs" to shop code (`0x5E1AA2`, `0x5E1E14`, `0x5E2057`, `0x5E2C80`). `0x5C8BE0` (thing `+0x70 == 0` while a Type 46
  runs) is called from `0x5E1BCE`, `0x5E2D42` (effect unread).
- Firing line reset: `0x74BB40` sets rank 1 and line 5 (per prior note; not re-read here).
- Debug: byte `0xA9B894` (toggled at `0x537891`) makes `HasFailed` return 0 and `DidThisWinIt` return 1 (`0x5C3DB0`, `0x5C3F78`).

---

## 7. Types

Dispatch tables: trigger `0x5C3BB8`/`0x5C3C14` (index type−1), value `0x5C52DC`/`0x5C5334` (index type−9), fail
`0x5C3E90`/`0x5C3EC0` (index type−10), win `0x5C4130`/`0x5C416C` (index type). "park" = `[[0xAA131C]]` → `0x64EA90`
(world thing `+0x1EAB54`); functions are named by address only — their game meaning is mostly **unread**.

### 7.1 Win (`DidThisWinIt(atEnd) 0x5C3F60`)
CheckAtEndOnly and !atEnd → false. Then: types 1–13,16,17,20,22,45,46: V ≥ TV; 48: V2 == 0 && V ≥ TV; 50: V ≠ 0;
51: V ≥ TV && V2 ≠ 0; 52: V2 ≥ TV2 && V ≥ TV; 53,56: V ≥ TV && V2 ≥ TV2; 55: V ≥ TV && V2 == 0;
57: V == 0 ? 0 ≥ TV : V2·100/V ≥ TV; 58: V2 == 0 ? 0 ≥ TV : V·100/V2 ≥ TV (signed `idiv`);
19: park `0x5B70E0(TargetObj)` ≥ 3; 28: park `0x5B54C0(TargetObj)` ≥ TV; 41: `0x64E910`→`0x634240()` == 0;
24,25,27,34,35,36,38,39,40,42,43: true (end-only types; failure is `HasFailed`); type 0: false; others: debug `0x9CD480`, true.
All comparisons signed.

### 7.2 Failure (`HasFailed 0x5C3DB0`, only while d ≠ 0)
48: V2 ≠ 0 || V < TV; 50: V == 0; 52: V2 < TV2; 53,56: V < TV || V2 < TV2; 55: V < TV || V2 ≠ 0; 34,10,25,35,36,37,46: V < TV;
38,39,40: V > TV; 41,42,43: V ≠ 0; others: never.

### 7.3 Value sources
Polled (`UpdateCurrentValue 0x5C4DD0`, every update in states 2 and 3): 35 V=`0x5B69C0()`; 34 V=round(Σ income history at
park `+0x1FEF8` − Σ expense history at `+0x1F808`, entries via `0x5C8F10`); 41 V=`0x634240()`; 28 V=`0x5B54C0(TargetObj)`;
9 V=`0x5B2650()`·100/`0x5B0210(0)` (0 if divisor 0); 36 V=round(`0x5AF940()`); 10 V=`0x5B2710()`·100/`0x5B0210(0)`;
43 V=`(u8)0x5B0A30(7)`; 42 V=`(u8)0x5B0A30(1)`; 25 V=`0x5AFED0(&mThingInFocus)`; 46 V=`0x5B7D30(&mThingInFocus)`;
37 V=`0x5B2060(4)`; with obj = thing of TargetObj (`0x647850`): 48 V=obj`0x5E6D80()`; 56 V=(u8)obj`0x5E41D0(0)`, V2=obj`0x5E6D80()`;
52 V2=(u8)round(obj float +0x54); 53 V=(u8)obj`0x5E41D0(0)`, V2=(u8)round(obj +0x54); 55 V=(u8)obj`0x5E41D0(0)`,
V2=(u8)`0x5B0A30(7)`; 50 V=(`0x5B0A30(9)`||`0x5B0A30(8)`); 58 V2=`0x5B2060(4)`; 51 V=`0x5B70E0(&t,TargetObj)`, V2=(t's
record +0x64 ≥ 2). 57 nothing; all other types print `0x9CD4E0` every update.
Event-driven (`0x5C8940(type,obj,amount,toV2)`: every slot in state 2/3 with that type and TargetObj == obj or obj == 0 gets
V (or V2) += amount): message `0x20` (type, amount, obj from message +8/+0xC/+0x10, `0x5C802D`); message `0x11` with +0xC == 0 →
(16, 0, 1) (`0x5C8051`); direct posts: 39 or 40 (+1, by a +0x2A4 flag), 38 (+1), 48 (+1) at `0x5AEE5B`–`0x5AEEDE`; 12 (+edi),
3 (+1), 45 (+edi), 13 (+esi) at `0x5E5E8D`–`0x5E618E`; 17, 52, 58 (+1, obj = used thing's type word +0x16) and 57 (V2 +1,
conditional) at `0x61A85B`–`0x61A8F3`. Senders' meaning (prank, sale, ride use) is inferred from the data comments, not read.

### 7.4 Triggers (`CalcAvailTime`)
43: `0x61D670(1)` on world thing `+0x1EAB56` ≥ TD (unsigned); 35: same with 2; 2, 42: `0x5B9380()`; 37: `0x5B2060(4)` ≥ TD;
25: `0x5B32F0(6)` ≥ TD; 45: `0x5B9420()` && `0x5B94C0()` && `0x5B2060(4)` > 25; 40: park `+0x2282C` (`0x5B2540`) ≥ TD; 39: same ≠ 0;
3, 36: park `+0x22830` ≥ TD; 34: `0x5B9880()` ≥ TD; 46: `+0x14` ≥ TD; 6: `+0x18` ≥ TD; 10: `+0x1C` > TD (strict);
41: `0x5B2F40()` ≥ TD; 38: `0x5B0210(0)` ≥ TD; 4: `0x5B9420()`; 5: `0x5B9600()`; 8: `0x5B9740()`; 9: `0x5B9560()` && `0x5B2060(0)` > 10;
1: `0x5B92E0()`; 17,48,50–53,55–58: TargetObj thing exists/built (`0x5C3828`–`0x5C38B8`: `0x43C700` record +0x5C > 0 ? obj
`0x5E8210()` : obj byte +0x12D; no thing → `0x64EB20(TargetObj)`→`0x61E6A0()`); all others (7, 11–16, 18–24, 26–33, 44, 47,
49, 54): debug `0x9CD458`, never (MaxDaysTilTrigger can still trigger them). ≥ is signed unless noted.

### 7.5 Retail rows (Challenges.sam) by Type
43 untidiness, 2 hot dogs, 37 visitors, 35 sideshow satisfaction, 25 staff happiness, 45 drink/ice income, 40 kid pranks,
17 ride riders (rows 8,12,22,30), 3 drinks, 42 hot-dog scare, 34 profit, 36 guest happiness, 46 safety, 39 adult pranks,
6 balloons, 10 costumes %, 41 strikes, 38 pranks, 48 Boating Lake, 56 Log Mill, 52 Diving Bell, 50 Whale Spout, 58 Sleigh
Swing, 57 Husky, 53 Scarabs, 55 Genie, 51 Palace Coaster. Types live in code but unused in data: 1, 4, 5, 8, 9, 16, 19, 24,
27, 28.

---

## 8. Differences from the Theme Park World port (`ParkObjectives.cs`)

- **Ten parallel slots, per-slot availability.** TPW offers from one scan with DaysUntilFirstChallenge /
  DaysAfterCompleted/DeclinedChallenge / DeclinesToForfeit; TPI has per-row triggers (TriggerData, Min/MaxDaysTilTrigger,
  DaysDelay, MissionNeededToActivate) plus one global DaysBetweenOffers gap, and a mission gate to switch on.
- **Offers do not block.** TPW stops while an offer waits for an answer; TPI keeps offering every DaysBetweenOffers while
  offers sit in state 1, blocks only during grace/running, and has no decline action, decline count or forfeiture.
- **Grace period (state 2)** with its own deadline and no checks; absent in TPW.
- **Cadence:** TPI checks every update (world pass); TPW checks on turns whose low two bits ≠ 0.
- **Park closed = loss** during grace or running (TPI); TPW only gates the start on an open park.
- **No time warning** in TPI (ShortTimeLeftWarningAt unreferenced), unlike TPW's `0x4D21FD` warning.
- **Type numbering differs**: TPI 1–58 with 34–58 new (end-only "keep X" types, ride-specific types with V/V2); TPW 1–33.
  Shared numbers do not share meaning (e.g. TPI 2 = hot dogs via events; TPW 1–8 are shop/ingredient sale types).
  TPI end-only types win on the final day without a final failure test.
- **Outcomes:** TPI pays cash, golden tickets, shares and share-price %, can promote (PromoGiven) and has the firing line;
  a lost challenge returns to state 1 rather than being re-offerable (TPW re-offers lost challenges). No follow-up chain logic
  beyond the load filter.
- **Level list holds row indices** (Challenges[n]), not types — the existing `docs/reverse/TPI-CHALLENGES.md` sentence "lists its
  challenges by type ... (types 2, 4, 5, 14, ...)" should read "row indices".

## 9. Still unread

- Game meaning of nearly every park accessor in §7 (`0x5B9380`, `0x5B2060(n)`, `0x61D670(n)`, `0x5B0A30(n)`, `0x5E41D0`,
  `0x5E6D80`, obj +0x54, `0x5B70E0`, `0x5B54C0`, `0x5AFED0`, `0x5B7D30`, `0x634240`, …) and of the event senders.
- Who posts message `0x20` (generic challenge event) and with which types (needed for types 2, 6, 16-style counters, 57's V).
- Consumers of message `0x1B` kinds 0–3 (mail creation, LAN text; text builders `0x5C5990`, `0x5C5EE0`, `0x5C63E0`,
  `0x5C6470`, `0x5C6A60` read ScriptIndex/GracePeriodLimit/Sender) and of message `0x26`.
- What advances career `+0x105C` (mission counter) and whether the career object is saved.
- Whether a lost challenge's mail can reach the accept control (state 1 is shared by "offered" and "lost").
- `0x608C70`'s origin (park start vs calendar zero) for Min/MaxDaysTilTrigger.
- The serializer's 4- vs 8-byte time fields (§1.2 note) and whether loads truncate times.
- World update frequency (how much game time one pass covers), hence the exact moment within the final day.
- `0x5C1000`–`0x5C2150` functions in the same module region (not part of the challenge classes as far as read).
- The exact `[0xC65B80]` mode values (0 is required for challenges to run).

---

## 10. Port status

OpenTPW ports the system as `ThemeParkIncChallenges` (`source/OpenTPW/Economy/ThemeParkIncChallenges.cs`, settings in `ThemeParkIncChallengeSettings.cs`, the
park's side in `ParkEconomy.ThemeParkIncChallenges.cs`), November 21, 2026. It is separate from Theme Park World's `ParkObjectives`, which stays off in a Theme Park
Inc park (`EconomyRules.Narrow`). The claims it relies on were re-read in the listing: the update `0x5C7930`, `CalcAvailTime` `0x5C36D0` (including the type-17
trigger branch `0x5C3828`), the offer `0x5C8140` and its post `0x5C8320` (always true), the accept `0x5C84C0`, the days-left routine `0x5C54E0` with
`0x7D8E30`, the comparisons `0x7D8ED0`/`0x7D8F30`, the clock `0x6089C0`, the event routine `0x5C8940`, the win and failure tables (`0x5C3F60`, `0x5C3DB0`,
jump tables re-read from the file), the value cases for types 25 and 41 (`0x5C50C6`, `0x5C4F70`), the record constructor's clamp `0x5C22C0`, the follow-up filter
`0x5C75C0`, the won cash and ticket steps `0x5C4250`/`0x74C890`, the lost branch `0x5C47D0`, the career reset `0x74BDC1`..`0x74BDE4` (mission counter 0, firing
line 5, golden tickets 0) and the sale events `0x5E5DE0`/`0x5E60E0`.

**Ported exactly.** The loader (row numbers, first free slot, the follow-up filter, the `GTPrize` clamp); the switch-on gate; the per-update cadence (the system is a
thing of the world pass, so it runs once per park turn, not in a lost park; a headless economy runs it in the turn's thing update); `CalcAvailTime` with the
unsigned day comparisons and the mission gate; the offer (lowest due slot, strict unsigned comparisons, the `DaysBetweenOffers` gap, offers not blocked by unanswered
ones); the accept into grace (state 2) or the timed phase (state 3); the grace end without resetting the values; the final-day decision without the failure test; the
early failure, park-closed loss and early win; the "days left stays negative" arithmetic (the low dword of the signed 64-bit quotient); the event routine
`0x5C8940`; the win and failure tests of every type except 19 and 28; the cash prize through the bank (as a paid fare is credited, `0x5C0760`), the golden tickets
to the career (`+0x1074`), the firing line on a slot's first loss and the firing letter once the line is spent. "Now" is the clock base (the FILETIME of 2000-01-01)
plus floor(turn × 15000 / 4) seconds in 100 ns units, and the park age of the day counts is `ThemeParkIncStaff.GameDay` (`0x608C70`), as the staff port reads it.
The slots, `mNextChallengeDelayTime`, the switch, the firing line and the career's golden tickets are saved with the park (`ParkSaveFile.Data.TpiChallenges`); a
Theme Park Inc save from before the system continues with the system as a new world builds it (it draws no random number).

**Mission counter.** A new career's `+0x105C` is 0 (`0x74BDC1`) and OpenTPW has no missions, so it stays 0. Every Science row except 8 and 22 needs
`MissionNeededToActivate` 2, 3 or 4, so those slots are never available: a consequence of the data, not a gate.

**Measured types.** `ThemeParkIncChallenges.IsPorted`: type 2 (trigger `0x5B9380`, any attraction whose `UsageInfo.SpecialIngredient` is 1; value: the sale events of
`0x5E5DE0`, which `ParkEconomy.BookShopSale` posts for a paid Theme Park Inc sale: types 12, 1–8 and 45 for a shop, 13 for a sideshow, and the negated costs of
`0x5E60E0`), type 25 (trigger `0x5B32F0(6)`, the highest entertainer experience, 0 with no entertainer; value `0x5AFED0`, the lowest truncated staff happiness byte,
100 with no staff) and type 41 (trigger `0x5B2F40`, the placed staff members; value and win `0x634240`, any role's active strike). The staff accessors read
`ParkEconomy.TpiStaff` (kinds 4–8 and 0x14 are its roles).

**Gates (`[EXT:tpi-capability-gates]`).**

- **Unmeasured types are never offered.** `CalcAvailTime` never latches a slot whose type is not measured, so `MaxDaysTilTrigger` cannot make it available
  either. The Science level's unmeasured types and why: 35 (trigger `0x61D670(2)`, researched sideshows: research is refused; value `0x5B69C0` reads the attraction
  satisfaction rings `0x5E6D80`, whose rings B–E are unread), 46 (trigger `+0x14` of an unread object; value `0x5B7D30`, ride repair), 36 (trigger park
  `+0x22830`, unread; value `0x5AF940`, guest `+0x1BC`, whose OpenTPW counterpart is not established), 56 (value `0x5E41D0(0)`, the computed excitement
  `0x403AE4`, unread), 17 and 48 (trigger `0x5C3828`: the target ride's `mBeenActivated` (`+0x12C`, set by the special-ride activation `0x5E8910`) or
  `mHasBeenOpenedAtSomePoint` (`+0x12D`), neither modelled at run time, and the record `+0x5C` test is only a candidate reading; type 17's ride-use sender
  `0x61A85B` and type 48's satisfaction value are not wired). All other types (the Arabian and Water levels' included) are unmeasured as well; types 19 and 28 also
  have unported win tests.
- **Shares.** The won branch's `0x621CE0(SharePrize)` and `0x621D30(SharePriceEffectIfWon)` and the first loss's `0x621D30(-SharePriceEffectIfLost)` are left
  off: Theme Park Inc's share system is not ported.
- **Promotions.** `PromoGiven` (message `0x25` → `0x74C450`: the rank and the firing line of `FiringLineForRank`) is left off: promotions are not ported, so the
  firing line only falls.
- **Level record.** The won branch's `+0xF0`/`+0xF4` of the career's level record (`0x5C4514`–`0x5C45FC`) are left off with the missions.
- **Type 42.** Its shop closing and reopening (`0x650800`, `0x5C73D0`) are not reached: type 42 is unmeasured.

**Not ported (presentation).** The sounds `0xC6` and `0xAF`, the advisor's sender advice (`0x718990`, `0x5C2150`), the challenge mails (message `0x1B` kinds 0–3,
the text builders `0x5C5990` etc., which assemble `CHALLENGE_SPECIFIC.str` entries `3 × ScriptIndex − 3` onward with `CHALLENGE_GENERIC.str` pieces) and message
`0x26`. The experimental HUD (`ThemeParkIncHud.Challenges.cs`, `[EXT:tpi-text-hud]`) lists the slots that left state 0 by their `CHALLENGE_TITLES.str` title
(`0x5C63E0`: table `[0xA0D65C]`, `mChallengeTitles`, at the slot's `ScriptIndex`), shows days left and the progress value, notes offers, wins and losses, and
accepts an offered row only in state 1 with the park open (`0x52C2B2`, `0x52C2D9`). A lost slot is in state 1 like an offered one, so its row can be accepted
again; whether the original's lost mail reaches the accept control is unread (§9).

**Open.** Missions (the mission counter), promotions and the shares; the unmeasured types' accessors; the challenge mail text; type 11's admission event
(`0x5AEF40`, delivered through the message queue) and the type-16 and guard senders; the Science prebuilt save's own challenge records (`CHHQ`, which the slice
does not restore, as it restores none of the prebuilt's economy).
