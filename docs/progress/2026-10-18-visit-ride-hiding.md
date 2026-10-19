## A riding visitor is hidden from the park's other players (October 18, 2026)

In a visit of a published park, a player who rides a ride is now hidden for the park's other visitors, and appears again at the ride's exit when the ride ends.
Riding starts from the ride window's Ride it! or, as in the original's chat mode, from a click on the ride itself: in the chat mode a rideable ride under the
pointer shows its name with "Left-click to ride" (UIHELPTEXT 580, filled with the ride's name) and a click rides it (tp.exe 0x4BD7F4 → 0x4E15B0,
`[EXT:online-chat-ride-click]`). The ride view's screen covers the chat overlay until the ride ends, and its own exits (Escape, the eject button, C, E and the
right click) leave it.

The chat protocol's movements carry a mode from protocol version 5: walk, teleport, hide and unhide, the original's `BOSS_CHAT_MOVE` modes (tp.exe 0x4E7265).
A game sends the mode only to a server whose version 5 it read after its chat connection opened (a restart on an older version gets places alone), and the
server sends it only to connections that sent one themselves, so older games and servers never see a member their strict JSON would refuse. An older game keeps a riding player standing where they were, a joiner is not told where a hidden player stood, and any
later movement shows a hidden figure again. A double click's teleport and a name popup's goto now go out as teleports, so the other figures jump even over a
short distance.

The original hid the own person while riding (0x4BD090, mode 2) and showed it again at the ride's exit only for a ride entered from its first-person walk (0x42A1D0,
mode 3); a ride boarded from the overview ended with a teleport alone (0x4BD260), which left the player hidden for the others. OpenTPW always shows the figure
again (`[EXT:online-ride-hiding]`). docs/reverse/ONLINE-VISIT.md "Riding in a visited park" records the traced sends and the receive side.

Tests: `ParkPresenceStateTests` (riding hides the figure where it stood and lets it out at the ride's exit; a jump moves a figure at once), `ParkPresenceTests`,
`ChatModeRideTests` (rides and features whose view opens, not add-ons, shops or sideshows; the filled help; the exit), `OnlineFormatTests.MovementModesAreWrittenOnlyWhenSet`, `OnlineServerTests.ARidingPlayerIsHiddenFromThoseWhoReadModesAndKeptForTheOthers`
and `OnlineIntegrationTests.VisitorsOfAPublishedParkSeeEachOtherWalk` (a ride between two real sessions). The native visit check
(`--visit-park … --smoke-test --smoke-presence`) now rides a ride from the chat mode with the figure hidden, and leaves it with the ride view's right click.
