# OpenTPW online extension

OpenTPW provides an opt-in, self-hostable service for shared parks, postcards and chat.
It does not connect to the original Theme Park World service and is not a reconstruction
of its network protocol. No service address is configured and no network request occurs
until the player selects a server and an action in the online screens.

The online screens support the server's Game News and System News, account registration/login, park export/publish/search/download,
voting, read-only visits, local postcard composition, sending the outbox, fetching the
inbox, and chat — including the globe's five **chat parks**, standing rooms with live
presence: everyone in a chat park sees who is there and where they stand, and the server
relays each player's movement. Chat commands use the selected original `CHAT_COMMANDS.str`
words; original text is used where verified and supplementary labels cover the six supported
languages. Chat lines use the local `GAME8AA.bf4` font. What the original service had and
where each piece stands is inventoried in [reverse/ONLINE-FEATURES.md](reverse/ONLINE-FEATURES.md).

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
| News | Online news panel, titled "News from ThemeParkWorld.com" (UITEXT 266) with Game News and System News tabs | `w_big` |
| Online Login | Online login dialog in its original composition (Mac table `data:0x505f4`): name and password, 16 characters each, labels inside the fields | `w_med`, `b_login`, `f_text1` fields |
| Find Parks | Find Parks in its original composition (Mac table `data:0x50f84`): six columns Name/Theme/City/Position/Visits/Votes, search by player name | `list_findprks`, `b_vote` |
| Park details | The original details dialog (Mac table `data:0x51150`): eight label rows, description, enter-park and the owner's un-publish | `w_big` |
| Other Parks | The city's outside-the-top-10 chart (Mac table `data:0x5137e`, "Other Parks In …", UITEXT 446) | `w_big`, `list_findprks` |
| Publish Park | Publish Park form (Mac table `data:0x514de`); OpenTPW adds the website opt-in (off by default) | `w_med` |
| Send Postcard | tp.exe's postcard window (table `0x7540d8`, `0x4a8c00`/`0x4a7d70`): photo frame, message, title and recipients fields with their prompts (shown while a field is empty) and lengths (the title at most the card format's 64 characters), clear, tick (unedited title and message take the defaults UITEXT 215/216; no recipient shows 213) and X; the preview and address book are disabled (UI-042); recipients are player names split at `;`, `,` and newlines, not spaces (`[EXT:postcard-player-names]`) | `window4`, `!frame`, `b_clear`, `b_okay`, `b_exit` |
| Unsent Postcards | Unsent Postcards (outbox) screen in its original composition (Mac table `data:0x508bc`): Title/Date/Send/Delete columns and the mark buttons | `list_outbox`, `b_sendall` |
| Inbox | (OpenTPW) | `list_outbox` |
| Chat | The original chat overlay (Mac table `data:0x50b2c`): people-in-park list with tabs, action bar, message list, text entry — and the chat park view | `list_msgs`, `f_text1` |
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
  it at GLOBERADIUSOUT (475) or, after the zoom button, GLOBERADIUSIN (200); field of view 60, pitch limited to ±1.5 rad
  (the limit stops the pitch turn); arrow keys, the panel arrows and, by the Scroll option, the
  cursor at a screen edge (Pushscroll) or the right button held (the other mode) turn it, and the
  turn dies away (zoomed out: step 0.15, damping 0.5; in: 0.075, 0.25). Each time a globe
  screen opens, also back from a city lobby, it faces yaw 0 and pitch 0 (60° W on the equator),
  zoomed out and at rest (0x005DCFE0). It does not spin
  by itself, and the plane and `Lpin` models in the folder are not used by the game.
- Cities: the server's list (`GET /api/v1/cities`, docs/SERVER.md). Each city gets a pin
  (`Hpin`; `Bullfrog` for the city named "North Pole") standing on the globe, taller the
  more parks it holds (0x005DC3E0); the cursor ray picks the city within 10 units of where
  it meets a sphere of radius 116, and its name and "Parks: n" (UITEXT 461) show at the top right
  (tp.exe 0x4B9980, labels 93201/93202 of table 0x7581A0), empty while no city is hovered.
- The 24 globe textures form an equirectangular world map, so a city at latitude φ and
  longitude L stands at atan2(x, z) = 30° − L, sin φ up the model's +Y axis.

A click on a city's pin selects it and opens its lobby (below; `tp.exe` 0x005DD530 sets the
selected city that the lobby's top-10 request reads, 0x005E23F0). The globe also shows over a
park (Go Online from the pause menu): it replaces the park and the HUD, and closing it restores the
park's camera.

**Publish Park** starts on the globe as the original's PublishGlobe does: "Choose the city to
publish in" (UITEXT 419) in the city label while no city is hovered, the panel's zoom and rotate
buttons without Find Parks, back and the satellites (tp.exe 0x5DD910 → 0x4C4FB0(1)), and a click on a
city's pin opens the name and description form for that city (Mac: the hovered city is stored and
the Publish Park dialog opens, 0x1018CE80). A server without cities skips the globe.

### Chat parks (the satellites)

The five satellites are chat parks: standing chat rooms the server names (docs/SERVER.md), each
showing how many players are in it — the original wrote each room's player count into its
satellite control (STP-PPC 0x1b8808), and the manual says "The number displayed next to each
satellite is the number of people currently in this chat park". A click enters the park. In a chat
park the players are present together and **move around**: the chat screen shows the park from
above with a marker per player, a click walks the player's marker there, and the server relays
each position to everyone in the park. The original's three-dimensional chat-park scene is not
decoded, so the overhead view is an approximation ([APPROX:ONLINE-019](#approximation-register));
the movement itself — one position per walk step, relayed to the room, rate-limited separately
from chat — follows the manual ("Left click to walk to destination"). Any published park's chat
room works the same way: joining a park's chat from its details dialog or the city lobby.

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
slot counter (0x005E23C0 → 0x005E27B0 → 0x005E3210): each park loads its theme's island
file and stands at its list position, the counter replacing the ISLAND line's slot. The list comes from `GET /api/v1/parks?city=`,
which needs a login, as the original's city server did. OpenTPW's own functions (login, news, postcards,
chat, import, account) are behind **OpenTPW online** at the top centre.

### The chat overlay

Chat is the original overlay (Mac table `data:0x50b2c`): the people-in-park list at the left with
its two tabs — the players in this park and the online buddies — the manual's player buttons under
it (buddy, ignore, blackmark, on a selected name), the action bar across the top (vote, earmuffs,
postcards, goto, page, locate), the message list and the text entry at the bottom, all at their
authored places. The original assigned them otherwise
([APPROX:ONLINE-021](#approximation-register)): its list buttons were earmuffs, vote and an
unidentified handler, and the six-button table was a popup on a name in the list (blackmark, goto,
ignore, buddy, locate, page); OpenTPW's assignment covers every action the manual names. The commands themselves are typed or clicked: `/say`, `/tell`, `/shout`, `/emote`
("Dan is bouncing up and down"), `/earmuffs`, `/buddy`, `/ignore`, `/mark`, `/page` ("Dan pages
you: Hello"), `/locate` ("Dan is currently in Halloween World in San Francisco" — the park and its
city), `/goto`, `/afk`, `/vote` and the mood emotes, in the selected language's words. A player's
online buddies are told when they enter a park
([APPROX:ONLINE-024](#approximation-register)).

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

### The version 2 package and what a visit does

A published park is the park **as the player built it** (package format 2, payload
version 2): every non-empty cell of the park cell map (paths and queues with their
links, queue links and flags), every placed object with its anchor, rotation, open
state, price and state of repair, every player-built coaster track with its pylons,
and the park's published statistics (rating, value, balance, visitors, entrance fee,
open state, in-game date). The manifest carries the park's name, description, author
and created date/time — the fields the original's publish request sent with the park
file and the website showed per park (docs/reverse/ONLINE-VISIT.md). A layout digest
(FNV-1a 64 over cells, objects and coasters) is part of the payload: the publisher
writes it from the live park and the visit recomputes it from what it built, so
"the visitor sees the owner's park" is checked, not assumed. Version 1 packages from
OpenTPW 0.7 (the original level's imported path/object records and the prototype
Totem) still import unchanged; they carry no built layout, so a visit of one draws the
shipped level import and says so.

A visit applies the published layout read-only and behaves as the original's visit
evidence shows (docs/reverse/ONLINE-VISIT.md, from the Mac online code, the manual and
the archived official pages):

- **Walk**: the visitor views and walks the rebuilt park with the normal park camera.
- **Ride**: the object panel of a visit offers *Ride* for a completed coaster or the
  prototype ride; the camera rides the front car along the published track (or the
  Totem's carriage motion). Escape, a right click or the button leaves the ride.
  Flat rides have no traced seat motion in OpenTPW yet and cannot be ridden here.
  Visits and editable parks use the same `Level.EnterRideView` / `ExitRideView`
  state and `RideCameraMode`; the visit's object-panel button enters that view.
  The original seat and mode details remain qualified by UI-093–095.
- **Inspect**: an object's panel shows its published state of repair (never simulated
  values), and the park information window shows the park's **published statistics**
  read-only — rating, value, balance, visitors and the published in-game date — with a
  note that nothing runs in the visit. No economy, staff or research is created or
  advanced, ever.
- **Report and vote**: downloading a park to visit it sends one visit report to the
  server (`POST /api/v1/parks/{id}/visits`), the counterpart of the original client's
  MarkParkVisited message per park entry; each report counts one visit for the park
  (docs/SERVER.md). Voting stays a separate action from Find Parks or the city lobby.

Visitors and rides animate locally (the visited park's own scripts run); those
simulations are not synchronized multiplayer, and no other players appear in the park
(the original's chat parks with online persons are not implemented). A visit cannot
place or remove rides, build paths or save, load or export the sandbox. The panel and
startup log explicitly report mismatches, missing objects/bonus archives and terrain
hash differences. Missing content is never downloaded automatically. Original
textures/models/audio and executable code are never embedded in these sharing files.
Live multiplayer, original server behavior and original online panel visual parity
remain unsupported.

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
| 070 | Platinum Tickets shown from the player's own server page: one per recorded visit of their parks |
| 071 | The five chat parks' default names; the original's room list is not recovered |
| 072 | Walking in a chat park sends one position per step under its own rate limit (`MovementBurst`/`MovementsPerSecond`) |
| 068 | The version 2 layout digest: FNV-1a 64 over the payload's cells, objects and coasters, written by the publisher and recomputed by the visit |
| 069 | The visit's Ride action in the object panel (coasters and the prototype ride) and its exits |
| 074 | The visit's park information window showing the published statistics read-only |
| 075 | The visit counter counts accepted visit reports, one per visit |
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
| ONLINE-003 | Response strings 102 and 110 mean failure to add an ignored player and a buddy respectively, based on nearby string blocks. | Original response-code table. |
| ONLINE-004 | Buddy response strings 111–113 mean online, offline and removed. | Localized variants or the original response-code table. |
| ONLINE-005 | Chat command semantics follow command words, response strings and weachatr.dll export names; say/emote/shout reach a room, wshout/ushout all rooms; hearing has no positional effect; blackmark creates a moderation report. | Original chat server behavior, including command reach, hearing range and blackmark handling. |
| ONLINE-006 | A visitor already inside a park must leave it before visiting another, inferred from CHAT_COMMANDS string 127. | Original park-visit transition behavior or an original chat/runtime trace. |
| ONLINE-007 | The globe camera's rates, steps, damping and radius ease are applied per tick at 30 ticks per second, and the angles move by velocity × ticks. | The original (tp.exe 0x5D5C50 → 0x5DC860) runs once a frame with no fixed rate: it damps the velocities once a frame, then moves the angles by velocity × dt and eases the radius by 0.3 × dt, dt = min(elapsed ms, 500) × 0.01. Still unknown: its frame rate on the online screens and how often a held panel arrow repeats its step (0x4C53F0, events 0x10008/0x10009). |
| ONLINE-009 | Edge and right-button steering add rate × offset × step per tick to the turn velocity. | The original (0x5DC860) adds, once a frame and without the step, rate × 50 × the cursor's depth into the 1 % edge band (at most rate / 2) or rate × the right button's offset from the centre; these need its per-frame integration (ONLINE-007), so the frame rate is still unknown. |
| ONLINE-010 | The hovered city's name and park count are drawn at the top right in the labels' rectangles (tp.exe table 0x7581A0: 93201 (1477,90)-(2048,240), 93202 (1627,275)-(2048,395)), white and centred, without the `hilight` model their table entries bind. | Whether a label draws that model (highlight.MD2) behind its text. |
| ONLINE-012 | Edge steering starts once the cursor has moved on the globe screen. | The original has no per-screen gate: it reads the online front end's last mouse-move position (0x5D5DD0), the canvas centre until the first move after the front end starts (0x5D5770), so it steers at once when the cursor already rests at an edge (for example back from a city lobby). Still unknown: an OpenTPW home for that move history across the online screens. |
| ONLINE-013 | Pins stand on the mesh surface (radius 119.7) at the decoded (s, s, p) scale of their root node. | The original puts the pin's root node at the city record's position vector (0x5DC3E0 → 0x5D8190), which the city list from weacityr.dll fills; its radius is not decoded (a city list from the original service or a capture of its globe). |
| ONLINE-017 | The publish globe shows UITEXT 419 in the city name label while no city is hovered. | Its panel is read (tp.exe 0x5DD910 → 0x4C4FB0(1): the worldlob table without the satellites, Find Parks and back, with the city labels); neither tp.exe nor the Mac publish globe code loads UITEXT 419 as an immediate, so where it showed is unknown. |
| ONLINE-019 | A chat park is shown as a flat overhead view in which each player is a marker that the server's relayed movement moves; the player walks their own marker by clicking (one walk speed; the original's double-click teleport is not separated). | The original chat park's three-dimensional scene, avatar model and animation (manual pp. 63-68) are not decoded. |
| ONLINE-021 | The chat overlay's action buttons are a fixed top bar (vote, earmuffs, postcards, goto, page, locate) and three list buttons (buddy, ignore, blackmark). | The original (tp.exe list handler 0x4BE820) gave 799016 earmuffs on/off, 799017 vote and 799018 the unidentified shared handler 0x4A9380, and the six-button table 6024-6031 (tp.exe 0x758CD8) is a popup on a name in the list (0x4BE290, at the cursor): blackmark, goto, ignore, remove buddy, locate, page (0x4BEDE0); other names get the popup 12290 (blackmark, goto, ignore, add buddy). Still unknown: what 0x4A9380 does and which list flag picks the popup. |
| ONLINE-020 | The park details' eighth label row shows the players currently in the park's chat room (UITEXT 273). | The original's last row showed the Instant action mode flag (UITEXT 282), which the package format does not record. |
| ONLINE-022 | The postcard photo attached by Send Postcard is the park's generated map thumbnail, and no park reference is attached. | The original attached a camcorder snapshot of the visited park (manual pp. 51/67); its content and encoding are not decoded. |
| ONLINE-023 | Park names are masked with the word filter when published, and the stored masked name is what everyone sees. | The original masked at display on the client: the city lobby's park label (tp.exe 0x4B8850), the park details (0x4C0AE0), Find Parks (0x4BFC70) and the other-parks chart (0x4C1E40) pass the name through the filter 0x5D3230 while the filter flag (0x5B09B0) is set, with the local swears.txt/alloweds.txt (0x5AFD70, 0x5D3140). Still unknown: what sets the filter flag (the matching rules are ONLINE-001). |
| ONLINE-024 | A buddy entering a park is reported as "Your buddy X has entered Y" from OpenTPW's own text. | The original notification's string; CHAT_COMMANDS 111-113 ("Your buddy ") have empty English suffixes and no enter-the-park variant is recovered. |
| ONLINE-076 | An owner's visits to their own park never count, and one visitor counts at most once per UTC day per park | The original service's guard against repeated or self visits (every visit earned Platinum Tickets, FAQ S2) |

## Retired ride-camera entry

`APPROX:ONLINE-073` is retired: the separate visit-camera view now uses the shared
camera approximation UI-094, with UI-093 and UI-095 covering entry and restoration.
The original view details remain unresolved; sharing the implementation does not
establish original runtime parity.
