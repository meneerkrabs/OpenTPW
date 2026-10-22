# RSE VM: executing original ride scripts

October 9, 2026; updated October 14, 2026 (WAITABS deadline, MIN/SEC, child head tables), October 17, 2026 (the hooked opcodes' operands, flags
and time base read from tp.exe, [below](#hooked-opcodes-the-operands-as-the-handlers-read-them)) October 18, 2026 (the sounds the object records hold, [below](#the-sounds-an-addobj-keeps)) and October 22, 2026 (the object type's own sound list, [below](#the-object-types-own-sound-list-type-10)). Status: **every opcode
used by the 308 original scripts has a VM handler**, and all scripts that can be
started run in the VM without faults. The two opcodes only Theme Park Inc
scripts contain — `WAITABS` (45) and `MULT` (48) — also have handlers now
([below](#theme-park-inc-gap-opcodes)).
A bare `RideVM` is VM-level execution: its effects go through a hook that records them as
unimplemented. In a park, every placed original object runs its scripts against the game systems they talk to: the visitors
(`RideVisitorBridge`), the animation channels, the sounds (events, screams, held object records), the particles, the ride-type controllers (TOUR, BUMP, COAST) and
the clock, and the object type's own sound list plays the sounds of ADDOBJ and EVENT type 10 ([below](#the-object-types-own-sound-list-type-10)). BOUNCESETNODE and
BOUNCESETBASE are served by the visitor bridge and the bouncing riders are drawn at their nodes ([below](#bounce-opcodes)).
Container format and static corpus inventory: [RSE-SCRIPTS.md](RSE-SCRIPTS.md).

Code: `source/OpenTPW/VM/` (`RideVM`, `Operand`, `Instruction`, `RideOpcodes`,
`RideScriptEffects`, `RideScriptWorld`, `Handlers/*`). Tests:
`RideVMTests` (synthetic scripts per opcode family) and `RideVMCorpusTests`
(original corpus, inconclusive without `OPENTPW_GAME_PATH`).

## Coverage summary

| | Opcodes | Corpus instructions |
| --- | ---: | ---: |
| Implemented (VM does the whole job) | 45 | 8,053 |
| Hooked (VM handles operands/flags/waits; game effect → `IRideScriptEffects`) | 39 | 3,862 |
| Unknown (no handler) | 0 | 0 |
| **Corpus total** | **84** | **11,915** |

Of the 22 named opcodes the TPW corpus never uses, all 22 have handlers: 4 from the
upstream docs alone (`SETVARINPARENT`, `YEAR`, `MONTH`, `DAY`),
`WAITABS`/`MULT` for the Theme Park Inc corpus
([below](#theme-park-inc-gap-opcodes)), `PUSH`, `POP`, `SETLV`, `DBGMSG` and `END`,
read from tp.exe ([below](#original-interpreter-internals)), and `EVENT_EXT`, the four
ride-light opcodes and `GETCUSTPTCLCODE`, read from tp.exe for script mods ([below](#event_ext-the-ride-lights-and-getcustptclcode)).
Only a number outside the opcode table faults the script. Operand counts of every handler equal the upstream signature
counts (`SETLIGHT`, which the upstream notes give one operand, reads two in tp.exe) and the corpus counts (`CRIT_UNLOCK`: 0).

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
  **strictly** positive (Sign and Zero clear). The flag word (`+0x48`) is also written, as the handlers do (`0x554B0F` tail, `0x552972`, `0x555780`), by GETTIME (the clock), the trigger
  family TRIGANIM, TRIGANIMSPEED, TRIGANIM_CH, TRIGWAITANIM and TRIGWAITANIM_CH (the adjusted out value, at least 300), BOUNCE (1, or 0 with a full table), UNBOUNCE and FORCEUNBOUNCE (the visitor
  handed back), the cross-script variable opcodes (the value; GETREMOTEVAR and FINDSCRIPTRAND clear it first) and the other opcodes the table below marks as setting them.
- Scheduling: in a park the substep scheduler runs the scripts (`RideScriptWorld.RunPass`, one pass per 31 ms substep of the
  park's tick count, DETERMINISM.md "The substep scheduler"): every script's clock gains 31 ms, newest script first, and a script
  runs a slice only when `(id xor pass counter) & 7 == 0` or its TURBO byte is nonzero (tp.exe `0x5516E1`), so an ordinary script
  gets a slice every 248 ms. A standalone VM (tests, tools) keeps `Advance(delta)`: it adds `delta` to the script clock and runs one
  slice per call, as every script did before the scheduler. The slice
  itself follows tp.exe ([below](#original-interpreter-internals)): the
  header's time slice (50 in every script) is a counter that falls by one per
  instruction outside a critical section; `CRIT_LOCK` stops the counting,
  `CRIT_UNLOCK` and `ENDSLICE` zero the counter, a wait ends the slice. The
  budget is what lets busy loops such as Totem's passenger loop
  (`TEST`/`BRANCH` for 10 s, no ENDSLICE) yield. The critical flag `0x87919C` is referenced six times in the whole image (a byte scan of tp.exe): cleared by the two routines that reset the script system's globals (`0x551651`, `0x559836`) and at every slice start (`0x551703`), read once, by the slice loop (`0x551725`), and written by CRIT_LOCK and CRIT_UNLOCK (`0x551D46`, `0x551D5B`), so
  nothing else in the program sees it; a shipped script can even wait inside a lock (bumper.RSE: the wait ends the slice and the next slice starts with the flag clear). A slice that stays locked for
  100,000 instructions faults (`[EXT:crit-slice-guard]`: the original would hang).
- Time unit is the millisecond, read from tp.exe. Every time opcode reads one clock, the global timer `0x785970` (`GETTIME` `0x551F1E`, `WAIT` `0x553783`, `WAITABS`, `SETTIMER` `0x55641B`,
  `GETTIMER` `0x55644D`, `LIMBO`, `BOUNCE`, `WALKON`, the animation waits): `0x402D70` returns the timer's base plus its offset (`0x4031E0`, `0x402F10`), the base is the elapsed
  performance-counter ticks divided by the counter's ticks per millisecond (`0x5F5E80` stores frequency / 1000, `0x5F5F10` divides the difference by it; without a counter the
  millisecond tick count `0x6FD3EC` stands in) multiplied by the timer's scale (`0x4033A0`, accumulated as a double and truncated; `0x403340` steps the scale between two limits).
  So `WAIT 500` is 500 timer milliseconds divided by the script speed (`ftol(operand / speed)` at `0x5537F7`), `SETTIMER` and `GETTIMER` are milliseconds with no speed division (`0x55643A`),
  and `LIMBO` and `BOUNCE` count seconds times 1000. The shipped operands fit: `WAIT 500…5300` around ride animations, `GETTIME; ADD VAR_STARTNOW 10000` passenger time-outs, animation durations fed into `WAIT`.
  Waits resume on the first slice at or after the
  wake time: in a park the slice of an ordinary script comes every 248 ms (the scheduler above), a standalone VM's every 1/60 s tick. `GETTIME` reads the park's shared
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

## Hooked opcodes: the operands as the handlers read them

October 17, 2026. Read from tp.exe: the jump table `0x5567D8` holds one handler per opcode (the interpreter `0x551CB0` fetches the opcode word, each handler fetches its operands in place).
**R** is an operand the handler resolves through `0x5573A0` (a variable's value, or the literal sign-extended from 16 bits); **W** is a raw word the handler tests or stores itself (a destination it
writes only when the word is a variable, `0x40000000` tag; a string operand is tag `0x10000000`). The flag word `+0x48` is what the branches test. The generated table below names the handler of every opcode; the
corpus constraints are `RideOperandCorpusTests`.

| Family | Opcodes (handler) | Operands | What the handler does |
| --- | --- | --- | --- |
| Objects and sounds by key | ADDOBJ (`0x551F5F`), ADDOBJ_EXT (`0x5520E8`), EVENT (`0x552615`), EVENT_EXT (`0x552707`) | `type key id slot` R R R R; EXT forms `type key id control (slot)` | The dispatcher `0x5573D0(script, type, key, id, control)` (control 1000 unless an EXT form gives it): types 1-2 particle effects (PARTICLES.md), 3-9 sounds of the category the type names (AUDIO.md), 10 the object type's own sound list (`0x557686`, [below](#the-object-types-own-sound-list-type-10); no shipped script); a type outside 1-10 logs. Key -1 (a negative key) is the footprint middle, a number a model dummy. ADDOBJ keeps a 0x1C-byte record on the list at +0xB0 (type, handle, key, node of class 0x100 for types 1-2 and 0x200 above, slot) and frees it when the handle is 0 |
| Ending a record | KILLOBJ (`0x5522C5`), FADEOBJ (`0x5523D0`), SETOBJPARAM (`0x5524D9`) | `slot` R; `slot param value` R R R | Every record with the slot: KILLOBJ ends types 1-2 with `0x51FF70(handle, -2)` and 3-10 with `0x51C2C0`, FADEOBJ types 3-10 with `0x51C300` (the stop, unless the library's SoundQuality bit `0x400` is set, [below](#the-sounds-an-addobj-keeps)), both free the record; SETOBJPARAM calls `0x51BC40(handle, param, value)` for types 3-10 and keeps the new handle. None sets flags. Built: `OriginalObjectRuntime.HeldSounds` |
| Animation | TRIGANIM `0x552875`, WAITANIM `0x5529BC`, LOOPANIM `0x552B2F`, TRIGWAITANIM `0x552C1A`, GETANIM `0x552DB4`, TRIGANIMSPEED `0x552E3A`, FLUSHANIM(_CH) `0x552861`/`0x553000`, TRIGANIM_CH `0x55306E`, WAITANIM_CH `0x55320A`, LOOPANIM_CH `0x553399`, TRIGWAITANIM_CH `0x553494`, GETANIM_CH `0x5536AE`, WAIT4ANIM `0x5538EB` | `anim variant` R R (+ `out` W for TRIG*, + `channel` R for the _CH forms, + `speed` R for TRIGANIMSPEED); GETANIM(_CH) `dest` W (+ channel R) | The Mac-read arithmetic (below) with the PC operand kinds; the trigger family also puts its out value in the flag word |
| Visitors, walks | WALKON `0x555963`, WALKOFF `0x555B0C`, WALKGET `0x555B34` | `visitor u1 u2 u3 u4 action u5` R x7; `visitor` R; `dest` W | The script's walk table (TOURS.md): first free entry of the header walk size, states 1-4, WALKGET hands out the first entry in state 4. The shipped WALKON operands: the rider a variable, `action` 1, 2, 4, 5 or 6 (only 2 and 4 act), `u5` always 1 (bit 0: the body's height from the nodes) |
| Visitors, bounce and float | BOUNCESETNODE `0x5555D9`, BOUNCESETBASE `0x555618`, BOUNCE `0x555674`, UNBOUNCE `0x5557A3`, FORCEUNBOUNCE `0x555842`, BOUNCING `0x555910`; WALKST_FLOAT `0x555B72`, WALKFLOATSTAT `0x555BD8`, WALKFLOATSTOP `0x555C11` | `node` W; `base` W; `visitor seconds` R R; `dest` W x3; `seconds a b` R R R; `dest` W | The script's own bounce table (16-byte entries at +0x28: visitor, node, deadline, start) and float words (+0x80, +0x84, +0x88, +0x8A); UNBOUNCE and FORCEUNBOUNCE release only inside the first 200 ms of a second since the bounce, UNBOUNCE also needs the deadline below the clock; the node and base words only feed the render pass |
| Heads | ADDHEAD `0x554C3E`, DELHEAD `0x554D26` | `visitor` R | The script's head table (+0x30): a random free entry (world generator `0x516330`), the head dummy (class 0x80, number entry + 1), `0x44B410` hangs and `0x44B4C0` takes down; child and sound scripts have no table |
| Screams and atmosphere | STARTSCREAM `0x555E5E`, STOPSCREAM `0x555EF7`, SINGLESCREAM `0x555F1B`, SCREAMLEVEL `0x555FDA`, REPAIREFFECT `0x5562A6`, SETREVERB `0x55649C`, DIPMUSIC `0x5564C0`, SPARK `0x5564ED` | `band level` R R; none; `band level` R R; `level` R; `on` R; `level` R; `on` R; `a b c d` R x4 | Scream handle at +0xD0 (`0x551130`, `0x551290`, `0x551320`, `0x551560`; the band is the script's own rider count, `level` 10 to 100, -1 for a one-shot that picks by band alone); REPAIREFFECT switches the type's repair effect (`0x556DE0`) and plays events 111 and 211 for a non-zero value, the band one-shot for 0; SETREVERB stores a dword (+0xD8) that the main loop reads ([below](#dipmusic-and-setreverb-the-sound-service-readers)); DIPMUSIC stores a byte (+0xB9) and writes the global duck request (`0x51E710`); SPARK only logs |
| Controllers | TOUR `0x5542FE`, BUMP `0x5546F5`, COAST `0x554A5A` | `command` W (the raw word, a literal) and `parameter` | Commands 1-18, 1-17 and 1-8 through the tables `0x5569D0`, `0x556A18` and `0x556A5C` (other words log). The 43 shapes (resolved or raw parameter, a variable destination written or required, flags written or kept) equal the Mac-static roles: TOUR 6, 7 and 13 and BUMP 15 log and do nothing |
| Time | WAIT `0x553783`, WAITABS `0x553828`, SETTIMER `0x55641B`, GETTIMER `0x55644D`, GETTIME `0x551F1E`, LIMBO `0x554DE5` | `ms` R; `ms` R; `ms` R; `dest` W; `dest` W; `value seconds` R R | One clock in milliseconds ([above](#execution-model)); WAIT divides by the script speed, the others do not |

What the shipped corpus does with them is pinned in `RideOperandCorpusTests`: the object types it uses are 1 to 9 (EVENT 1 to 6), SETOBJPARAM is always slot 1, parameter 20, value 0 or 75, the trigger out operand is a variable or the
literal 0, the query destinations are variables (or literal 0 / 10000 where only the flags matter), BOUNCE always passes VAR_LETMEON and VAR_DURATION, REPAIREFFECT is 0 or 1, SETREVERB 0 to 10 (0, 1, 3, 8, 9 occur),
DIPMUSIC only 1 (End.RSE and Gates.RSE), SCREAMLEVEL 10 to 100, the scream band a variable (VAR_ONRIDE in 81 of 86 uses) and SPARK has one use.

Still unread or not modelled: the frame steppers of the walking bodies' templates (TOUR-011). SETREVERB's reader and DIPMUSIC's sound-service reader are
built ([below](#dipmusic-and-setreverb-the-sound-service-readers)).

## DIPMUSIC and SETREVERB: the sound-service readers

Both opcodes only store a value in the script; the effect is in the sound service, read in tp.exe.

**DIPMUSIC** (`0x5564C0`) stores the low byte of its resolved operand at +0xB9 and passes the whole operand to `0x51E710`, which keeps its low word as the global duck request (`0x803AD0`)
and marks the sound service's settings changed (`0x803AC8`). The service (`0x51BD70`, run from the main loop and the start-up) then sends four channel settings to the mixer in a fixed order: for the
channel `0x700B88` (the one the second options slider, the music slider, feeds: `0x1D4DA` calls `0x51BB80`, which sets `0x803AC0`, and start-up passes the music default `0x78592C`) it sends
0 while the request word is non-zero (`0x51BE5A`, or `0x51BDC2` in the branch where speech lowers the others) and the music volume otherwise; the effects (`0x803ABC`), speech (`0x803AC4`) and the constant-100 setting (`0x803AB8`, `0x51BB60(100)` at start-up) are
not touched. That channel `0x700B88` is the music one is high confidence: the slider handler `0x4A2E90` case `0x1D4DA` shows the string id `0x142` as its label, and tp.exe's string ids are the 473-entry `UIStrings` numbers plus one from entry 207 on ([LANGUAGES.md](LANGUAGES.md)), so `0x142` is `UIStrings.MusicVolume` (321), while `0x1D4D8` (`0x141`) is the effects slider; the music setter `0x700B88` (`0x6B5A10`) passes channel index 1 and the effects setter `0x700B84` (`0x6B59B0`) index 3, and the speech-ducking branch scales exactly the music and effects pair, as the Mac's speech ducking does. The script's destruction (`0x558500`, `0x558513`) writes the request 0 again when its byte is set, whatever other scripts asked for. `End.RSE` and `Gates.RSE` use it, each at the start of
the end-of-park sequence (8 uses, operand 1, never cleared by the script itself).

OpenTPW: `RideVM.MusicDuckByte` (saved with the script, not hashed), `GameAudio.SetMusicDuck` and `AudioMixer.MusicDuckRequest` (the music channel's gain is 0 while it is non-zero; its voices keep playing, as the original's keep running at volume 0).
The request word is presentation state: it is not saved and not part of any digest; a resumed park asks for it again from the script's byte, and `GameAudio.LeavePark` clears it (the park's scripts are destroyed). `Gates.RSE` runs in OpenTPW (the gate is created with every
park), so its end-of-park command 2 now silences the park music; `End.RSE` is created and dismissed for Instant Action's finale, while Full Simulation's finale around it is not built yet (`APPROX:RIDES-019`, `ParkFinale`).

**SETREVERB** (`0x55649C`) stores its operand as a dword at +0xD8. The reader is `0x55A470`, called by the main loop (`0x54E360`): with the ride camera on a ride (`0x42A4E0`: camera byte `0x790AB0` bit 4,
the ridden object `0x790A44`) it walks the script list for the ride's script with no parent and a non-zero +0xD8; a level of 0 or more goes to `0x51C8D0`, a negative one does nothing; with no such script, or outside
the ride camera, the default `0x78578C` (0) goes to `0x51C8D0`. That call holds the preset (`0x803AB4`, -1 at start) and, only when it changes, sends it to the mixer, which looks up the `SndReverb.map` record
(`0x6C5D20` loads it; `0x6C6000` fetches it, refusing an absent entry) and hands it to the audio device as an EAX 2.0 listener property set (`0x6C46B0`, DSPROPSETID_EAX20_ListenerProperties, property 1, 0x38 bytes) or to
the second backend's vtable slot `0x50` (QMixer.dll exports `QSWaveMixSetReverb*`). The shipped levels are 0, 1, 3, 8 and 9 (`SndReverb.map` holds 15 of 32 entries: records 1 to 8 and 11 to 14 are identical, 0 differs from them in two words, 9 and 10 are their own).

OpenTPW: `RideVM.ReverbLevel`, `ReverbSelector` (called each park frame with the ride view's ride, like the main loop), `GameAudio.SelectReverb`/`ReverbPreset` (sent only on change) and `SndReverbMap` (the file's format). The reverb itself
is OpenTPW's own stand-in by design (`[EXT:audio-reverb]`, user-approved 2026-10-19, `ReverbUnit`, AUDIO.md "Reverb"; `APPROX:AUDIO-039` is retired): the original's DSP is the EAX hardware's or a COM object's behind QMixer.dll, whose exports hold no reverb code.

## EVENT_EXT, the ride lights and GETCUSTPTCLCODE

None of them occurs in a shipped script, but a script mod can use them (the ScriptMods docs), and a fault would stop the mod's script.

- **EVENT_EXT** (`0x552707`, `type key id control`) is EVENT with the fourth operand as the dispatcher's control instead of 1000; the dispatcher applies a control other than 1000 to a particle effect of types 1-2 (`0x51FFD0`)
  and ignores it for the sounds. Built on the EVENT path: the particle request carries the control, the sounds play as for EVENT.
- **ENABLELIGHT / DISABLELIGHT** (`0x555C7A`, `0x555CCD`, `light`) look the model's light dummy up (class `0x20000`, number `light`, `0x44B220`), take its light record (`0x4587B0`) and clear/set its off flag `0x10000000`. **SETLIGHT**
  (`0x555D20`, `light level`, two operands) sets the record's colour to the base colour times `level x 0.01` (`0x4587E0`); **COLOURLIGHT** (`0x555D9A`, `light red green blue`) sets it from the three operands times 0.01 (`0x458890`); both also
  store the packed ARGB word at +0x54. None of the four touches the flags. In the original they are real effects with nothing behind them: no model of the game has a light dummy (a census of the 274 object models of the four
  themes finds none with class `0x20000`), the instance builder `0x44A870` never fills the light pointer of such a dummy, so a script that used them would fault, and no shipped script does. **OpenTPW implements them as an
  extension** (`[EXT:object-lights]`, [LIGHTS.md](LIGHTS.md)): a model of its own (the content kit writes them) can carry light dummies, the opcodes switch, dim (`SETLIGHT`: base colour x level x 0.01) and colour
  (`COLOURLIGHT`: operands x 0.01) the light of the object's dummy number `light`, and the renderer draws it as a point light. A number the model does not have changes nothing and is logged once per object, opcode and
  number: deliberately safer than the original, which would use the missing record. They are not unimplemented effects any more (`OriginalObjectEffects`); a bare `RideVM` without game systems still counts them.
- **GETCUSTPTCLCODE** (`0x5563D5`, `dest in`) resolves its second operand, drops it and stores the constant 0 in the result word and a variable destination: a stub in the original, built as such.

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
cycle), not traced. The Totem's other effects are served too: the channel animations
`totemm2…10`, its screams, its sound records (`ADDOBJ 3 -1 73/74/75/90 2|5`, kept under their slots and ended by KILLOBJ) and its EVENT
instructions play their original sounds (the type routes are traced, AUDIO.md); no Totem effect is an unimplemented one.
With no visitors, an opened Totem waits 10 s, moves once, and repeats.

## The sounds an ADDOBJ keeps

October 18, 2026. ADDOBJ and ADDOBJ_EXT of the sound types 3-9 (334 of the 644 shipped ADDOBJ instructions, in 60 scripts) submit their event exactly as an EVENT of that type does
(the category the type names, the place of the key: a node of the model, or the footprint middle for a negative key, truncated to whole units; `0x5573D0`, `0x5575C7`),
and keep the handle in a record under the slot operand (`0x557970`; a submit with no handle, because the key names no node, the event is missing or no sound system runs, keeps no record).
`OriginalObjectRuntime.HeldSounds` holds the records, newest first, each with its script: the object list is the script's own (`+0xB0`), so a KILLOBJ, FADEOBJ or SETOBJPARAM reaches only the records of the script
that runs it, as in the original (the particle records of types 1-2 are still kept per object).

| Opcode | What it does to the records of its slot | tp.exe |
| --- | --- | --- |
| ADDOBJ, ADDOBJ_EXT (types 3-10; 10 from the object type's own sound list) | starts the sound and keeps it (a repeating event runs as a sentence, a one-shot as a sound that starts when its sample is decoded; both at the key's place, flat without a placement) | `0x551F5F`, `0x5520E8`, `0x557970` |
| (object pass) | every eighth scheduler pass, the one where `(script id xor pass counter) & 7 == 0`, a record with a key >= 0 moves its sound to the key's node again (the position truncated to whole units); a negative key never moves | `0x5516B0` -> `0x551A1C` -> `0x51C270` |
| KILLOBJ | ends the sound at once, no tail, and frees the record (a sound that has played out is no longer found by its handle, so nothing happens) | `0x5522C5` -> `0x51C2C0` -> `0x6B89B0` |
| FADEOBJ | Quality above the loaded threshold releases service-owned tails; otherwise immediate stop. The shared post-scan 100/250 ms phase folds enabled groups into integer own volume at 60 / 100 and stops at zero. Containers stop on their first poll; sample-backed sentences retain decay across segments. Both routes release script records. Managed rounding and host phase remain [`APPROX:AUDIO-040`](proposals/audio-040-plan-soundquality-fade-mode.md). Particles still take `0x51FF70(handle, -2)` as for KILLOBJ. | `0x5523D0` -> `0x51C300` -> `0x6B8930`; update `0x6BCB80` -> `0x6C50D0` -> `0x6C570B..0x6C5737`; poll `0x6B9E30` |
| SETOBJPARAM `slot code value` | the parameter call on the sound: `value` as a byte goes into every slot of the voice's parameter table coded `code` and a match in the sound's own bindings re-evaluates its volume and pitch (the bus engine's code 20 against its event 6, `0 / 75` for idle and driving); particle records are skipped | `0x5524D9` -> `0x51BC40` -> `0x6BBB80` |
| a script's end or removal | END (the program counter goes negative) destroys the script after its slice, and so does the removal of its object; every record it still holds ends | `0x551ABC`, `0x558500` |

Sounds are presentation: no record is in a world-state hash or in an OpenTPW park save. The original's RSSE block does write each script's records raw (type, handle, key, node pointer, slot,
`0x559710`) and its loader links them back without starting a sound (`0x559F44`), so a loaded game holds records whose handles name no voice; the park resume does the same in effect: it holds no record and starts no held sound
(`HeldSoundTests.HeldSoundsAreNotSavedAndAResumedParkStartsNone`), and a looping held ambient does not block a save (a looping EVENT does, `CaptureSnapshot`). Resumed rides therefore stay silent until their script starts the sound again.

## The object type's own sound list (type 10)

October 22, 2026. An object archive may carry a sound category of its own, and the sound types of EVENT, EVENT_EXT, ADDOBJ and ADDOBJ_EXT include 10, which plays from it
(`ObjectTypeSounds`, `ObjectCatalogEntry.OwnSounds`, `ObjectTypeSoundsTests`):

- **Where the list comes from.** The type loader (`0x413C10`) asks `0x41F420` whether the archive holds `cat_rideBANK.map`, `cat_rideSFX.map` and `BankHD.sdt` (the static names
  `0x78C440`, `0x78C248` and `0x78C4E8`, built from `0x747888`, `0x74789C` and `0x7478AC`); only with all three it registers the category `cat_ride` (`0x74787C`) from the archive's folder
  through `0x51B4F0`, the sound library's category loader that every category goes through, and keeps its handle in the type record at `+0x4F0` (`0x4140A6`). The scoped particle templates
  load afterwards (`0x414420`): every `*.emt` member is stored at `+0x4DC + 4 x atoi(the last character of its stem)`, so a template whose stem ends in 5 takes the same slot; OpenTPW then
  plays nothing for type 10 (the scoped templates are not loaded, `APPROX:PTCL-908`). No shipped archive has such a template.
- **How type 10 plays.** The object dispatcher's case 10 (`0x557686`) takes the thing's type (`+0xAC` through `0x7CFB90`, `0x412E90`) and its list (`0x4145E0`, `+0x4F0`). Without one it logs
  `RSSE: Tried to trigger a custom effect with no registered custom sound bank` (`0x765E18`) and returns 0 before the key is looked at. Otherwise everything is as for types 3-9: the key
  resolves with selector `0x200` (`0x556B90`; an unknown key submits nothing), and `0x51BFC0(0, the type's category, id, the place truncated to whole units)` submits the event, so priority,
  range, flags and the voice pool are those of the event in the type's catalogue. The event is looked up in that category only; the game's categories are no fallback, and an id the list
  lacks plays nothing. ADDOBJ keeps the handle under its slot (`0x557970`) like types 3-9, the object pass moves a keyed record (`0x551A1C`) and KILLOBJ, FADEOBJ, SETOBJPARAM and the
  script's end reach it (types 3-10 in each handler).
- **Shipped data.** Of all shipped archives only Jungle's loudspeaker (`features/speaker1`, Info.Id 1417) carries the three members; its `cat_ride` holds the one-shots 2-5 and names its
  bank `Speaker\Bank`. Its script plays its sound with `ADDOBJ 4` (the level's ambient), and no shipped script uses type 10 (`EventKeyCensusTests`), so the list is for mods. The bank is resolved by the
  category loader's folder rule ([AUDIO.md](AUDIO.md), "Bank folders"), which looks for `Speaker\Bank` at `/levels/jungle/features/Speaker/BankHD.sdt`, while the loudspeaker's
  `BankHD.sdt` lies inside its archive. The folder `0x51B4F0` receives comes from the same archive object as the member checks (`esi+0x2C`, its `vf+0x18`, `0x413DDA`/`0x41409A`),
  so under the same rule the original most likely misses the loudspeaker's bank as well: a mismatch in the shipped data, which no shipped script reaches.

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

## Bounce opcodes

BOUNCESETNODE and BOUNCESETBASE configure how the riders of a script's BOUNCE table are drawn; they do not touch the simulation. The visitor bridge keeps their two
values (`RideVisitorBridge.BounceNodeBase`, `BounceBaseHeight`): the handlers `0x5555D9` and `0x555618` store them at script `+0x70` and `+0x6E`, and script creation
(`0x558C45`) starts the node base at 1 and leaves the base height at 0. BOUNCESETNODE stores the raw operand word (a variable operand stores its code word, `0x40000000`
plus the index, not its value). BOUNCESETBASE stores the low 16 bits of a variable's value, or of a literal sign-extended from 16 bits (`0x555653`, `0x555665`; an earlier
reading that a literal is ignored was wrong). The values are part of the saved script struct, so a park save keeps them (only when they differ from the creation values), but
they are presentation state: they stay out of the canonical hash.

Shipped use: all four BOUNCE scripts call BOUNCESETBASE once, before their first BOUNCE (Bouncy in the jungle 8, Brain Buster 6, Bouncy in space 16, Jelly Bounce 12);
only Jelly Bounce calls BOUNCESETNODE (3), the other three keep the node base 1. A slot `n` of the BOUNCE table holds the node `base + n` (`0x555742` records it when the slot is filled).

The only reader is the per-frame script pass `0x557AB0`, which the main loop calls once per rendered frame (the walk update `0x557D80` follows it per script). For every
occupied slot it looks the node up in the ride's model (class `0x800`, `0x44B220`; a slot whose node the model lacks is skipped) and calls the visitor's sprite routine
`0x4F9E60` with the node's position, a height, a heading byte and a template:

- ride running (script variable 7, `VAR_BROKEN`, is 0): template 2 (the run animation, slot 2), heading byte = the time since BOUNCE in half seconds (`& 7` by the sprite pass),
  height = the larger of the node's height and `(visitor mod 7 + 12) * sin(phase) + ride height + base height`. The phase is `ftol((0.8 + 0.005 * speed word) * elapsed ms) mod 1000`
  times 0.003125 radians (0 to 3.125, a hump once per scaled second; the speed word is the script's `SpeedBias`, 50 at creation), looked up in a 4096-entry sine table;
- ride broken: template 3 (standing), the node's position unchanged, heading byte = the visitor mod 7.

OpenTPW: `BounceRiders` (GPU-free rule, `BounceRiderTests`) and `FlatRideBounceRiders` (the park source); `GuestRenderer` draws a Using guest that a bounce slot holds from the
pose instead of skipping it. Approximated: the run animation's frame interval (HEADS-007) and the node taken from the current base (HEADS-008).

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
  when `(script +8 xor pass counter) & 7` is 0 (`0x5516E1`..`0x5516F6`); the
  park's substep scheduler models it (`RideVM.StepSubstep`).
- **TURBO** (opcode 51, `0x5542B9`) stores the low byte of the raw operand word
  (not resolved: a variable operand stores its index byte, `0xFFFF` stores
  `0xFF`) at script +0xB8 and does nothing else. `RideVM.TurboByte` holds it
  for the cadence of the substep scheduler; it is part of the VM snapshot and, once nonzero,
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
- **Walk float** (opcodes 79-81, `WALKST_FLOAT` `0x555B72`, `WALKFLOATSTAT` `0x555BD8`, `WALKFLOATSTOP` `0x555C11`): the script's own state at +0x80 (start on the 32-bit clock),
  +0x84 (duration, 0 = no float) and the words at +0x88 and +0x8A. `WALKST_FLOAT seconds a b` (all three resolved) stores the clock, `seconds x 1000` (a wrapping 32-bit product) and the low
  words of `a` and `b` (the swing of the floating body, read by the render pass `0x5581D4` only); `WALKFLOATSTAT dest` puts the duration word in the flags and a variable destination;
  the walk update of the main loop (`0x558051`..`0x558077`) clears the word once the clock is unsigned past start + duration, which the VM applies when the script looks;
  `WALKFLOATSTOP` with a duration set turns the time spent into thousandths (`(clock x 1000 - start x 1000) / duration`, 32-bit unsigned), moves the start back from now by that
  share and sets the duration to 1000, so the word stays set for the remaining thousandths of a second; with none set it does nothing (no flags). ZeroG (`WALKST_FLOAT VAR_DURATION 1000 1000`, then
  `WALKFLOATSTAT 0; BRANCH_NZ` around `WAITANIM 5`) therefore floats for `VAR_DURATION` seconds, where the effect hook used to answer 0 and ended its float at once. The state is saved with the VM
  (`WalkFloatStart`, `WalkFloatDuration`, `WalkFloatWords`, optional) and hashed only once a float has started. Tests: `RideVMInternalsTests`.
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
- `EVENT_EXT`, the ride lights and `GETCUSTPTCLCODE` follow tp.exe ([below](#event_ext-the-ride-lights-and-getcustptclcode)); no script of either corpus uses them.

## Not original behaviour yet

- The millisecond unit of the time opcodes and the slice cadence are the original's (the unit from the timer `0x785970`, the cadence from the park's
  substep scheduler, above); a standalone VM without a park runs a slice per
  `Advance` call. The slice budget, the critical
  flag, HUSH/HOP, JSR/RETURN/PUSH/POP, the limbo table and TURBO's byte are
  ([below](#original-interpreter-internals)).
- The ride lights (82–85, tp.exe `0x555C7A`–`0x555D9A`) act on the light dummies of an OpenTPW object's own model (`[EXT:object-lights]`, [LIGHTS.md](LIGHTS.md)); a missing light is a logged no-op, never a fault.
- Visitor opcodes work for every placed original object (its `RideVisitorBridge`);
  riders are drawn walking along the ride nodes and standing, cheering or floating on board as the walk update draws them (the walk table, its times and its bodies, [TOURS.md](TOURS.md), TOUR-011 narrowed), and a bounce
  rider is drawn at its bounce node. The TOUR, BUMP and COAST commands go to their controllers ([TOURS.md](TOURS.md), [TRACKS.md](TRACKS.md), [COASTERS.md](COASTERS.md)).
  WALKST_FLOAT, WALKFLOATSTAT and WALKFLOATSTOP are the script's own state in the original and in the VM.
- Animation playback only through the objects' effects (OBJECTS.md; rigid node
  tracks, vertex animation not decoded). EVENT types 1-2 and ADDOBJ types 1-2 start particles (PARTICLES.md), EVENT types 3-10, ADDOBJ types 3-10 and REPAIREFFECT play
  their original sounds (the category routes are traced, AUDIO.md; type 10 from the object type's own sound list); the pair a non-zero
  REPAIREFFECT plays uses the same sampled placed sound-space origin and owned-loop cleanup as EVENT
  types 3–9, and the value 0 plays the band one-shot of the cat_kids 75 family; the screams (STARTSCREAM, STOPSCREAM, SCREAMLEVEL, SINGLESCREAM)
  are served too. Type 0 is unsupported, not a positioned sound loop. TPW YEAR/MONTH/DAY/HOUR/MIN/SEC
  sample host civil time (an unsaved external input, `[EXT:vm-host-time]`); they do not read the park calendar.
- What the engine does with `EventMap.rse` variables is still unread; the host protocol of `VAR_LETMEON`/`VAR_LETMEOFF` is [GUESTS.md](GUESTS.md)'s; the `0xFFFF` second operand of EVENT is the key -1,
  the footprint middle (`0x556B90`, [above](#hooked-opcodes-the-operands-as-the-handlers-read-them)).
- Child scripts in the original share the one timer (every time opcode reads `0x785970`) and draw RAND and FINDSCRIPTRAND from the world generator (`0x516330`); in a park OpenTPW's children share the park's
  scheduler clock and the world generator too (DET-013), a standalone VM keeps its own seed.

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
| 7 | `GETTIME` | 172 | implemented | TPW-EXE 0x551F1E (`dest`, raw): the clock 0x785970 (0x402D70), in milliseconds, stored unchanged into the flags and a variable destination; STP-PPC 0xaf6d8: the shared scheduler clock's 32-bit millisecond word (no creation timestamp subtracted) |
| 8 | `ADDOBJ` | 644 | hooked | TPW-EXE 0x551F5F (`type key id slot`, each resolved: a variable's value or the literal sign-extended from 16 bits): one 0x1C-byte record on the script's object list (+0xB0, newest first) keyed by the slot; the dispatcher 0x5573D0(type, key, id, control 1000) via 0x557970 gives its handle, a zero handle frees the record again; types 1-2 are particle effects, 3-9 sounds by category, 10 the object type's own sound list; a negative key (the corpus's 0xFFFF) is the footprint middle |
| 9 | `ADDOBJ_EXT` | 0 | hooked | TPW-EXE 0x5520E8 (`type key id control slot`, each resolved): ADDOBJ with the control operand in place of 1000; no corpus use |
| 10 | `KILLOBJ` | 235 | hooked | TPW-EXE 0x5522C5 (`slot`, resolved): every record with the slot ends and is freed: types 1-2 through 0x51FF70(handle, -2), 3-10 through 0x51C2C0, any other type logs; flags untouched (`ADD VAR_COUNT 0xFFFF; KILLOBJ 200; BRANCH_PV`) |
| 11 | `FADEOBJ` | 113 | hooked | TPW-EXE 0x5523D0 (`slot`, resolved): as KILLOBJ, but types 3-10 end through 0x51C300 (types 1-2 take the same 0x51FF70(handle, -2)); flags untouched |
| 12 | `SETOBJPARAM` | 20 | hooked | TPW-EXE 0x5524D9 (`slot param value`, each resolved): for every record with the slot, types 3-10 call 0x51BC40(handle, param, value) and keep the handle it returns, types 1-2 do nothing; the corpus sets param 20 to 0 or 75 on slot 1 |
| 13 | `EVENT` | 527 | hooked | TPW-EXE 0x552615 (`type key id`, each resolved): the dispatcher 0x5573D0(type, key, id, control 1000), its handle dropped; types 1-2 are particle effects (PARTICLES.md), 3-9 sounds by category (AUDIO.md), 10 the object type's own sound list (0x557686); the corpus uses types 1-6 and key 0xFFFF (-1, the footprint middle) 341 times |
| 14 | `EVENT_EXT` | 0 | hooked | TPW-EXE 0x552707 (`type key id control`, each resolved): EVENT with the control operand in place of 1000 (the dispatcher 0x5573D0 applies a control other than 1000 to a particle effect of types 1-2 through 0x51FFD0; the sound types ignore it); no corpus use |
| 15 | `FLUSHANIM` | 17 | hooked | TPW-EXE 0x552861 (no operands); STP-PPC 0xafa68 → 0xa6c90: clears only channel 0's deferred clip; the playing clip and the WAIT4ANIM deadline stay |
| 16 | `TRIGANIM` | 74 | hooked | TPW-EXE 0x552875 (`anim variant out`: two resolved, the third raw, written when it is a variable); STP-PPC 0xafa94: trigger channel 0, flags 0; out = max(ret - 300, 300); WAIT4ANIM deadline = now + out / script speed; LOOPANIM token reset |
| 17 | `WAITANIM` | 547 | hooked | TPW-EXE 0x5529BC (`anim variant`, resolved); STP-PPC 0xafbbc: trigger channel 0, flags 0; waits max(unsigned(ret - 300) / script speed, 300) (a clip under 300 ms saturates to INT32_MAX ms) on a u32 deadline; zeroes the WAIT4ANIM deadline; LOOPANIM token reset |
| 18 | `LOOPANIM` | 210 | hooked | TPW-EXE 0x552B2F (`anim variant`, resolved); STP-PPC 0xafd00: no-op while token anim + (sub << 16) equals script +168; else loops on channel 0 (flags 1), stores the token, zeroes the WAIT4ANIM deadline |
| 19 | `TRIGWAITANIM` | 133 | hooked | TPW-EXE 0x552C1A (`anim variant out`, as TRIGANIM); STP-PPC 0xafd94: as TRIGANIM, then re-executes until channel 0's AnimID equals the triggered one (+188 = id + 1), yielding while it differs |
| 20 | `GETANIM` | 0 | hooked | TPW-EXE 0x552DB4 (`dest`, raw); STP-PPC 0xaff3c: channel 0's AnimID (12 = none), -1 once its non-looping clip ended (channel flag 0x4); result sets flags |
| 21 | `TRIGANIMSPEED` | 4 | hooked | TPW-EXE 0x552E3A (`anim variant out speed`: three resolved, the third raw); STP-PPC 0xaffc4: trigger channel 0 at script speed x operand / 1000; out = max(ret - 300, 300); deadline = now + out x 1000 / operand (signed divw); LOOPANIM token reset |
| 22 | `FLUSHANIM_CH` | 0 | hooked | TPW-EXE 0x553000 (`channel`, resolved); STP-PPC 0xb00f4 → 0xa6c90: clears only the operand channel's deferred clip |
| 23 | `TRIGANIM_CH` | 63 | hooked | TPW-EXE 0x55306E (`anim variant out channel`); STP-PPC 0xb013c: as TRIGANIM on channel operand 4 (Totem `TRIGANIM_CH 5 1 0 1`); leaves the LOOPANIM token |
| 24 | `WAITANIM_CH` | 0 | hooked | TPW-EXE 0x55320A (`anim variant channel`), STP-PPC 0xb0268 (same arithmetic): as WAITANIM on channel operand 3 without the speed division or fctiwz: unsigned max(ret - 300, 300) added to the clock as a wrapping u32 (0xb035c), so a clip under 300 ms gives a deadline in the past and the script continues next slice; zeroes the deadline |
| 25 | `LOOPANIM_CH` | 1 | hooked | TPW-EXE 0x553399 (`anim variant channel`); STP-PPC 0xb037c: loops on channel operand 3 (flags 1) on every call, no token check; zeroes the deadline |
| 26 | `TRIGWAITANIM_CH` | 0 | hooked | TPW-EXE 0x553494 (`anim variant out channel`); STP-PPC 0xb0404: as TRIGWAITANIM on channel operand 4; leaves the LOOPANIM token |
| 27 | `GETANIM_CH` | 15 | hooked | TPW-EXE 0x5536AE (`dest channel`: the first raw, the second resolved); STP-PPC 0xb05b8: the channel's AnimID, -1 once ended; result sets flags (corpus `GETANIM_CH 0 n; BRANCH_PV x; BRANCH_Z x`) |
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
| 46 | `WAIT4ANIM` | 170 | hooked | TPW-EXE 0x5538EB (no operands); STP-PPC 0xb07a0: deadline 0 continues; otherwise blocks while now < deadline (re-executing), then clears it |
| 47 | `ADD` | 541 | implemented | docs (dest += value, sets flags); corpus: 115 branches consume ADD flags, e.g. `ADD VAR_SPACELEFT 0xFFFF; BRANCH_Z` |
| 48 | `MULT` | 0 | implemented | TPW-EXE 0x553eb9: dest-first, a × b by imul (32-bit wrap), result in the flag word and a variable destination; Theme Park Inc corpus: `MULT VAR_TIMER VAR_TIMEOUT VAR_SCALE` |
| 49 | `DIV` | 4 | implemented | corpus: `DIV dest a b`; Mac-static: signed truncation, zero divisor returns 0; signed overflow retains VM wrap policy |
| 50 | `MOD` | 5 | implemented | corpus: `MOD dest a b`; Mac-static: signed remainder, zero divisor returns 0; signed overflow retains VM zero policy |
| 51 | `TURBO` | 20 | implemented | TPW-EXE 0x5542B9: stores the low byte of the raw operand word in the script's TURBO byte (a variable operand stores its index byte); nothing else |
| 52 | `END` | 0 | implemented | TPW-EXE 0x5567B1 (no operands): sets the program counter to -10000, which ends the slice and the script (destroyed at the end of the pass); no corpus use |
| 53 | `TOUR` | 76 | hooked | TPW-EXE 0x5542FE (`command parameter`: the command is the raw word, 1-18 through the table 0x5569D0, others log): the 43 command shapes of TOUR, BUMP and COAST (flags written or kept, a variable destination written or required, the resolved parameter passed or dropped) were read from the PC handlers and equal the Mac-static command roles: mutators preserve flags; supported queries set flags/output; required-variable gates skip invalid calls |
| 54 | `BUMP` | 199 | hooked | TPW-EXE 0x5546F5 (`command parameter`, commands 1-17 through the table 0x556A18; 15 logs): shapes equal the Mac-static command roles (see TOUR): mutators preserve flags; queries set flags/output; commands 13 and 14 set the flags from their own parameter |
| 55 | `COAST` | 144 | hooked | TPW-EXE 0x554A5A (`command parameter`, commands 1-8 through the table 0x556A5C): shapes equal the Mac-static command roles (see TOUR): commands 1/4/5/6/8 preserve flags; 2/3 set flags and an optional variable output; 7 does nothing |
| 56 | `ADDHEAD` | 31 | hooked | TPW-EXE 0x554C3E (`visitor`, resolved): needs the script's head table (+0x30, size at +0x4C; none for child and sound scripts) with a free entry, draws entries with the world generator (0x554CB5) until it hits a free one, stores the visitor, finds the head dummy (class 0x80, number entry + 1, 0x44B220) and hangs the head there (0x44B410); no result |
| 57 | `DELHEAD` | 31 | hooked | TPW-EXE 0x554D26 (`visitor`, resolved): finds the visitor in the head table, takes its head down (0x44B4C0, dummy of class 0x80 numbered entry + 1) and frees the entry; no table or no such visitor: nothing |
| 58 | `LIMBO` | 24 | implemented | TPW-EXE 0x554DE5 (`value seconds`): the first free slot of the script's limbo table (header limbo size) takes the value and expiry now + seconds * 1000; result 1 (flags), 0 when the table is full |
| 59 | `UNLIMBO` | 24 | implemented | TPW-EXE 0x554EF0: releases the first occupied slot whose expiry is strictly past into the flags and a variable destination, else 0 (`UNLIMBO VAR_LETMEOFF; BRANCH_Z`) |
| 60 | `FORCEUNLIMBO` | 23 | implemented | TPW-EXE 0x554F74: variable destination only (anything else does nothing at all); releases the first occupied slot whatever its expiry, else 0 (`FORCEUNLIMBO VAR_LETMEOFF; BRANCH_NZ loop`) |
| 61 | `INLIMBO` | 6 | implemented | TPW-EXE 0x555012: the occupied slot count into the flags and the destination (`INLIMBO 0; BRANCH_Z`) |
| 62 | `LIMBOSPACE` | 24 | implemented | TPW-EXE 0x555045: limbo size minus the occupied count into the flags and the destination (`LIMBOSPACE 0; BRANCH_Z full`) |
| 63 | `SPAWNCHILD` | 20 | implemented | docs (one child); corpus: all 20 names resolve to an RSE in the same WAD; the child runs after its parent each tick |
| 64 | `SPAWNSOUND` | 28 | implemented | docs; corpus: always `EventMap.rse`, which only fills VAR_EVT0…9/VAR_PAR0 and idles; runs in its own slot (bumper.RSE has both). What the engine reads from it is not implemented |
| 65 | `REMOVECHILD` | 4 | implemented | docs |
| 66 | `SETVARINCHILD` | 7 | implemented | TPW-EXE 0x555282 (`variable value`, resolved): writes the child's variable (the script id at +0xC; none, or an index at or above its variable count: nothing, no flags) and sets the flags to the value |
| 67 | `GETVARINCHILD` | 9 | implemented | TPW-EXE 0x555349 (`dest variable`: the destination raw, the variable resolved): reads the child's variable into the flags and a variable destination (a literal destination, no child or an index at or above its variable count: nothing, no flags) |
| 68 | `SETVARINPARENT` | 0 | implemented | TPW-EXE 0x555402 (`variable value`, resolved): SETVARINCHILD for the parent (the script id at +0x10); no corpus use |
| 69 | `GETVARINPARENT` | 10 | implemented | TPW-EXE 0x5554F2 (`dest variable`): GETVARINCHILD for the parent (the script id at +0x10) |
| 70 | `BOUNCESETNODE` | 1 | hooked | TPW-EXE 0x5555D9 (`node`, raw): stores the raw operand word at +0x70, the base of the node number each BOUNCE entry records (+0x70 + entry index); only the per-frame script pass 0x557AB0 reads the nodes (a bouncing rider is drawn on the node of its slot): no effect on the simulation |
| 71 | `BOUNCESETBASE` | 4 | hooked | TPW-EXE 0x555618 (`base`): a variable operand's value or a literal sign-extended from 16 bits is stored as its low 16 bits at +0x6E, the base height added under the bouncing riders; read by the per-frame script pass 0x557AB0 only (the bounce height): no effect on the simulation |
| 72 | `BOUNCE` | 4 | hooked | TPW-EXE 0x555674 (`visitor seconds`, resolved): the first free entry (visitor word 0) of the script's bounce table (+0x28, 16-byte entries, header bounce size at +0x64) takes the visitor, the node +0x70 + index, the deadline clock + seconds x 1000 and the start clock; the count at +0x6C goes up; result 1 in the flags, 0 with a full table (nothing stored); STP-PPC 0xadf40 |
| 73 | `UNBOUNCE` | 4 | hooked | TPW-EXE 0x5557A3 (`dest`, raw): the first entry whose deadline is below the clock (signed) and whose time since the bounce is inside the first 200 ms of a second (the remainder of elapsed / 1000, divided by 200, is 0) is freed (count - 1) and its visitor goes to the flags and a variable destination; none: 0; STP-PPC 0xae020 |
| 74 | `FORCEUNBOUNCE` | 8 | hooked | TPW-EXE 0x555842 (`dest`, raw): UNBOUNCE without the deadline test: the first entry inside the 200 ms window of its second is freed and handed back, none: 0 |
| 75 | `BOUNCING` | 25 | hooked | TPW-EXE 0x555910 (`dest`, raw): the count at +0x6C (a signed word) into the flags and a variable destination (corpus `BOUNCING VAR_RUNNING; BRANCH_Z`) |
| 76 | `WALKON` | 47 | hooked | TPW-EXE 0x555963 (`visitor u1 u2 u3 u4 action u5`, each resolved): the walk table entry of 0x556F40 (first free of the header walk size; state 1 for ftol(distance u1 to u2) x 100 ms; the main loop's walk update 0x557D80 moves it on: state 2 hangs a head action 4 head on head id u2 and, for action 2, creates effect 0x13 at u2); u5 bit 0 takes the walking body's height from the nodes (all 47 uses: 1); see TOURS.md |
| 77 | `WALKOFF` | 53 | hooked | TPW-EXE 0x555B0C (`visitor`, resolved) → 0x5571A0: finds the visitor's entry (none: logs) and sets state 3 with the end now + ftol(distance u3 to u4) x 100 ms; a head up (head action 4, state 2) comes down; action 2 creates effect 0x13 at u3 |
| 78 | `WALKGET` | 43 | hooked | TPW-EXE 0x555B34 (`dest`, raw) → 0x557110: the first entry of the table in state 4 gives its visitor and is freed; the visitor goes to the flags and a variable destination, 0 when none (corpus `WALKGET VAR_LETMEOFF; BRANCH_Z wait`) |
| 79 | `WALKST_FLOAT` | 1 | implemented | TPW-EXE 0x555B72 (`seconds a b`, resolved): starts the script's float: start = clock, duration = seconds x 1000 ms, a and b kept as words (the body's swing, render only); no effect; 1 corpus use (ZeroG: `WALKST_FLOAT VAR_DURATION 1000 1000`) |
| 80 | `WALKFLOATSTAT` | 1 | implemented | TPW-EXE 0x555BD8: the float's duration word (0 once start + duration has passed) into the flags and a variable destination; ZeroG loops `WALKFLOATSTAT 0; BRANCH_NZ` for VAR_DURATION seconds |
| 81 | `WALKFLOATSTOP` | 1 | implemented | TPW-EXE 0x555C11: with a float running, start = clock - the share spent (thousandths of the duration) and duration = 1000 (the float settles over the rest); no operands, no flags, no effect |
| 82 | `ENABLELIGHT` | 0 | hooked | TPW-EXE 0x555C7A (`light`, resolved): looks the light dummy (class 0x20000, number `light`) up in the object's model (0x44B220), takes its light record (0x4587B0) and clears the record's off flag 0x10000000 (0x458910); flags untouched; no corpus use, no light in any shipped model; OpenTPW switches the light of the object's own light dummy, and a missing light is a logged no-op (EXT:object-lights, docs/LIGHTS.md) |
| 83 | `DISABLELIGHT` | 0 | hooked | TPW-EXE 0x555CCD (`light`, resolved): as ENABLELIGHT but sets the record's off flag 0x10000000 (0x458940); flags untouched; no corpus use; OpenTPW: EXT:object-lights |
| 84 | `SETLIGHT` | 0 | hooked | TPW-EXE 0x555D20 (`light level`, resolved): the light record's colour becomes the light's base colour times level x 0.01 (0x4587E0: red, green and blue, alpha 1.0, and the packed ARGB word); flags untouched; no corpus use; OpenTPW: EXT:object-lights |
| 85 | `COLOURLIGHT` | 0 | hooked | TPW-EXE 0x555D9A (`light red green blue`, resolved): each channel times 0.01 becomes the light record's colour (alpha 1.0, packed ARGB word with each channel times 255 truncated, 0x458890); flags untouched; no corpus use; OpenTPW: EXT:object-lights |
| 86 | `STARTSCREAM` | 40 | hooked | TPW-EXE 0x555E5E (`band level`, resolved): 0x551130(handle +0xD0, band, level, script speed word, object position in whole units) gives the handle (kept at +0xD0 and in the flags); band 0 plays nothing, a start while a handle is held is refused; all 40 corpus uses pass VAR_ONRIDE as the band |
| 87 | `STOPSCREAM` | 80 | hooked | TPW-EXE 0x555EF7 (no operands): with a handle (+0xD0) calls 0x51C300(handle), then clears the handle |
| 88 | `SINGLESCREAM` | 46 | hooked | TPW-EXE 0x555F1B (`band level`, resolved): a one-shot at the object's position (0x556AF0): level below 0 (the corpus's 0xFFFF, 44 of 46 uses) calls 0x551560, else 0x551320(band, level, script speed word); no handle kept |
| 89 | `SCREAMLEVEL` | 81 | hooked | TPW-EXE 0x555FDA (`level`, resolved): 0x551290(handle +0xD0, level, script speed word) sets the held voice's parameter 6 to clamp((level + speed) / 2, 0, 100), truncating toward zero, and does nothing without a handle (built; 10-100 occur) |
| 90 | `FINDSCRIPTRAND` | 5 | implemented | TPW-EXE 0x55601A (`name dest`: a string operand, the destination raw): a random live script with that NAME (one world draw when any matches) gives its id to the flags and a variable destination; with no match the flags are 0 and the destination keeps its value (corpus: `FINDSCRIPTRAND "Zob Upgrade" VAR_SCRIPTID; BRANCH_Z none`) |
| 91 | `GETREMOTEVAR` | 2 | implemented | TPW-EXE 0x55617F (`dest script variable`: the destination raw, the others resolved): the flags are cleared, then the other script's variable goes to the flags and a variable destination; an unknown script id or an index outside its variables leaves the destination as it was (corpus: `GETREMOTEVAR 0 VAR_SCRIPTID 0; BRANCH_Z`) |
| 92 | `SETREMOTEVAR` | 10 | implemented | TPW-EXE 0x556216 (`script variable value`, resolved): writes the other script's variable and sets the flags to the value; an unknown script id (bus.RSE writes to the FINDSCRIPTRAND result without checking it) or an index outside its variables does nothing |
| 93 | `REPAIREFFECT` | 140 | hooked | TPW-EXE 0x5562A6 (`on`, resolved): 0x556DE0(on) switches the type's repair effect (non-zero starts it, 0 ends it); then, from the compare at 0x5562C5, a non-zero value plays events 0x6F and 0xD3 of the global ride category at the object and 0 jumps to 0x55635F: with more than five variables 0x551320(variable 5, 1, 1, position), a band one-shot (silent for band 0) |
| 94 | `GETCUSTPTCLCODE` | 0 | implemented | TPW-EXE 0x5563D5 (`dest in`): the second operand is resolved and dropped, the result is the constant 0 in the flags and a variable destination (a stub in the original); no corpus use |
| 95 | `SETTIMER` | 40 | implemented | corpus: `SETTIMER 10000 … GETTIMER x; BRANCH_Z start` replaces the GETTIME time-out idiom; countdown in ms |
| 96 | `GETTIMER` | 21 | implemented | corpus: `GETTIMER 0; BRANCH_NZ skip` staggers unloading; dest = remaining ms (≥ 0), sets flags |
| 97 | `YEAR` | 0 | hooked | host-local civil year minus 1900 (tm_year; tp.exe 0x5565B2 and PPC-clock.md); TPW runtime samples host time |
| 98 | `MONTH` | 0 | hooked | host-local civil month 1..12 (tm_mon + 1; tp.exe 0x556605 and PPC-clock.md); TPW runtime samples host time |
| 99 | `DAY` | 0 | hooked | host-local civil day 1..31 (tm_mday; tp.exe 0x556659 and PPC-clock.md); TPW runtime samples host time |
| 100 | `HOUR` | 1 | hooked | host-local civil hour: tp.exe 0x5566AC calls time/localtime and stores tm_hour in the result register; a variable operand receives it, a literal only sets the flags; the TPW runtime samples host time (PPC-clock.md) |
| 101 | `MIN` | 1 | hooked | host-local civil minute (tm_min), as HOUR: tp.exe 0x5566FF calls time/localtime and stores tm_min in the result register; corpus `MIN 0; BRANCH_NZ` uses a literal dest for flags only |
| 102 | `SEC` | 1 | hooked | host-local civil second (tm_sec), as HOUR: tp.exe 0x55674E calls time/localtime and stores tm_sec in the result register; corpus `SEC 0; BRANCH_NZ` |
| 103 | `SETREVERB` | 21 | hooked | TPW-EXE 0x55649C (`level`, resolved): stores the operand as a dword at +0xD8 and nothing else; the reader is the main loop's 0x55A470, which sets the sound service's reverb preset (0x51C8D0, an index into SndReverb.map) from the root script of the ride being ridden whose level is not 0 (built: ReverbSelector; the mixed reverb is OpenTPW's stand-in by design, EXT:audio-reverb); the corpus uses 0, 1, 3, 8 and 9 |
| 104 | `DIPMUSIC` | 8 | hooked | TPW-EXE 0x5564C0 (`on`, resolved): stores the low byte at +0xB9 and calls 0x51E710(on), which writes the global duck request word 0x803AD0 (and the sound service's dirty flag 0x803AC8); the sound service (0x51BD70, from the main loop) then sets the music channel's volume to 0 while the word is non-zero (0x51BE5A..0x51BE9B, 0x51BDC2..0x51BDEC; built: AudioMixer.MusicDuckRequest); the script's destruction writes 0 again when the byte was set (0x558513); End.RSE and Gates.RSE use it (8 uses) |
| 105 | `SPARK` | 1 | implemented | TPW-EXE 0x5564ED (`a b c d`, resolved): stores a and b as words at +0xDC and +0xDE, looks the two key nodes (class 0x100) up and logs them; c and d are dropped; nothing reads the words, so no game effect (1 corpus use, Plasma) |

Sources: OpenTPW docs (`src/formats/rsse.md`, `rsse-vm.md`,
`rsse-vm-instructions.md`), the repository's `ScriptDefs`/`RideVariables`
enums, and static analysis of the local original scripts. No original binary
was executed.

### Independent root-fault lifecycle controls

The independent review adds real synthetic coaster/visitor and sound ownership controls: a fault removes that runtime's boarded riders, trains and controller while retaining another runtime's registrations, and stops its actual scream chain while another owner remains audible. Repeated containment/Stop does not release a rider twice. A fault set before the runtime services a tick keeps the existing live VM diagnostic/capture behavior; after containment the existing stopped-fault message is retained and live-sound refusal no longer applies to the cleaned owner. These tests add no persistence schema or native audio/fault claim.
