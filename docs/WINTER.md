# Winter (`opentpw.winter`, planned)

October 18, 2026. Status: **phase W0 (the research below and the decisions) and phase W2 (our own art and data pipeline, [Content](#content-extwinter-content)) are done; the extensions themselves are not written yet.**
Nothing of it is visible in a park: `content/winter` is shipped but no catalog, renderer or rule reads it (phases W1, W3, W4 and W5 below).

The request (user, 2026-10-18): three features of our own (`[EXT:winter-*]`), with no original art, script or number table copied: (1) snow that settles on the ground, roofs and objects during snowfall and
melts afterwards; (2) a winter kiosk that sells hats, mittens and earmuffs, worn visibly by the guests; (3) a glühwein stall (the user's choice over hot chocolate; a warm drink with no drunk effects).
Labels: `[EXT:winter-*]` for our additions, `[BIN:TPW-EXE:0x...]` / `[BIN:TPI-EXE]` / `[DATA:...]` for evidence. The shop and sprite pipeline is the umbrellas' one ([UMBRELLAS.md](UMBRELLAS.md), [CONTENT-KIT.md](CONTENT-KIT.md)).

## Decisions (user, October 18, 2026)

- Glühwein, not hot chocolate: a warm drink with **no drunk effects** (Theme Park World has no intoxication state and none is added).
- **Wintry Decembers** are an option (its own toggle) that turns December's thunderstorms into snow in jungle and fantasy. It changes the weather that umbrellas and the rain mood read, so it gets its own pin.
- **Cold** is a separate option, `opentpw.winter-cold`, default off: in snow -2 happiness per qualifying day, +1 back each for a head item, mittens and a recent glühwein, capped at 0; with it on it replaces the rain mood's snow +1.
- Defaults: settling snow **on** (presentation only), winter kiosks **on** (recorded in a save only for parks that use them), cold **off**. Paths under snow look like **slush** (greyish and wet), so they stay readable.
- Coordinator defaults for the remaining open questions (the user can still change them): kiosk **25** and glühwein **30**, both available from the start (no research item); **one kiosk** that gives a head item first, then mittens;
  the snow cover is saved as an optional unhashed field; the "Spices" slider keeps Ice's mechanics so sales count as drinks; mittens stay invisible on the original (1x) guest sprite sets; the kiosk sells only in snow or in December to February.

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
  full and 0 unless it snows or the month is December to February `[EXT:winter-kiosk-gate]`. Not drawn on costumed guests (their sets have no anchors).
- **Glühwein stall** `[EXT:winter-gluehwein]`. A drinks shop as the originals are (`ShopType 4`, `SpecialIngredient 3`, so the drinks code path, challenge and price statistics apply); the slider is labelled "Spices" for this object only
  `[EXT:winter-spice-label]`; `VomitEffect` 5, no new guest state, speech or animation `[EXT:winter-gluehwein-no-drunk]`. Demand: no gate; snow 250 %, rain and storm 130 %, winter months 150 %, spring and autumn 60 %, summer 20 %. With
  `opentpw.winter-cold` on, a glühwein warms the guest for one game day.
- **Cold mood** (`opentpw.winter-cold`, off by default). Once per qualifying day per guest, in snow only: happiness += -2, +1 for a head item, +1 for mittens, +1 while warmed, capped at 0. With `opentpw.rain-mood` also on the two add up,
  so an undressed kid nets -1 a qualifying day and a dressed one +1; this deviates from Theme Park Inc's +1 alone and is an open question.
- **Wintry Decembers** `[EXT:winter-weather]` (an option): December's rolls in jungle and fantasy give snow instead of thunderstorms; its own pin.
- **Saves and determinism.** Snow cover: no hash part, no draw from any stream, no mod-set record. Winter: optional guest fields `WarmItems` and `WarmUntilDay` written and hashed only when set, so older JSON and every park without the extension
  stay byte-equal; off, or on without shops and items, every pin is unchanged. New pins arrive with W3 and W5.
- **Demand keys** (read for the extension's own objects only; unknown to tp.exe, harmless in the data): Theme Park Inc's `WeatherModifier.Snow/Rain/StormPercentage`, our own `SeasonModifier.Winter/Spring/Summer/AutumnPercentage`
  `[EXT:winter-season-terms]` and `WinterInfo.WarmEffectDays` / `WinterInfo.IngredientLabel`.

## Phases

| Phase | What | State |
| --- | --- | --- |
| W0 | this page: the findings and decisions | done |
| W1 | `opentpw.winter-snow`: `SnowCover`, the uniform slot, test.shader and its web JSON, per-entity surface factor, path slush, the optional save field, tests, hallow captures (after `opentpw.evening` merges) | planned |
| W2 | art and data pipeline: the eight shop models, the two `.sam`, the shop scripts, the item sprites, the anchors, `content/winter`, `tools/winter/build-content.sh`, content tests; nothing wired | done |
| W3 | simulation behind `opentpw.winter`: catalog root, `AppearanceEffect` 4, the kiosk gate and terms, the stall's terms, saves, the used-only rule, pins | planned |
| W4 | drawing: the warm-item renderer (sharing the umbrellas' attachment code), original-set head anchors, costume exclusion, browser delivery, captures | planned |
| W5 | `opentpw.winter-cold` with its measurement and pins | planned |
| W6 | tuning from captures; optional hats on riders' heads, pool-deck snow, sheltered-ground masking | planned |

## Content (`[EXT:winter-content]`)

`content/winter` (copied next to the program with the rest of `content/`; not yet embedded in the browser build) holds, all our own, generated from code and committed (CI has no Blender). The content is MIT-licensed like the rest of the
repository; nothing in it comes from Theme Park World or Theme Park Inc, and no original bytes or derived pixels are read at any step.

- `objects.json`: eight shops, one **winter kiosk** and one **glühwein stall** per Theme Park World theme, ids **64010 to 64017** inside the extension's own range **64010 to 64499** (jungle kiosk 64010, jungle stall 64011, hallow 64012 and
  64013, space 64014 and 64015, fantasy 64016 and 64017), category `shops`, `"research": false`. The range sits below the umbrellas' 64500..64999 and the pools' 65000..65535 and above the Theme Park Inc rides' 40000..40499. The
  first ten ids of the 64000 block, 64000 to 64009, belong to the example lamp mod (`content/mods/opentpw.example.lamp-lights`), which is why the winter range starts at 64010. The extension's id is `opentpw.winter`, so a park save will name
  the files `mod:opentpw.winter:/...` (as [POOLS.md](POOLS.md#content-extpools-content)). Names in nine languages.
- `objects/<theme>/<shop>/` (`kiosk` and `gluehwein`): the MD2 model, 8 or 9 64 x 64 TGA textures (`wk_` and `wg_` prefixes), `Kiosk.sam` / `Gluehwein.sam` and the shop script. Both are **2 x 2 market huts** whose entrance is the lower left cell:
  a back wall, two side walls whose tops follow the roof, a counter, two front posts and a **pitched roof** (a closed prism with the ridge over X = 10, so the front is an open gable and the counter shows; the slopes face up, so the snow cover
  lands on them with no extra work). The kiosk has a hat rack on the back wall with bobble hats on pegs, a mitten garland under the roof's front and folded scarves on the counter; the stall has a copper pot on a stove, mugs, a lit sign and a chimney.
  Themed textures as the pool cabins and the brolly shop: bamboo and thatch (jungle), dark mossy timber with purple shingles and a pumpkin on the ridge (hallow), riveted metal with cyan strips (space), pastel candy (fantasy).
  The walls, counter, posts and every hanging prop keep at least 1 unit from the footprint edge at a walker's height (the pools lesson, `[EXT:pools-model-inset]`); the roof reaches 1.0 from it. Backface culling is on for every material.
- The **`.sam`** (`tools/winter/kiosk.sam`, built from the keys of the original balloon shop; `tools/winter/gluehwein.sam`, from the original drinks shops): the kiosk has `AppearanceEffect` 4, `ShopType` 1 (a gift shop's kind; not 5, which
  would count as costumes), price 25, cost of goods 10, `HappinessEffect` 15, `LitterEffect` 5, thirst, hunger and vomit 0, `ISIndoors` 0, capacity 5, upgrade 1000, `RunningCost` 30, demand `WeatherModifier.SnowPercentage` 200 and
  `SeasonModifier.WinterPercentage` 100 with the other seasons 0. The stall has `ShopType` 4 and `SpecialIngredient` 3, price 30, cost of goods 15, `ThirstEffect` 30, `HungerEffect` 5, **`VomitEffect` 5**, `HappinessEffect` 15, `LitterEffect` 40,
  capacity 1, upgrade 750, `WinterInfo.IngredientLabel` "Spices", `WinterInfo.WarmEffectDays` 1, and the demand percentages of the design above. The `WeatherModifier.*`, `SeasonModifier.*`, `WinterInfo.*` and `RunningCost` keys are
  unknown to tp.exe; the winter extension will read them for its own objects only (W3).
- The **`.RSE`** (`tools/winter/kiosk.rse`, `gluehwein.rse`): the brolly shop's script (customers go in hidden, are served, come out) with the stall's longer serving time. No steam particle: none of the existing particle keys fits a pot.
- `esprites/Winter/SPR_WI.ESP` + `.TPC` (28 KB in all): the **warm item sprites**, rendered by `tools/blender/opentpw_winter_items.py` with the pools kid camera (orthographic, 45 degrees, the same lights, grade and dark edge) at **2x
  (256 pixels per unit)**, five directions (the game mirrors them to eight), one 255-colour palette. Slots 0 to 2 bobble hats (red, blue, striped green and cream), 3 and 4 earmuffs (grey band with red cups, navy band with pale blue cups), 5 to 7
  mittens (red, blue, green; frame 0 the right mitten, frame 1 the left). Hats and earmuffs are rendered over a **holdout head proxy** (the rig's skull ellipsoid), so the part behind the head is already cut out and the sprite can simply be drawn in
  front of the guest. The **hotspot of every frame is the item's attach point**: the top of the skull for a hat, the head centre for earmuffs, the hand end point for a mitten.
- `anchors.json`, **schema 2**: for each of the eight 2x kid sets and **every land slot, frame and direction 0 to 4** (35 x 5 records) the head-top point, the head centre, both hand end points with the forearm's screen angle, and a visibility flag
  per hand (a ray from the hand towards the camera against the body), plus the head's yaw and pitch per frame; pixels at 256 per unit from the feet hotspot. Written by `opentpw_guest_sprites.py --emit-winter-anchors` into its own file: the
  umbrellas' schema 1 `anchors.json` and the `--emit-anchors` code path that writes it are unchanged and byte-identical (a test also checks that the winter table's right hand agrees with it). Directions 5 to 7 mirror 3 to 1: negate every x
  and angle, keep the hand's identity. Exact by construction (the same camera as the frames), rounded to 0.1 pixel; 108 KB. The anchors of the original (1x) kid sets and of the costume sets are not here (W4, a local measuring script that
  writes numbers only; mittens stay invisible on those).
- Rebuild: `tools/winter/build-content.sh` (Blender 5.x, the .NET SDK and python3; about a minute): the shop models (`opentpw_winter_shops.py`, which reuses the pool cabin's textures and geometry builder), the kit's `object` command, the item sprites,
  `tools/winter/pack_sprites.py` (the pools' set converter, `detailFlag` 0) and the anchors.

`WinterContentTests` load the committed files through the game's own pipelines (the object catalog with a content root made by the test, the sprite readers, the ride script VM) and check the ids and that no other content range overlaps, the `.sam`
keys, the models, textures, footprint and wall gaps, the scripts, the sprite banks (slots, directions, frames, 256 ppu, hotspots inside the frames), the anchor table (every set, slot, frame and direction, agreement with the umbrellas' table, hands and
head inside the shipped kid frames) and that nothing is wired into the game.

## Approval of the labels

Status of every `[EXT:winter-*]` label against the user's decisions of October 18, 2026 (see "Decisions" above). **Approved** means the label implements something the user decided or asked for, quoted in the last column.
**Awaiting** labels are the coordinator's defaults or details of ours that the user has not been asked about yet; they stay open until the user confirms or changes them (they do not block W3).

| Label | What | Status | Basis |
| --- | --- | --- | --- |
| `[EXT:winter-content]` | the extension's own shipped content (`content/winter`) and the pipeline that builds it | approved | the winter kiosks and the glühwein stall the user asked for |
| `[EXT:winter-snow]`, `[EXT:winter-snow-cover]`, `[EXT:winter-snow-surfaces]` | settling snow (W1) | approved | "settling snow ... on (presentation only)" |
| `[EXT:winter-snow-paths]` | paths under snow look like slush | approved | "Paths under snow look like **slush** (greyish and wet)" |
| `[EXT:winter-snow-save]` | the optional, unhashed saved cover | awaiting | coordinator default ("the snow cover is saved as an optional unhashed field") |
| `[EXT:winter-extension]`, `[EXT:winter-items]`, `[EXT:winter-kiosk]` | the winter kiosk, the warm items (hat, earmuffs, mittens) and the extension that carries them (W3, W4) | approved | "winter kiosks **on** (recorded in a save only for parks that use them)"; the request for a kiosk "that sells hats, mittens and earmuffs" |
| `[EXT:winter-item-colour]` | item colour as a hash of guest id and item | awaiting | our detail |
| `[EXT:winter-kiosk-gate]` | the kiosk sells only in snow or December to February | awaiting | coordinator default |
| `[EXT:winter-gluehwein]`, `[EXT:winter-gluehwein-no-drunk]` | the glühwein stall, a warm drink with no drunk effects | approved | "Glühwein, not hot chocolate: a warm drink with **no drunk effects**" |
| `[EXT:winter-spice-label]` | the "Spices" slider keeps Ice's mechanics | awaiting | coordinator default |
| `[EXT:winter-season-terms]` | our own `SeasonModifier.*` demand keys and the stall's demand numbers | awaiting | our detail (prices 25 and 30 are coordinator defaults too) |
| `[EXT:winter-cold]` | the cold mood rule (W5) | approved | "Cold is a separate option, `opentpw.winter-cold`, default off" with the stated -2 / +1 numbers |
| `[EXT:winter-weather]` | the wintry Decembers option | approved | "**Wintry Decembers** are an option (its own toggle)" |
| `[EXT:winter-anchors]` | the schema 2 head and hand anchors | approved | the data the approved hats, earmuffs and mittens attach to |
| `[EXT:pools-model-inset]` | the walls' gap from the footprint edge (the pools' rule) | existing | not a winter label |
| `[BIN:TPW-EXE:0x...]` | the Theme Park World `tp.exe` address a finding was traced to (this page) | n/a | evidence |

Still awaiting the user: the three coordinator defaults above that touch behaviour (`winter-snow-save`, `winter-kiosk-gate`, `winter-spice-label`), the two details of ours (`winter-item-colour`, `winter-season-terms`) and the id range 64010..64499 (the example lamp mod holds 64000..64009).
