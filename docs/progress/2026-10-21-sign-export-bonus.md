## Sign export: official bonus rides on request (October 21, 2026)

The PS2 version ships 14 rides that are official bonus rides on the PC; the translator needs their signs too. `--export-signs` now takes `--bonus-data <folder>` (or `--with-bonus` for the folder a normal start uses) and then exports each theme's bonus archives with a `.sgn` after its shipped objects.

- **Same lines and pixels as the game.** A bonus sign is composed by `ObjectSigns.Get` with the lines of `ObjectSigns.Lines`: the `SIGNA`/`SIGNB` of the archive's `<language>.txt` (all 14 rides have Spanish and English files), or the display name without one. A with-data test compares every bonus texture byte for byte with the game's composer and checks the shipped signs are unchanged by the bonus objects.
- **Opt-in.** Without the option the PNGs are byte-identical to before; `index.json` gains a `textSource` per sign (`OBJECT_NAMES.str ids`, `OBJECT_NAMES.str gate row`, `<language>.txt SIGNA/SIGNB`) and `"bonus": true` on bonus signs. Mod and extension objects are never exported; the bonus folder is protected like the game data.
- Documented in `docs/RUNNING.md`, "Sign textures (debug export)".
