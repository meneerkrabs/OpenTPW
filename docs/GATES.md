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
condemned, an upgrade on its way, or a coaster without a complete circuit (ORACLE.md F27; ECON-049 for what is not modelled).

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
Preserved approach-road and outside types, out-of-bounds positions and departed guests do not count. A rider counts at their stored cell.
The reduced imported guest grid does not yet preserve every original cell type: unpreserved outside, approach or footprint cells remain type 0 and can be overcounted (ECON-050).

## What closing does to guests

- Admissions stop: `ParkEconomy.TryAdmitVisitor` refuses while the park is closed, so visitors at the booth
  do not enter.
- Guests already inside keep their place and carry on. Their rides are closed; a queue for a closed ride is
  turned away, one head at a time (the fork's `FUN_004e0450`, `docs/exe/ride-operation.md`, "The closed
  ride"; not reproduced by `ParkGate`).
- The gate stays open while any guest stands on a counted cell, and shuts only when the park is empty.
- Opening reopens the attractions, but only those the may-open guard allows (`0xde654`, ORACLE.md F27: not broken down, condemned or upgrading, a coaster with a complete circuit; `ECON-049`).

## Approximations

- `ECON-049`: the reopen guard (`0xde654`) is executed in the oracle and follows the original's state test; the maintenance flag (no maintenance button in OpenTPW)
  and the back-of-queue path test (`0xdd744`, PC `FUN_004de4a0`) are not modelled.
- `ECON-050`: the guest-grid import preserves paths, but not all original outside, approach-road and footprint cell types. The census predicate is traced; the imported type provenance is incomplete.
- The ticket booth closes at once; the original's booth state and the closed-ride queue turn-away are not
  traced here.
- The keyboard shortcuts are the door button's own help rows; the original's shortcut table row for Ctrl-C
  is a fork lead (`park-engine.md`, PC `0x0040c410`), not verified statically.

## Tests

`ParkGateTests` covers the census cell types, the door's gate command and rides, the populated closure's hold
until the last guest leaves, the 30-turn retry across normal/fast speeds and skipped updates, single retries per boundary, duplicate attraction aliases, and the staff blocker (including fractional and out-of-bounds positions).
`ParkInputGuardTests` checks armed tools and held/captured mouse edges, plus shortcut capture. The native
jungle smoke calls the actual level input path with armed tools while riding, checks actual HUD shortcut
dispatch behind a modal and ImGui capture, and checks that closing the park closes the prototype
and stops its admissions. The gate-script tests need
`OPENTPW_GAME_PATH` and are skipped without it.
