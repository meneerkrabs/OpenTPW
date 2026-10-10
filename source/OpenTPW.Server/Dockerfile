# OpenTPW server image (docs/SERVER.md). Build from the repository root:
#   docker build -f source/OpenTPW.Server/Dockerfile -t opentpw-server .
# The image also serves the browser build of the game at the site root (docs/WEB.md);
# --build-arg WEB_CLIENT=false builds the API and chat only.
# It contains only OpenTPW code. Original game files are never copied in: players choose their
# own game folder in the browser, and the optional word filter is mounted read-only.
ARG WEB_CLIENT=true

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src
COPY source/OpenTPW.Online/ source/OpenTPW.Online/
COPY source/OpenTPW.Server/ source/OpenTPW.Server/
RUN dotnet publish source/OpenTPW.Server/OpenTPW.Server.csproj -c Release -o /app --nologo

# The browser build compiles the game's own sources (no game data) to WebAssembly.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS web
WORKDIR /src
COPY content/shaders/ content/shaders/
COPY content/data/ content/data/
COPY source/ source/
RUN dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -o /web --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-false
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
