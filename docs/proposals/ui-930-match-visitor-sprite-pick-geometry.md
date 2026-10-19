# UI-930: Match visitor sprite pick geometry

## Status and bounded implementation

The October 18 plan-only draft is superseded by a visitor-only geometry correction.
Static inspection now supports the literal circle, pointer conversion, canonical
projection adapter and primary-scale preservation for ordinary body programs 1–8.
UI-930 remains open. This is not complete picking parity or a rendered-view claim.

The old proposal's render-derived records and single-frame-delay suggestion are
not implemented. Two separately retained native results are proven; their total
event latency under skipped or multiple views is not. Current synchronous queries,
renderer frame selection and lowest-ID ties stay unchanged.

## Literal geometry and source evidence

The world route copies the projected hotspot before placement at `0x589E92` and
calls `0x5890E0` with zero scale overrides. Its defaults are `sx = 20*s*a` and
`sy = 20*s`, where `a = drawableHeight/drawableWidth` (the display setup divides
height by width at `0x582EB1..0x582ED5`). Frame width/height, unsigned divisors A/B
and signed origins are read at `0x589141..0x5891D4`.

For host clip point h and the existing host model scale m = 0.2, the canonical
90-degree adapter is:

- nativeW = sin(45 degrees) * h.W / m
- centerX = h.X/h.W + mirrorSign * originX/A * 20*a/nativeW
- centerY = -h.Y/h.W
- radiusSquared = float32(0.0002) * ((width/A * 20*a)^2 + (height/B * 20)^2)
- hit only when squared pointer distance is strictly less than radiusSquared

Mirroring negates originX; the mirrored bounding rectangle's `-(originX+width)`
is a different edge. Y stays at the hotspot: vertical-origin placement occurs
at `0x5893A0`, after picking. Radius has no division by W. These are not a
projected bounding-circle heuristic and do not sample texture alpha.

The sprite hotspot already uses view * projection * screen (`0x578485..0x57852F`,
`0x57820E`). Native screen scaling is `(2*a, -2)`; host `OriginalProjection.Create`
has M11 = 2*a, M22 = 2, M34 = -1 at 90 degrees, subject to host float rounding.
`ObjectPlacement.ModelScale`, `OriginalParkPlacement` and `ParkCameraMode.UnitScale`
account for the engine/native depth conversion. Native and host projected Z are
not interchangeable. The API continues returning the legacy final-corner host Z
for existing renderer aggregation, rather than substituting native W.

The current rendered quad's origin displacement is twice the literal native
placement displacement in ideal canonical arithmetic. This correction computes
its own literal geometry and makes no shared `SpriteScale` or rendering change.

Ordinary creation initializes primary scale to 1 (`0x475944`, `0x47599B`); it is
register 8 (`source+0xA4`, register base `source+0x84`), not register 9. All complete
programs 1–8 at table `0x75A418` preserve that scale, including finite programs
and their return to stand. Emission carries it to draw record `+0x2C`
(`0x54208B`, `0x5422B2`). This does not establish arbitrary imported-source scale.

## Positive compatibility boundary

Only the currently registered ordinary kid/costume renderer, owned by the current
level and simulation on an original park, may opt in. Existing visibility, Draws
callback and in-park filters still run once in their original order. Additional
owned-state checks require:

- A nondeleted visible renderer, live material pipeline, live atlas texture and
  positive frame dimensions contained in the atlas
- A live positive guest identity, valid current kid/costume category and peep
  type, finite position/heading/distance/time, nonnegative distance, and no swim
  session or invisible ride/held/removed body
- No slot/feet/depth callbacks, depth-pulled/rotated/bottom-anchored slot sets,
  exact-direction override or swim fallback; returning zero from a custom
  callback does not qualify it
- A live body player with supported active/current programs 1–8, a non-null valid
  emitted output, matching costume source, and no reconstructed history; supported
  pending requests are observed without applying them
- Positive frame divisors; unsafe signed integer origin sums are ineligible
- A canonical full-viewport camera and valid finite projected geometry

The player's output proves bounded scale history only. Geometry continues using
`SelectSpriteAndSlot`'s current selected frame and mirror. No query creates,
advances or replaces a player; no pose collection, render preparation or portrait
publication is introduced. Imported/legacy reconstructed histories retain the
rectangle, potentially for their lifetime. Fresh or faithfully restored bounded
host histories can qualify. Figures, head sprites, generic staff and extension
routes are not moved to the new circle path.

Eligibility is explicitly separate from hit outcome. Unsupported or numerically
invalid data uses the legacy route. A valid circle that misses returns no hit
for that candidate and never retries its rectangle. Legacy corner clipping is
shared unchanged, so a geometry rejection for a clipped corner cannot revive
that corner through fallback.

## Numerical and drawable-input contract

The camera must be `ParkCameraMode`, use original projection and have FOV exactly
90. Every supplied view/projection field must equal the current camera field;
every projection field must equal `OriginalProjection.Create(width/height, 90)`.
There is no aspect/projection epsilon. Invalid/overflowing/underflow-to-zero aspect
ratios and nonfinite expected matrices are rejected.

The view must be finite affine, with exact zero M14/M24/M34 and M44 = 1. Double
basis dot products, squared lengths and determinant are checked against the
exact float constant `1e-5f` widened to double. Lengths must be within that bound
of 1, cross-dots within that bound of 0, and determinant positive and within that
bound of 1. This accommodates float camera round-off; larger scale/shear and any
reflection are unsupported. Finite translation is allowed; transformed overflow
is rejected per hotspot. Circle calculations use doubles with the original
float radius constant, not an assertion of bitwise x87 equivalence.

Mouse and viewport are both drawable coordinates. Bounds are inclusive at zero,
exclusive at width/height. Finite fractional input is adapted as:

1. virtualX = trunc(double(pixelX) * 2048 / drawableWidth)
2. virtualY = trunc(double(pixelY) * 1536 / drawableHeight)
3. pointerX = float(virtualX * double(float(1/1024)) - 1)
4. pointerY = float(virtualY * double(float(1/767.5)) - 1)

Validation precedes multiplication. Finite float inputs cannot overflow the
double scaling operation, and in-bounds quotients fit the original signed-16
virtual words. Pixel values are not truncated before scaling. The final-only
float store preserves the documented center-Y asymmetry, about +0.000651449.
Fractional/high-DPI input has no exact original equivalent. Matching aspect alone
cannot detect a caller that supplies a consistently scaled but wrong logical
viewport, so callers remain responsible for drawable-space consistency.

## Preserved behavior and residuals

The host's inclusive projected-Z range [0, 1] and all-four-corners clipping remain;
the original strict hotspot clip and coarse extent culling are not reproduced.
Current kid/costume nearest-Z aggregation, lowest-ID ties, equal/nearer swimmer
precedence, and the post-winner hovered-cell plus eight-neighbor gate remain.
The neighborhood never prefilters candidates or retries a second-best visitor.
All Shift/locator, modal, pause, build/carry/patrol and press/release ownership
rules remain in the existing HUD code.

Residuals include current host animation/frame source, bounded-history coverage,
native pick-result epochs, allocation/bucket/material tie ordering, cross-kind
competition, native culling/near-far behavior and x87 rounding, high-DPI adaptation,
the doubled host quad displacement, terrain/object occlusion and real rendered-view
validation. Native timing reads the prior render pointer at `0x532C17`, publishes
the old resolved owner at `0x540B6E..0x540B79`, then retains the new owner at
`0x540B7E`. A one-frame-lag shortcut is not justified by those two retained stages.
Native equal W keeps the first visited sprite; the host does not retain the native
allocation/bucket/material traversal identity.

## Validation and acceptance

Synthetic C# tests exercise the actual production renderer query, canonical HUD
press/release, source rejection, live/dead resources, callback counts, all eight
body programs and finite transitions, nonmutation, current selected frame,
circle miss without rectangle retry, depth/ID/swimmer precedence and post-winner
neighborhood rejection. Pure tests use actual `OriginalProjection.Create` and
translated/rotated `CreateLookTo` matrices, independently derive the factor-two
witness, and cover pointer, strict circle and matrix tolerance boundaries. A
frozen pre-change rectangle oracle compares general fallback outcomes/depth.

Focused command in an SDK environment:

```
dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --filter "FullyQualifiedName~VisitorCircle|FullyQualifiedName~VisitorWorldSelection|FullyQualifiedName~VisitorPickNeighbourhood|FullyQualifiedName~VisitorLivePortrait|FullyQualifiedName~VisitorSprite|FullyQualifiedName~Locator|FullyQualifiedName~SpriteWorld|FullyQualifiedName~SpritePixelScale|FullyQualifiedName~SpriteDepthPull|FullyQualifiedName~Staff|FullyQualifiedName~Pool"
```

The existing required CI builds and tests the full solution on Linux, Windows and
macOS. Local Python source/arithmetic and documentation checks are separate from
C# execution. The authoring environment has no .NET SDK: C# tests, native GPU
execution and browser visual checks were not run there. Actual matrix/runtime CI,
independent source review and overlapping/occluded host-view checks at multiple
zooms/aspects remain required before any full-fidelity claim. Original software
was only statically inspected, never executed or emulated. No original media,
executable contents, fingerprints or private inspection artifacts are published.
