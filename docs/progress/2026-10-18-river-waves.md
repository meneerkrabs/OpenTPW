## The waves carry the lazy river's floaters too (October 18, 2026)

The push of a wave machine's waves (`PoolWaves.Push`, which already carries the free swimmers) now also acts on the floaters of a lazy river in the same pool. After its float step a floater takes
the push of the water's currents (`GuestSimulation.PoolCurrent`, the sum of the registered `IPoolCurrent`s, which the free swimmers' step now uses too) projected on its cell's flow axis
(`RiverActivity.WavePush`), and only into its own cell or that cell's next or previous cell of the same ring, in free water and 0.7 cell from the other bodies (`PoolSwimming.Drift`). So a floater keeps
to the middle line of its lane, never jumps to another lane of a small ring and never breaks its float. The waves run along +x +y: on a lane that runs with them a crest carries the floaters forward, on
one that runs against them it holds them back (at full swell of height 3 the push, about 0.085 cell a turn, is a little more than the current's 0.071, so for a few turns they drift back). A float still
ends after its drawn turns and is paid. `[EXT:pools-waves]` `[EXT:pools-river]`.

Integers only, no draw and no new field. A river without waves, or with waves that are off, floats exactly as before (every other pin is unchanged); the fixed water-attractions run, whose
channel is a river in a pool with a wave machine, moves `DeterminismTests.PoolsAttractionsFixedRunHash` from `0xD9F94E889BDAB453` to `0x2D7EE079E5A9B09F` (90 swims; jets 12, river 12, rope swing 4,
spa 6 starts). New tests: the push's projection on the lane for each side of a ring and its refusal outside the cell's ring neighbours; floaters in a river with waves stay in a river cell's free water,
as close to the middle line as in a calm river, and their floats still end; waves that are off change no position or state of anyone; a save while the waves carry a floater restores and continues
identically.
