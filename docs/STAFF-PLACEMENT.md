# Staff placement, pick-up and drawing

Traced in tp.exe (Windows Sim Theme Park World v2.0); labels `[BIN:TPW-EXE:...]` in the code cite the routines. Code: `World/Staff/StaffPlacement.cs`
(tools and rules), `World/Staff/StaffSprites.cs` (sprite banks and clips), `World/Staff/StaffRenderer.cs` (drawing), `Hud/ParkHud.Staff.cs` (window and
buttons), `Hud/ParkHud.cs` (`UpdateStaffTool`).

## Hire and place

A hire (0x507bf0) creates no object: it builds the Place tool (CMMPlaceStaff) and flags the candidate as being placed. OpenTPW keeps the candidate in the
pool (`ParkStaff.PendingPlacementId`, exempt from expiry); the staff window closes and an OpenTPW hint line replaces the advisor message 0x135
(UI-097). A click on a valid cell calls `ParkEconomy.Hire` and puts the member on the cell centre: state Idle, happiness and energy 100, patrol rectangle
the whole map; the state machines decide the first job on their next update. Right click or Escape (`RmbCancel`) only clears the flag.

## Pick up and drop

The staff window's employee view has a Pick up button (the original's control 0x50e, UI-098), enabled by `CanBePickedUp` (0x505bd0): refused on strike (states 4,
5), in hand (7) and escorting (13, 18: `StaffMember.Escorting`, ECON-052), and when the member's own cell is not type 0, 1, 3, 9 or 10. The member becomes
`StaffState.PickedUp`; a mechanic's ride assignment and a handyman's litter and toilet claims are released at once. The Carry tool drops it on a valid cell
(0x509af0 → 0x505ea0): cell centre, `Navigation = null`, Idle, idle stamp 0, no jobs; the patrol rectangle is untouched. Cancelling drops it back on the cell
it came from; only one carry runs at a time (a new pick-up drops the previous member back); there is no guest pick-up.

Valid cell: inside the map, not unowned, type grass, path, queue or queue end, under no placed object or the prototype ride (GUEST-013: tp.exe `0x46cc40` also
refuses a cell whose track record is a walkable track kind, and refuses object footprints by cell type rather than by occupancy). An invalid click
does nothing and the tool stays. The cursor is `Input.CursorTypes.Carry` (the original's cursor 9; UI-100), an invalid hovered cell shows the blueprint's red
ground marker (UI-099).

## Saves

A member in hand is saved dropped on its old cell (ECON-053); a pending hire is not saved. A member left in state PickedUp without a tool (an imported
original save) is put back to Idle by `StaffPlacement.Reconcile`.

## Drawing

Staff are sprites (`esprites.wad`), not MD2 models: Generic\Handymen `SPR_HA`; Generic\Mechanics `SPR_FA`, `SPR_OM`; Generic\Guards `SPR_GU`;
Generic\Researchers `SPR_RE`; entertainers per theme (Jungle DI, EX, NA; Fantasy FL, GN, LA; Hallow DR, FK, HU; Space MO, SM, SP). The bank is
`(NameIndex + Id)` modulo the folder's banks, where tp.exe draws a variant per candidate (`0x541f70`; GUEST-014). Clips follow the script table 0x75a418: stand slot 0, walk slot 1 (half rate for patrol and
handyman walks), run slot 2, work slot 4 (sweep at half rate, repair an 8-frame loop, research 4 frames then hold), strike slot 5, the dangling cursor
sprite slot 6 frames 0-3, the guard's catch and carry slots 7 and 8. Resting members have no sprite. Entertainer performances are the bank's multi-frame
slots (GUEST-015); frame rates, the moving test and the position smoothing are GUEST-016. `StaffRenderer` draws one quad per member in one draw call with the
guests' sprite shader and atlas loading and allocates nothing per frame once the members are known.
