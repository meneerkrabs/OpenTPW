# Running the current development build

This is not yet the complete game. The completion gates are in COMPLETION-PLAN.md.
Original legally obtained game assets are required and must not be committed.

## macOS / Linux

Install the pinned .NET 8 SDK. Apple Silicon uses native arm64 with Metal when
the matching libraries are available; see MACOS-NATIVE.md for reproduction.
The launcher falls back to an Intel/Rosetta bootstrap when those files are absent.
Linux currently uses Vulkan and needs a working Vulkan driver. Windows uses
Direct3D 11. Windows/Linux GPU execution is not yet locally qualified.

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World'
bash scripts/run.sh --game-path '/path/to/Theme Park World' --validate-assets
```

The installation directory must contain `data` or `Data`. `OPENTPW_GAME_PATH`
can replace `--game-path`. `DOTNET` selects a particular SDK executable.
`OPENTPW_RUNTIME` selects an explicit runtime identifier.
Native dependencies must match the selected process architecture.
Engine content is copied beside the executable; shader files/includes resolve
from the executable directory rather than the shell's current directory.

## Windows

```powershell
./scripts/run.ps1 --game-path 'C:\Games\Theme Park World'
```

## Tests

```sh
dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj
OPENTPW_GAME_PATH='/path/to/Theme Park World' dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj
```

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
original TPWS import remain roadmap tasks, not features of the sandbox.

## Read-only BF4 font inspection

```sh
dotnet source/OpenTPW/bin/Debug/net8.0/osx-arm64/OpenTPW.dll --inspect-font '/path/to/Theme Park World/Data/Language/English/SESHMED.bf4'
```

Use your actual build/RID DLL path. No `--game-path`, graphics or game-directory
writes are needed. Prints entry/sample counts and encoding inventory; decoding
errors exit nonzero. This does not integrate original fonts into the game UI.
See BF4-FONTS.md for supported encodings, limits and private fixture evidence.

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

## Native integration smoke test

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --smoke-test
OPENTPW_NATIVE_SHADER_TESTS=1 OPENTPW_GAME_PATH='/path/to/Theme Park World' dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj -r osx-arm64
```

The smoke test runs 150 rendered frames, places the original Totem, checks
procedural motion and stop/reset, removes/replaces the ride and exercises
save/load in an isolated temporary directory. It exits nonzero on failure or
premature window closure. GPU readback checks nonblack textured terrain and
writes `artifacts/native-smoke-park.png` and `artifacts/native-smoke-terrain.png`
relative to the current directory. These captures omit the UI and remain ignored;
they are not proof of interactive UI correctness or original-game visual fidelity.
The six native shader tests verify MSL/HLSL/GLSL material binding names without
creating a GPU device; omit the opt-in variable when matching libraries are absent.

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
