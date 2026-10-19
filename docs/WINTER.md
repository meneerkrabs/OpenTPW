# Winter (`opentpw.winter`, planned)

October 18, 2026. Status: **phases W0 (the research below and the decisions), W1 ([Settling snow](#settling-snow-extwinter-snow), `opentpw.winter-snow`, built), W2 (our own art and data pipeline, [Content](#content-extwinter-content)), W3 (the simulation, [Simulation](#simulation-phase-w3)), W4 (the drawing, [Drawing](#drawing-phase-w4)) and W5 (the cold mood, [Cold mood](#cold-mood-extwinter-cold-phase-w5)) are done.**
The settling snow (W1) is visible; the winter kiosk and the glühwein stall are in the Theme Park World catalogs and guests buy from them; the items are drawn on the guests (W4); the cold rule (W5) is written, off by default.

The request (user, 2026-10-18): three features of our own (`[EXT:winter-*]`), with no original art, script or number table copied: (1) snow that settles on the ground, roofs and objects during snowfall and
melts afterwards; (2) a winter kiosk that sells hats, mittens and earmuffs, worn visibly by the guests; (3) a glühwein stall (the user's choice over hot chocolate; a warm drink with no drunk effects).
Labels: `[EXT:winter-*]` for our additions, `[BIN:TPW-EXE:0x...]` / `[BIN:TPI-EXE]` / `[DATA:...]` for evidence. The shop and sprite pipeline is the umbrellas' one ([UMBRELLAS.md](UMBRELLAS.md), [CONTENT-KIT.md](CONTENT-KIT.md)).

## Decisions (user, October 18, 2026)

- Glühwein, not hot chocolate: a warm drink with **no drunk effects** (Theme Park World has no intoxication state and none is added).
- **Wintry Decembers** are an option (its own toggle) that turns December's rain and thunderstorms into snow in jungle and fantasy. It changes the weather that umbrellas and the rain mood read, so it gets its own pin.
- **Drawing** `[EXT:winter-draw]` (user, October 19, 2026, "Alle drie akkoord"): the items are pulled 1 % towards the camera so an umbrella falls over the hat; mittens turn with the forearm; hats and earmuffs are drawn on costumed guests too. `[EXT:debug-winter]` is a capture and test hook only.
- **Cold** is a separate option, `opentpw.winter-cold`, default off: in snow -2 happiness per qualifying day, +1 back each for a head item, mittens and a recent glühwein, capped at 0; with it on it replaces the rain mood's snow +1 (the user, October 19, 2026: only the happiness +1; the thirst and hunger snow effects stay as TPI's `[EXT:winter-cold-replaces-snow-cheer]`). In Halloween, where every snowfall has lightning and the rain mood reads a storm, the same holds for the storm row: with cold on and the winter signal saying snow, the rain mood's happiness of that day is left out whichever row it would use ("Kou vervangt storm-humeur", the user, October 19, 2026), thirst and hunger stay. A glühwein warms for the rest of its day plus the next game day, `WarmEffectDays` 1 (the user, October 19, 2026: "Rest van de dag + de volgende dag" `[EXT:winter-cold-warm-window]`).
- Defaults: settling snow **on** (presentation only), winter kiosks **on** (recorded in a save only for parks that use them), cold **off**. Paths under snow look like **slush** (greyish and wet), so they stay readable.
- Coordinator defaults for the remaining open questions (the user can still change them): kiosk **25** and glühwein **30**, both available from the start (no research item); **one kiosk** that gives a head item first, then mittens;
  the snow cover is saved as an optional unhashed field; the "Spices" slider keeps Ice's mechanics so sales count as drinks; mittens stay invisible on the original (1x) guest sprite sets. (The coordinator's default "the kiosk sells only in snow or in December to February" was replaced by the user's own decision below.)
- **The winter kiosk sells all year** (user, 2026-10-18/19, asked directly: when does the winter kiosk sell? Answer: "Het hele jaar, vraag via seizoen"). There is no snow or December-to-February gate and `[EXT:winter-kiosk-gate]` is retired. Demand follows the season terms of its `.sam` (winter 150 %, spring and autumn 60 %, summer 20 %, the same numbers the data gives the stall) and the snow (200 %). A guest with both slots full still scores 0; that is the one rule left, not a season gate. This replaces the earlier "Alles akkoord" for that one label only.

## Research findings

**Where it snows today (`ParkWeather`).** One quality (1..100) per weather period; the converter `0x512f00` turns it into bands: `Snow` when the quality is in `WeatherEffects.QualityForSnowLow..High`, rain in the rain band,
a coin flip (`rand & 1`, even = snow) when both hold, lightning in its own band. Snow has drops too: target = (100 - intensity) x `MaxRaindrops` (600) / 100, ramped by `SpeedOfChange` (20) a park turn. A period lasts
`DaysBetweenChanges` (7) days, rolled `DaysOfWarning` (4) days ahead. A game day is 23.04 turns, 5.7 s at normal speed; a year is about 35 minutes.
- The bands per theme (`Data/levels/<theme>/Standard.sam`, and the same in `Online_Standard.sam`): jungle and fantasy snow -1..-1 (never), rain 0..40, lightning 0..15; space snow and rain -1..-1 (dry lightning only);
  **hallow snow 0..25, rain 15..45, lightning 0..25**. So **only the Halloween world ever snows**. `[DATA:hallow/Standard.sam:WeatherEffects.QualityForSnowLow/High]`
- `Seasons[0..3]` (all themes): averages 75/80/90/50, tolerances 25/20/10/15, exceptional chances 10/10/5/10 %. The season is the roll date's month / 3 (Jan-Feb row 0, Mar-May 1, Jun-Aug 2, Sep-Nov 3) and **December is row 4**,
  one past the table: average = the array's count slot (4), tolerance = `DaysOfWarning` (4), chance = `DaysBetweenChanges` (7 %) (`[BIN:TPW-EXE:0x512e10]`, `ParkWeather.Roll`). A December roll is 1..7 in 93 % of cases.
- Consequence (computed from those numbers): in hallow a roll made in December is snow (with lightning: thunder-snow) with probability 0.93 + 0.07 x 0.205 = **94 %**; every other roll is snow only through the exceptional
  branch (q <= 14 always, 15..25 half): about 2 % a roll (1 % in summer). Rolls apply 4 days later, so hallow is white from about 5 December to about 10 January, plus about one stray spell a year: **roughly 42 snowy days a year,
  about 4 real minutes per 35-minute year**, in spells of 40 s. In jungle and fantasy every December is thunderstorms (q 1..7 is in the rain and lightning bands).
- Snow visuals today: the CRain port (`WeatherRenderer`, `WeatherVisualState`, `content/shaders/weather.shader`, [AUDIO.md](AUDIO.md) "Weather visuals"): falling flakes from `Data/Generic/Weather/snow.tga`, nothing on the ground.
  `--debug-weather 8` forces snow and lightning on hallow (`[EXT:debug-weather]`), which every capture of the work below uses.

**tp.exe has no ground snow.** Its only "snow" strings are `QualityForSnowLow` / `QualityForSnowHigh` (key table `0x741ee8`, `0x741f24`) and `Data\Generic\Weather\snow.tga` (`0x765218`; a byte scan of the image re-run for this page finds
exactly these three and no "frost", "winter" or "cold" string). The path is referenced three times (`push 0x765218`, found again by the same scan), all CRain texture loads: `0x5508cd` and `0x550994` (the weather texture set on the CRain
object `[0x8bcbac]`, slot 2, through `0x57d7f0`) and `0x57d857` (CRain's own load into `+0x3348`). Confidence: high for "no snow asset or key beyond the flakes", medium for "no weather input to the landscape" (static,
string-anchored, not a data-flow proof).

**Theme Park Inc.** The same converter with a snow band of 0..20 in all three worlds; its own season roller gives snow chances (water world 40 % in Jan-Feb, science 25 %, arabian 5 %;
`[DATA:TPI levels/*/Standard.sam:Seasons[].PercentageChanceForSnow]`). Rules around snow: the kid row snow +1 happiness, thirst -1, hunger +1 (parents -2, elderly -3); `Arrival.SnowPercentageModifier` 75;
`Vegetation.SnowWither` 1.0; a `ClosedDuringSnow` key and `mClosedDueToWeather` per attraction (strings in `Game.exe`). Its water world's "snowfield" ([THEME-PARK-INC.md](THEME-PARK-INC.md)) is **painted terrain texture**, not dynamic snow.
TPI's shop data is the best precedent for demand: drinks `WeatherModifier.SnowPercentage 50` ("people don't want cold drinks in snow"), ice cream 70, hot dog and costume shop 200, chips and gift shop 150, brolly 150.
OpenTPW runs no TPI weather (ARRIVAL-008).

**Drink shops.** TPW's drinks shops (fantasy/space `drinks.sam`, jungle `Coconut.sam`, hallow `Pumpkin.sam`): `ShopType 4`, `SpecialIngredient 3` (Ice), price 30, cost of goods 20, `ThirstEffect 40`, `HungerEffect 0`,
`VomitEffect 10`, `HappinessEffect 5`, `LitterEffect 50`, capacity 1, upgrade 650-750, `Research.Group 0`, a 2 x 2 shape. Ice gives thirst back: `+ trunc(amount x ThirstEffect / 100)` ([GUESTS.md](GUESTS.md), `0x4fe1e0`); a sale of
ShopType 2/4 with ingredient 3 counts for the "drinks" challenge (type 3, `[BIN:TPW-EXE:0x4e16b0]`) and records the price statistic in category 3 (`QueueJoinRules.PriceStatistics`). The numbers are the original's, read from its
data for the design only; none of it is copied into our files.

**Rendering and the shader slot.** Terrain, the path layer, queue models, placed objects, tracks, coasters and tours all draw with `content/shaders/test.shader`; guests, staff, litter and balloons with `sprite.shader`; pools with
`pool-*.shader`; lakes with `water.shader`. Engine space is Z up; the heightfield has one flat normal per cell quad. test.shader passes the model-space normal and the world position. The path layer is its own `ModelEntity` parts, so a per-draw
value can tell ground, path and structure apart. The 272-byte `ObjectUniformBuffer` has seven padding floats; after `opentpw.evening` ([LIGHTS.md](LIGHTS.md)) they are the ambient level, the point-light count, the view distance and the
ambient tint, and **`_padding6` (byte 268, after `g_vFogColor`) is the last free float of test.shader**. The pool parts use it as their glow, so pool draws get no snow. In the browser every shader is precompiled to GLSL ES 3.00
(`content/shaders/web/*.json`); a uniform-driven branch with procedural noise needs no new texture binding. The weather is outside the canonical determinism hash ([DETERMINISM.md](DETERMINISM.md) "Weather timing"), and presentation state may be saved as optional trailing fields that are not hashed.

**Wearables.** Balloons are presentation-placed sprites (`BalloonRenderer`, `0x004FA030`), their colour a pure function of the guest id. Umbrellas ([UMBRELLAS.md](UMBRELLAS.md)) are the model: `AppearanceEffect` 3 (1 balloon and 2 costume are
tp.exe's), own sprites rendered with the pools kid camera at 2x (256 ppu), five directions mirrored to eight, placed at an anchor from the rig. The 2x kid sets have eight land slots, 35 frames (stand 1, walk 8, run 8, gesture-a 4, gesture-b 4,
stumble 4, cheer 4, hands-on-hips 2) x 5 directions.

## Design (phases W1, W3 to W6)

All labels below are ours; nothing here is built yet except the content.

- **Three extensions, split along the determinism line.** `opentpw.winter-snow` `[EXT:winter-snow]`: snow cover, code, `AffectsSimulation: false`, never recorded in a save's mod set. `opentpw.winter` `[EXT:winter-extension]`: the kiosk, the
  stall, the warm items and their drawing, `AffectsSimulation: true`, recorded only while the park has one of its shops or a guest with a warm item or warmth (the used-only rule of umbrellas and pools).
  `opentpw.winter-cold` `[EXT:winter-cold]`: the cold mood rule, `AffectsSimulation: true`, always recorded while on. None is offered in a Theme Park Inc run.
- **Snow cover** `[EXT:winter-snow-cover]`. One scalar 0..1 per park, stepped once per park turn from the weather: while it snows, `cover += Accumulate x CurrentDrops / MaxRaindrops` (full after about 1.5 game days of heavy snow);
  otherwise `cover -= Melt` (gone after about 3 dry days, three times faster in rain). The renderer writes cover x a surface factor (ground and structures 1.0, the path layer -0.35 = slush look, 0 for cars, markers, sign text and lobby
  scenes) into `_padding6`; test.shader whitens up-facing surfaces with value noise so snow starts in patches and melts back into them; shading, lights and fog apply after, so snow darkens at night by itself. Sprites, lake and pool water and the sky
  are not covered. `[EXT:winter-snow-surfaces]`, `[EXT:winter-snow-paths]`, `[EXT:winter-snow-save]` (an optional, unhashed saved cover).
- **Winter kiosk** `[EXT:winter-kiosk]`. A new `AppearanceEffect` value **4** `[EXT:winter-items]`. A guest has two item slots (`WarmItems` bits: 1 hat, 2 earmuffs, 4 mittens): a purchase fills the head slot first (hat or earmuffs, by a hash of
  the guest id, about 2 hats to 1 earmuffs), then the hands (mittens). Colours are a hash of guest id and item (as the balloon's), so a purchase takes **no extra draw** from any stream `[EXT:winter-item-colour]`. The score is 0 for a guest whose slots are
  full; there is no season or weather gate: the kiosk sells all year and its demand follows the season and the snow (the user's decision, see Decisions). Drawn on costumed guests too (head item only, from the measured head anchors of the costume sets; [Drawing](#drawing-phase-w4)).
- **Glühwein stall** `[EXT:winter-gluehwein]`. A drinks shop as the originals are (`ShopType 4`, `SpecialIngredient 3`, so the drinks code path, challenge and price statistics apply); the slider is labelled "Spices" for this object only
  `[EXT:winter-spice-label]`; `VomitEffect` 5, no new guest state, speech or animation `[EXT:winter-gluehwein-no-drunk]`. Demand: no gate; snow 250 %, rain and storm 130 %, winter months 150 %, spring and autumn 60 %, summer 20 %. With
  `opentpw.winter-cold` on, a glühwein warms the guest for one game day.
- **Cold mood** (`opentpw.winter-cold`, off by default). Once per qualifying day per guest, in snow only: happiness += -2, +1 for a head item, +1 for mittens, +1 while warmed, capped at 0. With `opentpw.rain-mood` also on, the cold rule
  replaces the rain mood's happiness in snow (the user's decisions: snow's +1, and in Halloween, where the rain mood reads a storm, the storm's -1), so an undressed kid nets -2 a qualifying day and a dressed one 0, in Halloween too.
- **Wintry Decembers** `[EXT:winter-weather]` (an option): December's rain-band rolls in jungle and fantasy (rain and thunderstorms) give snow instead; its own pins.
- **Saves and determinism.** Snow cover: no hash part, no draw from any stream, no mod-set record. Winter: optional guest fields `WarmItems` and `WarmUntilDay` written and hashed only when set, so older JSON and every park without the extension
  stay byte-equal; off, or on without shops and items, every pin is unchanged. New pins arrive with W3 and W5.
- **Demand keys** (read for the extension's own objects only; unknown to tp.exe, harmless in the data): Theme Park Inc's `WeatherModifier.Snow/Rain/StormPercentage`, our own `SeasonModifier.Winter/Spring/Summer/AutumnPercentage`
  `[EXT:winter-season-terms]` and `WinterInfo.WarmEffectDays` / `WinterInfo.IngredientLabel`.

## Phases

| Phase | What | State |
| --- | --- | --- |
| W0 | this page: the findings and decisions | done |
| W1 | `opentpw.winter-snow`: `SnowCover`, the uniform slot, test.shader and its web JSON, per-entity surface factor, path slush, the optional save field, tests, hallow captures | done ([Settling snow](#settling-snow-extwinter-snow), `opentpw.winter-snow` 1.0.0, on by default) |
| W2 | art and data pipeline: the eight shop models, the two `.sam`, the shop scripts, the item sprites, the anchors, `content/winter`, `tools/winter/build-content.sh`, content tests; nothing wired (until W3) | done |
| W3 | simulation behind `opentpw.winter`: catalog root, `AppearanceEffect` 4, the kiosk's rule and terms, the stall's terms, saves, the used-only rule, pins, and the wintry Decembers option | done ([below](#simulation-phase-w3)) |
| W4 | drawing: the warm-item renderer (`WarmItemRenderer`, sharing the umbrellas' `TryMount` and pull code), head anchors of the original kid and costume sets, browser delivery, captures | done |
| W5 | `opentpw.winter-cold` with its measurement and pins | done (see [Cold mood](#cold-mood-extwinter-cold-phase-w5)) |
| W6 | tuning from captures; optional hats on riders' heads, pool-deck snow, sheltered-ground masking | planned |

## Settling snow (`[EXT:winter-snow]`)

Phase W1 (October 18, 2026). `opentpw.winter-snow` 1.0.0 is a presentation-only extension (`AffectsSimulation` false, on by default, listed on the Extensions and mods screen in every interface language). It shows wherever the
weather snows (under the shipped weather only the Halloween world, see above); `--debug-weather 8` forces snow for captures, and `--debug-snow-cover <0..1>` (`[EXT:debug-snow-cover]`) sets the cover directly.

- **The cover** (`SnowCover`, `World/Winter/`) `[EXT:winter-snow-cover]`: one scalar 0..1, stepped once per park turn from the weather's own turn (`ParkWeather.TurnStepped`, so it freezes while the park is paused and runs with the speed).
  While it snows with drops falling, `cover += Accumulate x CurrentDrops / MaxRaindrops` with `Accumulate` 1/35 (full cover after 35 turns of the full drop count, about 1.5 game days); otherwise `cover -= Melt` with `Melt` 1/70 (gone after
  about 3 dry days), three times faster while it rains. The drawn value eases toward the turn value per frame (rate 3 per second, real time) so the 4 Hz turn steps do not show. No random number is drawn and nothing reads it back.
- **The draw** `[EXT:winter-snow-surfaces]`: `ModelEntity.SnowSurface` (default 0) times the drawn cover goes into test.shader's `g_flSnowCover`, the last free float of the uniform block (`_padding6`, byte 268, after `g_vFogColor`; the point
  lights still start at byte 272, the block did not grow). Ground, coaster pieces and every placed object (roofs, trees, rides) have factor 1; the path layer, the queue models and the ride track tiles (kart roads, water channels: what cars and boats run on) have -0.35 `[EXT:winter-snow-paths]` (negative = slush:
  a wet greyish brown, so paths and tracks stay readable under snow, the user's decision for paths, extended to tracks in the W1 review); cars, markers, sign text, lobby scenes, water, sprites and the pools (their shaders keep the slot for the glow) have 0.
- **The shader**: after the light is known, an albedo blend toward white (slush: grey brown) with two octaves of value noise on the world position (no texture, no binding) and a smoothstep on the world-space up component of the normal (walls and
  cliffs stay bare), `m = smoothstep( n - 0.08, n + 0.08, cover x slope x 1.15 )`, so snow starts in patches and melts back into them. The target colour is divided by the draw's light level when that is above 1, so a lit white lands near 0.9
  instead of burning out, while the evening's dark ambient still darkens it at night. Full cover is not a flat white: the snow's brightness follows the albedo's relief, a fine grain (a third noise octave at 11x) breaks it up, and 5 % of the albedo still shows through. Cover 0 skips all of it: a capture of the hallow park with the extension on and cover 0 is **byte-identical** to one with the extension off (`cmp`).
  Review changes: the noise hash is sin-free (Dave Hoskins' hash12 on a lattice cell wrapped at 2048), because a `fract( sin( dot ) )` hash bands on ANGLE/D3D and mobile GPUs at world positions around 1e5 and is no cheaper; parts on an animated node of an object (`ModelEntity.SnowNoiseInModelSpace`, a positive cover plus 2) take their noise from the model-space position (`vLocalPosition`), so the patches turn with a rotating ride part instead of crawling over it
  (static parts keep world-space noise, so identical objects and track tiles do not repeat one pattern); a corner whose material is path-like (`TextureSlotAddressing.SlushFlag`, vertex bit 30, from the texture name: `*path*`, `*pav*`, `grd_ctr*`, `grd_krb*`) draws as slush whatever its entity, so the entrance plaza and its kerbs (terrain batches) and the pavement slabs of objects stay readable like the path layer. The extension is not offered in a Theme Park Inc run (`FeatureGates.WinterSnow`).
  `content/shaders/web/test.json` is regenerated (`WebShaderTests`); the generated GLSL ES 3.00 compiles and links in Chrome's WebGL2 and its uniform block is 784 bytes (272 + 16 lights x 32).
- **Saves** `[EXT:winter-snow-save]`: `ParkResumeSnapshot.SnowCover`, an optional `double`, written only while the extension is on and the cover is above 0 (otherwise the member is left out, so a park without snow saves the same bytes as
  before), validated on load (above 0, at most 1, finite) and ignored while the extension is off. It is in no hash and no mod record.
- **Determinism**: presentation only by construction (the cover reads `ParkWeather` through the pure getters of `TurnStepped`, writes nothing back, draws no random number; `SnowCoverTests` assert the weather's whole snapshot and trace are
  identical with and without a subscribed cover, that no file outside the presentation and save paths mentions the cover, and that the economy hash is unchanged at every cover). On the real level the `--perf-hash-tick` hash
  (`--perf-run 9000 --perf-speed 1 --perf-arrivals 3 --perf-hash-tick 4000`) is **not stable on `main` either**: the park opens a fixed 30 frames after the level is up but the park clock runs in real time through loading, so the start phase
  of the tick count against the arrival timer varies (park tick 346 or 349 at frame 600) and the hash follows the phase (hallow, `main`: `62b935be21b6182c` in four runs of five at phase 346, `cf01299b9d2e0cb9` at phase 349). Compare runs only
  at the same phase (the `frame 600` log line). At equal phase the extension changes nothing: the branch gave `62b935be21b6182c` in four of four runs at phase 346, and with `--debug-weather 8` the extension off and full cover forced both gave
  `3c4867e8e8737061` at phase 347. Details in the W1 progress fragment.
- Not covered: sprites, lake and pool water, pool decks, the sky; ground under a roof whitens too (no occlusion data). Wintry Decembers (`[EXT:winter-weather]`) is W3.

## Cold mood (`[EXT:winter-cold]`, phase W5)

`opentpw.winter-cold` is off by default, always recorded in a save while it is on and not offered in a Theme Park Inc run. Code: `World/Winter/WinterColdRules.cs` (the pure rules), `GuestSimulation.WinterCold.cs` (the step and the warmth), `GuestSimulation.cs` (`UpdateNeedsTurn`).

- **The step.** Right after the rain mood step, on the same phase gate (every fourth park turn, for every guest in every state), in snow only, as the winter shops read the weather (`GuestSimulation.WinterWeatherState`, `WinterRules.WeatherStateOf`: snow is snow even with lightning, which is every Halloween snowfall; the rain mood and the umbrellas keep their own map, where that is a storm) `[EXT:winter-season-terms]`: once per qualifying day,
  happiness += `ColdHappiness` (-2) + `HeadItemBonus` (1, hat or earmuffs) + `MittenBonus` (1) + `WarmBonus` (1, warmed by a glühwein), the sum capped at 0, then clamped to 0..100 as a float (`MeterMath.Add`). A fully dressed guest is
  neither cooled nor cheered. Thirst and hunger are not touched.
- **Day gate.** The rain mood's: a guest's age counts from its time in the park, an imported original guest's from the load (`RainMoodAgeTurnsOf`; the import marks it while either rule is on), and `UmbrellaRules.DayGateOpen` lets the whole first day through and then every
  fourth day. The mark is the guest's own `ColdMoodDay` (day + 1 of the last application), so the rule runs with or without the rain mood. `[EXT:winter-cold]`
- **Replaces the rain mood's happiness in snow.** With both on, whenever the winter signal says snow (`GuestSimulation.ColdOwnsHappinessToday`: cold on and `WinterWeatherState`, falling back to the umbrellas' map, reads snow) `RainMoodRules.Apply` leaves out the happiness of the row it would
  use: the snow row's +1 in an ordinary snowfall, or the storm row's -1 in Halloween, where every snowfall has lightning and the umbrellas' map (which the rain mood reads) says storm. Its thirst and hunger of that row stay (snow: thirst -1, hunger +1; storm: 0, 0 as shipped), and so does everything
  in rain, in a storm that is not snow and with the cold rule off (nothing changes for the rain mood then). So the cold rule's happiness is the only happiness there: an undressed kid nets -2 a qualifying day and a dressed one 0, in Halloween too. `[EXT:winter-cold-replaces-snow-cheer]`
  (user, October 19, 2026: "Alleen de humeur-+1" for the snow row, "Kou vervangt storm-humeur" for Halloween's storm row.)
- **A recent glühwein.** At a glühwein stall (a winter shop that is not the kiosk, with the Ice ingredient) a guest's `WarmUntilDay` becomes the park's `ParkCalendar.DayIndex` + 1 + `WarmEffectDays` (1) if that is later than what it had: warmed for the
  rest of the drink's day and the next game day. Only with the rule on, so a park with the rule off keeps its saves and hashes. No random draw. `[EXT:winter-cold-warm-window]`
- **Measured.** A headless kid (`WinterColdTests.ASnowyWeekCostsAKidWhatTheDocumentationSays`) over a snowy week (7 game days, 162 park turns of snow, a visit that starts at the first snowflake, so the gate opens on days 0 and 4: two applications), happiness lost or gained
  against the same kid with no rule, in two skies. Synthetic snow (the umbrellas' map and the winter signal both say snow, as in a wintry December):

| Protections | Cold alone | Cold + rain mood | Rain mood alone (today) |
| --- | --- | --- | --- |
| none | -4 | -4 | +2 |
| head item | -2 | -2 | +2 |
| head item + mittens | 0 | 0 | +2 |
| head item + mittens + glühwein | 0 | 0 | +2 |

  Real Halloween snow (a `ParkWeather` of the Halloween bands at quality 10: snow and lightning at once, so the map reads a storm and the winter signal snow). The cold column is the same; the rain mood alone is the storm's -1 per application, which the cold rule replaces:

| Protections | Cold alone | Cold + rain mood | Rain mood alone (today) |
| --- | --- | --- | --- |
| none | -4 | -4 | -2 |
| head item | -2 | -2 | -2 |
| head item + mittens | 0 | 0 | -2 |
| head item + mittens + glühwein | 0 | 0 | -2 |

  So one protection halves the cold, two remove it, and the third (or the glühwein in place of a garment) only matters for a guest that lost one of the other two. The rule is gentle: -4 happiness in a week of snow, while the needs step moves a guest by
  tens of points over the same time, and snow only falls about 42 days a year, in the Halloween world only (see above). The numbers are the extension's code defaults and its version (1.0.0) is the rule identity.
- **Saves and determinism.** `ColdMoodDay` is an optional guest field written, validated (never negative) and hashed only once set; `WarmUntilDay` and the items are W3's. Rule off: every pin stands (`DeterminismTests.EveryExistingPinIsUnchangedWhateverTheColdSwitch...`). New pins:
  `WinterColdFixedRunHash` and `WinterColdEverythingFixedRunHash` ([DETERMINISM.md](DETERMINISM.md#winter-cold)). A park saved mid-snow resumes with the same hash (`WinterColdTests.AParkSavedMidSnowContinuesIdenticallyAfterAResume`).
- **Options row.** "Cold mood" with its description in all eleven interface languages, between the wintry Decembers and the restored advice.

## Content (`[EXT:winter-content]`)

`content/winter` (copied next to the program with the rest of `content/`; not yet embedded in the browser build) holds, all our own, generated from code and committed (CI has no Blender). The content is MIT-licensed like the rest of the
repository; nothing in it comes from Theme Park World or Theme Park Inc, and no original bytes or derived pixels are read at any step.

- `objects.json`: eight shops, one **winter kiosk** and one **glühwein stall** per Theme Park World theme, ids **64010 to 64017** inside the extension's own range **64010 to 64499** (jungle kiosk 64010, jungle stall 64011, hallow 64012 and
  64013, space 64014 and 64015, fantasy 64016 and 64017), category `shops`, `"research": false`. The range sits below the umbrellas' 64500..64999 and the pools' 65000..65535 and above the Theme Park Inc rides' 40000..40499. The
  first ten ids of the 64000 block, 64000 to 64009, belong to the example lamp mod (`content/mods/opentpw.example.lamp-lights`), which is why the winter range starts at 64010. The extension's id is `opentpw.winter`, so a park save will name
  the files `mod:opentpw.winter:/...` (as [POOLS.md](POOLS.md#content-extpools-content)). Names in the eleven interface languages.
- `objects/<theme>/<shop>/` (`kiosk` and `gluehwein`): the MD2 model, 8 or 9 64 x 64 TGA textures (`wk_` and `wg_` prefixes), `Kiosk.sam` / `Gluehwein.sam` and the shop script. Both are **2 x 2 market huts** whose entrance is the lower left cell:
  a back wall, two side walls whose tops follow the roof, a counter, two front posts and a **pitched roof** (a closed prism with the ridge over X = 10, so the front is an open gable and the counter shows; the slopes face up, so the snow cover
  lands on them with no extra work). The kiosk has a hat rack on the back wall with bobble hats on pegs, a mitten garland under the roof's front and folded scarves on the counter; the stall has a copper pot on a stove, mugs, a lit sign and a chimney.
  Themed textures as the pool cabins and the brolly shop: bamboo and thatch (jungle), dark mossy timber with purple shingles and a pumpkin on the ridge (hallow), riveted metal with cyan strips (space), pastel candy (fantasy).
  The walls, counter, posts and every hanging prop keep at least 1 unit from the footprint edge at a walker's height (the pools lesson, `[EXT:pools-model-inset]`); the roof reaches 1.0 from it. Backface culling is on for every material.
- The **`.sam`** (`tools/winter/kiosk.sam`, built from the keys of the original balloon shop; `tools/winter/gluehwein.sam`, from the original drinks shops): the kiosk has `AppearanceEffect` 4, `ShopType` 1 (a gift shop's kind; not 5, which
  would count as costumes), price 25, cost of goods 10, `HappinessEffect` 15, `LitterEffect` 5, thirst, hunger and vomit 0, `ISIndoors` 0, capacity 5, upgrade 1000, `RunningCost` 30, demand `WeatherModifier.SnowPercentage` 200 and
  `SeasonModifier.WinterPercentage` 150, spring and autumn 60 and summer 20 (it sells all year). The stall has `ShopType` 4 and `SpecialIngredient` 3, price 30, cost of goods 15, `ThirstEffect` 30, `HungerEffect` 5, **`VomitEffect` 5**, `HappinessEffect` 15, `LitterEffect` 40,
  capacity 1, upgrade 750, `WinterInfo.IngredientLabel` "Spices", `WinterInfo.WarmEffectDays` 1, and the demand percentages of the design above. The `WeatherModifier.*`, `SeasonModifier.*`, `WinterInfo.*` and `RunningCost` keys are
  unknown to tp.exe; the winter extension reads them for its own objects only (W3, [Simulation](#simulation-phase-w3)).
- The **`.RSE`** (`tools/winter/kiosk.rse`, `gluehwein.rse`): the brolly shop's script (customers go in hidden, are served, come out) with the stall's longer serving time. No steam particle: none of the existing particle keys fits a pot.
- `esprites/Winter/SPR_WI.ESP` + `.TPC` (28 KB in all): the **warm item sprites**, rendered by `tools/blender/opentpw_winter_items.py` with the pools kid camera (orthographic, 45 degrees, the same lights, grade and dark edge) at **2x
  (256 pixels per unit)**, five directions (the game mirrors them to eight), one 255-colour palette. Slots 0 to 2 bobble hats (red, blue, striped green and cream), 3 and 4 earmuffs (grey band with red cups, navy band with pale blue cups), 5 to 7
  mittens (red, blue, green; frame 0 the right mitten, frame 1 the left; a short elongated knitted mitten with a thumb bump and a ribbed cuff in the same wool tone, continuing the forearm line, no contrasting rim). Hats and earmuffs are rendered over a **holdout head proxy** (the rig's skull ellipsoid), so the part behind the head is already cut out and the sprite can simply be drawn in
  front of the guest. The **hotspot of every frame is the item's attach point**: the top of the skull for a hat, the head centre for earmuffs, the hand end point for a mitten.
- `anchors.json`, **schema 2**: for each of the eight 2x kid sets and **every land slot, frame and direction 0 to 4** (35 x 5 records) the head-top point, the head centre, both hand end points with the forearm's screen angle, and a visibility flag
  per hand (a ray from the hand towards the camera against the body), plus the head's yaw and pitch per frame; pixels at 256 per unit from the feet hotspot. Written by `opentpw_guest_sprites.py --emit-winter-anchors` into its own file: the
  umbrellas' schema 1 `anchors.json` and the `--emit-anchors` code path that writes it are unchanged and byte-identical (a test also checks that the winter table's right hand agrees with it). Directions 5 to 7 mirror 3 to 1: negate every x
  and angle, keep the hand's identity. Exact by construction (the same camera as the frames), rounded to 0.1 pixel; 108 KB. The anchors of the original (1x) kid sets and of the costume sets are in `anchors-original.json` (W4, `opentpw-kit measure-head-anchors`, see [Drawing](#drawing-phase-w4)), a measuring tool that
  writes numbers only; mittens stay invisible on those).
- Rebuild: `tools/winter/build-content.sh` (Blender 5.x, the .NET SDK and python3; about a minute): the shop models (`opentpw_winter_shops.py`, which reuses the pool cabin's textures and geometry builder), the kit's `object` command, the item sprites,
  `tools/winter/pack_sprites.py` (the pools' set converter, `detailFlag` 0) and the anchors.

`WinterContentTests` load the committed files through the game's own pipelines (the object catalog with a content root made by the test, the sprite readers, the ride script VM) and check the ids and that no other content range overlaps, the `.sam`
keys, the models, textures, footprint and wall gaps, the scripts, the sprite banks (slots, directions, frames, 256 ppu, hotspots inside the frames), the anchor table (every set, slot, frame and direction, agreement with the umbrellas' table, hands and
head inside the shipped kid frames) (the wiring of the content is checked by `WinterWiringTests`).

## Simulation (phase W3)

October 18, 2026. `opentpw.winter` (1.0.0, simulation, **on by default**, used-only in saves, not in a Theme Park Inc run, `FeatureGates.Winter`) lists the eight shops of `content/winter` in the Theme Park World catalogs
(`WinterContent.CatalogRoot` in `ModContentRoots.CatalogRoots`, the id in the `ObjectCatalog` cache key) and runs the rules below. `opentpw.winter-weather` (1.0.0, **off by default**, `FeatureGates.WinterWeather`) is the wintry Decembers option.
Both have their names and descriptions in the eleven interface languages (the Extensions and mods screen). The kiosk and the stall are TPW-only: the extension's content is not offered in a Theme Park Inc run.

- **The kiosk's score** `[EXT:winter-kiosk]`. The host score of a winter kiosk (`GuestSimulation.WinterHostScore`, for the extension's own objects only; the decision interface is untouched) is 0 for a guest whose two slots are full (a head item and mittens; `WinterRules.KioskScoresZero`) and otherwise the game's score times the weather and season percentages below.
  **There is no season or weather gate: the kiosk sells all year** (the user's decision of 2026-10-18/19, "Het hele jaar, vraag via seizoen"; `[EXT:winter-kiosk-gate]`, which sold only in snow or December to February, is retired). The month comes from `GuestSimulation.WinterMonth`, read from the park's clock.
- **The winter shops' weather signal** `[EXT:winter-season-terms]`. The shops read the weather through their own signal (`GuestSimulation.WinterWeatherState`, `WinterRules.WeatherStateOf`): snow is snow whether or not lightning flashes, then storm, then rain. The umbrellas and the rain mood keep the shared map `[EXT:umbrellas-weather-map]` (`UmbrellaRules.WeatherStateOf`: lightning
  is a storm first). The two differ only for snow with lightning, which is every Halloween snowfall (its snow band 0..25 is its lightning band): the umbrellas and the rain mood still read a storm there (no pin moves), the winter shops read the snow, so the kiosk's 200 % and the stall's 250 % snow bonuses apply in Halloween. A wintry December's snow has no lightning and reads the same in both.
- **The items** `[EXT:winter-items]`, `[EXT:winter-item-colour]`. `UsageInfo.AppearanceEffect` **4** is this extension's (1 balloon and 2 costume are tp.exe's, 3 is the umbrellas'). A purchase fills the head slot first, with a hat for two guests in three and earmuffs for the third, chosen by a hash of the
  guest id, then the mittens; a third purchase gives nothing and a guest never has two head items. The colours are a hash of guest id and item (`WinterRules.ColourOf`, three for hats, two for earmuffs, three for mittens, the sprite slots of `SPR_WI`, `WinterRules.SpriteSlotOf`): **a purchase takes no draw from any
  stream**. Nothing takes an item off: the guest keeps it until it leaves, whatever the weather. A costume does not remove the items (a costumed guest shows its head item, [Drawing](#drawing-phase-w4)).
- **The glühwein stall** `[EXT:winter-gluehwein]`, `[EXT:winter-spice-label]`, `[EXT:winter-season-terms]`. A normal drinks shop: `ShopType 4`, `SpecialIngredient 3`, so the use routine, the drinks challenge and the drinks price statistics are the original's. Evidence, tp.exe: the sale counter `0x4e16b0` switches on the shop's
  `ShopType` (jump table `0x4e18e4`, index `ShopType - 1`): type 4 goes to the ingredient switch (`0x4e1779`, table `0x4e18fc`, index `ingredient - 1`), whose third slot (`0x4e17f9`) adds 1 to counter 3, the drinks challenge; ShopType 1 (the kiosk's) adds to counter 5 through `0x4e1814`, not to the costume counter 7
  (ShopType 5). The stall has no gate and no drunk effects (`[EXT:winter-gluehwein-no-drunk]`: `VomitEffect` 5, no new guest state). The score is the game's times the weather's percentage of its `.sam` (`WeatherModifier.Rain/Storm/SnowPercentage`) when that is nonzero, then the season's percentage when that is nonzero
  (`SeasonModifier.Winter/Spring/Summer/AutumnPercentage`; December to February, March to May, June to August, September to November), each step truncated, in signed 32-bit arithmetic. A key a `.sam` leaves out is 0, which changes nothing. The kiosk uses the same terms (snow 200, winter 150, spring and autumn 60, summer 20; rain and storm 0).
  The extension reads these keys for its own objects only: an object that merely says `AppearanceEffect 4` is no kiosk.
- **Golden ticket.** The "all researched and built" ticket asks for every shipped buyable object only, as the original does (`ParkObjectives.CheckGoldenTickets`). The objects of `objects.json` files that declare `"research": false` (winter, umbrellas and pools) are `NotResearched` catalogue entries the ticket does not ask for (`[EXT:mods]`), so a park that never places one wins the ticket the same with the extension on or off; no pin moves.
- **Saves.** Optional guest fields `WarmItems` (bits 1 hat, 2 earmuffs, 4 mittens, at most one head item) and `WarmUntilDay` (the park calendar day a glühwein's warmth lasts until; W3 only carries it, the cold rule of W5 writes it), each written, validated and hashed only when set, so an older save, and every save of a park with no
  winter state, has the same bytes. A park uses the extension (`ParkResumeMods.WinterInUse`) when it holds one of its shops or a guest with an item or warmth, and a save records `opentpw.winter` then (asset keys of the placed shops only, as the umbrellas); a save with an item and no record is refused. Off, or on in a park without shops
  or items: every pin is unchanged (`DeterminismTests.EveryExistingPinIsUnchangedWithTheWinterExtensionAndTheWinterWeatherOnOrOffAndNoWinterShop`).
- **Wintry Decembers** `[EXT:winter-weather]`. With the option on, the weather roll of a December (the roll, not the day it applies: it is made four days earlier, and the month is derived from the forecast's apply time, so nothing new is saved) that lands in the rain band of a park whose own snow band holds no quality (jungle and fantasy,
  snow -1..-1; the rain band holds plain rain and thunderstorms) becomes **snow, without lightning**: `Snow` is set, `Rain` and `Lightning` are not, and the drops follow the rain band's intensity exactly as the storm's did, so the arrival factor (0.8 while drops fall) is the storm's. The converter draws nothing for it; the lightning clock's draws stop with the lightning, so the weather stream moves on
  from the first wintry spell. The Halloween park already snows by its own band (its December rolls are untouched) and space has no rain band. Evidence, tp.exe: the converter `0x512f00` clamps the quality to 1..100 and compares it only with the three bands (`0x785504/08` snow, `0x78550c/10` rain, `0x785514/18` lightning; the lightning flag is stored at `+0x38`);
  it takes no calendar input, so the month is the option's own addition. What it changes downstream is exactly the weather state the shared map gives: umbrellas and the rain mood (kid snow +1 happiness, thirst -1, hunger +1) read snow instead of rain or a storm, and the winter shops see snow. The **forecast follows the option** (`ParkWeather.ForecastBands`): a forecast rolled in December whose quality the converter will turn into snow says snow, not thunderstorms. The option always rides in a save while it is on (it changes
  every park of those themes). It has its own pins (below), including a guest-level one for the umbrellas and the rain mood.
- **Pins.** `DeterminismTests.WinterFixedRunHash` = `0xD4E2340C99394B73` (was `0x8F7023321586F670`, moved by the decision that the kiosk sells all year and by its new season terms; the fixture's calendar is now May for the first 100 seconds and December after it, so the run sees both): the fixed run plus a winter kiosk at (10, 1) and a glühwein stall at (10, 8), snow from the 20th to the 80th second and from the 140th to the 200th
  (53 guests buy an item, 6 are fully dressed, the kiosk sells 61 times and the stall 67). `WinterWeatherTests.JungleSeed21OffHash` = `0x58F5D2429FDCADAA` and `JungleSeed21WintryHash` = `0x2DC4898B7594C4FA`: the jungle's weather turn by turn over its first 400 days under seed 21 with the option off (the original's weather) and on (29 snowy days, the first on day 343, none outside December's rolls).
  The guest-level pin for the umbrellas and the rain mood under real weather: `DeterminismTests.JungleDecemberOffFixedRunHash` = `0xF4A00471E60B9535` and `JungleDecemberWintryFixedRunHash` = `0x296DA4DA6A8BE323`, a brolly shop with the rain mood on, driven by the jungle's real `ParkWeather` (seed 21) from park day 320 over 35 days across the first December, option off (rain, then a thunderstorm in December) and on (rain, then snow). No other pin moved.
- **Tests.** `WinterSimulationTests` (the seasons, the kiosk's one rule, the percentages, the Halloween snow signal, the purchase order, the colours, the score through the host, the purchase without a draw, the snapshot, the validator, the hash, a resume mid-snow), `WinterWiringTests` (the registration, the catalogs of the four themes with the extension on and off, a Theme Park Inc run, the bridges' terms, the drinks counters, the save rule),
  `WinterWeatherTests` (the converter, December and other months, Halloween and space untouched, the Halloween snow signal, the forecast, the 400-day runs, a resume mid-snow), `WinterWiringTests.TheGoldenTicketForEverythingBuiltDoesNotAskForTheUsedOnlyExtensionsObjects` (winter, umbrellas and pools objects never hold back the ticket) and the determinism pins.
- **Not here.** The cold rule and the glühwein's warmth (W5). The kid's `RainMoodDay`, the umbrellas and the rain mood are untouched.

## Drawing (phase W4)

`WarmItemRenderer` (`World/Winter`), created by `Level` beside `UmbrellaRenderer` while the extension is on and its content is there, draws one camera-facing quad of `SPR_WI` for each item a guest wears: a hat at the **head top**, earmuffs at the **head centre**, mittens at the
two **hands**. The item's hotspot is its attach point, so it is put at the anchor of the frame the guest is drawn with (`GuestRenderer.TryMount`: set, slot, direction, step, the guest's own depth pull and mirroring), then pulled `WarmItemRenderer.CameraPull` (1 %) of the way to the camera along
the sight line, which is enough to draw in front of the guest it sits on and far less than the umbrella's 10 %, so a guest's umbrella covers its hat and another guest standing right in front still covers the wearer. Presentation only: no random draw, no pin or save change, nothing
about a guest is read but `WarmItems` (`ShownItems` drops an undefined value) and a guest the guest renderers do not draw (a swimmer, a rider, one in an attraction) shows nothing. `[EXT:winter-draw]`

- **Anchors** (`WinterAnchorTable`, parsed into arrays at load so a frame allocates nothing; `anchors.json` schema 2 for the 2x kids and `anchors-original.json` for the rest). The head follows the walk's bob step by step, a mitten its hand's step and the forearm's angle
  (`ForearmTurn`, the sprite is turned about its hotspot like a lying guest) and is drawn only while its hand is seen (the table's visibility bits, which keep their hand's identity when a direction is mirrored). A slot without a table (running, a ride's pose) follows walking.
- **Original sets** (`content/winter/anchors-original.json`, **numbers only**, user-approved 2026-10-18: committing measured numbers is allowed, never pixels). `opentpw-kit measure-head-anchors --data <Data> --anchors content/winter/anchors.json --pools content/pools/esprites/Generic/Kids --out <file>`
  (a maintainers' tool; a test reproduces the committed table from the local game frames) reads the game's frames as `measure-anchors` does for the umbrellas: each 1x kid set has a 2x redraw of the same pose, so its head top and centre are the rig's at half the pixels, moved sideways
  by the difference of the two silhouettes' mean x and, where the head is the top of the redraw (arms down), up or down by the difference of the top edges (at most 4 pixels). The four costume sets have no redraw: a rule fitted on the kids gives each point's height above the
  silhouette's bottom edge and distance from the box's centre per slot and direction (leaving each kid set out in turn predicts the head top to 2.04 pixels on average, largest 5.59, over 440 frames). The tables cover the standing, walking and waiting slots, the only ones a guest in
  the park is drawn with, for the eight kid sets (1x, 128 pixels per unit) and the four costume sets. **Mittens are not drawn on the original sets** (user-approved: no reliable hand point), so a costumed or original-art guest shows a hat or earmuffs only.
- **Draw order.** Depth decides: the item is pulled 1 %, an umbrella 10 %, so umbrella over hat over guest; the item sprites use `sprite.shader` with the same fog and **evening sprite tint** as the guests (`WithSpriteTint`; the umbrellas' renderer lacked it and now has it too), so a hat dims at night with its wearer; balloons, the
  thought bubble (which hangs above the head) and the pool water are unchanged (swimmers are drawn by the pools' own renderer and carry no item).
- **Browser.** `OpenTPW.Web.csproj` embeds the whole of `content/winter` like the umbrellas' folder: 1,322,442 bytes in 97 files, about 406 KB deflated (zlib -9 per file); every browser user downloads it with the build, also with the extension off. Nothing is fetched later. A real Chrome screenshot of
  the published build comes from `tools/web-perf/run.mjs --scenarios jungle,winter --zoom 70 --shot <file>`: the hook `PerfWinter` stands in for `--debug-winter` (docs/BROWSER-PERFORMANCE.md).
- **Captures** (Debug builds): `--debug-winter [hat|earmuffs|head|mittens|all|costume]` dresses every guest (by default a head item by the guest's id and mittens on all but every fourth guest; `costume` puts every second guest in costume) and writes the guests' item field directly, so nothing may be saved from such a run;
  `--debug-umbrellas-follow zoom,yaw` holds the camera on the first guest; with `--debug-weather 8`, `--debug-sim-minutes 3`, `--capture-world`. `[EXT:debug-winter]`

| Label | What |
| --- | --- |
| `[EXT:winter-draw]` | the drawing: the renderer, the placement from the tables, the 1 % pull, the forearm turn of a mitten, hats and earmuffs on costumed guests; approved by the user on October 19, 2026 ("Alle drie akkoord") |
| `[EXT:debug-winter]` | a capture and test hook, not a user feature (Debug builds only): `--debug-winter` and the browser harness hook `PerfWinter` |

## Approval of the labels

Status of every `[EXT:winter-*]` label against the user's decisions of October 18, 2026 (see "Decisions" above). **Approved** means the label implements something the user decided or asked for, quoted in the last column; `winter-item-colour`, `winter-spice-label`, `winter-season-terms`, `winter-snow-save` and the id range 64010..64499 were approved by the user's answer "Alles akkoord" (October 18, 2026). The one exception is the kiosk's gate, which the user decided differently on 2026-10-18/19.
**Retired** labels are gone from the code and the data.

| Label | What | Status | Basis |
| --- | --- | --- | --- |
| `[EXT:winter-content]` | the extension's own shipped content (`content/winter`) and the pipeline that builds it | approved | the winter kiosks and the glühwein stall the user asked for |
| `[EXT:winter-snow]`, `[EXT:winter-snow-cover]`, `[EXT:winter-snow-surfaces]` | settling snow (W1) | approved | "settling snow ... on (presentation only)" |
| `[EXT:winter-snow-paths]` | paths under snow look like slush | approved | "Paths under snow look like **slush** (greyish and wet)" |
| `[EXT:winter-snow-save]` | the optional, unhashed saved cover | approved | the user answered "Alles akkoord" on October 18, 2026 |
| `[EXT:winter-extension]`, `[EXT:winter-items]`, `[EXT:winter-kiosk]` | the winter kiosk, the warm items (hat, earmuffs, mittens) and the extension that carries them (W3, W4) | approved | "winter kiosks **on** (recorded in a save only for parks that use them)"; the request for a kiosk "that sells hats, mittens and earmuffs" |
| `[EXT:winter-item-colour]` | item colour as a hash of guest id and item | approved | the user answered "Alles akkoord" on October 18, 2026 |
| `[EXT:winter-kiosk-gate]` | the kiosk sold only in snow or December to February | **retired** | replaced by the user's decision of 2026-10-18/19 (asked directly: when does the winter kiosk sell?): "Het hele jaar, vraag via seizoen" (the kiosk sells all year; demand follows the season terms and the snow; a guest with both slots full still scores 0, which is no season gate and is covered by `[EXT:winter-kiosk]`). The earlier "Alles akkoord" no longer covers this label |
| `[EXT:winter-gluehwein]`, `[EXT:winter-gluehwein-no-drunk]` | the glühwein stall, a warm drink with no drunk effects | approved | "Glühwein, not hot chocolate: a warm drink with **no drunk effects**" |
| `[EXT:winter-spice-label]` | the "Spices" slider keeps Ice's mechanics | approved | the user answered "Alles akkoord" on October 18, 2026 |
| `[EXT:winter-season-terms]` | our own `SeasonModifier.*` demand keys, the stall's and the kiosk's demand numbers (the kiosk's: winter 150, spring and autumn 60, summer 20, snow 200) and the shops' own weather signal (snow is snow even with lightning) | approved | the user answered "Alles akkoord" on October 18, 2026 (the prices 25 and 30 are covered by it); the kiosk's season numbers carry out "Het hele jaar, vraag via seizoen" (2026-10-18/19) |
| `[EXT:winter-weather]` | wintry Decembers: December's rain and storms are snow in jungle and fantasy (an option, off by default); implemented as snow without lightning, with the storm's drops | approved | "Wintry Decembers are an option (its own toggle) that turns December's thunderstorms into snow in jungle and fantasy"; that December storms become snow without lightning falls under this approved option. Consequence, stated plainly: the lightning clock stops during such a spell and takes no more draws, so the weather stream diverges from the original from the first wintry spell (the option is recorded in every save while on) |
| `[EXT:winter-save]` | the used-only record of `opentpw.winter` and the optional guest fields `WarmItems` and `WarmUntilDay` | approved | "winter kiosks **on** (recorded in a save only for parks that use them)"; the optional guest fields implement exactly that rule |
| `[EXT:winter-cold]` | the cold mood rule (W5) | approved | "Cold is a separate option, `opentpw.winter-cold`, default off" with the stated -2 / +1 numbers |
| `[EXT:winter-cold-replaces-snow-cheer]` | with the cold rule on and the winter signal saying snow, the rain mood's happiness of that day is left out, whichever row it would use (snow's +1, or Halloween's storm -1 where the snowfall has lightning); its thirst and hunger effects stay as Theme Park Inc's; with cold off nothing changes | approved | "with it on it replaces rain mood's snow +1"; the user's answers on October 19, 2026: "Alleen de humeur-+1" (what it replaces) and, for the Halloween stacking, "Kou vervangt storm-humeur" |
| `[EXT:winter-cold-warm-window]` | a glühwein warms for the rest of its day plus the next game day (`WarmEffectDays` 1) | approved | the user's answer on October 19, 2026: "Rest van de dag + de volgende dag" |
| `[EXT:winter-anchors]` | the schema 2 head and hand anchors, and the measured head-only anchors of the original sets (numbers only; mittens stay invisible there) | approved | the data the approved hats, earmuffs and mittens attach to; "committing measured numbers is allowed, never pixels" and "mittens are invisible on the original sets" (2026-10-18) |
| `[EXT:winter-draw]` | how the items are drawn: the 1 % camera pull, the mitten's forearm turn, items on costumed guests | approved | the user's answer on October 19, 2026 to all three: "Alle drie akkoord" (the 1 % pull so an umbrella falls over the hat; mittens turning with the forearm; hats and earmuffs on costumed guests too) |
| `[EXT:pools-model-inset]` | the walls' gap from the footprint edge (the pools' rule) | existing | not a winter label |
| `[BIN:TPW-EXE:0x...]` | the Theme Park World `tp.exe` address a finding was traced to (this page) | n/a | evidence |

Nothing awaits the user: `winter-snow-save` (W1) and the id range 64010..64499 (the example lamp mod holds 64000..64009) were approved with "Alles akkoord" on October 18, 2026; `winter-draw` and the two cold questions were answered on October 19, 2026.
