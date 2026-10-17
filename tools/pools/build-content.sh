#!/bin/bash
# Regenerates the swimming pools extension's art and content files under content/pools/ (everything except objects.json, which is a committed source file):
#
#   1. Blender builds the changing room cabin for each theme      tools/blender/opentpw_changing_room.py   -> <work>/glb/<theme>.glb
#   2. opentpw-kit object turns each glb into an object archive   tools/pools/cabin.sam, cabin.rse        -> content/pools/objects/<theme>/cabin/
#   3. Blender renders the eight TPW kid sets at 2x               tools/blender/opentpw_guest_sprites.py   -> <work>/guests/tpw/{Pool,Kids,Kidsheads}/<SET>/
#                 (the swimwear table of 14 slots, the clothes sets and the heads)
#   4. pack_sprite_sets.py packs them with opentpw-kit sprites    tools/pools/pack_sprite_sets.py          -> content/pools/esprites/Swim/SPR_xx.ESP/.TPC
#                                                                                                          -> content/pools/esprites/Generic/Kids/ and Kidsheads/
#
# Takes about 30 minutes on an Apple-silicon Mac, nearly all of it step 3 (about 2300 sprite frames per set at 2x, 5 directions each, of which the kid sets keep the slots 0, 1 and 14; steps 1, 2 and 4
# are a few seconds each, plus the first kit build). Idempotent: the outputs are replaced, never appended to. Needs Blender 5.x, the .NET SDK and python3.
#
# Environment:
#   BLENDER          Blender executable (default /Applications/Blender.app/Contents/MacOS/Blender)
#   DOTNET           dotnet executable (default: dotnet from PATH, else ~/.dotnet/dotnet)
#   KIT_ARTIFACTS    optional --artifacts-path for the kit build, to keep it apart from other builds
#   WORK             scratch folder (default /tmp/opentpw-pools-build); step 3 is skipped when WORK/guests already holds the rendered
#                    sets, so set FORCE_SPRITES=1 to render them again
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
blender="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
dotnet="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
work="${WORK:-/tmp/opentpw-pools-build}"
themes=(jungle hallow space fantasy)

kit_options=(-c Release)
if [ -n "${KIT_ARTIFACTS:-}" ]; then
	kit_options+=(--artifacts-path "$KIT_ARTIFACTS")
fi
kit() { "$dotnet" run "${kit_options[@]}" --project source/OpenTPW.ContentKit -- "$@"; }

mkdir -p "$work"

echo "== 1. changing room models"
rm -rf "$work/glb"
"$blender" -b --factory-startup --python tools/blender/opentpw_changing_room.py -- --out "$work/glb" --preview "$work/previews" > "$work/cabin.log" 2>&1 \
	|| { tail -30 "$work/cabin.log"; exit 1; }

echo "== 2. object archives"
for theme in "${themes[@]}"; do
	kit object --glb "$work/glb/$theme.glb" --name cabin --out "content/pools/objects/$theme/cabin" \
		--sam tools/pools/cabin.sam --script tools/pools/cabin.rse --force
done

echo "== 3. guest sprites (2x)"
if [ -n "${FORCE_SPRITES:-}" ] || [ ! -f "$work/guests/tpw/Pool/TA/set.json" ] || [ ! -f "$work/guests/tpw/Kidsheads/TA/set.json" ]; then
	rm -rf "$work/guests"
	sets=BE,BI,CH,FR,KI,SA,SU,TA
	"$blender" -b --factory-startup --python tools/blender/opentpw_guest_sprites.py -- \
		--edition TPW --part pool --scale 2 --sets $sets --out "$work/guests" > "$work/sprites-pool.log" 2>&1 \
		|| { tail -30 "$work/sprites-pool.log"; exit 1; }
	"$blender" -b --factory-startup --python tools/blender/opentpw_guest_sprites.py -- \
		--edition TPW --part land,heads --scale 2 --sets $sets --out "$work/guests" > "$work/sprites-land.log" 2>&1 \
		|| { tail -30 "$work/sprites-land.log"; exit 1; }
else
	echo "   (using the sets already rendered in $work/guests; FORCE_SPRITES=1 renders them again)"
fi

echo "== 4. pack sprite sets"
kit_command="$dotnet run ${kit_options[*]} --project source/OpenTPW.ContentKit --"
rm -rf "$work/stage"
python3 -I tools/pools/pack_sprite_sets.py --kind pool --frames "$work/guests" --out content/pools/esprites/Swim --stage "$work/stage/pool" --kit "$kit_command"
python3 -I tools/pools/pack_sprite_sets.py --kind kids --slots 0,1,14 --frames "$work/guests" --out content/pools/esprites/Generic/Kids --stage "$work/stage/kids" --kit "$kit_command"
python3 -I tools/pools/pack_sprite_sets.py --kind heads --frames "$work/guests" --out content/pools/esprites/Generic/Kidsheads --stage "$work/stage/heads" --kit "$kit_command"

echo "== 5. pin the sprite files for the browser delivery"
python3 -I tools/pools/sprite_manifest.py --write

echo "done: content/pools"
