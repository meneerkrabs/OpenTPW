# UI-051: Route ticket purchases through ledger

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
HUD ticket count and spending use park state instead of global player earned-minus-spent ledger; key count is already matched.

## Source sites
- `source/OpenTPW/Hud/ParkHud.cs:288` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Hud/ParkHud.cs:289`: `economyElements.Add( hud.Add( new UiModelImage { Id = "dollar", Model = "i_dollar", Frame = () => Status.Money < 0 ? 1 : 0, Bounds = Rect( 37, 58, 242, 262 ), Anchor = UiAnchor.TopLeft } ) );`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Existing player ledger API and purchase transaction boundaries; original global/local/secret ticket accounting trace.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Plan one consistent ledger transaction route and HUD read model, coordinating persistence rather than patching display alone.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Global ticket moved parks counts once; local tickets across parks; secret flags; purchase refusal; spend once; save/load; keys unchanged.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: Economy/player ledger and SAVE-920..923 owners.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.

## Current-source reconciliation (2026-10-19)
At public main `fd8ff52eec12354fba02f23d23c5a217b2515a3b`, a global `PlayerTicketLedger` already exists, but its spent-ticket value is currently restored rather than used as the live purchase debit. The actual key callback is wired by `GameFlow` through `EconomyParkStatus.ForLevel(level, PlayerKeys)`; key display is not an unproven hypothetical caller.

The purchase route is also present: `Level.Objects` calls `Park.Economy.TryBuild`; `CheckBuild` refuses a cost above park `GoldenTicketsAvailable`, and successful `TryBuild` increases park `TicketsSpent` and records the ticket-bought item. Thus park-local spending remains a substantive residual. The HUD ticket count also remains park-local. Do not add an early-placement refusal test as though commit-time refusal were absent.

The next bounded design must define global ledger transaction ownership, cross-park ticket-bought items, failed-placement/rollback behavior and save continuation, using the existing ledger and economy ticket tests. Coordinate with the current command/build owner (#696) before implementation. No live transaction route is changed by this documentation reconciliation; original global/local/secret-ticket equivalence remains subject to the evidence gate above. No new native verification or executed-test claim.
