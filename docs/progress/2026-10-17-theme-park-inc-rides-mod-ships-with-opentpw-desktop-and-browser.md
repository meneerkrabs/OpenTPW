## The Theme Park Inc rides mod ships with OpenTPW, on the desktop and in the browser (October 17, 2026)

The data mod that brings Theme Park Inc attractions into Theme Park World is no longer an example to write by hand: OpenTPW ships it as
`opentpw.tpi-rides` (`content/mods/opentpw.tpi-rides/`, a manifest and an `objects.json`, no Theme Park Inc bytes), found like any other program
content, off by default, and it works in the browser build too.

- **The mod** (`[EXT:mods]`, `[EXT:cross-edition-objects]`): depends on the cross-edition objects extension and asks for the input `tpi` (Theme Park Inc
  data). It lists 183 objects: every archive of the retail install's `rides`, `shops`, `sideshow`, `features` and `upgrades` folders that the parks can
  load (182 of 217; the rest are the names the park already lists, or `ptract`, which declares no `Info.Id`) and the science litter bin, each in the
  Theme Park World theme of its world, with ids 40001 and up from the reserved range 40000 to 40499. English names are derived from the archive names;
  the first four keep their hand-written names and ids. `TpiRidesShippedModTests` compares the list with the archives the install really loads and
  regenerates it with `OPENTPW_WRITE_TPI_RIDES=1`.
- **Desktop**: `content/` is copied to the build output and to every release (`OpenTPW.csproj`), so discovery finds the mod in the program folder; it
  shows in Options > OpenTPW > Extensions and mods, and with the extension, the mod and the install chosen as the input it runs. The test fixture reads
  the shipped files (`TpiRidesFixture`), so the existing end-to-end tests (catalogue, economy, buy list, textures, sound routes, `WAITABS`, the gate's
  guests, the save records) now run against the shipped mod; `scripts/smoke-tpi-rides.sh` enables it instead of writing a copy.
- **Browser** (`[EXT:mods]`, `[EXT:mods-ui]`): the mod is embedded in the page like the other content, `ModStartup.InitializeBrowser` discovers and resolves it
  with `/config/mods.json` (kept in IndexedDB), and the Extensions and mods screen works there (it resolves at once, inspects in process and has no
  typed path or Remove). The input `tpi` is satisfied by the page's copy of the player's own Theme Park Inc files below `/inputs/theme-park-inc`
  (`BrowserModInputs`): the setup page's *Theme Park Inc files for mods* button (and **Change...** in the screen) copies only what the mod reads (324 files,
  74 MB of 400 MB) into memory and into a second origin-private store, so the Theme Park World and Theme Park Inc files coexist, each with its own
  manifest, and a later visit restores them before the mods are resolved. A copy an active mod reads is not replaced during a run.
- **Review fixes**: the browser copy is staged and published only when complete and recognised (`BrowserModInputs.Publish`: file count,
  `GameEditions.Detect`, `Ver.txt`, per world a ride archive and shared textures, the global sounds), Play waits for an import, a failed copy removes
  both trees, every outcome (a refused folder too) reports one result so the screen never stays disabled, a restore verifies every SHA-256 and clears
  an incomplete store, `.gitattributes` pins LF for `content/mods/**` and a golden content digest pins the shipped files.
- **Tests**: `TpiRidesWiringTests`, `TpiRidesShippedModTests`, `ModsInBrowserTests` (browser discovery, defaults, the lock and the screen with the page played
  by the test), `tools/test_web_inputs.mjs` (page side: what is chosen, replacing, keeping, restoring a complete set only; added to CI). Checked by
  hand in Chrome against a published interpreter build with the real install: copy before and during the game, restore after a reload, the saved
  `mods.json`, and the Jungle buy list showing the mod's objects. Not checked in a browser: the real folder dialog, the screen's drawing, placing and
  running a Theme Park Inc ride there (its scripts and sounds are the desktop's, covered by the desktop tests).
