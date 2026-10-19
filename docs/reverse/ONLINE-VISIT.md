# Publishing and visiting parks online: binary evidence

October 11, 2026. What the original client did when a player published a park and when another player
visited it, traced statically in the Feral Mac application `mac-feral/bin/SimThemePark.data`
(SHA-256 `04809cd4…295f5`, see [PPC-ui.md](PPC-ui.md)) and its online library
`mac-feral/bin/libraries/online_shared.data` (SHA-256 `05c21d2e…9b2cb`, PEF, exports listed below).
The Windows v2.0 `tp.exe` cited in earlier lanes (decrypted, SHA-256 `5472f97b…1b805`) was not available
this round. The Mac build imports an online client API with names corresponding to the Windows DLL's
exports; that does not establish identical implementation. The addresses below support the Mac build
only. Outside facts (archived official site,
official FAQ, FreeSO threads) are in [ONLINE-SOURCES.md](ONLINE-SOURCES.md); where both exist, the trace
wins. No original executable was run; disassembly stayed outside Git.

## The online library's client API

`online_shared.data` exports the whole online client in five classes, each with `Connect`, `Release`,
`SetSessionInfo` and a callback. C++ mangled names as exported (section 1 = the instantiated data
section, the value is the transition-vector offset; the code target is the tvec relocation's addend):

| Export | Code | Meaning |
| --- | --- | --- |
| `Auth::Login`, `Auth::Connect` | code:0x… (tvec 1:0x1E0…) | account login |
| `News::GetGameNews`, `News::GetServerNews`, `News::GetGameAds`, `News::GetGameServerDetails` | 1:0x560… | the news service |
| `City::Connect` | code:0x9078 | the city server (lists, visit marks, unpublish) |
| `City::GetCityList` / `CheckCityList`, `GetParkInCityList`, `GetParkInCityListTop10`, `GetParkInWorldByName`, `GetParkInWorldByID`, `GetParkByID`, `GetParkByGUID`, `GetThemeList`, `GetLogicalServerList`, `GetRideList` | code:0xACAC… | the list queries (TPW-SE's ThemeInfo/ParkInfo/Top10/SearchResult packets, S5) |
| `City::MarkParkVisited(void*, int, int)` | code:0xAE34 | **the visit report** |
| `City::UnpublishPark(void*, int, int)` | code:0xADEC | removing a published park |
| `Upload::Connect`, `Upload::UploadParkRequest(char*, ccParkFileInfo*, void*)` | code:0xBBEC | publish: park file plus metadata |
| `Upload::UploadParkFile(char*, void*)` | code:0xBC50 | the file transfer after the granted request |
| `Vote::SendVote(long, void*, long, void*)` | code:0xC494 | the vote, a separate service from the city server |
| `Chat::Say/Tell/Shout/…`, `Chat::MovePlayer(int, int, int)` | 1:0x260… | the chat park (movement is relayed; BOSS_CHAT_MOVE below) |

`MarkParkVisited` and `UnpublishPark` are thin guards (connection fields +0/+4 non-null) over the shared
query builder at code:0x8F50, which composes a fixed-size (0xD0-byte) message with an id byte 0x65, fields
from the session (word +0x60, word +0x64, halfword +0x68, 20 bytes from +0x6A) and the arguments, and
sends it through the connection at +0x30. The exact packet layout stays unread; the behaviour is what
OpenTPW follows.

## What a publish sent (app code:0x130620)

The publish flow (called once the park is saved for upload) builds a `ccParkFileInfo` on the stack —
the metadata the request carries — and hands it to `Upload::UploadParkRequest` (call at 0x11307F0):

| ccParkFileInfo field | Offset | Source in the game object |
| --- | --- | --- |
| word | +0 | a park/world id from game +0x344 |
| byte | +4 | a lookup by name (sub_133FC8: a case-insensitive list walk, default 1); the manual's park info shows an Instant Action / Full Simulation flag, but this byte is not verified to be it |
| byte | +5 | game +0x370 (a setting byte; the e-mail visibility or language choice is unidentified) |
| wide string, 20 chars | +6 | the park name (wcscpy from game +0x350) |
| wide string, 256 chars | +0x2E | the park description (from game +0x364) |
| wide string, 32 chars | +0x22E | the author: 32 wide chars copied from game +0x120 |

The park **file path** goes as the first argument (game +0x338): the request names the file, and
`Upload::UploadParkFile` then transfers it (app 0x1130868 drives the states: request granted →
"upload request granted - uploading…" → "file transfer ok"). A child lock forbids publishing at all
("ONLINE UPLOAD: childlock forbids publishing"). The uploaded park file is the park's save — the same
whole-park state a visitor needs to walk it — not a screenshot or summary: the visit flow below loads
the downloaded file as a level.

## What a visit did (app code:0x131370 and 0x1131C7C)

Entering a published park (Online Park Info's "Click to enter this park"; Online Lobby's "Enter Park"):

1. Connects to the city server ("ONLINE CITY: About to connect" → "Connected").
2. Downloads the park over FTP ("ONLINE FTPDOWNLOAD: About to connect", user `anonymous`; host strings
   `ftp.ea-europe.com` at 0x1DA413/0x1DB098): "Requesting park", then "Requesting next ride" for
   downloadable rides, "Download complete", "All downloads complete - disconnecting". A child lock can
   forbid downloading a park.
3. Sends the **visit report**: `City::SetSessionInfo` then `City::MarkParkVisited(conn, 0, key, id)`
   at 0x1131AB0 (and identically on the follow-up path at 0x1131E10) — once per park entry, before the
   park loads. This is the message by which the server learned of the visit that earned the owner
   Platinum Tickets (official FAQ, ONLINE-SOURCES.md S2).
4. Loads the park with the online level name (sub_110474C): the literals `data:levels:online_Standard.sam`
   and `online_Standard.sam` (0x1DF597) beside the own park's `data:levels:Standard.sam` — only the Standard
   variant is present as an online literal; no `online_Easy` string exists in this build — and switches to
   chat mode (manual p. 65: "Once you've entered the park, you will automatically switch to Chat Mode").

Inside the visited park (trace strings and classes):

- Other players are persons of the online model: `MODEL_ONLINE_PERSON` (sub_0FA9B0:
  "We probably shouldn't be encountering MODEL_ONLINE_PERSONs in the normal world tick"), and they are
  not saved ("Should not be able to save online persons", sub_105D3C). The server caps attendance
  ("Capping the number of people in online park", sub_0CB4E8; "Denied because the park is full").
- Movement is relayed by the chat server (`Chat::MovePlayer(int, int, int)`; the app decodes
  `BOSS_CHAT_MOVE` with an x/y/z the app validates: "Unrecognised Z value in online BOSS_CHAT_MOVE").
  The PC executable's handler (tp.exe 0x4E7265 in the message dispatcher 0x4E6E80) finds the online
  person by name (0x4F7D70 compares the name at +0x19C; the player's own person is skipped), takes x
  and y as 8.8 cell positions, and reads z as a mode: 0 walks the person there (0x4F7DE0 → 0x4FA5F0,
  which stores the target and plans the route through 0x510100 → 0x50F8E0; animation state 1), 1
  teleports it ("Pos for teleport", 0x4F7E20: walking stops, animation state 3, 0x4FA930 sets the
  place), 2 hides it (0x4F9ED0) and 3 unhides and teleports it ("Pos before unhide", "Pos for
  unhide", "Pos after unhide & teleport"). So every client walked a relayed person itself; the
  route search 0x511420 goes cell by cell with the octile distance (|dx| + |dy| − min / 2, at most
  0xFC steps) and the person moves straight to the point when it finds none (0x70000000).
- Every world turn the person tick's case for a thing of type 0x12 (0x50B453) runs, in the online game
  type ([0xFB3B7C] = 1), 0x4F7C60 and the person's walk state machine 0x4F7C90 (arrival → animation
  state 3; "The person has become stuck on their way to the destination" → state 3, animation 5).
  0x4F7C60 starts particle effect 0x35 (*Avatar*) for the player's own person (+0x3A0 set) and 0x39
  (*OtherAvatar*) for any other through 0x4FA470, at the person's position (0x4754E0) moved by the two
  world offsets 0x519410 (`DAT_00785584`) and 0x519420 (`DAT_00785588`), each times 10, on the x and z
  axes, at the terrain height there (0x4527F0), times 1024 (the pool's create 0x521E60). Both `Tp2.plb` templates live 7 ticks with particles of about 30, so each
  person sparkles continuously. Outside the online game type the tick logs the
  `MODEL_ONLINE_PERSON` warning instead.
- The park's own menu is the online one: the online game type's pause menu at 0x10197D90 offers only
  Resume Game, Exit To Lobby and Quit Game (already traced, ParkHud).
- The chat overlay offers the vote for the visited park (manual's Chat screen: "Click this to cast your
  vote for the park that you're currently visiting"): `Vote::SendVote` is called from the in-park online
  UI (sub_11AE290, which also routes `Chat::Say` and the other chat actions) — voting is a separate
  service connection from the city server, and the vote button exists only inside the visited park.
- Postcards can be sent "from the park you are visiting" (manual p. 51).

## Chat mode in the visited park (tp.exe)

Entering a park online builds its chat mode (0x4BF460) instead of the park's HUD: the HUD object
(0x7CB2AC) does not exist then, and the routines that reach the HUD (0x4814A0, 0x4862A0) check for
it and turn to the chat-mode windows instead.

- **The overlay** (0x7CC8BC) is table 0x758AE8 with the handler 0x4BD4A0: the chat frame `f_chat`
  (panel 0xC3123, which takes the name popups' handler 0x4BE290), the people list 0xC3124 (row
  template 0x4BF740, its text in font slot 7 as high as the font plus 4 units, 0x4BF760; colours
  0x4BE730), its tab group 0xC3125 starting on the people tab 0xC3126, the postcard 0xC312A, vote
  0xC3129 and earmuffs 0xC3128 buttons (handler 0x4BE820), the message list 0xC312B (row template
  0x4BF8B0, colours 0x4BE7C0) and the entry 0xC312C (key handler 0x4BE3A0, font slot 7, at most 255
  characters). A bare full-screen panel (0x7CC8C0, 0x65DD7B over 0-2047 × 0-1535) stands in for
  the overlay while it is off; both take the park's clicks through 0x4BD4A0.
- **Keys** (0x4BE3A0, the entry's key message 0x1000B): Enter says the line (0x4EA490), Tab takes
  the focus off the entry, F4 (0x73) switches the overlay and the bare panel (flag 0x7CC8B4);
  other keys go to the game's key table (0x40C870).
- **The people list**: the people tab lists every person in the park (0x4EE8D0), the buddy tab
  every buddy (0x4EE9E0), each name with its flags (0x4EE5C0): bit 0 a buddy, bit 1 the buddy is
  online, bit 2 the buddy is in this park, bit 3 the player ignores the name. A non-buddy is white
  (0x758F30), a buddy red while offline (0x758F3C), blue online elsewhere (0x758F34) and green in
  this park (0x758F38). Single rows are added, removed and re-flagged as the server reports
  (0x4BEB60-0x4BED00).
- **Name popups**: a right click on a row (0x402, 0x4BE290) opens nothing for the player's own
  row (0x4EA9A0), the six-button popup 0x758CD8 for a buddy and the four-button popup 0x758E18 for
  anyone else, placed at the pointer (0x4BF370) with the ignore toggle set from bit 3. Their buttons
  (0x4BEDE0) black-mark, go to, ignore or stop, add or remove the buddy, locate and page.
- **Player buttons** (0x4BE820): earmuffs on or off by the toggle (0x4EBEE0 / 0x4EBEF0), the vote
  (0x4EB300, then the button disables itself), and the postcard (0x4A9380). The server's answers
  set the earmuffs toggle (0x4BD400) and the vote button's state (0x4BD450).
- **Messages** (0x4BE970): a line is cut where its characters' widths reach the list column's
  width and each piece is a row; the list keeps 20 rows (0x758F0C) and shows its last. A row's
  colour is its kind's from 0x758F10 (0x4BE7C0); the kinds come with the lines (0x4EA890).
- **The park** (0x4BD4A0): over the ground the panel shows help 401 ("Left-click to walk to this
  point") and a click walks; over a rideable ride (thing type 3) it shows help 580 with the ride's
  name and a click runs 0x4E15B0. While a ride is ridden both windows hide and the person's place
  is kept (0x4BD090 / 0x4BD260).

## Riding in a visited park (tp.exe)

A visitor rides from the chat mode or from the first-person walk, and the chat server hears of it
through the movement's mode (`Chat::MovePlayer(x, y, z)`, imported from weachatr.dll at IAT 0x6FD4CC;
a byte scan finds ten call sites, each pushing its mode as a constant).

- **The click** (0x4BD7F4 in the panel handler 0x4BD4A0): over a thing of type 3 that is no add-on
  (AddOn.UpgradesId, definition +0x284, 0) and of interface type 0 (Info.WhichUIType, +0x4AC), or 3
  with the thing's choosable bit (+0x32 bit 0x10), the panel shows help 580 ("<ride>: Left-click to
  ride", the ride's name in its parameter) and a click runs the ride window's Ride it! (0x4E15B0):
  the camera takes the coaster, the track ride or the ride model as its source, the model's first
  camera dummy (0x42A560, 0x44B220) and camera state 4 (0x42AB20). The camera-mode object stays
  CMMOnline; a second call while riding leaves the view.
- **Entering any first-person view** from the overview runs the chat hook 0x4BD090 (through
  0x42AB20 → 0x481470 → 0x4A2AC0, only in the online game type): it hides both chat windows,
  remembers the active one, hides the own person locally (0x4F9ED0), sends a teleport (mode 1) to
  the ground point under the cursor (0x4EA240) and, for a ride, a hide (mode 2) at the same point
  (0x4EA3F0). The first-person walk then sends a walk (mode 0) every tick its 8.8 place changes
  (0x4EA370); stepping onto a ride's entrance from the walk (flag 0x400) sends a hide.
- **Leaving**: 0x42A190 ends the view (Escape, the HUD's exit button, a right click with RMB cancel,
  Ride it! again, the ride's removal, a load), not the ride's cycle. A ride entered from the walk ends
  at the ride's exit cell (0x42A1D0, the thing's +0x38; +0x36 is the entrance), and the next tick
  sends unhide-and-teleport (mode 3) there (0x4EA410). The return to the overview (0x4BD260) shows the
  remembered window and the own person again (0x4F9E60) and sends only a teleport (mode 1) to the
  camera's ground focus (0x4EA2D0).
- **The others' view** (dispatcher case 0x22, 0x4E7265): mode 2 destroys the person's model (+0xC)
  and its bubble (+0xB4) and leaves its walk and route running unseen; modes 0 and 1 still move a
  hidden person; only mode 3 builds the model again and teleports it to the place, standing (walk
  state 0, animation 3). Hidden has no flag of its own: it is the model handle 0, which the tick
  (0x50B453 → 0x4FA870) and the renderer skip.

So statically a ride boarded from the overview is never followed by mode 3, and the other visitors
keep that player hidden afterwards, until a later walk-in ride's mode 3. Whether the original server
repaired that (re-sending the players to a client) is not known. OpenTPW hides a riding player for
the room and always shows them again when the ride ends (`EXT:online-ride-hiding`, docs/ONLINE.md).

## Chat satellites (tp.exe)

The globe's five satellites are parks, not rooms of their own.

- **The list**: every City connection also asks for the parks of city 1 (0x5B7610: the list's
  request for city 1, which ends in `GetParkInCityList(0, 1, lastCRC, 0)` through 0x5BCB30; the
  progress text is UITEXT 252 "Getting Chat Satellite List", the log line "ONLINE CITY: Chat
  Satellites parks data downloaded ok"). Its entries are full park records like any park list's
  (0x5BDE30: id, name, description, author, city, theme, visits, votes, the ride bitmap, file name,
  e-mail, date, chart position and the instant-action flag); satellite *i* shows entry *i* in the
  server's order, and only the first five are used.
- **The counts**: a connection of their own ("ONLINE CHATELLITES", 0x4C4BB0) sends `RoomInfo` for
  each park's room, "%d" of the park id; an answer 7 is matched to its satellite by park id
  (0x4C4920), 0x2C counts 0, and the counts are written only after exactly five answers.
- **The click** (0x4C56E0; the panel builder 0x4C4FB0 gives the satellite controls 0x16008-0x1600C
  this handler and their index): with the city, theme, logical-server and satellite lists loaded
  it calls 0x5B7910(i - 1), which reads entry *i* of the list without a request, hides the globe's
  panels and opens the park details 0x4C0AE0 for it, with the lobby lists' success callback 0x4C0830;
  their cancel (0x4C4FA0) brings the globe back.
- **The details**: 0x4C0AE0 (table 0x759170) looks up the park's city (0x4C0C06, 0x5BC9C0). With that
  city's flag +0x60 set it fills only the first row (0xE5937) with "%s : %s" (0x7593D4) of UITEXT 271
  ("Theme") and the theme's name (0x5BD270), besides the park's name as the title (0xE5931) and its
  description (0xE5930), and it disables the owner action (0xE592F); the statistics rows get no text
  and no player count is shown. Whether city 1 had the flag is not known. UITEXT 284-291 ("Chat Park
  Details", Name, Theme, Players at the park, Creator's name, Creator's email, Date published,
  Description) are not used as texts, but for "Creator's name" (game id 0x121) in the postcard
  recipient list of an online park whose creator has an e-mail (0x4A7B7C).
- **Enter** (0xE592E, 0x4C0880 → 0x5B86A0, which asks for a login first when needed, → 0x5B8910) is a
  visit like any: the child lock and the ride bitmap are checked, missing files are downloaded over
  FTP and `MarkParkVisited` is sent; then 0x4C0830 sets game type 1, the level loader (0x5156A0)
  loads `online_Standard.sam` for the park's theme (record +0x4C) with the downloaded save, and every
  game of type 1 enters the chat mode (0x54ED1E → 0x48A410 → 0x4BF460).
- **The room**: after "Created a player, let's join a park." the answer handler 0x4E6790 sends the
  only `CreateRoom` (0x4E6A67): the park's name, an empty wide string, the city's name, "%d" of the
  park id and 0x100. A satellite's park, a visited park and a goto (0x4E96B7 → 0x5B87E0) all enter
  the park's own room. The only success answer is 0x36 ("The park has been created successfully"),
  so the server must have given it for a room that already existed.
- **Entering**: 0x4E5EF0 places the player's own online person at the centre of entrance 0's cell
  (0x4D8690(0): FixedItemInfo.EntranceA, 0x7855BC/0x7855C0; most likely the level's entrance, read
  before the save is merged in) with the teleport 0x4F7E20, and sends `MovePlayer(x·256+128, y·256+128, 1)` — only when that cell's type byte is 0x12 (0x4E5FFE; otherwise neither the teleport nor the first move happens; OpenTPW always does both),
  then `NotAFK`, `SetHearingRange(0xA00)` and `EnumPlayers`. The relayed-move handler skips the
  player's own person (0x4E72CA), so the server never placed it.
- There is no "publish as chat park": the upload (0x5B7E88) carries city, theme, a setting byte,
  name, description and author, and the publish globe's city filter (0x5DDAC0) leaves out cities
  with the flag +0x60, which the world globe's (0x5DD810) ignores.

Not settled statically: which parks the service kept in city 1 and whether city 1 had the flag
(without city 1 in the city list the client would crash); what `CreateRoom`'s empty string and
0x100 mean; the server's answer for a room that already exists; what the park record's flag at
+0x40 means (it selects child-lock bit 4 or bit 8). OpenTPW's chat parks follow the click, the
details and the visit with the server's own list (docs/ONLINE.md, "Chat parks (the satellites)").

## Ride cameras and riding

The original's manual sells the feature directly: "When you need a break, you can ride any ride in the
park" and, in the feature list, "Ride any ride and watch any sideshow"; visitors of published parks
could "walk round other players' parks, ride their rides and vote" (FreeSO thread 597, S3). The Mac
application names the camera-mode classes that implement riding (RTTI class names at 0x1C8625 area,
vtables in the data section):

| Camera mode | Vtable | Role |
| --- | --- | --- |
| `CMMDefault` | data:0x3D8F8 | the own park's normal camera |
| `CMMOnline` | data:0x3D7F0 | the camera in an online park (the visited/chat park; entering logs "Assigning CMMOnline") |
| `CMMCoaster` | data:0x3D7A4 | riding a coaster |
| `CMMFirstPersonTarget` | data:0x3D758 | the first-person view of a target (a ride or a visitor) |
| `CMMPickUpStaff`, `CMMPlaceStaff` | — | staff handling (not online) |

`CMMOnline` shares most slots with the default mode (the shared slots point at the same functions), so
the online camera is the normal park camera with online additions, not a restricted spectator view; the
first-person and coaster modes are separate states of the same machine. That riding was possible while
online is also implied by the display setting `FIRSTPERSONVIEWDISTANCE` (options 0..3, COMPATIBILITY.md)
and by the online park being a normal park load (the `online_Standard` level with the downloaded save,
whose rides run their scripts). The Update bodies of the two ride-camera classes are not decoded; the
seat position, view direction and entry/exit input stay approximations in OpenTPW
([APPROX:UI-094], with restoration qualified by UI-095).

## What the visit did *not* include

No building, selling or saving inside another player's park is traced — the client code paths above
download, mark, load, chat, move, vote and send postcards only, and the park is the owner's save. (The
absence is an observation of the traced flows, not proof: nothing here shows a build attempt being
*rejected*.)

## How OpenTPW follows this (2026-10-11)

- **Publish** (the `.tpwpark` package): the complete built park — cells, objects with their published
  state, coaster tracks, statistics — plus the manifest's park name, description, author and created
  date/time, matching the traced `ccParkFileInfo` fields (name, description, author) and the website's
  per-park publish date/time, visitor count and vote count (S1).
- **Visit report**: `POST /api/v1/parks/{id}/visits` once per visit (the client sends it with the
  download it enters the park from), counted server-side per report — the counterpart of
  `City::MarkParkVisited`. A ticket award can hook the accepted report (docs/SERVER.md).
- **Visit**: the visitor's client rebuilds the exact published layout (digest-checked), walks it with
  the normal park camera, can ride rides (coaster trains along the published track, the prototype
  Totem on its modelled motion and, since the ride view port of October 15, every ride whose model has
  a camera seat), inspects objects (published open state, price, state of repair) and
  the park's published statistics read-only, and votes through the existing vote endpoint. The
  park's other visitors appear as figures that walk and teleport, with the original's avatar
  sparkle, and the park's room chats (docs/ONLINE.md, "Players in a visited park", October 18).
  A click on a ride in the chat mode rides it, and a riding player's figure is hidden for the
  others until it comes out at the ride's exit (protocol 5, October 18).

## Verification of the shared-park lane

The finishing pass on October 11 ran the online, shared-layout and ride-camera tests with the local
original PC installation: **71 passed, none skipped or failed**. This includes version 1 compatibility,
malformed payload rejection, server-side validation, repeated/self-visit counting, the CPU coaster
rebuild, and a camera regression proving that translating a ride leaves its viewing direction unchanged.
The camera's rotation consumes `look - eye`; the look target is a world position, not a direction.
Object footprint tests use the local catalog shape without allocating GPU resources. Actual rendered
object placement is checked separately by the native visit test.

A fresh offline Jungle export was loaded in a native read-only visit: **78 cells, 14 objects, no
coasters**, layout digest `5381d1ab76f0f12b` matched the rebuilt world, eight rendered frames and a
1280 × 720 GPU capture. Build, remove, save, load and re-export were refused. This shipped fixture has
no completed coaster or prototype ride, so that run did **not** exercise the native ride-camera step;
the original-data CPU test exercises the front-car camera moving with a running coaster.

The native front-end smoke passed **514 frames** with the online pause-menu expectation and read-only
object guards. It sold the bought Totem for **$1,625**; the earlier reported zero-refund failure did not
reproduce in this run. Both native runs used `--mute --no-advisor`, so they do not verify audio or automatic
advice. The full suite without original assets passed **1,317 tests, 312 skipped, none failed**; the
complete original-asset suite was not rerun in this finishing pass. The Web build and the fidelity-register
check passed. The evidence runner passed 20 Python
suites (1,267 tests, 361 skipped); unavailable private binaries and optional .NET evidence projects
remain unverified by that run. No claim of PC binary equivalence follows from these tests.
