# COMPAT-018: Verify sign board orientation

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `evidence_only`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
SignWaveletDecoder and AlphaBlit exist; uploaded GPU row orientation and independent original board pixels remain unverified.

## Source sites
- `source/OpenTPW.Files/Formats/Font/SignSurfaceComposer.cs:39` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW.Files/Formats/Image/SignWaveletDecoder.cs:14` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW.Files/Formats/Font/SignSurfaceComposer.cs:40`: `var pixels = board.IsWavelet ? SignWaveletDecoder.Decode( board ) : board.Payload;`
- `source/OpenTPW.Files/Formats/Image/SignWaveletDecoder.cs:15`: `public static byte[] Decode( SignBitmap bitmap ) => Decode( bitmap, X87Precision.Bits != 24 );`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Original-platform decoded board pixels or a controlled asymmetric board capture.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Compare CPU decode and GPU upload separately, using an asymmetric synthetic fixture so a vertical flip is detectable without shipping assets.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Color/alpha plane order; top/bottom landmarks; CPU/GPU agreement; float32 versus emulated-register decoder regression.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: COMPAT-003, COMPAT-004 and texture-upload owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
