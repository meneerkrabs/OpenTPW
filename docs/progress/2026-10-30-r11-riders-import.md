## Riders import (October 30, 2026)

An imported original save now brings its riders back onto their rides. The ride script's walk, bounce and head tables are read from the RSSE block (loader 0x5597A0), checked against the ride's header and put on the saved game clock, and the guests they name are seated as riders of the attraction (state 16). Coaster riders, bump-ride vehicles and riders, the RSSE object lists, NAME, TRIGWAITANIM tokens and child or sound child scripts are imported too.

- SAVE-002 is narrowed: riders, walk, bounce and head tables are applied; the state-16 update stays a no-op as in tp.exe.
- SAVE-920 is retired (coaster riders), TRACK-032 and SAVE-921 are narrowed, DET-013 and HEADS-007 texts are narrowed.
- No corpus save holds riders, so the import is tested on synthetic saves.
