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

3. Open `https://tpw.example.org/`, load your game data (archive.org, an address, a CD image or the game folder), then **Go online**: the server
   address is already filled in with the page's own address.

Desktop players use the same address (`https://tpw.example.org`) in the online login screen.

The files are `source/OpenTPW.Server/Dockerfile`, `docker-compose.example.yml` and
`deploy/Caddyfile`. Another TLS proxy (nginx, Traefik) works the same way: forward to port 8080 of
the container, pass WebSocket upgrades through, and keep the `Host` header.

`docker build --build-arg WEB_CLIENT=false …` builds an image with the API and chat only.

### Public build identity

`GET /api/v1/build-info` returns only the running server assembly's build facts:

```json
{"version":"1.0.0","sourceRevision":"c884e2b24394e4ced11a0ef880a66d2fa9bd00ee","revisionKnown":true}
```

The revision is the full 40-digit Git SHA from `AssemblyInformationalVersion`,
normalized to lowercase. Build metadata uses nonempty dot-separated identifiers;
malformed metadata yields an unknown build. Other build metadata is omitted. A build
without that revision returns `sourceRevision: null` and `revisionKnown: false`; missing or
unrecognized version metadata also returns `version: null`. The endpoint is public,
read-only, rate-limited and served with `Cache-Control: no-store`. It exposes no
configuration, credentials or filesystem paths. The existing `/api/v1/server`
version and protocol fields retain their existing meanings.

Container builds exclude `.git`, so supply the public source revision explicitly:

```sh
docker build --build-arg BUILD_REVISION="$(git rev-parse HEAD)" -f source/OpenTPW.Server/Dockerfile -t opentpw-server .
```

Both Server and Web publish stages receive `BUILD_REVISION`; the endpoint describes
only the server assembly. CI passes `github.sha`, matching the default checkout:
the tested merge commit for pull requests, or the branch/tag commit for pushes
and dispatches ([GitHub event SHA definitions](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)).
Omitting the argument produces an explicitly unknown revision. A source SHA identifies the claimed server source,
not an OCI image digest or proof of a clean source tree. Compare served browser
resources separately when checking web identity. This endpoint becomes available
only after deploying a build that includes it; it does not attest older releases.

## The official server (`deploy/`)

`play.opentpw.io` runs the published image on a small Hetzner Cloud server (x86, Ubuntu
24.04). Everything it needs is in `deploy/`:

- `compose.yml` and `Caddyfile`: the image from `ghcr.io/meneerkrabs/opentpw-server` behind
  Caddy on port 443; player data in `/opt/opentpw/data`; settings in `.env`
  (`env.example`). Caddy keeps no access log. Its admin API is off (`admin off`): both
  containers share the host network, so the game server could otherwise reconfigure Caddy
  through `localhost:2019`. To apply a changed Caddyfile, run `docker compose restart caddy`.
  Docker keeps at most 3 × 10 MB of each container's output.
- `opentpw-update.timer`: every night, pull the image and restart if it changed. `latest`
  follows releases; CI publishes it for every `v*` tag (and `main` for every push to main). Those
  images carry the AOT browser page; a pull request's check builds the image with
  `--build-arg WEB_AOT=false` (the interpreter page) to save the AOT compile's six minutes.
- `opentpw-deploy.path`: runs the same update as soon as a release asks for it (below).
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

### Automatic deployment

A pushed `v*` tag updates the official server minutes after its image is published, without
waiting for the night. A normal release started through **Actions → Release builds → Run
workflow** explicitly starts the same build on its published tag; tags created with
`GITHUB_TOKEN` do not trigger push workflows themselves. Preview releases started there do
not request deployment. To retry manually, use **Actions → Cross-platform build and CPU tests
→ Run workflow**, select `main`, and enter the published stable tag in **Published stable release
to deploy**. The workflow verifies that it is published and not a preview before starting a
build on that exact tag. Leaving the field empty on `main` builds the development image
without updating the official server.

The `deploy` job of `.github/workflows/build.yml` runs after the image job and
sends `POST /api/v1/admin/deploy` with the repository secret `OPENTPW_DEPLOY_TOKEN`. The server
answers 202 only for that token (compared in constant time, rate limited like logins; the route
does not exist without `DeployToken`) and does nothing but write `.deploy-requested` into its
data folder. On the host, `opentpw-deploy.path` sees the file and starts `opentpw-update.service`,
which removes it and pulls and restarts as every night. The container never gets access to
Docker, and SSH stays closed to GitHub.

The token lives only in the server's `.env` (`OPENTPW_DEPLOY_TOKEN`, generated by
`make-cloud-init.py`) and in the repository secret. To rotate it, generate a new one with
`openssl rand -hex 32`, put it in both places, and run `docker compose up -d` in `/opt/opentpw`.
Without the secret the job skips the request; the nightly update still happens.

### Deploys without interruption

The server is a single process, so an update stops it and starts the new one, which takes a
few seconds. Players should not notice:

- Caddy holds requests, chat connections included, for up to 30 s until its health check
  (`/api/v1/server`, every second) sees the new server answer, instead of returning errors.
  The server runs on the host network and listens on 127.0.0.1:8080 only: Docker's port proxy
  would accept connections while the server is still starting and then drop them, which Caddy
  does not retry. Measured on the official server: a forced restart with a request every 100 ms
  gave no errors, the slowest request taking 2.4 s.
- Sessions are kept in `sessions.json`, so nobody is logged out.
- On stopping, the server sends what is queued in chat and closes each chat connection with
  status 1012 ("service restart"); `compose.yml` gives it 20 s to stop. Clients that do not
  answer are dropped after 3 s.
- The game then reconnects the chat by itself (after 1, 2, 4, 8, 15 and 30 s), rejoins the room
  it was in and shows "The server restarted; chat reconnected." It does so too after a server that
  crashed or went away without closing: the desktop's socket then reports no status, and the
  browser's reports 1006 ("abnormal closure"). The page's chat did not reconnect by itself after
  such a drop until the live acceptance's page runs found it (docs/COOP.md, "The live acceptance").

Running two servers side by side (blue-green) would need a shared database instead of the
JSON files; at this scale the few seconds of held requests are the simpler choice.

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
| `WebsiteOrigins` | none | Web pages that may read the news and website park list from a browser |
| `DeployToken` | none | Secret for `POST /api/v1/admin/deploy` (automatic deployment of the official server) |
| `DataDirectory` | `data` (`/data` in the image) | Accounts, sessions, parks, postcards and reports |
| `WebClientDirectory` | empty (`/app/web` in the image) | The published browser game; empty serves the API only |
| `FilterDirectory` | empty | The operator's own original language folder with `swears.txt` and `alloweds.txt` for the chat word filter |
| `AllowRegistration` | `true` | Whether new accounts can be made |
| `RequireTotp` | `true` | New accounts must set up two-step login with an authenticator app (docs/ONLINE.md, "Two-step login"); older accounts are not affected |
| `RequireEmail` | `false` | New accounts must confirm an e-mail address, used only for recovery (docs/ONLINE.md, "Account recovery"); needs `RequireTotp`, `ResendApiKey` and `EmailFrom` |
| `ResendApiKey` | none | Secret API key of the Resend mail service; empty turns account e-mail off. Set it in `.env` only (`OPENTPW_RESEND_API_KEY`) |
| `EmailFrom`, `EmailReplyTo` | none | Sender on a domain verified at Resend (the official server: `OpenTPW <noreply@email.opentpw.io>`) and the reply address named in password notices (`contact@opentpw.io`) |
| `RequestsPerMinute`, `AuthenticationsPerMinute`, `UploadsPerHour` | 240, 10, 30 | Limits per client address |
| `MaximumParksPerPlayer`, `MaximumVotesPerDay`, `MaximumInboxPostcards`, `MaximumChatConnections` | 10, 10, 200, 1000 | Per-player and server limits; ten parks on show at once is the original's limit (its FAQ) |
| `ChatBurst`, `ChatMessagesPerSecond` | 8, 1 | Chat message bucket per connection |
| `MovementBurst`, `MovementsPerSecond` | 30, 10 | Walking-position bucket per connection (chat parks; positions sent in the lobby are ignored) |
| `BannedPlayers`, `MutedPlayers`, `HiddenParks` | empty | Moderation lists |
| `MaximumCoopConnections`, `MaximumCoopSessions` | 1000, 250 | Shared parks: co-op sockets (counted before they authenticate) and live sessions |
| `CoopHostMessagesPerSecond`, `CoopHostBytesPerSecond` | 400, 4 MiB | What a host's co-op socket sends, with a two-second burst; an account keeps its buckets over reconnects |
| `CoopGuestMessagesPerSecond`, `CoopGuestBytesPerSecond` | 50, 256 KiB | What a guest's co-op socket sends, with a two-second burst |
| `CoopOutboxBytes`, `CoopServerOutboxBytes` | 2 MiB, 256 MiB | Bytes queued for one co-op socket and for all of them: a socket over its share is cut off, and a server over its budget cuts off the socket with the longest queue. A socket that takes no message for 30 seconds is cut off too |
| `CoopCreatesPerHour`, `CoopJoinsPerHour`, `CoopWrongCodesPerHour`, `CoopInvitesPerHour`, `CoopKicksPerHour`, `CoopDownloadsPerHour` | 6, 30, 10, 60, 60, 30 | Per account: sessions started, joins, codes that matched no session, invitations, removals and leaves, snapshot downloads |
| `CoopSnapshotsPerSessionPerHour` | 60 | Per shared park: the join snapshots its host uploads, counted once the request is known to be the host's with room for it (a limit of its own, not `UploadsPerHour`) |
| `MaximumCoopSnapshotBytes`, `MaximumCoopStoreBytes` | 96 MiB, 4 GiB | A shared park's join snapshot, and all of them together |
| `CoopPingInterval` | 15 s | A co-op socket that has heard nothing from the server for this long gets a ping (browsers never see WebSocket pings) |

The address limits (requests, logins and park uploads) and the shared parks' snapshot and download
limits answer a 429 with a `Retry-After` header: the time until the oldest counted request leaves its
hour, or for an address limit its window's length (an upper bound). A joiner's download waits that
long, up to a minute.

Behind a proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the limits see the real
client addresses (the compose example does).

Back up the data directory (the `opentpw-data` volume) to keep accounts and shared parks.

## News

The game's Online World has a **News** screen with the original two columns, Game News and
System News. The server reads them from plain-text files the operator writes in the data
directory: `news/game.txt` and `news/system.txt` (UTF-8; at most 4,000 characters each are
served; a missing file is empty news). They are served publicly at `GET /api/v1/news`, also
before logging in, and re-read only when a file changes. On the official server the files are
in `/opt/opentpw/data/news/`, which the nightly backup already covers.

## Cities

The online world's globe shows the server's cities (`GET /api/v1/cities`, public): id,
name (at most 16 characters), description (128), latitude, longitude and the number of
parks published there. Parks are published into a city (`POST /api/v1/parks?city=<id>`).
The default list holds "North Pole" (the Bullfrog pin, for approved parks), the four
cities known from the original (Hong Kong, Ulaanbaatar, Timbuktu, San Francisco) and some
others; replace it with `cities.json` in the data directory, an array of
`{"id", "name", "description", "latitude", "longitude"}`. It is re-read when the file
changes; invalid entries are left out.

## Parks, packages and visits

Parks are published as `.tpwpark` packages (`POST /api/v1/parks`, `MaximumParksPerPlayer`
at once — publishing more is refused until one is unpublished with `DELETE
/api/v1/parks/{id}`; the original allowed ten parks on show and treated publishing and
removing as separate actions). The server validates every upload as a package: the
container and manifest, the payload's size (`ParkPackage.MaximumPackageBytes`) and
digest, and the payload itself, including its version. **Package format 2** (payload
version 2) carries the complete built park (docs/ONLINE.md); **format 1** packages from
OpenTPW 0.7 are still accepted, stored and served unchanged, so old clients and old
parks keep working. **Package format 3** is a version 2 package whose manifest also lists the content extensions the park uses
(`extensions`: id and version, docs/ONLINE.md, "Official extensions in a package"): the
server validates the member with the rest of the manifest (ids that start with `opentpw.`, a plain version, no
duplicate, at most 32, and only in version 3) and stores the package byte for byte, so it serves the member to
visitors as it came. A park that uses no content extension is still published as version 2. The protocol version
does not change. A server from before this change cannot read a version 3 package: its strict manifest reader
refuses the unknown `extensions` member as malformed (a 400), and the client then says the server must be updated,
so servers are updated before clients publish such parks. Older clients that download a version 3 park report a
malformed manifest.
(`PublishedUtc`), visitor count and vote count — the fields the original's "My Theme
Park" page showed per park.

`POST /api/v1/parks/{id}/visits` is the **visit report**: the client sends exactly one
per visit, with the download it enters the park from, as the original client sent its
MarkParkVisited message once per park entry. Each accepted report counts one visit, within the limits below;
the player pages count one Platinum Ticket per counted visit (every visit earned the park's
owner tickets in the original, voted or not). An owner's visits to their own park do not count,
and one visitor counts at most once per UTC day per park, an OpenTPW guard against farming
tickets (`EXT:online-visit-count-guard`; the original's guard is not recovered). The
distinct-visitor list stays separate for the "visited before" flag, and voting
(`POST /api/v1/parks/{id}/votes`) is its own action with its own limits.

## Chat parks

The globe's five chat satellites are the server's chat parks: standing chat rooms with live
presence — everyone in one sees the others and their positions, which the server relays with a
separate rate limit. `GET /api/v1/chat-parks` (public) lists them with their player counts.
`GET /api/v1/chat-parks/scenes` (with a login, as a park summary needs one) lists what each
satellite's park details show and the park its visit enters: its name, description and level, and
the published park's summary while the satellite names one that is still published. A game visits
that park, or else builds the starting park of the satellite's level from its own data; either way
the visit walks in the satellite's room, which its count counts (docs/ONLINE.md, "Chat parks (the
satellites)"). A server without the scenes route leaves games to show the room from above.

The operator configures them with `chatparks.json` in the data directory, an array of
`{"id", "name", "level", "parkId", "description"}`: ids 1-5, names at most 32 characters (a longer
name is cut), `level` one of the original levels a game can build (`jungle`, `hallow`, `fantasy`,
`space`; left out or any other, the satellite's default level), `parkId` a published park's id, and
a `description` of at most 512 characters (left out, the park's own). The file is re-read when it
changes; an entry without a valid id or name is left out, as is a malformed park id or description
of an entry, and a file without any valid entry gives the defaults. A field of the wrong JSON type (such as a number for `level`) makes the whole file unreadable, so all five satellites get the defaults until it is fixed. A satellite's visit of a
published park walks in the satellite's room, so it has no vote and is not counted at the park
itself. The defaults put the five satellites in the four themes:

| Satellite | Default name | Level |
| --- | --- | --- |
| 1 | The Meeting Place | `jungle` |
| 2 | The Fairground | `fantasy` |
| 3 | Coaster Corner | `hallow` |
| 4 | The Lost Kingdom | `jungle` |
| 5 | The Space Port | `space` |

Parks published by players keep their own chat rooms (`park:<id>`) with the same behaviour; a
visit of a published park joins its room and shows the other visitors walking (docs/ONLINE.md,
"Players in a visited park").

`[EXT:online-coop-session]` A shared park's session has a room too, `coop:<id>` with its session id
(docs/ONLINE.md, "Shared parks"). The chat asks the co-op hub whether a player is a member
(`ICoopMembership`) as a join or a goto takes them in, while it holds its rooms; the hub answers from a
table it keeps beside its own, so the chat never waits on the hub's locks or its files. When an account
stops being a member (it left or was kicked, or the session ended), the hub tells the chat at once,
under its own locks and before it announces the change, and the chat takes the account's connections
in that room back to the lobby. The rooms come after the hub's locks, never before them, and the move
waits for the rooms, so it comes after any entry that was let in before. No one who is not a member
stays in the room, and by the time the host hears that a guest left or was removed, that guest's chat
is in the lobby. What a member says or does in the room (lines, emotes, shouts, moods, the away notice,
movement) is routed while the chat holds its rooms, and only from a member still in the room, so nothing
said there reaches the room an evicted member was taken to. `/locate` answers that the player is in a
shared park without naming it; a world shout or a page from the room carries no room; and no buddy is
told a player entered one. What is said to a room (a line, an emote, a shout, a mood, the away notice)
may name a shared park's room it is for, and no other command names a room: the server routes it only
while the connection is in that room, and otherwise drops it and tells the sender the shared park's chat
is not open (`shared-park-chat-closed`). A tell, a page or a world shout from the room goes where it is
addressed, as from any room, without naming it. Servers with these rooms report protocol
version 8, and the game asks for a shared park's room, or names it in a command, only on such a server.

Movement is relayed only inside a park's room (the lobby's are dropped) and only to connections
that have moved, so a client without a walking view never receives it. A player's first movement
in each room is answered with where everyone else in the room stands; entering a room forgets the
player's place there. Protocol version 4 (`GET /api/v1/server`) marks a server that answers each
room's first movement this way; visits draw figures only on such a server.

Protocol version 5 adds a movement's mode, the original's `BOSS_CHAT_MOVE` modes: `"mode"` is
`"walk"`, `"teleport"`, `"hide"` (the player rides a ride) or `"unhide"` (the ride ended; the figure
appears at the place). A game sends it only to a server of version 5 or later, and only after it
has read that version for the open connection: a hide is held back until the read finishes, so a
reconnect cannot show the rider as a plain place, and a failed read is tried again with a growing
wait for as long as the connection stays open. A room change or refused join discards coalesced
movement still waiting for that room; already queued sends retain their order before the join.
The server sends a mode only to connections that
sent one themselves, so neither older games nor older servers see a member they would refuse. A
hidden player's movements reach no connection that reads no mode, the answer to a first movement
leaves hidden players out, and the player's next movement of any other mode shows them again.

Publishing a park masks swear words in its **name** with the server's word filter — the original
showed them as asterisks — while a filtered **description** is refused outright. The publish limit
answer uses the original's wording (the limit itself is `MaximumParksPerPlayer`).

## The original website's pages

The original service's website showed a per-player park page, the top-10 parks and players, the
richest players (Platinum Tickets) and a player search. The server serves the same information as
public, privacy-respecting endpoints: without a login they include **only parks whose authors
chose the website list**, so nobody appears who did not opt in. Platinum Tickets are one per
recorded visit of a park (the original awarded them per visit, voting or not).

| Endpoint | The original page |
| --- | --- |
| `GET /api/v1/players/{name}` | "My Theme Park": the player's parks with visitors, votes, publish date, current rank and highest rank, and their Platinum Tickets |
| `GET /api/v1/charts/parks` | Top 10 parks: rank, park name, player |
| `GET /api/v1/charts/players` | Top 10 players by votes |
| `GET /api/v1/charts/richest` | Richest players by Platinum Tickets |
| `GET /api/v1/players?search=` | Player search by name; at most 50 results, `more` when the search was too broad. Without a login only players who show a park on the website are found; logged in, every player |

All are GET-only and share the website CORS policy (`WebsiteOrigins`). The game uses the player
page for Find Parks' Platinum Tickets display; a logged-in player page lists every published park
of that player, like the park list. Player names are searched by substring; OpenTPW accounts have
one name, so the original's first-name/last-name search has no equivalent.

## The website's top parks

When publishing, a player can choose **Also on the OpenTPW website** (off by default). Only
those parks appear in the public list `GET /api/v1/parks/website`: at most ten, most votes first
(then visits, then the earlier published), with name, description, author, theme, votes, visits
and publish date, recomputed at most once a minute (a park removed within that minute can
still be listed briefly, without its picture). Their pictures are public at
`/api/v1/parks/website/{id}/thumbnail`; other parks' pictures and the full park list still need a
login. `WebsiteOrigins` lists the web pages allowed to read the news and this list from a
browser (CORS, GET only); the official server allows `https://opentpw.io` and
`https://www.opentpw.io`, and a server without the setting allows none.

## How the browser game is served

- The game's files are served before the API's rate limiter: one page load fetches over a
  hundred files.
- The publish step stores a Brotli and a gzip copy beside each compressible file (the pools sprite
  `.TPC` files included); the server sends Brotli to a browser that accepts it, else gzip (a proxy such
  as Caddy leaves a response that already has a `Content-Encoding` alone).
- The runtime's fingerprinted files (`_framework/*.<hash>.*`) and the pools sprite files asked for by
  their content hash (`pools-sprites/...?v=<first 16 hex digits of the file's SHA-256>`, checked by the server against the uncompressed file; any other `?v=` is revalidated) are cached for a year; the page, its
  scripts and `dotnet.js` are revalidated on every load (ETag, answered 304).
  Measurements: [BROWSER-PERFORMANCE.md](BROWSER-PERFORMANCE.md#transfer-and-memory-october-19-2026).
- The page has a strict content security policy: scripts and connections only to its own
  address (WebAssembly compilation allowed), no inline scripts or styles, no framing. Only `connect-src`
  also allows `https:`, so the player can load a CD image from an address they choose
  ([WEB.md](WEB.md#game-data-sources)); scripts, styles, images and frames stay restricted to the page itself.

## Account e-mail

With `ResendApiKey` and `EmailFrom` set the server sends account e-mails through Resend: a code to
confirm a new account's address, a recovery code, and a notice when a password or two-step login
was reset. With `RequireEmail` every new account needs a confirmed address; without it the e-mail
routes stay available but registration does not ask for one. The official server sets, in
`/opt/opentpw/.env` (never in the repository):

```sh
OPENTPW_RESEND_API_KEY=re_…            # from the Resend dashboard; rotate it if it was ever shared
OPENTPW_EMAIL_FROM=OpenTPW <noreply@email.opentpw.io>
OPENTPW_EMAIL_REPLY_TO=contact@opentpw.io
OPENTPW_REQUIRE_EMAIL=true
```

then `docker compose up -d`. The sending domain (`email.opentpw.io`) must be verified at Resend.
The key is read from the settings only; it is never logged or returned, and a failed send names
only the HTTP status. Each address belongs to one account. At most three e-mails an hour go to one
player name or one address, and the recovery route answers the same, at the same speed, whether a
name or address exists (its e-mail is sent in the background). Accounts made before e-mail have no address and keep the operator reset
(`totp-reset/`) as their only recovery.

## What the server keeps

- **Accounts:** name, password hash (PBKDF2-SHA256 with salt), creation time, buddy list and
  vote counters, the confirmed e-mail address (accounts made with e-mail; used only for recovery),
  and for two-step login the authenticator secret. Codes sent by e-mail are kept in memory only,
  as hashes, for at most fifteen minutes. The secret has to be readable
  to check codes, so it is stored as is in `accounts.json`; `totp-steps.json` holds the last code
  step each account used, so a code stays used after a restart. The server makes its data
  directory and the files it writes readable by itself only (0700 and 0600) and logs a warning
  when it cannot; the backups are readable by root only. A registration waiting for its first
  code is kept in memory only.
- **Do not roll back** to a server version without two-step login: it ignores the secrets, lets
  those accounts log in with their password alone and drops the secrets the next time it saves
  `accounts.json`.
- **Sessions:** token hashes (SHA-256, never the token) and expiry in `sessions.json`, so a restart
  does not log players out; expired sessions are dropped.
- **Parks:** published packages and thumbnails with their author, visits and votes.
- **Postcards:** cards waiting in a recipient's inbox.
- **Reports:** moderation reports (`reports.jsonl`).
- **Shared parks** (`coop/`): the live sessions in `coop/sessions.json` (code, title, host, members,
  the host's build and mods, the last frame relayed), so a restart does not end them, and their join
  snapshots in `coop/blobs/` (deleted with the session; the nightly backup leaves them out). The
  frames and messages the server relays are not stored.

Client addresses are only used in memory for rate limiting; they are not stored or logged by
the server. Chat is not stored.

**Deleting an account:** the Online World screen offers *Delete account* while logged in; the
player confirms with their name and password (`DELETE /api/v1/accounts`). The server then
removes the account, its sessions and chat connection, its co-op connection and its place in a shared
park (a shared park it hosts ends), its published parks with their files,
the postcards in its inbox, and its entries in other players' buddy lists and park visitor and
vote lists. Reports the player made name them as a deleted player; reports about them are
kept for moderation. Postcards they already sent stay with their recipients. The name becomes
free again.

**Moderation** is by hand for now: read `reports.jsonl`, and add names to `BannedPlayers` or
`MutedPlayers` (or park ids to `HiddenParks`) in the settings, then restart the server.

**Resetting two-step login** (a player lost their phone): put a file holding the player's name in
the `totp-reset` folder of the data directory, one file per request, for example
`echo "Player Name" > /opt/opentpw/data/totp-reset/player-name.txt`. No restart is needed. At the
player's next login with the right password the server removes the secret and then the file,
and the game shows the setup screen with a new secret. A request counts only if the file was
written after the account was made and after its last reset, and is not the request applied last
time, so a leftover file (one the server could not delete, or one for a deleted account of the same
name) is never used again. The setup
can only finish the same account: if the account is deleted, a waiting setup is dropped. Check who is asking before you do this: whoever knows the password can then set up the
account's two-step login.

## Security

- Passwords are hashed with PBKDF2-SHA256; login returns a session token. New accounts also need a
  code from an authenticator app (RFC 6238) at every login; codes work once, and five wrong codes
  block codes for an account for fifteen minutes.
- The chat WebSocket takes the token from the `Authorization` header (desktop) or, because
  browsers cannot set WebSocket headers, from a first `{"type":"auth","token":"…"}` frame.
  The token never appears in a URL. A socket without a valid first frame within ten seconds
  is closed; such sockets count against `MaximumChatConnections` before they are accepted.
- Packages, postcards and chat frames are validated and size-limited; owners and postcard
  recipients are checked.
- The desktop client refuses automatic HTTP redirects so a redirect cannot forward a login
  to another address.
