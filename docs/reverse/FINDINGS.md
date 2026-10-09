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

## Direct timer-call evidence (2026-10-09 follow-up)

Run the dependency-free, identity-pinned operand verifier:

```sh
python3 tools/ppc-analysis/timer_evidence.py /Users/sander/server/game-assets/mac-feral/bin
python3 -m unittest discover -s tools/ppc-analysis -p 'test_*.py' -v
```

It verifies the three SHA-256 identities listed above, resolves transition
vectors and selected import relocations, and checks selected branch targets,
register operands and constants. It outputs interpreted metadata only. The
inspection never executes original instructions and bypasses the heuristic
function-discovery/cache layer. A locally available disassembler was used to
review the control flow, but is not a dependency of the checked-in verifier;
no original disassembly or binary bytes are stored here. The checks are bounded
witnesses for these exact binaries, not a general decompiler or formal proof.

| Export | Container | Transition vector (section 1) | Code entry (section 0) | TOC offset (section 1) |
| --- | --- | ---: | ---: | ---: |
| `LbTime_GetClock__Fv` | bullfrog_shared | `0x1d14` | `0x38bb8` | `0` |
| `GetAbsolute__Q213SamsUtilities6UTimerFv` | sams_utils_shared | `0x11e4` | `0xa4f8` | `0` |
| `SetRate__Q213SamsUtilities6UTimerFRCQ213SamsUtilities13UMicrosecondsUc` | sams_utils_shared | `0x11d4` | `0xa738` | `0` |

**Verified local fact: the low-level clock wrapper divides a 64-bit absolute
timer result by 1,000 and returns the low 32 bits of the integer quotient.** At
bullfrog code `0x38bc8`, the wrapper calls import glue `0x5ad60`; its TOC slot
`0x10` relocates to `UTimer::GetAbsolute()` from `sams utils shared`. The wrapper
reads both result words, supplies the two-word denominator `(0, 1000)` and
calls the local arithmetic helper at code `0x59d98` from `0x38be0`. It then
returns the low quotient register. Static review of helper range
`[0x59d98, 0x59e84)` identifies unsigned long division: leading-zero
normalization, repeated carry shifts and trial subtraction of the two-word
denominator produce quotient bits; an input smaller than the divisor returns
zero. Its SHA-256 is
`9a130bd65ecb0edbca64d29fd90f712e96be72cd7089a05b7e64a2fe4af41d83`.
The helper's arithmetic meaning was reviewed manually; the verifier checks its
identity and caller operands rather than executing or formally verifying it.

**Verified conditional unit: the fallback route supplies microseconds.** In
sams `GetAbsolute`, code `0xa664` calls glue `0x1f6cc`; TOC slot `0x2a0`
relocates to `Microseconds` from `InterfaceLib`. That route reads the returned
high/low words and copies them unchanged to the output at `0xa674–0xa678`.
Apple's [Unsigned Wide Record documentation](https://developer.apple.com/library/archive/documentation/mac/OSUtilities/OSUtilities-103.html)
defines that API's result as microseconds since system startup, with high and
low 32-bit fields. Consequently, **when this fallback route is selected**, the
wrapper expression is `floor(system_microseconds / 1000) modulo 2^32`, a
millisecond counter with a wrap period of about 49.71 days. The branch is
conditional on earlier timer capability flags/pointers; no original runtime
was observed selecting it. This is a clock representation, not a simulation
rate, pause policy or deterministic update schedule.

Other `GetAbsolute` routes read hardware time or dynamically resolved timer
services and apply conversions. Literal doubles in sams section 1 include
`0x8c0 = 1000.0`, `0x8d0 = 60.15`, and `0x8d8 = 60000000.0`. The latter two
occur in initialization/calibration involving `LMGetTicks` (its import glue
is code `0x1f714`, TOC slot `0x1e4`). Their literal values and local arithmetic
use are established; they do not establish animation or park-calendar Hz.
The selected dynamic route, calibration result and environmental accuracy
remain runtime dependencies. Apple also documents the hardware-dependent
relationship between `Microseconds`, `UpTime` and the native Time Manager in
[Technical Note TN1063](https://developer.apple.com/library/archive/technotes/tn/tn1063.html).

**Verified application initialization: one timer receives raw interval
100,000 and flag 1.** In `SimThemePark.data`, code `0x310–0x334` constructs
`(2 << 16) - 0x7960 = 100000`, stores high word zero and that low word in a
stack record, and passes it to a timer object at offset `0x5c` from the
surrounding object's pointer. The call at `0x338` reaches import glue
`0x1c4524`; the executable's TOC base is `0x8000`, and the relocated import
slot is section 1 `0x6f4`. It imports the `SetRate` export above. That callee
copies the interval words unchanged to object offsets `0x0c/0x10` and the
flag byte to `0x08`, without a scaling operation. The verifier establishes
the interval and field-copy fact, but the surrounding object's role and
consumer path have not been recovered. It would be unjustified to call this
a 10 Hz gameplay/animation update merely because 100,000 microseconds is
one tenth of a second.

Confidence: **high** for identities, transition-vector resolution, imports,
operand values and field copies; **high, conditional** for the API-backed
fallback millisecond interpretation; the division-helper semantics rely on
manual static reasoning. No original execution/capture corroborates the
selected timer route or application consumer.

No `ADVISOR`, `RIDES` or `ECON` timing approximation can yet be removed.
Concrete dependencies remain:

- `RIDES-001`: connect the animation clip advancement/RSE consumers to the
  timer values and recover their frame-index/elapsed-time conversion.
- `ECON-001..004`: identify the park calendar and speed/pause consumers,
  including how game days and months advance. OS clock units cannot supply
  those game rules.
- `ADVISOR-008`, `ADVISOR-010..013`: identify speech start, audio-consumption
  timing and the original LIP consumer. This timer wrapper does not reveal
  those synchronization or LIP interpretation rules.

The heuristic `analyze.Analysis` path hit a false-positive traceback-table
candidate while examining `bullfrog_shared`; it attempted an out-of-range
name read. The evidence above uses explicit exports and loader relocations
instead. General heuristic function boundaries remain untrusted, as already
noted in the reader limitations.
