## The original chat overlay in a visited park (October 18, 2026)

A visit of a published park with other players now shows the original's chat overlay, and the chat parks use the same overlay (docs/ONLINE.md "The chat
overlay"):

- **Chat mode.** The visit starts in the original's chat mode: the chat overlay (tp.exe table 0x758AE8, built by 0x4BF460) stands over the park in place of
  the HUD, which the original's chat mode does not have (0x4862A0). F4 switches between the overlay and the visit's HUD, as it switched the original's
  overlay off and on (0x4BE3A0), and Enter over the park returns to the chat mode with the entry taking the keys. In the chat mode a click anywhere in the
  park's opening walks the player's figure, with the original's help 401 ("Left-click to walk to this point"). The chat mode starts once the visit is in its park's
  room and gives way to the HUD when the server refuses the park.
- **People list.** The room's players or, on the second tab, the buddies; a buddy's name is green in this park, blue online elsewhere and red offline, any
  other name white (0x4BE730 by the row flags of 0x4EE5C0). The buddy list is asked as the chat connection opens, so the colours and the popups are right
  from the start, and these answers stay out of the message list; the list stands until each ask's answer comes.
- **Name popups.** A right click on a name opens the original's popup at the pointer: six buttons for a buddy (ignore, goto, locate, page, black-mark,
  remove buddy; table 0x758CD8), four for anyone else (ignore, goto, black-mark, add buddy; table 0x758E18), none for the player's own name (0x4BE290). A goto
  to a player in the visited park takes the player's figure to them. A click beside a popup closes it when the button is let go, so it reaches nothing
  under the popup.
- **Player buttons.** The postcard opens Send Postcard (0x4A9380, the button's model `b_chtpcard`), the vote votes once for the visited park (and not
  after the server refused it) and the earmuffs stay down while they are on (0x4BE820). The session now keeps the server's answers for them: the buddy
  list, the ignored players and the earmuffs (per connection, as the server keeps them) and the parks voted for or refused. A popup's command quotes a
  name with a space, so the server takes the whole name.
- **Messages and entry.** The room's lines are cut into rows at the list's width and the last twenty rows shown (0x4BE970); a click gives the entry the
  keys, and so does Enter while nothing else has the focus, Enter says its line and Tab leaves it. While it has the keys the camera still takes the
  wheel, the edges and the drag over the park, and no hover help covers it while a line is typed.
- **Panels.** The frame's left column and bottom strip keep clicks off the park, which takes them through the opening between them (the triangles of
  `f_chat`); pushing the pointer against the screen's edge over them still scrolls the camera.

The chat park screen drops OpenTPW's top action bar for the same overlay, so `EXT:online-chat-action-bar` and `EXT:online-visit-chat-line` are retired;
new design choices are labelled `[EXT:online-visit-chat-mode]`, `online-chat-help`, `online-chat-person-flags`, `online-chat-row-highlight`,
`online-chat-popup-place` and `online-chat-goto`, each with the known original evidence in docs/ONLINE.md. The decoded chat mode is described in
docs/reverse/ONLINE-VISIT.md, including the message colour table (0x758F10), which is not reproduced yet: which speech lines its kinds colour is not traced.

From the review of the online presence: the walk's route search reuses arrays the size of the park and knows a spot that cannot be reached at once from
the park's connected areas, so such a click no longer floods the whole park on the game thread; the server answers a join of the room a player is
already in (a retried join, a goto to someone in the same room) with the roster only, keeping their place and telling nobody; a chat connection the
online screens are still opening is no longer closed when a visit leaves; and the avatar effect's two world offsets (0x519410, 0x519420) are noted.

Tests: `ChatPanelTests` (the people flags and colours, the message rows, the table places and help, the buttons, the entry and how it takes the keys,
the popups at the pointer and their commands, a quoted name), `VisitChatModeTests` (the visit's chat mode in place of the HUD, F4, Enter to the entry,
Tab, Escape to the pause menu, the pointer left to the park while the entry has the keys, waiting for the park's room and leaving it when the park is
refused), `OriginalUiAssetTests.TheChatFrameCoversTheParkOnlyWithItsPanels`, `CameraControlTests.AUiThatLeavesTheEdgeBandToTheCameraStillScrollsAtTheEdge`,
`OnlineIntegrationTests` (`AVisitsChatOverlayFollowsTheServersAnswers`, `AVisitsVoteButtonKeepsTheServersRefusals`, `ADroppedBuddyAskKeepsTheBuddyList`),
`OnlineServerTests.JoiningTheRoomAgainKeepsThePlaceAndTellsNobody`, `ParkPresenceStateTests.TheRouteSearchOfTheLargestParkAnswersUnreachableSpotsAtOnceAndFindsLongDetours`
and `AFigureOnABlockedCellLeavesItByAnyFreeStep`, and the opening connection in `ParkPresenceTests`. The native visit smoke with `--smoke-presence` walks
the chat mode (entry, popups, F4) and captures the overlay and a buddy's popup.
