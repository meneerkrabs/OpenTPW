#!/usr/bin/env bash
# [EXT:online-coop-snapshot-join] The co-op join's measurements with a browser host (docs/COOP.md, "The browser's host"): several runs, each the page's host
# phase in a fresh headless, muted Chrome profile and the native measuring join phase over its recording (scripts/smoke-coop-join-browser-host.sh), in a
# Release build, silent and offscreen; then the median, the range and every run's value of each timing (tools/coop_join_timings.py), the page host's
# captures and long frames among them.
#
# Usage: scripts/measure-coop-join-browser-host.sh <published wwwroot> [runs, default 3] ['/path/to/Theme Park World'] (the game path may also come from
# OPENTPW_GAME_PATH). Publish the page first: dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release [-p:RunAOTCompilation=true] -o <folder>,
# whose wwwroot this takes. OPENTPW_MEASURE_DIR keeps every run's recording, logs and evidence (a temporary folder otherwise, removed at the end).
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
web="${1:-}"
runs="${2:-3}"
game="${3:-${OPENTPW_GAME_PATH:-}}"
if [[ -z "$web" || -z "$game" ]]; then
  printf '%s\n' 'Pass the published wwwroot and the Theme Park World path (or set OPENTPW_GAME_PATH).' >&2
  exit 2
fi
if [[ ! "$runs" =~ ^[1-9][0-9]*$ ]]; then
  printf '%s\n' "The runs are a whole number, not '$runs'." >&2
  exit 2
fi
output="${OPENTPW_MEASURE_DIR:-}"
if [[ -z "$output" ]]; then
  output="$(mktemp -d "${TMPDIR:-/tmp}/opentpw-coop-join-browser-host-XXXXXX")"
  trap 'rm -rf "$output"' EXIT
fi
for ((run = 1; run <= runs; run++)); do
  if [[ -e "$output/run-$run" ]]; then
    printf '%s\n' "$output/run-$run exists already: choose another OPENTPW_MEASURE_DIR." >&2
    exit 2
  fi
done
mkdir -p "$output"
export OPENTPW_CONFIGURATION=Release OPENTPW_COOP_JOIN_PHASE=measure OPENTPW_SILENT=1 OPENTPW_OFFSCREEN=1
evidence=()
for ((run = 1; run <= runs; run++)); do
  OPENTPW_SMOKE_LOG_DIR="$output/run-$run" bash scripts/smoke-coop-join-browser-host.sh "$web" "$game"
  evidence+=("$output/run-$run/opentpw-coop-join-evidence")
done
# the machine as the page's driver described it, with the page's recording
python3 -I tools/coop_join_timings.py --machine "$output/run-1/browser-host/machine.txt" "${evidence[@]}"
