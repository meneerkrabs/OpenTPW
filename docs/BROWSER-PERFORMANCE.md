# Browser performance

OpenTPW shares its game code between native builds and the browser. The browser's
WebAssembly runtime and WebGL bridge run much of that work on the page's main thread.
A task that occupies that thread also postpones drawing and input.

The desktop engine's own CPU profile (simulation and frame, with the changes of 18 October that also remove
garbage the browser pays for) is in [ENGINE-PERFORMANCE.md](ENGINE-PERFORMANCE.md).

## Measuring

`tools/web-perf/run.mjs` repeats the measurements below with one command. Chrome runs with `--mute-audio` (also headed, and for `tools/web-postcard-smoke.mjs`), so measurement runs make no sound; `--audible` lets a human listen. `--music` is unaffected: it switches the game's own music and speech on, and Web Audio still decodes them while the speakers are muted. It serves a published
`wwwroot` and the player's own game folder on 127.0.0.1 (the page's `?data=` route), opens them
in a fresh headless Chrome profile through the DevTools Protocol, drives the game through the
same UI calls as the native front-end smoke test and writes a JSON report plus a Markdown summary.
It needs Node 22 or later and Chrome; it has no dependencies. The game files stay on the machine
and no game data goes into the report.

```sh
dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -o out/web
node tools/web-perf/run.mjs --web out/web/wwwroot --game "/path/to/Theme Park World" --label interpreter --runs 3
```

| Scenario | What it measures |
| --- | --- |
| `start` (always first) | loading until the first frame and the lobby |
| `lobby` | a steady window (`--window`, 30 s) in the lobby |
| `jungle` | the cold start of Full Simulation Jungle (the construction frame) with its load profile, then a steady window |
| `buy` | the first buy-menu open (Aztec Mayhem selected) and the first Belly Bounce, Balloon Shop, Burger Shop and Drinks Shop selections |
| `memory` | wasm memory, managed heap and JS heap every 10 s over `--memory-minutes` |
| `switch` | memory across park changes (Jungle, the lobby, the next island's park, the lobby, Jungle again), each point after a full collection: wasm, live managed memory, live parks, entities, live textures by source, archive and sound bytes, and in the lobby the static fields that hold the most |
| `revisit` | with `--keep lazy` or `--keep all`: the same profile reopened without the served folder; Play, the lobby, then Jungle from the kept files (fails if it asks for the folder) |
| `umbrellaCost` | (after `jungle`) the frame cost of a staged crowd (`--crowd`, default 400 extra guests) in rain, a steady window without and one with an open umbrella over every guest, at the Home and the farthest zoom (`Program.PerfUmbrellaCrowd`, loopback `?perf=1` only; docs/UMBRELLAS.md "Tuning") |
| `winterCost` | (after `jungle`) the frame cost of a staged crowd (`--crowd`, default 400) in rain at the Home zoom, one window per step, each added to the one before: `rain` (a rain-only crowd, no snow: the staging applies weather quality 30 and every step applies it again before its window), `rainSnow` (the settled snow held at `--cover`, default 1), `rainSnowUmbrellas` (an open umbrella over every guest, held open), `rainSnowDressed` (and every guest in warm items) and the same at the farthest zoom (`rainSnowDressedFar`); each window records whether it still rained at its end, and the umbrellas and items drawn. The items' own cost is `rainSnowDressed` minus `rainSnowUmbrellas`. Last it saves a screenshot of a freshly staged dressed crowd in snow without umbrellas (`--shot`, `--zoom` default 70, the camera on one guest chosen by `--follow`, default -250: it skips the 250 newest visible guests, so with the default crowd of 400 it is the 150th guest of the new crowd, which stands in the open away from the entrance roofs; guests are picked by id order, so look at the picture and adjust `--follow` if a run lands under a roof) (`PerfSnow`, `PerfUmbrellas`, `PerfUmbrellasOff`, `PerfWinter`, `PerfRaining`; docs/WINTER.md "Tuning") |
| `digest` | deterministic work that must give the same result in every build, timed: every texture of the theme decoded and hashed, and the fixed park run of the original-data determinism test (seed 2026, 3000 ticks at the fastest speed; economy and guests) reduced to its world state hash. It blocks the page while it runs, so it comes last |

`tools/web-perf/compare.mjs --a <reports> --b <reports>` prints the medians of two builds side by
side with the change per measure (and how many runs a row rests on when some lacked it), and
fails unless every run of both builds has the same digest.

Frame times are the wall time of each exported `Program.Frame`, recorded by the page only with
`?perf=1`. "Main thread busy" is the renderer main thread's task time over wall time
(`Performance.getMetrics`): how much of the time that thread is occupied, which is what postpones
drawing and input. It is not the operating system's CPU time, and whether the October samples
below used the same method is not recorded, so the two are not directly comparable.
Music and speech are muted through the options file and effects stay on, as on 11 October
(`--music` keeps them on); world textures are the default Original (the browser then also carried the redrawn HD interface art, removed on October 18, 2026).
The hooks behind `?perf=1` (`Program.Perf.cs`: state, a click on an element by id, a key, a
buy-window selection, memory counters) change nothing unless the harness calls them. The page
honours `?perf` and `?perfSeed` only on a loopback address, and the seed may only write the
options file and the display settings (`--frame-cap`), so a link to the public page cannot write
into a player's saved files. In perf mode the page asks for the drawn-frame count around every
call, a small extra cost on the main thread that both sides of a comparison share.

**Baseline, 13 October** (interpreter build at `618ad830`, Chrome 155 headless, Apple M5 hardware
Metal through ANGLE, 1280×900; median of three runs of the final harness, range in brackets). This
is a different machine from the M4 numbers below, so compare runs of the harness with each other,
not with them.

| Measure | Value |
| --- | --- |
| Lobby, main thread busy | 16.5 % (9.5–17.0), 60 fps, frame p95 3.8 ms (3.2–4.0) |
| Jungle, main thread busy | 16.6 % (11.8–21.1), 60 fps, frame p95 4.7 ms (4.6–5.1) |
| Jungle cold construction, largest frame | 2,492 ms (2,468–2,549); park ready 2,808 ms after the click |
| Open buy menu / Aztec Mayhem, largest frame | 80.3 ms (80.1–96.1) |
| Belly Bounce | 71.5 ms (62.0–74.2) |
| Balloon Shop | 25.6 ms (25.5–28.6) |
| Burger Shop | 37.1 ms (27.6–38.9) |
| Drinks Shop | 50.7 ms (35.0–55.6) |
| Wasm memory in the park | 230 MB, no growth over 2 minutes |

**How far a single run can be trusted.** Over six runs in two sessions (three with an earlier
version of the harness) the cold construction stayed within ±3 % (2,399–2,549 ms): that is the
measure to decide on (the AOT experiment in the browser plan). Main thread busy and the small
first-use frames varied by up to about ±30 % between sessions on this laptop. Compare builds with
`--runs 3` or more (medians, every run kept in the report) and run them alternately on an idle
machine; a difference smaller than the ranges above is not a result.

## Ahead-of-time compilation (13 October)

The interpreter build against the same sources compiled ahead of time
(`-p:RunAOTCompilation=true`, the `wasm-tools` workload), three runs each taken alternately on the
M5 setup above:

| Measure | Interpreter | AOT | Change |
| --- | ---: | ---: | ---: |
| Start: first frame | 1,914 ms | 1,367 ms | −29 % |
| Lobby: main thread busy | 19.3 % | 10.8 % | −44 % |
| Jungle cold construction, largest frame | 2,540 ms (2,539–2,554) | 1,136 ms (1,132–1,137) | −55 % |
| Jungle: main thread busy | 23.0 % | 14.5 % | −37 % |
| Jungle: frame p95 | 5.1 ms | 3.0 ms | −41 % |
| Open buy menu / Aztec Mayhem | 103.6 ms | 53.5 ms | −48 % |
| Belly Bounce / Balloon / Burger / Drinks Shop | 70 / 34 / 36 / 51 ms | 37 / 20 / 24 / 27 ms | −32 to −48 % |
| Digest: 1,698 Jungle textures decoded | 2,396 ms | 626 ms | −74 % |
| Digest: fixed park run (3000 ticks) | 478 ms | 257 ms | −46 % |
| Wasm memory in the park | 229 MB | 211 MB | −8 % |

The ranges do not overlap. In every run both builds gave the same digest, texture hash
`1CC5AEB033691043` and world state hash `120A09CBAE070E0A`, and the desktop runtime gives the
same two hashes for the same work, as does an AOT build made in the Linux server image. That
covers decoding the theme's 1,698 textures and the fixed park run, which exercises the economy and
the guests but not the rides, object scripts, weather or staff; within that coverage AOT changed
the speed, not the result (the check now covers rides, object scripts, weather and staff as well and runs in CI: docs/DETERMINISM.md, "Browser and native"). These are three runs per build on one machine: they show a large
improvement there, not a speed guarantee for every device.

The cost is the build: the native module grows from 2.9 to 32.6 MB, the Brotli-compressed
framework from 11.8 to 16.5 MB, and a publish takes about 110 s instead of 27 s. The browser
plan's rule (AOT when the cold construction or the first menu frame gets at least 40 % faster,
judged against the size of the original game the player supplies anyway) is met, so the
published page (the server image, `source/OpenTPW.Server/Dockerfile`) is compiled ahead of time.
A local `dotnet publish` stays an interpreter build without a workload (docs/WEB.md).

## CoreCLR on WebAssembly (17 October)

.NET 11 can run the browser build on CoreCLR (`dotnet publish -p:UseMonoRuntime=false`) instead of Mono. It starts and plays,
and the texture hash `1CC5AEB033691043` and the simulation hash `0B45FAA2ED526C78` match the Mono builds in every variant below,
but it is slower than Mono AOT, so Mono stays the default and CoreCLR is an opt-in. Medians of 3 runs, .NET 11
(11.0.100-rc.2.26504.105), Chrome 155 on an Apple M5:

| Measure | Mono AOT | Mono interpreter | CoreCLR, interpreter | CoreCLR + ReadyToRun (`tools/web-coreclr/r2r.mjs`) |
| --- | --- | --- | --- | --- |
| Bundle, Brotli / raw | 19.9 MB / 69.0 MB | 13.3 MB / 29.2 MB | 13.8 MB / 30.9 MB | 29.8 MB / 152.2 MB |
| First frame / lobby | 2376 / 2399 ms | 3323 / 3323 ms | 8657 / 8816 ms | 4501 / 4525 ms |
| Jungle ready | 10206 ms | 13085 ms | 98368 ms | 21442 ms |
| Jungle steady: main thread busy / p95 frame | 9.8 % / 1.6 ms | 23.8 % / 4.4 ms | 100 % / 69.6 ms (7.9 fps) | 96.6 % / 9.1 ms (38 fps) |
| Texture decode (1698 textures) | 555 ms | 2443 ms | 16985 ms | 1590 ms |
| 3000-tick run | 202 ms | 355 ms | 3093 ms | 1385 ms |

What the .NET 11 pages say and what exists in RC2. The "What's new in .NET 11 runtime" page (updated for RC 2) lists, under
CoreCLR on WebAssembly, WebCIL V1 as the default (crossgen2's `WasmObjectWriter` writes it) and a working native re-link. That
is the crossgen2 tool and the loader; it does not announce an SDK switch. In the installed RC2 packs
(`microsoft.net.sdk.webassembly.pack` and `Microsoft.NET.Runtime.WebAssembly.Sdk` 11.0.0-rc.2.26504.105) there is no ReadyToRun
step for browser-wasm: no `PrebuiltR2RDirectory` in `ConvertDllsToWebcil`, no crossgen target, and `PublishReadyToRun=true`
stops with NETSDK1095. The SDK integration is dotnet/runtime#133378 (merged 17 September 2026 into main, milestone 12.0.0;
`ReadyToRunTests.cs` exists on main and not on release/11.0), and the runtime pack carries no prebuilt `native/r2r` images.
So `dotnet publish` gives an interpreter-only CoreCLR build. The crossgen2 package that the workload restores can however target
`--targetos:browser --targetarch:wasm --obj-format:wasm`, and the loader accepts its images if the boot config gives each one
its `tableSize` and its `payloadSize`. `tools/web-coreclr/r2r.mjs` does exactly that on a finished publish (it compiles the trimmed IL
as one version bubble with `--inputbubble --compilebubblegenerics`; the SDK's `--opt-cross-module:*` crashes crossgen2 RC2 on
this project, and composite images are rejected by the runtime), which is the last column. Per assembly bubbles (no
`--inputbubble`) boot too but are barely faster than the interpreter (about 8.6 fps, simulation 2.2 s).

Result: ReadyToRun takes CoreCLR from 98 s to 21 s for Jungle and from 8 to 38 fps, texture decode becomes faster than the Mono
interpreter, but it is still 2 times slower to Jungle, 7 times slower on the simulation, the main thread is saturated (97 % against
10 %) and the download is 50 % larger (the Brotli size of the 152 MB raw payload is 29.8 MB). The rule for switching the published page
(at least as fast as Mono AOT in time to playable and in frame or simulation speed) is not met, so it stays on Mono. The earlier
"never reaches the first frame" finding came from the measuring harness waiting at the Game Mode screen, not from the runtime.
Revisit with .NET 12 (the SDK integration, the runtime's own performance work in dotnet/runtime#124218, and probably a working
`--opt-cross-module`).

## Frame cap (13 October)

The frame rate limit on the OpenTPW options page (`DisplaySettings.FrameCap`: off by default, 30
or 60 fps) caps drawing, not the simulation: `FrameLimiter` books frames on a grid of 1/cap
seconds with a quarter interval of slack for timing jitter, the browser skips an animation frame
that comes too early and the desktop sleeps until the next one is due, and the simulation's fixed
clock (FixedStepClock) takes the elapsed time in larger steps. The browser now keeps its display
settings in `/config` like the desktop, so the choice survives a reload.

Interpreter build, two runs per setting taken alternately on the M5 setup (the harness's
`--frame-cap`; frames are counted only when the renderer drew one):

| Cap | Frames drawn | Calls skipped (30 s) | Main thread busy, lobby / Jungle |
| --- | ---: | ---: | ---: |
| Off | 60 fps | 0 | 17.5 % / 22.6 % |
| 30 | 30.0 fps | about 900 | 10.0 % / 12.9 % (−43 %) |
| 60 | 59.96–59.99 fps | 0–1 | 17.1 % / 17.2 % |

In these samples a 30 fps cap took 43 % off the main thread rather than half; the likely reason
(not measured apart) is that only the drawing halves, while the simulation keeps its 60 ticks a
second and the page still runs one animation-frame callback per display frame. A cap at the
display rate drew practically every frame. Input is read once per drawn frame, so a 30 fps cap
can add about one display frame of input delay; that is why it stays off by default.

## Where the cold park start goes (13 October)

`LoadProfile` (`source/OpenTPW.Common/LoadProfile.cs`) times named sections of the park start when
the harness turns it on (`PerfProfile`): the start's steps in `GameFlow.StartLevel` and the `Level`
constructor, and the building blocks texture decoding, texture creation, model parsing and WAD
reads. Off, a section costs a thread-id read and a comparison. Sections nest, so a parent includes
its children. The harness reads the profile when the park is ready and again five seconds later; for
cold Jungle the two are identical, so all of this happens during the start. Two runs of the AOT
build on the M5 setup (interpreter in brackets, one run):

| Section | AOT, ms | Within it |
| --- | ---: | --- |
| Whole start (`start-level`) | 1,010–1,047 (2,338) | |
| Objects placed | 250–256 (386) | |
| Original park read | 186 (297) | object catalogue 121 |
| Guests | 151 (222) | kid sprite sets 107–108 |
| Park audio started | 131–133 | |
| Economy | 76 (132) | |
| Terrain | 75–78 (257) | |
| Texture decoding, 90 textures | 61–64 (243) | spread over the sections above |
| Model parsing, 14 models | 62–64 (102) | spread over the sections above |
| Texture creation, 129 / WAD reads, 295 | 5 / 2–3 | |

In these runs decoding was about 6 % of the AOT start (10 % in the interpreter), so a cache of
decoded textures could save at most about 60 ms of a Jungle start, less the time to read it back;
the browser plan's phase 3 therefore waits. The larger shares are the object catalogue, the kid sprite sets, starting the park
audio and the object placement: those, or spreading the start over frames, are the next targets.

## Building a park over frames (13 October)

Until now a park was built in one frame: `GameFlow.StartLevel` tore the lobby down and constructed
the level in a single call. That took about 550 ms on the AOT build (the largest frame of the cold
Jungle start), and the page stood still for all of it. The audio mixer keeps only about 93 ms queued,
so the lobby music stopped too. Phase 5 of the browser plan spreads that work over frames:

- **The construction is a sequence of steps.** `Level`'s constructor body is an iterator: the original
  park's files, the economy, the terrain, the gate sign, the object catalogue one archive at a time,
  the objects one at a time, the coasters, the guests, then the paths and the rest. The steps and
  their order are the same either way. The constructor runs them all at once, so tests, smoke tests,
  saved-park resumes and the determinism run are unchanged.
- **The lobby keeps running.** A park chosen in the front end, loaded from the load screen or visited
  online is staged beside the live scene with `EntityConstructionScope`, which can now pause between
  frames. Each frame builds for up to 12 ms, then the lobby (or the park being left, with its
  simulation paused) runs its own frame. Input waits, and a progress line ("Building the park...",
  in all six languages) shows at the top. When the construction is complete, the live scene is
  retired and the park entered in the frame after (`[EXT:park-build-over-frames]`).
- **The result is the same park.** The native front-end smoke test builds the shipped Jungle save
  both ways. The world state hash, the entities and the objects are equal (hash `D810E5600FC911BA`,
  103 entities, 14 objects). A transition queued during a build replaces it; the smoke test checks
  that the lobby's entities and materials are then untouched.
- **The harness** reports the frames over 50 ms of the cold start and the construction time of each
  build frame (`PerfParkBuild`). `LoadProfile` leaves the time between build frames out of its
  sections, so the load profile still counts only the work.

Three alternating runs against main (AOT, M5 setup, medians):

| Measure | main | over frames | Change |
| --- | ---: | ---: | ---: |
| Jungle cold: largest frame | 509 ms | 107 ms | −79 % |
| Jungle cold: frames of 50 ms or more | 1 | 1–2 | |
| Jungle cold: ready | 717 ms | 832 ms | +16 % |
| Construction frames | 1 | 22–23 (12 ms each, a few steps 25–54 ms) | |

The digest hashes are identical in every run. The steady lobby and park windows are unchanged:
in the runs without other load on the machine their frame p95 was 0.6–0.7 against 0.6 ms (lobby)
and 0.8–0.9 against 0.9 ms (Jungle). A review running alongside the later runs raised both builds
alike, so their medians are left out. With a 10 ms budget an earlier set gave 584 → 140 ms and a
ready time 29 % later. The park is ready later because the work now waits for frames; the lobby
goes on drawing and playing meanwhile. Two costs remain:

- **The first park frame**, about 100 ms. It is no longer construction: the automatic advisor
  starting its welcome (about 45 ms, loading the speech and its lip sync), the first simulation step
  and the first draw.
- **A few steps of 25–54 ms:** the first fixed item's model and scripts, the gate sign's text, the
  terrain, and the last step that connects guests, staff and the economy.

## Files read on demand, and the textures that stayed (13 October)

The second part of the memory phase closed the three items left open below:

- **Whole files in memory.** A WAD archive opened from a path keeps its directory and reads a member
  from the file when it is asked for (the file is opened for that read and closed again, so it can
  still be replaced, also on Windows); an SDT sound bank read from a file keeps its offset table and
  reads one entry at a time. A file that changed length since its directory was read gives a clear
  error instead of other bytes, and a closed archive refuses reads. Both used to hold the whole
  file (about 29 MB of archives and 35 MB of banks in a park), which the browser already holds.
  `OnDemandReadTests` compares every member of every original WAD and every entry of every original
  SDT bank, read both ways, byte for byte, and damaged banks behave the same.
  Reopened WAD reads check the existing captured-length invariant before allocating the
  requested payload buffer. Bounded 16 MiB synthetic requests after physical growth or
  truncation verify that refusal stays small; an unchanged short source still allows the
  large request with zero padding. Zero-length and beyond-original-end direct requests
  keep their existing no-reopen shortcuts, and cached members remain usable after archive
  disposal. This checks length only: same-length replacement is not identified. No new
  archive size cap, directory policy, eager caching or original-data qualification.
- **Textures that stayed after their park.** Tracking where textures made from bytes come from
  (`PerfTrackTextureOrigins`) found three makers: `Texture.Missing` made a new texture on every call
  (now one shared texture), and the coaster palette and the park gate's own sign were never released
  (now `Texture.ReleaseOwned` when their entity goes, together with the material each made for them).
- **The mixer's growth.** It was the sound banks: with entries read on demand the mixer's share stays
  about 11 MB across park changes.

| Step | Live managed before | After | Wasm before → after | Textures without a path |
| --- | ---: | ---: | --- | ---: |
| Jungle | 94 MB | 41 MB | 254 → 176 MB | 20 |
| Lobby | 47 MB | 24 MB | 254 → 176 MB | 1 (25 before) |
| Fantasy | 109 MB | 48 MB | 305 → 254 MB | 20 |
| Lobby again | 58 MB | 28 MB | 305 → 254 MB | 1 (25 before, growing) |
| Jungle again | 113 MB | 51 MB | 305 → 254 MB | 20 |

What still grows a little (24 → 28 MB in the lobby) is the cache of each theme visited for the first
time: its textures stay cached by path (Fantasy's 57), and so does its part of the object catalogue,
which makes a revisit faster. Visiting a park of a theme seen before adds nothing (Jungle again holds
the same textures); with all themes visited the cache is at its largest, and a limit on it is not
part of this phase.
Three alternating runs against the build without this change: the digest's hashes are unchanged,
the cold Jungle frame is the same (625 against 586 ms, medians of four), and the page's wasm memory in
the park is 31 % lower.

## Memory across park changes (13 October)

The `switch` scenario takes each point after a full collection, so the managed figure is live
memory. On the AOT build (M5 setup) it found two leaks:

- **The first park stayed alive for the session.** `RootPanel` (an entity) stored the first one ever
  made in a static `Instance` that nothing used; through it the first park's `Level` and everything it
  held stayed reachable. The diagnostic `PerfStaleLevelPaths` (a walk from the static fields to any
  park but the current one) named the path. The field is gone.
- **Each park left its sound banks behind.** The decoded-sample cache is keyed by sound category; after
  a park the old categories, with their whole sound banks, stayed reachable through it.
  `SoundEventSystem.Forget` now drops a park's and the lobby's categories when they end, and a decode
  that finishes later does not put them back. `PerfMemoryRoots` (bytes of arrays per static field)
  showed the mixer's share.

| Step | Live managed before | After | Live parks before → after |
| --- | ---: | ---: | --- |
| Jungle | 94 MB | 94 MB | 1 → 1 |
| Lobby | 67 MB | 47 MB | 1 → 0 |
| Fantasy | 124 MB | 109 MB | 2 → 1 |
| Lobby again | 81 MB | 58 MB | 1 → 0 |
| Jungle again | 132 MB | 113 MB | 2 → 1 |

These are one run of the scenario on one machine. Still open, and larger: the textures made at run
time without a path (signs, object textures, colour blocks) stay in `Asset.All` and on the GPU after
their park (textures held 265 → 251 → 333 → 319 → 345 over the scenario: Fantasy's own textures stay
cached by path, and about a dozen path-less ones remain per park); the archive files (22–29 MB read
so far; archives are never closed) and the sound banks (about 35 MB) are kept whole in managed memory
although the browser already holds the files; and the mixer's share still grows by about 6 MB a park.
The page's wasm memory peaks at 305 MB after the second park and does not shrink.

## Three start costs removed (13 October)

Splitting the profile's large sections further showed that two of them were not the work their
names suggest:

- **Opening archive members.** `Refpack.Decompress` (WAD members) found its command classes by
  reflecting over the whole assembly on every call and built the output as a list of bytes with an
  array per byte read: 269 ms for 426 members in the Jungle start. It is now one pass into a growing
  array; `RefpackTests` compares it with the former decoder (kept as `LegacyRefpack`) on crafted
  streams, 4,000 random streams and (with the game files) every compressed WAD member of the original
  game folder, byte for byte.
  The kid sprite sets (108 → 16 ms) and most of the object catalogue gained from this.
- **Path lookups.** Every read listed the directories along its path twice (the case correction and
  the archive search): 113 ms for 461 lookups. The game data's file system now remembers lookups
  (`BaseFileSystem.CacheLookups`; an answer found before an invalidation is not kept); the browser's `AddFile` forgets them when it copies
  a theme folder, and writes through the file system forget them too. Saves keep the plain lookups.
- **Placed ambients.** The land's ambient sounds were decoded in the park's first frame (116 ms).
  `SoundEventSystem.PlayAtWhenDecoded` draws the sound now and starts the voice when its sample is
  decoded (cooperatively in the browser, on a worker on the desktop).

AOT builds, three runs each taken alternately on the M5 setup (`compare.mjs`):

| Measure | Before | After | Change |
| --- | ---: | ---: | ---: |
| Jungle cold, largest frame | 1,143 ms | 520 ms | −55 % |
| Jungle cold, ready after the click | 1,421 ms | 804 ms | −43 % |
| Whole start (`start-level`) | 992–1,083 ms | 431–451 ms | about −58 % |
| Start, first frame | 1,603 ms | 1,131 ms | −29 % |
| Open buy menu / first shop selections | 64 / 18–38 ms | 42 / 13–23 ms | −29 to −45 % |
| Digest: texture decoding / fixed park run | 644 / 261 ms | 481 / 135 ms | −25 / −48 % |
| Wasm memory in the park | 212 MB | 254 MB | +20 % |

The digest's hashes are unchanged in every run. Live managed memory is the same (102 MB) and the
start allocates less (555 against 736 MB in all), but with far fewer small allocations the garbage
collector runs less often (16 against 57 gen0 collections), the heap grows further between them,
and a page's WebAssembly memory never shrinks: its peak is 42 MB higher. The memory phase of the
plan starts from that.

## Game files kept in the browser (13 October)

With the game files kept in OPFS (docs/WEB.md), interpreter build, M5 setup; the harness's
`--keep` and its `revisit` scenario (the same profile reopened without the served folder):

| Measure | Value |
| --- | --- |
| First visit, first frame, without keeping / keeping what the parks use | 1,974 / about 1,990 ms (medians of three alternating runs; within the noise) |
| First visit, Jungle ready | 2,880 / 2,931 ms |
| Revisit, Play button shown after loading the page | 160–170 ms |
| Revisit, lobby after Play | 1,630–1,700 ms (97 MB kept: the start and Jungle) |
| Revisit, Jungle ready from the kept files | 2,800–2,870 ms |
| All at once: first visit, first frame / revisit lobby | 3,406–4,658 ms (307 MB kept before the start) / 1,709–1,752 ms |

Writing first ran during the copy and slowed the first start by about a quarter; it now waits
until the game has started.

## Measured workload

Two matched 30-second samples on 11 October 2026 used headless Chrome, Apple M4
hardware Metal through ANGLE, a 1280×900 canvas, Original world textures and the
redrawn HD interface art that the build then bundled (removed on October 18, 2026). Jungle used a stationary camera, music and speech muted through
the UI, effects on and no world-texture downloads (the browser build has none). Muting does not itself stop audio decoding.

| Phase | v0.7.6 main-thread CPU | Optimized prototype | Reduction |
| --- | --- | --- | --- |
| Lobby | 27.8% of one core | 22.0% of one core | 20.8% |
| Jungle | 41.1% of one core | 29.4% of one core | 28.4% |

The Jungle frame-callback 95th percentile fell from 7.3 to 5.8 ms. The old sample had
two 286/279 ms main-thread tasks outside the render callback; the optimized steady
sample had no tasks of at least 50 ms. Its largest frame gap was still 40.4 ms, compared
with 292.8 ms before. Terrain, rides, guests and HUD remained visible and animated.
These figures measure the page's main thread, not all Chrome processes or GPU time,
and do not promise the same numbers on another device.

## First build-menu selections

On 12 October, the first selection of an attraction or shop stalled a browser frame
for 422–1,011 ms. Profiling traced most of that time to LZSS texture decompression:
each backreference read its entire lookback window, up to 4,095 bytes, even for a
short copy. The decoder now copies forward directly into a growing output array,
including overlapping references. Texture scratch storage is quadratic rather than
cubic, decoded planes use arrays, and previews resolve each used material once and
reuse parsed models. Unused sign materials still do no work.

Matched cold selections used the same original files, Full Simulation Jungle,
Original world textures, the then-bundled HD interface (removed on October 18, 2026) and a 1280×800 browser viewport. These numbers
measure wall time inside the actual exported `Program.Frame`, not network loading,
GPU time or overall FPS. The baseline is deployed v0.7.12; the after column is the
optimized local build.

| First action | Before | After |
| --- | ---: | ---: |
| Open buy menu / Aztec Mayhem | 710 ms | 172 ms |
| Belly Bounce | 478 ms | 75 ms |
| Balloon Shop | 422 ms | 42 ms |
| Burger Shop | 649 ms | 35 ms |
| Drinks Shop | 1,011 ms | 42 ms |

Selections are substantially shorter, but first-use work remains: opening the menu
still takes about 172 ms in this sample, and Belly Bounce takes 75 ms. Warm preview
frames remain around 4–6 ms. Device and runtime differences affect these timings.
The complete original texture corpus retains identical pixels: 8,412 successful
RGBA hashes and the same four malformed-file errors, plus 31 synthetic cases.

The follow-up on 13 October reduced the same first menu frame from about 165 ms
in deployed v0.7.13 to about 94–96 ms in repeated local candidate runs. Bundled HUD PNGs are decoded at
build time and stored as compressed, exact RGBA; sign rasterization scans an ordered
active-edge window without changing coverage. The existing park-file copy wait also
prepares only shared buy-menu image resources, with a 6 ms budget checked between
images. One indivisible image can exceed the remaining budget. Previews and signs
still load on request. All 55 bundled images and all 75 signed previews retain
their original pixel hashes.

Preparation uses the normal image cache and invalidation. A transition with no file
wait keeps the usual lazy loading fallback, so this timing is not a guarantee for
every first-open situation. It caches menu art earlier, including button states,
even if the menu is never opened. The compressed image representation adds about
1.7 MB to the Web module; desktop builds retain PNGs. In the measured wait, no extra
frame exceeded 20 ms apart from the existing initial scene construction. The
remaining first-preview work is still visible; this does not claim a 16 ms cold frame.

## Changes

- Model uniforms are recorded in draw order in the existing frame command list.
  Jungle's WebGL execute calls fell from 86 to one per frame. Uniform values are still
  copied before their draw; batching does not combine differently transformed objects.
- Material resource sets are reused while their native binding identities stay the
  same. A texture replacement or shader-layout change invalidates the cache. Retiring
  model buffers clears cached sets while preserving shared materials.
- UI texture names use a cached case-insensitive stem index rather than repeated
  filename scans. A 1,000-lookup regression measured zero warm query allocations,
  compared with 5,320,000 bytes in the former scan, with first-match behavior preserved.
- Archive parsing no longer forces a full garbage collection for every member. The
  48 KB global lip-sync archive has 639 members; its read used to force 639 collections.
  Response 587 lip-sync loading fell from 648.9 to 1.2 ms, and complete preparation from
  732.7 to 87.9 ms, without changing archive contents or lip-sync marks.
- Browser sentence audio decodes four MPEG frames per batch and yields through a timer
  task. Long sentence PCM has an 8 MiB LRU retention budget, so repeated music segments
  can reuse decoded PCM. This budget covers retained long samples; current/prefetched
  working arrays and the existing short-sample cache are separate. The native decoder
  keeps its background task. Synchronous and cooperative output is bit-identical.

## Transfer and memory (October 19, 2026)

Measured with the published AOT page (`dotnet publish -c Release -p:RunAOTCompilation=true`), served by the real `OpenTPW.Server` (so its precompressed copies, ETags and headers are what the browser sees), Chrome 155 headless and muted, Apple M5, 1280 x 900, the game folder from the harness. `tools/web-perf/run.mjs --upstream http://127.0.0.1:<port> --scenarios start,jungle,switch,reload,transfer` forwards every request but `/game/` to that server and logs it (path, status, Content-Encoding, Cache-Control, ETag, conditional or not, bytes sent); `reload` loads the page again in the same profile and `transfer` prints the log by scenario. `--track-origins`, `--gc <params>` and the counters below are diagnostics; the harness reads them only when asked.

### Transfer

A cold page load plus a Jungle park (the game's own files come from the player's folder and are not counted): 105 requests and 20.2 MB sent before, 104 and 17.4 MB after. Everything is listed in the table; "raw" is the size on disk, "sent" the bytes on the wire.

| Files | Count | Raw MB | Sent MB before | Sent MB after | Content-Encoding | Cache-Control after |
| --- | ---: | ---: | ---: | ---: | --- | --- |
| `dotnet.native.<hash>.wasm` (the runtime, AOT code) | 1 | 42.6 | 8.49 | 8.47 | br | immutable, 1 year |
| `OpenTPW.Web.<hash>.wasm` (the game) | 1 | 12.4 | 2.22 | 2.22 | br | immutable |
| 38 other assemblies (`.wasm`) | 38 | 5.0 | 1.29 | 1.29 | br | immutable |
| ICU data (`icudt_no_CJK`) | 1 | 1.2 | 0.23 | 0.23 | br | immutable |
| `dotnet.js` and the two fingerprinted runtime scripts | 3 | 0.4 | 0.10 | 0.10 | br | `dotnet.js` no-cache, the others immutable |
| page scripts (`main.js`, `opentpw-*.js`), `index.html`, `style.css` | 12 | 0.1 | 0.02 | 0.02 | br | no-cache (ETag, 304) |
| pools sprites `.TPC` (Jungle, with the pools extension on: the swim and land sets) | 24 | 7.9 | **7.86** | **5.02** | none before, br after | **no-cache before**, immutable after (`?v=<hash>`) |
| pools sprites `.ESP` (350 bytes each) | 24 | 0.01 | 0.01 | 0.01 | none | no-cache before, immutable after |
| `/favicon.ico` (404) | 1 | | 0 | gone | | |
| Total | 105 / 104 | 69.5 | 20.2 | 17.4 | | |

What was wrong:

- **The sprite files went out uncompressed.** The SDK's publish step stores `.br` and `.gz` copies only for the extensions it knows to be compressible (js, css, json, wasm, dat, html and a few more); `.TPC` is not on its list, and Caddy's `encode` does not touch `application/octet-stream`. The 8.2 MB of sprite files (24 of them for a Jungle visit) are 36 % smaller with Brotli (5.3 MB for all 48): `OpenTPW.Web.csproj` adds `**/*.TPC` to `CompressionIncludePatterns`. The 350-byte `.ESP` files stay as they are.
- **Every visit asked again for all 48 sprite files** (`no-cache`, 304 each; the 61 requests of a reload, 48 of them for sprites). The page already puts the content hash in the address (`?v=` plus the first 16 hex digits of the pinned SHA-256, `opentpw-pool-sprites.js`); the server now serves `/pools-sprites/...?v=<16 hex digits>` as `public, max-age=31536000, immutable`. Without the hash, or with another query, the file is revalidated as before. A changed sprite file is a new address; the game keeps a file only when the manifest pins its SHA-256 in any case. A reload now makes 13 requests (the page, its ten scripts, `dotnet.js` and the stylesheet, all 304 with an empty body) instead of 61.
- **A browser without Brotli made Caddy compress on the fly.** The server only knew `.br`; for `Accept-Encoding: gzip` Caddy gzipped the 44.6 MB runtime again on every request (15 MB out), and sent the 1.2 MB ICU data as it was. The SDK stores `.gz` copies too, so `WebClient` now sends the `.gz` copy to a browser that lists gzip but not Brotli (`br;q=0` counts as not accepted). Checked against Caddy 2.11.7 with the production Caddyfile (`encode zstd gzip`, run locally): a response that already has a `Content-Encoding` passes through untouched, so there is no double compression of `.br`; zstd is never used (Chrome offers it, the server prefers Brotli).
- **The page asked for `/favicon.ico` on every visit** (a 404). `index.html` now declares `<link rel="icon" href="data:,">`.

Checked and left as it is: the SDK's Brotli is already level 11 with the 4 MB window (`lgwin` 22: the same 8,899,876 bytes for the runtime as a local re-run); window 24 would save 138 KB (1.5 %). The scripts of the page (`main.js` and its modules) stay revalidated with ETag and 304: they have no fingerprint in their names, and giving them one (the SDK's import map) needs an inline script, which the page's content security policy forbids.

### Memory

Jungle in the browser (the `switch` scenario: Jungle, lobby, Jungle, lobby, Jungle, each point after a full collection; the second park is Jungle again because the next island is locked). Counters added for this: `gpuTextureBytes` and `gpuTextures` (the live WebGL textures and renderbuffers, from their size and mip chain), `PerfTextureBytes` (distinct textures by path group and the largest ones), `PerfAssetBytes` (array bytes behind `Asset.All` by type, and the shaders by file), committed and fragmented GC bytes, and the renderer process's resident memory (`ps`).

| Step | Live managed MB before / after | Shader assets before / after | Renderer resident MB before / after | Wasm MB before / after | GPU textures MB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Jungle | 72 / 61 | 236 / 90 | 755 / 619 | 214 / 214 | 149 |
| Lobby | 59 / 45 | 357 / 195 | 789 / 663 | 214 / 214 | 82 |
| Jungle again (park 2) | 80 / 62 | 398 / 90 | 1,121 / 852 | 445 / 371 | 150 |
| Lobby again | 73 / 52 | 519 / 195 | 1,123 / 861 | 445 / 371 | 82 |
| Jungle once more | 86 / 62 | 560 / 90 | 905 / 842 | 445 / 371 | 150 |

Texture digest hash `845CD7FD1E465A18` and fixed-park simulation hash `ECB0C5BC6BFC46B9` are the same in both builds. (One run each, one machine: the wasm and resident figures depend on when the runtime's allocator grew.)

Where the memory goes in a Jungle park (after): the wasm memory is 214 MB: 61 MB live managed objects, the rest the runtime (the 13 MB game assembly and the others are loaded into it), the allocator and its free space after the build's garbage (the build allocates about 590 MB). Wasm memory never shrinks; its peak after the second park is 371 MB. GC parameters do not change it: `MONO_GC_PARAMS` `soft-heap-limit=128m` gave 214 MB and `nursery-size=1m` 209 MB (against 215), and `major=marksweep-conc` hangs the page. The JS heap is 16-23 MB. The GPU holds 149 MB in Jungle (by estimate from size and mip chain; 82 MB in the lobby): about 46 MB of that are the render targets at 1280 x 900 (4x multisampled colour and depth, the resolve texture and the swap chain; they scale with the canvas size, so a high-DPI window costs several times as much), the 38 textures made from bytes are 69 MB (the guest, staff, particle and sign atlases, mostly 1024 x 1024 and 1024 x 512), and the 137 + 81 + 42 textures loaded by path 14 MB.

What was changed:

- **Lobby scene and terrain never released their materials.** Every part of the lobby scene (`OriginalModelScene`) and of a park's terrain (`OriginalTerrain`) makes its own `Material`, and a material makes its own `Shader`, so each carries a copy of the 15-30 KB shader source, a pipeline, a uniform buffer and layouts. Deleting the scene or terrain released the vertex and index buffers only. Each lobby visit left about 160 materials and shaders in `Asset.All` (`test.shader` 308 -> 470 in the lobby, 168 -> 330 in the park; the harness's `(shaders by file)` list), 5-6 MB of managed memory and the GPU objects with them. They now go with their owner (`OriginalObject` already did); the terrain's path batches keep one shared material per texture set until the terrain goes, and the queue models' own materials go when the path layer is rebuilt (each path edit made new ones). Releasing a material frees only what it owns (shader copy, pipeline, uniform buffer, layouts, its binding tables); it never touches a texture, so `Texture.Missing` and the textures cached by path stay live. The native front-end smoke test checks this (the count of `test.shader` copies is the same before and after a park, and no live texture lost its GPU texture, right after the exit and a few frames later); without the fix it fails (576 before, 745 after in a run of several visits). Live managed memory no longer grows from visit to visit (73 -> 86 MB became 52 -> 62 MB).
- **Equal shader sources share one string** in the browser's Veldrid layer (a park holds about 90-170 shaders over eight distinct sources).
- **The particle atlas dropped its CPU pixels after the upload** (`ParticleSpriteAtlas.ReleasePixels`, as the guest atlases already did): the park's renderer and the lobby's each held one for their lifetime.

The answers to the questions asked:

- **(a) CPU pixel copies after the upload.** The web Veldrid layer keeps none (`UploadTexture` copies to JavaScript for the call and drops it); `Texture` keeps none; the guest, staff and balloon atlases are released after the upload (`GuestRenderer.LoadAtlas`); the particle atlas was the one that was not (fixed above). `FontAtlas.Alpha` (the glyph coverage, a few hundred KB per font) and the sign textures' byte arrays stay; they are small.
- **(b) Texture formats.** Every texture is RGBA8 with a full mip chain. The sources are not 16-bit: a WCT decodes from a wavelet YCbCr code through floats into 8-bit RGBA, and the sprite banks are 8-bit paletted (the guest atlas bank keeps one byte per pixel). A census of all 8,412 WCT textures of the original data: 5,886 are fully opaque, but only 34 are exactly representable in RGB565, 32 in RGBA4444 and 32 in RGB5A1, and even for those the sampled value is `c / 31` in WebGL against `c8 / 255` now, and the mip levels would be averaged in 16 bits. So uploading as RGB565, RGBA5551 or RGBA4444 is not lossless for any real texture, and was not done. An opaque texture as RGB8 saves nothing: ANGLE on Metal stores it as RGBA8.
- **(c) Audio.** The decoded PCM kept in memory is 8 MB while Jungle starts (the music sentences, capped at 8 MiB by an LRU) and 4 MB afterwards, and `SoundEventSystem.Forget` drops a park's samples when it ends. Keeping MP2 and decoding on demand would save at most 4-8 MB and cost decoding on the main thread for every effect; not done.
- **(d) Per-theme texture cache.** Textures loaded by path stay cached for the session. A theme's own textures are small: Jungle's 81 `levels/jungle` textures are 6.8 MB of GPU memory with mips and the lobby terrain's 137 are 7.1 MB, so visiting all four themes adds about 20 MB. A limit would need reference counting of path textures; recommended only if a device with little memory needs it.
- **(e) Other managed caches.** Largest roots in the lobby: `AudioMixer` 18.7 MB (23.3 MB after a second park: about 4.6 MB more per park visit, not found yet; it is not the decoded samples, which stay at 4 MB), `Global.Render` 1.9 MB (10 MB before the leak fix), `SignTextRenderer.shared` 1.6 MB. Archive members read on demand are 13 MB.

Recommendations (not done, with their trade-offs):

- **GPU block compression (S3TC/BC, ETC2, ASTC).** About 4x (BC1/ETC2 RGB) or 4x-8x less texture memory, lossy on top of the wavelet decode, and needs an encoder at load time (a CPU cost on the main thread that the browser cannot spare) or a precompressed pack (data the project must not ship). Not recommended while the textures are about 14 MB by path and 69 MB of atlases.
- **A 1x render path.** The multisampled colour and depth are about 37 MB at 1280 x 900 and 4x that at 2560 x 1800; the GPU cost follows the canvas size. A lower sample count or a render scale on high-DPI screens would help most devices with little memory, at a visible cost in edge quality.
- **Merging the resolve texture and the swap-chain texture** (two full-size 1x surfaces: 9 MB at 1280 x 900) would need a change in `Renderer.cs`, which is shared with the desktop build and with the frame-hitch work.
- **Palette textures for the sprite atlases** (one byte per pixel instead of four) would cut about 50 MB of the 69 MB, but linear filtering and mip levels do not work on indices; it would need a shader that filters after the lookup.
- **Pooling the decode buffers** (`TextureFile` allocates four planes of `size * size` floats for each texture) would lower the allocator's peak, which sets the wasm memory; it touches the decoder and the decode scheduling, so it is left to the work on frame hitches.
- **The wasm memory after the second park** (371 MB against 214) is the allocator's peak and does not respond to GC parameters; the lever is less garbage in the build, not collection settings.

## Remaining stalls

In the earlier 11 October sample, after removing archive collections, cold Jungle
construction still occupied one callback for roughly 16.5 seconds (17.9 before).
The 12 October build-selection run measured 3.2 seconds for its initial Jungle
construction frame, versus 10.8 seconds in its v0.7.12 baseline; loading the original
folder and copying theme files are outside that frame. Advisor response 587
prepared in 87.9 ms, including 76.6 ms synchronous PCM decode; the first response
measured 223.5 ms of PCM decoding. Those first-use costs are outside the matched steady
windows and are not claimed as eliminated. Occasional smaller frame gaps remain as well. Broad visibility culling
and full scene-material lifetime work are separate optimization opportunities; ahead-of-time
compilation has since halved the cold construction (see above).

## Verification

The focused original-data suite passed 101 tests; the general CPU suite passed 1,424,
with 348 fixture/platform skips. After the archive change, the full original-data suite
passed 1,681 tests with 94 optional/platform skips in 55 seconds; the earlier v0.7.5
run took 28 minutes 54 seconds. Archive metadata, lazy data caching, response 587 PCM
and its nine lip-sync marks are covered. Web publish passed. A 601-frame native frontend smoke
checked two distinct shared-material transforms by GPU pixel readback, outside-frame
uniform uploads, stable resource-set reuse, native texture replacement, shader rebuild,
shared-owner retirement and deferred disposal, along with the existing park/menu tests.

See [browser setup and limitations](WEB.md), [audio](AUDIO.md) and
[texture packs](TEXTURE-PACKS.md).


## Wide zoom (October 18, 2026)

The wide zoom option lets the park camera go out to the zoom value 300 (UI.md). `tools/web-perf/run.mjs --scenarios jungle --window 20 --zoom 300` measures a second steady window with the
park camera held at that zoom value (hook `PerfZoom`); the control run with `--zoom 110` shows what the second window costs by itself (the first window follows the park's cold start). Published build of this branch (`dotnet publish -c Release`), Chrome 155 headless, Apple M5 through ANGLE/Metal, 1280 x 900, Jungle, 20 s windows, one run each:

| Window | Main thread busy | fps | Frame p95 | Largest gap | Long tasks |
| --- | ---: | ---: | ---: | ---: | ---: |
| First steady window, zoom 110 (zoom 300 run) | 18.9 % | 59.77 | 3.6 ms | 62.1 ms | 0 |
| Second window, zoom 300 | 12.7 % | 59.97 | 2.6 ms | 20.2 ms | 0 |
| First steady window, zoom 110 (control run) | 17.3 % | 59.94 | 3.2 ms | 34.2 ms | 0 |
| Second window, zoom 110 (control) | 11.4 % | 59.99 | 2.4 ms | 19.4 ms | 0 |

`tools/web-perf/run.mjs --scenarios jungle,umbrellas --zoom 80 --shot umbrellas.png` is a capture aid for the umbrellas extension (docs/UMBRELLAS.md): after the Jungle start it calls the harness hook `PerfUmbrellas`, which gives every guest an open umbrella and keeps the camera on the first visible guest, waits until the renderer draws one (a new park has no guests for a while) and saves a screenshot. It is the browser twin of the desktop's Debug-build `--debug-umbrellas`, not a measurement; the guests' fields are written directly, so nothing from such a run may be saved.

`tools/web-perf/run.mjs --scenarios jungle,winter --zoom 70 --shot winter.png` is the same capture aid for the winter extension (docs/WINTER.md): the harness hook `PerfWinter` dresses every guest in warm items (a head item by the guest's id, mittens on three guests in four), keeps the camera on the first visible guest, waits until the renderer draws an item and saves a screenshot after ten more seconds, so the followed guest has left the entrance. Not a measurement; nothing from such a run may be saved.

The browser holds 60 fps at zoom 300 with no long task: against the same second window at zoom 110 the main thread is busy 1.3 points more (12.7 % against 11.4 %) and the 95th percentile frame
is 0.2 ms longer. The Jungle park has few guests, so this is the empty-park case; a crowded park was not measured in the browser.
