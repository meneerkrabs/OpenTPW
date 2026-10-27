# Guests (park visitors)

October 9, 2026. Status: **approximated**. Guests arrive, pay admission, walk the
imported paths, choose attractions, queue, ride the original Totem through its
own `Totem.RSE` script, react to the ride and leave. They are drawn with the
original kid sprites. Data comes from the original files where they exist; every
rate, unit and formula without an original source is an OpenTPW approximation and
is labelled as such below. Nothing here was compared against the running original
game (no original binary was executed).

Code: `source/OpenTPW/World/Guests/` (`GuestSimulation`, `Guest`, `GuestSettings`,
`GuestPathGrid`, `IRideVisitorBridge`, `RideVisitorBridge`, `GuestSpriteAtlas`,
`GuestRenderer`), `source/OpenTPW.Files/Public/SpriteBankFile.cs` (sprite formats),
`content/shaders/sprite.shader`. Tests: `GuestTests` (asset-free), `GuestAssetTests`
(inconclusive without `OPENTPW_GAME_PATH`). Native smoke:
`--load-original-level <theme> --smoke-test`.

## All Visitors elapsed time

`Guest.TimeInParkSeconds` records simulated elapsed seconds for the All Visitors
detail row. The counter starts at zero when the guest is created (an arrival at its
drop-off, a seeded guest at setup), as the original's arrival stamp does, and the
first increment is the following simulation tick. Bus/road travel, the booth and the
way home all count; only a guest imported from an original save has no time.

Only the guest simulation's supplied tick delta advances the counter. The level's
fixed-step clock receives scaled simulation time, so pause supplies no steps and
speed changes the number of simulated steps rather than elapsed wall time. The
All Visitors row displays whole seconds as `HH:MM:SS`, with total hours continuing
past 24. A duration unknown from an older save displays an em dash (`—`).

The own-format guest snapshot carries an optional nullable field. Missing/null
durations remain unknown after restore and subsequent updates: elapsed time is
not reconstructed from the guest's exit counter, needs or the park clock. A guest
restored keeps its known duration.
Known values must be finite, nonnegative and no greater than
`TimeSpan.MaxValue.TotalSeconds`; the guest restore and world-file shape validators
enforce the same bound. A tick that would exceed it rejects before changing the
simulation. This is an own-format/display range, not a native game limit. Existing
save versions remain readable; older builds that reject unknown JSON fields may
not read newly written files.

The counter is saved display metadata and does not drive gameplay. Both the world
canonical guest digest and the guest gameplay hash exclude it, consistently with
presentation-only fields. Known and unknown duration metadata can therefore
produce the same gameplay hash while their displayed values differ. Duration
range validation is a separate input/display failure: at the supported bound it
can refuse a tick even though the gameplay digest excludes the counter. Hash
equality alone does not establish that display metadata passes its range checks.

The original keeps no visitor counter but an arrival stamp (+0x1D4 = the world turn) that only the guest constructor writes (tp.exe `0x4fafdb`, at the bus drop-off), so its time also covers the road crossing and the booth and does not stop on the road home; OpenTPW's seconds start at the guest's creation and run to its end, and the visitors list (which guests the original's list holds is unread, `GUEST-920`) shows the guests in the park. The original visitors list's "Time In Park" column (tp.exe `0x49326c`) shows whole hours of the turns since the stamp in whole 4-turn steps (a new row a quarter of them until the 2 s refresh, `0x493800`), as "N hours" up to a day and "N day"/"N days" above (`0x490ff0`); OpenTPW's roster and individual-visitor info now use an independent unsigned integer-turn age for newly created guests and valid original arrivals. The age advances at existing host turn boundaries, including swept lost-park turns, and preserves native uint32 wrap. Optional own-format age and display-phase metadata preserve immediate display and continuation across a restore before `AlignTurns` reattaches; this does not restore or change the gameplay clock. Older own-format saves without integer provenance keep their existing seconds-derived fallback, and a null duration stays unknown. Statistics/stay accounting, umbrellas and other weather gates continue to use the unchanged seconds (`GUEST-920`); roster membership and arbitrary historical clock resets remain unqualified. See the [bounded display-age contract](proposals/guest-920-reconcile-visitor-elapsed-time-semantics.md).
`GuestTimeInParkTests` verifies admission/refusal, seeded guests, pause/speed,
known/unknown codec continuation, both validators, invalid/overflow rejection,
the live UI model and hash exclusion using synthetic fixtures only. No original
asset or original-runtime parity is claimed.

## Original data found

### Guest graphics: sprites, not models

Theme Park World draws visitors as pre-rendered **sprites**, not MD2 models: no
WAD contains a kid/peep/visitor model (all 312 WADs listed). The sprites live in
`Data/esprites.wad` (121 members, all decoded by `SpriteBankFile`/`SpriteAnimationFile`):

| Folder | Members | Content |
| --- | --- | --- |
| `Generic/Kids` | `SPR_{BE,BI,CH,FR,KI,SA,SU,TA}.{ESP,FPC,TPC}` | 8 kid sets, 175 frames each |
| `Generic/Kidsheads` | `SPR_xx.{ESP,TPC}` | heads (8 frames × 7 directions); drawn on riders of ADDHEAD rides (below) |
| `Generic/Thoughts` | `SPR_TB` (16), `SPR_TC` (10) | thought-bubble icons (drawn by `ThoughtRenderer`) |
| `Generic/{Balloons,Litter,Particles,SpecialFX}` | `.ESP/.TPC` | balloons (`BalloonRenderer`), litter (`LitterRenderer`), particles, the upgrade balloon (`UpgradeBalloonRenderer`) |
| `Generic/{Guards,Handymen,Mechanics,Researchers}`, `<Theme>/{Entertainers,Costumes,Costumeheads}` | | staff and entertainer sprites |

**`.FPC` / `.TPC` sprite bank** (little-endian): `u16 3, u16 3, u32 frameCount,
u32 0`, then 255 palette entries of 4 bytes **B, G, R, A** (alpha varies: anti-
aliased edges), then per frame: `u32 dataSize, u16 width, u16 height, u16 128,
u16 128` (pixels per sprite unit across and down: the placement 0x5890E0 spans the quad from
`originX / A · scale` to `(width + originX) / A · scale`, likewise down with B; 128 in every frame. The reader names the fields
`SpriteFrame.PixelsPerUnitX` (A) and `PixelsPerUnitY` (B), the writer's frame has one `PixelsPerUnit` for both; OpenTPW honours them, see
[Pixels per unit](#pixels-per-unit-and-resolution) below),
`i32 originX, i32 originY` (offset of the frame's
top-left corner from the hotspot; for walking kids the hotspot is at the feet),
then `height` RLE rows. Each row is a length byte followed by control bytes:
a signed control `c < 0` repeats the next byte `−c` times, `c > 0` copies `c`
literal bytes. Pixel index 0 is transparent; index `n ≥ 1` is palette entry
`n − 1` (tp.exe's loader 0x587DB0 reads the `u32 0` as palette entry 0 of 256, so index n is entry n there;
the first u16 is the format, 3 = paletted, and the second is ignored). Every row decodes to exactly `width` pixels and every bank ends exactly
after its last frame (121/121 members). `.FPC` frames are taller than
`.TPC` frames and about as wide (the kids' reach 37×45 in the FPC, 36×43 in the TPC): the same art drawn from a lower camera (a standing
handyman is 58 to 62 px tall in the FPC, 49 to 53 in the TPC). The ESP chooses (tp.exe 0x5410D3): `.fpc` when its byte 0x10C and the
sprite-detail flag 0x8768FC are both set, else `.tpc`. The flag's only writer, 0x540900, stores its first
argument, and both of its calls pass 0 (0x54E688, 0x54ED2C; TPI's Game.exe likewise: 0x68E240 from 0x6A3768), so the
PC game draws the TPC of every set and never opens an FPC (an earlier reading took the start-up call's second
argument, 1, for the flag). OpenTPW draws the TPC of every set too (`SpriteAnimationFile.BankExtension`). Until October 18,
2026 it drew the FPC of the 27 sets with 0x10C = 1, so its people looked filmed from the side, and the staff tools' dangling
slot 6, which every staff FPC holds as 2 × 2 pixel placeholders, was invisible ([STAFF-PLACEMENT.md](STAFF-PLACEMENT.md#drawing)).
The texture-pack key of such a set's atlas ends in `.tpc.atlas` ([TEXTURE-PACKS.md](TEXTURE-PACKS.md)).

**`.ESP` animation table** (350 bytes): magic `ESP_FILE2.00`, NUL-terminated bank
name (`SPR_BE.TPS`), zero padding, two flag bytes at `0x10C`, then 20 slots of
`u16 firstFrame, u8 framesPerDirection, u8 directions` from `0x10E`. Frames are
direction-major. Flag `0x10C` marks a full-detail FPC bank (above); flag `0x10D` is 1 for kid sets CH, KI,
SA, SU and their heads, which look like girls; the loader 0x540D90 stores it and nothing in tp.exe reads it.
Only the first 16 slots are animations. The last four 4-byte entries (0x14E–0x15D) are the **performance entries**
(`SpriteAnimationFile.Performances`): the loader 0x540D90 reads them as single bytes, counts those with a non-zero
first byte (0x541FA0), and 0x540C70 returns entry e as program (byte 0 − 1, the sprite script table 0x74F7C8), slot
(byte 1 − 1) and two program registers (bytes 2 and 3). Their only user is the entertainer's search 0x4D46D0, which
starts a performance as script 13 + (r mod (n − 1)), or 13 for one entry (scripts 13–16 hold entries 0–3), and
0x475B80 plays it. Exactly the twelve entertainer banks have one: `01 05 00 00` (program 0 on slot 4) in eleven and
`01 05 02 28` in Hallow SPR_DR, so every entertainer performs slot 4; the program's registers 0x11 and 0x12 are SPR_DR's 2 and 40: frames 0-2 once, then for ever the frames from 2 on and 41 steps of frame 0 (`StaffScripts.PerformanceFrame`, GUEST-015).

**Writing a sprite set** (`SpriteBankWriter`, `SpriteAnimationWriter`, `SpriteSetBuilder`). A usable set is the `.ESP` plus its `.TPC`, the
bank the game and OpenTPW draw (`GuestSpriteAtlas.LoadSets`); a set with byte 0x10C also ships an `.FPC`, which nothing opens. The `.FPS`/`.TPS` files the ESP names are never shipped and
never opened, so nothing else is needed. The writers rewrite **all 75 banks (29 FPC, 46 TPC) and all 46 ESP files of `esprites.wad` byte for byte**
(`SpriteBankWriterCorpusTests`). What is written, field by field:

- *Bank*: the signature and frame count; the u32 at offset 8 (`SpriteBankFile.HeaderWord`, 0 everywhere); the palette; per frame the sizes, both
  unit words, the origin and the RLE rows. The writer's encoding is byte-identical to the shipped corpus (not proven to be the original encoder in general): per row every run of two or more equal indices is a repeat (up to
  127), everything between is literals (up to 127 per control byte). When that does not fit the one-byte row length, a row falls back to the smallest encoding the decoder accepts (repeats up to 128). Runs of two being repeats is what the corpus shows (3 would make files 3 to 4 % smaller
  and not match). Frames are at most 1020 pixels wide (what `GuestSpriteAtlas`'s 1024-pixel rows hold with their padding). Rows that need more than 255 bytes even then are refused (the length prefix is one byte). The corpus never reaches 127 equal pixels or 127
  literals in a row, so the 127 limits of the greedy path are not observed from the original; the reader accepts repeats up to 128 and literals up to 127.
- *ESP*: the magic, the bank name (zero padded, the padding is zero in all 46), both flag bytes, the 16 slots and the 16 performance bytes.

`SpriteSetBuilder` authors a set from straight RGBA frames: `AddAnimation( slot, directions x frames )` appends frames direction-major (a slot added
as non-directional stores 0 directions), `SetPerformance` fills an entertainer entry, `Build()` returns the `.ESP` and the banks. The pixel format is paletted, so
the builder quantises: one 255-entry palette for the whole set, alpha 0 is the transparent index 0, up to 255 distinct colours (with alpha) are stored exactly,
more go through median cut with nearest-entry mapping. Defaults of a new set, and which are unverified in the game itself (nothing was run):
`FullDetail` true (flag 0x10C = 1, FPC and TPC written with the same content, as every full-detail set ships both), kid flag 0, both unit words 128 (every
shipped frame), no performance entries. Unverified against tp.exe: whether the original's loader accepts a bank with unused zero palette entries or frames wider than any shipped
one (they decode here), and how the game blends the palette's alpha (the readers treat it as straight alpha).

### Which direction is drawn

Guests and staff go through one sprite pass in tp.exe (`0x518F90` calls `0x4FA030` for the things of classes 1 and 4 to 8 on every rendered frame, `0x542010` picks the frame). The only things it reads are the sprite's script frame, its heading and the camera's yaw; **the camera's pitch and the distance never enter**, and no bank holds a second elevation: every walking set stores 5 directions (the kid sets, the handyman `SPR_HA`, the mechanics `SPR_FA` and `SPR_OM`, the guard `SPR_GU`, the researcher `SPR_RE`; the entertainers likewise) pre-rendered from one angle, the other three are the mirrored ones. A staff member therefore looks "more from above" in the original only through the camera pose (45 to 65 degrees pitch for the zoom values 70 to 130) and through the way the yaw is turned into a direction:

- the heading is the angle of the last walking step from +Y towards +X (`0x4FA2A0` stores `FUN_006E7074` of the step), and the draw keeps it in 45 degree sectors, `((angle - 0x380) & 0x7FF) >> 8`: sector 0 is +Y, sector 2 is +X;
- the camera adds a sector count of its own (`0x542010`): `n = trunc((yaw + pi / 8) / 2 pi x 8)` (the code subtracts the constant `0x700E74`, which is -pi / 8), the sector is `8 - (n & 7)`, with the park camera's yaw wrapped to 0 to 2 pi (`0x540AB0`; the other cameras use the arc tangent of their forward vector, or of the right vector when it looks steeply up or down, `0x46F1C0`). The wrapped yaw keeps the term positive, so the truncation is a floor and the sectors are centred on the 45 degree steps of the yaw: a camera turned by 45 degrees turns the sprites by one sector, and a figure walking along the view axis is drawn from behind or in front at every multiple of 45 degrees. Only the other cameras' negative yaw is truncated towards zero (-67.5 to -22.5 degrees count as sector 0);
- direction = (camera sector + heading sector) & 7; a slot with fewer than 8 directions draws the sectors above its last stored direction mirrored, as 8 minus the sector (`SpriteAnimation.Directions` is 5, so sectors 5 to 7 are 3 to 1 mirrored).

`GuestSpriteAtlas.SelectOriginalDirection` is this rule (`HeadingSector`, `CameraSector`, `CameraYaw`); the kid, costume and staff renderers use it. Before October 17 they took the nearest 45 degrees of the exact camera-relative angle; the original quantises the heading and the yaw separately, so the two differ between the sector borders. The swimmers' renderer (`[EXT:pools-swim]`, lying poses turned by the residual of the exact snap) keeps the exact angle.

### Pixels per unit and resolution

Every frame header carries two u16 fields, A across and B down. The original's sprite placement `0x5890E0` ([BIN:TPW-EXE:0x005890E0 sprite placement]) spans the quad from `originX / A · scale` to
`(width + originX) / A · scale` and likewise vertically with `originY`, `height` and B, so a frame drawn with 256 in both fields covers the same world area with half as many world units per pixel, that is twice the pixels for
the same size. All 29 FPC and 46 TPC banks of Theme Park World have 128 in both fields (`GuestAssetTests.EveryEspChoosesTheBankTheGameOpens`).

OpenTPW honours the fields in every sprite renderer (`GuestRenderer` for the kids, costumes and swimmers, `RideHeadRenderer`, `BalloonRenderer`, `ThoughtRenderer`, the staff and entertainers through `GuestRenderer`/`StaffRenderer`, the hire window's
figure and the picking of guests in the world): `GuestAtlasFrame.Extents` scales a pixel by `128 / A` across and `128 / B` down on top of the engine's fixed `GuestRenderer.UnitsPerPixel` (0.045, an approximation), so a
frame of 128 is drawn exactly as before (the factor is 1.0, the multiplication leaves every float unchanged: `SpritePixelScaleTests.FramesWith128PixelsPerUnitMakeTheFixedScaleQuadBitForBit`,
5000 random frames, mirrored and not). Nothing pinned can move with the shipped data: every shipped frame has 128 (`GuestAssetTests.EveryShippedAtlasHasUnitScaleAndTheOriginalWidth`), and sprites are presentation only, so no simulation
hash, save or determinism pin reads them. Particle sprites (`ParticleSpriteAtlas`) are drawn by the particle system's own size rules and do not read the fields; no shipped bank would show a difference.

The 1024-pixel atlas width of `GuestSpriteAtlas` suffices for every shipped set (corpus-tested: all are 1024 wide, at most 512 rows). A set drawn at 2x needs up to 2048 rows in 1024 columns; beyond `MaximumHeight` (2048, which is also what WebGL2 guarantees)
an atlas is packed into the next power of two wider (2048, then 4096) instead of a taller texture, a frame wider than the row widens it too, and a set that fits no 4096 x 4096 atlas is refused (skipped with a warning in `LoadSets`). Frames drawn at more than 128 pixels per unit get 4 pixels of padding instead of 2 so mip levels do not bleed into the next frame. The pools extension's sets (docs/POOLS.md): 1024 x 256 for the kids, 1024 x 256 to 1024 x 512 for the heads, 1024 x 2048 for the swimwear sets, 83 MiB of RGBA in all and about 110 MiB on the GPU with mip levels (pinned by `PoolsContentTests.TheShippedAtlasSizesArePinned`); the decoded pixels are released after the upload.

Kid slots (identical in all 8 sets; labels from viewing the frames, not traced):

| Slot | Frames | Content | OpenTPW use |
| --- | --- | --- | --- |
| 0 | 100, 1 × 5 dirs | standing | idle |
| 1 | 135, 8 × 5 | walking | walking |
| 2 | 60, 8 × 5 | running | — |
| 4 | 20, 4 × 5 | short gesture | — |
| 6 | 115, 4 × 5 | gesture | — |
| 11 | 40, 4 × 5 | stumbling/sick-looking | — |
| 12 | 0, 4 × 5 | cheering, arms up | — |
| 14 | 105, 2 × 5 | hands on hips | queueing/waiting |

The five directions run from "walking away from the camera" (0) through "facing
screen right" (2) to "facing the camera" (4); the left half is drawn mirrored.
The slots cover frames 0–174 exactly once in every kid set.

### Guest rules: `levels/Standard.sam`

The theme balance file (`levels/Standard.sam`, overridden per theme by
`levels/<theme>/Standard.sam`; the jungle one does not override guest values)
has a `PeepInfo` block, eight `PeepTypes`, `Arrival`, `BankAccountInfo` and the
`FixedItemInfo` cells. `Easy_Standard.sam` changes prices/loans for easy mode; it
is not applied (difficulty selection does not exist yet). Used values:

| Key | Use in OpenTPW |
| --- | --- |
| `PeepTypes[n].PreferredExcitement.StartingCash.BoredomThreshold` (8 types) | Per-guest type; type index also picks the kid sprite set (`type mod 8`; which set belongs to which type is **not known**) |
| `PeepInfo.StartingCashVarPc` | ± variance of starting cash |
| `PeepInfo.ExitLevel` / `ExitLevelVar` ("starting value for the ExitLevel counter, in SECONDS") | The exit counter's start: ExitLevel plus a draw within ±ExitLevelVar; it counts down once per needs phase (4 park turns, 0.992 s) in every state |
| `PeepInfo.PerfectRide/GoodRide/OKRide` (±5/±15/±40 of preferred excitement) | Happiness change after a ride vs. `UsageInfo.ExcitementLevel` |
| `PeepInfo.Small/Medium/BigHappinessChange` | Penalties (needs, boredom, full queue, sickness) |
| `PeepInfo.RideVomitDivisor`, `VomitCapacity` | Nausea += excitement / divisor; at capacity "Feeling sick" |
| `PeepInfo.ToiletDesparate` | Toilet level that hurts happiness |
| `PeepInfo.DecisionVar{Dist,Queue,Excitement,Thirst,Hunger,Toilet,Illness}Weight` | Attraction score weights |
| `Arrival.MinPeople`, `TimeBetweenArrivals`, `NewParkBonus`, `PointsPerVisitor` | Arrival groups ([Arrivals](#arrivals); `FixedRate` is not read by the arrival code) |
| `BankAccountInfo.InitialAdmissionFee` | Admission paid at the ticket booth (standalone only; with the park economy attached its entrance fee applies, see Money below) |
| `FixedItemInfo.{BusStop,CrossingBSSide,CrossingParkSide,TicketBooth,Entrance}{A,B}Pos{X,Y}` | The level's fixed cells of arrival lanes A and B (the entrance cells are the first InitialPath cells, docs/MAP.md); every guest walks the cell map between them |

Not used yet: `ExcitementToCostDivisor`, `MinimumEntryFee`, price multipliers and
`OpinionFor*` (entry-fee opinion; economy slice), `DecisionVariable4–8`, `PrankeryLikelihood`, `StinkbombLikelihood`, `RegionFX`, `NewParkBonus`,
`PointsPerVisitor`. `GameOptions.NUMKIDS` in `low/med/high.sam` ("0->4, 1->6,
2->8, 3->MAX") is probably a guest-count or sprite-set limit; its meaning is not
established and it is not used.

### Ride data: `<ride>.sam`

`UsageInfo.ExcitementLevel`, `Info.AttractionValue`, `Upgrades[0].InitCapacity`
feed `IRideVisitorBridge`. `Info.Shape` marks the guest cells: Easymode's Belly
Bounce has its queue (save cells with path-flag bit 1, consecutive sequence bytes
and connection bits) ending at its `2` cell and a separate path attached to its
`S` cell, so `2` is the entrance and `S` most likely the separate exit; shops and
sideshows only have `2` (enter and leave there). With rotation 0 in the save the
shape's first row lies at the footprint's **maximum** Y. `UsageInfo.EntryCellStandPos`
/ `ExitCellAppearPos` (0.5, 0.5) and `RideHandlesSprite 1` (the script positions
riders) are noted but not used. These findings are for the rides/objects slice.

### Strings

`Language/<lang>/THOUGHTS.str` (18 entries: Hungry, Thirsty, Hungry and thirsty,
Need the toilet, Pleased, Dissatisfied, Too messy, Super happy!!, Happy!, Okay,
Unhappy, Bored, Angry, Feeling sick, Scared, Queue too long, Confused...) and
`KIDSTATES.str` (22 entries: crossing the road, going to the ticket booth, at the
ticket booth, entering the park, walking around, going to a ride, queueing,
boarding, waiting to board, exiting a ride, "Using: ", leaving the park, waiting
to go home, …). `GuestThought` and `GuestState` use these indices as their values,
so the UI can show the original strings directly. The `KIDSTATES` order is also
the state machine below. No executable constants were recovered from the
Windows `TP.ICD` string search. Separate static evidence from the identified
Feral Mac PowerPC application is recorded in [PPC-guests](reverse/PPC-guests.md).

## Arrivals

Traced from the Mac binary (`0x100CB2AC`, `0x100CB4E8`, `0x100C24C0`; Ghidra, static). A group of guests
arrives when no group is being dropped off and more than `Arrival.TimeBetweenArrivals` timer units have
passed since the last group emptied. A unit is four park turns (`(turn >> 2)`, about one second), so the
shipped 150 is about two and a half minutes. The timer starts unset, so a park loaded at a later turn (an
original save) gets its first group at once, while a new park waits one interval.

**The arrival vehicle.** The group comes by bus, seaplane or Ferry, the level's own fixed items run by their
level scripts (`bus.RSE`, `seaplane.RSE`, `Ferry.RSE`). The arrival update (tp.exe `0x4cf3e0`, once per world
turn, at the end of `0x516380`) talks to the vehicle at work through two script variables, 0 (the trigger) and
1 (the status), as `GuestSimulation.ArrivalVehicles` does. A group that starts with no vehicle at work sends
one by size (`0x51a2f0`): a bus up to 35 guests, a seaplane up to 60, a Ferry above. A vehicle the park
already holds gets its trigger set; a new one is placed then (not at world setup) with status 1; a kind the
level lacks falls back to the Ferry, then the seaplane, then the bus. A new vehicle is built with the common object
constructor (`0x004db090`, called at `0x51a57e`), which counts the type and stamps its first-purchase turn, and every
shipped TPWS saves an attraction record for the Bus, Seaplane and Ferry; so the vehicle gets the uncharged economy
record the fixed items at the park's start get (`Level.BindFixedItemMadeInPlay`). The constructor's last call stamps the
park's newest construction (`0x4c7680` at `0x4db7a3`), so the first vehicle also restarts the advisor's days since an object
was built (`ParkEventKind.FixedItemMade`). While the vehicle reports status 2 (at
its stop) one guest steps off per world turn; the turn that finds the group empty restarts the timer and sets
the trigger, and the vehicle drives off. Status 6 (gone) releases it (`0x51a690` writes 0 back). Guests
waiting to go home call a vehicle when none is at work (size 0: one world draw picks the kind, `0x51a34b`;
OpenTPW draws it from the park's one world generator) and keep it at status 4 until the last has boarded
(`0x500bd0`). The time to the stop is the script's: the jungle bus waits 2 s, plays its drive-in clip
`Busm1` for the time its trigger reports (the clip's 7,333 ms less 300) and waits 1 s more, so its first guest steps off about 10 s after
the group starts. A park without any vehicle definition (the original stops with a fatal error) steps its
guests off from the turn after the group starts, and its leaving guests go after 3 s.

**The arrival walk** (states 0 to 5, `GuestSimulation.ArrivalWalk.cs`) is the original's walk over the cell map from the vehicle to the park; the settings' lanes only name the level's fixed cells
(`FixedItemInfo`: bus stop, bus-side crossing, park-side crossing, ticket booth, entrance, each for sides A and B). A guest is made at the centre of the bus stop B cell (`0x4cf720`; two rows up for the seaplane
and the Ferry) and a world draw's bit 0 picks the bus-side crossing cell it walks to (`0x4d8710`: side A's or B's X with side A's row), the next draw's low byte being the point in the cell and 0xC8 the other
(`0x4fb201`); a route makes state 0, none state 6. State 0 (`0x501a0f`) walks there and ends in state 1, which waits until the vehicle lets guests walk (`0x51a760`, as for the way home) and then draws the
booth side and the two bytes of the point (`0x4ff520`) and asks for the route (state 2, `0x4ff730`: the speed term, then the walk; the park open ends it at the booth, state 4, the park closed in state 3).
State 4 is the booth below. A paid guest is in state 3 (`0x4ff7f0`) with a wait of 200 + R % 150 park turns (`0x501e6e`): it walks on to the entrance cell of the side a draw picks (`0x4d8690`, state 5) once it
heads its cell's thing list; with the park closed or the entrance gate's script variable 1 not 1 it counts the wait down and goes home (mask 1, a draw's bit 0 picks the gate's crossing cell, state 18 with the exit
counter 0), and an unpaid one goes back to the booth it stands on. State 5 (`0x4ffb20`) walks to the entrance, back to a booth when the park closes or the gate shuts, and on arrival numbers the visitor
(`0x51aaf0`: the park's visitor count plus one, kept as `Guest.VisitorNumber`) and decides (state 6). The walks use walk mask 0: a walker leaves a path cell for plain ground only with the leaving mask,
so the road's cells are walkway cells (type `0x1e`), which a mask-0 walker may enter. A save made before the arrival walk may hold a guest on an arrival lane off the grid: it is restored on the cell map in the state
its lane position stood for. What still differs around the stop is ARRIVAL-001. A leaving guest walks the cell map as the original does: state 18 ends at the gate's
crossing cell (`CrossingParkSide`, `0x4d86d0`), state 19 (`0x500ad0`) waits there until the vehicle lets it walk (`0x51a760`: not while the
bus is at status 3, 1 or 2), picks one of the four cells around the level's bus stop A (`0x4d8650`: the cell, its +X neighbour and the two cells two
rows up) with a draw and a random point in it with two more, and state 20 walks the route there; state 21 (`0x500bd0`) re-picks at status 1 to 3 off
the vehicle kind's two cells (the bus's are the first two, the seaplane's and the Ferry's the others) and boards the guest that heads its cell's thing
list at status 4. A stop cell no route reaches leaves the walk stuck in state 20, as the original's does.

The group size is `(int)(0.8 × (park points + NewParkBonus)) / PointsPerVisitor` (`PointsPerVisitor` below 2
divides by 1), at least `MinPeople`, cut so that the guests in the park plus the group stay within 1,500;
a bankrupt park gets none. With the shipped 20 and 6 a new park gets 2 guests per group. The PowerPC oracle
ran this routine (docs/reverse/ORACLE.md, F9). It adds three details to the first reading. The factor is
0.8, not 1.2, while the `+48` word of the weather thing (game `+0x1da724`, saved as `mWeather`) is positive. That word is the weather
object's `mCurrentDrops`, the drops its tick ramps toward the rain or snow target (0 in clear weather), so the group is
smaller while rain or snow is falling and the factor is 1.2 otherwise. OpenTPW uses 0.8 while `ParkWeather.CurrentDrops`
is above 0 (`GuestSimulation.WeatherDropsFalling`); a scene without weather has no drops and uses 1.2. (An earlier reading took the
thing to be the Traffic Lights fixed item at `+0x1da734`; that offset was miscalculated, see ORACLE.md F9.) Online parks (GameType 1) are cut at
500 instead of 1,500; OpenTPW applies that to a visit of another player's park (`OnlineVisit`): tp.exe sets GameType 1 when a
published park is entered (`0x4c0830`) and runs the arrival step there. A visit still reads `Standard.sam` instead of the original's
`online_Standard.sam` and counts no park points (ARRIVAL-002). Having no economy, a visit's arrival timer reads the park turn of the save
the visit is built on (`EXT:online-visit-arrivals`, `Level.VisitArrivalClock`), so its first group comes at once, as in an Instant Action
start of the level. Both cuts compare unsigned, and a group cut with more
guests than the limit becomes negative, in OpenTPW as in the original (tp.exe `0x4cf6e5`, no floor). Executing the update
routine `0xcb2ac` over repeated calls (F11) shows that a group at or below 0 creates no guest and, at the next stop of the
arrival vehicle, restarts the timer and clears the active byte exactly as an empty group does; on the PC every other reader
of the word treats it as 0 too (the vehicle size `0x4cf48f`, the boarding gate `0x51a844`, the advisor score `0x59f220`
over the group history).

Park points are summed over the placed rides and sideshows: each adds its `Info.AttractionValue` plus the
`Attraction[n].NewBonus` of its age bracket (n = age in game days / `Info.NewAttractionDecayTime`, no
bonus from the fourth bracket on). The PowerPC oracle ran this routine (docs/reverse/ORACLE.md, F10). A type
without a track (`Bumper.WhichTrackType` 0) is aged from its first purchase after turn 0, so all its copies
share one age, and is divided by the number of its objects, so its copies count once; selling does not reset
that first purchase. Every copy of a tracked type (karts 1, water rides 2, coasters and flumes 3) is aged
from its own build and counts in full. The jungle `Rides.sam` gives 20 and a 60-day decay; Belly Bounce has
25 and bonuses 10/7/4. The original counts only things that pass the test at `0xdc834` (executed in the oracle, F10): the type is
choosable (`Info.IsChoosable`), the ride is neither broken down nor condemned and is open, its entrance connects
to a cell, its queue holds fewer guests than four per queue cell, a coaster carries no riders, and a kart or water
ride has its track word (set when the ride is built). OpenTPW applies all of it (tp.exe `0x4dd920`, ORACLE.md X15): `IsChoosable`,
`IsBrokenDown` or the script's VAR_BROKEN, `IsOpen`, `QueueHasRoom` (queue cells from the entrance and fewer guests than four per
cell), for a coaster its controller test (`0x441970`: no self-intersection, complete and closed, `CoasterRide.AcceptsGuests`) and for
a kart or water ride its track word (`BumpController.IsTrackValid`: the constructor's 1 until a track edit's circuit walk writes its verdict, `0x524D1B`); the level syncs them each tick. So a
closed, broken, queue-less, full, crossing or invalid ride gives no points, while its copies still count in the divisor of an untracked
type. Only things whose type's `Info.WhichUIType` is 0 or 2 count (`0x4c8240`), which includes the shipped add-ons that declare 0. The
coaster test is the shared predicate `0x441970` (its edited-coaster term is `CoasterRide.BeingEdited`; the host's three-sample guard stays on guest admission only) and the track word starts at 1 as the constructor stores it (`0x4db0df`), until a circuit walk rewrites it (`APPROX:ARRIVAL-003` is retired). Objects imported from an original save find each type's first purchase restored from the save's object-control table (tp.exe `0x4d3aa0`, `+0x1c`) and stamp the import turn only into a type still at 0, as the original does; fixed items of a new park stamp turn 0 in both. Open points are ARRIVAL-001, 002 and 006 (`ArrivalApproximations`; ARRIVAL-003 is retired).

## Simulation

All on the 60 Hz `FixedStepClock` in `Level.Update`, guests first, then the
placed ride (bridge host step, then script slice). One world generator
(`WorldRng`, tp.exe `0x516330`) per park, shared with the economy, the staff and the scripts and reseeded with a guest's id at its creation (docs/DETERMINISM.md); guests are updated in id
order and attractions in `AttractionId` order, so the same seed and inputs give
the same `ComputeStateHash` (FNV-1a over all guest fields, RNG state, counters).
`System.Random` is not used.

**Path grid** (`GuestPathGrid`): walkable cells are save cells with the path flag,
linked by their cardinal connection bits (`SavePathConnections`), plus MAP
`InitialPath` cells (4-neighbour links) where no save exists (fantasy, space,
hallow: their 10 entrance cells only). The save's queue cells (map type 3) are
imported with their link byte, queue link, placement counter, owner word and NoModify flag, as
tp.exe's cell load 0x4D0B30 reads them (Jungle: the Belly Bounce's four cells).
Queue cells (original map cell type 3, see [Queues](#queues)) are kept beside
the path cells and are not walkable for routing. Routing uses breadth-first **flow fields** cached per target cell and
invalidated on grid edits; `FindPath` follows the field. Jungle: 78 cells, one
network reachable from EntranceA.

**States** (`KIDSTATES` indices): bus stop → crossing (0, 1) → ticket booth (2, 4:
the booth weighs the fee against the park, see [The ticket booth](#the-ticket-booth); 3: paid, waiting at the booth for the gate) → entering (5) → walking around (7)
→ going to a ride (10) → walking to a queue position (12) ⇄ standing in the queue
(11) → called: walking to the stand point (13) → waiting to board (14) → using (16, hidden;
a guest whose body the object keeps stays in view, see [Kept bodies](#kept-bodies))
→ exiting a ride (15) → … → leaving the park (18) → crossing (19) → waiting to go
home (21) → removed.

**Pool states** (the `opentpw.pools` extension, [POOLS.md](POOLS.md#the-swim-session-extpools-swim); OpenTPW's own values, not `KIDSTATES` entries): after a changing room releases a guest it is `Swimming` (22), then `ClimbingOut` (23), then `ChangingOut` (24, hidden, 12 park turns), then `ExitingRide` (15) on the room's exit. During the session a guest may take a pool activity ([POOLS.md](POOLS.md#activities-extpools-activities)), `PoolActivity` (25), with its own generic fields (an id, target, phase, turns and a point); it is part of the session, visible, uncatchable and counted as walking. "Changing in" is not a value of its own but the existing `Using` (16, hidden) at a changing room (`GuestSimulation.IsChangingIn`): a fourth value would duplicate every `Using` check for no behavioural difference. The swim fields of a guest (heading, turns, stroke, goal, own random stream) exist only in these states; swimmers are in no walker cell list, are not catchable, count as walking in the statistics (`ChangingOut` as on a ride), and finish their swim before they can decide to leave the park.

**Approximations** (no original source; values in `GuestSettings`):

| Item | OpenTPW choice |
| --- | --- |
| Arrival step-off | One guest steps off per park turn while the arrival vehicle reports status 2 at its stop (tp.exe `0x4cf3e0`, [Arrivals](#arrivals)) and walks the cell map to the booth and into the park; a leaving guest walks to the vehicle's stop cells and boards at status 4 (ARRIVAL-001); OpenTPW also caps the simulation at 1,000 guests |
| Needs | traced ([below](#needs-and-starting-values)); the energy meter is tp.exe's unnamed float +0x1B8: 0 at creation, a completed use takes the type's `FatigueEffect` off it (`0x4fd970`, clamped to 0..100; 5 in the shipped data, so it stays 0), and only the save reads it; thoughts as in tp.exe ([Thoughts](#thoughts)) |
| Starting happiness | 50 of 100 (traced, [below](#needs-and-starting-values)) |
| Decision | traced ([below](#deciding-and-wandering)): once per park turn in state 6; the original's score (oracle F24, [below](#attraction-choice)) picks the best attraction of at least 10 at most every 30 turns, otherwise the guest walks one to five cells to a wander destination (state 7). The litter step and pranks are modelled ([litter](#litter), [pranks](#pranks)), and so are the turns toward a show object and a performing entertainer, which end the call (`0x4fec90` steps 5 and 6, `0x501020`, `0x500f00`); a guest on a cell without links searches the cells around it for a path cell (`0x4f9490`) and a guest with no way on is stranded until a cell near it is edited (`+0x198`, `0x4fa770`); the rest of GUEST-010 is a linked start cell that is no path cell |
| Boredom | none: the major decision (tp.exe `0x4ff419`) has no boredom test and nothing reads `BoredomThreshold`; a decision that finds nothing costs the small step |
| Ride outcome | the original's bands and illness gain (oracle F24); a ride does not extend the exit counter |
| Queues | traced; see [Queues](#queues) and its `QUEUE-NNN` register |
| Walking | traced; see [Walking](#walking) and its `WALK-NNN` register |
| No exit lane | guests vanish (original ejection not modelled) |

### Attraction choice

Traced from the Mac binary and executed in the PowerPC oracle (finding F24, [ORACLE.md](reverse/ORACLE.md)); implemented in `AttractionScore`,
`AttractionChoice` and `RideOutcome`. A guest scores every attraction as seven **matches** (0..100 each) normalised by the sum of **all** seven
`PeepInfo.DecisionVar*Weight` values, so a perfect ride for a guest without needs scores 27, not 100:

| Match | Rule |
| --- | --- |
| Distance | `100 − min(100, 100 · (dx² + dy²) / 450)`, squared cell displacement to the attraction's back-of-queue cell (divided by the environment's attraction word at that cell when it is nonzero: only fireworks write it, row 7) |
| Queue | `100 − 100 · guests / (4 · queue cells)`, only within a squared distance of 8, **unclamped** (a queue above four per cell wraps) |
| Excitement | `2 · (50 − min(50, abs(preferred − live excitement)))`; its weight counts only when the type's `ExcitementLevel` byte is nonzero |
| Thirst, hunger | `trunc(sqrt(effect / 10) · need²)` with the shop's `ThirstEffect` / `HungerEffect` and the need in tens (the original's 11 × 11 table is this formula) |
| Toilet, illness | a 21-entry curve (9 zeros, 1, 2, 5, 10, 20, 30, 40, 50, 60, 75, 90, 100 at `(meter + 4) / 5`), only for relief objects |

Then the score is multiplied by `DecisionVariable2` (5) for a ride at most `DecisionVariable1` (7) days old, by `DecisionVariable3` (5) for an indoor
ride while drops fall, by `1 + (cost + 1) / 10` for a ride with a golden-ticket cost, else by `1 + cost / 30000` when its first upgrade costs more than 3000, and
divided by 5, 4, 3, 2 for each match in the guest's used history (the first match only) and temporary history (every match). The used history takes an attraction when a use ends; an
attraction of the type used last scores 0 outright, so the divisors that matter are 4, 3 and 2. The temporary history takes an attraction the guest was turned away from (queue too long, too
boring, too exciting) and forgets it after 80 park turns (a 0 is shifted in every 20 turns). The guest chooses the highest score of at least 10 (ties go to the later attraction on odd park turns), among open, unbroken
attractions it can reach; there is no random bonus. A use gives happiness `PerfectRide` below 5 from the preferred excitement, `GoodRide` below 15, `OKRide` below 40 and nothing beyond (no penalty) and raises illness by
`excitement / RideVomitDivisor · ((100 − hunger) / 20)`, so a hungry guest (81 and up) is not made sick.
Open: the decision cadence and the "nothing found" happiness loss, the minor decision, the environment grid, price eligibility, shop and toilet effects on the meters (`GUEST-003`).

### The ticket booth

At the booth (state 4, tp.exe `0x4ff9d0`) the guest weighs the entrance fee against what the park is worth (`0x4ff5b0`, "Working out
how exciting park is"). The park value is GP = S / (N + R % (N / 2 + 1)) + `MinimumEntryFee`, with S the park's attraction points (the
same sum as the arrival rule's, `0x4c8240`), N = `PeepInfo.ExcitementToCostDivisor` (4) and R a draw. A fee and a value both at or below
`MinimumEntryFee` (20) are fair. Otherwise the fee, against GP in single precision, is:

- from `ExpensivePriceMultiplier` × GP (2.0): far too expensive — the guest thinks 6 and goes home;
- from `AveragePriceMultiplier` × GP (1.25): on the expensive side — the guest thinks 6, loses `MediumHappinessChange` and waits: the counter
  of 50 + R % 50 counts down one per park turn and the verdict runs on the turn after it reaches 0, N + 1 turns in all (OpenTPW times
  them on the frame clock, ARRIVAL-006); once its happiness truncates to 0 it goes home instead;
- up to `CheapPriceMultiplier` × GP (0.75): cheap — the guest gains `MediumHappinessChange` and pays;
- otherwise fair — the guest pays.

An empty park is worth 20, so a fee of 24 is fair and 25 already expensive. A paying guest's fee is booked to the bank (tp.exe `0x4d0600`,
`ParkEconomy.TryAdmitVisitor`) without reading or debiting the guest's purse, as in the original. The booth has no park-closed test: a
closed park stops its visitors on the walk to the booth ([GATES.md](GATES.md), "What closing does to guests"); OpenTPW's economy refuses an
admission only while the park is bankrupt (tp.exe's bankruptcy stops the thing loop, ECON-030). The paying verdict sets the guest's paid word (`+0x204`) and state 3 with its wait; a guest
sent home (far too expensive, a refused booking) takes the way home of state 3: walk mask 1, a draw picks the gate's crossing cell, state 18 with the exit counter 0 so that the recall never takes it back.

### Thing ids

Every thing of the park takes its id from **one pool of 10239 slots** (tp.exe's allocator `0x516270`, reached from the base constructor `0x50AFE0` by the twelve thing constructors); `ThingPool` is the port, and the park's
`Level.Things` is the one pool of a park. A guest's id (`Guest.Id`) is its thing id: the needs phase (`0x501676`), the reseed of the world generator when the guest is made (`0x4FB19E`), the prank flag
(`0x4FB114`), the advisor's prank line (`0x59B060`), the queue edit's anger (`0x501496`, id modulo 3), the dismissal's voice (`0x50133D`, low three bits 0) and the booth walk's speed term (`0x4FF776`, low two bits 0) read it.

- **Allocation.** The free list starts ascending (slot 1 first); the allocator takes its head and the new thing becomes the newest of the used list (the original's thing sweep walks that list newest first, and so does OpenTPW's park turn: see "The park turn's order" in [DETERMINISM.md](DETERMINISM.md)). A full pool logs a
  fatal error and gives id 0 (OpenTPW refuses to build the thing).
- **Free.** A destroy request (`0x50B780` → `0x516310`) puts the id at the head of a queue; the park turn's end takes the head again and again (`0x516641`), so a turn's requests are carried out last first, and each slot goes to the
  head of the free list (`0x516200`): the next thing built takes the slot freed last, which is the request made first. Guests ask for it when they leave, objects when they are demolished or sold, staff when they are dismissed.
- **A new park** builds ten singletons first, in the constructor order of `0x5156A0`: the staff HQ (1), the mechanic HQ, the advisor controller, the tag system, the park analyser, the research lab, the weather, the bank, the UI message
  receiver and the challenge system (10); then Gates (11) and Lights (12), which the shipped jungle save lists as things 11 and 12 (`ThingPoolTests`). The first guest is thing 13; the arrival vehicles take their slot when they are first sent; a hired
  staff member takes its slot at the placing click (`0x46C8E0`), not at the hire.
- **Saves.** An original save stores the chain of used things, each with its id and model, from the head (newest) on; the load keeps every thing in its own slot and rebuilds the free list ascending (`0x518D43`). OpenTPW's importer puts
  the chain back the same way (guests, attractions and staff keep their thing ids). A saved thing it does not rebuild (a guest in a state it drops, an object that did not resolve, a staff member nobody employs) keeps its slot as a
  placeholder (`ThingPool.Hold`, `OriginalThingSlots`), as the original's load would: the next thing built gets the id the original hands out, and nothing in OpenTPW names or frees the slot (docs/SAVE-THINGS.md lists what the
  import does not rebuild). OpenTPW's own park save keeps the pool (`Things`: the used list, the head of the free list in its exact order, the waiting destroy requests and the placeholder slots, so a resumed park hands out the
  ids a continuing one would; `[EXT:resume-exact-thing-pool]`, an OpenTPW save keeps what the original's format drops), the objects' and staff members' `ThingId`, and, for a standalone guest simulation, its own pool; guest snapshot
  schema 8 marks guest ids as slots, and a save from before it keeps its counter ids as slots and builds the singletons and objects around them. All of it is optional in the file and validated (docs/PARK-SAVES.md).

What is still unlike the original is `WALK-010` below.

### Needs and starting values

Traced from the Mac binary and executed in the PowerPC oracle (finding F25, [ORACLE.md](reverse/ORACLE.md)); implemented in `GuestStart`, `NeedsUpdate` and `UseEffects`. A new guest starts with
happiness **50**, thirst and hunger a random 0..49, toilet 0..29, illness and litter 0, an exit counter of `ExitLevel` plus a draw within ±`ExitLevelVar` (60..179 with the shipped 120 ± 60) and cash
`StartingCash` ± `StartingCashVarPc` percent of a random type's. Every park turn each guest goes through the needs function; **it does something only on the guest's phase**, when the low two bits of the park turn equal those of
its thing id (the guest's id, a slot of the park's thing pool; see [Thing ids](#thing-ids)):

- the exit counter loses 1 (so one count is 4 park turns = 0.992 s, in every state, and it runs negative after 0);
- the cell's environment words move happiness, illness and hunger (vomit, stinkbombs, entertainers and guards so far, [Pranks](#pranks); GUEST-006);
- on a turn that is also a multiple of 16, toilet +1, hunger +2 and thirst +2 (all clamped to 0..100). A multiple of 16 has its low two bits 0, so **only a guest whose id is a multiple of four ever grows**;
  three in four guests keep the needs they started with unless they use something;
- each of illness, hunger, thirst and toilet at a truncated byte of exactly 100 costs 1 happiness;
- the toilet speed term is 25 while the toilet byte is above 80.

Using an attraction then applies its own effects after the outcome of [Attraction choice](#attraction-choice): thirst and hunger fall by the type's `ThirstEffect` / `HungerEffect`, illness and happiness rise by
`VomitEffect` / `HappinessEffect` (clamped); a toilet sets the toilet meter to 0 (GUEST-005). Illness has no decay of its own. A guest at hunger 80 at a shop with a hunger effect of 50 goes to 30.
The rest of tp.exe's use routine `0x4fe1e0` follows, with the object's ingredient amount (50 when built): for a nonzero hunger, then thirst effect a world draw `r`
and a small happiness loss when `(r & 7) + amount + effect` is below 30; happiness `+ trunc(amount × HappinessEffect / 100)`; the special ingredient (Fat: toilet
`+ amount`, Salt: thirst `+ amount`, Ice: thirst `+ trunc(amount × ThirstEffect / 100)`, Sugar: the speed boost); the appearance; and a relief visit's toilet speed term of 25.
The draws come from the park's one world generator (DET-014). There is no boredom loss: nothing in tp.exe reads `BoredomThreshold`.

### Lifecycle

Traced and executed (finding F26, [ORACLE.md](reverse/ORACLE.md)); implemented in `GuestLifecycle`. A guest starts at happiness **50** with the exit counter described under [Needs](#needs-and-starting-values), which loses one per
needs phase (4 park turns, 0.992 s) in every state. A guest that is walking around or doing nothing (the original's state 6) **leaves when its happiness is below 1, when its exit counter is exactly 0, or when the park is closed**, and
loses `BigHappinessChange` (25) as it goes home; the test runs on every park turn the guest spends in state 6 ([below](#deciding-and-wandering)). The counter compares for equality, so a guest that is walking to a wander
destination, queueing or riding in the four turns it reads 0 never leaves by it; rides do not add to it. There is no "happiness at most 10" rule. The leaving gate needs a way out (a path to an arrival lane's end); without one
the guest still loses `BigHappinessChange` and decides on.

### Deciding and wandering

Traced and executed (x86 findings X18, X19, [ORACLE.md](reverse/ORACLE.md) section 7.14; [WANDER-plan.md](reverse/WANDER-plan.md)); implemented in `GuestDecision` and `GuestWander`. A guest in the park alternates
between deciding (state 6, `DoingNothing`) and walking to a wander destination (state 7, `WalkingAround`). Once per park turn in state 6 it draws one number R and acts on the first test that applies:

1. **Cheering** when more than 100 turns have passed since its last activity and its happiness byte is above 80.
2. **Vomiting** when its illness byte is 100 and R % 3 is 0: illness drops to 0 and vomit stays on its cell ([below](#litter)).
3. **Litter** when its litter meter byte is at least 90: it walks to a bin in reach, or drops its litter on its cell and goes on ([below](#litter)).
4. **Leaving** by the [lifecycle](#lifecycle) test.
5. **Pranks** when the guest is a prankster and unhappy ([below](#pranks)); the decision goes on either way.
6. **R % 3 = 0:** a major decision, if more than 30 turns have passed since the decision stamp (the turn it last entered a queue or last failed a decision). It picks an attraction (state 10), or it pauses ("can't decide"), loses `SmallHappinessChange` and stamps the turn.
7. **R % 3 = 1:** a wander. The guest takes R′ % 5 + 1 random steps along the path links (never straight back while another way is open, one more step if it ends where it started) and walks the shortest route to a random point in the first quarter of the last cell. If there is no destination, it stamps the turn.

An activity (cheering, vomiting, can't decide) holds the guest for ten park turns (the original's state 8). On arriving, a walking guest picks another destination with a chance of one in four, otherwise it decides again. A new park's guests make their first major decision at turn 31.

### Litter

Traced and executed (x86 findings X18, X20, X21, [ORACLE.md](reverse/ORACLE.md) section 7.15; [WANDER-plan.md](reverse/WANDER-plan.md) §6); implemented in `ParkLitter` and
`GuestSimulation`. Every use of an attraction adds its `UsageInfo.LitterEffect` to the guest's litter meter (0..100; drinks and burgers 50, ice cream 25). In
state 6 a guest whose meter byte is at least 90 looks for a litter bin (`UsageInfo.HoldsLitter`) less than 3 cells away (squared distance below 9) that a
route reaches, the nearest first. It walks there (state 9) and throws its litter away, which sets the bin script's `VAR_TRIGGER` to 1
(the bin's own sound and animation); with no bin in reach it drops its litter on its own cell (one of five
kinds) and decides on. Either way the meter empties. Vomit lies on the cell where the guest was sick.

A cell holds one item of litter: what falls on a littered cell is lost. Litter stays until a handyman sweeps it; it does not decay or spread. An online visit
drops none. The handymen strike over litter when the littered cells exceed 0.2 of the path and queue cells. In a level the handymen patrol
the paths and take the nearest unclaimed litter within their `DetectionRange` (2 to 5 cells by grade); each walks there and sweeps it for `WorkDuration`
park turns from his arrival (ORACLE.md X23, WANDER-plan.md §4). A headless economy without a map takes the first unclaimed littered cell, scanning columns
from the left.

### Pranks

Traced and executed (x86 findings X18 and X22, [ORACLE.md](reverse/ORACLE.md) section 7.16; [WANDER-plan.md](reverse/WANDER-plan.md) §7). `PeepInfo.PrankeryLikelihood`
guests in 100 (5 in the shipped balance) are pranksters, of one of three kinds by their id: stinkbombs, litter or balloons. In state 6 a prankster whose
happiness byte is below 15 pranks when its flag beats a draw modulo 101: a stinkbomb on its cell (`StinkbombLikelihood` percent of the time, 25),
litter on its cell, or another guest's balloon burst (the first other guest on the cell holding one; without a victim there is no prank). A stinkbomb
plays `cat_kids` 207 at the prankster. Each prank cheers the prankster up by 1. The prank message reaches the advisor and the staff HQ at once
(the bus `0x40fb10` delivers it as it is sent). The advisor queues one of its three prank lines (advice 0x7D, 0x7E or 0x7F, responses 302–307,
scored by `PrankSbomb.Score`, `PrankLitter.Score` and `PrankBalloon.Score`), chosen not by the prank but by the message's kind word, which the
prank step fills with the prankster's id modulo 3 (tp.exe `0x4ff2f8`, `0x59b060`).

A prank calls the guards (tp.exe `0x508a30`, WANDER-plan.md §4), but only on a cell with security: a guard spreads `RegionFX[3]` (security 10,
radius 3) where he stands, so a prankster within three cells of a guard is seen. Then the free guard (idle or patrolling) **nearest the staff HQ**
is sent, and the HQ always sits at map cell (0, 0), so the guard nearest the map's corner goes, however far he is from the prankster. He walks
to the prankster (cat_staff 136) and chases it for `GuardConstsPerGrade.WorkDuration` turns (10 to 75 by grade), re-routing when he arrives where
it no longer is. On its cell he catches it if it is deciding, wandering, taking litter to a bin or walking to a ride (cat_staff 137): he carries it,
out of sight, to the ticket booth (in OpenTPW the park entrance, as the booth lies off the paths) and sets it down there at the cell's centre
(cat_kids 128). A world draw's bit 0 picks the side it leaves by (`0x4feb50`, `0x4d86d0`): it heads for that side's exit, and is removed when
that side is out of reach. Like any leaving guest it is recalled on its next turn while its exit counter and happiness are above 0 and the park is
open (the leaving handler `0x500a50`), so the guard's escort sends home only a guest that wanted to go anyway. A carried guest whose guard is dismissed or abandons the escort is removed from the park, as in the original.

Vomit and stinkbombs spread their `RegionFX` row around their cell while they lie: every cell within the row's square radius gets the row's
happiness, illness and hunger divided by its distance + 1, and a guest's needs phase adds those of its own cell (vomit: −1 happiness, +1 illness,
−1 hunger on its cell; a stinkbomb −3, +3, −1 on its cell and less up to 3 cells away). Entertainers and guards carry their rows with them: an
entertainer's `RegionFX[0]` adds 2 happiness on his cell and 1 beside it, a guard's `RegionFX[3]` the security above. The other sources (toilets,
cameras, fireworks) are not modelled yet (GUEST-006).

### Thoughts

Read in full in tp.exe (GUEST-006). A guest's thought (the visitor window's line and the park information window's Top 3 Thoughts) is set by the thought
object `0x50be80`, which 22 places call:

- **The needs phase** (`0x501650`), outside states 16 and 17 and on a park cell, picks on one phase in ten (`0x4fc8a0`): hunger and thirst
  bytes both above 90 → "Hungry and thirsty"; a hunger, thirst, toilet or illness byte above 99 → that need; else more than 20% littered
  path cells in the guest's 4 × 4 block → "Too messy"; else happiness above 99 → "Happy!", below 10 → "Unhappy"; else the thought stays.
- **Events:** a queue join refused as too exciting ("Scared"), not exciting enough ("Bored") or too long; leaving a queue for the toilet,
  illness, unhappiness or boredom; a price refusal at the queue's head or the booth ("Dissatisfied"); navigation failures ("Confused"); a
  sideshow won ("Pleased") or lost ("Dissatisfied"); sent away by a queue edit ("Angry", one guest in three, `0x501496`). A ride or shop
  visit sets no thought.
- **The staff** think through the same object (`0x506a40`, `0x5056e0`, `0x506f30`): 19 when unhappy (happiness byte 10 or less), 18 when
  very happy (above 97, one gate pass in sixteen), 20 when tired and off to rest, 21 while walking out to strike, 22 when no cell of the
  patrol area can be reached. Their bubbles go on the member's phase (`0x505490`), and when it rests or is picked up.
- **The bubble:** each thought replaces the bubble, and a new one shows when the last appeared at least 20 × the thought's priority turns
  ago (0 for event thoughts, 1 for needs and "Too messy", 3 for the happiness thoughts); it goes on the first needs phase more than 12
  turns after it appeared, and when the guest boards a ride or is caught. Ids 18-22 are the staff's own (tiredness, walking pace, no
  destination).

The thresholds OpenTPW used before (`NeedThoughtLevel` 70, happiness bands 90/70/30, "Super happy" and "Okay") have no source in tp.exe and
are gone; the original never thinks those two. Not yet: "Angry" for a failing coaster (`0x500650`).

**Bubbles** are drawn by `ThoughtRenderer` from `Generic/Thoughts`: `SPR_TB`'s sixteen guest bubbles (a smile, a frown, a burger, a drink,
the toilet, litter, a question mark, ...) and `SPR_TC`'s ten (a thumb up for "Pleased" and the staff's own: a pink smile and frown, a
sleeping face for "tired", a protest sign for "strike", a question mark). Each thought's bubble script picks its slot (`0x74f190` on); the
bubble hangs 2.5 units above its thinker (`0x4fa030`) with the sprite's own offset, up and to the right of the head.

#### Top 3 Thoughts (park information window)

Traced in tp.exe (the builder `0x4A52A0`, the window procedure `0x4A4830`, the refresh `0x4A4AB0`, the ranking `0x4C8430`). The window shows **three `ti_hungry` icons and no text**:

- **What is counted:** the thought id (the first dword of the guest's thought object, `+0x30`) of every guest (thing type 1) standing on a park cell (`0x4FA990`: cell types 0, 1, 3, 9 and 10, whatever its state; a guest in the booth queue or on the road home does not count), into 23 counters for ids 0 to 22. **The count of id 0 (no thought) is then discarded** (`0x4C84D7` writes 0 to counts[0] right after the counting loop), so the guests without a thought never take a slot.
- **Ranking:** three slots start as the ids 0, 1 and 2. Each id from 3 to 22, in order, finds the slot with the lowest count (the largest difference, signed, strictly above 0; the first slot among equal lows) and takes it if its own count is strictly higher. So an equal count never replaces a slot (the lower id keeps it on a tie), and **the slots are not sorted by count**. A slot whose count is 0 is written as 0x17 (nothing). The three slots are the result in slot order.
- **Icons:** slot 0 goes to the rightmost icon (`0x4A68D4`), slot 1 to the middle (`0x4A68D3`), slot 2 to the leftmost (`0x4A68D2`). An id of 1 to 22 sets the icon's frame to the id less 1; 0 and 0x17 hide the icon ; an icon the update hides (`0x65D9DD(0)`) is never shown again while the window stays open, as nothing in `0x4A4AB0` or the window procedure calls the show form (not fully settled, `APPROX:UI-914`).
- **Hover text:** each icon's handler `0x4A4700` answers message 0xC with the THOUGHTS.str entry of the icon's frame plus 1 (table `0x78BA9C`, as the visitor window reads it); the icons are control type 6 of table `0x7537D0` (the factory `0x65DD7B` builds them through `0x65E057` and the constructor `0x66D9E0`, vtable `0x705C58`), whose frame setter (slot 7, `0x66DA4D`) stores the frame at `+0x13C` before the base setter `0x661525` and whose getter (slot 8, `0x66DAD2`) returns it. So **each icon names its own thought** (the frame plus 1 is the thought id), and OpenTPW does the same.
- **Cadence:** the builder arms timer `0x80083` at 1000 ms and runs the update once as it builds; every timer message re-reads the counts, the happiness levels and the thoughts. There are no strings: the title is UITEXT `0x9C` ("Top 3 Thoughts") and the frame's help id is 186.
- **In OpenTPW:** `TopThoughtRanking.Rank` (pure) and `GuestSimulation.GetTopThoughts()` (a read of the guests on park cells); `ParkHud.OpenParkInfoWindow` draws the frame of the original table `0x7537D0` (`OriginalLayoutTables.ParkInfoWindow`) and refreshes it every second. This replaces `GuestStatistics.CommonThought`, a most-common-thought guess no code read, which the original never computes. Staff thoughts (18 to 22) live on staff things, which this census never reaches. The window's other parts are described in [UI.md](UI.md) (graph over the monthly statistics rings of `0x4C7720`, counts frame, the happiness bars from `0x4C9750` / `0x4C9670`, the navigation buttons).

### The environment grid

Every map cell carries five words (happiness, illness, hunger, security, attraction) that the original's region sources spread around them
(`RegionFX[0..7]` in `levels/Standard.sam`, the writer `0x4d8480`: the row's coefficient divided by the Manhattan distance + 1, within its
square radius). A guest's needs phase adds the first three words of its cell. The sources, all modelled:

| Row | Source | When | Shipped row |
| --- | --- | --- | --- |
| 0 | entertainer | where it stands, moving with it | radius 3, happiness +2 |
| 1 | clean toilet | from placement while the gauge is 25 or more | radius 3, illness +1, hunger −1 |
| 2 | vomit | while it lies | radius 1, happiness −1, illness +1, hunger −1 |
| 3 | guard | where he stands, moving with him | radius 3, security 10 |
| 4 | security camera | from placement | radius 5, security 20 |
| 5 | stinkbomb | while it lies | radius 3, happiness −3, illness +3, hunger −1 |
| 6 | dirty toilet | from the use that takes the gauge below 25 until a handyman cleans it | radius 3, illness +2, hunger −2 |
| 7 | fireworks (Hallow's Firework Show, Space's Laser Show) | from placement until the show's script sets `VAR_STATUS` to 1 | radius 6, happiness +2, attraction +10 |

The security word is what a prank tests before a guard comes, so a camera guards its square as a guard does. The attraction word divides the
distance match of an attraction whose back-of-queue cell lies under a show (`0xe9388`). The objects' rows come from the constructor `0x4db090`,
the destructor `0x4dd0a0`, the toilet wear `0x4e2440`, the handyman's clean `0x4dfd80` (which also writes `VAR_WORN` = 0, so the toilet's script drops its dirty look; ECONOMY.md "Toilets: the door, the call and the clean") and the fireworks tick `0x4e0b90`.

### Balloons and costumes

Read in full in tp.exe (ECON-044). The use routine (`0x4fe1e0`) switches on the type's `UsageInfo.AppearanceEffect` (+0x15c) after the special
ingredient: the balloon shops (1) give the guest a balloon, the costume shops (2) put a costume on, or take it back from a guest who already
wears one ("Customer returning a costume"). A balloon lives the shop's quality of goods × 255 / 100, within 25 and 255; OpenTPW has no quality
control, so the build's quality 50 gives 127. The life (`mRemainingBalloonLife`, +0x214) loses 1 on every needs phase (every fourth park turn,
`0x501650`) while the guest is in the park and neither on a ride nor held by a guard; the balloon goes at 0, about two minutes after the purchase.

A ride puts the balloon away (state 16, `0x501db0`) and gives it back after the visit when it still has life (state 15). A balloon prank or a
guard's catch bursts the balloon but leaves its life, so it comes back after the guest's next ride. The challenge types 9 and 10 count the
in-park guests holding a balloon or wearing a costume, as a truncated percentage of the in-park guests (`0x4d1746`). How they are drawn:
see Rendering.

### Walking

Traced from the Feral Mac binary ([WALK-plan.md](reverse/WALK-plan.md) §11, WALK-I;
`GuestNavigation`, `GuestSpeed`) and, for the walk itself, from tp.exe (`WalkSearch`,
`WalkEdges`, `GuestNavigation.Navigate`). There is no global walk speed and no energy factor.

- **Speed** (§4). Each guest draws a base speed (`+192`) from {60, 80, 100, 120, 140}
  from the world generator at creation (the base constructor's first draw, `0x4F89E1`). Once per park turn (248 ms), before its state
  logic, it smooths s ← (3·s + (base + toilet term + boost) / 100) · 0.25 from s = 0,
  in single precision with one fused multiply-add (`fmadds`, PPC oracle F6). The toilet
  term (`+194`) is 25 while toilet > 80 (the truncated toilet's low byte, unsigned),
  rewritten after the speed update, but only on the guest's needs phase (park turn & 3 =
  id & 3, `0xeed40`). The boost (`+196`) decays by trunc(× 99 / 100) per turn, with the product kept to 16 bits. It is raised only when a guest
  leaves an object whose special ingredient is Sugar (the Ice Cream Shop, `UsageInfo.SpecialIngredient` 4): by
  trunc(6 × amount / 100) with the amount byte 50 the ride build stores (+3; at most +15), so a sugar rush lasts three turns and moves a speed of 1.0
  by about one percent ([ORACLE.md](reverse/ORACLE.md) F20).
  The speed setter (`0xffe38`) gives the max speed max(655, trunc(min(s, 2) · 0.2 ·
  65536)) and the max force with 0.4, in 16.16 cells per turn: 0.48 cell/s at s = 0.6,
  0.81 cell/s at s = 1 and 1.61 cell/s at s = 2.
- **Steering** (§3, §5–§7; tp.exe `0x50f3b0`). Positions and velocities are 16.16 fixed point. On the
  tick that starts a park turn, a walking guest takes one step: avoid_walls (0.9),
  follow_path (0.5) and separation (0.1), each truncated to the max force, summed and
  truncated, added to the velocity, which is truncated to the max speed. A new
  destination keeps the velocity. On the park's cells this is tp.exe's walk
  (`GuestNavigation.Navigate`), checked against the original routines in the x86 oracle
  ([ORACLE.md](reverse/ORACLE.md) X25):
  - **Route.** Every destination (SetDest `0x510100` → set-up `0x50f8e0`) searches from
    the guest's cell (`WalkSearch`, `0x511420`): a four-connected line towards the
    goal; at a blocked edge two boundary tracers (one turning each way) follow the wall
    until one may leave it towards the goal; shortcuts over up to four waypoints
    (`0x5110d0`) and a refinement (`0x5108a0`). The waypoints are cell centres after the
    start, the goal last; five are buffered, and passing the fifth searches again
    (`0x50efce`). A failed search leaves the walk arrived and stuck (`+0xb8`): the walk
    step returns 2, which states 7 and 9 treat as the original does.
  - **Cells.** Walls, commits and searches ask the edge test `0x4d8750` (`WalkEdges`)
    with the walk mask (`+0xb4`, guest `+0x188`: 0 in the park, 1 after a guard's
    set-down, cleared by the recall): path to path is always open whatever the links,
    queue boundaries need the target's link back, mask 1 also opens steps from a path
    cell to ground. A step commits through `0x510510`: one cell at most, a diagonal
    through either L-shaped route; a refused step adds a history bit.
  - **Walls.** avoid_walls (`0x50e4a0`) takes the position's cell: the first edge in the
    direction of motion that the probe penetrates pushes back with twice the
    penetration; else the corner of the motion's quadrant whose diagonal cell is cut
    off pushes where the velocity's ray meets the circle of the radius around it
    (`0x50cc20`).
  - **Progress.** ONE − trunc(remaining × ONE / route length) (`0x50fd40`), the length
    taken over the whole route at every set-up; a step that does not raise it adds a
    history bit, and six bits among the last 15 search again from the current cell.
    Waypoints are passed at octile < 0.4 cell; the last one arrives at octile < 0.32
    cell of the exact destination, with the desired speed min(max speed, d / 2).
  - **Changed ground.** A cell type change stamps its 16 × 16-cell block
    (`ParkCellMap.BlockStamp`, `0x4d8c60`); a walk whose block's stamp is above its own
    searches again (`0x50ed6c`).
  - The arithmetic is tp.exe's: FixMul rounds towards −∞ (`0x50df70`), FixDiv towards 0
    (`0x50e080`).
  Queue walks are this walk too: the edge test opens a step between linked queue
  cells and from the join path into the back cell through the link bytes the queue
  tool writes. The arrival chain (states 0 to 5, over walkway cells) and the way home
  are this walk too. The last walk into the ride's entrance cell (the stand point) is this
  walk (an imported park's entrance cells carry their saved link byte, and a park resumed from an
  OpenTPW save made before the import took that byte gets it laid at load, `[EXT:legacy-save-access-links]`); a search the
  edge test refuses is the original's failure: the log line, the called slot released, the guest unlinked and dismissed. Only a
  walk restored from an older OpenTPW save keeps OpenTPW's route walk (`[EXT:legacy-save-route-walk]`): the route's cells are the walkable ones,
  avoid_walls has its edge branch only, and the Mac's rounding applies.
- **Separation** (`0xfd778`, `0x10087c`, `0xfd5f4`). Off on the last segment within
  ONE + radius of its waypoint; else every guest in the 3 × 3 cells around the walker
  (rows from the top) whose 1/256-cell position lies within 0.4 cell pushes it away by
  FixDiv(offset, octile(offset)), at most five of them.
- **Geometry.** Sub-cell destination bytes are byte / 256 of a cell. A walk to a ride is
  one route to the centre of the ride's back cell and the walk out of the park one route to the centre of the gate's crossing cell; the arrival chain walks the cell map too. Queue walks follow the
  queue cells; the cell ahead of the destination and the cell behind the start are
  walkable too. The stand point is in the ride's entrance cell at trunc(255 ×
  `EntryCellStandPos`) after rotating it with the object (`0xde1d8`): (0.5, 0.5) gives
  (127, 127).
- **Presentation.** The drawn position and walk distance interpolate between the last
  two steps over a turn (`Guest.DrawX`, `DrawY`, `DrawDistanceWalked`); they are not
  simulation state.

**Walk approximation register** (`WalkApproximations.cs`; also in
[FIDELITY-REGISTER](https://opentpw.io/docs/FIDELITY-REGISTER.html)):

| ID | OpenTPW choice | Evidence needed |
| --- | --- | --- |
| WALK-003 | Separation's neighbours are the guests of each cell outside rides and shops, head first like the original's persistent chains (the guest that entered the cell last first; kept across the park turns and saved), then the staff on the cell; the original (tp.exe `0x5102f0`, at most five neighbours) walks one chain per cell, in the order things were head-inserted on cell entry (`0x50b6a0` via `0x4d91f0`), keeps class-1 guests in any state and also the online avatar (class 18), and interleaves the staff (classes 4 to 8) with the guests by entry time; the position (`+4/+6` = navigation position >> 8, `0x4fa2a0`) matches | The entry order of the staff (they keep no cell list); where the thing position of a guest on a ride, held by a guard or leaving lies |
| WALK-005 | The edge test `0x4d8750` (`WalkEdges`) reads only the cell map plus one resolver: no track record of a target cell blocks (`0x5363f0`: kind 11, 13, 16, 18 or 25, or the parent of a kind 12 or 17 record; the step is open only when the record's flag nibble `+0xE & 0xF` is set, `0x53ad20`), type-17 and zero-parent records keep the old fallthrough, and the types are those OpenTPW's map holds (map init `0x536490`'s 0, 1, 2, 7 and `0x1e`, and the path, queue, entrance, exit, object and track cells its tools write). The mask-2 rule for a type-9 target (the owner cell's first object must be rideable, `0x42a440` = `RideCameraMode.CanRide`, and its type's CannotRide `+0x118` 0) is ported (`ParkCellMap.EntranceRideOpens`), but its only callers are the camera's (`0x42c093`, `0x42c290`); the underground link byte `+0x22` is read only while the land tools' flag `0x81b4cc` is set (`0x532408`..`0x532622`) | The track records of the cell map (TRACK-002) and the writer of their flag nibble |
| WALK-010 | Thing ids are slots of one pool of 10239 for every kind of thing, taken in the order the park builds them (see "Thing ids"); but the pool holds only what OpenTPW builds (the online persons and the developer prototype ride take no slot; the finale's End takes its slot like every placed object; a saved thing of an imported original save that OpenTPW does not rebuild holds a placeholder slot that nothing simulates or frees), and the guests' histories, queues and the economy hold the script world's attraction ids rather than the objects' thing ids | What else the original builds over a session |

Retired: `APPROX:WALK-008` (the arrival chain, states 0 to 5: tp.exe `0x4fb201`, `0x4ff520`, `0x4ff730`, `0x4ff7f0`, `0x4ffb20`, walked over the cell map like every other guest walk) and `APPROX:GUEST-012` (the state-18 recall for every leaving guest: the booth refusal and a closed park's state 3 enter state 18 with the counter zeroed, `0x4ff9d0`, `0x4ff7f0`). `APPROX:WALK-002` (avoid_walls' corner blocks and the position's cell: tp.exe `0x50e4a0` with the ray test `0x50cc20`, ported for walks on the park's cells). `APPROX:WALK-004` (the whole-walk destinations: state 6's choice requests one route to the ride's back cell, `0x4fcb10` through `0x4fa530`, and its leaving gate one route to the gate's crossing cell, `0x4d86d0`; the states then only step the walk, `0x4ffbc0`, `0x500a50`). `APPROX:WALK-009` (the progress measure `0x50fd40` over the whole route's length, reset at every set-up `0x50f8e0`).

Retired (wave 5): `APPROX:WALK-006` (the last walk into the ride's entrance cell, state 13's route request `0x4FA5F0` of `0x4FFFF0` to the entry cell `+0x36` read by `0x4DEDF0`, is the search and edge-test walk of every other walk; an imported park's entrance and exit cells carry the saved link byte `+0x0C` and a placement lays it through the outside path cell's link writer; a search the edge test refuses logs "the player has removed the path from under me and I can no longer get into object", releases the called slot `0x4E0AC0`, unlinks the guest `0x4DDD20` and dismisses it `0x5012F0`, as in the original). A park resumed from an OpenTPW save made before the import took the byte gets it laid at load: `EXT:legacy-save-access-links`.

Retired (wave 3): `APPROX:QUEUE-008` (the wear step's branch for game type 1: the type is the online one, `SetGameType(1)` at the online entries `0x4c0830` and `0x5e2700`, read through the game-type object `0xfb3b7c` and built by `0x550ca0` from the flag word `0x7a1a8c`; the branch wears nothing and cleans a relief object whose gauge byte is below 25 at once, row 6 off and row 1 on, script variable 8 = 0, gauge 100, claim and call cleared, `0x4e2478..0x4e252a`; `ParkEconomy.OnlineVisit` follows it for the online visit).

Retired (wave 3): `APPROX:QUEUE-021` (where the queue tool goes on after the click on a queue cell: the first click enters tool 0x14, the next cell click collects the queue's corners with `0x530120`, runs `0x4de1f0` and enters mode 3, and `0x530120` ends by setting the line start `0x81ede4`/`0x81ede8` to the queue's last corner, so the line goes on from the back cell as OpenTPW starts it; the two-click entry itself is the design choice `EXT:queue-click-at-back-cell`).

Retired (round 8): `APPROX:WALK-007` (the queue recompute's stamp of its back cell, `0x4de1f0` with `0x4d8c50` and `0x4d8c60`, after the queue tool's edits, a path laid over a queue end (`0x5346d0` at `0x534858`, the owning ride found through the cell's owner word `+0x10`) and a queue cell clear (`0x5367a0` at `0x53694b`)); the route walk restored from an OpenTPW save made before the original walk, which ends where the guest stands instead of replanning, is the design choice `EXT:legacy-save-route-walk`.

Design choice (wave 4): the conversion of a guest on an arrival lane in an OpenTPW save of schema 8 or older (the lane has no counterpart in tp.exe, whose guests walk the cell map from their creation) is `EXT:legacy-save-arrival-lane`: the guest stays where it stands on the map and takes the state its lane position stood for, with no world draw; a schema 9 save with a lane is refused.

Retired (wave 3): `APPROX:ARRIVAL-005` (the first purchase of an imported object: the world loader reads the object-control table `0x4d3aa0` before the saved things, every record's `+0x1c` comes back verbatim and a constructed object stamps the turn into a record only while it is 0, `0x4db690`; `OriginalEconomyImport.ImportFirstPurchases` restores the saved turns before the objects are registered).

An isolated [GuestOriginalRules helper](../tools/ppc-analysis/lanes/guests/rules/README.md)
and 23 synthetic regression cases preserve selected Feral arithmetic: four
positions per queue cell, normalized base score, distance divisor 450,
nonlinear need lookups, two ride histories, shop effect subtraction, and
hunger-dependent ride illness. The helper remains outside the production
project because current guest/attraction types lack the required metadata and
history bindings. It supplies no growth rate or timing rule.

The original score uses low-32-bit products/additions followed by unsigned
division. An overfull queue can yield a very large positive quotient; the
helper preserves that result and documents the later signed history division.
Zero divisors and invalid metadata domains remain unsupported, with explicit
rejection. Full scoring, candidate selection, queue geometry, source-table
loading, and simulation integration need independent review. The choices in
the approximation table above still describe the running OpenTPW simulation.

## Money

`GuestSimulation` keeps only the guests' purses, an admissions count and the `MoneySpent` event.
In original levels `Level` attaches the park economy (`ParkEconomyRuntime.AttachGuests`,
docs/ECONOMY.md): `GuestSimulation.Payments` (`IGuestPayments`, implemented by
`GuestEconomyBridge`) charges admission through `ParkEconomy.TryAdmitVisitor` — the economy's
entrance fee decides, refused guests turn back at the booth, and a closed park turns guests home as they
reach the booth or once they have paid (docs/GATES.md) — shop and
sideshow visits through `TryBuy`/`PlaySideshow`, and ride visits are counted with
`RecordRideUse`. The bridge also supplies `IParkGuestStatistics` (people in park, happiness).
Without `Payments` (unit tests) the `.sam` fee and `IRideVisitorBridge.Price` apply and nothing is booked.

## Visitor bridge and RSE visitor opcodes

`IRideVisitorBridge` (`IRideVisitorBridge.cs`) is the attraction side guests see:
id, name, kind, open state, capacity, `ExcitementLevel`, `AttractionValue`,
`Satisfies` (hunger/thirst/toilet for shops), `Price`, `EntranceCell`, `ExitCell`,
the queue (cells, join cell, limits, the called guest, join/leave/present
operations) and an `IRideVisitorHost` (implemented by `GuestSimulation`) for
callbacks: called forward (`OnVisitorOffered`), boarded, released, turned away.
The host also supplies the park turn, whether guests walk to the stand point,
and whether a guest stands at position 0.

`RideVisitorBridge` implements it for an RSE script. Host protocol (inferred from
the corpus: Coconut, Totem, Gift Shop, Bouncy, Jungle Spray all follow it), run
once per tick before the script slice (the original's host never looks at the critical flag, so there is no `CRIT_LOCK` gate):

1. `VAR_LETMEOFF ≠ 0` → release that guest at `ExitCell`, write 0 back (scripts
   wait with `TEST VAR_LETMEOFF; BRANCH_NZ` until the host has taken the guest).
2. The called guest's id was written to `VAR_LETMEON` and is no longer there →
   the script took it: it leaves the queue list and boards (original state 14).
3. Ride closed (its open word 0) → once per park turn the queue head is sent away
   (tp.exe `0x4e1220` → `0x4e0450`: unlink and the dismissal `0x5012f0`); the others wait.
4. Once per park turn, the admission of [Queues](#queues) may call the front
   guest forward. The guest writes its own id to `VAR_LETMEON` (only while that
   is 0) when it reaches the stand point. Hosts that do not simulate the walk
   (unit tests) present the guest at once.

Visitor opcodes (all operate on the guests the script holds; semantics inferred):

| Opcode | Bridge behaviour | Corpus evidence |
| --- | --- | --- |
| HUSH v | script heap push (VM-internal, not a visitor operation; tp.exe `0x553D25`) | Totem `HUSH VAR_LETMEON` before `WALKON`, `COPY VAR_LETMEON 0` |
| WALKON v … | guest boards; the body walks along the ride nodes, then stands, cheers or floats on board by the walk action (WalkerBodies) | Jungle Spray boards with WALKON alone |
| HOP dest | script heap pop, newest first (VM-internal; tp.exe `0x553DAA`) | Totem `HOP VAR_TEMP2; WALKOFF VAR_TEMP2` |
| WALKOFF v | queue v for WALKGET (instant) | |
| WALKGET dest | next walked-off guest, else 0 | `WALKGET VAR_LETMEOFF; BRANCH_Z` |
| LIMBO v s | script limbo table (VM-internal; tp.exe `0x554DE5`): the first free slot holds `v` for `s` **seconds**, result 1, 0 when full; boards nobody (the guest boarded when the script consumed `VAR_LETMEON`) | Gift Shop `LIMBOSPACE 0; BRANCH_Z; LIMBO VAR_LETMEON 5` |
| UNLIMBO / FORCEUNLIMBO dest | VM-internal: first slot whose time is strictly past / first occupied slot, else 0 (the script then hands the id on, e.g. through `VAR_LETMEOFF`) | `UNLIMBO VAR_TEMP; BRANCH_Z; WALKOFF VAR_TEMP` |
| INLIMBO / LIMBOSPACE dest | VM-internal: held count / header limbo size − held | |
| BOUNCE v d | guest boards, held `d` s (10 s when VAR_DURATION is 0) | Bouncy `BOUNCE VAR_LETMEON VAR_DURATION` |
| UNBOUNCE / FORCEUNBOUNCE out | writes the leaving guest into its operand | Bouncy `TEST VAR_LETMEOFF; BRANCH_NZ; UNBOUNCE VAR_LETMEOFF` |
| BOUNCING dest | bouncing count | |
| ADDHEAD / DELHEAD | the ride's head table (`RideHeadSpace`): a free slot takes the visitor, DELHEAD frees it; the head is drawn by `RideHeadRenderer` (below) | Mac `0x100B0E9C` / `0x100B0F90` |

`TOUR/BUMP/COAST` (ride-type controllers with their own guest handling, e.g.
`COAST 3 VAR_LETMEOFF`) stay unimplemented. `WALKST_FLOAT`, `WALKFLOATSTAT` and
`WALKFLOATSTOP` are the script's own float state, which the VM keeps (RSE-VM.md). A rider on a ride is not drawn as a guest (`RideHandlesSprite 1`);
on a ride whose script runs ADDHEAD the rider's head is drawn at the head's node
instead (see "Riders' heads"). Riders on the other rides stay hidden: the original
places them through the model and seat attachment is not implemented.

**Totem** (`PrototypeRide`): the bridge is wired into its effects; capacity 6,
excitement 70, attraction value 25 from `Totem.sam`. Its entrance/exit is the
nearest path cell to its 5×5 footprint (the prototype has no catalog shape yet;
jungle smoke site: cell (39, 21)). With guests near the ride the script fills at
script time ≈3.3 s and starts (versus 11.3 s without passengers), runs one cycle
and unloads all six through HOP/WALKOFF/WALKGET/`VAR_LETMEOFF`. (Measured before
the traced queue; guests now pass through the one-cell path queue at its
entrance. Since WALK-I each called guest also walks into the entrance cell to the
traced stand point before the next is called, so the asset test's first run starts
with a full load at about 17 s instead of before 10 s.)

## Queues

Traced from the Feral Mac binary in [QUEUE-plan](reverse/QUEUE-plan.md)
(sections cited below). Rules marked `[BIN:…]` at their code sites follow the
original; the rest are the `QUEUE-NNN` approximations listed at the end.

**Cells** (§3.3). A queue cell is original map cell type 3 with a link byte
(1, 4, 16 or 64) pointing back toward the ride entrance. They live in the
shared `ParkCellMap` (`GuestPathGrid.Cells`; PATH-plan §9.1/§9.6), which holds
per cell the original type byte (0 empty, 1 path, 3 queue), flags, the
same-type placement counter, the cardinal links and the queue link.
`GuestPathGrid` mirrors its path cells into the map and writes queue cells
through `SetQueue`/`ClearQueue`; queue cells are not walkable for routing. The **front cell** is the cell outside the object's
entrance (`QueueFrontCell`). The queue is the chain of type-3 cells whose link
points back at the previous cell, followed from the front at most 1,000 steps
(`RideVisitorBridge.RecomputeQueue`). That gives the back cell and the size in
cells, at least 1. A front cell that is neither a queue nor a path cell falls
back to the nearest walkable path cell as a one-cell queue (a cell nothing laid: a restored park or a harness; a build lays the queue cell, see OBJECTS.md, and an import takes the saved ones). Guests
step onto the back cell from the **join cell**, the path cell attached to the
back cell: the first cardinal neighbour (order 1, 4, 16, 64) whose bit is set in
the back cell's link byte `+0x0C` (tp.exe `0x4de840`). A queue whose back links
to no path is not connected and guests do not choose the ride. Every grid edit
recomputes all queues. A changed queue is a
queue edit: queued guests beyond 4 × cells or off the queue leave, and the
others change nothing (a guest whose place changed walks to it from its own state 11).

**Building** (§3.3, partly approximated). `QueuePaths.TryExtend` (headless)
and the level's queue tool (`Level.BuildQueueCell`) lay one cell at a time. The
first cell is the front cell, linked toward the ride. Every later cell touches
the back cell and links toward it. The queue tool's link pass (tp.exe link
writer `0x5348D0`, mode 3) writes the link bytes: the front cell gets the bit
toward the entrance, each later cell and the cell before it get each other's
bit while that cell has fewer than two, and a queue line that ends on a path
cell (the validator's code 8) links that path cell and the back cell to each
other (`QueuePaths.Connect`): the queue is connected to that path, and only
to it. A path laid later beside a queue does not connect it. Removing a queue
or path cell unlinks it on both sides (`0x5367A0`). The queue tool's entry walks the queue into a stack of
corner points (`QueuePaths.CornerStack`, `0x530120`: the front cell, every corner cell, the back cell) and Backspace in the tool takes the
queue back by the last segment of that stack (`0x52fe50`, never the front cell), also for a queue laid earlier. A ride's demolition
takes every queue cell with it (`0x527ee0`: the forced clears, each cell refunded at the ride's scrap percentage and one cell's refund
debited, `ParkEconomy.ChargeQueueDemolition`). Saves made before the
links existed get them when the ride registers: the chain, and the back linked
to its first adjacent path cell (`QueuePaths.RebuildMissingLinks`). Only `Info.HasQueue` objects take queue
paths, at most 25 cells. A queue cell goes only on an empty cell
(`ParkCellMap.CanChangeCellType`, traced; the original's line tool may also lay
queue over a path on every cell of a line but the last, which this tool does
not do). Object
footprints and terrain the object build rule refuses are blocked. Each cell is
charged `Costs.QueueCell` (75) when it is written, refused only when
balance − cost < 0 (`ParkEconomy.TrySpendCell`, `SetCellType` `0x82ac4`).
`QueuePaths.RemoveFrom` / `Level.RemoveQueueCell` remove a cell and every cell
behind it; each removed cell refunds `Costs.QueueCell` × the ride's scrap
percentage / 100 (`ParkEconomy.RefundQueueCell`, `ClearCell` `0x85dcc`; 100 for a ride nobody used in its first 30 days, ORACLE.md F21). After a HasQueue ride is
built from the HUD, park clicks lay its queue until Back (UI-031). Queue cells
also block object placement. Queue cells carry the theme's queue models ([PATHS.md](PATHS.md#path-textures)).

**Joining** (§4, state 10 at the join cell), in this order:

1. Room: the guest joins only while the count is below 4 × cells.
2. Excitement (ORACLE.md F16, executed): an object tests its guests only when the low byte of its
   `UsageInfo.ExcitementLevel` is nonzero, whatever its kind. The difference is
   clamp(excitement − preferred, −50, 50) over bytes, or 100 while the relief predicate
   holds (a toilet whose wear gauge is below 25). |difference| ≥ 45 refuses: a negative
   difference is "not exciting enough" (thought Bored), otherwise "too exciting"
   (thought Scared). The excitement is the live value of `0xdff70`: a sideshow
   20 + trunc(0.8 × win × sqrt(clamp(goods − price, 0, 100)) / 10) with the win chance
   100 − `InitChanceOfLoosing`, a coaster trunc(0.5 E + 50), any other object its level times
   the speed ratio and the duration ratio, each clamped to 0.75..1.25. A sideshow at level 30
   with a prize 25 above its price and a win chance of 50 scores 40, so a guest who prefers 80
   joins; a ride of level 100 at a tenth of its design speed scores 75, so a guest who prefers 50
   joins. Four shipped rides whose `InitDuration` (3) lies under their `MinDuration` score 1.25 × their level, because
   the ride's duration byte is the clamped value and the ratio divides by the raw one (Brain Buster, Zero G, Bumper Bugs,
   Jelly Bounce). A ride with a controller behind it (the PC ride `+0x28`, the Mac `+36`) scores 60 % of its level plus up to 40
   in the original; OpenTPW gives that base to every ride of the shipped data that has one (the water, kart and bumper-car rides, whose types are all of the shipped set −1 and −4..−14; `APPROX:QUEUE-004`, retired).
3. Limit: the count must be below the data limit, an unsigned 32-bit value.
   With `Info.HasQueue` it is
   trunc(max(QWTC × (SPEED/InitSpeed × CAP) / DUR, 4)) in single precision,
   with `Upgrades[L].QueueWaitTimeConstant` at type record +436 and InitSpeed
   at +424 (SPEED unsigned, the quotient saturating at 2³² − 1).
   `InitDuration` is +416, not +436. A NaN quotient gives 4. Without
   `Info.HasQueue` it is 100 (ORACLE-V F1 corrected the polarity).

So the maximum queue length is min(limit, 4 × cells) for HasQueue rides (Belly
Bounce: 21, six cells). Objects without a queue path (shops, toilets,
sideshows) get one front cell and so 4.
CAP and DUR are the clamped `InitCapacity`/`InitDuration` of level 0. The old
OpenTPW rule "4 × capacity" is gone.

**Positions** (§3.4). Four positions per cell from the front. The depth along
the cell is trunc(255 × 0.25 × r) = 0, 63, 127 or 191 from the edge toward the
previous cell, in bytes of 1/256 cell. The sideways byte is 114 + (rand mod 28), drawn from
the park's world generator (`System.Random` is not used). Guests walk along the queue cells to their
position (state 12) with the traced steering ([Walking](#walking)).

**Standing** (§5, state 11, once per park turn = 248 ms of the fixed clock,
`ParkCalendar.Turn`). On entry the move delay is trunc(1.2 × position) turns.
Each turn, in order:

1. Called forward → walk to the stand point (state 13).
2. The ride failed (`0xdfe34`: a toilet whose wear gauge is below 25) → leave, "feeling sick".
3. Not in the list any more → leave, no thought ("Problem with a queue", tp.exe `0x500270`).
4. The list position moved up: wait while the delay is non-zero and the gap is
   at most 2 (one turn per decrement), else walk to the new position.
5. Recorded position above the limit → leave, "queue too long". Then a coaster or track
   that refuses guests (`0x441970`, the track word) → leave, "angry" (tp.exe `0x500612`).
6. Needs window, every 30 turns after the last interlude: happiness > 80 or
   10–19 idles for 10 turns, happiness < 10 leaves, else toilet > 80 thinks
   "need toilet" and leaves unless the attraction provides relief.
7. Within 30 turns of the last interlude only (the needs window returns on every path): a 1-in-10 facing change by
   (r mod 800) − 400 of 2048 from a second draw (tp.exe `0x4ffff0`).

There is **no boredom timeout**. The original's test (`turn > +508 + 100`
within the 30-turn window) can never fire (§5.3); the code keeps it and the
tests show that guests stand for 2,000 turns without leaving. Guests leave a
queue only when admitted, when unhappy (< 10), for the toilet (> 80, not when
queueing for a toilet), beyond the queue limit, when the coaster or kart track
refuses guests (thought 13), when the ride fails, closes or is removed, or on a
queue edit; every exit but the removal costs the dismissal's medium happiness step. Happiness and toilet are compared after truncation to whole
numbers, as the original does (`fctiwz`): "> 80" means at least 81.
Every exit of state 11, like the queue edit's and the price refusal's, ends in the dismissal
(tp.exe `0x5004aa` → `0x5012f0`): after its thought the guest loses `PeepInfo.MediumHappinessChange`
(`0x4fea70` with 1); a guest whose thing id has its low three bits 0 also plays the guest sound `cat_kids` 0x80 (`0x50133d`, `GuestSimulation.Voiced`); the dismissal's guest event 6 is a record of the debug-only guest event ring (`[EXT:guest-event-ring]`).
A guest called forward to a shop or sideshow with a price tests it at the stand point (`0x4fde50`) and records its view of the price,
paid or refused, in the park's perception statistics (`0x4c74b0`: 20 rings of 50 values; categories 1–4 by the shop's special
ingredient, 6 and 7 by its balloon or costume, 9 for a sideshow; min(100, (W + 5) × 10 − price × 10) as an unsigned word). A guest
that refuses raises the ride's walk-away counts (`0x4e1670`: `mNumWalkAways`, saved and imported, and the day's count, which the day
message rolls into a ring of 30 days, `0x4dd320`). Only the advisor's conditions read the statistics and the day ring; the guest's
event records (`0x50c100`) feed only its debug dump (`0x4faa60`).

**Breakdown** (ORACLE.md F15). While `VAR_BROKEN ≠ 0` the ride dismisses its queue head first,
one guest per park turn: the ride update (`0xe0a8c`, executed in the oracle for its states 1, 2
and 4) removes the head from the queue and sends it away (`0xdcebc`, `0xee804`: reason 6, no
thought, happiness down by `PeepInfo.MediumHappinessChange`, back to wandering). A head the script
has already taken is forced aboard instead. The guests behind keep their places and wait, and a
repaired ride keeps whoever is left. While the ride is broken (state 1) the guests behind do not walk forward either (`0xed4b0` skips the move), so they keep their physical places (`0x4e0370` is true for state 1 only: behind a condemned ride's dismissed head, state 4, the guests walk on; the bridge tells the two apart by the life gauge it reads when it enters the state, `0x4e14e0`). A script that closes the ride on breakdown does not empty the queue at once: the drain takes precedence. Nobody new chooses it: the
choice's counts-test (tp.exe `0x4dd920`) refuses states 1 and 4 and a closed ride, and never reads VAR_BROKEN, so a ride whose script has
just reported a breakdown is still chosen until its ride update enters state 1. The join itself (state 10, tp.exe `0x4ffbc0`) reads neither the state nor the open word, so a guest already walking there still joins and is sent away when it reaches the head. A closed ride drains the same way: its admission (`0x4e1220`) runs only the head step `0x4e0450` while the open word is 0, one head per park turn, two on the turn a closed ride notices a breakdown (the admission's step, then SetState(1)'s). State 1 lasts
until the ride is opened again (a repair, an upgrade's end, the open button), even when the script clears VAR_BROKEN first. The ride update runs the admission once on the turn it
notices the breakdown and then enters state 1, which writes VAR_RIDECLOSED = 1 and the open word 0 (ORACLE.md F27); admission does not call the head afterwards. Queued guests no longer leave "feeling sick" for
a breakdown: state 11's exit at `0xed424` is `0xdfe34`, a relief object (toilet) whose wear gauge
is below 25. OpenTPW wears only rides, so a toilet's gauge never falls and the exit does not fire
in practice (QUEUE-008).

**Admission** (§6, once per park turn per ride). The front guest is called when
all of these hold:

- `VAR_LETMEON = 0`;
- `VAR_ONRIDE < VAR_CAPACITY` (skipped for `Bumper.WhichTrackType` 2/3);
- `VAR_RUNNING = 0` or `Info.RunsContinuously`;
- nobody is pending;
- the head stands in state 11 at recorded position 0.

The called guest walks to the stand point and writes its id to `VAR_LETMEON`
once that is 0. It stays in the list until the script has consumed it, then
boards. Waits are counted in park turns from joining to boarding.

**Gate signals.** For the M3 gate's progress check and the BOUNCE bound
W_max = ⌈Qmax/CAP⌉ × (DUR + 1 s + τ) (QUEUE-plan §9):

- `RideVisitorBridge.AdmissionChecked` (event) and `LastAdmissionCheck` give
  one `AdmissionCheck` per ride update. It holds the turn, whether the gates
  held, the head guest, whether it stood at position 0, and the called guest.
  Admission calls the head whenever the gates hold and it stands at position
  0, so "gates held, head ready, nobody called" cannot happen and is not a
  signal. The progress signals are two counters:
  - `HeadNotReadyStreak`: consecutive evaluations with the gates held, a head
    guest, and that head not yet standing at position 0
    (`AdmissionCheck.HeadNotReady`); `MaximumHeadNotReadyStreak` is its
    largest value so far.
  - `CalledAgeTurns`: park turns since the current `CalledGuest` was called
    without boarding or being withdrawn; `MaximumCalledAgeTurns` is its
    largest value so far.

  Neither has a traced bound. The gate must compare them against explicit
  bounds of its own.
- Per ride: `QueueLength`, `MaximumQueueLength` (Qmax), `QueueLimit`,
  `QueueRoom`, `QueueSizeInCells`, `QueueCells`, `JoinCell`, `CalledGuest`,
  `Parameters` (CAP, DUR, HasQueue, RunsContinuously).
- Per guest: `QueuePosition`, `QueueJoinTurn`, `GuestSimulation.QueueWaitTurns(guest)`
  (current wait), `LastQueueWaitTurns` and the `GuestSimulation.QueueWaitCompleted`
  event (wait at boarding), `GuestSimulation.ParkTurn`.

τ (boarding latency) is not derived and must stay an explicit parameter.

**Queue approximation register** (`QueueApproximations.cs`; also in
[FIDELITY-REGISTER](https://opentpw.io/docs/FIDELITY-REGISTER.html)):

| ID | OpenTPW choice | Evidence needed |
| --- | --- | --- |
| QUEUE-003 | Back-cell admission and fourth-slot path geometry are implemented. Supported live typed HasQueue entries derive the front from the entry links `+0x0C` in native priority 1,16,64,4 (`0x4DE040`); a native zero front (no cardinal bit; `0x4DE4A0` reads such a ride as unconnected, which the opening guard still answers permissively under ECON-049) leaves its demolition without a count to undo (`0x527FB4`) and its re-evaluation without a block stamp; a zero-front ride has back cell 0 and size 0 for the guests (`BackCell` null, `QueueSizeInCells` 0: they neither score, choose nor join it, and its queued guests but the called one leave at the re-evaluation, `0x4fcb84`, `0x4dd96c`, `0x4dda20`); the host keeps one cell of geometry for the called guest's stand cell and the queue tool; unsupported entries and untyped worlds keep the positive one-cell fallback. See [typed-front contract](reverse/QUEUE-003-typed-front.md). | Divergent imported mEntryPos; the native cache invalidation timing (`0x4DE130` keeps `+0x3A` until the next `0x4DE1F0`, OpenTPW recomputes on every grid change) |
| QUEUE-010 | A guest leaving a queue (`0x5004aa`, the edit's dismissal `0x501390`, a removed ride's message `0x4fb360`) stays where it stands in state 6 on every placed ride, whatever its cell is now (a path, a queue cell, the owned stand cell or a cell the queue's undo or a demolition emptied), and decides from there by the ported wander (`0x4f9490`: a start without links searches for a path cell around it). A bridge without placed cells (no owner word or exit cell: the gates, the test rigs, the Theme Park Inc slice) and the exits of a guest whose attraction is gone keep the relocation to the front cell or the nearest path | The relocation of the bridges without placed cells |
| QUEUE-012 | queue cells follow the decision order of tp.exe's validator 0x00535670 (QueueCellValidator, replayed on all 14,256 answers of the executable's own sweep; the per-cell routes of both tools ask it for a refusal on top of their own rules, with a fresh drag's latches): the flag 0x40 refuses first, an object's cell (types 4, 9 and 10) is accepted only when the owner's overwrite priority is below the tool's 3 (0x818C10, 0x535D66; Objects.GivesWay), a hoarding post and the three cells beside it are refused on owned land as well (by the queue and the path tool), a corner or junction track tile and the coaster marker 0x15 are refused, an empty cell is accepted and the validator reads no MAP terrain bit; OpenTPW's per-cell route still takes the terrain rule of the map start's cell types (Objects.Grid.CheckTerrain, with the Easymode save's occupied cells before the live map is attached), refuses any non-empty cell, so it neither lets a queue run over its own queue cell (the original: owner = the ride's cell, not two links) nor over a straight or stub track tile, keeps no latch across a drag (the line walker stops at the first refusal), and cannot lay a path or queue cell over a straight or stub track tile, which needs the map's type byte and the track layer to hold both (the type byte holds the track's type here); the placed rides' lines replace path cells in QueueSegmentReducer; not determined: the owner priority 0x818C14 the validator keeps when no class-3 thing is found, and whether the latches 0x816D50 and 0x816D58 reset between drags | The Easymode save's occupied cells stand in for their cell types in the terrain rule until the live map is attached |
| QUEUE-015 | The queue tool's undo (Back `0x52fe50`) takes its cells and refunds as `0x5367a0` does and writes no guest: the re-evaluation that follows (`0x4de1f0` at `0x52ffec`, `0x501390` per guest) dismisses by place alone, so a guest on an emptied cell stays there. The corner walk's counters (`+0x20`: 1 on a corner, 0 elsewhere, `0x5302c2`) are written by the queue edit click, whose tool takes the segments back through the native undo (the cells go from the segment's end, so a corner keeps its counter 1 - 1 and stays); a ride whose front holds no queue cell is demolished with one cell's debit (`0x527fb4`). The tool entries that run no walk here (a ride's placement, the ride window's button, a track's or coaster's completion) take the chain from the segment's first cell on without counters, and the removal from a clicked cell on is kept only as `[EXT:dev-one-click-remove]` and `[EXT:queue-backtrack]`, which keep the original's guest policy (the clear writes no guest and the re-evaluation dismisses by place) | The undo after the tool entries that run no walk |

Retired: `APPROX:QUEUE-016` (admission and state 11 once per park turn: tp.exe `0x516380` walks the live list once per turn and dispatches every thing whose byte `+3` is 0 through `0x50b360`). `APPROX:QUEUE-009` (the queue facing change: only within 30 turns of the last interlude, r mod 10 = 0, then (r₂ mod 800) − 400 of 2048; tp.exe `0x4ffff0`). `APPROX:QUEUE-017` (the choice applies the counts-test's queue room, list count < 4 × cells; tp.exe `0x4dd920`). `APPROX:QUEUE-018` (the join reads neither the ride state nor the open word, tp.exe `0x4ffbc0`; a closed ride drains one head per park turn, `0x4e1220` → `0x4e0450`). `APPROX:QUEUE-019` (state 4, condemned: the ride update reads the life gauge `+0x48` when it enters state 1 or 4, `0x4e14e0`; a queued guest's move-up holds only in state 1, `0x4e0370` at `0x500523`).

Retired (fidelity round 9, package B): `APPROX:QUEUE-014` (the queue tool's lines: mode 3 always has a start, the cell outside the entrance after a placement (`0x52a050` writes `0x81ede4`/`0x81ede8`) or the queue's back after the edit click (`0x530120`, `0x526103`), so the tool starting there is the original's and no first click without a start reaches it; the commit `0x532fc0` runs the passes `0x87` (the demolition of an object of a lower overwrite priority, now in `QueueSegmentReducer`), type, links, direction, owner, refresh; a queue over a player's path cell is laid by the native route and an initial path cell is NoModify, as in the original; the refund of the clear is `price * scrap / 100` unsigned (`0x5367a0`, `0x4e2290`)).  `APPROX:QUEUE-023` (the queue re-evaluation `0x4de1f0`: its callers are the queue tool's cell click `0x526118`, the segment commit `0x527541`, Back `0x52fe50` (`0x52ffec`, after every undo that pops a point, also when the prior corner is a path cell and the second click is suppressed), the setter's path over a queue cell (`0x5346d0` at `0x534858`), a path cell's clear beside a type-9 cell with an owner word (`0x5367a0` at `0x53694b`: the clear of a queue cell calls it nowhere) and a ride's placement, twice for a type with a queue (inside the commit at `0x529890` and at the click's tail `0x52537a` or the Move tool's `0x5259ae`); every call raises the stamp counter and stamps the back cell's block, except that a zero front (no cardinal link bit on the entrance, `0x4de040` writes 0) stamps no block (`0x4d8c60` tests the cell word first). The path tool's Back passes no ride (`0x40beb3`), so its re-evaluation takes the selected ride (`0x8186ac`: the UI selection `0x7c2654`, written by placement `0x52a02e`, the object windows through `0x5276d0`, an add-on standing for its ride `0x527831`, and cleared when the selected object is destroyed `0x4dd2d6`), which the Back command carries (`UndoLineCommand.Ride`); a ride's demolition loops Back (`0x527fc1`, `0x527fd0`), each pop re-evaluating with the shrunken room (`QueuePaths.ClearAll`), so the guests beyond it leave in place with thought 13 (ids divisible by 3) before the removal message `0x4fb360`. A placement cannot connect a queue: the entrance's outside cell is cleared and typed 3 with only the link toward the entrance, which `0x4de4a0` reports unconnected, so neither call opens the ride).

Not modelled: ride
state `+408` (beyond the states 1 and 4 the bridge keeps), the price check at the stand
point (guests pay when boarding, see Money) and save/load of queues.

## Rendering

`GuestRenderer` builds one dynamic vertex buffer per frame: a camera-facing quad
per visible guest (both windings), feet on the bilinear heightfield corner
heights (also over heightfield holes such as the entrance road), one draw call
with up to eight kid atlases (`GuestSpriteAtlas`, 1024-wide RGBA, built on the
CPU from `.TPC`). Direction comes from the guest's heading relative to the
camera; the walk frame advances 20 frames per cell walked (10 running). Sprite world size
is read from the original: one pixel is 20 / 128 x sqrt(2) = 0.2210 world units (ten a cell), 0.0442 engine units (`SpriteScale`: the quad placement 0x5890E0 adds 20 sprite extent units a sprite unit
of 128 pixels in clip space, and the projection builder 0x578BE0 runs a 90 degree field of view with the clip x and y scaled by cos 45 degrees); a kid of 43 pixels stands 0.95 cell.
A kid's set is the original's own draw at its creation, the world generator reseeded with its thing id and the first draw shifted right by 2 modulo the 8 sets (`GuestAppearance.KidSet`), not its peep type;
a walking kid steps its 8-frame walk 20 times a cell (0x4FA2A0: the interval is 62 ms over twice the speed).
Not drawn: shadows, riders' bodies on a ride (their heads are drawn, see below).

### Kept bodies

A guest an object is serving (state 16) stays in view when the object's `UsageInfo.RideHandlesSprite` is 1, whatever
the object is. tp.exe's state-16 entry tests the thing flag `+0x32` bit `0x20`, which the object constructor sets from
that key (`0x4DB41C`): clear deletes the guest's body (`0x4D4170` at `0x502147`), set keeps it, queues the stand
template and sets `+0x28` = 1 (`0x50212B`..`0x50213D`). The test reads no object kind. The guest update places the
body at the guest's position only while `+0x28` is 0 (`0x4FA087`); `+0x28` is `mSpriteUnderRideCtrl`, which the save
writes and reads (`0x4F8B10`), the state-15 entry clears (`0x501F79`) and the removed object's message 10 clears
(`0x4FB3DF`); no other guest code reads it. The sprite pass (`0x542010`) draws every sprite that has a frame at its
stored position, whoever owns it, so the body stays drawn where it was last placed until the state-15 entry places it
again.

Which objects keep the body (the inherited key: `Shops.sam`, `Features.sam` and `Rides.sam` say 0, the `SideShow.sam`
of fantasy, hallow and space say 1, jungle's 0):

- **Shops**: in the jungle the Burger, Fries, Drinks, Ice Cream, Steak, Gift and Balloon shops; the Costume Shop keeps 0.
- **Sideshows**: every fantasy, hallow and space sideshow but the fantasy and space Arcades and the Fortune Teller (which set 0);
  in the jungle Laughing Hyenas, Jungle Spray, Giant Puzzle and Strength Bird.
- **Toilets**: the fantasy Small and Super Toilets, the jungle and space Small Toilets.
- **Rides**: the bounce rides and every ride whose script walks its riders (all of them run `WALKON` or `BOUNCE`).

What the scripts do with the kept body (decoded from the shipped `.RSE`): nothing but the walk table. `WALKON`,
`WALKOFF` and `WALKGET` walk it between the model's nodes and pose it on board ([TOURS.md](TOURS.md#the-walking-body-tour-011)):
the sideshows Laughing Hyenas, Jungle Spray, Aqua Spray and Martian Mooners (cheer, action 6), Strength Bird, Fruit
Shy and Strength Skull (the short gesture, action 5), and the hallow Arcade, the Super Toilet and the Steak, Gift,
Balloon and Cauldron shops (action 1, which deletes the body on board, `0x4F9ED0`, until the walk-off draws it again).
The rest of the scripts read and write the guest only through `VAR_LETMEON`, `VAR_PEEPID` and `VAR_LETMEOFF`
(`TEST`/`COPY`; `HUSH`/`HOP` and the limbo table appear only in the walking scripts), which are script state, and use `EVENT`, `ADDOBJ` and `TRIGANIM` on the object's own effects, sounds
and animations: the Small Toilets, the Giant Puzzles, the Strength Flower, the Ufo Blaster and the remaining shops leave
the body standing where it stood, at the stand point it walked to. None of these objects sets `UsageInfo.RequiresTeleport`.

OpenTPW draws such a guest (`GuestSimulation.ShowsKeptBody`) from the ride poses when the walk table or a bounce
table holds it, and once its entry is gone (`WALKGET`, `UNBOUNCE`, a node the model lacks) where the walk or the bounce
left it, with the frames its body plays (`FlatRideBounceRiders.HoldKeptBodies`); otherwise standing where it stood,
in the stand pose, by the kids or costume renderer of its kept body's appearance. A body the script deleted is not
drawn. Its balloon, thought bubble, pick and portrait stay off, as for any rider; an umbrella or warm items it holds
stay with a body that stands in place (not with one a ride pose draws). A pools changing room still hides the guest
(`[EXT:pools-swim]`). OpenTPW's own winter kiosk, glühwein stall and brolly shop set the key to 1 as well, so their
customers stand at the counter too; the kiosk (capacity 5) and the brolly shop (capacity 10) can serve several at
once, and those stand on the same stand point. Their `.sam` comments still say the script hides the customer: the
shipped archives are pinned by the asset manifest of every park save that placed one, so the comment waits for a
content change that has to touch them anyway. Before October 19, 2026 every served guest was hidden, so guests at the
Burger Shop seemed to vanish into the hut, and the toilets' and the puzzles' users vanished too.

### Leaving an object (ExitRide)

A script lets a guest off by writing it to `VAR_LETMEOFF`. tp.exe reads that variable only in the ride update of a park
turn (the dismiss `0x4E1410`): ride state 0 runs it after the admission (`0x4E1220`) and the breakdown test, unless that
test sets state 1 or 4 (`0x4E159A`); states 1, 2 and 4 run it after the head step (`0x4E0E27`). So a use lasts the
script's time plus up to one park turn (248 ms): the Burger Shop's `WAIT 1000` serves a customer 1.0 to 1.25 s. The
dismiss runs ExitRide (`0x5014E0`) and writes 0 to the variable only when ExitRide succeeds.

ExitRide:

1. Puts the guest at `UsageInfo.ExitCellAppearPos` in the object's exit cell (`+0x38`; for an object without an exit
   it is the entrance cell `+0x36`). The point `0x4DEDF0(1)` takes the bytes `ftol(255 x f)` after the object's
   rotation, as the stand point does: 90 gives (y, 1 - x), 180 gives (1 - x, 1 - y), 270 gives (1 - y, x). The
   teleport `0x4FA930` sets the position, zeroes the velocity and moves the guest to the cell's list.
2. Takes the exit cell's link byte `+0x0D` (`0x522850`), reversed by `0x4D8C00` when the exit cell is the entrance
   cell, and the neighbour along it (`0x4D97E0`).
3. Calls the exit blocked when that neighbour is a queue cell (types 3 and 9, `0x536320`) or no route reaches it
   (`0x4FA530`). It logs "SetDest on ride exit failed, exit of object %d is blocked!" and closes the ride
   (`0x4DF150`): the open word, the pending visitor and `VAR_RIDECLOSED` = 1, as a breakdown's SetState does. The
   guest stays on the ride (state 16) at the exit point, its kept body where it was, and `VAR_LETMEOFF` keeps it,
   so every later ride update tries again.
4. Otherwise runs the use routine (`0x4FD970`) and enters state 15.

State 15 (`0x500900`) walks the guest to that neighbouring cell. A stuck walk clears its target and sends it to state 6;
on arrival the guest decides again (state 6, "successfully left ride"), unless it has a major destination (`+0x1de`, set aside by a
minor decision): that attraction becomes the target again and the guest walks to its back cell in state 10. The target `+0x1dc` is not
written by the exit and lives until the next major choice (`0x4fcb21`), the leaving state's entry (`0x502305`) or a dismissal; GUEST-004 retired. `GuestSimulation.ExitRide`,
`RideVisitorBridge.DismissLetOff` and `CloseForBlockedExit` port this.

Some cell maps hold no exit cell with the link a placement writes:
- the M3 and M4 gates, the measurement scenarios, test rigs and the developer prototype lay no access cells;
- the Theme Park Inc slice and online visits keep no access-cell links.

An attraction on such a map lets off in the cell it takes its guests in and routes to its `ExitCell`. Only a Theme
Park World level types the footprints, so only there does ExitRide use the object's own exit cell
(`Level.RegisterWithGuests`, `ownExitCell`).

A guest the ride lets go of without a let-off leaves by ExitRide when it can. That happens when its script halted or
faulted, or for a save's leftover rider. When ExitRide fails, the guest is set down beyond the exit, as a guest on a
removed ride is (`QUEUE-010`).

A demolished object's riders get neither ExitRide nor the use routine: tp.exe's removal message (`0x4FB360`) gives
them their model back, the small happiness step and state 6 (`GuestSimulation.Unregister`). The pools changing room's
swimmers keep their own exit (`[EXT:pools-swim]`).

`ShopVisitAssetTests` runs the shipped Burger Shop in all four rotations: every customer is let off 1.0 to 1.3 s after
the shop takes it, walks out of the hut, and no guest is lost. `ExitRideTests` covers the exit point, the reversal,
the blocked exit and the state-15 walk.

**Depth.** The quad is camera-facing, so all of it lies at the depth of the hotspot, and seen from above the ground just in front of the
feet is nearer the camera than the hotspot: it hides whatever hangs below the hotspot. In the `.TPC` the soles of a standing kid end 3 to 5
pixels below it (up to 8 in a stride). The original pulls such sprites towards the camera once their quad is placed: in its sprite pass
(0x589B50) a sprite flagged 0x200 gets the clip z of its four vertices moved to z - 0.2 / (z + 1), x, y and w kept, unless engine flag
0x2000 is set, which only the front end's 3D scenes do (0x5D9680). 0x200 is the pick flag that the pick of 0x5890E0 tests, the way the
original selects visitors and staff, so it is their sprites that are pulled; the shadow copies clear it (0x475CF0, 0x475E10), and the staff
tools' dangling person keeps the flags its creation clears (0x4758F0). Under the projection 0x578BE0 (clip z = a (v - n), w = s v, with
s = sin 45 degrees, a = s / (1 - n / f), near 0.1 and far 1000) the pulled depth is that of the view depth v / (1 + 0.2 / (n a (a (v - n) + 1))),
3.5 to 3.9 world units nearer at the park view's depths of 40 to 250. OpenTPW moves the hotspot along its view ray by that share and shrinks
the quad by it (`SpriteScale.PulledDepthShare`, `GuestRenderer.PullTowardsEye`), so it covers the same pixels nearer the camera: the kids,
the costumes and the staff members, not the dangling person, balloons, thoughts, litter or riders' heads. At the lowest camera pitch, 45 degrees,
that keeps soles up to 16 pixels below the hotspot clear of flat ground. Before October 18, 2026 OpenTPW drew them at the hotspot's
depth, which cut the soles of the original kids and hid the shoes of the pools' 45 degree kids altogether.

**Costumes.** A guest in costume is drawn by a second `GuestRenderer` with the theme's costume set (`esprites.wad/<theme>/Costumes`: Jungle
`SPR_TI`, Fantasy `SPR_BK`, Hallow `SPR_HK`, Space `SPR_SK`; one set per theme, so the original's draw among them, `0x541f70(2)`, always
picks it). The sets share the kids' animation slots. On a ride, its head comes from the theme's costume heads (Jungle and Space; the other
themes ship none, and the rider keeps its kid head).

**Balloons.** `BalloonRenderer` draws each held balloon from `Generic/Balloons/SPR_BL`: four colours (red, green, blue, yellow), each a whole
and a burst frame, the colour drawn anew whenever the balloon appears (after a purchase or a ride; `0x541fd0`). The placement follows
`0x4fa030`: the balloon hangs at the guest's position 0.35 of a park turn behind (`0x4f9f00`), one unit up, lowered by twice the distance
between that point and the guest in world units (ten per cell), and bobbing through a 20-entry table (0 to 0.2, scaled by 1.5). A
balloon that bursts (its life ends, a prank, a guard's catch) shows its burst frame for a moment; one put away for a ride just goes.
The colour is the guest's own draw (the second draw after the reseed with its thing id, 0x541FD0, the same whenever the balloon appears, a shop's gift or a give-back), the bob phase steps on every eleventh balloon placement and every balloon is placed on every rendered frame (0x4FA030 from 0x518F90), and a burst fades the sprite from alpha 250 by 20 a step for 13 steps of 62 ms (script 0x74F4C0, register 7 the alpha byte, drawn through the sprite shader's alpha). GUEST-017 retains original-save script phase (SAVE-013), the fixed-60Hz/non-pooled host scheduling boundary and placement fidelity. Live balloon players now use explicit creation/burst/removal and the shared integer sprite pass, not render-time transition detection; see [the timing contract](reverse/BALLOON-TIMING.md).

**Litter.** `LitterRenderer` draws each item the drop creates (0x4D93B0; the only creators are the guest's litter step and the vomit step) as one flat quad of `Generic/Litter/SPR_LI` on the ground (script flag 0x80000: the quad's y extents go to the map's y axis, 0x589320), at the cell corner plus the 1-9 tenths the cell id's two draws give, half a unit above the ground height, the picture upright with the map's rows running down. The slot is the kind: 1-5 slots 0-4, 6 and any other slot 5, vomit slot 6, stinkbomb slot 7. The width takes the display's height over width (0x582EB0, 0.75 for 4:3, which is what `SpriteScale` uses). The sweep deletes the sprite (0x4D9620); litter restored from a save has none, as the original's cell load makes none.

**Upgrade balloon.** An installed upgrade (0x4DF8F0) deletes the ride's old balloon and, when the new level is 1 or 2, hangs one of `Generic/SpecialFX/SPR_SF` (a blue balloon with a yellow up arrow, its hotspot 85 pixels below its top) over the middle of the ride's front cell; the ride's removal (0x4DD0A0) takes it. `UpgradeBalloonRenderer` draws it from `ParkEventKind.UpgradeCompleted`. The developer option
`--debug-guest-looks` gives every second guest a balloon and every third a costume as they appear, for captures.

## Riders' heads

A ride whose script runs `ADDHEAD` shows each rider as a head hung on the model.
Traced from the Feral Mac binary (`SimThemePark`, PowerPC):

- `ADDHEAD` is `0x100B0E9C` and `DELHEAD` `0x100B0F90`, read from the RSE opcode table (`0x2F58` → `0x3F2A4`, 106 entries, [RSE-VM.md](RSE-VM.md)). `ADDHEAD` takes the script's head table (script +48, null: nothing) of length +76, draws a random slot until one holds 0 (none free: nothing), stores the visitor and hangs the head of head id slot + 1. `DELHEAD` zeroes every slot holding the visitor and takes its head down. [BIN:STP-PPC:0x100B0E9C] [BIN:STP-PPC:0x100B0F90]
- A head id is the value of an attribute record of the model whose type has flag `0x80`: `0x1019B298(model, mask, id)` returns the index of the FIRST record with that value and flag, or -1; `0x1019B56C` hangs the head on that record (record flag `0x2`, a sprite handle). [BIN:STP-PPC:0x1019B298] [BIN:STP-PPC:0x1019B56C]
- **Record to node:** attribute record `i` belongs to node `header+0x46 + i` (`Unknown46 + i`, the rule the particle key resolver uses too); below the mesh count (`+0x44`) it is a mesh record, else a dummy record. Every shipped head record has type `0xb1`/`0xf1`, so it has flag `0x30` and a world matrix. The node name is irrelevant (the old `headNN`/`HeadN`/`Kid_NN`/`spriteposition NN` name rule is gone). Known data: `monkey.MD2` id 3 is node 10 `Head02` and id 5 `Head06`; `Bird.MD2` id 9 is node 2 `Head03`. [BIN:STP-PPC:0x1019AE64] [BIN:STP-PPC:0x1019B7CC]
- **Per-script table:** the script init `0x100B2BA0` counts head ids from 1 until the lookup returns -1, allocates 4 bytes per id at script `+0x30` and stores the count at `+0x4C`. The init also runs for `SPAWNCHILD` (63) and `SPAWNSOUND` (64), so every script (root, child, sound) has its own table. [BIN:STP-PPC:0x100B2BA0] [BIN:STP-PPC:0x100B32D4]
- **Slot draw:** `ADDHEAD` repeats `labs(worldRand() >> 1) % length` until a slot is free; the world generator `0x10105328` is `s * 1664525 + 1013904223` with `labs`. [BIN:STP-PPC:0x100B0E9C] [BIN:STP-PPC:0x10105328]
- The table of a car type is sized by the same records: `0x100303A8` counts the flagged records (`0x1019B354`), prints "Car type %s can receive %d heads" and fills its node table (`0x1019B300`). [BIN:STP-PPC:0x100303A8] [BIN:STP-PPC:0x1019B354]
- **Drawing** (`0x1019B6F8`): the head is hidden when its node has flag `0x8000` (frame -1); with node flag `0x400` rows 0 and 2 of the matrix are negated; the position is exactly the node's world translation (no offset). The direction is the camera expressed in the node's frame (general inverse, times the camera matrix), normalised and binned to the nearest of 7 elevation rows (30 degree steps from the local +Y pole) by 8 azimuths (45 degree steps); frame = 8 * row + column, the column forced to 0 at rows 0 and 6. The roll is `atan2(-m10, m11)` of the remaining rotation / 2 pi, so the sprite rotates and heads tilt with the seat or car. The scale property is 0.685 (default 1.0) and there is no animation. The art is `kidsheads`, or `costumeheads` when the visitor's `+0x24` is 2 (set index `+0x20`) [BIN:STP-PPC:0x100E8F70]. [BIN:STP-PPC:0x1019B6F8]
- The head art is `esprites/Generic/Kidsheads` (the kid set's string `kidsheads` is loaded at `0x10069A70`). Each set's ESP slot 0 has 7 directions of 8 frames, direction-major (frame = direction * 8 + step; 56 frames, one set stores 55 because row 6 column 7 is never selected), so the original's 7 x 8 binning is exactly direction = elevation row and step = azimuth column. [BIN:STP-PPC:0x10069A70]

Corpus facts (`RideHeadsCorpusTests`): all 308 associated scripts in the four-theme catalog are inspected (262 roots and 46 child/sound scripts); only the roots contain head commands. 31 scripts run `ADDHEAD`/`DELHEAD` in four themes (fantasy 8, hallow 9, jungle 6, space 8).
Jungle head runs: Sun God 32, Crazy Ape 16, Mumbo 5, Rocky Racers 8, Tom Tom Twister 40, Eruption 16; the ferry 1, Inca Totem 13 and
Aztec Mayhem 27 carry runs without `ADDHEAD`. Tubes Of Zob runs `ADDHEAD` but its model has no head id 1, so its table is null.
Coaster and tour car models with head records: `CrocCar` 6, `ape` 2 (coaster3), `cart` 6 (minecart), `Bird` 9 and `Balloon` 9 (tourride). Every head record in the ride and car models has flag `0x30` and a node inside its model.

OpenTPW (`RideHeadSpace`, `HeadPresentation`, `RideHeadRenderer`): each script keeps its own head table over the model's records (the root's on the ride, child and sound scripts' on their first head opcode); `ADDHEAD` and `DELHEAD` update it through `RideVisitorBridge`. `RideHeadRenderer` draws from a list of head sources (visitor, node world matrix, node flags); the flat-ride source yields every table's hung heads. A rider is drawn only when its state is Using, its attraction id matches that ride and its head is up, as a kid head sprite (the rider's kid set, `GuestSpriteAtlas.LoadKidHeads`, TPC banks) at the node's world translation. `HeadPresentation` (GPU-free, unit-tested) computes the frame, roll and scale from the node matrix (in MD2 axes) and the camera; the quad is rotated by the roll. The rider's body is not drawn on the ride. Normal guest release removes its old slots; forced teardown clears every slot, including orphaned ids. This is host-owned presentation cleanup, not a traced original destructor rule. The coaster seats are a separate follow-up: a coaster head source can be added to the renderer's list.

The original-level smoke test requires submitted head quads and separately compares visible/hidden GPU captures on one identified, naturally boarded animating ride. Simulation, animation, camera and shader time are frozen; repeated visible frames and repeated hidden frames must each be byte-identical, and toggling only that attraction's head renderer must change pixels. This establishes visible OpenTPW head geometry, not original visual equivalence.

Approximations ([HEADS register](https://opentpw.io/docs/FIDELITY-REGISTER.html)): the size and depth are read (a head is a camera-facing quad of scale 0.685 at the node, 0.2210 x 0.685 world units a pixel, no depth offset; the PC routine sets the sprite's frame register to 8 x row + column, register 9 to the roll in turns and register 8 to the scale 0.685, tp.exe 0x44BA04..0x44BA34); the slot draw is the original's, `labs(worldRand() >>> 1) % length` (tp.exe `0x554CB5`, repeated until a free slot), on the one shared world generator `0x516330` that the park's guests, staff and scripts draw from, so HEADS-001 is retired (the record-to-node link, the per-script allocation and the table-length rule, formerly HEADS-002/003, are traced and retired too). The remaining head presentation uncertainty (HEADS-004): how sprite scale 1.0 maps to world units (the body's pixel scale times 0.685 is used), depth handling, numerical and rendered qualification of the roll (the traced camera-to-node / selected-frame composition is now used; native sine-table and x87 behavior are not claimed bit-exact), and costume heads (guests wear none). The frame choice is read (October 16, 2026): `0x44B510` takes the (row, column) of the 7 by 8 table whose direction has the largest dot product with the camera direction in the node's frame (first on a tie; rows 0 and 6 use column 0), where row r is r times 30 degrees from +Y towards +Z and column 0 faces local +Z, each column turning 45 degrees about Y from +Z towards -X. A child or sound script has no head table at all on the PC (it is created with thing id 0, which skips the table allocation, and copies the parent's thing id only afterwards; ADDHEAD and DELHEAD exit on the NULL table, tp.exe `0x558D2B`, `0x554C89`, `0x554D6F`), and OpenTPW does the same: their head opcodes do nothing (HEADS-005 retired, October 14, 2026); no associated child/sound script in this shipped corpus uses a head command.

The roll composition is traced separately in [Head reference basis](reverse/HEAD-REFERENCE-BASIS.md): the camera orientation is required, not just its position. `RideHeadRenderer` supplies the MD2 right/up/front basis derived from the same view used by its billboard. Synthetic matrix regressions cover all frame rows/columns, camera bank, node transforms and mirroring; they are not original rendered-image evidence. HEADS-004 remains active.


Riders of coaster, tour and track cars hang from their car's head dummies through the same renderer: a coaster rider from the socket of its index in the car's seated list (`CoasterHeadSources`, [COASTERS.md](COASTERS.md)), a tour rider from a head node of the flying car (`TourHeadSources`, [TOURS.md](TOURS.md)), and rider k of a go-kart, a water ride's boat or a bumper car from the vehicle mesh's head dummy k (`TrackHeadSources`, tp.exe `0x00549C60` called by the vehicle creation `0x00549DB0`: the karts carry heads 1 and 2, the `wr_ring` boats 1 to 5; [TRACKS.md](TRACKS.md)). The boats' riders are drawn since October 18, 2026. Rides that run no `ADDHEAD` can still show their riders' heads through WALKON's head action 4 (The Firepit, Inca Totem, Aztec Mayhem, Hover Bot Havoc, Rock 'n Roll): the rider's head hangs on head id u2 (the second node operand, a rider counter in these scripts) and WALKOFF takes it down (tp.exe `0x00557D80` walk state 1 to 2 calls the hang `0x0044B410`; `0x005571A0` calls `0x0044B4C0`). `RideVisitorBridge` hangs it through `RideHeadSpace.Hang` at once, without the original's per-frame walk update ([APPROX:TOUR-011]); a rider on a ride that walks bodies (WALKON) is drawn walking on and off and, for the actions 3, 5 and 6, on board ([TOURS.md](TOURS.md), "The walking body"); the walk slot keeps the action so the head comes down at WALKOFF and a park save keeps both.
Other root scripts without `ADDHEAD` and without that action do not enable this renderer; this does not establish that the original cannot show heads through vehicle/controller paths. Native head atlas reload and repeated park entry/exit checks cover Asset.All removal and scheduled GPU texture/material/shader disposal.

### Bouncing riders

The bounce rides (Bouncy in the jungle and in space, Brain Buster, Jelly Bounce) show no heads: their riders are bodies. tp.exe's per-frame script pass `0x557AB0` draws the
guest in each occupied BOUNCE slot with its own kid sprite, in the run animation, on the slot's node (the node base BOUNCESETNODE set plus the slot index, class `0x800`),
and bounces it up from the ride height plus the BOUNCESETBASE height along a half sine hump once per second of the ride's speed-scaled time, never below the node; the sprite
turns one heading step (45 degrees) every half second. While the ride is broken the riders stand on their nodes, each facing the visitor id modulo 7. Details, constants and
the opcodes in [RSE-VM.md](RSE-VM.md), "Bounce opcodes".

OpenTPW (`BounceRiders`, `FlatRideBounceRiders`, `GuestRenderer.BounceRiders`): a guest in the Using state that its ride's bounce table holds is drawn by the kid renderer
(or the costume renderer for a guest in costume) at that pose, through the same direction rule as any guest (`SelectOriginalDirection` with the pose's heading byte). Riders of
other rides stay hidden as before. Approximation: HEADS-007 (the run animation's frame interval is not decoded; 100 ms is used). A rider's node is the one BOUNCE stored in its slot
(`0x555742`: the script's `+0x70` plus the slot index, kept in the entry), so a later BOUNCESETNODE moves only the riders that bounce afterwards; HEADS-008 is retired. Jelly Bounce's slots 7 to 9 sit on nodes its model lacks, so, as in the original, those riders are not drawn.

## Tests and evidence

- `QueueTests` (no assets): queue-cell links and recompute after edits, build
  refusals, both limit formulas (single precision, NaN floor, unsigned saturation, 100 without HasQueue),
  join order room → excitement → limit (`QueueJoinTests`: the live excitement, the level-byte gate, the
  relief predicate, a broken ride's head-first dismissal), guests walking queue cells to four
  positions per cell, depth bytes, move-up delays trunc(1.2 × position) with the
  gap ≤ 2 rule, no boredom exit over 2,000 turns, the unhappy/toilet/relief,
  ride-failure and queue-edit exits, the admission gates (LETMEON, ONRIDE <
  CAPACITY, RUNNING/RunsContinuously, capacity bypass, head at position 0,
  one per turn), the stay-queued-until-consumed handshake, and first-come
  first-served boarding with waits in park turns.
- `GuestTests` (no assets): sprite RLE/palette/bounds, ESP slots, direction
  mirroring, flow fields and connection bits, shop-protocol queue order
  (call → board → release), queue room and closing, LIMBO timing and header
  limbo size, admission and turning back, ride choice + PerfectRide/OKRide
  happiness, needs growth and a thirst shop, leaving through the lane, same seed →
  same hash (different seed → different), 600 guests with six scripted shops:
  ~0.06 ms per 60 Hz tick on an M-series Mac.
- `GuestAssetTests`: 8 kid sets × 175 frames, slot coverage, flags; jungle
  `Standard.sam` settings; 78-cell jungle network from EntranceA; real guests on
  the original `Totem.RSE` (the first run starts with a full load, all riders released); the
  original Belly Bounce with a 4-cell queue path beside the Easymode paths
  (its .sam queue parameters, guests standing only on queue cells and on every
  one of them, the queue filling to 16, 30 boarded in 180 s, no stalled
  admission); deterministic 120 s jungle run.
- Native smoke (`--load-original-level jungle --smoke-test`, also fantasy, space,
  hallow): guests seeded near the Totem board it (`VAR_ONRIDE` > 0 at motion
  start; it was 6 before the traced queue. In a jungle run with it the Totem had
  boarded 3 by the guest capture: its one-cell path queue holds 4 and admission
  waits for each guest to walk up and be taken), a rider is released through `VAR_LETMEOFF`,
  arrivals pay admission, and GPU readback with vs. without guest sprites
  differs; captures `native-smoke-original-guests.png` and `-guests-hidden.png`.

## Open

Original arrival/need/decision formulas and units; staff interaction, litter,
vomit puddles, prankster/stinkbomb behaviour; entry-fee opinion; shops and
toilets (need the rides/objects slice to expose catalog attractions through
`IRideVisitorBridge`); drawing built queue cells; seat attachment of riders;
costumes, thought bubbles, balloons; save/load of guest state;
which `PeepTypes` entry uses which kid set.

### Catalogue upgrade defaults

Ordinary scripted Theme Park World rides now resolve the exact SAM `Upgrades[L]` row of their mapped economy instance. Fresh/imported construction seeds that tier before execution. Completion updates catalogue queue metadata and fallback capacity, and writes the ride words of tp.exe's completion (0x4DF8F0): SPEED from the level's `InitSpeed`, `VAR_CAPACITY` through the capacity setter 0x4DD7F0 (the low byte clamped to `UsageInfo.MinCapacity..MaxCapacity` when the two sum above 0) and `VAR_DURATION` clamped to `UsageInfo.MinDuration..MaxDuration`, each only when the level's value is above 0, so a word the level leaves at 0 keeps the ride's setting. The routine's common entry also sets the state of repair to 100.0 for an upgrade, and its reopen tail opens the ride except a coaster without a complete circuit (`RideRepair.OpensAfterRepair`); it writes `VAR_WORN` = 0 too. Flags, waits, rider/queue lists and head slots stay intact. `InitSpeed` of the tier a ride is created at (or seeded to before execution) is also its script speed bias (`RideVM.ScriptSpeed` = 0.5 + bias / 100, RIDES-033); a live completion sets the bias to the new level's `InitSpeed` when above 0 (tp.exe 0x4DF8F0, RIDES-033 round 2) and leaves the animations and controller coefficients alone. 

Whole-park resume already saves the economy's `Level`; staged binding derives the tier before runtime/script-world/guest restoration. Saved VM values overwrite fresh seeds afterward. No new schema or write-provenance state is added. Exact instance/object identity, factory parameter-record ownership, sparse/missing rows, duplicate completions, stopped/removed owners and no-economy/read-only/TPI boundaries are checked. Nonzero imported/resumed tiers that cannot resolve a row fail reconstruction rather than use tier0. A legacy tier0 entry without its row reports an explicit refusal and keeps prior construction semantics. Coasters and developer prototypes keep their existing controllers and receive no catalogue-tier mutation. A custom public queue record cannot be overwritten, even when its values equal the factory record.

The entry-only queue factory keeps its level0 compatibility behavior. Tier-specific resolution uses exact row labels and existing raw seed/queue clamp semantics; no guessed duration units, sliders, refunds, physical seats are introduced by tier metadata binding. The separately qualified host purchase confirmation is documented in [ECONOMY.md](ECONOMY.md). Retained live operating values may differ from new catalogue defaults. A broader VM-to-queue projection or native upgrade reset needs a separate contract and evidence.

Catalogue-default snapshot eligibility is checked before capture or file writing. Ordinary bound scripted TPW rides must retain factory-owned queue metadata and match their completed economy tier. Custom or refused tier metadata is not repaired during saving. Matching stopped owners and live CAP values different from the fallback remain supported; legacy unbound tier0 defaults, including catalogues without row0, retain compatibility. No new save fields record write provenance.

Independent synthetic whole-park controls exercise the actual runtime/script-world/guest restore stage after economy-tier binding. Live CAP1 with fallback3 and script-authored CAP9/DUR77 survive that stage. Matching stopped rides preserve catalogue metadata and teardown; the existing stopped-runtime format omits their VM slots because their scripts are absent from the saved world, so those slots retain reconstructed defaults rather than the former stopped values. This does not widen the save schema or native upgrade qualification.

The All Attractions shop/sideshow business rows display the counters already maintained by guest payments and the economy month boundary. Actual synthetic HUD controls cover positive shop and negative sideshow profit, different attraction/economy IDs, same-type instance isolation, current versus previous month, existing save restoration, copied frame values, category/removal and unsupported binding transitions. Ride monthly users, remaining life and satisfaction stay unavailable in this slice. These controls qualify host projection, not original accounting or native UI behavior.

## Closed or split in the guests, queues and arrivals pass

- `APPROX:WALK-006`, retired (wave 5): `[EXT:legacy-save-access-links]` (`ObjectAccessCells.RepairMissingLinks`, user-approved 2026-10-17) lays at load, through the placement's link writers, the link byte `+0x0C` that the entrance and exit cells of a park resumed from an OpenTPW save made before the saved byte was taken lack. The original's saves always hold the byte (cell serializer `0x4D0B30`; placement `0x5297BD`, `0x52989A`, `0x529A58`; edge test `0x4D8750`), so it needs no repair; a cell that holds any bit is left alone.
- `[EXT:legacy-save-queue-cells]` (`ObjectAccessCells.RestoreLegacySavedQueue`, `Level.RestoreLegacySavedQueue`, user-approved 2026-10-20, "Herstel bij laden (EXT)"): a park resumed from an OpenTPW save made before the import took the shipped save's queue cells (#722) holds empty ground where an imported ride's queue lies (the Jungle Belly Bounce's (52..49, 22)), so its typed front (`0x4DE040`) is a cell no guest can join. The load lays those type-3 cells again from the level's shipped save (type, link byte, queue link, owner word, NoModify, placement counter), only where the resumed cell is still empty ground and the object's entrance links into it, and only for an object the shipped save placed at the same anchor. A newer save holds the cells, and a queue the player removed leaves the entrance unlinked, so neither is touched. A queue restored only in part keeps no link toward a neighbour that does not link back. Limit: a save older than `[EXT:legacy-save-access-links]` (2026-10-17) is not repaired, because its entrance holds no link byte, which is also what a newer save whose queue the player removed holds; the load does not guess between them. tp.exe's saves always hold the cells (`0x4D0B30`), so the original needs no repair.
- `APPROX:QUEUE-004`, retired: every shipped ride's controller type is covered (`BumperTypeCoverageTests`).
- `APPROX:QUEUE-013`, retired: a queue has no cell cap but the back-of-queue walk's 1,000-cell bound (`0x4de130`).
- `APPROX:GUEST-018` and GUEST-019 retired: the minor decision runs every twelfth update of state 10, lists the rides on the cells they stand on (`+5`, `+7`), and sends the guest to the best's access cell `+0x36`, keeping the old target as the major destination; GUEST-005 retired: a bin's access cell is its entrance cell (`+0x36`, written only by the constructor `0x4db33c`; the Jungle save's entry and exit words of all 11 placed objects equal the shape's cells).
- `APPROX:QUEUE-020`, closed as `[EXT:bounce-single-clock-read]` (a stepped clock has no two live reads).
- `APPROX:QUEUE-022`, closed as `[EXT:synthetic-ride-zero-guard]` (only synthetic objects lack operating values).
- `APPROX:ARRIVAL-002`, closed as `[EXT:online-visit-settings]` (the online visit is a read-only extension).
- GUEST-006 keeps its original-behaviour remainder; `[EXT:online-bubbles-on]` covers the bubbles in a visit.
- GUEST-017 keeps the native import/host-clock/placement remainder (see [timing contract](reverse/BALLOON-TIMING.md)); `[EXT:empty-costume-heads-keep-kid-head]` avoids the original's division by an empty group. The costume return needs no change: the reseed with the thing id makes the kid set repeat.
- Round 7 (sprites in the game): the sprite pixel size, the kid sets, the balloon colour, bob and fade, the staff scripts and their sprite clock, the entertainers' program, the staff variant, litter and the upgrade balloon are read and drawn (GUEST-014 to 017 narrowed, HEADS-004 narrowed). The balloon give-back after a ride reseeds the world generator and takes two draws (0x502053).
- PATH-007 keeps the land rules; `[EXT:no-land-maps]` covers maps without a fence setup, Theme Park Inc and visits.
- GUEST-014 is split: GUEST-020 holds the flagged-folder bank limits. PATH-013 is retired: PATH-017 holds the P window's layer. TRACK-032 is new: the saved-ride controller.
