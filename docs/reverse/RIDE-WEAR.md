# SAM schema layout, ride wear and breakdown (ECON-023/024)

2026-10-09. Static analysis only; no original program was run. Original binaries, decompiler output and
disassembly stay outside this repository; this page records addresses, field offsets and the conclusions
drawn from them.

## Sources and weight

| Binary | Identity | Weight |
| --- | --- | --- |
| Mac `SimThemePark.data` (PEF, PowerPC) | SHA-256 `04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5` (see [FINDINGS.md](FINDINGS.md)) | **Evidence**: Theme Park World itself |
| Theme Park Inc `Game.exe`, scene noCD build (x86) | 6,742,016 bytes; SafeDisc wrapper removed by a third party in 2001 | **Lead only**: a later game on the same engine |

Tools: Ghidra 12.1.4 headless. The Mac container was imported as `PowerPC:BE:32:default`; the loader's
automatic choice (VLE) is wrong for this binary. `FindFunctionsUsingTOCinPEFScript` ran after analysis.
Addresses below are Ghidra addresses: Mac code at `0x10000000`, Mac data section at `0x101ebeb0`;
Theme Park Inc at its PE image base `0x400000`.

## SAM schema records

Every kind of `.sam` balance file has a static schema in the executable: an array of 60-byte records.

| Offset | Size | Meaning |
| --- | --- | --- |
| +0x00 | 4 | type |
| +0x04 | 32 | field name, NUL-terminated |
| +0x28 | 4 | maximum, for type 6 (e.g. `Group` 255, `Category` 7, `EstimatedPriceVariance` 100) |
| +0x34 | 4 | array length, for type 3 |

The Mac stores the same records big-endian.

| Type | Meaning | Storage |
| --- | --- | --- |
| 0, 1 | struct boundary; type 1 names the struct that just ended | none |
| 2 | array begin (unnamed) | none |
| 3 | array end: array name and length | one count slot after the elements |
| 4–11 | value: 5 = integer, 6 = integer with maximum, 7 = float, 10 = string, 11 = enum; 4, 8 and 9 not yet identified | one 4-byte slot |
| 12 | end of schema | none |

Type 7 is a float because the wear code below reads `RideBreakDown.Imminent`/`WarnPlayer` (Theme Park Inc)
with float loads, while type-5 fields are read with integer loads.

### Slot layout (Theme Park Inc base constructor `0x418f60`, read from the disassembly)

- Slots are numbered from 1. Every value record takes slot `pos` at `data + 4*pos`, then `pos += 1`.
- An array with `n` fields per element and length `cnt` stores its elements inline and element-major.
  Field `j` of element `k` sits in slot `start + k*n + j`. One count slot follows at `start + cnt*n`,
  and `pos` continues at `start + cnt*n + 1`.
- Each schema class has a two-entry vtable: `GetField(i)` returns `base + 60*i`, and `GetData()` returns
  `this + 8`. In the ride settings object the schema sub-object sits at +4, so slot `s` is at object
  offset `0xc + 4*s`.
- Theme Park Inc reaches these functions through MSVC incremental-link `jmp` stubs, and the getters
  only through vtables, so plain cross-references to the schema arrays are empty.

### Ride schema

| | Theme Park Inc | Theme Park World (Mac) |
| --- | --- | --- |
| Schema base | `0x9a2cc0` | `0x10225294` (data + 0x393e4) |
| Records / slots | 211 / 245 | 163 / 160 |
| `Upgrades[]` element | 18 fields (stride 0x48) | 16 fields (stride 0x40) |
| `UsageInfo.MaxCapacity` | +0x1e4 | +0x128 |
| `UsageInfo.MaxSpeed` | +0x1f4 | +0x138 |
| `Upgrades[k].RedLineCapacity` | +0x290 | +0x19c |
| `Upgrades[k].RedLineSpeed` | +0x2a0 | +0x1ac |
| `Upgrades[k].WearRate` | +0x2a4 | +0x1b0 |

Offsets are relative to the ride settings object. On the Mac the `+0xc` base is inferred, not read from a
constructor: all five offsets the wear code uses fall exactly on the slots predicted by the layout above.

## Ride wear

Mac functions: `0x100dec2c` (wear step) and `0x100de904` (usage factor). They were found through the
float literal pool at data + 0x54a0…0x54bc (20.0, 0.02, 9.0, 10.0, 0.5, 1.1, 0.1, 0.9). The Theme Park Inc
counterparts are `0x5e25b0` and `0x5e21a0`, with the same structure.

The ride's upgrade level is a byte in the ride instance (`+0x4c` Mac, `+100` Theme Park Inc).

**Gate.** The wear step does nothing when:
- `Upgrades[level].WearRate` is 0;
- the game runs in online mode (see [Game mode](#game-mode));
- the ride is not running (helper `0x100dff2c` or instance flag `+0x2e & 0x100`);
- the global tick counter is not a multiple of 64 (`(counter & 0x3f) != 0`; counter at `*DAT_101ec904 + 0x1da70c`,
  see [Tick counter](#tick-counter)).

**Step.** When ride statistic 5 is 0 (interpreted as no riders), the wear is 0. Otherwise:

| Quantity | Formula |
| --- | --- |
| `riders` | statistic 5, clamped to `[0, MaxCapacity]` |
| speed factor `s` | `0.9 * speed / MaxSpeed`, then `(s + 1.1) * 0.5` if `RedLineSpeed[level] <= speed`, else `s + 0.1` |
| capacity factor `c` | `0.9 * riders / MaxCapacity`, then `(c + 1.1) * 0.5` if `RedLineCapacity[level] <= riders / 10`, else `c + 0.1` |
| `wear` | `(s + c) * 0.5 * WearRate[level]` |

**Effects.**
- State of repair (float at instance `+0x40` Mac, `+0x54` Theme Park Inc) becomes `clamp(repair - wear, 0, 100)`.
- A second float (`+0x44` Mac, `+0x58` Theme Park Inc; meaning unknown) loses `0.02 * wear`, clamped the
  same way. Theme Park Inc skips this when `IsSpecial == 1`; the Mac does not check.
- Mac: below 20 a status flag 8 is set, and crossing below 10 in this step posts message `0x42`.
  Theme Park Inc instead takes both thresholds from SAM (`RideBreakDown.WarnPlayer`, `RideBreakDown.Imminent`)
  and posts message `0x40`. Theme Park World has no `RideBreakDown` struct.

The bracketed quantities come from the binaries without interpretation. "Speed", "riders" and "running"
are interpretations, based on the comparisons with `MaxSpeed`, `MaxCapacity` and the `RedLine*` fields.

## Tick counter

- `0x1010536c` increments the counter at `game + 0x1da70c` by one per simulation step. `0x1010474c` resets it
  to 0 at game initialisation.
- The main loop `0x101c22dc` adds 31 to an accumulator per step until it reaches a target. The same function
  computes `SamsUtilities::UTimer::GetAbsolute() / 1000`.
- Superseded (ORACLE.md F30): the counter at `+0x1da70c` is the park turn, which the scheduler advances once per 248 ms at
  the default clock scale (eight 31 ms substeps), so a wear step (every 64 turns) comes about every 15.9 s at normal speed,
  not every 2 s as this page first estimated from the 31 ms substep.

## Game mode

`DAT_101ec8f8` holds a mode value G. It is initialised by `0x1012bb64` from three bits of a flag word and
written by `0x1012bbf4`. The game initialisation `0x1010474c` loads a different balance file per value:

| G | Balance file | Mode |
| --- | --- | --- |
| 0 | `data:levels:Standard.sam` | normal |
| 1 | `data:levels:online_Standard.sam` | online |
| 2 | the theme's `Easy_Standard.sam` (error text "There is no Easy_standard.sam file for this theme") | Easy |

The strings sit in the code section: TOC slot `0x101ef5dc` relocates to code + `0x1d369c`.

The wear step is off only in online mode. In online mode `0x100e2650` takes over: state of repair falls by
an amount / 20, and below 25.0 it is restored to 100. OpenTPW does not use the online balance, so for OpenTPW
wear always applies.

## Breakdown and repair

Updated 2026-10-12: the routines below are executed in the PowerPC oracle (ORACLE.md F27; the targets `ride-breakdown-update`, `ride-set-state`,
`ride-repair` and `ride-may-open`), no longer only read. Offsets: the object state word `+0x198` (408), the open word `+0x64` (100), the maintenance flag
`+0x60` (96), the mechanic reservation `+0x5A` (90), the wear gauge `+0x40`, the life gauge `+0x44`, the pending guest `+0x68` (104).

The ride update `0x100e077c` runs on ticks where `(counter & 7) == 0`.

1. A ride in state 3 or 4 is not updated at all. A ride in state 1 skips the next steps (the relief test below still runs), and so does a ride whose open word
   `+0x64` is 0, which includes every closed ride and state 2.
2. It calls the wear step, which acts only on every eighth of these calls, because of its own `& 0x3f` gate.
3. It checks for a breakdown. A ride breaks down when the truncated state of repair (`+0x40`) is 0, or when the second gauge (`+0x44`) is truncated to 0
   (each taken as a byte). Two further conditions apply: bit 0 of `ride + 0x2e` is clear (a relief object never breaks down), and the ride is not already
   broken (statistic 4 is 0). There is no random roll.
4. If the ride breaks down because repair reached 0 while the gauge is still above 0, the gauge first loses 5.0 (clamped to 0–100).
5. `0x100e09f4` posts an announcement and sets ride statistic 4 to 1. Statistic 4 is the broken-down state (VAR_BREAKSTAT).
6. A relief object whose state of repair byte is below 25 gets statistic 8 (VAR_WORN) set to 1 (`0x100e0a40`).

The ride script then reports VAR_BROKEN (statistic 7) itself. The next ride update in state 0 (`0xe0a8c` → `0xe1864`) runs the admission, reads VAR_BROKEN and
calls `SetState(1)` (`SetState(4)` when the life gauge byte is 0), which clears the open word and writes VAR_RIDECLOSED = 1 (ORACLE.md F15, F27).

`0x100def2c` repairs a ride (executed for a ride that is not being upgraded): the state of repair becomes **100.0** (the float at TOC −11016), statistic 8 is
cleared, the mechanic reservation and the maintenance flag are cleared, statistic 4 is cleared, and the ride is **opened** (open word 1, VAR_RIDECLOSED 0,
`SetState(0)`) whatever the reopening guard `0x100de654` says. A coaster without a complete circuit is closed again. If `ride + 0x198 == 2`, the same function
instead advances the upgrade level (`+0x4c`); that branch is not executed. The life gauge is not restored by a repair.

The second gauge falls by `0.02 * wear` per wear step and by 5.0 per breakdown, and no repair restores it.
It behaves like a remaining-life meter, which may be the Remaining Life value in UI-028. That is a hypothesis.

`0x100d9e04` (a mechanic's job timer; static): for a ride in state 1 it clears statistic 4 and then repairs only once statistic 7 (VAR_BROKEN) is not positive, so the
job waits for the script; a ride in any other state is repaired at once. `0x100df5b4` repairs a relief object on a timer without clearing statistic 4.
The timer the arrival `0x100d9c0c` starts was executed (ORACLE.md F19): a repair takes `WorkDuration × (100 − low byte of the truncated +0x40) / 100`
park turns, an upgrade (ride state 2) `WorkDuration × Upgrades[level + 1].DurationOfUpgrade`; the ride's own level does not scale a repair.

The constructor `0x100da874` first stores 0 in `+0x40`, `+0x44` and `+0x48`, and then 100.0 (`0x100dab1c`; this retired ECON-047).

Verified since: the object serializer `0x100daf04` writes the three floats as whole numbers 0–255, and
`SaveAttractionList` decodes them from original saves (see
[TPWS-PAYLOAD.md](../TPWS-PAYLOAD.md#attraction-records-thing-model-3)). In Easymode all three are 100.

## Ride statistics are the ride script's variables

The statistics the wear and breakdown code reads and writes are the variables of the ride's script (RSE).
`0x100B5758` finds the ride's script instance, and `0x100B5BE0`/`0x100B57D4` read and write
`variables[index]` with a bounds check against the variable count. The indices match OpenTPW's
`RideVariables`:

| Index | Use in the Mac code | Variable |
| --- | --- | --- |
| 2, 3 | passed to the wear amount (unused on the path the wear step takes) | `VAR_CAPACITY`, `VAR_DURATION` |
| 4 | set to 1 by the breakdown (`0x100E09F4`) | `VAR_BREAKSTAT` |
| 5 | riders; 0 means no wear | `VAR_ONRIDE` |
| 7 | checked by the repair job (`0x100D9E04`) | `VAR_BROKEN` |
| 8 | set to 1 below 20, cleared by a repair | `VAR_WORN` |
| 9 | non-zero while the ride runs | `VAR_RUNNING` |

The speed is the script's own field `+0xC0` (`0x100B5B40`). The wear step calls `0x100DE904` with `r7 = 0`
(`0x100DED70`), so the rider term uses `VAR_ONRIDE`, clamped to `UsageInfo.MaxCapacity`.

## In OpenTPW

`ParkEconomy` follows the original ride update (`UpdateRides`, `Wear`, `WearAmount`; `RideBreakdownRules` is the pure form the oracle compares):

- Every 8 park turns each ride that is open and not broken down (and has no upgrade waiting) gets the breakdown check. On every 64th turn the wear step runs first. The
  park turn is the original world counter `+0x1DA70C`, so no conversion is needed. A closed ride does not wear or break down; a mechanic at work on a ride in state 0 does
  not stop its update.
- The wear step reads `VAR_RUNNING`, `VAR_ONRIDE` and the speed through `IRideOperations`. In the game,
  `GuestEconomyBridge` supplies them from the linked ride's script; the speed is the ride record's InitSpeed,
  because OpenTPW has no speed control. Objects without a linked running script do not wear.
- The state of repair (`Repair`, shown truncated as `StateOfRepair`) and the life gauge (`LifeGauge`) are kept
  exactly and saved in OpenTPW's park save.
- A breakdown starts when the truncated life gauge or state of repair is 0 (`0x4E0B90`). For an object a script runs it goes through the script as in tp.exe: the
  economy writes `VAR_BREAKSTAT = 1`, the script answers with `VAR_BROKEN`, and the ride update's state 1 (`0x4E14E0`, read in OpenTPW through the visitor bridge's
  broken state) breaks the object down on its rising edge and calls a mechanic. The mechanic's repair writes `VAR_BREAKSTAT = 0` when its timer ends and waits while
  `VAR_BROKEN` stays set (`0x4DA830`), on the headless and on the world path. The repair (`0x4DF8F0`) sets 100 (`RideRepair.FullGauge`), writes `VAR_WORN = 0`, does
  not restore the life gauge and opens the ride (`Level.OpenAfterRepair`; a coaster with an incomplete circuit stays closed). The last observed state 1 is saved, so a
  load counts no second breakdown.
- The wear step writes `VAR_WORN = 1` when the repair byte falls below 20 (`0x4DF670`); a relief object (toilet) writes it below 25 on every update (`0x4E0D16`).
- The park door and the open buttons ask the reopening guard (`RideOpening.MayOpen`, `ParkGate.MayOpen`): a ride the economy has broken down, a ride with an upgrade on its
  way and a coaster without a complete circuit stay closed; the script's `VAR_BROKEN` still counts as a breakdown.

Still approximations:

| ID | What |
| --- | --- |
| ECON-023 | The ride's state word is split between the visitor bridge (state 1) and the economy's broken flag, which the level joins on the rising edge of state 1; state 4 is told apart only by the economy's life gauge (QUEUE-019); a headless economy without ride scripts breaks an object down at once and repairs it without the wait. |
| ECON-049 | The back-of-queue link test of the reopening guard runs only for rides with real queue cells (entrance cells keep no link byte). |

Not in OpenTPW yet:

- The message when the state of repair crosses below 10.
- Online mode, which OpenTPW does not have.
- OpenTPW's own `RideWorn` event and mechanic dispatch keep the Advisor.sam threshold 25.

Remaining evidence: two original saves of one park at a known turn distance, with a ride in use. They would
check the implementation against the original; `SaveAttractionList` can read them.
