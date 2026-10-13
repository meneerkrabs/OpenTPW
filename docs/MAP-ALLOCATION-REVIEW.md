# Live-map legend allocation: independent review

**ACCEPT** `d3ddd5604c9fd28a8cbf7b31866a1093db310fe7` against `f3fbd645`.
No production correction is required. The author cleanup plan and original UI
scope were reviewed; accepted HUD/input/video/profile/visitor-duration work is
unchanged.

The legend previously grouped every eligible visitor by cell, materialized the
groups, and summed their pleased/dissatisfied counts. The new helper traverses
the current visitors directly. Its shared eligibility predicate preserves the
original visibility, recorded-thought and finite/in-bounds-coordinate tests.
There is no cache, visitor-count key or retained result. A nonempty eligible
group necessarily contains a pleased or dissatisfied observation, so the new
zero-totals condition preserves the old no-observations legend decision.

Both counters use checked integer increments, preserving the separate integer
`Count`/`Sum` overflow policy. No saturation, wraparound or arbitrary new count
limit is introduced. This policy was reviewed in source; the bounded tests do
not enumerate more than `int.MaxValue` observations. Existing grouping/projection
and category, role, zoom, pan, clipping and layer controls are unchanged.

Independent baseline reproduction restored only the parent map source during
the safe synthetic red run, then restored the author source byte-for-byte. The
behavior lock passed before optimization; the legend allocation assertion
failed. In the same warmed .NET 10 **Debug** fixture with
`DOTNET_TieredCompilation=0`, the independent measurements were:

| Measurement | Parent | Reviewed change |
|---|---:|---:|
| Map draw plus legend | 492,184 bytes/frame | 327,696 bytes/frame |
| Legend alone | 165,168 bytes/call | 680 bytes/call |

These managed allocation numbers are not browser, GPU or wall-clock performance
measurements. The author's separately recorded Release numbers differ slightly
for the complete draw; configurations should not be mixed.

Three additional review cases use an independent copy of the legacy grouping
predicate across every defined guest state/thought and fixed-point grid edges,
including negative/zero/changing grid bounds. They inspect actual CPU batch
reaction vertices as well as the real legend delegate while visitors move into
one mixed cell, hide, return, change thoughts without changing count, leave the
simulation through a real `Tick`, and are replaced by newly spawned guests.
Visitor/happiness/staff and satisfaction/excitement toggles stay independent and
re-enabled reactions use current thoughts. Existing map projection/control and
overview tests provide adjacent regression coverage.

Verification: **27 focused synthetic map/control/overview cases passed**, with no
skips. Full solution Release build passed with zero errors (143 warnings).
Changed-test style, new-file whitespace, CRLF-aware diff, fidelity inventory and
its eleven Python tests passed. The production map source remains byte-for-byte
the reviewed author's version; only tests and this report/link are added.

Limits: original assets were not opened and browser/GPU/original-map parity is
unqualified. The synthetic HUD is constructed without live rendering resources;
the tests exercise its real draw/label/batch path. Per-cell overlay grouping is
still allocated as before and remains a separate measured follow-up.
