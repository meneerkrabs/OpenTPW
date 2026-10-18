# Smarter guests: plan, measurement and baseline

The goal is an opt-in official extension that makes park visitors decide better, borrowing ideas (not code) from RollerCoaster Tycoon 1, 2 and 3. The faithful tp.exe
simulation ([GUESTS.md](GUESTS.md)) stays the default: with the extension off, behaviour, determinism pins and saves do not change. OpenRCT2 is GPLv3, so only concepts are
taken: no code, no data tables, no constants. Every phase is one pull request. This page holds the overview, the measurement that is built first, and its baseline.

## Architecture in short (as built in phase 0b)

- **Two extensions** (`OfficialExtensions`, both `DefaultEnabled: false`, version 1.0.0). `opentpw.guest-insights` is UI only (`AffectsSimulation: false`): refusal-reason counters (phase 1) and, later, a
  complaints summary; it is never recorded in saves. `opentpw.smarter-guests` is `AffectsSimulation: true`; the mod set is recorded in park saves (`ParkResumeMods`
  refuses a mismatched set), and the extension version is the algorithm identity: every behaviour-changing PR bumps it, and a park of another version is refused with a clear message
  (`needs opentpw.smarter-guests 1.1.0, not 1.0.0`). `FeatureGates.SmarterGuests` and `FeatureGates.GuestInsights` read them. **Smarter guests is released as opt-in in version 1.6.0** (`PlayerSelectable: true`, `DefaultEnabled: false`): until then it was hidden, because a version without a measurable effect would lock saves to a rule set the next version bump makes unloadable. 1.6.0 has a measurable effect (phase 6 crowding, phase 6b litter) and met the CPU gate on a hosted runner (see [Cost and release](#cost-and-release)). Its name and description exist in the nine interface languages (a test pins them).
  **Version policy:** the extension's version is the algorithm identity. A park saved by another version is refused with a clear message (`needs opentpw.smarter-guests 1.1.0, not 1.0.0`) and stays in the
  load list marked as requiring it; a later release may offer loading an older version's park with the newer rules as an explicit "migrate" choice. Park sharing stays blocked while a simulation extension is
  active, as for every simulation mod, until packages record mods.
- **A pure decision layer** (`World/Guests/Decisions/`). `GuestSimulation` takes an `IGuestDecisions` at construction (null: the gate decides, read once, in the constructor and never again):
  `OriginalGuestDecisions`, a stateless pure extraction of the original, or `SmarterGuestDecisions`, which delegated every call to it in 1.0.0 and has had its first rules since 1.1.0 (phase 2) and its spreading rules since 1.2.0 (phase 3) and its queue patience rules since 1.3.0 (phase 4) and its planning rules since 1.4.0 (phase 5). A policy gets a `DecisionContext` (settings, park
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
| 4 | Queue patience (built, version 1.3.0: [below](#phase-4-queue-patience)): a guest does not join a queue whose expected wait exceeds its patience, and gives up after waiting too long, when another ride it could choose would be clearly quicker. Measured honestly it acts rarely and does not meet the wait targets. |
| 5 | Planning needs (built, version 1.4.0: [below](#phase-5-planning-needs)): a service whose need will cross its level before the guest is done with its ride is scored as if it had; a service on the guest's way gets one detour step; shortly after a meal a ride that would make the guest sick scores lower (a persisted meal time). Measured honestly it acts rarely and has no measurable effect. |
| 6 | Clean and quiet paths (built, version 1.5.0: [below](#phase-6-clean-and-quiet-paths)): the wander goal moves to the cheapest of the original's own walk cells, the linked cells beside its goal and the goal itself when that is at least 2 cheaper (litter and guests around a candidate), and a guest standing in litter or a crowd complains (the original's "Too messy" or "Dissatisfied") at a cost of 0.1 happiness, at most once per 480 turns (a persisted complaint time). Measured honestly it lowers the share of guest time spent in crowds without changing happiness or revenue, and does not reduce litter exposure. |
| 6b | Litter (built, version 1.6.0: [below](#phase-6b-litter-routes-and-bins)): a guest with a full litter meter looks eight cells around it for a bin instead of three. Litter-aware routes were built and measured too and not kept. Measurable only where bins stand beyond the original's reach of where guests litter. |
| 7 | Tuning by data mods (optional). |
| 8 | RCT3 groups and families (only if wanted). |

## Phase 1: Top 3 Thoughts and the refusal counters

**Top 3 Thoughts (fidelity).** Re-validated in tp.exe: the original park information window (builder `0x4A52A0`, table `0x7537D0`) shows the three most common guest thoughts as three icons of the `ti_hungry` model, ranked by `0x4C8430`
(counts of the thought ids 0 to 22 over the guests on park cells, the count of id 0 discarded; three slots start as 0, 1 and 2 and take each larger id by the largest count difference, ties keep the lower id, slots unsorted, a zero count shows nothing) and refreshed by a one-second timer.
The full rule, the layout and the order of the icons are in [GUESTS.md](GUESTS.md) "Top 3 Thoughts". It is not a "most common thought" statistic: `GuestStatistics.CommonThought` was removed. The window's graph, counts frame, happiness levels and navigation buttons are built from tp.exe too (UI.md).

**Refusal counters (`opentpw.guest-insights`).** A passive `IGuestObserver` (`GuestRefusalCounters`), created by the guest simulation when the extension is on (the gate is read once, when it is built), shares the observer slot with a measurement collector
through `GuestObserverChain`. It counts five reasons from the queue-end reports the measurement already has: **too expensive** (`Price`, the stand-point refusal of a shop or sideshow), **queue too long** (`Full`, `QueueLimit`), **too scary**, **too boring**,
and **ride broken or closed** (`RideBroken`, `RideClosed`: a queue head sent away). `NotAccepting` is left out because it cannot tell a closed ride from a double join, and the guests' own leaving tests are abandonments, not refusals. The totals show in the
park information window, in the two free strips beside its title (above the graph and its toggles, so they never cover the plot), in the original UI style (the window's labels and fonts), with the labels in the nine interface languages. Never saved, never hashed, no draw: with it on, the guests'
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
| 1.3.0 | 4 | Queue patience: no join when the expected wait (the queue ahead) exceeds the guest's patience, and a give-up after waiting too long, each only when another ride the guest could choose is clearly quicker (a 70 to 130 percent patience trait, stateless: no new saved state). It acts rarely and does not meet the wait targets (see phase 4). A 1.2.0 park is refused (`needs opentpw.smarter-guests 1.2.0, not 1.3.0`). |
| 1.4.0 | 5 | Planning needs: the three rules above (`Meals` in the section, the meal times by birth id). The rules act rarely and have no measurable effect on the needs metrics (see phase 5). A 1.3.0 park is refused (`needs opentpw.smarter-guests 1.3.0, not 1.4.0`). |
| 1.5.0 | 6 | Clean and quiet paths: the wander goal weighted by litter and crowding on the cells of the original's own walk, and a rate-limited complaint (`Complaints` in the section). It lowers the crowded share of guest time and leaves litter exposure alone; about one complaint per visit at 0.1 happiness (see phase 6). A 1.4.0 park is refused (`needs opentpw.smarter-guests 1.4.0, not 1.5.0`). |
| 1.6.0 | 6b | Litter bins: the bin search of a guest with a full litter meter looks eight cells around it, not three (no new saved state). It lowers litter and litter exposure in a park whose bins stand beyond the original's three cells of where guests litter, and does nothing in a park without such bins (see phase 6b). A 1.5.0 park is refused (`needs opentpw.smarter-guests 1.5.0, not 1.6.0`). |

Version 1.6.0 is the released version: player-selectable and off by default (see [Cost and release](#cost-and-release)).

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

Version 1.3.0 of `opentpw.smarter-guests`. In the original a queued guest leaves only by its own tests (happiness below 10, a toilet need above 80, the queue limit); the 100-turn boredom branch of `GuestSimulation` is unreachable (`QUEUE-plan` section 5.3: `+508 >= +520` always holds there), so TPW has no time limit that can fire and no guest judges a queue before joining. Phase 3 keeps every candidate at the choice floor on purpose, so a long wait never makes a guest give up. This phase adds two rules for that. Both are `[EXT:smarter-guests-patience]`, live only in `SmarterGuestDecisions` / `SmarterGuestsRules`, and cost nothing with the extension off.

**Plain summary of the result.** With the rules built correctly (below) they act rarely: in `park-scenario` about two guests per seed are turned away and nobody gives up in 8 extended seeds, so **the censored p90 and p99 wait do not move toward the default's** (93 and 116 turns against the default's 73 and 108; 1.2.0 has 96 and 117). The phase 4 wait targets are **not met**, at any setting tried. An earlier build of this phase did cut the p90 to 43 and the p99 to 81 turns, but that was an artifact: it counted guests still walking to the ride as ahead of a guest that was already standing at the back, and it let a guest be refused for a ride that no other ride could replace, so it refused guests whose real wait was fine and the guests rode less (details under "What the first build got wrong"). Phase 4 as shipped is a small safety net, not a wait reduction: a queue wait cut by refusals is a cut by having guests ride less, and the guarded metric below says so.

**The base is measured, not borrowed.** `BoredomThreshold` is not used: its unit is unproven and its test cannot fire. The base is the original's own wait in the scenario baseline: boarded waits of p90 73 and p99 108 turns ([baseline](#park-scenario-extended-scale-20--60-days)). The join patience is **90 turns** (between p90 and p99) and the give-up patience **120 turns** (above the baseline's p99). Both are scaled by a stateless per-guest trait, `PatienceTrait` (`Trait( guest, 2 )`, a hash of the park seed and the birth id, the same for the whole visit): **70 to 130 percent**. Being a trait, patience needs no saved state.

**What the host adds (additive; the policy stays pure).** `IGuestDecisions.AlternativeLookAfterTurns( context, guest )` (default -1, forwarded by `ScriptGuestDecisions`) asks the host for the best alternative: `AdmissionView.BestAlternativeWait256` at every admission and `QueuePatienceView.BestAlternativeWait256` for a guest **not at the front** once it has waited the returned number of turns (the smarter policy returns the guest's own give-up turn, 84 to 156, so the scan of the park's rides is not paid before it can matter). It is the shortest phase 3 expected wait, in 1/256 turn, over the **other rides the guest could actually choose next**: open, working, with room, admitted by its excitement gate, **and not in its used or temporary history, not of the type it rode last and of an excitement it likes** (`SmarterGuestsRules.IsUnusedAdmissibleRide`, the criteria of phase 3's floor rule), or -1 for none. The scan (`GuestSimulation.BestAlternativeWait256`) is pure: no draw, no state. `QueuePatienceView.Position` is the guest's **current** place (after a move-up the new one; scripts' `leaveQueue` sees the same).

**1. Do not join.** `DecideAdmission` runs the original gates first (excitement, queue limit). A guest that passes them is refused when it is at a **ride** whose expected wait is above `90 x patience` turns **and** another ride it could choose has a wait of at most 80 percent of it. At admission the guest already stands on the queue's last cell, so **only the queue ahead counts**: guests still walking to the ride will stand behind it (`Reservations` is set to 0). The refusal is `QueueTooLong`, so the host's existing path applies: the "queue too long" thought (16), `QueueEndReason.QueueLimit` (counted as a refusal, in the insights' "queue too long" counter), the refused ride pushed into the temporary history and the guest's next choice is the host's. No happiness is lost, as in the original refusal.

**2. Give up.** `DecideQueuePatience` runs after the existing leave tests, on a turn the guest stays, never while it is called forward and never in an interlude (the host's call sites; it can run on the turn a move-up starts, which keeps the original's flow, with the new place in the view). A guest **not at the front** leaves when it has waited longer than `120 x patience` turns from `QueueJoinTurn` **and** another ride it could choose has a wait of at most 80 percent of its own remaining wait (`position x time per guest`). It thinks "queue too long" (16, the nearest original thought for "waited too long") and is dismissed by the host's existing path with `QueueEndReason.Impatient` (counted as abandoned): the original dismissal's medium happiness step is the cost, and the guest chooses again at once. The hook is still called for a guest at the front (script mods' `leaveQueue` sees it, as before); the host scans nothing for it and the smarter policy declines.

**Does rule 2 earn its place?** In the 8 extended seeds of `park-scenario` it fires **0 times** (4 times with a give-up of 50 turns; waits of the scenario's four rides stop near 117 turns, below the 84 to 156 turns of the band for any guest with a clearly quicker ride), and the level parks have no alternative at all. It is kept as insurance for a park with many rides and one badly slow one, where the guests of the slow queue otherwise wait for ever for a ride far slower than the rest; a test builds exactly that (a ride of 120 turns a guest next to quick ones) and checks that it fires. It costs nothing where it does not apply.

**Only rides, only with somewhere better to go.** Shops, sideshows and toilets are services a guest wants for its own reason, and the alternatives are rides, so neither rule concerns them. A park with one ride (the four level parks) has nothing to go to: the rules never act, and every seed of Jungle, Halloween, Fantasy and Space ends in the same state hash as under 1.2.0.

**Interactions.**
- *Phase 3 spreading (hysteresis).* The rules never change a walking guest's target. A refused guest has the ride in its temporary history and chooses by the expected-wait score, which already prefers the quicker ride that made the refusal. Restricting the alternative to rides the guest could choose is what keeps refusal chains short: the first build refused 34 percent of its refusals again (54 at the same ride, one guest 12 times in a row) because it counted rides the guest would never pick next.
- *Phase 2 (go home when price empties every choice).* Independent: a patience refusal is a queue refusal at a ride, not a price-emptied decision (`PriceTurnedAway` looks only at priced stands), and a patience exit is a queue exit, not a stall: the stall counter and the go-home rule do not move.
- *The existing leave tests* run first and win: a guest below happiness 10, with a toilet need or beyond the queue limit leaves for its own reason; the patience hook never runs for it. A broken or closed ride dismisses its head and keeps the rest waiting as before.

### Measured effect (phase 4)

`--guest-report --guest-scale extended --guest-extension smarter-guests` against the default and against 1.2.0 (the same build with both rules switched off), 8 seeds per scenario, `--guest-compare` with 3 standard errors, release build. The new guarded metric, **guest seconds per ride boarding** (`use.guestSecondsPerRide`: the window's guest time over the boardings of rides in it, the pooled form of "time between a guest's boardings"; down is good, and `--guest-compare` flags a regression), is there because the queue wait is measured over the boardings that happened: a wait cut bought with refusals leaves it unchanged or lower while guests ride less.

`park-scenario` (mean ± σ over the 8 seeds):

| Metric | Default | 1.2.0 | 1.3.0 | vs default (SE, verdict) | vs 1.2.0 (SE, verdict) |
| --- | ---: | ---: | ---: | --- | --- |
| Queue attempts | 341.500 ± 22.916 | 387.375 ± 14.609 | 394.000 ± 18.307 | 5.1 changed | 0.8 same |
| Boarded / attempts | 0.544 ± 0.020 | 0.755 ± 0.020 | 0.745 ± 0.022 | 19.2 improved | -1.0 same |
| Rejected / attempts | 0.410 ± 0.019 | 0.208 ± 0.020 | 0.219 ± 0.022 | -18.4 improved | 1.0 same |
| Abandoned / attempts | 0.026 ± 0.008 | 0.008 ± 0.004 | 0.010 ± 0.003 | -5.0 changed | 1.1 same |
| Price rejections / attempts | 0.280 ± 0.026 | 0.027 ± 0.006 | 0.025 ± 0.005 | -27.0 improved | -0.8 same |
| Wait p50, turns | 11.625 ± 2.066 | 13.500 ± 1.690 | 13.500 ± 2.204 | 1.8 same | 0.0 same |
| Wait p90, censored | 73.375 ± 7.090 | 96.125 ± 3.227 | 93.125 ± 2.532 | 7.4 regressed | -2.1 same |
| Wait p99, censored | 108.125 ± 4.643 | 117.000 ± 3.665 | 116.250 ± 2.550 | 4.3 regressed | -0.5 same |
| Guest seconds per ride boarding (`use.guestSecondsPerRide`) | 97.478 ± 3.531 | 91.157 ± 3.279 | 91.825 ± 2.833 | -3.5 improved | 0.4 same |
| Happiness, guest-time weighted | 49.007 ± 3.130 | 63.260 ± 1.160 | 63.812 ± 1.070 | 12.7 improved | 1.0 same |
| Happiness of the guests still inside | 48.217 ± 4.606 | 64.323 ± 3.247 | 65.133 ± 3.087 | 8.6 improved | 0.5 same |
| Departures per admission | 0.599 ± 0.051 | 0.413 ± 0.031 | 0.405 ± 0.033 | -9.1 improved | -0.5 same |
| Happiness-0 departures per admission | 0.528 ± 0.047 | 0.100 ± 0.024 | 0.096 ± 0.020 | -23.9 improved | -0.4 same |
| Use Gini over the rides | 0.198 ± 0.037 | 0.106 ± 0.015 | 0.107 ± 0.010 | -6.8 improved | 0.2 same |
| Revenue per admitted guest | 82.062 ± 8.502 | 103.801 ± 8.311 | 107.961 ± 7.397 | 6.5 improved | 1.1 same |
| Guests with no choice for over 50 turns | 1.250 ± 1.035 | 1.250 ± 1.488 | 1.375 ± 1.061 | 0.2 same | 0.2 same |
| Guests in the park (mean) | 32.840 ± 1.877 | 40.726 ± 0.880 | 40.803 ± 0.843 | 10.9 changed | 0.2 same |
| Admissions | 63.875 ± 0.354 | 63.875 ± 0.354 | 63.875 ± 0.354 | 0.0 same | 0.0 same |

1.3.0 is **the same as 1.2.0 within the noise on every metric** (the censored p90 is 2.1 SE lower, "same"), and its two wait targets fail against the default (+7.4 and +4.3 SE; the same as 1.2.0 failed them). Phase 2 and 3's gains hold: happiness +12.7 SE, revenue per admitted guest +6.5, price rejections -27.0, the use Gini -6.8, departures per admission -9.1. Boardings per seed are 293 (1.2.0: 293, default 186), and 16 refusals by patience happen in all 8 seeds together. Only 2 of the 8 seeds end in a different state hash from 1.2.0. `--guest-compare` against the default exits 1 because of the wait targets; against 1.2.0 it exits 0.

**Level parks.** Every seed of the four level parks ends in the same state hash as under 1.2.0, so every metric equals the 1.2.0 run (and the phase 3 table above): one ride, no alternative, no rule acts. Against the default the only regression is the one phase 3 already had (Space's median wait 33.0 to 35.3).

**What the first build got wrong** (found by an independent review, kept here so the next phase does not repeat it). The first 1.3.0 build counted the walkers (`Reservations`) as ahead of a guest that was already on the back cell, and scanned every open ride as an alternative. It measured p90 43 and p99 81 turns, "both targets pass", with a boarded share of 0.583 and 266 boardings a seed (1.2.0: 293). Those wait numbers were not a better queue: refused guests re-attempted (attempts 457 against 387), 34 percent of refusals were followed by another, and by the guest's own clock the build was worse: **104.2 guest seconds per ride against 1.2.0's 91.2** (the metric above, added because of this), a mean time from a refusal to the next boarding longer than the p99 wait. With walkers set to 0 alone the effect nearly vanished (p90 91, p99 117, boarded share 0.741). The fix is the two corrections above (queue ahead only; alternatives the guest could choose).

**Tuning (after the fixes).** Every row is the same 8 seeds. Scenario censored p90 / p99 (default 73 / 108, 1.2.0 96 / 117), boardings per seed, patience refusals and give-ups (totals over the 8 seeds; the scenario has no other `QueueLimit` rejection under these rules):

| Variant (join / give-up / alternative share) | p90 / p99 | Boardings | Refusals / give-ups | Guest s per ride |
| --- | ---: | ---: | ---: | ---: |
| 1.2.0 (rules off) | 96 / 117 | 292.5 | 0 / 0 | 91.2 |
| 90 / 120 / 80 (shipped) | 93 / 116 | 293.1 | 16 / 0 | 91.8 |
| 60 / 120 / 80 | 94 / 118 | 291.9 | 21 / 0 | 91.3 |
| 45 / 120 / 80 | 95 / 119 | 293.5 | 24 / 0 | 90.2 |
| 30 / 120 / 80 | 95 / 120 | 293.4 | 22 / 0 | 90.1 |
| 45 / 120 / 100 | 96 / 116 | 290.4 | 70 / 0 | 92.1 |
| 30 / 120 / 100 (also 20 and 10 / 120 / 100) | 96 / 116 | 289.4 | 74 / 0 | 91.7 |
| 20 / 120 / 150 | 88 / 116 | 290.8 | 182 / 0 | 93.3 |
| 60 / 90 / 80 and 60 / 70 / 80 | 94 / 118 | 291.9 | 21 / 0 | 91.3 |
| 60 / 60 / 100 | 94 / 118 | 292.0 | 47 / 0 | 91.6 |
| 60 / 50 / 100 | 94 / 118 | 289.2 | 53 / 4 | 91.4 |

No setting meets the targets (the best p90 is 88, at 20 / 120 / 150, which also refuses a guest for a ride only 1.5 times slower than the best: about 23 refusals a seed, guest seconds per ride 93.3). A lower join limit changes nothing: guests that have a clearly quicker, choosable ride rarely stand in a queue beyond 30 turns, and the tail of the scenario's waits (90 to 117 turns) belongs to guests whose rides all have the same queue. The shipped values are the baseline-derived ones; lowering them gains nothing and tighter ones only add refusals.

**Constants** (all in `SmarterGuestsRules`, all OpenTPW's own): join patience 90 turns; give-up 120 turns; patience trait 70 to 130 percent; alternative at most 80 percent of the wait.

**Cost.** The scan runs at an admission and, for a guest not at the front, from its own give-up turn (84 to 156 turns of waiting) on every update, over the park's rides (a view per ride, the host's pure estimate). `--guest-compare` reports the whole extension at 1.25 of the default's CPU per tick on this noisy machine (limit 1.10), of which phase 4's share was not isolated; the gate and the position-0 skip are what keep the scan off the common path.

**Phase 4 tests.** `SmarterGuestsPatienceTests` covers the band, the base, rule 1 (refusal with a clearly quicker alternative as `QueueTooLong`, none without one or with an alternative that is no quicker, **walkers behind the guest not counted**, the band moving the limit, the original's gates first, services and a missing estimate untouched), rule 2 (the turn it fires on, the thought, none for the front of the queue, without an alternative or a join turn, for a shop, sideshow or toilet, or when the remaining wait is short, the band moving the turn, the current place), the host (alternatives asked only when the policy wants them and only from the guest's give-up turn, nothing scanned at the front, the hook only ever running for a waiting guest, never called forward or in an interlude, an alternative being a ride the guest could choose, `IsUnusedAdmissibleRide`'s exclusions), and three constructed parks that use `ParkScenarioOptions.TwinAHoldMilliseconds` and `SingleRide` with the original's choice so queues form: a slow ride refuses guests who have a quicker one (non-empty, rides only), the same park makes a guest give up (non-empty, `Impatient`, after 84 turns), and a one-ride park, whose guests do wait longer than the patience, sees neither. `GuestMeasurementTests.GuestTimePerRideBoardingIsAGuardedHeadlineMetric` covers the new metric and its direction. There is no new saved state, so the save and resume tests are the phase 3 ones (their version strings are 1.3.0): a 1.2.0 park is refused with the usual message. The off path is unchanged: every existing pin holds.

## Phase 5: Planning needs

Version 1.4.0 of `opentpw.smarter-guests`. The plan: eat or use the toilet before a thrill when a meter is rising, pick shops on the way, and no thrill right after a meal. All three rules are `[EXT:smarter-guests-planning]`, live in `SmarterGuestDecisions` / `SmarterGuestsRules`, and cost nothing with the extension off. They are built on the real meter dynamics of the code, not on a guess of them, and the first thing the dynamics say is that **needs are slow**, which shapes the whole phase.

**Plain summary of the result.** Measured honestly the three rules **act rarely and have no measurable effect**: over 8 extended seeds of the default `park-scenario` the planning rule changes one seed's run and the other two change none; with a food shop in the scenario (24 seeds) planning and the meal rule change every other seed's run and **no needs metric moves beyond 0.9 standard errors** (the 3 SE rule: nothing is "changed"). The detour step changed no decision in any of 24 seeds, in any level park or in the default scenario. They are kept because they are small, cheap (the cost of the extension did not move beyond the noise: 0.997 to 1.02 of 1.3.0's) and defined on the original's own numbers; the page says what they do not do.

**The meter dynamics the rules stand on** (`NeedsUpdate`, the oracle-checked needs block `0xeece4`):
- The needs block runs on a guest's phase only (`turn & 3 == id & 3`), and its growth runs on a turn that is also a multiple of 16: toilet +1, hunger +2, thirst +2. A multiple of 16 has its low two bits at 0, so **only a guest whose thing id is a multiple of 4 ever grows** (one in four): +0.125 hunger or thirst and +0.0625 toilet per park turn. The other three guests in four change only by what they use. `NeedsUpdate.GrowthSteps( id, fromTurn, turns )` counts the steps (pure arithmetic; a test replays `NeedsUpdate.Apply` turn by turn and agrees).
- A guest starts with hunger and thirst under 50 and toilet under 30 (`GuestStart`), and stays about 480 turns (the exit counter, 21 days). A grower therefore gains at most 60 hunger or 30 toilet in a visit; a non-grower none. Food adds to the others (Fat raises toilet, Salt thirst).
- The levels where something happens: a **hunger or thirst byte of 100** (the needs block charges 1 happiness per phase turn, and the thoughts start above 99) and a **toilet byte above 80** (a queueing guest leaves for a ride that is not a toilet, tp.exe `0x005003AA`; `GuestSimulation.ToiletLeaveLevel`). Illness is different: a ride adds `trunc(excitement / RideVomitDivisor) * trunc((100 - hunger byte) / 20)` (`RideOutcome`), a food shop adds its `VomitEffect` (10 to 15 in the shipped data), a guest vomits at an illness byte of exactly 100, and only a vomit or a toilet (above 90) clears it.

**1. Plan** (`SmarterRuleSet.Planning`). When a guest decides, the host looks at whether any of its meters will cross a level within the longest look ahead (`SmarterGuestsRules.MayCross`: 180 turns, which is the give-up patience of phase 4 plus a default cycle: a longer commitment is not a plan, the guest would have left the queue first). Only then, and in practice only for a grower within about 22 to 24 points of 100 or 11 to 12 of 80 (11 or 12 growth steps in 180 turns, by where the multiples of 16 fall), it scans the rides the guest could choose (open, working, with room, admitted by its excitement, not used lately: `IsUnusedAdmissibleRide`) and gives the policy the **shortest commitment**: the phase 3 expected wait (queue ahead plus walkers over the observed time per guest) plus one cycle (`AttractionView.CycleTurns256`, the time per guest times the capacity, **capped at the nominal cycle of 60 turns**: the product overestimates for a ride that boards partly loaded or has stalled, and a plan that errs short only makes the rule act less; a ride whose real cycle is longer than 60 turns is read short too), in `GuestView.PlanTurns256`. In the minor decision it is the commitment of the ride the guest is already walking to. The shortest on purpose: the rule acts only when even the quickest ride leaves a need to cross. The policy then scores a **service that answers a crossing need** (a shop with a thirst or hunger effect, a relief object) as if the need had already grown to what `NeedsUpdate` gives it by then (`Planned`): a pre-emptive need, so the host's choice mechanics and the original's score decide, nothing is forced. A ride, and a need that does not cross, are scored on the guest as it is. The price rule stays on the real guest's willingness (the host computes it from the real meters, as the stand-point test does) while `PriceTurnedAway` judges "would the guest have chosen it" on the planned guest, so it agrees with the score about which stands the price emptied. The projection is the needs block's growth only; it leaves out the environment words of the guest's cell (entertainers, vomit and the like), which the same block adds and which can push either way where they are not 0, so it is approximate there.

**2. Detour** (`SmarterRuleSet.Detour`). "Near the route" is the original's own definition: the minor decision's window (the attractions within 4 x 4 cells of a guest that are nearer its target than the guest is, `GuestSimulation.MinorDecision`), so the host computes nothing new; it marks the window's candidates `AttractionView.OnTheWay`. A service on the way is scored with each need it serves **one step higher**: `DetourStep` = 10 meter points for hunger and thirst (one step of the original's table, which reads the need in tens) and `ReliefDetourStep` = 5 for a relief object (one step of the relief curve, which the original indexes in fives; 10 would move two entries), at most 100. By the tables that is a few points of score on a shop or toilet that is not new (a test walks the whole table and bounds it at 5) and none below a need of 20.

**3. Meal time** (`SmarterRuleSet.MealTime`). The host notes the park turn of a guest's meal, a use of an attraction with a hunger effect, in the extension's state, by birth id (`MealTimes`, `GuestView.MealTurn`), and forgets it when the guest leaves. Within `MealWindowTurns` = **160 turns** of the meal a ride whose illness gain for this guest is at least `MealGainLimit` = **25** is brought halfway to the choice floor (`LowerAfterMeal`: `floor + (score - floor) / 2`, so a ride stays a candidate, two rides keep their order and nothing ends above what it was; the 2 is the weakest of the original's own history divisors). Where the numbers come from: 160 is the time the hunger meter takes to grow back the 20 points a typical meal takes off (the shipped Fries Shop's `HungerEffect`; burgers and restaurants 25, ice cream 15) at the only rate there is, 2 points per 16 turns (`20 / 2 x 16`); that rate belongs to a grower, so for the other guests the window is the rule's own limit, not a measurement. 25 is a quarter of the illness meter: four such rides in a row make a guest vomit. The gain is the original's formula over the guest's own stomach (`RideOutcome.IllnessGain`, shared by the outcome and the rule), so the same ride is judged by what it would really do: a thrill ride (excitement 95) after a meal (hunger 10) adds 36; at excitement 70 it adds 28; a calm ride (20) 8. Neither number is tuned.

An honest caveat of the design, found while measuring: the stomach factor is read from the hunger meter itself, so it is exact whenever the guest rides, meal or not; what the window adds is only a recency gate, and what the rule can win is bounded by how few rides follow a meal: in the 24 food-shop seeds 0.054 of a guest's vomits per admission happen within 160 turns of a meal, out of 0.357 in all.

**What the host adds (additive; the policy stays pure).** `IGuestDecisions.UsesPlanning` (default false; forwarded by `ScriptGuestDecisions`; needs `UsesSpreading`); `GuestView.MealTurn` and `PlanTurns256` (defaults -1); `AttractionView.CycleTurns256` and `OnTheWay` (defaults 0 and false). A policy that does not plan is given the defaults and the host does none of it, so the original path does no extra work. The scan is paid only by a guest `MayCross` selects, which is a grower near a level and nobody else (`MayCross` returns false at once for an id that is not a multiple of 4). The look at the rides is made only when the planning rule is on (`UsesPlanHorizon`): a meal-only or detour-only policy pays nothing for it, and it runs inside the decision's reservation count, so the walkers are counted once.

**State and saves.** `Meals` in the `SmarterGuests` section (like `Stalls` and `Throughputs`, only in a save that records the extension: a script-mod-only save that carries any of them is refused): `(birth id, turn)` pairs in birth-id order, left out while empty (so a section from before and a park nobody ate in are as they were), hashed only while it holds an entry (marker "MEAL"). Validation: every birth id one the section holds, strictly ascending, a turn of 0 or more, no empty list; a meal after the saved park's turn is refused at restore. A 1.3.0 park is refused by the version check (`needs opentpw.smarter-guests 1.3.0, not 1.4.0`).

**Constants** (all in `SmarterGuestsRules`, all OpenTPW's own, none tuned): planning cap 180 turns; toilet level 80 and full level 100 (the original's); detour step 10; meal window 160 turns; meal gain limit 25; meal divisor 2.

### Measured effect (phase 5)

The new passive metrics (`GuestMetrics`, hash-neutral): `needs.vomits.perAdmission`, `needs.vomitsAfterMeal.perAdmission` (a vomit within 160 turns of a boarding of an attraction with a hunger effect, watched by the collector itself so it counts in any configuration), `needs.thoughts.perAdmission` (hungry, thirsty, both, need the toilet), `departures.unmetNeeds.perAdmission` (an admitted guest leaving with hunger or thirst at 100 or toilet above 80), `departures.meanIllness` and `queue.abandoned.needToilet.rate`, all with "down is good" in `--guest-compare`. Two flags make the comparison possible from one build: `--guest-rules none|all|planning,detour,meal` (**none is version 1.3.0's behaviour**; the pin of 1.3.0 holds for it; the rule set is passed to each simulation explicitly as its policy, there is no process-wide switch, and it cannot be combined with `--guest-mod`) and `--guest-food` (the scenario's fourth branch with a Burger-Shop-like food shop, 25 off hunger and 15 on illness, off by default so the baseline is the baseline). The default `park-scenario` has no food shop; a food shop is also the only way its guests can fill their stomach.

Release build, `--guest-scale extended` (20 + 60 days), `--guest-extension smarter-guests`, 3 standard errors. "Same hash" counts seeds whose end state equals 1.3.0's (8 or 24 seeds). Mean ± σ over the seeds, SE of the difference to 1.3.0 in brackets.

`park-scenario` with the food shop, **24 seeds** (the most power; the 8-seed run says the same):

| Metric | 1.3.0 (rules none) | planning only | detour only | meal only | all (1.4.0) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Seeds with the same end state as 1.3.0 | 24 | 13 | 24 | 0 | 0 |
| Vomits per admission | 0.357 ± 0.050 | 0.349 (-0.5) | 0.357 (0.0) | 0.350 (-0.5) | 0.358 (+0.1) |
| Vomits within 160 turns of a meal | 0.054 ± 0.032 | 0.053 (-0.1) | 0.054 (0.0) | 0.051 (-0.3) | 0.053 (-0.1) |
| Need thoughts per admission | 1.234 ± 0.297 | 1.220 (-0.2) | 1.234 (0.0) | 1.183 (-0.6) | 1.164 (-0.9) |
| Departures with an unmet need per admission | 0.078 ± 0.021 | 0.081 (+0.4) | 0.078 (0.0) | 0.079 (+0.1) | 0.077 (-0.2) |
| Mean illness at departure | 42.7 ± 4.8 | 43.6 (+0.6) | 42.7 (0.0) | 43.0 (+0.2) | 42.0 (-0.5) |
| Queue exits for the toilet, per attempt | 0.000 | 0.000 (-0.2) | 0.000 | 0.000 (-0.2) | 0.000 (-0.2) |
| Happiness, guest-time weighted | 64.27 ± 1.58 | 64.51 (+0.5) | 64.27 (0.0) | 64.23 (-0.1) | 64.34 (+0.1) |
| Revenue per admitted guest | 144.8 ± 9.7 | 145.2 (+0.1) | 144.8 (0.0) | 145.5 (+0.3) | 145.2 (+0.1) |
| Boarded share of queue attempts | 0.860 ± 0.023 | 0.862 (+0.4) | 0.860 (0.0) | 0.869 (+1.6) | 0.867 (+1.1) |
| Departures per admission | 0.198 ± 0.043 | 0.201 (+0.2) | 0.198 (0.0) | 0.200 (+0.2) | 0.205 (+0.6) |
| Guest seconds per ride boarding | 101.9 ± 3.8 | 101.0 (-0.8) | 101.9 (0.0) | 101.4 (-0.5) | 101.7 (-0.2) |

Every verdict is "same"; `--guest-compare` exits 0 for each pair. The default `park-scenario` (no food shop, 8 seeds): planning changes 1 seed's end state, detour and meal none, every metric equals 1.3.0 to the third decimal (need thoughts 1.622 to 1.618), and so do the level parks Jungle, Hallow and Fantasy (all 8 seeds of each end in 1.3.0's state hash). In the Space park, which has a food shop, the meal rule changes 7 of 8 seeds and nothing beyond 1.9 SE moves (need thoughts +1.9, unmet needs +1.2, departures per admission +0.4, mean illness -0.3, happiness -0.3). The level parks have no vomits at all in these runs (one ride, few uses): the meal rule has nothing to prevent there.

**How often the rules act** (a one-off count with a throwaway harness that scored every candidate with 1.4.0 and with the rules off, 8 seeds x 80 days; the meal count is from the first build, before the review's changes to the lowering and the cycle): without a food shop 26,438 scores, 475 of a service whose guest had a plan, **8 changed by planning**, 147 of a service on the way, **1 changed by detour**, none by the meal rule; with the food shop 31,900 scores, 630 with a plan, **87 changed by planning**, 245 on the way, **1 changed by detour**, about **600 ride scores lowered by the meal rule**. A changed score is not a changed decision: the minor decision already retargets to **any** open attraction in its window with a score above 0 (`BeatsInMinorDecision` starts from a best of 0; the original's route test only wants the candidate nearer), so a shop on the way is picked for its presence and the detour step can only reorder the window's candidates among themselves. That is why it never changes a decision: the original already has the behaviour the plan wanted ("shops on the way"), and what is left for a bonus to do is nothing. It is the first candidate to drop if the extension needs to be smaller.

**Why the meal rule cannot show a gain here.** It moves guests from a thrill ride to a calmer one only in the 160 turns after a meal, which is about a third of a visit, and only when the thrill ride is one of the two or three rides the guest would take; a calmer ride still adds illness (6 x 4 = 24 against 36), so a vomit is a little later, not avoided. Total illness is the sum over all rides, and a vomit needs it to reach exactly 100: the rule lowers the rise for about 4 percent of the ride scores it sees (the 600 of about 15,900 above) by a third, a few percent of the total, below the seed noise (σ 0.05 on 0.35 vomits per admission). A larger effect needs a different rule (a guest who uses a toilet when its illness is above 90, which clears it, would remove vomits outright), which is not phase 5's.

**Cost.** Phase 5 costs nothing that the noise lets us see. A Stopwatch over `ParkScenario` runs of 250 in-game days (3 seeds, 7 repeats each, the median of 21, Release, the machine otherwise quiet) gives, without a food shop, original 105.7 ms, 1.3.0's rules 136.1 ms, 1.4.0 135.8 ms (**0.997** of 1.3.0), and with the food shop 118.5, 179.1 and 182.5 ms (**1.019**; the best runs are 174.1 and 173.1 ms, 0.994; two earlier passes of the same measurement gave 1.00 and 1.00, so the run-to-run noise is about two percent). The ratio of the whole extension to the original in this scenario is 1.29 (1.51 to 1.54 with the food shop), above the 1.25 of the earlier noisy measurement; it is not a per-guest figure, because the extension keeps about 40 guests in the park where the original keeps 33 (happier guests stay longer), so the same simulated days hold more guests to update. The phase 6 limit of 1.10 is the extension's as a whole and is not met by phases 2 to 4 either; phase 5 does not make it worse. The scan is paid only for a grower near a level, and nothing per guest per turn is new: no path search, one `GrowthSteps` computation per decision and a score for a service that does arithmetic on three meters.

**Phase 5 tests.** `SmarterGuestsPlanningTests` (29) covers the growth arithmetic against `NeedsUpdate.Apply` turn by turn, the levels, the plan (a crossing need is read grown for the service that answers it and nothing else, no plan and no crossing leave the guest alone, the score is the original's over the projected guest), the detour (one step, clamped, only the need the shop serves, at most a few points over the whole table, never lowers a score), the meal rule (window edges, the gain against `RideOutcome.Apply` over a grid, the limit, the lowered score keeps the floor and is monotone), the rules as switches, `MealTimes` (order, replace, forget, no id), the hash, the save (round trip into a fresh instance, left out while empty, bad data refused: unknown or repeated birth ids, a negative turn, an empty list, a meal after the park's turn) and the host (the meal turn and plan only for a policy that plans, notes at the food shop and forgets at departure, a plan only for a grower near a level, `OnTheWay` in the minor decision only). `GuestMeasurementTests` pins the food-shop scenario with the extension on (`ScenarioFoodSmarterFixedRunHash`) and shows the rules off reproduce 1.3.0's pin; the resume test of the extension now includes meal times.

## Phase 6: Clean and quiet paths

Version 1.5.0 of `opentpw.smarter-guests`. The plan: wander goals weighted by litter and crowding, and "disgusting path" and "too crowded" thoughts with a small cost. Both rules are `[EXT:smarter-guests-paths]`, live in `SmarterPathComfort` and cost nothing with the extension off.

**Plain summary of the result.** The rule **measurably lowers the share of guest time spent in crowds** (about 20 percent less in the busy and dirty scenarios, beyond 5 standard errors; 10 to 25 percent in the four level parks, 2.2 to 3.7 t) **without a measurable change of happiness, revenue, admissions or stuck guests in the scenarios** (each within 1.4 SE, except guests in the park, -3.0 SE in the busy one, 76.1 against 77.2) and with **one real cost in the level parks: the Jungle park's guest-time happiness is 0.8 points lower (54.2 to 53.4, t = -4.8)**; the other three are within the noise. It does **not** reduce litter exposure (steps onto litter per visit and the share of time on litter are unchanged in every scenario and park): where a guest meets litter is decided by its route to a ride or shop, not by where its idle wander ends. The complaints are rare (at most one per 480 turns, one cooldown for both kinds) and cost 0.1 happiness, so they do not drive the result.

**How the original wanders, and where the rule hooks in.** The destination search (`GuestWander.Pick`, tp.exe `0x4F9490`) draws a step count (`rand % 5 + 1`) and walks that many random steps over the cell links, choosing each step with one draw (`rand & 3`) and finishing with two sub-cell draws. The number of draws depends on the path. The rule does **not** change that: the host runs `Pick` exactly as before, **with the same draws in the same order**, and also keeps a copy of the cells the walk stood on (`GuestWander.Pick`'s `trail`, at most `TrailCapacity` = 8, the goal last). The policy chooses among cells the host enumerated and draws nothing. (The off-path search of a start cell without links stays host-only and is not offered.)

**1. Goal weighting** (`SmarterPathComfort.Decide`, hook `IGuestDecisions.ChoosePath`, asked before `ChooseWanderGoal`, so script mods chain over the result). The candidates are the cells of the original's walk, the linked path cells beside its goal that the walk did not visit (at most 4) and the goal itself, last. The host builds a `PathCellView` for each and for the guest's own cell: `LitterUnits` (the litter in the cell and the eight around it: 1 for a piece, 2 for vomit or a stinkbomb), `Crowd` (the guests in the cell, from the per-cell lists `occupants` the host keeps) and `NearCrowd` (the guests in the eight cells around it, the crowd a guest walks into on its way). A candidate costs `LitterUnits x 5 + Crowd x 2 + NearCrowd x 1`. The goal moves to the cheapest candidate (ties go to the later one, nearer the goal) when it is at least `SwitchMargin` = 2 cheaper, i.e. one more guest in the goal's own cell. The goal's sub-cell bytes stay the original's. `Litter` and `Crowding` count only their own kind of cost (`--guest-rules litter`, `crowding`, `paths`).

**2. Complaints.** A guest standing where the litter in its nine cells reaches `LitterComplaintUnits` = 2 complains of litter with the original's **"Too messy"** (id 7). A guest in a cell of `CrowdComplaintGuests` = 5 or more complains of a crowd with the existing **"Dissatisfied"** (id 6; the original has no crowd thought, and a new id would need a sprite, a THOUGHTS.str entry and snapshot validation). Litter wins when both apply. A complaint costs `HappinessCost` = the small happiness step over 50 (`PeepInfo.SmallHappinessChange` = 5, so 0.1) and a guest complains at most once per `ComplaintCooldownTurns` = 480 turns (a whole visit, the exit counter). The cooldown is the guest's last complaint turn (`GuestView.ComplaintTurn`, from the extension's state); a guest without a birth id does not complain.

**Where the constants come from.** `LitterComplaintUnits` 2 is the original's "Too messy" share (`GuestThoughts.MessyShare` = 0.2 of the path cells of a 4 x 4 block) over the nine cells around a guest (1.8, rounded up). `CrowdComplaintGuests` 5 is one more than a queue cell holds (`RideVisitorBridge.PositionsPerCell` = 4). The litter weight 5 and crowd weight 2 cross the two complaint levels (2 x 5 = 5 x 2). The **margin 2, the neighbour weight 1, the candidate set, the complaint cost 0.1 and the cooldown 480 were chosen by the sweep below**, not derived; the sweep is reported in full.

**The sweep** (24 seeds, busy and dirty scenarios, 3 SE; all with the complaints off unless stated; baseline is 1.4.0's rules). Crowded share of guest time, busy scenario (baseline 0.062): the first version (the walk only, margin 10, goal cell only, complaints 0.5 at 240 turns) 0.058 (-1.9 SE); margin 10 with no complaints 0.057 (-2.3); plus the goal's neighbours and the crowd around a candidate 0.058 (-2.3); the same with margin 3 0.048 (-8.7); margin 1 0.046 (-9.4); margin 3 with a litter weight of 8 0.048 (-7.5) and **no better litter exposure**. Margin 1 and 3 lowered happiness by 0.2 to 0.6 points (up to -2.1 SE in the dirty scenario); margin 2 (shipped) is the largest margin that keeps most of the gain with happiness inside the noise (-0.4 and -0.7 SE). Complaints at 0.5 and every 240 turns cost up to 1.5 happiness points (-1.5 SE dirty); 0.1 and 480 turns cost nothing the noise shows.

**Before and after the strengthening, the two red flags of the first version, and a bug found in review.** The first version (walk cells only, margin 10) did almost nothing useful: crowded share -1.9 SE in busy and -1.8 in dirty, litter flat, happiness -0.4 to -1.5 SE from the complaints. Two things in its level-park run looked like bugs and were investigated, on the first version: (a) **Fantasy, stuck guests 54 to 95 and admissions 97 to 131.** In a diagnostic run (about 110 moves in the Fantasy park) none of the moved goals left the guest stuck (0 of 116, against 0 of 3,122 unmoved) and none was the guest's own cell; the route to a moved goal was as short as to an unmoved one. It was **not a bug**: admissions in this park are chaotic, and moving only the arrival timer of the **rule-off** park from 17 to 25 gives 126, 120, 105, 103 and 84 admissions and 94 to 50 stuck guests. With the final rule, over five arrival timers, admissions change by -4.8 on 114 (t = -1.7) and stuck guests by -3.6 on 78 (t = -2.0): not beyond the band. (b) **Busy scenario, revenue per admitted guest -2.4 to -3.9 SE under the crowding rule.** It did not reproduce after main's per-cell list change and with the final rule revenue is +0.2 SE (busy), -0.7 (plain) and +1.4 (dirty): seed noise on the old trajectories. (c) **The dirty-scenario crowd drop (-3.0 SE)** was the rule working weakly; the final rule makes it systematic. **Review then found a real bug in the strengthened rule**, which the diagnostic above (made on the first version) could not show: the new "linked cells beside the goal" candidates and the walk's own cells could be **the guest's own cell** (a walk passes back through its start, and a goal next to the start offers it), and the asking guest counted itself in the crowd of its own cell (+2) and of the cells around it (+1), which biased the weighting towards staying put. Fixed: no candidate but the original's goal may be the start cell, only path cells are offered (a walk from a queue cell can enter queue cells), and the asking guest is taken out of every count; a test asserts no move names the start cell. Everything below was measured **after** the fix.

**What the host adds (additive; the policy stays pure).** `IGuestDecisions.UsesPathComfort` (default false; forwarded by `ScriptGuestDecisions`), `ChoosePath` (default: keep the goal, no complaint), `GuestView.ComplaintTurn` (default -1), `PathCellView`, `WanderPathView` (a ref struct over the host's stack), `PathDecision` and `PathComplaint`; `IGuestObserver.PathComplained` and `WanderGoalMoved` (default no-ops). The host checks the verdict (a walk index outside the candidates, a complaint without a thought or with a negative, non-finite or larger-than-big-step cost, a thought without a complaint) and throws naming the policy. A policy that does not ask pays nothing. With the rules on, a wander reads at most 13 candidates (a candidate repeated by the walk is read once): nine litter reads (skipped while the park holds no litter) and nine cell-list lookups each, no scan of the guests.

**State and saves.** `Complaints` in the `SmarterGuests` section (birth id and park turn, the same bookkeeping as `Meals`, birth-id order, left out while empty, validated, refused after the park's turn, forgotten when the guest leaves, hashed under the marker "CMPL" only while it holds an entry). The litter map belongs to the economy's save and the cell lists are rebuilt from the guests on a restore, so a resumed park sees the same litter and crowds and continues like the uninterrupted one with complaints in flight. A 1.4.0 park is refused by the version check (`needs opentpw.smarter-guests 1.4.0, not 1.5.0`).

### Measured effect (phase 6)

New passive metrics (`GuestMetrics.Paths`, hash-neutral, sampled every tick for every paid guest in the park): `path.littered.timeShare`, `path.litterNear.timeShare`, `path.litterEncounters.perAdmission`, `path.crowd.guestsPerOccupiedCell`, `path.crowded.timeShare` (the share of path guest time spent in a cell of 5 guests or more), the complaint and moved-goal counts per admission and `path.litter.meanCells`. The first five and the last are "down is good" in `--guest-compare`. `--guest-rules` gains `phase5` (version 1.4.0's behaviour), `litter`, `crowding` and `paths`; `none` is still 1.3.0.

The scenario needed litter, so `ParkScenario` has a **litter variant** (`ParkScenarioOptions.Litter`, `--guest-litter`; off by default so the baseline and its pins are untouched): shops add `LitterEffect` to the litter meter (5, the shipped value, `--guest-litter-effect`), guests drop it and vomit on the paths, and a handyman proxy removes one littered cell every `HandymanTurns` park turns (`--guest-handyman-turns`). `--guest-scenario-arrivals N` sets the arrival timer. "plain" is the default scenario, "busy" the litter scenario with the food shop, an arrival timer of 10 (77 guests in the park instead of 41) and a sweep every 40 turns, "dirty" the same with a sweep every 400 turns and a litter effect of 25.

Release build, `--guest-scale extended` (20 + 60 days), 24 seeds, 3 standard errors, after merging main's per-cell list order and after the start-cell fix. Mean over the seeds, 1.4.0's rules (`--guest-rules phase5`) to 1.5.0, difference in standard errors in brackets:

| Metric | plain | busy | dirty |
| --- | ---: | ---: | ---: |
| Crowded share of guest time | 0.011 to 0.009 (-2.8) | **0.062 to 0.048 (-6.6)** | **0.060 to 0.049 (-5.4)** |
| Steps onto litter per visit | 0 | 2.41 to 2.27 (-0.6) | 13.9 to 13.5 (-0.4) |
| Littered share of guest time | 0 | 0.030 to 0.028 (-0.6) | 0.206 to 0.192 (-1.6) |
| Happiness, guest-time weighted | 63.39 to 63.24 (-0.3) | 61.23 to 61.58 (+1.0) | 61.42 to 61.40 (-0.1) |
| Revenue per admitted guest | 110.3 to 108.4 (-0.7) | 127.8 to 128.3 (+0.2) | 125.3 to 127.8 (+1.4) |
| Guests in the park | 41.3 to 41.4 (+0.3) | 77.2 to 76.1 (-3.0) | 76.6 to 76.2 (-1.1) |
| Stuck guests | 1.7 to 1.8 (+0.4) | 3.9 to 4.3 (+0.5) | 3.5 to 3.4 (-0.2) |
| Goals moved per admission | 6.0 | 8.3 | 8.4 |
| Litter / crowd complaints per admission | 0 / 0.20 | 0.40 / 0.46 | 0.90 / 0.27 |

**Level parks** (the four themes, extended scale; a level seed ends in the same state, so five arrival timers 14, 17, 20, 23 and 26 stand in for seeds, a paired difference per timer and its t value, 4 degrees of freedom):

| Park | Crowded share, 1.4.0 | Difference (t) | Happiness | Admissions | Stuck guests | Revenue per guest |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Jungle | 0.502 | **-0.051 (-3.3)** | **-0.8 (-4.8)** | +1.6 (+2.4) | +1.4 (+0.4) | -0.2 (-2.0) |
| Hallow | 0.106 | **-0.028 (-2.2)** | -0.5 (-0.7) | -1.2 (-2.4) | -0.8 (-0.3) | +1.9 (+1.0) |
| Fantasy | 0.275 | **-0.098 (-3.7)** | -0.2 (-0.2) | -4.8 (-1.7) | -3.6 (-2.0) | +0.4 (+0.7) |
| Space | 0.372 | **-0.066 (-2.8)** | -0.1 (-0.1) | +0.6 (+0.9) | +2.6 (+0.8) | +0.1 (+0.2) |

Litter exposure in the level parks (about 1 to 3 steps per visit, 1 to 2 percent of guest time) does not fall: Jungle's steps onto litter rise 1.29 to 1.51 (t = +2.2) and its littered share by 0.001 (t = +3.7), the other parks' change is within 2.1 t either way. Complaints in the level parks are 1.2 to 1.5 per admitted guest (a guest there stays longer than the 480-turn cooldown; one cooldown for both kinds), 0.1 happiness each. The Jungle happiness drop (the one real cost, 1.5 percent) is not explained: the complaints alone cost 1.5 x 0.1 = 0.15 points per guest and the rest of it is the changed wandering; a Jungle-specific rerun with the complaints off was not made.

**What it does not do, and open doubts.** It does not lower litter exposure, which is the other half of the plan's aim: only a change of the guests' routes (the original's path search) could, and that is not a wander goal. The crowd complaint reuses "Dissatisfied", so the Top 3 Thoughts row counts it as that. The per-park figures rest on five arrival timers each (4 degrees of freedom): the crowd effect is consistent in sign across all four parks but its t values are 2.2 to 3.7, so it is established less firmly in the parks than in the 24-seed scenarios.

**Phase 6 tests.** `SmarterGuestsPathsTests` (27) covers the constants, the litter kinds, the pure rule (a clean walk keeps the goal, the margin boundary, ties, one kind of cost at a time, the complaint thoughts, the cost, the cooldown at its boundary, no id no complaint), the original's walk (the same goal and draws with and without the trail over 200 starts; a long walk reports its last cells), the host (only a policy that asks gets the views; a policy that keeps every goal leaves guests, draws and state as they were; the candidates are the walk, its goal's linked neighbours and the goal last, never the guest's own cell and all path cells (a walk through the start and off the path is fed in directly); the sub-cell bytes of a moved goal are the original proposal's; a cell's crowd and the crowd around it are the guests standing there, the asking guest not counted; the litter around a cell is read by kind; a complaint costs happiness, thinks the thought, notes the turn; a goal move names a candidate and keeps the original's bytes; an invalid verdict is refused naming the policy; the shipped rules complain and move goals in a crowded dirty park and replay the same, each rule on its own), the scenario variant, the complaint times (order, replace, forget, hash marker, round trip into a fresh instance, a restored park sees the same crowds and litter, bad data refused, absent while empty) and the report. `AResumedParkContinuesLikeTheUninterruptedOneWithTheExtensionOn` includes a complaint in flight and one after the save point.

## Phase 6b: Litter, routes and bins

Version 1.6.0 of `opentpw.smarter-guests`. Phase 6 left one condition open: the path rule lowered crowding but not litter exposure, because where a guest meets litter is set by its route to a ride or shop. Two ways to a measurable litter effect were built and measured with the same protocol (24 seeds, 3 standard errors, the litter scenarios, the four level parks at five arrival timers): **litter-aware routes** and **bins that guests find from further away**. Only the second is kept.

**Plain summary of the result.** The bin rule **measurably lowers litter where it can act**: in the very dirty scenario with four bins along the street, litter on the paths falls by 27 percent (-6.9 SE; -11.7 SE over 64 seeds), the littered share of guest time by 31 percent (-7.5 SE) and steps onto litter per visit by 34 percent (-6.2 SE), with happiness, revenue, admissions and the guests in the park within 1 SE; stuck guests rose 3.4 to 4.3 at 24 seeds (1.7 SE) and 2.3 SE at 64 seeds but only 3.76 to 3.99 (1.0 SE) at 200 seeds, so it is most likely seed noise (see below). It **does nothing in the three other scenarios, the shops-layout of bins, and the four level parks** (every metric identical to 1.5.0), because there the guests either never fill their litter meter (their litter is vomit, which no bin takes) or already stand within three cells of a bin. So the extension has a measurable litter effect in a park whose guests fill their meters and whose bins stand more than three cells from where they litter, and none in the level parks as the gate builds them (no bins, vomit only). Litter-aware routes lower exposure by 8 to 12 percent at most (-0.9 and -2.5 SE) and only on a three-cell-wide street; on the one-cell-wide streets of every scenario and level park they find no detour at all.

### What was built and why the routes were not kept

**1. Litter-aware routes (prototype, commit `61cfa902`, not kept).** The original's route search is not a weighted shortest-path search. It is a line-and-boundary tracer (`WalkSearch`, tp.exe `0x511420`: a four-connected line towards the goal, boundary tracers at a wall, corner shortcuts, a refinement), so a per-cell cost cannot be added to it without replacing it, and the walk searches again each time it has used its five buffered waypoints. The prototype therefore left the original search untouched and refined its result: for the first five segments whose straight walk crosses litter, a Dijkstra search over the four-connected path cells replaced the segment by the cheapest walk (one cell of walking per step plus 3 per litter unit on a cell entered), at most 4 cells longer than the Manhattan distance, ties broken by the heap order with an insertion counter. The policy supplied the cost per littered cell and the bound (`IGuestDecisions.RouteCellCost`, `RouteMaxExtraCells`) and the host ran the search, only when the park held litter. With the rule off the route is byte-identical (the hook is a null delegate).

| Where | Route segments that crossed litter | Detours found | Effect on litter exposure |
| --- | ---: | ---: | --- |
| Busy and dirty scenario (one-cell street) | 4,995 (busy) | 0 | none (every metric identical) |
| The four level parks (one-cell paths at the rides; arrival timer 17) | 137, 69, 83, 121 | 0 in each | none; the littered cell is the only way, or the goal itself |
| Busy, three-cell street (`--guest-wide`) | 5,401 | 1,010 | steps onto litter per visit 2.353 to 2.149 (-0.9 SE), littered share 0.033 to 0.030 (-0.8), litter cells 0.954 to 0.885 (-0.5) |
| Dirty, three-cell street | 37,865 | 7,451 | steps onto litter 12.81 to 11.19 (-2.5 SE), littered share 0.196 to 0.194 (-0.3), litter near 0.268 to 0.298 (+1.7), litter cells 6.99 to 7.20 (+0.7) |

Happiness, revenue, admissions and stuck guests stayed within 0.8 SE (the wide scenarios) and a larger cost per unit (6, 12) or bound (8, 16) changed nothing, so the limit is the streets and the goals, not the constants: most litter lies on the cells guests stand on (queue ends, shop stand points, entrances: 29,696 of the 37,865 segments could not be improved), not on cells they pass over. No metric reached the 3 SE bar, so the rule was removed from the shipped code (the prototype stays in the history, commit `61cfa902`, with `--guest-rules routes` and `--guest-wide`).

**2. Bins (kept).** The original already carries litter to bins: a guest whose litter meter byte is at least 90 and who decides in state 6 looks for a bin (`SeekBin`, tp.exe `0x00500DC0`) whose cell is closer than 3 cells (squared distance below 9), walks to the cell in front of it (state 9) and throws the litter in; without one it drops `1 + rand % 5` on its own cell. The rule changes one number: the radius is **8** (`SmarterBinCarry.SearchRadius`; `IGuestDecisions.UsesBinCarry`, `BinSearchRadius`, accepted by the host from 0 to `GuestSimulation.MaxBinRadius` = 16, otherwise it throws naming the policy). Everything else is the host's, as before: the bin list, the nearest-bin choice (squared distance, strict), one route request per closer candidate, the walk, the throw and the drop with its draw. **Random draws, and what a binning guest skips.** The decision (`GuestDecision.Decide`) tests the litter first and returns `ToBin` as soon as a bin is chosen, so a guest that now goes to a bin ends that turn's decision there: it takes none of the later tests and none of their draws (leave, watch or perform, prank, ride choice, wander), exactly as the original does for a bin within three cells; those tests run again when the walk ends and the guest decides again. The drop's own draw (`random.NextWord() % 5`) is taken only when the guest drops. With no bin in reach the draws are the original's, in the same order, and a park without bins asks nothing (the host reads the radius only when `Bins` is set and a guest's meter is full). **The route bound:** the 8 cells are a straight line to the bin's cell, and the route to the cell in front of it is not bounded by that (a bin across a hedge or a river can be a 30-cell walk). With the rule on, a candidate beyond the original's reach whose route (the host's `Grid.FindPath`, already searched for the candidate) is empty or longer than `SmarterBinCarry.MaxRouteCells` = 16 (twice the radius; `IGuestDecisions.BinMaxRouteCells`) is skipped **before the route request**, so it retargets nothing; a bin within three cells is never skipped, as in the original.

**Where the constant comes from.** The radius 8 was chosen by a sweep (the very dirty scenario with bins, 24 seeds; litter cells, litter share, steps onto litter, difference in SE): 3 (the original's) nothing; 5 -2.1, -1.4, -1.9; 6 -3.0, -3.2, -2.5; **8 -6.9, -7.5, -6.2**; 12 the same as 8 (the four bins stand 4 to 15 cells apart: a larger radius reaches no further bin). It is therefore tied to that layout; in a park with bins further apart it would reach fewer. The walk is the original's bin walk, a few park turns out of the guest's way.

### Measured effect (phase 6b)

New passive metrics (`GuestMetrics.Paths`, hash-neutral): `path.litterDropped.perAdmission` (a guest's litter that landed on a free cell) and `path.litterBinned.perAdmission`; `IGuestObserver.LitterDropped` and `LitterBinned` (default no-ops). New scenario variants: `--guest-bins` (four bins beside the street, columns 5, 20, 24 and 34; the guests litter on the shop branches and the street, 3 to 15 cells away), `--guest-bins-shops` (three bins beside the shop branches, within three cells of every shop stand point), `--guest-level-bins N` (a proxy bin on a path cell about every N cells in the level parks) and `--guest-rules phase6` (version 1.5.0's rules, the baseline here). The scenarios: "busy" has a litter effect of 5 per shop use and a handyman every 40 turns, "mid" an effect of 12 and a handyman every 120, "dirty" an effect of 25 and a handyman every 400; plain has no litter. Release build, `--guest-scale extended`, 24 seeds, 1.5.0's rules to 1.6.0, difference in SE:

| Scenario | Littered share | Steps onto litter | Litter cells | Dropped / binned per admission | Happiness | Revenue | Guests in park | Stuck guests |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| plain, busy, dirty (no bins) | identical | identical | identical | identical | identical | identical | identical | identical |
| busy, four bins | identical (no guest fills its meter) | | | 0.185 / 0 | identical | identical | identical | identical |
| mid, four bins | 0.106 to 0.104 (-0.1) | 7.20 to 7.15 (-0.1) | 3.20 to 3.16 (-0.1) | 0.145 to 0.142 / 0.005 | 61.33 to 61.30 (-0.1) | 128.1 to 128.7 (+0.3) | 75.8 to 75.8 (+0.1) | 3.3 to 4.1 (+1.5) |
| **dirty, four bins** | **0.192 to 0.133 (-7.5)** | **13.51 to 8.90 (-6.2)** | **6.32 to 4.60 (-6.9)** | 0.141 to 0.125 (-3.5) / 0 to 0.297 | 61.40 to 61.22 (-0.5) | 127.8 to 128.1 (+0.1) | 76.2 to 76.2 (-0.1) | 3.4 to 4.3 (+1.7) |
| dirty, bins at the shops | identical (bins already within three cells) | | | 0.138 / 0.205 in both | identical | identical | identical | identical |

The very dirty scenario is robust to the load: at arrival timers 8, 12 and 14 (24 seeds each) litter cells fall 6.4, 4.9 and 6.8 SE, the littered share 8.3 SE each time, steps onto litter 6.5, 5.3 and 4.9 SE; happiness -0.7, -0.9 and 0.0 SE, revenue -0.5, +0.3, -0.5, guests in the park +0.4, +0.5, -0.1. Over **64 seeds** (the same scenario) litter cells fall 11.7 SE, littered share 12.8, steps onto litter 9.9; happiness -0.04 points (-0.2 SE), revenue +0.6 (+0.5), admissions -0.05 (-0.2), **stuck guests 3.45 to 4.31 (+2.3 SE)**.

**The stuck-guest count and happiness (investigated).** Hypothesis from review: a guest walks to a street bin farther from the rides, which lowers its scores under the choice floor. Checked by logging the cell and the turns since the last bin throw when a guest became stuck (24 seeds, the very dirty scenario): in both 1.5.0 and 1.6.0 the stuck guests stand at the far end of the street (x 30 to 38, beyond the toilet at 28, 15 of 24 and 25 of 34 logged), and only 3 of 34 became stuck within 150 turns of a bin throw. Removing the farthest bin (column 34) changed nothing (it is never used), a fifth bin next to the shop branches gave +2.1 SE again, and the longer route bound changes nothing in this scenario (a street bin's route is 6 to 9 cells, below 16: the results with and without it are identical). The rise **does not grow with more seeds** (+1.7 SE at 24, +2.3 SE at 64, **+1.0 SE at 200**: stuck guests 3.755 to 3.985; litter cells -18.4 SE, littered share -21.0 SE and steps onto litter -17.2 SE at 200 seeds; happiness 61.24 to 61.09, -1.3 SE; revenue +0.5, guests in the park +1.0 SE). It is most likely the divergence of seed trajectories once any guest uses a bin (stuck counts vary by several guests per seed), not a side effect I could find, so nothing was changed for it; at 200 seeds it is below 1.1 SE. Happiness is 0.0 to -0.35 points in five 24-seed runs and -0.15 at 200 seeds (-1.3 SE), a possible small cost of the walks to bins (a guest that carries its litter eight cells spends a few turns not choosing a ride).

**Level parks** (five arrival timers 14, 17, 20, 23, 26, 4 degrees of freedom): every metric of all four parks is identical to 1.5.0, with and without a proxy bin every 10 path cells (`--guest-level-bins 10`: no guest of a level park ever fills its litter meter; its 0.1 to 0.3 dropped pieces per admission are vomit, which no bin takes). The gate's parks place no bin, and the guests' litter there is vomit: **the rule has no effect on the level parks and cannot have one while their guests litter only by vomiting**.

**The Jungle happiness drop of phase 6.** The drop (-0.81 points, t = -4.8, timers 14 to 26) reproduces exactly on this build. Rerun with the complaints switched off (an experiment, not shipped) it is **-0.92 (t = -4.0)**: the complaints are not the cause, they even cost a little less (+0.11). Each rule on its own, complaints off: litter alone +0.01 (t = 0.0), crowding alone -0.11 (t = -0.3); both together -0.92 (t = -4.0). A replication on six other timers (12, 15, 18, 21, 24, 28) gives -0.30 (t = -1.4) with and -0.36 (t = -1.6) without complaints; over all eleven timers -0.53 (t = -3.4). So the drop is **real but smaller than the first five timers showed (about 0.3 to 0.5 points, 1 percent)**, it comes from the changed wandering of the two goal rules together and not from the complaints, and its mechanism is still unexplained; the Jungle park (half the guest time in crowds) is where the goal rule moves most goals.

**Cost.** CPU per guest-turn: see [Cost and release](#cost-and-release); the rule adds no work to a park without bins or litter-full guests.

**What it does not do, and open doubts.** It does not touch litter that is not carried: vomit (2 units each, the larger part of litter in the level parks), drops by guests with no bin within eight cells, and pranks. The effect is the size of the gap between where guests litter and the original's three cells: the same four bins that never catch a guest at three cells catch 0.3 per admission at eight, and a park with bins at its shops has nothing to gain. The radius is tied to one layout. The stuck-guest rise above is unexplained. The scenario's bins are proxies (a bin is an object in the game; `Level.LitterBins`), the walk and the throw are the host's own and are tested, but the bin script (`TriggerBin`) is not exercised by the scenario.

**Phase 6b tests.** `SmarterGuestsBinsTests` (12, adding the exact squared distance of 64 and a bin behind a wall that is 5 cells away in a straight line and 20 on foot): the constants and the policy's switches (only a policy with the rule asks; 1.5.0's rules do not; the version), a bin six cells out is carried to by 1.6.0 and not by the original or 1.5.0, the radius boundary (exclusive, as the original's), a park with no bin in reach hashes like 1.5.0, a park without a bin list never asks, a radius outside 0 to 16 is refused naming the policy, the carried litter stays in the meter until the throw, the scenario with bins replays the same and bins only under the rule, and a park resumed while a guest walks to a far bin continues like the uninterrupted one. `GuestMeasurementTests.ParkScenarioWithBinsAndTheSmarterGuestsExtensionOnIsPinned` pins the bin scenario (see [DETERMINISM.md](DETERMINISM.md)).

## Cost and release

Phase 6 is the last phase before the extension is released to players, so it also settles the CPU gate of the plan: **at most 1.10 times the original guests' CPU**.

**How it is measured.** `--guest-report --guest-cost` (`GuestCost`, Release build) runs the same `ParkScenario` for the original guests and for the extension's rule sets (1.3.0, 1.4.0, 1.5.0) over 250 in-game days (85,709 ticks), 3 seeds x 11 repeats = 33 runs per variant, the variants interleaved and their order rotated inside each repeat, one unmeasured round to warm the JIT, a full GC before each run. `ParkScenario.RunTimed` is `Run` with the caller's clock around the guests' own part of each tick (the guest tick or the turn's sweep over the thing pool and the arrival update; the ride scripts and the admission are left out; a test-time check proves it runs the same scenario). It prints medians of the total time, the process CPU time, the guests' own time, the **guests in the park** and the time and allocation **per guest-turn** (a guest in the park for one park turn). Two things matter for reading it. The extension keeps more guests in the park than the original (happier guests leave later: in the default scenario departures per admission fall from 0.60 to 0.41 and guest-time happiness rises from 49.0 to 63.7, with the same 63.9 admissions), so a total ratio mixes the cost of a guest with the number of guests. And the machine is shared: these runs had a load average of 6 to 8 from other work, which is why variants are interleaved and the two rounds below are shown as two rows; the noise between rounds is about 0.01 to 0.02 in a ratio.

**Before the optimisation** (the build at the commit before it), 1.5.0 against the original, two rounds:

| Scenario | Guests in the park (original / 1.5.0) | Total time | Guests' own update | Per guest-turn | Allocation per guest-turn |
| --- | ---: | ---: | ---: | ---: | ---: |
| default | 41.7 / 51.1 | 1.276, 1.272 | 1.344, 1.333 | **1.097, 1.089** | 230 B / 239 B |
| with the food shop | 45.2 / 64.2 | 1.480, 1.472 | 1.599, 1.597 | **1.124, 1.123** | 246 B / 251 B |

**What costs.** The sampling profile of `dotnet-trace` was no use (91 percent of its samples land on the runtime's GC poll, a safe-point artefact), so the hot paths were found with timers around the host's own methods (a throwaway instrumentation, not committed) and a timing decorator over the policy. The policy itself is small: about 4 percent of the guests' time (`ScoreAttraction` about 12,000 calls per run at 260 ns, the patience and alternative-look hooks about 30,000 calls each at 20 to 30 ns; the whole policy 6 ms of 150 per run). The cost was in the **host's views for the policy**:
- **The queue patience hook** is called for every queued guest on every turn (about 30,000 calls per run), and each call built a guest view and an attraction view, and the attraction view counted the walkers with a **scan of every guest** (`ReservationsOf`, outside a decision), to ask a question whose answer is "stay" until the guest has waited 84 to 156 turns: 11.3 ms of the 120 ms of guest time per run (against 1.9 ms for the original's, which builds nothing).
- **The candidate views** were built by a chain of `with` copies of a 170-byte struct (price, then spreading), and the reservation dictionary was allocated per decision.
- **Two small allocations per guest view** (the read-only wrappers of the two history arrays) and **one array per score** (`AttractionScore.DivideByHistory`'s divisor table, shared with the original's path).

**What changed, with no change of behaviour** (every pin, off and on, is unchanged; a spy that is always asked gives the same run as the skipping host, `TheQuietWindowIsTheTurnsBeforeTheGiveUpTestCanFireAndSkippingItChangesNothing`):
- `IGuestDecisions.QueuePatienceQuietTurns( context, birthId )` (default -1, always asked): the number of turns a guest must have waited before the policy can answer anything but the original's "stay". The host skips the call, and the views, before that. The smarter policy answers `floor(patience in turns) + 1` (the give-up test needs `waited x 256 > patience`, 85 to 157 turns); `ScriptGuestDecisions` answers -1 while any mod has a `leaveQueue` hook, so a mod still sees every turn.
- The queue hook's attraction view carries no walkers (`Reservations` is 0; neither hook reads them, the admission zeroes them too).
- `AttractionView` is built once; the reservation dictionary is kept between decisions; the history wrappers are made once per guest (`Guest.UsedHistoryView`); `DivideByHistory` uses a constant span.

**After**, same method, two rounds:

| Scenario | Guests in the park (original / 1.5.0) | Total time | Guests' own update | Per guest-turn | Allocation per guest-turn |
| --- | ---: | ---: | ---: | ---: | ---: |
| default | 41.7 / 51.1 | 1.215, 1.207 | 1.264, 1.255 | **1.032, 1.025** | 222 B / 203 B |
| with the food shop | 45.2 / 64.2 | 1.407, 1.404 | 1.514, 1.495 | **1.065, 1.051** | 238 B / 207 B |

1.3.0 and 1.4.0 measure 1.03 to 1.05 per guest-turn in the default scenario in the same runs, so phase 6 adds nothing the noise shows. The original's own time per guest-turn did not move (380 ns to 381 ns and 375 ns to 384 ns in the two rounds; the shared allocation fix saves it 10 B).

**Final numbers** (after the path rule was strengthened and main's changes were merged; Release, 3 seeds x 9 repeats, the same method): per guest-turn 1.5.0 is **1.057** of the original in the default scenario (1.3.0 1.049, 1.4.0 1.036) and **1.068** with the food shop (1.032 and 1.049); the totals are 1.19 and 1.43 against 1.20 and 1.40 for 1.3.0 (the guests in the park are 51.5 against 43.8, and 63.8 against 44.3). The path rule costs about 0.02 per guest-turn; the wander is read for at most 13 candidates, a repeated cell once.

**The verdict.** The gate is **at most 1.10 times the original guests' cost per guest-turn** (a guest in the park for one park turn). It is met in both scenarios, with room (1.06 and 1.07; the single worst sample of any run was 1.11 with the food shop, before the repeated-cell shortcut). *In total* the cost is 1.2 (1.4 with the food shop) of the original's guest time; that is the guest count, not a per-guest cost: 1.5.0 holds 18 percent more guests in the default scenario (44 percent with the food shop). The extension's rules are what make guests stay (fewer departures, higher happiness), so more guests to update is its intended outcome, and stating the gate per guest-turn is the honest form of it. What is not measured: the browser build (Mono AOT, a different machine) and a whole frame.

**Phase 6b (1.6.0).** The bin rule runs only inside the bin search, which a guest reaches when its litter meter is full and the park has bins (`GuestSimulation.SeekBin`: one `UsesBinCarry` test and one policy call per search, then the original's loop over the bins, at most 8 cells out instead of 3); a park without bins or without full meters runs the same instructions as 1.5.0. Measured with `--guest-cost` (now taking the scenario flags, here `--guest-litter --guest-bins --guest-litter-effect 25 --guest-handyman-turns 400 --guest-scenario-arrivals 10 --guest-food`, 27 runs per variant, medians): per guest-turn 1.5.0 is 1.082 of the original and 1.6.0 1.062, total 0.98 and 0.96 (this scenario holds more guests in the original, 102 against 92). In the plain and food-shop scenarios 1.6.0 and 1.5.0 run the same code and measure 1.04 to 1.15 and 1.09 to 1.14 per guest-turn in two passes on a machine that other sessions kept 60 to 85 percent busy: the **noise of that machine is larger than the gate's margin** (the quiet-machine figure of phase 6 was 1.057 and 1.068), so the 1.10 gate of the release was **not re-confirmed under quiet conditions** in phase 6b and should be measured again on an idle machine before the extension is released; nothing in 1.6.0 adds work to those scenarios.

**Release status (1.6.0).** The user decided to release the extension as opt-in (`PlayerSelectable: true`, `DefaultEnabled: false`) once the review findings were fixed, the tests green and the CPU gate (at most 1.10 per guest-turn) re-confirmed on a quiet machine. The development machine never became quiet (other sessions kept 60 to 98 percent of the CPU busy; a measurement taken anyway gave 1.000 to 1.363 per guest-turn, the spread of the noise), so the gate was measured on GitHub's hosted runners by the `Smarter guests CPU cost` workflow (`.github/workflows/guest-cost.yml`: the four synthetic scenarios, `--guest-cost-variants original,1.5.0,1.6.0`, 3 seeds x 11 repeats, medians). **It is met**, and 1.6.0 is released:

| Scenario (Ubuntu runner) | 1.5.0 per guest-turn | 1.6.0 per guest-turn | Guests in the park (original / 1.6.0) |
| --- | --- | --- | --- |
| default | 1.077 | 1.072 | 43.8 / 51.0 |
| food shop | 1.090 | 1.091 | 44.3 / 63.8 |
| litter and bins | 1.073 | 1.075 | 43.8 / 51.9 |
| litter, bins and food | 1.082 | 1.081 | 44.3 / 62.7 |

On this runner the noise is small: 1.5.0 and 1.6.0 run the same code wherever the park has no bins (`SeekBin` returns at once) and agree within 0.005. The macOS runner (arm64, a shared virtual machine) is noisy and was not used for the decision: the original's own time per guest-turn was 772 ns in the default scenario and 587 ns in the litter-and-bins one, which runs nearly the same work (770 and 781 ns on Ubuntu), and 1.6.0 against 1.5.0 differed by up to 0.05 in either direction where they run the same code; its 1.6.0 figures were 1.018 to 1.120 (above 1.10 in three of eight). In total time the extension costs 1.19 to 1.44 times the original, because it keeps 16 to 44 percent more guests in the park (happier guests stay longer).

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
  | 4 queue patience | `queue.wait.p99.censored` (down) and `queue.wait.p90.censored` (down), **with `use.guestSecondsPerRide` guarded**: a wait gain that costs guest time per ride is a regression. Phase 4 did not meet this target (see its section) |
  | 5 planning needs | `departures.happinessZero.perAdmission` (down) and `happiness.guestTime` (up) |
  | 6 clean and quiet paths | `happiness.guestTime` (up) |

- **No regression elsewhere:** every other metric with a direction must not move the wrong way by more than 3 standard errors. Up is good for `queue.boarded.rate`, `happiness.guestTime`,
  `happiness.survivors` and `revenue.perAdmittedGuest`; down is good for `queue.rejected.rate`, `queue.rejected.price.rate`, the `queue.wait.*` rows (boarded and censored), `departures.perAdmission`,
  `departures.happinessZero.perAdmission`, `use.giniRides`, `use.giniRidesPerCapacity`, `use.guestSecondsPerRide` and `guests.stuck`. The others (`admissions`, `queue.attempts`, the abandoned and unfinished rates, the exit-counter
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
