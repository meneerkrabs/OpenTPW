## Pool swimmers keep their distance (October 17, 2026)

Guests in a pool no longer swim through each other ([POOLS.md](../POOLS.md), "Spacing of bodies in the water").

- **Free swimmers**: they keep 0.7 cell from every body in the water (was 0.25) and steer away from 1.2 cells (was 0.5). Picked from captures of a crowded jungle pool; 0.55 was tried first and still overlapped on screen. The ways out stay: a move that increases the distance is always allowed and the crowd at the steps is ignored within 0.7 cell.
- **Everyone in the water is an obstacle**: states 22 and 23 and the state-25 guests whose activity says it is in the water (`IPoolActivity.InWater`, default false): river floaters, guests under a jet, the approach and the splash of the ladder, board, lounger and rope swing.
- **Activities**: river floaters keep a gap along the lane (drift at a quarter of the current's speed when another body is ahead within 0.8 cell, stand within 0.6); jets give each guest a spot 0.7 cell from the others; landings (ladder, lounger, board, rope swing) wait at the top, at most 40 turns, while another body is within 0.7 cell of the landing point. Slides are not touched.
- **Capacity**: one body per water cell, at least 2 (was 2 and 4); floaters and guests under a jet count.
- **Performance**: bodies are found through a per-tick 2 x 2 cell bucket lookup in guest-list order instead of a scan of every guest per swimmer.
- **Determinism**: no random number is drawn and all of it is integer or 16.16. `FixedRunHash` and `PoolsFixedRunHash` did not move; `PoolsChangingRoomFixedRunHash`, `PoolExtrasFixedRunHash` and `PoolsAttractionsFixedRunHash` moved on purpose (DETERMINISM.md, "Spacing of bodies in the water").
- **Smoke option**: `--debug-pool-lead N` sets the frames between painting the pool and the capture, so swimmers can swim before it. Captures: `reports/pools-spacing-crowd.png`, `reports/pools-spacing-river.gif`.
- Tests: `PoolsSpacingTests`.
