# Publishing and visiting parks online: binary evidence

October 11, 2026. What the original client did when a player published a park and when another player
visited it, traced statically in the Feral Mac application `mac-feral/bin/SimThemePark.data`
(SHA-256 `04809cd4…295f5`, see [PPC-ui.md](PPC-ui.md)) and its online library
`mac-feral/bin/libraries/online_shared.data` (SHA-256 `05c21d2e…9b2cb`, PEF, exports listed below).
The Windows v2.0 `tp.exe` cited in earlier lanes (decrypted, SHA-256 `5472f97b…1b805`) was not available
this round; the Mac build carries the same online client code (its imports from `online shared` are the
Windows DLL's exports by name), so Mac addresses are cited here. Outside facts (archived official site,
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
- The park's own menu is the online one: the online game type's pause menu at 0x10197D90 offers only
  Resume Game, Exit To Lobby and Quit Game (already traced, ParkHud).
- The chat overlay offers the vote for the visited park (manual's Chat screen: "Click this to cast your
  vote for the park that you're currently visiting"): `Vote::SendVote` is called from the in-park online
  UI (sub_11AE290, which also routes `Chat::Say` and the other chat actions) — voting is a separate
  service connection from the city server, and the vote button exists only inside the visited park.
- Postcards can be sent "from the park you are visiting" (manual p. 51).

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
([APPROX:ONLINE-018]).

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
  Totem on its modelled motion), inspects objects (published open state, price, state of repair) and
  the park's published statistics read-only, and votes through the existing vote endpoint. Chat and
  other players in the park are not implemented (the chat-park lane).
