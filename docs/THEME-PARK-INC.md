# Theme Park Inc (Sim Coaster) compared with Theme Park World

Theme Park Inc (Europe, 2001; Sim Coaster in North America) is Bullfrog's follow-up
to Theme Park World. This note records how far its data and engine match what
OpenTPW reads, measured on October 9, 2026. It is a comparison, not a support claim:
OpenTPW does not support Theme Park Inc. Since October 10, 2026 OpenTPW has a reader for
its `.fsh` textures ([FSH.md](FSH.md)), and since October 13, 2026 an experimental
[playable slice](#theme-park-inc-playable-slice) that loads its worlds, builds in them
and walks guests through them under an explicit capability policy; since October 17, 2026 it also runs the
[money](#theme-park-inc-economy-money-only) of Theme Park Inc's own balance keys.

For installation and launch steps, use the [static Theme Park Inc guide](https://opentpw.io/theme-park-inc.html), which distinguishes current release/browser starts from the upcoming desktop edition chooser.

## Sources

- **Theme Park Inc:** the Dutch/Benelux retail CD (`Game.exe` PE timestamp
  2001-01-10). The data comes from the InstallShield cabinets `data1.cab`/`data2.cab`
  (1,566 files, 336 MB) via `unshield`. The CD also carries third-party
  no-CD/compatibility patches; they were not used or inspected.
- **Theme Park World:** the Mac "Sim Theme Park" CD (HFS volume, November 2000),
  folder `Theme Park Data/data` (312 MB). This is a different edition from the
  Windows edition the rest of OpenTPW's documentation pins (see below).

Neither set is committed. Every number here comes from OpenTPW's own readers run
over loose files and every WAD member, plus small read-only scripts. The exception is the
[Global Master Save](#global-master-save-gms) layout, which was traced in the Theme Park Inc
executable with Ghidra (static analysis only) and then checked against three original save files.

## Summary

The two games run the same engine family. Archives, models, ride scripts, settings,
fonts, lip-sync, sound banks and the online DLLs are the same formats. The largest
asset change is textures: Theme Park Inc replaced the wavelet `.WCT` textures with
EA `SHPI` (`.fsh`) palette images. Its save header is an extended version of the
TPWS container, and its balance settings grow.

| Area | Theme Park World (Mac data) | Theme Park Inc | OpenTPW reader on Theme Park Inc |
| --- | --- | --- | --- |
| Archives `.wad` (`DWFB`, RefPack) | 327 | 303 | 303/303 open, every member extracts |
| Models `.md2` (version bytes 221) | 2,197 | 1,980 | 1,978/1,980; the same 2 `garrow.MD2` (version 24) fail as in TPW |
| Ride scripts `.rse` (`RSSE`) | 324 | 288 | 288/288 parse; see the VM section |
| Settings `.sam` | 418 | 369 | 369/369 |
| Fonts `.bf4` (`F4FB`) | 32 | 180 | 180/180 |
| Lip sync `.lip` | 641 | 1,136 | 1,136/1,136 |
| Strings `.str` (`BFST`), `BFMU`/`BFUM` tables | 21 | 185 | same magic (not parsed in this run) |
| Sound banks `.sdt` | 48 | 28 | 26/28; the 2 failures are truncated music banks, see below |
| Textures | 8,712 `.wct` | 34 `.wct` (intro only), 7,283 `.fsh`, 2,856 `.tga`, 172 `.png` | `.wct` 34/34; `.fsh` 7,283/7,283 decode to RGBA ([FSH.md](FSH.md)) |
| Terrain/attribute maps `.map` (`TP2M`) | 5 | 4 | 4/4; the other `.map` files are sound catalogs in both games |
| Saves | `Easymode.TPWI` (magic 400) | `prebuilt.TPWS` (magic 500, extended header) | read-only science prebuilt import; no TPI save writing |
| Movies | QuickTime `.mov` (Mac edition) | 2 `.tgq` (`SCHl`) | 2/2 |
| Banner companions `.mtr` | none on the Mac CD | 7 | 0/7: header words differ from the TPW variant |

<a id="theme-park-inc-level-viewer"></a>
## Theme Park Inc playable slice

Until October 13, 2026 this section described the view-only "level viewer"; the same command now
starts the playable slice below (older links still land here).

`--load-original-level arabian|science|water` with Theme Park Inc data starts the experimental
**playable slice** (`[EXT:tpi-viewer]`, October 13, 2026). A start without a world opens the slice's
[Personnel File start screen](#theme-park-inc-front-end) instead. It is not Theme Park Inc support: there is
no Theme Park Inc in-game interface, staff, research, challenges or native save writing (its money is the only economy), and nothing it draws has been
compared with the original game. How to extract the CD cabinets and start it:
[RUNNING.md](RUNNING.md#theme-park-inc-level-viewer-experimental).

The browser setup also offers the same experimental route with an explicit Arabian,
Science or Water choice and the player's own extracted `Data` folder (the Personnel File start screen is not offered in the browser yet: phase 1 keeps this up-front
choice, because the browser copies a theme's files at setup and its on-demand copy for a zone chosen later is not built for Theme Park Inc). It imports the
selected theme before checking the existing edition evidence, then enters the existing
experimental world/HUD path without a Theme Park World lobby. Its capability restrictions
are the same as the desktop slice. Synthetic startup/cache controls and a browser-project
build cover the ingress; fresh original-data and browser rendering qualification are deferred.
See [WEB.md](WEB.md#game-files-on-demand) for import retries and storage limits.

**Edition capability policy.** `GameEditions.Capabilities` (Client/Setup/GameEdition.cs) is the one
place that says what an edition's park runs. Theme Park World keeps every implemented system. The
Theme Park Inc slice runs building, guests and the money under an experimental HUD and refuses the systems whose
Theme Park Inc rules or data OpenTPW has not traced. The economy capability is split into `Money` (on for the slice),
`Staff`, `Research` and `Challenges` (off): Theme Park Inc's `Standard.sam` lacks 86 of the 152 fixed balance
keys the Theme Park World economy reads, e.g. `PerGradeStaffConsts[0].BaseWage` (re-measured against the executable's
schema rows October 17, 2026; the October 13 count of 89 of 155 was a text count), but carries the money keys. Also refused: advisor, Theme Park World's 3D lobby front end (`FrontEnd`; the Theme Park Inc Personnel File start screen has its own flag,
`PersonnelFileFrontEnd`, on for the slice, `[EXT:tpi-front-end-phase1]`), park saves, sharing
(export/import/visit), the mission campaign, the Global Master Save and the disc's COS files (`[EXT:tpi-capability-gates]`, an OpenTPW scope decision).
`Level` gates its economy on the policy; the refusals stay real, not deleted: nothing writes or resumes a native Theme Park Inc park save, GMS
or COS file, and the start-up command line additionally refuses `--sandbox`,
`--smoke-test`, `--advisor-say`, `--advisor-response`, `--setup`, `--park-resume-smoke`,
`--export-park`, `--import-park` and `--visit-park` on Theme Park Inc data, and `--front-end` together with a world. `--front-end` alone (or a start with no
world and only file, language, display or sound options) opens the Personnel File; any other option without a world is refused as before.

**What the slice shows and runs.** The base terrain (`base.MD2` heightfield and meshes), the lake, the
path layer (`pathtex` textures), the monorail (part of the terrain meshes), guests with their sprites,
and the world's sound and music. Science also loads the sole shipped `PreBuilt/prebuilt.TPWS`: its
paths, placed objects and fixed-item records. Arabian and water have no matching shipped prebuilt
save and still use the default fixed items (RIDES-019). The camera is the park camera (pan, rotate,
zoom); the sandbox developer panels are off. On top of that the slice adds:

- The **experimental HUD** (`ThemeParkIncHud`, `[EXT:tpi-text-hud]`): an OpenTPW text interface — a state line that
  names the experiment and its refused systems, a guest counter, a money line (cash, park date, entrance fee), a message line, a build window over
  the world's own catalog with each object's price, remove/rotate/cancel tools, a speed control and a pause menu (Esc) whose
  application-owned layout checkpoint controls are described below. Building is **charged** at Theme Park Inc's own prices and removing refunds the scrap value
  ([economy](#theme-park-inc-economy-money-only); `[EXT:tpi-free-builds]` is retired). If the HUD cannot be built the world falls back to the bare viewer
  (Esc quits, P pauses) with a warning.
- **Building** through the same rules as a Theme Park World park (`ParkObjects` terrain and occupancy
  checks, `Level.PlaceObject` with the purchase), including the path and queue tools of the traced
  Theme Park World flow (`[EXT:tpi-tool-flow]`): a click on an empty owned cell starts the path tool, Backspace
  undoes, right-click cancels; a placed `Info.HasQueue` ride switches to the queue tool. The build
  ghost markers now resolve their textures by name without extension, so Theme Park Inc's own
  `blue.tga`/`red.tga` markers draw (Theme Park World keeps its `blue.wct`/`red.wct`).
- **Guests** (`[EXT:tpi-twp-guest-rules]`) under the ported Theme Park World guest rules over the Theme Park Inc world's
  own guest settings: arrivals from the bus stop through the ticket booth, path walking, needs,
  queues and rides. The slice's frame pumps guests, object scripts, the money and crowd audio only — no advisor,
  staff voices or weather sounds, which need systems the slice refuses.

`ThemeParkIncPlayTests` covers the policy (synthetic) and the supported gameplay route over the corpus
(headless composition of the build rules, a placed object's original script, path laying, guest arrivals and the money:
the settings, the charges, the running costs and the calendar; `OPENTPW_TPI_PATH`); `ThemeParkIncEconomyTests` covers the money rules on synthetic data.
None of it compares with the original game.

**Layout checkpoints** (an OpenTPW extension, `EXT:tpi-layout-checkpoint`) are separate from continuation and native park saves. The experimental
pause menu can save the built layout and explicitly confirm loading it as a new session. Typed cells,
objects, open flags, IDs and creation seeds persist; guests, script progress, clock and audio restart.
Prototype/coaster layouts are refused. Trusted asset checks and staged candidate construction preserve
the current world on preflight/construction failure. See [PARK-SAVES.md](PARK-SAVES.md#experimental-theme-park-inc-layout-checkpoints)
for the separate `.tpilayout` format, exact state boundaries and verification limits.

**What it leaves out.** No research, challenges or staff (the money runs, see above); no continuation or native park saving/loading;
no front end or online world; no advisor. The prebuilt save's economy, guests, ride state and
untraced scene records are not restored. Arabian and water remain empty apart from default fixed
items; the science save contains no Bus record, so none is inferred for that park. Several ride
scripts report unimplemented effects in the log; the two opcodes only Theme Park Inc scripts use
(`WAITABS`, `MULT`) have VM handlers since October 13, 2026 (see RSE-VM.md's gap-opcode section;
the science smoke never reached them, so retail execution is not claimed). Textures are not
checked against the original game's alpha or colour handling (the palette converter's `0x24` reading is read at instruction level, `APPROX:COMPAT-014` retired; the `0x2D` palette is `[EXT:fsh-0x2d-palette]`, `APPROX:COMPAT-015` retired).

**Textures.** `Texture` now loads `.fsh` (first image of the SHPI file, `FshFile`) and stb images
(`.tga`, `.png`) from the game data as well as from disk; decoding is GPU-free
(`Texture.DecodeImage`). Terrain and object textures are looked up by name without extension
(`OriginalTerrain.FindTexture`, `ObjectAssets.ResolveTexture`), so `.wct` and `.fsh` need no
separate paths. Theme Park Inc never goes through the `.wct` texture packs. All terrain and object
textures of the three worlds resolve and decode, except the build-tool markers `PathTex` 16 to 18
(`baseblue`, `grnarrow`, `redarrow`, unused here) and six lake bed textures of `science` (below).
A texture the data lacks is drawn plain white and named in one log line.

**The `@` shape symbol.** `Info.Shape` rows of every Theme Park World object use `. * 2 S N E < > + W`.
Theme Park Inc adds `@`, in exactly six objects, all road signs: `features/smrdsgn` ("`*@*`") and
`lroadsgn` ("`*@@*`") in `arabian` and `water`, `signsm` ("`*@*`") and `signlrg` ("`*@@*`") in
`science`. Each sign model spans its shape width x 10 units and consists of a base, two pillar meshes and a sign
board, and the `.sam` marks the object `Info.OverwritePriority 1` ("Overwritable!") and
`Info.IsChoosable 0`. The likely reading is a pillar cell at each end (`*`) and cells under the board (`@`),
but the data does not prove it. The Theme Park Inc `Game.exe` is
SafeDisc-encrypted, so this reading rests on the data only. The viewer classifies `@` as an occupied
cell (RIDES-031), which is also the safe choice for placement. (The obstacles use further symbols
`X`, `H`, `%`, which no catalog object uses and the viewer does not read.)

**The science hole.** The first spike showed a black region of about 450 cells in `science`. The
heightfield (`base.MD2`) leaves 1,462 cells open as holes, and in Theme Park World meshes of the same
file fill them. In Theme Park Inc the lake is a separate model: `terrain.wad` holds `dlake.MD2`
(`lake_surface`, `lake_bottom`; its extent equals the holes that no `base.MD2` mesh covers: 453
cells, none left once it is drawn), `lake.MD2` (the same lake with hoardings, a tent, a plank and a
shovel, so a building site) and the animation files `idle1`/`idle2`/`lakem1`/`lakem2`/`dlakem`. The
viewer now always draws `dlake.MD2` (`[EXT:tpi-lake-drained]`, `APPROX:COMPAT-019` retired: the viewer runs no missions; `lake.MD2` and the animations are
ignored). That was a loader gap. The data names it the drained state: `Data/Objects.txt` lists 1703 "New Science Lake Obstacle" and 1704
"New Science Drained Lake Obstacle", and `missions/miss01.sam` sets `Obstacle.Type 1703 # lake`, cleared by one gardener with ability 10
("Water Drainage"); so `dlake` is the drained lake, not a finished one, and which one a park shows is data-driven (the exe holds no
"lake" string). Open: six of its bed textures (`bed04`, `bed06`, `bed07`, `bed09`,
`bed10`, `bed12`) are in no archive of the supplied data, so the lake's bank is drawn white. `arabian`
has 29 more open cells in two mountain pits that no mesh covers (`4an_newenv.MD2`/`3an_newenv.MD2`,
variants of the environment, do not either); they show the clear colour. The `*_newenv.MD2` files,
`lake.MD2`, `lake01_alpha.fsh` (a separate water alpha image) and the `proc_tex` set are not used.

**Other open spots.** Each world has 8 to 16 heightfield ground cells textured `trans01` (only a `.tga`, in
`Sharetex.wad`); they draw as black quadrilaterals (visible in `water` as small black patches in the
snowfield), most likely because the ground is drawn without blending (not confirmed). The mountain pits next to the entrance road
show black interiors in all three worlds. Esc and P in the viewer are implemented but were not
exercised by an automated run (all checks used `--capture-world`).

The slice's own scope decisions are OpenTPW design decisions, not approximations (October 14, 2026): the text HUD, the builds' price (free until October 17),
the Theme Park World guest rules over Theme Park Inc data, the capability gates and the Theme Park World tool flow (`[EXT:tpi-*]` at their sites; TPI-900 to
TPI-904 are retired).

**Approximations and open questions.** (The `0x2D` palette and the drained lake are extensions now: `[EXT:fsh-0x2d-palette]`, `[EXT:tpi-lake-drained]`.)
RIDES-031 (`@`), RIDES-030 (non-finite texture coordinates in ten vertices of `science/sideshow/natnav`,
drawn as 0, 0), RIDES-019 (the End object; the Bus from setup is `[EXT:tpi-bus-from-setup]`), RIDES-040 (zero-coordinate fixed
scene records). Open: how the original game transforms the zero-coordinate scene records and
places the gate and bus in worlds without a prebuilt save; what the `*_newenv.MD2` files are for (`3ns_newenv` and `4ns_newenv` carry a heightfield and an `arrival_*` mesh set); the
missing lake bed textures; the viewer's lighting and water transparency against the original.

### Theme Park Inc economy (money only)

Since October 17, 2026 the slice runs Theme Park Inc's **money** (`EditionCapabilities.Money`) and still refuses staff, research and challenges
(`Staff`, `Research`, `Challenges`; the whole Theme Park World economy is `Economy`, true only for Theme Park World). The park's economy is
the same `ParkEconomy` class under `EconomyRules.ThemeParkIncMoney` (`Economy/EconomyRules.cs`), over settings built from Theme Park Inc's own keys.
Evidence: static reading of the no-CD `Game.exe` against `tp.exe` (October 17, 2026), nothing run; none of it is compared with the running original.

**Settings.** `levels/Standard.sam` of Theme Park Inc lacks 86 of the 152 fixed keys the Theme Park World economy reads (the executable's schema rows
`0x009966C0`, getter `0x0041B350`, block `0x00A01A6C`; tp.exe's are `0x0073FC70`, `0x00402B10`, `0x0078504C`) and no theme layer supplies them. So
`BalanceSettings.ForThemeParkInc` / `LoadThemeParkInc(theme)` (global file under the theme's, no Easy layer) reads only the money keys and fills every
staff, research, golden-ticket and challenge member with an empty or zero placeholder, never a Theme Park World number; a missing money key fails the load.
The object catalogue adds the keys only Theme Park Inc's type record has: `Info.ExpectedPrice` (+0x88), `Info.WorkingLife` (+0x8C),
`Upgrades[n].RunningCost` (+0x2B4, stride 0x44) and `Upgrades[n].CashPerBrainPoint` (+0x274); Theme Park World's files read 0 for them. `Info.ExpectedPrice`,
`WorkingLife` and `CashPerBrainPoint` are read but not used (meaning not traced).

| What | Theme Park Inc | Theme Park World | Here |
| --- | --- | --- | --- |
| `BankAccountInfo.InitialCash` 50,000, `InitialAdmissionFee` 25 | bank constructor `0x5BFBBA`, `0x5BFC73` (block `0x00A020BC`, `0x00A020C0`) | `0x4CF8C6` (`0x007851D8`) | the same bank; the data holds 20 for the fee in Theme Park World |
| `LoanInfo[n]` (ten slots, eight used) | `0x5BFB97` (`0x00A020C4`); the loan loop and the monthly step `0x5C0B10`..`0x5C0C49` | `0x4CF7C0`, `0x4D0370` | `LoanMath` unchanged (oracle X8, X9) |
| bank debit | `0x5C07B0` | `0x4D01F0` | `ParkEconomy.Post` |
| `Costs.QueueCell`, `PathCell`, `KartTrackCell`, `WaterTrackCell` | setter `0x4289AC` (`0x00A0264C`..`0x00A02658`) | `0x408192` | path, queue and track charges as in Theme Park World |
| `Costs.MapCell` | in the data (`0x00A0265C`), read by no instruction found | the Buy Land tool | **no Buy Land**: `TryBuyCells(Land)` answers `NoLandForSale`; the park boundary is the mission's `BoundaryClicks` (moderate confidence) |
| calendar | constructor `0x607E90` | `0x4F7E80` | 2000-01-01, 15,000 game seconds per real second (`ParkCalendar`) |
| entry-fee judgement | `0x6157D0` (divisor word `0x00A01A90`) | `0x4FF5C0` | same routine; the divisor is data: 6 in Theme Park Inc's global file (4 in arabian's layer), 4 in Theme Park World's |
| object purchase, upgrade purchase | `0x5DBE3C`, `0x5E2A99` | `0x4DB090`, `0x4AF4E9` | the build charges `Upgrades[0].CostOfUpgrade`; the upgrade needs a mechanic, so it stays refused (`NeedsStaff`) |
| scrap value | `0x5E7890`, percentage `0x5E75F0` | `0x4E2400`, `0x4E2290` | by year of age, 100 % if unused within 30 days, plus a condemned object (state 4) refunds nothing; the thing-flag branch is left out (ECON-061) |
| running costs | month message `0x5DEFFF` | none | each object pays its current level's `Upgrades[n].RunningCost` every month, nothing in ride state 1 or 4 (ECON-060) |
| arrival group | `0x5BCD60` (words `0x00A02240`..`0x00A02250`) | `0x4CF5B0` | `GuestSimulation.ArrivalGroupSize` with `Arrival.*PercentageModifier`, single-precision 0.01 (oracle X7); no Theme Park Inc weather runs, so the percentage is always 100 (ARRIVAL-008) |

**What runs.** Starting cash and admission fee; the build, path, queue and kart/water track charges (a refused purchase says why); scrap refunds when an object
is removed; the loans; the bankruptcy after six months in the red (the arrival stops, building is refused); the calendar and each
object's cost statistics (the month's costs, the 30-day cost ring and the total profit, which the running cost enters; Theme Park Inc's are monthly rings, ECON-060); the monthly running costs; the entry-fee judgement and the arrival rule above. The HUD's money line shows the cash, the park date and the entrance
fee, the build list shows each object's price and a placed object's refusal (not enough money) is the level's message. The Theme Park Inc park opens at the start
(`[EXT:tpi-park-open]`): the slice has no gate control, its gate script is not traced, so the economy neither drives nor closes the park door. The prebuilt
science save's objects register uncharged, like an imported park's; its own economy is not restored. A layout checkpoint saves the layout only: the cash, calendar and
loans start fresh in the new session, like the guests.

**What stays refused.** Staff (no pool, no wages, no mechanics, handymen or entertainers), so the rides neither wear nor break down (`[EXT:tpi-money-only]`), a toilet use wears no gauge and no litter is dropped, because no handyman would ever clean it (`[EXT:tpi-no-maintenance-wear]`), and no
upgrade is installed; research (the whole catalogue is on offer, `[EXT:tpi-no-research]`); challenges, golden tickets and the missions; the shares game; Theme Park Inc's
weather; the advisor; every save format. Rides carry `UsageInfo.InitPricePerUse` (20) and `Info.ExpectedPrice` (25) in Theme Park Inc's data, but the guests pay
nothing for a ride, as in Theme Park World (ECON-062). Coaster track charges use the Theme Park World coaster cost model over Theme Park Inc's object settings and are
not verified for it. The statistics block's accumulator (`+0x938`), which the month message adds to the running cost, is not traced (ECON-060).

### Prebuilt science save

The supplied retail `Data/PreBuilt` folder contains one file, `prebuilt.TPWS` (SHA-256
`05ea01f2151757b3f4bbe7c15a1cb702af9f53b959c533c5e346d970695d882d`). Its decoded
cell attributes equal `science/terrain/base.map` in all 16,384 cells; they differ from Arabian
in 1,053 and water in 2,125. This is the evidence for loading it only in the science viewer.
No original executable was run to establish a broader TPI save schema.

| Field used | Observation and use |
| --- | --- |
| Container | Magic 500; the 24-byte version block after the magic (`01 13 00 00 00`, `Version: Beta_21\r\n` and its NUL); language byte at `0x1c`, stamp `01 22 19 85` at `0x61d`, online word at `0x621`, `BILZ` at `0x625`, zlib at `0x641`. The bounded zlib reader applies after shifting these offsets; see [SAVE-CONTAINER.md](SAVE-CONTAINER.md#theme-park-inc-version-block). |
| Decoded payload | 1,692,641 bytes; all 17 writer-order trailing delimiters occur once. The World block ends at `DLRW` offset 1,571,677. The blocks are split by their readers' widths under the Theme Park Inc profile (45 message lists, a 48-byte camera, an empty UI block), see [the save layout](#theme-park-inc-park-save-layout). |
| Cell grid | One 128×128 X-fastest run at offsets 7,116–1,449,038, read by the map loader's status-byte rule (below): every cell has status 3 (88 bytes) or, for 13 cells, 7 (98 bytes with the 10-byte effects record); `FF FF` closes each base record at +86/+87. MAP attributes remain at +46; path flag bit 0 at +13, connections +8, path texture +17, turn +21 and occupancy +12 match the TPW positions. The save yields 76 path cells. |
| `SYSG` objects | A candidate begins with byte `1`, then an opaque `u32`, followed by little-endian `u32` Info.Id at +5, X +9, Y +13, width +17, height +21, kind +25 and index +29; rotation is `u16` at +43. The bounded signature scan finds 23 records: 9 kind 815 objects and 14 kind 865 fixed/scene records. |
| Catalogue and placement | All nine kind 815 IDs resolve in science: eight rectangular footprints are occupied in the grid and one (`1101`, 4×5 at 42,26) has two unoccupied corner cells (42,30 and 45,30), so its footprint remains unresolved while the object is rendered at its saved anchor. Of the 14 fixed/scene records, nine IDs resolve; five zero-coordinate IDs (`1702`, `1701`, `1700`, `1750`, `1798`) have no object archive and are not rendered. One resolved record is a tool, leaving 17 objects rendered in the native viewer. |
| Entrance | Saved `1601` Gates at (46,17), and `103` Lobby plus `104` atmdesk at (46,16), all rotation 0. TPI fixed models use local coordinates, so the viewer applies these saved anchors and rotations. There is no Bus record in this save. Eleven other fixed/scene records use (0,0); their thing records hold the default position (128, 128) and angle 0 for the six with an archive (real positions for the five without one), and how the original places their local-coordinate models at that anchor remains an approximation (RIDES-040). |

The importer is read-only. The World block is read into typed records
(`ThemeParkIncSaveWorld`) and must consume exactly to its delimiter; each SYSG record found by
the signature scan must then name exactly one attraction thing of that World by mesh instance id
(the record's `u32` at +1, the thing's `MeshInstanceID`), Info.Id and, for a placed object, cell, so a damaged or
different save cannot quietly produce a partial park. This replaced the former requirement of
exactly 23 records with 14 fixed items. The join runs both ways: every record must name one attraction,
no attraction may be named by two records, and every attraction must have a record except the lake
obstacle `1703` (whose RideSystem entry is not an 815/865 scene record; it is the only entry of
`UnrecordedThemeParkIncAttractions`). A damaged or duplicated record therefore still fails the import. It uses the same exact map-attribute check
as TPW but the TPI record widths and SYSG header are separate, leaving TPW save reading unchanged. A 300-frame native
science capture draws the paths and park objects; it does not prove their appearance against the
original game. Economy, ride state, guests, opaque cell data and unrecognised scene records are
not restored.

### Theme Park Inc park save layout

Static reading of the no-CD `Game.exe` (save `0x43ee20`, header writer `0x440a20`, loader `0x43f370`),
checked against the one park save that exists, `prebuilt.TPWS` (decoded 1,692,641 bytes). Container
fields: [SAVE-CONTAINER.md](SAVE-CONTAINER.md). The payload loader (`0x43fad0`, writer `0x441060`)
uses the same 17 delimiters in the same order as `tp.exe` (`0x415270`), so `SavePayloadLayout.Parse(payload,
SavePayloadProfile.ThemeParkInc)` splits it by the blocks' own reader widths; only a Coasters block with a
coaster is still found by its delimiter (SAVE-011). `ParseThemeParkInc` is that call.

| Block | Prebuilt bytes | Reader rule (Theme Park Inc against Theme Park World) |
| --- | ---: | --- |
| ActionRecording | 563 | `u32` published flag, `u32` size, bytes (same) |
| World | 1,571,114 | `ThemeParkIncSaveWorld`, below |
| Scripts | 412 | `SCPT` block, `0x118`-byte records (same; loader `0x4e4410`) |
| Particles | 76,252 | same (loader `0x658810`) |
| MessageCentre | 476 | 45 lists of `u32 count, count × u16` (TPW 29; loader `0x437140`) |
| Clock / VanillaTime / GameSystem | 8 / 4 / 36 | same (`0x41b890`, `0x41bec0`, `0x6a2390`) |
| RideSystem | 13,154 | `u32` length and that many bytes (same; its records have an extra `u32` before Info.Id, `0x4c5770`) |
| TrackRides / FlyingRides | 180 / 452 | same (`0x6921a0`, `0x6b6720`) |
| RSSE | 26,446 | same; the records end in `OBJ ` (`0x6afd90`) |
| Camera | 48 | 40 bytes plus `gf_SavedRideXRotation` and `gf_SavedRideYRotation` (`0x46a150`) |
| Coasters | 1,226 | the TPW layout: one coaster, one section, 30 train slots of one car (30 × 38 bytes), `SaveCoasterBlock.ParseBlock` consumes it exactly (`0x4782e0`) |
| Advisor / Sound | 392 / 1,804 | same (`0x717460`, `0x652e60`) |
| Cheat / AdvisorScoring | 2 / 4 | same (`0x423c40`, `0x71c300`) |
| UI | 0 | the writer (`0x4f8630`) is a bare `ret` and the loader reads nothing: nothing may follow the `ADSC` delimiter |

**World block** (loader `0x64bc00`, 1,571,114 bytes, consumed exactly): variables (version 3,
70 bytes: the TPW version 2's 64 plus `mLobby`, `mCurrentObstacle` and `mFirstGardener`), the control
manager (`0x5cf9b0`: 150 records of 36 bytes, `u32` count, `u16` search key, 5,406 bytes), the staff pool
(`0x6308a0`: 32 candidates of 31 bytes (`0x62f980`: type, name, salary, agency, sub-type, valid, on-pointer, time
stamp, timeout, experience), six role pairs of count and stop flag, time stamp and flag, 1,027 bytes), the
calendar (32 bytes: the TPW calendar with `mYearAtLastUpdate`), the arrivals (18 bytes, raw), the map and the thing list.

The map (`0x5d6d40`) is the TPW rule with a wider cell: for each of the fixed `0x4000` cells a status byte,
then the 56-byte map cell (TPW 52, `mIsObstacle` added) when bit 0 is set, the 31-byte track cell when bit 1 is set and the
10-byte effects record when bit 2 is set. The prebuilt's grid is at 7,116 to 1,449,038. The thing list is `u32 head`, the marker `EATC`, then per
thing `u32 next, u32 model, 'LDMT'`, the model's 4-byte tag and the body, and ends with `ENOD`, followed by the new special
rides (`0x6244b0`: `u32 count`, then count records of a `u16` and 20 slots of 10 bytes, 4 = 812 bytes in the
prebuilt).

| Model | Tag | Body | In `ThemeParkIncSaveWorld` |
| --- | --- | --- | --- |
| 3 attraction | `OBJE` | 312 bytes, plus 5,263 of history when its flag byte (the last of the 312) is 1: 13 rings of 30 and 6 rings of 144 | typed (`ThemeParkIncAttraction`, including the history rings); 24 in the prebuilt |
| 16 bank | `BANK` | 292 (the TPW bank) | typed (`SaveBank`) |
| 10 mechanic HQ, 12 tag system, 17 UI receiver, 21 `OBJH`, 22 `CUTS`, 23 `TRNR` | | 10, 12, 8, 14, 10, 8 | exact; base plus raw tail (`ThemeParkIncSmallThing`) |
| 9 staff HQ, 11 advisor controller, 13 park analyser, 14 research lab, 15 weather, 19 challenge system | | 411, 233, 76,617, 8,414, 104, 1,287 | raw; these widths are the prebuilt's, not traced from the serializers (closure-derived), and the header that must follow checks them |
| 1 guest, 4 to 8 staff, 20 gardener | | none in the prebuilt | raw; no width, the body ends at the first following header that reads (heuristic) |

Models 0, 2 and 18 have no serializer (the jump table `0x64d7e0` sends 18 to the challenge case too, but only 19 is witnessed) and are
rejected. The widths of the staff HQ, advisor, analyser, lab, weather and challenge things are therefore **not** traced
reader widths, and guests, staff and gardeners (absent from the prebuilt) are found by searching for the next header that reads
(or, for the last thing, the `ENOD` whose special-rides tail is followed by the `WRLD` delimiter). A complete header
lookalike inside such a search-bounded body would fabricate a shorter body and an extra thing that still closes at the delimiter and passes
the variable checks; a uniqueness check cannot catch it, because a raw body can always absorb the thing after it. This is SAVE-944.
The block must in every case read exactly to its delimiter.
Typed fields that are inferred rather than traced are SAVE-940 to SAVE-944 in the [register](FIDELITY-REGISTER.md).

## Theme Park Inc front end

Phase 1 builds the start screen only: the Personnel File.

Theme Park Inc has no island lobby. It boots into the 3D intro world (`wIntro`; the exe's world name table at `0x9d53b4` lists arabian, science, water, intro as 0 to 3, and
`THEMENAMES.str` names the zones "Land of Invention", "Polar Zone", "Intro World" and "Arabian Nights") and runs its front-end screens as overlays on it. Its start menu is
the **Personnel File** (October 17, 2026, static reading of the no-CD `Game.exe`, SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`; the retail
executable is SafeDisc-encrypted):

| Item | Where in `Game.exe` |
| --- | --- |
| Layout table `PersonnelFile` | `0x9c0fbc` (39 nodes, root `0xc738`, `!window_s`, rectangle (273,72)-(1775,1464)); ends at `0x9c15ac` |
| Builder / handlers | builder `0x56adb0` (window created by `0x84dea0` over the table), click handler `0x568ce0`/`0x568d40`, slot handlers `0x569e12` to `0x56a9ce` |
| Title | text box `0xc75c`, UITEXT 19 "TPi Personnel File" |
| Player slots | four `filetab` buttons `0xc73d` to `0xc740`; a free slot shows UITEXT 20 "New Player" |
| File page | `filepage` panel `0xc73b`; label boxes `0xc741` to `0xc74e` (money, golden tickets, shares, portfolio, objective: UITEXT 21 to 26, 29; UITEXT 28 "Zone not yet opened") |
| Name entry | `filebox` `0xc756` + `filebutok` `0xc757` + text field `0xc758` (prompt UITEXT 32 "Enter name here") |
| Buttons | Enter Game `0xc754` (UITEXT 34, help 287; handler `0x56ab40` saves the player, selects the slot and reads its world: -1 or 3, the intro world, becomes 1, science, then `ChangeWorld` `0x6a26b0`), Load A Game `0xc759` (35, help 287, opens table `0x9ba364`), Delete Player `0xc755` (33, help 288), Coaster Design Kit `0xc73a` (31, help 290), Quit Game `0xc75b` (30, help 289) |
| Mugshot | `!frame` `0xc75d` around `mugshot` `0xc75e` |
| Quit confirmation | generic confirm builder `0x4f47f0` over table `0x9b18bc` (5 nodes, root `0xea60`, `!window`); the quit click (`0x568d67`) passes UITEXT 773 and mode words 2 and 1, which make its two buttons `b_cancel` (`0xea61`, help 3) and `b_okay` (`0xea62`, help 0, action `0x568e60` quits) |
| Fonts | PERSONNELFILEBIG, PERSONNELFILEMED, PERSONNELFILESMALL (and MATISSE18/36 elsewhere) in the language folder |

Theme Park Inc's `ui.wad` has 1,462 members, 172 MD2 models with unique root-name hashes and its textures as `.fsh`/`.tga`; only 37 root names are shared with Theme Park World's.
Its `UITEXT.str` has 994 records with ids unlike Theme Park World's (`UIStrings` does not apply; `ThemeParkIncText` names the ids used, read with `UiStringTable.Entry`),
`UIHELPTEXT.str` 367 records.

**What phase 1 builds.** `tools/ppc-analysis/lanes/ui/tpi_exe_layouts.py` (the same stream grammar as `tpw_exe_layouts.py`; a one-off manual scan parsed all 86 layout tables of the
executable, which the script's `--check` does not repeat: it reproduces the two tables) writes `ThemeParkIncLayoutTables` (`UI/Original/ThemeParkIncLayoutTables.cs`: rectangles, ids, help ids and model names only, no game data) for the two tables above.
`FrontEnd/ThemeParkIncFrontEnd.cs` draws the Personnel File from Theme Park Inc's `ui.wad` models at those rectangles with its strings and the PERSONNELFILE fonts
(`[EXT:tpi-front-end-phase1]`), over a flat dark backdrop (no 3D intro world, no sound, no start-up movies):

- **One player** (`[EXT:tpi-front-end-one-player]`, the same decision as `[EXT:no-player-profiles]`): slot 1 is shown selected as "New Player" and the other three slots, the name
  box and its tick are drawn inactive. Nothing is saved or loaded; the stock labels (money, shares, objective) are not drawn because the slice runs no economy.
- **Enter Game** starts the slice in the chosen zone, science by default (the original's default for a new player), through the same queued level start as `--load-original-level`.
- **Zone chooser** (`[EXT:tpi-front-end-zone-chooser]`): the original reaches Polar Zone and Arabian Nights only through its campaign and Overview Map, so the page offers a latched
  button per zone (Land of Invention = science, Polar Zone = water, Arabian Nights = arabian, named by `THEMENAMES.str`) above Enter Game. The label above them is OpenTPW's
  (`OpenTpwText.TpiZoneChooser`).
- **Load A Game, Delete Player and the Coaster Design Kit** are drawn but inactive (disabled buttons show their disabled frame and give no help text); **Quit Game** (and Escape)
  asks the confirmation (cancel has the focus) and the tick closes the window.
- Desktop: `--front-end`, or a plain start with Theme Park Inc data (`OPENTPW_GAME_PATH`/`--game-path` and no world), opens the page; `--load-original-level` still starts a world
  directly. The setup wizard and the remembered edition still ask for a world and start it directly in this phase, and the browser keeps its up-front theme choice.

`ThemeParkIncFrontEndTests` covers the generated tables, the id map, the capability flag and start-up routing, the page's routing headless and (with `OPENTPW_TPI_PATH`) that every
model of the tables is in Theme Park Inc's `ui.wad` and loads with its textures, that the strings resolve in English and Dutch, that the fonts load and that the page draws.
**Not verified:** how the page looks on screen (no capture was taken), the exact text positions inside the controls (the buttons draw fitted text over the whole control rectangle),
the window's own text area in the quit confirmation, and the label boxes' content, which phase 1 leaves out.

## Ride-script VM: shared formats and opcode lineage

All 288 Theme Park Inc scripts have the same 48-byte header and time slice (50) as
all 324 TPW scripts. Every opcode they use is in OpenTPW's `Opcode` enum with the
same operand count as in TPW, and no unknown opcode occurs. Theme Park Inc uses 72
distinct opcodes; it adds `WAITABS` (45) and `MULT` (48), which no TPW script uses.
It no longer uses `TRIGANIMSPEED`, `LOOPANIM_CH`, `TURBO`, the child-variable
opcodes (66, 67, 69), `GETREMOTEVAR`, the `WALK*FLOAT*` opcodes, `HOUR`/`MIN`/`SEC`
or `SPARK`. So Theme Park Inc scripts would exercise two VM paths that TPW data
never reaches.

`TestScript.wad` ships five RSE **source** files (`test.rss`, `Shop.rss`,
`Feature.rss`, `TestAnim.rss`, `Null.rss`) for Bullfrog's assembler (`#setstack`,
`#setlimbo`, `variable`, `.label`, `#include "\source\game\rsse\code\rsse_scriptdefs.h"`).
They are first-party evidence for the VM:

- The 12 common ride variables are declared in the order of OpenTPW's
  `RideVariables` (`VAR_LETMEON` = 0 … `VAR_PARAM` = 11); custom variables follow from 12.
- `SUB VAR_TEMP VAR_STARTNOW VAR_TEMP` followed by `BRANCH_NV go ; timed out !`, and
  `SUB VAR_TEMP VAR_CAPACITY VAR_TEMP` followed by `BRANCH_Z`/`BRANCH_NV` (shop full),
  match OpenTPW's `SUB dest a b` = a − b.
- `ADD VAR_COUNT -1` followed by `BRANCH_PV runlp` matches the strictly positive
  `BRANCH_PV`.
- Comments name visitor operations: `HUSH VAR_LETMEON ; This guy is now on the ride`,
  `HOP VAR_LETMEOFF` then `; Wait until AI has taken him...`,
  `UNLIMBO VAR_LETMEOFF ; get next person out (if any)`, and `INLIMBO VAR_TEMP` as
  the number of visitors in limbo.
- `LIMBO VAR_LETMEON VAR_DURATION` gives the second `LIMBO` operand, which
  OpenTPW's handler names `unknown`: it is the time spent in limbo.
- `COPY VAR_LETMEON 0 ; Open the gate` and `COPY VAR_LETMEON 1 ; Close the gate`
  give the gate meaning of `VAR_LETMEON` in shop scripts.

These are test scripts, not shipped ride scripts, so they support but do not prove
the semantics of the TPW corpus.

## Differences

- **Textures.** Theme Park Inc stores level, UI and advisor textures as one-image
  EA `SHPI` files: all 7,283 have directory id `G231` and an 8-bit palette image
  (code `0x7B`; 6,736 of them RefPack-compressed as `0xFB`) with a 24-bit (4,308),
  32-bit (2,974) or 16-bit (1) palette. Most are 32×32, 64×64 or 128×128. `.wct`
  remains only in `levels/wIntro`. `FshFile` decodes all of them; see [FSH.md](FSH.md).
- **Saves.** `prebuilt.TPWS` starts with magic 500 (TPWS), like TPW saves, but then
  carries the Theme Park Inc version block (`01 13 00 00 00` and the string `Version: Beta_21\r\n`) before the
  language byte and the UTF-16 `THE SAVE GAME DATA…` banner. Its BILZ chunk and decoded payload retain
  the familiar trailing delimiters, but the Camera block, cell and SYSG records have the distinct widths
  documented [above](#prebuilt-science-save). The reader accepts this observed
  offline profile only; it does not write TPI saves.
- **Global save.** Theme Park Inc keeps the player's progress in a Global Master Save (`.GMS`), separate from park
  saves. `.GMI` is the same format, written for the game's internal "Refresh" snapshot. `ThemeParkIncGlobalSave`
  reads both; see [Global Master Save](#global-master-save-gms) below.
- **Balance settings.** `levels/Standard.sam` has 413 keys against 192 in TPW,
  156 of them shared. The additions are mainly staff simulation
  (`AllStaffConstants.*`: wages, happiness, experience, staff rooms). `Challenges.sam`
  is restructured around missions (`MissionNeededToActivate`, `ScriptIndex`,
  `CashPrize`, `GTPrize`). `sound.sam` and `high.sam` have identical key sets.
- **Themes.** Arabian, water, science and an intro level (`wIntro`) replace jungle,
  fantasy, hallow and space. Each theme folder has the same layout (`rides`,
  `shops`, `features`, `facilities`, `obstacles`, `sky`, `Music`, `Sound`,
  `terrain.wad`, `Standard.sam`, `global.sam`, `Hoardings.sam`, `scape.omp`).
  New top-level folders: `missions`, `PreBuilt`, `Sky`, `extents`.
- **Music banks.** `arabian` and `water` `MusicHD.sdt` are cut off at
  40,787,968 and 31,457,280 bytes (exactly 30 MiB for water). The installer's own
  file table lists these sizes, so the cut is on the CD, not in the extraction.
  Their entry tables list 143 and more tracks than fit; OpenTPW's SDT reader skips
  the 40 and 15 entries past the end and keeps the rest.
- **MTR.** All 7 `.mtr` members start with the word 777590191, unlike the
  6/1/1/0 TPW variant `MtrFile` accepts.

## Shared runtime components

- **Online:** Theme Park Inc ships the same `wea*` online DLL family as TPW
  (`weaauth`, `weachat`, `weacity`, `weamail`, `weanews`, `wearas`, `weaupload`,
  `weavote`, each as `…d`/`…r`), built 1999-09-28 to 2000-01-26. `weachatr.dll`
  (96,256 bytes) and `weauploadr.dll` (70,144 bytes) have the sizes of the patched
  TPW files in OFFICIAL-PATCH-RUNNER.md, but different hashes.
- **Audio:** `QMixer.dll` (built 1999-10-29), as in TPW's import list.
- **Executable:** `Game.exe` (linker 6.0, image base 0x400000) is SafeDisc-wrapped
  (`stxt774`/`stxt371` sections, high-entropy `.text`), like TPW's `TP.ICD`. Its
  section names name the bundled media code: `TQIA_DAT` and `IDCT_DAT` (TGQ/TQI
  video), `LBMPEG_D` (MPEG audio), `UVA_DATA`, `GRPOLY_D`. The executable was not
  decrypted or disassembled.

## Sound banks: Layer I verified on original data

Running the Layer I decoder over the Mac TPW data decodes every Layer I entry:
2,641 MPEG-2 22,050 Hz mono, 5 MPEG-1 44,100 Hz mono and 4 MPEG-2 22,050 Hz
stereo clips (2,650 total), without a failure, and every clip matches ffmpeg's `mp1`
decoder in length and to within 1 LSB. Theme Park Inc's banks hold 3,691
MPEG-2 22,050 Hz mono and 12 MPEG-1 44,100 Hz mono Layer I clips, plus Layer II
speech/music in the same modes as TPW; all decode except the clips past the end
of the two truncated music banks.

## Edition note: Mac Theme Park World

The Mac data differs from the Windows edition OpenTPW pins in its tests. The
differences found so far: QuickTime `.mov` movies instead of `.tgq`; 641 loose
`.lip` files instead of `lips.wad`; TrueType fonts in resource forks (the data
forks of its 17 `.ttf` files are empty); 324 RSE scripts against 308. Asset tests
that pin Windows hashes and counts fail on it for these reasons, which says nothing
about the readers.

## Possible follow-up work

1. ~~`.fsh` (`SHPI`, code `0x7B`) reader.~~ Done (October 10, 2026): `FshFile`
   decodes all 7,283 files and WAD members to RGBA, with `--inspect-fsh`; see
   [FSH.md](FSH.md). The [level viewer](#theme-park-inc-playable-slice) uses them (October 11, 2026).
   Not done: checking them against the original game's rendering. Opaque `0x24`
   palettes match the original's converter (`APPROX:COMPAT-014` retired).
2. Use the `.rss` evidence in the VM: name `LIMBO`'s second operand as the limbo
   duration and cite the source comments in the opcode handlers.
3. Other TPI save profiles, untraced prebuilt fields and the new `Standard.sam` keys,
   if Theme Park Inc support ever becomes a goal.

## Global Master Save (.GMS)

The player's progress across the worlds lives in a Global Master Save. "GlobaMasterSave" is the name the program
uses in its own log text. It is not a park save. The layout was traced in the Theme Park Inc executable, the scene
noCD build of `Game.exe`, by static analysis only:

- `0x0074C8F0` writes the file;
- `0x0074D110` loads it;
- the shared body is `0x0074D840` with its sub-serializers.

The file is uncompressed and little-endian. The loader rejects it when one of six markers differs.

| Part | Content |
| --- | --- |
| Header | `u32` world (0–2; the writer stores 3 when there is none), `u32` version 12, a `u32` and 8 bytes |
| Card table | 256 × 16 bytes (`0x0074B1F0`): Info.Id, a level 0–2, a flag and an amount. Empty slots have flag `0x7FFFFFFF`. The observed files use 16 slots |
| Words and bytes | including the mission number (object +0x105C; the loader then reads the world's `Miss%02d.sam`) |
| 5 × 48 bytes, `TATS`, 15 words, `AMTA` | untraced |
| Parks | `u32` count; per park a length-prefixed name (`arabian`, `science`, `water`, `wIntro`), then `0x0074FEF0`: two interleaved UTF-16 sign lines (`mSignNameA`/`mSignNameB`, 33 characters), `d_available`, `d_open`, `mParkNumber`, `mNameChanged`, `mAllResearchCompleted`, `mChallengesDone`, `mAllChallengesDone`, `mCurrentTechLevel` |
| `MEHT`, options | `0x0045A130`: four volumes stored as (on, level) word pairs (the last two named SpeechVolume and MovieVolume), AdvisorOn, TutorialOn, TooltipsOn, ConfirmDeleteOn, RMBScrollOn, RMBCancelOn, IsometricOn, AnimateMenus, GameSpeed, mZoomMin, mZoomMax, mFPSClip, MusicEnabled |
| History | `mFirstHourTime`, then 644 × 20 bytes `mHistory[i]` |
| Tail | `0x00592720` (a word and 92 bytes), `DTOT`, the shares game (`0x00620E30`: three company slots, `mNumberOwned`, `mPctCompanySharesOwned`, `mCompanyName`, `mCurrentValue`), `RAHS`, `0x00622D70` (count, bytes, a byte and three words), `MAPS`, the ambient sound tags (`0x005BF470`: count × 16 bytes) |

Three German retail saves read to their last byte with every marker in place. They are kept private and
checked by `ThemeParkIncGlobalSaveTests` when `OPENTPW_TPI_SAVES` names their folder. Sections without a traced
meaning are read and skipped, and nothing is written back.

The reader bounds park counts and names, map flags and ambient tags before reading
their sections. It rejects truncated sections, trailing bytes and unpaired UTF-16
surrogates in decoded sign lines or the company name. Valid surrogate pairs are
accepted through the field's final code unit; unused units after the first NUL
remain ignored. These text checks are OpenTPW's malformed-input policy. Synthetic
tests cover every truncated prefix and the maximum supported table sizes.

### `.GMI`: the refresh snapshot

`.GMI` is not a separate format. One save routine, `0x00745720`, takes a flag. Without the flag it writes a
normal save: the park as `.TPWS` and the progress as `.GMS`, and it then walks the parks (`arabian` first).
With the flag it writes the park as `.TPWI` and the progress as `.GMI` and skips that walk. Both go through the
same writer `0x0074C8F0`. The writer stores the flag at object +0x14EC, and the save routine later reads it there to pick
`.TPWI` or `.TPWS` for one of its file names. The writer does not pass the flag to the body and writes version 12 either way.

Only `0x00744E80` calls the save routine with the flag; its counterpart `0x00744E10` passes no flag, and the
autosave paths (`0x007765D0`, `0x00777120`) write `.GMS`. `0x00744E80` has one caller: when the options screen
(`0x004597C0`) applies a change that needs a reload, it saves the game as `Refresh`, re-initializes, and loads
`Refresh.GMI` back (`0x00744DF0`, the loader `0x007451E0` with the `.GMI` extension).

A second path loads a `.GMI`: the handler at `0x006A4E70` (reached through a pointer table) loads `restart.GMI`
the same way. No code was found that writes a `restart` `.GMI`, and none ships in the game data, so where that file
would come from is open.

No `.GMI` file was available, so all of this rests on the executable. `ThemeParkIncGlobalSave` reads one like a
`.GMS`, and the corpus test includes `.GMI` files when the folder has any.
