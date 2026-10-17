# Content kit: authoring new objects

The content kit turns original, newly made art into files the game reads: MD2 models, textures, ride scripts and
sprite banks, packed as an object a data-only mod adds through `objects.json` ([MODS.md](MODS.md#objects)). It exists
for community content such as the planned swimming pools. Nothing in it reads or copies art from the original game;
the original corpus is only used to prove that each writer produces files the readers (and the formats) accept.

Status (October 17, 2026): phase 0 is under way. In place:

- the Blender side (`tools/blender/`): authoring conventions, a validating glTF exporter, a generated sample object and
  the swimmer sprite generator ([below](#swimmer-sprites));
- `GltfScene` (`source/OpenTPW.Files/Formats/Model/GltfScene.cs`), the reader for what the exporter writes.

Still to come in phase 0: the MD2 writer and the glTF-to-MD2 converter, the RSE text form and assembler, the ESP
sprite-bank writer, a content-kit command line that builds a mod folder, and an end-to-end check that a built object
loads, is placed and runs its script.

## Blender conventions

`tools/blender/opentpw_conventions.py` holds the rules, and the exporter checks them before it writes anything:

- **Units and axes.** One Blender unit is one MD2 unit; one park cell is 10 units ([MAP.md](MAP.md)), and a kid guest is
  about one cell tall ([GUESTS.md](GUESTS.md#rendering)). Blender is Z-up; the glTF export is Y-up, which is the MD2 axis
  convention. The front of a footprint (its text row) is model +Z, which is Blender −Y.
- **Rider heads.** An object (usually an empty) with the custom property `opentpw_head` = n becomes head record n, the
  point where a ride script's `ADDHEAD` hangs a guest's head ([GUESTS.md](GUESTS.md#riders-heads)). Ids must run from 1
  without gaps, because the original's script init counts ids from 1 until one is missing.
- **Materials.** `opentpw_wrap` (default true) wraps the texture in U and V (MD2 slot flag 0x10), else it is clamped;
  `opentpw_self_lit` (default false) draws the mesh in one constant colour instead of per-vertex lighting (material
  word +0xC bit 0x1, which the original uses for water, signs and glow).
- **Textures.** The image name becomes the texture name, `<name>.tga` in the object's `textures/` folder. MD2 texture
  names are 20-byte fields, so a name may have at most 15 characters before `.tga`. tp.exe tries `<name>.wct` and then
  `<name>.tga` in the archive's `textures` folder ([ObjectAssets](../source/OpenTPW/World/Objects/ObjectAssets.cs)),
  so `.tga` needs no wavelet encoder. Which TGA variants tp.exe's own loader accepts is not checked yet; OpenTPW decodes
  them with stb.

Export a scene (or one collection) with:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b scene.blend --python tools/blender/opentpw_export.py -- --out object.glb --collection Pool
```

`tools/blender/opentpw_sample_pool.py` builds the kit's sample from code: a 3 × 3 cell pool (four rim boxes and a
self-lit water box with generated checker textures) with six head points just above the water, and exports it:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python tools/blender/opentpw_sample_pool.py -- --out pool.glb
```

## glTF reader

`GltfScene.Read` reads binary glTF 2.0 (`.glb`) with one embedded buffer:

- nodes with their parent-relative transform, as a row-vector matrix like MD2 nodes;
- triangle primitives with float positions, normals and the first UV set, and u8, u16 or u32 indices;
- materials with their base-colour image;
- embedded images, kept as their PNG or JPEG bytes;
- the `extras` of nodes and materials.

It refuses rather than ignores anything it cannot carry into an MD2 object: skins, animations, morph targets, sparse
accessors, external buffers or images, required extensions, non-triangle primitives, shared or cyclic children, and
indices outside the vertices. Animation is out of scope for now.

Tests (`GltfSceneTests`): hand-built files for the fields, the TRS and matrix transforms and each refusal. When Blender
is installed, `ReadsTheBlenderSamplePool` also runs the sample script and reads its export. The test looks for Blender at
`OPENTPW_BLENDER`, else `/Applications/Blender.app`, and is inconclusive without it.

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
vertical local contrast is about 20 % higher; mean colours differ because ours wear swimwear and the originals clothes.

Directions 0 to 4 put the camera at azimuth
`-45 * direction` (0 looks at the kid's back, 2 sees it facing screen right, 4 facing the camera); the game mirrors the
rest.

| Constant | TPW | TPI |
| --- | --- | --- |
| Camera elevation | 32 degrees | 34 degrees (re-measured against the FPC frames: 25 degrees matched widths and heights best, 42 the look of hats and shoulders, so a compromise) |
| Pixels per unit (full detail, `.FPC`, what the game draws) | 11.875 | 14.3 |
| Pixels per unit (low detail, `.TPC`, the calibration reference) | 9.5 | 11.44 |
| Calibration reference cell / standing hotspot | 48 x 56, feet at (24, 53) | 64 x 80, feet at (32, 70) |
| Swim frame canvas / hotspot | 80 x 60, (40, 34) | 100 x 80, (50, 45) |

The guest sets ship their art twice: a low-detail `.TPC` bank and the full-detail `.FPC` bank the game opens (ESP byte 0x10C,
[GUESTS.md](GUESTS.md)). The FPC frames are 1.25 times the size of the TPC ones (a kid stands 40 to 42 px tall in the FPC, 32 to
37 in the TPC). The reference sheets the calibration was done against came from the TPC (`ppu_tpc`); every sprite the generators
write is rendered at the FPC scale (`ppu`, 1.25 times larger), and `opentpw_kid_calibrate.py` keeps comparing at the TPC scale.

`tools/blender/opentpw_kid_calibrate.py` renders the standing kid in the original's cell for every set and, given a
reference sheet of the original standing frames (viewed locally only), writes a side-by-side comparison (original left,
ours right) and prints heights. Standing height in pixels (direction 4), ours vs original, at the TPC scale of the reference sheets (the FPC-scale sets of the next section are 1.25 times larger):

| TPW | BE | BI | CH | FR | KI | SA | SU | TA |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ours | 32 | 33 | 31 | 33 | 32 | 30 | 31 | 32 |
| original | 32 | 32 | 34 | 31 | 34 | 30 | 34 | 32 |

| TPI | BD | GB | GK | KB | FA | MO | GM | GR |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ours | 38 | 37 | 36 | 39 | 49 | 47 | 41 | 37 |
| original | 40 | 41 | 38 | 43 | 50 | 48 | 42 | 38 |

All within 3 pixels for TPW and within 4 for TPI (the TPI kids come out a little short), most within 2. The proportions follow the originals more closely since October 17, 2026:
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
sets as `/esprites/Generic/<Folder>/<NAME>.ESP` plus the bank its ESP selects, so a replacement set ships at the original paths:

```
data/esprites/Generic/Kids/SPR_BE.ESP        .FPC   .TPC        (TPW kids; TPI: SPR_BD, SPR_GB, SPR_GK, SPR_KB)
data/esprites/Generic/Parents/SPR_FA.ESP     .FPC   .TPC        (TPI: SPR_FA, SPR_MO)
data/esprites/Generic/Grandparents/SPR_GM.ESP .FPC  .TPC        (TPI: SPR_GM, SPR_GR)
data/esprites/Generic/Kidsheads/SPR_BE.ESP   .TPC               (the head sets ship a .TPC only; also Parentsheads, Grandparentsheads)
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

`name` is the set's member name; `fullDetail` is the ESP byte 0x10C (true: the game opens the `.FPC`, and both banks are written with the
same content, as the shipped full-detail sets do; false for the heads); `kidFlag` is ESP byte 0x10D (1 for the girls); `frames` are
listed direction-major, as the bank stores them. Heads have `source` `heads`, one slot 0 of 7 directions (elevation rows) by 8 frames
(azimuths).

### Hotspot rule

`originX`/`originY` follow the reader (`SpriteFrame.OriginX/OriginY`): the offset of the frame's top-left pixel from the hotspot, the
quad spanning `originX` to `originX + width` (and likewise vertically) in 1/128 units. The hotspot of a standing, walking, running
or gesturing guest is on the floor between the feet; ours is the world origin pixel of the render, moved one pixel lower
(`LAND_HOTSPOT_DROP`), because our soles reach 1 to 2 pixels lower than the originals' (the originals' soles end 1 to 2 pixels below
the hotspot). A swimmer's hotspot is the centre of the torso at the water line (slots 8 to 10), a head's the centre of the skull. All of
a set's frames of one slot share one hotspot, so the guest does not jump between frames. Frame content against the originals' FPC
frames (ours / original, in pixels; `top`/`bottom` are `originY` and `originY + height`):

| Set | Slot | Width | Height | Top | Bottom |
| --- | --- | --- | --- | --- | --- |
| BE | 0 | 21 to 26 / 20 to 27 | 38 to 42 / 40 to 42 | -39 to -37 / -41 to -38 | 1 to 3 / 1 to 2 |
| BE | 1 | 22 to 28 / 17 to 32 | 36 to 42 / 38 to 43 | -39 to -36 / -41 to -38 | 0 to 5 / 0 to 2 |
| BE | 12 | 20 to 26 / 19 to 33 | 37 to 42 / 40 to 45 | -43 to -37 / -43 to -38 | -3 to 3 / 1 to 2 |
| TA | 0 | 22 to 26 / 17 to 26 | 39 to 41 / 38 to 41 | -38 to -38 / -39 to -37 | 1 to 3 / 1 to 2 |
| TA | 1 | 22 to 28 / 16 to 31 | 36 to 42 / 36 to 41 | -39 to -35 / -39 to -35 | 0 to 5 / 1 to 2 |
| TA | 12 | 22 to 26 / 17 to 32 | 35 to 42 / 37 to 41 | -42 to -36 / -39 to -36 | -3 to 3 / 1 to 2 |
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
