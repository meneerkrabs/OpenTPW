# Staff source and employee portrait owners

UI-092 remains open. This slice connects the bounded staff VM to the five staff
roles, the level scheduler, world staff drawing, and an independently owned
employee-window clone. It removes the employee portrait's hire-walk and
world-publication fallback. Hire/carry animation and guest ownership remain
separate.

This document describes the authored implementation, not a completed acceptance
run. The local environment has no .NET SDK; C# compilation and execution require
CI for the exact final commit. The [65-vector acceptance map](STAFF-SPRITE-OWNER-ACCEPTANCE.md)
distinguishes CPU fixtures from actual host methods and records remaining gaps.
Lawful OpenTPW host visual/GPU smoke is pending. No native raster comparison,
original program execution, or saved sprite-history equivalence is claimed.

## Components and authority

All production paths below are relative to `source/OpenTPW`.

| Component | Responsibility |
|---|---|
| `World/Staff/StaffSpriteRegistry.cs` | Owns mutable VM allocations, generation-safe tickets, ascending scheduler traversal, retirement and identity invalidation. |
| `World/Staff/StaffBodySprite.cs` | One person's nullable source, last-write-wins pending template and interval, person angle, explicit lifecycle events, and published source pose. |
| `World/Staff/StaffSpritePresentation.cs` | Level session, immutable catalog, shared sampled clock, roster binding, fresh reconstruction and partial-before-publication phase. |
| `Hud/EmployeePortraitOwner.cs` | Independent employee clone and Closed/OpenUnbound/OpenBound/OpenRetired state machine. |
| `World/Staff/StaffSpriteMetadata.cs` | Frozen CPU bank identity, animation ranges, frame geometry and performance metadata. |
| `World/Staff/StaffSpriteMotionMath.cs` | Signed-octant movement observation, mathematically generated ratio table, heading publication and qualified interval calculation. |
| `World/Staff/StaffSpriteDrawRecord.cs` | Detached raw-word output records and strict, nonmutating frame/route resolution. |
| `World/Level.StaffSprites.cs` | Final-restore initialization, legacy-load session replacement and outer-frame presentation seam. |
| `World/Staff/StaffRenderer.cs` | Source-record world drawing and clone-record HUD resource resolution; no staff animation clock. |
| `Hud/ParkHud.EmployeeWindow.cs`, `Hud/ParkHud.EmployeeRename.cs` | Actual selection, full-refresh, rename, periodic refresh and removal routes. |

The [core contract](STAFF-SPRITE-VM.md) still owns VM instruction and raw-copy
semantics. Owners do not infer PCs, stack state, deadlines or output from
`SpritePlayer.Steps`, elapsed hire time, display state, renderer recency or a
previously drawn frame. World staff and the employee clone consume the same
source ownership system. Spatial smoothing in the presentation owner is a host
draw aid; it does not select a program or heading.

## Identity, allocation and window lifetime

A live identity contains the level session, the `StaffMember` object and its
captured staff/thing numbers. Tickets additionally contain the slot, generation
and source/clone kind. Reusing either numerical identity or a registry slot
cannot reconnect an old window. The VM Handle remains the slot word; generation
is an owner-side safety property.

Slot zero is invalid. Allocation uses the first available slot and grows by 50
slots. Exhausted generations are not wrapped. Sources and employee clones share
ascending scheduling, but guests retain their existing scheduler. This establishes
only the bounded staff subset; it does not reproduce global native handle numbers.
The supported programs do not read or allocate other instances.

- Closed: no selection binding or clone allocation.
- OpenUnbound: an immediate full refresh found no live source. Later partial
  refreshes cannot bind or allocate, even when that source returns.
- OpenBound: source loss keeps the independent clone alive. A real source return
  permits partial copy into the same clone ticket and epoch.
- OpenRetired: a scheduler retirement invalidated the clone. Partial copy cannot
  recover it; a real full-refresh event is required.

Full refresh unregisters the previous clone before first-free allocation. It
uses the core's deep copy and only its authored handle/XYZ/primary-scale/flags
overrides. Partial refresh uses the exact core allowlist and masks the clone's
existing flags. It retains the clone clock, control state, stack/cache, restart
and last PCs, XYZ and primary scale. Invalid VM state remains diagnostic; partial
copy is not a whole-state repair.

Open on the same selected object, repaint and stats refresh are not full events.
Different selection and successful rename confirmation are full events, even
when an accepted name is unchanged. Rename open/cancel are not. Actual stack
removal, roster invalidation, firing, successful session replacement and level
disposal retire their owned allocations; cleanup is idempotent. Supported staff
programs never END, so retirement timing is tested using a synthetic END program.

## Source events and pending requests

A hire attaches identity without prematurely creating a placed source. Placement
and real return from carrying/rest create fresh Stand state after final position.
Successful pickup deletes immediately, after cancellation returns any previously
carried member. Actual rest deletes; walking to rest retains its source despite
the display state. Rest-building removal distinguishes those cases. Ordinary
synchronization cannot recreate an already initialized but absent source.

A source starts with output false, heading/flags/XYZ zero, interval 62, due equal
to the current sample plus 62, slot-zero frame count from its bank, alpha 255,
primary/secondary scale 1 and zero intro/hold. Heading publication is later than
construction, so an immediate full refresh sees constructor heading zero.

Template and interval are independent overwrite fields. Common pre-update
consumes template first, then interval, and clears both even with an absent
source. Creation/deletion alone preserves pending fields. Requests produced by
the current role handler wait for its next pre-update. Repeating a genuine
setter requests again; a display-field change or countdown is not a setter.

| Event | Request behavior |
|---|---|
| Idle, Patrol, ToRestArea, ToStrike | Stand 3, Patrol 9, Walk 1, Patrol 9 respectively. |
| Handyman arrival/work | ToLitter/ToToilet request 9; Sweeping/Cleaning request 17; cleaning completion ends with pending Stand 3. |
| Mechanic | ToRide requests 9; Repairing requests 18. BusyTicks and countdown-only JobStamp changes do not select/restart the program. |
| Researcher | Researching requests 10. Failed wandering that only changes the stamp does not restart. |
| Entertainer | The existing gameplay choice word selects requests 13–16. The count-one branch still consumes the existing draw; no additional RNG is used. Ordinary application preserves intro/hold; direct-performance construction is separate. |
| Guard | Catch 22 and release 24 are explicit events before route attempts. A later Idle 3 can overwrite either. Escorting alone does not request catch. |
| Continuing movement | Run 2 only for the actual fast-guard predicate; otherwise escort Hold 23 or Walk 1. Compare the source's restart entry, not its current PC or an older pending request. |
| Strike | Entry clears person angle and requests 19. Conditional angle adjustment observes the already-consumed random word. |

Arrived/Stuck movement returns do not invoke the WALK adapter. A continuing
zero-displacement return does, writing person angle zero while interval zero is
the no-change request sentinel. Teleports and arrival snaps do not become WALK.
The legacy one-cell compatibility route does not fabricate steering events.

## Clock and outer-frame order

The presentation owns an epoch host tick, epoch milliseconds and current signed
sample. Sampling uses the existing `GuestSimulation.BodySpriteSampleAtTick`
conversion. `Level.AdvanceSimulation` sets the sample before potential source
creation; pooled and fallback `TourDue` seams schedule source and clone once at
the same sample. Pause creates no opportunities; the existing lost-park scheduler
path continues presentation while gameplay handlers stop.

The ordering contract is deliberately qualified:

1. Full open/selection/rename/close operations happen immediately at their actual
   host callsites. They are never coalesced or deferred to a final selected ID.
2. Existing host HUD input still precedes `Level.Update`. A full event can
   therefore occur before that frame's scheduler. Native full-event phase parity
   is not established.
3. After `Level.Update`, `GameFlow` calls `PresentStaffSprites`: eligible employee
   partial copy first, then source heading/pose publication.
4. Every still-open employee root is eligible, including beneath a rename child.
   This is the host open-root rule, not proof of native hidden/modal traversal.
5. The presentation phase also runs on pause/zero elapsed without ticking a VM.
   Headless harnesses invoke it explicitly after a simulation batch. Draw/repaint
   and raw frame getters do not invoke it.

The registry retains direct signed `now > due`, unchecked deadline addition,
one run per opportunity and the core's 50-instruction cap. Identical samples do
not imply identical clone history if full/partial event boundaries differ.
Tests compare complete state, including retained LastPc, rather than only the
visible frame. Full refresh and scheduling do not commute.

## CPU metadata, draw eligibility and recovery

Staff metadata is loaded and frozen before source construction, independently of
the `OriginalPark` renderer gate. Expected missing/unreadable files, malformed
ESP/TPC data and invalid ranges become unavailable bank records. Identified
invalid banks retain their ordinal; a different bank is not silently substituted.
Unrelated programming failures propagate. Successful discovery order and the
legacy variant-selection policy are retained; that asset policy remains a host
qualification. Shared guest/hire permissive lookup is not changed into this
strict owner resolver.

Missing metadata still creates and schedules an ordinary source with frame
count zero. Its VM may later set output valid; unavailable metadata blocks drawing
without repairing output or PCs. Failed performance requests are consumed without
replay. A same-level source replacement sees the same frozen catalog. Only a new
catalog lifetime can discover newly available metadata.

Each draw record copies raw register words, output and ticket identity; it has
no mutable person/VM reference or texture handle. The resolver checks actual
slot, frame, direction, mirrored range, bank identity and layout. It never wraps
an invalid frame, clamps it into plausible art or selects a Stand fallback.
Performance program 1 can genuinely yield frame count, which is rejected when
outside the resource range.

A screen-routed clone resolves at literal camera yaw zero. A clone whose program
clears the screen bit keeps its raw authored XYZ and primary scale 8. It returns
`WorldRouteNotImplemented` and produces no HUD quad; it is not substituted with
the employee's world pose. A later live-source partial copy can restore screen
flags on the same clone. Source loss alone does not change route: a performance
loop without a flag-writing instruction can keep producing HUD output.

GPU texture replacement can restore drawing for an already valid catalog bank.
Every read resolves the current texture; it cannot reuse a disposed handle or
change VM history, tickets or epoch. It cannot hot-fill unavailable metadata.
The authored tests use managed texture identity sentinels and actual resolver/
replacement methods, not a live graphics device or GPU upload.

The supported transform domain is alpha 255, source primary scale 1 / clone
primary scale 8, secondary scale 1, zero rotations, finite pose and ordinary
single-frame resources. Nondefault transforms and multipart resources retain raw
words and return named unsupported results. Widening this domain requires its
own expected geometry/color evidence.

Employee screen geometry retains feet `(526, 476)`, nominal scale 2.4 and the
frame's two pixel-density factors. Raw clone coordinates use authored normalized
X/Y and host depth slot 1, giving Z = 0.975. That depth is not a recovered native
control-depth word. Synthetic quad geometry, mirroring and asymmetric-origin
checks establish OpenTPW geometry only; they are not native depth/raster parity.

## Loading and excluded persistence

This slice adds no sprite-history snapshot or save-schema field. Fresh sources
are reconstructed once after final economy clock, placement and staff-world
restore. Resting/carried/unpositioned absence is respected. Reconstructed current
jobs may queue a fresh entry; an unknown performing choice uses entry zero
without consuming RNG. This is deterministic reconstruction, not continuation
of unsaved PC/stack/deadline/flags/heading history.

Successful legacy in-place load invalidates the old session and clone, then
anchors fresh owners to the final economy tick without rebasing guest clocks.
Invalid candidate data must leave the live economy, streams, pool and owners
intact. Owner-specific tests exercise actual full-world restore/initialize stages after
a host-envelope round trip and actual original-format import using synthetic
records. They exclude the full application GPU/menu construction shell; the
acceptance map identifies these route limits. Original saved history and
saved-window reconnection remain unimplemented. Optional acceptance vectors
SS01–SS09 are excluded; no snapshot support is claimed.

## Verification and remaining gates

The acceptance map lists exact C# method names. CPU synthetic fixtures include
actual role handlers, roster/placement, Level scheduler/load/dispose, HUD controls,
resource resolution, world quad preparation and HUD Draw routes where stated.
Those labels describe which methods the tests call; they do not mean that the
tests ran locally, used lawful game assets, or rendered pixels on a GPU.

Required final-head checks remain:

- SDK/build/test execution on supported CI platforms using `global.json`.
- Existing staff, hire/carry, guest, controls, save and canonical-hash regressions.
- Fidelity, progress, evidence and structural guards. Python guards inspect
  structure; passing them is not C# compilation or behavior execution.
- Lawful OpenTPW visual smoke covering all roles/work states, source loss/return,
  rename and pause; lawful host visual/GPU smoke is pending.
- Host smoke measurement of allocation rate and per-frame layout-validation
  cost. Draw-record construction copies register arrays; bank-layout matching
  validates current resources. No performance improvement or acceptable budget
  is claimed until measured.

Native full-event ordering and modal eligibility, value-record clone-world
submission/culling under multiple cameras, native control depth/raster output,
nondefault transforms/resources, saved history and original-window reconnection
remain separate gates. The interval helper deliberately selects nearest-p53/p64-
compatible results and safe zero for nonpositive wrapped sums. p24, directed
rounding, unmasked exceptions and universal native floating-environment parity
are not established. The two minimum-int axis angle faults become an explicit
`InvalidAngleArithmetic` no-write diagnostic while other movement requests
continue; this is a named host safety qualification.

No original binaries, media, copied executable lookup tables, private evidence
paths or private reports are part of this documentation. The angle table is
mathematically generated; independent mathematical C# oracles still require
execution on the final supported-platform build.
