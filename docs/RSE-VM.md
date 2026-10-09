# RSE VM: executing original ride scripts

October 9, 2026. Status: **every opcode used by the 308 original scripts has a
VM handler**, and all scripts that can be started run in the VM without faults.
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
| Implemented (VM does the whole job) | 33 | 7,850 |
| Hooked (VM handles operands/flags/waits; game effect → `IRideScriptEffects`) | 51 | 4,065 |
| Unknown (no handler) | 0 | 0 |
| **Corpus total** | **84** | **11,915** |

Of the 22 named opcodes the corpus never uses, 4 have handlers from the
upstream docs alone (`SETVARINPARENT`, `YEAR`, `MONTH`, `DAY`); the other 18
stay unknown and fault the script if executed. Operand counts of every handler
equal the upstream signature counts and the corpus counts (`CRIT_UNLOCK`: 0).

"Implemented" means the semantics are documented upstream and/or forced by
corpus control flow; none is verified against the original executable or
runtime traces (no original binary was run).

## Execution model

- `RideVM` takes a parsed `RideScriptFile`: variables are a zeroed `int[]` of the
  declared size (the old constructor left the list empty and threw on the
  first write), strings come from the blob, and branch operands — code-word
  indices — are resolved to instruction indices at load time.
- Literals are sign-extended 16-bit values. `0xFFFF` is the only literal
  ≥ `0x8000` (918 uses) and must be −1: `ADD VAR_SPACELEFT 0xFFFF; BRANCH_Z`
  and `ADD VAR_COUNT 0xFFFF; BRANCH_PV loop` are count-downs.
- Writes to a literal operand are discarded. The corpus uses a literal `0`
  destination when only the flags matter (`MOD 0 VAR_CAPACITY 9; BRANCH_Z`,
  `RAND 0 1; BRANCH_Z`, `MIN 0; BRANCH_NZ`).
- Flags: Zero = result 0, Sign = result negative. Only opcodes whose result a
  corpus branch consumes (or the docs say set flags) update them; COPY, WAIT,
  ENDSLICE and KILLOBJ demonstrably leave them alone. `BRANCH_PV` is
  **strictly** positive (Sign and Zero clear).
- Scheduling (**assumption**, not verified): each `Advance(delta)` — one
  `FixedStepClock` tick (60 Hz) in the game — adds `delta` to the script clock
  and runs one slice. A slice ends at ENDSLICE, at a wait, or after the
  header's time slice (50 in every script) instructions outside a
  `CRIT_LOCK…CRIT_UNLOCK` section. That budget is what lets busy loops such as
  Totem's passenger loop (`TEST`/`BRANCH` for 10 s, no ENDSLICE) yield; a slice
  that stays locked for 100,000 instructions faults.
- Time unit is the millisecond (**inferred**: `WAIT 500…5300` around ride
  animations, `GETTIME; ADD VAR_STARTNOW 10000` passenger time-outs, animation
  durations fed into `WAIT`). Waits resume on the first tick at or after the
  wake time, so they are quantised to 1/60 s. `GETTIME` is milliseconds since
  the VM was created.
- `JSR`/`RETURN` use a LIFO stack limited to the header stack size (every
  script that uses JSR declares ≥ 3); the old VM used a FIFO queue.
- Children: `SPAWNCHILD` and `SPAWNSOUND` load a script from the parent's
  archive through `RideVMOptions.ResolveScript` and run it after the parent
  each tick, in separate slots (bumper.RSE uses both). Variable IDs in
  `SET/GETVARINCHILD`/`GETVARINPARENT` are variable indices of the other script.
- `RideScriptWorld` holds the live scripts for `FINDSCRIPTRAND` (by `NAME`),
  `GETREMOTEVAR` and `SETREMOTEVAR`. A missing script (ID 0) reads 0 and ignores
  writes, because bus.RSE writes to its `FINDSCRIPTRAND "Traffic Lights"`
  result without checking it.
- Faults (script stops, `FaultMessage` set, logged): unknown opcode, RETURN
  without JSR, stack overflow, ÷0, wrong operand kind, missing child script,
  child/parent variable index out of range, missing child or parent.

## Corrections to the upstream instruction notes

| Opcode | Upstream | Corpus evidence |
| --- | --- | --- |
| SUB, DIV, MOD | `<a> <b> <dest>` | destination first: `SUB VAR_TEMP VAR_TEMP 3000; WAIT VAR_TEMP`, `SUB VAR_TEMP VAR_STARTNOW VAR_TEMP; BRANCH_NV start`, `DIV VAR_TEMP VAR_CAPACITY 9` + `MOD 0 VAR_CAPACITY 9; BRANCH_Z; ADD VAR_TEMP 1` (ceiling) |
| CMP | bitwise AND | subtraction: `RAND VAR_TEMP 10; CMP VAR_TEMP 4; BRANCH_PV; BRANCH_NZ` only makes sense as `a − b` |
| BRANCH_PV | "positive" | excludes zero: 15× `BRANCH_PV x; BRANCH_Z x` |
| RAND | `0..max` | inclusive: Totem's `RAND VAR_TEMP 2` has three outcomes; sets flags |
| UNLIMBO, FORCEUNLIMBO | visitor input | output + flags: `UNLIMBO VAR_LETMEOFF; BRANCH_Z none; ADD VAR_ONRIDE 0xFFFF` |
| TRIGANIM | unknown operands | `animation variant duration-out`: `TRIGANIM 5 0 VAR_TEMP; SUB VAR_TEMP VAR_TEMP 3000; WAIT VAR_TEMP; …; WAIT4ANIM` |
| SETTIMER / GETTIMER | unknown | countdown in ms; `GETTIMER dest` = remaining time, sets flags |

## Effects hook

Every hooked opcode calls `IRideScriptEffects.Perform(RideEffectCall)` with the
resolved operand values. The VM writes the returned value to the opcode's
destination where the corpus shows one (HOP, WALKGET, UNLIMBO, LIMBOSPACE,
GETANIM_CH, HOUR, …), sets flags where branches consume them, and uses it as
the duration in milliseconds for TRIGANIM/WAITANIM/TRIGWAITANIM/TRIGANIMSPEED
(waits and WAIT4ANIM). For TOUR/BUMP/COAST the direction of the second operand
depends on the command, so the effect writes it via `SetOutput`.

The default, `UnimplementedRideScriptEffects`, returns 0 (no visitor, no
animation time), counts the call in `RideVM.UnimplementedEffects` and logs
"unimplemented effect" once per script and opcode. Nothing is faked.

## Corpus run (`RideVMCorpusTests`)

Roots are the 263 scripts not started by another script; 44 are children (28
`EventMap.rse`, 16 `SPAWNCHILD` targets) and one, `space/rides/creature.wad/
CreatureFX.RSE`, is an orphan copy of hoverbot's "Driod Anim Child" that reads
its parent but is never spawned. All 263 roots run in one shared world for
3,600 ticks (60 s), twice:

1. default effects, `VAR_CAPACITY` 6, ride open: 23.8 M instructions, no
   faults, no halts, 216 running/47 waiting at the end; 34 hooked opcodes
   reached (e.g. WAITANIM in 229 scripts, ADDOBJ 109, LOOPANIM 105, EVENT 48);
2. "chaos" effects (seeded random results and animation durations, random
   pokes of LETMEON/LETMEOFF/RIDECLOSED/BREAKSTAT/WORN/TRIGGER/COMMAND):
   no faults, no halts.

Together the runs execute 83 of the 84 corpus opcodes (GETREMOTEVAR is never
reached; it is covered by a synthetic test) and start all 44 child scripts.
Set `OPENTPW_RSE_VM_REPORT_OUT=<file>` to write the full report.

`TotemScriptTraceIsPinned` pins 30 s of Totem.RSE (seed 1234, capacity 6, no
visitors): WAITANIM 0 at 16 ms, the 10 s passenger time-out, WAIT 700,
VAR_RUNNING = 1, a random TRIGANIM_CH set via JSR at 10,766 ms, TRIGANIM 5 at
11,283 ms, the sound/object timeline to 23,350 ms, then VAR_RUNNING = 0 because
nobody boarded. A closed Totem only emits WAITANIM 0 and idles.

## Sandbox Totem

The placed Totem (`PrototypeRide`) now runs its original `Totem.RSE` on the
fixed simulation tick. Open/Close set `VAR_RIDECLOSED` (0/1); `VAR_CAPACITY`
is the .sam `Upgrades[0].InitCapacity` (6); `VAR_DURATION` has no source and
stays 0 (one cycle per run). The script decides **when** the carriage moves:
its `TRIGANIM 5` (`ANIM_Main` in the repository's ScriptDefs and the docs'
`TRIGANIM ANIM_Main 0 0` example) plays the original `totemm1.MD2` clip once
through `ModelAnimationPlayer` ([MD2-MODELS.md](MD2-MODELS.md)); its 430 ticks
at the sandbox's unverified 30 ticks/s (14,333 ms) are reported back as the
animation duration for the duration operand and WAIT4ANIM. Which Totem clip the
original binds to `ANIM_Main` is inferred (`totemm1` is the full cart/cog
cycle), not traced. All other Totem effects (channel animations
`totemm2…10`, sounds, objects, screams, reverb, visitors) are unimplemented.
With no visitors, an opened Totem waits 10 s, moves once, and repeats.

## Visitors

The visitor opcodes (HUSH, WALKON, HOP, WALKOFF, WALKGET, LIMBO, UNLIMBO,
FORCEUNLIMBO, INLIMBO, LIMBOSPACE, BOUNCE, UNBOUNCE, FORCEUNBOUNCE, BOUNCING,
ADDHEAD, DELHEAD) and the `VAR_LETMEON`/`VAR_LETMEOFF` host protocol are
implemented against real guests by `RideVisitorBridge` (an `IRideScriptEffects`
layer; `VisitorRideScriptEffects` wraps any other effects). Every original object (OBJECTS.md) and the sandbox Totem use
it: with guests nearby its script fills at ≈3.3 s and unloads through
HOP/WALKOFF/WALKGET. Semantics are inferred from the corpus, see
[GUESTS.md](GUESTS.md). The default `UnimplementedRideScriptEffects` still returns
0 for scripts without a bridge.

Since the objects slice every original object runs its script this way with
generic effects that play the original clips for all animation opcodes,
including the Totem's `TRIGANIM_CH` channel clips (`totemm2…10`); the ANIM_* →
member mapping, channels and the corpus run of all 262 object scripts are in
[OBJECTS.md](OBJECTS.md).

## Not original behaviour yet

- Slice scheduling (one slice per 60 Hz tick, budget = header time slice),
  millisecond units, and CRIT_LOCK semantics are inferred, not traced.
- Visitor opcodes work for every placed original object (its `RideVisitorBridge`);
  walks along ride nodes are instantaneous, riders are not drawn, and
  TOUR/BUMP/COAST guest handling and WALKST_FLOAT/WALKFLOATSTAT/WALKFLOATSTOP
  are unimplemented.
- Animation playback only through the objects' effects (OBJECTS.md; rigid node
  tracks, vertex animation not decoded), no sounds/EVENT mapping,
  no objects/particles, no ride-type controllers (TOUR/BUMP/COAST), no park
  clock (HOUR/MIN/SEC return 0), no light opcodes.
- What the engine does with `EventMap.rse` variables, how the host is meant to
  feed `VAR_LETMEON`/`VAR_LETMEOFF`, and the `0xFFFF` second operand of EVENT
  are unknown.
- Child scripts get their own clock and RAND stream; whether the original
  shares them is unknown.

## Opcode table

Generated by `RideOpcodes.FormatCoverageTable` (corpus counts from the pinned
histogram); `CoverageDocumentMatchesTheHandlerTable` fails if this table and the
handler attributes diverge.

| # | Opcode | Corpus uses | Status | Evidence |
| ---: | --- | ---: | --- | --- |
| 0 | `NOP` | 2 | implemented | docs |
| 1 | `CRIT_LOCK` | 150 | implemented | docs (locks the ride); VM: the slice budget cannot preempt inside CRIT_LOCK…CRIT_UNLOCK, hosts must not change visitor variables while `InCriticalSection` |
| 2 | `CRIT_UNLOCK` | 240 | implemented | undocumented upstream; corpus: closes every CRIT_LOCK section (240 uses, 0 operands) |
| 3 | `COPY` | 1243 | implemented | docs; no flags (no branch in the corpus consumes flags after COPY) |
| 4 | `SETLV` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 5 | `SUB` | 74 | implemented | docs (sets flags); corpus: operand order is `SUB dest a b` → dest = a − b |
| 6 | `ENDSLICE` | 396 | implemented | name; corpus: sits in idle loops (`ENDSLICE; TEST VAR_RIDECLOSED; BRANCH_NZ`), so it yields until the next slice; flags survive it |
| 7 | `GETTIME` | 172 | implemented | docs (time the ride has been alive); milliseconds since the VM started |
| 8 | `ADDOBJ` | 644 | hooked | docs (`type parameter id slot`) |
| 9 | `ADDOBJ_EXT` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 10 | `KILLOBJ` | 235 | hooked | docs (`slot`); does not touch flags (`ADD VAR_COUNT 0xFFFF; KILLOBJ 200; BRANCH_PV`) |
| 11 | `FADEOBJ` | 113 | hooked | docs (`slot`) |
| 12 | `SETOBJPARAM` | 20 | hooked | docs (`slot parameter value`) |
| 13 | `EVENT` | 527 | hooked | docs (`type unknown event`); event names are theme-dependent and not assigned |
| 14 | `EVENT_EXT` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 15 | `FLUSHANIM` | 17 | hooked | docs (stop all animations); clears pending WAIT4ANIM time |
| 16 | `TRIGANIM` | 74 | hooked | docs example `TRIGANIM ANIM_Main 0 0`; corpus: third operand receives the duration (`TRIGANIM 5 0 VAR_TEMP; SUB VAR_TEMP VAR_TEMP 3000; WAIT VAR_TEMP; …; WAIT4ANIM`) |
| 17 | `WAITANIM` | 547 | hooked | docs (play and wait for the end) |
| 18 | `LOOPANIM` | 210 | hooked | docs (loop an animation); loops are not awaited by WAIT4ANIM (assumption) |
| 19 | `TRIGWAITANIM` | 133 | hooked | name (trigger + wait); operand layout as TRIGANIM (`TRIGWAITANIM 4 0 VAR_TEMP`) |
| 20 | `GETANIM` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 21 | `TRIGANIMSPEED` | 4 | hooked | unknown upstream; corpus `TRIGANIMSPEED 6 0 VAR_TEMP 4000 … WAIT4ANIM`: returned duration is tracked for WAIT4ANIM, operands passed through |
| 22 | `FLUSHANIM_CH` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 23 | `TRIGANIM_CH` | 63 | hooked | unknown upstream; corpus (Totem `TRIGANIM_CH 5 1 0 1` …, .sam `NumSimultAnims 4`) suggests animation/variant/duration/channel; passed through |
| 24 | `WAITANIM_CH` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 25 | `LOOPANIM_CH` | 1 | hooked | unknown upstream; 1 corpus use; passed through |
| 26 | `TRIGWAITANIM_CH` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 27 | `GETANIM_CH` | 15 | hooked | unknown upstream; corpus `GETANIM_CH 0 n; BRANCH_PV x; BRANCH_Z x` → `dest channel`, result sets flags |
| 28 | `RAND` | 56 | implemented | docs (`RAND dest max`); corpus: inclusive 0..max (Totem `RAND VAR_TEMP 2` selects one of three animation sets; `RAND 0 1; BRANCH_Z` coin flips) and sets flags |
| 29 | `JSR` | 70 | implemented | docs; corpus: every script using JSR declares stack ≥ 3, used as the return-address limit |
| 30 | `RETURN` | 33 | implemented | docs; LIFO return stack (the old VM used a FIFO queue) |
| 31 | `BRANCH` | 725 | implemented | docs; all 725 operands are branch words |
| 32 | `BRANCH_Z` | 787 | implemented | docs (zero flag set) |
| 33 | `BRANCH_NZ` | 943 | implemented | docs (zero flag clear) |
| 34 | `BRANCH_NV` | 55 | implemented | docs (negative); corpus: `SUB VAR_TEMP VAR_STARTNOW VAR_TEMP; BRANCH_NV` passenger time-outs |
| 35 | `BRANCH_PV` | 84 | implemented | docs (positive); corpus: strictly positive, because 15× `BRANCH_PV x; BRANCH_Z x` and `CMP VAR_TEMP 4; BRANCH_PV; BRANCH_NZ` would otherwise be dead code |
| 36 | `DBGMSG` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 37 | `NAME` | 277 | implemented | docs; the name is what FINDSCRIPTRAND searches for |
| 38 | `TEST` | 1318 | implemented | docs |
| 39 | `CMP` | 61 | implemented | docs say bitwise AND; corpus needs subtraction (a − b): `RAND VAR_TEMP 10; CMP VAR_TEMP 4; BRANCH_PV; BRANCH_NZ`, `CMP VAR_CAPACITY VAR_CARS; BRANCH_Z` |
| 40 | `PUSH` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 41 | `POP` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 42 | `HUSH` | 39 | hooked | unknown upstream; corpus input (`HUSH VAR_LETMEON` before boarding) |
| 43 | `HOP` | 39 | hooked | unknown upstream; corpus output (`HOP VAR_TEMP2; WALKOFF VAR_TEMP2`) |
| 44 | `WAIT` | 458 | implemented | docs; milliseconds (inferred); flags survive it (`WAIT 300; BRANCH_NZ`) |
| 45 | `WAITABS` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 46 | `WAIT4ANIM` | 170 | hooked | docs (wait for all playing animations); waits for the latest end time the effects layer reported |
| 47 | `ADD` | 541 | implemented | docs (dest += value, sets flags); corpus: 115 branches consume ADD flags, e.g. `ADD VAR_SPACELEFT 0xFFFF; BRANCH_Z` |
| 48 | `MULT` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 49 | `DIV` | 4 | implemented | docs; corpus: `DIV VAR_TEMP VAR_CAPACITY 9` + `MOD 0 VAR_CAPACITY 9` round-up idiom gives `DIV dest a b`; truncating, ÷0 faults |
| 50 | `MOD` | 5 | implemented | docs; corpus: `MOD 0 VAR_CAPACITY 9; BRANCH_Z` and clock `MOD VAR_TEMP VAR_TEMP 12; BRANCH_NZ`; ÷0 faults |
| 51 | `TURBO` | 20 | hooked | docs (`value` 0..1, meaning unknown) |
| 52 | `END` | 0 | unknown | no corpus use; not implemented (executing it faults the script) |
| 53 | `TOUR` | 76 | hooked | docs (command list); corpus `TOUR 10 0; BRANCH_Z` → result sets flags |
| 54 | `BUMP` | 199 | hooked | docs (command list); corpus 57 branches consume BUMP flags |
| 55 | `COAST` | 144 | hooked | docs (COAST_* ids); corpus 36 branches consume COAST flags |
| 56 | `ADDHEAD` | 31 | hooked | docs (`visitor`) |
| 57 | `DELHEAD` | 31 | hooked | docs (`visitor`) |
| 58 | `LIMBO` | 24 | hooked | docs (`visitor unknown`) |
| 59 | `UNLIMBO` | 24 | hooked | docs say input; corpus output + flags (`UNLIMBO VAR_LETMEOFF; BRANCH_Z`) |
| 60 | `FORCEUNLIMBO` | 23 | hooked | docs say input; corpus output + flags (`FORCEUNLIMBO VAR_LETMEOFF; BRANCH_NZ loop`) |
| 61 | `INLIMBO` | 6 | hooked | unknown upstream; corpus `INLIMBO 0; BRANCH_Z` → dest + flags |
| 62 | `LIMBOSPACE` | 24 | hooked | unknown upstream; corpus `LIMBOSPACE 0; BRANCH_Z full` → dest + flags |
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
| 97 | `YEAR` | 0 | hooked | docs only (no corpus use); as HOUR |
| 98 | `MONTH` | 0 | hooked | docs only (no corpus use); as HOUR |
| 99 | `DAY` | 0 | hooked | docs only (no corpus use); as HOUR |
| 100 | `HOUR` | 1 | hooked | docs (in-game hour → dest); corpus Clock.RSE `HOUR VAR_TEMP; MOD VAR_TEMP VAR_TEMP 12`; sets flags; park clock not implemented |
| 101 | `MIN` | 1 | hooked | docs (in-game minute → dest); corpus `MIN 0; BRANCH_NZ` uses a literal dest for flags only |
| 102 | `SEC` | 1 | hooked | docs (in-game second → dest); corpus `SEC 0; BRANCH_NZ` |
| 103 | `SETREVERB` | 21 | hooked | docs (0–10) |
| 104 | `DIPMUSIC` | 8 | hooked | docs (0/1) |
| 105 | `SPARK` | 1 | hooked | unknown upstream; 1 corpus use (Plasma); passed through |

Sources: OpenTPW docs (`src/formats/rsse.md`, `rsse-vm.md`,
`rsse-vm-instructions.md`), the repository's `ScriptDefs`/`RideVariables`
enums, and static analysis of the local original scripts. No original binary
was executed.
