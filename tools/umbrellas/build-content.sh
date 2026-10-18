#!/bin/bash
# Regenerates the umbrellas extension's art and content files under content/umbrellas/ (everything except objects.json, which is a committed source file):
#
#   1. Blender builds the brolly shop for each theme               tools/blender/opentpw_brolly_shop.py     -> <work>/glb/<theme>.glb
#   2. opentpw-kit object turns each glb into an object archive    tools/umbrellas/brolly.sam, brolly.rse   -> content/umbrellas/objects/<theme>/brolly/
#   3. Blender renders the umbrella sprites (3 colourways, 5 directions, 2x, the pools kid camera, hotspot = handle tip)
#                                                                  tools/blender/opentpw_umbrella.py        -> <work>/frames/tpw/Umbrella/UM/
#   4. pack_sprites.py packs them with opentpw-kit sprites         tools/umbrellas/pack_sprites.py          -> content/umbrellas/esprites/Umbrellas/SPR_UM.ESP/.TPC
#   5. Blender emits the hand anchors of the eight 2x kid sets (the rig's hand through the frames' camera; nothing is rendered)
#                                                                  tools/blender/opentpw_guest_sprites.py --emit-anchors -> content/umbrellas/anchors.json
#
# Takes about a minute (the sprites are 15 small frames; the first kit build adds a little). Idempotent: the outputs are replaced, never appended to. Needs Blender 5.x, the .NET SDK and python3.
# Nothing from the original games is read or written: every file is our own art and numbers.
#
# Environment:
#   BLENDER          Blender executable (default /Applications/Blender.app/Contents/MacOS/Blender)
#   DOTNET           dotnet executable (default: dotnet from PATH, else ~/.dotnet/dotnet)
#   KIT_ARTIFACTS    optional --artifacts-path for the kit build, to keep it apart from other builds
#   WORK             scratch folder (default /tmp/opentpw-umbrellas-build)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
blender="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
dotnet="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
work="${WORK:-/tmp/opentpw-umbrellas-build}"
themes=(jungle hallow space fantasy)

kit_options=(-c Release)
if [ -n "${KIT_ARTIFACTS:-}" ]; then
	kit_options+=(--artifacts-path "$KIT_ARTIFACTS")
fi
kit() { "$dotnet" run "${kit_options[@]}" --project source/OpenTPW.ContentKit -- "$@"; }

mkdir -p "$work"

echo "== 1. brolly shop models"
rm -rf "$work/glb"
"$blender" -b --factory-startup --python tools/blender/opentpw_brolly_shop.py -- --out "$work/glb" --preview "$work/previews" > "$work/shop.log" 2>&1 \
	|| { tail -30 "$work/shop.log"; exit 1; }

echo "== 2. object archives"
for theme in "${themes[@]}"; do
	kit object --glb "$work/glb/$theme.glb" --name brolly --out "content/umbrellas/objects/$theme/brolly" \
		--sam tools/umbrellas/brolly.sam --script tools/umbrellas/brolly.rse --force
done

echo "== 3. umbrella sprites (2x)"
rm -rf "$work/frames"
"$blender" -b --factory-startup --python tools/blender/opentpw_umbrella.py -- --out "$work/frames" --scale 2 > "$work/sprites.log" 2>&1 \
	|| { tail -30 "$work/sprites.log"; exit 1; }

echo "== 4. pack the sprite set"
kit_command="$dotnet run ${kit_options[*]} --project source/OpenTPW.ContentKit --"
python3 -I tools/umbrellas/pack_sprites.py --frames "$work/frames" --out content/umbrellas/esprites/Umbrellas --stage "$work/stage" --kit "$kit_command"

echo "== 5. hand anchors of the 2x kid sets"
rm -rf "$work/anchors"
"$blender" -b --factory-startup --python tools/blender/opentpw_guest_sprites.py -- --edition TPW --part none --emit-anchors --scale 2 --out "$work/anchors" > "$work/anchors.log" 2>&1 \
	|| { tail -30 "$work/anchors.log"; exit 1; }
cp "$work/anchors/tpw/anchors.json" content/umbrellas/anchors.json

echo "done: content/umbrellas"
