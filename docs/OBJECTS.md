# Original objects: catalog, placement, animation and scripts

October 9, 2026. Status: **every original ride, shop, sideshow, feature, fixed
item and track upgrade of the four themes is catalogued, every one can be
placed with its original model, and every object script runs in the RSE VM
with its original animations.** (Updated October 18, 2026: the scripts' other
effects are served as well, visitors, particles, sounds and their object records, and the ride-type controllers; what is
left is listed with the corpus run below.) Everything below is read-only analysis of
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
45, 16). Base height = the midpoint of the lowest and highest terrain vertex under the footprint (`0x466D70`; RIDES-014: the earlier mean of the cell heights
stays only for a park-resume save without `BaseHeightRule`)
(**approximation** for the blend: rides flatten their ground in the original, cf.
`Info.DontDeformBase` and the per-archive `.hmp` files, whose header repeats the
shape size, e.g. Totem 3×4).

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
coordinates) the highest channel that writes it wins. A finished clip with
nothing deferred takes one of three routes by the instance flags (`0x47376E`): an object that has a clip beyond AnimID 0 becomes
AnimID 14 (a non-looping clip: frozen on its last frame, pose kept), an object whose only clips are AnimID 0, the construction
clip C (88 of the 274 catalog entries, instance flag 0x10 set by `0x473e30`), holds its last frame as AnimID 12 whether the clip
loops or not (`0x4737D7`), and the advisor model alone (flag 0x8) stops (`0x473778`); `APPROX:RIDES-003` retired.
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
the fixed gate's worm face, the queue-bin fires (RIDES-035 retired; the mixing and
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
`VAR_RIDECLOSED` 1 for a built ride whose type has `Info.HasQueue` (the constructor `0x4DB090` opens the object and then closes it, `0x4DB712`–`0x4DB779`: RIDES-015 retired; the player opens it) and 0 otherwise; imported objects open (the saved open word `mCanLoad` is SAVE-006) — the sandbox Totem starts closed. Variables are written by name since
scripts declare different subsets.

Corpus run (`EveryObjectScriptRunsInItsParkWithOriginalAnimations`): all 262
scripted objects, every theme in one shared world, 60 s at 60 Hz: no faults,
258 played original clips (1,451 plays). EVENT and REPAIREFFECT play their
original sounds ([AUDIO.md](AUDIO.md)); ADDOBJ keeps the particles (types 1-2) and sounds (types 3-10; type 10 from the object type's own sound list, [RSE-VM.md](RSE-VM.md#the-object-types-own-sound-list-type-10)) it starts under their slots and KILLOBJ, FADEOBJ,
SETOBJPARAM and a script's end reach them ([RSE-VM.md](RSE-VM.md), "The sounds an ADDOBJ keeps"); the screams, the TOUR, BUMP and COAST controllers ([TOURS.md](TOURS.md), [TRACKS.md](TRACKS.md),
[COASTERS.md](COASTERS.md)), DIPMUSIC and SETREVERB (through the sound service) are served. What the unimplemented-effect hook still records
(`RideVM.UnimplementedEffects`): the bounce riders' node and base words where the rider is not rendered at its node.

## Building (`ParkObjects`)

Footprint cells from the shape and rotation; per cell: inside the grid, terrain
rule (original levels: the map start's cell types, heightfield holes, the Easymode
save's occupied non-object cells such as the Belly Bounce queue — see MAP.md), no
NoModify path (a plain path is cleared), no object of an equal or higher overwrite
priority, no queue, track or other typed cell and no sandbox Totem; then the
entrance's and exit's outside cells ("Build rules" below). Queue paths for `Info.HasQueue` rides are laid from the cell
outside the entrance (`QueuePaths`, `Level.BuildQueueCell`; GUESTS.md "Queues"). Removing
an imported object frees its cells. In original levels, building now uses `ParkEconomy.TryBuild`: research, money and
golden-ticket checks apply, costs are charged and removal credits the economy
scrap value. The generic sandbox still builds free. The cell rules
follow tp.exe's validator ("Build rules" below; owned land included, no slope test). The ImGui build panel lists the
buildable catalog per category; R rotates, click builds centred on the cell, the
remove tool deletes the object under the cursor. Sandbox JSON saves still contain
only the Totem.

Original levels: `--load-original-level jungle` places the 11 Easymode objects
and 3 fixed items with their models and scripts (no more orange markers); themes
without a save get the Gates, Lights and Bus (the fixed items Easymode records;
which fixed items the original spawns, and when, is unknown).

## Hoarding (closed, broken down, condemned and upgrading)

The original raises a hoarding of boards around an attraction whenever it closes, and lowers it again when the
attraction opens (`ObjectHoardingGeometry`, `ObjectHoardingState`, `ObjectHoardingRenderer`). It is presentation only:
the state lives in the object's runtime, no state hash reads it, and the park-resume snapshot keeps it
(`OriginalObjectRuntimeSnapshot.Hoarding`, written only once a hoarding has stood).

- **Boards.** The type loader (tp.exe `0x00413C10` → `0x004629D0`) builds one board per side bit of the object's
  `Info.Hoarding` block for every type that is not a fixed item (`0x00452E70`). The block is read by the map parser
  `0x00402720` with the hoarding table `0x007397B0` (`^` +V, `]` +U, `_` −V, `[` −U, `F`, `7`, `L`, `J` the corners, ...):
  a space takes no column and the last text row is row 0. A board is a 10 × 10 unit quad on the cell's edge, drawn from
  both sides, its ends pulled half-way to the second nearest vertex of the model's ground node (the node with most
  vertices on the footprint's boundary corners at height 0, `0x0046A5A0` → `0x00469A80`); a 1 × 1 grid uses fixed pulls.
  A type needs at least two such ground vertices. The boards are ordered by the angle of their middles round the grid's
  centre, starting at the upper left and going counterclockwise (with +U to the right and +V up: the left side, the
  bottom, the right side, the top), which is the order they rise in. All 129 shipped
  types with an `Info.Hoarding` block get one.
- **Terrain.** A placed object's boards stand with each end halfway between its base and the terrain corner the end maps
  to over the footprint box (`0x00454190`, `0x00454050`: the fraction runs over the box's first and last cell indices).
- **Texture sets.** Four textures of the level's `MiscMesh\textures` (`Closed`, `Hoarding`, `Condemn`, `Upgrade`; `.wct`
  in Theme Park World, `.fsh` in Theme Park Inc). The model call `0x00454550(mode)` picks the set: 1 the closed set
  (unless another set stands and is not sinking), 2 the broken-down set, 4 the condemned set and 8 the upgrade set.
- **Who calls it.** Every closing that writes the open word 0 and `VAR_RIDECLOSED` 1 calls mode 1: the player's and the
  park door's close (`0x004DF300`), a blocked exit (`0x004DF150`), the maintenance request (`0x004DFE30`), the repair
  tail's re-close of a coaster (`0x004DFD62`), `SetState` 1, 2 and 4, and a built object with a queue
  (`0x004DB78B`). The breakdown calls mode 2, or 4 when the ride is condemned, before its `SetState` (`0x004E14E0`),
  and the upgrade request calls mode 8 after its `SetState(2)` (`0x004E0050`). Every opening calls `0x004547C0`
  first: the queue re-evaluation `0x004DE1F0`, `0x004DF390`, the repair and upgrade end `0x004DF8F0`, the maintenance
  cancel and the upgrade cancel.
- **Rise and fall.** Each render frame (`0x0044E410` → `0x004548F0`, real time, on screen or not) a rising hoarding
  gains 0.2 of its height per second and a sinking one loses 0.3. Board *i* of *n* stands at
  clamp(1 − ((i / n)·0.8 − (level − 0.25))·4, 0, 1) of its height (odd boards are 0.8 boards), and its texture
  rises with it (`0x004543C0`). A hoarding stops rising on the first frame after it is up, and leaves the scene, back on
  the closed set, on the first frame after it is down.
- **Saves.** An original save's RideSystem record keeps the flags and the level (`0x004647A0`: flag word bits 0..6,
  dword +0x22), so an imported park shows its closed rides' hoardings as they were; the import's close of an
  attraction saved closed makes no model call. A park without terrain (the sandbox) has no hoardings, as the type
  loader's ground-node search needs the heightfield.

## Interfaces for other slices

- Guests: `IRideVisitorBridge.Perform(ride, call)` receives every visitor opcode
  (ADD/DELHEAD, BOUNCE family, WALK family; HUSH, HOP and the LIMBO family are
  VM-internal, RSE-VM.md) and returns the opcode result. Set it
  per object (`Runtime.Visitors`) or for new objects (`ParkObjects.Visitors`).
  Host inputs: `Runtime.SetVariable("VAR_LETMEON"/"VAR_LETMEOFF", id)` at any
  time (the original host never looks at the critical flag). Geometry: `OriginalObject.AccessPoints`
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
3301); the fallbacks are `[EXT:bonus-name-fallback]`.
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
NoModify (0x20, the map start's initial path) or unowned (0x40), a fence post or track piece on the second layer
(types 0x19, 0xB, 0xD, 0x10, 0x12), a queue and any other non-empty type, and the build when the pending cost
exceeds the balance (`0x52FBC0`, unless `0x785A2C`); it allows an empty cell, a path cell without NoModify
(`0x535D21`) and an object cell whose owner has a lower **overwrite priority**: the hover loads the placed
type's `Info.OverwritePriority` (type record +0x54, schema `0x745020`) into `0x818C10`, the validator the
owner's into `0x818C14` and compares them (`0x535D66`). On commit cell op 0x87 (`0x5344E5`) demolishes every
such owner whole (`0x527EE0`, crediting its scrap value) before the new footprint is written, and every
footprint cell is cleared with ClearCell `0x5367A0(0, 0)` before its type is written (`0x52910C`), so a path
cell under the footprint goes whatever the priorities (op 0x87 already clears it when the new priority is the
higher, `0x534549`). The path case of ClearCell refunds nothing. In the shipped catalogues rides, shops,
sideshows, upgrades, toilets, staff rooms, fantasy's Large Sign and the fixed items have priority 5, the
small scenery (bushes, flowers, small rocks and trees, statues, speakers; 45 types) 1 and the other 48
features 0 (test `OverwritePrioritiesAndAccessCellsFollowTheTypes`).

**The path and queue tools.** The hover (`0x5234D0`, `0x524571`) and the click (`0x524960`, `0x527200`,
`0x527218`) set `0x818C10` to 2 for the path tool and 3 for the queue tool, and the validator compares it
with the owner's priority before any path or queue rule, so a path or queue may be laid over a feature of
priority 0 or 1 but over no ride, shop, sideshow, toilet or staff room. The commit's first line pass
(`0x536100(0x87, ...)`) demolishes each such feature whole before the cells are typed.

OpenTPW follows these rules: `ParkObjects.Check` and `TryPlace` (overwrite priority, a plain path cell under
the footprint cleared through `ParkPathBuilder.Remove`, a NoModify one refused, the access cells by
`Info.HasQueue`, refusing with `AccessBlocked`, laid as the section below says), `ParkPathBuilder.ObjectGivesWay`/`DemolishObject` and the
queue builder's `givesWay` (`QueuePaths`), and `Level`, which hands the cell map to these hooks and refuses
track and queue cells by their type (`IsReserved`). Guests standing on a cleared path cell are set down on the
nearest path cell (`GuestSimulation.SetDownOffCells`). Still open (RIDES-018): the second-layer link tests
`0x53AE00`/`0x53AE90`, the types whose record +0x288 is 1, and what tp.exe does with a guest inside a new footprint.

## Remaining gates

Path parameters and the draw-path reader of texture frames; EVENT objects/particles (their sounds play, [AUDIO.md](AUDIO.md));
water ride and kart jumps and tunnels (the track tool and the BUMP controllers of the water rides and go-karts are built, [TRACKS.md](TRACKS.md)); the tour controllers are described in [TOURS.md](TOURS.md); visitor simulation behind the bridge; `.hmp` ground deformation;
the open build rules above (RIDES-018); animation tick rate; the coaster module's cell marker 0x15 beyond a track end (RIDES-011); the object-list update route (RIDES-032).

## The cells outside an entrance and an exit (RIDES-028 retired)

The placement `0x528A70` validates and lays them (`ObjectAccessCells`; `APPROX:RIDES-028` is retired). The build tool sets the queue option `0x81B0CC` from the type's
`Info.HasQueue` (`0x52FA90`, `0x527986`). For a queue type the entrance's outside cell is validated as an object cell (op `0x204`: empty ground, a path cell without
NoModify, an object of lower overwrite priority) and the exit's as a path cell (op `0x201`: empty ground or any path cell); the commit lays a queue cell outside the entrance
(the entrance cell's link byte and queue link take the bit toward it, the queue cell the opposite bit as link and queue link, the entrance's owner word and the flag word 0x20)
and a NoModify path cell outside the exit, linked by the path link writer. For any other type the entrance's outside cell is validated as a path cell and laid as the exit's is,
and the exit gets nothing. A cell off the map refuses the placement. The cells are free (the commit runs with the free flag `0x8186D4` set, so the cell setter
charges nothing), unless the balance is negative, when the cell setter refuses them. Easymode's eight rides, shops and toilets carry exactly these cells
(tested). The remove tools take these cells although they are `NoModify` (`[EXT:removable-access-cells]`, [PATHS.md](PATHS.md)); the commit's op 0x87 on them demolishes a lower-priority
owner, and in the corpus every type with a queue has one entrance and one exit and every other type at most one entrance and no exit. The placement also lays the first track tile of a kart or water ride (option `0x81ECAC`, `0x5286A0`), which is not modelled (the station-derived start,
TRACK-005).

## The cell record the placement writes

The original's map cell has a type (`+8`), the link byte (`+0xC`), the queue link (`+0xD`), a flag word (`+0xE`), the owner word (`+0x10`) and the
MAP byte (`+0x26`). The placement `0x528A70` writes, for each footprint cell of a nonzero shape type: the type (the shape's, with 11, 16 and 23
stored as 4), the owner word (the cell word `y × 128 + x + 1` of the object's anchor) and by symbol the flag word: `@` a type-1 path cell with
0x20 (NoModify) linked to its path neighbours, `+` and `#` the flag 0x10 on the cell and on its track-layer cell. Easymode's saved cells
(`mParentID`, record +11) carry the owner words exactly (tested on every footprint cell of the Jungle park). Track cells are the types 0xB, 0xD,
0x10 and 0x12 (and the park fence post 0x19), the predicate `0x53AD30`; the extension cells 0xC and 0x11 name their head through the owner word.

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
| RIDES-011 | World/Objects/ObjectShape.cs | The cells of `+` and `#` are stamped as the original placement `0x528A70` stamps them (object type 4, the owner word, the flag word 0x10 on the cell and its track-layer cell, `0x52950D`–`0x52951C`) and validated as its add-on mode does (a cell of a straight, unflagged tile of the selected ride: `TrackBuilder.CanStandAddOn`, `Level.CheckAddOn`, `0x535953`–`0x5359B3`); a track end `<` `>` `D` needs the cell beyond it on the map (`0x5295A8`). The type 0x15 the commit writes on that cell with the ride's owner word (`0x5295F7`–`0x52963F`) is not written: it is the coaster module's cell marker (`0x452D30`, called by the coaster tool `0x434412`, `0x443057`, `0x444EE7`, `0x445B50`), which OpenTPW's coaster builder neither writes nor reads | What the coaster tool reads the marker for (the validator's modes 0x15 and 0x16, `0x535B53`) |
| RIDES-014 | World/Objects/ParkObjects.cs | Base height = (lowest + highest) / 2 of the terrain vertex heights at the corners of the footprint's shape cells (`0x466D70`, cells with a non-zero type nibble, `0x469930`; the vertex is `ftol(lerp + 0.1)` of `0x454050`, equal to `ObjectFootprint.ToGrid` of the corner: `ObjectBaseHeightTests`). The original then blends the ground flat under the ride (`0x467030`, `0x57ACE0`), which is not modelled; a park-resume save from before the rule restores with the earlier cell mean (`BaseHeightRule` absent) | The blend; the instance words +0xF0/+0xF4 taken as the shape size; an instance height captured on a slope |
| RIDES-018 | World/Objects/ParkObjects.cs | Build rules: grid, the original's map-start cell types (holes refused; `Blocked` surface cells buildable, as in tp.exe), plain path cells cleared and NoModify ones refused, overlap by `Info.OverwritePriority` (a lower-priority object gives way and is demolished, for objects, paths and queues alike), the access cells by `Info.HasQueue`, owned land; no slope test (none in tp.exe either); Level enforces economy purchases. Guests on a cleared path cell are set down on the nearest path cell | The second-layer link tests `0x53AE00`/`0x53AE90`, which types have record +0x288 = 1 (their `Bumper.WhichTrackType` turns the access-cell option off), and what tp.exe does with a guest inside a new footprint |
| RIDES-021 | World/Level.Objects.cs, World/Original/ObjectRayPick.cs, World/Objects/ObjectPickModel.cs | The cursor's cell is the cell of the ray's nearest hit (the hit divided by the cell size, `0x45D560`; the click command `0x46C5D0` hands it to `0x524790`): the terrain hit (`TerrainRayPick`, the heightfield triangle planes of `0x45BF90`) or, when nearer the camera, the first object model the ray meets as `0x45BF90` walks the cells from the camera one cell per pass (with the cell at each crossing of the other axis and the edge clamp of `0x45CDC0`). A cell is tested when the cell lookup `0x452AE0` names an instance there (the owner with a height map) and the ray over the pass reaches down to the height it gives and up to the model's bounds minimum (`0x45CEE6`-`0x45CFBD`); its model is tested by `0x45D090` (the nodes depth first, a node's faces material by material unless its state has `0x10` or `0x8210`, the first face whose plane hit has an angle sum of at least 6.2821856). The same point is the mouse point `0x7A1C90`. The generic sandbox has no heightfield and takes the Z = 0 plane for the terrain hit (`EXT:build-cursor-ground-plane`). The anchor taken from that cell is the original's (`0x528B44`: the cell less (W/2, H/2), then the rotation's 5 × 5 window offsets), so the click is local (W/2, H/2) when each side is odd or at most 5 cells; the only shipped exception is space 3104 Orbiter (6 × 3: local (3, 1) at 0°, (2, 2) at 90°) | A track tile's second-layer piece instance, which `0x452AE0` names before the owner, is not tested (the port holds no piece model there); the members of an instance with the group flag `0x80000` (`0x447790`, `0x4477D0`); the node state bits `0x20` and `0x8000` (`0x200` marks the dummies, which hold no faces); the normal refresh `0x473C30` (`0x473A40`) of vertex-passed nodes when the instance's model flags word +0x30 has `0x40000` and `0x20000` (the port uses the stored normals); the hit instance handed to `0x469340` (`0x45CCD5`) and by the hover handler to `0x469180` (a hover highlight: normals copied, node flag `0x1000000`, model flag `0x400000`), not used; the original keeps its previous hit when the ray finds nothing |

| RIDES-031 | World/Level.Objects.cs | A Theme Park Inc world keeps the cells its layout was given and types none of its footprints, so the road signs' `@` cells ("`*@*`", "`*@@*`") are plain occupied cells. The original placement `0x528A70` stamps `@` as a type-1 NoModify path cell linked to its path neighbours (`0x529173`–`0x529208`) and `Q` as a queue cell (`0x529119`); `ParkCellMap.WriteFootprint` does both for Theme Park World, whose shapes have neither | Theme Park Inc's own placement (its executable), not traced |
| RIDES-032 | World/Objects/ObjectAnimator.cs | Every instance takes the callback route. PC: render callback 0x463030 (model object +0x140, set at 0x4638bd) poses via 0x44e380(inst, 1) and sets instance flag 0x200000; the list pass 0x44e410 (once per frame, from 0x54e360) updates instances without it via 0x473c70(inst, 0, 8), which poses only with instance flag 0x400000 (set once by the hold route of RIDES-003), and clears it on the others. The list-pass route is not modelled, so an instance the renderer does not pose (a view-dependent choice) is posed here | The callback's call site: no instruction calls or jumps through that +0x140 (the other `+0x140` operands belong to other structures) and the renderer (`0x579D95`) only initialises the word to 0, so any call goes through a computed address |
| RIDES-040 | World/Objects/ParkObjects.cs | Theme Park Inc fixed scene records saved at (0, 0) use their local model origin. Their thing records (tag `EJBO`: position X, Y in 1/256 cell, two links, angle, Info.Id) carry the fixed-item default (128, 128), angle 0 for the six that have an archive (1705, 1706, 1709, 3708, 1707, 1710); the five without an archive (1702, 1701, 1700, 1750, 1798) carry real positions but have nothing to draw | How the original places a local-coordinate fixed model at its anchor (the no-CD Theme Park Inc exe holds that code) |
| RIDES-045 | World/Objects/ObjectCatalog.cs | A Theme Park Inc object that a mod brings into a Theme Park World park runs its original script, but only the script variables Theme Park World's engine writes are driven: the variables the scripts of the three Theme Park Inc worlds declare beyond the standard ride set (VAR_FPBYPASS in 69 scripts, VAR_SPEED in 19, VAR_SPECIAL in 15, the sideshows' `VAR_WIN*`/`VAR_IN*`/VAR_TIMEOUT/VAR_WINLANE, the litter bin's VAR_BOMB, VAR_PRANKED, VAR_ONFIRE, VAR_DIRTY, VAR_EMPTY, VAR_FIRERISK, VAR_CONTENTS and VAR_TIMER, the staff rooms' VAR_CLOSED) keep their initial 0, so the branches they guard never run (the litter bin's WAITABS, opcode 45, sits behind VAR_BOMB); what Theme Park Inc's executable writes into them, and when, is not read | The writers of those variables in the no-CD Theme Park Inc executable (`Game.exe`) and the events that make them write |
| MD2-900 | World/Objects/ObjectAnimator.cs | A texture-frame slot's frame selection is the PC writer `0x470C90`: the last key at or before the clip time wins, none before the first key keeps the previous frame, later tracks of a slot overwrite earlier ones (hyenasm1 carries two tracks on one slot); a slot no bound clip addresses shows frame 0 again here (the writer never resets), and the fixed-item pose hold is not extended to frames | The draw-path reader of a slot's current frame, and what resets a slot (the writer only sets it) |
| MD2-901 | World/Objects/ObjectAnimator.cs | Texture-frame keys naming a frame outside the slot's frame names, or a slot the model does not have (Pmegacostm slot 12), are ignored; the PC writer `0x470C90` checks neither index | Deliberate defensive divergence: writer `0x470C90` can address a slot unchecked; binder `0x57A420` passes the stored unsigned frame to unchecked pointer-array read `0x575F90`. No native wrapping or clamping is established; unsafe out-of-range memory behaviour is not reproduced |


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
`[EXT:bonus-name-fallback]` (formerly `APPROX:RIDES-025`: a bonus object whose archive lacks the selected language takes the English name, then the `.sam` name; the original reads only the selected language file and the id table, `0x41F350`; third-party content only, all 35 shipped bonus archives carry the four languages),
`[EXT:build-cursor-ground-plane]` (the Z = 0 plane pick that is part of `APPROX:RIDES-021`; the original's pick stays open, see the register),
`[EXT:ride-cycle-counter]` (formerly `APPROX:RIDES-023`: `CompletedCycles` counts VAR_RUNNING 1 → 0 edges as OpenTPW's own diagnostic counter; nothing in the original counts ride cycles, it only reads VAR_RUNNING as a level),
`[EXT:park-clock-origin]` (formerly `APPROX:RIDES-044`, retired: each park's script world starts its scheduler clock at 0 where the original's is the app-lifetime timer, so a park's run never depends on how long the program ran before it; the one origin-dependent sum, WAITANIM_CH (`0x55320A`, an unsigned wrapping sum), is issued by no shipped script, so nothing original remains),
`[EXT:tpi-bus-from-setup]` (formerly the Theme Park Inc part of `APPROX:RIDES-019`: a Theme Park Inc world without a prebuilt save gets its Bus at setup where the Theme Park World arrival vehicles are placed on first dispatch; no original reading of how Theme Park Inc places its arrival vehicle is available),
`[EXT:malformed-texture-coordinates]` (formerly `APPROX:RIDES-030`, non-finite texture coordinates drawn as (0, 0); the original passes raw floats to Direct3D) and
`[EXT:trig-anim-speed-guard]` (formerly `APPROX:RIDES-038`: the corpus has 4 TRIGANIMSPEED uses, all literal 1800–4000, so the guard against 0 and negative speeds only meets malformed input).

Narrowed: RIDES-011, 012, 019, 024, 031, 033, 035 and 044 (rows above); RIDES-013, 021, 028, 032, 040 stay as they were.

### Retired: the end-of-park finale (RIDES-019, October 22)

`APPROX:RIDES-019` is retired: the finale around `End.RSE` is the original's in both game types
([the end-of-park finale](reverse/INSTANT-ACTION-FINALE.md)). A new all-research-complete event
marks the park's record in the player's record list (`0x5B0CA0`) and creates End in Instant Action
and, when the list shows every park complete (`0x5B0C40`), in the Full Simulation (`0x503D40` ->
`0x516B00`, after advice `0xA7`); End takes its thing slot and an uncharged economy record, guards
bankruptcy while it lives, and the winning screen (`0x48AE70`) rides its camera seat and draws the
language's animated `s_congrats` banner (tables `0x74FAF0`, `0x74FCF8`) until Escape or a late right
click (`0x48A970` -> `0x516C10`); Instant Action then shows UITEXT 471 in the generic message box
(`0x47F020`). A park restored with End enters the same screen. Theme Park Inc and read-only visits
never start it.

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
