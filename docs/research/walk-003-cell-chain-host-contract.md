# WALK-003: guest cell-chain host contract

Public-source audit at `f4be81cbbe120d1638cfb0355a701bdac3d59f1e` (2026-10-22). This is synthetic host regression coverage, not recovered mixed-person native behavior. Proposal #551 remains incomplete.

## Current source contract

- `GuestSimulation.cs`, `Occupy`: each guest has one `OccupiedCell`. Moving removes the old list member, appends to the destination list and assigns an increasing `CellSerial`. Appending implements head insertion because readers traverse in reverse. Repeating placement within the same cell is a no-op for membership and serials.
- `RebuildOccupants`: restore sorts by saved serial, then guest-array index; zero serials provide legacy fallback. Positive saved serials are retained, and the allocator advances beyond the largest restored value.
- `GuestSimulation.Swimming.cs`, `LeaveCellLists`: removes the old entry and clears `OccupiedCell`. Swim sessions take this route; this is an explicit host extension, not evidence of native ride/held membership.
- `GuestSimulation.cs`, `SeparationForStep`: visits the surrounding 3 by 3 cells in row order, skips out-of-grid cells, reads guests head-first excluding Using/HeldByGuard/Gone, then appends staff with navigation on that cell.
- `StaffWorldSimulation.Handymen.cs`, `StaffSeparation`: likewise appends members after guest walkers, in member enumeration order. Picked-up/resting staff are filtered. Neither path supplies a persistent mixed-person entry order.

## Added regression coverage

`GuestCellChainLifecycleTests` adds three asset-free tests:

1. Moving interior/head members between two cells removes every old link, preserves all remaining entries in order, and leaves no stale head after emptying a cell.
2. Repeated same-cell placements preserve complete ordering and every saved serial; the next actual move consumes exactly one serial.
3. Restoring a multi-cell snapshot retains complete chains, and the next movement receives the same next serial and ordering as uninterrupted execution.

Existing `GuestThingListOrderTests` already covers newest-first guests, head preservation and legacy zero serials, as well as happiness-read invariants. The new cases specifically check complete migration chains and post-restore allocator continuation. Happiness values are unique synthetic labels used only to observe existing chain enumeration; tests do not change happiness policy or use it as native evidence.

## Mixed-person design boundary

A future shared chain needs typed identity (guest/staff/avatar), one shared entry sequence, explicit enter/leave hooks, saved membership/sequence, and the same ordered enumeration for both separation callers. Guest and staff IDs must not be assumed to share a namespace or timeline. Rebuilding by guest order followed by staff order cannot recover lost interleaving.

Before implementing that model, collect witnessed timing and positions for staff cell entry, picked-up/resting staff, riding/held/leaving guests, and online-avatar scope. Restore/migration must specify behavior for saves with no mixed sequence. Coordinate GuestSimulation runtime changes with umbrella work and the ECON-022/SAVE-002 owners. No runtime change is made here, and no native acceptance item is marked passed.

## Validation boundary

Source/API inspection and a separate Python list/serial model check are available. C# compilation, actual test execution, browser checks and native executable runs have not been performed for this patch. CI must compile and run the new tests before any test-pass claim.
