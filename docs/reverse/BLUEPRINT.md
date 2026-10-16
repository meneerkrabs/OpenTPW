# Build-tool blueprint markers

2026-10-11. Static analysis of the Mac PowerPC `SimThemePark.data` (SHA-256 `04809cd4…e295f5`, see
[FINDINGS.md](FINDINGS.md)) with Ghidra 12.1.4. No original program was run, and no decompiled code is
kept in this repository. Addresses are Ghidra addresses: code starts at `0x10000000`.

## Question

When the player lays a path or a queue, or places an object, the original shows blue (and red) markers on
the cells the tool would change. OpenTPW drew small coloured squares in the interface instead (PATH-001).
What does the original draw, and from which data?

## Textures

`0x1022C374` is a table of 16-byte records whose first word points at a texture name. The names are loaded
from `Data:Generic:Dynamic:Textures` (string at `0x101CC223`); the PC CD has the same files as
`Data/generic/dynamic/textures/*.wct`. A face's texture is its index in this table:

| Index | Texture | | Index | Texture |
| ---: | --- | --- | ---: | --- |
| 0 | `blue` | | 7 | `m_inout` |
| 1 | `red` | | 8 | `m_link` |
| 2 | `orange` | | 9 | `m_break` |
| 3 | `m_enter` | | 10 | `m_cross` |
| 4 | `m_exit` | | 11 | `m_end` |
| 5 | `m_direct` | | 12 | `m_nocash` |
| 6 | `m_front` | | 13 | `m_erase` |

Indices 15–17 are `gby_sur1`–`gby_sur3`, chosen by `rand() % 3 + 15` in `0x100BCB60`, which confirms the
numbering. `orange.tga` is not on the PC CD. The theme tables' `PathTex` entries 16–18 (`baseblue`,
`grnarrow`, `redarrow`) name files that no archive contains; the original does not use them for markers.

## The BlueprintMesh

`0x100BCB60` creates three dynamic meshes: `BlueprintMesh` (up to 0x600 faces), `WaterMesh` and
`FlagsMesh`. `AddFace` `0x100BD3D4(x, y, z, f4, code, texture, …)` appends a square face record (0xA0
bytes); the record's mesh, texture and draw code are kept for sorting by texture.

`0x100BF540` turns a square face into geometry, with the face size `DAT_101F1240` = 10.0 (one cell):

- code 0: a flat quad from (x, y, z) to (x + 10, y, z + 10);
- codes 1, 4, 0x10 and 0x40: a vertical quad, 10 high, on the cell's −Y, +X, +Y or −X edge.

The vertex builder `0x100BF388` takes y as given for the blueprint mesh (only the water mesh adds the
terrain height).

## Ghosts of a path or queue line

`LayLine(mode | 0x100)` (PATH-plan §4.3) calls `SetCell` `0x100866B8` with the ghost flag for each cell.
The ghost branch:

1. **Position**: x = cell X × 10, z = cell Y × 10 (`mulli 0xa` at `0x10086908`), y = height + 1.5
   (TOC −0x3070).
2. **Height** (`0x10086794`): 0 for a plain cell; for cells of types 4, 7, 9, 10, 0xB, 0x10 and 0x19 (rides,
   track) the terrain height at the cell corner from `0x100509E8`, rounded up to a multiple of 10
   (`((int)h + 9) / 10 × 10`). `0x10086030` raises the height next to those types.
3. **Texture**: the chooser `0x100895D8` returns 0 (blue) for an allowed cell and 1 (red) for a refused one,
   including a line the park cannot pay for; other codes (9 `m_break`, 11 `m_end`, 13 `m_erase`) belong to
   track types 0xB/0x10/0x19.
4. **Walls** (`0x10086B9C`): for each neighbour whose marker height differs from the cell's, edge faces
   are added from the neighbour's height in steps of 10 (`0x10086C2C`) while below the cell's height.
   The same faces are drawn in red for a refused cell (`0x100873E4`…).

`0x10080C50` is the same per-cell ghost routine for other build modes; its validator is `0x10084154`, and
`0x10075618` calls it for many cells (object footprints).

## Which markers the build modes use

2026-10-12, same binary and tools. `AddFace` has four callers: the two per-cell ghost routines
`0x100866B8` (`SetCell`) and `0x10080C50`, and `0x1006BC38` and `0x100E5EE0`, which each add one flat red
face (code 0, texture 1).

- **`SetCell` default texture** (`0x10086778`/`0x10086784`): the first face takes the chooser's result, or,
  when that is 0, the value in r17. r17 is 0 (blue) except for track cells (types 0xB and 0x10), where it
  can be 5 (`m_direct`).
- **`0x10084154`**, the validator of `0x10080C50`, returns 0, 1, 2 (`orange`, mode 0x1F), 8 (`m_link`),
  0xB (`m_end`) and 0xD (`m_erase`, mode 0x3A) as constants. One branch returns a byte of the cell itself
  (`0x1006E0E4`: the cell's +0xC byte, or +0x22 for a cell whose +2 byte is 2 while the global at
  `_DAT_101ECF68` is set). It never returns 3, 4 or 7 as a constant.
- In modes 1 and 3 that routine first reads `0x1007BC58`/`0x1007BC70`. These are not texture indices: they
  are the path and queue cell costs, set through `0x1007BC4C`/`0x1007BC64` by the cost loader `0x1010F29C`.

So in the routines read, the entrance and exit symbols (`m_enter`, `m_exit`, `m_inout`) and `m_front`,
`m_cross` and `m_nocash` are never chosen as constants, and nothing marks an object's entrance or exit
cell. The arrow symbols that do appear belong to track building (`m_direct`, `m_link`, `m_end`,
`m_break`, `m_erase`). The only open source of other codes is the cell byte returned by `0x1006E0E4`.
OpenTPW therefore draws an object footprint in blue or red only, without entrance or exit arrows.

## In OpenTPW

`BlueprintMarkers` (a `ModelEntity`) builds this geometry for a list of cells with the `blue` and `red`
textures, and the HUD (`ParkHud.UpdateBlueprint`) feeds it the path or queue tool's preview line and, while
an object is being placed, the object's footprint under the cursor. The cost text beside the line stays.

Open points (`BlueprintApproximations`):

- BLUEPRINT-001: every cell takes its corner height rounded up. The PC SetCell `0x532FC0` (October 15) passes 0 for a
  plain cell and `10 × ((int)H + 9) / 10` of the cell height `0x452AE0` (the owner object's `.hmp`, else the terrain
  sampler `0x4527F0`) for a second-layer cell of type 11, 12, 16, 17 or 25 and then a map cell of type 4, 7, 9, 10 or
  30 (`0x5330AD`–`0x533189`); the neighbour raise `0x536CA0` belongs only to the 2 × 2 track ghost `0x5370E0`. The
  vertex writer `0x53DDD0` adds to every vertex of a texture 0–14 face the terrain at that vertex and `sin(phase + x + z)`
  (phase `0x874FC0`, + 0.1 per unpaused frame at `0x53C762`), so the original's markers drape over the ground and ripple
  by ±1 unit. Neither is reproduced.
- BLUEPRINT-002 (retired October 14, 2026): texture coordinates. The PC table `0x871EE8` holds the unit square in entries 0–3 and (0.5, 0.5) in
  entry 4 (filled by `0x53C3F0`); entries 5–8 are the rotating ±0.4 square of the track symbols. The face builder `0x53DF30` uses the rotating set only
  for textures 8–14 and the unit square for all others, with the corner order by direction byte (default 0,1,2,3; `0x40`: 1,3,0,2; `0x10`: 3,2,1,0;
  4: 2,0,3,1), which shows only on a non-uniform texture. The blue and red markers match (a whole texture per face, both sides drawn), so nothing changed
  in the code; the corner order is not applied.
- BLUEPRINT-003: which cells get which marker. Existing path cells of a line are drawn blue, and a footprint
  is red when the build rule or the purchase refuses it; the chooser's exact result for existing cells and
  the footprint caller `0x10075618` were not read in full. The PC twins (October 14, 2026): AddFace `0x53C8D0` (face records `0x820B00 + i·0xA0`; callers
  `0x46C480`, `0x4FA030`, `0x532FC0`, `0x5370E0`), the ghost routine `0x5370E0` (called from `0x52BA50` and `0x53AAC0`) with chooser `0x539760` (1: the refused
  branch `0x537A1B`, 0: the default texture, otherwise that texture) and `0x532FC0` (called from `0x452D30`, `0x5286A0`, `0x528A70`, `0x536100`); still to read:
  the chooser's result for existing path cells and the footprint caller among the `0x532FC0` callers. October 15: the line preview
  `LayLine 0x536100` draws every cell of the line (the ghost `SetCell` returns 1 for allowed and refused cells, `0x53391D`/`0x534009`), and the
  validator `0x535670` refuses every later cell once its flag `0x816D50` is set (`0x5358A0`; reset at `0x527C52`); OpenTPW stops at the first refused
  cell. The footprint caller is the object placement `0x528A70` in ghost mode: row 0 of the shape is drawn with `m_front` (texture 6, turned to the build
  direction, `0x52967E`), the other cells blue, each red when refused; the entrance's outside cell gets `m_enter` (3, `0x52989A`), or `m_inout` (7,
  `0x5299AE`) when option `0x81B0CC` lays no queue, and the exit's outside cell `m_exit` (4, `0x529B1C`). So the entrance and exit symbols are chosen
  after all (correcting the section above, which read only the Mac `0x10080C50` path); OpenTPW does not draw them yet. The track symbols (`m_direct`, `m_link`, `m_end`,
  `m_break`, `m_erase`) and the arrow models `garrow.MD2`/`rarrow.MD2` are not drawn yet; entrance and exit
  arrows are not drawn because no routine read chooses them (see above).
