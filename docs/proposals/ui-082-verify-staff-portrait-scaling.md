# UI-082: Hire portrait geometry

## Implemented boundary

This proposal now implements a bounded geometry correction. UI-082 remains an open
approximation; this is not full native portrait fidelity.

Only the hire window's figure opts into `HirePortraitGeometry`:

- Its figure uses inner bounds `(513,202)..(723,415)`, width 210 and height 213.
- The picture model keeps outer bounds `(484,179)..(748,443)`, width and height 264.
- Its authored feet are exactly `(626,362)`, calculated with signed integer shifts
  before canvas mapping. A floating quarter-height would incorrectly give 361.75.
- Its nominal scale is 2.4 canvas units per reference sprite pixel. Per-frame A/B
  density factors apply to both signed origin offsets and dimensions, through the
  existing `UiSpriteFigure.Layout`. Horizontal mirroring reflects the hotspot and
  reverses the UVs.
- Center-anchor mapping happens once, through the existing figure draw path.

The existing `PortraitFrame` callback, role-plus-variant phase key, walking loop,
selection and resource-fallback policy are unchanged. The employed tab deliberately
shares the corrected hire-box geometry and its existing walking-loop callback.
That tab is the documented `[EXT:staff-employee-view]` host extension. The original
hire selector reads applicants; this is not evidence for a native employed tab.

The generic figure default stays 3.2 with rectangle-relative feet. The separate
employee window now uses its own UI-092 nominal 2.4 geometry and feet `(526,476)`,
with its existing live-world-or-hire-loop-fallback behavior unchanged;
the visitor window keeps 3.6 and its own inner rectangle and feet `(598,450)`.

## Evidence and derivation

Read-only static tracing identified the portrait control's inner-rectangle getter,
the dedicated hire player, its fixed script, and the downstream screen-sprite
consumer. The original executable was neither run nor emulated. No original media,
disassembly, private input metadata or original capture is included here.

The hire control's layout opcode 3 supplies the inner rectangle, while model
attachment separately retains the outer frame. The hire position calculation uses
signed half-width and quarter-height shifts. In nominal authored coordinates:

- `feetX = left + ((right - left) >> 1) + 8`
- `feetY = bottom - ((bottom - top) >> 2)`

For the hire's width 210 and height 213, the shifts give 105 and 53. Thus feet are
`(513 + 105 + 8, 415 - 53) = (626,362)`. The synthetic odd rectangle
`(511,201)..(724,416)` gives `(625,363)`. The hire path does not use the visitor/employee
clone helper's additional scale-dependent Y term.

The dedicated player sets primary scale 8 and a fixed walking script: slot 1,
direction 5, frames 0 through 7. Its own flags select the screen-sprite consumer;
this route is verified independently of the visitor's scale. The consumer uses
primary scale times 0.05, display aspect on X and descriptor A/B density values.
At the ideal 2048 by 1536 canvas and reference A=B=128:

- Horizontal: `8 * 0.05 * (1536 / 2048) * (2048 / 2) / 128 = 2.4`
- Vertical: `8 * 0.05 * (1536 / 2) / 128 = 2.4`

The second register is conditional rotation data, not an extra size multiplier.
No separate hire override appears between the dedicated player's scale and this
consumer. The native single-precision constant and later viewport transforms mean
2.4 is a nominal geometry factor, not bit-identical native arithmetic.

A synthetic 20 by 40 frame with origin `(-8,-38)` and A=B=128 places its unmirrored
quad at `(606.8,270.8)`, size `(48,96)`. Mirroring moves the left edge to 597.2 and
reverses U. With A=256/B=64, origin and size use factors 1.2 and 4.8 respectively.

## Explicit residuals

- The host returns frame zero immediately. A fresh native hire player starts
  invisible and first emits on a scheduled FRAME instruction.
- Host elapsed-time playback differs from the native strict due-time scheduler,
  its sampled-time rebasing, pause behavior and long-frame handling.
- Invalid selection clears the host key/frame. A native invalid applicant lookup
  can retain the previous player. Initial default-selection/hover order also
  remains qualified.
- Host close/reopen can retain phase for an unchanged role/variant. Native close
  and role reset invalidate the player and cached key.
- The hire script enables a global rotation path. The host still draws an
  unrotated quad; the global angle is not assumed to be zero.
- Native control depth, screen depth adjustment and full occlusion are not
  reproduced by local insertion order.
- The native viewport uses subpixel scales, offsets and per-view integer
  truncation. Host uniform scaling and non-4:3 anchoring are existing policies.
  Clipping, filtering, blending and final raster/real-data visuals remain
  unverified; the figure's bounds do not introduce a new clipping rectangle.
- UI-092/UI-910 independent-clone VM timing, partial refresh and source-loss
  histories remain separately owned. Variant-bank availability keeps its
  existing GUEST-014/GUEST-020 qualifications.

## Regression coverage and validation

`HirePortraitGeometryTests` exercises the actual hire screen callback and its
ordinary CPU `Draw`, rather than replacing the callback with a synthetic frame:

- Every staff role, two distinct variant banks, full and mirrored direction sets,
  initial frame and later walking phase.
- Inner/outer bounds, exact integer feet, both A/B offsets and sizes, UV order,
  and 2048x1536, 640x480, 1280x720, 1920x1080, 1000x1000 and 801x601 outputs.
- Signed shift arithmetic on even, odd and synthetic negative differences.
- Same-key applicant changes, variant/role changes, sorting, filtering, switching
  between applicant/employed views and closing/reopening the same screen.
- Missing renderer, bank or texture, invalid selection and recovery, retaining
  the existing host behavior rather than claiming native lifecycle equivalence.
- Economy save serialization and world RNG remain unchanged by UI observations.

`StaffPortraitTests` keeps its generic feet/layout tests, now clearly labelled as
generic. `VisitorLivePortraitTests` changes only actual hire-control expectations;
its generic default, actual employee and actual visitor geometry controls remain.
`EmployeeLivePortraitTests.HireCallbackIsUnchangedAndLiveRoleChangesStillUpdateTheEmployeeFallbackClock`
continues to cover independence of the hire clock and employee live/fallback path.
The new source guard is included in the existing multi-platform build workflow,
which still builds the solution and runs the entire CPU test project.

Local Python/source guards, register/progress checks and available JavaScript
checks are recorded in the implementation progress entry. The implementation
workspace has no .NET SDK: C# compilation, the focused tests and the full CPU suite
must run in CI on the published head. No GPU or real-data visual pass is claimed.

## Ownership

The runtime change is confined to hire geometry and its HUD wiring. Shared figure
code receives only a documentation correction; its behavior is unchanged. Staff
rendering, animation, simulation, separate employee/visitor runtime, the blocked
information-window file and bridge work are outside this patch. UI-082 is narrowed,
not retired. The original proposal is updated in place rather than duplicated.
