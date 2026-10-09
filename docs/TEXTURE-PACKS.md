# Enhanced textures (optional local texture pack)

October 9, 2026. Status: **optional OpenTPW extension (`[EXT:texture-pack]`), off by default.**
Original textures are always the default; nothing here changes original behaviour.

The original textures are small: 8,375 `.wct` textures in the installed WADs, mostly
32×32 (2,777) and 128×128 (2,352), the largest 256×128 (48.4 million pixels in total).
On a high-resolution or Retina display at native render size they are magnified a lot.
An enhanced texture pack replaces them with AI-upscaled copies (Real-ESRGAN, 4×: 128×128
becomes 512×512).

## Building a pack

The pack is built on the player's machine from their own installation. It is a derived
work of the original assets, so it is **never shipped, committed or uploaded**; OpenTPW
neither ships nor downloads the upscaler.

1. Get `realesrgan-ncnn-vulkan` for your platform from the Real-ESRGAN releases
   (https://github.com/xinntao/Real-ESRGAN/releases, BSD-3-Clause; the macOS build is
   universal and runs on Apple silicon through MoltenVK). Unpack it with its `models`
   folder next to the executable.
2. Build:

   ```sh
   bash scripts/run.sh --game-path '/path/to/Theme Park World' --build-texture-pack --upscaler /path/to/realesrgan-ncnn-vulkan
   ```

   Options: `--upscale-model <name>` (default `realesrgan-x4plus`), `--texture-pack-dir <dir>`
   (default `<config>/texture-packs/enhanced`, next to `display.json`; on macOS
   `~/Library/Application Support/OpenTPW`), `--texture-pack-subtree <data-relative dir>`
   (e.g. `levels/jungle`, for a quick trial). The game itself only loads the default
   location; a pack built elsewhere is used with `OPENTPW_TEXTURE_PACK=<dir>/textures`.
3. Turn on **Game Options → Enhanced textures** (or set `"EnhancedTextures": true` in
   `graphics.json`) and restart the game. Without a pack the row shows "No pack built" and
   cannot be turned on.

Building writes to a temporary directory and replaces an existing pack only when it is
complete; `pack.json` is written last. A failed build, including an upscaler run that
leaves an output missing or the wrong size, leaves the previous pack untouched; if
moving the new pack into place fails, the previous one is moved back.

## What the builder does

- Reads every `.wct` in the installation's WADs and keeps the game path the engine loads it
  by (lower case), e.g. `levels/jungle/terrain/textures/jgr_bas1.wct`.
- Keeps these original: interface art (`ui/`, pixel art drawn at integer scales), the
  low-detail `stexture`/`ssharete` variants, textures whose smaller side is below 32 pixels
  (too little detail to upscale) and textures containing exact magenta (255,0,255), which
  the renderer may treat as a chroma key.
- Wrap-pads each texture by an eighth of its smaller side (2–8 pixels) before upscaling and
  crops the padding afterwards, so tiling textures (grass, paths) stay seamless.
- Runs the upscaler once over all textures and writes `textures/<game path>.png` plus
  `pack.json` (format, scale, upscaler, model, counts per skip reason).

At load time `Texture` checks the active pack before decoding a `.wct`; a missing file
falls back to the original. `OPENTPW_TEXTURE_PACK=<pack>/textures` forces a pack for tests.

## Results and limits (MacBook Pro, Apple silicon, Retina)

- A full build from an English installation took about 15 minutes: 3,784 textures
  upscaled; kept original 921 interface, 3,617 low-detail, 49 small and 4 unreadable
  textures (four low-detail `.wct` files the current decoder cannot read). The pack is
  1.3 GB of PNG. A jungle-only trial upscaled 1,698 textures in 167 seconds.
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
