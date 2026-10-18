## Land waterfall and teleport beams from tp.exe (October 18, 2026)

PTCL-908 (the particle callers the register still listed) was triaged against the game data and tp.exe; the register text had gone stale, because the vehicles' break smoke 2, boat spray 96, retire beam 15 and a kart's first-draw beam 15, the walk beam 19 and the tour cars' smoke and fire were already wired. What was missing and is built now:

- **The land's effects** (`0x550e00`, type 2 records of `scape.omp`): Jungle's waterfall (effect 20) is started with the level, as the original does at every level load; every saved jungle pool already held it, so only a new jungle park lacked its splash. Captured in the native jungle level.
- **Rider beams** (`0x549C60`, `0x54AC70`): every bump vehicle raises particle 19 for each rider hung with the beam flag at the BUMP 12 load (`0x549B80` is shared by all families), and the bumper cars again for each rider taken off at the session's unload, only for seats with a head dummy; the creation and the retire pass no flag.
- **Guest teleport beams** (`0x501db0` states 15 and 16, `0x4fa470`): a guest of a ride with `UsageInfo.RequiresTeleport` (20 shipped rides) beams at its place when it boards and when it leaves.
- **Narrowed**: the removal's mode 2 is the world's clear loops (modelled), no shipped fixed item or script reaches the footprint-less paths, no shipped script names a scoped effect literally, and the one type-2 effect with a negative key (Fantasy's bugstv) reads uninitialised stack. The staff, guest-creation, lobby key-panel (`0x4b8ee0` too), message-tag (`0x48de1a`, `0x48dfbe` → `0x486b90`) and UI pool callers are listed, as is the first-draw beam 15 of non-kart vehicles (`0x546a89`), which OpenTPW does not raise; with their addresses in docs/PARTICLES.md.

Presentation only: nothing is saved or hashed, the determinism pin is unchanged. `ParticleEmitterCensusTests` pins what the shipped data reaches.
