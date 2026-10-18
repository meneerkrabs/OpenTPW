## Land waterfall and teleport beams from tp.exe (October 18, 2026)

PTCL-908 (the particle callers the register still listed) was triaged against the game data and tp.exe; the register text had gone stale, because the vehicles' break smoke 2, boat spray 96, launch and unload beam 15, the walk beam 19 and the tour cars' smoke and fire were already wired. What was missing and is built now:

- **The land's effects** (`0x550e00`, type 2 records of `scape.omp`): Jungle's waterfall (effect 20) is started with the level, as the original does at every level load; every saved jungle pool already held it, so only a new jungle park lacked its splash. Captured in the native jungle level.
- **Rider beams** (`0x549C60`, `0x54AC70`): the bumper cars raise particle 19 for each rider hung or taken off with the beam flag (loading with BUMP 12, the session's unload), only for seats with a head dummy; the creation and the retire pass no flag.
- **Guest teleport beams** (`0x501db0` states 15 and 16, `0x4fa470`): a guest of a ride with `UsageInfo.RequiresTeleport` (20 shipped rides) beams at its place when it boards and when it leaves.
- **Narrowed**: the removal's mode 2 is the world's clear loops (modelled), no shipped fixed item or script reaches the footprint-less paths, no shipped script names a scoped effect literally, and the one type-2 effect with a negative key (Fantasy's bugstv) reads uninitialised stack. The staff, guest-creation, lobby key-panel and UI pool callers are listed with their addresses in docs/PARTICLES.md.

Presentation only: nothing is saved or hashed, the determinism pin is unchanged. `ParticleEmitterCensusTests` pins what the shipped data reaches.
