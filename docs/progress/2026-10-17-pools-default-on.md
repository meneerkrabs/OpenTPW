## Pools on by default (October 17, 2026)

The official extension `opentpw.pools` ([POOLS.md](../POOLS.md)) is now on by default (`DefaultEnabled: true`).

- **Existing configs**: `mods.json` stores only explicit choices, so a player who never touched the pools toggle gets it on after the update, and an explicit `"opentpw.pools": false` stays off. The browser's `/config/mods.json` behaves the same. No migration needed.
- **Determinism**: nothing moves. A park without pool cells or a changing room saves with no `Mods` field and the same bytes with the extension on or off; `FixedRunHash`, `PoolsFixedRunHash`, `PoolsChangingRoomFixedRunHash` and every other pin are unchanged, research tables are unchanged (`"research": false`).
- **Theme Park Inc**: the pools UI is unavailable in a Theme Park Inc run even with the extension on (`FeatureGates.Pools` is false there; the pools objects are not added to its catalog), because styles, changing rooms, swim sprites and layout were made for Theme Park World only.
- **Browser**: `content/pools` is embedded and the defaults apply, so pools is on there too; sprite delivery is unchanged.
- Docs: MODS.md, POOLS.md, RUNNING.md, DETERMINISM.md. Tests: `PoolsTests` (default on, old config, explicit off, Theme Park Inc).
