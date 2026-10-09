# Release builds

`.github/workflows/release.yml` publishes self-contained builds (no .NET install
needed) for six runtimes on every `v*` tag, and attaches them to a GitHub
pre-release. Original game files are never part of a release: start the game with
`--game-path` pointing at an installed Theme Park World (see RUNNING.md).

| Runtime | Archive | Graphics | Notes |
| --- | --- | --- | --- |
| `win-x64` | zip | Direct3D 11 | |
| `win-x86` | zip | Direct3D 11 | |
| `win-arm64` | zip | Direct3D 11 | |
| `osx-arm64` | tar.gz | Metal | unsigned; see below |
| `linux-x64` | tar.gz | Vulkan | needs system SDL2 and a Vulkan driver |
| `linux-arm64` | tar.gz | Vulkan | needs system SDL2 and a Vulkan driver |

`linux-x86` is not built: .NET (8 and 10 alike) has no 32-bit x86 Linux runtime, so
`dotnet publish -r linux-x86` cannot succeed. Intel macOS (`osx-x64`) is not in the
list either.

## Native libraries

Three pinned packages carry native code, but only for some runtimes:

| Runtime | SDL2 | veldrid-spirv | cimgui |
| --- | --- | --- | --- |
| `win-x64`, `win-x86` | package | package | package |
| `linux-x64` | system `libSDL2-2.0.so.0` | package | package |
| `osx-arm64` | **built** | **built** | package (universal) |
| `win-arm64` | **built** | **built** | **built** |
| `linux-arm64` | system `libSDL2-2.0.so.0` | **built** | **built** |

`scripts/build-native.py` builds the missing ones from the **same pinned source
revisions** the packages were made from, so a release adds no new dependency:

| Library | Package | Source revision |
| --- | --- | --- |
| veldrid-spirv | Veldrid.SPIRV 1.0.14 | `veldrid/veldrid-spirv` v1.0.14 (`f2e50faa…`), shaderc and its dependencies from the release's `ext/known_good.json` |
| cimgui | ImGui.NET 1.87.2 | `ImGuiNET/ImGui.NET-nativebuild` v1.87.1 (`54206de6…`), cimgui `4492660b…` (Dear ImGui 1.87) |
| SDL2 | Veldrid.SDL2 4.8.0 (SDL2 ABI) | `libsdl-org/SDL` release-2.32.10 (`5d249570…`) |

The script refuses modified or mismatched sources, builds Release with CMake
(static C runtime on Windows; static libstdc++/libgcc on Linux; macOS 11.0 deployment
target with an `@rpath` install name), then checks the produced binary's CPU type
and the exports the managed bindings call before copying it, under the file name
the loader probes, to `native/<rid>/`. It must run natively on the target
architecture; the workflow uses `macos-latest`, `windows-11-arm` and
`ubuntu-22.04-arm` runners. Generated binaries are not committed.

```sh
python3 scripts/build-native.py --rid osx-arm64 veldrid-spirv sdl2
python3 scripts/build-native.py --rid linux-arm64 veldrid-spirv cimgui
py scripts/build-native.py --rid win-arm64 veldrid-spirv cimgui sdl2
```

The OpenTPW project copies every file in `native/<rid>/` next to the executable and
fails the build for `osx-arm64`, `win-arm64` and `linux-arm64` when a required file is
missing. For local arm64 macOS development without a built SDL2 it still falls back
to Homebrew's SDL2 (and SDL3 for sdl2-compat; MACOS-NATIVE.md); release builds always
ship the SDL2 built above.

## Running a release

- Windows: unzip and run `OpenTPW.exe --game-path "C:\Games\Theme Park World"`.
- macOS: the build is not signed or notarized. After extracting, clear the download
  quarantine once with `xattr -dr com.apple.quarantine OpenTPW-osx-arm64`, then run
  `./OpenTPW --game-path "/path/to/Theme Park World"`.
- Linux: install SDL2 (`libsdl2-2.0-0` on Debian/Ubuntu) and a Vulkan driver, then
  run `./OpenTPW --game-path /path/to/theme-park-world`.

## Qualification

The workflow builds and packages; it does not run the game. GPU execution is only
locally qualified on Apple Silicon (Metal, RUNNING.md smoke test). Windows on Arm,
Windows x86 and Linux arm64 builds are untested on real hardware.
