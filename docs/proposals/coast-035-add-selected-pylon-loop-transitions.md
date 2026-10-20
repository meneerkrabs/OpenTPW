# COAST-035: Add selected-pylon loop transitions

## Status and classification
- Classification: missing.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
COAST-GHOST: the build cursor links the original's model-less spare node (flag 0x8000, plus 0x4000 when the validator refuses the cell) after the last node, within 300 units of it (0x435d50, CoasterToolPanel.WithinReach), drawn through without a pylon and its link in the invalid cross section's copy with blue.tga, or red.tga when refused or crossing the track (0x43f4e0); the closing target (a hovered chain start) marks the start 0x800000, or 0x804000 when the closing link is refused (0x4360c4), and the start's own tag pairs take the blue or red marker (0x43f4e0 with both nodes the same; the rebuild 0x43eb70 walks the next links from the list head and draws no link into the start); the marks 0x4360c0 also sets (0x200000 on both ends and their neighbours) select the nodes the self-intersection pass re-tests (0x439b6b) and are redundant here; approximated: the handlers of a selected pylon that also start the loop mode (0x442d10, 0x447a10: when the last node carries 0x80 the mode counter 0x79c664 becomes 1 and the mode bit 0x40 is set) have no counterpart

## Source inspection
- `source/OpenTPW/World/Coasters/CoasterTrack.cs:1443`: 1445: public (IReadOnlyList<CoasterNode> Chain, bool Closed)? BuildPreviewChain( (int X, int Y)? cursor, float height, bool loop, bool firstOfPair = false, bool valid = true ); 1446: {; 1447: if ( (IsComplete && !IsCut) || nodes.Count < StationNodeCount || DepartCell == null ); 1448: return null;

## Bounded work
Implement selected-pylon handlers that enter loop mode/counter states, preserving existing hover preview.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace 0x442d10/0x447a10 conditions, pylon flag 0x80 and transition cancellation.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Select first paired loop pylon and assert mode bit 0x40/counter 1.
2. Select eligible non-loop pylon with mode off and verify counter 2 branch, placement decrement and cancel reset.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
