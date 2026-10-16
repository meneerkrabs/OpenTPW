# MD2 (Bullfrog M3D2) models: CPU parsing evidence

October 9, 2026. Status: bounded CPU reader for the version 221.203 layout;
geometry, texture slots and node hierarchy are verified against the whole local
WAD corpus. Node matrices are parent-relative (corpus evidence below).
Animation members decode into position/rotation/scale tracks, quantised vertex
groups, per-corner texture-coordinate tracks, node-flag toggles, the 12-byte
vertex block and texture-frame tracks, validated over all 1,278 members; every
record bit is classified (decoded, path parameter, or exporter metadata that the
original never reads). The sandbox Totem plays its original `totemm1.MD2` cycle
and placed objects play their vertex and texture-coordinate tracks. Decoder
mechanics and the 30 ticks/s clock are read from the Feral Mac PowerPC build
(`docs/reverse/PPC-formats.md`, `docs/reverse/ANIM-plan.md`); the PC executable's
equivalence is **not verified** apart from the path sampler, which is read from the decrypted PC executable
(275 of 293 PC WADs with MD2 members are byte-identical to the Mac copies), and original rendering
fidelity is not verified. These are
not Quake II MD2 files. No dependency or original asset is added to the repository.

`ModelFile` (`source/OpenTPW.Files/Formats/Model/`) reads the whole member into a
capped buffer (16 MiB; the largest original member is 1,377,696 bytes), validates
every offset/count before use, leaves caller-owned streams open and supports
short nonseekable reads. Malformed data raises `InvalidDataException`; other
versions raise `NotSupportedException`, which for majors below 221 names the
original loader's own `DeadMesh` refusal (see "Older versions"). The unused `OpenTPW.Files.ModelFile` stub,
which logged fixed offsets from one file, was removed. The renderer-facing API
(`Meshes`, `Mesh.Vertices/Indices/TexCoords/Normals/TransformMatrix/Materials`,
`MaterialData.Name/Flags`) is unchanged except that `Normals` now prefers verified
stored normals; mesh names no longer carry a trailing NUL.

## Verified layout (little-endian, version 221.203)

Header, 0xB8 bytes:

| Offset | Field | Evidence |
| --- | --- | --- |
| 0x00 | magic `0x1CD15D46` | all 2,118 members |
| 0x04/0x08 | version 221/203 (2 members: 207/201, different layout, rejected) | all |
| 0x0C, 0x10, 0x14, 0x2C | opaque words (0x14 looks like a 1999 Unix time; 0x2C is a few bytes below file length) | not interpreted |
| 0x18 | 20-byte source name, NUL-padded (92 differ from the member name) | `SourceName` |
| 0x30 | flags (geometry 1/3/5/9/13/25; animation 0/4/8/16/32/34); bit 0x4 of a geometry model is "relative animation" (Mac sampler adds instead of sets; 27 geometry members, all coaster pylons/carts/track, none in the object catalog); other bits opaque | `HeaderFlags`, `RelativeAnimationFlag` |
| 0x36 | texture slots; 0x38 position blocks (×4); 0x3A UV blocks (×4); 0x3C materials; 0x3E faces | totals re-summed per mesh |
| 0x40 | 16-byte path records (pointer at 0xAC; see "Paths" below); 0x42 nodes; 0x44 meshes | `Paths` |
| 0x46 | opaque (equals mesh count in 254 files, 0xFFFF in 493) | `Unknown46` |
| 0x48 | dummy attribute records; 0x4A normals (corners + faces) | |
| 0x50–0x7C | flag table, slot table, position, normal, UV, material, face regions, 0x6C block, mesh table, dummy table, root node, attribute table | each region bounds-checked |
| 0x80 | bounds min/max, 6 floats | |
| 0x98 | animation trailer pointer (animation members only) | |

Texture slots: 8-byte records (`u32 flags`, `u32 0`) at 0x50, then contiguous
20-byte NUL-terminated `.tga` names, then 16-byte slot records at 0x54
(`u32 opaque 0/1`, `u32 0`, `u16 0`, `u16 frame count`, `u32 first-name pointer`).
124 slots have several frame names. Flag bits (observed 0x1..0x73) are retained raw. tp.exe reads them
(loader 0x462F80, texture setup 0x575E9C): **0x40** = multi-frame (set exactly on the 124 animated slots;
it makes instances copy their texture tables, 0x462FEE), **0x10/0x20** become texture bits 0x10000/0x20000,
which mean *wrap*: on the Direct3D path either bit sets render-key bit 0x400000 (0x590C40, 0x581847), and the
stage decoder 0x567620 sets `D3DTSS_ADDRESS` to WRAP with a linear `MINFILTER` for that bit, else CLAMP with a point
`MINFILTER` (0x5678CF, 0x567912); the address state covers both axes, so either bit wraps U and V (the software
texel path insets only the flagged axis, 0x590422/0x59043B). They select no blend, alpha or depth state.
**0x80** is a procedural texture (not in the corpus). **0x1** and **0x2** have no reader: the runtime model generator
writes them (0x42539E: 0x1 always, 0x2 from its descriptor), and a scan of every load of the slot record (+0x50), the
material pointer (+0x6C) and every bit test of 1, 2 or 3 in 0x440000–0x5A0000 finds none. Transparency in the
original follows the image instead: the image decoder 0x574BD0 turns its header's alpha bits into texture flags
0x100/0x200/0x300 (0x575771), which 0x590C40 maps to the blended render keys 0x2756, 0x556 and 0x200552 (additive);
without them the key is 0x1256, no blend. Material word +0xC
bit 0x1 draws the mesh in one constant colour instead of per-vertex lighting (0x580C51; on signs, fire,
glow, water, flames and lava, i.e. self-lit art).

Nodes: one tree rooted at 0x78. Mesh records are 160 bytes, dummy records 88:
`u32 type`, parent/next-sibling/first-child record pointers, 4×4 float row-major
matrix (translation in row 4), `u32 index`, name pointer. Mesh records continue
with `u16 positions, materials, faces, corners`, then position, normal, UV,
material and face pointers, 6-float mesh bounds at +120 and a corner-table
pointer at +148; the remaining words were zero throughout. The reader requires
every pointer to hit a record start, the root to have no parent/sibling, and a
walk to reach each node exactly once with matching parent pointers.

Geometry mesh totals are checked as each mesh is reached, before its decoded
arrays are allocated. A header-total excess cannot be repaired by later meshes;
this prevents malformed repeated pointers from allocating the same large span
for every mesh before the final mismatch is discovered. The final equality check
still refuses deficient totals. `Md2BoundaryTests` also covers all truncated
geometry prefixes, maximum declared counts and a valid maximum per-mesh vertex
count. This audit uses synthetic fixtures; fresh retail compatibility is deferred.

The [independent allocation review](MD2-ALLOCATION-REVIEW.md) records the red/green
measurement and valid shared-span/indexed-geometry and aggregate boundary checks.

Mesh data (all arrays must lie inside the header region for their kind):
- Positions: blocks of four, X×4/Y×4/Z×4 floats; padding dropped.
- Corners: `u16` position index each; UVs are U×4/V×4 blocks, one per corner.
- Normals: `corners` normals, then `faces` normals. Stored corner normals agree
  with geometric face normals (dot ≈ 1) for 587,194 of 628,560 corner checks.
- Faces: 8 bytes, `u16` (normal index; in `[corners, corners+faces)` for 4,714
  of 4,914 meshes) then three corner indices. `cross(p1-p0, p2-p0)` has dot ≈ 1 with
  the referenced stored normal for 206,872 faces (out-of-range or other dots are rare).
- Materials: 16 bytes, `u32` flag-record pointer (0 = untextured, 3 cases),
  `u16 0`, `u16` face count (matches in 12,947/12,951), `u16` first/last corner,
  `u32` opaque 0/1. Faces never span materials; 2 meshes list the same range twice.

Kept opaque: the 20-byte dummy attribute records (`u32` type such as
0x100000B1, `u32` value, 12 bytes; count differs from the dummy count in 170
files); the 16-byte records at 0xAC are paths (below). The 0x6C block, found only in the four
level `terrain.wad/base.MD2` files (96×85 cells) and lobby `terrain/Base.MD2`
(111×110), is decoded as a terrain heightfield (`ModelFile.Heightfield`: corner
heights, hole/ground-texture-slot cell words; layout and evidence in MAP.md). Header +4/+6 are
the corner and cell counts, +8/+0xC the grid origin (0x452560). Cell-word low bits, from tp.exe:
0x1 hole (no height: 0x452560 returns FLT_MAX; the renderer skips it); **0x4** splits the cell
along (x+1, z)–(x, z+1) instead of (x, z)–(x+1, z+1) in every height lookup (0x452560, 0x4527F0,
0x46A34C), and **0x2**, set on exactly the other surface cells, orders the renderer's corners
(0x56F670) for the same split (`ModelHeightfield.SplitsAlongAntiDiagonal`, `GetHeight`; the terrain
mesh and ground heights follow it); **0x8/0x10/0x20** turn the ground texture by one, two or three
corner steps and **0x40** mirrors it first (0x56F4F0; set 0x8 on 3, 0x10 on 17, 0x20 on 284 cells,
0x40 on about 95 %); 0x80/0x100/0x200/0x400/0x800 are runtime bits never stored (overlay placed,
dirty, footprint scan, darkened land outside the park, bent cell). The high 16 bits index the model's
textures (0x56F443). The texture turn and mirror are not drawn yet. Node types (mesh 0x1/0x401/0x2 empty; dummy 0x200/0x208/0x300/0x600)
are not interpreted.

### Node transform space

Matrices are **parent-relative**, row-vector convention: `world = local × parent
world` (`ModelAnimationPlayer.ComputeRestTransforms`). Evidence:
- Totem: the 12 `HeadNN` seat dummies and `camera01` are children of `tp_cart`
  (local translation 15, −2.34, 15.12) with local offsets of about ±3 units;
  read as absolute they would sit at the ride's corner, away from the cart.
- Header bounds (0x80) versus the box of all mesh positions, for the 173
  non-UI geometry files whose meshes have non-root parents: the composed
  hierarchy matches (within 2% of the extent) in 146, only the uncomposed
  matrices in 5 (`Bbugs` ×2, `bugstv`, `Arc2x3`, `TROUGH_MASTER`; `Bbugs` would
  put its `Sign01` outside the 50×50 footprint), neither in 22.
- Sampling every name-paired animation at 9 ticks keeps all sampled base mesh
  positions inside the header bounds widened by one extent for 1,237 of 1,275
  animations with composition, 1,172 without.
- Terrain: jungle `bridge_top` (local translation (0, −0.1, −15) under dummy
  `bridge01` at (530, 21.1, 575)) only lands on the MAP bridge cells when
  composed with its parent (MAP.md).
- `ui.wad` models match the uncomposed matrices instead (116 vs 7, 42 neither):
  their child nodes are alternative state frames (normal/disabled/highlight/
  pressed) spread one button width apart in the authoring scene, and the header
  bounds are the box of each node's matrix applied alone. They are drawn with the
  root's pose in a 2048×1536 virtual screen (UI.md); this is inferred from the
  data, not from code.

### Normals

Stored corner normals are unit length in 4,811 of 4,914 meshes and lie in the
same hemisphere as every face using the corner for 297,540 of 301,629 corner
uses (65,995 equal the face normal only, 47,479 the full smooth normal only,
55,062 both; the rest look like authored smoothing groups). `Mesh.Normals`
(renderer) uses the stored corner normal when it is unit length and agrees
with every face of the corner, otherwise the recomputed smooth normal. Raw
arrays remain in `CornerNormals`/`FaceNormals`.

## Animation members

1,278 members have all region offsets at 0x50–0x7C zero and a pointer at 0x98
to a 72-byte trailer that ends the file. Header counts and bounds repeat *a*
model of the WAD, not necessarily the target (the nine `spider*` animations
repeat `Pspider`'s 19-node header but address `spider.MD2`'s 65 nodes). Pairing
by the longest geometry member name that is a proper prefix of the animation
name in the same WAD directory binds 1,275 animations with every node index in
range; `Pmegacostm` exceeds `Pmegacost`'s single node and
`c_hade/TROUGH_ROTATE`/`TROUGH_SCALE` have no prefix base. `ModelFile.Clip`
holds the decoded `ModelAnimation`; the raw trailer stays in `Animation`.

Every table pointer is checked against the payload and charged to a budget
before its array is allocated (a vertex group's keys × vertices product reaches
2³² − 2¹⁷ + 1 words). The budget is an OpenTPW resource limit, not original
behaviour: tables may reuse payload bytes, but together they may read at most
`ModelAnimation.TableBytesPerPayloadByte` (4) times the payload. No table in the
PC baseline or Patch 2 corpus reuses bytes; their 1,278 clips read 6,010,956
table bytes, each clip within its own payload (`ModelAnimation.TableBytes`).

Trailer words (u32 unless split):

| Word | Meaning | Evidence |
| --- | --- | --- |
| 0 | flags (0–21); bit 0x2 enables the texture-frame tracks (set exactly in the 229 files that have them) | `TrailerFlags` |
| 2 | clip duration in ticks (1–860) | loop length |
| 3 | u16 position-track count, u16 total scale keys | equal in all 1,278 |
| 4 | u16 total rotation keys, u16 record count | equal in all 1,278 |
| 6 | u16 texture-frame track count, u16 node-list count | `TextureFrameTracks` |
| 8, 10 | first position header / rotation key (informational) | |
| 11 | record table (64 bytes per record) | |
| 12 | texture-frame tracks: 8 bytes `u16 slot, u16 count, ptr→count × (u16 tick, u16 frame)` | `ModelTextureFrameTrack` |
| 14 | node list (u16 node indices; 874 files): the clip start sets node-state bit 0x10 on each listed node whose state bit 31 is clear (0xa67d8 → 0xa65b8) | `NodeList` |
| 15 | first easing table (informational) | |
| 1, 7, 13, 16, 17 | zero throughout | |

Record (64 bytes): `u32` ordinal (= index in all 6,549), `u32` flags, `u32`
opaque, `u32` record duration, `u16` rotation-key count, `u16` scale-key count,
`u16` node index, `u16` opaque (non-zero only with flag 0x20000), then pointers:
+24 position header (flag 0x1), +28 rotation keys (flags 0x08/0x10/0x20/0x40),
+32 scale keys (flags 0x80/0x100), +36 path-parameter block (flag 0x200; below),
+40 vertex-animation block (flag 0x1000, or the 12-byte block with 0x4000), +44
texture-coordinate block (flag 0x10000), +48 small u16 list (flag 0x20000), +52
easing curves; +56/+60 zero (the original binds record +60 to the node at run time). The position,
rotation and scale pointers are present exactly when their flag group is set
(all records; enforced).

Decoded kinds:
- **Position**: 16-byte header `u32` kind, `u16` points, `u16` keys, `u32`
  points pointer, `u32` times pointer; `u32` times (non-decreasing; 4 tracks
  repeat a time = step). Kind 0x18 (431 tracks): one point per key, linear.
  Kind 0x12 (785 tracks): `3 × keys − 2` points `P0, (C, C, P)…`, a cubic Bézier
  per segment in the segment fraction (e.g. Advisor `0 → −9.97 → −43.2 →
  −53.17`). Points are absolute local translations: the first point equals the
  rest translation in 644 of 1,215 paired tracks.
- **Rotation**: 20-byte keys `u16` tick, `i16` easing index, quaternion
  x, y, z, w. All 26,233 are unit length; ticks strictly increase. The first key
  equals the rest rotation in `System.Numerics` convention for 830 non-identity
  rest nodes versus 2 for the conjugate. The exporter keeps sign continuity
  (half-turn keys alternate w = 1/−1; only 8 consecutive pairs have
  dot < −0.01), so playback slerps **without** hemisphere flipping.
- **Easing** (12,451 keys): index into the record's table of 8-byte curves; the
  index applies to the segment starting at that key (never set on a final key);
  every table is exactly `8 × (max index + 1)` bytes (1,768 tables). Bytes are
  the slerp fraction × 255 at s = 1/9 … 8/9: Totem `tp_cog` curves
  31, 66, 104, 142, 177, 208, 233, 250 and 5, 22, 46, 77, 114, 152, 188, 224
  reproduce, within about 1/255, a velocity-continuous Hermite ease-out/ease-in
  from/into the neighbouring constant spin sampled at i/9. Playback
  interpolates linearly through (0, 0), the eight samples and (1, 1). Only
  9,515 of 11,161 moving-segment curves are monotonic; short segments carry
  clamped/quantised values (e.g. `32, 64, 96, 160, 192, 224, 224, 0`, where the
  final 0 may be a wrapped 256), so exact original evaluation is unverified.
- **Scale**: 16-byte keys `u16` tick, `u16` 0, 3 floats; linear (2,832 keys).

Timing (Mac key search 0xa3ff0): before a channel's first key the original
leaves the channel unchanged, so the player keeps the stored node component;
after the last key position, rotation and scale hold the last. Key ends equal the
record duration for 3,732 key tracks, 10 end earlier and 1,157 extend past it;
clips loop at trailer word 2, so later keys are unreachable. Placed objects
follow the original replay (below); the Totem prototype's `ModelAnimationPlayer`
still wraps with `tick % duration`. 30 ticks/s at speed 1.0 is proven for the
Mac build (Totem cycle 430 ticks ≈ 14.3 s); scene-clock scaling and pause are not.

- **Vertex animation** (flag 0x1000 without 0x4000; 1,736 records): a 44-byte
  block `u16 flags, u16 groups, …, ptr groups (+12), vec3 offset (+20), vec3
  scale (+32)` and 20-byte groups `u16 keys, u16 vertices, ptr indices, ptr
  ticks, ptr keys × vertices packed words (key-major), u32 runtime cursor`.
  Words are signed 10:10:10 (bits 30–31 unused), `value = q × scale + offset`.
  Group 0 holds two virtual vertices just past the mesh: the per-key culling box
  (the sampler 0x4711D0 writes them into the mesh record's bounds +0x78…+0x8C, which the
  frustum test 0x561A30 in the draw walk reads), not geometry; with block flag 0x2 group 1 is static (key 0);
  the other groups interpolate mesh positions linearly between keys. In all
  1,735 paired blocks the static and animated groups list every position of the
  node's mesh exactly once. `ModelVertexAnimation`.
- **Node-flag toggles** (flag 0x20000; 2,536 lists): signed `i16` ticks; the last
  entry with `|value| ≤ trunc(tick)` sets (≤ 0) or clears (> 0) node-state bit
  0x10, whose consumer is not traced. `SampleNodeFlag`.
- **Texture-frame tracks** (trailer word 12): the last key with `tick ≤
  trunc(time)` selects a frame of the slot. `ModelTextureFrameTrack`.
- **Texture-coordinate tracks** (flag 0x10000; 690 records): a 20-byte block
  `u32 corners, ptr → corners × (u16 first, u16 count), u32 keys, ptr → u16
  ticks, ptr → (f32 u, f32 v) keys`. Corner `i` replaces corner `i` of the node
  mesh's texture coordinates. Key search 0xa8304 differs from the others: the
  first key whose tick is greater than `trunc(time)` ends the segment, otherwise
  the last key holds (fraction 1); `(1 − t)·a + t·b` with `t·b` rounded first. In
  the 685 paired blocks of this run the corner count equals the mesh's, every
  track has at least two keys, starts at tick 0, increases strictly and reaches the
  clip end. `ModelUvAnimation`.
- **12-byte block** (flag 0x4000 with 0x1000; 30 records): `ptr → u32
  index[count], u32 count, ptr → floats`. The float table holds one pose of `count`
  positions (f32 x, y, z) per tick of the record, `duration + 1` poses in all: in
  all 30 records the table ends exactly where the next one starts (`ModelVertexPose.FrameCount`,
  checked by the corpus test). The earlier finding that no reader of the floats exists was wrong: the PC
  sampler (tp.exe 0x471860; record flags 0x1000 and 0x4000 at 0x4718F7) takes
  frame = `ftol(clip time)` (record clock +0x20), base = `count · frame`, and writes vertex *i* from entry
  `j = index[i]` (identity when the index pointer is null) at `floats[(base + j) · 12 bytes]`; in set mode
  (model header flag 4 clear, 0x471AA1, always the case for objects) the vertex is replaced and the node
  bounds updated, in add mode (0x471943) the position is added. Its "interpolation" multiplies the same
  address by `t` and `1 − t` (0x471AC5 and 0x471AD2 compute one address twice), so the result is the stored
  position with no blending between poses. `ModelVertexPose.ApplyPose` and `ObjectAnimator` do this
  (RIDES-041 retired): the pose of tick `trunc(time)`, held at the last pose past the table (an OpenTPW
  guard; a table that does not hold duration + 1 poses, not in the corpus, is read as far as it fits). All
  30 records have clip lengths of 10 to 280 ticks (go-karts 10, speakers 30, rocks and tree 50, droid 72 to 280, plane 200), so
  these are real per-tick vertex animations, not one stored pose; 19 of the 30 start on a pose that
  differs from the stored mesh (speaker, rocks, tree, droid and plane "c" clips), which the old
  stored-mesh display got wrong.

- **Path parameters** (flag 0x200; 71 records, 62 of them also 0x400; 0x400 never occurs alone):
  a 16-byte header `u32 start tick, u32 count, u32 reserved, ptr → f32[count]`. In all 71 blocks the start
  tick and the reserved word are 0, the count is the record duration + 1 and the table follows the header
  (`ModelPathParameters`, corpus test `OriginalPathParameterBlocksHoldOneValuePerTick`). The PC sampler
  (tp.exe 0x471860, block read at 0x471ECF) uses it when the record has no position keys (flag 0x1 wins), the
  node's parent is a path dummy (type bit 0x8) and the clip time lies in `[start, start + count]`; outside that
  range the node keeps its translation. 0x474EA0 interpolates the table at `time − start`: values i and i + 1
  blend linearly (at `time − start = count` the last pair with fraction 1); on a closed path i + 1 wraps to 0,
  on an open one the last value holds; two values more than 50 apart cross the 100 → 0 seam (the smaller gains
  100, the blend is taken modulo 100). The result is a percentage of the path: `s = N · fmod(v + 1000, 100) ·
  0.01` with N the path's segment count, segment `trunc(s)` at fraction `s − trunc(s)`. The go-karts rise by
  100 per lap. Flag 0x400 then turns the node along the path (0x470780): row 2 becomes the unit direction, row 0
  `normalize(up × direction)`, row 1 `direction × row 0`, with up = +Y unless the record has rotation keys
  (flag 0x8), whose row 1 is used; the rows replace rotation and scale. `ModelAnimationTrack.TrySamplePath`,
  played by `ObjectAnimator`. 0x400 with position keys would turn the node along its position curve; no record
  does that, so it is not played.
- **Paths** (geometry header u16 0x40 = count, u32 0xAC = table; 18 paths in 15 distinct geometry models): 16-byte
  records `u32 kind, u16 point count, u16 0, ptr → points (f32 x, y, z), u32 0` (`ModelPath`). Kind bit 0x1
  closes the path, bit 0x2 makes it cubic Bézier (`P0, (C, C, P)…`), else bit 0x8 linear and otherwise
  Catmull-Rom; the corpus has kind 3 (closed Bézier: karts, Haunted House, Rat Race, water ride, lobby
  islands, Big Apple), kind 2 (open Bézier: bus, ferry, seaplane, one go-kart model) and kind 8 (the slide). A node follows a
  path when its parent is a type-0x208 dummy (`Spline path`, `Kart_path01`…`04`, `Flightpath`, `FERRYSPLINE`,
  `h_line`, `sb_spline`); the high 16 bits of the dummy's stored index select the path (Kart_path02 is
  0x10033, path 1) and the points are in the dummy's space. N is the point count (÷ 3 for Bézier) minus 1 when
  open; segment i is evaluated by 0x474840 (Bézier, points 3i … 3i + 3), 0x474BF0 (linear, points i, i + 1)
  or 0x474CC0 (Catmull-Rom, points i − 1 … i + 2), indices wrapping modulo the point count. The direction is
  the Bézier derivative (0x474A20), the linear segment (0x474C70) or, for Catmull-Rom, the normalised position
  at fraction + 0.1 itself (0x474CC0 again). The open Bézier paths store a closing control pair the sampler
  never reads, so a vehicle on them jumps from the end back to the start when its value wraps past 100 (the
  bus once per loop, the ferry for 22 ticks of FerryM1). Checks (`OriginalPathsStartFlagged200NodesOnTheirStoredPose`):
  every flag-0x200 node hangs under such a dummy, and where a clip starts on the stored pose (Haunted House
  B/E/M/S, Busm1, slidec) tick 0 lands within 0.02 of the stored translation, facing within a few degrees of
  its stored row 2; a placed Haunted House moves its four karts along their paths (`PlacedHauntedHouseKartsFollowTheirPaths`).

Record bits (ANIM-plan §1): 0x1, 0x8, 0x80, 0x1000, 0x10000 and 0x20000 are
decoded and played, and so are 0x4000 (the 12-byte block) and 0x200/0x400 (path parameters); only 0x400 together
with 0x1, which no record has, stays in `ModelAnimationTrack.UnsupportedFlags`. 0x10, 0x20, 0x40, 0x8000 (always with
0x8), 0x100 (with 0x80), 0x800 (with 0x200/0x400) and 0x2000 (with 0x1000) are
never tested by the sampler, the bind, the engine relocation or the clip bind:
they are exporter metadata (`ModelAnimationTrack.MetadataFlags`), neither
unsupported nor undecoded payload. 0x2000 is not the static-group flag.
`HasUndecodedPayload` marks every record with more than rigid tracks.

Totem (`totemm1.MD2`, 3 records): `tp_cart` Bézier position (9 keys at
0, 177, 185, 211, 214, 257, 280, 380, 430; lifts to Y ≈ 47.6 above the rest
−2.34, drops into the shaft to −37.2 and returns), `tp_cog`/`tp_cog01`
counter-rotating about Y with eased stops. Pinned samples (C# against a
separate Python decoder): cart Y at ticks 50/177/300 = 8.4123/47.5655/−33.5249;
`tp_cog` (y, w) at 181 = (0.54410, 0.83902).

## Renderer and player

`ModelAnimationPlayer` validates node indices against the base model, samples
each track (missing components, and components before their first key, keep the
decomposed rest matrix), composes
local × parent world, and loops or clamps at the clip duration. `PrototypeRide`
draws each Totem mesh with its composed node matrix (Y/Z swapped on both sides,
footprint centred, ×0.2) via `ModelEntity.TransformOverride`, and samples
`totemm1.MD2` once when `Totem.RSE` triggers `ANIM_Main` (TRIGANIM 5), reporting
the clip length (14,333 ms at 30 ticks/s) to the script; otherwise the rest pose
is shown. The native smoke test reads back frames 10 and 30 after the
script-triggered motion starts and requires the animation tick, at least 3 node matrices (observed 16: cart, its 13 children, two cogs) and
over 100 pixels to change. The Totem prototype plays rigid tracks only.

Placed objects (`OriginalObject`) also play quantised vertex tracks.
`ObjectAnimator` keeps per instance a position array for every mesh whose
winning clip (the same most-recent-channel rule as the node matrices) has a
playable vertex track, and fills it in set mode: static group key 0, then the
animated groups at the clip tick. This equals the Mac sampler's result after
the set-mode passes since the clip was bound, because the groups list every
position once and the static group is applied once per bind (node-state flag
0x00800000). Each sample searches keys from key 0; the original keeps a cursor,
but its loop replay past the clip end rebinds the clip (0xa7190 → 0xa67d8 →
0xa5894), which resets it, so both agree on the normal object update. Its
object-list update replays without a bind and is not supported
(`docs/reverse/PPC-formats.md`, "Clip lifecycle"). Clip time follows the
original channel clock: a clip starts at a whole millisecond of the instance's
clock, its frame is `30 × elapsed ms / 1000 × speed` in single precision (speed
1.0, or TRIGANIMSPEED's operand / 1000), a
frame equal to the duration shows the last key, and a looping clip replays only
once the frame is strictly past the duration, restarting from the carry capped
at the duration and truncated to whole milliseconds, at most once per update. So
a 10-tick clip restarts every 334 ms, not every 333.3 ms, and one update far
past the end lands at the duration rather than at `time mod duration`. A
non-looping clip counts as finished only past its end. Without such a track the mesh
shows its stored positions, except on fixed items (`Info.DontApplyOffset 1`),
which keep the last pose. That is the original's bind (0xa5894): it copies the
stored mesh back unless the object has flag 0x00100000 with 0x8 clear, which only
the ride catalog loader requests, for descriptors whose word +56, by the
`CRideBalance` schema `Info.DontApplyOffset`, is set (the option word that could
also skip it is only ever stored as 0). In the catalog only the fantasy gates'
three clips are affected. The parsed, cached `ModelFile` is never written, so instances of one
asset keep independent poses. Unplayable tracks are listed in
`VertexLimitations` and leave the stored mesh: UV blocks whose corner count differs from the mesh, blocks that do not list every
position exactly once, and groups that end before the clip. All 609 catalog clips with vertex tracks play
(the 10 that carry a 12-byte block were reported as limitations before RIDES-041). `ObjectRenderParts.WritePositions` maps
positions through the corner order into the part's own vertex array (engine
axes); stored normals are kept. The original's object update recomputes face
normals after a vertex pass only for relative-animation models (0xa772c); two
other recomputation routes have untraced conditions. Those parts use a fixed-size `Dynamic` vertex buffer
that `Model.UpdateVertices` refreshes through the frame command list when the
pose version changes; texture-coordinate tracks write the same parts' texture
coordinates. Channels mix by ascending index, the highest channel writing a
component winning (0xa7960). Node-state bit 0x10 from node lists and toggles
hides the node's mesh: the draw walk 0x57A530 skips a node's own mesh when its state word has any of
0x8210 (0x57A5E3) and still draws its children; only bit 0x20 prunes a subtree. (The bit 0x10 tested at
0x57A797 belongs to an object render record, not to an MD2 node.) Bit 0x8000 is the UI widgets' hidden-state bit:
the widget model class (vtables 0x6FEE10, 0x6FEE38, 0x6FEE60; `!cbut`, `!frame`, `!slider`) sets 0xC8010 on the root and
its first-child chain in its constructor 0x476A90, and its state selector 0x476E80 hides the previous state frame with
`or 0x8010` (0x476EEC) and shows the new one by clearing 0x8010 (0x476EF9), so node lists and animation toggles, which
set or clear only 0x10, cannot show a hidden widget frame. The draw and picking walks (0x57A5E3, 0x57B664, 0x451E44,
0x45D0C1) treat it as 0x10. The only other writer is the save round trip of a model's node states (packed at
0x4643A8, restored at 0x4647A0). OpenTPW's `UiModel` draws one chosen state frame, which is the same result.
Relative-animation models play their vertex tracks in set mode, because the
default ride load path strips header flag 0x4 (the loader 0x461F10 keeps it only for the TrackMaster coaster loader, whose
Add mode is registered under COAST-026). Texture-frame tracks
apply (RIDES-035): `ObjectAnimator.UpdateTextureFrames` resamples every slot a
bound clip addresses at that channel's tick — channels and tracks mix in
ascending order, the last non-null `FrameAt` wins (hyenasm1 carries two tracks
on slot 15; its constant 0 loses to the face cycle), a null sample before a
track's first key keeps the previous frame (initially 0), and a slot no clip
addresses any more shows frame 0 again (MD2-900; frame or slot indices outside
the model are ignored, MD2-901). The renderer binds the effect:
`ObjectRenderPart.TextureSlots` carries each part texture's MD2 slot, and
Static batching keeps distinct model slots separate even when their frame-0
art has the same name. `OriginalObject` decodes each animated slot's frames
once through the existing path texture cache, then swaps the
`Texture` of the material slots that show it when the animator's frame changes
(`Material.SetTextureArrayElement`; nothing is reallocated while the frame
holds; resource sets are retired and rebuilt only when native texture identity
changes. Missing art is registered with the animator and holds its effective
selected frame, `[EXT:missing-frame-keeps-visible]`, retired `APPROX:MD2-902`). Sign-owned slots (`sign1`/`sign2`) are never frame-swapped.
Corpus (catalogue objects): 37 entries carry enabled texture-frame clips, 203
clips apply in-range tracks, 155 of them change the frame while playing
(Belly Bounce eyes `sp_eye01..06`, arcade/arc2x3 attract screens `sf_01..10`,
Bug TV `tv01..10`, queue-bin fires `fire0..04`, sideshow faces `ss_*`), and
every keyed frame name resolves in its object's texture directories — the
never-keyed missing art (`sq_eye.tga`, `Worm.tga`) and the out-of-range
`Pmegacostm` slot are refused safely. Theme Park Inc Science/Arabian clips
carry 230 in-range tracks; 101 unique keyed textures decode through the
edition's FSH/TGA/PNG path. One keyed retail reference is absent:
`science/tvsim` slot 39 frame 3 (`lt1.tga`), which holds the last available frame.

Own-format saves optionally store plain per-slot frame/animated history and
the frame version in `ObjectAnimatorSnapshot.TextureFrames`. Null and invalid
keys, and missing-art holds, continue from the same visible frame after the
JSON codec and a fresh restore. Older saves without this optional field start
at frame 0, then sample their restored channels. Slot counts and frame indices
are validated against the model before mutation; no GPU references are saved.
When a headless save selects art unavailable to the renderer, restore starts
that slot at its base texture before sampling the restored channel. Availability
registration before or after restore therefore produces the same selection.
Object deletion clears instance bindings and retires its owned materials and
shaders; shared decoded textures keep their existing process cache ownership.

Native GPU evidence: the opt-in `ObjectTextureFrameGpuTests` check plays the
Belly Bounce eye track, verifies slot 19 changes from `sp_eye01.wct` to frame 1
in the material's native texture binding, reads different GPU pixels, and
checks held selections allocate zero bytes and reuse their resource sets.
It also checks object deletion retires the owned materials. This is binding
and texture-readback evidence, not a framebuffer comparison or original-game
parity. On macOS invoke the public test method from a temporary `net10.0`
console harness referencing `OpenTPW.Tests.csproj`, on the main thread, with
`OPENTPW_TEXTURE_FRAME_GPU=1` and `OPENTPW_GAME_PATH` set; MSTest's pool thread
cannot create a Cocoa window. Native SDL2, cimgui and veldrid-spirv libraries
must be available beside the harness. No original assets or native binaries
are committed. Not played: the group-0
culling boxes (OpenTPW does not cull per mesh). `LobbyIsland` (not instantiated by the game yet) uses the composed
hierarchy, swaps normal axes like positions, and renders untextured materials
white instead of requesting `lobby/terrain/textures/.wct`.

## Private corpus

312 local WADs, 2,118 `.MD2` members: 2,116 parse (838 geometry, 1,278
animation). The two refused members are `levels/jungle/rides/wateride.wad/wr_tunnel.md2`
and `wr_tunnelm.md2`, version 207.201 with a 4-byte-shifted header; the loose
`generic/dynamic/garrow.MD2` and `rarrow.MD2` are version 24.23. The original
loader refuses all four too (below), so they are not an OpenTPW gap. Geometry totals: 4,914 meshes, 7,520 nodes (2,606 dummies), 210,034
faces, 303,199 corners, 133,707 positions, 12,951 materials, 7,282 texture slots.

Pinned fixtures; geometry SHA-256 covers per mesh name+NUL, per-corner float
X/Y/Z/U/V, `u32` face corners and per-material `i32` slot/`u16` start/end, and
was calculated separately in Python from the raw bytes:

| Member | File SHA-256 | Meshes/nodes/slots/faces | Geometry SHA-256 |
| --- | --- | --- | --- |
| `levels/jungle/rides/totem.wad/totem.MD2` | `1b499e5a2e43ebb3ac4e9252a24a66f4a2efe0285afe522a573cb226438ba42a` | 13/31/34/316 | `e0b32787aa1916f403c4e952b2f7025e65866bfcf25f8ec98297967e1ebb4f52` |
| `global/advisor.wad/Advisor.MD2` | `f518a91bcebd1770a2927a0143549a547d02eed5ee91705e626fc08c2bc3b51c` | 25/29/41/1321 | `a0174988c4a8d423f329c85bb7b5187f9f71cf157bfe8cad82ab4030d57fe733` |
| `levels/fantasy/features/gates.wad/gates.MD2` | `1f3013c8140be310ae885ff240d96be228b18db4b36120a91217b081bf9a454d` | 2/5/9/544 | `66da2a022e956b79081eab4ffeb48b9593dbcab66270987fc9fdf967db39532d` |
| `levels/fantasy/terrain.wad/base.MD2` | `1b783ee1735855575e6f8d2bbfa694b4a27d2f04ec979f4addc9da049514f495` | 195/196/40/17450 | `3ad596d88e48aca3bb05365d806e23517b29eee152779c7568c64a7b090f586e` |
| `levels/jungle/rides/totem.wad/totemm1.MD2` (animation) | `6467b35faadd40fef639c2b10f3f0bb2754675f15509172778ef399b74ae76fe` | counts = totem | trailer at 0x4F0; 3 tracks, samples above |

Synthetic public tests (`Md2ModelFileTests`, `Md2AnimationTests`) cover
Bézier/linear position, eased slerp without hemisphere flip, linear scale,
parent-relative composition, looping/clamping, stored-normal selection, malformed
tracks (non-unit quaternions, decreasing times, counts, pointers, totals), and the full geometry layout,
animation trailer, untextured material, short reads, input cap, truncation,
magic/version, out-of-file and out-of-region pointers, corner/position/material
references, hierarchy cycles/parent mismatch/unreachable nodes, header totals,
texture/node names and trailer pointers. Corpus tests (`Md2CorpusTests`) pin the
track totals (6,549 records, 3,475 with TRS data, 785 Bézier + 431 linear
position tracks, 8,703 position keys, 26,233 rotation keys, 12,451 eased, 2,832
scale keys), play every name-paired animation (1,275; bounded count 1,237) and
the Totem samples; they are inconclusive without `OPENTPW_GAME_PATH`, not passes.

## Older versions: 207.201 and 24.23

`wr_tunnel.md2`, its animation `wr_tunnelm.md2` (207.201) and the loose
`generic/dynamic/garrow.MD2`/`rarrow.MD2` (24.23, listed in that folder's
`qickload.txt`) are rejected, as by the original (below). Their
header has counts at 0x34–0x4B, region pointers from 0x4C, bounds at 0x78 and a
table of 260-byte source-path strings (`C:\WORK\Theme 2\…`) at 0xB8 before the
texture flags; slot records do not match the 221.203 layout. With a single
geometry sample the layout cannot be verified, so it is not supported.

**Original loader (Mac `engine_shared`, `LoadM3D2Header(char*, unsigned long)`).**
Static analysis of the Mac PowerPC library (data fork SHA-256
`c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b`; Ghidra, nothing
executed) shows how the original treats versions. The loader byte-swaps one fixed
header layout for every version (32-bit words at 0x00–0x14 and 0x2C–0x30, sixteen
16-bit counts at 0x34–0x4B, region offsets from 0x4C that it turns into pointers;
the animation block pointer is word 0x98), then checks, in this order:

| Condition | Log tag | Result |
| --- | --- | --- |
| magic ≠ `0x1CD15D46` | `BadFileType` | rejected |
| major ≥ 222 | `OldCode` | rejected |
| major < 221 and the caller's flag bit 1 clear | `DeadMesh` | rejected |
| caller flag bit 2 set (geometry expected) but an animation block exists | `Master is Anim` | rejected |
| flag bit 2 clear (animation expected) but no animation block | `Anim is Master` | rejected |
| animation block, minor > 203 | `OldCode` | animation dropped, flag 0x20 cleared |
| animation block, minor < 203 | `DeadAnim` | animation dropped |
| animation block, minor = 203 | — | animation tables fixed up |
| no animation block, major < 221 | `OldMesh` | loaded, warning |
| otherwise | `Ok` | loaded |

So rejecting 207.201 and 24.23 matches the original's default (`DeadMesh`); the
original loads such a file only when a caller passes flag bit 1, and would then
discard a 201 animation (`DeadAnim`). No application call site passes that bit
(0x58f00 passes 0, 0x99acc/0x99ad4 pass 6 and 2; docs/reverse/ANIM-plan.md §6),
so none of the four members loads in the Mac build and their layout needs no
decoding for parity. The
documented 207.201 fields at 0x34–0x4B and 0x4C coincide with the loader's
count and offset fields, but that does not verify the rest of its layout.

## Remaining gates and sources

Next: the object-list replay without a bind (RIDES-032), and game-speed scaling
of the scene clock; the
texture slot's current frame is read by the draw (`0x580530`) and the per-slot bind (`0x57A420`) only while the ANIMATINGTEXTURES option is on (RIDES-035 retired; MD2-900..902 stay);
object textures sampled with the slot's wrap (0x10/0x20) or clamp instead of OpenTPW's mirrored repeat (COMPAT-010);
the heightfield's texture turn and mirror in the terrain renderer. CPU parsing and sandbox playback do not qualify original
rendering or animation behavior.

Sources: upstream OpenTPW docs page is a TODO stub
(`c077cc9a12aaaac93caac52673ab3080f631fbd9`,
https://github.com/OpenTPW/opentpw-docs/blob/c077cc9a12aaaac93caac52673ab3080f631fbd9/src/formats/m3d2.md);
everything above comes from local read-only inspection of extracted WAD members
with separate Python scripts. No original binary executed; no third-party
decoder source copied.
