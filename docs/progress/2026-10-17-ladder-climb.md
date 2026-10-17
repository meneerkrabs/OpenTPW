## Guests climb a pool ladder on the spot (October 17, 2026)

- The pool ladder no longer makes a guest glide diagonally out of the pool: the swimmer goes to the foot of the ladder (0.78 cell from the land cell's middle, right at the rungs, measured on the ladder model), faces it, climbs for 8 turns without moving while the drawn feet rise from the water to the deck, steps over the coping onto the deck (walk slot, about 6 turns) and then stands, walks to the rim, jumps in and splashes as before.
- The lounger's climb-out uses the same foot, the same on-the-spot climb and then a short walk onto the seat, the feet rising onto the cushion; the diving board's stair climb is unchanged.
- The ladder and lounger steps are renumbered (0 to 6); `PoolExtrasFixedRunHash` moved to `0xA07E1347C234D5E1` for that reason only (docs/DETERMINISM.md); the original game has no pools.
- The four ladder models were regenerated (`add_ladder`): the rails curve over the coping and go down along the pool wall into the water, with five rungs 1.7 units apart from above the deck to below the water line, at the guest's foot point; only the ladder MD2s changed (textures, .sam and .rse are byte-identical).
- New tests pin the model measurements, the unmoving climb with monotonically rising feet, the short step onto the deck and the completed activity. The result still needs a look in a native capture.
