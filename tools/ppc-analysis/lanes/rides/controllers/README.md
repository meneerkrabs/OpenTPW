# Controller reference primitives

This standalone .NET8 console project has no package/project dependencies and
is not linked into production. It uses the existing interpreted
`controller-contracts.json`, never original executable or COS bytes.

```sh
dotnet run --project tools/ppc-analysis/lanes/rides/controllers/ControllersWitness.csproj
dotnet run --configuration Release --project tools/ppc-analysis/lanes/rides/controllers/ControllersWitness.csproj
```

Proof anchors are `15313d2`/`controller-native.json`, the39 native command
contracts and37 PC-used command/callsite groups. The exact Mac SHA and function
offsets are recorded there. Controller memory strides are not serialized-file
strides. Shared TPI metadata at `7f2a6b2`,
`tools/tpi-compare/format_questions.py` and `docs/TPI-COMPARISON.md`, groups all80
COS files into16 equal-body groups after offset132, five languages each. It
also finds no TrackInfo/Direction key in369 parsed SAM inputs. This identifies
comparison boundaries and missing schema evidence; it does not decode tracks.

The helper implements the following bounded observations:

- `PassengerRing` uses supplied allocated capacity, queued/held counts,
  configured limit, cursors and positive visitor IDs. Append checks
  queued+held against allocated capacity, writes the supplied ID and wraps
  the cursor. This is a buffer operation, not park admission or allocation.
- Dequeue requires an externally observed release boundary. Empty counts and
  equal read/boundary cursors produce typed empty/blocked statuses. Missing
  boundary produces unsupported. The helper does not invent boundary/held
  transfer rules or clear native storage when logical ownership advances.
- Room query uses signed MAX with a supplied global minimum. Global100 and
  free−1 returns100; global0 and free−1 returns0. The append/full predicate
  remains a separate allocated-capacity check.
- A capacity plan computes `min(min(max(request,global),definition),train)`
  and distributes passengers over explicitly observed eligible cars.
 10/3 gives3,3,4; excluded cars receive0. Applying a live rebuild is unsupported
  because teardown/redistribution and host callbacks are not recovered.
- `CoasterVehicleState` separates logical rider allowance and observed rider
  count. Two vehicles with rider counts2 and1 have3 total passengers. A reduced
  allowance does not discard existing riders. Physical paired seats or node
  ownership remain unsupported; the logical allowance proves neither.
- The64-vehicle guard is specifically BUMP (`0x24374`), combined with configured
  count and nonzero phase. Passing the guard does not promise allocation or
  admission success. A COAST implicit64 limit is explicitly unsupported.
- TOUR negative occupancy advances toward zero and reads slots in reverse
  boarding order. The helper models slot arithmetic, not loading/state changes,
  IDs, animation or accounting callbacks.
- Typed command metadata exposes input/output and accumulator directions.
  Missing effect results produce unsupported instead of a fabricated zero.
  Required-variable gates skip calls; consumed COAST7 is a no-op. No real
  controller results or admission outcomes are generated.
- `SampledMotionPrimitives` operates on supplied adjacent sampled XYZ values,
  never serialized tracks. Interpolation rounds `next*fraction` to single,
  then uses a single fused operation for `current*(1-fraction)+product`.
  Height target uses single height subtraction/multiplication and a single
  fused modifier/acceleration/speed operation before the supplied floor.
  `motion-native.json` pins the schema→loader→tick field assignments.
- Distance retains the helper0x4316c mixed precision: single coordinate deltas,
  single Y/Z squares, double fused X-square plus Y-square, double sum/sqrt,
  then single return. Finite-domain validation is tool policy. Full train
  advancement returns unsupported; original FPSCR/exceptional modes are
  unqualified.

Sixteen synthetic cases cover full/empty, held counts, wraps, configured-limit
changes, clamping/distribution, multiple riders per vehicle, reverse order,
BUMP63/64 boundaries, all39 command directions, unavailable results, and typed
COS/track-position/direction/physical-seat diagnostics, interpolation endpoints,
uphill/downhill/floor behavior, a fused-rounding regression and mixed-precision
distance. Input bounds and snapshot
validation are evidence-tool policy. Full coaster/cart motion, original track
data layouts, save linkage, complete controller lifecycle and Windows runtime
equivalence remain unqualified.

The native boarding function0x3e2f8 removes guests FIFO from the pending ring,
but car selection starts from an RNG-derived candidate and scans with wrap for
available allowance. The ring helper therefore does not establish car/seat
order. TOUR14 parameter0 changes positive occupancy into negative occupancy
for type1 entries unless controller+52==3, connecting the already qualified
reverse departure-slot arithmetic to an actual transition. Neither observation
is a complete controller/admission implementation.

`seat-native.json` additionally pins the producer that filters20-byte runtime
model attribute records by mask0x80 into carDefinition+44, one-based matching
ordinal to raw index; +48 is the count. These are attribute indices, not
hierarchy node IDs. The corpus tool reuses the existing PC model reader to
record14 declared model bindings and selected attribute indices, unchanged
between baseline and verified Patch2 despite two changed model identities.
Per-train capacity+760 sums front/centre/rear role counts, then fleet capacity
+792 multiplies by maximum trains. Two passenger-ID buffers per car remain
separate from two-person seating. Native/file loader linkage, transforms and
host visual ownership remain unsupported by this reference project.

Round6 independent review corrected the earlier MIN interpretation of the
query and first capacity selection. `controller-native.json` now pins the
comparison registers and selected pointers. Counterexample regressions failed
before the fixes and pass in Debug/Release, including distinct definition/train
upper caps. Global0 is used for the10/3 distribution example.
