## A guest buys land, clears, sells and edits queues in the shared park (October 26, 2026)

A granted guest in a shared park can now buy land, clear an area, remove path and queue cells, use Backspace, sell objects and edit a ride's queue ([COOP.md](../COOP.md), "Building by every player"; `[EXT:online-coop-commands]`, `[EXT:online-coop-permissions]`). This is C3b-1, after the first tools of C3a-2.

- **The area clear.** It is now one command for every player: the whole rectangle goes to the host as one request. The park ends up as it did when each object and path cell was cleared on its own. A guest's clear leaves standing what it may not remove, such as a coaster's or track ride's station, and still clears the paths around it.
- **Land, the area clear and the sell buttons** end at once, as the host's do, so a guest's request goes out and the guest carries on. A refused purchase says that the land cannot be bought.
- **The sell buttons** take no second sale of an object while its first waits. The attractions list no longer offers a fixed item for sale, just as the object window does not.
- **The remove click** now takes the path or queue cell under it when no object stands there.
- **Backspace** takes a path or queue cell back. A held key sends one request at a time.
- **The queue's edit.** A guest's queue click starts the queue tool once the edit comes back, from the queue exactly as the host's click would have read it. Escape, a right click or another tool leaves the wait. If a later change sold the ride or changed its queue in the meantime, no tool starts.
- **Still the host's for now:** pools, Move, tracks and coasters (the next slices of C3), staff and money (C4), and undo (C5).
