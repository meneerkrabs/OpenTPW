# MD2 running mesh totals: independent review

**ACCEPT** `578e7ebac772cb5f9919dd88fd1f554a9ac13b90` against `df997a11`.
No production correction is required. The review adds synthetic compatibility
and boundary coverage; particle and WCT changes remain untouched.

The five running totals are already required to equal the header totals at the
end of geometry decoding: padded vertex blocks, padded UV blocks, materials,
faces and normals. They are accumulated in `long`. Per-mesh counts are unsigned
16-bit values, padding uses `int`, and the largest local normal sum is 131070;
none of these increments can be negative or overflow their accumulator. Once a
running total exceeds its header total, later meshes cannot repair final
equality. The new comparison therefore changes refusal timing, not this reader's
accepted geometry set. The final equality check still refuses deficient totals.

The check runs after source-region validation but before each mesh's decoded
position, UV, index, normal, material and vertex arrays. It does not impose
nonoverlapping source regions, a new dimension/vertex cap, or semantics on opaque
header fields. Existing file bounds and header-consistency policy remain intact.

Independent red reproduction temporarily used the parent reader with the bounded
32-mesh shared-position fixture: refusal allocated **28,099,360 bytes**, failing
the regression assertion. Restoring the reviewed reader byte-for-byte reduced
the same refusal to **3,686,432 bytes**. The test's 8 MiB threshold distinguishes
this regression fixture; it is not a runtime format limit or a budget promised
for every valid model.

Twelve additional review cases prove:

- Two indexed meshes may share position, UV, normal, material, face and corner
  spans when aggregate header totals agree. Decoded geometry/material results
  match the original single-mesh fixture.
- Two meshes with 65535 vertices each remain supported when their aggregate
  header and backing region agree. The existing single maximum-vertex control
  remains covered as well.
- Each of the five excess totals retains the existing consistency refusal when
  individual source spans still fit.
- Each of the five deficient totals still reaches the final equality refusal.

Verification: **92 synthetic MD2 geometry, boundary, animation and vertex-animation
cases passed**, with no skips; the allocation case was also rerun with its exact
measurement emitted. Full solution Release build passed with zero errors
(142 warnings). Changed-test style, new-file whitespace, CRLF-aware diff,
fidelity-register validation and its eleven Python tests passed. Existing test
file LF endings are preserved; the new file follows formatter output.

Limits: no original assets were opened, and fresh TPW/TPI corpus compatibility,
GPU rendering and original decoder behavior remain deferred. The positive alias
fixtures establish support under the existing port reader's policy, not native
format qualification. This review does not establish a universal allocation or
CPU quota for all otherwise valid inputs.
