# Review DET-V: determinism research and world seed (471e4bb, d1813eb)

October 10, 2026. Independent review of the stack on fork main `efc090d`:
`471e4bb` (research: docs/reverse/DET-plan.md, tools/ppc-analysis/lanes/det/) and
`d1813eb` ("Make park runs reproducible from one explicit world seed"). The M3
gate commit `f6ad99f` is not on main. It was merged into scratch clones to check
the integration. No original instruction was executed. Evidence is static
decoding of the identified Feral Mac PEF (`SimThemePark.data`, SHA-256
`04809cd4…5e295f5`) and OpenTPW builds and tests in scratch clones under /tmp.
Claims are bounded to this Mac build, to .NET 10 on one arm64 Mac, and to the
code at `d1813eb`.

## Merge blockers

**None for `d1813eb` on `efc090d`.** `git merge-tree --write-tree efc090d d1813eb`
is clean. Build, tests, register, evidence runner, CRLF and whitespace all pass
(see Numbers).

**Integration with `f6ad99f` (whichever lands second):**

- **B1. Conflicts in the register tool and the generated register; one
  sentence auto-merges wrongly.** `git merge-tree --write-tree f6ad99f d1813eb`
  conflicts in `tools/fidelity_register.py` (the `REGISTERS` dict: both sides
  add one line after `"AUDIO"`) and in `docs/FIDELITY-REGISTER.md` (generated).
  Both commits change "six configured existing C# registers" to the same
  "seven configured C# registers". Git therefore merges that sentence without a
  conflict, and it stays wrong at eight registers. `--check` does not catch it,
  because the document is regenerated from the same wrong string. Exact fix:
  1. In `REGISTERS`, keep both lines in this order:
     `"DET": "source/OpenTPW/World/DeterminismApproximations.cs",` then
     `"GATE": "source/OpenTPW/Client/M3GateApproximations.cs",`.
  2. In `render()`, change `from the seven configured C# registers` to
     `from the eight configured C# registers`. This confirms the author's
     Directive.
  3. Run `python3 tools/fidelity_register.py --write`, then `--check`. Result
     in the scratch merge: **148 unresolved unique APPROX IDs**
     (efc090d 140 + DET 6 + GATE 2).
- **B2. docs/M3-GATE.md becomes stale.** After the merge, `determinism.same-seed`
  passes (see §3). M3-GATE.md still describes the failure: lines 104–106 cite
  "a process-wide counter in `OriginalObjectRuntime`", the baseline table row
  is FAIL with two different raw guest hashes, line 135 gives "Totals: 13 pass,
  2 fail, 1 unresolved", and the follow-up at line 143 says
  "`OriginalObjectRuntime.nextAttractionId` is static". Exact fix, in the same
  merge: set the row to PASS (run-2 raw guest hash 634BC7756C8C6326), the totals
  to 14 / 1 / 1, and rewrite lines 104–106 and the line-143 follow-up to say
  that attraction ids are now per park (`RideScriptWorld`, d1813eb). Also drop
  "(docs/M3-GATE.md, not on main yet)" from DETERMINISM.md "For the M3 gate".

## Verdicts

| Check | Verdict |
| --- | --- |
| 1a. Four DET-plan claims decoded independently | **Confirmed** (LCG + abs, RAND call site, 31 ms / `&7` / cap 3, reseed 0xe79b8, two scans) |
| 1b. "only"/scan claims labelled bounded | **Confirmed, one exception** (N1: "the one simulation-relevant path") |
| 1c. The 8 lane mutation tests bite | **Confirmed**: each fails at its own site and reports the binary's true value; a 253-point sweep finds one dead table field (N3) |
| 2. Remaining process-wide or unseeded simulation state | **No simulation leak found** in the folders reviewed. One hash-scope defect (F1). The guard misses several forms (F3) |
| 2b. Guard scans the right folders; catches a reintroduced `new Random()` | **Partly**: catches `new Random()` and `static int next…`; misses target-typed `new()`, `Level.Objects.cs` and others (F3) |
| 3. Default seed reproduces previous outcomes | **Confirmed** for every previously seeded stream (gate hashes equal before/after); vacuous for formerly unseeded scripts |
| 4. Save/load | **Round-trip and old saves confirmed; the mid-run test is weaker than stated** (F2: a no-op guest or script restore survives) |
| 5. Hash fields, stability, ordering | **Confirmed for the headless sources**; `Level.ComputeStateHash` includes a wall-clock-driven sound seed (F1); some missing fields are not listed (N5) |
| 6. Merge readiness | **Clean on efc090d**; with f6ad99f see B1/B2 |

## 1. Research (471e4bb)

### 1a. Independent decode

`tools/ppc-analysis/lanes/review/test_det_v1.py` decodes the instruction words
with its own field split. It does not import `det_evidence.py`; it uses only
`pef.py` to load the container. Run with `OPENTPW_PPC_BIN_ROOT`: 11/11 pass.
Without it: 4 run, 7 skip.

- **World LCG 0x105328.** `lis r3,25; addi r0,r3,0x660d` gives 1664525.
  `addis r3,r3,0x3c6f; addi r3,r3,-0xca1` gives 1013904223. The field is
  `addis r5,r3,30` + `-0x58f8`, which is world+0x1da708. A nine-opcode
  interpreter in the test executes the generator for six states. Every successor is stored,
  and the return value is the wrapping absolute value (`cmpwi; bclr 4,0; neg`).
  A state whose successor is 0x80000000 returns 0x80000000 unchanged. The setter
  0x105360 stores r4 to the same field.
- **Abs at a call site (RSE RAND 0xb0800).** `bl 0x105328; srwi r3,r3,1; bl labs`,
  then `addi r4,r28,1` with `r28 = extsh(bound)`, then `divw`/`mullw`/`subf`
  (signed C remainder), then `bl labs` again. That is
  `labs(labs(r >>> 1) % (int16(bound) + 1))`, as DET-plan §4.3 says. The plan's
  vector (seed 0, bound 10 → 6, 8) was recomputed from first principles.
- **31 ms substep, every-eighth mask, cap 3.** At 0x1c22dc–0x1c22f0:
  `previous += 31`, `substep += 1`, both before the world gate. At 0x1c233c:
  `rlwinm. r0,r0,0,29,31` (substep & 7), and a non-zero result branches to
  0x1c2428, past the turn. At 0x1c2354: `cmplwi r3,3` (unsigned) on the
  park-work count, and not-less branches to 0x1c2424 (`bl 0xb5da4`, which runs
  even when the cap is spent). Otherwise the count is incremented and stored
  before the mode dispatch. The backlog test is `cmpwi 2000` /
  `previous = now − 2000`, and the loop repeats on a signed `cmpw; bgt`.
- **Reseed to thing ID (0xe79b8, one of the 7).** At 0xe7964/0xe7968,
  `r3 = sp+200` and `r4 = r29` (the new thing). No instruction up to 0xe79a4
  writes r3 or r4. `0xfa9a4` is `lhz r0,0(r4); sth r0,0(r3); blr`. The site then
  does `lhz r0,200(sp); sth r0,172(sp); lhz r4,172(sp)`, loads the world into
  r3 and calls `bl 0x105360`. The setter's argument is therefore the
  zero-extended u16 at thing+0.
- **Scans recounted.** 142 direct `bl` to 0x105328. Exactly 8 `bl` to 0x105360:
  0xd5b14, 0xd86dc, 0xe79b8, 0xe8fd4, 0xeb160, 0xeb28c, 0xefa30, 0x1a9574.

### 1b. Bounded language

The plan defines **scan** as one instruction form ("only among sites of that
form … never a claim about the only runtime route"). The "only"/"exactly" claims
in §2.2/§2.3 sit under that label: address takers, one direct coaster call, one
seed string, no ANSI multiplier, and no direct RNG call in the loop body.

- **N1 (wording).** §2.2 says coaster motion is "the one simulation-relevant path
  that is not replayable from substep inputs alone". The commit body says "Coaster
  motion is the one frame-coupled simulation path". Neither is bounded. The same
  post-loop block holds 0xb2aac ("per-frame walk over all scripts; role
  unqualified") and 0x483ac ("role unqualified"). The advisor update also draws
  from the C-library `rand`, which the coaster seed shares. Suggested wording:
  "the one frame-coupled path among the qualified post-loop calls; 0xb2aac and
  0x483ac are unqualified".
- **N2 (label).** The RNG-instances table marks the kart, particle and weather
  generators "pinned". `det_evidence.py` pins their multiplier pieces and state
  fields, but not their increments. The particle multiplier is pinned only
  through the 214013 site count. This review decoded the increments: kart and
  particle 2531011 (`addis 0x27; addi -0x613d`), weather 1013904223. They are
  pinned in `test_det_v1.py`. The facts are right; only the label overstates
  the lane's own witness.

### 1c. Mutation tests

All 8 lane mutation tests fail with a `PEFError` at the mutated site, and the
message shows the binary's true value. Examples:
`code:0x1c22e0: unexpected interpreted value (0, 3, 31)`,
`world RNG increment: unexpected interpreted value 1013904223`,
`bounded scan world_rng_direct_calls: unexpected interpreted value 142`.
`test_det_v1.LaneMutationsBite` asserts those messages, so a mutation that trips
an unrelated check would fail this review test. A wider sweep bumped every
scalar in the 19 expectation tables, 253 mutations in all. 232 raise. 20 only
change the output `OWN_STATE_GENERATORS` table: it is descriptive, and its facts
are pinned by literal checks elsewhere. **N3:** one field,
`CLIB_RAND['code'] = 0x1e78c`, is never read.

## 2. Implementation (d1813eb): remaining nondeterminism

Searched `source/OpenTPW` (VM, Economy, World and its subfolders, Audio,
Client/GameFlow and, after the merge, Client/M3Gate), `OpenTPW.Common` and
`OpenTPW.Files`. The search covered `Random`, `Random.Shared`,
`Environment.TickCount`, `DateTime.Now`, `Stopwatch`, `Guid`, `GetHashCode`,
`HashCode`, `Parallel`/`Task.Run`/threads, `Interlocked`, mutable statics, static
caches, `Dictionary`/`HashSet` iteration and libm calls.

Not simulation leaks:
- The remaining `new Random()` (`Advisor` mouth), `Stopwatch` (renderer, mixer,
  smoke tests, setup), `DateTime.Now` (logger, intro playlist), `Guid` (temp
  file names) and `Task.Run` (audio decode, online) are presentation or I/O.
- `GetHashCode`/`HashCode.Combine` appear only in value types and render code,
  never in simulation decisions.
- Mutable statics in simulation folders are log-once flags, the camera, `Level.Current`
  (set only by the constructor and GameFlow) and read-only asset caches
  (`ObjectCatalog.cache`, `ObjectAssets.models`/`listings`). `ObjectCatalogEntry`
  setters are `internal set`, used during load.
- Dictionary and HashSet iteration: `RideScriptWorld.FindRandom` sorts by
  ScriptId. `ParkSaveFile.Capture` orders its sets and lists, and the hash sorts
  JSON keys. `RideVisitorBridge.hopped` is sorted for the hash. The other
  `Dictionary<int,…>` and `HashSet` iterations depend on insertion and removal
  order only, not on hash codes, in the current CoreCLR implementation. That
  includes reference-keyed `Level.economyInstances`. This is a property of the
  implementation, not a documented contract.
- Floats: the guest simulation uses `float` arithmetic and `MathF.Sqrt`, both
  IEEE-exact. The only libm call on a simulation path is `Math.Pow` in
  `ParkLedger.MonthlyRepayment` (floored; APR > 0 only). RyuJIT does not
  contract to FMA implicitly. Not tested on x64; the local x64 SDK has only net8.

Findings:

- **F1 (medium, hash scope). `Level.ComputeStateHash` and `Level.SavePark`
  include a sound seed that advances with wall-clock time.** `SoundEventSystem.Draw`
  runs on UI clicks (`ParkHud` → `PlayUi`). It also runs whenever a music or
  speech sentence segment starts, from `voice.Completed` inside
  `AudioMixer.Pump` (device-queue and Stopwatch driven) and after a `Task.Run`
  decode completes. `Level.ComputeStateHash` hashes `GameAudio.Events?.Seed`
  and whether it is present. With audio running, two same-seed, same-input runs
  therefore get different "world" hashes. A muted or device-less run also gets
  a different hash from an audio run. The headless gate and `DeterminismTests`
  are unaffected because both have no sound service. Nothing calls
  `Level.ComputeStateHash` yet. Fix: leave the sound seed out of
  `WorldStateHash` and keep it in the save. DETERMINISM.md already calls it
  presentation. DET-plan §4.5 can list `sound_seed` as replay state only
  because the original's draws never advance it, and OpenTPW's chooser does
  advance it (DET-012). If the seed stays in the hash, document the hash as
  reproducible only when headless.
- **F3 (low, guard coverage).** `SimulationCodeUsesNoProcessWideRandomnessOrClock`
  was mutation-checked in a scratch clone: the source was edited and the test
  rebuilt and run. **Caught:** `new System.Random()` in `World/Guests`, and
  `private static int nextAttractionId` in `World/Objects`. **Missed:** the
  target-typed `System.Random leak = new();`, `new System.Random()` in
  `World/Level.Objects.cs` (not scanned), `private static long attractionCounter`,
  and `"x".GetHashCode()` (process-randomized for strings). `World/*.cs` other
  than `Level.cs` (`Level.Objects.cs`, `PrototypeRide.cs`, `Ride.cs`) and
  `Client/M3Gate.cs` are outside the scanned set. Suggested: scan
  `World/Level*.cs` and `Client/M3Gate*.cs`. Add `Random\s+\w+\s*=\s*new\s*\(\s*\)`,
  `static\s+(?!readonly)\w+\s+\w+\s*[;=]` (allow-listed), `\.GetHashCode\(\)`,
  `Guid\.NewGuid` and `Parallel\.`. The gaps are pinned in `test_det_v1.CSharpSourceGuard`.
  A fix will make that test fail, which is intended; update it at that point.
- **N4 (API hazard).** `OriginalObjectRuntime(entry, world: null)` now creates
  a private `RideScriptWorld`, so every such runtime gets attraction id 1. All
  in-tree callers pass a world (ParkObjects, PrototypeRide via Level, M3Gate,
  DeterminismAssetTests). A future caller that omits it would make guests key
  two attractions as one. Consider making `world` required, or documenting it.
- **N6.** `RideVM.FindScript` counts a draw even when no script matches, and then
  no `Random` call happens. The hash's `seed + randomDraws` stand-in is still a
  deterministic function of history, but the comment "number of RNG calls" is
  inexact.

Checked and correct: the `nextAttractionId` static is gone. Ids now start at 1
per `RideScriptWorld`, and the prototype ride in visit mode joins the park
world after the objects, so its id (N+1) is unchanged. Unseeded VMs draw a
SplitMix64 seed from their world. Child seeds still come from the parent `Random`.
`GameAudio.EnterPark` reseeds per park. The M3 gate after the merge
constructs `new RideScriptWorld()` per run, with seed 0, which equals the
default `ScriptStream`.

## 3. Default seed reproduces previous outcomes

By construction, the default seed gives guests `0x5450574775657374`, economy
`1`, object script key `0` (seeds 1, 2, 3, …) and script stream `0`. These are
the previous fixed values (`DefaultWorldSeedKeepsThePreviousStreamSeeds`). No
existing test pins a numeric outcome of those seeds, so the evidence is the M3
gate. Results are 30 simulated minutes, jungle, default seed, `f6ad99f` merged
into each base, two separate processes each:

| Scratch merge | Process | Run-1 guest hash | Run-2 guest hash (same process) | Gate hash (both runs) | Totals |
| --- | --- | --- | --- | --- | --- |
| efc090d + f6ad99f (before) | 1 | 634BC7756C8C6326 | 94E148DBB92A1650 | B046FADB47BE93F3 | 13 pass, 2 fail, 1 unresolved |
| efc090d + f6ad99f (before) | 2 | 634BC7756C8C6326 | 94E148DBB92A1650 | B046FADB47BE93F3 | 13 / 2 / 1 |
| d1813eb + f6ad99f (after) | 1 | 634BC7756C8C6326 | 634BC7756C8C6326 | B046FADB47BE93F3 | 14 pass, 1 fail, 1 unresolved |
| d1813eb + f6ad99f (after) | 2 | 634BC7756C8C6326 | 634BC7756C8C6326 | B046FADB47BE93F3 | 14 / 1 / 1 |

The guest hash and gate hash are identical before and after `d1813eb`, and they
equal the documented baseline. A comparison of the JSON reports shows that 15
of the 16 rows have the same verdict, evidence and first-violation tick before
and after. Each side's two processes produce identical reports. The only row
that changes is `determinism.same-seed`, from FAIL to PASS. Scripts that had no seed used
`new Random()` before, so "reproduces" has no prior outcome for them
(PrototypeRide in sandbox/visit mode, `Ride`).

## 4. Save and load

- **Round trip and older saves: confirmed.** `ParkSaveRoundTripsTheWorldStreams`
  covers extreme values (`ulong.MaxValue`, `0x8000…01`, `uint.MaxValue`), a save
  without the section and a section with an unknown member (rejected).
  Mutation: writing `GuestRandom` into `ScriptRandom` fails this test and the
  mid-run test.
- **F2 (medium, test strength). The mid-run test does not prove the guest or
  script stream is restored.** It "scrambles" those streams with the same
  `RestoreRandomState` methods that the load uses. Mutation results in a
  scratch clone:
  - `GuestSimulation.RestoreRandomState` as a no-op: all 9 `DeterminismTests` pass.
  - `RideScriptWorld.RestoreRandomState` as a no-op: all 9 pass.

  Only the economy part bites, because the load replaces the economy object.
  The sound seed is not exercised: `SyntheticPark` has none. DETERMINISM.md's
  "scramble every saved part, load ⇒ the hash at the save point is restored"
  is therefore stronger than the test. The test also calls its own `Load`, not
  `Level.LoadPark`/`RestoreRandomState`/`CaptureRandomState`, which no test
  reaches. Fix: scramble by drawing instead of restoring. For example, run
  more guest ticks and call `Scripts.NextScriptSeed()` after the save. Then
  assert that `Guests.RandomState` and `Scripts.RandomState` each differ from
  the saved values before the load and equal them after. Also add a
  sound-seed round trip through `WorldRandomState`.
- Forward compatibility (note): the save version stays 2. Older OpenTPW builds
  reject unknown members, so they cannot read a save that has `World`. This is
  acceptable for an unreleased format, but a version bump would make the
  refusal explicit.

## 5. Hash

- **Coverage.** The fields follow DET-plan §4.5 with the listed substitutes:
  guests (every field the tick reads, including navigation and lane), the
  attractions' queues, riders, limbo and bounce lists, every live VM (PC,
  variables, call stack, deadlines, counters, seed and draws), and the
  economy's canonical JSON. The "missing" fields in DETERMINISM.md are correct
  for what OpenTPW lacks.
- **N5 (doc).** These parts are neither hashed nor listed: the object table
  outside the VMs (placements, `OriginalObjectRuntime.stopped`/`wasRunning`,
  animator channels; the attraction `IsOpen` enters through the guest digest),
  `FixedStepClock.PendingSeconds` (frame timing, DET-I2) and
  `GuestEconomyBridge`'s attraction→instance map. List them as missing or add
  them.
- **Stability.** FNV-1a over explicit little-endian bytes. Strings are hashed
  per UTF-16 code unit, floats by bit pattern. Scripts are hashed in ScriptId
  order and guests in id order. JSON object keys are sorted with
  `StringComparer.Ordinal`, and doubles use .NET Core's shortest round-trip
  formatting. Nothing uses `GetHashCode` or culture-dependent formatting. The
  pinned `0x86B5046D497B900C` was reproduced in this review's own test
  processes. Cross-architecture equality is not tested.

## 6. Notes for QUEUE-I (merging after d1813eb)

QUEUE-I adds queue cells and positions and draws a sideways offset from the
guests' RNG. When it lands after `d1813eb`:

1. Draw only from `GuestSimulation`'s `random`. The draws are then saved
   (`WorldRandomState.GuestRandom`) and hashed automatically. If QUEUE-I needs
   a stream of its own, derive it from `WorldSeed` (new key), add it to
   `WorldRandomState`, `ParkSaveFile.WorldData` and `WorldStateHash`, and bump
   `SchemaVersion` (this is the commit's Directive).
2. Add every new per-guest or per-queue field to
   `GuestSimulation.AddCanonicalState` / `RideVisitorBridge.AddCanonicalState`
   by hand. The lists are explicit, so a missing field is silently left out of
   the hash. Bump `WorldStateHash.SchemaVersion` when the field list changes.
3. Expect `HashOfAFixedRunIsTheSameInEveryProcess` (`0x86B5046D497B900C`) to
   change, because its synthetic park queues guests at a shop. Re-pin it
   deliberately and record why. The M3 gate's guest and gate hashes will change
   too; update M3-GATE.md's baseline.
4. Iterate queue cells in a defined order: list order, or sorted. Do not rely
   on `HashSet` order of value tuples.
5. Queue positions are not saved (DET-016). If QUEUE-I adds state that must
   survive a load, extend DET-016's wording.
6. New files under `World/Guests` are covered by the source guard; elsewhere they are not (F3).

## Numbers

SDK 10 (`/Users/sander/.local/share/opentpw-dotnet10/dotnet`), Release, scratch
clones, `OPENTPW_GAME_PATH` = the Theme Park World install for "with assets".

| Tree | Build | OpenTPW.Tests without assets | With assets |
| --- | --- | --- | --- |
| efc090d (before) | 0 errors | 864 / 240 / 0 | 1033 / 71 / 0 |
| d1813eb | 0 errors | **873 / 241 / 0** (author: same) | **1043 / 71 / 0** (author: same) |
| d1813eb + f6ad99f (B1 resolved as above) | 0 errors | 875 / 242 / 0 | 1046 / 71 / 0 |

Pass / skip / fail. Other checks on `d1813eb` unless noted:

| Check | Result |
| --- | --- |
| `git merge-tree --write-tree efc090d d1813eb` | clean (tree 4503195) |
| `git merge-tree --write-tree f6ad99f d1813eb` | conflicts in `tools/fidelity_register.py`, `docs/FIDELITY-REGISTER.md` (B1) |
| `fidelity_register.py --check` | 146 IDs (d1813eb); 148 after B1 on the f6ad99f merge; 142 on efc090d + f6ad99f |
| `test_fidelity_register.py` | 8 / 8 |
| `run_evidence_checks.py --mac-bin … --dotnet <net8 SDK>` | OK: 10 Python suites, 564 tests (49 skipped) on d1813eb; 575 with this review's test (review lane 220 ran, 19 skipped) |
| `lanes/det` with `OPENTPW_PPC_BIN_ROOT` | 33 / 33 |
| `test_det_v1.py` | 11 / 11 with the PEFs; 4 pass, 7 skip without |
| `--m3-gate --minutes 30` | see §3 (two processes per side; 14 / 1 / 1 after) |
| Pinned `0x86B5046D497B900C` | reproduced in every test process of this review (arm64) |
| `git diff efc090d d1813eb --stat` vs `--ignore-cr-at-eol --stat` | identical (28 files, +2159 / −55) |
| `git diff --check efc090d d1813eb` | clean |
| C# mutation runs (scratch clone, `DeterminismTests` 9 tests) | baseline 9/9; guard catches 2 of 6 forms (F3); restore no-ops for guests and scripts survive (F2); save-field swap, hash field drop and a static attraction counter are caught |

Not tested: Windows/Linux hosts; x64 (the local x64 SDK has only net8); the
interactive game with an audio device (F1 is from reading the code);
`Level.SavePark`/`LoadPark` end to end (they need a GPU-backed `Level`).

Nothing from the game data or binaries is committed. Scratch work used
`git clone --shared` copies under /tmp. Four temporary `git worktree add`
entries were created at the start by mistake; they were removed and pruned
before any build ran. `git merge-tree --write-tree` wrote unreferenced objects
to the shared object store. No refs were created.

# Round 2: fixes d620fb7 and the merge onto main 8a88379

October 10, 2026. Review of the rebased stack `b27babc` → `89f2db9` → `e4b256c`
(this review's round 1) → `d620fb7` (fixes) on main `12992e8`, and of its merge
onto the current main `8a88379` (the M3 gate evaluator). Scratch clones under
/private/tmp (`git clone --shared`; no worktrees). SDK 10 at
`/Users/sander/.local/share/opentpw-dotnet10/dotnet`, Release, built and run from
the non-symlinked `/private/tmp` path. Claims are bounded to .NET 10 on one arm64
Mac and to the code named. Nothing from the original was executed.

## Round 2 merge blockers

**None.** `d620fb7` merges onto `8a88379` with the two expected conflicts
(round 1's B1) and needs the doc updates of round 1's B2. With the resolution
below, the build, both test runs, the register, the evidence runner, the gate,
CRLF and whitespace all pass. One correction to the author's Directive: the
run-2 raw guest hash on `8a88379` is **00E874A92846742F**, not 634BC7756C8C6326
(that value belonged to the earlier gate base `ea09cda`), and the totals are
**13 / 2 / 1**, not 14 / 1 / 1, because main's gate already had 12 / 3 / 1.

### Exact resolution for the merge onto 8a88379

1. `git merge --no-ff d620fb7` (or the review head on top of it) on `8a88379`.
   Conflicts: `tools/fidelity_register.py`, `docs/FIDELITY-REGISTER.md`. Every
   other file auto-merges (`Level.Objects.cs` and `ParkObjects.cs` included).
2. `tools/fidelity_register.py` `REGISTERS`: keep both lines, DET then GATE:
   `"DET": "source/OpenTPW/World/DeterminismApproximations.cs",`
   `"GATE": "source/OpenTPW/Client/M3GateApproximations.cs",`.
   In `render()`, change `from the seven configured C# registers` to `from the
   eight configured C# registers` (git keeps "seven" from both sides).
3. `docs/FIDELITY-REGISTER.md`: take either side, then
   `python3 tools/fidelity_register.py --write` and `--check`:
   **157 unresolved unique APPROX IDs** (173 APPROX, 69 EXT, 70 DATA, 81 BIN;
   DET 6, GATE 2).
4. `docs/M3-GATE.md`:
   - "Hashes" paragraph: the attraction ids are allocated per park by the park's
     `RideScriptWorld`, so a second scenario gets the same ids (4 5 6 in both
     runs), instead of "a process-wide counter in `OriginalObjectRuntime` …
     (run 1: 4 5 6; run 2: 10 11 12)".
   - Baseline row: `| determinism.same-seed | PASS | raw guest hash matches
     in-process (00E874A92846742F both runs; attraction ids 4 5 6 in both,
     allocated per park); gate hash matches (C4D8D348D84FF042); no divergent
     minute |`.
   - Totals: `Totals: 13 pass, 2 fail, 1 unresolved; **exit code 1**.` The
     `--no-determinism` sentence stays (12 / 2 / 2, exit 1, re-run).
   - "Which gameplay area the failures point at": rewrite the determinism
     bullet as resolved (it failed while `nextAttractionId` was static; run 2
     had ids 10 11 12 and raw hash F9AB11FDA4D90B0F; ids are now per park).
5. `docs/DETERMINISM.md` "For the M3 gate": drop ", not on main yet" and say
   the row passes.
6. `source/OpenTPW/Client/M3Gate.cs` `ComputeGateHash` summary: "(the bridge
   ids come from a process-wide counter)" is stale; say the ids are allocated
   per park and the placement index only keeps the gate hash independent of
   their allocation. Comment only.

`test_det_v2.MergedGateDocs` checks steps 2, 4 (row and totals), 5 and 6 when
docs/M3-GATE.md is present.

## Round 2 verdicts

| Check | Verdict |
| --- | --- |
| F1 sound seed out of the hash | **Fixed.** `WorldStateSources` has no sound field, `SchemaVersion` is 2, `ParkWorldStreams.ComputeStateHash` passes no sound. Mutation "sound seed XORed back into the hash" fails `SoundDrawsDoNotChangeTheCanonicalHash` |
| F2 save/load tests bite | **Fixed.** Each mutation below fails at least one test; a LoadPark that skips the stream restore fails only the asset-gated test, as the author states |
| F3 guard | **Fixed for every round-1 form** (target-typed `new()`, `Level.Objects.cs`, static counters of any width, `GetHashCode`/`HashCode`). Inserted into real scanned files, each fails the guard. New known gaps, none present in the code today: N1 |
| Presentation exclusion list | **Hides no simulation code.** In the 11 excluded files the guard finds only `Advisor.cs` `new AdvisorMouth( new Random() )` (mouth mesh only; BIN 0x10007434 uses `rand()` there) and four camera tuning properties in `LobbyCameraMode.cs`/`ParkCameraMode.cs`. None of them names `RideScriptWorld`, `WorldSeed`, `GuestSimulation` or `ParkWorldStreams` |
| Rebase resolutions (`89f2db9` vs `d1813eb`, `git range-diff`) | **Both sides kept.** `Level(…, ParkStartKind start, WorldSeed? seed)` passes `start` and `Seed` to `ParkEconomyRuntime.ForOriginalLevel( OriginalPark, start, Seed )`; `ForOriginalLevel` keeps main's `ParkStart.Resolve`/`ImportShippedSave`/`SeedResearcherStandIn` flow and adds only the economy seed; `Load` keeps main's mode check and `ForwardEvent` and returns the world streams; `GameFlow` keeps `start:` and `StartKind = level.Park?.Start.Kind` and adds the sound seed to `EnterPark`. `b27babc` and `e4b256c` are identical to `471e4bb` and `68e80d2` |
| Pinned hash change in `89f2db9` | **Justified, reproduced.** `89f2db9` with the `SeedResearcherStandIn` key left out of the economy digest's canonical JSON gives exactly `0x86B5046D497B900C`. `d620fb7` with schema 1 and the two empty sound fields restored gives exactly `0xB55E94284EBFE91B`. So the chain old → `89f2db9` → `d620fb7` has no other cause |
| `ParkWorldStreams` extraction | **Behaviour-preserving.** Capture, restore, save and load bodies are the old `Level` bodies with `Objects.ScriptWorld` → `Scripts` and `GameAudio.Events` → `Sound`; `Level.Streams` reads both at call time as before. The only intended change is the hash (F1). `Level.SavePark`/`LoadPark` still have no caller outside tests |
| Merge onto `8a88379` | **Merge-ready** with the resolution above |
| `--m3-gate` on the merge | **13 pass / 2 fail / 1 unresolved, exit 1, twice**; reports identical apart from `wallSeconds`. Every row except `determinism.same-seed` has the same verdict and evidence as main's run on `8a88379` (12 / 3 / 1, gate hash C4D8D348D84FF042, also run twice here) |

### C# mutations (fix head, rebuilt each time; `DeterminismTests` + `DeterminismAssetTests`)

| Mutation | Without assets | With assets |
| --- | --- | --- |
| guest restore no-op (`ParkWorldStreams`) | fails `LoadRestoresEveryStream…` | + `ParkSaveAndLoadThroughTheLevelPath…` |
| script restore no-op | fails `LoadRestores…`, `SaveAndLoadMidRun…` | + level-path test |
| sound reseed no-op | fails `LoadRestores…` | + level-path test |
| `LoadPark` skips the stream restore | **passes** | fails level-path test |
| sound seed back in the hash | fails `SoundDrawsDoNotChange…` | same |
| capture takes the sound seed from `WorldSeed` | fails `LoadRestores…` | + level-path test |
| `System.Random r = new();` in `Level.Objects.cs` | fails guard | same |
| `private static long counter;` in `WorldSeed.cs` | fails guard | same |
| `s.GetHashCode()` in `ParkWorldStreams.cs` | fails guard | same |
| `public static int Next { get; set; }` in `Level.cs` | fails guard | same |

Baseline without mutation: 12 pass / 2 skip without assets, 14 / 0 with.

### N1. Guard forms still not caught (not blocking)

`ForbiddenInSimulation` at `d620fb7` does not match: a static `Random` field,
seeded or not, `readonly` or not (one stream shared by every park in the
process); mutable static collections and arrays (`static List<int>`,
`static int[]`); static enum or struct fields (`static GameSpeed speed;`); any
line that contains the word `readonly` elsewhere, e.g. in a trailing comment;
`RandomNumberGenerator`; `DateTime.Today`. A search of the scanned files at
`d620fb7` finds none of these in use (the only static members are get-only or
`readonly` tables, and the only `Guid.NewGuid` is `ParkSaveFile`'s temporary
file name). DETERMINISM.md lists only `Guid.NewGuid` and `Parallel` as not
forbidden. Fix when convenient: forbid `static (readonly )?(System\.)?Random\b`,
widen the static-field rule to any non-`readonly` static field except `bool`
flags (or list allowed types), strip `//` comments before matching, and list
the remaining gaps in DETERMINISM.md. `test_det_v2.GuardRound2.test_known_gaps_after_round2`
pins the current behaviour so the fix shows up as a test change.

## Round 2 numbers

| Tree | OpenTPW.Tests without assets | With `OPENTPW_GAME_PATH` |
| --- | --- | --- |
| `d620fb7` | **925 / 245 / 0** (author: same) | **1099 / 71 / 0** (author: same) |
| main `8a88379` | 915 / 244 / 0 | 1088 / 71 / 0 |
| merge `8a88379` + `d620fb7` (resolved as above) | 927 / 246 / 0 | 1102 / 71 / 0 |

Pass / skip / fail. Build of `OpenTPW.sln`: 0 errors on all three trees.

| Check (merge unless noted) | Result |
| --- | --- |
| `fidelity_register.py --write` / `--check` | 157 IDs; `test_fidelity_register.py` OK |
| `run_evidence_checks.py --mac-bin … --dotnet <net8 SDK>` | OK: 11 Python suites, 758 tests, 106 skipped (includes `test_det_v2.py`) |
| `test_det_v1.py` with `OPENTPW_PPC_BIN_ROOT` (`d620fb7` + this review) | 13 / 13 |
| `test_det_v2.py` | 7 / 7 on the merge; 5 pass, 2 skip (no M3 gate) on `d620fb7`. Mutations: a `Random r = new()` in excluded `Sky.cs`, the gate row back to FAIL, and `SchemaVersion = 1` each fail it |
| `--m3-gate` (30 min, two processes each) | merge 13 / 2 / 1 exit 1; main 12 / 3 / 1 exit 1; `--no-determinism` on the merge 12 / 2 / 2 |
| `git diff --check 8a88379` (merge) | clean |
| `git diff --stat` vs `--ignore-cr-at-eol --stat` against `8a88379` | identical (34 files, +3246 / −68, including this review's test); no file's CR count changed |

Not tested: Windows/Linux and x64 hashes; a real audio device;
`Level.SavePark`/`LoadPark` on a constructed `Level` (needs a renderer; the
one-line delegation is read, and `ParkWorldStreams` is tested); Client/ code
for unseeded randomness (not scanned by the guard).
