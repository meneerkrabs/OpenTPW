## The walk to the stand point is the original walk, and a queue edit leaves no guest stuck (October 17, 2026)

A called guest's last walk, into the ride's entrance cell for the stand point (state 13), is now the search and edge-test walk of every other guest wherever the cell map lets it in (WALK-006 narrowed; QUEUE-015 narrowed). Read in tp.exe via REA:

- **The call** (`0x4FFFF0`): a guest at place 0 that the ride called asks the generic route `0x4FA5F0` to the stand point `0x4DEDF0(0)`, the entry cell `+0x36` of the ride; a route makes state 13 ("being loaded to ride"), none logs "the player has removed the path from under me and I can no longer get into object" and releases the called slot (`0x4E0AC0`), unlinks the guest (`0x4DDD20`) and dismisses it (`0x5012F0`, exit `0x5004AA`). State 13 itself (`0x5006B0`) treats a stuck walk (result 2) as arrived and goes on to the price test and the ride's admission (`0x4E0900`).
- **The queue edit** (`0x4DE1F0` calling `0x501390` for every queued guest but the called one): a guest leaves only when its place is at least four per cell and it is not in state 14, so the original re-evaluation never looks at a guest's cell; the demolition (`0x527EE0`) and the queue undo (`0x52FE50`) are the only clears of queue cells there.
- **Built**: the walk into the entrance cell goes through `GuestNavigation.Navigate` like the queue walks. A queue whose outside cell is removed under the player-approved `[EXT:removable-access-cells]` falls back to the path cell as its one-cell front, and guests stand in that cell (`GuestSimulation.StandCellOf`), where the stand point used to lie in the emptied cell beyond it and the guest waiting to board was left stuck there.
- **Still approximated** (WALK-006): OpenTPW's imported and restored rides, and a placed ride without a queue, keep no link byte on the type-9 entrance cell, so the edge test refuses the step in; the guest then keeps the old route walk where the original would dismiss it. Evidence needed: what the original's load and placement leave on that cell for the types without a queue. QUEUE-015 keeps its demolition charge and placement counters.

**Pinned digest moves** (docs/DETERMINISM.md): the fixed synthetic run `0x3DA16AED71E2F1E4` → `0x625402703E4F66A7`; the Jungle guest run and the cross grid of `PathBuilderTests` do not move.

**Saves**: unchanged; a walk restored from an older save keeps its route walk.

New tests (`QueueStandWalkTests`): the original walk into a linked entrance cell, the route walk where the edge test refuses it, and a called guest whose queue is edited away standing in the fallback entrance and not stuck. `GuestThingIdReuseTests` now restores the script variable of a presented guest like the other snapshot tests do.
