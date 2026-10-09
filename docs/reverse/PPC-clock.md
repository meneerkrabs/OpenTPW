# PowerPC clocks and their consumers

Static evidence from the identified Feral Mac release, 2026-10-09. No original
instructions were executed. Addresses are unpacked PEF section offsets, not
process addresses. Application code is section 0, initialized data is section 1,
and its TOC base is `data:0x8000`. Sams and Bullfrog use TOC `data:0` here.
This evidence does not establish Windows baseline or Patch 2 behavior.

| Binary | SHA-256 |
| --- | --- |
| `SimThemePark.data` | `04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5` |
| `sams_utils_shared.data` | `1959a54b2280c95ddcc25ec77c070b4d609dcca7b7e59683b460c92c7297bcd6` |
| `bullfrog_shared.data` | `b67b56b7b2b75962b8b34559e20fd97b623b2d0f7f08a0035f82072ffbf4ec06` |

## The application interval is an allocator guard

The existing `timer_evidence.py` establishes the `100000` raw microsecond
interval, flag 1, and application timer at object `+0x5c`. Its consumer is now
identified:

- Application initialization `code:0x2dc`, `SetRate` call `0x338`, supplies the
  interval. Sams `UGamework` constructor `0x29fc` constructs that same timer
  at `0x2a58`; the application changes its rate afterwards.
- Exported `UGamework::Run` at Sams `0x2c24` calls internal guard `0x4434` at
  `0x2ca8`. The guard passes object `+0x5c` to `UTimer::HasTicked` at `0x445c`.
- When the timer has not ticked, the guard returns success immediately. When
  it has ticked, it compares allocator queries against application fields
  `+0x70`, `+0x74`, `+0x78`. The application initializes `+0x70` to `500000`
  at `0x31c`. The guard's query objects are `UAllocator` and
  `UAllocatorTemporary`; virtual offset 52 binds to `GetAvailable`, offset 56
  to `GetLargest`. Sams allocator vtable `data:0x33c4`, slots `0x33f8` and
  `0x33fc`, relocate to the corresponding exported transition vectors.
- `UGamework::Run` calls the game callback at virtual offset 28 (`0x2cfc` /
  `0x2d00`) after a successful guard result, including iterations when the
  guard timer has not ticked. The interval does not gate this callback.

`UTimer::HasTicked` itself is Sams `0xa698`: elapsed unsigned 64-bit time is
compared with the interval. With flag 1 it resets the previous timestamp to now;
with flag 0 it advances the previous timestamp by one interval. This explains
why the initialized guard need not catch up every missed interval. It proves
no gameplay, rendering, or animation rate.

## Two elapsed millisecond clocks

The low-level source is still the conditional microsecond timer chain described
by `timer_evidence.py`: Bullfrog `LbTime_GetClock`, code `0x38bb8`, divides the
absolute 64-bit reading by 1000 and returns its low quotient. Its application
import glue is `0x1c4d64`, TOC slot resolved from `data:0x8000`. The existence of
other hardware clock branches and calibration values does not prove an FPS.

| Consumer | Application code chain | Behavior |
| --- | --- | --- |
| Scheduler and RSE | `0x10e844` → `0x11a588` → `0x10ed54` → `0x117d74` → `0x127cd0` | Uses a scaled elapsed clock, with pause bookkeeping and an optional forced-step source. |
| Advisor LIP | `0x10e864` → embedded object `+68` → `0x11a428` → `0x117c00` → `0x10edb4` | Uses an unscaled elapsed clock, with independent pause bookkeeping and an offset. |

`0x10edb4` samples `LbTime_GetClock` at `0x10edc8`, subtracts the previous
unsigned 32-bit sample, accumulates the delta in a double at object `+4`, and
returns its integer conversion. `0x117c00` subtracts pause accumulation `+16`;
when flag `+20` is set, it uses stored `+12` instead of sampling now.
`0x11a428` then adds object `+24`.

The other source `0x127cd0` multiplies the millisecond delta by its double scale
at `+24` and accumulates at `+16`. Its pause methods use fields `+32/+36/+40`.
The UI lane identifies initialization 1, multiplication/division by 1.25 and
clamping to 0.25–2 at `0x127c08/0x127c48/0x127c88`. Those modifiers affect this
scheduler/RSE source, not the unscaled advisor chain. Their UI meaning is
separate evidence in [PPC-ui.md](PPC-ui.md).

Forced stepping at `0x10ec60` stores unsigned `1000/rate` in object `+52`.
The main loop can request rate 32 at `0x1c2298`; the resulting stored interval
is 31. `0x10ed54` returns the forced reading `+48` while flag `+44` is set;
`0x10ed10` advances it by `+52` only when the relevant pause query is false.
This branch prevents treating all clock readings as ordinary wall time.

## Scheduler, park turns and conditional guest cadence

`ThemeParkWorld` game callback `0x574` calls `0x1c1208` at `0x6d8`. The latter
contains the active simulation catch-up loop:

| Witness | Interpretation |
| --- | --- |
| `0x1c22b4` → `0x10e844` | Obtain scheduler milliseconds. |
| `0x1c22c8` | Compare backlog against 2000; excessive backlog is bounded by adjusting previous time. |
| `0x1c22e0`, `0x1c22ec` | Advance previous time by 31 and increment substep counter. |
| `0x1c2314` → `0xb2838` | Run one script-manager pass per active substep. |
| `0x1c231c` | Test counter low bit; the even phase calls `0x63dd0` and `0xb7ac0`. |
| `0x1c233c` | Test counter low three bits; park work occurs only on the phase divisible by eight. |
| `0x1c2354` | Compare park-work counter with 3; catch-up park work is capped within this callback. |
| `0x1c23b8` / `0x1c23ec` | Mode-specific direct call to `0x10536c` or call to wrapper `0x10565c`. |
| `0x105398–0x1053a0` | Increment the park turn at object `+0x1da70c`. |
| `0x1c24c8–0x1c24cc` | Repeat while sampled now is greater than previous scheduled time. |

Thus the nominal increments are **31 ms per substep and 248 ms per park turn
of the selected scaled clock**, under the active branches. The matching float
constants `data:0x5958/0x5954/0x5950` are 31/62/248 and appear in interpolation
normalization. These are static scheduling intervals, not a guaranteed measured
frame rate. Pause, scale, forced stepping, mode branches and catch-up limits
matter; the OpenTPW 60 Hz scheduler is not established by this binary.

The economy lane's calendar conversion reads that park turn at `0xe43dc` and
computes `turn * mFunnySecsPerRealSec / 4`, then converts seconds to 100 ns
units. Its constructor supplies `15000` for the multiplier. Consequently the
calendar's divisor 4 and this build's actual scheduled 248 ms turn do not justify
substituting OpenTPW's fixed 60 Hz tick. The exact calendar proof and civil-date
API dependency belong to [PPC-economy.md](PPC-economy.md).

The guests lane locates `0xeed24–0xeed30`, which reads the same park counter
and requires its low two bits to match the guest ID. The fixed need additions
also require counter low four bits zero at `0xeeeb0–0xeeebc`. Algebraically,
16 nominal turns are 3968 ms of scaled clock; this is **not a uniform guest need
rate**. The nested guards admit fixed additions only for ID low two bits zero
on this traced path. Fresh IDs are sequential, not uniformly aligned; other
eligible per-cell deltas and countdown work precede the fixed-addition guard.
See [PPC-guests.md](PPC-guests.md) for allocator and caller proof.

## Animation delta and frame advancement

Application updater `0xa6f70` obtains scheduler time from `0x10e844` at
`0xa6fa0` and advisor time from `0x10e864` at `0xa6fd8`. The animation globals
base is relocated through TOC slot `data:0x97c` to `data:0x1577c0`:

- `+16400`: scheduler milliseconds.
- `+16404`: `(new scheduler ms - old scheduler ms) / 1000`, a float in seconds,
  written at `0xa6fd4`.
- `+16408`: unscaled advisor milliseconds, written at `0xa6fdc`.

The channel updater `0xa7000` chooses `+16408` when channel flag `0x40` is set,
otherwise `+16400` (`0xa7070–0xa7084`). It subtracts channel start time `+16`,
then computes:

```
channel.frame = (now_ms - start_ms) * 30 / 1000 * channel.speed
```

The multiplications/division at `0xa70b8/0xa70bc/0xa70c0` use float constants
30 (`data:0x516c`) and 1000 (`data:0x5170`) and speed at channel `+12`. The
result is stored at channel `+32` (`0xa70c4`) and compared with channel `+28`.
This is direct static evidence for 30 frames per second **of the selected clock**,
including per-channel speed. No FPS inference from a name or calibration is
required.

Related duration conversion at `0xa6ed4–0xa6eec` subtracts animation header
start/end frame values and multiplies by `1000/30`. Query `0xa7e74` converts
header end-frame count using unsigned multiply/divide by 30 (`0xa7ed4–0xa7edc`).
The caller's added delay, speed conversion and output clamping remain relevant;
this does not prove every opcode returns the bare clip duration. Channel/binding
proof is in [PPC-rides.md](PPC-rides.md).

## RSE scheduling and timer instructions

The interpreter is `0xaf534–0xb2374`; TOC slot `data:0x2f58` relocates to the
opcode jump table `data:0x3f2a4`. The script manager is `0xb2838–0xb2aac`.

The loader reads successive four-byte header words: magic, version, variable
count, stack size, then time slice into script `+148` at `0xb2de0–0xb2df8`.
That is file offset 16. The manager reloads it into remaining budget `+152` at
`0xb28e0–0xb28e4`, invokes one opcode at `0xb28f0`, and decrements budget only
when the global critical flag is zero (`0xb28f4–0xb2908`). `CRIT_LOCK` case 1
sets this global flag; `CRIT_UNLOCK` case 2 clears it **and yields** by clearing
remaining budget (`0xaf5d4–0xaf5e8`). `ENDSLICE` case 6 at `0xb0e4c` likewise
clears the budget. The manager checks budget and script program position before
another instruction. This supports an instruction budget, with a critical
section exemption, rather than a 50 ms duration.

Script `+192` is a **separate signed speed bias**, initialized to 50 at
`0xb3154/0xb318c`. The interpreter forms `speed = 0.5 + bias/100` at
`0xaf568–0xaf590`; the default bias therefore gives speed 1. It is not a second
copy of the header time-slice field.

| Opcode | Dispatch code | Static behavior |
| --- | --- | --- |
| GETTIME 7 | `0xaf6c8` | Reads current shared scheduler clock (`0xaf6d8` → `0x10e844`). There is no VM-creation timestamp subtraction in this case. |
| WAIT 44 | `0xb0658` | First encounter stores deadline `now + trunc(operand/speed)` at script `+160`; later encounters retain it. It waits while unsigned now < deadline, rewinding two code words and clearing budget. Equality resumes. |
| WAITABS 45 | `0xb0710` | On its first encounter stores **now + operand**, without speed division (`0xb0764–0xb0788`); despite its name, this case does not store operand as an absolute deadline. |
| SETTIMER 95 | `0xb1fd0` | Stores scheduler now + resolved operand at script `+196`. |
| GETTIMER 96 | `0xb1ffc` | Subtracts scheduler now from `+196`, clamps negative result to zero, writes the accumulator and optional variable destination. |

The default WAIT unit is thereby milliseconds, now tied to a concrete consumer.
Waits are serviced by scheduler substeps, not OpenTPW's assumed one slice per
60 Hz tick. The manager phase filter (`0xb28b4–0xb28c8`) runs ordinary scripts
only when ID low three bits match manager-pass counter low three bits. A flag at
script `+184` bypasses that filter. The existing port's all-scripts-every-tick
model is not established here. Catch-up may run multiple manager passes before
rendering; do not replace it with a measured fixed FPS without further evidence.

## Assumption disposition and unresolved consumers

| Port assumption | Result for identified Mac build | Remaining boundary |
| --- | --- | --- |
| RIDES-001, 30 animation ticks/s | Direct frame formula proves 30 per second of selected clock, multiplied by channel speed. | Windows/Patch 2 qualification; channel flags and full playback paths. |
| RIDES-016, raw InitDuration | No mapping from `Info.DurationUnit` to script variable established. | Settings loader and host assignment. |
| RSE time slice = instruction budget | Confirmed loader and manager consumer; CRIT_UNLOCK also yields. | Header zero/negative budget behavior, flag+184 selection and critical reentrancy need broader tracing. |
| RSE milliseconds / GETTIME since creation | Milliseconds confirmed; shared current clock observed, not a per-VM origin. | Runtime reset/offset configuration. |
| RSE every 60 Hz tick | Mac uses 31 ms manager substeps with ordinary scripts spread across eight ID phases. | Exact reset phase, catch-up and Windows behavior. |
| ADVISOR-008/010/011/013 | LIP source chain is the unscaled pause-aware millisecond clock; advisor lane proves marks divided by 1000. | Trigger timing, audio callback latency, device failure path and Windows audio backend. |
| ECON-001/003/004 | Park turns derive from 31 ms substeps; scaled source distinct from unscaled clock. | Calendar configuration, civil-date implementation and intended speed-control UI. |
| ECON-006/010/013/015/021/022/023/033/039 | Clock units alone do not prove loan, staff, training, research, repair, wear, awards or profit-window formulas. | Their actual event/timeout subscribers remain separate consumers. |

No assumption is globally removed by this document. Root integration must retain
platform applicability and distinguish the pinned Mac facts from Windows claims.

## Reproduction and validation

```
python3 tools/ppc-analysis/lanes/clock/clock_evidence.py /path/to/mac-feral/bin
python3 -m unittest discover -s tools/ppc-analysis/lanes/clock -p 'test_*.py' -v
python3 -m py_compile tools/ppc-analysis/lanes/clock/*.py
```

The helper pins identities, calls, relocated dispatch and allocator bindings,
selected operands, arithmetic operations and constants. It emits only interpreted
metadata, never original bytes or raw disassembly. The original toolkit's
traceback finder was not used to claim function bounds: stripped functions were
followed by bounded instruction inspection, import glue, transition vectors and
explicit control edges. LLVM's locally installed disassembler was used only for
transient inspection, outside repository artifacts. Eight synthetic tests cover
signed operands, negative/conditional/absolute branches, call-vs-jump validation,
and aligned bounds rejection. The pinned helper passes on the identified assets.

## Follow-up: startup modes, input actions and boundary behavior

The phase-two witness is `tools/ppc-analysis/lanes/clock/clock_edges.py`.
It adds exact guards and finite conversion boundaries; its bounded mathematical
examples are not an original executable run.

### Startup and turn gates

The lazy mode constructor at `0x12bb64` initializes the integer stored at
`data:0x53d98` from gameplay flags `data:0x52e40`:

| Flags | Stored mode | Exact observable linkage |
| --- | ---: | --- |
| `0x02000000` set | 2 | The active loop calls turn update `0x10536c` directly. |
| Otherwise `0x01000000` set | 1 | `CLBScreenComponentOnlineIslands::vf18` explicitly selects this at `0x971f4–0x971fc`; the loop uses wrapper `0x10565c`. |
| Otherwise | 0 | The active loop calls turn update `0x10536c` directly, as for mode 2. |

The setter `0x12bbf4` also rewrites these flag bits. Its diagnostic string is
`Invalid GameType in SetGameType`, so these are a game-type selector, not the
clock scale or the difficulty selector. Default startup `0x4b6a4` writes
`0x000c0e15`, which has no recognized mode bits and therefore selects mode 0.
A loaded configuration can override the flags at `0x4b6e8`.
A successful transition predicate in the main state machine selects mode 2 at
`0x1c2a94–0x1c2a9c`, and its failed branch selects mode 0 at
`0x1c2ac0–0x1c2ac8`. Similar selection is visible at `0x1379b4/0x1379bc`
and `0x1379e0/0x1379e8`. **Numeric mode selection is proven; a complete mapping
of every single-player/load/network entry route to these transitions is not.**
The OnlineIslands action alone must not be used to label mode 2 exclusively
single-player or to invent a network-only cadence.

World work has an override followed by an exclusion check: `data:0x52e44 & 8` at
`0x1c22f4–0x1c22fc`, and `data:0x52e40 & 1` at `0x1c2300–0x1c2308`.
The branch at `0x1c22fc` goes directly to work at `0x1c230c` when flag 8 is
set. Only when flag 8 is clear does the gameplay flag-1 check apply; if that bit
is set, the branch at `0x1c2308` skips to `0x1c24c0`. Work therefore runs for
`application_flag8 || !gameplay_flag1`. The excluded case still advances
scheduled time and its substep counter, but skips the script/world/turn body. These exact bits are identified;
the first is also altered by `ThemeParkWorld::vf26` at `0xbf8–0xc24`, which
can invoke the high-level pause path at `0xc50`. They are not automatically
synonyms for the separate pause state described below.

The park-work cap is reset at the normal callback tail `0x1c27a4–0x1c27a8`.
Once its three eligible park phases have been consumed, later eligible phases
skip the turn update while the scheduler and script manager continue. Their
scheduled phases are consumed, not queued for a later park turn. This means the
calendar/needs/history turn count can lag the script clock during catch-up.

Calendar conversion and advisor history use the concrete `mGameTick` writer
`0x105398–0x1053a0`. High-level pause freezes both source clocks, so normal
scaled-time scheduling and this turn-derived calendar stop. The combined
exclusion condition `!application_flag8 && gameplay_flag1` and catch-up
park-work drops independently prevent turn advances; mode 0 does not itself
suppress world ticks. Advisor history consumer `0x1210f8` uses
`(current_turn >> 2) - (saved_turn >> 2)`, not LIP milliseconds. Four nominal
248 ms turns are 992 ms of scaled clock, but this is not an exact wall-second
or a uniform message cooldown once those gates and phase alignment matter.

### Pause and speed input records

The startup input initializer `0x114d50` creates a `system` group with 12
records and a `game` group with 15 records. The strings are at
`code:0x1d4e48` and `code:0x1d4e4f`. A group stores its record pointer at `+0`,
count at signed short `+4`, enabled state at `+6`, and name at `+8`.
The record layout is established by the consumers, not inferred from a symbol:

| Record field | Consumer |
| --- | --- |
| signed short `+0` | action ID (the state lookup at `0x114c78` checks it) |
| signed short `+2` | key token, compared at `0x114b14` / `0x114bf0` |
| signed short `+4` | modifiers, compared at `0x114b20` / `0x114bfc` |
| short `+6` | held state, set by `0x114b80`, cleared by `0x114c5c` |
| pointer `+12` | release-side action, invoked at `0x114c48` |
| pointer `+16` | press-side action, invoked at `0x114b6c` |

Records are 20 bytes. `0x114ac0` handles the press-side path;
`0x114b9c` handles the release-side path. `ThemeParkWorld::vf23` calls them at
`0x824`, `0x850`, and `0x8b4`; game-group routing also occurs in the larger
input dispatcher around `0x13b88c–0x13b950`.

| Group / record | Key token / modifiers | Release callback |
| --- | --- | --- |
| System record 0, `data:0x4515c` | `0x0050` (ASCII `P`), 0 | Transition vector `0x7550` → `0x11288c` → `0x110604` pause toggle. |
| Game record 6, `data:0x452c4` | `0x6d00`, 0 | Vector `0x7508` → `0x11315c` → `0x127c88`, divide scale by 1.25. |
| Game record 7, `data:0x452d8` | `0x6b00`, 0 | Vector `0x7510` → `0x113184` → `0x127c48`, multiply scale by 1.25. |

The speed callbacks clamp finite scale to 0.25–2. The physical names of the two
special key tokens and any runtime binding replacement remain unqualified.
The first word of the game table, 27, is record 0's Escape key token, not the
record count. This distinction prevents the earlier ambiguous table view from
being treated as a hotkey schema without its consumer.

Pause entry `0x110518`, resume `0x110598`, and toggle `0x110604` require host
state `+60 == 1`. Pause stores host `+28 = 1`; resume clears it.
The pause entry calls `0x10e888`, which freezes the scaled clock through
`0x117c54` and the unscaled clock through `0x117ae8`.
Resume calls `0x10e8bc`, which resumes them through `0x117c9c/0x117b30`.
Toggle calls `0x10e8f0`, which toggles them through `0x117cf4/0x117b88`.
Thus advisor LIP, ordinary animation/script time and scaled scheduling share
this pause action while retaining separate scaling. This narrows UI-022's
missing pause evidence but does not establish the port's complete UI behavior.

### Rollover, saturation and interval drops

The underlying `LbTime_GetClock` result is a low 32-bit unsigned value. Both
application accumulators subtract the prior reading with ordinary 32-bit
subtraction, then convert the resulting **unsigned** word to double. One source
rollover therefore produces a modulo-32-bit delta, not a negative elapsed time,
provided no more than one full source period passes between samples. An absence
of samples for a full 32-bit period cannot be reconstructed by this consumer.

Both accumulator return paths call compiler helper `0x1c3fbc`. Its constants
are 0, `2^32`, and `2^31` at `data:0x52e24/0x52e2c/0x52e34`.
For finite input it returns 0 below zero, `0xffffffff` at or above `2^32`, and
otherwise truncates toward zero, using a `2^31` subtraction/reconstruction for
the upper unsigned half. The double accumulator itself continues increasing;
**the returned clock saturates instead of wrapping**. Pause offsets, forced
clock additions and the park/substep counters use 32-bit word arithmetic and
can wrap independently. A direct scale setter `0x127c40` stores its argument
without the UI callbacks' clamps; its complete caller/load validation remains
untraced.

The scheduler's backlog check is signed `now - previous > 2000` at
`0x1c22c4–0x1c22cc`. It first drops excess backlog by setting
`previous = now - 2000`. Its loop condition is a direct **signed**
`now > previous` at `0x1c24c8`, and each iteration advances previous by 31.
For an ordinary same-signed-range example, a 1 ms positive gap runs one step
and overshoots by 30 ms. A 2000 ms gap runs 65 steps and finishes 15 ms ahead;
with initial substep phase 0 and active mode 2, it services 65 script passes,
advances three park turns, and consumes five later eligible park phases without
turn updates. A 5000 ms gap first drops 3000 ms, then performs that bounded work.
These are mathematical consequences of the pinned slice, not measured gameplay.

Signed comparison and word wrapping are not interchangeable around `2^31`.
The bounded synthetic prefix `previous=0x7ffffffe, now=0x7fffffff` still has its
loop condition true after previous crosses the signed boundary. No corrective
saturation/wrap guard was found in this loop. This is a concrete unresolved
long-session edge, not a claim that the original runtime exhibits a particular
hang; initialization, resets, alternate sources and higher-level runtime paths
still need qualification.

### WAIT boundaries and ID phases

The manager increments its independent pass counter before matching ID low
three bits (`0xb2884–0xb28c8`). With initial counter 0, ordinary ID 1 runs on
passes 1/9, and ID 0 on passes 8/16. Ordinary WAIT/WAITABS servicing is therefore
nominally eight active 31 ms manager passes apart; script flag `+184` bypasses
that filter. A world exclusion skips the manager entirely while the outer
substep phase advances. The manager phase and park phase must not be conflated.

WAIT's first encounter always writes/reuses the operand-derived deadline,
rewinds two code words, and yields (`0xb06f8–0xb0708`), including a zero or
negative duration. The later encounter compares unsigned now against deadline;
equality resumes. WAITABS has the same first-encounter yield at
`0xb0788–0xb0798`, but performs integer `now + operand` without speed division.
Both use a zero deadline as the uninitialized sentinel. A deadline that wraps
to zero can therefore be reinitialized rather than treated as already set.

WAIT converts the resolved signed operand through single-precision arithmetic,
divides by `0.5 + signed_bias/100`, then uses `fctiwz` at `0xb06e8` before adding
the result to the 32-bit clock. `fctiwz` truncates toward zero and saturates
finite out-of-range values to signed 32-bit limits under the architecture's
conversion semantics. [IBM Assembler Language Reference, instruction section](https://public.dhe.ibm.com/systems/power/docs/aix/53/alangref.pdf)
The speed-bias field is signed 16-bit; bias -50 produces zero speed. No zero,
negative speed, negative duration, NaN or exception check appears in this WAIT
path. Floating-point exception mode and NaN results remain an explicit boundary;
no synthetic model invents their runtime handling. Single-precision rounding
and 32-bit deadline addition also limit faithful use of a wide integer clock.
For example, a deadline wrapping below current unsigned now can resume early,
while the zero sentinel has different behavior. The port's checked/nonnegative
clock and delay rules need platform-qualified comparison before replacement.

Additional validation: both pinned witness commands pass; **21 synthetic tests**
cover operand/branch validation, unsigned saturation and source rollover,
fractional scale accumulation, catch-up ceiling/drop/cap behavior, excluded-world
phase advancement and a bounded signed-boundary prefix. Tests explicitly reject
NaN/infinity from the finite arithmetic model. Run the second witness with:

```
python3 tools/ppc-analysis/lanes/clock/clock_edges.py /path/to/mac-feral/bin
```

## PEF relocation-reader qualification

The phase-three reader fix is specification-backed. Apple's PEF glossary defines
relocation blocks as two-byte portions; repeat instructions count those blocks
and cannot nest. [Apple Mac OS Runtime Architectures, printed pp. 8-32, 8-34 and GL-6](https://developer.apple.com/library/archive/documentation/mac/pdf/MacOS_RT_Architectures.pdf)
The prior implementation counted decoded instructions, which incorrectly widened
a repeat when a preceding instruction occupied two blocks. The corrected reader
tracks instruction starts separately from block offsets. It rejects split-instruction
repeat spans conservatively; that restriction is a reader limitation, not a
separately established prohibition in the specification.

Nine added synthetic reader tests reproduce the pre-fix multiword failure and
cover mixed widths, state carry, the maximum block window, nesting, malformed
spans, count conventions and execution limits. The toolkit's 25 tests and this
lane's 21 tests pass. A pre-fix snapshot and the corrected reader produce identical
normalized metadata for all **38,072 relocations in 16 identified containers**.
The complete map comparison includes section, offset, target kind/index and
stored addend, hashed as deterministic JSON. The saved baseline contains only
identities, counts and metadata digests. It preserves no original binary contents.

```
python3 tools/ppc-analysis/lanes/clock/relocation_corpus.py /path/to/mac-feral/bin
python3 -m unittest discover -s tools/ppc-analysis -p 'test_*.py'
```

This change qualifies the evidence reader; it makes no gameplay or Windows
fidelity change. Original runtime execution and deliberately overlapping
relocation instruction streams remain outside verification.

## CFM glue and widget clock follow-up

The shared `glue_import` helper now validates the entire supported six-instruction
CFM form before assigning an imported name: caller TOC preservation at stack+20,
callee code/TOC loads, code transfer to CTR and an unconditional unlinked branch.
Code and TOC-slot bounds/alignment and import index are checked. This helper
supports that form and unaddended pointers; other CFM variants require separate
recognition. Six added synthetic tests exercise malformed lookalikes, every
truncated prefix, bounds, alignment and invalid metadata. The toolkit's 31 tests
and the clock lane's 21 tests pass. All 2,209 full standard imported glues in the
16 identified containers pass; the prior timer, clock, clock-edge and complete
relocation-corpus witness outputs remain byte-for-byte identical.

A further real consumer is the button repeat clock. `0x171ef4` reads the selected
clock object through TOC `data:0x40e4` / pointer `data:0x4fc24` and invokes its
first virtual slot. The fallback callback is `timeGetTimeClockTickFunction`,
`0x171e9c`, which divides `GetAbsolute` by 1000 and returns the low quotient.
Its bounded divider body at `0x1c4010–0x1c40fc` has the same SHA-256 as the
previously inspected Bullfrog unsigned division helper. The application's UI
initialization installs object `data:0x120e74` at `0x13cb18`; constructor stores
at `0x13e218–0x13e220` bind it to the `LbTimeClockTickFunction` vtable
`data:0x47ddc`, whose callback `0x13ca44` uses `LbTime_GetClock`.
Both identified widget sources therefore use raw milliseconds, independently
of the scaled park elapsed clock. This supplies units for the UI lane's 500/125
repeat thresholds; alternate injected clock objects, complete input event
behavior and Windows applicability remain unqualified.

```
python3 tools/ppc-analysis/lanes/clock/widget_clock.py /path/to/mac-feral/bin
```

## Branch-interpretation correction from independent review

The decoded condition fields were correct in earlier witnesses, but this lane's
prose and standalone contract `a5263bb` interpreted two destinations incorrectly.
Those statements are superseded by the following actual routes:

| Native conditional | Fields and comparison | Taken destination / consequence |
| --- | --- | --- |
| `0x1c22fc` | BO 4, BI 2 after the record-form flag-8 mask | EQ clear means flag 8 is set; target `0x1c230c` runs work and bypasses the flag-1 check. |
| `0x1c2308` | BO 4, BI 2 after the record-form flag-1 mask | EQ clear means flag 1 is set; target `0x1c24c0` skips work. This check is reached only when flag 8 is clear. |
| `0x1c2388` | BO 12, BI 2 after comparison with mode 0 | EQ set targets `0x1c23b4`, the direct world-tick block. |
| `0x1c23b0` | BO 4, BI 2 after comparison with mode 2 | Unequal targets the mode-1 check; equal falls through to the direct world-tick block. |
| `0x1c23e4` | BO 4, BI 2 after comparison with mode 1 | Unequal skips world-tick work; equal falls through to the wrapper call. |

The correct gate is **flag8 OR NOT flag1**, and modes **0 and 2 direct / 1
wrapper**. Excluded substeps still advance scheduled time and phase. The repaired
contract removes the invented mode-0 suppression counter. Its new tests derive
truth tables from `NativeBranchRules.json` BO/BI/destination metadata, independently
of the model formula. The metadata is reproduced by the identity-pinned
`native_scheduler_branches.py` helper; no instruction words are committed.

The same helper independently checks `0x127d14` as binary64 fused multiply-add
with delta and scale as multiplicands and the prior double accumulator as the
addend. The contract's `Math.FusedMultiplyAdd` and cancellation regression match
this operation. Native nondefault FP state remains unqualified.

## Independent VM source review: `b3d14f9`

Reviewed exact source `b3d14f99480ee3338237b0830ae8987f8aed7e61` against
`15313d26a892d72a153ad15298ae2a199c7f21fa`, the pinned Mac interpreter, and the
independent review `d0e5c75` (`PPC-review.md`, rounds 2/3). **Verdict: conditional
acceptance of the three bounded primitive changes. No blocking objection for
the supplied baseline/Patch 2 corpus.** This is not full VM/runtime parity.

| Changed path | Native proof and source assessment |
| --- | --- |
| Variable-destination COPY updates flags | Case `0xaf5f0`: source is fetched only after variable-kind acceptance; store `0xaf638` copies the written variable into script+72. The source handler reads the source before writing the destination, then derives Sign/Zero from the result. Aliasing source/destination remains correct. Accepted for the validated operand domain. |
| Literal-destination COPY maps to PC −10000 / Faulted | `0xaf604` returns before fetching the source; the next dispatch at `0xaf5a4–0xaf5b0` rejects its non-opcode tag and stores −10000 at `0xb234c–0xb2350`. Parsed operand tags are 0x00/0x10/0x20/0x40, so the VM can abort directly. Its `Faulted` enum/message and immediate diagnostic timing are explicit host policy, not recovered native states or slice timing. The prior accumulator is preserved. Accepted as that mapping, not a claim about child/lifecycle cleanup. |
| Zero DIV/MOD results | Zero arms `0xb0ca8` / `0xb0d30` store 0 to the branch accumulator, then optionally the variable destination. Source writes 0 and sets Zero even for a literal destination. Accepted. Native fetches/resolves its operands before the zero check; source resolves divisor first and skips dividend access in the zero arm. Current validated operand getters are pure, so this changes no valid observed result; malformed operands and concurrent host mutation are not qualified by this equivalence. |
| Signed arithmetic overflow | Ordinary signed division/remainder match truncation toward zero and the dividend's remainder sign. Source's `Int32.MinValue / -1 → Int32.MinValue` and remainder 0 remain named implementation policy. Neither the static native `divw` path nor the new synthetic test establishes that overflow result as original behavior. |
| RAND raw signed bound | `0xb07f8` sign-extends the raw word; the path never resolves a tagged variable. Source's `(short)maxValue.Raw` matches that fetch rule and sets flags from its result. For nonnegative bounds, inclusive range is accepted; `System.Random`, seed interpretation and per-VM stream ownership remain host policy. |

**Low, nonblocking corpus-bound caveat:** source still faults on negative raw
RAND bounds. The later independent review refines this domain: for `b <= -2`,
`0xb0804–0xb0820` gives `abs(x − trunc(x/(b+1))*(b+1))` in 0..−b−2; for b = −1,
the divisor's zero makes its multiplied quotient contribution zero, so the
result is x regardless of the undefined quotient. Thus this negative-bound fault
is a host-domain restriction, not the defined Mac result. The source documentation
already calls it VM safety policy; retain that boundary or implement a separately
qualified replacement. No supplied corpus RAND uses this domain.

### Focused before/after validation

The review materialized both exact Git revisions in temporary directories and
restored only already-cached packages from the local package directory. Neither
peer nor root source was edited. On macOS, canonical `/private/tmp` paths were
required so NuGet's restore graph retained project references; the initial
`/tmp` alias build failure was an artifact-path issue, not a source defect.
Only `RideVMTests` and `RideVMCorpusTests` ran; no codec suite or original
executable ran.

| Source revision / inputs | Focused result |
| --- | --- |
| Before `15313d2`, baseline | 21 passed, 0 skipped |
| Target `b3d14f9`, baseline | 34 passed, 0 skipped |
| Before `15313d2`, verified Patch 2 corpus | 2 passed, 0 skipped |
| Target `b3d14f9`, verified Patch 2 corpus | 3 passed, 0 skipped |

The new inventory gate confirms **1,243 variable COPY destinations** and **56
nonnegative literal RAND bounds** in each corpus. The fixed Totem trace passes
before and after. The full default/chaos VM report is byte-identical between the
source revisions for each version:

| Asset set | Default / chaos executed instructions | Report SHA-256 before and after |
| --- | --- | --- |
| Baseline | 23,776,799 / 17,200,190 | `c9c303d56de1cce967176aa5661c6a9d6e4df080b7fe5ffb42cc11308c82b8ca` |
| Verified Patch 2 | 23,776,682 / 17,196,764 | `f4916c715e72ce66c3c02039ccee9b60ee9a3f6d3558cabe2c10679381864af8` |

Both reports finish with default Running=216/Waiting=47 and chaos
Running=199/Waiting=64. They compare executed counts, hook counts, final root
states and reached/spawned coverage; they are not complete variable-state hashes
or original-game traces. COPY's accumulator semantics intentionally change, but
no corpus control/effect-count change was observed under these tested host
configurations. Malformed COPY, negative/raw-variable RAND and divide overflow
are covered by synthetic domain checks, not by original corpus reachability.

The corpus runs retain the port's assumed 60 Hz per-VM clocks. This primitive
commit does not implement the original shared clock, 31 ms scheduler, eight-ID
phasing, WAIT speed/first-yield rules, CRIT_UNLOCK yield, or native resource/lifecycle
behavior. Successful runs do not resolve those existing differences, or qualify
Mac primitive behavior for the Windows baseline or Patch 2 executable.

## Independent format source review: `2439695`

Exact reviewed source: `24396953778852398c5721d88256a601802697b4`, materialized
read-only in a temporary snapshot. **Verdict: objection pending one HIGH
allocation/bounds repair.** Native sampling interpretation is otherwise accepted
within its declared corpus/domain limits. The subsequently committed runtime
wiring `11c1003` is a separate candidate, not approved by this parser review.
Neither peer nor root source was edited.

**HIGH — `ModelVertexAnimation.cs:209` (`Decode`).** Packed-key storage is
allocated with `keyCount * vertexCount` before the packed pointer/byte span is
validated at line 212. Both counts are u16 and their product is computed as an
unchecked signed int. The decoder can overflow its array length or request a
large allocation before determining that the declared payload is impossible.
Validate a checked wide product, supported materialization/resource limits and
the complete payload span **before** allocating. Account for aggregate decoded
storage when many groups/records alias the same payload. This is a defensive
reader requirement, not a claim that the original loader enforced that budget.
No oversized allocation, hostile asset or exploitation reproduction was run.
The same allocation order remains present in `11c1003`.

| Reviewed behavior | Native/source assessment |
| --- | --- |
| Signed 10:10:10 unpacking and `q*scale+offset` | Accepted. Native `0xa446c` is single-precision fused multiply-add; target `ModelVertexAnimation.Dequantise` uses `MathF.FusedMultiplyAdd` on each axis. A separate safe synthetic cancellation check against the target assembly gives −2⁻²⁷ for q=3/scale=0.1f/offset=−0.3f, while separately rounded multiplication/addition gives 0. |
| Vertex interpolation order | Accepted. `0xa4484` rounds `next*fraction` first; `0xa44ac` then fuses `current*(1-fraction)+product`. The source explicitly uses that same order rather than generic vector Lerp. Nondefault native FP state is unqualified. |
| Group 0 interpretation | Accepted as padded lower/upper vectors, not instance translation. `0xa4ba0` passes node-state+120 to `0xa468c`, with second vector at +132; `0xa47fc/0xa4800` subtract scale then 0.25, and `0xa49b8/0xa49bc` add scale then 0.25. `SampleBounds` preserves these separately rounded operations. Their rendering/culling consumer is not qualified here. |
| Vertex cursor | Accepted for nondecreasing time since reset. `0xa4b78` advances only when the next tick is strictly less than time. The source remains on the earlier segment at an exact key, extrapolates before the first key, and rejects past-last/NaN sampling. Its search starts at zero each call; native cursor persistence/reset and decreasing/looping time remain caller policies. |
| TRS before-first and final rotation | Returning null before the first key matches the native no-write path. `0xa820c` overrides the final rotation partner to itself, so the final key holds. This does not prove that a runtime caller may always substitute rest pose for a prior component, or qualify degenerate single-key/nonfinite inputs. |
| Unsupported blocks | The 12-byte 0x4000 variant, 0x10000 block and other unknown payloads remain reported through `UnsupportedFlags`; the reader does not invent a vertex decoder for them. Accepted. |
| Texture-frame gate | Native `0xa5768` requires trailer bit 0x2 set and `0xa5778` skips when global option 0x8 is set. Thus option **0x8 clear** is the correct polarity. The source property checks only the trailer gate and explicitly documents the additional runtime condition; callers must bind it before claiming actual texture-frame behavior. |
| Rotation option | Native `0xa824c` selects table slerp when option **0x2 clear** and a linear blend otherwise. The current sampling API uses its analytic slerp path, without a runtime option input. Its existing trig/threshold approximation and global selector value remain unqualified; native table-sine equivalence is not claimed. |
| Mutable state and instance ownership | The committed parser/sampler writes only the supplied destination span, not its packed keys or index/tick tables, and keeps no internal runtime cursor. The destination must be owned by the caller. This proves no renderer/per-object isolation for the separate wiring candidate; loop/rebind, static-once, add-mode, normals and unsupported fallback need that review. |

The animation clock `0xa7000` advances `(selected_ms-start_ms)*30/1000*speed`;
these parser APIs consume **ticks**, so they neither choose a scene clock nor
establish pause/scale/loop behavior. Root integration must preserve this boundary
and the Mac/Windows qualification boundary.

### Validation

The identity-pinned target `format_witness.py` passes all 284 native instruction
facts, including corrected branch polarities. Focused tests on the exact source
snapshot with the actual baseline assets passed **91**, with three optional
fixture cases initially skipped. Supplying the known language and official
bonus roots separately passes the two relevant optional cases: **93 relevant
MD2/Advisor/ObjectCatalog checks passed overall**. The remaining skipped
ISO-MTR method was matched by the loose `Md2` name filter and is outside this
parser review; no extracted MTR directory was supplied. No codec suite or
original executable ran.

The suite covers the actual quantized/toggle/frame corpus, malformed pointers
and key ordering, exact-key/before-first/final-rotation cases, unsupported layouts
and existing catalog/advisor behavior. It does **not** exercise the large-count
allocation order above, prove complete renderer state isolation, or provide
original-game visual/clock traces. Passing valid corpus inputs cannot clear the
allocation objection.

## Consumer dependencies after the scheduler review

This follow-up uses the identified application above and C runtime
`c_c++_shared.data`, SHA-256
`5e04f9c00c922dc78a787d1b93067c75d37a3e65b0a0202e50c2f449f131b27f`,
code section 0 and TOC data section 1 `0x8000`. The live-source map was read
from root commit `9e40f52b7a43825c943393b27d754ab00df5c236`; it describes
connections required for future integration, not production changes.

### Three distinct mode/state domains

The callback dispatch at `0x1c1358..0x1c1374` uses state word
`data:0x15c488`, reached through TOC `data:0x988`, and a 16-entry code-pointer
table at `data:0x52cc8` through TOC `data:0x4770`. Callback state **4** dispatches
`0x1c1378`, sets state **5**, then leaves the body. Callback state **10**
dispatches `0x1c2264`, the active scheduler. These are distinct from the
GameType selector **0/1/2** and from world field `+0x1da738`.

The latter world field is tested against **4** at `0x1053a4..0x1053ac`,
after `mGameTick` has already incremented at `0x105398..0x1053a0`. Its equality
branch targets `0x105470`, bypassing normal thing iteration (`0x1053b0..0x10546c`).
The common tail still calls player update `0xd67f0` at `0x10563c`; that updater
calls calendar `0xe3f0c` at `0xd6818` using player member `+672`. Therefore world
state 4 alone does **not** stop the virtual calendar or advisor turn history.
The scenarios lane's bankruptcy-to-state-4 path must be combined with any
subsequent callback/pause transition before claiming that bankruptcy freezes
time. It does prevent the bypassed regular guest/thing update on this path.
The same world-state-4 comparison also zeros two periodic aggregate operands
at `0x1c2460..0x1c2468` and `0x1c24ac..0x1c24b4`; their full downstream meaning
remains separate from clock units.

### Manager initialization, sample time and signed timer boundaries

Manager initialization `0xb2760` sets manager `+0 = 1`, `+8 = 1`, list head
`+16 = 0`, and pass counter `+4 = 0` at `0xb27a0..0xb27c0`. The manager at
`0xb2838` checks initialized state `+0` at `0xb2870..0xb2878`; the zero path
bypasses incrementing `+4`. Its initialized path increments the counter before
reading list `+16`, so an initialized **empty** list still advances the pass
phase. Shutdown `0xb2b18` clears initialized state at `0xb2b68`. The standalone
`SchedulerRules.ScriptPassCounter` assumes the manager is initialized during
allowed work; its `ScriptManagerPasses` counts calls and cannot by itself prove
a manager counter advance while initialization is false. Script IDs require
manager-owned allocation, independently of attraction and guest IDs.

Animation cache update `0xa6f70` is called at `0x1c22ac`, before the scheduler
samples its loop's current word at `0x1c22b4`. It fills global animation
`+16400` with the selected scaled clock and `+16408` with the unscaled clock.
The per-channel frame formula reads those cached words. In contrast, RSE
GETTIME/WAIT/SETTIMER/GETTIMER call the clock getter again at their instructions.
A catch-up pass is **not** an instruction to advance all clocks by 31 ms.
Injected timestamp reads must preserve this distinction between cached callback
samples, direct getter samples, and scheduled previous time.

SETTIMER `0xb1fd0` stores wrapped word `now + resolved_operand` in script `+196`.
GETTIMER `0xb1ffc` subtracts the current word at `0xb2014`, stores raw difference
in accumulator `+72`, and uses a **signed** compare/clamp at `0xb2020..0xb202c`.
Thus deadline `0x80000010`, now 0 gives remaining 0; deadline `0x10`, now
`0xfffffff0` gives 32. This differs from WAIT's unsigned now/deadline comparison
and from the live VM's wide double deadline with a ceiling/nonnegative clamp.
The new witness separately verifies BO/BI/target of the negative clamp branch.

Animation elapsed `0xa70a4` is a wrapped subtraction interpreted **unsigned**:
the conversion uses high word `0x4330` without a signed XOR and subtracts
`2^52` from the constructed double (`data:0x51a8`). It then rounds to binary32
before separately rounded multiplication by 30, division by 1000, and
multiplication by channel speed (`0xa70b4..0xa70c4`). Backward timestamps are
not clamped here. For now 100/start 200/speed 1, the bounded formula gives
128849016 frames after the unsigned word rounds to `2^32`; this is an
arithmetic edge witness, not a claim that normal playback reaches that state.

Scheduler reset `0x1c3520` samples the current selected clock into previous
scheduled time and two other cadence stamps (`0x1c3538..0x1c3568`), then clears
substep phase at `0x1c3570`. Its only direct linked call in this identified
code is `0x1c2274`, behind a nonzero reset request at TOC `data:0x1864` (pointer to
`data:0x15c468`). Lifecycle routine `0x11b4f4` sets that request to 1 at
`0x11b540..0x11b548`; the scheduler clears it at `0x1c227c..0x1c2280`. The
complete caller mapping of that lifecycle routine and indirect entry paths
remains unresolved. No automatic near-`2^31` reset is established; the signed crossing
remains unsupported in the bounded scheduler contract.

### RSE civil time is an independent clock domain

The RSE dispatch table binds YEAR 97 to `0xb2144`, MONTH 98 to `0xb2194`, DAY
99 to `0xb21e8`, and HOUR 100 to `0xb2238`. Every handler invokes app glue
`0x1c6474` importing C runtime **time**, then `0x1c648c` importing **localtime**.
Their return fields are:

| Opcode | Native result | Load/adjust address |
| --- | --- | --- |
| YEAR 97 | `tm_year` directly, with no addition of 1900 | `0xb215c` reads `+20` |
| MONTH 98 | `tm_mon + 1` | `0xb21ac` reads `+16`; `0xb21b4` adds 1 |
| DAY 99 | `tm_mday` | `0xb2200` reads `+12` |
| HOUR 100 | `tm_hour` | `0xb2250` reads `+8` |

C runtime transition vector `data:0x1dcc` maps time to `0x23200`; vector
`data:0x1dac` maps localtime to `0x23974`. Time calls wrapper `0x248a8`, which
invokes full validated import glue `0x2bf30` for InterfaceLib **GetDateTime** at
`0x248b8`. The wrapper adds 126144000 to the returned word at `0x248c4..0x248c8`.
Apple defines this OS input as current seconds since January 1, 1904.
[Apple, Getting the Current Date and Time](https://developer.apple.com/library/archive/documentation/mac/OSUtilities/OSUtilities-105.html)
The offset is the 1461 days from January 1, 1900 to January 1, 1904. Localtime
passes that word to converter `0x22d00`; the year-count loop starts at zero
(`0x22d3c`), increments at `0x22de8`, and stores the count directly at `+20`
(`0x22df0`). This native chain supports the year-minus-1900 interpretation,
without relying only on the export name or a modern runtime's ABI.

These opcodes use host civil time, independently of scale, scheduler turn
count, virtual epoch and pause offset. They sample host time only when an
eligible script executes. The native OS timezone, invalid/rollover dates,
clock edits, imported runtime loading, and Windows/Patch 2 implementation still
need qualification. They must not be connected to `ParkCalendar.ToDate` merely
because `Clock.RSE` uses HOUR. Flags/destination-fetch ordering are not approved
by this clock-domain witness.

### Live integration map

| Live consumer at the pinned root version | Required original input/state | Connection dependency |
| --- | --- | --- |
| `Level.Update` → `FixedStepClock.Advance` | Callback state10; selected `u32` source; previous scheduled word; outer phase; reset request; binary flag gates; cap reset | One scheduler owner supplies allowed work and world turns. Current 60 Hz/16-step frame loop does not implement the 31 ms ceiling/2000 ms/3-turn rules. |
| `RideVM.Advance`, `Handlers/Scheduling.GetTime` | Shared selected clock word; manager initialized state/phase; stable script ID; `+184` phase override; header `+148` budget | Supply shared clock samples and eligible slices separately. A VM's creation time, `TimeMilliseconds`, or recursive per-child elapsed update cannot substitute for the manager clock/ID list. |
| `WAIT`, `WAITABS`, `SETTIMER`, `GETTIMER`, animation waits | Separate raw word deadline fields `+160/+164/+196`, zero sentinel, first-encounter yield; signed bias `+192` | Preserve each opcode's conversion and comparison. Do not normalize all deadlines to one wide monotonic/nonnegative duration API. |
| `OriginalObjectRuntime.Simulate` → `ObjectAnimator.Advance`; `PrototypeRide` | Callback animation caches; channel `+16` start word, `+12` speed, flag `0x40` source selection, `+32` binary32 frame | Feed selected timestamps, then native frame arithmetic. Local double elapsed totals and multiplying the same per-object seconds by 30 omit alternate clock, reset, wrap and callback-cache behavior. Playback/loop/end options remain the formats/rides contract. |
| `ParkEconomyRuntime.FixedTick` → `ParkEconomy.AdvanceFixedTick` / `ParkCalendar.ToDate` | Actual world counter `+0x1da70c`, serialized epoch/rate, calendar previous fields and day/month/year messages | Advance the original world counter once per admitted turn, then compute virtual date. Do not reapply `GameSpeed` after the scheduler scales time. World state4 suppresses regular things but still reaches calendar update. Existing 60 Hz/30-day persisted port `Tick` is a different field and cannot be silently reinterpreted. |
| `Handlers/Effects.Year/Month/Day/Hour` → effect hook | Host civil `time/localtime` field snapshot, sampled at instruction execution | A separate host-date provider is required. `IParkClock`'s virtual date is the wrong domain for this Mac path; these hooks currently lack a demonstrated original binding. |
| `Advisor.Render` / `SpeechAudioPlayer.Position` / `LipSyncTimeline` | Unscaled pause-aware elapsed word, speech-start stamp, one-mark-per-update strict deadline, independent mouth-choice elapsed state | LIP driver's elapsed source is independent of PCM queue position and selected simulation speed. Do not wire it to scheduler phase or virtual turn; native audio pause/completion callbacks remain separate. |
| Native advisor queue/repeat history (standalone helper, no live root binding) | World counter sampled/stored raw; elapsed `(current >> 2) - (saved >> 2)` with word arithmetic | Supply actual world counter, including cap/gates/state4 behavior. A wall-second cooldown or LIP elapsed value does not reproduce history units. |

The adapter boundary therefore needs explicit raw clock reads, a scaled/pause
wrapper, an unscaled/pause wrapper, callback animation samples, scheduler
previous/phase/cap state, manager initialization/pass/IDs, and original world
counter. Live source ownership and save migration require a separate integration
change. The current standalone C# scheduler intentionally excludes unqualified
signed timestamp crossings; it must expose that diagnostic to its caller rather
than inventing native recovery.

Reproduction:

```sh
python3 tools/ppc-analysis/lanes/clock/clock_consumers.py /path/to/mac-feral/bin
OPENTPW_PPC_BIN_ROOT=/path/to/mac-feral/bin python3 -m unittest discover -s tools/ppc-analysis/lanes/clock -p test_clock_consumers.py -v
```

Validation: **9/9** new tests with the identified-input witness enabled; eight
bounded application hashes and four C runtime hashes; full six-instruction
import-glue validation for both RSE time imports and the OS host-clock import.
Synthetic examples qualify finite arithmetic only. No original instructions
were executed and no game source or original bytes were added.

### Script creation and reload phase follow-up

The script loader `0xb2ba0` maintains a manager-owned list count at `+12`
(`0xb2cb0..0xb2cc0`), separate from initialized state `+0` and pass `+4`.
New script `+0` points to the former head, former head `+4` points back to the
new script when nonnull, and manager `+16` becomes the new script
(`0xb2cc4..0xb2ce0`). Normal traversal therefore starts in reverse creation
order; sorting live VMs by attraction ID would change that order.

The next-script-ID word is manager `+8`, initialized to 1 at `0xb27b0`.
The loader reads it at `0xb3174`, increments and writes it at `0xb3180..0xb3184`,
and writes the prior value into new script `+8` at `0xb3188`. There is no
attraction/guest-ID lookup in this assignment. Manager pass `+4`, script ID
`+8`, and outer scheduler substep phase are three separate counters. Loader
creation does not itself establish an elapsed-clock origin for GETTIME.
A scan of aligned direct linked calls finds interpreter `0xaf534` only at
manager `0xb28f0`; indirect execution paths are not ruled out by that scan.
In particular, the live `RideVM.Child.Advance` recursion cannot be justified
as native manager ordering solely from the parent/child relationship.

Manager state writer `0xb3868` writes a 20-byte header size at `0xb38c8` and
copies initialized/pass/allocator/count/head words at `0xb390c..0xb3940`.
Reader `0xb4824` invokes manager initialization at `0xb48a4`, then restores
these words from the state block. Its pass-counter byte-reversed store is
`0xb495c`; the allocator word is separately reversed and stored at
`0xb4960..0xb4974`. The saved phase and next ID therefore override fresh
initialization on this reader path. The five-word header includes a list
reference requiring subsequent reconstruction; this is **not** a complete
production save framing or pointer-restoration recipe. Actual original-save
script records and matching load order remain the necessary integration proof.

The scheduler reset-request routine `0x11b4f4` has a direct caller at
`0x11b3d8`. This caller skips that routine when its saved argument in `r29`
is **1** (`0x11b3cc..0x11b3d0`). Consequently the reset cannot be assumed for
every transition through this caller. The argument's named load/restore mode
and indirect callers remain unresolved; no unconditional long-session signed
timestamp recovery was established. The new witness pins the selector's
comparison, BO/BI/target, reset call, four lifecycle hashes, ID assignment and
actual manager-header byte-swap fields. The clock suite remains **30/30** with
local identified inputs enabled.

The identified PC Jungle `Easymode.TPWI` contains one matching manager-header
candidate in its decoded payload at offset **1595542**. Container SHA-256 is
`6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`;
1,608,309-byte decoded payload SHA-256 is
`a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173`.
The `RSSE` marker is followed by length20 and these interpreted header fields:

| Header member | Fixture value | Mac field correspondence |
| --- | ---: | --- |
| Initialized word | 1 | manager `+0` |
| Pass counter | 6055 | manager `+4` |
| Next script ID | 16 | manager `+8` |
| List cardinality | 14 | manager `+12` |
| Opaque head reference | 80650884 | manager `+16`; subsequent list reconstruction required |

The three independent counts are present in a real save. The economy lane's
world-prefix tick755 must not be substituted for manager phase6055; this pair
does not establish the number of earlier drops or excluded callbacks. The
candidate agrees with the pinned Mac header layout, while its preceding framing,
serialized script-record boundaries and reference reconstruction remain
unqualified. It provides asset-structure corroboration, not proof of PC runtime
cadence or a generally usable save importer. Save inspection is bounded and
identity-pinned; output contains interpreted metadata only.

```sh
python3 tools/ppc-analysis/lanes/clock/save_phase_evidence.py /path/to/Data/levels/jungle/Easymode.TPWI
OPENTPW_PPC_BIN_ROOT=/path/to/mac-feral/bin OPENTPW_PPC_SAVE_PATH=/path/to/Easymode.TPWI python3 -m unittest discover -s tools/ppc-analysis/lanes/clock -p 'test_*.py' -v
```

Validation for this follow-up: **35/35** clock Python tests with both original
fixtures enabled, no skips; malformed/truncated synthetic headers; four
additional native lifecycle hashes; Python compilation and whitespace checks.
A complete production connection is specifically blocked on save-list framing
and reconstruction, the lifecycle selector's named meaning, indirect clock
configuration paths, and target-platform qualification of signed timestamp
crossings. Those gaps require separate source/caller or runtime evidence; this
lane introduces no guessed recovery or save migration.
