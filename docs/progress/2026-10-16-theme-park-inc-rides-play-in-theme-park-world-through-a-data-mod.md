## Theme Park Inc rides play in Theme Park World through a data mod (October 16, 2026)

The cross-edition objects extension and the content roots now meet: a data mod that lists Theme Park Inc archives (two small text files,
nothing of the install copied) puts them into a Theme Park World park, where they are listed, priced, researched, bought, placed, run and saved.

- **Loading** (`[EXT:mods]`, `[EXT:cross-edition-objects]`): an input that the manifest declares as `theme-park-inc` and that
  `GameEditions.Detect` recognises is registered as a sound root while the extension is on, so the objects' `EVENT` ids play from the install's own
  banks; the economy catalogue (`EconomyObjectCatalog`) adds the mod's objects after the game's, with the archive's own price, research and group, the
  `Info.Id` of `objects.json` and its English name; the shared textures of such an object come from the install's world folder it was authored in
  (`levels/science/sharetex`), not from the park's theme. Content roots are made once per mod digest, folder and input, so cached catalogues keep the file system the sound
  roots are registered for. With no mod active none of this runs and the pinned digests are unchanged.
- **Textures of another file system**: bonus, tour and track objects decoded every non-game texture as `.wct`; `Texture.DecodeImage(fileSystem, path)` now
  follows the extension (`.wct`, `.fsh`, stb images such as the `.tga` files inside Theme Park Inc's WADs). The native smoke caught this
  (`WCT dimensions must be positive`).
- **Litter bins**: a guest's litter sets the bin script's variable at index 0, as the original does (tp.exe `0x4FFF20` through `0x55A0B0`); Theme
  Park World's bins name it `VAR_TRIGGER`, Theme Park Inc's `VAR_USE`. Same behaviour for Theme Park World's four bins.
- **The Theme Park Inc only opcodes**: none of the ride, shop or sideshow scripts has `WAITABS` or `MULT`; they sit in the scenery obstacles and in
  the litter bin, behind its `VAR_BOMB`. The test drives the bin's variable and the VM runs `WAITABS` inside a placed object. Variables only Theme
  Park Inc's own engine writes (`VAR_FPBYPASS` in 69 scripts, `VAR_SPEED`, `VAR_SPECIAL`, the bin's prank and fire variables, ...) stay 0:
  `[APPROX:RIDES-045]`.
- **Command line**: `--enable-mod <id>` and `--mod-input <mod id>:<input id>=<folder>` (for one run, also in smoke tests).
- **Tests and smokes**: `TpiRidesWiringTests` (synthetic) and `TpiRidesEndToEndTests` (over `OPENTPW_TPI_PATH` and `OPENTPW_GAME_PATH`: the
  example mod generated at test time, every archive of the install, catalogue, economy, buy list, textures, sound routes, the gate's guests boarding a
  Theme Park Inc ride in two parks, save records). `scripts/smoke-tpi-rides.sh` runs the sandbox smoke (it builds the mod's objects) and the
  park-resume smoke pair whose fixture ride is the mod's Turban Bounce, built through the HUD, saved and restored (`OPENTPW_SMOKE_MODS=1`).
- **Two tests that failed on an extracted install, explained**: `ThemeParkIncKeyedFramesUseItsEditionSpecificTextureDecoders` took the folder it was given
  as the Data folder (an extracted CD has `Data` inside it; the other original-data tests find it), and
  `ReportFixedTpwKeyCoverageSeparatelyFromDynamicLoanAndChallengeTables` pinned the size of the repository's own test schema (152 keys, 167 after the
  staff and queue rules), which has nothing to do with the install; it now pins a floor.

How to make such a mod: docs/MODS.md, "Making a Theme Park Inc rides mod".
