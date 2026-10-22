# COAST-033: Finish pylon presentation state

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
COAST-PYLON: pylons are their MD2 posed with all five clips in add mode (loft, rotate, incline, bank, loop tilt) at the knot pass's node values through the pylon control's clip maps, the suspended pylon's half turn (kept by straight and loop-pylon nodes from their last curve pass) and the stack parts shown or hidden by the pylon's place in its stack (0x432df0); approximated: the bottom pylon's model shadow (model +0x2c at the ground height, 0x469940) is not drawn, a part's flag hides only its own mesh, the half turn is stored only by the controller's rebuild of a finished track where the original's every edit-step rebuild (0x435d50 -> 0x43eb70) stores it, and the clip sampler's slerp takes exact sines where 0x4742a0 reads the runtime table

## Source inspection
- `source/OpenTPW/World/Coasters/CoasterPylonRig.cs:57`: 58: public sealed class CoasterPylonRig : ICoasterTagModel; 59: {; 61: public const int SlotCount = 5;; 62: private const int LoftSlot = 0, RotateSlot = 1, InclineSlot = 2, BankSlot = 3, LoopTiltSlot = 4;
- `source/OpenTPW/World/Coasters/CoasterTrack.cs:1101`: 1102: internal bool[] PylonsTurnedBack( IReadOnlyList<CoasterNode> chain, IReadOnlyList<SVector3> positions, bool write = false ); 1103: {; 1104: var count = chain.Count;; 1105: var result = new bool[count];

## Bounded work
Add ground shadow, determine hierarchical hiding and store suspended half-turn at edit-step rebuild; table-sine interpolation separate evidence gate.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode shadow transform and child visibility propagation; share live rebuild owner with COAST-028.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Bottom versus stacked upper pylons produce exactly appropriate ground shadow.
2. Edit incomplete suspended track through reversal, then finish it; orientation must not jump solely on controller rebuild.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
