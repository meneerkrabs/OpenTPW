# Original user interface: evidence and implementation

October 9, 2026. Status: an original-style front end (3D lobby + menus), in-game HUD,
options screen and pause menu are implemented with original UI models, textures, BF4
fonts and string tables, and run by default. They are **approximations** of the
original screens. Windows `TP.ICD` remains encrypted, but static analysis of
the identified Feral Mac executable now supplies original layout tables,
widget allocation and model binding evidence (see [PPC UI findings](reverse/PPC-ui.md)).
Most screen/controller/rendering choices below remain OpenTPW approximations. No original
captures were compared, so visual fidelity is **not verified**.

The CD's autorun launcher window is documented separately in [AUTORUN.md](AUTORUN.md).

## Inventory

All counts from read-only listings of the installed English data.

| Source | Contents | Use |
| --- | --- | --- |
| `ui.wad` | 278 `.MD2` UI models; 459 `textures/*.wct` and 462 `stexture/*.wct` (low-detail copies, plus `chatpan2`, `mb_gobuy[d]`); `qickload.txt` lists | Buttons, panels, windows, HUD |
| `lobby.wad` | `lobby.txt`, `jungle/fantasy/hallow/space.txt`; `terrain/` 38 MD2 (Base heightfield + sea, four `*_isle`/`*_gate` with `M1..M3` animations, butterflies, bats, golden ticket) + 4 `.sgn` + 167 textures; `globe/` 6 MD2 (online world globe, plane, pins) + 28 textures | 3D front end |
| `ui/cursors` | `C???.tga` 4-frame strips + Windows `.ani`/`.cur` | not drawn yet (system cursor) |
| `Init/<400..1024>` | per-resolution splash/legal/welcome TGAs per language | not used |
| `postcard/`, `2dmap/` | postcard art, map sprites | not used |
| `Language/<lang>/` | `UITEXT.str` (473), `UIHELPTEXT.str` (589 popup help lines), `THEMENAMES.str` (4), `OBJECT_NAMES.str`, 33 BF4 fonts | all text |
| level `rides/<ride>.wad` | `P<name>.MD2` preview model (Rides.sam `Info.PreviewAnimType "m"`, `PreviewAnimNum 1`) | build-menu icons |
| `Rides.sam` | `Info.WhichUIType` 0 rides, 1 shops, 2 sideshows, 3 features | build categories |

No loose `.sam` or text layout files for these screens were found. The Mac
executable contains embedded layout command tables; 55 identified tables have
been decoded as metadata, independently of the currently authored screens.

### Original drawing model registry

`UiModels` now resolves original drawing keys from each decoded model's actual
root-node name: signed-byte XOR followed by multiplication by 47, modulo 2^32.
Root names retain case and spaces. For example, `mainpanel.MD2` binds `base`,
`panel.MD2` binds `pan_money`, and `f_optpanel2.MD2` binds `optpanel2`. The filename
is an explicit presentation asset alias, not an original drawing key.

The 278-model corpus has 277 ordinary bindings: both `shadow1.MD2` and
`w_small_shadow.MD2` store `wshadow1`. The original loader has an exclusion for
`w_small_shadow.md2`, compared with case-sensitive `strcmp`; whether its input
was lowercased first remains untraced. The selected-corpus registry applies that
specific exclusion, and the asset stays available through explicit lookup. Other duplicate
root hashes fail with both conflicting assets named; filename aliases also fail
when ambiguous instead of selecting whichever member appears first. Existing
widgets can still request unambiguous filename aliases, so this change does not
rewrite screen layouts or alter custom resolutions, upscaling or interface scale.

Verification: a differing-filename/root regression failed before the fix; eight
binder cases plus the all-278-model corpus test now pass. The complete UI suite
passes 56 tests with zero skips. Original visual fidelity remains unverified.

### The UI model coordinate space (resolves the MD2-MODELS.md open question)

- Full-screen models (`f_chat`, `w_map`, `f_screen`) span exactly x 0..2048,
  y 0..−1536: UI models are authored in a **2048×1536 virtual screen** with y up
  (negative downwards). `f_viewfinder` is centred at (1024, 768).
- Models are either *placed* (root translation = on-screen centre: e.g.
  `mainpanel` (238, 1245.7) bottom-left, `date` (284.4, 1084.1) on it, `gauge`,
  `panel` arms (668.4, 1281.6), `islandlobby` (254.3, 1318.6), `f_tagl/m/r`
  message tags, `cashtrend`, `b_eject` bottom-right) or *code-positioned* (root
  at the origin: generic buttons, windows, list frames). Several buttons share
  one placed position (`b_buy`, `b_info`, `b_map`, `b_resrch`, `b_camera` all at
  (229.3, 1181.2)), so the code moves them; their final places are unknown.
- Child nodes are **alternative state frames**, not parts: buttons have six
  meshes named e.g. `b_buy, disable, hilite, heldown, hidown, down`, each child
  translated about one button width (+98…+178) along x from its parent. Composing
  the matrices would march the states off screen; using a child matrix alone puts
  it near the origin in the wrong plane. The header bounds of UI files equal the
  box of every node's stored matrix applied alone (b_buy: x 82.2 = 141.4 − 59.2
  from `hidown`, y 0 from the unrotated children), which is the "uncomposed"
  match recorded in MD2-MODELS.md; it describes how the exporter computed bounds,
  not where states are drawn. OpenTPW draws a state frame with the child's own
  rotation/scale and the root pose, so all states coincide with the root mesh.
  This is consistent with all 278 models; it is inferred, not seen in code.
- Frame order and textures: frames 0–2 (normal, disabled, highlight) use the
  plain art, 3–5 (pressed states) the `…d` art (`b_door`: open door normally,
  closed door when down). `panel.MD2` stores four arm variants
  (`pan_money, pan_info, pan_buy, pan_staff`) the same way.
- Texture V runs bottom-up as in the 3D shaders: button UVs span v 0.42..1
  while the art occupies the top 58% of the texture. Pure pink (255, 0, 255)
  fills every unused texel and is the transparent key.

### Lobby data (lobby.wad)

`lobby.txt`: `ISLANDFOV(100)`, `SPINSPEED(0.02)`, `SPINRADIUS(70)`,
`VERTICALOFFSET(20)`, `GLOBERADIUSOUT/IN`, `ISLANDCAMERAPOSITION(i, x, z)` for ten
slots. Theme files: `ISLAND(index, dir, island, gate, name, angle, height)`
(jungle 0 at 90°/12.5, fantasy 1 at 270°/17.5, hallow 2 at 180°/38, space 3 at
0°/35), `FLYINGMESH`, `SKYCOLOUR`, `RAINY`, `LIGHTNING`. Islands are ~50 units
across around their origin; `Base.MD2` holds the 111×110 heightfield and the sea
meshes. Reading angle as yaw and height as the camera target height is an
inference; FOV and spin units are unknown.

## Implementation

Code: `source/OpenTPW/UI/Original/` (canvas, models, batch/renderer, widgets,
screens, options), `FrontEnd/`, `Hud/`, `World/Lobby/LobbyScene.cs`,
`Client/GameFlow.cs`; shader `content/shaders/ui-batch.shader`.

- **One scale helper** (`UiCanvas`): the 2048×1536 canvas scales uniformly to fit
  the framebuffer times the user UI scale; each element keeps its authored
  distance to an anchor edge, so corner HUD parts stay in corners on wide
  screens (4:3 reproduces the authored layout exactly). BF4 fonts come in SMALL,
  MED and BIG versions; the tier is chosen by scale (< 0.36, < 0.6, larger) and
  text is point-sampled at an integer scale (≥ 2 only above ~4K). Both are
  inferences from the shipped files.
- **Rendering**: `UiBatch` (CPU triangle list) → `UiRenderer` (one alpha-blended
  pipeline; images linear filtered with the pink key bled to neighbour colours,
  font atlases point sampled). Model frames are stretched into element rects.
- **Input**: hover focuses, press+release on the same element activates
  (buttons react on release), wheel/arrows change option rows, Up/Down move
  focus, Enter activates, Escape goes back/opens the pause menu, P pauses.
  The HUD handles input before `Level.Update` and sets `Level.UiCapturesMouse`,
  so clicks on the HUD never build; park clicks use the same ground pick as
  `Level` placement to select the ride.
- **Popup help** shows the original UIHELPTEXT line of the hovered element
  (top-centre box: placement/background are OpenTPW choices).

### Front end (default start)

`bash scripts/run.sh --game-path …` opens the lobby. `--load-original-level`,
`--sandbox` (the former default sandbox) and plain `--smoke-test` bypass it;
`--front-end --smoke-test` tests it.

- 3D lobby: terrain/sea + four islands and gates at their ISLANDCAMERAPOSITION,
  turned by the ISLAND angle; the camera orbits the selected island at
  SPINRADIUS/VERTICALOFFSET and glides between islands; the sky is SKYCOLOUR.
  Not rendered: flying meshes, rain, lightning, island/gate animations, globe.
- Menu: `islandlobby` panel + `f_lobbutbg` with the theme name (THEMENAMES) and
  previous / enter / next island buttons (`b_lobleft`, `b_entpark`,
  `b_lobright`, tooltips UIHELPTEXT 343–345); the TPW logo model and "Theme Park
  World" (UITEXT 421) title; Load (3), Options (6), Quit Game (8) as text buttons
  on the original `purple_button` art. Positions inside the panel and of the
  right-hand buttons are approximations.
- Enter park → Game Mode window (239; Instant Action 240 / Full Simulation 241,
  tooltips 358/359) → starts a new park on the original level of the island.
  Instant Action adds the `Easy_` balance layer and the shipped `Easymode.TPWI`
  seed where the level has them and gates tickets, challenges, loans, the
  research lab and upgrades; Full Simulation starts with the standard balance
  and no seed (traced in the Mac binary, see reverse/FINDINGS.md and
  docs/ECONOMY.md, "Game modes"). `--load-original-level` and the Load Park
  entry keep the read-only reference start (shipped save, Full Simulation rules)
  whatever mode was chosen before (UI-041).
  The original fixes the mode per player and resumes each theme's autosave; every
  OpenTPW entry starts a new park (UI-015).
- Load Park (202): original Easymode parks (read-only) and the OpenTPW sandbox
  save. Quit asks with the original confirmation (9).
- Not implemented: player profiles (Create/Select New Player), online world,
  golden tickets/keys, credits. (The start-up movies now play before the lobby: docs/TGQ-MOVIES.md.)

### Button text fitting

Button labels shrink to fit their button (`UiTextFit`, `[EXT:fit-button-text]`). The candidates are the
role's font and the smaller sizes of the same BF4 family (`MENUBIG` → `MENUMED` → `MENUSMALL`, `TITLE…`,
`SESH…`, `GAME12AA` → `GAME10AA` → `GAME8AA`, `GAMEBOLD12` → `GAMEBOLD10`), each at every whole text
scale up to the current one so glyphs stay pixel-exact; the largest that fits wins. Only when none fits
is the label wrapped in the smallest size.

Fitting uses the drawn pixels, not the BF4 line box: glyph offsets put ink above and below the line box,
so a label measured by its line box could hang out of the button. Horizontally the label's own ink is
centred; vertically the font's letter box (capitals, ascenders, descenders) is centred, so neighbouring
buttons share a baseline. The `purple_button` texture holds two button ends, not two whole buttons: the upper half is a bar with
a rounded left end (normal), the lower half a lighter bar with a rounded right end (focused/pressed);
each bar fills 48 of the half's 64 rows and is cut flat at the other end. A button is drawn as that
piece plus its mirror image, rounded caps at their own aspect and the body stretched to meet in the
middle (UI-008), so both ends are rounded. The label is fitted inside the bar with a margin off the rim
and the rounded ends.

### Options (Game Options, 314)

The page follows the options table of the Mac build (authored 2048x1536 coordinates, 57
controls; `OptionsScreen`). The `f_screen` root (frame plus tiled wave background) covers the
4:3 area; all elements are centre-anchored, so wider outputs show the lobby beside it. The
title (UITEXT 314) is in the yellow title font. Labels are small dark text
([UI-037]) inside the light-green bars, left aligned, label and value in one string
without shadow, fitted to the bar (`UiLabel.Fit`, so long translations shrink).

| Row (panel model) | Control | Wired to |
| --- | --- | --- |
| 3D card rendering (`f_optpanel`, 120008) | `b_on2`, disabled | fixed: OpenTPW always renders on the GPU (UI-038) |
| Videocard: Primary (`f_optpanel`, 120010) | `b_on2`, disabled | fixed (UI-038) |
| Screen resolution (`f_optpanel3`, 120021) | slider over `IDisplaySettings.GetResolutions` | pending display size; OK applies it with the keep-or-revert flow |
| Graphics quality (120022) | slider over `IGraphicsSettings.Presets` | Low/Medium/High, then Enhanced (OpenTPW extension, `[EXT:COMPAT-GFX-ENHANCED]`, last step), Custom only while current; applied on OK, restart notice when `RestartRequired` |
| Audio quality (120032) | slider, disabled at 100 % | fixed (UI-038): OpenTPW has no audio quality setting |
| Sound effects / Music / Speech / Movie volume (`f_optpanel2`, 120024-120030) | slider 0..10 (shown as 0..100 %) + `b_on` mute toggle | `GameOptions` volume and `*On`; `GameOptions.*Gain` is 0 while off |
| Advisor, Tutorial, Popup help, Confirmations, RMB cancel (`f_optpanel`, 120011-120015) | `b_on` toggle | `GameOptions` bools |
| Rotation (Smooth / 90 degs), Scroll (Pushscroll / Right button) (120016, 120017) | `b_on2` cycle button | `GameOptions.Rotation`, `Scroll` |
| OK (`b_okay`), Cancel (`b_exit`) on `!f_plain` (120031) | buttons | OK applies, Cancel/Escape/right click discard |

`b_on` draws its normal and highlight frames as ON (upper indicator lit) and the down frame
as OFF, so a toggle is a `UiButton` with `Selected = () => !isOn`. `UiSlider` draws the
`b_scroller` ball on the track; clicking or dragging inside the hit region sets the value
from the mouse x, the wheel and Left/Right step it (UI-036).

Wired today: movie volume and its mute toggle (start-up movies), popup help (hover
help), screen resolution/size, graphics preset. STORED ONLY, no effect yet and listed in the
`GameOptions` doc comment: sound effects, music and speech volume and mute (no game audio
mixes them), Advisor (the advisor speaks only from `--advisor-say`), Tutorial,
Confirmations, RMB cancel, Rotation and Scroll. Options files from older versions get the
defaults (all on, 90 degs, pushscroll).

Option labels share one size per page (`UiLabelGroup`, UI-039): the family size and whole text
scale whose letter box (capitals, ascenders, descenders) is closest to 58 % of the label
rectangle height without exceeding it - the capture's labels are about 26 of 1536 units tall in
45-unit rectangles. A label uses it when the widest text it can show (all values of its row,
in the current language) fits its rectangle, so the size does not change while a value changes;
only a label that does not fit drops, alone, to the largest smaller size that does (in
English every label has the page size, in German only a few long ones are smaller). The title
keeps its own font.

The "OpenTPW" button (purple text button left of the OK panel, `[EXT:opentpw-page]`) opens a
second page built from the same original pieces: an `f_screen` page titled "OpenTPW"
(title font and position of 120020) with `f_optpanel` bars in two columns (left x 57..690, right
x 1331..1964, rows at y 167/323/479, the authored right column's panel size and pitch),
each with a dark label in the shared size and a `b_on2` cycle button: window mode (windowed /
borderless / exclusive "Full screen"), upscaling (Native/Linear/Nearest), render scale (Native,
presets 77/67/59/50 %, others labelled with the original " Custom"), interface scale (Automatic
or 1x-8x), enhanced textures (see below) and language. Clicking the button or wheel up/Right cycles
forward, wheel down/Left backward, both wrapping. The effective internal/output size and
fallback reason are small dark text below the rows. Game files is a purple button where the
main page has its OpenTPW button, and Back is the `b_okay` button in the OK place on the
`!f_plain` panel (Escape and right click also go back). It edits the same pending state: Back
returns to the original page with the edits still pending, and OK on the original page applies
both pages; Cancel/Escape there discards everything.

The enhanced-textures row (`enhancedTextures`, `[EXT:texture-pack]`) cycles "Original" (the game's own textures),
"Enhanced" (the default: the HD interface art shipped in `content/hero-art` plus the local `enhanced` pack
when one is built), then every other installed texture pack (a directory under `<config>/texture-packs` with a
valid `pack.json`; see TEXTURE-PACKS.md): `detailed` is labelled "Detailed" (translated in all six languages),
other pack names are shown as they are. Its popup help explains the choice and that switching runs in the
background. The choice is applied on OK, without a restart and without a loading screen: the options close
and the game loop swaps the textures of the running game in place while the player keeps playing; a
non-blocking line at the top centre ("Updating textures in the background... n / total" with a thin bar,
`OptionsScreen.DrawTextureSwitchProgress`) shows the progress and disappears when the switch is done. No
restart notice appears for a texture pack.

OK saves `save/opentpw-options.json`. Size/window-mode changes use
`ApplyWithConfirmation` (15 s) with the original UITEXT 400 question showing the display's
countdown - Yes -> `Confirm`, No -> `Revert`, and on No or the timeout the original 401
message; upscaling, render scale and UI scale use `Apply`. A language change (and a
graphics change that needs a restart) shows UITEXT 402 (RESTART GAME). `StubDisplaySettings`
is only for tests/headless use. Audio code reads volumes via `GameOptions.Current`
(`MovieGain` etc.).

### Local art overrides

`UiArtOverrides` (`[EXT:art-override]`) lets a player replace an original UI element with their own
sharper art: a PNG at `<config>/art/<name>.png` (on macOS `~/Library/Application Support/OpenTPW/art`).
Only the lobby logo uses it so far: `tpw_logo.png` is drawn, aspect-fitted, in the `tpwlogo` model's box
instead of the model, whose 128×128 texture holds the logo in about 90×28 pixels. OpenTPW ships no
override art; without a file the original is drawn. The CD's autorun archive (`Autorun/general.tre`)
holds a sharper copy of the logo (about 215 pixels wide) in data players already own.

### Lobby button panel

`f_lobbutbg` maps `ipan`, an opaque texture with a black background, through a texture slot with flag
0x2; `w_dialog` and `f_train` map the same texture without the flag and draw it opaque. Of the 185 ui.wad
slots with flag 0x2, the other 184 textures carry their own alpha, so the flag reads as "transparent":
for a fully opaque texture on such a slot black is keyed out (UI-034), leaving the panel's blue edge.

### Resolution independence

The UI is built in drawable pixels in the renderer's overlay pass (after world
upscaling, so render scale never blurs it). The canvas fits the 2048×1536 layout
to the pixel size; BF4 text uses the requested integer UI scale up to the
existing display policy's reference-layout fit (`UiScaling`, 1280×720). An
oversized manual request falls back to the largest fitting integer (at least
1); the request stays saved, the fallback is logged, and options show requested
and applied values. Thus 1280×720 with a requested 2× uses 1×; 2560×1440 uses
2×. This is an OpenTPW extension (`EXT:interface-scale-fit`), with no claim
about original game scaling. The font tier is picked from the fitted logical
size, so a HiDPI output shows the same layout and fonts
as its logical size with every glyph texel a pixel-exact 2×2 block (verified with
`OPENTPW_TEST_PIXEL_SCALE=2`). Mouse input is converted from logical to pixel units.

### OpenTPW supplementary strings

Labels the original tables lack (window mode, upscaling, render/interface scale,
language, back, date format, read-only/save/build messages, "not simulated yet")
are in `UI/Original/SupplementaryStrings.cs`: repository-owned, clearly OpenTPW
additions, translated for English, Danish, Dutch, French, German and Swedish
(native-language names for the language row). They render with the original BF4
fonts; tests check that every used original and supplementary string has a
glyph in all 14 UI fonts of each language.

### In-game HUD

- Placed originals at their authored places (anchored bottom-left):
  `mainpanel`, `gauge` (tooltip 477), `date` display with the date (478), the
  bank balance (465; UITEXT 448 "$ " prefix, which French and German leave
  empty; digits grouped with commas — an OpenTPW choice), the `f_tagl/m/r`
  message area (up to three recent messages, 8 s), and the `panel` arms
  (`pan_buy`, `pan_info`) with `b_retract` (481) at its authored place.
- Panel buttons `b_buy` (469), `b_info` (470), `b_money` (471), `b_resrch` (472),
  `b_map` (473): their places on the panel are approximations; only Buy works,
  the others show their disabled frame.
- Build arm: `b_srides/b_sshop/b_sshow/b_sfeature` category buttons (521–524),
  the category title (119–122) and three-slot pages with previous/next arrows.
  Every buildable object in the current theme is accessible, ordered by Info.Id,
  with a turning preview of
  the original `P<name>.MD2` (CPU orthographic projection, 30° tilt, painter
  sorted — not the original 3D draw), the object name and the economy catalogue
  price; unresearched items are greyed and refused. Choosing an item starts
  placement (message UIHELPTEXT 440). `Level.PlaceObject` checks the footprint,
  purchases through `ParkEconomy.TryBuild`, creates the original object and links
  its guest accounting; the HUD never charges again. Removing it through the
  level's object collection sells it once for its economy scrap value. Tools,
  fixed items and standalone upgrades are excluded by `ObjectCatalog.Buildable`.
  Official bonus objects use their original names, but their separate-root
  preview icons are omitted; items absent from the economy catalogue stay
  unavailable in original levels.
- Info arm for the selected original object (picked by occupied grid cell): name (OBJECT_NAMES), Excitement (the original
  default `UsageInfo.ExcitementLevel`, 70 for the Totem), Reliability, State of
  repair, Remaining life ("Not simulated yet"), open/close (`b_door`) and
  delete (`b_erase`). Open/close is disabled for non-attractions; fixed items
  cannot be deleted. Unknown statistics remain labelled as not simulated.
  Read-only visits disable build, open/close, delete and Save; callbacks also
  reject direct mutation attempts using the existing localized online message.
- Speed control (OpenTPW addition, bottom-right): pause, ×1, ×2, ×4 set the
  economy's `GameSpeed` (its clock runs Speed ticks per fixed tick); pause also
  stops rides and guests (`Level.SimulationTimeScale`).
- Pause menu (Escape): PAUSED (403) with Resume (7), Save (4), Load (3), Options
  (6), Exit To Lobby (12), Quit Game (8, confirmation 9). Original parks cannot
  be saved (message); the sandbox saves its OpenTPW JSON.
- `--load-original-level` and `--sandbox` keep the ImGui developer panel and the
  sandbox BF4 panel next to the HUD; games started from the front end hide them.

### Interfaces for other slices

| Interface | Owner | Stub |
| --- | --- | --- |
| `Hud.IHudParkStatus` (money, date, speed, price/availability) | economy | `EconomyParkStatus` binds to `Level.Park` (looked up on every access, so a loaded park save is followed): balance, `IParkClock` date and speed; `Level.PlaceObject`/object removal own `TryBuild`/`Sell`; `NoEconomyStatus` for the economy-less sandbox; `StubParkStatus` for tests only |
| `Hud.IBuildCatalog` (`BuildItem`: category = WhichUIType, OBJECT_NAMES index, cost, preview model, texture dirs) | rides/objects | `OriginalBuildCatalog` adapts `Level.Objects.Catalog`, with original names, preview models and `Upgrades[0].CostOfUpgrade`; `TotemBuildCatalog` remains a test fixture |
| `Hud.ObjectInfo`/`ObjectStat` for the info arm | rides/guests/economy | `ParkHud.SelectedInfo` follows `OriginalObject` |
| `IDisplaySettings` (real: the renderer) | display | `StubDisplaySettings` (tests only) |
| `GameOptions.Current` volumes, mute toggles and gameplay switches | audio, camera, tutorial/advisor | movie volume only; the rest is stored (see Options) |

`GameFlow.StartLevel` binds the HUD; guest admissions reach the balance through the economy's guest bridge.

## Verification

- Tests (`OriginalUiTests`, no assets): canvas mapping/anchors/font tiers,
  mouse and keyboard navigation, option rows, modal covering, front-end island/
  game-mode/quit flow, load screen labels, lobby file parsing, option labels,
  cancel/confirm/timeout-revert/restart flows, stub display, options file,
  supplementary string coverage and placeholders, stub park status, pink key,
  glyph batching.
- Asset tests (`OriginalUiAssetTests`): all 278 UI models flatten; full-screen
  frames span 2048×1536; button states coincide with the root; every model and
  texture the screens use exists; the four lobby islands; Totem price/category
  and preview textures; all four theme menus expose every buildable catalogue entry,
  excluding fixed items/tools/upgrades; UIStrings ids pinned against English;
  every used string has glyphs in every UI font of all six languages.
- `--front-end --smoke-test` (Metal): lobby + menu capture, mouse click (next
  island), keys (left, Escape, Enter), options open/adjust/cancel, game mode,
  original jungle load, HUD capture with bank balance and date verified texel by
  texel against the BF4 atlas, build-page navigation, research refusal, Totem
  purchase (−3,250; research marked complete as smoke setup) with one paid
  economy object linked to guests, info arm, economy pause, sale (+1,625),
  a second naturally researched catalogue object, exact charges, overlap refusal
  without a charge, selected-object open/close, pause menu, refused save, lobby;
  a prepared read-only visit verifies disabled build/open/delete/save controls
  and their direct callbacks without mutating the park or writing a save; the visit
  exits to the lobby and jungle starts again in Instant Action (seed imported, no
  staff invented, research unchanged over 60 days, loans/research effort/upgrades
  refused, bank balance read back; English, 1280×720, 288 frames). The earlier UI flow
  passed in all six languages; the economy integration was verified in English
  (1280×720, UI scale 1) and, before reference-fit limiting, Dutch at
  1920×932 drawable/UI scale 2 with nearest upscaling at 50% render scale
  (the desktop clamps the requested 1920×1080 window). The smaller 1280×720 Dutch/nearest/50% case with requested UI
  scale 2 also verifies the explicit 1× fallback and full glyph readback. Captures: `artifacts/native-smoke-<language>-*.png`.

## Approximation register

Project rule: every UI value or rule that is not original data or hard evidence is
tagged at its site with `// [APPROX:UI-NNN] … — evidence needed: …` and logged once
at startup (`[APPROX:UI-NNN]` warnings from `UiApproximations.LogOnce`). Original
values carry `// [DATA:<file>:<field>]`, OpenTPW extensions (display/upscaling/
language rows and their supplementary strings) `// [EXT:…]`. Paths are relative to
`source/OpenTPW/`; line numbers are as of this commit.

| Id | Site(s) | Current value / rule | Evidence needed |
| --- | --- | --- | --- |
| UI-001 | `UI/Original/UiCanvas.cs:45` | non-4:3 outputs keep each element at its authored distance from the nearest edge (anchors) | original widescreen behaviour (none in 1999) / design decision |
| UI-002 | `UI/Original/UiCanvas.cs:41`, `UI/Original/UiText.cs:57` | BF4 font size tier (SMALL/MED/BIG) chosen by logical scale thresholds 0.36 and 0.6 | binary: font selection per screen mode |
| UI-003 | `UI/Original/UiModel.cs:60` | button/arm state frames drawn at the root node pose; child translations ignored | binary: UI model drawing code |
| UI-004 | `UI/Original/UiModel.cs:124` | UI model triangles drawn back to front by Z, grouped per texture | binary or capture of overlapping UI parts |
| UI-005 | `UI/Original/UiImages.cs:34` | pink (255,0,255) key with neighbour colour bleed; linear filtering of UI images | capture of edges at non-native resolutions |
| UI-006 | `UI/Original/UiText.cs:102` | text colours (white, yellow highlight/title, green values, grey disabled), shadow and backdrop colours | captures of original screens |
| UI-007 | `UI/Original/UiWidgets.cs:96` | one-pixel (×UI scale) drop shadow under UI text | captures of original screens |
| UI-008 | `UI/Original/UiWidgets.cs` | text buttons on purple_button art: each half is one end cap drawn with its mirror image; upper half normal, lower half focused/pressed | capture of the original front-end buttons |
| UI-010 | `UI/Original/UiScreen.cs:174` | popup help box at the top centre with a dark blue backdrop | capture of original popup help (helpbg art exists) |
| UI-011 | `UI/Original/UiScreen.cs:164` | modal screens dim what is below | captures of original dialogs |
| UI-012 | `UI/Original/UiInput.cs:49`, `UI/Original/UiScreen.cs:59` | hover focuses, release activates, arrows/Enter/Escape navigate, P pauses, right click backs out of modal screens | binary: input handling; KEYBOARD.str meaning |
| UI-013 | `UI/Original/UiDialogs.cs:9` | window sizes and inner layout of game mode, load, pause and message dialogs (the options page now follows the original table) | captures of original dialogs |
| UI-014 | `FrontEnd/FrontEndMenu.cs:76`, `FrontEnd/FrontEndMenu.cs:92` | positions inside the lobby panel (island name, prev/enter/next), logo/title placement, right-hand Load/Options/Quit column | capture of the original lobby screen |
| UI-015 | `FrontEnd/FrontEndMenu.cs:127` | front-end flow without player profiles: Game Mode is asked on every park entry instead of fixed at player creation (the original stores it per player, STP-PPC 0x1015D220/0x1013741C), and every entry starts a new park instead of resuming the player's per-theme autosave | PC confirmation of the Mac player and autosave flow (docs/reverse/PPC-scenarios.md, "Park entry") |
| UI-016 | `FrontEnd/LobbyDefinition.cs:79` | lobby ISLAND angle = island yaw in degrees, height = camera target height | binary: lobby script interpretation or capture |
| UI-017 | `World/LobbyCameraMode.cs:25` | lobby camera: SPINSPEED read as radians per 0.1 s, vertical field of view 60, 3/s glide between islands, ISLANDFOV unused | binary or capture of the lobby camera |
| UI-018 | `Client/GameFlow.cs:238`, `World/Lobby/LobbyScene.cs:14` | lobby sky drawn as a flat SKYCOLOUR backdrop; flying meshes, rain, lightning, animations not drawn | binary/capture of the lobby |
| UI-019 | `FrontEnd/LobbyDefinition.cs:92` | fallback island position (400 + index × 200, 400) when lobby.txt has none | none needed if lobby.txt is complete |
| UI-020 | `Hud/ParkHud.cs:119` | positions of buy/info/finance/research/map buttons on the main panel (shared authored centre) | capture of the original HUD |
| UI-021 | `Hud/ParkHud.cs:115`, `Hud/ParkHud.cs:80` | positions and fonts of the date and bank balance text; money grouped with ',' digits | capture of the original HUD; locale number format |
| UI-022 | `Hud/HudStubs.cs:88`, `Hud/HudStubs.cs:47`, `Hud/ParkHud.cs:126` | speed control (pause, ×1, ×2, ×4) bottom-right; faster speeds only speed up the economy clock, not rides/guests | binary: original game speed options (pause only is known) |
| UI-023 | `Hud/HudStubs.cs:9` | test-only stub calendar (2 s/day); the game shows the economy clock (see ECON tags) | none for the game path |
| UI-024 | `Hud/ParkHud.cs:144`, `Hud/ParkHud.cs:171`, `Hud/ParkHud.cs:207`, `Hud/HudStubs.cs:108`, `Hud/ParkHud.cs:581` | layout inside the build and info arms (category buttons, title, three-slot pages/arrows sorted by Info.Id, adaptive preview size to fit translated names/prices, stat rows, door/erase buttons) | captures of the original arms |
| UI-025 | `Hud/ParkHud.cs:28` | message area keeps up to 3 messages for 8 s in the f_tag frame | binary/capture of the original message system |
| UI-026 | `Hud/ParkHud.cs:586`, `Hud/PreviewIcon.cs:14` | build icons: CPU orthographic projection of P<name>.MD2 with 30° tilt, 0.8 rad/s turn, painter sorting | capture of the original build menu |
| UI-027 | `Hud/ParkHud.cs:481` | a park click selects the original object occupying its grid cell | binary: original picking |
| UI-028 | `Hud/ParkHud.cs:283` | excitement shown as '<ExcitementLevel>%'; reliability, repair and life shown as not simulated | capture of the original ride info; simulation |
| UI-029 | `Hud/ParkHud.cs:207` | b_door 'down' frames mean the ride is closed; b_erase used as the delete button | capture of the original ride panel |
| UI-030 | `UI/Original/Options/GameOptions.cs` | volumes in 0..10 steps (10 % each; the capture shows 75 %, so the original has finer steps), default 8; popup help, advisor, tutorial, confirmations and RMB cancel default on, rotation 90 degs, scroll pushscroll | original step count and defaults (registry/ini of the original options) |
| UI-031 | `Hud/ParkHud.cs:350` | one placement per menu selection; Level.PlaceObject owns purchase/guest linkage and its removal handler owns scrap credits | original build-tool continuation |
| UI-032 | `UI/Original/UiWidgets.cs:179`, `Hud/ParkHud.cs:590` | longer labels fall back to the small font; catalogue names greedily wrap in their slots | captures of translated original screens |
| UI-034 | `UI/Original/UiImages.cs` | a fully opaque texture on a transparent (flag 0x2) model slot keys out black; only `ipan` in the lobby `f_lobbutbg` panel | the original's render state for flagged texture slots |
| UI-035 | `Client/Movie/IntroPlaylist.cs` | start-up movies: the Mac order (bf, then a day-of-month trailer) assumed for the PC; input held at launch ignored until released; movies letterboxed to 640:352 | PC executable analysis or captures of the PC start-up sequence |
| UI-036 | `UI/Original/UiWidgets.cs` | options slider: ball centre moves linearly over the track for value index 0..steps-1; click/drag sets the nearest step | capture of the original slider ends or binary slider code |
| UI-037 | `UI/Original/UiText.cs` | option bar label colour (16,16,48), no drop shadow | exact label colour from a capture or the font palette |
| UI-038 | `UI/Original/Options/OptionsScreen.cs` | 3D card rendering, videocard and audio quality drawn fixed and disabled (OpenTPW has no software renderer, card choice or audio quality) | none for the game path |
| UI-039 | `UI/Original/UiWidgets.cs` | option label size: letter box about 58 % of the label rectangle height, shared per page; a label whose widest value does not fit drops alone to the largest size that does | capture of the original option labels in several languages |
| UI-040 | `Client/Autorun/AutorunView.cs` | autorun launcher focus rectangle: dotted frame inverting the pixels with even x + y, 2 pixels inside the button | capture of the original launcher with a focused button |
| UI-041 | `Client/GameFlow.cs:147` | Load Park opens a shipped park as the reference start (its own balance, Full Simulation rules) whatever Game Mode was last chosen; the original's GameType is not saved with a park but copied from the loading player's profile (`mEasyModeUser`) | player profiles and what the Mac park loader 0x11acfc reads from a park file |

Data-backed (tagged `[DATA]`): the 2048×1536 canvas and authored rectangles of
placed models (`ui.wad` roots/bounds), button state frames and texture order, V
flip, lobby.txt/theme files, balance, prices and research availability (the economy's
`.sam` data), Totem price (`Totem.sam`), build category (`Rides.sam`), excitement level (`Totem.sam`/
`Rides.sam` `UsageInfo.ExcitementLevel`), every UITEXT/UIHELPTEXT/THEMENAMES/
OBJECT_NAMES string, BF4 fonts.

## Open

Original screen positions of code-placed elements, menu flow details (profiles,
online), cursor drawing, button/arm animations (`b_door` etc. have no animation
members; arm slide-in is not reproduced), sounds, original thousands separators
and currency handling in French/German, D3D11/Vulkan runs, captures from the
original game for comparison.
