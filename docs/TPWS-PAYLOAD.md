# Decoded TPWI/TPWS payload: block layout

Evidence date: October 10, 2026. Read-only analysis of the decoded BILZ payload
(see SAVE-CONTAINER.md). The payload is split into its 19 writer-order blocks by
the 17 trailing delimiters; the World block is parsed through its labelled
variables, the **per-cell grid** and the **thing list**, which must close exactly
at the World delimiter; the **placed-object records** of the RideSystem block are
walked by their frame (`SaveRideSystemBlock`). The traced block and thing layout is
[reverse/SAVE-plan.md](reverse/SAVE-plan.md). A read-only importer
(`OriginalParkImport`, then `OriginalEconomyImport` for the economy) uses these
parts. This is not save writing. The layout was found on one fixture (Easymode);
the marker and grid readers were later checked on eleven private TPWS/INTS saves
([Second fixture set](#second-fixture-set)), the block split and thing chain were
not (that set was not available to SAVE-I). No original executable was run.

## Fixture inventory

| Location | TPWS/TPWI/INTS/LAYS files |
| --- | --- |
| Loose `Data` tree (all extensions) | 1: `levels/jungle/Easymode.TPWI` |
| `TPWORLD.ISO` (7z listing, 2,783 files) | 1: `Data/levels/jungle/Easymode.TPWI`, byte-identical to the loose copy |
| WAD member names / ISO-wide filename strings | none observed |

No TPWS, INTS or LAYS file exists in the game data. The conclusions below were made on
Easymode alone; eleven saves found later confirm the readers (next section). LAYS remains
untested.

Pinned hashes (SHA-256):

- Container: `6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`
- Decoded payload (1,608,309 bytes): `a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173`
- Action recording + World block (first 1,495,462 bytes): `58521ff5b0804eeea19358ac68cc3fc219babb8bb3672e3687c892c21d75e3e5`
- Original `TP.ICD` used for the static tag table: `df66561b91794368674a35abade40661398cd703697089c781a685a533fe550e`

## Upstream document

The upstream page (`opentpw-docs` `src/formats/tpws-ints-lays.md`, read via the
GitHub contents API) describes only the outer container: magic, copyright text,
a "file type" `00 01 22 19` and a "version" (which are the end of the header block and the stamp `01 22 19 85`, see
[SAVE-CONTAINER.md](SAVE-CONTAINER.md)), the online flag, `BILZ` header and zlib stream.
"INTS" and "LAYS" there are **file kinds** (initial save; `upload.LAYS` online
save), not payload sections. It documents nothing about the decoded payload.

## Second fixture set

Eleven original saves, one park per theme, published on Nexus Mods as a cheat
(all research done, large starting cash; kept private, never committed). Per theme:
the named park save, its `autosave.TPWS` (absent for space) and `restart.INTS`;
7 TPWS and 4 INTS. The parks are empty: no placed objects, only the entrance path.
`OriginalSaveCorpusTests` runs every reader below over them (`OPENTPW_TPWS_SAVES`) and requires a
TPWS and an INTS for each theme.

| Check | Result on all 11 |
| --- | --- |
| Container | Magic 500, stamp `01 22 19 85`, language byte 0 to 6 (the corpus is checked for magic and stamp); INTS uses the same container as TPWS |
| Markers | All 17, in the Easymode order. Decoded payload 1,560,526–1,579,807 bytes |
| Untagged prefix | 1,469,256 bytes (every INTS) or 1,473,869–1,475,452 (TPWS), against 1,495,462 in Easymode: the prefix length is not fixed |
| Cell grid | Found once, starting at 5,729 (INTS) or 6,259–6,512 (TPWS); 16,384 × 84 bytes, no extension records; +46 equals the theme's `base.map` |
| Path cells | The 10 MAP `InitialPath` cells, nothing else |
| SYSG | No placed objects; fixed items 5 (TPWS) or 2 (INTS), all with their theme's Info.Ids (*600–*604) |
| Attraction records | 5 in each TPWS, one per fixed item (Bus, Seaplane, Ferry, Traffic Lights and the *601 park object); an INTS holds two attraction things (Gates, Lights), which decode once the initial calendar and empty history ring are accepted (docs/SAVE-THINGS.md, "The restart saves"); all three gauges are 100 in all 35 records |
| Economy | Bank prefix, 8 loan offers equal to the theme's `LoanInfo`, and the full challenge list of `ChallengesInThisLevel` (8–10 records); see docs/ECONOMY.md for the corrected challenge layout |

These checks first ran with the earlier leading-tag marker reader ("prefix" = action
recording + World block, "SYSG" = RideSystem block). `OriginalSaveCorpusTests` now runs
the writer-order block split, the thing-chain walk and the park and economy imports on
all eleven saves, the four restart saves included.

Initial state of the INTS saves (an unplayed park): the macro-AI calendar is the constructor's (`0x4F7E80`: start 2000-01-01, month cache 0,
day cache -1, rate 15000, park turn 0) and every attraction history ring is the empty ring of `0x51ADE0` (current entry -1, 30 entries, no wrap).
The loaders (`0x4F7F30`, `0x4E2C10`) take both unchecked; `SaveWorld.Parse` and `SaveAttraction.Read` accept exactly these two states besides the played ones.
OpenTPW's Restart Park reloads its own snapshot of the same moment, not these files (SAVE-012).

The balances (about 1.2 billion) come from the cheat, so money is not a reference value.
Named saves and their autosaves decode to the same layout; only small sections differ.

## Block split (`SavePayloadLayout`)

The original writer (`0x11cb60`) writes each subsystem block and then a 4-byte
delimiter; the loader (`0x11b5ac`) reads the block and aborts with "The
SAD_&lt;module&gt; module did not load the same number of bytes that it saved" unless
the next four bytes are that delimiter. The delimiters are **trailing length
sentinels**: the bytes *before* a tag belong to it. The file stores each constant
byte-reversed (`WRLD` as `DLRW`).

```
[ActionRecording][World]WRLD[Scripts]SPSC[Particles]PART[MessageCentre]MESS
[Clock]CLOK[VanillaTime]VANT[GameSystem]GSYS[RideSystem]RSYS[TrackRides]TRAK
[FlyingRides]FLYR[RSSE]RSSE[Camera]KAME[Coasters]COAS[Advisor]ADVS[Sound]SOUN
[Cheat]CHTS[AdvisorScoring]ADSC[UI]
```

Easymode block sizes: ActionRecording 1,179 (`u32 mLoadedPublishedPark` 0,
`u32 size` 1,171, recording), World 1,494,283, Scripts 5,452, Particles 76,252,
MessageCentre 266, Clock 8, VanillaTime 4, GameSystem 36, RideSystem 17,526,
TrackRides 44, FlyingRides 452, RSSE 10,856, Camera 40, Coasters 16, Advisor 372,
Sound 1,445, Cheat 2, AdvisorScoring 4, UI 4 (zero).

**Correction of the earlier leading-tag reading.** Earlier versions of this page
and of `SavePayloadLayout` read each tag as the *start* of the following data and
named the bytes after it. Those bytes belong to the next block: the "SYSG
records" are the RideSystem block (before `RSYS`), the "`SYSR` build stamp" is
the TrackRides block, `KOLC`'s word is VanillaTime, `RYLF` holds RSSE, `SAOC`'s
`ADV_` data is the Advisor block, and so on. The static table of 17 values in
`TP.ICD` is the same set of constants.

`SavePayloadLayout.Parse` (span or `SaveReader`, payload capped at 64 MiB):

- reads the action-recording header (published flag 0/1, size within the payload);
- splits the payload at the 17 delimiters in writer order. Every block but two ends exactly where its tp.exe
  reader stops (`SaveBlockWidths`; the loader `0x415270` runs each reader and then compares the next four bytes with the
  block's tag, `0x415371` and the following checks), so a delimiter's bytes inside a block are data:
  Scripts `0x475730` (`u32 'SCPT'`, `u32 0x118`, `u32 n`, n slot words and `0x118` bytes for each nonzero slot 1 to n-1; another magic or record size makes the original
  report "Restore scripts FAILED" and fail the whole load, so `SaveBlockWidths` rejects it),
  Particles `0x51f7a0` (`u32 'PCLT'`, `u32 on`, then `0x9e68` and `0x8b60` byte tables and a counted tail; a table size other than these fails the original load and is
  rejected; the pool length `c` of the tail is always 0 in a save the game writes, because the game's save call `0x41661B` writes `c = 0`, but the loader would read `c` bytes),
  message centre `0x40fd60` (a word and 29 counted `u16` lists), Clock 8 (`0x402e30`), VanillaTime 4 (`0x403260`),
  GameSystem 36 (`0x5506e0`), RideSystem `0x4647a0` (`u32` length and that many bytes), TrackRides `0x543560` (a chunk
  whose container header `u32 1, u32, u32 size` carries the width; the close `0x544290` seeks to its start plus the size,
  and the sub-chunk sizes must add up to it), FlyingRides `0x55e020` (`'FLY_'`, an on word, a counted slot table, ten
  words and one block per nonzero slot), RSSE `0x5597a0` (`'RSSE'`, a counted header, five words, a record count and size,
  then per record its struct, eight counted buffers, the folder string and an `'OBJ '` list; see "RSSE block" below), Camera 40 (`0x42cec0`), Advisor `0x599d00`
  (`'ADV_'`, two counted buffers), Sound `0x51c400` (two flags, the counted 84-byte settings and, when sound was initialised, three
  listener floats and the sound library's voice list behind its length word; see "Sound block"), Cheat 2 (`0x405570`), AdvisorScoring 4 (`0x59c860`) and UI `0x481680` (`u32 count`, count x `0x21c`).
  The Jungle Easymode save's seventeen widths all agree with these readers. An empty Coasters block is exactly its
  16-byte count and globals. The World block is the loader's own readers
  (`SaveBlockWidths.TryMeasureWorld`: the 64 variable bytes, the control manager 4,806, the macro AI 670 + 28 + 18, 0x4000
  status-byte cells of 1 + 52 / 31 / 10 bytes by status bits 0 / 1 / 2, and the self-closing thing list) and agrees with the Jungle
  Easymode save to the byte. A Coasters block with a coaster (its trains and cars depend on the coaster type's counts) ends where its reader stops when the caller
  knows the coaster types (`SavePayloadLayout.Parse(payload, coasterTrains)`: the park import's station mesh instance finds the type's `uiMaxTrains` and `uiMaxCars`, `SaveCoasterBlock.TryMeasure`, tag at `0x415371`) and the World
  reads as the original's; without the types, and a World block that does
  not read that way (a synthetic payload), are located by their delimiter's bytes, and the payload must split in exactly one way there; a copy of those delimiters
  that forms a second valid split throws, as does a payload holding one of them more than 256 times, although the
  count-based original loader accepts those cases (SAVE-011). A missing or out-of-order delimiter, a block that does not
  read as its block and a block not followed by its tag throw as well;
- reports the 19 blocks (offset, length, delimiter offset); UI is everything
  after the AdvisorScoring delimiter. No partial result on error.

Blocks other than World and RideSystem stay raw byte spans, measured by `SaveBlockWidths` — except the Coasters
block, which has a bounded structural reader (next section).

## World block (`SaveWorld`)

| Easymode range | Content |
| --- | --- |
| 1,179–1,242 | 64 bytes of labelled World variables (`SaveWorldVariables`, writer `0x105d3c`/reader `0x106708`): `version` (2; other versions are rejected), arrival-vehicle sizes, thing IDs of the bank, mechanic HQ, analyser, gates, traffic lights, research lab, staff HQ, tag system, UI receiver, weather and the first staff of each role and first object, `mGameTick` 755, `mParkClosed` 0, `mNumberOfVisitorsToDate`, `mRandomSeed`, `mWorldState` |
| 1,243–6,048 | Control manager (`0xd1a60`): width not pinned, kept opaque |
| 6,049–6,718 | Staff hiring pool (`SaveStaffPool`, tp.exe writer `0x507850`, the macro AI's first part): 32 × 20-byte candidates (role, name index, grade, sub-type, valid, on pointer, time stamp, timeout), five (people in category, stop producing) pairs, the pool's time stamp (722) and the opening-pool flag; 16 valid candidates |
| 6,719–6,746 | Calendar (`0xe3d30`, tp.exe `0x4F7F30`): FunnyTimeStart 8, SessionStart 8, month cache 4, day cache 4, rate 4 (15,000); a calendar not updated since its constructor (`0x4F7E80`, the restart saves) holds month 0, day -1 |
| 6,747–6,764 | Arrivals (`0xcb050`): 18 bytes, raw |
| 6,765–1,385,520 | Per-cell grid: 16,384 records (16,134 × 84 bytes + 250 × 94 bytes) |
| 1,385,521–1,495,461 | `Used Thing Head`, then the thing list: 42 things, ending with next ID 0 exactly at the `WRLD` delimiter |

The calendar and arrivals are the fixed-width suffix immediately before the map,
so they are read relative to the grid start. Every World thing-ID variable must
name a thing of the matching model (bank → model 16, gates and first object →
model 3, …), or the save is rejected.

### Thing list (`SaveThingChain`)

Each thing is `u32 next used ID, u32 model`, then its body; the current ID is the
previous record's next ID. The walk dispatches on the 17 serializer models of
SAVE-plan §3 (model 3 is variable: six history buffers carry their own counts;
every other model has one body width; 0, 2, 18 and unknown models are rejected)
and requires the list to end with next ID 0 exactly at the World delimiter.
Repeated or out-of-range IDs, a header or body that crosses the delimiter, and a
list that ends before or after it are rejected. Easymode: 13 guests, 14
attractions, five staff (one per role), and one each of staff HQ, mechanic HQ,
advisor controller, tag system, park analyser, research lab, weather, bank, UI
message receiver and challenge system.

Typed readers (`SaveThingRecords.cs`): `SaveAttraction` (model 3), `SaveStaff`
(4–8: base, the person block, the 105-byte staff suffix, role suffix and its next
ID), `SaveStaffHq` (9), `SaveAdvisorController` (11: eight 21-byte pending
records, three raw words, 351 history entries, the current record),
`SaveResearchLab` (14, records raw), `SaveBank` (16), `SaveChallengeSystem`
(19) and `SaveGuest` (1, `SaveGuests.cs`). The mechanic HQ (10), tag system (12),
weather (15) and UI receiver (17) are walked by width and not decoded. The park
analyser (13) has bounded recent-research and statistics readers described below;
its remaining state stays opaque.

### Person and staff (`SavePerson`, `SaveStaff`)

The person block (390 bytes) is tp.exe's person serializer `0x4f8b10`: the base thing
(`0x50b090`), the sprite (`0x4d3ee0`: `mSpriteScript`, `mNextAnim`, `mNextServiceInterval`),
`mAccurateDestX`/`Y` (2 + 2, 1/256 cell), `mAdjustorSpeed`, `mBaseSpeed` (2 + 2), `mCount` (1),
`mESPSprite` (4, the sprite category: 2 is a costume), `mLastRecordedMapId` (2), the 177-byte
navigation state (`0x50d150`, decoded, see "Navigation state" below), `mPreviousSpeed`, `mPreviousX`, `mPreviousY`,
`mStrandedTime` (4 each), `mPurposeSpeed` (2), `mSetDestSuccessfully`, `mSpriteAngle`, `mSpriteID`,
`mSpriteUnderRideCtrl` (4 each), and the thought block (`0x50bad0`: `mActionHistIndex`, 32 ×
`mEventHistory[i]`, `mLastThought`, `mThoughtScript`, `mTimeBubbleShown`). Once it has read the
person the loader routes it again to its accurate destination (`0x4fa5f0` at `0x4f9407`; a
failure only logs "Couldn't renav to my accurate dest").

Navigation state (177 bytes, reader `0x50D150`, `SaveNavigation`; every value 16.16 fixed point in cells unless noted), in order: force 8 (x, y), formation_pos 8, local_xaxis 8, local_yaxis 8, mass 4,
max_force 4, max_speed 4, mCantReachDest 4 (int), nav_mode 4 (int), path_buffer_count 4 (int), path_count 4 (int), path_finished 1 (byte), path_last_progress 4, path_stuck_buffer 4, path_subpath_dist 4,
path_tail_dist 4, path_target_pos 8, path_timestamp 4 (int), path_total_count 4 (int), path_total_dist 4, pos 8, radius 4, five times (subpath_buffer[i] 8, subpath_dist[i] 4) = 60, vel 8. Verified on
every guest and staff member of the Easymode save and the corpus saves: `pos` equals the thing position within 1/256 cell, `radius` is 0.2 and `mass` 1.0; unused sub-path distances hold `0xCDCDCDCD`.
`SavePerson.Navigation` keeps the raw bytes and `SavePerson.NavigationState` decodes them.

The staff suffix (`0x504de0`) holds `mCurrentPayGrade`, the happiness byte, `mJobsDone` (set
to 0 by the constructor and otherwise only serialized), the 33-unit inline `mName`, the
patrol corners `mPatrolRegionBL`/`TR` (cell ids y × 128 + x + 1, the area test `0x506ed0`),
`mPercentageThroughGrade`, `mRestArea` (a thing id), `mState` (the member's own state word
+0x19c), `mTimeStartedIdling` (a park turn), `mTimeHired` (the clock's whole-turn stamp) and
the energy byte. The role suffixes are: mechanic `mDurationOfRepair` 4, `mObjectToRepair` 2;
handyman `mTargetLitterCell` 2, `mTimeStartedCleaning` 4, `mToiletToClean` 2; entertainer
`mTimeStartedEntertaining` 4; guard `mPerp` 2, `mProsecutionTimestamp` 4; researcher
`mTimeStartedResearching` 4; each followed by the role list's `mNext` 2. In Easymode the five
staff patrol (state 1) or idle (the guard, since turn 752), their patrol rectangles are the
whole map (researcher), (47, 18)–(48, 25) (entertainer) and (39, 21)–(56, 28), and they were
hired on turns 15 and 648.

### Guests (`SaveGuest`)

After the person block a guest holds the 135 bytes of tp.exe's guest serializer `0x4fb530`, in
its order: `mArrivalDate`, `mArrivalIndex`, `mBalloonScript` (+0x210), `mBeenAdmitted`, `mCash`
(+0x1a0), `mExitLevel` (+0x1bc), the happiness (+0x19c) and hunger (+0x1a8) bytes, `mLastPosX`/`Y`,
the litter byte (+0x1b4), `mMajorDest` (2, +0x1dc), the ride, shop, sideshow and win counts,
`mPaidAdmission`, `mParkOpeningWaitingTime` (+0x200), `mPersonType` (1), `mPrankeryIndex` (1), four
interleaved `mPreviousRides[i]`/`mPreviousTemporaryRides[i]` pairs (2 + 2), `mQNext`, `mQPrev` (2
each), `mQueueMoveDelay` (4), `mQueuePos` (1), `mRemainingBalloonLife` (4), `mSavedMajorDest` (2),
`mSavedState`, `mState` (4 each), the thirst byte (+0x1a4), `mTimeOfLastSpotAnim` (+0x208),
`mTimeStartedIdling` (+0x1fc), and the bytes of +0x1b8, toilet (+0x1ac) and illness (+0x1b0). The
seven meters are written as their truncated low byte. Easymode's 13 guests are all on their way
in: 5 going to the ticket booth (state 2), 1 paid and waiting for the gate (3) and 7 entering the
park (5), each with 300–750 cash, happiness 50 and one a balloon prankster (102).

Offsets and meanings that tp.exe's code shows (round 5): the guest-object offsets of the fields are `+0x1A0` cash, `+0x1BC` exit level, `+0x1C0` prankery index, `+0x1C4`/`+0x1C8`/`+0x1CC`/`+0x1D0` the
ride, shop, sideshow and win counts, `+0x1D4` arrival date, `+0x1D8` arrival index, `+0x1DC` major destination, `+0x1DE` saved major destination, `+0x1E0`/`+0x1E8` the ride histories, `+0x1F0` person type,
`+0x1F1` queue position, `+0x1F4` queue move delay, `+0x1F8` been admitted, `+0x1FC` idle stamp, `+0x200` opening wait, `+0x204` paid admission, `+0x208` spot animation stamp, `+0x210` balloon script,
`+0x214` balloon life, `+0x218`/`+0x21C` last position, `+0x220` state, `+0x224` saved state, `+0x228`/`+0x22A` queue next and previous. The constructor `0x4FAEC0` zeroes the links, flag, destinations and
last position and stores the constant 6 into both state and saved state (6 in every saved guest).

- `mBeenAdmitted` is the called-forward flag: 1 after `OnAdmittance` (`0x5012B0`, from the ride's LetMeOn; log "Person %d: OnAdmittance (I think I'm on object %d)"), cleared when the guest notices it in its queue update `0x4FFFF0` (state 13); only a state-11 head at position 0 carries it, typically for one turn. The dismissal `0x5012F0` ("dismissed from queue on ride %d") logs event type 6 with the major
  destination, clears the links, the flag, the destination and the queue position, and returns the guest to state 6. `SaveGuest` refuses any other flag value.
- `mQNext`/`mQPrev` are the thing ids of the neighbours in the attraction's queue (0 ends the chain). The unlink `0x4DDD20` rewrites both neighbours (or the ride's head); the needs phase at `0x501971` clears only the guest's own links when its state is outside 11..14.
- `mQueuePos` (+0x1F1) is the recorded queue position (0 = the front, written by `0x501160` before routing), except in state 14: the state-14 entry (`0x501EF8` → `0x4E2670`) stores the sideshow-win draw there, so the byte is the win flag (`SaveGuest.SideshowWon`), and the queue re-evaluation `0x501390` exempts state 14.
- The happiness at queue join (guest +0x20C, a float) is not saved: the thing-list loader zeroes it right after the constructor (`0x51861C`).
- `mSavedState` is the state to resume after a spot animation: `0x4FC800` copies the state into it, stamps `mTimeOfLastSpotAnim` (+0x208) with the world tick and enters state 8 through the state setter
  `0x501DB0`. All 52 call sites of that setter pass at most 0x15, so states are 0 to 21 (`SaveGuest.MaximumState`; the reader refuses more). The setter itself does not guard the range: it stores the state at `0x501DBF` before its `ja` at `0x501DC5`.
- `mLastPosX`/`mLastPosY` are the balloon sprite's previous position: `0x4FE900` moves the balloon script (+0x210) from them to the new point and stores it (0 without a balloon).
- The person's event ring (thought object at person `+0x30`: `mLastThought`, 32 slots from `+0x34`, the bubble script `+0xB4`, the head `mActionHistIndex` `+0xB8`, `mTimeBubbleShown` `+0xBC`): each slot is two
  words, event type then value (the constructor `0x50BA80` clears them as words at +4+4i and +6+4i; `SavePerson.ThoughtEvents`, `SaveThoughtEvent`). `AddEvent(type, value)` (`0x50C100`) writes the slot at
  the head, advances it with `(head + 1) & 0x1F` and skips a pair equal to the previous slot (head − 1, wrapped). Its only callers (25 direct sites in `0x4F`–`0x53`, none elsewhere in the code section) pass types 1 to
  0x19, so the reader refuses a head above 31 or a type above 0x19. The type numbers have no names in the code; the only non-empty ring in the corpus is the fantasy state-20 guest (head 1, slot 0 = type
  0x13, value 0, written at `0x502312`). The ring is not imported (SAVE-002).
- Nothing else is non-default beyond the arrival phase: all 35 corpus guests (Easymode 13, the seven private `.TPWS` 22) are unnumbered (`mArrivalIndex` 0), unqueued, never admitted and
  hold saved state 6; `EveryGuestBodyOfEverySaveIsConsumedExactly` reads every one to the last byte. The queue, admission, balloon-position and ring-wrap fields therefore rest on the code, not on a
  saved guest in a queue, and a save of a park with guests in queues and on rides would test them.
- A refused guest aborts the whole park import: `OriginalParkImport` reads every guest with `SaveGuest.Read` (`OriginalParkImport.cs:283`) and the exception propagates. The refusal bounds (states
  at most 21, admission flag 0 or 1, ring head at most 31, event type at most 0x19) are verified against this tp.exe build only.

### Cell grid (verified structure)

Each record is 84 bytes: type byte at +0 (3 or 7), opaque fields, the **MAP
attribute byte at +46**, and `FF FF` at +82/+83. Type bit 0x04 (value 7) appends a
10-byte extension after `FF FF` (all 250 type-7 records, and only they, are 94
bytes). Records are X-fastest: record `k` is game cell (`k % 128`, `k / 128`).

`SaveCellGrid` takes the grid at the offset the World loader's readers put it, 5,522
bytes after the variables (the control manager 4,806 and the macro AI 670 + 28 + 18, tp.exe `0x5179C0`); a World
that does not read that way has its start located structurally: exactly one offset in the World block
after its variables must begin a run of exactly width × height valid records
(validity = type 3/7 and `FF FF` at +82), within 16 MiB. In Easymode the run
starts at 6,765 and ends exactly where `Used Thing Head` (42) begins. PPC-formats.md
names the record fields (status byte, map/track/region-effect sub-records).

| Field | Interpretation | Evidence |
| --- | --- | --- |
| +46 | MAP attribute byte | Equals `base.map` at all 16,384 cells (and does not match `terrain.map`) |
| +13 bit 0 | Path cell | 78 cells forming a connected network from the gate; includes all 10 MAP `InitialPath` cells; never on a MAP flag other than `InitialPath` |
| +8 | Path connections | For all 78 path cells every cardinal path neighbour has its bit (0x01 −Y, 0x04 +X, 0x10 +Y, 0x40 −X); the 9 other set cardinal bits point at adjacent object/queue cells. The odd bits are the diagonals (0x02 +X−Y, 0x08 +X+Y, 0x20 −X+Y, 0x80 −X−Y) |
| +17 | Path texture | Index into the theme's `PathTex` list; with +21 it follows from +8 on all 78 path cells (docs/PATHS.md, "Path textures") |
| +21 | Path turn | u16, clockwise degrees: 0, 90, 180 or 270 |
| +12 ≠ 0 | Occupied | Nonzero on all 43 footprint cells of the 11 placed objects and on 5 queue/entrance cells of the Bouncy ride, nowhere else |
| +0 bit 2 | Extension present | Record length 94 vs 84 |

Observed but **not interpreted** (kept raw in `SaveCell.Record`):
- +17 holds 55 or 8 on cells that are no path.
- +2 splits the map into 0x40 cells and a 0x00 region around the park (plus 0xC0
  on the entrance, 0x20 on a few cells); possibly purchasable land — unverified.
- Extension byte 6 forms two 11×11 decaying patterns (peaks 23 and 20) centred
  exactly on the two Security Camera objects at (40, 29) and (55, 29); other
  extensions sit on the toilets and around (47, 25). Possibly coverage data.
- Byte +77 alternates 12/25 along lines resembling fences; +56 is a running index
  on 143 cells.

## Attraction records (thing model 3)

Evidence: the Mac PowerPC build's object serializer (`0x100daf04` in `SimThemePark.data`, static
analysis, see [reverse/RIDE-WEAR.md](reverse/RIDE-WEAR.md)) reads every field with `LbFile_Read` and then
byte-swaps it, so the save format is little-endian on both platforms. The Mac disc ships a byte-identical
`Easymode.TPWI` (same container SHA-256), so the Mac loader reads exactly this file. Field names below are
the original serializer's name strings.

Model 3 is the attraction thing. It covers rides, shops, sideshows, features and the fixed gates, lights
and bus. Its body is 1,091 bytes while the history buffers hold 30 entries:

| Bytes | Fields |
| --- | --- |
| 2 + 2 + 4 | base thing (`0xfa808`): position X, Y in 1/256 cell (buildable objects: cell = position / 256, equal to the RideSystem record cell; fixed items store 128, 128), `mMapChild`, `mMapParent` |
| 4 + 2 | `mAngle` (0/90/180/270), `mId` (= Info.Id) |
| 8 × 4 | `tv[t]`: the park time at +0x18 as the fields of FileTimeToSystemTime (tp.exe `0x4DB8EE` → `0x5FC660`): year, month, day, day of the week (0 = Sunday), hour, minute, second, milliseconds. The loader rebuilds +0x18 with SystemTimeToFileTime from all of them but the day of the week (`0x4DC8A1` → `0x5FC5C0`) and checks nothing. The Mac build agrees: its serializer writes the out-words of the imported `TbTimeStamp::GetTime` (eight int pointers, `0x100dafd8`) in order, and its loader passes every word but the fourth to `TbTimeStamp::SetTime` (seven ints, `0x100db7d0`) |
| 4 + 2 | `MeshInstanceID` (110–125 in Easymode), `mFlags` |
| 33 × (2 + 2) | `mNameA[i]`, `mNameB[i]` interleaved: the two UTF-16 sign lines ("Belly"/"Bounce", gates "LOST"/"Kingdom") |
| 4 × 3 | `mRideScriptHandle`, `mTrackRideHandle`, `mState` (+0x19c: 0 working, 1 broken down, 2 upgrade requested, 3 not choosable, 4 condemned; SetState `0x4e0e60` closes the object for 1, 2 and 4) |
| 24 | `mTopLeft`, `mEntryPos`, `mNext`, `mAssignedStaffMember` (+0x5e, the claiming staff thing), `mBackOfQueue` (u16, the cell id y × 128 + x + 1 of the cached back-of-queue cell, 0 when not cached, `0x4DE130`; not a guest), `mCanLoad` (u32, the open word +0x68), `mExitPos`, `mFirstInQ` (u16, the head guest; there is no tail word), `mIsTrackRideValid` (u32), `mUpgradeParent` (u16) |
| 6 history buffers | each `u32` `mCurrentEntry`, `u32` `mNumEntries`, `u8` `mWrappedAround`, `u32` `mTemp` (today's running value), then count × `u32`; `mNumCustomers` (+0x1a0) follows the second buffer and `mNumWalkAways` (+0x1a4) the third. The rings are, in order, the daily costs (+0xf8), takings (+0x70), customers (+0x1a8), walk-aways (+0x230), visits (+0x2b8) and the visits' averaged happiness change (+0x340); the day message 0xb rolls each into its ring (`0x4dd320`) |
| 1 + 1 + 4 + 2 | `mOperatingCapacity`, `mOperatingDuration`, `mOperatingSpeed`, `mPersonBeingLoaded` |
| 6 × 4 | `mCostOfGoods`, `mQualityOfGoods`, `mChanceOfWinning`, `mPricePerUse` (the loader clamps it to 0–500), `mAmountOfSpecialIngredient`, `mQueueSizeInCells` |
| 3 × 4 | three `float32` gauges, read into object offsets +0x48, +0x44, +0x40 on the Mac and +0x4c, +0x48, +0x44 on the PC (`0x4db7d0`): the third gauge, the life gauge and the state of repair of the wear and breakdown code. The original writes each as a whole number 0–255 |
| 4 × 5 + 1 | `mRequestedService`, `mTimeMarkedForMaintenance`, `mTotalCosts`, `mTotalTakings`, `mUpgradeBalloonSprite`, `mUpgradeLevel` (u8) |

`SaveAttraction.Read` parses the body completely and rejects it outside these bounds: non-zero Info.Id;
angle a multiple of 90; a time stamp with year up to 9999, month 1–12, day 1–31, hour 0–23, minute and second
0–59 and milliseconds 0–999 (the day-of-the-week word is kept as read, as the loader skips it); history buffers
of at most 31 entries with flag 0/1; price at most 500; gauges finite in 0–255; upgrade level at most 3. `SaveAttractionList` returns
every model-3 thing of the chain in list order; there is no signature search any more.

Easymode has 14 attraction things, IDs 15, 24–16 and 14–11 (the earlier signature scan reported the
*next* IDs 28, 23–16 and 14–10 as handles). They are the 11 buildable objects and the 3 fixed items of the
RideSystem list, with the same Info.Ids, and every buildable record has its RideSystem cell
(`OriginalParkImport.FindAttraction`). All three gauges are 100, upgrade levels, customers and takings
are 0, and the Belly Bounce stores capacity 5, duration 30 and speed 60. Because this is a freshly
designed park it cannot show a worn or broken ride. Two saves of one park at a known tick distance, with
a ride in use, would turn the state of repair into the oracle for ECON-023.

RideSystem `kind` values 815 and 865 are engine mesh-instance types: the Mac code passes them to the mesh creator
(`0x10059f00`, from `0x100dc350`), and the attraction record links to its mesh through `MeshInstanceID`. The
RideSystem block therefore describes scene meshes rather than game-object state. Kind 826 appears in four Mac
functions (`0x1006e3d4`, `0x1006e630`, `0x1006e8bc`, `0x1006eb84`) that have not been read yet; its records are
separate slots of the block, not nested inside the Belly Bounce record (that reading was a signature-scan
artifact).

## RideSystem placed-object records (`SaveRideSystemBlock`)

The RideSystem block (its bytes lie before the `RSYS` delimiter; earlier called "SYSG") is
framed: PC writer `0x464140`, PC loader `0x4647a0` (the Mac reader is the plan's `0x5b898`).
After the u32 length word — which names the block's remaining bytes — come
u32 `used`, u32 `free` and u32 `first free slot`, then one slot per used + free in slot
order: a presence byte (`1` present, `0` absent), and for a present slot its record.
`SaveRideSystemBlock.Parse` walks the slots exactly: the walk must end on the stored length with the
stored counts, the first absent slot must carry the stored first free slot's ordinal (the free hole the
live instance allocator keeps; with no hole the count equals `used`, as every corpus INTS save
holds), every presence byte must be exactly 0/1, and the node/embedded-node counts, the instance-flag
word and the node-state words stay within the shapes below. The counts are summed as 64-bit values and
bounded (at most 65,535 slots, each at least one byte; the per-record node, embedded-node and control counts have no cap beyond their u16 fields and the block's bytes); a block with no slot at all is exactly its three
zero counters (16 bytes with the length word, first free slot 0, which is what the writer emits). The
original loader is more lenient than this reader (it takes any presence byte other than 1 as an empty
slot, ignores bytes behind an empty frame and never compares its read position with the length); the
reader accepts only what the writer produces. A breaching block refuses the save instead of falling back
to a guess.

One record: a 0x36-byte header, then the variable part.

| Offset | Size | Content |
| --- | --- | --- |
| +0x00 | u32 | Info.Id; matches the `Info.Id` in the object's `.sam` |
| +0x04/+0x08 | u32 | X, Y |
| +0x0C/+0x10 | u32 | width, height in cells |
| +0x14 | u32 | kind (815 placed object, 826 scenery, 865 fixed item) |
| +0x18 | u32 | index (slot 5 is absent in Easymode) |
| +0x1C | u32 | packed instance flags: the writer (`0x4641c4`) maps the live flags 0x20, 0x40, 0x80, 0x100, 0x200, 0x400, 0x800, 0x40000 and 0x8000000 onto bits 0–8 and never stores higher bits (Easymode holds only 0 and 8) |
| +0x20 | u16 | raw (the live instance's +0xc word) |
| +0x22 | u32 | raw (the live instance's +0xb8 word) |
| +0x26 | u32 | rotation in degrees |
| +0x2A/+0x2C | u16 | node-state count / embedded-node count |
| +0x2E/+0x32 | u32 | two embedded words, kept raw |

The variable part holds embeddedCount × 8 bytes of embedded nodes, nodeCount × 4 bytes of packed
node-state words — bits 0–3 and 5–11 map onto the flag constants 0x10, 0x20, 0x8000, 0x10000, (empty),
0x40000, 0x80000, 0x200000, 0x400000, 0x20000000, 0x80000000 and 0x100000 (writer `0x464380`,
loader `0x464af6`; bit 4 and bits 12+ are empty in every corpus save, so the reader refuses them) — and
then `T` animation-time controls (`SaveRideSystemControl`), 0x2C bytes each: 11 u32 words in the save's
word order [0], [1], [2], [4], [5], [6], [3], [9], [10], [11], [12] (PC loader `0x464bef`-`0x464bf4`
copies the seventh record word to control word 3). In save order the words are Flags, AnimID,
SubAnim, StartAnimTime, AnimTime, NoPauseAnimTime, the speed (float, control word 3, the factor of the
frame formula `0x473490`), DeferredAnimID, DeferredSubAnim, DeferredFlags and the deferred speed (float);
the names are those the loader's debug dump `0x464580` prints. The flags are 0x1 loop, 0x2 held on the
first frame (AnimID 13), 0x4 ended (AnimID 14, frozen on the last frame; the loader then sets `0x10`,
sample once, at `0x464bfb`), 0x8 an AnimID-0 clip ended and 0x40 the unscaled clock (`0x472f60`,
`0x4735d0`). AnimID 12 is none (`0x464bb6` logs when it is anything else, and `0x464cbe` sets the
`0x400000` animated-instance flag when it is below 12). The times are milliseconds of the game clock that
the Clock block stores and the loader sets again (`0x4164c0`, `0x415270`); the loader bounds no word, so
the reader keeps them raw.

On import (`ParkObjects.ImportOriginal`, `OriginalObjectRuntime.RestoreSavedScene`) OpenTPW restores
what the loader restores of a record: the node states' bits 0x10 and 0x80000000 when the record's node
and embedded-node counts are the model's nodes and dummy attribute records (`0x464ab4`), and every
control below AnimID 12 on its channel, bound without a trigger (`0x472d10`, `0x472cb0`), with its start
`clock − StartAnimTime` milliseconds back on the saved game clock, so the next channel update
(`0x4735d0`) continues the saved frame; an ended control stays on its last frame. The object's animator
clock starts at 0 at the import, so that start is zero or negative, up to the 32-bit elapsed time back,
and an OpenTPW save of the park keeps it until the clip starts again. Jungle Easymode's gate
saved the open command's clip (5/1) ended on frame 50 of 50, so it stands open; the Bus its leaving clip
(5/2) ended, with its three meshes hidden; the Traffic Lights loop 5/0. Flag 0x2 and 0x40 controls, which
no corpus save holds, are reported and not restored.

The control count `T` is the one thing the block never stores: the loader reads it from the live
instance at +0xe (`0x464b98`), which `0x4557c0` copies with the instance's 0xfc-byte header from the
kind table's type entry (`0x463060` finds the entry by the type index of the record's Info.Id). That
entry is built by `0x4629d0`, which passes its seventh argument to the model builder `0x461f10` as the
count (stored at entry +0xe) and replaces 0 with 1, so `T >= 1`. The object-archive loader
(`0x414175`, `0x4141c7`) passes the definition's field at +0x170, which is the `.sam` key
`UsageInfo.NumSimultAnims` (Jungle Spray declares 3 and has `T=3`; Belly Bounce declares none and has
`T=1`); the other registrars of the table pass literals (`0x41437e` and `0x430130` pass 1, `0x42fcbf` passes
5). OpenTPW builds no live instances on import and the save has neither value, so
`SaveRideSystemBlock.Parse` takes an optional table (`controlCount`: `max(1, UsageInfo.NumSimultAnims)`
by Info.Id, from `ObjectCatalog`); `OriginalPark` supplies it on every import, read lazily from the theme's catalogue. A record whose Info.Id the table resolves has exactly that `T`; every
other record may have any `T` of at least 1 that fits the block (no cap: a cap would hide a second complete parse, so only the work and state budgets can refuse a block), and the walk then considers **every** complete parse: an
iterative search over (slot, read position, used count) with a saturated path count per state, explicit
limits (1,000,000 states and 20,000,000 steps, refused beyond them) and the chosen path rebuilt from
predecessor edges. The block is accepted only when it has exactly one complete parse; none, or more
than one (for instance a frame that parses with `T=1` for a record and `T=3` for it), refuses the
block. The search and the decoder share one record predicate (`TryMeasure`: header fits, instance-flag
word within bits 0–8, bounded counts, node-state words within the writer's bits, tested as a bit mask so
bit 4 is refused).

The eleven corpus saves and Easymode each have exactly one complete parse without any table. The
corpus holds `T=1` for every record except one `T=2` record in each of `space/Space Zone.TPWS` and
`space/restart.INTS` and Easymode's slot 114 (`T=3`); in each park save five records (two in an INTS save)
resolve through the object archives and match the table, and the other Info.Ids (17302, 17303, 17309,
17319, 17320, 17323, 17326; not declared by any object archive) hold `T=1`. That those types are built
with the literal 1 at `0x41437e` is consistent with this but not traced to these ids.

| Index | Info.Id (.sam name) | X, Y | Size | Rot | Kind |
| --- | --- | --- | --- | --- | --- |
| 1 | 1601 Gates | 45, 16 | 6×3 | 0 | 865 |
| 2 | 1603 Lights | 48, 17 | 1×1 | 0 | 865 |
| 3 | 1100 Belly Bounce | 51, 23 | 3×4 | 0 | 815 |
| 4 | 1303 Jungle Spray | 51, 30 | 3×3 | 0 | 815 |
| 6 | 1203 Drinks Shop | 43, 30 | 2×2 | 0 | 815 |
| 7 | 1406 Litter Bin | 44, 29 | 1×1 | 0 | 815 |
| 8, 9 | 1413 Security Camera | 55, 29 / 40, 29 | 1×1 | 0 | 815 |
| 10 | 1411 Staff Room | 58, 16 | 2×2 | 90 | 815 |
| 11–13 | 1402 Small Toilet | 55, 17 / 16 / 15 | 1×1 | 270 | 815 |
| 14 | 1403 Round Fountain | 57, 19 | 3×3 | 90 | 815 |
| 15 | 1600 Bus | 48, 17 | 1×1 | 0 | 865 |

Kind 865 marks the gates, lights and bus, which `Standard.sam` calls fixed items
placed from `FixedItemOrigin` (48, 17); kind 815 everything buildable. Footprints
verified against the grid: rotation 0 covers [X, X+W) × [Y, Y+H); rotation 90 of a
square covers [X, X+W) × (Y−H, Y]; 1×1 any rotation. Non-square rotated footprints
are not observed and stay unresolved. Index 5 is absent. About 150 further
records share the leading layout with kind 826 and index 0: Info.Ids
17302–17323 in 2×2 steps along the lines where cell byte +77 alternates 12/25
(fence-like), and 17002/17003/17005 on the Bouncy queue cells (49–52, 22). Those
Info.Ids are not in the level object archives; the kind filter skips them and
nothing about them is imported.

## Scripts and Particles blocks (`SaveScriptsBlock`, `SaveParticlesBlock`)

Both blocks end exactly where their tp.exe loaders stop (`SaveBlockWidths`, no delimiter search), and both typed readers
consume them to that width in every corpus save (11 saves: jungle, hallow, fantasy and space, each with a `restart.INTS`).
Each reader checks what the loader checks and throws `InvalidDataException` where the original load fails (Scripts: another magic or record size; Particles: a table size other than 0x9E68 / 0x8B60). A wrong Particles magic or `on` = 0 is **not** a failure: the loader returns 0 (`0x51F7E9` jumps to `0x51FA0C`; `on` = 0 at `0x51F80B`), which the caller at `0x41543F` takes as success, after 4 or 8 bytes, and the reader models that as a short block (`MagicMatched`, `On`). Fields
the loader never reads, or overwrites with a process value, are kept raw and are reachable by offset.

**Scripts** (writer `0x475650`, loader `0x475730`; file tag `TPCS`). `u32 'SCPT'`, `u32 0x118`, `u32 n` (100: `0x4752B3`),
`n` slot words (process pointers; only zero or not zero is read, and the loader zeroes slot 0), then the 0x118-byte record of
each nonzero slot 1 to n - 1. The record is a sprite-script VM instance (`0x4758F0` fills it, `0x475010` steps it, `0x475360` runs the due ones).
The program is a word table at `0x74DAB8` shared by every script (the guests' balloon, burst and entertainer-performance steps, GUESTS.md).

| Offset | Field |
| --- | --- |
| +0x00 | not written by any traced code (0) |
| +0x04 | the record's own slot number (checked equal to its slot in the corpus) |
| +0x08 / +0x0C / +0x10 | program counter / entry point / counter of the last instruction run, in words from the program table |
| +0x14 | the program table (`0x74DAB8`; the loader rewrites it after reading) |
| +0x18 | run state (1 to 4 in the VM: 1 stepping, 3 skipped, 4 ended; every corpus record holds 2) |
| +0x1C | 0x14, the capacity of the 20-word area +0x20..+0x6F (read as the stack by position; zero in the corpus) |
| +0x70 | nesting counter of the block-skipping scans `0x4750C0` and `0x475130` (0 between runs) |
| +0x7C / +0x80 | game tick of the next run / ticks between runs (`0x475360`) |
| +0x84..+0xCF | 19 parameter words (`0x475590`, `0x4755D0`); +0x88, +0x8C, +0x90 are the position (three floats, `0x475480`) and +0xC0 its fourth word (0x1200); +0xBC is the sprite handle of the creator `0x540C60` |
| others | raw (+0xA0 is 0xFF, +0xA4 and +0xA8 are 1.0f, +0xD0 and up hold floats and process pointers) |

The corpus holds 0 scripts in the `restart.INTS` saves, 1 in fantasy and 4 in each of hallow, jungle and space (entry points 0x5A, 0x3B and 0x61 seen; the program words behind them are not matched to what the scripts do).

**Particles** (writer `0x51F680`, called at `0x41661E`; loader `0x51F7A0`, 76,252 bytes; file tag `LCTP`). `u32 'PCLT'`, `u32 on`; when on `u32 0x9E68` and the pool table
(the memory at `0x80CED0`), `u32 0x8B60` and the template table (`0x804370`), `u32 c` and `c` bytes of the particle pool. The game's save call writes `c = 0`; the
loader allocates 52 x capacity bytes (32-bit wrapped) and, **if `c` is 0**, rebuilds an empty pool, clears the live count and the free-particle head (`0x51F8FB` `je 0x51F91B`); **if `c` is not 0** it reads `c` bytes into the pool
and skips the rebuild (`0x51F9FC`). A larger `c` overruns the original's heap and a capacity of 0 dereferences a NULL pool at `0x51F954`; both are rejected here. It keeps only two words of the running process (the sine table `+0x1C` and the ground function `+0x24`), so the saved density (`+0x20`) becomes the pool's.

The pool table (0x9E68 bytes):

| Offset | Content |
| --- | --- |
| +0x00 / +0x04 / +0x08 / +0x0C | live particle count (cleared on load) / tick counter / low-detail flag / pool capacity (0x800) |
| +0x10 | the pool's private generator (`state x 214013 + 2531011`) |
| +0x14 | set to 1 by the reset, read by nothing |
| +0x18 / +0x1A | next emitter / effector handle generation (u16) |
| +0x1C / +0x24 / +0x28 | sine table / ground function / pool pointer (process values) |
| +0x20 | density (500, 1000 or 1500: the detail file's PARTICLEDENSITY) |
| +0x2C | 120 emitter slots of 320 bytes (to +0x962C) |
| +0x962C | 20 effector slots of 104 bytes (to +0x9E4C) |
| +0x9E4C | list heads (`0x816D1C`..): active emitter, active effector, free emitter, free effector (i16, -1 none), the free particle head (u16, reset to 0), then 18 raw bytes |

An emitter slot is a copy of its effect template (`0x521E60` copies 0x50 words, so the template offsets of PARTICLES.md are the slot offsets) overwritten by: +0x00 in use (1), +0x06 first
live particle (-1), +0x08 live particles, +0x0A handle generation, +0x0C effect id, +0x10 death effect, +0x14/+0x18/+0x1C position (1/64 unit), +0x20 life, +0x2C velocity, +0x38 base velocity,
+0x44 volume extent, +0x54 spiral angle, +0x60 burst, +0x64 cap, +0x66 rate counter, +0x68..0x6B rates, +0xA2 control, +0xAB followed by parent, +0xB4 child handle, +0xD0/+0xD2 next/previous slot (i16),
+0xD4 initial life, +0x118 name (16 bytes: `WaterFall`, `Button`, ...), +0x128..0x13F bounds. An effector slot (`0x522360`) is a copy of an effector template with +0x00 in use, +0x08 generation, +0x0A followed,
+0x0C position, then the template fields (velocity +0x18, drag, gravity, radius +0x2C, mode +0x30, strength +0x40, death effect +0x46, life +0x4C) and +0x60/+0x62 next/previous; **it stores no template id**.
The template table is 105 emitter templates and 20 effector templates, the game's in-memory copy of `Data/Particle/Tp2.plb`: all 20 effector templates and emitter templates 0 to 100 equal the file
in every corpus save; the last four emitter templates are scratch records the game rewrites at run time (Jungle's templates 101 and 102 differ, a modified "Smoke").

Corpus: `restart.INTS` holds 0 to 1 live emitters (Jungle's is the ambient `WaterFall`, effect 20), the park saves 3 to 18 (`Button` effects and ambient emitters); no corpus save has a live effector; every pool table's
active and free lists partition the 120 emitter slots and the 20 effector slots, and the in-use flags agree with the lists. The original loader checks none of this and no writer was proven to keep it, so a block that breaks it is read and reported in `ListProblems`; only the runtime conversion refuses it.

**Mapping to the runtime.** `ParticleSystemState` (the park-resume part, PARTICLES.md "Saves") describes the same native structure, and `SaveParticleState.ToSystemState` copies the fields it has. The conversion is partial:

- Not carried (no runtime field): the emitter's hold word, group, screen-space flag, ring mode, immortal word, word +0x2A, live particle count, first particle and bounds; the effector's group, mode and strength; the saved template tables. `ParticleSystem.Restore` refills hold, group, screen-space and killed-on-load from the runtime's own Tp2.plb templates, so a save made with modified templates (Jungle's scratch templates 101 to 104) restores with the file's.
  In the corpus every emitter uses effects 0 to 100 and its saved hold, group, screen-space, ring and immortal values equal the file's, so nothing is lost there.
- Inferred: an effector slot stores no template id, so the id is matched on template bytes (group, mode, jitter, strength) that the tick does not write by direct displacement (writes through a base register were not checked); no match or several is refused. No corpus save has a live effector, so this is unexercised on real data.
- Carried pool configuration: density is the signed interpretation of the saved word (including zero); low-detail preserves its zero/nonzero meaning; capacity must be 100..32767, the managed supported initialization domain. `ToSystemState` refuses other counts instead of clamping or allocating them. [Bounded load evidence](reverse/PARTICLE-POOL-SAVE-CONFIGURATION.md).
- Conditional or capped: `VolumeExtent` is null when it equals the runtime template's; `TickCounter` is capped at `int.MaxValue`; a block with `ListProblems` is refused.

The corpus test restores every save's pool into `ParticleSystem`, captures it back and compares every carried field (the post-load pass `0x5200B0` is the only difference, applied to the expectation). A level that starts from an original save restores the pool into its particle system
(`Level.ImportOriginalParticles`): the post-load pass `0x5200B0` hides the screen-space emitters (the saved `Button` sparkles are dead on the first frame) and leaves the
world emitters (Jungle's `WaterFall`). The sprite scripts of the Scripts block belong to the saved people and litter, not to scenery: the corpus' park saves hold exactly one record per saved guest
(four guests waiting at the gate in the Hallow, Jungle and Space saves, one walking guest in Fantasy) whose entry point `0x5A` is program 3 of the person-script table `0x75A418` (`0x4D4140` creates it, the staff drop `0x505EA0` sets it): a loop of three threaded-code instructions that
sets a frame and jumps back; litter (`0x4D93B0`) has its own programs `0x74F050` to `0x74F168` and the cell's `mLitterScript` points at its slot. The records are decoded and not applied (SAVE-013): OpenTPW animates its guests from their state and its litter from the cell.

## RSSE block (`SaveRsseBlock`)

The RSSE block (writer `0x559350`, loader `0x5597A0`; before the `RSSE` delimiter, earlier read as "`RYLF`") is the script system: `'RSSE'`, `u32 0x14` and a 20-byte
header (the globals at `0x8791A0`: an initialised flag, the tick counter, the next script id, the record count and the raw list-head pointer), five `'PAD_'` words
(`0x5F444150`, discarded by the loader), `u32` record count and `u32 0xF4` record size. Each record is the 0xF4-byte script struct, eight length-prefixed buffers, the script
folder and an object list:

| Buffer | Stored length word |
|---|---|
| code | byte length (a whole number of words) |
| stack, variables, string blob, node table | byte length (the stack and variables are whole words; the node table is 4 x the node count) |
| limbo table | byte length (8-byte entries) |
| bounce table | byte length (16-byte entries) |
| walk table | an entry **count** (32-byte entries) |

then the folder as `u32` length and NUL-terminated text (for example `data\levels\jungle\Rides\bouncy\`), and the object list `'OBJ '`, `u32` count, `u32 0x1C`, count x 0x1C bytes
(+8 type, +0x0C runtime handle, +0x10 key, +0x14 model node, +0x18 slot; the first two words are kept as read).

Script struct fields (offsets, named as the loader's log strings and the save/load order give them): +8 script id; +0x0C / +0x10 / +0x14 child, parent and sound script id; +0x18..+0x38 nine buffer
pointers (raw process addresses); +0x3C program counter; +0x40 stack pointer; +0x48 result/flag register; +0x4C node count; +0x50 code word count; +0x54 stack size; +0x58 limbo size;
+0x60 limbo entries in use; +0x64 bounce size; +0x6C `i16` bounce count; +0x6E `i16` bounce base; +0x70 bounce node; +0x74 NAME; +0x7C walk size; +0x80 / +0x84 WALKFLOAT words;
+0x8C variable count; +0x90 string bytes; +0x94 time slice (50); +0x98 yield word; +0xA0 WAIT deadline; +0xA4 / +0xA8 / +0xBC animation-wait words; +0xAC `u16` thing id; +0xB0 object-list head;
+0xB8 TURBO byte; +0xB9 DIPMUSIC byte; +0xC0 `i16` speed; +0xC4 timer; +0xC8 mesh instance; +0xD0 scream; +0xD4 sound emitter handle; +0xD8 reverb; +0xDC / +0xDE SPARK; +0xE0 COAST;
+0xE4 `u16` animation speed; +0xE6 `i16` BUMP. `SaveRsseRecord` names these and keeps the raw struct (the +0x88 word `0x03E803E8` and the other unnamed words stay raw).

Verification on the shipped Jungle `Easymode.TPWI`: the walk consumes exactly 10,856 bytes with 14 records (script ids 15 to 1 without 5; the header's record count is 14 and its next script id 0x10).
Every record's code words and string blob are byte-identical to the matching ride's `.RSE` member in the level archive (the folder gives the archive: `Rides\bouncy\` is
`levels/jungle/rides/bouncy.wad`), and its stack, variable, limbo, bounce and walk sizes (struct and buffers) equal that member's header (the buffers in the units above). Each record's thing id (+0xAC)
is an attraction thing whose mesh instance equals the record's mesh instance (+0xC8). The same holds for the eleven other original saves of the corpus test (four themes, `.TPWS` and the initial `.INTS`
saves: 5 records in each park save and 2 in each `.INTS`, consumed to the byte). The block is read strictly: a wrong magic, header length, record size, object-list tag or object size, a folder that is not one NUL-terminated string, a code buffer that is not whole words
and any truncation are rejected. `SaveBlockWidths` keeps the width of a block without the `'RSSE'` magic at the tag alone (unverified against the loader); the park import then fails on the typed reader.

## Coasters block (`SaveCoasterBlock`)

The Coasters block is the body between the stored `EMAK` (the Camera delimiter `KAME` byte-reversed) and the
stored `SAOC` (`COAS`): the original loader checks the preceding integer `0x4b414d45`, runs the coaster loader
`0x39f98`, then checks `0x434f4153` (docs/reverse/PPC-rides.md, "Original TPW coaster save/load contract").
The saver `0x394b8` writes the same shape. Like every delimiter this one is trailing: the block's bytes lie
before `SAOC`. The reader is read-only; OpenTPW builds no coaster chunk (the writer carries a template's bytes verbatim).

```
u32 coaster count          (linked-list count)
u32, u32, u32              the slot allocator's state: 0x790B04 used slots, 0x790B00 free holes,
                           0x791CE0 next-slot cursor (0, 0, 1 in an empty park)
per coaster:
  32-byte header:
    +0  u32  remapped controller flags (+0x3C bits 0/1, +0 track flags 0x20..0x800)
    +4  u32  controller+8       (state word)
    +8  u16  controller+12      (type id)      +10 u16 controller+16 (station mesh instance id)
    +12 u16  controller+20      (slot)         +14 u16 controller+28 +16 u16 controller+32
    +18 f32  controller+44      (the power factor, 0 or 1)
    +22 u16  section count      (chain nodes excluding descriptor flag 0x10: no station or end node)
    +24 u16  controller+244     (trains in service)   +26 u16 controller+240 (seats per train)
    +28 u32  controller+320     (the self-intersection count, not riders)
  auxiliary block: u32 count, then count × u16 ordinals
    (station node S2's collision list; the loader reads it before any section record,
     the saver writes it where its chain walk meets the node with node+0 == 2)
  per section (the header's count times):
    +0  u32  remapped section+8 flags      +4 u32 section+112
    +8  u16  cell X       +10 u16 cell Y   (the descriptor's +12/+16 grid coordinates)
    +12 u8   cell-stack insertion ordinal
    +13 f32  section+232  (height parameter t)   +17 f32 section+216 (bank)
    +21/+25/+29 f32       section+220 (incline), +224 (yaw), +248 (loop rotation, loop nodes only)
    +33 u8   collision count (narrowed from section+176, at most 8), then that many u16
                          collision ordinals, each its own 2-byte read like the original
  per train slot of the coaster type (type +0x2e8, uiMaxTrains):
    27-byte record: u32 train flags, u8 car count (+0xc), u32 +0x74 (the reference car's position, f32),
                    u32 +0x78 (speed, f32), u32 +0x38, u32 +0x64, u32 +0x68, u16 riders (+0x10)
    per car of the train (type +0x2f4, uiMaxCars):
      11-byte record: u32 car flags, u8 count A (+0x30), u8 count B (+0x34), u8 +0x44, u32 mesh instance (+0xc)
      then count A u32 (riders on the lap) and count B u32 (guests seated at the station)
```

The header fields as tp.exe's loader treats them (`SaveCoaster` names them):

| Header | Controller | Meaning | Restored |
| --- | --- | --- | --- |
| +0 u32 | +0x3C b0/b1, +0 0x20..0x800 | wire 0x1 track complete, 0x2 circuit cut open, 0x4..0x100 the track flags (SAVE-921) | yes |
| +4 u32 | +8 | state word: low nibble 1/2/4/8, mode bits 0x10/0x20/0x40 (0x40 the breakdown, no power); the constructor's value is 0x11 | yes |
| +8 u16 | +0xC | coaster type id (`TypeId`) | no, the constructor set it |
| +0xA u16 | +0x10 | station mesh instance id (`StationMeshInstanceId`), the bind key | used to bind |
| +0xC u16 | +0x14 | slot in the table `0x790BD0` (`Slot`, at most 255), the scripts' coaster handle | as the saved slot, which the allocator assigns as given |
| +0xE u16 | +0x1C | the station's placement cell x from `0x466F90`, dead data | no |
| +0x10 u16 | +0x20 | the station's placement cell z from `0x466F90`, dead data | no |
| +0x12 f32 | +0x2C | power factor 0 or 1 | yes |
| +0x16 u16 | — | section count | — |
| +0x18 u16 | +0xF4 | trains in service (`TrainsInService`; the loop bound of `0x435784`, `0x43BA35`, `0x43CD36`) | yes |
| +0x1A u16 | +0xF0 | seats per train (`SeatsPerTrain`) | yes |
| +0x1C u32 | +0x140 | self-intersection count (`IntersectionCount`; PC writers `0x439C40`, `0x43AD50`, docs/COASTERS.md) | yes |

Of the controller words only `+20` (header `+0xC`) reaches the slot allocator `0x468760`; `+28` and `+32` go to the station-node
builder `0x436AC0`, which never reads them (its only stack argument read is the fifth, at `0x436BFA`).

### The loader `0x437B60`, end to end

The loader binds saved blocks to coasters that already exist; it creates none. Before the Coasters block the World
block restored the station's attraction thing, and the RideSystem loader `0x4647A0` recreated every saved mesh
instance through `0x463060` (saved id plus 1, load flag 1). For a coaster type that calls the controller constructor
`0x4368F0` with the type id, the station mesh id, the two placement values of `0x466F90`, the load flag and one more
word. The constructor allocates the 0x14C-byte controller, pushes it on the list `0x790FE0` and stores the type id
(`+0xC`), the type record (`+4`), the station mesh id (`+0x10`) and the placement values (`+0x1C`, `+0x20`); with the
load flag set it skips the slot, the trains, the seat split and the station nodes. The loader then:

1. reads the count and the three globals straight into the allocator's state (`0x790B04`, `0x790B00`, `0x791CE0`);
   a count of 0 returns at once;
2. per coaster sets the loading flag `0x790AFC = 1`, reads the header and walks `0x790FE0` until `ctl+0x10` equals
   the header's station mesh id (`0x437CBF`). **When no controller matches, the pointer stays 0 and the next
   instruction dereferences it (`0x437CD5`): the original crashes rather than skipping the block**, so the station's
   attraction and its RideSystem mesh record must load first. On a match it allocates the train slots and car arrays
   (`0x436D10` with load flag 1, no car models), builds the three station nodes and the cursor node (`0x436AC0`, from
   the station model's dummies), restores the slot (`0x468760` with the header's `+0xC`: a slot other than -1 is stored as given, with no
   allocation, fallback or change to the allocator's globals; the result goes to `ctl+0x14`, `0x4386B2`), sets `ctl |= 2`
   (`0x4386BA`) and then `ctl |= 0x4000` (`0x437CD8`);
3. reads the auxiliary block into station node S2's collision list (count `+0xB0`, eight slots `+0xB4..`);
4. reads each section: wire 0x8 runs the loop-mode setup `0x442CD0`, then `0x435020` finds or creates the cell's
   stack descriptor in any coaster's stack list (the key is the cell index width·y + x), builds the node through
   `0x434060` (re-attaching the saved mesh instance) and appends it to the chain (`0x430CA0`); with the load flag
   it skips the link search and the cost charge. The setters `0x444150`, `0x4441D0` and `0x444270` restore t, the
   yaw and the bank and incline (or the loop rotation of a loop node); the wire bits become node flags
   (`0x437EA6..0x437F58`; wire 0x40/0x80 also mark the cut candidates); the collision ordinals are stored raw;
5. clears the loading flag and runs the stack pass: zero bank (node flag 0x100) on every stacked node but the top,
   the pylon parts and the stack-part visibility `0x432DF0`;
6. when the track is complete (wire 0x1), sets `ctl+0x3C |= 1`, builds the end node and sets the state through
   `0x435730(ctl, 2)`;
7. resolves every node's collision ordinals to node pointers with `0x43E520`, which walks the head (`ctl+0x54`)
   that many times along `+0x64`: the ordinals are chain positions (S0 = 0, S1 = 1, S2 = 2, the pylons in saved
   order, then the end node);
8. restores the cut (wire 0x2, `0x4382A5`: `ctl+0x60` the cut-end node, default S2, and `ctl+0x64` the cut-start
   node, default the chain's tail `ctl+0x58` as read at `0x437CEC`, before the sections were added: S2 again) and the
   track flags (`0x4382D1..0x438335`);
9. restores the controller words (`0x438340..0x43837D`): header `+4` → `+8`, `+0x12` → `+0x2C`, `+0x18` → `+0xF4`,
   `+0x1A` → `+0xF0`, `+0x1C` → `+0x140`; header `+8`, `+0xE` and `+0x10` are never written back;
10. reads every one of the type's train slots (27 bytes, the loader ORs train flag 0x4) and each car of the train
    (11 bytes), re-attaching the car model at the saved mesh id (`0x463AB0`, `0x4384CF`) and reading the two rider
    lists when both buffers exist;
11. re-splits the seats (`0x43BA90`), refreshes the tool state (`0x4426A0`), highlights the build end (`0x441840`)
    and clears the current-coaster pointer `0x790FEC`.

The loader runs no knot pass, sampler, validator or station step. It leaves the dirty flags `0x1001` and `0x4000`:
the next rebuild (`0x43EB70`) re-derives the curve, the winch marks and the mesh and clears `0x4000`, and the rating
pass (`0x43C450`) skips re-laying the trains while `0x4000` is set. The boarding span, the lead-car coordinate and
the admission and leaving rings are not saved and are empty after a load.

`SaveCoasterBlock.Parse` (payload + layout + map dimensions) bounds and validates what the traced
contract states and refuses everything else: truncation inside the header, auxiliary block, section
record or collision ordinals (named per part); a coaster count above its bound or a collision count above the native
list's eight slots (`SaveCoasterBlock.CollisionListSlots`; the loader stores the count unbounded, so a larger one
would overrun the list); a non-finite
power factor or section float; and a section cell outside the map. The train payload's extent depends
on the coaster's type (the file carries neither the train slots, `uiMaxTrains`, nor the cars per
train, `uiMaxCars`: the loader `0x437b60` reads them from the live type, `0x436d10`), so the caller
may pass each coaster's `SaveCoasterTrainLayout`, keyed by the header's controller `+0x10`: with it
`SaveCoasterTrain.ReadAll` decodes the train slots and cars exactly as the loader reads them, and
a block of several coasters reads (`OriginalParkImport.Import` takes the type's counts from a
callback and `OriginalPark` supplies them from the level's coaster `.sam`, finding the station as the
saved attraction whose mesh instance equals the controller word). Without a layout only a single coaster's
train payload stays raw (the rest of the block) and a block with more coasters is refused, since the next
header cannot be located. The section flag word follows the saver's
and the loader's maps (`SaveCoasterSectionFlags`, `0x437615..0x4376e0`, `0x437ea6..0x437f58`): wire bits 0x1, 0x2, 0x4
(manual yaw, incline, bank), the cut markers 0x40/0x80, the link marks 0x100/0x200 and the intersection marks
0x400/0x800 are restored, while 0x8, 0x10 and 0x20 (the loop-pair, loop and zero-bank node flags) are
derived again by the node constructor (the loader runs the loop setup `0x442cd0` for 0x8 and picks the loop
rotation float for 0x10). The section words are named: `section+112` is the node's mesh instance (re-attached
through `0x463ab0`), `+216` bank, `+220` incline, `+224` yaw, `+232` the height parameter and `+248`
the loop rotation (setters `0x444150`, `0x4441d0`, `0x444270`). The header's remaining track flags
(SAVE-921) are kept raw. The placement cell `+28`/`+32` is dead data (the loader hands it to `0x436ac0`, which never reads it); the train words `+0x38` (the state of the train loop sound routine `0x439780`) and `+0x68` (its handle) and `+0x64` are restored raw by the loader; the state seeds the slot's sound state (`CoasterTrainSounds`) and no loop plays until the slot's next setup; the flags word's 0x100 (the scream flag) is restored with the train; only `+0x38`, `+0x64` and `+0x68` are saved (`0x43780A..0x437821`) and the saved handles are numbers of the previous session's sound library (AUDIO-018; SAVE-923 is retired); the car byte `+0x44` is the car's seat share, which `0x43ba90` computes and the import's seats per train reproduce. A block with count 0 is exactly its 16 bytes (`SavePayloadLayout` enforces this on the
split). `OriginalParkImport.Import` runs the reader on every save and exposes the result as
`CoasterBlock`, with `ObjectsAt`/`AttractionsAt` cell queries and `CoasterSections` joins that require both cell
and object `Info.Id` to attach attraction ids. Missing or ambiguous scene matches remain visible;
header words and scene indices are never guessed as station ids. Only recognized scene kinds
815/865 participate: kind 1018 pylons are not decoded by the existing scene parser.
`ParkObjects.ImportOriginal` calls `RequireSupportedCoasterRuntime` before creating any objects: it refuses a park
only when a saved coaster's station mesh id names no single saved attraction thing, the case where tp.exe crashes
(`0x437CD5`). The separate TPI viewer is unchanged.

### The import (`OriginalCoasterImport`)

A park started from an original save restores its coasters after the stations were placed and the scripts took their
saved run state (`Level.ImportOriginalCoasters`, after `ImportOriginalScripts`). Per saved coaster, in block order:

1. **Bind.** The block binds to the saved attraction thing whose mesh instance id is the header's `+0xA`, and through
   its thing id to the placed station object. A station that is not placed as a coaster station refuses the park with
   the reason, as does a placed station whose track holds anything but its three station nodes.
2. **Nodes.** The station's three nodes are the placement's (the loader builds them from the station model with
   `0x436AC0`). Section i becomes chain node 3 + i at its cell, with its stack ordinal, loft t (`0x444150`), height
   h0 + t(h1 − h0) of its pylon control and yaw (`0x4441D0`). The loop mode starts on a section with wire 0x8 when the
   coaster type has flag 2, which the type loader `0x42FAD0` sets when the LoopPylon control names a mesh; that node and
   the next take the LoopPylon control, and a loop control (flag 0x4000) gives them the pair's node flags 0xE0 and 0x60
   (`0x434060`). The bank setter `0x444270` gets (bank, incline), or (loop rotate, 0) for a section with wire 0x10, and
   stores them as bank and incline on a node without flag 0x40, or as the loop rotate on a loop node. Wire 0x1, 0x2 and
   0x4 become the manual yaw, incline and bank flags. The zero-bank mark of a stack's lower nodes is the knot pass's stack
   pass here, as for a track the tool builds.
3. **End node and cut.** A complete track (wire 0x1) gets its one pylon-less end node (type flag 0x20, always set; `0x4380D4`). A cut
   (wire 0x2, restored at `0x4382A5`) runs from the last section with wire 0x40, or S2, to the last section with wire
   0x80, or the chain's tail as it stood before the sections were added (`0x437CEC`), which is S2 again;
   the two must be neighbours, as the cut `0x446740` leaves them, and their link bits 0x40000/0x80000 come only with the
   cut markers (closing the circuit, `0x442C70`, clears both).
4. **Controller words.** The state (low nibble of `+4`), the ride mode (0x10, 0x40 the breakdown, 0x20), the power factor,
   which must match the mode, and the seats per train are restored. A coaster that never ran is saved in state 1 with power 0:
   the constructor's controller is zero-filled (`0x4368F0`) and only the first 1→2 transition (`0x43577D`) or a 0x40→0x10/0x20
   step (`0x435931`, `0x43597F`) sets 1.0, so state 1 in mode 16 or 32 accepts power 0 as well as 1. The speed setting is not in the block and stays the
   constructor's 60.
5. **Trains.** A runnable track is rated at that cap, and the trains in service (`+0x18`) keep their saved slots: the
   reference car's position (`+0x74`), the speed (`+0x78`) and the train flags 0x10..0x80. The first rebuild after a load
   does the same: the rating pass `0x43C450`, called only from the rebuild `0x43EB70`, skips the train count, the slot
   set-up and the layout while the loader's flag 0x4000 is set. Each in-service slot must be set up (bit 0) and hold the
   type's car count. That first frame's coaster update then runs the controller's first motion tick, which takes no
   time (`0x43CE85`: the tick that finds controller flag 0x10 clear sets it and its clock word first, and the loader
   leaves the flag clear): the trains stay where they were saved, while a speed above the cap is capped, one below 2.0
   decays by 0.95 and one below 0.01 stops (`CoasterRide.UpdateFirstFrameAfterLoad`). A newly built controller's first
   tick is the same.
6. **Script binding.** When the station's saved script holds this coaster's slot (script `+0xE0`, `COAST 8` stored it),
   the coaster counts as created by its script. The script loop's speed setter (`0x5516B0` → `0x43B2B0` → `0x43C3D0`)
   runs in the tail of every slice the cadence selects (one pass in eight by script id unless the TURBO byte is set,
   `0x5516E1`; the call at `0x5517EA`), so it applies the script's speed word (`+0xC0`) after the script's first slice
   after the load (`OriginalObjectEffects.AfterSlice`). When the word differs from 60, the rebuild that follows has
   0x4000 clear, so the track is rated at the script's cap and the trains are laid out again from train 0's last car
   (`0x43C1F0`).
7. **The first frame's order.** In tp.exe the order of the first frame after a load depends on the wall clock. The load
   path that sets `0x879070` (the save loader `0x414D40` in mode 2 calls `0x415140`; the paths through `0x5ACCF0` and
   `0x5AC650` load in mode 2) makes the running case of the main loop `0x54E360` restart its substep clock at the
   frame's own time: it reads the clock into `0x878C74`, then reads it again after the pause handling and `0x473440`,
   and runs a substep (and with it the script pass) only when the second read is later. Usually it is not, and the
   frame's coaster update (`0x435A30`, once per frame after the substep loop) runs first: its rebuild keeps the saved
   trains and clears 0x4000, and the speed word re-lays them later. When the clock ticks between the two reads, one
   substep runs first, and if the station script's slice falls on that pass, the speed word applies before the first
   rebuild, which then rates at the script's cap but keeps the saved trains (no re-lay). OpenTPW takes the usual order.
   A third load path (`0x54F113`, the save loader in mode 1 on the file of the online object `0xF7D88C`, built by
   `0x5B50B0`, whose code logs "ONLINE_NEWS" and "ONLINE_FTPDOWNLOAD", followed by `0x404140`) does not set `0x879070`
   and runs eight script passes before the first frame (`0x4041D0`), so every script's slice, and with it the speed word,
   comes before the first rebuild there. That is the online park download, not the load of a saved park.

Checked but not imported (evidence lines in the import report):

- the self-intersection count and the collision lists, including the auxiliary block, which the rebuild re-derives
  (COAST-004);
- the track flags (SAVE-921);
- surplus set-up slots;
- (the riders on the trains are imported since SAVE-920 was retired: each car's lap list and station list fill the
  train's car lists as saved, without hanging a head; every listed guest must be a rider of the ride, the train's rider
  count must equal its listed guests, and a list longer than the car type's head sockets is refused. The admission and
  leaving rings are not saved, so a rider on no list stays aboard with nobody to let it off, as in tp.exe, which has
  no sweep after a load. Only synthetic blocks test this: no corpus save holds a coaster with riders.)

Shapes the original's own editing never leaves are refused with the reason. Examples: a cut on an incomplete track, a
state that does not fit the track, a word with bits beyond the state and the mode, a power factor that does not fit the
mode, seats outside COAST 6's range, or an in-service slot that is not set up.

**Verification status.** This mapping is verified against tp.exe's loader and against synthetic blocks only, **not
against a real Theme Park World save with a coaster**: none of the local saves holds one. `OriginalCoasterImportTests`
covers it. A test-only writer (`OriginalCoasterBlockWriter`) follows the decoded reader field for field. It writes
coasters that OpenTPW built (a running circuit with a hand-rotated and a hand-tilted pylon, an unfinished chain with a
loop pair and a stacked pylon, a cut in the middle and after S2, a breakdown) in the original layout. The production
reader parses them and the import restores the same nodes, flags, circuit, state, samples, rating, collision lists,
trains and seats. The imported coaster continues as the built one does and round-trips through OpenTPW's own save.
Hand-made blocks cover the refusals and the loop-mode rule. A fixed run of an imported coaster is pinned
(docs/DETERMINISM.md). The Theme Park Inc prebuilt block is a real-byte cross-check: it reads byte for byte and maps
consistently (see below), but its coaster type is a stand-in.

### Evidence status

- **Static contract (high):** the Mac PowerPC saver/loader field layout above, pinned by
  `save_evidence.py` (132 native fields/calls; docs/reverse/PPC-rides.md).
- **Original Theme Park World data (empty boundary only):** jungle `Easymode.TPWI` holds a 16-byte
  block, count 0 with globals 0, 0, 1 (body SHA-256
  `741939ccb979df0dbf391a6548f88fd9d1873aa3fb996f1df39abc6a66df7025`). No original Theme Park World
  save with a coaster was found under the available `game-assets` tree (the Patch2 Easymode
  copy is the same empty boundary). The private eleven-save set is described in the prior
  [second fixture set](#second-fixture-set) report, but its location was not available and
  `OPENTPW_TPWS_SAVES` was unset for this slice. Rechecked on 2026-10-17 with the folder set: all eleven
  saves hold the empty boundary (count 0, globals 0, 0, 1) and the empty TrackRides (44 bytes) and FlyingRides (452 bytes)
  blocks, so none holds a coaster, a train, a car or a track ride. The nonempty shape is therefore **not** proven on
  Theme Park World bytes.
- **Theme Park Inc cross-check (sibling game, separate semantics):** the Theme Park Inc prebuilt
  save's Coasters block (1,226 bytes between the same delimiters) parses under this contract exactly:
  count 1, globals 1, 0, 2 (slot 1 used, the cursor at 2); header wire flags 0x3, state word 0x11, type id 19,
  station mesh 4, slot 1, placement values 69 and 65, power factor 1.0, one section, three trains in service,
  two seats, no self-intersection, an empty auxiliary block; one section record with no collisions, cell (65, 67), stack ordinal 0, height parameter ≈ 0.0556, and a 1,140-byte train
  tail. Its park's RideSystem list places a 6×6 coaster station (Info.Id 1180) and 1×1 pylon records
  (Info.Id 1181, kind 1018) — the saved section's cell (65, 67) carries its own such record. The raw kind-1018 pylon header is pinned separately because the existing scene parser excludes it. That is
  a real-data cell association observation, but it is Theme Park Inc evidence: no Theme Park World proof, and no
  Theme Park Inc coaster chunk is inferred anywhere in production code (the reader runs on it only in
  a labelled test).
  The decoded words are also consistent with tp.exe's loader. The one section is the start of a cut (wire 0x280:
  0x80 with the link bit 0x200) after station node S2. The track is complete and cut open (wire 0x3) in state 1,
  which is the cut's state. Three train slots are still set up (flags 0x1, cars hidden) on a track that does not run.
  `ThemeParkIncSaveWorldTests.TheCoasterBlockReadsAndMapsAsTheTpwLoaderReadsIt` imports it onto a stand-in coaster
  type with the block's 30 one-car slots: a complete track, cut after S2, with no trains. Theme Park Inc's own loader
  is not traced, so this is a cross-check, not proof.
- Station nodes are never sections: the saver `0x437300` skips every node whose descriptor byte `+4` carries 0x10
  (the station nodes 0x12 and the end node 0x13) and writes S2's collision list as the
  auxiliary block instead. Consumers still join sections to objects by cell.

## Sound block (`SaveSoundData`, `SaveSoundSettings`)

Writer `0x51C350` (called at `0x416C02`, after the advisor block), loader `0x51C400` (called at `0x415BA2`, which logs "Sound: loaded %d bytes" and
then checks the `SOUN` tag). Neither is QMixer code: QMIXER.dll exports no state functions and tp.exe imports none.

| Field | Meaning |
| --- | --- |
| `u32` | sound initialised (`0x802BD4`, set by the init `0x51B660`) |
| `u32` | sound enabled (`0x802BC8`; a running game writes `0x400`); the loader writes it straight back |
| `u32` length, bytes | the 84 bytes (`0x54`) of the sound-service globals at `0x803AA0` (`SaveSoundSettings`, below) |
| 3 x `f32` | only when initialised: the listener position, which the loader reads and discards |
| `u32` length, bytes | the sound library's voice list (writer `0x6B6C30`, reader `0x6B6DA0`): a registry record, the handle counter and every live voice by handle; the loader stops every voice and rebuilds these |

The 21 words of the settings (`SaveSoundSettings`, `SaveSoundData.SettingsView`; null unless the length is 84):

| Offset | Name | Meaning |
| --- | --- | --- |
| +0x00 | `MusicHandle` | park music voice handle (`cat_music` event 2, park entry `0x51E730`) |
| +0x04 | `CrowdHandle` | crowd sentence handle (`cat_kids` 0x5B, `0x51E7B0`) |
| +0x08 | `CrowdAllowed` | 1 after the init; no other writer |
| +0x0C | `RainHandle` | level rain voice handle (`cat_ambient` 0x21, `0x51E830`) |
| +0x10 | `CrowdLevel` | crowd level 0..100 |
| +0x14 | `ReverbPreset` | held `SndReverb.map` preset; the loader sends it again |
| +0x18 | `MasterVolume` | master percent (setter `0x51BB60`, only called at start-up with 100) |
| +0x1C | `EffectsVolume` | effects percent (setter `0x51BBA0`) |
| +0x20 | `MusicVolume` | music percent (setter `0x51BB80`) |
| +0x24 | `SpeechVolume` | speech percent (setter `0x51BBC0`) |
| +0x28 | `Dirty` | the tick `0x51BD70` pushes all seven mixer values while it is non-zero, then clears it; park entry sets it after the load (`0x54F3AE`) |
| +0x2C | `SpeechPlaying` | the advisor speaks (`0x51E6F0`); a duck condition |
| +0x30 | `MusicDuck` (u16) | DIPMUSIC request (`0x51E710`); non-zero sends music as 0 |
| +0x32 | `ListenerFreeze` (u16) | listener updates skipped (`0x51C1C0`); the post-load pass `0x415140` clears it |
| +0x34 | `Unused34` | no reader or writer found |
| +0x38 | `EffectsOn` | effects on (setter `0x51BC00`) |
| +0x3C | `MusicOn` | music on (setter `0x51BBE0`) |
| +0x40 | `SpeechOn` | speech on (setter `0x51BC20`) |
| +0x44..+0x50 | `Tail` | four words without references |

All twelve corpus files (the eleven private saves and the shipped Easymode) hold the defaults: master 100, effects 75, music 60, speech 75, every
channel on, dirty 1, no duck.

**Import.** A park started from an original save (a player's own, `--original-save`, or the level's shipped Easymode seed, which the original's park
entry `0x5ACCF0` loads as the Instant Action player's copy) plays at the save's master, effects, music and speech volumes and
on-flags (`GameAudio.ApplySavedSound`, `SavedSoundVolumes`), as the original's next sound tick does with the copied shadows; the player's options
and `opentpw-options.json` are not changed, because the original's profile options (`0x78D8E8`) are a separate struct that the load does not touch
and that the profile file is written from (`0x423E90`). The saved values end where the original's end (docs/AUDIO.md, "Volumes of a loaded original
save"); Restart Park applies the start's values again, as the original's `restart.INTS` reload does. Everything else in the block is the original
engine's own state (`[EXT:original-engine-state-dumps]`): crowd, rain and reverb are recomputed within frames by OpenTPW as by the original, the
duck follows the saved DIPMUSIC script byte, the original itself discards the listener floats and clears the freeze, and the voice handles and
voice list name voices of tp.exe's sound library. The writer copies the block verbatim from the template (byte-exact round trip).

## Money (bank thing, model 16)

The bank thing named by `mBankAccount` holds, after its 8-byte base, seven bank
words (admission fee 25, balance 87,987, batch balance 0, withdrawals enabled,
last balance 87,787, turn entered red 0, annual profit −12,013) and eight 32-byte
loan offers: available flag, 32-bit amount, APR, months, monthly repayment,
bought flag, months repaid and lender name index. Lenders need not equal record
order. Repayments equal floor(amount/months), matching `Easy_Standard.sam`. The
challenge system thing (model 19) holds 20 slots of 45 bytes; the leading slots
with a nonzero type are the level's challenges (eight in Easymode, equal to jungle
`ChallengesInThisLevel`), the rest are uninitialized `0xCD` memory. Both are read
through the chain by `SaveEconomyRecords`; the former structure search and its
plausibility bounds (ECON-045) are gone. The spending history is not in the save.

## Importer (`OriginalParkImport`, `OriginalEconomyImport`)

What the import skips or only partly rebuilds, thing by thing with its constructor and covering ID, is inventoried in [SAVE-THINGS.md](SAVE-THINGS.md); the slots of the things it does not rebuild stay used.

`OriginalParkImport.Import` takes the decoded payload and the level `base.map`.
It splits the blocks, parses the World block (rejecting any cell attribute that
differs from the map: wrong level), reads the RideSystem records (rejecting any kind 815 or 865 record whose
Info.Id or index is not 1–65,535, whose cell is outside the grid, whose size is not 1–16 cells or whose
rotation is not 0/90/180/270, tested on the raw unsigned words, and any
placed footprint outside the map or not occupied in the grid) and the typed
things. Output: path cells, placed objects with footprints, fixed items,
unresolved records, attractions, the bank and challenges, staff, staff HQ,
advisor controller and research lab. `OriginalPark.Load` adds
`base.MD2`/heightfield and resolves every Info.Id through the level's object
`.sam` files (unknown ids are an error).

A park started from the save (Instant Action, or the reference start) then runs
`OriginalEconomyImport.Apply` (docs/ECONOMY.md, "Original save import") and the
automatic advisor takes the saved controller (docs/LIPS.md). `Import` also parses the
Coasters block (`SaveCoasterBlock`; a block that breaks its traced contract refuses the save) and
exposes it as `CoasterBlock`, with `ObjectsAt`/`AttractionsAt` cell joins for consumers. A park whose block lists a coaster is refused
(`RequireSupportedCoasterRuntime`, SAVE-920): the headers, sections, trains and cars are decoded and no runtime track, controller or train is rebuilt.
Imported: park turn and date, the world generator state `mRandomSeed` (`World.Restore`, SAVE-001 retired), bank balance, admission fee, annual profit and bought loans, each
object's price, cost of goods, upgrade level, state of repair, life gauge, walk-away count, operating state, chance of winning, quality of goods, customer
count and time stamp (also for objects whose footprint is unverified), staff (type, grade, training percentage, happiness, energy, name, job, hire time, idle stamp and patrol), the hiring pool, the staff HQ's training budgets and strike levels, research (finished and started projects, each category's effort and current project, the work load and the highest open groups), the analyser's four recent-research rings, the challenges, the park
open flag, and the advisor's pending advice and history of bound messages. The saved guests are restored by `GuestSimulation.ImportOriginal`: states 0–10, 15, 17, 18, 19 and 21 keep their thing id, type, cash, meters and destination and walk on; guests in a queue (11–14 and the state-8 interlude with the saved state 11) are restored in the order of their attraction's saved queue (`GuestSimulation.PlanOriginalQueues`: the head mFirstInQ +0x3C, the links mQNext +0x228 and mQPrev +0x22A and the called guest mPersonBeingLoaded +0x6C are read as saved, nothing relinks them, and a chain that is not a list of guests of that ride in the queue states, a head 13 or 14 behind the head, or a called guest that is not the head refuses the queue with its reason); a guest held by a guard (17) is imported held and stays only while a guard in state 18 holds it as its perpetrator (the escorting guard is imported with it and walks on to the ticket booth; no corpus save holds one, so it is checked on synthetic saves only); guests using an attraction (16) or walking to the arrival vehicle (20) are not imported (SAVE-002); a park guest (states 6–10) standing off the paths is imported where it stands and recovers through its own state handlers, as tp.exe's load leaves it (`0x4F9407`, `0x4F9490`).
Still read and not imported, each reported and tracked by a SAVE approximation: those dropped guests (SAVE-002), unknown research object/project identities absent from the reconstructed lab (SAVE-007; reported; none in the corpus; known current projects are kept as saved even when the picker would not offer them), the hiring
pool's candidates saved on the pointer (SAVE-004), the advisor presentation block, the world state, the arrival variables and bytes, the calendar session start, the advisor
controller's presentation words (its current record is the last tutorial advice when an automatic advisor attaches) and the weather, park analyser apart from its recent-research rings,
tag system, mechanic HQ and UI receiver things (SAVE-010). The Scripts block's sprite scripts, the saved guests' and litter's animations, are decoded and not applied (SAVE-013): guests and staff animate from their state and litter from the cell. The Particles pool is restored, minus the emitter and effector fields SAVE-013 drops. The RSSE block's script ids, list order, next id, pass counter and run state are applied (`SavedScriptImport`, `RideVM.ImportSavedRunState`: the program counter, stack, variables, limbo table, waits and timers on the saved game clock); the records' animation controls (below AnimID 12, on the saved game clock, ended or looping as saved, with their deferred clip) and node bits 0x10/0x80000000 are restored (`OriginalObjectRuntime.RestoreSavedScene`, loader 0x4647A0); the RSSE records' object lists, object node tables, bounce and walk tables, and controls held on the first frame (flag 0x2) or on the unscaled clock (flag 0x40) are not. A script with a saved child or sound child keeps its fresh state; saved child records are left for their parent to recreate. Of the Sound block a park started from an original save applies its volumes and on-flags for the session (see "Sound block"); the rest of it, the message-centre lists and the Track and Flying ride chunks are not restored (`[EXT:original-engine-state-dumps]`). Staff
values outside OpenTPW's ranges (happiness outside the traced 0–100, a 100 %
training percentage, a state without a STAFFSTATES entry, a hire time outside
the calendar or inside a park turn, which is rounded down to the turn) are adjusted and each adjustment is reported; an attraction whose
values are out of range (including a negative cost of goods) keeps OpenTPW's
defaults and is listed. The saved advisor's pending advice and history are
dropped, with an evidence line, when no automatic advisor is attached.
Path styles, queues, extension data and object models are not imported.

## Writing original-format saves (`SaveContainerWriter`, `OriginalSavePayload`, `OriginalParkExport`)

An original-format save can be written now, byte for byte. Nothing here runs the original game or holds a save's bytes: the proof is a read-then-write round trip over the eleven private saves and the
shipped Jungle `Easymode.TPWI`.

**Compression.** The game's BILZ writer (vtable method `0x619280`, reached from the save function `0x414920` through `0x5F8050`) calls `deflateInit2(-1, Z_DEFLATED, 15, 9, 0)` and one
`deflate(Z_FINISH)` of zlib 1.1.3 (the version string at `0x7780F0`) and stores `'BILZ'`, the payload length, the chunk length (the compressed length plus the 0x1C header), 15, 9, 0, 0. The stock
`ZLibStream` is another zlib and emits other bytes, so `ClassicZlibDeflater` ports the algorithm (level 6 lazy matching, a 64K hash, 32K symbol blocks; `deflate_slow`, `longest_match`, `trees.c`).
`ClassicZlibDeflaterTests` pins its output hashes against the reference encoder over deterministic inputs (matches across window slides, runs, incompressible blocks), and every corpus
file's compressed stream is reproduced exactly.

**Container.** `SaveContainerWriter` is the twin of the save function `0x414920` and the header writer `0x415F50`: magic (the static 500 at `0x6FD928`; the shipped TPWI keeps 400), the language byte
(the chain of name compares at `0x415F88..0x416048`: English 0, Spanish 1, Italian 2, Swedish 3, German 4, French 5, Japanese 6, anything else 0), a zeroed 0x500-byte buffer with the banner copied
from the language table at `0x78A464`, a zeroed 0x100-byte buffer with the 8-word required-content bitmap at `0x802080`, `htonl(0x01221985)` and the online flag, then the BILZ chunk. The banner is the
only text of the file the original takes from its own tables; the writer takes it from its caller (`SaveContainerFields.Of` copies it from an existing container, the original's English notice is not
repeated in the repository).

**Payload.** `OriginalSavePayload` holds the 19 blocks as models whose fields are exactly what each loader reads, in the loader's order, and `Write` emits them in the order of the payload writer
`0x4164C0` with its 17 delimiters (the constants at `0x6FD8B8..0x6FD8F8`, read from the executable and found equal to `SavePayloadLayout.Delimiters`, each stored byte-reversed). The original's block
routines are single serializers run with the writer's stream (the World writer `0x516C80` calls `0x4D3AA0`, `0x4D7A70` and `0x4D7EA0` and the per-model serializers, the same routines the loader
`0x5179C0` calls), so the loader's field order is the writer's; the 26 labelled variables, `Used Thing Head`, `Used Thing Next` and `thingmodel` were checked in the World writer's own label calls.
Typed: the action recording, the World variables, staff pool, calendar, arrivals, map cell sizes and thing chain, the Scripts slot table, the Particles on flag and counted tables, the message centre's
29 lists, the clock, vanilla-time and game-system words, the flying-rides slot array and bodies, the RSSE header, padding words, records, buffers and `'OBJ '` lists, the camera words, the coasters'
counts, the advisor and sound framing and the cheat, advisor-scoring and UI words. Kept as raw runs, because the original copies them as one dump or no OpenTPW reader decodes their fields: the
control manager's 150 records, each map cell's records, every thing body except the bank (`SaveBank.ToBody` is the bank serializer `0x4CF920`), the RideSystem frame, the track-rides chunk, the coaster
chunks, the particle tables, the RSSE structs and the sound library's voice list.

**Round trip.** `OriginalSaveWriterCorpusTests` reads each of the eleven saves (seven named parks and autosaves, four `restart.INTS`) and the shipped Easymode into the models, writes them again and
compares the payload and the whole container: identical bytes in all twelve (Easymode's 18,288 uninitialised `0xCD` bytes included, because they sit in raw runs). Synthetic tests
(`OriginalSaveWriterTests`) cover the writer without any original data: the block order and delimiters, the container fields and BILZ header, strictness (a broken thing chain, a nonzero script slot
without a record, a block with bytes its loader does not read).

**Export of an OpenTPW park.** `OriginalParkExport` writes the economy back into a *template*, an original save of the same level (the shipped Jungle Easymode, a park the player loaded or a
`restart.INTS`): the park turn (`mGameTick`), the open flag (`mParkClosed`), the world generator state (`mRandomSeed`), the calendar caches, the bank account words and loans and the object controls'
first-purchase turns replace the template's; every other byte is the template's. Importing each of the twelve originals into an economy and exporting it onto its own bytes reproduces the file
exactly except for the first-purchase stamps: the import stamps the saved turn into a type whose control word was 0 (the original's own stamp when the first saved object of the type is built,
`0x4DB690`), so the original writes the same value on its next save; the four `restart.INTS` come back unchanged. The exported payload goes back through the loader's split (each block's reader and
tag) and both importers and yields the same economy (`OriginalParkExportTests`).

**Research (`OriginalResearchExport`).** When the caller passes the park's research (`OriginalParkExportOptions.Research`), the research lab thing and the object controls are written from it too: the 256 project
records in the original's order (the fill `0x5031A0` walks the object scan and adds a base project for a research cost above 0, or for a free base its first level project at that turn; the level projects completions
appended follow in completion order, `0x504580`), each project's cost and points (a finished project holds its cost, the overshoot of its last research step is not kept), the five group records (effort, output
`(effort x 100 / the sum) x work load / 100` in unsigned integers, the current project's index, the highest open group), the tail (project count, work load, the fill's most expensive project, the lab-active byte
that `0x504090` and `0x504140` set with the researchers) and each researchable object's control (`+0x10` researched, `+0x14` the highest finished level, the levels without a cost counting as finished,
`ParkResearch.ResearchedLevel`). The four `restart.INTS` the original wrote hold exactly OpenTPW's new-park table for their theme (Hallow 48 projects, Jungle 48, Fantasy 44, Space 46, key for key and in order), so a
new park's lab is built, not carried, and reads back to the byte in all four (`OriginalResearchExportTests`); only the shipped Easymode save lists a subset (24 of the 44), which a template of the same level keeps.
The eleven private saves come back through import and export with the same fields (the played saves' finished projects hold their cost where the save kept the overshoot).

**Recent research (`SaveRecentResearch`).** The world-referenced TPW park analyser (model 13) carries four capacity-three rings at body bytes 62,492 through 62,559. Each 17-byte record holds head i32, capacity i32, wrapped u8, scratch u16 and three physical u16 slots. Import checks the referenced model and bounds, retains scratch and inactive values, and replaces runtime history without creating completion events. With explicit `OriginalParkExportOptions.Research`, export writes only this 68-byte range, even for empty history; without it, the template history is preserved. All other analyser bytes remain untouched. The host rejects malformed ring counts, capacities, head/wrapped combinations and unsigned-short values before applying state; this bounded validation is a defensive host policy, stronger than the original loader. These offsets and the paid-completion policy are TPW-specific, not a Theme Park Inc interpretation. Synthetic regression tests cover typed references, byte-preserving round trips and malformed input; they do not claim original execution or pixel parity.

**Statistics (`SaveParkStatistics`).** The World-referenced TPW analyser imports five independent 144-slot monthly rings, its live arrivals accumulator and the 50-slot recent-stay ring. Relative to the body (including the eight-byte thing base), the monthly records start at 62,590 and occupy 589 bytes each: signed head, capacity, wrapped byte, scratch word and 144 physical words. The graph selects ordinals 9 (people), 12 (arrivals), 13 (binary32 happiness), 14 (whole-day stay) and 15 (rating); recent stays start at 72,014. The serializer (`0x4C5D70` / `0x4CDF00`), graph dispatch (`0x4A4750`) and month producer (`0x4C7720`) establish these meanings, independently of equal record sizes.

The reader requires the complete 73,536-byte fixed body, checks all four short-ring and sixteen monthly capacities, and validates each selected ring's head and wrapped byte. Only active happiness words are reinterpreted as floats and required to be finite; signed integer samples are preserved, including negatives and outliers. Inactive words are retained in the immutable native carrier but normalized to zero in host statistics. Negative live arrivals are rejected by host continuation policy. A malformed selected ring or continuation input rejects the entire bundle before economy application; there is no partial graph recovery. No timestamps, old bank transactions, financial months or simulation caches are invented.

Rating has its own host ring. Month close samples it once for both statistics and the financial ledger. Legacy host saves lacking rating seed it once from their existing ledger; a present empty ring remains empty. The whole statistics section stays excluded from the simulation hash. Native statistics export is unchanged: original export keeps the template's statistics bytes, and host JSON is not a byte-identical analyser round trip. Theme Park Inc uses the separate `SaveThemeParkIncStatistics` named-field decoder and independently traced ordinals, with the [fresh-host-start boundary](THEME-PARK-INC.md#historical-statistics-and-the-host-start-boundary) kept explicit; no TPW offset or ordinal mapping is reused. The shipped one-month TPW fixture is an import witness; wrapped and continuation coverage is synthetic, not a claim of a real long-running save or original execution. UI-914 remains open for its other residuals.

**Object and hire stamps (`SaveAnalyserStamps`).** After the arrival ring (body bytes 72,227 to 72,259) the analyser serializer `0x4C5D70` writes twenty byte rings of 60 bytes, the counters `mMisbehavingKids`, `mMisbehavingKidsThatGotAwayWithIt`, `mMisbehavingKidsEver` and `mLifetimeVisitors`, six 8-byte time stamps at body bytes 73,476 to 73,523 and the three words `mMostPaidForTicket`, `mLargestBusLoad` and `mLongestStay` that end the body. The stamps are written raw (`0x4CEDE0`) and read back raw (`0x510240`) in the order `mLastStrike`, `mLastPuke`, `mLastPrank`, `mParkLastOpened`, `mLastObjectBuilt` and `mLastStaffHired`. Each is the park time of its moment, the calendar's `FunnyTimeStart` plus floor(turn x rate / 4) x 10^7 in 100 ns units (`0x4F8690`), so in an original save it lies a whole park turn from the calendar's start; all six do in the eleven private saves (the four restart saves hold turn 0; in the seven played ones `mLastObjectBuilt` lies at turns 1,169 to 1,777 and `mLastStaffHired` at 2,609 to 3,702). The loader `0x5179C0` zeroes the six before the read (`0x518A43`-`0x518A85`) and nothing it loads restamps them, so the import converts the last two to the turns of the save's calendar (its start and rate) whose park time they are and hands them to the automatic advisor (`OriginalEconomyImport.AnalyserStampTurns`). For every stamp an original save holds, a whole turn at or before the park's turn, the advisor's days since the newest construction and hire then count exactly as tp.exe's `0x4C9070` and `0x4C90D0` count them. A stamp between two turns' times, which only a modified save holds, is taken at the earlier turn and reported, and can move the count by the day its seconds cross; one before the calendar's start or past the park's turn keeps its turn (a stamp still to come counts negative days, as the original's signed division does). The days count on OpenTPW's calendar of 3,750 seconds a turn, so for a modified save whose calendar runs at another rate than 15000 they follow the park's own calendar, as its dates do; the import reports that the save's calendar does not agree. The reader refuses a byte ring before the stamps that is not the 50-entry shape, which would move them. The other four stamps are not imported.

**Things of a new park (`SaveNewParkThings`).** The park analyser (73,536 bytes: the grid words, 1,024 cell records whose extents the setup `0x4C7A90` fills, the history rings and extreme trackers at the
constructor `0x4C58C0`'s values, in the order of the serializer `0x4C5D70`), the advisor controller (`0x599E70`; its first pending record, message 0 at score 100,000, is in all four restart saves and its posting site is
not read), the tag system, the UI message receiver, the mechanic HQ and the staff HQ are generated, and equal the four restart saves' bodies byte for byte (`SaveNewParkThingsTests`).

What OpenTPW cannot export yet, so the template's version stays: placed objects and their attraction things (a new object needs its attraction body, its RideSystem scene record with the object
model's node and control counts, the cells' parent, mesh-instance and footprint fields and a thing id), built paths (a path cell differs from a plain one in the direction, flag, neighbour, texture,
rotation and type fields; the rule that sets each is not traced), staff, guests, the advisor's state beyond a new park's, weather and the engine blocks. A park without a template cannot be exported: of a new park's
12 things the analyser, the advisor controller, the tag system, the message receiver and both HQs are now built, and the research lab is built from the research; the object controls (the ids of the level's
objects), the map cells, the attractions, the challenges, the bank, the weather, the particles, the RSSE, the RideSystem, the flying rides, the advisor block and the sound chunk depend on the theme, and the
message centre's lists and the track-rides build stamp are executable constants. Building those from the level's data is the rest of the from-scratch writer; it needs the attraction and scene-record
constructors and the object models' node counts next.

**Import of the script run state.** The Clock block's first word is the game clock the RSSE records' absolute deadlines count from (the Fantasy corpus seaplane is saved inside a `WAITANIM` 4,873 ms short of its
deadline, `0x553783` and `0x553828` store the deadline at `+0xA0` and rewind the program counter): an imported park's script world starts its scheduler clock there and each matched script resumes at its saved
program counter with its stack, variables, limbo table, waits and timers (`SavedScriptImport`, `RideVM.ImportSavedRunState`). The object lists, node, bounce and walk tables of the records, and the animations
playing on their objects, are not restored. A script with a saved child or sound child keeps its fresh state; saved child records are left for their parent to recreate.

## Tests

`SavePayloadLayoutTests`: synthetic payloads in writer order (blocks tile the
payload, trailing delimiters), missing/out-of-order/repeated delimiters, each
traced block width, the empty Coasters block, truncated and corrupt action
recordings, the size cap and a non-seekable save stream; the private fixture
checks that every block parses and has content and that the corrected
attribution holds (TrackRides ends with the build stamp, FlyingRides starts
`FLY_`). `SaveWorldTests`: the thing-chain walk on synthetic Worlds (closure at
the delimiter, one byte more or less, wrong body width, unknown/rejected models,
repeated and out-of-range IDs, truncated bodies, World version, variables naming
a missing or wrong-model thing, calendar), and each typed reader (attraction
bounds and truncation, bank and loan bounds, challenge slot prefix, advisor
valid/played flags, staff, staff HQ, research lab); the private fixture checks
invariants rather than offsets: the chain closes at `WRLD`, every model occurs,
singletons once, one attraction per RideSystem record, staff and role heads
agree, and the calendar caches equal the date of the saved turn.
`OriginalParkImportTests`: synthetic grids (extensions, X-fastest order,
missing/truncated/ambiguous runs), the RideSystem framed walk and footprint
routing rules,
wrong-map and unoccupied-footprint rejection; the private fixture pins the grid
range, 78 path cells, the connection-bit property, the 11 placed objects and 3
fixed items, and rejection against `terrain.map`. `OriginalSaveImportTests`: the
runtime import on a synthetic park (turn, date, ledger month, pool and challenge
timers, bank, bought loans, closed flag, object state, staff, every SAVE
approximation reported, save round trip), settings verification, the advisor
controller import, and the Instant Action start on the private fixture.
`OriginalParkPlacementTests` covers Info.Id names and the build rules. Without
`OPENTPW_GAME_PATH` the private tests are inconclusive, not passes.
`OriginalCoasterSaveTests` covers the Coasters block reader: synthetic bodies in the traced
layout (every header, auxiliary, section, link and train field; offsets), truncation inside each
part, the coaster/auxiliary/link bounds, non-finite floats, out-of-map cells, the two-coaster
refusal and the layout's empty-block rule, plus the park import with its cell joins and
out-of-map refusal. Its two fixture tests pin real data: the Easymode empty boundary by SHA-256
(`OPENTPW_GAME_PATH`) and the Theme Park Inc prebuilt body under the shared contract
(`OPENTPW_TPI_PATH`, labelled cross-check).
`SaveRideSystemTests`: the Easymode record facts, the synthetic decode (presence
bytes, headers, flag mapping, embedded pairs, node states, controls), the
unique-parse rule (a two-parse frame is refused, a control-count table picks
one), the 50,000-slot frame without recursion, the search budget, the `T >= 1` rule
and the refusals (presence byte, lie counts and wrapping count sums,
`firstFree` mismatch, empty frames with trailing bytes, slot bounds, unknown flag bits,
node-state bits 4 and 12, truncation, unsafe `RideSystem` layouts — the length word is
also re-proved by `TryMeasure` in `SavePayloadLayoutTests`).
`OriginalSaveCorpusTests` runs container, block split, grid, thing chain,
RideSystem, attraction and economy readers over the second fixture set
(inconclusive without `OPENTPW_TPWS_SAVES`; not run for SAVE-I); since the coaster slice
`Import` parses every save's Coasters block there too.

## Remaining gates

- A from-scratch original-format park (no template): the constructors of the attractions and ride-system scene records (the analyser, advisor controller and research lab are built, see "Writing original-format saves"); exporting placed objects, built paths, staff and guests.
- An online (LAYS) save; LAYS payloads are unsupported.
- A non-empty TPWS: the second fixture set has no placed objects, extension records or
  extra paths, so those readers are still proven on Easymode alone. A Theme Park World
  save with a built coaster does not exist in any available corpus: the Coasters block's
  nonempty shape rests on the static Mac contract and the Theme Park Inc cross-check.
- The meaning of the coaster header's track flags (SAVE-920, SAVE-921) and the train sound handles (AUDIO-018); the train layout is decoded from the loader and the saver but
  no Theme Park World save with a coaster exists to check it on real bytes.
- The control manager and the macro AI before the calendar (their widths are traced, their bodies are not), the guest, analyser and weather bodies, record
  fields marked raw above, the extension data, the RideSystem control and embedded words and the remaining
  node-state flag meanings (the record frame, header and lengths are decoded) and the meaning of the unnamed RSSE struct words.
- Saves with known ride wear to confirm the attraction gauges against the original wear rule.
- Money/time/guest state needs known-state reference saves before import.


## TrackRides: structural reader and track import (TRACK-032 narrowed)

`SaveTrackRideBlock.ParseContainer` accepts exactly the full type-1 container, including its 12-byte header.
`ParseBody` accepts only subchunks, as stored in `OriginalSavePayload.TrackRides`; check `TrackRidesType == 1`
and pass `TrackRidesWord` separately. Chunk offsets are always relative to the full container header.
All header words, stream order, opaque payload bytes, duplicate identities/positions and repeated chunk-6 requests
are preserved. A malformed later chunk returns no partial document. This does not establish runtime import atomicity.

TPW writer shapes (12-byte framing included in total):

| Chunk | Payload | Total | Contents |
|---|---:|---:|---|
| 3 | 40 | 52 | handle, ride +b4/+b8, +b0, +10, +1c, +14, +18, +4 duration, +50 |
| 4 | 16 | 28 | handle, tile/type word, two coordinates |
| 5 | 196 | 208 | handle, raw 0xac record, two fallback tile coordinates, waypoint owner handle and two waypoint coordinates |
| 6 | 4 | 16 | ride handle requesting reference selection, not a section index |

Evidence: writer `0x542b05..0x542bda`, `0x542ccb..0x542d06`, `0x542e3b..0x542f6c`, `0x54335b..0x5433b5`;
reader dispatch `0x544124`. The reader requires these exact typed sizes as a **managed strict writer-shape policy**.
Native reads may zero-fill short data and skip excess bytes, so this is not a claim about native corruption rejection.
Other chunk types retain their opaque bytes; Theme Park Inc has not been verified for this typed contract.

The 0xd0 ride size is an **in-memory stride**, not chunk 3's serialized width. Chunk 5's raw records come from the
0x877b68 pool, in pool order, not circuit order. Their raw pointer-looking fields are not followed. Raw +0x34/+0x38
locate a tile; trailing coordinates provide a fallback. The trailing waypoint key is resolved natively against an
owner ride's separate +0xa8 chain by waypoint +8/+c coordinates (`0x543df6..0x543e48`): first match supplies +4,
no match stores 0, and absent owner stores -1. The structural reader leaves every nonzero-owner key **unresolved**,
even if a matching ride header exists. Duplicate raw positions are legitimate structural data, not duplicate waypoint identities.

Chunk 3's `0x545890` creates station/grid waypoints; chunk 4's `0x54b2f0` appends derived waypoints
(`0x54bae5..0x54bb49`). These are distinct 0x1c records at 0x877b78, not the raw 0xac records.
Chunk 6 (`0x5439bb..0x5439e4`) scans from the head to the first flag-4 waypoint, **always advances once**, wraps a null
successor to the head, and skips flag-2 candidates. The first scan has no wrap/null guard in the original.
No reference index can be obtained from chunk-5 record order. Geometry-specific waypoint reconstruction, safe handling
of malformed chains and the vehicles' runtime restore remain future work; the structural reader does not fresh-walk a map.

### Track import (`Level.ImportOriginalTracks`)

The loader `0x543560` reads chunk 2 (the build stamp: the time `15:25:02` and date `Mar 17 2000` strings, 32 bytes with the header), then per chunk: 3 builds the ride's
controller through `0x545890` (the handle, the station cell `+0xB4`/`+0xB8`, the orientation `+0xB0`, then the words `+0x10`, `+0x1C` (performance), `+0x14`, `+0x18`, the duration `+4` ("Set Ride Duration to %d") and the
phase `+0x50`; the handle the constructor gives must equal the saved one), 4 hands each section's type word and corner to the section builder `0x54B2F0` (which rebuilds the buoy and waypoint chain), 5 reads a vehicle (the 0xAC-byte
record, not a controller record: its `+0x34`/`+0x38` place a vehicle on its tile, `+0x7C` is its aimed waypoint), 6 selects the reference waypoint (the first flag-4 buoy, then the next that is not flag 2: the open routine
`0x544840`'s choice), 7 and 8 add guests to the ride's leaving and pending lists (`0x54ABD0`, `0x54AA80`) and 9 links riders' heads. The track itself is the structure layer of the World block: the 31-byte track cells (the base
`0x4D0B30` and the segment word `0x50C200`: `SaveCell.StructureMask`, `StructureTile`, `StructureSegment`), the kart tiles (type 11, station 13), the water tiles (16, station 18) and their type-12 extension cells; type 25 (`0x19`) is a hoarding post.

The import writes the saved track cells (the ride of a tile is the one whose station reaches it through the masks), walks each ride's circuit from them (`WalkTrack`), compares it with chunk 4's section list and applies chunk 3's laps,
performance and phase. What is not imported (vehicles, riders and the pending and leaving lists; the words `+0x10`/`+0x14`/`+0x18`; a racing or over phase of a kart or bumper ride) is reported. No save of the corpus or Easymode holds
a ride or a track cell, so the import is tested against the saver's shapes and OpenTPW's own tracks (`TrackAddOnLevelTests.OriginalImport`) and not against a real Theme Park World track.

