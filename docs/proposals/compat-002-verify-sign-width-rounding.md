# COMPAT-002: Verify sign width rounding

## Status and classification
- Evidence-only characterization; no runtime implementation changes, no test-execution claim, no retirement or merge.
- Classification: `evidence_only` (behavioral characterization).
- Current baseline: `meneerkrabs/OpenTPW` main `cb625bfedb8bbcbf6d2e71e551a5772929700574` (2026-10-19).
- Historical baseline: the first plan-only draft was written against `5ccddcab` (2026-10-18). That plan is superseded by the findings below and is retained only under "Historical context".
- Authored by this task: this proposal and `source/OpenTPW.Tests/SignWidthRoundingCharacterizationTests.cs`. No production source is changed.
- All fixtures are synthetic and use the existing `TrueTypeFontTests.FontBuilder`; no private font, game asset or original executable was read.
- No local SDK is available. The first published version ran in cross-platform CI and exposed a fragile expected fit width at the exact 512 px boundary (expected 17, observed 18). The revised percent-fit case uses a 513 px canvas and awaits a fresh exact-head CI run; no passing result is claimed for this revision.

## Current findings (source-verified)

### Fractional advances at 100 percent
`SignTextLayout.Place` accumulates the raw `hmtx` advance in font units while `widthPercent` is 100, and `Measure` multiplies only once by `ScaleForEmHeight(emPixels) * horizontalScale`. The result therefore stays fractional; there is no per-glyph or per-line integer rounding. Example from the characterization tests: a 601-unit advance at upem 1000 and a 100 px em measures 60.1 px; `"AB"` with advances 601 and 603 measures 120.4 px, and 180.6 px at `horizontalScale = 1.5`.

### Percent truncation is per character, in pixel space
For any `widthPercent` other than 100, `Place` passes the already scaled pixel advance (`advance * pixelsPerUnit`) to `ScaledAdvance`. `ScaledAdvance` computes `advancePixels * widthPercent / 100f` and applies `MathF.Truncate` to that one character. The truncated pixel value is divided back by `pixelsPerUnit` for accumulation and multiplied out again by `Measure`. Consequently the line width is the sum of per-glyph integer pixel advances, not `truncate(line total * percent / 100)`. Example: two 612-unit glyphs at a 100 px em are 122.4 px at 100 percent; at 50 percent each 61.2 px advance becomes 30 px, for 60 px, whereas truncating the line total would give 61 px.

### Horizontal scale acts before percent truncation
Both the explicit `horizontalScale` argument and the value returned by `LogFontHorizontalScale` enter `pixelsPerUnit` before `ScaledAdvance`. At 100 percent they remain continuous. At another percent they change the pre-truncation pixel value, so truncation can discard a different fraction at different scales. Example: advances 601 and 603 at `horizontalScale = 1.5` give 90.15 and 90.45 px; at 90 percent those become 81 and 81 px, for 162 px, while a continuous 90 percent of the 180.6 px line would be 162.54 px.

### LOGFONT width scaling is a continuous average-width ratio
`LogFontHorizontalScale` returns `logFontWidth / (AverageCharWidth * ScaleForEmHeight(emPixels))` when `logFontWidth > 0` and `AverageCharWidth > 0`, otherwise 1. It is continuous and performs no rounding. It is a different mechanism from the slot percent: the percent truncates each already-scaled glyph advance to a whole pixel, while the LOGFONT width ratio does not truncate at all. Example with synthetic `xAvgCharWidth = 600`, upem 1000 and a 100 px em: the average character is 60 px, `lfWidth = 90` selects scale 1.5, and a 601-unit glyph measures 90.15 px at 100 percent; the same glyph at 90 percent measures the truncated 81 px.

### Synthetic builder and the OS/2 dependency
`TrueTypeFontTests.FontBuilder` writes `head`, `hhea`, `maxp`, `hmtx`, `loca`, `glyf`, `cmap`, optional `name` and optional `kern`, but no `OS/2` table. Such a font therefore has `AverageCharWidth == 0`, so `LogFontHorizontalScale` is neutral for it. That is characterized directly, and the new test file adds a minimal 78-byte OS/2 table in a test-only wrapper so the existing average-width path can be exercised with synthetic metrics. The base glyph metrics still come from `FontBuilder`.

### Long synthetic names
A 32-character synthetic name with every glyph 601 units wide, em 144, upem 1000, and synthetic `xAvgCharWidth = 600` measures 2769.408 px at 100 percent. At 90 percent the 86.544 px advance truncates to 77 px per character, for 2464 px, a loss that accumulates over the name. `FitLogFontWidth(..., logFontWidth: 58, canvasWidth: 512)` narrows the name to width 15 at 100 percent because average-width scaling makes the extent depend on `lfWidth`; at 90 percent a 513 px canvas selects width 18. The one-pixel margin avoids treating float reconstruction of 32 truncated 16 px advances as an exact 512 px comparison.

## Source sites
- `source/OpenTPW.Files/Formats/Font/SignTextLayout.cs:43-49` — `ScaledAdvance`: 100 percent returns the fractional advance unchanged; other values truncate the scaled pixel advance.
- `source/OpenTPW.Files/Formats/Font/SignTextLayout.cs:54-76` — `Place`: raw `hmtx` advance at 100 percent; per-character pixel scaling and truncation otherwise.
- `source/OpenTPW.Files/Formats/Font/SignTextLayout.cs:78-83` — `Measure`: the single final multiply by em scale and horizontal scale.
- `source/OpenTPW.Files/Formats/Font/SignTextLayout.cs:93-96` — `LogFontHorizontalScale`: continuous average-width ratio; 0 keeps the design aspect.
- `source/OpenTPW.Files/Formats/Font/SignTextLayout.cs:103-130` — `FitLogFontWidth`: the original bisection over integer LOGFONT widths.
- `source/OpenTPW.Files/Formats/Font/TrueTypeFont.cs:52-53,134-145` — `AverageCharWidth` from OS/2 xAvgCharWidth (0 when OS/2 is absent); Windows ascent/descent fall back to `hhea` when OS/2 is absent.
- `source/OpenTPW.Tests/TrueTypeFontTests.cs:17-213` — existing synthetic `FontBuilder`; `387-403` is the original-font LOGFONT test (inconclusive without private data).
- `source/OpenTPW.Tests/SignWidthPercentTests.cs:18-30` — existing per-character percent truncation examples.
- `source/OpenTPW.Tests/SignWidthRoundingCharacterizationTests.cs` — new characterization fixtures described below.

## Characterization test contract (revised, exact-head CI pending)
- `HundredPercentKeepsFractionalAdvancesAndHorizontalScaleStaysContinuous`: 601/603-unit advances retain 60.1/120.4 px at 100 percent, including after `horizontalScale = 1.5`.
- `PercentTruncatesEachCharacterAdvanceNotTheLineTotal`: 612-unit glyphs at 50 percent yield 30 + 30 = 60 px, not the 61 px obtained by truncating the 122.4 px total.
- `PercentTruncationHappensAfterHorizontalScaleAndLosesFractions`: 601/603 units at scale 1.5 and 90 percent yield 81 + 81 = 162 px, distinct from 90 percent of 180.6 px.
- `LogFontWidthScaleIsContinuousWhilePercentQuantizes`: with synthetic xAvgCharWidth 600, `lfWidth = 90` at em 100 gives scale 1.5; 100 percent keeps 90.15 px, while 90 percent truncates to 81 px; non-positive widths and width 0 give scale 1.
- `SyntheticFontWithoutOs2HasNoLogFontWidthScale`: a plain `FontBuilder` font has `AverageCharWidth == 0` and `LogFontHorizontalScale == 1`, so LOGFONT width is neutral.
- `LongSyntheticNameCharacterizesPercentAndLogFontWidthFits`: a 32-character synthetic name pins the fractional extent, the accumulated percent loss, and the fitted widths 15 (100 percent, 512 px canvas) and 18 (90 percent, 513 px canvas), including the existing "fitting width + 3" invariant.

These fixtures call only the managed `TrueTypeFont` and `SignTextLayout` APIs. They make no comparison with native output and no parity claim.

## Distinguishing the two mechanisms
| Mechanism | Where applied | Rounding | Observable result |
| --- | --- | --- | --- |
| Slot width percent | Per character, after em scale and horizontal scale | `MathF.Truncate` to a whole pixel per glyph | Quantized line width; fraction lost per glyph; not a continuous percentage of the line |
| LOGFONT width (`lfWidth`) | As a continuous horizontal-scale ratio derived from OS/2 xAvgCharWidth | None in managed code | Fractional extent proportional to `lfWidth`; drives `FitLogFontWidth` |

The two can be combined: the LOGFONT ratio changes the pre-truncation pixel advance, and the percent then truncates each character. Treating percent truncation as LOGFONT width scaling, or the reverse, is therefore incorrect.

## What remains unproven
- Native GDI/Uniscribe rounding and integer `gmCellIncX` advances; no parity with the original is claimed or tested.
- The Mac GDI layer's width rounding, which is the residual noted by the existing `[APPROX:COMPAT-002]` annotation.
- Actual original long-name captures, shipped-font extents and platform/display settings; none are used here.
- x87 precision behavior in the original generator; the separate corpus precision tests were not run, and this task makes no precision claim.
- Any `SignSurfaceComposer` or layout implementation behavior; those sources are outside this task and were not changed.

## Exact evidence still needed
- Settling native rounding requires original-platform captures that record font, `lfWidth`, `lfHeight`, slot percent, platform, display settings and the rendered extent. These are private/original data and are not on the public allowlist; no allowlisted path can substitute for them.
- No additional allowlisted source was needed for the characterization above. If future work wants `FontBuilder` itself, rather than the test-local wrapper, to emit OS/2, the exact path to add to the authorable allowlist is `source/OpenTPW.Tests/TrueTypeFontTests.cs` (read-only in this task).
- `[APPROX:COMPAT-002]` and the fractional-advance extension are not retired. An evidence-only outcome remains valid.

## Historical context (superseded plan)
The first draft, based on `5ccddcab`, was plan-only and stated that `LogFontHorizontalScale` uses average-width scaling and retains fractional advances, while Mac GDI width rounding is unverified. Its proposed steps are historical, not executed:
1. Confirm ownership and rebase against latest main.
2. Compare line extents at fit-loop boundaries before proposing platform-specific rounding.
3. Turn each verified distinction into a deterministic regression fixture.
4. Keep unknown behavior unresolved; an evidence-only outcome is valid.
5. Submit any later implementation with exact evidence, changed scope and test output.

Its acceptance list (long names just below/at/above canvas width, width 0, narrow and wide runs) is now partially realized as the characterization fixtures above. The original caution still applies: synthetic consistency alone is not original-fidelity proof, and no screenshots or runtime checks were performed.

## Ownership and overlap
- Consult COMPAT-003, COMPAT-005 and COMPAT-033, plus the sign-layout owner.
- This task does not overwrite another worker's runtime edits, duplicate an active implementation PR, or change `SignSurfaceComposer`.
- Historical notes about merged UI #433 and languages #423, and evening #439, remain historical context only; they are not evidence for this characterization.

## Completion boundary
- This deliverable is documentation plus tests only; no gameplay or runtime source file is changed.
- The first CI run found the corrected fit-boundary expectation described above; this revision awaits fresh exact-head CI and no pass claim is made.
- No approximation is retired, no runtime or gameplay behavior is claimed complete, and native GDI rounding remains unverified.
