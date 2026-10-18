# RIDES-031: Bound Theme Park Inc footprints

## Status and classification
- Classification: out-of-scope.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
a Theme Park Inc world keeps the cells its layout was given and types none of its footprints, so the road signs' '@' cells (\"*@*\", \"*@@*\") are plain occupied cells; tp.exe's commit stamps '@' as a type-1 NoModify path cell linked to its path neighbours (0x529173..0x529208) and 'Q' as a queue cell (0x529119), which ParkCellMap.WriteFootprint does for Theme Park World, whose shapes have neither; Theme Park Inc's own placement is not traced

## Source inspection
- `source/OpenTPW/World/Level.Objects.cs:118`: 119: private bool TypesFootprints => !IsReadOnlyVisit && incLayoutCells == null && !GameEditions.IsThemeParkInc;; 122: internal static ushort OwnerWord( OriginalObject item ) => ParkCellMap.CellWord( item.Placement.X, item.Placement.Y );; 125: private void ConnectObjectsToGuests(); 126: {

## Bounded work
Out-of-scope for TPW original-fidelity closure pending explicit TPI compatibility scope; do not copy TPW stamping as evidence.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace Theme Park Inc placement before typing road-sign @/Q footprints.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. If accepted later, compare TPI *@* and *@@* road-sign ownership/path links against TPI oracle.
2. TPW footprints retain existing type-1 NoModify and queue behavior; no current TPW regression justified by TPI guess.
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
