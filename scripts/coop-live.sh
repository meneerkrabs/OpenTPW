#!/usr/bin/env bash
# [EXT:online-coop-session] The live co-op acceptance (docs/COOP.md, "The live acceptance"; C2d-5): builds the game and the server once, then tools/coop-live.mjs runs the
# server on 127.0.0.1 with a fresh data folder and two games, native (silent and offscreen) or a page (headless, muted Chrome), that share a park through it with the game's
# data: the host shares Jungle from its pause menu and the guest joins it from the lobby with the host's code, both through the game's own screens.
#
# Usage: scripts/coop-live.sh <out directory> ['/path/to/Theme Park World'] [driver options...]
# The out directory must not exist yet; it keeps run.json, the logs and the evidence. The game path may also come from OPENTPW_GAME_PATH. Driver options, for example
# --scenario recovery or --scenario endurance --minutes 30, go to tools/coop-live.mjs; a page side (--host page or --guest page) also needs the published web build
# (dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -p:RunAOTCompilation=false -o <dir>, then --web <dir>/wwwroot --runtime interpreter).
# OPENTPW_CONFIGURATION=Debug builds the Debug configuration (Release otherwise); DOTNET names the dotnet to build and run with.
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
out="${1:-}"
if [[ -z "$out" ]]; then
  printf '%s\n' 'Pass the out directory (it must not exist yet).' >&2
  exit 2
fi
shift
game="${OPENTPW_GAME_PATH:-}"
if [[ $# -gt 0 && "$1" != --* ]]; then
  game="$1"
  shift
fi
if [[ -z "$game" ]]; then
  printf '%s\n' 'Pass the Theme Park World path (or set OPENTPW_GAME_PATH).' >&2
  exit 2
fi
dotnet="${DOTNET:-dotnet}"
runtime="${OPENTPW_RUNTIME:-}"
if [[ "$(uname -s)" == Darwin && -z "$runtime" ]]; then
  if [[ "$(uname -m)" == arm64 && -f native/osx-arm64/libveldrid-spirv.dylib && -f /opt/homebrew/lib/libSDL2.dylib ]]; then
    runtime=osx-arm64
  else
    runtime=osx-x64
  fi
fi
configuration="${OPENTPW_CONFIGURATION:-Release}"
build_arguments=(build source/OpenTPW/OpenTPW.csproj --nologo -v q -c "$configuration")
game_output="source/OpenTPW/bin/$configuration/net11.0"
if [[ -n "$runtime" ]]; then
  build_arguments+=(-r "$runtime")
  game_output+="/$runtime"
fi
"$dotnet" "${build_arguments[@]}" > /dev/null
"$dotnet" build source/OpenTPW.Server/OpenTPW.Server.csproj -c Release -v q -nologo > /dev/null
DOTNET="$dotnet" node tools/coop-live.mjs --game "$game_output/OpenTPW.dll" --server source/OpenTPW.Server/bin/Release/net11.0/OpenTPW.Server.dll \
  --game-path "$game" --out "$out" "$@"
