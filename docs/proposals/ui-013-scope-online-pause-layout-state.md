# UI-013: Scope online pause layout state

## Current bounded implementation (2026-10-20)

#470 (merged 2026-10-20, exact-head CI green) implements the authenticated host-session pause-menu spacing adapter described in [AUTHENTICATED-PAUSE-LAYOUT](../AUTHENTICATED-PAUSE-LAYOUT.md), with synthetic lifecycle, geometry and hit-region fixtures. The plan-only statements below record the original proposal, not the merged change. The native online lifecycle is read: tp.exe's net-session field `+0x60` is set only by the login reply "Authorization OK" (`0x5b6363`) and cleared by the login errors (`0x5b6375`-`0x5b63dd`) and the login teardown (`0x5b6588`), which is the logged-in state the adapter samples. No visual parity claim is made; UI-013 remains open for font-bank parity.

## Historical proposal status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Dialogs and load timestamps exist; pause menu height follows font-tier extension and online zero-spacing variant lacks reply-state linkage.

## Source sites
- `source/OpenTPW/UI/Original/UiDialogs.cs:115` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/UI/Original/UiDialogs.cs:118`: `public static UiScreen TextMenu( string name, IReadOnlyList<(string Id, Func<string> Text, Action Clicked, bool Enabled)> items, Action back, string? focus = null )`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Original net-session +0x60 lifecycle and verified menu measurement for target display mode.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Limit proposal to online reply-state spacing and documented font-tier differences; do not reopen completed dialog/timestamp/hover work.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Offline top/gap; online field 0 and 1; error clears field; long menu labels; eight-frame hover ramp regression.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: Online session owner, UI-007 and text layout owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- Superseded by the merged bounded implementation above (#470), which changed `Hud/ParkHud.cs` and `UI/Original/UiDialogs.cs` and added `AuthenticatedPauseLayoutTests`.
- The remaining UI-013 work (the menu font tier) is outside this proposal.
- UI-013 is not retired.
