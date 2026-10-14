# Reproducible runs

A park run in OpenTPW is reproducible: the same world seed and the same inputs
give the same state. This holds for two runs in one process and for runs in
separate processes only when external host-civil-time query samples are also
the same. A seed and player inputs alone do not pin those samples. This page
covers the DET-I1 slice of the contract in
[reverse/DET-plan.md](reverse/DET-plan.md) §4. The simulation still runs on the
60 Hz `FixedStepClock` (ECON-001) and keeps its own generators. Moving to the
original's 31 ms substep and its world LCG is DET-I2, a later fidelity step.
That step changes every balance outcome.

### External host civil time

TPW `YEAR`/`MONTH`/`DAY`/`HOUR` read host-local civil time, not the park calendar
or scheduler clock (the Feral witness is in reverse/PPC-clock.md, "RSE civil
time is an independent clock domain"). A `RideScriptWorld` accepts an injected
provider; its default samples the current host clock per executed instruction.
TPI and unsupported-edition worlds cannot enable it. `YEAR`, `MONTH`, `DAY`, `HOUR`, `MIN` and `SEC` all read it (tp.exe opcodes 97–102);
the sample is an external input (`[EXT:vm-host-time]`; VM-902 retired), so a replay needs the same samples.

The provider and its current host input are external: neither is serialized in
`RideScriptWorldSnapshot` or fed to the canonical hash. Scheduler timestamps,
returned VM variables, flags and branch state keep their existing save/hash
rules. Restoring a saved script preserves prior results, but future clock
queries may diverge unless the caller supplies the same external samples.
The existing synthetic deterministic-run fixtures do not query host civil time;
their pinned digests are unchanged. Real objects do: Halloween's Haunted Tower
(Info.Id 2418) runs `Clock.RSE`, which reads `HOUR`, and the hour reaches the
world state (`AParkWithACivilClockObjectHashesTheSameUnderAFixedClock`). Headless
runs that pin or compare a hash therefore pass `RideScriptWorld.FixedClock` (noon on
1 January 2000): the M4 gate and the browser digest do, and `ParkObjects` takes an
optional clock for any other. The game itself keeps the host clock. No digest exclusion or schema change hides
the results of a clock query.

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
| Coaster boarding (`CoasterRng`) | `ScriptStream` XOR a fixed key, folded to 32 bits (COAST-027) | the original's 214013/2531011 LCG (`CoasterRandom`) | `RideScriptWorld.Coasters` |
| Sound choosers (presentation) | `SoundStream`, set by `GameAudio.EnterPark` | the original's 1664525/1013904223 LCG; draws do not store the successor (sound_shared `0x100118C8`) | `SoundEventSystem` |

The stream keys are chosen so that the default seed reproduces the seeds each
system used before world seeds existed: guests `Level.GuestSeed`, economy 1,
placed objects 1, 2, 3, … As a result, existing balance results and the M3
gate baseline stay the same. The original seeds its world generator from
`time(NULL)`, so no seed is "the original one"; the explicit seed is OpenTPW's replay policy (`[EXT:explicit-world-seed]`, formerly DET-002).

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
`Economy`, `World/Guests`, `World/Objects`, `World/Coasters` and every top-level `World` file
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

The only clock exception is one exact TPW-gated provider expression in
`VM/RideScriptWorld.cs`, with its unique occurrence checked. The rest of that
file is scanned normally. `CivilTimeBoundaryDoesNotExemptOtherClockExpressionsOrFiles`
checks that different files, other clock expressions, appended expressions and
a weakened edition gate remain forbidden.

`SourceGuardCatchesEachForbiddenForm` checks each form against sample lines,
and each was also checked by inserting it into a scanned source file. Not
forbidden: `Guid.NewGuid` (temp file names) and `Parallel` (unused). Client
code (`GameFlow`, the M3 gate) is not scanned.

## Park save

The economy-only park save (`ParkSaveFile`, still version 2; docs/ECONOMY.md
"Save format") has an optional `World` section:

```json
"World": { "Seed": 6075451861746676596, "GuestRandom": …, "ScriptRandom": …, "SoundSeed": … }
```

The economy stream stays in `RandomState`. `Level.SavePark` writes
both. `Level.LoadPark` restores the economy and, when the section is present,
continues the guest, script and sound streams. Both delegate to
`ParkWorldStreams` (`source/OpenTPW/World/ParkWorldStreams.cs`), which holds no
state and needs no renderer, so tests run the same save, load and hash code. Saves without the section
(older files and economy-only callers) still load, but those streams are not
restored. This legacy file does not hold guests, queues or object script state, so a
`LoadPark` keeps the running guests and scripts (DET-016). Coaster tracks, trains and
the coaster generator are not saved by it either (COAST-025).

The park-resume world save (docs/PARK-SAVES.md) is separate: its
`opentpw-world` envelope captures the whole running park — layout, guests,
script world, per-object runtimes, clock and the attraction→economy links —
and restores it into a fresh candidate park, continuing the run exactly. The
streams still ride inside the embedded economy's `World` section, and the
envelope's shape check requires them to agree with the typed guest and script
fields, so a resumed park continues the same streams this page describes.

## Canonical state hash

`WorldStateHash.Compute(WorldStateSources)` (`Level.ComputeStateHash()` for a
level) is FNV-1a 64 over little-endian values, with floats by bit pattern. It
follows the field order of DET-plan §4.5, restricted to what OpenTPW has. Schema
version 14 (version 1 also hashed the sound seed; versions 3 and 4 added the
queue state, version 5 the cell map, version 6 the bounce slots' start time on one
branch, the guests' walk state on another and the coasters on a third, version 7
the first two on one branch and the scheduler clock and per-script clock origins
on another, version 8 all of those but the coasters, version 9 everything, version 10 the guest
arrival timer's start turn and group flag instead of its countdown, version 11 the economy objects' use counts, version 12 the guests' used and temporary attraction histories and an integer exit counter, version 13 their state-6 activity, version 14 the pending mechanic-arrival phase, see Tests):

| DET-plan field | OpenTPW | In the hash |
| --- | --- | --- |
| `schema_version` | `WorldStateHash.SchemaVersion` | yes |
| (world seed) | `WorldSeed.Value` | yes (extra) |
| `scheduler_previous_ms` | no clock A | **missing** |
| `substep_counter` | 60 Hz fixed-step count (`GuestSimulation.TickCount`) | substitute |
| `manager_pass_counter` | no RSE manager pass; per-VM `SliceCount` is in the script digest | **missing** |
| `park_turn` | `ParkEconomy.Turn` | yes |
| `world_rng_state` | economy, guest and script-world stream states | substitute (three streams) |
| `coaster_rng_state` | `CoasterWorld.Random` (the original's `s·214013 + 2531011` LCG, started from the world seed, COAST-027) | yes (after the script digest, with the coasters) |
| `clib_rng_state` | no C-library generator | **missing** |
| `sound_seed` | `SoundEventSystem.Seed` | **not hashed** (saved; see below) |
| `scale` | `ParkEconomy.Speed` | yes |
| `paused` | `Speed == Paused` | yes |
| `game_type_mode` | `ParkEconomy.Mode` | yes |
| `world_state` | no world-state field | **missing** |
| `calendar_funny_start` | `ParkCalendar.Epoch` (fixed) | yes |
| `calendar_rate` | no clock rate | **missing** |
| `thing_table_digest` | guests in id order (every field the tick reads, including navigation and timers), including each guest's walk speed and steering state and its queue fields, the queue edge detectors, then attractions with queue cells, the queue list, admission state and counters, riders, limbo and bounce lists (each bounce slot with its deadline and start) | yes (order is id order, not the original's newest first) |
| (cell map) | `ParkCellMap` of the guest grid: every non-empty cell in row order with its type, flags, placement counter, links and queue link (paths built by the path tool included) | yes (extra, after the thing table) |
| `script_table_digest` | the script world's scheduler clock, then every live script in id order: id, name, parent, state, PC, flags, call stack, variables, clock origin, clock, wake time, timers, WAIT4ANIM deadline, LOOPANIM and TRIGWAITANIM tokens, instruction and slice counts, seed and RNG call count; for a root object script also its animator channels (AnimID, variant, loop, speed, start, ended, tick, deferred clip) and clock | yes |
| (coasters) | `RideScriptWorld.Coasters`: the coaster generator, then every coaster in creation order: station ends, closed and complete flags, nodes (cell, height, t, pylon kind, stack index, station end, flags, bank), controller state (initialised, closed, ride mode, speed setting, seats, speed cap, net track money, time, last departure, braking start), samples (flags, height, roll, speed), rating, winch sections, trains (position, speed, state, boarding time, riders per car) and riders waiting to leave | yes (extra, after the script digest) |
| (tracks) | the track layer after the cell map (every track cell: type, mask, link, flags, piece, parent, rotation, overlap, segment) and `RideScriptWorld.Bumpers`: the cosmetic LCG, then every controller (type, laps, performance, phase, state, validity, tick count and clock, station, sections, waiting and leaving guests, every boat's slot, flags, position, velocity, heading, spin, target buoy, laps and rider count) — both only while a track or controller exists, so every park without one keeps its earlier digests and the schema stays 14 (TRACKS.md); bumper cars add the chase target and the drawn heading and yaw to their boats' fields (the same conditional extension: only while a controller exists, no schema bump, as for the water and kart fields); boats draw their aiming jitter from the script stream (`RideScriptWorld.NextWorldDraw`, DET-013) | yes (extra, after the coasters) |
| `economy_digest` | the park save's economy part as canonical JSON (sorted keys) | yes |
| `input_log_cursor` | no input log | **missing** |

The sound seed is saved with the park but is not part of the hash. As in the
original, a draw does not store its successor: the library init
(`0x100118B4`, store at `0x100118C8`) is the only writer of
`CAudioPlaceHolder::mRandomSeed`, the eight chooser and volume sites only read
it, and no library imports the symbol. Draws therefore never move the seed, so
UI clicks and audio-device timing cannot change the replay state (DET-plan §3
D9, §4.5).

A park started from an original save (SAVE-I, docs/ECONOMY.md "Original save
import") sets the economy's tick, bank, loans, objects and staff before its first
tick. All of it is in the economy digest, and the run that the pinned hash
covers starts no original save, so the schema and the pin are unchanged. The
save's `mRandomSeed` is not imported (SAVE-001): OpenTPW has no world LCG yet,
so its streams still come from the world seed.

Other OpenTPW state that is **not in the hash**:

| State | Why it is outside |
| --- | --- |
| Placed objects outside their scripts: placement list and positions, `OriginalObjectRuntime` run flags (`stopped`, `wasRunning`), animator poses (node matrices, vertex and UV arrays, node-state bits) | not yet part of the canonical state; the scripts and the attractions' guest-facing state (queues, riders, `IsOpen` through the guest digest) are hashed |
| `GuestEconomyBridge` attraction → economy instance map | not yet part of the canonical state; the economy objects themselves are in the economy digest |
| `FixedStepClock` pending time (`PendingSeconds`) | frame timing, below one tick; the substep scheduler is DET-I2 |
| Sound chooser seed | presentation, see above |

A change to any of these does not change the hash. Adding one means bumping
`SchemaVersion` and re-pinning the fixed-run hash.

Rides register round (October 14): a script's speed bias (`RideVM.SpeedBias`, the ride's `InitSpeed`; 50 at creation) is
part of the script digest only when it differs from 50, and is carried by the script snapshot (default 50, so older
snapshots restore at the creation value). No pinned hash moved. The scripted-object run of the original Totem
(`TotemScriptTraceWithItsObjectIsPinned`) was re-pinned deliberately: Totem's `InitSpeed` is above 50, so its animation
waits (`out / script speed`) and its WAITs (`operand / script speed`, STP-PPC 0xb0658) are now shorter, which moves
every later timestamp of its 30 s trace by up to about 1.2 s.

Track register round (October 14): the kart and water controllers tick every 31 ms (`BumpController.TickSeconds = 0.031`, was 1/30 s) and a
launched boat or kart starts at heading 0 (was the station's flow), so every park with a track controller runs a different
trajectory than before: the golden boat run of `BumpControllerTests` (positions, velocities and headings from tick 1, the
unload at tick 618 instead of 610) and the kart grid test were re-pinned deliberately, and the park-resume continuation hashes
of a park with a boat, kart or bumper car move with them. No `SchemaVersion` bump: the hashed fields are the same ones and the
track layer and the controllers still join the hash only while one exists (the conditional extension above), so no park without
a controller changes and the pinned fixed run (`0x776A2A9E9A53BF25`, schema 17) is untouched. The new per-car `Smoke` and the
boats' `Engine` flag are presentation state: saved (trailing optional fields, so older saves restore) but not hashed. A save from
before the tick change may carry a tick clock up to 1/30 s and still restores (`BumpController.MaximumSavedRemainder`).

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
  (`0xCAF8114002723B8C`, schema 18: the bank's withdrawals flag, balance after the last debit and turn-entered-red stamp join the
  park save and so the hash; the synthetic park stays in the black, so only the hashed fields change the value (docs/ECONOMY.md). Before that:
  `0xB4FA626D739B5697`, schema 17: the guests think as in tp.exe (the pick of the needs phase on one phase in ten, from a hash of the
  guest and the turn, and no thought at the end of a ride or shop visit), so the hashed thoughts change; the guests' stream is untouched
  (docs/GUESTS.md "Thoughts"). Before that:
  `0x776A2A9E9A53BF25`, schema 17: the guests' balloon life, balloon and costume join the hash, so the schema is bumped; the synthetic park has no
  balloon or costume shop, so only the hashed fields change the value (docs/GUESTS.md, "Balloons and costumes"). Before that:
  `0x74187CE603B2ACDC`, schema 16: the guests' prankery flag joins the hash, so the schema is bumped; the synthetic park has no pranksters
  (PrankeryLikelihood 0), so only the hashed field changes the value. Vomit and stinkbombs now spread their RegionFX on the environment grid,
  which the needs update reads (docs/reverse/WANDER-plan.md §7). Before that:
  `0x05B8B8443FF0454B`, schema 15: litter moves from a park-wide count to map cells (the economy snapshot keeps the littered cells and a
  handyman's swept cell) and the guests gain a litter meter and a bin to walk to, so the schema is bumped and the run changes: a sale no longer
  adds litter to the park; a guest drops or bins its litter in state 6 and vomit stays on its cell (docs/reverse/WANDER-plan.md §6). Before that:
  `0x9EA19826749C30F7`, schema 14: optional awaiting-mechanic-arrival is gameplay state
  in the canonical economy snapshot. Missing/false is omitted for legacy jobs; true
  waiting and started work must hash differently. The fixed headless run itself
  remains unchanged; only its schema word changes. Before that:
  `0x05DD3EC845B5F51C`, schema 13: a guest's seconds-based decision timer leaves the hash and its state-6 activity joins it, so the schema is bumped,
  and the run changes: a guest in the park follows the original's state 6 (one decision per park turn, the major choice at most every 30 turns) and state 7 (a walk of
  one to five cells to a wander destination) instead of deciding every 3 s and wandering one cell at a time (docs/reverse/WANDER-plan.md, GUEST-010). Before that:
  `0xB7F84A5A989B61F7`, schema 12: the hashed fields are unchanged, but guests now step off the bus one per park turn instead of every 0.4 s (tp.exe `0x4cf3e0`, ARRIVAL-001), so the run changes. Before that:
  `0xA35A9A8C809310FB`, schema 12: the hashed fields are unchanged, but the park value that the ledger months record (hashed with the economy) is now the original's object rule (PPC oracle F29: the costs of an object's levels times its state of repair / 100) instead of the age-scaled catalogue costs, so the run's hash changes. Before that:
  `0xC028F4EF0BED9D1C`, schema 12: the guests' used and temporary attraction histories and an integer exit counter join the hash, so the schema is bumped,
  and the run changes: guests choose and rate attractions by the original's score (PPC oracle F24), start and grow by the traced needs update and constructor
  (F25) and leave by the traced test (F26: happiness below 1, an exit counter of exactly 0 or a closed park, tested every park turn). Before that:
  `0x373368AA3A0DB3A0`, schema 11: the saved economy hashed by the schema gains each object's use count (`TimesUsed`, the original's object word +412, PPC oracle F21), so the schema is bumped; the run's behaviour is unchanged. Before that:
  `0x523D1494D867C0AC`, schema 10 (the same field added without the bump, never released): each economy object's use count (`TimesUsed`, the original's object word +412, PPC oracle F21) is saved and so hashed; the run's behaviour is unchanged and without the field it still gives `0xA51B682C587D0689`. Before that:
  `0xA51B682C587D0689`, schema 10: the economy counters, already hashed, gain each type's first-purchase turn
  (`firstturn:<Info.Id>`, PPC oracle F10); the synthetic park's shop adds one, so the run's hash changes. Before that:
  `0xF21F9E6EC3DBEB23`, schema 10: the traced arrival rule (docs/GUESTS.md, "Arrivals") hashes the arrival
  timer's start turn and group flag instead of a countdown, and its groups change the run. Before that:
  `0x3FD757BA59B3524D`, schema 9; COAST-M merged the coasters into schema 8: the coaster digest
  (see the table) follows the script digest. The synthetic park has no coaster, so the digest holds only the coaster
  generator's start state and a count of 0; with schema 8's fields the run still gives `0xB684DD70FFFF8309`. Before
  that, on two branches:
  `0xB684DD70FFFF8309`, schema 8; ANIM-M merged the two schema-7 branches, so the script digest's scheduler clock and
  clock origins and the thing table's walk state are all hashed. Before that, on two branches:
  `0xE3498781B921C80D`, schema 7; the run is unchanged (schema 6's fields still give `0x0BC982AB91BE9A28`), but the
  script world now hashes the shared scheduler clock that GETTIME and the WAITANIM(_CH) deadlines read, and each
  script its clock origin, the scheduler clock when it was created (docs/RSE-VM.md, RIDES-044). Before that:
  `0x0BC982AB91BE9A28`, schema 6; the run was unchanged, but each script then hashed the animation state the
  original keeps per script (the single WAIT4ANIM deadline, the LOOPANIM token and the TRIGWAITANIM token,
  replacing the latest-clip-end field) and, for a root script whose effects own animation channels, the
  object's channel state that GETANIM and TRIGWAITANIM read back (docs/reverse/ANIM-plan.md §8); and
  `0x082000C95B7EB3FE`, schema 7; the hashed fields are unchanged, but WALK-F ported the traced separation force
  (guests push each other apart), stops avoid_walls at the first pushing edge and updates the toilet term only on the
  guest's needs phase, so the run changed. Before that: `0x733233EC7B25360E`, schema 7: WALK-I's walk state and
  ORACLE-F's bounce slot start, merged. Before that, on
  two branches: `0x714AAFDCEC083403`, schema 6: WALK-I added each guest's walk speed state (base
  speed, toilet term, boost, smoothed speed, max speed and force) and its navigation state
  (16.16 position and velocity, destination, arrived flag, waypoint index, buffer, route and
  cursor, the walk's extra open cells, progress history and last remaining distance), and
  removed the float waypoint; the run changed too, because guests now walk with the traced
  steering once per park turn, draw a base speed from the guest stream and smooth their speed
  with the original's fused multiply-add (PPC oracle F6); and `0x53E84EFBE47E1D30`, schema 6: UNBOUNCE now needs the
  slot's start time (`0xae020`, ORACLE-V F5), so each bounce slot hashes its start as well; that fixed run itself was
  unchanged and gave the previous pin under schema 5.
  Before that: `0x98BFAE5138E2A343`, schema 5; the hashed fields are unchanged, but the original ride update gave each park-save object an exact state of repair and a life gauge (`Repair`, `LifeGauge`), which the economy digest includes. Before that: `0xC0E5705C8924E49A`, where the hashed fields were unchanged since `0x10A80C328A477CBE`, but upstream 1198a93 traced staff candidate grades to the binary and dropped the `ChanceToGetGreat` draw, so the economy stream's draws and the run changed. Before that:: the cell map digest was added when the path
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
| DET-013 | script RAND/FINDSCRIPTRAND use per-VM `System.Random` seeded from the park stream, not the world generator (tp.exe `0x516330`: LCG, rotate right by 13, abs) |
| DET-014 | guests use their own SplitMix64 stream, not the world generator reseeded per guest id |
| DET-015 | the economy uses its own SplitMix64 stream, not the world generator |

DET-016 is retired (October 14, 2026): the economy-only park save is the legacy `ParkSaveFile` (`[EXT:legacy-economy-save]`), used by the
determinism tests; the player's save (`GameFlow.SaveCurrentPark` → `ParkResumeSaveFile`) stores the economy and its streams, the layout,
the guests with their queue bookkeeping, the script world and each object runtime, the advisor, the coasters and the tracks (docs/PARK-SAVES.md).

Port requirements for DET-013 to DET-015 (tp.exe, static evidence, nothing missing; the port is not done): the one world generator is
`s = rotr(s * 1664525 + 1013904223, 13)` returning `abs((int)s)` with `int.MinValue` staying negative (`0x516330`); one `WorldRng` per level
holds the u32 at world+0x1DA708; `0x516370` reseeds it with the zero-extended u16 thing id at eight sites (`0x4D71CA` and `0x4D94E2` in litter,
`0x4F7B7E`, `0x4FB19E` in guest creation with eight draws, the reseed and two more draws, `0x4FCAFD`, `0x4FE6A1` and `0x4FE6FC` for the balloon,
`0x50205F`), all taking the id through `0x50B350`; the state is seeded from `time()` at world setup and saved as `mRandomSeed` next to
`mGameTick` (save `0x51710F`, load `0x517DE8`); every consumer shares the one stream with its own projection (RAND `labs(labs(r >>> 1) % (int16 + 1))`,
ADDHEAD and FINDSCRIPTRAND `(labs(r >>> 1) % n) + 1`, tour and flying cars `labs(r) % 100`, 139 direct calls plus four inline copies); the original
call order (substeps and the RSE pass schedule, things newest first, economy month-end) is needed for the right sequence, not only the distribution.

The remaining IDs proposed in DET-plan §4.6 (DET-001, DET-003 to DET-011) belong
to the substep scheduler and the original generators. DET-I2 will register them
with their code.

### Faulted scripts

Since the root-fault containment (`[EXT:vm-fault-containment]`, docs/RSE-VM.md) a script that faults stops advancing its
clock, and its object releases its riders, trains, controller and sounds. Runs without a fault hash as before (the pinned
digests are unchanged); a run in which a script faults now hashes differently from before that change, because the faulted
script's `TimeMilliseconds` stays put and the released riders change the guest digest.
