## Backspace takes back your own last coaster pylon in a shared park (October 28, 2026)

In a shared park the coaster tool's Backspace now takes back the player's own last pylon of the coaster, as single player's Backspace takes the last pylon off, for the host and every guest ([COOP.md](../COOP.md), "Conflicts and undo (C5)"; `[EXT:online-coop-undo]`). This is the second half of C5-3, the third of three parts that reach the game together.

- **Your own pylons, newest first.** Backspace takes back the pylons your clicks placed in this run of the tool, newest first and with no refund, as in the original; a pylon another player placed, or one from an earlier run of the tool, is not yours to take back, and Backspace says so.
- **As long as nothing changed it.** Once a pylon of the coaster is dragged, a stack swapped or the circuit cut, the pylons placed before can no longer be taken back; the ones placed after can. A coaster whose tool closed, or that is complete, takes nothing back.
- **Your claim, held since.** A pylon can only be taken back while you still hold the coaster's claim you placed it under; once the claim went, Backspace says why and ends the tool.
