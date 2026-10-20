# COAST-034: Align coaster car animation lifecycle

## Implementation update (October 18, 2026)

Wheel animation epochs now belong to train slots and reset at the bounded state/rating initialization boundaries, independently of visibility and renderer lifetime. Independent source review passed. Added CPU lifecycle and optional asset-UV regressions, including direct existing-slot state reset. C# compilation/tests remain pending because local dotnet is unavailable; exact-head CI and GPU evidence are not yet established. COAST-034 stays open for bend endpoints, flume clips and rider sockets. The historical plan below preserves the remaining scope.

## Historical status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
COAST-CAR: cars stand on the curve at their coordinate's knot-pair parameter around the train's middle car, spaced by their types' pair distances, with the root node's pose replacing the model's root rest (0x43d8f0, so the model's +Z runs along the track and the camera seat looks along it), the wheel clip played at the type's factor x the train speed (0x474050) and the bend cars' meshes bent along the samples (0x438800, 0x438ab0); approximated: the wheel clip starts when the car is first drawn where the original starts clip 5 on every car at speed factor 1.0 in 0x43be10 (0x4732a0(model, 5, 0, 1, 1.0, 0); called by the state routine 0x435730 and the rating pass 0x43c450, which also zeroes the cars' speeds), the bend reads the sample yaw of this build's curve and a vertex beyond the tables takes their end entries, the flume boats' own clips (CrocCarM1..M3, MAGGOTM1..M2) have no player in the coaster code (only 0x43be10 plays id 5, on non-bend cars), and a rider hangs on the socket of its index in the car's seated list, the PC's socket order (0x44b270), unbent on a bend car

## Source inspection
- `source/OpenTPW/World/Coasters/CoasterCarRig.cs:93`: 94: public sealed class CoasterCarRig; 95: {; 96: public CoasterCarRig( ModelFile model ); 97: {

## Bounded work
Start/reset wheel clips at original state/rating initialization instead of first visibility; investigate bend endpoints and seated sockets.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace end-of-table bend handling and whether flume clips are actually invoked; do not invent unused animation players.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Create car offscreen then reveal it: wheel phase matches continuously visible control car.
2. Rating reset zeroes speed/restarts clip as original; bend-car rider sockets and out-of-range vertices follow established oracle.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
