# RIDES-045: Bound Theme Park Inc script inputs

## Status and classification
- Classification: out-of-scope.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
a Theme Park Inc object that a mod brings into a Theme Park World park runs its original script, but only the script variables Theme Park World's engine writes are driven: the variables the scripts of the three Theme Park Inc worlds declare beyond the standard ride set (VAR_FPBYPASS in 69 scripts, VAR_SPEED in 19, VAR_SPECIAL in 15, the sideshows' VAR_WIN*/VAR_IN*/VAR_TIMEOUT/VAR_WINLANE, the litter bin's VAR_BOMB, VAR_PRANKED, VAR_ONFIRE, VAR_DIRTY, VAR_EMPTY, VAR_FIRERISK, VAR_CONTENTS and VAR_TIMER, the staff rooms' VAR_CLOSED) keep their initial 0, so the branches they guard never run (the litter bin's WAITABS, opcode 45, sits behind VAR_BOMB); what Theme Park Inc's executable writes into them, and when, is not read

## Source inspection
- `source/OpenTPW/World/Objects/ObjectCatalog.cs:567`: 570: var ownName = modObject != null ? new ObjectSettings( layers )["Info.Name"] : null;; 571: if ( modObject != null ); 572: layers.Add( new ObjectSettingsFile( ModLayerText( modObject ), "objects.json" ) );; 573: var settings = new ObjectSettings( layers );

## Bounded work
Out-of-scope TPI-to-TPW mod extension; enumerate unsupported variables without implying original TPW missing feature.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace TPI writers and lifecycle for FPBYPASS/SPEED/SPECIAL, sideshow, bin and staff-room variables.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Provide variable coverage matrix with source executable and supported mode for each writer.
2. TPW standard script inputs retain behavior; optional TPI implementation must test guarded WAITABS/bomb branch with explicit source oracle.
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
