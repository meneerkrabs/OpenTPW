# Draft proposal: GUEST-014 — Research saved staff sprite variants

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
New candidates keep drawn variant but original saved members and legacy saves fall back to name/id.

## Source audit
- `source/OpenTPW/World/Staff/StaffSprites.cs:51` (APPROX).
- Current source `source/OpenTPW/World/Staff/StaffSprites.cs:52`: `public static int VariantKey( int nameIndex, int id ) => (nameIndex + id) & int.MaxValue;`.

## Missing evidence
Trace per-role save load construction and variant argument beyond placement-only callers.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define evidence-backed original-load variant rule separately from backward-compatible own-save migration.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. New hire preserves candidate variant.
2. original staff load does not invent RNG draw.
3. legacy migration is deterministic and labeled.
4. two mechanic banks covered.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: DET-015.
- Dependency: GUEST-020.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
