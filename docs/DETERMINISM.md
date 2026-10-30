# Reproducible runs

A park run in OpenTPW is reproducible: the same world seed and the same inputs
give the same state. This holds for two runs in one process and for runs in
separate processes only when external host-civil-time query samples are also
the same. A seed and player inputs alone do not pin those samples. This page
covers the DET-I1 slice of the contract in
[reverse/DET-plan.md](reverse/DET-plan.md) §4. The simulation runs on the
60 Hz `FixedStepClock` (`EXT:fixed-60hz-clock`; ECON-001 keeps the substeps) and, since the world generator was ported,
draws every random decision from the original's one generator (below). A park turn now runs in the original's order
(the thing sweep, below) and inside the original's 31 ms substeps (the script pass, the bump module and the tour cars,
below); what remains of DET-I2 (DET-013 and DET-014; DET-015 is retired) is listed under "What is still unlike the original".

### External host civil time

TPW `YEAR`/`MONTH`/`DAY`/`HOUR` read host-local civil time, not the park calendar
or scheduler clock (the Feral witness is in reverse/PPC-clock.md, "RSE civil
time is an independent clock domain"). A `RideScriptWorld` accepts an injected
provider; its default samples the current host clock per executed instruction.
TPI and unsupported-edition worlds cannot enable it. `YEAR`, `MONTH`, `DAY`, `HOUR`, `MIN` and `SEC` all read it (tp.exe opcodes 97–102);
the sample is an external input (`[EXT:vm-host-time]`; VM-902 retired), so a replay needs the same samples.

The provider and its current host input are external: neither is serialized in
`RideScriptWorldSnapshot` or fed to the canonical hash. In a co-op session the
scripts read the session's clock instead, which the host sets with a command
(`SetSessionClockCommand`, `[EXT:online-coop-lockstep]`; [COOP.md](COOP.md),
"The host's command stream"), so every peer reads the same time from the same
tick; that value is part of the session digest, not of the world hash or a save. Scheduler timestamps,
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
| World generator (guests, staff, the economy's staff pool, script RAND and FINDSCRIPTRAND, the bump and tour rides, rider heads, litter) | `GeneratorState` = `WorldRng.StateFromSeed( EconomyStream )` | the original's `WorldRng` (tp.exe `0x516330`: `s = rotr(s·1664525 + 1013904223, 13)`, the rotated value is the new state, the result its absolute value) (DET-013 to DET-015) | `ParkEconomy.World`; guests and script world attach to it |
| Scripts created with an explicit seed (tests, standalone scripts, older saves) | the explicit seed | per-VM `ScriptRandom(seed)` (System.Random-compatible) | the VM |
| Coaster boarding (`CoasterRng`) | `ScriptStream` XOR a fixed key, folded to 32 bits (`[EXT:coaster-rng-seed]`, formerly COAST-027) | the original's 214013/2531011 LCG (`CoasterRandom`) | `RideScriptWorld.Coasters` |
| Sound chooser (presentation) | `SoundStream`, set by `GameAudio.EnterPark` | the library's one shared LCG, `s·0x19660D + 0x3C6EF35F` stored on every draw, `ror(s, 13)` then `>> 16` or `% range` (tp.exe `0x6bc839`; AUDIO-037) | `SoundEventSystem` |
| Weather (presentation and sim) | `ParkWeather` seed from the park's weather key | SplitMix64 `DeterministicRandom`; the PC draws from the shared world LCG `0x516330` (`[EXT:weather-stream]`) | `ParkWeather` |

### The one world generator (DET-013 to DET-015)

The original has one generator for the whole simulation (tp.exe `0x516330`, the u32 at world `+0x1DA708`, saved raw as
`mRandomSeed`, `0x517123` write and `0x517DFC` read). `WorldRng` (`source/OpenTPW/World/WorldRng.cs`) is that generator;
the economy owns the park's instance and the level attaches the guests and the script world to it
(`Level.LinkWorldGenerator`, `ParkEconomyRuntime.AttachGuests`, `ParkWorldStreams.LinkGenerator`), so everything that draws
shares one stream:

- guests: the creation draws in the original's order (the base constructor's speed index `0x4F89E1`, the eight starting
  values `0x4FAFF8..0x4FB114`, the reseed with the guest's id `0x4FB19E`, the sprite variant `0x541F85`, and for an arrival the
  entrance side `R & 1` and the sideways byte `0x4FB201`, `0x4FB21C`), the balloon and costume reseeds (`0x4FE6A1`,
  `0x4FE6FC`), the thought pick's draw on every needs phase (`0x501919`), and the wander, booth, queue, litter and
  small-portion draws that were already traced;
- the economy's staff pool and the staff walks (candidates, the rest gate, the no-rest-area happiness hit, the guards' set-down,
  the handymen), and the litter drop and sweep reseeds with the cell id (`0x4D94E2`, `0x4D71CA`);
- scripts without an explicit seed (every script of a park, children included): RAND, FINDSCRIPTRAND and ADDHEAD take the
  original's projections of one draw (`0x55398A`, `0x5560CB`, `0x554CB5`), and the bump and tour rides their own;
- the arrival vehicle's pick when a leaving guest calls one (`0x51A34B`).

Presentation (particles, voices, the sound chooser, the weather's visuals, the advisor, animation, the texture roll of
PATH-014 and the online players of a visited park) stays off it; `WorldRngTests.PresentationCodeNeverReachesTheWorldGenerator`
checks the folders and files. The explicit
seeds remain for what the original seeds explicitly or OpenTPW seeds for itself: the coaster LCG, the weather, the sound
chooser and a script made with a seed.

What is still unlike the original, and recorded in DET-013 and DET-014 (DET-015 is retired, see the table below): the guest and staff sites with no counterpart here
(the arrival lane picks of later states, the gate wait, the won sideshow's tail, the staff constructors' role searches, which run a turn late), the
scripts of an imported original save (a saved animation control held on its first frame or on the unscaled clock, a script with a saved child or sound child, and the saved child records its parent recreates, below), scripts created with an explicit seed (tests and
older saves), and the weather, which keeps its own stream (`[EXT:weather-stream]`). The advisor takes no draw: its pick is
sequential for every message (below).

Round 10 (October 28, 2026): the fixed run's pin `0x5946C7CB09C6EEE7` does not move (its run hires no staff). The Instant Action economy digest of `BuildStepsTests` moves from `E19E5729D4A8EE5A` to `4DCABA51A693C7D7`, because the seed's imported staff now carry their saved sprite variant in the park save (GUEST-014, `0x4F8B10`); no draw is added or removed by it. A hired member (a placing click with a thing pool) now consumes one draw for the base constructor's base speed (`0x4F89E1`) and a guard a second for its own sprite variant (`0x4D5E02`).

Round 10, package B (cells under track, October 28, 2026): no pin moves (the fixed run's `0xB4AEC10551DF2738` and the economy digest of `BuildStepsTests` stay as they are; the with-data suite passes unchanged). The map's type byte and the track layer hold a path or queue cell over a straight or stub tile and the demolition clears the second layer's records, but every state the pinned runs reach is the one they reached before: no run lays a path over a tile or sells an add-on. The cell digest gains no field (the track layer joins it only while a track exists, as before); the new states hash through the type byte and the records already hashed. No draw is added or removed.

### The substep scheduler (DET-013)

The original's main loop (tp.exe `0x54F4BF`) runs a 31 ms substep, and every eighth substep is a park turn. Each substep, in
this order (`Level.AdvanceSimulation`, `ParkObjects.Simulate`, `SimulationSubstep`):

1. the substep counter (`0x877D34`) and the scaled clock (`0x878C74`, +0x1F) rise at `0x54F4C4`..`0x54F4D6`, before the inactive-application test (`0x54F4D4`): the body is skipped
   (`0x54F8CD`) only while the application is inactive (`0x7A1A14` bit 3 clear, kept by the main window procedure `0x46B600`) and the option bit `0x7A1A8C` bit 0 is set (its default,
   `0xC0E15` at `0x449E1C`). The game pause (`0x4092A0`) freezes the clock object `0x785970` that drives the substep loop (`0x54F47F`..`0x54F4AF`), so a paused park runs no
   substep at all: its scripts, bump module, tour cars and turns stand still with its calendar and its counter stands, as OpenTPW's 60 Hz clock does while paused;
2. the effects system (`0x520130`, not modelled);
3. the bump module tick (`0x546C80`, `0x54F55F`): once per substep, whichever rides exist;
4. the script pass (`0x5516B0`, `0x54F56B`): the pass counter (`0x8791A4`) rises, then the scripts run **newest first** (a
   script is linked at the list's head when it is created); a script runs its slice when
   `(script id xor pass counter) & 7 == 0`, so one slice per eight passes (248 ms), on the pass its id selects, or in every
   pass when its TURBO byte (opcode 51) is nonzero; a script's WAIT therefore wakes on the first slice after its time;
   after its slice (run or skipped) the pass walks the script's object records: a kept sound with a key is moved to its node only when
   `(script id xor pass counter) & 7 == 0` again (`0x551A1F`, built: `OriginalObjectRuntime.FollowHeldSounds`, once per pass), while a kept particle effect moves in every pass, which OpenTPW runs on every 60 Hz tick (DET-013);
5. on even substeps the tour cars (`0x55ABF0`, `0x54F5D7`) and the sprite scheduler `0x475360`. Visitor body players now use these opportunities with an explicit fixed-60Hz host sample (see [Visitor sprites](VISITOR-SPRITES.md)); staff retains its renderer-driven adapter. Other effects remain DET-013;
6. on every eighth substep (`0x54F668`) the park turn (the order below).

The 60 Hz tick (`EXT:fixed-60hz-clock`) derives its substep from the park's own tick count: substep *n* ends in the first tick whose
end is at or past *n* × 31 ms (`tick × 1000 / 1860`), so at most one substep ends in a tick and the turn counter is the substep
counter divided by eight, exactly. The script clock (`GETTIME`, WAIT) gains 31 ms per substep, slice or not. Script ids are the
creation order from 1 (`0x558C0D`: the next-id word `0x8791A8` starts at 1 and is never reused), which OpenTPW's creation order
matches for a park built in OpenTPW; the script pass counter (`0x8791A4`) restarts at 0 when a level starts, and the RSSE block of an
original save holds it (the header is written and read raw over `0x8791A0`, writer `0x559350`, loader `0x5597A0`), as OpenTPW's park save does
(`RideScriptWorldSnapshot.PassCounter`, optional: an older OpenTPW save starts it at 0), so that a resumed park continues exactly; it joins the canonical hash once a pass has run.
An imported original save's scripts take the saved ids, the saved next id and that pass counter (`SavedScriptImport`): the loader links every record at the list's
head, so after a load the pass walks the saved scripts in reverse of the file's order (the writer walks the list head first: the corpus' `2, 1` of a new park is walked `1, 2`,
and a park saved after a load lists `9, 8, 7, 1, 2`), which `RideScriptWorld.ListOrder` keeps explicitly. What
the host does between substeps (the ride visitor protocol's host step, the coasters, the animators) stays per tick.

The advisor controller's update (thing type 11, `0x59A550`) has one draw of the world generator, in the mode-3 branch of its
response pick (`0x59A7AC`); the mode word (`+0x2C` of the 0x38-byte descriptor row, read by `0x59C490`) is 2 in all 351 rows that
the initializer `0x5A0A50` fills (`tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py` checks it), which is "the previous response + 1"
(`0x59A796`), so the advisor draws nothing and stays off the stream for good.

### The park turn's order (DET-013)

The tick that starts a park turn (every 248 ms of scaled time, `ParkCalendar.Turn`) runs the original's world update
(`Level.AdvanceSimulation`, `Level.RunThingSweep`; tp.exe main loop `0x54F4BF`, world update `0x516380`):

1. the substep's work first: the bump module, the script pass and the tour cars (`0x546C80`, `0x5516B0`, `0x55ABF0`; the section above, `Objects.Simulate`);
2. the turn counter advances;
3. the thing sweep: the pool's used list from its head, the thing built last (`0x7CF56C`, `ThingPool.Capture`), each
   thing dispatched by its type (`0x50B360`): a guest (`0x501650`, `0x5019F0`), an attraction (the economy's wear and
   breakdown update `0x4E0B90`, then the ride update and its admission `0x4E0E00`), a mechanic, handyman, entertainer,
   guard or researcher (a researcher does its research first, on every 20th turn, `0x502960`), the weather (`0x512880`)
   and the challenge system (`0x4D1E90`); a thing that has asked for its destruction (byte `+3`) is skipped, and a
   lost park (world state 4) skips the walk only (`0x516395` jumps to the gate retry);
4. the gate retry on every 30th turn (`0x51643B`);
5. the turn's deferred destroys, last request first (`0x516641`): the ticks between turns free nothing;
6. the world turn's tail (`0x4D7B20`): the arrival update (`0x4CF3E0`), then the economy's staff pool, calendar,
   month end and golden-ticket step, and the main loop's crowd read. The tail runs in every world state, a lost park's
   arrival update included (its group is 0, `0x4CF5B0`, but the bus still comes and goes); the weather is updated once, by
   its thing in the walk (`AdvanceTo` is a no-op for a turn it has already stepped), so a lost park's weather stands still.

A thing without a slot in the pool (a headless scene's guest, a placeholder) is updated after the sweep, in list order. The
economy without a host sweep (the headless economy of tests and tools) keeps its own order: staff, research, rides,
challenges.

The stream keys still derive the seeds a standalone guest simulation, economy or script world starts from
(guests `Level.GuestSeed`, economy 1), and the placement ordinal XOR `ObjectScriptKey` stays an object's creation seed (its identity in
the layout and the save), but the world generator does not reproduce the old per-system streams: every balance outcome moved once when
it was ported (the re-pin under "Tests"). The original seeds its world generator from
`time(NULL)`, so no seed is "the original one"; the explicit seed is OpenTPW's replay policy (`[EXT:explicit-world-seed]`, formerly DET-002).

## Global and unseeded sources

Found in `source/OpenTPW` (World, Guests, Economy, Objects, VM, Audio) and
`OpenTPW.Common`:

| Source | Effect | Handling |
| --- | --- | --- |
| `OriginalObjectRuntime.nextAttractionId` (static counter) | attraction ids, and through them guest state and the raw guest hash, depended on how many objects the process had created before | ids are now allocated by the park's `RideScriptWorld`; every park starts at 1 |
| `RideVM`: `new Random()` when no seed was given (prototype Totem, `Ride`, standalone VMs) | RAND/FINDSCRIPTRAND differed on every run | a script without a seed draws from its `RideScriptWorld`'s world generator (the park's one stream); a world-less VM gets its own default world (state from seed 0) |
| `Level.RestoreVisitedRide`: prototype ride in its own script world | its attraction id could collide with id 1 of the park's objects | uses `Objects.ScriptWorld` |
| `GameAudio`: `SoundEventSystem(mixer, Environment.TickCount)` | sound choice (presentation) | the clock seed matches the original's start-up seeding (`QueryPerformanceCounter`, tp.exe `0x72e09c`) and only affects front-end sounds: each park reseeds the chooser in `GameAudio.EnterPark` from `WorldSeed.SoundStream` and saves it (`[EXT:sound-chooser-seed]`, the one deviation from the original's unsaved chooser) |
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
and each was also checked by inserting it into a scanned source file. `Guid.NewGuid`
and `Parallel` are banned by the second scan below. Client code (`GameFlow`, the M3
gate) is not scanned.

## Browser and native

A co-op session (docs/ONLINE.md; the user chose lockstep with the host ordering commands, and browsers that join and host) needs the
browser build and every desktop to compute the same simulation bits. This section is the gate (co-op phase C1b).

### What can differ

The CLR's `Math.Sin`, `Math.Atan2`, `Math.Pow` and their relatives, `MathF`'s, and the System.Numerics builders that use them (rotation
matrices, quaternion slerp) call the C library of the runtime: MSVC's on Windows, glibc on Linux, Apple's libm on macOS, and
emscripten's musl in the browser (Mono, AOT or interpreter). They are not correctly rounded, and they disagree in the last bit. Over
the 20,000 fixed inputs per function of `CrossRuntimeMathProbe`, Apple's libm (macOS arm64) and the browser's musl (Chrome, Mono AOT)
differ in:

| Function | Inputs that differ |
| --- | ---: |
| `Math.Sin` / `Math.Cos` | 895 / 894 |
| `Math.Atan` / `Math.Atan2` / `Math.Acos` | 715 / 3,483 / 3,439 |
| `Math.Pow` / `Math.Log2` | 1,994 / 39 against FreeBSD's routines (musl uses Arm's table routines, which glibc shares) |
| `MathF.Sin` / `MathF.Cos` / `MathF.Atan2` / `MathF.Acos` | 418 / 230 / 1,150 / 228 against the double result rounded once |

Square root, fused multiply-add, the four arithmetic operations, rounding and conversions are exact in IEEE 754 and gave the same
digests in both (pinned in `DetMathTests.CorrectlyRoundedPlatformFunctionsArePinned`).

System.Numerics' `Vector2`/`Vector3`/`Vector4.Lerp`, `Transform` and `TransformNormal` and the `Matrix4x4` product are another possible
source: .NET evaluates them with `MultiplyAddEstimate`, which fuses the multiply and the add where the processor has fused multiply-add.
On macOS arm64 (.NET 11 RC2) `Vector3.Transform` equals `fma(z, m31, fma(y, m21, x * m11)) + m41` for every one of 200,000 random inputs
and differs from the unfused sum for a quarter of them; a runtime that does not fuse rounds twice. `Dot`, `Cross`, `Length`, `Normalize`
and `Distance` are not fused. The browser's Mono AOT build computes other values for them than the desktops (below). The guard below does not ban these operations, and a source
scan cannot find the matrix product. `DetMatrix` (`source/OpenTPW/Utils/DetMatrix.cs`) has the plain versions (product, point
transform, lerp, and a slerp with DetMath's acos and sin), every product and sum rounded left to right. `CoasterPylonRig` composes its
tag matrices with them and blends its clips' keys with them, and the coaster station frame (`CoasterStationLayout.FromModel`)
composes a dummy's parent chain (`ObjectAnimator.LocalTransforms`) and the placement with them ("Cross-platform coaster pins" below).
`CrossRuntimeMathProbe` reports the System.Numerics operations and `DetMatrix`'s over fixed inputs:
`DetMathTests.DetMatrixDigestsArePinned` pins `DetMatrix`'s on every runtime. `FusedSystemNumericsOperationsArePinned` holds the
fused System.Numerics values that Windows, Linux and macOS compute; the browser's Mono AOT build computes others (its interpreter
matches the desktops), so that comparison is informative in the browser check, like the platform library's, and documents why
simulation code must not use those operations.

### DetMath

`DetMath` and `DetMathF` (`source/OpenTPW/Utils/DetMath.cs`) are C# ports of FreeBSD's msun as musl ships them: sin, cos
(with the Cody-Waite and Payne-Hanek reductions), atan, atan2, acos, pow and log2, with a fixed order of IEEE double operations, so
every runtime computes the same bits. The single-precision versions evaluate in double and round once. Over the probe, sin, cos,
atan, atan2 and acos equal the browser's musl bit for bit, and every function is within one unit in the last place of Apple's libm
(`DetMathIsWithinOneUlpOfThePlatformLibrary`). `DetMathTests.DetMathDigestsArePinned` pins their results; CI checks the pins on
Windows, Linux and macOS, and the browser check checks them in WebAssembly.

Simulation code calls DetMath for every elementary function. `[EXT:deterministic-math]` (user decision, 2026-10-19: "Als EXT:
deterministic-math"): tp.exe evaluates them on the x87 in extended precision (`fsin`, `fpatan`, `fyl2x`) or through its C runtime
(`pow`, `acos`). OpenTPW evaluated them before through the platform's double `Math`, and now through DetMath. That is the same
double precision, now identical everywhere, and the change moved no pin. The x87 control word is 24 bits on the one simulation thread of the hardware renderer path (`COAST-032`; the software path is `[EXT:hardware-d3d-path]`, ECON-059 retired). The call sites:

| Site | Function | Original |
| --- | --- | --- |
| `LoanMath.MonthlyRepayment` | pow | the instalment `0xcb7a8` |
| `CoasterRating` swing | atan2 | fpatan `0x43c918` |
| `HermiteCoasterCurve.Orientation`, `SecondRailAngle` | atan2 | `0x41e24` |
| `CoasterTrack.TurnAngle`, `TurnAngleXZ`, loop knot axis, `TagRoll`, `PcOrientation`, `ApplyManualPose` | acos, sin, cos, atan2 | `0x36a50`, `0x31984`, `0x4324a0`, `0x43e590` |
| `CoasterCollision` angle sum | acos | the crossing test |
| `TourController` approach and steering | atan2 | the tour cars |
| `BumpMath.HeadingOf`, `SteerAngle`, `ReflectAngle` | atan2 | the bump module |
| `WalkerBodies.HeadingOf` | atan2 | fpatan `0x5570C2` |
| `HeadingAngle.Facing` | sin, cos | the turn `0x4FEF25` |
| `GuestSimulation` queue facing change | sin, cos | `0x4FFFF0` |
| `SideshowWin.HappinessGain` | log2 | fyl2x `0x4FE835` |

The start-up tables are built with DetMath too and equal the platform libraries' tables entry for entry
(`DetMathTests.LookupTablesBuiltWithDetMathEqualThePlatformLibrarysTables`; pinned in `LookupTablesArePinned`). They keep their
binary-evidence labels: the coaster and PC sine tables (`0xa3cc8`, `0x4708D0`), `OriginalSineTable`, the tour table, the bump
table `0x5442E0`, the slide's quarter sine, the bounce riders' table and the heading arctangent table `0x6E71B4`, whose 257 words
equal the executable's. Every loan offer's instalment is the same with DetMath as with the platform's `pow`
(`LoanInstalmentsWithDetMathEqualThePlatformLibrarys`). `RideMotion` (the unused prototype ride) uses DetMath as well.

Presentation keeps the platform functions: renderers, meshes, cameras, the sky and lobby, particles, the evening, the drawn boat and
kart poses (`TrackSurface`), the drawn pool waves and feet heights, the coaster car bend and rig, and the coaster tool's camera yaw
input. A difference there changes a pixel, not the park.

### The guard

`DeterminismTests.SimulationCodeUsesNoPlatformMathGuidsOrParallelism` scans every file below `VM`, `Scripting`, `Economy` and
`World` (all folders). It forbids `Math.`, `MathF.`, `double.` and `float.` elementary functions (sin to atan2pi, exp, log, pow, cbrt,
hypot, rootn), `Matrix4x4`/`Matrix3x2.CreateRotation*`, `CreateFromYawPitchRoll`, `CreateFromAxisAngle`, `Quaternion.Slerp`,
`Guid.NewGuid`, `Parallel.` and `.AsParallel(`. Exempt are:

- the presentation files and folders it lists (`PresentationMathFiles`);
- a line marked `[DET:presentation]` with its reason;
- the atomic writes' temporary file names (`Guid.NewGuid().ToString( "N" ) + ".tmp"`).

`PlatformMathGuardCatchesEachForbiddenForm` checks the forms and the exemptions.

### The browser check

`tools/web-determinism/run.mjs` runs the asset-free tests that pin simulation results inside the browser build. They cover the
economy, guests, pools, umbrellas, winter, the weather, rain mood, smarter guests, script mods, cheats, coasters, tours, bump rides,
track rides, staff, loans and DetMath. The page is published with `-p:OpenTpwDeterminismPins=true`, which compiles the tests into the
page's own assembly with the page's settings (the published page never sets it). The check runs the tests one by one in headless,
muted Chrome, where each test asserts the constant the desktop test asserts.

- Skipped: two tests that read the repository's own sources or shaders.
- Informative only: the comparisons with the runtime's own C library (`TestCategory("PlatformLibrary")`).
- Gate: the `Browser determinism` workflow runs the check on every pull request and on main, for Mono AOT (as published) and the
  interpreter.

The with-data twin is `CrossRuntimeDigest`: per theme it gives the browser digest's fixed park run (economy and guests), the same run
with one employee of every role, two park years of the level's weather, and the M4 gate's scenario as its list of state hashes. The
M4 scenario covers every supported ride, shop, sideshow and feature with its object scripts and guests, and the track rides'
stations. Run it on the desktop with `--determinism-digest [--out native.json]` and in the browser with tools/web-perf's
`determinism` scenario, then compare with `tools/web-determinism/compare-digest.mjs`.

### Results (October 19, 2026; macOS arm64, Chrome 155 headless, .NET 11 RC2)

| Run | Before DetMath | After DetMath |
| --- | --- | --- |
| Asset-free pins in the browser, Mono AOT (29 s of tests) | 631 passed, 0 failed (7 inconclusive for want of data, 2 skipped) | 632 passed, 0 failed (the new source scan is inconclusive without sources) |
| Asset-free pins in the browser, interpreter | 631 passed, 0 failed | 632 passed, 0 failed |
| With data, four themes, browser AOT against desktop: fixed run, with staff, weather, M4 (692, 692, 677, 659 state hashes) | identical | identical |
| The same with data, browser interpreter against desktop | not run | identical |
| Desktop pins (2,050 tests of the simulation classes) | pass | pass, no pin moved |

The desktop digests are the same before and after: jungle `ECB0C5BC6BFC46B9`, hallow `4B56CE11D7E43D7A`, space `69BBE374CB405BD0`,
fantasy `9DC0B98B9C6BB2E9` (fixed run); M4 `9689EA415EBCD10C`, `7E6304824ABCA32A`, `B272F3F44E867B4E`, `B50BBEAFFD13640E`. So the
browser already computed the desktop's bits for everything these runs cover, although the libraries disagree in the last bit for
thousands of inputs. DetMath removes that dependency, so a later change cannot start to depend on it.

Still outside these runs, and so not proven identical:

- the state that is not hashed (above: placed-object animator poses, the guest economy bridge map);
- a NaN that reaches hashed state, whose sign and payload differ between x86, ARM and WebAssembly.

The co-op hash check covers the unhashed state only once it reaches hashed state. The work a frame does outside the fixed tick is the
subject of the next section.

### Frame batching

For lockstep the park after a tick may depend only on the ticks and the commands, never on how the ticks fall into frames or on wall time.
`Level.Update` reads the world input and then runs `Level.AdvanceFrame`: the path layer, the pools' drawing, the evening, the snow, the scene
clock, the fixed-step simulation for the frame's time (0 to 16 ticks) and every entity's update. Each piece of that frame step outside
the simulation was audited (co-op phase C1b, October 20, 2026):

| Hook | Verdict | What it writes |
| --- | --- | --- |
| `RefreshPools` | presentation | Creates the pool, slide and spray renderers and, once, the swimmers' renderer. The slide rules' water levels are computed from the cell map by the simulation's own site (`Level.SlideSite`), not from the renderer. |
| `UpdateEvening` | presentation | The evening clock, look, sun colour and lamps, and the torches' flame transforms, which act on the drawn parts only (`OriginalObject.SetPresentationNodeTransform`); none of it is saved or read by the simulation (docs/EVENING.md). |
| `UpdateSnow` | presentation | Eases `SnowCover.Drawn` towards the cover. The cover a save keeps is stepped by the weather's turns inside the tick. |
| `SceneClock` | presentation | Read by the sky's scroll and the coasters' scrolling textures only; not saved. |
| `FlatRideBounceRiders.Prepare` | moved into the tick | Runs at the end of every tick (`Level.PrepareRideBodyPoses`), see below. |
| `StaffPlacement.Reconcile`, `StaffWorld.Synchronize` | moved into the tick | Run at the start of every tick, after its commands, see below. |
| `Entity.All` updates | presentation | The nine entity types with an update: the placed original objects (the list-pass flags, the key-table refresh and the drawn pose's reset to the stored pose, tp.exe `0x0044E410` → `0x0044E380` → `0x004726D0`, are presentation: the reset lives in a stash the simulation puts back before its next step, so saves, joins and picks see the simulation's own state; the ride hoarding's rise and fade, tp.exe `0x0044E410` → `0x004548F0`, advances its level by real frame time; the simulation only sets the hoarding's state and nothing it hashes or reads uses the level), the coaster renderer (meshes and cars from the track; its knot pass does not write the banks), the particle renderer, the pool, slide and bridge renderers (meshes, the drawn waves, the spray's water levels), the weather renderer and forecast presenter (their visual state and log lines) and the UI's root panel, whose panels edit the park only through the command funnel (`ParkCommandLayerTests`). `FrameBatchingTests.EveryEntityUpdateIsAnAuditedPresentationUpdate` fails when another entity type gains an update. |

**The ride bodies' pose phase** (`FlatRideBounceRiders.Prepare`) asks the guests' body players (`VisitorSpriteState`) for templates, creates and deletes
bodies and stores the walkers' headings. The body players are not hashed and the simulation never reads them, but a save keeps them, and run once per
frame after the frame's ticks they depended on how many ticks the frame ran. tp.exe runs the walk update (`0x00557AB0`) and the bounce pass once per
rendered frame after the frame's substeps (`0x0054E360`, `0x0054FA08`); OpenTPW now runs them after every fixed tick (`[EXT:fixed-60hz-clock]`). A
frame of one tick, 60 Hz at normal speed, is unchanged. A frame without a tick no longer runs the phase. tp.exe's walk update still runs on a frame
without a substep and reads the free-running scaled timer `0x785970` (`0x00557AFF` → `0x00402D70`); OpenTPW's phase reads the script and body
clocks, which only the ticks advance (`[EXT:fixed-60hz-clock]`, as before this change), so on such a frame it only repeated the requests of the frame
before: nothing changes except after a load or an edit between two ticks, when a body now follows at the next tick's end instead of in the same frame.

**The staff host's bookkeeping** ran once per frame before the world input. `StaffPlacement.Reconcile` returns a member left picked up with no hand
holding him (an imported save) to idle; `StaffWorld.Synchronize` puts a member without a position (hired since the last tick, or loaded so) on the gate
path. Both write hashed staff state. They now run at the start of every tick after its commands, so a replayed or a session's hire is placed as a live
one is, however many ticks a frame runs. `TryGetStaffWorldPosition`, which the HUD's locator calls, no longer synchronizes: a HUD read must not move
the park between two ticks. The live differences concern a member without a position (a hire made without the park view's place tool, which positions
the member at the clicked cell itself, or a member loaded so): it is placed at the next tick's start rather than at the next frame's start, so a path the
same frame's input built before that tick can change the spawn cell of a park without a walkable gate, and while the park is paused (no tick runs) it
stays off the map and out of the HUD's locator until the park runs again; an imported save's member left picked up, or a carried member dropped while the park is paused, is likewise released at the first tick, not at the next frame.

What stays outside: the clock's banked fraction of a tick and its dropped-time total follow the frames' time feed (in a session the network clock
feeds whole ticks, C2); the particle pool runs on the frame time, so the particle part of a save and the ADDOBJ records of effects that have ended
(`OriginalObjectRuntime.ParticleObjects`) and the ride hoarding's level and shown set (a sunk hoarding goes back to the closed set at the frame its level reaches 0, which a later model call reads) can differ between two players' saves, though neither is hashed nor returned to a script; and
`GameFlow.Update`'s work after the level's frame (the advisor, the autosave, the audio, the presence figures) is not part of this audit.

The tests:

- `FrameBatchingTests.TheParkAfterEveryTickIsTheSameHoweverTheTicksFallIntoFramesLiveOrReplayed` runs a scripted park without game data (objects,
  paths, guests, staff hired between ticks and a member left picked up, the speed changed to every setting, edits on both sides of frame
  boundaries) through the simulation alone and through `AdvanceFrame` in frames of 1 tick, 16 ticks and two seeded random sizes, with its edits
  issued live between frames and replayed from the recorded log inside the frames: the world hash after every tick, the command log and the
  park save at the end (less the clock's remainder) are equal in all ten runs.
- `FrameBatchingTests.TheStaffHostsBookkeepingRunsInsideTheTick` pins where the staff calls run.
- With game data, the park-resume smoke's restore phase (docs/PARK-SAVES.md) continues the saved Jungle park through `AdvanceFrame` in seeded
  random frames of 1 to 16 ticks (`OPENTPW_SMOKE_BATCHING=random`, the default; `1` and `16` choose fixed frames) and replays the continuation's
  edits (the speed raised and set back, the first attraction closed and reopened, the entrance fee) inside them; every tick's sections, the
  guests' body players included, must equal the capture phase's, which runs one tick a frame and issues the edits between frames.

## Park save

The economy-only park save (`ParkSaveFile`, still version 2; docs/ECONOMY.md
"Save format") carries the world generator as the optional `WorldGenerator` (a u32, the original's `mRandomSeed`) and has an optional `World` section:

```json
"WorldGenerator": 3290436124, "World": { "Seed": 6075451861746676596, "SoundSeed": … }
```

`RandomState` (kept for the format) holds the same number. `Level.SavePark` writes both. `Level.LoadPark` restores the economy and its
generator, attaches the guests and the script world to the loaded economy's generator, and, when the section is present,
continues the sound seed. Both delegate to `ParkWorldStreams` (`source/OpenTPW/World/ParkWorldStreams.cs`), which holds no
state and needs no renderer, so tests run the same save, load and hash code. Saves written before the port have no `WorldGenerator`
(their `RandomState` is the economy's own SplitMix64 state and `GuestRandom` and `ScriptRandom` in the `World` section are streams that no longer exist):
the load ignores those words and starts the generator from the saved world seed (`WorldSeed.GeneratorState`), or, for a save without a `World`
section, from `WorldRng.StateFromSeed( RandomState )`; both are deterministic, neither continues the old draws (`[EXT:legacy-generator-state]`: an old save has no generator state, so there is nothing of the original's to match). This legacy file does not hold guests, queues or
object script state, so a `LoadPark` keeps the running guests and scripts (DET-016). Coaster tracks, trains and
the coaster generator are not saved by it either (the own-format park save holds them, `[EXT:coaster-own-save]`).

The park-resume world save (docs/PARK-SAVES.md) is separate: its
`opentpw-world` envelope captures the whole running park — layout, guests,
script world, per-object runtimes, clock and the attraction→economy links —
and restores it into a fresh candidate park, continuing the run exactly. The
generator rides inside the embedded economy (`WorldGenerator`) and, as an optional marker, in the typed guest and script-world parts
(`GuestSimulationSnapshot.WorldGenerator`, `RideScriptWorldSnapshot.WorldGenerator`); the envelope's shape check requires the three to
agree, so a resumed park continues the one stream this page describes. A script's snapshot says how it draws
(`RideVMSnapshot.UsesWorldRandom`; an older one carries its own seeded generator, which the restore keeps); a head space of the world generator
holds no state of its own. Snapshots without the markers restore as before and leave the generator as the economy's restore set it. The
envelope's optional `Particles` part (the particle pool's emitters, effectors and
private generator, docs/PARTICLES.md) is presentation: it is neither hashed nor
drawn from any of these streams, so the hash schema stays 14.

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
| `world_rng_state` | `ParkEconomy.World.State` (the economy's, else the guests', else the scripts'): the one world generator, tp.exe `0x516330` | yes (schema 20; before it three stream states) |
| `coaster_rng_state` | `CoasterWorld.Random` (the original's `s·214013 + 2531011` LCG, started from the world seed, `[EXT:coaster-rng-seed]`) | yes (after the script digest, with the coasters) |
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
| (coasters) | `RideScriptWorld.Coasters`: the coaster generator, then every coaster in creation order: station ends, closed and complete flags, nodes (cell, height, t, pylon kind, stack index, station end, flags, bank, loop rotate, the exact position of the middle station node), controller state (initialised, the state 1/2/4/8, ride mode, speed setting, seats, speed cap, the lead car coordinate a reopened track keeps for the next train placement, net track money, time, whether the motion tick has run, braking start, station window, self-intersection count), samples (flags, height, roll, speed), rating, winch sections, trains (position, speed, train flags, boarding span, the riders of the lap and the guests seated at the station per car), the admission ring and the riders waiting to leave | yes (extra, after the script digest) |
| (tracks) | the track layer after the cell map (every track cell: type, mask, link, flags, piece, parent, rotation, overlap, segment) and `RideScriptWorld.Bumpers`: the cosmetic LCG, then every controller (type, laps, performance, phase, state, validity, tick count and clock, station, sections, waiting and leaving guests, every boat's slot, flags, position, velocity, heading, spin, target buoy, laps and rider count) — both only while a track or controller exists, so every park without one keeps its earlier digests and the schema stays 14 (TRACKS.md); bumper cars add the chase target and the drawn heading and yaw to their boats' fields (the same conditional extension: only while a controller exists, no schema bump, as for the water and kart fields); boats draw their aiming jitter from the script stream (`RideScriptWorld.NextWorldDraw`, DET-013) | yes (extra, after the coasters) |
| `economy_digest` | the park save's economy part as canonical JSON (keys in ordinal order, compact, as the default JSON writer writes it), hashed as text | yes |
| `input_log_cursor` | no input log | **missing** |

The economy digest's text is the park save serialized with the serializer's default options and written again canonically:
every object's members in ordinal order of their names, the parts the table below leaves out skipped at their place, and every
name and value as the default JSON writer writes it, so every character outside ASCII as an escape. It is hashed as text: its
length, then each character as a whole number. Since C2c-2b-4b (docs/COOP.md, "The digest's canonical text") a JSON writer
writes that text from the save's UTF-8 bytes (`WorldStateHash.CanonicalEconomy`) instead of from a JSON tree, and an ASCII text
is hashed a byte a step (`StateHasher.AddAscii`). The text and the hash are the tree's, byte for byte (`WorldStateHashCanonicalTests`
keeps the tree's form as its reference), and no pin moved.

The sound seed is saved with the park but is not part of the hash. In the PC
executable the chooser **advances**: every draw stores `s·0x19660D + 0x3C6EF35F` back
into the shared state (`0xfb1f20`; sites `0x6bc810`, `0x6bc680`, `0x6bc090`, `0x6bc170`,
`0x6bc343`, `0x6be450`, `0x6c3e00`) and uses `ror(s, 13)`; it is seeded once from
`QueryPerformanceCounter` (`0x72e09c`) and never saved. The earlier text of this
paragraph followed the Mac reading (a draw never stores its successor; only the
library init `0x100118C8` writes `CAudioPlaceHolder::mRandomSeed`), which contradicts
the PC instructions; the PC reading is used and the Mac reading is to be rechecked
(docs/AUDIO.md "Mac versus PC"). Because UI clicks and mixer-timed sentence segments now move the
chooser, its state depends on wall-clock events whenever sound is on: it is not a
replay guarantee. The park still saves it (`[EXT:sound-chooser-seed]`) and reseeds it from the world
seed at entry, so headless (`--mute`) runs and tests start from a known state, and it
stays out of the canonical hash and out of the resume smoke test's composite hash
(DET-plan §3 D9, §4.5).

The sounds of the October 18 finishing pass (the coaster trains' crash, the track and coaster tool clicks, the pause menu's
backdrop click, the rectangle ambients and the reverb) draw only from this sound chooser, as the original's sound library
does (all its draws are the one shared `0xfb1f20` state), and the reverb draws nothing. None of them touches the world stream,
the script stream or any hashed state, so no pin moves. The coaster crash is raised by the train collision exchange
(`CoasterRide.Crashed`) after the exchange has run; its subscriber only plays a sound.

A park started from an original save (SAVE-I, docs/ECONOMY.md "Original save
import") sets the economy's tick, bank, loans, objects and staff before its first
tick. All of it is in the economy digest, and the run that the pinned hash
covers starts no original save, so the pin is not affected by it. The
save's `mRandomSeed` is imported (SAVE-001 retired): it is the state of the one world generator (`0x516330`, saved raw at world `+0x1DA708`,
written at `0x517123` and read back at `0x517DFC`), and the park continues the saved stream. A saved WAIT4ANIM deadline
that is not ahead of the saved clock imports as 0 (October 18; it was -1, which the script snapshot rejects): the script's
hashed state differs from before until it next runs an animation opcode, and tp.exe treats 0 and a reached deadline alike
(`0x5538EB`). Only Jungle's shipped save holds such a deadline, and no pinned run starts from it.

Round 10 package F (saves import): the imported queue links (head, links, called guest), the bridge's called and letMeOnWritten words and called turn, the guests' queue fields, the state-15 exits
and the imported kart and water track cells and controller words join the hash for imported parks only. No corpus or Easymode save holds any of it (no guest in states 8/11-15, no set mFirstInQ,
no kart or water ride), so no pinned run reads it and no pin moves.

Other OpenTPW state that is **not in the hash**:

| State | Why it is outside |
| --- | --- |
| Placed objects outside their scripts: placement list and positions, `OriginalObjectRuntime` run flags (`stopped`, `wasRunning`), animator poses (node matrices, vertex and UV arrays, node-state bits) | not yet part of the canonical state; the scripts and the attractions' guest-facing state (queues, riders, `IsOpen` through the guest digest) are hashed |
| `GuestEconomyBridge` attraction → economy instance map | not yet part of the canonical state; the economy objects themselves are in the economy digest |
| `OriginalObjectRuntimeSnapshot.Hoarding` (the boards of a closed ride, docs/OBJECTS.md "Hoarding") | presentation: its level moves by real frame time, as tp.exe's `0x4548F0` does; it is saved for the look of a loaded park, but no simulation step reads it, and the park-resume smoke compares the runtimes without it |
| `FixedStepClock` pending time (`PendingSeconds`) | frame timing, below one tick; the substep scheduler is DET-I2 |
| Sound chooser seed | presentation, see above |
| `ParkSaveFile.Data.AnalyserCaches` (the analyser's cached readings) | saved for an exact continuation, but a reading aid that no simulation step reads; removed from the economy digest |
| `ParkSaveFile.Data.StatisticsHistory` (the monthly rings, including independent rating, of `ParkStatisticsHistory`) | only the park information window's graph reads them, no simulation step does; removed from the economy digest |
| `ParkSaveFile.Data.AnalyserChase` (the analyser's three misbehaving-kids words that a guard's chase raises, `ParkEconomy.MisbehavingKids`) | only the advisor's guard and prank lines read them (`AdvisorInputsCallbacks`), no simulation step does; saved when any is nonzero, removed from the economy digest, so the chases of a pinned run move no pin |
| `ParkSaveFile.ResearchData.RecentItems` (TPW buy-list stars) | only the buy list reads the four rings; just this nested presentation property is removed from the economy digest. Completed projects, progress, effort and every other research field stay hashed. Raw ring state remains saved; no schema or canonical pin changes |
| `ParkSaveFile.StaffData.RoleOrder` (locator role sequences) | presentation-only member-ID order for the five staff radios; only this nested staff property is removed from the economy digest. Members, training budgets, `HaveEverTrained`, strikes and every other staff/gameplay field stay hashed. The roster and global ThingPool order are not changed; no schema or canonical pin change is required |

A change to any of these excluded fields does not change the hash. Adding an
excluded presentation-only field does not require a schema or pin change. Bringing
one of these fields into the canonical hash requires bumping `SchemaVersion` and
re-pinning the fixed-run hash.

### The exit walk (October 20)

`GUEST-023` is retired: a guest a ride, shop, sideshow or toilet lets off now leaves as tp.exe's ExitRide makes it leave
([GUESTS.md](GUESTS.md#leaving-an-object-exitride)). Three things changed in the simulation, and every pinned run with guests
moved with them:

1. **The let-off is read once per park turn.** The ride update's dismiss (`0x4E1410`) reads `VAR_LETMEOFF` after the
   admission (state 0, `0x4E159A`) or the head step (states 1, 2 and 4, `0x4E0E27`). OpenTPW used to read it on every
   tick (`RideVisitorBridge.HostStep`). A use now lasts up to one park turn longer, and in the update that lets a guest
   off, the next one is called before it (`GuestTests.QueueFeedsLetMeOnInOrderAndReleasesThroughLetMeOff`).
2. **The guest walks out.** ExitRide (`0x5014E0`) puts it at `UsageInfo.ExitCellAppearPos` in the exit cell
   (`0x4DEDF0(1)`, the teleport `0x4FA930`), and state 15 (`0x500900`) walks it to the cell beyond the exit before it
   decides again. OpenTPW used to set it down on that cell and let it stand for `ExitRideSeconds`. The walk changes the
   guests' positions, the cell lists and the separation pushes, and with them every later draw.
3. **A blocked exit closes the ride** (`0x4DF150`) and keeps the guest on it. No pinned run has one. The synthetic parks'
   attractions have no placement, so they let off in their stand cell and route to their `ExitCell`. The same holds for
   the M3 and M4 gates (they lay no access cells), the Theme Park Inc slice and online visits (their cell maps keep no
   access-cell links). Only a level that types the footprints passes `ownExitCell`.
4. **A demolished object's riders** get the removal message's treatment (`0x4FB360`, `Unregister`). They no longer
   get the use routine at the demolition.

No schema or save field changed. The pools changing room's swimmers keep their `[EXT:pools-swim]` exit. The slide's
landing test (`PoolSlideActivity.IsLandingClear`) now reads a rider not yet updated this turn where that turn's step
takes it, so the test no longer depends on the update order. The later arrivals had exposed that dependence. The kept
body's position (`Guest.KeptBodyX/Y`) is presentation: it is not saved and not hashed.

Pins moved (old → new):

| Pin | Before | After |
| --- | --- | --- |
| `DeterminismTests.FixedRunHash` | `0x5D8385D13CC3BB9B` | `0xF3F895E3EF2F969B` |
| `PoolsFixedRunHash` (pool and changing room) | `0x2E9AA1D5A79E1FB2` | `0x347F0BA1F0DF5DFB` |
| pool cells | `0x8EE352E1FD1EEAB3` | `0x3BAFF5A07D0A82F6` |
| pool extras | `0x20DED2AC4BEC293A` | `0x5CFE6494C7706870` |
| `PoolsAttractionsFixedRunHash` | `0x2D7EE079E5A9B09F` | `0x1B5829884FE0659C` |
| `PoolSlideActivityTests.PoolsSlidesFixedRunHash` | `0xB6C14C9856DF363A` | `0x758CE71C17D724D0` |
| `UmbrellasFixedRunHash` | `0xE912E7E5D5B6E612` | `0x925B87221BE9AF53` |
| `RainMoodFixedRunHash` | `0x3697B9094432D152` | `0xBCD73D82A2F5D0D4` |
| `RainMoodWithUmbrellasFixedRunHash` | `0xADEE2FA6AFC2ACE3` | `0x2C8B850C069307BB` |
| `WinterColdFixedRunHash` | `0x9E74A906AB249401` | `0x0E6B528D92E00145` |
| `WinterColdEverythingFixedRunHash` | `0x45FCB3FC30C601FC` | `0x2BCAC8B1DC03F064` |
| `WinterFixedRunHash` (winter shops) | `0xA66540FF8C2AC6DE` | `0x8171CE2F7537C034` |
| `JungleDecemberOffFixedRunHash` | `0xF4A00471E60B9535` | `0xC9F860B54E30EB11` |
| `JungleDecemberWintryFixedRunHash` | `0x296DA4DA6A8BE323` | `0x071D0789F44BE723` |
| `WinterAlwaysFixedRunHash` | `0x5F767F69EF5CBF07` | `0x0C36671C5B99F0CD` |
| `SmarterGuestsFixedRunHash` | `0xE7D4BED7787E4748` | `0x1A54C3057DBEED2E` |
| script mods | `0x8AB18F8AC449CC49` | `0x995AD10275D35532` |
| script mods, every hook | `0x6B74FB3AE8A4937F` | `0x2FB7EFA85DBAD719` |
| `CheatsExtensionTests` fixed run | `0x5CBF8584199279AB` | `0x18440BAB5DB3D443` |
| `GuestMeasurementTests` scenario | `0x383ECBB8309B25FD` | `0xA7A17A97F4D7F7D7` |
| scenario, smarter guests (1.4.0 rules) | `0xF4F3CD74CD7253AB` | `0xBDC91FF7BA167AFF` |
| scenario, smarter guests with paths | `0xDA1A5CD6C526A112` | `0x37A002F5FE807C35` |
| scenario, food shop | `0x083802FC438DC19C` | `0x04442F9535C03C94` |
| scenario, bins | `0xF43AEA85CC6DB665` | `0x7539BB3BB9729317` |
| scenario, bins, 1.5.0 rules | `0x8E13F761512B0043` | `0xD2AF869CDF9A7D3D` |

The extension-off checks (umbrellas, winter, pools, rain mood, cold, online, mods, observer) compare against these
pins and moved with them. Tests that sampled a run's state at a fixed time take a later or another sample of the same
property:
- the smarter guests' planning and capacity tests;
- the pool swing, jets and bob tests (more swimmers, a whole wave cycle);
- the pools changing room tests (the guest is at the exit point and walks out).

### Imported queue cells (October 20)

An imported park now keeps the save's queue cells (map type 3) with their link byte, queue link, placement counter,
owner word and NoModify flag, as tp.exe's cell load (`0x4D0B30`, mode 0) reads every cell record into the map
(`ParkCellMap.FromOriginal`). Jungle Easymode has four, the Belly Bounce's queue. Before, those cells were empty
ground, and since the typed queue front (QUEUE-003, #540) the ride's front was such an empty cell, so no guest could
join it. No draw, hashed field or schema changed; the cell map holds four more queue cells. A run over the Jungle
cell map moves because the walk's edge test (`0x4D8750`) enters a queue cell only along its link, where the empty
ground was open to an off-path step (walk mask 1); with the edge test reading the four cells as ground again the
Jungle guest run returns to 22 admissions, which isolates that cause.

Pins moved (old → new):

| Pin | Before | After |
| --- | --- | --- |
| `PathAssetTests` Jungle guest run (22 → 23 admissions) | `0xBE549F10640102B9` | `0x11C1DCF60A32B061` |

### Staff locator role order (October 19)

The own save records five independent role-ID sequences in `StaffData.RoleOrder`. This presentation owner
is separate from member iteration and the global ThingPool used chain; neither gameplay traversal is
reordered to emulate locator order. Valid original role chains are preserved independently, while malformed
original chains and older own saves use explicitly documented [compatibility policies](PARK-SAVES.md#staff-locator-order-and-compatibility).
Role-order reads and captures consume no randomness and do not materialize a legacy fallback. A hire/fire
materializes that fallback before its intentional prepend/splice, without introducing a new random draw.

Canonical JSON removes only `StaffData.RoleOrder`, not the `Staff` object or another staff field. Changing
only locator order must leave the hash identical; changing gameplay data, including `HaveEverTrained`, must
still affect it (`StaffRoleOrderTests` pins that with `HaveEverTrained` as the example). `HaveEverTrained` is read only by the advisor's train-your-staff line and no simulation step reads it, so it could leave the hash like `StatisticsHistory` did; it stays in on purpose, because the role-order contract above and its test name it as hashed gameplay data and no pin sets a training budget, so dropping it later is a deliberate, separate change that moves no pin. `StaffRoleOrderTests` and `LocatorStaffRoleOrderTests` exercise this boundary and read/capture
immutability. Their C# execution awaits exact-head CI because no local dotnet SDK is available;
`tools/test_staff_role_order_contract.py` is a source-integration guard, not a substitute for those runs.

### Weather timing (October 14)

The weather schedule moved from day indices to game-clock timestamps (docs/AUDIO.md "Weather schedule",
AUDIO-026/027; tp.exe `0x511f70`, `0x51295c`): the constructor keeps the season's roll unapplied with
`lastChanged = 0`, the first update rolls a forecast at once, and the apply falls through to the roll
test in the same tick. The weather is not part of the canonical hash (it is presentation-side state saved
in its own snapshot, `ParkWeather.ParkWeatherSnapshot`), so no hash pin moves: the fixed-run pin of
`DeterminismTests` and the M3/M4 gate baselines were checked unchanged. What moved are the weather's own
expectations (`ParkWeatherTests`, `WeatherForecastTests`, `SnapshotJsonRoundtripTests`), which were
rewritten to the timestamp rules with the reasons in their messages, and the snapshot fields
`LastChanged`/`NextRollDay`/`ApplyDay`, which now hold game seconds. A resumed save written before this
change held day indices there; `ParkWeather.MigrateSchedule` recognises them (all three values within
`DayIndex + DaysOfWarning + DaysBetweenChanges + 1` of a park at least a day old) and multiplies them by
86,400 on restore, so no save version was bumped.

The weather's own draws still come from its own stream, not from the PC's world LCG (`0x516330`:
`world +0x1da708 = ror13(s·0x19660D + 0x3C6EF35F)`, the rotated value stored and `|value|` returned,
seeded with `time(NULL)` at world creation `0x51586b`, setter `0x516370`). That LCG is shared by 139
call sites and is now ported (`WorldRng`), but the weather's draws only land where the original puts them once the
draws of every other user come in the original's order (the park turn's sweep and the substep scheduler are ported, DET-013); the weather keeps its own
stream, OpenTPW's design (`[EXT:weather-stream]`, formerly AUDIO-030).

Rides register round (October 14): a script's speed bias (`RideVM.SpeedBias`, the ride's `InitSpeed`; 50 at creation) is
part of the script digest only when it differs from 50, and is carried by the script snapshot (default 50, so older
snapshots restore at the creation value). No pinned hash moved. The scripted-object run of the original Totem
(`TotemScriptTraceWithItsObjectIsPinned`) was re-pinned deliberately: Totem's `InitSpeed` is above 50, so its animation
waits (`out / script speed`) and its WAITs (`operand / script speed`, STP-PPC 0xb0658) are now shorter, which moves
every later timestamp of its 30 s trace by up to about 1.2 s.

Coaster register round 2 (October 14): the pylon heights (h0, h1, the stack offset) feed the hash, so `CoasterPylonRig` composes its node matrices with plain single-precision multiplies and adds instead of `Matrix4x4`'s operator, which may fuse the multiply-add on some CPUs. The coaster hash content changed shape (the trains drop the stop-and-dispatch state, last
departure and boarding clock; they gain the train flags, the boarding span, the seated-at-the-station lists, the admission
ring, the platform start, the station window, the intersection count and each node's loop rotate). No pinned golden or
resume hash in the suite depended on a running coaster's content, so none had to be re-pinned; the coaster tests assert
relations (boarded equals released, hash round trips, an old save migrates and loads). The shape of the
save moved with it, so a save of the earlier shape is migrated on load (stop-and-dispatch states, no ring, end node `0x144`,
no loop rotate) and its hash is recomputed in the new shape.

Coaster three-node station (October 15): the coaster hash content changed again. A coaster's station is three nodes (S0, S1, S2) built from the
station model's dummies and the end node of a finished track stands on S0's knot (COAST-029), so the node list gains two nodes, every flag word
of the station nodes changes, the middle node's exact position joins the hash, the sample list starts on S0 and ends with the closing pair's single
sample (COAST-019, `0x440c10`), and the samples, the rating, the braking start, the train placement and the station window (now read from the knot
coordinates of S0 and S2; the platform start is gone from the hash) all differ from the earlier two-node samples. The controller state (1 unfinished,
2 built or out of service, 4 open, 8 closed, COAST-012) replaces the closed flag in the hash. Any park with a coaster therefore hashes differently
from before; the synthetic parks of the suite have none. No pinned golden or resume hash in the suite depended on a coaster's content, so
none had to be re-pinned; the coaster tests assert relations. What was re-pinned deliberately: the test station of `CoasterTests` is a two-cell
`<>` station whose last node S2 stands where the single station node stood (so the first link of every circuit has its old length and the pinned
costs and windows hold), `CoasterTests.First` (= 3) is the index of the first pylon, the closing target is S0's cell, a built ride runs no station
step until its script opens it (state 2), the dispatch fixtures open and close it as the script does, and the station window test reads the window
from the knot coordinates. A save of the two-node shape is migrated on load: the placement's three nodes stay, the pylons follow, the end node returns
onto S0, the circuit is sampled again and the riders wait to leave because the train placement of the new sampling is another one; its hash is
recomputed in the new shape.

Coaster trains (October 15, 2026): the coaster hash content changed again, in the values it holds rather than its shape. A train's position is
now its reference car's, the middle car (n − 1) >> 1 (tp.exe train `+0x74`, `0x43baef`), with every other car at its offset from the car types'
extents (`0x43bc20`), where it was the front car's with the cars 7 units apart; a placed train's last car stands the release time ahead of the
train before it (`0x43c1f0`) instead of at an even share of the lap, and it starts at the mean rated speed under its cars (`0x43cbda`); the hops of
a frame run in the remaining-budget queue of `0x43ced8` instead of train order, and the half-sample point after the last sample reads the last
sample (slot n, `0x441783`) instead of sample 0, so the positions, speeds and flags of a running coaster move differently. The seats are split
over the centre-type cars only (`0x43ba90`), so riders land in other cars, and the capacity (`COAST 6`) starts at and is bounded by one train's
head sockets (`0x436a61`, `0x43b330`). The auto bank runs its speed estimate through the station end nodes (`0x4310c8`), so the node banks and
the sample rolls of a banking coaster change. The controller adds the lead car coordinate (where train 0's last car stood when the trains were
taken off, the start of the next placement), and a ride whose trains are taken off now drops the braking start and the station window with its
samples (the next rating rewrites both before a train runs), so a reopened coaster that is saved and resumed hashes as the one that kept running.
Any park with a coaster therefore hashes differently from before; the synthetic parks of the suite have none. No pinned golden or resume hash in
the suite depended on a coaster's content (the Determinism, PathBuilder, PathAsset and park-resume tests pass with their pins unchanged), so none
had to be re-pinned; the coaster tests assert relations (the release walk, the hop queue order, the round trip of the lead car coordinate). A save
from before the car layout holds the front car's coordinate and no reference car; it is moved back to the reference car on load.

Coaster pylons and track tags (October 16, 2026): the coaster hash content changes for coasters built from catalogue entries, which now
sample and rate on the knots of the pylon models' posed track tags (`0x430ed0`, `0x4344c0`, `0x430590`) instead of node positions (for
example Gorilla Thrilla's excitement 18.20 → 18.78, Chac Atak 143 samples instead of 140); a nonzero `fTrackHeightAdjust` now only lowers
the drawn cross section (`0x42d6d0`) and no longer reaches the simulation; suspended coasters' node flags gain `0x10000000`. The pinned
run hash is unchanged (the synthetic park has no coaster), and a coaster without pylon models hashes as before (`0xDB31A60AFE5F1CD6` in
an A/B against main). The coaster save gains the optional `KnotPass` (null or 2); a save without it, for a coaster with pylon models,
re-places its trains.

Coaster rolls (October 16, 2026): the coaster hash content changes for every coaster, with and without pylon models: a sample's roll
(`CoasterSample.Bank`, hashed with the samples) is now the angle of the second rail's point about the sample's tangent (`0x440c10`) instead of
the interpolated node banks, so it is zero up to the chords' sag except on loop pylons and suspended coasters (whose swing reads it), the rating
step reads these rolls, and a coaster's rating, sickness and excitement can move with it; the car poses (not hashed) take the slice roll of
`0x43e6b0`. The earlier A/B hash of a coaster without pylon models (`0xDB31A60AFE5F1CD6`, above) therefore no longer holds. No pinned
golden or resume hash depended on a coaster's content (the pinned run has none) and no test pins a coaster digest, so none was re-pinned;
the coaster save gains no field, the samples are re-derived on load, and a save from before places its trains as it did.

Rides register round 2 (October 14): upgrade completion now sets the script speed bias to the new level's `InitSpeed` when
above 0 (tp.exe 0x4DF8F0; `OriginalObjectRuntime.TryApplyCatalogueTierDefaults` with `completion`, called from
`Level.ApplyCompletedCatalogueTier` only; a resume or import still seeds the tier without touching the bias, since the saved VM
state restores it). The bias is already in the script digest and snapshot, so no schema change is needed, and no pinned
hash moved: the fixed run's synthetic park completes no upgrade, and the upgrade tests assert the bias directly
(`CompletedTierSetsTheScriptSpeedBiasToThatLevelsInitSpeed`). A saved park that completed an upgrade under the older build keeps
the bias it saved; only upgrades completed after this change move it (a ride that speeds up changes the timing of every later
`WAIT` and animation trigger of its scripts, so such a run does not replay a recording made before). The other changes of the round
(object names, shape symbols, the 12-byte vertex poses, the low-detail texture folders, `coaster.sam` as its own settings) touch no
simulation state; the pins they moved are presentation and data pins: the catalog "named" counts, the MD2 corpus table-byte and
unsupported-flag counts, the vertex-track census (609 clips play, none limited) and the frozen sign baseline (seven signs whose
name changed; the five newly named signs are pinned in `RidesRound2CorpusTests`).

Track register round (October 14): the kart and water controllers tick every 31 ms (`BumpController.TickSeconds = 0.031`, was 1/30 s) and a
launched boat or kart starts at heading 0 (was the station's flow), so every park with a track controller runs a different
trajectory than before: the golden boat run of `BumpControllerTests` (positions, velocities and headings from tick 1, the
unload at tick 618 instead of 610) and the kart grid test were re-pinned deliberately, and the park-resume continuation hashes
of a park with a boat, kart or bumper car move with them. No `SchemaVersion` bump: the hashed fields are the same ones and the
track layer and the controllers still join the hash only while one exists (the conditional extension above), so no park without
a controller changes and the pinned fixed run (`0x776A2A9E9A53BF25`, schema 17) is untouched. The new per-car `Smoke` and the
boats' `Engine` flag are presentation state: saved (trailing optional fields, so older saves restore) but not hashed. A save from
before the tick change may carry a tick clock up to 1/30 s and still restores (`BumpController.MaximumSavedRemainder`).

Tour register round (October 15): the tour controllers tick every 62 ms (`TourController.TickSeconds = 0.062`, was 31 ms: the original runs the tour tick on every second world step), so every park with a tour
ride runs different flights than before; the park-resume continuation hashes of such a park move with it. A free car slot's stale words (heading, patrol corner, approach heading, seat ids) now join the
tour part of the hash and are saved (trailing optional `FreeSlots`, and every seat word of a live car), a handed-out seat keeps its id, and a ride closed after a departure order keeps its car docked until it
reopens, which changes a closing run. No `SchemaVersion` bump and no pinned hash moved: the tour module still joins the hash only while a controller exists, and nothing pins a park with one.
A save from the 31 ms clock still restores (`TourController.MaximumSavedRemainder`, 62 ms). The wall-clock catch-up of the original's world loop (2 s clamp) and the parity phase of the tour tick
against the script loop are not reproduced (`[EXT:tour-fixed-step]`). The drawn pose of a car (`TourCar.Drawn`, the head-node shuffle) is presentation state: not saved, not hashed, and it draws nothing from any stream.

Tour ground (October 18, TOUR-005): a tour car's ground (`TourWorld.CellGround`) now reads a track tile's own piece height map The piece lookup is the level's own (`Level.TourGroundOf`, built from the family's first station), never the drawing's track renderer, which exists only after a frame has drawn the tracks, so the cars fly the same with or without rendering.
(`0x00452AE0` asks `0x0053BF30` before `0x00527E80`), so the target heights and the clearance choices of `TourController` over kart
and water track tiles change, and with them the tour part of the hash and the saved controller state of a park with a tour ride over
such tiles. No pinned hash moved: nothing pins a park with a tour. The walking bodies of TOUR-011 are presentation state (not saved,
not hashed).

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

## Mods

Extensions and mods (MODS.md) are resolved at startup, and nothing changes while a park simulates: the files-based mods stay as resolved for the whole run, and an official extension
switched afterwards (the Extensions and mods screen, or a park load, [below](#switched-parks); `[EXT:extensions-apply-at-load]`) takes effect only for the next park built, never for a running one. With no files-based mods
and the default extensions nothing in the simulation changes: the online extension `opentpw.online` touches no
simulation state, and `DeterminismTests.TogglingOnlyTheOnlineExtensionKeepsThePinnedDigests` runs the pinned fixed
run (`FixedRunHash`) and the guest-pathing run with it on and off and gets the same digests. The M4 gate and the
browser digest need the original game data and run outside the unit tests, so they are not part of that test; they
read no extension switch.

Park saves record the mods that can change the simulation (`ParkResumeSnapshot.Mods`, `ParkModRecord`: id, version and
content digest). An extension that sets `AffectsSimulation` is recorded with the digest of no bytes; a files-based mod is
recorded when it adds objects (`content.objects`) or overrides a file that is not plainly presentation media (textures,
sounds, text and fonts; an unknown file type counts as simulation). The field is **null, and left out of the file, when
nothing of that kind is active**, so a park saved without such a mod is byte-identical to one from before mods existed.
Online and presentation-only mods are never recorded. Loading (`ParkResumeSaveFile.ValidateSnapshot`) needs every recorded
mod active in the same version with the same content digest, in the same order, and no other simulation-changing mod
active; an extra presentation-only mod is fine. A missing mod, another version or another digest refuses the load with a
named reason such as `needs community.tpi-rides 1.0.0`. The load and Continue menus keep such a save listed and add
"requires mod X" instead of hiding it. The shape check bounds the list (256 entries), the ids, versions and lowercase hex
digests, and every `mod:<id>:` key of the asset manifest must name a recorded mod. A build from before this field rejects a
save that has it (the codec refuses unknown members), which is intended. `ParkSharing.ExportLevel` refuses to export while a
simulation-changing mod is active, because a park package records none yet.

The smarter-guests extension (`opentpw.smarter-guests`, [SMARTER-GUESTS.md](SMARTER-GUESTS.md)) keeps its own state (park seed, birth-id counter, each guest's birth id) apart from the guest snapshot and
from `GuestSimulation.ComputeStateHash`; it joins `WorldStateHash` only while the extension is on, so the off-path hash and `SchemaVersion` (21) do not change, and it is saved in the optional
`ParkResumeSnapshot.SmarterGuests` section (null and omitted while off). The gate is read once when the guest simulation is built. With the delegating policy of version 1.0.0 the guests and
the `WorldRng` stream equal the off run's tick for tick; `DeterminismTests.SmarterGuestsFixedRunHash` pins the on run's world hash. Version 1.1.0 (value for money) gives the policy its first rules and the state a count of consecutive price-emptied decisions per guest (`PriceStalls`, saved as `Stalls` in the section in birth-id order, and hashed only while it holds an entry). `SmarterGuestsFixedRunHash` did **not** move: the synthetic park's one drinks shop is cheap enough that no guest is ever refused at its stand, so the rules never act in that run, and an on run still equals the off run guest for guest (the test that says so stays, now over an identity-only policy where it needs the original's decisions). The rules show in `GuestMeasurementTests`' `ScenarioSmarterFixedRunHash` (0x5680EC44DBB01974, the `ParkScenario` run of 12,000 ticks with the extension on), which differs from the off pin because the scenario's dear shop turns guests away. The script-mod pins run over the original policy (no smarter extension) and are unchanged. A deliberate change of a rule constant changes the scenario pin and bumps the extension's version. Version 1.2.0 (spreading, phase 3) adds the attractions' throughput estimates to the state (`RideThroughputs`, saved as `Throughputs` in the section in attraction-id order, and hashed only while it holds an entry; integers only). **Both on-path pins moved, on purpose:** `SmarterGuestsFixedRunHash` 0xCB2F2ECD287ED83F → 0xC8534726E08D0EC2, although the synthetic park's guests are still the off run's guest for guest (`GuestSimulation.ComputeStateHash` is equal; there is one attraction, so there is nothing to spread) — what changed is that the drinks shop's estimate (its average, samples and silence clock) now joins the world hash; and `ScenarioSmarterFixedRunHash` 0x5680EC44DBB01974 → 0xC0344925E23A32C1, because the scenario's four rides now see each other's expected waits and the preference and hysteresis act. The expected-wait term, the preference hash and the hysteresis are integer arithmetic (the floating point is the original's own score code, whose cross-platform pins already hold) and do not read a clock or a generator. `AResumedParkContinuesLikeTheUninterruptedOneWithTheExtensionOn` includes the estimates: a resumed park hashes like the uninterrupted one every tick and its estimates end equal. Every off-path pin, the script-mod pins and `ParkScenarioFixedRunHash` are unchanged. Version 1.3.0 (queue patience, phase 4) adds no state: the patience is a stateless trait (`Trait( guest, 2 )`), the rules are integer arithmetic over the phase 3 estimates and the host's facts (`AdmissionView.BestAlternativeWait256`, `QueuePatienceView.BestAlternativeWait256`, pure host computations that take no draw and change no state), and a refusal or a give-up goes through the host's existing queue-too-long and dismissal paths. **Neither on-path pin moved:** `SmarterGuestsFixedRunHash` (0xC8534726E08D0EC2: the synthetic park has one ride, hence no alternative) and `ScenarioSmarterFixedRunHash` (0xC0344925E23A32C1: in the scenario's 12,000-tick run of seed 1 no guest is refused or gives up, so the guests end exactly as under 1.2.0; the rules act in 2 of the 8 extended-report seeds, which end in a different hash). An earlier build of 1.3.0 moved the scenario pin (to 0x83140C77CD575BE1) because it refused guests wrongly (see SMARTER-GUESTS.md "What the first build got wrong"); that value is obsolete. The four level parks, which have one ride, end in the same state hash as under 1.2.0 (measured). Every off-path pin, the script-mod pins and `ParkScenarioFixedRunHash` are unchanged. Version 1.4.0 (planning needs, phase 5) adds the guests' meal times to the state (`MealTimes`, saved as `Meals` in the section in birth-id order, hashed only while it holds an entry; integers only) and three rules that read host facts the host computes without a draw (`GuestView.MealTurn` and `PlanTurns256`, `AttractionView.CycleTurns256` and `OnTheWay`); the projection of a meter is `NeedsUpdate.GrowthSteps` times the needs block's own growth, float adds of small integers that the guest hash's cross-platform pins already rely on. **Neither existing on-path pin moved**, and that is the result, not an oversight: `SmarterGuestsFixedRunHash` (0xC8534726E08D0EC2) is the synthetic park, which has one ride and no food shop, and `ScenarioSmarterFixedRunHash` (0xC0344925E23A32C1, unchanged since 1.2.0) is the default `ParkScenario`, where nobody eats and no grower is near a level at a decision, so the rules never act (seeds 1 to 8 of the extended report differ from 1.3.0 in one seed out of eight, by planning). `GuestMeasurementTests` shows the phase 5 rules switched off (`SmarterRuleSet.None`, `new SmarterGuestDecisions( rules )`, which the report's `--guest-rules none` passes to each simulation) reproduce 1.3.0's scenario pin. A **new** on-path pin covers phase 5: `ScenarioFoodSmarterFixedRunHash` 0x336E044598FE1EBB, the `ParkScenario` run of 12,000 ticks with its food shop (`ParkScenarioOptions.FoodShop`, off by default so the other scenario pins stand) and the extension on; it differs from the same run with the rules off, because guests who have eaten score a thrill ride lower. The world hash of that run includes the meal times. `AResumedParkContinuesLikeTheUninterruptedOneWithTheExtensionOn` includes two meal times: a resumed park hashes like the uninterrupted one every tick and its meal times end equal. Every off-path pin, the script-mod pins and `ParkScenarioFixedRunHash` are unchanged.

Version 1.5.0 (clean and quiet paths, phase 6) adds the complaint times to the state (`Complaints`, saved in the section in birth-id order, hashed under the marker "CMPL" only while it holds an entry) and takes **no `WorldRng` draw of its own**: the host runs the original's wander search unchanged (`GuestWander.Pick` with its draws, now also copying the cells of the walk), the policy picks a candidate cell (the walk's path cells other than the guest's own, the linked cells beside its goal, the goal) and a complaint, and the host thinks the thought, takes the happiness and notes the turn. The crowd it reads is the host's per-cell guest list (rebuilt from the guests on a restore) and the litter is the economy's litter map (saved with the economy), so a resumed park sees the same ones. Main's per-cell list change (PR #401, head insertion) moved the off pins to `FixedRunHash` 0x5D8385D13CC3BB9B, `ParkScenarioFixedRunHash` 0x383ECBB8309B25FD and the script-mod pins; this branch keeps main's values for every off-path pin and for the pins of rule sets without the path rules (`ScenarioSmarterFixedRunHash` 0xF4F3CD74CD7253AB, the 1.4.0 rules, which `SmarterRuleSet.Phase5` and `None` still reproduce). **Three on-path pins moved, on purpose, because the path rules act in those runs:** `DeterminismTests.SmarterGuestsFixedRunHash` 0xF0B3F8366E2B73E4 (main, 1.4.0) to 0xE7D4BED7787E4748 (the synthetic cross park crowds, five guests in one cell, so its guests complain and the wander goal moves; `SmarterGuestsPathRulesActInTheSyntheticParkAndItsPinnedRunDiffersFromPhaseFive` counts both in the pinned run, and the test that says an on run equals the off run guest for guest now runs over `SmarterRuleSet.Phase5`); `GuestMeasurementTests` scenario with the extension 0xF4F3CD74CD7253AB to 0xDA1A5CD6C526A112 (new constant `ScenarioSmarterPathsFixedRunHash`; the old value stays as the 1.4.0 pin that `Phase5` reproduces); and the food-shop scenario with the extension 0x30C8AF969CFEF592 to 0x083802FC438DC19C. The litter variant of `ParkScenario` is off by default and gives its shops no litter effect then, so the baseline is untouched.

Version 1.6.0 (litter bins, phase 6b) adds **no state** (nothing in the `SmarterGuests` section, nothing hashed, no validation, nothing the save refuses beyond the version check: a 1.5.0 park is refused with `needs opentpw.smarter-guests 1.5.0, not 1.6.0`) and takes **no `WorldRng` draw of its own**. The host's bin search (`SeekBin`, tp.exe `0x00500DC0`) asks the policy for its radius only when the park has bins and a guest's litter meter is full (`IGuestDecisions.UsesBinCarry`, `BinSearchRadius`: eight cells against the original's three, accepted from 0 to 16). A guest that now carries its litter to a bin does not take the drop's draw (`1 + rand % 5`, taken only when the guest drops), so a park in which a bin is within eight cells and out of three of a litterer draws one word fewer per such guest and diverges from 1.5.0's from then on; a park with no bin in reach of either radius, and every park without bins (the four level parks, every scenario but the bin variants, every save the gate builds), draws and hashes exactly as 1.5.0 did (`SmarterGuestsBinsTests.AParkWithoutABinInReachRunsLikeVersion150`). **Pins:** no existing pin moved (none of them has a bin); new `GuestMeasurementTests.ScenarioBinsSmarterFixedRunHash` 0xF43AEA85CC6DB665 (the litter variant of the scenario with a food shop, four bins on the street, a litter effect of 25 and a handyman proxy every 400 turns, 24,000 ticks, seed 1, all rules on) and `ScenarioBinsPhase6FixedRunHash` 0x8E13F761512B0043 (the same run with 1.5.0's rules, which equals the run without bins). A park resumed while a guest walks to a far bin continues like the uninterrupted one (`SmarterGuestsBinsTests.AResumedParkContinuesLikeTheUninterruptedOneWhileAGuestCarriesLitterToABin`: the guest's bin id and walk are in the guest snapshot already). The litter-aware routes of the prototype (not shipped) left the original's search untouched and only refined its result through a null-by-default delegate, so with the rule off the route was byte-identical.

Script mods ([SCRIPT-MODS.md](SCRIPT-MODS.md)) keep their `state`, failure counters and disabled flag in the optional `ParkResumeSnapshot.ScriptMods` section (null and omitted while no script mod is on) and join `WorldStateHash`
only while one is on, so the off-path hash and `SchemaVersion` (21) do not change; the section needs the guests' identity state (the `SmarterGuests` section, shared) and the behaviour and API versions it was saved under, and a
park of another version is refused. A script's `rng` is a stateless hash of (park seed, mod id, hook, guest birth id, park turn, draw ordinal), so it never moves the `WorldRng` stream. Two pins hold the interpreter and the wiring
on every platform: `DeterminismTests.ScriptModsFixedRunHash` (the shipped example mod) and `ScriptModsAllHooksFixedRunHash` (all four hooks, state and `rng`); a resumed park continues equal to the uninterrupted one
(`ScriptModsSaveTests.AResumedParkWithScriptModsContinuesLikeTheUninterruptedOne`).

### Switched parks

`[EXT:save-extension-switch]` (user-approved 2026-10-19, "Ook ontbrekende extensie aanzetten"): a player who loads an own park save whose official extensions differ from the active ones is asked
instead of refused, in two cases only (MODS.md, [Loading a park with other extensions](MODS.md#loading-a-park-with-other-extensions)): a **rule-only** extension (`ParkExtensionSwitch.RuleOnlyExtensionIds`:
`opentpw.rain-mood`, `opentpw.winter-weather`, `opentpw.winter-cold`, `opentpw.winter-always`, `opentpw.smarter-guests`) that is on now and was not then, off now and was then, or of another version,
continues under the active set; a **content** extension the park uses that is off now (`ParkResumeMods.UsedOnlyExtensionIds`: pools, umbrellas, winter, cheats and the evening extension with its torches) is switched on for the run
(`[EXT:extensions-apply-at-load]`) and in `mods.json`. The confirmed switch rewrites the loaded snapshot for the switched set (`ParkExtensionSwitch.Apply`): its `Mods` become the records a park saved
now would hold, the smarter guests' section starts fresh (the park seed, no birth ids yet) when that extension comes on, and loses the extension's own state (stalls, throughputs, meals, complaints;
the guest identity script mods share stays) when it goes off or continues in another version (whose rules wrote that state with their own meaning). The rewritten snapshot then passes the **unchanged strict validation**. A rule's per-guest day marks (`RainMoodDay`, `ColdMoodDay`,
`WarmUntilDay`) and the weather it shaped stay in the guest and weather state as they were; every build reads them with the rule on or off.

**A switched park is a new run from the switch point.** It is not a replay of the saved run: the rule set that drives it from that tick on is another, so its state hashes from then on are not comparable
with a run that kept the old set, and its next save records the set it now runs under. Nothing about it is pinned; a park whose sets agree takes the strict path, so every existing pin, save byte and resume
test is unchanged (a switch is only ever made from the interactive load dialog; `ResumePark` without a confirmed switch, `--park-resume-smoke`, the tests, the online server and co-op, community
mods of another version or content, the cross-edition objects extension and the original-format export stay strict). A content extension switched on reproduces the save exactly, because the park
already used it: `OPENTPW_SMOKE_SWITCH=pools` on the restore phase of the native park-resume smoke loads the pools variant's save with the extension switched off and back on through the dialog's path
and compares every continuation tick with the capture phase (`SaveExtensionSwitchTests` covers the planning, the rewrite, the dialog and the revert of a failed load headless).

A run with a mod that brings Theme Park Inc rides is as deterministic as any other: the M3 gate with such a ride as its attraction gives the same guest and state hashes in two runs
(`TpiRidesEndToEndTests`, over the install named by `OPENTPW_TPI_PATH`), and the extension is recorded in the save, so a park saved with it loads only with it.

The asset manifest names the content root of every archive member: `data:` for the game data, `bonus:` for the official
bonus content and `mod:<id>:` for a mod (`ContentRoot.KeyPrefix`, `ObjectCatalogEntry.Root`). A mod's override of a
game-data path keeps the `data:` key, and its changed hash makes the existing manifest comparison refuse a mismatched park.
The object catalog's cache key includes `ModSet.Digest`, so a catalog is never shared between two sets of files-based mods.
`ModSaveTests.EmptyOnlineOnlyAndPresentationOnlyModSetsKeepThePinnedDigests` runs the fixed run and the guest pathing under an
empty set, with only the online extension toggled and with a presentation-only mod, and checks the same digests and no
record; the M4 gate and browser digests read the same code paths and are checked by the smokes.

## Pools

The `opentpw.pools` extension ([POOLS.md](POOLS.md), `[EXT:pools-save]`) adds the pool water cell type (`ParkCellTypes.PoolWater`, 0x40). Pool cells are ordinary non-empty cells of the cell map, so they join
the canonical hash through `ParkCellMap.AddCanonicalState` with no schema change (the schema stays 21), and a park without any keeps every pin: `PoolsTests.TogglingThePoolsExtensionKeepsEveryExistingPinWithoutPoolCells`
runs the pinned fixed run (`FixedRunHash`; the default extension set now includes pools, so every test that builds no explicit set runs with pools on and the pins prove the no-pools guarantee) and the guest-pathing run with the extension on and off (asserting the gate really differs) and gets the same digests. The extension is recorded in a park save **only while the park
has pool cells** (`ParkResumeMods.UsedOnlyExtensionIds`); a park without them saves with no `Mods` field and the same bytes whether the extension is on or off (`PoolsLevelTests.RealCapturesWithoutPoolCellsAreByteIdenticalWithTheExtensionOnAndOff`,
two real `Level.CaptureResumeSnapshot` captures).

Guest code does read pool cells, in two places, and neither changes anything in a park without them: `WalkEdges.Blocked` blocks every step onto a pool cell for every walk mask, and `GuestWander.OffPath` skips a random
candidate in the water (its draws are unchanged). A pool cell also scores 2 park-value points, like empty land (`ParkValueRules.CellPoints`). Pool *drawing* (the render-only holes of the ground mesh, the meshes) is
presentation and reads nothing the simulation hashes.

**Swimming (P2).** A park with a changing room whose guests swim has its own pin, `DeterminismTests.PoolsChangingRoomFixedRunHash` = `0xA83721661FA72F69` (it was `0xB5B033AE20AD9BA0` before the hiring pool's candidates kept their sprite variant, which the digest's park save holds; **the spacing work then moved it from main's `0x42E916DFC15CED19`**: bodies keep 0.7 cell apart and steer from 1.2 cells, every body in the water is an obstacle, and a pool takes one body per water cell, at least 2, so this 12-cell pool fills at 12 instead of 24; 90 swims and 129 admitted instead of 91 and 130; see POOLS.md, "Spacing of bodies in the water"): the same fixed run on a pool `PoolBuilder.Rectangle((3,8),(8,9))` with a changing room hanging below path cell (5,5) (pool-side cells (5,8) and (6,8)); 91 guests swim, 24 at once, 130 are admitted. (The first pin, `0xA3A5D45575B07527`, was taken before the review fixes: the pool then took any number of swimmers, and the swimmers did not count in the park census. A pool now takes at most 2 swimmers per water cell, at least 4 (`PoolSwimRegion.Capacity`; this pool of 12 cells fills at 24 and a guest released while it is full leaves the room on foot), swimmers and guests changing back count in the guest census, and a guest leaving the session is a walker again before it is placed. A guest refused by a full pool takes no draw. `PoolsFixedRunHash` and `FixedRunHash` did not move.) A swim takes exactly **one** draw of the world generator (`BeginSwim` seeds the swimmer's own xorshift32 stream, `SwimRandom`); all steering, length, stroke and goals come from that stream, and positions are 16.16 integers with a 64-entry heading table. The swim fields join the canonical hash only for a guest inside a session, so a park without a room (or with pools and no room, `PoolsFixedRunHash`) is unchanged and takes no extra draw. Swimmers are kept out of the walkers' cell lists. A mid-swim save and load gives the same hash (`PoolsSwimSessionTests`, native `OPENTPW_SMOKE_POOLS=1` smoke).

**Pool extras (P3).** `DeterminismTests.PoolExtrasFixedRunHash` = `0xA07E1347C234D5E1` (**it moved from `0xEEEDBDE12564B670` when the ladder and the lounger started to climb out on the spot**, and from `0xCE55DFBF312FF20A` when the ladder model's rails moved to the wall and the foot point from 0.8 to 0.78 cell: the swimmer now swims to the foot of the ladder at the wall, climbs for 8 turns without moving, steps over the coping in its own step and the ladder, lounger, jump and splash steps are renumbered, so the positions, steps and turns of every ladder and lounger guest in state 25 changed; 92 swims, ladder 10, lounger 10, board 8, shower 9 guests; the board and shower are untouched and every other pin stands; before that it was `0x0C35A6A0F10D914E` before the hiring pool's candidates kept their sprite variant, which the digest's park save holds; **the spacing work then moved it from main's `0xD8806A8911B990C7`**, for the reason of the changing-room pin plus the ladder, lounger and board landings waiting for a free zone: 87 swims, and each activity is done by 8 to 12 guests): the swimming run above with the four pool activities registered and extras set around the pool (a ladder at (4,7) and a board at (7,7) facing the water to the south, a shower at (4,6) beside the room, a two-seat lounger at (4,10) and (5,10) facing north); 94 swims, and each activity is done by 12 guests. The pin moved from `0x15A2596129C01546` when the P2 review fixes were merged and the diving board stopped counting `PoolActivityTurns` during its dive (the turn count is part of the digest in state 25); nothing else changed in the offers (the two-pass cadence draws exactly as the one-pass did). The host draws only from the swimmer's own stream, and only while an activity has an offer: with an empty registry, or no extra placed, no draw and no hash change (`PoolsActivityHostTests`, `PoolsExtrasTests.WithoutAnExtraPlacedTheActivitiesDrawNothing`), so `PoolsChangingRoomFixedRunHash` and every other pin stand. The activity fields (id, target, phase, turns, point) join the canonical hash only for a guest in state 25.

**Water slides (P4).** A park with a water slide has its own pin, `PoolSlideActivityTests.PoolsSlidesFixedRunHash` = `0xB6C14C9856DF363A` (it was `0x5B4437FD295E3DFF` before the spacing of bodies in the water: the swimmers on their way to the stairs keep 0.7 cell from every body and the pool takes one body per water cell; six still ride, three wait at the busiest moment): the swim session's test rig (a changing room, a pool of 6 x 6 cells, the paths and queue of `QueueTests.Rig`) with a tower at (3, 5) and a seven-piece slide into the pool cell (6, 3), six guests sent in one after the other (60 park turns apart, capacity 3) and 2,160 park turns in all; six of them ride and (since the spacing) three wait in the tower at the busiest moment (earlier versions of the pin: `0x91710DEE845F2503`, before the host's offer interval went from 45 to 90 turns, and `0x8FDB7A3F30ACC019`, before the climb followed the model's stairs and the waiting spots moved onto the deck, which change the guests' places in the hash, and before a launch also waited for the landing water to be free). The slide world has **no random state** and only the swimmers' own steering step on the way to the stairs draws (one draw of the swimmer's own stream per turn, as in the water); the ride is a table of positions per turn from integer physics (speed += slope x 100 / 1000 - speed / 8), a rider saves only its turn counter, the line in the tower orders by turns waited and guest id, and a launch needs the start clear of riders under 1.5 cells down the track and no swimmer within 0.6 cell of the landing point (positions only: no draw, the guests read in list order). The slides join the canonical hash only when there are any (`PoolSlideWorld.AddCanonicalState`, called from `GuestSimulation.AddCanonicalState`: key, profile, start, heading, heights and pieces in tower order), and the guests' activity fields only while a guest does one (the host's rule), so every park without a slide keeps its pins, and a slide world without slides leaves hash and swim streams exactly as they were (`PoolSlideActivityTests.ASlideWorldWithoutSlidesAddsNothingToTheHashAndDrawsNothingFromTheSwimmersStream`). A save in the middle of a ride restores and continues with the same hash (`ASaveInTheMiddleOfARideRestoresAndTheRideContinuesWithTheSameHash`).

**Water attractions, waves and the lazy river (P5).** The wave pool's ring floaters (`waves.ring`, POOLS.md "Ring floaters") are offered only while a pool's waves run (with at least 40 turns of the run left), never in or beside a river lane or beside the steps and up to the pool's rings, draw only the float's length from the swimmer's own stream, pay the waves' happiness once per period like a free swimmer and move the floater by `PoolWaves.Sway` and a paddle in integer 16.16 arithmetic; every pin below is unchanged by them (the water-attractions run's pool is all lazy river, so no ring is offered there), and `ASaveOnARingRestoresAndContinuesIdentically` checks a save in the middle of a float. `DeterminismTests.PoolsAttractionsFixedRunHash` = `0x2D7EE079E5A9B09F` (it was `0xD9F94E889BDAB453` before the waves also carried the lazy river's floaters: after its float step a floater takes the push's projection on its cell's flow axis (`RiverActivity.WavePush`), only into its own cell or that cell's next or previous cell of the same ring, in free water and clear of the other bodies; the fixed run's channel is a river in a pool with a wave machine, so its floats move; 90 swims, jets 12, river 12, rope swing 4, spa 6 starts; and `0x5232DC9F32832BC6` before the waves pushed the free swimmers: `PoolWaves.Push`, integer 16.16 arithmetic from the turn, the swimmer's 16.16 point, the envelope and the height, with the sine from the swimmers' 64-step heading table, carries a swimmer along +x +y while a crest passes, after its stroke and only where the water and the other bodies leave room (`PoolSwimming.Drift`); the carried distance is no stroke (it adds nothing to the distance walked); it takes no random number, but the moved swimmers reach their goals at other turns, so their goal draws and every later draw move; 92 swims, jets 12, river 13, rope swing 5, spa 3 starts. Before that `0x6FE6C8B4BDCD0FED`, which was `0x3FEC2717D892094A` before the spa took one seat, in the middle of its tub, instead of eight on a circle: an occupied spa now declines the next swimmer, so the offers, and with them every draw after them, differ; before that `0x79EEF3026A22FE72` before the hiring pool's candidates kept their sprite variant, which the digest's park save holds; **the spacing work then moved it from main's `0xC7370809D0D43A0B`**: the river floaters keep a gap, the jets give each guest a spot it steps onto, the rope swing waits for its landing, and the swimmers and the approaches keep 0.7 cell from every body in the water; 93 swims, jets 12, river 10, rope swing 6, spa 11 starts) pins the same fixed run on a pool that is a lazy river **channel one cell wide round an island of two land cells** (10 cells, x 6 to 9, y 7 to 9, the changing room's back cell (6, 8) in it: the hard case, whose bends cut the corner squares that are coping), the wave settings of a wave machine in its access cell (on, medium height, short interval) and a rope swing, water jets and a spa pool beside the water, given as sites as the level derives them from placed objects, and every kind of activity is used (the test asserts at least one start of each; the counts of the current pin are given above). History of the value: first `0xF633A268183A1635`, then `0x924D021F9CC1347D` when the host's `OfferIntervalTurns` went from 45 to 90 (which changes every swimmer's offer draws), now `0x79EEF3026A22FE72` because the review made the float follow the lane's middle line round bends (it no longer ends at the first bend), moved the wave settings from the pool's first cell to the machine's access cell, and the scenario became the channel. None of it draws from the world generator. Every activity draws only from the swimmer's own stream (`SwimRandom`, which the host passes): the approach to an attraction draws as every swimmer's step does; the jets then draw the length of the stay and the pose, the spa the length of the sit, the river the length of the float; the rope swing draws nothing more. Its state is the guest's generic activity fields (`PoolActivityPhase`, `PoolActivityTurns`, `PoolActivityX/Y`) plus its position, all already in the swim hash while a guest is in an activity. The river's current is `PoolRiverMap.Float`, integer 16.16 arithmetic on the guest's position: it keeps the way it came in by until the middle of the cell and then takes the cell's flow, a step of 1/14 cell, and loses an eighth of the offset across the lane (arithmetic shifts). Rings are derived from the flags on the pool cells (row-by-row order). A float is paid only when it ran its drawn turns. The waves a swimmer is paid for come from `PoolWaves.HappinessGain`, a turn count and integer arithmetic (`turn % 24`, a constant times the height) added with `MeterMath.Add`; which settings apply is the wave machine with the lowest object id whose settings are on in the swimmer's pool (`PoolWaveSettings.ActiveIn`), and a pool with no machine has no waves. The waves' push on the free swimmers (`PoolWaves.Push`) is simulation and in the pin; the surface waves, the swimmers' bob (`PoolWaves.SmoothWave`, floating point, the presentation clock), the spray and the flow of the water are presentation only: they change no position, state or hash and are not in any pin. The wave settings, the river flags and the objects are in the cell map and object saves, hashed with them. A park without a river, a wave machine or a water attraction has no offer from them, takes no draw and hashes nothing extra: every earlier pin, `PoolsChangingRoomFixedRunHash` included, is unchanged, also with the full registry registered (`FixedRunWithAPoolAndAChangingRoomIsUnchangedByTheFullActivityRegistryWithNothingToOffer`, `NoRiverNoOfferAndNoDrawFromTheSwimmersStream`).

A new pin covers a park with pools: `DeterminismTests.PoolsFixedRunHash` = `0x3492A988F0EF4AD7` (it was `0x4EEF5812C3D8023D` before the hiring pool's candidates kept their sprite variant, which the digest's park save holds), the fixed synthetic run (the same park, shop, scripts and 14,400 ticks as `FixedRunHash`) on a cross park that also has
a 4 x 2 and a 3 x 3 block of pool cells (17 cells) painted through `PoolBuilder` before the first tick. It differs from `FixedRunHash` because the pool cells and the block stamps their writes leave are part of the
digest and because the walks keep out of the water (the first versions of the pin, `0x51B905DC5CF06BC6` and `0xC53F22BFFA12A843`, were taken before walkers that leave the paths were kept out of pool cells and before main's substep scheduler re-pin, which moves the run underneath); no random draw is added or removed.

## Umbrellas

The `opentpw.umbrellas` extension ([UMBRELLAS.md](UMBRELLAS.md), `[EXT:umbrellas-save]`) adds a brolly shop, a guest umbrella state and its weather rule. A park without a brolly shop and without a guest with an umbrella is untouched by all of it: nobody owns an umbrella, so the weather rule
and the speed term never run, the host score changes no score (the shop's percentages are read for the extension's own objects only, and the balloon-holder refusal of a balloon shop applies only in a park with a brolly shop or an umbrella), and no draw is taken. **Every pin stands with the extension at its default (on)**, and
`DeterminismTests.EveryExistingPinIsUnchangedWithTheUmbrellasExtensionOnOrOffAndNoBrollyShop` runs the fixed run (`FixedRunHash`) and the changing-room run with the extension on and off and gets the same hashes. The umbrella fields (state, colour, due turn) join the canonical hash and the guests' own digest only for a
guest that has an umbrella, and the guest snapshot writes them only then (schema unchanged, older saves load), so a park without one has the same bytes. A park save records the extension only while it has a brolly shop or an umbrella (`ParkResumeMods.UsedOnlyExtensionIds`).

A new pin covers a park with a brolly shop: `DeterminismTests.UmbrellasFixedRunHash` = `0xE912E7E5D5B6E612`, the fixed synthetic run (the same park, shop, scripts and 14,400 ticks as `FixedRunHash`) plus a brolly shop at (10, 1) paid through the economy like the drinks shop and a weather
that rains from the 20th to the 80th second and from the 140th to the 200th (`GuestSimulation.UmbrellaWeatherState`, which the level reads from `ParkWeather`'s rain, lightning and snow flags): 45 guests buy an umbrella and every state of the machine is seen. Its moves of the stream: the decisions (the shop scores in the rain), the sales,
and **one world draw per purchase** for the umbrella's colour (taken only at a brolly shop); the reaction delay is a per-guest hash of (guest id, park turn) and takes no draw (`[EXT:tpi-time-seeded-rand]`). A park saved mid-rain with umbrellas in every state and resumed into a fresh instance continues with the same hash tick by tick
(`UmbrellasSimulationTests.AParkSavedMidRainWithUmbrellasInEveryStateContinuesIdenticallyAfterAResume`).

Review fixes of the same phase did not move `UmbrellasFixedRunHash` (still `0xE912E7E5D5B6E612`): the balloon gate is now per park (a holder count the simulation keeps, no scan), and the purchase mirrors the state-3-then-4 sprite test of `TPI-EXE:0x612ea9`, a branch the run never reaches because entering the shop closes a drawn umbrella first.

The owner then chose Theme Park Inc's kid score of 75 % for the shipped shops (it had been OpenTPW's 100). `SyntheticPark`'s `brollyKidScore` defaults to that 75 so the synthetic brolly shop mirrors the `.sam` data. Recomputed on purpose: `UmbrellasFixedRunHash` stays `0xE912E7E5D5B6E612` (it was `0xE912E7E5D5B6E612` at 100). The pinned run gives nobody a balloon, and the U5 week showed that 75 % and 100 % sell the same then, so the decisions, the sales and the colour draws do not move.

## Rain mood

The `opentpw.rain-mood` extension ([UMBRELLAS.md](UMBRELLAS.md#the-rain-mood-rule-phase-u4), `[EXT:rain-mood-once-per-day]`) is off by default. The level hands the rule to the guest simulation only while the extension is on (`GuestSimulation.RainMood`, null otherwise), so with it off nothing runs, no draw is taken
and no hash part is added: **every existing pin stands, including `UmbrellasFixedRunHash`** (`DeterminismTests.EveryExistingPinIsUnchangedWhateverTheRainMoodSwitchWhileTheSyntheticParksSetTheRuleByHand`, and `RainMoodTests.WithTheRuleOffTheWeatherChangesNothingAboutAGuestWithoutAnUmbrella`). The rule itself draws from no stream; it only moves happiness, thirst
and hunger, which moves the decisions and so the run. The guest's last rule day (`Guest.RainMoodDay`) joins the canonical hash and the guests' own digest only once it is set, and the snapshot writes it only then.

Two new pins cover the rule, on the same fixed synthetic run and the weather of the umbrellas pin (rain from the 20th to the 80th second and from the 140th to the 200th): `DeterminismTests.RainMoodFixedRunHash` = `0x3697B9094432D152` (the fixed run, no brolly shop, the rule on; 102 guests changed by it) and
`DeterminismTests.RainMoodWithUmbrellasFixedRunHash` = `0xADEE2FA6AFC2ACE3` (the brolly shop of the umbrellas pin and the rule both on; 104 guests changed). A park saved mid-rain and resumed into a fresh instance continues with the same hash (`RainMoodTests.AParkSavedMidRainContinuesIdenticallyAfterAResume`).

Review fix of the umbrella modifier: in Theme Park Inc only a guest who is already open (state 4) when the rule starts gets it (`TPI-EXE:0x611334`), not one whose umbrella the same step opens (1 to 4 at `0x6111E3`, a due 3 through `0x402d8d`). That moved `RainMoodWithUmbrellasFixedRunHash` from `0xF599E7E8B09C0FB7` to `0xADEE2FA6AFC2ACE3` (104 guests changed, as before); `RainMoodFixedRunHash` (no shop, so no umbrella) and every other pin stand. The optional `Guest.RainMoodAgeStartTurn` (the load turn of an imported guest without a time in the park, `[EXT:rain-mood-imported-age]`) joins the canonical hash and the guests' own digest only when set, which only an import with the extension on does, so no pin is affected.

The kid-score change above was recomputed for the shop run of this section too: `RainMoodWithUmbrellasFixedRunHash` stays `0xADEE2FA6AFC2ACE3` (it was `0xADEE2FA6AFC2ACE3` at a kid score of 100). `RainMoodFixedRunHash` stays `0x3697B9094432D152`; that run has no brolly shop, so the kid score is not read.

## Winter cold

The `opentpw.winter-cold` extension ([WINTER.md](WINTER.md#cold-mood-extwinter-cold-phase-w5), `[EXT:winter-cold]`) is off by default. The level hands the rule to the guest simulation only while the extension is on (`GuestSimulation.WinterCold`, null otherwise), so with it off nothing runs, no draw is taken and no hash part is added: **every existing pin stands**
(`DeterminismTests.EveryExistingPinIsUnchangedWhateverTheColdSwitchWhileTheSyntheticParksSetTheRuleByHand`). The rule draws from no stream; it only moves happiness (and, through the glühwein stall, `WarmUntilDay`), which moves the decisions and so the run. The guest's last rule day (`Guest.ColdMoodDay`)
joins the canonical hash and the guests' own digest only once it is set, and the snapshot writes it only then.

Two new pins on the fixed synthetic run, in a weather that snows from the 20th to the 80th second and from the 140th to the 200th: `DeterminismTests.WinterColdFixedRunHash` = `0x9E74A906AB249401` (no shop, the rule on; 97 guests changed by it) and `DeterminismTests.WinterColdEverythingFixedRunHash` = `0x45FCB3FC30C601FC` (was `0xCCEA2E164DF6888A`, moved by the kiosk's return rule, see the winter section, and before it `0x7589B229530055D6`, moved by the winter base: the kiosk sells all year with its season terms and the harness's calendar is now May for the first 100 seconds and December after it; the cold rule's own code did not move it, `WinterColdFixedRunHash` is unchanged; the rule, the rain mood, the brolly shop and the winter
shops on: the harness's winter shops, a kiosk at (10, 1) and a glühwein stall at (10, 8); 100 guests changed, 68 wore an item or were warmed). With the rain mood on, the cold rule replaces only the happiness of the row the rain mood would use in snow: the +1 of its snow row, or in Halloween, where the snowfall has lightning and the map reads a storm, the storm row's -1 (`[EXT:winter-cold-replaces-snow-cheer]`, the user's decisions, "Kou vervangt storm-humeur"), so this run differs from a plain sum of the two. The Halloween decision moved no pin: the harness's weather has no lightning (its map and the winter signal both say snow), and with cold off nothing changed; the Halloween case is covered by `WinterColdTests.InHalloweenSnowColdReplacesTheStormMoodAndTheStormsThirstAndHungerStay` and by the measured week in docs/WINTER.md. The glühwein warms for the rest of its day plus the next game day (`[EXT:winter-cold-warm-window]`, the user's decision), so this run differs from a plain sum of the two. The cold step reads the winter shops' own weather signal (`GuestSimulation.WinterWeatherState`, falling back to the umbrellas' map), so Halloween's snow, which comes with lightning, counts as snow for it (`WinterColdTests.HalloweenSnowWithLightningIsSnowForTheColdRuleButAStormForTheUmbrellasMap`); the harness sets only the umbrellas' map, so no pin moved because of it. A park saved mid-snow and resumed into a fresh instance continues with the same hash
(`WinterColdTests.AParkSavedMidSnowContinuesIdenticallyAfterAResume`).

## Winter

The `opentpw.winter` extension ([WINTER.md](WINTER.md#simulation-phase-w3), `[EXT:winter-save]`) adds a winter kiosk, a glühwein stall and the guest fields `WarmItems` and `WarmUntilDay`. A park without a winter shop and without a guest with an item is untouched by all of it: the host score reads the extension's percentages for its own objects only, the kiosk's
purchase is the only writer of `WarmItems` and takes **no draw from any stream** (the head item and the colours are hashes of the guest id, `WinterRules.HeadItemOf`, `WinterRules.ColourOf`), and the fields join the canonical hash, the guests' own digest and the guest snapshot only while they are set, so such a park keeps its bytes and its pins.
`DeterminismTests.EveryExistingPinIsUnchangedWithTheWinterExtensionAndTheWinterWeatherOnOrOffAndNoWinterShop` runs the fixed run, the changing-room run, the umbrellas run and the rain mood run with the extension and the wintry Decembers option each on and off and gets the same hashes (no baseline moved). A park save records the extension only while it uses it
(`ParkResumeMods.WinterInUse`).

A new pin covers a park with winter shops: `DeterminismTests.WinterFixedRunHash` = `0xA66540FF8C2AC6DE` (see the re-pin of 2026-10-19 below), the fixed synthetic run plus a winter kiosk at (10, 1) and a glühwein stall at (10, 8), paid through the economy like the drinks shop, a snowfall from the 20th to the 80th second and from the 140th to the 200th
(`GuestSimulation.UmbrellaWeatherState`, which the level reads from `ParkWeather`) and a calendar that is May for the first 100 seconds and December after it (`GuestSimulation.WinterMonth`): 51 guests buy a hat or earmuffs or mittens (10 are fully dressed), the kiosk sells 63 times and the stall 67. A park saved mid-snow with items and resumed into a fresh instance
continues with the same hash (`WinterSimulationTests.AParkSavedMidSnowWithItemsContinuesIdenticallyAfterAResume`).

**Re-pin, 2026-10-19.** `WinterFixedRunHash` moved from `0x8F7023321586F670` (49 buyers, kiosk 50 sales, stall 70) to `0xD4E2340C99394B73`. Cause: the user's decision that the winter kiosk sells all year ("Het hele jaar, vraag via seizoen"; the snow or December-to-February gate is gone) and the kiosk's new season terms
(winter 150, spring and autumn 60, summer 20, snow 200; the `.sam` and the fixture's terms), plus the fixture's calendar now running May first and December after it, so the kiosk's second purchase (mittens) still happens (with the kiosk open in May at a low demand, December first left no guest a mittens purchase in the 200 seconds); the fixture also samples the items every tick now. The weather pins, the umbrellas, rain mood and changing-room
pins and the fixed run did not move: the kiosk's terms are read for the extension's own objects only. The winter shops' own weather signal (snow even with lightning) changes nothing in any pin, since the umbrellas and the rain mood keep the shared map and no fixture has Halloween snow with a winter shop.

**Re-pin, 2026-10-19 (kiosk return, `[EXT:winter-kiosk-return]`).** User decision, asked directly: "Laten terugkomen". A guest whose slots are not full no longer counts a winter kiosk as recently used (`GuestSimulation.WinterScoreOf`), so a guest with a hat can come back for mittens; before it, no guest in about 1,740 measured ever bought a second item, because the kiosk, the last thing the guest used, scored 0 by the game's same-type rule and was then divided by 5, 4, 3 or 2 by the used history (both had to go; either alone changed nothing, see [WINTER.md](WINTER.md#returning-for-the-second-item-extwinter-kiosk-return)). Three pins moved, every one of them a park with a winter kiosk and guests who lack an item:

| Pin | Old | New | What changed in the run |
| --- | --- | --- | --- |
| `WinterFixedRunHash` | `0xD4E2340C99394B73` | `0xA66540FF8C2AC6DE` | 53 buyers, 6 fully dressed, kiosk 61 sales, stall 67 became 51 buyers, 10 fully dressed, 130 admitted, kiosk 63, stall 67 |
| `WinterAlwaysFixedRunHash` | `0xAD0FF92A6B42F187` | `0x5F767F69EF5CBF07` | 55 umbrella buyers, 47 warm item buyers, 127 changed by the rain mood became 50, 45 and 122 |
| `WinterColdEverythingFixedRunHash` | `0xCCEA2E164DF6888A` | `0x45FCB3FC30C601FC` | 103 changed, 74 with an item or warmed became 100 and 68 |

No pin of a park without a winter kiosk moved, and nothing else changed: `WinterColdFixedRunHash` (no shop), the weather pins, the umbrellas, rain mood, changing-room and fixed-run pins and the December guest pins all stand (the "every existing pin is unchanged" tests pass with only these three constants edited). The rule takes no draw from any stream: a guest with a free slot is scored from views without the kiosk in its used history, and a guest with full slots still scores the kiosk 0.

**No re-pin, 2026-10-19 (the kiosk's price, [WINTER.md](WINTER.md#price-extwinter-tuning)).** The return rule now also requires that the guest could pay the kiosk's price (`QueueJoinRules.TooExpensive`, the pure test), and the measurement harness can give the winter shops their shipped price terms (`SyntheticPark`'s `winterPrices`). No pin moved: the pins' winter shops keep no price terms (no `winterPrices`: the price test reads a price of 0 and never refuses), so the new condition is always true there, and no `.sam` changed.

## Wintry Decembers

The `opentpw.winter-weather` option ([WINTER.md](WINTER.md#simulation-phase-w3), `[EXT:winter-weather]`) is off by default; the level hands it to `ParkWeather` when it builds the weather. The weather is outside the canonical hash ("Weather timing"), but what the guests read from it is not: the umbrella rules, the rain mood and the winter shops see snow instead of a
thunderstorm or rain in a December of the jungle and fantasy parks (the whole rain band: plain rain and thunderstorms). The converter draws nothing for the change (the roll's draws and the tie-break are the same), but with no lightning the lightning clock's draws stop, so the weather stream moves on from the first wintry spell: a park with the option on is another weather from its first December, which is why a save always records
the option while it is on. The roll's month is derived from the forecast's apply time (`applyAt - DaysOfWarning` days), so the weather snapshot gains nothing and a resume mid-snow continues exactly (`WinterWeatherTests.AWintryWeatherSavedMidSnowContinuesIdenticallyAfterAResume`).
Two pins cover the jungle's weather turn by turn over its first 400 days under seed 21: `WinterWeatherTests.JungleSeed21OffHash` = `0x58F5D2429FDCADAA` (the original's weather, option off) and `JungleSeed21WintryHash` = `0x2DC4898B7594C4FA` (option on; 29 snowy days, the first on day 343). Halloween and space run the same weather with the option on or off.

The guests' side of the option has a pin of its own, because the two weather pins hash only the weather: `DeterminismTests.UmbrellasAndRainMoodUnderTheJungleWeatherAcrossDecemberArePinnedWithWintryDecembersOffAndOn` runs the synthetic park with a brolly shop and the rain mood on, driven by the jungle's real `ParkWeather` (seed 21) from park day 320 over 35 days across the first December: `JungleDecemberOffFixedRunHash` = `0xF4A00471E60B9535` (option off: rain, then a thunderstorm in December) and `JungleDecemberWintryFixedRunHash` = `0x296DA4DA6A8BE323` (option on: rain, then snow). `EveryExistingPinIsUnchangedWithTheWinterExtensionAndTheWinterWeatherOnOrOffAndNoWinterShop` builds that weather with the option's real gate and also covers `RainMoodWithUmbrellasFixedRunHash`. The forecast follows the option (`ParkWeather.ForecastBands`) and takes no draw.

## Always winter

The `opentpw.winter-always` option ([WINTER.md](WINTER.md#always-winter-extwinter-always), `[EXT:winter-always]`) is off by default; the level hands it to `ParkWeather` when it builds the weather and to the guests (`GuestSimulation.WinterAlways`). With it on, every roll uses December's season row in a park that can snow from it
(jungle, fantasy and Halloween; space keeps its own seasons) and every roll is wintry, so the rain band is snow without lightning all year; the winter shops' season terms read December. The roll's draws are the same in number and kind (only the row changes), the conversion draws nothing, and with no lightning in the
jungle the lightning clock takes no draws at all, so the weather stream is another one from the first roll. Nothing new is saved in the weather snapshot (the row and the wintry flag follow from the option), and a resume mid-snow in July continues exactly (`WinterAlwaysTests.AnAlwaysWinterWeatherSavedMidSnowInJulyContinuesIdenticallyAfterAResume`).
The option implies the wintry Decembers: both on is the same weather and the same guest run as Always winter alone. With it off **every existing pin stands**: the weather pins `JungleSeed21OffHash` and `JungleSeed21WintryHash` (`WinterAlwaysTests.TheJungleWeatherOfAFixedSeedIsPinnedWithTheOptionOn`), the December guest pins built with the option's real gate,
and the fixed, umbrellas, rain mood and winter runs (`DeterminismTests.EveryExistingPinIsUnchangedWithTheAlwaysWinterOptionOff`). The option is always recorded in a save while it is on.

Two new pins. The weather: `WinterAlwaysTests.JungleSeed21AlwaysWinterHash` = `0x71A3DF8635ACF9B9`, the jungle's first 400 days under seed 21 with the option on (378 snowy days, the first on day 4; no rainy day). The guests: `DeterminismTests.WinterAlwaysFixedRunHash` = `0x5F767F69EF5CBF07` (was `0xAD0FF92A6B42F187`, moved by the kiosk's return rule, see the winter section)
(`ShopsAndRainMoodUnderTheJungleWeatherFromMayArePinnedWithAlwaysWinterOn`), the synthetic park with a brolly shop, the winter shops (a kiosk and a glühwein stall) and the rain mood on, driven by the jungle's real `ParkWeather` (seed 21) from the first of May over 168 days, four weather turns to each park turn, so the
guests meet May to mid-October, with the shops reading the weather's own calendar month: snow in 230 of its 240 samples (one dry spell), 50 umbrella buyers, 45 warm item buyers and 122 guests changed by the rain mood. The same run with the option off (no pin) never snows and differs. The cold rule is off in this run, so the
cold mood's replacement of the rain mood's snow and storm happiness (Winter cold) does not enter it.

## Cheats

A park's cheats ([CHEATS.md](CHEATS.md), `ParkCheats`) live in the park economy: the original's cheat mode (`CHTS` byte 0), the record of every applied cheat (turn, kind, argument; at most 4,096 entries, older ones dropped and
counted) and the actions waiting for the next turn. Every cheat the player asks for, through the original's secret park name and Alt keys or the extension's window, is **queued and applied at the start of the next park turn**
(`ParkEconomy.AdvanceTurn`, before the turn's thing sweep with its weather and arrival updates; in a level whose turns are not swept, a paused economy's or one without pooled guests, the tick's guests and objects have run
before the economy's turn, and only the weather follows), in a fixed order of kinds (`CheatKind`), and **takes no draw from any random stream** (`[EXT:cheats-turn-boundary]`). So a run with the same
cheats at the same turns is the same run; the record in the save names those turns. The record does not replay itself; the cheats the player asks for are commands, like the park's other
edits, and their command log replays ([Commands](#commands)).

The record is part of the park save (`ParkSaveFile.Data.Cheats`) only when the park has cheated or has a cheat waiting; otherwise it is left out, so **a park that never cheated keeps its save bytes and its hash** whether the
`opentpw.cheats` extension is on or off. Because the economy digest of the canonical hash is that save's text, the record joins the hash exactly when it is present; `WorldStateHash.SchemaVersion` did not move.
`CheatsTests.EveryExistingPinAndTheSaveBytesAreUnchangedWithTheExtensionOnOrOff` runs the fixed run (`FixedRunHash`) with the extension off and on and gets the same hash and the same save bytes. A world save records
`opentpw.cheats` only once the park has used one of the extension's own cheats (`ParkCheats.UsesExtension`, `ParkResumeMods.UsedOnlyExtensionIds`); a park cheated through the original's cheat mode alone does not record it
and loads with the extension off.

The original's cheats (K2) change only economy state: Alt+1 credits through the ledger, Alt+R adds the objects to `Research.CheatAvailable` (saved, and hashed through the economy digest only
once the list has an entry), and the ticket cheats add to the park's ticket set (and the player's ledger, which is outside the hash). None of them draws. Importing an original save applies
its `CHTS` byte 0, but a park started from the jungle's shipped `Easymode.TPWI` (which carries `01 01`) is not marked (`[EXT:cheats-shipped-seed]`), so every Instant Action and
reference start keeps its save bytes and its pins.

The extension's own cheats (K3) are the arrival and the weather. Both apply at the turn boundary through the level (`IParkCheatHost`) and take no draw: the arrival's group size is the next
natural group's size moved into the chosen vehicle's band (computed from the park's points and the weather, as the natural size is, with no draw), and the weather cheat sets its kind,
quality and drop target explicitly and skips the converter's tie-break draw. A cheat the park cannot take at that turn (an arrival while a vehicle is at work) stays queued and is applied,
and recorded, at a later turn boundary, so the turn in the record is the one it took effect; while an arrival waits, the arrival update starts no natural group and calls no vehicle for
leaving guests (`GuestSimulation.ArrivalHeldForCheat`, read from the saved queue, so a resumed park holds the same turns). A pin covers a scripted sequence: `CheatsExtensionTests.CheatsFixedRunHash` =
`0x5CBF8584199279AB`, the fixed synthetic run (the park, shop, scripts and 14,400 ticks of `FixedRunHash`) with the extension on, a jungle weather (seed 21) whose drops feed the guests'
arrival factor, and money at turn 100, research at 200, the Visitors ticket at 300, a boat at 400, a bus asked for at 402 (it waits while the boat's group is delivered and applies at
turn 462) and rain at 500 (the plan's bump-ride vehicle limit at turn 600 is K4's; this park has no bump ride). `AParkSavedMidSequenceContinuesIdentically` saves and loads the economy
before the boat (with it queued), at turn 405 (the bus waiting across the save) and after the bus, and gets the same hash each time.

The ride vehicle cheat (K4) raises a ride's limit at a turn boundary with no draw. A raised bump controller's limit and a raised tour controller's 40 slots join the hash only for a raised
ride, and the runtime snapshot, the track and the tour parts write the raised state only then, so every other park keeps its pins and save bytes. A pin covers the scenario:
`CheatsRideVehicleTests.CheatsBumperRaisedRunHash` = `0xDF2C86E99A8FB3C0`, the jungle bumper controller (template -1, world generator of seed 99) raised to 32 cars, loaded with 32 guests,
run through a 30-second session and 600 more ticks.

## Commands

`[EXT:online-coop-commands]` The park's edits are commands ([COOP.md](COOP.md), `ParkCommands`): each applies before a stated simulation tick, the next one the fixed-step clock runs. In single
player a command runs at once in the input handler, between two ticks before the frame's simulation, exactly where the edit ran before the command layer, so **no pin moved** and no
save byte changed (the layer adds no saved or hashed state; the Move tool's carried entry and the employees in the players' hands are session state, not saved). Held commands (`ParkCommands.Schedule`, a replayed log) apply at
the start of their tick inside `Level.AdvanceSimulation`, before the tick's economy sync. A command's executor reads only the command and the world, never a tool's state, and draws no
random stream of its own beyond what the edit itself draws (a placement's script seed, as before). `ParkCommandLevelTests` records a build session on a no-data level and replays it on a
twin with the same `WorldStateHash` after every tick; `ParkSettingCommandLevelTests` does the same for a session of the HUD windows' settings. `ParkToolCommandLevelTests` applies
the staff and pool tools' logged commands to a twin that reaches the same hash, `TrackAddOnLevelTests` the track tools' to a twin whose track layer and ride controller match, and
`CoasterCommandTests` a coaster tool session's to a twin coaster with the same canonical state and money paid. The coaster tool's entry and exit set and clear the being-edited mark that guests read
(`CoasterRide.BeingEdited`) through their commands. A tool issues one command for each write it made before (a pool
stroke one per cell, as the drag wrote them), and a track command walks the circuit inside the command while the tool's own step after the walk stays on the issuer's machine; the line
tool's walk to the track's open end, which writes the walked tiles' overlap words, is a command too (`OpenTrackLineToolCommand`, or inside the cut that opens the circuit). A replay runs one tick per `AdvanceSimulation` call or any number per `Level.AdvanceFrame`; the park after a tick does not depend on how the ticks fall
into frames, as "Frame batching" shows.

## Tests

`DeterminismTests` (no game data needed):

- same seed twice in one process ⇒ equal hash; different seed ⇒ different hash
  (the synthetic park: economy, paying guests, a shop script, unseeded RAND
  scripts);
- save at mid-run, move the world generator by advancing the economy and
  drawing from it, load ⇒ the hash at the save point is restored, and the
  final hash equals the uninterrupted run;
- save, then draw (guest ticks, a script draw, sound draws), load ⇒ the one generator and
  the sound seed equal the saved values and the guests and the scripts draw from the loaded economy's generator; a
  `RestoreRandomState` that does nothing fails this test. The scramble never
  goes through the restore path under test;
- sound draws do not change the hash, and a run with a sound chooser hashes like
  a headless one;
- a pinned hash of a fixed run, checked in every test process
  (`0xB4AEC10551DF2738`, schema 21, the walk chains, see [The walk chains](#the-walk-chains-round-10-package-a-october-28-2026); before it `0x5946C7CB09C6EEE7`, schema 21, the guests' destinations, see [Guest destinations](#guest-destinations-round-9-package-g-october-27-2026); before it `0x1AABD1698E2E75BE`, schema 21, the first day message of a new park, see [First day message of a new park](#first-day-message-of-a-new-park-econ-063-october-26-2026); before it `0xF3F895E3EF2F969B`, schema 21, the exit walk, see [The exit walk](#the-exit-walk-october-20); before it `0x5D8385D13CC3BB9B`, fidelity round 8 (guests), on top of the previous pin `0x2A8418A90725699A`: the per-cell thing lists keep their order across the park turns instead of being rebuilt in guest order every turn, and separation visits each list head first, the guest that entered the cell last first (WALK-003; every guest saves its entry number, the optional `CellSerial`, and an older save starts the lists in guest order); the booth's first verdict is the guest's next update after the walk's arrival, not a second later (ARRIVAL-006, 61 to 62 admissions in the ownership-flip run); the bin search makes a route request for every candidate closer than the best and a failed one leaves the walk stuck, and a route request of a stranded guest refuses at once (GUEST-005, 0x00500DC0, 0x004FA530); a won sideshow pays its prize into the guest's purse and ends with the logarithmic happiness tail and one world draw (GUEST-005, 0x004FE1E0); a show or performer turn writes the 11-bit heading angle (GUEST-010, 0x006E7074); the balloon prankster's victim is the balloon holder that entered the cell last (GUEST-007). No draw order changed except the tail's one draw. The pins that followed moved with it: the pool changing room `0xA83721661FA72F69` to `0x2E9AA1D5A79E1FB2` (90 swims, 131 admitted), the pool extras `0xA07E1347C234D5E1` to `0x20DED2AC4BEC293A`, the water attractions `0x6FE6C8B4BDCD0FED` to `0x5232DC9F32832BC6`, the pool cells `0x3492A988F0EF4AD7` to `0x8EE352E1FD1EEAB3`, the smarter guests `0x64340E899EC258EB` to `0xF0B3F8366E2B73E4`, the script mods `0xC4BE433626584D47` to `0x8AB18F8AC449CC49` and with every hook `0x9F2877C39A19BD52` to `0x6B74FB3AE8A4937F`; `GuestMeasurementTests`' scenario pins `0xBD62B929BDB55659` to `0x383ECBB8309B25FD`, with the extension `0xC0344925E23A32C1` to `0xF4F3CD74CD7253AB` and with the food shop `0x336E044598FE1EBB` to `0x30C8AF969CFEF592`; the cross grid of `PathBuilderTests` `0x7E11BFA00FE093FD` to `0x9D34C006E9694818` (61 to 62 admissions) and, with game data, the Jungle guest run of `PathAssetTests` `0xBC02D81A5FC6621D` to `0xBE549F10640102B9` (20 to 22 admissions). Before that `0x2A8418A90725699A`, schema 21: the staff candidate's sprite variant (GUEST-014): the hiring pool's candidates keep the variant their constructor draws (the draw itself was always made, so no random stream moves), the park save lists it for every candidate and the digest hashes the park save, so the candidates of the synthetic park (their variants are the draw mod the role's bank count, 0 for the handymen, guards and researchers) change the digest and nothing else does; FINDSCRIPTRAND's walk from the list head (DET-013, `0x55605C`, `0x5560E2`) moves no pin, which the pins show: with the walk reverted to the lowest id first every pin below stays the same, and with the old walk and the variant removed every pin returns to its previous value, on top of the previous pin `0xA91A607272718E93`, schema 21: the substep scheduler (DET-013) on top of the pin before it `0xE48128553808938B`: the fixture runs Level's substeps, so the shop's and the dice scripts take one slice per eight substeps in the original's pass order (newest first, the id's pass) instead of one on every tick, and their draws, waits and every guest interaction after them move; the park's save carries the script pass counter, which joins the hash once a pass has run. Before that, `0xE48128553808938B`, schema 21, two causes that landed separately and now compose, on top of the previous pin `0x7A199E7425A19C8B`: the walk change of that pin (WALK-006, below; alone it gave `0x7A199E7425A19C8B`) and the park turn's order (DET-013; the sweep alone, on the base before the walk change, gave `0x497D46DEE19D3ABE`; both together give the new pin): the fixture runs Level's order, the substep's scripts and then the economy's turn with its thing sweep (guests newest first by the pool's list, the economy's staff, research, rides and challenges, the shop's ride update, the turn's destroys, the arrival update), and the ticks between turns free nothing, so the draws of guests, staff, scripts and the arrival group come in another order and every later draw moves. The fixture's clocks also changed to match the level's: the economy advances one tick for each guest tick (it ran four, at the old Fastest speed) and the run is 14,400 ticks, as many ticks of the economy as before, so the pin still crosses a month end. Intermediate value: the lockstep clocks with the old per-tick order and without that walk change give `0x86CBDABB37372FDA`; the sweep moves it to `0x497D46DEE19D3ABE`, and the walk change then to the new pin; the schema stays 21. Before that, `0x7A199E7425A19C8B`, schema 21, on top of the previous pin `0x188C45B21C1E15E8`: the called guest's last walk into the ride's entrance cell (WALK-006: the route request `0x4FA5F0` to the stand point of state 13, `0x4FFFF0`) is the search and edge-test walk of every other walk (`GuestNavigation.Navigate`) wherever the search can enter the cell, so the steering, timing and the guests' separation of the guests the synthetic park calls forward move, and a queue whose outside cell was removed has its guests stand in the path cell the queue now starts on; no draw from the world or guest generators is added or removed, no hashed field is added and the snapshot schema is unchanged, and the Jungle guest run `0xBC02D81A5FC6621D` and the cross grid `0x7E11BFA00FE093FD` of `PathBuilderTests` do not move (this change alone, on the base before the ride-script VM change, gave `0x625402703E4F66A7`; the VM change alone gave `0x188C45B21C1E15E8`). Before that, `0x188C45B21C1E15E8`, schema 21 (bumped: the script digest hashes the raw words of the shared stack/heap array and the bridge digest drops two lists), on top of the previous pin `0x3DA16AED71E2F1E4` (schema 20; the intermediate digests `0xA1A7D52EB51607D4` under schema 21 and `0x1B77FB29EC69D815` under schema 20 never shipped): the ride-script VM's internal semantics follow tp.exe (RSE-VM.md, "Original interpreter internals"): `CRIT_UNLOCK` ends the slice, the critical flag is cleared at every slice start and the slice budget does not count instructions inside a critical section, HUSH/HOP are the script's own heap and the LIMBO family its own table (the bridge no longer boards on HUSH/LIMBO and no longer keeps a limbo list or a hopped set, so those two lists leave the bridge's digest; the VM digest adds heap, limbo and TURBO byte only once a script uses them), and the bridge's host steps no longer wait for a script that ended its slice inside `CRIT_LOCK`. No draw from any random stream and no save schema changed; a save's script snapshot gains the shared stack/heap array, the limbo slots and the TURBO byte (older snapshots load). Before that, `0x3DA16AED71E2F1E4`, schema 20, the arrival walk (WALK-008, ARRIVAL-001) on top of the previous pin `0x5F177F986FADADB1`: a guest is made at the centre of the bus stop B cell and walks the cell map through states 0 to 5 (tp.exe `0x4FF520`, `0x4FF730`, `0x4FF7F0`, `0x4FFB20`), taking the draws the original takes (the booth side and the two sub-cell bytes of state 1, the gate wait `0x501E6E` of state 3, the booth, entrance and gate sides, the way home's side), the person constructor stores the speed term 25 (`0x4FB1C1`), a paid guest walks in only as the head of its cell's thing list, and the thought pick counts only guests on cells of type 0, 1, 3, 9 and 10 (`0x4FA990`), so every guest's route, timing and draws move; the guests' visitor numbers and the park's count (`0x51AAF0`) join the digest, and the guest snapshot is schema 9. The Jungle guest run `0x30F213E46933EB8C` → `0xBC02D81A5FC6621D` (21 → 20 admissions) and the cross grid of `PathBuilderTests` `0x397F3EC43F6F70C8` → `0x7E11BFA00FE093FD` (61 admissions, as before). Before that, `0x5F177F986FADADB1`, schema 20, two causes on top of the previous pin `0x01946A39A53F851E`: (1) guests take their ids from the park's one thing pool (WALK-010; tp.exe's allocator `0x516270` through the base constructor `0x50AFE0`), and the synthetic park builds the pool as Level does (the world's ten singletons, then the gates and the lights, so the first guest is thing 13 and not 1; this change alone gave `0xA7A559BCC863F8E2`): the reseed of every guest creation (`0x4FB19E`), the needs phase (`0x501676`), the prank flag and every other id-keyed rule read the thing id, and a leaving guest's slot is taken again by the next arrival (last in, first out after the turn's deferred destroy, `0x516310`); (2) the booth walk's speed term (`0x4FF730`: 25 for a thing id with its low two bits 0, 50 while the bus waits at status 3, otherwise 0, set on every park turn of state 2), which moves the guests' walks to the booth and with them the admissions. No hashed field is added and no draw from the world generator is added or removed: the pool's next free id joins the guest digest in place of the old counter. Before that, `0x01946A39A53F851E`, schema 20, on top of the previous pin `0x0487112FF9FF20DB`: a finished use now averages 3 x the guest's happiness byte change into the object's satisfaction ring (UI-024; tp.exe `0x4FD970` -> `0x4E1E00`), so the economy state the digest hashes holds the rings' values; the fields and the schema are unchanged, and no draw from the world or guest random streams was added. Before that, the pin `0x0487112FF9FF20DB`, schema 20, two causes on top of the previous pin `0xDB3EA9A7AF384B5B`: (1) a finished shop or sideshow visit no longer counts as a ride (the use routine `0x4FD9B1` sorts a visit by the type's `WhichUIType`, `+0x4AC`: 0 raises the rides counter `+0x1C4`, 1 the shops `+0x1C8`, 2 the sideshows `+0x1CC`, anything else counts nothing; SAVE-002), so the rides counter and the guests' ride counts move; (2) the three new counters (shops visited, sideshows visited, sideshows won) are hashed in the guest digest, and the park resume snapshot gains optional `ShopsVisited`, `SideshowsVisited` and `SideshowsWon` fields that older saves load as 0. Before that, the pin `0xDB3EA9A7AF384B5B`, schema 20, two causes on top of the previous pin `0x205523C08061A05D`: a guest walking to a ride runs the minor decision of tp.exe's `0x4FD570` every twelfth update (GUEST-018, retired; GUEST-019), which can switch the target, and the booth's expensive wait is counted in executed park turns (`0x4FF9D0`, ARRIVAL-006), so the guests' targets, the joins and the booth's timing move; the pin before that, `0x205523C08061A05D`, schema 20, two causes on top of the previous pin `0xC92A25B3D77EAE17`: (1) guests walk whole walks as tp.exe does: one destination for the whole walk to a ride (state 10, `0x4FFBC0` -> `0x4FA530` -> `0x510100`) and out of the park (state 18, `0x500A50`, walk mask 1), a guest leaving a queue stays where it stood until the decision (QUEUE-010), the guests leaving through the gate walk to the vehicle's stop cells (states 19 to 21, `0x500AD0`, `0x500BD0`, `0x51AAD0`: the cell pick and the two sub-cell bytes are draws of the shared world generator, in the original's order) and the guard release goes to the gate cell, so the guests' positions, the joins and the world stream move; without the rings below, this change alone gave `0x9DB7A7AAEA8D1CE2` on the world-generator tree (`0x7AED41D19433D4B2`). No save field is added by the walks (the walk's destination and route are the navigation state the guest save already carries, and the saved guest state gains the value 20, WalkingToVehicle, which the validators accept; older saves load unchanged). (2) The rings: ECON-057 below, which moved the pin from `0x7AED41D19433D4B2` to `0xC92A25B3D77EAE17` only through the saved `Rings` field in the economy digest. Before that, `0xC92A25B3D77EAE17`, schema 20: the attractions' six 30-day daily rings (ECON-057) join the economy digest through the park save's optional `Rings` field, written once a ring holds The same cause (the hashed candidate variants) moved the other pins that run a park: `SmarterGuestsFixedRunHash` 0xC8534726E08D0EC2 → 0x64340E899EC258EB, `ScriptModsFixedRunHash` 0xD7388BB84BBA0676 → 0xC4BE433626584D47, `ScriptModsAllHooksFixedRunHash` 0xC3190E46B1E70633 → 0x9F2877C39A19BD52, `PoolsChangingRoomFixedRunHash` 0xB5B033AE20AD9BA0 → 0x42E916DFC15CED19, `PoolExtrasFixedRunHash` 0x0C35A6A0F10D914E → 0xD8806A8911B990C7, `PoolsFixedRunHash` 0x4EEF5812C3D8023D → 0x3492A988F0EF4AD7, `PoolsAttractionsFixedRunHash` 0x79EEF3026A22FE72 → 0xC7370809D0D43A0B.
  a running value or a day: the synthetic park's shop and its sales make today's customers, takings and costs non-zero, so the digest changes; the hash without the
  ring field is the previous pin `0x7AED41D19433D4B2`, so no simulated behaviour moved. The challenge system's exact times (`NextOfferSeconds`, `DeadlineSeconds`), the carried object and the
  challenge update on every park turn (ECON-055, ECON-058) add optional save fields that are written only when they differ from the defaults. Before that,
  `0x7AED41D19433D4B2`, schema 20: the world generator (DET-013 to DET-015). Guests, staff, the economy's staff pool and the scripts draw from the original's one
  generator (`0x516330`), so `world_rng_state` is one state where there were three; a guest is created with the original's draw order (base speed, the eight starting
  values, the reseed with its id `0x4FB19E`, the sprite variant and, for an arrival, the entrance side and sideways byte), so its speed, offsets, lane and every draw
  after it move, and the staff pool, the scripts' RAND and the bump and tour rides move with the shared stream; placed objects' scripts draw from the generator
  instead of a per-placement seed. The park save gains the optional `WorldGenerator`; no other field. Before that,
  `0x09894103FAFB8D45`, schema 19: tp.exe's path links, queue cells and queue walk (PATH-004, QUEUE-006, QUEUE-003) on top of the loan offers, the ticket booth, tp.exe's guest walk and the staff candidate draws (`0xA2FAA54FEB56DA05`; main's pin after the staff walks, #229, left it unchanged). The synthetic park's queue now carries the links the queue tool writes (the link writer `0x5348D0` from the commit link pass `0x52744E`: the front toward the entrance, each cell to the one before it, the back to the path cell it ends on, with the cells' queue links `+0x0D`), and guests reach the back of the queue by the original's queue walk and edge test (`0x4D8750`) over those links instead of OpenTPW's earlier search, so the guests' positions, the joins and the world stream move; no save field is added. Before that, `0xA2FAA54FEB56DA05`, schema 19: the bank's stored credit test (`0x4D06B0`, run when the loans dialog opens or a tab is selected)
  decides which offers the Available tab lists, and the park save gains the optional `AvailableLoanOffers` field, so the hashed save
  text moves, on top of the ticket booth, tp.exe's guest walk and the staff candidate draws (`0x944788E41E278016`). The loan-offer field
  on the walk and the staff draws only was `0x650CEA6712B82D88`, on the staff draws only `0x0C9F0042AD44A3E3`. Before that,
  `0x944788E41E278016`, schema 19: the ticket booth no longer reads the guest's purse (#207) on top of tp.exe's guest walk and the staff
  candidate draws: the booth books the fee with the bank (tp.exe `0x004FFADE` → `0x004D0600`; the purse +0x1A0 is never touched), so the
  guests' money and the world stream move. The booth change alone, off `0xD28386DACAD73241`, was `0x9AA9973FC2A6A5D6`, and on top of
  the staff draws only `0x89123A798D58778F`. Before that:
  `0x4A4014649DF391DB`, schema 19: tp.exe's guest walk on top of the staff candidate draws, pinned on separate branches off
  `0xD28386DACAD73241`. The walk alone was `0x0B7C79BA1B5AC12A`: guests walk the park with tp.exe's walk: the route search `0x511420`,
  the edge test `0x4d8750`, the step commit `0x510510`, avoid_walls `0x50e4a0` and the walk's progress `0x50fd40`, in tp.exe's
  fixed-point arithmetic (multiply rounding towards −∞ `0x50df70`, divide towards 0 `0x50e080`), and cell type changes stamp their
  blocks (`0x4d8c60`), so the guests' positions and the world stream move. The staff draws alone were
  `0xDB1DDEE4817DE5B0`, schema 19: a staff candidate draws as tp.exe does (`0x507600`): the variant (`0x50761c` via `0x541f70`), a
  discarded draw (`0x50762d`), the grade (`0x507638`), the name (`0x507580`, at most 15 draws while `0x5083f0` finds it on a pool
  candidate or a hired member) and then the timeout, so the world stream moves; and `0x0F080CC6C6923088`, the loan-offer save field
  alone. Before those:
  `0xD28386DACAD73241`, schema 19: the guest's energy meter is tp.exe's field `+0x1B8` (0 at birth, changed only by `0x004FD970`), the
  attraction choice walks the objects newest first, a full queue costs no happiness and the use routine `0x004FE1E0` applies its
  remaining terms, so the guests and the world stream move. Before that:
  `0x4AEFBABAD786E2FD`, schema 19: guests skip a ride whose queue is full (4 × cells, tp.exe `0x004DD920`) when they choose, and a queued guest
  turns only within 30 turns of its last interlude, by −400..+399 of 2048 (`0x004FFFF0`), so the guests' choices and the world stream move. Before that:
  `0x611EFAD61F9A8D68`, schema 19: each staff candidate draws its own timeout, `StaffTimeoutTime` + R % (`StaffTimeoutTime` / 2) units of
  four park turns, and the pool refills on the first turn past 4 × `TimeBetweenStaffUpdates` turns (tp.exe `0x00507600`, `0x005084F0`), so the
  candidates and the world stream after them move. Before that:
  `0x9F437861FA2E5452`, schema 19: a ride keeps its broken-down state (QUEUE-019) in the hash, the booth's verdict on the fee
  (tp.exe `0x004FF5B0`) draws from the guest stream, and the invented boredom loss is gone (nothing in tp.exe reads BoredomThreshold; docs/GUESTS.md). Before that:
  `0xCAF8114002723B8C`, schema 18: the bank's withdrawals flag, balance after the last debit and turn-entered-red stamp join the
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
- the default seed gives the same world generator start state (`WorldSeed.GeneratorState`); per-park attraction ids;
  unseeded scripts draw from the park's one generator; the `WorldGenerator` save field round trip and older saves (with and without a `World` section);
- the two guest-path digests moved with the generator (`PathAssetTests`: 22 → 21 admissions, `0xF55B1DF82DFF7F3F`; `PathBuilderTests`: 61 → 60 admissions,
  `0x4AFCF82F0A7796C1`): a guest's reseed and its entrance draws move every offset, lane and walk, and each guest's admission with them;
- the source guard above.

`DeterminismAssetTests` (`OPENTPW_GAME_PATH`): original jungle objects get
attraction ids 1… in each of two parks in one process, with equal script hashes;
and `ParkWorldStreams.SavePark`/`LoadPark` (the code behind `Level.SavePark`/
`LoadPark`) with the real jungle park runtime and guests: every stream drawn
after the save, the load restores all of them and the saved economy. `Level`
itself needs a renderer and is not constructed by tests.

### Thing ids (WALK-010)

A guest's id is a slot of the park's one thing pool of 10239 (`ThingPool`, tp.exe `0x516270`): see docs/GUESTS.md, "Thing ids". It enters the digest in two places: the guest's own id, and the pool's next free id in place of the former guest counter
(`GuestSimulation.AddCanonicalState`). The pinned runs moved (old → new): the fixed synthetic run `0x01946A39A53F851E` → `0x5F177F986FADADB1`; the Jungle guest run of `PathAssetTests` (21 admissions) `0xF55B1DF82DFF7F3F` →
`0x30F213E46933EB8C`; the cross grid of `PathBuilderTests` 60 admissions, `0xD2F05F91D6567215` → 61 admissions, `0x397F3EC43F6F70C8`. A standalone `GuestSimulation` owns a pool of its own (ids from 1), so the guest-only tests keep their ids; the synthetic park
mirrors the level (ten singletons, gates and lights first). Saves: guest snapshot schema 8, the optional `Things` part and `ThingId` fields of the park save (docs/PARK-SAVES.md); an older save loads, its guests keeping their counter ids as slots.

## Approximations

| ID | Rule |
| --- | --- |
| DET-013 | scripts, bump and tour rides and rider heads draw from the one world generator with the original's projections, a park turn takes its draws in the original's thing-sweep order and the substep scheduler (script pass newest first, one slice per eight passes, tour cars on even substeps, bump module every substep) is ported; but a script created with an explicit seed (tests, older saves) keeps a private generator, an imported original save's scripts take its ids, list order, pass counter and run state on the saved game clock (the program counter, shared stack and heap, variables, limbo table, result register, TURBO/DIPMUSIC/reverb/speed words, WALKST_FLOAT state and the WAIT, WAITANIM, WAIT4ANIM and SETTIMER deadlines, `RideVM.ImportSavedRunState`), and their objects take the saved animation channels and node bits of the RideSystem records (`OriginalObjectRuntime.RestoreSavedScene`, loader 0x4647A0), but not a control held on its first frame (flag 0x2) or on the unscaled clock (flag 0x40), the RSSE records' object node tables, bounce and walk tables or the object lists, and a script saved inside a TRIGWAITANIM triggers its animation again, and a script with a saved child or sound child keeps its fresh state; saved child records are left for their parent to recreate, and the host's visitor step, the coasters, the animators and the effects system 0x520130 stay per 60 Hz tick |
| DET-014 | guests draw from the world generator in the original's creation order and at the implemented arrival-walk, sideshow-win tail and balloon-recreation sites, and reseed with their thing id (a slot of the park's thing pool, WALK-010); however, outside a pooled park-turn sweep, `Level` calls `GuestSimulation.Tick` when the park is not lost, which updates guests in list order between turns as well as in standalone simulations, rather than limiting guest updates and their draws to the original's park turn |

The full text of the two is in the register (`DeterminismApproximations.cs`). They were narrowed, not retired: the park turn's order and the substep scheduler are ported; what is left is above. DET-015 is retired (October 30, 2026): the role searches run at the placing click and the researcher flag is ported, and the only difference left, the headless economy's first-update search (no staff world in tests and tools), is `[EXT:headless-economy]`.

DET-014's missing-draw claims were reconciled against source on October 27, 2026 (`60c941de233ac45a0d8c7d680aef2269f655b8b6`), not against a new original-game capture:

- `GuestSimulation.ApplyVisit` already takes one discarded world draw at the won sideshow's tail (`0x4FE8D9`). A lost play skips that use routine in `FinishUse`.
- `GuestSimulation.FinishUse` already reseeds with the zero-extended 16-bit guest id and takes two draws when giving back a live balloon after a non-balloon-shop visit, unless the guest is still in a swim session (`0x502053`..`0x502079`). These are not draws to add again.
- The arrival walk already takes its later lane/stand-point picks and gate-wait draws; the older table's claim that those sites were absent was stale too (see the arrival-walk account above and `GuestSimulation.ArrivalWalk.cs`).
- The scheduler boundary remains unresolved: `GuestSimulation.Tick` calls `UpdateGuest` in list order, `SweepGuest` calls it for one thing, and `Level.SimulateTick` still uses `Tick` outside a pooled turn sweep when the park is not lost. Correcting the descriptions establishes neither native inter-turn equivalence nor a turn-only RNG transcript.

This reconciliation changes the annotation and register descriptions only. DET-014 remains active; no guest logic, random calls, scheduler, save format or deterministic pin is changed, and no new runtime test or original-fidelity result is claimed.

DET-016 is retired (October 14, 2026): the economy-only park save is the legacy `ParkSaveFile` (`[EXT:legacy-economy-save]`), used by the
determinism tests; the player's save (`GameFlow.SaveCurrentPark` → `ParkResumeSaveFile`) stores the economy and its streams, the layout,
the guests with their queue bookkeeping, the script world and each object runtime, the advisor, the coasters and the tracks (docs/PARK-SAVES.md).

The port followed these requirements (tp.exe, static evidence): the one world generator is `s = rotr(s * 1664525 + 1013904223, 13)` returning
`abs((int)s)` with `int.MinValue` staying negative (`0x516330`; the test vectors are the instructions executed in an x86 emulator, see
`WorldRngTests`); one `WorldRng` per park holds the u32 at world+0x1DA708; `0x516370` reseeds it with the zero-extended u16 thing id at eight sites,
of which OpenTPW follows `0x4D71CA` and `0x4D94E2` (litter), `0x4FB19E` (guest creation: base speed draw, eight draws, the reseed, the sprite variant and, for an arrival,
two more draws) and `0x4FE6A1` and `0x4FE6FC` (costume and balloon) and `0x50205F` (the balloon given back after a ride: the reseed and two draws, `0x502053`..`0x502079`; no pinned run has a balloon, so no pin changed), and does not follow `0x4F7B7E` (a person thing OpenTPW does not model) nor `0x4FCAFD` (the sprite lookup `0x4FCAC0`, called from the drawing routine `0x44B410`: when `0x541F60(3)` returns 0 it reseeds with the thing id and draws, so the original's generator depends on what is drawn; OpenTPW keeps drawing off the generator); the
state is saved as `mRandomSeed` (`0x517123`, `0x517DFC`) and an original save's word is imported; the consumers share the stream with their own projections (RAND
`labs(labs(r >>> 1) % (int16 + 1))`, FINDSCRIPTRAND `(labs(r >>> 1) % n) + 1`, ADDHEAD `labs(r >>> 1) % length`, tour and flying cars `labs(r) % 100`). What is not ported is
the original call order (substeps and the RSE pass schedule, things newest first, economy month end), which the right sequence needs and not only the distribution.

The remaining IDs proposed in DET-plan §4.6 (DET-001, DET-003 to DET-011) belong
to the substep scheduler and the original generators. DET-I2 will register them
with their code.

### Faulted scripts

Since the root-fault containment (`[EXT:vm-fault-containment]`, docs/RSE-VM.md) a script that faults stops advancing its
clock, and its object releases its riders, trains, controller and sounds. Runs without a fault hash as before (the pinned
digests are unchanged); a run in which a script faults now hashes differently from before that change, because the faulted
script's `TimeMilliseconds` stays put and the released riders change the guest digest.

Coaster tool panel (October 15): the coaster track hash gained the cut (`CutAfter`, only while the circuit is cut open) and, for a node with a hand-set yaw or incline, those two values, so every hash of a coaster without hand edits and without a cut is unchanged. The hand edits, the cut, the stack swaps and the panel's money settlements are simulation-side state of the owner's own tool; the panel's pointer scale and picking are host input.

Park signs round (October 16): sign paint masks are now shaped as tp.exe 0x5EBF10 shapes them (offset copy, erosion or dilation by
the mask word, the separable Gaussian by the parameter), so the composed sign halves of every sign with paint changed: 144 left/right
half hashes of the frozen sign baseline and the five signs pinned in `RidesRound2CorpusTests` were re-pinned deliberately (no sign
line, text pixel count or `.sgn` hash moved). Sign pixels are presentation only and never enter the world hash, so the pinned fixed
run is untouched. Catalogue signs now draw the info-record name (empty for an Info.Id outside tp.exe's table), also presentation only.

Sign precision (October 17): the sign blurs, bevel levels and relief arithmetic run on emulated x87 registers at 24 mantissa bits (`Ext`, `X87Precision`), the precision tp.exe's thread runs at, instead of in double precision. 72 of the 75 frozen sign halves and the five `RidesRound2Tests` signs were re-pinned; no line, text pixel count or `.sgn` hash moved, and sign pixels still never enter the world hash. The arithmetic is integer-only (UInt128) apart from sine, cosine, 2^x - 1 and log2, which come from `System.Math` in double precision (the x87 computes them in extended precision, so a last-bit difference there could only matter at a rounding tie).

Wavelet boards (October 17): the sign boards decode with tp.exe's own arithmetic, instruction for instruction at the thread's 24 bit x87 precision (the header scale on each coefficient, the D4 taps computed at 53 bits and stored as float32, the row and column inverses in the original's operand order, the colour conversion's chroma terms once per 2x2 block, the alpha truncation). The 28 left/right half hashes of the 14 frozen signs that have a wavelet board and the five `RidesRound2CorpusTests` signs were re-pinned deliberately (no line, text pixel count or `.sgn` hash moved); alpha moves by at most one level and colour components by one level, and sign pixels never enter the world hash, so the pinned fixed run is untouched.

The park's name (the gate's two sign lines) is saved in the world save's optional `ParkName` part, but it is not hashed: renaming the
park changes no simulation state, and a save from before the part restores with the gate's default lines.

Sign text round (October 17): the sign text raster is reduced as tp.exe 0x5EAFC3 does (the 2x2 box, four bytes summed and shifted right by 2), the surface, glyph masks and paint shaping run 256 rows high with only the top 128 packed (0x5ECB40), and the relief is shaded from its effect's shape (0x5EB820). Every left/right half hash of the 75 signs in the frozen sign baseline and the five signs in `RidesRound2CorpusTests` were re-pinned deliberately (the causes are in the baseline's header); the text pixel counts of eight signs moved once, for the raster reduction (jungle 1102, hallow 2103, 2111 and 2118, fantasy 4108, 4140 and 4425, space 3190), and so did that of hallow 2601 in `RidesRound2CorpusTests`. No sign line or `.sgn` hash moved, sign pixels never enter the world hash, and no save, RNG draw or determinism pin changed.

Sign text antialias model (October 17): the glyph coverage of sign text is now the documented GDI grayscale model (4x4 point samples per pixel, 17 levels, byte `(level * 255 + 8) / 16`, no gamma, no hinting; `TrueTypeRasterizer`, `CoverageModel.GdiGray4`) instead of 16 linear sub-scanlines with exact span ends, so the 150 left/right half hashes of the 75 frozen corpus signs, the five signs of `RidesRound2Tests` and the 75 text pixel counts were re-pinned deliberately (no line, `.sgn` hash, world hash or save changed; sign pixels never enter the world hash). The rasterizer is plain IEEE arithmetic with fixed sample positions, so the hashes stay deterministic. `TrueTypeFontTests` (original data, `OPENTPW_GAME_PATH`) was re-pinned for the same cause: the SHA-256 of the 32-pixel-em 'A' of each of the 17 original fonts and the hash of the 'Lost Kingdom' gate name line (Young Itch AOE, 144 px em, `SignTextLayout.Rasterize`; the TSV header does not cover it) moved with the coverage; the font file hashes, glyph metrics, bitmap sizes and origins did not. `TrueTypeRasterizerGoldenTests` now passes `CoverageModel.Linear` explicitly, so its goldens did not move.

**Spacing of bodies in the water.** Free swimmers keep 0.7 cell (`MinimumDistance`) from every body in the water of their region and steer from 1.2 cells, the pool takes one body per water cell (at least 2), river floaters keep a gap along the lane, the jets give each guest a spot and the landings wait for a free zone (POOLS.md, "Spacing of bodies in the water"). All of it is integer or 16.16 arithmetic over positions and saved fields (the activities' `InWater` reads the guest's saved phase only), takes **no** random number (a wait is a counter in the saved turns, a jet spot comes from a fixed list), and the bodies are found through a per-tick cell lookup in guest-list order. `FixedRunHash` (`0x2A8418A90725699A`) and `PoolsFixedRunHash` (`0x3492A988F0EF4AD7`) stay at main's values (a park without a changing room reaches none of it). The three pool pins that run swimmers moved relative to main (after #358): `PoolsChangingRoomFixedRunHash` 0x42E916DFC15CED19 to 0xA83721661FA72F69, `PoolExtrasFixedRunHash` 0xD8806A8911B990C7 to 0xEEEDBDE12564B670, `PoolsAttractionsFixedRunHash` 0xC7370809D0D43A0B to 0x3FEC2717D892094A, for the reasons given with each pin above. `FixedRunWithAPoolAndAChangingRoomIsUnchangedByTheFullActivityRegistryWithNothingToOffer` still equals the changing-room pin, and the mid-swim save and load tests still match tick by tick.

## Land and cell types (round 9 package H, October 27, 2026)

The park fence is laid and extended by integer routines of tp.exe ported as they are (`FenceEngine`: the click handler's line walker, the ring walk, the fill and the purchase), the demolition leaves the cells `0x5367A0` leaves (`ParkCellMap.DemolishFootprint`: the whole flag word and the owner of a removed object's cells are zero, not only the type) and the queue tool refuses a hoarding post's block (`QueueCellValidator`). None of it draws a random number or reads a clock, and **no pin moved**: the fixed-run hashes, the cross grid and the Jungle guest run keep their values, because the shipped fences give the cells of the old fill model (the four restart saves are reproduced cell for cell, `ParkFenceCorpusTests`) and the pins demolish nothing next to an entrance. A purchase on which the original crashes or hangs is refused before it writes anything, so a refused purchase leaves the digest as it was. The cell map's digest still hashes the ring, not the structure records: a save keeps the ring only (the layer is derived from it, equal to the live layer in every golden case).

## Umbrellas tuning

The canopy size, the pull cap, the renderer's per-frame work and the measurement aids of the umbrellas tuning (phase U5, [UMBRELLAS.md](UMBRELLAS.md#tuning-phase-u5)) are presentation or test code that reads the simulation and writes nothing into it, so they move no pin. The owner chose Theme Park Inc's kid score of 75 % over the 100 % OpenTPW had shipped. That value is now `GenerationModifier.KidScorePercentage` in the four `Brolly.sam` files and `tools/umbrellas/brolly.sam`, and `SyntheticPark`'s `brollyKidScore` defaults to 75 so a synthetic brolly shop mirrors the shipped data (the headless harness, `UmbrellasTuningMeasureTests`, still passes 75 and 100 explicitly and is not pinned). The shop pins were recomputed on purpose and held, because those runs give nobody a balloon and the measured week sells the same at 75 % and at 100 % then:

| Pin | At kid score 100 | At kid score 75 |
| --- | --- | --- |
| `UmbrellasFixedRunHash` | `0xE912E7E5D5B6E612` | `0xE912E7E5D5B6E612` |
| `RainMoodWithUmbrellasFixedRunHash` | `0xADEE2FA6AFC2ACE3` | `0xADEE2FA6AFC2ACE3` |

`RainMoodFixedRunHash` (`0x3697B9094432D152`) and every pin with no brolly shop are unchanged: the kid score is read only for a brolly shop.

## Original coaster import

The original coaster import (October 19, `OriginalCoasterImport`, docs/TPWS-PAYLOAD.md "The import") moves no pin and
adds none. Every original save in the corpus holds an empty Coasters block, so no park takes the new path. The import
draws nothing from `World.Random` or the coaster generator (`CoasterWorld.Random`). It rates the track at the
constructor's speed setting and keeps the saved trains. The first frame after the load then runs the controller's first
motion tick, which takes no time. After the station script's first slice, the slice tail passes the script's speed word
(`0x5517D7` → `0x43B2B0`, `OriginalObjectEffects.AfterSlice`), which re-rates the track and lays the trains out again when
it differs from 60.

The PR first pinned a fixed run of an imported synthetic coaster (`ImportedCoasterPin`, `0x0B6F305C6589A229`). That pin
held on macOS arm64 only: the Linux and Windows x64 runners compute `0x43373F0B3F89E03C` for the same run. The coaster's
curve, rating and collision pass called libm functions (`Atan2`, `Sin`, `Cos`, `Acos`), whose results are not bit-equal
across platforms. The pin was removed then and is back since DetMath ("Cross-platform coaster pins" below).

The same review ported the controller's first motion tick (`CoasterRide.MotionClockStarted`, controller flag `0x10`):
tp.exe's motion tick `0x43CE20` takes the animation clock's time since its last run as the frame time, and the tick that
finds flag `0x10` clear sets the flag and that clock word first, so it takes no time (`0x43CE85`). That is the first tick
of every new or loaded controller. Every train makes one partial hop of no length: it stays where it stands, while its
speed is capped, decays by 0.95 below 2.0 and stops below 0.01. Before, OpenTPW's first tick moved the trains by a whole
step. The coaster hash content therefore changes twice: it holds the flag, and a coaster's trains run one step behind from
their first tick on (any park with a running coaster hashes differently). No pinned golden or resume hash in the suite
depends on a coaster's content, so no pin moved; `CoasterTests.ADepartureFromRestDoesNotReachTheMinimumSpeedInOneFrame`
now measures the second tick. The park-resume save carries the flag (optional; a save from before it counts the flag set
on a track that runs), so a resumed coaster continues as the one that kept running.

The speed setter now runs after every slice of a station's root script that holds its coaster, as the original's loop
does. For a coaster OpenTPW built, the speed word equals the setting after `COAST 8`, so nothing changes until an upgrade
changes the script's speed word (RIDES-033); then the coaster takes the new speed after the script's next slice, as in
the original, where OpenTPW before kept the old one. No pinned run upgrades a coaster.

## Coaster clock catch-up

The coaster's motion tick now takes as its frame time the fixed steps since its last motion tick
(`CoasterRide.MotionClockLagSeconds`), as tp.exe's `0x43CE20` takes the scaled game clock since its clock word `ctl+0x120`
(October 19). A running coaster's tick follows the one before it, so its frame time is one step, bit-equal to before
(the lag is the sum of the steps in double precision, cast to single precision once). Only a coaster whose track stood cut or
reopened (state 1, which runs no motion tick, `0x435AC9`) moves differently: the first tick after the track is completed
again takes the whole stretch, so its trains run further in that tick and every later state differs from before. The
fixed steps are the original's scaled game clock, as the substep scheduler maps it: they stop while the park is paused and
multiply with the game speed. What stays OpenTPW's own (`EXT:coaster-fixed-step`) is the frame: one motion tick per 60 Hz
step instead of one per rendered frame, and no zero-time ticks while the clock stands still (the original runs them on
every paused frame).

The coaster hash gains the lag and the speed setter's replay bit (`CoasterRide.SpeedSetByReplay`, controller bit `0x8000`,
set only by the original's layout replay, which OpenTPW does not run), so any park with a coaster hashes differently from
before. The park-resume save carries both (optional; a save from before them holds no lag and a clear bit). No pinned
golden or resume hash in the suite depends on a coaster's content, so no pin moved.

## Cross-platform coaster pins

Since DetMath (#697) the coaster's curve, rating and collision pass evaluate their elementary functions identically everywhere,
and the remaining System.Numerics arithmetic of the coaster's simulation path now goes through `DetMatrix` (October 20): the
pylon rig's clip blending (`Vector3.Lerp` and the slerp's `MathF.Acos`/`Sin` before) and the station frame of a placed station
model (`Matrix4x4`'s product before, which may fuse the multiply-add). A coaster's content is therefore a cross-platform digest,
and it is pinned again:

| Pin | Value | What |
| --- | --- | --- |
| `OriginalCoasterImportTests.AnImportedSyntheticCoasterRunsDeterministically` | `0x010C244879E72489` | the imported synthetic coaster's minute (the #702 pin, `0x0B6F305C6589A229` on macOS and `0x43373F0B3F89E03C` on x64, moved with the coaster hash's two new fields, the motion clock lag and the replay bit) |
| `CoasterTests.BuiltCoasterRunsArePinned` | `0x0FE29AA5CBF09FDB` | a built synthetic circuit open for a minute with a guest on board |
| `CoasterTests.BuiltCoasterRunsArePinned` | `0xE71F6F7026ACA2B3` | the same circuit reopened for three seconds at 20 s and completed again (the catch-up tick) |

CI checks them on Windows, Linux and macOS and in both browser legs (Mono AOT and the interpreter). The synthetic coasters have no pylon or station models; a coaster built from game
data adds the rig and the station frame, which `DetMatrix` covers. The rig's rest decomposition (`Matrix4x4.Decompose`) and its
`Matrix4x4.CreateFromQuaternion` stay System.Numerics': they use the unfused `Dot`, `Cross`, `Length` and scalar arithmetic. The station frame's change from the fused product
moves a station node's position by at most a few last bits; no pinned golden builds a coaster from game data, so no other pin moved.
The animator itself (`ObjectAnimator`, presentation and the object scripts' poses) still uses System.Numerics.

## Historical pre-ExitRide retained-target diagnostics (2026-10-20)

These mappings apply only to the pre-ExitRide diagnostic baseline. PR #678 changed the guest behavior and its pins; the main baseline default is `0xF3F895E3EF2F969B`, schema 21. See the fresh joint-baseline reconciliation below; do not reuse the historical values as its oracle.

GUEST-004 retains the refused full-room target until replacement or removal. `AttractionId` already participates in the canonical hash; the hash schema and production hashing are unchanged. The diagnostic head `c101b4abb9c54256ec6000d26035eec02d539d1b` projects only retained target identities to zero without advancing the simulation, restores them in `finally`, and leaves each actual pin assertion intact. All 30 failing assertions on each of macOS, Windows and Linux recovered their individual previous expected hashes under that projection (jobs `118523918197`, `118523918216`, `118523918184`, run `39391350115`). Browser interpreter actual hashes match the same observed mappings (job `118523539167`, run `39391350411`); its harness does not emit the projection log, so the retained projection assertions must establish that check in the next run. This is endpoint equivalence, not a claim of native-game parity. The default fixed run also has a paired per-tick legacy-cleared replay which checks complete world hashes and RNG and recovers its original pin.

The obsolete pre-ExitRide executable lookup was removed because it is not a valid oracle for the new baseline. The diagnostic retains current-main expected pins, complete hash restoration and repeated-read checks, and the default paired replay asserts the current-main legacy-cleared pin. Direct concrete hash reads are compared with delegate reads before, during and after target projection; the later joint-baseline reconciliation below uses only freshly observed values.

The following historical mappings are not valid evidence for the post-ExitRide integration. No runtime hash field is ignored. Fresh cross-platform projection evidence, rather than this historical table, governs joint-baseline pin changes.

| Pin | Previous target-cleared hash | Retained-target hash |
| --- | --- | --- |
| FixedRunHash | `0x5D8385D13CC3BB9B` | `0x60020C37ABCABF02` |
| PoolsChangingRoomFixedRunHash | `0x2E9AA1D5A79E1FB2` | `0x4A5E388817DC9C20` |
| UmbrellasFixedRunHash | `0xE912E7E5D5B6E612` | `0xFD4D4FA2BC53FB5A` |
| WinterFixedRunHash | `0xA66540FF8C2AC6DE` | `0x04E34ADC1294F043` |
| JungleDecemberOffFixedRunHash | `0xF4A00471E60B9535` | `0xD4B54BFD2A1A3B40` |
| JungleDecemberWintryFixedRunHash | `0x296DA4DA6A8BE323` | `0xAFC63547CAB25B4D` |
| WinterAlwaysFixedRunHash | `0x5F767F69EF5CBF07` | `0x5965F2218331DD31` |
| RainMoodFixedRunHash | `0x3697B9094432D152` | `0xD07DB488E3A1FC4A` |
| WinterColdEverythingFixedRunHash | `0x45FCB3FC30C601FC` | `0xB3D783A30895BD7C` |
| PoolExtrasFixedRunHash | `0x20DED2AC4BEC293A` | `0x2006BE5B455712F4` |
| PoolsAttractionsFixedRunHash | `0x2D7EE079E5A9B09F` | `0x0B29FCE4C3ADFA82` |
| SmarterGuestsFixedRunHash | `0xE7D4BED7787E4748` | `0x0A277921CBEAB355` |
| ScriptModsFixedRunHash | `0x8AB18F8AC449CC49` | `0xECA942D0405666C8` |
| ScriptModsAllHooksFixedRunHash | `0x6B74FB3AE8A4937F` | `0x3F3FBCA22B2A3F26` |
| PoolsFixedRunHash | `0x8EE352E1FD1EEAB3` | `0x24E20D8037EFED67` |
| ScenarioBinsSmarterFixedRunHash | `0xF43AEA85CC6DB665` | `0xF0B70A41A0CF4F7A` |
| ScenarioBinsPhase6FixedRunHash | `0x8E13F761512B0043` | `0x7F5CE116FCF4ACE2` |
| CheatsFixedRunHash | `0x5CBF8584199279AB` | `0xC75F009A73FB11B7` |


## Joint ExitRide retained-target pin reconciliation (2026-10-20)

Baseline main `9b8943a9` includes ExitRide #678 and presentation-only balloons #532. Diagnostic head `0f619d6bd9485a6993002d82ef279b878691c429` recovered all 32 failing desktop pin assertions on each of Linux, macOS and Windows by clearing only retained refused target IDs at the endpoint. The 18 distinct mappings below agree across those runs. All 30 browser pin failures in each of normal interpreter, traces-off interpreter and AOT use the same observed actual values. Direct/delegate, repeated-read and full-restoration guards passed on this diagnostic source shape. This does not establish the cause of earlier interpreter-only instability or universal intermediate behavior equivalence. The default paired per-tick hash/RNG replay remains an independent guard against the original main default.

Evidence: [desktop run](https://github.com/meneerkrabs/OpenTPW/actions/runs/39410581238), [browser run](https://github.com/meneerkrabs/OpenTPW/actions/runs/39410581259). The candidate updates exactly 18 observed constants and adds an inverse projection guard retaining each corresponding main value. Secondary values not reached by a failed pin assertion are deliberately excluded. Newly exposed later assertions and the new guard still require fresh CI. The candidate default is `0x29CE354FAB027ABF`, schema 21; production hash inputs remain unchanged.

| Main baseline | Observed retained-target value |
| --- | --- |
| `0x18440BAB5DB3D443` | `0x0FAA0BA10A2E2B43` |
| `0xF3F895E3EF2F969B` | `0x29CE354FAB027ABF` |
| `0x347F0BA1F0DF5DFB` | `0x23A31F409845D585` |
| `0x925B87221BE9AF53` | `0x9429690D329115B6` |
| `0x8171CE2F7537C034` | `0x7B6BD0C81ED66C35` |
| `0xC9F860B54E30EB11` | `0x6FDBB79D8B9377BB` |
| `0x0C36671C5B99F0CD` | `0x4F2841D3F85402FC` |
| `0xBCD73D82A2F5D0D4` | `0x4098DC9D36A6D8CD` |
| `0x2C8B850C069307BB` | `0xEB1C231F596B30D2` |
| `0x0E6B528D92E00145` | `0x547522BF092EEC0C` |
| `0x2BCAC8B1DC03F064` | `0x48F18D1BED795051` |
| `0x5CFE6494C7706870` | `0xE88AD1079B2559A5` |
| `0x1B5829884FE0659C` | `0xBD827362B8D9DC5C` |
| `0x1A54C3057DBEED2E` | `0x905024CE0B91E577` |
| `0x995AD10275D35532` | `0xF85EB64C10598DB6` |
| `0x2FB7EFA85DBAD719` | `0x3D741AAF6928D531` |
| `0x7539BB3BB9729317` | `0x5AC8D8A47AAC35D2` |
| `0x3BAFF5A07D0A82F6` | `0x4AAC7DD831EFD113` |

The first reconciliation head `48314a81f1d59f4a8696330af00afcacae297dbc` passed those 18 inverse guards and exposed four later assertions covering exactly two additional constants. Each of Linux, macOS and Windows recovered all four by the same target-only projection; normal browser interpreter, traces-off interpreter and AOT agreed on the same actual values, with no guard failures. Evidence: [desktop run](https://github.com/meneerkrabs/OpenTPW/actions/runs/39413440835), [browser run](https://github.com/meneerkrabs/OpenTPW/actions/runs/39413440819). The followup changes only those two constants and adds their inverse guards; current-head CI remains required.

| Additional main baseline | Observed retained-target value |
| --- | --- |
| `0x071D0789F44BE723` | `0x812FCC248B729C28` |
| `0xD2AF869CDF9A7D3D` | `0xEF00F0D64A9077FE` |

## First day message of a new park (ECON-063, October 26, 2026)

A new Theme Park World park now posts the day message its first update owes (tp.exe's calendar constructor `0x4F7E80` stores day cache -1; the world
init `0x5156A0` syncs only the month and year caches; the first update `0x4F8260` posts one day message whatever the date). `ParkEconomy.PendingInitialDay`,
already carried by the restart imports and own saves made before the first turn, is set by the constructor (Theme Park Inc keeps its synchronised start).
The first park turn therefore runs the day step once more than before: every object's six rings roll from head -1 to 0, the running daily average of the
challenge system accumulates and the day listeners run. Every fixed run of the synthetic parks reaches that turn, so every pin moved; nothing else changed
in them. A save written before this change loads as before (the optional field is absent, so no first day is owed).

Pins moved (old → new), re-pinned by the same run on the same code: the fixed-run hashes of `DeterminismTests` and `CheatsExtensionTests`, and the target-only
projections of `GuestRefusedTargetTests.JointMainProjectionPins` (the key is the new joint hash, the value the new projection):

| Pin | Before | After |
| --- | --- | --- |
| `CheatsFixedRunHash` | `0x0FAA0BA10A2E2B43` | `0xAF6434D38684D562` |
| `PoolsChangingRoomFixedRunHash` | `0x23A31F409845D585` | `0xF29FC9EE9FA2CBC4` |
| `UmbrellasFixedRunHash` | `0x9429690D329115B6` | `0x5467A6E95F5BC676` |
| `WinterFixedRunHash` | `0x7B6BD0C81ED66C35` | `0x0AECF3233FBD5294` |
| `JungleDecemberOffFixedRunHash` | `0x6FDBB79D8B9377BB` | `0x1CDA6A275C9F3F5B` |
| `JungleDecemberWintryFixedRunHash` | `0x812FCC248B729C28` | `0xED306F312346AEC8` |
| `WinterAlwaysFixedRunHash` | `0x4F2841D3F85402FC` | `0xA8C95CA65160807C` |
| `RainMoodFixedRunHash` | `0x4098DC9D36A6D8CD` | `0xCB5E98B00C5AAD4C` |
| `RainMoodWithUmbrellasFixedRunHash` | `0xEB1C231F596B30D2` | `0x130B524C99334012` |
| `WinterColdFixedRunHash` | `0x547522BF092EEC0C` | `0x5AEE05EB1A2D348D` |
| `WinterColdEverythingFixedRunHash` | `0x48F18D1BED795051` | `0x72FD6BD04393AED1` |
| `PoolExtrasFixedRunHash` | `0xE88AD1079B2559A5` | `0xEDBB35F77BB53B24` |
| `PoolsAttractionsFixedRunHash` | `0xBD827362B8D9DC5C` | `0x0AD27109C2E5643D` |
| `PoolsFixedRunHash` | `0x4AAC7DD831EFD113` | `0x51F09D065798C992` |
| `FixedRunHash` | `0x29CE354FAB027ABF` | `0x1AABD1698E2E75BE` |
| `SmarterGuestsFixedRunHash` | `0x905024CE0B91E577` | `0x8ACD297D0155243E` |
| `ScriptModsFixedRunHash` | `0xF85EB64C10598DB6` | `0x92910893AA42DAD7` |
| `ScriptModsAllHooksFixedRunHash` | `0x3D741AAF6928D531` | `0x46D7114F4B803468` |

The browser build pins the same constants (`tools/web-determinism`), so its page, built from this commit, reproduces them.

The first day also appends a zero entry to every object's walk-away history. The park save drops an all-zero history, so a restored list lost that entry and the next day's roll
produced another saved form than the live park's (the park-resume smoke diverged at its 80th continuation tick). The saved form now leaves out the oldest zero days
(`SkipWhile( day == 0 )`): the sums, which are all the history is read for, are unchanged, live and restored parks save alike, and no pin moves.

## Guest destinations (round 9, package G, October 27, 2026)

The pinned fixed run moved from `0x1AABD1698E2E75BE` to `0x5946C7CB09C6EEE7` (schema 21 unchanged), the largest pin change of the round. Causes, all reads of tp.exe, none adds or removes a draw from the world or guest generators:

- **The target `+0x1DC` lives until the next major choice** (GUEST-004, `0x5014E0`, `0x500900`, `0x4FCB21`): the exit of a visit no longer clears it, so `AttractionId` (hashed) names the ride a guest has just left through states 15 and 6 and 7; the leaving state's entry clears it (`0x502305`), a stuck exit walk and the dismissals still do. The retained target takes part in the removal message, which hits those guests.
- **The major destination `+0x1DE`** (`Guest.SavedMajorAttractionId`): the minor decision (`0x4FD570`) keeps the old target there and the exit walk takes it up again (state 10 to its back cell); it joins both guest digests only while set (`0x4D414A52 + id`) and the guest snapshot gains the optional `SavedMajorAttractionId`, written only while set (older saves load with none).
- **The minor decision reads the cell lists and the access cell** (GUEST-019, GUEST-005): rides stand on their anchor cell (`+5`, `+7`) in the 4 x 4 window, the route lengths start at the target's entrance cell `+0x36` and the route request ends at the best's `+0x36`, a cell inside its footprint (the guest walks into it and then asks for the back cell again, `0x4FFE16`). The bin walk ends in the bin's own entrance cell.
- **A ride without a front** (QUEUE-003): back cell 0 and size 0, so it is never scored, chosen or joined; the ride's digest gains a word only while it has no front, and its snapshot the optional `ZeroFront` (older saves load, their one-cell queue turns into the zero front at the next recompute).
- **Review follow-ups**: a front the entry link names counts on empty ground (`0x4DE130`); the queue entrance resolver takes the first entrance (`0x4DE040`); an original save imports `+0x1DC` (states 6, 7, 8) and `+0x1DE`; a wanderer that targets a removed attraction stops where it walks (`0x4FB360`). Round 2: the litter bin a guest last used stays its target (`+0x1DC`, `0x4FFF20` writes none) until the next major choice, so the bin guests of the bin scenario hash with `BinId` set in states 6 and 7 (`ScenarioBinsSmarterFixedRunHash` `0x1602A76005EAC11E` to `0x6FA232F60F4FB782` on top of the round-1 move; no other pin moved); the cached back and size (`+0x3A`, `+0x40`) join a ride's digest only while an edit left them different from the queue. Jungle 120-minute debug sim: see the PR.

The pins that followed: the pool changing room `0xF29FC9EE9FA2CBC4` to `0x83F06FBC1A0E415A`, the brolly shop `0x5467A6E95F5BC676` to `0xC4931841467FFE10`, the winter shops `0x0AECF3233FBD5294` to `0x641FF93505F9A256`, the Decembers `0x1CDA6A275C9F3F5B` to `0x8D214B5AFD655F63`, always winter `0xA8C95CA65160807C` to `0xB17F09BB7A904F0C`, rain mood `0xCB5E98B00C5AAD4C` to `0xD3A005D9F4A14A1C` (with umbrellas `0x130B524C99334012` to `0x0DA90C70BD163773`), cold `0x5AEE05EB1A2D348D` to `0x749DC8C7555DEB40` (all together `0x72FD6BD04393AED1` to `0xB239A84F4EAD3CA9`), the pool extras `0xEDBB35F77BB53B24` to `0x5B4E201F2073F5F0`, the water attractions `0x0AD27109C2E5643D` to `0x703C3B13DBCC3452`, the pool cells `0x51F09D065798C992` to `0x6A91F49737F7A592`, the smarter guests `0x8ACD297D0155243E` to `0xB0701C1E015223AF`, the script mods `0x92910893AA42DAD7` to `0x7B4D74D1C1D6C42E` and with every hook `0x46D7114F4B803468` to `0x3F61A9E2BD19D5FD`, and `GuestMeasurementTests`' scenario pins `0xA7A17A97F4D7F7D7` to `0x1BCFBE03005E3F15`, with the extension `0x37A002F5FE807C35` to `0x08332C3DE9829437`, with the food shop `0x04442F9535C03C94` to `0x3AED0FC04692B628` and with bins `0x5AC8D8A47AAC35D2` to `0x6FA232F60F4FB782` (the guests keep the bin they last used as their target, round 2), the umbrellas and rain mood across a December `0xED306F312346AEC8` to `0xE25C39E4B9063F5A`, and the smarter-guests scenario's second pins `0xBDC91FF7BA167AFF` to `0x941F50BD37B29247` and `0xEF00F0D64A9077FE` to `0x9DABF7726BD76932`, and the cheats run (`CheatsFixedRunHash`) `0xAF6434D38684D562` to `0xF150FC6BAF22E002`. The cross grid of `PathBuilderTests` (`0x9D34C006E9694818`, 62 admissions) did not move: its guests meet no ride with a cell list or an entrance cell. `GuestRefusedTargetTests` replays the run with the retained targets cleared as before: it equals the new pin except for the target identity, and the replay's own value moved with the run (`0x05C6483AF1DFF71A` to `0x307DA54FFB0EC403`).

## The walk chains (round 10, package A, October 28, 2026)

The pinned fixed run moved from `0x5946C7CB09C6EEE7` to `0xB4AEC10551DF2738` (schema 21 unchanged), the largest pin change of the round. Causes, all reads of tp.exe; no draw is added to or removed from the world or guest generators by the chains or the drawing, the wander's draw counts follow the original's counter (below):

- **One thing chain per cell, guests and staff interleaved by entry time, every state** (WALK-003; `0x4D91F0`, `0x50B6A0`, `0x4FA2A0`, `0x4FA930`, `0x5102F0`). Separation's neighbours are no longer the guests outside rides and the held (the riding and the carried guest stay on their cell's chain at the position of their last walked point, a neighbour like any other) followed by the staff in member order: they are the cell's chain head first, the five-neighbour cut falls where the original's does, and a staff member that entered a cell after a guest is ahead of it (the head tests of the booth walk, the arrival vehicle's stop cells and the pick-up test read the real head). The staff's entry numbers come from the guests' counter (`Guest.CellSerial`); a resume snapshot keeps them in the optional `StaffActorSnapshot.CellSerial` (an older save has none and files the staff oldest, first member head-most, as they stood after the guests before).
- **The speed smoothing rounds as the x87 does** (GUEST-016; `0x4FA870`): `(q + 3 s) x 0.25` with the product and the sum rounded separately to single precision, where the Mac's fused multiply-add rounded once (about one input in five differs in the last bit, which moves a speed cap now and then). The Mac form stays for the PPC oracle bridge (`GuestSpeed.SmoothFused`, `UpdateFused`, `CapsMac`).
- **The wander walks the links of any cell with the four removals** (GUEST-010; `0x4F9490`): a link to a cell of any type is a candidate unless a path cell drops queue targets, a queue cell its own queue link and every cell the exit cells; the original's counter takes the own queue link off even where no link leads that way (one draw less than the candidates would need), and a step with none left strands a guest at any point of the walk. The x86 oracle (X19) runs the real routine on 1027 synthetic inputs and agrees. `GuestPathGrid.SetPath` without connections now links path cells on both sides only (as its documentation says), which changes the synthetic park scenarios: a cell without a neighbour has no link and takes the off-path search.
- **Presentation only, not in the hash:** the drawn point is the sub-turn point of `0x4F9F00` between the start of the person's turn (`0x4FA870` saves it every turn) and its position, for guests and staff; the staff's walk step asks the sprite for the interval of its own step. The hash holds none of it.

Pins that moved with it (old to new, the tests that hold them): the fixed run `0x5946C7CB09C6EEE7` to `0xB4AEC10551DF2738` (legacy replay `0x307DA54FFB0EC403` to `0x3B23DA342C6DDC55`), the cheats `0xF150FC6BAF22E002` to `0x0A8CA22E1A9B0E08`, the pool changing room `0x83F06FBC1A0E415A` to `0xA794F0319F569CE2`, the pool extras `0x5B4E201F2073F5F0` to `0x50092F36C43AC693`, the water attractions `0x703C3B13DBCC3452` to `0x824DA576B3644CA7`, the pool cells `0x6A91F49737F7A592` to `0xA31B7485202FE636`, the pool slides `0x758CE71C17D724D0` to `0x641364DF12DED0F7`, the brolly shop `0xC4931841467FFE10` to `0x7F084940237B577F`, the winter shops `0x641FF93505F9A256` to `0x55C6A3E48DC56D9C`, the Decembers `0x8D214B5AFD655F63` to `0xE5E0857EEA5A0DE1` (wintry `0xE25C39E4B9063F5A` to `0x8B66871BC4BF3F33`), always winter `0xB17F09BB7A904F0C` to `0x9E14F4733B0DE59E`, rain mood `0xD3A005D9F4A14A1C` to `0x43818BD4AC22950E` (with umbrellas `0x0DA90C70BD163773` to `0xA6343896042E628D`), cold `0x749DC8C7555DEB40` to `0x290320BF12153E75` (all together `0xB239A84F4EAD3CA9` to `0xB5FF8D87B78B5547`), the smarter guests `0xB0701C1E015223AF` to `0xFD4DAA23C3C5C5A8`, the script mods `0x7B4D74D1C1D6C42E` to `0xB673E778DB9A7BDB` (all hooks `0x3F61A9E2BD19D5FD` to `0xD606ED998555A7E8`), the Easymode Jungle's guest run `0x11C1DCF60A32B061` to `0x08FB6348EBBF4D68` and the cross grid's `0x9D34C006E9694818` to `0xC9C26F6E02212726` (61 admissions instead of 62; `PathAssetTests`, `PathBuilderTests`), and the synthetic park scenario `0x1BCFBE03005E3F15` to `0xF94F6FACF8361AF9` (smarter guests `0x941F50BD37B29247` to `0xD1976AB915BC43CB`, with the path rules `0x08332C3DE9829437` to `0x4E45A35CE1826B00`, with a food shop `0x3AED0FC04692B628` to `0xDBD51A57EF0197EE`, with bins `0x6FA232F60F4FB782` to `0xA4A15A28A4B2F6AA`, phase six `0x9DABF7726BD76932` to `0xDC5CC27DB4E1E1D7`).

Saves: only optional fields were added (`StaffActorSnapshot.CellSerial`), and an older save loads.

## Coaster x87 (round 10, package J, October 29, 2026)

The coaster's knot chord, Bezier and derivative shapes, knot controls, node tangent (`0x431660`), auto bank, speed estimate, turn fraction, pylon height, sampler step lengths and the self-intersection pass (`0x43A2B0`, at the truncating control word `0xC7F`) now follow the PC executable's single-precision instruction order, bit-exactly against isolated tp.exe runs in the Unicorn oracle (`CoasterX87Golden`, ORACLE X40). A coaster's content moves: `CoasterTests.BuiltCoasterRunsArePinned` `0x1ABA458D0079E09C` to `0x0FE29AA5CBF09FDB` (reopened `0x045BFFE59994F3E7` to `0xE71F6F7026ACA2B3`) and `OriginalCoasterImportTests.AnImportedSyntheticCoasterRunsDeterministically` `0x8A1279434220868B` to `0x010C244879E72489`. The collision pass moved no pin (the synthetic circuit has no crossing within reach of the changed rounding).

Saves: none changed.

The Theme Park Inc weather spells (`ThemeParkIncWeatherSpells`, `[EXT:tpi-weather-spell-seed]`) take their draws from the economy's world stream, one or two at each month message of a Theme Park Inc economy only; Theme Park World runs draw nothing there, so no pin moves.

## Head-hang draw (round 10, package L, October 30, 2026)

In a theme without costume heads (Fantasy, Hallow) every head hung on a ride reseeds the world generator with the rider's thing id and takes one draw (`0x44B410` → `0x4FCAC0`: `0x516370`, then `0x541F70` with category 1). `RideScriptWorld.HeadHung` takes it at ADDHEAD (`0x554D13`), WALKON action 4 (`0x557EB9`), each coaster guest the station step seats (`0x43B70D`), a bump ride's vehicle creation and load (`0x549CAF`) and a tour boarding (`0x55D91E`), and the level switches it on from the costume heads it loaded (`RideScriptWorld.HeadHangReseeds`). No existing pin moved: every asset-free digest runs without the flag, and none covers a theme without costume heads. New pin `DeterminismTests.HeadHangFixedRunHash` = `0xE998936EFFF3BAE5` (old: none): a script world with the flag on runs a bump creation and load, a tour boarding and ADDHEAD and WALKON hangs, so the draw is under the interpreter and AOT pin jobs. The guests' kid-set draw (`0x4FB1A7`) was already taken in the original's order and is presentation only, so it moved nothing either.

Saves: none changed (the guest's `KidSetDraw` and the staff `ResumeMask` are optional, unhashed fields).
