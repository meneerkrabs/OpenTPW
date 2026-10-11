# The park door and entrance gate

The park door is the park's open and closed switch: the entry-price screen's door button (`b_door`, help
193/194), and the keyboard shortcuts Ctrl-O and Ctrl-C (`InputButton.OpenPark`, `ClosePark`). It drives the
economy (`ParkEconomy.OpenPark`/`ClosePark`, which raise `ParkOpened`/`ParkClosed`) and the level's
`ParkGate` (`source/OpenTPW/World/ParkGate.cs`), which commands the entrance gate's script and closes or
opens the attractions. Original levels only; read-only visits and the sandbox have no gate.

## What the original does

**Evidence.** The door routine is the Feral Mac function at `0x108ee4` (`[BIN:STP-PPC:0x108ee4]`). Its
prologue and the split on its second argument match the maexah/OpenTPW lead (`docs/exe/park-gate.md`, PC
`FUN_00519ef0`, runtime addresses): a nonzero second argument is the end-of-park path, a zero one the
ordinary door. The fork's PC addresses are not in the static `tp.exe` image: its `.txt` section is an
encrypted stub and the code at its addresses is unpacked at run time, so only the Mac binary was checked
here. The world field the routine reads is at Mac `world+0x1DA710` (decoded from `addis 0x1E0000` and
`-0x58F0`).

**Opening** (`DoorMoved(closed: false)`): the gate's command variable `VAR_COMMAND` becomes 1, and every
closed attraction the walk reaches opens again, unless it is broken down (the `VAR_BROKEN` guard, see
ECON-049 for what is not modelled).

**Closing** (`DoorMoved(closed: true)`): every attraction the guests may be offered closes at once (thing
flag `+0x2E` bit 4). The gate is commanded shut (`VAR_COMMAND` 0) only when the gate reports itself open
(`VAR_STATUS` 1) and no guest stands inside. Otherwise the gate stays open and the closure is retried.

**The retry** (`ParkGate.Tick`, every 30 economy ticks, Mac `0x10536c`, PC `FUN_00516380`): a closed park
whose gate still stands open commands the gate shut once the guest census is zero and no live staff stands
outside the counted cells (staff of kinds 4 to 8 on a cell that is not of type 0, 1, 3, 9 or 10). The fork
records that the immediate close does not run this staff scan.

**The census** counts guests on cells of type 0, 1, 3, 9 and 10 (Mac predicates `0x85190`–`0x85258`). Guests
on the approach road, outside the park or already gone do not count. A rider counts at their stored cell.

## What closing does to guests

- Admissions stop: `ParkEconomy.TryAdmitVisitor` refuses while the park is closed, so visitors at the booth
  do not enter.
- Guests already inside keep their place and carry on. Their rides are closed; a queue for a closed ride is
  turned away, one head at a time (the fork's `FUN_004e0450`, `docs/exe/ride-operation.md`, "The closed
  ride"; not reproduced by `ParkGate`).
- The gate stays open while any guest stands on a counted cell, and shuts only when the park is empty.
- Opening reopens the attractions, but only those the may-open guard allows (`ECON-049`).

## Approximations

- `ECON-049`: the reopen guard checks only `VAR_BROKEN`. The upgrade and condemned states and the back-of-queue
  path test (`FUN_004de4a0`) are not modelled.
- The ticket booth closes at once; the original's booth state and the closed-ride queue turn-away are not
  traced here.
- The keyboard shortcuts are the door button's own help rows; the original's shortcut table row for Ctrl-C
  is a fork lead (`park-engine.md`, PC `0x0040c410`), not verified statically.

## Tests

`ParkGateTests` covers the census cell types, the door's gate command and rides, the populated closure's hold
until the last guest leaves, the 30-tick retry, and the staff blocker. The gate-script tests need
`OPENTPW_GAME_PATH` and are skipped without it.
