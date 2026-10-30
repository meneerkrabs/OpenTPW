## Riders import (October 30, 2026)

An imported original save now brings its riders back onto their rides. The ride script's walk, bounce and head tables are read from the RSSE block (loader 0x5597A0), checked against the ride's header and put on the saved game clock, and the guests they name are seated as riders of the attraction (state 16). Coaster riders, bump-ride vehicles and riders, the RSSE object lists, NAME, TRIGWAITANIM tokens and child or sound child scripts are imported too.

- SAVE-002 is narrowed: riders, walk, bounce and head tables are applied; the state-16 update stays a no-op as in tp.exe.
- SAVE-920 is retired (coaster riders), TRACK-032 and SAVE-921 are narrowed, DET-013 and HEADS-007 texts are narrowed.
- No corpus save holds riders, so the import is tested on synthetic saves.

Review round 1 changes: a coaster car list that names a guest the import did not seat drops that train's lists instead of refusing the park; sound child scripts are found by the parent's +0x14 edge (their own parent word stays 0 in a real save); riders of a ride without restored carrier state (rider tables that did not read, tour cars, track rides whose vehicles were refused) are put off the ride and reported; the TRIGWAITANIM token is kept only when a restored channel shows its animation; duplicate bounce entries are accepted as tp.exe's BOUNCE leaves them; and a snapshot taken before the first host step validates the called head the tables name. Side effect of the new import order: the scripts are imported before the guests, so a script's CloseLoaded no longer clears an imported mPersonBeingLoaded.

Review round 2 changes: the saved TRIGWAITANIM token is kept as tp.exe keeps it (the instruction's own channel, decoded at the saved program counter) and is cleared only where the object's saved scene gave that channel no restored control; a coaster car list keeps its valid riders and drops only the stray entries; the track drop covers every kart or water ride whose vehicles were not imported; and the guard sweep runs again after the drop.
