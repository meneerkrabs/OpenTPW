## Track edit tool, track demolition replays and coaster precision (October 18, 2026)

The track edit tool of a ride with a closed circuit is built from tp.exe's click handler `0x524960`: the first click picks a tile, the second picks another, and the track between them is cut (`0x52B400`, `0x53B280`, `0x53BC60`) with the station repaired afterwards. A cut that leaves the circuit open continues as the line tool from the open end, a valid walk restarts the pieces, and an invalid one stops them (`TrackWalkCause.Edit`, also used after an add-on is placed). The hover marks the tiles the cut would take. `TRACK-014` is retired because its readers, the edit sub-modes, now exist; `TRACK-018` is narrowed to Backspace's replay and the ride's reopening.

Track demolition now replays the two chains of tiles from the station as the original does, so a branch or detached piece stays in the park and refunds nothing, and the refund follows each tile's pass counter (`TRACK-007` narrowed).

The pylon cost, the suspended swing, its cosine index and the loop knot factor now round to a single at every operation, because the original thread runs its x87 at 24 bits (`COAST-032` narrowed). A coaster's cost can differ by a rounding step from before, but no corpus save holds a coaster to compare. `COAST-011` is retired: the option table has no reader in tp.exe.

A cache-hit texture copy now keeps its path, so releasing it no longer disposes the shared native texture; this fixed a Metal crash after drawing blueprint markers. `TRACK-005` and `TRACK-008` are narrowed with the line ops and the add-on walk read.
