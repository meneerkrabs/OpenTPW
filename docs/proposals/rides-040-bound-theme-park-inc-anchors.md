# RIDES-040: Bound Theme Park Inc anchors

## Status and classification
- Classification: out-of-scope.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
Theme Park Inc fixed scene records saved at (0, 0) use their local model origin; the thing records of the six that have an archive carry the fixed-item default position (128, 128) and angle 0, so the save leaves no transform field unread; how the original places a local-coordinate fixed model at its anchor is not read

## Source inspection
- `source/OpenTPW/World/Objects/ParkObjects.cs:602`: 604: var thingId = Things != null && save.FindAttraction( record ) is { } thing && Things.IsUsed( thing.Id ) && Things.ModelOf( thing.Id ) == SaveThingModel.Attraction; 605: && objects.All( other => other.ThingId != thing.Id ) ? thing.Id : 0;; 606: Register( new OriginalObject( entry, ComputePlacement( entry, record.X, record.Y, record.Rotation ), ScriptWorld, nextSeed++ ^ scriptKey, true, attractionId: null, seedScript: false ), thingId );; 607: count++;

## Bounded work
Out-of-scope for TPW parity unless TPI fixed-object compatibility is selected; maintain separate evidence proposal.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace TPI local-coordinate fixed model placement; saves already account for known transform fields.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. For six archived fixed records compare local (0,0) geometry anchored at default (128,128).
2. No unread-transform claim without binary evidence; TPW fixed-item placement stays unchanged.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Explicit Theme Park Inc compatibility scope decision.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
