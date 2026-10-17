# Smarter guests: plan, measurement and baseline

The goal is an opt-in official extension that makes park visitors decide better, borrowing ideas (not code) from RollerCoaster Tycoon 1, 2 and 3. The faithful tp.exe
simulation ([GUESTS.md](GUESTS.md)) stays the default: with the extension off, behaviour, determinism pins and saves do not change. OpenRCT2 is GPLv3, so only concepts are
taken: no code, no data tables, no constants. Every phase is one pull request. This page holds the overview, the measurement that is built first, and its baseline.

## Architecture in short

- **Two extensions.** `opentpw.guest-insights` is UI only (`AffectsSimulation: false`): refusal reasons and a complaints summary. `opentpw.smarter-guests` is
  `DefaultEnabled: false` and `AffectsSimulation: true`; the mod set is recorded in park saves, and the extension version is the algorithm identity (every
  behaviour-changing PR bumps it; a park of an older version is refused with a clear message).
- **A pure decision layer.** `GuestSimulation` takes an `IGuestDecisions` (the original logic extracted as `OriginalGuestDecisions`, or `SmarterGuestDecisions`). A policy only
  decides from an immutable snapshot; the host keeps every effect (routing, queue edits, payments, statistics, thoughts, happiness, RNG) in the original order. The gate is
  read once when the simulation is built. Decision points: booth verdict, major decision, minor decision while walking, queue admission, queue patience (a new host hook),
  post-use memory and wander goal.
- **Randomness.** Extension off: the `WorldRng` sequence is untouched (the existing pins prove it). On: the promise is deterministic replay for the same seed, set and save, not
  stream equality with the original; the policy itself draws nothing and derives variation from stateless hashes of its own identity inputs.
- **Identity and persistence.** The extension allocates its own birth id per guest (a stateless hash of the park seed and the id gives stable traits). Its state lives in its own
  optional park-resume section, written only when the extension is on; the guest snapshot DTO stays untouched.
- **Not supported at first:** online park sharing (park packages do not record simulation mods yet) and tuning by data mods (phase 7).

## Phases (one PR each)

| Phase | Content |
| --- | --- |
| 0a | Passive measurement: `ParkScenario`, instrumentation, `--guest-report`, this baseline and its thresholds. No extension, no behaviour change, every pin unchanged. |
| 0b | Seam and identity: both extensions, `FeatureGates`, `IGuestDecisions` with `OriginalGuestDecisions` as a pure extraction, the birth-id section, off/on pins and a resume test. |
| 1 | Top 3 Thoughts (fidelity, independent) and `opentpw.guest-insights` refusal-reason counters. |
| 2 | Value for money: price enters the shop and sideshow score; remember too-expensive shops; the booth weighs the fee against the park; a price sensitivity trait. |
| 3 | Spreading: a throughput-aware queue estimate for every attraction, a stable per-guest preference per instance, hysteresis. |
| 4 | Queue patience as a new rule: expected wait against a trait-scaled patience; leaving gives a thought and a small happiness cost. |
| 5 | Planning needs: a shop or toilet first when a meter will cross its threshold, a detour bonus, no high-excitement ride right after eating. |
| 6 | Clean and quiet paths: wander goals weighted by litter and crowding, with thoughts. |
| 7 | Tuning by data mods (optional). |
| 8 | RCT3 groups and families (only if wanted). |

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

To compare two runs: `--guest-compare baseline.json candidate.json [--guest-targets metric,metric] [--guest-compare-k 3]` prints every metric's change in standard errors with a verdict
(`improved`, `regressed`, `changed`, `same`, `undefined`) and exits 1 on a regression, a named target that did not improve, or a non-zero `queue.superseded`.

**Why an extended scale.** A day is about 343 ticks, and a guest's exit counter runs about 120 s (about 21 days). At the plan's 2 + 10 days the scenario sees 3 to 8 queue attempts and
no departures per seed, far too little for a spread to mean anything. The baseline and the thresholds below therefore use the extended scale (20 warm-up + 60 measured days); the plan
scale is recorded for completeness.

## Baseline

The runs go through the game's turn order, not plain `Tick` loops: the tick that starts a park turn runs the substep's ride scripts and then the turn's thing sweep over the thing pool newest first (the guests, the attractions' admission), the turn's destroys and the arrival update, as `Level.AdvanceSimulation` and `DeterminismTests.SyntheticPark.Run` do (`BeginSweepTick`, `SweepGuest`, `FinishSweep`, `UpdateTurnEndArrivals`). `ParkScenario.Run` and the level parks (`M3GateOptions.SweptTurns`, which only the report sets, so the gate keeps its own loop) both use it. The scenario has no economy, so its turns are the guests' own; the level parks' economy turns are aligned to them. Measured on plain loops first, the baseline differed slightly (for example the scenario's guest-time happiness was 48.8 and its boarded share 55.1%).

Original policy, release build, 8 seeds per scenario. Reproduce with `--guest-scale extended`. The same seeds reproduce these numbers exactly; σ is the sample standard deviation over
the 8 seeds, so it is the seed-to-seed variation a rule has to beat.

### park-scenario, extended scale (20 + 60 days)

| Metric (key) | Mean | σ | Min | Max | Band (3 SE) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Admissions in the window (`admissions`) | 63.875 | 0.354 | 63 | 64 | ±0.958 |
| Queue attempts (`queue.attempts`) | 326 | 25.315 | 302 | 380 | ±37.973 |
| Boarded / attempts (`queue.boarded.rate`) | 0.56 | 0.017 | 0.541 | 0.585 | ±0.026 |
| Abandoned / attempts (`queue.abandoned.rate`) | 0.021 | 0.005 | 0.015 | 0.026 | ±0.007 |
| Rejected / attempts (`queue.rejected.rate`) | 0.392 | 0.016 | 0.366 | 0.409 | ±0.025 |
| Unfinished / attempts (`queue.unfinished.rate`) | 0.026 | 0.004 | 0.018 | 0.03 | ±0.006 |
| Wait p50, turns (boarded only) (`queue.wait.p50`) | 14.375 | 2.264 | 11 | 18 | ±3.396 |
| Wait p90 (boarded only) (`queue.wait.p90`) | 79.75 | 14.069 | 63 | 97 | ±21.103 |
| Wait p99 (boarded only) (`queue.wait.p99`) | 119.625 | 10.663 | 108 | 142 | ±15.994 |
| Wait p90, boarded + unfinished ages (`queue.wait.p90.censored`) | 79.125 | 13.282 | 65 | 97 | ±19.923 |
| Wait p99, boarded + unfinished ages (`queue.wait.p99.censored`) | 119.5 | 10.69 | 108 | 142 | ±16.036 |
| Superseded attempts (must be 0) (`queue.superseded`) | 0 | 0 | 0 | 0 | ±0.75 |
| Happiness, guest-time weighted (`happiness.guestTime`) | 48.528 | 2.69 | 44.093 | 51.398 | ±4.035 |
| Happiness of the guests still inside (`happiness.survivors`) | 47.752 | 4.491 | 43.553 | 55.658 | ±6.737 |
| Departures per admission (`departures.perAdmission`) | 0.562 | 0.035 | 0.516 | 0.609 | ±0.052 |
| Happiness-byte-0 departures per admission (`departures.happinessZero.perAdmission`) | 0.481 | 0.035 | 0.422 | 0.547 | ±0.052 |
| Exit-counter-0 departures per admission (`departures.exitCounterZero.perAdmission`) | 0.082 | 0.024 | 0.047 | 0.111 | ±0.035 |
| Park-closed departures per admission (`departures.parkClosed.perAdmission`) | 0 | 0 | 0 | 0 | ±0.003 |
| Use Gini over the rides (`use.giniRides`) | 0.197 | 0.014 | 0.184 | 0.227 | ±0.021 |
| Use Gini over the rides, per capacity (`use.giniRidesPerCapacity`) | 0.197 | 0.014 | 0.184 | 0.227 | ±0.021 |
| Use Gini over all attractions (`use.giniAll`) | 0.297 | 0.021 | 0.264 | 0.329 | ±0.032 |
| Revenue per admitted guest (flow ratio) (`revenue.perAdmittedGuest`) | 79.144 | 8.029 | 64.219 | 90.156 | ±12.044 |
| Guests with no choice for over 50 turns (`guests.stuck`) | 0.625 | 0.744 | 0 | 2 | ±1.116 |
| Guests in the park (mean) (`guests.meanInPark`) | 31.944 | 2.171 | 29.829 | 36.334 | ±3.257 |

Outcome mix over the 8 seeds: rejected `Full` 79, `Price` 711, `TooBoring` 95, `TooScary` 138; abandoned `Unhappy` 55. About 56% of the attempts board, 39% are rejected (most of them the dear shop's price at the stand point), 2% abandon and 3% are unfinished when the window closes (the oldest has waited 63 to 98 turns). About 0.6 guests depart per admission in the window, mostly by the happiness byte reaching 0 (the exit counter fires only for a guest that is in the right state when it passes 0, see `GuestLifecycle`). Guests wait a median of 14 turns and a p99 of 120 turns; no wait is capped, because no queue time limit is reachable in tp.exe. Here the censoring is small (the unfinished attempts are young); in the level parks it is not.

### Level parks, extended scale (mean ± σ over 8 seeds)

| Metric | Jungle | Halloween | Fantasy | Space |
| --- | ---: | ---: | ---: | ---: |
| Queue attempts | 831 ± 21.159 | 158 ± 13.342 | 379.375 ± 41.255 | 534 ± 23.845 |
| Boarded / attempts | 0.219 ± 0.008 | 0.712 ± 0.04 | 0.433 ± 0.019 | 0.398 ± 0.018 |
| Abandoned / attempts | 0.011 ± 0.003 | 0.011 ± 0.009 | 0.034 ± 0.007 | 0.04 ± 0.008 |
| Rejected / attempts | 0.736 ± 0.009 | 0.246 ± 0.047 | 0.431 ± 0.034 | 0.488 ± 0.024 |
| Wait p50 (turns, boarded) | 39.875 ± 1.246 | 18.25 ± 2.866 | 33.875 ± 2.1 | 41.375 ± 0.744 |
| Wait p90 (boarded) | 563.25 ± 26.37 | 165.5 ± 3.891 | 559.25 ± 62.858 | 493.625 ± 25.551 |
| Wait p99 (boarded) | 653 ± 16.336 | 177.25 ± 1.035 | 1048 ± 31.2 | 538 ± 29.301 |
| Wait p90, boarded + unfinished ages | 555.625 ± 27.944 | 164.625 ± 3.662 | 787.25 ± 31.153 | 479.125 ± 34.395 |
| Wait p99, boarded + unfinished ages | 647.125 ± 14.277 | 177.25 ± 1.035 | 1113 ± 52.544 | 538 ± 29.301 |
| Happiness, guest-time weighted | 52.837 ± 0.767 | 38.069 ± 0.688 | 48.73 ± 0.617 | 48.83 ± 1.09 |
| Departures per admission | 0.27 ± 0.028 | 0.494 ± 0.065 | 0.329 ± 0.034 | 0.344 ± 0.028 |
| Happiness-0 departures per admission | 0.172 ± 0.011 | 0.328 ± 0.061 | 0.237 ± 0.029 | 0.276 ± 0.021 |
| Exit-counter-0 departures per admission | 0.1 ± 0.028 | 0.166 ± 0.04 | 0.093 ± 0.031 | 0.07 ± 0.025 |
| Revenue per admitted guest | 43.621 ± 0.39 | 60.466 ± 2.388 | 52.62 ± 1.5 | 43.065 ± 0.314 |
| Guests with no choice > 50 turns | 69.875 ± 5.055 | 58.75 ± 1.581 | 71.625 ± 6.346 | 64.5 ± 4.14 |
| Guests in the park (mean) | 103.713 ± 1.539 | 36.719 ± 1.01 | 85.072 ± 4.792 | 98.834 ± 1.972 |

Outcome mix (all seeds): Jungle rejected `Full` 1001, `QueueLimit` 3891, abandoned `NeedToilet` 1, `Unhappy` 75; Halloween rejected `Full` 315, abandoned `Unhappy` 13; Fantasy rejected `Full` 1317, abandoned `NeedToilet` 4, `Unhappy` 99; Space rejected `Full` 804, `QueueLimit` 1283, abandoned `Unhappy` 169. With a single ride a park saturates: most attempts are rejected, waits reach hundreds of turns and a large share of the guests find no choice for a long time. The waits are right-censored: in Fantasy 10% of the attempts are still queued at the end, the oldest for 1,074 to 1,193 turns of a 1,380-turn window, so its boarded p99 (1,048) and even its p90 (559 against a censored 787) understate the waiting; use the censored rows. That is the headroom the later phases are measured against.

No ride is closed or broken in these runs. (An earlier version of this report counted `RideClosed` rejections that were the gate stopping its objects after the last tick, which turns the queues away; the window now closes before the teardown.)

### park-scenario, plan scale (2 + 10 days)

| Metric | Mean | σ |
| --- | ---: | ---: |
| Queue attempts | 5.75 | 0.886 |
| Boarded / attempts | 0.584 | 0.237 |
| Rejected / attempts | 0.315 | 0.206 |
| Wait p90 (turns) | 17.625 | 16.035 |
| Happiness, guest-time weighted | 49.351 | 2.784 |
| Happiness-0 departures per admission | 0 | 0 |
| Use Gini over the rides | 0.55 | 0.146 |
| Revenue per admitted guest | 20.469 | 1.326 |

Too few events for thresholds: `queue.attempts` is about 5 and there are no departures.

### Cost of the instrumentation

CPU and allocation per tick over the extended window (release build, Apple silicon, machine under load, so about ±20% noise): observer off about 1.0 µs and 567 B per tick (the
production path: one null test per event), observer on 1.0 µs and the same 567 B; the two sides are indistinguishable. The default path was also compared with the commit before
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
