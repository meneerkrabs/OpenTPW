# OpenTPW server image. Build from the repository's source/ directory:
#   docker build -f OpenTPW.Server/Dockerfile -t opentpw-server .
# The image contains only OpenTPW code. Original game files are never copied in; mount your
# own language folder read-only if you want the original word filter (see docs/SERVER.md).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY OpenTPW.Online/ OpenTPW.Online/
COPY OpenTPW.Server/ OpenTPW.Server/
RUN dotnet publish OpenTPW.Server/OpenTPW.Server.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    OpenTPW__DataDirectory=/data
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenTPW.Server.dll"]
