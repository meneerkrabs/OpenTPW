## Sharing the open park from the game (October 24, 2026)

The park's pause menu now has a Build Together line that shares the open park with other players ([COOP.md](../COOP.md), "Sharing the park from the game"; `[EXT:online-coop-session]`). This is C2d-2a, the second part of the game's own co-op screens.

- The Build Together screen says why a park cannot be shared, or asks for a login. The player then chooses the title, whether the park is on the open list, and how many players it takes.
- Once the park is shared, the screen shows its code and players, removes guests (asked first), invites players by name, and ends the shared park for everyone (asked first). The park stays the host's.
- The attempt prepares the park while the link opens and creates the session. It hosts the park only after the server's answer, if the park is still open and can still be shared. A cancel, a closed park or a refusal at the answer leaves the session the server made.
- Logging out or switching servers now leaves a shared park at once, so guests hear that the host left instead of waiting for the away limit.
- Quitting or logging out while the server is still creating the shared park leaves nothing behind on the server. A leave that has to wait for a reconnection is kept for its own server, even across a visit to another server.
- Invitations go out one at a time, so a refusal always names the right player; a refused removal is no longer shown as a failed invitation.
- A status line at the top of the screen shows the code and how many players are connected while the park is shared, and for ten seconds that the shared park ended when the player did not end it.
- All texts are in eleven languages.
