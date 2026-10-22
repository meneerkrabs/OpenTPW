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

### What is left

1. **The remaining placement cost is the sign.** A ride with a sign composes it on its first placement (`object-sign`, 36 to 46 ms on the Mac for
   types 1104 and 1101, on the frame of the click), and its textures decode then (`texture-decode`, 5 to 11 ms). Composing the sign of a type
   when the buy window first lists it, off the render thread, would take it out of the placement frame; the composer and the file system it reads
   would have to be checked for use from a worker first.
2. **GLSL to SPIR-V on every start** (`shader-spirv`, 343 to 348 ms for 7 files before the lobby, 9 files and 450 ms with a park, and a 52 ms
   frame the first time the buy window's turning preview draws). The result depends only on the shader source and the compiler, so it could be
   kept in the cache folder between runs; on Direct3D 11 the compiled HLSL could be kept the same way.
3. **Directory listings.** The desktop's game-data file system does not remember its path lookups (`CacheLookups`, which the browser uses), so
   each read lists the directories along its path again: 1,063 listings before the lobby, 15,770 for a park start, 300 to 480 for a first
   placement. Each is cheap on a warm cache, but on Windows every listing is a directory enumeration in the kernel.
4. **One file open per WAD member read.** A member read opens the archive again (so a bonus import can replace the file on Windows): 466 opens
   before the lobby, 1,311 in a park start. On a hard disk with an antivirus filter each open has a cost.
