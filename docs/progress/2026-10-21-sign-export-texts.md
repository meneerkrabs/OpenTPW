## Sign export: replacement texts from a file (October 21, 2026)

The PS2 translator wants the signs drawn with the PS2 disc's own Spanish names, which differ from the PC language data. `--sign-texts <file>` (opt-in, only read) gives the export replacement texts per sign.

- **Format.** UTF-8 JSON: a `signs` array of `{ "theme", "object", "lines": [two strings] }` or `{ "theme", "object", "name": "one string" }`, keyed exactly as `index.json` names a sign (the gate is the object `gates` of its theme, the lobby's signs are theme `lobby`). Strict: unknown members, duplicate keys, both or neither of `lines`/`name`, control characters and keys that match no exported sign are errors (all unknown keys listed) before anything is written.
- **Game rules.** A `name` is trimmed and split at the space nearest its middle, the rule `ObjectSigns.Lines` uses for a sign with only a display name (a with-data test checks it against every shipped object's own lines); the sign is composed again from its own `.sgn` by `SignTextRenderer.ComposeSign`, and a test compares the textures byte for byte.
- **Index.** An overridden sign has `textSource` `override (<file name>)` plus `originalText`, `originalLine1`, `originalLine2` and `originalTextSource`. Without the option nothing changes; with it only the named signs differ.
- Documented in `docs/RUNNING.md`, "Sign textures (debug export)".
