# SAM schema layout and ride wear (ECON-023 evidence)

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
- a global flag is set (Mac `DAT_101ec8f8`, initialised lazily by `0x1012bb64`; meaning unknown);
- the ride is not running (helper `0x100dff2c` or instance flag `+0x2e & 0x100`);
- the global tick counter is not a multiple of 64 (`(counter & 0x3f) != 0`; counter at `*DAT_101ec904 + 0x1da70c`).

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

## Consequences for ECON-023

OpenTPW currently assumes that "an open ride loses WearRate state of repair per game day; breakdown at 0".
The Mac code differs:

- Wear depends on use: `0.1` to about `1.05` times `WearRate` per step, and zero without riders.
- It runs once per 64 ticks, not once per game day.
- There are warning thresholds at 20 and 10.

ECON-023 stays open until these are known:

- the unit of the 64-tick counter (shared with ECON-001);
- the meaning of ride statistics 2, 3 and 5 and of the second float;
- the global disable flag;
- the breakdown decision itself, which is not in the wear step;
- agreement with an oracle, for example two saves of one park at a known tick distance, if the save stores
  state of repair.

The repo's acceptance rule for the original-binary route requires one bounded original function plus
reproducible agreement with a reference oracle. The first half is met by `0x100dec2c`/`0x100de904`; the
second is not.
