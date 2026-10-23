## The browser's own transport carries the co-op link through the real server (October 23, 2026)

The browser's co-op link now runs end to end through the real server, not over a recording ([COOP.md](../COOP.md), "The browser's link to a real server"; `[EXT:online-coop-session]`). This is C2c-2b-5.

- Two pages of the server, a host and a guest, log in, open the co-op socket with the token in its first frame, share a session, store and download a snapshot, and pass a frame and a message each way. Every payload is checked by its length and SHA-256.
- `scripts/smoke-coop-link-browser.sh` runs it locally, and the browser determinism workflow runs it against both of its pages. It needs no game data.
- `CoopLinkProbeTests` runs the same check on the desktop against the loopback server.
