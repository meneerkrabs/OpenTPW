## Scripts and Particles blocks of original saves decoded (October 17, 2026)

The Scripts (`SCPT`, tp.exe loader `0x475730`) and Particles (`PCLT`, loader `0x51F7A0`) blocks of an original park save are now typed records,
`SaveScriptsBlock` and `SaveParticlesBlock`, that consume exactly the width the loaders read (the widths were already traced in `SaveBlockWidths`).
A script record is a 0x118-byte sprite-script VM instance (slot, program counter, entry, state, wake tick, period, position, 19 parameter words, the rest raw by offset).
The particle block gives the pool globals (generator, tick counter, handle generations, density, list heads), the 120 emitter slots (effect id, generation,
life, position, velocity, rates, cap, child, list links, name) and 20 effector slots, and the saved template tables; the emitter slot is a copy of its
template, so the template offsets are the slot offsets. All 11 corpus saves read to the delimiter, the saved effector templates and emitter templates 0 to 100 equal
`Tp2.plb`, and every pool restores into `ParticleSystem` and captures back (`SaveParticleState`). Neither block is applied to a park: the sprite-script table has no
runtime counterpart and the ambient emitters restart from the level (SAVE-013).
