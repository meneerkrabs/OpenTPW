# Point lights

October 18, 2026. OpenTPW's park can show **point lights**: a light that shines from a point, brightens the ground, the objects, the coasters and tracks and the pools around it, and fades to nothing at its radius.
The original game's engine supports exactly this light but never creates one, so everything on this page is an addition of this reimplementation, labelled `[EXT:point-lights]` in the code (and
`[EXT:object-lights]` for the object side). It is **presentation only**: no light is read by the simulation, enters a state hash or changes a determinism pin, and a park without lights draws exactly
as before.

| Part | Where |
| --- | --- |
| The renderer: the light set, the falloff, the uniform layout, the shaders | [The renderer](#the-renderer) |
| Light dummies in object models, the four RSE opcodes, saves | [Lights on objects](#lights-on-objects) |
| Content kit: `ModelBuilder`, glTF `KHR_lights_punctual` | [CONTENT-KIT.md](CONTENT-KIT.md) |
| An example mod with a lamp that pulses | [The example mod](#the-example-mod) |
| Lamps that light at dusk, from a data table, with a day and night cycle | [EVENING.md](EVENING.md) |

## What the original has

tp.exe has a CPU, per-vertex light system (`TPLight.cpp`; the Mac build's `CLight`, `CLightParallel` and `CLightPoint`):

- The factory `0x457E70` builds a light of type 1 (ambient), 2 (directional, `CLightParallel`) or 3 (point, `CLightPoint`); these are the factory's case arguments (jump table `0x458300`, case 3 goes to `0x458113`), not the record's own type word: a point light's record has the **record type word 4** at +0x50, and the lighting `0x5741B0` dispatches on `type & 0xF` (2 directional, 4 point, 6 spot-like). A point light's defaults are a **radius of 100.0** (record +0x44), its square (+0x48) and one over
  the square (+0x4C); an **off flag** `0x10000000` sits in the type word (+0x50) and the colour is four floats at +0x04..+0x10 (which the lighting `0x5741B0` reads; a packed ARGB word at +0x54 is a second copy for other users).
- `0x5744D0` links the lights without the off flag, `0x5743A0` moves them into a model's space, and the vertex lighting `0x5741B0` sums them per vertex: the ambient colour, the directional lights (`N . L`), the
  **point lights** (the vertex's squared distance `d2` to the light is compared with `r2`; a vertex at or beyond the radius gets nothing, else `(r2 - d2) x (1 / r2)` times the light colour is added, **with no normal
  term**) and the spot-like type 6. `0x574530` packs the vertex colours, `0x5836B0` marks lighting dirty (the meshes `0x57AA10` and the terrain `0x56EF10` are relit).
- Nothing creates a point light. The RSE opcodes `ENABLELIGHT`, `DISABLELIGHT`, `SETLIGHT` and `COLOURLIGHT` (`0x555C7A` to `0x555D9A`) look up the model's **light dummy** (node attribute class `0x20000`, the light
  number as value, `0x44B220`) and its light record (`0x4587B0`), but the instance builder `0x44A870` never fills that pointer, so a script that used them would fault. No data set (Theme Park World PC, CD,
  Mac, PS2, Theme Park Inc, Alpha 50, the bonus content) has a light dummy or uses the opcodes.

## The renderer

**Model.** Every lit draw (the ground, placed objects, coasters, tracks, tours, the pool deck and basin) can carry up to **16 point lights** (`PointLightSet.MaxPerDraw`). A light is a position in engine units, a
colour (linear channels, normally 0..1) and a radius in engine units. The shader adds, per pixel, for each light

```
share  = max( (r2 - d2) * (1 / r2), 0 )        r2 = radius^2, d2 = squared distance from the pixel's world position to the light
shading += light.colour * share
```

to the same sum the sun and the 0.4 ambient make (`test.shader`: `vDiffuse + vAmbient`), and the texture is multiplied by the result. That is the original's per-vertex term evaluated per pixel: full colour at
the centre, nothing at the radius and beyond, no normal term, added to the lit colour (`0x5741B0`).

**Units.** The original's radius is in model units. One model unit is **0.2 engine units** (`ObjectPlacement.ModelScale`, `[EXT:md2-engine-scale]`) and a park cell is 10 model units = 2 engine units, so the
original's default radius of 100 is **20 engine units, ten cells**. The light's position is in the world (engine) space the shaders get from `g_mModel`, the same space as the objects and the ground.

**Which lights reach a draw** (`PointLightSet`). The set is per level (`Level.PointLights`).

1. Sources register once (a placed object with light dummies implements `IPointLightSource`). At the start of every frame `Level.Render` calls `Gather`, which asks each source for the lights that are on and drops
   those that are unusable or whose sphere lies wholly outside the view frustum (left, right, bottom, top and near planes of the view-projection).
2. Each draw of a lit material takes its model's bounds, moves the box to world space (the centre moved, the half extents widened by the absolute matrix) and calls `Select`: lights whose sphere does not reach the box (`distance(box, light) >= radius`) are
   rejected, the rest are ordered by the squared distance from the light to the box (0 inside it), then by the squared distance to the camera, then by the order the sources gave them, and the nearest 16
   are kept. The order is deterministic for a given camera. A big draw, such as the ground (one mesh), gets the 16 lights nearest the camera among those in view; farther ones do not light it until the
   camera moves (the limit of one uniform array per draw). With no light in view a draw does no light work at all.

**Uniform layout.** Lights ride in the draw's own uniform upload, not in a second one (per-draw uniform uploads are already the largest single cost of a frame, `ENGINE-PERFORMANCE.md`). The engine's block
(`ObjectUniformBuffer`, 272 bytes) is unchanged; the light count is a float in the padding after the light colour (byte 220, `g_flLightCount` in the lit shaders, `ObjectUniformBuffer.LightCount`), and the lights follow
the block from byte 272 as `vec4 g_vPointLight[32]`, two vec4 per light: `(position.xyz, radius^2)` and `(colour.rgb, 1 / radius^2)` (`PointLightGpu`, `LitObjectUniformBuffer`). Only
`272 + 32 x count` bytes are uploaded, so a draw with no lights uploads the same 272 bytes as before and a draw with all 16 uploads 784, inside the 2,048-byte buffer every material owns. std140
and the cross-compilers agree on this layout: a vec3 followed by a float packs, and a vec4 array after the float at byte 268 starts at 272.

**Backends.** The lit shaders are `test.shader`, `pool-deck.shader` and `pool-basin.shader` (`Material.ReceivesPointLights`); the others (`sprite`, `particle`, `3d`, `unlit`, `water`, `pool-water`,
`weather`, `advisor`, UI) do not read the lights and never carry them. Desktop shaders are compiled through Veldrid.SPIRV to Metal, Vulkan, Direct3D and OpenGL; the browser's GLES 3.00 versions are
precompiled into `content/shaders/web` (regenerate with `OPENTPW_NATIVE_SHADER_TESTS=1 OPENTPW_WRITE_WEB_SHADERS=1 dotnet test --filter WebShaderTests`), and the browser's buffer binding is the whole
2,048-byte scratch buffer, which is larger than the 784-byte block. The pool water (`pool-water.shader`) keeps the padding slot for its wave phase and is not lit; the guests, staff and
particles are pre-rendered sprites or unlit billboards (as in the original) and take no light.

**No lights, no change.** A draw without lights takes the same code path as before the lights existed: the same shader source apart from a branch on `g_flLightCount > 0.5` that adds nothing, the same 272-byte
upload with a zero in a padding slot that was always zero (the pool parts' wave phase moved to the water only) and the same texture and fog arithmetic. `PointLightTests` checks the layout (offsets,
sizes, upload size 272), that the byte image of a lit draw's block differs from a plain one only in the count, that the CPU falloff (`PointLightMath.Sum`, the same expression the shaders use, which the
tests compare with the shader text) of no light is exactly zero, and which lights reach which box. There is no pixel comparison in the test suite (it needs a GPU window), and no capture of the renderer
was taken for this feature: the identity rests on the CPU-side facts above and on the shader text.

**Looking at a light.** `--debug-light x,y,height,radius[,red,green,blue]` (smoke-only, `[EXT:debug-light]`) adds one fixed light a few frames after the original level is up, at x, y, height relative to
the park camera's target in engine units (a cell is 2) and a radius in engine units (the default radius is 20); the colour is three 0..1 channels (white without). It lets someone look at what a light does
without placing an object (pass `--visible` to see it on screen: `--debug-*` runs are offscreen by default). With `--capture-camera` the held camera's ground target is the origin (the live camera may not have moved there yet when the light is added).

**Cost.** `Gather` runs once a frame and does nothing without sources. `Gather` also sorts the lights into a coarse XY grid (at most 64 x 64 cells, each light in every cell its sphere overlaps; from 8 lights up). A draw's `Select` looks only at the lights of the cells its box covers (a box covering more than 16 cells, such as the ground, scans every light) with a box-distance test and, per kept light, one insertion into 16 slots; the result is the same as scanning every light (a test checks it on random scenes). The draw's box is moved to world space as centre plus the absolute matrix times the half extents, and the 784-byte block is built only for a draw that a light reaches. The
upload grows by 32 bytes per light. Nothing is allocated after the first frame. The per-pixel cost is a loop of at most 16 iterations of a dot product and a multiply-add, skipped by a uniform branch when
the count is zero. Measured on one Apple-silicon Mac while it was shared with other builds (a Release build, the jungle park of `ENGINE-PERFORMANCE.md`, `--no-vsync --perf-run 3000 --perf-arrivals 3`, four
alternating runs of the build before and the build after this change, no light in the park): the whole `Renderer.Update` call 1.91, 2.01, 2.01 and 2.17 ms before, 1.93, 1.96, 1.90 and 2.16 ms after; the world
render 0.91 to 1.15 ms before and 0.79 to 1.18 after, allocation 273 to 318 KB a frame in both: no difference outside the spread of the runs. With one light on the ground (`--debug-light`), three alternating pairs: the whole
update call 1.84, 1.96 and 1.67 ms without it and 1.97, 2.03 and 1.81 ms with it, about 0.1 ms more on the thirty-five parts a frame (one more `UpdateBuffer` size, a box transform and a selection per draw, and 32
bytes of upload).

## Lights on objects

An object's model can carry **light dummies**, and its script can switch, dim and colour them with the four light opcodes. Every light of a placed object follows its dummy node, so a light on a moving part moves
with it. Lights are presentation: the light state is saved with the object so a resumed park looks the same, but it never enters a world-state hash or a determinism pin.

### The model format

A light dummy is a node attribute record (the 20-byte records of [MD2-MODELS.md](MD2-MODELS.md)) of class `0x20000`, the original's light class (`0x44B220` looks a node up by class and number), with the **light
number** as the value. OpenTPW writes the type word `0x20040` (the class and the data-block flag `0x40`) and a data block, because the original format gives a class-`0x20000` record no meaning of its own:

| Part | Meaning (OpenTPW's own, `[EXT:object-lights]`) |
| --- | --- |
| record type | `0x20040` (`ModelBuilder.LightType`); any record whose type has bit `0x20000` is a light, a record without a data block is a light with the defaults |
| record value | the light number, 0 to 32767 (a script operand is 16 bits); the first record of a number wins, as the original's lookup takes the first dummy |
| data block word | the radius in model units, 1 to 65535; **0 is the default of 100**, the original's point-light radius (low 16 bits; the high 16 bits are the position distance of the other blocks) |
| data block X, Y, Z | the base colour, linear red, green and blue from 0 to 1 |
| the node | the light stands at the world transform of the node the record belongs to (record *i* belongs to node `Unknown46 + i`, as for the rider heads), at its pose, so animation moves it |

No shipped model has such a record (`ModelLights` is the reader; the attribute types of the shipped corpus have no bit `0x20000`). Rider heads and lights can share one model: the attribute table spans the nodes of both.

### The `.sam` keys

The object's `.sam` tunes the lights without touching the model. Each value is one number, like every `.sam` value, so a colour is three keys. Everything is optional; a key for a light the model does not have is ignored.

| Key | Meaning | Default |
| --- | --- | --- |
| `Lights[n].Red`, `.Green`, `.Blue` | the **base colour** of light `n` in percent (0 to 100), the scale COLOURLIGHT uses; a key replaces that channel of the model's colour | the model's colour, else white |
| `Lights[n].Radius` | the radius in model units (a positive number; 0.2 engine units a model unit, 10 model units a cell) | the model's radius, else 100 (20 engine units, ten cells) |
| `Lights[n].On` | `1` or `0`: whether the light starts on | `1` |

### The four opcodes

They behave as the original's handlers (`0x555C7A` to `0x555D9A`), with one deliberate difference.

| Opcode | Operands | Effect |
| --- | --- | --- |
| `ENABLELIGHT` | `light` | clears the light's off flag (the light shines) |
| `DISABLELIGHT` | `light` | sets the off flag (the light is dark; the colour is kept) |
| `SETLIGHT` | `light level` | colour = **base colour** x `level` x 0.01 (so 100 is the full base colour, 50 half, 200 twice) |
| `COLOURLIGHT` | `light red green blue` | colour = each operand x 0.01; a later `SETLIGHT` scales the base colour again, not this colour |

The colour is a float and is **not clamped to 0..1**, as the original keeps it (`0x4587E0`, `0x458890` store floats; the packed ARGB word they also write is truncated and is not what the lighting reads): `SETLIGHT 200` is twice the base colour and a negative operand darkens. OpenTPW only bounds each channel to +-4 so a huge operand stays finite (`PointLightMath.ColourBound`); the model's own base colour (the data block, the glTF colour) has channels from 0 to 1. A light's radius is at least one model unit (a smaller `.sam` radius is ignored). A dummy hidden by its node-state bit (the animator's hidden bit) puts its light out. None of them touches the script's flags. **A missing light is a safe no-op.** The original looks the dummy up and uses the result without a check, and its instance builder (`0x44A870`) never fills the light pointer of a
dummy, so a script that used a light there would fault; here a number the model does not have changes nothing and is logged once per object, opcode and number (`OriginalObjectRuntime.PerformLight`). The four are no
longer unimplemented effects (`APPROX:RIDES-046` is retired); a bare `RideVM` with no game systems still counts them as unimplemented, as for any opcode with no host.

### Saves

The state of each light (on or off, the current colour as three channels from -4 to 4) is saved in the object's runtime snapshot (`OriginalObjectRuntimeSnapshot.Lights`), so a resumed park shows the same lights. An
object whose model has no light writes no `Lights` member, so every saved park of the original objects is byte for byte what it was. The member is optional and validated on load (at most 64 lights, unique numbers
in range, finite channels within +-4); a saved number that the model no longer has is dropped with a note (the model changed since the save), never an error. The base colour and radius are not saved: they come from the model and
the `.sam` again. No state hash reads the lights (`OriginalObjectEffects.AddCanonicalState` hashes the animator only), and the determinism pins (`DeterminismTests`, the fixed-run hashes, the park-resume tests) are unchanged.

### The content kit

`ModelBuilder` writes light dummies: `node.AddLight( number, colour, radiusModelUnits )` on a dummy node (numbers unique, a node has one attribute record, so a light and a rider head cannot share a node). The glTF
converter reads `KHR_lights_punctual` **point lights** on nodes into dummies ([CONTENT-KIT.md](CONTENT-KIT.md#lights)): the light's colour is the base colour, its `range` is the radius in model units (a scene unit is a model unit),
only a point light on a node with the extra `opentpw_light` (the number) becomes a dummy (an exporter writes every light of a scene, and Blender's default scene has one; a point light without the extra is skipped with a warning, and the Blender exporter warns too), and `opentpw_light_radius` sets the radius by hand (Blender's lights have no range). The glTF intensity is not carried (its unit differs between exporters) and spot
and directional lights are skipped with a warning.

## The example mod

`content/mods/opentpw.example.lamp-lights` is an official data mod, **off by default**, that adds a *Lamp Post* to the jungle park's features: a one-cell lamp with a light dummy (a warm white, radius 60 in the model,
70 in the object's `.sam`) and an RSE script that uses all four opcodes: the light breathes (`SETLIGHT` 100, 80, 60, 80 per cent), flickers off and on (`DISABLELIGHT`, `ENABLELIGHT`), then cycles red, green and
blue (`COLOURLIGHT`) and returns to the warm colour. Switch it on in Options > OpenTPW > *Extensions and mods* (or `--enable-mod opentpw.example.lamp-lights`). It is built from `tools/lights`: `make_example_lamp.py`
writes the glTF (with a `KHR_lights_punctual` point light), `lamp.sam` and `lamp.rse` are the sources, and `build-example-mod.sh` runs the content kit and writes the mod. It is not part of the browser build (the
browser ships no `opentpw.example.*` mod).

## Not done

- How a light *looks* was not judged: nothing was looked at on screen, so the falloff against the sun and the 0.4 ambient, the example's radius and colours and the 16-light limit on the
  one-mesh ground are a first guess for someone to look at (`--debug-light`, or the example mod).
- The sprites (guests, staff) and particles are not lit, as in the original; the pool water is not lit.
- The renderer has no shadows and no light on the sky. The evening and night extension (`opentpw.evening`, on by default) uses these lights for lamps that switch on at dusk, from a data table, and darkens the sun, the ambient, the sky and
  the sprites with the time of day: [EVENING.md](EVENING.md).
