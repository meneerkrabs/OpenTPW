# Staff placement, pick-up and drawing

Traced in tp.exe (Windows Sim Theme Park World v2.0); labels `[BIN:TPW-EXE:...]` in the code cite the routines. Code: `World/Staff/StaffPlacement.cs`
(tools and rules), `World/Staff/StaffSprites.cs` (sprite banks and clips), `World/Staff/StaffRenderer.cs` (drawing), `Hud/ParkHud.Staff.cs` (window and
buttons), `Hud/ParkHud.cs` (`UpdateStaffTool`).

## Hire and place

A hire (0x507bf0) creates no object: it builds the Place tool (CMMPlaceStaff) and flags the candidate as being placed. OpenTPW keeps the candidate in the
pool (`ParkStaff.PendingPlacementId`, exempt from expiry); the staff window closes and the advisor's help 0x135 (309, place your new staff member) is posted as the
original's hire handler does (0x49b9dd); the Place tool withdraws it when it ends (0x46c890). A click on a valid cell calls `ParkEconomy.Hire` and puts the member on the cell centre: state Idle, happiness and energy 100, patrol rectangle
the whole map; the state machines decide the first job on their next update. Right click or Escape (`RmbCancel`) only clears the flag.

## Pick up and drop

The per-employee window (opened from the staff window's employee view) has a Pick up button (the original's control 0x50e, `b_pick`), enabled by `CanBePickedUp` (0x505bd0): refused on strike (states 4,
5), in hand (7), repairing (13: `StaffJob.Repairing`) and escorting (18: `StaffMember.Escorting`), and when the member's own cell is not type 0, 1, 3, 9 or 10. The member becomes
`StaffState.PickedUp`; a mechanic's ride assignment and a handyman's litter and toilet claims are released at once. The Carry tool drops it on a valid cell
(0x509af0 → 0x505ea0): cell centre, `Navigation = null`, Idle, idle stamp 0, no jobs; the patrol rectangle is untouched. Cancelling drops it back on the cell
it came from; only one carry runs at a time (a new pick-up drops the previous member back); there is no guest pick-up.

Valid cell: inside the map, not unowned, type grass, path, queue or queue end, under no placed object or the prototype ride (GUEST-013: tp.exe `0x46cc40` also
refuses a cell whose track record is a walkable track kind, and refuses object footprints by cell type rather than by occupancy). An invalid click
does nothing and the tool stays. The cursor is `Input.CursorTypes.Carry` (the original's cursor 9; `[EXT:software-pointers]`), an invalid hovered cell shows the blueprint's red
ground marker (UI-099).

## Saves

A member in hand is saved dropped on its old cell, as the original's save does (it first ends the carry tool, whose exit `0x46cdc0` drops the member, `0x516c80`); a pending hire is not saved. A member left in state PickedUp without a tool (an imported
original save) is put back to Idle by `StaffPlacement.Reconcile`.

## Drawing

The hire and carry tools draw the person in hand every frame at the mouse's world point, 5 units up, with heading 4 (0x46c480 reads the point 0x7a1c90 through 0x45d610 and places the tool's sprite with 0x475480), also while the pointer is over the HUD; the sprite pass adds the camera's yaw sector to the heading (0x542010), so the person turns with the camera. The sprite, created by the tool's enter 0x46c730 with 0x475a10, is script 0x15 (0x74ed98: slot 6, frames 0-3 looping) of the role's sprite type (mechanics 6, handymen 5, entertainers 4, guards 7, researchers 8 in the table 0x763f88) with the candidate's variant: the person's own bank, kicking its legs. The art is in the `.TPC` the game draws ([GUESTS.md](GUESTS.md)); every staff `.FPC` holds slot 6 as 2 x 2 pixel placeholders, which is why the person in hand was invisible while OpenTPW drew the FPC. The world point is the picker's nearest hit, the terrain's or an object model's (as tp.exe's `0x45bf90` gives it; `World/Original/ObjectRayPick.cs`), and the PC video (81:30) shows the person just below the carry glove, where this placement puts it above the pointer (`GUEST-021`). The hire window closes with the hire (0x49b9dd) and the pick-up button closes the employee window, so no window covers the park while a tool runs.

Staff are sprites (`esprites.wad`), not MD2 models: Generic\Handymen `SPR_HA`; Generic\Mechanics `SPR_FA`, `SPR_OM`; Generic\Guards `SPR_GU`;
Generic\Researchers `SPR_RE`; entertainers per theme (Jungle DI, EX, NA; Fantasy FL, GN, LA; Hallow DR, FK, HU; Space MO, SM, SP). The bank is
the candidate's drawn variant (`0x541f70`), or for a member without one `(NameIndex + Id)` modulo the folder's banks (GUEST-014).
Clips are the scripts of the table 0x75a418 decoded in `StaffScripts` (`StaffAnimation.cs`): stand slot 0, walk slot 1 (the patrol walk shows each frame twice), run slot 2, work slot 4 (the sweep shows
each of 4 frames twice, repair is an 8-frame loop, research loops 4 frames and four steps of frame 0), strike slot 5 (4 frames and six steps of frame 0, looping), the dangling person slot 6 frames 0-3 (from the `.TPC`, as the `.FPC` holds 2 x 2 placeholders there), the guard's catch then carry slots 7 and 8. Resting members have no sprite. Entertainer performances play the bank's performance program (intro and hold: GUEST-015). The sprite clock is
`SpritePlayer`: the staff adapter takes one step per rendered frame past the due time (native 0x475360 instead runs on even 31 ms substeps), 62 ms at creation, and a walking member's interval is 62 over its speed (doubled for the walk, 0x4FA2A0): 20 steps a cell walking, 10 running; the moving test and the position smoothing are GUEST-016. `StaffRenderer` draws one quad per member in one draw call with the
guests' sprite shader and atlas loading and allocates nothing per frame once the members are known. A member's quad gets the original's depth pull of a pickable sprite, as a visitor's does ([GUESTS.md](GUESTS.md#rendering), 0x589B50); the person in hand does not, as the tool's sprite has no pick flag.

## Patrol areas (reviewed October 26, 2026)

A staff member patrols a rectangle of cells, not a route. Every hired member starts with the whole map (corner cell ids `+0x20A` = 1 and `+0x20C` = 0x4000, `0x506E90`; a cell id is y × 128 + x + 1).

**How a player sets one.** Open the employee's window (a click on the person, or a row of All Staff). Its footprint button (`b_route`, a toggle, help 86 "set a patrol area" / 87 "whole park") passes its new state to `0x4B5B50`. Switched on, it holds the member (`0x48C950`), builds the tool object `0x46C580(0x36)` (the generic tool class of the path tool, vtable `0x6FE9E0`), makes it current (`0x46C350`, which sets mouse mode 0x36 and clears the stored corner), closes the window and posts help 193; the mode change withdraws it. Switched off it only clears the member's area (`0x506E90`) and the window stays. The refresh `0x4B5AB0` checks the button when `0x506EB0` finds an area of its own (corner words other than 1 and 0x4000) and posts help 212.

**The two clicks** (`0x524960`, mode 0x36, on the button release `0x46C610`). The first stores the clicked cell (`0x81EDE4`/`0x81EDE8`) and the held member (`0x48C920`). The second orders the two cells (smaller x and y first, larger x and y second, both inclusive, so the same cell twice is a one-cell area), stores both cell ids on the held member (`0x506E50`), clears the stored corner and ends the tool (`0x52F200(0, 0)`). Nothing checks the cells: not a path, not owned land, not reachable. Escape ends the tool as it ends every mode object other than the default (`0x40C180`); the tool object's right-button slots are `ret 8` (`0x46C650`), and `RMBCancelOn` is read only by the camera and ride views (`0x488A00`).

**While it runs** (`0x5234D0`, mode 0x36). Before the first click the hovered cell is marked, after it the four edges of the ordered rectangle (LayLine `0x536100`, op 0x136: the top and bottom rows from x0 + 1 to x1 - 1 unless the rectangle is one column wide, the right and the left column). The markers are blue; they are red when every map cell of the rectangle is unowned land (flag 0x40) or a fence post (`0x532A50`, kept in `0x816D50`, answered by the validator `0x535670` for the op 0x36). The cursor is the default one (`0x52F950` sets none for this mode).

**The highlight.** The staff locator (Ctrl+S) draws the located member's rectangle with the same four lines in blue while the camera follows it (`0x4B3640`, message 0x1E of the locator panel `0x4B3790`, flag 0x1000, staff types 4 to 8 with an own area). The employee window draws nothing.

**What the area does.** Only the wander `0x4F9490`, used by the handyman, mechanic, entertainer, guard and researcher: a member outside the rectangle (`0x506ED0`, inclusive on both axes) first walks to a random reachable path cell inside it (`0x506F30`: up to 30 tries of two draws, "Could not find or reach a destination" and thought 22 when none); inside it the four neighbour candidates of every step that leave it are dropped, and when none is left the same walk into the area runs. Where the area cannot be reached the wander ignores it. The handyman's litter and toilet searches (`0x4D7100`), the mechanic's ride search, the guard's chases and the entertainer's performances never ask for the area, so a member leaves it for a job and walks back afterwards.

**Save and load.** The two words are part of the staff record (`+0x20A`, `+0x20C`); OpenTPW keeps them as the rectangle's cells and reads original saves' corner ids (`OriginalEconomyImport.ImportPatrolArea`).

**Differences kept.** OpenTPW acts on the press, not the release, as its other tools do; its right click (`RMBCancelOn`, UI-012) also leaves the tool; the click is refused off the map where the original stores whatever cell the pointer picked; the tool says its instruction in a message line besides help 193 ([EXT:strings] `PatrolAreaHint`).
