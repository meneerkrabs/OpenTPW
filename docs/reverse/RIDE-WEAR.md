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
- If `GetAbsolute` returns microseconds, the target is in milliseconds, so one step is 31 ms (about 32.3 steps
  per second) and one wear step about every 2 s. Not proven: the origin of the target and any game-speed
  factor.
- No reader of the counter that derives the hour or day has been found yet, so ECON-001 is unchanged.

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

The ride update `0x100e077c` runs on ticks where `(counter & 7) == 0`. It skips rides in states 1, 3 and 4
(`ride + 0x198`) and rides with `ride + 0x64 == 0`.

1. It calls the wear step, which acts only on every eighth of these calls, because of its own `& 0x3f` gate.
2. It checks for a breakdown. A ride breaks down when the truncated state of repair (`+0x40`) is 0, or when a
   second gauge (`+0x44`) is truncated to 0. Two further conditions apply: bit 0 of `ride + 0x2e` is clear,
   and the ride is not already broken (statistic 4 is 0). There is no random roll.
3. If the ride breaks down because repair reached 0 while the gauge is still above 0, the gauge first loses
   5.0 (clamped to 0–100).
4. `0x100e09f4` posts an announcement and sets ride statistic 4 to 1. Statistic 4 is the broken-down state.

The wear step itself sets statistic 8 below 20. Statistic 8 is a worn warning; its reader is not yet found.

`0x100def2c` repairs a ride: state of repair becomes 100.0 and statistic 8 is cleared. If `ride + 0x198 == 2`,
the same function instead advances the upgrade level (`+0x4c`). It does not touch the second gauge.

The second gauge falls by `0.02 * wear` per wear step and by 5.0 per breakdown, and no repair restores it.
It behaves like a remaining-life meter, which may be the Remaining Life value in UI-028. That is a hypothesis.

Not verified (agent reports, recorded as leads):
- `0x100d9e04` clears statistic 4 and calls the repair when a job timer ends, except when statistic 7 is set.
- `0x100df5b4` repairs on a timer without clearing statistic 4.
- The constructor `0x100da874` sets `+0x40`, `+0x44` and `+0x48` to 0.

Verified since: the object serializer `0x100daf04` writes the three floats as whole numbers 0–255, and
`SaveAttractionList` decodes them from original saves (see
[TPWS-PAYLOAD.md](../TPWS-PAYLOAD.md#attraction-records-thing-list-in-the-prefix-tail)). In Easymode all three are 100.

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

`ParkEconomy` follows the original ride update (`UpdateRides`, `Wear`, `WearAmount`):

- Every 8 park turns each ride gets the breakdown check. On every 64th turn the wear step runs first. The park
  turn is the original world counter `+0x1DA70C`, so no conversion is needed.
- The wear step reads `VAR_RUNNING`, `VAR_ONRIDE` and the speed through `IRideOperations`. In the game,
  `GuestEconomyBridge` supplies them from the linked ride's script; the speed is the ride record's InitSpeed,
  because OpenTPW has no speed control. Objects without a linked running script do not wear.
- The state of repair (`Repair`, shown truncated as `StateOfRepair`) and the life gauge (`LifeGauge`) are kept
  exactly and saved in OpenTPW's park save.
- A breakdown happens when the truncated life gauge or state of repair is 0. Repair sets 100 and does not
  restore the life gauge.

Still approximations:

| ID | What |
| --- | --- |
| ECON-023 | The ride update skips rides in states 1, 3 and 4 (`+0x198`) and rides with `+0x64 == 0`, and the breakdown check skips `+0x2E` bit 0. OpenTPW maps these only to "a mechanic is at work". |
| ECON-047 | A newly built ride starts with life gauge 100. Easymode stores 100, but the constructor sets 0 and the code that fills it in was not traced. |

Not in OpenTPW yet:

- The breakdown and worn state reaching the ride script (`VAR_BREAKSTAT`, `VAR_WORN`). Guests and queues still
  read `VAR_BROKEN` only.
- The message when the state of repair crosses below 10.
- Online mode, which OpenTPW does not have.
- OpenTPW's own `RideWorn` event and mechanic dispatch keep the Advisor.sam threshold 25.

Remaining evidence: two original saves of one park at a known turn distance, with a ride in use. They would
check the implementation against the original; `SaveAttractionList` can read them.
