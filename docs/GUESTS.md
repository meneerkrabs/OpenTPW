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

## Original data found

### Guest graphics: sprites, not models

Theme Park World draws visitors as pre-rendered **sprites**, not MD2 models: no
WAD contains a kid/peep/visitor model (all 312 WADs listed). The sprites live in
`Data/esprites.wad` (121 members, all decoded by `SpriteBankFile`/`SpriteAnimationFile`):

| Folder | Members | Content |
| --- | --- | --- |
| `Generic/Kids` | `SPR_{BE,BI,CH,FR,KI,SA,SU,TA}.{ESP,FPC,TPC}` | 8 kid sets, 175 frames each |
| `Generic/Kidsheads` | `SPR_xx.{ESP,TPC}` | heads (8 frames × 7 directions); not used yet |
| `Generic/Thoughts` | `SPR_TB` (16), `SPR_TC` (10) | thought-bubble icons; not used yet |
| `Generic/{Balloons,Litter,Particles,SpecialFX}` | `.ESP/.TPC` | balloons, litter, particles |
| `Generic/{Guards,Handymen,Mechanics,Researchers}`, `<Theme>/{Entertainers,Costumes,Costumeheads}` | | staff and entertainer sprites |

**`.FPC` / `.TPC` sprite bank** (little-endian): `u16 3, u16 3, u32 frameCount,
u32 0`, then 255 palette entries of 4 bytes **B, G, R, A** (alpha varies: anti-
aliased edges), then per frame: `u32 dataSize, u16 width, u16 height, u16 128,
u16 128` (meaning unknown), `i32 originX, i32 originY` (offset of the frame's
top-left corner from the hotspot; for walking kids the hotspot is at the feet),
then `height` RLE rows. Each row is a length byte followed by control bytes:
a signed control `c < 0` repeats the next byte `−c` times, `c > 0` copies `c`
literal bytes. Pixel index 0 is transparent; index `n ≥ 1` is palette entry
`n − 1`. Every row decodes to exactly `width` pixels and every bank ends exactly
after its last frame (121/121 members). `.FPC` frames are slightly larger than
`.TPC` (kids: up to 37×45 vs 36×43); OpenTPW draws `.FPC`. Which detail level the
original picks when is unknown.

**`.ESP` animation table** (350 bytes): magic `ESP_FILE2.00`, NUL-terminated bank
name (`SPR_BE.TPS`), zero padding, two flag bytes at `0x10C`, then 20 slots of
`u16 firstFrame, u8 framesPerDirection, u8 directions` from `0x10E`. Frames are
direction-major. Flag `0x10C` is 1 for every walking character; flag `0x10D` is 1
for kid sets CH, KI, SA, SU and their heads, which look like girls (visual
inspection only; meaning unverified).

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
| `PeepInfo.ExitLevel` / `ExitLevelVar` ("starting value for the ExitLevel counter, in SECONDS") | Countdown in park seconds; at 0 the guest heads home |
| `PeepInfo.PerfectRide/GoodRide/OKRide` (±5/±15/±40 of preferred excitement) | Happiness change after a ride vs. `UsageInfo.ExcitementLevel` |
| `PeepInfo.Small/Medium/BigHappinessChange` | Penalties (needs, boredom, full queue, sickness) |
| `PeepInfo.RideVomitDivisor`, `VomitCapacity` | Nausea += excitement / divisor; at capacity "Feeling sick" |
| `PeepInfo.ToiletDesparate` | Toilet level that hurts happiness |
| `PeepInfo.DecisionVar{Dist,Queue,Excitement,Thirst,Hunger,Toilet,Illness}Weight` | Attraction score weights |
| `Arrival.MinPeople`, `TimeBetweenArrivals`, `FixedRate` | Bus arrivals (units: see approximations) |
| `BankAccountInfo.InitialAdmissionFee` | Admission paid at the ticket booth |
| `FixedItemInfo.{BusStop,CrossingBSSide,CrossingParkSide,TicketBooth,Entrance}{A,B}Pos{X,Y}` | Arrival/leaving lanes A and B (the entrance cells are the first InitialPath cells, docs/MAP.md) |

Not used yet: `ExcitementToCostDivisor`, `MinimumEntryFee`, price multipliers and
`OpinionFor*` (entry-fee opinion; economy slice), `DecisionVariable1–3` (new/indoor
rides), `PrankeryLikelihood`, `StinkbombLikelihood`, `RegionFX`, `NewParkBonus`,
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
the state machine below. `TP.ICD` is not readable as plain strings (no `Peep`/
`Kid` strings found), so no executable constants were recovered.

## Simulation

All on the 60 Hz `FixedStepClock` in `Level.Update`, guests first, then the
placed ride (bridge host step, then script slice). One SplitMix64 stream
(`GuestRandom`) per park, seeded with `Level.GuestSeed`; guests are updated in id
order and attractions in `AttractionId` order, so the same seed and inputs give
the same `ComputeStateHash` (FNV-1a over all guest fields, RNG state, counters).
`System.Random` is not used.

**Path grid** (`GuestPathGrid`): walkable cells are save cells with the path flag,
linked by their cardinal connection bits (`SavePathConnections`), plus MAP
`InitialPath` cells (4-neighbour links) where no save exists (fantasy, space,
hallow: their 10 entrance cells only). Queue cells (path-flag bit 1) are not
walkable. Routing uses breadth-first **flow fields** cached per target cell and
invalidated on grid edits; `FindPath` follows the field. Jungle: 78 cells, one
network reachable from EntranceA.

**States** (`KIDSTATES` indices): bus stop → crossing (0, 1) → ticket booth (2, 4:
pays `InitialAdmissionFee` or turns back angry) → entering (5) → walking around (7)
→ going to a ride (10) → queueing (11) → waiting to board (14) → using (16, hidden)
→ exiting a ride (15) → … → leaving the park (18) → crossing (19) → waiting to go
home (21) → removed.

**Approximations** (no original source; values in `GuestSettings`):

| Item | OpenTPW choice |
| --- | --- |
| Walking speed | 1.0 cell/s (0.7× below 20 energy) |
| Arrival units | `TimeBetweenArrivals` read as tenths of a second (150 → a bus every 15 s) carrying `max(MinPeople, FixedRate)` guests, spawned 0.4 s apart, lanes A/B alternating; cap 1,000 guests |
| Needs | hunger +0.35/s, thirst +0.45/s, toilet +0.3/s, energy −0.15/s walking / +1/s resting or riding, nausea −0.2/s; thoughts from level 70 |
| Starting happiness | 60 of 100 |
| Decision | every 3 s: leave if ExitLevel ≤ 0 or happiness ≤ 10; else score = AttractionValue − DistWeight·distance − QueueWeight·queue + ExcitementWeight·(25 − |preferred − excitement|) + need terms (weight · need / 4) − IllnessWeight·nausea / 4 − AttractionValue if just ridden + random 0–9; best positive score wins, otherwise wander (random link, no U-turn) |
| Boredom | `BoredomThreshold` read as seconds since the last ride |
| Ride outcome | ExitLevel += positive happiness change |
| Queue length | 4 × capacity (`QueueWaitTimeConstant` not understood) |
| Queue positions | 3 abreast, 0.28 cells apart, back along the entrance cell's first link (real queue cells are not walked yet) |
| No exit lane | guests vanish (original ejection not modelled) |

## Visitor bridge and RSE visitor opcodes

`IRideVisitorBridge` (`IRideVisitorBridge.cs`) is the attraction side guests see:
id, name, kind, open state, capacity, `ExcitementLevel`, `AttractionValue`,
`Satisfies` (hunger/thirst/toilet for shops), `Price`, `EntranceCell`, `ExitCell`,
queue operations and an `IRideVisitorHost` (implemented by `GuestSimulation`) for
callbacks: offered, boarded, released, turned away.

`RideVisitorBridge` implements it for an RSE script. Host protocol (inferred from
the corpus: Coconut, Totem, Gift Shop, Bouncy, Jungle Spray all follow it), run
once per tick before the script slice and never inside `CRIT_LOCK`:

1. `VAR_LETMEOFF ≠ 0` → release that guest at `ExitCell`, write 0 back (scripts
   wait with `TEST VAR_LETMEOFF; BRANCH_NZ` until the host has taken the guest).
2. A guest offered earlier whose id is no longer in `VAR_LETMEON` was taken.
3. Ride closed → a pending offer is withdrawn and the queue turned away.
4. `VAR_LETMEON = 0` and a queue → write the front guest's id.

Visitor opcodes (all operate on the guests the script holds; semantics inferred):

| Opcode | Bridge behaviour | Corpus evidence |
| --- | --- | --- |
| HUSH v | guest boards (leaves host control) | Totem `HUSH VAR_LETMEON` before `WALKON`, `COPY VAR_LETMEON 0` |
| WALKON v … | guest boards; walk along ride nodes not simulated | Jungle Spray boards with WALKON alone |
| HOP dest | next boarded guest not yet hopped, else 0 | Totem `HOP VAR_TEMP2; WALKOFF VAR_TEMP2` |
| WALKOFF v | queue v for WALKGET (instant) | |
| WALKGET dest | next walked-off guest, else 0 | `WALKGET VAR_LETMEOFF; BRANCH_Z` |
| LIMBO v s | guest boards, held `s` **seconds** (unit inferred; corpus 5, 15, VAR_DURATION) | Gift Shop `LIMBOSPACE 0; BRANCH_Z; LIMBO VAR_LETMEON 5` |
| UNLIMBO / FORCEUNLIMBO dest | first guest whose time is up / any guest, else 0 | `UNLIMBO VAR_TEMP; BRANCH_Z; WALKOFF VAR_TEMP` |
| INLIMBO / LIMBOSPACE dest | held count / header limbo size − held | |
| BOUNCE v d | guest boards, held `d` s (10 s when VAR_DURATION is 0) | Bouncy `BOUNCE VAR_LETMEON VAR_DURATION` |
| UNBOUNCE / FORCEUNBOUNCE out | writes the leaving guest into its operand | Bouncy `TEST VAR_LETMEOFF; BRANCH_NZ; UNBOUNCE VAR_LETMEOFF` |
| BOUNCING dest | bouncing count | |
| ADDHEAD / DELHEAD | accepted, no effect (head sprites not drawn) | |

`TOUR/BUMP/COAST` (ride-type controllers with their own guest handling, e.g.
`COAST 3 VAR_LETMEOFF`), `WALKST_FLOAT`, `WALKFLOATSTAT`, `WALKFLOATSTOP` stay
unimplemented. Riders are hidden while on a ride (`RideHandlesSprite 1`: the
original script places them; seat attachment is not implemented).

**Totem** (`PrototypeRide`): the bridge is wired into its effects; capacity 6,
excitement 70, attraction value 25 from `Totem.sam`. Its entrance/exit is the
nearest path cell to its 5×5 footprint (the prototype has no catalog shape yet;
jungle smoke site: cell (39, 21)). With guests near the ride the script fills at
script time ≈3.3 s and starts (versus 11.3 s without passengers), runs one cycle
and unloads all six through HOP/WALKOFF/WALKGET/`VAR_LETMEOFF`.

## Rendering

`GuestRenderer` builds one dynamic vertex buffer per frame: a camera-facing quad
per visible guest (both windings), feet on the bilinear heightfield corner
heights (also over heightfield holes such as the entrance road), one draw call
with up to eight kid atlases (`GuestSpriteAtlas`, 1024-wide RGBA, built on the
CPU from `.FPC`). Direction comes from the guest's heading relative to the
camera; the walk frame advances 10 frames per cell walked. Sprite world size
(0.045 engine units per pixel, ≈ one cell per kid height) is an approximation.
Not drawn: heads/costumes, balloons, thought bubbles, shadows, riders.

## Tests and evidence

- `GuestTests` (no assets): sprite RLE/palette/bounds, ESP slots, direction
  mirroring, flow fields and connection bits, shop-protocol queue order
  (offer → board → release), queue limit and closing, LIMBO timing and header
  limbo size, admission and turning back, ride choice + PerfectRide/OKRide
  happiness, needs growth and a thirst shop, leaving through the lane, same seed →
  same hash (different seed → different), 600 guests with six scripted shops:
  ~0.06 ms per 60 Hz tick on an M-series Mac.
- `GuestAssetTests`: 8 kid sets × 175 frames, slot coverage, flags; jungle
  `Standard.sam` settings; 78-cell jungle network from EntranceA; real guests on
  the original `Totem.RSE` (full load before 10 s, all riders released);
  deterministic 120 s jungle run.
- Native smoke (`--load-original-level jungle --smoke-test`, also fantasy, space,
  hallow): guests seeded near the Totem board it, `VAR_ONRIDE` = 6 at motion start
  before 10 s, a rider is released through `VAR_LETMEOFF`, arrivals pay admission,
  and GPU readback with vs. without guest sprites differs (≈3,000 pixels);
  captures `native-smoke-original-guests.png` and `-guests-hidden.png`.

## Open

Original arrival/need/decision formulas and units; staff interaction, litter,
vomit puddles, prankster/stinkbomb behaviour; entry-fee opinion; shops and
toilets (need the rides/objects slice to expose catalog attractions through
`IRideVisitorBridge`); walking real queue cells; seat attachment of riders;
heads, thought bubbles, balloons; save/load of guest state; which `PeepTypes`
entry uses which kid set.
