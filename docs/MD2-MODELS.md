# MD2 (Bullfrog M3D2) models: CPU parsing evidence

October 9, 2026. Status: bounded CPU reader for the version 221.203 layout;
geometry, texture slots and node hierarchy are verified against the whole local
WAD corpus. Node matrices are parent-relative (corpus evidence below).
Animation members decode into position/rotation/scale tracks, quantised vertex
groups, node-flag toggles and texture-frame tracks, validated over all 1,278
members; the sandbox Totem plays its original `totemm1.MD2` cycle and placed
objects play their vertex tracks. Decoder mechanics and the 30 ticks/s clock are
read from the Feral Mac PowerPC build (`docs/reverse/PPC-formats.md`); PC
equivalence, the 12-byte vertex layout and other record kinds are **not
verified/decoded**; original rendering fidelity is not verified. These are
not Quake II MD2 files. No dependency or original asset is added to the repository.

`ModelFile` (`source/OpenTPW.Files/Formats/Model/`) reads the whole member into a
capped buffer (16 MiB; the largest original member is 1,377,696 bytes), validates
every offset/count before use, leaves caller-owned streams open and supports
short nonseekable reads. Malformed data raises `InvalidDataException`; other
versions raise `NotSupportedException`. The unused `OpenTPW.Files.ModelFile` stub,
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
| 0x40 | extra 16-byte records (pointer at 0xAC); 0x42 nodes; 0x44 meshes | |
| 0x46 | opaque (equals mesh count in 254 files, 0xFFFF in 493) | `Unknown46` |
| 0x48 | dummy attribute records; 0x4A normals (corners + faces) | |
| 0x50–0x7C | flag table, slot table, position, normal, UV, material, face regions, 0x6C block, mesh table, dummy table, root node, attribute table | each region bounds-checked |
| 0x80 | bounds min/max, 6 floats | |
| 0x98 | animation trailer pointer (animation members only) | |

Texture slots: 8-byte records (`u32 flags`, `u32 0`) at 0x50, then contiguous
20-byte NUL-terminated `.tga` names, then 16-byte slot records at 0x54
(`u32 opaque 0/1`, `u32 0`, `u16 0`, `u16 frame count`, `u32 first-name pointer`).
124 slots have several frame names. Flag bits (observed 0x1..0x43) are retained raw.

Nodes: one tree rooted at 0x78. Mesh records are 160 bytes, dummy records 88:
`u32 type`, parent/next-sibling/first-child record pointers, 4×4 float row-major
matrix (translation in row 4), `u32 index`, name pointer. Mesh records continue
with `u16 positions, materials, faces, corners`, then position, normal, UV,
material and face pointers, 6-float mesh bounds at +120 and a corner-table
pointer at +148; the remaining words were zero throughout. The reader requires
every pointer to hit a record start, the root to have no parent/sibling, and a
walk to reach each node exactly once with matching parent pointers.

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
files) and 27 extra 16-byte records. The 0x6C block, found only in the four
level `terrain.wad/base.MD2` files (96×85 cells) and lobby `terrain/Base.MD2`
(111×110), is decoded as a terrain heightfield (`ModelFile.Heightfield`: corner
heights, hole/ground-texture-slot cell words; layout and evidence in MAP.md); its
header words and cell-word low bits stay opaque. Node types (mesh 0x1/0x401/0x2 empty; dummy 0x200/0x208/0x300/0x600)
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
| 14 | node list (u16 node indices, meaning unknown; 874 files) | `NodeList` |
| 15 | first easing table (informational) | |
| 1, 7, 13, 16, 17 | zero throughout | |

Record (64 bytes): `u32` ordinal (= index in all 6,549), `u32` flags, `u32`
opaque, `u32` record duration, `u16` rotation-key count, `u16` scale-key count,
`u16` node index, `u16` opaque (non-zero only with flag 0x20000), then pointers:
+24 position header (flag 0x1), +28 rotation keys (flags 0x08/0x10/0x20/0x40),
+32 scale keys (flags 0x80/0x100), +36 per-frame float block (flags 0x600),
+40 vertex-animation block (flags 0x1000–0x4000), +44 (flag 0x10000), +48 small
u16 list (flag 0x20000), +52 easing curves; +56/+60 zero. The position,
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
the sandbox loops at trailer word 2, so later keys are unreachable (the
original's loop policy is not traced). 30 ticks/s at speed 1.0 is proven for the
Mac build (Totem cycle 430 ticks ≈ 14.3 s); scene-clock scaling and pause are not.

- **Vertex animation** (flag 0x1000 without 0x4000; 1,736 records): a 44-byte
  block `u16 flags, u16 groups, …, ptr groups (+12), vec3 offset (+20), vec3
  scale (+32)` and 20-byte groups `u16 keys, u16 vertices, ptr indices, ptr
  ticks, ptr keys × vertices packed words (key-major), u32 runtime cursor`.
  Words are signed 10:10:10 (bits 30–31 unused), `value = q × scale + offset`.
  Group 0 holds two virtual vertices just past the mesh (a padded per-key
  bounding box, not geometry); with block flag 0x2 group 1 is static (key 0);
  the other groups interpolate mesh positions linearly between keys. In all
  1,735 paired blocks the static and animated groups list every position of the
  node's mesh exactly once. `ModelVertexAnimation`.
- **Node-flag toggles** (flag 0x20000; 2,536 lists): signed `i16` ticks; the last
  entry with `|value| ≤ trunc(tick)` sets (≤ 0) or clears (> 0) node-state bit
  0x10, whose consumer is not traced. `SampleNodeFlag`.
- **Texture-frame tracks** (trailer word 12): the last key with `tick ≤
  trunc(time)` selects a frame of the slot. `ModelTextureFrameTrack`.

Not decoded (`ModelAnimationTrack.UnsupportedFlags`, 1,122 records): the
12-byte vertex layout (0x4000, 30 records), per-frame float/path blocks 0x600,
the 0x10000 block and 0x2000. The node list's role and the rotation flag bits
are open. `HasUndecodedPayload` still marks every record with more than rigid
tracks.

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
0x00800000). Without such a track the mesh shows its stored positions, as the
original copies them back from the base model when a clip is replaced
(0xa5894). The parsed, cached `ModelFile` is never written, so instances of one
asset keep independent poses. Unplayable tracks are listed in
`VertexLimitations` and leave the stored mesh: the 12-byte layout (10 catalog
clips, 23 tracks), relative-animation models, blocks that do not list every
position exactly once, and groups that end before the clip. All 599 other
catalog clips with vertex tracks play. `ObjectRenderParts.WritePositions` maps
positions through the corner order into the part's own vertex array (engine
axes); stored normals are kept (the original's handling of normals after a
vertex pass is not traced). Those parts use a fixed-size `Dynamic` vertex buffer
that `Model.UpdateVertices` refreshes through the frame command list when the
pose version changes. Not played yet: group-0 bounds (no consumer traced),
node-flag toggles and texture-frame tracks (their runtime bindings to visibility
and texture slots are not proven), and the add mode. `LobbyIsland` (not instantiated by the game yet) uses the composed
hierarchy, swaps normal axes like positions, and renders untextured materials
white instead of requesting `lobby/terrain/textures/.wct`.

## Private corpus

312 local WADs, 2,118 `.MD2` members: 2,116 parse (838 geometry, 1,278
animation). The two failures are `levels/jungle/rides/wateride.wad/wr_tunnel.md2`
and `wr_tunnelm.md2`, version 207.201 with a 4-byte-shifted header, explicitly
unsupported. Geometry totals: 4,914 meshes, 7,520 nodes (2,606 dummies), 210,034
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

## Version 207.201

`wr_tunnel.md2` and its animation `wr_tunnelm.md2` remain rejected. Their
header has counts at 0x34–0x4B, region pointers from 0x4C, bounds at 0x78 and a
table of 260-byte source-path strings (`C:\WORK\Theme 2\…`) at 0xB8 before the
texture flags; slot records do not match the 221.203 layout. With a single
geometry sample the layout cannot be verified, so it is not supported.

## Remaining gates and sources

Next: how scripts select clips (RSE `TRIGANIM` family) and the loop policy;
the 12-byte vertex layout and the other record kinds; node list; rotation flag
bits; consumers of the toggle bit, the group-0 box and texture frames;
texture/material flag bits from original captures;
the heightfield cell-word low bits. CPU parsing and sandbox playback do not qualify original
rendering or animation behavior.

Sources: upstream OpenTPW docs page is a TODO stub
(`c077cc9a12aaaac93caac52673ab3080f631fbd9`,
https://github.com/OpenTPW/opentpw-docs/blob/c077cc9a12aaaac93caac52673ab3080f631fbd9/src/formats/m3d2.md);
everything above comes from local read-only inspection of extracted WAD members
with separate Python scripts. No original binary executed; no third-party
decoder source copied.
