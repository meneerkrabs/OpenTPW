## UI follow-ups: research knob startup, map, modes, fonts, validator pointers, moon, banner, bridge word (October 29, 2026)

Fidelity round 10, package D2 (the follow-ups of package D, PR #894). Every claim was read in tp.exe; the tpw-decomp research only pointed to where to look.

- **UI-064 (retired).** The research window's workload knob is the slider class whose position setter (vtable slot `0x1c` = `0x66b508` -> `0x66b088`) returns before touching the ball when the raw value is unchanged and the mode is not the refresh -2 (`0x66b201`-`0x66b20d`); the constructor `0x66ad6a` starts raw 0 with the ball at the layout's authored top 399 (row of `0xd2d10`), and no message 0x15 reaches the slider after its base constructor (`0x65bffd`). So an opening with raw 0 keeps the ball at 399 instead of the track bottom 634.
