# The original online service: feature inventory

Every online feature of the original Theme Park World / Sim Theme Park online service that the
available evidence names, and the state of its OpenTPW counterpart. Written for
[ONLINE-SOURCES.md](ONLINE-SOURCES.md)'s outside sources: where a fact can be traced in the game
files, the trace is the evidence and wins. "Done" means implemented from original evidence;
"partial" means implemented with registered approximations; where a "Done" online feature differs
from an original that is not fully recovered, the row names the OpenTPW design choice
([EXT:online-*](../ONLINE.md#online-design-choices), which keeps the known original evidence);
"missing" means not built (with the reason); "extension" marks OpenTPW features with no original
counterpart ([EXT:ONLINE-*](../ONLINE.md)).

Evidence keys: **BIN** — static analysis of the Windows v2.0 `tp.exe` (0x00… addresses) or the Feral
Mac PowerPC binary (`STP-PPC:0x…`, load base 0x10000000 removed); **DATA** — decoded game data
(Mac UI layout tables `data:0x…`, string tables `Language/English/*.str`); **MAN** — the Sim Theme
Park manual (page numbers); **S1–S5** — the outside sources of ONLINE-SOURCES.md (S1 archived
ThemeParkWorld.com, S2 the official online FAQ, S3 FreeSO thread 597, S4 FreeSO thread 6610,
S5 the TPW-SE roadmap).

## Connection and accounts

| Feature | Evidence | OpenTPW status |
| --- | --- | --- |
| Client connects to EA's service (`daphne.eagames.co.uk` in `online.sam`; an `onlineoverride.sam` exists; online files kept in `save\online`) | S3 | Done differently by design: the player's own server over OpenTPW's HTTP/WebSocket API [EXT:ONLINE-040]; the original protocol is unknown and not reproduced |
| Four service parts: login server (password check, error codes), news server (Game/System News), city server, chat server; city packets named ThemeInfo, LogicalServerInfo, RideInfo, ParkInfo, CityInfo, SearchResult, Top10 | S5 | Done as one server with the same information (the packet names are evidence for which data existed; OpenTPW's API names stay) |
| Connection progress messages: "Connecting…", "Getting Server Details", "Getting Game/System News", "Getting City List", "Getting Top 10/All/Player's Park List", "Getting Chat Satellite List" | DATA UITEXT 231, 243-252 | Done — each fetch shows its original message |
| Login dialog: name and password, 16 characters each, labels inside the fields, Enter name→password→submit | DATA Mac UI table data:0x505f4 (window 500,233-1516,830), STP-PPC 0x10184924; UITEXT 235-237 | Done — the original composition, decoded from the layout table |
| Registration on the website, with a security question and its answer for identity checks | S1, S2 | Done differently: in-game registration; two-step login with an authenticator app replaces the security question (stronger, [EXT:online-two-step]); differences documented in docs/ONLINE.md |
| E-mail address shown with published parks only if the player opted in, changeable under "change my details" | S1, S2 | Done more strictly: e-mail is never shown to other players; used only for account recovery ([EXT:online-email]) |
| Forgot password through the website's "MY DETAILS" | MAN p. 61 | Done differently: a code by e-mail sets a new password (docs/ONLINE.md, "Account recovery") |
| "Game Mode" choice (Instant Action / Full Simulation) on the online forms | DATA UITEXT 239-241 | Missing: OpenTPW has one simulation mode; the choice has nothing to drive |
| Delete account, sessions, moderation lists, bans | — (OpenTPW requirement) | Extension [EXT:ONLINE-055] |

## News

| Feature | Evidence | OpenTPW status |
| --- | --- | --- |
| News panel titled "News from ThemeParkWorld.com" with Game News and System News tabs, scrollable body | DATA data:0x50738; UITEXT 266-268; STP-PPC 0x1018539c | Done — original composition, title restored |
| News readable before logging in | BIN globe flow (news fetched on entering the online world) | Done (`GET /api/v1/news`, public) |

## The online world (globe)

| Feature | Evidence | OpenTPW status |
| --- | --- | --- |
| The globe fills the screen; camera circles it, zoom toggles two radii, arrows/edges/right button turn it with damping | BIN tp.exe 0x005DC860/0x005DCFE0; DATA data:0x523f0 (worldlob panel) | Done (start view, steering mode and pitch limit as tp.exe; per-frame timing [EXT:online-globe-tick], steering increments [EXT:online-globe-steering], move history [EXT:online-globe-edge-gate]) |
| Cities as pins, taller with more parks; Bullfrog pin; hover shows name and "Parks: n" | BIN tp.exe 0x005DC3E0; UITEXT 461; S4 | Done; city list [EXT:ONLINE-059] |
| Click a city → its lobby (sets the selected city for the top-10 request) | BIN tp.exe 0x005DD530, 0x005E23F0 | Done |
| Five chat satellites around the globe, each showing the player count of its chat park; click to enter the park | DATA data:0x523f0 (i_sat 90120-90124); STP-PPC 0x1b8808 ("ONLINE CHATELLITES" connector, RoomInfo query, "%d" count); tp.exe 0x4C56E0, 0x5B7610, 0x4C4BB0 (ONLINE-VISIT.md, "Chat satellites"); MAN p. 63; S5 | Done — the server names the five chat parks, the globe shows each count centred in the satellite's text region (tp.exe 0x4C4FB0), and a click opens the chat park's details, from which Enter visits its park [EXT:online-chat-park-scene] |
| Publish globe: "Choose the city to publish in" over the globe; click a city opens the publish form | DATA UITEXT 419; STP-PPC 0x1018CE80 | Done (panel read: tp.exe 0x5DD910; [EXT:online-publish-globe-prompt] where UITEXT 419 showed) |
| Back to your own lobby from the online world | DATA data:0x523f0 b_bck2lob, help 431 | Done |

## City lobby

| Feature | Evidence | OpenTPW status |
| --- | --- | --- |
| The city's top 10 parks as lobby islands (one per theme at slot positions), ranked | BIN tp.exe 0x005E23C0 → 0x005E27B0 → 0x005E3210; DATA data:0x50172 (citylob panel) | Done (each island is its park's theme's island at its list position: 0x005E3210's slot argument is the running counter) |
| Selected island's rank at the top, name at the bottom (item detail overlay) | STP-PPC 0x101834A0 labels 0x49D9/0x49DA | Done |
| "Other Parks In <city>": the chart of the parks outside the top 10, five columns | DATA data:0x5137e; UITEXT 446, 292-297 | Done — original composition and columns |
| Enter this park (download and visit); "Entering…" | DATA data:0x50172 b_entpark; UITEXT 463 | Done (read-only visit) |
| Other players in a visited park: online persons walking and teleporting, each with its avatar sparkle | tp.exe 0x50B453 (online person tick), 0x4F7C60/0x4FA470 (effects 0x35/0x39), 0x4E7265 (`BOSS_CHAT_MOVE` modes 0-3); MAN pp. 65, 68 | Done — the visit joins the park's room; figures with names, routes and the original sparkle; places relayed as fractions of the park [EXT:online-visit-presence]; a riding player's figure is hidden and shown again at the ride's exit (modes 2 and 3, protocol 5) [EXT:online-ride-hiding] |
| A visit's chat mode: the chat overlay in place of the HUD, F4 switching it off and on, a park click walking | tp.exe 0x4BF460 (chat mode windows), 0x4862A0 (no HUD in chat mode), 0x4BE3A0 (F4), 0x4BD4A0 (park click, help 401); MAN p. 65 | Done — F4's other side is the visit's HUD [EXT:online-visit-chat-mode]; a rideable ride's hover (help 580 with its name) and click (0x4BD7F4 → 0x4E15B0) ride it [EXT:online-chat-ride-click] |
| Previous/next island | DATA data:0x50172 b_lobleft/right | Done |
| "Getting Ride List" / "Getting Theme List" / "Getting Logical Server List" | DATA UITEXT 253-255 | Missing: the visit runtime builds its content from the package (docs/ONLINE.md); the lists themselves are not decoded |

## Find Parks, park details, publishing

| Feature | Evidence | OpenTPW status |
| --- | --- | --- |
| Find Parks: six-column results (Name/Theme/City/Position/Visits/Votes), search by user name, "No search results" | DATA data:0x50f84 (columns, search field 0xe05a, search button 0xe059); UITEXT 298-306; MAN p. 64; S5 | Done — original composition and columns; selecting a row opens the details dialog (notify 0x400 → STP-PPC 0x1018b474) |
| Park details: name, created by, date published, theme, e-mail, chart position, number of votes, number of visits, description, enter-park, owner-only unpublish | DATA data:0x51150 (eight label rows 0xe5933-0xe593a); UITEXT 269-283; MAN p. 65; S5 | Done — original composition; e-mail shows "None supplied" (UITEXT 283) because OpenTPW never publishes addresses; the eighth row shows the players at the park instead of the Instant action mode flag [EXT:online-park-details-players] |
| A player may publish at most **ten** parks at once; the limit message tells them to un-publish first | S2 (FAQ); DATA UITEXT 221 | Done — the default limit is ten and the server answers with UITEXT 221's wording |
| Publishing goes into a chosen city | DATA UITEXT 419; S5 | Done |
| Un-publishing is separate from publishing (a publish removes nothing) | S5 | Done |
| Every **visit** earns the park's owner **Platinum Tickets**, voting or not; tickets bought ride downloads | S2; MAN p. 62 | Done — the server counts one per recorded visit; Find Parks shows the player's total ([EXT:ONLINE-070]) |
| Top-parks ranking is by votes | S2 | Done — lists and charts order by votes (then visits, then the earlier published) |
| Swear words in park names shown as `****` | S2 | Done — the server masks names with its word filter, where the original client masked at display (tp.exe 0x5D3230 from the park-name labels) ([EXT:online-park-name-masking]; the list itself is the operator's `swears.txt`, [EXT:online-word-filter]) |
| Votes limited per day; one vote per park per day | DATA CHAT_COMMANDS 131 | Done (`MaximumVotesPerDay`, per-park per-day) |
| Visited-before / voted-for flags in the details | DATA UITEXT 280/281 | Done (in the park summary the list carries) |
| Parks built with cheats cannot be published | DATA UITEXT 417 | Missing: OpenTPW has no cheat detection to drive it |
| "PARK NOT PUBLISHED…" / "PARK PUBLISHED" / "MISSING RIDES" notices | DATA UITEXT 405/410-412/417 | Done (405, 410; the missing-rides variants depend on the visit runtime's content check) |

## Chat parks and chat

| Feature | Evidence | OpenTPW status |
| --- | --- | --- |
| Chat parks joinable from any menu (the satellites); players present together, moving around the park; the server relays movement; teleport | S5; MAN pp. 63, 67-68 ("Left click to walk to destination. Double click to teleport"); tp.exe 0x4C0880 (a satellite's park is visited like any park) | Done — a satellite's park is visited in three dimensions with the room's players as figures, walking and teleporting as in any visited park ([EXT:online-chat-park-scene] the park and the room); movement is relayed to the room with its own rate limit; a room without a park to visit is a flat overhead map of markers [EXT:online-chat-park-view] |
| Chat overlay: people list with its people and buddy tabs, the postcard, vote and earmuffs buttons under it, message list and text entry at the bottom | tp.exe table 0x758AE8, 0x4BF460, list handler 0x4BE820 (tabs, buttons); MAN pp. 67-70 | Done — original composition and actions; help ids by wording [EXT:online-chat-help] |
| Name colours in the people list (buddy here green, elsewhere blue, offline red, others white) | tp.exe 0x4BE730 (colours 0x758F30-0x758F3C), 0x4EE5C0 (flags) | Done — a buddy's online and in-park flags follow the roster and the buddy list [EXT:online-chat-person-flags] |
| Name popups on a right click: six buttons for a buddy, four for others, none for the player | tp.exe tables 0x758CD8 / 0x758E18, 0x4BE290 (0x402), 0x4BF370 (place), 0x4BEDE0 (buttons) | Done — placed at the pointer [EXT:online-chat-popup-place]; a goto in the visited park teleports the figure [EXT:online-chat-goto] |
| Say (default; "broadcast to people in your vicinity", shown "Dan: Hello") | MAN pp. 67-68; CHAT_COMMANDS words | Done — reaches the whole room; the hearing range has no positional effect [EXT:online-chat-command-semantics] |
| Tell (private), reply, last | CHAT_COMMANDS; MAN | Done |
| Shout / eshout; wshout / ushout to every park | CHAT_COMMANDS | Done |
| Emote, shown "Dan &lt;emote text&gt;"; emote-tell | MAN p. 68; S5 | Done |
| Earmuffs (stop hearing shouts); cannot shout with them on | CHAT_COMMANDS 95-97; MAN p. 67 | Done |
| Messages from "Server" (system text) | CHAT_COMMANDS 160; S5 | Done |
| Buddies: add/remove, list with online/offline | CHAT_COMMANDS 106-113; MAN pp. 69-70 | Done — toggle from the people list or `/buddy`; the buddy tab shows the last answer |
| Ignore | CHAT_COMMANDS 98-102; MAN p. 70 | Done |
| Blackmark a player (report to the account supervisor) | CHAT_COMMANDS 103-105; MAN p. 70 | Done as a moderation report [EXT:online-chat-command-semantics] |
| Be told when a buddy enters another park or yours | S5 | Done ([EXT:online-buddy-enters-text] the notification wording) |
| Locate a buddy: "Dan is currently in Halloween World in San Francisco" — park and city | MAN p. 70; CHAT_COMMANDS 169 | Done — the locate answer names the park and its city |
| Go to a buddy's park ("exits you from your current chat park") | MAN p. 70; CHAT_COMMANDS 126/127 | Done |
| Page a buddy in another park ("Dan is paging you", "Dan pages you: Hello") | MAN p. 70; S5; CHAT_COMMANDS 115 | Done — the page event reaches any room; the carried text is OpenTPW's line layout [EXT:ONLINE-064] |
| AFK with room announcement | CHAT_COMMANDS 121-124 | Done |
| Hearing range setting | CHAT_COMMANDS 117, weachatr.dll SetHearingRange | Done as an acknowledged no-op [EXT:ONLINE-053] |
| Mood emotes (cheer … confused) | CHAT_COMMANDS words 24-42, responses 134-151 | Done |
| Who (list the park's players) | CHAT_COMMANDS | Done; the roster also updates on every join and parting |
| Per-player word filter on/off | CHAT_COMMANDS 132/133 | Done |
| Help: the command list | CHAT_COMMANDS 120 | Done |
| Command words localized per language | DATA `CHAT_COMMANDS.str` 0-42 (+ English 43-85) | Done — the selected language's words are accepted |
| Chat lines drawn in per-command colours (white, green, …) | MAN p. 68; tp.exe 0x4BE7C0 (row colours 0x758F10 by kind), 0x4EA890 (kinds) | Missing: all lines use one colour ([EXT:ONLINE-064] formatting). Read so far: a message row takes the colour of its kind from 0x758F10 (0 green, 1 white, 2 blue, 3 red, 4 white, 5 blue, 6 red, 7 yellow (240,255,0)); the speech lines of CHAT_COMMANDS 88/89/92/93 have kinds 1/2/3/4 and every response kind 7; which speech each of 88/89/92/93 is (their English strings are empty) is not traced |
| Chat Park Details dialog (name, theme, players, creator's name and e-mail, date, description) | DATA UITEXT 284-291; tp.exe 0x4C0AE0 (the park details' short variant for a city with the flag +0x60) | Done as tp.exe shows a satellite's park: the park details' short variant with the name, "Theme : …" (UITEXT 271), the description and Enter [EXT:online-chat-park-details]; tp.exe uses UITEXT 284-291 nowhere as a dialog |

## Postcards

| Feature | Evidence | OpenTPW status |
| --- | --- | --- |
| Send postcards from the park you are visiting, with a photo | MAN pp. 51, 67 | Done — the card carries the park's map thumbnail [EXT:online-postcard-photo] |
| Outbox: four-column list (Title/Date/Send/Delete), per-card send/delete marks, mark-all/unmark-all | BIN tp.exe 0x758878 (window table), 0x4bc710 (builder; every row starts marked for sending, 0x4bcaec; the date is UITEXT 0x1c0, 0x4bca6a), 0x4bcbc0/0x4bcca0 (row cells; the marks are `i_boxtick` frames 0/1), 0x4bbc20 (handler: cell clicks 0x4bbf2c, bulk buttons 0x4bbd2d, tick 0x4bbcbd); DATA data:0x508bc (columns ids 16-19, buttons 0xee5-0xee7); STP-PPC 0x10185d5c | Done — tp.exe's window, rows and mark flow; the tick asks for OpenTPW's login where tp.exe checked the account's settings [EXT:ONLINE-UI]. Missing: the card's picture in its frame, the headers' sort (attribute 0x10, 0x665a44; its sort 0x6628f4 is not decoded) and the address text's font slot 6 and slider (0x4bc100) |
| "Sending mails from outbox, please wait …" | DATA UITEXT 313; BIN tp.exe 0x4bc500 (send), 0x4bc660 (its progress closes with no count) | Done — the globe's status line while the cards go out, cleared once they are sent |
| Reminder that postcards wait in the outbox when going online | DATA UITEXT 472 | Done — the online menu shows the reminder |
| Inbox (cards from other players) | MAN (e-mail postcards) | Done [EXT:ONLINE-UI] |
| Recipients are player names | MAN; CHAT_COMMANDS | Done |

## The website's lists

| Feature | Evidence | OpenTPW status |
| --- | --- | --- |
| My Theme Park: every published park of one player with park name, visitors, votes, publish date and time, current rank, highest rank (shareable per-player URL) | S1 | Done — `GET /api/v1/players/{name}`, public for the parks their author shows on the website, complete with a login |
| Top 10 parks (rank, park name, player) | S1 | Done — `GET /api/v1/charts/parks` |
| Top 10 players (rank, player) | S1 | Done — `GET /api/v1/charts/players` (by votes) |
| Richest players (rank, player, Platinum Tickets) | S1 | Done — `GET /api/v1/charts/richest` |
| Player search by user name / first name / last name, any combination; over 50 results asks for more detail | S1 | Done by user name with the 50 cap and a `more` flag; OpenTPW accounts have one player name, so first/last names do not exist |
| The website itself (opentpw.io) | S1 | Owned by another work (website/); the endpoints above are what it may read |

## What the traces add to the outside sources

- The satellites' player counts are written into the satellite controls themselves by the connector
  at STP-PPC 0x1b8808 (RoomInfo per park, "%d" per control 90120-90124) — the count, not just the
  satellite, is original (S5 named the rooms; the trace shows where the count went).
- The chat overlay's layout (data:0x50b2c, tp.exe 0x758AE8) confirms the manual's picture: people list
  left, message list and text entry bottom right; the six-button table data:0x50d1a (tp.exe 0x758CD8)
  is the buddy name popup, not a bar.
- UITEXT 284-291 names a "Chat Park Details" dialog the outside sources do not mention; tp.exe never
  shows it: a satellite opens its park's details in their short variant (tp.exe 0x4C0AE0,
  ONLINE-VISIT.md "Chat satellites").
- On the PC the satellites are the parks of a city of their own (city 1), visited like any park in
  the park's own room (tp.exe 0x5B7610, 0x4C56E0, 0x4E6A67).
- The original's service structure (S5) matches the client's own progress strings (UITEXT 243-255):
  news server, city server (city/theme/ride/logical-server lists, top-10, search), chat server.
