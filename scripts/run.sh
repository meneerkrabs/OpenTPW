#!/usr/bin/env bash
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
dotnet_command="${DOTNET:-dotnet}"
runtime="${OPENTPW_RUNTIME:-}"
if [[ "$(uname -s)" == Darwin && -z "$runtime" ]]; then
  if [[ "$(uname -m)" == arm64 && -f native/osx-arm64/libveldrid-spirv.dylib && -f /opt/homebrew/lib/libSDL2.dylib ]]; then
    runtime=osx-arm64
  else
    runtime=osx-x64
  fi
fi
if ! command -v "$dotnet_command" >/dev/null 2>&1 && [[ ! -x "$dotnet_command" ]]; then
  if [[ "$runtime" == osx-x64 && -x "$HOME/.local/share/opentpw-dotnet-x64/dotnet" ]]; then
    dotnet_command="$HOME/.local/share/opentpw-dotnet-x64/dotnet"
  elif [[ -x "$HOME/.local/share/opentpw-dotnet/dotnet" ]]; then
    dotnet_command="$HOME/.local/share/opentpw-dotnet/dotnet"
  else
    printf '%s\n' 'Install the .NET 10 SDK or set DOTNET to its executable.' >&2
    exit 1
  fi
fi
build_arguments=(build source/OpenTPW/OpenTPW.csproj --nologo)
output_directory=source/OpenTPW/bin/Debug/net10.0
if [[ -n "$runtime" ]]; then
  build_arguments+=(-r "$runtime")
  output_directory+="/$runtime"
fi
"$dotnet_command" "${build_arguments[@]}"
exec "$dotnet_command" "$output_directory/OpenTPW.dll" "$@"
