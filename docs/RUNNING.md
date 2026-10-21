# Running the current development build

This is not yet the complete game. The completion gates are in COMPLETION-PLAN.md.
Original legally obtained game assets are required and must not be committed.

## macOS / Linux

Install the pinned .NET 11 SDK (`global.json`). Apple Silicon uses native arm64 with Metal when
the matching libraries are available; see MACOS-NATIVE.md for reproduction.
The launcher falls back to an Intel/Rosetta bootstrap when those files are absent.
Linux currently uses Vulkan and needs a working Vulkan driver. Windows uses
Direct3D 11. Windows/Linux GPU execution is not yet locally qualified; CI draws the textured
park shaders on Direct3D 11 (the Windows runner's WARP adapter) in `NativeWorldShaderRenderTests`.

Linux (x64) needs the system packages for SDL2, the Vulkan loader and a Vulkan
driver (Debian/Ubuntu: `libsdl2-2.0-0 libvulkan1 mesa-vulkan-drivers`) and an X11
or Wayland session; the NuGet packages bring `libveldrid-spirv.so` and
`libcimgui.so` but no SDL2. Without a GPU, Mesa's llvmpipe software Vulkan works,
and `xvfb-run -a bash scripts/run.sh …` provides a virtual display for headless
smoke tests. Missing display or Vulkan driver ends with a named error instead of a
.NET loader exception. Game data may use any letter case (`data`, `Data`, `DATA`
from a mounted CD, `Speech`/`speech`): paths are matched to the on-disk spelling.
Linux ARM64 release packages include built `libveldrid-spirv` and `libcimgui`.
For development on ARM64, build these native libraries first as described in
[release builds](RELEASES.md#native-libraries); SDL2 and Vulkan remain system dependencies.
Shader hot reload (recompiling a shader when its file changes) is a development aid
and off by default; turn it on with `--shader-hot-reload` or
`OPENTPW_SHADER_HOT_RELOAD=1`. It is off because starting a file watcher on macOS
calls `sync()`, which waits for every mounted file system: one unresponsive network
share kept the game from starting. It shares one file watcher per shader file,
because every Linux watcher holds an inotify instance (default limit 128 per user);
if the limit is still reached, hot reload is disabled with a warning.

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World'
bash scripts/run.sh --game-path '/path/to/Theme Park World' --sandbox
bash scripts/run.sh --game-path '/path/to/Theme Park World' --validate-assets
```

The default start is the original-style front end (3D lobby; pick an island and
enter the park, or Load/Options/Quit Game) with the in-game HUD; see UI.md.
`--sandbox` opens the former default jungle sandbox with its ImGui developer panel.

Without `--game-path`, OpenTPW finds the game itself or opens a setup window to
choose the folder once (SETUP.md); `--setup` opens it again. The folder must contain
`data` or `Data`. `OPENTPW_GAME_PATH` can replace `--game-path`. `DOTNET` selects a particular SDK executable.
`OPENTPW_RUNTIME` selects an explicit runtime identifier.
Native dependencies must match the selected process architecture.
Engine content is copied beside the executable; shader files/includes resolve
from the executable directory rather than the shell's current directory.

## CD launcher (autorun screen)

When the game folder or the CD (`--cd-data`, saved CD) has the original `Autorun` folder, the start opens the
CD's launcher window first (Play continues, View Read-me opens the read-me, Exit quits; see AUTORUN.md). Skip it
with `--no-autorun`, `OPENTPW_NO_AUTORUN=1` or `"showAutorun": false` in `setup.json`.

## Language

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --language German --language-data /path/to/extracted-cd
```

Text uses English when installed, otherwise the installed language.
`--language` (or `OPENTPW_LANGUAGE`) picks English, Danish, Dutch, French, German
or Swedish (Dutch comes from the Benelux CD). Non-English languages other than the installed one come from the
original CD: extract its `<Lang>/data` and `<Lang>/Meshes` folders outside the
repository and pass the extraction folder (or one `<Lang>/data` folder) with
`--language-data` (or `OPENTPW_LANGUAGE_DATA`). Nothing is copied into the game
folder. See LANGUAGES.md for the 7-Zip command, defaults and current limits.

## Windows

```powershell
./scripts/run.ps1 --game-path 'C:\Games\Theme Park World'
```

## Tests

```sh
dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj
OPENTPW_GAME_PATH='/path/to/Theme Park World' dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj
```

Set `OPENTPW_LANGUAGE_DATA` to the extracted CD language folders to also run the
Danish/Dutch/French/German/Swedish string and font tests (LANGUAGES.md).
Asset-dependent tests are inconclusive when original files are unavailable; CPU
tests do not require them. An asset-test skip is not a compatibility pass.
The workflow builds/tests all three host OSes, but does not qualify GPU rendering
or full gameplay and does not download proprietary game files.

## Extensions and mods

| Option | Meaning |
| --- | --- |
| `--disable-extension <id>` | Switches an official extension (such as `opentpw.online`) or an installed mod off for this run; repeatable. An unknown id is an error. |
| `--enable-mod <id>` | Switches an official extension or an installed mod on for this run; repeatable. An unknown id is an error. |
| `--mod-input <mod id>:<input id>=<folder>` | Chooses the folder of a mod input (such as the Theme Park Inc install of a rides mod) for this run; repeatable. |
| `--mod-dir <folder>` | Adds one mod folder (with its `opentpw-mod.json`) ahead of the installed mods; repeatable. |
| `OPENTPW_MOD_DIRS` | Mod folders separated by the platform's path separator (`:` or `;`), after `--mod-dir`. |

The choices themselves live in `<config>/mods.json`; see [MODS.md](MODS.md). Official extensions switched in the game apply to the next park started or loaded; mods and edits to the file apply at the next start ([When a change applies](MODS.md#when-a-change-applies)).

`OPENTPW_TPI_PATH=/path/to/Theme\ Park\ Inc bash scripts/smoke-tpi-rides.sh '/path/to/Theme Park World'` runs the native smokes with the shipped Theme Park Inc rides mod (`opentpw.tpi-rides`)
([MODS.md](MODS.md#the-shipped-theme-park-inc-rides-mod)); it needs a window, so not while the screen is locked.

## Diagnostic model inspection

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --inspect-model '/levels/jungle/rides/totem/totem.MD2'
```

Use `--validate-assets --inspect-rides` for the Jungle archive inventory.
Runtime logs are written to standard output/error. Original RSE execution and
playable original TPWS import remain roadmap tasks, not features of the sandbox.

## Read-only original level view

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --load-original-level jungle
```

Renders the level's `terrain.wad/base.MD2` meshes and heightfield. For jungle it
also imports `Easymode.TPWI` read-only: 78 path cells (drawn with the TCT's first
path texture) and 11 placed objects plus 3 fixed items, drawn with their original
models and running their original scripts (see OBJECTS.md). The prototype Totem and
any catalog object (ImGui build panel; R rotates) can be placed only where MAP flags,
heightfield holes, the imported paths and other objects allow (see MAP.md). Sandbox
save/load is disabled in this mode and no original file is written. Money, time,
staff and path styles are not imported. Other themes load without a save.
Guests are simulated from scratch: kids arrive at the bus stops, pay at the ticket
booth, walk the paths, queue at the real entrance cells of the rides, shops,
sideshows and toilets (and the Totem), use them through their scripts and leave
([GUESTS.md](GUESTS.md), [OBJECTS.md](OBJECTS.md)).

Official bonus objects (`_name_N.wad`) are read from an extracted bonus directory
with `--bonus-data <dir>` (or `OPENTPW_BONUS_DATA`); they join the build panel and
the private tests (OBJECTS.md). Nothing is copied into the game data. Without a flag,
the folder saved in Options > Game files is used, or the copy imported there into
`<config>/bonus` (SETUP.md), so the flag is only needed for a one-off directory.
`--smoke-test` also works with this flag (captures `native-smoke-original-*.png`;
the smoke also checks that guests fill and leave the Totem and ride imported objects).

## Headless M3 gameplay-loop gate

```sh
dotnet source/OpenTPW/bin/Debug/net11.0/OpenTPW.dll --game-path '/path/to/Theme Park World' --m3-gate --minutes 30 --report m3-gate.json
```

Builds a fixed minimal Jungle park without a window or GPU (entrance, paths laid with the path builder,
Belly Bounce with a 25-cell queue laid through the queue tool's code, Drinks Shop, Small Toilet, a mechanic
and a handyman), runs 30 simulated minutes as fast as possible and prints one pass/fail/unresolved row per
invariant. Exit code: 1 when any row fails, 2 when no row fails but at least
one is unresolved, 0 only when every row passes. M3 is accepted only at exit code 0. See [M3-GATE.md](M3-GATE.md).

## Headless guest report

```sh
dotnet source/OpenTPW/bin/Debug/net11.0/OpenTPW.dll --guest-report [--guest-scale plan|extended] [--guest-report-json guests.json]
```

Measures the guests' behaviour passively (docs/SMARTER-GUESTS.md): eight fixed seeds of the synthetic park scenario and, when `OPENTPW_GAME_PATH` or `--game-path` is set, the four
themes' level parks, each with a warm-up and a measured window in in-game days. It prints a short table (outcome mix of the queue attempts, waits over boarded attempts, cohort
happiness, departures by predicate, the use Gini, revenue per guest, stuck guests, CPU and allocation per tick) and writes the stable JSON report. It opens no window and changes no
simulation state. `--guest-seeds`, `--guest-warmup-days`, `--guest-days`, `--guest-levels`, `--guest-level-seeds`, `--guest-arrivals` and `--guest-no-perf` set the scale. See
[SMARTER-GUESTS.md](SMARTER-GUESTS.md). `--guest-extension smarter-guests` (with `--guest-report`: build the run with the extension on, so `--guest-compare` can set it against the default), `--guest-mod folder|id` (with `--guest-report`: switch a mod on for the run, a mod folder or the id of a mod shipped with OpenTPW such as `opentpw.example.patient-guests`; repeatable; the table and the JSON then list each script mod's calls, steps and failures), `--guest-rules none|phase5|phase6|paths|all|planning,detour,meal,litter,crowding,bins` (with `--guest-extension smarter-guests`: which phase 5, 6 and 6b rules the run uses; `none` is version 1.3.0's behaviour, `phase5` version 1.4.0's and `phase6` version 1.5.0's, from the same build), `--guest-bins` / `--guest-bins-shops` (with `--guest-litter`: litter bins in the scenario) and `--guest-level-bins N` (a proxy bin about every N path cells in the level parks), `--guest-food` (the scenario with a food shop, so guests can eat) and `--guest-litter` with `--guest-litter-effect N`, `--guest-handyman-turns N` and `--guest-scenario-arrivals N` (the scenario with litter, a handyman proxy and a faster arrival timer), `--guest-cost` (with `--guest-report`: times the guests' own update per guest-turn for the original guests and the extension's rule sets, medians over `--guest-cost-seeds N` x `--guest-cost-repeats N` runs of `--guest-cost-days N` days, `--guest-cost-variants original,1.3.0,1.4.0,1.5.0`; Release build, a quiet machine), `--guest-compare baseline.json candidate.json [--guest-targets metric,metric]` compares two reports
in standard errors of the seed spread and exits 1 on a regression or a target that did not improve.

## Headless M4 attraction and script gate

```sh
dotnet source/OpenTPW/bin/Debug/net11.0/OpenTPW.dll --game-path '/path/to/Theme Park World' --m4-gate --report m4-gate.json
```

Classifies every catalog object of the four themes, takes each supported one through load, build, run,
stop and delete in its own headless park (player build code, charged), checks that every opcode of the
original scripts has a handler and a unit test, lists the effect hooks, and reports roller-coaster
construction, the ride camera and ride assessment. Same exit codes as the M3 gate; `--theme`, `--window`
and `--no-determinism` shorten a run. See [M4-GATE.md](M4-GATE.md).

## Read-only BF4 font inspection

```sh
dotnet source/OpenTPW/bin/Debug/net8.0/osx-arm64/OpenTPW.dll --inspect-font '/path/to/Theme Park World/Data/Language/English/SESHMED.bf4'
```

Use your actual build/RID DLL path. No `--game-path`, graphics or game-directory
writes are needed. Prints entry/sample counts and encoding inventory; decoding
errors exit nonzero. This does not integrate original fonts into the game UI.
See BF4-FONTS.md for supported encodings, limits and private fixture evidence.

Private format tests use `OPENTPW_GAME_PATH`. MTR fixtures exist only on the
ISO; extract its `*/Meshes/*/*.mtr` files to a folder outside git and set
`OPENTPW_MTR_PATH` to it to run the MTR fixture tests (see MTR.md). Theme Park Inc
texture tests use `OPENTPW_TPI_PATH` (a Theme Park Inc installation or its `Data`
folder, read in place; see FSH.md).

## Theme Park Inc level viewer (experimental)

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park Inc' --load-original-level arabian
```

Runs a Theme Park Inc world (`arabian`, `science` or `water`) as the experimental playable slice: the
world in 3D with the park camera, an OpenTPW text HUD (build window, remove/rotate tools, speed
control, pause menu on Esc) and guests under the ported Theme Park World guest rules — building is
charged at Theme Park Inc's own prices (the money runs; see [the economy](THEME-PARK-INC.md#theme-park-inc-economy-money-only)), and the slice runs no staff, research, challenges, advisor, saves or Theme Park Inc interface. P pauses
the simulation. `--capture-world out.png --capture-frames 300` writes a screenshot. The folder must
hold Theme Park Inc's `Data` folder (an installation, or the cabinets extracted as below); it is
detected by its retail `levels/Standard.sam` or by `Ver.txt` beside the Theme Park Inc theme folders.
Without a world (`--front-end`, or no mode option at all) the same data opens the slice's
[Personnel File start screen](THEME-PARK-INC.md#theme-park-inc-front-end): Enter Game starts science, and a zone
chooser offers Polar Zone and Arabian Nights. Theme Park Inc data given to any other start (a Theme Park World level,
`--sandbox`, `--smoke-test`, `--park-resume-smoke`, `--export-park`, `--import-park`,
`--visit-park`, `--front-end` together with a world) is refused with a message that points here. The explicit world command above works in every package; the desktop edition chooser (`--setup`) has been part of the releases since v0.8.0, and the browser offers explicit experimental
TPI world selection. See the [static TPI launch guide](https://opentpw.io/theme-park-inc.html) for
build availability, setup steps and limitations. Adding
`--tpi-smoke-test` (best on `science`) drives the slice's whole HUD user flow with injected input —
build window, place, queue tool, Escape ordering, pause and the pause menu's Quit — and fails loudly
if any of it breaks; on Theme Park World data it is refused. See
[THEME-PARK-INC.md](THEME-PARK-INC.md#theme-park-inc-playable-slice) for what works.

Extracting the CD: the data sits in the InstallShield cabinets `data1.cab`/`data2.cab` on the retail CD.
With [unshield](https://github.com/twogood/unshield) (`brew install unshield`):

```sh
mkdir tpinc-data && unshield -d tpinc-data x '/Volumes/THEMEPARKINC/data1.cab'
```

The files land in per-component folders; `tpinc-data/Data` is the folder the viewer needs (point
`--game-path` at `tpinc-data`, or at the folder that holds `Data`). `Data/Language` holds the five languages of the CD
(Dutch, English, French, German, Swedish): `--language Dutch` (or `OPENTPW_LANGUAGE`, or the saved choice) shows the slice's
interface and its object names in that language ([LANGUAGES.md](LANGUAGES.md#theme-park-inc)). The separate folders
`_Support_<Language>_String_Tables` hold only the installer's own strings and are not read.

## Read-only Theme Park Inc texture inspection

```sh
dotnet source/OpenTPW/bin/Debug/net11.0/OpenTPW.dll --inspect-fsh '/path/to/TPI/Data/ui.wad!stexture/tb_camera.fsh'
```

Takes a loose `.fsh` path or `archive.wad!member/path`. Prints the image size,
codes, palette and decoded RGBA hash; decoding errors exit nonzero. Theme Park Inc
is not playable; see FSH.md.

## Read-only original container inspection

```sh
dotnet source/OpenTPW/bin/Debug/net8.0/osx-arm64/OpenTPW.dll --inspect-save '/path/to/Theme Park World/Data/levels/jungle/Easymode.TPWI'
```

Use the DLL path for your actual build/RID or its published package. This command
does not require `--game-path`, create a window or write to the game directory.
It validates the supported offline container envelope and zlib stream, prints
metadata and input/decoded hashes, and exits nonzero on invalid/unsupported input.
Limits are 64 MiB each for input and decoded output. This is **not** playable park
import or proof of all original TPWS layouts. See SAVE-CONTAINER.md for evidence.

## Sign textures (debug export)

```sh
OPENTPW_SILENT=1 OPENTPW_OFFSCREEN=1 dotnet source/OpenTPW/bin/Debug/net11.0/OpenTPW.dll \
  --game-path '/path/to/Theme Park World' --export-signs Spanish /path/to/new-folder --language-data /path/to/spanish-language-data
```

A debug tool for translators and comparisons: writes every sign texture the shipped data defines, in one language, as PNG.
`--export-signs <language> <output directory>` composes, with the code the game itself uses (`ObjectSigns`, the gate's
`OriginalGateSign` lines, `LobbySigns`), for each of the four themes the park gate (its `OBJECT_NAMES.str` row, as a new park's gate
starts: English jungle entries 85 and 86, "LOST" and "Kingdom") and every object archive with a `.sgn` (its name split at the middle
space), and the lobby's four island and gate signs. Each sign gives three files: `<theme>/<object>-1.png` is the `sign1` texture (the
left 128 x 128 half that replaces the model's `sign1` placeholder), `<theme>/<object>-2.png` the `sign2` texture (the right half), and
`<theme>/<object>.png` a 256 x 128 preview of both side by side. `<object>` is the archive name (`gates`, `totem`; the lobby uses its
model names, `jun_isle`); `index.json` lists for every file the theme, kind, object, archive, `.sgn`, info id, the `OBJECT_NAMES`
string ids and their texts, the two drawn lines, the texture, the file, its size and the SHA-256 of its RGBA and PNG bytes, plus the
language, its folder (relative to the game folder, or from its `Language` folder on: the index holds no path of the user's machine) and
the compatibility profile.

The language is found as for a normal start (`--language-data`, `OPENTPW_LANGUAGE_DATA`, the saved language data folder, then the CD
folder; LANGUAGES.md); a language that is not there is an error listing the available ones, before anything is written. The output
directory must be new or empty and outside the game folder, its data, every language data folder in use (the exported language's, the
`--language-data` folder and the CD folder, also when the exported language comes from the installation) with symbolic links resolved; nothing is overwritten, and everything is composed before the
first file is written. The run opens no window and no audio device, loads no park, starts no mods and, by default, no bonus objects (only the
shipped data), and reads the game data only. One sign file depends on the compatibility profile: in the default Original profile the second
line of Space Zone's `megacost.sgn` (a font `fonts.wad` lacks) is not drawn; `--compat-profile recommended` draws it with the
`sign-font-substitution` fix (COMPATIBILITY.md). The sign files themselves are the installed game's; a CD whose levels carry other
`.sgn` files (the Korean CD) is exported by pointing `--game-path` at that CD.

Official bonus objects are exported only when asked for: `--bonus-data <folder>` (the extracted bonus content, the folder that holds
`levels` or one up to two levels above it, as for a normal start; OBJECTS.md), or `--with-bonus` for the folder a normal start would use
(`OPENTPW_BONUS_DATA`, the saved bonus folder, `<config>/bonus`); a folder whose `levels` tree holds no `_name_N.wad` archive (such as the
`__MACOSX` copy of an unpacked zip) is an error. Each theme's bonus archives with a `.sgn` then follow its shipped objects,
named by archive (`jungle/_dizzyd_8-1.png`; the 35 shipped bonus archives give 14 ride signs: Dizzy Dinos, King of the Swingers, Manic Mayan,
Slither, Ghost Train, Dare Devil, Haunted House, Bone Shaker, The Dizzy Tree, Rubber Ringos, Flying Fountain, G-Force, Whirligig and
Wormhole), with the lines the game draws for them (`ObjectSigns.Lines`): the `SIGNA`/`SIGNB` sections of the archive's `<language>.txt`, or,
without that file, the object's display name split at the middle space. Mod and extension objects are never exported. `index.json` records
each sign's `textSource` (`OBJECT_NAMES.str ids`, `OBJECT_NAMES.str gate row`, `Spanish.txt SIGNA/SIGNB`, ...) and marks bonus signs with
`"bonus": true`; the bonus folder is protected like the game data. Without either option the output is the same as before.

`--sign-texts <file>` draws chosen signs with other texts (for instance a translation table of another release), opt-in and only read.
The file is UTF-8 JSON:

```json
{
  "description": "optional free text",
  "signs": [
    { "theme": "jungle", "object": "gates", "lines": [ "El Reino", "Perdido" ] },
    { "theme": "jungle", "object": "totem", "name": "El Tótem Inca" },
    { "theme": "jungle", "object": "_dizzyd_8", "name": "Dinos Mareados" },
    { "theme": "lobby", "object": "jun_isle", "lines": [ "El Reino", "Perdido" ] }
  ]
}
```

Only `description` (a string) and `signs` (an array) are allowed at the top. Each entry names one sign by `theme` and `object`, exactly as
`index.json` does (`jungle`, `hallow`, `fantasy`, `space` or `lobby`; the archive name such as `totem`, `gates` for each theme's park gate,
`_dizzyd_8` for a bonus ride, which needs `--bonus-data`; the lobby model such as `jun_isle`), matched without regard to case, and gives
exactly one of `lines` (two strings, drawn as they are; either may be empty) or `name` (one string, trimmed and split at the space nearest
its middle, the rule the game uses for a sign that has only a display name; a name without a space is one line). No other member is
allowed; a member given twice in one object (JSON would keep the last), a key listed twice, a text with a control character or a line or
paragraph separator (a line break is given as two lines), a string that is no valid text (a lone escaped surrogate) or a file over 4 MiB is an error, and
so is a key that names no exported sign (all such keys are listed) before anything is written. Signs the file does not name keep their
text. A named sign is composed again from its own `.sgn` by the game's composer; its `index.json` entry has `textSource`
`"override (<file name>)"` and keeps the game's own text in `originalText`, `originalLine1`, `originalLine2` and `originalTextSource`
(`stringIds` and `strings` stay those of the game's text).

## Movies

Export a single decoded TGQ video frame without game assets, a window or audio:

```sh
dotnet source/OpenTPW/bin/Release/net11.0/OpenTPW.dll --export-tgq-png /path/movie.tgq 1 /path/new.png
```

The index is zero-based among video frames. The PNG uses the encoded dimensions
and the current opaque RGBA conversion, without display aspect stretching. The
output must be new; invalid input leaves it absent. See TGQ-MOVIES.md for limits.

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --play-movie bf
bash scripts/run.sh --game-path '/path/to/Theme Park World' --play-movie plan --headless
bash scripts/run.sh --game-path '/path/to/Theme Park World' --play-movie bf --smoke-test
```

Plays `Data/Movies/<name>.tgq` with SDL2 audio (`--mute` disables it); a key
press or click skips.

Automated runs are silent: `--smoke-test`, `--tpi-smoke-test`, `--park-resume-smoke`, `--coop-join-smoke`, `--smoke-presence`, any `--capture-*` or
`--perf-*` option, every `--debug-*` option (`--debug-sim-minutes`, `--debug-weather` and the other capture helpers), `--offscreen` (or `OPENTPW_OFFSCREEN=1`), `--headless`, the gates and the guest reports open no audio
device (the game runs on its wall clock exactly as without a sound card), so tests and agents make no sound on a developer machine.
Add `--smoke-audio` or `--audible` to hear one. `--silent` or `OPENTPW_SILENT=1` silences any run; a plain launch is never silenced.
The browser perf tool (`tools/web-perf`) and the postcard smoke start Chrome with `--mute-audio`; pass `--audible` to `run.mjs` to listen.
`--music` still measures decoding, since muting only silences the speakers.

Automated runs are also offscreen (`[EXT:offscreen-automated-runs]`, extended on 2026-10-19 at the user's request that smoke tests and
agent runs use a virtual screen instead of their display). The same modes that are silent, so the smokes, every `--capture-*`, `--perf-*`
and `--debug-*` option, `--offscreen`, `--headless`, the gates, the guest reports and `--validate-assets`, create the window hidden, never show
it and render into an offscreen framebuffer (a colour texture of the size the window would have had; the logical size and
`OPENTPW_TEST_PIXEL_SCALE` apply as usual) instead of the swapchain, paced at 60 frames per second (`--no-vsync` unpaces it). macOS has no
Xvfb; this hidden window with its framebuffer is the virtual screen. No window appears, no focus is taken, and a locked screen does
not matter. `--offscreen` or `OPENTPW_OFFSCREEN=1` does the same for any run; `--visible` (or `--no-offscreen`, or `OPENTPW_OFFSCREEN=0`) keeps the
visible window. Sound and window are separate choices: `--audible` brings the sound back but keeps the run offscreen, and `--visible`
shows the window but keeps an automated run silent. To look at a `--debug-*` helper on screen (such as `--debug-light`), pass `--visible`: without a
`--capture-*` such a run does not end by itself, and offscreen it would run unseen until closed. An offscreen run without usable game data stops with
an error instead of opening the setup window. The interactive modes stay visible: a plain start, `--front-end`, `--sandbox`,
`--load-original-level`, `--visit-park`, `--setup` and `--play-movie` (which is offscreen only as the movie smoke, `--play-movie <name> --smoke-test`).

What changes offscreen, and what does not:

- **Captures** read the renderer's own textures (the world, or the composed output with `--capture-interface`), not the window, so they are
  the same either way: a Jungle capture at 800 x 500 (1600 x 1000 pixels, `OPENTPW_TEST_PIXEL_SCALE=2` offscreen against the Retina
  window) with the frame cap at 60 was byte-identical offscreen and visible. Without a frame cap a visible window runs at the display's
  refresh (up to 120 Hz on a ProMotion panel), so a capture after N frames shows an earlier moment of an animated scene than the
  offscreen run's 60 Hz; the offscreen run gives the same picture on every machine. An offscreen window is never focused, so the
  park camera never edge-scrolls towards the pointer.
- **Perf runs** (`--perf-run`) stay offscreen by default. The baselines in [ENGINE-PERFORMANCE.md](ENGINE-PERFORMANCE.md) were measured
  with `--offscreen`, so they remain comparable; a visible window's frame rate depends on the display and on macOS, which throttles the
  Metal drawables of hidden, occluded or locked windows. Pass `--visible` to measure the presented path; those numbers are not comparable
  to the offscreen baselines.
- **The OS-level fullscreen check** needs a visible run: an offscreen window has no OS fullscreen state, so the sandbox smoke's
  fullscreen toggle then verifies the logical window size only and logs that the OS transition was skipped.

Every offscreen run logs `Offscreen mode: window hidden ...` at start-up. Normal game runs stay visible.

A normal start (the front end, no `--smoke-test`) first plays the original's
start-up movies: `bf`, then a trailer chosen by the day of the month, at the
Movie volume of Game Options (its mute toggle silences them). Esc, Space or a mouse button skips. Use
`--no-intro` (or set `OPENTPW_NO_INTRO=1`) to go straight to the front end.
Any nonempty `OPENTPW_NO_INTRO` value, including `0`, disables intros.
`--capture-world` also bypasses intros so its frame count starts with the world;
smoke tests never play the movies. Missing movies (no `--cd-data`) are skipped. `--headless` simulates decoding and the audio clock
without a window or device. `--smoke-test` plays 60 frames with audio off and
checks GPU readback against the CPU frame. See TGQ-MOVIES.md.

## Display, resolution and upscaling

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --resolution 2560x1440
bash scripts/run.sh --game-path '/path/to/Theme Park World' --fullscreen --upscale linear --render-scale 67
```

| Option | Effect |
| --- | --- |
| `--resolution WxH` | Windowed size in logical units (any size, e.g. 3440x1440); on HiDPI/Retina the drawable is larger and rendering uses its real pixels. macOS may shrink windows larger than the desktop. |
| `--windowed`, `--fullscreen`, `--fullscreen-exclusive` | Window mode. `--fullscreen` is borderless at the desktop mode; exclusive switches the display mode to `--resolution` (experimental; falls back to borderless with a reason if the display does not list that mode). |
| `--upscale native\|linear\|nearest` | How the 3D world reaches the output. Native (default) renders the world at output size. Linear/Nearest render it smaller and scale it up; Nearest is a deliberate retro look. |
| `--render-scale <50-100>` | World size as a percentage of each output dimension (presets 77, 67, 59, 50; 50% is a quarter of the pixels). Without `--upscale` it selects Linear. Out-of-range values fall back to Native with a warning. |
| `--ui-scale auto\|N` | Integer scale of the BF4 text UI (auto: 2 from 2560x1440, 3 at 4K; on HiDPI/Retina at least the pixel density, so 2 on Retina). |
| `--no-vsync` | Presents frames as fast as they are made instead of at the display's refresh rate; for performance measurements. |
| `--frame-stats` | Logs frames per second, average/p99/max frame time, CPU use, allocation rate and garbage collections every 5 seconds, and where a frame goes: the update (and inside it the simulation), the world drawing, the interface overlay, submit and present, the GPU wait and the whole `Renderer.Update` call. |
| `--perf-run <frames>` | For performance runs ([ENGINE-PERFORMANCE.md](ENGINE-PERFORMANCE.md)): once the original level is up it hides the developer panels as a front-end park does, opens the park door, runs the simulation at `--perf-speed 1\|2\|4` (default 4) with a guest arriving every `--perf-arrivals N` turns, and closes the window after that many frames; `--perf-zoom <value>` holds the park camera at that zoom value (40 to 300 with the wide zoom range, 70 to 130 without). Combine it with `--load-original-level`, `--frame-stats` and, for CPU-bound frames, `--no-vsync`; it is offscreen by default (`--visible` for the presented path, whose numbers are not comparable to the baselines). `--perf-digest` adds the co-op session digest's cost on the running park to every 600th frame's line ([COOP.md](COOP.md), "Measurements"); its five digests run inside that frame, so leave it out of frame-time runs. `--perf-crowd <n>` stages n guests on the park's paths when the door opens (the browser harness's crowd), so a run sees a full park at once; the park is not a real run afterwards. |
| `--save-display-settings` | Also stores these values as the user's display settings. |
| `--export-ps2 <ps2 DATA dir> <out dir>` | Lists the PS2 version's archives and exports its textures as PNG for viewing (PS2.md). |
| `--debug-light x,y,height,radius[,red,green,blue]` | Smoke-only: once the original level is up, adds one fixed point light at x, y, height relative to the park camera's target (engine units, a cell is 2) with a radius in engine units (20 is the original engine's default of 100 model units) and an optional colour of three 0..1 channels; for captures and a look at what a light does ([LIGHTS.md](LIGHTS.md)). |
| `--debug-pool [x,y,w,h;...]` | With the `opentpw.pools` extension (on by default; `--disable-extension opentpw.pools` turns it off): paints pool cells near the camera target once the level is up, for captures ([POOLS.md](POOLS.md#checking-it-on-screen)). |
| `--capture-world <file.png>` | After `--capture-frames N` frames (default 240) writes the 3D world without interface at its render size and exits; for screenshots and comparisons. |
| `--debug-guest-looks` | For captures: gives every second guest a balloon and every third a costume as they appear, keeps a thought bubble over every fourth and has every fifth drop a litter item on its cell (the drop takes draws from the park's generator, so such a run is not comparable) (`[EXT:debug-guest-looks]`, docs/GUESTS.md "Rendering"). |
| `--capture-interface` | With `--capture-world`: writes the composed output (world plus the original-style interface, no ImGui) instead of the world alone. |
| `--capture-camera x,y,height,yaw` | With `--capture-world`: holds the park camera at that ground target, height and yaw (degrees) for the last 30 frames before the capture, so a smoke test or park start keeps its own camera until then. The capture log prints the pose. |
| `--capture-sequence count,interval` | With `--capture-world`: writes `count` numbered images (`shot-0000.png`, ...) one every `interval` frames, then exits; for clips and for finding a moment in a smoke run. |
| `--build-texture-pack --upscaler <path>` | Builds an optional texture pack from the installation with a player-supplied Real-ESRGAN executable (TEXTURE-PACKS.md). Further flags: `--texture-pack-name <name>` (default `enhanced`; `detailed` for the second variant), `--upscale-model`, `--prepass-model <onnx>` (1x de-artifact model before upscaling), `--texture-pack-dir`, `--texture-pack-subtree`, `--interface-model`, `--texture-pack-no-interface`, `--texture-pack-interface-only`, `--texture-pack-sprites-only`, `--prepass-sprites`, `--texture-pack-hero-dir <dir>`, `--texture-pack-merge`. Then choose the pack under Game Options → OpenTPW → Enhanced textures (applied at once, no restart). |

Alt+Enter or F11 toggles fullscreen at runtime; the ImGui park panel has a Display
section with the same options and the diagnostics (method, requested/effective scale,
internal and output size, fallback reason). Settings live in `display.json` under
`~/Library/Application Support/OpenTPW` on macOS, `~/.config/OpenTPW` on Linux
(`$XDG_CONFIG_HOME/OpenTPW` when set), or `%APPDATA%\OpenTPW` on Windows. Override
the directory with `OPENTPW_CONFIG_DIR`. These settings are never in saves. Malformed command-line values are errors; an invalid
settings file falls back to defaults with a warning. Only the 3D world is scaled: BF4 text,
ImGui and movies render at output size, and 4x MSAA stays on independently. The Options
screen can use the `OpenTPW.IDisplaySettings` API (including a keep-or-revert timeout).
Window mode, upscaling, render scale, the frame rate limit (off, 30 or 60 fps; off by default) and interface scale are on the OpenTPW page of Game Options. Status and limits: UPSCALING-DESIGN.md.

## Native integration smoke test

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --smoke-test
OPENTPW_NATIVE_SHADER_TESTS=1 OPENTPW_GAME_PATH='/path/to/Theme Park World' dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj -r osx-arm64
```

The smoke test runs at least 150 rendered frames, places the original Totem, waits for
its script to trigger the original `totemm1.MD2` animation, requires two
readbacks to show changed node poses and pixels, requires at least three other original objects to each move
their model nodes in at least 100 frames of the run (and, in an original level, the imported Belly Bounce to move
between the two readbacks), checks close/reset, removes/replaces the ride and exercises
save/load in an isolated temporary directory. It exits nonzero on failure or
premature window closure. GPU readback checks nonblack textured terrain and
writes `artifacts/native-smoke-park.png` and `artifacts/native-smoke-terrain.png`
relative to the current directory. The BF4 text panel (original fonts and strings
in the selected language; add `--language German --language-data <dir>` to check
another one) must match its CPU composite in readback; its crop is `artifacts/native-smoke-text.png`.
The smoke test ignores the user display settings (defaults plus command-line options)
and also reads back the final output after world scaling and the BF4 UI
(`artifacts/native-smoke-output.png`): it checks present-target (swapchain, or the offscreen framebuffer), world-target and output
sizes, exact BF4 text at the active UI scale, that picking returns the Totem cell and a
neighbouring cell, and runtime render-scale, window-size, unconfirmed-revert and
fullscreen-toggle changes without growing GPU resources. Combine it with the display
options, e.g. `--render-scale 50 --upscale linear`; `OPENTPW_TEST_PIXEL_SCALE=2` (test only)
doubles the drawable to exercise the HiDPI path on a 1x display (1280x720 window ->
2560x1440 output).
`--front-end --smoke-test` first checks the CD launcher when available (readback, Tab focus, Play) and then drives the front end with injected mouse/keys
into the original jungle level and checks lobby, options, HUD, build/info arms
and pause menu text in readback (UI.md); captures are
`artifacts/native-smoke-<language>-*.png`.
These captures omit the ImGui UI and remain ignored;
they are not proof of interactive UI correctness or original-game visual fidelity.
The nine native shader tests verify MSL/HLSL/GLSL material and text binding names without
creating a GPU device; omit the opt-in variable when matching libraries are absent.

## Park-resume smoke test

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --mute --no-advisor \
  --park-resume-smoke capture --park-resume-file park.otpw --park-resume-evidence evidence/
bash scripts/run.sh --game-path '/path/to/Theme Park World' --mute --no-advisor \
  --park-resume-smoke restore --park-resume-file park.otpw --park-resume-evidence evidence/
```

Two-phase verification of the whole-park save (PARK-SAVES.md) on a real renderer. The capture phase
builds a seeded Jungle park (the same ride placed twice at rotations 0 and 180 with edited queues,
two identical shops at different prices, a toilet, a demolished object leaving an id hole, two staff
and fourteen guests), saves it through the pause menu's own save handler and continues the live park.
The restore phase — a cold second process — requires the save's bytes to survive the restart,
rejects six corrupted candidates with the live park intact each time, resumes the park and compares
every typed section hash and the composite world hash against the capture run at each checkpoint,
then runs post-load economy checks (admission booked once, a sale credited once with its links
removed, a new placement, wages charged once, payment bindings one-to-one). The evidence directory
holds the phase evidence (hashes and counters only, never asset bytes), the corrupted probe files
and captures; `--park-resume-file` accepts the own-format `.json`/`.otpw` suffix only. Each phase also takes the periodic autosave
(`[EXT:periodic-autosave]`, [PARK-SAVES.md](PARK-SAVES.md#periodic-autosave)) of its park at the end: five minutes of play time in quarter-second frames, a readable autosave slot, the manual save untouched,
the sliced serialization equal to the manual serializer's bytes, a captured snapshot unchanged while the park runs on, and the state hash and random streams unchanged; the capture, serialization and write times are logged. These are
controlled, accelerated, seeded fixtures that validate persistence of current OpenTPW behaviour,
not original-game fidelity.

## Advisor speech

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --advisor-say 1
bash scripts/run.sh --game-path '/path/to/Theme Park World' --smoke-test --advisor-say 1
```

`--advisor-say N` (1–637) draws the original advisor model in the bottom-left
corner, plays global speech clip `sp_NNN` of the selected language (`--language`/`--language-data`) through SDL2 audio and switches its mouth
between `Mouth - Aah` (talking) and `Mouth - Normal` from the clip's `.LIP` marks.
Without an audio device the mouth follows a wall clock silently. With `--smoke-test`
it runs until the clip ends, checks the clock against wall time and the mouth sequence,
and writes `artifacts/native-smoke-advisor-{talking,closed}.png`. The advisor's
original pose and animation are not reproduced; see LIPS.md.

In an original-level park (front end or `--load-original-level <level>`; not the
generic sandbox) the advisor speaks by
itself for the traced game events (LIPS.md, "Automatic advice"): the level-start
welcome comes first. `--no-advisor` turns automatic advice off; `--advisor-say`
and `--advisor-response` also turn it off. The Game Options Advisor switch
silences it (advice is still consumed, as in the original) and the Tutorial switch
gates tutorial advice. With `--mute` it opens no audio device. `--smoke-test
--load-original-level jungle` logs `Advisor says response 1 … /levels/jungle/Speech/speechHD.SDT`.

## Development packages

```sh
dotnet publish source/OpenTPW/OpenTPW.csproj -c Release -r osx-arm64 --self-contained false -o artifacts/publish/osx-arm64
dotnet publish source/OpenTPW/OpenTPW.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish/win-x64
dotnet publish source/OpenTPW/OpenTPW.csproj -c Release -r linux-x64 --self-contained false -o artifacts/publish/linux-x64
```

These packages require a matching .NET 11 runtime and legally obtained external
game assets. Build the arm64 dependency before publishing that RID. Successful
cross-publication is not successful execution on the target platform.
Current verification and remaining release blockers are recorded in PROGRESS.md.

The CPU-only `--inspect-tpi-mission /local/mission.sam` command checks explicitly selected retained TPI mission bounds and reports parser omissions. See [static mission inspection](TPI-MISSION-INSPECTION.md) for its modified-NoCD evidence and lexical/runtime limits.
