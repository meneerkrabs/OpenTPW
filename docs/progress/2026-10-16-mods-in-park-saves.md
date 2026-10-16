## Park saves record the mods that change the simulation (October 16, 2026)

Phase 4 of the mod system (docs/MODS.md, docs/DETERMINISM.md "Mods"). `ParkResumeSnapshot.Mods` lists the simulation-changing
extensions and mods that were active (id, version, content digest). It is null and left out of the file when there are none, so
unmodded saves are byte-identical to before; a test pins that. Loading needs the same mods, versions and digests in the same
order and no other simulation-changing mod; an extra presentation-only mod or the online extension is fine. A mismatch refuses
with a named reason such as `needs community.tpi-rides 1.0.0`, and the load and Continue menus keep such a save listed with
"requires mod X" in the six languages instead of hiding it. The shape check bounds the list, the ids and the hex digests and ties
every `mod:<id>:` asset key to a recorded mod.

`ObjectCatalogEntry.IsBonus` and the bonus prefix generalise to a `ContentRoot` (Data, Bonus, Mod(id)) with the asset-manifest key
prefix `mod:<id>:/...`; nothing loads a mod's archives yet, that is the content-root phase. The catalog cache key includes the mod set
digest, `ParkSharing.ExportLevel` refuses while a simulation-changing mod is active, and tests check that an empty set, an
online-only toggle and a presentation-only mod keep the pinned digests.
