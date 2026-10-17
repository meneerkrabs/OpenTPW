# SAVE: original park save inventory and SAVE-I import specification

Evidence date: October 10, 2026. Static analysis of the Mac PowerPC build
(`SimThemePark.data`, SHA-256 `04809cd4…95f5`) and byte-level checks of the one
identified PC save fixture, `Data/levels/jungle/Easymode.TPWI` (container
`6d89303d…b39a`, payload `a3c9a282…9173`). No original executable was run and
nothing was written to an original file. Witness:
`tools/ppc-analysis/lanes/save/save_inventory.py` (tests in
`test_save_inventory.py`, run by `run_evidence_checks.py`).

This document builds on, and does not repeat, [SAVE-CONTAINER.md](../SAVE-CONTAINER.md)
(container), [TPWS-PAYLOAD.md](../TPWS-PAYLOAD.md) (cell grid, placed objects,
attraction records), [PPC-formats.md](PPC-formats.md#tpwstpwi-save-payload)
(trailing delimiters, labelled world/cell schemas), [PPC-economy.md](PPC-economy.md)
(bank, calendar, staff/guest widths, researcher), [PPC-clock.md](PPC-clock.md)
(clock, cadence and RSSE script state), [PPC-rides.md](PPC-rides.md) and
`lanes/rides/save_evidence.py` (coasters), [PPC-advisor.md](PPC-advisor.md) and
[DET-plan.md](DET-plan.md) (`mGameTick`, `mRandomSeed`).

## 1. What this adds

- **Writer/reader pairing for all 17 delimited blocks** is pinned in one witness:
  writer `0x11cb60` calls the subsystem writer, then builds the delimiter with
  `lis/addi` and writes it byte-swapped; loader `0x11b5ac` calls the matching
  reader, reads four bytes and compares with `addis/cmplwi`. A mismatch reports
  "The SAD_<module> module did not load the same number of bytes that it saved"
  and aborts. Delimiters are **trailing length sentinels**, not chunk headers.
- **The World thing list is fully enumerated.** Writer `0x105d3c` and reader
  `0x106708` dispatch on a u32 thing model through two 20-entry jump tables
  (data `0x44924` / `0x44974`, bound `cmplwi 19`). Every model case calls **one
  bidirectional serializer** with `r5 = 1` (write) or `0` (read) and `r6 = 1` on
  both sides. Model names follow from the debug labels passed to the save
  helpers and from the world variables that reference each thing.
- **Byte-level closure on the PC fixture.** Starting at the map end (1,385,521)
  the chain `Used Thing Head` → `(next ID, model, body)…` walks 42 things and
  ends with next ID 0 **exactly** at the `WRLD` delimiter (1,495,462). Every body
  size below is therefore consistent with the real file; changing any one of
  them by a byte breaks closure (fixture test).
- **ADVISOR-022 evidence:** the original park save *does* persist the advisor
  controller (model 11): 8 pending 21-byte records from controller `+20`
  (stride 24, serializer `0xbc10`), 351 16-byte history entries from `+224`, the
  current record at `+5840`, `mRequestedLastTutorial` and `mMessagePlaying`.
- **PC/Mac fixture equivalence:** the Mac disc image (`hfs.img`) contains one
  contiguous, byte-identical copy of the PC `Easymode.TPWI` (offset 214,763,008;
  CLI `--mac-image`, not a test, because the runner does not provide the image).

## 2. File layout

Container (SAVE-CONTAINER.md) → one zlib payload → 19 blocks in writer order:

```
[ActionRecording][World]WRLD[Scripts]SPSC[Particles]PART[MessageCentre]MESS
[Clock]CLOK[VanillaTime]VANT[GameSystem]GSYS[RideSystem]RSYS[TrackRides]TRAK
[FlyingRides]FLYR[RSSE]RSSE[Camera]KAME[Coasters]COAS[Advisor]ADVS[Sound]SOUN
[Cheat]CHTS[AdvisorScoring]ADSC[UI]
```

Tags are shown as constants; the file holds them byte-reversed (`DLRW`, `CSPS`…).
The writer has an early exit after the action recording when its mode argument
is 1 (`0x11cc00`); its use (published/online parks) is not traced.

**Correction note:** earlier code read each tag as the *start* of the following
data. The bytes before a tag belong to it: the RideSystem block sits before the
`RSYS` delimiter (cheap sanity probes had mislabelled it "SYSG records"), the
`SYSR` build stamp is **TrackRides**, `KOLC`'s word is **VanillaTime**, `RYLF`
holds **RSSE**, and so on (PPC-formats.md already says this). The reader rename
has since happened in code: the former "SYSG" reader is now
`SaveRideSystemBlock` (see block 8), `SavePayloadLayout` walks tags backwards
from the delimiter.

## 3. Block inventory

Writer/reader addresses are call targets witnessed at the listed top-level call
sites. "Easymode" is the block size in the PC fixture. Coverage: **R** read by
OpenTPW production code, **W** witnessed by an evidence lane only, **—** opaque.

| # | Block (delimiter) | Writer / reader | Easymode bytes | Content | Coverage |
| --- | --- | --- | --- | --- | --- |
| 0 | ActionRecording (none) | `0x10bc1c` / same, mode arg | 1,179 | `u32 mLoadedPublishedPark`, `u32 size`, recording (1,171) | W (formats, economy) |
| 1 | World (`WRLD`) | `0x105d3c` / `0x106708` | 1,494,283 | world vars (64), control manager, macro AI (staff pool, calendar, arrivals), map (16,384 cells), thing list | grid **R**, things partly **R** (signature scan), vars **W** |
| 2 | Scripts (`SPSC`) | `0xb7f54` / `0xb83e4` | 5,452 | sprite-script state, starts `TPCS` | — |
| 3 | Particles (`PART`) | `0x9c2c0` / `0x9cc30` | 76,252 | particle systems, starts `LCTP`, named emitters | — |
| 4 | MessageCentre (`MESS`) | `0x116a20` / `0x116ad8` | 266 | message queue; both top-level paths first allocate the 29 × 16-byte message centre if it is absent | — |
| 5 | Clock (`CLOK`) | `0x10e944` / `0x10e998` | 8 | adjusted scaled + unscaled clock words | W (clock) |
| 6 | VanillaTime (`VANT`) | `0x127ac8` / `0x127b64` | 4 | raw `LbTime_GetClock` at save | W (clock) |
| 7 | GameSystem (`GSYS`) | `0x1c2e00` / `0x1c31f4` | 36 | outer cadence stamps and phase | W (clock) |
| 8 | RideSystem (`RSYS`) | `0x5ae70` / `0x5b898` (PC: `0x464140` / `0x4647a0`) | 17,526 | scene-mesh instance records (kinds 815/826/865); framed layout decoded, see TPWS-PAYLOAD.md | **R** (`SaveRideSystemBlock`, formerly the signature-scanned "SYSG" reader) |
| 9 | TrackRides (`TRAK`) | `0x1a424` / `0x1b74c` | 44 | chunked (`{id,size,…}`); PC stamp `20:25:29Oct 21 1999`, Mac writer stamp `Nov  6 2000`; reader seeks past unread chunk bytes | W (formats) |
| 10 | FlyingRides (`FLYR`) | `0x674bc` / `0x67c1c` | 452 | starts `FLY_` | — |
| 11 | RSSE (`RSSE`) | `0xb3868` / `0xb4818` | 10,856 | RSE script manager, 14 × 244-byte scripts, IDs, deadlines | W (clock) |
| 12 | Camera (`KAME`) | `0x2cb90` / `0x2ceac` | 40 | 3×4 + 12 + 12 + 4 | W (formats) |
| 13 | Coasters (`COAS`) | `0x394b8` / `0x39f98` | 16 | count + 3 globals; per coaster 32-byte header, 34-byte sections, links, trains | W (rides; empty only) |
| 14 | Advisor (`ADVS`) | `0x78c8` / `0x7c7c` | 372 | advisor presentation state, starts `ADV_` (log label says "Coasters") | — |
| 15 | Sound (`SOUN`) | `0xbbad0` / `0xbbd1c` | 1,445 | `cat_*` sound/speech/music paths | — |
| 16 | Cheat (`CHTS`) | `0x10e5a8`+`0x10e788` / `0x10e5a8`+`0x10e6d4` | 2 | cheat flags | — |
| 17 | AdvisorScoring (`ADSC`) | `0xe834` / `0xe7c0` (object via `0xcfd4`) | 4 | advisor scoring system | — |
| 18 | UI (none) | `0x18f738` / `0x18f704` | 4 | final block, zero in Easymode | — |

### World thing models

Header per thing: `u32 next used ID` (helper `0xcd1b0`), `u32 model` (`0x10a5b8`);
the current ID is the previous record's next ID (first: `Used Thing Head`).
Body bytes are what follows the 8-byte header.

| Model | Name (evidence) | Serializer | Reader alloc | Body bytes | Components | Easymode |
| --- | --- | --- | --- | --- | --- | --- |
| 0, 2 | — | rejected ("Unknown thing model number loaded") | | | | 0 |
| 1 | Guest | `0xe7f34` | 556 | 525 | person 390 + guest 135 | 13 |
| 3 | Attraction (rides, shops, sideshows, features, gates, lights, bus) | `0xdaf04` | 1,096 | 371 + 4 × Σhistory | TPWS-PAYLOAD.md table | 14 |
| 4 | Mechanic (`mFirstMechanic`) | `0xd9674` | 536 | 503 | person + staff 105 + repair 4, object 2, next 2 | 1 |
| 5 | Handyman (`mFirstHandyman`) | `0xd57ac` | 536 | 505 | … + litter cell 2, start 4, toilet 2, next 2 | 1 |
| 6 | Entertainer (`mFirstEntertainer`) | `0xd28e0` | 532 | 501 | … + start 4, next 2 | 1 |
| 7 | Guard (`mFirstGuard`) | `0xd483c` | 532 | 503 | … + perp 2, prosecution 4, next 2 | 1 |
| 8 | Researcher (`mFirstResearcher`) | `0xf00f0` | 532 | 501 | … + start 4, next 2 | 1 |
| 9 | StaffHQ (`mStaffHQ`) | `0xf7c34` | 104 | 95 | base 8, force strike 4, picked up 2, 5 × 12 strike levels, 5 × 4 budgets, trained 1 | 1 |
| 10 | MechanicHQ (`mMechanicHQ`) | `0xda068` | 14 | 10 | base 8, `mNextObject` 2 | 1 |
| 11 | AdvisorController | `0x82e0` | 5,872 | 5,838 | base 8, time sig 4, sound 4, 8 × 21 pending, 3 × 4, 351 × 16 history, 21 current, 1, 4 | 1 |
| 12 | TagSystem (`mTagSystem`) | `0xf9108` | 2,062 | 8 | base only | 1 |
| 13 | ParkAnalyser (`mParkAnalyser`) | `0xc0230` | computed | 73,536 | grid/regions/6 histories/statistics; **closure-derived only** | 1 |
| 14 | ResearchLab (`mResearchLab`) | `0xf0930` | 5,228 | 4,182 | base 8, 256 × 16 projects, 5 × 13 groups, 4+4+4+1 | 1 |
| 15 | Weather (`mWeather`) | `0x102b1c` | 104 | 91 | base 8, quality 4, 2 × 8 times, forecast 13, drops/lightning/wind … | 1 |
| 16 | Bank (`mBankAccount`) | `0xcba54` | 296 | 292 | base 8, 7 × 4 bank words, 8 × 32 loan offers | 1 |
| 17 | UIMsgReceiver (`mUIMsgReceiver`) | `0xfc7dc` | 12 | 8 | base only | 1 |
| 18 | online person | rejected ("Should not be able to save online persons") | | | | 0 |
| 19 | ChallengeSystem | `0xcf42c` | 996 | 929 | base 8, 20 × 45 challenge slots, 4, 8, 1+1+1, 4, 2 | 1 |

`base 8` = `mX, mY, mMapChild, mMapParent` (`0xfa808`; the "two opaque u16" of
the attraction table are `mMapChild`/`mMapParent`). Person 390 = base 8 +
sprite animation 12 + scalars 49 + navigation 177 + thoughts 144 (PPC-economy.md).
Staff 105 = grade, happiness, jobs, 33 UTF-16 name units, patrol region, grade
percentage, rest area, state, idle tick, hired timestamp (8), energy. Gates (11)
and traffic lights (12) are attractions; `mFirstObject` (15) is the first one.

## 4. Priority fields for M4/M5

| Need | Where | Fields (wire order, widths) | Status |
| --- | --- | --- | --- |
| Ride/shop run state, wear, upgrades, prices | model 3 | `mState` 4, `mCanLoad` 4, queue `mBackOfQueue/mFirstInQ` 2, operating capacity/duration/speed 1/1/4, `mCostOfGoods`, `mQualityOfGoods`, `mChanceOfWinning`, `mPricePerUse` (0–500), gauges (repair, life, third) as whole-number floats, `mTotalCosts`, `mTotalTakings`, `mUpgradeLevel` 1, six u32 histories | Layout proven; values only for a fresh park (all gauges 100) |
| Guests | model 1 | person block (position, navigation path, thoughts), `mCash` 4, `mState`/`mSavedState` 4, `mQNext`/`mQPrev` 2 and `mQueuePos` 1 (queue membership), `mMajorDest` 2, 4 previous-ride pairs, `mPersonType` 1, eight unlabeled `pv` floats (needs/mood; semantics not mapped) | Layout proven by closure; `pv` meanings unknown |
| Staff | models 4–8, 9 | grade, happiness/energy (byte-quantized floats), name, patrol region, state, hired timestamp, role suffix; HQ strike levels and 5 training budgets | Layout proven; researcher values decoded (PPC-economy.md) |
| Economy, loans, research | 16, 14 | bank words + 8 loan offers; 256 project records (`mObjId`, `mCat`, group, upgrade level, cost, done) and 5 group records (effort, output, current project, highest group) | Bank/loans decoded; research layout proven, values not mapped |
| Calendar, tick, RNG | world vars, macro AI | `mGameTick` u32, `mRandomSeed` u32, calendar 8,8,4,4,4 | Decoded (DET-plan, PPC-economy) |
| Advisor controller | model 11 | pending records `mThingId 2, mSecondary 4, mMessage 4, mScore 4, mValid 1, mResponseNo 4, mOverrideOnlyOnce 1, mScoreOverridden 1` | Layout proven; Easymode: 3 initialized records (messages 188, 346, 128; only the third valid) and 5 filled with `0xCD` |
| Coasters/tracks | blocks 9, 13 | see rides lane | Empty fixture only |
| Cell map | World map | 84/94-byte cells, `mType` 1 path / 2 water, tile data, neighbours, parent | Decoded (formats); queue cell type values unverified |
| Challenges | model 19 | 20 slots × 45 bytes, current challenge, next event time (8), switches, value, actual thing | Decoded (economy) |
| Keys, golden tickets | not in the park save | `gms.dat` profile (PPC-scenarios.md) | Out of scope here |

## 5. Versioning

- Container: version byte 133, offline flag 0 (SAVE-CONTAINER.md).
- World: labelled `version` u32 = 2 in Easymode, first world field.
- Thing serializers: a version argument `r6 = 1` is passed as a **constant** by
  both writer and reader; the serializers branch on it (`cmpwi r6,2`, `cmpwi r6,0`)
  but no per-thing version is stored. A different build that changed a body
  layout would not be detectable from the thing records.
- TrackRides: its own chunk framing; the reader skips unread chunk bytes
  ("Close Chunk %d bytes unread") — the only tolerant block.
- No other block carries a version; every other reader is fixed-width or
  count-driven, and the trailing delimiter is the only consistency check.

## 6. PC vs Mac equivalence

| Evidence | Result |
| --- | --- |
| Byte order | Every Mac helper byte-swaps before `LbFile_Write` and after `LbFile_Read`: the wire is little-endian, as on PC |
| Shipped save | Mac `hfs.img` holds one contiguous copy byte-identical to the PC Easymode container |
| Layout | The PC fixture parses completely with Mac-derived widths (block split, cell grid, 42-thing closure, bank, calendar) |
| Differences | Mac TrackRides writes its own build stamp (`Nov  6 2000`); runtime-only pointer words, `0xCD` fill and clock words are host-specific |
| Not established | A Mac-written save; any PC executable trace; whether the Mac reader accepts a PC-written TrackRides stamp at load (the chunk reader suggests tolerance, not proven) |

## 7. Proved vs unknown

**Proved (static + fixture):** block order and writer/reader pairs; delimiter
constants and loader check; the 20-entry thing dispatch (17 serializers, models
0/2/18 rejected); bidirectional serializers with mode/version arguments; reader
allocation sizes; loop bounds (20 challenges, 256 projects, 5 groups, 8 pending
advisor records + 351 history, 5 strike levels/budgets); 4-byte thing headers;
body sizes of 16 models; Easymode chain closure and world-variable ownership.

**Derived, not independently witnessed:** ParkAnalyser body 73,536 (closure
only); guest/person internals beyond PPC-economy's width table; weather internals
beyond the closure.

**Unknown:** semantics of guest `pv` floats and many state enums; contents of
Scripts, Particles, MessageCentre, FlyingRides, Advisor (ADVS), Sound, Cheat,
AdvisorScoring and UI blocks; control manager / macro-AI widths between world
vars (end 1,243) and the map (cell grid from 6,765); non-empty coasters; any save
with guests in queues, broken rides, upgrades, active loans or research progress;
LAYS/online saves; TPI saves (different container, see THEME-PARK-INC.md).

## 8. SAVE-I specification (read-only import)

### 8.1 Parse (no partial results)

1. `SaveReader` decodes the container (existing).
2. Split the payload by the 17 delimiters **in writer order**; each must occur
   once and after the previous one (replaces the leading-tag reading of
   `SavePayloadLayout`). Report block sizes; reject a missing/duplicate tag.
3. ActionRecording: `u32 published`, `u32 size`, skip `size` bytes.
4. World vars: 26 labelled fields from the action-record end (formats lane schema).
5. Control manager + macro AI: parse candidate pool, calendar and arrivals once
   their widths are pinned; until then locate the map by the existing structural
   cell-grid scan and record the gap as opaque.
6. Map: 16,384 cells with status-driven sub-records (existing `SaveCellGrid`
   logic, but field names from PPC-formats).
7. Thing chain: walk `(next, model, body)` from the map end; dispatch on the
   model table above; **require closure at the `WRLD` delimiter**. Replace the
   signature scans in `SaveAttractionList` and `SaveEconomyRecords` with chain
   lookups (attractions = model 3, bank = 16, challenges = 19).
8. Remaining blocks: keep as raw, size-checked byte spans until decoded.

### 8.2 Map into OpenTPW runtime (`ParkSaveFile.Data` v2 and world state)

Import order follows dependencies, not file order:

| Step | Original source | OpenTPW target | Mapping status |
| --- | --- | --- | --- |
| 1 | level/theme from the save path + `base.map` cross-check | `Theme`, `Easy`, terrain | existing (`OriginalPark.Load`) |
| 2 | cell grid path/queue/water types, parents | `ParkCellMap`, path network | path cells exist; queue cell types **APPROX** until a save with queues |
| 3 | RideSystem mesh records + model 3 | `Objects[]` (`InfoId`, cell, rotation, `Price`, `CostOfGoods`, `ChanceOfLosingPercent`, `Level`, `StateOfRepair`/`Repair`/`LifeGauge`, `TotalSpent`, statistics) | placement exists; state fields map 1:1 except gauge scale (original whole numbers 0–255 vs OpenTPW) and `IsOpen`/`IsBrokenDown` from `mState` — **APPROX** until state enums are traced |
| 4 | model 16 | `Ledger.Balance`, `EntranceFee`, `ProfitThisYear`, `Loans`, `TakenLoanOffers`, `MonthsInRed` from `mTurnEnteredRed` | bank words decoded; ledger history/categories have no original source → **APPROX** (start empty) |
| 5 | models 4–9 | `Staff.Members` (type, grade, happiness, state, hired tick, assignment), `TrainingBudgets` | widths proven; name index vs inline UTF-16 name, state enum, assignment IDs → **APPROX** |
| 6 | macro-AI candidate pool | `Staff.Candidates` | widths not pinned → **APPROX** |
| 7 | model 14 | `Research.Completed/Progress/Effort` | layout proven, value mapping open → **APPROX** |
| 8 | model 19 | `Objectives` (current challenge, next offer time, declines) | slots decoded; next-offer time unit and decline mapping open |
| 9 | `mGameTick`, calendar, `mRandomSeed` | `Tick`, calendar, `RandomState` | DET-005: only the seed transfer is a qualified checkpoint; the other streams are reconstructed |
| 10 | model 1 | guests | restored into the guest simulation (`GuestSimulation.ImportOriginal`), whose own save target is the resume snapshot; queued, boarding, riding and held guests stay **APPROX** (SAVE-002) |
| 11 | model 11 | advisor pending/history | target exists in `AdvisorScoreQueue`; import is the ADVISOR-022 fix; `0xCD` records must be treated as invalid, not as messages |
| 12 | Scripts, RSSE, Particles, Clock words, Sound, Camera | — | not importable; recreate from object scripts at load (**APPROX**, matches the original reader's own post-load reconstruction only where traced) |

Every unmapped value must be reported, never silently defaulted, so a later
reader can tell "original value dropped" from "original value absent".

### 8.3 Write-back

Do **not** write original saves yet. Required before any writer:

1. **Round-trip evidence:** a Mac or PC original must load a save written by
   OpenTPW, and an original-written save of a known park must re-import to the
   same OpenTPW state. Neither is possible without running an original, which
   this project has not done.
2. **Byte equality is not the criterion.** Easymode contains 18,288 `0xCD`
   bytes (uninitialized memory written raw, e.g. five unused advisor records),
   live pointer-like words and host clock values. A faithful writer can only
   promise *semantic* round-trip (re-import equality), plus exact delimiters,
   block sizes and closure.
3. The opaque blocks (Scripts, Particles, RSSE, Sound, …) would have to be
   regenerated, not copied, because they depend on runtime script/particle state
   OpenTPW does not model the same way; until they are decoded a writer would
   produce saves the original may reject (`SAD_*` size check) or misinterpret.
4. Fixtures needed first: a non-empty park with guests in queues, a broken and an
   upgraded ride, active loans, research progress and a coaster; two saves of one
   park at a known tick distance (wear oracle, ECON-023).

## 9. Running the witness

```
python3 -I tools/ppc-analysis/lanes/save/save_inventory.py $MAC/SimThemePark.data \
  --fixture $TPW/Data/levels/jungle/Easymode.TPWI [--mac-image $MAC/../hfs.img]
python3 -I tools/ppc-analysis/run_evidence_checks.py --mac-bin $MAC \
  --pc-fixture $TPW/Data/levels/jungle/Easymode.TPWI
```

Tests: 7 synthetic (always), 4 native (`OPENTPW_MAC_APP`), 3 fixture
(`OPENTPW_PC_FIXTURE`). Mutation checks: wrong delimiter, writer, reader or
register; wrong loop bound/stride/offset; wrong model serializer, allocation
or call site; any ±1/±record body size and a shifted chain start break the
fixture closure.
