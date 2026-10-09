# Original user interface: evidence and implementation

October 9, 2026. Status: an original-style front end (3D lobby + menus), in-game HUD,
options screen and pause menu are implemented with original UI models, textures, BF4
fonts and string tables, and run by default. They are **approximations** of the
original screens: the original layout code is inside the encrypted executable
(`TP.ICD` has no readable strings), so only what the data files show is
original; everything else below is marked as an OpenTPW choice. No original
captures were compared, so visual fidelity is **not verified**.

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

No `.sam` or text layout files for screens exist; there is no UI layout data
outside the models themselves.

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
  tooltips 358/359) → loads the original level of the island
  (`--load-original-level` equivalent). Both modes currently play the same.
- Load Park (202): original Easymode parks (read-only) and the OpenTPW sandbox
  save. Quit asks with the original confirmation (9).
- Not implemented: player profiles (Create/Select New Player), online world,
  golden tickets/keys, credits, intro movies before the lobby.

### Options (Game Options, 314)

Original rows: Screen resolution (318, values 340–346 where the size matches,
otherwise the same " W x H" format), Sound effects / Music / Speech / Movie volume
(320–323, 0–10), Popup help (326, Yes/No). OpenTPW rows in the same style:
Window mode, Upscaling (Native/Linear/Nearest), Render scale (Native, presets
77/67/59/50 %, other values labelled with the original " Custom"), Interface
scale, Language. Each row is the original `f_optpanel2` bar with `b_sleft`/
`b_sright` arrows inside the original `w_med` window; OK is `b_okay`. OK applies:
options are saved to `save/opentpw-options.json`; display changes go through
`IDisplaySettings.Apply()` — *NeedsConfirmation* shows UITEXT 400 with a
15 s countdown, Yes keeps, No/timeout reverts and shows 401; *RestartRequired*
(stub) or a language change shows 402 (RESTART GAME). Back/Escape cancels and
restores.

The display slice owns `IDisplaySettings`; `UI/Original/Options/DisplaySettingsContract.cs`
is a placeholder copy, and `StubDisplaySettings` stores the window size in the
existing `GameWindowSize` setting (applied at the next start). Audio code should
read volumes from `GameOptions.Current` (`GameOptions.Gain`).

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
  the category title (119–122) and up to three items with a turning preview of
  the original `P<name>.MD2` (CPU orthographic projection, 30° tilt, painter
  sorted — not the original 3D draw), the object name and price. Choosing an
  item starts placement (message UIHELPTEXT 440); building charges the price.
- Info arm for the selected ride: name (OBJECT_NAMES), Excitement (the original
  default `UsageInfo.ExcitementLevel`, 70 for the Totem), Reliability, State of
  repair, Remaining life ("Not simulated yet"), open/close (`b_door`) and
  delete (`b_erase`).
- Speed control (OpenTPW addition, bottom-right): pause, ×1, ×2, ×4 drive
  `Level.SimulationTimeScale`.
- Pause menu (Escape): PAUSED (403) with Resume (7), Save (4), Load (3), Options
  (6), Exit To Lobby (12), Quit Game (8, confirmation 9). Original parks cannot
  be saved (message); the sandbox saves its OpenTPW JSON.
- `--load-original-level` and `--sandbox` keep the ImGui developer panel and the
  sandbox BF4 panel next to the HUD; games started from the front end hide them.

### Interfaces for other slices

| Interface | Owner | Stub |
| --- | --- | --- |
| `Hud.IHudParkStatus` (money, date, speed/time scale, spend/refund) | economy/simulation | `StubParkStatus`: starting cash `BankAccountInfo.InitialCash` from `Easy_Standard.sam` (100,000); calendar 2 s/day, 30-day months (placeholder) |
| `Hud.IBuildCatalog` (`BuildItem`: category = WhichUIType, OBJECT_NAMES index, cost, preview model, texture dirs) | rides/objects | `TotemBuildCatalog`: Totem, cost `Upgrades[0].CostOfUpgrade` 3,250 |
| `Hud.ObjectInfo`/`ObjectStat` for the info arm | rides/guests/economy | `ParkHud.Describe(PrototypeRide)` |
| `IDisplaySettings` | display | `StubDisplaySettings` |
| `GameOptions.Current` volumes | audio | — |

`GameFlow` builds the stubs in `StartLevel`; replace them there.

## Verification

- Tests (`OriginalUiTests`, no assets): canvas mapping/anchors/font tiers,
  mouse and keyboard navigation, option rows, modal covering, front-end island/
  game-mode/quit flow, load screen labels, lobby file parsing, option labels,
  cancel/confirm/timeout-revert/restart flows, stub display, options file,
  supplementary string coverage and placeholders, stub park status, pink key,
  glyph batching.
- Asset tests (`OriginalUiAssetTests`): all 278 UI models flatten; full-screen
  frames span 2048×1536; button states coincide with the root; every model and
  texture the screens use exists; the four lobby islands; Totem price/category,
  starting cash and preview textures; UIStrings ids pinned against English;
  every used string has glyphs in every UI font of all six languages.
- `--front-end --smoke-test` (Metal): lobby + menu capture, mouse click (next
  island), keys (left, Escape, Enter), options open/adjust/cancel, game mode,
  original jungle load, HUD capture with bank balance and date verified texel by
  texel against the BF4 atlas, build arm, Totem purchase (−3,250), info arm,
  pause menu, refused save, exit to lobby. Passed in all six languages
  (captures `artifacts/native-smoke-<language>-*.png`).

## Open

Original screen positions of code-placed elements, menu flow details (profiles,
online), cursor drawing, button/arm animations (`b_door` etc. have no animation
members; arm slide-in is not reproduced), sounds, original thousands separators
and currency handling in French/German, D3D11/Vulkan runs, captures from the
original game for comparison.
