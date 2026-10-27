# Evening and night

October 18, 2026. The **evening and night extension** (`opentpw.evening`) gives the park a day and night cycle: the sun sets, the sky, the ground, the guests and the pool water darken, and lamps, pool lights and a few ride lights
switch on as it gets dark. The original game's parks are always in daylight (its theme `.sam` files carry an ambient and a directional light level that tp.exe never changes over time), so everything on this page is an
addition of this reimplementation, labelled `[EXT:evening]` in the code. The cycle is **presentation only** and the extension is **on by default** (the user decided on October 18, 2026, after the tuning captures; it can be switched off on Options > OpenTPW > Extensions and mods): with it on or off the simulation, the economy, the guests, the
world state hashes and the determinism pins are exactly the same. Since version 1.1.0 (October 19, 2026) the extension also brings a buildable **[torch](#torches)** per theme, an object with a price, so it is a simulation extension (`AffectsSimulation` is true) that a park save records **only
while the park holds a torch**, like the pools, umbrellas and winter extensions: a park without a torch saves, loads and exports byte for byte as before, with the extension on or off.

| Part | Where |
| --- | --- |
| Switching it on, the day cycle row | [Using it](#using-it) |
| The time of day: the clock, the curves, what each channel does | [The cycle](#the-cycle) |
| What gets dark | [What gets dark](#what-gets-dark) |
| The lamps and their table | [Lamps](#lamps) |
| The buildable torches | [Torches](#torches) |
| The options file, the browser, the command-line options, performance, tests | [Options](#the-option-and-its-file), [Browser](#the-browser), [Performance](#performance), [Tests](#tests) |
| What nobody has looked at yet | [Not done](#not-done) |

## Using it

- The extension is on by default. Switch it off in **Options > OpenTPW > Extensions and mods** ("Evening and night"; the change applies to the next park you start or load, [MODS.md](MODS.md#when-a-change-applies)) or for one run with `--disable-extension opentpw.evening`.
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
| Sun colour (the directional term; fades with daylight to the power 1.8, tinted orange by the glow) | 1, 1, 1 | 0.62, 0.33, 0.17 (orange) | 0.08, 0.14, 0.40 (moonlight) |
| Ambient level (of the original's 0.4; `0.35 + 0.65 d^1.4`) | 1 | 0.60 | 0.35 |
| Ambient tint in `test.shader` (times the level) | 1, 1, 1 | 0.78, 0.58, 0.52 (warm) | 0.55, 0.80, 1.35 (blue) |
| Sky tint (clouds and cylinder) | 1, 1, 1 | 0.54, 0.32, 0.26 (orange) | 0.07, 0.09, 0.22 |
| Fog and backdrop colour | 1, 1, 1 | 0.53, 0.33, 0.28 | 0.05, 0.07, 0.16 |
| Guest, staff and litter sprites | 1, 1, 1 | 0.67, 0.54, 0.50 | 0.34, 0.38, 0.60 |
| Pool water colours | 1, 1, 1 | 0.60, 0.54, 0.57 | 0.20, 0.28, 0.46 |
| Lamp level | 0 | 0.68 | 1 |

The **lamp level** is `1 - smoothstep((d - 0.35) / 0.4)`: 0 while it is light (d of 0.75 and more), growing as the dusk goes on (about 3:58 in a 20-minute cycle) and full from d = 0.35 (about 4:50), so the lamps come on as it gets dark and
fade out again at dawn. **At full day every value is exactly the original's** (`EveningLook.Day`: the sun white, the ambient uniform 0, no tint, no lamp), the shaders read a zero as "unchanged", and the draw's uniform block is byte for byte
what it was without the extension (a test compares the bytes).

The values were tuned by eye on offscreen captures of the jungle park and a pool (October 18, 2026, see [Tuning](#tuning)); the dusk was retuned the same day (open point 1, below: a stronger orange sun and a warmer, lower ambient, so green ground reads as a warm, darker evening).

## What gets dark

- **The ground, the objects, the coasters and tracks, the tours and the pool deck, walls and basin** (every draw of `test.shader`, `pool-deck.shader` and `pool-basin.shader`): the sun colour (`Level.SunLight.Color`, which they already read) and the
  new ambient. The lit shaders had `vec3(0.4)` as the ambient; the pool deck and basin now multiply it by `g_flAmbientLevel`, a float in the padding after the sun position (byte 204 of the uniform block, 0 = unchanged), and `test.shader` (the ground, objects,
  coasters, tours) takes a **tinted** ambient from three floats `g_flAmbientR/G/B` in the padding after the view distance (bytes 244 to 255, all zero = unchanged), which gives the dusk its warm and the night its blue cast. Day and extension-off draws use the same arithmetic as before. The basin's own flat light follows it too. The block's size, the point lights' layout from byte 272 and the pool water's use of the slot (it keeps the padding for its wave strength and does not read it as an
  ambient level) are unchanged ([LIGHTS.md](LIGHTS.md)).
- **The sky** (`Sky.cs`: the cloud layers and the backdrop cylinder): `particle.shader` multiplies the vertex colour by the light colour of its uniform block when that is not zero; the sky sets it, and since the open-points round so do the pool spray and the world's alpha particles (kind 0: smoke, dust, steam), which take the sprite tint; fire, sparks and fireworks stay as before.
- **The fog and the backdrop**: the viewport clear and the engine fog colour (`RenderFog.WorldColour`) are the level's own fog colour (`Level.BackdropColour`) times the fog tint (`Level.WorldBackdropColour`), so the world fades into a dark
  blue at night, and the sky's haze still ends in the same colour.
- **A soft shoulder at dusk and night** (`test.shader`, only when the tinted ambient is set): a surface whose brightest channel exceeds 0.4 keeps 25 % of the excess (all three channels scaled alike, so the hue stays). It keeps a near-white surface next to a lamp, such as the ticket booths' panels, from burning out to flat white; everything below 0.4 is unchanged, and day and extension-off draws skip it. It fades in with the falling ambient (`clamp((1 - g_flAmbientLevel) / 0.65, 0, 1)`: none on the first dusk frame, whole at full night), so a lit surface never steps between two frames.
- **The pre-rendered sprites** (guests, staff, ride heads, guest balloons, upgrade balloons, litter): they are not lit, as in the original, so they get a tint instead (`sprite.shader` multiplies by the otherwise unused light colour when it is not zero). They
  do not brighten near a lamp.
- **The pool water** is unlit; its two colours are multiplied by the water tint on the CPU (`PoolRenderer`).
- **Self-lit materials** (material word +0xC bit 0x1, [MD2-MODELS.md](MD2-MODELS.md#self-lit-materials); user decision of October 27, 2026): at day tp.exe draws them full-bright and so does `test.shader`. At dusk and night (the tinted ambient is set) a self-lit material takes the same night tint as the other unlit content (the pool water's) (`EveningLook.UnlitTint` is the pool water's `WaterTint`, which keeps a river as dark as the lit art round it; the sprites' own tint is brighter and made the night river noticeably lighter than before): `ModelEntity` writes it into the camera position that `test.shader` never reads, and the shader multiplies the corner's colour by it, without the soft shoulder. The **real light sources stay full-bright**: a self-lit material whose texture name contains `glow`, `flame`, `fire`, `torch`, `lamp`, `light`, `coal` or `lava` (`TextureSlotAddressing.IsLightSource`, vertex flag `LightSourceFlag`). Over the original files these are `b_glow2`, `glow2`, `h_glow`, `h_glow2`, `GEM_GLOW` (the coaster pylons' gems), `sc_glow` (`station.MD2`, `megacost.MD2`), `flame` (the candle), `h_flame` and `fire0` (the go-karts' track, the coaster models and the queue pieces), `s_lights` and `h_light` (the go-karts' track, the teleporter and the bumper cars), `fp_coals` (the fire pit's glowing coals) and `lava2`, `lava2a` and `ig_lava2` (the lava; the user counted coals and lava as glowing fire, October 27, 2026), and the extension's own torch flames (`tt_*_flame`, and the hallow torch's own `tt_hal_glow`); every other self-lit material (water, signs, beams, panels, the fire pit's stand `fp_standred`, the advisor's mouths) is tinted. `SelfLitMaterialTests` pins the list over every model of the original archives, `EveningTorchTests` the torches and `NativeWorldShaderRenderTests` the shader at night, at day and for a light source.
- **The particles**: the pools' spray droplets (`PoolSpray`) and the world's alpha-blended particles (smoke, dust, steam; `ParticleRenderer`, run kind 0) take the sprite tint through the light colour `particle.shader` multiplies in when it is not zero. The additive and one-plus-one ones (fire, sparks, fireworks) give off light and stay as they are, and so do the screen-space ones.
- **Not changed**: the interface, the thought bubbles (they stay as bright as the interface), the build tools' placement markers (`BlueprintMarkers`, so they stay readable), the additive particles (fire, fireworks), the rain and snow, the advisor, the sun's direction (the renderer's fixed light position), and the text. There are no stars and no moon in the sky.

## Lamps

The lamps are the point lights of [LIGHTS.md](LIGHTS.md): one `IPointLightSource` per park (`EveningLampSource`, registered in `Level.PointLights` while the extension is on), asked once a frame, which adds nothing at day. They are **attached in
code from a data table**, `content/data/evening-lights.json`, not by changing any model: the pools extension's models and content build scripts are untouched, no original file is changed, and no guest sprite is rendered again.

**The table.** Each row names objects with a `match` (any of `theme`, `category` as `ride`, `shop`, `sideshow` or `feature`, `archive` name, `poolsExtra`, the pools extension's own `Pools.Extra` key of the object's `.sam`: 5 is the pool lamp,
6 the underwater light, and `mod`, the id of the extension or mod whose content root lists the object, used for the [torches](#torches); every field given must match) and the `lights` they get. A light has:

| Key | Meaning |
| --- | --- |
| `node` | the model node it follows (found by name, any case); it follows the node's animation, so a candle on a moving arm moves with it |
| `anchor` | `pivot` (the node's origin), `centre` (the middle of the node's mesh box, the default) or `top` (the middle of its top); a node without a mesh uses its origin |
| `offset` | three numbers added to the anchor, in model units in the engine's frame (ground in XY, up in Z; a model unit is 0.2 engine units, a cell is 10) |
| `colour` | linear red, green and blue from 0 to 4, not all 0 |
| `radius` | 1 to 200 model units: full colour at the centre, nothing at the radius (`PointLightMath`) |
| `flicker` | optional, 0 to 0.5: the share by which the light dips with the [torches' flicker](#torches) (0, the default, is steady) |

A row may also name a `flame`: a model node the extension moves as a flame ([Torches](#torches)).

A bad row refuses the whole table with a reason (`EveningLampTable.Parse`); a missing or damaged file logs a warning and gives no lamps. A node a model does not have leaves that light out and is logged once. At night a lamp's colour is the table's
colour times the lamp level.

**The shipped rows** (25 rows and 38 lights: two rows for the pools extension, four for the [torches](#torches), 19 for original objects):

| Object | Lights |
| --- | --- |
| Pool lamp (`Pools.Extra` 5, all themes) | one warm light under the top of the lamp head, radius 42 |
| Underwater light (`Pools.Extra` 6, all themes) | one aqua light 14 model units into the pool from the disc and 6 below the object's ground level, radius 26 |
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
| The torches (the evening extension's own, one per theme) | one light on the flame: warm orange (jungle, hallow), blue (space), pink (fantasy); radius 40 |

The limit of the point lights holds: at most 16 lights reach one draw (the nearest to it; the one-mesh ground gets the 16 nearest the camera among those in view) and the lights are sorted into the renderer's grid as usual. A park full of lamps
therefore lights the objects near the camera and not all of them at once; the lamps' own radii are small (4 to 12 engine units), so the nearest 16 are the ones that matter.

## Torches

The user asked on October 19, 2026 for a torch in all four themes, available by default, because a park can now be dark and the only light a player could build was the pool lamp. Their decisions, quoted in the labels:

1. **Part of the evening extension** ("Onderdeel van de avondextensie"): the torches are listed while `opentpw.evening` is on, and a park save records the extension **only while the park holds a torch** (`ParkResumeMods.UsedOnlyExtensionIds`, `EveningInUse`, `[EXT:evening-torches-save]`).
   A park without a torch records nothing and is the same bytes with the extension on or off (`EveningTorchTests`, and the default-set tests of the other extensions, which save no mod, still pass); a save with a torch records `opentpw.evening 1.1.0` and refuses to load
   with the extension off (`needs opentpw.evening 1.1.0`), and holds the asset keys of the placed torches only. A park with a torch cannot be exported to a `.tpwpark` package (packages record no mods yet, [MODS.md](MODS.md)), as with a winter kiosk; visits open exported
   packages, so they never meet one.
2. **Light and mood only** ("Alleen licht en sfeer"): a torch has no attraction value and no new-attraction bonus (its `.sam` sets `Info.AttractionValue` and the three `Attraction[n].NewBonus` to 0 over the theme's `Features.sam` defaults), no guest chooses it (`Info.IsChoosable 0`), and its
   script only waits. Its light is a row of the [lamp table](#lamps) on the flame node, so it shines at dusk and night only, like every lamp.
3. **Themed variants** ("Themavarianten"), all OpenTPW's own art (MIT, like the other extension content): jungle a bamboo **Tiki Torch** with a woven cup, Halloween a wrought-iron **Pumpkin Torch** with a carved pumpkin head whose face glows, space a metal **Plasma Torch** with a
   blue plasma flame, fantasy a striped **Candy Torch** with a pink flame.
4. **Cheap, like a bench** ("Goedkoop, als een bankje"): Theme Park World has **no bench** (none of the four themes' features is one, and none of Theme Park Inc's file names); its cheapest shipped scenery is space's Small Bush at **50**, and most small scenery and the
   litter bins cost 100. A torch costs **50**, has no upkeep (`Upgrades[0].RunningCost 0`) and no research (`"research": false`), so it is available from the start in every park. It is a feature (`Info.WhichUIType 3`, the Features tab, where the lamps and the litter bins are).

**Content.** `content/evening` (built by `tools/evening/build-content.sh`; nothing in it comes from either original game):

- `objects.json`: four torches, ids **63900** (jungle), **63901** (hallow), **63902** (space) and **63903** (fantasy) inside the extension's own range **63900 to 63999** (below the example lamp mod's 64000..64009 and the winter extension's 64010..64499; the table of
  OpenTPW's ranges is in [MODS.md](MODS.md)), category `features`, names in all eleven interface languages (Tiki Torch, Pumpkin Torch, Plasma Torch, Candy Torch in English).
- `objects/<theme>/torch/`: the MD2 model, five or six 64 x 64 TGA textures (`tt_` prefix), `Torch.sam` and `Torch.RSE` (a one-cell feature; the script waits in a loop). The model has two nodes: **`torch`** (a footing, the pole and the head, lit like any object) and
  **`flame`** (a rounded teardrop of eight sides with a self-lit gradient material). The flame node's origin is the flame's base; the extension hides it by day and grows it with the lamp level ([below](#torches)), so a torch shows an empty cup by day and burns from dusk. The torch is about 15 model units tall (a guest is about 10).
- Built by `tools/blender/opentpw_torches.py` (geometry and generated textures, reusing the changing room's texture generators and builder) and the content kit's `object` command with `tools/evening/torch.sam` and `tools/evening/torch.rse`.
- `EveningContent` lists the folder as a content root of the id `opentpw.evening` while the extension is on and not in a Theme Park Inc run (its parks have other themes), so the asset keys are `mod:opentpw.evening:/objects/<theme>/torch/...`. The catalog cache key names the root
  (as for the pools, umbrellas and winter roots), so a catalog loaded with the extension on is not reused with it off.

**Lamps.** Four rows of `content/data/evening-lights.json` match `{ "mod": "opentpw.evening", "theme": <theme>, "archive": "torch" }` (the new match key `mod` compares the id of the content root that lists the object, so an original or a mod's object named `torch` never gets
the light) and give one light on the `flame` node: warm orange (1.0, 0.55, 0.18), radius 40, at the flame's centre in Halloween; blue (0.35, 0.62, 1.0), radius 40, in space; and, because green grass reflects little red or blue (the first captures showed hardly any pool of
light on the jungle and fantasy lawns, where Halloween's grey ground showed a clear one), a stronger light 4 model units below the flame's centre, radius 44, in the jungle (warm, 1.9, 0.88, 0.24) and in fantasy (pink, 2.1, 0.55, 1.6).

**The flicker and the flame** (`[EXT:evening-torches-flicker]`, user-approved 2026-10-19: "Alleen licht en sfeer", a deterministic flicker preferred). Both are presentation only: nothing reads or writes the simulation, a save or a state hash.

- A row may name a `flame` node and a light may have a `flicker` (0 to 0.5): the four torch rows have `"flame": "flame"` and `"flicker": 0.25`; every other row is steady and names no flame.
- `TorchFlicker.At(seconds, seed)` is a fixed sum of three sines, `0.5 + 0.28 sin(2 pi 0.9 t + p1) + 0.14 sin(2 pi 2.3 t + p2) + 0.08 sin(2 pi 4.7 t + p3)`, 0 to 1, 0.5 on average; a gentle flicker that changes by at most a tenth between two frames at 60 frames a second. The phases come from a seed of the
  torch's cell (`SeedOf(x, y)`), so the same torch flickers the same way in every run and torches side by side do not pulse together.
- The clock is the extension's own play time (`Level.EveningFlameSeconds`): the frame time while the park runs, a quarter of a second at most per frame, standing still while the park is paused, at every pace including the fixed looks; it is not saved (a resumed park starts it again).
- A flickering light's colour is the table's colour times the lamp level times `1 - flicker (1 - f)`: a torch's light dips by up to a quarter, in step with its flame.
- The flame (`EveningFlames`): each frame, every placed object whose row names a flame node has that node's part drawn with `TorchFlicker.FlameMatrix`, in the node's own frame about its origin (the flame's base): its height 1 plus or minus 0.14 times `2 f - 1`, its width
  half that the other way, and a slow lean of at most 0.06 radians (about 3.4 degrees) at 0.9 and 1.3 Hz, all scaled about the base by the lamp level (`TorchFlicker.FlameAt`): **by day the flame is hidden** (the torch shows an empty cup), it grows as the lamps come on at
  dusk and burns at full size at night, as the torch's light does (chosen on October 19, 2026 over a flame drawn all day, after the first captures showed the self-lit flame a dull brown by day). The transform and the hiding are set on the drawn parts only
  (`OriginalObject.SetPresentationNodeTransform`): the animator's node transforms, which the lamps, the snow cover and the simulation read, do not change. The torches are cached and rebuilt only when the placed set changes, as the lamp source does. When the cycle stops (the extension or the browser harness's look switched off) the flames get their model pose back.

**Browser.** `OpenTPW.Web.csproj` embeds `content/evening` (347,862 bytes) and its `RequireExtensionContent` target fails the build without `content/evening/objects.json`; the server image copies the folder (`source/OpenTPW.Server/Dockerfile`).

**Captures** (Debug builds): `--debug-torches N` places N of the theme's torches through the level's own placement on the free cells nearest the camera target (`--capture-camera`'s when given), three cells apart; with `--evening-pace dusk|night` and `--capture-world`; `--capture-interface --debug-hud "buytab:features:Tiki Torch"` shows the buy window's Features tab with that row selected. `[EXT:debug-evening]`

**Labels.**

| Label | What | Status |
| --- | --- | --- |
| `[EXT:evening-torches]` | the torches: content, catalog root, price, the lamp rows and the `mod` match key, the extension becoming a simulation extension | user-approved 2026-10-19 |
| `[EXT:evening-torches-save]` | `opentpw.evening` recorded only while the park holds a torch | user-approved 2026-10-19 |
| `[EXT:evening-torches-flicker]` | the deterministic flicker of a torch's light and the movement of its flame, presentation only | user-approved 2026-10-19 |

## The option and its file

The pace is `GameOptions.Evening` (`EveningPace`: `Minutes20` by default, `Minutes10`, `Minutes40`, `AlwaysDay`, `AlwaysDusk`, `AlwaysNight`), written by name to `save/opentpw-options.json` with the other options. A file from before the option has no
value and gets the default; **an unknown name, a number or a value of another type is read as the default** and the rest of the file is kept (`EveningPaceConverter`), and a value outside the enum is replaced on load (`Normalized`). The OpenTPW
page's row cycles through the six values (click, wheel or arrow keys, wrapping), OK saves it and Cancel puts the saved value back, like the other rows there. The options page's "effective size" text is narrower (its left two thirds) to make
room for the row.

## The browser

Nothing in the extension needs the desktop: the shaders it changed (`test`, `pool-deck`, `pool-basin`, `sprite`, `particle`) are regenerated in `content/shaders/web` (the byte layout of the uniform block did not change, only a float that was
padding is named), the lamp table is embedded with the other `content/data` files, the torches with `content/evening` ([Torches](#torches)), and the options row and the mods screen are the same code. `source/OpenTPW.Web` builds.

## Tuning

Captures of the jungle park (the entrance, with the gate's and the lamp posts' lamps) and of a painted pool with swimmers at day, dusk and night were looked at and the look adjusted: the moonlight became bluer (0.08, 0.14, 0.40), the ambient got its own tint
(blue at night, warm at dusk) because a scalar ambient left the green grass green, the sun now fades faster than the daylight so dusk is clearly dimmer than day, and the sprites and the water were darkened a little. At night the ground and objects are dark and bluish and the
gate and the lamp posts light their surroundings clearly. 

### The open points round (October 18, 2026)

Offscreen, silent captures, `reports/evening/open-points` (untracked), 3024 x 1686, Debug build (the pool options are Debug-only):

1. **Dusk on green ground.** The old dusk (sun 0.41, 0.24, 0.18; ambient 0.675) left grass dark green with only the paths warm. The terrain uses the same `test.shader` arithmetic as objects (the ground, paths and objects all go through one lit shader), so nothing there was different; the values were. The sun now fades with the power 1.8 and is tinted (1.8, 0.85, 0.30) by the glow (an orange of 0.62, 0.33, 0.17 at the middle of the dusk), the ambient level is `0.35 + 0.65 d^1.4` (0.60) and its warm tint is (1.0, 0.64, 0.44). Paths and bricks glow orange, the lawns turn olive gold and the grass is a clearly darker, warmer green than day (`dusk-before.png`, `dusk-after.png`). Full day is exactly unchanged (every term is 1 at d = 1; the day capture and an extension-off capture have identical ground pixels, the guests differ only in where they walk), and night is the approved look (the sun and ambient at d = 0 are the same numbers; the shoulder below touches only lamp-lit highlights).
2. **The ticket-booth doors.** They are not self-lit. The booths are a mesh of the park's `base.MD2` (`ticket_booths`), drawn like all terrain meshes by `test.shader` with its plain alpha blend; their materials are `gte_rof1`, `gte_rof2` (the glass, flag `TRANSPARENT`, mean alpha 121 of 255), `gte_rof3`, `gte_wal1` and `gte_wal2`, with no unlit or additive flag. The bright panels are the opaque `gte_rof3` roof panels, albedo 221, 221, 228 of 255: the lightest texture in the entrance, so at night the moon and ambient give them a blue-grey (`night-no-lamps-doors-albedo.png`), and the street lamps beside them add 1.0 of warm light on top, which clips a near-white surface to flat white. So they do follow the time of day, but a white albedo under a lamp saturates. The fix is the soft shoulder in `test.shader` (see "What gets dark"): at night the panels' pixel went from about 170, 169, 203 to about 106, 104, 126 (`night-before-doors.png`, `night-after-doors.png`). Every other draw of `test.shader` gets the same shoulder, which only matters where a lamp or a white texture pushes a channel past 0.4.
3. **The pool lamp and the underwater light.** They had never been captured because `--debug-light` and `--debug-pool` are Debug-only and `--debug-light` placed its light relative to the live camera, which has not yet moved to the capture camera when the light is added. `--debug-light` now takes the held `--capture-camera` ground target as its origin (like `--debug-pool`), and `--debug-extras` logs each extra's engine position. A capture setup that needs no new option: `--debug-pool default --debug-swimmers 6 --debug-pool-offset 3,3 --debug-extras --capture-camera 36,-46,70,-90 --evening-pace night|dusk` (`pool-lights-*.png`). Findings: both lights switch on at dusk and night and the lamp follows the lamp level; the lamp's radius 55 lit a huge area of the lawn and the underwater light's radius 50 flooded the dry grass beside the pool with aqua, because a point light has no occlusion. Fixed in the table: the lamp is radius 42 and (0.95, 0.78, 0.48), the underwater light sits 14 model units into the pool and 6 below ground level with radius 26 and (0.30, 0.85, 1.0), so it lights the basin walls and the water's edge and only a little of the grass (`pool-lights-night-before.png`, `pool-lights-night-after.png`, `pool-lights-dusk-after.png`). The water itself stays unlit (its basin shows through it and carries the glow); making the water surface read the lights is not done.
4. **Hundreds of lamps in the browser.** Measured, see [The browser](#the-browser): no change needed.
5. **Particles and the sky.** The spray droplets and the world's alpha particles take the sprite tint (above). The park sky is never in view: captures at the camera's two extremes (zoom 40 at night, zoom 300 at dusk, `zoom*-no-sky.png`) show only ground, and the wide-zoom check (its progress entry of October 18, 2026) found the view stays at least 8 degrees below the horizon, so the sky tint cannot be judged from any park camera angle and needs no change; the lobby and park-info scenes are not park levels and the extension does not touch them.

## Command-line options

Both work in Release builds as well (unlike most debug options) because performance runs use them:

- `--evening-pace <minutes10|minutes20|minutes40|day|dusk|night>` sets the pace for this run only: it is a run-scoped override in `Level` that is read instead of the option and never written to `GameOptions.Current`, so nothing is saved. It needs the extension on, which it is by default.
- `--debug-evening-lamps <n>` adds `n` lamp lights (the pool lamp's colour and radius) on a square grid 40 engine units (20 cells) across around the park camera's target, so they crowd into the view, that follow the lamp level, for measuring many lamps. The `--perf-run` log lines now also say how many point lights are in view. The browser twin is `--evening-lamps <n>` of `tools/web-perf/run.mjs` ([The browser](#the-browser)).

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

### The lamp cache and the browser (N2)

The lamp source used to walk every placed object on every night frame and look each one up in a table of resolved lamps. It now keeps a list of the lamp-bearing objects and rebuilds it only when the park's object set changes (`ParkObjects.Version` counts placements and
removals; a host without a counter is caught only when the list's length changes, so the level passes the counter; by day the cache is dropped, so it never keeps removed objects alive), so a night frame touches only the objects that carry lamps, in the same order as before, and allocates nothing. `EveningTests` cover that the cache follows placement and removal, resolves each object once per
rebuild and allocates nothing while the set is unchanged. (An `OriginalObject` needs a graphics device, so those tests use stub objects and a resolver seam; the lights themselves are unchanged code, and the with-data runs below show the same number of lights in view before and after.)

Cost of the lamp source per night frame, measured with a temporary stopwatch around `AppendPointLights` (not committed) in the jungle park, Release, `--evening-pace night`, six runs of 3000 frames per cell on a machine with a load average of 20 to 40 (so read the ratios, not the
microseconds). The jungle park has only 14 placed objects, so the placed list was padded with repeats of its own objects (14, 420 and 1400 objects carrying 5, 150 and 500 lamp lights) to stand in for a big park with many lamps:

| Placed objects (lamp lights) | Before: min / median (us per frame) | After: min / median (us per frame) |
| --- | --- | --- |
| 14 (5) | 3.0 / 6.4 | 1.3 / 3.4 |
| 420 (150) | 36 / 67 | 12 / 41 |
| 1400 (500) | 84 / 128 | 45 / 99 |

What stays is the per-lamp work (the node pose and one matrix transform per light). The saving is at most about 0.1 ms a frame in the extreme case, and a few microseconds in a real park: below the noise of a frame.

The whole frame, jungle at night with the park's own lamps (3 lights in view), `--no-vsync --frame-stats --perf-run 3000 --perf-arrivals 3`, three alternating rounds on the same loaded machine, frame average in ms (median of three; the first round of each cell ran into other load):

| Configuration | Frame average (ms), 3 rounds | Median | World render, median |
| --- | --- | --- | --- |
| extension off, before | 3.42, 3.55, 3.01 | 3.42 | 1.19 |
| extension off, after | 3.93, 3.53, 2.80 | 3.53 | 1.19 |
| night, before | 6.72, 3.65, 3.55 | 3.65 | 1.25 |
| night, after | 4.93, 3.30, 3.09 | 3.30 | 1.15 |

Night against off is inside the spread of the runs both before and after (the earlier quiet-machine table above has the same shape: 1.37 ms at night against 1.40 ms off).

**Browser.** `tools/web-perf/run.mjs --scenarios jungle --window 20 --evening night|day|off` measures a second 20-second window after the cold start with the evening look held at night or day, or the cycle and its lamps switched off (hook `Level.PerfEvening`, exported as `PerfEvening`; since the torches made the
extension a simulation one, "off" no longer takes it out of the run's mod set but sets the run-scoped `Level.EveningLookOff`, so the catalog and every other extension stay as they are). Published
Release build, Chrome 155 headless, Apple M5 through ANGLE/Metal, 1280 x 900, Jungle, three runs per mode, medians:

| Window | Main thread busy | fps | Frame p50 | Frame p95 | Frame p99 | Long tasks |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Night | 12.4 % | 59.99 | 1.6 ms | 2.1 ms | 2.8 ms | 0 |
| Day (extension on) | 11.9 % | 59.99 | 1.6 ms | 1.9 ms | 2.3 ms | 0 |
| Extension off | 13.7 % | 60.03 | 1.8 ms | 2.6 ms | 3.3 ms | 0 |

Night costs the browser nothing measurable: the frame is no longer than at day and inside the spread of the runs (the off runs happened to be the noisiest), with no long task. That first set needed no shader change. The shoulder of the open-points round (below) changed `test.shader`, so `content/shaders/web/test.json` was regenerated from it (`WebShaderTests`, the generated JSON stays the desktop shader's twin).

**Hundreds of lamps in the browser (open point 4).** The harness now has the twin of `--debug-evening-lamps`: `tools/web-perf/run.mjs --scenarios jungle --window 20 --evening night --evening-lamps <n>` adds `n` lamp lights on the same 40-unit grid around the park camera's target before the second window (hook `PerfEveningLamps`, exported from `Program.Perf.cs`, built on `Level.SetDebugEveningLamps`, which the native option now shares). Measured on the published AOT build (`-p:RunAOTCompilation=true`, as the live page is built), Chrome 155 headless, Apple M5, 1280 x 900, Jungle, a 20-second window after the steady one, medians of 3 runs per row, on a machine with a load average of 9 to 22 (so read the differences with care):

| Window | Main thread busy | fps | Frame p50 | Frame p95 | Frame p99 | Long tasks |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Extension off | 8.3 % | 59.99 | 1.1 ms | 1.5 ms | 1.9 ms | 0 |
| Night, the park's own lamps | 8.5 % | 59.99 | 1.2 ms | 1.5 ms | 1.8 ms | 0 |
| Night, +50 lamps | 8.3 % | 59.99 | 1.2 ms | 1.4 ms | 1.6 ms | 0 |
| Night, +200 lamps | 8.5 % | 60.01 | 1.2 ms | 1.5 ms | 1.6 ms | 0 |
| Night, +500 lamps | 11.7 % | 59.99 | 1.6 ms | 2.7 ms | 3.1 ms | 0 |

Up to 200 extra lamps cost nothing measurable. With 500 the frame is about 0.4 ms (p50) and 1.2 ms (p95) longer and the main thread about 3 points busier; the same session's own steady window before the lamps read 10.6 % busy and a p95 of 2.0 ms against about 8.9 % and 1.5 ms in the other sessions, so about half of that is the loaded machine. It is not clearly slower (60 fps held, no long task, p99 3.1 ms of a 16.7 ms frame), so no code was changed. (An interpreter build, which is what a plain local `dotnet publish` gives, is slower with any extra lamps in view: p50 2.4 to 3.3 to 3.7 ms for 0, 50 and 500, busy 18.9, 24.1 and 25.2 %; that build is not what the page ships, and no lamp count changes its p95 past 5.6 ms.) A real park has a few dozen lamps at most.

## Determinism

The cycle reads the frame time and the pause state and writes only the renderer's inputs. Two kinds of evidence: the unit tests run the cycle beside an economy run and beside the full synthetic park of `DeterminismTests` (which have no level, so they prove that
the cycle shares no state with the simulation, not more), and a **real level** run: `--perf-run 9000 --perf-speed 1 --perf-hash-tick 11800` on the jungle park logs `Level.ComputeStateHash()` at park tick 11800 and the point lights in view. With the extension off,
at `--evening-pace night` (the lamp source registered, 3 point lights in view) and at night with 50 more debug lamps (53 in view) the hash is the same, `bdafd580cee2a816`.

## Tests

`EveningTorchTests` (no game data): the torches' ids, range and names, their catalog entries (feature, price 50, no upkeep, no research, no attraction value), the models (a lit body and a self-lit flame node), textures and script, the listing only while the extension is on
and never in a Theme Park Inc run, the used-only save (byte-identical without a torch, recorded and refused with the extension off with one, the placed torches' asset keys only), the lamp rows, the flicker (deterministic, bounded, smooth, 0.5 on average, different for neighbouring
torches), the flame's transform (its base fixed, its height within the stretch, a small lean), only the torch rows flickering and naming a flame, bad `flicker` and `flame` values refused, the flame cache following placement and removal, and the browser build's embedding and the server image's copy.
`EveningTests` (no GPU; the data test is inconclusive without `OPENTPW_GAME_PATH`): the daylight curve (full around noon, zero at night, mirrored, monotone, no jumps, wrapping), that **full day is the original's look exactly** (every uniform
and tint, byte for byte), the clock (pace lengths, wrapping, bad steps), the level's step (pausing stops it, a hitch is capped, off creates nothing, null-safe for a level built without its constructor), the options value (default, round trip by
name, unknown values, `CopyFrom`, `Normalized`, the row on the OpenTPW page: cycling, OK and Cancel), every interface language of every text, the extension's registration (on by default, a simulation extension since the torches but recorded in a save only while the park holds one, selectable), the
mods screen row, a park run's **world state hash equal with the extension on and off** (`EveningTests` on the economy and `DeterminismTests` on the full synthetic park with guests, scripts and pools, which also equals the plain run), the uniform
layout (byte 204, the block still 272 bytes) and the shader text of the five changed shaders, the lamp table (the shipped one, every kind of bad row), the lamp resolution on a built model, the pool lamp and underwater light on their shipped models,
and, with the game's data, that every row of the shipped table for original objects matches an original object and every node it names exists (44 lights resolved, counting each theme's copy of an object; the pools' and the torches' rows are checked on their own models).

## Not done

- **Only partly looked at.** The captures above were looked at by the implementer only; the dusk and night values, how dark the sprites should be next to the lit ground, the fog's night colour, the shoulder's strength and the pool lights' look still need a human eye (`--evening-pace dusk` and `night` show them).
- The sun's direction does not change and there is no moon or stars; the shaders' light position is a fixed one.
- The sprites do not brighten near a lamp, the pool water is not lit by the lamps, and lamps do not change the ambient.
- Which original objects get a lamp is a short, hand-picked list (above); shops, sideshows and most rides have none.
- The additive particles (fire, sparks, fireworks) are not tinted, on purpose. The pool water surface is not lit by the underwater light (its basin shows through it). The park sky has no judged tint because no park camera shows it.
