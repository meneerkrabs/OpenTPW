## Guests climb a pool ladder on the spot (October 17, 2026)

- The pool ladder no longer makes a guest glide diagonally out of the pool: the swimmer goes to the foot of the ladder (0.8 cell from the land cell's middle, just off the wall and in front of the rungs, measured on the ladder model), faces it, climbs for 8 turns without moving while the drawn feet rise from the water to the deck, steps over the coping onto the deck (walk slot, about 6 turns) and then stands, walks to the rim, jumps in and splashes as before.
- The lounger's climb-out uses the same foot, the same on-the-spot climb and then a short walk onto the seat, the feet rising onto the cushion; the diving board's stair climb is unchanged.
- The ladder and lounger steps are renumbered (0 to 6); `PoolExtrasFixedRunHash` moved to `0xCE55DFBF312FF20A` for that reason only (docs/DETERMINISM.md); the original game has no pools.
- New tests pin the model measurements, the unmoving climb with monotonically rising feet, the short step onto the deck and the completed activity. The result still needs a look in a native capture.
