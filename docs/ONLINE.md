# OpenTPW online extension

OpenTPW provides an opt-in, self-hostable service for shared parks, postcards and chat.
It does not connect to the original Theme Park World service and is not a reconstruction
of its network protocol. No service address is configured and no network request occurs
until the player selects a server and an action in the online screens.

Online is the official extension `opentpw.online` ([MODS.md](MODS.md)). It is on by default; switching it off in
`mods.json` or with `--disable-extension opentpw.online` hides every online entry and refuses the online
command-line options until the next start.

The online screens support the server's Game News and System News, account registration/login, park export/publish/search/download,
voting, read-only visits, local postcard composition, sending the outbox, fetching the
inbox, and chat — including the globe's five **chat parks**, parks the players visit
together: everyone in a chat park sees who is there and where they stand, and the server
relays each player's movement. A visit of a published park or a chat park shows the park's
other visitors walking in it ([Players in a visited park](#players-in-a-visited-park)). Chat commands use the selected original `CHAT_COMMANDS.str`
words; original text is used where verified and supplementary labels cover the six supported
languages. Chat lines use the local `GAME8AA.bf4` font. What the original service had and
where each piece stands is inventoried in [reverse/ONLINE-FEATURES.md](reverse/ONLINE-FEATURES.md).

Building a park together (co-op, `[EXT:online-coop-*]`) is planned as host-sequenced lockstep; its approved design
decisions and the command layer every park edit now goes through are in [COOP.md](COOP.md). The server side of
its sessions and the game's link to them exist ([Shared parks](#shared-parks)). A player shares the open park from the
pause menu's Build Together line ([COOP.md](COOP.md), "Sharing the park from the game"), or one of their saved parks
from the lobby's ([COOP.md](COOP.md), "Sharing a saved park from the lobby"). Another player joins from the lobby with the
park's code ([COOP.md](COOP.md), "Joining a shared park from the lobby"), from the open list, or by the host's invitation,
which reaches the player anywhere in the lobby ([COOP.md](COOP.md), "Invitations and the open list").

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
| OpenTPW online (menu) | (OpenTPW) login, news, postcards, chat, import, account, behind a button on the globe; a window opened from it takes its place, so the menu's buttons and status line do not show around that window (`[EXT:ONLINE-UI]`) | `w_big`, `i_mail` (authored place) |
| News | Online news panel, titled "News from ThemeParkWorld.com" (UITEXT 266) with Game News and System News tabs | `w_big` |
| Online Login | tp.exe's login window (table `0x7585a8`, built by `0x4bb070`, handler `0x4bb030`, UITEXT 0xec-0xee; the twin of the dial-up details dialog `0x7583e8`): name and password, 16 characters each, labels inside the fields, the bare check (`b_okay`) and cross (`b_exit`) buttons; OpenTPW's server address, authenticator code, Register and Forgot password sit in a framed panel of their own under the window, which also holds the status line (`[EXT:ONLINE-UI]`, `[EXT:own-window-sizes]`) | `wdialogw`, `!frame`, `b_okay`, `b_exit`, `w_med`, `f_text1` |
| Find Parks | Find Parks in its original composition (Mac table `data:0x50f84`): six columns Name/Theme/City/Position/Visits/Votes, search by player name | `list_findprks`, `b_vote` |
| Park details | The original details dialog (Mac table `data:0x51150`): eight label rows, description, enter-park and the owner's un-publish | `w_big` |
| Other Parks | The city's outside-the-top-10 chart (Mac table `data:0x5137e`, "Other Parks In …", UITEXT 446) | `w_big`, `list_findprks` |
| Publish Park | tp.exe's publish window (table `0x7595b0`, built by `0x4c4670`, handled by `0x4c4480`; Mac table `data:0x514de`): the name field (15 characters, starts with the park's theme name), the description field (126 characters, starts with UITEXT 0xdc), the bare check (`b_okay`) and cross (`b_exit`) buttons; the check publishes and closes (logged out it opens the login dialog); OpenTPW adds the website opt-in (off by default), the export button and a status column inside the window (`[EXT:ONLINE-UI]`) | `window2`, `!frame`, `b_okay`, `b_exit` |
| Send Postcard | tp.exe's postcard window (table `0x7540d8`, `0x4a8c00`/`0x4a7d70`): photo frame, message, title and recipients fields with their prompts (shown while a field is empty) and lengths (the title at most the card format's 64 characters), clear, tick (unedited title and message take the defaults UITEXT 215/216; no recipient shows 213) and X; the preview and address book are disabled (UI-042); recipients are player names and e-mail addresses split at `;`, `,` and newlines, not spaces (`[EXT:postcard-player-names]`) | `window4`, `!frame`, `b_clear`, `b_okay`, `b_exit` |
| Unsent Postcards | tp.exe's outbox window (table `0x758878`, built by `0x4bc710`, handled by `0x4bbc20`): the selected card's picture frame and addresses, the bulk buttons mark all for sending, mark all for deletion and unmark all, the Title/Date/Send/Delete list (UITEXT 309-312) and the tick. Every card starts marked for sending; a click on a row's Send or Delete cell toggles that mark and clears the other. The tick deletes the cards marked for deletion, sends the ones marked for sending and closes the window; Escape closes it without either. Logged out, the tick opens the login dialog over the window where the original checked the account's settings; the card's picture is not drawn in its frame yet, and a mark shows as an X (`[EXT:ONLINE-UI]`) | `window4`, `!frame`, `list_emailaddr`, `b_okall`, `b_delall`, `b_nuall`, `list_outbox`, `b_okay` |
| Inbox | (OpenTPW) | `list_outbox` |
| Chat | The original chat overlay (tp.exe table `0x758ae8`): chat frame, people list with its two tabs and slider, postcard, vote and earmuffs buttons, message list with its slider, text entry, and the name popups (tables `0x758cd8`, `0x758e18`) — over the chat park view | `chat` (`f_chat`), `b_speeps`, `b_sbuddi`, `b_chtpcard`, `b_vote`, `b_muff`, `!f_plain`, `b_bmark`, `b_gopeep`, `b_ignore`, `b_unbuddi`, `b_peeploc`, `b_page` |
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
satellite is the number of people currently in this chat park". In tp.exe a satellite is a
**park**: the satellites show the parks of a city of their own (city 1's park list, entry *i* on
satellite *i*), a click opens that park's details (0x4C56E0 → 0x4C0AE0) and Enter visits it like
any park, in the chat mode ([reverse/ONLINE-VISIT.md](reverse/ONLINE-VISIT.md#chat-satellites-tpexe)).

OpenTPW does the same. A click opens the chat park's details in the variant tp.exe shows for such a
park — the name as the title, "Theme : …" (UITEXT 271 and the theme's name), the description and
Enter, with the owner's action disabled and no statistics — and Enter visits its park in three
dimensions with the room's other players walking in it, as [Players in a visited
park](#players-in-a-visited-park) describes: the chat mode, the figures, walking, riding. The park
is the published park the server names for the satellite, downloaded and reported as any visit,
or else the starting park of the satellite's original level (jungle, hallow, fantasy or space),
which the game builds from the level's own data. The visit walks in the satellite's standing room
(`satellite:<n>`), so the satellite's count counts it.

A room without a park to visit is shown from above: a marker per player, a click walks the player's
marker there, and the server relays each position to everyone in the room. That is the view of a
satellite on an older server (one without the satellites' scenes), and of a published park's chat
joined from its details dialog. The movement itself — one position per walk step, relayed to the
room, rate-limited separately from chat — follows the manual ("Left click to walk to
destination"). Positions are fractions of the park in both views, so a player who sees the room
from above walks among the park's visitors.

| EXT label | OpenTPW decision | Known original evidence (not reproduced) |
| --- | --- | --- |
| `online-chat-park-scene` | The server names a published park or an original level for each satellite (`chatparks.json`, docs/SERVER.md), and the game asks for these scenes at the click. A satellite without a published park visits the starting park of its level, built by the game from the level's data once the level's files are there. The visit walks in the satellite's standing room, which an older game enters from above and the satellite's count counts; so a satellite's visit of a published park has no vote, as no chat park has, and its players are not counted at the park itself. | The satellites were the parks of city 1 (`GetParkInCityList(0, 1, …)`, tp.exe 0x5B7610 → 0x5BCB30), read from the client's copy of that list at the click (0x5B7910), and each was visited like any park in the park's own room ("%d" of its id, `CreateRoom` at 0x4E6A67). Which parks the service kept in city 1 is not known. |
| `online-chat-park-details` | A satellite's details are always the short variant, titled with the chat park's name. | 0x4C0AE0 shows the short variant for a park whose city has the flag +0x60, titled with the park's name. Whether city 1 had that flag is not known. |

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

Chat is the original chat overlay (tp.exe table `0x758ae8`, built by 0x4BF460 when a park enters chat
mode), at its authored places over the chat frame (`f_chat`); a visit with other players shows it over
the visited park ([Players in a visited park](#players-in-a-visited-park)), a room without a park to
visit over the overhead view ([Chat parks](#chat-parks-the-satellites)):

- **People list** at the left, with its two tabs: the players in the room and the buddies. The
  overlay asks the server for the buddy list as the chat connection opens and again on the buddy
  tab; the list it has stands until the answer comes. A buddy's name is green in this park, blue online elsewhere and red offline; any other name
  is white (the row colours of 0x4BE730 by the flags of 0x4EE5C0: buddy, online, in this park,
  ignored).
- **Name popups**: a right click on a name opens the original's popup at the pointer — six buttons
  for a buddy (ignore, goto, locate, page, black-mark, remove from the buddy list; table `0x758cd8`),
  four for anyone else (ignore, goto, black-mark, add to the buddy list; table `0x758e18`), none for
  the player's own name (0x4BE290). The ignore button stays down while the name is ignored. A
  button sends its command for the name. A click beside the popup closes it when the button is let
  go, so the click reaches nothing under it; a right click or Escape closes it too. A goto to a
  player in the visited park takes the player's figure to them.
- **Player buttons** under the list: the postcard (opens Send Postcard), the vote for the visited
  park and the earmuffs, which stay down while they are on (0x4BE820). The vote goes once, and not
  again after the server refused it (the player's own park, a vote already given today; the day's
  limit holds for every park). A chat park has no vote.
- **Message list** along the bottom: the room's lines cut into rows at the list's width, the last
  twenty rows, following the newest (0x4BE970).
- **Entry** under the messages, up to 255 characters: a click gives it the keys, and so does Enter while nothing else
  has the focus; the pointer passing over it does not, and neither do the arrows or Tab. Enter says
  the line and Tab leaves the entry (0x4BE3A0). While it has the keys, the camera still takes the
  wheel, the edges and the drag over the park, and no hover help covers the entry while a line is
  typed.
- **Panels**: the frame's left column and bottom strip keep clicks off the park; the park shows
  through the opening between them and takes the clicks there (the triangles of `f_chat`).
  Pushing the pointer against the screen's edge over them still scrolls the camera.

The overlay keeps the server's answers for its state: the buddy list (its own asks stay out of the
message list; a typed `/buddy` lists it), the ignored players and the earmuffs (both per chat
connection, as the server keeps them) and the parks voted for or refused. The commands
themselves can also be typed: `/say`, `/tell`, `/shout`, `/emote` ("Dan is bouncing up and down"),
`/earmuffs`, `/buddy`, `/ignore`, `/mark`, `/page` ("Dan pages you: Hello"), `/locate` ("Dan is
currently in Halloween World in San Francisco" — the park and its city), `/goto`, `/afk`, `/vote`
and the mood emotes, in the selected language's words. A player's online buddies are told when they
enter a park (in OpenTPW's own words, [EXT:online-buddy-enters-text](#online-design-choices)).

| EXT label | OpenTPW decision | Known original evidence (not reproduced) |
| --- | --- | --- |
| `online-chat-help` | The overlay's controls show UIHELPTEXT 402-409, 418, 419, 422, 461 and 480 by their wording (tabs, names, earmuffs, scrollers, vote, postcard). The entry shows none: the help box over it would cover UIHELPTEXT 420 and the typed line. | The overlay's table carries no help ids; where the original took its controls' help from is not read. |
| `online-chat-person-flags` | The overlay asks for the buddy list as the chat connection opens and on the buddy tab, at most once in five seconds counting a typed `/buddy`, and keeps those answers out of the message list. A buddy counts as online and in this park while the room's roster names them; otherwise as the last answer says. | The original kept both flags per buddy from the server's buddy messages (0x4EE5C0, buddy record +0x1C and +0x1D). |
| `online-chat-line-limit` | A chat line carries up to 256 characters; the overlay's entry takes 255, as the original's. | The original entry's limit is 255 (0x4BF460); the original server's limit is not known. |
| `online-chat-row-highlight` | The chosen name is marked with a bar. | The list's highlight art (`hilight`, set by 0x4BF460) is not drawn. |
| `online-chat-popup-place` | A name popup's corner is at the pointer, kept inside the screen. | 0x4BF370 places it at the pointer with an offset that is not read. |

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


### Postcard recipients and delivery

[EXT:postcard-player-names] The recipient field accepts a mixture such as
`Bob Smith; friend@example.test, Alice`. Semicolons, commas and newlines separate
recipients; spaces stay inside player names. Tokens containing `@` must be plain
ASCII mailbox addresses (no display names, comments or quoted local parts); all
other tokens use the existing player-name rules. The shared package/client/server
validator rejects malformed or duplicate recipients and caps a card at ten. The
composer uses the original localized UITEXT 213 refusal and preserves the draft.

Composition is still offline: the tick saves to Outbox. Sending the cards marked in the
Unsent Postcards window (its tick) requires login.
Player names go to that server's Inbox; addresses go through its already configured
`IEmailSender` / Resend transport, using `EmailFrom` and `EmailReplyTo`. The e-mail
contains the title, sender's player name and message, plus `postcard.png` when the
card has an image. It does not disclose the sender's recovery address. Each inbox
copy lists only its own recipient, and each e-mail has a single recipient.
Unknown players are reported as player recipients, not unknown e-mail recipients.
A server without mail configuration continues player delivery and reports that
addresses cannot be delivered. No client-side SMTP or new mail credentials are used.

Partial responses list only confirmed recipients. A send records that subset in
Sent and atomically rewrites the Outbox card with the unconfirmed recipients, so a
failed address, unknown player or full inbox is not silently discarded. A failed
request leaves its local card in Outbox. Successful delivery here means an inbox
write or mail-provider acceptance, not a guarantee that an external mailbox received
or displayed the message. Old clients can still send fully successful player cards;
partial-result handling requires this client version.

The server persists hashed account/content/recipient receipts separately from inbox
files, and uses deterministic inbox delivery ids. Retrying the same immutable card
also after a restart or local recipient-list reduction does not resend a confirmed
recipient within the 30-day receipt lifetime. Reusing a client-chosen card id with
different content is a different delivery. Confirmed receipts expire after 30 days;
this is bounded deduplication, not a permanent exactly-once guarantee.

For e-mail, the same hashed key is sent as Resend's `Idempotency-Key` header
([provider contract](https://resend.com/docs/dashboard/emails/idempotency-keys)).
Because that provider retains keys for 24 hours, an unconfirmed attempt may be
retried for at most 23 hours. Afterward the server reports an uncertain result and
requires the player to check with the recipient before composing a new card.
Uncertain tombstones do not expire automatically, and are capped at 256 per sender;
further new e-mails then require operator attention. A process or storage failure
between the provider's acceptance and the local receipt write is explicitly
uncertain, never reported as guaranteed exactly-once delivery.

The existing upload/address rate limiter remains. `PostcardRecipientsPerHour`
(default 20) additionally limits recipient delivery attempts per authenticated
account, and `PostcardEmailsPerAddressPerHour` (default 3) limits attempts to the
same address across accounts. Failed mail attempts count; confirmed retries do not.
These one-hour attempt counters survive restart. Their persisted records and the
receipt ledger contain hashes and timestamps, not raw e-mail addresses or messages.

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
and export. After eight rendered frames it boards the park's first open rideable ride
through the HUD and checks on every frame that the camera sits on the seat the view
rides (the ride model's camera seat, a coaster's camera car) or, without one, follows
the fallback frame, until simulation frames have moved it at least 0.25 units. It then
leaves the ride, checks that the same park camera returns and that the published layout
is unchanged, and captures `artifacts/native-smoke-online-visit.png`. A ride moves when
its own script runs it: the original Jungle's Belly Bounce bounces only with riders
aboard, so in a Jungle export the step waits for the visit's first guests (40 to 55 s).
A step that stalls times out after two minutes with the ride's riders, queue and the
guests in the park in its message. It uses no sandbox build/save smoke steps.

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
- **Ride**: the object panel of a visit offers *Ride* for a completed coaster, the
  prototype ride and every ride whose model carries a camera seat (the Belly Bounce,
  the bumper cars, the go-karts and others); the camera rides the seat, a coaster's
  camera car along the published track. A ride moves when its own script runs it: the
  Belly Bounce bounces only while the visit's guests ride it. Escape, a right click or
  the button leaves the ride.
  Visits and editable parks use the same `Level.EnterRideView` / `ExitRideView`
  state and `RideCameraMode`; the visit's object-panel button enters that view.
  The original seat and mode details remain qualified by UI-094 and UI-095.
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
simulations are not synchronized multiplayer: each visitor's guests and rides are their
own, and only the players themselves are shared ([below](#players-in-a-visited-park)). A
package carries no guests, so a visit starts empty and fills from its own arrivals, which
read the park turn of the save the visit is built on (`EXT:online-visit-arrivals`): the
visit of a Jungle park sends its first group at once, as an own park started from the
shipped save (Instant Action) does. Counted from turn 0, the first group would wait until
turn 604, about 150 s into the visit, with nobody before it to ride a ride such as the
Belly Bounce. A visit cannot
place or remove rides, build paths or save, load or export the sandbox. The panel and
startup log explicitly report mismatches, missing objects/bonus archives and terrain
hash differences. Missing content is never downloaded automatically. Original
textures/models/audio and executable code are never embedded in these sharing files.
Building one park together, original server behavior and original online panel visual
parity remain unsupported.

### Players in a visited park

A visit of a park downloaded from the server (Find Parks, the park details or a city lobby's
*Enter Park*) while logged in puts the player in the park's chat room (`park:<id>`), as the
original's visit switched to chat mode on entering the park (manual p. 65); a chat park's visit (a
satellite's *Enter*) puts them in the satellite's room (`satellite:<n>`). Every visitor of the
park — and anyone in its room through the park details' *Chat* button or the overhead view —
appears as a figure in the park (`ParkPresence`):

- **Figures**: each player is drawn with the guests' kid sprites (the pools extension's sets when
  it is on), walking or standing like a guest, with their name over their head on a dark plate —
  the player's own in yellow — under the HUD's windows. Each figure gets the original's avatar
  sparkle once a world turn: effect 53 *Avatar* for the player's own figure, 57 *OtherAvatar* for
  the others, from `Data/Particle/Tp2.plb` (tp.exe 0x4F7C60 / 0x4FA470 in the online person's tick
  0x50B453; docs/reverse/ONLINE-VISIT.md).
- **Chat mode**: the visit starts in the original's chat mode, which puts the chat overlay
  ([The chat overlay](#the-chat-overlay)) in place of the park's HUD (tp.exe 0x4862A0: the HUD does
  not exist in chat mode); **F4** switches between it and the visit's HUD, as it switched the
  original's overlay off and on (0x4BE3A0), and **Enter** over the park returns to the chat mode
  with the entry taking the keys. The chat mode starts once the visit is in its park's room, stays
  through a reconnection, and gives way to the HUD when the server refuses the park. The HUD shows
  the room's lines in the corner chat box.
- **Walking**: in the chat mode a left click anywhere in the park's opening (not on the overlay's
  panels, nor on a rideable ride: see Riding) walks the player's figure there, as the original's chat mode panel does (help 401,
  "Left-click to walk to this point"); in the HUD a click on the ground that hits no object does,
  and objects keep their click (a ride's window, the ride view). A double click teleports (manual
  p. 68). The walk goes from cell centre to cell
  centre round water, objects, the fence and cells without terrain, by the octile distance the
  original's person route uses (0x50F8E0), and straight when there is no route. A spot the figure
  cannot reach at all (an island, a fenced corner) is known at once from the park's connected
  areas, which the first click labels, and a search gives up after 40,000 cells.
- **Places**: the player's figure enters the park at the centre of the entrance's first cell, and
  its first place goes out as a teleport, as the original's own person entered (tp.exe 0x4E5EF0:
  entrance 0's cell, `MovePlayer(…, 1)`). The figure's place goes to the server as a fraction of the
  park's map (the chat protocol's `move`), at most every 0.12 s while it walks, on arrival, and
  every ten seconds while it stands; the server relays it to the room. The other figures follow the relayed places and
  jump when a place is more than three cells away (a player in the overhead chat view, whose
  square maps onto the same fractions), or when the movement was a teleport.
- **Riding**: in the chat mode a rideable ride under the pointer shows its name with "Left-click
  to ride" (UIHELPTEXT 580) and a click rides it (tp.exe 0x4BD7F4 → 0x4E15B0, the ride window's
  Ride it!), as the HUD's Ride it! does. While the player rides, their figure is hidden for the
  park's other visitors, and when the ride ends it appears at the ride's exit (where the ride has
  visitor cells; else where it stood): the original's `BOSS_CHAT_MOVE` modes 2 (hide) and 3 (unhide
  and teleport). The ride view's screen covers the chat overlay until the ride ends, as the
  original hid both chat windows (0x4BD090), and its own exits leave it (Escape, the eject button,
  C, E and, with RMB cancel, a right click).
- **Leaving** the park leaves its room, so the others see the parting; a chat connection that
  the visit opened is closed again, so the Online World's lines do not follow the player into
  their own park. A park that was unpublished meanwhile is refused by the server; its visit then
  shows no figures.

Walking runs on real time, whatever the visited park's speed or pause. Figures need a server that
answers each room's first movement with where everyone stands (protocol version 4,
docs/SERVER.md); with an older server the visit chats but shows no figures. Hiding a riding
player needs protocol version 5, whose movements carry their mode; on a version 4 server, and
for a player whose game is older, the riding player's figure stands where it was until the ride
ends. A reconnect reads the version again before a hide goes out, and holds the hide until that
read finishes, so neither an older game nor a current one is shown the rider in the meantime. A
failed read is tried again, with a growing wait that levels off, for as long as the connection
stays open. Leaving the room discards any coalesced movement that has not entered the send
queue, including a held hide, so another park on the same connection starts with its own visible
position. A park file opened
from disk (Import file, `--visit-park`) has no room and no figures.
`--visit-park <file> --smoke-test --smoke-presence` runs the native visit check with three scripted
players round the entrance: the visit starts in the chat mode, their figures, names and sparkles are
drawn, a click walks the player's figure and its places are sent, Enter focuses the entry and says a
line, Tab leaves it, a right click on a name opens its popup and the ignore button sends its
command, F4 switches to the HUD and back, a buddy's popup has its six buttons, and a click on a
ride in the chat mode rides it with the player's figure hidden and brings it back at the ride's
exit (`artifacts/native-smoke-online-visit-presence.png`, `native-smoke-online-visit-chat-popup.png`).

| EXT label | OpenTPW decision | Known original evidence (not reproduced) |
| --- | --- | --- |
| `online-visit-presence` | The visit joins the park's room and the server relays fractions of the park, sent several times a second while a figure walks; a far jump is shown as a teleport, and from protocol 5 each movement says whether it walks, teleports, hides or shows the figure again. | The original's `BOSS_CHAT_MOVE` carried a cell position (8.8 words) and a mode: 0 walk to it (every client walked the online person there itself, 0x4F7DE0), 1 teleport (0x4F7E20), 2 hide, 3 unhide and teleport (tp.exe 0x4E7265 in 0x4E6E80). |
| `online-ride-hiding` | A player who rides is hidden for the room from boarding to the ride's end, and then shown at the ride's exit (or where they stood); any later movement shows a hidden figure again, so a lost "unhide" heals. The server sends the mode only to games that send one, and a hidden player's place to nobody who reads no mode. A hide waits until the version of the open connection is known, and a failed version read is tried again until it is. | The original hid the own person locally on entering any first-person view and sent mode 2 for a ride (0x4BD090 → 0x4EA3F0); a ride entered from the first-person walk ended at its exit cell with mode 3 (0x42A1D0, 0x4EA410), but the return to the overview from a ride boarded there sent only a teleport (mode 1, 0x4BD260 → 0x4EA2D0), which leaves the person hidden for the others: only mode 3 rebuilds its model, while modes 0 and 1 move a hidden person unseen. |
| `online-avatar-look` | A figure wears the guests' kid set chosen by an FNV-1a hash of the lower-case name, the same for every client. | The online person's own model (`MODEL_ONLINE_PERSON`) is not decoded. |
| `online-avatar-walk` | The player's figure walks 1.2 cells a second, about a quick guest's pace. | The online person's walk step (0x4FA2A0 on its route) is not read. |
| `online-avatar-route` | The cells a figure may cross: land, paths, queues, object entrances and exits and fixed walkways inside the park. | The route map the original passes to its route search (person +0xB4 → 0x511420) is not read. |
| `online-avatar-start` | A level whose entrance A lies outside its cell map starts the player's figure in the park's middle. | The original's own person enters at the centre of entrance 0's cell (tp.exe 0x4E5EF0, 0x4D8690(0)), which every original level has; OpenTPW follows it. |
| `online-avatar-click` | In the chat mode a click anywhere in the park but on a rideable ride walks the figure, in the HUD a ground click that hits no object; the ground's help is UIHELPTEXT 401 in both, and not while the chat entry takes the keys (the help box would cover it). | The chat mode panel's handler (0x4BD4A0) walks on a park click with help 401. |
| `online-chat-ride-click` | In the chat mode a ride whose ride view opens (a camera seat or a running coaster) shows UIHELPTEXT 580 with its name and a click rides it. A feature's choosable bit is not tracked: a feature rides when its view opens. | Over a thing of type 3 that is no add-on (AddOn.UpgradesId 0) and of interface type 0, or 3 with the thing's choosable bit (+0x32 bit 0x10), the panel shows help 580 and a click runs the ride window's Ride it! (0x4BD7F4 → 0x4E15B0). |
| `online-avatar-labels` | Names over the figures, the player's own in yellow, drawn under the HUD's windows. | The decoded online code draws no names over persons; the chat overlay's people list names them. |
| `online-avatar-sparkle-cap` | At most six figures sparkle in a turn (the player's own first, then the nearest), and none while more than 80 of the particle pool's 120 emitters are live. | The original sparkles every online person every turn; its server capped a park's attendance ("Capping the number of people in online park"). |
| `online-visit-chat-mode` | F4's other side is the visit's read-only HUD (its windows, the camera and the ride view), and Enter over the park returns to the chat mode with the entry focused. The camera's edge scroll works over the overlay's panels along the screen's left and bottom edges. | The original's F4 switched between the overlay and a bare full-screen panel over the park (0x7CC8C0, 0x65DD7B); its chat mode has no HUD. Whether its edge scroll worked over the overlay is not read. |
| `online-chat-goto` | A name popup's goto to a player in the park teleports the player's figure to them (the server keeps the player in the park's room). | The original server's answer to a goto (command 0x13) is not read. |

## Shared parks

`[EXT:online-coop-session]` A shared park is a session on the server: a host and up to three guests
(decision Q5) who build one park together ([COOP.md](COOP.md)). The server relays the host's command
stream and the players' messages without reading them. The game's link to the server's sessions is open
while a shared park uses it, and while a logged-in player is in the lobby ([COOP.md](COOP.md), "Invitations and
the open list").

- **The co-op socket.** `/api/v1/coop` (server protocol 7) is a WebSocket of binary messages
  (`OpenTPW.Online.Coop.CoopProtocol`), beside the text chat socket. A desktop game sends its login
  token as a header, a browser as the chat's first frame. The first message is a hello with the game's
  build, its simulation-changing mods in load order and a random id of the game process; the server
  answers with the session the account belongs to.
- **One game per account.** A reconnect of the same game process replaces its old connection; another
  game of the account (a forgotten browser tab) is refused unless the player takes over. A close says
  whether the game reconnects and resumes (a restart, a lost or slow connection, a rate close) or stops
  (replaced, in use elsewhere, a protocol error, a logout): `CoopCloseStatus`.
- **Sessions.** A host creates a session, listed on the open list or by code and invitation only
  (decision Q4); a code is six characters without the ones read alike (0 and O, 1, I and L). A player
  joins by code, or from the list (`GET /api/v1/coop/sessions`: listed sessions with room and a
  connected host, for the same build). A join needs exactly the host's build (Q11) and the same
  simulation-changing mods in the same order (Q16); a refusal names the difference. The host invites a
  player whose game has its link open (a player in the lobby), removes a player (who cannot come back), and ends the session by
  leaving; Q18's host migration is a later phase. Members have ids that are never reused, and slots
  (the game's player index; the host is 0).
- **Order and resume.** Only the host sends frames, numbered; the server relays a frame only from the
  host's current connection and only when it follows the last one, so no guest ever misses one unseen.
  A frame out of turn is dropped and the host told which one comes next. A member whose connection
  drops stays a member; it resumes with its hello and hears the last frame the host relayed, and a
  session ends when its host has been away ten minutes. A deploy stores that number exactly, then
  closes each queue to new messages and each socket after what its queue holds, so a guest that reads
  its queue within three seconds gets every frame up to the stored number; a hello during the stop is
  told to come back and a request is ignored, and the game resumes on the new server. That number is
  exact for the next run only: the new server stores it again as unknown at once, so after a crash the
  number may be behind, the welcome says so, and the host's next later frame sets it. A host that
  no longer holds the frames the server needs gets every member the park again and tells the server its
  frames go on past them (protocol 7, `[EXT:online-coop-snapshot-join]`, [COOP.md](COOP.md), "The host's
  trimmed frame"); the server takes that frame next and never goes back. A game whose
  release or mods changed while it was away cannot resume: it leaves (a host's change ends the
  session). A message to one member is delivered at most once (C2b asks again after a resume).
- **The session's chat room** (C2d-1). Each session has a chat room of its own, `coop:<id>` (its session
  id), on the chat socket. Its members alone enter it, by a join or by going to a member who is in it;
  anyone else is told there is no such park. The game's chat follows its session. On a server that has
  such rooms (protocol 8), hosting, joining or resuming a session opens the chat when it is closed (and
  tries again, with growing waits, while the connection fails), and
  the chat enters the room once the connection's own server version is read (a new connection after a
  lost one asks again only then). While the game is in a shared park its chat is the session's: no other
  room takes it, a goto is not sent (the chat says to leave the park first; a name popup's Go to moves the
  view to a co-builder's figure instead, C2d-4), and what is said to a room
  (lines, emotes, shouts, moods, the away notice) is not sent until the chat is on its way to the
  session's room ("The shared park's chat is not open."). Once on its way, what is said names the
  session's room, and the server drops it when the connection is not there (it was taken out, or the
  join was refused) and says so. On an older server the chat is not opened for the session, and what is
  said to a room is not sent. The chat goes back to the lobby once the session is over for the game,
  from wherever it is. What a member addresses beyond the room (a tell, a page, a world shout) goes
  where it is addressed, as from any room, and never names the session's room. The server takes a member who leaves or is removed,
  and every member of a session that ends, back to the lobby before the change is announced. Locating a player in a shared park
  says only that, never its title or room; a world shout or a page from the room does not name it; and no
  buddy is told a player entered one. The game names the room after its session's title, or "Shared park"
  when the title is not at hand.
- **Co-builders as figures** `[EXT:online-coop-presence]` (C2d-4). The members walk as figures with name
  plates in the session's room, as a visit's players do ([Players in a visited park](#players-in-a-visited-park)).
  Each member's own figure follows that member's view, not clicks; places go out only once the room is
  the connection's own, confirmed, and a shared park's figures do not sparkle. The game's side is in
  [COOP.md](COOP.md), "Co-builders as figures".
- **Join snapshots** `[EXT:online-coop-snapshot-join]`. The host uploads the park a joiner starts from
  (`PUT /api/v1/coop/{id}/snapshot`, the answer names its SHA-256; a limit of the session's own, 60 an
  hour) and its members download it by digest (`GET …/snapshot/{sha256}`, resumable with a range); the
  newest two are kept, and they go with the session. The game's side is in [COOP.md](COOP.md),
  "Joining a running session".
- **Limits.** A host may send far more than a guest, and an account's limits carry over a reconnect;
  per account the server counts sessions started, joins (and codes that matched no session),
  invitations, removals and downloads from the start of a snapshot; a refusal of a snapshot limit says
  when to try again (`Retry-After`). A slow reader is cut off (it
  resumes) instead of losing messages, and a connection whose login ends (a logout elsewhere, a ban, a
  recovery) closes within a minute. Every text the server sends fits what a game reads.
  [SERVER.md](SERVER.md) lists the settings.
- **The game's link** (`CoopLink`, under the online login). It opens the co-op socket and says hello
  with the game's build (its informational version) and its simulation-changing mods in load order,
  the used-only extensions included, and its own random id: a second link of the account (another game,
  or another online session of this one) is refused unless the player takes over. A socket lost by a
  close that resumes is opened again after 1, 2, 4, 8 and 15 seconds, then every 30 seconds, for ten
  minutes from the loss (a rate close waits at least ten seconds; a socket that keeps dropping soon after
  its welcome counts as the same loss), and its hello resumes the session; the welcome tells a host the
  last frame the server relayed, so it can resend from there. A connect that does not open within 20
  seconds is tried again, a link that hears nothing for 45 seconds opens a new socket, and the server's
  pings are answered at once. Any other close stops the link: another game of the account, a refused
  login, a protocol error. A create or join whose answer is lost with its socket is refused, so the game
  can ask again; a session the server names that the game does not hold (the game restarted, took the
  socket over, or lost the answer to its create or join) is left at once, with the reason of a leave
  that could not go out. Closing the link, or logging out, ends the session for this game without
  leaving it; the next login leaves it.

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

All `EXT:ONLINE-*` tags and the named `EXT:online-*` labels describe OpenTPW choices, not
recovered original behavior; the [design choices](#online-design-choices) list the labels that
replaced the former approximations.
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
| 071 | The five chat parks' default names, levels and descriptions; the original's list (the parks of city 1) is not recovered |
| 072 | Walking in a chat park sends one position per step under its own rate limit (`MovementBurst`/`MovementsPerSecond`) |
| 068 | The version 2 layout digest: FNV-1a 64 over the payload's cells, objects and coasters, written by the publisher and recomputed by the visit |
| 069 | The visit's Ride action in the object panel (coasters, the prototype ride and the rides with a camera seat) and its exits |
| 074 | The visit's park information window showing the published statistics read-only |
| 075 | The visit counter counts accepted visit reports, one per visit |
| UI | Original-style online screens: window composition, places of code-positioned controls, text field art and caret, server address field, Inbox and Import screens, the News window title |

## Online design choices

The online service is an allowed OpenTPW extension: it does not have to reproduce the original
service, whose servers are gone. Where an online feature imitates something of the original that
is not (fully) recovered, OpenTPW's behavior is an explicit design choice with an `EXT:online-*`
label in the code. These labels replaced the eighteen former `APPROX:ONLINE-*` IDs (closed
October 16, 2026); the IDs stay reserved in `tools/fidelity_retired.txt` (listed in the
[fidelity register](https://opentpw.io/docs/FIDELITY-REGISTER.html)) and are not reused. A label
does not claim the original's behavior: the last column keeps what is known about the original, so
a later fidelity pass can start from it. Traces of the Mac build
(`STP-PPC`) are in [APPROX-TRACE.md](reverse/APPROX-TRACE.md); tp.exe addresses are from the
Windows v2.0 executable.

| EXT label | Former ID | OpenTPW decision | Known original evidence (not reproduced) |
| --- | --- | --- | --- |
| `online-word-filter` | `APPROX:ONLINE-001` | Word filtering over the original `swears.txt`/`alloweds.txt` lists: case-insensitive substring matches in space-padded text, entry spaces as word boundaries, allowed entries exempt the hits inside them, hit characters except spaces become `*`. Simple and predictable for an operator's own server. | The original rule is partly traced in the Mac build (APPROX-TRACE.md ONLINE-001; STP-PPC 0x101a5664, 0x101a51e8, 0x101a5594, 0x1018b474): lines starting with `#` are skipped, matching is a case-insensitive wide compare, a pattern starting with `*` matches a substring, space-prefixed and default patterns check a letter/digit boundary, a match the allowed list matches is exempt, matched characters become asterisks, and a flag (mSwearFilterOn) turns the filter on. tp.exe's filter is 0x5D3230 (see `online-park-name-masking`). |
| `online-chat-add-failure` | `APPROX:ONLINE-003` | The server answers a failed ignore with CHAT_COMMANDS 102 and a failed buddy add with 110 (both "Could not add "), by their place in the ignore (98-105) and buddy (106-113) blocks. | The texts are a text resource and the original server chose the code; its response table is not recovered (APPROX-TRACE.md ONLINE-003). |
| `online-buddy-status-notices` | `APPROX:ONLINE-004` | 111, 112 and 113 announce a buddy coming online, going offline and being removed. | All three are "Your buddy " with empty English suffixes; the Mac client has the buddy and ignore files (`:buddy.dat`, `:ignore.dat`) but no status mapping; the meanings need the response text resource or a runtime capture (APPROX-TRACE.md ONLINE-004). |
| `online-chat-command-semantics` | `APPROX:ONLINE-005` | Command meanings follow the command words, response strings and weachatr.dll export names: say/emote/shout/eshout reach the room, wshout/ushout every room, the hearing range has no positional effect, blackmark files a moderation report. | Reach, hearing and blackmark handling were server-side; the client only reports results ("You can't blackmark yourself", "You can't ignore yourself") and the original chat server is not among the available files (APPROX-TRACE.md ONLINE-005). |
| `online-goto-leave-park` | `APPROX:ONLINE-006` | A goto into another park from inside a park is refused with CHAT_COMMANDS 127. | The text 127 shows the original had the rule; whether its server or client enforced it is not known (APPROX-TRACE.md ONLINE-006). |
| `online-globe-tick` | `APPROX:ONLINE-007` | The globe camera's rates, steps, damping and radius ease apply per tick at 30 ticks per second, and the angles move by velocity × ticks, so turning does not depend on the frame rate. | The original (tp.exe 0x54E360 state 2 → 0x5D5C50 → 0x5DC860) runs once a frame with no fixed rate: it damps the velocities once a frame, then moves the angles by velocity × dt and eases the radius by 0.3 × dt, dt = min(elapsed ms, 500) × 0.01. Still unknown: its frame rate on the online screens and how often a held panel arrow repeats its step (0x4C53F0, events 0x10008/0x10009). |
| `online-globe-steering` | `APPROX:ONLINE-009` | Edge and right-button steering add rate × offset × step per tick to the turn velocity. | The original (0x5DC860) adds, once a frame and without the step, rate × 50 × the cursor's depth into the 1 % edge band (at most rate / 2) or rate × the right button's offset from the centre; these need its per-frame integration (`online-globe-tick`), so the frame rate is still unknown. |
| `online-globe-city-labels` | `APPROX:ONLINE-010` | The hovered city's name and park count are drawn as plain white centred text at the top right in the labels' rectangles (tp.exe table 0x7581A0: 93201 (1477,90)-(2048,240), 93202 (1627,275)-(2048,395)), without the `hilight` model their table entries bind. | Whether a label draws that model (highlight.MD2) behind its text. |
| `online-globe-edge-gate` | `APPROX:ONLINE-012` | Edge steering starts once the cursor has moved on the globe screen, so a cursor resting at an edge does not spin a globe that just opened. | The original has no per-screen gate: it reads the online front end's last mouse-move position (0x5D5DD0), the canvas centre until the first move after the front end starts (0x5D5770), so it steers at once when the cursor already rests at an edge (for example back from a city lobby). OpenTPW keeps no move history across the online screens. |
| `online-globe-pin-radius` | `APPROX:ONLINE-013` | Pins stand on the mesh surface (radius 119.7) at the decoded (s, s, p) scale of their root node; the cities are the OpenTPW server's own list, since the original list is lost. | The original puts the pin's root node at the city record's position vector (0x5DC3E0 → 0x5D8190), which the city list from weacityr.dll fills; its radius is not decoded (a city list from the original service or a capture of its globe). |
| `online-publish-globe-prompt` | `APPROX:ONLINE-017` | The publish globe shows UITEXT 419 ("Choose the city to publish in") in the city name label while no city is hovered. | Its panel is read (tp.exe 0x5DD910 → 0x4C4FB0(1): the worldlob table without the satellites, Find Parks and back, with the city labels); neither tp.exe nor the Mac publish globe code loads UITEXT 419 as an immediate, so where it showed is unknown. |
| `online-chat-park-view` | `APPROX:ONLINE-019` | A room without a park to visit (a satellite on an older server, a published park's chat from its details) is a flat overhead view in which each player is a marker that the server's relayed movement moves; the player walks their own marker by clicking (one walk speed; the double-click teleport is not separated). A satellite's park is visited in three dimensions ([Chat parks](#chat-parks-the-satellites)). | The original's chat rooms were parks: a satellite's park was visited like any park (tp.exe 0x4C0880 → 0x5B8910, the chat mode 0x4BF460), and a park's room was entered by visiting the park. |
| `online-park-details-players` | `APPROX:ONLINE-020` | The park details' eighth label row shows the players currently in the park's chat room (UITEXT 273). | The original's last row showed the Instant action mode flag (UITEXT 282), which the package format does not record. |
| `online-postcard-photo` | `APPROX:ONLINE-022` | The postcard photo attached by Send Postcard is the park's generated map thumbnail, and no park reference is attached (the live level has no package identity). | The original attached a camcorder snapshot of the visited park (manual pp. 51/67); its content and encoding are not decoded. |
| `online-park-name-masking` | `APPROX:ONLINE-023` | Park names are masked with the word filter once, on the server at publish, and the stored masked name is what everyone sees. | The original masked at display on the client: the city lobby's park label (tp.exe 0x4B8850), the park details (0x4C0AE0), Find Parks (0x4BFC70) and the other-parks chart (0x4C1E40) pass the name through the filter 0x5D3230 while the filter flag (0x5B09B0) is set, with the local swears.txt/alloweds.txt (0x5AFD70, 0x5D3140). Still unknown: what sets the filter flag (the matching rules are under `online-word-filter`). |
| `online-buddy-enters-text` | `APPROX:ONLINE-024` | A buddy entering a park is reported as "Your buddy X has entered Y", OpenTPW's own text. | The original notification's string is not recovered; CHAT_COMMANDS 111-113 ("Your buddy ") have empty English suffixes and no enter-the-park variant. |
| `online-visit-count-guard` | `APPROX:ONLINE-076` | An owner's visits to their own park never count, and one visitor counts at most once per UTC day per park, so Platinum Tickets cannot be farmed. | Every visit earned the owner Platinum Tickets (FAQ, S2); the original service's guard against repeated or self visits is not recovered. |

## Retired chat action bar

`online-chat-action-bar` (former `APPROX:ONLINE-021`) is retired: the chat overlay now follows the
original's (tp.exe 0x4BF460 and its handlers, [The chat overlay](#the-chat-overlay)), with the
player buttons' own actions (postcard 0x4A9380, vote, earmuffs) and the name popups in place of
OpenTPW's top action bar.

## Retired ride-camera entry

`APPROX:ONLINE-073` is retired: the separate visit-camera view now uses the shared
camera approximation UI-094, with UI-095 covering restoration.
The original view details remain unresolved; sharing the implementation does not
establish original runtime parity.
