# UI-086: Restore map object layer colors

## Status and scope

The first bounded implementation replaces the default fill of live placed-object cells with the
original repair ramp. It retains the current category filters, geometry, clipping and later host overlays.
UI-086 remains open; this is neither a full implementation of the native map nor a pixel-parity claim.
The October 18 plan-only progress fragment remains historical; this maintained proposal records the new scope.

## Verified default repair contract

- PC map draw `0x5f2602` and the other default object-class branches read object `+0x44` as a float,
  truncate with `__ftol`, mask the low byte with 255 and pass it to `0x5f09a0`.
- That PC field is `SaveAttraction.StateOfRepair` in `SaveThingRecords.cs`, imported into
  `ParkObjectState.Repair` by `OriginalEconomyImport.cs`. The Mac `LifeGauge` offset comment refers
  to a different layout; it does not make the PC map field unidentified.
- The ramp constructor at `0x5f08a5..0x5f08df` supplies RGB (248,0,0), (248,200,0), (48,252,0).
  The gradient clamps its integer input to 0..100 and splits at 50. Its fraction is
  `(distance << 16) / 50`, and each channel is `start + (((end - start) * fraction) >> 16)`.
  The right shift is arithmetic; alpha is 255. Floating-point color interpolation is not equivalent.
- Static source/instruction evidence and independent rational-floor calculations support this bounded
  algorithm. No original software was executed; no executable bytes, assets, disassembly or private
  reference metadata are included in this proposal or its synthetic tests.

## Runtime implementation

`ParkMapPalette.RepairFill` converts the host double back to the original stored-float domain before
truncation. Nonfinite or outside-supported-int stored values return no color. Bounds are compared as
doubles, so `int.MaxValue` cannot round upward to an accidentally accepted float value. The supported
integer conversion takes the low byte before the gradient clamp, including wrapping negative values.
Malformed-input fallback is host robustness, not a claim to reproduce undefined x87 behavior.

`ParkMapView.ObjectFill` requires a writable current level, an identical current `Status`/`level.Park`
economy, the exact `OriginalObject` binding and matching state `Id` and `InfoId`. It reads the live
repair on each draw. Missing, read-only, mismatched or malformed state retains the previous exact
(207,67,198,235) fill; no catalogue default or invented healthy value is substituted. Deleted/removed
objects are still excluded by the existing draw enumeration. The shared `LiveState` helper is unchanged.

## Validation and evidence limits

- `MapRepairPaletteTests`: all 0..100 integer colors (plus surrounding values and integer extremes)
  against an independent decimal rational/floor reference; explicit -1/0/1/25/49/50/51/75/99/100/101
  vectors; fractional values, float-precision transitions, low-byte wrapping and safe integer bounds;
  nonfinite/overflow rejection.
- `MapRepairDrawTests`: actual `ParkMapView.Draw` CPU triangles for two current same-type objects with
  distinct repairs, all occupied cells and category filters; original/sandbox offsets; partial viewport
  clipping; repair and binding changes between draws; deletion/removal, missing state, mismatched
  `Status`/current economy, wrong `InfoId`/record `Id`, visits and malformed values; exact fallback;
  unchanged path/queue/unowned colors and host track/excitement/staff/visitor/happiness/thought order.
- Draw immutability checks compare economy capture, canonical economy hash, economy/guest/coaster
  random state, bindings, objects, placement, occupied cells and script variables.
- `tools/test_map_repair_contract.py` supplies six source/algorithm guards, wired into build CI.
  These and the repository Python/evidence checks are separate from executing C#. The local
  environment has no dotnet SDK, so the new C# tests and full solution build require exact-head CI
  before merge. Synthetic CPU draw coverage is not native runtime or original-game rendered evidence.

## Remaining UI-086 work

Native value modes 2/3 call excitement `0x4e0860` and satisfaction `0x4e1e30` before ordinary category
gating for applicable cell classes. Their controls and land modes, exact cell-class/track priority,
track pass `0x5f2fd0`, open-status neighbor outlines and style argument 8, item dots and original
footprints (`0x5f44d0`) remain unresolved/incomplete. Existing independent host metric toggles do not
implement that native dispatch and have not been relabeled as native behavior. Real-data captures and
an original rendered comparison remain outstanding. No approximation ID is retired by this slice.

## Ownership and completion gate

The existing UI-086 proposal PR owns these map changes. Independent source review and full required
CI for the exact published head must precede merge. Other active implementations are not dependencies
or alternate publication lanes. Historical progress fragments must not be rewritten.
