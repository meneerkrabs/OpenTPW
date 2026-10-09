# Optional upscaling — design

Added on request on October 9, 2026. Status: **M6-U1 implemented and smoke-tested on
macOS arm64/Metal; Windows/D3D11 and Linux/Vulkan untested** (see
"Implementation status" below). This is an optional presentation improvement, not a
replacement for full offline gameplay or original graphics fidelity. No new dependency
has been added.

## Implementation status (October 9, 2026)

Implemented (M6-U1):

- `DisplaySettings` (`source/OpenTPW/Client/Display/`): window size (logical units),
  `Windowed`/`Borderless`/`Exclusive`, `Native`/`Linear`/`Nearest`, render scale
  (presets 77/67/59/50, custom 50–100) and UI scale. JSON in
  `~/.config/OpenTPW/display.json` (Windows `%APPDATA%\OpenTPW`, or
  `OPENTPW_CONFIG_DIR`); never in saves. Missing fields = Native defaults; invalid
  values fall back with diagnostics; unknown method names are not silently mapped to a
  different algorithm.
- `RenderScaling.Compute`: internal size = rounding of output × scale per dimension,
  minimum 1 pixel, capped at the device texture limit (aspect preserved, with a
  reason). Native = 100% and the existing blit; minimizing pauses without zero-size
  allocations. An allocation failure at the scaled size falls back to native with a
  reason; a native failure is a hard error.
- Renderer: world (4x MSAA remains a separate setting) at internal size → resolve →
  blit with Linear or Point sampler to the swapchain at drawable pixels → BF4 UI and
  ImGui at output size. Camera aspect follows the output. Changes (resize, fullscreen,
  DPI, scale) are applied at a frame boundary after `WaitForIdle`; all own
  color/depth/resolve/capture textures, framebuffers and resource sets are disposed
  (previously the MSAA color and depth texture leaked on resize).
- HiDPI: window with `SDL_WINDOW_ALLOW_HIGHDPI`; drawable via
  `SDL_GetWindowSizeInPixels` (fallback Metal/Vulkan drawable, then window size).
  Input, picking and `Panel` layout remain in logical units; ImGui receives
  `DisplayFramebufferScale`. Render scale is never a mouse scale factor.
- UI scale policy: integer factors only, so point-sampled BF4 text stays pixel-exact.
  Automatic = largest integer at which 1280×720 fits in the output pixels (1 to
  2559×1439, 2 from 2560×1440/Retina, 3 at 4K). ImGui (debug UI) does not scale.
- Movies always render at output size (no 3D world; fallback reason logged).
- Diagnostics: the log and the ImGui "Display" section show method, requested/effective
  scale, internal/output size and fallback reason. `OpenTPW.IDisplaySettings` is the
  UI-agnostic API for the in-game Options screen, including confirm or revert after N
  seconds (UIStrings 401/402).
- Tests: CPU tests for presets, rounding (≤ 0.5 internal pixel per axis), clamps,
  device limit, fallbacks, zero size, DPI conversion, picking at 1x/2x/3x and 77/50%,
  UI scale, JSON/CLI/file round trip and the confirmation flow. The Native smoke test
  now also reads back the final output after scaling and BF4 UI (ImGui excluded),
  checks target/output sizes, exact BF4 text, picking of the Totem cell and a
  neighboring cell, and switches runtime scale/method/window size, an unconfirmed
  change with timeout and a fullscreen toggle without growth of own GPU resources.

Not done / not proven:

- Real Retina hardware: only a 1x screen with the test variable
  `OPENTPW_TEST_PIXEL_SCALE=2` (drawable 2× the window size) has been tested. Veldrid's
  `CAMetalLayer.contentsScale` stays 1; sharpness on a real Retina screen is
  unconfirmed.
- Exclusive fullscreen is implemented via `SDL_SetWindowDisplayMode`, but is
  experimental and not smoke-tested (it changes the display mode); unavailable →
  borderless with a reason.
- Windows/D3D11 and Linux/Vulkan: not run. `app.manifest` does not declare DPI
  awareness, so Windows scales the window as a bitmap above 100%; per-monitor DPI
  (manifest + SDL hint) is a separate step to verify on Windows.
- Legacy `Panel`/`RootPanel` HUD (empty in park mode) still draws in the world pass.
- No GPU timing or performance measurement per scale; no performance claim.
- No capture comparison of native versus scaled for first-person/transparency.
- M6-U2 (EASU/RCAS), temporal upscaling and dynamic resolution remain deferred.

## Purpose and settings

- Default: `Native / off`, render scale 100%, no extra sharpening.
- First portable modes: `Linear` and `Nearest` via the existing fullscreen pass.
  Nearest is a deliberate retro option, not a quality improvement for every image.
- With upscaling enabled: presets 77%, 67%, 59% and 50%; custom scale 50–100%.
  Percentages apply to both dimensions, not to the number of pixels. At 50% width and
  height, the world renders about a quarter of the output pixels. These presets are
  our choices, not vendor quality labels or original values.
- Show the method, requested/effective scale, internal/output resolution and any
  fallback reason. Do not silently enable a different quality algorithm.
- Store preferences in user configuration, not in park/original saves. Invalid
  configuration falls back to Native with diagnostics; missing new fields keep the
  existing native render route.
- Sharpening stays off by default and is only available with an implemented, qualified
  method. No slider without a working underlying pass.

This concerns real-time upscaling of the 3D world. Offline AI texture upscaling,
replacement of original assets, supersampling and frame generation are not covered
here and are not added implicitly.

## Fit in the current renderer

`source/OpenTPW/Client/Renderer.cs` currently renders the world to a 4x-MSAA
framebuffer, resolves it to `ResolveColorTexture`, blits with `Device.LinearSampler`
to the swapchain, and then renders the editor/UI. `content/shaders/blit.shader`
contains the fullscreen texture sampling. The render targets now use `Screen.Size`.
That existing separation is the attachment point; no renderer rewrite is needed.

Desired order:

1. Determine the output size from the actual swapchain/drawable pixels, separate from
   the logical window size and DPI. Choose the internal size from output size × render
   scale; round consistently, clamp to at least one pixel and to device limits.
2. Render only the 3D world at that internal size. Keep the existing MSAA resolve
   intact; upscaling is not an antialiasing replacement, and MSAA is a separate
   setting.
3. Resolve, scale to output size, and run only explicitly chosen sharpening. Native
   keeps the current pass without a new filter.
4. Render text, menus, cursors and ImGui at output resolution. The UI is not passed
   through the world upscaler and stays sharp at lower world resolution.

Record the internal and output dimensions in one small render configuration; do not
introduce a plugin framework for two sampler choices. Camera aspect follows the output
aspect; rounding of internal dimensions must not stretch the image or shift picking.
Input and picking stay in the existing logical screen coordinates, with explicit DPI
conversion where needed; render scale must never become a second mouse scale factor.

Resize, fullscreen, DPI and scale changes replace targets and resource sets at a safe
frame boundary. Dispose all own color and depth textures and views, not only the
framebuffer object; retire old GPU resources only after their use has completed. When
minimized or zero-sized, rendering pauses without zero-size GPU allocations.
Allocation or capability failure gives a native fallback with a reason; if native also
fails, a clear error follows, not endless retries or a black screen reported as success.

Settings must **not** affect tick rate, RNG, simulation state, save format or replay.
GPU quality stays strictly outside the game core.

## Staged algorithm choice

| Step | Choice | Gate / constraint |
| --- | --- | --- |
| M6-U1 | Native, Linear, Nearest + fixed/custom scale | Existing Veldrid/shaders/samplers; qualify resize/DPI/input/resources first |
| M6-U2, optional | Research spatial edge-aware upscaling, for example FSR 1 EASU/RCAS | Prove source and license beforehand, pinned version, shader translation and actual Metal/D3D11/Vulkan costs; no automatic dependency addition |
| Later, separate decision | Temporal upscaling, for example FSR 2 or MetalFX | Investigate motion vectors, depth/jitter/history, camera-cut/reset and transparency/UI contracts first; the current renderer does not supply these inputs as an upscaling contract |

A vendor name in this design is not an integration or support claim. MetalFX does not
become the mandatory Mac route; a platform-specific extension must work alongside the
portable baseline. DLSS/XeSS and other vendor integrations are not release
requirements. Frame generation falls outside this slice.

Automatic or dynamic resolution only comes after GPU timing and fixed-scale proof:
bounded scale, hysteresis, adjustment interval and an explicit target frame budget. No
auto-scaling based on total frame time alone: a CPU or simulation bottleneck is not
solved by fewer pixels. Do not claim a performance gain without measurement; filter
overhead can be heavier than the savings at small resolutions.

## Acceptance and evidence

- CPU tests for preset/custom bounds, invalid values, rounding/device limits,
  Native = 100%, DPI/output versus logical input, and configuration round trip.
- Shader compilation and bindings for Metal/MSL, D3D11/HLSL and Vulkan/SPIR-V; actual
  package execution separately on each platform. Translated shaders are not a runtime
  pass.
- Same scene, camera and state: native and scaled captures of terrain, rides,
  transparency, UI/text and first-person. Check UV orientation, color space, aspect,
  sharp UI and the absence of extra picking offsets.
- Native mode reproduces the baseline; tests must not hide a difference by running the
  native baseline through a new filter.
- Switch modes and scale repeatedly; resize, minimize/restore, fullscreen and DPI/focus
  changes. No black frames, crashes or growing own GPU resources.
- Measure GPU pass costs, frame percentiles and memory per resolution and backend, with
  fixed asset identity, camera and park load. Report CPU and GPU costs separately.
- Identical actions and simulation ticks produce the same canonical replay state for
  every image mode; rendering does not affect gameplay.
- Explicitly test the fallback for unsupported methods and allocation failure. Optional
  vendor algorithms must not block native gameplay, but the offered baseline modes must
  be qualified on all three target platforms for release.

The current GPU smoke test reads the internal resolve texture and excludes UI. It is
therefore only world-render evidence. For upscaling, add a capture of the final output
after scaling **and** UI; do not call the existing capture an end-to-end
upscaling/UI test.

## Primary research sources

- AMD FidelityFX Super Resolution: https://gpuopen.com/fidelityfx-superresolution/
- AMD FSR 1 source: https://github.com/GPUOpen-Effects/FidelityFX-FSR
- AMD FSR 2 source/inputs: https://github.com/GPUOpen-Effects/FidelityFX-FSR2
- Apple MetalFX: https://developer.apple.com/documentation/metalfx

Check the API, capabilities and license against the chosen pinned implementation before
integration; this design deliberately does not choose an unproven newest vendor SDK.
