# Hosting an OpenTPW server

`source/OpenTPW.Server` is the server for the online extension (docs/ONLINE.md): accounts,
shared parks, postcards and chat. It can also serve the browser build of the game
(docs/WEB.md), so one address offers both the game and its online world.

The server and its image contain only OpenTPW code. They never contain original game files:
players use their own installation, in the browser by choosing their game folder.

## Running it for a server on the internet

Everything runs over HTTPS on port 443: the game page, the API (`/api/v1/…`) and the chat
WebSocket (`wss://…/api/v1/chat`). There is no plain-HTTP site; port 80 stays closed. The example
below puts Caddy in front of the server; Caddy gets and renews the certificate itself over port
443 (TLS-ALPN), sends HSTS, and passes the chat's WebSocket connections through. The OpenTPW
container only listens inside the Docker network.

1. Point the domain's DNS at the machine and open port 443 (TCP, and UDP for HTTP/3).
2. From the repository root:

   ```sh
   OPENTPW_DOMAIN=tpw.example.org docker compose -f source/OpenTPW.Server/docker-compose.example.yml up -d --build
   ```

3. Open `https://tpw.example.org/`, choose the game folder, then **Go online**: the server
   address is already filled in with the page's own address.

Desktop players use the same address (`https://tpw.example.org`) in the online login screen.

The files are `source/OpenTPW.Server/Dockerfile`, `docker-compose.example.yml` and
`deploy/Caddyfile`. Another TLS proxy (nginx, Traefik) works the same way: forward to port 8080 of
the container, pass WebSocket upgrades through, and keep the `Host` header.

`docker build --build-arg WEB_CLIENT=false …` builds an image with the API and chat only.

## The official server (`deploy/`)

`play.opentpw.io` runs the published image on a small Hetzner Cloud server (x86, Ubuntu
24.04). Everything it needs is in `deploy/`:

- `compose.yml` and `Caddyfile`: the image from `ghcr.io/meneerkrabs/opentpw-server` behind
  Caddy on ports 80 and 443; player data in `/opt/opentpw/data`; settings in `.env`
  (`env.example`). Caddy keeps no access log.
- `opentpw-update.timer`: every night, pull the image and restart if it changed. `latest`
  follows releases; CI publishes it for every `v*` tag (and `main` for every push to main).
- `opentpw-backup.timer`: every night, a copy of the data folder in `/var/backups/opentpw`,
  kept for seven days. It is on the same disk; download it now and then, or turn on Hetzner's
  backups, for a copy elsewhere.
- `make-cloud-init.py`: writes the cloud-init user data that sets all of this up on the first
  boot of a fresh server, with SSH password logins turned off:

  ```sh
  python3 deploy/make-cloud-init.py --domain play.opentpw.io > user-data.yml
  ```

Give the server a Hetzner Cloud Firewall that allows TCP and UDP 443 from everywhere and TCP 22
only from the maintainer's own addresses, and nothing else; port 80 stays closed (Docker's
published ports bypass a firewall on the server itself, so the firewall belongs outside it).
SSH accepts keys only, and fail2ban bans addresses after repeated failed logins. Point the
domain's DNS (A and AAAA, not proxied) at the server. When the maintainer's address changes,
update the firewall rule, or use Hetzner's web console to get in.

## Running it locally

```sh
dotnet run --project source/OpenTPW.Server -- --urls http://127.0.0.1:5000
```

This serves the API and chat only. To serve the browser game as well, publish it and point
the server at it:

```sh
dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -o out/web
dotnet run --project source/OpenTPW.Server -- --urls http://127.0.0.1:5000 --OpenTPW:WebClientDirectory=out/web/wwwroot
```

Plain HTTP is for testing on one machine only.

## Settings

Settings are in `OpenTPW.Server/appsettings.json`, or environment variables named
`OpenTPW__<setting>` (as in the compose example), or command-line options
`--OpenTPW:<setting>=…`.

| Setting | Default | Meaning |
| --- | --- | --- |
| `ServerName`, `Message` | | Shown in server info; the message is also sent to chat on connect |
| `DataDirectory` | `data` (`/data` in the image) | Accounts, sessions, parks, postcards and reports |
| `WebClientDirectory` | empty (`/app/web` in the image) | The published browser game; empty serves the API only |
| `FilterDirectory` | empty | The operator's own original language folder with `swears.txt` and `alloweds.txt` for the chat word filter |
| `AllowRegistration` | `true` | Whether new accounts can be made |
| `RequestsPerMinute`, `AuthenticationsPerMinute`, `UploadsPerHour` | 240, 10, 30 | Limits per client address |
| `MaximumParksPerPlayer`, `MaximumVotesPerDay`, `MaximumInboxPostcards`, `MaximumChatConnections` | 5, 10, 200, 1000 | Per-player and server limits |
| `BannedPlayers`, `MutedPlayers`, `HiddenParks` | empty | Moderation lists |

Behind a proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the limits see the real
client addresses (the compose example does).

Back up the data directory (the `opentpw-data` volume) to keep accounts and shared parks.

## How the browser game is served

- The game's files are served before the API's rate limiter: one page load fetches over a
  hundred files.
- The publish step stores a Brotli copy beside each file; browsers that accept Brotli get it.
- The runtime's fingerprinted files (`_framework/*.<hash>.*`) are cached for a year; the
  page, its scripts and `dotnet.js` are revalidated on every load.
- The page has a strict content security policy: scripts and connections only to its own
  address (WebAssembly compilation allowed), no inline scripts or styles, no framing.

## What the server keeps

- **Accounts:** name, password hash (PBKDF2-SHA256 with salt), creation time, buddy list and
  vote counters.
- **Sessions:** token hashes and expiry, in memory only.
- **Parks:** published packages and thumbnails with their author, visits and votes.
- **Postcards:** cards waiting in a recipient's inbox.
- **Reports:** moderation reports (`reports.jsonl`).

Client addresses are only used in memory for rate limiting; they are not stored or logged by
the server. Chat is not stored.

**Deleting an account:** the Online World screen offers *Delete account* while logged in; the
player confirms with their name and password (`DELETE /api/v1/accounts`). The server then
removes the account, its sessions and chat connection, its published parks with their files,
the postcards in its inbox, and its entries in other players' buddy lists and park visitor and
vote lists. Reports the player made name them as a deleted player; reports about them are
kept for moderation. Postcards they already sent stay with their recipients. The name becomes
free again.

**Moderation** is by hand for now: read `reports.jsonl`, and add names to `BannedPlayers` or
`MutedPlayers` (or park ids to `HiddenParks`) in the settings, then restart the server.

## Security

- Passwords are hashed with PBKDF2-SHA256; login returns a session token.
- The chat WebSocket takes the token from the `Authorization` header (desktop) or, because
  browsers cannot set WebSocket headers, from a first `{"type":"auth","token":"…"}` frame.
  The token never appears in a URL. A socket without a valid first frame within ten seconds
  is closed; such sockets count against `MaximumChatConnections` before they are accepted.
- Packages, postcards and chat frames are validated and size-limited; owners and postcard
  recipients are checked.
- The desktop client refuses automatic HTTP redirects so a redirect cannot forward a login
  to another address.
