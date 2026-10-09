# PowerPC evidence: format decoder mechanics

2026-10-09. Static reading of the Feral Mac PowerPC build (Sim Theme Park);
the original program was not run. Original `.data` files, the HFS image,
extracted assets, expanded sections and disassembly stay outside this
repository. Facts below are **Mac-build facts** unless a PC/Patch 2 variant check
is stated; Mac static evidence alone does not prove PC runtime behaviour.

## Reproduce

From the repository root, with Python 3 and no third-party packages:

```sh
python3 -m unittest discover -s tools/ppc-analysis/lanes/formats -p 'test_*.py' -v
python3 -I tools/ppc-analysis/lanes/formats/format_witness.py /Users/sander/server/game-assets/mac-feral/bin
python3 -I tools/ppc-analysis/lanes/formats/corpus_check.py /Users/sander/server/game-assets/theme-park-world/Data
python3 -I tools/ppc-analysis/lanes/formats/corpus_check.py /Users/sander/server/game-assets/theme-park-world-patch2/Data
# C# parser (synthetic) and corpus cross-check of the decoders in "Implemented in the parser"
dotnet test source/OpenTPW.Tests --filter "FullyQualifiedName~Md2|FullyQualifiedName~ObjectVertexAnimation"
OPENTPW_GAME_PATH=/Users/sander/server/game-assets/theme-park-world dotnet test source/OpenTPW.Tests --filter "FullyQualifiedName~Md2|FullyQualifiedName~ObjectVertexAnimation"
# two dynamic vertex buffers read back from Metal (macOS)
OPENTPW_NATIVE_SHADER_TESTS=1 dotnet test source/OpenTPW.Tests --filter "FullyQualifiedName~NativeDynamicVertexBuffers"
```

- `format_witness.py` refuses any container whose SHA-256 differs from the pins
  below, then makes 353 checks of instruction fields, branch conditions, relocated TOC slots, literal
  constants and label strings at the offsets cited here, and prints interpreted
  JSON (labels, widths, constants, offsets). It never executes code and does not
  print bytes or instruction text. It bypasses `analyze.py` and its heuristic
  function boundaries; every function start cited here is an export transition
  vector, a direct `bl` target or both.
- `corpus_check.py` cross-checks the code-derived models (`models.py`) against a
  private PC `Data` tree using its own bounded DWFB/RefPack/zlib reader and prints
  counts only (about 50 s).
- `models.py` restates each decoder path in Python with the code offset in its
  docstring; synthetic tests pin the semantics, including an instruction-level
  emulation of the packed-vertex conversion that must agree with the model on
  2,008 words.

| Container | SHA-256 |
| --- | --- |
| `SimThemePark.data` | `04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5` |
| `libraries/engine_shared.data` | `c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b` |
| `libraries/ltms_shared.data` | `2b0f7ac92c1f8b67761d271dd5832fca1bd19a8dd8d494b7b89b6ffa693e6ee8` |

Offsets are section offsets (code = section 0, data = section 1). TOC bases:
`SimThemePark` 0x8000 (main vector), `engine_shared` 0x8000 (export vectors).
All Mac loaders byte-swap little-endian files, so the Mac build consumes the same
byte layout as the PC files.

## Variant checks

| Input | Mac HFS copy vs PC baseline | Patch 2 |
| --- | --- | --- |
| `levels/jungle/Easymode.TPWI` | byte-identical (`6d89303d…`) | not changed |
| `levels/jungle/rides/wateride.wad` (both 207.201 members) | byte-identical (`77c6a09f…`) | not changed |
| `levels/*/terrain.wad` `base.map`, `terrain.map`, `base.MD2` | decoded members identical (77/80, 92/95, 103/106, 73/76 members equal; only three `.wct` differ) | not changed |
| MD2 corpus invariants below | — | all counts identical (2,118 members) |

The Mac `data:Language:American` fonts differ from PC English files (locale),
so BF4 statements below are code facts, not corpus comparisons.

## MD2 (M3D2)

### Loader gating (engine_shared `LoadM3D2Header`, code 0x3f9d8)

The loader opens the file (`"rb"`), reads it whole, byte-swaps the header,
checks magic `0x1CD15D46` (`addis −7377` / `cmplwi 23878` at 0x3fc38), then gates
on the major word (+4) and minor word (+8). Status labels are printed as
`"%s (Label)"` from a table at code 0x44101:

| Condition (in order) | Label | Result |
| --- | --- | --- |
| magic mismatch | BadFileType | NULL |
| major > 221 | OldCode | NULL |
| major < 221 and argument bit 1 clear | DeadMesh | NULL |
| argument bit 2 clear and trailer pointer (+0x98) ≠ 0 | Master is Anim | NULL |
| argument bit 2 set and trailer pointer = 0 | Anim is Master | NULL |
| trailer and minor > 203 | OldCode | header returned, trailer pointer and header flag 0x20 cleared |
| trailer and minor < 203 | DeadAnim | header returned, trailer pointer cleared, header word 0x30 zeroed |
| no trailer and major < 221 | OldMesh | header returned |
| otherwise | Ok | header returned |

The application imports it through one glue stub with exactly two call sites:
0x58f04 passes flags 0, 0x99ad8 passes 2 or 6. **No call site sets bit 1**, so
in this build every major-207 member, including `wr_tunnel.md2` and
`wr_tunnelm.md2` (Mac copies byte-identical to PC), is refused with `DeadMesh`
before any layout is interpreted. OpenTPW's rejection of 207.201 matches the Mac
loader; no 207.201 layout needs to be supported for parity with this build. Model:
`models.load_status`; corpus: 2,116 `Ok`, 2 `DeadMesh` (baseline and Patch 2).

### Header/trailer field roles from the swap and relocation code

Header u32 fields 0x00–0x14, 0x2C and 0x30 and u16 0x34–0x4A are swapped;
0x40bc8 relocates 0x4C–0x7C, 0x98 (trailer) and 0xAC (a table swapped with the
same routine as animation position headers, 0x40664); 0x9C is overwritten with
the header's own address at load.
Trailer (72 bytes): words 0–2 u32, 12–28 nine u16, 32–68 pointers relocated by
0x4123c. The trailer helpers (call site → callee, pointer, count) establish:

| Trailer pointer | Count | Helper | Content |
| --- | --- | --- | --- |
| +32 (word 8) | u16 +12 | 0x40664 | position headers (16-byte) |
| +36 (word 9) | u16 +14 | 0x41604 | scale keys (`u16 tick, u16, 3×f32`), 192 files |
| +40 (word 10) | u16 +16 | 0x4171c | rotation keys (20-byte) |
| +44 (word 11) | u16 +18 | 0x413b4 | 64-byte records |
| +48 (word 12) | u16 +24 (word 6 low half, previously "opaque") | 0x412e0 | **texture-frame tracks**: 8-byte `{u16 slot, u16 count, ptr→count × (u16 tick, u16 frame)}`, 229 files |
| +56 (word 14) | u16 +26 | 0x417c4 | node list (u16) |
| +52 (word 13) | — | relocated only | zero in corpus |

Texture-frame tracks: the clip update 0xa56f8 calls 0xa4160 only when
**trailer word 0 bit 0x2** is set (`bt eq` skip at 0xa5768) and global option bit
0x8 (+16396 of the global block) is **clear** (`bf eq` skip at 0xa5778). 0xa4160
scans each track from its last key backwards and takes the first key with
`tick ≤ trunc(time)` (so of two equal ticks the later entry wins; none found →
slot unchanged); when the frame differs it stores it in that slot's 8-byte
runtime entry (array pointed to by instance +80) and sets the entry's dirty bit
0x400. Corpus: trailer word 0 bit 0x2 is set exactly in the 229 files that have
tracks (1,278/1,278 agree); 459 tracks (4 with a repeated tick), 458 index a
slot of the paired base model with every frame below that slot's frame count
(one track has no in-range slot).

Record (64 bytes) pointers relocated by 0x41574: +24 position header, +28
rotation keys, +32 scale keys, +36 path-parameter block, +44 0x10000 block, +48
u16 list (count u16 +22), +52 easing curves. +40 (vertex block) is relocated by
0x41a98 only after the 0x4000 test; +60 is swapped but not relocated and +56
is left untouched.

### Record sampler (SimThemePark 0xa4f68)

Inputs: player state (float time +32, float duration +28), instance (the
geometry M3D2 header), node state (the record's runtime +60 word: a mesh or
dummy record of that header), record. If time > the **player's** duration
(clip duration, not the record's +12) the record is skipped. Then, in order:

- **0x20000 node-flag toggle list** (`u16` at +22 entries of signed `i16` at
  +48): scanning from the last entry backwards, the first entry whose
  `|value| ≤ trunc(time)` (unsigned compare; −32768 never matches) decides; a
  positive entry **clears** node-state bit 0x10, a zero/negative entry **sets**
  it; none found → unchanged. Corpus: 2,536 lists, all sorted by magnitude and
  `|value| ≤` record duration. What bit 0x10 controls is not traced (often read
  as visibility).
- **0x1000 vertex animation**, only when 0x4000 is clear → 0xa4a58 (below) with
  the node state as its object (`mr r3, r27` at 0xa5054) and add mode = header
  flags (+0x30) bit 0x4 ("relative animation", see "Instance, node state and
  clip bind"); afterwards the header gets flag 0x00040000 (0xa5070). 0x4000
  records (30 in corpus) are not handled by this sampler.
- **0x8 rotation** keys (kind 2, stride 20, wrap flag set) → optional easing →
  0xa820c.
- **0x80 scale** keys (kind 4, stride 16, no wrap) → per-component linear.
- **0x1 position**: header kind bit 2 → Bézier (0xa85ac, base index
  `3·key+1`); kind bit 8 → linear (0xa88fc); otherwise 0xa89f0. Result is
  written to the node translation, or added when header flag 0x4 is set.
- **0x200 path parameter** (used when 0x200 is set, the position flag 0x1 is
  clear and the node state's bit 8 is set): block +36 is
  `{u32 start, u32 count, u32, ptr→count f32}`; within `start ≤ time ≤
  start+count` the sampled value `v` becomes `fmod(1000 + v, 100)` percent along
  a spline selected through the header 0xAC table, evaluated with the same three
  evaluators.
  0x400 additionally derives an orientation (0xa3b50) from a normalised vector
  from 0xa8758/0xa8994 or, for the third kind, 0xa89f0 at fraction + 0.1. The
  table and node-selection details are only partly traced.

**Key search** (0xa3ff0, kinds 1/2/4 = strides 4/20/16): `tick = trunc(time)`
via the runtime double→unsigned routine 0x1c3fbc (negative → 0). It takes the
last key with `key.tick ≤ tick`; if none, the channel is left unchanged. At the
last key a wrapping search pairs with key 0, otherwise with itself at
`tick + 1`. Fraction = `(time − tick₀)/(tick₁ − tick₀)`. Rotation interpolation
(0xa820c) then **overrides** the partner index to `min(i + 1, count − 1)`, so a
final rotation key holds. Model: `find_key`, `rotation_pair`.

**Easing** (0xa50d4): an index of 0xFFFF means none; otherwise the 8-byte curve
at `+52 + 8·index` is evaluated as `s = fraction × 8.999995` (single precision,
literal at TOC 0x5198), segment `i = trunc(s)`, local `s − i`, linear between
`0 / b0` (i = 0), `b[i−1] / b[i]` (1 ≤ i ≤ 7) or `b7 / 1.0` (i ≥ 8), bytes
divided by 255.0. This proves the "(0,0), eight samples, (1,1)" reading and adds
the 8.999995 scale. Model: `ease`.

**Rotation interpolation** (0xa820c): with global option bit 0x2 (`+16396` of
the global block at data 0x1577c0) **clear** it calls table-sine slerp 0xa7fc8
(threshold 0.001 on `1 ± dot`, no sign flip of the second quaternion); with the
bit set the components are blended linearly, `(1 − t)·a + (t·b)`. Either result goes through 0xa7ef8, a
quaternion-to-matrix conversion using `s = 2/|q|²`, which normalises the blended
quaternion implicitly. The runtime value of the option bit is not established.

**Bézier** (0xa85ac): with `k = trunc(t)`, `s = t − k`, `i = base + 3k`, the
cubic Bézier over `P[i−1], P[i], P[i+1], P[i+2]` (indices modulo the point count
in header u16 +4). With `base = 3·key + 1` this is exactly `P[3k] … P[3k+3]`,
confirming the existing `P0, (C, C, P)…` reading. Model: `bezier`.

### Instance, node state and clip bind (identities)

- **Instance = the geometry M3D2 header.** The geometry loader 0x99aa4 passes
  model object +4 to `CMesh::Init(tag_sM3D2Header *)` (0x99b84/0x99b88); both
  record-sampler call sites take the instance as model object +4 (0xa573c,
  0xa7238). The fields the runtime uses are header fields of `ModelFile`: +0x30
  flags (+48), +0x42 node count (+66), +0x44 mesh count (+68), +0x50 texture-slot
  table (+80; the texture-frame tracks write its 8-byte entries, the same
  entries materials point at), +0x70 mesh records (+112, 160 bytes) and +0x74
  dummy records (+116, 88 bytes).
- **Node state = that header's mesh record** (dummy record past the mesh
  count; 0xa5a08–0xa5a2c). So the vertex array at node state +96 is the mesh
  record's vertex block (`VertexOffset`, count u16 +88), stored in blocks of four
  (48 bytes), and group 0's vectors at +120/+132 overwrite the record's stored
  bounds fields (`Mesh.BoundsMin/BoundsMax`). A scratch corpus check (not in the
  checker) found the tick-0 box equal to the stored bounds in 0 of 1,735 blocks
  and containing them in 811, so the stored bounds are not the animated box.
  Which renderer or culling code reads the bounds was not traced. After a vertex
  pass the record gets flag 0x00010000 (0xa4f44); its consumer was not traced.
- **Relative animation (header flag 0x4).** The ride loader 0x58a3c strips the
  flag (clears 0x4, sets 0x01000000) and prints `"Ride %s has relative animation
  incorrectly set / Stripping flag, animation will not work!"` unless its
  argument has bit 0x20000 (`bf eq` at 0x58bd4). Its constant call sites pass
  0x50120 (0x29180), 8 (0x4da20, 0x4da44) and 0 (0x59704); 0x596b0 passes a word
  built by 0x594c8 that gets 0x20000 only from bit 0x01000000 of its r7 flag word
  (0x595f8), whose source in ride data was not traced. So in this build set
  mode is the default and add mode needs that explicit flag. Corpus: 27
  geometry members carry 0x4 (coaster pylons, carts, track), 31 vertex clips pair
  with them, and no object-catalog model has it.
- **Clip bind 0xa5894** (target and base header, each as object +8 → +4): for
  every record of the clip being replaced it clears node-state flags 0x00800000
  (with header flag 0x4) or 0x00F40000 (without), which includes the
  static-group/cursor-reset flag. Channels the replaced clip animated but the
  new one does not (`old & ~new` per node, 0xa5aa8) are copied back from the
  base header: matrix rows for 0x289, the vertex block (`(n + 3)/4 × 48` bytes
  via `memcpy`) plus the four bounds words for 0x1000, and the array at +104
  (count +94, i.e. texture coordinates per corner) for 0x10000. The last is a
  lead that the 0x10000 block animates texture coordinates; it is not proven.
- The record +60 binding itself (where a clip's records are pointed at the
  header's records) was not found among direct stores; the sampler and the bind
  routine agree on the record layout above.
- 0xa4a58 computes `modf(time)` and a decremented integer part on entry
  (0xa4a88–0xa4abc) that nothing reads.

### Animation clock: 30 ticks per second (proven for this build)

- 0xa6484 (update): `time = speed × (30.0 × (now − start) / 1000.0)` with
  `speed` at player +12, `now/start` integer milliseconds at +20/+16; literals
  30.0 and 1000.0 at TOC 0x516c/0x5170.
- 0xa6398 (set clip): duration = `(float)(clip+8 − clip+4)` (trailer words 2 and
  1; word 1 is 0 in all trailers); a start time in ticks is converted back with
  `1000 × t / 30 / speed`. 0xa65a0 returns remaining time as
  `(duration − time) × 33.333332` ms.
- `now`/`start` come from one of two scene clocks refreshed by 0xa6f70 into the
  global block (+16400 and +16408; player flag 0x40 selects +16408). Both derive
  from `LbTime_GetClock` deltas accumulated into doubles; the +16400 chain can be
  frozen (+44/+48 of its object) or scaled by a double rate (+24 of the 0x127cd0
  object). The timer lane documents `LbTime_GetClock` as milliseconds on its
  fallback route.
- One caller (0x4f888) starts a clip with speed 1.0.

So, at speed 1.0 and an unscaled scene clock, MD2 ticks advance at 30 per
second. The game-speed scaling and pause policy of the scene clock and clip
looping at the end are runtime dependencies (not traced).

### Vertex animation (record +40)

**Quantised groups (flag 0x4000 clear; 1,736 records).** Block (44 bytes):
`u16 flags, u16 groups, u16, u16 count2, u16` (+10 pad), `ptr groups` (+12),
`ptr pairs` (+16, `count2 × (u16, u16)`), `vec3 offset` (+20), `vec3 scale`
(+32). Group (20 bytes): `u16 keys, u16 vertices, ptr→u16[vertices] vertex
indices, ptr→u16[keys] ticks, ptr→keys×vertices packed words, u32 runtime key
cursor`.

- Packed word (engine 0x41cac swap + application extraction): signed 10-bit
  X/Y/Z in little-endian bits 0–9/10–19/20–29; bits 30–31 discarded.
  `value = q × scale + offset` per axis (0xa446c `fmadds`).
- Packed words are **key-major**: the row of key `c` starts at
  `packed + 4·c·vertices` (`mullw` at 0xa436c).
- Dispatcher 0xa4a58 (object = node state): while node-state flag 0x00800000
  is clear it first resets every group's cursor (+16) to 0; then per group the
  cursor advances while `ticks[c+1] < time` (no upper bound: a time past the
  last tick reads beyond the array); fraction
  `(time − ticks[c])/(ticks[c+1] − ticks[c])` in single precision, so a time on a
  key tick stays on the earlier segment with fraction 1 and a time before the
  first tick extrapolates. Values are `cur·(1 − t) + (next·t)` (`fmuls` then
  `fmadds`). Destination is the node state's position array (+96, the mesh
  record's vertex block), laid out like the stored blocks (`48·(v/4) + 4·(v%4)` for X, +16 Y, +32 Z); add mode
  adds, else sets.
- Group 0 is always handled by 0xa468c and uses **both** of its vertices
  (words 0 and 1 of each key row): `lerp(v0) − scale − 0.25` → node state
  +120..+128 and `scale + lerp(v1) + 0.25` → node state +132..+140 (set or
  add). Corpus: group 0 lists exactly `(positions, positions + 1)` (1,735/1,735
  paired), `v0 ≤ v1` on every axis at every key (63,465 keys), and every key of
  another group at a group-0 tick lies between them (713,695 animated and
  7,308 static vertex keys, none outside). So group 0 is a per-key bounding box
  padded by one quantisation step plus 0.25, written over the mesh record's
  stored bounds fields (see "Instance, node state and clip bind"); their reader
  was not traced. (b0935b4 described only vertex 0 as an instance
  translation; that was wrong.)
- Header flag 0x2: group 1 is static (key 0, no interpolation), applied only
  while node-state flag 0x00800000 is clear; a set-mode pass then sets that flag
  (0xa4ddc), so cursors persist and the static group is not re-applied until the
  clip is replaced (0xa5894 clears the flag on the replaced clip's nodes). Corpus: flags are 1 (1,338) or 3 (398);
  static groups have 1 (365) or 2 (33) keys.
- Corpus: fields +4/+6/+8 and the +16 pointer are zero in all 1,736 (the
  dispatcher does not read them); animated groups (group 0 included) have ≥ 2
  keys with strictly increasing ticks starting at 0, and each reaches the clip
  end (1,736/1,736), so sampling never runs past a tick array within a clip; the
  latest group tick equals the record duration (1,736/1,736); packed bits 30–31
  are zero; no vertex index repeats. A scratch check (not in the checker) found
  static-group key 0 within one quantisation step of the paired base position
  for 89% of 6,945 vertices, so the static group is not simply a copy of the
  base mesh.

**12-byte variant (flag 0x4000 set; 30 records, `plane_anim` etc.).** Block
`{ptr, u32 count, ptr→count × 3 u32}`; the first pointer is relocated but not
swapped. Its consumer is not in the record sampler and was not found.

**0x10000 block** (record +44, one 20-byte header): `{u32 n, ptr→n × (u16, u16),
u32 m, ptr→u16[m], ptr→2m u32}`. Consumer not traced.

## MAP (TP2M terrain attributes)

- 0x107f1c builds a chunk-file reader with file tag `TP2M` and registers a
  `CMapChunkHandler` (RTTI name at code 0x1ce3a5) for chunk `MAP `.
- Handler 0xd7e6c reads width (+0) and height (+4) of the chunk payload, then
  cells from +28. **The five values at +8…+24 are stored to stack slots that are
  never read** (raw +8 at −60; swapped +12…+24 at −64, −76, −88, −100): the Mac
  loader ignores them. All five PC fixtures hold `8,8,8,8,8`.
- For file row `r` (slow axis) and column `c`: `x = r − originX`,
  `y = c − originY` (globals at data 0x54860 +1348/+1352); cells outside
  0–127 are skipped; the byte is stored at `cells[(y·128 + x)].+38` (68-byte
  map cells). This confirms row → X, column → Y and adds the origin offset.
- `+38` of the map cell is the field the save writer serialises as
  `mStatusFlags` (0xd8144 passes cell +38), so the MAP attribute byte is the map
  cell's status flags.
- 0x107f1c has one caller (0x4db74), with the path `"%s:Terrain:Base.map"`.
  No code path names `terrain.map`: the jungle `terrain.map` is not loaded by this
  build's terrain loader. (Absence is bounded to this single loader.)
- Bit 0x04: a bounded search for loads of `+38` followed by a bit test found only
  a 0x08 test (bit tests at 0x74d0c/0x74df4). The 0x04 consumer remains
  unresolved.

## TPWS/TPWI save payload

### Section framing is a **trailing delimiter** (correction)

The save writer 0x11cb60 calls each subsystem writer and **then** writes its
4-byte tag (byte-swapped constant; e.g. `WRLD` is stored as `DLRW`), logging
`"<Name>: saved %d bytes"`. The bytes *before* a tag belong to that tag's
subsystem. `docs/TPWS-PAYLOAD.md` currently reads each tag as the start of the
following data; that attribution is shifted by one section.

| Delimiter (file bytes) | Writer | Block (Easymode bytes) | Notes |
| --- | --- | --- | --- |
| — | 0x10bc1c action recording | 8 + 1,171 | `u32 mLoadedPublishedPark`, `u32 recording_size`, recording |
| `DLRW` | 0x105d3c World | rest of the 1,495,462-byte prefix | world vars, control manager, macro AI, map, things |
| `CSPS` | 0xb7f54 Scripts | 5,452 | starts `TPCS` |
| `TRAP` | 0x9c2c0 Particles | 76,252 | starts `LCTP` |
| `SSEM` | 0x116a20 MessageCentre | 266 | |
| `KOLC` | 0x10e944 Clock | 8 | two 4-byte timer fields (values consistent with milliseconds) |
| `TNAV` | 0x127ac8 VanillaTime | 4 | raw `LbTime_GetClock()` at save time (the value previously called "pointer-like") |
| `SYSG` | 0x1c2e00 GameSystem | 36 | 4-byte fields |
| `SYSR` | 0x5ae70 RideSystem | 17,526 | the placed-object records attributed to "SYSG" today |
| `KART` | 0x1a424 TrackRides | 44 | chunk headers `{1,12,44}`, `{2,32,32}`, then build `__TIME__` + `__DATE__\0` (PC file: `20:25:29Oct 21 1999`; the Mac writer emits its own build stamp) |
| `RYLF` | 0x674bc FlyingRides | 452 | starts `FLY_` |
| `ESSR` | 0xb3868 RSSE scripts | 10,856 | starts with its own `RSSE` |
| `EMAK` | 0x2cb90 Camera | 40 | 3×4 + 12 + 12 + 4 |
| `SAOC` | 0x394b8 Coasters | 16 | 4×4 |
| `SVDA` | 0x78c8 (tag `ADVS`) | 372 | starts `ADV_`; log label reuses "Coasters" |
| `NUOS` | 0xbbad0 Sound | 1,445 | `cat_*` paths |
| `STHC` | 0x10e5a8 + 0x10e788 Cheat | 2 | |
| `CSDA` | 0xcfd4 / 0xe834 AdvisorScoring | 4 | |
| (none) | 0x18f738 | 4 (zero) | final untagged block; log label reuses "UI" |

Payload offset 0 is the action record, so the "untagged prefix" is action
recording + World, not a separate header. Model: `split_sections`.

### Labelled schemas (debug labels passed to the save helpers)

Helper widths (bytes written by each helper's `LbFile_Write`): 0xbefc 2,
0xc90c/0xc7f8/0xc6e4/0xcd1b0/0x10a4a4/0xcdf5c/0xd8ecc 4, 0xcabc4 1,
0xcde50/0xcdd44/0xce070 2, 0xcd704 12.

World vars (Easymode offset 1,179–1,243): `version` 4 (=2),
`mArrivalVehicle_Size1..3` 2 each, `mBankAccount` 2, `mCurrentArrivalVehicle` 2,
`mGameTick` 4 (=755), `mMechanicHQ`, `mParkAnalyser` 2, `mParkClosed` 4,
`mNumberOfVisitorsToDate` 4, `mParkGates`, `mTrafficLights` 2, `mRandomSeed` 4,
`mResearchLab`, `mStaffHQ`, `mTagSystem`, `mUIMsgReceiver`, `mWeather` 2 each,
`mWorldState` 4, `mFirstHandyman`, `mFirstMechanic`, `mFirstEntertainer`,
`mFirstGuard`, `mFirstResearcher`, `mFirstObject` 2 each. The 2-byte fields hold
small distinct thing indices in the fixture (e.g. gates 11, lights 12). Then
`mControlManager`, `mMacroAI`, `mMap`, `Used Thing Head/Next` and `thingmodel`
blocks follow (not decoded here).

**Cell grid** (map writer 0xd6c90, 16,384 cells, X-fastest): per cell a status
byte (bit 1 map cell, bit 2 track cell, bit 4 region-effect cell) followed by the
flagged sub-records. Shared cell base (29 bytes): `mDirection` 1, `mFlags` 2,
`mMeshInstance` 4, `mNeighbours` 1, `mOverlapCounter` 2, `mParentID` 2,
`mTileData` 12, `mType` 4, `mHoardingNeighbours` 1. Map cell = base +
`mLitter` 4, `mLitterCollector` 2, `mLitterScript` 4, `mLitterScript` 4,
`mPylonIndex` 2, `mStatusFlags` 1, `mTimeMarkedForLitterCollection` 4, `mWho` 2
(52). Track cell = base + `mSegmentNumber` 2 (31). Region-effect cell =
`mpEffect[5]`, 5 × u16 (10). Hence 1 + 52 + 31 = **84** and +10 = **94** bytes —
the two observed record sizes. This names the previously opaque offsets:

| Record offset | Field | Former observation |
| --- | --- | --- |
| +0 | status (3 = map+track, 7 = +region effect) | "type byte" |
| +2 | map `mFlags` low byte | 0x40/0x00 regions |
| +8 | map `mNeighbours` | path connection bits |
| +11/+12 | map `mParentID` (u16) | "+12 ≠ 0 occupied" (49 nonzero cells) |
| +13, +17, +21 | map `mTileData` bytes 0, 4, 8 | path bit, PathTex index, orientation |
| +25 | map `mType` (u32) | 1 = path on exactly the 78 tile-bit-0 cells; 2 = water (240) |
| +46 | map `mStatusFlags` | MAP attribute byte |
| +56 | track `mMeshInstance` | "running index" |
| +77 | track `mType` | 12/25 fence-like values |
| +82 | track `mSegmentNumber` | `FF FF` |
| +84… | `mpEffect[0..4]` | the 11×11 camera patterns are `mpEffect[3]` |

`corpus_check.py` parses all 16,384 cells from 6,765 to 1,385,521 with this
model (16,134 × 84 + 250 × 94). The meaning of individual status/flag bits and
of the other subsystem blocks remains open.

## MTR

No Mac container references MTR: no `.mtr`/`.MTR` string and no `AF 15 59 2E`
magic in either byte order in any code/data section of all 16 containers, and the
Mac data volume has no `.mtr` file (it does ship `bankrupt/congrats/paused.MD2`).
Static evidence that the Mac build never loads MTR; a PC runtime file-access
trace would still be needed for the PC build.

## BF4 fonts (ltms_shared)

- `TbIRLE4bitFont` constructor (0x2f80) swaps the glyph record as u16 +0, u16
  +2, u32 +4, u32 +8, u32 +12, u16 +16, u16 +18, leaves bytes +20/+21, swaps u16
  +22. This confirms OpenTPW's widths (encoding is a u32 at +12; offsets are
  bytes; advance is 16-bit); +2 is an unused/unknown u16.
- `TbOneColour4BitFontRenderMethod::DrawCharTo8888` (0xb41c): with colour
  alpha 255, each channel becomes `D + q`, `q = mulhwu(0x88888889, n·(C − D)) >>
  3`. For `C ≥ D` this is exactly `floor(n·(C − D)/15)`, i.e. the same weight
  `n/15` as OpenTPW's `n × 17 / 255`. For `C < D` the unsigned multiply makes the
  result one coverage step short (255 → 17, not 0, at full coverage). Other
  alpha values first scale `n` by `trunc(n·A/255)` (0x80808081 division). Which
  render method/pixel format the game selects at run time was not traced, and
  the PC build's font code was not readable, so PC equivalence is unverified. Model:
  `bf4_blend_channel`.

## TGQ/TQI movies

The Mac build has no TQI decoder: it imports QuickTime (`NewMovieFromFile`,
`StartMovie`, …), names `Data:Movies:<name>.mov`, and its data volume ships
`.mov` files (plus `bfj.mov`, `BFj2.mov`). No `pIQT`/`SCHl` constant occurs in
any container. The PC `TP.ICD` (baseline and Patch 2) keeps section names such as
`IDCT_DAT`, `TQIA_DAT` and `LBMPEG_D`, but its `.text` and `.data` are
high-entropy (7.99 and 7.34 bits/byte) and were not decoded. The residual IDCT
rounding therefore cannot be resolved from either binary statically here.

## Implemented in the parser (`source/OpenTPW.Files/Formats/Model`)

Parser and sampling helpers; the runtime use is in the next section.

| Proof above | C# |
| --- | --- |
| quantised block, groups, key-major packed words | `ModelVertexAnimation`, `ModelVertexGroup` (`ModelAnimationTrack.VertexAnimation`) |
| signed 10:10:10, fused `q·scale + offset` | `ModelVertexAnimation.Unpack`, `Dequantise` |
| cursor search, `cur·(1 − t) + next·t` | `ModelVertexGroup.FindKey`, `ApplyAnimatedGroups(time, positions, add)` |
| group 0 padded lower/upper vectors | `SampleBounds` |
| static group (key 0) | `ApplyStaticGroup(positions, add)` |
| texture-frame tracks and their gate | `ModelAnimation.TextureFrameTracks`, `TextureFramesEnabled`, `ModelTextureFrameTrack.FrameAt` |
| 0x20000 toggle list | `ModelAnimationTrack.NodeFlagToggles`, `SampleNodeFlag` |
| easing scale 8.999995, `(1 − s)·lo + (s·hi)` | `ModelAnimationTrack.Ease`, `EaseScale` |
| key search: before the first key → unchanged (null); last rotation/scale key holds | `SampleTranslation/Rotation/Scale` (the player substitutes the stored node component for null) |
| scale `(1 − t)·a + (t·b)` | `SampleScale` |
| set-mode pose (static key 0, then animated groups) and its preconditions | `ModelVertexAnimation.ApplyPose`, `GetPoseLimitation` |
| header flag 0x4 | `ModelFile.RelativeAnimationFlag` |

Explicitly unsupported (not decoded, reported by `ModelAnimationTrack.UnsupportedFlags`,
1,122 corpus records): the 12-byte vertex layout (0x4000), the 0x10000 block,
0x2000 and the path parameters (0x200/0x400). `HasUndecodedPayload` keeps its
earlier meaning ("more than rigid tracks"). Rejected as malformed: a missing
bounds or static group, a group-0 vertex count below 2, an animated group with
fewer than 2 keys or non-increasing ticks, a static group without keys,
non-finite offset/scale, and pointers or counts outside the payload. Sampling a
vertex group past its last tick, NaN times, and negative times for the TRS
channels throw instead of guessing. The C# corpus test checks 1,736 decoded
blocks, 1,735 paired (virtual pair, coverage, whole-clip sampling, 713,683
bracketed animated keys and none outside), 2,536 toggle lists, 1,278 clip gates
and 459 texture-frame tracks; these agree with `corpus_check.py`.

## Runtime integration (objects)

`ObjectAnimator` keeps per instance one position array per mesh and fills it
from the winning clip's vertex track (same most-recent-channel rule as the node
matrices, an OpenTPW choice) with `ApplyPose` at the channel tick; without one
the mesh shows its stored positions. Equivalence argument: when
`GetPoseLimitation` is null the static and animated groups write every position
of the mesh, so the array equals the original's after its set-mode passes since
the bind, and the base copy on clip replacement (0xa5894) equals showing the
stored positions. The per-bind cursor is replaced by a fresh search from key 0,
which is the original's result for times that do not decrease since the bind; at
a sandbox loop wrap this assumes the original rebinds (its loop policy is not
traced). Played: 599 of the 609 catalog clips that carry vertex tracks; the 10
others hold only the 12-byte layout (23 tracks) and are listed in
`VertexLimitations` with the stored mesh shown. Also listed instead of guessed:
relative-animation models, blocks that do not list every position once, and
groups that end before the clip (none in the catalog).

`ObjectRenderParts.WritePositions` maps positions through the corner order into
the part's own vertex array (engine axes; stored normals kept), and
`OriginalObject` refreshes a fixed-size `Dynamic` vertex buffer
(`Model.UpdateVertices`) through the frame command list only when the mesh's pose
version changes. The parsed `ModelFile` is shared through the model cache and is
never written. Tests: two Belly Bounce instances at ticks 30 and 105 hold
different poses, each equal to a direct sample; the shared positions are
unchanged; each `Build` gets its own arrays; stopping one instance restores its
stored mesh only; a Metal readback of two dynamic buffers keeps each instance's
upload over three frames with no buffer growth. Native jungle smoke (baseline
and Patch 2 data) passes, 3,808 frames each; it does not assert vertex-animated
pixels.

Not wired, because the runtime binding is not proven: group-0 bounds (no
consumer of the mesh-record bounds traced), node-flag toggles (bit 0x10's
consumer), texture-frame tracks (the header slot entries are identified, but the
engine's frame-to-texture use and option bit 0x8 at run time are not), the
0x10000 block, add mode, and normals after a vertex pass.

## Code replacement handoffs (root to verify and integrate)

1. **MD2 animation runtime**: vertex tracks are wired for objects (section
   above; `docs/MD2-MODELS.md` updated). Still open: texture-frame tracks to
   texture slot frames, the 0x20000 bit, group-0 bounds, once their consumers
   are known. Keep 30 ticks/s but cite this proof. Gate PC claims behind a PC
   capture.
2. **Fidelity register / RIDES-001**: tick rate is proven (Mac, speed 1.0);
   loop policy, scene-clock scaling/pause and trigger mapping remain open.
3. **TPWS** (`SavePayloadLayout`, `TPWS-PAYLOAD.md`, importer naming): treat tags
   as trailing delimiters; rename sections by owner (RideSystem holds the placed
   objects); adopt the cell schema and world-var names; the importer's offsets
   (+8, +11/12, +13, +25, +46, +82) keep working but can use field names.
4. **MAP**: document `OpaqueHeaderValues` as ignored by the original loader;
   `terrain.map` unused by the Mac terrain loader.
5. **MTR**: mark "not loaded by the Mac build".
6. **BF4**: optional exact-blend mode reproducing the `C < D` lag; current alpha
   mapping is weight-equivalent for `C ≥ D`.

## Unresolved, with exact dependencies

- Consumers of the mesh-record bounds (+120/+132, written by group 0) and of
  node flag 0x00010000; where record +60 is bound to the header's records; the
  ride-data source of the 0x594c8 flag word that permits relative animation.
- Degenerate inputs absent from the corpus and not modelled: a single-key
  rotation track (the wrapping search divides by zero, so the original
  produces NaN), times past a vertex group's last tick, and position-time
  entries whose upper u16 is nonzero (the search reads `lhz` at +0 of each
  4-byte entry; all upper halves are zero in the corpus).
- OpenTPW rejects animation members whose tables together read more than four
  times the payload (shared spans counted each time) and checks each span before
  allocating. The Mac loader has no such check; the limit is a resource bound
  for malformed or modded input, and no corpus member comes near it.

- 12-byte vertex variant (flag 0x4000), 0x10000 block and node-state bit 0x10:
  consumers not found; need a traced caller of these record fields. The node
  list (trailer +56) is read by 0xa65b8, which sets bit 0x10 in listed node
  records whose type word has bit 31 clear; what bit 0x10 controls is not traced.
- Path parameter (0x200/0x400) and header 0xAC table: selection via node +82 and
  the third evaluator 0xa89f0 only partly read.
- Clip end/loop policy, game-speed scaling, pause: owners of player +12/+16/+20
  and the scene-clock rate field (0x127cd0 object +24) at run time.
- Rotation interpolation and texture-frame gating: runtime values of global option bits 0x2 and 0x8 (+16396).
- MAP bit 0x04 and the remaining cell/status bits; World sub-blocks after the
  world vars; all other subsystem payloads.
- PC equivalence of everything above: PC `TP.ICD` code is not readable
  statically; a PC runtime capture is required before PC-only replacements.
- TGQ IDCT rounding: needs readable PC TQI decoder code or player captures.
