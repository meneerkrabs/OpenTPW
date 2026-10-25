## A co-op invitation test no longer races the server (October 25, 2026)

One more co-op test failed now and then on CI, on its own timing ([ONLINE.md](../ONLINE.md), "Shared parks"; `[EXT:online-coop-session]`).

- The test of an invitation to a player whose lobby closed its link invited at once. The server takes a closed link off its players a moment after the close, and an invitation that came first went to that link unanswered, so the test waited in vain for the server's "not online". The test now invites again until the server answers, a few times at most, well within the invitations' quota.
