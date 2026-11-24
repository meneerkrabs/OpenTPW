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

The entries of 7, 17, 19, 20, 21 and 26 read objects of the later phases and are not run in P1.

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

**State 6 in P1** is a stub under `[EXT:tpi-capability-gates]`. Only the leave gate of `0x614A40` runs: a happiness byte of 0, an exit counter at or below 0 or a closed park
costs `BigHappinessChange` and sends the guest to the bus stop (state 22). The rest of the handler is phase P2's, including its draw A. The states the later
phases port (7, 9..21, 24..26) are unreachable in P1, and the dispatch refuses them.

**Not ported, gated under `[EXT:tpi-capability-gates]`:**
- the tutorial tips of state 5 (advice 0x207 and 0x208 in the first `FirstHour.Minutes`, `0x650890`);
- state 30's mark of a prankster in the tag system (`0x636B10`, phase P4);
- the balloon's litter when an umbrella opens (phase P2; no native guest holds a balloon before phase P3).

## Save

Guest snapshot schema 10 adds an optional block `GuestSnapshot.Tpi` (`ThemeParkIncGuestSnapshot`) with every field of the record. A guest without it
writes nothing new, so a save made with the switch off differs from schema 9 only by its version number. The block joins the state hashes only on a
native guest.

The snapshot also records whether the park ran the native machine (`GuestSimulationSnapshot.ThemeParkIncNativeGuests`). A restore refuses a save whose
value differs from the running simulation's:
- a save with native blocks into a park running with the switch off (its guests would need the native machine);
- a schema 9 save, or a schema 10 save made with the switch off, into a Theme Park Inc park running with it on (its guests run Theme Park World's rules).

A restore also refuses a native guest in a state this build cannot run (7, 9..21 and 24..26, or a spot animation 8 that returns to one), so a save of a
later phase or an edited save never stops a guest's update mid-tick.

## Settings

`ThemeParkIncGuestSettings` reads the Standard object (`0xA01A60`, `ThemeParkIncStandard`) by address. It reads:
- the PeepInfo words the states use (`0xA01A78..0xA01B84`);
- the three weather arrays (`0xA01B88`, `0xA01BBC`, `0xA01BF0`);
- FixedItemInfo's booths and stops (`0xA025B4..0xA025DC`, `TicketBooth[]` at `0xA02610`);
- `FirstHour.Minutes` (`0xA034D4`).

A test checks every address against its key through the SAM binding.

## Verification

- `tools/ppc-analysis/oracle_x86/tpi_guest_states_oracle.py` runs four routines natively at control word `0x7F` and compares them with the port: the catch test over -1..40, the setter's P1
  entries, the pre-update and the booth's verdict. No mismatch (see the oracle README).
- `ThemeParkIncNativeGuestTests` and `ThemeParkIncGuestSettingsTests` cover the rig behaviour, the draws, the saves and the shipped values.

## Phases after P1

- P2: idle (state 6 in full), wander, spot animations, litter.
- P3: rides and queues.
- P4: pranks, then the switch-over.
- P5: infodesk, whinging and rumours.
- P6: the guard's chase, carry and first aid.
