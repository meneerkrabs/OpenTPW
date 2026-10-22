# COAST-004: Complete collision topology cases

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
COAST-COLLIDE: the self-intersection pass (CoasterCollision: 0x439ab0 -> 0x439c40 -> 0x43a2b0) re-tests the nodes it marks as the original's does (0x200000, and the overflowed holders a dropped node re-marks, 0x43ad50; the passes repeat here until no mark is left where the original runs one per rebuild) and runs on an unfinished chain with its cursor link (the rebuild 0x43eb70 samples and tests every track it draws); it carries the x87 intermediates in double (COAST-032), takes only the default asTrackCollisionPoints (no PC .sam names them; the key layout is not decoded), has no case for the descriptor-equality skips (0x439c5f..0x439c81) and does not sample a chain the demolish button cut open (the cut-end skip 0x10000 is built, the sampler across the cut ends is not traced); there is no terrain or scenery collision in the original and fCollisionPossibleRadius is the pass's box margin; the collision count (+0xb0) is consumed by the section builder 0x43f4e0 (material variant 2, 0x43f4fb, 0x43f58f..0x43f5a0), the lists (+0xb4) by the drop 0x43ad50 and the save writer 0x437300 as far as traced (the loads at 0x430867, 0x43420a, 0x443087 and 0x44309c are not decoded)

## Source inspection
- `source/OpenTPW/World/Coasters/CoasterCollision.cs:48`: 49: public static Result Run( IReadOnlyList<Node> nodes, IReadOnlyList<CoasterSample> samples, IReadOnlyList<(float X, float Y)>? points = null, int maxPasses = MaxPasses ); 50: {; 51: ArgumentNullException.ThrowIfNull( nodes );; 52: ArgumentNullException.ThrowIfNull( samples );
- `source/OpenTPW/World/Coasters/CoasterRide.cs:377`: 378: internal static IReadOnlyList<IReadOnlyList<int>> ChainCrossings( ICoasterCurve curve, IReadOnlyList<CoasterNode> chain, CoasterKnotPass pass, CoasterDefinition definition ); 379: {; 380: if ( chain.Count < 3 || pass.Knots.Length < 3 || chain.Any( node => (node.Flags & (CoasterNodeFlags.CutEnd | CoasterNodeFlags.CutStart)) != 0 ) ); 381: return Array.Empty<IReadOnlyList<int>>();

## Bounded work
Implement descriptor skips and cut-open sampling; evaluate one-sweep versus repeated sweeps separately from numeric precision.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode asTrackCollisionPoints layout, descriptor equality and untraced collision-list consumers.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Compare overlapping descriptor-equal and descriptor-distinct nodes with same geometry.
2. Cut open an intersecting chain and assert sampled collision pairs and marker material; do not invent terrain collision.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
