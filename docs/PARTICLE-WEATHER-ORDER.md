# Park particle/weather submission order

This bounded #582 change ensures that the park's particle renderers are submitted
before its weather renderers, following the established scene-pass annotation in
`ParticleRenderer` (`0x576a00`, particles `0x57C620`, weather `0x57FF60`).

Previously `Level.Render` submitted the entity snapshot in registration order.
Placed/imported particles can register before weather, but a later script/advisor
request can register particles after weather. Resume also constructs weather before
restoring a particle pool when no placed effect has already created that pool.
Thus the relative order depended on the load path and first effect request.

`Level.WeatherAfterParticles` defers only weather entries before the last particle
entry until immediately after it. It preserves the relative sequence of every
non-weather entry and of all weather entries. Already-ordered snapshots and those
without particles are unchanged. The entity registry, update order, per-pass
snapshot behavior, materials and exception propagation are unchanged.

Synthetic tests assert exact entity identities for interleaved and unchanged
snapshots using the actual sealed renderer types without invoking graphics.
Real `Level.Render` tests cover snapshot mutations and exception propagation.
These are source-level tests pending exact-head CI, not rendered image evidence.

PTCL-909 remains open for native HUD/advisor ordering. The lobby render loop is
unchanged. Depth/blend fidelity and rendered weather/particle overlap still need
separate visual acceptance; this change does not establish full layering parity.

## Translucent world parts (issue 829)

`Level.Render` now applies `Level.WorldPassOrder` after `WeatherAfterParticles`: translucent terrain and object parts (`ModelEntity.Translucent`, the original's deferred list 0x582AD0)
draw after every other entity, farthest first, and the particle and weather renderers follow them in the order this note describes. A snapshot without translucent parts is unchanged. The original also defers the particle batches
whose state is 0x556 or 0x200552 into that same far-to-near list; OpenTPW keeps the particles a pass of their own (PTCL-905 is not narrowed by this).
