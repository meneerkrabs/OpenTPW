## A shared park's own chat room (October 24, 2026)

Each shared park's session now has a chat room of its own on the server, `coop:<id>`, which only its members can enter ([ONLINE.md](../ONLINE.md), "Shared parks"; [SERVER.md](../SERVER.md); `[EXT:online-coop-session]`). This is C2d-1, the first part of the game's own co-op screens.

- A member enters by a join or by going to another member in the room. Anyone else is refused, also through `/goto`.
- The game's chat enters the room once it hosts or joins a session (opening the chat if needed, and only on a server of protocol 8 or later), and goes back to the lobby when the session is over for it. While in a shared park, its chat belongs to the session: nothing said to a room is sent anywhere else.
- A member who leaves or is removed, and everyone once the session ends, is back in the lobby before the others hear of it.
- `/locate` only says that a player is in a shared park; world shouts and pages from the room do not name it; and no buddy is told about it.
- The game shows the room under its session's title, or "Shared park", in all eleven languages.
