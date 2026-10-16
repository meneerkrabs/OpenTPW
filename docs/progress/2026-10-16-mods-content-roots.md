## Mods can now change what the game reads: overrides, strings and objects (October 16, 2026)

Phase 5 of the mod system builds on the mod core. An enabled desktop mod now takes effect, at the next start, through content roots
(`ModContentRoots`, docs/MODS.md). Loose files in a mod's `overrides` folder replace the game file at the same data-relative path,
members of archives included, through a new `BaseFileSystem.Overrides` hook (textures, sounds, UI art and settings alike; the game's folder
is never written). `strings/<Language>/<NAME>.str` replaces a language's string table through `GameLanguage.StringOverride`. With no
files-based mod enabled neither hook is set, so every lookup, the catalog and the pinned digests are unchanged.

`ObjectCatalog.LoadSteps` no longer knows only one bonus root. Mod objects come from `CatalogContentRoot` entries, each a file system with
WAD and SDT handlers rooted at the mod's folder or at the data folder of a mod input (a user-supplied original install, such as another
original game). `objects.json` lists the archives with theme, category, an Info.Id from the range the mod declares and name strings per
language. It is parsed strictly and bounded, the archive paths cannot leave their folder, and the assigned Info.Id replaces the archive's own.
Default `.sam` files still come from the game data, as for the official bonus objects. Skipped archives (a taken name, a missing folder, no
`.sam`) are warned about.

Conflicts are refused, never resolved by "last wins": two mods that supply the same file need `after`, and overlapping Info.Id ranges or the
same archive name in a theme and category refuse both mods. The catalog cache key now includes the mod set's digest, and a park's asset
manifest keys mod members `mod:<id>:/...` (own folder) and `mod:<id>/<input>:/...` (input), so a saved park pins their content. Recording the
mod list in saves, the mods screen, and the checks and engine work for Theme Park Inc objects (`opentpw.cross-edition-objects`) are later phases.
