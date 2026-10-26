# MAP: terrain attribute maps

October 9, 2026. Status: bounded container/grid reader verified against all five
terrain MAP members. The grid-to-world mapping and five cell bits are now
**supported by cross-format evidence** (terrain MD2 geometry and heightfield in all
four themes, `Standard.sam` fixed-item positions, the Jungle `Easymode.TPWI` cell
grid). tp.exe's loader (October 16, below) ignores the five header words and never opens `terrain.map`;
bit 0x04 is stored but nothing reads it. `MapCellFlags` drive the
read-only original-level build rules (`--load-original-level`). No original asset is
included in the repository; no original executable was run.

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
which is the cross-check for the tag+size chunk reading.

## Grid-to-world mapping (verified)

The file's slow axis ("height"/row) is the game **X** cell and its fast axis
("width"/column) the game **Y** cell: `MapFile.GetCellAt( x, y )` reads row `x`,
column `y`. One cell is 10 MD2 units; cell (x, y) covers MD2 world
X ∈ [10x, 10x+10), Z ∈ [10y, 10y+10) with no offset. Evidence:

- `Standard.sam` (all four themes) names fixed-item cells with `PosX`/`PosY`.
  EntranceA/B (47/48, 17) are the first two MAP value-8 cells; TicketBooth,
  CrossingParkSide, CrossingBSSide, BusStop A/B and the 6-cell StrikeArea all lie
  on value 144/148 cells (test `OriginalFixedItemSettingsLieOnMatchingMapFlags`).
- Jungle `base.MD2` dummy node `bridge01` is at MD2 (530, 21.1, 575): exactly the
  centre of the four value-128 cells (51–54, 57). The `ticket_booths` mesh spans
  X 463–497, Z 129–141, i.e. cells (46–49, 12–14).
- Rasterising all 20,405 jungle MD2 triangles (node transforms composed up the
  hierarchy) onto cell centres: 233 of 240 value-3 cells lie under `river*`
  meshes, all 4 value-128 cells under `bridge_top`, value-17/144/148 cells under
  `verge`/`road_*`/`arrival_*`, value-1 interior shapes under `volcanoe`/`newcliff`.
- The MD2 heightfield (below) has its holes on exactly these cells.

`Standard.sam` also states `MapInfo.HeightfieldXStart/YStart 0/0`, `HeightfieldWidth 95` / `HeightfieldHeight 84`
(maximum indices of the 96×85 heightfield) and `FixedItemOriginX/Y 48/17`. The four heightfield keys are the procedural terrain's rectangle
(`ProceduralTerrainRectangle`, docs/LND.md): the cells from (XStart, YStart) to (XStart + Width, YStart + Height) inclusive, which is the whole heightfield in all four themes,
are textured procedurally from the theme's `terrain.wad/base.lnd` (the weight grid of that file, docs/LND.md, covers 95 of the 96 columns; the original reads the next sample row for the last).

## Terrain heightfield (`base.MD2` 0x6C block, verified layout)

The MD2 header pointer at 0x6C (terrain models only) locates a 48-byte block:
four opaque words (`0`, `0x1FE02096`, `0x80000000`, `0`), cell size X/Z (10.0,
10.0), cell counts X/Z (96, 85), two floats that bracket the heights
(integer-truncated lower/upper; kept raw), then pointers to (96+1)×(85+1) float
corner heights and 96×85 `u32` cell words, both X-fastest. In all four models the
two regions are adjacent and end at the block. `ModelFile.Heightfield` exposes it.

- Corner (x, z) is at MD2 (10x, height, 10z): of the MD2 mesh vertices lying on
  grid corners where the heightfield is not flat (|h| > 0.5), 283/283 (fantasy),
  558/635 (hallow), 746/984 (space) and 768/1,861 (jungle) have the same height,
  i.e. cliff/river/volcano seams meet the heightfield.
- Cell word `1` = hole (no heightfield surface). In all four themes every cell
  with MAP Water, EntranceArea or FixedWalkway is a hole, every value 0/8 cell is
  not, and value-1 cells are holes only where rock/volcano meshes replace the
  ground (jungle 363, fantasy 192, hallow 238, space 314 of them).
- Otherwise the high 16 bits are an MD2 texture slot: always six distinct slots
  per theme, all `*_bas1..6` ground textures. The low 16 bits are read by tp.exe: the triangle
  diagonal (0x4, with 0x2 its complement), the ground-texture turn (0x8/0x10/0x20) and mirror (0x40);
  see MD2-MODELS.md ("Kept opaque" no longer applies to them).

## Cell bits (`MapCellFlags`)

Value inventory over the five fixtures: 0, 1, 3, 8, 16, 17, 128, 144, 148. All
four `base.map` files share identical counts at identical positions for 17 (490),
144 (48), 148 (14) and 8 (10); nonzero cells lie within X 0–95, Y 0–84.

| Bit | Name | Values | Evidence |
| --- | --- | --- | --- |
| 0x01 | `Blocked` | 1, 3, 17 | Edge ring (heightfield present) and rock/cliff/volcano shapes; water; entrance buildings. No Easymode path or object cell has it. |
| 0x02 | `Water` | 3 only | River/lake meshes, heightfield holes (jungle, 240 cells) |
| 0x08 | `InitialPath` | 8 | Starts at FixedItemInfo.EntranceA/B; all 10 cells are path cells in the Easymode save grid; heightfield present |
| 0x10 | `EntranceArea` | 17, 144, 148 | Entrance complex meshes; holes in all four themes |
| 0x80 | `FixedWalkway` | 128, 144, 148 | Bridge deck (128) and entrance crossings/strike area/ticket lane (144/148): not blocked, holes, covered by deck/road meshes |
| 0x04 | — | 148 only | Ticket-booth lane cells (X 47–48, Y 10–16); copied to tile flag 0x400 and never read (below), masked out |

Exception: fantasy has one isolated value 16 at (27, 17) with no geometry and no
heightfield hole; it is unexplained and reported as `EntranceArea` only.

**Bit 0x04 (unknown meaning, not in `MapCellFlags`).** Measured on the four
`base.map` fixtures (fantasy, hallow, jungle, space): raw bit 0x04 is set on exactly
14 cells per level, the same cells in each: X 47–48 × Y 10–16 (`GetCellAt(47..48, 10..16)`),
all with raw value 148. No other value has bit 0x04 in any of the four base maps. The
jungle `terrain.map` has no bit-0x04 cells (values 0/1 only). The pinned test
`OriginalRawBit04IsOnlyTicketLaneCellsWithValue148` checks this; `MapFile.EnumerateCellsWithRawBits(0x04)`
exposes it without giving it a flag. In tp.exe the tile initialiser 0x536490 copies it to tile flag
word +0xE bit 0x400 (`or byte [tile+0xF], 4` at 0x536576), and no reader of that tile flag or of the raw bit
was found (the flag accessor 0x535DB0 is only called with 0x4/0x10/0x20/0x40/0x80 and direction nibbles;
the getter 0x53A520's seven callers touch 0x100/0x20/0x40). So the bit has no observed effect.

The Jungle save (TPWS-PAYLOAD.md) carries a byte identical to `base.map` at every
one of 16,384 cells and does **not** match `terrain.map` (0/1 only), so the
Easymode park was built on `base.map`. tp.exe never loads `terrain.map`: 0x450900 builds only
`"%s\Terrain\Base.map"`, and no other `.map` name is built anywhere.

**Original loader (tp.exe, October 16, 2026).** 0x5192D0 reads the file through the generic chunk parser
0x4D3860, which checks only `TP2M` and a minimum size of 0x2C, walks chunks from 0x24 and stops at an empty
`END `; the title is not checked. The `MAP ` handler 0x4D8CF0 reads width and height, skips the five header
words without reading them, and stores each byte at tile `(column << 7) + row + 1`, +0x26 (68-byte tiles at
`[0x7CF83C] + 0x294`), keeping coordinates 0–127. The tile initialiser 0x536490 then interprets the cell:
0x08 lays the initial path (0x5348D0); 0x10 sets tile flag 0x80; on a heightfield hole 0x02 gives kind 2,
else 0x80 kind 0x1E, else 7. On a surface cell bit 0x01 picks kind 5, 0x02 kind 2 and 0x80 kind 0x1E, but
the 0x08 test right after overwrites the kind with 1 or 0 (0x536524..0x536548), so **bit 0x01 has no effect**:
blocking comes from the heightfield hole.

**Original build check (tp.exe, October 16, 2026).** The cursor handler 0x5234D0 places an object through
0x528A70, which tests every footprint cell through 0x532FC0 (mode 0x200) with the cell validator 0x535670.
The validator reads the cell's kind (+8), flags (+0xE: 0x40 unowned, 0x20 path), path links (+0xC) and owner
(+0x10), never the MAP byte (+0x26): holes (kinds 2, 0x1E, 7) refuse by kind, the initial path by its flag 0x20, and a
`Blocked` cell with a surface is kind 0 like any other ground, buildable once the park owns it.
`OriginalParkPlacement` follows this: its terrain rule is the map-start kind (`ParkLand.OriginalCellType`,
the heightfield cell word's bit 0 for the hole test), plus imported save paths and objects. Measured over the
four `base.map` levels, 318–326 surface cells per theme carry `Blocked` (all value 1, the edge ring and the
ground round rocks), plus fantasy's value-16 cell (27, 17); of these only space (45, 18) and (50, 18) and
fantasy (27, 17) lie in the starting land, the rest become buildable when bought.

## Reader limits

`MapFile` caps input at 4 MiB, width/height at 1024 and cells at 1,048,576, checks
dimensions before allocating, requires exactly one `MAP ` chunk whose size equals
28 + width × height, and an empty final `END ` chunk with no trailing bytes.
Unknown chunk tags raise `NotSupportedException` (none observed); other malformed
input raises `InvalidDataException`. Caller-owned streams stay open and
nonseekable short reads work.

## Raw-preserving writer

`MapFile.RawTitleSlot` retains a copied, read-only 32-byte title slot, including
its NUL and all uninterpreted trailing bytes. `Title` remains the existing ASCII
display value; the writer never re-encodes it. `map.Encode()` and `map.Write(output)`
serialize the parsed model. Static `MapFile.Encode(width, height, rawTitleSlot,
opaqueHeaderValues, cells)` and `Write(output, ...)` also accept explicitly supplied
raw fields, validating the same reader limits before allocating the output buffer.

The result is `TP2M`, the exact title slot, one `MAP ` chunk with payload size
28 + width × height, little-endian dimensions and five opaque u32 values, every
raw cell byte in the existing x-major order, and an empty final `END `. A caller
may edit the existing `Cells` array; no bit masking, terrain interpretation or
header/title default is applied. The title must be exactly 32 bytes with a NUL,
the header must contain exactly five values, and cell count must match dimensions.
Dimension/product/file-size arithmetic is checked against the existing bounds.

All validation and encoding complete before the first stream write. Writing uses
the caller's current position and leaves the stream open without seeking,
truncating or flushing. A physical I/O failure propagates and may leave partial
bytes; this stream API does not promise file replacement or rollback. There is no
CLI, terrain-rule change, native-writer parity or original-save interoperability
claim. Original-asset round trips were not run for this slice. Raw input lists and the mutable `Cells` array must remain stable during encoding; this API does not synchronize concurrent edits. The strict reader refuses unknown chunks, duplicate MAP chunks and nonempty/nonfinal END chunks, so no accepted unrepresented chunk payload is silently discarded.

`MapFileWriterTests` locks nonsquare read/write byte equality with all raw cell
values, high-bit title padding and opaque header words; one-cell edit/readback;
empty/final-slot title terminators; existing maximum grid; count/shape/NUL
preflight and allocation refusal; current-position/nonseekable ownership; and
partial I/O propagation. These controls use synthetic containers.

## Tests

`source/OpenTPW.Tests/MapFileTests.cs`: 20 synthetic cases (header, opaque values,
cell indexing, truncation, magic/title, dimension limits, chunk size mismatch,
missing/duplicate/unknown/non-final chunks, sound-catalog signature, short reads,
oversized input) and private-asset cases: five pinned fixtures, cross-level
identical positions of values 8/17/144/148, heightfield holes and ground texture
slots versus MAP flags in all four themes, Standard.sam fixed items on the
expected flags, both speaker catalog members and all 62 loose catalog `.map`
files rejected. `OriginalParkImportTests` covers `GetCellAt`/`GetFlagsAt`;
`Md2ModelFileTests` the synthetic heightfield block. Asset tests are
inconclusive without `OPENTPW_GAME_PATH`.

## Remaining gates

- Heightfield: the cell-word low bits are read by tp.exe (MD2-MODELS.md: triangle diagonal, texture turn
  and mirror; the diagonal is applied); the texture turn and mirror are not drawn yet.
- The rest of the validator 0x535670's object branches (the second-layer link tests and the cost of the
  access cells the placement lays); see OBJECTS.md "Build rules" and RIDES-018.
- Sound catalog `.map` schema: decoded separately, see [AUDIO.md](AUDIO.md).
