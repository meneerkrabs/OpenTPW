## Passive guest measurement and a baseline for smarter guests (October 17, 2026)

Phase 0a of the smarter-guests plan ([SMARTER-GUESTS.md](../SMARTER-GUESTS.md)) measures the guests before any rule changes them. Nothing about the simulation changes: the pinned
determinism hash, every other pin and the saves are the same, and a run with the new observer ends in the same state hash as one without.

- **Instrumentation.** `GuestSimulation.Observer` (`IGuestObserver`, null by default, one null test per event) reports every queue attempt and how it ends (boarded, abandoned,
  rejected with a reason, or unfinished), the leaving predicate of each departure (happiness byte 0, exit counter 0, park closed, plus the booth and the guards), admissions, spending
  and the major decisions that found nothing. `GuestMetrics` collects them with happiness per admission cohort and weighted by guest time, use per attraction instance with a Gini,
  revenue per admitted guest and stuck guests. Compared with the commit before, the default path costs the same (1.182 vs 1.194 µs per tick, identical hash).
- **ParkScenario.** A headless park without game data: two identical rides, a thrill and a calm ride, a cheap and a dear shop, and a toilet at the end of a long street.
- **`--guest-report`.** Eight seeds of the scenario plus, with game data, the four level parks through the M3 gate's builder, a warm-up and a measured window in in-game days, a table and a
  stable JSON report (`--guest-scale plan|extended`). The plan's 2 + 10 days see 3 to 8 queue attempts and no departures per seed (a day is 343 ticks and an exit counter runs about 21 days), so the
  baseline and its acceptance thresholds (2σ of the seed spread to improve a target, 1σ to regress elsewhere) use the extended 20 + 60 days. In the baseline about 55% of the scenario's
  queue attempts board, 40% are rejected (mostly the dear shop's price) and the level parks' single ride saturates with waits of hundreds of turns.

After merging the substep scheduler (#312): the observer hooks also cover the swept turn (`RemoveGone`, `FinishSweep`); `DeterminismTests.FixedRunHash` (now the composed pin) is unchanged with the observer on; `ScenarioFixedRunHash` and the whole baseline stayed the same, because the scenario and level-park runs use plain `Tick` loops that main's changes do not reach, so no re-pin was needed.

Verified with the guest, queue and determinism tests and the report run on the four themes.
