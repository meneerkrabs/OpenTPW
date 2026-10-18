# Umbrellas (`opentpw.umbrellas`, planned)

October 18, 2026. Status: **phases U0 (the Theme Park Inc trace, below) and U1 (our own art and data pipeline, [Content](#content-extumbrellas-content)) are done; the extension itself is not wired into the game yet.**
The winter extension ([WINTER.md](WINTER.md)) follows the same pipeline for its kiosk, glühwein stall and warm items.
Nothing of it is visible in a park: `content/umbrellas` is shipped but no catalog, renderer or rule reads it (phases U2 to U5 below).

The idea: bring the brolly shop and guests carrying open umbrellas in the rain of Theme Park Inc to Theme Park World parks as an official extension (docs/MODS.md), with OpenTPW's own art, so a player needs no Theme Park Inc
data. The behaviour comes from a static trace of the Theme Park Inc no-CD `Game.exe` (SHA-256 `77ea0c41...`, `[BIN:TPI-EXE:0x...]` below; nothing was run). Theme Park World's `tp.exe` has none of it: no umbrella strings, no
`WeatherModifier`, `PeepHappinessWeatherEffects` or "Weather is effecting" keys; its only weather rules are the arrival factor 0.8/1.2 and the indoor-ride score x5 (`PeepInfo.DecisionVariable3`). No art, model, script
or number table of Theme Park Inc is copied: the sprites and models are drawn in Blender from code, and the behavioural numbers below are rules, not data files.

## Decisions (October 18, 2026)

- `opentpw.umbrellas` is **on by default** and recorded in a save only for parks that use it (a brolly shop or a guest with an umbrella: the pools rule, [POOLS.md](POOLS.md#the-extension-and-the-save-rule)); other parks save identically on or off. Not offered in a Theme Park Inc run (it has its own shop and art).
- `opentpw.rain-mood` (the weather happiness, hunger and thirst rule) is **off by default** and applies **once per qualifying day**: Theme Park Inc's every-update behaviour comes from a stored day that is never read (`[EXT:rain-mood-once-per-day]`; the literal rule is not offered).
- Umbrellas are **available from the start** (no research item), price 30 as in Theme Park Inc. A closed umbrella is not drawn (no furled sprite), as in Theme Park Inc.

## What Theme Park Inc does (static trace)

Confidence: high for the state machine, the gates, the multipliers, the settings addresses and the data; medium for the per-day count (the clock scale `+0x1C` is not read) and for who counts as "inside" (`+0x38`, `+0x3C`);
low for the view condition and the season index. Not found statically: the producer of the log line "I got a nice umbrella from the ride".

**Settings.** Theme Park Inc's settings are one block of words in key-table order (rows of 60 bytes from `0x996468`; a struct adds no words, an array adds count x fields and then one count word; anchored on `MinPeople`
`0xA02230` and checked on the guest constructor's reads of `PeepTypes`). The umbrella keys are `UmbrellaModifier` `0xA01B5C`, `WeatherModifierFrequency` `0xA01B60`, `SlowestWeatherChangeResponse` `0xA01B64`,
`DiscardBalloonPctModifier` `0xA01B68`, three weather arrays of 3 ages x {Heatwave, Rain, Storm, Snow} floats at `0xA01B88` (happiness), `0xA01BBC` (thirst) and `0xA01BF0` (hunger), `GenericUmbrellaModifiers`
`0xA01E08..0xA01E14` and `PersonSpriteUmbrellaModifiers[12]` from `0xA01E18` (14 floats of 0x38 bytes each: X, Y, Z, Projection, HandPosDistance, HandPosAngle, FPX, FPY, FPZ, FPProjection, FPCentreOffsetX/Z, FPHandPosDistance,
FPHandPosAngle). Object types: `WeatherModifier.Snow/Rain/Storm/HeatWavePercentage` at type `+0xF0..+0xFC`, `GenerationModifier.*ScorePercentage` `+0x110..+0x118`, `UsageInfo.AppearanceEffect` `+0x21C` (1 balloon, 2 costume,
**3 umbrella**). The weather thing's state `+0x70` is 1 heat wave, 2 rain, 3 storm, 4 snow, other none. `[BIN:TPI-EXE:0xa01b5c]`

**Guest fields** (`0x60D260` names them): `mUmbrellaState` `+0x238`, `mUmbrellaType` `+0x244` (the colour), `mUmbrellaScript` `+0x240` (the sprite object). `+0x23C` (the turn a pending change is due) is not saved.
States: 0 none, 1 open and about to close, 2 owned and closed (not drawn), 3 closed and about to open, 4 open. `[BIN:TPI-EXE:0x60d260]`

**Demand** (`0x6101E0`, the attraction score, Theme Park Inc's twin of Theme Park World's `AttractionScore`). An umbrella shop scores 0 for a guest who already has an umbrella (any state) and in any weather but rain, storm or snow.
Every object's score is multiplied by its `WeatherModifier` percentage of the current weather (when nonzero), then by the generation's `GenerationModifier` percentage. A guest holding a balloon scores an umbrella shop lower:
factor = 100 - life x 100 / 255 (balloon life `+0x298`), scaled by `DiscardBalloonPctModifier`/100; a factor of exactly 0 (a full-life balloon) skips the cut, a quirk. A balloon shop scores 0 for a guest who already holds a balloon (`+0x294` nonzero or life `+0x298` above 0) and for a guest with an open or opening umbrella (states 3, 4).
Theme Park Inc's `DecisionVariable3` (indoor x5) is marked unused in its data. `[BIN:TPI-EXE:0x6101e0]`

**Purchase** (`0x612740`, the use outcome, `AppearanceEffect` 3): the colour is one world-generator draw, (draw >> 2) mod 3; the umbrella opens at once (state 4, sprite object created) whatever the weather; the sale's other effects are a
shop's. The balloon is not touched by the purchase itself. `[BIN:TPI-EXE:0x612740]`

**Weather rule** (`0x611050`, called from the guest update `0x619B50` every fourth tick for every guest, wherever it is). It runs only while the guest's age in game days (ticks since its creation `+0x1FC`, through the clock `0x608A90`)
is a multiple of `WeatherModifierFrequency` (4): the whole first game day of a visit, then every fourth day. The stored day `+0x2E8` is written but never read, so the designers' "once every X days" is in fact every update of those days.
In rain, storm or snow the guest's happiness, hunger and thirst get the age row's deltas (clamped 0..100), and an **open** umbrella (state 4) adds `UmbrellaModifier` (1, as an integer) to a negative happiness delta. Shipped kid row: rain -1, storm -1,
snow +1, heat wave +1 happiness; snow thirst -1, hunger +1; heat wave thirst +2, hunger -1. Parents and elderly are worse (elderly rain -2). The same call drives the umbrella: in bad weather a closed umbrella (2) gets an opening
scheduled, a pending close (1) is cancelled (back to 4) and a pending open (3) is carried out when due; in clear weather or a heat wave an open one (4) gets a close scheduled, a pending open (3) is cancelled (2) and a pending close (1)
is carried out. `[BIN:TPI-EXE:0x611050]`, called by `[BIN:TPI-EXE:0x619b50]`

**Scheduling** (`0x613EA0`): only while the guest is not inside an attraction (`+0x38`, `+0x3C` zero) and not under a view condition (`0x67F890`/`0x685680`, meaning not identified). Delay = CRT `rand()` mod
`SlowestWeatherChangeResponse` guest updates, lengthened by the balloon's share of life x `DiscardBalloonPctModifier`/100. `[BIN:TPI-EXE:0x613ea0]`
**Opening** (`0x614010`): a held balloon is let go (float-away effect), happiness drops by `MediumHappinessChange` (15) and the balloon life is cleared; then the sprite is created (state 4). That is the whole
balloon-for-umbrella trade, together with `0x61A540` state 0x14: entering an umbrella shop with a balloon also releases it with the same -15. `[BIN:TPI-EXE:0x614010]`

**Attractions** (`0x61A540`): entering any attraction closes an umbrella (state 2, sprite destroyed); leaving does not reopen it (the weather rule does, on a qualifying day). An umbrella is never lost or discarded: 0 is written only by the
constructor. Holders of an open or pending umbrella get no walking speed term (`0x60B3F0`, table 0/25/50) and play no post-visit cheer. `[BIN:TPI-EXE:0x61a540]`, `[BIN:TPI-EXE:0x60b3f0]`

**Drawing** (`0x60AE00`, per frame). Default camera: the umbrella sprite's hotspot (the handle tip) goes to the guest's position at time t + `Projection`, raised by `Y[type]` (5.5 to 6.5 units: hand height) plus
(camera height - 40) x `YAdjustCamera`, and pulled 10 % (`X/ZAdjustCamera`) of the way toward the camera, so it covers the guest. The guest's overhead bubble is raised from 2.5 to 6.0 units while the umbrella shows. The renderer scales
sprite bank 0x13 by `ScalingFactor` 1.25 (`0x690680`). The `FP*` path (walk-around camera) adds per-direction hand offsets precomputed at guest creation (`0x60C3F0`): for the 8 headings, centre offset plus sin/cos x
`FPHandPosDistance` of heading + `FPHandPosAngle`, but the angle is truncated to whole radians before sin/cos (a Theme Park Inc bug). Type index = generation x 4 + sprite variant. Its `Spr_um.esp` holds 3 slots of 1 frame and
**no directions** (blue, rainbow, red/white; 36 x 34 pixels, hotspot bottom centre, 128 ppu; the walk-around bank 37 x 45). No closed-umbrella frame exists. `[BIN:TPI-EXE:0x60ae00]`, `[BIN:TPI-EXE:0x60c3f0]`

**Data.** Three `brolly.wad` (water 2206, named "Balloon Shop" by mistake, arabian 3202, science 1201): price 30, cost 10 to 15, `HappinessEffect` 20, litter 10 to 20, capacity 5 or 10, `ISIndoors` 1, kid score 75 % (science 50 %),
weather Rain/Storm 200 (science 250), Snow 150, HeatWave 5. Weather spells (`0x642770`) roll freak heat wave, rain, storm or snow from `Seasons[]` only in the third month of a quarter, after `YearsBeforeFirstFreakWeather`.
`[BIN:TPI-EXE:0x642770]`

## Behaviour in Theme Park World parks (design for U2 to U5)

- **Architecture.** `opentpw.umbrellas` (code, `AffectsSimulation: true`): the brolly shop, the umbrella item, the state machine and the drawing. `opentpw.rain-mood` (code, `AffectsSimulation: true`, always recorded while on): the weather
  happiness, hunger and thirst rule, separate because Theme Park World has no such rule and it changes every park with weather. Umbrellas without it are mood items (purchase happiness, the look, the balloon trade).
- **Weather mapping** `[EXT:umbrellas-weather-map]`: `ParkWeather` rain without lightning = rain (2), lightning = storm (3), snow = snow (4), else none. Theme Park World has no heat wave, so the heat wave rows and `HeatWavePercentage` never apply.
- **Generation:** every Theme Park World guest is a kid, so the kid rows apply (rain -1, storm -1, snow +1 happiness, snow thirst -1, hunger +1); kept as data.
- **Faithful:** the shop gate, the weather and generation multipliers, the balloon factor with its zero quirk, purchase opens at once, the five states and their transitions, the day gate (the whole first day, then every fourth), the delay bounds, the
  balloon release with -15, closing on entering an attraction, no reopening on leaving, no speed term, no cheer.
- **Approximated** (to be registered as `APPROX:UMB-nnn` when the code lands): the reaction delay uses a per-guest hash of (guest id, park turn), not CRT `rand()` (no draw from any shared stream; UMB-001); the view condition is dropped (UMB-002);
  "inside" = OpenTPW's in-attraction states (UMB-003); the pending change turn is saved, unlike Theme Park Inc (`[EXT:umbrellas-save-due]`). The colour keeps Theme Park Inc's one world draw at purchase.
- **Not ported:** the first-person `FP*` placement (no walk-around camera yet; the formula and its radian truncation are recorded above), the season roller (Theme Park World keeps its own weather), Theme Park Inc's log lines.
- **Saves** (U2): optional guest fields `UmbrellaState` (0..4), `UmbrellaType` (0..2), `UmbrellaDueTurn` written only when the state is not 0, so every older JSON is byte-equal; the sprite object is rebuilt from the state on load.
- **Determinism:** both extensions off, or umbrellas on without a brolly shop and rain mood off, leave every pin unchanged; with a shop the decisions, sales and the colour draw move the world stream (new pins in U2/U4).

## Phases

| Phase | What | State |
| --- | --- | --- |
| U0 | the trace in this page, `[BIN:TPI-EXE]` labels | done |
| U1 | art and data pipeline: umbrella sprites, anchors from the rig, the four shop models, `.sam`, RSE, `content/umbrellas` | done (original-set anchors left to U3) |
| U2 | simulation behind `opentpw.umbrellas`: purchase, states, scoring gate, balloon trade, attraction close, saves, pins; also a catalog root for the extension in `ModContentRoots.CatalogRoots` and its id in the `ObjectCatalog` cache key (`ObjectCatalog.cs`, where only the pools' root is named today), or a park would reuse a catalog built without the shops | planned |
| U3 | drawing: `UmbrellaRenderer`, anchors for the original sets, pull, bubble lift, browser delivery, captures | planned |
| U4 | `opentpw.rain-mood` with its pins and the measurement that decides its variant | planned |
| U5 | tuning from captures; the first-person placement if a walk-around camera lands | planned |

Open questions: Theme Park Inc's kid score 75 % or our 100 % (decide from captures in U5); the shop's `ISIndoors` 0 where Theme Park Inc's is 1 (it keeps the indoor-in-rain x5 off the weather percentages once U2 scores rain); the user's approval of `[EXT:umbrellas-weather-map]` and `[EXT:umbrellas-save-due]`, which are planned for U2 and not yet in the decisions above; the umbrella gate and weather percentages live in the host score for extension objects only, so the smarter-guests seam (`IGuestDecisions`) needs a word before it is touched.

## Content (`[EXT:umbrellas-content]`)

`content/umbrellas` (copied next to the program with the rest of `content/`; not yet embedded in the browser build, phase U3 decides how the sprites travel: the set is 29 KB) holds, all our own, generated from code and committed (CI has no Blender):

The content is our own (modelled and textured in Blender from the scripts in `tools/`), MIT-licensed like the rest of the repository; nothing in it comes from Theme Park Inc or Theme Park World.

- `objects.json`: four brolly shops, one per Theme Park World theme, ids **64500 to 64503** inside the extension's own range **64500 to 64999** (below the pools' 65000..65535, above the Theme Park Inc rides' 40000..40499), category `shops`, `"research": false`.
  The extension's id is `opentpw.umbrellas`, so a park save would name the files `mod:opentpw.umbrellas:/...` (as [POOLS.md](POOLS.md#content-extpools-content)).
- `objects/<theme>/brolly/`: `brolly.MD2`, eight 64 x 64 TGA textures (nine in space: an extra light strip), `Brolly.sam` and `Brolly.RSE`. The model is a **2 x 2 booth** (Theme Park Inc's is 3 x 3; smaller suits a stall) whose entrance is the lower left cell:
  a back wall, two side walls, a counter with three open umbrellas on it and an umbrella-shaped roof (eight panels in two alternating textures, a finial). Themed textures as the pool cabins: bamboo and thatch (jungle), mossy stone and purple shingles (hallow),
  riveted metal with cyan strips (space), pastel candy (fantasy). The walls, counter and posts keep at least 1 unit (1.6 by construction) from the footprint edge at a walker's height (the pools lesson, `[EXT:pools-model-inset]`); the roof reaches 1.0 from it.
- The **`.sam`** (`tools/umbrellas/brolly.sam`, built from the keys of the original balloon shop): `AppearanceEffect` 3, `ShopType` 1 (a gift shop's kind, OpenTPW's own choice), price 30 and cost of goods 13 (Theme Park Inc: 10 to 15), `HappinessEffect` 20, `LitterEffect` 15,
  capacity 10, `ISIndoors` 0 (so the original's indoor-in-rain x5 does not stack on the weather percentages), `WeatherModifier.Rain/Storm 200`, `Snow 150`, `HeatWave 5`, `GenerationModifier.KidScorePercentage` 100 (Theme Park Inc's 75 cut kids, who are
  all of Theme Park World's guests; open), upgrade cost 1000, research cost 0, `Upgrades[0].RunningCost` 35. `WeatherModifier.*`, `GenerationModifier.*` and `RunningCost` are Theme Park Inc keys that Theme Park World does not read: the extension will read them for its own objects only, so original objects keep their scoring.
- The **`.RSE`** (`tools/umbrellas/brolly.rse`): the shop has no animation, so it is the pool changing room's script with shop timings (customers go in hidden, are served for a second, come out).
- `esprites/Umbrellas/SPR_UM.ESP` + `.TPC`: the **umbrella sprites**, rendered by `tools/blender/opentpw_umbrella.py` with the pools kid camera (orthographic, 45 degrees, the same lights, grade and dark edge) at **2x (256 pixels per unit)**: three colourways as slots 0 to 2
  (plain sky blue, rainbow, coral and cream stripes), **five directions** (0 to 4; the game mirrors them to eight like the kids), one frame each, one 255-colour palette. An eight-panel scalloped canopy 0.8 of a kid's height across (Theme Park Inc's ratio after its 1.25 scale),
  a ferrule, a rim band, a hooked handle, the shaft tilted 14 degrees towards the body so that the canopy centre is over the head of a kid whose hand is at its side. The **hotspot of every frame is the handle tip**, the lowest point of the crook, which sits at the render's world origin,
  the point a kid's feet stand on. No closed-umbrella frame (a closed umbrella is not drawn).
- `anchors.json`: the **hand anchors** of the eight 2x kid sets, from the rig (`tools/blender/opentpw_guest_sprites.py --emit-anchors`): per set and direction 0 to 4, where the kid's right hand lands in the land frames, in pixels (256 per unit) from the feet hotspot, x right and y down, for the
  standing frame, every walking frame and the waiting frames, plus the hand's height above the feet. Exact by construction (the same camera as the frames; nothing is rendered, the table takes seconds). The umbrella is placed by putting its hotspot at the anchor
  (then pulled towards the camera, U3). Directions 5 to 7 are the mirror of 3 to 1: the game flips the frame, so the anchor's x is negated. The hand is a little in front of the body's axis, so the side views are not exact mirrors (within 2 pixels).
  The anchors of the original (1x) kid sets and the four costume sets are **not** here: they need a local measuring script that reads the player's own game frames and writes numbers only; that belongs to phase U3, together with the table of those numbers.
- Rebuild: `tools/umbrellas/build-content.sh` (Blender 5.x, the .NET SDK and python3; about 20 seconds): the shop models (`opentpw_brolly_shop.py`, which reuses the pool cabin's textures and geometry builder), the kit's `object` command, the umbrella sprites,
  `tools/umbrellas/pack_sprites.py` (which reuses the pools' set converter, `detailFlag` 0) and the anchors.

`UmbrellasContentTests` load the committed files through the game's own pipelines (the object catalog with a content root made by the test, the sprite readers, the ride script VM) and check the ids and names, the `.sam` keys, the models, textures, footprint and wall gaps,
the script, the sprite banks (slots, directions, 256 ppu, hotspots at the tip) and the anchor table (every set, slot and direction, mirrored symmetry, hands inside the shipped kid frames), and that nothing is wired into the game.

| Label | What |
| --- | --- |
| `[EXT:umbrellas-content]` | the extension's own shipped content (`content/umbrellas`) and the pipeline that builds it |
| `[EXT:umbrellas-weather-map]` | the mapping of `ParkWeather` to Theme Park Inc's weather states (planned, U2; needs the user's approval before U2) |
| `[EXT:umbrellas-save-due]` | the saved pending-change turn (planned, U2; needs the user's approval before U2) |
| `[EXT:rain-mood-once-per-day]` | the weather mood rule applied once per qualifying day (planned, U4) |
| `[BIN:TPI-EXE:0x...]` | the Theme Park Inc `Game.exe` address a rule was traced to (this page) |
