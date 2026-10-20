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

## Full-register coverage addendum (October 18, 2026)

This reviewed addendum extends the bounded initial plan to cover the remaining registered residuals. All acceptance work remains pending. Source snapshot independently verified by the proposal coordinator: `5ccddcab56ffb9bda4ded1821988a8492bee8943`.


## Status and classification
- Classification: approximation.
- Plan-only addendum: no runtime implementation, annotation retirement or merge performed.
- Existing PR: #445; residual addendum only.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
CELLS: a refused placement is one verdict for the whole footprint (all red, no arrows) where the original validates each cell and each access outside cell on its own and turns only the refused ones red; the footprint cells of types 1, 11, 16 and 23 ('@', '+', '#' and track ends), which 0x528A70 stamps through their own branches, are drawn blue; the track symbols (m_direct, m_link, m_end, m_break, m_erase) and the arrow models garrow.MD2 and rarrow.MD2 are not drawn; the validator's answer (0x535670 after 0x5358B5) for a line cell that already holds the line's type is not read, such cells before the refusal stay blue; the Buy Land ghost marks the cells along the rectangle's edges where the original draws a ghost fence model along them (0x53AAC0 with the tool mode | 0x100); m_inout (7, no queue laid) is never chosen because OpenTPW has no auto-laid queue option

## Source inspection
- `source/OpenTPW/Hud/ParkHud.cs:1095`: 1096: var marker = landTool.HoverRefused ? BlueprintMarker.Refused : BlueprintMarker.Allowed;; 1097: foreach ( var (x, y) in landTool.PreviewEdge() ); 1098: cells.Add( (x, y, marker) );; 1099: }
- `source/OpenTPW/Hud/ParkHud.cs:1102`: 1103: foreach ( var (x, y) in clearTool.PreviewEdge() ); 1104: cells.Add( (x, y, BlueprintMarker.Allowed) );; 1105: }; 1106: else if ( level.PatrolTool is { IsActive: true } patrol )
- `source/OpenTPW/World/Original/BlueprintGhosts.cs:14`: 15: public static List<(int X, int Y, BlueprintMarker Marker)> LineCells( IReadOnlyList<(int X, int Y)> line, (int X, int Y)? stoppedAt ); 16: {; 17: var cells = new List<(int X, int Y, BlueprintMarker Marker)>( line.Count );; 18: var refused = false;
- `source/OpenTPW/World/Original/BlueprintGhosts.cs:59`: 60: public static (List<(int X, int Y, BlueprintMarker Marker)> Cells, Dictionary<(int X, int Y), byte> Directions) Footprint(; 61: ObjectShape shape, int anchorX, int anchorY, int rotation, bool allowed ); 62: {; 63: var cells = new List<(int X, int Y, BlueprintMarker Marker)>();

## Bounded work
PROPOSED addendum extending #445: its existing scope covers per-cell feedback and marker textures only. Add evidence-backed Buy Land fence ghost work; it is not already covered by that PR. Preserve the original loader rejection of garrow/rarrow models: arrows remain unsupported and evidence-gated, not an assumed implementation target. Record auto-queue m_inout dependency separately.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode same-line validator verdict, Buy Land fence ghost model/placement, and auto-laid queue option before claiming the entire ID complete. Preserve garrow/rarrow original-loader rejection; require evidence of supported format and original use before proposing arrow support.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Mixed valid/refused footprint colors only refused cells red; preserve the existing original-loader rejection of garrow/rarrow rather than silently drawing unsupported arrows.
2. Track ends and erase/cut marker textures remain under existing #445 scope. Proposed fence ghost extension must match the traced edge placement; any arrow support first requires new loader/format evidence. No-queue mode selects m_inout only with an implemented option.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Review existing PR #445 current head; use residual addendum, not duplicate proposal.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
