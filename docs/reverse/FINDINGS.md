# Classic Mac PowerPC evidence

2026-10-09. Static metadata inspection only; the original program was not run.
The local Feral corpus is `/Users/sander/server/game-assets/mac-feral/bin`.
Original `.data`, `.bin`, disk images, expanded sections and disassembly stay
outside this repository.

## Reproduce

From the repository root, with Python 3 and no third-party packages:

```sh
python3 -m unittest discover -s tools/ppc-analysis -p 'test_*.py' -v
python3 tools/ppc-analysis/inventory.py /Users/sander/server/game-assets/mac-feral/bin/*.data /Users/sander/server/game-assets/mac-feral/bin/libraries/*.data
python3 tools/ppc-analysis/inventory.py --symbols UTimer /Users/sander/server/game-assets/mac-feral/bin/*.data /Users/sander/server/game-assets/mac-feral/bin/libraries/*.data
python3 tools/ppc-analysis/inventory.py --symbols LbTime_GetClock /Users/sander/server/game-assets/mac-feral/bin/*.data /Users/sander/server/game-assets/mac-feral/bin/libraries/*.data
```

The JSON inventory sorts input paths and keys and contains file identities,
section sizes, entry vectors, imports, exports and symbolic relocation counts.
`--symbols` adds case-insensitive name matches with demangled display names.
Export offsets are offsets in the stated section; a `tvector` export is a
transition vector, **not** the code entry address. Symbol names establish an
interface, not its implementation or time unit.

## Observed corpus

All 16 containers parse successfully: 2,392 import records, 3,979 exports and
38,072 relocated words. Imports include repeated interfaces across containers;
these counts are not distinct functions. All have three sections: code,
pattern-initialised data and loader. The executable has no exported symbols.

| Container | Imports | Exports | Relocated words |
| --- | ---: | ---: | ---: |
| SimThemePark | 716 | 0 | 17,393 |
| bullfrog_shared | 180 | 529 | 3,146 |
| c_c++_shared | 273 | 750 | 3,593 |
| engine_shared | 142 | 198 | 1,493 |
| indirectx_shared | 80 | 87 | 602 |
| libjpeg_shared | 31 | 101 | 846 |
| ltms_shared | 41 | 245 | 851 |
| macdoze_shared | 154 | 187 | 1,032 |
| mail_shared | 40 | 11 | 151 |
| more_files_shared | 132 | 160 | 475 |
| online_shared | 64 | 124 | 591 |
| runtime_shared | 5 | 57 | 229 |
| sams_utils_shared | 336 | 494 | 2,911 |
| sound_shared | 91 | 951 | 3,915 |
| winsock_shared | 86 | 59 | 646 |
| zlib_shared | 21 | 26 | 198 |

Selected SHA-256 identities:

- `SimThemePark.data`: `04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`
- `bullfrog_shared.data`: `b67b56b7b2b75962b8b34559e20fd97b623b2d0f7f08a0035f82072ffbf4ec06`
- `sams_utils_shared.data`: `1959a54b2280c95ddcc25ec77c070b4d609dcca7b7e59683b460c92c7297bcd6`

## Clock leads, not behavioral proof

`SimThemePark.data` imports `LbTime_GetClock__Fv` from `bullfrog shared` and
`GetAbsolute__Q213SamsUtilities6UTimerFv` and
`SetRate__Q213SamsUtilities6UTimerFRCQ213SamsUtilities13UMicrosecondsUc`
from `sams utils shared`. The matching exports are:

| Demangled interface | Container | Section | Transition-vector offset |
| --- | --- | ---: | ---: |
| `LbTime_GetClock()` | bullfrog_shared | 1 | 7,444 |
| `SamsUtilities::UTimer::GetAbsolute()` | sams_utils_shared | 1 | 4,580 |
| `SamsUtilities::UTimer::GetAbsoluteCycles()` | sams_utils_shared | 1 | 4,588 |
| `SamsUtilities::UTimer::GetElapsed()` | sams_utils_shared | 1 | 4,596 |
| `SamsUtilities::UTimer::SetRate(const SamsUtilities::UMicroseconds&, unsigned char)` | sams_utils_shared | 1 | 4,564 |
| `SamsUtilities::UTimer::HasTicked()` | sams_utils_shared | 1 | 4,572 |

This narrows the timer investigation but does not connect those interfaces to
the MD2/RSE animation frame advance or park calendar. No animation-clock or
economy approximation is resolved by this inventory. `ECON-001` and all other
unverified rates/formulas retain their approximation status.

## Reader validation and limits

The reader expands all five supported pidata forms and zero-fills instantiated
sections. Relocations are interpreted symbolically into section/import targets;
no original machine instructions are executed. Tests use synthetic fixtures
only, covering pidata encoding overhead, repeats/interleave, truncation,
invalid sizes/strings, relocation failures, and deterministic inventory output.

Bounds checks reject truncated container/loader tables and section payloads,
out-of-range import/section references, truncated relocation instructions and
output exceeding declared sizes. Explicit limits are 64 MiB of total section
size, 1,000,000 symbolic relocation steps, 32 recursive repeat levels and export
hash power 20. These are toolkit resource limits, not claims about PEF format
limits. Every current local corpus member fits within them.

The reader is a bounded analysis aid, not a complete PEF verifier. Successful
parsing does not independently validate every relocation opcode's semantics,
loader version compatibility, instantiated-section ordering, export hash keys
or execution behavior. The existing `analyze.py` heuristic function/RTTI/TOC
and value tracking remains exploratory; its pickle cache is outside the
repository and must only be used with trusted local cache files. Inventory
runs bypass that cache and do not use heuristic function discovery.

Next evidence needed: trace the animation/calendar caller paths to these timer
interfaces and verify conversion constants against an original runtime capture.

## Game types and the Easy_/Online_ settings layers (2026-10-09)

Static analysis of the same `SimThemePark.data` (SHA-256 `04809cd4…e295f5`,
here the data fork of `Theme Park Data/SimTheme Park` from the Mac CD) with Ghidra
11.4.2 headless (PEF loader, default analysis). Addresses are Ghidra's, with the
code section at `0x10000000`; the TOC register r2 is `0x101F4AB0` at every
function entry. Strings are reached through TOC slots holding a pool base plus an
immediate offset; they were resolved with local scripts. Decompiler output,
memory dumps and scripts stay outside the repository; below are descriptions in
our own words.

- **Game type** (`0x1012BBF4`, asserts "Invalid GameType in SetGameType"): three
  values. 0 sets flag bit `0x00800000` and clears the others, 1 sets `0x01000000`,
  2 sets `0x02000000`, in one global flag word. `0x1012BB64` derives the type back
  from that word (bit `0x02000000` → 2, else `0x01000000` → 1, else 0).
- **Settings layers at world setup** (`0x1010474C`):
  - type 1 loads `data:levels:online_Standard.sam`, then the theme's
    `online_Standard.sam`, *instead of* the two `Standard.sam` files;
  - otherwise `data:levels:Standard.sam`, then the theme's `Standard.sam`;
  - type 2 additionally loads the theme's `<prefix>Standard.sam`, where the prefix
    is the engine name-table entry `Easy_`; if that file is missing the game only
    logs "There is no Easy_standard.sam file for this theme - This is not critical".
- **Name table** (`0x101BF60C`): a constructor of fixed engine strings, including
  `Easy_`, `Online_`, `standard.sam`, `online_standard.sam`, `online_rides.sam`,
  `onlineoverride.sam`, the detail presets (`low/med/high/custom.sam`) and the file
  extensions the game uses (`.tps`, `.tpc`, `.fps`, `.fpc`, `.tgq`, `.wad`, …).
- **Player profile** (serialiser `0x10129308`): fields `mEarnedGlobalTicket[i]`,
  `mEarnedSecretTicket[i]`, `mSpentTickets`, `mExtraKeys`, `mEasyModeUser` (byte at
  +0x24), `mSwearFilterOn`, `mFirstTimePlayer`.
- **Easy-mode park copy** (`0x10137600`): creates a save directory per level and,
  when its flag argument is set, copies the theme's file named with the engine
  string `easymode` into it.
- The lobby/globe code (`0x100923A8`) sets bit `0x02000000` directly; `0x1009261C`,
  `0x10092C08` and `0x10092F94` clear it.

**Not yet closed:** where `mEasyModeUser` selects game type 2, i.e. that type 2 is
the manual's "Instant Action". The names (`mEasyModeUser`, `Easy_`, `easymode`,
"RESET_FOR_EASY_MODE") make it likely, not proven. The Instant Action rules from the
manual and `UITEXT.str` (no upgrades or loans, automatic research) are not traced yet
and stay approximations.

**Consequence for OpenTPW:** `RIDES-009` ("Easy_/Online_ overlays not applied") can
be resolved for `Standard.sam` from `0x1010474C`. Online uses `online_Standard.sam`
as a replacement, not as an overlay on `Standard.sam`.
