# OpenTPW online extension

OpenTPW provides an opt-in, self-hostable service for shared parks, postcards and chat.
It does not connect to the original Theme Park World service and is not a reconstruction
of its network protocol. No service address is configured and no network request occurs
until the player selects a server and an action in the online screens.

The online screens support the server's Game News and System News, account registration/login, park export/publish/search/download,
voting, read-only visits, local postcard composition, sending the outbox, fetching the
inbox, and chat. Chat commands use the selected original `CHAT_COMMANDS.str` words;
original text is used where verified and supplementary labels cover the six supported
languages. Chat lines use the local `GAME8AA.bf4` font.

## Screens

Players open the online screens with **Go Online** (UITEXT 1), which OpenTPW adds to the
lobby menu and the pause menu. They are drawn in the original UI style (`OnlineScreens`):
original `ui.wad` window, list, button and field art, BF4 fonts, keyboard and mouse
navigation, and new text fields and scroll lists (`UiTextField`, `UiScrollList`). Their
contents follow the original online screens mapped from the Mac binary in
[reverse/UI-MAP.md](reverse/UI-MAP.md):

| Screen | Original counterpart (UI-MAP) | Original art |
| --- | --- | --- |
| Online World | Secondary menu, Go Online | `w_big`, `i_mail` (authored place) |
| News | Online news panel: Game News and System News columns (UITEXT 267/268); the original's "News from ThemeParkWorld.com" title is replaced by News | `w_big` |
| Online Login | Online login dialog: name and password, 16 characters each, Enter moves from name to password and submits | `w_med`, `b_login`, `f_text1` fields |
| Find Parks | Find Parks dialog and Park details (creator, visits, votes, visited/voted) | `list_findprks`, `b_vote` |
| Publish Park | Publish Park dialog: park name and description; OpenTPW adds the website opt-in (off by default) | `w_med` |
| Send Postcard | Send Postcard dialog | `w_med` |
| Unsent Postcards | Unsent Postcards (outbox) screen | `list_outbox`, `b_sendall` |
| Inbox | (OpenTPW) | `list_outbox` |
| Chat | Chat screen | `list_msgs` |
| Import file | (OpenTPW) | `w_med` |

The original files place several of these models on screen (`f_profile`, `b_login`,
`list_findprks`, `list_outbox`, `list_msgs`, `b_vote`, `i_mail`), but the original
composition of each screen and the place of code-positioned controls are not decoded:
the Mac code builds each dialog from a template it looks up at run time
(`0x10184b48` for the login dialog) and only then finds its controls by id. The windows
therefore use OpenTPW layouts ([EXT:ONLINE-UI]); the server address field and the
Inbox and Import screens have no original counterpart. Visits start directly from Find
Parks or Import file instead of needing `--visit-park` and a restart.

The ImGui Online panel remains as a developer tool in `--sandbox` and other developer
starts. The front-end smoke test walks the screens and captures
`native-smoke-<language>-online-*.png`.

## Local files and visits

The default folder is `<ApplicationData>/OpenTPW/online`. Set `OPENTPW_ONLINE_DIR`
or pass `--online-dir <directory>` to choose a different writable directory outside
the original installation. `online.json` remembers the chosen server and player name;
passwords and session tokens are not persisted. The `parks`, `visited`, and
`postcards/{inbox,outbox,sent}` folders hold locally exported/downloaded files.
Malformed local postcards are reported. A conflicting or damaged existing inbox file
is retained and prevents deletion of that incoming postcard from the server.

Export/import works without an account or a server. The Online panel inspects a local
`.tpwpark` and reports required content and layout warnings. Downloading a shared park
saves it locally and inspects it. To open it as a read-only visit, restart the game:

```sh
bash scripts/run.sh --game-path /path/to/installation --visit-park /path/to/shared.tpwpark
```

Offline command-line export/import also runs before graphics or the save folder is created:

```sh
bash scripts/run.sh --game-path /path/to/installation --export-park /tmp/jungle.tpwpark --load-original-level jungle
bash scripts/run.sh --game-path /path/to/installation --import-park /tmp/jungle.tpwpark --online-dir /tmp/online
```

`--export-park` captures the original startup import selected by `--load-original-level`;
without that option it exports an empty Jungle sandbox. It does not capture a running
process's edits. `--import-park` validates the package against local content and copies
it into the online folder's `visited` directory under its package id. Both commands exit
without launching the renderer or contacting a server. Their output paths cannot be
inside the original installation, including through existing symbolic links. Malformed
imports create no downloaded park. Choose one export/import/visit action per invocation.

`--visit-park <file> --smoke-test` performs a dedicated native read-only visit check:
no economy runtime, rejected object/prototype building and removal, rejected save/load
and export, followed by eight rendered frames and a GPU capture at
`artifacts/native-smoke-online-visit.png`. It uses no sandbox build/save smoke steps.

A visit cannot place/remove rides or save/load the sandbox. It creates no economy
runtime and therefore cannot charge visitors or advance a park's economy. Version 1
contains only the original level's imported path/object records and the prototype
Totem state. It has `read-only-visit` and `no-economy` flags. Visitors and the ride can
animate locally; those simulations are not synchronized multiplayer.

The original imported paths/footprints can be drawn; an edited shared path/object
layout cannot yet be applied. The panel and startup log explicitly report mismatches,
missing objects/bonus archives and terrain hash differences. Missing content is never
downloaded automatically. Original textures/models/audio and executable code are
never embedded in these sharing files. Economy, staff, research, live multiplayer,
original server behavior and original online panel visual parity remain unsupported.

## Local server

Run from the repository root using the existing .NET SDK:

```sh
dotnet run --project source/OpenTPW.Server -- --urls http://127.0.0.1:5000
```

Choose `http://127.0.0.1:5000` in the game panel and press Go online, then register or
log in. This command is for local testing; docs/SERVER.md describes hosting a server on
the internet over HTTPS (Docker image with Caddy on port 443), optionally serving the
browser build of the game from the same address. No deployment is performed by the client.
Operator settings are in `OpenTPW.Server/appsettings.json` and use `OpenTPW__<setting>`
environment variables. The server stores accounts, sessions, parks, postcards and reports in its
configured data directory. Its optional word filter uses the operator's local original
`swears.txt` and `alloweds.txt` files; they are not distributed.

The server hashes passwords with PBKDF2-SHA256, validates and bounds packages/chat
frames, checks owners and postcard recipients, and limits authentication/uploads/chat.
For remote use, provide HTTPS and configure the operator's storage and limits. The
client validates server URLs and disables automatic HTTP redirects so a redirect cannot
forward a login body to another service. HTTP remains available for local testing.
Going offline disconnects local clients; issued tokens retain their server expiry.

## Verification and extension labels

CPU and loopback tests cover malformed containers/manifests/payloads, image validation,
account authorization, ownership, postcard privacy, rate limits, chat commands and
rooms, the game's background session transitions, local folders and client workflows.
They require no external server or original assets. Original-data tests opt in through
`OPENTPW_GAME_PATH`; interactive Online panel behavior and BF4 chat rendering still
need a graphics-capable manual run.

All `EXT:ONLINE-*` tags describe OpenTPW choices, not recovered original behavior.
The package/protocol/server code owns tags 001–059. The game adapters own:

| Tag | OpenTPW choice |
| --- | --- |
| 060 | Opt-in settings and local folder layout |
| 061 | Edition label (installation edition detection unsupported) |
| 062 | Generated map thumbnail palette/layout |
| 063 | Supplementary translated labels |
| 064 | Chat line formatting |
| 065 | BF4 chat overlay layout, colours and line count |
| 066 | ImGui Online panel (developer starts only; players use the original-style screens) |
| 067 | Publish Park's website opt-in, off by default (the server's public website list is 057) |
| UI | Original-style online screens: window composition, places of code-positioned controls, text field art and caret, server address field, Inbox and Import screens, the News window title |

## Approximation register

The online service is an allowed OpenTPW extension. Its `EXT:ONLINE-*` choices
remain separate from these unresolved interpretations of original words, data
and behavior. An extension label does not resolve an `APPROX:ONLINE-*` uncertainty.
The six entries below preserve the source comments' evidence requirements; they
are not claims that the original online protocol or service has been recovered.

| ID | Assumption | Evidence needed |
| --- | --- | --- |
| ONLINE-001 | Word filtering uses case-insensitive substring matches in space-padded text; entry spaces act as boundaries; allowed substrings exempt matches; hit characters except spaces become asterisks. | Original word-filter code or observed original filtering behavior; the source comment says the encrypted TP.ICD implementation is unreadable. |
| ONLINE-002 | A leading slash introduces a chat command; other text means say. | Original chat input syntax, which is not documented in the available data. |
| ONLINE-003 | Response strings 102 and 110 mean failure to add an ignored player and a buddy respectively, based on nearby string blocks. | Original response-code table. |
| ONLINE-004 | Buddy response strings 111–113 mean online, offline and removed. | Localized variants or the original response-code table. |
| ONLINE-005 | Chat command semantics follow command words, response strings and weachatr.dll export names; say/emote/shout reach a room, wshout/ushout all rooms; hearing has no positional effect; blackmark creates a moderation report. | Original chat server behavior, including command reach, hearing range and blackmark handling. |
| ONLINE-006 | A visitor already inside a park must leave it before visiting another, inferred from CHAT_COMMANDS string 127. | Original park-visit transition behavior or an original chat/runtime trace. |
