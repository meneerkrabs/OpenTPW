# Staff sprite owner host GPU smoke

This is the bounded host-application check for staff source/employee-clone
ownership. It extends the existing `--front-end --smoke-test` plan and offers a
bounded `--staff-owner-smoke` selection within that harness. Both routes reuse
the real application clock, renderer, menu/start and update loop. See the
[owner architecture](STAFF-SPRITE-OWNERS.md), [acceptance map](STAFF-SPRITE-OWNER-ACCEPTANCE.md)
and [running guide](RUNNING.md).

## Evidence status and scope

The smoke and its structural guard are authored. No SDK build, C# execution,
GPU run, capture inspection, or performance result for this revision is claimed
by this document.
Those results must be recorded for the exact tested commit and artifact. A
passing Python guard establishes only source structure; it does not establish
that C# compiles, assertions run, or pixels were rendered.

The harness exercises the actual OpenTPW front-end/park application, original
assets supplied lawfully by the operator, the staff renderer, the employee
window, and composed GPU readback. A successful run is host evidence, not a
comparison with the original executable. The word "native" in the existing
capture prefix describes the OpenTPW executable smoke. It does not establish
native Theme Park World raster, control-depth, scheduling, or modal parity.

The fixture deliberately submits labelled source work-program requests. These
exercise real source request consumption, scheduling, resource resolution and
drawing. They are not proof that natural repair, research, performance, guard,
or handyman gameplay triggers occurred. Actual role-event CPU coverage remains
in the acceptance map; it is a separate evidence route.

## Running the exact artifact

Use the pinned SDK in `global.json`, the matching platform dependencies described
in `RUNNING.md`, a graphics device or supported software GPU, and legally obtained
original game data. Do not download or commit proprietary game files. Use a clean
output directory or preserve the previous run separately so stale captures
cannot be mistaken for current evidence.

From the repository root, run the bounded staff-only route with:

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --front-end --smoke-test --staff-owner-smoke
```

`--staff-owner-smoke` requires `--front-end --smoke-test` and Theme Park World
data. It enters the actual lobby, uses the shared Full Simulation start route,
loads one fresh jungle park, runs all five roles and lifecycle checks, then marks
the bounded plan complete and exits cleanly. Existing CD autorun is still
supported and passes the selection through to that same front-end harness.
Prerequisite captures include `staff-owner-game-mode.png` and
`staff-owner-park.png`. There is no second simulation driver or direct Level
construction for this mode.

The full integration route remains:

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --front-end --smoke-test
```

That default runs the existing full plan, including this staff slice and later
park rebuilds. Successful bounded execution proves only the selected staff lane
and its startup prerequisites; it does not pass the full front-end smoke or its
unrelated later stages. A full-plan timeout or failure elsewhere cannot be
relabelled as a full pass merely because the bounded route succeeds.

For an already built artifact, run that artifact directly with the same flags;
do not use `scripts/run.sh`, which rebuilds first. Record the exact command line
(including bounded versus full mode), commit, artifact identity, OS, process
architecture, graphics backend/device, SDK/runtime
version, language, drawable size, render scale, and any texture-pack or extension
overrides. An unrecorded rebuild is not the artifact that CI or an earlier review
qualified. Keep the complete process exit status and log with the captures.

The existing smoke runs offscreen and mutes audio. On Linux, the display and
Vulkan dependencies still apply; the running guide documents the supported
`xvfb-run`/software-Vulkan route. No private operator path belongs in a committed
report. The existing harness writes composed-output PNGs beneath `artifacts/`
with the `native-smoke-<language>-` prefix. This slice also writes
`native-smoke-<language>-staff-owners.jsonl` there. Each JSONL line contains
`Category`, `Stage`, `OuterFrame`, `Details`, and the complete `Observation`
fingerprint; the file is reset at the start of this slice. Categories include
`scope`, `lifecycle`, `request-fixture`, `observation`, `normal-scheduler`,
`gpu-pair`, `resource-fault-fixture`, `resource-reload`, `resource-cleanup`,
`rename`, `rename-pause`, and `microbenchmark`. A `scope/complete`
entry completes only the staff slice. In bounded mode, require the final bounded
success log, successful `VerifyCompleted`, and normal process exit. In full mode,
the subsequent front-end plan must also finish successfully.

A run is incomplete if setup, a required role, a required animation/resource,
placement, bounded scheduling, a readback, or an assertion fails. Required cases
must not be silently skipped. A screenshot by itself is not a passing run, and a
partial run must not be described as the full front-end smoke passing.

## Bounded fixture and role coverage

The plan uses the existing queued `Do`, `Wait`, and `CaptureFrame` stages. Actual
placement creates sources; the active level's normal scheduler provides VM
opportunities. The fixture does not advance a source or clone by editing its
raw flags, PCs, deadline, output-valid word, or clock. Each `WaitStaffOwner`
predicate has a 180-normal-outer-frame limit. The staff slice additionally has its
own 1,500-outer-frame budget, counted by the existing `Update`; those frames are
excluded from the unchanged 3,000-frame baseline front-end budget. This adds a
separate bounded allowance without extending the original plan's own limit.

The program/pixel plan hires and checks one fixture role at a time, then fires
only that fixture. Rename additionally hires a temporary second employee to
check captured-target behavior, then removes that comparison fixture. Other
pre-existing staff must be outside the world region. The underlying buy window
is closed during this lane and restored after successful completion if it was
previously open. Every role is required:

| Role | Required evidence |
|---|---|
| Handyman | `stand`, `walk-request`, `sweep-request`, and `stand-before-pickup`; mandatory Stand resource-unavailable/reload lane, rename/pause and Stand source loss. |
| Mechanic | `stand`, `walk-request`, and `repair-request`. |
| Researcher | `stand`, `walk-request`, and `research-request`. |
| Entertainer | `stand`, `walk-request`, and `performance-request`; source-absent retained Screen behavior separately. |
| Guard | `stand`, `walk-request`, `catch-request`, `catch-to-hold` through ordinary scheduling, and `release-request`. |

Each listed program has world and portrait visible/hidden/restored triples.
Their PNG suffixes are `<role>-<program>-<world|portrait>-<visible|hidden|restored>.png`,
with lowercase role names. A `request-fixture` line records the program and
whether that stage submitted a request. `catch-to-hold` observes the existing
program transition without issuing a Hold request. The fixture explicitly keeps
its newly hired member stationary with Idle setup; it does not claim those
work-request shots depict naturally entered jobs. After the source publishes the
requested output, the plan closes and reopens the real employee root so its
independent clone receives the full work-program state. The source publication
wait pauses simulation; the reopened root then stabilizes before pixel pairs. Stale pre-request output is excluded by checking
request consumption, the current PC and resource eligibility; the entertainer
must already be inside its performance loop.

A valid bank and a resolvable, live GPU resource are required for each selected
case. Reporting an unavailable resource or an invalid output as a named failure
is useful; replacing it with an unrelated animation, role, or successful skip
would invalidate coverage. Program labels must identify the requested program
rather than imply the fixture naturally completed a gameplay job.

## Pixel-pair isolation and invariants

Each visible/hidden pair is an observation of one stabilized state. Before taking
the baseline, allow the ordinary presentation sequence to settle, including
partial-copy-before-heading publication. Require three consecutive unchanged
normal paused outer updates, each on a different outer frame. Then compare
subsequent presentation passes with that stabilized baseline. This is the
P-to-H idempotence check: a change caused by an unsettled publication phase must not be mislabeled
as renderer mutation or hidden by comparing only a frame/slot.

Retain and compare all 70 raw VM words, including output validity, for the source
and independent clone, together with their live identity/tickets and clone
epoch. Also preserve the gameplay hash, random-stream state, camera state and
viewport/drawable configuration, including the actual world framebuffer width
and height (`WorldViewport`) as well as screen/canvas dimensions. Repeated
observations and the hidden frame must
leave these equal to the visible baseline. Camera and viewport equality are
necessary even when the employee portrait uses literal camera yaw zero.

For world evidence, choose an on-screen region containing the selected employee
and ensure that other staff cannot contribute pixels to that region. Placement
searches valid cells across the visible viewport at the ordinary maximum camera height (130),
set through the existing camera API before baseline stabilization. For each
selected role bank, it projects the union of every frame's extents and both
mirrors at the actual interpolated terrain point, using the same billboard axes,
pull-to-eye share, finite-coordinate and positive-W checks as the final world
ROI. There is no central-band restriction: a fully isolated side gap beside the
employee root remains eligible. The envelope must clear mapped authored employee-window rectangles and
every visible element in every active HUD stack screen, with the two-pixel
separation rule. The idle request-fixture setup is installed immediately after
real placement, before its first navigation turn. After normal publication, the
exact current quad must still clear all other staff quads and visible stack
elements. A failure logs the actual target and every overlapping staff/UI
rectangle (screen, element ID and type), and retains `staff-isolation-failure.png`.

The previous head `dff71ffc` passed full CPU CI on all three platforms. Its first
bounded Vulkan run failed the initial handyman Stand world-ROI/UI-overlap guard,
before contribution captures: the actual quad was 145.58 by 155.07 pixels,
exceeding the previous 80-by-108 estimate and overlapping the employee root by
18.64 pixels. The
terrain there was flat; interpolation was not the cause of that incident. The
shared geometry precheck replaces that underestimated envelope. This revision
also adds D06; neither its corrected fixture nor its new resource lane has an
executed result yet. Exact-artifact execution remains required.

Underlying windows and distant staff are not assumed harmless without those
checks. Require
the selected source to resolve and contribute world geometry. Compare the
selected region when world staff visibility is toggled. A change elsewhere in
the park or another staff member's pixels does not establish that this employee
was drawn. Keep the portrait region separate from the world region.

For portrait evidence, toggle only the selected portrait's drawing visibility.
Do not remove/rebuild the window, change layout, replace text, or hide unrelated
controls between the pair. Require a drawable Screen clone and a pixel delta in
the portrait region. For deliberately unsupported source-absent World routing,
require no portrait contribution rather than accepting a fallback image.

After restoring visibility, take a third capture and require zero changed RGB
pixels in the same region relative to the original visible capture. This
visible/hidden/restored control rejects unrelated changing background pixels
inside that region. A visible/hidden delta alone would not do so.

Visibility toggles are restored on both success and failure. The slice restores
its temporary speed, camera pose and scheduler subscription; successful role
cleanup closes the employee root, returns any carried member and fires only the
fixture. `Dispose` also invokes restoration if a step fails. The pair comparison
is not a whole-frame equality claim: unrelated scenery, effects, or UI may repaint
outside the isolated regions.

## D06: bounded live GPU resource lifetime

Both full and staff-only modes require one resource lane immediately after the
handyman's initial Stand world/portrait controls. It uses the current level's
staff renderer, current texture pack, supported frozen bank, live source and
Screen clone. The selected normalized `PackKey` must be unique among all loaded
staff atlases; an ambiguous key fails rather than reloading multiple atlases.
The hire window, carry tool and any texture-pack switch must be inactive.

After normal paused P/H stabilization, retain one complete `StaffObservation`
for the entire sequence, including all source/clone words, tickets and epochs,
pending fields, heading, clock/scheduler counts, gameplay/RNG, matrices,
world/output viewports and employee layout/text. Require equality across normal
frames and immediately around resource changes; do not replace the baseline
after a fault or reload. Output validity remains nonzero throughout.

The unavailable interval is an explicit resource-fault fixture. After waiting
for GPU idle, the selected atlas wrapper temporarily references separately
created, genuinely disposed test-native texture/view handles. Its real original
native allocation remains alive in local custody. The disposed handles are
never bound or submitted. A live transparent scratch texture quarantines every
material slot referring to that atlas, including padded aliases: each of the
eight slots maps to atlas `min(slot, atlasCount - 1)`. This fixture does not claim
that destruction of the original atlas caused the unavailable interval.

Require actual source and clone resolution to report `TextureUnavailable` and
the actual employee figure callback to return no frame. Keep the previously
validated, isolated world and portrait ROIs; an unavailable frame cannot supply
new drawable geometry. Each negative visible/hidden/restored triple must have
zero toggle-induced RGB changes, while its unavailable visible image must
differ from the original positive visible image in the same ROI. Restore each
visibility toggle even on failure. A disabled visibility flag is not evidence of
resource unavailability.

Recovery restores the original live handles and immediately invokes the narrow
selected-atlas reload, with no draw or observer between those operations. This
uses the existing matching-current-atlas loader and frozen-layout checks,
rebuilds all eight material bindings and schedules the actual original native
allocation for retirement. It neither switches the global texture pack nor
writes original or replacement game-data files. After normal retirement frames
and GPU idle, require:

- A different, live, owned current wrapper/native allocation; the old wrapper
  removed from `Asset.All`, its original real texture disposed, and its view
  released under the existing Metal full-view alias qualification.
- Retirement of the old material resource sets, unchanged staff
  material/shader/pipeline, and unchanged, live identities for other staff atlases
  and guest/head renderer resources.
- The actual employee frame to reference the new current native texture, never
  the old or fault handle; drawable source and clone; positive recovered world
  and portrait controls whose visible ROIs exactly equal the original stable
  visible ROIs.
- Every material slot restored from the current owned atlas list before scratch
  release, no bound scratch alias or quarantine resource set, and completion of
  submitted use before the scratch is released exactly once. Normal retirement
  must remove its owned wrapper and dispose its native texture/view as well.

Exception-safe restoration is armed before the first change. Before reload it
can restore the original live allocation; after successful reload it uses the
new current owned atlas list and never resurrects the retired wrapper. A failed
recovery fails the smoke. The lane does not mutate VM words, submit work-program
requests, schedule owners, rebuild the catalog or reconstruct the level.

Resource evidence must distinguish the injected unavailable fixture,
current-handle recovery and actual old/scratch resource retirement from
work-request and gameplay-lifecycle evidence. Require the category/stage entries
`resource-fault-fixture/d06-unavailable`, `resource-reload/d06-recovered`, and
`resource-cleanup/d06-scratch-retired`. Resource GPU-pair names are
`d06-<available|unavailable|recovered>-<world|portrait>`; their PNGs use the same
name with `-<visible|hidden|restored>.png`. Queuing deletion is not proof of native
disposal. This route does not simulate hardware device loss or validate
hot-filling a `MetadataUnavailable` bank. D06 live-GPU execution remains pending
until the exact artifact exits successfully with all required evidence and the
original, unavailable and recovered world/portrait captures are inspected.

## Lifecycle, rename, and pause

The lifecycle cases use actual pickup and return/placement routes.
`StaffPlacement.PickUp` intentionally keeps the employee root open to inspect
source loss. A separate `ParkHud.PickUpEmployee` case verifies that the real HUD
pickup handler closes that root and releases its clone before returning and
reopening. With the root intentionally open, controller pickup must remove the
source while retaining the independent clone's identity and epoch.
Return must create a fresh source and restore eligible Screen output through
the real partial-copy route into that retained clone. Both the returned world
source and portrait receive visible/hidden/restored pixel controls.

The Stand pickup case must continue through the normal scheduler until the next
actual `SET 0x1200` instruction clears Screen. Checking immediately after pickup
or forcing the route bit is insufficient. Its expected resource diagnostic is
`WorldRouteNotImplemented`, with no HUD portrait quad. The undrawable case checks
the null employee frame and runs a negative visible/hidden/restored portrait
control. Its PNG suffixes are
`stand-screen-to-world-undrawn-<visible|hidden|restored>.png`. With the portrait
figure's control bounds as the region, hiding the undrawable figure must change
exactly zero RGB pixels. This is explicitly marked `ExpectedContribution: false`
in its `gpu-pair` evidence; ordinary role and returned-source pairs continue to
require a positive delta. The entertainer case is separate: source loss during a performance loop can retain Screen until a real
flag-writing instruction. Source absence alone is not evidence that every clone
becomes World-routed. The scheduled-loss gate counts successful Yielded results
for this selected employee clone, rather than using an unrelated source's
scheduler event or a NotDue check as proof of clone execution.

Exercise the employee rename dialog's captured callback and successful
confirmation, recording the full-refresh/epoch effect rather than changing the
name directly on the data model. Capture the rename state. Before opening the
rename dialog, invoke the actual HUD `speedNormal` and `speedPaused` button
handlers and assert that the park is paused. Pause must prevent
new scheduling opportunities while allowing the ordinary presentation/read
routes to run; verify complete owner state, gameplay hash and random-stream
state across paused observations. An unchanged visible frame alone cannot prove
that pause retained owner history. The modal capture is
`staff-paused-rename.png`; six further paused outer frames must retain the same
complete observation before changed-name confirmation. The sequence also checks
cancel, unchanged-name confirmation, and a stale callback repeated after
confirmation. Before changed-name confirmation, selection moves to the second
employee while the captured rename dialog stays open. The callback must rename
only its original target, leave the second employee's name unchanged, retain the
second selection and refresh its current clone exactly once. A repeated stale
confirmation must not refresh it again. The original fixture is selected again
and the temporary comparison employee is fired afterward.

## Allocation and timing observations

Measure the actual owner/draw paths on the same thread after warmup:

- draw-record creation, including register-array copies;
- current-resource/layout resolution;
- `StaffRenderer.PrepareWorld`;
- `StaffRenderer.EmployeeFrame`.

The emitted path labels are `fresh-DrawRecord`,
`cached-record-ResolveRecord-including-layout-match`, `actual-PrepareWorld`, and
`EmployeeFrame`. Each path receives 64 warmup calls followed by 21 measured
batches on the same managed thread. The log reports calls per batch, batch count,
warmup count, median and p95 nanoseconds per call, median allocated bytes per
call, bank kind/variant, slot count and frame count. The p95 is the twentieth
sorted batch of 21, rather than an interpolated percentile.

Fresh record creation uses 200 calls per batch; cached-record resolution uses
separate 5/50/200-call batches; `PrepareWorld` uses 50; `EmployeeFrame` uses 200.
These are repeated calls against the fixture, not 5/50/200 employees. The
emitted statistics do not include every individual batch sample. Preserve the
reported measurements, including zero values, without substituting an assumed
budget. Record construction remains separate from resolution of an existing
record so allocation costs cannot disappear into an unreported setup phase.

GPU readback, screenshot encoding, log formatting, and fixture preparation must
be outside timed loops. Warmup and measurement are observations: source/clone
raw words, identities, clone epoch, random streams and gameplay hash must remain
unchanged. The measurement may change renderer scratch buffers/counters; it must
not create scheduler opportunities or mutate gameplay.

These are bounded host micro-measurements, not an FPS benchmark, GPU frame-time
measurement, population-scaling test, performance improvement claim, or universal
budget. Device/backend, build configuration, runtime/JIT and the particular
fixture all affect the result. A separate supported workload and baseline are
needed before making those broader claims.

## Source map

The shared owner lane is in
[`FrontEndSmokeTest.StaffOwners.cs`](../source/OpenTPW/FrontEnd/FrontEndSmokeTest.StaffOwners.cs):

- `PlanStaffOwnerOnly` selects the real single-park startup and terminal exit.
  `PlanStaffOwners`, `HireStaffSmoke`, and `PlanStaffProgram` build the shared
  all-role lane; `WaitStaffOwner` fails after its normal-frame limit.
- `StabilizeStaffOwner`, `StaffObservation`, `StaffWords`, and `StaffTicket`
  define the state baseline; `StaffWords` retains all 70 words.
- `PlanStaffPair`, `StaffPortraitRect`, `StaffWorldRect`,
  `StaffFrameEnvelope`, `StaffProjectFrameRect`, `RequireIsolatedStaffRect`,
  `StaffUiClear`, `StaffRectsSeparate`, and
  `StaffPixelChanges` define the placement, occlusion and pixel controls.
- `PlanStaffLoss` and `PlanStaffRename` drive lifecycle and captured callbacks.
- `MeasureStaffObservers` emits the scoped measurements through `StaffEvidence`.
- `RestoreStaffVisibility` and `RestoreStaffSmoke` own restoration; the existing
  front-end `Dispose` calls the latter as well.

The resource lane is `PlanStaffTextureReload` in
[`FrontEndSmokeTest.StaffResources.cs`](../source/OpenTPW/FrontEnd/FrontEndSmokeTest.StaffResources.cs).
Its helpers `StaffAtlasSlots`, `StaffUniqueAtlasKey`, `StaffEvidenceRect` and
`StaffDisposeOnce` cover all-slot aliasing, unique normalized keys, the fixed
unavailable ROI and exactly-once fault-handle cleanup.
`StaffRenderer.ReloadAtlas(index)` forwards to the existing targeted reload;
`AtlasPackKeys` supplies a defensive snapshot of loaded atlas keys.

## Review and remaining gates

Review the exact source and run these structural checks from the repository
root before interpreting an executed result:

```sh
python3 tools/test_staff_sprite_owner_gpu_smoke_contract.py
python3 tools/test_staff_sprite_owner_contract.py
```

The asset-free C# helper suite
[`StaffOwnerGpuSmokeEvidenceTests`](../source/OpenTPW.Tests/StaffOwnerGpuSmokeEvidenceTests.cs)
authors independent checks of the 70-word snapshot/deep-copy result and
positive/zero/fractional/clipped ROI comparisons, including mismatched viewport
rejection. CPU tests also check overlap and the two-pixel separation boundary
used by placement and final isolation, all-eight-slot atlas aliases, normalized
key uniqueness, fixed unavailable ROIs, partial/throwing disposal, oversized
frame rejection and pull-preserving/invalid-point projection. These CPU checks do not render a GPU
frame. Their compilation and execution are not implied by passing the structural
guard.

The final-head C# build/tests and existing staff, hire/carry, guest, control,
save, and canonical-hash regressions remain required. Review every expected
capture and its matching assertion/log entry, including hidden controls,
source-loss/return, rename and pause. Record incomplete cases explicitly. Retain
private captures and original assets outside the committed evidence; a public
report can identify the tested revision, host, case names and measured results
without redistributing either.

The authored D06 lane provides a bounded live staff GPU texture-reload/disposal
route; source guards and CPU helpers do not close its execution or capture-review
gate. This smoke does not itself close original-executable raster/control-depth
parity, native full-event/modal order, clone-world projection/submission/culling,
nondefault transforms, multipart resources, saved VM/window-history persistence,
or floating-environment
provenance. Those gates remain as qualified in the acceptance map. No measured
result or acceptance status should be upgraded until the corresponding exact
artifact actually runs and the evidence is reviewed.
