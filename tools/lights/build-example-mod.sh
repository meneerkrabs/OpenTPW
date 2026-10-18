#!/bin/sh
# Rebuilds content/mods/opentpw.example.lamp-lights from tools/lights (the .NET SDK and python3; no Blender).
#   sh tools/lights/build-example-mod.sh
set -eu
root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d "${TMPDIR:-/tmp}/lamp.XXXXXX")
trap 'rm -rf "$work"' EXIT
mod="$root/content/mods/opentpw.example.lamp-lights"
python3 -I "$root/tools/lights/make_example_lamp.py" "$work/lamp.glb"
rm -rf "$mod"
dotnet run --project "$root/source/OpenTPW.ContentKit" -- object --glb "$work/lamp.glb" --name lamp --out "$work/lamp" \
	--sam "$root/tools/lights/lamp.sam" --script "$root/tools/lights/lamp.rse"
# the kit's `mod` command writes community mods only; the example is an official one (opentpw. id, content/mods), so its two documents are written here
mkdir -p "$mod/objects"
cp -R "$work/lamp" "$mod/objects/lamp"
cat > "$mod/opentpw-mod.json" <<'JSON'
{ "schema": 1, "id": "opentpw.example.lamp-lights", "name": "Lamp light (example)", "version": "1.0.0", "category": "official",
  "author": "OpenTPW",
  "description": "An example data mod: a lamp post whose light the object's script pulses, flickers and colour-cycles with the ENABLELIGHT, DISABLELIGHT, SETLIGHT and COLOURLIGHT opcodes (docs/LIGHTS.md). Adds a feature to the jungle park. Off by default.",
  "content": { "objects": "objects.json" } }
JSON
cat > "$mod/objects.json" <<'JSON'
{ "schema": 1, "idRange": { "first": 64000, "last": 64009 },
  "objects": [
    { "archive": "objects/lamp", "theme": "jungle", "category": "features", "infoId": 64001, "names": { "English": "Lamp Post" } } ] }
JSON
rm -rf "$work"
