# Release builds

Releases are normal stable releases: the [hourly release train](#hourly-releases) publishes one from a green `main` whenever something new has merged, and a maintainer can still publish one by hand.

`.github/workflows/release.yml` publishes self-contained builds (no .NET install
needed) for six runtimes on every `v*` tag, and attaches them to a GitHub
release. Versions with a suffix, or the preview checkbox, are pre-releases. The bundled runtime is the one that ships with the SDK pinned in `global.json` (.NET 11 RC2 until the pin moves to the GA SDK). Original game files are never part of a release: start the game with
`--game-path` pointing at an installed Theme Park World (see the [running guide](RUNNING.md)).

| Runtime | Archive | Graphics | Notes |
| --- | --- | --- | --- |
| `win-x64` | zip | Direct3D 11 | |
| `win-x86` | zip | Direct3D 11 | |
| `win-arm64` | zip | Direct3D 11 | |
| `osx-arm64` | tar.gz | Metal | unsigned; see below |
| `linux-x64` | tar.gz | Vulkan | needs system SDL2 and a Vulkan driver |
| `linux-arm64` | tar.gz | Vulkan | needs system SDL2 and a Vulkan driver |

`linux-x86` is not built: .NET (8, 10 and 11 alike) has no 32-bit x86 Linux runtime, so
`dotnet publish -r linux-x86` cannot succeed. Intel macOS (`osx-x64`) is not in the
list either.

## Publish from GitHub Actions

Open **Actions → Release builds → Run workflow**, select `main`, and enter a new
version such as `v0.7.2`. Leave **Mark as preview** unchecked to publish the latest
download used by the website. A `-preview.1` suffix also selects preview mode.
The selected commit is fixed for the entire run; later changes to `main` are not
included. The tag must be unused or already point at that exact commit.

Publication waits for all six builds. New or incomplete releases remain drafts
until all archives and `SHA256SUMS.txt` are uploaded. Retrying the same commit and
version repairs an incomplete release; a complete published release is left
unchanged. Original game data is never included.

A normal manual release then starts **Cross-platform build and CPU tests** on
that exact published tag. Its server image is published and the official server
is asked to update after the image check passes. Preview releases do not start
this deployment. Explicit dispatch is required because a tag created with the
workflow's `GITHUB_TOKEN` does not trigger another push workflow.

To retry a server deployment, open **Actions → Cross-platform build and CPU
tests → Run workflow**, select `main`, and enter the published stable release
tag, such as `v0.7.5`, in **Published stable release to deploy**. The workflow
checks that the release is published and not a preview, then starts a build on
that exact tag. Leaving the field empty on `main` publishes only the development
image and does not deploy the official server. See the [server guide](SERVER.md) for the deploy
token and host setup.

To deploy the website separately, use **Actions → Website → Run workflow** on
`main`. A normal (non-preview) release updates the existing latest-download links
without requiring a website code change.

## Hourly releases

`.github/workflows/release-train.yml` (**Actions → Hourly release**) wakes after a successful **Cross-platform build and CPU tests** push run on `main`, and keeps
minute 17 of every hour as a recovery fallback. At the end of a successful stable **Release builds** run dispatched
on `main`, the release explicitly dispatches a catch-up train with its run ID. The train waits at most three minutes
for that exact source run to finish successfully before planning, so green commits skipped while it was active
are reconsidered without depending on a downstream `workflow_run` event (which GITHUB_TOKEN may suppress). GitHub schedules can be delayed or dropped; the cron expression is
not an hourly delivery guarantee. When the train finds something new, it starts the normal release workflow for the next stable version. The result is a regular release
that becomes the latest download, exactly like a manual one. Manual releases keep working as described above.

Each train (`tools/release_train.py`) takes the current commit of `main` and:

1. skips when a release run is queued, running, requested, pending, or waiting for approval;
2. retries a half-failed release first: if the newest stable tag exists but its release is missing or still a draft,
   the train first requires successful push CI on that exact tagged commit, then dispatches the same version again
   (the publish script repairs it), without assigning that version to a different commit. Missing, pending or failed
   CI on the tagged commit blocks automatic repair and needs a successful CI rerun or maintainer attention;
3. skips when that commit already carries a stable tag, or when no commit is newer than the newest stable tag;
4. skips when **Cross-platform build and CPU tests** has not finished on that exact commit (the next train retries,
   so pull requests that merge while CI runs ride a later train) or did not succeed, including cancelled runs (the job
   summary says so);
5. otherwise picks `vX.Y.(Z+1)`, where `vX.Y.Z` is the newest stable tag (preview tags are ignored), plans again
   after refetching `main` and the tags right before dispatching, and dispatches **Release builds** with that version,
   `prerelease=false` and the commit SHA. The commit input fixes the build and the tag to the validated commit, even
   when `main` moves before the build starts (the commit must be on `main`). Trains never overlap (concurrency group),
   and the release workflow serializes runs of the same version. A changed commit/version or a newly active release
   aborts dispatch. The publish script leaves a complete release unchanged, including if duplicate wake-ups race
   with GitHub making a dispatched run visible.

A completion event is only a wake-up, never proof that the current `main` is green: the planner always queries
all matching push CI runs for the selected full SHA again. PR, fork, tag and manually dispatched CI-build events
cannot use this trigger to start a release; the train checks out trusted `main`, never the event checkout or its artifacts.
For a new release, if `main` moves during planning, the train starts nothing and lets a subsequent completion or
fallback retry. Repairing an unfinished tagged release remains pinned to its older tagged commit.
Manual **Only report** remains the default and never dispatches. The existing workflow name is retained so
saved links and manual instructions still work. The internal **wait_for_release_run** input is normally left empty.
A catch-up validates the source run as this repository's main-dispatched release, then uses the same planner:
an already-tagged head or another active release is a no-op, and a new candidate needs its own green push CI.
A failed/cancelled source, invalid ID, API error or wait timeout stops the train before planning; there is no blind
retry chain. The existing hourly fallback remains available for missed dispatches. This uses the existing
workflow token and action permissions, with no new credential.

The release notes are GitHub's list of merged pull requests since the previous tag, plus links to the progress
fragments (`docs/progress/`) added since then. A tag pushed with the workflow token would not start other workflows,
which is why the train dispatches the release workflow instead of pushing a tag.

To see what the next train would do without starting anything, run **Hourly release** manually with
**Only report** ticked (the default for manual runs); the plan is in the job summary. Untick it to release now.

## Native libraries

Three pinned packages carry native code, but only for some runtimes:

| Runtime | SDL2 | veldrid-spirv | cimgui |
| --- | --- | --- | --- |
| `win-x64`, `win-x86` | **built** | package | package |
| `linux-x64` | system `libSDL2-2.0.so.0` | package | package |
| `osx-arm64` | **built** | **built** | package (universal) |
| `win-arm64` | **built** | **built** | **built** |
| `linux-arm64` | system `libSDL2-2.0.so.0` | **built** | **built** |

`scripts/build-native.py` builds the missing ones from the **same pinned source
revisions** the packages were made from, so a release adds no new dependency.
SDL2 is the exception: Veldrid.SDL2 4.8.0 does carry `win-x64` and `win-x86`
binaries, but they are SDL 2.0.8 (2018), whose WASAPI output drops about 15% of
the audio on current Windows ([#754](https://github.com/meneerkrabs/OpenTPW/issues/754)).
Every Windows release therefore ships the SDL2 built from the pin below, and the
package's `SDL2.dll` is left out of those builds. The package veldrid-spirv and
cimgui binaries are the same versions as their pins and stay.

| Library | Package | Source revision |
| --- | --- | --- |
| veldrid-spirv | Veldrid.SPIRV 1.0.14 | `veldrid/veldrid-spirv` v1.0.14 (`f2e50faa…`), shaderc and its dependencies from the release's `ext/known_good.json` |
| cimgui | ImGui.NET 1.87.2 | `ImGuiNET/ImGui.NET-nativebuild` v1.87.1 (`54206de6…`), cimgui `4492660b…` (Dear ImGui 1.87) |
| SDL2 | Veldrid.SDL2 4.8.0 (SDL2 ABI) | `libsdl-org/SDL` release-2.32.10 (`5d249570…`) |

The script refuses modified or mismatched sources, builds Release with CMake
(static C runtime on Windows; static libstdc++/libgcc on Linux; macOS 11.0 deployment
target with an `@rpath` install name), then checks the produced binary's CPU type
and the exports the managed bindings call before copying it, under the file name
the loader probes, to `native/<rid>/`. A Windows SDL2 must also carry the pin's
FileVersion. It must run natively on the target architecture (an x64 Windows host
also builds `win-x86`); the workflow uses `macos-latest`, `windows-11-arm`,
`windows-latest` and `ubuntu-22.04-arm` runners. Generated binaries are not committed.

```sh
python3 scripts/build-native.py --rid osx-arm64 veldrid-spirv sdl2
python3 scripts/build-native.py --rid linux-arm64 veldrid-spirv cimgui
py scripts/build-native.py --rid win-arm64 veldrid-spirv cimgui sdl2
py scripts/build-native.py --rid win-x64 sdl2
py scripts/build-native.py --rid win-x86 sdl2
```

The OpenTPW project copies every file in `native/<rid>/` next to the executable, in
place of a package file of the same name, and fails the build for `osx-arm64`,
`win-arm64` and `linux-arm64` when a required file is missing. A local `win-x64` or
`win-x86` publish without a built SDL2 still takes the package's 2.0.8; the release
workflow refuses that: after publishing, `tools/check_release_natives.py` fails a
Windows folder that does not hold exactly one `SDL2.dll`, for the runtime's CPU, with
a FileVersion no older than the pin. For local arm64 macOS development without a built SDL2 it still falls back
to Homebrew's SDL2 (and SDL3 for sdl2-compat; [native macOS libraries](MACOS-NATIVE.md)); release builds always
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
locally qualified on Apple Silicon (Metal, [running guide smoke test](RUNNING.md)). Windows on Arm,
Windows x86 and Linux arm64 builds are untested on real hardware.
