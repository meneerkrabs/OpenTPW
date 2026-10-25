## A dropped co-op socket always resumes (October 25, 2026)

A shared park's link that dropped its own socket could give up instead of reconnecting ([ONLINE.md](../ONLINE.md), "Shared parks"; `[EXT:online-coop-session]`). Four co-op tests failed now and then on CI: two because of that, two because of their own timing.

- Dropping a socket (a send queue that fell too far behind, a test's lost network) could race the socket's writer: the writer closed the connection normally while it was being dropped, and the link took the server's answer for a close it does not resume after. The connection is now dropped before the writer wakes, and a socket this game dropped counts as lost whatever its reader saw on its way out, so the link always resumes.
- The open list's test of a park that ended after the list came now waits for the server to end the park before the stale row's join: a join that raced the host's leave was seated first and then heard that the host left.
- The live acceptance's harness tests run on a clock of their own, so a slow first frame on a loaded machine no longer passes a step's deadline early.
- A co-op link test that waits in vain now also says how each player's last socket ended and which states its link went through.
