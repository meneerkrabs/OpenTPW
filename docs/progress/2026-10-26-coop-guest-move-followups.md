## Guest building follow-ups: the Move tool across a resync, and fewer needless refusals (October 26, 2026)

These follow up on a guest's building in a shared park (C3b; [COOP.md](../COOP.md), "Building by every player"; `[EXT:online-coop-commands]`, `[EXT:online-coop-permissions]`).

- **The Move tool across a resync.** A guest whose park was resynced while carrying an object keeps its Move tool: the new park arms it again from the carry its snapshot holds, at the same angle. Before, the carry was ended and the object stayed sold.
- **A Move tool without its carry.** When the host releases a guest's carry while its Move tool is armed (for example after a reconnect under a new member id), the guest's tool now disarms. Before, every placement of it was refused until Escape.
- **A host link that lost its session's state** for a moment no longer counts every seat as left, so no guest loses its build grant or its carry for it.
- **Claimed queues refuse less.** A placement next to the path cell a claimed ride's queue joins may now lay or link path there; only a queue ride's entrance, whose first queue cell would replace that path, is still refused. A queue line that only ends on that path cell, which links to it, is allowed too.
- **The object window** turns its erase and Move buttons off for a guest on a claimed station, whose sale or move the host would refuse.
- **Tidying:** the buy item of a pick-up goes with its dropped wait; the stray-carry throttle lives on the session; doc references and a stale known limit are fixed.
