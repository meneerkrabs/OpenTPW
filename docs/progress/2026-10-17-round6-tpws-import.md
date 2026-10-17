## Original saves apply their scripts, particles, research, camera and litter (October 17, 2026)

Importing a TPWS/TPWI save now applies more of what the file holds. The RSSE chunk gives the running scripts their saved ids, the saved next id and
the saved pass counter, and the loaded script list is the reverse of the saved order, as the loader at `0x5597A0` builds it, so the 8-pass cadence
(`(id xor pass) & 7`) matches the save. The Particles block is applied to the particle renderer. The saved research groups (highest open group per
lab project) are restored, adopted challenge slots no longer fail verification, the gate name, the open word of rides, the staff member's variant,
the camera pose and the cell grid's litter (with a sweeping handyman keeping his claim) are imported. `--original-save` starts an original level
from a player save.

FINDSCRIPTRAND (`0x5560CB`) counts and picks from the head of the script list, so a script that finds one of several alike now picks the n-th match
from the highest id down; this moved every pinned determinism hash (see docs/DETERMINISM.md). SAVE-005 is retired; SAVE-004, 006, 007, 009, 010 and
013 keep narrowed text for what is still unknown (script run state, the sound block, the HQ limbo id, unheld staff claims).
