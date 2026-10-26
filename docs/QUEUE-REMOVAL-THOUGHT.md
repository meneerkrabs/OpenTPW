# Queue removal: the re-evaluation dismisses, the clear writes no guest

The queue tool's undo (Back, tp.exe `0x52FE50`) clears the cells through the cell clear `0x5367A0`, which writes no guest. The queue re-evaluation that follows (`0x4DE1F0` at
`0x52FFEC`) runs `0x501390` for every queued guest but the called one: a guest whose place is at least four per cell (unsigned, the guest not in state 14) thinks 13 when its
thing id is divisible by three, leaves the queue through the dismissal `0x5012F0` (the medium happiness step) and goes to state 6 where it stands. Every other guest keeps its place.

OpenTPW follows this. The undo (the native route and the per-cell route of an existing queue's segments) no longer sets down the guests on the emptied cells; the next simulation step
re-evaluates the queue (`GuestSimulation.ReevaluateQueue`) and dismisses by place alone, in place, with the thought. A guest within the new room keeps its place while it stands on a cell the
queue no longer holds: its state 11 routes it to its place from where it stands and dismisses it, again in place, when the search fails. A dismissed guest decides from its cell by the ported wander
(`0x4F9490`: a start without links searches for a path cell around it).

This holds for the cells of a placed ride (`RideVisitorBridge.OwnerWord` and `ExitAccessCell` set, a Theme Park World level). The bridges without them (the gates, the test rigs, the Theme Park Inc slice) keep
the host's relocation to the front cell or the nearest path. The removal from a clicked cell on (`[EXT:dev-one-click-remove]`, `[EXT:queue-backtrack]`) keeps `GuestSimulation.SetDownOffQueueCells`, which
puts the queued guests on the emptied cells on the nearest path and dismisses them: the original has no such removal.

`QueueRemovalCommandTests` drives the queue tool's `Undo()` through the production writer and command: the clear writes no guest (before the re-evaluation every occupant is where and what it was),
the re-evaluation dismisses the overflow in place with the thought for ids divisible by three, excludes the called guest and a guest waiting to board, leaves another ride's queue alone, replays to the same
state hash, and a dismissed guest starts its wander from the emptied cell.
