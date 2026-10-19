# UI-003: Selected-mesh fitting and remaining transform parity

## Status and bounded implementation

The earlier proposal treated stripped child translation as the main possible gap.
New static analysis establishes selected-mesh endpoint fitting as a separate,
bounded correction. This implementation preserves that fit metadata and uses it
for `DrawFrame` and inverse shape picking. UI-003 remains open; it is not a claim
of complete native UI fidelity. UI-004 depth, ordering and texture grouping are
unchanged.

## Static evidence and its limits

- The native selected-state handler copies the selected mesh's two stored bounds,
  composes its matrix through its parents, transforms those two endpoints and
  orders X and Y (`0x476fd3..0x47708f`). It does not compute an eight-corner AABB
  or use the model-header bounds.
- The fit builder maps that box into the control rectangle (`0x476c50`). For
  zero control angle, a common XY translation of vertices and endpoints cancels.
  The hierarchical draw also composes node matrices (`0x57a530`). Replacing
  translation stripping alone is therefore not an established visible fix.
- The available UI corpus has 278 simple first-child-chain models. Static
  algebraic replay over 1,185 frames found at most approximately 3.4e-7 of a
  destination dimension from full composition versus the current shortcut when
  both are fitted by their own tight vertex bounds.
- Stored-endpoint fitting versus tight vertex fitting differs by more than
  1e-5 in 125 frames across 13 models. Excluding `s_X1`, the maximum is about
  0.001015 of a destination dimension, often around one tenth of a pixel at a
  100-pixel destination. Live callers include `b_on`, lobby arrow buttons,
  `!slider` and research fluid. `s_X1` has substantial padding, but no current
  production instantiation was found; its large difference is not a demonstrated
  currently displayed defect.
- The available data archive was not freshly matched to the examined executable
  release. These are native-rule replays, not captured original-PC pixels or
  bit-exact floating-point/rasterization validation. Original software was not
  executed, and no original assets or disassembly are included here.

## Implementation contract

- `UiModelFrame.FitBounds` is optional. The existing six-argument constructor and
  geometric `MinX/MinY/MaxX/MaxY`, `Width/Height/Center` keep their meaning.
- `UiModel.Flatten` transforms exactly the selected mesh's two stored endpoints
  with the same existing transform and Y flip as its vertices, then orders axes.
  This intentionally retains the current root-pose shortcut. The bounds and
  vertices must not be placed in different coordinate spaces.
- Drawing and inverse shape picking share a fit resolver. Missing, nonfinite,
  overflowed or nonpositive fit extents fall back to usable geometric bounds;
  unusable fallback geometry produces no draw or shape hit. This is an explicit
  host fallback, not native malformed-input behavior. No small-width epsilon is
  introduced.
- Signed destination dimensions still reflect drawing. Shape picking retains
  its existing rejection of nonpositive destination dimensions. Fitting does
  not add clipping around the destination rectangle.
- Authored layout queries and direct-origin/scale `UiBatch.AddModelFrame` remain
  geometric. State order, root fallback, hierarchy transforms and UI-004 remain
  unchanged.

## Synthetic verification

`UiModelFitTests` builds synthetic meshes and checks actual CPU draw vertices and
`UiModelImage.CoversPoint`, without game fixtures or a GPU:

- Padded nonzero-origin fitting, triangle interior/edges and unoccupied padding.
- Unchanged geometric metadata, authored rectangles and direct-origin submission.
- Translation cancellation, reflected endpoints and signed destinations.
- A sheared transform that distinguishes two-endpoint fitting from an
  eight-corner box and retains geometry outside the control rectangle.
- Legacy manual frames, invalid optional/source bounds and unusable geometry.
- Three selected meshes with different stored fit bounds, repeated selection
  transitions, retained root-pose placement and existing out-of-range fallback.

Local .NET was unavailable when these tests were prepared. Repository/Python
checks and independent algebraic assertions are distinct from compiling or
executing the C# suite. Exact-head CI and source review are required before
publication is considered validated; no rendered/native pixel comparison is
claimed.

## Remaining work

General ancestor scale/rotation composition, control-angle handling, linked
first-child state traversal (including empty/dummy states), native out-of-range
behavior and rendered comparisons remain open. Any later hierarchy change must
preserve geometric layout consumers or explicitly migrate them. UI-004's depth
and render-order uncertainty must be investigated separately, not inferred from
this fit correction.
