## The waves carry the lazy river's floaters too (October 18, 2026)

The push of a wave machine's waves (`PoolWaves.Push`, which already carries the free swimmers) now also acts on the floaters of a lazy river in the same pool. After its float step a floater gets
the push of the water's currents (`GuestSimulation.PoolCurrent`, the sum of the registered `IPoolCurrent`s, which the free swimmers' step now uses too) through `PoolSwimming.Drift`, only where it
lands in free water of a river cell and 0.7 cell from the other bodies, so it never leaves the lane and its float never breaks. Along the lane the floater surges forward and falls back with the
crests; across it the float's own centring (an eighth of the offset per turn) brings it back to the middle line. A float still ends after its drawn turns and is paid. `[EXT:pools-waves]`
`[EXT:pools-river]`.

Integers only, no draw and no new field. A river without waves, or with waves that are off, floats exactly as before (every other pin is unchanged); the fixed water-attractions run, whose
channel is a river in a pool with a wave machine, moves `DeterminismTests.PoolsAttractionsFixedRunHash` from `0xD9F94E889BDAB453` to `0xE9FD7C4AC1871E89` (92 swims; jets 7, river 15, rope swing 6,
spa 4 starts). New tests: floaters in a river with waves move apart from the same floaters in a calm river, always stay in a river cell's free water and their floats still end; waves that are off
change no position or state of anyone; a save while the waves carry a floater restores and continues identically. `Pools*` and `Determinism*` tests: 318 passed.
