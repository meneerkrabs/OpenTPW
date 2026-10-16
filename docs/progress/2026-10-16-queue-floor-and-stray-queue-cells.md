## Queue floors above the grass and stray queue cells (October 16, 2026)

- **Queue models stand on the drawn ground** (PATH-015 narrowed): each vertex of a queue model is now raised by the ground
  under it (the landscape sampler over the cell's own diagonal, `0x4527F0`) plus the path tiles' small lift, instead of the
  whole model sitting at the cell's mean corner height. The plank floor the original's footage shows under a queue was hidden
  by the grass, and on a sloped cell the posts sank into it.
- **Stray queue cells can be removed**: a queue cell that no ride's queue reaches (a piece an older save left) is now taken
  by Backspace (`[EXT:queue-backtrack]`) and by the remove tool, alone and without a refund, as the original's `ClearCell`
  (`0x5367A0`) clears one queue cell. Before, both found no owning ride and left the cell.
- The native queue smoke now presses Backspace over the laid queue's last cell through the HUD, and its site search skips
  guest-grid cells outside the level's heightfield.
