# OpenTPW in the browser

Status, October 10, 2026: the **front end runs in the browser** over WebGL2. The lobby with
its islands, the camera glide between them, the original UI (buttons, BF4 fonts, popup help)
and the options screen draw as on the desktop, and mouse and keyboard work. Parks, sound,
movies and the developer panels do not run in the browser yet (see the end of this page).

Nothing from the game is shipped or uploaded: the player chooses their own Theme Park World
folder, and the page copies the files the front end needs into the runtime's memory.

## Building and running

```sh
dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -o out/web
```

Serve `out/web/wwwroot` over http (WebAssembly needs a server, not `file://`), open
`index.html` and choose the game folder (the one that holds `Data`). No extra .NET workload
is needed: the build runs .NET in interpreter mode.

For local testing without the folder dialog, put a `files.txt` (one path per line, such as
`Data/ui.wad`) next to a copy or link of the game's `Data` folder, serve both from the same
server and open `index.html?data=<that folder's URL>`.

## Measurements (Apple Silicon, Chromium, local server)

| Check | Result |
| --- | --- |
| Download, Brotli-compressed | about 4.5 MB (15 MB uncompressed), runtime and game included |
| Game files copied for the front end | 205 files, everything in `Data` except movies, parks, start-up pictures and sound banks |
| Front end frame rate | 119 frames per second (the display rate) at 2048×1536 pixels, interpreter mode |

## How it works

### Two projects

- **`source/OpenTPW.Web`** compiles the game's own source files (OpenTPW, OpenTPW.Common,
  OpenTPW.Files) for `browser-wasm`. Files that only make sense on the desktop are left out
  or replaced by browser versions in `Browser/`:

  | Desktop file | In the browser |
  | --- | --- |
  | `Render/ShaderCompiler.cs` (native SPIR-V compiler) | `Browser/ShaderCompiler.cs` reads the precompiled shaders |
  | `Common/Client/SdlDisplay.cs` (SDL display queries) | `Browser/SdlDisplay.cs`: canvas size times the device pixel ratio |
  | ImGui developer panels, the editor, SDL audio | `Browser/DeveloperPanels.cs`, `Browser/DesktopOnly.cs`: silent stand-ins |
  | `Program.cs`, `Client/Game.cs` (command line, setup window) | `Program.cs`: `AddFile`, `Start` and `Frame` for the page |

  The desktop code itself only skips two things in the browser: watching shader files and
  colouring console output.

- **`source/OpenTPW.Web.Veldrid`** implements the Veldrid API the game uses over WebGL2. The
  description structs and enums have Veldrid's names, fields and values (the enums are
  generated from the Veldrid assemblies), so game code compiles unchanged. Textures, buffers,
  framebuffers, samplers and pipelines live in `wwwroot/opentpw-gl.js` behind integer
  handles. A `CommandList` records draws and buffer updates as integers and bytes and replays
  them in JavaScript at `SubmitCommands`, one interop call per submit, so updates keep their
  order relative to draws as in Veldrid. The canvas plays the SDL window: `Sdl2Window`
  turns its mouse, wheel and keyboard events into an `InputSnapshot`.

`Renderer.Run` is split into `Start`, `Frame` and `Stop`. The desktop keeps looping over
`Frame`, and the page calls it from `requestAnimationFrame`.

A Veldrid call added on the desktop that the replacement lacks breaks the browser build at
compile time instead of failing at run time. `tools/AssemblySurface` lists what a compiled
assembly uses from another package:

```sh
dotnet run --project tools/AssemblySurface -- path/to/OpenTPW.dll Veldrid
```

### Shaders

All nine shaders in `content/shaders` cross-compile to GLSL ES 3.00 through the existing
SPIR-V path (`ShaderTests.NativeShadersCrossCompileToWebGl2`), and every generated pair also
compiles and links in a real WebGL2 context. The native SPIR-V compiler does not run in the
browser, so the shaders are stored precompiled in `content/shaders/web/<name>.json` (vertex
and fragment source, vertex elements, resource layouts with their names), embedded in the
browser build. `WebShaderTests` fails when those files no longer match the shader sources;
after changing a shader, regenerate them with

```sh
OPENTPW_NATIVE_SHADER_TESTS=1 OPENTPW_WRITE_WEB_SHADERS=1 dotnet test source/OpenTPW.Tests -r osx-arm64 --filter WebShaderTests
```

CI does not run the native shader tests yet, so this check is local for now.

GLSL ES 3.00 has no `layout(binding = …)`. Pipelines therefore bind by position, as on Metal
and Direct3D, and look up each resource by its name from the shader's reflection: uniform
blocks and texture units are numbered in layout order, and a set's sampler applies to all
of that set's textures (SPIR-V-Cross names each combined sampler after its texture).

### Coordinates

OpenTPW renders with Metal and Direct3D conventions (depth 0..1, row 0 at the top). The web
shaders move depth to -1..1 (`fixClipSpaceZ`) and negate Y (`invertVertexOutputY`), so
everything WebGL renders lands in memory with row 0 at the top, as on the desktop.
Viewports, scissor rectangles, `gl_FragCoord` and texture uploads then need no conversion.
Two things change instead: the negated Y reverses the winding, so the browser swaps the
front face for culling, and the frame is flipped once when it is copied to the canvas
(`present` in `opentpw-gl.js`; every render target, the swapchain included, is an offscreen
framebuffer).

### Differences from WebGL2

- **BGRA**: WebGL2 has no BGRA8. `B8_G8_R8_A8_UNorm` is only used for render targets, so the
  browser stores it as RGBA8 (uploads to such a texture are swizzled anyway).
- **Multisampling**: the 4x world target is a multisampled renderbuffer, resolved into the
  sampled texture with `blitFramebuffer`.
- **Base vertex**: WebGL2 cannot offset indices; `DrawIndexed` offsets the attribute pointers.
- **`Map`/`Unmap` and `CopyTexture`**: only used to read back smoke-test and capture images on
  the desktop; the browser throws `NotSupportedException`.
- **Clicks within one frame**: the game reads mouse button state once per frame. A click
  that starts and ends within one browser frame stays down for that frame and is released in
  the next.

### Pitfalls found

- **Trimming removes reflection targets.** The file system creates archive handlers by
  reflection; the browser project roots its own assembly (`TrimmerRootAssembly`), and turns
  reflection-based `System.Text.Json` back on for the settings files.
- **`dotnet.run()` exits the runtime** when `Main` returns; `runMain()` keeps the exports
  callable.
- **`Console.ForegroundColor` throws** in the browser.
- **The window asks for its size before the device exists**, so the page's canvas is looked up
  when `opentpw-gl.js` loads.

## What the browser build still lacks

- **Parks:** starting a park needs the `levels` folder (about 45 MB per theme); copying all of
  it up front is slow, so it wants lazy loading (the File System Access API or a one-time copy
  into OPFS) and a check of the simulation's speed in the interpreter (AOT is the fallback,
  at a larger download).
- **Sound and movies:** the decoders already run in managed code; playback needs WebAudio in
  place of the SDL audio stand-in.
- **Developer panels:** they need ImGui, which is a native library.
- **Saving:** saves go to the in-memory file system and are lost when the page closes.
- **Online play** works over `fetch` and WebSockets once the server allows the page's origin.

## Background: what the game uses from Veldrid

Measured before the replacement was written, from the member references in the compiled
`OpenTPW.dll` and `OpenTPW.Common.dll`:

| Package | References | Notes |
| --- | --- | --- |
| Veldrid | 186 (about 60 types) | 9 `ResourceFactory.Create*` kinds, 22 `CommandList` and 16 `GraphicsDevice` members, plus description structs and enums |
| Veldrid.SDL2 | 31 | window and display queries; input types (`InputSnapshot`, `Key`, `KeyEvent`, `MouseEvent`) |
| Veldrid.SPIRV | 16 | shader compilation and reflection, replaced by the precompiled files |
| Veldrid.ImGui | 6 | developer panels only |

A build of all game sources for `browser-wasm` without Veldrid, SDL2 or ImGui failed in 45
files, almost all of them on Veldrid types; outside Veldrid only ImGui and SDL were missing.
An OpenTPW device interface implemented twice would have rewritten about 40 desktop files,
so the browser got its own Veldrid instead.

The first spike (same day) ran only the file readers in the browser: map, terrain, a ride
model and a texture parsed unchanged in about 430 ms, from a 2.4 MB download.
