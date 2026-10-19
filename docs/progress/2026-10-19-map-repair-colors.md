## Default map object fills use live repair colors (October 19, 2026)

The default placed-object map fill now reads the exact current object's economy repair and uses the
traced native integer ramp: red (248,0,0), orange (248,200,0), green (48,252,0) at 0/50/100, alpha 255.
The host double is first converted to the original stored-float domain, then truncated and masked to
its low byte before clamping. The 16.16 fraction and arithmetic channel shifts preserve the original
integer rounding. The PC field `+0x44` is the already-imported `StateOfRepair`, not an unidentified value.

The map-local guard rejects read-only visits, stale Status/current-level economy mismatches, missing
bindings/records and mismatched instance/type identities. Unsupported nonfinite/out-of-int-range
values also retain the prior exact magenta fill; missing state never invents a healthy value. Category
filters, all occupied-cell geometry, clipping, path/queue/unowned colors and later host overlays stay intact.

Synthetic C# tests target all ramp integers and boundary/float/wrap cases, actual `ParkMapView.Draw`
triangles, fallback/current-state lifecycle, viewport clipping, host overlay precedence and no mutation
of economy/save/RNG/object state. The local environment has no dotnet SDK; these C# tests and the full
solution build await exact-head CI. Six map source/algorithm guards, all 133 top-level Python tests
(after generating the ignored fidelity register), fidelity/progress validation and the evidence aggregate
(1,537 cases, 595 skipped) pass locally; `git diff --check` is clean. These checks do not substitute for
executing the C# tests.

UI-086 remains open for native value modes 2/3 and controls, class/track priority, outlines, original
footprints and item dots. Static algorithm evidence and synthetic draw tests are not a native runtime
or pixel-equivalence claim. See the [maintained implementation and residual contract](../proposals/ui-086-restore-map-object-layer-colors.md).
The October 18 plan-only progress entry is preserved unchanged.
