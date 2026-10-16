# M4 gate: attractions, scripts and roller coasters (evaluator and baseline)

October 10, 2026. This is the deterministic evaluator for milestone M4, **subset
acceptance** ([COMPLETION-PLAN.md](COMPLETION-PLAN.md), "M4 — Attractions, scripts
and roller coasters"): every supported attraction goes through load, build, run,
stop and delete; every opcode the original scripts use has a unit test; unknown
opcodes are reported explicitly. It **measures** the current runtime and changes no
gameplay. A pass means the step works in OpenTPW. It does not mean the original
ride behaviour is reproduced: that needs linked original traces (COMPLETION-PLAN.md,
"Before acceptance of M3/M4 semantics"). Full M4 acceptance also needs every
mandatory attraction and roller coaster, reference traces and no unsupported
requirements; this gate reports those gaps but cannot accept them.

Code: `source/OpenTPW/Client/M4Gate.cs` (`M4Gate`: options, report, CLI and the
pure row rules), `source/OpenTPW/Client/M4GateRun.cs` (`M4GateRun`: one pass of the
scenario), `source/OpenTPW/Client/M4GateApproximations.cs` (GATEM4 register),
`source/OpenTPW/VM/RideOpcodeRecorder.cs` (which opcodes the scripts on the current
async flow execute; diagnostics only) and `source/OpenTPW/VM/RideOpcodeTestMap.cs`
(which unit test exercises each opcode). Tests: `M4GateTests` (row rules and exit
codes, no assets), `RideOpcodeTestMapTests` (keeps the opcode test map true, no
assets) and `M4GateAssetTests` (a short jungle run and the mutation checks,
inconclusive without `OPENTPW_GAME_PATH`).

## Running

```sh
dotnet source/OpenTPW/bin/Release/net10.0/OpenTPW.dll --game-path '/path/to/Theme Park World' \
  --m4-gate [--theme jungle[,hallow,space,fantasy]] [--seed N] [--window 5] [--report m4-gate.json] [--no-determinism]
```

`OPENTPW_GAME_PATH` can replace `--game-path`. The command opens no window and uses
no GPU or audio. It prints one line per row with the row's scalar evidence; the JSON
report (`--report`) has every row with its full evidence, including one entry per
object and lifecycle step.

Exit code, as for M3: **1** when any row fails, **2** when no row fails but one is
unresolved, **0** only when every row passes. M4 subset acceptance needs exit code 0.
The default seed is `WorldSeed.Default`, the seed `Level` uses; guests, economy and
scripts take their streams from it as in `Level`. A full run over the four themes
with the determinism probe takes about 2.5 minutes on an M-series Mac (two passes).

## Scenario

**Inventory.** `ObjectCatalog.Load` for each of the four themes (OBJECTS.md). Every
entry is classified by `M4Gate.Classify`:

| Class | Rule | Lifecycle |
| --- | --- | --- |
| ride, shop, sideshow, feature | offered by the build panel (`ObjectCatalogEntry.IsBuildable`: `Info.WhichUIType` 0–3, not fixed, not a tool, not an add-on; RIDES-017) and no built track | **supported**: load, build, run, stop, delete |
| fixed item | `Info.DontApplyOffset` (gates, lights, bus, ferry, seaplane, end): placed by the level | not built by the player |
| tool | no occupied cells (Mystery ride, Buy Land, Clear Land) | not an object |
| add-on | category Upgrade (`AddOn.UpgradesId`): track upgrades and the Tubes Of Zob extra tube | not offered by the build panel |
| track ride | `Bumper.WhichTrackType` 1 (kart track), 2 (water channel) or 3 (coaster track) | probed by `coasters.construction` |

**One park per object.** Each supported object gets its own headless park, composed
like `Level` and the M3 gate (OpenTPW's `Level` needs a GPU):

1. The theme's level is loaded for a Full Simulation start
   (`OriginalPark.Load`, `ParkEconomyRuntime.ForOriginalLevel`).
2. The economy is replaced by a **late-game save**: the start economy with every
   research item completed and every golden ticket earned, written with
   `ParkSaveFile.Save` and loaded with `ParkEconomyRuntime.Load`, the player's Load
   Park path. Research and tickets therefore do not hide objects from the build flow.
   Cash, prices and every other value stay the theme's standard balance ($50,000).
3. The level's entrance (MAP `InitialPath`, the `Standard.sam` arrival lanes, the
   Gates, Lights and Bus fixed items, registered with the economy as `Level` does)
   and a 14-cell path from it, laid by `ParkPathBuilder.BuildSegment` (the path
   tool's code, charged per cell), as in the M3 gate.
4. **Load**: `new OriginalObjectRuntime(entry, …)`: the main model (geometry), the
   main RSE script with its initial variables, child-script resolution and the
   animation members (`OriginalObject` creates the same runtime).
5. **Build**: the site is the gate's choice. Clicked cells around the path in all four
   rotations go through `Level.GetCentredAnchor` and `ParkObjects.Check`; an object with
   an entrance must open onto a path cell (nearest the park entrance first, then the
   shortest exit snap), one without must touch a path cell. The purchase is
   `ParkEconomy.TryBuild`. Guest attractions are registered with
   `Level.RegisterWithGuests` (a one-cell queue at the path, RIDES-028; queues are the
   M3 gate's subject) and linked to the guest payment bridge.
6. **Run**, **stop**, **delete**: below. The tick follows `Level.Update`: the open
   state into the economy, guests, fixed items, the object, the economy.

Guests arrive only in the parks of guest attractions (`IsAttraction`:
`Info.IsChoosable`, a script, not fixed, not a tool). Scenery runs without guests.

**Track rides.** Each track ride gets the same park; its station is built through the
same build flow, its script runs for one simulated minute without guests, and it is
removed. Nothing else can be done: OpenTPW has no track builder.

## Rows and how they are decided

| Row | Pass rule | Notes |
| --- | --- | --- |
| `attractions.inventory` | every catalog entry of the four themes is classified with a reason, no theme catalog is empty and no Info.Id repeats | bookkeeping: unsupported entries are listed, not failed (the plan's subset acceptance); track rides fail in `coasters.construction` |
| `attractions.<theme>.load` | the object's runtime is created and its script parses | one row per theme; FAIL names the object and the exception |
| `attractions.<theme>.build` | a site passes `ParkObjects.Check`, `ParkEconomy.TryBuild` returns Ok, the balance drop, the OtherCosts ledger entry and the record's `TotalSpent` all equal `Upgrades[0].CostOfUpgrade` (DATA), or, for an object with `UsageInfo.GoldenTicketCost` > 0, no cash and exactly that many tickets (`BIN:STP-PPC:0x100DA874`); a guest attraction's entrance and exit are reachable from the park entrance | the path must also be complete |
| `attractions.<theme>.run` | the script never faults or halts and its clock strictly increases; a guest attraction is **used**: a guest enters `Using` there and later leaves it | **FAIL**: a fault or halt; guests queued and none admitted within W; a guest boarded and was not released within W. **UNRESOLVED**: no guest chose it within W (guest choice has no derived bound). Scenery runs W ticks |
| `attractions.<theme>.stop` | after the build panel's close button (`OriginalObject.Close` → `OriginalObjectRuntime.Close`: `VAR_RIDECLOSED` = 1) the script keeps running, **no guest boards**, the riders on board are released within W and the economy record is closed | scenery: not applicable (PASS, `applicable` false). Whether and when the script reads `VAR_RIDECLOSED` is evidence only (below) |
| `attractions.<theme>.delete` | the remove tool's removal (`ParkObjects.Remove`: cells freed, the script stopped; `Level`'s handlers: `GuestSimulation.Unregister`, `Level.SellRemovedObject`) credits exactly `Upgrades[0].CostOfUpgrade × ScrapValueYearN / 100` of the object's current level for its age, or the whole `CostOfUpgrade` for an object no guest used in its first 30 days (DATA; the rule is the Mac's `0xe25e8`, ORACLE.md F21), the balance and the OtherIncome ledger rise by it, the economy record is gone, the site passes `ParkObjects.Check` again, every script of the object has left the script world, and one tick later no guest heads for, queues at or uses it | |
| `vm.opcodes` | every opcode in the RSE corpus has a VM handler and an entry in `RideOpcodeTestMap`; opcodes without a handler are listed by number, name, use count and first script | FAIL on any unknown or untested opcode |
| `vm.effect-hooks` | each hooked opcode the corpus uses is **resolved** when OpenTPW's game effects (`OriginalObjectEffects`, `VisitorRideScriptEffects`) performed it in this scenario without recording it as unimplemented, and a map entry exercises it through those effects | UNRESOLVED per opcode otherwise, with the reason; FAIL only when a scenario script faulted on a hooked opcode |
| `coasters.construction` | every track ride: station built, a track built, a car ran, it stopped, it was deleted | FAIL today: no track builder exists |
| `rides.camera` | a `CameraMode` that takes a placed ride exists (UNRESOLVED then: the gate cannot exercise a GPU view) | FAIL when none exists |
| `rides.rating` | every supported ride has its data excitement wired into the guest bridge and every track ride has an assessment | FAIL while track rides cannot be assessed |
| `determinism.same-seed` | the whole scenario runs twice in one process; the list of `WorldStateHash` values (every object's park each simulated minute and at the end of build, run, stop and delete) is identical in length and value | UNRESOLVED with `--no-determinism` |

### Bounds and where they come from

- **W, the use window ([EXT:m4-gate-use-window], retired `APPROX:GATEM4-001`).** A working attraction admits a queued
  guest within W, releases a boarded guest within W, and after closing releases its
  riders within W. W = 5 simulated minutes (18,000 ticks; one simulated minute is
  3,600 fixed ticks, EXT:m3-gate-minute). The original has no gate and no such bound, so W is
  the harness's own tolerance, not original behaviour; W is far above every cycle the baseline completed (the slowest,
  The Aerotron's, took 4,811 ticks from boarding to release). Each window starts at
  its own event (first queued guest, first boarding, closing), so a late boarding is
  not cut short. `--window` changes W.
- **Closing ends use ([EXT:m4-gate-closing-ends-use], retired `APPROX:GATEM4-002`).** No guest boards a closed attraction.
  OpenTPW's host refuses admission while `VAR_RIDECLOSED` is set
  (`OriginalObjectRuntime`: `Visitors.Attach(Script, () => !stopped && IsOpen)`); the
  criterion is the harness's own pass rule, not a claim about the original host's test.
- **The stop window.** After closing the gate watches until the riders are released
  and the queue is empty, and at least one simulated minute unless the script read
  `VAR_RIDECLOSED`, at most W. The minute is an observation length, not a pass bound.
- **Reading `VAR_RIDECLOSED` is not required.** Original scripts differ: jungle
  `Toilet.rse`, `Puzzle.RSE` and `Junspray.RSE` never name it, and space `slide.RSE`
  tests it only before its running loop (code word 6), so a closed, idle slide never
  reads it again. The stop evidence records `scriptReferencesRideClosed` and
  `readRideClosedTick`.
- **Charges and credits** come from the object's `.sam` layers (`Upgrades[0].CostOfUpgrade`,
  `UsageInfo.GoldenTicketCost`, `ScrapValueYear1..4`); the ticket rule is the traced
  purchase (`BIN:STP-PPC:0x100DA874`); the scrap rule is the Mac's `0xe25e8` (ORACLE.md F21), which tp.exe `0x4e2400` implements identically (X13).
- **Unit tests.** A unit test is an asset-free test in one of the script test classes
  (`RideOpcodeTestMapTests.ScriptTestClasses`). The asset-gated corpus run
  (`RideVMCorpusTests`, which executes 83 of the 84 corpus opcodes) is not counted:
  it cannot run without the original files. `RideOpcodeTestMapTests` runs every test
  the map names under a `RideOpcodeRecorder` and fails unless it executes its opcode
  (or, for a `ThroughRuntimeEffects` entry, performs it through the game effects
  without an unimplemented record), and fails when an opcode the script tests execute
  has no entry. Set `OPENTPW_OPCODE_TEST_MAP_OUT=<file>` to regenerate the table.

### Divergences from the player's build flow

- The gate composes the park itself (no `Level`, which needs a GPU): the same
  composition the M3 gate uses, plus `Level.SellRemovedObject` for removal (now shared
  with `Level`'s `ObjectRemoved` handler). `ParkObjects.Remove` and
  `OriginalObject.Delete` are repeated: free the footprint, `Runtime.Stop()`.
- The site and the 14-cell path are the gate's choices; the HUD click route, the
  build preview and the queue tool are not exercised.
- Research and golden tickets come from the late-game save, not from play.

## Baseline (four themes, seed 6075451861746676596, W = 5 minutes)

Command: `--m4-gate --report m4-gate.json` on the local Theme Park World data
(`/Users/sander/server/game-assets/theme-park-world`), Release build, SDK 10. Exit code
**1**. Two complete runs gave identical reports apart from `wallSeconds` (about 155 s
each, determinism probe included). **14 pass, 12 fail, 1 unresolved.**

| Row | Verdict | Evidence |
| --- | --- | --- |
| `attractions.inventory` | PASS | 274 entries, 207 supported, 67 unsupported: per theme 6 fixed items, 3 tools, 2–3 add-ons, 5 track rides |
| `attractions.jungle.load` / `.build` / `.delete` | PASS | 53 objects each |
| `attractions.jungle.run` | FAIL | 49 pass, 4 fail: The Hot Pot (bumper cars; boarded, never released), Jurassic Tours (queued, never admitted), Jungle Spray and Laughing Hyenas (sideshows; boarded, never released) |
| `attractions.jungle.stop` | FAIL | 50 pass, 3 fail: The Hot Pot (230 riders not released), Jungle Spray, Laughing Hyenas (3 riders each) |
| `attractions.hallow.load` / `.build` / `.delete` | PASS | 53 objects each |
| `attractions.hallow.run` | FAIL | 52 pass, 1 fail: Flightmare Tours (queued, never admitted) |
| `attractions.hallow.stop` | FAIL | 52 pass, 1 fail: Pumpkin Castle (bumper cars; 3 riders not released after closing) |
| `attractions.space.load` / `.build` / `.delete` | PASS | 51 objects each |
| `attractions.space.run` | FAIL | 49 pass, 2 fail: Star Tours (queued, never admitted), Martian Mooners (sideshow; boarded, never released) |
| `attractions.space.stop` | FAIL | 49 pass, 2 fail: Uforia (bumper cars), Martian Mooners (3 riders each) |
| `attractions.fantasy.load` / `.build` / `.delete` | PASS | 50 objects each |
| `attractions.fantasy.run` | FAIL | 46 pass, 4 fail: Bumper Bugs (boarded, never released), Tweety Tours (queued, never admitted), Aqua Spray and Fruit Shy (sideshows; boarded, never released) |
| `attractions.fantasy.stop` | FAIL | 47 pass, 3 fail: Bumper Bugs (220 riders), Aqua Spray, Fruit Shy (3 riders each) |
| `vm.opcodes` | FAIL | 308 scripts, 84 opcodes, 11,915 instructions; 84 handled, **0 unknown**; 47 with a unit test, **37 without** (KILLOBJ, FADEOBJ, SETOBJPARAM, LOOPANIM, TRIGWAITANIM, TRIGANIMSPEED, TRIGANIM_CH, LOOPANIM_CH, GETANIM_CH, HUSH, HOP, TURBO, ADDHEAD, DELHEAD, FORCEUNLIMBO, INLIMBO, the BOUNCE family, WALKON, the WALK*FLOAT* opcodes, the scream opcodes, REPAIREFFECT, HOUR, MIN, SEC, SETREVERB, DIPMUSIC, SPARK) |
| `vm.effect-hooks` | UNRESOLVED | 51 hooked opcodes, 5 resolved (LIMBO, UNLIMBO, LIMBOSPACE, WALKOFF, WALKGET), 46 unresolved; no hook fault |
| `coasters.construction` | FAIL | 20 track rides (12 coasters, 4 karts, 4 water rides): every station is built, charged, runs its script and is removed with its scrap credit; 0 tracks, 0 cars; COAST/BUMP calls are recorded unimplemented |
| `rides.camera` | FAIL | camera modes: CameraMode, LobbyCameraMode, ParkCameraMode; none takes a ride |
| `rides.rating` | FAIL | 52 fixed rides carry their data excitement into the guest bridge; the 20 track rides have no assessment |
| `determinism.same-seed` | PASS | 1,450 hashes per pass, identical; final `DC661D5F9137C8BC` |

109 of the 207 supported objects are guest attractions; every one of them was chosen,
queued for and boarded within W, so no run row is unresolved. Of the 109 stop steps,
96 scripts read `VAR_RIDECLOSED` after closing, 12 never name it and 1 (space Space
Balls) names it only before its running loop.

### Which area each failure points at

- **Ride-type controllers (VM effects).** The four tour rides (TOUR) never admit a
  guest, and the four bumper-car rides (BUMP) take guests on board and never let them
  off: Hot Pot and Bumper Bugs keep boarding until 230 and 220 guests are on board. The
  sideshows Jungle Spray, Laughing Hyenas, Martian Mooners, Aqua Spray and Fruit Shy
  keep their 3 players and never release one (Jungle Spray's `Junspray.RSE` walks its
  players on with WALKON; why no release follows was not traced here). `vm.effect-hooks` lists the same gaps from the VM side (TOUR,
  BUMP, COAST recorded unimplemented hundreds of thousands of times).
- **Coasters and tracks.** `coasters.construction` and `rides.rating`: no track builder,
  so no coaster, kart or water ride runs and none can be assessed.
- **Tests.** `vm.opcodes`: 37 corpus opcodes are exercised only by the asset-gated corpus
  run, not by a unit test; 46 hooks have no test through the game effects.
- **Ride camera.** Absent.
- **Animation**: no step fails on animation; clips without a member are listed per object
  (`unresolvedAnimations`), and vertex animation is outside the gate.
- **Guests**: guest choice, queueing and paying work for every other attraction; the
  stuck riders above are the controller gaps, not the guest simulation.
- **Saves**: not measured (the late-game save is only the gate's starting point).

## Mutation checks

`M4GateAssetTests` injects one fault per row through `M4GateHooks` (internal; null in
real runs) into a short jungle run (W = 2 minutes) of an object that passes the step
unmutated, and requires the row to FAIL:

| Mutation | Row | Fault |
| --- | --- | --- |
| `MutationChargingTwiceFailsTheBuildRow` | `attractions.jungle.build` | the purchase is made twice (Drinks Shop) |
| `MutationStoppingTheScriptFailsTheRunRow` | `attractions.jungle.run` | `Runtime.Stop()` at tick 100 (Drinks Shop): script halted |
| `MutationReopeningAfterCloseFailsTheStopRow` | `attractions.jungle.stop` | `Runtime.Open()` every tick after closing (Belly Bounce) |
| `MutationRemovingWithoutScrapCreditFailsTheDeleteRow` | `attractions.jungle.delete` | removal with `ParkEconomy.Remove` instead of the sale |
| `MutationDivergingTheSecondRunFailsDeterminism` | `determinism.same-seed` | the second pass changes one script variable before each hash |
| `MutationDroppingAnOpcodeTestFailsTheOpcodeRow` | `vm.opcodes` | the map without its COPY entry: one more untested opcode, named |

`M4GateTests` flips the pure rules the same way: an unknown opcode (200) is listed
with its number, name and script and fails `vm.opcodes`; `vm.effect-hooks` goes from
PASS to UNRESOLVED when the test or the runtime effect is missing and to FAIL on a hook
fault; `coasters.construction`, `rides.camera`, `rides.rating` and
`determinism.same-seed` pass, fail or stay unresolved on synthetic facts. The first
determinism mutation passed: quick objects end before their first simulated minute,
and the post-delete hash no longer contains the object's scripts. The hash at the end
of each lifecycle step was added for that reason.

## Not measured

- Original semantics: no original trace exists. Every PASS is a statement about OpenTPW.
- Queues (built by the queue tool) and path building: the M3 gate measures them. Here
  every guest attraction uses the one-cell queue at its path cell.
- Original ride cycles, ride durations, capacities and their units, ride
  breakdown and repair, upgrades, prices, and what guests feel after a ride.
- Visual animation fidelity (vertex animation, channel mixing; OBJECTS.md): the gate
  sees only that clips were requested and resolved (`animationsPlayed`,
  `unresolvedAnimations` in the run evidence).
- Sounds, particles, EVENT mappings and objects spawned by scripts (ADDOBJ and the
  rest of the unimplemented hooks): listed by `vm.effect-hooks`, not judged.
- Saves: no object is saved and reloaded during its lifecycle.
- Bonus objects (`--bonus-data`), Instant Action balance, other seeds.
- Rendering, input, HUD, the build preview, the remove-tool click and frame pacing.
