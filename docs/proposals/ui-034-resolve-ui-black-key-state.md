# UI-034: Resolve UI black key state

## Current bounded implementation (2026-10-20)

Preserve per-slot transparency in contiguous UI material runs without changing painter order; native black-key thresholds remain open. See [implementation contract](../UI-MATERIAL-RUNS.md). Recovered the independently source-reviewed frozen change and integrated current main de0a3bff. Synthetic C# fixtures are included; exact-head CI and rendered validation are not yet claimed. No fidelity identifier is retired. The original plan below is historical and its plan-only description is superseded for this bounded slice; unresolved evidence gates remain.

## Historical proposal

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Opaque transparent-slot textures use guessed black-key thresholds 6 and 48 with color un-premultiplication.

## Source sites
- `source/OpenTPW/UI/Original/UiImages.cs:68` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/UI/Original/UiImages.cs:69`: `if ( opaque )`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Verified mapping from MD2 slot flag 0x2 to texture-state flags and original ipan pixels.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Trace alpha state and compare soft-key output on the single known ipan case; avoid broad texture changes from an unproven flag equivalence.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Channel values 0/6/7/47/48; true alpha textures bypass keying; panel edges on light/dark backgrounds.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-004, COMPAT-032 and UI texture owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
