# Evening and night

October 18, 2026. The **evening and night extension** (`opentpw.evening`) gives the park a day and night cycle: the sun sets, the sky, the ground, the guests and the pool water darken, and lamps, pool lights and a few ride lights
switch on as it gets dark. The original game's parks are always in daylight (its theme `.sam` files carry an ambient and a directional light level that tp.exe never changes over time), so everything on this page is an
addition of this reimplementation, labelled `[EXT:evening]` in the code. It is **presentation only** and **off by default** until it is tuned: with it on or off the simulation, the economy, the guests, the saved parks, the world
state hashes and the determinism pins are exactly the same (`AffectsSimulation` is false, so a park save does not record it, as for `opentpw.restored-advice`).

| Part | Where |
| --- | --- |
| Switching it on, the day cycle row | [Using it](#using-it) |
| The time of day: the clock, the curves, what each channel does | [The cycle](#the-cycle) |
| What gets dark | [What gets dark](#what-gets-dark) |
| The lamps and their table | [Lamps](#lamps) |
| The options file, the browser, the command-line options, performance, tests | [Options](#the-option-and-its-file), [Browser](#the-browser), [Performance](#performance), [Tests](#tests) |
| What nobody has looked at yet | [Not done](#not-done) |

## Using it

- Switch the extension on in **Options > OpenTPW > Extensions and mods** ("Evening and night"; the change applies at the next start, [MODS.md](MODS.md)) or for one run with `--enable-mod opentpw.evening`.
- Pick the pace on **Options > OpenTPW > Day cycle**: **10, 20 or 40 minutes** for one full day and night (20 is the default), or **always day**, **always dusk** or **always night**. The row's help text says it only matters while the
  extension is on. Its name, description and values are translated in all eleven interface languages like the other OpenTPW rows.
- Always day is the original's look exactly (the extension then changes nothing and the lamps stay off).

## The cycle

**The clock follows play time.** A park day of the original lasts only about 6 seconds of real time at normal speed, which would be a strobe light, so the cycle runs at its **own pace, independent of the park speed**: `Level.UpdateEvening`
adds the real frame time to the clock when the park is running and nothing when it is not. "Running" is `Level.SimulationTimeScale > 0`, the same test the periodic autosave uses (the pause menu and the windows that stop the park, a speed
setting of Paused, the map, a texture-pack download, a deletion question: all give 0), so **pausing the game pauses the cycle**, and the fast and fastest speeds do not make it faster. A single frame counts for at most a quarter of a second,
so a load or a long hitch cannot jump the sun.

**Where it starts, and what is saved.** The phase is a number from 0 to 1 over one day, **0 being noon**. A park starts (and a saved park resumes) at noon with the cycle running; the phase is **not saved** (not in the park save, not in the options):
it is presentation state, a resumed park is simply at the start of a new day. What is saved is the pace, in the options file ([below](#the-option-and-its-file)).

**The curve** (`EveningClock.Daylight`, a pure function of the phase; tested): with *distance* the phase's distance from noon (0 at noon, 0.5 at midnight, symmetric before and after midnight), daylight is 1 up to a distance of **0.15**, 0
from **0.30**, and the cubic smooth step between. At the default 20 minutes that is:

| Time in the cycle | What it is |
| --- | --- |
| 0:00 to 3:00 | full day |
| 3:00 to 6:00 | dusk (the middle, daylight 0.5, is at 4:30) |
| 6:00 to 14:00 | night |
| 14:00 to 17:00 | dawn |
| 17:00 to 20:00 | full day again |

The fixed paces skip the clock: **always day** is daylight 1, **always dusk** daylight 0.5 (the middle of the dusk, the sun low and the lamps lit) and **always night** daylight 0.

**The look** (`EveningLook.At(daylight)`) turns the daylight into every value the renderer reads. `glow = 4 d (1 - d)` is 1 in the middle of the dusk and 0 at day and at night; the tints are `lerp(night, 1, d)` times `lerp(1, warm, glow)`:

| Channel | Day | Dusk (d = 0.5) | Night |
| --- | --- | --- | --- |
| Sun colour (the directional term; fades with daylight to the power 1.5) | 1, 1, 1 | 0.41, 0.24, 0.18 (warm) | 0.08, 0.14, 0.40 (moonlight) |
| Ambient level (of the original's 0.4) | 1 | 0.675 | 0.35 |
| Ambient tint in `test.shader` (times the level) | 1, 1, 1 | 0.78, 0.65, 0.61 (warm) | 0.55, 0.80, 1.35 (blue) |
| Sky tint (clouds and cylinder) | 1, 1, 1 | 0.54, 0.32, 0.26 (orange) | 0.07, 0.09, 0.22 |
| Fog and backdrop colour | 1, 1, 1 | 0.53, 0.33, 0.28 | 0.05, 0.07, 0.16 |
| Guest, staff and litter sprites | 1, 1, 1 | 0.67, 0.54, 0.50 | 0.34, 0.38, 0.60 |
| Pool water colours | 1, 1, 1 | 0.60, 0.54, 0.57 | 0.20, 0.28, 0.46 |
| Lamp level | 0 | 0.68 | 1 |

The **lamp level** is `1 - smoothstep((d - 0.35) / 0.4)`: 0 while it is light (d of 0.75 and more), growing as the dusk goes on (about 3:58 in a 20-minute cycle) and full from d = 0.35 (about 4:50), so the lamps come on as it gets dark and
fade out again at dawn. **At full day every value is exactly the original's** (`EveningLook.Day`: the sun white, the ambient uniform 0, no tint, no lamp), the shaders read a zero as "unchanged", and the draw's uniform block is byte for byte
what it was without the extension (a test compares the bytes).

The values were tuned by eye on offscreen captures of the jungle park and a pool (October 18, 2026, see [Tuning](#tuning)).

## What gets dark

- **The ground, the objects, the coasters and tracks, the tours and the pool deck, walls and basin** (every draw of `test.shader`, `pool-deck.shader` and `pool-basin.shader`): the sun colour (`Level.SunLight.Color`, which they already read) and the
  new ambient. The lit shaders had `vec3(0.4)` as the ambient; the pool deck and basin now multiply it by `g_flAmbientLevel`, a float in the padding after the sun position (byte 204 of the uniform block, 0 = unchanged), and `test.shader` (the ground, objects,
  coasters, tours) takes a **tinted** ambient from three floats `g_flAmbientR/G/B` in the padding after the view distance (bytes 244 to 255, all zero = unchanged), which gives the dusk its warm and the night its blue cast. Day and extension-off draws use the same arithmetic as before. The basin's own flat light follows it too. The block's size, the point lights' layout from byte 272 and the pool water's use of the slot (it keeps the padding for its wave strength and does not read it as an
  ambient level) are unchanged ([LIGHTS.md](LIGHTS.md)).
- **The sky** (`Sky.cs`: the cloud layers and the backdrop cylinder): `particle.shader` multiplies the vertex colour by the light colour of its uniform block when that is not zero; only the sky ever sets it, so every other particle draw is as before.
- **The fog and the backdrop**: the viewport clear and the engine fog colour (`RenderFog.WorldColour`) are the level's own fog colour (`Level.BackdropColour`) times the fog tint (`Level.WorldBackdropColour`), so the world fades into a dark
  blue at night, and the sky's haze still ends in the same colour.
- **The pre-rendered sprites** (guests, staff, ride heads, guest balloons, upgrade balloons, litter): they are not lit, as in the original, so they get a tint instead (`sprite.shader` multiplies by the otherwise unused light colour when it is not zero). They
  do not brighten near a lamp.
- **The pool water** is unlit; its two colours are multiplied by the water tint on the CPU (`PoolRenderer`).
- **Not changed**: the interface, the thought bubbles (they stay as bright as the interface), the build tools' placement markers (`BlueprintMarkers`, so they stay readable), the particles (smoke, fire, fireworks), the rain and snow, the advisor, the sun's direction (the renderer's fixed light position), and the text. There are no stars and no moon in the sky.

## Lamps

The lamps are the point lights of [LIGHTS.md](LIGHTS.md): one `IPointLightSource` per park (`EveningLampSource`, registered in `Level.PointLights` while the extension is on), asked once a frame, which adds nothing at day. They are **attached in
code from a data table**, `content/data/evening-lights.json`, not by changing any model: the pools extension's models and content build scripts are untouched, no original file is changed, and no guest sprite is rendered again.

**The table.** Each row names objects with a `match` (any of `theme`, `category` as `ride`, `shop`, `sideshow` or `feature`, `archive` name, and `poolsExtra`, the pools extension's own `Pools.Extra` key of the object's `.sam`: 5 is the pool lamp,
6 the underwater light; every field given must match) and the `lights` they get. A light has:

| Key | Meaning |
| --- | --- |
| `node` | the model node it follows (found by name, any case); it follows the node's animation, so a candle on a moving arm moves with it |
| `anchor` | `pivot` (the node's origin), `centre` (the middle of the node's mesh box, the default) or `top` (the middle of its top); a node without a mesh uses its origin |
| `offset` | three numbers added to the anchor, in model units in the engine's frame (ground in XY, up in Z; a model unit is 0.2 engine units, a cell is 10) |
| `colour` | linear red, green and blue from 0 to 4, not all 0 |
| `radius` | 1 to 200 model units: full colour at the centre, nothing at the radius (`PointLightMath`) |

A bad row refuses the whole table with a reason (`EveningLampTable.Parse`); a missing or damaged file logs a warning and gives no lamps. A node a model does not have leaves that light out and is logged once. At night a lamp's colour is the table's
colour times the lamp level.

**The shipped rows** (21 rows and 34 lights: two rows for the pools extension, 19 for original objects):

| Object | Lights |
| --- | --- |
| Pool lamp (`Pools.Extra` 5, all themes) | one warm light under the top of the lamp head, radius 55 |
| Underwater light (`Pools.Extra` 6, all themes) | one aqua light on the pool side of the disc, radius 50 |
| The *Lights* feature of the four themes (the four lamp heads along the entrance road) | four warm street lamps, radius 45 |
| The *Gates* of the four themes | one light on the arch or sign: warm (jungle), violet (hallow), cyan (space), peach (fantasy); radius 60 |
| Hallow: Giant Pumpkin, Demon Statue, Haunted Tower | an orange glow inside the pumpkin, the statue's flames, a window light |
| Space: Plasmatrope, Large Crystals | violet and cyan glows |
| Fantasy: Fountain, Jungle: Lava Fountain | a pale blue glow on the crystal base, a red-orange glow on the lava |
| Hocus Pocus (hallow ride) | six candle flames, which move with the arms |
| Aztec Mayhem (jungle ride) | four fire pots |
| Eruption (jungle ride), Pumpkin Castle (hallow ride) | the lava spurt, the ring of bulbs |
| Escape Velocity, Star Tours (space rides) | the red and green beam lights, the red and green aerials |
| Restaurant (hallow shop) | the green glow of the cauldron |

The limit of the point lights holds: at most 16 lights reach one draw (the nearest to it; the one-mesh ground gets the 16 nearest the camera among those in view) and the lights are sorted into the renderer's grid as usual. A park full of lamps
therefore lights the objects near the camera and not all of them at once; the lamps' own radii are small (4 to 12 engine units), so the nearest 16 are the ones that matter.

## The option and its file

The pace is `GameOptions.Evening` (`EveningPace`: `Minutes20` by default, `Minutes10`, `Minutes40`, `AlwaysDay`, `AlwaysDusk`, `AlwaysNight`), written by name to `save/opentpw-options.json` with the other options. A file from before the option has no
value and gets the default; **an unknown name, a number or a value of another type is read as the default** and the rest of the file is kept (`EveningPaceConverter`), and a value outside the enum is replaced on load (`Normalized`). The OpenTPW
page's row cycles through the six values (click, wheel or arrow keys, wrapping), OK saves it and Cancel puts the saved value back, like the other rows there. The options page's "effective size" text is narrower (its left two thirds) to make
room for the row.

## The browser

Nothing in the extension needs the desktop: the shaders it changed (`test`, `pool-deck`, `pool-basin`, `sprite`, `particle`) are regenerated in `content/shaders/web` (the byte layout of the uniform block did not change, only a float that was
padding is named), the lamp table is embedded with the other `content/data` files, and the options row and the mods screen are the same code. `source/OpenTPW.Web` builds.

## Tuning

Captures of the jungle park (the entrance, with the gate's and the lamp posts' lamps) and of a painted pool with swimmers at day, dusk and night were looked at and the look adjusted: the moonlight became bluer (0.08, 0.14, 0.40), the ambient got its own tint
(blue at night, warm at dusk) because a scalar ambient left the green grass green, the sun now fades faster than the daylight so dusk is clearly dimmer than day, and the sprites and the water were darkened a little. At night the ground and objects are dark and bluish and the
gate and the lamp posts light their surroundings clearly. Still open: the sky (the park camera never shows it at its normal angles, so its tint was not judged), the lamps of the pools extension (no pool lamp was placed in a capture), the ticket-booth
doors at the entrance (they stay bright at night), and the strength of dusk on green ground (the grass is green, so a warm sun shows mostly on paths, bricks and stone).

## Command-line options

Both work in Release builds as well (unlike most debug options) because performance runs use them:

- `--evening-pace <minutes10|minutes20|minutes40|day|dusk|night>` sets the pace for this run only: it is a run-scoped override in `Level` that is read instead of the option and never written to `GameOptions.Current`, so nothing is saved. It needs the extension on (`--enable-mod opentpw.evening`).
- `--debug-evening-lamps <n>` adds `n` lamp lights (the pool lamp's colour and radius) on a square grid 40 engine units (20 cells) across around the park camera's target, so they crowd into the view, that follow the lamp level, for measuring many lamps. The `--perf-run` log lines now also say how many point lights are in view.

## Performance

Measured on one Apple-silicon Mac that was shared with other builds (a Release build, the jungle park of [ENGINE-PERFORMANCE.md](ENGINE-PERFORMANCE.md), offscreen at the default size, `--silent --mute --no-advisor --no-vsync --frame-stats --perf-run 6000
--perf-arrivals 3`, the last 5-second statistics line of each run, three alternating rounds of four configurations, no capture of any kind). "Night" is `--enable-mod opentpw.evening --evening-pace night`, the lamps are the real ones of the
park (the jungle's Easymode park has 5 lamp lights: the four lamp heads and the gate, 3 of them in view) or `--debug-evening-lamps 100` and `400` crowded into the view (103 and 403 point lights in view, the selection hands each draw its nearest 16):

| Configuration | Frame average (ms), 3 rounds | Median | World render (ms), median | Allocation per frame |
| --- | --- | --- | --- | --- |
| extension off | 1.41, 1.37, 1.40 | 1.40 | 0.68 | 20 to 23 KB |
| night, the park's own 5 lamps | 1.37, 1.37, 1.37 | 1.37 | 0.57 | 21 to 23 KB |
| night, 100 lamps in view | 1.42, 1.43, 1.43 | 1.43 | 0.78 | 22 to 25 KB |
| night, 400 lamps in view | 1.42, 1.44, 1.45 | 1.44 | 1.04 | 22 KB |

With the park's own lamps the extension costs nothing measurable (the night frame is inside the spread of the runs). A hundred lamps in view add about 0.03 ms to the frame and 0.1 ms to the world render, four hundred about 0.04 ms to the frame and 0.36 ms to the world
render (the per-draw selection and the 32 bytes of upload per light; the growth of the world render shows only partly in the frame average, which is not explained further here). Nothing is allocated per frame beyond the usual. At day with the extension on, the lamp
source returns at once and no draw carries a light, so the cost is one extension check, the clock step and a few uniform writes a frame. A first set of runs taken while the machine's load average was 20 to 40 had a spread (1.4 to 2.2 ms for the same
configuration) larger than any of these differences and is not used. Per-draw cost and the 16-light limit are explained in [LIGHTS.md](LIGHTS.md#the-renderer); the many-lamp figures above are an upper bound, since a real park has a few dozen lamps at most.

## Determinism

The cycle reads the frame time and the pause state and writes only the renderer's inputs. Two kinds of evidence: the unit tests run the cycle beside an economy run and beside the full synthetic park of `DeterminismTests` (which have no level, so they prove that
the cycle shares no state with the simulation, not more), and a **real level** run: `--perf-run 9000 --perf-speed 1 --perf-hash-tick 11800` on the jungle park logs `Level.ComputeStateHash()` at park tick 11800 and the point lights in view. With the extension off,
at `--evening-pace night` (the lamp source registered, 3 point lights in view) and at night with 50 more debug lamps (53 in view) the hash is the same, `bdafd580cee2a816`.

## Tests

`EveningTests` (no GPU; the data test is inconclusive without `OPENTPW_GAME_PATH`): the daylight curve (full around noon, zero at night, mirrored, monotone, no jumps, wrapping), that **full day is the original's look exactly** (every uniform
and tint, byte for byte), the clock (pace lengths, wrapping, bad steps), the level's step (pausing stops it, a hitch is capped, off creates nothing, null-safe for a level built without its constructor), the options value (default, round trip by
name, unknown values, `CopyFrom`, `Normalized`, the row on the OpenTPW page: cycling, OK and Cancel), every interface language of every text, the extension's registration (off by default, not simulation, selectable, never recorded in a save), the
mods screen row, a park run's **world state hash equal with the extension on and off** (`EveningTests` on the economy and `DeterminismTests` on the full synthetic park with guests, scripts and pools, which also equals the plain run), the uniform
layout (byte 204, the block still 272 bytes) and the shader text of the five changed shaders, the lamp table (the shipped one, every kind of bad row), the lamp resolution on a built model, the pool lamp and underwater light on their shipped models,
and, with the game's data, that every row of the shipped table matches an original object and every node it names exists (44 lights resolved, counting each theme's copy of an object).

## Not done

- **Only partly looked at.** The park and pool captures above were; the colours and strengths of the table above, the lamp radii and positions, how dark the ground and the sprites should be at night, whether the sprites are too bright or
  too dark next to the lit ground, the sky's and the fog's night colours, and whether the underwater lights and the pool water look right still need a human eye (`--evening-pace dusk` and `night` show them).
- The sun's direction does not change and there is no moon or stars; the shaders' light position is a fixed one.
- The sprites do not brighten near a lamp, the pool water is not lit by the lamps, and lamps do not change the ambient.
- Which original objects get a lamp is a short, hand-picked list (above); shops, sideshows and most rides have none.
- The default stays off until the user decides.
