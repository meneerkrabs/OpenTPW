#!/usr/bin/env bash
# [EXT:online-coop-snapshot-join] The co-op join's measurements (docs/COOP.md, "Measurements"): the native join smoke's host phase and its measuring join
# phase (the untouched captures, joined and resynced once each), in a Release build, several times, each phase a fresh process, silent and offscreen;
# then the median, the range and every run's value of each timing (tools/coop_join_timings.py).
#
# Usage: scripts/measure-coop-join.sh [runs, default 5] ['/path/to/Theme Park World'] (the game path may also come from OPENTPW_GAME_PATH).
# OPENTPW_MEASURE_DIR keeps every run's logs and evidence (a temporary folder otherwise, removed at the end).
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
runs="${1:-5}"
game="${2:-${OPENTPW_GAME_PATH:-}}"
if [[ -z "$game" ]]; then
  printf '%s\n' 'Pass the Theme Park World path (or set OPENTPW_GAME_PATH).' >&2
  exit 2
fi
output="${OPENTPW_MEASURE_DIR:-}"
if [[ -z "$output" ]]; then
  output="$(mktemp -d "${TMPDIR:-/tmp}/opentpw-coop-join-measure-XXXXXX")"
  trap 'rm -rf "$output"' EXIT
fi
mkdir -p "$output"
export OPENTPW_CONFIGURATION=Release OPENTPW_COOP_JOIN_PHASE=measure OPENTPW_SILENT=1 OPENTPW_OFFSCREEN=1
evidence=()
for run in $(seq 1 "$runs"); do
  OPENTPW_SMOKE_LOG_DIR="$output/run-$run" bash scripts/smoke-coop-join.sh "$game"
  evidence+=("$output/run-$run/opentpw-coop-join-evidence")
done
python3 -I tools/coop_join_timings.py "${evidence[@]}"
