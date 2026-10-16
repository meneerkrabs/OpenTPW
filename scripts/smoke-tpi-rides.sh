#!/usr/bin/env bash
# Native smokes with the example Theme Park Inc rides mod (docs/MODS.md, "Making a Theme Park Inc rides mod"):
# the sandbox smoke builds the mod's rides, shop and bin through the park economy, and the park-resume smoke
# saves and restores a Jungle park whose rides are the mod's Turban Bounce. Nothing of Theme Park Inc is copied:
# the mod is two small files written to a temporary folder, and the install is read where it is.
#
# Usage: OPENTPW_TPI_PATH='/path/to/Theme Park Inc' scripts/smoke-tpi-rides.sh ['/path/to/Theme Park World']
# (the game path may also come from OPENTPW_GAME_PATH). Run it where a window can open, not while the screen is locked.
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
game="${1:-${OPENTPW_GAME_PATH:-}}"
tpi="${OPENTPW_TPI_PATH:-}"
if [[ -z "$game" || -z "$tpi" ]]; then
  printf '%s\n' 'Set OPENTPW_TPI_PATH to a Theme Park Inc install (or its Data folder) and pass the Theme Park World path (or set OPENTPW_GAME_PATH).' >&2
  exit 2
fi
work="$(mktemp -d "${TMPDIR:-/tmp}/opentpw-tpi-smoke-XXXXXX")"
trap 'rm -rf "$work"' EXIT
mod="$work/community.tpi-rides"
mkdir -p "$mod" "$work/config" "$work/evidence"
cat > "$mod/opentpw-mod.json" <<'JSON'
{ "schema": 1, "id": "community.tpi-rides", "name": "Theme Park Inc rides", "version": "1.0.0", "category": "community",
  "edition": "theme-park-world",
  "dependencies": [ { "id": "opentpw.cross-edition-objects", "version": ">=1.0.0" } ],
  "inputs": [ { "id": "tpi", "kind": "original-data", "edition": "theme-park-inc" } ],
  "content": { "objects": "objects.json" } }
JSON
cat > "$mod/objects.json" <<'JSON'
{ "schema": 1, "idRange": { "first": 40000, "last": 40099 }, "objects": [
  { "archive": "levels/arabian/rides/tbounce", "input": "tpi", "theme": "jungle", "category": "rides", "infoId": 40001, "names": { "English": "Turban Bounce" } },
  { "archive": "levels/science/rides/robot", "input": "tpi", "theme": "space", "category": "rides", "infoId": 40002, "names": { "English": "Giant Robot Swing" } },
  { "archive": "levels/science/Facilities/litbin", "input": "tpi", "theme": "space", "category": "features", "infoId": 40003, "names": { "English": "Science Litter Bin" } },
  { "archive": "levels/arabian/shops/drinks", "input": "tpi", "theme": "jungle", "category": "shops", "infoId": 40004, "names": { "English": "Desert Drinks" } } ] }
JSON
mod_options=(--mod-dir "$mod" --enable-mod opentpw.cross-edition-objects --enable-mod community.tpi-rides --mod-input "community.tpi-rides:tpi=$tpi")
# A private configuration folder, like a developer's smoke runs: copies of the small settings files (not mods.json, whose choices the
# command line below replaces) and links to the large art folders, so the runs never write the real configuration.
source_config="${OPENTPW_SMOKE_CONFIG_SOURCE:-$HOME/Library/Application Support/OpenTPW}"
for file in "$source_config"/*.json; do
  [[ -f "$file" && "$(basename "$file")" != mods.json ]] && cp "$file" "$work/config/"
done
for folder in art hero-art texture-packs; do
  [[ -d "$source_config/$folder" ]] && ln -s "$source_config/$folder" "$work/config/$folder"
done
export OPENTPW_CONFIG_DIR="$work/config"
export OPENTPW_SILENT=1
status=0
run() {
  local name="$1"; shift
  local result=passed
  bash scripts/run.sh --game-path "$game" "$@" "${mod_options[@]}" > "$work/$name.log" 2>&1 || result=FAILED
  printf 'smoke [%s] %s (%s)\n' "$name" "$result" "$(tail -1 "$work/$name.log" | cut -c1-160)"
  # a failed run's log is kept; OPENTPW_SMOKE_LOG_DIR keeps every run's
  if [[ "$result" == FAILED || -n "${OPENTPW_SMOKE_LOG_DIR:-}" ]]; then
    local logs="${OPENTPW_SMOKE_LOG_DIR:-${TMPDIR:-/tmp}}"
    mkdir -p "$logs" && cp "$work/$name.log" "$logs/opentpw-tpi-smoke-$name.log" && printf '  log: %s/opentpw-tpi-smoke-%s.log\n' "$logs" "$name"
  fi
  [[ "$result" == passed ]] || status=1
}
run sandbox --sandbox --smoke-test
export OPENTPW_SMOKE_MODS=1
for phase in capture restore; do
  run "resume-$phase" --no-advisor --park-resume-smoke "$phase" --park-resume-file "$work/park.otpw" --park-resume-evidence "$work/evidence"
done
exit "$status"
