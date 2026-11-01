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
45, 16). Base height = the midpoint of the lowest and highest terrain vertex under the footprint (`0x466D70`; the earlier mean of the cell heights
stays only for a park-resume save without `BaseHeightRule`). The original does not flatten the ground under a ride: `0x467030` only places the
footprint box and stores it, the ground meets the floor through the edge conform and the skirt ([Floors](#floors-the-skirt-and-the-edge-conform)); the per-archive `.hmp` files are the
height maps of the object's footprint (their size is the shape's, e.g. Totem 3×4).

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
`VAR_RIDECLOSED` 1 for a built ride whose type has `Info.HasQueue` (the constructor `0x4DB090` opens the object and then closes it, `0x4DB712`–`0x4DB779`: RIDES-015 retired; the player opens it) and 0 otherwise; imported objects open, and an idle attraction saved with its open word `mCanLoad` clear is closed again by the level — the sandbox Totem starts closed. Variables are written by name since
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

## Floors (the skirt and the edge conform)

Where a placed ride, shop, sideshow or feature meets the ground the original does five things (all of tp.exe, none flattening the terrain). Package F of issue 829 ports them in steps:
the skirt read at type load, the opaque skirt slots, the edge conform and the depth compare (F1 to F3, F8), and, with the procedural terrain textures of PR 843, the holes under the footprint
and the composite of the floor art over the procedural ground (F4, F5). The queue models' skirt (F6) is ported with the queue models' drape ([PATHS.md](PATHS.md)); the path art over the procedural ground (F9) is ported too ([PATHS.md](PATHS.md#path-cells-are-terrain-cells)).

- **The skirt, read at type load.** The type loader (`0x004629D0`, load flag 1 → `0x0046A5A0` → `0x0046AA10`) reads the model's ground node (the node with most ground vertices,
  `ObjectHoardingGeometry.ScanGround`) when the MD2 header `+0x30` has bit 0x08 ("has procedural base", the exporter's option; every non-fixed catalogue type has it except the
  `mystery` box, header 1) and the type has a ground node or header bit 0x80. `ObjectSkirt` is that reading, built once per type over the level's terrain: the edge mask of the
  shape (`0x0052C420`: cells of type 4, 9, 10 or 0x17 that lack a floor neighbour on any side, a 16 × 16 array indexed column × 16 + row, with the original's reads one cell past the
  last column and row), the best axis-aligned horizontal floor face of every cell (`0x0046AA10`), the texture coordinates of the node after the snap, shift and wrap pass
  (`0x00467310`, tolerance 0.02), and per edge cell an entry `{orientation code, texture slot}` (code 0x80 | corner 0, 8, 0x10 or 0x20 of the quad with the smallest
  texture coordinates, 0x40 when the quad runs along its other axis; the default entry `{1, 10000}`). The base record gets flag 0x40. The skirt grid is the shape's size
  (`+0xF0/+0xF4` = shape words 0 and 1, and the object's height map `+0xCC`, whose file size must equal it, is the grid the instance constructor walks: OPEN-F3 settled, every
  shipped height map has its shape's size). Shipped: 60/57/61/59 types per theme (jungle, fantasy, hallow, space) have skirt cells, 394/370/437/437 cells in all, no two cells of
  a type share a slot, the error "Could not generate skirt flags" never fires; Belly Bounce's ten entries are `C8 E0 90 A0 88 A0 88 80 90 E0` with slots 11, 10, 9, 8, 6, 5, 3, 2, 1, 0
  (`ObjectSkirtCorpusTests`). Jungle `lavajump` has the base bit and no cell, `mystery` is not exported.
  The reading is the oracle's, bit for bit (`SkirtReading`, `ObjectFloorMath`, [ORACLE.md](reverse/ORACLE.md) X28 to X32), in plain float arithmetic: tp.exe's thread runs the x87 at the
  24-bit control word 0x7F (DDSCL_FPUSETUP and the Direct3D device start leave it so, which nothing restores: COAST-032, `X87Precision`), where every operation is one single-precision operation. At
  the 53-bit word the same routines differ on some inputs (31 of 400 skirt, 104 of 400 conform).
  Theme Park Inc's no-CD `Game.exe` has the same code (the skirt strings at `0x009AF698` and `0x009AF65C`, the mask code at `0x0066D146`, the slot flag 0x800 → state 0x1256 test at
  `0x006EFCA0`, the conform tail `0x00469A56..0x00469A75` byte for byte at `0x004CE588`), so the floors apply to both games.
- **Opaque skirt slots.** The same pass sets the runtime slot flag 0x800 on the texture slots of the ground node's faces that lie in an edge cell (and clears it on the others; the last
  face of a slot decides) and clears the wrap bits of the slots whose faces all lie on the floor in an edge cell. A face of a slot with 0x800 is drawn with state 0x1256 (no blend,
  no alpha test), never in the sorted translucent pass (`0x00581790`, [MD2-MODELS.md](MD2-MODELS.md#the-translucent-pass-of-world-models)). The grass-fringe art of these floors is
  translucent, so before this the floors went through the sorted pass and z-fought with the terrain. Forced opaque, a skirt cell's art shows its painted grass edge as it is: with the
  static ground the edge is meant to be seen on nothing, because the ground under a footprint is a hole (below), and with the procedural ground the composite replaces the art
  (below). `ObjectFloor` applies the flags, the normalised coordinates and the conform to the
  instance's copy of the meshes (the type's parsed model stays as read); `test.shader` reads the vertex bit `WorldTranslucency.OpaqueSlotFlag`.
- **The edge conform, at every placement.** The creation flag 8 of every non-fixed object runs `0x00469940` → `0x00469A80` in its apply mode, whatever the texturing option is: each
  ground vertex that the counting walk listed at type load is snapped to the nearest terrain grid corner (`ObjectGroundConform`): the placed vertex's ground coordinates
  onto the grid line (within 0.01 of a cell) and its height onto that corner's height, then back into the node's frame through the inverse matrix. The floor's outer edge lies
  on the terrain; the inner floor stays at the base height, so on a slope the edge ring tilts. A vertex whose corner lies past the last terrain corner ends its node (and its
  subtree) there. Presentation only: the conformed copy is drawn and picked (`OriginalObject.TryHitPickLine`), no simulation state, world hash or save field reads it
  (`ObjectGroundConform` works on copies; the type's model is shared and never changes). Not ported for the draw: after the vertices the original recomputes the normals of a model with flag 0x40000
  (`0x469A66` → `0x473C30` / `0x473A40`); OpenTPW's draw keeps the stored normals, as for every vertex-passed node. The cursor pick does the recompute (`ObjectPickModel.NormalRefresh`, RIDES-021).
- **Depth compare.** The original compares depth with LESSEQUAL (`0x0057360F`, once, in the device set-up), so on equal depth the later draw wins and the floor, drawn after the terrain,
  shows over the ground it lies in. `Material.DepthComparison` ports it for every depth-tested material (Theme Park Inc's no-CD `Game.exe` sets the same LESSEQUAL, at `0x006D6574` and in its restore helper `0x006D5C1E`, and nothing else).
  The terrain under a footprint is not drawn at all (next item), so nothing of it is left to fight the floor.
- **No ground under a footprint (F4).** The cell refresh `0x005365D0` asks the chooser `0x00535DD0`, which answers 8 for the (absolute) cell types 2, 7, 0x1E, 4, 9, 10 and 0x15, and hands that to
  `0x00450470` → `0x0046DF50`: mode 8 sets bits `0x101` of the landscape word and clears `0x80`. The terrain cell list (`0x0056F090`, for the static ground and the procedural ground alike) skips a
  cell whose word has bit 0x01. So every cell an object, entrance or exit stands on (types 4, 9 and 10, which the placement writes, `ObjectCellHoles`) is drawn with no ground, in both texturing
  modes; types 2, 7 and 0x1E are cells without terrain and 0x15 the coaster's track connection marker. `Level` adds these to the render holes of the ground (`OriginalTerrain.RenderHoleSource`, with
  the pools extension's), so the static mesh and the procedural cell list leave them out and the ground under a ride is rebuilt only when the cell map changes. A cell that goes back to type 0
  (the occupant demolished: `0x005367A0` clears the cell, `0x005365D0` then restores it) takes its backup word again with the dirty bit 0x100, so the procedural terrain draws it again and
  builds its texture once more (`ProceduralTerrain` marks a cell that stops being a hole dirty, `ProceduralTextureCache.Resolve` rebuilds the hit). OPEN-F5: every footprint cell of a
  demolished object (`0x00527EE0`, types 4, 9 and 10 alike, `0x5367A0` cases 4, 3 / 9 and 1 / 10) ends in `0x5346D0(0)` and the refresh, which is the 0x37 restore. A queue cell (type 3) is not on the
  list: its refresh places the queue model and writes the hole itself (F6, below). The pools' render holes and the camera modes that suspend the texturing leave these holes as they are (`0x46DED0`
  skips a word with bit 0x01).
- **The skirt cell's word (F4).** With the procedural textures on (`[0x7A1A8C]` bit 0x4000) and the base flag 0x40, the instance constructor `0x00463060` (from `0x0046334C`, after the placement and
  the conform) writes, for every entry with code bit 0x80, the word `(old & 6) | orientation | 0x281` of the landscape cell the entry lands on, the entry's slot in the high half
  (`OriginalCellWords.SkirtWord`). The cell is `(X + column, Z + row)`, `(X + row, Z − column)`, `(X − column, Z − row)` or `(X − row, Z + column)` for 0, 90, 180 and 270 degrees, with no bounds test
  (`ObjectFloorBase.Target`; the origin cell `X, Z` is `ftol(root translation / cell size)` less one in x for 180 and 270 and in z for 90 and 180: the placement's anchor, `OriginCell`). The
  orientation is the entry's bits 0x08, 0x10 or 0x20 turned with the instance (0 → 8 → 0x10 → 0x20 → 0 for 90, the other way for 270; 0 ↔ 0x10 and 8 ↔ 0x20 for 180: the jump tables `0x46395C`,
  `0x463994` and `0x4639CC`), and the flip bit 0x40 is kept. The words live in `OriginalCellWords` (the heightfield stays the file's backup; a removal restores the cell's backup word,
  `ParkObjects.Remove`; a level that clears its park drops them with it). Presentation only: no simulation state, hash or save reads them, and a park load rebuilds them from its objects.
- **The composite (F5).** Right after the words, `0x57ACE0` runs over the footprint box (`0x467030` stores it in `+0xC0/+0xC4`: the shape's rectangle at the anchor, width and height swapped
  for 90 and 270) row by row: every cell whose word has bits 0x80 and 0x200 (this instance's skirt cells and, at a park load, any other object's inside the box; a slot the
  instance's model lacks is skipped where the original reads past its slot table) takes the art of the slot its word names and the cell's procedural base (`0x572630` builds an N × N
  texture, `0x55F780` with a floor descriptor) and the slot's texture is redirected to it. `ProceduralTerrainTexturer.Compose` is the tail of `0x55F780` (from `0x5608B0`): the base buffer
  (the texturer's output, rows bottom-up) is read in one of eight orientations of the word's bits 0x78 (`BaseTexel`, tested 0x40, 0x08, 0x10, 0x20), the art is point sampled at row
  `Y · (height / N)` and column `X · (width / N)` (integer divisions: a 128 × 128 art at N = 32 steps by 4, one of 16 by 8, one smaller than N keeps its first texel), and each channel is
  `base + (((art − base) · alpha) >> 8)`, so an opaque pixel keeps 1/256 of the base. The oracle runs the original's tail with a floor descriptor on synthetic levels and art (X33,
  [ORACLE.md](reverse/ORACLE.md); `ProceduralTerrainComposeGolden`). One composite per skirt cell and placed instance, made once at placement (`OriginalObject.PlaceFloorBase`, from
  `ParkObjects.Register`, after the demolished objects' cells were restored), no cache slots (the cells are holes, so the terrain never lists them), freed with the instance. A slot named by
  two cells gives the second the first's composite as its art, which the original stores with alpha 0, so it blends nothing (`ObjectFloorBase.Compose`). The composite texture is a page of
  its own without mip levels, as the terrain's pages are (E's OPEN-3), and the clamped addressing the skirt reader gave its slot.
  **OPEN-F1 (the art's rows).** `0x00590DA0` turns the art over (row r with row width − 1 − r) before the tail reads it, for a texture with flag 0x40000, which every floor art has; the
  composite itself has no such flag and is read as stored. In OpenTPW's order (a texture's first row is the top of the file's image and the shader reads `1 − v`) the composite is made in the
  original's device order, where row r lies at `v = r / N`, and turned over once more for the renderer (`ObjectFloorBase.FlipRows`). The data decides both halves: the base orientation word
  puts, for 97% of the texels of every shipped floor at every turn, the base pixel of the ground under the texel there (the cells' texture coordinates give the ground; every
  other reading of the base scores below 55%; the rest are the flared cells of Belly Bounce, the totem and the lookout, whose art stretches over more than their cell), and the art turned
  over is transparent along a cell edge that borders open ground (mean alpha 69) and opaque along one that borders the floor (199), where the file's rows as decoded do not separate the two (118 and 128):
  `ObjectFloorBaseCorpusTests`. The seam test follows: where the art is transparent along an open edge the composite's texel is the base pixel of the ground at that place, so the cell's
  texture goes on the neighbouring terrain cell's (over 95% of more than ten thousand texels).
  **OPEN-F4 (order).** A live placement runs the constructor first (`0x528E19` → `0x529E10` → `0x4DB090` → `0x4DD500(0)` → `0x463060`: the skirt words, then the composites) and only then types and
  refreshes every footprint cell (op 0x81 at `0x52912C` → `0x5340A5` → `0x5365D0`), whose mode 8 sets 0x101 and clears 0x80 on every one of them. A later object's `0x57ACE0` therefore skips an
  earlier object's cells, and a type-0 cell's `0x5367A0` does nothing (the `ja` at `0x5367ED`). `OriginalObject.RefreshFootprint` does this right after `PlaceFloorBase` for a live placement
  (`ParkObjects.TryPlace`). A park load differs: `0x4647A0` creates every saved instance (with the creation flags it saved) and only afterwards `0x415140` → `0x4D83A0` refreshes all 128 × 128
  cells, so while the instances are made every skirt cell still has bit 0x80 and a neighbour's cell inside a box is composed, in the saved order. Snapshot restore, the Theme Park Inc checkpoint,
  the save import and the co-op join all end in `ParkObjects.CommitFootprintRefresh`, which does the refresh once the instances exist. The removal restores exactly the cells the placement wrote
  (`OriginalObject.FloorWordCells`: the skirt cells and the refreshed footprint cells).
  With the procedural textures off none of this happens: no skirt words, no composites, the skirt cells draw their painted art (the holes are the same), as in the original.
- **Queue models (F6): ported; path art (F9): ported ([PATHS.md](PATHS.md#path-cells-are-terrain-cells)).** A queue piece is placed by `0x005229E0` with the instance flags 0x33A (bit 8 included) and every queue model of the four themes has the
  base flag (header 0x09, ground node 0) and one skirt entry (`C8` or `80`, `E0`, `A0` slot 0), so the original gives each queue cell a skirt word, a composite (the slot is the model's first) and,
  from the refresh that placed it, a hole. `QueueModelFloor` and `OriginalTerrain.BuildQueueModels` port them, the composites as tiles of an atlas page ([PATHS.md](PATHS.md), "Queue models on the terrain"). The path art is the other user of the
  descriptor: `0x0056F090` gives a non-hole cell with word bit 0x80 (a path tile written by `0x46DF50`: slot from the path table, turn bits 0x20, 0x10, 0x08 for 90, 180, 270, flags
  `0x180`) the path texture as art, and the cache builds the cell with the same tail, so the original's path is the terrain's own texture, point sampled at N × N, not a layer over it;
  that moved the whole path layer into the terrain (F9).

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
the footprint cleared through `ParkPathBuilder.Remove`, a NoModify one refused, a cell whose structure record is of a track kind refused as the validator's mode 4 does (`0x535BB3`;
a path cell over a straight or stub tile and the cells of a fence post included), the access cells by the build tool's queue option (`Info.HasQueue`, off while the upgrade selection
`0x8187F8` is set: `ObjectCatalogEntry.QueueOptionOnBuild`), refusing with `AccessBlocked`, laid as the section below says), `ParkPathBuilder.ObjectGivesWay`/`DemolishObject` and the
queue builder's `givesWay` (`QueuePaths`), and `Level`, which hands the cell map to these hooks and refuses
track and queue cells by their type and track records by their kind (`IsReserved`). RIDES-018 is retired (round 11 package E): the corner and junction tests `0x53AE00`/`0x53AE90` at
`0x535BD7` run for a body cell only on a record of no track kind, which holds no arms (they decide the path and queue tools, `QueueCellValidator`), the types whose record `+0x288` is 1 hand
their `Bumper.WhichTrackType` to the upgrade selection (`0x46D605`), and the placement commit `0x528A70` holds no code that moves a person: a guest inside a new footprint keeps walking by
the edge test (`WalkEdges`), which lets it out of a type 4 cell, so nothing sets it down.

## Remaining gates

Path parameters and the draw-path reader of texture frames; EVENT objects/particles (their sounds play, [AUDIO.md](AUDIO.md));
water ride and kart jumps and tunnels (the track tool and the BUMP controllers of the water rides and go-karts are built, [TRACKS.md](TRACKS.md)); the tour controllers are described in [TOURS.md](TOURS.md); visitor simulation behind the bridge; the skirt, hole and composite of queue models and the path art as the terrain's own texture ([Floors](#floors-the-skirt-and-the-edge-conform), F6 and F9);
animation tick rate; the coaster module's cell marker 0x15 beyond a track end (RIDES-011); the object-list update route (RIDES-032, the simulation poses every instance).

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
| RIDES-021 | World/Level.Objects.cs, World/Original/ObjectRayPick.cs, World/Objects/ObjectPickModel.cs | The cursor's cell is the cell of the ray's nearest hit (the hit divided by the cell size, `0x45D560`; the click command `0x46C5D0` hands it to `0x524790`): the terrain hit (`TerrainRayPick`, the heightfield triangle planes of `0x45BF90`) or, when nearer the camera, the first object model the ray meets as `0x45BF90` walks the cells from the camera one cell per pass (with the cell at each crossing of the other axis and the edge clamp of `0x45CDC0`). A cell is tested when the cell lookup `0x452AE0` names an instance there (the owner with a height map) and the ray over the pass reaches down to the height it gives and up to the model's bounds minimum (`0x45CEE6`-`0x45CFBD`); its model is tested by `0x45D090` (the nodes depth first, a node's faces material by material unless its state has `0x10` or `0x8210`, the first face whose plane hit has an angle sum of at least 6.2821856), after the refresh `0x473C30` of the root node's face normals (`0x473A40`: the unit cross product of the edges from the second corner word, written to the face's normal index; once per instance, the first test of an instance whose conform ran). The group flag `0x80000` (`0x447790`, `0x4477D0`) is never set on an instance and the node bits `0x20` and `0x8000` never occur at pick time (`ObjectPickNodeBitsCorpusTests`). The same point is the mouse point `0x7A1C90`. The generic sandbox has no heightfield and takes the Z = 0 plane for the terrain hit (`EXT:build-cursor-ground-plane`). The anchor taken from that cell is the original's (`0x528B44`: the cell less (W/2, H/2), then the rotation's 5 × 5 window offsets), so the click is local (W/2, H/2) when each side is odd or at most 5 cells; the only shipped exception is space 3104 Orbiter (6 × 3: local (3, 1) at 0°, (2, 2) at 90°) | A track tile's second-layer piece instance, which `0x452AE0` names before the owner (`0x53BF30`), is not tested (the port holds no piece model there); the hit instance handed to `0x469340` (`0x45CCD5`) and by the hover handler and the click dispatcher to `0x469180` (a hover highlight: the instance's nodes get a private copy of their face normals, node flag `0x1000000` and model flag `0x400000`, and `0x469410` and `0x469840` fill the node colour words with a pulsing colour each frame, `0x54E301`) has no counterpart in the draw; the normals the refresh gives the root node light the original's draw too, which keeps the stored ones; the park tools read the pick at the call where the original's hover cell word keeps its previous value when the ray finds nothing |

| RIDES-031 | World/Level.Objects.cs | a Theme Park Inc world keeps the cells its layout was given and types none of its footprints (the slice as a whole does not type them: the walks, queues, coasters, tours and staff paths that read Level.TypesFootprints stay off), so the road signs' '@' cells ("*@*", "*@@*") are plain occupied cells; Theme Park Inc's own placement commit is read and is the twin of tp.exe's (the same 24-way cell-type table, 0x6691D8 and 0x6691FC against 0x529D04 and 0x529D24: '@' stamps a type-1 NoModify path cell linked to its path neighbours, 0x668552 against 0x529173, the queue cell and the entrance and exit cells the same sequences) with one more case, for the coaster marker type 0x15 (0x6688F0), so ParkCellMap.WriteFootprint is its port but the slice has not been moved onto typed footprints | whether the slice's walks, queues and staff paths run on typed footprints (a Theme Park Inc park over `TypesFootprints`) |
| RIDES-032 | World/Objects/ObjectAnimator.cs | The simulation updates and poses every instance in its tick, seen or not (saved node states, picks and the hash must not depend on the camera; point lights and lamps read the drawn `IsNodeHidden`, harmless because light dummies have no mesh). The drawn reset lasts only until the next simulation step puts the stash back (`ObjectAnimator.EndPresentation`), where tp.exe keeps it until the next writer changes it, so in edge cases it is a one-tick flash of the stored state. The original's list pass `0x44E410` (once per frame) updates an instance the renderer did not pose in the previous frame without sampling (`0x473C70(inst, 0, 8)`, which samples only with instance flag `0x400000`) and sets `0x800000`; the render callback `0x463030` (stored at the mesh object's `+0x140`, `0x4638BD`; called by the mesh prepare `0x57A750` as `call eax`, once per frame) restores the stored pose first when `0x800000` is set (`0x44E380` → `0x4726D0`, `ObjectAnimator.ResetToStored`), poses, rebuilds the key table (`0x44AB30`) and sets `0x200000`. The presentation side is ported in `OriginalObject.OnUpdate` / `PresentDrawn` (`ModelEntity.Prepared`: any drawn part); the ride view's instance rebuilds its key table every frame (`0x44E410` with argument 2) | The callback's visibility test is each drawn part's, where the original's is the whole mesh object's, and the simulation poses an instance the camera does not see |
| RIDES-040 | World/Objects/ParkObjects.cs | Theme Park Inc fixed scene records saved at (0, 0) use their local model origin. Their thing records (tag `EJBO`: position X, Y in 1/256 cell, two links, angle, Info.Id) carry the fixed-item default (128, 128), angle 0 for the six that have an archive (1705, 1706, 1709, 3708, 1707, 1710); the five without an archive (1702, 1701, 1700, 1750, 1798) carry real positions but have nothing to draw | How the original places a local-coordinate fixed model at its anchor: a thing's position bytes are decoded as (cell + fraction / 256) x 10 only by the camera-target setters `0x503490` and `0x5A1100` of the no-CD Theme Park Inc exe, and no model placement reads them there |
| RIDES-045 | World/Objects/ObjectCatalog.cs | A Theme Park Inc object that a mod brings into a Theme Park World park runs its original script, but only the script variables Theme Park World's engine writes are driven: the variables the scripts of the three Theme Park Inc worlds declare beyond the standard ride set (VAR_FPBYPASS in 69 scripts, VAR_SPEED in 19, VAR_SPECIAL in 15, the sideshows' `VAR_WIN*`/`VAR_IN*`/VAR_TIMEOUT/VAR_WINLANE, the litter bin's VAR_BOMB, VAR_PRANKED, VAR_ONFIRE, VAR_DIRTY, VAR_EMPTY, VAR_FIRERISK, VAR_CONTENTS and VAR_TIMER, the staff rooms' VAR_CLOSED) keep their initial 0, so the branches they guard never run (the litter bin's WAITABS, opcode 45, sits behind VAR_BOMB); what Theme Park Inc's executable writes into them, and when, is not read | The writers of those variables in the no-CD Theme Park Inc executable (`Game.exe`) and the events that make them write |


### Retired and reclassified (RIDES round 2, October 14)

Traced in tp.exe (`TPW-EXE`) and no longer approximations:

- `APPROX:RIDES-009`: shared `.sam` files are not part of the object's settings stack (0x413C10); `coaster.sam` is read on its own for track type 3 and given to the TrackMaster loader 0x42FAD0 (`SharedSettings`). The values were already identical.
- `APPROX:RIDES-010`: names bind by Info.Id through the table at 0x7488D8 (see Names).
- `APPROX:RIDES-017`: the build-menu filler 0x4AAF70 lists `WhichUIType` = tab, `AddOn.UpgradesId` = 0, model loaded and available; the corpus agrees. The two land-tool entries it appends to tab 3 belong to the UI register.
- `APPROX:RIDES-022`: the low-detail texture folders (`stexture`, `ssharete`) are used exactly when the active detail has TEXTUREQUALITY 0 or 1 (the Low preset; startup 0x54DCF0 → 0x4688B0). `ObjectAssets.UsesLowDetailTextures` follows the preset when an object's textures are looked up (the original picks once at startup, so objects loaded before a preset change keep their textures).
- `APPROX:RIDES-036`: the UV pass gate (0x47219A) is open for authored nodes: option bit 0x10 is never set and node bit 0x4000 is only written for the generated ground-conforming base node (0x46A81D).
- `APPROX:RIDES-041`: the PC sampler reads the 12-byte block (0x4718F7); see MD2-MODELS.md. The earlier finding that no reader existed was wrong.
- `APPROX:MD2-901` is retired (October 30, 2026): texture-frame keys outside the slot's frame names are ignored where the original reads undefined memory, `[EXT:original-undefined-reads]`.
- `APPROX:RIDES-043`: objects load in set mode (loader 0x461F10 strips header flag 4 unless the TrackMaster loader asks for it). The Add-mode remainder for TrackMaster coaster models moved to COAST-026.

Reclassified as OpenTPW conventions (no original rule to recover): `[EXT:md2-engine-scale]` (formerly `APPROX:RIDES-026`, the 0.2 presentation scale),
`[EXT:bonus-name-fallback]` (formerly `APPROX:RIDES-025`: a bonus object whose archive lacks the selected language takes the English name, then the `.sam` name; the original reads only the selected language file and the id table, `0x41F350`; third-party content only, all 35 shipped bonus archives carry the four languages),
`[EXT:build-cursor-ground-plane]` (the Z = 0 plane pick that is part of `APPROX:RIDES-021`; the original's pick stays open, see the register),
`[EXT:ride-cycle-counter]` (formerly `APPROX:RIDES-023`: `CompletedCycles` counts VAR_RUNNING 1 → 0 edges as OpenTPW's own diagnostic counter; nothing in the original counts ride cycles, it only reads VAR_RUNNING as a level),
`[EXT:park-clock-origin]` (formerly `APPROX:RIDES-044`, retired: each park's script world starts its scheduler clock at 0 where the original's is the app-lifetime timer, so a park's run never depends on how long the program ran before it; the one origin-dependent sum, WAITANIM_CH (`0x55320A`, an unsigned wrapping sum), is issued by no shipped script, so nothing original remains),
`[EXT:tpi-bus-from-setup]` (formerly the Theme Park Inc part of `APPROX:RIDES-019`: a Theme Park Inc world without a prebuilt save gets its Bus at setup where the Theme Park World arrival vehicles are placed on first dispatch; no original reading of how Theme Park Inc places its arrival vehicle is available),
`[EXT:malformed-texture-coordinates]` (formerly `APPROX:RIDES-030`, non-finite texture coordinates drawn as (0, 0); the original passes raw floats to Direct3D) and
`[EXT:trig-anim-speed-guard]` (formerly `APPROX:RIDES-038`: the corpus has 4 TRIGANIMSPEED uses, all literal 1800–4000, so the guard against 0 and negative speeds only meets malformed input).

Narrowed: RIDES-011, 012, 019, 024, 031, 033, 035 and 044 (rows above); RIDES-013, 021, 028, 032, 040 stay as they were. RIDES-014 retired on October 26 (below).

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

### Retired: the base height and the ground under a ride (RIDES-014, October 26)

`APPROX:RIDES-014` is retired. Its three clauses were all read in tp.exe:

- **"The original blends the ground under the ride flat (`0x467030`, `0x57ACE0`)" was wrong.** `0x467030` is the placement: it places the instance and stores the footprint box
  (`+0xC0/+0xC4`); `0x57ACE0` builds the composite texture of the floor art over the procedural ground. Nothing flattens the terrain. The floor meets the ground through the skirt read at
  type load and the edge conform at every placement ([Floors](#floors-the-skirt-and-the-edge-conform); `ObjectSkirt`, `ObjectGroundConform`).
- **The instance words `+0xF0/+0xF4` are the shape's width and height.** `0x004629D0` stores shape words 0 and 1 there (its parameters 10 and 11, which override them, are 0 for objects);
  the object's height map (`+0xCC`, the `.hmp`) has the same size, which every shipped type's file has (`ObjectSkirtCorpusTests.TheHeightMapOfEveryTypeHasItsShapesSizeSoTheInstanceGridIsTheShape`).
- **The base height on a slope is the midpoint rule.** `0x00466D70` (with `0x00454050`, `0x00469930`) run in the x86 oracle (`tpw-exe-base-height`, [ORACLE.md](reverse/ORACLE.md))
  returns, for every synthetic footprint, rotation and heightfield tried, the value of `ParkObjects.GetBaseHeight`
  (`ObjectSkirtGoldenTests.TheBaseHeightIsWhatTheOriginalReturned`). The earlier mean of the cell heights stays only for a park-resume save without `BaseHeightRule`.

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
