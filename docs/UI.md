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
| `postcard/`, `2dmap/` | postcard art, map sprites | the per-theme `2dmap.tga` is the map terrain image; sprite layers are not drawn yet |
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
- Missing state art: the disable frames (1) of `b_callmech`, `b_callhandy` and `b_upgrade` name
  `b_callmechg`, `b_callhandyg` and `b_upgradeg`, and `b_loantog`'s pressed frames (3–5) name art
  too, that `ui.wad` does not ship (pinned by `OnlyTheCallUpgradeAndLoanToggleStateFramesNameMissingTextures`).
  Drawn as is they come out as untextured white squares; `UiButton` draws the normal frame instead,
  greyed (160, 160, 160) when disabled (`[EXT:missing-state-texture]`). The original's texture binder
  `0x46EAD0` (called by the UI model loaders `0x488D20`, `0x488EE0`, `0x489060`) logs "Could not load
  texture '%s'" and binds entry 0 of its texture table instead; which texture that entry holds at run
  time is not known, so the original's look of these frames is not known either.
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
meshes. The angle is the island and gate models' yaw in degrees and the height
the Y of the island's camera target (tp.exe 0x5dfa60, 0x5e1210). The island
view's eye orbits that target at SPINRADIUS and VERTICALOFFSET above; its angle
advances by SPINSPEED radians per tenth of a second (the lobby step is the
frame's milliseconds, at most 500, × 0.01; 0x5d5c50, 0x5e0727), the eye and the
target move 0.1 and 0.2 of the way to their goals per step (0x5e1210), and the
field of view is ISLANDFOV (0x5e13ed; the globe's is 60, 0x5dd034), applied by the camera's
method at 0x5d8cb0 to the projection 0x578be0 and the view gamut 0x56b790; it is vertical,
inferred from 0x582eb0/0x56b790 (the gamut divides only the x extent by height/width). The glide
is a share per frame, as in tp.exe, stepped at the 60 Hz reference frame rate (`[EXT:frame-driven-at-60hz]`,
docs/COMPATIBILITY.md "Frame-driven timing"), so it converges at the same speed on every display;
the spin is linear in time.

## Implementation

Code: `source/OpenTPW/UI/Original/` (canvas, models, batch/renderer, widgets,
screens, options), `FrontEnd/`, `Hud/`, `World/Lobby/LobbyScene.cs`,
`Client/GameFlow.cs`; shader `content/shaders/ui-batch.shader`.

- **One scale helper** (`UiCanvas`): the 2048×1536 canvas scales uniformly to fit
  the framebuffer times the user UI scale; each element keeps its authored
  distance to an anchor edge, so corner HUD parts stay in corners on wide
  screens (4:3 reproduces the authored layout exactly). BF4 fonts come in SMALL,
  MED and BIG versions; the tier is chosen by scale (< 0.36, < 0.6, larger) and
  text is drawn at an integer scale (2 on HiDPI and from 2560×1440). Both are
  inferences from the shipped files. Above 1× each glyph is magnified with
  Catmull-Rom and its edge tightened with a contrast curve instead of doubling
  pixels, which turns the fonts' anti-aliased edges into staircases (`FontSmoothing`,
  `[EXT:hidpi-glyphs]`, a design decision: the original only drew its fonts at 1×); at 1× the original coverage is drawn unchanged.
- **Rendering**: `UiBatch` (CPU triangle list) → `UiRenderer` (one alpha-blended
  pipeline; images linear filtered with the pink key bled to neighbour colours,
  font atlases point sampled at one texel per pixel after smoothing). Model frames are stretched into element rects.
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
  SPINRADIUS/VERTICALOFFSET and glides between islands; the backdrop is painted with the
  cloud layer's eased colour as a stand-in (the original clears to the fog colour 0x44DDFF
  and tints only its clouds), and rain and lightning come from the weather renderer.
  Flying meshes (`LobbyFlyingMeshes`, tp.exe `0x5DA200`): every FLYINGMESH line spawns its
  instances over the first frames (one more per draw until a draw ends in 1 of 8), each
  flies at its speed toward a random point of its box (centred 50 above the island's camera
  target), turning 0.1 × step of the way per frame and picking a new point within √500 of
  it, and plays its `<mesh>m1` wing cycle in a loop; all draws come from the lobby generator.
  Island clips (tp.exe `0x5E11D0`): every island model whose clip has under 1 ms left plays
  its `M1` or `M2` at random (C `rand() & 1`, a local stand-in stream), once.
  Enter sequence (tp.exe `0x5e0490` states 1 and 2, `LobbyCameraMode.BeginEnter`): choosing a
  start in an island's menu hides the lobby UI, turns the camera to the island's angle + π at 0.05 × step a
  frame the short way round, plays the gate's `M1` clip, then zooms in (radius × (1 − 0.07 × step), height
  × (1 − 0.6 × step)) under a black fade that eases an eighth of the way a frame to the closed share of
  SPINRADIUS − 8; below radius 8 the queued park starts (`0x5e1e50`). Not rendered: the cloud layer
  (coloured from SKYCOLOUR), the fog and the key-award animation; the early-leave `M2` gate close
  plays only when the enter is abandoned (the park it was to start was refused: the orbit resumes,
  the fade eases out and the menu returns); the browser's level copy starts when the sequence
  does and the park waits for both.
- Menu: the `islandlobby` panel + `f_lobbutbg` and the globe (`b_worldview`, 346; opens the online
  world), enter (`b_entpark`, 345), previous and next (`b_lobleft`, `b_lobright`, 343, 344) buttons at the
  rectangles of the original lobby table (tp.exe `0x757f60`, generated into `UI/Original/OriginalLayoutTables.cs`
  by `tools/ppc-analysis/lanes/ui/tpw_exe_layouts.py`), and the island name (THEMENAMES, large yellow) in the
  field (477,1282)-(1571,1457) of the companion table `0x757d88`. The original lobby has no logo, no title and no
  right-hand menu column (UI-014 retired): Escape opens its menu, a frameless list of cyan text lines (Go Online,
  Resume Game, Options, Quit Game as in the original's lobby menu, plus Load Park, `[EXT:lobby-load]`; there are no
  player profiles, so no Select New Player). Choosing Go Online, Load or Options closes the menu before opening
  its screen.
- Enter park → Game Mode window (the original's name dialog `0x753f50`: 239; Instant Action 240 / Full Simulation 241,
  tooltips 358/359): the two toggles latch in one group with none chosen at first, `e_joy` shows while Instant Action is
  chosen, and the tick starts a new park on the original level of the island, in Instant Action only when that toggle is
  chosen (`0x4a6d00`).
  Instant Action adds the `Easy_` balance layer and the shipped `Easymode.TPWI`
  seed where the level has them and gates tickets, challenges, loans, the
  research lab and upgrades; Full Simulation starts with the standard balance
  and no seed (traced in the Mac binary, see reverse/FINDINGS.md and
  docs/ECONOMY.md, "Game modes"). `--load-original-level` and the Load Park
  entry keep the read-only reference start (shipped save, Full Simulation rules)
  whatever mode was chosen before (UI-041).
  The original fixes the mode per player and resumes each theme's autosave; OpenTPW has no player
  profiles, so every entry starts a new park (`[EXT:no-player-profiles]`, a design decision; the original's
  GameType is derived from flag bits 0x2000000/0x1000000 of 0x7a1a8c, tp.exe `0x550ca0`). The enter-park sound
  (`cat_globallobbysfx` 4) plays when the park starts, and the screen-space particle effect 0x62 starts over the key panel with it (`LobbyEnterEffect`, docs/PARTICLES.md), except in Instant Action (game type 2), which skips both.
- Load Park (202): own saved parks, original Easymode parks (read-only) and the
  OpenTPW sandbox save. The shared lobby/pause chooser is tp.exe's load dialog (table `0x7523f0`, built by
  `0x49efb0`): `window2`, the title (202), the `f_load` list whose rows have the buy list's slot-7 pitch, so twelve show
  (`0x49f0b0`, `0x663718`), its scroller and arrows only while the rows do not fit (`0x664495`) and `b_exit`; the rows
  show the park's name and, right-aligned, the save file's local date and time (`0x49ec80`, UITEXT `0x1c0`; UI-013). The wheel scrolls; Left/Right selects and keeps that row
  visible. A first click selects, Enter, a second click or the localized Load Park button activates that exact
  entry; Back closes without loading. Reopening starts with fresh entries and selection. The Load Park text button,
  at the place of the name field the original hides when loading, is OpenTPW's (`[EXT:load-chooser]`).
  `LoadChooserScrollTests` and `LoadDialogTableTests` cover actual chooser and HUD pause input, beyond-twelve
  selection, 0/1/8/9/13/17 boundaries, entry metadata, cancellation, fresh state and the table layout.
  Quit asks with the original confirmation (9).
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
| 3D card rendering (`f_optpanel`, 120008) | `b_on2`, disabled | fixed: OpenTPW always renders on the GPU (`[EXT:options-fixed-rows]`: the PC lets players change it, disabling it is OpenTPW scope) |
| Videocard: Primary (`f_optpanel`, 120010) | `b_on2`, disabled | fixed (`[EXT:options-fixed-rows]`) |
| Screen resolution (`f_optpanel3`, 120021) | slider over `IDisplaySettings.GetResolutions` | pending display size; OK applies it with the keep-or-revert flow |
| Graphics quality (120022) | slider over `IGraphicsSettings.Presets` | Low/Medium/High, then Enhanced (OpenTPW extension, `[EXT:COMPAT-GFX-ENHANCED]`, last step), Custom only while current; applied on OK, restart notice when `RestartRequired` |
| Audio quality (120032) | slider, disabled at 100 % | fixed (`[EXT:options-fixed-rows]`): OpenTPW has no audio quality setting |
| Sound effects / Music / Speech / Movie volume (`f_optpanel2`, 120024-120030) | slider 0..100 (percent, shown as " %d %%"; defaults 75/60/75/100; the 1 % step is the original slider's granularity, tp.exe `0x66b47d`) + `b_on` mute toggle | `GameOptions` volume and `*On`; `GameOptions.*Gain` is 0 while off |
| Advisor, Tutorial, Popup help, Confirmations, RMB cancel (`f_optpanel`, 120011-120015) | `b_on` toggle | `GameOptions` bools |
| Rotation (Smooth / 90 degs), Scroll (Pushscroll / Right button) (120016, 120017) | `b_on2` cycle button | `GameOptions.Rotation`, `Scroll` |
| OK (`b_okay`), Cancel (`b_exit`) on `!f_plain` (120031) | buttons | OK applies, Cancel/Escape/right click discard |

`b_on` draws its normal and highlight frames as ON (upper indicator lit) and the down frame
as OFF, so a toggle is a `UiButton` with `Selected = () => !isOn`. `UiSlider` draws the
`b_scroller` ball on the track; clicking or dragging inside the hit region sets the value
from the mouse x, the wheel and Left/Right step it (UI-036).

Wired today: every row has its effect — volumes and mute toggles set the audio
mixer's channel gains, Advisor and Tutorial gate the automatic advisor, popup help
gates the hover help, Confirmations gates the delete/dismiss dialogs, RMB cancel
gates the tool right-click cancel, Rotation and Scroll feed the park camera, and
movie volume its mute toggle the start-up movies; screen resolution/size and
graphics preset apply on OK. Options files from older versions get the
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
x 1331..1964, rows every 156 units from y 167, the authored right column's panel size and pitch),
each with a dark label in the shared size and a `b_on2` cycle button. The left column holds the
four display rows, window mode (windowed / borderless / exclusive "Full screen"), upscaling
(Native/Linear/Nearest), render scale (Native, presets 77/67/59/50 %, others labelled with the
original " Custom") and the frame rate limit (the original " Off", 30 or 60 fps; `FrameLimiter`
caps drawing only, the simulation keeps its fixed clock); the right column interface scale
(Automatic or 1x-8x), enhanced textures (see below), language and the enhanced interface. Clicking the button or wheel up/Right cycles
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

A park chosen in the front end, loaded or visited is built over several frames while the lobby (or
the park being left) keeps drawing and playing, and input waits (`[EXT:park-build-over-frames]`,
BROWSER-PERFORMANCE.md). The same kind of line shows "Building the park..." (in all six languages)
with the construction's progress.

OK saves `save/opentpw-options.json`. Size/window-mode changes use
`ApplyWithConfirmation` (15 s) with the original UITEXT 400 question showing the display's
countdown - Yes -> `Confirm`, No -> `Revert`, and on No or the timeout the original 401
message; upscaling, render scale, UI scale and the frame cap use `Apply`. A language change (and a
graphics change that needs a restart) shows UITEXT 402 (RESTART GAME). `StubDisplaySettings`
is only for tests/headless use. Audio code reads volumes via `GameOptions.Current`
(`MovieGain` etc.).

### Local art overrides

`UiArtOverrides` (`[EXT:art-override]`) lets a player replace an original UI element with their own
sharper art: a PNG at `<config>/art/<name>.png` (on macOS `~/Library/Application Support/OpenTPW/art`).
Nothing uses it now that the lobby has no logo (the original lobby has none, UI-014); the lobby logo used `tpw_logo.png` is drawn, aspect-fitted, in the `tpwlogo` model's box
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
frame rate limit, language, back, date format, read-only/save/build messages, "not simulated yet")
are in `UI/Original/SupplementaryStrings.cs`: repository-owned, clearly OpenTPW
additions, translated for English, Danish, Dutch, French, German and Swedish
(native-language names for the language row). They render with the original BF4
fonts; tests check that every used original and supplementary string has a
glyph in all 14 UI fonts of each language.

### In-game HUD

- Placed originals at their authored places (anchored bottom-left):
  `mainpanel`, `gauge` (tooltip 477 "Happiness of the park visitors"), `date` display with the date (478), the
  bank balance (465; the bare absolute balance as the original's `swprintf "%d"` shows it, without grouping or prefix, in a colour that steps from red to white with the balance (table `0x752e30`); `i_dollar` carries the sign), the `f_tagl/m/r`
  message tube with its slide-out panel (UI-025, `Hud/ParkHud.MessageTube.cs`: pending messages stack as icons in a red tube left of the panel, a left click opens one in the panel drawn from `messagel/m/r` at the rectangles of table `0x7501a0`, a right click deletes it; the panel's `mb_erase` button and up to two action buttons with the `mb_locate`, `mb_callmech`, `mb_resrch`, `mb_buy`, `mb_accept` or `mb_decline` art of their action's kind, of which the challenge offers use `mb_accept` and `mb_decline`; OpenTPW's own feedback lines are a separate status line above the help box), and the object window (below).
- Panel buttons at the original rectangles of the Mac main HUD table (`data:0x4ab38`, controls 38-43),
  staggered along the panel's curve: `b_buy` (469), `b_info` (470), `b_money` (471), `b_resrch` (472),
  `b_map` (473) and `b_camera` (474). Buy, Info, Finance, Research and Map open their windows; the
  camera button (474) slides out the panel of table `0x751720` with the camcorder (`b_1person`, 479) and postcard (`b_postcard`, 480) buttons and a third `b_camera` button that cycles the host Standard/Close/Overview views (`[EXT:camera-view-cycle]`; UI-042): the arm drawer `0x4a14f0` moves it in ten 20 ms steps from behind the HUD with the drawer sounds (`cat_ui` 0x1C, and 0xC2 when it is back in), its buttons show once they pass the arm's left edge (`0x4a1440`), and it posts advisor help 0x131 (305) while it is out (`0x498bb0`/`0x498bd0`). Buy is disabled in a read-only
  visit; the others stay available (their windows are read-only views there).
- The date text is drawn in black inside the date region (182,1061)-(383,1103) (table control 32), in the
  date font slot 3 (`DATETINY/SMALL/MED` by tier; only official Patch 2 and the Mac version ship these, the
  1999 data falls back to the Small font, which is the same file as `DATESMALL`). The bank
  balance is not on the panel: the table puts it at the top left of the screen (control 47,
  (258,60)-(720,260), white cash font slot 1, drawn with the shipped `CASHSMALL/MED/BIG` fonts; the original then resizes the box to the width of "999999999" and the text height + 16, centred on `i_dollar`, `0x4a2082`, which the HUD does every frame). Beside it the same table
  places the `i_dollar` icon (50), the change text (48, yellow, right-aligned under the balance) with the `cashtrend`
  model (root `green_up`, 49), always shown, whose frame is 1 (red, down) when the last closed month spent more than it took (`0x4a0e30`); the yellow text under the balance (control 48) is the cost of the build action under way (held blueprint price, path or queue line so far, coaster segment), hidden outside the build tools (UI-047), and at the top right the golden
  ticket (`gtick`, 53) and key (`gkey`, 52) icons with their counts (55, 54). The ticket count is the park economy's unspent
  golden tickets (UI-051); each counter shows only while its count is not zero, as the original's `"%d  x"` (`0x4a0fb0`), and
  the keys are the player's: the attached player ticket ledger's earned tickets / 3 plus its extra keys (`0x5af680`), the park's
  own earned tickets / 3 while no ledger is attached, and none in Instant Action, where the original skips the key row.
- The Mac build has 55 such layout tables, including Buy (`data:0x4cd94`), Financial Information
  (`data:0x49ee4`), Available/Outstanding Loans (`data:0x4a79c`), the money arm (`data:0x496c4`),
  Research (`data:0x4c652`), the staff list (`data:0x4a1e0`), the map screen (`data:0x5265e`),
  coaster tools and ride windows. The named
  screens were identified by matching table drawing keys to `ui.wad` model root names.
- Financial Information (`ParkHud.FinanceWindow.cs`) places `w_park`, `f_graph`, eight `b_ltoggle`
  series buttons, three `b_stoggle` period buttons and the right-side controls at their Mac table
  rectangles. The series use `ParkLedger` closed months plus its current month: bank balance,
  park value, money in, gate/shop/sideshow takings, money out and staff costs. Money in includes
  loan proceeds, and money out includes repayments. Bank balance and park value are exclusive
  with each other and with the six cash-flow series, as in the original control handler.
  The 1/3/12-year controls limit the displayed
  months. The graph's line style, colours, scale and initial selection are OpenTPW choices
  (UI-060). The Loans button opens the separate bank window when loans are available; the entry
  and staff-cost detail buttons open their management screens (`ParkHud.Management.cs`), disabled
  while no park economy runs. The training-budget window's minus and plus buttons move a budget by the
  original spinner's step of 25 within 0..10000, and each row's gauge shows the role's employees' mean
  grade as a share of grade 5, one above the best grade (0..4), so grade-4 staff fill 80% (tp.exe
  `0x4b2797`, `0x4c9c00`, scaled at `0x4b28eb`); the entry-price window is a host layout (`[EXT:entry-price-window]`).
- The Loans window (`ParkHud.LoansWindow.cs`) is tp.exe's table `0x7525a0` (`window4`/`f_loans`), six columns,
  Available/Outstanding tabs (the tab chosen last stays for the session, `0x4a06b0`), a scroll control and the four summary cells. Offers and outstanding
  accounts come directly from `ParkEconomy`. Borrowing and repayment use its existing methods,
  only where the game mode permits loans. Lender names come from the selected language's
  `LOANNAMES.str`; a missing entry uses a numbered fallback, which the shipped data never reaches (`LOANNAMES.str` has 18 entries and every `Lendername` is 0..17). The list rows have the buy list's
  pitch (the same slot-7 row factory, `0x4a02b0`; `EXT:list-row-pitch`), so seven whole rows fit (`0x663718`); every opening sorts by lender name
  (a new list sorts by column 0 ascending, `0x662679`; text by `wcscmp`, equal keys in slot order, `0x66403b`), the top row is chosen,
  and the scroller with its arrows shows only while the rows do not fit (`0x664495`). The summary is the original's (`0x49fb30`), filled when the
  window opens, top to bottom: the last closed month's money in, the monthly repayments of the loans now outstanding,
  its money out less those repayments, and money in less money out.
- Research (`ParkHud.ResearchWindow.cs`) places `w_res`, five `res_panel` rows and effort sliders
  in the table's visual order: rides, upgrades, shops, sideshows, features. Item names, progress
  and effort come from `ParkResearch`; changing effort calls `ParkEconomy.SetResearchEffort` only
  in modes that support the research panel; Instant Action shows its original unavailable message.
  The main workload knob is the original's slider (`0x4a96b0`, `0x4aac80`): it sets a workload of
  70 + 30 × value / limit, the limit growing with the researchers' summed ability (only part of the
  track is reachable with few researchers), and the keys move the workload one point within 70..100 (UI-064). The five effort sliders are the original's generic slider with
  SetRange(0,100) (`0x4aa6bd`): a drag position is truncated to its value, not rounded (`0x66afc9`). Each
  row's fluid (`res_fluid`, 64 frames) animates as the original's (`ResearchFluid`, `0x4a95b0` / `0x4a9fc0`): a
  category with a project and a non-zero rate (its effort share times the staffing percentage, `0x503980`) fills
  from empty and then loops its bubbling frames at (rate + 20) / 100 × 2 frames a tick, and drains back to the
  empty frame when the rate falls to zero; there is no progress fill. Over each fluid the row draws its tube (`res_pipe`, root `pipe`, orange end caps, the child of the category control in the table at file offset `0x35299c`). The fluids step at the 60 Hz reference rate
  (`[EXT:frame-driven-at-60hz]`; `APPROX:UI-066` is retired). The item-navigation
  arrows move the category's current target through `ParkResearch.MoveCurrent`
  when more than one item is selectable (tested in `ManagementUiTests`).
- Buy window (`ParkHud.BuyWindow.cs`, Mac buy table `data:0x4cd94`): the Buy button opens the original window as a modal
  screen over the park (it dims the park like other windows and does not pause the game). Every element sits at its authored
  rectangle (`Anchor.Center`): `w_big` (root `window4`) with two `!frame` panels (the turning preview, and the stats
  frame), the `f_buyitem` list frame, title text (UITEXT 119-122 per category), the item name above the preview, the
  four category buttons `b_sride/b_sshow/b_sshop/b_sfeature` (help 140-143; the chosen one is drawn in its down frame),
  `b_up`/`b_down` and the `b_scroller` ball on the `!slider` track (shown only while the list has more items than rows, tp.exe
  `0x664495`; the ball's top runs from the track's top to a ball above its bottom in whole units, `0x66b088`), and `b_exit` (help 2, also Escape and right click).
  `b_allstaff` (help 153) opens the staff window. The list has the three table columns: name,
  price (the economy catalogue price) and a third column holding a tick box, ticked when the park owns one (UI-050). Click a row to select it
  and show its preview (the original `P<name>.MD2` or the main model, CPU projection with the original's 45° tilt, 0.4 rad/s turn and fit, UI-026), name and stats; click the selected row again,
  press Enter or double-click it to buy: the window closes and placement starts (`Level.PlaceObject` checks the footprint and
  charges through `ParkEconomy.TryBuild` exactly once). The table has no OK button, so that is OpenTPW's choice. Unresearched
  items are greyed and refused with the "not available" message. The wheel scrolls the list anywhere over the window; Up/Down
  move the selection. The Name and Price header buttons sort (the original's header sort `0x665a44`; the sort is kept for the session, `0x4ac9dd`). Control 512 beside the title shows the cash balance as UITEXT `0x1ca` "Cash $ N" (`0x1cb` with the magnitude when negative, tp.exe `0x4acdae`), like the hire window's control 9366. The stats frame rows are the table's five label/value rows:
  rides show Number owned (UITEXT 125), Excitement (126), Safe capacity (127), Reliability (128) and Working life (129), whose gauge
  keeps the 100 of 1024 it gets when the window is built (tp.exe `0x4abae0`);
  shops 130, sideshows 131/132 and features 133 show fewer rows (UI-046). Items are the buildable objects of the theme
  by `Info.WhichUIType`, ordered by name as the original list inserts its rows (ordinal, `0x10178768`). Items without a `P<name>.MD2` preview model (shops, among others) show their
  main model, as the original's preview does (`0x10059F00`). Tools, fixed items and standalone upgrades are excluded (`ObjectCatalog.Buildable`); items absent
  from the economy catalogue stay unavailable in original levels.
- Per-visitor window (`ParkHud.VisitorInfoWindow.cs`, table `0x7575e0`, opener `0x4b79b0`, handler `0x4b7720`, refresh `0x4b72a0`): the window1 frame with the title "Visitor #n"
  (UITEXT 455; 456 "New Visitor" without a number), the portrait frame `0x3722` (empty, UI-910), the stats frame `0x3723` with the labels UITEXT 70 to 77 and the values cash
  (`+0x1a0`), time in park (the list's hours), rides ridden (`+0x1c4`), purchases made (`+0x1c8`), sideshows played (`+0x1cc`), sideshows won (`+0x1d0`), the happiness bar
  (`happygrad.wct`, inset 10) and the current status (`KIDSTATES.str` by the state word `+0x220`; the "Using: " record takes the attraction's name), the thought frame `0x3734`
  with the thought text (`THOUGHTS.str`) and the icon `0x3735` (the thought less one), the cycle arrows (`b_arup` previous, `b_ardown` next; help 98, 97), `b_locate` (the visitor locator on
  the visitor, help 99) and `b_kids` (the All Visitors window on the visitor, help 100). It opens from a left click on a row of the All Visitors list (message `0x400`, `0x49351e`) and from a press on
  a visitor in the park (cursor mode 7, `0x4879d0`), closing the window that is open; it plays the sound `0x81` and posts help `0x12e` as it opens, refreshes every 4000 ms (timer `0x80080`) and
  closes when its visitor is gone. The use counters are the guest's simulation state (`Guest.RidesTaken`, `ShopsVisited`, `SideshowsVisited`, `SideshowsWon`; a finished visit counts by the type's `WhichUIType` at `0x4fd9b1`, so a shop or a sideshow is no ride), saved as optional fields and imported from original saves; the list's rides column shows the rides counter too.
- Staff window (`ParkHud.Staff.cs`, Mac table `data:0x4a1e0`): `b_allstaff` opens the `w_big` window over the buy window.
  The authored `f_staffpic`, `f_staffinfo`, `f_balance` and `hirestaff` frames initially show applicants from
  `ParkEconomy.Staff.Candidates`. The five role buttons filter applicants (help 154–158), the name and wage headers sort
  them (help 159/160; the sort is kept for the session), hovering a name selects it (the details follow the pointer, as in the video; the employee view, an extension, only highlights) and a left click on a name hires the
  applicant through `ParkEconomy.Hire` (help 161; list message 0x400 → `0x49b988`, which here starts the Place tool;
  Enter hires too, `[EXT:staff-enter-hire]`). The video never shows a click on a name; the original's own advisor line ("click on the name of the person you want to hire", 11:54) and the list's 0x400 message (`0x6655a2`/`0x6656fa`) settle the trigger, so UI-083 is retired. The list scrolls and takes the buy list's row pitch (`APPROX:UI-080` retired: the hire list's row factory `0x49bbb0` is the buy list's `0x4ad240` formula on font slot 7, so it shows 11 rows); the details are a Vaardigheid gauge (UI-081) and a mini balance of the last closed month, taken when the window opens, that includes the selected candidate (hovering a name selects it, a left click hires; tp.exe `0x49c53d` reads the newest entry of the monthly sample rings), the portrait box shows the selected role's sprite-bank figure walking on the spot, facing direction 5, as the original's figure script `0x74f758` does (its drawn size is UI-082).
  Control 9366 is the plain cash label ("Cash $ N", UITEXT `0x1ca`/`0x1cb`). A labelled extension button (`[EXT:staff-employee-view]`) toggles to employed staff, showing tracked state, wage, skill and happiness (the original's employee list is the separate All Staff window, S key). As in the All Staff list (row message 0x400 → `0x4b6080`), a left click on an employee (or Enter) opens the per-employee window (`ParkHud.EmployeeWindow.cs`, table `0x756f48`) in place of the staff window: the employee's name as the title, the stats frame (employed for "N days", monthly wage, skill, happiness and energy bars, current status with its `staffstat` icon, refreshed every 4 s by timer `0x80080`), `b_fire` (dismiss; with the Confirmations option `[0x78d90f]` the window closes and the yes/no box asks first), `b_pick` (pick up), the `b_route` patrol toggle (help 86/87: set an area with the patrol tool, or clear it), the cycle arrows over all staff (hidden with fewer than two employees), `b_allstaff` (back to the employee list on the role), `b_locate` (the staff locator panel on the employee) and the tick (UI-092 for its remaining gaps).
  `b_allthings` returns to Buy (help 162). The
  nearby `data:0x4e45c` staff and visitor locate buttons use their authored positions; each is
  enabled while a live actor of its role (or any visible visitor) can be focused, and the click
  focuses the host park camera on that actor's world position (UI-091). The keyboard locators
  Ctrl+S and Ctrl+V open the original's locator panels in the HUD arm (`ParkHud.LocatorPanel.cs`, tables `0x756950` and
  `0x756ad8`; `0x4b3f60`, `0x4b4280`): the staff panel's five role radios and the visitor panel's radio locate the role's
  first actor, the chosen radio again steps to the next one, and the strip above shows the actor's name with the staff
  state icon or the visitor's thought while the camera follows the actor until the player moves it (`0x4b3790`,
  `0x4b3490`, `0x4b3640`); the info arm, a message, the camera panel and the arm's red X put the panel away. Ctrl+V with
  the per-visitor window open (a row of the All Visitors window clicked, or a visitor picked in the park) locates that visitor at
  once, as the original does with its visitor info window (`0x4817c0`), and Ctrl+S or `b_locate` with the per-employee
  window open locates its employee (`0x481790`, `0x4b5f07`); without an actor the keys keep the strip with its
  last name, as the original does (UI-091).
- The All Attractions and All Visitors windows' lists (attribute 0x291) follow the original's list control: the pointer
  highlights the hovered row (the original selects it), a left click selects it and opens the row's info panel (the info arm for an attraction, which keeps the list; for a visitor the
  per-visitor window `0x4b79b0`, below, which gives the list way) and a right click centres the camera on the row and closes the window
  (list messages 0x400 and 0x402, tp.exe `0x6655a2`/`0x66563d`; button 0 is the primary, left button: DirectInput button 0 becomes code 0xf0, `0x634963`, `0x486ca1`).
  Enter and the select/go-to buttons keep the older select-and-close behaviour (`[EXT:overview-select-button]`).
  A release is a click only within 500 ms of its press with at most 6 px of movement on each axis (`0x65f6d1`,
  `0x65fa33`; `UiInputSource`); a longer press or a drag is no click. Each list keeps its last sort for the session
  (`ListSortMemory`, the original's global sort words, `0x6659af`).
- Map window (`ParkHud.Map.cs`, Mac table `data:0x5265e`): the HUD `b_map` button opens `map_win` with its table-positioned
  pan, zoom, object-category, layer and close controls. The shipped per-theme `levels/<theme>/2dmap.tga` supplies the
  terrain image. Current path and queue cells and placed-object footprints are drawn above it. Each category button toggles its own layer (independent bits, each category drawn from its own bit; only the rides layer is on when the map first opens, the original's layer word 1, tp.exe `0x5f06a9` and the map table `0x774da0`; paths, queues and unowned land use the original's records, UI-086 keeps the object colours); zoom steps half a unit within 1.0–3.0 and a pan moves the centre one map cell, independent of zoom. Live cells use the original's grid on the 512-pixel image: 15 + column × 5 and 42 + row × 5 pixels for a 96 × 85 park (tp.exe `0x5f2380`), one pixel left of and above the art's own terrain cells. The generic sandbox draws its grid
  and heights in place of theme art (`[EXT:sandbox-map]`, OpenTPW content); a missing original theme image is reported. Track, visitor, staff and metric
  layer buttons draw live coaster cells, actor positions and observations (provisional host overlays). Escape, right click and `b_okay` close the map.
  The map window pauses the park while it is open, as the original's `0x5f0b40` calls the game pause (`0x4092a0`; UI-096).
- Object window for the selected original object (picked by occupied grid cell): the window of the original ride, shop or
  sideshow table (tp.exe `0x755150`, `0x755908`, `0x755e80`; UI-024): `w_med` frame (248,30)-(1800,1007), the name in
  yellow, a preview, the stats frame with white labels and values (Users last month, Age, and the four gauge rows
  Excitement, Reliability, State of repair and Remaining life, Scrap value), the round buttons open/close (`b_door`),
  delete (`b_erase`), call mechanic (`b_callmech`), upgrades (`b_upgrade`), Ride it! (`b_ride it!`, rides that can be ridden), the
  arrows (`b_arup`, `b_ardown`) that step through the objects of the type, the list button (`b_allthings`, All Attractions) and the tick that closes the window.
  The gauge rows are the original's: UI-028 retired. The window refresh `0x4ade40` sets each gauge to `(byte << 10) / 100` of 1024 with the window's own slider
  values, so a drag moves them before the window applies it: excitement is `0x4e0560` (a ride's data level, or its track controller's base, times the speed and
  duration ratios clamped to 0.75..1.25 and truncated in extended precision; a sideshow's `20 + win × sqrt(prize margin) × 0.08`; a coaster's `50 + ½ excitement`),
  reliability is `100 - trunc(9 × wear)` of `0x4df640` at the window's speed and capacity, and repair and remaining life are the truncated floats at `+0x44` and
  `+0x48`; the shop and sideshow windows' local happiness gauge is the mean happiness byte of the guests on the 3 x 3 cells around the object (`0x4cd880`).
  The queue (`b_queue`), track (`b_track`) and apply-all (`b_applyall`) buttons start the queue tool, the coaster or water track tool, and copy the window's settings to every object of the type (`0x4b0ae0`, `0x4b20f0`).
  **The Move tool** (`b_move`, controls `0x3e2c`, `0xc080` and `0xa0a8`; UI-104 retired; `Level.BeginMove`, `ParkHud.MoveObject`). The ride window's refresh (`0x4ad4e0`) enables the button
  with `0x52c2b0`: always, except for a kart or water ride (type track word `+0x9c` 1 or 2), whose two tiles beside the station (the thing's cell plus the exit offset (4, 1), (1, -5),
  (-5, -2) or (-2, 4), and the entry offset (-2, 1), (1, 1), (1, -2) or (-2, -2) by the angle) must each hold exactly one arm (`0x522790`: the set bits of the cell's connection byte, `Level.MoveEnabled`);
  the shop and sideshow windows' button has no test. The click (`0x48cfa0`) is the erase button's (`0x48cd10`) without its confirmation: the thing is picked up (`0x48c940`, `0x5276d0`), a type
  with a footprint saves its queue corners (`0x530120`, `0x52fc30`), interface mode `0x33` and the click handler `0x524960` run the very erase `0x527ee0` that the demolition runs (last argument 0), so the
  scrap credit, the queue and the track clearing are the demolition's; then the tool `0x46c5a0(0x3b, type)` is built, the mode `0x3b` set and the angle `0x52f1b0(+0x10)` kept, and the window closes.
  The saved corner words `0x81b14c` and the thing `0x81b3ac` are written and read nowhere: a move keeps only the type and its angle, the new object starts with no history, queue, track or settings
  of the old one, and cancelling the tool leaves the object gone. Mode `0x3b` places like mode 4 (`0x5253d5` after `0x524b35`: `0x528a70` with the type 4 and the angle, the same follow-up into the
  entrance, exit and queue or track sub-modes, the same Ctrl test); the only price it skips is the cells the placement lays (`0x8186d4`: the path and queue cell creators `0x5346d0` and `0x538fc0` charge
  only with the flag clear, and mode 4 sets it as well). The ride itself is paid by the constructor (`0x4db4e2..0x4db4f6`, the type's `+0x1b8`), so a move costs the whole price again less the credit
  of the pick-up; the validator's cash gate (`0x535927`) applies, and the buy list's availability and research do not (`ParkEconomy.CheckBuild( moving )`). The ghost, pointer and Ctrl rule are the build tool's.
  The settings controls are at their table rectangles: the ride window's speed, capacity and duration sliders with their labels ("Speed : 60"; the capacity slider only when `UsageInfo.MinCapacity` and `MaxCapacity` differ, the duration slider only with an `Info.DurationUnit`, its label Duration, Laps, Cycles or Repetitions) and the red line of the speed and capacity sliders; the shop window's quality slider (three steps, the lamps `l_low`/`l_med`/`l_high`), the ingredient slider (0–100, only for `UsageInfo.SpecialIngredient` 1–4, labelled from `INGREDIENT.str`) and the price box; the sideshow window's chance-of-winning slider and prize box (hidden for `UsageInfo.NonWinnableSideshow`, whose price is the price of entry) and price box. The controls edit the window's values, which the window applies when it closes or steps to another object (tp.exe `0x48d200`): the ride's SetSpeed, SetCapacity and SetDuration (`0x4af440`), the shop's ingredient amount, quality (50 × the step) and price (`0x4b0aa0`), the sideshow's price, win chance and prize (`0x4b20b0`). `b_move` is not drawn. Escape, and the right click with RMBCancelOn, close the window. Open/close is disabled for non-attractions; fixed items
  cannot be deleted. Unknown statistics remain labelled as not simulated. The shop rows are labelled UITEXT 32–38 and the sideshow rows 41–48 (`0x4afc10`, `0x4b1040`); the sideshow's second row is the winners gauge (`0x4e1f20`).
  The developer prototype's repair gauge follows its linked economy instance,
  including wear and mechanic repair, in both the info arm and the attractions
  overview. Without a linked economy record, the bridge retains its last gauge.
  Read-only visits disable build, open/close, delete and Save; callbacks also
  reject direct mutation attempts using the existing localized online message.
- Speed control (OpenTPW addition, bottom-right): pause, ×1, ×2, ×4 set the
  economy's `GameSpeed` (its clock runs Speed ticks per fixed tick); pause also
  stops rides and guests (`Level.SimulationTimeScale`).
- Happiness gauge: the `gauge` model is only the empty dish (one `thoughts2`-textured
  quad); the HUD samples the in-park visitors' mean happiness every two seconds (each visitor's
  happiness truncated to a byte, tp.exe `0x4c7bb0`), turns it into the original's 0..1024 gauge value
  (`ftol(mean × 1024 × 0.01)`, so a mean of 100 gives 1023; `0x4a1cd0`) and draws it as a rising red→yellow→green
  fill in the dish, empty while no visitor is in the park or the park is closed (UI-901). Sampled in
  `ParkHud.SampleHappiness`, drawn by the `gaugeFill` element; pure mapping in
  `ParkHud.GaugeFill`/`GaugeColour` (tested in `HudScreenShortcutTests`).
- Original keyboard shortcuts (`ParkHud.ScreenShortcuts.cs`): the game's key table
  (the `InputButton` defaults; the research and map tooltips themselves say
  "(R)" and "(Space)") opens the HUD windows: B buy, I park info, F finance,
  R research, Space map, L loans, T staff training budgets, G entry price,
  H staff applicants and V all visitors. Space activates no hovered button
  (Enter does, as for a text field) so the map key never co-fires with the panel
  under the pointer. The All Staff key S and All Attractions key A stay
  unhandled: the host camera pans with S and A (WASD), and one press cannot be
  both; the All Attractions window opens through a round button above All Staff
  in the buy window (and one in the staff window) instead, wearing the window's rides tab icon
  `b_srides` with "All Rides" as its popup help: the shipped `b_allthinginfo` art has no texture,
  and a text label does not fit the round slot. A second press of a window key closes the
  window (`[EXT:window-key-toggle]`); dialogs and the pause menu keep the keys. The shortcut letters come from the
  language's `KEYBOARD.str` (21 entries, `Input.ApplyKeyboardStrings`; the Dutch buy key is K and research is O),
  except a plain letter that is a camera-pan key (W, A, S, D), which keeps its default, and in the browser build the Ctrl+W/T/N chords the browser reserves (`[EXT:browser-reserved-chords]`); a plain shortcut never fires while Ctrl is held (Dutch research O vs Ctrl+O) (the Dutch hire letter A, UI-012). The time keys
  (numpad +/−/*) step and reset the four-speed clock model (`[EXT:time-key-speeds]`; the original scales the clock by ×0.8/×1.25 within 0.25–2.0 and * resets to 1.0),
  comma adds 90° to the pending blueprint's angle (one `Level.RotateBuild` step) and full stop adds 270° (three steps; tp.exe `0x40c120`/`0x40c150`),
  holding exactly Ctrl at the placing click keeps the build tool armed (Escape or a right click leaves it) and leaving the tool resets the angle to 0 (`0x5253b8`, `0x52f3b6`; UI-031),
  and Ctrl+S / Ctrl+V open the staff and visitor locator panels (UI-091). The keys wait
  while a menu, dialog, text field or the ride view owns the input; window keys
  keep acting over their own window. Camcorder terrain-walk ingress and postcard ingress use their dedicated full-frame
  actions below. Ctrl+H flips the global Popup help option (options byte +0x36, tp.exe `0x40c5d0` → `0x4817f0` stores `0x78d90e`) for every screen
  and the host persists it; it never activates the H staff window. Both Ctrl keys
  work, held chords toggle once, and input owners consume blocked presses.
  The help box is the fixed rectangle (574,1520-h)-(1474,1520) drawn with `f_helpbg` (tp.exe `0x492180`, UI-010 retired); its height follows the small font.
  Both physical and injected taps accept either Ctrl key. `ParkHudHelpTests`
  checks actual input dispatch and synthetic hover-help draws without original
  assets or a GPU.
  Mapping and gating are pure functions (`ParkHud.ShortcutFor`,
  `WindowShortcutsAllowed`, `SteppedSpeed`), tested in `HudScreenShortcutTests`
  and `OverviewWindowTests`. The native smoke test presses the real keys:
  `Input.InjectKeyDown` queues a physical key for the next frame's button
  rebuild only (`Input.UpdateButtons`), so V and B run through
  `Input.Pressed` and every gate above exactly like a keyboard tap — the
  injection never enters the persistent keyboard state or the WASD pan axes
  (`InputInjectionTests`). Cancellation belongs to the returned scope, so
  disposing a consumed tap cannot cancel a later tap of the same key.
- Pause menu (Escape): a frameless list of cyan text lines (white while focused), each as wide and high as its text in
  the menu font plus the original's margins and stacked from the top (`0x492f60`, UI-013), in the original order: Load (3), Save (4),
  Restart Park (10; only for an Instant Action park of a theme, `[EXT:pause-menu-restart]`), Publish Park (5; the online publish flow), Options
  (6), Go Online (1, `[EXT:pause-menu-go-online]`), Resume Game (7), Exit To Lobby (12), Quit Game (8, confirmation 9). Confirmations are the window of table `0x7583e8` with the tick and X buttons stacked at its right (UI-013). Save writes the owning
  game flow's park save (the own-format world save for sandbox and front-end
  started original-level parks; a read-only visit has no Save entry).
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

### Coaster tool panel

While the coaster tool is active the HUD shows the original's slide-out panel (tp.exe table `0x751530`, click handler `0x4982a0`, docs/COASTERS.md "The tool panel"): the green drawer art
(`panel`) with two `!f_plain` boxes and nine buttons of the `cb_*` models (`cb_track`, `cb_loop`, `cb_move`, `cb_loft`, `cb_swapup`, `cb_rotate`, `cb_incline`, `cb_swapdown`, `cb_dellast`) at the
table's rectangles, offset by the window position measured from the video (`(−372, +232)`, `APPROX`). The selected button shows its pressed frame, a disabled one its grey frame (the enable mask
`0x445910`); the tooltips are help 564, 565, 296, 297, 302, 300, 569, 303 and 304 and a click on build, loop, move or demolish shows help `0x125`, `0x126`/`0x127`, `0x121` or `0x124`. The panel appears
at once (the original slides it out), and the park's pointer handling (press, drag, release on pylons, the blueprint-marker cursor and placement area) is `ParkHud.CoasterPanel.cs`.

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
  without a charge, selected-object open/close, the developer prototype Totem
  listed beside the catalogue rides in All Attractions (status following its
  door, row activation, delete through the prototype removal with the economy
  record gone and the selection healed), the window keys V and B pressed as
  real keyboard taps through `Input.InjectKeyDown`, pause menu, own-format world save
  (verified on disk), lobby;
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

## Control inventory

Every visible control of the original-style HUD, its windows and the front end,
against its handler (October 2026 audit from the code, not the historical matrix).
"Test" names the suite that exercises the action end-to-end or its model effect;
the native front-end smoke test (`FrontEndSmokeTest`) covers the click flows with
the real assets. Approximation tags refer to the register below.

### Main HUD (`Hud/ParkHud.cs`)

| Control | Action | Status | Test |
| --- | --- | --- | --- |
| `b_buy` panel button (469), key B | opens/closes the buy window; disabled in a read-only visit | wired | `FrontEndSmokeTest`, `HudScreenShortcutTests` |
| `b_info` panel button (470), key I | opens/closes the park information window | wired | `FrontEndSmokeTest` |
| `b_money` panel button (471), key F | opens/closes the financial information window | wired | `FinanceWindowTests` |
| `b_resrch` panel button (472), key R | opens/closes research; Instant Action shows its original unavailable message | wired | `ManagementUiTests` |
| `b_map` panel button (473), key Space | opens/closes the map window | wired | `MapViewStateTests` |
| `b_camera` panel button (474) | opens the camera slide-out panel: camcorder (C), postcard (Ctrl-P) and the host view cycle (`[EXT:camera-view-cycle]`, UI-042) | wired (host approx) | `CameraControlTests` |
| speed buttons (pause/×1/×2/×4), numpad +/−/* | set/step/reset the park clock scale (`[EXT:speed-buttons]`, `[EXT:time-key-speeds]`) | wired | `HudScreenShortcutTests` |
| happiness gauge (477) | shows the visitors' mean happiness (UI-901) | wired (host approx) | `HudScreenShortcutTests` |
| date display (478), balance (465), cash change, golden tickets/keys | live economy readouts | wired | `FrontEndSmokeTest` |
| park clicks | select the object of the clicked grid cell; selection opens the object window | wired | `FrontEndSmokeTest` |
| comma / full stop while a blueprint is pending | add 90° / 270° to the build angle (one / three `RotateBuild` steps) | wired | `UiRegisterRound2Tests` |
| Ctrl+S / Ctrl+V | open the staff / visitor locator panels; their radios locate and cycle the role's actors and the camera follows (UI-091) | wired | `LocatorPanelTests` |
| key V | opens/closes the All Visitors window (plain V pans nothing; Ctrl+V stays the locator) | wired | `OverviewWindowTests` |

### Object window (selected object)

| Control | Action | Status | Test |
| --- | --- | --- | --- |
| name and stat rows | live catalogue values; unknown statistics say "not simulated" | wired | `FrontEndSmokeTest` |
| `b_door` open/close (12) | closes/opens the attraction through the reopening guard; disabled for non-attractions and read-only visits | wired | `FrontEndSmokeTest` |
| `b_erase` delete (15) | sells and removes the object (confirmation when enabled); fixed items and read-only visits cannot | wired | `FrontEndSmokeTest` |
| `b_move` (14 / 32 / 44) | the Move tool ("The Move tool"): demolishes the object as `b_erase` does, without the confirmation, and arms the same type at its angle; a kart or water ride needs one arm on each tile beside its station; fixed items and read-only visits cannot | wired | `MoveToolTests`, `SandboxSmokeTest` |
| `b_okay` tick (1) | closes the window | wired | `FrontEndSmokeTest` |
| `b_ride it!` (16) | enters/leaves the first-person ride view of a rideable ride (UI-094/95; the E key is `[EXT:ride-view-key]`) | wired | `FrontEndSmokeTest` |
| `b_arup` / `b_ardown` (21, 20), `b_allthings` (17) | step through the objects of the type; open All Attractions | wired | `OriginalLayoutTablesTests` |

### Ride view (camcorder on a ride)

Source: `World/RideCameraMode.cs`, `World/RideSeat.cs`, `Hud/ParkHud.RideView.cs`, `World/Level.RideView.cs`. Traced in tp.exe (UI-093/094/095):

- **Eye.** The first dummy-attribute record whose type has bit 0x1000 and whose value is 1 (0x44b220 through the ride camera 0x42a560); its node's world translation is the eye, copied exactly. About 58 object models carry one (rides, but also the security cameras, gates and End features, which are not attractions and cannot be ridden here). A coaster uses the camera seat of the car 0x436880 selects (controller +0x124, the train with the smallest position, then train +0x34: car 0, or car 1 behind a front car of another type than the seats'; `CoasterCarRig.CameraSeat`).
- **Orientation.** The per-frame camera update 0x42b1c0 blends the three rotation rows of the seat matrix with the seat's matrix of the previous frame: `a = (1000 - clamp(dt_ms, 1, 1000)) * 0.0005` (about 0.48 at 30 frames a second), `row = node * (1 - a) + previousNode * a`, and stores the node's matrix (not the blend) for the next frame, so it is a one-frame average, not an accumulating ease. The frame time is the millisecond clock difference of two frames.
- **Free look.** The pointer's offset from the view centre (-1..1 over the window, Y up) turns the view: yaw `(x -/+ 0.2) * 3.5` radians outside a dead zone of 0.2, pitch `(0.2 - y) * 1.2` above 0.2 and `-y - 0.4` below -0.4 (rotation about X, then about Y, axis string "XY" at 0x74daa4). There is no easing in the look itself; a pointer at the screen edge turns about 160 degrees. The directions (right and down turn right and down) follow from the rotation code (`0x46f350`, `0x46f650`: standard left-handed X and Y rotations) and agree with the video; the pointer's reference is the full-screen park window (`0x489f50`).
- **Screen.** The viewfinder model at (170,170)-(1878,1366) and the b_eject button at (1916,1404)-(2018,1506) (table 0x74fa98, help 468), the HUD hidden (the ride screen hides the screens below), the crosshair pointer drawn as a yellow plus, and 0.33 s of full-screen TV static on entry and on exit.
- **Exits.** Escape (no modifier), the C key, the eject button, and a right click only when "right click cancels" is on and more than 2 s have passed since the view opened (0x48a7bd). E also leaves ([EXT:ride-view-key]).
- **Leaving.** The park camera's target moves to the cell centre of the placed object under it whose type has `UsageInfo.CannotRide` 0 (`0x42a340`, `0x42a1d0`: the shipped rides, not shops, sideshows or features) and the camera looks along the direction named by the link byte of that cell (`& 0x55`: 1, 4, 16, 64; `0x522850`), which OpenTPW's object cells never carry (UI-095); the camcorder walk restores the exact previous camera.
- **Not original or not traced.** The static and the crosshair are generated ([EXT:ride-static], [EXT:ride-crosshair]: no static art or CCro.tga exists in the data); the field of view is the park camera's 55 where the original's projection is built with 90 (UI-101, UI-094). `--debug-hud "ride:<name>"` and `ride:look:x,y` (Debug builds) board a ride and hold the look pointer for captures.

### Pause menu (Escape)

| Control | Action | Status | Test |
| --- | --- | --- | --- |
| Resume Game | closes every stacked screen | wired | `FrontEndSmokeTest` |
| Restart Park / Publish Park | restarts an Instant Action park after its confirmation / opens the online publish flow; disabled without their host action | wired (host) | `HudScreenShortcutTests` |
| Save | writes the owning game flow's park save (own-format world save); a read-only visit has no Save entry | wired | `FrontEndSmokeTest` |
| Load | opens the Load Park screen; disabled when the host provides none | wired | `HudScreenShortcutTests` (entry enable logic) |
| Options | opens the options screen; disabled when the host provides none | wired | `OriginalUiTests` |
| Go Online | opens the online screens when built | wired | online suite |
| Exit To Lobby / Quit Game (confirmation) | leave the park / quit | wired | `FrontEndSmokeTest` |
| (open menu) | stops the park simulation while the menu or a screen opened from it is open, as the original's pause menu pauses the game (Mac `0x10198514` → `0x10110518`); the speed setting is kept, so the park resumes at the same speed (UI-096 covers the other pausing screens) | wired | `ParkPauseTests`, `FrontEndSmokeTest` |

### Windows

| Window | Controls | Status | Test |
| --- | --- | --- | --- |
| Buy (`ParkHud.BuyWindow.cs`) | list select/sort/scroll, category buttons, purchase via row activation, staff button, all-attractions button, exit/Back | wired | `BuyWindowTests`, `FrontEndSmokeTest` |
| Staff (`ParkHud.Staff.cs`) | role filters, name/wage sort, hire on a left click (hover highlights), employee view (`[EXT:staff-employee-view]`, a click opens the employee window), scroll, locators, all-visitors and all-attractions buttons, exit | wired | `ManagementUiTests`, `MapStaffControlTests`, `FrontEndSmokeTest` |
| Employee (`ParkHud.EmployeeWindow.cs`) | table `0x756f48`: name title, stats frame (4 s refresh), dismiss with confirmation, pick-up, patrol toggle, cycle arrows, all staff, locate (opens the staff locator panel on the employee), tick (UI-092) | wired | `StaffWindowTests`, `FrontEndSmokeTest`, sandbox smoke (pick-up) |
| Attractions (`ParkHud.AttractionsWindow.cs`) | the original tables (window `0x7508e0`, lists `0x750ab8`/`0x750ba0`/`0x750ca0`): category tabs, per-category columns (rides: users, excitement, repair, life; shops: customers, profit last month, total profit, satisfaction; sideshows: customers, excitement, profits, satisfaction; features: number owned) refreshed every 2 s, any column sorts, scroll, the tick, the park information, all-staff and all-visitors buttons, an object window opens its own category with its object (UI-905), a left click opens the info panel and keeps the list, a right click locates and closes, delete (`[EXT:overview-select-button]`) with the original category confirmations; the sandbox developer Totem is listed beside the catalogue rides and its delete goes through the prototype removal | wired | `OverviewWindowTests`, `FrontEndSmokeTest` (list/activation/sell, prototype list/activation/remove with healed selection, visit open + read-only refusal) |
| Visitors (`ParkHud.VisitorWindow.cs`) | table `0x7506c8`: six sortable columns (number, cash, time in park, rides, thought icon, happiness bar; values refreshed every 2 s), 56-unit rows, scroll, a left click selects, a right click locates and closes, park information / all staff / all attractions buttons, tick/Back, V key (UI-907) | wired | `OverviewWindowTests`, `VisitorsWindowTests`, `HudScreenShortcutTests`, `FrontEndSmokeTest` (list/scroll/sort/Enter-locate) |
| Map (`ParkHud.Map.cs`) | pan, zoom, four category filters, track/visitor/staff/metric layer toggles, close | wired | `MapViewStateTests`, `MapStaffControlTests` |
| Finance (`ParkHud.FinanceWindow.cs`) | 1/3/12-year periods, eight series toggles (the bank balance and park value can show together and exclude the six cash-flow series, as tp.exe's `0x49a2e0` masks do; the series colours are its records), loans/staff-cost/entry detail buttons, exit | wired | `FinanceWindowTests` |
| Loans (`ParkHud.LoansWindow.cs`) | Available/Outstanding tabs, six sortable columns, list select/scroll, take/repay with confirmations and balance checks, summary rows, navigation buttons | wired | `FrontEndSmokeTest` (loan flow), model in economy suite |
| Research (`ParkHud.ResearchWindow.cs`) | per-category effort sliders, workload knob (UI-064), item previous/next when selectable, exit | wired | `ManagementUiTests` |
| Entry price / staff costs (`ParkHud.Management.cs`) | ± adjustments, park door toggle (b_door), exit; read-only visits cannot change | wired | `ManagementUiTests` |
| Park information (`ParkHud.InfoWindow.cs`) | live statistics (published statistics in a visit), close | wired | `FrontEndSmokeTest` |

### Front end (`FrontEnd/FrontEndMenu.cs`)

| Control | Action | Status | Test |
| --- | --- | --- | --- |
| previous / enter / next island buttons (343-345) | wrap through the theme islands; enter asks for the game mode | wired | `OriginalUiTests` |
| Game Mode dialog (Instant Action / Full Simulation, 358/359, tick) | the toggles latch; the tick (or Enter, the original's 0x802 from the name field) starts the park in the chosen mode | wired | `OriginalUiTests` |
| Load (202) | lists shipped and own saves and loads them | wired | `OriginalUiTests` |
| Options | opens the options screen | wired | `OriginalUiTests` |
| Go Online | opens the online screens when built | wired | online suite |
| Quit Game (confirmation 9) | quits | wired | `OriginalUiTests` |

Not implemented (no screen exists; their keys stay unhandled): F1 advisor help speech,
player profiles, online world, golden keys. The All Attractions (A) and All
Staff (S) keys stay with the camera's WASD pan by policy; the attractions
window opens from both the buy window and the staff window's visitor corner
(so it stays reachable in a read-only visit, where the buy window is
disabled), the visitors window from the staff window and the V key.

Project rule: every UI value or rule that is not original data or hard evidence is
tagged at its site with `// [APPROX:UI-NNN] … — evidence needed: …` and logged once
at startup (`[APPROX:UI-NNN]` warnings from `UiApproximations.LogOnce`). Original
values carry `// [DATA:<file>:<field>]`, OpenTPW extensions (display/upscaling/
language rows and their supplementary strings) `// [EXT:…]`. Paths are relative to
`source/OpenTPW/`; line numbers are as of this commit.

| Id | Site(s) | Current value / rule | Evidence needed |
| --- | --- | --- | --- |
| UI-003 | `UI/Original/UiModel.cs:60` | button/arm state frames drawn at the root node pose; child translations ignored; the control's model setter 0x65d070 (model at +0x98, model vtable +0xc with 0x57, then +0x14) is found, the node walk that submits the triangles is not | the node walk of the PC UI model draw (the selected state mesh -> local matrix -> ancestor composition), or a capture of a button whose state child is offset |
| UI-004 | `UI/Original/UiModel.cs:124` | UI model triangles drawn back to front by Z, grouped per texture; the original's display object divides its depth buffer into a UI range in front of a linear and a log range (the dump 0x5833c0 prints [+0x48] begin, [+0x4c] ui end / linear begin, [+0x50] linear end / log begin, [+0x54] log end; the buffer itself is enabled or not, strings 0x768988 and 0x768958), which suggests depth-tested rather than sorted UI parts, not shown for the UI model draw; the device init 0x573080 sets ZENABLE 1, ZFUNC LESSEQUAL (4) and ZWRITEENABLE 1 when the display object's flag +0x88 bit 0x10 is set and ZENABLE and ZWRITEENABLE 0 otherwise, so a UI part is depth-tested when the buffer is on, but the UI draw's own state and order are not found | the UI model draw call (which per-draw depth state and which order it submits) and the writer of the four range floats +0x48..+0x54 |
| UI-006 | `UI/Original/UiText.cs:102` | text colours: window titles and the lobby island name yellow, the object windows' labels and values white, the pause menu's lines cyan (white while focused), green values elsewhere, grey disabled, shadow and backdrop colours (the video's 720p chroma is good to about +-15 per channel); the original gives each text control an RGBA colour (0x65c5d5 → renderer +0x48, kept as ARGB at +0x78, 0x48fd07) and renders its glyphs white (0x48fa96) unless the control keeps the font's own colours (+0xdd); the values traced are the options labels' (0,0,0,255) (0x4a441e, UI-037), the buy list rows' (255,255,255,255) (0x4ad340), the pause menu lines' (0,175,190,255) (0x48b2b9) and the shared title colour (234,239,102,255) (0x492d20, fifteen window builders), with the lobby island name's second line (252,232,7,255) (0x4b8604) and a list row control that turns (140,0,60) on message 0x403 with a negative argument (0x4a0710); the other 0x65c5d5 calls (134 sites in all; by an instruction scan: about 36 with white (255,255,255,255), 31 with black (0,0,0,255) such as the runs 0x49ada6-0x49b385 and 0x4a53a2-0x4a5d26, 7 with red (255,0,0,255) at 0x49ff69, 0x4a002f, 0x4a6273 and 0x4b2e93-0x4b31f7, 5 with (224,86,86,255) at 0x4aa82d-0x4aac70, and about 45 that pass the colour from registers or tables) are not yet assigned to screens | which screen each remaining 0x65c5d5 call colours (values, disabled states, costs) and the backdrop colours |
| UI-007 | `UI/Original/UiWidgets.cs:96` | one-pixel (×UI scale) drop shadow under UI text; the original's control text renderer (0x48f830) lays each string out and draws it once (one 0x6ae0f0 call into the control's bitmap), so its controls have no drop shadow; the whole glyph layout and draw library has one draw entry, 0x6ae0f0, whose only caller is the text box routine 0x492750 (three callers, each calling it once: the label and list text of 0x4a84c0 through 0x492c40, 0x480810 through 0x47fcd0 and 0x480460, 0x480570 through 0x47fe90; 0x6ae1c0 is the measure entry used by 0x4929f0), no offset or second pass exists there, and the BF4 glyphs carry only 0-15 coverage samples (docs/BF4-FONTS.md), so no shadow is baked into the font art; the control's attribute byte +0xdb is only ever written (the constructors 0x48f703, 0x48fe3d, 0x490091 and the attribute setter 0x48fcab; no read form of it exists in tp.exe's code by a byte scan), so it is no shadow flag; the capture's apparent shadow is not explained, and the vtable-reached control renderer 0x48f830 and the sprite blit of its bitmap (0x48f410) are not shown to add one | the sprite draw of a text control's bitmap (0x48f410 → 0x588c20 is only the release of the control's sprite quads, and the text control's vtable 0x6ff350 has no draw slot of its own (slot 2 is a bare return), so the control's sprite quads are drawn by the engine's sprite pass, where no reader of the colour at +0x78 was found) and whether a capture's shadow is a render-state effect (UI-037) |
| UI-008 | `UI/Original/UiWidgets.cs` | text buttons on purple_button art (a texture name of the Mac build: tp.exe holds no `purple` string, so which art the PC front-end buttons use is unread): each half is one end cap drawn with its mirror image; upper half normal, lower half focused/pressed | the PC front-end button drawer and its art names (not found by string; look for the models the front-end tables name), or a capture of the original front-end buttons |
| UI-013 | `UI/Original/UiDialogs.cs:9` | the confirm dialog follows table 0x7583e8, the game-mode screen the name dialog 0x753f50 and the load screen the load dialog 0x7523f0, whose rows show the park's name and, right-aligned, the save file's local date and time as 0x49ec80 formats them (UITEXT 0x1c0 with day, month and year, then " %02d:%02d"; shipped parks and the sandbox have no file time and show none); the lobby's and the park's pause menus lay out their lines as 0x48c150 → 0x492f60 does (text width + 1 and height + 5 pixels on the canvas, centred, from top 10, 5 units apart), but the height follows OpenTPW's menu font tier ([EXT:ui-font-tier-by-scale]) with its text layout box standing in for the measure 0x4929f0 and the online state's top of 0 (FUN_005b6450) is not followed; the focused line's hover ramp is built (0x48b2a0 sets the ramp slot to +1 on message 0x10001 and -1 on 0x10002, 0x48b220 steps it on every message 0x1e, eight frames at the 60 Hz reference, [EXT:frame-driven-at-60hz], from grey 0x21 in steps of 0x20 to white; the ramp is driven by OpenTPW's focus, [EXT:pause-menu-hover-focus]) | the measure 0x4929f0 (its screen-size dependent flag cases) and the online state's top of the pause menu lines |
| UI-018 | `Client/GameFlow.cs`, `World/Lobby/LobbyEffects.cs`, `World/Lobby/LobbyFlyingMeshes.cs`, `World/Lobby/LobbyScene.cs` | the lobby backdrop is painted flat with the cloud layer's eased colour (red toward the blue target) as a stand-in: the original clears to the fog colour 0x44DDFF and tints only its cloud layer (the engine sky's sky.tga, coloured from that grid), which is not drawn, nor is the scene's fog; two settings gate the eased colour and the flying-mesh count share, all in the .bss settings block 0x785840-0x785920 filled at runtime ([0x785888] > 1 and the options word 0x78d8d8 == 1 assumed to pass, as the capture's tinted clouds show; [0x7858dc] assumed 100 %, the video's flocks cannot be counted at 720p); an island without SKYCOLOUR gets black instead of the random target its constructor draws (0x5dfb4a-0x5dfb7b, three CRT rand() calls per island); the island clips are picked by a local stand-in for the C runtime's rand(); while the globe is up the lobby's flyers and island clips hold still (the original's per-frame lobby update 0x5d5c50 has no globe branch and the main state stays 2, 0x54e6c7, while GLOBERADIUSIN and GLOBERADIUSOUT are lobby-file commands, so the original probably keeps stepping them behind the globe; whether the lobby scene is drawn then is not traced); the enter sequence is built (the camera turns to the island angle + π, the gate plays M1, the camera zooms under the black fade and the park starts below radius 8, 0x5e0490 states 1 and 2; an early leave's gate M2 close, 0x5e1350, is not reachable from the front end here); the key-unlock sequence is not traced; the lightning flash is not drawn (the capture shows none) | the engine sky's draw (0x585ee0-0x586a00) and fog, the runtime contents of the settings block 0x785840-0x785920 and the options word 0x78d8d8, the lobby's subsystems while the globe is up, the key-award animation's code |
| UI-024 | `Hud/ParkHud.ObjectWindow.cs`, `Hud/ParkHud.ObjectSettings.cs`, `Hud/ParkHud.ObjectGauges.cs`, `Hud/ParkHud.BuyWindow.cs` | the object windows (tables 0x755150, 0x755908, 0x755e80) draw their controls at the table rectangles and, for a shop or sideshow, the customer satisfaction gauge from the object's visit history (0x4e1e30), but the preview is OpenTPW's orthographic icon ([EXT:preview-icon-cpu-projection]); the local happiness gauge counts the guests on park cells at the object's anchor cell where 0x4cd880 walks the thing lists of the cells around the thing's own cell; the build window fits the preview size to translated names and prices | the thing cell byte pair (+5, +7) an object keeps and whether it is the placement anchor |
| UI-025 | `Hud/ParkHud.MessageTube.cs` | the message tube holds up to five messages (the most the video shows) and drops the oldest on overflow, where the original's tube has eight 24-byte slots (insert 0x59a940; a message is admitted by 0x59abc0 under a per-type cap, 0x59c320, from the registry 0x76e300) and, with every slot full, replaces the lowest-priority slot only when that priority is lower than its own and is otherwise dropped; pending messages are not saved with the park, as in the original (its MESS chunk, 0x40fc80, holds only the 29 category id sets); the panel's rectangles are table 0x7501a0; its action buttons take the mb_ model of their kind (the original's table 0x48e610: kinds 3 and 4 mb_locate, 9 to 0x11 mb_buy, 0x12 b_loan, 0x13 mb_resrch, 0x14 b_pkinfo, 0x15 b_staffcost, 0x17 mb_callmech, 0x18 b_entry, 0x1a mb_accept, 0x1b mb_decline), but only the challenge offers carry actions (Accept and Decline) here and the other kinds' events are not ported; the strike messages (TAG_SYSTEM.str) and the challenge offers use the tube; its red body and white icons are OpenTPW's art ([EXT:message-tube-art]), an unanswered offer that was deleted is posted again ([EXT:challenge-offer-repost]) and OpenTPW's own feedback lines stay in a status line ([EXT:status-line]) | the registry's per-type caps and priorities (filled at run time, the loader 0x50af78 is not traced) and whether the tube draws eight icons |
| UI-026 | `Hud/ParkHud.BuyWindow.cs`, `Hud/PreviewIcon.cs:14` | build icons: the preview model (P<name>.MD2, else the main model) is drawn by OpenTPW's CPU projection ([EXT:preview-icon-cpu-projection]); the buy window takes the original's 45° tilt (0x468e50), its turn of 0.0004 rad a millisecond that runs on across selections (0x46908a, 0x468e2f) and its fit (0x4689f0), but the original draws the preview through its 3D object pipeline (the ride object made by 0x463060 with a world matrix that 0x468e50 rebuilds each frame: the yaw and the ±π/4 tilt through the sine table, the matrix's x column scaled by 0.75 and its depth column by 0.1, then offsets) and starts the model's animation 5 (0x4732a0), which OpenTPW does not; the selected item's footprint grid (8×8 cells coloured by cell type in control 0x1eb, 0x4ab1b0, record 0x754d50) is built (BuyFootprint), with the 1-pixel edges of its cell rectangles not traced; the object windows' previews keep a 30° tilt and 0.8 rad/s; sign faces use the object's rendered sign textures (ObjectSigns) | the camera and projection the 3D pipeline draws the preview object with, the offset terms of 0x468e50 and the meaning of animation 5 |
| UI-027 | `Hud/ParkHud.cs` | a park click selects the object occupying the cell of the cursor's terrain hit (TerrainRayPick: 0x45bf90's heightfield triangles, the cell 0x45d560); the original selects a ride, shop or other object by its pick sprite: the sprite pass 0x5890e0 picks the nearest sprite flagged 0x200 under the cursor (a circle round its projected anchor), 0x532bd0 finds its owner among the things of the 3 x 3 cells round the hovered cell (types 1, 4 to 8 and 0x12), the hover handler 0x486d90 turns a hovered type 5, 4, 6, 7 or 8 into cursor mode 8, 9, 0xa, 0xb or 0xc and the click dispatcher 0x4879d0 opens the object window 0x4b6080 for it, only outside the visitor pick state (Shift alone or the visitor locator panel out); the ray's object hit [0x7a1cd4] selects nothing: its only readers copy it to the camera control block 0x79c6ac (0x443aab, 0x443d23) | which model instances carry a pickable sprite (flag 0x200; the instance word +0x114 that 0x475ef0 tests: it maps a sprite at instance +0xd0 to its instance, +0x114 is copied by 0x475eb0 and cleared at 0x4750af, 0x47691f and 0x47698c, read at 0x477e33 and 0x478483, and no non-zero writer was found by a byte scan of tp.exe's code) and that sprite's position, size and scale, to build the sprite pick for objects; the object half of the ray pick (RIDES-021) |
| UI-031 | `Hud/ParkHud.cs:350` | exactly Ctrl at the placing click keeps the build tool armed (0x5253b8) and at the last click of a ride's queue re-arms it, the tool's angle returns to 0 when it is left (0x52f3b6); the coaster and water-track tools, whose original sub-modes are not traced, end the build tool whatever the modifier; the Ctrl word 0xC at 0x5259e2/0x5259f4 is Ctrl alone (the modifier builder 0x46b420: Shift 3, Ctrl 0xC, Alt 0x30) and a placement that 0x52fab0 reports as entrance-bearing (the sub-record flag +0x40) takes the tail 0x5259cf: the angle returns to 0x80, an item record with +0x29c = 1 ends the tool through 0x52f200 whatever the modifier, otherwise Ctrl alone keeps it (0x525a06) and any other word runs 0x52f1b0, which only shifts the mode history; which OpenTPW placements are those is not mapped; Level.PlaceObject owns purchase/guest linkage and its removal handler owns scrap credits | the coaster sub-mode's continuation and the predicate that selects the 0x5259cf tail (what record +0x29c = 1 stands for) |
| UI-034 | `UI/Original/UiImages.cs` | a fully opaque texture on a transparent (flag 0x2) model slot keys out black with a soft edge: transparent up to channel 6, opaque from 48, alpha ramped and colour un-premultiplied between (only ipan in the lobby f_lobbutbg panel); the texture state routine 0x567c10 sets an alpha test, an alpha reference and blend factors from the record flag bits (mask 0x556), but that its record flag 0x2 is the slot's flag 0x2 is not shown | the original's render state for flagged texture slots (whether the record flag in 0x567c10 is the slot flag, and the colour-key handling of an opaque texture) |
| UI-037 | `UI/Original/UiText.cs` | option bar label colour (16,16,48), no drop shadow; the original's options labels (0x4a43b0) and slider labels (0x4a4490) are text controls given the colour (0,0,0,255) (0x4a441e, 0x4a44fe) and font slot 6 ([0x7537b4]) and drawn once without a shadow (0x48f830); how the control's colour reaches the white glyph bitmap (0x48fa96) when it is drawn is not traced, so the capture's near-black is kept | the sprite draw of the control's text bitmap (0x48f410 → 0x588c20 is only the release of the control's sprite quads, and the text control's vtable 0x6ff350 has no draw slot of its own (slot 2 is a bare return), so the control's sprite quads are drawn by the engine's sprite pass, where no reader of the colour at +0x78 was found) and whether it tints with the colour at +0x78 |
| UI-041 | `Client/GameFlow.cs:147` | Load Park takes no game type from the loaded park: the original derives GameType from flag bits 0x2000000/0x1000000 of 0x7a1a8c (0x550ca0), set by a front-end toggle (0x4bbf63), and the Mac saves none with a park | whether a park file of the PC build can carry the game type (not traced) |
| UI-042 | `Hud/ParkHud.CameraPanel.cs`, `Online/OnlineScreens.cs` | the camera button's slide-out panel (table 0x751720) slides in and out as the arm drawer 0x4a14f0 does (ten 20 ms steps, cat_ui 0x1C and 0xC2, help 0x131), but its slide ends at the table rectangle where the original's positions come from the HUD arm window ([0x7cb2b8], its child 0x21 found at 0x4a1b35) and the arm: the arithmetic of 0x4a1613-0x4a1650 starts the panel at the arm's left less the HUD window rectangle's width and 50, centres it on the arm and puts its top 50 units above the HUD window's top (read from the instructions, medium confidence; the end position is not resolved); the camcorder button arms the C-key walk ([EXT:camcorder-first-person]) where the original's button 99 enters the camcorder at once (0x498ad0 → 0x481a10), and the walk's movement and look controls are not traced (the original's eye is terrain + 5 units, 0x42b1c0); the postcard window (table 0x7540d8) keeps its photo frame empty and its preview and address book buttons disabled (the 640×240 camcorder photo 0x4a9280, the preview redraw 0x4a84c0 and the address book 0x4a8140 are not ported); the view cycle is a third button ([EXT:camera-view-cycle]) | the HUD window's rectangle; the walk's key bindings (action table 0x787c14) and the time unit of its turn step; the photo, its preview and the address book |
| UI-046 | `UI/Original/UiGauge.cs`, `Hud/ParkHud.BuyWindow.cs`, `Hud/ParkHud.ObjectWindow.cs` | the gauge bars (buy, ride, shop and sideshow windows) take the original's values and shape: the integer (value << 10) / 100 of a 0..1024 range (0x4abb00, 0x4ade40), a bar of the control's cell less 10 units top and bottom (0x485e30) that shows the art cut off at the value with the rest of the cell the track (0x478400: ridestatbar.wct's top 16 texel rows the bar, its bottom 16 the track); the colours of the bar and the track are read off the 720p video (a 70 % bar runs red, orange, yellow to lime green) because the decoded art's colours (now that the chroma decode is fixed, a red-orange start running to green) were not matched against the video | the gauge sprite's colours as the original draws them (the draw of 0x477ff0, which would let the sprite replace the video-read ramp) |
| UI-047 | `Hud/ParkHud.cs` | which OpenTPW tool state corresponds to the mode-object types that show control 48: type 3 (the tool-mode object, vtable 0x6fe9e0) wraps every build tool, so the held blueprint's price, the path or queue line's cost so far and the coaster track segment under the cursor show it; type 8 is the coaster panel's mode object (vtable 0x6feaa0, built by 0x46cfc0 when a panel button is pressed, 0x497bc0), which shows it only while bit 0 of 0x79c658 is set (0x447990; set at 0x441a5d), a placement-in-progress state OpenTPW does not track for the coaster tool (the bit is set at 0x441a5d when (flags & 0x3f) is 0 and cleared by the second action, 0x441a00; medium confidence), so OpenTPW shows the coaster cost in every sub-mode; no mode-object class returns type 4 (the classes 0x6fe980-0x6fead0 return 7, 3, 1, 5, 6, 8 and 9; 5 and 6 are the staff place and carry tools), and the staff and patrol tools show nothing | which OpenTPW coaster tool state corresponds to the placement-in-progress bit of 0x79c658, and the class that returns type 4 |
| UI-050 | `UI/Original/UiGauge.cs` | the buy list's third column draws a tick box per row, ticked when the park owns at least one of the item; the box and the tick are drawn with rectangles and strokes in the video's colours, not the original's art (i_boxtick.MD2 holds only the empty box); the original's row cell 0x4ad340 sets the model i_boxtick (0x755144) on a child, and the owned state is 1 for an owned count of at least one (0x4d3d80, populate 0x4aaf70), else 2 for the first three items of a category (0x4caa70), else 0 (stored through 0x66403b), but what each state draws is not read | the handler that turns the cell state into a frame (the message 0xc00 path) and the frames of i_boxtick |
| UI-051 | `Hud/ParkHud.cs` | the ticket counter is the park's available tickets, where the original shows the player's earned tickets less the spent count (0x5af5d0), because OpenTPW's park keeps its own spent count and never moves the player ledger's, where the original's purchase check 0x4d4ad0 calls 0x5af740, which adds the cost to the player's spent count (+0x1c) when 0x5b1930 passes; the key counter is the ledger's (0x5af680, matched) | routing the park's ticket purchases into the player ledger (a design decision: the ledger is optional and absent in the sandbox and Instant Action) |
| UI-060 | `Hud/ParkHud.FinanceWindow.cs` | finance graph: the eight series' colours (the records of 0x499f30), their order and the selection masks (0x49a2e0: the bank balance and the park value clear the six cash-flow series, a cash-flow series clears the pair), the ten-division scale from min(0, lowest sample) to max(0, highest) (0x47fb30) and the default series (the bank balance, word 0x751fa0 = 1) follow tp.exe; the line style (two-unit strokes, grid lines, point marks) and the one-unit scale OpenTPW gives a flat range are OpenTPW's | the original's graph drawer 0x47fe90 (stroke style, grid, point marks) and its empty or flat range |
| UI-064 | `Hud/ParkHud.ResearchWindow.cs` | the research workload knob is the original's slider: values 1023 (top) to 0 (bottom), a limit of ((100 - c) / 3 + c) x 1024 / (MaxResearchersInPark x the grade-4 ResearchAbility) from the researchers' summed ability c, a workload of 70 + 30 x value / limit and the ball's travel (0x4a96b0, 0x4aac80, 0x66b088); a drag that starts on the ball moves it by the pointer's movement, clamped to the track, as 0x66b771 does; the knob's child 0xd2d12 (set to the limit) and 0xd2d11 (state (2 x step) / 7) are not drawn; the slider's ball proc 0x66bb9b moves the ball by the pointer's offset from the press point (stored per button at 0x10005 in 0xfaa59a/0xfaa59c) from the rectangle saved at the press or release (0x65cf3d), clamped to the track by 0x66b771, then reads the value back with 0x66af19, and a press in the track outside the ball puts the ball's centre under the pointer where the slider class pages by its step (0x66b8aa, 0x66b088 with the page word +0x148, whose value is not found) | what 0xd2d12 and 0xd2d11 draw, the page step of a press in the track, and whether the saved rectangle is the press-time ball |
| UI-081 | `Hud/ParkHud.Staff.cs` | applicant details: the skill gauge is a type-9 gauge set to the candidate's grade byte (+8, written by 0x507600 as AvgGradeOf* + R % 3 - 1 stored as 4 when above 4, OpenTPW's Grade) x 1024 / 5 (0x49b8e2) of its 0..1024 range (0x66d885 clamps), so a grade-0 candidate's value is 0; OpenTPW fills (Grade + 1) / 5 because the video's cheapest ($60) candidate shows about a fifth (12:00); the gauge models crop their art to value × 100 / 1024 per cent (UI-046), so a byte of 0 draws an empty bar and the fifth is not explained by the drawing | which byte the video's cheapest candidate holds (the writer 0x507600 stores AvgGradeOf* + R % 3 - 1 as a byte, a wrapped 0xff becoming 4) |
| UI-082 | `Hud/ParkHud.Staff.cs` | the hire window's figure is drawn one sprite pixel to 3.2 canvas units (640 pixels across the 2048-unit canvas), where the original sets its register 8 to 8.0 (0x004755D0, the scale 0x00542010 copies to the draw record +0x2C, default 1.0; the record slot 8 is +0xA4, the X scale, +0xA8 the second scale, both default 1.0); how the screen-sprite path (flag 0x40000, list 0x0087692C, count 0x875864) turns that scale into pixels is not traced: the list and its snapshot 0x876424 have no reader by absolute address, so the code that consumes them is not found; the figure also shows for an employee selected in the employed view, where 0x49BA00 reads only the applicant pool (0x507BD0, 32 entries of 0x14 bytes) | the engine's screen-sprite quad size, or a capture of the hire window and of the employed view |
| UI-086 | `Hud/ParkHud.Map.cs` | paths, queues and unowned land take the original's records (classes 10 to 12 of the writer 0x5f2050: 0xf86b00 (31,166,31), 0xf86b24 (37,198,37), 0xf86b6c (0,23,0 alpha 128)); placed objects use one plain colour over the shipped theme map image where the original gives an object cell the class of its WhichUIType (1 rides, 2 shops, 3 sideshows, 4 to 7 features, 8 track cells), colours it by the ramp 0xf86bd8 (248,0,0) at 0, 0xf86bfc (248,200,0) at 50 and 0xf86c20 (48,252,0) at 100 (0x5f09a0) of the value +0x44 of its object record and outlines it where a neighbour differs (0x5f2380, black or green by +0x68); the base colours are immediates written by 0x5f0700 (black, white, green, yellow, blue (0,78,254), cyan, dark blue (11,66,110) and gradient triples built by 0x5f0970), and the draw 0x5f2380 switches on the cell kind (1 path, 2 queue, 3 object, 8 ...) with the layer mask at +0x18, taking an object cell's colour from 0x5f09a0 over the gradient of the value read from the thing (0x4e0860, 0x4e1e30 by layer mode 2 or 3) | what the value +0x44 of an object record is (modes 1, 2 and 3 of the layer word +0x20: 0x4e0860, 0x4e1e30), the land modes of the buttons 2444 to 2447 and 2455 to 2457, the track pass 0x5f2fd0, the item dots and footprints (kinds +0x14, 0x5f44d0) and the outline's style argument 8 |
| UI-091 | `Hud/ParkHud.LocatorPanel.cs` | the locator panels take staff newest first (by id, descending) and visitors by id, where the original walks the role's thing list (+0x210) and the thing list (0x4824d0); the camera snaps to the followed actor every frame where the original sets the focus directly (0x486860 → 0x42aab0) and the per-frame matrix blend of 0x42b1c0 (weight (1000 - dt) x 0.0005 of the previous frame, dt clamped to 1..1000 ms) smooths it, which OpenTPW's follow does not copy; a staff member's patrol rectangle is not marked (0x536100 in 0x4b3640); the panels appear without sliding and, for a given actor, without the sounds the opener 0x4b3f60 plays (0x11f for a visitor, 0xc6 for staff; the Ctrl+V opener 0x4b4280 without an actor plays none); a picked-up member's state frame 9 (0x485ec0) draws the first frame; the staff window's own locate buttons focus actual actor positions, the list rows' 0x402 path (0x4867b0) is the original's | the insertion order of the role lists (the thing chain 0x516130/0x516160) and the patrol markers |
| UI-100 | `UI/Original/OriginalPointers.cs` | the link and end pointers are never chosen and the cash pointer is not chosen at 0x539fb6: end (0x816d4c) is set when the path validator 0x535670 returns code 0xb (0x535ae5, 0x535b3d) and at 0x53a1c6, link (0x816d60) by 0x535670 at 0x535c72 and for code 8, a queue end whose neighbour is of kind 3 (0x535d00), and cash (0x816d5c) also at 0x539fb6; the crosshair's art (CCro.tga) is not in the game data | the path and queue states behind the validator codes 8 and 0xb, the cash pointer of the tool at 0x539fb6 and the crosshair art |
| UI-092 | `Hud/ParkHud.EmployeeWindow.cs` | the per-employee window (table 0x756f48, 0x4b6080) plays the employee's figure in its portrait frame as the hire window does (0x4b5330 → 0x486530, scale 8.0; UI-082 for the pixel size) where the original's phase comes from a record field (+0xE0) OpenTPW has no counterpart for; a click on the title does not rename (0x48d2e0 holds no rename dialog; the title message 0x10006 goes to a vtable slot +0x38 of the object 0x65e705(0) returns, which is not identified; help 85); the help 0xd4 the window posts while the employee has a patrol area (0x4b5ab0) is not posted; the arrows cycle the economy's member list in hire order for the thing list (0x7cf56c, newest first); the locate button is disabled while the employee has no world position (held in the hand), where 0x4b5f07 always opens the locator panel | the rename (that vtable slot) and the thing-list order of staff loaded from a save |
| UI-094 | `World/RideCameraMode.cs`, `Hud/ParkHud.RideView.cs` | the ride view is a first-person eye on the camera car of the coaster train with the smallest position (the car 0x436880 selects) or the carriage's height (prototype), looking along the track or the entry heading; the ride camera never changes the projection (tp.exe builds it once with the argument 90, 0x42a990 and 0x54e860), so its field of view is the park camera's and shares that difference (UI-101: 55 in OpenTPW against 90); the static lasts 0.33 s (video 0.30-0.35 s) | the static's timer; the field of view is UI-101's |
| UI-095 | `World/Level.RideView.cs` | leaving the ride view restores its prior camera instance, including the host terrain walk, and re-centres the host park camera on the object under its target with the original's test (0x42a340: a placed object whose type has CannotRide 0) and look direction (0x42a1d0: the link byte (+0xd) of the object's map cell, a 0x44-byte record at *0x7cf6ec + 0x294, masked with 0x55); the original's park camera is its own pose (yaw, height and smoothing in 0x42b1c0) that OpenTPW's host camera does not reproduce; the link byte is set whole by 0x5227e0 and cleared by bits by 0x5227f0 (called from 0x524960, 0x528a70, 0x52a050, 0x52a970, 0x52b400, 0x52ba50, 0x52fc80, 0x532fc0, 0x536490, 0x5367a0, 0x539670, 0x53af00, 0x53b280 and 0x53bb20) and reset by the cell reset 0x536490, and which of these writers give a ride's cell its bits is not traced (OpenTPW's cell map keeps link bytes for path, queue and track cells only, so the yaw rule fires for none of the placed objects) | which of the cell-link writers set the bits of a placed ride's cell, and the park camera's own pose |
| UI-096 | `Hud/ParkHud.cs` | the pause menu (with the screens opened from it) and the map window stop the park simulation; the pause menu's cases 1 and 22 open 0x49efb0 and case 2 0x49f280 and case 5 0x4a3a30 (jump table 0x48bbc8 of 0x48b6a0), which keep the pause (0x49efba, 0x49f29f, 0x4a3a6c), so those callers are covered; the postcard (0x4a93d7, Ctrl+P) and the delete confirmations (the message box 0x47f020 of 0x48cd10) and the employee dismiss box (0x4b5e5d, whose call 0x4b5eac is a 0x47f020 caller with UITEXT 0x18d) pause too, but the other generic message boxes (table 0x74f920, up to three b_okay/b_exit buttons, 27 call sites in 7 functions: the alert queue 0x47f620 fed through 0x47f410, the file errors of 0x4237f0, 0x4a77f0, the online notices 0x4bc500 and 0x4ec030; each pauses at 0x47f26c while the gate 0x786ba4 is 1 and 0xf82884 is 0) and the application's deactivation (WM_ACTIVATEAPP in the main window procedure 0x46b600, 0x46b74c, while 0xf82884 is 0) do not | which of OpenTPW's other dialogs (the loan boxes, the restart and quit boxes) correspond to the remaining 0x47f020 call sites, and pausing for focus loss |
| UI-107 | `World/Sky.cs`, `World/Level.Sky.cs`, `Client/GameFlow.cs`, `Render/RenderFog.cs` | the park's sky is built (World/Sky.cs): the cloud layers (sky.tga over the 16 x 16 grid, 1, 2 or 4 by SKYQUALITY, scrolling with the frame time) and the backdrop cylinder (sky_cyl.tga) over the level's ThemeEngine.FogColour clear, all camera-centred with no depth; not drawn: the moon billboards (SkyObjects of the hallow and space levels: moon.tga, moon2.tga; the draw 0x5860d0 places them in clip space and only with SKYDETAIL, flag 0x400000), the flag 0x2000000 variants (a mirrored dome under the horizon and other cylinder rows, 0x5863c0) and the single opaque layer of the flag 8 mode; the texture page's scale and offset (+0x20..+0x2c) are taken as 1 and 0, the alpha test of state 0x556 (alpha below 0x10) is not applied, the vertex colour mask (DAT_0087a248 + 0x2c) is taken as all ones, and the clear is always the fog colour; objects, terrain, water and kid sprites fade into the level's fog colour (RenderFog, the shaders' g_vFogColor; 0x4080e0 copies it to [0x8bd47c]), but over OpenTPW's own distance curve instead of the original's linear range (FIRSTPERSONVIEWDISTANCE 0-3 picks start and end 50-60, 100-110, 135-150 or 230-250 original units = 0.2 engine units each: 0x44d8f0 -> 0x583280 -> D3DRS_FOGTABLEMODE linear with FOGSTART/END, 0x567ad0, in view depth with no range fog), which would hide the whole park from OpenTPW's own camera (UI-101); which passes set the per-primitive fog bit 0x100000 of the state word (D3DRS_FOGENABLE, 0x567620) and whether particles fog are not traced | the park's render flags ([0x8bcbc8] bits 0x2000, 0x4000000, 0x2000000, 0x8) and the clear that follows them (0x5737aa), the texture page values of a standalone TGA, the sky objects' clip-space size and place (0x5860d0, FUN_00578d20's matrix), the original's park camera pose that its fog range goes with (UI-101), and the passes that set the fog bit |
| UI-901 | `Hud/ParkHud.cs`, `Hud/ParkHud.Gauge.cs` | the happiness gauge shows the visitors' mean happiness as a rising red-yellow-green bar in the gauge model's empty dish; the original's gauge is a type-9 control (vtable 0x705c20) whose setter 0x66d885 keeps a value of 0 to 1024 and sends message 8 with it to its two listener models (+0x98, +0x9c); the value follows the original (every 2 s, ftol(mean x 1024 x 0.01) of the byte-truncated happiness of the in-park visitors, 0 while the park is closed: 0x4a1cd0, 0x4c7bb0), and the fill is that value over 1024 | the message-8 handler of the gauge's listener models (the value-to-frame mapping) and the HUD builder 0x4a1d70's gauge control |
| UI-905 | `Hud/ParkHud.AttractionsWindow.cs` | the All Attractions list's bars take the object window's values (the gauge rows of the ride window) and the satisfaction bar reads the object's visit history as the window does (UI-024) | whether the list's bars are scaled as the object window's gauges are |
| UI-907 | `Hud/ParkHud.VisitorWindow.cs` | the All Visitors window (table 0x7506c8) centres the rides column, where its cell (0x48fde0) keeps the class default alignment | the cell text object's default alignment |
| UI-910 | `Hud/ParkHud.VisitorInfoWindow.cs` | the per-visitor window is built from the original's table 0x7575e0 with its values (cash, time in park, rides ridden, purchases, sideshows played and won, the happiness bar, the status text from KIDSTATES.str, the thought text from THOUGHTS.str with its icon, the 4 s refresh, the cycle arrows, the locate and All Visitors buttons), but its portrait frame (0x3722) stays empty where the original plays a live figure of the visitor (0x4b6b90: 0x486530 with the frame's figure id + 2 at speed 8.0), and the arrows walk OpenTPW's visitors newest first (by id) where the original walks the thing list's type-1 things (0x4824d0, mask 0x40), an order not proven the same | what 0x486530 draws for a figure id and which id a visitor's frame holds (+0xe0), and the order of the visitors in the thing list against OpenTPW's ids |

UI-102, UI-103, UI-104 and UI-105 are retired: the park camera's middle-button turn, the map window's edge scroll, the Move tool ("The Move tool" below) and the buy list's golden-ticket rows are built from the original's routines.

Closed as OpenTPW design choices (October 16, 2026, approved by the user): the former
`APPROX:UI-002`, `APPROX:UI-005`, `APPROX:UI-012` and the own-window sizes of
`APPROX:UI-013` are now `[EXT:…]` labels at their code sites, and UI-025 and UI-026
keep only their original-behaviour parts as register rows. Behaviour
is unchanged.

| EXT name | Former | What it is |
| --- | --- | --- |
| `ui-font-tier-by-scale` | `APPROX:UI-002` | The BF4 size tier follows the output scale (thresholds 0.36 / 0.6) and the role fonts are OpenTPW's pick; the original chose one of four font banks by its fixed screen modes (`0x419710`) and named a bank slot per control (`0x485a70`: menu lines 0, HUD cash 1, HUD date 3, window labels 6, list rows 7), with no rule for other output sizes. |
| `ui-linear-filtering` | `APPROX:UI-005` | The pink (255,0,255) key with neighbour colour bleed lets UI images scale with linear filtering at any output size; the original drew at its few fixed modes. |
| `ui-modal-right-click-back` | `APPROX:UI-012` | A right click backs out of OpenTPW's modal screens (and its other windows) when RMBCancelOn is set, with hover focus following the pointer; the option's only direct readers in the original are the park view, the camcorder and the ride views. |
| `own-window-sizes` | `APPROX:UI-013` (part) | The sizes of OpenTPW's own windows (`UiDialogs.CenteredWindow`), which have no original layout table. |
| `challenge-offer-repost` | `APPROX:UI-025` (part) | A challenge offer whose message is deleted unanswered is posted again so the challenge system never stalls. |
| `message-tube-art` | `APPROX:UI-025` (part) | The tube body is drawn as rectangles and its icons as discs. |
| `preview-icon-cpu-projection` | `APPROX:UI-026` (part) | Build and object icons are drawn by a CPU orthographic projection with painter sorting rather than the engine's 3D object pipeline. |
| `fit-button-text` | `APPROX:UI-032` (part) | The small-font fallback for a long single-line label. The rest of `APPROX:UI-032`, the formatter's greedy wrap (`0x6aec80`) and its glyph classes (`0x6aeab0`), is matched and retired. |
| `options-slider-steps` | `APPROX:UI-036` (part) | OpenTPW's option sliders step through its own presets and 11 volume steps. The rest of `APPROX:UI-036`, the original slider class (value to thumb `0x66b088`, truncated drag `0x66af19`, page click `0x66b8aa`) and the options page's 0..100 range (`0x4a455a`, thumb left where it was dragged, `0x4a2e90`), is matched and retired. |
| `option-label-fit` | `APPROX:UI-039` (part) | Option labels are sized by a shared 58 % letter box with a per-label drop to a size that fits, where the original does no fitting and lays an overflowing label out through the text library (wrapped into its rectangle, `0x6ae930`, `0x6aeb10`, `0x6aec80`), so this size choice stays a deliberate difference. The rest of `APPROX:UI-039`, the original's slot-6 label font (`0x4a443c`, tier pairing under `ui-font-tier-by-scale`), is matched and retired. |
| `pause-menu-hover-focus` | `APPROX:UI-013` (part) | The pause menu line's hover ramp is driven by OpenTPW's pointer focus (the original's line procedure `0x48b2a0` takes messages `0x10001` and `0x10002`, read as the pointer entering and leaving); the user approved it as a design choice. |
| `list-row-pitch` | `APPROX:UI-044` | The buy, loans and load-park lists use a fixed pitch of 41.4 units; the original's pitch (34 to 42 units) follows font slot 7's line height and the screen height, which OpenTPW's resolution-independent output sizes cannot copy. |
| `online-globe-audio` | `APPROX:UI-942` | The online globe's music and ambience (`cat_globallobbysfx` 2 and 3) replace the lobby's, which OpenTPW stops while the globe is up and restarts after it. The globe belongs to the online extension. |
| `visitor-info-window` | `APPROX:UI-091` (part) | The visitor info window that Ctrl+V tests is not ported; a click on an All Visitors row or a visitor picked in the park stands in for opening it. |

Data-backed (tagged `[DATA]`): the 2048×1536 canvas and authored rectangles of
placed models (`ui.wad` roots/bounds), button state frames and texture order, V
flip, lobby.txt/theme files, balance, prices and research availability (the economy's
`.sam` data), Totem price (`Totem.sam`), build category (`Rides.sam`), excitement level (`Totem.sam`/
`Rides.sam` `UsageInfo.ExcitementLevel`), every UITEXT/UIHELPTEXT/THEMENAMES/
OBJECT_NAMES string, BF4 fonts.

### Design choices closed from the register (2026-10-16)

These register IDs were closed as OpenTPW design choices, with the label at the code site; behaviour did not change.

- `APPROX:UI-061` is `[EXT:entry-price-window]`: the entry-price window is a host layout over the economy values with the original's range and one-pound step.
- `APPROX:UI-940` is `[EXT:frame-driven-at-60hz]` (first `[EXT:lobby-fixed-step]`, at 30 ticks a second, now merged into the 60 Hz reference label, docs/COMPATIBILITY.md "Frame-driven timing"): the lobby effects advance at a fixed 60 Hz, independent of the frame rate.
- `UI-041`, `UI-100`, `UI-905`, `UI-907` and `UI-941` keep their IDs for the original behaviour that remains; their design-choice parts are `[EXT:no-player-profiles]`, `[EXT:software-pointers]` (the pointer art drawn by the UI), `[EXT:list-selection-by-id]` and `[EXT:frame-driven-at-60hz]` (the islands and the flyers now step at the same 60 Hz).
- `APPROX:UI-044` is `[EXT:list-row-pitch]`, `APPROX:UI-942` is `[EXT:online-globe-audio]` and the visitor info window of `UI-091` is `[EXT:visitor-info-window]`; see the EXT table above.

## Open

Original screen positions of code-placed elements, menu flow details (profiles,
online), cursor drawing, button/arm animations (`b_door` etc. have no animation
members; arm slide-in is not reproduced), sounds, original thousands separators
and currency handling in French/German, D3D11/Vulkan runs, captures from the
original game for comparison.

The developer prototype Totem shares the ordinary objects' per-tick economy
synchronization: its visitor bridge receives the linked instance's wear, age
and sideshow terms, while its script supplies open and broken status.
`PrototypeEconomySyncTests` checks this without original assets or a GPU.

The All Visitors window consumes the plain V edge only when the registered
Ctrl+V visitor-locator chord is not down. The locator retains its existing park,
modal and ride-view gates, while plain V retains the existing window toggle
behavior. `HudModifierShortcutTests` checks both physical Ctrl with injected V
and a fully injected chord through actual button rebuilding; Ctrl+S remains
the staff locator (plain S stays unhandled here), and Ctrl+P remains excluded
from the pause key. Global bindings and help shortcuts are unchanged by this fix.
These are synthetic model/input checks, not rendered or original-game parity.

Ctrl+P opens the existing postcard composer directly from a Theme Park World park,
including a read-only visit. It keeps plain P as pause and follows modal, text,
ImGui, build/removal/cell/coaster and ride-view input ownership. An unavailable
host callback or edition sharing capability reports the existing unavailable
message. The chord opens a form once; it never sends a message automatically.
The existing composer, local outbox and map-thumbnail attachment are reused
(`EXT:online-postcard-photo` still qualifies the photo). A simultaneous park press/release belongs
to the opened composer, without activating a path or selecting a visitor/object.
`PostcardShortcutIngressTests` exercises actual injected button rebuilding and
the full HUD frame, including the GameFlow composer and a temporary local outbox.
Original assets, native/GPU layout and network delivery are not qualified here.
Ctrl+P does not change the selected simulation speed. The composer retains the
existing modal pause while open; closing it removes that modal pause.
The registered default uses left Ctrl; right Ctrl+P remains excluded from plain
P pause but has no postcard action under the existing global bindings. Independent
full-frame tests also cover held chords after a capture owner releases input and
a simultaneous press over a pickable visitor, followed by release and a new click.

### Visitor selection in the park view

A genuine left press on a visible in-park visitor opens the per-visitor window (`0x4b79b0`) on that visitor in the original's visitor pick states only: Shift alone with no locator panel out, or the visitor locator panel out with no modifier (`0x487013`; the hover handler `0x486d90` sets cursor mode 7 only then, and in that state it sets no object or cell mode, so objects are not selected either). In every other state a press on a visitor acts like a press on the ground under it. Picking uses the current guest renderer's interpolated DrawX/DrawY terrain position, selected animation frame/hotspot, mirroring, camera axes and UnitsPerPixel. Overlapping rectangles choose the nearest projected depth, then the lowest visitor id; on an original level the chosen visitor must then stand on the cell of the cursor's terrain hit or one of its eight neighbours (`0x532bd0`, the offset table `0x81ED40`), otherwise no visitor is picked. A successful press consumes the existing tool/release latch before automatic path activation; a miss preserves ordinary path input. The roster's existing live selection healing applies when the visitor leaves.

UI-930 qualifies this host rectangle policy: the original tests a circle round the sprite's projected anchor in its sprite pass (`0x5890e0`: radius squared 0.0002 x (width squared + height squared) in clip units, nearest clip w wins, the first drawn keeps a tie, no alpha test), which the port does not reproduce, and partially near/far-clipped sprites are refused; transparent pixels and terrain/object occlusion are not tested. The original picks in the render pass (the pick block 0x7b05a8 is opened and closed around the draws by 0x46c100/0x46c110 in the frame routine 0x54e2f7, and the thing under the pointer lands in 0x7b05ac; the hover handler 0x486d90 reads its kind and the Shift/Ctrl/Alt state and the click 0x4879d0 opens the visitor window 0x4b79b0 or the employee window 0x4b6080 on it). No GPU parity is claimed. Hidden/removed/outside-park visitors and invalid/nonfinite/behind-camera projections are refused. Overlay/HUD controls, text entry, ImGui capture, build/removal/cell/coaster tools, ride view and its exit frame own their input. Read-only visits may inspect visitors without simulation or finance mutation. The public HUD update samples native capture flags at its existing point; an internal explicit-capture overload runs the same frame body in synthetic tests.

The window's opener plays cat_kids event 129 (`GameAudio.PlayVisitorClick`, the sound `0x81`) when it opens; there is no hover/held/release audio trigger. Original assets, native geometry/occlusion and rendered interactions remain unqualified.
### Live-map legend allocation (2026-10-13)

The satisfaction legend now counts the current eligible visitors directly instead
of grouping them into cells, allocating an array, and summing it. The drawn overlay
still uses its existing per-cell groups. Both paths share the same visibility,
thought and coordinate predicate, and totals are recomputed on every call; no
result cache or invalidation policy was added. Map layers, category/role controls,
projection, clipping, zoom and pan are unchanged.

`MapOverlayAllocationTests` drives the real map view and legend label through the
CPU `UiBatch`, with 1,500 synthetic guests on a 32×32 sandbox map and visitor,
happiness and satisfaction layers enabled. In the same Release/.NET 10 fixture,
with tiered compilation disabled and draw buffers warmed, allocated bytes changed:

| Measurement | Before | After |
|---|---:|---:|
| Map draw plus legend | 492,144 bytes/frame | 327,656 bytes/frame |
| Legend alone | 165,168 bytes/call | 680 bytes/call |

The allocation regression requires less than 2 KiB per legend call, with generous
headroom above the measured result. Live tests independently check hidden guests,
outside cells, changed thoughts, removal/return and metric toggles. Existing map
projection/control tests remain green. These are allocation measurements, not
wall-clock or browser/GPU/original-player performance claims; ordinary tiered-JIT
runs can report different allocation counts. Per-cell overlay grouping remains
an explicitly measured source of allocations for a separate bounded pass.

The [independent map allocation review](MAP-ALLOCATION-REVIEW.md) records fresh
marker/legend lifecycle coverage and matched managed red/green measurements.

### Outcome-cell allocation measurement

The satisfaction overlay now reuses view-owned coordinate/index and struct-count scratch collections, cleared and rebuilt from current guests on every draw (including clearing while disabled). First observed-cell encounter order, visibility/bounds/thought filtering, checked per-cell counts, colours and existing geometry remain unchanged. Scratch stores no Guest/Level references and uses tuple coordinates, with no width-linearized keys or result cache. The visitor/happiness grouping and all other map layers remain unchanged.

A warmed Release .NET 10 synthetic 1500-visitor/32×32 map draw with tiered compilation disabled allocated 327,656 bytes/frame before and 176,672 after: 150,984 fewer bytes/frame. Satisfaction enabled minus disabled fell from 152,240 to 1,256 bytes/frame, while the old isolated grouping measured 150,984. Actual UiBatch output tests preserve ordered reaction rectangles/colours through live movement, thought/state/bounds changes and off/on/empty redraws. These are managed allocation measurements, not browser timing, GPU results or original overlay parity. Remaining visitor/happiness grouping allocations require a separate measured slice.

Independent scratch tests compare every visitor state/thought and fixed-point edge against the prior grouping, including empty/negative and very large numeric bounds. Reordered and replaced sources, non-observed first encounters, disabled frames and empty redraws preserve fresh counts and order. The returned scratch list is borrowed for the current draw and changes at the next rebuild; its collections retain peak capacity for the view's lifetime, without retaining visitors.

### Explicit theme continuation

Entering a TPW theme with a readable matching application-owned world save now offers **Continue saved park**, **New park**, and **Back** (`[EXT:theme-continue]`, host UI). Continue uses the existing `opentpw-{theme}-world.json` manual slot, preserving its start kind, Full Simulation/Instant Action mode and easy-layer choice. New opens the existing mode chooser and leaves that file alone until a later explicit manual Save. A theme without a valid matching slot keeps the existing new-game mode flow. There is no autosave, profile progression/key gate or native-save writer in this ingress.

The menu reads the existing own codec's structural metadata; it does not load or hash game assets. Activation re-reads the trusted generated slot and rejects missing/corrupt files or changed theme/start/mode/easy metadata. The existing `ResumePark` performs complete trusted-asset validation and builds the staged candidate before old-world retirement. Failure shows the existing-style error dialog and preserves the live world and manual slot; it never silently starts an empty park. The ordinary Load picker and native original-reference import remain separate. TPI's layout checkpoint continues to start a new experimental session and is never offered as TPW continuation.

Synthetic tests drive actual Enter/Continue/New/Back dispatch with a tiny own-world file, pass its restored economy and saved clock/guest metadata through the real continuation handler, and exercise the real resume path refusing missing synthetic assets before replacement. Other-theme, deleted/corrupt, changed-mode/easy and TPI refusal controls preserve live references. Original-data/GPU/browser resume and native profile/theme-entry parity remain unqualified; retirement callback exceptions retain the existing staging limitation.

The Load chooser owns its dispatch: Back/Escape or right-click cancellation wins
over activation in the same input batch, and combined mouse release plus Enter
loads at most once per update. Removed chooser list, Load and Back callbacks
cannot load stale entries or remove a fresh screen. Separate valid updates and
a retry after a refused loader remain available; selected entry references and
metadata are unchanged. These are bounded host chooser rules (`[EXT:load-chooser]`), not
changes to generic screen input or qualified native navigation.

### Host terrain walk (C)

In a Theme Park World terrain park, press plain **C**, then press the terrain to enter the host first-person inspection camera. Press **C** again or **Escape** to cancel entry or return to the exact previous park camera instance and pose. A ride entered from walking returns to that walking camera; leaving the walk then restores the original park camera. Read-only visits allow this inspection without park edits. Theme Park Inc and the terrain-free sandbox refuse entry.

WASD moves in the horizontal view direction; the rotation keys turn, and holding the right mouse button and dragging looks around. Movement samples the existing bilinear heightfield and refuses outside-terrain or hole cells. Host tuning is an eye height of 1.6 engine units, speed 4 units/second, turn speed 90 degrees/second, 180 degrees of mouse look per viewport width/height and pitch limited to ±80 degrees. Frame delta is capped at 0.1 seconds. These values, flat Z=0 plane entry picking and field-of-view 55 are host choices (`[EXT:camcorder-first-person]`: the original's C key is the unported camcorder, see UI-042). Picking is not a ray/terrain intersection; the resulting X/Y is sampled on the actual terrain. There is no object/path collision, photo capture, recording, native walking physics or camera save continuation.

Typing, overlays, captured mouse/keyboard input, ride view and build/removal/cell/coaster tools block entry. Both Control keys exclude C and terrain-click entry; existing Ctrl+C park closing stays separate. Blocked C holds and mouse presses are consumed through release, including current-frame injected keys and a hold spanning new HUD creation. This input snapshot is host ownership support, not native input parity. Walking owns park tool/selection clicks and the exit frame. Camera controls freeze under modal/captured input and while a replacement park is building. On level disposal, the owned camera is released without replacing another world's camera.

Independent CPU controls in `FreeWalkCameraReviewTests` additionally pin nonplanar
bilinear height sampling and hole refusal, raw snapshot stability without widening
Ctrl bindings, RMB drag reset across capture/text/modal/focus gates, actual park
closing and read-only refusal, and visitor press ownership with fresh-click recovery
after walking exits. Guest canonical state remains unchanged by these camera actions.
These synthetic controls do not qualify native terrain collision, pixel picking,
renderer/audio behavior or original camcorder equivalence. Whole `Level.Dispose`
remains source-reviewed because its renderer teardown needs a live renderer; the
actual walking release and ride-return methods are covered independently.

### Selected shop and sideshow price

In an editable Theme Park World park, the shop and sideshow windows show their price box (tp.exe `0xc085`, `0xa08f`) with the window's price; `b_minus` and `b_plus` step it by one within 0–500, and a click on the box opens a modal editor that starts with the current mapped instance price. Enter a decimal whole value from 0 through 500 (the price boxes' range, `0x4b0735`, and the range the original loader clamps a saved price to) and press the existing OK button or Enter to apply; Escape, right click or the exit button cancels; cancellation takes priority over a simultaneous confirmation. Invalid input remains in the editor with a localized explanation. Typing and cancellation never change the price.

Apply revalidates the selected object, its live Level economy-instance mapping, category, and the exact economy/record captured on opening. Deleted, remapped, replaced or read-only targets are refused. Editing changes `Price` at once and the window's own price with it, so the window's apply on close keeps it: no immediate payment, open/close command, quality/cost-of-goods or prize-chance change. Existing visitor-payment and economy save/restore paths consume and retain that price. Two copies of the same catalogue item remain independent. Captured input, text entry, overlays, building/removal tools and ride/non-orbit cameras block entry; a retained hidden/retracted or retired dialog action cannot apply. Theme Park Inc/no-economy inspection and rides have no price action. Ride tickets and park entrance fees are separate from this shop/sideshow API.

`[EXT:shop-price-editor]` qualifies the typed editor behind the price box and at most ten-digit invariant decimal validation. A bounded larger text buffer preserves oversized pasted numbers for rejection instead of silently applying a truncated price. The original's box has no typed entry; the window's other settings are its own controls (below). No new price formula, balance policy or save schema is introduced.

Independent CPU controls drive mouse entry and Apply for the second of two identical catalogue owners, then verify its actual visitor payment, instance statistics and ledger; an unaffordable visit leaves the ledger unchanged. Separate controls retain staged input across mouse/keyboard/text capture and retry after capture ends, and check all six localized error bindings with invariant decimal validation under a French process culture. These synthetic controls do not qualify original fonts, GPU drawing or native price controls.

All Attractions shows the original per-category columns (tp.exe tables `0x750ab8`/`0x750ba0`/`0x750ca0`): the mapped ride users and shop/sideshow customers last month, profit last month and total profit come from the exact economy instance, the bars (excitement, state of repair, remaining life, satisfaction) use the object window's values, and features show the number owned of their type. The HUD takes each row's cells when the window opens, the category or order changes and every 2 s (the original's timer `0x80083`), keyed by attraction ID; cell reads do not revisit the object list or hold mutable economy records. Counts use invariant digits (centred), profits the existing localized money formatter (right-aligned). Read-only visits, TPI, missing economies and mismatched bindings retain the unavailable fallback. This adds no payment, monthly rollover, RNG, save fields or native accounting/UI equivalence claim.

The existing money formatter computes signed integer magnitudes through exact decimal conversion so accepted signed 64-bit profit values, including the minimum, cannot overflow while being displayed. Currency labels, invariant grouping and accounting values are unchanged.
