# Theme Park Inc guests: the native state machine

Theme Park Inc's guests have their own state machine: 31 states (the word `+0x2C0`), a setter with an entry table and a dispatch. OpenTPW ports it
in phases (the plan is kept outside the repository). This note records what each phase ports, with the addresses in the no-CD `Game.exe`.

The port runs on the shared `Guest` records inside the shared `GuestSimulation` sweep, for the guests that carry a Theme Park Inc record
(`Guest.Tpi`, `ThemeParkIncGuestRecord`). The hidden switch `GuestSimulation.ThemeParkIncNativeGuests` turns it on, either from the command line
(`--tpi-native-guests`) or from a test. The switch is **off by default**. With it off, Theme Park Inc guests keep Theme Park World's rules
(`[EXT:tpi-twp-guest-rules]`), and Theme Park World and the default Theme Park Inc park behave exactly as before. Phase P4 turns the switch on by default.

The walk step, the route search, the wander and the speed stay Theme Park World's under `[EXT:tpi-twp-guest-rules]`, as for the staff.

## Files

| File | Contents |
|---|---|
| `World/Guests/ThemeParkInc/ThemeParkIncGuestStates.cs` | state numbers, the name table, the catch test, the queue predicate, the entry modes, the projection onto `GuestState` |
| `World/Guests/ThemeParkInc/ThemeParkIncGuestRecord.cs` | the fields of the constructor `0x60C3F0` that `Guest` does not hold |
| `World/Guests/ThemeParkInc/ThemeParkIncGuestSettings.cs` | the Standard words the states read, by address |
| `World/Guests/ThemeParkInc/GuestSimulation.ThemeParkInc.cs` | the setter, the dispatch, the pre-update, the weather rule, the thought pick, the constructor's tail, the P1 states |
| `World/Guests/ThemeParkInc/GuestSimulation.ThemeParkIncArrivals.cs` | the arrival guest, the vehicle dispatch, the pick-up test |
| `World/Guests/ThemeParkInc/GuestSimulation.ThemeParkIncIdle.cs` | phase P2: idle (state 6), the spot animation, the bin search, the wander (7), the walk to a bin (9), the removal message |
| `World/Guests/ThemeParkInc/GuestSimulation.ThemeParkIncQueues.cs` | phase P3a: the walk to a ride (13), the queue (14), the queue step (15), the queue functions, the ride's calls and the bridged boarding (16) |
| `World/Guests/ThemeParkInc/GuestSimulation.ThemeParkIncDecisions.cs` | phase P3a: the major decision `0x610080`, the minor decision `0x6114B0`, the score's inputs |
| `World/Guests/ThemeParkInc/ThemeParkIncAttractionScore.cs` | the attraction score `0x6101E0` (pure), the x87 single rounding |
| `World/Guests/ThemeParkInc/IThemeParkIncGuestWorld.cs` | what the native guests read of the park: the object list's bins, shows and attractions, a thing by id, the entertainers |
| `World/Level.ThemeParkIncGuests.cs` | the level's side of that view, built once per tick, and the removal message's hook |
| `World/Guests/ThemeParkInc/ThemeParkIncGuestSnapshot.cs` | the save block (guest snapshot schema 10) |

## The tick

The kind switch `0x6375E0` (kind 1) calls the pre-update `0x619B50` and then the dispatch `0x619FF0`, every turn, for every guest, inside the thing
sweep (`Level.RunThingSweep` → `GuestSimulation.SweepGuest`). Between two turns a native guest runs nothing but its drawn point.

**Pre-update `0x619B50`.**
1. The speed average `0x60BC90`. This is Theme Park World's `0x004FA870` (`GuestSpeed.Update`).
2. On the guest's phase only, where `(id & 3) == (turn & 3)`:
   - the weather rule `0x611050` (see below);
   - the exit counter `+0x1E0` goes down by 1;
   - the region record's words `+0`, `+2` and `+4` (`0x5D7420`) are added to happiness, illness and hunger;
   - on a turn whose low four bits are 0, toilet grows by 1 and hunger and thirst by 2;
   - an illness, hunger or thirst byte of 100 costs 1 happiness each;
   - a toilet byte above 80 costs 1 happiness and sets the speed term to 25, unless an umbrella is up (`0x60B3F0`); a byte of 100 costs 1 more, and
     a byte of 95 asks the thought object for thought 4;
   - outside states 0x14 and 0x15 and on a park cell (`0x60BE00`): one world draw, and `R % 10 == 0` runs the thought pick `0x60FC60`. The balloon
     countdown `+0x298` runs under the same test, so a guest on an object, a held guest or a guest outside the park keeps its balloon's time;
   - the thought object `0x6388F0`.
3. Every turn: outside the queue states 14..17 the queue words `+0x2CC`/`+0x2CE` are cleared (`0x619EC3`).
4. Every 20th turn the history shifts (`0x611D60` → `0x611D20`). This shift is bug-compatible: its dword copy overlaps, so `[a, b, c, d]`
   becomes `[0, a, b, b]`. Theme Park World keeps `c`.

The oracle found the overlapping copy and the balloon countdown's place in the branch.

**Thought pick `0x60FC60`.** Hunger and thirst bytes both above 90 give 3. Otherwise the first match of this list wins:

| Test | Thought |
|---|---|
| toilet above `ThoughtBubbleNeedTheLoo` | 4 |
| hunger above `Hungry` | 1 |
| thirst above `Thirsty` | 2 |
| illness above `Sick` | 14 |
| the block's littered share above `Mess` (a float) | 7 |
| happiness above `Happy` | 9 |
| happiness below `Unhappy` | 11 |

Thought 4 is voiced (`0x85`) for one thing id in sixteen. Two parts run by Theme Park World's rules under `[EXT:tpi-twp-guest-rules]`:
- the block census, through Theme Park World's analyser blocks;
- the bubble timing of the thought object `0x638960`.

**Weather rule `0x611050`.**
- It applies only on a day the guest's age (`((turn − +0x1FC) >> 2) << 2`, in game days) makes a multiple of `WeatherModifierFrequency`. On that day the day goes into
  `+0x2E8`.
- The generation row (`+0x2A4`) of the weather (1 heat wave .. 4 snow) changes happiness, hunger and thirst. The heat wave counts too.
- The umbrella step: in fair weather and the heat wave a pending close is carried out, a pending open is cancelled and an open umbrella is scheduled
  to close. In bad weather a pending close is cancelled, a closed umbrella is scheduled to open and a pending open is carried out. While the umbrella
  is open, `UmbrellaModifier` softens a negative happiness delta.
- The schedule `0x613EA0` draws its delay with the CRT `rand()` at `0x613F18`. That draw is a world draw, `world.Next(SlowestWeatherChangeResponse)`,
  under the widened `[EXT:tpi-staff-crt-rand]` (user decision of 2026-11-22). A held balloon stretches the delay (`UmbrellaRules.StretchDelay`).

## Setter and dispatch

The setter `0x61A540` stores the word, then runs the entry of the table `0x61AA54`:
- the animation mode `+0x20`, through `0x5D0F90`;
- 3: the wait `+0x228 = R % 150 + 200`;
- 4: at a booth's cell, the booth's `Angle` as the heading and mode 3;
- 8: the turn into `+0x230`;
- 14: `+0x224` and `+0x21C = _ftol(+0x21B × 1.2f)`;
- 15: the speed term 0;
- 22: the target `+0x204` cleared;
- 27: `+0x2B4 = 100`;
- 30: a paid guest's stay goes into the analyser's stay ring.

- 7 (phase P2): with the wander flag `+0xE8` set, the walk to the target words `+0x28`/`+0x2A` again (`0x60B8E0`).
- 13 and 16 (phase P3a): the mode only.

The entries of 17, 19, 20, 21 and 26 read objects of the later phases and are not run yet.

The projection (`ThemeParkIncGuestStates.Projection`) writes `Guest.State` for the readers outside the simulation, by role and never by number. For
example, TPI 18 waits at the infodesk while TPW 18 is the leaving state. The guard's catch test `0x614720` catches 6..0xD, 0x16 and any word
outside the table; `IGuardGuests.IsCatchable` asks it for a native guest. The dispatch `0x619FF0` uses the table `0x61A3A8`.

## Phase P1: arrival and leaving

**Constructor tail `0x60C9F5..0x60CB7F`.**
- On a park cell (`0x60BE00`): the visitor number (`0x650560`, the park's count plus one) and state 6.
- Otherwise:
  - two world draws, R10 (`0x60CA29`) and R11 (`0x60CA44`), whose results are never read;
  - the walk to booth `id % NumTicketBooths`; a refused route flips bit 0 of the index;
  - state 2 and the arrival beam.

**The arrival update `0x5BCB10`** is Theme Park World's `0x004CF3E0` step for step, with three differences:
- The guest is built at the stop of the vehicle at work (`0x5D79F0`: BusStopA for the bus or none, BusStopB for the heli, BusStopC for the mono).
- The dispatch `0x64FB90` destroys every cached vehicle (the bus, the heli, then the mono) and builds a new one: size 1, 2 or 3 picks the bus, the heli or the
  mono, and size 0 a world draw `% 3`. This moves the thing ids. A missing feature is retried in the order mono, heli, bus.
- The pick-up test `0x6503F0` reads only the head of BusStopA's thing chain, which must be a guest in state 30.

The vehicles are the features `bus`, `heli` and `mono`. On the switch-on path the level gives the guests these vehicles, and no Bus is placed at setup
(`[EXT:tpi-bus-from-setup]` holds only on the default path).

**The states.**

| State | Handler | Behaviour |
|---|---|---|
| 0 | `0x61A05C` | walk; arrived: heading 0x400, state 1 (unreachable: nothing writes 0) |
| 1 | `0x61A082` | once the crossing gate `0x6501D0` allows: a booth from a draw, a point in it from two more, state 2 (unreachable) |
| 2 | `0x615A80` | the speed term (50 while the bus waits at status 3, else 25 for one id in four), the walk; arrived: 4 with the park open, 3 closed |
| 3 | `0x615B40` | closed or the gate shut: the wait counts down, and at 0 the walk to the bus stop and state 22. Open and paid, as the head of its cell's list: an entrance from a draw, state 5. Open and unpaid: the booth scan `0x615BEB` (only `id % N` and its `^ 1` partner are compared, bug-compatible), state 2 |
| 4 | `0x615E90` | the verdict `0x6157D0` (`BoothVerdict`'s formula over Theme Park Inc's words; 24-bit products, exact below 2^24); see the list below |
| 5 | `0x616790` | closed or the gate shut: the booth block `0x6168D6` (it compares the cell with booth k but routes to booth i, bug-compatible), state 3. Open: the walk; arrived: the visitor number, state 6 |
| 8 | `0x61A382` | the saved state comes back once the turn is past the stamp plus 10 (unreachable in P1) |
| 22 | `0x618620` | the recall "Make up your mind!" (exit counter above 0, happiness byte nonzero, park open, on a park cell) gives state 6 and walk mask 0. Otherwise the walk: arrived, heading 0x7FF (heli or mono) or 0x400 and state 30; stuck, the bus stop again |
| 23 | `0x61A307` | state 6 |
| 27 | `0x614830` | 101 waits, then state 6 |
| 28, 29 | `0x61A3A0` | nothing (unreachable) |
| 30 | `0x618790` | status 4: the guest leaves (beam, destroyed). Status 1..3, off BusStopA and its +X neighbour: one draw for the cell, two for the point, the walk, state 22 |

The verdict cases of state 4:
- far too expensive: the walk to the bus stop, thought 6, state 22;
- expensive: wait `50 + R % 50`, thought 6, minus `MediumHappinessChange`, and at a happiness byte of 0 the bus stop;
- cheap: plus `MediumHappinessChange`;
- cheap and fair: the fee, `+0x22C = 1`, `+0x1C4 += fee`, state 3. The fee is booked with no refusal, as `0x5C0E10` books it
  (`ParkEconomy.AdmitThemeParkIncVisitor`; Theme Park World's booking refuses a bankrupt park), and `+0x1C4` adds the fee booked.

**Walk to the bus stop `0x615D50`.**
- Two world draws pick a point anywhere in the BusStopA cell, and the route request `0x60B8E0` asks for the walk there.
- Every caller retries once with walk mask `+0x1A0 = 1`. A refused first route therefore costs four draws.

`0x60B7F0` and `0x60B8E0` are byte for byte Theme Park World's `0x004FA530` and `0x004FA5F0`. Both have the stranded gate (`RequestRoute`,
`RouteToTpiPoint`).

**State 6** ran a stub in P1 (only its leave gate); phase P2 ports the whole handler (below).

**Not ported, gated under `[EXT:tpi-capability-gates]`:**
- the tutorial tips of state 5 (advice 0x207 and 0x208 in the first `FirstHour.Minutes`, `0x650890`);
- state 30's mark of a prankster in the tag system (`0x636B10`, phase P4);
- the balloon's litter when an umbrella opens (the types 9, 0xC, 0xB and 0xA of `0x9D0614`; no native guest holds a balloon before phase P3).

## Phase P2: idle, wander, spot animations, litter

**State 6 `0x614A40`.** One world draw A comes first; then, in order:
1. **Mood.** The turn more than 100 past the spot stamp `+0x230`, a happiness byte above 80 and the umbrella down (`+0x238` 0 or 2): the mood animation `0x60CDD0` (a kid 5, a
   parent 6 female or 7 male, a grandparent 8 or 9; the gender is the sprite bank's byte `+0x206`, the ESP header's byte `0x10D`) through the spot animation, and the update ends.
2. **Vomit.** An illness byte of 100 and `A % 3 == 0`: the spot animation 0xC, vomit (litter 7) on the cell, the sound 0xCC, illness 0, and the update ends.
3. **Litter urge.** A litter byte of 90 or more: draw B, and with `B & 3 == 0` the bin search `0x618D10` of radius 3 (a bin: the target `+0x204`, state 9, end); else draw C and,
   on a cell without litter, litter `C % 5 + 1` and the urge 0.
4. **Leave gate.** As in P1, but the handler goes on (bug-compatible): a later step can overwrite state 22 in the same update.
5. **Show.** The nearest show (`UsageInfo.IsFireworks`, the thing flag `+0x42` bit 0x80) within a squared distance of 4 whose script variable 0 is 0 (`0x6193B0`): the heading
   `0x91E074(−dx, dy)` unless `dx + dy == 0`, and the update ends.
6. **Entertainer reaction `0x614D41`.** An entertainer in the 3 × 3 cells (`0x5B1B20`, kind 6, radius 1) and the nearest one of the entertainers' list (`0x619100`) performing
   (`+0x1B4 == 0x17`): the heading towards him and `+0x1B8 = 1`, and the update ends; nothing else changes. On the diagonal (`dx + dy == 0`) `+0x1B8 = 0` and the update goes on.
   The staff world gives the list (`ThemeParkIncStaffWorld.Entertainers`); this closes the gate the entertainer's port left.
7. **Prank gate `0x614E56`.** A happiness byte below `PrankHappinessThreshold`, `+0x29C` clear and `A % 100` below `PrankLikelihood` (or the one-shot `+0x2B1`, always 0 in
   retail): `+0x2B1 = 0`, then the switch `0x615414` on the prank code − 1:
   - 0 (code 1): `A % 100` below `StinkbombLikelihood`: a stinkbomb (litter 8), the sound 0xCF and the tail;
   - 1 (code 2): the toilet meter 80 and `+0x29C = 1`, which shuts the gate for good;
   - 2 (code 3): the bin search of radius 6 (its route requests included); a bin whose `VAR_PRANKED` (10) is 0 becomes the target `+0x204` and the update ends (`0x61510A`), state
     10's entry gated (below); no bin goes on;
   - 4 (code 5, which no birth gives): litter `A % 5 + 1`, the code 0, state 7 and the tail;
   - 6 and 7 (codes 7 and 8, 16 of the 20 adult entries of the prank table): the stand animation 3 (`0x60B6E0`) and the update ends (`0x6151C8`, `0x615188`) with no draw and
     no decision; the entries of 25 and 24 and their words are gated (below);
   - 3 and 5: gated (below).

   The tail `0x614F14` posts the prank message (`ThemeParkIncPrankPosted`: kind 1, 5 or 6 and the guest's id) and adds 1 to happiness; the update goes on.
8. **Decision on `A & 3` `0x61523F`.**
   - 0 and 2: after 30 turns past `+0x224`, the major decision `0x610080` (phase P3a); a ride chosen sets state 13; with none the can't-decide pause (spot animation 4, or 3 with the
     umbrella up), `SmallHappinessChange` and the stamp.
   - 1: the wander destination `0x609F70`; found: state 7, else the stamp.
   - 3: `rand() < 0x3FFF`, and when it passes a second one, towards another guest or an infodesk. Both are world draws `world.Next(0x8000) < 0x3FFF` under `[EXT:tpi-staff-crt-rand]`
     (the user's decision of 2026-11-22, the reduction decided 2026-11-23).

**Spot animation `0x60FB30`.** The sound for one thing id in three (4: 0x7E, 5..9: 0x4D, 10: 0xD5), the animation on the sprite (`+0x20`), the state into `+0x2C4`, the turn into
`+0x230` and state 8, which restores the saved state ten turns later.

**State 7 `0x61A179`.** The walk; arrived or stuck, a world draw with `G & 3 == 0` and a new wander destination keep the guest wandering, anything else makes it idle. The wander
destination is Theme Park World's (`[EXT:tpi-twp-guest-rules]`); a walk it sets raises the wander flag `+0xE8`, which state 7's entry reads.

**State 9 `0x616F10`.** The walk. Arrived at a bin: the bin counts a customer (`0x5E5DA0`); unless its `VAR_CONTENTS` (4) reaches `VAR_CAPACITY` (3) or `VAR_ONFIRE` (5) is set,
its `VAR_USE` (0) becomes 1. Otherwise (no bin, a full or burning bin, a stuck walk): a draw and litter `D % 5 + 1` on the cell. The urge is emptied and the guest is idle.

**Bin search `0x618D10`.** The object list's bins (`+0x42` bit 0x40), the nearest below the radius whose `VAR_PRANKED` (10) and `VAR_ONFIRE` (5) are 0 and that the route
request to its stand point reaches. The stand point (`0x5E1300`) is `EntryCellStandPosX/Y` in the entrance cell `+0x46`, turned with the object and times 255.0f at 24 bits. Every
closer bin makes its own route request, so a failed request after a found bin leaves the walk on the failed one.

**Removal message `0x60D120`.** A destroyed attraction thing (message 0xA, at the click): every native guest whose target `+0x204` it is loses `SmallHappinessChange` and its target
and becomes idle, whatever its state; `+0x206` and the histories forget it.

**Freeze `0x60FF40`.** Its only caller (through the thunk `0x407815`, at `0x50467C` and `0x5047F7`) is `0x503FD0`, a UI input handler of the park view: it reads the shift,
control and alt keys (`GetAsyncKeyState`) and freezes the person it keeps in `[0xA568F4]` when that is a guest (kind 1) whose cell is the cell `[0xA43494]`, then shows its info
text. The freeze itself: not in the queue states (`0x61AC40`), on a park cell (`0x60BE00`) and outside the states 0x13, 0x14 and 0x1B, the old state goes into `+0x2C4` (unless it
is 23), state 23 and the heading 0. It is UI only and is not ported in P2; state 23's handler (P1) is.

**Gated in P2** (`[EXT:tpi-capability-gates]`; none of them draws):
- the prank switch's state entries: 10 after a bin is found (the walk to the bin for the prank `0x617140`, phase P4: the guest keeps its target and walk and stays idle), and 25 and 24
  after rumours or whinging (phase P5: the guest stays idle, and the words `+0x1E8` = `WhingeDuration` and `+0x2B0` = 0, which only those states read, are not held; the thought 0x12
  is not shown). The update ends where the original's does, so the draws match;
- case 3, the plant search `0x618EF0` (with the fire test `0x618F30`, phase P4), is read as finding no plant: a prankster of code 4 within six cells of a plant goes on to the decision
  where the original's update ends (state 11), so the draws differ there;
- case 5 finds no victim (a guest on the cell holding a balloon, `0x613D00` with litter): exact while no native guest holds a balloon, which none can before phase P3; a restore refuses a
  block that holds one. In phase P3a a balloon bought in the bridged boarding is Theme Park World's (the shared record's balloon), so the native words `+0x294`/`+0x298` still never
  hold one; phase P3b's visit writes them;
- the walk to another guest (`0x619240`) and the infodesk (`0x618B60`, state 12), phase P5;
- the prank thought 0x15 ("I pranked", held 120 turns): the bubbles are Theme Park World's, which has no such thought;
- the removal message's ride and queue branches. Phase P3a ports the queue branch and the major decision's ride scan (below); the ride branch (state 0x14) is the bridged boarding's.

`[EXT:tpi-no-maintenance-wear]` keeps the bins' fill: a dirty bin's RegionFX row change (`0x5E3990`, rows 8 and 9 through `0x5D74D0`/`0x5D74A0`) is not made, and the constructor puts
no row 8 down either. The script itself runs, so `VAR_USE` reaches it.

## Phase P3a: the ride choice, the walk to a ride, the queue

**The queue list.** Theme Park Inc's ride keeps its queue as a list through the guests' words `+0x2CC` (next) and `+0x2CE` (previous), its head at `+0x4C`. The functions are
Theme Park World's instruction for instruction, the field offsets aside, so the list is the ride bridge's (`RideVisitorBridge`), and the native guests' two words follow it:

| Function | Theme Park World | What it does |
|---|---|---|
| `0x5DFF50` | `0x004DDB90` | appends the guest (`AppendToQueue`) |
| `0x5E0100` | `0x004DDD20` | unlinks it; the script's `VAR_LETMEON` naming it becomes 0 (`LeaveQueue`) |
| `0x5E0280` | `0x004DDF50` | the guest's place, −1 when the walk from the head does not reach it; with id 0 the length. The walk stops at a guest outside the queue states (`0x61AC40`), which it still counts (`TpiQueuePosition`) |
| `0x5DFDD0` | `0x004DDA20` | room: the length below 4 × the cells `+0x50` |
| `0x5DFE00` | `0x004DDA40` | the limit: 100 without `Info.HasQueue`, else `_ftol(max(QWTC × capacity × g / duration, 4.0))`, g the speed over InitSpeed. Theme Park Inc multiplies in this order, at 24 bits (`ThemeParkIncQueueLimit`) |
| `0x5DFF10` | `0x004DDB60` | too long: the length at least the limit |
| `0x5E0520` | `0x004DE110` | the back-of-queue cell word |
| `0x5E0C70` | `0x004DE7E0` | a place's queue slot, one world draw for the sideways byte (`QueueSlotPoint`) |

`+0x21B` has two uses, one after the other: in states 14 to 16 it is the guest's place (FindQueueDestination `0x619530` writes it, state 14 compares it with `0x5E0280`, and its entry
sets `+0x21C = _ftol(1.2f × +0x21B)`); state 17's entry then overwrites it with the ride's roll `0x5E7A00`, which the end of the visit reads. Theme Park World's `+0x1F1` does the
same (a sideshow's play, `0x004E2670`).

**FindQueueDestination `0x619530`** stores the place in `+0x21B`, takes the slot and asks for the walk there (`0x60B8E0`); a walk sets state 15. **The dismissal `0x619700`**
takes an argument: 0 plays the sound 0x80 for one thing id in eight and costs `MediumHappinessChange`, 1 neither. It clears `+0x2CC`, `+0x2CE`, `+0x28C`, `+0x290`, `+0x220`,
`+0x204` and `+0x21B` and sets state 6. **The walk-away `0x5E5D70`** raises the ride's walk-away count (`mNumWalkAways`, the economy's `RecordWalkAway`); Theme Park Inc calls it on
every queue exit of state 14 and on state 13's refusal of a long queue, where Theme Park World calls it only for a refused price.

**The score `0x6101E0`** is Theme Park World's (`0x004FCC30`, `AttractionScore`) with these differences (`ThemeParkIncAttractionScore`):
- gates that score 0: a special ride (the save's table `0xAA0420`, `0x6242F0`); `Info.GenderSpecific` 1 or 2 against the sprite bank's gender; an umbrella shop (`AppearanceEffect` 3)
  for a guest with an umbrella or in weather other than rain, storm and snow; a balloon shop (1) for a guest with a balloon or an umbrella state 3 or 4; the same type as used-history slot 0
  (`0x61035F..0x61036E`: the type word `+0x16` of whatever live thing the slot `0xAA18B0` holds for that id, read through `IThemeParkIncGuestWorld.ThingTypeId`, the level's live objects);
- the weights are `PeepInfo.DecisionVar*Weight` (`0xA01AC4..0xA01ADC`); the queue's weight is read whole, the others' low 16 bits;
- a new ride (younger than `DecisionVariable1` days) scores `DecisionVariable2` times; `DecisionVariable3` (indoors in rain) is unread;
- the golden-ticket scale `(cost + 1) × 0.1 + 1.0` and the upgrade scale `q × 0.1f + 1.0f` round every x87 step to a single (24 bits);
- then the weather's `WeatherModifier.*Percentage` and the guest generation's `GenerationModifier.*ScorePercentage` (signed, over 100), and an umbrella shop's balloon factor
  `100 − 100 × countdown / 255` (times `DiscardBalloonPctModifier` / 100);
- the history divisions are the same.

**The major decision `0x610080`** (state 6, `A & 3` 0 or 2) clears the target and walks the object list newest first: a choosable thing (`+0x42` bit 4) with a back cell and a score of
at least 10 that beats the best (a tie on an odd turn) wins when it passes the counts test `0x5DFC90` (open, not broken down or condemned, room, the coaster and track words) and the
route request to its back cell succeeds. A chosen ride sets state 13; none gives the can't-decide pause of phase P2.

**The minor decision `0x6114B0`** is tp.exe's `0x004FD570` over Theme Park Inc's score: the 4 × 4 cells round the guest nearer to the target's back cell, the best above 0, the route
lengths from the target's access cell; it keeps the old target in `+0x206`.

**State 13 `0x616AC0`, the walk to the ride.** The walk step comes first. A closed ride (the open word `+0x7C`, which Theme Park World's state 10 does not read) or a stuck walk costs
`BigHappinessChange` and makes the guest idle. Arrived:
- the back cell must be the guest's own, else the walk there again or idle;
- no room: idle, the target kept;
- the excitement difference `0x611410` (tp.exe's `0x004FD4E0` with the guest's `+0x2A8`) of at least 45 with the type's `ExcitementLevel` set: thought 12 or 15, the target into the
  temporary history (`0x611D20`), `+0x204` and `+0x224` cleared, idle;
- too long: the history, thought 16, idle and the walk-away;
- else `+0x234` takes the happiness, the guest joins and FindQueueDestination sends it to its place (state 15); no walk: out, the dismissal (0), idle.

While walking: a closed park costs `BigHappinessChange` and ends the walk; a balloon shop with the umbrella state 3 or 4 and an umbrella shop in fair weather end it with no happiness
step; every twelfth update makes a minor decision.

**State 14 `0x6176C0`, the queue.**
- No target: idle.
- The head (`+0x21B` 0) with the call `+0x220`: the ride's loading slot naming it clears the call and asks for the walk to the stand point (`0x5E1440`); a walk sets 16, none sends it
  away. A loading slot naming another guest sends it away with no thought (Theme Park World's state 11 waits instead).
- A worn relief object (`0x5E3990`): thought 14 and away.
- An umbrella shop in weather other than rain, storm and snow: a heat wave sends the guest away quietly (`0x619700(1)`); other weather arms the delayed reaction once,
  `+0x290 = rand() % SlowestWeatherChangeResponse + (turn >> 2)` and `+0x28C = 1`, and sends it away quietly when the turn's quarter reaches it. The CRT `rand()` at `0x61795E` is a
  world draw, `world.Next(SlowestWeatherChangeResponse)`, under the widened `[EXT:tpi-staff-crt-rand]` (user decision of 2026-11-22).
- The place: not found, away; changed, the move delay `+0x21C` counts down while the gap is at most 2, a broken-down ride holds, else FindQueueDestination or away; unchanged, a place
  beyond the limit leaves with thought 16 and a refusing coaster or track with thought 13.
- More than 30 turns past the spot stamp `+0x230`, the happiness bands: above 80 the mood animation (umbrella down), below 10 thought 11 and away, below 20 the can't-decide animation
  (umbrella down), else a toilet byte above 80 thinks 4 and leaves unless the ride gives relief.
- Within those 30 turns: 100 turns past `+0x224` leave bored (thought 12); else one world draw in ten turns the head by a second, `+0x2C += R % 800 − 400`, wrapped (`0x617CF1`,
  `0x617D0C`).

Every exit away from the queue is `0x5E0100`, `0x619700(0)`, the walk-away and state 6.

**State 15 `0x61A1DB`, the queue step.** The walk; arrived or stuck, state 14.

**The ride's side.** The bridge's admission calls a native guest when it stands at the front (`0x619640`: state 14, place 0) through OnAdmittance `0x619670` (`+0x220 = 1`). A closed or
broken ride's head step `0x5E3CC0` sends the head away with the dismissal (0). The queue edit's re-evaluation `0x6197F0` sends a guest whose place the room no longer holds away (thought 13
for one thing id in three, then the dismissal), outside state 17. The removal message `0x60D120` now sends a queued guest away with the dismissal (0) before the small step.

**The bridged boarding (state 16 until phase P3b).** States 16, 17, 19 and 20 and the ride's control of the guest are phase P3b's. A guest the ride calls walks to the stand point and enters
16 as in the original; from there it walks, pays, boards, rides and leaves by Theme Park World's boarding (`UpdateQueueWalk`, the bridge's admission and dismissal, `[EXT:tpi-twp-guest-rules]`),
keeping the state word 16 throughout. When that ends the guest is idle with no target (or back at its queue place, state 15, when the ride refused it at the stand point). The bridged
update first runs on the update after the one that set 16, as the original's dispatch of 16 does. Every world draw up to the setter of 16 is the original's, and state 16 itself draws
nothing. After that the bridged boarding's world draws are known to differ from the original's:
- state 17's entry roll `0x5E7A00` is missing: one world draw per boarding (`0x5E7A2D`, `R % 100` against the ride's byte `+0x90`, through the game object's second pointer `0xAA1484`);
- the pre-update's thought draw and balloon countdown (`0x619E63`) still run in Theme Park World's boarding, waiting and exit states, as the original's 16, 17 and 19 do; while Theme
  Park World's ride (Using) projects state 20 they are skipped, as the original skips them in 20 (`0x619E6A`);
- Theme Park World's balloon give-back at the end of a use reseeds the world generator with the guest's id and takes two draws (FinishUse, tp.exe `0x00502053`), and a balloon
  shop's appearance step does the same (ApplyAppearance, tp.exe `0x004FE6FC`); Theme Park Inc has neither;
- a won sideshow's tail takes one more draw whose value nothing reads (tp.exe `0x004FE8D9`);
- Theme Park World's small-portion tests (one draw each, tp.exe `0x004FE44E`) stand in for Theme Park Inc's hunger and thirst draws `0x612AEC` and `0x612BCF`, which are taken at a
  different moment of the use.

The used history is the original's: the end of a bridged use pushes the thing id into `+0x20A` with the bug-compatible shift (`0x6118E9`..`0x611906`), not Theme Park World's
attraction id, and Theme Park World's decision hook does not run for a native guest. The fare's happiness term reads the happiness at the join (`+0x234`, `0x6119A6`), which the
join stores.

**Gated in P3a** (`[EXT:tpi-capability-gates]`):
- the information desk scores 0 (its target word `+0x140` is the desk's own, phase P5; exact while no desk names a ride);
- the attraction history's ring 10 (`+0x550`), which state 13's refusal of a long queue and state 14's limit raise, is not kept (no reader; SAVE-940);
- OnAdmittance's fee word `+0x1B4` is not kept: the bridged boarding books the fee by Theme Park World's boarding (ECON-062).

## Save

Guest snapshot schema 10 adds an optional block `GuestSnapshot.Tpi` (`ThemeParkIncGuestSnapshot`) with every field of the record. A guest without it
writes nothing new, so a save made with the switch off differs from schema 9 only by its version number. The block joins the state hashes only on a
native guest.

The snapshot also records whether the park ran the native machine (`GuestSimulationSnapshot.ThemeParkIncNativeGuests`). A restore refuses a save whose
value differs from the running simulation's:
- a save with native blocks into a park running with the switch off (its guests would need the native machine);
- a schema 9 save, or a schema 10 save made with the switch off, into a Theme Park Inc park running with it on (its guests run Theme Park World's rules).

A restore also refuses a native guest in a state this build cannot run (10..12, 17..21 and 24..26, or a spot animation 8 that returns to one), so a save of a
later phase or an edited save never stops a guest's update mid-tick. Phase P3a runs 13 to 16; a native guest in a queue state (14..17, or 8 returning to one) must be in the list of the
ride it names and no other guest may be, and a guest of the bridged boarding is checked as Theme Park World's boarding guests are. Phase P3a adds no saved field: the queue words, the call,
the weather wait and the place are in phase P1's block. Phase P2 adds one optional member to the block, the wander flag `+0xE8` (`Wandered`; absent in a phase P1 save, whose
guests never wandered), without a new schema.

## Settings

`ThemeParkIncGuestSettings` reads the Standard object (`0xA01A60`, `ThemeParkIncStandard`) by address. It reads:
- the PeepInfo words the states use (`0xA01A78..0xA01B84`, with the prank words `StinkbombLikelihood`, `PrankHappinessThreshold` and `PrankLikelihood` at `0xA01B00..0xA01B08`);
- the three weather arrays (`0xA01B88`, `0xA01BBC`, `0xA01BF0`);
- FixedItemInfo's booths and stops (`0xA025B4..0xA025DC`, `TicketBooth[]` at `0xA02610`);
- `FirstHour.Minutes` (`0xA034D4`).

A test checks every address against its key through the SAM binding.

## Verification

- `tools/ppc-analysis/oracle_x86/tpi_guest_states_oracle.py` runs four routines natively at control word `0x7F` and compares them with the port: the catch test over -1..40, the setter's P1
  entries, the pre-update and the booth's verdict. Phase P2 adds state 6 (`0x614A40`, the searches stubbed), the spot animation `0x60FB30`, the mood `0x60CDD0`, the dispatch's states 7 and 8
  (`0x61A179`, `0x61A382`) and state 9 (`0x616F10`). Phase P3a adds the score `0x6101E0` (every gate, weight, scale and history division, the type and park records stubbed), the queue
  functions `0x5E0280`, `0x5DFDD0` and `0x5DFF10` with the limit `0x5DFE00` over random lists, and states 13 (`0x616AC0`), 14 (`0x6176C0`) and 15 (`0x61A1DB`) with their callees stubbed.
  No mismatch (see the oracle README).
- `ThemeParkIncNativeGuestTests`, `ThemeParkIncNativeIdleTests`, `ThemeParkIncNativeQueueTests` and `ThemeParkIncGuestSettingsTests` cover the rig behaviour, the draws, the saves and the
  shipped values; `ThemeParkIncPlayTests.NativeGuestsChooseQueueAndBoardTheScienceRides` runs native guests in the prebuilt Science park until they queue and ride;
  `ThemeParkIncPerformerTests` runs the entertainer reaction against a hired, placed and performing entertainer of the retail Science economy.

## Phases after P2

- P3b: being loaded (16), waiting for the script (17, the roll `0x5E7A00`), the ride's control (20, `0x61A77E`, `0x6182A0`) and being unloaded (19, the photo booth and the minor
  destination), which replace the bridged boarding; OnAdmittance's fee word; the information desk's part of the score with P5.
  A P3a save can hold a guest in the bridged boarding: state word 16 with Theme Park World's Boarding, WaitingToBoard, Using or ExitingRide (the snapshot validation accepts exactly these
  four with 16, and a mid-ride round trip is tested: `AGuestMidRideRoundTripsThroughTheSave`). P3b's restore maps such a guest, drawing nothing at the restore: Boarding (the walk to
  the stand point) stays 16; WaitingToBoard becomes 17 without its entry roll (`+0x21B` keeps the queue place the save holds, as the roll `0x5E7A00` never ran); Using becomes 20,
  under the ride's control; ExitingRide becomes 19. A guest in 16 with any other projection, or a mapping that the ride's own saved state does not support (no loading slot or rider
  that names the guest), is refused with the guest and its states named, as the other states this build cannot run are; it is never re-run or rerouted silently.
- P4: pranks, then the switch-over.
- P5: infodesk, whinging and rumours.
- P6: the guard's chase, carry and first aid.
