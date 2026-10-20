## Signed arrival history survives park resume (October 19, 2026)

The advisor's five-entry arrival history now accepts the signed group sizes that the arrival simulation already records. Above 1500 guests offline or 500 online, a group keeps the limit minus the counted guests. Rejecting that negative value meant a park could save its advisor state and later be refused at the advisor preflight of resume, before the live park was replaced.

- Only the negative-value rejection is removed. The five-entry capacity, controller validation, speech guard and validate-before-restore order remain.
- Arrival generation, ring contents and scoring stay unchanged. The hype callback reads signed values; a synthetic threshold above 100 percent distinguishes a negative newest group from a zero-clamped one.
- Added asset-free C# regressions cover both arrival update paths and both limits, signed JSON and ring continuation, an absent legacy field, and rejected restores leaving all advisor state intact. A synthetic whole-park regression attaches the actual advisor, produces an over-limit arrival, saves and loads the file, and exercises the candidate's simulation and advisor resume stages. It does not run the GPU/menu shell of `GameFlow.ResumePark`.
- The local workspace has no .NET SDK, so these C# tests require CI execution. No original software was run, original assets published, gameplay hash definition changed or approximation retired.

See the [arrival-ring trace](../reverse/ADVISOR-SCAN.md#challenges-and-golden-tickets).
