## A sprite bank and ESP writer, proven on all 121 shipped files (October 17, 2026)

Phase 0 of the content pipeline: original sprite sets (guests, staff, heads) can now be authored and written, not only read.

- **Writers** (`SpriteBankWriter`, `SpriteAnimationWriter`): rewrite every `.FPC`/`.TPC` bank (75) and `.ESP` table (46) of `esprites.wad` byte for byte. The RLE encoder turns every run of two or more equal indices into a repeat, as the shipped files do; `SpriteBankFile.HeaderWord` keeps the one header field the reader used to drop (0 everywhere).
- **Authoring** (`SpriteSetBuilder`): RGBA frames per direction and slot, performance entries and the flag bytes in, `.ESP` plus FPC/TPC out. The paletted format means one shared 255-entry palette per set, exact up to 255 colours and median cut beyond.
- **Tests**: the corpus roundtrip (skips without `OPENTPW_GAME_PATH`) and no-data tests that build a 2 directions x 2 frames set, read it back through `SpriteBankFile` and `SpriteAnimationFile` and load it through `GuestSpriteAtlas.LoadSets` on the CPU.
- **Unverified**: the game's own acceptance of an authored set (the original was not run), run and literal splits at 127 (no shipped row is long enough), and the blend of palette alpha. See docs/GUESTS.md.
