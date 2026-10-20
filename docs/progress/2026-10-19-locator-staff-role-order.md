## Staff locator order survives import and resume (October 19, 2026)

`ParkStaff` now owns five independent role sequences. Fresh hires prepend to their role; firing splices out
only the removed member. Staff locator radios read current roster identities through these sequences,
including carried members and members without world coordinates. Employee arrows retain their global
ThingPool traversal, and the roster/global pool are not reordered to emulate a role chain.

Original imports preserve valid saved role heads and `RoleNextId` links exactly, mapping ThingIds to member
IDs. Validation covers each entire role before installation. Cycles, wrong-role/missing/out-of-bounds links
and orphaned members produce precise per-role evidence and a whole-role saved-global-order fallback;
no valid prefix is partially installed, and other valid roles keep their exact chains.

Own-format saves add optional `StaffData.RoleOrder`: five fully validated arrays, copied on restore and
capture. Absent/null older data keeps a read-only pool-then-reverse-roster compatibility projection until a
hire/fire materializes it. Repeated pool attachment cannot replace explicit/materialized order. Capture
always records the effective order without materializing a legacy owner or consuming randomness.
Only this presentation field is excluded from the canonical economy hash; all gameplay fields, including
`HaveEverTrained`, remain hashed. No schema or canonical pin change is required.

New synthetic C# validation targets `StaffRoleOrderTests` and `LocatorStaffRoleOrderTests` for import and
native validation, copy isolation, compatibility/materialization, actual locator radio lifecycle, safe camera
behavior, read/capture immutability and the narrow hash boundary. Local fidelity/progress checks, all
127 top-level Python tests and the evidence aggregate (1,537 cases, 595 skipped) pass; `git diff --check` is clean. The local environment has no dotnet SDK: C# tests
and the full solution build await exact-head CI. `tools/test_staff_role_order_contract.py` is a
source-integration guard, not execution or visual proof.

UI-091 stays open for camera smoothing, the retained visitor position-loss fallback, patrol rectangles,
panel sliding and real-data visual verification. Existing visitor used-chain selection, blank state-frame 9,
staff no-position identity behavior, UI-092 employee controls and #460 hooks remain intact. Historical
progress fragments are not rewritten. See the [maintained implementation and residual contract](../proposals/ui-091-complete-locator-presentation-behavior.md).
