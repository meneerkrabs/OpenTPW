# TRACK-004: Complete junction clear links

- **Superseded (October 27, 2026):** PR #879 (round 9 F) ports the clear op `0x53B280` and the replays from the corner stacks in full and retires `APPROX:TRACK-004` (docs/TRACKS.md, "The clear op"). The rest of this proposal is kept as history.

## Status and classification
- Classification: approximation.
- Baseline: current main `75fdaed088bd20a23b7157dae565d2d52ace7911`.
- Characterization-only review slice: the tests characterize the clearing already implemented by `TrackEditor.Cut`/`ClearCell`. No runtime code was changed, and no helper or duplicate clear API is proposed.
- Tests are authored but not executed: this environment has no .NET SDK; exact-head CI awaits independent review. No passing result is claimed.
- No annotation retirement, commit, publication or merge. This is not a completed feature and claims no parity.

## Current baseline behavior
The earlier brief's premise that `ClearCell` clears perpendicular `Link` bits was wrong. On the baseline above:

- `Cut` first removes the cut line's directional flow `Link` bit on a junction (`TrackEditor.Cut`: `cell.Link &= ~along`) before it calls `ClearCell`. The cut's own direction therefore does not survive into the clear.
- `ClearCell(pass: true)` decrements `Overlap` once and then removes the perpendicular physical connection `Mask` pair based on the remaining single `Link`: link `0x01`/`0x10` removes mask `0x04`/`0x40`; link `0x04`/`0x40` removes mask `0x01`/`0x10`. It does not clear perpendicular `Link` bits.
- It repicks `Family`/`Piece`/`Rotation` and the bridge state through `TrackBuilder.Refresh`. Type, the surviving mask arms, the surviving link, flags outside the bridge bits, parent, alternate mask, segment and owner are otherwise retained.
- At `Overlap >= 0` after the decrement the tile stays. At `Overlap < 0`, `ClearOrigin` removes the tile and unlinks neighbours, except the first station tile and `station.Entry` when `replay` is false. `replay: true` bypasses that station spare.
- A zero or multibit `Link` matches no `ClearCell` switch arm: no axis is removed and no exception is thrown.
- Repeatedly clearing a survivor decrements `Overlap` again; the whole operation is not idempotent.

## Remaining evidence gaps
- Native overlap underflow, saturation and wrap were not independently reverified in this slice. The existing public `ClearCell` annotation already traces below-zero removal and station exceptions; this slice characterizes the managed implementation, not a new native trace. Tests avoid `short.MinValue`.
- Recursion into the three extension cells in the original clear is not characterized here.
- The other `+0xD` writers (the type-25 walk and the edit sub-modes) remain outside this slice.
- No original executable or reference capture was run, and no parity is claimed.

## Characterization tests (authored, not executed)
`source/OpenTPW.Tests/TrackJunctionClearTests.cs` uses synthetic `ParkCellMap`/`TrackBuilder`/`TrackStation` fixtures and the test assembly's `InternalsVisibleTo` access to call the internal `ClearCell`. The test tiles are synthetic and detached from the placed station except for the explicit station-exception cases; they do not establish a complete ride lifecycle. It covers:

- Direct `ClearCell(pass: true)` over crossing mask `0x55` and every tee mask (`0x15`, `0x45`, `0x51`, `0x54`) for all four single `Link` headings. The assertions are the perpendicular mask pair removed, the single link preserved, one overlap decrement, and metadata preserved apart from the intentionally repicked piece/rotation.
- A positive overlap keeping the tile, and repeated clears decrementing the survivor again rather than claiming operation-wide idempotence.
- Zero overlap removing a non-station tile, the surviving-axis neighbours losing their arms, and the current perpendicular-neighbour behavior recorded explicitly.
- The first station tile and the entry tile being spared below zero unless `replay` is true.
- Zero and multibit links removing no mask arms and not throwing.
- End-to-end `Cut` over both kart and water families and the original crossing links `0x14`, `0x41`, `0x50` and `0x05`, asserting that the cut's link bit goes before `ClearCell` and that the expected remaining link/mask results.

## Historical evidence (prior annotations; not executed here)
- Historical residual note: TRACK-X2LINK: the flow links of water crossings and tees are written as the original's line op does (0x539670: a tile with more than two arms ORs the line's direction into +0xD, so crossings get 0x14, 0x41, 0x50 or 0x05; the circuit walk 0x52A970 writes +0xD only at corners); not modelled: the clear op (ClearCell 0x53B280 -> 0x5227F0, which ANDs the line's direction bits out of +0xD) counts the overlap word (+0x20) down on every clear and, in its (-1, -1) form, clears the pair of the other axis' bits through 0x522730 (1 and 0x10 clear 4 and 0x40, 4 and 0x40 clear 1 and 0x10, by the link 0x522850), recursing on the extension cells (0xC, 0x11); OpenTPW removes the tile or counts the overlap down only, and the other writers of +0xD (the type-25 walk 0x52BA50 takes the first linked arm by rotation, the edit sub-modes 0x52B400) have no counterpart.
- Historical source inspection: `source/OpenTPW/World/Tracks/TrackPieceSelector.cs:135`: 136: if ( link == 0x14 ); 137: rotation = 90;; 138: else if ( link == 0x41 ); 139: piece += SecondFamilyOffset;
- The earlier `ClearCutAxisLink` premise and helper were withdrawn: the current implementation removes the perpendicular `Mask` pair in `ClearCell`, not perpendicular `Link` bits, and `Cut` removes the directional link bit before that call.

## Review boundary
- Stop at this characterization/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots or commits.
- Future implementation or oracle work needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
