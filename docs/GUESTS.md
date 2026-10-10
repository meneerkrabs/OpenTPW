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
| `BankAccountInfo.InitialAdmissionFee` | Admission paid at the ticket booth (standalone only; with the park economy attached its entrance fee applies, see Money below) |
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
the state machine below. No executable constants were recovered from the
Windows `TP.ICD` string search. Separate static evidence from the identified
Feral Mac PowerPC application is recorded in [PPC-guests](reverse/PPC-guests.md).

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
| Walking speed | 1.0 cell/s (0.7× below 20 energy) |
| Arrival units | `TimeBetweenArrivals` read as tenths of a second (150 → a bus every 15 s) carrying `max(MinPeople, FixedRate)` guests, spawned 0.4 s apart, lanes A/B alternating; cap 1,000 guests |
| Needs | hunger +0.35/s, thirst +0.45/s, toilet +0.3/s, energy −0.15/s walking / +1/s resting or riding, nausea −0.2/s; thoughts from level 70 |
| Starting happiness | 60 of 100 |
| Decision | every 3 s: leave if ExitLevel ≤ 0 or happiness ≤ 10; else score = AttractionValue − DistWeight·distance − QueueWeight·queue + ExcitementWeight·(25 − |preferred − excitement|) + need terms (weight · need / 4) − IllnessWeight·nausea / 4 − AttractionValue if just ridden + random 0–9; best positive score wins, otherwise wander (random link, no U-turn) |
| Boredom | `BoredomThreshold` read as seconds since the last ride |
| Ride outcome | ExitLevel += positive happiness change |
| Queues | traced; see [Queues](#queues) and its `QUEUE-NNN` register |
| No exit lane | guests vanish (original ejection not modelled) |

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
and unloads all six through HOP/WALKOFF/WALKGET/`VAR_LETMEOFF`. (Measured before
the traced queue; guests now pass through the one-cell path queue at its
entrance, and the asset test still sees a full load before the 10 s time-out.)

## Queues

Traced from the Feral Mac binary in [QUEUE-plan](reverse/QUEUE-plan.md)
(sections cited below). Rules marked `[BIN:…]` at their code sites follow the
original; the rest are the `QUEUE-NNN` approximations listed at the end.

**Cells** (§3.3). A queue cell is original map cell type 3 with a link byte
(1, 4, 16 or 64) pointing back toward the ride entrance. `GuestPathGrid` keeps
them beside the path cells (`SetQueue`, `IsQueue`, `GetQueueLink`); they are
not walkable for routing. The **front cell** is the cell outside the object's
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
paths, at most 25 cells. Path cells, queue cells, object footprints and
terrain the object build rule refuses are blocked. Each cell costs
`Costs.QueueCell` (75). `QueuePaths.RemoveFrom` / `Level.RemoveQueueCell` remove
a cell and every cell behind it, without refund. After a HasQueue ride is
built from the HUD, park clicks lay its queue until Back (UI-031). Queue cells
also block object placement. New queue cells are not drawn on the terrain yet.

**Joining** (§4, state 10 at the join cell), in this order:

1. Room: the guest joins only while the count is below 4 × cells.
2. Excitement: rides and sideshows refuse a guest when
   |preferred − excitement| ≥ 45 (thought Scared or Bored).
3. Limit: the count must be below the data limit. That is 100 with
   `Info.HasQueue`. Otherwise it is
   trunc(max(QWTC × (SPEED/InitSpeed × CAP) / DUR, 4)) in single precision,
   with `Upgrades[L].QueueWaitTimeConstant` at type record +436 and InitSpeed
   at +424. `InitDuration` is +416, not +436. A NaN quotient gives 4.

So the maximum queue length is min(100, 4 × cells) for HasQueue rides. Objects
without a queue path (shops, toilets, sideshows) get one front cell and so 4.
CAP and DUR are the clamped `InitCapacity`/`InitDuration` of level 0. The old
OpenTPW rule "4 × capacity" is gone.

**Positions** (§3.4). Four positions per cell from the front. The depth along
the cell is trunc(255 × 0.25 × r) = 0, 63, 127 or 191 (of 255) from the edge
toward the previous cell. The sideways byte is 114 + (rand mod 28), drawn from
the simulation's seeded `GuestRandom` (the world-seeded stream guests already
use; `System.Random` is not used). Guests walk cell by cell along the queue to
their position (state 12).

**Standing** (§5, state 11, once per park turn = 248 ms of the fixed clock,
`ParkCalendar.Turn`). On entry the move delay is trunc(1.2 × position) turns.
Each turn, in order:

1. Called forward → walk to the stand point (state 13).
2. The ride failed (`VAR_BROKEN`) → leave.
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
queue edit.

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
  `Stalled` means gates held and the head was ready, yet nobody was called.
  `StalledAdmissionChecks` counts these. By construction it stays 0; the gate
  should verify that.
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
| QUEUE-001 | Link values 1/4/16/64 map to grid directions −Y/+X/+Y/−X | Run-time neighbour offset tables (data `0xec52c..0xec5a4`) |
| QUEUE-002 | The next queue cell is searched in grid direction order | Same tables; the order paired with links 16, 1, 64, 4 |
| QUEUE-003 | Guests step onto the back cell from its first walkable neighbour | `0xdd744` and the state-10 walk to the back cell |
| QUEUE-004 | Excitement gate for rides and sideshows, using \|preferred − excitement\| | `0xe02ec`, `0xe9a84` |
| QUEUE-005 | Queue-edit exits: beyond 4 × cells, or standing cell removed | `0xee8f8` in detail |
| QUEUE-006 | Depth from the cell's front edge; the 4th position stays in its cell | `0xdde74` and the sub-cell axis selection |
| QUEUE-007 | The stand point is the front cell's edge toward the ride | `0xde1d8` (`EntryCellStandPos`) |
| QUEUE-008 | Ride failure = `VAR_BROKEN ≠ 0` | Predicate `0xdfe34` |
| QUEUE-009 | Facing-change sign from the 1-in-10 draw | The sign selection in `0xed244` |
| QUEUE-010 | A guest leaving a queue reappears on the join cell at once | The state-6 transition after a queue exit |
| QUEUE-011 | Upgrade level 0 for QWTC/InitSpeed | None (OpenTPW does not apply upgrade levels to objects) |
| QUEUE-012 | Queue cells need allowed terrain and no object | The queue tool's validity checks |
| QUEUE-013 | At most 25 cells per queue | The queue tool (`0x70b98..0x8c7c0`) |
| QUEUE-014 | Laid cell by cell from the front, each touching the back | The queue tool's placement rules (UI-031) |
| QUEUE-015 | `Costs.QueueCell` per laid cell; no refund on removal | The queue tool's purchase path |
| QUEUE-016 | Admission and state 11 run once per park turn | Live-list eligibility of objects and guests |

Not modelled: the track-type exits of step 6 (`0x45eac`, ride `+40`), ride
state `+408`, the price check at the stand point (guests pay when boarding, see
Money), save/load of queues, and the import of the Easymode queue cells.

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

- `QueueTests` (no assets): queue-cell links and recompute after edits, build
  refusals, both limit formulas (single precision, NaN floor, HasQueue 100),
  join order room → excitement → limit, guests walking queue cells to four
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
  the original `Totem.RSE` (full load before 10 s, all riders released); the
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
heads, thought bubbles, balloons; save/load of guest state; which `PeepTypes`
entry uses which kid set.
