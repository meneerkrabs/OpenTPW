# Smarter guests: plan, measurement and baseline

The goal is an opt-in official extension that makes park visitors decide better, borrowing ideas (not code) from RollerCoaster Tycoon 1, 2 and 3. The faithful tp.exe
simulation ([GUESTS.md](GUESTS.md)) stays the default: with the extension off, behaviour, determinism pins and saves do not change. OpenRCT2 is GPLv3, so only concepts are
taken: no code, no data tables, no constants. Every phase is one pull request. This page holds the overview, the measurement that is built first, and its baseline.

## Architecture in short (as built in phase 0b)

- **Two extensions** (`OfficialExtensions`, both `DefaultEnabled: false`, version 1.0.0). `opentpw.guest-insights` is UI only (`AffectsSimulation: false`): refusal-reason counters (phase 1) and, later, a
  complaints summary; it is never recorded in saves. `opentpw.smarter-guests` is `AffectsSimulation: true`; the mod set is recorded in park saves (`ParkResumeMods`
  refuses a mismatched set), and the extension version is the algorithm identity: every behaviour-changing PR bumps it, and a park of another version is refused with a clear message
  (`needs opentpw.smarter-guests 1.1.0, not 1.0.0`). `FeatureGates.SmarterGuests` and `FeatureGates.GuestInsights` read them. **Smarter guests cannot be switched on by a player yet** (`IOpenTpwExtension.PlayerSelectable` is false; guest insights became selectable in phase 1): a no-op
  1.0.0 would lock saves to an extension that does nothing, and the next version bump would make them unloadable. They stay registered so tests and development code can use them
  (`OpenTpwExtensions.Use`); the Extensions and mods screen hides them, and a `mods.json` entry or `--enable-mod` for them is ignored with a log line. They become selectable with the first real rule
  (phase 2 for smarter guests, phase 1 for guest insights). Their names and descriptions exist in the nine interface languages for that day (the screen shows a selectable extension's, and a test pins it).
  **Version policy:** the extension's version is the algorithm identity. A park saved by another version is refused with a clear message (`needs opentpw.smarter-guests 1.1.0, not 1.0.0`) and stays in the
  load list marked as requiring it; a later release may offer loading an older version's park with the newer rules as an explicit "migrate" choice. Park sharing stays blocked while a simulation extension is
  active, as for every simulation mod, until packages record mods.
- **A pure decision layer** (`World/Guests/Decisions/`). `GuestSimulation` takes an `IGuestDecisions` at construction (null: the gate decides, read once, in the constructor and never again):
  `OriginalGuestDecisions`, a stateless pure extraction of the original, or `SmarterGuestDecisions`, which delegated every call to it in 1.0.0 and has had its first rules since 1.1.0 (phase 2) and its spreading rules since 1.2.0 (phase 3) and its queue patience rules since 1.3.0 (phase 4). A policy gets a `DecisionContext` (settings, park
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
| 2 | Value for money (built, version 1.1.0: [below](#phase-2-value-for-money)): price enters the shop and sideshow choice; a guest that price keeps from every stand goes home; the booth weighs the fee against the park; a price sensitivity trait. |
| 3 | Spreading (built, version 1.2.0: [below](#phase-3-spreading)): an expected-wait queue term for every attraction from a persisted, per-attraction throughput estimate; a stable per-guest preference per attraction instance; hysteresis between rides in the minor decision. |
| 4 | Queue patience (built, version 1.3.0: [below](#phase-4-queue-patience)): a guest does not join a queue whose expected wait exceeds its patience, and gives up after waiting too long, when another ride would be clearly quicker; leaving gives a thought and the original dismissal's happiness step. |
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

## Phase 2: Value for money

Version 1.1.0 of `opentpw.smarter-guests`. In TPW price plays no part in the choice: a guest walks to a shop or sideshow, queues, and is turned away at the stand point when the price is above its willingness to pay or its money
(`GuestSimulation.TooExpensive`, which also records the economy's price statistics). The original guest then loses happiness twice (the refusal and the dismissal), chooses again, and in the end leaves by the happiness byte. Rides cost nothing per use, so nothing below touches them.
All rules are `[EXT:smarter-guests-value]`, live only in `SmarterGuestDecisions` / `SmarterGuestsRules` and are gated by the extension; with it off the host does no extra work and every pin is unchanged.

**What the host adds (the policy stays pure).** A policy that returns `UsesValueForMoney` (the smarter one; script mods forward their base's answer) gets, for each shop or sideshow with a price above 0 in a candidate view: `Price` and `Willingness`
(`QueueJoinRules.WillingnessToPay` with this guest's meters, the same word the stand-point test uses, computed as a pure function: it records no statistic). The booth view gets `OpenAttractions`, the number of **rides and sideshows** that are open, not broken and
have a join cell (shops and toilets are services, not draws; a park with none open reads 0). The stand-point test is untouched and still records its statistic exactly once per test (a test runs the scenario with and without the facts asked for and compares the counts and the hash).
Two additive members of `IGuestDecisions` (with default implementations, so a policy that ignores them, including the original and every script policy, is unchanged) serve rule 2: `PriceTurnedAway` and `DecidePriceStall` (returning `PriceStallDecision`).

**1. Price in the choice.** `ScoreAttraction` returns the original score, except 0 for a priced shop or sideshow the guest expects to refuse, which is below the choice floor of 10 and so never chosen. The guest expects a refusal by the stand's own rule
(`price > willingness` or `price > money`, unsigned) with the willingness divided by its sensitivity (rule 4). The minor decision (a walking guest looking for something nearer) is the original's: a zero score can still tie the initial 0 on an odd park
turn. Overriding that (a zero never wins) was measured and made Halloween worse (guest-time happiness -4.6 SE, revenue per admitted guest -15 SE) by removing a quirk that happens to help there, so it is not part of the rule.

**2. A guest that price keeps from every stand goes home.** Rule 1 alone has a flaw an independent review found: where the original guest walks to the dear shop, is refused (two medium happiness steps) and soon leaves, the rule-1 guest finds *no choice*, which costs one small step, and then wanders and tries again
every ~30 turns. The review counted over 8 seeds: "can't decide" outcomes rose from 59 to 1,150, and in 81% of them a stand scoring at least 10 had been zeroed by rule 1. That loop is what `guests.stuck` counts (1.25 to 19.75), and the extra guests in the park are its symptom, not a longer healthy stay.
The host now flags a major decision as **price-emptied** when it chose nothing and the policy says (`PriceTurnedAway`) that it left out a stand (open, working, with room) that the original would have let the guest choose. It counts a run of such decisions per guest in `SmarterGuestsState.Stalls` (`PriceStalls`, keyed by birth id, reset when the guest chooses something, goes home or leaves), asks the policy (`DecidePriceStall`) and,
when the answer is "go home", walks the guest to the gate as the leaving test does (with the decision's own draw, state "leaving the park", reported as a leaving without a predicate) with thought 6, the original's "too expensive" thought (`Dissatisfied`, the thought of the stand refusal and of the booth's far-too-expensive verdict; no other original thought fits better). The shipped limit is `StallsBeforeGoingHome` = 1 (see the sweep below).
The runs are saved in the `SmarterGuests` section as `Stalls` (absent while there are none, birth-id order), checked strictly on load (birth ids the section holds, strictly ascending, 1 to 255, a run saturates at 255), and join `WorldStateHash` only while there is one. With a limit of 1 nothing stays counted in practice; the counter is exercised by a test policy with a limit of 3.
The earlier "too expensive" memory of a refused stand was **removed**: with rule 1 on, a refused guest has already lost happiness, so its willingness is below the price and the prediction (the same function as the stand-point test) already skips the stand; switching the memory off gave identical state hashes in every measured park, so it never acted.

**3. The booth.** The original verdict is the fee against `GP = points / (N + R % (N / 2 + 1)) + base` (the park rating is `ParkPoints`; `BoothVerdict`). The smarter booth keeps that, the host's draws and the original bands, and scales only the **rating part**
of the value (`GP - base`, the minimum entry fee being the base) by the park's variety: `100 + 3 x min(open rides and sideshows, 5)` percent, so the original value with none open, 109% with three and 115% from five on. A park with no rating, or a fee and value at the minimum, is the original's verdict.
It is never harsher than the original: a first version with `85 + 5 x min(open, 6)` percent was measured and made the level parks worse (one ride gave 90%, more expensive verdicts, guest-time happiness -9 to -15 SE in Jungle, Fantasy and Space), so the rule only rewards variety. The scenario has no park rating (its booth reads 0 points), so the report cannot exercise it: it is validated by unit tests (never harsher with more open rides, the original's verdict with none open, some verdicts move) and by a test on a scenario variant with a rating (`ParkScenarioOptions.ParkPoints`, not part of the report), which shows the booth sees the rating and the four rides and that the guests differ from the original's. In the level parks it changed no verdict (their runs are identical to the default's).

**4. Price sensitivity.** `DecisionContext.Trait( guest, 0 )` (a stateless hash of the park seed and the birth id, so stable for the guest's whole visit) gives a sensitivity of 90 to 110 percent (`90 + trait % 21`). A guest judges a stand by `willingness x 100 / sensitivity`: a
sensitive guest (110) expects to pay about 9% less than the stand would accept and skips borderline stands, a relaxed one (90) expects 11% more and tries them (and may be refused at the stand: the stand-point test stays the host's). The band is narrow on purpose.

**Constants** (all in `SmarterGuestsRules`, all OpenTPW's own): go home after 1 price-emptied decision; sensitivity 90 to 110; booth 100 + 3 x min(open rides and sideshows, 5) percent.

### Measured effect

`--guest-report --guest-scale extended --guest-extension smarter-guests` against the default, 8 seeds per scenario (the same seeds as the baseline above), `--guest-compare` with 3 standard errors, release build.

`park-scenario` (mean ± σ over the 8 seeds):

| Metric | Default | Smarter 1.1.0 | SE | Verdict |
| --- | ---: | ---: | ---: | --- |
| Queue attempts (`queue.attempts`) | 341.500 ± 22.916 | 337.875 ± 11.205 | -0.4 | same |
| Boarded / attempts (`queue.boarded.rate`) | 0.544 ± 0.020 | 0.717 ± 0.015 | 19.6 | improved |
| Rejected / attempts (`queue.rejected.rate`) | 0.410 ± 0.019 | 0.245 ± 0.015 | -18.8 | improved |
| Price rejections / attempts (`queue.rejected.price.rate`) | 0.280 ± 0.026 | 0.024 ± 0.007 | -26.7 | improved |
| Abandoned / attempts (`queue.abandoned.rate`) | 0.026 ± 0.008 | 0.009 ± 0.004 | -5.1 | changed |
| Wait p50, turns (`queue.wait.p50`) | 11.625 ± 2.066 | 17.000 ± 1.690 | 5.7 | regressed |
| Wait p90, censored (`queue.wait.p90.censored`) | 73.375 ± 7.090 | 101.000 ± 6.525 | 8.1 | regressed |
| Wait p99, censored (`queue.wait.p99.censored`) | 108.125 ± 4.643 | 123.875 ± 16.013 | 2.7 | same |
| Happiness, guest-time weighted (`happiness.guestTime`) | 49.007 ± 3.130 | 62.836 ± 1.114 | 11.8 | improved |
| Happiness of the guests still inside (`happiness.survivors`) | 48.217 ± 4.606 | 62.867 ± 2.922 | 7.6 | improved |
| Departures per admission (`departures.perAdmission`) | 0.599 ± 0.051 | 0.506 ± 0.064 | -3.2 | improved |
| Happiness-0 departures per admission (`departures.happinessZero.perAdmission`) | 0.528 ± 0.047 | 0.084 ± 0.014 | -25.5 | improved |
| Use Gini over the rides (`use.giniRides`) | 0.198 ± 0.037 | 0.132 ± 0.025 | -4.2 | improved |
| Revenue per admitted guest (`revenue.perAdmittedGuest`) | 82.062 ± 8.502 | 94.238 ± 12.138 | 2.3 | same |
| Admissions (`admissions`) | 63.875 ± 0.354 | 64.000 ± 0.000 | 0.4 | same |
| Guests with no choice for over 50 turns (`guests.stuck`) | 1.250 ± 1.035 | 5.250 ± 2.315 | 4.5 | regressed |
| Guests in the park (mean) (`guests.meanInPark`) | 32.840 ± 1.877 | 36.473 ± 1.262 | 4.5 | changed |

Price rejections fall from 761 to 65 over the eight seeds. The four level parks (Jungle, Halloween, Fantasy, Space) are **bit-identical** to the default: every seed ends in the same guest state hash and every metric is equal. They have no price rejections at all
(`rejected by reason` holds `Full` and `QueueLimit` only; their shops are priced so that nobody is turned away), so rules 1 and 2 have nothing to act on, and the booth rule moved no verdict there. `--guest-compare` therefore prints the level parks' phase targets as "did not improve (0.0)" and exits 1.

**Limit K, by measurement.** The same run for each limit (scenario; the level parks are identical to the default for every K; the K = 2, 3 and 5 rows were measured before the booth change, which the scenario's booth, with no rating, does not see):

| Limit K | Price share | Happiness (SE) | Revenue (SE) | Wait p90 censored (SE) | Wait p99 censored (SE) | Stuck guests (SE) | Guests in park |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| none (rule 1 alone) | 0.016 | 59.92 (+9.0) | 99.9 (+4.0) | 103.3 (+11.4) | 121.0 (+5.2) | 19.75 (+15.2) | 43.1 |
| 1 | 0.024 | 62.84 (11.8) | 94.24 (2.3) | 101.00 (8.1) | 123.88 (2.7) | 5.25 (4.5) | 36.5 |
| 2 | 0.015 | 62.63 (10.7) | 96.68 (3.5) | 104.00 (10.5) | 123.50 (4.0) | 13.75 (11.1) | 38.7 |
| 3 | 0.015 | 61.84 (10.6) | 99.20 (3.8) | 105.62 (11.1) | 126.25 (4.4) | 18.62 (12.5) | 41.0 |
| 5 | 0.020 | 61.02 (9.9) | 98.77 (3.5) | 103.00 (10.2) | 122.38 (3.5) | 20.50 (10.8) | 42.0 |

K = 1 is the best on every guard that fails: the fewest stuck guests (5.25, against 18.6 to 20.5 for rule 1 alone, K = 3 and K = 5), the smallest wait regression and the guests-in-park count nearest the default, with the largest happiness gain (+11.8 SE); its cost is revenue per admitted guest, +2.3 SE ("same", still above the default) instead of +3.5 to +4.0 SE. A longer run only brings the rule-1 loop back.

**Verdict, honestly.** Passing at 3 SE on the scenario: price rejections (-26.7 SE), guest-time happiness (+11.8), the survivors' happiness (+7.6), departures per admission and happiness-0 departures (-3.2, -25.5), the use Gini (-4.2), admissions (0.4), revenue per admitted guest (+2.3, not a regression), wait p99 (+2.7) and every level park. **Still failing** on the scenario: the wait p50 (11.6 to 17.0 turns, +5.7 SE), the wait p90 (73 to 101 turns, +8.1 SE) and the stuck-guest count (1.25 to 5.25, +4.5 SE; at most about 5 guests in a seed).
- *Waits.* In the default, 28% of all queue attempts end at the dear shop's stand and never join a ride queue. They now go to the four rides (boarded share 0.544 to 0.717), which have a capacity of one guest each, so the ride queues are longer. Every variant tried (rule 1 alone, a fallback to a below-floor attraction, a heavier cost for the price-emptied decision, going home after K = 1 to 5) lands on a p90 of 101 to 108 turns, the queue limit, so this is the load the scenario puts on its rides, not an effect of the loop; the spreading rules of phase 3 are the answer.
- *Stuck guests.* A price-emptied guest now leaves at once, so the remaining ones (5 against 1.25) are decisions that find nothing for other reasons; they last longer than before because the dear shop no longer fills the guest's time. This is not analysed further here.
- Nothing else was tuned; the constants were chosen by the sweep above and the booth change by the level-park run.

CPU: 1.31 us per tick for the default and 1.42 us for the rules on the scenario's first seed (machine under load, noisy; ratio 1.08, limit 1.10) with 36.5 against 32.8 guests in the park; allocation 615 B against 612 B per tick.

### Version history of `opentpw.smarter-guests`

| Version | Phase | Behaviour |
| --- | --- | --- |
| 1.0.0 | 0b | The seam and the guests' identity (birth ids, park seed, the `SmarterGuests` section). Every decision delegates to the original. |
| 1.1.0 | 2 | Value for money: price in the shop and sideshow choice, a guest that price keeps from every stand goes home (`Stalls` in the section), the booth's variety scaling and the price sensitivity trait. A 1.0.0 park is refused (`needs opentpw.smarter-guests 1.0.0, not 1.1.0`). |
| 1.2.0 | 3 | Spreading: the expected-wait queue term for every attraction (`Throughputs` in the section), the 94 to 106 percent preference and the ride-to-ride hysteresis. A 1.1.0 park is refused (`needs opentpw.smarter-guests 1.1.0, not 1.2.0`). |
| 1.3.0 | 4 | Queue patience: no join when the expected wait exceeds the guest's patience, and a give-up after waiting too long, each only when another ride is clearly quicker (a 70 to 130 percent patience trait, stateless: no new saved state). A 1.2.0 park is refused (`needs opentpw.smarter-guests 1.2.0, not 1.3.0`). |

The extension stays not player-selectable (`PlayerSelectable: false`): it is exposed once the rule set (phases 2 to 6) is complete, so a player does not meet a save break with every phase.

## Phase 3: Spreading

Version 1.2.0 of `opentpw.smarter-guests`. In TPW a guest's queue term is the original's near-only match: it counts only within a squared distance of 8 of the back of the queue, counts only the guests already in the queue, and knows nothing of how fast the attraction serves them.
Phase 2 moved the guests that the dear shop used to absorb onto the four one-guest rides, whose queues then grew; this phase makes the choice see the queue it is walking into. All rules are `[EXT:smarter-guests-spread]`, live in `SmarterGuestDecisions` / `SmarterGuestsRules` / `RideThroughputs`, and are gated by the extension; with it off the host does no extra work and every pin is unchanged.

**What the host adds (the policy stays pure).** A policy that returns `UsesSpreading` (the smarter one; the script policy forwards its base's answer) gets, in every candidate view, `Reservations` (the guests other than the asking one walking to the attraction: state "going to a ride" with it as their target; counted by the host once per decision, exact at the moment of the decision, because the candidate loops change no guest) and
`TurnsPerGuest256` (the attraction's observed time per guest in 1/256 park turn; the throughput is its inverse). It also feeds the throughput estimates from the boardings it already sees (`OnVisitorBoarded`, in the host's deterministic order), looks at the attractions once per park turn, and asks `SwitchesTarget` in the minor decision.
Two additive members of `IGuestDecisions` with default implementations (a policy that ignores them, including the original and every script policy, is unchanged): `UsesSpreading` and `SwitchesTarget( context, guest, target, targetScore, candidate, candidateScore )`; `AttractionView` gained two optional trailing members; `AttractionScoreInput` gained an optional trailing `QueueMatchOverride`.

**1. The queue estimate, for every attraction.** The expected wait of a guest who joins now is `(guests in the queue + guests walking there) x time per guest`, which is `ahead / throughput` with throughput = capacity / cycle duration. The smarter score is the original score with its queue match replaced by
`100 - 100 x wait / 120 turns` (clamped to 0..100, `QueueWaitScaleTurns` = 120), counted at any distance and with the original's queue weight. An attraction without an estimate (a host that was not asked, interval 0) is scored by the original alone.
The score is never lowered below the choice floor for a candidate the original would have chosen, and a **ride the guest has not used lately** (in neither history, not of the type it used last) **whose excitement the admission gate would let the guest through to** is kept at the floor too, so a guest does not stand still while an unused ride exists. The candidates kept at the floor rank as `10 + score / 5`, so their own wait and preference still order them. **a long wait orders the candidates but does not remove them**. Leaving a queue is the patience rule's job (phase 4); a first version without this protection let a long expected wait take a guest's only ride out of the choice, which raised "can't decide" outcomes
(jungle: 72.5 to 93 stuck guests, guest-time happiness -8.6 SE).

*The throughput estimate* (`RideThroughputs`, one entry per attraction instance, persisted):

- **What is averaged.** The gap in park turns between two boardings of the attraction, while it had demand throughout (two boardings in one park turn are a gap of 0, taking the average down to the minimum interval of a quarter turn), as a moving average in 1/256 turn (integers only, so a resumed park continues exactly). With a ride that serves a batch the mean gap is cycle duration / capacity; with a one-guest ride it is the cycle.
  A new gap is weighed `1 / (samples + 2)`, at most `1 / 16`: the cold-start value counts as one sample, and a long history moves by a sixteenth per gap.
- **Cold start.** An attraction nobody has boarded yet reads as `DefaultCycleTurns` / its live capacity: 60 turns for a ride, `DefaultServiceTurns` = 12 for a shop, sideshow or toilet (measured gaps: shops 9 to 12, rides 29 to 31 per guest, a toilet 45). A capacity below 1 reads as 1.
- **Demand follows the queue.** At each park turn start an attraction that is open and working with a queue and has no demand flag gets one, with a silence clock started that turn, and an entry at the cold start if it had none; a closed or broken attraction, or one with an empty queue, loses the flag. A gap counts only if the attraction had demand throughout and the gap started at a boarding (the first boarding after a queue formed measures the wait, not the service): otherwise it measures how fast guests arrive. A repaired ride whose head never boards, and a ride that never boards anybody, are therefore read as stalled by their silence.
- **Closure and breakdown.** The average is kept and the downtime is not folded in; the first boarding after a reopening starts a new gap, and a queue still waiting at the repair starts a new silence clock.
- **Upgrade.** A changed **live** capacity (the ride script's `VAR_CAPACITY`, which the ride settings change; `IRideVisitorBridge.LiveCapacity`) or speed restarts the estimate at the cold start of the new capacity, when the host looks at the turn start and again, defensively, whenever the estimate is read or a boarding is folded in.
- **Zero throughput.** The interval cannot go below a quarter turn (`MinimumInterval256`) or above 1,200 turns (`MaximumGapTurns` is also the longest gap folded in), so neither a zero nor an infinite wait can enter the score. An attraction that has not boarded anyone for more than two nominal cycles while guests wait is read as the silence since its clock started (the last boarding, or the turn its queue formed or its repair), divided by its capacity (a stalled script, a stuck head).
  A view with no estimate at all is "unknown" and falls back to the original's queue term.
- **Removal.** An attraction's estimate is dropped when it is unregistered.
- **Persistence.** `Throughputs` in the `SmarterGuests` section (attraction-id order; omitted while empty, so a section from before, a script-mod-only park and a park whose attractions never boarded anybody are as they were), validated strictly (attractions the save holds, strictly ascending ids, capacity at least 1, interval and samples inside their limits, a boarding turn of -1 or more, a silence clock not before it, neither after the park's turn, demand only with a clock), and hashed only while non-empty (`THRP`).

**2. A stable preference per guest and attraction instance.** The score of a candidate is scaled by `94 + hash % 13` percent (94 to 106), where the hash mixes the guest's `Trait( guest, 1 )` (a stateless hash of the park seed and the birth id) with the attraction's id. It is the same for the guest's whole visit, takes no random number, and differs between guests and between instances, so two identical rides have different favourites.
The band is narrow on purpose: it breaks ties, it does not override excitement or distance. A score of 0 stays 0.

**3. Hysteresis.** In the minor decision (a guest walking to a ride looks at the attractions within a 4 x 4 window that are nearer than the guest is) the host scores the target once with the same policy, inside the same metered decision, and asks `SwitchesTarget`. A **ride** target is kept unless the best ride in reach beats it by `max(2 points, 20 percent of the target's score)`; a target scoring below the choice floor holds nothing.
A walk toward a shop, toilet or sideshow, and a shop, toilet or sideshow in reach, stay impulse stops as in the original. The restriction is measured: the same margin on every kind removed the original's impulse purchases and cost the shop-and-toilet park (Halloween) 14 SE of revenue per admitted guest (59.3 to 51.1) and 6 SE of boarded share, with no gain elsewhere.
Two host counters (`TargetsKept`, `TargetsSwitched`, not saved or hashed) show it acts: 5 kept and 1 switched in 40 days of seed 1, 3 and 7 in seed 2, 4 and 4 in seed 3. It is rare in these parks (a decision needs two rides within a few cells), so the measured effect is nil; it is insurance against the original's "any better score steals the guest" quirk on parks with ride clusters.

**Constants** (all in `SmarterGuestsRules` / `RideThroughputs`, all OpenTPW's own): wait scale 120 turns; preference 94 to 106 percent; margin 20 percent and at least 2 points; cold start 60 turns per cycle for a ride and 12 for a service; weight 1/(samples + 2) up to 1/16; gap and interval limits a quarter turn to 1,200 turns; floor order `10 + score / 5`; stall at 2 cycles.

### Measured effect (phase 3)

`--guest-report --guest-scale extended --guest-extension smarter-guests` against the default, 8 seeds per scenario (the same seeds as the baseline above), `--guest-compare` with 3 standard errors, release build. 1.1.0 is the phase 2 run of the table above.

`park-scenario` (mean ± σ over the 8 seeds):

| Metric | Default | 1.1.0 | 1.2.0 | vs default (SE, verdict) | vs 1.1.0 (SE, verdict) |
| --- | ---: | ---: | ---: | --- | --- |
| Queue attempts | 341.50 ± 22.92 | 337.88 ± 11.21 | 387.38 ± 14.61 | 4.8 changed | 7.6 changed |
| Boarded / attempts | 0.544 ± 0.020 | 0.717 ± 0.015 | 0.755 ± 0.020 | 21.3 improved | 4.4 improved |
| Rejected / attempts | 0.410 ± 0.019 | 0.245 ± 0.015 | 0.208 ± 0.020 | -20.3 improved | -4.1 improved |
| Abandoned / attempts | 0.026 ± 0.008 | 0.009 ± 0.004 | 0.008 ± 0.004 | -5.4 changed | -0.5 same |
| Price rejections / attempts | 0.280 ± 0.026 | 0.024 ± 0.007 | 0.027 ± 0.006 | -26.5 improved | 1.0 same |
| Wait p50, turns | 11.62 ± 2.07 | 17.00 ± 1.69 | 13.50 ± 1.69 | 2.0 same | -4.1 improved |
| Wait p90, censored | 73.38 ± 7.09 | 101.00 ± 6.52 | 96.12 ± 3.23 | 8.3 regressed | -1.9 same |
| Wait p99, censored | 108.12 ± 4.64 | 123.88 ± 16.01 | 117.00 ± 3.66 | 4.2 regressed | -1.2 same |
| Happiness, guest-time weighted | 49.01 ± 3.13 | 62.84 ± 1.11 | 63.26 ± 1.16 | 12.1 improved | 0.7 same |
| Happiness of the guests still inside | 48.22 ± 4.61 | 62.87 ± 2.92 | 64.32 ± 3.25 | 8.1 improved | 0.9 same |
| Departures per admission | 0.599 ± 0.051 | 0.506 ± 0.064 | 0.413 ± 0.031 | -8.9 improved | -3.7 improved |
| Use Gini over the rides (raw and capacity-normalised: every ride has capacity 1) | 0.198 ± 0.037 | 0.132 ± 0.025 | 0.106 ± 0.015 | -6.6 improved | -2.5 same |
| Revenue per admitted guest | 82.06 ± 8.50 | 94.24 ± 12.14 | 103.80 ± 8.31 | 5.2 improved | 1.8 same |
| Guests with no choice for over 50 turns | 1.250 ± 1.035 | 5.25 ± 2.31 | 1.250 ± 1.488 | 0.0 same | -4.1 improved |
| Guests in the park (mean) | 32.84 ± 1.88 | 36.47 ± 1.26 | 40.73 ± 0.88 | 10.8 changed | 7.8 changed |
| Admissions | 63.88 ± 0.35 | 64.00 ± 0.00 | 63.88 ± 0.35 | 0.0 same | -0.4 same |

The four level parks, 1.2.0 against the default (they have one ride, so spreading has little to do; they are **no longer bit-identical** to the default (no seed ends in the same state hash), but little moves beyond 3 SE, see below):

Default → 1.2.0 (SE and verdict against the default), mean ± σ:

| Metric | Jungle | Halloween | Fantasy | Space |
| --- | --- | --- | --- | --- |
| Boarded / attempts | 0.254 ± 0.010 → 0.254 ± 0.006 (-0.0 same) | 0.684 ± 0.041 → 0.719 ± 0.028 (2.0 same) | 0.454 ± 0.025 → 0.467 ± 0.019 (1.2 same) | 0.481 ± 0.021 → 0.499 ± 0.021 (1.7 same) |
| Wait p50, turns | 31.88 ± 0.83 → 32.75 ± 0.46 (1.8 same) | 11.62 ± 1.19 → 11.38 ± 1.06 (-0.4 same) | 25.62 ± 2.62 → 26.62 ± 1.41 (1.0 same) | 33.00 ± 1.07 → 35.25 ± 0.71 (4.3 regressed) |
| Wait p90, censored | 528.50 ± 24.05 → 516.88 ± 17.71 (-1.1 same) | 194.12 ± 6.81 → 196.50 ± 4.81 (0.8 same) | 738.38 ± 36.35 → 710.75 ± 37.32 (-1.5 same) | 432.38 ± 9.97 → 387.12 ± 26.25 (-4.6 improved) |
| Happiness, guest-time weighted | 53.04 ± 0.51 → 53.68 ± 1.04 (1.5 same) | 37.82 ± 0.89 → 38.70 ± 0.82 (2.1 same) | 49.22 ± 0.47 → 49.60 ± 0.40 (1.6 same) | 48.45 ± 0.55 → 48.51 ± 0.62 (0.2 same) |
| Revenue per admitted guest | 47.63 ± 0.50 → 48.25 ± 0.32 (2.5 same) | 59.31 ± 1.49 → 60.12 ± 2.40 (0.8 same) | 55.27 ± 2.31 → 57.57 ± 0.95 (2.6 same) | 46.00 ± 0.53 → 48.30 ± 0.48 (8.9 improved) |
| Guests with no choice for over 50 turns | 72.50 ± 3.70 → 67.50 ± 5.29 (-2.2 same) | 59.12 ± 2.47 → 57.12 ± 3.72 (-1.3 same) | 70.50 ± 8.64 → 71.88 ± 6.47 (0.4 same) | 64.00 ± 4.54 → 65.00 ± 5.32 (0.4 same) |
| Guests in the park (mean) | 104.34 ± 1.62 → 103.18 ± 1.57 (-1.4 same) | 36.95 ± 0.92 → 35.89 ± 1.15 (-2.0 same) | 87.06 ± 4.11 → 87.13 ± 2.41 (0.0 same) | 97.88 ± 1.67 → 97.07 ± 2.46 (-0.8 same) |

Level parks, honestly: space waits at the median are 2.3 turns longer (33.0 to 35.3, +4.3 SE, regressed) while its p90 falls (432 to 387, -4.6 SE), its revenue per admitted guest rises (46.0 to 48.3, +8.9 SE) and its use Gini over all attractions changes (0.285 to 0.302, +6.6 SE: the spread over the ride, the shops and the toilet shifts); Halloween's happiness of the guests still inside rises (29.7 to 33.1, +3.2 SE). Everything else is equal within the spread. The shipped parks have one ride, so the unused-ride fallback below has nothing to act on there.

**Tuning.** (Swept on the build before the unused-ride fallback, the demand-follows-the-queue estimator and the live capacity; the last row is the final build.) 24 seeds of the scenario (`--guest-seeds 24 --guest-levels none`; 8 seeds are too few to tell settings of this size apart), the shipped rules against variants of one constant at a time (the default and the phase 2 run on the same 24 seeds are the first two rows):

| Variant | Boarded | Wait p50 | Wait p90 cens. | Departures / adm. | Use Gini | Revenue / adm. | Happiness | Stuck | In park |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| default | 0.554 | 11.9 | 72.6 | 0.592 | 0.197 | 81.2 | 48.8 | 1.39 | 32.6 |
| 1.1.0 | 0.723 | 16.6 | 102 | 0.519 | 0.133 | 94.7 | 62.7 | 4.79 | 36.5 |
| wait scale 60 | 0.767 | 12.3 | 97.8 | 0.464 | 0.127 | 104 | 62.7 | 5.12 | 38.9 |
| **shipped: scale 120** | 0.769 | 13.0 | 93.5 | 0.407 | 0.108 | 103 | 62.8 | 5.54 | 40.5 |
| wait scale 240 | 0.755 | 14.5 | 96.0 | 0.337 | 0.103 | 108 | 63.7 | 3.83 | 41.9 |
| ride cycle 30 | 0.764 | 14.4 | 91.2 | 0.397 | 0.097 | 108 | 63.4 | 4.96 | 41.1 |
| ride cycle 120 | 0.774 | 12.3 | 94.7 | 0.400 | 0.131 | 106 | 62.5 | 6.79 | 40.2 |
| service cycle 30 | 0.765 | 12.7 | 91.7 | 0.415 | 0.102 | 110 | 63.8 | 5.38 | 40.1 |
| no hysteresis | 0.775 | 13.2 | 91.8 | 0.412 | 0.107 | 105 | 63.0 | 5.04 | 40.7 |
| margin 0 | 0.768 | 12.9 | 93.3 | 0.403 | 0.109 | 103 | 62.8 | 5.96 | 40.6 |
| margin 50 percent, 4 points | 0.769 | 13.0 | 93.5 | 0.407 | 0.108 | 103 | 62.8 | 5.54 | 40.5 |
| no preference | 0.767 | 13.0 | 92.2 | 0.388 | 0.107 | 107 | 62.9 | 5.25 | 41.0 |
| preference 97 to 103 | 0.771 | 13.4 | 92.6 | 0.402 | 0.104 | 106 | 62.8 | 5.67 | 40.4 |
| preference 88 to 112 | 0.772 | 12.8 | 90.3 | 0.394 | 0.110 | 108 | 62.9 | 5.12 | 40.5 |
| **final build** | 0.752 | 13.8 | 95.7 | 0.394 | 0.107 | 105 | 63.3 | 1.54 | 41.3 |

Every variant of every constant lands within about 1.5 SE of the shipped value on each headline metric, so none of them is a lever: the step from 1.1.0 to any of them is the rule itself (the expected-wait term), not its tuning. The wait scale trades the median wait for crowding: a larger scale lets the park fill (departures per admission 0.46, 0.41, 0.34 and guests in the park 38.9, 40.5, 41.9 for 60, 120 and 240 turns) and leaves fewer guests with nothing to do (stuck 5.1, 5.5, 3.8), while the median wait grows (12.3, 13.0, 14.5).
120 is the middle value; the preference band and the hysteresis margin did not move the scenario, where the two identical rides were already used evenly under 1.1.0 (368 and 364 boardings over 8 seeds) and are now (354 and 347 with more guests), so they keep their narrow, documented values; the hysteresis acts rarely there and its margin never changed a run (margin 50 percent is the shipped run to the digit).
A multiplicative variant that keeps the original's near-only queue match and scales the score by 100 down to 40 percent of the expected wait was measured at 8 seeds: it matched the replacement within the spread (wait p50 13.9 against 13.6, p90 96.8 against 93.0) and was dropped, because the specified design replaces the queue match.

**Verdict, honestly.**
- *Fixed or clearly reduced* against 1.1.0: the median wait (17.0 to 13.5 turns, -4.1 SE; the default's is 11.6, +2.0 SE, "same"), the censored p90 (101 to 96, -1.9 SE, "same"), departures per admission (-3.7 SE), the use Gini (0.132 to 0.106), and the **stuck guests (5.25 to 1.25, -4.1 SE; equal to the default's 1.25)**. Phase 2's gains hold: price rejections 0.024, happiness (+12.1 SE against the default), revenue per admitted guest (+5.2 SE), the abandon rate.
- *Not fixed.* The censored p90 and p99 remain longer than the default's (96 and 117 against 73 and 108; +8.3 and +4.2 SE): in the default 28% of the attempts end at the dear shop and the rest of the guests never queue, while now 76% board (up from 54%), the park holds 41 guests instead of 33, and the four one-guest rides carry all of them at the queue limit. That is the cost of a park that keeps its guests, until the patience rule (phase 4).
- *The stuck guests, corrected.* The first version of this page blamed guests that had used all four rides lately. An independent review logged 64 stuck guests at the moment each passed 50 turns without a choice: none had four rides in its history (44 had two, 18 one, 2 none; the two shops usually fill two of the four slots), and in 62 of 64 every attraction, **including rides never used**, scored below the floor of 10 under both the original rules and the policy (an unused ride scores 1 to 8 from its distance and excitement).
  The fix is cheap and was measured: a ride the guest has not used or turned away from lately, of another type than the last, and one the admission gate would admit by excitement, is kept at the floor (rule 1). Scenario stuck guests fall from 4.5 (the same build without the fallback) to 1.25, the median wait is unchanged (13.5) and the censored p90 moves from 91.5 to 96.1; the level parks, with one ride, do not change. It lives here because it is the same floor rule; the guest that is truly done with the park (every ride used, nothing needed) is still the business of phase 5 or a satisfied-guest rule. It moves the on-path behaviour, not the version (1.2.0 is unreleased).
- CPU and allocation: 609 to about 650 bytes per tick on the scenario (+6%); the walkers per attraction are counted once per decision, not once per candidate view. Microseconds per tick 1.8 to 3.2 for both runs on a machine under load, which is the noise.

## Phase 4: Queue patience

Version 1.3.0 of `opentpw.smarter-guests`. In the original a queued guest leaves only by its own tests (happiness below 10, a toilet need above 80, the queue limit); the 100-turn boredom branch of `GuestSimulation` is unreachable (`QUEUE-plan` section 5.3: `+508 >= +520` always holds there), so TPW has no time limit that can fire and no guest judges a queue before joining. Phase 3 spreads guests with an expected-wait term but keeps every candidate at the choice floor on purpose, so a long wait never makes a guest give up: that is this phase's job. Both rules are `[EXT:smarter-guests-patience]`, live only in `SmarterGuestDecisions` / `SmarterGuestsRules`, and cost nothing with the extension off.

**The base is measured, not borrowed.** `BoredomThreshold` is not used: its unit is unproven and its test cannot fire. The base is the original's own wait in the scenario baseline: boarded waits of p90 73 and p99 108 turns ([baseline](#park-scenario-extended-scale-20--60-days)). The join patience is **90 turns** (between p90 and p99: a wait the original gives to its unluckiest tenth), the give-up patience **120 turns** (above the baseline's p99, so it only touches waits the original does not produce). Both are scaled by a stateless per-guest trait, `PatienceTrait` (`Trait( guest, 2 )`, a hash of the park seed and the birth id, the same for the whole visit): **70 to 130 percent**. Being a trait, patience needs no saved state.

**What the host adds (additive; the policy stays pure).** `IGuestDecisions.AlternativeLookAfterTurns` (default -1, forwarded by `ScriptGuestDecisions`) asks the host for the best alternative: `AdmissionView.BestAlternativeWait256` at every admission and `QueuePatienceView.BestAlternativeWait256` once the guest has waited that many turns (the smarter policy asks from 80, the earliest possible give-up being 70 percent of 120 = 84). It is the shortest phase 3 expected wait, in 1/256 turn, over the **other open, working rides that the guest could join now** (room in the queue, excitement word and join difference admitted), or -1 for none. The host computes it with the same estimate the spreading rules use (`GuestSimulation.BestAlternativeWait256`, pure: no draw, no state), only for a policy that asks.

**1. Do not join.** `DecideAdmission` runs the original gates first (excitement, queue limit). A guest that passes them is refused when it is at a **ride** whose expected wait from the back (`(queue + guests walking there) x time per guest`) is above `90 x patience` turns **and** another ride's wait is at most 80 percent of it. The refusal is `QueueTooLong`, so the host's existing path applies and statistics stay consistent: the "queue too long" thought (16), `QueueEndReason.QueueLimit` (counted as a refusal, in the insights' "queue too long" counter), the refused ride pushed into the temporary history (so the guest does not retry it at once) and the guest's next choice is the host's. No happiness is lost, as in the original refusal.

**2. Give up.** `DecideQueuePatience` runs after the existing leave tests, on a turn the guest stays, never while it is called forward and never in an interlude (the host's call sites; it can run on the turn a move-up starts, which keeps the original's flow). A guest **not at the front** leaves when it has waited longer than `120 x patience` turns from `QueueJoinTurn` **and** another ride's wait is at most 80 percent of its own remaining wait (`position x time per guest`). It thinks "queue too long" (16, the nearest original thought for "waited too long") and is dismissed by the host's existing path with `QueueEndReason.Impatient` (counted as abandoned): the original dismissal's medium happiness step is the cost, and the guest chooses again at once.

**Only rides, only with somewhere better to go.** Shops, sideshows and toilets are services a guest wants for its own reason, and the alternatives are rides, so neither rule concerns them (a guest queueing for relief, or with the toilet need that makes the original leave other queues, is never touched). The alternative condition is what the measurement required (below): without it the rules also fired in the four level parks, which have one ride, where a refused or dismissed guest has nowhere to go and only loses happiness. With it the level parks are **bit-identical to 1.2.0** (every seed ends in the same state hash).

**Interactions.**
- *Phase 3 spreading (hysteresis).* The rules never change a walking guest's target: hysteresis acts in the minor decision, patience at the back of a queue and in it. A refused guest has the ride in its temporary history and chooses by the expected-wait score, which already prefers the quicker ride that made the refusal; a dismissed guest likewise. Refusal needs the alternative to be clearly quicker (80 percent), a margin like hysteresis's, so two similar rides do not make guests oscillate between queues; the 80 percent test and the 90 and 120 turn limits also leave a gap between "join" and "give up" (a guest that was admitted had an expected wait within its patience or no better ride, and gives up only after 120 turns and against the then-current alternative).
- *Phase 2 (go home when price empties every choice).* Independent: a patience refusal is a queue refusal at a ride, not a price-emptied decision (`PriceTurnedAway` looks only at priced stands), and a patience exit is a queue exit, not a stall: the stall counter and the go-home rule do not move. The guest that left a queue chooses again with its money and happiness as they are.
- *The existing leave tests* run first and win: a guest below happiness 10, with a toilet need or beyond the queue limit leaves for its own reason; the patience hook never runs for it. A broken or closed ride dismisses its head and keeps the rest waiting as before; a guest at position 0 is never asked.

### Measured effect (phase 4)

`--guest-report --guest-scale extended --guest-extension smarter-guests` against the default and against 1.2.0, 8 seeds per scenario (the same seeds), `--guest-compare` with 3 standard errors, release build.

`park-scenario` (mean ± σ over the 8 seeds):

| Metric | Default | 1.2.0 | 1.3.0 | vs default (SE, verdict) | vs 1.2.0 (SE, verdict) |
| --- | ---: | ---: | ---: | --- | --- |
| Queue attempts | 341.500 ± 22.916 | 387.375 ± 14.609 | 456.750 ± 22.670 | 10.1 changed | 7.3 changed |
| Boarded / attempts | 0.544 ± 0.020 | 0.755 ± 0.020 | 0.583 ± 0.059 | 1.8 same | -7.8 regressed |
| Rejected / attempts | 0.410 ± 0.019 | 0.208 ± 0.020 | 0.399 ± 0.062 | -0.5 same | 8.2 regressed |
| Abandoned / attempts | 0.026 ± 0.008 | 0.008 ± 0.004 | 0.005 ± 0.004 | -6.2 changed | -1.6 same |
| Price rejections / attempts | 0.280 ± 0.026 | 0.027 ± 0.006 | 0.022 ± 0.008 | -26.6 improved | -1.6 same |
| Wait p50, turns | 11.625 ± 2.066 | 13.500 ± 1.690 | 10.375 ± 1.061 | -1.5 same | -4.4 improved |
| Wait p90, censored | 73.375 ± 7.090 | 96.125 ± 3.227 | 43.125 ± 8.323 | -7.8 improved | -16.8 improved |
| Wait p99, censored | 108.125 ± 4.643 | 117.000 ± 3.665 | 80.875 ± 13.271 | -5.5 improved | -7.4 improved |
| Happiness, guest-time weighted | 49.007 ± 3.130 | 63.260 ± 1.160 | 62.984 ± 1.264 | 11.7 improved | -0.5 same |
| Happiness of the guests still inside | 48.217 ± 4.606 | 64.323 ± 3.247 | 63.284 ± 2.893 | 7.8 improved | -0.7 same |
| Departures per admission | 0.599 ± 0.051 | 0.413 ± 0.031 | 0.366 ± 0.034 | -10.8 improved | -2.9 same |
| Happiness-0 departures per admission | 0.528 ± 0.047 | 0.100 ± 0.024 | 0.088 ± 0.014 | -25.3 improved | -1.2 same |
| Use Gini over the rides | 0.198 ± 0.037 | 0.106 ± 0.015 | 0.103 ± 0.038 | -5.1 improved | -0.2 same |
| Revenue per admitted guest | 82.062 ± 8.502 | 103.801 ± 8.311 | 100.676 ± 10.057 | 4.0 improved | -0.7 same |
| Guests with no choice for over 50 turns | 1.250 ± 1.035 | 1.250 ± 1.488 | 0.375 ± 0.518 | -2.1 same | -1.6 same |
| Guests in the park (mean) | 32.840 ± 1.877 | 40.726 ± 0.880 | 40.423 ± 1.060 | 9.9 changed | -0.6 same |
| Admissions | 63.875 ± 0.354 | 63.875 ± 0.354 | 63.875 ± 0.354 | 0.0 same | 0.0 same |

Both phase 4 targets pass against the default and against 1.2.0: the censored p90 falls from 96 (1.2.0) to 43 turns and the p99 from 117 to 81, **below the default's 73 and 108** (-7.8 and -5.5 SE against it). Phase 2 and 3 gains hold: happiness (+11.7 SE against the default, same as 1.2.0), the survivors' happiness (+7.8), revenue per admitted guest (+4.0), price rejections (-26.6), the use Gini (-5.1), departures per admission (-10.8), stuck guests (0.375 against 1.25) and happiness-0 departures (-25.3) all keep their sign and no metric moves the wrong way against the default beyond 3 SE.

Against 1.2.0 two metrics are marked regressed and are the price of the rule, not noise: the boarded share falls from 0.755 to 0.583 (-7.8 SE) and the rejected share rises from 0.208 to 0.399 (+8.2 SE). That is mostly a counting effect: a refusal is an attempt that ends as a rejection, and refused guests try another ride, so attempts rise from 387 to 457 (+7.3 SE) while the boarding count falls only from 292.5 to 265.6 per seed (9%; the default boards 185.8). The guests ride a little less and wait much less, and their happiness and revenue do not change (-0.5 and -0.7 SE). The abandoned share is unchanged (0.005; the patience exits are about one a seed).

**Level parks.** Every seed of Jungle, Halloween, Fantasy and Space ends in the same state hash as under 1.2.0, so every metric is equal to the 1.2.0 run (and so to the phase 3 table above): they have one ride, hence no alternative, hence no rule acts. Against the default the only metric marked regressed is the one phase 3 already had (Space's median wait 33.0 to 35.3). `--guest-compare` with the phase 4 targets therefore reports the level parks' censored waits as "did not improve" (there is nothing to improve there) and exits 1, as the phase 2 and 3 compares of those parks did; the targets are judged on `park-scenario`, as for phase 3.

**Tuning.** (Every row is the same 8 seeds, the measured settings in order.) Variants, scenario waits (censored p90 / p99, default 73 / 108, 1.2.0 96 / 117) and what the level parks did:

| Variant (join / give-up / alternative share) | Scenario p90 / p99 | Scenario boarded share, happiness | Level parks |
| --- | ---: | --- | --- |
| no alternative condition, 110 / 150 | 27 / 58 | 0.440, 61.7 | Jungle, Hallow, Fantasy, Space board 0.084 to 0.46 of attempts (-14 to -49 SE); Fantasy happiness -8.5 SE, revenue -7.4 SE; **rejected** |
| no alternative condition, 200 / 280 | 80 / 114 | 0.697, 63.6 | Jungle, Fantasy and Space board 0.10 to 0.19 of attempts; Fantasy happiness -10.1 SE, revenue -7.1 SE; the scenario does not improve beyond 3 SE; **rejected** |
| no alternative condition, 300 / 420 and 450 / 600 | 94 / 116 and 96 / 117 | 0.751 and 0.753, 63.2 and 63.3 | Fantasy happiness -7.3 and -8.5 SE; the scenario no longer improves; **rejected** |
| give-up only, 120 / 200 / 300 (no refusal) | 96 / 117 | 0.753, 63.3 | happiness Jungle -12.7 / -7.4 / -3.1 SE, Fantasy -24.5 / -14.3 / -9.6, Space -13.5 / -7.6 / -4.6; the scenario does not improve (its waits stay below 120); **rejected** |
| with alternative, 110 / 150 / 60 percent | 64 / 108 | 0.676, 63.8 | none; p90 -2.1 SE and p99 -0.2 SE against the default, "same": too weak |
| with alternative, 70 / 100 / 60 | 55 / 98 | 0.630, 63.9 | none; p90 improved (-3.5 SE), p99 "same" (-1.6) |
| with alternative, 50 / 80 / 60 | 60 / 102 | 0.633, 63.1 | none; p90 -2.6 SE, "same": too weak |
| with alternative, 50 / 80 / 80 | 40 / 86 | 0.532, 63.2 | none; both pass |
| with alternative, 70 / 100 / 80 | 41 / 81 | 0.572, 62.7 | none; both pass |
| with alternative, 70 / 100 / 100 | 33 / 75 | 0.480, 61.2 | rejected share +4.5 SE and Jungle's median wait +3.3 SE against the default: too much |
| **with alternative, 90 / 120 / 80 (shipped)** | **43 / 81** | 0.583, 63.0 | none; both pass |

The shipped values are the mildest of the settings that pass both targets with a margin: they refuse least (90 sits just under the baseline's p99 of 108) and keep the use Gini and the boarding count highest. The simpler rules without the alternative condition won on the scenario and failed everywhere else, and a give-up alone changes nothing in a park whose waits are below it and costs happiness where waits are long.

**Constants** (all in `SmarterGuestsRules`, all OpenTPW's own): join patience 90 turns; give-up 120 turns; patience trait 70 to 130 percent; alternative at most 80 percent of the wait; the host looks at the alternatives from 80 turns of waiting.

**Phase 4 tests.** `SmarterGuestsPatienceTests` covers the band (70 to 130, every value, stable, its own trait index), the base (between the baseline's p90 and p99; the give-up above both), rule 1 (refusal with a clearly quicker alternative as `QueueTooLong`, none without one or with an alternative that is no quicker, the walkers counted as guests ahead, the band moving the limit, the original's gates first, services and a missing estimate untouched), rule 2 (the turn it fires on, the thought, none for the front of the queue, without an alternative or a join turn, for a shop, sideshow or toilet, or when the remaining wait is short, the band moving the turn), the host (alternatives asked only when the policy wants them, `-1` otherwise so the original path does no extra work, and the hook only ever running for a waiting guest, never called forward or in an interlude), the scenario (a refusal below the queue limit happens, only at rides; a give-up is an `Impatient` end at a ride after the earliest patience) and that the policy has no mutable field. There is no new saved state, so the save and resume tests are the phase 3 ones (their version strings are 1.3.0): a 1.2.0 park is refused with the usual message. The off path is unchanged: every existing pin holds.

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
dotnet source/OpenTPW/bin/Release/net11.0/OpenTPW.dll --guest-report [--guest-scale plan|extended] [--guest-seeds 8] [--guest-warmup-days N] [--guest-days N] \
  [--guest-levels jungle,hallow,fantasy,space|all|none] [--guest-level-seeds 8] [--guest-arrivals 20] [--guest-report-json report.json] [--guest-no-perf]
```

Without game data (`OPENTPW_GAME_PATH` or `--game-path`) only `park-scenario` runs. The command prints a short table (mean, σ, minimum and maximum over the seeds, and the reject and
abandon reasons) and, with `--guest-report-json`, the stable machine-readable report (`opentpw.guest-report/1`: every seed's metrics, the per-metric spread and, unless
`--guest-no-perf`, a machine-dependent `performance` block that is left out of comparisons). Two runs with the same options give the same JSON apart from `performance`.
The plan scale (2 warm-up + 10 measured days, the default) takes about 20 s with the four level parks; `--guest-scale extended` (20 + 60 days) about 35 s.

**Decision: two scales.** The plan scale (2 + 10 days) stays the quick default of `--guest-report`; `--guest-scale extended` (20 + 60 days) is the scale of the baseline and of every acceptance threshold on this page.

With `--guest-extension smarter-guests` (repeatable; the extension's id works too) the whole run builds its simulations with the extension on (each reads the gate once, when built), the JSON gets an
`extensions` list beside its protocol (always present, `[]` for the default run), and the table says `Extensions on: ...`. With the delegating policy of 1.0.0 the report was the default's (every seed ended in the same guest state hash); since 1.1.0 the scenario differs (see [phase 2](#measured-effect)) and a note names the two extension sets.

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
  | 2 value for money | Price rejections as a share of attempts (`queue.rejected.price.rate`, down), `happiness.guestTime` (up) |
  | 3 spreading | `use.giniRides` or `use.giniRidesPerCapacity` (down) and `queue.wait.p90.censored` (down), on `park-scenario` only (the level parks have one ride) |
  | 4 queue patience | `queue.wait.p99.censored` (down) and `queue.wait.p90.censored` (down) |
  | 5 planning needs | `departures.happinessZero.perAdmission` (down) and `happiness.guestTime` (up) |
  | 6 clean and quiet paths | `happiness.guestTime` (up) |

- **No regression elsewhere:** every other metric with a direction must not move the wrong way by more than 3 standard errors. Up is good for `queue.boarded.rate`, `happiness.guestTime`,
  `happiness.survivors` and `revenue.perAdmittedGuest`; down is good for `queue.rejected.rate`, `queue.rejected.price.rate`, the `queue.wait.*` rows (boarded and censored), `departures.perAdmission`,
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
`ParkScenarioFixedRunHash` is the off path's.

**Phase 2 tests.** `SmarterGuestsValueTests` covers the rules in isolation (the sensitivity band, the prediction against the stand-point test, the zeroed score, `PriceTurnedAway`, the booth's scaling, its monotonicity and that it is never harsher than the original), the run counter (`PriceStalls`), the guest that goes home (a policy with a limit of 3 keeps and bounds the runs; the shipped limit of 1 sends guests home at once and keeps none), the host's facts (none and no state off; the stand-point statistic recorded the same number of times with the facts asked for, and the same hash), the section (round trip, empty omitted, strict validation, a refused restore changes nothing), the scenario variant with a park rating, and the section, the first-tick bootstrap and the world hash on the shipped policy. `SmarterGuestsTests` keeps the identity tests on an identity-only policy (what 1.0.0 was) where they compare guests with the off run, extends the resume test with runs that continue equal, and holds the compare test (price rejections improve beyond 3 SE; happiness, revenue per admitted guest and admissions do not regress). **Pins in phase 2:** the off-path pins are unchanged, `DeterminismTests.SmarterGuestsFixedRunHash` did not move (the synthetic park has one cheap shop that never turns a guest away, so the rules never act there; see [DETERMINISM.md](DETERMINISM.md)), and `GuestMeasurementTests` pins the scenario with the extension on (`ScenarioSmarterFixedRunHash`), where the rules do act. The script-mod pins run over the original policy and are unchanged.

**Phase 3 tests.** `SmarterGuestsSpreadingTests` covers the estimator (cold start for a ride, a shop and a capacity below 1; the first boarding, the half-weight first gap, the 1/(samples + 2) weights, no folding of a gap without demand; convergence on a steady gap and the 1/16 cap; gap and interval clamps and saturating samples; closure, breakdown and an empty queue starting a new gap, a queue forming again starting the silence clock, a repaired ride with guests still queued and a ride that never boards read as stalled, two boardings in one turn; an upgrade restarting by capacity or speed in the read, the turn-start look and the boarding, and a ride-settings change of the live capacity restarting the estimate in a park run; zero capacity and a stalled ride read as their silence; ordering and removal), the expected wait and the queue match (unknown without an estimate, reservations as guests ahead, the inverse of throughput, saturation), the score
(the original's for a view without an estimate, longer waits score lower, the queue counts at any distance, the floor protection over a sweep that reaches it, the order kept among the candidates at the floor, the unused-ride fallback and its exceptions), the preference band (94 to 106, every value occurs, roughly even, stable for the visit, two identical rides get different favourites, 0 stays 0), the hysteresis (the margin at the boundaries, rides only, shops stay impulse stops, the original always switches, the script policy forwards both new members), the host (no spreading fact, no state and no extra work off; reservations exclude the asking guest; throughput is tracked on the boardings; a removed attraction takes its estimate), the section (round trip, strict validation of every field, an unknown attraction refused at file level and a future boarding turn at restore, absent while empty, hashed only while non-empty) and that the spreading rules act in the scenario run. `AResumedParkContinuesLikeTheUninterruptedOneWithTheExtensionOn` also checks that the estimates are in the section and continue equal to the uninterrupted park's, every tick, and that they moved after the save point.
