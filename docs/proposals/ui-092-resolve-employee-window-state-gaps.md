# UI-092: employee navigation, rename and locator state

## Bounded implementation and remaining portrait work

The employee-window investigation resolves three actionable gaps: ordered employee navigation, title-click rename and locator selection when coordinates are unavailable. The implementation and focused tests must pass independent source review and exact-head CI before merge. UI-092 remains registered for the separate live-portrait and rendering qualifications below. No original program was executed and no original media or private input metadata is included.

## Employee order

The original arrow handlers use the all-staff thing chain, accepting types 4..8 (`0x4B5CB0`, `0x48CAF0`, `0x48CBE0`, `0x481F80`). Forward/down follows older links and wraps from the used-list head; backward/up follows newer links and wraps from the tail (`0x483740`, `0x483770`). Numeric thing IDs and roster insertion order do not establish this chronology.

Use existing ThingPool order joined to current employee identity. Do not reorder the economy or save pool to navigate. With no pool, retain reverse-roster order. For mixed or malformed host membership, valid pooled members come first; unsupported members follow in reverse-roster order, while queued or placeholder claims remain excluded. Tests must cover imported/restored nonmonotonic order, interleaved nonstaff, queued removals, reused slots, wrap and singleton behavior. Locator role-chain ordering remains a separate UI-091 concern.

## Title rename

The title stores its employee-window owner in property 0 (`0x48D290`); its click handler retrieves that owner and invokes vtable slot +0x38 (`0x48D2E0`). The employee vtable resolves this to `0x4B6110`, which opens rename table `0x7572E8`. The target employee and owner are captured in child properties, and repeated opening is blocked while the child exists.

The editor is initialized from the selected employee's current name, uses a 32 UTF-16-unit limit and title UITEXT 195. Apply calls the bounded name setter `0x506830`; cancel closes without a write (`0x4B6320`). There is no native nonempty-name requirement. Keep null as the host generated-name fallback and distinguish an explicit blank name. Preserve identity, variant, name index and unrelated simulation fields.

Host protections must reject read-only changes on both opening and applying, reject stale targets, cancel when the owning window closes or the employee leaves, and avoid duplicate modals. Default labels and text fields retain their existing behavior; any activation/initial-selection support is opt-in. Test original layout, keyboard and pointer actions, Unicode boundaries, blank-name persistence, stale callbacks and unchanged gameplay state on cancel/navigation.

## Locator identity

The original employee locate action opens the locator for its selected employee without requiring a position lookup (`0x4B5F07`, `0x4B3F60`). Ordinary held staff in OpenTPW already retain coordinates, so the earlier blanket claim that held employees cannot be located was inaccurate.

Distinguish a current employee's existence from availability of world coordinates. Selecting an unpositioned employee should retain the actor and strip while camera following safely waits for a valid position. Actual removal clears the selection. Keep the host camera-mode safety boundary and existing visitor behavior. Test repeated opening, unavailable-to-available coordinates, actual removal and stale previous camera targets.

## Corrected portrait evidence and residual

The employee portrait control's +0xE0 is UI depth, converted by `0x476A70`; it is not an employee animation phase. The genuine mismatch is that the original clones the selected employee's live sprite and refreshes its animation/direction registers (`0x486530`, `0x475CF0`, `0x486610`, `0x475E10`). Screen-space clone flags suppress camera-yaw contribution (`0x5421CC` onward). A subsequent [bounded live-output correction](../EMPLOYEE-PORTRAITS.md) reads the selected member's current world output at yaw zero, retaining the earlier hire-loop fallback when unavailable.

Live-portrait changes were deferred from the navigation/rename/locator slice. The subsequent live-output accessor does not invoke mutating world animation selection from UI draw: repeated portrait reads cannot tick, restart, consume movement or alter the simulation. Static follow-on tracing establishes same-frame world-before-HUD ordering and an independently scheduled native clone whose refresh omits its clock/state/stack. Script writes can change its screen-space flags after source loss. The subsequent slice validates current host source availability and texture generations; cloned-VM behavior, native source-loss/refresh lifecycle and full depth/occlusion still need separate implementation and review. No invented fallback loop or complete UI-092 retirement is justified by the navigation/rename/locator work.

## Employee-only geometry correction

The [employee portrait geometry](../EMPLOYEE-PORTRAITS.md#employee-only-nominal-geometry) uses
the layout control's inherited inner rectangle `(386,280,264,264)`, signed integer authored feet
`(526,476)` and nominal scale 2.4. The frame artwork keeps its original outer rectangle. The
employee clone helper's scale-8 Y offset is -2; hire's separate creator has no such offset and
visitor scale 12 uses -3. No shared renderer, live source, fallback clock or lifetime code changes.

Synthetic CPU tests exercise the real employee callback and Draw, nonzero live/non-walk and
fallback phases, densities/mirroring/viewports, observational reads and independent actual
hire/visitor/default Draw controls. These tests do not establish original raster or clone parity.
UI-092 remains open for the VM/lifecycle/depth/rendering qualifications above.

## Verification boundary

Record executed tests and exact published commit in the PR and progress fragment. Static code/table reconstruction and synthetic tests do not demonstrate original rendered-pixel equivalence. Original assets remain private; UI-082 owns hire dimensions only. Employee viewport/raster parity and generic UI model/depth behavior retain their qualifications.
