#!/usr/bin/env bash
# [EXT:online-coop-snapshot-join] The native join smoke (docs/COOP.md, "The game's join"): the host phase builds and shares a Jungle park and
# records its session (its frames, two captures and its evidence); the join phase, a fresh process, joins from the front end through the game's join
# and follows the recorded host to the end, resyncing once on the way. Both phases are silent and offscreen.
#
# Usage: scripts/smoke-coop-join.sh ['/path/to/Theme Park World'] (the game path may also come from OPENTPW_GAME_PATH).
# OPENTPW_SMOKE_LOG_DIR keeps both runs' logs and the evidence; a failed run's log is kept in any case. OPENTPW_COOP_JOIN_PHASE=measure runs the
# measuring join phase instead of the adversarial one (scripts/measure-coop-join.sh; docs/COOP.md, "Measurements"); OPENTPW_COOP_JOIN_PHASE=host
# runs the host phase only, for a join phase elsewhere (the browser's, with OPENTPW_SMOKE_LOG_DIR to keep its evidence). OPENTPW_COOP_HOST_EVIDENCE
# names a host phase recorded elsewhere (the browser's, scripts/smoke-coop-join-browser-host.sh), which the join phase replays instead of a native one.
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
phases=(host)
if [[ -n "${OPENTPW_COOP_HOST_EVIDENCE:-}" ]]; then
  # a host phase recorded elsewhere: its files copied in, the join phase alone
  if [[ ! -f "$OPENTPW_COOP_HOST_EVIDENCE/host-evidence.json" || "${OPENTPW_COOP_JOIN_PHASE:-join}" == host ]]; then
    printf '%s\n' 'OPENTPW_COOP_HOST_EVIDENCE names a host recording (its host-evidence.json) for a join phase, not a host phase.' >&2
    exit 2
  fi
  cp -R "$OPENTPW_COOP_HOST_EVIDENCE"/. "$work/evidence/"
  phases=()
fi
if [[ "${OPENTPW_COOP_JOIN_PHASE:-join}" != host ]]; then
  phases+=("${OPENTPW_COOP_JOIN_PHASE:-join}")
fi
for phase in "${phases[@]}"; do
  result=passed
  bash scripts/run.sh --game-path "$game" --coop-join-smoke "$phase" --coop-join-evidence "$work/evidence" > "$work/$phase.log" 2>&1 || result=FAILED
  printf 'smoke [coop-join-%s] %s (%s)\n' "$phase" "$result" "$(grep -E 'Native co-op join smoke' "$work/$phase.log" | tail -1 | cut -c1-200)"
  if [[ "$result" == FAILED || -n "${OPENTPW_SMOKE_LOG_DIR:-}" ]]; then
    logs="${OPENTPW_SMOKE_LOG_DIR:-${TMPDIR:-/tmp}}"
    mkdir -p "$logs" && cp "$work/$phase.log" "$logs/opentpw-coop-join-$phase.log" && printf '  log: %s/opentpw-coop-join-%s.log\n' "$logs" "$phase"
    # the evidence so far (the join phase's added to the host's), replacing the last phase's copy
    [[ -n "${OPENTPW_SMOKE_LOG_DIR:-}" ]] && rm -rf "$logs/opentpw-coop-join-evidence" && cp -R "$work/evidence" "$logs/opentpw-coop-join-evidence"
  fi
  if [[ "$result" == FAILED ]]; then
    status=1
    break
  fi
done
exit "$status"
