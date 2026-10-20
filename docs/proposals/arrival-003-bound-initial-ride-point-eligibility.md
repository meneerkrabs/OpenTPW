# Draft proposal: ARRIVAL-003 — Bound initial ride point eligibility

- Classification: **approximation**.
- Mode: Bounded coaster-economic implementation; kart/water first-walk residual remains.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: Coaster economic consumer implemented with synthetic tests; exact-head CI and final review remain merge gates. ARRIVAL-003 is not retired.

## Current residual
ParkPoints now uses the native flags/intersection/edit predicate independently of the host admission sample guard. The kart/water track flag initially false remains outside this change.

## Source audit
- `source/OpenTPW/Economy/ParkEconomy.cs:1770` (APPROX).
- Current source `source/OpenTPW/Economy/ParkEconomy.cs:1771`: `public int ParkPoints()`.

## Missing evidence
Witness placement-to-first-circuit ordering and original complete-track flags before samples exist.
- An authorized local tp.exe is now available for bounded static inspection (hash and addresses below); no original execution or real coaster-save witness is claimed.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Original design intent: separate safe runtime sample availability from economic eligibility and establish the import dependency. The bounded implementation and synthetic reconstruction coverage are recorded below.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ArrivalVehicleTests.cs`, `source/OpenTPW.Tests/ArrivalWalkTests.cs`.
1. Compare points before and after first circuit walk.
2. incomplete/open-cut coaster gives no false admission.
3. Synthetic original-format coaster import now has production import/rebuild coverage; this does not establish fidelity to a real built-coaster save.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: SAVE-920.
- Dependency: QUEUE-023.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- The bounded coaster-economic change may proceed after independent source review and exact-head CI; retain the unresolved kart/water first-walk work and ARRIVAL-003 identifier.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.

## Bounded implementation evidence (2026-10-20)

Source baseline: `d655ed1bf562c113662ca53417a6f86fc0703eb7`.
Local tp.exe SHA-256: `5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`.
Read-only static inspection, not native execution: `0x441970`–`0x4419af` requires a nonnull thing and controller lookup,
controller `+0x140 == 0`, `+0x3c` bit 0 set and bit 1 clear, and controller different from global `0x790fec`.
The counts-test type-3 branch `0x4dd9ad`–`0x4dd9c1` calls this shared helper. Tool entry `0x447520` calls
`0x430ac0`, which stores the edited controller in `0x790fec`. No sample count, rider count or controller-state test occurs.

The host's new `CoasterRide.CountsForParkPoints` carries that predicate into actual `Level.SyncObjectEconomy`.
`IsOperational`, `AcceptsGuests`, guest selection/admission and reopen consumers retain all existing guards.
The edited-coaster exclusion already existed; the old ORACLE text saying otherwise was corrected.

Synthetic coverage: complete closed restored track before samples versus after rebuild; incomplete/open/intersecting/edited
controllers; inconsistent CutAfter retains admission safety; actual Level synchronization and numeric park points through
edit enter/switch/exit and missing controller; synthetic original-format import before its first motion update.
Import still recomputes intersection geometry: no claim of raw saved `+0x140` fidelity. Local .NET restore/build was not
retried because of the existing environment denial; runtime results must come from exact-head CI. A separate 384-case
Python transcription agrees with the decoded branch logic, but is neither C# execution nor an original-game oracle.

Kart/water first-walk timing, collaborator-owned Level.Tracks work and broader arrival behavior remain unchanged.
