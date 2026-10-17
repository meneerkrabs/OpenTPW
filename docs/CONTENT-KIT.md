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
| Camera elevation | 32 degrees | 42 degrees (the TPI sprites are rendered from a steeper camera) |
| Pixels per unit | 9.5 | 12.8 |
| Original cell / standing hotspot | 48 x 56, feet at (24, 53) | 64 x 80, feet at (32, 70) |
| Swim frame canvas / hotspot | 64 x 48, (32, 27) | 80 x 64, (40, 36) |

`tools/blender/opentpw_kid_calibrate.py` renders the standing kid in the original's cell for every set and, given a
reference sheet of the original standing frames (viewed locally only), writes a side-by-side comparison (original left,
ours right) and prints heights. Standing height in pixels (direction 4), ours vs original:

| TPW | BE | BI | CH | FR | KI | SA | SU | TA |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ours | 34 | 34 | 31 | 34 | 32 | 30 | 31 | 32 |
| original | 32 | 32 | 34 | 31 | 34 | 30 | 34 | 32 |

| TPI | BD | GB | GK | KB | FA | MO | GM | GR |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ours | 41 | 39 | 38 | 41 | 48 | 47 | 42 | 38 |
| original | 40 | 41 | 38 | 43 | 50 | 48 | 42 | 38 |

All within 3 pixels (most within 2); the spiky-haired boys and the TPW pigtail girls are the 3 px outliers.

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
