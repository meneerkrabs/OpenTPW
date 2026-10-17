# Decoded TPWI/TPWS payload: block layout

Evidence date: October 10, 2026. Read-only analysis of the decoded BILZ payload
(see SAVE-CONTAINER.md). The payload is split into its 19 writer-order blocks by
the 17 trailing delimiters; the World block is parsed through its labelled
variables, the **per-cell grid** and the **thing list**, which must close exactly
at the World delimiter; the **placed-object records** of the RideSystem block are
found by signature. The traced block and thing layout is
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
file type `00 01 22 19`, version, online flag, `BILZ` header and zlib stream.
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
| Container | Magic 500, file type `00 01 22 19`, version 133; INTS uses the same container as TPWS |
| Markers | All 17, in the Easymode order. Decoded payload 1,560,526–1,579,807 bytes |
| Untagged prefix | 1,469,256 bytes (every INTS) or 1,473,869–1,475,452 (TPWS), against 1,495,462 in Easymode: the prefix length is not fixed |
| Cell grid | Found once, starting at 5,729 (INTS) or 6,259–6,512 (TPWS); 16,384 × 84 bytes, no extension records; +46 equals the theme's `base.map` |
| Path cells | The 10 MAP `InitialPath` cells, nothing else |
| SYSG | No placed objects; fixed items 5 (TPWS) or 2 (INTS), all with their theme's Info.Ids (*600–*604) |

These checks ran with the earlier leading-tag marker reader ("prefix" = action
recording + World block, "SYSG" = RideSystem block). The writer-order block
split and the thing-chain walk have not been run on this set yet.
| Attraction records | 5 in each TPWS, one per fixed item (Bus, Seaplane, Ferry, Traffic Lights and the *601 park object); an INTS holds two attraction things (Gates, Lights), which decode once the initial calendar and empty history ring are accepted (docs/SAVE-THINGS.md, "The restart saves"); all three gauges are 100 in all 35 records |
| Economy | Bank prefix, 8 loan offers equal to the theme's `LoanInfo`, and the full challenge list of `ChallengesInThisLevel` (8–10 records); see docs/ECONOMY.md for the corrected challenge layout |

Initial state of the INTS saves (an unplayed park): the macro-AI calendar is the constructor's (`0x4F7E80`: start 2000-01-01, month cache 0,
day cache -1, rate 15000, park turn 0) and every attraction history ring is the empty ring of `0x51ADE0` (current entry -1, 30 entries, no wrap).
The loaders (`0x4F7F30`, `0x4E2C10`) take both unchecked; `SaveWorld.Parse` and `SaveAttraction.Read` accept exactly these two states besides the played ones.
OpenTPW's Restart Park does not reload the snapshot (SAVE-012).

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
  (`'ADV_'`, two counted buffers), Sound `0x51c400` (the settings, and with sound on a chunk the loader skips by its
  length word), Cheat 2 (`0x405570`), AdvisorScoring 4 (`0x59c860`) and UI `0x481680` (`u32 count`, count x `0x21c`).
  The Jungle Easymode save's seventeen widths all agree with these readers. An empty Coasters block is exactly its
  16-byte count and globals. The World block is the loader's own readers
  (`SaveBlockWidths.TryMeasureWorld`: the 64 variable bytes, the control manager 4,806, the macro AI 670 + 28 + 18, 0x4000
  status-byte cells of 1 + 52 / 31 / 10 bytes by status bits 0 / 1 / 2, and the self-closing thing list) and agrees with the Jungle
  Easymode save to the byte. Only a Coasters block with a coaster (its trains and cars depend on the coaster type's counts) and a World block that does
  not read that way (a synthetic payload) are located by their delimiter's bytes, and the payload must split in exactly one way there; a copy of those delimiters
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
| 6,719–6,746 | Calendar (`0xe3d30`): FunnyTimeStart 8, SessionStart 8, month cache 4, day cache 4, rate 4 (15,000) |
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
park analyser (13), weather (15) and UI receiver (17) are walked by width and not
decoded.

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
| 8 × 4 | `tv[t]`: game-time stamp year, month, day, hour, minute, second, two opaque values |
| 4 + 2 | `MeshInstanceID` (110–125 in Easymode), `mFlags` |
| 33 × (2 + 2) | `mNameA[i]`, `mNameB[i]` interleaved: the two UTF-16 sign lines ("Belly"/"Bounce", gates "LOST"/"Kingdom") |
| 4 × 3 | `mRideScriptHandle`, `mTrackRideHandle`, `mState` (+0x19c: 0 working, 1 broken down, 2 upgrade requested, 3 not choosable, 4 condemned; SetState `0x4e0e60` closes the object for 1, 2 and 4) |
| 24 | `mTopLeft`, `mEntryPos`, `mNext`, `mAssignedStaffMember` (+0x5e, the claiming staff thing), `mBackOfQueue` (u16), `mCanLoad` (u32, the open word +0x68), `mExitPos`, `mFirstInQ` (u16), `mIsTrackRideValid` (u32), `mUpgradeParent` (u16) |
| 6 history buffers | each `u32` `mCurrentEntry`, `u32` `mNumEntries`, `u8` `mWrappedAround`, `u32` `mTemp` (today's running value), then count × `u32`; `mNumCustomers` (+0x1a0) follows the second buffer and `mNumWalkAways` (+0x1a4) the third. The rings are, in order, the daily costs (+0xf8), takings (+0x70), customers (+0x1a8), walk-aways (+0x230), visits (+0x2b8) and the visits' averaged happiness change (+0x340); the day message 0xb rolls each into its ring (`0x4dd320`) |
| 1 + 1 + 4 + 2 | `mOperatingCapacity`, `mOperatingDuration`, `mOperatingSpeed`, `mPersonBeingLoaded` |
| 6 × 4 | `mCostOfGoods`, `mQualityOfGoods`, `mChanceOfWinning`, `mPricePerUse` (the loader clamps it to 0–500), `mAmountOfSpecialIngredient`, `mQueueSizeInCells` |
| 3 × 4 | three `float32` gauges, read into object offsets +0x48, +0x44, +0x40 on the Mac and +0x4c, +0x48, +0x44 on the PC (`0x4db7d0`): the third gauge, the life gauge and the state of repair of the wear and breakdown code. The original writes each as a whole number 0–255 |
| 4 × 5 + 1 | `mRequestedService`, `mTimeMarkedForMaintenance`, `mTotalCosts`, `mTotalTakings`, `mUpgradeBalloonSprite`, `mUpgradeLevel` (u8) |

`SaveAttraction.Read` parses the body completely and rejects it outside these bounds: non-zero Info.Id;
angle a multiple of 90; a valid month/day/hour/minute/second; history buffers of at most 31 entries with
flag 0/1; price at most 500; gauges finite in 0–255; upgrade level at most 3. `SaveAttractionList` returns
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
RideSystem block therefore describes scene meshes rather than game-object state. Kind 826 appears in four functions
(`0x1006e3d4`, `0x1006e630`, `0x1006e8bc`, `0x1006eb84`) that have not been read yet; its records sit nested
inside the Belly Bounce RideSystem record, on the queue cells.

## RideSystem placed-object records

The RideSystem block (writer `0x5ae70`, the bytes before the `RSYS` delimiter;
earlier called "SYSG") contains variable-length records that begin with `u8 1` followed by
`u32` Info.Id, X, Y, width, height, kind, index; a `u16` rotation in degrees sits
38 bytes after the Info.Id. `SaveObjectList` finds them by signature (valid
coordinates, 1–16 cell size, kind 815/865, rotation multiple of 90, increasing
index); the importer then cross-checks footprints against grid occupancy. The
Info.Id matches the `Info.Id` in the object's `.sam`:

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
before `SAOC`. The reader is read-only; no original coaster chunk is written.

```
u32 coaster count          (linked-list count)
u32, u32, u32              three globals, meaning unqualified
per coaster:
  32-byte header:
    +0  u32  remapped controller flags
    +4  u32  controller+8
    +8  u16  controller+12      +10 u16 controller+16
    +12 u16  controller+20      +14 u16 controller+28    +16 u16 controller+32
    +18 f32  controller+44      (the power factor, 0 or 1)
    +22 u16  section count      (nodes excluding descriptor flag 0x10)
    +24 u16  controller+244     +26 u16 controller+240
    +28 u32  controller+320     (riders on board)
  auxiliary block: u32 count, then count × u16 ordinals
    (the loader reads it before any section record, into the expected third node's
     +180 link slots; the saver writes it where its node walk meets ordinal 2)
  per section (the header's count times):
    +0  u32  remapped section+8 flags      +4 u32 section+112
    +8  u16  cell X       +10 u16 cell Y   (the descriptor's +12/+16 grid coordinates)
    +12 u8   cell-stack insertion ordinal
    +13 f32  section+232  (height parameter t)   +17 f32 section+216 (bank)
    +21/+25/+29 f32       section+220 (incline), +224 (yaw), +248 (loop rotation, loop nodes only)
    +33 u8   link count   (narrowed from section+176), then that many u16 ordinals,
                          each its own 2-byte read like the original
  per train slot of the coaster type (type +0x2e8, uiMaxTrains):
    27-byte record: u32 train flags, u8 car count (+0xc), u32 +0x74 (the reference car's position, f32),
                    u32 +0x78 (speed, f32), u32 +0x38, u32 +0x64, u32 +0x68, u16 riders (+0x10)
    per car of the train (type +0x2f4, uiMaxCars):
      11-byte record: u32 car flags, u8 count A (+0x30), u8 count B (+0x34), u8 +0x44, u32 mesh instance (+0xc)
      then count A u32 (riders on the lap) and count B u32 (guests seated at the station)
```

`SaveCoasterBlock.Parse` (payload + layout + map dimensions) bounds and validates what the traced
contract states and refuses everything else: truncation inside the header, auxiliary block, section
record or links (named per part); a coaster or auxiliary-link count above its bound; a non-finite
power factor or section float; and a section cell outside the map. The train payload's extent depends
on the coaster's type (the file carries neither the train slots, `uiMaxTrains`, nor the cars per
train, `uiMaxCars`: the loader `0x437b60` reads them from the live type, `0x436d10`), so the caller
may pass each coaster's `SaveCoasterTrainLayout`, keyed by the header's controller `+0x10`: with it
`SaveCoasterTrain.ReadAll` decodes the train slots and cars exactly as the loader reads them, and
a block of several coasters reads (`OriginalParkImport.Import` takes the type's counts from a
callback and `OriginalPark` supplies them from the level's coaster `.sam`, finding the station as the
saved attraction whose mesh instance equals the controller word); SAVE-920 is narrowed to the runtime
restore, which is still refused. Without a layout only a single coaster's
train payload stays raw (the rest of the block) and a block with more coasters is refused, since the next
header cannot be located. The controller word `+0x10` is the station's mesh instance id and `+8` the controller
state word (low nibble: 1 incomplete, 2 built, 4 open, 8 closed). The section flag word follows the saver's
and the loader's maps (`SaveCoasterSectionFlags`, `0x437615..0x4376e0`, `0x437ea6..0x437f58`): wire bits 0x1, 0x2, 0x4
(manual yaw, incline, bank), the cut markers 0x40/0x80, the link marks 0x100/0x200 and the intersection marks
0x400/0x800 are restored, while 0x8, 0x10 and 0x20 (the loop-pair, loop and zero-bank node flags) are
derived again by the node constructor (the loader runs the loop setup `0x442cd0` for 0x8 and picks the loop
rotation float for 0x10). The section words are named: `section+112` is the node's mesh instance (re-attached
through `0x463ab0`), `+216` bank, `+220` incline, `+224` yaw, `+232` the height parameter and `+248`
the loop rotation (setters `0x444150`, `0x4441d0`, `0x444270`). The header's remaining track flags
(SAVE-921), controller words (SAVE-922) and the train words `+0x38`, `+0x64`, `+0x68` and the car flag and
byte `+0x44` (SAVE-923) are kept raw. A block with count 0 is exactly its 16 bytes (`SavePayloadLayout` enforces this on the
split). `OriginalParkImport.Import` runs the reader on every save and exposes the result as
`CoasterBlock`, with `ObjectsAt`/`AttractionsAt` cell queries and `CoasterSections` joins that require both cell
and object `Info.Id` to attach attraction ids. Missing or ambiguous scene matches remain visible;
header words and scene indices are never guessed as station ids. Only recognized scene kinds
815/865 participate: kind 1018 pylons are not decoded by the existing scene parser.
`ParkObjects.ImportOriginal` calls `RequireSupportedCoasterRuntime` before creating any objects:
a nonempty original TPW controller is explicitly refused, even with no saved sections. Empty TPW
parks continue through the existing importer. Structural inspection remains available; no runtime
track, controller or train state is reconstructed. The separate TPI viewer is unchanged.

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
  count 1, globals 1, 0, 2; header power factor 1.0, one section, zero riders; one section record
  with no links, cell (65, 67), stack ordinal 0, height parameter ≈ 0.0556, and a 1,140-byte train
  tail. Its park's RideSystem list places a 6×6 coaster station (Info.Id 1180) and 1×1 pylon records
  (Info.Id 1181, kind 1018) — the saved section's cell (65, 67) carries its own such record. The raw kind-1018 pylon header is pinned separately because the existing scene parser excludes it. That is
  a real-data cell association observation, but it is Theme Park Inc evidence: no Theme Park World proof, and no
  Theme Park Inc coaster chunk is inferred anywhere in production code (the reader runs on it only in
  a labelled test).
- Which section is the station node is **not** qualified (the saver may exclude station nodes like
  any descriptor-flag-0x10 node), so the reader makes no station claim; consumers join by cell.

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
differs from the map: wrong level), reads the RideSystem records (rejecting any
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
exposes it as `CoasterBlock`, with `ObjectsAt`/`AttractionsAt` cell joins for consumers; no coaster
state is imported. Imported: park turn
and date, bank balance, admission fee, annual profit and bought loans, each
object's price, cost of goods, upgrade level, state of repair, life gauge and walk-away count
(also for objects whose footprint is unverified), staff (type, grade, training percentage, happiness, state, hire time), the park
open flag, and the advisor's pending advice and history of bound messages. Every
value read but not imported is reported with its SAVE approximation (SAVE-002 to
SAVE-010 in the fidelity register): guests, staff
names/jobs/positions/energy/idle ticks/role suffixes, the hiring pool, the
challenge system's timing, switch bytes, Value and ActualThing, each
attraction's state, chance of winning, third gauge, totals, name lines,
quality of goods, queue size, customer
count, time stamp and mesh instance ID, research, unmapped bank
words and the loan offers' Available flags, the staff HQ's training budgets,
strike words, strike levels and trained byte, the world state,
arrival variables and bytes, the calendar session start, the advisor
controller's presentation words and current record, the weather, park analyser,
tag system, mechanic HQ and UI receiver things. The raw dumps of the original's own engine structures (the Scripts, RSSE, Particles, Sound, message-centre, Track ride and Flying ride blocks) are
not restored: OpenTPW's engines restart from the archives (`[EXT:original-engine-state-dumps]`, formerly part of SAVE-010). Staff
values outside OpenTPW's ranges (happiness outside the traced 0–100, a 100 %
training percentage, a state without a STAFFSTATES entry, a hire time outside
the calendar or inside a park turn, which is rounded down to the turn) are adjusted and each adjustment is reported; an attraction whose
values are out of range (including a negative cost of goods) keeps OpenTPW's
defaults and is listed. The saved advisor's pending advice and history are
dropped, with an evidence line, when no automatic advisor is attached.
Path styles, queues, extension data and object models are not imported.

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
missing/truncated/ambiguous runs), RideSystem scanning and footprint rules,
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
`OriginalSaveCorpusTests` runs container, block split, grid, thing chain,
RideSystem, attraction and economy readers over the second fixture set
(inconclusive without `OPENTPW_TPWS_SAVES`; not run for SAVE-I); since the coaster slice
`Import` parses every save's Coasters block there too.

## Remaining gates

- An online (LAYS) save; LAYS payloads are unsupported.
- A non-empty TPWS: the second fixture set has no placed objects, extension records or
  extra paths, so those readers are still proven on Easymode alone. A Theme Park World
  save with a built coaster does not exist in any available corpus: the Coasters block's
  nonempty shape rests on the static Mac contract and the Theme Park Inc cross-check.
- The meaning of the coaster header's track flags and controller words and of the train words
  `+0x38`, `+0x64`, `+0x68` (SAVE-920 to SAVE-923); the train layout is decoded from the loader and the saver but
  no Theme Park World save with a coaster exists to check it on real bytes.
- The control manager and the macro AI before the calendar (their widths are traced, their bodies are not), the guest, analyser and weather bodies, record
  fields marked raw above, the extension data, RideSystem record bodies/lengths and the meaning of the unnamed RSSE struct words.
- Saves with known ride wear to confirm the attraction gauges against the original wear rule.
- Money/time/guest state needs known-state reference saves before import.
