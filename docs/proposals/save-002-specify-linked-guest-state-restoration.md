# Draft proposal: SAVE-002 — Specify linked guest state restoration

- Classification: **missing**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Import drops queue/boarding/riding/using/held states and off-path guests, flattens activity and stamps; admission and queue links absent.

## Source audit
- `source/OpenTPW/Economy/OriginalEconomyImport.cs:573` (APPROX).
- `source/OpenTPW/World/Guests/GuestSimulation.OriginalImport.cs:170` (APPROX).
- Current source `source/OpenTPW/Economy/OriginalEconomyImport.cs:574`: `evidence.Add( $"[APPROX:SAVE-002] {what} guard job (state {word}, perpetrator thing {saved.RoleTarget}) imported idle: the guest is not imported in the park." );`.
- Current source `source/OpenTPW/World/Guests/GuestSimulation.OriginalImport.cs:171`: `if ( dropped.Count > 0 )`.

## Missing evidence
Real saves for states 8 and 11-17 with attraction/guard links; importer rejection diagnostics and state invariants.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan transactional two-pass guest/attraction/guard restore; distinguish debug-only event ring from functional state.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/OriginalGuestImportTests.cs`.
1. Queue links remain reciprocal and acyclic.
2. called guest belongs to ride once.
3. guard/held relation restored both ways.
4. resumed timers preserve pre-save age.
5. malformed links rejected safely.
6. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
7. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: QUEUE-003.
- Dependency: SAVE-006.
- Dependency: SAVE-920.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.

## Current-main reconciliation (October 22, 2026)

The historical statement that all off-path guests are dropped is no longer current.
`GuestSimulation.OriginalImport.cs` imports supported in-park states where they
stand, including queue cells and non-walkable cells, and lets the state handler
recover. `OriginalGuestImportTests` covers queue-cell preservation, off-path
recovery and an enclosed guest. It also verifies known imported arrival age and
its integer display phase. These implemented subparts must not be redone.

The functional remainder is still substantial:

- States 11–17 (queue, boarding, leaving/using an attraction and guard-held) are
  rejected. State 8 activity is flattened; pre-save activity stamps do not yet
  reconstruct the linked activity's elapsed time.
- Admission and reciprocal queue links are not restored. Guard chasing can resume
  when its target was imported, but an escort or chase of a dropped target falls
  back to idle. Restoring one endpoint alone would be unsafe.
- The 32-slot event ring remains omitted. Establish whether a field is functional
  or debug history before making it a prerequisite of linked-state restoration.

The bounded regression addition in #803 exercises rejection/placeholder behavior;
passing those tests does not restore these missing states or complete this proposal.
Next implementation needs a staged two-pass mapping of guest IDs to attraction,
queue and guard identities, validation of reciprocal/acyclic links and timer units,
and atomic publication only after all linked endpoints validate. Obtain an allowed
save witness or independently supported synthetic contract for each restored state;
reject malformed links without partially installing either endpoint.

This reconciliation is source review, not a new original-game oracle or runtime
test run. SAVE-002 remains open; retain the evidence and review gates above.

