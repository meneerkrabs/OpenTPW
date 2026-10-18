# TRACK-032: Restore saved controller semantics

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
SAVED-RIDE CONTROLLER: a saved kart or water ride's controller is rebuilt from the saved circuit (BumpController.RestoreCircuit after a re-walk) and the saved sections are compared with the walked ones, where tp.exe's loader 0x543560 (called from 0x415270) reads a chunk stream: chunk 3 the ride record (0xd0 bytes per ride at 0x877b60, its duration at +4), chunk 5 the circuit's section records as raw 0xac-byte copies (linked to the ride and to the tile list at 0x877b70 by cell, the circuit step looked up from the ride's own list), chunk 6 the ride's reference section (+0xa4: the first section with flag 4, then the next without flag 2) and the ride's controller handle +0x28 pointing at it; the section records' fields beyond the kind, place and segment, and whether the walk verdict is re-derived on load, are not traced

## Source inspection
- `source/OpenTPW/World/Level.Tracks.cs:616`: 618: private TrackCircuit? RewalkForRestore( OriginalObject item, BumpControllerSaveData entry ); 619: {; 620: if ( waterTracks == null || !trackStations.TryGetValue( item, out var station ) || item.Runtime.Bump is not { } bump ); 621: return null;

## Bounded work
Decode section fields/reference selection and preserve original loaded controller rather than blindly re-walk where different.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace chunk 3/5/6 semantics, all 0xac-byte fields and whether load derives verdict again.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Import circuit whose saved section state differs from fresh walk and compare duration/controller/ref section.
2. Reference chooses first flag-4 then next without flag-2 section; malformed links fail safely without mutating park.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
