## Reconnected riders keep movement in its room (October 19, 2026)

- **Room transitions.** A hide held while a reconnected visit learns the server version is discarded when that visit leaves its room. Entering a chat park on the same connection can announce its normal visible position without inheriting the ride's hide. A refused join likewise drops movement still waiting for the refused room.
- **Send ordering.** A coalesced position cannot be appended after a later room join. Positions already queued keep their order before the join; a new room's initial movement and same-room join retries remain intact. Existing version-read retries, stale-response guards and legacy mode stripping are unchanged.
- **Coverage.** Added asset-free loopback regressions with a held reconnect-version response, a deterministically gated send chain, new-room announcements and a refused join. Existing hide coalescing coverage also checks same-room joins. The local workspace has no .NET SDK, so C# build and execution remain for CI; local documentation and source checks are recorded separately.

See [online presence](../ONLINE.md#players-in-a-visited-park) and the [server protocol](../SERVER.md).
