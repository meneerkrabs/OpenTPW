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
detail row. Successful ticket-booth admission starts the counter at zero; the
first increment is the following simulation tick. Bus/road travel and waiting
before admission do not count. `SpawnInPark` starts seeded guests at a known zero.
Walking, attraction queues, rides and walking toward the park exit count; travel
along the road home and waiting for the bus do not.

Only the guest simulation's supplied tick delta advances the counter. The level's
fixed-step clock receives scaled simulation time, so pause supplies no steps and
speed changes the number of simulated steps rather than elapsed wall time. The
All Visitors row displays whole seconds as `HH:MM:SS`, with total hours continuing
past 24. A duration unknown from an older save displays an em dash (`—`).

The own-format guest snapshot carries an optional nullable field. Missing/null
durations remain unknown after restore and subsequent updates: elapsed time is
not reconstructed from the guest's exit counter, needs or the park clock. A guest
still outside the park when restored starts a known zero if later admitted.
Known values must be finite, nonnegative and no greater than
`TimeSpan.MaxValue.TotalSeconds`; the guest restore and world-file shape validators
enforce the same bound. A tick that would exceed it rejects before changing the
simulation. This is an own-format/display range, not a native game limit. Existing
save versions remain readable; older builds that reject unknown JSON fields may
not read newly written files.

The counter is saved display metadata and does not drive gameplay. Both the world
canonical guest digest and the guest gameplay hash exclude it, consistently with
presentation-only fields. Known and unknown duration metadata can therefore
produce the same gameplay hash while their displayed values differ.

Native visitor timing and display units remain untraced (`GUEST-920`, `UI-908`).
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
| `PeepInfo.ExitLevel` / `ExitLevelVar` ("starting value for the ExitLevel counter, in SECONDS") | The exit counter's start: ExitLevel plus a draw within ±ExitLevelVar; it counts down once per needs phase (4 park turns, 0.992 s) in every state |
| `PeepInfo.PerfectRide/GoodRide/OKRide` (±5/±15/±40 of preferred excitement) | Happiness change after a ride vs. `UsageInfo.ExcitementLevel` |
| `PeepInfo.Small/Medium/BigHappinessChange` | Penalties (needs, boredom, full queue, sickness) |
| `PeepInfo.RideVomitDivisor`, `VomitCapacity` | Nausea += excitement / divisor; at capacity "Feeling sick" |
| `PeepInfo.ToiletDesparate` | Toilet level that hurts happiness |
| `PeepInfo.DecisionVar{Dist,Queue,Excitement,Thirst,Hunger,Toilet,Illness}Weight` | Attraction score weights |
| `Arrival.MinPeople`, `TimeBetweenArrivals`, `NewParkBonus`, `PointsPerVisitor` | Arrival groups ([Arrivals](#arrivals); `FixedRate` is not read by the arrival code) |
| `BankAccountInfo.InitialAdmissionFee` | Admission paid at the ticket booth (standalone only; with the park economy attached its entrance fee applies, see Money below) |
| `FixedItemInfo.{BusStop,CrossingBSSide,CrossingParkSide,TicketBooth,Entrance}{A,B}Pos{X,Y}` | Arrival/leaving lanes A and B (the entrance cells are the first InitialPath cells, docs/MAP.md) |

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

The group size is `(int)(0.8 × (park points + NewParkBonus)) / PointsPerVisitor` (`PointsPerVisitor` below 2
divides by 1), at least `MinPeople`, cut so that the guests in the park plus the group stay within 1,500;
a bankrupt park gets none. With the shipped 20 and 6 a new park gets 2 guests per group. The PowerPC oracle
ran this routine (docs/reverse/ORACLE.md, F9). It adds three details to the first reading. The factor is
0.8, not 1.2, while the `+48` word of the weather thing (game `+0x1da724`, saved as `mWeather`) is positive. That word is the weather
object's `mCurrentDrops`, the drops its tick ramps toward the rain or snow target (0 in clear weather), so the group is
smaller while rain or snow is falling and the factor is 1.2 otherwise. OpenTPW uses 0.8 while `ParkWeather.CurrentDrops`
is above 0 (`GuestSimulation.WeatherDropsFalling`); a scene without weather has no drops and uses 1.2. (An earlier reading took the
thing to be the Traffic Lights fixed item at `+0x1da734`; that offset was miscalculated, see ORACLE.md F9.) Online parks (GameType 1) are cut at
500 instead of 1,500; OpenTPW applies that to a visit of another player's park (`OnlineVisit`, an assumption,
ARRIVAL-002). A group cut with more guests than the limit becomes
negative; OpenTPW stops at 0 (ARRIVAL-004). Executing the update routine `0xcb2ac` over repeated calls (F11)
shows that this changes nothing in behaviour: a group at or below 0 creates no guest and, at the next stop of
the arrival vehicle, restarts the timer and clears the active byte exactly as an empty group does, so nothing
holds. Only the stored group word and one vehicle step for an exactly empty group differ (effect not traced).

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
ride has its track word (set when the ride is built). OpenTPW applies the first three (`IsChoosable`,
`IsBrokenDown` or the script's VAR_BROKEN, `IsOpen`), so a closed or broken ride gives no points, while its copies still count in the
divisor of an untracked type; the entrance, queue room and coaster conditions have no state in the economy
(ARRIVAL-003). Objects imported from an original save or placed as fixed items set the first
purchase to the current turn (ARRIVAL-005). Open points are ARRIVAL-001..005 (`ArrivalApproximations`).

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
hallow: their 10 entrance cells only). The save's queue cells (path-flag bit 1)
are not imported. Queue cells built in OpenTPW (original map cell type 3, see
[Queues](#queues)) are kept beside the path cells and are not walkable for
routing. Routing uses breadth-first **flow fields** cached per target cell and
invalidated on grid edits; `FindPath` follows the field. Jungle: 78 cells, one
network reachable from EntranceA.

**States** (`KIDSTATES` indices): bus stop → crossing (0, 1) → ticket booth (2, 4:
pays `InitialAdmissionFee` or turns back angry) → entering (5) → walking around (7)
→ going to a ride (10) → walking to a queue position (12) ⇄ standing in the queue
(11) → called: walking to the stand point (13) → waiting to board (14) → using (16, hidden)
→ exiting a ride (15) → … → leaving the park (18) → crossing (19) → waiting to go
home (21) → removed.

**Approximations** (no original source; values in `GuestSettings`):

| Item | OpenTPW choice |
| --- | --- |
| Arrival step-off | A group's guests appear 0.4 s apart, lanes A/B alternating, instead of at a bus's stops (ARRIVAL-001); OpenTPW also caps the simulation at 1,000 guests |
| Needs | traced ([below](#needs-and-starting-values)); energy −0.15/s walking / +1/s resting or riding is OpenTPW's own meter (GUEST-008); thoughts from level 70 (GUEST-009) |
| Starting happiness | 50 of 100 (traced, [below](#needs-and-starting-values)) |
| Decision | every 3 s (cadence approximated): the original's score (oracle F24, [below](#attraction-choice)) picks the best attraction of at least 10, otherwise wander (random link, no U-turn). Leaving is tested every park turn instead ([below](#lifecycle)) |
| Boredom | `BoredomThreshold` read as seconds since the last ride |
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
| Distance | `100 − min(100, 100 · (dx² + dy²) / 450)`, squared cell displacement to the attraction's back-of-queue cell (divided by an environment word that OpenTPW lacks, GUEST-001) |
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
Open: the decision cadence and the "nothing found" happiness loss, the minor decision, the environment grid, price eligibility, shop and toilet effects on the meters, thought thresholds (`GUEST-001..005`).

### Needs and starting values

Traced from the Mac binary and executed in the PowerPC oracle (finding F25, [ORACLE.md](reverse/ORACLE.md)); implemented in `GuestStart`, `NeedsUpdate` and `UseEffects`. A new guest starts with
happiness **50**, thirst and hunger a random 0..49, toilet 0..29, illness and litter 0, an exit counter of `ExitLevel` plus a draw within ±`ExitLevelVar` (60..179 with the shipped 120 ± 60) and cash
`StartingCash` ± `StartingCashVarPc` percent of a random type's. Every park turn each guest goes through the needs function; **it does something only on the guest's phase**, when the low two bits of the park turn equal those of
its thing id (OpenTPW: its guest id, WALK-010):

- the exit counter loses 1 (so one count is 4 park turns = 0.992 s, in every state, and it runs negative after 0);
- the cell's environment words move happiness, illness and hunger (not modelled: 0, GUEST-006);
- on a turn that is also a multiple of 16, toilet +1, hunger +2 and thirst +2 (all clamped to 0..100). A multiple of 16 has its low two bits 0, so **only a guest whose id is a multiple of four ever grows**;
  three in four guests keep the needs they started with unless they use something;
- each of illness, hunger, thirst and toilet at a truncated byte of exactly 100 costs 1 happiness;
- the toilet speed term is 25 while the toilet byte is above 80.

Using an attraction then applies its own effects after the outcome of [Attraction choice](#attraction-choice): thirst and hunger fall by the type's `ThirstEffect` / `HungerEffect`, illness and happiness rise by
`VomitEffect` / `HappinessEffect` (clamped); a toilet sets the toilet meter to 0 (GUEST-005). Illness has no decay of its own. A guest at hunger 80 at a shop with a hunger effect of 50 goes to 30.
Open: the thought selection (hunger and thirst above 90, a single need above 99, happiness above 99 or below 10; GUEST-009), the pranksters (GUEST-007), the draws (OpenTPW's own stream, DET-014).

### Lifecycle

Traced and executed (finding F26, [ORACLE.md](reverse/ORACLE.md)); implemented in `GuestLifecycle`. A guest starts at happiness **50** with the exit counter described under [Needs](#needs-and-starting-values), which loses one per
needs phase (4 park turns, 0.992 s) in every state. A guest that is walking around or doing nothing (the original's state 6) **leaves when its happiness is below 1, when its exit counter is exactly 0, or when the park is closed**, and
loses `BigHappinessChange` (25) as it goes home; the test is evaluated every park turn (GUEST-010). The counter compares for equality, so a guest that is queueing or riding in the four turns it reads 0 never leaves by it;
rides do not add to it. There is no "happiness at most 10" rule.

### Walking

Traced from the Feral Mac binary ([WALK-plan.md](reverse/WALK-plan.md) §11, WALK-I;
`GuestNavigation`, `GuestSpeed`). There is no global walk speed and no energy factor.

- **Speed** (§4). Each guest draws a base speed (`+192`) from {60, 80, 100, 120, 140}
  with the guest stream at creation. Once per park turn (248 ms), before its state
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
- **Steering** (§3, §5–§7). Positions and velocities are 16.16 fixed point. On the
  tick that starts a park turn, a walking guest takes one step: avoid_walls (0.9; the
  first edge of +x, −x, +y, −y that pushes), follow_path (0.5) and separation (0.1),
  each truncated to the max force, summed and
  truncated, added to the velocity, which is truncated to the max speed. The position
  is committed only when the new cell is walkable for the walk. A new destination
  keeps the velocity. Routes start with the guest's own cell; waypoints are cell
  centres, passed at octile < 0.4 cell; the last segment arrives at octile < 0.32 cell
  of the exact destination, with the desired speed min(max speed, d / 2). Arrival is
  tested before the step moves. A routing failure arrives at once. Six non-improving
  steps among the last 15 reroute.
- **Separation** (`0xfd778`, `0x10087c`, `0xfd5f4`). Off on the last segment within
  ONE + radius of its waypoint; else every guest in the 3 × 3 cells around the walker
  (rows from the top) whose 1/256-cell position lies within 0.4 cell pushes it away by
  FixDiv(offset, octile(offset)), at most five of them.
- **Geometry.** Sub-cell destination bytes are byte / 256 of a cell. Path walks go one
  path cell at a time to the guest's offset point in it; lane walks (bus stop to
  entrance) have no walls. Queue walks follow the queue cells; the cell ahead of the
  destination and the cell behind the start are walkable too. The stand point is in
  the ride's entrance cell at trunc(255 × `EntryCellStandPos`) after rotating it with
  the object (`0xde1d8`): (0.5, 0.5) gives (127, 127).
- **Presentation.** The drawn position and walk distance interpolate between the last
  two steps over a turn (`Guest.DrawX`, `DrawY`, `DrawDistanceWalked`); they are not
  simulation state.

**Walk approximation register** (`WalkApproximations.cs`; also in
[FIDELITY-REGISTER](FIDELITY-REGISTER.md)):

| ID | OpenTPW choice | Evidence needed |
| --- | --- | --- |
| WALK-001 | The boost `+196` comes from the Sugar ingredient only, with the ride build's amount 50 (+3 per visit); the original's per-object amount (ride `+404`, set from the shop screen and saved) is not modelled | The shop screen that sets `+404` (`0x1685b4`, `0x168640`), its range and the saved `mAmountOfSpecialIngredient` |
| WALK-002 | avoid_walls models its edge branch (the first pushing edge of +x, −x, +y, −y), not its four diagonal-corner branches | `0xfc9e0` and `0xfdfe4..0xfe5e0` |
| WALK-003 | Separation's neighbours are the guests outside rides and shops at their navigation position, in OpenTPW's list order within a cell; the original's query `0x10087c` also keeps thing types 4–8 and 18, which are not neighbours in OpenTPW | The writers of thing `+4/+6` and of the per-cell thing lists (`+764`, `+10`) |
| WALK-004 | Routes are OpenTPW's cell sequences, refilled five waypoints at a time; the reroute prepends the current cell to the rest | The route search `0x101c64` and its `+164/+168` bookkeeping |
| WALK-005 | A cell is walkable when it is on the current walk (its route and extra open cells); path cells beside the route are walls | The edge flags `0xd768c` reads, the `+180` mask per state and the commit callback `0xfee3c..0xfee58` |
| WALK-006 | A queue walk may also enter the queue cell ahead of its destination and the one behind its start, and no other cell off its route | The queue cells' edge flags and the `+180` mask in states 12 and 13 |
| WALK-007 | No map-edit reroute: the block stamp check against `+72` and the failure flag `+184` are left out | `0xd7e54` and the `+72/+184` bookkeeping (`0xfe650..0xfe6f8`) |
| WALK-008 | Guests on the arrival lanes walk off the path grid: no wall force, every position committed | The lane cells' edge flags and the `+180` mask outside the park |
| WALK-009 | The progress history compares raw remaining distances and skips the first step after route setup | The `+160` route length and the `+172` reset (`0xfeed8`, `0xff6fc`) |
| WALK-010 | The needs phase that gates the toilet term compares the park turn with OpenTPW's guest id | The thing id allocator `0xfa9a4` and its order |

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
entrance fee and open/closed state decide, and refused guests turn back at the booth — shop and
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
once per tick before the script slice and never inside `CRIT_LOCK`:

1. `VAR_LETMEOFF ≠ 0` → release that guest at `ExitCell`, write 0 back (scripts
   wait with `TEST VAR_LETMEOFF; BRANCH_NZ` until the host has taken the guest).
2. The called guest's id was written to `VAR_LETMEON` and is no longer there →
   the script took it: it leaves the queue list and boards (original state 14).
3. Ride closed → the called guest is withdrawn and the queue turned away.
4. Once per park turn, the admission of [Queues](#queues) may call the front
   guest forward. The guest writes its own id to `VAR_LETMEON` (only while that
   is 0) when it reaches the stand point. Hosts that do not simulate the walk
   (unit tests) present the guest at once.

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
| ADDHEAD / DELHEAD | the ride's head table (`RideHeadSpace`): a free slot takes the visitor, DELHEAD frees it; the head is drawn by `RideHeadRenderer` (below) | Mac `0x100B0E9C` / `0x100B0F90` |

`TOUR/BUMP/COAST` (ride-type controllers with their own guest handling, e.g.
`COAST 3 VAR_LETMEOFF`), `WALKST_FLOAT`, `WALKFLOATSTAT`, `WALKFLOATSTOP` stay
unimplemented. A rider on a ride is not drawn as a guest (`RideHandlesSprite 1`);
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
back to the nearest walkable path cell as a one-cell queue (RIDES-028). Guests
step onto the back cell from the **join cell**, a walkable neighbour of the
back cell. A queue that touches no path is not connected and guests do not
choose the ride. Every grid edit recomputes all queues. A changed queue is a
queue edit: queued guests beyond 4 × cells or off the queue leave, and the
others walk to their positions again.

**Building** (§3.3, partly approximated). `QueuePaths.TryExtend` (headless)
and the level's queue tool (`Level.BuildQueueCell`) lay one cell at a time. The
first cell is the front cell, linked toward the ride. Every later cell touches
the back cell and links toward it. Only `Info.HasQueue` objects take queue
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
also block object placement. New queue cells are not drawn on the terrain yet.

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
   Jelly Bounce). A ride with a thing behind its record `+36` scores 60 % of its level plus up to 40 in the
   original; OpenTPW has no such thing (QUEUE-004).
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
the simulation's seeded `GuestRandom` (the world-seeded stream guests already
use; `System.Random` is not used). Guests walk along the queue cells to their
position (state 12) with the traced steering ([Walking](#walking)).

**Standing** (§5, state 11, once per park turn = 248 ms of the fixed clock,
`ParkCalendar.Turn`). On entry the move delay is trunc(1.2 × position) turns.
Each turn, in order:

1. Called forward → walk to the stand point (state 13).
2. The ride failed (`0xdfe34`: a toilet whose wear gauge is below 25) → leave, "feeling sick".
3. Not in the list any more → leave.
4. The list position moved up: wait while the delay is non-zero and the gap is
   at most 2 (one turn per decrement), else walk to the new position.
5. Recorded position above the limit → leave.
6. Needs window, every 30 turns after the last interlude: happiness > 80 or
   10–19 idles for 10 turns, happiness < 10 leaves, else toilet > 80 leaves
   unless the attraction provides relief.
7. A 1-in-10 facing change.

There is **no boredom timeout**. The original's test (`turn > +508 + 100`
within the 30-turn window) can never fire (§5.3); the code keeps it and the
tests show that guests stand for 2,000 turns without leaving. Guests leave a
queue only when admitted, when unhappy (< 10), for the toilet (> 80, not when
queueing for a toilet), when the ride fails, closes or is removed, or on a
queue edit. Happiness and toilet are compared after truncation to whole
numbers, as the original does (`fctiwz`): "> 80" means at least 81.

**Breakdown** (ORACLE.md F15). While `VAR_BROKEN ≠ 0` the ride dismisses its queue head first,
one guest per park turn: the ride update (`0xe0a8c`, executed in the oracle for its states 1, 2
and 4) removes the head from the queue and sends it away (`0xdcebc`, `0xee804`: reason 6, no
thought, happiness down by `PeepInfo.MediumHappinessChange`, back to wandering). A head the script
has already taken is forced aboard instead. The guests behind keep their places and wait, and a
repaired ride keeps whoever is left. While the ride is broken (state 1) the guests behind do not walk forward either (`0xed4b0` skips the move), so they keep their physical places; a condemned ride (state 4) is not told apart. A script that closes the ride on breakdown does not empty the queue at once: the drain takes precedence. Nobody new joins: walking guests do not choose the ride
(QUEUE-017) and joins are refused as if it were closed (QUEUE-018); these two are approximations. The ride update runs the admission once on the turn it
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
[FIDELITY-REGISTER](FIDELITY-REGISTER.md)):

| ID | OpenTPW choice | Evidence needed |
| --- | --- | --- |
| QUEUE-001 | The queue link (+13) uses the connection bits' compass: 1/4/16/64 = −Y/+X/+Y/−X (verified for +12 on 78 Easymode cells) | Run-time neighbour offset tables (data `0xec52c..0xec5a4`) |
| QUEUE-002 | The next queue cell is searched in grid direction order | Same tables; the order paired with links 16, 1, 64, 4 |
| QUEUE-003 | Guests step onto the back cell from its first walkable neighbour | `0xdd744` and the state-10 walk to the back cell |
| QUEUE-004 | The join excitement gate, clamped difference and live excitement follow the original (ORACLE.md F16, executed); only the excitement of a ride with a thing behind ride `+36` (60 % of the level plus up to 40 from three piece counts of `0x1ea40`) is not modelled | What ride `+36` and the three counts are (no OpenTPW object has one) |
| QUEUE-005 | Queue-edit exits: beyond 4 × cells, or standing cell removed | `0xee8f8` in detail |
| QUEUE-006 | Depth from the cell's front edge; the 4th position stays in its cell | `0xdde74` and the sub-cell axis selection |
| QUEUE-008 | The ride-failure exit follows `0xdfe34` (ORACLE.md F15, executed): a relief object whose wear gauge is below 25. OpenTPW wears only rides, so a toilet's gauge never falls and the exit never fires | The wear step of relief objects (callers of `0xdec2c`) |
| QUEUE-009 | Facing-change sign from the 1-in-10 draw | The sign selection in `0xed244` |
| QUEUE-010 | A guest leaving a queue reappears on the join cell at once | The state-6 transition after a queue exit |
| QUEUE-011 | Upgrade level 0 for QWTC/InitSpeed | None (OpenTPW does not apply upgrade levels to objects) |
| QUEUE-012 | Queue cells need allowed terrain and no object | The queue tool's validity checks |
| QUEUE-013 | At most 25 cells per queue | The queue tool (`0x70b98..0x8c7c0`) |
| QUEUE-014 | Laid cell by cell from the front, each touching the back | The queue tool's placement rules (UI-031) |
| QUEUE-015 | Removing a queue cell removes every cell behind it | The remove tool's route into `ClearCell` `0x859b4` |
| QUEUE-016 | Admission and state 11 run once per park turn | Live-list eligibility of objects and guests |
| QUEUE-017 | Guests do not choose a broken attraction | The state-6 attraction choice and its test of ride `+408` |
| QUEUE-018 | A broken attraction refuses joins as if closed | Ride `+408` and the state-6/10 handling of a broken ride |
| QUEUE-019 | OpenTPW keeps no ride state word: a ride is in state 0 while it is open and in state 1 while it is closed and `VAR_BROKEN ≠ 0` (the admission runs on the turn it is noticed, `SetState(1)` writes `VAR_RIDECLOSED` and the open word, F27); a queue still there when the script clears `VAR_BROKEN` is turned away at once, where the original's ride stays in state 1 and keeps dismissing until a `SetState(0)` caller reopens it | A trace of when the script clears `VAR_BROKEN` |
| QUEUE-020 | BOUNCE takes one script-clock reading for the deadline's base and the slot's start; the original calls its millisecond clock twice (`0xadfb0`, `0xadfcc`), so its start can lie 1 ms later | None for the reading; OpenTPW's stepped script clock cannot tick between the two calls |
| QUEUE-021 | A left click on a queue cell (help 444, "Left-click to edit this queue") starts the queue tool at that ride's back cell; the click handler is not traced | None: `Level.EditQueueAt` follows the help text and `EnterQueueTool` |
| QUEUE-022 | A bridge without any operating values (speed, design speed, duration and design duration all 0: a synthetic object; every original object has `InitSpeed` and `InitDuration`) is read at its design speed and duration, where the original would divide 0 by 0 and score 0 | None |

Not modelled: the track-type exits of step 6 (`0x45eac`, ride `+40`), ride
state `+408` (beyond the QUEUE-019 reading), the price check at the stand
point (guests pay when boarding, see Money), save/load of queues, and the import of the Easymode queue cells.

## Rendering

`GuestRenderer` builds one dynamic vertex buffer per frame: a camera-facing quad
per visible guest (both windings), feet on the bilinear heightfield corner
heights (also over heightfield holes such as the entrance road), one draw call
with up to eight kid atlases (`GuestSpriteAtlas`, 1024-wide RGBA, built on the
CPU from `.FPC`). Direction comes from the guest's heading relative to the
camera; the walk frame advances 10 frames per cell walked. Sprite world size
(0.045 engine units per pixel, ≈ one cell per kid height) is an approximation.
Not drawn: costumes, balloons, thought bubbles, shadows, riders' bodies on a ride (their heads are drawn, see below).

## Riders' heads

A ride whose script runs `ADDHEAD` shows each rider as a head hung on the model.
Traced from the Feral Mac binary (`SimThemePark`, PowerPC):

- `ADDHEAD` is `0x100B0E9C` and `DELHEAD` `0x100B0F90`, read from the RSE opcode table (`0x2F58` → `0x3F2A4`, 106 entries, [RSE-VM.md](RSE-VM.md)). `ADDHEAD` takes the script's head table (script +48, null: nothing) of length +76, draws a random slot until one holds 0 (none free: nothing), stores the visitor and hangs the head of head id slot + 1. `DELHEAD` zeroes every slot holding the visitor and takes its head down. [BIN:STP-PPC:0x100B0E9C] [BIN:STP-PPC:0x100B0F90]
- A head id is the value of a dummy attribute record of the model whose type has flag `0x80`: `0x1019B298(model, mask, id)` returns the index of the record with that value and flag, or -1; `0x1019B56C` hangs the head on that record (record flag `0x2`, a sprite handle). The table length is the run of ids from 1, following the MIT fork's reported PC sizing rule (its `FUN_0044b220(thing, 0x80, id)` takes the ids upwards until -1; the MIT fork's docs, `maexah/OpenTPW` `docs/exe/park.md`, "The head table"). [BIN:STP-PPC:0x1019B298] [BIN:STP-PPC:0x1019B56C]
- The table of a car type is sized by the same records: `0x100303A8` counts the flagged records (`0x1019B354`), prints "Car type %s can receive %d heads" and fills its node table (`0x1019B300`). [BIN:STP-PPC:0x100303A8] [BIN:STP-PPC:0x1019B354]
- The head art is `esprites/Generic/Kidsheads` (the kid set's string `kidsheads` is loaded at `0x10069A70`). [BIN:STP-PPC:0x10069A70]

Corpus facts (`RideHeadsCorpusTests`): all 308 associated scripts in the four-theme catalog are inspected (262 roots and 46 child/sound scripts); only the roots contain head commands. 31 scripts run `ADDHEAD`/`DELHEAD` in four themes (fantasy 8, hallow 9, jungle 6, space 8).
Jungle head runs: Sun God 32, Crazy Ape 16, Mumbo 5, Rocky Racers 8, Tom Tom Twister 40, Eruption 16; the ferry 1, Inca Totem 13 and
Aztec Mayhem 27 carry runs without `ADDHEAD`. Tubes Of Zob runs `ADDHEAD` but its model has no head id 1, so its table is null.
Coaster and tour car models with head records: `CrocCar` 6, `ape` 2 (coaster3), `cart` 6 (minecart), `Bird` 9 and `Balloon` 9 (tourride).

The PC sizing rule and reported measurements above are fork leads, not an independent decode of the current packed `tp.exe`. The Mac handlers were independently read; the record-to-node link and script-table allocation remain untraced.

OpenTPW (`RideHeadSpace`, `RideHeadRenderer`): each attraction keeps a shared head table; `ADDHEAD` and `DELHEAD` update it through
`RideVisitorBridge`; the renderer draws a rider only when its state is Using, its attraction id matches that ride, and its head is up as a kid head sprite
(the rider's kid set, `GuestSpriteAtlas.LoadKidHeads`, TPC banks) at the position of the model node named after the head id
(`headNN`, `HeadN`, `Kid_NN`, `spriteposition NN`). The rider's body is not drawn on the ride. Normal guest release removes its old slots; forced teardown clears every slot, including orphaned ids. This is host-owned presentation cleanup, not a traced original destructor rule.

The original-level smoke test requires submitted head quads and separately compares visible/hidden GPU captures on one identified, naturally boarded animating ride. Simulation, animation, camera and shader time are frozen; repeated visible frames and repeated hidden frames must each be byte-identical, and toggling only that attraction's head renderer must change pixels. A native jungle run identified Crazy Ape, attraction 17, visitor 16, node 20, with 10 moving frames before freeze: one submitted quad changed 2,478 pixels. The captures show the rider head at the left seat below the Ape's left arm. This establishes visible OpenTPW head geometry, not original visual equivalence.

Approximations ([HEADS register](FIDELITY-REGISTER.md)): the slot draw uses a per-ride generator (HEADS-001); the head's node is
the node named after its id, because the original's link from a head record to its node is not traced, and Flower Power,
Phantom, Eruption, Hover Bot Havoc and Rock 'n Roll name no node for some ids (HEADS-002); the record = attribute and
table-length correspondences use the Mac lookup code and the fork-reported PC rule (HEADS-003). Presentation reuses the pre-boarding guest heading, body five-direction selector, stand frame 0 and body pixel scale for the seven-direction head art; the seat node contributes translation only (HEADS-004). Root and resolved child/sound VMs share the attraction table, whereas the Mac handler reads the calling script's table; child allocation/binding is untraced, though no associated child/sound script in this shipped corpus uses a head command (HEADS-005).

Not done in OpenTPW: heads of riders on coaster and tour cars. The cars carry head records (above) and the PC executable keeps them as
records with a handle and no head table (maexah `saves.md`, "A head on a node"); the Mac code that places a car's rider is not
traced yet. Root scripts without `ADDHEAD` do not enable this renderer; this does not establish that the original cannot show heads through vehicle/controller paths. Native head atlas reload and repeated park entry/exit checks cover Asset.All removal and scheduled GPU texture/material/shader disposal.

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
`IRideVisitorBridge`); drawing built queue cells and importing the Easymode
queue; seat attachment of riders;
heads of riders on coaster and tour cars (the car models carry head records, see
"Riders' heads"); costumes, thought bubbles, balloons; save/load of guest state;
which `PeepTypes` entry uses which kid set.
