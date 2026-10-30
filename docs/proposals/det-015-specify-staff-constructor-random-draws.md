# Draft proposal: DET-015 — Specify staff constructor random draws

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: proposal head `b66a1ffe5162ef0e4f2ade8c951a75ba3cb2087e`; reconciled against main `0910d275638548f12ebfa41915bb71e64f091119` source on 2026-10-30.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
The missing-draw premise is substantially superseded: pooled staff hires consume the base-speed draw and guards consume their own variant draw; level placement runs role searches after those draws. Remaining boundaries are the headless path (no thing pool means no constructor draws; without the level placement-world hook, search waits for the first world update) and the intentionally separate weather stream. No staff-constructor reseed is proposed: repository binary annotations identify `0x004F7B7E` as online-avatar behavior, not a staff constructor.

## Source audit
All references below are to the reconciled main baseline, not claims about the older proposal branch's runtime.
- `source/OpenTPW/Economy/ParkEconomy.cs:105-106` retains `APPROX:DET-015`, describing implemented draws/searches, the headless boundary and separate weather stream; it records sequential advisor mode 2 as taking no draw.
- `source/OpenTPW/Economy/ParkStaff.cs:500-529`: allocate the thing before consuming one base-speed draw and (guards only) a variant draw, when the thing pool and RNG are attached. Base speed is overwritten by grade (60 below grade 3, otherwise 100), with entertainers fixed at 60; other roles retain the candidate variant.
- `source/OpenTPW/World/Staff/StaffWorldSimulation.Constructed.cs:6-24`: placement construction applies cell effects and invokes the role search before the first sweep.
- `source/OpenTPW/Economy/ParkSaveFile.cs:1105-1108,1181-1210` and `source/OpenTPW/Economy/ParkStaff.cs:614-630`: restore constructs a park (including its initial candidate pool), then restores the saved generator. Saved members/candidates are installed directly, not rehired, with saved scalar/world speed applied afterward. This does not mean restore takes no temporary initialization draws; it does not replay hire/candidate draws on the restored stream.

## Missing evidence
Original-game trace evidence for the remaining headless/world and weather-stream scope decisions is still required. Distinguish candidate generation, hire construction and saved-member restoration; implemented contracts and synthetic regression pins are not themselves an original-game oracle.
- No original game files, executables or save corpus were inspected for this reconciliation.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Preserve the implemented constructor draw/allocation/search order; specify and review the remaining headless-world boundary. Retain weather-stream unless separately adjudicated.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner's implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Inspected, not executed in this reconciliation: `source/OpenTPW.Tests/StaffCreationDrawTests.cs` (constructor speed, guard variant, no-pool draw boundary, save/restore and pickup/drop) and `source/OpenTPW.Tests/StaffConstructorSearchTests.cs` (all five roles, pinned post-click RNG/job/destination, no repeated first-update search and headless boundary). Guest ordering regression homes remain `GuestCreationDrawTests.cs` and `GuestThingListOrderTests.cs`; they were not re-audited here.
1. Re-run the existing per-role constructor/search cases and retain recorded seed, allocation order and next world word; label synthetic pins as synthetic.
2. Verify saved staff/candidate variants and generator state across load without replaying hire draws on the restored stream; include legacy speed fallback and pre-first-update saves.
3. Verify advisor mode 2 adds no spurious draw and keep weather-stream decisions separate.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: WALK-010.
- Dependency: GUEST-014.
- Dependency: EXT:weather-stream.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results and independent review; this document performs none.
