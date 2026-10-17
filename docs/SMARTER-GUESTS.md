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
  `QueueRemoved`, `NoPlace`, `ReliefWornOut`, `TrackRefuses`, `AttractionRemoved`) or *unfinished* (still queued when the window closes, or the guest left the park). An attempt
  counts when it starts inside the window. Waits are reported over boarded attempts only, next to the abandon and reject rates.
- **Departures** with the real predicate (`GuestLifecycle.ShouldLeave`): happiness byte 0, exit counter 0 or park closure, evaluated as the guest decides to leave. The booth
  (`BoothRefused`) and a guard (`Guard`) send guests home without that test. The flags overlap (a guest can meet two predicates), so the counts need not add up to the total.
- **Happiness per admission cohort** (the in-game day of admission; `carryOver` for guests admitted before the window) and **weighted by guest time** over every admitted
  guest-second, next to the happiness of the guests still inside at the end (the survivors).
- **Use per attraction instance** (boardings), with the Gini coefficient over the rides and over all attractions. The Gini is 0 for the level parks, which have one ride.
- **Revenue per admitted guest** (entrance fees and spending), admissions, and **stuck guests**: guests whose major decisions found nothing for more than 50 park turns.

**Scenarios.** `ParkScenario` (`World/Guests/Measurement/ParkScenario.cs`) is a headless park with no game data and no economy: a 40-cell street from the gate with three
branches carrying two identical rides, a thrill ride (excitement 95, too scary for the calm guest type) and a calm ride (excitement 20, too boring for the thrill-seeking type), a
cheap shop (price 30) and a dear shop (price 220, which most guests refuse at the stand point), and a toilet at the far end of the street (a long walk; the rides and shops are a short one).
Every attraction serves one guest at a time through the corpus shop protocol with its own hold time. With game data, `--guest-report` also runs the level parks of the four themes
through the M3 gate's park builder ([M3-GATE.md](M3-GATE.md)): one ride, a shop and a toilet each. The level parks use an arrival timer of 20 (`--guest-arrivals`), because the
themes' 150 would bring no group within a few days. `SyntheticPark` in `DeterminismTests` and its pin are unchanged.

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

**Why an extended scale.** A day is about 343 ticks, and a guest's exit counter runs about 120 s (about 21 days). At the plan's 2 + 10 days the scenario sees 3 to 8 queue attempts and
no departures per seed, far too little for a spread to mean anything. The baseline and the thresholds below therefore use the extended scale (20 warm-up + 60 measured days); the plan
scale is recorded for completeness.

## Baseline

The runs go through the game's turn order, not plain `Tick` loops: the tick that starts a park turn runs the substep's ride scripts and then the turn's thing sweep over the thing pool newest first (the guests, the attractions' admission), the turn's destroys and the arrival update, as `Level.AdvanceSimulation` and `DeterminismTests.SyntheticPark.Run` do (`BeginSweepTick`, `SweepGuest`, `FinishSweep`, `UpdateTurnEndArrivals`). `ParkScenario.Run` and the level parks (`M3GateOptions.SweptTurns`, which only the report sets, so the gate keeps its own loop) both use it. The scenario has no economy, so its turns are the guests' own; the level parks' economy turns are aligned to them. Measured on plain loops first, the baseline differed slightly (for example the scenario's guest-time happiness was 48.8 and its boarded share 55.1%).

Original policy, release build, 8 seeds per scenario. Reproduce with `--guest-scale extended`. The same seeds reproduce these numbers exactly; σ is the sample standard deviation over
the 8 seeds, so it is the seed-to-seed variation a rule has to beat.

### park-scenario, extended scale (20 + 60 days)

| Metric (key) | Mean | σ | Min | Max | 2σ band |
| --- | ---: | ---: | ---: | ---: | ---: |
| Admissions in the window (`admissions`) | 63.75 | 0.463 | 63 | 64 | ±1.275 |
| Queue attempts (`queue.attempts`) | 328.75 | 17.645 | 297 | 353 | ±35.291 |
| Boarded / attempts (`queue.boarded.rate`) | 0.557 | 0.041 | 0.5 | 0.616 | ±0.082 |
| Abandoned / attempts (`queue.abandoned.rate`) | 0.027 | 0.008 | 0.019 | 0.039 | ±0.017 |
| Rejected / attempts (`queue.rejected.rate`) | 0.396 | 0.033 | 0.343 | 0.439 | ±0.067 |
| Unfinished / attempts (`queue.unfinished.rate`) | 0.019 | 0.006 | 0.009 | 0.027 | ±0.011 |
| Wait p50, turns (boarded only) (`queue.wait.p50`) | 16.125 | 4.357 | 11 | 24 | ±8.714 |
| Wait p90 (`queue.wait.p90`) | 83.75 | 10.539 | 66 | 97 | ±21.078 |
| Wait p99 (`queue.wait.p99`) | 121 | 8.928 | 112 | 142 | ±17.857 |
| Happiness, guest-time weighted (`happiness.guestTime`) | 47.825 | 3.338 | 42.877 | 51.099 | ±6.676 |
| Happiness of the guests still inside (`happiness.survivors`) | 46.513 | 6.548 | 38.957 | 55.658 | ±13.095 |
| Departures per admission (`departures.perAdmission`) | 0.576 | 0.042 | 0.5 | 0.609 | ±0.084 |
| Departures: happiness byte 0 (`departures.happinessZero`) | 31.75 | 4.234 | 26 | 37 | ±8.468 |
| Departures: exit counter 0 (`departures.exitCounterZero`) | 5.125 | 2.949 | 2 | 10 | ±5.898 |
| Departures: park closed (`departures.parkClosed`) | 0 | 0 | 0 | 0 | ±0 |
| Use Gini over the rides (`use.giniRides`) | 0.219 | 0.037 | 0.158 | 0.259 | ±0.074 |
| Use Gini over all attractions (`use.giniAll`) | 0.309 | 0.028 | 0.264 | 0.336 | ±0.056 |
| Revenue per admitted guest (`revenue.perAdmittedGuest`) | 81.962 | 12.856 | 67.656 | 97.969 | ±25.713 |
| Guests with no choice for over 50 turns (`guests.stuck`) | 0.625 | 0.916 | 0 | 2 | ±1.832 |
| Guests in the park (mean) (`guests.meanInPark`) | 32.235 | 1.939 | 30.007 | 35.037 | ±3.877 |

Outcome mix over the 8 seeds: rejected `Full` 66, `Price` 718, `TooBoring` 80, `TooScary` 179; abandoned `NeedToilet` 3, `Unhappy` 69. About 56% of the attempts board, 40% are rejected (most of them the dear shop's price at the stand point), 3% abandon and 2% are unfinished when the window closes. About 0.6 guests depart per admission in the window, mostly by the happiness byte reaching 0 (the exit counter fires only for a guest that is in the right state when it passes 0, see `GuestLifecycle`). Guests wait a median of 16 turns and a p99 of 121 turns; no wait is capped, because no queue time limit is reachable in tp.exe.

### Level parks, extended scale (mean ± σ over 8 seeds)

| Metric | Jungle | Halloween | Fantasy | Space |
| --- | ---: | ---: | ---: | ---: |
| Queue attempts | 831 ± 21.159 | 158 ± 13.342 | 379.375 ± 41.255 | 534 ± 23.845 |
| Boarded / attempts | 0.219 ± 0.008 | 0.712 ± 0.04 | 0.433 ± 0.019 | 0.398 ± 0.018 |
| Abandoned / attempts | 0.011 ± 0.003 | 0.011 ± 0.009 | 0.034 ± 0.007 | 0.04 ± 0.008 |
| Rejected / attempts | 0.769 ± 0.008 | 0.278 ± 0.046 | 0.533 ± 0.023 | 0.563 ± 0.021 |
| Wait p50 (turns) | 39.875 ± 1.246 | 18.25 ± 2.866 | 33.875 ± 2.1 | 41.375 ± 0.744 |
| Wait p90 | 563.25 ± 26.37 | 165.5 ± 3.891 | 559.25 ± 62.858 | 493.625 ± 25.551 |
| Wait p99 | 653 ± 16.336 | 177.25 ± 1.035 | 1048 ± 31.2 | 538 ± 29.301 |
| Happiness, guest-time weighted | 52.837 ± 0.767 | 38.07 ± 0.688 | 48.73 ± 0.617 | 48.83 ± 1.09 |
| Departures per admission | 0.27 ± 0.028 | 0.494 ± 0.065 | 0.329 ± 0.034 | 0.344 ± 0.028 |
| Departures: happiness byte 0 | 26.75 ± 1.753 | 20.75 ± 3.694 | 24.75 ± 1.669 | 42.625 ± 3.292 |
| Departures: exit counter 0 | 15.5 ± 4.276 | 10.5 ± 2.563 | 9.75 ± 3.284 | 10.75 ± 3.808 |
| Revenue per admitted guest | 43.621 ± 0.39 | 60.466 ± 2.388 | 52.62 ± 1.5 | 43.065 ± 0.314 |
| Guests with no choice > 50 turns | 69.875 ± 5.055 | 58.75 ± 1.581 | 71.625 ± 6.346 | 64.5 ± 4.14 |
| Guests in the park (mean) | 103.71 ± 1.539 | 36.718 ± 1.01 | 85.07 ± 4.791 | 98.831 ± 1.972 |

Outcome mix (all seeds): Jungle rejected `Full` 1001, `QueueLimit` 3891, `RideClosed` 222, abandoned `NeedToilet` 1, `Unhappy` 75; Halloween rejected `Full` 315, `RideClosed` 40, abandoned `Unhappy` 13; Fantasy rejected `Full` 1317, `RideClosed` 307, abandoned `NeedToilet` 4, `Unhappy` 99; Space rejected `Full` 804, `QueueLimit` 1283, `RideClosed` 320, abandoned `Unhappy` 169. With a single ride a park saturates: most attempts are
rejected, waits reach hundreds of turns and a large share of the guests find no choice for a long time. That is the headroom the later phases are measured against.

### park-scenario, plan scale (2 + 10 days)

| Metric | Mean | σ |
| --- | ---: | ---: |
| Queue attempts | 6 | 0.756 |
| Boarded / attempts | 0.543 | 0.239 |
| Rejected / attempts | 0.317 | 0.244 |
| Wait p90 (turns) | 18.125 | 17.217 |
| Happiness, guest-time weighted | 49.234 | 2.753 |
| Departures: happiness byte 0 | 0 | 0 |
| Use Gini over the rides | 0.562 | 0.086 |
| Revenue per admitted guest | 20.469 | 1.326 |

Too few events for thresholds: `queue.attempts` is about 5 and there are no departures.

### Cost of the instrumentation

CPU and allocation per tick over the extended window (release build, Apple silicon, machine under heavy load, so about ±20% noise): observer off about 1.4 µs and 579 B per tick (the
production path: one null test per event), observer on 1.3 to 1.9 µs and the same 580 B; the two sides are indistinguishable. The default path was also compared with the commit before
this change on a 41,160-tick run of the earlier plain-tick scenario loop (the best of eight runs in each of five alternating process pairs): 1.182 µs per tick before, 1.194 µs after (+1%, inside the noise), and the
guests' state hash is identical. The allocation per tick is the simulation's own; the observer adds none on the default path. Compare CPU only as a ratio on one machine, from the
`performance` block of one report.

## Acceptance thresholds

Defined from the extended baseline above. A later phase re-runs `--guest-report --guest-scale extended` with its extension on and compares scenario by scenario against the original policy
in the same environment.

- **Noise unit.** σ(scenario, metric) is the sample standard deviation over the 8 seeds in the tables above. When it is smaller than 1% of the mean (the fixed arrival timer makes
  `admissions` constant), 1% of the mean is used instead, so a spread of 0 cannot make noise significant. The `2σ band` column is twice this unit.
- **A rule improves its target metric** when the mean over the 8 seeds moves in the wanted direction by more than 2σ on every scenario where the metric is defined (beyond 2σ of the seed
  spread on every scenario). The target metrics are:

  | Phase | Target metric (wanted direction) |
  | --- | --- |
  | 2 value for money | Price rejections as a share of attempts (down), `happiness.guestTime` (up) |
  | 3 spreading | `use.giniRides` (down) and `queue.wait.p90` (down), on `park-scenario` only (the level parks have one ride) |
  | 4 queue patience | `queue.wait.p99` (down) and `queue.wait.p90` (down) |
  | 5 planning needs | `departures.happinessZero` (down) and `happiness.guestTime` (up) |
  | 6 clean and quiet paths | `happiness.guestTime` (up) |

- **No regression elsewhere** (beyond 1σ): every other metric with a direction must not move the wrong way by more than 1σ. Up is good for `queue.boarded.rate`,
  `happiness.guestTime`, `happiness.survivors` and `revenue.perAdmittedGuest`; down is good for `queue.rejected.rate`, `queue.wait.p50`, `queue.wait.p90`, `queue.wait.p99`,
  `departures.perAdmission`, `use.giniRides` and `guests.stuck`. A rule may raise `queue.abandoned.rate` only when its own target is the patience rule.
- **No pathology:** no oscillation (a guest's target changing back and forth), no empty park (`guests.meanInPark` not below the baseline mean minus 1σ) and no stuck guests
  (`guests.stuck` not above the baseline mean plus 2σ).
- **CPU per tick** at most 10% above the original policy's in the same report; allocation per tick is reported.
- **Revenue and golden-ticket feasibility** are reported against the default (`revenue.perAdmittedGuest` and the level parks' rows), not gated, so the game does not become trivially easy or hard.

## Tests

`GuestMeasurementTests` covers: every queue attempt ends as exactly one outcome (attempts = boarded + abandoned + rejected + unfinished, also per attraction); waits cover boarded attempts
only; turning the observer on leaves the pinned fixed run's hash (`DeterminismTests.FixedRunHash`) and a scenario run's hash unchanged; `ParkScenario` is deterministic and its
fixed-run hash is pinned (a change moves this baseline); the report JSON is stable for fixed seeds. The determinism guard scans the simulation folders, so `Stopwatch` lives in
`Client/GuestReport.cs`, not beside the simulation.
