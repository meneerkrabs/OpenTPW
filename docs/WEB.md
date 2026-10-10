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
