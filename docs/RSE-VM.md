# RSE VM: executing original ride scripts

October 9, 2026; updated October 14, 2026 (WAITABS deadline, MIN/SEC, child head tables). Status: **every opcode
used by the 308 original scripts has a VM handler**, and all scripts that can be
started run in the VM without faults. The two opcodes only Theme Park Inc
scripts contain — `WAITABS` (45) and `MULT` (48) — also have handlers now
([below](#theme-park-inc-gap-opcodes)).
This is VM-level execution, not original ride behaviour: the game systems the
scripts talk to (visitors, animation playback, sounds, objects, ride-type
controllers, park clock) are not implemented, so their effects go through a
hook that records them as unimplemented. Container format and static corpus
inventory: [RSE-SCRIPTS.md](RSE-SCRIPTS.md).

Code: `source/OpenTPW/VM/` (`RideVM`, `Operand`, `Instruction`, `RideOpcodes`,
`RideScriptEffects`, `RideScriptWorld`, `Handlers/*`). Tests:
`RideVMTests` (synthetic scripts per opcode family) and `RideVMCorpusTests`
(original corpus, inconclusive without `OPENTPW_GAME_PATH`).

## Coverage summary

| | Opcodes | Corpus instructions |
| --- | ---: | ---: |
| Implemented (VM does the whole job) | 41 | 8,049 |
| Hooked (VM handles operands/flags/waits; game effect → `IRideScriptEffects`) | 43 | 3,866 |
| Unknown (no handler) | 0 | 0 |
| **Corpus total** | **84** | **11,915** |

Of the 22 named opcodes the TPW corpus never uses, 10 have handlers: 4 from the
upstream docs alone (`SETVARINPARENT`, `YEAR`, `MONTH`, `DAY`),
`WAITABS`/`MULT` for the Theme Park Inc corpus
([below](#theme-park-inc-gap-opcodes)) and `PUSH`, `POP`, `SETLV` and `DBGMSG`,
read from tp.exe ([below](#original-interpreter-internals)); the other 12 (the ride
lights, `EVENT_EXT`, `GETCUSTPTCLCODE` and others) stay unknown and fault the
script if executed. Operand counts of every handler equal the upstream signature
counts and the corpus counts (`CRIT_UNLOCK`: 0).

"Implemented" means the VM handles the operation; much of its behavior still
comes from upstream notes and corpus control flow. COPY, zero-divisor DIV/MOD,
and RAND's raw bound now have independently reviewed static evidence from the
identified Feral Mac executable ([PPC-rides](reverse/PPC-rides.md), review
`d0e5c75`). This does not qualify Windows runtime equivalence, native traces,
or the host PRNG sequence. No original binary was run.

Primitive proof identity: Feral `SimThemePark.data` SHA-256
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`.
Code-section offsets: COPY case `0xaf5f0`, accumulator store `0xaf638`,
invalid-tag sentinel `0xb234c..0xb2350`; DIV zero arm `0xb0ca8`, MOD zero arm
`0xb0d30`, raw RAND bound `0xb07f8`. Static checks and independent review
establish these Mac paths. PC corpus tests independently verify all 1,243
COPY destinations are variables and all 56 RAND bounds are nonnegative
literals, so they do not exercise the malformed COPY or variable-bound RAND
forms. Their fault-free VM runs and the pinned Totem trace validate this
implementation against those inputs, not equivalence to an executed PC game.

## Execution model

- `RideVM` takes a parsed `RideScriptFile`: variables are a zeroed `int[]` of the
  declared size (the old constructor left the list empty and threw on the
  first write), strings come from the blob, and branch operands — code-word
  indices — are resolved to instruction indices at load time.
- Literals are sign-extended 16-bit values. `0xFFFF` is the only literal
  ≥ `0x8000` (918 uses) and must be −1: `ADD VAR_SPACELEFT 0xFFFF; BRANCH_Z`
  and `ADD VAR_COUNT 0xFFFF; BRANCH_PV loop` are count-downs.
- Literal writes are discarded by the operand accessor, with per-handler
  rules. SUB/DIV/MOD and RAND can use a literal `0` destination when only flags
  matter (`MOD 0 VAR_CAPACITY 9; BRANCH_Z`, `RAND 0 1; BRANCH_Z`). COPY requires
  a variable destination: a literal destination stops the VM with PC −10000
  and `Faulted`. The native handler leaves its source word unread, so the next
  native dispatch diagnoses an invalid opcode tag and sets that sentinel.
  The VM's parsed-instruction representation aborts directly; `Faulted` is its
  mapping of that diagnostic stop, not a recovered original state enum.
- Flags: Zero = result 0, Sign = result negative. COPY updates them from the
  copied value; WAIT, ENDSLICE and KILLOBJ preserve them. `BRANCH_PV` is
  **strictly** positive (Sign and Zero clear).
- Scheduling: each `Advance(delta)` — one `FixedStepClock` tick (60 Hz) in the
  game — adds `delta` to the script clock and runs one slice (**the cadence is
  an assumption**: the original runs a script's slice only on the passes its
  TURBO byte and id select, tp.exe `0x5516E1`, not yet modelled). The slice
  itself follows tp.exe ([below](#original-interpreter-internals)): the
  header's time slice (50 in every script) is a counter that falls by one per
  instruction outside a critical section; `CRIT_LOCK` stops the counting,
  `CRIT_UNLOCK` and `ENDSLICE` zero the counter, a wait ends the slice. The
  budget is what lets busy loops such as Totem's passenger loop
  (`TEST`/`BRANCH` for 10 s, no ENDSLICE) yield. A slice that stays locked for
  100,000 instructions faults (`[EXT:crit-slice-guard]`: the original would hang).
- Time unit is the millisecond (**inferred**: `WAIT 500…5300` around ride
  animations, `GETTIME; ADD VAR_STARTNOW 10000` passenger time-outs, animation
  durations fed into `WAIT`). Waits resume on the first tick at or after the
  wake time, so they are quantised to 1/60 s. `GETTIME` reads the park's shared
  scheduler clock (`RideScriptWorld.ClockMilliseconds`), as the original reads
  `0x10e844` on the global r30 (0xaf55c, 0xaf6d8) without subtracting a creation
  timestamp; a script created later starts at the clock's current value. The
  WAITANIM(_CH) deadlines use the same 32-bit clock word; WAIT, SETTIMER and the
  wake times count from the script's own creation, which only shifts their base.
  The clock starts at 0 with the park's script world (`[EXT:park-clock-origin]`; `APPROX:RIDES-044` retired): the PC clock is the app-lifetime scaled
  timer 0x785970 (started once at 0x54DCF0, read by 0x402D70, set only by the save writer 0x4164C0 and loader
  0x415270, never reset for a new park), so a new park's first script sees the time since program start;
  `GETTIME` (PC handler `0x551F1E`) stores that clock unchanged and every shipped script uses it only as a deadline or in a difference (`RideClockCorpusTests`); the only possible effect of the origin is the 32-bit wrapping `WAITANIM_CH` sum (PC handler `0x55320A`) inside a park's first 134 ms, and none of the 308 Theme Park World or 282 Theme Park Inc scripts issues `WAITANIM_CH`.
- `JSR`/`RETURN` use a LIFO stack in the script's array of the header stack size
  (every script that uses JSR declares ≥ 3), which HUSH/HOP share from the other
  end ([below](#original-interpreter-internals)); the old VM used a FIFO queue.
- Children: `SPAWNCHILD` and `SPAWNSOUND` load a script from the parent's
  archive through `RideVMOptions.ResolveScript` and run it after the parent
  each tick, in separate slots (bumper.RSE uses both). Variable IDs in
  `SET/GETVARINCHILD`/`GETVARINPARENT` are variable indices of the other script.
- `RideScriptWorld` holds the live scripts for `FINDSCRIPTRAND` (by `NAME`),
  `GETREMOTEVAR` and `SETREMOTEVAR`. A missing script (ID 0) reads 0 and ignores
  writes, because bus.RSE writes to its `FINDSCRIPTRAND "Traffic Lights"`
  result without checking it.
- DIV/MOD with a zero divisor write result 0 and set Zero, including literal
  destinations. Signed division truncates toward zero and remainder follows
  the dividend's sign. `Int32.MinValue / -1` still wraps to `Int32.MinValue`
  in this VM, with remainder 0; native signed-overflow behavior is unqualified.
- RAND sign-extends the raw low 16 bits of its maximum operand; it does not
  resolve a tagged variable. A variable-index-2 operand therefore bounds the
  result by 2 regardless of variable contents. Nonnegative bounds are
  inclusive; the host `System.Random` algorithm/seed/stream sharing remain
  unqualified. Negative bounds retain the VM's safety fault policy.
- Faults (script stops, `FaultMessage` set, logged): unknown opcode, RETURN
  without JSR, stack overflow, non-variable COPY destination, negative RAND
  bound, wrong operand kind, missing child script,
  child/parent variable index out of range, missing child or parent.

## Managed root-fault containment

A faulted VM freezes its own clock and stops both owned child slots before
another child slice can run. The parent is checked again after each descendant
advance, so a callback fault cannot run the next child slot; the current child
slice remains the servicing boundary. A live object runtime checks its root before and
after the root slice; a fault uses the existing object `Stop()` teardown before
coaster or animation progression. This releases riders, unregisters scripts,
stops owned sound chains and clears animation channels. The root's fault state
and diagnostic remain available. Normal completion, waits and child-only faults
do not stop the parent runtime; fixed-step pause still skips the runtime callback.
This is OpenTPW's deterministic host safety policy, not qualified native fault
handling, TOUR/BUMP success semantics or audio ownership equivalence.

The existing stopped-runtime snapshot records the root fault message and restores
it onto a stopped candidate. No snapshot schema changes or new support for live
sound-chain capture are introduced. `RootVmFaultContainmentTests` uses actual
synthetic VMs and CPU object runtimes to pin fault teardown, frozen presentation,
stopped-fault restore, ordinary completion and no-root controls. Fresh original
corpus, native audio and GPU checks remain deferred.

## Corrections to the upstream instruction notes

| Opcode | Upstream | Corpus evidence |
| --- | --- | --- |
| SUB, DIV, MOD | `<a> <b> <dest>` | destination first: `SUB VAR_TEMP VAR_TEMP 3000; WAIT VAR_TEMP`, `SUB VAR_TEMP VAR_STARTNOW VAR_TEMP; BRANCH_NV start`, `DIV VAR_TEMP VAR_CAPACITY 9` + `MOD 0 VAR_CAPACITY 9; BRANCH_Z; ADD VAR_TEMP 1` (ceiling) |
| CMP | bitwise AND | subtraction: `RAND VAR_TEMP 10; CMP VAR_TEMP 4; BRANCH_PV; BRANCH_NZ` only makes sense as `a − b` |
| BRANCH_PV | "positive" | excludes zero: 15× `BRANCH_PV x; BRANCH_Z x` |
| RAND | `0..max` | inclusive for nonnegative bounds; Mac static code sign-extends the raw bound word without variable resolution; host random sequence is not qualified |
| UNLIMBO, FORCEUNLIMBO | visitor input | output + flags: `UNLIMBO VAR_LETMEOFF; BRANCH_Z none; ADD VAR_ONRIDE 0xFFFF` (tp.exe `0x554EF0`/`0x554F74` confirm) |
| HUSH, HOP | visitor operations | the script's own heap: `HUSH VAR_LETMEON` pushes the id, `HOP VAR_TEMP2` pops it (tp.exe `0x553D25`/`0x553DAA`) |
| TURBO | `value` 0..1 | stores the low byte of the raw operand word in the script's TURBO byte (tp.exe `0x5542B9`) |
| TRIGANIM | unknown operands | `animation variant duration-out`: `TRIGANIM 5 0 VAR_TEMP; SUB VAR_TEMP VAR_TEMP 3000; WAIT VAR_TEMP; …; WAIT4ANIM` |
| SETTIMER / GETTIMER | unknown | countdown in ms; `GETTIMER dest` = remaining time, sets flags |

## Theme Park Inc gap opcodes

No Theme Park World script contains `WAITABS` (45) or `MULT` (48); the Theme
Park Inc corpus uses WAITABS 34 times in 19 scripts (obstacles, litbins and
`vegetation.RSE`) and MULT once, inside `vegetation.RSE`'s opening critical
section. Until October 13, 2026 executing either faulted the script; both now
have handlers. The Theme Park Inc slice's native smoke
([RUNNING.md](RUNNING.md)) never reached them on the science level (0 of 18
object scripts faulted even before), so this is static corpus use plus VM-level
execution — not a claim that observed retail play exercises them.

**WAITABS** keeps the traced Mac shape ([reverse/PPC-clock.md](reverse/PPC-clock.md),
dispatch `0xb0710`) and the PC's (tp.exe `0x553828`, `0x553783` for WAIT): despite the name, the first encounter stores
`now + operand` — a relative deadline, **without** WAIT's script-speed
division — in the same script field WAIT uses (`+160` Mac, `+0xA0` PC), always yields (the PC rewinds the program counter by 2),
and later encounters resume once the clock passes the deadline (unsigned compare, `jb` at `0x55386D`).
All 34 corpus uses are positive literals (1..10000): 1 ms frame spacing between
fire-effect ADDOBJs, 300/500 ms stage delays, a 1000 ms litbin fire-risk check
and a 10000 ms obstacle startup delay. The deadline is the 32-bit
scheduler clock word plus the operand as a wrapping unsigned sum, like WAITANIM's (`Scheduling.WaitAbsolute`; the VM
turns it into a wait until the scheduler clock reaches the deadline when the sum is ahead of the clock, so the wake follows the integer clock word, not the arming time's fraction): a sum that wraps (a negative operand) or an
operand of 0 is already past and yields once, resuming at the next servicing tick, and a sum of exactly 0 reads as not armed (the PC
word 0 is the sentinel, cleared once a wait ends), so the next execution stores again and yields again. The VM keeps no extra word,
so snapshots round-trip as before. VM-901 is narrowed to the one thing the double clock cannot do: a wait that spans the 2^32 ms wrap of the original's clock word.

**MULT** follows the SUB/DIV/MOD family: `MULT dest a b` →
`dest = a × b`, destination first, 32-bit wrap, flags set — the sole corpus use
`MULT VAR_TIMER VAR_TIMEOUT VAR_SCALE` (immediately consumed by `SETTIMER`)
matches that shape, and the upstream docs give the same three-operand signature.
tp.exe confirms it: interpreter case 48 (`0x553eb9`, jump table `0x5567d8`) reads the
destination word first, multiplies the two operand values with `imul` (32-bit wrap), stores
the product in the flag word and, through the shared tail `0x555b48`, in a variable
destination. Tests: `RideVMTests` (signed/literal/variable operands,
wrap, flags-only literal destination, WAITABS timing/yield/variable
operand/shared wake field/snapshot restore) and `RideVMTpiCorpusTests`
(original-corpus execution of both, gated on `OPENTPW_TPI_PATH`).

## Effects hook

### Host civil-time queries

TPW object scripts now route `YEAR`, `MONTH`, `DAY`, `HOUR`, `MIN` and `SEC` through their
shared script world's injected host-local clock (the system clock by default).
Each executed instruction samples independently. The existing Feral evidence
in [PPC-clock.md](reverse/PPC-clock.md#rse-civil-time-is-an-independent-clock-domain)
proves C `time`/`localtime` and the fields: year minus 1900, month 1–12, day
1–31 and hour 0–23; the PC opcodes 97–102 (`0x5567D8`, handlers `0x5565B2`, `0x556605`, `0x556659`, `0x5566AC`, `0x5566FF`, `0x55674E`) call `time`
(`0x67D3F0`) and `localtime` (`0x67D8E0`) and store tm_year (raw), tm_mon + 1, tm_mday, tm_hour, tm_min and tm_sec, so minute and
second are host time too (October 14, 2026). These are not `ParkCalendar` fields or scheduler elapsed
time. Pausing prevents script execution; the next eligible query samples
current host time, regardless of simulation speed, turn count or saved date.

The provider is unavailable for worlds created under TPI or an unsupported
edition, even if explicitly supplied. No TPI clock semantics are enabled.

The destination is written only when the operand is a variable (`& 0xFF000000 == 0x40000000`); the result register (+0x48) is set
either way and the branches test it, so a literal destination only sets the flags (tp.exe `0x556736`). VM-902 is retired: what
remains is an OpenTPW replay design point (`[EXT:vm-host-time]`): the host sample is neither saved nor hashed, so a replay needs the same samples.
Provider/current host input is neither snapshotted nor hashed. Previously
returned variables and flags remain saved and hashed; future queries after
restore may diverge unless the same external inputs are supplied. No save or
hash schema changes. `RideCivilTimeRuntimeTests` exercises actual synthetic
object runtimes and VM instructions without original assets or a GPU.

### Effect dispatch

Valid hooked operations call `IRideScriptEffects.Perform(RideEffectCall)` with the
resolved operand values. The VM writes the returned value to the opcode's
destination where the corpus shows one (WALKGET, UNBOUNCE,
GETANIM_CH, HOUR, …; HOP and the LIMBO family are VM-internal), sets flags where branches consume them, and for the
animation triggers takes it as the original trigger result in milliseconds
(the clip length, 1000 for a missing member). The animation opcodes follow the
Feral Mac handlers (docs/reverse/ANIM-plan.md §8): TRIGANIM, TRIGWAITANIM,
TRIGANIMSPEED and TRIGANIM_CH store `max(ms − 300, 300)` in their out operand and
overwrite the script's single WAIT4ANIM deadline (`now + out / script speed`, or
`out × 1000 / operand` for TRIGANIMSPEED); WAITANIM waits
`max(unsigned(ms − 300) / speed, 300)` (a clip under 300 ms saturates to
`INT32_MAX` ms, as in the original) and WAITANIM_CH `max(unsigned(ms − 300), 300)`
without the division or saturation, added to the 32-bit clock as a wrapping add,
so a clip under 300 ms gives a deadline in the past and the script continues at
its next slice; both and LOOPANIM(_CH) zero the deadline; LOOPANIM is a no-op while its
script-local token is unchanged, and the plain triggers reset the token;
TRIGWAITANIM re-executes until the channel shows the triggered AnimID;
FLUSHANIM(_CH) clears only the deferred clip; GETANIM(_CH) return the AnimID, or
−1 once a non-looping clip ended. Without an animation host (an unimplemented
effect) the script has no object, and every trigger uses 0 ms as the original's
null-object path does (`li r3,0` at 0xafb1c, 0xafc74, 0xafe88, 0xb0078, 0xb01d4,
0xb032c, 0xb0510): the triggers write 300, WAITANIM waits `INT32_MAX` ms, and
TRIGWAITANIM never resumes for a variable out operand; a literal out operand word 0x0000vvvv resumes once v equals the triggered AnimID (PC 0x5535c7, 0x552cd3: both compare the raw word + 1 with the token). Game objects
always have a host (`OriginalObjectEffects`); only headless VMs reach this.
TOUR/BUMP/COAST use the independently reviewed command
contracts from [PPC-rides](reverse/PPC-rides.md) (`15313d2`): mutators preserve
flags, supported queries set flags, and output queries write the effect's
returned value to their parameter. BUMP13/14 use the captured original input
for flags, not the controller return. No controller/admission logic or result
is synthesized by these dispatch rules.

Required-variable gates skip calls for literal parameters: BUMP1/2 and
TOUR3/4/16. BUMP11 and COAST2/3 query with optional variable output, so a
literal destination still permits flag-only queries. COAST7 is a native
consumed-parameter no-op and does not call an effect. Nonliteral commands,
unknown/default diagnostic command IDs and unqualified numeric-input tags
fault explicitly as VM safety policy; original native diagnostic fatality
and full invalid-host handling are not claimed equivalent.

The default, `UnimplementedRideScriptEffects`, returns 0 (no visitor, no
animation time), counts the call in `RideVM.UnimplementedEffects` and logs
"unimplemented effect" once per script and opcode. Controller queries detect
that marker and retain prior flags/output instead of treating its placeholder 0
as a real result. This lets the incomplete VM continue without manufacturing
visitor/admission values; it does not establish original controller behavior.
Other effect handlers retain their documented placeholder policy.

The dispatcher implements all 39 reviewed command roles; the 37 command IDs
used by the PC corpus are corroborated by parameter-kind/callsite metadata.
Core dispatch now owns output writes for BUMP2/11, TOUR4/16 and COAST2/3.
Supported admission/query values still come exclusively from the effect
implementation. It does not create visitors, vehicles, capacities, queues,
or success responses. Original controller systems remain unimplemented.

Only the root script has a head table: on the PC the init (0x558D2B) stores the thing id and, when it is 0, skips the head-id lookup and the table allocation, and SPAWNCHILD (0x555081) and SPAWNSOUND (0x5551AB) create their script with thing id 0 and copy the parent's thing id (+0xAC) only afterwards (0x555168), so ADDHEAD (0x554C89) and DELHEAD (0x554D6F) exit on the NULL table in child and sound scripts. OpenTPW does the same (`RideVisitorBridge.HeadTableFor`; HEADS-005 retired, October 14, 2026): a child's head opcode does nothing, there is no per-child table to save, and the canonical state is unchanged (none of the 46 associated child/sound scripts in the shipped four-theme catalog uses a head opcode, so no pinned hash moved). See the full inventory and pixel proof in GUESTS.md.

## Corpus run (`RideVMCorpusTests`)

Roots are the 263 scripts not started by another script; 44 are children (28
`EventMap.rse`, 16 `SPAWNCHILD` targets) and one, `space/rides/creature.wad/
CreatureFX.RSE`, is an orphan copy of hoverbot's "Driod Anim Child" that reads
its parent but is never spawned. All 263 roots run in one shared world for
3,600 ticks (60 s), twice:

1. default effects, `VAR_CAPACITY` 6, ride open: each script has an object
   without clips (every animation trigger returns the missing-member 1000 ms and
   the channel stays on AnimID 12; everything else unimplemented): 20.4 M
   instructions, no faults, no halts, 222 running/41 waiting at the end;
2. "chaos" effects (seeded random results and animation durations, channels
   that show the last TRIGWAITANIM AnimID, random pokes of
   LETMEON/LETMEOFF/RIDECLOSED/BREAKSTAT/WORN/TRIGGER/COMMAND): 14.6 M
   instructions, no faults, no halts.

Since ANIM-F a script without any host has no object and, like the original,
waits `INT32_MAX` ms on its first WAITANIM, so the corpus run gives the default
pass the clip-less object above; the figures below predate that change.

Together the runs execute 83 of the 84 corpus opcodes (GETREMOTEVAR is never
reached; it is covered by a synthetic test) and start all 44 child scripts.
Set `OPENTPW_RSE_VM_REPORT_OUT=<file>` to write the full report.

After the reviewed controller flag dispatch change, baseline default effects
execute 23,768,180 instructions (previously 23,776,799), with 216 roots running
and 47 waiting. Chaos effects execute 17,065,708 (previously 17,200,190), with
202 running and 61 waiting. The verified Patch 2 corpus executes 23,768,063
default and 17,062,282 chaos instructions. All runs remain fault/halt-free,
and the Totem trace remains pinned. Totals changed because accumulator
contracts, query output directions, unsupported-query policy and COAST7 no-op
handling affect branching; they do not demonstrate original controller or PC
runtime equivalence. Focused tests cover all 39 reviewed command roles,
required/optional variable gates, supported outputs, unsupported markers and
unknown-command safety faults.

`TotemScriptTraceWithItsObjectIsPinned` pins 30 s of Totem.RSE run as the placed
original object (`OriginalObjectRuntime`, seed 1234, capacity 6, no visitors), so
the animation opcodes have their real host: WAITANIM 0 at 16 ms plays AnimID 0 and
waits its length less 300 ms divided by the script speed (Totem's `InitSpeed` 60 gives
1.1), then the 10 s passenger time-out, WAIT 700 (also divided by the speed),
VAR_RUNNING = 1, a random TRIGANIM_CH set via JSR at 14,749 ms, TRIGANIM 5 at
15,216 ms, the sound/object timeline to 26,216 ms, then WAIT4ANIM
and VAR_RUNNING = 0 because nobody boarded. All five clip requests resolve.
`TotemWithoutAnObjectWaitsOnItsFirstWaitAnim` pins the same script with no host:
like the original's null-object path its first WAITANIM gets 0 ms and waits
`INT32_MAX` ms, so nothing else happens.

The script speed is `0.5 + SPEED / 100` (0x551769) with SPEED the ride's speed bias: ride init writes
`Upgrades[0].InitSpeed` when above 0 (0x4DB51C..0x4DB54C), children copy the parent's (0x551888), the default is 50,
and upgrade completion (0x4DF8F0) writes the new level's `InitSpeed` when above 0, in the same routine that sets capacity from
+0x198 and duration from +0x1A0 (`OriginalObjectRuntime.TryApplyCatalogueTierDefaults` with `completion`, each only when
above 0). A completed upgrade therefore changes the speed of every later `WAIT`, animation trigger and wait
of that ride's scripts. The ride window's speed slider sets it too: its apply (0x4AF440, run when the window closes or steps) calls the ride's
SetSpeed 0x4DD6E0 (`OriginalObjectRuntime.ApplyRideWindow`). A TRIGANIMSPEED operand of 0 or below faults
(`[EXT:trig-anim-speed-guard]`, formerly RIDES-038: the corpus has four uses, all literal 1800 to 4000).

## Sandbox Totem

The placed Totem (`PrototypeRide`) now runs its original `Totem.RSE` on the
fixed simulation tick. Open/Close set `VAR_RIDECLOSED` (0/1); `VAR_CAPACITY`
is the .sam `Upgrades[0].InitCapacity` (6); `VAR_DURATION` has no source and
stays 0 (one cycle per run). The script decides **when** the carriage moves:
its `TRIGANIM 5` (`ANIM_Main` in the repository's ScriptDefs and the docs'
`TRIGANIM ANIM_Main 0 0` example) plays the original `totemm1.MD2` clip once
through the object's channels ([MD2-MODELS.md](MD2-MODELS.md)); its 430 ticks
at 30 ticks/s give the trigger result 14,333 ms, so the duration operand gets
14,033 ms and WAIT4ANIM waits until that deadline. Which Totem clip the
original binds to `ANIM_Main` is inferred (`totemm1` is the full cart/cog
cycle), not traced. All other Totem effects (channel animations
`totemm2…10`, objects, screams, reverb, visitors) are unimplemented; its EVENT
instructions now play their original sounds (the type routes are traced,
AUDIO.md).
With no visitors, an opened Totem waits 10 s, moves once, and repeats.

## Visitors

The visitor opcodes WALKON, WALKOFF, WALKGET, BOUNCE, UNBOUNCE, FORCEUNBOUNCE,
BOUNCING, ADDHEAD and DELHEAD and the `VAR_LETMEON`/`VAR_LETMEOFF` host protocol
are implemented against real guests by `RideVisitorBridge` (an `IRideScriptEffects`
layer; `VisitorRideScriptEffects` wraps any other effects). HUSH, HOP and the
LIMBO family are not visitor operations: they work on the script's own heap and
limbo table inside the VM ([below](#original-interpreter-internals)); the guest
boards when the script consumes `VAR_LETMEON` (or through WALKON/BOUNCE), and
leaves when the script writes its id to `VAR_LETMEOFF`. Every original object (OBJECTS.md) and the sandbox Totem use
it: with guests nearby its script fills at ≈3.3 s and unloads through
HOP/WALKOFF/WALKGET. Semantics are inferred from the corpus, see
[GUESTS.md](GUESTS.md). The default `UnimplementedRideScriptEffects` still returns
0 for scripts without a bridge.

Since the objects slice every original object runs its script this way with
generic effects that play the original clips for all animation opcodes,
including the Totem's `TRIGANIM_CH` channel clips (`totemm2…10`); the ANIM_* →
member mapping, channels and the corpus run of all 262 object scripts are in
[OBJECTS.md](OBJECTS.md).

## Original interpreter internals

Read from tp.exe (the interpreter `0x551CB0` executes one instruction and
dispatches through the jump table `0x5567D8`; the scheduler pass is `0x5516B0`).
The unit tests are `RideVMInternalsTests`.

- **Slice and critical flag.** At the start of each slice the pass clears the
  global critical flag (`0x87919C`, `0x551701`) and copies the script's time
  slice (header, +0x94) into the slice counter (+0x98). It then runs
  instructions while the counter is above 0 and the pc is valid; after each
  instruction the counter falls by one only while the flag is 0
  (`0x551724`..`0x551730`). `CRIT_LOCK` (`0x551D44`) sets the flag,
  `CRIT_UNLOCK` (`0x551D59`) clears it and zeroes the counter, so it always ends
  the slice; `ENDSLICE` (`0x5542A8`) zeroes the counter; `END` (`0x5567B1`) sets
  the pc to -10000 (the script is destroyed at the end of the pass); a wait
  rewinds the pc and ends the slice. The flag's only reader is the slice loop:
  no host or game code looks at it, so the bridge's earlier "never inside
  `CRIT_LOCK`" gates (`RideVisitorBridge.PresentForBoarding`, `HostStep`) had no
  original counterpart and are gone. Whether a slice runs at all in a pass is
  decided before: a script with a nonzero TURBO byte always runs, any other only
  when `(script +8 xor pass counter) & 7` is 0 (`0x5516E1`..`0x5516F6`); that
  cadence is not modelled yet.
- **TURBO** (opcode 51, `0x5542B9`) stores the low byte of the raw operand word
  (not resolved: a variable operand stores its index byte, `0xFFFF` stores
  `0xFF`) at script +0xB8 and does nothing else. `RideVM.TurboByte` holds it
  for the later cadence change; it is part of the VM snapshot and, once nonzero,
  of the canonical hash.
- **Heap and stack.** Each script owns one array of header-stack-size words
  (+0x20, size +0x54, zero-filled here). `JSR` (`0x5539A9`), `RETURN`
  (`0x553A32`), `PUSH` (`0x553C1E`) and `POP` (`0x553CBA`) use it from the top
  (pointer +0x40, starts at size - 1, falls on push); `HUSH` (`0x553D25`) and
  `HOP` (`0x553DAA`) use it from the bottom (count +0x44, starts at 0). Nothing
  checks the two ends against each other. A return address is stored as
  `pc | 0x20000000` where the pc is a code-word offset (the VM maps it back to an instruction on `RETURN` and faults on a word that starts none); `RETURN` jumps only to a word with that tag and otherwise
  continues at the next instruction. The original's asserts are a bare `ret`
  (`0x5DA3C0`): a full `JSR` jumps without pushing (the VM no longer faults
  there); a full `PUSH`, an empty `POP` and an empty `RETURN` set the pc to
  -10000 (the VM's fault, pc -10000); a full `HUSH` and an empty `HOP` do
  nothing at all (flags and destination untouched). `HUSH` pushes the resolved
  operand and sets the flags to it; `HOP` pops LIFO into the flags and a
  variable destination. `SETLV` (`0x551E19`) sets the flags from the raw operand
  word (a literal counts as its unsigned 16 bits, a variable as its tagged
  word); `DBGMSG` (`0x554243`) skips its operand.
- **HUSH/HOP in the corpus.** The 39 `HUSH VAR_LETMEON` come right after the
  script has taken the called guest (`TEST VAR_LETMEON`, then `HUSH`, `ADD
  VAR_ONRIDE 1` and `ADDHEAD`/`WALKON`/`COPY VAR_LETMEON 0`), and the 39
  `HOP VAR_TEMP2`/`VAR_LETMEOFF` come before the guest leaves (`WALKOFF` after 19
  of them, `DELHEAD` after 31): the script pushes the id it just took and pops
  the newest (LIFO, not the oldest) when unloading. The earlier bridge treated
  HUSH as boarding and HOP as "first boarded guest not yet hopped"; the boarding
  now comes from the `VAR_LETMEON` protocol (`HostStep` marks the called guest
  aboard when the script consumed `VAR_LETMEON`) and `WALKON`, and HOP hands back
  the ids the script itself pushed.
- **Limbo table** (opcodes 58-62). A per-script table of header-limbo-size
  `{value, expiry}` slots (+0x24, zeroed) and an occupied count (+0x60), kept
  by the VM and part of its snapshot. `LIMBO v s` (`0x554DE5`): the first slot
  whose value is 0 takes `v` and the expiry `clock + s x 1000` (the same
  32-bit millisecond clock as `GETTIME`, wrapping), count++, result 1; a full or
  empty table gives result 0 and stores nothing (a stored value of 0 leaves its
  slot "free" but still counts). `UNLIMBO d` (`0x554EF0`): the first occupied
  slot whose expiry is strictly below the clock (signed 32-bit) is cleared in
  place (its expiry stays), count--, result = its value, else 0; a variable
  destination is written, a literal one only sets the flags. `FORCEUNLIMBO d`
  (`0x554F74`) needs a variable destination (a literal does nothing at all) and
  takes the first occupied slot whatever its expiry. `INLIMBO d` (`0x555012`) is
  the count, `LIMBOSPACE d` (`0x555045`) is size - count. The bridge only
  consumes what the script does with the result (`UNLIMBO VAR_LETMEOFF`, then
  the host releases that guest); it no longer holds a limbo list, does not
  board on `LIMBO`, and its snapshot's `Hopped`/`Limbo` arrays are always empty
  (kept for the file format; limbo state of older saves is dropped).
- Not implemented, tp.exe addresses noted: the ride lights (`ENABLELIGHT`
  `0x555C7A`, `DISABLELIGHT` `0x555CCD`, `SETLIGHT` `0x555D20`, `COLOURLIGHT`
  `0x555D9A`; `SETLIGHT` reads two operands, the upstream notes say one),
  `EVENT_EXT` (`0x552707`) and `GETCUSTPTCLCODE` (`0x5563D5`) are game effects
  with no use in either corpus.

## Not original behaviour yet

- Slice cadence (one slice per 60 Hz tick: the original runs a script's slice
  only on the passes its TURBO byte and script id select, tp.exe `0x5516E1`) and
  the millisecond unit of WAIT are not original; the slice budget, the critical
  flag, HUSH/HOP, JSR/RETURN/PUSH/POP, the limbo table and TURBO's byte are
  ([below](#original-interpreter-internals)).
- The ride lights (82–85, tp.exe `0x555C7A`–`0x555D9A`), `EVENT_EXT`
  (`0x552707`) and `GETCUSTPTCLCODE` (`0x5563D5`) are game effects the VM does
  not implement; no script of either corpus uses them.
- Visitor opcodes work for every placed original object (its `RideVisitorBridge`);
  walks along ride nodes are instantaneous, riders are not drawn, and
  TOUR/BUMP/COAST guest handling and WALKST_FLOAT/WALKFLOATSTAT/WALKFLOATSTOP
  are unimplemented.
- Animation playback only through the objects' effects (OBJECTS.md; rigid node
  tracks, vertex animation not decoded); EVENT types 3-9 and REPAIREFFECT play
  their original sounds (the category routes are traced, AUDIO.md); repair hide (0)
  uses the same sampled placed sound-space origin and owned-loop cleanup as EVENT
  types 3–9, while show keeps its qualified flat family 75 path. Types
  1/2 create particles in the original and remain unimplemented here; type 0
  is unsupported, not a positioned sound loop. No objects/particles, no ride-type
  controllers (TOUR/BUMP/COAST), no light opcodes. TPW YEAR/MONTH/DAY/HOUR/MIN/SEC
  sample host civil time (an unsaved external input, `[EXT:vm-host-time]`); they do not read the park calendar.
- What the engine does with `EventMap.rse` variables, how the host is meant to
  feed `VAR_LETMEON`/`VAR_LETMEOFF`, and the `0xFFFF` second operand of EVENT
  are unknown.
- Child scripts get their own clock and RAND stream; whether the original
  shares them is unknown.

## Original-binary evidence

Static, read-only evidence from the original Mac PowerPC build; no original
binary was executed. File identity: `SimThemePark.data`, SHA-256
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`.

**Proven (byte-level, reproducible):** at file offset 1,929,106 the binary holds
a NUL-separated string region. Its identifier strings (`[A-Z][A-Z_0-9]+`, digit-only
pooled literals such as `0` and `2` skipped) start with exactly 106 names, and
the first 106 match the opcode table above ordinal for ordinal: `NOP` (0) to
`SPARK` (105). Identifier 107 is `RSSE`, not an opcode name. The numbering
0–105 is therefore confirmed by name order alone.

**Not proven:** that the interpreter indexes this table by opcode number. No
code that reads the table has been located or traced, so the link between the
name table and opcode numbers is an inference from matching order.

Reproduce with `python3 -I repro.py <path to SimThemePark.data>` from the repository root (prints `True`):

```python
import re, hashlib, sys
data = open(sys.argv[1], "rb").read()  # path to SimThemePark.data
assert hashlib.sha256(data).hexdigest() == "04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5"
names = re.findall(rb"[A-Z][A-Z_0-9]+", data[1929106:1929106 + 4000])[:106]
table = re.findall(r"^\| (\d+) \| `([A-Z_0-9]+)` \|", open("docs/RSE-VM.md").read(), re.M)
print([n.decode() for n in names] == [name for _, name in table])
```

**Diagnostic strings (leads for static analysis; meanings inferred from the text):**

| Offset | String | What it suggests |
| ---: | --- | --- |
| 1930408 | `RSSE: Walknodes need a 'setwalk' in the script` | a walk-node check. No `SETWALK` name is among the 106 opcode names, so the keyword is a script-side construct, a WALKON action or something outside the table; compare with the header walk size |
| 1931191 | `RSSE: Script & Script interpreter are different versions` | a version comparison between the script and the interpreter; the script-side field is the open question ([RSE-SCRIPTS.md](RSE-SCRIPTS.md), header version) |
| 1931254 | `Zero length script` | explicit loader fault for an empty file |
| 1931282 | `Not a RSSE script file` | magic check, matching the reader's rejection of a bad magic |
| 1931491 | `Load Fail - Missing header` | header-length fault, matching the reader's truncation check |
| 1930962 | `RSSE: Unknown instruction` | an explicit fault for an unknown opcode, matching `RideVM`'s fault (the binary's own behaviour is unverified) |
| 1930846 | `RSSE: Stack Error` | fault path for the return stack, sized by the header stack field |
| 1930864 | `RSSE: Heap Error` | a second runtime allocation fault; what the heap holds is unknown |
| 1944713 | `Ride script not located for %s - using placeholder` | a missing script target falls back to a placeholder, compare `FINDSCRIPTRAND` returning 0 |

These strings are not semantics. They show which faults the original reports
explicitly and that the script format carries a version check. Next static
steps: locate the loader code that reads the header and compares versions,
and the code that indexes the name table.

## Opcode table

Generated by `RideOpcodes.FormatCoverageTable` (corpus counts from the pinned
histogram); `CoverageDocumentMatchesTheHandlerTable` fails if this table and the
handler attributes diverge.

| # | Opcode | Corpus uses | Status | Evidence |
| ---: | --- | ---: | --- | --- |
| 0 | `NOP` | 2 | implemented | docs |
| 1 | `CRIT_LOCK` | 150 | implemented | TPW-EXE 0x551D44: sets the critical flag, which only stops instructions counting against the slice budget (the flag is cleared at every slice start; no host or game code reads it) |
| 2 | `CRIT_UNLOCK` | 240 | implemented | TPW-EXE 0x551D59: clears the critical flag and ends the slice (240 corpus uses, 0 operands) |
| 3 | `COPY` | 1243 | implemented | Mac-static: variable destination receives the resolved source and sets flags; non-variable destination aborts with PC -10000 |
| 4 | `SETLV` | 0 | implemented | TPW-EXE 0x551E19: sets the flags from the raw operand word (not resolved: a variable operand counts as its tag word, a literal as its unsigned 16 bits); no corpus use |
| 5 | `SUB` | 74 | implemented | docs (sets flags); corpus: operand order is `SUB dest a b` → dest = a − b |
| 6 | `ENDSLICE` | 396 | implemented | name; corpus: sits in idle loops (`ENDSLICE; TEST VAR_RIDECLOSED; BRANCH_NZ`), so it yields until the next slice; flags survive it |
| 7 | `GETTIME` | 172 | implemented | STP-PPC 0xaf6d8: the shared scheduler clock's 32-bit millisecond word, stored unchanged (no creation timestamp subtracted) |
| 8 | `ADDOBJ` | 644 | hooked | docs (`type parameter id slot`) |
| 9 | `ADDOBJ_EXT` | 0 | hooked | TPW-EXE 0x5520E8 (`type key effect control slot`): ADDOBJ with the control operand; no corpus use |
| 10 | `KILLOBJ` | 235 | hooked | docs (`slot`); does not touch flags (`ADD VAR_COUNT 0xFFFF; KILLOBJ 200; BRANCH_PV`) |
| 11 | `FADEOBJ` | 113 | hooked | docs (`slot`) |
| 12 | `SETOBJPARAM` | 20 | hooked | docs (`slot parameter value`) |
| 13 | `EVENT` | 527 | hooked | docs (`type unknown event`); event names are theme-dependent and not assigned |
| 14 | `EVENT_EXT` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 15 | `FLUSHANIM` | 17 | hooked | STP-PPC 0xafa68 → 0xa6c90: clears only channel 0's deferred clip; the playing clip and the WAIT4ANIM deadline stay |
| 16 | `TRIGANIM` | 74 | hooked | STP-PPC 0xafa94: trigger channel 0, flags 0; out = max(ret − 300, 300); WAIT4ANIM deadline = now + out / script speed; LOOPANIM token reset |
| 17 | `WAITANIM` | 547 | hooked | STP-PPC 0xafbbc: trigger channel 0, flags 0; waits max(unsigned(ret − 300) / script speed, 300) (a clip under 300 ms saturates to INT32_MAX ms) on a u32 deadline; zeroes the WAIT4ANIM deadline; LOOPANIM token reset |
| 18 | `LOOPANIM` | 210 | hooked | STP-PPC 0xafd00: no-op while token anim + (sub << 16) equals script +168; else loops on channel 0 (flags 1), stores the token, zeroes the WAIT4ANIM deadline |
| 19 | `TRIGWAITANIM` | 133 | hooked | STP-PPC 0xafd94: as TRIGANIM, then re-executes until channel 0's AnimID equals the triggered one (+188 = id + 1), yielding while it differs |
| 20 | `GETANIM` | 0 | hooked | STP-PPC 0xaff3c: channel 0's AnimID (12 = none), −1 once its non-looping clip ended (channel flag 0x4); result sets flags |
| 21 | `TRIGANIMSPEED` | 4 | hooked | STP-PPC 0xaffc4: trigger channel 0 at script speed × operand / 1000; out = max(ret − 300, 300); deadline = now + out × 1000 / operand (signed divw); LOOPANIM token reset |
| 22 | `FLUSHANIM_CH` | 0 | hooked | STP-PPC 0xb00f4 → 0xa6c90: clears only the operand channel's deferred clip |
| 23 | `TRIGANIM_CH` | 63 | hooked | STP-PPC 0xb013c: as TRIGANIM on channel operand 4 (Totem `TRIGANIM_CH 5 1 0 1`); leaves the LOOPANIM token |
| 24 | `WAITANIM_CH` | 0 | hooked | STP-PPC 0xb0268, PC 0x55320A (same arithmetic): as WAITANIM on channel operand 3 without the speed division or fctiwz: unsigned max(ret − 300, 300) added to the clock as a wrapping u32 (0xb035c), so a clip under 300 ms gives a deadline in the past and the script continues next slice; zeroes the deadline |
| 25 | `LOOPANIM_CH` | 1 | hooked | STP-PPC 0xb037c: loops on channel operand 3 (flags 1) on every call, no token check; zeroes the deadline |
| 26 | `TRIGWAITANIM_CH` | 0 | hooked | STP-PPC 0xb0404: as TRIGWAITANIM on channel operand 4; leaves the LOOPANIM token |
| 27 | `GETANIM_CH` | 15 | hooked | STP-PPC 0xb05b8: `GETANIM_CH dest channel`: the channel's AnimID, −1 once ended; result sets flags (corpus `GETANIM_CH 0 n; BRANCH_PV x; BRANCH_Z x`) |
| 28 | `RAND` | 56 | implemented | Mac-static: signed raw bound word, inclusive nonnegative maximum, sets flags; host PRNG remains unqualified |
| 29 | `JSR` | 70 | implemented | docs; TPW-EXE 0x5539A9: the return address goes onto the script's shared stack/heap array (header stack size, top-down); a full array still jumps, without pushing; corpus: every script using JSR declares stack ≥ 3 |
| 30 | `RETURN` | 33 | implemented | docs; TPW-EXE 0x553A32: pops the shared array LIFO; only an address-tagged word is jumped to; an empty stack kills the script |
| 31 | `BRANCH` | 725 | implemented | docs; all 725 operands are branch words |
| 32 | `BRANCH_Z` | 787 | implemented | docs (zero flag set) |
| 33 | `BRANCH_NZ` | 943 | implemented | docs (zero flag clear) |
| 34 | `BRANCH_NV` | 55 | implemented | docs (negative); corpus: `SUB VAR_TEMP VAR_STARTNOW VAR_TEMP; BRANCH_NV` passenger time-outs |
| 35 | `BRANCH_PV` | 84 | implemented | docs (positive); corpus: strictly positive, because 15× `BRANCH_PV x; BRANCH_Z x` and `CMP VAR_TEMP 4; BRANCH_PV; BRANCH_NZ` would otherwise be dead code |
| 36 | `DBGMSG` | 0 | implemented | TPW-EXE 0x554243: skips its operand and does nothing; no corpus use |
| 37 | `NAME` | 277 | implemented | docs; the name is what FINDSCRIPTRAND searches for |
| 38 | `TEST` | 1318 | implemented | docs |
| 39 | `CMP` | 61 | implemented | docs say bitwise AND; corpus needs subtraction (a − b): `RAND VAR_TEMP 10; CMP VAR_TEMP 4; BRANCH_PV; BRANCH_NZ`, `CMP VAR_CAPACITY VAR_CARS; BRANCH_Z` |
| 40 | `PUSH` | 0 | implemented | TPW-EXE 0x553C1E: pushes the resolved value onto the JSR array (top-down, no collision check) and sets the flags; a full array kills the script; no corpus use |
| 41 | `POP` | 0 | implemented | TPW-EXE 0x553CBA: pops the JSR array into the flags and a variable destination; an empty array writes 0 and kills the script; no corpus use |
| 42 | `HUSH` | 39 | implemented | TPW-EXE 0x553D25: not a visitor operation; appends the resolved value to the script's heap (header stack size, up from index 0) and sets the flags; a full heap does nothing |
| 43 | `HOP` | 39 | implemented | TPW-EXE 0x553DAA: pops the newest heap value (LIFO) into the flags and a variable destination; an empty heap does nothing (`HUSH VAR_LETMEON … HOP VAR_TEMP2; WALKOFF VAR_TEMP2`) |
| 44 | `WAIT` | 458 | implemented | STP-PPC 0xb0658: milliseconds divided by the script speed (0.5 + SPEED/100), truncated; flags survive it (`WAIT 300; BRANCH_NZ`) |
| 45 | `WAITABS` | 0 | implemented | tp.exe 0x553828 and STP-PPC 0xb0710: despite the name the first encounter stores the 32-bit clock + operand (no WAIT speed division) in the shared deadline word and yields; later encounters wait while the clock is unsigned-below it; a wrapped deadline is already past and a deadline of exactly 0 arms again (Theme Park Inc corpus: 34 uses, literals 1..10000) |
| 46 | `WAIT4ANIM` | 170 | hooked | STP-PPC 0xb07a0: deadline 0 continues; otherwise blocks while now < deadline (re-executing), then clears it |
| 47 | `ADD` | 541 | implemented | docs (dest += value, sets flags); corpus: 115 branches consume ADD flags, e.g. `ADD VAR_SPACELEFT 0xFFFF; BRANCH_Z` |
| 48 | `MULT` | 0 | implemented | TPW-EXE 0x553eb9: dest-first, a × b by imul (32-bit wrap), result in the flag word and a variable destination; Theme Park Inc corpus: `MULT VAR_TIMER VAR_TIMEOUT VAR_SCALE` |
| 49 | `DIV` | 4 | implemented | corpus: `DIV dest a b`; Mac-static: signed truncation, zero divisor returns 0; signed overflow retains VM wrap policy |
| 50 | `MOD` | 5 | implemented | corpus: `MOD dest a b`; Mac-static: signed remainder, zero divisor returns 0; signed overflow retains VM zero policy |
| 51 | `TURBO` | 20 | implemented | TPW-EXE 0x5542B9: stores the low byte of the raw operand word in the script's TURBO byte (a variable operand stores its index byte); nothing else |
| 52 | `END` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 53 | `TOUR` | 76 | hooked | Mac-static command roles: mutators preserve flags; supported queries set flags/output; required-variable gates skip invalid calls |
| 54 | `BUMP` | 199 | hooked | Mac-static command roles: mutators preserve flags; queries set flags/output; duration commands set flags from original input |
| 55 | `COAST` | 144 | hooked | Mac-static: commands 1/4/5/6/8 preserve flags; 2/3 set flags and optional output; 7 is a no-op |
| 56 | `ADDHEAD` | 31 | hooked | docs (`visitor`) |
| 57 | `DELHEAD` | 31 | hooked | docs (`visitor`) |
| 58 | `LIMBO` | 24 | implemented | TPW-EXE 0x554DE5 (`value seconds`): the first free slot of the script's limbo table (header limbo size) takes the value and expiry now + seconds * 1000; result 1 (flags), 0 when the table is full |
| 59 | `UNLIMBO` | 24 | implemented | TPW-EXE 0x554EF0: releases the first occupied slot whose expiry is strictly past into the flags and a variable destination, else 0 (`UNLIMBO VAR_LETMEOFF; BRANCH_Z`) |
| 60 | `FORCEUNLIMBO` | 23 | implemented | TPW-EXE 0x554F74: variable destination only (anything else does nothing at all); releases the first occupied slot whatever its expiry, else 0 (`FORCEUNLIMBO VAR_LETMEOFF; BRANCH_NZ loop`) |
| 61 | `INLIMBO` | 6 | implemented | TPW-EXE 0x555012: the occupied slot count into the flags and the destination (`INLIMBO 0; BRANCH_Z`) |
| 62 | `LIMBOSPACE` | 24 | implemented | TPW-EXE 0x555045: limbo size minus the occupied count into the flags and the destination (`LIMBOSPACE 0; BRANCH_Z full`) |
| 63 | `SPAWNCHILD` | 20 | implemented | docs (one child); corpus: all 20 names resolve to an RSE in the same WAD; the child runs after its parent each tick |
| 64 | `SPAWNSOUND` | 28 | implemented | docs; corpus: always `EventMap.rse`, which only fills VAR_EVT0…9/VAR_PAR0 and idles; runs in its own slot (bumper.RSE has both). What the engine reads from it is not implemented |
| 65 | `REMOVECHILD` | 4 | implemented | docs |
| 66 | `SETVARINCHILD` | 7 | implemented | docs (`variable value`) |
| 67 | `GETVARINCHILD` | 9 | implemented | docs (`dest variable`) |
| 68 | `SETVARINPARENT` | 0 | implemented | docs only (no corpus use); mirror of SETVARINCHILD |
| 69 | `GETVARINPARENT` | 10 | implemented | docs (`dest variable`) |
| 70 | `BOUNCESETNODE` | 1 | hooked | unknown upstream; passed through |
| 71 | `BOUNCESETBASE` | 4 | hooked | unknown upstream; passed through |
| 72 | `BOUNCE` | 4 | hooked | unknown upstream (`visitor unknown`); passed through |
| 73 | `UNBOUNCE` | 4 | hooked | unknown upstream (`visitor`); passed through |
| 74 | `FORCEUNBOUNCE` | 8 | hooked | unknown upstream (`visitor`); passed through |
| 75 | `BOUNCING` | 25 | hooked | unknown upstream; corpus `BOUNCING VAR_RUNNING; BRANCH_Z` → dest + flags |
| 76 | `WALKON` | 47 | hooked | docs (`visitor u1 u2 u3 u4 action u5`, WALK_ACTION_* in ScriptDefs); passed through |
| 77 | `WALKOFF` | 53 | hooked | unknown upstream (`visitor`); passed through |
| 78 | `WALKGET` | 43 | hooked | unknown upstream (`dest`); corpus `WALKGET VAR_LETMEOFF; BRANCH_Z wait` → dest + flags |
| 79 | `WALKST_FLOAT` | 1 | hooked | unknown upstream; 1 corpus use (ZeroG); passed through |
| 80 | `WALKFLOATSTAT` | 1 | hooked | unknown upstream; corpus `WALKFLOATSTAT 0; BRANCH_NZ` → dest + flags |
| 81 | `WALKFLOATSTOP` | 1 | hooked | unknown upstream; 1 corpus use; passed through |
| 82 | `ENABLELIGHT` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 83 | `DISABLELIGHT` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 84 | `SETLIGHT` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 85 | `COLOURLIGHT` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 86 | `STARTSCREAM` | 40 | hooked | docs (`visitor unknown`) |
| 87 | `STOPSCREAM` | 80 | hooked | docs |
| 88 | `SINGLESCREAM` | 46 | hooked | docs (`visitor unknown`) |
| 89 | `SCREAMLEVEL` | 81 | hooked | docs (0–100) |
| 90 | `FINDSCRIPTRAND` | 5 | implemented | docs (`name dest`); corpus: `FINDSCRIPTRAND "Zob Upgrade" VAR_SCRIPTID; BRANCH_Z none` → dest = random live script with that NAME or 0, sets flags |
| 91 | `GETREMOTEVAR` | 2 | implemented | corpus: `GETREMOTEVAR 0 VAR_SCRIPTID 0; BRANCH_Z` → `dest script variable`, sets flags; mirror of SETREMOTEVAR; a missing script reads 0 |
| 92 | `SETREMOTEVAR` | 10 | implemented | docs (`script variable value`); bus.RSE writes to the FINDSCRIPTRAND result without checking it, so a missing script (ID 0) is ignored |
| 93 | `REPAIREFFECT` | 140 | hooked | docs (0 hide / 1 show) |
| 94 | `GETCUSTPTCLCODE` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 95 | `SETTIMER` | 40 | implemented | corpus: `SETTIMER 10000 … GETTIMER x; BRANCH_Z start` replaces the GETTIME time-out idiom; countdown in ms |
| 96 | `GETTIMER` | 21 | implemented | corpus: `GETTIMER 0; BRANCH_NZ skip` staggers unloading; dest = remaining ms (≥ 0), sets flags |
| 97 | `YEAR` | 0 | hooked | host-local civil year minus 1900 (tm_year; tp.exe 0x5565B2 and PPC-clock.md); TPW runtime samples host time |
| 98 | `MONTH` | 0 | hooked | host-local civil month 1..12 (tm_mon + 1; tp.exe 0x556605 and PPC-clock.md); TPW runtime samples host time |
| 99 | `DAY` | 0 | hooked | host-local civil day 1..31 (tm_mday; tp.exe 0x556659 and PPC-clock.md); TPW runtime samples host time |
| 100 | `HOUR` | 1 | hooked | host-local civil hour: tp.exe 0x5566AC calls time/localtime and stores tm_hour in the result register; a variable operand receives it, a literal only sets the flags; the TPW runtime samples host time (PPC-clock.md) |
| 101 | `MIN` | 1 | hooked | host-local civil minute (tm_min), as HOUR: tp.exe 0x5566FF calls time/localtime and stores tm_min in the result register; corpus `MIN 0; BRANCH_NZ` uses a literal dest for flags only |
| 102 | `SEC` | 1 | hooked | host-local civil second (tm_sec), as HOUR: tp.exe 0x55674E calls time/localtime and stores tm_sec in the result register; corpus `SEC 0; BRANCH_NZ` |
| 103 | `SETREVERB` | 21 | hooked | docs (0–10) |
| 104 | `DIPMUSIC` | 8 | hooked | docs (0/1) |
| 105 | `SPARK` | 1 | hooked | unknown upstream; 1 corpus use (Plasma); passed through |

Sources: OpenTPW docs (`src/formats/rsse.md`, `rsse-vm.md`,
`rsse-vm-instructions.md`), the repository's `ScriptDefs`/`RideVariables`
enums, and static analysis of the local original scripts. No original binary
was executed.

### Independent root-fault lifecycle controls

The independent review adds real synthetic coaster/visitor and sound ownership controls: a fault removes that runtime's boarded riders, trains and controller while retaining another runtime's registrations, and stops its actual scream chain while another owner remains audible. Repeated containment/Stop does not release a rider twice. A fault set before the runtime services a tick keeps the existing live VM diagnostic/capture behavior; after containment the existing stopped-fault message is retained and live-sound refusal no longer applies to the cleaned owner. These tests add no persistence schema or native audio/fault claim.
