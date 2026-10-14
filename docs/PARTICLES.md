# Particles

Ride scripts' `EVENT` types 1 and 2 start effects of the original particle system. OpenTPW reads the
effect templates from the game data, simulates the pool the way the Mac build's pool is traced (static
reading of the Feral PPC binary, with the Windows `tp.exe` twins as medium-confidence cross-checks) and draws
the particles as camera-facing sprites. How the key operand becomes a position and direction is in
[PARTICLE-KEYS.md](PARTICLE-KEYS.md); this page is everything after that.

Particles are presentation only. They are not part of the canonical simulation, the world hash or any
save (PTCL-907), and they use their own generator, so the determinism tests do not see them.

## Data

`Data/Particle/Tp2.plb` (`ParticleTemplateFile`, `source/OpenTPW.Files/Public/ParticleTemplateFile.cs`),
little-endian:

| Part | Content |
| --- | --- |
| header | `u32 105`, `u32 320` |
| templates | 105 records of 320 bytes (`ParticleTemplate`) |
| effectors | `u32 20`, `u32 104`, 20 records of 104 bytes (read, not simulated, PTCL-903) |
| trailer | `u32 33`, `u32 1024` (unused by the Mac build; optional here) |

`Data/Particle/par_lib.h` names the ids (`P_EFFECT_Sparks 1`, `Smoke 2`, `Firework1 3`, ...). The reader checks
the counts and sizes against the file length and rejects anything else. Template fields used (offsets in the
320-byte record; `[BIN:STP-PPC:0x1009BA44]`):

| Offset | Field |
| --- | --- |
| +0x10 / +0x12 | death effect (i16, negative = none) / emitter velocity jitter |
| +0x20 | emitter life in ticks; +0x26 immortal flag |
| +0x24 / +0x28 | emitter drag / gravity; +0x2C emitter velocity (3 x i32); +0x76 ground bounce |
| +0x38 | base particle velocity (3 x i32); +0xB0 the speed of a type-2 effect |
| +0x44 | spawn volume extents (3 x i32), shape +0xC2 (0 box, 1 ellipsoid) |
| +0x50 / +0x58 / +0x5C | velocity jitter / random colour / radial burst speed |
| +0x60 / +0x64 / +0x68..6B | burst, per-emitter cap, per-quarter-of-life rate (negative: one particle per \|n\| ticks) |
| +0x70 | render flags: 0x2004 additive, 0x4 one-plus-one |
| +0x74 / +0xA6 | size at birth / at death |
| +0x78 / +0x7C / +0x80 / +0x84 | particle life (+-25%) / drag per 1024 / gravity (Y up) / random acceleration |
| +0x94 / +0x96 | sprite code and frame count (0 = untextured) |
| +0x98 / +0xB4 / +0xB8 | per-particle death effect / chained child effect / child follows the parent |
| +0xA2 | control, the draw-time spatial scale in permille (loader default 1000) |
| +0xBE | spin per tick, in 1/65536 turn |
| +0xC0 / +0xC1 | not scaled by density / refused under low detail |
| +0xD8 | 16 colours `0xAARRGGBB`, index 15 at birth, 0 at death |
| +0x118 | name |

Verified on the shipped file: 105 templates and 20 effectors; Sparks is a 12-tick emitter with radial
speed 37, rates 5/3/1/1, particle life 8 and an untextured orange-to-yellow fade; Firework1 is a rocket
emitter (vy 109, gravity 2, drag 7) that spawns FW1explosion (60) at its death, which chains LaserRing (63);
Smoke is immortal with sprite 11 (16 frames) and size 800 to 2200. `ParticleSystemTests` reads the file
when `OPENTPW_GAME_PATH` is set.

## Sprites

The sprite code is `bank * 16 + animation` of the `particles` set: `esprites.wad/Generic/Particles/SPR_PA`
(bank 0) and `SPR_PB` (bank 1), each an `.ESP` animation table with a `.TPC` frame bank. The frame counts of
the animations match the templates (code 11 and 12 are 16 frames, 5 is 14, 3 is 8, 13 is 6, 14 is 9, 15 is 1;
`[BIN:STP-PPC:0x1006AC2C]`, medium confidence). `ParticleSpriteAtlas` packs both banks and a white block
(for untextured particles) into one RGBA texture like the guest sprite atlas does. A particle's frame advances
over its life.

## The pool

`ParticleSystem` (`source/OpenTPW/World/Particles/ParticleSystem.cs`) mirrors the native pool
(`[BIN:STP-PPC:0x1009EE70, 0x1009EF70]`): 120 emitter slots, 2048 particles (clamped 100..32767), 20 effector
slots, a private Microsoft LCG (`state * 214013 + 2531011`, value `>> 16`) seeded 1 at every reset, all integer
state. It needs no GPU, so tests build one directly.

- **Create** (`[0x1009FD70]` type 1, `[0x1009F968]` type 2): refuses ids outside 0..104 and, with the low-detail
  flag, templates with byte +0xC1; takes a free slot (120 maximum); stores the position `>> 4` (the dispatcher
  passes original units x 1024, the pool keeps 1/64 unit); jitters the emitter velocity; scales burst, cap and
  rates by the density unless +0xC0; emits the burst; chains the child emitter (+0xB4). Type 2 first replaces the
  base particle velocity with `direction * template[+0xB0] / 1024` (`[0x1009FB54]`). The handle is
  `slot | generation << 16`; the EVENT dispatcher discards it, so effects are fire and forget and do not follow
  the node (`[0x100AF9E8, 0x100AEA34]`).
- **Tick** (`[0x1009F748]`, every 31 ms, the very first call does nothing): emitters newest first (life,
  death effect at life -1, child follow, motion with the ground bounce, emission), then particles (age, colour,
  frame, size, spin, random acceleration, drag, gravity, then `pos += v`, a particle does not move on its spawn
  tick; a particle death starts its +0x98 effect). A finished emitter frees its slot once it has no particles.
  `RSE KILL` is life -2: no death effect (`[0x100AE718]`).
- **Advance** converts wall-clock frame time into whole ticks with a carried remainder, capped at a quarter
  second per frame so a stalled frame does not replay seconds of particles.
- **Draw view** (`Snapshot`): the live particles in emitter slot order with colour (the palette at
  `life * 15 / maxLife`, with random colour keeping the palette's alpha and taking another entry's colour), frame,
  size, spin, render flags and the control scale `drawn = emitter + (particle - emitter) * control / 1000`
  (`[engine_shared 0x1ACC8]`), plus per-emitter bounds for culling.

## Drawing

`ParticleRenderer` (`source/OpenTPW/World/Particles/ParticleRenderer.cs`, shader
`content/shaders/particle.shader`) is modelled on the weather renderer: one dynamic vertex buffer sized for the
pool, camera-facing quads with half-size `size / 1024` original units (PTCL-904), rotated by the spin, and three
batches, one per blend: the sprite's own alpha blend (also all untextured particles), source-alpha-over-one for
flags `0x2004` and one-plus-one for flags `0x4` (`MaterialFlags.OneOneBlend`). Depth testing is on, depth writes
and face culling are off, emitters outside the view are culled by their bounds. Vertex colour is packed into the
normal slot (red, green, blue plus 256 x alpha as whole floats) so the existing vertex format and the web
shader path need no integer attribute. The precompiled web shader is `content/shaders/web/particle.json`
(`WebShaderTests`).

The renderer is created by the first request of a level and deleted with the level's entities, which clears the
pool at park teardown. An object that stops does not kill the effects it already started.

## Wiring

`OriginalObjectRuntime.ObserveParticleEvent` prepares a request (owner, script, key, effect id, control 1000,
frozen engine position, unit direction for type 2; see PARTICLE-KEYS.md) and raises
`OriginalObjectRuntime.ParticleRequested` for prepared requests only. `ParticleRendererModule` (module
initialiser, like the weather) converts the engine pose to native units, inverting the placement
(0.2 engine units per original unit, the park heightfield's centring, engine Z-up to original Y-up; PTCL-906),
and calls `Create` or, for type 2, `CreateDirected` with the direction as `unit * 1024`. Refusals (negative or
unresolved key, scoped effect, headless runtime) start nothing.

In the shipped jungle, the Aztec Mayhem torches (`EVENT 2` key 5 and 8, effect 22) are the first scripts to
fire in a plain park: the request reaches the pool (seen in a native smoke run); the effects themselves were drawn in
the same run by starting templates directly, since a torch's short effect is hard to catch in a capture.

## Not done

Registered as approximations in the fidelity register (`PTCL-900`, `PTCL-902` to `PTCL-908`):

- The arithmetic of the template fields is modelled from the template bytes and the traced summary, not from
  the instructions (units, drag and gravity signs, jitter ranges, volume shapes, spin, random colour).
- Effectors and the emitter spiral are not simulated.
- Sprite scale and UVs, the draw order and depth sorting are not traced.
- The density setting, the particle total and the terrain-height callback are not wired.
- The PART save block is not restored.
- Only EVENT 1/2 start particles. The other native callers are not wired: ride broken and fixed smoke
  (`0x1001E150`, `0x1001E388`), ride over (`0x10025738`), flying cars (`0x10066F58`), litter (`0x100D8548`),
  RSSE spawn and SPARK (`0x100ACF24`, `0x100B2BA0`), `0x10183BEC` to `0x10184028` and `0x1014552C`.
- EVENT effects that are immortal (for example Smoke, id 2) never end on their own because the handle is
  discarded, as in the original; no literal EVENT in the shipped scripts uses one.

## Verification

```sh
dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --filter "FullyQualifiedName~ParticleSystemTests|FullyQualifiedName~ParticleSpriteAtlasTests|FullyQualifiedName~ParticleEventRequestTests"
OPENTPW_GAME_PATH=/path/to/theme-park-world dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --filter FullyQualifiedName~Particle
OPENTPW_NATIVE_SHADER_TESTS=1 dotnet test source/OpenTPW.Tests -r osx-arm64 --filter WebShaderTests
python3 tools/fidelity_register.py --check
```

The synthetic cases cover the file layout and its rejections, creation limits, burst and rate counts, density,
life jitter, colour and frame progression, the spawn tick, drag and gravity, type-2 velocity, the control scale,
emitter motion and bounce, death and kill, chaining and following, slot and pool exhaustion, deterministic
replay and the engine-to-native conversion. A visual check draws effects in the native jungle smoke test
(`--smoke-test --load-original-level jungle --capture-world`).
