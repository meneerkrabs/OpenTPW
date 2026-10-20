# Employee portraits: bounded live world output and nominal geometry

The employee window now prefers the selected member's latest successful world sprite output.
It reads that member's emitted slot, step and raw heading, resolving the sprite bank with a
literal zero camera-yaw contribution. A work, strike, research or performance frame therefore
uses the live member's phase rather than always playing the hire-window walking loop.

This is a bounded host correction under **UI-092**, not independent native-clone parity.
The hire window itself and its `StaffRenderer.PortraitFrame` path are unchanged.

## Publication and reads

`StaffRenderer.OnRender` calls `PrepareWorld`, the same CPU path that tracks movement, ticks
players, selects clips and prepares world quads. Its existing order remains view creation,
placement exclusion, tracking, player tick, clip selection and quad preparation. Only a
successfully prepared member quad publishes a value snapshot. `Describe` remains a mutating
world diagnostic and never publishes an employee portrait.

The snapshot has the exact member reference and ThingId, owning level, level-render pass,
renderer-render pass, asset generation, atlas index, raw slot/step and heading. It contains no
texture handle. The read-only `EmployeeFrame` requires:

- The same current level and staff renderer, still registered in the entity list
- Visible, material-ready, nondeleted rendering and the same placement/body exclusions
- The exact member still in the current roster with its matching ThingId
- A publication from the current world/renderer pass and asset generation
- The current matching atlas and a live native texture

The accessor only resolves bank direction/mirroring and constructs the UI frame from current
textures. It never ticks, selects or restarts a clip, consumes pending movement, creates a view,
or draws random numbers. Repeated reads are observational. Rendering still precedes HUD draw,
so the normal draw uses the same world pass, including when a modal covers the employee window.
There is no requirement for the lower employee screen to receive an update first.

The renderer pass advances before hidden/no-material/deleted early returns. The level pass
advances before entity traversal, so a renderer entirely omitted from that traversal cannot
revive an older output when re-registered. Level or renderer replacement and removal of a
member reference also reject earlier snapshots. The world path's existing CellX-only placement
gate is preserved; tracking still requires both coordinates, without inventing a portrait-only
position rule.

Every atlas replacement invalidates publications before replacing its texture slot. Until a
new successful world publication, no live employee frame is available from that generation.
Deletion marks the renderer unavailable and clears its texture list before queued disposal.
No member view or window retains an old GPU frame.

## Unavailable output and remaining qualifications

Before a first publication, while carried, resting, unpositioned, hidden or missing resources,
or after source loss, the employee callback uses its **pre-existing qualified hire-loop
fallback**. Its role/variant key and start time are still updated on every callback, even when
live output wins. Returning to fallback therefore retains the previous clock/key behavior;
it does not reset or freeze a live frame. A later valid publication can become visible again.
Selection, close/reopen and rename do not create a second player or restart the world's player.

This fallback is not the original source-loss lifecycle. The original portrait is an independently
registered sprite clone, with independent due time, interpreter state and control stack; its
periodic source refresh does not copy all those fields. A clone can remain scheduler-eligible
without its source, and script writes can overwrite its screen-space flags. Neither continuous
visible animation nor a frozen portrait after source loss follows from that fact alone.
Missing-source creation epochs, source return, full-refresh clone replacement, complete VM/flag
history, native depth/occlusion and rendered-pixel equivalence remain open under UI-092.
The existing render-driven staff animation timing qualifications also remain in force.

## Employee-only nominal geometry

The employee portrait now uses its own nominal 2.4 canvas units per reference sprite pixel.
The authoritative layout node `0x511` has no explicit inner override. The original type-1
control copies its outer endpoints into its inner rectangle on construction, so `Inner ?? Rect`
gives `(386,280,264,264)`. Frame artwork stays at those same outer bounds and Center anchor.
No hire or visitor inset is substituted.

Signed integer endpoint arithmetic precedes viewport mapping: feet X is
`left + ((right - left) >> 1) + 8`; feet Y is `bottom - ((bottom - top) >> 2) - 2`.
The employee's scale-8 clone helper supplies that final two-unit upward offset, giving authored
feet `(526,476)`. Its nominal pixel factor is `8 * 0.05 * (1536 / 2) / 128 = 2.4`.
The unchanged shared draw applies each frame's `128/A` and `128/B` to both hotspot offsets
and dimensions, including mirrored left edges and reversed U coordinates.

The hire window has its own inner bounds and feet `(626,362)` at 2.4 with no clone-helper
Y offset. The visitor keeps `(598,450)` at 3.6, including its three-unit upward offset.
Generic figures retain 3.2 and rectangle-relative feet. UI-082 owns hire geometry only;
UI-092 owns employee geometry and its remaining qualifications.

This correction applies equally to a live frame and the existing unavailable-source fallback.
It changes no accessor, animation clock, publication, lifetime or source-loss behavior. Static
layout/helper tracing supports nominal geometry, not exact x87/viewport/raster output, independent
clone VM timing, script flag history, native rotation/depth or GPU rendered-pixel parity.

## Verification

`EmployeeLivePortraitTests` exercises synthetic sprite banks through the actual CPU world
preparation and actual employee figure callback. It covers clip/phase, observational repeated
reads against a no-read control, camera rotation and limited-direction mirroring, same-role
selection and reused IDs, source eligibility, skipped render passes, level/renderer replacement,
asset generation/deletion, rename/modal/reopen and unchanged fallback timing. Managed resource
sentinels exercise lifetime checks without creating a graphics device; they do not establish GPU
submission, disposal or visual equivalence.

`python3 tools/test_employee_live_portrait_contract.py` guards production call order and the
read-only boundary. These source checks do not replace the C# tests. Run the focused C# class
and full CPU suite on the exact proposed head before merging. Real-data GPU/visual comparison
remains a separate verification requirement; this change includes no original assets.

`EmployeePortraitGeometryTests` adds actual employee callback/Draw checks with synthetic 5- and
8-direction banks, mirrored and unmirrored headings, unequal A/B densities, non-square art and
negative origins. Its independent arithmetic oracle checks all six positions/UVs, texture identity
and exactly one quad across authored, small, wide, square, odd-sized and minimum-scale viewports.
A nonzero non-walk live phase and the established source-loss fallback phase share the corrected
geometry. Repeated actual reads/draws are compared with a no-read world control, including save,
RNG, player, pending movement and next world vertices. Actual hire, employee and visitor widgets,
plus a generic default figure, have separate Draw controls so equal hire/employee scales cannot
hide a wrong hotspot. The additional source guard is `tools/test_employee_portrait_geometry_contract.py`.

## Independent VM core seam

The [bounded staff sprite VM](STAFF-SPRITE-VM.md) now has clean semantic programs,
complete control/register state and exact full/partial clone-copy operations with
synthetic CPU tests. It is not consumed by this window or the staff renderer yet.
UI-092 remains open; the existing callback and its qualifications above still apply.
