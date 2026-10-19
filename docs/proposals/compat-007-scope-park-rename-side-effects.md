# COMPAT-007: Scope park rename side effects

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
The historical proposal overstated the missing behavior. Current host rename, pending secret-name action, imported player-save gate lines and resume name persistence are implemented. Original online-record/event 0x15 side effects and exact original import equivalence remain separately evidence-gated.

## Source sites
- `source/OpenTPW/World/Level.cs:112` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/World/Level.cs:113`: `public bool RenamePark( string line1, string line2 )`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Original event 0x15 listeners and forwarded flag behavior; authorized original save with a non-default two-line gate name.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Separate local save-name import from online and secret-event effects; trace setters/listeners before adding side effects, preserving read-only visit refusal.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Review the existing rename/next-turn, gate-name and resume fixtures first. Add only demonstrated coverage gaps, including original event 0x15 once-only semantics and original-save corpus equivalence after the required evidence exists; do not duplicate the existing host secret-name test.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: SAVE-920..923 owner, online record owner and secret-event owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.

## Current-source reconciliation (2026-10-19)
At public main `fd8ff52eec12354fba02f23d23c5a217b2515a3b`, `SetupParkName` reads `SavedGateName`, while `RenamePark` updates the gate and queues the host cheat action. `CheatsOriginalTests.RenamingTheParkToTheSecretNameTurnsTheCheatModeOnAtTheNextTurnAndTheSignKeepsTheName` already exercises the real rename, pending action and next-turn behavior. Host resume stores the name; these are not missing features or reasons to duplicate a secret-name fixture.

The online publication title is separately editable: the name field initially reads `My park`, and `ExportLevel` receives that field's text. This is not proof of automatic rename propagation or a defect by itself. Remaining work must specify original online-record/event 0x15 effects and original-save import equivalence, rather than reimplement the current host paths. Historical baseline and source sites remain provenance. This reconciliation introduces no runtime changes, new native verification, test-execution claim or whole-ID closure.
