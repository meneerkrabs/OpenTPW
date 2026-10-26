## Pause owners, the lost park and the first day (October 26, 2026)

Fidelity round 9, package A (ECON-030, UI-096, ADVISOR-017, PATH-017, ECON-063). Work in progress; this fragment is completed with the last commit of the branch.

- **ECON-063 retired: a new park posts its first day message.** tp.exe's calendar constructor `0x4F7E80` stores day cache -1 and the world init `0x5156A0` syncs only the month and year caches, so the first update `0x4F8260` posts one day message (`0x40F4C0`) whatever the date. `ParkEconomy.PendingInitialDay`, which only the restart imports and own saves made before the first turn carried, is now also set for a new Theme Park World park (Theme Park Inc keeps its synchronised start). The first turn therefore rolls every object's six rings and runs the day listeners once more than before, which moves the fixed-run determinism pins (below).
