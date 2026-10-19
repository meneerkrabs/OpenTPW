## The globe's chat satellites are parks to walk in (October 19, 2026)

A click on one of the Online World's five chat satellites now opens its chat park's details, and **Enter** visits its park in three dimensions: the chat mode
with the room's players as figures that walk, teleport and ride, as in a visited published park. Before, a satellite entered its room in a flat overhead view.

This follows tp.exe, where the satellites were parks, not rooms of their own: they showed the parks of a city of their own (city 1's park list, entry *i* on
satellite *i*; 0x5B7610), a click opened that park's details (0x4C56E0 → 0x4C0AE0) and Enter visited it like any park, in the chat mode and the park's own room
(0x4C0880 → 0x5B8910, `CreateRoom` at 0x4E6A67). OpenTPW shows the details in the short variant tp.exe uses for such a park: the name as the title, "Theme : …"
(UITEXT 271 with the theme's name, formatted "%s : %s" as 0x4C0AE0 does), the description and Enter, with the owner's action disabled
(`[EXT:online-chat-park-details]`). docs/reverse/ONLINE-VISIT.md "Chat satellites (tp.exe)" records the traced list, counts, click, details, visit and room.

The server's `chatparks.json` now names, for each satellite, an original level (`jungle`, `hallow`, `fantasy` or `space`), optionally a published park and a
description; a new route, `GET /api/v1/chat-parks/scenes` (with a login), lists them for the click. A satellite that names a published park downloads and
visits it as any visit; otherwise the game builds the starting park of the satellite's level from its own data, once the level's files are there (the
browser copies them only when a level is first played). The visit walks in the satellite's standing room,
so its count counts it and a player on an older version, who sees the room from above, walks among the park's visitors (`[EXT:online-chat-park-scene]`). An
older server without the route keeps the overhead view (`[EXT:online-chat-park-view]`, now the view of a room without a park to visit).

The research also read how the original's own figure entered a park (0x4E5EF0): at the centre of entrance 0's cell (FixedItemInfo.EntranceA), placed as a teleport
and sent as `MovePlayer(…, 1)`. A visit's figure now starts there and its first place goes out as a teleport; before, it started between the entrance's two
cells as an OpenTPW choice.

Tests: `ChatSatelliteTests` (the default and configured chat parks, malformed fields and levels that are no theme's, the scenes with a published park and after it
is unpublished, the scenes route's login, the satellite's count, a satellite's details and their Enter without a visit, an older server without the route, an
answer that comes after the globe closed, a satellite the server keeps no chat park for, and — with the game data — Enter visiting a level's starting park and a
published park in the satellite's room through a real server), `ParkPresenceTests` and `ParkPresenceStateTests` (the entry as a teleport). The front-end smoke
captures the chat park details (`online-chat-park.png`) and checks the theme row is drawn.

Review changes: CreateRoom is cited at its call 0x4E6A67; the docs note that tp.exe sends the entry teleport only when the entrance cell is of type 0x12; a satellite has exactly one room name (`satellite:01` is refused); SERVER.md says a wrongly typed field resets `chatparks.json` to the defaults.
