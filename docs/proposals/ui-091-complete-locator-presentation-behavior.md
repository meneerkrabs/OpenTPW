# UI-091: Complete locator presentation behavior

## Status and classification
- Bounded implementation: visitor used-chain selection and blank staff state frame 9.
- Classification: `missing_behavior`; UI-091 remains open for the residuals below.
- Implementation baseline: `a6a2f5f3` (October 19, 2026).
- This continues draft #484's proposal. Its October 18 progress entry remains historical.
- One ID per proposal; UI-092 employee arrows keep their separate global-staff traversal.

## Implemented slice

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

## Static evidence

The implementation uses a read-only static trace; no original executable was run for this slice.
No original media, asset extracts or disassembly are distributed.

- `0x4B3340` dispatches role mask 64 to `0x4824D0`, then retries from null if no next actor exists.
- Visitor branch `0x482757` reads the pool head through `0x516130` and older links through `0x516160`.
  It tests actor type 1, without sprite-visibility, visitor-state or coordinate predicates.
- A destroy request sets the type to 2 (`0x50B906`) before the slot is freed. Host queued slots must be skipped.
- Allocation `0x516270` prepends to the used chain; IDs are reusable slots, not a chronology.
- State mapping `0x485EC0` returns 9 for picked-up/other staff states. The selected staffstat chain has four
  drawable nodes (`staffstat`, `busy`, `resting`, `strike`); walking to node 9 exhausts it and hides the old node.
- OpenLocator/OpenVisitorLocator already post advisor help `0xC6`/`0x11F`; CloseLocator withdraws both.
  Their callees do not establish missing sound events. Source/register/UI documentation now says this consistently.

## Retained boundaries and next work

- Visitor follow requires a resolved finite world position. Selection itself does not require visibility or
  coordinates. No park or an unusable conversion leaves the camera unchanged, and the following update still
  clears visitor identity/strip/follow. This is a retained host fallback, not native missing-position equivalence.
- Staff identity-preserving no-position behavior is unchanged. Staff radio order still depends on finite world
  snapshots sorted by member ID. Fixing it requires a persisted, validated role-order owner, not reuse of the
  employee window's global ThingPool projection.
- The original saves independent role heads at world offsets `0x1DA73C`, `0x1DA73E`, `0x1DA740`, `0x1DA742`,
  `0x1DA744`, and each staff member's next link at `+0x210`. Load preserves these directly; live hires prepend
  and removal splices survivors. File decoding retains this information, but live economy import/resume loses it.
  A later change must restore valid independent chains, validate cycles/wrong roles/missing IDs, persist them in
  native resume, and explicitly qualify reconstruction for older saves without role order. Do not reorder the
  roster or the global pool to emulate them.
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

The local environment has no dotnet SDK. These C# tests and the full solution build must run on the exact
published head before merge; source-only Python guards are not a substitute. Run LocatorPanelTests,
LocatorVisitorOrderTests, LocatorStateIconTests, EmployeeWindowControlsTests, StaffWindowTests, UiModelFitTests,
UiModelBindingTests, ThingPool/resume tests, AdvisorToolHelpTests and the required aggregate suite.
Run fidelity/progress validation and their Python tests as well. Report executed and unavailable stages separately.

## Proposal history and coordination

The initial October 18 proposal was plan-only against `5ccddcab`. Its source cross-check corrected misleading
sound wording: the `0xC6`/`0x11F` help posts were already present. The October 19 static investigation resolved
visitor ordering, frame-9 drawing and independent staff-role persistence requirements, enabling this bounded
slice without claiming UI-091 complete. Coordinate remaining work with UI-092 and staff/patrol/camera owners.
