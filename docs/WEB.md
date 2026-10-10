# Browser (WebAssembly) feasibility

October 10, 2026. Question: can OpenTPW run in a browser? This spike answers the part
that does not depend on rendering: do the OpenTPW file readers, the player's own game
data and the download size work in WebAssembly?

## The spike

`source/OpenTPW.Web` is a `Microsoft.NET.Sdk.WebAssembly` app (`browser-wasm`, .NET 10,
interpreter mode, no workload needed) that references the **unchanged** `OpenTPW.Files`
and `OpenTPW.Common`. The page asks the player for their own Theme Park World folder
(`<input webkitdirectory>`), reads only `levels/jungle/terrain.wad` and
`levels/jungle/rides/totem.wad`, copies them into the runtime's in-memory file system and
uses the desktop `BaseFileSystem` + `WadArchive` as-is. It then shows the jungle
attribute map (`MapFile`), the jungle terrain and the Totem ride (`ModelFile`, drawn with a
small WebGL2 renderer in `main.js`) and a path texture (`TextureFile`). Nothing from the
game is shipped or uploaded.

```sh
dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -o out/web
# serve out/web/wwwroot over http (wasm needs a server, not file://)
```

## Results (Apple Silicon, built-in browser, local server)

| Check | Result |
| --- | --- |
| Builds without extra workloads | yes (interpreter; AOT needs `wasm-tools`) |
| Download, Brotli-compressed | about 2.4 MB, runtime included |
| Runtime start | about 55 ms after download |
| Readers unchanged | yes: map 16,384 cells, terrain 20,405 triangles, Totem 316 triangles, texture 128×128 |
| Parse time for those four files | about 430 ms in the interpreter |

## Pitfalls found

- **Trimming removes reflection targets.** `BaseFileSystem.RegisterArchiveHandler<T>` creates
  archives by reflection, so the trimmed build lost `WadArchive`'s constructor. The project
  roots `OpenTPW.Files` and `OpenTPW.Common` (`TrimmerRootAssembly`).
- **Reflection-based `System.Text.Json` is disabled** in trimmed browser builds; use source
  generators or plain arrays across the JS boundary.
- **`dotnet.run()` exits the runtime** when `Main` returns; `runMain()` keeps exports callable.
- Folder access: `webkitdirectory` works in Chromium, Firefox and Safari but hands over
  every file of the folder; the File System Access API (lazy, Chromium only) or a one-time
  copy into OPFS would avoid reading hundreds of megabytes up front.

## What a playable browser build still needs

- **Rendering:** Veldrid has no WebGL or WebGPU backend. A WebGL2 implementation of the
  subset of `GraphicsDevice` OpenTPW uses, behind JS interop, is the most direct route;
  shaders would be cross-compiled to GLSL ES ahead of time instead of shipping
  `libveldrid-spirv`.
- **Window and input:** canvas events instead of SDL2.
- **Audio:** the MP2/ADPCM decoders already run in managed code; playback needs WebAudio.
- **Data:** lazy access to the whole `Data` folder (OPFS or File System Access) instead of
  in-memory copies.
- **Speed:** AOT compilation (larger download) for the simulation and decoders; .NET's
  browser threading is limited, so background work must tolerate a single thread.
- The online extension works over `fetch`/WebSockets once the server allows CORS.

## Rendering: the WebGL2 plan

### Shaders (checked)

All nine shaders in `content/shaders` cross-compile to GLSL ES 3.00 through the existing
SPIR-V path (`ShaderTests.NativeShadersCrossCompileToWebGl2`), and every generated pair
also compiles and links in a real WebGL2 context (Chromium, October 2026). The output has
no `layout(binding = …)` qualifiers, which ES 3.00 lacks, so the browser renderer binds
uniform blocks and samplers by name, using the names from `SpirvReflection`.
`libveldrid-spirv` is native and does not run in the browser: shaders and their
reflection are compiled ahead of time on the desktop into `content/shaders/web/<name>.json`
(vertex and fragment source, vertex elements, resource layouts with their names).
`WebShaderTests` fails when those files no longer match the shader sources; after changing
a shader, regenerate them with

```sh
OPENTPW_NATIVE_SHADER_TESTS=1 OPENTPW_WRITE_WEB_SHADERS=1 dotnet test source/OpenTPW.Tests -r osx-arm64 --filter WebShaderTests
```

CI does not run the native shader tests yet, so this check is local for now.

Coordinates: OpenTPW renders with Metal and Direct3D conventions (depth 0..1, row 0 at the
top). The web shaders move depth to -1..1 (`fixClipSpaceZ`) and negate Y
(`invertVertexOutputY`), so everything WebGL renders lands in memory with row 0 at the top,
as on the desktop. Viewports, scissor rectangles, `gl_FragCoord` and texture uploads then
need no conversion. Two things change instead: the negated Y reverses the winding, so the
browser swaps the front face for culling, and the final frame is flipped once when it is
copied to the canvas.

### What the game uses from Veldrid (measured)

Measured from the member references in the compiled `OpenTPW.dll` and
`OpenTPW.Common.dll`, not by searching the source:

| Package | References | Notes |
| --- | --- | --- |
| Veldrid | 186 (about 60 types) | 9 `ResourceFactory.Create*` kinds, 22 `CommandList` and 16 `GraphicsDevice` members, plus description structs and enums |
| Veldrid.SDL2 | 31 | window and display queries; input types (`InputSnapshot`, `Key`, `KeyEvent`, `MouseEvent`) |
| Veldrid.SPIRV | 16 | shader compilation and reflection, replaced by the precompiled files |
| Veldrid.ImGui | 6 | developer panels only |

A build of all game sources for `browser-wasm` without Veldrid, SDL2 or ImGui fails in
45 files, almost all of them on Veldrid types. Outside Veldrid it only needs ImGui (the 5
developer-panel files plus the editor) and SDL (`Window`, `SdlDisplay`, movie audio).
The managed packages (StbImageSharp, ImageSharp, Zio, SharpZipLib) build unchanged.

Three differences from WebGL2, none of them blocking:

- **BGRA**: `B8_G8_R8_A8_UNorm` is only used for render targets (swapchain, world target,
  capture), never for uploaded or read-back pixels, so the browser can use RGBA8 there.
- **`Map`/`Unmap`**: only used to read back staging textures for smoke-test and capture
  PNGs; the first milestone does not need it (`readPixels` can do it later).
- **4x MSAA world target with `ResolveTexture`**: a multisampled renderbuffer resolved
  with `blitFramebuffer` into the sampled texture.

### Approach

A **source-compatible Veldrid replacement for the browser build only**: a small assembly
that declares namespace `Veldrid` with exactly the types and members listed above,
implemented over WebGL2 through `[JSImport]`. The browser project compiles the game's
source files against it instead of the real Veldrid packages. The desktop code and its
smoke-test images stay unchanged, and a Veldrid call added on the desktop that the
replacement lacks breaks the browser build at compile time instead of failing at run time.
The alternative, an OpenTPW device interface implemented twice, would rewrite about
40 desktop files for no desktop gain.

Steps for the first milestone (the front-end lobby drawn in the browser; no developer
panels, sound, movies or park):

1. Split `Renderer.Run` into a `Frame()` that the browser calls from
   `requestAnimationFrame`; on the desktop `Run` keeps looping over it (done).
2. Precompile every shader to GLSL ES plus its reflection (done: `content/shaders/web`).
3. Write the Veldrid replacement over WebGL2.
4. Replace the SDL window and input with the canvas and its events in the `InputSnapshot`
   shape.
5. Load the files the front end needs from the player's folder into the in-memory file
   system.
