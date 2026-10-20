# UI-091: Complete locator presentation behavior

## Status and classification
- Bounded implementations: visitor used-chain selection, blank staff state frame 9, and persisted independent staff-role locator order.
- Classification: `missing_behavior`; UI-091 remains open for the residuals below.
- Visitor/icon implementation baseline: `a6a2f5f3` (October 19, 2026); staff-role persistence is based on main `ed67fddf` after #484 merged.
- This continues draft #484's proposal. Its October 18 progress entry remains historical.
- One ID per proposal; UI-092 employee arrows keep their separate global-staff traversal.

## Visitor and state-icon slice

The visitor radio passes the guest simulation's actual ThingPool to locator selection, rather than obtaining
Level.Things (which can create a new pool in headless fixtures). Current guests are projected onto the used
chain. Each accepted slot must have model Guest, a current roster claim, no queued destroy or placeholder,
and a state other than Gone. Using and HeldByGuard are selectable despite their hidden sprites.

The current candidate advances to its immediate successor. A null current starts at the head; a tail or stale
current returns zero, so the existing LocatorModel press logic wraps from the actual head. Numeric ID
comparisons never substitute for chronology on the valid chain. Restored chain order and reused low slots work.

Pool-free/unsupported fixtures retain an explicit compatibility path: valid pooled visitors first, followed
by active pool-free guests in ascending ID order. IDs are positive and unique; the first roster claim wins,
including Gone claims, matching the HUD's existing first-match name/position lookup. Rejected used slots
cannot re-enter through the fallback. This is host robustness, not original ordering evidence.

Only the locator's staff state image changes to the existing nested FrameIcon. Its ID, model, frame provider,
original bounds, bottom-left anchor, group and visibility routing remain unchanged. Frames 0–3 draw their
selected mesh; state 9 draws nothing, including after a valid frame. StaffStateFrame still returns 9 and
UiModel.GetFrame still retains its generic root fallback.

## Persisted staff-role order slice

`ParkStaff` owns five independent sequences of roster member IDs. Fresh hires prepend to their role and
firing splices out the member without changing survivor order. The role sequences do not reorder `Members`
or the global ThingPool. Employee-window arrows keep their independent global-pool traversal (UI-092).

Original import follows each saved world role head and staff `RoleNextId` chain, mapping ThingIds to imported
member IDs. Valid independent chains keep their exact order even when it differs from the saved global pool
or roster order. Validation walks the complete role before installation: cycles (including interior cycles),
wrong-role links, missing records/members, bounds failures and orphaned role members produce precise
per-role import evidence. The entire malformed role falls back to its saved-global-order projection; no
valid prefix is installed and other valid roles retain their independent chains. Invalid nonzero heads still
fail in the existing Files layer; unsupported above-capacity rosters are not made valid by the fallback.

Own-format persistence adds optional `StaffData.RoleOrder`: exactly five non-null integer arrays in
`StaffType` order, each containing every matching roster member once. Validation rejects malformed shape,
invalid/unknown IDs, duplicates, wrong roles and omitted members before any role order is installed. Restore
and capture copy the arrays. The member count is bounded by ThingPool capacity, not the monotonically
allocated roster IDs. Capture always records the effective order, even for empty roles.

Missing or null data is the explicit older-save path. Its effective order is a read-only live-pool projection
with unsupported members appended in reverse roster order. Reads, locator presses and capture do not
materialize this projection. Hire/fire materializes the effective order before prepend/splice; repeated
`AttachThings` never overwrites explicit or materialized role ownership. Neither this fallback nor a malformed
original role's global projection is claimed to reconstruct a lost native role chain.

Staff radio selection resolves current roster identity in that role order, including carried members and
members without coordinates. The immediate successor is selected; tail/stale selection wraps to the role
head through the existing press logic. Role switching, close/reopen, empty/singleton roles and removal retain
the existing locator lifecycle. Missing coordinates preserve the staff identity/name strip/follow request,
but cannot invent a camera target: finite conversion and the park-camera guard still apply.

Role order is presentation-only. `WorldStateHash` removes only nested `StaffData.RoleOrder` from canonical
economy JSON; members, training budgets, `HaveEverTrained` and every other gameplay property stay hashed.
No schema/pin change is required. Selection/capture leave RNG, global pool, roster and gameplay data unchanged.
See [save validation and legacy behavior](../PARK-SAVES.md#staff-locator-order-and-compatibility) and
[the hash boundary](../DETERMINISM.md#staff-locator-role-order-october-19).

## Static evidence

The implementation uses a read-only static trace; no original executable was run for this slice.
No original media, asset extracts or disassembly are distributed.

- `0x4B3340` dispatches role mask 64 to `0x4824D0`, then retries from null if no next actor exists.
- Visitor branch `0x482757` reads the pool head through `0x516130` and older links through `0x516160`.
  It tests actor type 1, without sprite-visibility, visitor-state or coordinate predicates.
- A destroy request sets the type to 2 (`0x50B906`) before the slot is freed. Host queued slots must be skipped.
- Allocation `0x516270` prepends to the used chain; IDs are reusable slots, not a chronology.
- The original saves independent staff role heads at world offsets `0x1DA73C`, `0x1DA73E`, `0x1DA740`,
  `0x1DA742`, `0x1DA744`, with the next same-role ThingId at staff `+0x210`. Load preserves these directly;
  live hires prepend and removal splices survivors. The independent chains cannot be inferred from numeric IDs
  or the all-staff ThingPool traversal. Import validation/fallback is a host corruption policy.
- State mapping `0x485EC0` returns 9 for picked-up/other staff states. The selected staffstat chain has four
  drawable nodes (`staffstat`, `busy`, `resting`, `strike`); walking to node 9 exhausts it and hides the old node.
- OpenLocator/OpenVisitorLocator already post advisor help `0xC6`/`0x11F`; CloseLocator withdraws both.
  Their callees do not establish missing sound events. Source/register/UI documentation now says this consistently.

## Retained boundaries and next work

- Visitor follow requires a resolved finite world position. Selection itself does not require visibility or
  coordinates. No park or an unusable conversion leaves the camera unchanged, and the following update still
  clears visitor identity/strip/follow. This is a retained host fallback, not native missing-position equivalence.
- Staff identity-preserving no-position behavior stays intact, now including role-radio selection directly from
  the roster. Employee arrows remain global-pool based. The visitor/frame behavior and existing control and
  smoke-test hooks (including #460) are retained; this slice does not broaden their scope or close UI-092.
- Camera smoothing (`0x42B1C0`), patrol rectangles (`0x536100`) and panel sliding remain out of this slice.
- Real-data visual parity remains unverified. Synthetic CPU draw submissions cover the changed icon; they do
  not prove full native visual equivalence.

## Validation and publication boundary

Prepared C# tests use actual locator radio callbacks and the actual locator icon Draw path with synthetic data:

- Nonmonotonic mixed-model chain, permuted roster, exact restored snapshot, stale current and wrap.
- Walking/Using/HeldByGuard transitions with retained coordinates; removal before press or update; lower-slot
  reuse; role switch, close/reopen, repeated, empty and singleton selection.
- Missing roster claims, wrong models, queued/placeholder/Gone exclusions and deterministic fallback deduplication.
- Pool/guest/RNG/save immutability, missing park, invalid heightfield conversion and non-park camera guard.
- Valid state frames 0–3, Working → PickedUp → Idle/Working, repeated blank 9, strip/name preservation,
  visitor icon hiding, close/reopen and staff role switching; generic GetFrame(9) still returns the root.

The staff-role slice adds synthetic validation targets in `StaffRoleOrderTests` and
`LocatorStaffRoleOrderTests`:

- Distinct role/global/roster orders with nonmonotonic IDs, exact original chain import, independent roles,
  fresh-hire prepend, fire splice, reused pool slots and repeated pool attachment.
- Full-role import validation, including an interior cycle, wrong-role/missing/out-of-bounds link and orphan,
  precise per-role evidence, whole-role fallback and preservation of other valid roles.
- Native five-array validation, explicit empty roles, input/capture copy isolation, economy/world JSON roundtrip,
  absent/null legacy projection before materialization, later attachment, and hire/fire materialization.
- Actual locator radio callbacks, successor/wrap/stale selection, role switches, close/reopen, empty/singleton,
  carried/no-position roster membership, removal and safe missing-park/non-park-camera behavior.
- Unchanged roster/global chain/RNG/capture state on reads, order-only hash equality and gameplay-field hash
  sensitivity, explicitly including `HaveEverTrained`.

`tools/test_staff_role_order_contract.py` checks source integration only. It cannot establish execution,
visual parity, successful native resume or deterministic continuation by itself.

The local environment has no dotnet SDK. These C# tests and the full solution build must run on the exact
published head before merge; source-only Python guards are not a substitute. Run StaffRoleOrderTests,
LocatorStaffRoleOrderTests, LocatorPanelTests, LocatorVisitorOrderTests, LocatorStateIconTests,
EmployeeWindowControlsTests, StaffWindowTests, OriginalStaffImportTests, UiModelFitTests,
UiModelBindingTests, ThingPool/resume tests, AdvisorToolHelpTests and the required aggregate suite.
Run fidelity/progress validation and their Python tests as well. Report executed and unavailable stages separately.

## Proposal history and coordination

The initial October 18 proposal was plan-only against `5ccddcab`. Its source cross-check corrected misleading
sound wording: the `0xC6`/`0x11F` help posts were already present. The October 19 static investigation resolved
visitor ordering, frame-9 drawing and independent staff-role persistence requirements. The subsequent staff-role
slice implements the bounded ownership/import/resume contract above without claiming UI-091 complete. The
October 18 proposal and earlier October 19 progress fragments remain historical; this maintained proposal
describes the current boundaries. Coordinate remaining work with UI-092 and staff/patrol/camera owners.
