# WALK-003: the cells' thing chains (retired in round 10)

The original keeps one chain per map cell for every kind of thing (the world's singletons and the vehicles sit on cell 1, objects on their own cells; only the people move between cells) (tp.exe `0x004D91F0` inserts at the head, `0x0050B6A0` moves a thing on every cell change, `0x004D9280` unlinks, the destroy request `0x0050B780` turns the thing into kind 2 and queues it, and the turn's deferred destroys `0x00516641` unlink it through `0x0050B980`; until then it still heads its chain for the head tests and the separation query skips it). The separation query `0x005102F0` walks the chains of the 3 by 3 cells around the stepping thing head first and keeps the kinds 1, 4 to 8 and `0x12` with no test of the state.

## What OpenTPW does

- Guests keep a list per cell (`GuestSimulation.Occupy`, oldest entry first, the head is the last) and the staff keep theirs (`StaffCellChain`). Both take their entry numbers from the one counter (`GuestSimulation.cellSerial`), so reading the two lists by number, newest first (`AppendCellChain`), is the original's single chain. Guests save their number (`Guest.CellSerial`); the staff actors' numbers are in `StaffActorSnapshot.CellSerial` (0 in an older save, which files the staff oldest, first member head-most).
- A staff thing enters its cell's chain when its member first has a walk, goes to the head of another cell's chain when the walk's cell changes (`StaffWorldSimulation.UpdateStaffMember`, `BeginStaffTurn`), keeps its place while the member is in the player's hand (the position words are written only by the walk step `0x004FA2A0` and the teleport `0x004FA930`, the drop is `0x00505EA0`) and leaves it when the member is fired.
- A guest on a ride (state 16) or carried by a guard (state 17) keeps the position of its last walked point and stays on the chain, so it is a neighbour like any other thing. Swimmers and guests changing are in no chain (`[EXT:pools-swim]`).
- The heads of a cell's chain read by the original (the guest that waits at the entrance booth, the head of the arrival vehicle's stop cells, state 21) are the real head: a staff member that entered later heads the cell.

## Still outside

The online avatar (kind `0x12`) is a thing the separation query reads like a guest or a staff member; OpenTPW builds no such thing and the persons of the online game either (WALK-010), so those heads and neighbours are missing from its chains, and the objects on a cell's chain are not neighbours (the query skips every other kind).

Tests: `WalkChainTests`, `GuestThingListOrderTests`, `GuestCellChainLifecycleTests`.
