# Enhanced textures (shipped HD interface art and optional local texture packs)

October 10, 2026. Status: **OpenTPW extension (`[EXT:texture-pack]`), Enhanced by default.**
The options offer **Original** (the game's own textures, exactly as shipped) and **Enhanced** (the
default): the HD interface art that ships with OpenTPW, plus a locally built AI texture pack when there
is one. Original reproduces the original look unchanged.

## Shipped interface art

`content/hero-art/textures/ui/textures/*.wct.png` holds 48 HD replacements (8×, e.g. a 64×64 texture
becomes 512×512) for the HUD, lobby and options buttons: buy, info, money, research, map, the build
categories (rides, shops, shows, features), door, enter-park, erase, OK, exit, retract, the
arrows and the slider ball, with their pressed, highlighted and disabled states. With Enhanced they
replace the original interface art before any local pack is consulted (`TexturePack.Find`).

Provenance: on October 10, 2026 each base icon was cut out of the original texture at its alpha mask,
enlarged, and redrawn by Google Gemini's image model in the Gemini web app with a fixed prompt per icon
("recreate exactly this icon as a crisp 1024×1024 image, same composition, colours and late-90s
glossy 3D cartoon style; do not redesign"; `tools/hero-art/prompts.tsv`). Matching pairs were made
as edits of one result (door closed/open, left/right arrow; the right lobby arrow is the mirrored
left one; the lit feature button is a recolour of the normal one). `tools/hero-art/fit.py` fits each
result back into the original texture layout: the icon is scaled onto the original icon's bounding
box, the outline comes from the original alpha mask (capped to the redrawn silhouette where the image model's
white backdrop would show through), background and fully transparent texels are recoloured with the nearest
icon colour so neither the backdrop nor texture filtering leaves a light halo, and the state variants are derived from the
redrawn base with the original's colour change (per-channel gain for pressed and grey states, a
fitted colour mapping for highlighted ones). For the round yellow HUD buttons and the green build-category
buttons, `tools/hero-art/rim.py` then keeps the original rim (from the anime-upscaled original) and takes
only the inner face and symbol from the redrawn icon, so every button has the original's thin shaded rim
instead of the heavier one the image model drew; the blue buttons, arrows, door panel and slider ball keep
the redrawn version. The two option toggles `b_on1`/`b_on2` (multi-piece
atlases) stay original. These images are AI recreations of the original EA/Bullfrog icons; the
project owner chose to ship them.

The original textures are small: 8,375 `.wct` textures in the installed WADs, mostly
32×32 (2,777) and 128×128 (2,352), the largest 256×128 (48.4 million pixels in total).
On a high-resolution or Retina display at native render size they are magnified a lot.
An enhanced texture pack replaces them with AI-upscaled copies (4×: 128×128
becomes 512×512).

## Variants

OpenTPW knows two world-texture variants. Each is a separate pack, a directory under
`<config>/texture-packs/` (on macOS `~/Library/Application Support/OpenTPW/texture-packs`), and you
can build either or both and switch between them in the options.

The original `.wct` textures are lossy (wavelet) compressed, so they carry blocking and ringing
artifacts that a 4x upscaler happily magnifies. Both variants therefore first run a 1x
de-artifact model over every world texture and only then upscale. The lead's comparison on eight
jungle textures at native resolution:

| Pack name | Options label | Pipeline | Look |
| --- | --- | --- | --- |
| `enhanced` (default) | Enhanced | 1x DeJPG, then Real-ESRGAN `realesrgan-x4plus` | Crisp edges, keeps the original cartoony CG style |
| `detailed` | Detailed | 1x DeJPG, then `ultrasharp-4x` (4x-UltraSharp) | Most detail, some grain |

Interface art always uses `realesrgan-x4plus-anime` without the pre-pass. Guest sprite atlases use
the variant's world model and no pre-pass by default (see "Pre-pass" below).

### Models and licences

You supply the models; OpenTPW ships and downloads none of them.

- **Real-ESRGAN ncnn Vulkan** executable and `realesrgan-x4plus`, `realesrgan-x4plus-anime`:
  BSD-3-Clause, https://github.com/xinntao/Real-ESRGAN/releases.
- **4x-UltraSharp** by Kim2091 (ncnn conversion `ultrasharp-4x` from Upscayl, in its `resources/models`
  folder, https://github.com/upscayl/upscayl): **CC-BY-NC-SA-4.0** (OpenModelDB). Non-commercial,
  share-alike, credit required. It is for building your own local pack; do not redistribute a pack built
  with it commercially. Put `ultrasharp-4x.param` and `.bin` into the `models` folder next to the
  Real-ESRGAN executable.
- **1xDeJPG (realplksr)** by Helaman/Phhofm, `1xDeJPG_realplksr_otf_fp32_fullyoptimized.onnx`:
  CC-BY-4.0, credit required, from the Phhofm/models releases (https://github.com/Phhofm/models).
  Fixed 1x3x256x256 float32 RGB input in 0..1.

## Building a pack

The pack is built on the player's machine from their own installation. It is a derived
work of the original assets, so it is **never shipped, committed or uploaded**.

1. Get `realesrgan-ncnn-vulkan` for your platform (the macOS build is universal and runs on Apple
   silicon through MoltenVK). Unpack it with its `models` folder next to the executable. For the
   Detailed pack add the Upscayl `ultrasharp-4x` model files there. Get the DeJPG ONNX file.
2. Build (the ONNX model runs through ONNX Runtime: CoreML on macOS when available, CPU otherwise):

   ```sh
   # Enhanced (default pack name "enhanced")
   bash scripts/run.sh --game-path '/path/to/Theme Park World' --build-texture-pack \
     --upscaler /path/to/realesrgan-ncnn-vulkan --prepass-model /path/to/dejpg.onnx

   # Detailed
   bash scripts/run.sh --game-path '/path/to/Theme Park World' --build-texture-pack \
     --upscaler /path/to/realesrgan-ncnn-vulkan --prepass-model /path/to/dejpg.onnx \
     --texture-pack-name detailed --upscale-model ultrasharp-4x
   ```

   Options: `--texture-pack-name <name>` (default `enhanced`; the pack goes to
   `<config>/texture-packs/<name>`), `--upscale-model <name>` (default `realesrgan-x4plus`),
   `--texture-pack-hero-dir <dir>` (hero art, below), `--prepass-model <onnx>` (without it there is no pre-pass and the build is exactly the old one),
   `--prepass-sprites`, `--texture-pack-dir <dir>` (overrides the location), `--texture-pack-subtree <data-relative dir>`
   (e.g. `levels/jungle`, for a quick trial; it selects WAD directories), `--interface-model <name>` (default
   `realesrgan-x4plus-anime`), `--texture-pack-no-interface`, `--texture-pack-interface-only`,
   `--texture-pack-sprites-only` (with `--texture-pack-interface-only` both) and
   `--texture-pack-merge` (keep that pack's existing textures and add or replace the ones built now;
   it warns when the models differ). All of these act per pack name. The game only lists packs under the
   config directory; a pack built elsewhere is used with `OPENTPW_TEXTURE_PACK=<dir>/textures`, which pins
   the pack and overrides the setting.
3. Choose **Game Options -> OpenTPW -> Enhanced textures** (Original, Enhanced, then Detailed and any other
   installed pack) and press OK. The textures are swapped in the running game behind a loading bar; no
   restart is needed. Enhanced is always offered: without a local `enhanced` pack it means the shipped
   interface art only.

### Setting and migration

`graphics.json` stores the choice as `"TexturePack": "<name>"`; `""` is the original textures,
`"enhanced"` (the default, also when the key is missing or null) is Enhanced, `"detailed"` Detailed, any
other name is a pack directory of that name (with the shipped interface art). Files from earlier
versions with `"EnhancedTextures": true` are read as `"TexturePack": "enhanced"`, `false` as Original;
the old key is dropped the next time the settings are saved. Smoke tests always run on the originals. If both keys exist the new one
wins. Names must be plain directory names (letters, digits, `-`, `_`, `.`).

### Pre-pass

`--prepass-model` runs the 1x model over each world texture before padding and upscaling. The model has a
fixed 256x256 input: textures up to 192 px per side are centred in one tile and wrap-padded (world
textures tile), larger ones are cut into overlapping tiles with a 32 px margin that advance by 192 px,
reading across the border by wrapping, so tiling textures stay seamless. Only RGB is processed; alpha
passes through untouched. `pack.json` records the model as `PrepassModel`.

Guest sprite atlases (decision): they use the variant's world model and, by default, **no** pre-pass. Trial on
the `spr_be` atlas with `--prepass-sprites` (RGB only, edge-padded, alpha untouched) showed no fringes, but the
gain is marginal (slightly cleaner shading), the 1024x256 atlas takes 12 model runs (about 18 s per atlas, 146 s
for the eight kid atlases against 0 s) and tile borders can show faint steps on non-tiling art. Add
`--prepass-sprites` to opt in.

Timing: one 256x256 run takes about 1.2 s on Apple silicon through CoreML (1.9 s on CPU) and the builder
runs four textures at once (about 0.47 s per 128x128 texture, `OPENTPW_PREPASS_PROVIDER=cpu` forces the CPU
provider). A 128x128 texture takes one run, a 256x256 one four. A full build with the pre-pass therefore takes
roughly an hour or more on top of the upscale; try `--texture-pack-subtree levels/jungle` first.

### Hero art

`--texture-pack-hero-dir <dir>` points at a directory of hand-made or redrawn replacements. A file named
like the pack's texture, e.g. `ui/textures/b_buy.wct.png` (lower case, same layout as the pack's `textures`
directory), is copied over the automatic result for that key, so it wins over the upscale and survives rebuilds
and `--texture-pack-merge`. It may have any size whose aspect ratio equals the original texture's; a file with
another aspect ratio, or for a texture that does not exist, is skipped with a warning. Hero art is applied at the
end of every build that names the directory and counted in `pack.json` as `HeroTextures`. Keep it outside the repository.

### Switching at runtime

Changing the choice in the options takes effect at once, in the background: there is no loading screen
and the player keeps playing while the textures change one by one; a small line at the top of the screen
shows "Updating textures in the background... n / total" until the switch is done. (Building a pack is a
separate, slow step: see "Building a pack".) Technically, the switch reloads every texture the game has made from a `.wct` (a registry of
weak references keyed by game path): the new pixels are decoded on a worker thread through a small
bounded queue (a 4x pack would otherwise hold gigabytes in memory), uploaded on the render thread in
slices of about 8 ms per frame, and swapped into the existing `Texture` objects. Materials look the GPU
texture up when each draw is recorded, and the old GPU texture is deleted after the frame. A full switch is fast: in the front end, 134 loaded textures swapped to and from the real 1.4 GB
pack in 0.5-1.3 s over 60-140 frames, with the working set unchanged (about 4 ms per texture on the render thread, so the roughly 4,200 textures of a
fully loaded park take under half a minute). Interface images
(UI renderer cache) and guest sprite atlases reload when the switch ends. Textures from the bonus content
roots do not come from game `.wct` files and are not part of the pack. `OPENTPW_TEXTURE_PACK` pins the pack,
so the options row has no effect then.

## What the builder does

- Reads every `.wct` in the installation's WADs and keeps the game path the engine loads it
  by (lower case), e.g. `levels/jungle/terrain/textures/jgr_bas1.wct`.
- Keeps these original: the low-detail `stexture`/`ssharete` variants (also `ui/stexture`),
  fonts, textures whose smaller side is below 32 pixels (too little detail to upscale) and
  world textures containing exact magenta (255,0,255), which the renderer may treat as a
  chroma key.
- Upscales interface art (`ui/textures`: buttons, panels, frames, icons) with a separate model,
  `realesrgan-x4plus-anime` by default: it is drawn art with flat colours and hard outlines, where
  the photographic model softens edges. Interface textures carry real alpha after decoding; the
  model keeps it. They are edge-padded (border pixels repeated) instead of wrap-padded, because a
  UI texture is an atlas of separate pieces (the two ends of `purple_button`, the parts of a
  panel), not a tile. The UI loader (`UiImages`) checks the pack like `Texture` does; model UVs
  are relative, so the 4× image drops in.
- Upscales the guest sprites. Guests are pre-rendered 2D sprites (`esprites.wad/Generic/Kids/SPR_xx.FPC`
  with `.ESP` animations), not `.wct` textures; OpenTPW packs each set into a 1024×256 RGBA atlas at
  load time. The builder builds the same atlases and upscales them with the world model
  (`realesrgan-x4plus`: the sprites are rendered 3D figures, and the anime model drew outlines around
  hair and faces), edge-padded, under the pack key `esprites/generic/kids/<set>.atlas`. The guest
  renderer uses the pack's atlas when its aspect ratio matches; frame UVs are relative.
- Before upscaling interface art and sprites, fully transparent texels take the colour of their
  opaque neighbours (up to four texels out, alpha stays 0). The decoded textures leave white or black
  in empty texels, which the upscaler would otherwise blend into a light or dark fringe.
- Wrap-pads each texture by an eighth of its smaller side (2–8 pixels) before upscaling and
  crops the padding afterwards, so tiling textures (grass, paths) stay seamless.
- Runs the optional pre-pass on world textures, then each upscaler once over its textures and writes `textures/<game path>.png` plus
  `pack.json` (format, scale, upscaler, model, interface model, pre-pass model, counts per skip reason).

At load time `Texture` checks the active pack before decoding a `.wct`; a missing file
falls back to the original. `OPENTPW_TEXTURE_PACK=<pack>/textures` forces a pack for tests.

## Results and limits (MacBook Pro, Apple silicon, Retina)

- A full build from an English installation took about 15 minutes: 3,784 textures
  upscaled; kept original 921 interface, 3,617 low-detail, 49 small and 4 unreadable
  textures (four low-detail `.wct` files the current decoder cannot read; the builder now
  skips low-detail paths before decoding, so they no longer show up). The pack is
  1.3 GB of PNG. A jungle-only trial upscaled 1,698 textures in 167 seconds.
- Adding the guest sprites (and rebuilding interface art with the fringe fix) to that pack with
  `--texture-pack-interface-only --texture-pack-sprites-only --texture-pack-merge` took about a
  minute: 8 sprite atlases and 453 interface textures.
- Adding interface art to the existing pack with `--texture-pack-interface-only
  --texture-pack-merge` upscaled 453 interface textures in about 13 seconds (6 below 32 pixels
  stayed original). Buttons, the lobby panel, arrows and round edges become sharp at 2560×1440,
  where the original 64–128-pixel art was stretched 3–5× with bilinear filtering.
- In the 3D lobby the difference is visible on the island (grass, rock edge, the dinosaur).
  At the default park camera it is small: a texture covers only about
  100–200 screen pixels there and is sampled from a lower mip level. The gain shows when
  zoomed in and in first-person ride views, where a texture is stretched across much of
  the screen.
- The model invents detail that the original never had (individual grass blades on the
  jungle grass) and shifts colours slightly darker. This is a presentation choice, not a
  restoration; compare with the original before keeping it on.
- Real-ESRGAN keeps the original soft alpha (WCT alpha is wavelet-compressed with many
  intermediate values) and upscales it with the colour.
- Memory: 4× means 16× the pixels. The
  textures are uploaded uncompressed with mipmaps, so GPU memory use grows accordingly.
- Animated texture frames are upscaled independently; no flicker was checked.
