## Players in a visited park (October 18, 2026)

A visit of a park downloaded from the server now shows the park's other visitors walking in it (docs/ONLINE.md "Players in a visited park"):

- **The park's room.** A visit started from Find Parks, the park details or a city lobby carries the park's server id (`ParkVisitInfo.OnlineParkId`) and, while logged
  in, enters the park's chat room, as the original's visit switched to chat mode (manual p. 65). Leaving the park leaves the room; a chat connection the visit opened
  is closed again.
- **Figures.** Every player of the room is drawn with the guests' kid sprites, walking or standing like a guest, with their name over the head (the player's own in
  yellow) under the HUD's windows, and sparkles once a world turn with the original's avatar effects: 53 *Avatar* for the own figure, 57 *OtherAvatar* for the
  others (tp.exe 0x4F7C60 / 0x4FA470, run by the online person's tick 0x50B453). The figures are presentation only: the kids' `GuestRenderer` draws them after
  the guests (`SpriteFigure`), and nothing of the simulation sees them.
- **Walking.** A ground click that hits no object walks the player's figure there round water, objects and the fence (the octile cell route of the original's person
  route 0x50F8E0, straight when there is none); a double click teleports (manual p. 68). Its place goes to the server while it walks, on arrival and every ten
  seconds; the others follow the relayed places and jump on a teleport.
- **Chat.** The room's last lines show at the original chat overlay's message list, and Enter over the park opens a chat line at its entry.
- **Evidence.** The PC executable's `BOSS_CHAT_MOVE` handler (0x4E7265 in 0x4E6E80) is read: a relayed move names the online person and carries an 8.8 cell
  position and a mode (walk, teleport, hide, unhide and teleport), and every client walked the person there itself (docs/reverse/ONLINE-VISIT.md).

The chat session no longer sends joins, places and lines through the operation slot (`Busy`), which dropped them while a download, a vote or another request
ran; they go out in order on their own chain, the newest place replacing places not yet sent. A room asked for before the connection opens is joined once it
does, a reconnection returns to it, a join the rate limit dropped is sent again after five seconds, and a refused park is remembered. A roster update of the
room the player is in no longer clears the room's places. The server answers each room's first movement with where everyone stands, not only a connection's
first movement ever, and reports protocol version 4; visits draw figures only on such a server.

Design choices are labelled `[EXT:online-visit-presence]`, `online-avatar-look`, `online-avatar-walk`, `online-avatar-route`, `online-avatar-start`,
`online-avatar-click`, `online-avatar-labels`, `online-avatar-sparkle-cap` and `online-visit-chat-line`, each with the known original evidence in docs/ONLINE.md.
Not done: hiding a figure while its player rides, the original in-park chat overlay with its people list and vote button, chat parks drawn as 3D parks, and
building a park together.

Tests: `ParkPresenceStateTests` (routes, walks, teleports, send rules, remote figures, looks), `ParkPresenceTests` (connection, room, announcements, keyframes,
old servers, refusals, leaving), `OnlineIntegrationTests.VisitorsOfAPublishedParkSeeEachOtherWalk` (two sessions against the loopback server see each other
walk, chat and part), `RosterUpdatesKeepTheRoomsPlacesAndAJoinDroppedByTheRateLimitIsSentAgain` and `OnlineServerTests.EachRoomsFirstMovementAnswersWhereEveryoneStands`.
The native visit smoke gains `--smoke-presence`: three scripted players round the entrance are drawn with names and sparkles, a HUD click walks the player's
figure and its places are sent (`artifacts/native-smoke-online-visit-presence.png`). Its plain ride step compared a seated ride's camera with the fallback
frame; it now compares it with the seat. The front page no longer lists seeing other players as missing.
