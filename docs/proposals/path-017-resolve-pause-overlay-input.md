# PATH-017: Resolve pause overlay input

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current source reconciliation (October 22, 2026)

Pinned main: `1c874922df6e42425dc5638f7e2554c9fde2c68c`. This is a public-source comparison only: no original executable, C# tests, browser or GPU was run here.

The October 18 question about whether the P backdrop can own the click route is no longer wholly unresolved in current implementation. `source/OpenTPW/Hud/ParkHud.PointerInput.cs` implements bounded ordinary-window-manager batch admission before P/pointer drain, a separate pause-overlay owner, captured movement and original down/up recipients. `ParkHud.cs` records that the eligible backdrop covers windows existing when it is created, takes clicks without capturing the camera, and is distinct from a generic speed-pause flag.

This narrows, not closes, PATH-017. Current source still explicitly excludes alternate buffered input, aggregate wheel/non-P keys and original backdrop/panel rendering from that contract. Original visual/input parity is not established by this reconciliation.

## Source inspection
- `source/OpenTPW/Hud/ParkHud.PointerInput.cs`: `CreatePausePointerOverlay`, `RetirePausePointerOverlay`, ordered admission/drain and release-owner routing.
- `source/OpenTPW/Hud/ParkHud.cs`: `UpdateCellTool` and its PATH-017 boundary annotation.
- Native address comments (0x65db25, 0x658b1a/0x658b7f, 0x4879d0) remain source provenance; they are not a new independent original trace from this pass.

## Bounded work
Preserve the landed ordinary-WM P/pointer contract. Investigate alternate buffered input, wheel/non-P routing and original lolight/panel rendering as separate residuals, without treating generic speed pause as an input owner.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Obtain acceptance evidence for the current ordered pointer contract, and separately establish the unported alternate-input and backdrop/panel behavior. Do not repeat the old broad hit-test question as though current ownership routing were absent.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Press P with armed path tool and click the overlay; verify current event delivery and map mutation, including down/up across overlay retirement, and compare with an identified original reference when available.
2. Unpause leaves tool selection and cursor consistent; test camera movement separately from tool clicks and keep Escape menu behavior separate. Qualify alternate buffered input, wheel/non-P keys and backdrop/panel rendering independently rather than inferring them from pointer tests.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
