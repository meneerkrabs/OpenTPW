## Pools: water attractions, wave machine, spa and lazy river (October 17, 2026)

Phase P5 of the pools extension ([POOLS.md](../POOLS.md#water-attractions-waves-spa-and-lazy-river-phase-p5)): four own objects per theme made with the content kit and the activities and effects that use them, all on the activity host.

- **Rope swing, water jets, spa pool** (Info.Ids 65200 to 65215, placement rule: pool water on the object's water side; the last water cell of the side cannot be erased): the guest climbs, swings and splashes in; stands under the jets (and every swimmer near them is paid a small spray bonus, with a particle spray); sits in one of the spa's eight seats.
- **Wave machine:** settings (on or off, height, interval) belong to the machine and are stored in the flags of its access cell, so they save and hash with the cell map and the waves go with the machine; waves animate the pool-water shader, swimmers bob on them (presentation only) and are paid a small happiness bonus per wave period. A second click on the selected machine steps through four presets; a real window is not made.
- **Lazy river:** the pool tool's river option marks a ring of cells; the rings and the current's direction per cell are derived from the flags; swimmers in the ring float round it at the current's speed.
- New pin `PoolsAttractionsFixedRunHash`; every earlier pin is unchanged. Saves round-trip in the middle of each activity.
