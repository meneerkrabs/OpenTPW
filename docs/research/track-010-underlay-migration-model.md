# TRACK-010: synthetic underlay migration contract

This is a proposed design model for #594, not game code or a native oracle.
It advances item 3 (explicit old-save migration) in
`track-010-path-memory-boundary.md`; existing `TrackBridgePathMemoryTests`
remain separate actual-C# coverage and are not duplicated here.

## Information loss and conservative policy

The current writer ORs four ordinary-path clear results into `FlagOverPath`.
Exhausting all 16 boolean footprints gives one false preimage and 15 distinct
true preimages. Any chosen true-bit layout is wrong for 14 other possibilities.
The bit also cannot encode path links, owner, queue direction or placement count.

The proposal therefore represents each cell as **unknown**, **known empty**,
**path** or **queue**, and retains the legacy bit separately as historical bridge
memory. Both legacy bit values initially produce four unknown cells. False can
exclude an ordinary path in the narrow clear-result model; it alone is not an
authoritative full-cell snapshot, particularly for queue/other primary metadata.
Never turn unknown into known empty just because no information was stored.

The illustrative JSON record has its own version 1. This is NOT an existing
OpenTPW save version, a chosen production schema or an import implementation.
Path/queue fields are illustrative nonnegative metadata: actual enum ranges,
ownership types and wire representation require a separate production design.

## Proposed transitions

- New, authoritatively empty footprints start known empty, unlike legacy ones.
- Installing track preserves all four cell records.
- An already-admitted explicit path/queue write updates only its named cell;
  it does not resolve unknown neighbours. Admission itself is not modeled.
- Explicit path/queue removal makes that cell known empty without removing track.
- Track removal preserves known cell content. It rejects unknown content until
  a separate explicit recovery/migration policy exists, rather than inventing it.
  This conservative rejection is a proposed guard, not observed original behavior.
- Live path/queue presence is three-valued: true when any known path/queue exists,
  false when all cells are known empty, otherwise unknown. It is not a bridge
  eligibility predicate; straight/corner/type-25 rules remain separate.

## Reproducible checks

From the repository root, run `python3 -I tools/evidence/test_track_underlay_model.py`.
It uses only Python's standard library and sibling synthetic model, no game
assets, original binaries, .NET, network or external model provider.

Seven tests cover the 16 legacy layouts, both legacy bits, all 81 four-cell
empty/path/queue layouts, both modeled build orders, all four removal positions,
distinct per-cell metadata roundtrips, partial recovery, and malformed records.
These tests establish the proposal's internal consistency, not native parity.

## Still open

Original writer/refresh ordering, path admission, no-modify and reservation
rules, primary-type consumers, guest traversal, station-aware normalization,
real save integration/full resume, deterministic game hashes, rendering and
type 25 remain unimplemented/unverified. No runtime file or accepted bound is
changed, and TRACK-010 must remain active with #594 in draft.

Sources checked 2026-10-24: #594 head
`a934ebb5482ca910a10b358d5c418877b5222e60`; current-main boundary document blob
`91f2c3e6a3801b6e91de284b5f410a67febb0265`. The historical source observations
in that document are not fresh native execution evidence.
