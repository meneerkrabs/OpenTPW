# Original objects: catalog, placement, animation and scripts

October 9, 2026. Status: **every original ride, shop, sideshow, feature, fixed
item and track upgrade of the four themes is catalogued, every one can be
placed with its original model, and every object script runs in the RSE VM
with its original animations.** This is not original gameplay: visitors,
particles/EVENT objects, ride-type controllers (coasters, karts, tours,
bumpers) and the economy are not implemented, so many rides
only play their create/idle clips. Everything below is read-only analysis of
the local original files; the original executable is encrypted (`TP.ICD`
strings are not readable) and was never run.

Code: `source/OpenTPW/World/Objects/` (`ObjectCatalog`, `ObjectSettings`,
`ObjectShape`/`ObjectFootprint`, `ObjectAnimations`, `ObjectAnimator`,
`ObjectAssets`, `ObjectRenderParts`, `OriginalObjectRuntime`,
`OriginalObject`, `ParkObjects`), `World/Level.Objects.cs`,
`UI/Layouts/ObjectBuildPanel.cs`. Tests: `ObjectCatalogTests.cs`
(`ObjectSettingsTests` public/synthetic; `ObjectCatalogCorpusTests` private,
inconclusive without `OPENTPW_GAME_PATH`).

## Catalog

Each theme has `levels/<theme>/{rides,shops,sideshow,features,upgrades}/*.wad`.
An archive is one object when one of its `.sam` files has `Info.Id`; that id is
the one the TPWI `SYSG` records use (docs/TPWS-PAYLOAD.md).

| Theme | Entries | Buildable | Rides | Shops | Sideshows | Features | Upgrades | OBJECT_NAMES bound | With script |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| jungle | 70 | 58 | 18 | 8 | 5 | 36 | 3 | 67 | 67 |
| hallow | 70 | 58 | 20 | 8 | 4 | 35 | 3 | 67 | 67 |
| space | 68 | 56 | 21 | 8 | 4 | 32 | 3 | 65 | 65 |
| fantasy | 66 | 55 | 17 | 8 | 4 | 35 | 2 | 63 | 63 |

Per theme, 6 entries are fixed items (`Info.DontApplyOffset 1`: bus, gates,
seaplane, lights, ferry, end; `Info.WhichUIType 4`) and 3 are tools without
occupied cells (Mystery ride 100, Buy Land 101, Clear Land 102). "Buildable" =
`WhichUIType` 0–3, not fixed, not a tool, not a track upgrade (upgrades attach to
a ride's track, which is not implemented). This is the executable's build-menu rule
(filler 0x4AAF70: the definition's `WhichUIType` equals the tab, `AddOn.UpgradesId`
is 0 and its model loaded; tools and fixed items have `WhichUIType` 4), and it gives
the same set on the whole corpus; the original also needs the item to be available or researched.

Settings layers (`ObjectSettings`): the category default file beside the
archives (`Rides.sam`, `Shops.sam`, `SideShow.sam`, `Features.sam`,
`Upgrades.sam`), then the object's own `.sam`; `Easy_<object>.sam` is layered last
in Instant Action when it exists (traced, STP-PPC 0x10119328), `Online_*.sam` is not applied. The
object loader 0x413C10 builds the same stack (the caller's base file, `<object>.sam`, in game type 2
`Easy_<object>.sam`). A shared archive file without `Info.Id` (only `coaster.sam`, in the 12
TrackMaster archives) is not part of that stack: for track type 3 the loader reads it into a separate settings
object that goes to the TrackMaster loader 0x42FAD0, and `ObjectCatalogEntry.SharedSettings` /
`CoasterDefinition.FromEntry` do the same here (its keys are `asTextureData`, `asCrossSection*`,
`asPylonControls`, `sCoasterType`, `sTrainType`, `asCarTypes`; no object or category file sets one,
so the coaster definitions equal the old merged ones). The generic `SettingsFile` cannot read quoted names or `---` blocks,
hence `ObjectSettingsFile`. Values stay raw (`Settings`, `Upgrades[n]`);
convenience accessors: `BuildCost` (`Upgrades[0].CostOfUpgrade`, "cash cost
when buying this item"), `InitialCapacity`, `HasQueue`, `IsChoosable`,
`RideTypeIndex` (`Info.RideTypeStringIndex`, an index into `ITEMTYPES.str`:
Totem 12 = "Vertical Drop", Belly Bounce 7 = "Bouncy Castle"), `Economy`
(price per use, cost of goods, chance of losing, all upgrade levels). No upkeep
field exists in any object or category file.

Members: main model `<archive>.MD2`, preview model `P<model>.MD2` with
`PreviewAnimationPath` (`Info.PreviewAnimType "m"`, `Info.PreviewAnimNum 1` →
`Ptotemm`, `Pspiderm1`), main script `<archive>.RSE` (else the only non-EventMap
RSE), animation members (below) and auxiliary geometry (vehicles, track pieces,
pylons). All 274 main models parse as geometry; all 911 animation members parse
and bind to their main model's nodes; 5,629 material slots resolve to textures
in `<archive>/textures`, `<archive>/gtexture` or `/levels/<theme>/sharetex`
(with the Low detail preset, TEXTUREQUALITY 0 or 1, `<archive>/stexture` and `/levels/<theme>/ssharete`
replace `textures` and `sharetex`; they hold every texture the full folders hold except six slots, which the search finds in `textures` (`[EXT:low-detail-texture-fallback]`: the low folders are searched first, then the full ones; a selected texture pack replaces the full textures by path, so with a pack the low set is not used), the startup
choice of 0x54DCF0) except two (hallow ogre upgrade: unnamed slot; space rocket: `s_leg3` not
shipped). 31 floor meshes have more than 16 texture slots and are split by
material for the 16-slot shader.

### Names (OBJECT_NAMES.str)

The `.sam` files carry no string index; the executable binds names by `Info.Id` through a
static table of 281 rows `(Info.Id, first, second)` at 0x7488D8 (the name getter 0x4146B0 walks it
by id; an id outside it gives an empty name). `ObjectNameTable` holds those rows (values derived
from the executable, no bytes committed); `first` and `second` are OBJECT_NAMES indices, `second` = 1
(the empty string) when the object has one entry ("Inca" + "Totem" for rides, one entry for the rest;
every `second` is 1 or `first` + 1). The texts are joined with a space (a trailing `-` of the first
text is cut and the two joined without it), as before (`ObjectNames.Join`); `DisplayName` comes from
the selected language's table. 262 of 274 entries bind; the 12 unbound ones are the three tools of each
theme (ids 100–102), which keep their `.sam` name here where the original shows an empty name. The English
name matcher this replaces missed the loudspeakers, gates, signs, lights, seaplane and several renamed rides
(Thrill Grill = "The Firepit", Bounce On Iggy = "Cosmic Bounce", Star Tours, Butterfly Nest, Kid Creosote), and
bound the fantasy Fountain (4417) and the space Crater (3407) to the wrong entry (now 246 and 359).
The table is Theme Park World's: Theme Park Inc ships another OBJECT_NAMES.str, so Theme Park Inc keeps the English-name matcher (`[EXT:tpi-object-name-matcher]`).
The four End items (ids 1605, 2605, 3605, 4605) bind to entry 0, the empty string; they keep their `.sam` name as display name.
The caller 0x412FB0 uses the archive's own text-file `NAME` first (bonus objects) and the table otherwise.

## Shape, footprint and rotation

`Info.Shape` rows (`ObjectShape`) use the symbol table of the executable (0x7396C8, parser
0x402720; 19 symbols, anything else is "Illegal map character", at most 20 columns). Each symbol has a cell
type and a compass bit (`ObjectShapeSymbols`): `.` free (0), `*` occupied (4), `Q` (3), `@` (1) and `#` (16)
(types of unknown placement meaning, counted as occupied here), `+` upgrade cells (11), entrances (9) `8` `6` `2`
`4` `O` with bits N E S W N, exits (10) `N` `E` `S` `W` `X` with bits N E S W N, and track ends (23) `D`
(no bit), `>` (E) and `<` (W). The bits are 0x01 N, 0x04 E, 0x10 S, 0x40 W. The loader 0x413410 keeps the
first entrance and first exit cell with their direction byte, so the open side belongs to the symbol, not to the
edge the cell lies on: an exit opens towards its bit, an entrance opposite to it (the guest walks in along the bit).
With text-down N (−V), text-up S (+V), right E (+U) and left W (−U) every corpus shape (209 access cells) opens
as before; the placement `0x528A70` uses exactly this mapping (retired RIDES-012, see the read round below). The bonus Snake's `W` is a west exit, not an occupied cell.

Local coordinates: `U` = column = model +X, `V` = rows counted from the **last**
text row = model +Z; one cell = 10 MD2 units. Evidence:

- 240 of 248 non-fixed main models span exactly [0, 10·width] × [0, 10·height]
  in X/Z (the other 8 are the Buy/Clear Land tools and the fantasy Strength
  Flower, −4…23 × 0…32 for a 2×3 shape);
- seat dummies: Belly Bounce `body12` (15, 0, 5) and `body13` (15, 0, 35) sit in
  the `2` and `S` cells, Staff Room `position01` (15, 0, 3) in its `2` cell, only
  with V counted from the last row;
- Easymode (cell map with path connection bits): the Belly Bounce queue (49–52, 22)
  ends at its `2` cell (52, 23) and its `S` cell (52, 26) joins the path at
  (52, 27); the Drinks Shop (`**`/`2*` at 43, 30) is reached from (43, 29) at
  column 0, which also fixes the X direction; Jungle Spray and the Staff Room
  likewise.

Rotation (`ObjectFootprint`): the save anchor (X, Y) is the cell of local
(0, 0) and rotations turn the grid rigidly: 0° (u, v) → (X+u, Y+v); 90° →
(X+v, Y−u); 180° → (X−u, Y−v); 270° → (X−v, Y+u). Verified in Easymode for 0°,
90° (Staff Room and Round Fountain footprints; the Staff Room entrance opens to
−X onto the path at (57, 15)) and 270° (Small Toilet entrances open to +X onto the
paths at x = 56). 180° and non-square rotated shapes follow from the same rigid
rotation but no save contains them; `SaveObject.TryGetFootprint` (save slice)
still leaves them unresolved, `ParkObjects.ImportOriginal` places them with the
catalog shape. Saved width/height equal the shape's (unrotated) size for all 11
Easymode objects.

Entrances/exits open to the outside across their footprint edge (first/last
row before first/last column); `OriginalObject.AccessPoints` gives each with
its outside cell. All 9 Easymode access points open onto a path cell or the
Belly Bounce queue (test `EasymodeObjectsMatchTheirCatalogShapesAndAccessCells`).

Placement (`ObjectPlacement.ModelToEngine`): MD2 (Y up) → swap Y/Z → rigid
rotation about the anchor → + 10·(X, Y) → × 0.2 → grid origin (the heightfield
mapping of MAP.md; sandbox: the 32×32 tile park). Fixed items use anchor
(0, 0) and no rotation: their models are authored in park coordinates (Jungle
gates span MD2 X 450…510 = cells 45…51, matching the saved 6×3 footprint at
45, 16). Base height = mean ground height of the footprint cells
(**approximation**: rides flatten their ground in the original, cf.
`Info.DontDeformBase` and the per-archive `.hmp` files, whose header repeats the
shape size, e.g. Totem 3×4, but are not decoded).

## Animations: ANIM_* → member

`ObjectAnimations`: the first operand of TRIGANIM/WAITANIM/LOOPANIM/
TRIGWAITANIM/TRIGANIMSPEED/TRIGANIM_CH/LOOPANIM_CH is a ScriptDefs animation,
whose initial letter is the member suffix; the second operand is the variant.

| ANIM | Letter | Members |
| --- | --- | ---: |
| 0 Create | c | 232 |
| 2 Idle | i | 76 |
| 3 Load | l | 59 |
| 4 Start | s | 51 |
| 5 Main | m | 465 |
| 6 End | e | 51 |
| 7 Unload | u | 14 |
| 9 Break | b | 77 |
| 10 Repair | r | 76 |
| 11 Other | o | 6 |

(Counts over all members of the form `<geometry member><letter>[n]`, including
vehicles and pylons.) Variant v plays `<model><letter>{v+1}`, or the unnumbered
member for v = 0. Evidence: exactly these ten letters occur as suffixes (plus
`d` twice, never requested; ANIM 1 and 8 are never used by any script); the
ScriptDefs names and letters agree one-to-one; variant counts match the scripts
(Totem `TRIGANIM 5 0` + `TRIGANIM_CH 5 1…9` ↔ totemm1…m10, Spider `WAITANIM 5 0…3`
↔ spiderm1…m4, Monkey `TRIGWAITANIM 5 0…6` ↔ monkeym1…m7, Bouncy `LOOPANIM 5 0/1`
↔ bouncym1/m2, Spider `LOOPANIM 9 1` ↔ spiderb2, Hyenas idle I1…I3 ↔ `TRIGANIM_CH 2
0…2`). Confidence: high for the letter mapping and variants (no counterexample in
1,451 plays), not traced in the executable. Of all requests in the corpus run, 6
have no member: fantasy `royaloo`, `fries`, `icecream` (5/0), `purse` (2/0, 5/0),
space `crys_b` (5/0) — those archives do not ship the clip.

Playback (`ObjectAnimator`) follows the channel records of the Feral Mac build
(docs/reverse/ANIM-plan.md §8, §13): 30 ticks/s (the Mac formula, and the PC frame (u32)(now − start) · speed · 0.03f on x87, 0x473490/0x4735d0; ride channels read the scaled clock 0x7b496c, the advisor the unscaled 0x7b4974);
plain opcodes use channel 0, `_CH` opcodes the channel in their operand
(sideshows use 0–2, the Totem 1–3). Channels update in ascending index, so for
each node component (translation, rotation, scale, mesh positions, texture
coordinates) the highest channel that writes it wins. A finished non-looping
clip becomes AnimID 14: frozen on its last frame, pose kept (RIDES-003), and
GETANIM(_CH) then returns −1 (otherwise the AnimID, 12 when empty). FLUSHANIM
clears only the deferred clip. The scripts' waits use the trigger's result
(`trunc(1000 × ticks / 30)` ms, 1000 for a missing member): TRIGANIM-family
opcodes store `max(ms − 300, 300)` and overwrite the script's single WAIT4ANIM
deadline; WAITANIM waits `max(unsigned(ms − 300), 300)`; LOOPANIM is a no-op
while its script-local token (`anim + (sub << 16)`) is unchanged, and the plain
triggers reset that token (Bouncy re-issues its idle loop every 500 ms);
TRIGWAITANIM continues once channel 0 shows the triggered AnimID;
TRIGANIMSPEED plays at `operand / 1000` times the script speed.

Quantised vertex tracks (0x1000) and per-corner texture-coordinate tracks
(0x10000, e.g. `fountainm`, `Coconutm`) play per instance. Texture-frame tracks
(trailer word 12 behind bit 0x2) select the frame of a model texture slot while
the clip plays — the Belly Bounce's alien eyes, the arcade and Bug TV screens,
the fixed gate's worm face, the queue-bin fires (RIDES-035; the mixing and
fallback rules are MD2-900..902) — and the renderer swaps the bound texture of
the material slots that show the slot, with no reallocation while the frame
holds. Node-state bit 0x10
(clip node lists, toggle tracks) hides the node's mesh, as the original's draw walk does (0x57A5E3 tests 0x8210 and still draws the children; RIDES-042 retired). The 12-byte block (0x4000) holds one stored pose per tick (duration + 1 poses
of `count` positions); the PC sampler (0x471860, 0x4718F7) replaces vertex i by entry `index[i]` of pose
`trunc(time)` with no blending (its "interpolation" weighs one address by t and 1 − t) and `ObjectAnimator` does the same
(see MD2-MODELS.md); not applied: path parameters (0x200/0x400).

## Scripts in the world

`OriginalObjectRuntime` loads the main RSE with `OriginalObjectEffects`, child
scripts from the same archive, and one `RideScriptWorld` per park
(`ParkObjects.ScriptWorld`) so FINDSCRIPTRAND/REMOTEVAR work across objects (bus →
traffic lights). Initial variables from the .sam layers: `VAR_CAPACITY` =
`Upgrades[0].InitCapacity`, `VAR_DURATION` = `Upgrades[0].InitDuration` (written only when above 0: its low byte clamped to
`UsageInfo.MinDuration..MaxDuration`, 0x4db565..0x4db64f; `Info.DurationUnit` is never used, Totem.RSE uses it as cycle count),
`VAR_RIDECLOSED` 0 (built and imported objects open; the saved ride state is not
decoded) — the sandbox Totem starts closed. Variables are written by name since
scripts declare different subsets.

Corpus run (`EveryObjectScriptRunsInItsParkWithOriginalAnimations`): all 262
scripted objects, every theme in one shared world, 60 s at 60 Hz: no faults,
258 played original clips (1,451 plays). EVENT and REPAIREFFECT play their
original sounds ([AUDIO.md](AUDIO.md)); their other effects stay unimplemented
(the unimplemented-effect hook still records them): ADDOBJ/KILLOBJ particles and
objects, TOUR and kart BUMP controllers (a water ride's BUMP goes to its controller, [TRACKS.md](TRACKS.md)), park clock, screams, reverb, light opcodes.

## Building (`ParkObjects`)

Footprint cells from the shape and rotation; per cell: inside the grid, terrain
rule (original levels: MAP blocked/water/entrance/fixed walkway, heightfield
holes, Easymode path cells and occupied non-object cells such as the Belly Bounce
queue — see MAP.md), not occupied by another object, a built queue cell or the
sandbox Totem. Queue paths for `Info.HasQueue` rides are laid from the cell
outside the entrance (`QueuePaths`, `Level.BuildQueueCell`; GUESTS.md "Queues"). Removing
an imported object frees its cells. In original levels, building now uses `ParkEconomy.TryBuild`: research, money and
golden-ticket checks apply, costs are charged and removal credits the economy
scrap value. The generic sandbox still builds free. These are
OpenTPW rules grounded in the data, not the original build checks (no slope,
path-connection or land-ownership rules). The ImGui build panel lists the
buildable catalog per category; R rotates, click builds centred on the cell, the
remove tool deletes the object under the cursor. Sandbox JSON saves still contain
only the Totem.

Original levels: `--load-original-level jungle` places the 11 Easymode objects
and 3 fixed items with their models and scripts (no more orange markers); themes
without a save get the Gates, Lights and Bus (the fixed items Easymode records;
which fixed items the original spawns, and when, is unknown).

## Interfaces for other slices

- Guests: `IRideVisitorBridge.Perform(ride, call)` receives every visitor opcode
  (`OriginalObjectRuntime.VisitorOpcodes`: HUSH, HOP, ADD/DELHEAD, LIMBO family,
  BOUNCE family, WALK family) and returns the opcode result (RSE-VM.md). Set it
  per object (`Runtime.Visitors`) or for new objects (`ParkObjects.Visitors`).
  Host inputs: `Runtime.SetVariable("VAR_LETMEON"/"VAR_LETMEOFF", id)` while
  `Script.InCriticalSection` is false. Geometry: `OriginalObject.AccessPoints`
  (entrance/exit cell + outside cell), `Cells`, `GetNodeTransform(node)` (seat
  dummies such as `HeadNN`/`bodyNN`), `Entry.HasQueue`, `InitialCapacity`, raw
  `UsageInfo.EntryCellStandPos*`/`ExitCellAppearPos*`.
- Economy: `Entry.Economy`/`BuildCost`/`Upgrades`, `Runtime.State`
  (`OriginalObjectState`: open, running, broken, on ride, completed cycles —
  cycles count VAR_RUNNING 1→0 transitions), `ParkObjects.ObjectPlaced/Removed`.
- Frontend: `ObjectCatalog.Load(theme).Buildable` with `DisplayName`,
  `Category`/`WhichUIType`, `PreviewModelPath`/`PreviewAnimationPath` (the
  build menu shows 3D previews; no 2D icons exist in the object archives),
  `RideTypeIndex` (ITEMTYPES.str), `BuildCost`.

## Official bonus content

The official bonus objects are separate WADs meant to be dropped into
`Data/levels/<theme>/<category>/`. OpenTPW reads them read-only from
`--bonus-data <dir>` or `OPENTPW_BONUS_DATA` (the directory containing `levels`,
or up to two levels above it; directory names matched case-insensitively) and
merges them into the catalogs; nothing is copied into the game data.

Without a flag the root is, in order: the saved bonus folder in `setup.json`
(`bonusPath`), then `<config>/bonus` when it holds a `levels` folder. An empty
`bonusPath` (the player chose Remove in Options > Game files) turns the bonus
objects off without deleting anything. Options > Game files can import the
original "Bonus content" folder or its `.zip` into `<config>/bonus`, which copies
only the `levels/<theme>/<category>/_name_N.wad` files (see
[SETUP.md](SETUP.md#changing-the-folders-in-game)). The startup log names the
source used.

The supplied set has **35** archives (`_name_N.wad`): jungle 4 rides + 4
features, hallow 4 rides + 1 sideshow + 4 features, space 3 rides + 2 sideshows
+ 5 features, fantasy 3 rides + 1 sideshow + 4 features; N runs 1…39 with gaps.
Members use `_name` (`_snake.md2`, `_snake.RSE`); `Easy__name.sam` overlays are
ignored like the base game's. Info.Ids continue the theme ranges (jungle rides
1118–1121, features 1436–1439, …) and none collides with a base object of the
same theme (the fantasy bonus Giant Puzzle reuses space's 3301, which is not in
the fantasy catalog). Category defaults come from the game's own `Rides.sam` etc.

The archives load as the original's scan does (tp.exe `0x413930`, once per category: Rides,
Features, Upgrades, Shops, Sideshow, `0x413140`): a category's game and bonus archives are one
directory in name order. Each archive is named by `0x41ec30`: the file name without `.wad`,
and a name with exactly two underscores loses the second one and what follows, then a leading
lower-case `p` (`_snake_3` and `p_snake_3` are both `_snake`, the name its members use,
`0x41eb90`). An archive whose name the category already holds is not read (`0x41ede0`,
`_stricmp`), so the first in name order wins. A definition whose `Info.Id` is taken still
loads, but every lookup by id finds the first definition (`0x412e90`, `0x412ef0`) in the
category order above.

Names: bonus objects are not in OBJECT_NAMES. Each archive has UTF-16 text
files per language (`english.txt`, `German.txt`, `Dutch.txt`, … also languages
the game does not ship) with `NAME`, `SIGNA`, `SIGNB` sections; `DisplayName`
uses the selected language, then English, then the `.sam` name (never fails; the original reads only
the selected language file, 0x41F350, and falls back to the id table, which holds no bonus id except
3301: RIDES-025).
E.g. `_snake_1` (.sam "Snake") is "Slither" — a name the base OBJECT_NAMES
already reserves at jungle index 22 — and "Rutschbahn" in German.

Tests (`BonusContentMergesIntoTheCatalogsAndRuns`, private, inconclusive without
`OPENTPW_BONUS_DATA`): catalog counts with/without bonus, every bonus model
parses and renders into parts, every animation binds, every script runs 30 s
without faults. The native smoke test also builds the first bonus ride when the
bonus root is set (observed: Dizzy Dinos animating).

## Build rules (tp.exe's validator)

The build tool's hover (`0x5234D0`) and click (`0x524960`) run the placement `0x528A70` over the footprint;
each cell goes through cell op `0x204` (`0x532FC0`) into the validator `0x535670` with the cell type the
footprint symbol asks for (4 for an occupied cell). For an object the validator refuses a cell flagged
NoModify (0x20) or unowned (0x40), a fence post or track piece on the second layer (types 0x19, 0xB, 0xD,
0x10, 0x12), a queue and any other non-empty type, and the build when the pending cost exceeds the balance
(`0x52FBC0`, unless `0x785A2C`); it allows an empty cell, a path cell without NoModify (`0x535D21`, the
commit then clears the path, `0x534549`) and an object cell whose owner has a lower **overwrite priority**:
the hover loads the placed type's `Info.OverwritePriority` (type record +0x54, schema `0x745020`) into
`0x818C10`, the validator the owner's into `0x818C14` and compares them (`0x535D66`). On commit cell op 0x87
demolishes every such owner whole (`0x527EE0`, crediting its scrap value) before the new footprint is
written. The path and queue tools use the same comparison with `0x818C10` = 2 and 3, so they may replace
scenery. In the shipped catalogues rides, shops and sideshows have priority 5, small scenery (bushes,
flowers, tiny rocks, speakers) 1 and the other features 0.

OpenTPW follows the overwrite priority for objects (`ParkObjects.Check`, `TryPlace`) and refuses track,
path and queue cells by their cell type (`Level`'s `IsReserved`); objects on path cells, the outside cells'
path rule and paths or queues over scenery are still open (RIDES-018).

## Remaining gates

Path parameters and the draw-path reader of texture frames; EVENT objects/particles (their sounds play, [AUDIO.md](AUDIO.md));
TOUR controllers, kart controllers and track building (karts, water ride jumps and
tunnels; Splish Splash runs its BUMP controller on a built circuit, [TRACKS.md](TRACKS.md)); visitor simulation behind the bridge; `.hmp` ground deformation;
the open build rules above (RIDES-018); animation tick rate; the placement meaning of the cell types 1, 3, 11 and 16
(`@`, `Q`, `+`, `#`); the object-list update route (RIDES-032).

Open point, not registered and not implemented (found in RIDES round 2): in Instant Action (game type 2) the
original's category walk 0x413930 does not load an object that has no `Easy_<object>.sam` at all
(0x413AC4..0x413ADD: `Easy_` load result != 1 jumps to 0x413B3A, which runs the object's deleting destructor).
OpenTPW layers the `Easy_` file only when it exists and loads every object. Following the original would change
which objects the Instant Action catalog holds (for instance the jungle coaster1, coaster3, incagod, tourride and
volcano archives have no `Easy_` file), so it needs a decision first.

## Approximation register

Every value or rule below is not taken from original data or hard evidence; each
is tagged `// [APPROX:RIDES-NNN]` at its site (paths under `source/OpenTPW/`) and
logged once as a warning at the first catalog load (`RidesApproximations`).
Original-data values are tagged `// [DATA:<file>:<field>]`.

| Id | Site | Current value / rule | Evidence needed |
| --- | --- | --- | --- |
| RIDES-003 | World/Objects/ObjectAnimator.cs | Finished clip holds its last pose (AnimID 14 path traced: 0xa7498, 0xa68ac, 0xa694c); the next trigger restores against the ended clip, while the original passes the AnimID-14 slot as the old clip (0xa692c–0xa6948) | Capture after a clip ends; the object's AnimID-14 table slot; the ride-data sources of object flags 0x8 (stop) and 0x10 (hold) |
| RIDES-011 | World/Objects/ObjectShape.cs | `+` is an overlay cell over the parent ride's track, the track ends `<` `>` station connections. The original placement `0x528A70` stamps `+`, `#` and the ends as occupied type 4 (`0x5290C7`–`0x5290EE`), checks `+` with cell op 0x10B and `#` with 0x110 and on commit writes the flags word 0x10 on the cell and its second-layer cell (`0x52950D`); an end with a direction needs the cell beyond it and on commit makes it type 0x15 (`0x5295F7`) | What cell ops 0x10B/0x110 require and what type 0x15 is |
| RIDES-014 | World/Objects/ParkObjects.cs:153 | Base height = mean footprint ground height | `.hmp` format, captures on slopes |
| RIDES-015 | World/Objects/ParkObjects.cs | Imported and built objects start open. The original constructor `0x4DB090` opens a built object, then closes one whose type has `Info.HasQueue` (def+0x40; `0x4DB712`–`0x4DB779`: open word 0, VAR_RIDECLOSED 1), so built rides with a queue start closed | TPWS ride state (save loader `0x415270`) |
| RIDES-018 | World/Objects/ParkObjects.cs:120 | Build rules: grid, the original's map-start cell types (holes refused; `Blocked` surface cells buildable, as in tp.exe), save paths/objects, overlap by `Info.OverwritePriority` (a lower-priority object gives way and is demolished, as in tp.exe), owned land; no slope test (none in tp.exe either); Level enforces economy purchases | Objects on plain path cells (tp.exe allows them and clears the path), the entrance/exit outside cells' path rule, and paths and queues over scenery |
| RIDES-019 | World/Objects/ParkObjects.cs | Levels without a save get Gates and Lights at setup, as the original's world setup (0x5156A0); the bus, seaplane and Ferry are placed when the arrival update first sends them (0x51A2F0, see GUESTS.md "Arrivals"). End is not created: the original creates it through 0x516B00 from the message handler 0x503D40 (message type 0x11). A Theme Park Inc world without a prebuilt save keeps a Bus from setup | What sends the End message; how Theme Park Inc places its arrival vehicle |
| RIDES-021 | World/Level.Objects.cs | The cursor ray picks the clicked cell on the Z = 0 plane. The anchor taken from that cell is the original's (`0x528B44`: the cell less (W/2, H/2), then the rotation's 5 × 5 window offsets), so the click is local (W/2, H/2) when each side is odd or at most 5 cells; the only shipped exception is space 3104 Orbiter (6 × 3: local (3, 1) at 0°, (2, 2) at 90°) | The original's mouse-to-cell pick (`0x524960` → `0x528A70`) |
| RIDES-023 | World/Objects/OriginalObjectRuntime.cs:125 | Completed cycle = VAR_RUNNING 1 → 0 | Original cycle/income accounting |
| RIDES-025 | World/Objects/ObjectCatalog.cs | Bonus name: selected language file, then English, then the .sam name. The original reads only `<selected language>.txt` (0x41F350) and falls back to the id table (empty for bonus ids except 3301); the English and .sam fallbacks are an OpenTPW extension for languages an archive lacks (third-party content) | None needed for the shipped archives (all 35 carry the four shipped languages) |

| RIDES-028 | World/Level.Objects.cs | Non-walkable exit outside cells, and entrance outside cells without a queue path, use the nearest walkable path (a one-cell queue). The original placement `0x528A70` lays a queue cell at the entrance's outside cell and a path cell at the exit's (option `0x81B0CC` on; only an entrance path cell with it off) and refuses an outside cell off the map | The link ops 0x81/0x85/0x86 of the laid cells |
| RIDES-031 | World/Objects/ObjectShape.cs | Shape symbols `@` (type 1; only in the Theme Park Inc road signs, "`*@*`" and "`*@@*`"), `Q` (3) and `#` (16) count as occupied here. The original placement `0x528A70` stamps `@` as a linked path cell inside the footprint (flags word 0x20, `0x529173`–`0x529208`), `Q` as a queue cell (type 3) and `#` like `+` (RIDES-011) | A path and queue cell model inside footprints; TPI placement of a sign over a path (`[EXT:tpi-viewer]`, docs/THEME-PARK-INC.md) |
| RIDES-032 | World/Objects/ObjectAnimator.cs | Every instance takes the callback route. PC: render callback 0x463030 (model record +0x140, set at 0x4638bd) poses via 0x44e380(inst, 1) and sets instance flag 0x200000; the list pass 0x44e410 updates instances without it via 0x473c70(inst, 0, 8) without posing and clears it on the others; object flag 0x00400000 is set at 0x4737e8 when a non-looping clip ends with nothing deferred, flags & 0x10 set and & 0x8 clear (also sets 0x40000000, holds the last frame). The list-pass route is not modelled | The renderer's call site of the +0x140 callback |
| RIDES-035 | World/Objects/ObjectAnimator.cs | Texture-frame tracks apply to rendering (MD2-900..902). PC writer: clip update 0x4721F0 runs 0x470C90, which writes the slot record at model+0x50+slot·8 (frame at +2, dirty bit 0x04 in byte +1); no reader of the frame or the dirty bit was found, so the binding follows this port's material-slot mapping | The draw-path reader of a texture slot's current frame (a runtime write watchpoint on a slot's +2, or the D3D texture-bind code) |
| RIDES-040 | World/Objects/ParkObjects.cs | Theme Park Inc fixed scene records saved at (0, 0) use their local model origin | The original fixed-scene transform fields and a game capture |
| RIDES-044 | VM/RideVM.cs | The scheduler clock that GETTIME and the WAITANIM(_CH) deadlines read starts at 0 when the park's script world is created and advances with the script ticks. The PC clock is the app-lifetime scaled timer 0x785970 (started at 0x54DCF0, read by 0x402D70, set only by the save writer 0x4164C0 and loader 0x415270, never reset for a new park), so a new park's first script sees the time since program start; the two differ only in absolute GETTIME values, since every deadline is relative | GETTIME's PC handler (to show it is only used for differences) |
| MD2-900 | World/Objects/ObjectAnimator.cs | A texture-frame slot mixes like the node components: ascending channels and tracks, the last non-null frame wins (hyenasm1 carries two tracks on one slot; the constant 0 loses to the face cycle), null before the first key keeps the previous frame (initially 0), and a slot no clip addresses shows frame 0 again; the fixed-item pose hold is not extended to frames | The original draw-path reader of a slot's current frame (the clip update 0xa56f8 only writes it) |
| MD2-901 | World/Objects/ObjectAnimator.cs | Texture-frame keys naming a frame outside the slot's frame names, or a slot the model does not have (Pmegacostm slot 12), are ignored | The original's behaviour on an out-of-range frame or slot |
| MD2-902 | World/Objects/OriginalObject.cs | A frame whose art no searched texture directory has (including Theme Park Inc `tvsim` slot 39 frame 3, `lt1.tga`) keeps the effective selected frame; the renderer registers decoded-art availability with the animator so save history retains that visible frame | The original's fallback for a missing frame file |

### Retired and reclassified (RIDES round 2, October 14)

Traced in tp.exe (`TPW-EXE`) and no longer approximations:

- `APPROX:RIDES-009`: shared `.sam` files are not part of the object's settings stack (0x413C10); `coaster.sam` is read on its own for track type 3 and given to the TrackMaster loader 0x42FAD0 (`SharedSettings`). The values were already identical.
- `APPROX:RIDES-010`: names bind by Info.Id through the table at 0x7488D8 (see Names).
- `APPROX:RIDES-017`: the build-menu filler 0x4AAF70 lists `WhichUIType` = tab, `AddOn.UpgradesId` = 0, model loaded and available; the corpus agrees. The two land-tool entries it appends to tab 3 belong to the UI register.
- `APPROX:RIDES-022`: the low-detail texture folders (`stexture`, `ssharete`) are used exactly when the active detail has TEXTUREQUALITY 0 or 1 (the Low preset; startup 0x54DCF0 → 0x4688B0). `ObjectAssets.UsesLowDetailTextures` follows the preset when an object's textures are looked up (the original picks once at startup, so objects loaded before a preset change keep their textures).
- `APPROX:RIDES-036`: the UV pass gate (0x47219A) is open for authored nodes: option bit 0x10 is never set and node bit 0x4000 is only written for the generated ground-conforming base node (0x46A81D).
- `APPROX:RIDES-041`: the PC sampler reads the 12-byte block (0x4718F7); see MD2-MODELS.md. The earlier finding that no reader existed was wrong.
- `APPROX:RIDES-043`: objects load in set mode (loader 0x461F10 strips header flag 4 unless the TrackMaster loader asks for it). The Add-mode remainder for TrackMaster coaster models moved to COAST-026.

Reclassified as OpenTPW conventions (no original rule to recover): `[EXT:md2-engine-scale]` (formerly `APPROX:RIDES-026`, the 0.2 presentation scale),
`[EXT:malformed-texture-coordinates]` (formerly `APPROX:RIDES-030`, non-finite texture coordinates drawn as (0, 0); the original passes raw floats to Direct3D) and
`[EXT:trig-anim-speed-guard]` (formerly `APPROX:RIDES-038`: the corpus has 4 TRIGANIMSPEED uses, all literal 1800–4000, so the guard against 0 and negative speeds only meets malformed input).

Narrowed: RIDES-011, 012, 019, 024, 025, 031, 033, 035 and 044 (rows above); RIDES-013, 015, 021, 023, 028, 032, 040 stay as they were.

### Read round (tp.exe, October 15)

The object placement routine `0x528A70` (preview, check and commit of the build tool) settles two items:

- `APPROX:RIDES-012`: the access cells' outside side is read from the shape grid's direction byte, turned by the build rotation (`0x4D8C20`):
  an entrance opens opposite to its bit (`0x529323`, table `0x529D90`), an exit along it (`0x529239`, table `0x529D3C`), N −V, E +U, S +V, W −U,
  as `ObjectFootprint.OutwardDirection` does.
- `APPROX:RIDES-013`: the footprint is stamped by the rigid rotation about the anchor cell (`0x528F62`; the parser `0x402720` reverses the rows),
  for every rotation and any width and height, as `ObjectFootprint.ToGrid` does.

Narrowed by the same round: RIDES-011, 015, 028 and 031 (rows above).

Imported objects share their economy instance with the guest payment bridge;
shop/sideshow payments and ride-use statistics use that instance. Open/closed
state is mirrored before visitor payments. Imported fixed items absent from the
save economy register without charge. The developer Totem only links where its
Jungle Info.Id is in the running theme catalogue.
