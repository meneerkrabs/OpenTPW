## Evening and night: the open points (October 18, 2026)

All five open points of the evening and night extension ([EVENING.md](../EVENING.md#the-open-points-round-october-18-2026)), presentation only: no simulation, state hash or determinism pin changes.

- **Dusk.** A stronger orange low sun (power 1.8 fade, glow tint 1.8, 0.85, 0.30), a lower and warmer ambient: green ground now reads as a darker, warm evening, paths and bricks glow. Day and night values are unchanged.
- **Ticket-booth panels.** Not self-lit: opaque near-white `gte_rof3` roof panels (albedo 221) of the park's `base.MD2`, lit and clipped to white by the street lamps beside them. `test.shader` gets a soft shoulder (brightest channel above 0.4 keeps 25 % of the excess) behind the dusk and night check; the web shader JSON was regenerated.
- **Pool lights.** First captures: the lamp and the underwater light work; the lamp's radius 55 and the underwater light's 50 flooded the lawn, so the lamp is radius 42 and the underwater light sits 14 units into the pool, 6 below ground, radius 26. `--debug-light` now honours `--capture-camera`; `--debug-extras` logs engine positions.
- **Browser lamps.** `tools/web-perf/run.mjs --evening-lamps <n>` (hook `PerfEveningLamps`, shared with the native option through `Level.SetDebugEveningLamps`). AOT build, 3 runs: +50 and +200 lamps cost nothing measurable, +500 about 0.4 ms p50 and 1.2 ms p95; no code change.
- **Particles and sky.** The pool spray and the world's alpha particles take the sprite tint. The park sky is never in view at any zoom (captures at 40 and 300), so its tint needs no judgement.
- **Tests.** Three new `EveningTests` (dusk warmth, the shoulder, the particle tint); the lamp-on-model test allows the underwater light below the object's ground.
