#!/usr/bin/env bash
# [EXT:online-coop-snapshot-join] The co-op join's measurements in the browser (docs/COOP.md, "Measurements"): the native join smoke's host phase once,
# in a Release build, silent and offscreen; then the page's measuring join phase over that recording, several times, each in a fresh headless, muted
# Chrome profile (tools/web-coop-join.mjs); then the median, the range and every run's value of each timing (tools/coop_join_timings.py), the reused
# host phase counted once.
#
# Usage: scripts/measure-coop-join-browser.sh <published wwwroot> <label, such as aot or interpreter> [runs, default 5] ['/path/to/Theme Park World']
# (the game path may also come from OPENTPW_GAME_PATH). Publish the page first: dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release
# [-p:RunAOTCompilation=true] -o <folder>, whose wwwroot this takes. OPENTPW_MEASURE_DIR keeps the host phase's evidence and every run's (a temporary
# folder otherwise, removed at the end).
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
web="${1:-}"
label="${2:-}"
runs="${3:-5}"
game="${4:-${OPENTPW_GAME_PATH:-}}"
if [[ -z "$web" || -z "$label" || -z "$game" ]]; then
  printf '%s\n' 'Pass the published wwwroot, a label and the Theme Park World path (or set OPENTPW_GAME_PATH).' >&2
  exit 2
fi
if [[ ! "$runs" =~ ^[1-9][0-9]*$ ]]; then
  printf '%s\n' "The runs are a whole number, not '$runs'." >&2
  exit 2
fi
web="$(cd "$web" && pwd)"
output="${OPENTPW_MEASURE_DIR:-}"
if [[ -z "$output" ]]; then
  output="$(mktemp -d "${TMPDIR:-/tmp}/opentpw-coop-join-browser-XXXXXX")"
  trap 'rm -rf "$output"' EXIT
fi
# earlier runs in the folder would mix with these
for existing in "$output/host" "$output/$label"; do
  if [[ -e "$existing" ]]; then
    printf '%s\n' "$existing exists already: choose another OPENTPW_MEASURE_DIR or label." >&2
    exit 2
  fi
done
mkdir -p "$output"
# the host phase once: the page's runs replay its recording
OPENTPW_CONFIGURATION=Release OPENTPW_COOP_JOIN_PHASE=host OPENTPW_SMOKE_LOG_DIR="$output/host" OPENTPW_SILENT=1 OPENTPW_OFFSCREEN=1 \
  bash scripts/smoke-coop-join.sh "$game"
node tools/web-coop-join.mjs --web "$web" --game "$game" --evidence "$output/host/opentpw-coop-join-evidence" --mode measure --label "$label" \
  --runs "$runs" --out "$output/$label"
runs_found=()
for ((run = 1; run <= runs; run++)); do
  runs_found+=("$output/$label/run-$run")
done
python3 -I tools/coop_join_timings.py --machine "$output/$label/machine.txt" "${runs_found[@]}"
