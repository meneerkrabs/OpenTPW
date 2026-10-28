# The park door and entrance gate

The park door is the park's open and closed switch: the entry-price screen's door button (`b_door`, help
193/194), and the keyboard shortcuts Ctrl-O and Ctrl-C (`InputButton.OpenPark`, `ClosePark`). It drives the
economy (`ParkEconomy.OpenPark`/`ClosePark`, which raise `ParkOpened`/`ParkClosed`) and the level's
`ParkGate` (`source/OpenTPW/World/ParkGate.cs`), which commands the entrance gate's script and closes or
opens the attractions, including the separately owned prototype ride. Original levels only; read-only visits and the sandbox have no gate.
The door and ride-view shortcuts wait while a HUD menu, text field or keyboard-capturing developer UI owns input.

## What the original does

**Evidence.** The door routine is the Feral Mac function at `0x108ee4` (`[BIN:STP-PPC:0x108ee4]`). Its
prologue and the split on its second argument match the maexah/OpenTPW lead (`docs/exe/park-gate.md`, PC
`FUN_00519ef0`, runtime addresses): a nonzero second argument is the end-of-park path, a zero one the
ordinary door. The fork's PC function exists in the decrypted no-CD `tp.exe` that COMPLETION-PLAN.md allows for
analysis (`0x519ef0` starts a normal SEH-framed function); its body was not compared here, so the Mac
binary is the checked source. The world field the routine reads is at Mac `world+0x1DA710` (decoded from `addis 0x1E0000` and
`-0x58F0`).

**Opening** (`DoorMoved(closed: false)`): the gate's command variable `VAR_COMMAND` becomes 1, and every
closed attraction the walk reaches opens again, unless the original's reopening guard `0xde654` refuses it: broken down or
condemned, an upgrade on its way, a mechanic called to it, or a coaster without a complete circuit (ORACLE.md F27; ECON-049 for what is not modelled).

**Closing** (`DoorMoved(closed: true)`): every attraction the guests may be offered closes at once (thing
flag `+0x2E` bit 4). The gate is commanded shut (`VAR_COMMAND` 0) only when the gate reports itself open
(`VAR_STATUS` 1) and no guest stands inside. Otherwise the gate stays open and the closure is retried.

**The retry** (`ParkGate.Tick`, at crossed multiples of 30 park turns, Mac `0x10536c`, PC `FUN_00516380`):
one park turn is 248 ms, so the interval is 7.44 seconds of scaled time
([DET-plan §2.1](reverse/DET-plan.md#21-substep-order-pinned), `det_evidence.py` `TURN_THIRTY`).
It commands a closed park's still-open gate shut once the guest census is zero and no live staff stands
outside the counted cells (staff of kinds 4 to 8 on a cell that is not of type 0, 1, 3, 9 or 10).
Calls during the same qualifying turn cannot repeat the command; faster speeds and skipped fixed updates
still detect the crossed boundary. The fork records that the immediate close does not run this staff scan.

**The census** applies the original predicate to the guest grid: it counts guests on cells of type 0, 1, 3, 9 and 10 (Mac predicates `0x85190`–`0x85258`).
Preserved approach-road and outside types, out-of-bounds positions and departed guests do not count. The cell is the one the guest's position stands on: tp.exe's predicate
(`0x004FA990`) reads the thing's cell bytes, which every walk step rewrites whatever the new cell's type (`0x004FA2A0` → `0x0050B6A0`) and the teleport `0x004FA930` sets (the ride
exit `0x005014E0`), so OpenTPW reads the walk position's cell (`GuestNavigation.Cell`, the cell its cell lists follow), not the stored cell a step moves only onto path and queue cells.
A guest walking home leaves the census as it steps off the park onto the gate's road, so a closed park's gate shuts at the first 30-turn retry after the last guest walked out,
not when the guests board the vehicle; a rider counts at the stand point it walked to (the ride's entrance cell).
The grid holds the original's types: the world init types the cells without terrain 7, 2 or 30 (`0x00536490` with
`0x00450420`), a placed object writes 4, 9 and 10 over its footprint (`0x00528A70`), and a removed object leaves the cells the demolition `0x00527EE0` leaves (the cell remover `0x005367A0` per type; ECON-050 keeps the second-layer clear).

## What closing does to guests

- Admissions stop at the walk to the booth, not at the booth (tp.exe `0x4ff730`, `0x4ff7f0`, `0x4ff9d0`): a visitor
  who reaches the booth while the park is closed turns home with no thought; one already at the booth is weighed and
  pays as usual (the booth and `ParkEconomy.TryAdmitVisitor`, like `0x4d0600`, read no park-closed state) and
  is then sent home without a refund while the park stays closed. A bankrupt park takes no fee: tp.exe's bankruptcy
  stops the whole thing loop (world state 4, `0x516380`, ECON-030), where OpenTPW's guests keep updating.
- Guests already inside keep their place and carry on. Their rides are closed; a queue for a closed ride is
  turned away, one head at a time (tp.exe `0x4e1220` → `0x4e0450` → `0x5012f0`, the ride's own update, `RideVisitorBridge.HostStep`;
  the door only closes the rides), and a guest still walking to a ride drops it and takes the big drop (`0x4ffeb9`); nobody is pulled out of a queue by the closing itself (GUEST-011).
- The gate stays open while any guest stands on a counted cell, and shuts only when the park is empty.
- Opening reopens the attractions, but only those the may-open guard allows (`0xde654`, ORACLE.md F27: not broken down, condemned or upgrading, a coaster with a complete circuit; `ECON-049`).

## Approximations

- `ECON-049`: the reopen guard (`0xde654`) is executed in the oracle and follows the original's state test, including the maintenance flag (the ride
  panel's call for a mechanic) and the back-of-queue path test (`0xdd744`, PC `FUN_004de4a0`: the back queue cell's link byte must differ
  from its queue link, so a queue whose back has lost its path stays closed); a queue ride whose entry has no cardinal link bit has no front and is unconnected
  (`0x4DE040`, `0x4DE130`); a ride on OpenTPW's one-cell queue and an object without a queue on untyped geometry are taken as connected, because OpenTPW's ride entrance cells
  keep no link byte there.
- `ECON-050`: a removed object leaves the cells the demolition leaves (`DemolishFootprint`); the second-layer clear `0x0053B280`, the moved-object variant and the track replays are not ported.
- `GUEST-011` (retired): a paid visitor walks in only while the entrance gate's script variable 1 (`VAR_STATUS`, `0x51a290`) is 1, else it goes home as at a closed park (`0x4ff814`, `GuestSimulation.EntranceGateOpen`);
  the closed-ride queue turn-away is the original's (the door closes the rides, whose update dismisses the queue head, one per turn), and the close clears a called guest's loading slot at once (`0x4df300`, [CLOSED-RIDE-ADMISSION.md](CLOSED-RIDE-ADMISSION.md)).
- The keyboard shortcuts are the door button's own help rows; the original's shortcut table row for Ctrl-C
  is a fork lead (`park-engine.md`, PC `0x0040c410`), not verified statically.

## Tests

`ParkGateTests` covers the census cell types, the door's gate command and rides, the populated closure's hold
until the last guest leaves, the gate shutting at the first retry after the last guest walked out onto the road, the 30-turn retry across normal/fast speeds and skipped updates, single retries per boundary, duplicate attraction aliases, and the staff blocker (including fractional and out-of-bounds positions).
`ParkInputGuardTests` checks armed tools and held/captured mouse edges, plus shortcut capture. The native
jungle smoke calls the actual level input path with armed tools while riding, checks actual HUD shortcut
dispatch behind a modal and ImGui capture, and checks that closing the park closes the prototype
and stops its admissions. The gate-script tests need
`OPENTPW_GAME_PATH` and are skipped without it.
