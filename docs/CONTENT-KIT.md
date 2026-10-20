# Content kit: authoring new objects

The content kit turns original, newly made art into files the game reads: MD2 models, textures, ride scripts and
sprite banks, packed as an object a data-only mod adds through `objects.json` ([MODS.md](MODS.md#objects)). It exists
for community content, and it builds the shipped swimming pools ([POOLS.md](POOLS.md)), the umbrellas' content ([UMBRELLAS.md](UMBRELLAS.md)) and the winter shops and warm items ([WINTER.md](WINTER.md)). Nothing in it reads or copies art from the original game;
the original corpus is only used to prove that each writer produces files the readers (and the formats) accept.

Status (October 17, 2026): phase 0 is complete in the code. In place:

- the Blender side (`tools/blender/`): authoring conventions, a validating glTF exporter, a generated sample object and
  the swimmer sprite generator ([below](#swimmer-sprites));
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
- **Lights.** A point light on a node (`KHR_lights_punctual`, in Blender a point light object) becomes a light dummy, the place a ride script's `ENABLELIGHT`, `DISABLELIGHT`, `SETLIGHT` and `COLOURLIGHT`
  switch and colour, and the renderer's point light ([LIGHTS.md](LIGHTS.md)). The custom properties `opentpw_light` (the light number, an integer from 0) and `opentpw_light_radius` (the radius in model units) go on
  the light object. Only a point light with `opentpw_light` becomes a light dummy (Blender's default scene has a light that is not the object's; the exporter warns about a point light without the property). The exporter writes lights (`export_lights`), the other light kinds are skipped.
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
- **Lights.** A `KHR_lights_punctual` point light becomes a light dummy ([Lights](#lights)).

### Lights

A `KHR_lights_punctual` light of type `point` on a node with `opentpw_light` → `AddLight( number, colour, radius )` on that node's dummy (`[EXT:object-lights]`, [LIGHTS.md](LIGHTS.md#the-model-format)): the light's `color`
  is the base colour (clamped to 0..1 with a warning), its `range` the radius in model units (a scene unit is one model unit; rounded, 1 to 65535; no `range` means the engine's default of 100), the node extra `opentpw_light` = n
  the light number (**required**: a point light without it is skipped with a warning, because exporters write every light of a scene) and `opentpw_light_radius` = r a radius that beats the range. The glTF `intensity` is **not carried** (candela or lux, its unit differs
  between exporters; one warning per file when it is not 1): scale the colour, and a script's `SETLIGHT` dims the base colour at run time. Spot and directional lights are skipped with a warning. `KHR_lights_punctual` may be listed
  as a required extension (any other required extension is still refused). A node that is a rider head cannot also be a light, and numbers must be unique.
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

Writes `opentpw-mod.json` and `objects.json` ([MODS.md](MODS.md#manifest)) and, for an object spec with `src=`, a copy of that built folder at `archive` in the mod. `--overrides data/` sets the manifest's
`content.overrides` (the folder of replacement game files, which `sprites --mod` fills); a mod may have objects, overrides or both, and no `objects.json` is written without objects. The documents are
checked with the game's rules before the folder exists: id and version shapes, the Info.Id range (5000 to 65535, at most 4096 ids), ids inside it and unique, themes, categories, archive paths that stay in
the folder, names. The game's own strict loaders remain the authority; the tests load the output with them.

**`sprites`**: complete sprite sets, packed from a folder of frames and a `set.json`.

```sh
opentpw-kit sprites --frames art/SPR_BE --mod mymod --folder Generic/Kids          # into the mod's overrides tree
opentpw-kit sprites --frames art/SPR_BE --frames art/SPR_CH --out /tmp/sprites     # or flat into a folder
```

A set folder holds `slot<NN>_d<direction>_f<frame>.png` (RGBA; NN is the animation slot 0 to 15, directions and frames count from 0) and `set.json`:

```json
{ "name": "SPR_BE", "detailFlag": 1, "kidFlag": 0,
  "slots": { "0": { "directions": 5, "framesPerDirection": 1 }, "1": { "directions": 5, "framesPerDirection": 8 } },
  "defaultOrigin": { "originX": -24, "originY": -52 },
  "frames": { "slot01_d0_f0": { "originX": -25, "originY": -53 } },
  "performances": [ { "program": 0, "slot": 4, "intro": 0, "hold": 0 } ] }
```

`pixelsPerUnit` (default 128, 1 to 65535) is written to both pixels-per-unit words of every frame; a set drawn at twice the pixel size has 256 (see "Resolution" under Guest sprite sets). `name` is the set's stem (1 to 7 letters, digits or underscores, such as `SPR_BE`; `--name` may supply it when the file does not). `detailFlag` is ESP byte 0x10C: 1 writes `.FPC` and `.TPC` with the same content, as every shipped
full-detail set ships both, 0 only the `.TPC`, as the head sets do (default 1); `kidFlag` is byte 0x10D (default 0). `slots` lists every slot the set has, with its directions (the shipped kid sets have 5, the head sets 7;
0 means a slot without directions, as in the balloons and thoughts, whose files are `d0` only) and frames per direction; slots keep their numbers and frames are stored slot by slot, direction-major, as the
shipped sets are. `originX`/`originY` are the offset of the frame's top-left corner from the hotspot (`SpriteFrame.OriginX`/`OriginY`; negative when the hotspot lies inside the frame), per frame under `frames` (the key is the
file name without `.png`) with `defaultOrigin` for the rest. `performances` are the entertainer entries (`SpriteSetBuilder.SetPerformance`). Head sets (`Kidsheads`, 7 elevation rows) use the same format with one slot. The whole set shares one
255-entry palette (exact up to 255 colours, median cut beyond), as `SpriteSetBuilder` does ([GUESTS.md](GUESTS.md#guest-graphics-sprites-not-models)). Every missing or surplus frame, an undeclared slot, a frame without an origin and every unknown
field is an error that names the file; an unrelated `.png` is ignored with a warning.

With `--mod <dir> --folder <F>` the files go to `<dir>/<overrides>/esprites/<F>/` (`--overrides`, default `data`): with the mod's `content.overrides` (`"data/"`, set by `mod --overrides data/`) the game then reads them in place of the game's own
at the data path `/esprites/<F>/<NAME>.ESP` and so on ([MODS.md](MODS.md#overrides)). `GuestSpriteAtlas` reads `/esprites/Generic/Kids`, `Generic/Kidsheads`, `<Theme>/Costumes`, `<Theme>/Costumeheads`, `Generic/Balloons` and
`Generic/Thoughts`; the folders of `esprites.wad` are `Generic/{Kids, Kidsheads, Balloons, Thoughts, Mechanics, Handymen, Guards, Researchers, Litter, Particles, SpecialFX}`, `{Jungle, Hallow, Space, Fantasy}/{Costumes, Entertainers}` and
`{Jungle, Space}/Costumeheads`; `--folder` takes any of them in any case and writes the casing of the archive (another folder is allowed with a warning). The command warns when the mod's manifest does not name the overrides folder. A set that
replaces a shipped one needs the same file names; sets that the game does not already list in the folder (new names) are not found by the loaders, which list the game's own directory.

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

## An example with a light: the lamp post

`tools/lights/build-example-mod.sh` builds the example mod `content/mods/opentpw.example.lamp-lights` (python3 and the .NET SDK, no Blender): `make_example_lamp.py` writes a glTF lamp with a point light node, `opentpw-kit object` converts it with
`tools/lights/lamp.sam` (which tunes the light with the `Lights[n]` keys) and `tools/lights/lamp.rse` (a script that uses the four light opcodes), and the mod's two documents are written by the script (the kit's `mod` command writes community
mods only, and this one is official). See [LIGHTS.md](LIGHTS.md#the-example-mod).

## Content shipped with the game: pools

The official `opentpw.pools` extension ships its changing rooms and swim sprites as committed generated files in `content/pools` (our own art, nothing from the original game). `tools/pools/build-content.sh` regenerates them (Blender 5.x, the .NET SDK, python3; about 30 minutes, nearly all of it the sprite renders; the kid sets keep only slots 0, 1 and 14, the ones the land renderers draw; env `KIT_ARTIFACTS`, `WORK`, `FORCE_SPRITES`): the cabin model comes from `tools/blender/opentpw_changing_room.py` through `GltfModelConverter` and `opentpw-kit object` with `tools/pools/cabin.sam` and `cabin.rse`, the guests from `opentpw_guest_sprites.py --scale 2 --part pool` (the swimwear sets, `content/pools/esprites/Swim`) and `--part land,heads` (the clothes sets and heads that replace the game's while the extension is on, `esprites/Generic/Kids` and `Kidsheads`), packed by `tools/pools/pack_sprite_sets.py` through `opentpw-kit sprites`. All of it is drawn at 2x (8.25 MB of sprites). The pool extras (ladder, diving board, shower, lounger, lamp, underwater light) are built the same way from `tools/blender/opentpw_pool_extras.py` with `tools/pools/extra-*.sam` and `extra.rse` (step 2b). See [POOLS.md](POOLS.md#content-extpools-content) and [Extras](POOLS.md#extras-phase-p3-extpools-extras).

## What is unverified

- Whether tp.exe itself accepts an authored MD2 (the planned layout, the default words of [MD2-MODELS.md](MD2-MODELS.md#writing-and-authoring)), an authored ESP/FPC set or an assembled RSE: the original was not run.
- The TGA variants tp.exe's loader takes beyond the ones the shipped files use (the writer produces exactly those), and non-power-of-two textures.
- The handedness and V conventions are derived from OpenTPW's renderer and the original data, not compared with a capture of the original game.
- The sample settings' values (price, capacity, research, `Info.RideTypeStringIndex`), and the placeholder script's timing are the kit's own.
- glTF features beyond the reader's list (animation, skins) are refused, so there are no animated models yet.

## Tests

- `GltfSceneTests`: hand-built files for the reader's fields, transforms and each refusal; with Blender installed, `ReadsTheBlenderSamplePool` also runs the sample script.
- `GltfModelConverterTests`: the conventions (position mirror, V flip, winding and face normals, the engine-frame reading of a plain, a rotated and a nested quad), hierarchy, materials, extras, heads, textures and every refusal.
- `TgaWriterTests`: the variant, stb read-back and the 155-file corpus identity. `Md2ConventionCorpusTests`: the sign-face evidence above.
- `ObjectLightTests`: light dummies through the builder and the writer, the converter's lights (number, range, extras, warnings, refusals), the light bank, the opcodes and the example mod end to end.
- `ContentKitTests`: the three commands, their errors, the sprite sets (a kid-set folder through the readers and `GuestSpriteAtlas`, head and balloon sets, an override into the real `esprites.wad`) and the end-to-end flow above.

## Swimmer sprites

Guests are pre-rendered 2D sprites, not models ([GUESTS.md](GUESTS.md#guest-graphics-sprites-not-models)), so a swimming
pool needs swimmer sprites that look like the original guests. `tools/blender/opentpw_kid.py` is a parametric low-poly
person; `tools/blender/opentpw_swim_sprites.py` poses it, renders the frames in sprite size and writes the PNGs. Both are
original art made from code (nothing is read from or derived from the game's files; the originals were only looked at to
match proportions and picked colours). Run Blender 5.x headless:

```sh
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python tools/blender/opentpw_swim_sprites.py -- --out /tmp/swimart --edition TPW
$B -b --factory-startup --python tools/blender/opentpw_swim_sprites.py -- --out /tmp/swimart --edition TPI --binary-alpha
```

Rendered images are not committed; regenerate them with the commands above (about three minutes per edition).

### Rig and presets

The rig is a stack of pivots (`root` > `lay` (stand to lie) > `roll` > `upper` (stoop) and legs; head `neck` (yaw) > `nod`
(pitch); shoulders and hips each an abduction pivot and a swing pivot; elbows, knees, ankles). The kid looks along +Y, the
rig origin is the middle of the torso, and `apply_pose(kid, pose)` sets every pivot from a dict of angles. Limbs are single
smooth capsules (a UV sphere whose halves are pulled apart, shallow end caps and analytic normals) with a ball at each
joint, so there are no stacked cones and no rings. Proportions are in `BODIES` (`kid`: head about 40 % of the height,
short limbs, big feet; `mum`, `dad`, `granny`, `grandpa`: taller, longer limbs, smaller head, the grandpa stooped). One
preset per original sprite set picks skin, hair, outfit colour, eyes and a hair style (spiky, pigtails with bobbles, bob,
curly, swim cap, short, bun, perm, balding):

| Edition | Sets |
| --- | --- |
| TPW kids | BE, BI, CH, FR, KI, SA, SU, TA |
| TPI kids | BD, GB, GK, KB |
| TPI adults | FA (dad), MO (mum), GM (granny), GR (grandpa) |

Swimwear is the set's main outfit colour: trunks for boys and men (a girl's or woman's swimsuit covers the torso). Hats
and the dad's camera are left out; GK and KB wear swim caps in their cap colours (KB's is taller, with brown hair showing
at the fringe and the nape).

Surface look (late-90s pre-render, not plastic): every material is purely diffuse (specular 0.04, roughness 0.9; only the
eyes are glossy) over a procedural painted albedo: hair has strand bands times clumpy noise, the swimwear distorted fold
bands, skin dabs of slightly different value, all with a slow warm/cool hue wander; hands, feet, ears and nose are rosier,
plus cheek blush, darker eyebrows and lips. All textures of a figure share one texture space (the `roll` pivot), so
neighbouring body parts agree where they meet (separate per-object spaces showed as rings on the limbs). Ambient
occlusion darkens creases, and the grade adds warm shadows. Characters: FA has a round belly, a moustache and stubble; MO
a bun and red lipstick; GM a grey curly perm and round glasses; GR white sideburns, a bald top, a white moustache and a
slight stoop (standing pose only; swimmers lie flat).

### Camera and lighting (calibrated against the original standing frames)

Orthographic camera, render 6 times larger and box-filter down (premultiplied), a mild unsharp mask (0.2) to keep the
pixel crispness of the originals, a grade (contrast 1.2 about 0.45, saturation 1.0, warm shadow tint), then darken the
silhouette's outermost pixels by a factor 0.7 (the originals have dark edges). Three suns are parented to the camera, all
without shadow maps (they banded the thin limbs): a warm key from above and the upper left (energy 6.0), a cool fill from the
right (0.5) and a rim from behind and above (2.0, it only catches edges), plus a world ambient of 0.34. Judged on the
palette-quantised frames (below), the per-region pixel statistics of the standing frames against the originals (direction
4) are: luminance spread 45-80 vs 40-70, 90th percentile 130-250 vs 130-220, local contrast (mean absolute difference of
neighbouring pixels inside the figure) 19-33 horizontally vs 19-37 in the originals, saturation 0.2-0.8 vs 0.4-0.6. Our
vertical local contrast is about 20 % higher; mean colours differ because ours wear swimwear and the originals clothes (these
statistics are from the earlier 27 degree camera).

Directions 0 to 4 put the camera at azimuth
`-45 * direction` (0 looks at the kid's back, 2 sees it facing screen right, 4 facing the camera); the game mirrors the
rest.

| Constant | TPW | TPI |
| --- | --- | --- |
| Camera elevation | 45 degrees, measured (it was 27, set by eye against the FPC) | 25 degrees, set by eye against the FPC (not measured) |
| Pixels per unit | 11.1, measured (it was 11.7) | 14.4 |
| Calibration reference cell / standing hotspot | 48 x 56, feet at (24, 53) | 64 x 80, feet at (32, 70) |
| Swim frame canvas / hotspot | 80 x 60, (40, 34) | 100 x 80, (50, 45) |

The guest sets ship their art twice: the `.TPC` bank the game draws and, for the sets with ESP byte 0x10C, an `.FPC` bank of the
same art seen from a lower camera, which the game never opens ([GUESTS.md](GUESTS.md)). The FPC frames are about as wide and taller
(a kid stands 40 to 42 px tall in the FPC, 32 to 37 in the TPC). `tools/blender/opentpw_kid_elevation.py` measures the camera: it renders
the standing kid of every set over a sweep of elevations and scales and scores each by the mean intersection over union of our five
silhouettes with the original's, placed by their hotspots (the originals come from a local export, never committed). Against the TPC the
score peaks at 44 to 48 degrees and pixels per unit 11.0 to 11.2 (IoU 0.77); against the FPC at 15 degrees or lower and 11.7, so the two banks
differ in camera, not in scale. The TPW constants were 27 degrees and 11.7 (with a "TPC scale" of 11.7 / 1.25 = 9.36 for the comparison),
calibrated by eye against the FPC on the reading that the game draws it; our guests looked filmed from the side next to the originals.

Standing frames at 45 degrees and 11.1 pixels per unit (`opentpw_kid_elevation.py` at its best), height x width per direction (0 to 4),
ours / the original TPC:

| TPW | d0 | d1 | d2 | d3 | d4 |
| --- | --- | --- | --- | --- | --- |
| BE | 32x28 / 37x26 | 34x21 / 36x21 | 35x17 / 34x21 | 35x21 / 34x21 | 34x28 / 32x26 |
| BI | 32x28 / 37x26 | 34x21 / 36x22 | 35x17 / 34x21 | 36x21 / 34x21 | 35x28 / 32x26 |
| CH | 31x28 / 33x27 | 36x21 / 36x21 | 39x18 / 40x16 | 37x21 / 39x21 | 33x28 / 34x25 |
| FR | 32x28 / 36x26 | 34x21 / 36x21 | 35x17 / 34x18 | 35x21 / 33x21 | 34x28 / 31x26 |
| KI | 31x28 / 33x27 | 36x21 / 37x22 | 39x18 / 40x16 | 37x21 / 39x21 | 33x28 / 34x27 |
| SA | 31x28 / 33x25 | 33x21 / 34x19 | 33x18 / 34x18 | 34x21 / 33x20 | 33x28 / 30x25 |
| SU | 31x28 / 33x27 | 36x21 / 37x22 | 39x17 / 40x16 | 37x21 / 39x21 | 33x28 / 34x27 |
| TA | 31x28 / 32x25 | 33x21 / 33x20 | 35x18 / 34x17 | 35x21 / 34x20 | 34x28 / 32x26 |

Heights are within 3 pixels except the back view (direction 0, 1 to 5 short); ours are 1 to 3 pixels wider from the front and back, and
from the side up to 4 narrower (BE, BI) or 2 wider (CH, KI). The heads keep their own scale (`HEAD_PPU`, 11.7 for TPW): rendered at the
bodies' 11.1 they come out 6 % smaller than the original head sets, which they matched at 11.7. `tools/blender/opentpw_kid_calibrate.py` still renders the standing kid in the original's cell for
every set and, given a reference sheet of the original standing frames (viewed locally only), writes a side-by-side comparison (original
left, ours right). The TPI standing heights below (direction 4, in pixels, ours and the original's) are from the earlier calibration, at the old camera:

| TPI | BD | GB | GK | KB | FA | MO | GM | GR |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ours | 39 | 39 | 37 | 40 | 50 | 48 | 41 | 37 |
| original | 40 | 41 | 38 | 43 | 50 | 48 | 42 | 38 |

All within 4 pixels for TPI (the TPI kids come out a little short), most within 2. The proportions follow the originals more closely since October 17, 2026:
- smaller eyes set higher in the face, with the forehead visible under the hair;
- a smaller kid head (scale 1.0, was 1.14);
- a longer torso and longer legs for kids and adults;
- shorter spikes for the spiky-haired boys.

### Slots, directions and frame names

| Slot | Animation | Frames per direction |
| --- | --- | --- |
| A | front crawl (face down, alternating arm circles, flutter kick, head turns to breathe, body roll) | 8 |
| B | breaststroke (glide, out-sweep, pull, hands to chest, frog kick) | 8 |
| C | treading water / idle (upright, sculling arms, bicycling legs under water) | 4 |

Five directions as in the kid sprites (0 swims away from the camera, 2 faces screen right, 4 faces the camera, 1 and 3
are the diagonals; 5 to 7 are mirrored by the game). The sprite index of a frame is `direction * frames + frame`.
Files: `<out>/<edition>/<SET>/<slot>_d<direction>_f<frame>.png`, RGBA.

### Water line and hotspot

The water surface is the plane z = 0. Every material tints what is below it (multiply by a blue, mix 34 % water blue) and
renders it at 74 % opacity, so the part of the sprite under the water takes on the pool's colour (a light blue tint,
so submerged legs still read as a shape). Swimmers have wet hair (`swim=True`): darker (x0.72), caps and curls closer to
the head, spikes cut to stubs, pigtails hanging, buns smaller; the standing calibration keeps the dry hair. The swimmer's body is raised so the back and shoulders are above the surface, and
the limbs are lengthened by a cartoon factor (arms 1.3, legs 1.12, `swim=True` in `build_kid`) with exaggerated strokes so
arms leaving the water and kicks read at 1x. A light foam ring (an inner and an outer, fainter ring) lies on the surface
around the torso, and droplets (seven per hand or foot) are placed on the frames where it crosses the surface or moves
fast near it. In the crawl the head turns to the side of the recovering arm and the body rolls that shoulder up (roll 44 degrees,
recovery arm arc up to 76 degrees out). `--verify` checks this on every set (roll and head turn on all 8 crawl frames) and
renders arm/shoulder masks from directions 0 and 1 to count visible arm pixels: at 1x the arms are visible (6 px or more
above the water) in 8 of 8 frames for every set in both directions, except SA (6 of 8; her hair hides part of the near arm).

The frames of a set are quantised to one shared 255-colour palette (weighted median cut over RGBA, the same family as
`SpriteSetBuilder`; the palette is not byte-identical to its result) before they are written, and the sheets and GIFs show
the quantised frames; the unquantised frames are in `<SET>/truecolor/`. An ordered dither (`--dither 0.05`) adds a faint grain
in the flat regions, a little closer to the originals' grain; the difference is small, so it is off by default. `--binary-alpha` makes every pixel fully opaque or fully transparent
(alpha threshold 0.4) for palette sprite sets, which cannot hold partial transparency; the water tint stays, and the foam
ring becomes opaque.

The hotspot (sprite origin) of every swim frame is the `swim_origin` pixel: the centre of the torso at the water line,
so placing the sprite at a guest's position puts the body on the surface there. The frames are not trimmed. Packing them
into ESP/TPC banks goes through the sprite-set writer, which is separate from this generator.

`tools/blender/opentpw_swim_compare.py` builds a before/after sheet of two renders (for reviewing a change to the
generator): per set a 1x and a 4x band with old | new for the crawl from two directions and treading water.

## Guest sprite sets

For the swimming-pool mod every guest is drawn in our own style, not only the swimmers. The mod's `overrides` folder replaces any
game file by its data-relative path, WAD members included ([MODS.md](MODS.md#overrides)), and `GuestSpriteAtlas` reads the guest
sets as `/esprites/Generic/<Folder>/<NAME>.ESP` plus its `.TPC`, the bank the game draws (an `.FPC` beside it is never opened), so a
replacement set ships at the original paths:

```
data/esprites/Generic/Kids/SPR_BE.ESP        .TPC        (TPW kids; TPI: SPR_BD, SPR_GB, SPR_GK, SPR_KB)
data/esprites/Generic/Parents/SPR_FA.ESP     .TPC        (TPI: SPR_FA, SPR_MO)
data/esprites/Generic/Grandparents/SPR_GM.ESP .TPC       (TPI: SPR_GM, SPR_GR)
data/esprites/Generic/Kidsheads/SPR_BE.ESP   .TPC        (also Parentsheads, Grandparentsheads)
```

The path is the WAD member path below `esprites` with the archive's name dropped, compared without case (the TPI members
`Kidsheads/Spr_bd` and `Spr_gb` have lower-case names). Directory listings stay the game's, which is how `LoadSets` finds the sets: an
override can replace the members of an existing set but not add a new set, so a replacement keeps the original set names, and
an ESP replaces the whole animation table (that is where our extra slots live).

### Generating the sets

```sh
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python tools/blender/opentpw_guest_sprites.py -- --out /tmp/swimart/guests --edition TPW
$B -b --factory-startup --python tools/blender/opentpw_guest_sprites.py -- --out /tmp/swimart/guests --edition TPI
# options: --sets BE,CH   --part land,swim,heads   --slots walk,run   --outfit clothes|swim   --no-quantise   --dither 0.05
```

Per set a folder `<out>/<edition>/<Folder>/<SET>/` with `slot<NN>_d<d>_f<f>.png` (RGBA, trimmed to the frame's content, the whole
set quantised to one 255-colour palette, median cut as `SpriteSetBuilder` does) and a `set.json` for the packer (the content-kit
`sprites` command packs it with `SpriteSetBuilder`; nothing here writes ESP or bank files):

```json
{ "name": "SPR_BE", "set": "BE", "edition": "TPW", "folder": "Generic/Kids", "fullDetail": true, "kidFlag": 0, "colours": 255,
  "slots": [ { "slot": 1, "name": "walk", "source": "land", "directions": 5, "framesPerDirection": 8,
               "frames": [ [ { "file": "slot01_d0_f0.png", "originX": -13, "originY": -39, "width": 22, "height": 40 }, "..." ], "..." ] } ] }
```

`name` is the set's member name; `fullDetail` is the ESP byte 0x10C (true: the set ships an `.FPC` too, written with the same content as the
`.TPC`, as the shipped full-detail sets do, though the game draws the `.TPC` either way; false for the heads); `kidFlag` is ESP byte 0x10D (1 for the girls); `frames` are
listed direction-major, as the bank stores them. Heads have `source` `heads`, one slot 0 of 7 directions (elevation rows) by 8 frames
(azimuths).

### Resolution

`--scale N` renders every frame at N times the pixel size: canvas, hotspot offsets and pixels per Blender unit are N times larger (`setup_scene(..., scale=N)`), the supersampling drops from 6 to 4 at 2x (the frame is rendered 8 times its pixel size), and `set.json` gets `"pixelsPerUnit": 128 * N`. The packer (`opentpw-kit sprites`, `pixelsPerUnit` of its `set.json`) writes that into every frame's two pixels-per-unit words, and the game scales a quad by 128 / the word ([GUESTS.md](GUESTS.md#pixels-per-unit-and-resolution)), so a 2x frame is drawn at the world size of the 1x frame it replaces with twice the detail. Hotspot offsets are in the set's own pixels (a 1x offset of -39 is -78 at 2x), and `LAND_HOTSPOT_DROP` counts 1x pixels (doubled at 2x;
0 since October 18, 2026).

Quantisation at 2x (one 255-colour palette per set, as at 1x): measured on all 175 frames of TA, the mean largest-channel error of the quantised frames against the unquantised ones is 2.9 of 255 and 2.6 % of the pixels are off by more than 16, with no visible banding beyond what the unquantised render already has. A light ordered dither (`--dither 0.03` or 0.06) raises the error to 4.0 or 5.7 and shows only as grain, so none is used. Sizes: a TPW kid set in clothes is 0.12 to 0.14 MB as shipped (55 frames, slots 0, 1 and 14; with every slot, 175 frames, it was 0.46 MB at the earlier camera), its heads 0.12 to 0.14 MB (56), a swimwear set 0.75 to 0.78 MB (295), all `.TPC` only; the 24 shipped sets are 8.25 MB. `tools/blender/opentpw_guest_hires_compare.py --compare` draws 1x against 2x (`reports/guests-hires-compare.png`) and `--sheet` a contact sheet of any generated set.

### Swimwear slots

`--part pool` renders `opentpw_pool_poses.POOL_SLOTS` into `<out>/<edition>/Pool/<SET>/`: the swimwear table of the pools mod (the `PoolSwimSlots` constants), five directions each. Dry slots use swimwear and dry hair; the water slots (8 to 13) the wet kid, with the water's tint and translucency below the water line, foam rings and droplets.

| Slot | Meaning | Frames | Hotspot |
| --- | --- | --- | --- |
| 0 | stand on the deck | 1 | feet on the floor |
| 1 | walk on the deck | 8 | feet |
| 2 | run (unused, empty) | | |
| 3 | climb a ladder, facing it, hands reaching for rungs, knees coming up | 4 | torso centre |
| 4 | dive or jump: crouch, leap, arc, head-first entry (upside down) | 4 | torso centre |
| 5 | lying on the back, head away from the heading (lounger) | 2 | torso centre |
| 6 | standing under a shower, hands in the hair, falling drops | 4 | feet |
| 7 | sliding on the back, feet first, arms up then crossed | 2 | torso centre |
| 8, 9, 10 | crawl, breaststroke, treading water (as before) | 8, 8, 4 | torso at the water line |
| 11 | splash-down: the body sinks from the shins to the shoulders, a crown of drops, growing rings | 4 | torso at the water line |
| 12 | sitting in the water (spa), knees up, an arm on the rim, then waving | 2 | torso at the water line |
| 13 | floating in an inflatable ring (own colour per kid), paddling and bobbing | 4 | torso at the water line |
| 14 | swinging on a rope, hands overhead: legs forward, down, trailing, down | 4 | torso centre |
| 15 | spare, empty | | |

Slots whose hotspot is the torso centre may point up or down the frame (the dive ends upside down), so the generator renders them on a larger canvas (96 x 96 px at 1x for TPW). The engine places the torso centre on the path of the ladder, dive, slide or rope; the lying hotspot is the centre of the body on the lounger.

### Hotspot rule

`originX`/`originY` follow the reader (`SpriteFrame.OriginX/OriginY`): the offset of the frame's top-left pixel from the hotspot, the
quad spanning `originX` to `originX + width` (and likewise vertically) in 1/128 units. The hotspot of a standing, walking, running
or gesturing guest is on the floor between the feet; ours is the world origin pixel of the render (`LAND_HOTSPOT_DROP` 0). From the
TPC's camera the originals' soles end 3 to 5 pixels below the hotspot when standing, and ours 2 to 5 (a mean of 3.9 against 4.0 to 4.2).
Until October 18, 2026 the hotspot was one pixel lower, to match the FPC, whose soles end 1 to 2 pixels below it. A swimmer's hotspot
is the centre of the torso at the water line (slots 8 to 10), a head's the centre of the skull. All of a set's frames of one slot share
one hotspot, so the guest does not jump between frames. Frame content of the shipped TPW kid sets against the originals' TPC frames
(ours, the 2x frames halved / original, in pixels; `top`/`bottom` are `originY` and `originY + height`):

| Set | Slot | Width | Height | Top | Bottom |
| --- | --- | --- | --- | --- | --- |
| BE | 0 | 17 to 24 / 21 to 26 | 32 to 36 / 32 to 37 | -31 to -30 / -34 to -29 | 2 to 5 / 3 to 5 |
| BE | 1 | 17 to 27 / 18 to 32 | 31 to 36 / 29 to 40 | -32 to -26 / -34 to -27 | 2 to 9 / 2 to 7 |
| BE | 14 | 17 to 30 / 19 to 25 | 32 to 36 / 33 to 37 | -31 to -30 / -34 to -29 | 2 to 6 / 3 to 5 |
| TA | 0 | 19 to 24 / 17 to 26 | 31 to 35 / 32 to 34 | -30 to -29 / -29 to -29 | 2 to 5 / 3 to 5 |
| TA | 1 | 18 to 27 / 16 to 31 | 31 to 36 / 30 to 35 | -32 to -26 / -29 to -27 | 2 to 9 / 2 to 8 |
| TA | 14 | 18 to 30 / 16 to 34 | 31 to 36 / 27 to 35 | -31 to -29 / -31 to -24 | 2 to 6 / 3 to 5 |

The TPI sets as measured before (at 25 degrees, the hotspot one pixel lower, against the originals' FPC frames; TPI's Game.exe draws the
TPC too, so they are not calibrated against the bank it draws yet):

| Set | Slot | Width | Height | Top | Bottom |
| --- | --- | --- | --- | --- | --- |
| BD | 0 | 27 to 30 / 21 to 32 | 46 to 49 / 47 to 50 | -45 to -45 / -49 to -49 | 1 to 4 / -2 to 1 |
| BD | 1 | 27 to 34 / 19 to 38 | 44 to 49 / 45 to 50 | -46 to -42 / -48 to -46 | 0 to 7 / -2 to 3 |
| BD | 12 | 26 to 30 / 21 to 38 | 42 to 50 / 45 to 50 | -50 to -42 / -49 to -47 | -3 to 4 / -3 to 1 |
| FA | 0 | 32 to 38 / 27 to 35 | 61 to 64 / 60 to 64 | -59 to -59 / -63 to -61 | 2 to 5 / -1 to 1 |
| FA | 1 | 32 to 42 / 28 to 46 | 57 to 65 / 56 to 63 | -60 to -54 / -63 to -58 | -1 to 10 / -2 to 1 |
| FA | 12 | 32 to 38 / 29 to 49 | 59 to 64 / 58 to 65 | -64 to -57 / -63 to -59 | -2 to 5 / -1 to 2 |
| GM | 0 | 28 to 32 / 30 to 36 | 52 to 56 / 46 to 55 | -54 to -49 / -54 to -51 | 1 to 4 / -5 to 1 |
| GM | 1 | 29 to 36 / 27 to 36 | 47 to 58 / 50 to 56 | -54 to -43 / -55 to -52 | -1 to 8 / -3 to 1 |
| GM | 12 | 26 to 32 / 26 to 42 | 52 to 56 / 47 to 61 | -55 to -49 / -59 to -49 | -3 to 4 / -2 to 2 |

### Slots

The original slots keep their numbers, frame counts and directions (5 directions: 0 away from the camera, 2 facing screen right, 4
facing the camera; the game mirrors 5 to 7). The meaning of each slot was identified by looking at the original frames:

| Slot | Meaning | Frames per direction | Sets |
| --- | --- | --- | --- |
| 0 | stand | 1 | all |
| 1 | walk | 8 | all |
| 2 | run | 8 | TPW kids, TPI kids |
| 4 | gesture A: turn and point, spread the arms, bow | 4 | all |
| 6 | gesture B: a hand to the chin (thinking), tilt the head, nod | 4 | all |
| 11 | stumble, hunching over a hand on the stomach (sick) | 4 | TPW kids |
| 12 | cheer: arms up, a hop, down | 4 | all |
| 14 | wait with the hands on the hips | 2 | TPW kids |
| 15 | TPI idle: one arm points out and sweeps | 4 | TPI kids and adults |
| 8 | **new** swim: front crawl | 8 | all |
| 9 | **new** swim: breaststroke | 8 | all |
| 10 | **new** swim: treading water | 4 | all |

The TPI adults have no run slot, as in the originals. Slots 8 to 10 are free in every original table (TPW kids use 0, 1, 2, 4, 6, 11, 12, 14;
TPI kids 0, 1, 2, 4, 6, 12, 15; TPI adults 0, 1, 4, 6, 12, 15), the pose generators are `tools/blender/opentpw_guest_anims.py` (land)
and the swim poses of `opentpw_swim_sprites.py`. The motions were recreated from the originals' frames (stride width and arm swing of
the walk and run, the arm arcs of the gestures, the hunch of the stumble, the raised arms of the cheer); the timing is a plain
even sampling of each loop and the poses are not frame-for-frame copies.

### Outfits

`build_kid(preset, outfit="clothes")` builds the land outfit of `CLOTHES` (jacket, dress, shirt, cardigan or suit; sleeves; shorts,
trousers, skirt or dungarees; socks; shoes; the TPI caps, the dad's safari hat and camera), after the originals' standing sprites;
`outfit="swim"` the swimwear. The generator uses the clothes for the land slots and the swimwear for the swim slots, and
`--outfit swim` (or the preset switch in `build_kid`) draws swimwear on land too, for a mod that wants it everywhere.

### Head sets

`Generic/Kidsheads`, `Parentsheads` and `Grandparentsheads` hold one slot 0 of 7 elevation rows by 8 azimuths, direction-major
(frame = 8 * row + column), drawn on riders of `ADDHEAD` rides ([GUESTS.md](GUESTS.md#riders-heads)). The camera sits at row * 30
degrees from the head's up pole and column * 45 degrees from its front, column 2 seeing the head's left side (face to the left in the
picture), which is how the original frames are arranged; at the poles the picture's up follows the azimuth. Dry hair, hats on. A head is
rendered at 1 / 0.685 of the body's pixel scale (the game draws a head sprite at 0.685 of the body's scale) times 0.9, so the frames are
25 to 42 px wide like the originals' (25 to 40). The shipped TPW sets store 55 or 56 frames (one set 64); ours store 56.

### Not reproduced well

- Faces do not change: the originals' cheer, stumble and sick frames show different mouths and eyes; ours keep one face.
- The hands cannot reach the hips (the elbow only bends in the arm's plane), so slot 14 and the stumble approximate them.
- Slots 4 and 6 of the originals have a few extra poses (a head shake, a shrug) that are folded into the four we make.
- Our lighting is baked from one fixed direction, so the mirrored half of the directions is lit from the other side, as in the originals.
- The shipped heads' hotspots sit about 5 pixels lower than ours; the head frames' sizes are matched, their placement is not.


### Third-review fixes (measured against the original FPC frames)

| Measure | Before | After | Original |
| --- | --- | --- | --- |
| TPI camera elevation | 34 degrees | 25 | matches widths and heights best |
| TPW camera elevation | 32 degrees | 27 | 25-30 by eye (45 since October 18, 2026, measured against the TPC) |
| Walk foot spread, direction 2, contact frame (px; BE / BD / FA) | 22 / 26 / 39 | 28 / 34 / 40 | 28 / 33 / 38 |
| Walk foot spread at the passing frame (px; BE / BD / FA) | 14 / 15 / 20 | 12 / 14 / 18 | 11 / 15 / 20 |
| Run foot spread at contact (px; BE / BD) | 25 / 30 | 28 / 35 | 33 / 41 |
| Head height over total height, direction 4 (BE / BD / FA) | 0.39 / 0.38 / 0.29 | 0.35 / 0.33 / 0.26 | 0.32 / 0.33 / 0.24 |

Walk and run now start at the contact pose (legs furthest apart, as the originals' frame 0), pass with the feet together a quarter
of the loop later and push off at the next contact; the body is highest at the passing frame, and the hip swing and arm swing scale
with a per-body `stride` (kids 1.0, mum 0.85, dad 0.72, granny 0.6, grandpa 0.62). Kids have longer legs (0.46 + 0.42) and a head
scale of 0.84; the adults' heads are 0.85 to 0.95. FA has a smaller hat brim and a bigger nose so his face shows in profile; GR stoops
34 degrees, has darker skin so the bald top does not read as a white ball, and the white hair sits at the sides and back. The TPI head
sets use a hotspot 6 px lower (the shipped TPI heads put it about 9 px below the head centre), and FA's head is drawn 1.3 times larger.
The head ratio is measured at the narrowest row between 25 % and 55 % of the frame height, so it is a rough number.
