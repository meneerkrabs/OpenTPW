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
  (`cat_globallobbysfx` 4) plays when the park starts, except in Instant Action (game type 2, UI-942).
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
  pitch (the same slot-7 row factory, `0x4a02b0`, UI-044), so seven whole rows fit (`0x663718`); every opening sorts by lender name
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
  empty frame when the rate falls to zero; there is no progress fill. The fluids step at the 60 Hz reference rate
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
- Staff window (`ParkHud.Staff.cs`, Mac table `data:0x4a1e0`): `b_allstaff` opens the `w_big` window over the buy window.
  The authored `f_staffpic`, `f_staffinfo`, `f_balance` and `hirestaff` frames initially show applicants from
  `ParkEconomy.Staff.Candidates`. The five role buttons filter applicants (help 154–158), the name and wage headers sort
  them (help 159/160; the sort is kept for the session), hovering a name selects it (the details follow the pointer, as in the video; the employee view, an extension, only highlights) and a left click on a name hires the
  applicant through `ParkEconomy.Hire` (help 161; list message 0x400 → `0x49b988`, which here starts the Place tool;
  Enter hires too, `[EXT:staff-enter-hire]`). The video never shows a click on a name; the original's own advisor line ("click on the name of the person you want to hire", 11:54) and the list's 0x400 message (`0x6655a2`/`0x6656fa`) settle the trigger, so UI-083 is retired. The list scrolls (UI-080); the details are a Vaardigheid gauge (UI-081) and a mini balance of the last closed month, taken when the window opens, that includes the selected candidate (hovering a name selects it, a left click hires; tp.exe `0x49c53d` reads the newest entry of the monthly sample rings), the portrait box shows the selected role's sprite-bank figure walking on the spot, facing direction 5, as the original's figure script `0x74f758` does (its drawn size is UI-082).
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
  a visitor's info open (a row of the All Visitors window clicked, or a visitor picked in the park) locates that visitor at
  once, as the original does with its visitor info window (`0x4817c0`), and Ctrl+S or `b_locate` with the per-employee
  window open locates its employee (`0x481790`, `0x4b5f07`); without an actor the keys keep the strip with its
  last name, as the original does (UI-091).
- The All Attractions and All Visitors windows' lists (attribute 0x291) follow the original's list control: the pointer
  highlights the hovered row (the original selects it), a left click selects it and opens the row's info panel (the info arm for an attraction; a visitor row is
  selected and becomes the visitor Ctrl+V locates, as the per-visitor window `0x4b79b0` is not ported, UI-910) and keeps the list, a right click centres the camera on the row and closes the window
  (list messages 0x400 and 0x402, tp.exe `0x6655a2`/`0x66563d`; button 0 is the primary, left button: DirectInput button 0 becomes code 0xf0, `0x634963`, `0x486ca1`).
  Enter and the select/go-to buttons keep the older select-and-close behaviour (`[EXT:overview-select-button]`).
  A release is a click only within 500 ms of its press with at most 6 px of movement on each axis (`0x65f6d1`,
  `0x65fa33`; `UiInputSource`); a longer press or a drag is no click. Each list keeps its last sort for the session
  (`ListSortMemory`, the original's global sort words, `0x6659af`).
- Map window (`ParkHud.Map.cs`, Mac table `data:0x5265e`): the HUD `b_map` button opens `map_win` with its table-positioned
  pan, zoom, object-category, layer and close controls. The shipped per-theme `levels/<theme>/2dmap.tga` supplies the
  terrain image. Current path and queue cells and placed-object footprints are drawn above it. Each category button toggles its own layer (independent bits, each category drawn from its own bit; only the rides layer is on when the map first opens, the original's layer word 1, tp.exe `0x5f06a9` and the map table `0x774da0`; UI-086 keeps the colours); zoom steps half a unit within 1.0–3.0 and a pan moves the centre one map cell, independent of zoom. Live cells use the original's grid on the 512-pixel image: 15 + column × 5 and 42 + row × 5 pixels for a 96 × 85 park (tp.exe `0x5f2380`), one pixel left of and above the art's own terrain cells. The generic sandbox draws its grid
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
  quad); the HUD samples the visitors' mean happiness twice a second
  (`GuestStatistics.AverageHappiness`) and draws it as a rising red→yellow→green
  fill in the dish, empty while no visitor is in the park (UI-901). Sampled in
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
| Finance (`ParkHud.FinanceWindow.cs`) | 1/3/12-year periods, eight series toggles (bank balance/park value exclusive), loans/staff-cost/entry detail buttons, exit | wired | `FinanceWindowTests` |
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
| UI-003 | `UI/Original/UiModel.cs:60` | button/arm state frames drawn at the root node pose; child translations ignored | the PC UI mesh draw (the selected state mesh → local matrix → ancestor composition, not located in the PC round), or a capture of a button whose state child is offset |
| UI-004 | `UI/Original/UiModel.cs:124` | UI model triangles drawn back to front by Z, grouped per texture; the original's display object divides its depth buffer into a UI range in front of a linear and a log range (the dump 0x5833c0 prints [+0x48] begin, [+0x4c] ui end / linear begin, [+0x50] linear end / log begin, [+0x54] log end; the buffer itself is enabled or not, strings 0x768988 and 0x768958), which suggests depth-tested rather than sorted UI parts, not shown for the UI model draw | the UI model draw call (which depth state and which order it submits) and whether the depth buffer is on for it |
| UI-006 | `UI/Original/UiText.cs:102` | text colours: window titles and the lobby island name yellow, the object windows' labels and values white, the pause menu's lines cyan (white while focused), green values elsewhere, grey disabled, shadow and backdrop colours (the video's 720p chroma is good to about +-15 per channel); the original gives each text control an RGBA colour (0x65c5d5 → renderer +0x48, kept as ARGB at +0x78, 0x48fd07) and renders its glyphs white (0x48fa96) unless the control keeps the font's own colours (+0xdd); the values traced are the options labels' (0,0,0,255) (0x4a441e, UI-037), the buy list rows' (255,255,255,255) (0x4ad340), the pause menu lines' (0,175,190,255) (0x48b2b9) and the shared title colour (234,239,102,255) (0x492d20, fifteen window builders), with the lobby island name's second line (252,232,7,255) (0x4b8604) and a list row control that turns (140,0,60) on message 0x403 with a negative argument (0x4a0710); about a hundred more 0x65c5d5 calls (white, black, red (255,0,0) and (224,86,86) in the 0x4aa480 window) are not yet assigned to screens | which screen each remaining 0x65c5d5 call colours (values, disabled states, costs) and the backdrop colours |
| UI-007 | `UI/Original/UiWidgets.cs:96` | one-pixel (×UI scale) drop shadow under UI text; the original's control text renderer (0x48f830) lays each string out and draws it once (one 0x6ae0f0 call into the control's bitmap), so its controls have no drop shadow; the whole glyph layout and draw library has one draw entry, 0x6ae0f0, whose only caller is the text box routine 0x492750 (three callers, each calling it once: the label and list text of 0x4a84c0 through 0x492c40, 0x480810 through 0x47fcd0 and 0x480460, 0x480570 through 0x47fe90; 0x6ae1c0 is the measure entry used by 0x4929f0), no offset or second pass exists there, and the BF4 glyphs carry only 0-15 coverage samples (docs/BF4-FONTS.md), so no shadow is baked into the font art; the capture's apparent shadow is not explained, and the vtable-reached control renderer 0x48f830 and the sprite blit of its bitmap (0x48f410) are not shown to add one | the sprite draw of a text control's bitmap (0x48f410 → 0x588c20) and whether a capture's shadow is a render-state effect (UI-037) |
| UI-008 | `UI/Original/UiWidgets.cs` | text buttons on purple_button art: each half is one end cap drawn with its mirror image; upper half normal, lower half focused/pressed | capture of the original front-end buttons |
| UI-013 | `UI/Original/UiDialogs.cs:9` | the confirm dialog follows table 0x7583e8, the game-mode screen the name dialog 0x753f50 and the load screen the load dialog 0x7523f0, whose rows show the park's name and, right-aligned, the save file's local date and time as 0x49ec80 formats them (UITEXT 0x1c0 with day, month and year, then " %02d:%02d"; shipped parks and the sandbox have no file time and show none); the lobby's and the park's pause menus lay out their lines as 0x48c150 → 0x492f60 does (text width + 1 and height + 5 pixels on the canvas, centred, from top 10, 5 units apart), but the height follows OpenTPW's menu font tier ([EXT:ui-font-tier-by-scale]) with its text layout box standing in for the measure 0x4929f0, the online state's top of 0 (FUN_005b6450) is not followed and the focused line turns plain white at once instead of the original's ramp: the line procedure 0x48b2a0 starts it on message 0x10001 (read as the pointer entering) and 0x48b220 steps it on every message 0x1e, once per drawn frame (eight frames at the 60 Hz reference, [EXT:frame-driven-at-60hz]), from grey 0x21 in steps of 0x20 to white, then idle, while message 0x10002 restores (0, 175, 190) | the measure 0x4929f0, and a capture of the ramp (what 0x10001 and 0x10002 are is read, not confirmed) |
| UI-018 | `Client/GameFlow.cs`, `World/Lobby/LobbyEffects.cs`, `World/Lobby/LobbyFlyingMeshes.cs`, `World/Lobby/LobbyScene.cs` | the lobby backdrop is painted flat with the cloud layer's eased colour (red toward the blue target) as a stand-in: the original clears to the fog colour 0x44DDFF and tints only its cloud layer (the engine sky's sky.tga, coloured from that grid), which is not drawn, nor is the scene's fog; two settings gate the eased colour and the flying-mesh count share, all in the .bss settings block 0x785840-0x785920 filled at runtime ([0x785888] > 1 and the options word 0x78d8d8 == 1 assumed to pass, as the capture's tinted clouds show; [0x7858dc] assumed 100 %, the video's flocks cannot be counted at 720p); an island without SKYCOLOUR gets black instead of the random target its constructor draws (0x5dfb4a-0x5dfb7b, three CRT rand() calls per island); the island clips are picked by a local stand-in for the C runtime's rand(); while the globe is up the lobby's flyers and island clips hold still (the original's behaviour there is not traced); the enter sequence is built (the camera turns to the island angle + π, the gate plays M1, the camera zooms under the black fade and the park starts below radius 8, 0x5e0490 states 1 and 2; an early leave's gate M2 close, 0x5e1350, is not reachable from the front end here); the key-unlock sequence is not traced; the lightning flash is not drawn (the capture shows none) | the engine sky's draw (0x585ee0-0x586a00) and fog, the runtime contents of the settings block 0x785840-0x785920 and the options word 0x78d8d8, the lobby's subsystems while the globe is up, the key-award animation's code |
| UI-024 | `Hud/ParkHud.ObjectWindow.cs`, `Hud/ParkHud.ObjectSettings.cs`, `Hud/ParkHud.ObjectGauges.cs`, `Hud/ParkHud.BuyWindow.cs` | the object windows (tables 0x755150, 0x755908, 0x755e80) draw their controls at the table rectangles, but the preview is OpenTPW's orthographic icon; the shop and sideshow customer satisfaction gauges (0x4e1e30 over a 30-entry history at +0x3bc, which OpenTPW's guests do not keep) are not simulated and draw empty; the local happiness gauge counts the guests on park cells at the object's anchor cell where 0x4cd880 walks the thing lists of the cells around the thing's own cell; the build window fits the preview size to translated names and prices | the satisfaction history |
| UI-025 | `Hud/ParkHud.MessageTube.cs` | the message tube holds up to five messages (not saved with the park) (the most the video shows) and drops the oldest on overflow; the panel's rectangles are table 0x7501a0; its action buttons take the mb_ model of their kind, but only the challenge offers carry actions (Accept and Decline) and which other original message uses which mb_ model is not traced; the strike messages (TAG_SYSTEM.str) and the challenge offers use the tube; its red body and white icons are OpenTPW's art ([EXT:message-tube-art]), an unanswered offer that was deleted is posted again ([EXT:challenge-offer-repost]) and OpenTPW's own feedback lines stay in a status line ([EXT:status-line]) | the original message queue (capacity, overflow, the events that post messages) and the mb_ model per message type |
| UI-026 | `Hud/ParkHud.BuyWindow.cs`, `Hud/PreviewIcon.cs:14` | build icons: the preview model (P<name>.MD2, else the main model) is drawn by OpenTPW's CPU projection ([EXT:preview-icon-cpu-projection]); the buy window takes the original's 45° tilt (0x468e50), its turn of 0.0004 rad a millisecond that runs on across selections (0x46908a, 0x468e2f) and its fit (0x4689f0), but the original draws the preview through its 3D object pipeline (the ride object made by 0x463060 with a world matrix that 0x468e50 rebuilds each frame: the yaw and the ±π/4 tilt through the sine table, the matrix's x column scaled by 0.75 and its depth column by 0.1, then offsets) and starts the model's animation 5 (0x4732a0) and draws the footprint grid of the selected item (8×8 cells coloured by cell type) in control 0x1eb (0x4ab1b0), which OpenTPW does not; the object windows' previews keep a 30° tilt and 0.8 rad/s; sign faces use the object's rendered sign textures (ObjectSigns) | the camera and projection the 3D pipeline draws the preview object with, the offset terms of 0x468e50 and the meaning of animation 5 |
| UI-027 | `Hud/ParkHud.cs` | a park click selects the object occupying the cell of the cursor's terrain hit (TerrainRayPick: 0x45bf90's heightfield triangles, the cell 0x45d560); the original selects a ride, shop or other object by its pick sprite: the sprite pass 0x5890e0 picks the nearest sprite flagged 0x200 under the cursor (a circle round its projected anchor), 0x532bd0 finds its owner among the things of the 3 x 3 cells round the hovered cell (types 1, 4 to 8 and 0x12), the hover handler 0x486d90 turns a hovered type 5, 4, 6, 7 or 8 into cursor mode 8, 9, 0xa, 0xb or 0xc and the click dispatcher 0x4879d0 opens the object window 0x4b6080 for it, only outside the visitor pick state (Shift alone or the visitor locator panel out); the ray's object hit [0x7a1cd4] selects nothing: its only readers copy it to the camera control block 0x79c6ac (0x443aab, 0x443d23) | which model instances carry a pickable sprite (flag 0x200; the instance word +0x114 that 0x475ef0 tests) and that sprite's position, size and scale, to build the sprite pick for objects; the object half of the ray pick (RIDES-021) |
| UI-031 | `Hud/ParkHud.cs:350` | exactly Ctrl at the placing click keeps the build tool armed (0x5253b8) and at the last click of a ride's queue re-arms it, the tool's angle returns to 0 when it is left (0x52f3b6); the coaster and water-track tools, whose original sub-modes are not traced, end the build tool whatever the modifier, and the Ctrl tests at 0x5259e2/0x5259f4 are not mapped to an OpenTPW action; Level.PlaceObject owns purchase/guest linkage and its removal handler owns scrap credits | what 0x52fab0 tests (an entrance or queue, medium confidence) and the coaster and track sub-modes' continuation |
| UI-032 | `UI/Original/UiWidgets.cs:179`, `Hud/ParkHud.cs:590` | catalogue names greedily wrap in their slots; the original's formatter (0x6aec80) wraps greedily: a line ends at the last break opportunity (glyph flag 0x10) before the glyph that overflows the width, dropping trailing spaces (flag 2), or before that glyph when the line has none; longer single-line labels fall back to the small font ([EXT:fit-button-text]) | which characters the formatter flags as break opportunities and spaces, and whether the catalogue slots are multi-line text controls |
| UI-034 | `UI/Original/UiImages.cs` | a fully opaque texture on a transparent (flag 0x2) model slot keys out black; only `ipan` in the lobby `f_lobbutbg` panel | the original's render state for flagged texture slots |
| UI-036 | `UI/Original/UiWidgets.cs` | options slider: ball centre moves linearly over the track for value index 0..steps-1; click/drag sets the nearest step (OpenTPW's own steps, [EXT:options-slider-steps]); the original's slider class (vtable 0x705b20) places the thumb at track left + (value - min) × (track width - thumb width) / (max - min) in integer units (0x66b088), sets the value from a drag as min + the truncated (thumb left - track left) × (max - min) / (track width - thumb width) (0x66af19), moves one page (+0x148, default 1 from 0x66adc9) toward a click on the track (0x66b8aa) and one step on its arrows (message 0x100) or by the wheel delta (0x1000d); the options page gives its sliders the range 0..100 (0x4a455a) and sets the resolution and quality sliders to index × 50 (0x4a3d02, 0x4a3d23); the options page's change handler (0x4a2e90) turns the slider value v into the resolution index (v × 2) / 100 (0x423b60) and the quality index (k × v) / 100 with k 2, or 1 while the options word 0x78d8d8 is 0 (0x423ba0), and passes the music, effects and speech sliders on as the raw 0..100 volume (0x51bba0, 0x51bb80, 0x51bbc0, 0x423b40) without snapping the knob | the knob's position after a drag for the 0..100 volume sliders and what a track click does there |
| UI-037 | `UI/Original/UiText.cs` | option bar label colour (16,16,48), no drop shadow; the original's options labels (0x4a43b0) and slider labels (0x4a4490) are text controls given the colour (0,0,0,255) (0x4a441e, 0x4a44fe) and font slot 6 ([0x7537b4]) and drawn once without a shadow (0x48f830); how the control's colour reaches the white glyph bitmap (0x48fa96) when it is drawn is not traced, so the capture's near-black is kept | the sprite draw of the control's text bitmap (0x48f410 → 0x588c20) and whether it tints with the colour at +0x78 |
| UI-039 | `UI/Original/UiWidgets.cs` | option label size follows OpenTPW's fit rule ([EXT:option-label-fit]: letter box about 58 % of the label rectangle height, shared per page, a label whose widest value does not fit drops alone to the largest size that does); the original's options labels take font slot 6 of the bank ([0x7537b4], 0x4a443c: GAME5AA/GAME7/GAME8/GAME9 for banks 0-3) and their setup (0x4a43b0) does no fitting | what the original does with a label that overflows (UI-032) |
| UI-041 | `Client/GameFlow.cs:147` | Load Park takes no game type from the loaded park: the original derives GameType from flag bits 0x2000000/0x1000000 of 0x7a1a8c (0x550ca0), set by a front-end toggle (0x4bbf63), and the Mac saves none with a park | whether a park file of the PC build can carry the game type (not traced) |
| UI-042 | `Hud/ParkHud.CameraPanel.cs`, `Online/OnlineScreens.cs` | the camera button's slide-out panel (table 0x751720) slides in and out as the arm drawer 0x4a14f0 does (ten 20 ms steps, cat_ui 0x1C and 0xC2, help 0x131), but its slide ends at the table rectangle where the original ends at the HUD window's right + 50 with the panel centred on the arm (0x4a15d2-0x4a1650); the camcorder button arms the C-key walk ([EXT:camcorder-first-person]) where the original's button 99 enters the camcorder at once (0x498ad0 → 0x481a10), and the walk's movement and look controls are not traced (the original's eye is terrain + 5 units, 0x42b1c0); the postcard window (table 0x7540d8) keeps its photo frame empty and its preview and address book buttons disabled (the 640×240 camcorder photo 0x4a9280, the preview redraw 0x4a84c0 and the address book 0x4a8140 are not ported); the view cycle is a third button ([EXT:camera-view-cycle]) | the HUD window's rectangle; the walk's key bindings (action table 0x787c14) and the time unit of its turn step; the photo, its preview and the address book |
| UI-044 | `Hud/ParkHud.BuyWindow.cs` | buy window list rows are 41.4 units high (measured at 720p, +-1 unit), ten rows show and the wheel scrolls one row; the original's pitch is the height of the list's first row control (0x663718 via 0x65d865, frame insets removed), which the row factory 0x4ad240 makes once from font slot 7's line height (BF4 header byte 5, 0x6b05e0) × 1536 / screen height + 6 (cached at 0x7cc200): 42 at 512×384 (GAME6AA, 9), 41 at 640×480 (GAME8, 11), 39 at 800×600 (GAME9, 13) and 34 at 1024×768 (GAME10, 14); the loans list's row factory 0x4a02b0 uses the same rule, so its rows take the same pitch; OpenTPW keeps the measured pitch because its output sizes have no original font bank | a bank rule for OpenTPW's output sizes (UI-002) |
| UI-046 | `UI/Original/UiGauge.cs`, `Hud/ParkHud.BuyWindow.cs`, `Hud/ParkHud.ObjectWindow.cs` | the gauge bars (buy, ride, shop and sideshow windows) take the original's values and shape: the integer (value << 10) / 100 of a 0..1024 range (0x4abb00, 0x4ade40), a bar of the control's cell less 10 units top and bottom (0x485e30) that shows the art cut off at the value with the rest of the cell the track (0x478400: ridestatbar.wct's top 16 texel rows the bar, its bottom 16 the track); the colours of the bar and the track are read off the 720p video (a 70 % bar runs red, orange, yellow to lime green) because the decoded art does not show them (UI-106) | the gauge sprite's colours as the original draws them (UI-106) |
| UI-047 | `Hud/ParkHud.cs` | which OpenTPW tool state corresponds to the mode-object types that show control 48: type 3 (the tool-mode object, vtable 0x6fe9e0) wraps every build tool, so the held blueprint's price, the path or queue line's cost so far and the coaster track segment under the cursor show it; type 8 is the coaster panel's mode object (vtable 0x6feaa0, built by 0x46cfc0 when a panel button is pressed, 0x497bc0), which shows it only while bit 0 of 0x79c658 is set (0x447990; set at 0x441a5d), a coaster sub-state OpenTPW does not track, so OpenTPW shows the coaster cost in every sub-mode; no mode-object class returns type 4 (the classes 0x6fe980-0x6fead0 return 7, 3, 1, 5, 6, 8 and 9; 5 and 6 are the staff place and carry tools), and the staff and patrol tools show nothing | what bit 0 of 0x79c658 stands for in the coaster tool |
| UI-050 | `UI/Original/UiGauge.cs` | the buy list's third column draws a tick box per row, ticked when the park owns at least one of the item; the box and the tick are drawn with rectangles and strokes in the video's colours, not the original's art (i_boxtick.MD2 holds only the empty box) | the original tick art and the unnamed control 491 |
| UI-051 | `Hud/ParkHud.cs` | the ticket counter is the park's available tickets, where the original shows the player's earned tickets less the spent count (0x5af5d0), because an in-park ticket purchase does not reach the player ledger | an in-park ticket purchase recorded in the player ledger |
| UI-060 | `Hud/ParkHud.FinanceWindow.cs` | finance graph line colours, scaling and default selected series use the tracked monthly ledger values | original financial graph capture and its sample/render handler |
| UI-064 | `Hud/ParkHud.ResearchWindow.cs` | the research workload knob is the original's slider: values 1023 (top) to 0 (bottom), a limit of ((100 - c) / 3 + c) x 1024 / (MaxResearchersInPark x the grade-4 ResearchAbility) from the researchers' summed ability c, a workload of 70 + 30 x value / limit and the ball's travel (0x4a96b0, 0x4aac80, 0x66b088); the pointer sets the value whose ball centre lies under it, which stands in for the slider class's own pointer handling; the knob's child 0xd2d12 (set to the limit) and 0xd2d11 (state (2 x step) / 7) are not drawn; the slider's ball proc 0x66bb9b moves the ball by the pointer's offset from the press point (stored per button at 0x10005 in 0xfaa59a/0xfaa59c) from the rectangle saved at the press or release (0x65cf3d), clamped to the track by 0x66b771, then reads the value back with 0x66af19, where OpenTPW centres the ball under the pointer | what 0xd2d12 and 0xd2d11 draw, and whether the saved rectangle is the press-time ball |
| UI-080 | `Hud/ParkHud.Staff.cs` | staff list has ten equal rows in the table content area, where the video's hire list (12:00) has a pitch of about 19 px at 720p (about 40 units, not 47) and so shows about twelve rows: the original list class takes its row pitch from its font's text height (0x663718: 0x65d865 into +0x170) and shows (bottom - top) / pitch rows (0x663786); the wheel's one row per notch is the original's (0x665c35, 0x46b600) | the staff list font's text height |
| UI-081 | `Hud/ParkHud.Staff.cs` | applicant details: the skill gauge is a type-9 gauge set to the candidate's grade byte (+8, written by 0x507600 as AvgGradeOf* + R % 3 - 1 stored as 4 when above 4, OpenTPW's Grade) x 1024 / 5 (0x49b8e2) of its 0..1024 range (0x66d885 clamps), so a grade-0 candidate's value is 0; OpenTPW fills (Grade + 1) / 5 because the video's cheapest ($60) candidate shows about a fifth (12:00); the gauge models crop their art to value × 100 / 1024 per cent (UI-046), so a byte of 0 draws an empty bar and the fifth is not explained by the drawing | which byte the video's cheapest candidate holds (the writer 0x507600 stores AvgGradeOf* + R % 3 - 1 as a byte, a wrapped 0xff becoming 4) |
| UI-082 | `Hud/ParkHud.Staff.cs` | the portrait box's figure (the role's sprite-bank character walking on the spot, script `0x74f758` from `0x49ba00`) is drawn one sprite pixel to 3.2 canvas units, where the original sets its scale register to 8.0 (`0x4755d0`) and the screen-sprite path's pixel size is untraced | the engine's screen-sprite quad size, or a capture of the hire window |
| UI-086 | `Hud/ParkHud.Map.cs` | paths, queues and placed objects use plain coloured cells over the shipped theme map image; the original colours each cell from tables at 0xf86a28-0xf86c20 that are filled at run time (the per-object colours through 0x5f09a0 from a value) and outlines a cell where its neighbour differs (0x5f2380); the base colours are immediates written by 0x5f0700 (black, white, green, yellow, blue (0,78,254), cyan, dark blue (11,66,110) and gradient triples built by 0x5f0970), and the draw 0x5f2380 switches on the cell kind (1 path, 2 queue, 3 object, 8 ...) with the layer mask at +0x18, taking an object cell's colour from 0x5f09a0 over the gradient of the value read from the thing (0x4e0860, 0x4e1e30 by layer mode 2 or 3) | which table entry and gradient value each cell kind and layer mode of 0x5f2380 uses, and the outline rule |
| UI-091 | `Hud/ParkHud.LocatorPanel.cs` | the locator panels take staff newest first (by id, descending) and visitors by id, where the original walks the role's thing list (+0x210) and the thing list (0x4824d0); the camera snaps to the followed actor every frame where the original eases it (0x486860); a staff member's patrol rectangle is not marked (0x536100 in 0x4b3640); the panels appear without sliding and without the sounds 0xc6 / 0x11f; a picked-up member's state frame 9 (0x485ec0) draws the first frame; the staff window's own locate buttons focus actual actor positions, the list rows' 0x402 path (0x4867b0) is the original's; the visitor info window that Ctrl+V tests ([0x7cc418], window object 0x7cc408) is not ported: a left click on an All Visitors row or a visitor picked in the park stands in for opening it and it closes with the All Visitors window, where the original's stays open on its own | the order of the role lists, the easing of 0x486860 and the patrol markers |
| UI-100 | `UI/Original/OriginalPointers.cs` | the link and end pointers are never chosen and the cash pointer is not chosen at 0x539fb6: end (0x816d4c) is set when the path validator 0x535670 returns code 0xb (0x535ae5, 0x535b3d) and at 0x53a1c6, link (0x816d60) by 0x535670 at 0x535c72 and for code 8, a queue end whose neighbour is of kind 3 (0x535d00), and cash (0x816d5c) also at 0x539fb6; the crosshair's art (CCro.tga) is not in the game data | the path and queue states behind the validator codes 8 and 0xb, the cash pointer of the tool at 0x539fb6 and the crosshair art |
| UI-102 | `World/ParkCameraMode.cs` | the park camera has no middle-button mouse turn: while the middle button is held (message 0x10005 with button 2 sets the mode bit 0x790aac & 2, 0x10004 clears it, 0x42a760) the original adds (pointer X x 2048 / width - 1024) / 2048 to the view's yaw (0x790a38) on every message 0x1e, once per drawn frame, so the turn rate follows the pointer's distance from the screen centre (up to 0.5 radian a frame) and, in the original, the frame rate; OpenTPW turns by its keys only, at its own speed, and a built turn would step at the 60 Hz reference rate ([EXT:frame-driven-at-60hz]) | the yaw's unit and sign (0x790a38, read by the camera 0x42b1c0 and the pointer view 0x443340), whether button 2 is the middle button, and a capture of the turn |
| UI-103 | `Hud/ParkHud.Map.cs` | the map window's view does not scroll at the screen edge: the original's map view (built by 0x5f0b40 from the HUD command 0x29 of 0x4a0840) moves its centre on every message 0x1e (0x5f1130, once per drawn frame, while the flag 0x78d910 is 0) when the pointer is in the outer 1 per cent of the canvas: (pointer - 0.99) x 250 or (0.01 - pointer) x 250 map units a frame (up to 2.5, 0x5f35b0 clamps the centre to the map), so it also follows the frame rate; OpenTPW pans by its buttons and arrow keys only, and a built edge scroll would step at the 60 Hz reference rate ([EXT:frame-driven-at-60hz]) | what the flag 0x78d910 is, whether the map's view units are the cells of PanMap, and a capture of the edge scroll |
| UI-104 | `Hud/ParkHud.ObjectWindow.cs` | the Move tool is not simulated: the object windows' b_move control (0x3e2c, 0xc080, 0xa0a8 in the tables) is not drawn, where the original enables it (0x52c2b0) and its click (0x48cfa0) enters the move tool, mode 0x3b, with the object picked up | the mode 0x3b handlers (the pick-up, the placement test, the cancel, the cost of a move) and the enabled test 0x52c2b0 |
| UI-106 | `UI/Original/UiGauge.cs` | the gauge art ridestatbar.wct (32 x 32 after OpenTPW's wavelet decode: rows 0-15 a gradient from dark olive through yellow (225,255,0) to green (119,255,0), rows 16-31 a grey track) decodes without the red end the video shows (its happygrad.wct has blue texels at the left), so the video's red-to-orange start comes from a decode difference, a tint or vertex colours that are not read | why the decoded ridestatbar.wct differs from the rendered bar: the wavelet decoder (TextureFile) or the tint or vertex colours of the gauge sprite (0x477ff0) |
| UI-901 | `Hud/ParkHud.cs`, `Hud/ParkHud.Gauge.cs` | the happiness gauge shows the visitors' mean happiness as a rising red-yellow-green bar in the gauge model's empty dish | capture of the original gauge and its value drawing |
| UI-905 | `Hud/ParkHud.AttractionsWindow.cs` | the All Attractions list's bars take the object window's values (the gauge rows of the ride window) and satisfaction, which OpenTPW does not simulate, draws an empty bar | the satisfaction gauge's data, which OpenTPW's guests do not keep (ECON-057) |
| UI-907 | `Hud/ParkHud.VisitorWindow.cs` | the All Visitors window (table 0x7506c8) centres the rides column, where its cell (0x48fde0) keeps the class default alignment | the cell text object's default alignment |
| UI-910 | `Hud/ParkHud.VisitorWindow.cs` | a left click on a visitors row, and a press on a visitor in the park (UI-930), select the visitor and open the All Visitors window with it as the visitor Ctrl+V locates (UI-091); the original routes both to the per-visitor window 0x4b79b0 (list message 0x400 through 0x49351e; click mode 7 of 0x4879d0), which is not ported: layout table 0x7575e0 with the title 0x3736, the visitor number slider 0x3735, the value widgets 0x3724, 0x372d and 0x372e to 0x3731, the happiness bar 0x3732 (the float at +0x19c), the status text 0x3733 (the word at +0x220) and the buttons 0x3737 (the locator 0x4b3f60) and 0x3738 (the visitors window 0x493530), refreshed by 0x4b72a0 when its timer 0x80080 fires | the layout table 0x7575e0 (rectangles, models, help ids), which label each value widget belongs to and which visitor fields they read (+0x1a0, +0x1c4 to +0x1d0, +0x1d8), the handlers of the buttons 0x3720 and 0x3721, and the timer period |
| UI-092 | `Hud/ParkHud.EmployeeWindow.cs` | the per-employee window (table 0x756f48, 0x4b6080) shows the role's name in its portrait frame where the original plays a live figure of the employee (0x4b5330 → 0x486530); a click on the title does not rename (0x48d2e0, help 85); the help 0xd4 the window posts while the employee has a patrol area (0x4b5ab0) is not posted; the arrows cycle the economy's member list in hire order for the thing list (0x7cf56c, newest first); the locate button is disabled while the employee has no world position (held in the hand), where 0x4b5f07 always opens the locator panel | drawing the figure, porting the rename dialog, and the thing-list order of staff loaded from a save |
| UI-094 | `World/RideCameraMode.cs`, `Hud/ParkHud.RideView.cs` | the ride view is a first-person eye on the camera car of the coaster train with the smallest position (the car 0x436880 selects) or the carriage's height (prototype), looking along the track or the entry heading; the ride camera never changes the projection (tp.exe builds it once with the argument 90, 0x42a990 and 0x54e860), so its field of view is the park camera's and shares that difference (UI-101: 55 in OpenTPW against 90); the static lasts 0.33 s (video 0.30-0.35 s) | the static's timer; the field of view is UI-101's |
| UI-095 | `World/Level.RideView.cs` | leaving the ride view restores its prior camera instance, including the host terrain walk, and re-centres the host park camera on the object under its target with the original's test (0x42a340: a placed object whose type has CannotRide 0) and look direction (0x42a1d0: the link byte of the object's cell masked with 0x55); the original's park camera is its own pose (yaw, height and smoothing in 0x42b1c0) that OpenTPW's host camera does not reproduce, and whether the original's object cells carry a link byte at all is not traced (OpenTPW's cell map keeps link bytes for path, queue and track cells only, so the yaw rule fires for none of the placed objects) | the link byte of an object's map cell (the writers of +0xd outside paths, queues and tracks) and the park camera's own pose |
| UI-096 | `Hud/ParkHud.cs` | the pause menu (with the screens opened from it) and the map window stop the park simulation; the pause menu's cases 1 and 22 open 0x49efb0 and case 2 0x49f280 and case 5 0x4a3a30 (jump table 0x48bbc8 of 0x48b6a0), which keep the pause (0x49efba, 0x49f29f, 0x4a3a6c), so those callers are covered; the postcard (0x4a93d7, Ctrl+P) and the delete confirmations (the message box 0x47f020 of 0x48cd10) pause too, but the other generic message boxes (table 0x74f920, up to three b_okay/b_exit buttons, 27 call sites in 7 functions: the alert queue 0x47f620 fed through 0x47f410, the file errors of 0x4237f0, 0x4a77f0, the online notices 0x4bc500 and 0x4ec030; each pauses at 0x47f26c while the gate 0x786ba4 is 1 and 0xf82884 is 0) and the application's deactivation (WM_ACTIVATEAPP in the main window procedure 0x46b600, 0x46b74c, while 0xf82884 is 0) do not | which of OpenTPW's other dialogs (the dismissal box, whose original 0x4b5e5d is no 0x47f020 caller, the loan boxes, the restart and quit boxes) correspond to the remaining 0x47f020 call sites, and pausing for focus loss |
| UI-942 | `Audio/GameAudio.cs`, `FrontEnd/FrontEndMenu.Continue.cs` | the globe's music and ambience (cat_globallobbysfx 2 and 3) replace the lobby's while the globe is up; the PC enter-park routine 0x4b8fd0 (called by 0x5e1e30, vtable slot 0x702f04, which then posts message 6 to the lobby window [0x7cc4b4], table 0x757f60) plays global sound 0x62 (volume 50000, placed by the s_key control 0x1e0ea, 0x4b90a5) and cat_globallobbysfx 4 flat (0x4b90be) only while the game-mode global 0xfb3b7c is not 2, as on the Mac; OpenTPW plays only the second; the original's handling of the lobby's own music, ambience and sentences while the globe is up is untraced | the PC globe component (0x100923A8's twin) and the lobby's music under it |

Closed as OpenTPW design choices (October 16, 2026, approved by the user): the former
`APPROX:UI-002`, `APPROX:UI-005`, `APPROX:UI-012` and the own-window sizes of
`APPROX:UI-013` are now `[EXT:…]` labels at their code sites, and UI-025, UI-026, UI-032,
UI-036 and UI-039 keep only their original-behaviour parts as register rows. Behaviour
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
| `fit-button-text` | `APPROX:UI-032` (part) | The small-font fallback for a long single-line label. |
| `options-slider-steps` | `APPROX:UI-036` (part) | OpenTPW's option sliders step through its own presets and 11 volume steps. |
| `option-label-fit` | `APPROX:UI-039` (part) | Option labels are sized by a shared 58 % letter box with a per-label drop to a size that fits. |

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
- `UI-041`, `UI-080`, `UI-100`, `UI-905`, `UI-907` and `UI-941` keep their IDs for the original behaviour that remains; their design-choice parts are `[EXT:no-player-profiles]`, `[EXT:list-row-pitch]`, `[EXT:software-pointers]` (the pointer art drawn by the UI), `[EXT:list-selection-by-id]` and `[EXT:frame-driven-at-60hz]` (the islands and the flyers now step at the same 60 Hz).

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

A genuine left press on a visible in-park visitor opens the existing All Visitors window and selects that visitor, scrolling its row into view, in the original's visitor pick states only: Shift alone with no locator panel out, or the visitor locator panel out with no modifier (`0x487013`; the hover handler `0x486d90` sets cursor mode 7 only then, and in that state it sets no object or cell mode, so objects are not selected either). In every other state a press on a visitor acts like a press on the ground under it. Picking uses the current guest renderer's interpolated DrawX/DrawY terrain position, selected animation frame/hotspot, mirroring, camera axes and UnitsPerPixel. Overlapping rectangles choose the nearest projected depth, then the lowest visitor id; on an original level the chosen visitor must then stand on the cell of the cursor's terrain hit or one of its eight neighbours (`0x532bd0`, the offset table `0x81ED40`), otherwise no visitor is picked. A successful press consumes the existing tool/release latch before automatic path activation; a miss preserves ordinary path input. The roster's existing live selection healing applies when the visitor leaves.

UI-930 qualifies this host rectangle policy: the original tests a circle round the sprite's projected anchor in its sprite pass (`0x5890e0`: radius squared 0.0002 x (width squared + height squared) in clip units, nearest clip w wins, the first drawn keeps a tie, no alpha test), which the port does not reproduce, and partially near/far-clipped sprites are refused; transparent pixels and terrain/object occlusion are not tested. The original picks in the render pass (the pick block 0x7b05a8 is opened and closed around the draws by 0x46c100/0x46c110 in the frame routine 0x54e2f7, and the thing under the pointer lands in 0x7b05ac; the hover handler 0x486d90 reads its kind and the Shift/Ctrl/Alt state and the click 0x4879d0 opens the visitor window 0x4b79b0 or the employee window 0x4b6080 on it). No GPU parity is claimed. Hidden/removed/outside-park visitors and invalid/nonfinite/behind-camera projections are refused. Overlay/HUD controls, text entry, ImGui capture, build/removal/cell/coaster tools, ride view and its exit frame own their input. Read-only visits may inspect visitors without simulation or finance mutation. The public HUD update samples native capture flags at its existing point; an internal explicit-capture overload runs the same frame body in synthetic tests.

Visitor-specific cat_kids event 129 is deferred: the existing generic park press sound remains, with no new hover/held/release audio trigger. Original assets, native geometry/occlusion and rendered interactions remain unqualified.
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
