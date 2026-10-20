# OpenTPW server image (docs/SERVER.md). Build from the repository root:
#   docker build -f source/OpenTPW.Server/Dockerfile -t opentpw-server .
# The image also serves the browser build of the game at the site root (docs/WEB.md);
# --build-arg WEB_CLIENT=false builds the API and chat only.
# It contains only OpenTPW code. Original game files are never copied in: players choose their
# own game folder in the browser, and the optional word filter is mounted read-only.
ARG WEB_CLIENT=true

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS server
ARG BUILD_REVISION
WORKDIR /src
COPY source/OpenTPW.Online/ source/OpenTPW.Online/
COPY source/OpenTPW.Server/ source/OpenTPW.Server/
RUN dotnet publish source/OpenTPW.Server/OpenTPW.Server.csproj -c Release -o /app --nologo -p:SourceRevisionId="$BUILD_REVISION"

# The browser build compiles the game's own sources (no game data) to WebAssembly, ahead of time:
# AOT halves the cold park construction and the first buy-menu frames against the interpreter, with
# identical texture and simulation digests, for about 5 MB more download (docs/BROWSER-PERFORMANCE.md).
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS web
ARG BUILD_REVISION
# --build-arg WEB_AOT=false builds the interpreter page instead (CI's pull-request check: same sources, a few minutes faster;
# the images CI publishes from main and the release tags stay AOT).
ARG WEB_AOT=true
WORKDIR /src
# Emscripten, which the workload drives, runs on Python; the SDK image has none.
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/* \
    && dotnet workload install wasm-tools
COPY content/shaders/ content/shaders/
COPY content/data/ content/data/
# The shipped data mods and the official extensions' own content: embedded in the page (objects.json, sprites, anchors; OpenTPW.Web.csproj checks that each folder is there), and the pools sprite sets are
# published as static files (pools-sprites/).
COPY content/mods/ content/mods/
COPY content/pools/ content/pools/
COPY content/umbrellas/ content/umbrellas/
COPY content/winter/ content/winter/
COPY content/evening/ content/evening/
COPY source/ source/
RUN dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -p:RunAOTCompilation="$WEB_AOT" -o /web --nologo -p:SourceRevisionId="$BUILD_REVISION"

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime-false
# Links the published image to the repository, whose visibility it then shares.
LABEL org.opencontainers.image.source=https://github.com/meneerkrabs/OpenTPW \
      org.opencontainers.image.description="OpenTPW server: online play and the browser game" \
      org.opencontainers.image.licenses=MIT
WORKDIR /app
COPY --from=server /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    OpenTPW__DataDirectory=/data
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenTPW.Server.dll"]

FROM runtime-false AS runtime-true
COPY --from=web /web/wwwroot /app/web
ENV OpenTPW__WebClientDirectory=/app/web

FROM runtime-${WEB_CLIENT}
