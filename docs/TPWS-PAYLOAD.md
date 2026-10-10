# Decoded TPWI/TPWS payload: marker layout

Evidence date: October 9, 2026. Read-only analysis of the decoded BILZ payload
(see SAVE-CONTAINER.md): the 17 section markers, the **per-cell grid** in the
untagged prefix and the **placed-object records** in `SYSG` are decoded where
cross-format evidence supports them; everything else stays opaque. A read-only
importer (`OriginalParkImport`) uses only those parts. This is not save writing.
The layout was found on one fixture (Easymode) and later checked on eleven
private TPWS/INTS saves ([Second fixture set](#second-fixture-set)). No original executable was run.

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
- Untagged prefix (first 1,495,462 bytes): `58521ff5b0804eeea19358ac68cc3fc219babb8bb3672e3687c892c21d75e3e5`
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
| Attraction records | 5 in each TPWS, one per fixed item (Bus, Seaplane, Ferry, Traffic Lights and the *601 park object), none in INTS; all three gauges are 100 in all 35 records |
| Economy | Bank prefix, 8 loan offers equal to the theme's `LoanInfo`, and the full challenge list of `ChallengesInThisLevel` (8–10 records); see docs/ECONOMY.md for the corrected challenge layout |

The balances (about 1.2 billion) come from the cheat, so money is not a reference value.
Named saves and their autosaves decode to the same layout; only small sections differ.

## Section markers

The payload has no observed tag+length chunk framing. What is verified:

1. Static bytes of the original `TP.ICD` (inspected, not executed) contain one
   contiguous 68-byte table of 17 four-character values, in this order:
   `DLRW CSPS TRAP SSEM KART RYLF ESSR EMAK KOLC TNAV SYSG SYSR SAOC NUOS SVDA STHC CSDA`
   (bytes as stored; read as little-endian uint32 multi-character constants
   they spell e.g. `WRLD`, `SPSC`, `PART`, `MESS`).
2. Each of those 17 byte sequences occurs **exactly once** in the decoded
   Easymode payload, at unaligned offsets, all after a large untagged prefix.

Observed layout. "Span" is the distance to the next marker including the 4
marker bytes; it is derived, not a length field read from the file.

| Marker | Offset | Span | Notes (observation only) |
| --- | --- | --- | --- |
| (prefix) | 0 | 1,495,462 | Untagged: opaque header, per-cell grid, opaque tail (see below) |
| `DLRW` | 1,495,462 | 5,456 | Immediately followed by bytes `TPCS` |
| `CSPS` | 1,500,918 | 76,256 | Followed by `LCTP`; contains 16-byte names such as `WaterFall`, `tButton`, `Tag2` |
| `TRAP` | 1,577,174 | 270 | |
| `SSEM` | 1,577,444 | 12 | |
| `KOLC` | 1,577,456 | 8 | |
| `TNAV` | 1,577,464 | 40 | |
| `SYSG` | 1,577,504 | 17,530 | |
| `SYSR` | 1,595,034 | 48 | Third uint32 is 44 (= span − 4); ends with ASCII `20:25:29Oct 21 1999` |
| `KART` | 1,595,082 | 456 | Followed by `FLY_` |
| `RYLF` | 1,595,538 | 10,860 | Contains `PAD_` fill, length-prefixed object names/paths ending `OBJ ` |
| `ESSR` | 1,606,398 | 44 | |
| `EMAK` | 1,606,442 | 20 | |
| `SAOC` | 1,606,462 | 376 | Followed by `ADV_` |
| `SVDA` | 1,606,838 | 1,449 | Contains length-prefixed `\data\...\cat_*` sound/speech/music paths |
| `NUOS` | 1,608,287 | 6 | |
| `STHC` | 1,608,293 | 8 | |
| `CSDA` | 1,608,301 | 8 | Last 4 payload bytes are zero |

Payload order differs from table order (`KOLC TNAV SYSG SYSR` precede `KART`;
`SVDA` precedes `NUOS`). Markers are not followed by a consistent size field
(only `SYSR` happens to carry a matching value). Sub-tags such as `TPCS`,
`LCTP`, `FLY_`, `ADV_` and an unrelated `RSSE` inside `RYLF` are not in the
table and are not treated as sections. The `SYSR` text has `__TIME__`/`__DATE__`
shape and the date matches the ISO file date (1999-10-21); its meaning is not
established. `KOLC`, `SSEM` and `TNAV` hold pointer-like words (e.g. `KOLC` =
`0x06D9D0BC`), not a clock or date. Section names and subsystem roles are not
encoded in code beyond `SYSG` object records below.

## Untagged prefix

| Range | Content |
| --- | --- |
| 0–6,764 | Opaque header (starts `u32` 0, 1171, 231; contains runs of `0xCD` fill) |
| 6,765–1,385,520 | Per-cell grid: 16,384 records (16,134 × 84 bytes + 250 × 94 bytes) |
| 1,385,521–1,495,461 | Starts with `u32` 42, 41, 1 and holds 16.16-looking values near the entrance; contains the thing list with the 14 **attraction records** at 1,392,971–1,410,393 (see below); the rest is opaque |

### Cell grid (verified structure)

Each record is 84 bytes: type byte at +0 (3 or 7), opaque fields, the **MAP
attribute byte at +46**, and `FF FF` at +82/+83. Type bit 0x04 (value 7) appends a
10-byte extension after `FF FF` (all 250 type-7 records, and only they, are 94
bytes). Records are X-fastest: record `k` is game cell (`k % 128`, `k / 128`).

`SaveCellGrid` locates the grid structurally because the header is not decoded:
exactly one offset in the prefix must begin a run of exactly width × height valid
records (validity = type 3/7 and `FF FF` at +82), within the first 16 MiB. In
Easymode that run starts at 6,765 and ends exactly where the `u32 42` tail begins.

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

## Attraction records (thing list in the prefix tail)

Evidence: the Mac PowerPC build's object serializer (`0x100daf04` in `SimThemePark.data`, static
analysis, see [reverse/RIDE-WEAR.md](reverse/RIDE-WEAR.md)) reads every field with `LbFile_Read` and then
byte-swaps it, so the save format is little-endian on both platforms. The Mac disc ships a byte-identical
`Easymode.TPWI` (same container SHA-256), so the Mac loader reads exactly this file. Field names below are
the original serializer's name strings.

The tail of the prefix holds a list of game objects, each stored as `u32 handle, u32 class, body`. Class 3
is the attraction class. It covers rides, shops, sideshows, features and the fixed gates, lights and bus.
Its body is fixed at 1,091 bytes while the history buffers hold 30 entries:

| Bytes | Fields |
| --- | --- |
| 2 + 2 + 4 | base object: position X, Y in 1/256 cell (buildable objects: cell = position / 256, equal to the SYSG cell; fixed items store 128, 128), two opaque `u16` |
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

`SaveAttractionList` finds class-3 records by signature. A candidate must parse completely inside the
prefix and pass these bounds: handle 1–65535; non-zero Info.Id; angle a multiple of 90; a plausible
timestamp; history buffers of at most 31 entries with flag 0/1; price at most 500; gauges finite in 0–255;
upgrade level at most 3. Other classes (observed 2, 7 and 19 between and after the attractions) are not
decoded.

Easymode yields exactly 14 records, handles 28, 23–16 and 14–10. They are the 11 buildable objects and the 3
fixed items of the SYSG list, with the same Info.Ids, and every buildable record has its SYSG cell. All three
gauges are 100, upgrade levels, customers and takings are 0, and the Belly Bounce stores capacity 5,
duration 30 and speed 60. Because this is a freshly designed park it cannot show a worn or broken ride. Two
saves of one park at a known tick distance, with a ride in use, would turn the state of repair into the
oracle for ECON-023.

SYSG `kind` values 815 and 865 are engine mesh-instance types: the Mac code passes them to the mesh creator
(`0x10059f00`, from `0x100dc350`), and the attraction record links to its mesh through `MeshInstanceID`. SYSG
therefore describes scene meshes rather than game-object state. Kind 826 appears in four functions
(`0x1006e3d4`, `0x1006e630`, `0x1006e8bc`, `0x1006eb84`) that have not been read yet; its records sit nested
inside the Belly Bounce SYSG record, on the queue cells.

## SYSG placed-object records

`SYSG` contains variable-length records that begin with `u8 1` followed by
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

`RYLF` starts with `RSSE` and a header containing the value 14, followed by 14 length-prefixed object
names and `data\levels\jungle\...` archive paths (Traffic Lights ×2, Fountain,
Small Toilet ×3, Staff Room, Security Camera ×2, Litter Bin, Coconut Kiosk, Jungle
Spray Sideshow, Bouncy Dino, Gates), each ending `OBJ ` plus raw heap pointers and
script-VM-like values. They match the SYSG list but are not decoded.

## Money (observed, not imported)

The **loan-offer table** starts at 1,411,390 with eight 32-byte records, each containing
available flag, 32-bit amount, APR, months, monthly repayment, bought flag, months repaid and
lender name index. The amount at 1,411,394 is 100,000; its following word is APR 0, not the
upper half of an i64 amount. Lenders need not equal record order. Repayments equal
floor(amount/months), matching `Easy_Standard.sam`; the **challenge list** (eight 45-byte records
at 1,410,409) equals jungle `ChallengesInThisLevel`. Both are located heuristically by
`SaveEconomyRecords` and cross-checked against settings in docs/ECONOMY.md (ECON-045 remains).

Named Mac serializer fields and the actual PC fixture identify the preceding 28-byte bank
block at 1,411,362: admission fee 25, balance 87,987, batch balance 0, withdrawals enabled,
last balance 87,787, entered-red tick 0 and annual profit −12,013. The fields are decoded and
reported; money and active loans are not restored into the simulation by this parser correction.
The PC fixture SHA-256 is `6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`;
the spending history is still unknown.

## Importer (`OriginalParkImport`)

Inputs: decoded payload and the level `base.map`. It parses the marker layout, the
cell grid (rejecting any attribute that differs from the map: wrong level) and
the SYSG records (rejecting any placed footprint outside the map or not occupied
in the grid). Output: path cells, placed objects with footprints, fixed items, and
unresolved records. `OriginalPark.Load` adds `base.MD2`/heightfield and resolves
every Info.Id through the level's object `.sam` files (unknown ids are an error).
**Not imported:** money (see ECONOMY.md for the loan and challenge tables), date/time, guests, staff, research, ride/shop state,
prices, the extension data, path styles, queues and object models.

## Marker reader (`SavePayloadLayout`)

`SavePayloadLayout.Parse( ReadOnlySpan<byte> )` / `Parse( SaveReader )`:

- Payload capped at 64 MiB (same as the decoded-payload cap).
- Searches only for the 17 known byte sequences. Each must occur exactly once;
  a missing marker, a duplicate (ambiguous, e.g. a false match inside opaque
  data) or overlapping markers throw `InvalidDataException`. No partial result.
- Reports payload length, untagged prefix length and sections sorted by offset
  (tag bytes, uint32 value, offset, derived span). Spans tile the payload exactly.
- Any order is accepted; `MatchesObservedEasymodeOrder` reports whether it
  equals the single observed order. Order is not used to validate.
- Contents are not exposed or interpreted beyond these offsets.

## Tests

`TpwsPayloadTests`: synthetic marker layouts (observed and table order, through
`SaveReader`), empty payload, missing/duplicate/false/overlapping markers and the
size cap; the private fixture pins the payload hash, prefix hash and all 17
offsets/spans. `OriginalParkImportTests`: synthetic grids (extensions, X-fastest
order, missing/truncated/ambiguous runs), SYSG scanning and footprint rules,
wrong-map and unoccupied-footprint rejection; the private fixture pins the grid
range, 78 path cells, the connection-bit property, the 11 placed objects and 3
fixed items, and rejection against `terrain.map`. `OriginalParkPlacementTests`
covers Info.Id names and the build rules. `SaveAttractionListTests`: synthetic records in serializer order, class skipping, a case past each upper and lower rejection bound, unbounded opaque words and large counters, and truncation at the prefix end; the private fixture pins the 14 handles, the gauges and the Belly Bounce fields, and matches every buildable SYSG record by Info.Id and cell. Without `OPENTPW_GAME_PATH` the private
tests are inconclusive, not passes. `OriginalSaveCorpusTests` runs container, markers, grid, SYSG,
attraction and economy readers over the second fixture set (inconclusive without `OPENTPW_TPWS_SAVES`).

## Remaining gates

- An online (LAYS) save; LAYS payloads are unsupported.
- A non-empty TPWS: the second fixture set has no placed objects, extension records or
  extra paths, so those readers are still proven on Easymode alone.
- The prefix header and the rest of its tail (thing-list classes other than 3), record fields marked opaque above, the extension
  data, SYSG record bodies/lengths, RYLF object bodies and every other section.
- Saves with known ride wear to confirm the attraction gauges against the original wear rule.
- Money/time/guest state needs known-state reference saves before import.
