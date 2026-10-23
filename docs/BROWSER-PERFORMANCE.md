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
| `switch` | memory across park changes (Jungle, the lobby, the next island's park, the lobby, Jungle again; `--visits <n>` adds a lobby and Jungle for each visit past the third), each point after a full collection and after waiting up to 60 s for the sound sample decodes in flight to end (a warning when one is still running): wasm, live managed memory, live parks, entities, live textures by source, archive and sound bytes, what the sound mixer reaches by the field holding it (`PerfAudioBytes`), and in the lobby the static fields that hold the most (`--runtime-roots`: also the framework's statics and array pools, `PerfRuntimeRoots`, and every reached object by type with an estimate of their bytes, `PerfObjectCounts`) |
| `revisit` | with `--keep lazy` or `--keep all`: the same profile reopened without the served folder; Play, the lobby, then Jungle from the kept files (fails if it asks for the folder) |
| `umbrellaCost` | (after `jungle`) the frame cost of a staged crowd (`--crowd`, default 400 extra guests) in rain, a steady window without and one with an open umbrella over every guest, at the Home and the farthest zoom (`Program.PerfUmbrellaCrowd`, loopback `?perf=1` only; docs/UMBRELLAS.md "Tuning") |
| `coopDigest` | (after `jungle`) the co-op session digest's cost, which the host takes once a second of real time: the median of 15 digests and world hashes on the park as it stands, again after up to 80 of the buy menu's rides and shops are built (money cheated in for them), again after `--crowd` extra guests are staged, and once more after 20 s of play (`Program.PerfSessionDigest`, `PerfPlaceObject`, `PerfCrowd`; docs/COOP.md "Measurements") |
| `winterCost` | (after `jungle`) the frame cost of a staged crowd (`--crowd`, default 400) in rain at the Home zoom, one window per step, each added to the one before: `rain` (a rain-only crowd, no snow: the staging applies weather quality 30 and every step applies it again before its window), `rainSnow` (the settled snow held at `--cover`, default 1), `rainSnowUmbrellas` (an open umbrella over every guest, held open), `rainSnowDressed` (and every guest in warm items) and the same at the farthest zoom (`rainSnowDressedFar`); each window records whether it still rained at its end, and the umbrellas and items drawn. The items' own cost is `rainSnowDressed` minus `rainSnowUmbrellas`. Last it saves a screenshot of a freshly staged dressed crowd in snow without umbrellas (`--shot`, `--zoom` default 70, the camera on one guest chosen by `--follow`, default -250: it skips the 250 newest visible guests, so with the default crowd of 400 it is the 150th guest of the new crowd, which stands in the open away from the entrance roofs; guests are picked by id order, so look at the picture and adjust `--follow` if a run lands under a roof) (`PerfSnow`, `PerfUmbrellas`, `PerfUmbrellasOff`, `PerfWinter`, `PerfRaining`; docs/WINTER.md "Tuning") |
| `events` | (after `jungle`) the frames after each event a player causes in a park session ("Event hitches" below): every HUD window opened, closed and reopened; the first buy previews and builds; weather; a bus arrival; the evening; research completing; the autosave; a month end and a year end; a coaster built; a crowd of `--crowd` guests; and a soak at the fastest speed (`--soak` seconds). For each event it reports the worst frame, the p95 and the slowest frames with the game's named work in them (`FrameMarks`). `--profile` adds the functions a CPU profile spent each slow frame in, named with `--symbols` |
| `door` | (after `jungle`) the entry price window's park door clicked through the HUD's input three times in the fresh, closed park: open, close, open again. Each click gets its worst frame, p95, the time until the economy reports the new state and, for an opening, the time until the first new guest (at most `--door-wait` seconds, default 20). The `events` scenario ends with the same clicks |
| `lobbyEnter` | (after `jungle`) the lobby's enter-park button in a session that starts with saved parks: the park is left to the lobby, which writes its automatic checkpoint, and the page is loaded again in the same profile. Then three clicks on the button, each followed up to the screen it opens and back, with its frames and the time until that screen |
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
  from the file when it is asked for (the file was opened for that read and closed again; since
  October 22, 2026 the desktop keeps it open between reads and closes it wherever archives may change,
  ENGINE-PERFORMANCE.md "Kept archive files", while the browser still opens it per read); an SDT sound bank read from a file keeps its offset table and
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
- **(e) Other managed caches.** Largest roots in the lobby: `AudioMixer` 18.7 MB (23.3 MB after a second park; about 11.3 MB of it are the archive members the file system keeps read, which the sound categories reach; see "The AudioMixer root across visits" below: it does not grow per visit), `Global.Render` 1.9 MB (10 MB before the leak fix), `SignTextRenderer.shared` 1.6 MB. Archive members read on demand are 13 MB.

#### The AudioMixer root across visits (21 October)

The 4.6 MB "growth" of the `AudioMixer` root was a sampling artefact, not a leak. Two things made the figure misleading:

- **A sample decode in flight.** In the browser a sentence's next segment is decoded cooperatively (`SoundEventSystem.DecodeCooperatively`, `Mp2Decoder.DecodeCooperativelyAsync`), a few MPEG frames between frames. Until it ends, it holds its MPEG input (0.7 MB for a lobby music segment) and its growing PCM buffer (`DecodeState.output`, a `List<short>` that doubles: 2 MB, then 4 MB). The lobby music's segments are long, so a point taken two seconds after reaching the lobby usually catches one or two of them. The October run caught one in the first lobby (2.7 MB) and two in the second (7.4 MB): the 4.6 MB. Taken 20 seconds later, no decode was in flight and everything the mixer reaches was back at about 20 MB in every lobby.
- **The file system's cache counted under the mixer.** The root walk (`PerfReachability.BytesByStaticRoot`) counts an object under the first static field that reaches it. The sound categories keep the file system they read their banks from, and `AudioMixer.Current` comes before the file system's own static, so the 11.3 MB of archive members the file system keeps read (`WadArchiveFile.CachedData`) were counted as the mixer's. The mixer's own memory is the PCM of its voices (1.6-6.7 MB) and the decoded-sample cache (2.0-8.4 MB, the long music segments capped at 8 MiB).

The harness now waits until no sound decode is in flight before it takes a point (`soundDecodesInFlight` in `PerfCounters`, from `GameAudio.Counts`), and `PerfAudioBytes` names what the mixer reaches by the field that holds it (the diagnostic that found the two causes). The root walks also no longer enter a `ConditionalWeakTable` (the sound system's table of ended categories): reading an entry whose key has been collected crashed the walk in the AOT runtime ("memory access out of bounds"), once the points were taken with no decode in flight.

Six visits on the published AOT build (Chrome 155 headless and muted, Apple M5, 1280 x 900; `--scenarios jungle,switch --visits 6`), each point after a full collection. Both runs are of 21 October, before the texture fix below (the October figure above, 18.7 MB, is an earlier run of the same first point):

| Step | Before: AudioMixer root MB | Before: decodes in flight | After: AudioMixer root MB | After: decodes in flight | After: live managed MB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Lobby | 18.8 | 1 | 20.2 | 0 | 47.2 |
| Lobby again | 23.5 | 2 | 20.0 | 0 | 50.9 |
| Lobby 3 | 23.3 | 2 | 20.1 | 0 | 51.1 |
| Lobby 4 | 23.4 | 2 | 20.2 | 0 | 51.0 |
| Lobby 5 | 23.5 (by `PerfAudioBytes`) | 2 | 20.1 | 0 | 51.0 |

"Before" is the same game code sampled the old way (two seconds after reaching the lobby, decodes in flight or not); the game's sound code did not change. Even then the root steps up once, from the first lobby to the second (one decode in flight against two), and stays there; it never grew per visit. The Jungle points are flat as well (19.5-20.8 MB reached from the mixer, 7.8 MB of decoded samples, 4-5 voices). The live managed memory also steps up once (47.2 -> 50.9-51.1 MB in the lobby in this run) and stays flat from the second visit on; that step is not the mixer's (see the next section).

A with-data test (`AudioParkVisitTests`, inconclusive without `OPENTPW_GAME_PATH`) runs the sound system without an output device through five visits (the lobby, Jungle with its music, placed ambients, crowd, a scream and a click, the listener moved away so the looping ambients release their outputs, then the lobby again) and checks that every visit leaves the same voices, sentences, chains, placed sounds, tracked one-shots and loops and held sounds as the first, and no decode in flight; with the park's chains not stopped at its end it fails (one more chain each visit).

#### Textures and live memory across visits (21 October)

The same six-visit runs showed one more texture alive after every park visit (306 -> 311 in the park, 278 -> 282 in the lobby; 389 -> 396 GPU textures). A texture made from bytes now records the file and line that made it (`Texture.Origin`, with `PerfTrackTextureOrigins`; the browser's AOT stack traces carry no method names), and the switch scenario's texture groups named it: `BlueprintMarkers.AreaTexture`, the yellow-and-black square the coaster tool's placement area draws. Every park makes its blueprint markers, and their `OnDelete` released the material and model but not this texture, so each park left one 8 x 8 texture and its GPU texture for the session. It now goes with the markers. After the fix, textures held stay at 306 in Jungle and 277 in the lobby, and GPU textures at 391 and 361, from the second visit to the sixth.

The native front-end smoke test now checks it: the textures not loaded by path (less the type signs `ObjectSigns` keeps for the session on purpose) are as many after leaving a park as before entering it, after the first park and again after the second; a failure names the file and line of each texture left. Without the fix it fails with the blueprint area texture as the one extra.

Open item: the origin of a texture made from bytes uses `[CallerFilePath]`, so the build embeds the source paths of the build machine in the assemblies (the origin itself is recorded only while tracking is on). A `PathMap` in the build would make them relative; not done yet.

The texture-pack list of `.wct` textures (`Texture.wctTextures`, weak references used when a pack is switched) was pruned only at a pack switch, so each park added about 230 dead entries for the session. It is now pruned whenever it doubles (408-721 entries over six visits, against 232 more per visit before).

The one-time step of live managed memory after the first visit (in the run after the texture fix, with `--runtime-roots`: 47.1 -> 52.2 MB in the lobby, then flat at 52.1-52.2 MB to the sixth visit; 50.9-51.1 MB in the run of the previous section, so the step's size varies from run to run) is not held by the game. `PerfObjectCounts` (with `--runtime-roots`) walks every static field of the game's and the framework's assemblies and the shared array pools and estimates the bytes of everything they reach: 56.9 MB in the first lobby and 57.0 MB in each later one (+0.1 MB, a UI text layout cache filled on first use, 197 layouts). `PerfRuntimeRoots` shows the framework's statics and array pools flat at about 2.3 MB. The step therefore lies outside every static root: in the runtime's own accounting of its heap after the second park's allocation peak (the wasm memory grew from 218 to 452 MB during that build) or in GC handles the JavaScript interop holds; it does not grow after the second visit. What does keep growing is the GC heap's fragmentation, about 1 MB per visit (6.8 -> 12.5 MB in the lobby; the heap 48.7 -> 59.1 MB) with the live objects flat, and with it the wasm memory once more at the fourth park (452 -> 542 MB): the allocator's peak, not a leak, and the lever is less garbage in the park build (see the recommendations above).

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

## Event hitches (October 19, 2026)

The game stuttered in the browser at some in-game events. The `events` scenario measures them: `node tools/web-perf/run.mjs --web <wwwroot> --game <folder> --scenarios start,jungle,events --music`. `--music` keeps the advisor's speech on, so its decoding is measured too; Chrome stays muted. Every exported frame of at least `opentpwPerf.markMs` (8 ms; 40 ms during the cold start) carries the game's named work of that frame. `FrameMarks` sections cover the frame, update, draw, park, level and simulation parts, and every `LoadProfile` section counts as `load:<name>`. Off, a section is one boolean test. `--profile` adds a CPU profile (Profiler domain, 250 µs samples). For each slow frame it lists the hottest functions, the call stacks six frames deep (`stacks`) and the game functions behind the runtime's time (`owners`). The AOT module's functions appear as `wasm-function[n]`. `--symbols` names them from the publish's Emscripten symbol map: publish with `-p:WasmEmitSymbolMap=true` and pass `obj/Release/net11.0/wasm/for-publish/dotnet.native.js.symbols`. The `dotnet.native.*.js.symbols` copied into `wwwroot` is the runtime pack's, not the AOT module's. New hooks in `Program.Perf.cs` drive the events: `PerfOpenWindow`, `PerfSpeed`, `PerfAdvisorAdvice`, `PerfCheat`, `PerfResearch`, `PerfSkipToBoundary`, `PerfCrowd`, `PerfBuildCoaster`, `PerfAutosaveDue`, `PerfPlaceObject` and `PerfFrameMarks`. As with the other hooks, a park they touched is not a real run. The page puts them on the devtools console (as `opentpwPerf.program`) only on a loopback address with `?perf=1`; they are no security boundary, since the .NET runtime lets a console reach every `[JSExport]` of any page (`getDotnetRuntime(0).getAssemblyExports`), and they change only the game in that tab.

`PerfResearch` completes research items as a researcher's points would, with the park's events. `PerfSkipToBoundary` runs the economy alone to 45 ticks before a month or year boundary, and the park then crosses it at the fastest speed. The soak runs 30 s at the fastest speed with the Belly Bounce, the shops, the coaster and about 400 guests. That covers thought balloons, ride and coaster runs with guests boarding, arrivals and month ends as they happen. A ride start with boarding guests was not measured as an event of its own.

Published AOT builds, origin/main `eaf1ef83d` without (before) and with these changes (after). Both builds carry the harness. Chrome 155 headless and muted, Apple M5 through ANGLE/Metal, 1280 x 900, Jungle Full Simulation. Values are medians of three alternating runs of each build, in milliseconds: worst frame and p95 of the frames after the event. The machine carried other work (load average about 12), so differences of a few milliseconds are noise.

| Event | Before: worst | Before: p95 | After: worst | After: p95 | Cause (from the profile) |
| --- | ---: | ---: | ---: | ---: | --- |
| Park start, largest frame | 713 | | 234 | | the entering frame, before and after |
| Park start, entering frame | 572 | | 147 | | before: restart snapshot (System.Text.Json metadata for the snapshot types about 315 ms, asset manifest hash about 120 ms, capture about 85 ms) and the advisor (welcome line decode); after: capture 82, advisor creation 58, first speech play 22, first draw 19 |
| Park start, largest frame before entering | 216 | | 83 | | the `economy` step: case correction of archive member paths in the Emscripten file system (`BaseFileSystem.MatchCase`, about 120 ms of `lookupPath`); after: the economy's own catalogue and balance parsing |
| Park start, the gate sign step | 145-157 | | 73-74 | | x87 emulation of the gate sign (`SignMaskShaping`, `SignEffectShape`, `SignRelief`) |
| Park ready after the click | 9,763 | | 9,293 | | |
| First open of a HUD window (11 windows, range) | 13-42 | 1.7-2.7 | 8-18 | 2.4-6.4 | before: the advisor's line decoded in one frame (`Mp2Decoder.Synthesis.Run`, 17-50 ms per line, the most frequent hitch); after: decoded in 5 ms slices, which raises the p95 |
| Reopen of a HUD window (range) | 4-38 | 1.7-4.0 | 2.5-9 | 2.3-7.3 | the advisor's line, as above |
| First buy preview, Belly Bounce | 202 | 2.5 | 55 | 2.1 | sign composition in the draw; after: `Ext.Add` in `SignEffectShape.Blur` and `SignRelief` |
| Build Belly Bounce, Balloon Shop, Burger Shop | 2.7-9.2 | 2.1-2.6 | 3.3-7.0 | 2.2-2.4 | |
| Rain, thunderstorm, clear | 2.3-10.6 | 1.6-1.9 | 2.1-3.6 | 1.4-1.5 | |
| Bus arrival | 3.0 | 2.1 | 7.3 | 1.5 | noise |
| Evening dusk, night, day | 2.3-5.2 | 1.8-1.9 | 1.5-3.3 | 1.3-1.4 | |
| Research completes | 19.4 | 2.0 | 6.9 | 1.5 | before: the advisor's line |
| Autosave (capture, then slices) | 41.9 | 1.9 | 36.7 | 1.4 | the live capture, which must finish in one frame: `CaptureResumeSnapshot` and its `ValidateSnapshot` (`ParkLayoutSnapshot.ValidateCells`, `ValidateShape`, `ValidateGuests`) |
| Month end | 7.3 | 2.1 | 3.8 | 1.7 | |
| Year end | 26.5 | 2.3 | 7.6 | 2.6 | before: the advisor's line |
| Coaster built (station and circuit) | 20.3 | 2.1 | 16.5 | 1.7 | first use of the coaster's models and textures (`level.entities`) |
| Crowd of 400 guests added | 13.9 | 3.4 | 6.3 | 2.9 | |
| Soak at the fastest speed (30 s, 406 guests) | 21.3 | 3.4 | 10.6 | 3.0 | before: advisor lines |

The build of a park now takes about 35 frames instead of 16, each within the 12 ms budget, and the park is still ready about 0.5 s sooner.

### Changes

- **x87 sign arithmetic.** `Ext` multiplies, adds and rounds on two 64-bit words instead of `UInt128`, whose operators are slow in the AOT build. The blur loops skip zero bytes. A byte table replaces `FromInt`, and each blur kernel keeps its taps' products per byte value (`ScaledTaps`). `SignRelief.Shade` computes the normal terms of a pair of shape differences, and the unit and base terms of a strip byte, once per effect. The results are bit-identical: `X87FastPathTests` compares the word-pair code with the former `UInt128` code on random operands, the blurs with the loops that add every tap, and a relief's reused terms with a fresh relief. All Sign tests with the original data and the sign corpus hashes pass.
- **Advisor speech decoded over frames (browser only).** The length of a response comes from its MPEG frame headers (`Mp2Decoder.TryMeasure`) and equals the decoded length. The sequence and the span the controller waits are therefore unchanged. The PCM decodes in 5 ms slices (`Mp2Decoder.SlicedDecode`), and the player starts when it is complete, so a line's audio can start 50-150 ms later than with the whole decode first. The desktop decodes in one go as before. This is `[EXT:advisor-sliced-decode]`:
  - The mouth follows the audio that is heard. The LIP clock reads the advisor clock minus its base, as in the original, and when the audio started late because its decode was not done when it was due, the LIP clock is moved back by that lateness. The mouth therefore keeps the same lead over the audio as on the desktop.
  - The speech's end, the busy test that decides whether a cut-off plays the slap, and the end of the response all follow the player, so they move with the late audio. The animation, the drawn span and the controller's reservation stay on the request's clock, as in the original. The reservation's extra 1000 ms covers the lateness.
  - A damaged payload (frame headers readable, the decode failing later) ends the line at once: the speech stops, the model hides and its effects are cleared, and the controller's reservation becomes the 1000 ms a line that could not be said reserves. On the desktop the same payload fails before the line, and the advisor skips it with that reservation. The caption was posted and the message recorded as played either way. The difference: in the browser the model may have been shown for the few frames of the decode.
  - Any other failure of the decode is a fault and is reported once, as on the desktop.

  `SlicedAdvisorDecodeTests` checks the measured length and the sliced PCM against the one-go decode, for synthetic streams and a sample of the original responses. With `Advisor.SlicedDecoding` on and a test clock, `SlicedAdvisorSpeechTests` checks the speaking state during the decode, silence, kill and a new line during the decode, that the audio starts only once it is due, that the mouth matches the audio position after a late start, and the end of a line whose decode fails.
- **Park entry prepared in steps.** After the park is built and before it is entered, `GameFlow.PumpParkBuild` makes, in budgeted steps, what the first restart snapshot needs. One part is the JSON metadata of every type the snapshot reaches (`ParkResumeJson.WarmupSteps`), deepest first. The other is the asset manifest, one catalogue archive per step (`Level.PrepareAutosaveAssets`), which the snapshot and the autosave then reuse. Entering gets a frame of its own. The snapshot's bytes do not change. A refusal during the steps is left for the snapshot to report as before. Tests: `TheCodecsMetadataIsMadeInStepsAndChangesNoByte`, `TheStepsOfASyntheticParkMakeTheSameManifestInSeveralFrames` and `APreparedParkKeepsTheManifestItsStepsMadeForItsSnapshot`.
- **File system answers remembered.** With `CacheLookups` (the browser's game data), `BaseFileSystem` remembers whether a path exists on disk and each directory's listing, under the same invalidation as its other lookups. The case correction of a path inside an archive therefore stops testing and listing every directory along it for each member. With remembered answers, the walk also replaces the whole-path test. `FileSystemLookupCacheTests` compares the cached and uncached answers on mixed-case synthetic paths and on every Jungle path in three spellings.

### The entering frame (October 21)

After the changes above, the frame that enters the park still took 147 ms. The rest of it came from the runtime's first-call work (method initialization and generic instantiation in the AOT build) and from file reads that only the entering frame made. Measured on published AOT builds of this branch after merging origin/main `926db9535`, without (before) and with (after) the changes below, same machine and harness as above, medians of three runs, in milliseconds. The `FrameMarks` names in the table come from the entering frame:

| Part of the entering frame | Before | After |
| --- | ---: | ---: |
| Whole frame (also the largest frame of the park start) | 216 | 85 |
| Construction part (the last entry of the build-frame list) | 134 | 35 |
| Restart capture (`load:restart-capture`) | 71 | 7 |
| Advisor creation (`load:advisor`) | 37 | 6 |
| First speech (`advisor.play`; 5-6 ms of it is the first 5 ms decode slice) | 14 | 8 |
| First draw (`frame.draw`) | 19 | 19 |
| Park HUD, created and first update (`load:park-hud`, `park.hud`) | 23 | 16 |
| Park ready after the click | 9,199 | 9,147 |
| Largest frame before entering | 74 | 75 |

The build takes 38 frames instead of 34. No frame of 40 ms or more follows the entering one. The other events of the `events` scenario stayed within noise.

- **Validation after entering, in steps.** `ParkResumeSaveFile.ValidateSnapshotSteps` runs the same checks as `ValidateSnapshot`, in the same order, with a pause between groups of them. The first restart snapshot's write runs at least one step per slice, before its serialization (`[EXT:restart-snapshot-deferred-validation]`). The live capture still happens in the entering frame, between two ticks. The snapshot needs the advisor attached there, and the park's first update runs in that same frame. The pending marker is written in the entering frame as before, so a crash before the validation ends never restarts into an earlier park. A refusal removes the snapshot and the marker, as a refused capture did. Restart, park replacement and shutdown finish the steps at once. Because the steps include the shape check, the serialization does not repeat it. Tests: `DeferredValidationRunsInSlicesOfItsOwnAndTheBytesAreUnchanged`, `ARefusedDeferredValidationLeavesNoRestartPointAsARefusedCaptureDid` and `TheValidationInStepsIsTheWholeValidation`.
- **The capture's code warmed in the build.** `Level.WarmResumeCaptureSteps` runs the capture parts that only read the park once and drops their results: the economy, layout, guest, thing-pool, script-world, weather, gate, staff, ride, pool, tour and extension captures, which the periodic autosave also runs on a live park. The economy capture alone fell from 14 to 0.6 ms in the entering frame. Runtime captures stay out because they prune effect records. Test: `TheCaptureWarmUpOfAParkBuildChangesNeitherTheParkNorItsSnapshot`, which checks the state hash and the snapshot bytes.
- **The advisor's reads in the build.** `AdvisorEntryPreparation` reads what the advisor's creation and first line read: `Advisor.sam`, the global and the level advisor model, the clip lengths of both archives and the theme costume. It also loads the shipped response, caption and costume tables (`[EXT:park-entry-preparation]`). Each result is taken once, only by its own reader, only from the file system it was read from and only while that file system's lookup generation (`BaseFileSystem.LookupGeneration`, raised by every invalidation and every change of its mod overrides or archive redirect) is the one it was read under. A failed read is not kept, so its reader meets the failure and reports it as before. What a read logged is held (`Logger.BeginCapture`) and written when its result is taken. Whatever the park's first update did not take is dropped and disposed, but for the lines decoded ahead (since C2c-2b-3e, below), which wait for their own saying. Everything is also dropped when a build starts its preparation, when a build fails or is abandoned, and when the game tears down (except the tear-down of the park a build replaces, whose results are for the new park). Tests: `AdvisorEntryPreparationTests`.

The welcome line's PCM was still decoded over frames (the sliced path above), starting in the entering frame. Since C2c-2b-3e the park build decodes the lines a park's start says first (the Welcome, which has one response, and in Instant Action the prebuilt park's) ahead, a 5 ms slice of their MPEG a step (`AdvisorEntryPreparation.LineSteps`), and the advisor takes their PCM when it says them; a line decoded ahead starts at its time instead of a few frames late. The first draw did not change. The CPU profile of that frame did not name its cost, because the profile's window and the frame did not line up. Pipelines are already created with their materials during the build.

### The first draw and the park HUD (October 21)

The entering frame's first draw (`frame.draw`, 19 ms) and the park HUD (`load:park-hud` and the HUD's first `park.hud`, 16 ms) were the two largest parts left. Two harness changes named their cost:

- **A profile lined up with the frame.** The harness tied the CPU profile's clock to the page's with one `performance.now()` read after `Profiler.start`. That read was 28-30 ms off in the Jungle start, so the entering frame's profile showed the frames before it. Now the harness finds the frames the profile saw itself: the runs of samples under `main.js`'s frame callback (`profileFrames`). It slides them over every frame the page recorded and keeps the shift where both agree on the most time (`profileClockOffset`). A run of back-to-back frames merges into one in the profile, but the pattern of their lengths and gaps still lines up. Each slow frame reports the shift as `profileOffsetMs`. The entering frame's samples now add up to its length (89.9 ms sampled for an 89.7 ms frame).
- **Marks inside the draw.** `FrameMarks` now also cover the draw's parts (`draw.backdrop`, `draw.sky`, `draw.level`, `draw.advisorModel`, `draw.hud`, `draw.advisorUi`, `draw.depthImages`, `overlay.ui`), every entity type's draws (`draw:GuestRenderer`, ...), the lazy material resource sets (`draw.materialResourceSets`) and the interface textures made on first use (`ui.image <path>`, `ui.atlas`, `ui.solid`, `ui.renderedSet`). While marks are on, the browser's WebGL calls are recorded by kind too (`gl.createTexture`, `gl.uploadTexture`, `gl.createBuffer`, `gl.uploadBuffer`, `gl.createResourceSet`, `gl.createProgram`, `gl.createPipeline`, `gl.execute`; `Veldrid.WebGlMarks`).

What the profile and the marks showed, on the published build before the changes below:

| Part | ms | What it is |
| --- | ---: | --- |
| Sky (`draw.sky`) | 4-8 | first-time work: `Level.DrawSky` created the sky on its first call. That reads the colour grid (a `.tga` decode, most of `load:image-decode`), loads the cloud and cylinder textures, and builds the geometry and two materials. |
| Interface textures (`draw.overlay`) | 5-6 | first-time work: the overlay decoded, keyed and uploaded each image of the HUD's first draw (`UiImages.LoadKeyed`; `thoughts2.wct`, `thoughts.wct`, `purple_button.wct`, `meter.wct`, `b_money.wct` and smaller ones) and smoothed the font atlas |
| Entities (`draw.level`) | 3.4 | the first call of each renderer's draw (`RideHeadRenderer`, `BalloonRenderer`, `GuestRenderer`, `LitterRenderer` 0.4-0.6 ms each). The GPU calls are small: all `gl.*` marks together stay under 1 ms, 24 material resource sets take 0.3 ms. The rest is the AOT runtime's first-call work. |
| Advisor model (`draw.advisorModel`) | 1.9 | its first pose and the render target of its image; the advisor itself is made in this frame |
| HUD draw (`draw.hud`) | 1.2 | the first call of each widget's draw |
| Deletions at the frame's end (`frame.post`) | 0.4-2.9 | the retired lobby's textures, one WebGL `deleteTexture` each |
| HUD creation (`load:park-hud`) | 6.6 | `ParkHud.Build` (about 3 ms), the build catalogue's grouping (`OriginalBuildCatalog`, about 2 ms, mostly its LINQ `GroupBy`'s first use) and the rest of the constructor |
| HUD first update (`park.hud`) | 8.7 | spread over the update's parts, each paying the runtime's first-call work: `UpdateBlueprint` (`BlueprintMarkers.Show`, about 3 ms), `UpdateCursor`, `SampleHappiness`, `HandleCheatKeys` and smaller ones |

Two changes move or spread the first-time work. The picture does not change: the same sky, HUD and textures are made, only earlier, and a deletion only releases what nothing draws any more.

- **The sky, the HUD and its first textures in the build.** The build's preparation (`GameFlow.PrepareEntry`) now ends with `PrepareFirstFrame`, after the advisor's and the snapshot's steps. `Level.PrepareSky` loads the sky the first draw would load, from the same files. Nothing of it depends on when it is made. Next, the park HUD is created as `EnterLevel` creates it, in a step of its own, and `EnterLevel` takes it (`[EXT:park-entry-preparation]`). It is made last, so everything the other steps make comes before it, as in the entering frame. The staged scene is current then, as it is at entering. The level does not update before it is entered, and input waits while it is built, so the HUD meets the same park. The constructor reads only one thing from outside the park: whether the camcorder key and the mouse button are held. `ParkHud.RefreshEntryInput` reads both again in the entering frame, so a held key is not taken for a new press. Last, `ParkHud.EntryTextures` names the textures the main screen's first draw makes: every frame's images of the models its visible elements show, the purple bar of its text buttons, the happiness meter, the fonts of its labels and buttons at the canvas's text scale, and the two pointers the first frame can show. `UiRenderer.Prepare` makes each one in its own step, as the draw would. A dropped build clears its prepared HUD, as a tear-down clears a retired one. Each part is a head start only: a sky, HUD or texture that fails in its step is logged once and left to the entering frame, which meets the same failure, and the build goes on. A HUD whose constructor fails partway removes the handlers it had put on the level. A texture-pack switch that starts during the steps stops the texture preparation. Tests: `EnteringFramePreparationTests`; the front-end smoke now checks that the park built over frames is entered with the HUD a build step made. It also checks that this HUD's screens, elements, messages, modes, captured economy state and host connections equal those of a HUD made on entering, before and after the first update, that the sky was loaded before the first draw, and that a build dropped after its HUD was made clears that HUD and releases its materials. The world hash, the entity count and the object count still equal the park built in one go.
- **Deletions spread over frames (browser).** At the end of a frame the browser now runs the oldest scheduled deletions in order, for at most 1 ms (`Renderer.BrowserDeletionBudgetMilliseconds`), and the rest waits for the next frames. At least one runs per frame, so the queue drains. The desktop runs them all at once, as before. A deletion that throws is removed with the ones before it, so it is not run again every frame; `Renderer.FlushDeletions` still runs every deletion at once (the particle oracle, tests). The harness's counters report `pendingDeletions`, and the Jungle scenario fails when they have not drained 5 s after its steady window. Tests: `DeletionsRunInOrderWithinTheirBudgetAndTheRestWaitsForTheNextFrame`, `AThrowingDeletionIsRemovedAndNotRunAgain` and `ADeletionScheduledByADeletionRunsAfterTheOthers`.

Measured on published AOT builds of this branch without (before) and with (after) these changes, both with the new marks, same machine and harness as above. Values are medians of five runs of each build, in milliseconds:

| Part of the entering frame | Before | After |
| --- | ---: | ---: |
| Whole frame | 83 | 60 |
| Construction part (the last entry of the build-frame list) | 36 | 27 |
| First draw (`frame.draw`) | 19.4 | 7.7 |
| Sky (`draw.sky`) | 7.6 | 0.4 |
| Interface textures (`draw.overlay`) | 5.3 | 1.0 |
| Entities, advisor model, HUD draw (`draw.level`, `draw.advisorModel`, `draw.hud`) | 3.4, 1.9, 1.2 | 3.1, 1.8, 1.0 |
| Image decoding (`load:image-decode`) | 4.9 | 0 |
| HUD creation (`load:park-hud`) | 6.6 | 0 |
| HUD first update (`park.hud`) | 8.7 | 7.4 |
| Largest frame of the park start | 83 (entering) | 73 (a build step) |
| Largest frame before entering | 77 | 71 |
| Park ready after the click | 9,157 | 9,137 |

The build takes 39 frames instead of 38; each new step stays within the 12 ms budget. No frame of 40 ms or more follows the entering one. The entering frame is no longer the largest frame of the park start: the economy and gate sign steps of the build are. The `events` scenario's other events stayed within noise. Some runs of both builds met other work on the machine, which raised the p95 of every event in them to about 5 ms.

What the entering frame still spends, apart from the advisor's creation, first update and first speech (about 15 ms together) and the live capture (7 ms):

- **The HUD's first update (7.4 ms).** Almost all of it is the runtime's first-call work in the update's parts. Running the update during the build would warm it, but it would also change the HUD's state: its timers, the happiness sample, the pointer and the camera flags it sets. So it stays in the entering frame. `RuntimeHelpers.PrepareMethod` cannot warm it either: Mono does not implement it.
- **The first draws of the entities, the advisor model and the HUD (about 6 ms).** These are first calls as well. The advisor is made in the entering frame, so its model cannot be drawn before.

### Build steps (October 21)

After the entering frame's changes, the largest frames of the park start were steps of the park's build. Each did one piece of work at once. The harness named them: `--profile` with the new `--cold-mark-ms` option marks and profiles every frame of the cold start from the given length (20 ms here), and `tools/web-perf` reports the cold start's frames over 33 ms. Measured on the published AOT build of origin/main `6ecadcbd9`, same machine and harness as above, one profiled run (times under the profiler run about 1.5 times longer than without it). Construction ms of one step:

| Step | ms | What it was |
| --- | ---: | --- |
| Economy | 77-84 | `ObjectCatalog.Load`, called inside the economy's catalogue for the mods' objects (`EconomyObjectCatalog.AddModObjects`): with the official extensions on, their content roots count as mods, so the whole object catalogue was read here at once (about 44 ms). The economy's own catalogue (13), the balance settings (4) and the `ParkEconomy` constructor (10, of which `ParkResearch` 6). |
| Terrain and gate sign | 44 | the gate sign's composition (`SignSurfaceComposer.Compose`, 30: the effects 17, of which the shape's blur 6 and the relief 11; the paint masks' shaping 6; the text masks 5) and the blueprint markers |
| Objects | 53 | the ride script handler table (`RideOpcodes`, 33): its static constructor searched the assembly by reflection and compiled every handler with `Expression.Compile`, on the first object's script |
| Guests | 70 | `SetupGuests` (61): the kid, pool, umbrella and winter sprite sets and anchors (6-8 each), and the renderers' atlases and their texture uploads (8-10 each) |
| Staff, gate and economy | 58 | the staff's sprite sets (16) and renderer (17), the level HUD (7), the placed particles (4) |
| Entry preparation | 40 | the warm-up of the economy capture (15-20); in another run the advisor's settings (30, of which 10 a scan of every message per descriptor row in `AdvisorTables.UnproducedMessages` and 8 the scan messages) |
| Terrain | under 33 | the ground mesh and its textures, the terrain meshes' textures |

The work was not the problem; each step held a whole part of it. The steps were split, and the steps that remained long now start a frame of their own. What each step makes, and the order, is unchanged:

- **The economy in steps.** `ParkEconomyRuntime.ForOriginalLevelSteps` makes the balance settings, then one object archive per step (`EconomyObjectCatalog.LoadSteps`), the object catalogue the mods' objects come from in its own steps (`ObjectCatalog.LoadSteps`; the objects step later finds it cached, as before), the park, then the import. `ForOriginalLevel` runs the same steps to the end.
- **The gate sign composed in steps.** `SignSurfaceComposer.ComposeSteps` has a step after each text mask, each paint mask, each effect's shape and every 16 rows of relief. `Compose` runs it to the end, so the pixels are the same; `OriginalGateSign.CreateSteps` builds the gate's sign with it, and a sign that fails is left out as before.
- **The ride script handler table in steps.** `RideOpcodes.TableBuilder` looks at up to 256 methods and compiles up to 8 handlers per step, in the assembly's order. The level runs it before the objects (`RideOpcodes.PrepareStep`), and a use meanwhile finishes it at once. The table and its order are those of the former static constructor.
- **The guests, the staff and the terrain in more steps.** `SetupGuests` yields after each sprite set and renderer, the staff after its world, its sprites and its renderer, the staff-gate-economy section after each group of imports, the ground mesh after each texture and the terrain meshes after each mesh and batch texture.
- **Long steps in a frame of their own.** A park build frame runs steps until 12 ms have gone, so a long step that started late in a frame made it longer still. A construction step that knows the next one is long sets `Level.NextStepIsLong` (before the sprite sets and the renderers). A preparation step returns false (the steps before the advisor shader, the advisor's global model and the economy capture's warm-up). Either way the announcing step has done its work: the frame ends after it, and the long step starts the next frame. The advisor shader's GL program, which the browser's renderer keeps for later materials, is compiled in a step of its own (`AdvisorRig.WarmShader` makes a material of it and releases it). Before, it was compiled inside the model's step.
- **Smaller preparation steps.** The economy capture's warm-up runs its parts first, one per step. `ParkSaveFile.CapturePartSteps` runs the ledger, objects, staff, research and objectives, which the capture now takes from methods of their own, in the same order. The warm-up also runs the snapshot's extension and asset filters over the placed objects with a one-key manifest of the manifest's own type (a sorted dictionary), and drops the results. The advisor's message tables are made one per step (`AdvisorTables.WarmSteps`). The unproduced messages are found with a set of the produced ids instead of a scan per row, which gives the same list.
- **First calls of the entering frame done in the build.** Three were no-ops that could run earlier: the blueprint markers' `Show` while no cell is shown (its queries' first calls were 9.6 ms of the HUD's first update), the player progress file's serializer metadata (5 ms, written as the park is entered; `PlayerTicketFile.WarmUp` writes nothing) and the sliced serializer's constructor (6 ms of AOT method initialization; `SlicedJsonSerializer.WarmUp` serializes an empty string with options of its own).

Tests: `BuildStepsTests` compares the economy made in steps with the one made at once (Full Simulation and Instant Action Jungle, its whole save and import evidence), signs composed in interleaved steps with signs composed at once, the opcode table built in steps with every handler in the assembly's order, and the unproduced advisor messages with the former scan. The front-end smoke also compares the gate sign's pixels of the park built over frames with the park built in one go (digest `F22DF07B950D3E1C` both). The world hash `9C04AE5746B2E140`, 124 entities and 14 objects are as on origin/main. The sign corpus hashes, the restart snapshot's tests (its capture warm-up changes neither the park nor its snapshot) and the determinism tests pass.

Published AOT builds of origin/main (before) and this branch (after), alternating, medians of three runs, in milliseconds. The machine carried other work (load average 6-14):

| Measure | Before | After |
| --- | ---: | ---: |
| Largest frame of the park start | 80 (the economy step) | 56 (entering) |
| Largest frame apart from the entering one | 80 | 36 (the advisor's global model) |
| Largest construction part of a build step (the harness's build-frame list, without the entering one) | 79 | 34 |
| Build steps whose construction part is over 20 ms | 12 | 3 |
| Frames over 33 ms, from the click until 5 s after the park is ready | 8 | 2 |
| Frames of 50 ms or more, same window | 5 | 1 |
| Entering frame | 68 | 56 |
| Of it: the HUD's first update (`park.hud`) | 14.9 | 4.8 |
| Of it: the restart snapshot (`load:restart-snapshot`) | 8.2 | 11.7 |
| Frames the park is built over | 40 | 63 |
| Park ready after the click | 9,227 | 9,376 |

The park is ready about 150 ms later: it is built over 63 frames instead of 40, and each frame also updates and draws the lobby. The frames over 33 ms left are:

- the entering frame;
- the step that reads the advisor's global model (about 35 ms, see below);
- in one run, a frame of the restart snapshot's slices after entering.

The restart snapshot in the entering frame rose by about 3.5 ms. Its profile shows the runtime's generic-context work (`mini_init_method_rgctx` and its hash table) in the snapshot's asset filter (`ParkResumeMods.AssetsForUse`), which happened elsewhere before. The filter's warm-up with the manifest's own type took half of it back. Such first-call work moves between frames when other first calls move, so this part of the entering frame varies by a few milliseconds.

### The lobby's enter-park button (October 21)

The lobby's enter-park button (`enterPark`, `b_entpark`) opens the island's theme start: the parks to continue and New park, or the game mode screen when there is none. To offer the saved parks, the click reads them (`ThemeContinuation.Probe`, `AutomaticThemeContinuation.Newest`: each slot's file parsed and checked, the automatic ones also serialized again for their digest). The first such read of a session also made the world-save codec's reflection metadata, which took a whole frame. The new `lobbyEnter` scenario measures it. After the Jungle park, it leaves the park to the lobby, which writes the automatic checkpoint, then loads the page again in the same profile, as a player returning to the game does. It then clicks the button three times. Each click is followed up to the screen it opens and back to the lobby.

| Click | Before: frame | Before: until the screen | After: frame | After: until the screen |
| --- | ---: | ---: | ---: | ---: |
| First click of the session | 397 | 402 | 126 | 132 |
| Second click | 53 | 63 | 67 | 74 |
| Third click | 53 | 63 | 62 | 67 |

Values are in milliseconds: the worst frame after the click, and the time from the click until the game reports the theme start. Medians of five runs of each build. The later clicks differ only by the machine's load: in the two pairs of runs taken alternately they were 59-69 ms before and 56-67 ms after. The first click's frame was the codec's metadata (`ParkResumeJson.Deserialize`: `decode_klass_ref`, `mono_metadata_type_hash` and the other type work of the runtime). Every click then reads and checks the saves. The automatic ones are also serialized again for their digest (`menu.themeStartSaves`, 45-65 ms). The first click still spends about 60 ms more than the later ones, mostly in the runtime's interpreter on the codec's first runs (`mono_interp_exec_method`, `do_jit_call`). No click was ignored or needed a second click in these runs. The harness clicks through the game's own input injection (`PerfClick`), not through the browser's mouse events, so a focus problem of the page itself would not show here.

**The codec's metadata in the lobby's idle frames.** When any theme has a saved park, the lobby's frames now make that metadata in steps (`ParkResumeJson.WarmupSteps`, the same steps a park's build runs for its first restart snapshot). Each frame runs steps until at least 4 ms have gone. Nothing is read or written, and the codec's output does not change. A click before the steps end makes the rest itself, as before. A session without saved parks skips it. Every click still reads the saves, as before (about 50 ms); caching them would need a guarantee that the files have not changed.

### The park door (October 21)

The new `door` scenario clicks the entry price window's door through the HUD's input three times in the fresh Jungle park (closed): open, close, open again. It is also the last part of the `events` scenario, so the events before it keep their closed park. The new `PerfParkOpen` hook reads the door's state; the clicks go through `PerfClick`.

| Click | Worst frame | p95 | Until the economy reports the new state |
| --- | ---: | ---: | ---: |
| First (opens) | 9.5 | 1.4 | 17 |
| Second (closes) | 3.0 | 1.5 | 11 |
| Third (opens again) | 3.6 | 1.5 | 13 |

Values are in milliseconds, medians of three runs of the final build. The state time includes the harness's polling.

The door's first opening has no hitch: the economy reports the park open within a frame or two. The wait after it is the original's arrival schedule. A park's first arrival group is due at turn 604 of the economy's clock, counted from the park's start and not from the door (`GuestSimulation.UpdateArrivals`; a turn is 248 ms, so about 150 s at normal speed). Then the bus brings the group. The door does not reset that timer. In the long run, the first new guest was in the park 146 s after the first click. Nothing was changed there.

### Remaining hitches

| Hitch | Worst frame | Cause | What a fix would take |
| --- | ---: | --- | --- |
| Entering the park | 56 ms | the advisor's creation, first update and first speech (about 17 ms), the restart snapshot's live capture (12 ms), the first draw (8 ms: first calls of the entities', the advisor model's and the HUD's draws), the HUD's first update (5 ms) and the park's audio (3 ms) | first calls are the runtime's: warming them needs a side-effect-free way to run the code once before entering |
| Autosave capture | 37 ms, once per autosave interval | `CaptureResumeSnapshot` and its validation in the frame the autosave is due | the same deferred validation; a cheaper capture of the cell layout |
| First buy preview of a ride with a sign | 55 ms | the sign's effects: `Ext.Add` in `SignEffectShape.Blur` and the relief's square roots through `BigInteger` (`Ext.IntegerSquareRoot`) | a word-pair square root and add-free blur passes, or composing the sign over frames before the preview shows it |
| The advisor's global model (a park build step) | about 35 ms | one step reads the model, its textures and its clips (`AdvisorRig.Load`) | a read in steps: `AdvisorEntryPreparation.Prepare` would need steps, with its log capture around each one |
| A click on the lobby's enter-park button with saved parks | 50-65 ms per click (125 ms for the session's first) | the theme start reads and checks every saved park of the island, and serializes the automatic ones again for their digest | keep what the probe read for files that have not changed, or read them over frames before the screen shows |
| The restart snapshot's first slices after entering | 30-35 ms | its validation of the cells and its first serialization slices meet their first calls | finer validation slices |
| Coaster first built | 17 ms | first use of the coaster's models and textures | preload them with the park's catalogue |
| Advisor line, while decoding | p95 about 6 ms | the 5 ms slices | none needed (under a 60 Hz frame) |

## Joining a shared park (October 22, 2026)

A guest's join of a co-op session builds the host's park over frames beside the live scene, as a park start does, and swaps it in once it is checked (docs/COOP.md, "The game's join"). `scripts/measure-coop-join-browser.sh` measures it in a published page: the co-op join smoke's measuring join phase (`tools/web-coop-join.mjs`, `?coopSmoke=1`) over a host phase recorded once by the desktop's Release build of the same source. Chrome 155 headless and muted, Apple M5 through ANGLE/Metal, 1280 x 900, no frame cap; medians of five runs compiled ahead of time and three interpreted, in milliseconds. The native column is the desktop's first build in a process (Release, compiled as it first runs), from the measurement of C2c-2b-1; it times the update only, the page also each whole frame. Every value of every run is in docs/COOP.md, "Measurements".

| Part of the join | AOT | Interpreter | Native |
| --- | ---: | ---: | ---: |
| The save's parse (one step) | 374 | 508 | 167 |
| The objects' restore and the coasters (one step) | 320 | 1,413 | 436 |
| The level's last step | 141 | 307 | 172 |
| The guests (one step) | 85 | 250 | 135 |
| The check at the cut | 76 | 101 | 45 |
| The swap's frame (the check, the swap and the rest of the frame's update) | 331 | 644 | 325 |
| The longest frame while building, with its drawing | 392 | 1,420 | 440 (update) |
| The wait for the level's files | 375 frames, 6.4 s | 383 frames, 7.2 s | none |
| Catching up after the swap | 638 ticks in 10 frames, 0.59 s | 758 ticks in 29 frames, 2.0 s | 1 tick, 1 frame |
| The resync over the guest's park, longest frame | 142 | 333 | 88 (update) |

- **The wait for the files spins.** A page copies a theme's folder on the theme's first use, and the join's build waits for it. Its loop asks whether the files are there until the slice's 12 ms budget is spent, so every frame of the wait took 16 ms of update instead of the front end's 1 ms, for 6.4 s. A park start waits once a frame instead (`GameFlow.QueueLevel`).
- **The swap's frame.** About 245 ms of it is neither the check nor the swap: the rest of that frame's update, the joined park's first among it. The join's build has none of the steps that prepare a park start's entering frame (`GameFlow.PrepareEntry`, "The entering frame" above).
- **Next.** C2c-2b-3 splits the save's parse, the swap's frame, the objects' restore, the level's last step and the guests, and ends the wait's slice instead of spending it. It measures again first: the build steps split for a park start since ("Build steps", #741) are the join's level steps too.

### Where a join's slice ends (C2c-2b-3a)

On main after the build steps were split (#741), the join's level steps had shrunk (the level's last step 62 ms ahead of time, the guests 24 ms), but the wait for the files still spun, and the save's parse made the codec's reflection metadata in one go. C2c-2b-3a ends the wait's slice after one question, ends a slice before a step known to be long, and makes the metadata in steps of its own. Same machine and harness, medians in milliseconds (docs/COOP.md, "Measurements"):

| Part of the join | AOT before | AOT after | Interpreter before | Interpreter after |
| --- | ---: | ---: | ---: | ---: |
| The wait for the level's files | 371 frames | 9 frames | 387 frames | 8 frames |
| The join's build | 403 frames | 64 frames | 471 frames | 107 frames |
| From asking to join to the swap (the page's frame record) | 8.9 s | 2.7 s | 13.2 s | 5.2 s |
| The save's parse (the metadata in steps after) | 384 | 88 | 582 | 191 |
| The objects' restore and the coasters | 283 | 288 | 1,361 | 1,230 |
| The swap's frame, with its drawing | 341 | 345 | 658 | 627 |
| Catching up after the swap | 0.57 s | 0.26 s | 2.17 s | 0.75 s |

The page copies a theme's files between its frames, so the loop that spent every frame's budget asking for them most likely kept that copy waiting; these timings do not follow the copy itself. The measuring smoke's marks were on while each park was built, where the baseline had none, so small differences are no finding. The swap's frame is now the longest of a join ahead of time. Its marks show the joined park's first update of its level (185 ms: the pools' 72, the entities' first use of models, textures and pipelines 67, the first simulation step 45), the check at the cut (75) and the advisor's first update (39). A park start prepares much of that first-time work in its build (`GameFlow.PrepareEntry`); the join's build does not yet (C2c-2b-3c).

### The swap's frame (C2c-2b-3c)

C2c-2b-3c checks the joined park at the cut in the frame its build completes in and swaps it in the next, holds the stream's ticks to the frame after the swap, and prepares the park's first frame in a last build stage (`prepare`, `[EXT:park-entry-preparation]`): the coasters' views, the pool bridges' meshes and the swimmers' sprite sets. Same machine and harness, medians of three runs (five before) in milliseconds, compiled ahead of time (docs/COOP.md, "Measurements"):

| Part of the join | Before | Check and swap apart | First frame prepared |
| --- | ---: | ---: | ---: |
| The swap's frame, with its drawing | 345 | 229 | 119 |
| A resync's swap's frame, with its drawing | 144 | 121 | 39 |
| The frame after the swap, with its drawing | 38.6 | 52.7 | 53.2 |
| A join's longest frame, with its drawing | 345 (the swap's) | 290 (the objects' restore) | 292 (the objects' restore) |

The check (74 ms) now shares the frame its build completes in, the first ticks (a replayed placement's first models among them) the frame after the swap, and the prepared work 113 ms over 7 build frames, its longest step 39 ms. The swap's frame keeps the advisor's first update (37 ms), the pools' (23 ms, the swimmers' renderer still made there) and the session's step (13 ms). The longest frame of a join is now the objects' restore (C2c-2b-3d).

### The objects' restore over steps (C2c-2b-3d)

C2c-2b-3d restores a joined park's saved objects one a build step instead of all in one, so the build's budget ends its slices between them (`ParkLayoutSnapshot.RestoreObjectSteps`). Same machine and harness, medians of three runs in milliseconds, compiled ahead of time (docs/COOP.md, "Measurements"):

| Part of the join | Before | After |
| --- | ---: | ---: |
| The longest build frame, its update | 290 | 87.0 |
| The objects' sections | 287 | 266 |
| The level stage | 672 over 23 frames | 622 over 27 |
| A join's longest frame, with its drawing | 292 (the objects' restore) | 110 (the swap's) |

The restore's longest step is now its first: the snapshot's checks and the first object, 87 ms. A type's sign is still composed on the step of its first object (134 ms in all), since the page has no worker to compose it on; on the desktop the sign worker takes most of that work. The swap's frame is a join's longest again; its largest parts are the park's first update of its level and the advisor's first update, about 35 ms each.

### The advisor prepared for a join (C2c-2b-3e)

C2c-2b-3e reads in the join's build what the joined park's advisor reads for its first line, as a park start's build does, and decodes that line ahead (docs/COOP.md, "The game's join"). Same machine and harness, medians of three runs in milliseconds, compiled ahead of time:

| Part of the join | Before | After |
| --- | ---: | ---: |
| The swap's frame, with its drawing | 110 | 90.1 |
| Of it the advisor's first update | 33.9 | 4.3 |
| The first frame after the swap, with its drawing | 48.5 | 41.8 |
| The build's `prepare` stage | 103 over 7 frames | 187 over 14 |
| A join's longest frame, with its drawing | 110 (the swap's) | 94.4 (the restore's first step) |

The `prepare` stage's longest step is still a coaster's (38 ms); the advisor's steps took at most 14 ms, and the line's decode is spread over 5 ms slices. The save stage, which this change does not touch, took 87.6 ms against the before's 80.7. A join's longest frame is now a build frame (94 ms with its drawing, median); the longest steps are the restore's first (93 ms: the snapshot's checks and the first object with its sign) and the save's parse (88 ms).


### A type's first use in steps of its own (C2c-2b-3g)

C2c-2b-3g reads what each saved type's first object reads in build steps of its own before the restore places the objects: its model and clips, its sign, its textures and its hoarding (docs/COOP.md, "The game's join"). The page has no sign worker, so it composes a type's sign over the composition's own steps and hands it to the type's first object as the desktop's worker hands over its signs. The restore's checks end a step of their own, and the layout's checks format a message only when one fails. Same machine and harness, medians of three runs in milliseconds, compiled ahead of time; the before is `b5de0764e`, which only adds the measuring smoke's records of the build's long frames:

| Part of the join | Before | After |
| --- | ---: | ---: |
| The restore's first object | 94.0, with the checks | 12.1 |
| A type's first object, the longest step | 62.2 (the water ride) | 20.5 (the minecart) |
| The types' own steps, the longest | none | 13.5 |
| The validation stage's longest step | 27.4 | 6.7 |
| The cells' checks, three a join | 63.1 | 12.4 |
| The level stage | 656 over 27 frames | 620 over 41 |
| A join's longest build frame | 94.1 (the restore's first step) | 86.3 (the save's parse) |

The snapshot's checks, before in the restore's first step with the first object, are a step of their own under the budget. The signs cost the same (141 ms composed in steps ahead against 142 ms on their objects' steps), now spread over the level's 314 more steps, and no frame of the objects' part of the level reaches 30 ms. Of the minecart's 20.5 ms, 16.8 ms are its coaster ride's first use. A join's steps of more than 50 ms are now the save's parse (86 ms), the check at the cut (71 ms, in the frame its build completes in) and the level's last step (64 ms, the resume's restore).

### The save's read over steps (C2c-2b-3h)

C2c-2b-3h parses a join's save a 32 KiB slice a step, with the serializer's own asynchronous reader fed a slice a step (`SlicedJsonDeserializer`), and checks its shape in steps of their own (docs/COOP.md, "The game's join"). The page has no thread to parse on, so the parse is spread over frames instead. Same machine and harness, medians of three runs in milliseconds, compiled ahead of time; the before is `9e48a4ee4`:

| Part of the join | Before | After |
| --- | ---: | ---: |
| The save stage's longest step | 91.4 | 14.7 |
| The save stage | 91.4 in one step | 90.0 over 43 steps and 7 frames |
| A resync's save stage's longest step | 53.8 | 3.5 |
| A join's longest build frame | 91.5 (the save's parse) | 76.9 (the level's last step) |
| A resync's longest build frame | 55.7 | 30.3 |

The parse costs what it did, spread over its steps. The validation stage's last step, the comparison of the asset manifests, took 19.7 ms in five of six runs against 7 to 9 before, with no garbage collection in it; its cause is not known. A join's steps of more than 50 ms are now the check at the cut (71.5 ms, of which 56.5 ms is the economy's serialization for its digest making the serializer's metadata in a process's first check; 7.9 ms in a resync) and the level's last step (69 ms, of which the resume's restore takes 59 ms against 6.3 in a resync). Both are mostly a process's first use of their code. The review fixes after the measured commit make the asset manifests' comparison, among the save's last eager checks, format no message while its checks hold.

### The check and the level's last step (C2c-2b-3i, 3j)

Both were mostly a process's first use of their code: compiled ahead of time, the code a call runs for the first time still costs its compilation by the browser's WebAssembly engine, which is not the serializer's metadata alone. C2c-2b-3i runs the economy digest's serialization once ahead in the join's build, over the joined park's economy, a part of its save a step (a last build stage, `digest`), after making the metadata in the codec stage; C2c-2b-3j restores the saved simulation a part a step (docs/COOP.md, "The game's join"). Same machine and harness, medians of three runs in milliseconds, compiled ahead of time; the before is `0d7383931`:

| Part of the join | Before | After |
| --- | ---: | ---: |
| The check at the cut | 71.5 | 13.4 |
| The level stage's longest step | 68.9 (its last) | 28.1 |
| The digest stage | none | 49.1 over 43 steps and 3 frames, longest 23.7 |
| A join's longest build frame | 76.9 (the level's last step) | 40.5 |
| A resync's digest stage | none | 25.0 over 3 frames |

The metadata with the resume's steps alone took the check to 46 to 81 ms. No build frame of a join now reaches 50 ms. The swap's frame measured 93 to 106 ms with its drawing against 87 to 92 before, but its pool swimmers vary from run to run: in alternating runs of both pages the before's swap frames took 101 to 119 ms and the after's 95 to 114.
