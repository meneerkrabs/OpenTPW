## A claim keeps the path cell linked into its ride's entrance (October 27, 2026)

In a shared park, clearing a path cell that links into a ride's entrance cell runs that ride's re-evaluation, as the original's clear does (tp.exe `0x53694B`): the ride's queue is recomputed, a block is stamped, and a closed ride may open. Until now another player could do that to a ride someone else claimed, for example with a remove click or Backspace on the path in front of a claimed kart ride. Such a cell is now the claim holder's alone, as the path cell a claimed queue joins already was ([COOP.md](../COOP.md), "Building by every player" and "Claims"; `[EXT:online-coop-claims]`).

- **What refuses it.** These are refused for every other player, the host included, in the claim's words ("Another player is editing this ride."):
  - the remove click, Backspace and the host's single-cell removal;
  - the undo of a path line that built the cell;
  - native queue segments laid over it, and their undo;
  - placements: the paths under them, a queue ride's first queue cell, and a fresh kart or water station's exit and entry tiles;
  - kart or water track lines over it.

  The area clear leaves the cell standing.
- **Only a real link counts.** The rights ask exactly what the clear does: a path cell with a cardinal link into an entrance cell whose owner word belongs to the claimed ride. These stay anyone's: a path cell beside the entrance that does not link into it, a queue cell there, the queue tool's per-cell line (it lays no queue over path), a native segment that only ends on the cell, a path line along it, and an undo or a Backspace that only counts the cell down. The last rule now also holds for the path cell a claimed queue joins: a Backspace on a cell laid more than once only removes one placement, so it takes no link off.
- **One helper.** `CoopBuildRights.BesideClaimedEntrance` was first written for C5's take-backs, on a branch that is not merged yet. This change brings it to main with the path-cell and link conditions above. Every command that clears a path cell now asks it, except the coaster tool's cell markers (below).

Verification:
- Four new tests:
  - a session test of the guest's remove click and Backspace beside a kart ride the host claims: they are refused with the claim's note and nothing changes; once the claim is let go, both take the cell and the ride is re-evaluated on both parks;
  - a rights test over every clearing command, for a guest, the host and the claim's holder;
  - an execution test of the area clear;
  - a preflight test of the fresh station's exit and entry cells, for both track families and all four rotations.
- All co-op tests pass.
- Each of twelve parts of the change, reverted on its own, fails at least one test.
- A Codex review found two needless refusals, now fixed: a native segment's last cell, and a Backspace that only counts the cell down. The queue tool's per-cell line, which never clears a path cell, is no longer asked either.

Track lines and the fresh station's tiles are still asked as drawn, also past a tile their writer would stop at, as for a claimed queue's join.

Still open: the coaster tool's cell markers, which clear a path cell where they land, ask no claim for it, as before for a claimed queue's join.
