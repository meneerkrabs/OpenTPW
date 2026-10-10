# Original objects: catalog, placement, animation and scripts

October 9, 2026. Status: **every original ride, shop, sideshow, feature, fixed
item and track upgrade of the four themes is catalogued, every one can be
placed with its original model, and every object script runs in the RSE VM
with its original animations.** This is not original gameplay: visitors,
sounds/particles/EVENTs, ride-type controllers (coasters, karts, tours,
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
| jungle | 70 | 58 | 18 | 8 | 5 | 36 | 3 | 58 | 67 |
| hallow | 70 | 58 | 20 | 8 | 4 | 35 | 3 | 56 | 67 |
| space | 68 | 56 | 21 | 8 | 4 | 32 | 3 | 54 | 65 |
| fantasy | 66 | 55 | 17 | 8 | 4 | 35 | 2 | 52 | 63 |

Per theme, 6 entries are fixed items (`Info.DontApplyOffset 1`: bus, gates,
seaplane, lights, ferry, end; `Info.WhichUIType 4`) and 3 are tools without
occupied cells (Mystery ride 100, Buy Land 101, Clear Land 102). "Buildable" =
`WhichUIType` 0–3, not fixed, not a tool, not a track upgrade (upgrades attach to
a ride's track, which is not implemented).

Settings layers (`ObjectSettings`): the category default file beside the
archives (`Rides.sam`, `Shops.sam`, `SideShow.sam`, `Features.sam`,
`Upgrades.sam`), then shared archive files without `Info.Id` (`coaster.sam`),
then the object's own `.sam`. `Easy_*.sam` (difficulty) and `Online_*.sam` are
not applied. The order is inferred from the files' comments and overrides, not
traced. The generic `SettingsFile` cannot read quoted names or `---` blocks,
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
except two (hallow ogre upgrade: unnamed slot; space rocket: `s_leg3` not
shipped). 31 floor meshes have more than 16 texture slots and are split by
material for the 16-slot shader.

### Names (OBJECT_NAMES.str)

The `.sam` files carry no string index. OBJECT_NAMES holds one block per theme,
each ending with "Traffic Lights"; rides use two entries ("Inca" + "Totem"),
other objects one. An entry is bound when its English `Info.Name` equals one
entry or two consecutive entries (ignoring case, spaces, accents) in the block
with most matches; `DisplayName` then comes from the selected language's
table at that index. 220 of 274 bind. Unbound (keep the .sam name):
Loudspeakers ("Speaker: …" entries cannot be assigned to speaker1…4 without
evidence), `Sign1`, Gates/Lights/End/Plane, tools, and renamed rides (Thrill
Grill = "The Firepit", Bounce On Iggy = "Cosmic Bounce", Star Tours,
Butterfly Nest, Kid Creosote). Confidence: high for bound entries (string
equality), the binding rule itself is inferred.

## Shape, footprint and rotation

`Info.Shape` rows (`ObjectShape`): `*` occupied, `.` free, `2` entrance,
`S`/`N`/`E` exit, `<`/`>` coaster station ends, `+` track-upgrade cells, `W`
(bonus Snake only, unknown, treated as occupied).

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
(docs/reverse/ANIM-plan.md §8, §13): 30 ticks/s (the Mac formula; RIDES-001);
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
(0x10000, e.g. `fountainm`, `Coconutm`) play per instance. Node-state bit 0x10
(clip node lists, toggle tracks) hides the node's mesh (RIDES-030). Still shown
as stored: the 12-byte block (0x4000, RIDES-029); not applied: texture-frame
tracks (RIDES-035) and path parameters (0x200/0x400).

## Scripts in the world

`OriginalObjectRuntime` loads the main RSE with `OriginalObjectEffects`, child
scripts from the same archive, and one `RideScriptWorld` per park
(`ParkObjects.ScriptWorld`) so FINDSCRIPTRAND/REMOTEVAR work across objects (bus →
traffic lights). Initial variables from the .sam layers: `VAR_CAPACITY` =
`Upgrades[0].InitCapacity`, `VAR_DURATION` = `Upgrades[0].InitDuration` (raw;
unit per `Info.DurationUnit`, Totem.RSE uses it as cycle count),
`VAR_RIDECLOSED` 0 (built and imported objects open; the saved ride state is not
decoded) — the sandbox Totem starts closed. Variables are written by name since
scripts declare different subsets.

Corpus run (`EveryObjectScriptRunsInItsParkWithOriginalAnimations`): all 262
scripted objects, every theme in one shared world, 60 s at 60 Hz: no faults,
258 played original clips (1,451 plays). Not implemented (unimplemented-effect
hook): sounds/EVENT/SPAWNSOUND output, ADDOBJ/KILLOBJ particles and objects,
TOUR/BUMP/COAST controllers, park clock, screams, reverb, light opcodes.

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
same theme (a collision would be logged and skipped; the fantasy bonus Giant
Puzzle reuses space's 3301, which is not in the fantasy catalog). Category
defaults come from the game's own `Rides.sam` etc.

Names: bonus objects are not in OBJECT_NAMES. Each archive has UTF-16 text
files per language (`english.txt`, `German.txt`, `Dutch.txt`, … also languages
the game does not ship) with `NAME`, `SIGNA`, `SIGNB` sections; `DisplayName`
uses the selected language, then English, then the `.sam` name (never fails).
E.g. `_snake_1` (.sam "Snake") is "Slither" — a name the base OBJECT_NAMES
already reserves at jungle index 22 — and "Rutschbahn" in German.

Tests (`BonusContentMergesIntoTheCatalogsAndRuns`, private, inconclusive without
`OPENTPW_BONUS_DATA`): catalog counts with/without bonus, every bonus model
parses and renders into parts, every animation binds, every script runs 30 s
without faults. The native smoke test also builds the first bonus ride when the
bonus root is set (observed: Dizzy Dinos animating).

## Remaining gates

The 12-byte vertex block (0x4000), path parameters and texture frames; sounds/EVENT/particle objects;
TOUR/BUMP/COAST controllers and track building (coasters, karts, water rides,
track upgrades); visitor simulation behind the bridge; `.hmp` ground deformation;
original build rules and costs; animation tick rate; the meaning of `W`, `N`,
`E`, `<`, `>`; the object-list update route and idle restart (RIDES-032, RIDES-034).

## Approximation register

Every value or rule below is not taken from original data or hard evidence; each
is tagged `// [APPROX:RIDES-NNN]` at its site (paths under `source/OpenTPW/`) and
logged once as a warning at the first catalog load (`RidesApproximations`).
Original-data values are tagged `// [DATA:<file>:<field>]`.

| Id | Site | Current value / rule | Evidence needed |
| --- | --- | --- | --- |
| RIDES-001 | Mac channel rate 30 is proved; Windows rate and native scaled/unscaled channel-clock selection remain unverified | qualify target-PC rate and connect the correct original clock input before claiming runtime equivalence |
| RIDES-003 | World/Objects/ObjectAnimator.cs | Finished clip holds its last pose (AnimID 14 path traced: 0xa7498, 0xa68ac, 0xa694c); the next trigger restores against the ended clip, while the original passes the AnimID-14 slot as the old clip (0xa692c–0xa6948) | Capture after a clip ends; the object's AnimID-14 table slot; the ride-data sources of object flags 0x8 (stop) and 0x10 (hold) |
| RIDES-008 | World/Objects/ObjectAnimations.cs:28 | ANIM_* → letter, variant v → number v+1 | Binary member lookup |
| RIDES-009 | World/Objects/ObjectCatalog.cs:381 | shared (non-Info.Id) .sam files sit between category defaults and the object file; `Easy_<file>` is layered last in Instant Action (traced, STP-PPC 0x10119328) | which base file the binary's object loader is given |
| RIDES-010 | World/Objects/ObjectCatalog.cs:451 | OBJECT_NAMES index by English name equality | Binary name-index table |
| RIDES-011 | World/Objects/ObjectShape.cs:74 | Shape symbols N/E exit, `<`/`>` station ends, `+` upgrade, `W` occupied | Saves/captures with these objects |
| RIDES-012 | World/Objects/ObjectShape.cs:138 | Access cells open across first/last row, then columns | Saves with side entrances |
| RIDES-013 | World/Objects/ObjectShape.cs:103 | 180° and non-square rotations follow the rigid rotation | Save with such objects |
| RIDES-014 | World/Objects/ParkObjects.cs:153 | Base height = mean footprint ground height | `.hmp` format, captures on slopes |
| RIDES-015 | World/Objects/ParkObjects.cs:190 | Imported and built objects start open | TPWS ride state, original build behaviour |
| RIDES-016 | World/Objects/OriginalObjectRuntime.cs:62 | VAR_DURATION = raw `Upgrades[0].InitDuration` | Binary conversion by `Info.DurationUnit` (QUEUE-plan §7: the native host writes it raw, clamped to `UsageInfo.Min/MaxDuration`; `InitDuration` is type record +416, while +436 is `QueueWaitTimeConstant`) |
| RIDES-017 | World/Objects/ObjectCatalog.cs:124 | Buildable = WhichUIType 0–3, not fixed/tool/upgrade | Original build-menu contents |
| RIDES-018 | World/Objects/ParkObjects.cs:107 | Build rules: grid, MAP/save terrain, no overlap; no slope/path/land; Level enforces economy purchases | Original build checks |
| RIDES-019 | World/Objects/ParkObjects.cs:209 | Levels without save get Gates, Lights, Bus | Original fixed-item spawning |
| RIDES-021 | World/Level.Objects.cs:46 | Build centred on clicked cell; cursor ray hits Z = 0 | Original build cursor behaviour |
| RIDES-022 | World/Objects/ObjectAssets.cs:119 | Texture search archive textures → gtexture → sharetex | Binary texture lookup order |
| RIDES-023 | World/Objects/OriginalObjectRuntime.cs:125 | Completed cycle = VAR_RUNNING 1 → 0 | Original cycle/income accounting |
| RIDES-024 | World/Objects/ObjectCatalog.cs:276 | Bonus archives merge; Info.Id collision skips the bonus entry | Original behaviour with dropped-in WADs |
| RIDES-025 | World/Objects/ObjectCatalog.cs:545 | Bonus name: language file → English → .sam name | Original bonus-name lookup |
| RIDES-026 | World/Objects/OriginalObject.cs:12 | 1 MD2 unit = 0.2 engine units | None (engine convention) |

| RIDES-028 | World/Level.Objects.cs | Non-walkable exit outside cells, and entrance outside cells without a queue path, use the nearest walkable path (a one-cell queue) | Original queue-path joining rules |
| RIDES-029 | World/Objects/ObjectAnimator.cs | 12-byte vertex block (record flag 0x4000) parsed, stored mesh shown | A reader of the record +40 floats or of node bit 0x800 |
| RIDES-030 | World/Objects/ObjectAnimator.cs | Node-state bit 0x10 (node lists at clip start, toggles ≤ 0) hides the node's mesh | The draw-path reader of node bit 0x10, or captures |
| RIDES-031 | World/Objects/ObjectAnimator.cs | Relative-animation models (header flag 0x4) play their vertex and UV tracks in set mode, as the default ride load strips the flag (with it, both add: 0xa4a58/0xa4f68, UV 0xa56d4/0xa8420) | The ride-data source of the 0x594c8 flag word allowing 0x20000 |
| RIDES-032 | World/Objects/ObjectAnimator.cs | Every instance takes the callback route (0xa7960, r4 = 1); the object-list route (no posing, no-reset replay) is not modelled | The engine's call condition for the render-record callback (0x5a3bc) and the setter of object flag 0x00400000 |
| RIDES-033 | VM/RideVM.cs | Script speed 1.0 (creation bias 50) | What the native bias writers 0xb2a34/0xb5b18 store |
| RIDES-034 | World/Objects/ObjectAnimator.cs | No idle restart of AnimID 5/0 (0xa798c) | Which placed objects keep +4 & 0x8004 clear |
| RIDES-035 | World/Objects/ObjectAnimator.cs | Texture-frame tracks decoded, not applied | The reader of a texture slot's current frame |
| RIDES-036 | World/Objects/ObjectAnimator.cs | The UV pass always runs: global option +16396 bit 0x10 and node bit 0x4000 (gate 0xa5698–0xa56b8) taken as clear | The writers of option bit 0x10 and node bit 0x4000 |
| RIDES-037 | VM/Handlers/Animation.cs | TRIGWAITANIM(_CH) without an object never resumes; for a literal out operand this assumes its in-memory code word, like a variable's (tag 0xC0), never equals the token | The RSE loader's in-memory literal operand encoding |
| RIDES-038 | VM/Handlers/Animation.cs | TRIGANIMSPEED with a speed operand below 0 faults (0 is undefined `divw`) | The channel update with a negative speed (0xa6484, 0xa7190) |

Imported objects share their economy instance with the guest payment bridge;
shop/sideshow payments and ride-use statistics use that instance. Open/closed
state is mirrored before visitor payments. Imported fixed items absent from the
save economy register without charge. The developer Totem only links where its
Jungle Info.Id is in the running theme catalogue.
