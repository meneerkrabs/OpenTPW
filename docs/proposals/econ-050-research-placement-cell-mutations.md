# Draft proposal: ECON-050 — Research placement cell mutations

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
The broad missing-placement description is stale: current WriteFootprint clears eligible paths before typing, stores owners, links @ cells and their cardinal path neighbours, writes add-on flags in both layers and rotates fresh access queue links. ClearFootprint unlinks @ cells and removes add-on flags while retaining supported underlying tracks. Exact native demolition-helper writes remain unproven; the destructor is not the identified clearing owner.

## Source audit
- `source/OpenTPW/World/ParkCellMap.cs:422` (APPROX).
- Current source `source/OpenTPW/World/ParkCellMap.cs:423`: `public void ClearFootprint( IEnumerable<(int X, int Y, ObjectShapeCell Cell)> footprint )`.

## Missing evidence
Trace the remaining per-type writes of native demolition helpers 0x005367A0 and 0x0053B280, including path counts and move-versus-sale behavior; compare against the placement and link logic already implemented on current main.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Specify a cell-mutation transcript for place/remove with preserved unrelated layers; no speculative destructor rewrite.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/FootprintRemovalLayerIsolationTests.cs`; inspect placement/access suites before adding overlapping coverage. The original ParkCalendarTurnTests/ShopGoodsCostTests pointers are historical, not focused demolition coverage.
1. Place across path and nonpath records exact mutations.
2. @ entrance links are bidirectional as evidenced.
3. remove object preserves unrelated track/land flags.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: TRACK-002.
- Dependency: WALK-005.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.

## Current-source reconciliation (2026-10-22)

Inspected public main `1c874922df6e42425dc5638f7e2554c9fde2c68c`:

- [ParkCellMap.WriteFootprint](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/World/ParkCellMap.cs#L368-L411) already unlinks and clears a path lacking NoModify before a fresh type write. It writes footprint owners where applicable, rebuilds @ links and linked cardinal path neighbours, sets add-on flags in both layers, and rotates entrance/exit queue-link bytes for fresh cells. These are existing implementation, not a new work list.
- [ParkCellMap.ClearFootprint](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/World/ParkCellMap.cs#L449-L479) guards free/out-of-bounds and mismatched cells, unlinks @ cells, clears only their NoModify flag before emptying them, and removes AddOn from supported retained track records without emptying the underlying track type. Placement itself replaces certain flags; removal isolation must not be generalized into a claim that every placement preserves every flag.
- [FootprintRemovalLayerIsolationTests.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW.Tests/FootprintRemovalLayerIsolationTests.cs) contains synthetic coverage for add-on record/flag preservation, repeated removal, rejected/free/out-of-bounds footprints and @ neighbour unlinking with independent land flags and track records. These tests explicitly are not native demolition evidence; this source audit did not run them.
- The current ECON-050 annotation identifies demolition `0x00527EE0`, structure removal `0x0052FE50`, flag clear `0x0053A530`, cell remover `0x005367A0` and `0x0053B280`. The destructor `0x004DD0A0` is not asserted to clear cell words. These inherited reverse-engineering annotations narrow the investigation but are not newly verified instructions here.

Next action: obtain the per-type mutation transcript of those native demolition helpers, including path counts, neighbour writes, owner/link fields and the move-versus-sale branch. Compare it with current host placement/removal and author a fixture only for a demonstrated difference. Preserve unrelated-layer regressions and do not speculate that the destructor should clear the map.

This correction does not close ECON-050, change runtime behavior, claim original-save/rendered parity, or treat test-only coverage as completion of the whole proposal.
