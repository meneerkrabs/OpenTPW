## Original save blocks: block widths, coaster trains, object controls and the camera from tp.exe (October 16, 2026)

A deeper pass over the original save format, read in tp.exe via REA:

- **Block widths** (SAVE-011 narrowed): every block reader of the loader `0x415270` is traced, and the save is now split by the readers'
  own widths (`SaveBlockWidths`): scripts `0x475730`, particles `0x51F7A0`, RideSystem `0x4647A0`, track rides `0x543560`, flying rides
  `0x55E020`, RSSE `0x5597A0`, advisor `0x599D00`, sound `0x51C400` and UI `0x481680`. All 17 widths match the Jungle Easymode save
  exactly; only the World block and a Coasters block with a coaster are still found by delimiter search.
- **Coaster blocks** (SAVE-921, SAVE-922 and SAVE-923 narrowed, SAVE-920 narrowed): the section words (mesh instance `+112`, bank,
  incline, yaw, height, loop rotation), the section flag map read from both the saver and the loader (`0x437DE9`, `0x442CD0`), the header
  (station mesh instance `+0x10`, state word `+8`) and the train payload of the saver `0x437300` (27-byte train records, 11-byte car
  records with rider and seated lists, cars per train from the coaster type) are decoded; the coaster type is identified through the
  attraction whose mesh instance matches, so a block of several coasters now reads.
- **Object controls** (SAVE-007 narrowed): the control manager's table (`0x4D3AA0`, 150 records of 32 bytes) is decoded; its researched
  flag (`+0x10`) is written only by the lab (`0x5031A0`, `0x504630`), and on the Jungle save it agrees with OpenTPW for all 42 items.
- **Camera and small runtime blocks** (SAVE-010 narrowed): zoom, yaw, flags and the points of interest; the clock, vanilla time, game
  system, cheat, advisor scoring and UI count blocks are decoded and reported.
- **Engine-state dumps** (SAVE-010 split): the raw dumps of the original's own engine structures (the Scripts, RSSE, Particles, Sound mixer,
  message-centre, Track ride and Flying ride blocks) are an OpenTPW design choice, labelled `[EXT:original-engine-state-dumps]`; the camera, clock
  and similar decoded blocks stay under SAVE-010. The rebuild of a runtime coaster from a saved Coasters block stays SAVE-920.
- **Also:** a staff sub-type is the sprite variant (`0x763F88`, `0x541D90`; SAVE-004), the pick-up handler `0x505C50` sets state 7
  (SAVE-009), and the third gauge `+0x4C` is write-only (SAVE-006).

The pinned run hash and OpenTPW's own save format are unchanged; the original-save reader gains optional decoded fields. Verified with
4668 tests passing without game data, 5118 with it, and all five native smokes.

Still open: rebuilding a runtime coaster from a saved Coasters block, the World's control manager and macro-AI widths (`0x5179C0`), a few
coaster header and train words, the full ride state word (ECON-023) and the research table order (ECON-017).
