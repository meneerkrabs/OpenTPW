# Content kit: authoring new objects

The content kit turns original, newly made art into files the game reads: MD2 models, textures, ride scripts and
sprite banks, packed as an object a data-only mod adds through `objects.json` ([MODS.md](MODS.md#objects)). It exists
for community content such as the planned swimming pools. Nothing in it reads or copies art from the original game;
the original corpus is only used to prove that each writer produces files the readers (and the formats) accept.

Status (October 17, 2026): phase 0 is complete in the code. In place:

- the Blender side (`tools/blender/`): authoring conventions, a validating glTF exporter and a generated sample object;
- `GltfScene` (`source/OpenTPW.Files/Formats/Model/GltfScene.cs`), the reader for what the exporter writes;
- the MD2 writer and `ModelBuilder` ([MD2-MODELS.md](MD2-MODELS.md#writing-and-authoring)), the RSE text form and assembler
  ([RSE-SCRIPTS.md](RSE-SCRIPTS.md#text-form-and-assembler)) and the ESP/FPC/TPC sprite writers
  ([GUESTS.md](GUESTS.md#guest-graphics-sprites-not-models)): each proven against the original files;
- `GltfModelConverter` (glTF to `ModelBuilder`), `TgaWriter` and the command line `opentpw-kit`
  (`source/OpenTPW.ContentKit`), described below;
- an end-to-end check that a built object loads through the mod pipeline, runs its script, and is bought, placed, queued and
  ridden in a headless park.

Not done: nothing has been run in the original game or in the renderer (see "What is unverified").

```
Blender scene --opentpw_export.py--> pool.glb --opentpw-kit object--> archive folder --opentpw-kit mod--> mod folder
                                                (pool.MD2, textures/*.tga, Pool.sam, Pool.RSE)        (opentpw-mod.json, objects.json)
```

## Blender conventions

`tools/blender/opentpw_conventions.py` holds the rules, and the exporter checks them before it writes anything:

- **Units and axes.** One Blender unit is one MD2 unit; one park cell is 10 units ([MAP.md](MAP.md)), and a kid guest is
  about one cell tall ([GUESTS.md](GUESTS.md#rendering)). A footprint covers the Blender X/Y range `0..10·width` and
  `0..10·height`, and seen from above (top view, X right, Y up the screen) it reads exactly like its `Info.Shape` text: the
  first text row at the top, columns to the right. The converter maps Blender `(x, y, z)` to MD2 `(x, z, y)`; see
  "Converter conventions" for why this is not a plain axis relabelling.
- **Rider heads.** An object (usually an empty) with the custom property `opentpw_head` = n becomes head record n, the
  point where a ride script's `ADDHEAD` hangs a guest's head ([GUESTS.md](GUESTS.md#riders-heads)). Ids must run from 1
  without gaps, because the original's script init counts ids from 1 until one is missing. The record type is the
  default 0xB1.
- **Materials.** `opentpw_wrap` (default true) wraps the texture in U and V (MD2 slot flag 0x10), else it is clamped;
  `opentpw_self_lit` (default false) draws the mesh in one constant colour instead of per-vertex lighting (material
  word +0xC bit 0x1, which the original uses for water, signs and glow). Turn on **backface culling** for every material:
  the game culls back faces, and a double-sided glTF material makes the converter warn.
- **Textures.** The image name becomes the texture name, `<name>.tga` in the object's `textures/` folder. MD2 texture
  names are 20-byte fields, so a name may have at most 15 characters before `.tga`. tp.exe tries `<name>.wct` and then
  `<name>.tga` in the archive's `textures` folder ([ObjectAssets](../source/OpenTPW/World/Objects/ObjectAssets.cs)),
  so `.tga` needs no wavelet encoder. See "Textures and TGA" below for the file variant.

Export a scene (or one collection) with:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b scene.blend --python tools/blender/opentpw_export.py -- --out object.glb --collection Pool
```

`tools/blender/opentpw_sample_pool.py` builds the kit's sample from code: a 3 × 3 cell pool (four rim boxes and a
self-lit water box with generated checker textures) standing on the Blender X/Y range 0..30, with six head points just above
the water, and exports it:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python tools/blender/opentpw_sample_pool.py -- --out pool.glb
```

## glTF reader

`GltfScene.Read` reads binary glTF 2.0 (`.glb`) with one embedded buffer:

- nodes with their parent-relative transform, as a row-vector matrix like MD2 nodes;
- triangle primitives with float positions, normals and the first UV set, and u8, u16 or u32 indices;
- materials with their base-colour image and whether they are double-sided;
- embedded images, kept as their PNG or JPEG bytes;
- the `extras` of nodes and materials.

It refuses rather than ignores anything it cannot carry into an MD2 object: skins, animations, morph targets, sparse
accessors, external buffers or images, required extensions, non-triangle primitives, shared or cyclic children, and
indices outside the vertices. Animation is out of scope for now.

## Converter conventions

`GltfModelConverter.Convert( GltfScene )` returns a `ModelBuilder` and the images the model names. Two conventions are not
a matter of taste, because the original data fixes them. Both were determined from the original models and OpenTPW's renderer,
which draws the originals correctly; neither was compared against a screenshot of tp.exe.

### (a) Texture V: stored bottom-up, so `V = 1 - v`

glTF's V is 0 at the top row of the image. MD2's V is **1 at the top row**. Evidence:

- **The renderer flips V.** The object vertex shader (`content/shaders/test.shader`) samples at `1 - V` (`vs_out.vTexCoords.y = 1.0 - ...`),
  `ObjectAssets.ConvertMesh` passes the MD2 coordinate through unchanged, `ModelFile` reads it raw, and the decoded image's first
  row is the image's top row for both `.wct` (`TextureFile`) and `.tga` (stb, which honours the file's origin bit). So MD2 V = 1 is the top row. The
  UI renderer documents the same ("Texture V runs bottom-up as in the 3D shaders", [UI.md](UI.md)).
- **An original model with an obvious orientation.** The jungle ride archive `levels/jungle/rides/monkey.wad` (`monkey.MD2`) has the mesh `m_sign` whose texture
  `m_sign3.wct` is a monkey holding a board, head at the top of the decoded image (row 0). The quad's top edge (the larger Y) has V = 1 and its bottom edge V = 0. Its
  child `m_sign2` shows the runtime sign text and has V = 1 on its upper edge too.
- **The same on 800 sign triangles.** `Md2ConventionCorpusTests` takes every triangle whose three corners use the texture `sign1` or `sign2` (the faces of the shipped signs,
  whose pixels are an ordinary bitmap) in 120 models, puts it in the engine frame and checks from the viewer's side that V runs up on 792 of 800 triangles (the other 8 are not explained).

### (b) Handedness: MD2 is left-handed, a glTF model must be mirrored

OpenTPW's renderer shows the originals correctly, and its chain is: MD2 `(x, y, z)` (Y up) → `ObjectAssets.ConvertAxes` swaps Y and Z → engine
`(x, z, y)` with Z up, drawn by a right-handed camera (`Matrix4x4.CreateLookTo`, `CreatePerspectiveFieldOfView`) with `FrontFace.Clockwise` and back faces culled.
Evidence that this means **MD2 is a left-handed Y-up system (Direct3D's)**:

- Swapping two axes has determinant −1: if the engine frame is right-handed (it is: the camera and the culling above), the MD2 frame the original models were
  authored in is left-handed.
- **The face normals.** The corpus rule (4,714 of 4,914 meshes, [MD2-MODELS.md](MD2-MODELS.md#writing-and-authoring)) is that the stored face normal equals `cross(p1 - p0, p2 - p0)`.
  Through the swap that numeric cross product points *into* the surface for the engine's right-handed camera, which is exactly the clockwise winding `FrontFace.Clockwise` keeps. A
  right-handed model (glTF: counter-clockwise front faces) would be culled from the outside.
- **Text reads correctly only with this chain.** `Md2ConventionCorpusTests` (needs `OPENTPW_GAME_PATH`): of the 123 sign meshes in the 120 models with `sign1`/`sign2` faces,
  121 have a face whose U axis runs to the viewer's right and V axis up, seen from the side that is drawn; the rest of the 800 triangles are the back-facing duplicates most signs
  carry, whose text reads mirrored from behind. Had the frame been mirrored, every front face would fail the test. (Example: `levels/jungle/rides/monkey.wad`, `m_sign2`: the viewer stands on the −Z side
  of the face, looking along +Z, with +X to the right.)

The conversion is therefore the mirror `(x, y, z) → (x, y, −z)` of the glTF frame:

| | glTF (right-handed, Y up) | MD2 |
| --- | --- | --- |
| position, normal | `(x, y, z)` | `(x, y, −z)` |
| triangle | `(a, b, c)` counter-clockwise from the front | `(a, c, b)`: the stored face normal `cross(p1 − p0, p2 − p0)` points outward |
| node matrix (row vectors) | `M` | `R M R` with `R = diag(1, 1, −1)`: `M13, M23, M31, M32, M34, M43` change sign |
| texture V | `v` | `1 − v` |
| from Blender | `(x, y, z)` | `(x, z, y)` |

`GltfModelConverterTests` pin the mapping with a hand-built quad: after `ModelBuilder`, the file writer, `ObjectAssets.ConvertMesh` and the node composition of `OriginalObject`
(`ObjectPlacement.SwapAxes`), the quad seen from its glTF front side has U to the right and the image's top row up, for a plain quad, a rotated and translated one, and one
under a rotated parent. Negating Z (rather than X) keeps `X` right and `Y` up and turns the front to MD2's −Z side; the footprint reading above (Blender's top view equals the
shape text, because the engine frame equals Blender's) follows from the same equation, since the engine frame is Blender's frame.

### What is converted

- **Hierarchy.** One MD2 root: the scene's single root, or a dummy named `root` (made unique) when the scene has several. A node with a mesh becomes a mesh node, any
  other node a dummy; every node keeps its parent-relative transform. Names must be unique (without case) printable ASCII.
- **Meshes.** One MD2 mesh per mesh node. Primitives with the same glTF material merge into one MD2 material (a corner belongs to one material); the corners of a
  material are de-duplicated on position, normal and UV, and equal positions share one stored position. A mesh without normals gets flat face normals.
- **Materials.** `opentpw_wrap` → slot flags (0x31 wrap, 0x1 clamp); `opentpw_self_lit` → material word +0xC bit 0x1 (`ModelMaterialBuilder.SelfLit`); both accept `true`/`false` or `1`/`0`.
- **Heads.** Node extra `opentpw_head` = n → `AddHead( n )` with the default type 0xB1 (0xF1 is not used: it needs the 16-byte attribute data block the builder does not write).
- **Textures.** The glTF image's name without a known image extension is the texture name; it must be at most 15 printable ASCII characters, unique without case,
  and the image PNG or JPEG.
- **Refused, naming the node, material or image:** a primitive without a material, a material without a base-colour texture, no UVs (`TEXCOORD_0`), more than 65,535 corners (counted after splitting
  by material and by normal/UV seams), positions or triangles in one mesh, a name that is too long, not ASCII or duplicated, non-finite positions, normals, UVs or transforms, a mesh without primitives, a
  scene without nodes, head ids that are not 1..n. **Warned:** double-sided materials.

## Textures and TGA

`object` converts every texture the model names from the glTF's PNG or JPEG to `textures/<name>.tga` with `TgaWriter`: image type 2 (uncompressed true colour), 24 bits per pixel, or 32 bits
with 8 alpha bits when any pixel is translucent, bottom-left origin (descriptor 0x00 / 0x08, rows stored bottom to top), BGR(A), no image ID, followed by the 26-byte TGA 2.0 footer with no extension area.

**Why this variant.** The 160 `.tga` files of the shipped game (loose and inside WADs) are 156 uncompressed true-colour files with a bottom-left origin and a 26-byte footer (24-bit, descriptor 0x00; or
32-bit, descriptor 0x08), 3 of the same type with a 495-byte extension area, and 1 paletted file (a cursor). None is RLE-compressed or has the top-left bit. `TgaWriterTests` decodes all 155 of the
first kind that are not opaque 32-bit, writes them again with `TgaWriter` and gets the original **byte for byte (155 of 155)**. tp.exe loads `.wct` and `.tga` through one loader (`0x46EAD0` appends `.wct`
and then `.tga` and calls `0x46F120`, which reaches a codec factory at `0x5C4D50`); that factory's TGA reader was **not traced**, so which other variants it takes is unknown. The engine reads TGA rows in
file order (`World/Sky.cs`: the sky files are bottom-up and addressed as stored), so the kit writes exactly what the game's own files are, rather than a top-left file stb would also read.

Textures are not resampled. Every shipped model texture is a power of two (and at most 256 × 256 for objects); a texture that is not draws a warning, one above 4096 pixels is refused.

## The command line

`opentpw-kit` (`source/OpenTPW.ContentKit`, in `OpenTPW.sln`):

```sh
dotnet run --project source/OpenTPW.ContentKit -- <command> ...
```

**`object`**: a glb into an object archive folder.

```sh
opentpw-kit object --glb pool.glb --name pool --out mymod/objects/pool [--sam <file|template>] [--script <text .rse>] [--display-name "Pool"] [--force]
```

Writes `pool.MD2`, `textures/*.tga`, `Pool.sam` and an assembled `Pool.RSE` into the folder: the layout of an original archive such as the jungle sideshow `puzzle.wad` (`puzzle.MD2`, `Puzzle.sam`,
`Puzzle.RSE`, `textures/`; its `qickload.txt` and `.hmp` are optional and not written; the catalog needs neither). The archive name is 1 to 16 letters, digits or underscores
(the main model is `<name>.MD2`). Nothing is written when the conversion, the settings or the script fail, and existing files are only replaced with `--force`. `--sam` is a `.sam`
file (it needs an `Info.Id`) or `template`. `--script` is an RSE text source ([RSE-SCRIPTS.md](RSE-SCRIPTS.md#text-form-and-assembler)); without it the built-in pool script is assembled. A script that runs
`ADDHEAD` on a model without head records draws a warning.

**`mod`**: a mod folder.

```sh
opentpw-kit mod --out mymod --id community.pool --name "Pool" [--version 1.0.0] [--author Ann] [--description "..."] \
  --id-first 20000 --id-last 20099 \
  --object "archive=objects/pool,theme=jungle,category=rides,id=20001,name=Pool,name.German=Becken,src=mymod/objects/pool"
# or with your own documents (checked, and content.objects is set to objects.json):
opentpw-kit mod --out mymod --manifest manifest.json --objects objects.json
```

Writes `opentpw-mod.json` and `objects.json` ([MODS.md](MODS.md#manifest)) and, for an object spec with `src=`, a copy of that built folder at `archive` in the mod. The documents are
checked with the game's rules before the folder exists: id and version shapes, the Info.Id range (5000 to 65535, at most 4096 ids), ids inside it and unique, themes, categories, archive paths that stay in
the folder, names. The game's own strict loaders remain the authority; the tests load the output with them.

**`sprites`**: swimmer frames into a sprite set.

```sh
opentpw-kit sprites --frames /tmp/swimart/TPW/BE --name SPR_BE --out mymod/sprites [--hotspot 32,27] [--slot A=1 --slot B=2 --slot C=4]
```

Frames are named `<slot>_d<direction>_f<frame>.png` as `tools/blender/opentpw_swim_sprites.py` (branch `content/swim-sprites`) writes them: slot letters A (front crawl, 8 frames), B (breaststroke, 8) and C (treading
water, 4), directions 0 to 4. `SpriteSetBuilder` writes `SPR_XX.ESP`, `.TPC` and `.FPC` ([GUESTS.md](GUESTS.md#guest-graphics-sprites-not-models)). The letters go to the animation slots A → 1, B → 2, C → 4: the original kid sets have 8 × 5
frame slots at 1 (walking) and 2 (running) and 4 × 5 slots at 4, 6, 11 and 12, so the crawl and the breaststroke take the two 8-frame slots and treading water the first 4-frame one. This is the kit's own choice
(nothing in the game plays these slots as swimming; an extension picks them up by number); the other slots stay empty, so the set is not a drop-in replacement for a kid set. The hotspot defaults to
(32, 27) for the 64 × 48 frames of the Theme Park World sets and (40, 36) for the 80 × 64 frames of the Theme Park Inc sets; other sizes need `--hotspot`.

### The built-in settings and script

`object --sam template` writes a sideshow-like attraction modelled on the keys of the original `Puzzle.sam`: `Info.Id` (a placeholder; `objects.json` replaces it), `Info.Name`, a 3 × 3 `Info.Shape` with the
entrance `2` in the middle of the last text row, `Info.Hoarding`, `Info.RideTypeStringIndex` 22 (as the puzzle), `UsageInfo.InitPricePerUse` 15, `InitCostOfGoods` 0, `InitChanceOfLoosing` 0,
the stand and appear positions, `ExcitementLevel` 35, `MinCapacity` 1, `MaxCapacity` 6, `RideHandlesSprite` 1 (riders are drawn as heads on the head records), `Upgrades[0].CostOfUpgrade` 1500,
`CostOfResearch` 0 (an object without research cost starts researched, [ECONOMY.md](ECONOMY.md)), `InitCapacity` 6 and `Research.Group` 0. The values are the kit's choices; the category defaults (`SideShow.sam`, ...) of the game
data are layered under them by the catalog.

The built-in script is a **placeholder**: guests enter, wait and leave, nothing is animated and there are no effects. It is the shape of the original rides that hang heads on a model (for example
Bugs TV): wait while the ride is closed; a fill loop with `SETTIMER`/`GETTIMER 10000` that takes each called guest (`TEST VAR_LETMEON`, `HUSH`, `ADDHEAD`, `COPY VAR_LETMEON 0`, `ADD VAR_ONRIDE 1`) until the capacity
is used or ten seconds pass without a new guest; `WAIT 8000` with `VAR_RUNNING` set; then an unload loop (`HOP VAR_LETMEOFF`, `DELHEAD`, wait until the host has taken the guest, `ADD VAR_ONRIDE -1`, `WAIT 300`). It declares the twelve standard variables
first and `VAR_SPACELEFT` after them. **`.stack 20`**: the header's stack size is also the size of the `HUSH`/`HOP` heap that holds the riders' ids (docs/RSE-VM.md); with `.stack 0` the `HUSH` does nothing, `HOP` pops 0, and the
guest is never released. Real swimming comes later, in an extension.

## End to end

`ContentKitTests` run the whole flow without a GPU and without running the game:

1. A pool comes from the Blender sample script when Blender is installed (`OPENTPW_BLENDER`, else `/Applications/Blender.app`), and always from a hand-built glb of the same shape.
2. `opentpw-kit object` and `opentpw-kit mod` (through `ContentKitCli.Run`) build a mod folder.
3. The game's own mod pipeline loads it: `ModDiscovery`, `ModSet.Resolve`, `ModContentRoots.Use`, `ObjectCatalog` (the object is listed with its `objects.json` id, name, the template's 3 × 3 shape and its script),
   `ObjectAssets.LoadModel`, `ConvertMesh` (nothing unrenderable), `ResolveTexture` for every material (the `.tga` in `textures/`, decoding to the exported image's pixels exactly), the self-lit and wrap flags, the six head
   records found by `RideHeadSpace`, and the assembled script running in `RideVM` without a fault.
4. A guest goes through it without game data: the real `RideVisitorBridge`, `GuestSimulation` and head space with the built model and script (the rig of the queue tests). The guest queues, boards, a head is hung on a
   head record, the guest swims for about eight seconds, the head comes down and the ride is counted.
5. With `OPENTPW_GAME_PATH`, the **M3 gate** (`M3Gate.Run`, a real Jungle level with entrance, paths, queue, shop, toilet and staff, three simulated minutes) takes the built pool as its attraction: it is bought for 1500,
   built on its 3 × 3 footprint, registered with the guests and queued, its script cycles (6 cycles) and guests board it (42 boardings in the run). The gate only places rides, so this test files the object under `rides`.

What is missing: a placement as an `OriginalObject` entity (it builds renderer entities and needs a GPU context, so the real draw of the converted model, its texture orientation and its mirror state in the game are not
checked); and a sideshow-category object has no headless park test (the gate places rides only, and a sideshow guest needs the live sideshow terms the runtime derives).

## What is unverified

- Whether tp.exe itself accepts an authored MD2 (the planned layout, the default words of [MD2-MODELS.md](MD2-MODELS.md#writing-and-authoring)), an authored ESP/FPC set or an assembled RSE: the original was not run.
- The TGA variants tp.exe's loader takes beyond the ones the shipped files use (the writer produces exactly those), and non-power-of-two textures.
- The handedness and V conventions are derived from OpenTPW's renderer and the original data, not compared with a capture of the original game.
- The animation slot choice of `sprites`, the sample settings' values (price, capacity, research, `Info.RideTypeStringIndex`), and the placeholder script's timing are the kit's own.
- glTF features beyond the reader's list (animation, skins) are refused, so there are no animated models yet.

## Tests

- `GltfSceneTests`: hand-built files for the reader's fields, transforms and each refusal; with Blender installed, `ReadsTheBlenderSamplePool` also runs the sample script.
- `GltfModelConverterTests`: the conventions (position mirror, V flip, winding and face normals, the engine-frame reading of a plain, a rotated and a nested quad), hierarchy, materials, extras, heads, textures and every refusal.
- `TgaWriterTests`: the variant, stb read-back and the 155-file corpus identity. `Md2ConventionCorpusTests`: the sign-face evidence above.
- `ContentKitTests`: the three commands, their errors, and the end-to-end flow above.
