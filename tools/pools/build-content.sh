#!/bin/bash
# Regenerates the swimming pools extension's art and content files under content/pools/ (everything except objects.json, which is a committed source file):
#
#   1. Blender builds the changing room cabin for each theme      tools/blender/opentpw_changing_room.py   -> <work>/glb/<theme>.glb
#   2. opentpw-kit object turns each glb into an object archive   tools/pools/cabin.sam, cabin.rse        -> content/pools/objects/<theme>/cabin/
#   2b. the pool extras (ladder, diving board, shower, lounger, lamp, underwater light) the same way    tools/blender/opentpw_pool_extras.py, tools/pools/extra-*.sam, extra.rse -> content/pools/objects/<theme>/<extra>/
#   2b. Blender builds the water slide towers (flume and tube)    tools/blender/opentpw_slide_tower.py     -> <work>/towers/<theme>-<profile>.glb, then the kit
#                                                                tools/pools/slide-tower-<profile>.sam, slide-tower.rse -> content/pools/objects/<theme>/<profile>tower/
#   3. Blender renders the eight TPW kid sets at 2x               tools/blender/opentpw_guest_sprites.py   -> <work>/guests/tpw/{Pool,Kids,Kidsheads}/<SET>/
#                 (the swimwear table of 14 slots, the clothes sets and the heads)
#   4. pack_sprite_sets.py packs them with opentpw-kit sprites    tools/pools/pack_sprite_sets.py          -> content/pools/esprites/Swim/SPR_xx.ESP/.TPC
#                                                                                                          -> content/pools/esprites/Generic/Kids/ and Kidsheads/
#   5. Blender builds the water attractions (rope swing, jets,    tools/blender/opentpw_pool_attractions.py -> <work>/attractions/<kind>_<theme>.glb
#      wave machine, spa, large spa) for each theme, the kit makes archives  tools/pools/<kind>.sam, attraction.rse    -> content/pools/objects/<theme>/<kind>/
#   6. sprite_manifest.py pins the sprite files for the browser   tools/pools/sprite_manifest.py         -> content/pools/esprites.manifest.json
#
# Takes about 30 minutes on an Apple-silicon Mac, nearly all of it step 3 (about 2300 sprite frames per set at 2x, 5 directions each, of which the kid sets keep the slots 0, 1 and 14; steps 1, 2, 2b and 4
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

echo "== 2b. pool extras"
rm -rf "$work/extras"
"$blender" -b --factory-startup --python tools/blender/opentpw_pool_extras.py -- --out "$work/extras" --preview "$work/extra-previews" > "$work/extras.log" 2>&1 \
	|| { tail -30 "$work/extras.log"; exit 1; }
for theme in "${themes[@]}"; do
	for extra in ladder board shower lounger lamp light; do
		kit object --glb "$work/extras/$theme-$extra.glb" --name "$extra" --out "content/pools/objects/$theme/$extra" \
			--sam "tools/pools/extra-$extra.sam" --script tools/pools/extra.rse --force
	done
done

echo "== 2b. water slide towers"
rm -rf "$work/towers"
"$blender" -b --factory-startup --python tools/blender/opentpw_slide_tower.py -- --out "$work/towers" --preview "$work/previews" > "$work/towers.log" 2>&1 \
	|| { tail -30 "$work/towers.log"; exit 1; }
for theme in "${themes[@]}"; do
	for profile in flume tube; do
		kit object --glb "$work/towers/$theme-$profile.glb" --name "${profile}tower" --out "content/pools/objects/$theme/${profile}tower" \
			--sam "tools/pools/slide-tower-$profile.sam" --script tools/pools/slide-tower.rse --force
	done
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

echo "== 5. water attractions"
rm -rf "$work/attractions"
"$blender" -b --factory-startup --python tools/blender/opentpw_pool_attractions.py -- --out "$work/attractions" --preview "$work/previews" > "$work/attractions.log" 2>&1 \
	|| { tail -30 "$work/attractions.log"; exit 1; }
for kind in ropeswing jets wavemachine spa bigspa; do
	for theme in "${themes[@]}"; do
		kit object --glb "$work/attractions/${kind}_${theme}.glb" --name "$kind" --out "content/pools/objects/$theme/$kind" \
			--sam "tools/pools/$kind.sam" --script tools/pools/attraction.rse --force
	done
done

echo "== 6. pin the sprite files for the browser delivery"
python3 -I tools/pools/sprite_manifest.py --write

echo "done: content/pools"
