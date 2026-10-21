## Co-op: joining a running session (October 21, 2026)

The co-op stream can now bring a member that joins a running session, or a guest whose park lost the stream, to the host's park without leaving the session ([COOP.md](../COOP.md), "Joining a running session"; `[EXT:online-coop-snapshot-join]`). This is the stream's part (C2c-1): no game joins a session this way yet, since the game's own join (C2c-2) and the screens (C2d) follow.

- The host takes a capture of its park at a cut of its command stream, compresses it over several frames, uploads it to the server and tells the member where it is. The member downloads it, builds it, checks it against the host's digest at the cut and swaps it in.
- Every message of the stream carries the capture it belongs to, so a late message from an old stream changes nothing. A lost message, a reconnect or a slow upload is answered again, never with a second capture.
- A joiner catches up within a window of its own while the park runs on. Three failed joins within ten minutes leave the session; a server that asks to wait (too many downloads, a server error) is asked again later without counting.
- While a park is shared, its global golden tickets are decided by its own records on every machine. When the session ends, the player's progress takes them back by the player-wide rule.
- The server limits the snapshots each session's host uploads, and its 429s now say when to try again (`Retry-After`).
- The session digest no longer counts whether the coaster drag set exists (version 2): one machine's read of it would have been a desync.

`CoopJoinTests` and `CoopJoinSessionTests` cover the snapshot's parts and the joins and resyncs, over the in-memory server and once through the real links and the server's snapshot store; `CoopSnapshotDownloadTests` covers the client's ranged download.
