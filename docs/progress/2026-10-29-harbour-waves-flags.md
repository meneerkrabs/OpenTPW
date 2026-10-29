## Harbour waves and flags (October 29, 2026)

- **The harbour moves (issue #829).** tp.exe builds two dynamic meshes every park
  frame (`0x53C3F0`) that OpenTPW never drew: rolling waves from the level's
  `WaterStartPos` out to sea, in the translucent surf textures `gby_sur1` to
  `gby_sur3`, and eight cloth flags with the Theme Park World logo (`gte_lgo1`)
  on the entrance's flag poles, waving and turned to the wind. `HarbourDynamics`
  ports the wave grid, the cloth grid, the sea's patches, the flags and their
  phases. The weather now keeps the wind's direction and strength
  (`0x511FDF`, `0x512F00`); the wind's turn has its own draw stream, so weather
  rolls are unchanged. The wind is saved with the weather. The sea's texture
  table draws from the game's C runtime `rand()` stream, seeded from the
  tick count at start-up as tp.exe's is, so every run draws its own sea.
