#!/usr/bin/env bash
# [EXT:online-coop-snapshot-join] The co-op join smoke with a browser host (docs/COOP.md, "The browser's host"): the page records the host phase in a fresh
# headless, muted Chrome profile (tools/web-coop-join.mjs --mode host), and the native join phase replays that recording with every checkpoint compared
# (scripts/smoke-coop-join.sh with OPENTPW_COOP_HOST_EVIDENCE), silent and offscreen.
#
# Usage: scripts/smoke-coop-join-browser-host.sh <published wwwroot> ['/path/to/Theme Park World'] (the game path may also come from OPENTPW_GAME_PATH).
# Publish the page first: dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release [-p:RunAOTCompilation=true] -o <folder>, whose wwwroot this
# takes. OPENTPW_COOP_JOIN_PHASE=measure runs the measuring join phase instead of the adversarial one. OPENTPW_SMOKE_LOG_DIR keeps the recording, the
# join phase's log and its evidence (a temporary folder otherwise, removed at the end).
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
web="${1:-}"
game="${2:-${OPENTPW_GAME_PATH:-}}"
if [[ -z "$web" || -z "$game" ]]; then
  printf '%s\n' 'Pass the published wwwroot and the Theme Park World path (or set OPENTPW_GAME_PATH).' >&2
  exit 2
fi
if [[ "${OPENTPW_COOP_JOIN_PHASE:-join}" == host ]]; then
  printf '%s\n' 'The browser records the host phase here: OPENTPW_COOP_JOIN_PHASE is join or measure.' >&2
  exit 2
fi
logs="${OPENTPW_SMOKE_LOG_DIR:-}"
if [[ -z "$logs" ]]; then
  logs="$(mktemp -d "${TMPDIR:-/tmp}/opentpw-coop-join-browser-host-XXXXXX")"
  trap 'rm -rf "$logs"' EXIT
fi
recording="$logs/browser-host"
if [[ -e "$recording" ]]; then
  printf '%s\n' "$recording exists already: choose another OPENTPW_SMOKE_LOG_DIR." >&2
  exit 2
fi
node tools/web-coop-join.mjs --web "$web" --game "$game" --mode host --out "$recording"
OPENTPW_COOP_HOST_EVIDENCE="$recording" OPENTPW_SMOKE_LOG_DIR="$logs" bash scripts/smoke-coop-join.sh "$game"
