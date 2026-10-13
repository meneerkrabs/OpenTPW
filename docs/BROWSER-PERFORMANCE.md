# Browser performance

OpenTPW shares its game code between native builds and the browser. The browser's
WebAssembly runtime and WebGL bridge run much of that work on the page's main thread.
A task that occupies that thread also postpones drawing and input.

## Measuring

`tools/web-perf/run.mjs` repeats the measurements below with one command. It serves a published
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
| `revisit` | with `--keep lazy` or `--keep all`: the same profile reopened without the served folder; Play, the lobby, then Jungle from the kept files (fails if it asks for the folder) |
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
(`--music` keeps them on); world textures are the default Original with the bundled HD interface.
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
the speed, not the result. These are three runs per build on one machine: they show a large
improvement there, not a speed guarantee for every device.

The cost is the build: the native module grows from 2.9 to 32.6 MB, the Brotli-compressed
framework from 11.8 to 16.5 MB, and a publish takes about 110 s instead of 27 s. The browser
plan's rule (AOT when the cold construction or the first menu frame gets at least 40 % faster,
judged against the size of the original game the player supplies anyway) is met, so the
published page (the server image, `source/OpenTPW.Server/Dockerfile`) is compiled ahead of time.
A local `dotnet publish` stays an interpreter build without a workload (docs/WEB.md).

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
bundled Enhanced HUD. Jungle used a stationary camera, music and speech muted through
the UI, effects on and no Clarity downloads. Muting does not itself stop audio decoding.

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
Original world textures, Enhanced HUD and a 1280×800 browser viewport. These numbers
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
