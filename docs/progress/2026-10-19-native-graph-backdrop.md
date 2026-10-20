## Finance and park information share the traced graph backdrop (October 19, 2026)

UI-060 is narrowed, not retired. Both graphs now use a shared bitmap-space backdrop with an
opaque black background, dark-green grid RGB (0,100,0), and green axes and step labels RGB
(0,200,0). The plot has left/top 4 and right/bottom equal to the bitmap width/height minus 20
(`0x4806a0`, `0x47fb50`). The earlier register statement that the native setup draws three
interior grid lines was incorrect: `0x47fcd0` draws ten horizontal divisions and twelve labeled
vertical spans, labeled 1..12, 3..36 in threes, or 12..144 in twelves for the selected period.

The grid advances stored single-precision coordinates once per sample before integer truncation;
its coordinates are not index-times-step approximations or forced final endpoints. Horizontal
and vertical primitives use pixel counts, so their spans can extend beyond the stored plot bounds.
For a nonnegative minimum the horizontal axis is one pixel below the plot; with a negative
minimum in the supported zero-inclusive scale it uses the traced signed zero row without that
extra pixel (`0x47fc50`). Signed 128-bit integer arithmetic safely widens this calculation for
OpenTPW's long financial values; original signed-32-bit overflow behavior is not claimed. Wholly negative ranges,
including negative flat ranges, retain the bottom axis as a safe host fallback; current finance
scales always include zero and therefore do not produce those ranges.

The backdrop remains visible with empty histories, all-zero values or deselected series. Unsupported
periods and tiny host rectangles get background-only rendering as a host safety guard, not an
inferred native rule. Nonfinite host frames emit no vertices, and the shared adapter tells both
callers when series rendering must stop as well. The shared draw order is background/plot fill,
grid and labels, axes, then series. Finance retains its month selection, toggles, range selection, fractional series coordinates,
colors, ordering and marker-before-line behavior. Park information retains its independent Column/Row
series coordinates, series order, rating history and last-value presentation. The backdrop changes
neither model histories nor persistence or RNG logic.

Synthetic regression tests cover the bounded implementation (C# and ten Python source-integration and synthetic-arithmetic guards); they ran in CI and in the post-merge review (full with-data suite 8788 passed). No rendered host check or original-runtime comparison
has run for this change. Static evidence and independently authored synthetic arithmetic checks
establish the backdrop contract, not complete native runtime or pixel equivalence.

UI-060 remains open for the retained two-canvas-unit pixel adapter and original display-mode
calibration, financial sample-to-month correspondence and stretching of available months, and font,
palette, raster and composited-output fidelity. A rendered host check remains open. This is [PR #483](https://github.com/meneerkrabs/OpenTPW/pull/483). The [proposal](../proposals/ui-060-match-graph-pixel-metrics.md)
records the updated implementation and remaining qualification.
