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

Re-measured after the substep scheduler and swept park turn (#312) and the walk changes arrived on main: every number below is unchanged, because the scenario and the level parks are driven by plain `GuestSimulation.Tick` loops, not the host's thing sweep. The report does not exercise the sweep; the pinned fixed runs in `DeterminismTests` do, and they hold with the observer on.

Original policy, release build, 8 seeds per scenario. Reproduce with `--guest-scale extended`. The same seeds reproduce these numbers exactly; σ is the sample standard deviation over
the 8 seeds, so it is the seed-to-seed variation a rule has to beat.

### park-scenario, extended scale (20 + 60 days)

| Metric (key) | Mean | σ | Min | Max | 2σ band |
| --- | ---: | ---: | ---: | ---: | ---: |
| Admissions in the window (`admissions`) | 64 | 0 | 64 | 64 | ±1.28 |
| Queue attempts (`queue.attempts`) | 341 | 19.676 | 300 | 363 | ±39.352 |
| Boarded / attempts (`queue.boarded.rate`) | 0.551 | 0.027 | 0.515 | 0.584 | ±0.055 |
| Abandoned / attempts (`queue.abandoned.rate`) | 0.023 | 0.005 | 0.015 | 0.028 | ±0.01 |
| Rejected / attempts (`queue.rejected.rate`) | 0.405 | 0.025 | 0.376 | 0.441 | ±0.051 |
| Unfinished / attempts (`queue.unfinished.rate`) | 0.021 | 0.005 | 0.017 | 0.03 | ±0.01 |
| Wait p50, turns (boarded only) (`queue.wait.p50`) | 16.625 | 2.774 | 12 | 20 | ±5.548 |
| Wait p90 (`queue.wait.p90`) | 82.5 | 8.992 | 67 | 94 | ±17.984 |
| Wait p99 (`queue.wait.p99`) | 116.5 | 3.295 | 112 | 121 | ±6.59 |
| Happiness, guest-time weighted (`happiness.guestTime`) | 48.84 | 2.065 | 44.553 | 51.403 | ±4.13 |
| Happiness of the guests still inside (`happiness.survivors`) | 50.738 | 2.436 | 47.649 | 53.976 | ±4.873 |
| Departures per admission (`departures.perAdmission`) | 0.588 | 0.045 | 0.547 | 0.672 | ±0.09 |
| Departures: happiness byte 0 (`departures.happinessZero`) | 33.25 | 3.808 | 27 | 38 | ±7.616 |
| Departures: exit counter 0 (`departures.exitCounterZero`) | 4.5 | 2.619 | 1 | 9 | ±5.237 |
| Departures: park closed (`departures.parkClosed`) | 0 | 0 | 0 | 0 | ±0 |
| Use Gini over the rides (`use.giniRides`) | 0.223 | 0.031 | 0.185 | 0.289 | ±0.063 |
| Use Gini over all attractions (`use.giniAll`) | 0.311 | 0.03 | 0.255 | 0.345 | ±0.06 |
| Revenue per admitted guest (`revenue.perAdmittedGuest`) | 84.551 | 7.87 | 73.438 | 97.5 | ±15.74 |
| Guests with no choice for over 50 turns (`guests.stuck`) | 1.625 | 1.188 | 0 | 3 | ±2.375 |
| Guests in the park (mean) (`guests.meanInPark`) | 32.831 | 1.047 | 30.511 | 33.678 | ±2.094 |

Outcome mix over the 8 seeds: rejected `Full` 101, `Price` 729, `TooBoring` 94, `TooScary` 182; abandoned `Unhappy` 61, `NeedToilet` 2. About 55% of the attempts board, 40% are rejected
(most of them the dear shop's price at the stand point), 2% abandon and 2% are unfinished when the window closes. About 0.6 guests depart per admission in the window, mostly by the
happiness byte reaching 0 (the exit counter fires only for a guest that is in the right state when it passes 0, see `GuestLifecycle`). Guests wait a median of 17 turns and a p99 of 117
turns; no wait is capped, because no queue time limit is reachable in tp.exe.

### Level parks, extended scale (mean ± σ over 8 seeds)

| Metric | Jungle | Halloween | Fantasy | Space |
| --- | ---: | ---: | ---: | ---: |
| Queue attempts | 870.125 ± 38.387 | 159.75 ± 10.138 | 386.125 ± 31.434 | 576.875 ± 34.943 |
| Boarded / attempts | 0.207 ± 0.009 | 0.696 ± 0.042 | 0.435 ± 0.027 | 0.369 ± 0.019 |
| Abandoned / attempts | 0.015 ± 0.003 | 0.01 ± 0.008 | 0.034 ± 0.005 | 0.049 ± 0.008 |
| Rejected / attempts | 0.778 ± 0.009 | 0.293 ± 0.047 | 0.531 ± 0.022 | 0.583 ± 0.024 |
| Wait p50 (turns) | 40.375 ± 0.518 | 17.125 ± 2.295 | 34.875 ± 1.126 | 41.875 ± 1.246 |
| Wait p90 | 540.5 ± 34.949 | 165.75 ± 2.605 | 599.25 ± 86.373 | 485 ± 14.432 |
| Wait p99 | 649.5 ± 38.781 | 177 ± 2.828 | 1096 ± 51.399 | 525.75 ± 16.585 |
| Happiness, guest-time weighted | 52.436 ± 1.29 | 38.063 ± 1.118 | 49.418 ± 0.581 | 47.596 ± 0.709 |
| Departures per admission | 0.286 ± 0.011 | 0.502 ± 0.059 | 0.364 ± 0.016 | 0.365 ± 0.027 |
| Departures: happiness byte 0 | 29.125 ± 3.682 | 19.875 ± 3.944 | 23.875 ± 2.232 | 48 ± 4 |
| Departures: exit counter 0 | 17.25 ± 3.37 | 12.25 ± 2.605 | 11.625 ± 3.998 | 11.5 ± 1.69 |
| Revenue per admitted guest | 42.859 ± 0.111 | 59.615 ± 1.955 | 57.471 ± 2.797 | 42.344 ± 0.498 |
| Guests with no choice > 50 turns | 68.75 ± 4.367 | 58 ± 3.251 | 70.625 ± 5.829 | 61.375 ± 5.181 |
| Guests in the park (mean) | 107.826 ± 1.111 | 35.467 ± 1.056 | 86.894 ± 4.092 | 102.582 ± 0.459 |

Outcome mix (all seeds): Jungle rejected `Full` 1106, `QueueLimit` 4088, `RideClosed` 225, abandoned `Unhappy` 101, `NeedToilet` 2; Halloween `Full` 342, `RideClosed` 36, `Unhappy` 13;
Fantasy `Full` 1353, `RideClosed` 291, `Unhappy` 105; Space `Full` 866, `QueueLimit` 1511, `RideClosed` 318, `Unhappy` 223. With a single ride a park saturates: most attempts are
rejected, waits reach hundreds of turns and a large share of the guests find no choice for a long time. That is the headroom the later phases are measured against.

### park-scenario, plan scale (2 + 10 days)

| Metric | Mean | σ |
| --- | ---: | ---: |
| Queue attempts | 5.125 | 1.727 |
| Boarded / attempts | 0.54 | 0.242 |
| Rejected / attempts | 0.255 | 0.163 |
| Wait p90 (turns) | 12.5 | 9.134 |
| Happiness, guest-time weighted | 47.964 | 2.472 |
| Departures: happiness byte 0 | 0 | 0 |
| Use Gini over the rides | 0.448 | 0.317 |
| Revenue per admitted guest | 29.688 | 10.994 |

Too few events for thresholds: `queue.attempts` is about 5 and there are no departures.

### Cost of the instrumentation

CPU and allocation per tick over the extended window (release build, Apple silicon, machine under heavy load, so about ±20% noise): observer off about 1.5 µs and 586 B per tick (the
production path: one null test per event), observer on 1.3 to 1.9 µs and the same 587 B; the two sides are indistinguishable. The default path was also compared with the commit before
this change on a 41,160-tick scenario run (the best of eight runs in each of five alternating process pairs): 1.182 µs per tick before, 1.194 µs after (+1%, inside the noise), and the
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
