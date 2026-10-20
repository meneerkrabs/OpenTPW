# Draft proposal: ECON-058 live challenge progress messages

## Status and boundary

Plan only, October 18, 2026. No gameplay implementation or runtime tests are delivered by this draft. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

Source baseline: `4c87d5f4b657278578c77730524bc69f9002270e`. Re-check current main and active work before implementation. Register IDs: ECON-058.

## Observed evidence and current gap

`source/OpenTPW/Economy/ParkObjectives.cs`, `Update`, exits on turns whose low two bits are zero, where the recorded original update posts progress. `source/OpenTPW/Hud/ParkHud.ChallengeMessages.cs` handles offered challenges using `Describe`, not in-place running progress.

The supplied Data includes `Challenges.sam`, `Language/English/TAG_SYSTEM.str` and its character table. `BFSTReader`/`StringTemplate` already decode parameterized string records. The unresolved work is challenge-type/tag/field binding and message lifecycle, not a new string parser.

## Proposed bounded scope

Emit bounded running-progress updates using the original tag text and the correct slot fields. Update the existing challenge message rather than accumulating one message per turn. Keep acceptance, deadline, warning and scoring behavior unchanged.

## Work sequence

1. Record challenge type -> tag -> parameter-field mappings with source evidence. Resolve the documented substitution and in-place replacement questions before implementing their behavior.
2. Give the message update a stable identity that cannot overwrite another challenge's offer or unrelated advisor message.
3. Connect the existing parameterized text machinery and specify behavior for a deleted/closed/restored message.

## Acceptance and test plan (not yet run)

- Test supported challenge kinds and every evidenced parameter shape, including time, prize and count formatting; do not add guessed translations.
- Fourth-turn progress updates replace rather than append; message capacity, deletion, closure and accepting/declining still work.
- Save/load, late deadline turns, warning boundaries and completion/failure remove or finalize the correct progress message.
- Existing challenge simulation results/hashes remain unchanged unless a separately evidenced rule requires a change.
- Before publication of runtime changes: focused tests, relevant aggregate suite, exact-head required CI and independent review; private-data skips must be reported as skips.

## Dependencies and ownership

Coordinate HUD/message ownership with the UI owner; #433 was merged before this baseline, but active follow-up ownership must still be checked. Medium effort. Field mapping and replacement semantics are evidence blockers, not permission to invent a format.

Proposal coordinator: Codex. Runtime implementation owner is unassigned until the existing topic owners have acknowledged the boundary. Required next decision is technical ownership/evidence, not a claim that implementation is ready. The source/data audit is read-only; original assets remain outside git.

## Private-input identity (October 18 audit)

Relative paths below are inside the supplied local Data tree. SHA-256 pins audit inputs only; it does not authenticate their community-archive provenance or establish an original edition. No asset bytes are committed. WAD evidence above is a read-only directory/member inventory, not a gameplay test.

- `Challenges.sam`: 12603 bytes; SHA-256 `120fff68adc08120134787cfa24e5e6851dc33388c145dd11ee8a40bc4f4edae`.
- `Language/English/TAG_SYSTEM.str`: 35608 bytes; SHA-256 `525780dbb028b5cca80ad52e628df1b74fa128a0b8b4f750cb35e3ed81283f1a`.
