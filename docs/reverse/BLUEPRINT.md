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

## In OpenTPW

`BlueprintMarkers` (a `ModelEntity`) builds this geometry for a list of cells with the `blue` and `red`
textures, and the HUD (`ParkHud.UpdateBlueprint`) feeds it the path or queue tool's preview line and, while
an object is being placed, the object's footprint under the cursor. The cost text beside the line stays.

Open points (`BlueprintApproximations`):

- BLUEPRINT-001: every cell takes its corner height rounded up; the original's 0 for plain cells and the
  raise next to rides (`0x10086030`) are not reproduced.
- BLUEPRINT-002: texture coordinates. The original's per-corner coordinates come from a runtime table
  (`0x101ED3D4`) rotated by the face's direction code.
- BLUEPRINT-003: which cells get which marker. Existing path cells of a line are drawn blue, and a footprint
  is red when the build rule or the purchase refuses it; the chooser's exact result for existing cells and
  the footprint caller `0x10075618` were not read in full. The `m_*` symbols (entrance/exit arrows, track
  markers) and the arrow models `garrow.MD2`/`rarrow.MD2` are not drawn yet.
