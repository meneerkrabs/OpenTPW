## The carried staff member turns with the camera (October 18, 2026)

A follow-up to the staff member dangling at the cursor. The 4 that the staff tools give their sprite is its heading, and tp.exe's sprite pass adds the camera's yaw sector to it (0x542010). When the camera turns, the person turns with it. OpenTPW had used the 4 as a fixed direction; the dangle now follows the same rule as every other sprite.

Other changes:

- The `.TPC` now fills only the slot's frames that the chosen bank holds as 2 x 2 placeholders, and only when both banks have the same frames, so a set with art of its own keeps it. The atlas keeps those frames and their palette instead of the whole bank.
- A frame without a pointer, such as a key on its own, leaves the dangling person where it was.
- In the native smoke, each staff probe waits for three injected frames in a row and checks the hovered cell before each click. The coaster checks wait while a staff tool runs.
