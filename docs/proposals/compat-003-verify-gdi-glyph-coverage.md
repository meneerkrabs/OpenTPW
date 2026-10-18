# COMPAT-003: Verify GDI glyph coverage

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
GdiGray4 rasterization uses 4x4 samples and a 17-level byte map without hinting; the compositor already implements the documented blur/relief arithmetic.

## Source sites
- `source/OpenTPW.Files/Formats/Font/SignSurfaceComposer.cs:22` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW.Files/Formats/Font/TrueTypeRasterizer.cs:177` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW.Files/Formats/Font/SignSurfaceComposer.cs:23`: `public static (byte[] Left, byte[] Right, int TextPixelCount) Compose( SignFile sign, SignFontLibrary fonts, IReadOnlyList<string?> lines, ICollection<string> diagnostics, SignPaint? firstPaintOverride = null )`
- `source/OpenTPW.Files/Formats/Font/TrueTypeRasterizer.cs:178`: `var level = (int)Math.Clamp( accumulator[x], 0, GdiOversampling * GdiOversampling );`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Matched Windows GDI grayscale DIB outputs, including LOGFONT, hinting and palette settings.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Build a pixel-comparison harness for coverage before blur; isolate sample positions, hinting and byte mapping rather than rewriting proven compositor arithmetic.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Small and large glyphs; thin strokes; dropout cases; coverage-level histogram; separately compare coverage, blur and relief outputs.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: COMPAT-002, COMPAT-004, COMPAT-018 and font/compositor owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
