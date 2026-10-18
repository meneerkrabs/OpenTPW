# COMPAT-004: Verify gate sign pixels

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `evidence_only`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
OriginalGateSign composes decoded board and two park-name lines; board placement and relief strength have only low-resolution capture comparison.

## Source sites
- `source/OpenTPW/World/Original/OriginalGateSign.cs:121` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/World/Original/OriginalGateSign.cs:124`: `var (line1, line2) = reloaded ? lines.ReloadedSignLines : (lines.Line1, lines.Line2);`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Higher-resolution original gate captures with camera, name and sign resource identified.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Align decoded board and generated glyph layers against captures, retaining stored paint and existing name-split behavior.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- All four shipped gates; two-line rename and reload; pixel landmarks at board corners and relief edges; reject resolution-only apparent mismatches.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: COMPAT-003, COMPAT-018, COMPAT-007 and gate-sign owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
