#!/bin/bash
# Regenerates the winter extension's art and content files under content/winter/ (everything except objects.json, which is a committed source file):
#
#   1. Blender builds the winter kiosk and the gluhwein stall for each theme     tools/blender/opentpw_winter_shops.py    -> <work>/glb/<theme>-<shop>.glb
#   2. opentpw-kit object turns each glb into an object archive                  tools/winter/<shop>.sam, <shop>.rse      -> content/winter/objects/<theme>/<shop>/
#   3. Blender renders the warm item sprites (hats, earmuffs, mittens; 5 directions, 2x, the pools kid camera, over a holdout head)
#                                                                                tools/blender/opentpw_winter_items.py    -> <work>/frames/tpw/Winter/WI/
#   4. pack_sprites.py packs them with opentpw-kit sprites                       tools/winter/pack_sprites.py             -> content/winter/esprites/Winter/SPR_WI.ESP/.TPC
#   5. Blender emits the schema 2 anchors of the eight 2x kid sets (head top, head centre, both hands, hand visibility, for every land slot and frame; nothing is rendered)
#                                                                                tools/blender/opentpw_guest_sprites.py --emit-winter-anchors -> content/winter/anchors.json
#
# Takes about a minute. Idempotent: the outputs are replaced, never appended to. Needs Blender 5.x, the .NET SDK and python3.
# Nothing from the original games is read or written: every file is our own art and numbers.
#
# Environment:
#   BLENDER          Blender executable (default /Applications/Blender.app/Contents/MacOS/Blender)
#   DOTNET           dotnet executable (default: dotnet from PATH, else ~/.dotnet/dotnet)
#   KIT_ARTIFACTS    optional --artifacts-path for the kit build, to keep it apart from other builds
#   WORK             scratch folder (default /tmp/opentpw-winter-build)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
blender="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
dotnet="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
work="${WORK:-/tmp/opentpw-winter-build}"
themes=(jungle hallow space fantasy)
shops=(kiosk gluehwein)

kit_options=(-c Release)
if [ -n "${KIT_ARTIFACTS:-}" ]; then
	kit_options+=(--artifacts-path "$KIT_ARTIFACTS")
fi
kit() { "$dotnet" run "${kit_options[@]}" --project source/OpenTPW.ContentKit -- "$@"; }

mkdir -p "$work"

echo "== 1. winter shop models"
rm -rf "$work/glb"
"$blender" -b --factory-startup --python tools/blender/opentpw_winter_shops.py -- --out "$work/glb" --preview "$work/previews" > "$work/shops.log" 2>&1 \
	|| { tail -30 "$work/shops.log"; exit 1; }

echo "== 2. object archives"
for theme in "${themes[@]}"; do
	for shop in "${shops[@]}"; do
		kit object --glb "$work/glb/$theme-$shop.glb" --name "$shop" --out "content/winter/objects/$theme/$shop" \
			--sam "tools/winter/$shop.sam" --script "tools/winter/$shop.rse" --force
	done
done

echo "== 3. warm item sprites (2x)"
rm -rf "$work/frames"
"$blender" -b --factory-startup --python tools/blender/opentpw_winter_items.py -- --out "$work/frames" --scale 2 > "$work/sprites.log" 2>&1 \
	|| { tail -30 "$work/sprites.log"; exit 1; }

echo "== 4. pack the sprite set"
kit_command="$dotnet run ${kit_options[*]} --project source/OpenTPW.ContentKit --"
python3 -I tools/winter/pack_sprites.py --frames "$work/frames" --out content/winter/esprites/Winter --stage "$work/stage" --kit "$kit_command"

echo "== 5. head and hand anchors of the 2x kid sets"
rm -rf "$work/anchors"
"$blender" -b --factory-startup --python tools/blender/opentpw_guest_sprites.py -- --edition TPW --part none --emit-winter-anchors --scale 2 --out "$work/anchors" > "$work/anchors.log" 2>&1 \
	|| { tail -30 "$work/anchors.log"; exit 1; }
cp "$work/anchors/tpw/winter-anchors.json" content/winter/anchors.json

echo "done: content/winter"
