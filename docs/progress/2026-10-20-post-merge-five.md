## Post-merge review of five approximation PRs (October 20, 2026)

A review after merging #470, #495, #506, #517 and #593 checked their claims against tp.exe and fixed what had gone stale:

- **The online pause layout** (UI-013): the park pause menu starts at top 10 with a gap of 5, or at 0 with no gap while the net-session field `+0x60` is set (`0x48c36c`, `0x5b6450`). That field is set only by the login reply "Authorization OK" (`0x5b6363`) and every other store writes 0 (the other login replies, the login teardown and the session resets), so the mapping to the logged-in session is now a `[BIN]` label; UI-013 stays open for the menu font tier.
- **Visitor alignment** (UI-907 retired, UI-914 narrowed): the count cell's constructor `0x48fde0` does store alignment 0 (left) at `0x48fe28`, and the visitors list's row factory `0x4939d0` aligns only the number, cash and time cells. The visitor info window's builder `0x4b6c20` aligns none of its value cells, so its cash and time values are now left-aligned like its counts (they used the list's right and centred alignment).
- **Retained breakdown state** (ECON-023): a `[BIN]` label for `SetState` `0x4e0e60`: states 1 and 4 keep the object closed every tick until a repair sets state 0.
- **Linked access wandering** (GUEST-010) and **directional demolition** (TRACK-007): stale status text fixed. The demolition clears the same link bit as tp.exe (the line step `0x53B280` clears the bit opposite to the step, and `0x52FE50` replays each line backwards), now a `[BIN]` label; TRACK-007 is narrowed to the other junction shapes.

No simulation change and no pinned hash change.
