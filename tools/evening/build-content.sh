#!/bin/bash
# Regenerates the evening extension's torches under content/evening/ (everything except objects.json, which is a committed source file):
#
#   1. Blender builds the torch of each theme                       tools/blender/opentpw_torches.py      -> <work>/glb/<theme>-torch.glb
#   2. opentpw-kit object turns each glb into an object archive     tools/evening/torch.sam, torch.rse    -> content/evening/objects/<theme>/torch/
#
# Takes about half a minute. Idempotent: the outputs are replaced, never appended to. Needs Blender 5.x and the .NET SDK.
# Nothing from the original games is read or written: every file is our own art and numbers.
#
# Environment:
#   BLENDER          Blender executable (default /Applications/Blender.app/Contents/MacOS/Blender)
#   DOTNET           dotnet executable (default: dotnet from PATH, else ~/.dotnet/dotnet)
#   KIT_ARTIFACTS    optional --artifacts-path for the kit build, to keep it apart from other builds
#   WORK             scratch folder (default /tmp/opentpw-evening-build)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
blender="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
dotnet="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
work="${WORK:-/tmp/opentpw-evening-build}"
themes=(jungle hallow space fantasy)

kit_options=(-c Release)
if [ -n "${KIT_ARTIFACTS:-}" ]; then
	kit_options+=(--artifacts-path "$KIT_ARTIFACTS")
fi
kit() { "$dotnet" run "${kit_options[@]}" --project source/OpenTPW.ContentKit -- "$@"; }

mkdir -p "$work"

echo "== 1. torch models"
rm -rf "$work/glb"
"$blender" -b --factory-startup --python tools/blender/opentpw_torches.py -- --out "$work/glb" --preview "$work/previews" > "$work/torches.log" 2>&1 \
	|| { tail -30 "$work/torches.log"; exit 1; }

echo "== 2. object archives"
for theme in "${themes[@]}"; do
	kit object --glb "$work/glb/$theme-torch.glb" --name torch --out "content/evening/objects/$theme/torch" \
		--sam tools/evening/torch.sam --script tools/evening/torch.rse --force
done

echo "done: content/evening"
