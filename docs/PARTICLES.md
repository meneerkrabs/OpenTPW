# Particles

Ride scripts' `EVENT` and `ADDOBJ` types 1 and 2 start effects of the original particle system. OpenTPW reads the
effect templates from the game data, simulates the pool the way the Windows `tp.exe` does (ported from its traced
instructions, `[BIN:TPW-EXE:...]`; the Mac build's pool is the cross-check) and draws the particles as camera-facing
sprites. How the key operand becomes a position and direction is in [PARTICLE-KEYS.md](PARTICLE-KEYS.md); this page is
everything after that.

Particles are presentation only. They are not part of the canonical simulation or the world hash, and they use their
own generator, so the determinism tests do not see them. A park-resume save carries the pool's emitters and effectors,
not its particles, as the original's save does (see "Saves").

## Data

`Data/Particle/Tp2.plb` (`ParticleTemplateFile`, `source/OpenTPW.Files/Public/ParticleTemplateFile.cs`),
little-endian:

| Part | Content |
| --- | --- |
| header | `u32 105`, `u32 320` |
| templates | 105 records of 320 bytes (`ParticleTemplate`) |
| effectors | `u32 20`, `u32 104`, 20 records of 104 bytes (`ParticleEffectorTemplate`) |
| trailer | `u32 33`, `u32 1024` (unused by the Mac build; optional here) |

`Data/Particle/par_lib.h` names the ids (`P_EFFECT_Sparks 1`, `Smoke 2`, `Firework1 3`, ...). The reader checks
the counts and sizes against the file length and rejects anything else. An emitter slot is a full copy of its 320-byte
template (tp.exe `0x521E60`), so the template offsets are the emitter offsets, and each accessor reads the width the
native code reads (word, byte or dword). Template fields used:

| Offset | Field |
| --- | --- |
| +2 / +0xE | hold word (the emitter does not tick) / ring-emitter mode; neither is used by the data |
| +4 / +5 | effector group (signed byte) / screen-space flag |
| +0x10 / +0xBB | death effect (word, negative = none) / the death effect is an effector |
| +0x12 | emitter velocity jitter (word; -1 in 92 templates: adds nothing, still draws) |
| +0x20 / +0x26 | emitter life in ticks / immortal (word) |
| +0x24 / +0x28 | emitter drag (word) / gravity (word); +0x2C emitter velocity (3 x i32); +0x76 ground bounce (word) |
| +0x38 | base particle velocity (3 x i32); +0xB0 the speed of a type-2 effect |
| +0x44 | spawn volume extents (3 x i32); byte +0xC2 != 0 is a box, 0 a horizontal disc; word +0xA8 makes the disc a ring; word +0xA0 draws y unsigned; +0xAC rotates the volume about Y (4096 per turn) |
| +0x50 / +0x58 / +0x5C | velocity jitter (word) / colour mode (0 life palette, odd flicker, even keeps the spawn colour) / radial burst speed |
| +0x60 / +0x64 / +0x68..6B | burst, per-emitter cap (word), rate bytes (negative: one particle every \|n\| ticks) |
| +0x6C / +0x6D | add the emitter velocity / random initial rotation |
| +0x70 | render flags: 0x2004 additive, 0x4 one-plus-one |
| +0x74 / +0xA6 | size at birth / at death (words) |
| +0x78 / +0x7C / +0x80 / +0x84 | particle life (jitter +-(L >> 2) exclusive) / drag per 1024 / gravity (Y up) / random acceleration (3 x i32) |
| +0x94 / +0x96 | sprite code and frame count (0 = untextured) |
| +0x98 / +0x9C | per-particle death effect (dword) / skipped by every-group effectors |
| +0xAA / +0xBC | particles die with a dead emitter / a dead emitter sets its child's life to -1 |
| +0xB4 / +0xB8 / +0xBA | child effect (dword) / the child follows and starts at parent + its own +0x2C vector x 16 / the child is an effector |
| +0xA2 | control, the draw-time spatial scale in permille (loader default 1000) |
| +0xBE / +0xC6 | spin per tick / upper bound of the random spin step (words, 1/65536 turn) |
| +0xC0 / +0xC1 | not scaled by density / refused under low detail |
| +0xC4 / +0xC8 | spiral radius (word) and angle step per tick |
| +0xD8 | 16 colours `0xAARRGGBB`, index 15 at birth, 0 at death |
| +0x118 | name |

Effector records (104 bytes, copied into a slot at `0x522360`): +4 target group (-1 = all), +0xB bounce, +0x18 velocity,
+0x24 drag, +0x28 gravity, +0x2C kill radius, +0x30 mode (2 = kill inside the radius), +0x34 / +0x38 / +0x3C target
jitter, +0x40 strength, +0x44 refused under low detail, +0x45 killed on load, +0x46 death effect (word) and +0x49 whether
it is an effector, +0x4A immortal, +0x4C life.

Verified on the shipped file: 105 templates and 20 effectors; Sparks is a 12-tick emitter with radial
speed 37, rates 5/3/1/1, particle life 8 and an untextured orange-to-yellow fade; Firework1 is a rocket
emitter (vy 109, gravity 2, drag 7) that spawns FW1explosion (60) at its death, which chains LaserRing (63);
Smoke is immortal with sprite 11 (16 frames) and size 800 to 2200. `ParticleSystemTests` and
`ParticleSystemPortTests` read the file when `OPENTPW_GAME_PATH` is set: no template uses the ring emitter, the hold
word or the ground bounce; the box volume is on 75-82, 89 and 99, the ring on 90, the spiral on 51, 54 and 87; the
effector children are 84, 86, 90, 91 and 98 and the one effector death effect is 87 Key's; every template runs
deterministically and a finished firework frees its slots.

## Sprites

The sprite code is `bank * 16 + animation` of the `particles` set: `esprites.wad/Generic/Particles/SPR_PA`
(bank 0) and `SPR_PB` (bank 1), each an `.ESP` animation table with a `.TPC` frame bank. The frame counts of
the animations match the templates (code 11 and 12 are 16 frames, 5 is 14, 3 is 8, 13 is 6, 14 is 9, 15 is 1;
`[BIN:STP-PPC:0x1006AC2C]`, medium confidence). `ParticleSpriteAtlas` packs both banks and a white block
(for untextured particles) into one RGBA texture like the guest sprite atlas does. A particle's frame advances
over its life.

## The pool

`ParticleSystem` (`source/OpenTPW/World/Particles/ParticleSystem.cs`, the save part in `ParticleSystem.State.cs`) is
the native pool, rule by rule. It needs no GPU, so tests build one directly, and a tick allocates nothing.

- **Init** (`[BIN:TPW-EXE:0x51FAA0]`): the game calls it with no ground function (the pool then uses the stub `0x5D60C0`
  that returns 0, a flat plane at Y 0), the options' PARTICLEDENSITY and a count of `0x800` (`0x54E5F9`, `0x54EAB9`);
  the count is clamped 100..0x7FFF and a density of 0 becomes 1. The density is GameOptions.PARTICLEDENSITY of the detail
  object (`0x7858C8`), which only the detail-file load fills (`0x44DE20` -> `0x44DC60`: the player's detail level picks
  `low.sam` 500, `med.sam` 1000 or `high.sam` 1500); OpenTPW passes the value of the player's graphics preset (Enhanced and
  Custom included, `ParticleSystem.OptionsDensity`) and 500 when no options are loaded.
  TOTALPARTICLES has no code reference in tp.exe. The reset (`0x51FB70`) sets the generator to 1, both generation
  counters to 1 and the free lists ascending from slot 0 / particle 0.
- **Random forms** (`[BIN:TPW-EXE:0x5205D1, 0x5205F2]`): the generator is `state * 214013 + 2531011`. The signed form
  `sar(state, 16) % n` gives -(|n| - 1)..|n| - 1 with zero twice as likely; the unsigned form
  `(state >> 16 & 0x7FFF) % n` gives 0..n - 1; a draw is consumed only when n is not zero, so an emitter jitter of -1
  adds nothing but still draws three times.
- **Density** (`0x521D60`): each non-zero rate byte becomes `(rate * density) >> 10`, at most 127 and at least 1
  (negative rates are scaled too, with a floor shift: a higher density makes them faster); the cap (word) and the burst
  are scaled the same way only when positive, at least 1. 1024 is the templates as authored.
- **Create** (`0x521E60` type 1, `0x521930` type 2): ids 0..0x68, low-detail templates refused; the slot comes from
  the head of a LIFO free list and is pushed on a newest-first active list; the handle is `slot | generation << 16`
  with one global generation counter (a freed slot's generation is cleared, so stale handles die even when the slot is
  reused); the position is stored `>> 4` (1/64 unit); type 1 adds `sar % j` per axis to the emitter velocity, type 2
  sets the base velocity to `direction * template[+0xB0] / 1024` truncated and draws nothing; the burst calls spawn
  directly, with no cap check. The child (+0xB4, not when it is the template's own id) starts at the parent's position,
  plus the child template's +0x2C vector x 16 when +0xB8 is set, in which case it never runs its own motion; an
  effector child (+0xBA) is created by `0x522360` the same way.
- **Emitter tick** (`0x520130`): the first call does nothing; emitters newest first, then the effectors, then the
  particles. Life decrements unless immortal (and keeps decrementing below zero). Dead (life < 0): the death effect
  starts at exactly -1 and is then cleared, +0xBC sets the child's life to -1 (a child that ticked earlier in the same
  tick decrements it to -2 before seeing -1, so only an immortal child fires its death effect), and the slot is freed
  once the live count is 0; a dead emitter does not follow, move or emit. Alive: follow copies the position into the
  child, the spiral angle advances by +0xC8 (mod 4096), motion runs unless the emitter is a following child, and
  emission happens only when the live count is below the cap, once per tick: the rate byte comes from
  `(life << 2) / initial life` (0 -> +0x6B, 1 -> +0x6A, 2 -> +0x69, otherwise +0x68, so immortal emitters stay on
  +0x68); a positive rate spawns that many particles with no per-particle cap check, a negative one uses the emitter's
  counter (decrement; at 1 or below spawn one and reload |n|).
- **Emitter motion** (`0x520E00`): `v -= trunc(v * drag / 1024)`, `vy -= gravity`, `pos += v`; the ground branch runs
  only for the bounce word (none of the data) and halves the speeds.
- **Spawn** (`0x520560`): life `L + sar % (L >> 2)` (maximum 1 when 0), rotation `u & 0x7FFF` when +0x6D, colour
  `palette[u & 15]` when +0x58 is not zero else palette[15]. With all extents zero the particle starts at the
  emitter; otherwise a box (byte +0xC2) draws x and z signed and y signed (unsigned with word +0xA0), while the
  default is a horizontal disc: a 12-bit angle (table index `>> 3` of the 512-entry table `trunc(sin * 256)`), x =
  cos * (u % ex) / 256, z = -sin * (sar % ez) / 256, and with word +0xA8 a ring x = cos * ex / 256, z = -sin * ez / 256;
  every volume is then rotated about Y by +0xAC / 4096 turn and added to the emitter position; the spiral adds
  `sin(angle) * r / 256` to x and `cos * r / 256` to z. A radial burst (+0x5C) is horizontal and replaces the base
  velocity: x = cos * (u % R) / 256, z = sin * (u % R) / 256, y = sar % R; otherwise the velocity is base +
  `sar % j` per axis (word +0x50). The emitter velocity is added when +0x6C is set.
- **Particle tick** (`0x5214A0`): life decrements and a particle below zero dies, starting its +0x98 effect where it
  was; a dead emitter with +0xAA sets the life to -1. Colour: life palette `palette[life * 15 / max]`, an odd +0x58
  draws `u % 16` every tick, an even one keeps the spawn colour. Frame `(n - 1) - (n - 1) * life / max`, size
  `(birth - death) * life / max + death`, spin step `BE`, or `BE + u % (C6 - BE)` when they differ. Random acceleration
  `sar % a` per axis, then drag with truncating division, then gravity, then the move; the spawn tick (state 2) does
  everything but the move. Bounds grow by position +- size >> 10 (OpenTPW keeps them for screen-space emitters too, for
  culling).
- **Effectors** (`0x522360` create, `0x520F10` tick): life, then motion (drag, gravity, ground bounce with a /4 side
  branch) unless the effector follows its parent, then death (the death effect starts and the slot frees) or attraction:
  the target is the position plus `sar % jitter` per axis (one draw per non-zero axis per tick), and for every particle
  of a matching group (the emitter's and the particle's group equal the effector's, or any group whose template +0x9C
  is 0 for effectors of group -1) with `d = (target - p) / 4`, `q = (d . d >> 8) + 10` the velocity gains
  `strength * d / q`; mode 2 sets the life of a particle with `q < radius^2 >> 8` to 0. The native create indexes the
  effector table with the whole handle when it marks a following effector child; OpenTPW uses the slot.
- **Advance** converts wall-clock frame time into whole ticks with a carried remainder, capped at a quarter second per
  frame so a stalled frame does not replay seconds of particles.
- **Draw view** (`Snapshot`): the live particles in emitter slot order 0..119, each slot's particles newest first, with
  colour, frame, size, spin, render flags and the control scale `drawn = emitter + (particle - emitter) * control /
  1000` (`[BIN:TPW-EXE:0x51EF30]`), plus per-emitter bounds for culling.

## Drawing

`ParticleRenderer` (`source/OpenTPW/World/Particles/ParticleRenderer.cs`, shader
`content/shaders/particle.shader`) is modelled on the weather renderer: one dynamic vertex buffer sized for the
pool and camera-facing quads rotated by the spin. The half-height is `size / 1024` original units for every particle
(`[BIN:TPW-EXE:0x57C620]`: `(size << 1) * (1/1024) * 0.5`); only the width gets a factor, the frame record's float
+8 for a sprite and `DAT_008BCBCC`, the display's height / width (`0x582EB0`), for an untextured quad in the original,
which OpenTPW replaces with the frame's pixel aspect and 1 (`APPROX:PTCL-904`; the +8 is the frame's width / height, `0x588660`). Screen-space templates (byte +5) are drawn in clip
space without the camera and without depth, in slot order with the world particles (one loop, `0x57C620`): the draw list maps their x and z to the screen
(`0x51F01A`, `ParticleScreenSpace`, docs/LIPS.md "Screen effects"), the half-height is (size >> 6) / 2048 screen units and the
quad turns in 1/256-turn steps (`0x591430`); OpenTPW's width factor there also takes the target's height / width (PTCL-904). Runs of one blend in the original's order: source-alpha-over-one for flags
`0x2004`, one-plus-one for flags `0x4` (`MaterialFlags.OneOneBlend`), else the sprite's own alpha blend; the flags apply
to untextured particles too, as in the original. The original submits one primitive per particle, slots 0..119 and
newest particle first, each with its own state word; OpenTPW keeps that order and merges neighbours of one blend into a draw, depth-tested without depth
writes and without face culling. The original's state words (decoder `0x567620`) keep depth writes on unless flags
`0x4000` add `0x800`, draw an untextured particle opaque (`0x1256`) when its alpha byte is 0 or >= 0xF0 and by alpha
with an alpha test otherwise (`0x556`), and depth-sort the two triangles of a particle's own batch when the state lacks
`0x1000`/`0x2000` (`0x565590`); `APPROX:PTCL-905`. Emitters
outside the view are culled by their bounds. Vertex colour is packed into the normal slot (red, green, blue plus 256 x
alpha as whole floats) so the existing vertex format and the web shader path need no integer attribute. The
precompiled web shader is `content/shaders/web/particle.json` (`WebShaderTests`).

The renderer is created by the first request of a level (or by a resumed park that held live emitters) and deleted
with the level's entities, which clears the pool at park teardown; renderers are registered per level, so a rolled-back
resume candidate leaves the live park's pool alone. When an object stops (sold, demolished) its ADDOBJ records end their
effects, as the native destructor does (`0x558703` walks the object's list and sets life -2); effects an EVENT started have
no record and run on.

## Wiring

`OriginalObjectRuntime.ObserveParticleEvent` prepares a request (owner, script, key, effect id, control 1000,
frozen engine position, unit direction for type 2; see PARTICLE-KEYS.md) for RSE `EVENT` 1/2 and raises
`OriginalObjectRuntime.ParticleRequested` for prepared requests only (`[BIN:TPW-EXE:0x552615]`: the dispatcher
`0x5573D0` runs with control 1000 and the handle is discarded). `ObserveParticleObject` does the same for RSE `ADDOBJ`
type 1/2 (`type key effect slot`, `0x551F5F`) through `OriginalObjectRuntime.ParticleStarter`, which returns the pool
handle (any non-zero value counts, a generation from 0x8000 included): the runtime keeps it under the slot operand,
drops records whose emitter ended, and `KILLOBJ slot` ends every record of the slot with the pool's
life -2 (`0x5522C5` -> `0x51FF70`; no death effect). `ADDOBJ_EXT` (`type key effect control slot`, `0x5520E8`) is the
same with its control operand. Each `Simulate` then moves the kept effects with a key of 0 or more to the key node
(`ParticleFollower`: the position truncated to whole units, `<< 10`; a type-2 effect turned to the node's direction,
`0x51FD90`), as the object pass `0x5516B0` does on every run. A negative key starts the effect at the footprint box middle
at the base height (`0x556B90` -> `0x466B70`, the EVENT sounds' anchor). `ParticleRendererModule` (module initialiser, like the weather)
converts the engine pose to native units, inverting the placement (0.2 engine units per original unit, the park
heightfield's centring, engine Z-up to original Y-up) and truncating `world * 1024` as the original's `_ftol` does
(see "Pose conversion" below), and calls `Create` or, for type 2, `CreateDirected` with the direction as `unit * 1024`; a control
other than 1000 is set after creation (`0x51FFD0`). A key entry without a matrix (`0x556BF1`) starts the effect at the
world origin, with direction (0, 0, 1024). Refusals (negative or unresolved key, scoped effect, headless runtime) start
nothing.

In the shipped jungle, the Aztec Mayhem torches (`EVENT 2` key 5 and 8, effect 22) are the first scripts to
fire in a plain park: the request reaches the pool (seen in a native smoke run); the effects themselves were drawn in
the same run by starting templates directly, since a torch's short effect is hard to catch in a capture.

## Saves

The original saves the pool in its `PART` container (`0x51F680`, tag `PTCL`): the pool globals, the generator, the 120
emitters, the 20 effectors, the list heads and the templates, with a particle array of length 0 (the game save passes 0,
`0x41661E`). On load (`0x51F7A0`) the block comes back, the particle pool is rebuilt empty and every live count is 0;
the post-load pass (`0x5200B0`) kills screen-space emitters (life -2, hidden) and effectors flagged +0x45. So world
emitters, including immortal smoke, their generator and their handles survive a save and load, but their particles
restart empty.

OpenTPW's park-resume envelope (docs/PARK-SAVES.md) carries the same thing as an optional part, `Particles`
(`ParticleSystemState`: the pool configuration, generator, both generation counters, tick counter, live emitters in active-list order,
live effectors and free lists' order, no particles and no templates; the file's templates are read again).
`ParticleSystem.Capture` retains every initialized pool, including an empty one: its settings, generator and allocation history
still determine the next new effect. A host without a particle renderer writes `"Particles": null`; older files without the part
still load. Its optional `Configuration` carries signed density, capacity and the low-detail flag's zero/nonzero meaning. Absent or
null configuration in older own-format saves keeps the host's current settings. Present configuration replaces them before the
empty particle free list is rebuilt; existing saved emitters' rates/cap/burst are not rescaled, while future emitters use the saved density.
Density is restored literally, including zero and signed 32-bit extremes, using the existing native wrapped arithmetic. Capacity must
be 100..32767, an explicit managed allocation boundary matching native initialization, not a claim that native load rejects other counts.
See [the bounded SAVE-013 evidence](reverse/PARTICLE-POOL-SAVE-CONFIGURATION.md). `ParkResumeSaveFile.ValidateShape` checks the slot
counts, ranges, generations and configuration; `Level.CaptureResumeSnapshot` captures it through the renderer module and
`RestoreResumeSimulation` rebuilds the renderer and restores the pool (`Level.ParticlesCaptured` / `ParticlesResumed`).
It is presentation: it is not in the world hash, so the hash schema and `DETERMINISM.md`'s conditional extension rule do
not apply, and the envelope version is unchanged. The runtime snapshot also keeps the object's `ADDOBJ` records (slot, handle, and the optional type and key the follow needs, absent in older saves, whose records then stay where they are; the pool's generations and counters
restore exactly), so a resumed effect can still be ended by its `KILLOBJ`. `ValidateState` bounds the emitters' lives,
rate bytes, control, cap and counters; a saved pool the template file cannot rebuild is dropped with a warning instead of
failing the resume.

## Object effects: build, demolish, repair and upgrade

Every category `.sam` (`Info.CreateParticleEffect`, `DestroyParticleEffect`, `RepairParticleEffect`,
`UpgradeParticleEffect`; schema `0x745020`/`0x74505c`/`0x745098`/`0x7450d4`, type record `+0x60`/`+0x64`/`+0x68`/`+0x6C`)
names four effects of the template file. Jungle rides use 79 / 75 / 51 / 89, shops 81 / 77, sideshows and upgrades 80 / 76
and features 79-82 / 75-78; shops, sideshows and features have no repair or upgrade key, and 0 starts nothing.
`ObjectCatalogEntry` reads the four keys and `OriginalObjectRuntime.ParticleEffectId` returns them.

| Effect | tp.exe | Trigger | Handle |
| --- | --- | --- | --- |
| Create | `0x5587F0` (`0x558e7c`) | a new object's script attaches (`Level.PlaceObject`); not while a park loads (`DAT_00785a2c`) | dropped |
| Destroy | `0x559060` (`0x5590f7`) | the removal `0x4dd0a0` passes flag `0x2` unless a park loads or clears (`Level.RemoveObjectAt`, before the object goes) | dropped |
| Repair | `0x556DE0`, from the REPAIREFFECT handler `0x5562bd` before its sounds | value 1 starts it, value 0 ends it (life -2); the script's end ends it too (`0x55854b`) | kept at the script's `+0xCC` |
| Upgrade | `0x55A100`, from `0x4df8f0` | the upgrade is installed (`ParkEventKind.UpgradeCompleted`) | dropped |

All four start at the footprint box middle (`0x466B70`: the packed cell rectangle at object `+0xC0`/`+0xC4`, corners x 10, one
cell added to the far corner), at the object's base height, converted like every other request (x 1024 into the pool's
`>> 4`). Create, Destroy and Upgrade then size the emitter's spawn volume with `0x520030` to the footprint:
`(width * 512, (y1 - y0) * 256, depth * 512) >> 4` for the box, width and depth in original units (10 per cell), so the stars
spread over the footprint; `ParticleSystem.SetVolumeExtent` replaces the extent after the creation burst, which ran with the
template's. Repair keeps its template's volume. A fixed item has no footprint box in OpenTPW and starts nothing (the original's `0x559060` would create the effect anyway, at coordinates the box call leaves unset, but no shipped fixed item names a type effect; `PTCL-908`). The removal's mode in which the global at `+0x1da738` is 2 (no effect) is the world's clear and reset loops (`0x515dd0`, `0x515fb0`), which OpenTPW's teardown does not run through the player's removal; headless hosts have no
pool; the vertical extent is the model box's height (record `+0x1C`/`+0x28`, which `0x451640` copies into the model header's bounds) times 256 (`0x558F53`-`0x558FAC`).

Captured in the native jungle level (`--load-original-level jungle --debug-object-effect build|demolish --capture-camera 1,-38,7,-135
--capture-world out.png --capture-frames 110 --capture-sequence 6,6`): building Belly Bounce shows green and yellow
four-point stars over the footprint for about a second (effect 79), demolishing it a green and white burst (effect 75),
matching the build and demolish effects of the original's video (2:07, 74:38).

## Registered

Retired (the port is from the instructions, with the tests below): `APPROX:PTCL-902` (template arithmetic: create,
density, spawn, emitter and particle updates, handles and slots), `APPROX:PTCL-903` (effectors and the spiral) and
`APPROX:PTCL-907` (saves, above). Still registered in the fidelity register:

- `PTCL-900`: EVENT positions use the animator's current matrices; the original reads the key table (`0x556B90`), which
  `0x44AB30` refreshes (gated by header bit 0x1 and table bit 0x1) when the instance is created (`0x463851`, the pose after
  animations 0 and 13 start), when it is drawn (the render node's per-frame callback `0x463030` -> `0x44E380` -> `0x473C70`
  -> `0x473E1B`) and from `0x42A6D6` and `0x438A53`, so it is about a render frame behind and stale while culled. What the two
  header and table bits mean and what the other two refreshes serve are not read.
- `PTCL-904` (OpenTPW's own world scale is `[EXT:particle-world-scale]`): the frame record's float +8 is the frame's width / height (`0x588660`) and `DAT_008BCBCC` the display's height / width; the extents are added in clip space, so a textured quad is wider on the screen by the display's width / height unless something rescales +8 (nothing found) and its world size depends on the original's 90 degree camera (OpenTPW: 55); a screen-space quad's width gets the target's height / width in OpenTPW and only the frame's aspect (1 untextured; height / width untextured) in the original.
- `PTCL-905`: OpenTPW draws in the original's order (runs of one blend, `[EXT:particle-blend-runs]`); the per-particle state words are decoded (depth writes, opaque untextured particles, alpha tests) but need an alpha test in the particle shader and depth-writing and opaque material variants. Against the world: the original draws the alpha and additive particle batches (states 0x556, 0x200552) and the weather's in the same far-to-near list as the translucent world faces (0x582AD0), so a particle behind water or glass shows through it; OpenTPW draws them after the whole sorted world list, so such a particle is hidden by the water's depth (porting the merge needs every quad as a sortable record).
- `PTCL-909`: screen-space particles are drawn in the world pass in slot order with the world particles and without depth, so the HUD and the advisor's image draw over them; the original draws them in the same loop (`0x57C620`, inside the scene view render `0x576A00`, before the weather), and which later view or callback draws the HUD windows and the advisor's model is not read.
- `PTCL-908`: wired are EVENT 1/2, ADDOBJ and ADDOBJ_EXT 1/2 (`0x5520E8`: ADDOBJ with a control operand), their kept
  effects following the key node on every object pass (`0x55189D`: the node position truncated to whole units through
  `0x51FE30`, a type-2 effect turned through `0x51FD90`), a negative key at the footprint box middle (`0x556B90`/`0x466B70`),
  the advisor's screen effects (docs/LIPS.md "Screen effects"), the stinkbomb litter's effect 50 (`ParkLitterEffects`: started at the sprite's position times 1024 when the litter is placed, `0x4D95BD`, ended when it is swept, `0x4D9620`; litter restored from a save starts none), the per-type object effects (above), the land effects of `scape.omp`, every bump vehicle's rider beams and the guests' teleport beams (below), the vehicles' break smoke 2 (`0x544C80`), boat spray 96 (`0x547F50`), the retire beam 15 (`0x54AE50`) and a kart's first-draw beam 15 (`0x546280`), the walk beam 19 (`0x5572EB`, `0x557f0e`) and the tour cars' smoke and fire 39 and 40 (`0x55DA70`, docs/TOURS.md). Not wired: a type-2
  effect with a negative key (`0x556B90` leaves its direction unset and the dispatcher passes uninitialised stack; the only shipped use is Fantasy's `bugstv`, ADDOBJ effect 16); an object without the footprint box (no shipped fixed item names a type effect or starts an effect at a negative key: `ParticleEmitterCensusTests`); the scoped effects (bit `0x8000`: the type record's `+0x4E0 + 4n`, `0x4145D0`,
  template ids `0x414420` loads into free pool slots (`0x51FA20`, released by `0x51FA80`) from every `*.emt` member of the
  type's archive (pattern `0x788F40`, built at `0x411970`), each stored at `+0x4DC + 4 x atoi(last character of its stem)`, so a
  template numbered 5 also takes the type's own sound list at `+0x4F0` (docs/RSE-VM.md; no shipped archive has one); no shipped script names one literally, and Jungle's `porkpie` takes the effect of its EVENT from a variable); the further dispatch after the build effect (`0x51C640`; the upgrade's `0x51BFC0` is its sound, wired); the beam when a guest is created (`0x4faec0`, taken when `0x4fa990` finds none of five cell types, which are not read), the staff members' beams (`0x505790`, `0x500bd0`) and the online avatars (`0x4f7c60`); the first-draw beam 15 of the boats and bumper cars (`0x549DB0` sets flag `0x4000000` for every vehicle, `0x54a009`; its only reader is `0x546a89` in the per-vehicle draw `0x546280`, which `0x519060` calls for every type-3 object with a controller, with no family check found; OpenTPW raises it for karts only); the lobby's key-panel effect `0x61` (`0x4b8ee0`, `0x4b90f0`, `0x4b9340`; OpenTPW has no island lobby key panel); the UI pool groups (`0x5ED5A0` and its callers) and the message-tag effects 90 and 91 of the region `0x48DE00`-`0x48E0C0`, which Ghidra left unanalysed (`0x48de1a` starts `0x5b` at a control's centre, keeps it in control property 9 and moves it through `0x51fe30`; `0x48dfbe` calls `0x486b90` with `0x5a`).

**The land's effects.** A level's `scape.omp` (`PlacedObjectsFile`) holds sounds and particle effects that belong to no thing. The loader `0x550e00` (called once per level load from `0x407f20`) calls `0x521E60(word 1, word 3, word 4, word 5)` for every type 2 record and drops the handle; the shipped files hold one, Jungle's effect 20 (`P_EFFECT_WaterFall`) at `(545100, 5000, 538700)`, which every saved jungle pool holds as a live emitter (the 11 reference saves; `ParticleEmitterCensusTests`). `Level.StartPlacedParticles` starts it with the level, before a resumed or imported pool replaces the state, so a park that never had it (a new jungle park) shows the waterfall's splash at the foot of the falls.

**Rider and guest beams.** `0x549C60` (hanging a vehicle's riders' heads) and `0x54AC70` (taking them off one by one) raise particle `0x13` (`P_EFFECT_BeamUp`) at the vehicle's drawn place (its instance's root translation times 1024; OpenTPW uses the simulation position) for each rider whose seat has a head dummy when their flag is set (`0x54AC70` also needs the head still hung, `[rider+8] != -1`). The load `0x549B80` is shared by every vehicle family, so boats and karts beam their riders too: `0x549B80` (BUMP 12, loading) and the bumper unload in `0x5474B0` pass 1, the creation `0x549DB0` and the retire `0x54AE50` pass 0 (`BumpController.RaiseRiderBeams`). A guest of a ride with `UsageInfo.RequiresTeleport` (type record `+0x100`, the key after `ChillsYouOut`: 20 shipped rides, jungle `bouncy`, `mumbo`, `spider`, the hallow and space rides, fantasy `bugstv` and `jelly`) beams when it boards (state 16) and when it leaves (state 15): `0x4fa470(0x13)` at the guest's position with the ground height under it (the two offset words `0x519410` and `0x519420` read 0 and are never written), times 1024 (`Level.StartGuestBeam`).

**The lobby's enter-park effect.** The enter-park routine (`0x4B8FD0`, called through the lobby's vtable slot `0x702F04` from `0x5E1E30`) starts the screen-space template `0x62` (`LobbyEnterEffect`) with `0x521E60(0x62, 50000, 0, z)`, where z is the centre row of the key panel control `0x1E0EA` (rows 170..437 of the 1536-row canvas: `(437 - 170) >> 1 + 170 = 303`) times 75000 / `0x600`, 14794: the horizontal centre of the screen and screen y -613/1024, over the panel, before it plays `cat_globallobbysfx` 4. Game mode 2 (Instant Action) skips the whole block, the sound included. The pool is global in the original and drawn by whichever scene is up; OpenTPW gives the lobby its own `ParticleRenderer` (`ParticleRendererModule.CreateScreenRenderer`, the pool of the park's renderer without a level) that `GameFlow` starts at the tick of the game-mode dialog and that is deleted with the lobby's entities when the park loads (the original's post-load pass `0x5200B0` kills screen-space emitters). The template runs 61 ticks of 31 ms (about 1.9 s) and draws 898 sprites on the shipped file; it takes its random draws from the particle pool's own stream (`0x80CEE0`), never the world's.

Differences that are not registered because the data cannot reach them: the ring emitter (+0xE, tp.exe `0x5224F0`),
the hold word (+2) and the ground bounce (+0x76) are ported or skipped without a shipped template using them; a low-detail
refusal returns -1 where the native code returns 0 (also no valid handle); the sine table uses `Math.Sin` where the
original uses the x87 `fsin` (the table is `trunc(sin(2 i * 0.00613591796875) * 256)`).

## Verification

```sh
dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --filter "FullyQualifiedName~ParticleSystem|FullyQualifiedName~ParticleSpriteAtlasTests|FullyQualifiedName~ParticleEventRequestTests|FullyQualifiedName~ParticleKeyResolverTests"
OPENTPW_GAME_PATH=/path/to/theme-park-world dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --filter FullyQualifiedName~Particle
OPENTPW_NATIVE_SHADER_TESTS=1 dotnet test source/OpenTPW.Tests -r osx-arm64 --filter WebShaderTests
python3 tools/fidelity_register.py --check
```

`ParticleSystemTests` covers the file layout and its rejections, creation limits, burst and rate counts, density, life
jitter, colour and frame progression, the spawn tick, drag and gravity, type-2 velocity, the control scale, emitter motion
and bounce, death and kill, chaining and following, slot and pool exhaustion, deterministic replay and the
engine-to-native conversion. `ParticleSystemPortTests` has one case per ported rule: the generator and the random forms,
LIFO slots and the global generation, the rate quarter, burst and cap, the disc, ring, box and rotation, the spiral,
colour flicker, frame, size and spin, the dead-emitter and child rules, the effectors, the save part and its validation,
the truncating position conversion, the blend flags and the quad's size, plus the shipped corpus (which templates use
which feature, every template running twice to the same state, the effector chains). `ParticleEventRequestTests` covers
EVENT and ADDOBJ/KILLOBJ and the world-origin key; `ParkResumeValidationTests` the envelope part. A visual check draws
effects in the native jungle run: `--load-original-level jungle --debug-particle 2,60,1 --capture-camera 1,-38,7,-135 --capture-world out.png --capture-frames 75`
(`--debug-particle` starts template effects at the camera target through the ADDOBJ starter, `--debug-particle-at x,y,z` picks an
engine position; the smoke puff of effect 2 is visible in the capture).

## Pose conversion

An EVENT or ADDOBJ effect starts at the key node's world position times 1024 (`0x5573D0`, `_ftol`; `0x55190F` for a kept
effect). OpenTPW builds the engine pose from the original position through `ObjectPlacement.ModelToEngine` (0.2 engine
units per original unit, the grid origin `-OriginalParkPlacement.GetOrigin(heightfield)`, original Y up to engine Z up) and
`ParticleRendererModule.ToOriginal` undoes exactly that map, so the native position is the original position times 1024 whatever
the scale and the centring are: they cancel, and no value of them needs to be read from tp.exe (the 0.2 scale is
`[EXT:md2-engine-scale]` for the drawing only). What remains is float32 rounding of the engine pose, at most one native unit
(1/1024 of an original unit) on a coordinate that lies exactly on a boundary; `ParticleSystemTests.PoseConversionRoundTripsThePlacementWithinOneNativeUnit`
pins it. The generic sandbox park has no original counterpart and no heightfield, and uses the scaled axes without its grid origin.
(PTCL-906, retired.)
