# Cheats: the original cheat mode and `opentpw.cheats`

October 19, 2026. Status: **phases K0** (this trace, the ride capacity data and the decisions), **K1** (the park's cheat record, the turn-boundary queue, the save, the
extension's recording and the cheated mark; [below](#the-parks-cheat-record-k1)) and **K2** (the original cheat mode: the secret park name, the twelve Alt keys and the `CHTS` chunk in
and out; [below](#the-original-cheat-mode-k2)). The extension's window comes in K3 and the ride vehicle limit in K4. The plan is `OpenTPW-notes/plans/cheats-plan.md`.

The request (user, 2026-10-19): a cheats extension with (1) a button that makes visitors arrive by bus, plane or boat, (2) a way to change the weather, (3) a way to raise the
cars-per-ride limit and (4) a button that awards a golden ticket. Evidence: a static trace of the decrypted Windows `tp.exe` v2.0 (SHA-256 `5472f97b...`), through the shared REA
session and byte-exact disassembly; the original game was not run. Labels: `[BIN:TPW-EXE:0x...]` for ports of the original, `[EXT:cheats-*]` for OpenTPW's additions, which the user
approved on 2026-10-19 (below).

## Decisions (user, 2026-10-19)

These are the user's answers to the plan's open questions; they were given on **2026-10-19** and are the approval of every `[EXT:cheats-*]` label of this feature.

- **The original cheat mode always works**, as in the original game, whether the extension is on or off: the secret park name "Kevin the" / "Cosmic Cow" and the twelve Alt keys.
  It is a faithful port (`[BIN]`), not part of the extension.
- **The cheats window** (Alt+C: arrivals, weather, vehicle limit, a chosen golden ticket) exists only with `opentpw.cheats` on. The extension is **off by default** and works only
  in Theme Park World parks (not in a Theme Park Inc run, a read-only visit or an online co-op or presence session). `[EXT:cheats-extension]`, `[EXT:cheats-window]`
- **Cheated parks** are marked permanently after the first cheat (the original's save byte) and shown as cheated, but **publishing and ranking stay allowed**: OpenTPW's server does
  not refuse a cheated park and the mark is informational. This departs from the original's refusal (UITEXT 417, below). `[EXT:cheats-publish]`
- **Golden tickets from cheats** count toward golden keys and theme unlocks, as in the original. The player progress file records that they came from a cheat. They play no advisor
  line, only a short on-screen note. `[EXT:cheats-ticket-provenance]`
- **The rest as the plan recommends:** weather choices clear, rain, thunderstorm and snow, through a forecast with zero warning (`[EXT:cheats-weather]`); arrival group sizes bus up
  to 35, plane 36-60, boat 61 or more (`[EXT:cheats-arrival]`); vehicle ceilings 32 for bumper cars and karts, 64 for boats and 40 for tours, coasters later (`[EXT:cheats-ride-vehicles]`);
  an export to the original save format writes `CHTS = 01 01` (the shipped seed's value) for a cheated park and refuses rides above the original limits (`[EXT:cheats-export]`).

## The original's cheat system

**Summary.** `tp.exe` has a real cheat mode. Renaming the park (the gate sign) to a secret name turns it on; while it is on, twelve Alt key combinations work: research everything,
+10,000 cash and the ten non-secret golden tickets. The mode is one byte in the park save (`CHTS`). There is no weather, arrival or ride vehicle cheat in the original.

### Entry: the secret park name `[BIN:TPW-EXE:0x4993E0]`

- The ticket-price window's park-name control opens the rename dialog `0x4990F0` (two edit fields of 14 characters, filled from the gate's sign lines). Its OK handler `0x4993E0`
  (message `0x100`, control `-1`) stores the lines on the gate (`0x4E2950`/`0x4E2A50`), redraws it (`0x4682B0`) and copies them to the player record (`0x409410`, then `0x5B0940`/`0x5B0970`).
- It then XORs each UTF-16 unit of the two input buffers (`0x7C2668`, `0x7C2688`) in place with its index (`0x499546..0x4995A4`, the length re-read with `wcslen` `0x67ACD0` on every
  step) and compares the results with `wcscmp` (`0x67C290`, case-sensitive) against the strings the pointers `0x751A34` and `0x751A38` hold: `Kdtjj%rom` (`0x751A54`) and `Cnqnmf&Dg~`
  (`0x751A3C`). Decoded with the same XOR: **line 1 "Kevin the", line 2 "Cosmic Cow"**. Both must match (`0x4995B1`, `0x4995C9`).
- On a match it calls `0x4053C0(1)` on the cheat object `0xFB3520` (`0x49960A`), after constructing it on first use (`0x4053B0`, guard bit 1 of `0xFB34C4`). The sign keeps the
  new name: the lines were stored before the compare.

### The cheat object `0xFB3520`

- A static of 12 bytes behind **three separate first-use guards**: `0xFB3519` bit 1 (the save writer `0x416C61` and the loader `0x415C21`), `0xFB34C4` bit 1 (the rename handler `0x4995D5`)
  and `0xFB34C8` bit 1 (the two key paths `0x4888D2`, `0x488961`). The first use of each guard in a process runs the constructor `0x4053B0` again on the same object, which clears `+0x9`,
  `+0xA` and `+0xB`. So in the original the mode can be lost once per run: the first key message that reaches the cheat check clears a mode that a load or the rename had just set (the
  first Alt cheat after a first rename then finds the mode off), and a first rename after loading clears the loaded `+0xB`, so the next save writes `01 00`. This is an artefact of the
  process's history, not park state; OpenTPW keeps the mode with the park and does not reproduce it (a port decision of K2).
- `+0xA` is the **cheat mode**. `0x4053C0(on)` writes it; when on, it builds a message object (vtable `0x6FD6C8`, built by `0x40F6D0` with the payload byte at `+8`; type `0x15` from
  `0x405530`; `CMsgCheatMode` in the Mac build's class list, docs/reverse/AREAS.md) and posts it through the message centre (`0x788D3C`, `0x40FB10`). Which things listen to type
  `0x15` is a runtime subscription and was not found statically; nothing in OpenTPW posts it.
- `0x405560` returns `+0xA`; its only callers are the two key paths below (`0x488902`, `0x488991`).
- `+0xB` is saved and loaded, but apart from the constructor (which clears it) and the loader no `tp.exe` code writes it (byte scan of every `0xFB3520` reference); its meaning is
  unknown. `+0x9` is only cleared.
- **The jungle's shipped `Easymode.TPWI` carries `CHTS = 01 01`** (`SavePayloadLayoutTests`): it was saved with the cheat mode on. The eleven Nexus corpus saves carry `00 00`. If a park
  started from the shipped seed loads it through the payload reader `0x415270`, the original starts that park in cheat mode (and, if the server reads the byte, could not publish it).
  OpenTPW does not mark a park started from the shipped seed (a port decision of K2).
- Nothing ever calls `0x4053C0(0)` (its only caller is the rename handler, `0x49960A`): renaming the park to another name leaves the mode on. Only loading a save or a level, or the
  constructor's re-runs above, replaces it.

### The save chunk `[BIN:TPW-EXE:0x4055D0, 0x405570]`

- Two bytes, `+0xA` then `+0xB`, written by `0x4055D0` (through the stream writer `0x5F5E50`) at `0x416C98` in the payload writer `0x4164C0`, and read by `0x405570` (`0x5F5E20`) at
  `0x415C56` in the payload reader `0x415270`. The chunk follows the Sound block and is closed by the tag `STHC` (the repo's `CHTS`). Log strings: "Cheat: saved %d bytes" (`0x74BF68`),
  "Cheat: loaded %d bytes" (`0x749C28`), "Cheat system failed to save"/"to load" (`0x74BF80`/`0x749C40`) and "The SAD_CHEAT module did not load the same number of bytes that it saved"
  (`0x749BDC`).
- So **cheat mode belongs to the park save**: saving writes the current mode, loading a save or a level sets it from the file, and a park saved while the mode was on carries the mark.
- OpenTPW reads the two bytes (`OriginalSavePayload.Cheat`, `SaveRuntimeBlocks.Cheats`) and reports them without applying them (`APPROX:SAVE-010`); `OriginalParkExport` keeps the
  template level's two bytes.

### Input: the "cheat" key context `[BIN:TPW-EXE:0x40CB80]`

- `0x40CB80` builds the key contexts; the one named "cheat" is the object `0x78733C`, whose table `0x7484D0` holds 12 entries of 0x14 bytes: `+0` index, `+2` virtual key,
  `+4` modifier mask, `+6` pressed flag, `+0xC` and `+0x10` two handlers. Every cheat entry has a handler at `+0xC` only.
- The modifier mask comes from `0x46B420` (`GetKeyState` of VK 0x10/0x11/0x12): Shift = 0x03, Ctrl = 0x0C, Alt = 0x30. Every cheat entry has mask 0x30, and the dispatcher
  `0x40C990` takes the first entry whose key **and** mask are equal (`0x40C9C7`, `0x40C9D3`): **Alt alone**, not with Shift or Ctrl.
- The key handler (the engine's key messages `0x1000A` and `0x1000B`, the jump table at `0x4889D0`) tries the camera (`0x787C14`), game (`0x787A60`) and shortcuts (`0x78721C`)
  contexts in that order and only then, if `0x405560` is true, the cheat context: `0x48890F` dispatches a `0x1000A` message through `0x40C900` (handler `+0x10`, none in the cheat
  table) and `0x48899A` a `0x1000B` message through `0x40C990` (handler `+0xC`). `0x40C900` sets the entry's pressed flag (`+6 = 1`, `0x40C974`) and `0x40C990` clears it (`0x40CA04`), so
  `0x1000A` is the key press and `0x1000B` the release: **a cheat fires when its key is released**, with Alt still held (the mask is read at that moment). The cheat keys are not
  rebindable.

### The twelve cheats (table `0x7484D0`)

| Keys | VK | Handler | Effect |
|---|---|---|---|
| Alt+R | 0x52 | `0x40C430` | The research manager (`0x519550`) runs `0x503FF0`: for every item of its list (count `+0x145C`, 0x14-byte entries, id at `+0xC`, walked from the last) the object control (`0x4D3D10`) gets its available byte `+0x10` = 1: **every research item becomes buildable**. Research progress is not touched. |
| Alt+1 | 0x31 | `0x40C450` | The bank (`0x5195D0`) credit `0x4D0190(10000)`: **+10,000** to the balance (`+0xC`), the current income slot (park `+0x1FC90`) and the year's profit (`+0x124`), the path a challenge prize takes. |
| Alt+G | 0x47 | `0x40C6C0` | `0x5AF810(0)`: the **Visitors** golden ticket. |
| Alt+Num1 | 0x61 | `0x40C6E0` | `0x5AF810(1)`: **PeopleInPark**. |
| Alt+Num2 | 0x62 | `0x40C700` | `0x5AF810(2)`: **Happiness**. |
| Alt+Num3 | 0x63 | `0x40C720` | `0x5AF810(3)`: **AllResearchedAndBuilt**. |
| Alt+Num4 | 0x64 | `0x40C740` | `0x5AF810(4)`: **ProfitYear**. |
| Alt+Num5 | 0x65 | `0x40C760` | `0x5AF810(5)`: **RecentVisitors**. |
| Alt+Num6 | 0x66 | `0x40C780` | `0x5AF940(0, 100)`: the global **CoasterHeight** with record 100. |
| Alt+Num7 | 0x67 | `0x40C7A0` | `0x5AF940(1, 100)`: global **GokartExcitement**. |
| Alt+Num8 | 0x68 | `0x40C7C0` | `0x5AF940(2, 100)`: global **WaterLength**. |
| Alt+Num9 | 0x69 | `0x40C7E0` | `0x5AF940(3, 100)`: global **BigPark**. |

- The ticket handlers call the player ledger (`0x409410`) directly and skip the rest of the normal award path `0x4D4BC0`: no advisor message, no first-ticket lines, no log. The award
  is **silent**.
- `0x5AF810(kind)` returns 0 when the park already holds the ticket; otherwise it sets the park's byte and returns 1, or 2/3 when the new total makes a golden key (the key rule
  `AdvisorScanChallenges` documents).
- `0x5AF940(kind, 100)`: when no park holds the global ticket, this park takes it with record 100 and the result is 1, or 2/3 with a key, as for `0x5AF810`; when this park holds it,
  nothing happens (0); when another park holds it with a record below 100, the ticket moves here (4); a holder with a record of 100 or more keeps it (0).
- The two secret tickets (CamerasEverywhere, AllLandOwned, `0x5AFB00`) have no cheat. No handler checks the game mode or `CanEarnTickets`.

### Publishing

UITEXT 417 (English): "PARK NOT PUBLISHED!! This park has been built using cheats, and therefore cannot be published!". The upload path `0x5B7CD0` sends no cheat byte of its own
(its `+0x370` byte is Instant Action) and the client refuses only on the child lock, so the refusal is most likely the server's, read from the uploaded save's `CHTS` byte. OpenTPW
does not port the refusal (user decision above).

## Where the requested cheats hook in

- **Arrivals.** One group at a time: `GuestSimulation.UpdateArrivals` starts a group when `(turn >> 2) - (start >> 2) > ArrivalTimeBetween`, sizes it with `ArrivalGroupSize` and picks
  the vehicle by size (up to 35 the bus, up to 60 the plane, otherwise the ferry, `[BIN:TPW-EXE:0x4CF3E0]`).
- **Arrival vehicles per theme** (checked in the data, K0): every theme has a `features/bus`, `features/seaplane` and `features/ferry` archive. The second vehicle's archive is
  `seaplane` in all four themes; its `Info.Name` is "Seaplane" in the jungle and "Plane" in Halloween, fantasy and space. Ids: jungle 1600/1602/1604, Halloween 2600/2602/2604, fantasy
  4600/4602/4604, space 3600/3602/3604.
- **Weather.** `ParkWeather` rolls a quality every `DaysBetweenChanges` days and applies it `DaysOfWarning` days later; a forecast with zero warning goes through the normal converter
  and banner.
- **Golden tickets.** `ParkObjectives.CheckGoldenTickets` and `ParkEconomy`'s `GoldenTicketWon` / `GlobalTicketMoved` events, which the player ledger follows.

## Ride vehicles: the shipped capacities (K0)

The player's lever is the ride's **capacity** (`UsageInfo.MinCapacity..MaxCapacity`, the ride window's slider, clamped by the setter `[BIN:TPW-EXE:0x4dd7f0]`). What the capacity
means differs per family; read from every theme's `.sam` files and ride scripts:

| Ride | Theme | Info.Id | Capacity | Script rule | Vehicles at the maximum | Engine limit |
|---|---|---|---|---|---|---|
| The Hot Pot | jungle | 1140 | 1..8 | `VAR_CARS` follows `VAR_CAPACITY` (one rider a car) | 8 cars | template −1: 8 |
| Pumpkin Castle | Halloween | 2140 | 1..8 | same | 8 | template −6: 8 |
| Bumper Bugs | fantasy | 4140 | 1..8 | same | 8 | template −11: 8 |
| Uforia | space | 3140 | 1..8 | same | 8 | template −14: 8 |
| Dino Karts | jungle | 1150 | 1..8 | one kart per boarding rider | 8 karts | template −4: 8 |
| CryptKarts | Halloween | 2150 | 1..16 | one kart per **two** riders (`VAR_PERCARCOUNT`) | 8 karts | template −7: 8 |
| Bumble Buggies | fantasy | 4150 | 1..8 | one kart per rider | 8 | template −10: 8 |
| Space Racers | space | 3150 | 1..8 | one kart per rider | 8 | template −9: 8 |
| Splish Splash | jungle | 1160 | 1..5 | capacity = riders per boat (`VAR_BOATCOUNT`); a boat leaves when full or after ten seconds | as many as riders come | template −5: 64 boats |
| Ooze Crooz, Taptastic Rapids, The Blobulator | Halloween, fantasy, space | 2160, 4160, 3160 | 1..5 | same | same | 64 |
| Jurassic Tours, Flightmare Tours, Tweety Tours | jungle, Halloween, fantasy | 1170, 2170, 4170 | 1..27 | `TOUR 5` with ceil(capacity / 9) cars | 3 cars | `TourController.CarSlots` 20 |
| Star Tours | space | 3170 | 1..24 | ceil(capacity / 8) cars | 3 cars | 20 |

Consequences for the vehicle limit (K4):
- Bumper cars and karts are the clear case: the capacity is the car (or rider) count, and the template's `MaxVehicles` (8) caps it twice. The ceiling 32 needs a per-ride capacity
  override and an effective `MaxVehicles`.
- **Boats are not limited by the capacity.** It is the riders per boat; the number of boats is limited only by the template's 64 (`BumpController.MaximumVehicles`), which is the
  agreed ceiling already. A boat cheat has nothing to raise.
- **Tours**: the capacity is riders; cars are ceil(capacity / 9) (/ 8 for Star Tours). The capacity is a byte (`ClampCapacity` keeps 8 bits), so a tour reaches at most 29 cars (32 for
  Star Tours) through it; the ceiling of 40 cars needs `CarSlots` 40 and either a capacity above 255 or a car override. K4 decides which.
- Coaster trains are derived from the release rate and are out of scope (K5).

## The park's cheat record (K1)

- **Where it lives.** `ParkCheats` (`source/OpenTPW/Economy/ParkCheats.cs`) is part of the park economy (`ParkEconomy.Cheats`): the cheat mode (`[BIN:TPW-EXE:0x4053C0]`, the original's `+0xA`), a
  record of every applied cheat (park turn, kind, argument; at most 4,096 entries, the oldest dropped and counted), the actions waiting for the next turn (at most 16) and a sticky flag for the
  extension's own cheats.
- **The turn boundary** `[EXT:cheats-turn-boundary]`. Every cheat is queued (`ParkEconomy.QueueCheat`) and applied at the start of the next park turn, before the turn's thing sweep (its
  weather and arrival updates; in a level whose turns are not swept, the tick's guests and objects run before the economy's turn), in the order of `CheatKind`, with no random draw. Applying any cheat turns the mode on; nothing turns it off (the original never calls `0x4053C0(0)`). A
  Theme Park Inc economy refuses cheats: only Theme Park World's are traced. The mode's message of type `0x15` is not posted.
- **The save** `[EXT:cheats-save]`. `ParkSaveFile.Data.Cheats` holds the record only for a park that has cheated or has a cheat waiting, so every other park saves (and hashes) as before; it is
  validated on load (known kinds and arguments, turns in order and not after the park turn, a mode that is on when cheats were applied). See [DETERMINISM.md](DETERMINISM.md#cheats).
- **The extension's recording.** `opentpw.cheats` is a used-only extension: a world save records it once the park has used one of its own cheats (or has one waiting), and such a save needs it to
  load. A park cheated through the original's cheat mode alone does not record it and loads with the extension off, because the user's decision makes that mode independent of the extension.
  Until K3 the extension is registered but not selectable, and no kind of its own exists.
- **The cheated mark** `[EXT:cheats-publish]`. The load and continue menus write "(cheated)" after a cheated park's name (in all eleven UI languages), and a cheated park's shared package
  carries the flag `cheated`, which a visit lists with the package's other flags. Exporting, publishing and ranking are not refused: `ParkSharing.ModsRefusal` does not count the cheats
  extension (it is used-only and the sharing code does not report it in use) and the server's acceptance is unchanged; the flag is informational and well-formed for every client and server
  version (any flag matching the package's pattern is accepted).
- **The original-format export** writes the park's own `CHTS` bytes since K2, which also reads them on import (in K1 an exported park kept the template's bytes).

## The original cheat mode (K2)

A faithful port of the original's cheat mode. It works whether `opentpw.cheats` is on or off (the user's decision), in Theme Park World parks that are not a read-only visit, and leaves no
extension record, so such a park loads with the extension off.

- **The secret name** `[BIN:TPW-EXE:0x4993E0]`. `Level.RenamePark` stores the lines and redraws the sign as before, then compares them as the OK handler does: each line through
  `SecretParkName.Transform` (unit `i` XOR `i`, the length read again after every unit) against the stored `Kdtjj%rom` / `Cnqnmf&Dg~`, exact and case-sensitive. A match queues
  `CheatKind.Mode`. The sign keeps the name.
- **The keys** `[BIN:TPW-EXE:0x7484D0, 0x488991]`. `ParkHud.CheatKeys` is the table: R, the top row's 1, G and the keypad's 1..9, each with Alt alone (the Mac's Option is Alt). They are
  consulted after the HUD's shortcuts and only while the park's cheat mode is on, and a cheat **fires when its key is released** with Alt alone held (`0x40C990` runs the `+0xC` handler
  on the release message), once per release. The plain shortcuts no longer fire with Alt held (the dispatcher's exact mask match), so Alt+R and Alt+G do not open the research and entry
  price windows. A keyboard without a keypad has no Alt+Num keys; the extension's window (K3) offers every ticket.
  - `[EXT:cheats-key-release]`: OpenTPW samples the keys once a frame, so a frame that sees the key and Alt released together cannot tell which went first; it takes the previous frame's
    mask, so a quick release with Alt still fires.
  - With Alt alone held the keypad's 4 and 6 do not scroll the camera: the dispatcher matches a context entry on the exact key and mask (`[BIN:TPW-EXE:0x40C900]`), and the camera
    context's table `0x748158` holds VK_NUMPAD4 and VK_NUMPAD6 with mask 0, so Alt+Num4 and Alt+Num6 reach only the cheat context.
- **Alt+R** `[BIN:TPW-EXE:0x503FF0]`: every research item's object becomes available to build (`ParkResearch.MakeEveryItemAvailable`, kept in the save as `Research.CheatAvailable`).
  Nothing is researched: the lab keeps its items and progress, no group opens, `IsAllResearched` and the AllResearchedAndBuilt ticket (`0x4CA6B0` reads the items left and the built
  counts) do not change, and an upgrade level stays a research project (its item names the ride's own object control).
- **Alt+1** `[BIN:TPW-EXE:0x4D0190]`: +10,000 through the prize credit: the balance, money in and the year's profit (so it counts toward the profit ticket).
- **Alt+G, Alt+Num1..9** `[BIN:TPW-EXE:0x5AF810, 0x5AF940]`: the six local tickets join the park's set, the four global ones go through the player's holder rule with record 100. The award
  is silent: `ParkEventKind.CheatApplied`, never `GoldenTicketWon`, so the advisor says nothing. No game mode or `CanEarnTickets` check, as in the original (an Instant Action park gets
  the ticket in its own set; the player's ledger takes Full Simulation parks whose rules can earn tickets only, as for any ticket). The HUD writes one short note per applied cheat (a ticket the park already held
  writes none). The player's progress file records that a cheat awarded the ticket ([PLAYER-TICKET-LEDGER.md](PLAYER-TICKET-LEDGER.md#tickets-a-cheat-awarded-docscheatsmd)).
- **The `CHTS` chunk** `[BIN:TPW-EXE:0x405570, 0x4055D0]`. Importing a player's original save applies byte 0: nonzero marks the park as cheated (mode on, empty record); byte 1 is reported
  and not kept (SAVE-010). The export writes the park's own chunk, `01 01` for a cheated park and `00 00` otherwise (`[EXT:cheats-export]`), whatever the template holds.
- **Alt+R in an original save** `[BIN:TPW-EXE:0x503FF0]`. The cheat writes the object controls' researched byte `+0x10` for every research item's object and leaves the projects as they
  are. Importing a cheated save (byte 0 nonzero) therefore takes an object marked researched whose project is unfinished as the cheat's (`Research.CheatAvailable`); in a clean save such a
  flag stays a reported disagreement. The research export writes `+0x10` for the objects the cheat made available.
- **Port decisions.**
  - `[EXT:cheats-shipped-seed]`: the jungle's shipped `Easymode.TPWI` carries `01 01`. A park started from it (an Instant Action jungle park, the reference path) is **not** marked: in the
    original it would start in cheat mode, but here a player who never cheated keeps an unmarked park whose save and hash are as before.
  - `[EXT:cheats-park-mode]`: the original's three first-use guards that clear the mode once per process run (above) are not reproduced; the mode belongs to the park.
  - `[EXT:cheats-turn-boundary]`: every cheat applies at the next park turn (K1), so after the secret name the keys work from the next turn on.
  - The message of type `0x15` is not posted: its subscriber list in the message centre `0x788D3C` is empty (no `0x40F9E0` subscribe site uses the type), so posting it changes nothing (COMPAT-007, retired).

## The cheats window (K3)

`opentpw.cheats` is selectable from K3 on (off by default). With it on, **Alt+C** opens and closes the cheats window (`ParkHud.CreateCheatsWindow`, `[EXT:cheats-window]`) in a Theme Park
World park of the player's own (not a read-only visit, not in a Theme Park Inc run). The window is built from the options pages' pieces (an `f_screen` page, `f_optpanel` rows, `b_on2`
buttons, the `b_okay` button) and every button queues a cheat for the next park turn; opening it marks nothing.

- **Cheat mode.** The first row turns the original's cheat mode on (`CheatKind.Mode`, as the secret name does). The other actions are enabled once the mode is on or waiting for the next
  turn, so the window does what the secret name allows and nothing more without it.
- **Visitors by vehicle** `[EXT:cheats-arrival]`. The row cycles bus, plane and boat (the theme's `bus`, `seaplane` and `ferry` archives); "Send visitors" queues `CheatKind.Arrival`.
  At the turn it applies, `GuestSimulation.TryStartCheatArrival` starts a group now: its size is the next natural group's size (the weather factor, the park's points and the minimum; no
  draw) moved into the vehicle's band, which is the dispatch's own (`[BIN:TPW-EXE:0x4CF3E0]`, bus up to 35, plane 36 to 60, boat 61 or more), then cut by the park limit as every
  group is (a lost park's group is empty). The chosen vehicle brings it whatever the cut size, and it is delivered as a natural group: the advisor's arrival ring sees its size, one guest
  steps off per turn, and the turn that finds it empty restarts the arrival timer. While a group is being delivered or a vehicle is at work the cheat waits, unrecorded, for a later turn;
  only one arrival cheat may wait at a time. While it waits no natural group starts and no vehicle is called for guests leaving the park (a vehicle standing at the stop is still sent on
  with them), so the vehicle at work finishes and the cheat's group comes next; without this hold a park whose leaving guests keep calling vehicles never had a free turn.
- **Weather** `[EXT:cheats-weather]`. Clear, rain, thunderstorm or snow, in every theme (snow too: it is a cheat). "Change weather" queues `CheatKind.Weather`, which
  `ParkWeather.ApplyCheat` applies as a forecast with no warning: the pending forecast is dropped, the change is now, and the next natural roll comes `DaysBetweenChanges - DaysOfWarning`
  days later, as after a natural change. The original's converter (`0x512F00`) derives the kind from a rolled quality and the park's bands, which can tie-break with a draw and gives
  snow only where the park's snow band holds a quality (not in the jungle or fantasy); the cheat sets the kind explicitly with a fixed quality (the middle of the park's band for that kind, or 30 for rain, 10 for a thunderstorm and 20 for snow when
  the park has no such band) and a drop target (clear none and quality 100, rain and snow half the maximum, a thunderstorm three quarters with lightning). The drops ramp toward the target
  at the usual speed. It waits while the park's weather has not taken its first step.
- **Golden ticket.** The row cycles the ten tickets that have an original cheat, each shown with its Alt key; "Award ticket" queues the same `CheatKind.Ticket` as the key (K2), so the
  award, its silence and its provenance are the original cheat's.
- **Money and research.** "Add 10,000" and "Build everything" queue the Alt+1 and Alt+R cheats (K2), with their keys beside them.
- **Status.** Below the right column: whether the park is already marked as cheated or will be, and how many cheats wait for the next turn.

Each applied weather and arrival cheat leaves a short note on the screen, like the original's cheats (K2). Alt+C is the window's key only: the camcorder's C and the ride view's C
match no modifier, as the original's key contexts match the exact mask (`[BIN:TPW-EXE:0x40C900]`). Every label, with every choice it shows, fits its row at the page's size in all eleven
languages (checked with the localisation pack's fonts).

The weather and arrival cheats are the extension's own (`ParkCheats.IsExtensionKind`): they need the extension on and a level (`ParkEconomy.CheatHost`, the level as `IParkCheatHost`; a
headless economy refuses them), and once one applied the world save records `opentpw.cheats` (K1). The money, research, ticket and mode buttons are the original's cheats and record
nothing, so a park cheated only through them loads with the extension off. All of them go through the turn-boundary queue and take no draw; the pinned run with a scripted sequence is
`CheatsExtensionTests.CheatsFixedRunHash` ([DETERMINISM.md](DETERMINISM.md#cheats)).

## The ride vehicle limit (K4)

`[EXT:cheats-ride-vehicles]`. The cheats window's last two rows pick one of the park's rides (bumper cars, karts or a tour; two of a kind are numbered) and raise its vehicle limit. The
cheat (`CheatKind.RideVehicles`, argument the ride's attraction id) applies at the next park turn like every cheat, takes no draw, and is the extension's own: once applied, the world save
records `opentpw.cheats`. The ride keeps the raised limit until it is removed; it cannot be lowered again (the capacity slider can still be moved down).

- **The ceilings** (the user's decision): bumper cars and karts **32**, tours **40** cars. The water rides are not offered: their capacity is the riders per boat and the number of boats is
  already limited only by the module's 64 (`BumpController.MaximumVehicles`).
- **The capacity that fills them.** The capacity maximum (`OriginalObjectRuntime.MaximumCapacity`) grows by the same factor as the vehicles: a bumper ride 8 → 32, a kart ride 8 → 32 and
  CryptKarts 16 → 64 (two riders a kart), a tour 27 → 360 and Star Tours 24 → 320 (ceil(capacity / 9), / 8 for Star Tours, gives 40 cars). The ride window's capacity slider and the
  capacity setter use it; for a raised ride the setter takes a value past the original's byte (`ClampCapacity`; `0x4dd7f0` keeps the low byte), since 40 tour cars need a capacity of 360.
  The capacity itself does not change when the limit is raised: the player moves the slider.
- **The controllers.** A raised bump controller launches up to 32 cars or karts (`BumpController.VehicleLimit`, in place of the template's `MaxVehicles` at the launch, `0x549DB0`); the
  module's shared pool of 256 vehicles still refuses a launch when it is full. A raised tour controller has 40 car slots instead of 20 (`TourController.RaisedCarSlots`); a tour ride raised
  before its script created the controller gets them at the creation. The real scripts follow: the jungle bumper script launches 32 cars at capacity 32, and all four tour scripts ask for
  and fly 40 cars at the raised maximum (tests with the game data).
- **Karts past the grid.** The kart grid keeps its eight slots (`TrackSections.KartStationBuoys` with the template's count). A kart numbered past eight finds no grid buoy when it is launched
  in the loading phase, so it waits at the station reference where it was created and races from there when the race starts; 32 karts on the test loop all come home and every racer is
  handed back.
- **What does not follow the raised maximum.** The economy's wear reads the riders against the type's own `UsageInfo.MaxCapacity` (clamped in `ParkEconomy`), so a raised ride's wear is that
  of a full ride; the ride window's reliability gauge, which predicts that wear, reads a raised capacity above the type's maximum as that maximum.
- **Saves.** The runtime snapshot writes `VehiclesRaised` only for a raised ride, the bump controller its `VehicleLimit` and the tour controller its `CarSlots` only when raised, so every other
  save keeps its bytes; the load checks that the three agree (a live raised bumper or kart ride must carry its controller with the raised limit) and that a raised ride's capacity is
  within its raised maximum. The raised limit and the tour's extra slots join the hash only
  for a raised ride.
- **The original-format export** refuses a park with a raised ride whose capacity is above its type's original maximum (`OriginalParkExportOptions.RidesAboveOriginalLimits`,
  `[EXT:cheats-export]`, the user's decision "refuses rides above the original limits"): the original keeps a ride's capacity in a byte and clamps it to its type's maximum. A raised ride
  still within the original's maximum is no obstacle. The export reads only the economy snapshot, which holds no rides, so its caller passes the level's count
  (`Level.RidesAboveOriginalLimits`); OpenTPW has no in-game caller of the export yet (the tests and tools call it).
- **Pin.** `CheatsRideVehicleTests.CheatsBumperRaisedRunHash` pins the jungle bumper controller raised to 32 cars, loaded with 32 guests and run through a 30-second session
  ([DETERMINISM.md](DETERMINISM.md#cheats)).

## Phases

- **K0** (this document): the trace, the capacity data, COMPAT-007 and SAVE-010 narrowed.
- **K1** (built): the extension (off, not yet selectable), the park's cheat record in the save, the used-only and sticky recording, the turn-boundary queue, the cheated mark shown.
- **K2** (built): the original cheat mode: the secret name in `Level.RenamePark`, the cheat key context, Alt+R, Alt+1, the ten tickets, the import of `CHTS` and the original-format export's own `CHTS`
  (moved from K1: the export round trip of the corpus saves needs the import).
- **K3** (built): the extension becomes selectable; the cheats window (Alt+C): the cheat mode, arrivals by vehicle, the weather, a chosen ticket, money and research; `CheatsFixedRunHash`.
- **K4** (built): the ride vehicle limit for bumper cars, karts and tours (32, 32 and 40), its window rows, saves, export refusal and pin; captures.
- **K5** (optional, not planned): coaster trains.
