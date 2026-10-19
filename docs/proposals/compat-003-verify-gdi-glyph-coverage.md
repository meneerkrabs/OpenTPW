# COMPAT-003: Verify GDI glyph coverage

## Status and classification (updated)
- Deliverable of this change: this proposal plus one new characterization test file, `source/OpenTPW.Tests/GdiGray4CoverageContractTests.cs`. No rasterizer, SignSurfaceComposer, layout or runtime edits.
- Classification: `approximation` (unchanged; the `[APPROX:COMPAT-003]` mapping annotation stays at `TrueTypeRasterizer.cs:177`).
- Baseline: `meneerkrabs/OpenTPW` main `cb625bfedb8bbcbf6d2e71e551a5772929700574` (anonymous public clone).
- **Not executed.** This workspace has no .NET SDK; the authored tests were never run, nothing is claimed to pass, and no commit or push was made. They are expectation-only pending independent review and exact-head CI.
- No native GDI/hinting parity is claimed anywhere in this document or the tests.
- The 2026-10-18 plan-only draft is preserved verbatim under "Historical plan" below (superseded context, labelled); everything above it reflects the current baseline.

## Current state (source-verified at this baseline, allowlisted files only)
- `CoverageModel.GdiGray4` / `CoverageModel.Linear` (`TrueTypeRasterizer.cs:26-35`); `GdiOversampling = 4` (`:53`), i.e. 4x4 point samples, 17 levels; the default overload selects GdiGray4 (`:73-74`).
- Per pixel, GdiGray4 counts sub-scanline sample points at `y = row + (sub + 0.5) / 4` (`:139-141`) inside half-open spans (`AddSamples`, `:190-202`) and writes `level * 255 + 8) / 16` (`:177-179`, the APPROX site). Linear instead integrates exact fractional spans (`AddSpan`, `:204-222`).
- Empty inputs return the shared `CoverageBitmap.Empty` (`:9`, `:90-91` for no edges, `:102-103` for degenerate dimensions); `ComputeHash` pins width, height, origin and coverage (`:14-22`).
- No hinting is applied (see the remarks at `:41-49`); outlines are flattened quadratics (`:224-301`).
- Test coverage at this baseline: `GdiGrayCoverageTests.cs` pins the 16/8/4/1-sample bytes, quarter-pixel sample centres, half-open spans, a dropped sliver and 17-value set membership on a triangle; `TrueTypeRasterizerGoldenTests.cs` pins Linear-model scanline window semantics, an empty bitmap, a composite hash and the 8192 guard; `TrueTypeFontTests.cs` pins font-path behaviour (2x2 squares, winding, curves, composites) and original-font hashes that require `OPENTPW_GAME_PATH` game data (inconclusive without it — none is available or used here).

## Substantive gap filled by this change
No test pinned (a) each of the 17 GdiGray4 levels with a constructed shape, (b) exact multi-byte GdiGray4 output for a slanted edge, (c) vertical half-open rows across multiple rows in the gray model, (d) negative-coordinate `OriginX`/`OriginY` under the gray model, (e) empty-glyph paths (default-model placed list, degenerate contours, the `.notdef`/empty-`loca` font path), (f) same-process determinism through the public API in the gray model, or (g) a direct gray-vs-linear divergence pin. All are characterization of existing behaviour only; no code change was needed or made.

## New test file: `source/OpenTPW.Tests/GdiGray4CoverageContractTests.cs`
Exact claims, each with a hand-derived pinned literal (derivation in the test comments; analytic sample-boundary fixtures use dyadic coordinates):

| Test | Pinned expectation |
|---|---|
| `EveryNonzeroSampleCountMapsToItsPinnedGrayByte` (16 DataRows) | levels 1..16 map to 16, 32, 48, 64, 80, 96, 112, 128, **143**, 159, 175, 191, 207, 223, 239, 255; 1x1 bitmap, origin (0,0). Note the asymmetry: level 8 -> 128 but level 9 -> 143, not 144. Shapes covering counts a rectangle cannot produce (5, 7, 10, 11, 13, 14, 15) combine two side-by-side rectangles. Level 1's geometry coincides with an existing pin in `GdiGrayCoverageTests`; the row is kept so the table is complete, and the overlap is disclosed. |
| `ASlantedEdgeWhoseCrossingsLandOnSamplePositionsIsPinned` | right triangle with hypotenuse `x == y`: rows `[96,0,0,0], [255,96,0,0], [255,255,96,0], [255,255,255,96]`, 4x4, origin (0,0). Diagonal pixels hold 0+1+2+3 = 6 samples -> 96. |
| `VerticalSampleRangeIsTopInclusiveAndBottomExclusiveAcrossRows` | rows [0.125, 2.375): `[255, 255, 64]`, 1x3, origin (0,0). |
| `AGlyphLeftOfAndAboveTheOriginGetsPositiveOriginsAndPinnedCoverage` | span [-1.25, -0.75) x [-2, -1]: bytes `[64, 64]`, 2x1, origin (2,2). |
| `EmptyAndDegenerateOutlinesProduceTheSharedEmptyBitmap` | empty placed list, a one-point contour and an all-horizontal contour each return `CoverageBitmap.Empty` under the default model. |
| `AnEmptyFontGlyphRasterizesToTheEmptyBitmap` | glyph 0 of a synthetic font (empty `loca` entry) rasterizes to `CoverageBitmap.Empty` through `RasterizeGlyph` (reuses `TrueTypeFontTests.FontBuilder`, same assembly). |
| `RepeatedRasterizationOfTheSameSceneIsByteIdentical` | two runs of the same composite scene produce byte-identical coverage, equal `ComputeHash()`, dims (6,4), origin (0,0), spot values 96 and 80. Composite-scene **hash literals are intentionally not pinned here** because no execution was possible to generate them; the existing Linear composite hash pin is the template to reproduce on exact-head CI. |
| `TheGrayModelDiffersFromLinearOnFractionalCoverage` | same rectangle [0, 0.3) x [0, 1): GdiGray4 pins 64 (one sample column, 4 samples); the two models must disagree (Linear's 77 is already pinned in `GdiGrayCoverageTests`). |

## Exact tested claims vs. unproven cases
Tested (once executed on exact head): the 17-level gray byte map on constructed sample counts; sampled (not integrated) fractional coverage with half-open spans on both axes; exact slanted-edge samples when crossings land on sample positions; origin handling for glyphs left of/above the pen; empty and degenerate outline paths, including the font `.notdef` path; byte-level repeatability of repeated rasterization in one process.

Unproven and explicitly not claimed:
- **No native GDI parity.** The level-to-byte palette mapping remains `[APPROX:COMPAT-003]` (`TrueTypeRasterizer.cs:177`); the sample positions and the absence of gamma are the model's own choices. No GDI capture exists in this environment and no comparison was made.
- Hinting: none applied (no bytecode interpreter; the shipped fonts carry no `gasp` table); the tests use synthetic unhinted outlines, so they say nothing about hinted output.
- Determinism: only same-process repeatability is tested. The analytic sample-boundary cases use exactly representable dyadic values; general curves involve flattening with `sqrt`, so cross-platform float bit-equality is a design expectation, not a tested claim.
- `SignSurfaceComposer` blur/relief behaviour is out of scope: `SignSurfaceComposer.cs` is outside this task's read allowlist and was not re-inspected; the line references in the historical plan are preserved as provenance leads only, not re-verified.
- Original-font coverage: requires `OPENTPW_GAME_PATH` game data; unavailable here, not fabricated.

## Evidence still needed (unchanged requirement)
Matched Windows GDI grayscale DIB outputs, including LOGFONT, hinting and palette settings, to verify the byte mapping, sample positions and gamma against real GDI. Any original captures/assets remain private; fixtures must stay synthetic or be references/hashes. No supporting evidence was needed from outside the allowlist for this deliverable.

## Ownership and overlap
- Consult: COMPAT-002, COMPAT-004, COMPAT-018 and the font/compositor owner.
- No runtime files touched: only this proposal and `GdiGray4CoverageContractTests.cs` were authored (the file did not previously exist and duplicates no existing test).
- The original-font pinned-hash suite stays the owner of game-data-dependent expectations; the new file only uses synthetic outlines/fonts and always runs.
- An APPROX marker alone is not evidence of an absent feature; nothing here retires the register entry or the APPROX annotation.

## Completion boundary
- Documentation plus characterization tests only; no gameplay source files changed, no approximation retired, no runtime work claimed done, no gameplay feature marked complete.
- Next steps: independent review of the hand-derived pinned literals, then execution on exact head; pin deterministic composite-hash literals only from real CI output.

## Historical plan (2026-10-18 draft, baseline `5ccddcab` — preserved verbatim, superseded by the sections above)

> ## Status and classification
> - Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
> - Classification: `approximation`.
> - Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
> - One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.
>
> ## Source-verified residual
> GdiGray4 rasterization uses 4x4 samples and a 17-level byte map without hinting; the compositor already implements the documented blur/relief arithmetic.
>
> ## Source sites
> - `source/OpenTPW.Files/Formats/Font/SignSurfaceComposer.cs:22` — APPROX annotation and adjacent implementation inspected.
> - `source/OpenTPW.Files/Formats/Font/TrueTypeRasterizer.cs:177` — APPROX annotation and adjacent implementation inspected.
>
> ## Implementation cross-check
> - `source/OpenTPW.Files/Formats/Font/SignSurfaceComposer.cs:23`: `public static (byte[] Left, byte[] Right, int TextPixelCount) Compose( SignFile sign, SignFontLibrary fonts, IReadOnlyList<string?> lines, ICollection<string> diagnostics, SignPaint? firstPaintOverride = null )`
> - `source/OpenTPW.Files/Formats/Font/TrueTypeRasterizer.cs:178`: `var level = (int)Math.Clamp( accumulator[x], 0, GdiOversampling * GdiOversampling );`
> - Existing behavior described above was checked against implementation, not only the register summary.
> - The presence of an APPROX marker alone is not evidence of an absent feature.
>
> ## Evidence needed
> - Matched Windows GDI grayscale DIB outputs, including LOGFONT, hinting and palette settings.
> - Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
> - Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
> - Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.
>
> ## Bounded investigation and implementation plan
> 1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
> 2. Build a pixel-comparison harness for coverage before blur; isolate sample positions, hinting and byte mapping rather than rewriting proven compositor arithmetic.
> 3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
> 4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
> 5. Submit any later implementation with exact evidence, changed scope and test output for review.
>
> ## Acceptance tests to author and run later
> - Small and large glyphs; thin strokes; dropout cases; coverage-level histogram; separately compare coverage, blur and relief outputs.
> - Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
> - Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
> - Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.
>
> ## Ownership and overlap
> - Consult: COMPAT-002, COMPAT-004, COMPAT-018 and font/compositor owner.
> - Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
> - Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
> - Evening #439 is an extension and is not proof of original lighting or behavior.
>
> ## Completion boundary
> - This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
> - Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
> - No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
