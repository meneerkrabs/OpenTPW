## Co-op: the level in a session (October 21, 2026)

A park can now take part in a co-op session as the host's or a guest's ([COOP.md](../COOP.md), "The level in a session"; `[EXT:online-coop-lockstep]`). `CoopSession` starts the host's command stream or a guest's on the level. It ends when the park closes, when the game quits, when the link's session ends or when the host's stream ends it.

- Only a running Theme Park World park is shared, in a game started without developer options or park-building smokes.
- A guest's own click is refused with "Only the host builds in a shared park", in the game's eleven languages, at most once every 1.5 seconds.
- The cheats, the secret park name and the developer panel's direct opening are refused alike on every machine.
- The pause menu and the other screens stop no shared park.
- A guest's park is never saved, restarted, published, exported or shared again, also after its session.
- Entering the queue tool no longer recomputes the ride's queue on one machine: it reads the queue from a copy, and the queue commands bring it up to date on every machine.
- The digest's cost and the frame sizes at the fastest speed were measured (COOP.md, "Measurements"). A faster park now takes its digests once a second of real time.
- The session's own recording reuses the digests' world hashes and records the session's rules (command log format 3), so its log replays offline.

No screen starts a session yet: the join and the screens follow.

`CoopSessionTests` covers each rule and each end of a session, and the guest's message in its HUD. It also runs a host and a guest through the game's real links to a loopback server, with a lost socket on each side, to equal parks.
