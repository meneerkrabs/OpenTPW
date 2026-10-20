# UI-901: HUD happiness gauge texture response

## Status and bounded scope

The original plan-only proposal is now a bounded implementation based on static source evidence.
The custom red/yellow/green rectangle has been replaced with the original vertical meter's cropped
image halves. UI-901 remains open for actual rendered shell/meter composition and alpha verification.
Independent code review and exact-head CI are required before merge. Original-runtime or pixel
parity is not claimed. The historical proposal progress entry is preserved unchanged.

## Traced HUD binding

- HUD builder `0x4a1d70` reads table `0x752940`. Shell 0x1e is `guage` (host alias `gauge`),
  rectangle (67,1080)-(163,1345), help477. Child 0x1f is type9, attributes3, rectangle
  (85,1100)-(144,1324). Shell and child use independent absolute bounds in the host.
- Calls `0x4a2497` and `0x4a24a8` construct the vertical model `0x477870` with `meter.wct`
  and attach it to the child's +0x98 model slot. The HUD does not supply a second +0x9c model.
- Type9 installs vtable `0x705c28`; setter `0x66d885` clamps to 0..1024 and sends message8.
  The vertical model's vtable `0x6fee88` dispatches to `0x477db0`; branch `0x477f4e` stores
  percent and updates geometry (`0x477cc0`) and UVs (`0x477d20`). This is a crop, not a frame map.
- Constructor zero is already empty. All 1,025 integer raw values were checked against the
  stored-float percent/UV arithmetic: the resulting fraction equals raw/1024 exactly.

Static inspection did not run or emulate the original executable. Original textures and binary
excerpts stay private; no original media, hashes or disassembly are included in this change.

## Rendering contract

Clamp raw value to 0..1024; let q = value/1024 and split = y + height × (1-q).
The empty top region samples the right texture half, UV (0.5,0)-(1,1-q).
The filled bottom region samples the left texture half, UV (0,1-q)-(0.5,1).
Both regions crop upright art at the same boundary; neither stretches a whole half into its region.
White vertex tint preserves the image's colours and alpha. Zero-height quads are omitted by UiBatch.
Null mean uses raw0 and still draws the empty half. No host red/yellow/green colour calculation remains.

The runtime loads `/ui/textures/meter.wct` through the existing image/texture-pack pipeline.
Missing or malformed images follow UiRenderer's warn-once/skip path; no substitute graphic is added.
No new normal/small texture selection, filtering, shader or depth policy is introduced.

The shared `UiVerticalGauge` body is extracted unchanged from `ResearchCapacityGauge.DrawGauge`,
with an explicit texture path. Its research wrapper and `/ui/textures/effortguage.wct` ArtPath remain;
the staffing limit still supplies its value. UI-064 input/state and startup geometry are unchanged.

## Sampling and layout

The existing two-second sampling, byte-truncated in-park mean, closed-park behavior and raw-value
calculation are unchanged. Mean50 still maps to511; mean100 still maps to1023, leaving a small empty strip.
The child is 59×224 inside the 96×265 shell, with insets 18/20/19/21. Both use BottomLeft once;
no parent offset or second scale is applied. Widescreen anchoring remains host layout policy.
Shell help477 is retained, but its actual tooltip dispatch is not established by storing Help:
UiModelImage is nonfocusable and existing generic hit testing selects focusable elements only.
Tooltip routing is explicitly outside this drawing patch.

## Verification and remaining work

- Synthetic C# batch tests cover raw -1,0,1,256,511,512,768,1023,1024,2048; UV halves, crop
  direction, shared split, white alpha, texture path and zero-height omission.
- Real HUD construction tests check shell/child ordering, independent bounds, 4:3, widescreen and
  width-limited mapping. The actual sampling method is exercised over two-second boundaries,
  no guests, absent simulation, closed park and byte truncation, with simulation counters unchanged.
- Resource tests exercise the existing renderer's CPU failure branch with missing/truncated synthetic
  meter files and assert no replacement resource. They also retain shell drawing and sampling.
- Research tests retain their existing suite and add edge fractions, white tint and effort-independence.
- Python source integration guards run in CI and check the derived arithmetic independently; they do
  not execute C# and are not evidence of runtime rendering or original-pixel parity.
- This authoring environment has no .NET SDK/compiler. C# tests/build and actual GPU/rendered captures
  were not run locally. Private texture inspection supports art orientation only, not composition.
- Before considering UI-901 retirement, run exact-head C# CI, independent source/code review and
  private actual-renderer captures at raw0,511,1023,1024 with the original shell and meter, including
  alpha and repeated draws at 4:3 and widescreen. Generic renderer qualifications retain their owners.
