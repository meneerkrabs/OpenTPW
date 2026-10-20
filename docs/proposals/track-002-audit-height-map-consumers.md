# TRACK-002: Audit height-map consumers

> Current findings up front. Evidence lives in the companion audit, [`track-002-public-consumer-audit.md`](track-002-public-consumer-audit.md).

## Status and classification

- Evidence-only. No runtime edit, no register or `APPROX` edit, no retirement, no parity or completion claim.
- Inspected snapshot: the supplied public clone head `cb625bfedb8bbcbf6d2e71e551a5772929700574`, independently verified against the clone HEAD.
- Nothing executed: no .NET SDK here. No test was run; no result is reported.
- **Supersedes** the plan-only draft written against `5ccddcab`. Its residual clause — "what those readers decide in OpenTPW's tour, ghost and bounding-box code is not traced" — was one undifferentiated item and is now split by owner; the piece and owner-object map lookup routes exist, while TOUR-005 retains fence-post/station and map-rebuild residuals.

## Current residual

1. **Kart, water and bumper vehicles do not read the `.hmp`** — implemented and correct (`TrackSurface` only; `TrackSurface.cs` has no `HmpFile` reference). *No residual.*
2. **Tour cars do read it** — implemented twice: `TrackRenderer.PieceGround` (`TrackRenderer.cs:275`) for piece tiles and `ObjectHeightMaps.Height` (`ObjectHeightMaps.cs:48`) for owned objects, tried in that order by `Level.TourObjectGround` (`Level.Tours.cs:23`). The draft's "not traced" is stale here.
3. **Build ghosts / ride preview, and the instance bounding box `0x466B70`** — no counterpart in the files reviewed. Recorded as an **open question**, not a missing implementation; no defect is claimed.
4. **Cell-flag copy `0x463060`** — likewise an open question. Zero flags in shipped files do **not** make the behaviour unprovable: synthetic non-zero flag fixtures plus the rule can settle it.
5. **Size check / rebuild `0x451880`** — not implemented, as `TOUR-005` already records. A mis-sized map is used as-is. This is a **semantic** gap: `ObjectHeightMaps.CellIndex` bounds-checks row and column and returns `null` outside the table, so the read is safe; bounds safety settles nothing about rebuild parity.
6. **Enforcement of loader `(5, 100)` and use of `Scale`** — the well-formed fixture already pins acceptance of 5 and 100; alternative valid sample counts/non-100 scales and rejection of such deviations are not characterized.

## Source inspection (current, public sources only)

| Item | Reference | State |
| --- | --- | --- |
| `.hmp` load, per stem, negative cache; a missing file makes **zero** `OpenRead` calls | `TrackPieceKit.cs:77`–`:98` | implemented |
| reader bounds: 1 MiB, header, version/magic, samples 1–16, cells 1–256, contiguous offsets, exact length, finite box | `HmpFile.cs:60`–`:113` | implemented |
| consumer index bounds-checked, `null` outside the map | `ObjectHeightMaps.cs:89` | implemented — withdraws the earlier "unguarded index" risk |
| vehicle probe closure reaches `TrackSurface` and the landscape only | `TrackRenderer.cs:100`, `:471`–`:513` | implemented, correct |
| piece-tile and owned-object ground for the tour cars | `TrackRenderer.cs:275`; `ObjectHeightMaps.cs:48`; `Level.Tours.cs:23` | implemented |
| no size comparison, no rebuild | `TourApproximations.cs:11` | open (residual 5) |
| `HmpFile` summary still says the consumers are not traced | `HmpFile.cs:10` vs `docs/TRACKS.md:80`–`:82` | stale text, out of authoring scope |

## Historical context (superseded, summarised)

The earlier plan-only draft (snapshot `5ccddcab`, no findings — its "source inspection" section was a mis-pasted source excerpt) stated the residual as: the `.hmp` is the original's per-object height map, loaded by `0x451640` from `0x4629D0` and rebuilt by `0x451880`; the vehicles' sampler does not read it and neither do OpenTPW's vehicles; the other readers of the map at `+0xCC` are the cell-height lookup `0x452AE0`, the bounding box `0x466B70`, the ride preview `0x4689F0` and the `0x463060` flag copy, and what they decide in OpenTPW's tour, ghost and bounding-box code is untraced. Its bounded work was an evidence-only audit with no invented vehicle implementation; its acceptance cases asked for a malformed/missing-map fixture identifying the bounding-box and preview fallbacks separately, tour tests under `TOUR-005` and ghost elevation under `BLUEPRINT-001`, a reproducible fixture naming input, expected output and function, and a re-run of the owner's regression tests with the remainder documented. These historical cases remain evidence gates, with existing piece-route coverage distinguished from untraced preview/bounds behavior.

## Bounded work

- Preserve what exists: the vehicle sampler's map-freedom and existing tour map lookup routes must not be rewritten by this audit. TOUR-005 fence-post/station and rebuild residuals remain open.
- Oracle first: establish the original's rule and a showing fixture before filing anything for residual 5 or 6.
- Do not invent a vehicle profile implementation; the register and the code agree.
- Register wording is the register owner's call. No narrowing, no retirement here.

## Remaining gates

1. Size/rebuild semantics (residual 5) — evidence plus a decision.
2. Enforcement of `(5, 100)` and runtime use of `Scale`; acceptance of that pair is already tested in source.
3. Preview / ghost / bounds / flag-copy counterparts — open questions, not defects.
4. `TrackAssetTests` is inconclusive without `OPENTPW_GAME_PATH`; execution requires an authorized environment with matching game data.

## Review boundary

- Stop at a reviewable evidence package: no tag removal, no register or `APPROX` retirement, no runtime edit, no parity claim.
- No game data, binaries, credentials or accounts were read or produced; this is public-source text only.
- Nothing here is merge-ready. A future implementation needs its own reviewed evidence and acceptance results.
