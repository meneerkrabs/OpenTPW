# Draft proposal: ADVISOR-017 map and options speech stop

## Status and boundary

Plan only, October 18, 2026. No gameplay implementation or runtime tests are delivered by this draft. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

Source baseline: `4c87d5f4b657278578c77730524bc69f9002270e`. Re-check current main and active work before implementation. Register IDs: ADVISOR-017.

## Observed evidence and current gap

`source/OpenTPW/World/Advisor.cs` records that original map/options open calls the stop path (speech stop, model kill and effect-group clear). `AutomaticAdvisor.OnWindowOpened` posts help/mode changes without a complete map/options-specific stop. `Update` pauses the advisor clock/scan but still advances presentation and serves the queue.

The supplied original advisor speech/lip assets support a local playback fixture. Existing recorded call sites are `0x5F0B91`, `0x4A3A92`, `0x5996D0`, `0x51C0E0` and `0x429D60`. These are repository evidence references; this proposal performs no new executable analysis.

## Proposed bounded scope

Wire the complete stop only for the evidenced map and Game Options transitions. Keep ordinary pause, window-close slap behavior and queue scheduling outside this slice. Reuse the existing audio/model/effect lifecycle rather than a new stop implementation.

## Work sequence

1. Identify actual current window identifiers and the existing complete stop primitive; distinguish it from a help withdrawal/slap.
2. Specify pending advice/caption handling and repeated open/close behavior from evidence.
3. Add the narrow transition hook and ensure no effect clear can kill an unrelated world effect.

## Acceptance and test plan (not yet run)

- Opening map/options mid-sentence stops sound, lip/model presentation and the owned advisor effects together.
- Repeated opening, closing, switching and level exit neither replay speech nor duplicate slap/stop events.
- Ordinary pause and unrelated windows keep their intended behavior; pending advice, captions and subsequent playback are tested.
- Native/browser playback smoke checks verify the boundary; no claim to close the per-turn queue or other ADVISOR-017 residuals.
- Before publication of runtime changes: focused tests, relevant aggregate suite, exact-head required CI and independent review; private-data skips must be reported as skips.

## Dependencies and ownership

Coordinate UI and particle effect-group ownership. Do not duplicate #438's documentation of the already implemented help-withdrawal slap, or audio owners' work. Small-to-medium effort after lifecycle semantics are confirmed.

Proposal coordinator: Codex. Runtime implementation owner is unassigned until the existing topic owners have acknowledged the boundary. Required next decision is technical ownership/evidence, not a claim that implementation is ready. The source/data audit is read-only; original assets remain outside git.
