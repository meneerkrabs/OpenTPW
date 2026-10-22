# Draft proposal: DET-014 — Reconcile remaining guest RNG scheduling

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Guest standalone/inter-turn updates still differ from the thing sweep; both sideshow-tail and balloon-recreation RNG draws are already implemented.
- Stale exclusion: GuestSimulation.cs:3290 already takes the sideshow tail draw; lines 3183–3185 reseed and take both balloon recreation draws. Do not duplicate these.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:519` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:520`: `for ( var index = 0; index < guests.Count; index++ )`.

- Hosted call-site confirmation: `source/OpenTPW/World/Level.Simulation.cs:25–29` calls Guests.Tick on non-swept ticks.

## Missing evidence
Trace all UpdateGuest call paths against park turns, separating hosted inter-turn updates from standalone test behavior.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Document existing sideshow and balloon recreation draws as excluded; specify one ordered guest RNG transcript and scheduler ownership boundary.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestCreationDrawTests.cs`, `source/OpenTPW.Tests/GuestThingListOrderTests.cs`.
1. Won sideshow consumes exactly its existing tail draw.
2. balloon recreation retains its existing reseed and two draws once.
3. extra host ticks do not silently add turn-only draws.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: DET-013.
- Dependency: WALK-010.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
