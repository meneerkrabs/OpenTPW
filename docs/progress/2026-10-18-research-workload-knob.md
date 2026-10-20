## Research workload knob: persistent raw state and capacity presentation (October 18, 2026)

Implemented the source-backed portion of UI-064 in the research window:

- Keep raw slider value 0..1023 separately from the rounded workload. Ordinary frames, draw, effort and target
  changes do not discard sub-workload steps. Opening and changed researcher ability reproject through the normal
  value-change path; keyboard workload adjustment explicitly synchronizes the raw value as an existing extension.
- A qualified completed track click changes one raw unit, using the saved press point and current ball bottom.
  It does not page on button-down or turn into a grab during hold. The narrower track and protruding ball retain
  distinct hit eligibility. A small opt-in captured-click path preserves the original recipient without changing
  ordinary widgets' click routing or OpenTPW's existing shared time/movement qualification.
- Every drag uses the immutable press-time rectangle and pointer, including overshoot followed by reversal.
  A stationary held frame does not undo a staffing refresh. Release normalizes from raw value; removal,
  lost capture, read-only/disabled transitions and reopening cancel the old interaction.
- Drive the existing cameo through its nine workload states and draw the staffing-capacity gauge with the
  original bottom-up split and cropped UVs. Its left texture half is filled and right half empty. Geometry/UV
  tests inspect the actual draw vertices; workload-only changes leave the capacity gauge unchanged.

The evidence is static PE disassembly, decoded research control-table metadata and private inspection of the
original gauge texture orientation. No original executable was run, and no original asset bytes, images or
private hashes are included. Generic model rendering, filtering and global double-click behavior are unchanged.

UI-064 remains open for raw-zero startup geometry: a constructor-time unchanged SetPos can retain the table's
ball top399, but later native initialization is not fully traced. The existing safe host normalization to
bottom634 is retained. This bounded implementation does not claim complete original-runtime parity.

Validation: whitespace, fidelity-register and progress checks passed, along with all 108 top-level Python tests
(after generating the ignored fidelity document). The evidence runner completed successfully: 1,537 tests,
595 skips; the review lane reports seven expected failures, not unexpected regressions. The focused
C# tests cover real screen input, raw persistence, page/drag/cancel/reopen/ability-change flows, read-only guards,
hum changes, cameo boundaries and gauge vertices/UVs. They were authored but could not be executed locally
because the .NET SDK is unavailable. Independent source review and exact-head CI are required before publishing
this slice as verified; the unresolved startup edge prevents retiring UI-064 as a whole.
