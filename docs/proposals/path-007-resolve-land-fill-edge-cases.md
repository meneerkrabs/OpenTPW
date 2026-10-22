# PATH-007: Resolve land fill edge cases

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
PATH-LAND: the land follows the original (tp.exe map init 0x536490 sets flag 0x40 on every non-InitialPath cell, the fence of the theme's Hoardings.sam is laid from the gate posts (0x5323f0), an imported save keeps its saved mFlags and hoardings (0x4d0b30), Buy Land validates (0x531a50), prices (0x532303) and merges (0x5327e0) a rectangle), except that the fence fill's cell outcome is modelled (the cells inside the ring closed along the gate, plus the four cells round each corner that turns away from the land; cell for cell equal to the Jungle Easymode save's flags) instead of its 2 x 2 scan (0x5311c0); the six-click fence laid when bit 0x400 of the level word 0x79d86c is set is not modelled; a purchase covering the open side of the gate, or whose merged fence cannot be walked from post A to post B, is refused (the original's validator accepts some such rectangles, e.g. Jungle (45, 11)-(49, 17); its ring walk 0x52ba50 then likely keeps the old ring and the refill drops the new hoardings, charging for no land, or may not terminate)

## Source inspection
- `source/OpenTPW/World/ParkLand.cs:230`: 231: public void ClaimEnclosedCells( ParkCellMap cells ); 232: {; 233: ArgumentNullException.ThrowIfNull( cells );; 234: foreach ( var (sx, sy) in squares )
- `source/OpenTPW/World/ParkLand.cs:378`: 379: if ( CoversGate( xmin, ymin, xmax, ymax ) ); 380: return Refused( LandPurchaseRefusal.Gate );; 381: if ( Merge( first, second ) == null ); 382: return Refused( LandPurchaseRefusal.Ring );

## Bounded work
Separate safe purchase rejection from original acceptance quirks; decode six-click fence mode and 2x2 fill.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace ring traversal termination and original result for gate-crossing purchase before relaxing safeguards.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Compare cell ownership against Jungle Easymode reference and concave ring cases.
2. Exercise rectangle (45,11)-(49,17) with bounded traversal; identify charged/no-land outcome without creating a hang.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
