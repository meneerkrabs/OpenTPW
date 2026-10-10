# Reproducible runs

A park run in OpenTPW is reproducible: the same world seed and the same inputs
give the same state. This holds for two runs in one process and for runs in
separate processes. This page covers the DET-I1 slice of the contract in
[reverse/DET-plan.md](reverse/DET-plan.md) §4. The simulation still runs on the
60 Hz `FixedStepClock` (ECON-001) and keeps its own generators. Moving to the
original's 31 ms substep and its world LCG is DET-I2, a later fidelity step.
That step changes every balance outcome.

## World seed

`WorldSeed` (`source/OpenTPW/World/WorldSeed.cs`) is the one explicit seed of
a run. `Level` takes it as a constructor argument (default
`WorldSeed.DefaultValue` = `Level.GuestSeed` = 0x5450574775657374) and
derives every simulation stream from it:

| Stream | Seed | Generator | Owner |
| --- | --- | --- | --- |
| Guests | `GuestStream` = seed | SplitMix64 `GuestRandom` (DET-014) | `GuestSimulation` |
| Economy | `EconomyStream` = seed XOR (default XOR 1) | SplitMix64 `DeterministicRandom` (DET-015) | `ParkEconomy` |
| Placed object scripts | placement ordinal XOR `ObjectScriptKey` | per-VM `System.Random(seed)` (DET-013) | `ParkObjects` |
| Scripts without a seed (prototype ride, `Ride`) | drawn from the script world's SplitMix64 stream, start `ScriptStream` | per-VM `System.Random(seed)` (DET-013) | `RideScriptWorld` |
| Script children (SPAWNCHILD/SPAWNSOUND) | drawn from the parent VM | per-VM `System.Random(seed)` | parent `RideVM` |
| Sound choosers (presentation) | `SoundStream`, set by `GameAudio.EnterPark` | the original's 1664525/1013904223 LCG (DET-012) | `SoundEventSystem` |

The stream keys are chosen so that the default seed reproduces the seeds each
system used before world seeds existed: guests `Level.GuestSeed`, economy 1,
placed objects 1, 2, 3, … As a result, existing balance results and the M3
gate baseline stay the same. The original seeds its world generator from
`time(NULL)`, so no seed is "the original one" (DET-002).

## Global and unseeded sources

Found in `source/OpenTPW` (World, Guests, Economy, Objects, VM, Audio) and
`OpenTPW.Common`:

| Source | Effect | Handling |
| --- | --- | --- |
| `OriginalObjectRuntime.nextAttractionId` (static counter) | attraction ids, and through them guest state and the raw guest hash, depended on how many objects the process had created before | ids are now allocated by the park's `RideScriptWorld`; every park starts at 1 |
| `RideVM`: `new Random()` when no seed was given (prototype Totem, `Ride`, standalone VMs) | RAND/FINDSCRIPTRAND differed on every run | the seed is drawn from the `RideScriptWorld` stream; a world-less VM gets its own default world (state 0) |
| `Level.RestoreVisitedRide`: prototype ride in its own script world | its attraction id could collide with id 1 of the park's objects | uses `Objects.ScriptWorld` |
| `GameAudio`: `SoundEventSystem(mixer, Environment.TickCount)` | sound choice (presentation) | each park reseeds it in `GameAudio.EnterPark` from `WorldSeed.SoundStream`; the clock seed now only affects front-end sounds |
| `Extensions.RandomVector3` → `Random.Shared` | none: it has no callers | kept for presentation and documented as such; `DeterminismTests` fails if simulation folders reference it |
| `Advisor`: `AdvisorMouth(new Random())` | mouth mesh choice while talking (presentation, clock B) | unchanged; the original also uses the C-library `rand` there (ADVISOR-001 area) |
| `ParkEconomy.CreateForTheme(seed: 1)` / `Level.GuestSeed` | fixed, not tied to one seed | now derived from the world seed (same values for the default) |

These statics were checked and are not simulation state: `Level.Current`, the
camera statics (`Camera`, `ParkCameraMode` target, set per level), `*.logged`
approximation-log flags, read-only parsed-asset caches (`ObjectAssets`,
`ObjectCatalog`, `AdvisorResponses`, `StringFile`), `GameAudio` service handles,
`CompatibilityStartup` configuration and render caches. `ParkObjects.nextSeed`
and `RideScriptWorld` counters are per instance.

`DeterminismTests.SimulationCodeUsesNoProcessWideRandomnessOrClock` scans `VM`,
`Economy`, `World/Guests`, `World/Objects` and every top-level `World` file
(including the `Level*.cs` partials) except the presentation files it lists
(advisor, cameras, sky, sun, sandbox file I/O). It forbids:

- `new Random()` and a target-typed `Random r = new();`, `Random.Shared`,
  `RandomVector3`;
- `Environment.TickCount`, `DateTime.Now`/`UtcNow`, `Stopwatch`;
- mutable static numeric fields and properties of any width (`static int`,
  `static long`, `static ulong`, `static float`, …), the form a process-wide
  counter takes; `static readonly` and `const` are allowed;
- `.GetHashCode(` and `HashCode.…`: .NET randomizes string hash codes per
  process, and a regular expression cannot tell a string receiver from another,
  so simulation code may not call them at all (`override GetHashCode`
  declarations are fine).

`SourceGuardCatchesEachForbiddenForm` checks each form against sample lines,
and each was also checked by inserting it into a scanned source file. Not
forbidden: `Guid.NewGuid` (temp file names) and `Parallel` (unused). Client
code (`GameFlow`, the M3 gate) is not scanned.

## Park save

OpenTPW's park save (`ParkSaveFile`, still version 2) has an optional `World`
section:

```json
"World": { "Seed": 6075451861746676596, "GuestRandom": …, "ScriptRandom": …, "SoundSeed": … }
```

The economy stream stays in `RandomState`. `Level.SavePark` writes
both. `Level.LoadPark` restores the economy and, when the section is present,
continues the guest, script and sound streams. Both delegate to
`ParkWorldStreams` (`source/OpenTPW/World/ParkWorldStreams.cs`), which holds no
state and needs no renderer, so tests run the same save, load and hash code. Saves without the section
(older files and economy-only callers) still load, but those streams are not
restored. The save does not hold guests, queues or object script state, so a
load keeps the running guests and scripts (DET-016).

## Canonical state hash

`WorldStateHash.Compute(WorldStateSources)` (`Level.ComputeStateHash()` for a
level) is FNV-1a 64 over little-endian values, with floats by bit pattern. It
follows the field order of DET-plan §4.5, restricted to what OpenTPW has. Schema
version 5 (version 1 also hashed the sound seed; versions 3 and 4 added the
queue state, version 5 the cell map, see Tests):

| DET-plan field | OpenTPW | In the hash |
| --- | --- | --- |
| `schema_version` | `WorldStateHash.SchemaVersion` | yes |
| (world seed) | `WorldSeed.Value` | yes (extra) |
| `scheduler_previous_ms` | no clock A | **missing** |
| `substep_counter` | 60 Hz fixed-step count (`GuestSimulation.TickCount`) | substitute |
| `manager_pass_counter` | no RSE manager pass; per-VM `SliceCount` is in the script digest | **missing** |
| `park_turn` | `ParkEconomy.Turn` | yes |
| `world_rng_state` | economy, guest and script-world stream states | substitute (three streams) |
| `coaster_rng_state` | no coaster generator | **missing** |
| `clib_rng_state` | no C-library generator | **missing** |
| `sound_seed` | `SoundEventSystem.Seed` | **not hashed** (saved; see below) |
| `scale` | `ParkEconomy.Speed` | yes |
| `paused` | `Speed == Paused` | yes |
| `game_type_mode` | `ParkEconomy.Mode` | yes |
| `world_state` | no world-state field | **missing** |
| `calendar_funny_start` | `ParkCalendar.Epoch` (fixed) | yes |
| `calendar_rate` | no clock rate | **missing** |
| `thing_table_digest` | guests in id order (every field the tick reads, including navigation and timers), including each guest's queue fields, the queue edge detectors, then attractions with queue cells, the queue list, admission state and counters, riders, limbo and bounce lists | yes (order is id order, not the original's newest first) |
| (cell map) | `ParkCellMap` of the guest grid: every non-empty cell in row order with its type, flags, placement counter, links and queue link (paths built by the path tool included) | yes (extra, after the thing table) |
| `script_table_digest` | every live script in id order: id, name, parent, state, PC, flags, call stack, variables, clock, wake time, timers, instruction and slice counts, seed and RNG call count | yes |
| `economy_digest` | the park save's economy part as canonical JSON (sorted keys) | yes |
| `input_log_cursor` | no input log | **missing** |

The sound seed is saved with the park but is not part of the hash. OpenTPW's
chooser stores every draw (DET-012), and it draws on UI clicks and whenever a
music or speech segment starts, which follows the audio device's timing. Two
runs with the same seed and the same simulation inputs would otherwise hash
differently with sound on, and a muted or device-less run would hash
differently from one with audio. The original's choosers do not advance the
seed (DET-plan §3 D9), which is why §4.5 can list it as replay state.

Other OpenTPW state that is **not in the hash**:

| State | Why it is outside |
| --- | --- |
| Placed objects outside their scripts: placement list and positions, `OriginalObjectRuntime` run flags (`stopped`, `wasRunning`), animator channels | not yet part of the canonical state; the scripts and the attractions' guest-facing state (queues, riders, `IsOpen` through the guest digest) are hashed |
| `GuestEconomyBridge` attraction → economy instance map | not yet part of the canonical state; the economy objects themselves are in the economy digest |
| `FixedStepClock` pending time (`PendingSeconds`) | frame timing, below one tick; the substep scheduler is DET-I2 |
| Sound chooser seed | presentation, see above |

A change to any of these does not change the hash. Adding one means bumping
`SchemaVersion` and re-pinning the fixed-run hash.

`GuestSimulation.ComputeStateHash` (the "raw guest hash") is unchanged. With
per-park attraction ids it now gives the same value for a second run in the
same process.

### For the M3 gate

The gate's `determinism.same-seed` row (docs/M3-GATE.md)
compares the raw guest hash and its own gate hash. Both now match within one
process, because attraction ids restart in every `RideScriptWorld`. The gate can
also record `WorldStateHash.Compute(new WorldStateSources { Seed, Economy,
Guests, Scripts })`. The gate's own `--seed` sets only the guest seed. Passing
`new WorldSeed(seed)` and using its streams would tie the economy and scripts to
the same seed as well.

## Tests

`DeterminismTests` (no game data needed):

- same seed twice in one process ⇒ equal hash; different seed ⇒ different hash
  (the synthetic park: economy, paying guests, a shop script, unseeded RAND
  scripts);
- save at mid-run, move the economy and the script stream by advancing and
  drawing from them, load ⇒ the hash at the save point is restored, and the
  final hash equals the uninterrupted run;
- save, then draw from every stream (guest ticks, a script seed, sound draws),
  load ⇒ the guest, script and sound streams equal the saved values; a
  `RestoreRandomState` that does nothing fails this test. The scramble never
  goes through the restore path under test;
- sound draws do not change the hash, and a run with a sound chooser hashes like
  a headless one;
- a pinned hash of a fixed run, checked in every test process
  (`0xC0E5705C8924E49A`, schema 5; the hashed fields are unchanged since `0x10A80C328A477CBE`, but upstream 1198a93 traced staff candidate grades to the binary and dropped the `ChanceToGetGreat` draw, so the economy stream's draws and the run changed. Before that:: the cell map digest was added when the path
  builder made `ParkCellMap` the source of truth for path cells. The run itself
  is unchanged (the raw guest hashes of the cross grid and the Jungle Easymode
  run are pinned across the change), but the hashed "last seen grid version"
  is now `ParkCellMap.Version`, which counts every cell-map write, so without
  the digest the schema-4 fields alone give `0x2EB8B824C2984A88` instead of
  schema 4's pin. Schema 4 (`0xE67AA45A94F4B20C`) added the guests' queue fields (position, join turn,
  last wait, move delay, called flag, standing and interlude turns, interlude
  flag, join happiness, queue cell and target index, target point, last
  state-11 turn), the guest simulation's queue edge detectors (last seen grid
  version and each attraction's last seen queue edit count) and the
  attractions' admission progress counters were added, because each of them
  decides what a later tick does; `StalledAdmissionChecks` was removed. The
  run also changed: broken attractions are no longer chosen, joined or
  admitted, and the queue needs window compares truncated happiness and toilet.
  Schema 3 (`0x8E84E46AA3C8D9EE`) added the queue cells, links, admission state
  and queue edits of each attraction when real queue cells landed);
- the default seed keeps the previous stream seeds; per-park attraction ids;
  unseeded scripts follow their world stream; `World` save section round trip
  and older saves;
- the source guard above.

`DeterminismAssetTests` (`OPENTPW_GAME_PATH`): original jungle objects get
attraction ids 1… in each of two parks in one process, with equal script hashes;
and `ParkWorldStreams.SavePark`/`LoadPark` (the code behind `Level.SavePark`/
`LoadPark`) with the real jungle park runtime and guests: every stream drawn
after the save, the load restores all of them and the saved economy. `Level`
itself needs a renderer and is not constructed by tests.

## Approximations

| ID | Rule |
| --- | --- |
| DET-002 | explicit world seed instead of the original's time-based seeds |
| DET-012 | sound draws store the successor seed; unmerged phase 10 says the original's choosers do not |
| DET-013 | script RAND/FINDSCRIPTRAND use per-VM `System.Random` seeded from the park stream, not the world LCG |
| DET-014 | guests use their own SplitMix64 stream, not the world LCG reseeded per guest id |
| DET-015 | the economy uses its own SplitMix64 stream, not the world LCG |
| DET-016 | the park save holds the streams and the economy, not guests, queues or scripts |

The remaining IDs proposed in DET-plan §4.6 (DET-001, DET-003 to DET-011) belong
to the substep scheduler and the original generators. DET-I2 will register them
with their code.
