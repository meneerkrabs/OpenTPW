## Mod system core and online as the official extension (October 16, 2026)

- **Mods** (`[EXT:mods]`, docs/MODS.md): strict `opentpw-mod.json` manifests (unknown fields rejected, bounded sizes, no paths leaving the
  mod), discovery from `--mod-dir`, `OPENTPW_MOD_DIRS`, `<config>/mods/<id>/` and `<program>/content/mods/<id>/`, player settings in
  `<config>/mods.json`, and one resolution at startup: version and edition checks, required inputs, dependencies and cycles, refusal of two
  mods overriding the same file, a fixed load order (dependencies, official before community, the player's order, id) and a content digest
  per mod. Community mods are data only; nothing consumes their content yet.
- **Online is the official extension `opentpw.online`** (on by default, `[EXT:extension-online-gate]`): switched off (`mods.json` or
  `--disable-extension opentpw.online`) the lobby's globe button and Go Online lines go, Publish Park stays as the original's disabled line,
  postcards and the online command-line switches refuse, and no online session or chat overlay is ever created. The pinned hashes are the
  same with the extension on or off.
