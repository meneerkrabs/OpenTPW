# OpenTPW in the browser

Status, October 10, 2026: the **front end, parks and sound run in the browser** over WebGL2 and WebAudio. The lobby with
its islands, the camera glide between them, the original UI (buttons, BF4 fonts, popup help)
and the options screen draw as on the desktop, and mouse and keyboard work. **Online play
works** against an OpenTPW server, which can also host the page itself (docs/SERVER.md):
register, log in and chat were checked in the browser. Starting a park works too: the Lost
Kingdom ran with its rides, guests, economy and build menu at about 97 frames per second in
the interpreter. Sound plays through WebAudio, and saves, options, online files and settings
stay in the browser between visits. Movies and the developer panels are not in the browser yet
(see the end of this page).

Nothing from the game is shipped or uploaded: the player chooses their own game
folder, and the page copies the files needed for the selected start into the runtime's memory.

## Building and running

```sh
dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -o out/web
```

Serve `out/web/wwwroot` over http (WebAssembly needs a server, not `file://`), open
`index.html` and choose the game folder (the one that holds `Data`). No extra .NET workload
is needed: this build runs .NET in interpreter mode. The published page (the server image) is
compiled ahead of time, which runs the game about twice as fast for about 5 MB more download
(docs/BROWSER-PERFORMANCE.md); for that build install the workload once and add the property:

```sh
dotnet workload install wasm-tools
dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -p:RunAOTCompilation=true -o out/web
```

The browser build can also run on CoreCLR instead of Mono (`-p:UseMonoRuntime=false`, .NET 11 only, no workload
needed). It works, with the same texture and simulation hashes, but .NET 11 runs it in the CoreCLR interpreter only and it is
roughly 10 to 30 times slower than the Mono AOT build, so it is an opt-in experiment and not what the published page uses
(docs/BROWSER-PERFORMANCE.md, "CoreCLR on WebAssembly"). Delete `source/OpenTPW.Web/obj` and `bin` when switching between
runtimes, or the next publish fails to link.

An OpenTPW server can serve the published `wwwroot` itself, beside its API
(`OpenTPW:WebClientDirectory`, docs/SERVER.md). The page then offers its own address as the
online server, and everything runs over one HTTPS address.

For local testing without the folder dialog, put a `files.txt` (one path per line, such as
`Data/ui.wad`) next to a copy or link of the game's `Data` folder, serve both from the same
server and open `index.html?data=<that folder's URL>`. A line may give the URL to fetch after
a tab, for servers that only serve known file types (the OpenTPW server does). Serve real
files, not symbolic links: ASP.NET reports a link's own size.

## Measurements (Apple Silicon, Chromium, local server)

| Check | Result |
| --- | --- |
| Published framework payload, Brotli-compressed | 11.8 MB for an interpreter build, 16.5 MB compiled ahead of time (the published page; 13 October); both include 7.7 MB of compressed, predecoded HD interface art |
| Game files copied at start | everything in `Data` except movies, start-up pictures and the theme folders below `levels` (with the speech and global sound banks) |
| Game files copied when a park starts | its theme folder, 122 files and 39 MB for the Lost Kingdom, most of it music |
| MP2 decoding in the interpreter | 60 to 130 times faster than real time; a 17.5 s music segment takes about 280 ms, a short stutter on the page's only thread |
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
  colouring console output. The browser has no `graphics.json`, so it uses the default Enhanced
  texture choice: the HD interface art from `content/hero-art`, prepared at build time and embedded
  (docs/TEXTURE-PACKS.md); a locally built texture pack does not exist there.

  `tools/hero-art` decodes the 55 bundled PNGs with the same StbImageSharp version used by
  the game. The Web build embeds zlib-compressed RGBA instead of those PNGs, avoiding PNG
  decode on the first menu frame. Pixel bytes, including colors behind transparent pixels,
  stay identical. Desktop builds and player texture packs continue to use PNGs. The generator
  runs on each build, rewrites only changed outputs and removes stale outputs under `obj`.

  The internal `OPRG` file holds magic, version, codec, width, height and compressed payload
  length in a 20-byte header, followed by zlib data. The loader checks the outer length,
  bounded dimensions and exact inflated RGBA length. It does not require a canonical zlib
  representation. UI pink and black keying still happen after loading, as on the PNG path.

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

### Game files on demand

The setup screen also offers an explicit **experimental Theme Park Inc** start in Arabian,
Science or Water. Choose the extracted installation folder containing `Data`, then choose
the theme before importing or pressing Play. That theme's complete browser-supported file
set is copied at startup. The existing edition detector checks its identity or structure,
and an edition/theme mismatch refuses before renderer creation. Before deferral,
the complete source list retains the native detector's actual theme-directory witnesses.
A structural Theme Park Inc list refuses the default Theme Park World choice immediately.
Only observed directory names are preserved under the actual copied Data root before
detection; no marker files, media bytes or loaded-theme flags are created for them.
This also preserves mixed-source classification and the unchanged Standard.sam identity
precedence. Up to seven real witness filenames may remain as metadata in the existing
cache manifest when their media payload is excluded; they stay unread, unstored and
unhashed. Secondary planning and cache reopening retain those full-source witnesses. No Theme Park World lobby
is opened for Theme Park Inc. The browser calls the same `GameFlow.StartLevel` route as
desktop `--load-original-level`, including its existing shipped-layout eligibility.
The experimental HUD and capability policy are the desktop slice's: building, guests and money (cash, loans, costs), no
staff, research, challenges, missions, original game save writing, tickets or sharing. OpenTPW's own
layout checkpoint is separate from an original game save.

A failed setup import does not mark the selected theme loaded. Before a retry, only the
runtime-owned `/game` staging tree and import readiness/speech tracking are reset; kept
original files, `/save`, `/online` and `/config` are retained. Play requires every selected
theme entry to be kept; missing or size-mismatched kept data still goes through the existing
folder recovery reader. Already verified individual files can remain kept after a partial
import, but that does not make the theme ready. Once a renderer has been created, choosing
another installation or retrying startup requires a page reload, including after a later
startup exception; this route does not dispose/recreate an active world.

`BrowserStartupTests`, `test_web_startup.mjs` and the originals-store tests cover synthetic
edition refusal, staging isolation, explicit dispatch, complete theme import, retry and
cache reopening. Independent review also executes the actual page module and registered
folder handler with synthetic DOM and runtime-export stubs, covering selected-theme dispatch,
partial-copy retry, refusal before cache work and the unchanged TPW lobby route. The browser
project compiles this route. These are CPU and handle-stub
controls: no fresh original-data, actual browser gameplay, WebGL rendering or original-game
parity qualification is claimed. The existing deferred Theme Park World park-copy failure
behavior is outside this initial Theme Park Inc import path.

The page copies the files the front end needs when the player chooses the folder, and keeps
the theme folders below `levels` (about 10 MB each without sound) for later. Starting, loading
or visiting a park goes through `GameFlow.QueueLevel`, which waits until
`GameFlow.LevelDataReady` says the level is there; the browser answers by asking the page to
copy that theme's folder and shows *Loading the park* while the lobby keeps running. On the
desktop `LevelDataReady` is null and parks start at once. Each theme's saved park
(`Easymode.TPWI`) is copied at start, so the Load list is complete.

### Languages

A game folder copied from the CD keeps the other languages beside `Data`
(`Dutch/data/Language/Dutch`, `Dutch/Meshes/Dutch`, likewise Italian, Polish). For every
top-level folder that holds its own `data/Language/<same name>` (detected by structure, not a
fixed list) the page copies those two trees at once: about 1 to 3 MB per language, so the
Options -> OpenTPW -> Language toggle can list and switch them. Polish is not offered because
its character table is not decoded (docs/LANGUAGES.md).

The advisor's voice is copied when the language is chosen, like a theme folder: the language's
`data/global/speech` (`speechHD.SDT`, 27.5 MB on the real Dutch folder, and `lips.WAD`) and each
level's `speech` folder (0.6 MB), all together so bank and lip marks never mismatch.
`GameFlow.LanguageDataReady` (the counterpart of `LevelDataReady`) asks the page through
`Page.RequestLanguage`; the page shows *Loading the language* and calls `Program.LanguageLoaded`.
A switch in the options waits for it (the choice stays pending, the text changes together with
the voice), a park does not start before the current language's speech is in, and a saved
language is requested at start-up. The cost of one language is that download (the local copy of
27.5 MB plus a short-lived second copy while it is written, about 56 MB more JavaScript heap at
the peak in Chrome), kept in memory for the visit. The installed language needs no request.

A language counts as loaded only when every one of its files is in memory. When a read or a
write fails (a file the page cannot read, a 404 in the `?data=` test mode), the page logs the
error, shows *Could not load the language speech* for ten seconds and reports
`Program.LanguageFailed`; the language stays unready. Text and speech must stay consistent, so
the game does not apply the switch: the pending choice is dropped with a warning (the options
row shows the current language again) and the current language, text and voice, stays. Nothing
waits forever, and a park queued meanwhile starts at once. Choosing the language again copies
again, and only the files that are still missing (the page remembers what it wrote). A pending
choice also holds a park that is started right after it (the park waits for the speech, then the
transition applies the language before the park is built), so the park is entered in the chosen
language. A saved language whose speech cannot be copied at start-up is not used: the game
starts in the default language (the saved choice stays in `language.json`, so the next visit
tries again).

At start the game reads the saved choice from `/config/language.json` (kept in browser storage
like the other settings), offers the CD languages with `LanguageStartup` (the code the desktop
uses) and, when the saved language is no longer in the folder, warns and uses the default.

### Sound

`Browser/WebAudioOutput.cs` answers to the desktop's `SdlMovieAudioOutput` name, so the game's
mixer, speech and movie code use it unchanged. It queues 16-bit stereo PCM to an AudioWorklet
(`opentpw-audio-worklet.js`) that plays it and reports the frames it has taken; that count is
the clock for the mixer and the advisor's lip sync. Browsers start audio only after a click or
key press on the page.

### Game files kept in the browser

The page can keep the player's own game files in the browser's origin-private file system
(OPFS, `opentpw-originals.js`), so a later visit starts with **Play** instead of choosing the
folder. The files stay on that device; OPFS is never uploaded and no other site can read it. The
setup screen chooses how much is kept, and remembers the choice:

- **what the parks use** (the default): the files the start copies, and each theme folder and
  language speech once the game has read it;
- **all at once**: when the folder is chosen, every file the page uses (Data without movies and
  start-up pictures, plus the other languages' text, banners and speech; about 300 MB for an English
  CD with three other languages), before the game starts;
- **no**: nothing; every visit asks for the folder, as before.

A manifest lists every file the page uses with its size and SHA-256, kept or not, so a later visit
rebuilds the file list; a park whose theme was not kept yet asks for the folder once more and keeps
it then. That folder is accepted only when it has the file, every kept file has the same size in it
and up to eight kept files have the same bytes (SHA-256); otherwise it is refused with a message and
can be chosen again. A folder
chosen on the setup screen is read directly and every kept file it meets is compared by its SHA-256;
any difference, whenever it shows, means another installation: everything kept is removed at once
(so files of two installations never mix, even if the start then fails) and the folder's start files
are kept again; in the "all at once" mode a chosen folder is read completely, so every kept file is
compared. Each start attempt drops reads and writes of earlier ones. Limits: in the lazy mode files
the visit never reads are not compared, a folder asked for again is checked by sizes and eight hashes
rather than every byte, and a read that overlaps a replacement may simply not be kept. Writing waits until the game has started (the start's files wait in memory until then)
and runs in the background with at most 64 MB waiting afterwards (one larger file can exceed it);
the manifest is written every 25 files and whenever the queue empties. A page closed before that
keeps less, and the next visit asks for the folder for what is missing. Only one tab keeps files (a Web
Lock); a second tab plays without them. **Remove kept files** deletes them (saves stay). Browsers
without writable OPFS streams, or whose storage does not open, keep nothing and show no choice.

Content hashing succeeds before a kept file is changed. Replacing a previously kept path first
persists it as missing, then writes its new bytes and makes it available after close succeeds;
the next manifest flush publishes the new hash. A failed replacement can therefore temporarily
make that path unavailable until it is kept again, rather than expose new bytes under an old
persisted hash. Initial lazy writes retain their existing checkpoint schedule. Failed manifest
flushes remain retryable. [Cache recovery checks](BROWSER-ORIGINAL-CACHE-RECOVERY.md) use synthetic
storage; they do not establish atomic multi-file OPFS transactions or real-browser behavior.

### Saves and settings

The display choices the browser honours (render scale, interface scale and the frame rate limit)
are kept in `/config/display.json` like the desktop's; the canvas decides the size, so a stored
window size or mode has no effect.

The game writes saves and options to `/save`, online files to `/online` and display and graphics
settings to `/config` (`OPENTPW_CONFIG_DIR`). `opentpw-storage.js` mirrors those folders into
IndexedDB every five seconds and when the page is hidden or closed, and puts them back before the
game starts. Whole-park saves (`opentpw-<theme>-world.json`, docs/PARK-SAVES.md) live in `/save`
too: a park saved in the browser is still on the Load screen after a page or runtime reload, and
resuming it asks for the game folder to be chosen again, because the original files are not kept.
The player's game files are not uploaded.

### Shaders

All eleven shaders in `content/shaders` cross-compile to GLSL ES 3.00 through the existing
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
- **Clicks and key taps within one frame**: the game reads button and key state once per
  frame. A press that starts and ends within one browser frame stays down for that frame and
  is released in the next. Held keys and buttons are released when the canvas loses focus.

### Pitfalls found

- **Trimming removes reflection targets.** The file system creates archive handlers by
  reflection; the browser project roots its own assembly (`TrimmerRootAssembly`), and turns
  reflection-based `System.Text.Json` back on for the settings files.
- **`dotnet.run()` exits the runtime** when `Main` returns; `runMain()` keeps the exports
  callable.
- **`Console.ForegroundColor` throws** in the browser.
- **The browser's HTTP responses only support asynchronous reads**; the online client reads
  them with `ReadAsync`.
- **Browsers cannot set WebSocket headers**, so the chat sends its session token as the first
  frame instead of an `Authorization` header (docs/SERVER.md).
- **The window asks for its size before the device exists**, so the page's canvas is looked up
  when `opentpw-gl.js` loads.

## What the browser build still lacks

- **Movies:** not copied or played yet; sound is in place (see Sound below).
- **Decoding off the frame:** music segments decode on the page's only thread; decoding them in
  pieces across frames would remove the short stutter (the AOT build decodes textures about four
  times faster; the music stutter itself has not been measured again).
- **Developer panels:** they need ImGui, which is a native library.
- **Typing without a keyboard event**: text arrives through key presses on the canvas, so
  input methods and on-screen keyboards that only insert text do not reach the game yet.

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

## Performance

See [browser performance](BROWSER-PERFORMANCE.md) for measured CPU/frame costs,
render binding reuse, cooperative audio decoding and remaining park-load stalls. Its harness
(`tools/web-perf/run.mjs`) opens the page with `?perf=1`, which records the time of every
`Program.Frame` and exposes the measurement hooks of `Program.Perf.cs`; without it the page
behaves as before.
