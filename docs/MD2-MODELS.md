# MD2 (Bullfrog M3D2) models: CPU parsing evidence

October 9, 2026. Status: bounded CPU reader for the version 221.203 layout;
geometry, texture slots and node hierarchy are verified against the whole local
WAD corpus. Animation payloads are identified but **not decoded**. Original
rendering fidelity, transform semantics and animation playback are **not
verified**. These are not Quake II MD2 files. No dependency or original asset is
added to the repository.

`ModelFile` (`source/OpenTPW.Files/Formats/Model/`) reads the whole member into a
capped buffer (16 MiB; the largest original member is 1,377,696 bytes), validates
every offset/count before use, leaves caller-owned streams open and supports
short nonseekable reads. Malformed data raises `InvalidDataException`; other
versions raise `NotSupportedException`. The unused `OpenTPW.Files.ModelFile` stub,
which logged fixed offsets from one file, was removed. The renderer-facing API
(`Meshes`, `Mesh.Vertices/Indices/TexCoords/Normals/TransformMatrix/Materials`,
`MaterialData.Name/Flags`) is unchanged; mesh names no longer carry a trailing NUL.

## Verified layout (little-endian, version 221.203)

Header, 0xB8 bytes:

| Offset | Field | Evidence |
| --- | --- | --- |
| 0x00 | magic `0x1CD15D46` | all 2,118 members |
| 0x04/0x08 | version 221/203 (2 members: 207/201, different layout, rejected) | all |
| 0x0C, 0x10, 0x14, 0x2C | opaque words (0x14 looks like a 1999 Unix time; 0x2C is a few bytes below file length) | not interpreted |
| 0x18 | 20-byte source name, NUL-padded (92 differ from the member name) | `SourceName` |
| 0x30 | opaque flags (geometry 1/3/5/9/13/25; animation 0/4/8/16/32/34) | `HeaderFlags` |
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
files), 27 extra 16-byte records, and the 0x6C block found only in five terrain
`base.MD2` files (48 bytes resembling a 96×85 grid header with two pointers, not
decoded). Node types (mesh 0x1/0x401/0x2 empty; dummy 0x200/0x208/0x300/0x600),
and whether matrices are parent-relative, are not interpreted. `Normals` is still
the legacy recomputed smooth normal; stored normals are `CornerNormals`/`FaceNormals`.

## Animation members

1,278 members have all region offsets at 0x50–0x7C zero and a pointer at 0x98
to a 72-byte trailer that ends the file. Their header counts and bounds repeat
the base model's (1,052 have a same-signature geometry member in the same WAD).
The trailer is retained as 18 raw words; words 8–12/14/15 must be 0 or point into
the payload. Word 2 (1–860) is plausibly a duration; keyframe tracks seen
between header and trailer (u16 times, quaternions/positions, node indices for
dummies) are **not decoded**.

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
| `levels/jungle/rides/totem.wad/totemm1.MD2` (animation) | `6467b35faadd40fef639c2b10f3f0bb2754675f15509172778ef399b74ae76fe` | counts = totem | trailer at 0x4F0 |

Synthetic public tests (`Md2ModelFileTests`) cover the full geometry layout,
animation trailer, untextured material, short reads, input cap, truncation,
magic/version, out-of-file and out-of-region pointers, corner/position/material
references, hierarchy cycles/parent mismatch/unreachable nodes, header totals,
texture/node names and trailer pointers. Corpus tests (`Md2CorpusTests`) are
inconclusive without `OPENTPW_GAME_PATH`, not passes.

## Remaining gates and sources

Next: decode animation tracks against base node indices; establish matrix space
(local vs world) and texture flag/material bits from original captures; decide
renderer use of stored normals; support 207.201 if its two members matter;
decode the terrain 0x6C block. Lobby `Spa_isle.MD2` has one untextured material
that now loads with an empty name instead of crashing the reader. CPU parsing
alone does not qualify original rendering or animation behavior.

Sources: upstream OpenTPW docs page is a TODO stub
(`c077cc9a12aaaac93caac52673ab3080f631fbd9`,
https://github.com/OpenTPW/opentpw-docs/blob/c077cc9a12aaaac93caac52673ab3080f631fbd9/src/formats/m3d2.md);
everything above comes from local read-only inspection of extracted WAD members
with separate Python scripts. No original binary executed; no third-party
decoder source copied.
