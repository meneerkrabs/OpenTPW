#!/usr/bin/env bash
# [EXT:online-coop-snapshot-join] The native join smoke (docs/COOP.md, "The game's join"): the host phase builds and shares a Jungle park and
# records its session (its frames, two captures and its evidence); the join phase, a fresh process, joins from the front end through the game's join
# and follows the recorded host to the end, resyncing once on the way. Both phases are silent and offscreen.
#
# Usage: scripts/smoke-coop-join.sh ['/path/to/Theme Park World'] (the game path may also come from OPENTPW_GAME_PATH).
# OPENTPW_SMOKE_LOG_DIR keeps both runs' logs and the evidence; a failed run's log is kept in any case.
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
game="${1:-${OPENTPW_GAME_PATH:-}}"
if [[ -z "$game" ]]; then
  printf '%s\n' 'Pass the Theme Park World path (or set OPENTPW_GAME_PATH).' >&2
  exit 2
fi
work="$(mktemp -d "${TMPDIR:-/tmp}/opentpw-coop-join-smoke-XXXXXX")"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/config" "$work/evidence"
# A private configuration folder, like a developer's smoke runs: copies of the small settings files and links to the large art folders,
# so the runs never write the real configuration.
source_config="${OPENTPW_SMOKE_CONFIG_SOURCE:-$HOME/Library/Application Support/OpenTPW}"
for file in "$source_config"/*.json; do
  [[ -f "$file" ]] && cp "$file" "$work/config/"
done
for folder in art hero-art texture-packs; do
  [[ -d "$source_config/$folder" ]] && ln -s "$source_config/$folder" "$work/config/$folder"
done
export OPENTPW_CONFIG_DIR="$work/config"
export OPENTPW_SILENT=1 OPENTPW_OFFSCREEN=1
status=0
for phase in host join; do
  result=passed
  bash scripts/run.sh --game-path "$game" --coop-join-smoke "$phase" --coop-join-evidence "$work/evidence" > "$work/$phase.log" 2>&1 || result=FAILED
  printf 'smoke [coop-join-%s] %s (%s)\n' "$phase" "$result" "$(grep -E 'Native co-op join smoke' "$work/$phase.log" | tail -1 | cut -c1-200)"
  if [[ "$result" == FAILED || -n "${OPENTPW_SMOKE_LOG_DIR:-}" ]]; then
    logs="${OPENTPW_SMOKE_LOG_DIR:-${TMPDIR:-/tmp}}"
    mkdir -p "$logs" && cp "$work/$phase.log" "$logs/opentpw-coop-join-$phase.log" && printf '  log: %s/opentpw-coop-join-%s.log\n' "$logs" "$phase"
    [[ -n "${OPENTPW_SMOKE_LOG_DIR:-}" ]] && cp -R "$work/evidence" "$logs/opentpw-coop-join-evidence"
  fi
  if [[ "$result" == FAILED ]]; then
    status=1
    break
  fi
done
exit "$status"
