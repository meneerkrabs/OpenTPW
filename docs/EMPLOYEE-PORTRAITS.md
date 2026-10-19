# Employee portraits: bounded live world output

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
UI-082 is the hire window only; the employee window's size is UI-092: OpenTPW draws its figure at 3.2 where scale 8.0 through `0x486530` gives a nominal 2.4 (the visitor's 12.0 gives 3.6 through the same path). The existing render-driven staff animation timing qualifications
also remain in force.

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
