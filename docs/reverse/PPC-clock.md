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
