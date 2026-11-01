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
(Superseded October 16, 2026: the PC placement `0x528A70` does choose `m_front`, `m_enter` and `m_exit`, see BLUEPRINT-003 below, and OpenTPW draws them.)

## In OpenTPW

`BlueprintMarkers` (a `ModelEntity`) builds this geometry for a list of cells with the `blue` and `red`
textures, and the HUD (`ParkHud.UpdateBlueprint`) feeds it the path or queue tool's preview line and, while
an object is being placed, the object's footprint under the cursor. The cost text beside the line stays.

Open points (`BlueprintApproximations`):

- BLUEPRINT-001: marker height. The PC SetCell `0x532FC0` (October 15) passes 0 for a plain cell and `10 × ((int)H + 9) / 10` of the cell
  height `0x452AE0` (the owner object's `.hmp`, else the terrain sampler `0x4527F0`) for a second-layer cell of type 11, 12, 16, 17 or 25 and then a map
  cell of type 4, 7, 9, 10 or 30 (`0x5330AD`–`0x533189`); the neighbour raise `0x536CA0` belongs only to the 2 × 2 track ghost `0x5370E0`. The
  vertex writer `0x53DDD0` adds to every vertex of a texture 0–14 face the terrain at that vertex and `sine[round((phase + x + z) × 4096 / 2π) & 4095]`
  (table `0x7B0968` filled by `0x4708D0` with `sin(i / 4096 × 2π)`, phase `0x874FC0`, + 0.1 per unpaused frame at `0x53C762`), so the markers drape over the
  ground, 1.5 above it, and ripple by ±1 unit. October 16: OpenTPW does this for every cell (`BlueprintMarkers.VertexHeight`, `Ripple`, `Update`), steps the phase
  0.1 per 1/30 s of real time (`[EXT:blueprint-phase-step]`, user approved: the original steps per rendered frame, which would tie the ripple speed to the frame rate), and originally had no walls. October 20: bounded typed TPW object offsets and stepped walls are implemented below; unsupported/missing-map/secondary/TPI cases remain open. The earlier claim that the host never types object cells is obsolete.
- BLUEPRINT-002 (retired October 14, 2026): texture coordinates. The PC table `0x871EE8` holds the unit square in entries 0–3 and (0.5, 0.5) in
  entry 4 (filled by `0x53C3F0`); entries 5–8 are the rotating ±0.4 square of the track symbols. The face builder `0x53DF30` uses the rotating set only
  for textures 8–14 and the unit square for all others, with the corner order by direction byte (default 0,1,2,3; `0x40`: 1,3,0,2; `0x10`: 3,2,1,0;
  4: 2,0,3,1), which shows only on a non-uniform texture. The blue and red markers match (a whole texture per face, both sides drawn), so nothing changed
  in the code. October 16: the arrow markers (`m_front`, `m_enter`, `m_exit`) are the non-uniform ones, so the corner order is now applied (`BlueprintMarkers.CornerOrder`).
- BLUEPRINT-003: which cells get which marker. The PC twins (October 14-16, 2026): AddFace `0x53C8D0` (face records `0x820B00 + i·0xA0`; callers
  `0x46C480`, `0x4FA030`, `0x532FC0`, `0x5370E0`), the ghost routine `0x5370E0` with chooser `0x539760` and `0x532FC0` (called from `0x452D30`, `0x5286A0`,
  `0x528A70`, `0x536100`). The line preview `LayLine 0x536100` draws every cell of the line (the ghost `SetCell` returns 1 for allowed and refused cells,
  `0x53391D`/`0x534009`), and the validator `0x535670` refuses every later cell once its flag `0x816D50` is set (`0x5358A0`; reset at `0x527C52`): OpenTPW now
  marks every cell and turns the refused cell and all later cells red (`BlueprintGhosts.LineCells`). The footprint caller is the object placement `0x528A70`
  in ghost mode: the cells of row 0 of the shape (the last text row) are drawn with `m_front` (texture 6) turned by the build direction byte (1, `0x40`, `0x10`, 4 for
  0, 90, 180, 270; `0x52967E`), the other cells blue; a failed whole-placement preflight forces all stamped ghost cells red (the caller correction below), and otherwise the per-cell chooser applies; with the queue option (`0x52FAB0`, default on) the entrance's outside cell gets `m_enter` (3, `0x52989A`)
  and, after the entrance-side bounds check and stamp, the exit's outside cell `m_exit` (4, `0x529B1C`), each turned to the opposite of its rotated symbol direction (`0x4D8C00` after `0x4D8C20`);
  without the queue option the entrance's outside cell gets `m_inout` (7, `0x5299AE`). OpenTPW draws all of these (`BlueprintGhosts.Footprint`) except `m_inout`. The track symbols (`m_direct`,
  `m_link`, `m_end`; `m_break` and `m_erase` are drawn over the track edit's pick and cut tiles since October 27, 2026, with the tile raise `0x536CA0`, the walls of `0x5370E0`, the glide of `0x53C3F0`/`0x53DF30` and the red faces' blink) and the arrow models `garrow.MD2`/`rarrow.MD2` are not drawn yet; the validator's answer for a line cell that already holds the line's type is not read.

## Object preflight refusal correction (October 18, 2026)

The earlier BLUEPRINT-003 claim that a refused object preview should colour only independently refused cells was too broad.
The PC ordinary-object hover checks the entire placement before it draws the ghost. A failed check forces the subsequent ghost
red, even for a refusal whose individual validator return is non-sticky. OpenTPW's all-red ordinary footprint therefore does not
need to be replaced with mixed per-cell feedback on this evidence.

### Caller, arguments and return chain

Bounded static inspection of the PC code establishes the following chain without executing original software:

1. The hover `0x5234D0` clears `0x816D48`, the sticky refusal `0x816D50` and related flags at `0x5234EC` onward. The ordinary
   build route clears the separate force-red word `0x81B604` at `0x52367D`.
2. At `0x52390E` it calls placement `0x528A70` with six arguments: cell X, cell Y, type 4, rotation, commit = 0, check = 1.
   In placement's normal stack frame these last two arguments are read at `[esp+0x90]` and `[esp+0x94]`. The ordinary footprint
   branch `0x529646`–`0x529676` skips ghost/check work when commit is set, chooses `SetCell(0x204)` when check is set and chooses
   `SetCell(0x104)` for the ghost otherwise. Entrance and exit footprint branches also check through `0x204`.
3. `SetCell`'s `0x200` branch (`0x532FEE`–`0x53300F`) calls validator `0x535670` and returns 0 exactly when the validator
   returns 1 (refused). The ordinary footprint check branches to placement's zero return at `0x529CED` when that happens
   (`0x52966E`). A queued entrance's outside cell uses `0x204` at `0x5297A4`; its failed check returns 0 through
   `0x5298C0`–`0x5298D4` as well.
4. A zero placement result makes the hover set `0x81B604 = 1` at `0x52391A`. It then calls the same placement immediately at
   `0x5239EE` with commit = 0, check = 0. The Move hover has the same two-pass structure at `0x5239C4`, `0x5239D0` and
   `0x5239EE`.
5. In the ghost branch, `SetCell` reads `0x81B604` at `0x53319A`. When set, it takes refusal before consulting that cell's
   validator (`0x5331A7`–`0x5331BF`), and the refused face path uses red. A front/access arrow supplied by the caller does not
   override this global refusal.

This also rules out the proposed fixed-path exception: type 4 on a NoModify cell returns refusal at `0x5356AA` → `0x535B31`
without setting `0x816D50`, but that is still a failed preflight and therefore sets `0x81B604` before drawing. For ordinary
footprint cells and a queue entrance's outside cell, preflight and ghost use the same type-4 cell rule and the same coordinates;
no commit runs between them. The inspected route provides no successful first-pass case that newly acquires that NoModify
refusal in its immediate ghost pass. Other shape branches and tool modes are not generalized from this result.

### Refusal word versus marker colour

The refused ghost calls `0x527940(1)` after its second validator call. That helper only writes the separate word `0x816D48`;
it does not write `0x816D50` or `0x81B604`. Direct references found in the retained code spans read `0x816D48` to gate click work
(`0x5247F0`, `0x52482C`, `0x5249E0`, `0x524A63`) or clear it. Its getter `0x527950` is called at `0x52F9F3`, where it selects
the no-place pointer 8 through the pointer setter `0x4A2AA0`. Those inspected consumers do not recolour the blueprint after
stamping. The positive force-red caller chain above, rather than an assumption that this separate word has no consumers,
is the basis for the correction.

### Outside access stamps and bounds

For an ordinary shape with one entrance and one exit, both footprint cells in bounds, the ghost pass still reaches the
outside access work after a failed preflight. With the queue option enabled, it calls `SetCell(0x104, 3, direction)` for the
entrance's outside cell at `0x52989A` and `SetCell(0x118, 4, direction)` for the exit's outside cell at `0x529B1C`. Both ghost
types reach the force-red check above; when it is set, these faces are red rather than access arrows. There is no early
return merely because the footprint ghost is refused: refused `SetCell` returns 1 at `0x534009`.

The order of the bounds checks matters. The helper `0x4D8300` accepts exactly `0 <= x,y < 128`. The entrance-side check at
`0x529744` returns from placement immediately on failure (`0x52974E`), before either outside stamp. The exit-side check at
`0x5299E2` also returns on failure (`0x5299EC`), but the entrance-side ghost has already been stamped. With the queue option
off, the entrance-side ghost instead uses `0x118`/`m_inout` at `0x5299AE`, and the exit-side work is skipped at `0x5299D2`.
The option helper `0x52FAB0` returns `0x81B0CC` only when the add-on selection `0x8187F8` is zero, and otherwise returns zero.

The older shorthand that an exit is drawn "only after an entrance" is not an exact presence test. The placement records
an entrance flag at normal-frame `[esp+0x12]` (`0x52932D`, with two arguments then pushed) and an exit flag at `[esp+0x13]`
(`0x529243`, likewise). At `0x529711`, one argument is still pushed, so `[esp+0x16]` reads the entrance flag; after popping
that argument, `0x52971C` reads the exit flag. Either flag enters the outside-stamp sequence. The coordinate locals start at
zero and are overwritten whenever the in-bounds footprint loop encounters another matching access symbol. Thus missing
partners and multiple entrances/exits cannot be inferred from OpenTPW's current first-entrance/first-exit selection. These
cases, and the distinction between a skipped out-of-bounds footprint cell (`0x52903D`) and an outside-cell bounds failure,
remain separate work. Only the ordinary paired-access case is implemented below.

### Scope and remaining work

Only the incorrect independent-cell expectation is removed from the register. `BLUEPRINT-003` stays open:

- The context-free `BlueprintGhosts.Footprint` fallback returns before adding refused outside access markers. The grid-aware
  overload now implements the bounded paired-access case below; other access selection and bounds cases remain open.
- Special shape branches (`@`, `Q`, `+`, `#` and track ends), unsupported arrow models and track markers still need their own work.
- The existing line-cell validator, Buy Land fence ghost and no-queue `m_inout`/queue-option residuals remain unchanged.

The evidence correction retains whole-footprint refusal, placement, overwrite, refund, economy, decoder and simulation behavior.
Both HUDs keep their existing colour policies. It does not establish original object money-colour behavior or retire the whole ID.
The existing all-red/no-arrow regression now covers all four rotations. No original binary, disassembly or assets are included.

## Bounded refused-access preview (October 18, 2026)

`BlueprintGhosts.Footprint` has a grid-aware overload for the proven omission: the outside cells of an ordinary refused queued
object can still need red faces. It adds those faces only when all of these conditions hold:

- the shape contains only free cells and ordinary types 4, 9 and 10;
- exactly one entrance and one exit are present, and the whole shape bounding box is within the host grid;
- the caller enables the queue branch, the placement result is `Path`, `Occupied`, `NotOwned`, `AccessBlocked` or
  `OutsideTerrain`, and the caller's existing colour decision is refused.

The entrance's outside cell is checked against the host grid dimensions first. If it is outside, neither access face is added.
Otherwise it is added red, followed by the exit's red face only if its outside cell is in bounds. The footprint remains entirely
red, and no direction arrows are added. Other inputs retain the existing fallback, including money/global-only refusal, unproven
refusal reasons, missing/multiple access points, partly off-grid shapes, special symbols and no-queue mode. Accepted previews
are unchanged; this does not implement `m_inout` or change which access cells actual placement accepts or lays.

Both HUDs call `Level.ObjectBlueprint`, which disables the new branch for tools, add-on entries and active add-on mode. It uses
their already-computed placement result and colour decision: TPW still includes its separate purchase check, while TPI still
colours by placement alone. The adapter does not repeat validation, consult the economy or buy anything. No validators, path
clearing, overwrite priorities, purchases, refunds or simulation code changed.

Synthetic regressions cover all rotations, non-square host bounds, entrance/exit ordering, footprint bounds, fixed paths,
ownership/occupancy refusal in both footprint and access cells, accepted/fallback output and repeated preview/cancel without
cell-map writes, object changes or ledger charges. They use no original assets. Local .NET execution remains blocked by the
missing SDK; the test command was attempted rather than reported as passed.

## Bounded occupied-cell height implementation (October 20, 2026)

Further read-only PC inspection resolves the top/wall contract independently of the PPC analysis.
`0x5330C6`–`0x533189` uses `Q(H) = 10 * trunc0((trunc0(H) + 9) / 10)`; the float conversion helper
`0x67A830` explicitly selects truncation toward zero, and the multiply-high/signed-shift correction implements
signed division by ten. This is not mathematical ceiling: H = 0.9 gives 0, H = 10.9 gives 10, H = -18.9 gives 0,
and H = -19 gives -10. Second-layer types 11/12/16/17/25 first set Q; base types 4/7/9/10/30 overwrite it using
the same `0x452AE0(x,y,0)` sampler, not a sum or maximum. That sampler looks for a second-layer instance first,
then the owner object, and finally terrain when no instance height map exists.

The top face is Q + 1.5. Each of the four PC neighbour loops (`0x53323C`–`0x53391B`, and the refused duplicates
`0x53397E`–`0x534007`) skips a missing neighbour and emits side quads only from lower neighbour Q to current Q,
in ten-unit steps. Neighbours come from the map even when they are not preview cells. The face codes 1/4/16/64
select the -Y/+X/+Y/-X sides in the PC face builder `0x53DF30`; subtracting the -10 constant at `0x700E1C`
adds the vertical step. Allowed sides use blue, except an area top (texture 2) also has area sides; refused
sides use red. Access/front arrows and their rotation do not propagate to side faces. `0x53DDD0` adds terrain
at each vertex and ripple to both tops and walls, so Q is an additional offset, not a replacement terrain height.

The host implementation deliberately supports only TPW ordinary primary cells 4/9/10 whose live owner word
matches the footprint object and whose existing `ObjectHeightMaps.Height` supplies a valid map sample. Plain
cells 0/1/3 have known zero offset. Any nonzero track/structure cell or fence post is excluded before owner
sampling; it does not call the generic tour sampler or assert track-priority compatibility. Unsupported cells
retain their old unraised top and do not acquire guessed walls. Missing, malformed, inaccessible or out-of-range
height maps all remain unresolved here, including the native missing-map terrain-quantization branch. Types
7/30, secondary-layer parity, and all TPI behavior remain open. BLUEPRINT-001 is not retired.

Preview samples include the current cell and four map neighbours. They are captured when the preview is shown;
a changed snapshot rebuilds topology even while paused, whereas ripple-only frame updates preserve the vertex
count. Synthetic geometry cases cover both-sided stepped walls, all four sides, unequal/equal/higher/unknown
neighbours, red/blue/area textures, arrow isolation, signed quantization and boundary sampling. No original assets
or disassembly are published with this implementation; no BLUEPRINT-003 marker-selection behavior changes.

## Typed ghosts of the line tools and Buy Land (November 1, 2026)

Round 11, package F. The PC ghost routines were replayed in the x86 oracle (docs/reverse/ORACLE.md, X50 to X52) and ported for a typed TPW world:

- **BLUEPRINT-001.** The raise is the quantized ground of `0x452AE0` for a map type 4, 7, 9, 10 or 30 and a second-layer record type 0xB, 0xC, 0x10, 0x11 or 0x19 (`BlueprintHeights`); the tile ghost's raise `0x536CA0` is the last writer of the origin record and the map cells +0, +Y, +X, +X+Y (`TileLineGhost.TileRaise`). Types 7 and 30 and the fence posts are raised; a world that types no cells, and Theme Park Inc, keep the terrain.
- **BLUEPRINT-003.** A path or queue line cell takes the validator's answer (`m_link` for 8, `m_end` for 0xB, including the answer for a cell that already holds the line's type, 0x535670 after 0x5358B5); the kart and water line tools and the Buy Land fence draw one 2 x 2 face per tile by the tile chooser (blue, red, `m_end`), the Buy Land ghost over the four edges of its rectangle (`TileLineGhost.FencePasses`, red throughout while the land validator refuses, the hovered post's tile alone before the first click). A footprint's track end gets `m_direct` beside it, and the kinds '@', '+' and '#' no front arrow (0x528A70 jump table: kind 1 stamps (0x104, 0, 0), kinds 3 and 4 the default front row, kinds 11 and 16 (0x10B, 0x110), kind 23 its own cell blue and `m_direct` on the neighbour). `garrow.MD2` and `rarrow.MD2` are named by the theme tables but no code of tp.exe draws them.
- **UI-100.** The tile chooser's end word 0x816D4C (answer 0xB at 0x53A1C6) and cash word 0x816D5C (the money test at 0x539F95, the write at 0x539FB6) give the kart and water lines their end and cash pointers. The crosshair art `CCro.tga` is not in the game data.
- **UI-031.** The pass 0x81 returns 0 for a line whose last cell met an existing path or queue cell (0x5340A5, the flag 0x820ABC, which 0x5345AD sets only while the last-cell word 0x820AB8 is nonzero (the gate at 0x534584..0x534593) and the cell is a path or queue cell), which takes 0x526568 (the handover to the queue tool while the queue start 0x8186C0 is pending, else the tool ends at 0x5266BC); any other result falls to 0x526527. The original's coaster placement enters the queue tool; its track tool (mode 0x15, coaster sub-mode 3, 0x497D20) is entered by the ride window's track button (0x4AF200 to 0x4AF275 to 0x4988C0), by a click on the coaster in the park (class 6, 0x487E56 to 0x487EEB to 0x4988C0, while 0x441910 says the track is not complete) and by the coaster window's button 0x1F (0x498333); OpenTPW still enters it at the placement. A tool armed from the buy list starts at the angle the placement kept: the buy click arms it (0x46C350 at 0x4AC65E, before the ride check at 0x4AC683, which only chooses the help line 0xC9 or 0xCE) and calls no angle setter, its activation 0x46D5A0 runs the hover 0x5234D0 (which calls the setter only for a track add-on, 0x5237F7) and SetMode(4, 1) (0x52F200 resets the angle only for mode 0, 0x52F3B6), and the other writers of 0x81D7A4 are the park-start reset 0x52F050 (called only at 0x457D34) and the setter's seven callers. Where this leaves OpenTPW, and the path-start queue cell, stay open.

Residuals found in the review of the same round (all stay in BLUEPRINT-003): the kind 23 commit branch 0x5295F7..0x52965C and its link to the cell beyond (0x4D8C20 turns a direction byte by the placement rotation, 0x4D97E0 answers the neighbouring cell or none) are carried by ParkObjects' beyond-cell refusal (0x5295A8) and `ParkCellMap.WriteFootprint` but not replayed against the executable; a shape 'D' (direction 0) takes 0x529581 with no own face and no cell beyond, so the check refuses it and only a refused ghost meets it; the face adder 0x53DDD0 takes a terrain flag ([EBP+0x1C]) per caller (0x53E083..0x53E580) that is not matched per texture for the m_direct face. The tile ghost's entry gate 0x537103..0x537164 reads the answer and direction arguments only for the modes 4, 0xB and 0x10 with the argument 5; the walker passes its step (-2 or 2), so the default branch always applies. The refusal word 0x816D48 is also read by the click handler (0x5247F0, 0x52482C, 0x5249E0, 0x524A63) and written by 0x5234EC, 0x524AD9 and 0x527C48. The kinds 11 and 16 set the flag word 0x10 of the map cell and of the second layer's record (0x53A510 at 0x52950D and 0x52951A), which are `ParkCellFlags.AddOn` and `TrackCell.FlagAddOn`.
