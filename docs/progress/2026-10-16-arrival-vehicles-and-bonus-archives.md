## Arrival vehicles and bonus object archives from tp.exe (October 16, 2026)

Guests now arrive and leave by the level's own bus, seaplane or Ferry instead of stepping off with no vehicle (read in tp.exe via REA):

- **Vehicle by group size** (`0x4cf3e0`, signed compares at `0x4cf48c`/`0x4cf49a`): a bus up to 35 guests, a seaplane up to 60, the Ferry
  above, falling back to Ferry, seaplane, then bus when the level lacks one. A new vehicle is placed with status 1; one already in the
  park gets its trigger (variable 0) set. Guests step off one per turn while the vehicle reports status 2 (`0x51a2f0`, `0x51a690`,
  `0x51a9d0`); status 6 is written back to 0 and the vehicle released. A guest waiting to go home calls a vehicle when none is at work and
  boards at status 4 (`0x500bd0`). Timing comes from the shipped scripts: the jungle bus reaches its stop 10,033 ms after it is placed
  (2 s, the Busm1 clip's 7,333 ms less the trigger's 300 ms, then 1 s). ARRIVAL-001 and RIDES-019 are narrowed; new parks get only Gates
  and Lights at setup (`0x5156a0`), and vehicles are created the first time they are sent, found by archive name (`0x413d9b`).
- **Bonus object archives** (RIDES-024 retired): the category scan `0x413930` runs once per category (Rides, Features, Upgrades, Shops,
  Sideshow), reading game and bonus archives as one directory in name order; archive names drop `.wad`, a two-underscore name is cut at
  the second underscore and loses a leading `p` (`0x41ec30`, `0x41eb90`); a name the category already holds is skipped (`0x41ede0`), and
  lookups by id return the first match (`0x412e90`).

The pinned determinism hash is unchanged (the synthetic park has no vehicles); the one new world draw, a pick-up vehicle when none is at
work, comes from the economy stream as DET-014 records. Saves gain the optional `GuestSimulationSnapshot.ArrivalVehicle` (0–2); older
saves load with no vehicle at work and re-adopt an existing Bus. Verified with 4300 tests passing without game data, 4734 with it, and all
five native smokes.

Still open: leaving guests wait at the lane's first cell rather than walking to the vehicle's stop cells (`0x51aad0`, state 20), boarding
order per stop cell, the arrival update running before rather than after the turn's guest updates, imported original saves starting with
no vehicle at work, and what sends the End vehicle's message (`0x516b00`, message type `0x11`).
