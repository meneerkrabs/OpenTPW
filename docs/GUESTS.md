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
produce the same gameplay hash while their displayed values differ. Duration
range validation is a separate input/display failure: at the supported bound it
can refuse a tick even though the gameplay digest excludes the counter. Hash
equality alone does not establish that display metadata passes its range checks.

Native visitor timing remains untraced (`GUEST-920`). The original visitors list's "Time In Park" column shows whole hours (ticks → seconds / 36e9); OpenTPW's detail row (`[EXT:overview-detail-rows]`) shows HH:MM:SS instead.
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
500 instead of 1,500; OpenTPW applies that to a visit of another player's park (`OnlineVisit`): tp.exe sets GameType 1 when a
published park is entered (`0x4c0830`) and runs the arrival step there. A visit still reads `Standard.sam` instead of the original's
`online_Standard.sam` and counts no park points (ARRIVAL-002). Both cuts compare unsigned, and a group cut with more
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
a kart or water ride its track word (the circuit walk's verdict, `BumpController.IsTrackValid`); the level syncs them each tick. So a
closed, broken, queue-less, full, crossing or invalid ride gives no points, while its copies still count in the divisor of an untracked
type. Only things whose type's `Info.WhichUIType` is 0 or 2 count (`0x4c8240`), which includes the shipped add-ons that declare 0. The
coaster test also needs three track samples and has no edited-coaster term, and the track word starts false before the first walk
(ARRIVAL-003). Objects imported from an original save set the first purchase to the import turn, where
the original restores each type's record from the save (tp.exe `0x4d3aa0`, ARRIVAL-005); fixed items of a new park stamp turn 0 in both. Open points are ARRIVAL-001, 002, 003, 005, 006 and 007 (`ArrivalApproximations`).

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
the booth weighs the fee against the park, see [The ticket booth](#the-ticket-booth)) → entering (5) → walking around (7)
→ going to a ride (10) → walking to a queue position (12) ⇄ standing in the queue
(11) → called: walking to the stand point (13) → waiting to board (14) → using (16, hidden)
→ exiting a ride (15) → … → leaving the park (18) → crossing (19) → waiting to go
home (21) → removed.

**Approximations** (no original source; values in `GuestSettings`):

| Item | OpenTPW choice |
| --- | --- |
| Arrival step-off | One guest steps off per park turn from the turn after the group starts, lanes A/B alternating, as the original's arrival update does once its arrival vehicle reports script variable 1 = 2 at the stop (tp.exe `0x4cf3e0`, run once per world turn). The vehicle (a bus up to 35 guests, a seaplane up to 60, a Ferry above, `0x4cf48c`) is not modelled, so its level script's drive to the stop takes no time (ARRIVAL-001); OpenTPW also caps the simulation at 1,000 guests |
| Needs | traced ([below](#needs-and-starting-values)); energy −0.15/s walking / +1/s resting or riding is OpenTPW's own meter (GUEST-008); thoughts as in tp.exe ([Thoughts](#thoughts)) |
| Starting happiness | 50 of 100 (traced, [below](#needs-and-starting-values)) |
| Decision | traced ([below](#deciding-and-wandering)): once per park turn in state 6; the original's score (oracle F24, [below](#attraction-choice)) picks the best attraction of at least 10 at most every 30 turns, otherwise the guest walks one to five cells to a wander destination (state 7). The litter step and pranks are modelled ([litter](#litter), [pranks](#pranks)); the presentation turns are not (GUEST-010) |
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
Open: the decision cadence and the "nothing found" happiness loss, the minor decision, the environment grid, price eligibility, shop and toilet effects on the meters (`GUEST-002..005`).

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

An empty park is worth 20, so a fee of 24 is fair and 25 already expensive. OpenTPW charges the fee from the guest's purse and turns back a
guest who cannot pay; the original's booth books the fee (`0x4d0600`) without reading the guest's cash (ARRIVAL-007).

### Needs and starting values

Traced from the Mac binary and executed in the PowerPC oracle (finding F25, [ORACLE.md](reverse/ORACLE.md)); implemented in `GuestStart`, `NeedsUpdate` and `UseEffects`. A new guest starts with
happiness **50**, thirst and hunger a random 0..49, toilet 0..29, illness and litter 0, an exit counter of `ExitLevel` plus a draw within ±`ExitLevelVar` (60..179 with the shipped 120 ± 60) and cash
`StartingCash` ± `StartingCashVarPc` percent of a random type's. Every park turn each guest goes through the needs function; **it does something only on the guest's phase**, when the low two bits of the park turn equal those of
its thing id (OpenTPW: its guest id, WALK-010):

- the exit counter loses 1 (so one count is 4 park turns = 0.992 s, in every state, and it runs negative after 0);
- the cell's environment words move happiness, illness and hunger (vomit, stinkbombs, entertainers and guards so far, [Pranks](#pranks); GUEST-006);
- on a turn that is also a multiple of 16, toilet +1, hunger +2 and thirst +2 (all clamped to 0..100). A multiple of 16 has its low two bits 0, so **only a guest whose id is a multiple of four ever grows**;
  three in four guests keep the needs they started with unless they use something;
- each of illness, hunger, thirst and toilet at a truncated byte of exactly 100 costs 1 happiness;
- the toilet speed term is 25 while the toilet byte is above 80.

Using an attraction then applies its own effects after the outcome of [Attraction choice](#attraction-choice): thirst and hunger fall by the type's `ThirstEffect` / `HungerEffect`, illness and happiness rise by
`VomitEffect` / `HappinessEffect` (clamped); a toilet sets the toilet meter to 0 (GUEST-005). Illness has no decay of its own. A guest at hunger 80 at a shop with a hunger effect of 50 goes to 30.
Open: the draws (OpenTPW's own stream, DET-014). There is no boredom loss: nothing in tp.exe reads `BoredomThreshold`.

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
plays `cat_kids` 207 at the prankster. Each prank cheers the prankster up by 1. The advisor's prank lines are not modelled yet (GUEST-007).

A prank calls the guards (tp.exe `0x508a30`, WANDER-plan.md §4), but only on a cell with security: a guard spreads `RegionFX[3]` (security 10,
radius 3) where he stands, so a prankster within three cells of a guard is seen. Then the free guard (idle or patrolling) **nearest the staff HQ**
is sent, and the HQ always sits at map cell (0, 0), so the guard nearest the map's corner goes, however far he is from the prankster. He walks
to the prankster (cat_staff 136) and chases it for `GuardConstsPerGrade.WorkDuration` turns (10 to 75 by grade), re-routing when he arrives where
it no longer is. On its cell he catches it if it is deciding, wandering, taking litter to a bin or walking to a ride (cat_staff 137): he carries it,
out of sight, to the ticket booth (in OpenTPW the park entrance, as the booth lies off the paths) and sets it down there (cat_kids 128), and it
heads for the exit. Like any leaving guest it is recalled on its next turn while its exit counter and happiness are above 0 and the park is
open (the leaving handler `0x500a50`), so the guard's escort sends home only a guest that wanted to go anyway. A carried guest whose guard is dismissed or abandons the escort is removed from the park, as in the original.

Vomit and stinkbombs spread their `RegionFX` row around their cell while they lie: every cell within the row's square radius gets the row's
happiness, illness and hunger divided by its distance + 1, and a guest's needs phase adds those of its own cell (vomit: −1 happiness, +1 illness,
−1 hunger on its cell; a stinkbomb −3, +3, −1 on its cell and less up to 3 cells away). Entertainers and guards carry their rows with them: an
entertainer's `RegionFX[0]` adds 2 happiness on his cell and 1 beside it, a guard's `RegionFX[3]` the security above. The other sources (toilets,
cameras, fireworks) are not modelled yet (GUEST-006).

### Thoughts

Read in full in tp.exe (GUEST-006). A guest's thought (the visitor window's line and the park's most common thought) is set by the thought
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
the destructor `0x4dd0a0`, the toilet wear `0x4e2440`, the handyman's clean `0x4dfd80` and the fireworks tick `0x4e0b90`.

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
repaired ride keeps whoever is left. While the ride is broken (state 1) the guests behind do not walk forward either (`0xed4b0` skips the move), so they keep their physical places; a condemned ride (state 4) is not told apart. A script that closes the ride on breakdown does not empty the queue at once: the drain takes precedence. Nobody new joins: the
choice's counts-test (tp.exe `0x4dd920`) refuses states 1 and 4 and a closed ride, and never reads VAR_BROKEN, so a ride whose script has
just reported a breakdown is still chosen until its ride update enters state 1; joins are refused in state 1 (QUEUE-018). State 1 lasts
until the ride is opened again (a repair, an upgrade's end, the open button), even when the script clears VAR_BROKEN first (QUEUE-019). The ride update runs the admission once on the turn it
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
| QUEUE-003 | Guests step onto the back cell from its first walkable neighbour; the original (tp.exe `0x4de4a0`, `0x4de840`) takes the first neighbour in the order 1, 4, 16, 64 that is connected and a path cell, and OpenTPW keeps no connection bits for queue cells | The queue tool's connection writes (QUEUE-012..014) and the state-10 walk |
| QUEUE-004 | The join excitement gate, clamped difference and live excitement follow the original (ORACLE.md F16, executed); only the excitement of a ride with a thing behind ride `+36` (60 % of the level plus up to 40 from three piece counts of `0x1ea40`) is not modelled | What ride `+36` and the three counts are (no OpenTPW object has one) |
| QUEUE-005 | The leave-for-room rule is the original's (position ≥ 4 × cells, not in state 14; tp.exe `0x501390`, ORACLE.md X14). OpenTPW also sends away a guest whose cell left the queue (Confused) and re-walks the others, where the original changes nothing for them; a leaving guest plays no animation | How the original's state-12 walk recovers a guest standing off the queue; animation 13 and the event-6 record |
| QUEUE-006 | Queue places follow tp.exe `0x4de840` (depths 0/63/127/191, the axis per link, the fourth place on the axis of the cell behind); only the back cell's fourth place takes its axis from OpenTPW's join cell (QUEUE-003) | QUEUE-003's join cell |
| QUEUE-008 | Toilets wear by use (tp.exe `0x4e2440`: 0.05 × the guest's toilet byte) and handymen clean them back to 100 after WorkDuration park turns (`0x4d7880`, state 11, `0x4dfd80`), so the worn-toilet exit fires; the online branch of the wear step and the dirty-toilet message handlers are not modelled | What the two message handlers do with the toilet's cell |
| QUEUE-009 | Facing-change sign from the 1-in-10 draw | The sign selection in `0xed244` |
| QUEUE-010 | A guest leaving a queue reappears on the join cell at once | The state-6 transition after a queue exit |
| QUEUE-011 | Exact completed economy tier for ordinary TPW catalogue queue defaults/fallback capacity; live VM CAP/DUR retained | Native completion reset, operating-state synchronization and performance effects |
| QUEUE-012 | Queue cells need allowed terrain and no object | The queue tool's validity checks |
| QUEUE-013 | At most 25 cells per queue | The queue tool (`0x70b98..0x8c7c0`) |
| QUEUE-014 | Laid cell by cell from the front, each touching the back | The queue tool's placement rules (UI-031) |
| QUEUE-015 | Removing a queue cell removes every cell behind it; the original's cell clear (tp.exe `0x5367a0`) removes only that cell and leaves the rest unreached on the map | The remove tool's route into `0x5367a0` (callers `0x452ca0` / `0x452d30`) |
| QUEUE-016 | Admission and state 11 run once per park turn | Live-list eligibility of objects and guests |
| QUEUE-017 | The queue room and the coaster controller tests of the counts-test (tp.exe 0x4dd920) are left to the join (ARRIVAL-003); the state and open-word tests are applied | none (read: tp.exe `0x4dd920`) |
| QUEUE-018 | A ride in state 1 refuses joins as if closed; the PC join gate is not traced (the Mac join offer 0xe03f0 rejects states 1 and 4) | the PC twin of the state-10 join checks |
| QUEUE-019 | OpenTPW keeps state 1 in the bridge and leaves it when the ride is opened again, standing for the SetState(0) callers (repair, the open button, an upgrade's end); state 2 is the economy's closed ride (ECON-023) and state 4 (life gauge 0) is not told apart from 1 | the life gauge in the bridge (state 4) |
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
Not drawn: thought bubbles, shadows, riders' bodies on a ride (their heads are drawn, see below).

**Costumes.** A guest in costume is drawn by a second `GuestRenderer` with the theme's costume set (`esprites.wad/<theme>/Costumes`: Jungle
`SPR_TI`, Fantasy `SPR_BK`, Hallow `SPR_HK`, Space `SPR_SK`; one set per theme, so the original's draw among them, `0x541f70(2)`, always
picks it). The sets share the kids' animation slots. On a ride, its head comes from the theme's costume heads (Jungle and Space; the other
themes ship none, and the rider keeps its kid head).

**Balloons.** `BalloonRenderer` draws each held balloon from `Generic/Balloons/SPR_BL`: four colours (red, green, blue, yellow), each a whole
and a burst frame, the colour drawn anew whenever the balloon appears (after a purchase or a ride; `0x541fd0`). The placement follows
`0x4fa030`: the balloon hangs at the guest's position 0.35 of a park turn behind (`0x4f9f00`), one unit up, lowered by twice the distance
between that point and the guest in world units (ten per cell), and bobbing through a 20-entry table (0 to 0.2, scaled by 1.5). A
balloon that bursts (its life ends, a prank, a guard's catch) shows its burst frame for a moment; one put away for a ride just goes.
GUEST-017 lists what is approximated (the colour stream, the bob's pace, the burst's length and fade). The developer option
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

Approximations ([HEADS register](FIDELITY-REGISTER.md)): the slot draw uses the original formula, `labs(worldRand() >>> 1) % length` (tp.exe `0x554CB5`), on a per-ride stream, not the shared world generator `0x516330`, which rotates its state right by 13 after the LCG step (`s * 1664525 + 1013904223`, `ror 13`, absolute value) and is not ported (HEADS-001, depends on DET-013; the record-to-node link, the per-script allocation and the table-length rule, formerly HEADS-002/003, are traced and retired). The remaining head presentation uncertainty (HEADS-004): how sprite scale 1.0 maps to world units (the body's pixel scale times 0.685 is used), depth handling, the azimuth origin and sense of column 0 (local +X towards +Z is used), the reference frame of the roll (the node matrix in world axes), and costume heads (guests wear none). A child or sound script has no head table at all on the PC (it is created with thing id 0, which skips the table allocation, and copies the parent's thing id only afterwards; ADDHEAD and DELHEAD exit on the NULL table, tp.exe `0x558D2B`, `0x554C89`, `0x554D6F`), and OpenTPW does the same: their head opcodes do nothing (HEADS-005 retired, October 14, 2026); no associated child/sound script in this shipped corpus uses a head command.

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

### Catalogue upgrade defaults

Ordinary scripted Theme Park World rides now resolve the exact SAM `Upgrades[L]` row of their mapped economy instance. Fresh/imported construction seeds that tier before execution. Completion updates catalogue queue metadata and fallback capacity only; it leaves all current VM variables, flags, waits, rider/queue lists and head slots intact. A positive current `VAR_CAPACITY` remains authoritative for admission, so CAP1 intentionally stays1 even when the tier's fallback default becomes3. `InitSpeed` of the tier a ride is created at (or seeded to before execution) is also its script speed bias (`RideVM.ScriptSpeed` = 0.5 + bias / 100, RIDES-033); a live completion sets the bias to the new level's `InitSpeed` when above 0 (tp.exe 0x4DF8F0, RIDES-033 round 2) and leaves the animations and controller coefficients alone. This is the conservative host contract qualified by QUEUE-011, not native reset or full performance parity.

Whole-park resume already saves the economy's `Level`; staged binding derives the tier before runtime/script-world/guest restoration. Saved VM values overwrite fresh seeds afterward. No new schema or write-provenance state is added. Exact instance/object identity, factory parameter-record ownership, sparse/missing rows, duplicate completions, stopped/removed owners and no-economy/read-only/TPI boundaries are checked. Nonzero imported/resumed tiers that cannot resolve a row fail reconstruction rather than use tier0. A legacy tier0 entry without its row reports an explicit refusal and keeps prior construction semantics. Coasters and developer prototypes keep their existing controllers and receive no catalogue-tier mutation. A custom public queue record cannot be overwritten, even when its values equal the factory record.

The entry-only queue factory keeps its level0 compatibility behavior. Tier-specific resolution uses exact row labels and existing raw seed/queue clamp semantics; no guessed duration units, sliders, refunds, physical seats are introduced by tier metadata binding. The separately qualified host purchase confirmation is documented in [ECONOMY.md](ECONOMY.md). Retained live operating values may differ from new catalogue defaults. A broader VM-to-queue projection or native upgrade reset needs a separate contract and evidence.

Catalogue-default snapshot eligibility is checked before capture or file writing. Ordinary bound scripted TPW rides must retain factory-owned queue metadata and match their completed economy tier. Custom or refused tier metadata is not repaired during saving. Matching stopped owners and live CAP values different from the fallback remain supported; legacy unbound tier0 defaults, including catalogues without row0, retain compatibility. No new save fields record write provenance.

Independent synthetic whole-park controls exercise the actual runtime/script-world/guest restore stage after economy-tier binding. Live CAP1 with fallback3 and script-authored CAP9/DUR77 survive that stage. Matching stopped rides preserve catalogue metadata and teardown; the existing stopped-runtime format omits their VM slots because their scripts are absent from the saved world, so those slots retain reconstructed defaults rather than the former stopped values. This does not widen the save schema or native upgrade qualification.

The All Attractions shop/sideshow business rows display the counters already maintained by guest payments and the economy month boundary. Actual synthetic HUD controls cover positive shop and negative sideshow profit, different attraction/economy IDs, same-type instance isolation, current versus previous month, existing save restoration, copied frame values, category/removal and unsupported binding transitions. Ride monthly users, remaining life and satisfaction stay unavailable in this slice. These controls qualify host projection, not original accounting or native UI behavior.
