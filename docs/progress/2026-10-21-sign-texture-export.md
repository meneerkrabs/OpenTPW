## Debug export of every sign texture in one language (October 21, 2026)

A translator of the PS2 version asked for the Spanish sign textures as images. `--export-signs <language> <output directory>` writes them: a debug tool, no game behaviour changes.

- **What.** For each of the four themes the park gate (its OBJECT_NAMES row, as a new park's gate starts) and every object archive with a `.sgn`, plus the lobby's four island and gate signs: `<theme>/<object>-1.png` (the `sign1` texture), `-2.png` (`sign2`) and a 256 x 128 preview, with `index.json` (theme, object, archive, `.sgn`, info id, string ids and texts, drawn lines, file, size, RGBA and PNG SHA-256). English: 84 signs, 252 files.
- **Same pixels as the game.** The textures come from the composers the level and the lobby use (`ObjectSigns.Get`, the gate's lines and composition, now shared through `OriginalGateSign.ComposeHalves`, and `LobbySigns.Compose`); a with-data test decodes every exported texture and compares it byte for byte with them.
- **Safe to run.** Headless and offscreen (no window, audio, park, mods or bonus objects); the language is found as for a normal start and a missing one is an error before anything is written; the output directory must be new or empty and outside the game and language data, names from data are sanitised and every path is checked to stay inside it.
- Documented in `docs/RUNNING.md`, "Sign textures (debug export)", and `docs/LANGUAGES.md`. Unit tests cover naming, collisions, path containment, the output-directory rules, the preview layout, the index and the command line.
