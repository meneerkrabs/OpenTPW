# TRACK-002: public consumer audit of the `.hmp` height profile

## Status

- **Evidence-only audit.** No runtime edit, no register or `APPROX` edit, no retirement, no parity or completion claim.
- **Snapshot:** the supplied public clone head `cb625bfedb8bbcbf6d2e71e551a5772929700574` (independently verified against the clone HEAD).
- **Nothing was executed.** No .NET SDK is present. Every test statement below is a *reading of test source*, not a result.
- **Scope:** public-source findings, the repository's own prior annotations, the current tests, and the remaining gates. A separate binary-excerpt review is deliberately **not** folded in here.

## 1. Load path (implemented)

`TrackPieceKit.GetHeightProfile` (`source/OpenTPW/World/Tracks/TrackPieceKit.cs:77`) is the only `.hmp` load site for pieces: `StemOf` (`:48`) → the kit's archive snapshot (`:29`) → `OpenRead` (`:88`) → `new HmpFile` inside a `using` → `InvalidDataException`/`IOException` become `null` (`:91`) → cached per stem including the negative result (`:96`). A **missing file performs zero `OpenRead` calls** (the `TryGetValue` at `:84` guards the open). `ObjectHeightMaps.MapOf` (`source/OpenTPW/World/Objects/ObjectHeightMaps.cs:17`) has the same shape for ride models.

Reader bounds (`source/OpenTPW.Files/Public/HmpFile.cs:60`–`:113`): 1 MiB cap (`:100`), header presence (`:64`), version+magic (`:67`), samples 1–16 (`:73`), cells 1–256 (`:75`), contiguous section offsets (`:83`), exact length (`:85`), finite box (`:91`), bounds-checked accessors (`:45`, `:53`).

**Prior annotation (`TOUR-005`, `source/OpenTPW/World/Tours/TourApproximations.cs:11`):** the map of a piece "is never rebuilt here either, its size is not compared with the original's expected size".

## 2. Consumers

| Original reader | Owner | OpenTPW counterpart | Status |
| --- | --- | --- | --- |
| `0x4511A0` vehicle sampler | kart / water / bumper vehicles | `TrackSurface` via `TrackRenderer.SurfaceHeight` | implemented; correctly map-free |
| `0x452AE0` tour callers | tour cars | `TrackRenderer.PieceGround` ← `Level.TourObjectGround` | implemented |
| `0x452AE0` owner route | tour cars over an owned object | `ObjectHeightMaps.Height` | implemented |
| `0x45CDC0`, `0x532FC0`, `0x536CA0`, `0x5370E0`; `0x4689F0` | build ghosts / ride preview | none in the files reviewed | open question |
| `0x466B70` | instance bounding box | none in the files reviewed | open question |
| `0x463060` flag copy | landscape flags | none in the files reviewed | open question, settle-able |

### 2.1 Vehicles read no map (implemented, correct)

`Level.Tracks.cs:379` → `TrackRenderer.cs:121` → `:299` → the `probe` at `:100` → `SurfaceHeight` (`:471`), which resolves to `TrackSurface` faces (`:504`–`:511`), the ride object (`:516`), the add-on delegate (`:489`) or the landscape (`Terrain`, `:476`; `TileHeight`, `:251`). `TrackSurface.cs` contains no reference to `HmpFile`. This matches the register (`TrackApproximations.cs:12`); the vehicle side needs no work and must not be re-opened. Note `TileHeight` is `0x466D70`'s landscape midrange, **not** the `0x466B70` map-extremes reader.

### 2.2 Tour cars — the implemented consumer

`TrackRenderer.PieceGround` (`TrackRenderer.cs:275`–`:290`) resolves an extension cell to its origin, requires an origin tile of this family with a piece and no add-on flag, then returns `map.CellMaxima.Span[index] * ObjectHeightMaps.ByteScale + TileHeight(...)/ModelScale`. `Level.TourObjectGround` (`Level.Tours.cs:23`–`:34`) tries the piece branch for every non-bumper family first, then the owner route — the precedence `TOUR-005` records.

### 2.3 Owner-object route

`ObjectHeightMaps.Height` (`:48`–`:57`) skips deleted and fixed items, takes `<mesh>.hmp` from the ride archive, indexes through the same `CellIndex`, and returns the byte scaled by `ByteScale = 0.39215687f` (`:12`, 100/255) plus the base height.

### 2.4 Preview, ghosts, bounds, flag copy

No counterpart appears in the files reviewed. **That is an open question, not a recorded missing implementation**: nothing here asserts OpenTPW needs one, and no defect is claimed.

## 3. Bounds and semantics

- **Consumer bounds are safe.** `CellIndex` (`ObjectHeightMaps.cs:72`–`:90`) turns the offsets by the instance rotation, truncates the tenths, and returns `mapRow * map.Width + mapColumn` **only when** `mapRow ∈ [0, Height)` and `mapColumn ∈ [0, Width)`, else `null` (`:89`). `PieceGround` propagates that `null`, so the `CellMaxima.Span[index]` read is in range. My earlier "unguarded index" risk finding is **withdrawn**.
- **What remains is semantic, not safety.** No size comparison and no mesh rebuild exist, so a mis-sized map is used as-is where the original would rebuild. Bounds safety says nothing about that parity.
- **Missing file ≠ one failed read:** zero `OpenRead` calls, as above.

## 4. Fields actually read at runtime

Only `CellMaxima` (and `Width`/`Height` via `CellIndex`). `Profile`, `Sample`, `CellMaximum`, `CellFlags`, `BoundingBox` and `Scale` are parsed and pinned by tests but have no runtime consumer in the files reviewed. `Scale` is not used even for scaling; `ByteScale` is a constant.

## 5. Current tests (read, not executed)

- `HmpFileTests` — header/section exposure, row-major sampling, both accessor range checks, and malformed inputs. It pins **0 and 17** samples as rejected (`data[4]=0`, `data[4]=17`); the well-formed fixture pins accepted `SamplesPerCell = 5` and `Scale = 100`. Alternative valid counts and non-100 scales are not characterized; rejection of deviations from `(5, 100)` is not tested.
- `TrackSurfaceTests` — the plain-triangle surface and the four-probe boat pose.
- `TourRegisterRoundTests` (asset-free) — `TheMapIndexFollowsThePlacementsRotation` (every map cell on the right world cell for all four rotations, plus outside cells → `null`), `ATrackTilesFourCellsAreFourMapCellsAtEveryRotation`, `ATrackPiecesInstanceSitsTwoCellsOverByTheEngineRotation`, `AnObjectsGroundIsItsMapByteScaledToModelUnits` (255 → 100 units; terrain fallback). **`CellIndex` bounds and scaling are covered.**
- `TrackAssetTests` (inconclusive without `OPENTPW_GAME_PATH`) — `ATrackTilesCellsGiveTheirPieceMapByteOnTheInstanceHeight` (a **nonuniform** maxima piece; the four tile cells give the four scaled bytes; the cell beside the tile → `null`; an add-on-flagged tile → `null`), `ATrackTilesPieceMapTurnsWithItsRotationAndStationsAndOtherFamiliesGiveNothing` (four rotations turn the map; station `Piece = 0` → `null`; other family → `null`), `EveryShippedPieceMapHoldsItsTilesFourCellsAtEveryRotation`, `TheFourWaterRidesAndFourKartRidesHaveCompletePieceKits` (every piece profile 2 × 2 of 5 samples, flags all zero, station 4 × 4), `EveryShippedHeightProfileIsAWellFormedHmp`. **The consumer is covered, not untested.**

## 6. Remaining gates

1. Semantic size/rebuild parity (§3) — needs a decision plus evidence, not a bounds fix.
2. Enforcement of the `(5, 100)` loader pair and runtime use of `Scale` remain open; acceptance of `(5, 100)` is already pinned.
3. Preview / ghost / bounding-box / flag-copy counterparts — open questions (§2.4).
4. Asset tests are inconclusive without game data; execution requires an authorized environment with the matching game data.

## 7. Conclusion

**Implemented:** the piece load and its bounds; the vehicle sampler's freedom from the map; the two described tour map lookup routes. TOUR-005 still records fence-post/station and map-rebuild residuals; overall tour parity is not established.
**Open:** the items in §6, none of which this audit turns into code.
**Not claimed:** any test result, any parity, any missing implementation.
