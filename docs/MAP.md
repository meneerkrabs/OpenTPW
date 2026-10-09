# MAP: terrain attribute maps

October 9, 2026. Status: bounded container/grid reader implemented and verified
against all five terrain MAP members in the selected install. Cell value
**semantics are not verified**; no gameplay, pathing or rendering uses the data
yet. No original asset is included in the repository.

Two unrelated formats share the `.map` extension:
- **Terrain attribute maps** (`TP2M`), inside `levels/<theme>/terrain.wad`. This
  document and `MapFile` cover only these.
- **Sound catalogs** (`cat_*BANK.map`, `cat_*SFX.map`): 62 loose files (31
  BANK, 31 SFX) under `Data/global/{sound,Speech}` and
  `Data/levels/*/{Sound,Speech,Music}`, plus two members of
  `levels/jungle/features/speaker1.wad`. They start with a 16-byte GUID-like
  signature: every BANK file has `012c61e9 d031d211 b40900a0 c993f203`, every
  SFX file `002c61e9 d031d211 b40900b0 c993f203` (bytes 0 and 11 differ). The
  speaker BANK member contains the name `Speaker\Bank`. The upstream "MAP (Sound
  Map)" page (docs `34f357f`) is TODO and describes this family, not terrain.
  `MapFile` rejects them (no `TP2M` magic). Their schema is not decoded here.

## Fixture inventory

All 312 `.wad` files under `Data` were enumerated with an independent Python
DWFB/RefPack reader (40-byte entries from offset 88). Seven `.map` members exist:
five terrain maps (RefPack, declared and decoded size 16,464) and the two
`speaker1.wad` sound catalogs (`cat_rideBANK.map` 56 B uncompressed;
`cat_rideSFX.map` 111 B RefPack, 364 B decoded). The C# `WadArchive`
extraction yields byte-identical terrain members (pinned file hashes below).

| Member | Container SHA-256 | Decoded SHA-256 | Cells SHA-256 | Nonzero cells |
| --- | --- | --- | --- | --- |
| `fantasy/terrain.wad/base.map` | `386f146985ee1eefae080c106da19d5f1b16c8620886853975ac98ff4fbebe7d` | `641b1f05fd3f47a4f0fcca551607c2f6c45b02845a8c7f4871338eaf42597368` | `cc2a150e668d42bdb2649c595b3ddde175be0bf579782a3fa024362afc991f36` | 1073 |
| `hallow/terrain.wad/base.map` | `76a1711d05aefc9faa34a666d1c781a04a0913945027129c3d0104b11c5cfad9` | `c74b5b1b9aa234eba8d81a20af9cf7af597055da13527805ca800879f50eb398` | `4fb515a2fbbe4d9be7d6a84d6cdff5fcf6ad502c9d3d60a2e26ca60bccd05aef` | 1118 |
| `jungle/terrain.wad/base.map` | `6a4443e7793122eb3a804eee3f4f1ebe19324199185b2562f5311d0f331c3300` | `adbc201acc29e9e81b936dfc9ffcdc403757a920f76c9ef41c435a5417d98364` | `9e8e692a2f25e1e0129d7795c5a1b7d1ddbca71d5a0db616e4f482010a1492c2` | 1495 |
| `jungle/terrain.wad/terrain.map` | (same as above) | `f6bcb05c1e085aeb1f085bf0cd6d672873f1681b0434ddd9babf929527ec10a1` | `d7bdfa1f257be03a817740c535448a07428f3799f91414517748695a2314e3d5` | 805 |
| `space/terrain.wad/base.map` | `a681acc1504697ea31c177625ad36ef48a847ae2d29c041bd147585bb30495f5` | `cc27092df61ef6fae42dc3c17bc49d42f2d87550d6d1f9b7cc2d40044772ec54` | `3a8146b640f52c2b9c9cac6de572eb13367eb8ccc08da3d6a33cd6ca41c3a89c` | 1196 |

Cell hashes were computed independently in Python from bytes 0x48..0x4047.

## Layout (all values little-endian)

| Offset | Size | Value in every fixture | Meaning |
| --- | --- | --- | --- |
| 0x00 | 4 | `TP2M` | magic |
| 0x04 | 32 | `Theme Park 2 Attribute Map File\0` | NUL-terminated title |
| 0x24 | 4 | `MAP ` | chunk tag |
| 0x28 | 4 | 16,412 (0x401c) | chunk payload size; = 28 + width × height |
| 0x2c | 4 | 128 | width |
| 0x30 | 4 | 128 | height |
| 0x34 | 20 | five u32 `8` | **unknown**, preserved as `OpaqueHeaderValues` |
| 0x48 | 16,384 | — | width × height raw cell bytes |
| 0x4048 | 4 | `END ` | chunk tag |
| 0x404c | 4 | 0 | END payload size; file ends here |

The chunk size exactly spans width/height/opaque values/cells in every fixture,
which is the cross-check for the tag+size chunk reading. Calling the two
128 values width/height and treating cells as column-fastest is supported only
by coherent straight wall runs when rendered that way (a square grid cannot
distinguish the order by size); the mapping to world X/Z, orientation and cell
scale is **not verified**.

## Cell values (observed only)

Value inventory over the five fixtures: 0, 1, 3, 8, 16, 17, 128, 144, 148. All
four `base.map` files share identical counts at identical positions for 17 (490),
144 (48), 148 (14) and 8 (10), all nonzero cells lie within columns 0–84 and rows
0–95, and rows 96–127 are zero. Value 1 forms the outer boundary and per-level
internal shapes; 3 (240 cells) and 128 (4 cells) appear only in jungle `base.map`;
16 appears once in fantasy. Jungle `terrain.map` contains only 0/1 with a
different arrangement (nonzero extent to column/row 96). The values look like
bit combinations (17 = 16|1, 144 = 128|16, 148 = 128|16|4) but that is a
hypothesis. The previous placeholder `TileType` enum (Ground/Wall/River/
NotWalkable/BrickPath) had no evidence and was removed; nothing referenced it.

## Reader limits

`MapFile` caps input at 4 MiB, width/height at 1024 and cells at 1,048,576, checks
dimensions before allocating, requires exactly one `MAP ` chunk whose size equals
28 + width × height, and an empty final `END ` chunk with no trailing bytes.
Unknown chunk tags raise `NotSupportedException` (none observed); other malformed
input raises `InvalidDataException`. Caller-owned streams stay open and
nonseekable short reads work.

## Tests

`source/OpenTPW.Tests/MapFileTests.cs`: 20 synthetic cases (header, opaque values,
cell indexing, truncation, magic/title, dimension limits, chunk size mismatch,
missing/duplicate/unknown/non-final chunks, sound-catalog signature, short reads,
oversized input) and 9 private-asset cases (five pinned fixtures, cross-level
identical positions of values 8/17/144/148, both speaker catalog members and all
62 loose catalog `.map` files rejected). Asset tests are inconclusive
without `OPENTPW_GAME_PATH`.

## Remaining gates

- Meaning of each cell value/bit, and of the five opaque `8` values.
- Why jungle has both `base.map` and `terrain.map` and which the game loads.
- Grid-to-world mapping (axes, origin, cell size) versus terrain MD2/heights.
- Sound catalog `.map` schema (separate format).
Verify these with original runtime observation or further corroborating data
before wiring the grid into placement, pathing or rendering.
