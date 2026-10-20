# Draft proposal: ADVISOR-017 map and options speech stop

## Status and boundary

Partial implementation, October 18, 2026. The map/options silent stop and actual-park-pause queue gate are now implemented with regression tests; C# execution and runtime playback validation remain pending. Other ADVISOR-017 residuals below remain planned. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

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

## Full-register coverage addendum (October 18, 2026)

This reviewed addendum extends the bounded initial plan to cover the remaining registered residuals. All acceptance work remains pending. Source snapshot independently verified by the proposal coordinator: `5ccddcab56ffb9bda4ded1821988a8492bee8943`.


## Status and classification
- Classification: approximation.
- Plan-only addendum: no runtime implementation, annotation retirement or merge performed.
- Existing PR: #444; residual addendum only.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
the advisor clock starts at 0 for a new park and carries the saved word on an original-save import (the second word of the clock block behind the MESS marker, writer 0x402E00 after the snapshot 0x402E60, reader 0x402E30, marker check 0x41555A, load 0x415140 -> 0x402E80 -> 0x403160); it stands still whenever 0x4092A0 pauses the timer: 0x46B600 (main window procedure, WM_ACTIVATEAPP deactivation), 0x47F020 (message box builder), 0x49F280 (save window), 0x49EFB0 (load window), 0x48C830 (the Escape menu, VK_ESCAPE at 0x4A2933 and 0x4BE537: OpenTPW's pause menu), 0x4A3A30 (the Game Options dialog, title 0x1D4D4, opened from that menu), 0x5F0B40 (the full-screen map window: control 0x29 of the HUD handler 0x4A0840 from Space, help message 0x130; the HUD's Paused covers it) and 0x4A9380 (the postcard, Ctrl+P, which UI-096 also covers; its last call sets state 3) are all identified, and the pause menu's windows and the map window pause the clock here, while the message boxes and the deactivation are the pauses UI-096 leaves open; the stop path 0x5996D0 (speech stop 0x51C0E0, Kill advisor 0x429D60, effect group 0 emptied) is also run by the map window (0x5F0B91) and the Game Options dialog (0x4A3A92), which OpenTPW does not do, so a speech still plays there; the scan (0x59C680, from 0x59A550) runs once per park turn exactly as in tp.exe (0x516380 increments the turn word and walks the things, the advisor's case 0x50B3F1-0x50B437 calls 0x59A550 unless the game type is 1, nothing runs while the world state +0x1DA738 is 4), at most 32 per frame and none while paused or lost, with no catch-up; the queue is served once per frame here but once per turn in tp.exe (the end of 0x59A550)

## Source inspection
- `source/OpenTPW/Client/AutomaticAdvisor.cs:15`: 16: private readonly PausableClock clock;; 18: private readonly Func<bool>? speechActive;; 20: private readonly Func<int> slapDraw;; 21: private readonly Action<uint> playSlap;
- `source/OpenTPW/Client/AutomaticAdvisor.cs:431`: 432: internal int ScanTurns( IAdvisorMoneyState? money, uint turn, int gameType, bool tutorialEnabled, bool halted, IAdvisorParkState? park = null, IRestoredAdviceState? restored = null ); 433: {; 434: var previousTurn = lastScanTurn;; 435: lastScanTurn = turn;

## Bounded work
Addendum to #444: queue service per park turn, focus/modal clock pause; exclude map/options speech-stop already covered.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode pause enable nesting and message-box/deactivation paths; reconcile older comment with identified map/postcard callers.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Run 0, 1 and 32 park turns per frame and compare dequeue sequence without catch-up.
2. Pause/resume nested modal and focus loss; preserve imported MESS clock and no service during lost state.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Review existing PR #444 current head; use residual addendum, not duplicate proposal.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## Ownership update (October 18, 2026)

Verified #433 (park information/lobby UI) and #423 (languages) are merged. The UI owner confirms no remaining HUD ownership blocker for this proposal. Runtime work can be scoped once the evidence/acceptance gates above are met; this update does not implement the feature. Coordinate any guest, snapshot/resume or shared simulation changes with the remaining umbrella U2–U4 work (Guest*, RideVisitorBridge, Level/Resume, ParkResumeMods/SaveFile, ObjectCatalog, OriginalObjectRuntime, official-extension gates/content roots and World/Umbrellas; U4 also adds a rain-mood option). Do not duplicate that work.

## Implementation checkpoint: map/options lifecycle slice

The existing window-open event now invokes the silent stop before mode-help withdrawal for map and options. Existing Kill clears active/delayed/trailing presentation and its effect group without a slap. Queue service now receives the actual park pause separately from the existing HUD advisor-clock pause, preserving queued advice in ordinary finance/research/loan windows. Controller reservation/history behavior is retained; this does not implement per-turn queue scheduling or deactivation/message-box lifecycle gaps.

Added AdvisorModalStopTests for active/inactive sound, ordinary and detached windows, real pending queue pause boundaries, ordinary-window queue consumption, preserved normal-response reservation/history, and stop-before-tool-withdrawal ordering with map help still posted. Independent source/test review passed. Local diff, fidelity/progress and 54 supporting Python checks passed; the evidence runner passed 1,537 tests with 595 skips and 7 expected failures. No C# build/test or runtime playback pass is claimed; exact-head CI is pending. Keep draft and ADVISOR-017 open.
