# Smarter guests: plan, measurement and baseline

The goal is an opt-in official extension that makes park visitors decide better, borrowing ideas (not code) from RollerCoaster Tycoon 1, 2 and 3. The faithful tp.exe
simulation ([GUESTS.md](GUESTS.md)) stays the default: with the extension off, behaviour, determinism pins and saves do not change. OpenRCT2 is GPLv3, so only concepts are
taken: no code, no data tables, no constants. Every phase is one pull request. This page holds the overview, the measurement that is built first, and its baseline.

## Architecture in short (as built in phase 0b)

- **Two extensions** (`OfficialExtensions`, both `DefaultEnabled: false`, version 1.0.0). `opentpw.guest-insights` is UI only (`AffectsSimulation: false`): refusal-reason counters (phase 1) and, later, a
  complaints summary; it is never recorded in saves. `opentpw.smarter-guests` is `AffectsSimulation: true`; the mod set is recorded in park saves (`ParkResumeMods`
  refuses a mismatched set), and the extension version is the algorithm identity: every behaviour-changing PR bumps it, and a park of another version is refused with a clear message
  (`needs opentpw.smarter-guests 1.0.0, not 1.1.0`). `FeatureGates.SmarterGuests` and `FeatureGates.GuestInsights` read them. **Smarter guests cannot be switched on by a player yet** (`IOpenTpwExtension.PlayerSelectable` is false; guest insights became selectable in phase 1): a no-op
  1.0.0 would lock saves to an extension that does nothing, and the next version bump would make them unloadable. They stay registered so tests and development code can use them
  (`OpenTpwExtensions.Use`); the Extensions and mods screen hides them, and a `mods.json` entry or `--enable-mod` for them is ignored with a log line. They become selectable with the first real rule
  (phase 2 for smarter guests, phase 1 for guest insights). Their names and descriptions exist in the nine interface languages for that day (the screen shows a selectable extension's, and a test pins it).
  **Version policy:** the extension's version is the algorithm identity. A park saved by another version is refused with a clear message (`needs opentpw.smarter-guests 1.0.0, not 1.1.0`) and stays in the
  load list marked as requiring it; a later release may offer loading an older version's park with the newer rules as an explicit "migrate" choice. Park sharing stays blocked while a simulation extension is
  active, as for every simulation mod, until packages record mods.
- **A pure decision layer** (`World/Guests/Decisions/`). `GuestSimulation` takes an `IGuestDecisions` at construction (null: the gate decides, read once, in the constructor and never again):
  `OriginalGuestDecisions`, a stateless pure extraction of the original, or `SmarterGuestDecisions`, which in 1.0.0 delegates every call to it. A policy gets a `DecisionContext` (settings, park
  turn, weather, park seed and a `Trait` accessor) and views (`GuestView`, `AttractionView`, ...) and returns a typed decision or a comparison. The host keeps every effect: routing, queue insertion
  and removal, payments, statistics, thoughts, happiness and every `WorldRng` draw. **By construction no policy can take, skip or reorder a draw or a route request**: the interface has no generator and
  no delegate, the host runs every loop and calls the policy only to score or compare, and the host throws an `InvalidOperationException` naming the policy for a result it cannot act on (an admission other
  than joined / too exciting / not exciting enough / queue too long, an undefined booth verdict, a wander goal off the map). The seven decision points:

  | Point | Host does | Policy decides |
  | --- | --- | --- |
  | Booth verdict | takes the verdict's draw first, then (for an expensive verdict) the wait draw; thoughts, happiness, payment | `DecideBooth( BoothView with the drawn word )` -> `BoothVerdictKind` |
  | Major decision | runs the choice loop newest first: asks the policy for each score, then whether it beats the best (`BeatsInMajorDecision`), and only then makes the counts-test and the route request itself, as before | `ScoreAttraction` and `BeatsInMajorDecision` (the original: floor of 10, signed, tie on an odd turn) |
  | Minor decision | scans the 4 x 4 window in the original order, tests the two routes, retargets | `ScoreAttraction` and `BeatsInMinorDecision` (the original: unsigned, tie on an odd turn) |
  | Queue admission | the physical checks (`CheckJoinRoom`: open, not listed, room), then appends the admitted guest (`AppendToQueue`) | `DecideAdmission`: the excitement gate, then the queue limit |
  | Queue patience (new) | calls it after the existing leave tests on every turn a waiting guest stays (never while called forward or in an interlude; it can run on the turn a move-up has just started, which keeps the original's flow, and the view then still shows the old place); a leaving guest is dismissed with the policy's thought (`QueueEndReason.Impatient`) | `DecideQueuePatience` -> stay (the original) or leave |
  | Post-use memory | the use's effects | `RememberUse` -> whether the attraction enters the used history |
  | Wander goal | runs the original destination search with its path-dependent draws | `ChooseWanderGoal( proposal )` -> the goal to walk to (the original: the proposal) |

  The wander's off-path search (`GuestWander.OffPath`, a start cell without links) stays host-only. The policy sources are checked against a list of banned APIs (hash codes, clocks, GUIDs, any random,
  hash-set or dictionary, threads) and the policy types by reflection (sealed, no mutable field); the histories in a `GuestView` are read-only wrappers that cannot be cast back to the guest's arrays;
  the extension's variation comes from `SmarterGuestsState.Trait`.
- **Script mods on the seam** (phase 4 of [SCRIPT-MODS.md](SCRIPT-MODS.md)). When script mods are enabled, `GuestSimulation` wraps the base policy (original or smarter) in `ScriptGuestDecisions`, which chains the mods' hooks over the
  base answers for `ScoreAttraction`, `DecideAdmission`, `DecideQueuePatience` and `ChooseWanderGoal` (the other four points pass through). Small additions to the seam serve it, none of which changes a policy that ignores
  them: `IGuestDecisions.BeginDecision()`/`EndDecision()` (default no-ops, bracketing each candidate loop so the scores of a decision can be treated as a unit), `IGuestDecisions.BeginTick()` (a default no-op, called at the start of every tick so a policy that meters work per tick, the scripts' step budget, starts its tick; it is the one place a policy holds state, and
  that state is the script mods' own, saved in their section), `DecisionContext.GridWidth` and `GridHeight` (a wander goal must lie in the map; 0 when unknown), and `UsesExtensionState` now also means "needs the guest identity
  state". That state (`SmarterGuestsState`: park seed, counter, birth ids) is **split from the smarter-guests rules**: a script policy brings it, the guests get birth ids at creation with no draw, and nothing else about the
  guests changes (a test runs a mod that only answers `prev` against the off run: equal guest hash and `WorldRng` state). A park with script mods saves the `SmarterGuests` section without the extension's record
  (`SmarterGuestsSave.ShapeProblem( ..., scriptMods )`). The policy still takes no generator; a script's `rng` is a stateless hash of its call identity.
- **Randomness.** Extension off: the `WorldRng` sequence is untouched (the existing pins prove it). On: the promise is deterministic replay for the same seed, set and save, not
  stream equality with the original; with the delegating policy of 1.0.0 the stream is the original's anyway. The policy itself draws nothing.
- **Identity.** With the extension on, `Guest.BirthId` (64 bits, 0 = none) is allocated in `GuestSimulation.Create`, after the creation's draws and before the guest's first decision (the booth
  sees it). The counter lives in `SmarterGuestsState`, beside the guest simulation, never in the guest snapshot or in `GuestSimulation.ComputeStateHash`. Guests without an id get one on the extension's **first tick**, in the park's
  thing-list order (newest first, then any guest outside the list by id). In practice that is the guests of an imported original save (and tests that restore a guest snapshot without the section): a park
  saved by this build with the extension on carries its ids, and a save made with it off is refused at load while it is on. An imported guest of a running extension gets its id at once. A counter at its limit is refused (`OverflowException`) before the creation takes a thing slot or a draw, never wrapped, and a save whose counter is `ulong.MaxValue` is invalid. `SmarterGuestsState.Trait( parkSeed, birthId, traitIndex )` is a stateless
  64-bit hash (SplitMix64's finalizer applied to each input); the park seed is the simulation's seed (the world seed's guest stream), kept in the state so a resume continues the same personalities.
  Nothing uses it yet beyond tests.
- **Persistence.** `ParkResumeSnapshot.SmarterGuests` (`SmarterGuestsSaveData`: park seed, counter, first-tick flag, and each guest's birth id keyed by the guest's thing id, the identity a restore
  rebuilds guests by) is `null` and left out of the file while the extension is off, so every other save is byte-identical to before. It has its own capture, shape validation (a section exists exactly when
  the extension is recorded in `Mods`; ids non-zero, below the counter, unique, naming guests the save holds; after the first tick every guest has one), restore (`SmarterGuestsSave`) and canonical
  hash. A build from before the section refuses a save that has it (the strict codec rejects unknown members), which is intended; the save version stays 1. Later phases add their state to this section.
- **The world hash.** `SmarterGuestsState.AddCanonicalState` joins `WorldStateHash` only while the extension is on (as the bump, tour and coaster modules do), so the off-path hash does not change and
  `WorldStateHash.SchemaVersion` stays 21. `GuestSimulation.ComputeStateHash` and the guest snapshot never see the extension, which is why a delegating policy gives the off run's guest state exactly.
- **Not supported at first:** online park sharing (park packages do not record simulation mods yet) and tuning by data mods (phase 7).

## Phases (one PR each)

| Phase | Content |
| --- | --- |
| 0a | Passive measurement: `ParkScenario`, instrumentation, `--guest-report`, this baseline and its thresholds. No extension, no behaviour change, every pin unchanged. |
| 0b | Seam and identity (built, below): both extensions, `FeatureGates`, `IGuestDecisions` with `OriginalGuestDecisions` as a pure extraction, the birth-id section, off/on pins and a resume test. |
| 1 | Top 3 Thoughts (fidelity, built: [below](#phase-1-top-3-thoughts-and-the-refusal-counters)) and `opentpw.guest-insights` refusal-reason counters (built, selectable). |
| 2 | Value for money: price enters the shop and sideshow score; remember too-expensive shops; the booth weighs the fee against the park; a price sensitivity trait. |
| 3 | Spreading: a throughput-aware queue estimate for every attraction, a stable per-guest preference per instance, hysteresis. |
| 4 | Queue patience as a new rule: expected wait against a trait-scaled patience; leaving gives a thought and a small happiness cost. |
| 5 | Planning needs: a shop or toilet first when a meter will cross its threshold, a detour bonus, no high-excitement ride right after eating. |
| 6 | Clean and quiet paths: wander goals weighted by litter and crowding, with thoughts. |
| 7 | Tuning by data mods (optional). |
| 8 | RCT3 groups and families (only if wanted). |

## Phase 1: Top 3 Thoughts and the refusal counters

**Top 3 Thoughts (fidelity).** Re-validated in tp.exe: the original park information window (builder `0x4A52A0`, table `0x7537D0`) shows the three most common guest thoughts as three icons of the `ti_hungry` model, ranked by `0x4C8430`
(counts of the thought ids 0 to 22 over the guests on park cells, the count of id 0 discarded; three slots start as 0, 1 and 2 and take each larger id by the largest count difference, ties keep the lower id, slots unsorted, a zero count shows nothing) and refreshed by a one-second timer.
The full rule, the layout and the order of the icons are in [GUESTS.md](GUESTS.md) "Top 3 Thoughts". It is not a "most common thought" statistic: `GuestStatistics.CommonThought` was removed. Not built from the window: the line graph and toggles,
the counts frame, the happiness levels and the three navigation buttons (`APPROX:UI-914`).

**Refusal counters (`opentpw.guest-insights`).** A passive `IGuestObserver` (`GuestRefusalCounters`), created by the guest simulation when the extension is on (the gate is read once, when it is built), shares the observer slot with a measurement collector
through `GuestObserverChain`. It counts five reasons from the queue-end reports the measurement already has: **too expensive** (`Price`, the stand-point refusal of a shop or sideshow), **queue too long** (`Full`, `QueueLimit`), **too scary**, **too boring**,
and **ride broken or closed** (`RideBroken`, `RideClosed`: a queue head sent away). `NotAccepting` is left out because it cannot tell a closed ride from a double join, and the guests' own leaving tests are abandonments, not refusals. The totals show in the
park information window, in the column where the original keeps its line toggles, in the original UI style (the window's labels and fonts), with the labels in the nine interface languages. Never saved, never hashed, no draw: with it on, the guests'
state hash, random state and guest snapshot are those of a run with it off (tested on the scenario park), `ParkResumeMods` records nothing for it, and a loaded park starts again from zero.

## Measurement

Everything here is passive: no behaviour change, nothing saved, nothing in `WorldStateHash` or `GuestSimulation.ComputeStateHash`, no `WorldRng` draw.

**Instrumentation.** `GuestSimulation.Observer` (`IGuestObserver`, `source/OpenTPW/World/Guests/GuestObserver.cs`) is null by default; each hook is one null test. The collector
`GuestMetrics` (`World/Guests/Measurement/GuestMetrics.cs`) tallies, for a measured window:

- Every **queue attempt** (a guest at the back of a queue) ends as exactly one of: *boarded*, *abandoned* (the guest's own tests: happiness below 10, toilet need, boredom),
  *rejected* (with a reason: `Full`, `TooScary`, `TooBoring`, `QueueLimit`, `NotAccepting`, `Price` at the stand point, `RideClosed`, `RideBroken`, `QueueShortened`,
  `QueueRemoved`, `NoPlace`, `ReliefWornOut`, `TrackRefuses`, `AttractionRemoved`) or *unfinished* (still queued when the window closes). An attempt counts when it starts inside the window.
  A fifth counter, *superseded*, catches an attempt that ended with no report (the guest joined again, or departed, with one open): the simulation never does that, so the tests assert it is
  0 and the identity attempts = boarded + abandoned + rejected + unfinished + superseded is not true by construction.
- **Waits** are reported over boarded attempts, next to the abandon and reject rates, and they are **right-censored**: an attempt still queued at the window's end has no wait yet, so the
  boarded percentiles undercount the long tail. The report therefore also gives the ages of the unfinished attempts at the window's end and a *censored* percentile over the boarded waits
  and those ages together, a lower bound of the true percentile (in the Fantasy park the boarded p90 is 559 turns and the censored one 787).
- **Departures** with the real predicate (`GuestLifecycle.ShouldLeave`): happiness byte 0, exit counter 0 or park closure, evaluated as the guest decides to leave. The booth
  (`BoothRefused`, including a guest restored from an older save on its way home from the booth) and a guard (`Guard`) send guests home without that test. A recalled guest forgets its
  announced departure. The flags overlap (a guest can meet two predicates), so the counts need not add up to the total. The report compares departures per admission, not absolute counts,
  because the window's admissions differ between scenarios.
- **Happiness per admission cohort** (the in-game day of admission; `carryOver` for guests admitted before the window) and **weighted by guest time** over every admitted
  guest-second, next to the happiness of the guests still inside at the end (the survivors).
- **Use per attraction instance** (boardings), with the Gini coefficient over the rides and over all attractions, and a capacity-normalised Gini (uses divided by the ride's capacity).
  A perfect spread does not give 0: rides of different excitement and ride time are meant to be used differently, so the Gini of the scenario is judged only as a change, never against 0.
  The Gini is 0 for the level parks, which have one ride, and equal to the raw one in the scenario, where every ride has capacity 1.
- **Revenue per admitted guest** (entrance fees and spending in the window over the admissions in the window) is a flow ratio, not the revenue of a cohort: it includes spending of guests
  admitted earlier and omits what guests admitted late will still spend. It is comparable between runs of one protocol only.
- **Stuck guests**: guests whose major decisions found nothing for more than 50 park turns; admissions.
- **Save and restore.** A restore rebuilds every guest as a new object, so the collector carries what it holds per guest over by guest id (`Restored`); a save and restore inside a window
  leaves the tallies as they were (a test checks the whole snapshot).
- `Ticked` is the sampling point of the guest-time tallies: it fires when the guests' update of a tick is over. The arrival update, and in a swept turn the host's admission, destroys and
  arrivals, follow it, so what the guests' happiness shows is the state before them.

**Scenarios.** `ParkScenario` (`World/Guests/Measurement/ParkScenario.cs`) is a headless park with no game data and no economy (its park turns are swept the way the game's are): a 40-cell street from the gate with three
branches carrying two identical rides, a thrill ride (excitement 95, too scary for the calm guest type) and a calm ride (excitement 20, too boring for the thrill-seeking type), a
cheap shop (price 30) and a dear shop (price 220, which most guests refuse at the stand point), and a toilet at the far end of the street (a long walk; the rides and shops are a short one).
Every attraction serves one guest at a time through the corpus shop protocol with its own hold time. With game data, `--guest-report` also runs the level parks of the four themes
through the M3 gate's park builder ([M3-GATE.md](M3-GATE.md)): one ride, a shop and a toilet each. The level parks use an arrival timer of 20 (`--guest-arrivals`), because the
themes' 150 would bring no group within a few days. `SyntheticPark` in `DeterminismTests` and its pin are unchanged.

**Seeds that stay different.** The original reseeds the world generator with each new guest's thing id (`WorldRng.Reseed`, tp.exe 0x004FB19E), so two seeds' streams merge into one after a few
dozen days and their runs become identical: with plain seeds the scenario's seeds 1 and 4 were the same park from about day 40, and the pairs 5 and 10, 9 and 11 by day 80, so the baseline had fewer
than eight independent samples. `ParkScenario` therefore gives every seed its own ride and shop times (a hold-time offset of -200 to +199 ms, `HoldJitterMilliseconds`), which the reseed cannot
erase; the load is the same on average. The report records every seed's end state hash, flags seeds with the same hash (`WARNING` in the table, `duplicateSeeds` in the JSON), leaves
the duplicates out of the spread and prints the distinct-seed count; a test checks that eight seeds stay distinct at 40 and 80 days. The level parks' seeds vary the economy's streams as
well and also end distinct (8 of 8 in every park).

**Turn order.** The runs use the game's turn order (below), with these differences from `Level.AdvanceSimulation` that remain: the scenario has no economy (its turns are the guests' own); the gate's
sweep walks the guests by the pool newest first but then updates the economy's things in `ParkEconomy.UpdateThings`' fixed order, not interleaved with them, and has no staff world, weather,
challenge system or gate retry; only the gate and the lights hold thing slots besides the guests; the guests' and the economy's turns must agree (the gate asserts it instead of falling back to
a plain tick).

**Protocol.** Eight fixed seeds (`WorldSeed` 1 to 8) per scenario, a warm-up, then a measured window, both in in-game days (23 park turns, about 343 fixed ticks, 5.7 s at normal speed).

```sh
dotnet source/OpenTPW/bin/Release/net10.0/OpenTPW.dll --guest-report [--guest-scale plan|extended] [--guest-seeds 8] [--guest-warmup-days N] [--guest-days N] \
  [--guest-levels jungle,hallow,fantasy,space|all|none] [--guest-level-seeds 8] [--guest-arrivals 20] [--guest-report-json report.json] [--guest-no-perf]
```

Without game data (`OPENTPW_GAME_PATH` or `--game-path`) only `park-scenario` runs. The command prints a short table (mean, σ, minimum and maximum over the seeds, and the reject and
abandon reasons) and, with `--guest-report-json`, the stable machine-readable report (`opentpw.guest-report/1`: every seed's metrics, the per-metric spread and, unless
`--guest-no-perf`, a machine-dependent `performance` block that is left out of comparisons). Two runs with the same options give the same JSON apart from `performance`.
The plan scale (2 warm-up + 10 measured days, the default) takes about 20 s with the four level parks; `--guest-scale extended` (20 + 60 days) about 35 s.

**Decision: two scales.** The plan scale (2 + 10 days) stays the quick default of `--guest-report`; `--guest-scale extended` (20 + 60 days) is the scale of the baseline and of every acceptance threshold on this page.

With `--guest-extension smarter-guests` (repeatable; the extension's id works too) the whole run builds its simulations with the extension on (each reads the gate once, when built), the JSON gets an
`extensions` list beside its protocol (always present, `[]` for the default run), and the table says `Extensions on: ...`. With the delegating policy of 0b the report is the default's: every seed ends in the same guest state hash and
`--guest-compare off.json on.json` shows every metric `same` (a note names the two extension sets).

With `--guest-mod <folder or id>` (repeatable) the run switches the named mods on (a mod folder, or the id of a mod shipped with OpenTPW, such as the example script mod); the JSON gets a `mods` list beside `extensions`, each scenario a
`scriptMods` list (calls, steps, the largest call and tick, failures, seeds that disabled the mod), the table the same as `script mod ...` lines, and `--guest-compare` names differing mod sets. A mod that cannot run is an error, not a silent
baseline. The measured budgets and the example mod's effect are in [SCRIPT-MODS.md](SCRIPT-MODS.md#budgets-measured).

To compare two runs: `--guest-compare baseline.json candidate.json [--guest-targets metric,metric] [--guest-compare-k 3]` prints every metric's change in standard errors with a verdict
(`improved`, `regressed`, `changed`, `same`, `undefined`) and exits 1 on a regression, a named target that did not improve, or a non-zero `queue.superseded`.

**Why an extended scale.** A day is about 343 ticks, and a guest's exit counter runs about 120 s (about 21 days). At the plan's 2 + 10 days the scenario sees 3 to 8 queue attempts and
no departures per seed, far too little for a spread to mean anything. The baseline and the thresholds below therefore use the extended scale (20 warm-up + 60 measured days); the plan
scale is recorded for completeness.

## Baseline

Re-measured on October 17, 2026 on main after #325 and #326 (see [What moved](#what-moved-in-the-october-17-re-measurement)); the earlier numbers are listed there.

The runs go through the game's turn order, not plain `Tick` loops: the tick that starts a park turn runs the substep's ride scripts and then the turn's thing sweep over the thing pool newest first (the guests, the attractions' admission), the turn's destroys and the arrival update, as `Level.AdvanceSimulation` and `DeterminismTests.SyntheticPark.Run` do (`BeginSweepTick`, `SweepGuest`, `FinishSweep`, `UpdateTurnEndArrivals`). `ParkScenario.Run` and the level parks (`M3GateOptions.SweptTurns`, which only the report sets, so the gate keeps its own loop) both use it. The scenario has no economy, so its turns are the guests' own; the level parks' economy turns are aligned to them. Measured on plain loops first, the baseline differed slightly (for example the scenario's guest-time happiness was 48.8 and its boarded share 55.1%).

Original policy, release build, 8 seeds per scenario. Reproduce with `--guest-scale extended`. The same seeds reproduce these numbers exactly; σ is the sample standard deviation over
the 8 seeds, so it is the seed-to-seed variation a rule has to beat.

### park-scenario, extended scale (20 + 60 days)

| Metric (key) | Mean | σ | Min | Max | Band (3 SE) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Admissions in the window (`admissions`) | 63.875 | 0.354 | 63 | 64 | ±0.958 |
| Queue attempts (`queue.attempts`) | 341.5 | 22.916 | 305 | 374 | ±34.374 |
| Boarded / attempts (`queue.boarded.rate`) | 0.544 | 0.02 | 0.525 | 0.577 | ±0.03 |
| Abandoned / attempts (`queue.abandoned.rate`) | 0.026 | 0.008 | 0.011 | 0.036 | ±0.013 |
| Rejected / attempts (`queue.rejected.rate`) | 0.41 | 0.019 | 0.371 | 0.427 | ±0.029 |
| Unfinished / attempts (`queue.unfinished.rate`) | 0.02 | 0.006 | 0.013 | 0.028 | ±0.009 |
| Wait p50, turns (boarded only) (`queue.wait.p50`) | 11.625 | 2.066 | 9 | 15 | ±3.099 |
| Wait p90 (boarded only) (`queue.wait.p90`) | 73.625 | 7.308 | 61 | 80 | ±10.962 |
| Wait p99 (boarded only) (`queue.wait.p99`) | 108.25 | 4.743 | 100 | 113 | ±7.115 |
| Wait p90, boarded + unfinished ages (`queue.wait.p90.censored`) | 73.375 | 7.09 | 61 | 80 | ±10.635 |
| Wait p99, boarded + unfinished ages (`queue.wait.p99.censored`) | 108.125 | 4.643 | 100 | 113 | ±6.964 |
| Superseded attempts (must be 0) (`queue.superseded`) | 0 | 0 | 0 | 0 | ±0.75 |
| Happiness, guest-time weighted (`happiness.guestTime`) | 49.007 | 3.13 | 44.33 | 53.311 | ±4.695 |
| Happiness of the guests still inside (`happiness.survivors`) | 48.217 | 4.606 | 42.238 | 55.421 | ±6.91 |
| Departures per admission (`departures.perAdmission`) | 0.599 | 0.051 | 0.531 | 0.672 | ±0.076 |
| Happiness-byte-0 departures per admission (`departures.happinessZero.perAdmission`) | 0.528 | 0.047 | 0.469 | 0.625 | ±0.071 |
| Exit-counter-0 departures per admission (`departures.exitCounterZero.perAdmission`) | 0.07 | 0.03 | 0.016 | 0.109 | ±0.045 |
| Park-closed departures per admission (`departures.parkClosed.perAdmission`) | 0 | 0 | 0 | 0 | ±0.003 |
| Use Gini over the rides (`use.giniRides`) | 0.198 | 0.037 | 0.147 | 0.255 | ±0.055 |
| Use Gini over the rides, per capacity (`use.giniRidesPerCapacity`) | 0.198 | 0.037 | 0.147 | 0.255 | ±0.055 |
| Use Gini over all attractions (`use.giniAll`) | 0.288 | 0.03 | 0.235 | 0.328 | ±0.045 |
| Revenue per admitted guest (flow ratio) (`revenue.perAdmittedGuest`) | 82.062 | 8.502 | 70 | 95.556 | ±12.753 |
| Guests with no choice for over 50 turns (`guests.stuck`) | 1.25 | 1.035 | 0 | 3 | ±1.553 |
| Guests in the park (mean) (`guests.meanInPark`) | 32.84 | 1.877 | 29.946 | 35.107 | ±2.815 |

Outcome mix over the 8 seeds: rejected `Full` 90, `Price` 761, `TooBoring` 106, `TooScary` 162; abandoned `NeedToilet` 2, `Unhappy` 69. About 54% of the attempts board, 41% are rejected (most of them the dear shop's price at the stand point), 3% abandon and 2% are unfinished when the window closes (the oldest has waited 30 to 87 turns). About 0.6 guests depart per admission in the window, mostly by the happiness byte reaching 0 (the exit counter fires only for a guest that is in the right state when it passes 0, see `GuestLifecycle`). Guests wait a median of 12 turns and a p99 of 108 turns; no wait is capped, because no queue time limit is reachable in tp.exe. Here the censoring is small (the unfinished attempts are young); in the level parks it is not.

### Level parks, extended scale (mean ± σ over 8 seeds)

| Metric | Jungle | Halloween | Fantasy | Space |
| --- | ---: | ---: | ---: | ---: |
| Queue attempts | 802.875 ± 24.416 | 154.875 ± 11.969 | 391.625 ± 39.619 | 494.75 ± 17.044 |
| Boarded / attempts | 0.254 ± 0.01 | 0.684 ± 0.041 | 0.454 ± 0.025 | 0.481 ± 0.021 |
| Abandoned / attempts | 0.013 ± 0.003 | 0.011 ± 0.009 | 0.041 ± 0.006 | 0.054 ± 0.012 |
| Rejected / attempts | 0.698 ± 0.011 | 0.279 ± 0.042 | 0.41 ± 0.034 | 0.389 ± 0.03 |
| Wait p50 (turns, boarded) | 31.875 ± 0.835 | 11.625 ± 1.188 | 25.625 ± 2.615 | 33 ± 1.069 |
| Wait p90 (boarded) | 538.875 ± 20.677 | 194.25 ± 6.902 | 531.875 ± 22.535 | 434.375 ± 9.054 |
| Wait p99 (boarded) | 642 ± 39.648 | 213.75 ± 3.495 | 999.875 ± 43.813 | 468.125 ± 7.624 |
| Wait p90, boarded + unfinished ages | 528.5 ± 24.054 | 194.125 ± 6.813 | 738.375 ± 36.347 | 432.375 ± 9.97 |
| Wait p99, boarded + unfinished ages | 641.625 ± 40.007 | 213.625 ± 3.583 | 1006.5 ± 55.185 | 468.125 ± 7.624 |
| Happiness, guest-time weighted | 53.043 ± 0.515 | 37.822 ± 0.886 | 49.217 ± 0.469 | 48.452 ± 0.555 |
| Departures per admission | 0.276 ± 0.022 | 0.451 ± 0.066 | 0.355 ± 0.042 | 0.373 ± 0.02 |
| Happiness-0 departures per admission | 0.166 ± 0.009 | 0.273 ± 0.029 | 0.246 ± 0.025 | 0.313 ± 0.021 |
| Exit-counter-0 departures per admission | 0.111 ± 0.022 | 0.177 ± 0.049 | 0.109 ± 0.031 | 0.063 ± 0.01 |
| Revenue per admitted guest | 47.633 ± 0.503 | 59.312 ± 1.49 | 55.268 ± 2.314 | 46.002 ± 0.533 |
| Guests with no choice > 50 turns | 72.5 ± 3.703 | 59.125 ± 2.475 | 70.5 ± 8.635 | 64 ± 4.536 |
| Guests in the park (mean) | 104.336 ± 1.624 | 36.951 ± 0.922 | 87.059 ± 4.115 | 97.885 ± 1.671 |

Outcome mix (all seeds): Jungle rejected `Full` 813, `QueueLimit` 3674, abandoned `NeedToilet` 1, `Unhappy` 81; Halloween rejected `Full` 348, abandoned `Unhappy` 14; Fantasy rejected `Full` 1293, abandoned `NeedToilet` 1, `Unhappy` 127; Space rejected `Full` 649, `QueueLimit` 892, abandoned `Unhappy` 214. With a single ride a park saturates: most attempts are rejected, waits reach hundreds of turns and a large share of the guests find no choice for a long time. The waits are right-censored: in Fantasy 9.5% of the attempts are still queued at the end, the oldest for 888 to 1,134 turns of a 1,380-turn window, so its boarded p99 (1,000) and even its p90 (532 against a censored 738) understate the waiting; use the censored rows. That is the headroom the later phases are measured against.

No ride is closed or broken in these runs. (An earlier version of this report counted `RideClosed` rejections that were the gate stopping its objects after the last tick, which turns the queues away; the window now closes before the teardown.)

### park-scenario, plan scale (2 + 10 days)

| Metric | Mean | σ |
| --- | ---: | ---: |
| Queue attempts | 5.5 | 1.069 |
| Boarded / attempts | 0.604 | 0.235 |
| Rejected / attempts | 0.267 | 0.215 |
| Wait p90 (turns) | 15.75 | 14.099 |
| Happiness, guest-time weighted | 49.47 | 2.885 |
| Happiness-0 departures per admission | 0 | 0 |
| Use Gini over the rides | 0.543 | 0.163 |
| Revenue per admitted guest | 20.469 | 1.326 |

Too few events for thresholds (this is the plan-scale re-measure, so these rows also moved with #325): `queue.attempts` is about 5 and there are no departures.

### What moved in the October 17 re-measurement

The first baseline was measured before two merges. #326 (the substep scheduler: script slices once per eight passes, tours on even substeps, the bump module) leaves every number of this report unchanged: the report was re-run on the commit before and on #326 and the 40 end hashes and all spreads are identical (the parks of the report do not exercise what the new scheduler changes). The whole shift therefore comes from #325 (old saves' access links, WALK-006 retired, stand cells in `ParkScenario`): a guest whose stand-point route search is refused is now dismissed as tp.exe dismisses it, instead of walking in on the route-walk fallback, and the scenario's attractions now have their entrance cells, so called guests walk the original's search walk.

Scenario (old baseline to new, 8 seeds; the band is the new 3 SE): queue attempts 326 to 341.5 (±34), boarded share 0.560 to 0.544 (±0.030), rejected 0.392 to 0.410, abandoned 0.021 to 0.026, wait p90 (censored) 79.1 to 73.4, p99 119.5 to 108.1, guest-time happiness 48.53 to 49.01 (±4.7), departures per admission 0.562 to 0.599 (±0.076), happiness-0 departures 0.481 to 0.528 (±0.071), guests in the park 31.9 to 32.8, revenue per admitted guest 79.1 to 82.1, stuck guests 0.625 to 1.25 (at most 3 in one seed, ±1.55). Every scenario change is inside the new band. The scenario's fixed-run hash is `0xBD62B929BDB55659` (was `0xF430B24D6D6B950E`).

Level parks: Space boards more (0.398 to 0.481, rejected 0.488 to 0.389, `QueueLimit` rejections 1,283 to 892), Jungle boards more (0.219 to 0.254, rejected 0.736 to 0.698), Fantasy slightly more (0.433 to 0.454), Halloween slightly less (0.712 to 0.684, censored wait p90 165 to 194). Queue waits fall in the three busy parks (censored p90: Jungle 556 to 529, Fantasy 787 to 738, Space 479 to 432), guests in the park and happiness move by under 1 point or 2 guests, and the stuck-guest counts are unchanged within their spread (Jungle 70 to 73, Halloween 59, Fantasy 72 to 71, Space 65 to 64). These parks are all the single-ride saturated case, so the shift is guests being called into the stand point more reliably, not a change in demand.

Checked and not a regression: no ride goes unused (the scenario's ride Gini is 0.198, min 0.147 over the seeds), the scenario's stuck guests stay at most 3 in a seed and the level parks' counts keep their spread, no scenario or park lost its boarded share beyond Halloween's 0.03, which is inside the seed spread (±0.04), the 5 scenarios x 8 seeds still end in 40 distinct state hashes (`duplicateSeeds` is empty everywhere) and `queue.superseded` is 0. The acceptance thresholds are defined in code from the report (`GuestReportCompare`), so no constant changed; the bands above are the new ones. Compare a smarter-guests run only against a report made on the same commit or later.

### Cost of the instrumentation

CPU and allocation per tick over the extended window (release build, Apple silicon, machine under load, so about ±20% noise): observer off about 1.2 µs and 612 B per tick (the
production path: one null test per event), observer on 1.2 µs and the same 612 B; the two sides are indistinguishable. The default path was also compared with the commit before
this change on a 41,160-tick run of the earlier plain-tick scenario loop (the best of eight runs in each of five alternating process pairs): 1.182 µs per tick before, 1.194 µs after (+1%, inside the noise), and the
guests' state hash is identical. The allocation per tick is the simulation's own; the observer adds none on the default path. Compare CPU only as a ratio on one machine, from the
`performance` block of one report.

## Acceptance thresholds

Defined from the extended baseline above, in code (`GuestReportCompare`, `--guest-compare`). A later phase re-runs `--guest-report --guest-scale extended` with its extension on and compares
scenario by scenario against the original policy, in the same environment and with the same seeds.

- **Noise unit.** σ(scenario, metric) is the sample standard deviation over the distinct seeds. It is floored at 1% of the mean and at an absolute floor for the kind of metric (0.002 for rates, per-admission
  rates and Ginis; 1 turn for waits; 0.1 for happiness; 0.5 for counts and revenue), so a metric with no spread (the fixed arrival timer makes `admissions` nearly constant) or a zero mean cannot make noise significant.
- **Standard error.** The difference of two means of n seeds each has a standard error of unit·√(2/n) (8 seeds: unit/2); with two different units it is √(u₁²/n₁ + u₂²/n₂). The `Band (3 SE)` column
  of the tables, and `band3se` in the JSON, is three of them for an 8-against-8 comparison.
- **A rule improves its target metric** when the candidate's mean moves in the wanted direction by more than 3 standard errors on every scenario where the metric is defined (not zero in both runs).
  Three, not two, because one comparison makes about 55 directed checks (11 metrics with a direction, 5 scenarios), so at 2 standard errors about 5 per cent of them, 2 to 3 checks, would flag by chance alone; at 3 about 0.3 per cent do,
 ; the compare command run on a report against itself flags nothing. The target metrics are:

  | Phase | Target metric (wanted direction) |
  | --- | --- |
  | 2 value for money | Price rejections as a share of attempts (down), `happiness.guestTime` (up) |
  | 3 spreading | `use.giniRides` or `use.giniRidesPerCapacity` (down) and `queue.wait.p90.censored` (down), on `park-scenario` only (the level parks have one ride) |
  | 4 queue patience | `queue.wait.p99.censored` (down) and `queue.wait.p90.censored` (down) |
  | 5 planning needs | `departures.happinessZero.perAdmission` (down) and `happiness.guestTime` (up) |
  | 6 clean and quiet paths | `happiness.guestTime` (up) |

- **No regression elsewhere:** every other metric with a direction must not move the wrong way by more than 3 standard errors. Up is good for `queue.boarded.rate`, `happiness.guestTime`,
  `happiness.survivors` and `revenue.perAdmittedGuest`; down is good for `queue.rejected.rate`, the `queue.wait.*` rows (boarded and censored), `departures.perAdmission`,
  `departures.happinessZero.perAdmission`, `use.giniRides`, `use.giniRidesPerCapacity` and `guests.stuck`. The others (`admissions`, `queue.attempts`, the abandoned and unfinished rates, the exit-counter
  and park-closed departures, `use.giniAll`, `guests.meanInPark`) are reported as `changed` or `same` without a verdict; a rule may raise `queue.abandoned.rate` only when its own target is the patience rule.
- **No pathology:** `queue.superseded` is 0 (a non-zero count fails the comparison), no oscillation (a guest's target changing back and forth), no empty park (`guests.meanInPark` not far below the baseline) and no stuck guests
  (`guests.stuck` not worse beyond the band).
- **CPU per tick** at most 10% above the original policy's in the same report; the compare command prints the ratio of the two reports' `performance` blocks (noisy: compare on a quiet machine). Allocation per tick is reported.
- **Revenue and golden-ticket feasibility** are reported against the default (`revenue.perAdmittedGuest`, a flow ratio, and the level parks' rows), not gated, so the game does not become trivially easy or hard.

## Tests

`GuestMeasurementTests` (and `GuestMeasurementAssetTests` on the level parks, with game data) covers: every queue attempt ends as exactly one outcome (attempts = boarded + abandoned + rejected + unfinished, also per attraction); waits cover boarded attempts
only; turning the observer on leaves the pinned fixed run's hash (`DeterminismTests.FixedRunHash`) and a scenario run's hash unchanged; `ParkScenario` is deterministic and its
fixed-run hash is pinned (a change moves this baseline); the report JSON is stable for fixed seeds; seeds stay distinct and duplicates are flagged; `superseded` is 0 and unfinished attempts report their age; save and restore keep the tallies; the compare thresholds. The determinism guard scans the simulation folders, so `Stopwatch` lives in
`Client/GuestReport.cs`, not beside the simulation.

`SmarterGuestsTests`, `SmarterGuestsParkResumeTests` and two tests of `DeterminismTests` cover phase 0b: both extensions are registered, off by default, at version 1.0.0 and only the smarter one is
recorded in saves; the gates follow the active set and the policy is read once when the simulation is built (`OriginalGuestDecisions` off, `SmarterGuestDecisions` on); a counting policy proves
every decision point goes through the layer and that a delegating policy gives the same guest hash and the same `WorldRng` state; a patience policy exercises the new hook; the policy sources name no
generator; birth ids are given at creation (the booth sees them), never repeat, are assigned in thing-list order on the first tick for guests without one, and a limit counter is refused; the trait
hash is well mixed; the section round-trips, validates and restores into a fresh instance, and a resumed park hashes like the uninterrupted one every tick for 1,500 ticks; the level captures the section only
while the extension is on and a mismatched set refuses the load. **Pins:** with the extension off every existing pin is unchanged; with it on `DeterminismTests.SmarterGuestsFixedRunHash` pins the
`SyntheticPark` run's world hash (the extension's state included), while that run's guests, economy and guest snapshot are asserted equal to the off run's every 1,200 ticks and `ParkScenarioFixedRunHash`
holds with the extension on.
