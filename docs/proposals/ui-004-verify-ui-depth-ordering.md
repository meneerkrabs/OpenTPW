# UI-004: Verify UI depth ordering

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
UiModel groups triangles by material and sorts by depth; device depth initialization alone does not establish per-UI draw state.

## Source sites
- `source/OpenTPW/UI/Original/UiModel.cs:138` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/UI/Original/UiModel.cs:141`: `var groups = new Dictionary<string, List<(float Depth, int A, int B, int C)>>( StringComparer.OrdinalIgnoreCase );`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Per-UI pass depth enable/write/compare and submission order trace.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Compare crossing textured parts under sort and depth-test approaches; propose isolated UI depth pass only with state evidence.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Interpenetrating parts; same-depth ties; opaque and transparent surfaces; root/child states; world/HUD depth isolation.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-003, UI-034 and render pipeline owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.

## Public-source investigation addendum (2026-10-22)

This is an algorithmic counterexample, not a native-parity proof or an implementation.
The original proposal and its evidence/ownership gates above remain in force.
Pinned implementation baseline: `16e4213ad5e45ee0d31bb5b15646c9a3a3f39364`.
The proposal branch inspected was `6cb4bfcd2a6734f4d577ec839f00e662e123ffce`.
Only public source was inspected; no original executable, assets, capture, GPU,
.NET test suite, browser, or native game was executed for this addendum.

### A material-average ordering counterexample

Use three triangles with identical XY positions `(0,0), (4,0), (0,4)` and constant
per-triangle authored Z. At interior sample `(1,1)` all three overlap:

| Triangle | Texture group | Z of every vertex | Synthetic sampled RGB |
| --- | --- | --- | --- |
| A0 | A | 0 | red `(1,0,0)` |
| A9 | A | 9 | green `(0,1,0)` |
| B6 | B | 6 | blue `(0,0,1)` |

A0/A9 can sample different UV regions of the same synthetic texture A; this does
not require different per-vertex tint in `UiModel`. Both material slots are
unkeyed in this example. The alpha examples below specify sampled alpha, not the
`Transparent` flag, which selects a keyed resource in the current code.

`Flatten` uses case-insensitive texture groups, sorts each group's triangles by
ascending mean vertex Z, then sorts groups by their mean triangle depth.
Here group A averages `4.5`, group B averages `6`; no tie or dictionary-order
assumption is needed. Current submission is `A0, A9, B6`. Global ascending-Z
submission is `A0, B6, A9`. The nonadjacent A triangles cannot remain a single
texture group while preserving that global order. Adjacent transparency-flag
run splitting inside A does not repair the cross-texture inversion.

For this constant-depth geometry, using the current comment's lower-Z-first
painter convention, last opaque sample wins: current grouping produces blue,
global sorting produces green. That establishes a difference from the stated
painter convention on a synthetic input, not a demonstrated defect in original
assets or evidence that global sorting matches the native renderer.

For an explicitly hypothetical depth alternative, map authored Z to
`d=(10-Z)/10`, clear depth to 1, accept `d <= stored`, and write accepted depth.
The same grouped opaque submission then produces green (A9); all six submission
permutations produce green because depths are distinct. This mapping is only a
synthetic illustration and must not be mistaken for the original projection.

For sampled alpha `1/2` over black, use source-over RGB
`out = alpha * source + (1-alpha) * destination`:

| Model | Result RGB |
| --- | --- |
| Grouped painter | `(1/8, 1/4, 1/2)` |
| Globally sorted painter | `(1/8, 1/2, 1/4)` |
| Grouped with hypothetical depth test **and writes** | `(1/4, 1/2, 0)` |

The last variant rejects B6 after A9 and loses its translucent contribution;
merely enabling depth writes is therefore not a general replacement for sorted
alpha composition. Conversely, global sorting by one mean Z is not a general
solution for intersecting/sloped triangles whose front/back relationship changes
across pixels. This example deliberately avoids that separate problem.

### Current renderer-state ownership map

All paths below are at the pinned implementation baseline, not claims about
original executable state:

- `UiModel.Flatten` in [UiModel.cs](../../source/OpenTPW/UI/Original/UiModel.cs)
  retains transformed vertex Z for CPU ordering and emits material parts.
  `GetFrame` selects one state frame or the root fallback; this investigation
  does not change frame hierarchy/transform ownership (UI-003).
- `UiContext.DrawFrame` in [UiWidgets.cs](../../source/OpenTPW/UI/Original/UiWidgets.cs)
  iterates parts/indices and emits `UiVertex` with XY/UV/color only. Z is discarded
  here; `TextureFor` preserves each part's keyed-resource choice.
- [UiBatch.cs](../../source/OpenTPW/UI/Original/UiBatch.cs) has a 2D `UiVertex`.
  `Current` coalesces only adjacent equal resource keys, without global texture
  regrouping. `AddModelFrame` also converts to 2D and does not forward Z.
- [UiScreen.cs](../../source/OpenTPW/UI/Original/UiScreen.cs) draws background,
  visible elements in insertion order, then overlay. Its stack draws bottom to
  top after `HidesBelow` selection, inserts modal dimming, then help. These
  cross-control ordering boundaries must not be replaced by a frame-global
  triangle sort. No `UiScreen` changes are included (avoid #756 overlap).
- [UiRenderer.cs](../../source/OpenTPW/UI/Original/UiRenderer.cs) constructs the
  ordinary overlay pipeline with `SingleAlphaBlend` and depth disabled; its
  invert pipeline also disables depth. `Draw` submits `batch.Draws` in order.
  [ui-batch.shader](../../content/shaders/ui-batch.shader) receives XY and writes
  clip-space `z=0`; flipping a pipeline depth switch alone cannot restore model Z.
- The **separate** `UiDepthImage` preview path uses a 3D vertex, its own color/depth
  target, clear depth 1, depth test/write with `LessEqual`, blending disabled,
  then presents the result as a normal batch quad. Its
  [ui-depth.shader](../../content/shaders/ui-depth.shader) discards sampled alpha
  below 0.5 and outputs alpha 1. It is an opaque/cutout path, not equivalent to
  the ordinary overlay's fractional-alpha composition.
- [GameFlow.cs](../../source/OpenTPW/Client/GameFlow.cs) builds the HUD/menu batch,
  adds fade/progress/cursor ordering, calls `PrepareDepthImages` outside the
  overlay pass, then `RenderOverlay` calls the ordinary renderer. Preview target
  restoration is explicit in `PrepareDepthImages`. A future isolated UI depth
  design needs this pass owner, not just a `Flatten` edit; scene/HUD isolation
  remains an acceptance gate rather than a tested result here.

### Existing coverage and narrowly new follow-up

[UiMaterialRunTests.cs](../../source/OpenTPW.Tests/UiMaterialRunTests.cs) already
covers alternating same-name slot flags, stable equal-depth order, texture-group
averages, adjacent run splitting, and resource-key/shape retention. Its ordering
fixture intentionally uses separated XY triangles; it verifies the present
ordering contract, not overlapped-pixel correctness. Do not duplicate those
checks or rewrite their expected order before choosing a rendering policy.

The new research result is the overlap example and state handoff map above.
A small standalone Python rational-arithmetic model was run locally: it checked
both orders, opaque winners, the three half-alpha RGB results, and six opaque
depth-tested permutations. It models the stated algorithms only; it is not a
C# regression, GPU readback, browser test, CI result, or native-fidelity test.

Before runtime implementation:
1. Obtain per-UI-draw original depth enable/write/compare, alpha/key state,
   projection/range, submission-order and restore evidence. Existing address
   comments remain leads, not independently verified evidence from this pass.
2. Agree with the render owner whether ordering is per model/control, an isolated
   depth image/pass, or another scoped policy; retain screen/HUD/modal ordering.
3. Add overlapping constant-depth cross-material coverage, then distinguish
   cutout from fractional-alpha cases and test sloped intersections, equal-depth
   ties, root/child state changes and world/HUD isolation under that policy.
4. Run required runtime/visual verification and independent review before
   claiming implementation or native parity. Keep #466 draft and UI-004 open.
