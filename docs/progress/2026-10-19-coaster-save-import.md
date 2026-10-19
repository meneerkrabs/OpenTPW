## Original saves with a roller coaster load (October 19, 2026)

A Theme Park World park saved with a built roller coaster now loads instead of being refused (S10, P1 and P2 of the
coaster import). `OriginalCoasterImport` restores each saved coaster as tp.exe's loader `0x437B60` does:

- the block binds to its station through the station's mesh instance id;
- the pylon nodes get their loop pairs, flags, loft, yaw, bank, incline and loop rotate;
- a complete track gets its end node, and a cut keeps its two ends;
- the state, ride mode, power factor and seats are restored;
- the trains in service stay at their saved positions, speeds and flags, because the first rebuild after a load keeps them
  (`0x43C450` while flag `0x4000` is set).

The first frame after the load runs the controller's first motion tick, which takes no time (`0x43CE85`): the trains stay
where they were saved, while a speed above the cap is capped, one below 2.0 decays by 0.95 and one below 0.01 stops. This
is the first tick of every controller, so a newly built coaster now starts the same way (`CoasterRide.MotionClockStarted`).

When the station's saved script holds the coaster's slot, the script loop's speed setter applies the script's speed
word after the script's first slice after the load (one pass in eight by script id, `0x5516E1`, `0x5517D7`). That re-rates the track and lays the trains out again, as the original's next rebuild does. A park whose coaster
binds to no station, where the original crashes (`0x437CD5`), is refused with the reason, and so are shapes the
original's editing never leaves. The riders on the trains are not restored yet: SAVE-920 is narrowed to them, and they
wait for the riding-guest import (S2).

No real Theme Park World save with a coaster exists, so the import is verified against tp.exe's reader and against
synthetic blocks. A test-only writer puts OpenTPW-built coasters into the original layout, and hand-made blocks cover
the edge cases. The Theme Park Inc prebuilt coaster serves as a real-byte cross-check. No pin moved, and none was added: a
coaster's content hashes differently on macOS arm64 and on x64 (libm), so the coaster tests assert relations.

Review change: a coaster that never ran (state 1, mode 16 or 32, power 0, as tp.exe's zero-filled constructor saves it) imports instead of being refused.
