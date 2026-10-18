# Draft proposal: BLUEPRINT-003 per-cell feedback and track markers

## Status and boundary

Plan only, October 18, 2026. No gameplay implementation or runtime tests are delivered by this draft. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

Source baseline: `4c87d5f4b657278578c77730524bc69f9002270e`. Re-check current main and active work before implementation. Register IDs: BLUEPRINT-003.

## Observed evidence and current gap

`source/OpenTPW/World/Original/BlueprintGhosts.cs`, `Footprint`, takes one `allowed` Boolean, so a single refusal colors the entire footprint red. The register also describes plain track-cut previews where specific symbols are missing.

The supplied `generic/dynamic` has `m_direct`, `m_link`, `m_end`, `m_break`, `m_erase` textures and `garrow.MD2`/`rarrow.MD2`. The loose arrows are documented as rejected by the original model loader; their presence is not authorization to relax decoding. See `docs/reverse/BLUEPRINT.md` and `docs/FORMAT-BACKLOG.md`.

## Proposed bounded scope

Separate implementation slices within one proposal: per-cell/access-cell refusal feedback, then the evidenced track marker textures and directions. Do not enable unsupported arrow models, add a guessed auto-queue option, or change construction validation while painting its result.

## Work sequence

1. Expose a pure per-cell diagnostic view of existing placement validation, including its original ordering/sticky-refusal behavior where applicable.
2. Pin marker selection/direction for each covered track operation to the recorded branches.
3. Render those diagnostics without applying a placement, charging money or rebuilding gameplay state. Coordinate shader/material choices with evening marker readability.

## Acceptance and test plan (not yet run)

- Mixed valid/refused footprints and access cells at all four rotations produce the correct individual markers.
- Bounds, ownership, occupancy and insufficient-money failures are distinct fixtures; repeated preview/cancel is side-effect free.
- Track cut/erase/link/end previews match their evidenced operation, including direction and multi-cell coverage.
- Native/browser captures, including #439 evening mode if merged, keep feedback readable. Preserve unsupported-arrow and other BLUEPRINT-003 residuals.
- Before publication of runtime changes: focused tests, relevant aggregate suite, exact-head required CI and independent review; private-data skips must be reported as skips.

## Dependencies and ownership

Coordinate HUD/tool ownership and the TRACK-018 proposal. Medium effort. No relaxation of original-data decoder rejection; no scope expansion into tour/particle/UI layout work.

Proposal coordinator: Codex. Runtime implementation owner is unassigned until the existing topic owners have acknowledged the boundary. Required next decision is technical ownership/evidence, not a claim that implementation is ready. The source/data audit is read-only; original assets remain outside git.
