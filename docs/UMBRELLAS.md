# Umbrellas (`opentpw.umbrellas`)

October 18, 2026. Status: **phases U0 (the Theme Park Inc trace, below), U1 (our own art and data pipeline, [Content](#content-extumbrellas-content)), U2 (the simulation, [below](#the-simulation-phase-u2)), U3 (the drawing, [below](#drawing-phase-u3)), U4 (the rain mood rule, [below](#the-rain-mood-rule-phase-u4)) and U5 (the tuning, [below](#tuning-phase-u5)) are done.**
The extension is registered, on by default, lists the four brolly shops in the Theme Park World catalogs and runs their rules: a guest buys an umbrella in the rain and carries it through the five states. Guests carrying an open umbrella are drawn (phase U3);
the rain mood rule (`opentpw.rain-mood`, off by default) is its own extension.
The winter extension ([WINTER.md](WINTER.md)) follows the same pipeline for its kiosk, glühwein stall and warm items.

The idea: bring the brolly shop and guests carrying open umbrellas in the rain of Theme Park Inc to Theme Park World parks as an official extension (docs/MODS.md), with OpenTPW's own art, so a player needs no Theme Park Inc
data. The behaviour comes from a static trace of the Theme Park Inc no-CD `Game.exe` (SHA-256 `77ea0c41...`, `[BIN:TPI-EXE:0x...]` below; nothing was run). Theme Park World's `tp.exe` has none of it: no umbrella strings, no
`WeatherModifier`, `PeepHappinessWeatherEffects` or "Weather is effecting" keys; its only weather rules are the arrival factor 0.8/1.2 and the indoor-ride score x5 (`PeepInfo.DecisionVariable3`). No art, model, script
or number table of Theme Park Inc is copied: the sprites and models are drawn in Blender from code, and the behavioural numbers below are rules, not data files.

## Decisions (October 18, 2026)

- **User decisions of phase U5 (2026-10-18, asked directly in the coordinating session):** the owner chose Theme Park Inc's kid score of 75 % over OpenTPW's 100 % (answer: "75%, zoals TPI"). Shipped `GenerationModifier.KidScorePercentage` is 75 in the four `Brolly.sam` files and `tools/umbrellas/brolly.sam`. `[EXT:umbrellas-size]` (canopy 0.85) and `[EXT:umbrellas-pull-cap]` (fixed 1.5 unit pull) are both approved (answer: "Beide akkoord").
- `opentpw.umbrellas` is **on by default** and recorded in a save only for parks that use it (a brolly shop or a guest with an umbrella: the pools rule, [POOLS.md](POOLS.md#the-extension-and-the-save-rule)); other parks save identically on or off. Not offered in a Theme Park Inc run (it has its own shop and art).
- **User approval of the rain mood extension and its imported-age rule (2026-10-18, asked directly):** the user approved `opentpw.rain-mood` as an extension, default off (`[EXT:rain-mood-extension]`), and, asked what an imported original guest's age should be for the rule's day gate, chose "Tellen vanaf het laden": the age counts from the load (the import turn), so the rule then applies normally every fourth day instead of age 0 forever (`[EXT:rain-mood-imported-age]`). The once-per-day rule was approved earlier the same day (`[EXT:rain-mood-once-per-day]`).
- `opentpw.rain-mood` (the weather happiness, hunger and thirst rule) is **off by default** and applies **once per qualifying day**: Theme Park Inc's every-update behaviour comes from a stored day that is never read (`[EXT:rain-mood-once-per-day]`; the literal rule is not offered).
- **User approval of the three U2 extensions (2026-10-18, asked directly in the coordinating session):** the question was "U2 heeft drie eigen keuzes gemaakt bij de paraplu's. Akkoord als uitbreiding (EXT)?" and the answer was "Alle drie akkoord", the options naming 1) the weather mapping (`[EXT:umbrellas-weather-map]`), 2) the saved pending open/close turn (`[EXT:umbrellas-save-due]`), 3) the balloon shop refusing balloon holders only in parks with umbrellas (`[EXT:umbrellas-balloon-gate]`).
- **User approval of the three U3 choices (2026-10-18, asked directly in the coordinating session):** (1) the sight-line pull instead of TPI's ground pull and `YAdjustCamera` lift is registered as an approximation, `APPROX:UMB-004`; the answer was "Als benadering (APPROX:UMB-004)". (2) `[EXT:umbrellas-draw]`, including the steady hand (the umbrella stays at the mean hand position over the walk steps), is approved; the answer was "Akkoord". (3) Committing `content/umbrellas/anchors-original.json` is approved: numbers only, measured from the user's own game frames, no pixels or bytes; the answer was "Ja, getallen mogen".
- **Two more `[EXT:umbrellas-draw]` choices are ours, covered by the plan the user approved on 2026-10-18 (answer "doe het allemaal" after a plan summary that named "5 richtingen gespiegeld naar 8" and "handposities uit het skelet"):** (1) TPI's umbrella has no directions (`0x614320` passes direction 0 to `0x4E3D10`), so OpenTPW's facing, the five stored directions mirrored to eight by the guests' own sectors, is our own. (2) TPI's anchor is straight above the guest at the set's Y (`0x60B29B`), so OpenTPW's hand anchors from the guest rig are our own.
- `[EXT:debug-umbrellas]` (the `--debug-umbrellas` flags, including `--debug-umbrellas-crowd N`, and the `PerfUmbrellas` hook) is a **Debug-only test hook**: it exists for captures and smokes in Debug builds only, and a player never turns it on.
- Umbrellas are **available from the start** (no research item), price 30 as in Theme Park Inc. A closed umbrella is not drawn (no furled sprite), as in Theme Park Inc.

## What Theme Park Inc does (static trace)

Confidence: high for the state machine, the gates, the multipliers, the settings addresses and the data; medium for the per-day count (the clock scale `+0x1C` is not read) and for who counts as "inside" (`+0x38`, `+0x3C`);
high for the track-cell test (UMB-002), low for the season index. Not found statically: the producer of the log line "I got a nice umbrella from the ride".

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

**Scheduling** (`0x613EA0`): only while the guest is not inside an attraction (`+0x38`, `+0x3C` zero) and not standing on a kart or water track cell (the two tests `0x67F890`/`0x685680` of UMB-002 below, applied to the guest's cell; not modelled). Delay = CRT `rand()` mod
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
- **Approximated** (registered as `APPROX:UMB-001` to `UMB-004`, [below](#the-simulation-phase-u2)): the reaction delay uses a per-guest hash of (guest id, park turn), not CRT `rand()` (no draw from any shared stream; UMB-001); the track-cell test is left out (UMB-002);
  "inside" = OpenTPW's in-attraction states (UMB-003); the pending change turn is saved, unlike Theme Park Inc (`[EXT:umbrellas-save-due]`). The colour keeps Theme Park Inc's one world draw at purchase.
- **Not ported:** the first-person `FP*` placement (no walk-around camera yet; the formula and its radian truncation are recorded above), the season roller (Theme Park World keeps its own weather), Theme Park Inc's log lines.
- **Saves** (done in U2): optional guest fields `UmbrellaState` (0..4), `UmbrellaType` (0..2), `UmbrellaDueTurn` written only when the state is not 0, so every older JSON is byte-equal; the sprite object is rebuilt from the state on load.
- **Determinism:** both extensions off, or umbrellas on without a brolly shop and rain mood off, leave every pin unchanged; with a shop the decisions, sales and the colour draw move the world stream (new pins in U2/U4).

## The simulation (phase U2)

`opentpw.umbrellas` 1.0.0 (`OfficialExtensions.Umbrellas`, `AffectsSimulation: true`, on by default, `PlayerSelectable`; the version is the rule identity and every behaviour change bumps it) with the gate `FeatureGates.Umbrellas`
(also off in a Theme Park Inc run). The code is `World/Umbrellas` (`UmbrellaRules`, the pure rules; `UmbrellaContent`, the catalog root; `UmbrellaApproximations`, the register) and `GuestSimulation.Umbrellas.cs` (the host side). `[EXT:umbrellas-extension]`

- **The catalog.** `UmbrellaContent.CatalogRoot` is a root of the id `opentpw.umbrellas` in `ModContentRoots.CatalogRoots` after the pools' root, so the four brolly shops (ids 64500 to 64503, shops, no research) are listed in the
  theme catalogs of a Theme Park World run while the extension is on; `ObjectCatalog`'s cache key names the root, so a park never reuses a catalog built without the shops. In a Theme Park Inc run, with the extension off or without the
  folder there is no root. The browser build embeds the folder (U3), so the shops are there too. `[EXT:umbrellas-content]`
- **The score** (`GuestSimulation.HostScore`, applied after the decision policy's score at every call of `ScoreAttraction`: the major decision, the minor decision and its hysteresis; `IGuestDecisions` and the smarter guests are untouched).
  For an object of the extension: a brolly shop scores 0 for a guest with an umbrella of any state and in any weather but rain, storm or snow (`[BIN:TPI-EXE:0x61038e]`); then the score is multiplied by the object's
  `WeatherModifier.*Percentage` of the current weather when that is nonzero, then (brolly shop) by the balloon factor `100 - life x 100 / 255` scaled by `DiscardBalloonPctModifier` (100), skipped when it is exactly 0 (a full-life balloon: the original's
  quirk, kept) or the life is 0, then by `GenerationModifier.KidScorePercentage` (`[BIN:TPI-EXE:0x6101e0]`, `0x6109d0`, `0x610af1`, `0x610b59`; signed integer division by 100 as the original; the key defaults to 100 when a .sam leaves it out). Every other
  object keeps the game's score: the weather and generation keys are read for the extension's own objects only. A balloon shop scores 0 for a guest with a balloon (the model, or a life above 0) and for a guest with an open or opening umbrella (states 3 and 4,
  `[BIN:TPI-EXE:0x6103ec]`). tp.exe's own score reads no appearance word, so the balloon half applies only in a park that uses umbrellas (a brolly shop registered, or any guest in the park with an umbrella, counted per park by the simulation, not per scored guest): `[EXT:umbrellas-balloon-gate]`, so every other park keeps its balloon shops as they were.
- **The weather** is mapped from `ParkWeather`'s flags (lightning is a storm, snow is snow, rain without lightning is rain, else none; no heat wave): `UmbrellaRules.WeatherStateOf`, wired by `Level` as `GuestSimulation.UmbrellaWeatherState`. `[EXT:umbrellas-weather-map]`
- **The purchase** (the shop's `UsageInfo.AppearanceEffect` 3, `[BIN:TPI-EXE:0x612ea9]`): the umbrella opens at once (state 3 is written first and becomes 4 when no sprite exists; entering the shop has closed any drawn umbrella, so a purchase always ends in 4), whatever the weather, in a colour `(draw >> 2) mod 3` from **one** draw of the world generator (`0x690370`); the shop's other effects are a shop's.
  A guest's creation reseeds the generator with its id (tp.exe `0x516370`), so the colour is a function of the guest's id, as the balloon's is. The draw is taken only at a brolly shop, so a park without one takes none.
- **The states** 0 none, 1 open and about to close, 2 closed, 3 closed and about to open, 4 open, with the weather rule on the guest's own update (every fourth park turn, `turn & 3 == id & 3`, before its needs; `[BIN:TPI-EXE:0x611050]`, called by `0x619B50`),
  only on a qualifying day: the guest's age in game days (3750 game seconds a park turn, the turns cut to a multiple of four) is a multiple of `WeatherModifierFrequency` (4), so the whole first day of a visit and then one day in four. Bad weather: a closed
  umbrella gets an opening scheduled (3), a pending close is cancelled (4), a pending open is carried out when due. Other weather: an open one gets a close scheduled (1), a pending open is cancelled (2), a pending close is carried out when due. The delay is below `SlowestWeatherChangeResponse`
  (3) guest updates, lengthened by the held balloon's share of life x `DiscardBalloonPctModifier` / 100 (`0x613ea0`); a carried-out open lets a held balloon go: the balloon is lost, the guest loses `MediumHappinessChange` (15) and the life is cleared (`0x614010`, `0x613d00`).
  Entering a brolly shop with a balloon (or a life) does the same release (`0x61a7b5`); entering any attraction closes an open umbrella (4 and 1 to 2; `0x6142e0`: only a state that has a sprite); leaving does not reopen it, the weather rule does. While an umbrella is open or pending (states 1, 3 and 4) the guest's walking speed term is 0 wherever OpenTPW writes one (a toilet need on its needs phase, a relief visit; `UmbrellaRules.SpeedTerm`, `[BIN:TPI-EXE:0x60b3f0]`: the setter zeroes the term index of a guest with those states before it reads its 0/25/50 table);
  the closed umbrella gives it back on the guest's next needs phase. The original's post-visit cheer (the reaction around `0x613594` and `0x613657`) keeps its animation but loses its voice while an umbrella is open or pending; OpenTPW's cheer (the state-6 activity) plays no voice, so there is nothing to
  omit. Nothing here touches a guest without an umbrella and nothing writes state 0 again. The umbrella's mood protection (`UmbrellaModifier`) belongs to `opentpw.rain-mood` (U4).
- **Saves.** The guest snapshot gains optional `UmbrellaState`, `UmbrellaType` and `UmbrellaDueTurn`, omitted while zero and so written only for a guest with an umbrella (a park without one has the same bytes), validated (state 0 to 4, colour 0 to 2 and 0 without an umbrella, a due turn a non-negative multiple of four
  only in states 1 and 3), restored, and hashed (both digests) only while a guest has an umbrella. The due turn is OpenTPW's addition: Theme Park Inc does not save it (`+0x23C`), so a resumed park would not continue the same run. `[EXT:umbrellas-save-due]`
  A park save records `opentpw.umbrellas` only while the park has a brolly shop or a guest with an umbrella, with the asset keys of the placed shops only (`ParkResumeMods.UsedOnlyExtensionIds`, `InUse`, `AssetsForUse`; the pools' rule); a save with an umbrella and no record is refused by
  the envelope's shape check, and a recorded extension that is off or in another version refuses the load. `[EXT:umbrellas-save]`
  The kid score's change to 75 (October 18) changed `Brolly.sam`, whose hash a save with a placed brolly shop pins: such a save from v0.8.26 or v0.8.27
  no longer resumes ("asset … hashes to …"); parks without a brolly shop are unaffected. The extension stays 1.0.0, because a bump would also refuse the saves that
  record it only through a guest's umbrella.
- **Determinism.** The fixed run on the cross park is unchanged with the extension on or off and no brolly shop (`DeterminismTests.EveryExistingPinIsUnchangedWithTheUmbrellasExtensionOnOrOffAndNoBrollyShop`, and every other pin, which now runs with the extension at its default). The new pin
  `DeterminismTests.UmbrellasFixedRunHash` covers the fixed run plus a brolly shop at (10, 1) and a rain that comes and goes (see [DETERMINISM.md](DETERMINISM.md#umbrellas)).
- **Approximated, registered** (`UmbrellaApproximations`): `UMB-001` the reaction delay is a SplitMix64 hash of (guest id, park turn) with the original's bound, not the C runtime's `rand()`; `UMB-002` the track-cell test of the scheduling and of the carrying out (a guest on a kart or water track cell stands down; none of OpenTPW's guests stands on one) is left out;
  `UMB-003` "inside" is OpenTPW's in-attraction states (using a ride or shop, or a changing room's swim session), and the day gate's age is the guest's time since creation (`TimeInParkSeconds`, GUEST-920), whose game-day conversion is the calendar's 3750 seconds a turn.
  `UMB-004` the umbrella is pulled towards the camera along the sight line, not by TPI's ground pull and `YAdjustCamera` height lift (the value is unknown, so is the original's screen-space placement).
  None of the first three can be retired: the first needs the C runtime's hidden state and by design takes no draw, the other two are state OpenTPW's guests do not have.

## The rain mood rule (phase U4)

`opentpw.rain-mood` (1.0.0, off by default, `AffectsSimulation`, not in a Theme Park Inc run) is the part of Theme Park Inc's weather rule (`0x611050`) that Theme Park World does not have: in rain, storm and snow a guest's happiness, thirst and hunger
change by the guest generation's row, and an umbrella that is **already open** (state 4) when the rule starts adds `UmbrellaModifier` (1) to a negative happiness delta; an umbrella the same step opens (state 1 to 4, or a due 3) does not (the switch at `0x611334` reads the state once: case 4 is the modifier at `0x6111BE` to `0x6111D3`, case 1 sets 4 at `0x6111E3`, case 3 calls `0x402d8d`), so `UpdateNeedsTurn` records `umbrellaWasOpen` before `UpdateUmbrella` and passes it in. It lives in `RainMoodRules` (pure) and `GuestSimulation.RainMood` (the host step); the level sets that from
`FeatureGates.RainMood`, and the weather comes from `UmbrellaWeatherState` (the same mapping as the umbrellas).

- **Where.** In `UpdateNeedsTurn`, on the guest's phase (turn & 3 == id & 3), after the umbrella step and before `NeedsUpdate.Apply` (the step reads the umbrella state before the umbrella step runs, see the modifier below); for **every** guest in every state (queued, riding, swimming), as the original runs it for every guest. The umbrella
  transitions of the same original rule stay in `UpdateUmbrella` (the umbrellas extension) and run with the rain mood off.
- **The numbers.** Theme Park Inc's shipped kid row is the default (all Theme Park World guests are kids): happiness rain -1, storm -1, snow +1 (heat wave +1, never used here); thirst snow -1; hunger snow +1; rain and storm change neither. The rows are `RainMoodSettings.Kid`
  (the settings of the extension), the meters are single floats clamped to 0..100 (`MeterMath.Add`), the order is the original's: the umbrella modifier first, then the clamped sums. Checked again against the no-CD `Game.exe`: the row addresses `0xA01B88` (happiness), `0xA01BBC`
  (thirst) and `0xA01BF0` (hunger) at 16 bytes per age, the test "delta below zero" before the `fild` of `0xA01B5C`, and the three clamped stores to `+0x1BC`, `+0x1CC` and `+0x1C8`.
- **Once per qualifying day.** `[EXT:rain-mood-once-per-day]` The original stores the guest's day (`+0x2E8`) at every qualifying update and never reads it, so its rule hits on every update of a qualifying day (the whole first day, then every fourth day). The extension applies it once per qualifying day
  and guest, when the weather is bad: the first update of that day at which it rains, storms or snows (rain that starts late in a day still counts that day). `Guest.RainMoodDay` (the game day plus one; 0 never) holds the day, is saved only once set (optional `RainMoodDay`, validated non-negative,
  hashed only when set, so older saves and every park without the extension are byte-equal) and keeps a resumed park on the same run. An imported original guest without a time in the park counts its age from the load, so the rule then applies normally, every fourth day (`[EXT:rain-mood-imported-age]`, user-approved: optional saved `RainMoodAgeStartTurn`, the park turn of the import, written only for such a guest while the extension is on, validated non-negative and only without a time in the park, hashed only when set).
- **Independent of `opentpw.umbrellas`.** The penalty without a shop is allowed (nobody has an umbrella, everyone pays), and umbrellas without the rule are mood items only (purchase happiness, the balloon trade).
- **Recorded.** A save records `opentpw.rain-mood` always while it is on in a Theme Park World run (it is not in `UsedOnlyExtensionIds`: it changes every park with weather; in a Theme Park Inc run its gate is off, so nothing records it and an export is not refused for it: `ParkResumeMods.RecordableMods`); a recorded extension that is off or in another version refuses the load, like any simulation extension.
- **Measured** (headless park, one kid on a path, 7 park days = 161 turns of constant weather, rule on against rule off, same seed, the needs step unchanged): rain or storm costs **2 happiness in the week** without an umbrella (days 0 and 4 qualify, once each) and **0 with an open umbrella**; snow gives
  **+2** with or without one (an umbrella only softens a loss), with thirst -2 and hunger +2. The first day of a visit costs 1. Theme Park Inc's literal rule would hit on **11 updates in the same week (6 on the first day)**: -11 for the week, about -6 on a first rainy day, 0 with an umbrella.
  (The plan's earlier guess of about 23 a day forgot that the rule runs on a guest's own phase, every fourth park turn.) A guest who stays a day or two loses about 1 happiness to rain, so the once-per-day rule is mild: the penalty is a hint to build a brolly shop, not a threat to a park's happiness.
- **Tests.** `RainMoodTests` (the kid row, the umbrella modifier, the clamp, the heat wave exclusion, the day gate and the once-a-day rule, a kid losing exactly 1, rain that starts late, a closed umbrella protecting nothing, a rider, the hash with the rule off, the umbrella states with it on and off, the snapshot, the
  resume, the extension's registration and its gate), the extension row in `ModsScreenTests`, and the determinism pins ([DETERMINISM.md](DETERMINISM.md#rain-mood)). The extension's name and description are in all eleven interface languages.

## Phases

| Phase | What | State |
| --- | --- | --- |
| U0 | the trace in this page, `[BIN:TPI-EXE]` labels | done |
| U1 | art and data pipeline: umbrella sprites, anchors from the rig, the four shop models, `.sam`, RSE, `content/umbrellas` | done (original-set anchors left to U3) |
| U2 | simulation behind `opentpw.umbrellas`: purchase, states, scoring gate, balloon trade, attraction close, saves, pins; also a catalog root for the extension in `ModContentRoots.CatalogRoots` and its id in the `ObjectCatalog` cache key (`ObjectCatalog.cs`, where only the pools' root was named), or a park would reuse a catalog built without the shops | done |
| U3 | drawing: `UmbrellaRenderer`, anchors for the original sets, pull, bubble lift, browser delivery, captures | done |
| U4 | `opentpw.rain-mood` with its pins and the measurement of what it costs a kid | done ([below](#the-rain-mood-rule-phase-u4)) |
| U5 | tuning from captures, the umbrella's frame cost, the kid score measurement | done ([below](#tuning-phase-u5)); the first-person placement waits for a walk-around camera |

Open questions: Theme Park Inc's kid score is decided: 75 %, as in Theme Park Inc (U5); the shop's `ISIndoors` 0 where Theme Park Inc's is 1 (it keeps the indoor-in-rain x5 off the weather percentages, which U2 now scores); the three U2 extensions are user-approved (see the decisions above). The smarter-guests seam (`IGuestDecisions`) was not touched: the umbrella gate and the weather percentages live in the host score, for the extension's own objects only.

## Content (`[EXT:umbrellas-content]`)

`content/umbrellas` (copied next to the program with the rest of `content/`; embedded in the browser build as a whole, about 0.7 MB before compression, the sprite set being 29 KB) holds, all our own, generated from code and committed (CI has no Blender):

The content is our own (modelled and textured in Blender from the scripts in `tools/`), MIT-licensed like the rest of the repository; nothing in it comes from Theme Park Inc or Theme Park World.

- `objects.json`: four brolly shops, one per Theme Park World theme, ids **64500 to 64503** inside the extension's own range **64500 to 64999** (below the pools' 65000..65535, above the Theme Park Inc rides' 40000..40499), category `shops`, `"research": false`.
  The extension's id is `opentpw.umbrellas`, so a park save would name the files `mod:opentpw.umbrellas:/...` (as [POOLS.md](POOLS.md#content-extpools-content)).
- `objects/<theme>/brolly/`: `brolly.MD2`, eight 64 x 64 TGA textures (nine in space: an extra light strip), `Brolly.sam` and `Brolly.RSE`. The model is a **2 x 2 booth** (Theme Park Inc's is 3 x 3; smaller suits a stall) whose entrance is the lower left cell:
  a back wall, two side walls, a counter with three open umbrellas on it and an umbrella-shaped roof (eight panels in two alternating textures, a finial). Themed textures as the pool cabins: bamboo and thatch (jungle), mossy stone and purple shingles (hallow),
  riveted metal with cyan strips (space), pastel candy (fantasy). The walls, counter and posts keep at least 1 unit (1.6 by construction) from the footprint edge at a walker's height (the pools lesson, `[EXT:pools-model-inset]`); the roof reaches 1.0 from it.
- The **`.sam`** (`tools/umbrellas/brolly.sam`, built from the keys of the original balloon shop): `AppearanceEffect` 3, `ShopType` 1 (a gift shop's kind, OpenTPW's own choice), price 30 and cost of goods 13 (Theme Park Inc: 10 to 15), `HappinessEffect` 20, `LitterEffect` 15,
  capacity 10, `ISIndoors` 0 (so the original's indoor-in-rain x5 does not stack on the weather percentages), `WeatherModifier.Rain/Storm 200`, `Snow 150`, `HeatWave 5`, `GenerationModifier.KidScorePercentage` 75 (follows Theme Park Inc, per the owner's decision of October 18, 2026; OpenTPW had shipped 100), upgrade cost 1000, research cost 0, `Upgrades[0].RunningCost` 35. `WeatherModifier.*`, `GenerationModifier.*` and `RunningCost` are Theme Park Inc keys that Theme Park World does not read: the extension reads them for its own objects only, so original objects keep their scoring.
- The **`.RSE`** (`tools/umbrellas/brolly.rse`): the shop has no animation, so it is the pool changing room's script with shop timings (customers stand at the counter, are served for a second, walk on; `UsageInfo.RideHandlesSprite` 1 keeps them in view, [GUESTS.md](GUESTS.md#kept-bodies)).
- `esprites/Umbrellas/SPR_UM.ESP` + `.TPC`: the **umbrella sprites**, rendered by `tools/blender/opentpw_umbrella.py` with the pools kid camera (orthographic, 45 degrees, the same lights, grade and dark edge) at **2x (256 pixels per unit)**: three colourways as slots 0 to 2
  (plain sky blue, rainbow, coral and cream stripes), **five directions** (0 to 4; the game mirrors them to eight like the kids), one frame each, one 255-colour palette. An eight-panel scalloped canopy 0.8 of a kid's height across (Theme Park Inc's ratio after its 1.25 scale),
  a ferrule, a rim band, a hooked handle, the shaft tilted 14 degrees towards the body so that the canopy centre is over the head of a kid whose hand is at its side. The **hotspot of every frame is the handle tip**, the lowest point of the crook, which sits at the render's world origin,
  the point a kid's feet stand on. No closed-umbrella frame (a closed umbrella is not drawn).
- `anchors.json`: the **hand anchors** of the eight 2x kid sets, from the rig (`tools/blender/opentpw_guest_sprites.py --emit-anchors`): per set and direction 0 to 4, where the kid's right hand lands in the land frames, in pixels (256 per unit) from the feet hotspot, x right and y down, for the
  standing frame, every walking frame and the waiting frames, plus the hand's height above the feet. Exact by construction (the same camera as the frames; nothing is rendered, the table takes seconds). The umbrella is placed by putting its hotspot at the anchor
  (then pulled towards the camera, U3). Directions 5 to 7 are the mirror of 3 to 1: the game flips the frame, so the anchor's x is negated. The hand is a little in front of the body's axis, so the side views are not exact mirrors (within 2 pixels).
  The anchors of the original (1x) kid sets and the four costume sets are in `anchors-original.json` (phase U3, [below](#drawing-phase-u3)).
- Rebuild: `tools/umbrellas/build-content.sh` (Blender 5.x, the .NET SDK and python3; about 20 seconds): the shop models (`opentpw_brolly_shop.py`, which reuses the pool cabin's textures and geometry builder), the kit's `object` command, the umbrella sprites,
  `tools/umbrellas/pack_sprites.py` (which reuses the pools' set converter, `detailFlag` 0) and the anchors.

`UmbrellasContentTests` load the committed files through the game's own pipelines (the object catalog with a content root made by the test, the sprite readers, the ride script VM) and check the ids and names, the `.sam` keys, the models, textures, footprint and wall gaps,
the script, the sprite banks (slots, directions, 256 ppu, hotspots at the tip) and the anchor table (every set, slot and direction, mirrored symmetry, hands inside the shipped kid frames), and that nothing is wired into the game.

| Label | What |
| --- | --- |
| `[EXT:umbrellas-content]` | the extension's own shipped content (`content/umbrellas`) and the pipeline that builds it |
| `[EXT:umbrellas-extension]` | the extension itself: its registration, its gate, the host rules (U2), the objects' weather and generation terms, and its screen texts in the eleven languages |
| `[EXT:umbrellas-weather-map]` | the mapping of `ParkWeather` to Theme Park Inc's weather states (U2; user-approved 2026-10-18) |
| `[EXT:umbrellas-save]` | the save rule: recorded only while the park has a brolly shop or an umbrella, the placed shops' asset keys, the optional guest fields (U2) |
| `[EXT:umbrellas-save-due]` | the saved pending-change turn (U2; user-approved 2026-10-18) |
| `[EXT:umbrellas-balloon-gate]` | a balloon shop's refusal of a balloon holder, applied only in a park that uses umbrellas (U2; user-approved 2026-10-18) |
| `[EXT:rain-mood-once-per-day]` | the weather mood rule applied once per qualifying day, with the saved day of the last application (U4; user-approved 2026-10-18) |
| `[EXT:rain-mood-extension]` | the extension itself: its registration, its gate (`FeatureGates.RainMood`), off by default, always recorded while on (U4; user-approved 2026-10-18) |
| `[EXT:rain-mood-imported-age]` | an imported original guest without a known time in the park counts its rain mood age from the load, with the saved load turn (U4; user-approved 2026-10-18, "Tellen vanaf het laden") |
| `[BIN:TPI-EXE:0x...]` | the Theme Park Inc `Game.exe` address a rule was traced to (this page) |

## Drawing (phase U3)

`UmbrellaRenderer` (`World/Umbrellas`), created by `Level` beside `BalloonRenderer` while the extension is on and its content is there, draws one camera-facing quad of `SPR_UM` for every guest whose umbrella is **open or open until a due close** (states 4 and 1; a closed one, state 2, has no
sprite: `[BIN:TPI-EXE:0x60ae35]`, the placement routine `0x60AE00` compares the state with 4 and 1, re-read statically from the no-CD `Game.exe`). Presentation only: no random stream is read, nothing is saved, no pin moves. `[EXT:umbrellas-draw]`

- **Placement.** The sprite's hotspot, the handle's tip, goes to the guest's hand: the point of the hand anchor table for the sprite set, animation slot (stand, walk, wait; running follows walking), stored direction and step the guest is drawn with
  (`GuestRenderer.TryMount` returns those, after the guest's own depth pull; mirrored directions negate x). A walking guest's hand swings by up to 12 pixels with its arm, which would shake the umbrella, so the walking and waiting hands are the mean over the
  animation's steps (`SteadyHand`; checked in captures). A set without a table gets TPI's rule: straight above the guest at 5.7 units (`[BIN:TPI-EXE:0x60b29b]`: the set's Y, 5.5 to 6.5 in TPI's data, is added to the guest's height).
- **Pull.** TPI's default placement moves the hotspot 10 % (`GenericUmbrellaModifiers` X/ZAdjustCamera, `[BIN:TPI-EXE:0x60b236]`) of the way to the camera along the ground and lifts it by `YAdjustCamera` x (camera height - 40) (the 40.0 is `0x93E5B0`), so that it covers the guest.
  OpenTPW pulls along the sight line instead, as the guests' own depth pull is done (the screen position stays and the quad shrinks by the same share), because the lift's value is not known here. The guests' pull is 3.5 to 3.9 units, under 5 % of the park view's distances, so
  the umbrella is in front of the guest it covers. Not modelled: TPI's ground-plane pull and height lift, registered as `APPROX:UMB-004` (unknown: the `YAdjustCamera` value and so the resulting screen-space placement). Phase U5 cut the pull to a fixed 1.5 units because 10 % drew umbrellas over roofs that hide their guests ([Tuning](#tuning-phase-u5)).
- **Direction and colour.** The five stored directions are mirrored to eight by the same heading sector and camera sector as the guests (`GuestSpriteAtlas.SelectOriginalDirection`); the colour is the guest's `UmbrellaType` (the sprite slot).
- **Close under a track (TPI only, not modelled here).** TPI's draw routine also closes the umbrella (`[BIN:TPI-EXE:0x60AF32]`, reached from the test at `0x60AEDC`). With the guest's tile index `[+7] * 128 + [+5] + 1`, it calls `0x40A344` (to `0x67F890`) on the tile's 72-byte cell record and, when that holds, `0x405F01` (to `0x685680`) on the 40-byte record of the same index at game +`0x11FFD8`; when both are true it calls `0x4055AB` (to `0x6142E0`): state (`+0x238`) 2, the sprite handle (`+0x240`) destroyed through `0x4E3E90` and cleared, and the local "umbrella shown" byte (`[esp+0x13]`) zeroed, so the bubble of that frame hangs at 2.5 and not 6.0 (`0x60B0A4`). `0x685680` accepts the record types 0xB, 0xC, 0x10 and 0x11, which are the **track cell types** of docs/TRACKS.md (11 kart origin, 12 extension, 16 water origin, 17 water extension, low confidence for 17); `0x67F890` follows the cell record's word to a track record (an extension, 12 or 17, goes on to its origin) and accepts the types the track walk accepts (0xB, 0xD, 0x10, 0x12, 0x19: kart and water tiles and stations, plus 25). So a guest standing on a kart or water ride's track cell loses the umbrella at once. This changes the simulation (the state), so it belongs to U2 and is **not implemented**; the drawn umbrella here follows the state. Also see `UMB-002`.
- **Thought bubble.** While the umbrella shows, the guest's bubble hangs 6.0 units above it instead of 2.5 (`ThoughtRenderer.UmbrellaShown`, `[BIN:TPI-EXE:0x60b0a4]`: the word at `0x9CFB38` is 6.0, `0x9CFB34` is 2.5, chosen by the same flag).
- **Hand anchors of the original sets** (`content/umbrellas/anchors-original.json`, numbers only). `opentpw-kit measure-anchors --data <Data> --anchors content/umbrellas/anchors.json --pools content/pools/esprites/Generic/Kids --out <file>` (a maintainers' tool; the table is committed and a test
  reproduces it from the local game frames) reads the game's own frames. The eight 1x kid sets have a 2x redraw of the same pose, so their anchor is the 2x rig anchor at half the pixels, moved sideways by the difference of the two silhouettes' mean x (the redraws match the originals
  within about a pixel). The four costume sets have no redraw: a rule fitted on the kids gives the hand's height as a share of the silhouette's height above its bottom edge and its x as a share of half the box's width, per slot and direction. Leaving each kid set out in turn
  predicts its anchors to 3.55 pixels on average (largest 9.7; a kid is 37 pixels tall, and most of it is the swinging walking hand the umbrella does not follow). The 2x sets of the pools guest art use the rig table; the table follows the frame's pixels per unit.
- **Browser capture.** A real Chrome screenshot of the published browser build (Jungle, zoom 80, four umbrellas drawn) comes from `tools/web-perf/run.mjs --scenarios jungle,umbrellas`: the browser has no command line, so the hook `PerfUmbrellas` stands in for `--debug-umbrellas`.
- **Browser.** The whole folder (shops, the 29 KB sprite set, the tables) is embedded (`OpenTPW.Web.csproj`), 544,475 bytes, about 190 KB deflated (gzip -9 per file); every browser user downloads it with the build, also with the extension off. There is nothing else to fetch.
- **Captures** (Debug builds): `--debug-umbrellas` gives every guest an open umbrella, colour by id, and keeps it open; `--debug-umbrellas-follow zoom,yaw` holds the park camera on the first guest; with `--debug-weather 8` and `--capture-world`. `[EXT:debug-umbrellas]`

| Label | What |
| --- | --- |
| `[EXT:umbrellas-draw]` | the drawing: the renderer, its placement from the tables, the five stored directions mirrored to eight (TPI has no directions), the hand anchors (TPI anchors straight above the guest), the sight-line pull, the steady hand; user-approved (see the decisions) |
| `[EXT:debug-umbrellas]` | (debug-only test hook) the Debug-build capture and crowd flags (`--debug-umbrellas`, `--debug-umbrellas-follow`, `--debug-umbrellas-crowd`) and the browser harness hook `PerfUmbrellas` (`tools/web-perf/run.mjs --scenarios jungle,umbrellas`, docs/BROWSER-PERFORMANCE.md) |


## Tuning (phase U5)

October 18, 2026. Captures, headless measurements and frame times with the three phases running together (U2 simulation, U3 drawing, U4 rain mood). The canopy size and the pull cap are presentation and move no pin. The owner chose Theme Park Inc's kid score of 75 % (see below): the shipped `KidScorePercentage` is 75, and the synthetic brolly shop's default matches it. The shop pins were recomputed at that 75 and did not move ([DETERMINISM.md](DETERMINISM.md#umbrellas-tuning)).

### Captures

Native Debug runs of the four themes (Jungle, Fantasy, Hallow, Space) in rain (`--debug-weather 30`), storm (5) and snow (15, Hallow) and clear, the park camera at the zoom values 40 and 300 of the wide range and 70 and 130 of the original range (pitch 45 and 65 degrees, 84 above 188) at four yaws, with the pool guest art on and off, with the umbrellas extension off, and with a held balloon and a thought bubble:
`--debug-sim-minutes 3 --capture-frames 300 --debug-umbrellas --debug-weather 30 --debug-umbrellas-follow <zoom>,<yaw> --capture-world <png>`. The captures hold original sprites and stay out of the repository.

- **Hands and anchors.** The handle meets the hand and the canopy stands over the head in all four themes, with the 2x pool art (rig anchors) and with the original 1x sprites (measured anchors), facing the camera, away from it and sideways, standing and walking; the mirrored directions put the handle at the mirrored hand. No anchor needed a change.
- **Size** `[EXT:umbrellas-size]`. The set was drawn with the canopy 0.8 of a kid's standing height across (the plan's reading of Theme Park Inc's ratio), but the camera looks down at 45 degrees and shortens a standing kid by cos 45 while the canopy's width stays whole: on screen the canopy was about 1.2 to 1.25 times a kid's height and hid its head from every side (from the front at the near zoom it covered everything above the shoulders). The quad is now scaled 0.85 about the hotspot (`UmbrellaRenderer.SizeScale`; the handle tip stays in the hand), which brings the canopy to about the kid's on-screen height and leaves the face visible from the front. Theme Park Inc's own kid frame height is not known statically (its art is not in the executable), so the ratio could not be checked against it: this is a Theme Park World tuning, one constant.
- **Camera pull** `[EXT:umbrellas-pull-cap]`. Theme Park Inc pulls the umbrella 10 % of the way to the camera (`[BIN:TPI-EXE:0x60b236]`), a share of the distance, which is 8 units at the zoom value 80, 13 at 130 and 30 at the wide range's 300. In OpenTPW's depth buffer that is far more than the pull needs: the umbrella of a guest who stands behind a booth, a shelter or a ride came out **nearer to the camera than that scenery's roof** and was drawn over it, glass or white, while the guest itself was hidden (the browser capture of the Jungle bus shelter; reproduced natively at the entrance booths: `--debug-sim-minutes 0.8 --debug-umbrellas --debug-umbrellas-follow 80,35`), and it also drew over neighbours up to a cell nearer to the camera. The pull only has to win the depth test against the guest's own quad (itself pulled 3.5 to 3.9 units) and the ground, so it is now a fixed 1.5 units at every zoom (`UmbrellaRenderer.MaxPullUnits`; the share stays as an upper limit, which only applies below a distance of 15 units). With it the umbrellas of the guests behind the captured booths are no longer drawn over the roofs (captures before and after in the notes folder `umbrella-captures`, `occlusion-*`); the umbrella still sits 1.5 units nearer to the camera than its guest, so a roof edge that only just covers the guest can leave a corner of the umbrella visible, and the umbrella is in front of its own guest at every zoom. Pulls of 0.5, 1.5 and 3 units were compared; the three look the same on the booths.
- **Thought bubble.** The bubble hangs 6.0 units above the umbrella's guest (`[BIN:TPI-EXE:0x60b0a4]`, re-read: `fld [0x9CFB38]` with the umbrella flag, else `[0x9CFB34]`, added to the guest's height). It clears the canopy at every zoom, with a gap that grows at the near zoom (about 0.6 canopy widths at 45 degrees, 0.4 at 65). Kept as Theme Park Inc has it.
- **Mirroring, colours, draw order.** The three colours (blue, rainbow, red and white) read well on all four themes' paths and grass, in rain and snow and clear. A guest never holds a balloon and an open umbrella (a balloon shop scores 0 for an open umbrella, and an umbrella shop lets a held balloon go), so there is no balloon under a canopy to order; a balloon that is let go flies away as before. Guests and balloons take no point light, so neither does the umbrella; the pool water is drawn in the world pass and a swimmer has no umbrella, so no order problem showed.
- **Wide zoom.** At 300 (84 degrees) the umbrella sits over its guest and hides most of it, as an umbrella seen from above does; at 40 it is large and sharp. Fine.

### The original's close in the draw routine: a track cell, not a roof (UMB-002)

A reviewer found that the placement routine closes the umbrella too: at `0x60AEDC` to `0x60AF3E` it forms the guest's cell index `[+7] x 128 + [+5] + 1` and, when two cell tests both hold, calls `0x6142E0` (state 2, sprite destroyed) and does not draw. The guess was a covered cell such as the bus shelter. Re-read statically in the no-CD `Game.exe` (`[BIN:TPI-EXE:0x60aedc umbrella cell close]`; `0x40A344` and `0x405F01` are thunks to `0x67F890` and `0x685680`), the tests are **track cells**, the same predicates tp.exe has for its second map layer:

- `0x685680` on the 40-byte record of the same cell index (`[0xADE6B4] + 0x11FFD8 + 40 x index`, not through the tile record's first word): the record's type word `+8` is `0xB`, `0xC`, `0x10` or `0x11`: kart origin, its extension cell, water origin, water extension (`TrackCellTypes.KartOrigin` 11, `Extension` 12, `WaterOrigin` 16 and the second extension type 17 of `ParkCellTypes.IsTrackExtension`).
- `0x67F890` on the tile record (the 72-byte record of the cell index): it does not test the tile record itself but the 40-byte record found through the tile record's first word (`0x401839` to `0x5C17B0`: the word indexes the array at `[0xADE6B4] + 0x11FFD8`), and for an extension record (`0x685760`: type `0xC` or `0x11`) goes on through that record's owner word `+0x10` to its head (the array at `+0x120438`); on the record it ends up with it applies the track predicate (`0x6856D0`: types `0xB`, `0xD`, `0x10`, `0x12`, `0x19`, TPW's `ParkCellTypes.IsTrackCell`). All of this read with `dis.py` on the no-CD `Game.exe`.
- Both must hold, so the rule fires for a guest standing on a kart or water track tile (an origin, or an extension whose head is a track cell). The scheduling (`0x613EA0`) and the carrying out (`0x614010`) stand down on the same test: that is UMB-002's "view condition", which was misread as "not identified"; it is "on a track cell".

How often it would apply here: in 12 simulated park minutes of Jungle (a guest sampled every 15 ticks) with no track ride, with the go-kart track (`--debug-kart-track`) and with the boat track (`--debug-water-track`), 19,030, 26,450 and 26,111 guest samples stood on cells of type 1 (path), 30 (fixed walkway) and 9 (an object's entrance, riders included) only; **none stood on a type 11, 12, 16 or 17 cell**, and none at the bus shelter (an object's cells are types 4, 9 and 10, not track types). OpenTPW's walking guests cannot step on a track record (WALK-005) and its riders are inside the ride (state `Using`, not drawn, umbrella already closed on entering). **Recommendation: U2 need not implement it**; it would be dead code. UMB-002 is narrowed to that: the original also stands down on a track cell (the track-cell test), which OpenTPW's guests never occupy; the register text and the code comment say so. The occlusion problem of the Jungle bus shelter was the pull, not this rule (see Camera pull).

### Simulation: a rainy week (headless, fixed seed)

`UmbrellasTuningMeasureTests` (`OPENTPW_UMBRELLAS_MEASURE=<file>` writes the table below): the pinned synthetic cross park (a drinks shop, the brolly shop at (10, 1), paid through the economy), four clear days so guests arrive, seven days of rain, three clear days; mean over four seeds. The park is small (about 39 guests admitted in 14 days), so the counts are small and the point is the comparison. Every second arrival holds a balloon of life 120 (a fresh balloon cuts the shop's score to nothing: the original's factor `100 - life x 100 / 255` hits exactly 0 only at full life, which the zero quirk skips, but a life near full gives almost no score), the other columns have none.

| Park | Buyers | Buyers who held a balloon | Mean happiness over the rain week, rain mood off | rain mood on |
| --- | ---: | ---: | ---: | ---: |
| no brolly shop | 0 | - | 52.02 | 50.95 |
| brolly shop, kid score 75 % (Theme Park Inc) | 12.5 | 4.0 | 52.86 | 51.81 |
| brolly shop, kid score 100 % | 13.75 | 7.0 | 51.50 | 50.47 |
| no balloons: no shop | 0 | - | 51.89 | 50.82 |
| no balloons: shop, 75 % or 100 % (identical) | 13.0 | - | 52.57 | 51.54 |

- **Sales and revenue.** A brolly shop sells to about a third of the guests of a rainy week (13 of 39); at the shop's price a week of rain brings about 13 sales, and 100 % instead of 75 % adds about 1.25 sales (+10 %); the mean balance at the end of the 14 days is the same at both, $49,742.5 (rerun of the matrix on 2026-10-18), so there is no measurable difference in money in this fixture. With nobody holding a balloon the two scores sell the same (the shop is the only candidate whose score changes, and the guests choose the best of the others), so the choice only matters through the balloon holders.
- **Happiness.** The umbrella lifts a buyer well above a non-buyer (buyers 62 against 52 with 75 %). The rain mood rule costs everyone about 1 over the week with or without a shop (a kid loses 1 on each qualifying rainy day, `opentpw.rain-mood`), and a buyer loses about 1.3 of it too: the rule fires once on the first rainy update of a qualifying day, most likely before a guest has reached the shop, so the umbrella's protection (`UmbrellaModifier`) only shows on later qualifying days (not isolated here).
- **Balloon releases.** Each buyer who holds a balloon lets it go (`-15` happiness, `MediumHappinessChange`): 4 per week at 75 % and 7 at 100 % in this park. That is why 100 % lowers the week's mean happiness (51.50) below a park with no shop at all (52.02), while 75 % leaves it above (52.86).
- **Decided by the owner (2026-10-18: "75%, zoals TPI").** The shipped score is Theme Park Inc's **75 %**: 100 % sells about a tenth more umbrellas, but the extra sales are balloon holders trading a balloon for an umbrella at -15 happiness each, which more than cancels the umbrella's gain in the mean, and where nobody holds a balloon the two values are the same. `GenerationModifier.KidScorePercentage` is 75 in the four `content/umbrellas/objects/{jungle,fantasy,hallow,space}/brolly/Brolly.sam` files and in `tools/umbrellas/brolly.sam` (it had been left at OpenTPW's 100). `SyntheticPark`'s `brollyKidScore` defaults to that 75, so a synthetic brolly shop mirrors the shipped data. The numbers above would change with the balloon share of a real park, which this fixture sets at one guest in two.

### Frame cost

- **Native** (Debug build, Jungle, offscreen, 338 guests staged and 342 after the arrivals during the run, every one holding an open umbrella in rain; measured 2026-10-18, two rounds each, `Frame stats` of the run). The crowd is the park's 13 starting guests plus the 325 that `--debug-umbrellas-crowd 325` places once on the walkable path cells (`UmbrellaPerf.Stage`, the same staging as the browser harness), which is what the earlier figure's 343 guests came from; `--perf-arrivals` is not needed (`--perf-arrivals 1` alone only reaches 18 guests). The complete command, with `<zoom>` 110 (Home) or 300, and with and without `--disable-extension opentpw.umbrellas`:

  ```
  OPENTPW_GAME_PATH="<Theme Park World folder>" bash scripts/run.sh --load-original-level jungle --offscreen --silent --mute --no-advisor --no-vsync --frame-stats --perf-run 3000 --perf-zoom <zoom> --debug-weather 100 --debug-umbrellas --debug-umbrellas-crowd 325 [--disable-extension opentpw.umbrellas]
  ```

  | Zoom, extension | Frame avg (ms), rounds 1 / 2 | World render (ms) | CPU per frame (ms) |
  | --- | ---: | ---: | ---: |
  | 110, off | 3.46 / 3.75 | 1.35 / 1.35 | 4.43 / 4.90 |
  | 110, on (342 umbrellas) | 3.95 / 5.86 | 1.60 / 1.92 | 5.20 / 7.28 |
  | 300, off | 3.00 / 3.20 | 1.58 / 1.61 | 3.38 / 3.59 |
  | 300, on | 3.15 / 3.44 | 2.18 / 2.65 | 4.33 / 5.20 |

  The umbrellas add about 0.25 to 0.6 ms of world render at Home zoom and 0.6 to 1.0 ms at zoom 300 (one quad per umbrella, drawn in one call); the whole-frame difference (0.15 to 2.1 ms) is as large as the run-to-run spread of a Debug build on a shared machine, so read it as "under about a millisecond of drawing", not as zero.
- **Browser** (`tools/web-perf` scenario `umbrellaCost`, interpreter build, headless Chrome 155, Apple M5, 1280 x 900, 10 s windows, the same 400 guests in rain without and then with an open umbrella over each; median / p95 / p99 frame time; report `/Users/sander/projects/OpenTPW-notes/umbrella-perf/2026-10-18-umbrellaCost-window10.json`, outside the repository):

| | No umbrellas | 400 umbrellas | Extra |
| --- | ---: | ---: | ---: |
| Home zoom: median / p95 / p99 frame (ms) | 3.6 / 7.6 / 13.1 | 4.7 / 7.7 / 9.0 | +1.1 ms |
| Zoom 300 | 3.4 / 6.2 / 7.2 | 4.5 / 6.2 / 7.2 | +1.1 ms |
| Main thread busy (Home / 300) | 29.7 % / 24.9 % | 33.8 % / 32.7 % | +4 / +8 points |

  All 400 umbrellas were drawn at both zooms (the renderer's `DrawnUmbrellas`: 400 and 399), the page stayed at 60 frames a second and every frame stayed far inside 16.7 ms. The default 30 s windows of the first run (`.../2026-10-18-umbrellaCost.json`, kept next to it) give the same Home result (3.2 / 6.6 / 8.9 against 4.7 / 8.5 / 10.8, +1.5 ms, busy 25.8 % against 36.0 %), but its zoom 300 pair is not a valid comparison: the umbrella window had 251 guests left and drew none of them (the park keeps simulating through the long windows; the cause was not investigated, and the 10 s windows do not show it). The earlier table (+1.4 to +1.7 ms, +8 to +9 points) was one unrecorded run of the same scenario and agrees with these within the spread. Before this phase's trim (`UmbrellaAnchors` computed the steady hand's mean over the steps for every umbrella every frame, and the renderer enumerated its guest renderers through an interface) the zoom 300 window cost +3.2 ms; the means are now made at load and the loop is indexed.
  The harness stages the crowd and the umbrellas through `Program.PerfUmbrellaCrowd` (`UmbrellaPerf`, loopback `?perf=1` only): `node tools/web-perf/run.mjs --web <wwwroot> --game <folder> --scenarios jungle,umbrellaCost --window 10`.

### Bugs found and fixed here (cross-check against the owners' PRs)

- U3, first push: the browser build did not embed `content/umbrellas`, so there were no umbrellas and no brolly shops in the browser; the current U3 push (`3232e996`) embeds it, and this branch's browser run confirms 400 umbrellas drawn.
- U3: the camera pull was 10 % of the distance (8 units at the zoom value 80, up to 30 at the wide range's 300), which drew an umbrella over the roof of a booth or shelter that hides its guest, and over neighbours up to a cell nearer to the camera (fixed: `[EXT:umbrellas-pull-cap]`, now a fixed 1.5 units; the U3 reviewer's browser capture of the bus shelter shows it).
- U3: per frame and umbrella the steady hand re-averaged its steps with LINQ and the renderer enumerated its guest renderers through an interface (fixed independently here and in the second U3 review, `18f62814`: means at load, indexed loops; see Frame cost).
- Test fixture: the `UmbrellasFixedRunHash` park's `SyntheticPark` could not vary the kid score or share its harness; it is internal with a `brollyKidScore` argument. The default is the shipped 75 (the owner's decision). U5 had left the default at 100, which was then the number in the `.sam` files, so those pins did not move; setting the default to 75 was recomputed and the shop pins still stand, because the pinned runs hold no balloons and 75 % and 100 % sell the same then.

| Label | What |
| --- | --- |
| `[EXT:umbrellas-size]` | (user-approved 2026-10-18) the canopy scaled 0.85 about the hotspot (a Theme Park World tuning from captures) |
| `[EXT:umbrellas-pull-cap]` | (user-approved 2026-10-18) the pull towards the camera limited to a margin of 1.5 units (TPI's 10 % of the distance drew umbrellas over roofs that hide their guests) |
| `[EXT:umbrellas-tuning]` | the measurement aids: the headless harness (`UmbrellasTuningMeasureTests`) and the browser scenario (`UmbrellaPerf`) |
