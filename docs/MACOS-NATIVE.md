# macOS arm64 native dependencies

The managed dependency versions remain unchanged. Veldrid.SPIRV 1.0.14 bundles
an Intel-only macOS native library, so Apple Silicon needs an arm64 build of
that exact release rather than a package upgrade.

## Reproduce the SPIR-V library

Run from a native arm64 macOS shell with existing Git, CMake, Python 3 and Apple
Clang/Xcode command-line tools available on PATH:

```sh
./scripts/build-macos-native.sh
```

The script is a wrapper around `scripts/build-native.py`, which also builds SDL2 and the
Windows/Linux arm64 libraries for release builds (RELEASES.md).

The script fetches source into the external sibling cache
`../tooling/veldrid-spirv` (for this checkout,
`/Users/sander/server/tooling/veldrid-spirv`). Override it with
`VELDRID_SPIRV_SOURCE_DIR`; override the four-job build limit with
`NATIVE_BUILD_JOBS`. It installs no tools or packages and does not run .NET.

The output is `native/osx-arm64/libveldrid-spirv.dylib`. These generated binaries
are not source assets and should remain ignored by Git. The application must
copy the generated library into its output directory instead of the NuGet
package’s Intel macOS binary when running as arm64.

The release is pinned to tag `v1.0.14`, commit
`f2e50faa56e9a4fa63394de0be7baa714043222d`. SPIRV-Cross uses the release gitlink
`9acb9ec31f5a8ef80ea6b994bb77be787b08d3d1`. Shaderc and its transitive source
dependencies use the release’s `ext/known_good.json`, not current upstream HEAD:

| Dependency | Pinned revision |
| --- | --- |
| shaderc | `702723ac7599d229195aabfee0b61954ad087140` |
| glslang | `dd69df7f3dac26362e10b0f38efb9e47990f7537` |
| SPIRV-Headers | `f027d53ded7e230e008d37c8b47ede7cd308e19d` |
| SPIRV-Tools | `b27b1afd12d05bf238ac7368bb49de73cd620a8e` |
| re2 | `91420e899889cffd100b70e8cc50611b3031e959` |
| effcee | `5af957bbfc7da4e9f7aa8cac11379fa36dd79b84` |
| googletest | `b1fbd33c06cdb0024c67733c6fdec2009d17b384` |

The script replaces the legacy dependency-sync helper with a Python standard
library implementation because that helper imports removed `distutils` APIs.
No upstream source files are patched. CMake 4 compatibility uses
`CMAKE_POLICY_VERSION_MINIMUM=3.5`. The Release build explicitly targets arm64
and macOS 11.0, with an `@rpath` install name. Binary contents can vary with the
installed compiler/SDK; the source revisions and architecture are pinned.

## Other existing native libraries

Homebrew's `sdl2` formula is now **sdl2-compat**: an SDL2-ABI shim that loads SDL3 at
startup from beside itself (`@loader_path/libSDL3.dylib`) or the default search path,
which does not include `/opt/homebrew/lib`. Without SDL3 next to the app it aborts with
"Failed loading SDL3 library." The osx-arm64 build therefore also copies
`/opt/homebrew/lib/libSDL3.0.dylib` as `libSDL3.dylib` when it exists (override with
`SDL3NativePath`). With a real SDL2 the extra file is unused.

`/opt/homebrew/lib/libSDL2.dylib` is the existing arm64 SDL2 library on this
machine (2.32.10). It is not the native binary bundled in Veldrid.SDL2 4.8.0;
using it retains the SDL2 ABI boundary but is not an exact native-version
reproduction. This script neither installs nor upgrades nor copies SDL2. Its
application integration and runtime validation are separate from the SPIR-V
build. If strict bundled-SDL version parity is required, first identify that
version from the existing package and build its pinned sources separately.

The existing ImGui.NET 1.87.2 `osx-universal/libcimgui.dylib` already includes
arm64. Reuse that package artifact; do not replace or upgrade ImGui.

## Verify

The script checks arm64 architecture and the three managed ABI exports
`CrossCompile`, `CompileGlslToSpirv`, and `FreeResult`, then prints dependency
load commands. Inspect manually with:

```sh
lipo -info native/osx-arm64/libveldrid-spirv.dylib
nm -gU native/osx-arm64/libveldrid-spirv.dylib
otool -L native/osx-arm64/libveldrid-spirv.dylib
```

The finished SPIR-V dylib should depend only on macOS system libraries, not on
the external source/build cache or Homebrew shader libraries. Compilation and
MSL cross-compilation can be smoke-tested through the native exports without
starting the game. Renderer/window validation remains an application-level
check.
