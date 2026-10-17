## Smarter guests, phase 0b: the decision seam, two extensions and the guests' identity (October 17, 2026)

- **Two official extensions, both off.** `opentpw.smarter-guests` (`AffectsSimulation`, version 1.0.0 = the algorithm identity) and `opentpw.guest-insights` (UI only, never recorded in saves) are registered
  in `OfficialExtensions`, read through `FeatureGates.SmarterGuests` and `FeatureGates.GuestInsights`, and listed on the Extensions and mods screen with their name and description in the nine interface
  languages (`SupplementaryStrings`). The existing `ModSet` and `ParkResumeMods` already record and refuse a mismatched simulation set; tests cover the three refusals (missing, extra, other version).
- **`IGuestDecisions`, a pure decision layer.** `GuestSimulation` takes one at construction (null: the gate decides, read once in the constructor). `OriginalGuestDecisions` is a stateless extraction of
  the original's logic at seven points: booth verdict, major decision, minor decision, queue admission, post-use memory, wander goal and a new queue patience hook (the original stays). The host keeps every
  effect and **every `WorldRng` draw, once, in the original order**: the booth's draw is taken first and passed in, the candidates' route requests stay host probes the policy calls only for a candidate it
  would take, the wander search and its draws run in the host and the policy accepts or replaces the result, and the bridge's join is split into the host's room checks (`CheckJoinRoom`), the policy's gate
  and limit, and `AppendToQueue`. The policy classes name no generator (a test reads the sources). `SmarterGuestDecisions` delegates every call to the original.
- **Birth ids and the extension's park state.** With the extension on, every guest gets a 64-bit `BirthId` at creation (before the booth decision); guests without one (older saves, original imports, parks
  started with it off) get theirs on the extension's first tick in thing-list order; a counter at its limit is refused, never wrapped. `SmarterGuestsState.Trait( parkSeed, birthId, traitIndex )` is a stateless
  SplitMix64-mixed hash; nothing uses it yet.
- **Persistence.** The optional park-resume section `SmarterGuests` (counter, first-tick flag, park seed, birth ids keyed by guest thing id) is null and omitted while the extension is off, has its own capture,
  validation (present exactly when the extension is recorded in `Mods`), restore and canonical hash, and leaves the guest snapshot untouched. **No `WorldStateHash.SchemaVersion` bump (stays 21):** the state joins
  the hash only while the extension is on, the way the bump and tour modules do, so the off-path hash is byte-for-byte what it was and every pin holds; `GuestSimulation.ComputeStateHash` never sees it. A build from before
  the section refuses such a save (the strict codec rejects unknown members), documented in MODS.md.
- **Tests and pins.** Off: the gate is off, the policy is `OriginalGuestDecisions`, and every existing pin is unchanged. On: `DeterminismTests.SmarterGuestsFixedRunHash` pins the `SyntheticPark` run (gate and policy
  type asserted); that run's guests, economy and guest snapshot equal the off run's every 1,200 ticks; `ParkScenarioFixedRunHash` holds with the extension on; a saved-and-resumed park hashes like the uninterrupted
  one every tick for 1,500 ticks; a counting policy shows every decision point is taken through the layer and changes neither the guest hash nor the generator state.
- **`--guest-report --guest-extension smarter-guests`** runs the whole report with the extension on (an `extensions` list in the JSON, a table line); `--guest-compare off.json on.json` shows every metric `same`
  with the delegating policy. Phases 2 to 6 can now compare on against off.
