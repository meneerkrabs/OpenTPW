#!/usr/bin/env bash
# [EXT:online-coop-session] The browser's link to a real server, the transport only (docs/COOP.md, "The browser's link to a real server"): builds the server,
# then tools/web-coop-link.mjs runs it on 127.0.0.1 with a fresh data folder and the published page as its own, and drives a host page and a guest page of it
# in headless, muted Chrome through a shared session: login, the co-op socket, the session, a snapshot up and down over HTTP, a frame and a message each way. No game
# data is used.
#
# Usage: scripts/smoke-coop-link-browser.sh <published wwwroot>. Publish the page first: dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release
# [-p:RunAOTCompilation=true] -o <folder>, whose wwwroot this takes. OPENTPW_SMOKE_LOG_DIR keeps the run's record and the server's log (in coop-link/ there,
# which must not exist yet). DOTNET names the dotnet to build and run the server with.
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
web="${1:-}"
if [[ -z "$web" ]]; then
  printf '%s\n' 'Pass the published wwwroot.' >&2
  exit 2
fi
dotnet="${DOTNET:-dotnet}"
"$dotnet" build source/OpenTPW.Server/OpenTPW.Server.csproj -c Release -v q -nologo > /dev/null
server="source/OpenTPW.Server/bin/Release/net11.0/OpenTPW.Server.dll"
out=()
if [[ -n "${OPENTPW_SMOKE_LOG_DIR:-}" ]]; then
  mkdir -p "$OPENTPW_SMOKE_LOG_DIR"
  out=(--out "$OPENTPW_SMOKE_LOG_DIR/coop-link")
fi
DOTNET="$dotnet" node tools/web-coop-link.mjs --web "$web" --server "$server" ${out[@]+"${out[@]}"}
