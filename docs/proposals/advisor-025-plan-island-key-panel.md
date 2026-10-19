> Update — October 19, 2026: the text below is a historical proposal snapshot. The reviewed counter-only host slice now displays a read-only saved Full Simulation key count in the actual lobby menu. It does not implement live advisor refresh, EA/EB affordability, effect 0x61 or full native panel behavior. Historical native-effect and visibility claims below are not acceptance requirements established by this host slice. Current host ownership, synthetic acceptance and remaining gates are documented in `docs/reverse/LOBBY-KEY-PANEL.md`. ADVISOR-025 remains open.

# ADVISOR-025: Plan island key panel

## Status and classification
- Classification: missing.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
a fixed sequence's timer (sequences 28 and 29, the ticket/key advice) starts the screen-space Key effect (template 87 at native (90000, 0, 7000), the top right) and plays cat_ui 0xC6 when the advisor clock reaches it, as 0x599991 does; its call 0x4B9340 does nothing in a park and refreshes only the island lobby's key panel (window 0x7CC4B4, layout Lobby 0x757F60; created by 0x4B8CA0, which also calls it, and cleared by 0x4B8B70), which OpenTPW's lobby lacks: it shows or hides 0x1E0EA-0x1E0F1 by the key count 0x5AF680 of the player ledger object (0xF7C58C, built by 0x5AEF50) and formats \"%d x\" into 0x1E0EE, with particle 0x61 when the count drops (0x521E60 at 50000, 0, y from the panel's rectangle x 75000/0x600); where the screen-space particles sit in the frame is PTCL-909

## Source inspection
- `source/OpenTPW/World/AdvisorSequence.cs:261`: 262: timer = sum + entry.TimerMilliseconds;; 263: continue;; 264: }; 265: clips.Add( entry.Clip );

## Bounded work
Add missing island-lobby key display and timer-triggered refresh; retain park behavior.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Establish lobby sequence 28/29 reachability and player-ledger key source; particle placement belongs to PTCL-909.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. At key counts 0, 1 and many verify visibility and count text for controls 0x1E0EA-0x1E0F1.
2. Decrease a key count and assert one effect 0x61; same sequence in park must not construct lobby UI.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
