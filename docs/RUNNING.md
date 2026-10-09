# Running the current development build

This is not yet the complete game. The completion gates are in COMPLETION-PLAN.md.
Original legally obtained game assets are required and must not be committed.

## macOS / Linux

Install the pinned .NET 8 SDK. Apple Silicon uses native arm64 with Metal when
the matching libraries are available; see MACOS-NATIVE.md for reproduction.
The launcher falls back to an Intel/Rosetta bootstrap when those files are absent.
Linux currently uses Vulkan and needs a working Vulkan driver. Windows uses
Direct3D 11. Windows/Linux GPU execution is not yet locally qualified.

Linux (x64) needs the system packages for SDL2, the Vulkan loader and a Vulkan
driver (Debian/Ubuntu: `libsdl2-2.0-0 libvulkan1 mesa-vulkan-drivers`) and an X11
or Wayland session; the NuGet packages bring `libveldrid-spirv.so` and
`libcimgui.so` but no SDL2. Without a GPU, Mesa's llvmpipe software Vulkan works,
and `xvfb-run -a bash scripts/run.sh …` provides a virtual display for headless
smoke tests. Missing display or Vulkan driver ends with a named error instead of a
.NET loader exception. Game data may use any letter case (`data`, `Data`, `DATA`
from a mounted CD, `Speech`/`speech`): paths are matched to the on-disk spelling.
linux-arm64 is blocked: the packages ship no arm64 `libveldrid-spirv`/`libcimgui`.
Shader hot reload shares one file watcher per shader file, because every Linux
watcher holds an inotify instance (default limit 128 per user); if the limit is
still reached, hot reload is disabled with a warning instead of stopping the game.

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World'
bash scripts/run.sh --game-path '/path/to/Theme Park World' --sandbox
bash scripts/run.sh --game-path '/path/to/Theme Park World' --validate-assets
```

The default start is the original-style front end (3D lobby; pick an island and
enter the park, or Load/Options/Quit Game) with the in-game HUD; see UI.md.
`--sandbox` opens the former default jungle sandbox with its ImGui developer panel.

The installation directory must contain `data` or `Data`. `OPENTPW_GAME_PATH`
can replace `--game-path`. `DOTNET` selects a particular SDK executable.
`OPENTPW_RUNTIME` selects an explicit runtime identifier.
Native dependencies must match the selected process architecture.
Engine content is copied beside the executable; shader files/includes resolve
from the executable directory rather than the shell's current directory.

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
the private tests (OBJECTS.md). Nothing is copied into the game data.
`--smoke-test` also works with this flag (captures `native-smoke-original-*.png`;
the smoke also checks that guests fill and leave the Totem and ride imported objects).

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
`OPENTPW_MTR_PATH` to it to run the MTR fixture tests (see MTR.md).

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

## Movies

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --play-movie bf
bash scripts/run.sh --game-path '/path/to/Theme Park World' --play-movie plan --headless
bash scripts/run.sh --game-path '/path/to/Theme Park World' --play-movie bf --smoke-test
```

Plays `Data/Movies/<name>.tgq` with SDL2 audio (`--mute` disables it); a key
press or click skips. `--headless` simulates decoding and the audio clock
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
| `--ui-scale auto\|N` | Integer scale of the BF4 text UI (auto: 2 from 2560x1440, 3 at 4K). |
| `--save-display-settings` | Also stores these values as the user's display settings. |

Alt+Enter or F11 toggles fullscreen at runtime; the ImGui park panel has a Display
section with the same options and the diagnostics (method, requested/effective scale,
internal and output size, fallback reason). Settings live in `~/.config/OpenTPW/display.json`
(`%APPDATA%\OpenTPW\display.json` on Windows; override the directory with
`OPENTPW_CONFIG_DIR`), never in saves. Malformed command-line values are errors; an invalid
settings file falls back to defaults with a warning. Only the 3D world is scaled: BF4 text,
ImGui and movies render at output size, and 4x MSAA stays on independently. The Options
screen can use the `OpenTPW.IDisplaySettings` API (including a keep-or-revert timeout).
Status and limits: UPSCALING-DESIGN.md.

## Native integration smoke test

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --smoke-test
OPENTPW_NATIVE_SHADER_TESTS=1 OPENTPW_GAME_PATH='/path/to/Theme Park World' dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj -r osx-arm64
```

The smoke test runs at least 150 rendered frames, places the original Totem, waits for
its script to trigger the original `totemm1.MD2` animation, requires two
readbacks to show changed node poses and pixels, checks close/reset, removes/replaces the ride and exercises
save/load in an isolated temporary directory. It exits nonzero on failure or
premature window closure. GPU readback checks nonblack textured terrain and
writes `artifacts/native-smoke-park.png` and `artifacts/native-smoke-terrain.png`
relative to the current directory. The BF4 text panel (original fonts and strings
in the selected language; add `--language German --language-data <dir>` to check
another one) must match its CPU composite in readback; its crop is `artifacts/native-smoke-text.png`.
The smoke test ignores the user display settings (defaults plus command-line options)
and also reads back the final output after world scaling and the BF4 UI
(`artifacts/native-smoke-output.png`): it checks swapchain, world-target and output
sizes, exact BF4 text at the active UI scale, that picking returns the Totem cell and a
neighbouring cell, and runtime render-scale, window-size, unconfirmed-revert and
fullscreen-toggle changes without growing GPU resources. Combine it with the display
options, e.g. `--render-scale 50 --upscale linear`; `OPENTPW_TEST_PIXEL_SCALE=2` (test only)
doubles the drawable to exercise the HiDPI path on a 1x display (1280x720 window ->
2560x1440 output).
`--front-end --smoke-test` instead drives the front end with injected mouse/keys
into the original jungle level and checks lobby, options, HUD, build/info arms
and pause menu text in readback (UI.md); captures are
`artifacts/native-smoke-<language>-*.png`.
These captures omit the ImGui UI and remain ignored;
they are not proof of interactive UI correctness or original-game visual fidelity.
The nine native shader tests verify MSL/HLSL/GLSL material and text binding names without
creating a GPU device; omit the opt-in variable when matching libraries are absent.

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
original pose, animation, mouth-shape choice and triggers are not reproduced; see LIPS.md.

## Development packages

```sh
dotnet publish source/OpenTPW/OpenTPW.csproj -c Release -r osx-arm64 --self-contained false -o artifacts/publish/osx-arm64
dotnet publish source/OpenTPW/OpenTPW.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish/win-x64
dotnet publish source/OpenTPW/OpenTPW.csproj -c Release -r linux-x64 --self-contained false -o artifacts/publish/linux-x64
```

These packages require a matching .NET 8 runtime and legally obtained external
game assets. Build the arm64 dependency before publishing that RID. Successful
cross-publication is not successful execution on the target platform.
Current verification and remaining release blockers are recorded in PROGRESS.md.
