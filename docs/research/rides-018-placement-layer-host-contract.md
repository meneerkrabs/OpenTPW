# RIDES-018 placement-layer host contract

Source baseline: meneerkrabs/OpenTPW main dcdf4d0c53723e7b7df9c7c7a5ae3834c4f7130b, inspected October 22, 2026. Existing proposal: #578, head 81b86b72c3efb9b5609f5e5d809f5107dceb109e.

This is public-source research plus synthetic regression coverage, not original-runtime evidence. RIDES-018 remains approximate. No production predicate, guest relocation rule, or fidelity annotation is changed.

## Current contract and exact gap

1. ParkObjects.Check starts with ObjectPlacementPlan.None. It accumulates overwrite/path candidates internally, validates footprint, owned land, access cells, then the optional host placement rule. Only Allowed publishes a plan. TryPlace returns before construction, allocation, demolition or path clearing on any refusal.
2. The ordinary footprint path is lower-layer terrain, live NoModify path, reserved primary type, and owner overwrite priority. The Level hook reserves primary types other than empty, path and object types; it also reserves host pool/prototype structures. It does not query Tracks for a generic footprint cell.
3. Ownership is a separate pass over all footprint cells, including accepted add-on '+'/'#' cells. Add-ons first require in-bounds terrain, the selected ride's AddOnCellRule, and absence of a real object. Do not substitute a blanket secondary-track ban for that exception.
4. Queued entrance outside cells use CheckObjectCell. Exits and non-queued entrances use CheckPathRule. A non-queued exit is skipped. An off-grid outside cell returns OutsideTerrain; an in-grid failed access rule returns AccessBlocked. Access-cell paths do not enter ClearedPaths. An overwrite-priority owner result takes precedence over PathRuleAllows.
5. Track-end shape cells ('<'/'>'/'D') are a distinct route: resolve the rotated direction, refuse absent/off-grid beyond cells, invoke TrackEndCellRule, then CheckObjectCell on that beyond cell. Level wires that callback to CoasterCellMarkers.AllowsOrdinaryGround. The callback refuses unowned land, pool bridges and secondary walkable track types 11/13/16/18/25, following one extension of type 12 or 17 through its valid parent index. This is not the missing generic 0x53ae00/0x53ae90 link test.
6. The generic footprint and queued-entrance routes have no equivalent secondary-link predicate. A deliberately synthetic primary-empty/secondary-track record is therefore insufficient to establish the native decision. Real host track writers normally mirror occupied primary types; conflating the mirrored reservation with complete independent-layer validation hides the gap.
7. Guests stepping off overwritten footprints already have host regression coverage in GuestsOnAClearedPathCellStepOff. It demonstrates nearest walkable-cell relocation, not parity with a native guest inside a newly constructed footprint.

## New asset-free coverage

ObjectBuildRuleTests adds two data-driven tests (eight test rows):
- Four rotations: a ride footprint has a real synthetic bush and path pending removal, then the exit path rule refuses. Check exposes no partial plan; TryPlace changes no primary/secondary version, object version or seed frontier, fires no placement/removal/clear callback, and preserves the bush/path. Restoring the access rule is a positive control proving both candidate operations were actually pending.
- Four rotations, each with five secondary origin types and direct/type-12/type-17 extension configurations: a station endpoint's beyond-cell path stays unchanged on secondary refusal. Empty secondary state is the allowed control. This gives 60 refusing configurations through the ParkObjects callback seam, not just isolated CoasterCellMarkers calls.

The tests reuse the existing synthetic dummy MD2 and END-only RSE generator. They require no original files. They do not run a full Level, instantiate a coaster controller, verify rendered output or change layer writers. Existing CoasterCellMarkerTests separately exercise the helper; these cases exercise propagation into placement result/plan/failed commit. Existing lower-layer/access/guest tests are retained unchanged.

## Missing native comparison, with bounded inputs

Before changing generic validation, record, for each case: build/tool mode and first/last/sticky context; primary type/flags/owner; secondary type, resolved parent, link mask and flags; object overwrite priority; shape cell kind and rotation; option 0x81b0cc; type-record +0x288 and WhichTrackType result; return/refusal and all mutated cell/guest fields.

The discriminating matrix must vary:
- empty, straight, corner and junction secondary link masks, with identical lower-layer cells;
- direct secondary record versus both extension types and valid parent resolution;
- ordinary footprint, queued entrance outside cell, exit/non-queued entrance, valid selected-ride add-on, and station beyond-endpoint;
- owned/unowned and NoModify independently, equal/lower/higher owner priority;
- +0x288 = 1 and other type records with observed WhichTrackType branch, rather than Info.HasQueue guessed as a replacement;
- a stationary guest, a guest crossing the footprint boundary, no surviving nearby path, and equal-distance destinations.

Public annotations identify 0x535BD7 -> 0x53ae00/0x53ae90 and the 0x46d605 option branch but do not provide their decoded predicates. No byte-level or original-executable work was performed in this task. Native corner/junction result expectations must remain unset until that comparison exists. The endpoint helper is not a substitute oracle.

## Ownership boundaries

Open PR search on October 22 found #513 (ECON-050 placement cell mutations), #530 (queue reevaluation), #574 (RIDES-011 endpoint markers), and #594 (under-track path retention). Their mutation/lifetime work is not changed here. Current main already implements the endpoint helper and markers; the old #574 proposal title must not be read as absence of all implementation. #576 land fill is separately owned and untouched.

## Verification and remaining gate

Only source inspection and local text/diff checks were performed. C# compilation and execution are NOT RUN: local .NET execution was excluded by the task's prior access denial. No browser, GPU, original-game run, commit, push, publication or merge is claimed. Obtain permitted CI execution and independent source review before publishing this coverage as tested. Keep #578 draft until its original native-evidence and acceptance gates are fulfilled.
