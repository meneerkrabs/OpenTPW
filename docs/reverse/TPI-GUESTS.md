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
| `World/Guests/ThemeParkInc/GuestSimulation.ThemeParkIncQueues.cs` | phase P3a: the walk to a ride (13), the queue (14), the queue step (15), the queue functions and the ride's calls |
| `World/Guests/ThemeParkInc/GuestSimulation.ThemeParkIncVisit.cs` | phase P3b: being loaded (16), the script's admission (17), the object's control (20), the exit, the use routine and its outcome, being unloaded (19) |
| `Economy/ParkEconomy.ThemeParkIncVisits.cs` | phase P3b: the economy's bookings of a native use (the income, the cost, the goods, the thing words) |
| `World/Guests/ThemeParkInc/GuestSimulation.ThemeParkIncDecisions.cs` | phase P3a: the major decision `0x610080`, the minor decision `0x6114B0`, the score's inputs |
| `World/Guests/ThemeParkInc/ThemeParkIncAttractionScore.cs` | the attraction score `0x6101E0` (pure), the x87 single rounding |
| `World/Guests/ThemeParkInc/IThemeParkIncGuestWorld.cs` | what the native guests read of the park: the object list's bins, shows and attractions, a thing by id and its type, the entertainers |
| `World/Level.ThemeParkIncGuests.cs` | the level's side of that view, rebuilt after a placement or a removal, and the removal message's hook |
| `World/Guests/ThemeParkInc/ThemeParkIncGuestSnapshot.cs` | the save block (an optional member of guest snapshot schema 9) |

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
- 17 (phase P3b, `0x61A66A`): mode 3 and the ride's chance roll `0x5E7A00` into `+0x21B` (below).
- 19 and 20 (phase P3b, `0x61A69D`, `0x61A77E`): below.

The entries of 21 and 26 read objects of the later phases and are not run yet.

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
| 4 | `0x615E90` | the verdict `0x6157D0` (`BoothVerdict`'s formula over Theme Park Inc's words; 24-bit products: the exact integer times the single-precision factor, rounded once to single precision as `fmuls` does at control word `0x7F`); see the list below |
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

**The balloon's litter** (phase P3b) drops wherever a held balloon (`+0x294`) goes with litter: the umbrella's carrying out (`0x61413A`, inline), state 20's entry at an umbrella shop
(`0x61A7D4`) and the balloon prank (`0x61507C`), the last two through `0x613D00(1)`. The type is the colour's (`+0x2A0`) entry of `0x9D0614`: 9, 0xC, 0xB and 0xA, Theme Park Inc's burst
balloons (`LitterType.BurstBalloon1..4`; the drop's table `0x5D8EFC` gives them the descriptors `0x9B0AD8..0x9B0B44`, its `SPR_LI` slots 8..11). It lies on the guest's own cell (the bytes
`+5`, `+7`) through the common drop `0x5D8C80` (`ParkLitter.Drop`): a cell that holds litter (`+0x30` nonzero) takes none and nothing is drawn; otherwise the world generator is reseeded with
the cell word (`0x5D8DCF`) and drawn twice (`0x5D8E14`, `0x5D8E42`). The countdown's release (`0x619EB6`) passes 0 and leaves no litter. A Theme Park Inc park's save holds the types 1..0xC.

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
- case 5 (the balloon prank) is ported in phase P3b, when the visit's balloon shop gives native guests balloons: the guest's cell's thing chain, head first, is searched for another guest
  holding a balloon (`+0x294` or `+0x298`); that guest loses it (`0x613D00(1)`, with its litter, above) and the tail runs with kind 6;
- the walk to another guest (`0x619240`) and the infodesk (`0x618B60`, state 12), phase P5;
- the prank thought 0x15 ("I pranked", held 120 turns): the bubbles are Theme Park World's, which has no such thought;
- the removal message's ride and queue branches. Phase P3a ports the queue branch and the major decision's ride scan (below); phase P3b ports the ride branch (state 0x14): the rider's
  model back (mode 1 when a ride that draws it kept it) and the sound 0x80, then the small step and idle like every other target.

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
- gates that score 0: a special ride awaiting its activation (`0x6101FD`: a thing `0x6242F0` does not free), read from the economy's special-ride table
  (`ParkEconomy.IsThemeParkIncSpecialRidePending` through `IGuestPayments.AwaitsActivation`), the same state the Theme Park World rules' HostScore and candidate test read; `Info.GenderSpecific` 1 or 2 against the sprite bank's gender; an umbrella shop (`AppearanceEffect` 3)
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

**State 16 onwards** is phase P3b's (below). Phase P3a ran it as a bridged boarding: the guest kept the state word 16 through Theme Park World's boarding, waiting, ride and exit states. A
phase P3a save of such a guest restores natively (see "Save").

**Gated in P3a** (`[EXT:tpi-capability-gates]`):
- the information desk scores 0 (its target word `+0x140` is the desk's own, phase P5; exact while no desk names a ride);
- the attraction history's ring 10 (`+0x550`), which state 13's refusal of a long queue and state 14's limit raise, is not kept (no reader; SAVE-940);
- OnAdmittance's fee word `+0x1B4` was not kept in phase P3a; phase P3b keeps it (below).

## Phase P3b: the visit

**OnAdmittance `0x619670`** now records the fee: `+0x1B4` = the thing's price `+0x94` (`0x5E6200`). Nothing clears it; the next call overwrites it.

**State 16 `0x617F80`, being loaded.** The walk to the stand point; a stuck walk (2) falls through as an arrival. Arrived:
1. the price test `0x6120C0` (`ThemeParkIncPriceTest`, X62) on the guest's own expected byte `+0x2A8`: too expensive is thought 6, `MediumHappinessChange`, the ride lets go of the
   admitted guest (`0x5E46A0`), `0x5E0100`, the dismissal `0x619700(0)` (a second medium step), the walk-away and state 6;
2. a weather reaction armed in the queue (`+0x28C` 1): a balloon (`+0x294`, `+0x298`) or a reaction due within `0.66 × SlowestWeatherChangeResponse` quarter turns (the unsigned difference
   `+0x290 − (turn >> 2)` against the single `fild × 0.66f` at 24 bits, not below) leaves quietly (`0x619700(1)`); else `+0x28C` and `+0x290` are cleared;
3. AdmitPerson `0x5E4420` (the bridge's `PresentForBoarding`: a broken or closed ride refuses, the loading slot is cleared, a busy VAR_LETMEON refuses, else it takes the id): state 17;
4. refused: FindQueueDestination `0x619530` (state 15), or "Couldn't rejoin FOQ even!": out of the list, the dismissal (0) and state 6.

**State 17 `0x61A21D`, waiting for the script.** The entry draws the ride's chance roll `0x5E7A00`: one world draw `R`, and `+0x21B` = 1 when the thing's win byte `+0x90` (100 −
`InitChanceOfLoosing`, so 100 for most attractions) is at least `R % 100`; the result also goes into the script's VAR_PARAM (variable 11). This overwrites the queue place `+0x21B` held. The
handler waits until the script has taken VAR_LETMEON (`0x5E4630`: VAR_LETMEON no longer the list's head), then the guest leaves the list (`0x5E0100`) and enters 20. A guest stays in the list
until then, so the bridge keeps a native guest's link when its script boards it (`IRideVisitorHost.KeepsBoardedGuestQueued`), and the ride's admission sees a head in 17, which is not at the front.
The ride's head step `0x5E3CC0` forces such a head onto the ride when VAR_LETMEON is no longer it (`0x6182A0`, `IRideVisitorHost.ForcesAdmittedHead`) instead of dismissing it.

**State 20 `0x61A292`, under the object's control,** does nothing. Its entry `0x61A77E`:
- a teleporting ride (`UsageInfo.RequiresTeleport`, type `+0x1C0`) plays the arrival beam (`0x60B700(0x13)`);
- the thought is cleared;
- a held balloon (`+0x294` or `+0x298`) goes into the umbrella shop's `0x613D00(1)`; at any other attraction its model is put away (`+0x294` = 0) and the countdown is kept;
- a drawn umbrella (states 4 and 1) is put away, state 2 (`0x6142E0`);
- a ride that draws its riders (`UsageInfo.RideHandlesSprite`, the thing flag `+0x42` bit 0x20) keeps the sprite in mode 3, any other takes it down (presentation);
- the challenge events 0x11, 0x34, 0x3A and 0x39 of the thing's type (`ThemeParkIncRideAdmitted`, for every kind of attraction). A photo booth's branch of 0x39 reads its host thing
  (`+0x38`); OpenTPW places no add-on, so it is not reached.

The pre-update skips the thought draw and the balloon countdown in 20 (`0x619E6A`).

**The exit `0x619980`** is the ride's dismiss of VAR_LETMEOFF (the bridge's `OnVisitorExitRide`) for a guest in state 20; the let-off of a guest still in 17 is open (below). A guest with no target leaves at once and is not moved. Otherwise the guest is put at the exit's
appear point and asked to walk to the cell beyond the exit (Theme Park World's ExitRide `0x005014E0`, byte for byte: a queue or entrance cell beyond, `0x67F5D0`, or no route closes the ride,
`0x5E1620`, and the guest stays). Then the use routine runs and the guest enters 19.

**The use routine `0x6118D0`:**
1. the thing id into the used history (`[a, b, c, d]` becomes `[id, a, b, b]`);
2. the visit count of the kind (`Info.WhichUIType` 0, 1, 2: `+0x1EC`, `+0x1F0`, `+0x1F4`);
3. the payment step `0x6126D0`: a fee above 0 is booked (`0x5E5DE0`: the bank, the takings, a shop's message 0xC and sale events), the till sounds (0xD0), the purse falls and `+0x1C4` rises;
4. the customer (`0x5E5DA0`) and the fatigue `+0x1DC` −= `UsageInfo.FatigueEffect`;
5. an attraction with `Info.RideTypeStringIndex` 0x17 reads its result back from VAR_PARAM into `+0x21B`;
6. `+0x21B` 0, a lost use: `MediumHappinessChange`, `+0x2E4` = 0, `+0x2DC` −= 35 and a sideshow's thought 6. Otherwise the outcome, then the record 3 × (byte(happiness) − byte(`+0x234`)) + 50 in
   the park statistics (a shop: categories 0xB..0xE by its special ingredient and 0x10..0x12 by its appearance; a sideshow: 0x14; `0x5AEDE0`, `IGuestPayments.PriceTested`) and a sideshow's
   thought 5.

**The outcome `0x612740`,** in this order:
1. `+0x2E4` = 1; a sideshow books its prize (`+0x88`, `0x5E60E0`), the prize goes into the purse and `+0x2DC` += 50; a shop books its goods (`0x5E63E0`);
2. the match step `0x611DA0`: with a rating (the live excitement byte, `0x5E41D0`) the difference to the guest's `+0x2A8` picks `PerfectRide`, `GoodRide` or `OKRide` (below
   `Perfect/Good/OKExcitementMatch`), which `+0x2E0` also takes (an OK match of a ride more exciting than wanted raises the declined ring `+0x4C8`); the illness rises by
   `((100 − byte(hunger)) / 20) × (rating / RideVomitDivisor)`, and a rise of its byte lowers `+0x2E0`;
3. happiness += the gain and `UsageInfo.HappinessEffect`; the mood (a single) = their sum; illness += `VomitEffect`;
4. a shop: thirst −= `ThirstEffect`, hunger −= `HungerEffect` (the larger plays 0x83 or 0x84); illness, happiness and the mood += the third slider byte `+0x9C` (50); litter += `LitterEffect` and
   += `+0x9C`; toilet += `ToiletEffect`;
5. the small portions: a `HungerEffect` draws (`0x612AEC`) and `(R & 7) + effect + amount` below 30 (the amount byte `+0x98`) costs the small step (happiness and mood) and `+0x2D0` −= 30, else
   `+0x2D0` += 35; the same for `ThirstEffect` (`0x612BCF`) and `+0x2D4`;
6. happiness and the mood += `HappinessEffect × amount / 100`;
7. the special ingredient: Fat toilet += `(FatEffect + 1) × amount / 100`, Salt thirst += `(SaltEffect + 1) × amount / 100`, Ice thirst += `(IceEffect + ThirstEffect) × amount / 100`, Sugar the
   speed boost word `+0xD8` += `(SugarEffect + 1) × amount / 100`;
8. the appearance (`AppearanceEffect`): a balloon shop gives a balloon (the colour draw `0x690370(0xE)`: the draw `>> 2` modulo the balloon bank's slot count `+0x21E`, which the ESP
   loader counts as the slots with frames (`0x68EBEF`; four in the shipped `Spr_bl`, read at the level's load), the countdown `clamp(quality × 255 / 100, 25, 255)` from the quality byte `+0x8C`) and
   judges the quality (a draw: below `ComplainAboutThisQuality + R % 10` raises the declined ring and `+0x2D8` by 33, else lowers it by 33); a costume shop puts a costume on (category 6,
   the variant draw `0x6902F0(6)`, a draw nothing reads, the judgement and `+0x2D8` = 33) or takes it off (category 0 and the variant drawn after a reseed with the guest's id, bug-compatible:
   the guest becomes a kid's bank); an umbrella shop opens an umbrella (the colour draw `0x690370(0x13)`, the speed term 0, the judgement and `+0x2D8` = 33);
9. a relief object (the thing flag `+0x42` bit 1): an illness byte above 90 is cured for −50 happiness, a worn toilet costs 20, else half the toilet byte is gained; the toilet meter goes to 0
   and the speed term to 25;
10. a won play: `+0x1F8` += 1 and happiness and the mood += `_ftol(ln(prize / price) / ln 2 × MediumHappinessChange)` (a free play: an infinity, 0);
11. the photo booth word `+0x208` (a booth whose score passes `PhotoThreshold`): 0, since the thing's booth word `+0x3A` is 0 without add-ons;
12. the mood: above `HappyDiffForVGood` thought 5 and the mood animation (umbrella down); below `HappyDiffForVPoor` thought 6 and animation 10; above `HappyDiffForGood` thought 5; below
    `HappyDiffForPoor` thought 6 and animation 10. The animation saves the state 20 and the exit's state 19 follows at once.

**State 19 `0x618380`, being unloaded.** The entry `0x61A69D` gives the body back (mode 1 when a ride that draws it kept it), plays a teleporting ride's beam and gives a balloon kept through the
ride back, unless a balloon shop has just given one. Before a teleporting ride's beam (`0x61A716`) the entry calls the sprite placement `0x60AE00(0)` (`0x61A70B`), which also raises the stamp
counter `0x5D81F0` (`0x60AFFD`) and clears the stranded mark `+0x1B0` when the raised value is below it (`0x60B002`): with Theme Park Inc's navigator (`--tpi-native-walk`) that is
`ThemeParkIncPlacementStamp`, as at the constructor's placement (TPI-NAVIGATOR.md); Theme Park World's walk counts the edits only and takes nothing here. The handler walks; stuck, the target is cleared and the guest is idle. Arrived:
- a photo booth word `+0x208`: the booth becomes the target (`+0x208` is kept, bug-compatible); a route to its back cell sends the guest there (13); a target that is not an attraction is cleared;
- the old major destination `+0x206`: it becomes the target and `+0x206` is cleared; an umbrella shop for a guest with an umbrella or a balloon shop for one with a balloon is dropped ("trapped"); a
  route to its back cell sends the guest there (13), else the target is cleared;
- then the guest is idle ("successfully left ride") with its target kept, and with the umbrella down plays the mood animation after a successful use or animation 10 after a lost one.

No world draw is taken in 16, 19 or 20's entry; 17's entry takes one; the outcome takes one per nonzero hunger and thirst effect and one to three in the appearance step.

**Gated in P3b** (`[EXT:tpi-capability-gates]`; none of them draws):
- the toilet prank (state 6's case 1 sets `+0x29C`) is phase P4's: the toilet visit's priming of the thing's `+0x3C` (and its +1 happiness and thought 0x15) is not made, so no toilet is ever
  primed and the victim's branch (`0x5E78E0(2000)`, the prank message with kind 2, the big happiness step, illness 95) is not reached; `+0x29C` stays set;
- the history object's rings the visit raises besides the declined ring `+0x4C8`: `+0x550` (a small portion), `+0x220` (`0x5E61D0`) and the four mood rings `+0x2A8`, `+0x440`, `+0x330`,
  `+0x3B8` (SAVE-940: OpenTPW keeps only the three price rings and the walk-away count);
- the analyser's balloon, costume and umbrella counters (`+0x18`, `+0x1C`, `+0x20` of `0x64EAC0`): no ported reader.

`[EXT:tpi-no-maintenance-wear]` keeps the toilet's wear: a relief visit's `0x5E78E0` (the repair word `+0x54` by the toilet byte and its RegionFX rows 1 and 6) is not made.

OpenTPW's own teardown (a ride whose script stopped or faulted) lets go of a native rider by the exit, and sets it down beyond the exit and idle when the exit is blocked; a guest still in 17 is
taken out of the list and made idle the same way. A demolished ride's riders are the removal message's.

**Deferred** (review follow-ups of phases P2 and P3a):
- a native oracle round for case 3's two world draws and the stand point `0x5E1300` (the P2 oracle compares a Python model);
- the guest's sex from its own `+0x34`/`+0x30`: the mood animation and the score's `GenderSpecific` gate read the loaded bank's header byte (a costumed guest's costume bank), so a headless rig
  without banks reads male, and the hash word of the mode follows;
- several attractions in one cell are visited in the object list's order where the minor decision walks the cell's thing chain (`0x6114B0`);
- `WorldRng.Next(0)` returns 0 where the original's division faults; the native states never divide by a zero they could reach (the variant draw `0x6902F0` of a rig without loaded banks reads
  variant 0 the same way).

**Open: the let-off in state 17 (not yet ported, under research).** The original's dismiss callers (`0x5E4FFF`, `0x5E5907`, `0x5E5A96`) call the exit `0x619980` without a state test,
and neither the exit, the use routine `0x6118D0` nor the setter `0x61A540` calls the unlink `0x5E0100`, so a guest let off while still in 17 leaves a ghost in the ride's queue list. The case is
reachable: a script can take a guest and let it off before the guest's own update (`RideVisitorBridge.KeepsBoardedGuestQueued`). OpenTPW ignores that let-off for now: the guest is later forced
into state 20 with no ride holding it and stays stuck, and a save of that park is refused ("state 20 but no attraction holds it aboard"). There is no crash, and the switch-off paths are not
affected. The ghost's later fate in the original is being researched before a decision.

## Save

The guest snapshot stays at schema 9. A native guest writes an optional block `GuestSnapshot.Tpi` (`ThemeParkIncGuestSnapshot`) with every field of
the record. A guest without it writes nothing new, so a save made with the switch off is byte for byte the save of a build without this phase. The
block joins the state hashes only on a native guest, so the hashes and the determinism pins of every switch-off park are unchanged.

The snapshot also records whether the park ran the native machine, in the optional member `GuestSimulationSnapshot.ThemeParkIncNativeGuests`
(written only when true). A restore refuses a save whose value (absent counts as false) differs from the running simulation's:
- a save with native blocks into a park running with the switch off (its guests would need the native machine);
- a save without the member, or with it false, into a Theme Park Inc park running with the switch on (its guests run Theme Park World's rules).

A restore also refuses a native guest in a state this build cannot run (10..12, 18, 21 and 24..26, or a spot animation 8 that returns to one), so a save of a later phase or an edited save never
stops a guest's update mid-tick. A native guest in a queue state (14..17, or 8 returning to one) must be in the list of the ride it names and no other guest may be; a guest in 17 must also be named
by the ride's written loading slot or held by the ride; a guest in 20 must be held by the ride it names; no other native guest may be held. Phase P3a adds no saved field. Phase P2 adds one
optional member to the block, the wander flag `+0xE8` (`Wandered`; absent in a phase P1 save, whose guests never wandered), and phase P3b five: the fee word `+0x1B4` (`Fee`), the use flag
`+0x2E4` (`UseSucceeded`), the balloon colour `+0x2A0` (`BalloonColour`) and the costume's sprite words `+0x34`/`+0x30` (`SpriteCategory`, `SpriteVariant`; absent while the guest keeps the
constructor's), all absent in an earlier phase's save, without a new schema. The block's balloon is no longer refused: a countdown above the balloon's life 255 is.

A phase P3a save can hold a guest of the bridged boarding: the state word 16 with Theme Park World's Boarding, WaitingToBoard, Using or ExitingRide. Boarding (the walk to the stand point) is
state 16 itself and restores. A rider of the bridged boarding (WaitingToBoard, Using or ExitingRide) stands for 17, 20 or 19, whose chance roll `+0x21B`, fee `+0x1B4` and mode the bridged
boarding never kept: the restore refuses it, with the guest and its states named, rather than inventing them. A guest in 16 with any other projection is refused too.

## Settings

`ThemeParkIncGuestSettings` reads the Standard object (`0xA01A60`, `ThemeParkIncStandard`) by address. It reads:
- the PeepInfo words the states use (`0xA01A78..0xA01B84`, with the prank words `StinkbombLikelihood`, `PrankHappinessThreshold` and `PrankLikelihood` at `0xA01B00..0xA01B08`);
- the three weather arrays (`0xA01B88`, `0xA01BBC`, `0xA01BF0`);
- FixedItemInfo's booths and stops (`0xA025B4..0xA025DC`, `TicketBooth[]` at `0xA02610`);
- `FirstHour.Minutes` (`0xA034D4`).

A test checks every address against its key through the SAM binding.

The level reads these settings (`GuestSettings.TpiNative`) only when the switch is on; a switch-off park never loads them.

## Verification

- `tools/ppc-analysis/oracle_x86/tpi_guest_states_oracle.py` runs four routines natively at control word `0x7F` and compares them with the port: the catch test over -1..40, the setter's P1
  entries, the pre-update and the booth's verdict. Phase P2 adds state 6 (`0x614A40`, the searches stubbed), the spot animation `0x60FB30`, the mood `0x60CDD0`, the dispatch's states 7 and 8
  (`0x61A179`, `0x61A382`) and state 9 (`0x616F10`). Phase P3a adds the score `0x6101E0` (every gate, weight, scale and history division, the type and park records stubbed), the queue
  functions `0x5E0280`, `0x5DFDD0` and `0x5DFF10` with the limit `0x5DFE00` over random lists, and states 13 (`0x616AC0`), 14 (`0x6176C0`) and 15 (`0x61A1DB`) with their callees stubbed.
  No mismatch (see the oracle README). Phase P3b's `tools/ppc-analysis/oracle_x86/tpi_guest_visit_oracle.py` adds states 16 (`0x617F80`), 17 (the entry roll through the setter and the
  handler `0x61A21D`), 20's entry (`0x61A77E`), 19 (`0x618380`) and the use routine `0x6118D0` with its payment step, its outcome `0x612740` and the match step `0x611DA0` (the meters, the words,
  the bookings, the records and the draws; the gated rings and counters recorded but not compared): no mismatch over 40 rounds (1,000 cases each).
- `ThemeParkIncNativeGuestTests`, `ThemeParkIncNativeIdleTests`, `ThemeParkIncNativeQueueTests`, `ThemeParkIncNativeVisitTests` and `ThemeParkIncGuestSettingsTests` cover the rig behaviour
  (with `OPENTPW_TEST_TPI_NATIVE_WALK=1` all four native rigs walk with Theme Park Inc's navigator),
  the draws, the saves (a guest saved mid-ride, a phase P3a rider refused) and the shipped values; the visit tests hold the oracle's reading of the use routine for a balloon shop, a sideshow won
  and lost, a costume put on and taken off, an umbrella shop and a relief visit (fresh, worn and ill); `ThemeParkIncPlayTests.NativeGuestsChooseQueueAndBoardTheScienceRides` runs native guests in the
  prebuilt Science park until one has boarded, ridden and been let off;
  `ThemeParkIncPerformerTests` runs the entertainer reaction against a hired, placed and performing entertainer of the retail Science economy.

## Phases after P3b

- P3b (done): the visit, above; the information desk's part of the score comes with P5.
- P4: pranks, then the switch-over. Once the switch is on by default, a Science save made with it off (every save before then) is refused by the
  mismatch rule above; P4 has to document that refusal in `docs/PARK-SAVES.md`.
- P5: infodesk, whinging and rumours.
- P6: the guard's chase, carry and first aid.
