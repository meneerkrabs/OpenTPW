## Theme Park Inc: the facilities and the rest areas (November 22, 2026)

- **Facilities in the catalogue.** The object and economy catalogues read each zone's `facilities` folder (Science spells it `Facilities`) with its `facilities.sam`, which gives every
  facility `Info.WhichUIType` 5 (`ObjectCategory.Facility`). Each zone has eight: two toilets, a litter bin, an info kiosk, a security camera, a staff room, a research room and a training
  room. Science's female toilet reuses Arabian's id 3417; each zone has its own catalogue, so the ids never meet.
- **The fifth tab.** Theme Park Inc's build panel calls its list builder `0x57CA20` for the tabs 0, 1, 2, 3 and 5 (`0x57A482`, buttons `0x32DF`, `0x32DE`, `0x32DD`, `0x32E0`, `0x32E1`), and
  the builder compares the type's `+0x600`, the type loader's copy of `Info.WhichUIType` (`0x43D014`), with the tab. `ObjectCatalogEntry.IsBuildable` accepts type 5 for a facility and
  `BuildCategory.Facilities` is 5; the text HUD's build window has a fifth button (in all eleven languages). The list keeps the mission gate and the research gate, so Science's mission 0 lists
  the two toilets, the bin and the staff room; the research room comes with mission 1 and the training room with mission 2 (or mission 1 after the obstacle research).
- **Theme Park World's buy window.** `BuildCategory.Facilities` exists for both editions, but Theme Park World's buy window has its four tabs (`ParkHud.BuyTabs`): the perf build
  scenario, the debug captures and `--debug-hud buytab` walk those, `buytab:facilities` is refused with a warning, and the window's title tolerates a category without a tab.
- **Economy catalogue lookup.** The economy catalogue now finds a category folder and its defaults file without regard to case (Science's `Facilities`), as the object catalogue does. It
  tries the exact path first, which Theme Park World's folders and defaults files (`Rides.sam`, `SideShow.sam`, ...) always match, so Theme Park World's results are unchanged.
- **Single instance.** The row activation `0x57CF60` refuses a type with `UsageInfo.SingleInstance` 1 (the research and training rooms) while its control's built count (`+0x18`, raised by the
  constructor `0x5DBA8A`, lowered by the destroy `0x5DE7B4`) is above 0, with the sound 0x1D; the text HUD refuses it with a note.
- **Research gate.** A type whose object control is not researched stays hidden (`0x57CB66`); now that the research room can be built, research unlocks it. After a park file's load the original
  gives every type of the zone without a control a new one (`0x43F900` → `0x5CFEB0`, researched when `Upgrades[0].CostOfResearch` is 0), so it never meets a type without one. No OpenTPW
  object can enter a Theme Park Inc zone: the official extensions stay off under Theme Park Inc and a mod's `objects.json` must name one of Theme Park World's four themes. So no extension or mod
  object vanishes from the Theme Park Inc build list, and the `tpi-rides` mod's science bin (a Theme Park World `space` feature, id 40003) is never listed beside Theme Park Inc's own (1422).
- **Rest areas.** The staff rooms (Science 1400, Water 2420, Arabian 3418) set `UsageInfo.ChillsYouOut`, the thing flag `+0x42` bit 2 the rest-area finder `0x62BA20` tests. Ported for the
  handymen, the researchers and the mechanics (`ThemeParkIncStaffWorld.Rest.cs`): the finder (nearest open area by squared cells, first on a tie), the tired gate's walk (`0x62BCA9`), the slack test of state 1
  (`0x62D650`), the low-motivation rest (`0x62D866`), state 2 with the capacity test and the customer count (`0x629EF0`), the rest (`0x62A1C0`) and its end (`0x62C230`), the search beside a full
  room (state 0x23, `0x62CA90`: its closing draw is taken on every branch but "no area", which the WP4 notes missed) and a rest area's removal (`0x626638`). The member's rest area (`+0x248`) is saved.
- **Mechanics rest too (a change to the merged mechanics, #985).** The mechanic's dispatch byte table `0x5DA358` sends state 2 to `0x629EF0` and 3 to `0x62A1C0`, and state 0x23 (above 0x16) to the
  common handler `0x6287A0`, which sends it to the rest search `0x62CA90`; #985 had left these states out because no rest area existed. With the staff rooms buildable a tired mechanic would have
  entered state 2 and stayed there, so the mechanic's dispatch and `IsPortedState` now take them (test `AMechanicRestsLikeTheOthers`).
- **Gardeners rest too (a change to the merged gardeners, #987).** The gardener's dispatch byte table `0x5D2B68` reads `00 01 02 03` for the states 0 to 3, so states 2 and 3 take the jump-table
  entries 2 (`0x5D2B26` → `0x629EF0`) and 3 (`0x5D2B2F` → `0x62A1C0`); state 0x23 lies above 0x20 and goes to the common handler `0x6287A0` (`0x5D2B41`), whose byte table `0x628B10` sends it to
  `0x62CA90`. #987 had left them out ("not reached") because no rest area existed; a tired gardener would have stopped in state 2 and his save would have been refused. The gardener's dispatch and
  `IsPortedState` now take them (tests `AGardenerRestsLikeTheOthersAndARestSavedMidwayGoesOnAlike`, a save taken mid-rest that goes on as the uninterrupted run does, and
  `AMechanicOrAGardenerBesideAFullRoomWaitsInTheRestSearch`, which pins both roles' rest-search case beside a full room).
- **The finders walk the object list.** The rest-area and room finders now walk the economy's Theme Park Inc object list (#985), the order `0x62BA20` walks (world `+0x1EAB88`), instead of the thing pool's.
  The walk builds its thing lookup once, so it stays linear in the list and the placed objects.
- **Limits line.** The slice's limits line (all eleven languages, the setup note, the start-up notice, the web page and RUNNING.md) no longer says that no research room can be built: research
  and training wait only for the missions that offer their rooms.
- **Toilets and bins.** The toilets are placed and used; a use still wears nothing and the bins' fill is not kept (`[EXT:tpi-no-maintenance-wear]`). A new litter bin's script now takes
  `Standard.sam`'s construction words (`VAR_CAPACITY` 20, `VAR_DIRTY` 15, `VAR_FIRERISK` 5).
- **Saves.** The asset manifest pins `facilities.sam` with the folders' archives. A layout checkpoint is version 5 and version 4 is refused by its version; a Science continuation whose manifest
  pins no facilities is refused with the start-again message before the asset comparison.
- **Data quirk.** `tools/test_web_inputs.mjs` named `levels/Arabian/Facilities/litbin.wad`, which the data does not have (Arabian's bin is `facilities/littbin.wad`); the fixture now names the
  archive the `tpi-rides` mod reads, `levels/science/Facilities/litbin.wad`.

Not ported yet: the info kiosk's `UsageInfo.IsInfoDesk` and the camera's `ConstrainCamera` (no reader), the build list's grey row (`0x57A399`) and its built and new marks, the first-build
message of the constructor (`0x5DBA0D`: message 0xE to 0x13 by type, which marks the advisor's mail), message 0xF's interface list (presentation), and the `+0x65C` word of the list builder.
Open question: `ThemeParkIncStaffMember.Rest`, the moving step and the daily gain follow the x86 oracle at the control word 0x27F (53 bits), while the research step reads the game thread at 0x7F.

Verified with these checks:

- The Theme Park Inc, park save, catalogue, staff and resume filters pass with the retail data, as do the HUD, buy window and perf tests. New tests cover the buy window's four tabs,
  the single-instance refusal, the level's rest areas in object-list order with their open word, the real Science manifest's facilities and the continuation's refusal without them, the facilities tab and its mission gate, the staff rooms as the only rest areas
  of the three zones, the finder's order and open word, the tired gate's walk, the rest to full energy and its end, the full room's wait and draws, the slack test, the low-motivation rest,
  a removed room, a resting researcher's save round trip, the version-4 checkpoint refusal, and the catalogue's litter bin with its script's construction words.
- The silent, offscreen Science smoke exits 0; it now opens the facilities tab, checks its mission-0 list, builds the staff room beside the cleaner, sends the tired cleaner to it, and builds the
  litter bin, whose script holds the construction words.
- The fidelity register stays at 76 unresolved IDs (no new approximation or extension label). The browser build with the determinism pins builds.

The original game was not launched, and no binary or data is included.
