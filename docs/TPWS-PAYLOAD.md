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
| Attraction records | 5 in each TPWS, one per fixed item (Bus, Seaplane, Ferry, Traffic Lights and the *601 park object), none in INTS; all three gauges are 100 in all 35 records |
| Economy | Bank prefix, 8 loan offers equal to the theme's `LoanInfo`, and the full challenge list of `ChallengesInThisLevel` (8–10 records); see docs/ECONOMY.md for the corrected challenge layout |

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
- splits the payload at the 17 delimiters in writer order. A block whose writer
  width is traced ends exactly there, like the original loader: Clock 8 (two
  clock words, `0x10e944`), VanillaTime 4 (`0x127ac8`), Camera 40 (`0x2cb90`),
  and an empty Coasters block is exactly its 16-byte count and globals (the
  World block is checked by its thing list, below). The other delimiters may be
  any later copy of their four bytes, and the payload must split in exactly one
  way. A copy of a block's delimiter bytes is harmless only inside an earlier
  block or where taking it as that block's end leaves no valid split of the
  rest (a later delimiter missing or out of order, or a traced width broken).
  A copy inside the block itself or any later block that forms a second valid
  split throws, for example the block's own delimiter bytes inside it with
  every later delimiter still in order, or the AdvisorScoring (`ADSC`) bytes
  anywhere in the trailing UI block. A missing or out-of-order delimiter, a
  broken width and a payload holding one delimiter's bytes more than 256 times
  throw as well. The count-based original loader accepts the ambiguous cases
  (SAVE-011);
- reports the 19 blocks (offset, length, delimiter offset); UI is everything
  after the AdvisorScoring delimiter. No partial result on error.

Blocks other than World and RideSystem stay raw, size-checked byte spans — except the Coasters
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
(4–8: base, raw person bytes, the 105-byte staff suffix, role suffix and its next
ID), `SaveStaffHq` (9), `SaveAdvisorController` (11: eight 21-byte pending
records, three raw words, 351 history entries, the current record),
`SaveResearchLab` (14, records raw), `SaveBank` (16) and `SaveChallengeSystem`
(19). Guests (1), mechanic HQ (10), tag system (12), park analyser (13), weather
(15) and UI receiver (17) are walked by width and not decoded.

### Cell grid (verified structure)

Each record is 84 bytes: type byte at +0 (3 or 7), opaque fields, the **MAP
attribute byte at +46**, and `FF FF` at +82/+83. Type bit 0x04 (value 7) appends a
10-byte extension after `FF FF` (all 250 type-7 records, and only they, are 94
bytes). Records are X-fastest: record `k` is game cell (`k % 128`, `k / 128`).

`SaveCellGrid` locates the grid structurally because the control manager and
macro-AI widths before it are not pinned: exactly one offset in the World block
after its variables must begin a run of exactly width × height valid records
(validity = type 3/7 and `FF FF` at +82), within 16 MiB. In Easymode that run
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
| 4 × 3 | `mRideScriptHandle`, `mTrackRideHandle`, `mState` |
| 24 | `mTopLeft`, `mEntryPos`, `mNext`, `mAssignedStaffMember`, `mBackOfQueue` (u16), `mCanLoad` (u32), `mExitPos`, `mFirstInQ` (u16), `mIsTrackRideValid` (u32), `mUpgradeParent` (u16) |
| 6 history buffers | each `u32` head, `u32` count, `u8` flag, `u32` first, then count × `u32`; `mNumCustomers` follows the second buffer and `mNumWalkAways` the third |
| 1 + 1 + 4 + 2 | `mOperatingCapacity`, `mOperatingDuration`, `mOperatingSpeed`, `mPersonBeingLoaded` |
| 6 × 4 | `mCostOfGoods`, `mQualityOfGoods`, `mChanceOfWinning`, `mPricePerUse` (the loader clamps it to 0–500), `mAmountOfSpecialIngredient`, `mQueueSizeInCells` |
| 3 × 4 | three `float32` gauges, read into object offsets +0x48, +0x44, +0x40. +0x40 is the state of repair and +0x44 the life gauge of the wear and breakdown code. The original writes each as a whole number 0–255 |
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

The RSSE block (before the `RSSE` delimiter, earlier read as "`RYLF`") starts with its own `RSSE` and a header containing the value 14, followed by 14 length-prefixed object
names and `data\levels\jungle\...` archive paths (Traffic Lights ×2, Fountain,
Small Toilet ×3, Staff Room, Security Camera ×2, Litter Bin, Coconut Kiosk, Jungle
Spray Sideshow, Bouncy Dino, Gates), each ending `OBJ ` plus raw heap pointers and
script-VM-like values. They match the RideSystem list but are not decoded.

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
    +21/+25/+29 f32       section+220/+224/+248 (meanings unqualified)
    +33 u8   link count   (narrowed from section+176), then that many u16 ordinals,
                          each its own 2-byte read like the original
  train/car/passenger payload (serializer 0x38da4) — layout not qualified, kept raw
```

`SaveCoasterBlock.Parse` (payload + layout + map dimensions) bounds and validates what the traced
contract states and refuses everything else: truncation inside the header, auxiliary block, section
record or links (named per part); a coaster or auxiliary-link count above its bound; a non-finite
power factor or section float; a section cell outside the map; and any block that lists more than one
coaster, because the extent of each coaster's undecoded train payload is unknown, so the next header
cannot be located (SAVE-920). The flag words are kept raw — the remapping is not recovered (SAVE-921)
— as are the unnamed controller words, globals, auxiliary ordinals, `section+112` and the floats
`+220/+224/+248` (SAVE-922). The train payload is preserved as raw bytes and never interpreted
(SAVE-923). A block with count 0 is exactly its 16 bytes (`SavePayloadLayout` enforces this on the
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
  `OPENTPW_TPWS_SAVES` was unset for this slice; its coaster content was not reverified. The
  nonempty shape is therefore **not** proven on Theme Park World bytes.
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
object's price, cost of goods, upgrade level, state of repair and life gauge
(also for objects whose footprint is unverified), staff (type, grade, training percentage, happiness, state, hire time), the park
open flag, and the advisor's pending advice and history of bound messages. Every
value read but not imported is reported with its SAVE approximation (SAVE-001 to
SAVE-010 in the fidelity register): the world generator state, guests, staff
names/jobs/positions/energy/idle ticks/role suffixes, the hiring pool, the
challenge system's timing, switch bytes, Value and ActualThing, each
attraction's state, chance of winning, third gauge, totals, name lines,
operating capacity/duration/speed, quality of goods, queue size, customer and
walk-away counts, time stamp and mesh instance ID, research, unmapped bank
words and the loan offers' Available flags, the staff HQ's training budgets,
strike words, strike levels and trained byte, the world state,
arrival variables and bytes, the calendar session start, the advisor
controller's presentation words and current record, the weather, park analyser,
tag system, mechanic HQ and UI receiver things and the opaque blocks. Staff
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
- The coaster train/car/passenger payload (0x38da4), the flag remapping, the auxiliary
  ordinals' meaning and the unnamed header/section words; bodies with more than one coaster
  are refused until the train payload's extent is known (SAVE-920).
- The control manager and macro AI before the calendar, the guest, analyser and weather bodies, record
  fields marked raw above, the extension data, RideSystem record bodies/lengths, RSSE object bodies and every
  other block.
- Saves with known ride wear to confirm the attraction gauges against the original wear rule.
- Money/time/guest state needs known-state reference saves before import.
