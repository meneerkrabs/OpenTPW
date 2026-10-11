# Browser performance

OpenTPW shares its game code between native builds and the browser. The browser's
WebAssembly runtime and WebGL bridge run much of that work on the page's main thread.
A task that occupies that thread also postpones drawing and input.

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

After removing the archive collections, cold Jungle construction still occupied one
callback for roughly 16.5 seconds (17.9 seconds before). Advisor response 587 now
prepares in 87.9 ms, including 76.6 ms synchronous PCM decode; the first response
measured 223.5 ms of PCM decoding. Those first-use costs are outside the matched steady
windows and are not claimed as eliminated. Occasional smaller frame gaps remain as well. Broad visibility culling, ahead-of-time compilation
and full scene-material lifetime work are separate optimization opportunities.

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
