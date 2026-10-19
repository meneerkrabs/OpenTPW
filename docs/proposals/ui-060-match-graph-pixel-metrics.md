# UI-060: Match graph pixel metrics

## Status and classification

- Implemented and narrowed (PR #483, merged October 19, 2026); a rendered composition check remains open.
- Classification: `approximation`; UI-060 is narrowed, not retired.
- This slice changes the shared backdrop only. It does not establish original display-mode,
  financial-history or framebuffer equivalence.

## Verified backdrop contract

Static inspection of the shared native setup and drawing helpers supersedes the old proposal's
three-interior-line assumption. Setup `0x4806a0` supplies geometry and colors; grid routine
`0x47fcd0` draws ten horizontal divisions and twelve labeled vertical spans.

- Opaque black background; grid RGB (0,100,0); axes and step labels RGB (0,200,0).
- Bitmap plot left/top are 4; right/bottom are bitmap width/height minus 20 (`0x47fb50`).
- Periods 12/36/144 advance the horizontal coordinate at every sample, labeling every
  first/third/twelfth step. The labels are sample numbers, not calendar month names.
- Coordinates use stored single-precision accumulation and truncate when emitted. Replacing
  accumulation with index multiplication, or forcing the final endpoint, can move a line.
- Horizontal/vertical primitives take pixel counts. Grid spans use the stored right/bottom
  values; axes use those counts plus one. They intentionally extend beyond the plot rectangle.
- A nonnegative minimum puts the horizontal axis at bottom + 1. A negative minimum in a
  zero-inclusive scale puts it at bottom + trunc(height × minimum / range), without the extra
  pixel (`0x47fc50`). Flat zero-valued series retain their separate baseline at bottom.
- Draw order is full clear, plot fill, vertical spans with their labels, horizontal spans, axes,
  then the existing series. Empty histories and deselected series retain the backdrop.

Completed research comprised 106 static and synthetic assertions. The original executable was
not run or emulated for this slice. Derived behavior and synthetic fixtures are publishable;
original executable material and private investigation artifacts are not included.

## Implementation and preserved behavior

`source/OpenTPW/UI/Original/GraphBackdrop.cs` contains immutable bitmap geometry and the shared
host draw adapter. Both `ParkHud.FinanceWindow.cs` and `ParkHud.InfoGraph.cs` consume it. The
canonical `UiApproximations.cs` entry and the UI documentation describe the same remaining scope.

Finance retains all eight value mappings and colors, selected-series masks, zero-inclusive
extrema, default bank balance, period/month selection, fractional series normalization,
sampling and marker-before-line ordering. It maps its drawing area to the shared plot but does
not snap series points onto integer backdrop coordinates. Park information retains Column/Row,
ring selection, ScaleMaximum, SeriesPoints, series order, sample marks, dedicated rating and
last-value text. No simulation or persistence code changes.

Host safety adaptations are explicit:

- Signed 128-bit multiplication and range subtraction preserve long-valued finance scales;
  original signed-32-bit overflow is not reproduced.
- Tiny rectangles and unsupported sample periods clear the background without a plot.
- Nonfinite frames, including overflowing right/bottom sums, emit nothing. The adapter tells
  callers not to emit series after a rejected frame or background-only fallback.
- The existing max(1, Canvas.Scale × 2) pixel policy is retained. The clear covers a fractional
  host-frame remainder; Small-font label placement remains the existing host text adapter.
- Wholly negative ranges cannot arise from current zero-inclusive scale selection. Their
  bottom-axis fallback is a host guard, not an additional native arithmetic claim.

## Tests and current evidence limits

Added 69 C# test cases cover the pure bitmap contract and the actual graph controls using
synthetic glyphs and independent CPU-batch expectations:

- Every rectangle, glyph, text record, color and draw-order run for both graphs and all periods.
- Empty/no-economy/deselected states, one live point, zero/positive/negative/mixed financial
  values and long extrema, all eight mappings, paired/cash-flow toggles and history windows.
- Preserved fractional finance series and mark ordering; park-information points, series
  order, dedicated rating and last-value labels.
- Repeated Clear/Draw, real period/toggle/close/back/reopen actions, unchanged histories,
  save JSON, world RNG state and run hash.
- Host canvas scales, fractional clears, tiny controls, unsupported periods and nonfinite
  coordinates that must not leak into series vertices.

The C# tests and the ten Python source-integration and synthetic-arithmetic checks run in CI; the post-merge review ran the full with-data suite (8788 passed).

## Remaining qualification and ownership

UI-060 remains open for original display-mode/host pixel calibration, financial sample-to-month
correspondence and stretching of available months, font and palette handling, and actual
raster/composited-output fidelity. A rendered host check must still inspect clipping/overlap and
opaque green-on-black output in both windows; that check alone would not prove native parity.

UI-914 retains ownership of park-information history and its other presentation residuals.
Existing UI #433 and languages #423 remain baseline work. The implementation kept the same
UI-060 ID rather than creating another owner or another approximation ID; the residual ID is not
retired by the backdrop change.
