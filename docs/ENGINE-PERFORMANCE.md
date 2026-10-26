# Engine performance

October 18, 2026. Where the desktop engine spends CPU time in a busy park, how to measure it, what was changed and
what is left. The browser build runs the same code on Mono AOT; its own measurements are in
[BROWSER-PERFORMANCE.md](BROWSER-PERFORMANCE.md).

The numbers below come from one Apple-silicon Mac (macOS, arm64, .NET 11 RC2, Release build) that was shared with other
`dotnet test` runs the whole time. Compare builds only with runs taken alternately on the same machine; a difference
smaller than the spread of the medians is not a result. Game data: a PC installation of Theme Park World.

## Measuring

| What | Command |
| --- | --- |
| Simulation, level park with the real objects and scripts | `dotnet source/OpenTPW/bin/Release/net11.0/osx-arm64/OpenTPW.dll --guest-report --guest-levels jungle --guest-level-seeds 1 --guest-seeds 1 --guest-arrivals 20 --guest-no-perf --guest-warmup-days 5 --guest-days 300` (add `--guest-extension smarter-guests` for the extension; `--guest-arrivals 3` for a crowded park) |
| Simulation, synthetic scenario (µs per tick, best of five) | `--guest-report --guest-levels none --guest-seeds 1 --guest-scale extended` |
| Frame, native, a busy park | `bash scripts/run.sh --load-original-level jungle --offscreen --silent --mute --no-advisor --frame-stats --perf-run 3000 --perf-arrivals 3` |
| CPU profile of either | `dotnet-trace collect --profile dotnet-sampled-thread-time --format speedscope -- dotnet OpenTPW.dll …` |

`--perf-run N` (RUNNING.md) hides the developer panels as a park started from the front end does, opens the park door,
runs the simulation at four times the normal speed (`--perf-speed 1|2|4`) with the guests arriving every
`--perf-arrivals` turns, and closes the window after N frames. Without `--no-vsync` an offscreen run is paced at 60 Hz,
which is what a player's frame budget looks like; with it the frames are CPU-bound, which suits profiling. `--frame-stats`
now also prints where a frame goes: the update (and inside it the simulation), the world drawing, the interface overlay,
submit and present, the wait for the GPU, and the whole `Renderer.Update` call.

Since October 19, 2026 every `--perf-*` run is offscreen without `--offscreen` (RUNNING.md, `[EXT:offscreen-automated-runs]`), so a
perf run never opens a window on the developer's display; `--visible` (or `--no-offscreen`) opts back in to the presented window. The
baselines on this page were all taken with `--offscreen`, so they stay comparable with the new default. A visible run is not: its
pacing follows the display (up to 120 Hz on this Mac's ProMotion panel) and macOS throttles the Metal drawables of hidden, occluded or
locked windows. Measured on the same Mac (Release, Jungle, `--mute --no-advisor --frame-stats --perf-run 1500 --perf-arrivals 3`,
offscreen 1512 x 843, two rounds each, other sessions running): paced, 60.0 fps with 1.7 to 1.9 ms of CPU and 0.6 to 0.7 ms of world
drawing per frame once loaded; with `--no-vsync`, 219.5 and 220.9 fps, a frame average of 4.55 and 4.53 ms (both windows include the
level load) and 1.05 to 1.11 ms of world drawing, in line with the 3.5 to 4.0 ms at the Home zoom in the table below.

The sampling profiler (`dotnet-sampled-thread-time`, 100 Hz) stops threads at GC poll points, so a loop with a back edge
collects more samples than its cost and inlined callees fall into their caller. The simulation figures below therefore
come from stopwatches around the tick's phases; the profile was used to find the candidates.

## Where the time goes

Simulation, Jungle level park of the guest report (14 placed objects, 2 staff, about 42 guests in the park, 104,577
ticks = 305 park days, original guests), before any change:

| Phase of a tick | Time | Share | Allocated |
| --- | ---: | ---: | ---: |
| Objects: scripts, host step, animation (`OriginalObjectRuntime.Simulate`) | 1,834 ms | 67 % | 723 MB |
| of which `ObjectAnimator.Advance` (poses, vertex animation) | about 1,600 ms | 59 % | |
| Guests: `BeginSweepTick` and the tick between turns | 451 ms | 17 % | 72 MB |
| Park turn: economy and the guests' thing sweep (7,028 turns) | 435 ms | 16 % | 149 MB |

That is 26 µs per tick, 8.9 ms per park day (343 ticks), or 0.16 % of one core at the normal speed of 60 ticks per second.
Even the crowded park (`--guest-arrivals 3`, 560 guests in the park on average) costs 41 µs per tick, 0.25 % of a core.
The simulation is not what limits the game on the desktop; a frame is (below). It matters more in the browser, where
the same work runs several times slower on the one thread that also draws.

Inclusive hotspots of the sampled tick loop (a profile taken before the vertex-pose and key-search changes below; inclusive, so they overlap, and biased as described above):

| # | Method | Share of the tick loop |
| ---: | --- | ---: |
| 1 | `ParkEconomy.AdvanceTurn` (turn sweep) | 30 % |
| 2 | `GuestSimulation.UpdateGuest` (all states) | 28 % |
| 3 | `ObjectAnimator.Update` (node poses and meshes) | 18 % |
| 4 | `OriginalObjectRuntime.Simulate` | 15 % |
| 5 | `ObjectAnimator.UpdateMesh` | 11 % |
| 6 | `ModelVertexAnimation.ApplyAnimatedGroups` | 9 % |
| 7 | `ParkEconomyRuntime.FixedTick` | 9 % |
| 8 | `GuestSimulation.SweepGuest` | 7 % |
| 9 | `ModelVertexAnimation.ApplyPose` | 7 % |
| 10 | `GuestSimulation.UpdateGoingToRide` | 6 % |

Frame, the same park in a native run (paced at 60 Hz, four ticks per frame, about 25 guests): the main thread spends about
1.05 ms of the 16.7 ms frame, of which the world drawing is 0.7 ms, the update 0.17 ms (0.13 ms of it the simulation) and
the interface overlay 0.04 ms. Submit, present and the GPU wait are under 0.02 ms. The process uses 1.6 to 2 ms of CPU per
frame in total (Metal and audio threads included).

Frame hotspots (a CPU-bound `--no-vsync` run before any change, with the developer panel still visible; share of the main
thread's CPU under `Renderer.Update` without the wait for the GPU fence; inclusive, so they overlap):

| # | Where | Share |
| ---: | --- | ---: |
| 1 | `Level.Render` → `ModelEntity.OnRender` (33 drawn parts per frame) | 44 % |
| 2 | Veldrid Metal `CommandList.UpdateBuffer` for the per-draw uniforms (a staging buffer and a blit encoder per call) | 16 % |
| 3 | Veldrid Metal `BeginCurrentRenderPass` (the render pass restarts after every blit) | 16 % |
| 4 | `GameFlow.Update` → `Level.Update` | 16 % |
| 5 | `Material.Set` (the same uniform uploads seen from OpenTPW) | 10 % |
| 6 | `Sky.Draw` (21,600 vertices rebuilt and uploaded per frame) | 14 % |
| 7 | `Renderer.Compose` (HUD, text, overlay) | 8 % |
| 8 | `ParkLayout.Draw` (ImGui developer panel; hidden in a front-end park) | 7 % |
| 9 | `Level.AdvanceSimulation` | 6 % |
| 10 | `Marshal.SizeOf<Vertex>()` in the renderers (reflection on every call) | 4 % |

## What changed

Every change keeps the order of operations and the floating-point expressions it touches, so results are bit for bit the
same; `DeterminismTests`, the `*FixedRunHash` pins, `ParkResume*`, the animator, vertex-animation and snapshot tests and
the guest report's JSON (including every seed's state hash, original and smarter-guests, two park densities) are
unchanged.

| Change | Why it is behaviour-neutral | Effect |
| --- | --- | --- |
| `ObjectAnimator` keeps its channels in two arrays rebuilt when the sorted dictionary changes, instead of enumerating the dictionary for every node and copying it every tick (`channels.ToArray()`) | the arrays hold the same channels in the same ascending order; `Advance` iterates a snapshot as before | the object phase allocates 0.8 KB per tick instead of 6.9 KB |
| `ObjectAnimator.Advance` skips the whole-pose update when no channel advanced and the last such update changed nothing (`settled`) | the update is a function of the channels' ticks and the node-state bits; when nothing changed it computes the previous result again. A pose that changes something on every update (two ended channels leaving opposite node-state bits on one mesh toggle it each time) is never marked settled, so its version counters count as before | 45 % of the object ticks of the Jungle park do no pose work |
| `OriginalObject.UpdateTransforms` returns when the animator's new `PoseVersion` has not changed | a part's transform is a function of the pose and the object's (read-only) placement | no matrix work for still objects |
| `ModelVertexGroup` hands the sampler its arrays and the largest index it lists; `ApplyAnimatedGroups` and `ApplyStaticGroup` loop over them | same keys, same fused multiply-adds in the same order; the index check is made once per group (a failing group still throws from the original loop with the original message) | no LINQ, interface enumeration or per-call validation in the per-tick vertex pose |
| `ModelAnimationTrack.FindKey` takes the key times through a struct accessor instead of a delegate over a captured list | the same linear scan | no closure allocation per sampled track and tick |
| `Model.Bounds` is measured when read (the next draw), not on every `UpdateVertices` | the bounds of the vertices that are drawn, which is what the frustum test used | no scan of the mesh per animated mesh and tick |
| `Sky` stages its vertices once per layer count and rewrites only the clouds' texture coordinates, computed once per grid vertex | the uploaded buffer is byte-identical (checked every frame for 4 layers in a native run before the check was removed) | about 30 % less `Sky.Draw` CPU |
| `Vertex.SizeInBytes`, and the same for the UI and text vertices, replace `Marshal.SizeOf` in the per-frame renderers | the same number, read once | no reflection per draw |
| `UiBatch.AddText` indexes the layout's glyph list instead of enumerating it through its interface | same glyphs, same order | no enumerator per text |
| `--frame-stats` phase timers, `--perf-run` | measurement only; the timers read the clock only while frame statistics are on | |

Results, medians of three alternating runs of the pre-change and the changed build:

| Measure | Before | After | Change |
| --- | ---: | ---: | ---: |
| Jungle park, original guests: object phase of the ticks | 1,834 ms | 1,092 ms | −40 % |
| Same, allocated by the object phase | 723 MB | 86 MB | −88 % |
| Same, guest phase / park turns (not touched) | 451 / 435 ms | 481 / 446 ms | noise |
| Same, per tick / per park day | 26 µs / 8.9 ms | 19 µs / 6.6 ms | −26 % |
| Same, whole `--guest-report` process, 600 days (median of 5, includes about 1 s of start) | 6.6 to 7.4 s | 5.4 to 6.3 s | −15 to −18 % |
| Same with `--guest-extension smarter-guests` (whole process) | 7.3 s | 6.7 s | |
| Smarter guests against original guests, whole process, after the change | | 6.7 s against 6.3 s | +6 % (about +8 % of the simulation) |
| Frame, update (simulation inside) | 0.18 (0.14) ms | 0.16 (0.11) ms | within noise |
| Frame, main thread, whole `Renderer.Update` | 1.08 ms | 1.05 ms | within noise |
| Frame, allocation | 96 KB, 5.9 MB/s | 43 KB, 2.7 MB/s | −55 % |

The frame's CPU time did not move measurably: the remaining cost is in the world drawing (below), which this pass did not
touch. What it removed is garbage, which matters most in the browser, where a collection pauses the only thread. The
smarter-guests ratio of the synthetic scenario (`--guest-report … --guest-extension smarter-guests`, best of five)
cannot be separated from noise on this machine (1.1 to 1.4 µs per tick both ways, the pinned 1.08 in SMARTER-GUESTS.md
stands); on the Jungle park it is the +6 % above. The guest code itself (`GuestSimulation`) was not changed.

Point lights (October 18, 2026, [LIGHTS.md](LIGHTS.md)) were added to the per-draw uniform upload rather than as another upload: a draw with no light uploads the same 272 bytes as before and the frame's cost with no
light in the park did not move (whole update call 1.91 to 2.17 ms before and 1.90 to 2.16 ms after in four alternating runs); a light adds 32 bytes to the draws it reaches.

## What is left

1. **Per-draw uniform uploads (about 30 to 40 % of the frame's CPU).** `Material.Set` writes the object uniforms through
   `CommandList.UpdateBuffer` between two draws. On Metal, Veldrid answers each call with a staging buffer, a blit
   encoder and a new render pass (`UpdateBufferCore`, `EnsureBlitEncoder`, `BeginCurrentRenderPass`, `newBuffer` in the
   profile), so 33 parts cost about 16 µs each. `UiRenderer` already avoids this by writing mapped buffers per frame slot.
   Approach: write all uniforms of a pass into one mapped ring per frame slot and bind them with dynamic offsets
   (`ResourceLayoutElementOptions.DynamicBinding`), or at least upload every uniform before the pass's first draw. It
   changes how every material binds, so it needs pixel comparisons that this pass (no captures) did not make.
2. **The sky (about 0.1 ms per frame after this pass).** It still uploads 21,600 unindexed vertices (227 KB) every frame
   although only the texture offsets move. Approach: an indexed 16 × 16 grid per layer in a static buffer and the scroll
   in the uniform buffer (a shader change).
3. **Vertex-animated meshes.** `ModelVertexAnimation.ApplyAnimatedGroups` dequantises both keys of every vertex on every
   tick. The words are fixed, so a per-group cache of the dequantised keys would halve the arithmetic (at three times the
   memory of the packed words; build it lazily per played group).
4. **The pose update of an animating object** recomputes every node's scale, rotation and translation matrices on every
   tick. A node whose channels give the same tick as the last update (several of the Jungle park's clips are slower than
   the 60 Hz tick) could keep its matrix, and unanimated nodes only need their parent's change to propagate.
5. **RideVM variable access by name** (`GetVariableIndex(string)` is 1 to 2 % of the tick): resolve the standard variables
   to indexes once per script.
6. **Per-turn allocations of the thing sweep** (`Level.RunThingSweep` builds dictionaries and sets, 21 KB per turn in the
   gate park): small at 7.5 turns per second, worth reusing in the browser.
7. **`Level.Update` per frame** copies `Entity.All` twice (`ToArray` in `Update` and `Render`); a reused snapshot buffer
   would remove the last of the per-frame garbage that is not Veldrid's.
8. **The browser.** None of the above was measured there. `tools/web-perf` (BROWSER-PERFORMANCE.md) runs the same
   simulation as its `digest` scenario; the object phase is the part most likely to show.


## Frame time at the wide zoom (October 18, 2026)

The wide zoom option (`GameOptions.WideZoom`, UI.md) lets the park camera go out to the zoom value 300, where much more of the park is in view. Measured on the same Mac (Apple silicon, Release
build, other sessions running), Jungle, `--offscreen --silent --mute --no-advisor --no-vsync --frame-stats --perf-run 1500 --perf-arrivals 3 --perf-zoom <value>`, two rounds, offscreen 1512 x 843:

| Zoom value | Frame average (ms) | World render (ms) | GPU wait (ms) | Model parts drawn / outside the view |
| ---: | ---: | ---: | ---: | ---: |
| 40 | 3.31 (one round) | 0.92 | 0.46 | 27 / 54 |
| 110 (Home) | 4.02, 3.53 | 1.01, 0.89 | 0.56, 0.68 | 35 / 46 |
| 130 | 3.83, 3.93 | 0.96, 1.05 | 0.67, 0.60 | 37 / 44 |
| 200 | 3.76, 4.06 | 0.94, 1.06 | 0.74, 0.64 | 38 / 43 |
| 300 | 6.68, 6.78 | 1.58, 1.52 | 1.43, 1.51 | 72 / 9 |

Longer runs (9000 frames, a guest arriving every turn, 20 to 25 guests in the park) gave 1.7 to 3.4 ms at zoom 110 and 3.4 to 3.7 ms at zoom 300 (150 to 290 frames a second without the
display's 60 Hz pacing). So zooming all the way out costs about 1.7 times the frame time of the Home view in this park, an extra 2 to 3 ms of drawing (the park's whole object set is now in view
and the terrain fills the screen), and stays far inside the 16.7 ms of 60 Hz. The run is unthrottled, so the numbers carry the other sessions' load; the Jungle park has few guests, so a crowded
park costs more (guest sprites in view scale with the guests), which this run did not measure. Nothing caps the frame rate or the draw distance at the wide zoom.

## Start and build hitches (October 22, 2026)

Issue #754: on a Windows PC (Direct3D 11, a hard disk, German data) the window stayed black for about 30 seconds after the start-up movies before
the lobby appeared, and the park froze for 0.5 to 2 seconds several times while the player opened the build menu and placed objects.

### Measuring

| What | Command |
| --- | --- |
| The start up to the lobby | `dotnet OpenTPW.dll --no-intro --no-autorun --mute --startup-profile` |
| The buy window and the first placement of each object type | `dotnet OpenTPW.dll --load-original-level jungle --mute --no-advisor --perf-build --hitch-report 30` |
| Any slow frame in a normal run | add `--hitch-report <ms>` |

`--startup-profile` records the start with the load profile of the browser harness (`LoadProfile`): named sections (`startup.language`,
`startup.mods`, `startup.renderer`, `front-end`, `lobby-scene`, `shader-spirv`, `shader-native`, `model-parse`, `texture-decode`, `wad-read`, ...)
and counted amounts on every thread (file opens, bytes read, directory listings, WAD archives indexed and member reads, native shader programs,
pipelines, GPU textures and their bytes). It logs them when the lobby has drawn 120 frames and closes the window. `--hitch-report` logs each frame
of at least the given length with its frame marks (the browser harness's `FrameMarks`, the load sections as `load:<name>`) and its counted amounts.
`--perf-build` does what the player in the issue's video did, one step every 20 frames: the buy window, each tab and its pages of rows (the preview
icons), a selected row (the turning preview), then one object of each of several types placed through the build tool's command; each step's frame
is logged, with `perf.place` (the placement) and `perf.site-search` (the scenario's own search for a free site) as separate marks. All three are
measurement only and run offscreen and silent by default. The file counts are what the game asks for; the numbers below come from the same Mac
as the sections above (warm file cache, Metal), so a cold hard disk adds seek time per file open and per first read of a directory, and Direct3D 11
adds the shader compiles described next.

### What took the time

Every `Material` made its own `Shader`, and every `Shader` asked Veldrid for new native shader objects of the same file. The SPIR-V was
already compiled once per file, but Veldrid turns SPIR-V into the backend's own form for each new native shader: on Direct3D 11 that is HLSL
handed to the system's HLSL compiler (`D3DCompile`, optimisation level 3), once per stage, for every material. The lobby makes 146 materials
of the lit object shader (`test.shader`) for its islands, so the start compiled 156 native programs (312 HLSL compiles); a park start made 225,
and the first placement of a ride type 9 to 15, one per model part. On Metal a native program costs about 1 ms (the source is compiled when the
pipeline is made, and Metal caches compiled libraries), which is why the Mac never showed it; an optimised HLSL compile of the lit shader takes
far longer, which matches both the 30 seconds before the lobby and the freezes when an object is placed.

Start to the lobby, before the change (Release, three alternating runs, `--startup-profile`):

| Phase | Time | Count |
| --- | ---: | ---: |
| Main to the lobby's first frame | 1,091 to 1,771 ms | |
| `front-end` (lobby definition, scene and menu) | 471 to 500 ms | 1 |
| `lobby-scene` | 456 ms | 1 |
| `shader-spirv` (GLSL to SPIR-V, once per file and process) | 343 to 348 ms | 7 |
| `shader-native` (Veldrid's backend shaders) | 174 to 183 ms | 156 |
| `startup.renderer` (window, device) | 152 to 160 ms | 1 |
| `startup.game-flow` (strings, fonts, interface renderers) | 87 ms | 1 |
| `model-parse` | 64 ms | 316 |
| `texture-decode` | 44 ms | 135 |
| `startup.compatibility`, `startup.mods`, `startup.language` | 21 to 23, 22, 7 ms | |
| `wad-read`, `wad-decompress`, `wad-index` | 8, 10, 4 ms | 483, 383, 2 |
| `pipeline` | 3 ms | 153 |
| counted | 477 file opens, 4.4 MB read, 1,063 directory listings, 2 WAD archives indexed, 466 member reads, 150 GPU textures (6.6 MB) | |

The pools extension's guest sprites and the other content packs are not read before the lobby (the counts above have no access to them); they load
with a park. Reading the data is small: 4.4 MB in 477 opens, almost all of `lobby.wad` and `ui.wad` (each member read opens the archive again) and
the fonts. Cold, on a hard disk, that is a few hundred milliseconds of seeks and reads at most; it cannot explain 30 seconds.

The build steps of `--perf-build` (Jungle, before the change): the buy window, its four tabs and their 85 rows each cost no frame over 17 ms (the
preview icons decode a few textures; the first turning preview compiles the interface's depth shader, a 62 ms frame once per process). The
placements are what costs: the first placement of type 1104 took a 100 ms frame on the Mac, of which `perf.place` was 69 ms, with
9 native shader programs, 25 GPU textures and 302 directory listings counted in it; later types 5 to 83 ms.

### What changed

| Change | Why it is behaviour-neutral | Effect |
| --- | --- | --- |
| `ShaderCompiler.CompileShader` shares one native shader pair per shader file among all its users, counting references; the last `Release` disposes it after the last frame that may draw it (the browser's compiler does the same) | native shader objects are immutable and every pipeline only reads them; each material still makes its own pipeline, layouts and buffers from the same compiled program | 7 native programs to the lobby instead of 156, 9 instead of 225 over a park start and the build scenario, none for a placed object once its shaders are in use |

After the change (same runs, alternating with the build before it):

| Measure | Before | After |
| --- | ---: | ---: |
| Native shader programs to the lobby | 156 (312 HLSL compiles on Direct3D 11) | 7 (14) |
| `shader-native` to the lobby | 174 to 183 ms | 3.4 to 3.6 ms |
| `front-end` | 471 to 500 ms | 295 to 339 ms |
| Main to the lobby's first frame | 1,091 to 1,771 ms | 908 to 1,193 ms |
| Native shader programs, park start plus the build scenario | 225 | 9 |
| First placement of type 1104: frame / `perf.place` | 100 to 104 / 69 ms | 78 to 80 / 54 to 56 ms |
| First placement of type 1101: frame / `perf.place` | 83 to 84 / 81 to 83 ms | 65 to 70 / 64 to 68 ms |
| Native shader programs made by a placement | 9 to 15 | 0 |

The world capture of the Jungle park (`--capture-world`, 120 frames) differs from the build before the change by as many pixels as two runs of the
old build differ from each other (0.3 % of the pixels, the moving guests); the native smokes pass.

### Remembered path lookups (October 22, 2026)

The desktop's game-data file system did not remember its path lookups, so every read listed the directories along its path again (to find the
archive a path lies in and, on case-sensitive systems, the on-disk spelling): 1,063 listings before the lobby, 15,770 for a park start, 300 to
480 for an object's first placement. It now remembers them as the browser's does (`BaseFileSystem.CacheLookups`, made in
`GameDataFileSystem.Create`, which the browser now uses too). Nothing in a session adds files below the data folder (a new game folder applies at the next start), and what
changes what the lookups see invalidates them itself: the mod overrides, the language's interface archive and the mod content roots. The files
read are the same; `FileSystemTests` now run on the desktop's own file system.

| Measure (Release, Mac, three alternating runs) | Before | After |
| --- | ---: | ---: |
| Directory listings to the lobby | 1,063 | 10 |
| Directory listings, park start plus the build scenario | 15,770 | 724 |
| `start-level` (Jungle) | 1,675 to 1,831 ms | 1,213 to 1,410 ms |
| `perf.place` of a type with few parts (1100) | 4.0 to 4.3 ms | 1.4 to 1.7 ms |
| Main to the lobby's first frame | 915 to 1,453 ms | 935 to 1,430 ms (within the noise) |

### Kept SPIR-V (October 22, 2026)

Every start compiled the same GLSL sources to SPIR-V again (`shader-spirv`, 343 to 384 ms for 7 files before the lobby on the Mac, about 30 ms per
stage whatever its size; 450 ms with a park, and a 52 to 58 ms frame the first time the buy window's turning preview draws its depth image).
`ShaderCompiler.CompileStage` now keeps each stage's SPIR-V in the user's cache folder (`cache/shaders`) under a SHA-256 of everything the
compile reads: the preprocessed source, the stage, the source name the debug information records, the options, and the compiler's managed
assembly version and native library (`libveldrid-spirv`, its name, size and time). A kept file is therefore the bytes the compiler would make now.
Each file (`ShaderCache`) holds a format tag and the SHA-256 of its payload, so a file damaged after it was written (cut short, overwritten) is
refused and compiled again; a payload that is whole but that the cross-compiler refuses anyway (a review wrote each stage cut to half, with a
matching digest) makes `CompileProgram` remove the kept files of that program and compile both stages from the source, which replaces them. A
file's time is refreshed whenever it is used, files unused for 30 days are removed when a new one is written, and a write that fails only logs a
trace line. The reflection still comes from the cross-compile (about 1 to 6 ms per program). The cache holds 14 files (about 116 KB) after a start,
a few more after a park.

| Measure (Release, Mac, runs alternating with the build before) | Before | After, cache filled |
| --- | ---: | ---: |
| `shader-spirv` to the lobby | 362 to 384 ms (14 compiles) | 7.5 to 7.8 ms (14 kept) |
| Main to the lobby's first frame | 918 to 948 ms | 554 to 558 ms |
| `shader-spirv`, park start plus the build scenario | 450 to 460 ms | 25 ms |
| `start-level` (Jungle) | 1,251 to 1,258 ms | 969 ms |
| First park frame | 225 to 229 ms | 171 to 177 ms |
| First turning preview in the buy window | 66 to 68 ms frame | under 30 ms |

The first start after an install or a shader change compiles as before and fills the cache.

### Kept Direct3D 11 bytecode (October 22, 2026)

With one native program per shader file (above) Direct3D 11 still compiled the HLSL of each program once per run: 14 compiles (7 programs, two
stages) before the lobby and a few more when a park starts. If the issue's 30 seconds were mostly the 312 compiles before the change, one took
about 0.1 s on that PC, so these are about 1.3 s of every start (an estimate; no Windows machine measured it here). Veldrid's Direct3D 11 backend compiles the cross-compiled HLSL of a new shader with the system compiler
(`D3D11Shader.CompileCode`: `vs_5_0` or `ps_5_0`, entry point `main`, `OptimizationLevel3`, no defines, include or source name) unless the
shader's bytes already are bytecode (`DXBC`), which it takes as they are. With a cache folder, `ShaderCompiler.CreateNative` now makes the same
cross-compile and the same HLSL compile itself (`Direct3D11Bytecode`), keeps the bytecode beside the SPIR-V (`cache/shaders/*.dxbc`, keyed by a
SHA-256 of the HLSL, the profile, the flags and the version, size and time of `d3dcompiler_47.dll`, which Windows updates replace) and hands the
bytecode to Veldrid. The files are `ShaderCache` files like the SPIR-V: a missing, damaged (digest mismatch) or malformed file (not a whole `DXBC`
container) is compiled again and replaced, and bytecode the device refuses anyway makes `CreateNative` remove the program's kept files and
compile its HLSL again; without the system compiler's identity nothing is kept. Old files go with the cache's 30-day expiry by last use.
`NativeWorldShaderRenderTests.KeptDirect3D11BytecodeIsVeldridsOwnCompile` checks on the Windows CI runner's Direct3D 11 device that the kept
bytecode is byte for byte the bytecode Veldrid compiles for the same shader, that a second request reads it, that a file cut short is compiled
again, that shaders made from the kept bytecode draw the same pixels as Veldrid's, and that a whole but wrong container is refused, compiled
again and drawn the same. Other backends are unchanged (Metal compiles its source when the pipeline is made and caches it itself).

### Signs composed ahead (October 22, 2026)

The first placement of a ride, shop or sideshow with a sign composed the sign on the frame of the click (`ObjectSigns.Get` from the object's
model build: the `.sgn` read, the wavelet decode of its board, the glyph rasters and the paint and relief layers; `object-sign`, 36 to 48 ms on the
Mac for types 1104 and 1101). The buy window now hands every type it lists to `ObjectSigns.ComposeAhead` whenever its list changes (a tab, a
sort), and one worker composes those signs in the order asked. Only the CPU part moves: the RGBA halves. The GPU textures are still made on the
render thread when a material first asks for them. `Get` uses a composition that has finished, waits for one that is running, and runs one that
has not started on the calling thread (one `Lazy` per sign and text, shared by the worker and every caller). The composition holds what it logs
(`Logger.BeginCapture`) and `Get` writes it when it uses the result, so the log reads as before. A composition records the file system it read
and that file system's lookup generation and the data corrections in force; when any of them changed before it is used (a mod set, the interface
language), even while it ran, `Get` composes again; it also composes again when the worker's composition failed (a file busy then may be
readable now), so a failure that `Get` counts and logs is always one met on use. The failure counting and caching of `Get` are unchanged. The browser does not compose ahead (one thread, and a
theme's files may not have arrived yet).

What the worker touches was checked for use from a second thread. The composer keeps no state between signs except its kernel tables (already
locked, and filled per value with interlocked writes); `SignFontLibrary` and `TrueTypeFont` are only read after loading; the shared renderer
(`SignTextRenderer.Shared`) is now made once under a lock. In the file system, the remembered lookups were already safe for several threads
(PR #787: each answer carries its generation), but the open archives were kept in a plain dictionary: `GetArchive` now takes a lock. A WAD
member's decompressed bytes are published with one compare-exchange, so two readers of the same member keep one copy and count it once; the
member read itself was already locked (texture packs read on a worker too). The x87 precision of the sign arithmetic is a thread-static test
setting whose default, the original's 24 bits, holds on every thread. `LoadProfile` counts on every thread, and frame marks ignore other threads.

`ObjectSignComposeAheadTests` compose every sign of the four themes (over the game data's own file system with its remembered lookups) on the
calling thread and on the worker, and compare the two byte for byte, with the same diagnostics and log; the same for signs taken in reverse
order while the worker runs (some waited for, some composed by the caller), for the debug sign export (`--export-signs`, every PNG), and a
composition made before the lookups changed is composed again. `SignRasterCorpusHashTests` (the frozen hashes of the 75 signs) still pass.

| Measure (Release, Mac, `--perf-build --hitch-report 30`, runs alternating with the build before) | Before | After |
| --- | ---: | ---: |
| First placement of type 1104: frame / `perf.place` | 86 to 101 / 50 to 63 ms | 44 to 47 / 16 to 19 ms |
| First placement of type 1101: frame / `perf.place` | 62 to 64 / 60 to 63 ms | 17 / 10 to 16 ms |
| `object-sign` in those frames | 36 to 48 ms | under 0.5 ms |

The rest of the 1104 frame is the scenario's own search for a free site (`perf.site-search`, 27 to 29 ms, not part of a player's click) and the
part textures (`texture-decode`, 5 to 6 ms). Composing the listed types' signs reads a little more in the background, for signs of types that are listed but not
placed (measured before the kept archive files below: 15 more file opens and 108 more directory listings over the whole run); each sign keeps 128 KB
of pixels for the session, as a placed type's sign already did.

### Kept archive files (October 22, 2026)

An archive opened from a path (`WadArchive`) reads its directory once and each member on demand, and it opened the file again for every member
read, so that the file could be replaced or deleted while the archive was in use: 477 file opens before the lobby (466 of them member reads of
`lobby.wad` and `ui.wad`) and 1,561 over a park start and the build scenario. On a hard disk with an antivirus filter every open is scanned. The
archive now keeps the file its directory was read from open (`File.OpenHandle`, sharing reading, writing and deleting) and reads every member
through it with positional reads (`RandomAccess`), still one read at a time per archive under its lock, with the same length check before each
read (a file of another length is refused and its handle closed, so a read after the original is back opens it again). At most
`WadArchive.MaxOpenHandles` files stay open (64; the least recently read is closed first, and its next read opens it again); the browser keeps
none (its files are in memory, and a theme's files arrive while archives are in use). `WadArchive.CloseFileHandles` closes every kept file:
before a bonus import moves the bonus folder aside, when the game files screen stores a new game, CD or bonus folder (they apply at the next
start; the session keeps reading the old folder, so a later read reopens a file there), when the mod set is installed, when the interface language changes its archive, when a
texture pack switch begins, and when the game ends. A disposed archive closes its file.

On Windows a kept file lets other readers in (they ask for read access, which it shares) and can be moved or deleted (it shares deleting); what it
refuses is a writer that wants the file to itself, such as a tool overwriting the archive in place, until the handles are closed. Moved away or
deleted, the archive keeps reading the file its directory came from instead of failing; the next open after a close reads what is at the path.
The archive cache of a file system (`BaseFileSystem.GetArchive`) is now locked, as members are read from worker threads too (texture packs).

`WadFileHandleTests` check one open for a directory and ten reads, the reopen after a close, the bound and its least-recently-read order, that
another reader is let in and the file can be moved and deleted while kept, and reads from eight threads over six archives with two kept files and
closes in between. `OnDemandReadTests` (every member of every archive of the game folder read from the file and from memory, byte for byte) pass.

| Measure (Release, Mac, warm file cache, runs alternating with the build before) | Before | After |
| --- | ---: | ---: |
| File opens to the lobby (`--startup-profile`) | 477 | 11 |
| `wad-read` to the lobby | 9.6 to 11.0 ms (483 reads) | 1.0 to 1.2 ms |
| Main to the lobby's first frame | 542 to 630 ms | 534 to 671 ms (within the noise) |
| File opens, park start plus the build scenario (`--perf-build`) | 1,561 | 303 |
| `wad-read`, park start plus the build scenario | 22.5 to 33.1 ms (1,311 reads) | 4.1 to 6.6 ms |
| `start-level` (Jungle) | 863 to 1,036 ms | 881 to 939 ms (within the noise) |

Of the 303 opens, 191 are loose files and 76 the archives' first opens (their directories); the other 36 open an archive again after it was
closed as the least recently read. With a bound of 32 they were 68. On the Mac the saved time is small because an open costs microseconds with a
warm cache; on the issue's hard disk with an antivirus filter each of the 1,258 opens no longer made is a scan and possibly a seek.

### What is left

1. **The first start after an install or a Windows update** compiles the shaders as before and fills the cache; nothing shows that start's
   progress (the window stays black until the lobby is drawn). A loading indicator would be a visible change and is not made here.

## Procedural terrain textures (October 26, 2026)

The ground drawn with the original's procedural textures, visible cells, fog and far plane (docs/LND.md, issue #829) against the build before it (main at the merge of #834), same Mac, Release, Jungle, High preset (32 × 32 textures, a cache of 2000 cells, FIRSTPERSONVIEWDISTANCE 2),
`--offscreen --silent --mute --no-advisor --frame-stats --no-vsync --perf-run 9000 --perf-crowd 150 --perf-zoom <value>`, two rounds, the builds alternating, other sessions running (the machine was busier than for the earlier tables, so the Home numbers are higher for both builds). "Panning" adds `--perf-pan` (the camera turns half a degree a frame and its target
goes round a circle of 30 units, so the cells in view change every frame); "still" is the unchanged park camera. Frame average and p99 in ms, round 1 and round 2:

| View | Zoom value | main (average) | procedural (average) | main (p99) | procedural (p99) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Still | 110 (Home) | 1.72, 1.76 | 1.72, 1.77 | 3.10, 3.34 | 3.33, 3.37 |
| Still | 300 | 3.58, 3.51 | 56.4, 60.1 | 5.06, 5.74 | 61.2, 67.8 |
| Panning | 110 (Home) | 1.39, 1.41 | 1.51, 1.53 | 3.22, 3.21 | 3.75, 4.04 |
| Panning | 300 | 3.32, 3.34 | 71.9, 57.9 | 5.81, 5.00 | 120, 65.1 |

At the Home zoom the frame cost is the main build's within the run-to-run spread when still, and about 0.12 ms more while panning. At zoom 300 it is not: the span selection is the original's, whose rays are twice as wide as the screen, and at that zoom it lists 2500 cells (the cap of a frame, `0x56F090`), which is more than the High preset's cache of 2000, so the ring evicts every cell before it is needed again and every frame
builds 2000 textures (about 55 ms); the original does the same with its wide zoom flag, and the ground is all fog colour there at the High preset (docs/LND.md, "Fog and far plane"). The ordinary range (70 to 130) lists 900 to 1750 cells. A first view builds its cells before the frame draws (the Home view lists 898 cells and builds them in 37 ms on this Mac, 0.04 ms a cell at N = 32; the smoke's resume capture lists 1062).
The terrain computes the spans every frame (a few hundred floating point operations), looks the cells up when the original's gate lets it (docs/LND.md; in practice every frame once the level has changed), and rewrites the mesh of at most 2500 cells only when the list or a texture changed. The hole answers of the pools extension are kept per hole version.


**At the wide zoom's new far end** (the range ends where the top of the screen still shows ground before the fog, docs/LND.md; one round, Release, Jungle, `--perf-run 9000` with 150 guests; 3000 frames for the last two rows; frame average ms, p99 in brackets; "panning" adds `--perf-pan`):

| Setting, preset, cache | Zoom | Still | Panning | main at the same zoom (still, panning) |
| --- | ---: | ---: | ---: | ---: |
| 2, High (32 texels, 2000 cells) | 173 | 1.40 (2.43) | 1.47 (3.06) | 1.37, 1.38 |
| 2, 64 texels, 2500 cells | 173 | 1.40 (2.42) | 1.56 (3.34) | 1.37, 1.38 |
| 3, 32 texels, 2000 cells | 300 | 56.6 (61.1) | 54.9 (59.5) | 3.04, 2.67 |
| 3, 64 texels, 2500 cells | 300 | 2.95 (4.12) | 4.76 (17.7) | 3.04, 2.67 |
| 3, Enhanced preset with its old 2000 slots (64 texels) | 300 | 152.6 (190.6) | 148.0 (156.0) | 3.04, 2.67 |

At zoom 173 the list is about 1400 cells, which fits every cache, and the frame cost is main's. Setting 3 at zoom 300 lists the cap of 2500 cells: a cache of 2500 holds them, but the High preset's 2000 slots do not (the Enhanced preset, our own, now has the cache option 2 = 2500 slots, so it fits), so every frame evicts what it needs next and builds 2000 textures: 55 ms at 32 texels and 150 ms at 64 texels. That is the original's own behaviour for a list larger than its cache, not worked around here.

In the browser (AOT page, headless Chrome 155 with `--mute-audio`, `tools/web-perf/run.mjs --scenarios jungle --window 15`, three runs of each build, Apple M5; final code): the first view is built in the park's entering frame, whose largest frame is 76, 79.7 and 73.4 ms against 46, 48.6 and 47.2 ms of the main build, about 28 ms once (the 898 cells of the Home view); the page is ready in 9.62, 9.43 and 9.44 s against 9.42, 9.59 and 9.42 s, and the steady window
is the same (main thread busy 9.1, 8.3 and 8.7 % against 9.9, 8.3 and 8.1 %, frame p95 1.6, 1.5 and 1.5 ms against 1.7, 1.5 and 1.5 ms, 60 fps, no long task). The browser builds every listed cell before it draws, as the desktop does; there is no per frame budget and no static stand-in texture. The fog and the far plane add no measurable cost there.

**The Enhanced preset** (our own: setting 3, 64 texels, now the cache option 2 = 2500 slots; one round, Release, Jungle, 150 guests, 9000 frames; frame average ms with p99 in brackets; still / panning; main at the same zoom for comparison):

| Zoom | Enhanced still | Enhanced panning | main still | main panning |
| ---: | ---: | ---: | ---: | ---: |
| 70 | 1.19 (2.25) | 1.85 (4.74) | 1.04 (1.93) | 1.11 (2.13) |
| 110 | 1.40 (2.19) | 1.39 (3.29) | 1.39 (2.35) | 1.35 (2.87) |
| 300 | 2.99 (4.83) | 4.43 (17.4) | 2.91 (4.16) | 2.67 (4.06) |

With 2500 slots the 2500-cell lists fit the cache at every zoom and nothing is rebuilt in a steady frame. Panning at 70 and 300 costs 0.7 and 1.8 ms more than main: the lists are the largest there (about 2500 cells), so the mesh of 10,000 vertices is rewritten each frame.
