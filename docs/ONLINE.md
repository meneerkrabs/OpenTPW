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
| Online World | The online globe (Mac UI table `data:0x523f0`, globe code in the Windows v2.0 `tp.exe`): see below | `worldlob`, `f_wlobbg`, `b_findprk`, `b_bck2lob`, `b_zoom`, `b_lobup/left/right/down`, `i_sat`; `lobby.wad/globe` |
| OpenTPW online (menu) | (OpenTPW) login, news, postcards, chat, import, account, behind a button on the globe | `w_big`, `i_mail` (authored place) |
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

### The online globe

**Go Online** opens the online world as the original draws it: the globe from
`lobby.wad/globe` fills the screen behind the UI (the lobby islands are hidden while it
is open), with the original `worldlob` panel at the bottom left and five chat satellites
around the edges. What is traced:

- Layout (Mac table `data:0x523f0`): panel `worldlob` (9,620)-(510,1531) with Find Parks,
  back to your own lobby, zoom and the four rotate arrows, help texts 424–431 and 435;
  five `i_sat` satellites (help 423) with a small label area each.
- Camera (`tp.exe` 0x005DC860/0x005DCFE0): the globe stands still and the camera circles
  it at GLOBERADIUSOUT (475) or, after the zoom button, GLOBERADIUSIN (200); field of view 60, pitch limited to ±1.5 rad; arrow keys, the
  panel arrows, the cursor at a screen edge and the right button held turn it, and the
  turn dies away (zoomed out: step 0.15, damping 0.5; in: 0.075, 0.25). It does not spin
  by itself, and the plane and `Lpin` models in the folder are not used by the game.
- Cities: the server's list (`GET /api/v1/cities`, docs/SERVER.md). Each city gets a pin
  (`Hpin`; `Bullfrog` for the city named "North Pole") standing on the globe, taller the
  more parks it holds (0x005DC3E0); the cursor ray picks the city within 10 units of where
  it meets a sphere of radius 116, and its name and "Parks: n" (UITEXT 461) show at the top.
- The 24 globe textures form an equirectangular world map, so a city at latitude φ and
  longitude L stands at atan2(x, z) = 30° − L, sin φ up the model's +Y axis.

A click on a city's pin selects it and opens its lobby (below; `tp.exe` 0x005DD530 sets the
selected city that the lobby's top-10 request reads, 0x005E23F0). The globe also shows over a
park (Go Online from the pause menu): it replaces the park and the HUD, and closing it restores the
park's camera.

**Publish Park** starts on the globe as the original's PublishGlobe does: "Choose the city to
publish in" (UITEXT 419) at the top, the panel's back, zoom and rotate buttons, and a click on a
city's pin opens the name and description form for that city (Mac: the hovered city is stored and
the Publish Park dialog opens, 0x1018CE80). A server without cities skips the globe. Not yet
built: the satellites' player counts.

### City lobby

A city's lobby (Mac table `data:0x50172`) shows its ten most-voted parks as islands, in
the same lobby as your own islands: rank *i* stands at ISLANDCAMERAPOSITION slot *i* as
the island of the park's theme (`<theme>.txt` ISLAND), the way the original builds lobby
islands from a theme entry at a slot counter (`tp.exe` 0x005E3210). The `citylob` panel
holds other parks in this city (Find Parks filtered by the city, by votes), back to your
own lobby, the online world, enter this park (download and visit) and previous/next
island; the selected park's rank (`"%d"`, font slot 0) shows at the top and its name
(font slot 5, yellow) at the bottom (the item detail overlay, labels 0x49D9/0x49DA,
`0x101834A0`). The top-10 request's completion builds one island per park with a running
slot counter (0x005E23C0 → 0x005E27B0 → 0x005E3210); that each park uses its theme's
island is inferred from the own lobby, which builds its four islands the same way. The list comes from `GET /api/v1/parks?city=`,
which needs a login, as the original's city server did. OpenTPW's own functions (login, news, postcards,
chat, import, account) are behind **OpenTPW online** at the top right.

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
forward a login body to another service. Plain `http://` is accepted only for a server on the same
computer or the local network (`localhost` names and loopback, private, link-local and
unique-local addresses written as numbers; a name such as `nas.local` could resolve to any
address); any other address needs `https://`, so a password never crosses the internet
unencrypted.
Going offline disconnects local clients; issued tokens retain their server expiry.

## Two-step login

[EXT:online-two-step] The original online service had no second step; this is an OpenTPW
addition. A server with `RequireTotp` (the default) asks every new account to set up an
authenticator app (Google Authenticator, Microsoft Authenticator, Aegis, 1Password and others;
RFC 6238: HMAC-SHA1, 30-second steps, six digits).

1. *Register* sends name and password. The server answers `202` with a new secret (base32) and
   its `otpauth://` URI and keeps the registration **in memory only**, for ten minutes; nothing is
   stored and the name is not taken yet.
2. The game shows the *Two-step login* screen: a QR code of the URI (black on white, with a
   quiet zone, so phones can scan it on the dark dialog) and the key as text in groups of four.
3. The player enters the first code. `POST /api/v1/accounts/confirm` sends it with a one-time
   ticket from step 1, creates the account with the secret and logs in. Without the ticket a
   code is refused, so seeing someone's code is not enough, and only the registering player's own
   attempts count against the limit below. A newer registration of the same name replaces a
   waiting one.

Logging in then needs the password **and** a code: the login dialog has a third field. Without a
code the server answers `401` with the reason `totp-required`, and the dialog moves to the code
field and keeps the password. A code works once (each 30-second step at most once per account), the
server accepts one step of clock difference either way, and five wrong codes block codes for
that account (or that setup) for fifteen minutes. Accounts made before two-step login keep logging in with their
password alone. The secret is never stored on the player's machine.

There are no recovery codes. A player who lost their phone resets two-step login by e-mail (below)
or, for an account without an address, asks the server's operator, who resets the account
(docs/SERVER.md). The next login with the right password then shows the setup screen
again with a new secret.

Servers with two-step login report protocol version 2. Games from before it can still log in to
accounts without two-step login, but cannot register on such a server or log in to an account
that has it: they need an OpenTPW version with this screen. A registration that is waiting for
its code keeps its name: registering it again works only with the same password.

Servers with account e-mail (below) report protocol version 3. On a server that requires an
address, games from before it cannot register either; logging in to existing accounts is unchanged.

## Account recovery

[EXT:online-email] On a server with `RequireEmail`, the *Two-step login* screen of a new account
also asks for an e-mail address. *Send code* mails an eight-digit code to it; the account is created
only when *Confirm* gets both that code and the first authenticator code. One address belongs to one
account, and it is never shown to other players.

*Forgot password?* in the login dialog opens *Account recovery*: enter the player name or the
e-mail address and press *Send code*. The server answers the same whether or not the account exists;
if it has an address, a code arrives there (valid fifteen minutes, five tries, at most three e-mails an
hour). The code and a new password set the password; with *Two-step login: set up again* the next
login also shows the setup screen with a new secret, for a lost phone. Either way every session of
the account ends and a notice goes to the address. Mails are in the game's language. Accounts made
before e-mail have no address and still recover only through the operator. A server without account e-mail
answers *Send code* with an error that tells the player to ask its operator.

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
| ONLINE-007 | The globe camera's rates, steps and damping are applied per tick at 30 ticks per second. | The tick length of the online world's update (`tp.exe` 0x005DC860). |
| ONLINE-008 | The globe first faces Europe and the Atlantic from 30° north. | The original's starting yaw and pitch. |
| ONLINE-009 | Edge and right-button steering add rate × offset × step per tick to the turn velocity. | The input branches of 0x005DC860. |
| ONLINE-010 | The hovered city's name and park count are drawn at the top centre. | The layout of the original city labels 0x16c11/0x16c12. |
| ONLINE-011 | `f_wlobbg` is the panel's button background in its second rectangle (33,1057,480,1505). | The table field that names the panel's background model. |
| ONLINE-012 | Edge steering starts once the cursor has moved on the globe screen. | Whether the original steers from the first frame. |
| ONLINE-013 | Pins are twice the decoded (s, s, p) scale and stand on the mesh surface (radius 119.7). | The scale of the original flag node's parent and a capture of the original globe. |
| ONLINE-015 | `f_lobbutbg` is the city panel's button background in its second rectangle (67,1273,438,1484). | The table field that names it. |
| ONLINE-017 | The publish globe reuses the Online World's panel (back, zoom, rotate) without Find Parks and the satellites, with UITEXT 419 at the top. | The publish globe's layout table. |
| ONLINE-016 | Each city-lobby island is the island of its park's theme. | The argument mapping of `tp.exe` 0x005E27B0 (what the top-10 list entry passes to 0x005E3210). |
| ONLINE-018 | Each satellite's player count is drawn at its bottom-right corner. | The original wrote the count into the satellite control itself (STP-PPC 0x1b96d8), whose text placement is not decoded. |
| ONLINE-020 | The park details' eighth label row shows the players currently in the park's chat room (UITEXT 273). | The original's last row showed the Instant action mode flag (UITEXT 282), which the package format does not record. |
| ONLINE-022 | The postcard photo attached by Send Postcard is the park's generated map thumbnail, and no park reference is attached. | The original attached a camcorder snapshot of the visited park (manual pp. 51/67); its content and encoding are not decoded. |
