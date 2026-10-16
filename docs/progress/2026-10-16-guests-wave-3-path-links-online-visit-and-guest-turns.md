## Guests wave 3: path links, the online visit, first purchases and guest turns (October 16, 2026)

Read from tp.exe via REA; four IDs retired, six narrowed.

Retired:

- **PATH-016** (`0x4D7F3D`, `0x536490`, `0x5348D0`): InitialPath cells are typed and linked in index order with the ported mode-1 link
  writer. The writer sees only the cells before it, so a later neighbour is still type 0; the old code linked toward all four neighbours.
- **QUEUE-008** (`0x4E2440`, `0x4E2478`): game type 1 is the online visit (`SetGameType(1)` at `0x4C0830` and `0x5E2700`, flag word
  `0x7A1A8C`). In that mode a use wears nothing and a dirty toilet is cleaned at once (`ParkEconomy.OnlineVisit`).
- **ARRIVAL-005** (`0x4D3AA0`, `0x4DB690`): the object-control table was already decoded. The shipped Jungle save carries `+0x1C` values
  15 and 604; `OriginalEconomyImport.ImportFirstPurchases` restores them before the objects are registered.
- **QUEUE-021** (`0x524960` mode 0x14, `0x530120`): `0x530120` ends by setting the line start (`0x81EDE4/8`) to the queue's last corner.
  The first click enters tool 0x14, the next cell click collects the queue and enters mode 3, and the line goes on from the back cell.
  The two-click entry is the already-approved `EXT:queue-click-at-back-cell`.

Narrowed:

- **GUEST-010**: the show and performer turns (`0x501020`, `0x500F00`) end the state-6 call before pranks and decision; the off-path
  search for a start cell without links (`0x4F9490`: 8 patterns x distances 0..3, then 5 random cells); the stranded mark (`+0x198`,
  thought Confused, no draw until a block near the cell is stamped). Open: a linked start cell that is not a path cell, and the heading
  angle (11-bit in the original, unit vector here).
- **WALK-003**: staff on a cell now push stepping guests. Open: the entry-order query and which guest states the list keeps.
- **WALK-007** (split): the queue re-evaluation `0x4DE1F0` stamps the back cell's block after queue-tool edits. Still missing after a path over a
  queue end and a queue-cell clear, which need the cell's owner word. The part about route walks restored from an OpenTPW save made before the
  original walk, which end where the guest stands instead of replanning, is split off as the user-approved design choice
  `EXT:legacy-save-route-walk`.
- **GUEST-011**: a paid guest walks in only while the entrance gate's script variable 1 (`VAR_STATUS`, `0x51A290`) is 1. Open: the close
  `0x4DF300` clears a walking guest's call at once (OpenTPW does it at the ride's next update), and the openable flag of shops.
- **GUEST-920**: the counter starts at creation and never stops (`0x4FAFDB`, `0x4FD950`). Open: which guests the visitor list holds.
- **QUEUE-015** (narrowed): the queue tool's entry now fills its stack of corner points the way `0x530120` does
  (`QueuePaths.CornerStack`: the front cell, every corner cell, the back cell, at most 0x401 points; a corner back cell or front cell comes up
  twice, as in the original) and the tool's undo (`0x52FE50`) pops it one segment at a time, also for a queue laid in an earlier session; it
  never takes the front cell and a stack of one point has nothing to undo. A ride's demolition (`0x527EE0`) now takes every queue cell with it
  (before, the cells stayed behind as orphans): the forced clear drops the NoModify front cell too, each cell refunds `Costs.QueueCell` x scrap
  percentage / 100 and the demolition debits one such amount (`ParkEconomy.ChargeQueueDemolition`). Nothing new is saved: the stack is rebuilt
  from the cells at every entry. The corner walk's other effect is ported too (`QueuePaths.DetachFromPath`, `0x530379` to `0x5303A1`): clicking a queue to edit it takes the link
  between its back cell and the path it ends on off both cells, and a cancel restores nothing (the exits `0x52FE50`, `0x52F200`, `0x52F580` only
  reset the tool; the relink pass `0x52FC80` serves the track modes only), so the queue stays unjoined until a line ends on the path again. What stays
  open is the placement counters and the guests set down off removed cells. The removal from
  a clicked cell on is kept only as `EXT:dev-one-click-remove` (the developer's remove tool) and `EXT:queue-backtrack` (Backspace).
- **WALK-008**: records the arrival chain (`0x4FF520`, `0x4FF730`, `0x4FF7F0`, `0x4FFB20`) but does not port it (about 25 sites).
- **GATE-006**: text corrected, since avoid_walls' corner branches are ported (WALK-002 is retired).

Determinism: the pin is unchanged at `0x01946A39A53F851E`. The new stranded mark joins the state hash only while non-zero. Saves gain an
optional `StrandedMark` per guest; old saves restore it as 0 (covered by a test).

Verification (on main with #267 and #268): 5068 tests pass without game data (595 skipped) and 5540 with it (123 skipped), including the determinism run
against main's pin `0x01946A39A53F851E`, which these changes leave as it is; all five native smokes pass (the sandbox smoke now lays a queue and demolishes the
ride, checking that the cells go); the register, progress and evidence checks pass. The corner walk's link clearing is ported (QUEUE-015, above).
