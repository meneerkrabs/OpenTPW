# ECON-030: lost-park menu and remaining bankruptcy presentation

## Status

Bounded menu implementation in progress, based on main `75c5de1b`. ECON-030 remains open (narrowed on October 26, 2026: the loss window, overlay and restore are ported; see docs/ECONOMY.md). This proposal does not claim completed loss-camera presentation, original runtime playback, or register retirement.

## Verified current behavior and correction

`ParkEconomy.HandleBankruptcyMessage` already records bankruptcy. The warning producers for advice 102–105 already exist in `AdvisorScan`, `EconomyMoneyState` and `AutomaticAdvisor`, with regression coverage. Their former description as absent was stale; this slice does not duplicate or replace them. Purchase/hire refusals remain current host policy rather than newly proven original behavior.

Static interoperability analysis establishes a separate lost-state pause-menu branch. The menu builder at `0x48C150` tests the visit/game-type branch before world state 4. Its lost-state rows are Load, Restart Park, Resume Game, Exit Lobby and Quit, in that order. The corresponding command values are 22–25 with Resume at 21. The old annotation conflated a text identifier with command 22: Load uses text ID 3. A previously claimed tick-button dismissal is not supported by the inspected loss-handler branches.

## Bounded implementation

Use live economy state when constructing the ordinary park pause menu. Preserve read-only-visit precedence. For a writable bankrupt park, select the five evidenced entries from the existing menu callbacks; retain the current confirmations, capabilities, stale-ownership guards and pause/speed restoration. Ordinary parks retain their existing menu, including the explicitly extended online entry. Resume, Back and canceled actions must not clear bankruptcy or alter money.

No new warning producer, camera, banner, scene transition, gameplay restriction, save field, hash behavior or network policy belongs in this slice.

## Source and evidence boundary

- `source/OpenTPW/Hud/ParkHud.cs`: actual pause-menu construction and callbacks.
- `source/OpenTPW/Economy/ParkEconomy.cs`: live bankruptcy state.
- `source/OpenTPW/Economy/EconomyApproximations.cs` and `docs/ECONOMY.md`: canonical residual and evidence qualifications.
- Original builder `0x48C150`, handler `0x48B6A0`, row dispatch `0x48B2A0`: branch ordering and existing action routes.
- The lost Load route passes a different argument to `0x49EFB0`, but the inspected callee does not read it. The extra lost Restart/Exit/Quit cleanup call is a return-only routine in the inspected build. These observations do not justify invented host behavior.

Only conclusions and source anchors are recorded here; original files, private input metadata and extracted resources are not published.

## Acceptance checks

1. Construct the real pause menu for ordinary, bankrupt and read-only-visit parks; assert exact lost row order and visit precedence.
2. Verify missing optional capabilities retain the existing enabled/action behavior and a missing economy does not masquerade as bankrupt.
3. Verify reopening observes current state, while duplicate opens preserve the owned menu guard.
4. Exercise Resume and Back with paused and running speeds; retain bankruptcy and cash.
5. Exercise Load cancellation, Restart confirmation/cancellation, Quit confirmation/cancellation and Exit Lobby with current ownership guards.
6. Preserve ordinary callbacks and the online extension; run existing economy/advisor warning regressions.
7. Run full code CI and independent source/test review before readiness or merge. Synthetic menu tests are not original rendered evidence.

## Remaining evidence-gated work

The original loss transition selects a gate-dependent camera, creates a dedicated fullscreen root and later installs a hidden-HUD overlay. Loaded bankrupt parks take a separate path without replaying the gate camera. The inspected dismissal paths include delayed right-release and unmodified Escape; this is insufficient to claim the complete default control procedure or all input restrictions.

The camera helper fallback order is statically traced, but gate model/script camera data, the root's drawing resource, layout/default dispatch, tool cancellation and complete lifetime behavior still require evidence and implementation. Existing ride-view behavior is not a substitute for this contract. Unsupported economic interaction restrictions must remain qualified. These remaining concerns keep ECON-030 open after the menu slice.

## Review and ownership

This slice owns pause-menu selection and directly related ECON-030 documentation/tests only. It reuses existing callbacks instead of changing shared level, network, guest, umbrella or postcard flows. Implementation results and exact-head CI are recorded in the PR and progress fragment when available; this document alone is not proof that those checks passed.
