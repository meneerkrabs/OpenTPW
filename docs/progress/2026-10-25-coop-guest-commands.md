## A guest's requests to the host (October 25, 2026)

A shared park's guests can now send the host commands to build with, and the host can grant each guest building ([COOP.md](../COOP.md), "Building by every player"; `[EXT:online-coop-commands]`, `[EXT:online-coop-permissions]`). This is C3a-1, the plumbing of building by every player; the guests' tools use it in C3a-2.

- A guest's park still changes through the host's stream only. A guest's command goes to the host as a numbered request, and the host applies it between two ticks at the start of its next frame, paused too, as the guest's seat.
- Every command the host applies becomes a record of the stream, accepted or refused alike on every peer. A guest's record names the request it answers, so the guest learns what became of its command when the record applies in its own park.
- The host refuses without a record a request that is too large, comes too fast, does not decode or comes from no guest's seat, and says why.
- A request lost with a connection goes out again after the resume, and the host never applies one twice. A resync keeps the requests and settles them against the snapshot's cut.
- The host grants building per guest with a command of the stream, so every park holds the same grants. A granted guest may lay paths and queues, place objects, remove a cell's object, path or queue (not a ride's station) and buy land; everything else stays the host's. A grant leaves with its member.
