# COAST-R: roller coaster evidence and the COAST-I implementation plan

Lane: COAST-R (research). Goal: give COAST-I what it needs so a player can build, run, stop,
rate and delete roller coasters as in the original. This document consolidates earlier
findings, adds new bounded traces of the Feral Mac `SimThemePark.data` (SHA-256
`04809cd4…e295f5`) and of the PC `Data` catalog, and specifies COAST-I.

Reproduce (private assets stay outside Git):

```sh
python3 -I tools/ppc-analysis/lanes/coast/coast_evidence.py /path/to/mac-feral/bin --pc-data /path/to/Data
python3 -I tools/ppc-analysis/run_evidence_checks.py --mac-bin /path/to/mac-feral/bin --pc-data /path/to/Data
```

COAST-R2 (sections 12–19, with 9.7, 10.1 and 11.1) adds the curve fit, sampling, banking, build
checks, closure, money and motion evidence. Section 19 corrects four COAST-R statements.

Confidence follows the PPC-rides convention. **High** means an identity-pinned instruction,
constant or relocation that was interpreted. **Medium** means a role inferred from callers,
names or data. **Unresolved** means the path was not recovered. "Bounded" marks a claim that
holds only for the enumerated sites, for example "no other TOC reference", which does not
exclude access through a computed pointer. All results are static. Nothing original was
executed. Addresses are code-section offsets unless written `data:`. The TOC is `data:0x8000`,
so `-13456(r2)` is `data:0x4b70`.

## 1. Headline: TPW coasters are spline tracks, not piece kits

The track has no catalog of pieces (no straight, curve, loop or lift pieces). The player places
a chain of **pylons** (support nodes) on map cells. The game fits a smooth curve through them,
samples it into 56-byte records and extrudes per-coaster **cross-sections** for the visible
track. Lifts and brakes are not placed. The rating pass marks **winch** (chain-lift) sections
wherever the simulated speed falls to the winch floor (section 6.2). Loops come from a second
pylon kind, the **LoopPylon**, which has wider turn and rotation limits.

Three `Bumper.WhichTrackType` values exist (Rides.sam comment: `0=no track, 1=car track,
2=water track`). Code `0xe2fe4` tests `type record +156 == 3` for "is a coaster" (high).

| Type | Rides (PC baseline) | Track system |
| --- | --- | --- |
| 3 | 12 coasters, including the 3 water flumes | This spline system (this document) |
| 1 | Go-karts: Dino Karts, CryptKarts, Space Racers, Bumble Buggies | Cell track (`KartTrackCell`), not traced here |
| 2 | Water rides: Splish Splash, Ooze Crooz, The Blobulator, Taptastic Rapids | Cell track (`WaterTrackCell`), not traced here |

TPW has no monorail. Karts and water rides do **not** share the coaster track system. They use
cell tracks and the BUMP controller (PPC-rides). They are out of scope for COAST-I.

## 2. What was already proved (consolidated)

| Source | Claim | Status after this lane |
| --- | --- | --- |
| PPC-rides, schema | Coaster settings descriptor table `data:0x2ef24`, 384 × 60 bytes, walked by constructor `0x16f4c`; motion fields' settings→definition offsets | Kept and extended to the build, cost and rating fields (section 3) |
| PPC-rides, motion | Tick `0x40650` (callback chain `0x1c262c→0x3864c→0x38710`), 56-byte samples at controller `+156`, height acceleration with uphill/downhill modifiers, winch bit `0x800`, friction on sample flag 8 | Kept. The rating pass `0x3fc18` uses the same speed model and is where the winch bit is **set** (section 6.2) |
| PPC-rides, controllers | COAST opcode 55 bridge `0xb6df4`, commands 1..8; admission ring, MAX/MIN capacity rule, random car choice `s·214013+2531011` | Kept. Boarding is unchanged by this lane |
| PPC-rides, seats | Seats from MD2 attribute flag `0x80`; front/centre/rear car roles; per-train sockets × `uiMaxTrains` | Kept |
| PPC-rides, save | Coaster body between `EMAK` and `SAOC`; 32-byte header, 34-byte section records, ordinal-2 auxiliary block, link ordinals | Kept. Header `+28` (controller `+320`) is now identified as **riders on board** (section 6.4) |
| DET-plan | Coaster motion is frame-coupled (elapsed ms of the cached clock); DET-I uses 31 ms per frame | Kept. COAST-I must use the DET-I frame delta |
| QUEUE-plan | Track types 2 and 3 bypass the capacity test; coaster cycle time out of scope | Kept. Coaster admission goes through the COAST controller rings |
| RIDE-WEAR | Wear and breakdown are generic per ride (`Upgrades[k].WearRate`) | Kept. Coaster SAMs carry `WearRate`, so coasters wear and break down like other rides |

## 3. Data model

### 3.1 Settings → definition (high)

The coaster definition is built by `0x302cc`: constructor `0x16f4c` parses `coaster.sam` into the
settings record, `0x2d274` fills definition **defaults**, then loader `0x2fde8` (with
`0x2f7dc` for pylons and `0x2fbfc` for car types) copies settings over the defaults. Almost
every float is copied **only when nonzero**, so an absent or zero SAM value keeps the default.
Exceptions: the three pylon cost rates are copied unconditionally (a missing rate is 0), and
`fEntranceSizeInterval` is copied unconditionally.

None of the motion, place-distance, angle or rating keys below occurs in any PC `.sam` or WAD
member (full scan of `Data`). The PC game therefore runs on these code defaults.

| Settings key | Settings offset | Definition / pylon offset | Default (code) |
| --- | ---: | --- | --- |
| fCurviness / fStraightDivisor | 15052 / 15056 | +8 / +12 | 1.25 / 1.25 |
| fAccelerationPerHeight | 15064 | +36 | 1.5 |
| fUphill / fDownhillAccelModifier | 15068 / 15072 | +40 / +44 | 1.0 / 1.0 |
| fTrackHeightAdjust | 15112 | +64 | 0.0 |
| fWinchSpeed / fMinSpeed | 15116 / 15120 | +68 / +72 | 5.0 / 5.0 |
| fMaxSpeedAtMin / AtMaxSetting | 15124 / 15128 | +76 / +80 | 80 / 130 |
| fFrictionMultiplier | 15132 | +84 | 0.8 |
| fBendElevationLow / High | 15136 / 15140 | +88 / +92 | 0 / 0 |
| fGravityX / Y / Z | 15144..15152 | +96 / +100 / +104 | 0 / −0.3 / 0 |
| fForceMultiplier | 15156 | +108 | 0.2 |
| fInitialPylonLoftHeight | 15076 | +164 | 10.0 |
| uiMaxTrains / uiTrainReleaseRate | 15080 / 15084 | +744 / +748 | 1 / 15000 ms |
| fInitialSickness / Excitement / Uncomfortableness | 15160..15168 | +800 / +804 / +808 | 0 / 1.0 / 0 |
| fGForceSicknessModifier / fUpsideDownSicknessModifier | 15172 / 15176 | +812 / +816 | 0.5 / 3.0 |
| fSpeed / GForce / UpsideDown / Bank ExcitementModifier | 15180..15192 | +820 / +824 / +828 / +832 | 0.1 / 1.0 / 1.0 / 5.0 |
| fXGForce / Inverted UncomfortableModifier | 15196 / 15200 | +836 / +840 | 1.0 / 2.0 |
| fNormal / Terror ScreamStartExciteDelta | 15204 / 15208 | +844 / +848 | 0.5 / 1.0 |
| fNormal / Terror ScreamSustainExcitement | 15212 / 15216 | +852 / +856 | 10 / 20 |

Flag bits written by the loader (high for the bit, medium for the name): `bSpecialIsTrough`
sets definition `+0 |= 0x100` and turns pylon 1 from a loop pylon (`0x4000`) into a trough
pylon (`0x8000`); the loader always sets `0xA0` (`0x301f4`); `bVisibleEndPylon` sets `0x20`;
`bNoAutoBanking == 0` sets `0x10`.

### 3.2 Pylon controls (high)

`asPylonControls[0..1]` (settings stride 188) become two 288-byte pylon controls at definition
`+168` (**StdPylon**) and `+456` (**LoopPylon**). Angles are given in degrees and multiplied by
`single(π/180)`. Place distances are whole cells. The loader stores both the square (`+156`,
`+160`, integer) and the float (`+164`, `+168`).

| Field (pylon offset) | StdPylon default | LoopPylon default |
| --- | --- | --- |
| flags (+0) | `0x4` | `0x4004` (loop) |
| uiMinPlaceDist / uiMaxPlaceDist (cells) | 3 / 8 | 3 / 9 |
| fEntranceMin/MaxXZAngle at min distance (+116/+120) | 140° / 180° | 120° / 174° |
| fEntranceMin/MaxXZAngle at max distance (+124/+128) | 110° / 180° | 120° / 174° |
| fMin/MaxXZAngle at min distance (+132/+136) | 80° / 180° | 0° / 174° |
| fMin/MaxXZAngle at max distance (+140/+144) | 50° / 180° | 0° / 174° |
| fMaxRotnAngleDeviance / fMaxLoopRotnAngleDeviance (+148/+152) | 45° / 0° | 90° / 90° |
| fMaxLoopHeightDifference (+172) | 0 | 25 |
| fMinHeight / fMaxHeight (+104/+108) | 10 / 10 | 10 / 10 |
| uiMaxStackedPylons (+204) | 1 | 1 |
| fCollisionPossibleRadius (+88) | 5 | 5 |
| fPylonStraightDotThreshold (+176) | −0.999 | −0.8 |
| fCostPerUnit / 2DLength / Height (+92/+96/+100) | from SAM | from SAM |
| mesh height at t=0 / t=1 (+188/+192) | from the pylon MD2 loft animation | same |

Every PC coaster overrides `fMinHeight 10`, `fMaxHeight 100`, `uiMaxStackedPylons` 3 or 4,
`fCollisionPossibleRadius 5`, and the cost rates (section 4.4). The loader applies
`fMaxLoopHeightDifference × 10 × π/180` (high for the operations; the unit reason is unknown).

### 3.3 Runtime track (medium unless stated)

| Object | Fields used by this lane |
| --- | --- |
| Controller (per built coaster) | `+4` definition; `+52` speed cap; `+60` flags (bit 0 track complete, bit 1 set on load from header flag 2); `+84` section list head (`+100` next, `+104` previous); `+88` last node; `+156/+164` samples / count; `+168`, `+172` sample count (float) and spacing; `+256` sickness; `+260` excitement; `+264` counted sections; `+268` length; `+272` max sample height; `+276` max speed; `+320` riders on board (high: written by passenger removal `0x3d6f8..0x3d85c`) |
| Node / section | `+0` ordinal; `+8` flags (`0x40` loop node, `0x800` winch, `0x10000` and `0x20000` used by the validator); `+76/+84` world X/Z; `+100/+104` next/previous; `+232` height parameter t; `+256` descriptor |
| Descriptor | `+4` flags (bit 0 station end, bit 1 entrance node); `+12/+16` cell X/Y; `+20` owning coaster; `+24` pylon control; `+44` pylon below; `+48` stack height; `+52` cell object |
| Sample (56 bytes) | `+0` flags (2 terror scream, 4 normal scream, 8 friction); `+4/+8/+12` XYZ; `+16/+20` direction terms; `+24` bank (rad); `+28` speed; `+32..+40` force vector; `+44` section |

The 12 PC coasters (`Data/levels/*/rides/*.wad`, from `track_catalog`):

| Id | Theme | Name | Price | Pylons | Max trains | Release ms | Cars/train |
| ---: | --- | --- | ---: | --- | ---: | ---: | ---: |
| 1180 | jungle | Temple Of Gloom | 10000 | Std, Loop | 6 | 30000 | 4 |
| 1185 | jungle | Chac Atak (flume) | 10000 | Std | 20 | 16000 | 1 |
| 1190 | jungle | Gorilla Thrilla | 12500 | Std, Loop | 15 | 15000 | 4 |
| 2180 | hallow | Ghosta Coasta | 10000 | Std, Loop | 10 | 30000 | 4 |
| 2185 | hallow | Hades (flume) | 10000 | Std | 20 | 15000 | 1 |
| 2190 | hallow | Scatty Batty | 12500 | Std, Loop | 8 | 31000 | 6 |
| 3180 | space | Shocker (suspended) | 12500 | Std, Loop | 20 | 30000 | 3 |
| 3185 | space | Moonshot | 10000 | Std, Loop | 30 | 8000 | 1 |
| 3190 | space | Escape Velocity | 12500 | Std, Loop | 15 | 30000 | 4 |
| 4180 | fantasy | Big Dripper (flume) | 10000 | Std | 30 | 7000 | 1 |
| 4185 | fantasy | Caterpillar Coaster | 10000 | Std, Loop | 7 | 30000 | 6 |
| 4190 | fantasy | Candy Coaster | 12500 | Std, Loop | 10 | 24000 | 3 |

Price is `Upgrades[0].CostOfUpgrade` of the ride `.sam` (the station). The three flumes set
`bSpecialIsTrough` and override acceleration and friction (PPC-rides lists the values).

## 4. Construction

### 4.1 Placement validator `0x36ba4` (high for each test, medium for the overall order)

Inputs: cell `(x, y)`, the node being moved (null for a new pylon) and optional neighbour cells.
Order of tests:

1. **Map bounds**: `x < map width (+24)` and `y < map height (+28)`, otherwise reject.
2. **Blocked cell** (new pylon only): bit 0 of the per-cell halfword (`map +44`, 4-byte stride)
   rejects. What sets that bit (paths, objects, water, other rides) is unresolved.
3. **Pylon control**: the LoopPylon applies after a node with flag `0x40` or while the loop
   edit mode bit `0x40` is set; otherwise the StdPylon.
4. **Same cell** as the previous node rejects.
5. **Distance window**: `d² = dx² + dy²` in cells must lie in `[min², max²]`. Outside it, the
   node is rejected unless the previous node's descriptor has flag 2 and its ordinal is not 2
   (a waiver whose purpose is unresolved).
6. **Turn window**: `θ = acos(clamp(dot(û_new, û_before), −1, 1))` at the previous node
   (`0x36a50`; π means straight on). With `t = clamp((√d² − min)/(max − min), 0, 1)`, the
   limits are `lo = A·(1−t) + B·t` and `hi = C·(1−t) + D·t`. The **entrance** angles (+116..+128)
   apply when the previous node is the station node (descriptor flag 2), otherwise the normal
   angles (+132..+144). The node is rejected if `|θ| < lo` or `|θ| > hi`.
7. **Cell already holds a pylon**: it must belong to the same coaster, use the same pylon
   control, have stack height `< uiMaxStackedPylons`, and have no object flags `0x600`.
   Otherwise reject.
8. **Moving a node** also re-validates the following node and the node after it by calling
   `0x36ba4` again with the moved position (`0x37318`, `0x373b4`).

Height is **clamped, not rejected**: the builder `0x34d90` clamps the requested height to
`[fMinHeight, fMaxHeight]` (+104/+108), then stores `t = clamp((h − h0)/(h1 − h0), 0, 1)`
(`0x36990`) in node `+232`. Here `h0, h1` are the pylon mesh heights at both ends of its loft
animation, and `fMinHeight` is first raised to `h0` by `0x30f20..0x30f44`.

Not traced (stay APPROX): `fMaxHeightDiffPerGSquareDist` (slope), `fMaxRotnAngleDeviance` and
the loop deviances (read by `0x94474`, `0xb35c8` and others, outside the coaster module), the
3D curve fit (`fCurviness`, `fStraightDivisor`), collision with terrain and scenery
(`asTrackCollisionPoints`, `fCollisionPossibleRadius`), and the string "Duplicate track
section - IGNORED!".

### 4.2 Build flow (medium)

- **Start**: the station is an ordinary ride object (`Info.Shape` with `<` `>` ends, PPC-rides).
  The first track node is the station node (descriptor flag 2). It cannot be picked up
  (`0x4a228`).
- **Extend / move**: the tool keeps a current node; `0x483ac` (per frame, with the build cursor)
  validates the cursor cell with `0x36ba4` and charges cost deltas (4.4). `0x4a1fc` picks up an
  existing node, plays sound 60 and records the costs of that node and the next one.
- **Complete**: `0x37ef0` (called from the ride UI `0x70b98`) sets controller `+60 |= 1`.
  **Reopen** for editing: `0x37e8c` removes nodes back to the station end and clears bit 0.
- `0x45da4` holds when bit 0 is set and bit 1 clear. `0x45eac` adds riders `+320 == 0` and
  "not the coaster being edited". The guest and queue code calls `0x45eac` (`0xdc834`,
  `0xde654`, `0xed244` and others); what its callers do with it is unresolved.
- Auto-completion and loop closure: not recovered. The completion call exists. How the game
  joins the last node back to the station is unresolved.

### 4.3 Undo and delete (unresolved)

No undo stack was found. Moving uses the pick-up/re-place path above. Deleting a whole coaster
and its refund were not traced (`0x4a43c`, `0x4a7e4`, `0x4a954` are the remaining UI-called
entries with cost calls).

### 4.4 Cost (high)

Per node, `0x4b168`:

```
cost = [CostPerUnit if the unit is charged]
     + CostPer2DLength × (horizontal distance to the previous node / 10)
     + CostPerHeight   × (pylon height / 10)
```

Height is `h0 + t·(h1 − h0)` (`0x369bc`). Distances are world units (÷ 10.0, constant
`-13300(r2)`). The unit term is the node's own pylon's and is added only when the caller passes
`r4 = 1`: the place path does (`0x37864`); pick-up, drop and the height slider pass 0 (COAST-V
CV-1). When the node's pylon is a loop or trough pylon (`0xC000`), the length and height rates
fall back to the StdPylon when no pylon stands below it or the one below has no `0xC000`
(`0x4b294..0x4b2b4`); a loop pylon (`0x4000`) also falls back when the previous node lacks flag
0x80 (`0x4b2b8..0x4b2d4`). The 2D length is `sqrt(fmadd(dx, dx, dz·dz))` in double. Results are
rounded **up** by `0x41aa4` (`modf`; any fraction adds 1). `0x483ac` adds
`ceil(new) − ceil(old)` to the build-cost accumulator `data:0x84ac4` via `0x7bcb8`. A negative
delta takes a separate arm that adds `−0x0EEEEEEE` as well; its meaning is unresolved. PC
rates: StdPylon 40 / 10 / 10, LoopPylon 60 / 15 / 15 for every coaster.

When the accumulated cost is charged to the park, and whether a deleted coaster refunds
anything, is unresolved (APPROX).

## 5. Running

### 5.1 Trains (high)

After the rating pass, when controller flag `0x1000` is set and `0x4000` is clear
(`0x40458..0x40524`; `0x1000` is then cleared):

```
trains = min(uiMaxTrains, max(1, trunc(lapTime_s / (uiTrainReleaseRate / 1000))))
```

`lapTime_s` is the sum of `spacing / speed` over the samples of the rating pass. Then
`0x3e9ec` allocates the trains and `0x3f930` spaces them along the track. `uiTrainReleaseRate`
is therefore the minimum headway between trains in milliseconds. The fleet seat total is
`sockets per train × uiMaxTrains` (PPC-rides).

### 5.2 Motion

Use the PPC-rides tick `0x40650` model: elapsed ms of the cached clock (DET-I: 31 ms per
frame), height acceleration `speed' = mod·(PerHeight·(y − y_next)) + speed`, a floor at the
winch speed on winch sections and at `fMinSpeed` otherwise (PPC-rides; exact floor gates are
unresolved), friction on flag-8 samples, cap `ctl+52`, interpolation between samples.
`ctl+52` comes from the ride speed setting between `fMaxSpeedAtMinSetting` and
`fMaxSpeedAtMaxSetting` (medium; the setter `0x3de68` is called from COAST 8 and from `0xb2838`).

### 5.3 Boarding, start and stop

Boarding, departure and capacity follow the COAST controller (PPC-rides: commands 1..8,
admission ring, `min(min(max(request, global), definition), train)`, random car choice with
`CoasterRng`). Guests queue as in QUEUE-plan, but track type 3 bypasses the generic capacity
test. Open/close is COAST 5 (`0x3dec8`), and COAST 4 maps states 0, 1, 2 to controller flags
16, **64, 32** (`0x3de0c..0x3de54`; COAST-V CV-2 corrected the earlier 16, 32, 64). Stopping a
running coaster lets trains finish to the station (medium; not traced). Breakdowns use the
generic wear path (RIDE-WEAR). A scripted breakdown (COAST 4 1) is mode 64: the power factor
drops to 0 and moving trains coast to a stop (17.4).

### 5.4 Riders on board (high)

Controller `+320` counts riders. Passenger removal subtracts the cleared slots
(`0x3d6f8..0x3d85c`), and the save header writes it at offset 28. The predicate
`0x45eac` and the predicate `0x45e58` (`+320 == 0`) read it.

## 6. Rating

### 6.1 Ride excitement for guests (high)

`0xdff70`, for a ride with track type 3: if the script has no coaster controller (`0xb5e24`
returns 0), excitement is 0; otherwise `trunc(0.5 · E + 50)`, where `E` is the coaster's
excitement rating in `[0, 100]` (getter `0x41a14`). A coaster thus scores 50..100, while other
rides use `UsageInfo.ExcitementLevel`. The coaster's own `UsageInfo.ExcitementLevel` (90 in
Chac Atak) is not used on this path. Sickness has no guest reader in this lane (unresolved).

### 6.2 Rating pass `0x3fc18` (high for the arithmetic)

Runs over samples `i = 1 .. n−2` after the geometry is rebuilt:

1. Start speed `v = fWinchSpeed`; `t_lap = 0`.
2. On trough coasters (definition flag `0x100`), sample flag 8 is set where the next segment
   climbs more than `0.01745` (unit Y of the normalised direction) and cleared elsewhere.
3. `a = ((p_(i−1) − p_i) + (p_(i+1) − p_i)) / spacing · (−g)`, with `g = data:0x3d140` (1.0 in
   the image): both differences are neighbour − current (`0x3fcf0`, `0x3fcf8`; COAST-V CV-16).
4. `v = max(mod·(PerHeight·(y_i − y_next)) + v, winch)` (`mod` uphill +40 / downhill +44),
   `× friction` on flag-8 samples, `max(·, winch)` again. If `v == winch`, the section gets
   flag `0x800` (**chain lift**). Then `v = min(v, ctl+52)`.
5. Sample `+28 = v`; `+32..+40 = a · (v / 10)` (`0x3ff60`).
6. Rating step (6.3) with `(i, v, bank = sample+24, F = sample+32)`.
7. Track the maximum speed and the maximum sample Y (both start at 0.0); `t_lap += spacing / v`.
   After the loop sample 0 gets `v = fWinchSpeed` and force 0, and sample `n−1` the speed and
   force of sample `n−2` (`0x4001c..0x4006c`).
8. After the loop: auto-banking (when definition flag `0x8` is set; medium), then final (6.4)
   with `(max Y, max speed)`, then the train count (5.1).

### 6.3 Step `0x415b4` (high)

With `|F|₁ = |Fx| + |Fy| + |Fz|`, `U = max(|bank| − π/2, 0)`, `u = U + (U ≠ 0 ? 1 : 0)`:

```
dS = GForceSickness·|F|₁ + UpsideDownSickness·u
dE = Speed·v + GForceExcitement·|F|₁ + BankExcitement·|bank|
dU = XGForceUncomfortable·|Fx| + Inverted   with Inverted = 0 unless u ≠ 0 and Fy < 0.1:
     Inverted = InvertedUncomfortable·u, tripled when Fy < 0
S  = 0.5·S + dS
E  = 0.5·E + max(dE − dU, 0)
peakS = max(peakS, S); peakE = max(peakE, E)
```

All values are singles. The "previous force" vector (`data:0x57784`) and "previous bank"
(`data:0x57780`) are cleared at reset (`0x41510`) and never written afterwards (bounded: only
TOC-slot references were searched). The formula therefore uses the current magnitudes, not
changes. `fUpsideDownExcitementModifier` (+828) and `fInitialUncomfortableness` (+808) are read
by no traced code. When `u ≠ 0` is false or `Fy ≥ 0.1`, the code adds a stale `f0` register
value to `dU`. COAST-I uses 0 there (APPROX).

**Screams** (`i > 2`): with `rise = E − E_previous`, a normal scream (sample flag 4) starts
when `rise > NormalScreamStartExciteDelta` and continues while `E > NormalScreamSustain`. A
terror scream (flag 2) uses the terror pair. The flags are precomputed per sample. Playback
goes through the sound lane (`0x3bb6c`).

### 6.4 Final `0x41900` (high)

```
sickness   = clamp(S + peakS, 0, 100)              → controller +256
excitement = clamp(0.5·E + 1.7·peakE, 0, 100)      → controller +260
```

Also stored: `+264` count of sections without flags `0x40` and `0x80`; `+268 = +168 × +172`
(track length, medium); `+272` max sample Y (medium: candidate source of the
`CoasterHeight` golden ticket); `+276` max speed. Intensity in the RCT sense does not exist. TPW
has excitement and sickness, with uncomfortableness folded into excitement.

## 7. Ride camera (unresolved)

`Info.FirstPersonSprites` and the graphics option `FIRSTPERSONVIEWDISTANCE` (0/1/2 in
low/med/high.sam) exist. The camera entry was not traced. Leads: `0x138a1c` and `0x14df5c` call
the coaster predicates `0x45dfc`/`0x45e58`; the car pose is car XYZ `+72/+76/+80` (PPC-rides);
`COASTERSMOOTHNESS` (1/4/6) and `COASTERTRACKDETAIL` are graphics options read elsewhere. COAST-I
should ride the front car of a train: position from the interpolated sample, forward from the
sample direction, roll from sample `+24`, and label it APPROX.

## 8. Save fields

The original save layout is in PPC-rides ("Original TPW coaster save/load contract"). This lane
adds names: header `+28` is riders on board (`+320`); section record floats `+232` (height
parameter t), `+216/+220/+224`, `+248`; byte 12 is the cell-stack ordinal. COAST-I need not
write the original binary layout (no nonempty PC sample exists) but must save the fields in
9.6.

## 9. COAST-I implementation spec

### 9.1 Data

- `CoasterDefinition`: all fields of tables 3.1 and 3.2, built from `coaster.sam` over the code
  defaults with the nonzero-override rule (cost rates unconditional). Keep both pylon controls.
  Recognise a coaster by `Bumper.WhichTrackType == 3`.
- `CoasterTrack`: the ordered nodes `{cell x, y; height t; pylon kind; flags (loop, winch);
  stack index}`, the station node first, a `Complete` flag and `RidersOnBoard`.
- `CoasterSamples`: generated, 56-byte-equivalent records `{pos, dir, bank, speed, force,
  flags, section}` with a fixed spacing (APPROX value, see 10).
- `CoasterRating { Sickness, Excitement, MaxHeight, MaxSpeed, Length, LapTime }`.

### 9.2 Build tool (validation order and constants)

1. Map bounds; 2. blocked cell (map the existing path/object/water occupancy to "blocked",
APPROX); 3. pick the pylon (Loop after a loop node or in loop mode); 4. same cell; 5. distance
`d²` in `[min², max²]` (Std 9..64, Loop 9..81); 6. turn angle window with t-interpolation,
entrance limits after the station node; 7. stacking (same coaster, same pylon kind, `<
uiMaxStackedPylons`); 8. when moving, re-validate the next two links. Clamp height to
`[max(fMinHeight, h0), fMaxHeight]` and store `t`. Cost per node from 4.4, rounded up; show the
running build cost, and charge it when the player confirms. The original charging point is
APPROX. Station node cannot be moved. "Complete" closes the circuit back to the station
(closure geometry APPROX) and sets `Complete`. "Reopen" clears it.

### 9.3 Simulation step

- On complete: generate samples (APPROX curve), run the rating pass (6.2–6.4) once, mark winch
  sections, compute the lap time and the train count (5.1), place the trains evenly.
- Each frame with the DET-I delta: the PPC-rides motion step per train. Boarding and departure
  through the COAST controller and the queue.
- Stop/close: no new boarding; trains run to the station (APPROX).
- Breakdown: the generic wear model. A broken coaster stops dispatching (APPROX).

### 9.4 Rating

Exactly 6.3/6.4 in single precision (the witness `Rating` class is the reference), with the
stale-`f0` term set to 0. Ride excitement for guests `trunc(0.5·E + 50)`. Re-rate after every
completed edit.

### 9.5 Camera

The front-car ride view from 7, APPROX.

### 9.6 Save

Per coaster: definition id, node list (cell, t, pylon kind, flags, stack index), `Complete`,
ride settings (speed setting → `ctl+52`), trains (position along the track, speed, riders),
`RidersOnBoard`, rating values, and the `CoasterRng` state (DET-plan).

### 9.7 Spec deltas from COAST-R2 (supersede the APPROX choices above)

Evidence in sections 12–18. Each delta names the item it replaces.

1. **Curve (replaces COAST-001).** Knots are the node positions (node world position from 13.2).
   Each knot gets a unit direction `u` and the Hermite tangents `T± = ±u · fCurviness · chord±`,
   where `chord±` is the distance to the neighbouring knot with the height difference weighted
   by 0.7 (1.0 when there is no neighbour). Control points are `P ± T±/3`. Each knot pair is one
   cubic Bézier (`P_i`, `P_i + T+_i/3`, `P_{i+1} − T−_{i+1}/3`, `P_{i+1}`). On a pylon-less
   node, `u` is the normalised node forward tangent from 13.3. On a loop node it is `(0, ±1, 0)`.
   On a pylon it is the pylon mesh's track-tag axis, which COAST-I approximates with the node
   forward tangent (COAST-015).
   On loop pylons, use `K` from 13.4 instead of `fCurviness`, and 1.7 toward a non-loop neighbour.
2. **Sampling (replaces COAST-001).** Spacing is **3.0 world units**, a constant, independent of
   COASTERSMOOTHNESS. Walk each pair with `t += 0.05`, sum the chord lengths, and emit a sample on
   the current chord where the sum reaches 3.0. Then realign `t` to the next multiple of 0.05.
   Sample orientation: yaw `atan2(T.x, T.z)` and pitch from the Bézier derivative, roll from the
   second rail (13.7).
3. **Banking (replaces COAST-010).** Node bank (when the definition has flag 0x10, i.e.
   `bNoAutoBanking == 0` and not suspended):
   `bank = v · (±π − θ) / 4.0 / (|prev − P| + |next − P|)`. Here θ is the signed XZ turn angle
   (13.3), the sign of π follows θ, and `v` comes from the height-energy estimate along the nodes
   (13.8, floor fMinSpeed, start 0). The divisor is the constant 4.0: `fAutoBankDivisor` is never
   loaded. The bank reaches the samples through the pylon bank animation (COAST-015). COAST-I uses
   the node bank as the knot roll. Suspended coasters (`bIsSuspendedCoaster`) have no auto-bank;
   their roll is the swing pass (13.8), which COAST-I may keep APPROX.
4. **Build checks (replaces COAST-002, COAST-003).** Do not enforce slope or rotation deviance.
   Slope (`fMaxHeightDiffPerGSquareDist`), loop rotation deviance and loop height difference are
   loaded and never read by the coaster code. `fMaxRotnAngleDeviance` only bounds the manual
   rotate slider (yaw ± deviance, step 0.02 rad). The blocked-cell test is the level's
   **no-ground** cell bit, frozen at game start. Paths, queues, scenery and other rides do not
   block a pylon in the validator. Another coaster's pylon in the cell does (validator test 7).
   A failed validation does **not** refuse the edit: it marks the node and its predecessor with
   flag `0x4000` (shown as invalid). The bit clears when validation passes.
5. **Closure and completion (replaces COAST-005).** Placing a pylon on the cell that holds the
   open start node of the chain closes the circuit: no node is added, the two end nodes are
   linked, and "circuit open" clears. Sampling then wraps from the last knot to the first knot of
   the second node. Completion appends one station end node with flags 0x144 at the station mesh
   tags (the loader always sets definition bit 0x20; section 15), then rebuilds and rates. A coaster runs only when
   complete and closed.
6. **Money (replaces COAST-006).** When money is on: placing a pylon charges `trunc(cost)` at
   once, with fCostPerUnit included. Moving a node, or releasing the height slider, refunds
   `trunc` of the float cost stored at pick-up and charges `ceil` of the new cost, both without
   fCostPerUnit, for the node and, on a complete track, the next node. Nothing changes hands if the node returns to its own
   cell. Cancelling a drag restores the node at no cost. Deleting pylons refunds nothing. The
   build-cost display is `Σ ceil(new) − ceil(old)`; the coaster code never debits it (16).
   There is no undo stack.
7. **Motion (replaces COAST-009, part of COAST-012).** Per frame, with the DET-I delta: each
   train advances in hops of half a sample (13.6 positions, linear between samples). One hop:
   `v' = mod·(PerHeight·(y(s) − y(s+½))) + v`, averaged over cars. Apply friction on flag-8
   samples. The floor is `fWinchSpeed·P` on winch (0x800) sections, otherwise `fMinSpeed·P`,
   where `P` is the power factor (0 or 1). Then cap at `ctl+52`. A hop takes
   `d / ((v + v')/2)` seconds, where `d` is the reference car's distance to its half-sample-ahead
   point. The last hop of a frame is partial and interpolates the speed. After every hop,
   `v ×= 0.95` if `v < 2`, and `v = 0` if `v < 0.01`. Station braking (17.3), for trains without
   flag 0x40, blends toward the floor with `x⁴`.
8. **Stop (part of COAST-012).** Switching the ride to mode 64 (COAST 4 argument 1) sets `P = 0`.
   Both floors vanish and the trains coast and decay to a stop. Leaving mode 64 sets `P = 1`.
9. **Speed setting.** `ctl+52 = MaxAtMin·(1 − s/100) + MaxAtMax·(s/100)`, default `s = 60`
   (110 with the code defaults).

## 10. What stays unknown (APPROX register for COAST-I)

| Id | Unknown | COAST-I default |
| --- | --- | --- |
| COAST-001 | Curve fit and sample spacing (`fCurviness`, `fStraightDivisor`, `COASTERSMOOTHNESS`) | Catmull-Rom through the pylon tops, spacing 1 world unit |
| COAST-002 | Slope (`fMaxHeightDiffPerGSquareDist`) and rotation-deviance checks | Not enforced |
| COAST-003 | What sets the blocked-cell bit | Paths, queues, ride and scenery footprints, water block |
| COAST-004 | Terrain and scenery collision of the track | Not enforced |
| COAST-005 | Loop closure to the station and the "complete" geometry | Straight join if the last link passes the window, else refuse |
| COAST-006 | Charging point, negative-delta arm, delete refund | Charge on confirm; refund 0 on delete |
| COAST-007 | Distance waiver for flagged nodes | Not applied |
| COAST-008 | Stale `f0` in the discomfort term | 0 |
| COAST-009 | Speed floor gates (winch vs `fMinSpeed`) during motion | Winch floor on winch sections, `fMinSpeed` elsewhere |
| COAST-010 | Auto-bank formula details | Bank from the lateral force, ×`fForceMultiplier`, clamp ±π/2 |
| COAST-011 | Ride camera | Front car view |
| COAST-012 | Stop/close behaviour and breakdown effect on moving trains | Trains finish to the station |
| COAST-013 | Sickness consumer for guests | Not used |
| COAST-014 | Windows equivalence | All values are Mac-derived; PC data matches inputs only |

### 10.1 Register after COAST-R2

| Id | Status after COAST-R2 | COAST-I action |
| --- | --- | --- |
| COAST-001 | **Resolved** (13.3–13.7): Hermite/Bézier knots, chord × fCurviness tangents, 0.7 height weight, spacing 3.0, 0.05 walk | Replace the Catmull-Rom APPROX with 9.7 items 1–2; keep only COAST-015 as APPROX |
| COAST-002 | **Resolved, bounded**: slope, loop deviance and loop height difference are never read. Rotation deviance is the rotate slider range | Remove: do not enforce |
| COAST-003 | **Resolved**: level no-ground bit frozen at game start; other coasters' pylons block | Replace the occupancy APPROX. Residual UI checks under COAST-016 |
| COAST-004 | Unchanged (collision points not traced) | Keep APPROX |
| COAST-005 | **Resolved** (15) except the trigger of the completion click (COAST-016) | Replace with 9.7 item 5 |
| COAST-006 | **Resolved** (16) | Replace with 9.7 item 6 |
| COAST-007 | Unchanged | Keep |
| COAST-008 | Unchanged | Keep |
| COAST-009 | **Resolved** (17) | Replace with 9.7 item 7 |
| COAST-010 | **Resolved** (13.8). Correction: flag 0x8 is the suspended swing, not auto-banking | Replace with 9.7 item 3; swing stays APPROX (COAST-018) |
| COAST-011 | Unresolved (18) | Keep the front-car APPROX |
| COAST-012 | **Partly resolved**: stop = power factor 0 (17.4). Breakdown effect on moving trains not traced | Replace the stop part with 9.7 item 8 |
| COAST-013 | Unchanged | Keep |
| COAST-014 | Unchanged | Keep |
| COAST-015 | **New**: pylon mesh track tags (knot positions, axes, count per pylon) and the mapping from loft, rotate, incline and bank parameters to tag frames | Knot = node position, axis = node forward tangent, roll = node bank |
| COAST-016 | **New**: cell action 129 in the build click (`0x73a3c`) decides between completing and adding a pylon; its semantics (and any UI-level occupancy check) live in the generic tool engine `0x80c50` | Complete when the click is on the station's entry cell (APPROX) |
| COAST-017 | **New**: roll on pylon-less non-loop nodes (second rail offset `(−sin a, 0, −cos a)` from node +224) is not bounded | Roll 0 there |
| COAST-018 | **New**: suspended swing formula (13.8) located but not restated | Bank from lateral force (old COAST-010 APPROX), suspended coasters only |
| COAST-019 | **New**: closing-pair rule. The sampler stops after the first sample on the closing pair, and the final count may gain one | Emit samples on the closing pair up to the first knot of the second node |

## 11. Witness and tests

`tools/ppc-analysis/lanes/coast/coast_evidence.py` pins 21 code-range digests, 171
decoded D-form operands, 34 call targets, 5 import glues (`sqrt`, `acos`, `fmod`, `memset`,
`modf`), 8 fused multiply-add shapes, 8 mask shapes, 42 TOC constants, 13 relocation-checked
TOC pointers, 12 data constants and 25 settings descriptor offsets (shared walk from
`motion_evidence.py`). `track_catalog` reads the PC rides.

`test_coast_evidence.py` (28 cases): placement (distance, turn, entrance, map, same cell),
cost and rounding, height parameter, train count, speed and winch, rating (convergence,
inversion, discomfort, screams, clamps), ride excitement, synthetic decoder mutations, pinned
key operands, identified-binary mutations (release-rate default, min-distance offset, peak
weight 1.7, rating clamp 100) and PC catalog checks (12/4/4 track types, Chac Atak, pylon rates,
loop-capable set). A source-mutation check changed five pinned operands in the witness (15000,
offset 156 in the validator, 1.7, offset 156 in the pylon loader, the final `fmadds` registers).
Each one failed the suite.

### 11.1 COAST-R2 witness

```sh
python3 -I tools/ppc-analysis/lanes/coast/coast_geometry_evidence.py /path/to/mac-feral/bin
```

`tools/ppc-analysis/lanes/coast/coast_geometry_evidence.py` pins 34 code-range digests, 97
decoded D-form operands, 41 call targets, 3 import glues (`sin`, `atan2`, `__fpclassifyf`), 12
mask shapes, 34 TOC constants, 3 relocation-checked TOC pointers (`0x66f78` snapshot,
`0x1577c0` sine table, `0x66f74` live map) and 6 bounded whole-code scans. It also checks that the
delete, remove, cancel, close and end-node blocks make no money call. The pure functions restate
13–17 and 16 (pricing).

`test_coast_geometry_evidence.py` (32 cases): constants linked to the decoded TOC values and
immediates, the sine table, Bézier endpoints and Bernstein form, knot control collinearity and
chord scaling, the 0.7 chord weight, node tangent cases, the loop factor, turn sign and bank
scaling, the speed estimate, the sampler (uniform 3.0 on a straight; ≤ 3.0 chords and ordered
parameters on a curve), the speed cap, flat-track `v·dt`, the hop step, the decay to a stop,
station braking, pricing (truncated place, ceil/trunc move, same-cell no-op), node world
position, synthetic decoder checks, and identified-binary mutations: spacing constant, 0.05
operand, snapshot → live map pointer, a credit call inserted into the delete block, and the
0.95 decay constant.

A source-mutation check changed seven pinned items in a copy of the witness: the 0.05 operand,
the 0.7 constant, the spacing, the credit-call scan, the decay, the snapshot operand and the
bank divisor. Each one failed the suite.

## 12. COAST-R2: method and scope

COAST-R2 traces the items section 10 left open, with the same conventions and the same
identified Mac binary. The witness is `tools/ppc-analysis/lanes/coast/coast_geometry_evidence.py`
(11.1). Each claim names its operand or call sites. "Bounded" claims rest on the whole-code scans
listed in the witness, which see only D-form immediates. Indexed or computed access is not
excluded. The pure functions in the witness restate the arithmetic in single precision. They are
bit-faithful for the Bézier evaluator, the sine table and the sampler's emission step. Elsewhere
they follow the same operation order but are checked only to small tolerances.

Rebuild order (`0x449f8`, called from the tick callback `0x3864c` and from the save loaders):
`0x33398` node layout, tangents, angles, auto-bank, pylon animation and knots; `0x445bc` knot
links; `0x337b4` knot angles; `0x44ff4` sampling; `0x3fc18` rating pass; then `0x3c600`,
`0x3da2c` and the track mesh (`0x43bb8`, `0x42590`/`0x43398`, `0x426a0`).

## 13. Track geometry (high unless stated)

### 13.1 Nodes and knots

A node is one placed pylon or one pylon-less node (descriptor flag bit 0, for example the station
end nodes). Each node owns **knots** through node `+144 → {count, A[], B[]}` (88-byte records).

- Pylon-less node (`0x3619c..0x361f4`): one knot, and `A` **aliases the node at +12**. Knot +52,
  +64 and +76 are node +64 (backward control), +76 (position) and +88 (forward control).
- Pylon node (`0x34078`): `count` = pylon control +60. The knots are the track **tags** of the
  pylon mesh, looked up through `0x19b298`. After the pylon animation (`0x32014`, from loft `t`,
  rotate = yaw/2π, incline, bank and loop-rotate), `0x32f80` sets knot +64 to the tag position
  and the second-rail knot `B +64 = A +64 + 2 · tag axis(+16)`. The tag table builder and the tag
  data in the pylon meshes are not traced (COAST-015).
- `B[]` is a second rail that gives the roll (13.7). On pylon-less nodes,
  `B = A + (−sin a, 0, −cos a)` with `a` = node +224, and on loop nodes (flag 0x40) `(0, −1, 0)` with node flag 0x80 clear (the 0x144 station end
  node) or `(0, 1, 0)` with 0x80 set (`0x32dd8..0x32e08`; COAST-V2 CV2-2).
  The resulting roll on non-loop pylon-less nodes is not bounded (COAST-017).

### 13.2 Node position (`0x33398`)

The descriptor stores `+28 = 10·x`, `+36 = 10·y` and `+32` = terrain height (`0x339d8..0x33a3c`).
The node is at the **cell centre**: `X = 10x + 5`, `Z = 10y + 5` (5.0 from the TOC, `0x333ec`).
At stack level 0, `Y = ground + h(t)`. Stacked, `Y = Y_below + pylon(+196) + h(t)`, where `h(t)`
is `0x369bc` (section 4.1). Nodes whose pylon has `+44 == −1`, and nodes with flag 0x2000, keep
their position.

### 13.3 Node tangents (`0x31450`)

With `a = prev − P`, `b = next − P`, unit `â`, `b̂`, `C = fCurviness` (definition +8) and
`SD = fStraightDivisor` (+12):

- **Ends.** No previous node: `T+ = b/SD`, `T− = −b/SD`. No next node: `T− = a/SD`, `T+ = −a/SD`.
  For an end node with flag 0x80 whose previous link folds back, the end case is reversed. Node
  flag 0x80000 drops the previous link and 0x40000 the next. Flag **0x100000** makes the next
  node the **second node of the track** (`0x314e0`); this is the closed circuit.
- **Straight.** The straight case applies when `â·b̂ <` the pylon's `fPylonStraightDotThreshold`
  (+176; StdPylon −0.999, LoopPylon −0.8), or `< −0.999` without a pylon. On a **loop** pylon
  (pylon flag 0x4000, `0x31a74..0x31a7c`; COAST-V CV-6 corrected "non-loop"),
  `T− = |a|·½(b̂ − â)/SD` and `T+ = |b|·½(â − b̂)/SD`; flag 0x80 reverses both. Otherwise
  `T− = a/SD`, `T+ = b/SD`.
- **Folded back** (`â·b̂ > 0.999`): the same straight branch.
- **Curve.** On a loop pylon (flag 0x4000), `T− = (â + b̂)·|a|·C` and `T+ = −(â + b̂)·|b|·C`: the
  track leaves the loop pylon away from both neighbours. Otherwise `n = a × b`, `ŝ = (â + b̂)/|…|`
  and `t = ŝ × n̂` (in-plane, perpendicular to the bisector), giving `T− = t·|a|·C` and
  `T+ = −t·|b|·C`. On suspended coasters (definition 0x8), a loop node with `n.y < 0` gets flag
  0x10000000 (inverted).
- **Non-loop pylon next to a loop pylon** (after the plain straight case `0x31be0..0x31c54` and
  after the curve case `0x31eac..0x31f20`): the tangent Y components are zeroed. Nodes without a
  pylon keep them (COAST-V CV-6 corrected "loop next to loop").

Stored: node +40 = `T−`, +52 = `T+`, +64 = `P + T−/3`, +88 = `P + T+/3`. Node +148 is the
**signed XZ turn angle** `±acos(clamp(â_xz·b̂_xz))`, negative when `â.z·b̂.x − â.x·b̂.z < 0`.
`0x420f0` turns `T+` into the node yaw (+212, and +224 when node flag 1) and pitch (+220). Those
drive the pylon rotate and incline animation. **Only the direction of `T+` survives into the track**,
because 13.4 rescales every knot.

### 13.4 Knot tangents (`0x32cf0`, `0x32990`, `0x32298`)

For each knot (both rails), `0x32990` computes the chords to the previous and next knot,
crossing node boundaries, as `√(dx² + (0.7·dy)² + dz²)` (constant 0.7 at `0x32a34`). Without a
neighbour the chord is 1.0. The direction `u` is the mesh tag axis (+32..+40) on a pylon,
`(0, −1, 0)` on a loop node with flag 0x80 clear (`0x32dfc`; the 0x144 station end node), `(0, 1, 0)` on
a loop node with 0x80 set (COAST-V2 CV2-2), and otherwise the normalised node `T+`.
Then (`0x32298`):

- **Plain knot:** `T− = −u·C·chord−/|u|`, `T+ = u·C·chord+/|u|`.
- **Loop-pylon knot:** `K = (C(1 − x²) + 2.1x²)(1 − w) + 2.1w`. Here `x = clamp(|h·r|, 0, 1)` for
  the unit horizontal direction `h` to the next node (the previous one on flag-0x80 nodes) and the
  yaw axis `r = (sin y, ·, cos y)` of the rotate parameter. `w = (1 + cos 2πu)/2` with
  `u = clamp(2·fmod(loopRotate, 0.5), 0, 1)` (node +248). With node flag 0x80 clear the back side
  always uses `K`, and the forward side uses `K` when the next node has flag 0x40, else **1.7**;
  flag 0x80 mirrors this (`0x32600..0x327dc`; COAST-V CV-10).
- Controls: knot +52 = `P + T−/3`, +76 = `P + T+/3`.

So every segment is a cubic Hermite curve whose tangent length is `fCurviness` (1.25 by default)
times the 0.7-weighted chord. The control arm is about 0.42 chord.

### 13.5 Segment curve (`0x41b00`)

`Q(t) = A64 + t·(3A76 − 3A64) + t²·(3A64 − 6A76 + 3B52) + t³·(−A64 + 3A76 − 3B52 + B64)`. That is
the cubic Bézier with `P0 = A +64`, `P1 = A +76`, `P2 = B +52`, `P3 = B +64`, evaluated with the
fmadds order of the witness `bezier()`. `0x41c0c` is its derivative.

### 13.6 Sampling (`0x44ff4`)

- **Spacing**: controller +172 = **3.0** world units. The only controller write is the
  constructor (`0x38ab8`/`0x38abc`; bounded scan `module_stfs_172`). COASTERSMOOTHNESS does not
  feed it.
- The cursor walks knot pairs: `A_i → A_{i+1}` within a node, then the last knot to the next
  node's first knot. Starting at `t = 0` (sample 0 = first knot), `t` advances by 0.05 and the
  chord lengths `|Q(t) − previous|` are summed. When the sum reaches 3.0, the sample is placed
  **on the current chord** at fraction `a = (3 − sum_before)/d`, and `t_s = t_prev(1 − a) + t·a`.
  The sum restarts from the sample, and the next `t` is the next multiple of 0.05 after `t_s`.
  While `t_s < 0` the sample record moves to the previous knot pair with `t_s + 1`
  (`0x45320..0x4535c`); the cursor stays.
  The polyline length between two samples is exactly 3.0 (witness test).
- Sample record: +4..+12 position, +16 yaw, +20 pitch, +24 roll (13.7), +44/+48 the A and B
  knots, +52 `t`. Rating flags and speed are added by `0x3fc18`. The capacity estimate is
  `Σ trunc(3·chord/3.0 + 1)`, grown by ×1.1.
- **Closed circuit**: when the cursor passes the last node, the closing pair runs from the last
  knot to the first knot of the second node (`0x44ea4`, via controller +84 → +100). The sampler
  stops after the **first** sample on that pair, and `t` may run past 1. The last sample is
  duplicated into slot `n`, and `n` may grow by one (`0x45858..0x45918`). COAST-019 keeps these
  details APPROX.

### 13.7 Orientation and the sine table

- `0x41e24`: yaw = `atan2(T.x, T.z)` of the derivative. `T` is rotated by −yaw, then
  pitch = `atan2(−T.y', T.z')`.
- `0x41f80`: the second-rail point `B(t)` minus the sample position is rotated by −yaw, then
  −pitch, and roll = `atan2(−x', y')`.
- Rotations use a **4096-entry sine table** (`0xa3cc8`, called from `0x4b72c`, size `0x4b720`):
  `T[k] = sin(f32(k/4096)·2π)`, `sin a = T[trunc(a·4096/2π) & 4095]`, and
  `cos a = T[trunc((π/2 + a)·4096/2π) & 4095]`. `atan2` is the library import.

### 13.8 Banking

- **Auto-bank** (`0x3f548`, from `0x33630`): when definition flag **0x10** is set, node flag 0x4 is
  **clear** (`0x3360c`; COAST-V CV-9) and the node has a next node; node flag 0x100 forces bank 0
  (`0x3f584`),
  `bank = v·(±π − θ) / def(+16) / (|prev − P| + |next − P|)`, with `+π` when `θ ≥ 0` and 0 when
  `θ = 0`. Definition +16 defaults to **4.0** (`0x2d2d0`). No code reads settings offset 15060,
  so `fAutoBankDivisor` is dead (scan `any_15060`). The speed `v` is a running estimate over the
  nodes: `v = max(mod·(PerHeight·(y_node − y_next)) + v, fMinSpeed)` (`0x3f36c`, uphill modifier
  when the next node is higher), starting at 0 and updated after each banked node. The bank is
  stored in node +216. It becomes pylon animation parameter +244 = `frac(bank/2π + ½) − ½`, which
  tilts the tag axis and so the roll.
- **Flags**: the loader sets 0x10 when `bNoAutoBanking == 0`. `bIsSuspendedCoaster` sets 0x8
  **and clears 0x10** (`0x30248`), so suspended coasters never auto-bank.
- **Suspended swing** (`0x400d8..0x40438`, definition flag 0x8, after the rating loop): per sample,
  the force vector plus gravity (+96..+104) is rotated into the sample frame. A target angle comes
  from `atan2`, and the roll moves toward it at rate `min(|F + g|·fForceMultiplier, 1)`. Over the
  braking zone (17.3) it fades linearly to 0. COAST-R (6.2 step 8) called this auto-banking; that
  was wrong. The exact arithmetic is located, not restated (COAST-018).

### 13.9 Loops

A loop has no special geometry code. LoopPylons carry several mesh tags along the loop, and their
knots use the loop tangent factor `K` (13.4) and the bisector tangent (13.3). Tags adjacent to plain
track use 1.7. The loop-rotate slider (`0x462f8`) edits node +248 over `[0.15, 0.85]`, or
`[−0.35, 0.35]` on flag-0x80 nodes, in steps of 0.01. Loop geometry is therefore data
(LoopPylon tags) plus these factors (COAST-015).

## 14. Build checks (high, bounded where stated)

### 14.1 Slope and deviance are not checked

- `fMaxHeightDiffPerGSquareDist` (pylon +112): no float load at displacement 112 in the coaster
  module (scan `module_lfs_112`). The four loads elsewhere (`0x9469c`, `0xa2f0c`, `0xa3014`,
  `0xa31a0`) are camera-object and matrix code.
- `fMaxLoopRotnAngleDeviance` (+152): module loads at +152 are node records in `0x3d1b8` only
  (scan `module_lfs_152`). `fMaxLoopHeightDifference` (+172): the module's +172 accesses are
  controller spacing reads, and the only stores are the defaults and the loader.
- `fMaxRotnAngleDeviance` (+148) is read only by the rotate slider `0x46508`: range
  `[yaw − dev, yaw + dev]`, step 0.02. Bank and pitch sliders (`0x46718`) span ±3.14149, step 0.02.
- Height stays clamped, not rejected (section 4.1).

### 14.2 Blocked cells

The validator's bit 0 test (`0x36cd0..0x36ce8`) reads the **map snapshot** `*0x66f78`, not the
live map `*0x66f74`. The snapshot is a copy of the level map record (`0x4db9c`, at level load). It
is refreshed from the live map once, at game start (`0x11b478 → 0x54ee0 → 0x4dcb8`), and freed
with the level. Only six instructions load that pointer (scan `snapshot_pointer_loads`): the
validator, create, free, the refresh, and two tile-restore paths. Bit 0 of the cell flags is the
**no-ground** bit: the terrain height query returns FLT_MAX for it (`0x5052c..0x50538`). Tile
painting sets it together with 0x100 (tile set 0 index 8, `0x99e18`). So the blocked set is the
level's no-ground cells as they stood at game start. Paths, queues, scenery and rides placed in
play do not set it, and the validator consults no other occupancy. The exception is test 7:
another coaster's pylon in the cell, found by walking every coaster definition's descriptor list
(`0x56bac`, desc +8 = cell index), rejects. Whether the generic tool layer (cell action 129,
COAST-016) adds occupancy checks is unresolved.

### 14.3 Failed validation marks, it does not refuse

The build tick (`0x47e4c`) calls the validator on the cursor cell. On failure it sets node flag
**0x4000** on the moved node and its predecessor (`0x48334..0x48368`); on success it clears it
(`0x482f8..0x4832c`). The node stays where it is.

## 15. Circuit closure and completion (high for the operations, medium for the UI meaning)

- **Add** (`0x375c4`; from the build click via `0x45f5c` with `r5 = 0`, from save loading with
  `r5 = 1`): if the clicked cell holds a descriptor of this coaster, controller +60 bit 1
  ("circuit open") is set, and one of the stacked nodes on that cell is the chain's open start
  (track +100), the call **closes the circuit** (`0x470c4`) instead of adding a node. Closing
  clears 0x40000 on the last node and 0x80000/0x20000 on the open start node, makes the end nodes
  adjacent (track +96 = track +88), clears controller +60 bit 1, and calls the state change
  `0x38250(…, 2)` and `0x369d0`. Otherwise a node is created (`0x35fdc`).
- **Complete** (`0x37ef0`): sets controller +60 bit 0 and appends the station end node(s)
  (`0x37cc8`) at the station mesh tag positions, pylon-less. Definition bit 0x20 set gives one
  node with flags 0x144 (`0x37e34`), clear gives two with flags 4 (`0x37e3c`). The loader always
  ORs 0xA0 (`0x301f4`) and nothing clears 0x20, so the original **always** appends one end node
  with flags 0x144, whatever `bVisibleEndPylon` says (COAST-V CV-3). The loop flag 0x40 makes its
  knot direction and second rail vertical (13.1, 13.4). `0x37ef0` tests neither the 0x4000 marks
  nor the closure (COAST-V CV-8). It then sets track
  flags 1 and 0x1000 (rebuild, re-rate, recount trains). The build click calls it when the
  generic cell action 129 (`0x84ea4`) returns 0 for the clicked cell, and adds a pylon otherwise
  (`0x73a3c..0x73a9c`). What action 129 tests is in the tool engine `0x80c50` and is unresolved
  (COAST-016).
- **Runnable**: `0x45da4` requires bit 0 set and bit 1 clear (section 4.2), i.e. complete and
  closed.
- **Reopen**: deleting a range of nodes (`0x47170`) marks the cut ends open (0x50000 / 0xA0000).
  Under the condition at `0x47508` it clears controller +60 bits 0 and 1 and calls `0x37e8c`.

## 16. Money, refunds and undo (high)

The debit is `0xcbfdc(bank, n)` (bank +12 −= n, plus statistics) and the credit is `0xcbf50`. All
coaster charges are skipped when the global flag at `0x11ef3c +36` is nonzero ("money off"; its
UI meaning is unresolved). Where `cost()` appears below, it is `0x4b168` (section 4.4), as a float.

| Action | Code | Money |
| --- | --- | --- |
| Place a pylon (UI) | `0x37888..0x378c0` | debit `trunc(cost(node, unit charged))` at once (`0x1c3fbc` truncates, negative → 0); the balance is read (`0x378a0`) and ignored, so the debit is unconditional |
| Cursor preview while dragging | `0x483ac` | accumulator `0x84ac4 += ceil(new) − ceil(old)`, used for the cost display and the affordability check of the tool (`0x6f664`); a negative delta is added as `delta − 0x0EEEEEEE` (meaning unresolved) |
| Pick up a node | `0x4a1fc` | stores `cost(node)` and `cost(next)` as floats (`0x4a404`, `0x4a420`) |
| Drop on another cell | `0x4a43c` | credit `trunc(stored node)` [+ `trunc(stored next)`], debit `ceil(cost(node))` [+ `ceil(cost(next))`]; the bracketed terms only on a complete track; every cost here without the unit (`r4 = 0`) |
| Drop on the same cell | `0x4a5a8..0x4a5bc` | nothing |
| Release the height slider | `0x45fc4` | credit `trunc(stored)`, debit `ceil(new cost)`, both without the unit |
| Cancel a drag | `0x4a7e4` | node restored at the pick-up cell, no money |
| Delete pylons / reopen | `0x47170`, `0x492d8`, `0x37e8c` | **none**: the module's credit calls are exactly the four above (scan `module_credit_calls`), and the delete blocks call no money routine |

There is **no undo stack**. Cancel-drag is the only revert. Because the refund truncates and the
charge rounds up, each paid move costs up to one extra unit per node. Whole-ride demolition goes
through the generic ride code. No coaster field accumulates the track spend, so a track refund
there is not possible without one (bounded by these sites).

## 17. Train motion per frame (`0x40650`, high for the arithmetic)

### 17.1 Frame

`dt = (now − last)/1000` s from the cached clock `0xa3e08` (DET-I: 31 ms per frame). Every train
gets the budget `dt` (train +124). Car positions are sample positions `s = train(+116) +
car(+56)/3.0`, wrapped into `[0, n)`, and linear between samples. Trains are processed from a
queue ordered by their remaining budget.

### 17.2 Hop

For the queue head, per car: the point half a sample ahead is evaluated. The car's candidate is
`v_c = max(mod·(PerHeight·(y(s) − y(s + ½))) + v, floor)`, with `mod` uphill or downhill by the
two heights. The floor is `W = fWinchSpeed·P` (halved for trains flagged 0x80 and 0x20) when the
sample's section has winch flag 0x800, otherwise `M = fMinSpeed·P`. The winch test reads the
section of the sample at `trunc(s + ½)` (`0x4098c`, `0x40a0c`). Cars with car flag bit 0 clear
are skipped in the sum, but the average divides by all cars. `P` is the power factor at
controller +44. The new speed is the car average. Then:

1. Braking (17.3) if any car is strictly past `ctl+308` and the train does **not** have flag 0x40.
   Otherwise friction: `×fFrictionMultiplier` when the sample at the reference car's +64 position
   has flag 8. Car +64 is written only after the frame's hops (`0x41094`), so this sample is fixed
   for the frame.
2. `max(·, W)` if any car was on a winch section (train flag 8), else `max(·, M)`.
3. `min(·, ctl+52)`.
4. Hop time `τ = d / ((v + v')/2)`, where `d` is the distance from the reference car (train +44)
   to its half-sample point (`0x4316c`). If `τ <` the remaining budget: `v = v'`, the budget
   drops by τ, `s += ½`, and the train is re-queued. Otherwise, with `α = budget/τ`:
   `v_end = (1 − α)v + αv'`, `s = s_saved + ½·((v + v_end)/2·budget/d)`,
   `v = min(cap, v_end)` with **no** floor, and the train leaves the queue. (`0x40d98` stores
   `max(v_end, M)`, but `0x40d9c..0x40dc8` reload the unfloored `v_end` from `276(r1)` and overwrite that
   store; COAST-V2 CV2-1.)
5. After every hop: `v ×= 0.95` if `v < 2.0`, and `v = 0` if `v < 0.01`. Then the train extents
   (+32/+36) are updated, `0x40574` runs, and `s` wraps by `fmod(s, n)`.

Accelerations are per hop, i.e. per distance (half a sample is 1.5 units), not per second. Time
enters only through the hop count.

### 17.3 Station braking

`ctl+308 = max((n − 1) − v_last/3.0, 0)` samples (`0x40070..0x400d4`), where `v_last` is the rated
speed of sample `(tail +252) − 1` (about one second of travel before the end). The tail is
controller +88 (the station end node), and node +252 is the truncated sampler coordinate when the
walk reaches the node (`0x45790`, `0x457a0`): the last sample before its knot (medium). For a train without
flag 0x40, `x = (front − ctl+308)/(n − ctl+308)`, with `x = 1` outside `[0, 1]`, and
`v = v(1 − x⁴) + W·x⁴`. A train's flag 0x40 clears while it is between `ctl+252` and `ctl+308`. Leaving mode 64 clears it on every train.

### 17.4 Power factor and stopping (`0x38250`)

Changing the ride mode from 16 or 32 to **64** sets `P = 0`. Changing from 64 to 16 or 32 sets
`P = 1`, flags every train 0x80 and clears train flag 0x40. The state step 1 → 2 in mode 64 also
sets `P = 1`. COAST 4 maps its arguments 0/1/2 to modes **16/64/32** (`0x3de0c..0x3de54`; COAST-V
CV-2), so a scripted breakdown (argument 1) removes the power. With `P = 0` both floors are 0, so
trains lose speed uphill, decay by 0.95 per hop below 2.0, and stop below 0.01. The winch floor
`W` is halved for trains with flags 0x80 **and** 0x20 (`0x408e4`); 0x20 is set after the seat
allocation (`0x3e9cc`) and cleared at `0x3e7e4`.

### 17.5 Speed setting

`0x3fb68`: `ctl+52 = fMaxSpeedAtMinSetting·(1 − s/100) + fMaxSpeedAtMaxSetting·(s/100)` for the
integer setting `s` (controller +48). The constructor sets `s = 60` (`0x38b2c`). COAST 8 and
`0xb2838` change it.

## 18. Ride camera (unresolved, with the reason)

No coaster-module function is called from camera code (bounded: direct `bl` calls only). The module's external callers
(`0x2d000..0x4bd64`) are the tick and render callbacks `0x3864c`/`0x387e0`, the sound-channel
setup `0x38bc0 → 0x3c21c`, save/load (`0x394b8`, `0x39f98`), the status predicates
`0x45dfc`/`0x45e58` (ride info text `0x138a1c`), the guest predicate `0x45eac`, the COAST bridge,
and UI entries. The graphics options live in the settings object `0x550a0` (descriptor table
`0x393b8`). `0x4c2bc` reads the four-level view distance at +76 and applies it as far plane and
fog (200/325/450/700 with fog 50–60/100–110/135–150/230–250). By descriptor order, this is
FIRSTPERSONVIEWDISTANCE or its neighbour (medium). Either way it is a render distance, not a
camera path. The camera virtual methods `0x93d08`/`0x94474` (jittered target/position objects) are the
next lead. Evidence that would settle it: trace those methods for a "follow object" mode fed by
car +72..+80, or capture the camera matrix during an original ride view. COAST-011 stays APPROX.

## 19. Corrections to COAST-R

- 6.2 step 8: definition flag 0x8 runs the **suspended swing**, not auto-banking (13.8).
  Auto-banking happens at the node level, before sampling.
- 3.3: sample +16/+20 are **yaw/pitch angles**, not "direction terms".
- 4.2: "Complete … how the game joins the last node back to the station is unresolved". Closure
  and completion are now separate operations (15).
- 4.4: the coaster code never debits the build-cost accumulator; its charges are the direct
  calls in 16. The generic object-placement path (`0x72bb4`, which places the station) debits
  the accumulator and resets it per placement (`0x6f628`/`0x6f640`). Whether coaster deltas can
  leak into that charge is not bounded.
- 10, COAST-001: the old default spacing of 1 world unit is wrong; the spacing is 3.0.

## 20. Corrections from COAST-V (REVIEW-COAST.md)

COAST-F corrected the sections above in place where the review refuted them: the COAST 4 mapping
(5.3, 9.7 item 8, 17.4), the rating force sign and the pass's end samples (6.2), the cost unit and
rate fallback (4.4), the straight tangent case and the Y zeroing (13.3), the auto-bank gate
(13.8), the station end node (15), and the motion details (17.2, 17.3). Further readings:

- The loop-pylon knot sides (`0x326dc..0x327dc`): with node flag 0x80 clear the back side always
  uses `K`, and the forward side uses `K` when the next node has flag 0x40, else 1.7 (13.4).
- A sample whose `t` falls below 0 moves its record to the previous knot pair with `t + 1`
  (`0x45320..0x4535c`); the cursor stays (13.6).
- `0x369c8` is `fmadds(t, h1 − h0, h0)`, the turn window limits are `fmadds(A, 1 − t, B·t)`
  (`0x36fb4/b8`, `0x36fe4/e8`) and the derivative `0x41c0c` is
  `c1 + fmadds(t, t·(3·c3), t·(2·c2))` with the Bézier's fused coefficients.
- The LoopPylon `fMaxLoopHeightDifference` default is the raw 25.0 (`0x2d5d4`).

## 21. Corrections from the PC register round 2 (October 14, COAST-REPORT-2)

The Windows executable `tp.exe` (TPW-EXE) was read for the COAST items; where it differs from the Mac readings above, OpenTPW
follows the PC instructions (docs/COASTERS.md "Running", `CoasterRide`, `CoasterCollision`).

- 5.3, 5.4, 9.7 item 8 (stop and dispatch): the PC never stops a train. The motion tick `0x43ce20` has no zero-speed branch, the
  controller tick `0x435a30` runs it in states 2, 4 and 8 and the station step `0x43b8b0` in states 4 and 8 outside mode 64;
  arrival unloads in any state, state 4 seats guests from the admission ring into the cars inside the window `[ctl+0xf8, ctl+0xfc]`,
  departure swaps the two rider lists of each car. `COAST 2` is the capacity minus the admission ring (`0x43b1f0`), closing moves the
  ring into the leaving ring (`0x43b390`). The round-2 report said trains do not collide: `0x43cd60` (called after every hop of
  `0x43ce20`) exchanges speed with the train ahead when it is overtaken and flags it `0x40`; the headway test at the end of
  `0x43ce20` multiplies the sample count between two trains by spacing over the speed of the sample numbered by the *train's own
  index* (a loop counter reused as a sample index, `0x43d85f`).
- 5.4: controller `+0x140` is the **self-intersection count** on the PC (writers `0x439c40`, `0x43ad50` only), not riders on board;
  the PC keeps riders per train (`+0x10`). The guest predicate `0x441970` tests it.
- 3.2 (pylon mesh heights): `0x430590` takes h0, h1 and the stack offset from the model's dummies. The attribute records of the
  model give the dummy number (counted from 1 over the dummy nodes): the track dummy is the one of the first record with bit
  `0x800000`, the stack dummy of the first with `0x100000`; the loft clip fractions are `clamp(Add, StartCap, EndCap)` and
  `clamp(Multiplier + Add, StartCap, EndCap)` (`fLoftAnim*`, copied when nonzero at `0x42ef05`). fMinHeight becomes `max(fMinHeight, h0)`.
  A stacked node's height adds each lower pylon's `h0 + t(h1 − h0)` and the stack offset (`0x433280`).
- 15 (end node): the call `0x435570` passes the creation parameters `0x144`; the node constructor `0x434060` maps parameter bits to
  node flags (`0x40` → `0x80000`, `0x100` → `0x100000`, `0x4` → `0x433bd0`, bit 0 clear → `0x200000`), so the end node is not a loop
  node and carries no `0x4` or `0x100`. The Mac site `0x37e34` could not be compared (no Mac binary in the register round).
- 13.9 (loops): a loop pylon comes in pairs. Loop mode sets the counter `0x79c664` to 2 (`0x442cd0`), `0x435020` decrements it; the node
  constructor gives the first node flags `0xe0` and loop rotate 0, the second `0x60` and 0.5 (`0x434153`).
- 13.6 and 17.3 (frames): the original's sample list starts at the platform start (its end node stands on the first station node and
  the closing pair is the platform's one-sample stub), the braking zone `[ctl+0x134, count)` lies before the platform and the window
  `[ctl+0xf8, ctl+0xfc]` between the sample coordinates of the first and third station node narrowed by `fEntranceSizeInterval`
  (`0x43b750`). OpenTPW built the same frame from the end node's knot while its station had two nodes; since the three-node station
  (section 22) the list starts on S0 and the window is read from the knot coordinates of S0 and S2.
- 13.7: the PC runtime sine table is `T[k] = f32(fsin(k/4096 · f32(2π)))` with scale `f32(4096 · f32(1/2π))` (`0x4708d0`); lookups round the
  index to nearest (`fistp`), except inside the self-intersection pass, which sets the truncating control word (`0x439ac4`).
- Collision (COAST-004): the track is tested against itself (`0x439ab0` → `0x439c40` → `0x43a2b0`), with the five default collision points
  `(4, 8), (4, 0), (0, −3), (−4, −1), (−4, 8)`; there is no terrain or scenery collision.
- Render (COAST-026): the textures scroll by `P · Δt` per frame with P the power factor (`0x435adf`); `fScrollRate` is not read there.

## 22. The three-node station and the controller states (October 15, TOUR-COAST report, round 3)

COAST-029, 019, 004 and 012 were implemented (`CoasterTrack`, `CoasterStationLayout`, `CoasterCurve`, `CoasterRide`, `Level.Coasters`).
Every claim was read again at instruction level in tp.exe (TPW-EXE); the round-3 report is the spec and was confirmed.

- **Station construction `0x436ac0`.** With definition flag `0x20` (always set) the loop runs `i = 0..2` over the dummy numbered `i + 1`
  with attribute `0x400000` (`0x44b220`) and reads its world position (node instance `+0x30..+0x38`, placement included). `i = 0` and `2`
  build a StdPylon node in the cell `ftol(x · 0.1)`, `ftol(z · 0.1)` (descriptor `0x432c10(…, 0x12, …)`, height = dummy y − the station's
  base height) with the creation parameters `0x274` and `0x28c`; `i = 1` builds a pylon-less node (descriptor `0x13`) on the exact
  position with `0x3c`. The constructor `0x434060` maps the parameter bits: `8` → `0x200`, `0x10` → `0x400`, `0x20` → `0x1800`
  (winch), `0x40` → `0x80000` (no previous), `0x80` → `0x40000` (no next), `0x100` → `0x100000`; bit `0x4` goes to `0x433bd0`, which
  skips the fMinHeight/fMaxHeight clamp of `0x433790` (an unstacked node's height is taken as it comes, the loft parameter is
  `clamp((h − h0)/(h1 − h0), 0, 1)`). The 12 PC stations' dummies (read from the model files, in model units, End/Mid/Start): End is
  always `x = −5` and `z = 10·row + 5`, Start half a cell past the `>` column, Mid between them; heights 20, 29, 12.04, 2.04, 14, 3.5,
  12.04, 17.43 → 42.32 (the Moonshot's station climbs), 44.09, 11.08, 7.09 and 12.69.
- **Completion `0x435570`.** One end node (definition `0x20`) at the world position of S0's pylon track dummy, a pylon-less descriptor with
  `0x144` (= `0x80000 | 0x100000`), then the state setter with state 2.
- **Tangents `0x431660`.** The previous neighbour is dropped on `0x80000`, the next on `0x40000`, and on `0x100000` the next is the head's
  next node (S1): S0 and the end node leave along S1 − S0, S2 along S2 − S1. The bank pass `0x4310c8` banks a node that has a next node
  in the list (the raw link) and no flag `0x4`: S2 banks, the end node (no next) does not, S0 has no previous neighbour and takes none.
- **Sampler `0x440c10`.** The walk covers every pair of the node list; at the end node the closing pair runs from it to the second node
  (head `+0x64`), emits one sample (t may pass 1), the count `n` is the samples written, the last sample is copied into slot `n` and `n`
  grows by one when `n ≤ trunc(c) + (frac(c) ≠ 0)` with `c` the end knot's coordinate (`ctl+0xa8`; it lies less than one sample past the last
  sample before it, so the growth does not happen). A knot's coordinate (`node +8`) is the index of the last sample before it plus the distance
  walked since that sample and the distance to the knot over the spacing; S0's is 0. The braking start is `(n − 1) − v/spacing` (`0x43c72f`) and
  the swing's fade runs over `(i − b)/(c − b)` (`0x43c9e8`).
- **Collision `0x439c40`.** S0 → S1 and S1 → S2 are ordinary links; the skips are the ordinal-0/pylon-less rule only, so a track over the
  platform is counted and the closing link into S0 is not tested against S0 → S1.
- **Stacking `0x434d34`.** The bottom node's flags `0x200`/`0x400` refuse a stack: S0, S1 and S2.
- **State setter `0x435730`.** Old → new: 1 → 2 sets P = 1 (outside mode `0x40`), sets every train slot up (`0x43be10`) and clears the highlight;
  1 → 8 and 2 → 8 run `0x43b390`; 2 → 1 removes every train (`0x43bf20`), clears flag `0x800` and highlights the chain start; 4 → 2 unloads
  both seat lists of every train (`0x43b480` with 0 and 1); 4 → 8 runs `0x43b390`; 8 → 2 unloads both lists and runs `0x43b390`; the rest changes
  nothing. Callers: completion, close-circuit `0x442c70` and load `0x437b60` set 2, the cut `0x446740` and `0x4368f0` set 1, the script's
  `COAST 5` (`0x43b2f0`) 4 or 8.
- **The coaster tool `0x4474d0` → `0x447520`.** Round 2 called `0x447520` the tool's exit; with param 1 it is the **entry**: with no coaster being
  edited (`0x790fec`) it closes the ride object (`0x4df300`: "Object %d Closing", `+0x68/+0x6c` cleared, event 6) and sets state 2 when the
  track is complete with its circuit closed (controller `+0x3c` bit 0 set, bit 1 clear), else 1. With param 0 it is the exit: after the
  sub-mode clean-up it opens the ride object again (`0x4df390`) when the controller's state is 2.
- **Still open (COAST-029).** A station whose dummies lie below the StdPylon's h0 (Ghosta Coasta 2.04 against 4, Scatty Batty 3.5 against 5,
  Candy Coaster 7.09 against 10, Escape Velocity 12.04 against 30; the pylon heights come from the models): the loft parameter stops at 0, so
  S0 and S2 stand at h0 above S1. Whether the original's pylon pose leaves it so, and the terrain height under the pylon's cell, were not traced.
  Whether the middle knot takes `fTrackHeightAdjust` (the Big Dripper's −1.5, zero elsewhere) is not known either.

## 23. The coaster tool panel (COAST-028, COAST-035; PC tp.exe, October 15)

Read at the instruction level for the panel round; addresses are `[BIN:TPW-EXE]`.

- **Panel.** Table `0x751530` (layout command stream; root `0x17`, `!f_plain` boxes `0x22` and `0x18`, controls `0x19`..`0x21` with the models `cb_incline`, `cb_loft`, `cb_move`, `cb_rotate`, `cb_swapdown`, `cb_swapup`, `cb_track`, (`cb_loop`), `cb_dellast`, help ids 569, 297, 296, 300, 303, 302, 564, 565, 304). The click handler `0x4982a0` maps a control to a button bit (`0x19` → 8, `0x1a` → `0x10`, `0x1b` → 4, `0x1c` → `0x20`, `0x1d` → `0x100`, `0x1e` → `0x80`, `0x1f` → 1, `0x20` → 2, `0x21` → `0x40`) and calls `0x446fa0`, which maps the bit to the sub-mode `0x79c624` (1 build, 2 move, 4 tilt, 8 height, `0x10` rotate, `0x20` stack up, `0x40` stack down, `0x80` demolish) through `0x444360`/`0x445220`.
- **Pointer.** `0x446300(press)` picks the pylon model under the pointer (`0x45d620` = the hovered thing, matched with the node's thing at `+0x70` through table `0x7a4610`; descriptors with flag 1, the pylon-less ones, are skipped, flag 2, the station's, cannot be edited) and starts the sub-mode's handler; the stack and demolish sub-modes act on the release. The per-frame drag `0x443340` computes `(start mouse − mouse) × rate` per axis (the start position is `0x790ac8/acc`, mouse pixels), clamps to the handler's range and, for the clamped, non-camera-relative drags, moves the start position to `(clamped − start value)/rate + mouse`; flag `0x2000` (tilt) first turns the vector by `camera yaw − node yaw` (`local_8 = cos·dy + sin·dx`, `local_c = cos·dx − sin·dy`).
- **Handlers.** Height `0x441a00` (pylon flag `0x20`, rate 0.005 on Y, range `[max(0, (pylon+0x68 − h0)/(h1 − h0)), t + (pylon+0x6c − 0x433280(top of stack))/(h1 − h0)]`; the release credits `ftol` of the noted cost and debits `0x43e160(cost)`); rotate `0x441e80` (flag `0x40`, rate 0.02 on X, `[auto yaw − pylon+0x94, auto yaw + pylon+0x94]`, node flag 1); tilt `0x442010` (flag `0x100`: `+0xD8` ±π with node flag 4, flag `0x80`: `+0xDC` ±π with node flag 2, rates 0.02); loop tilt `0x441cd0` (flag `0x200`, rate 0.01, `+0xF8` in `[0.15, 0.85]`, or `[−0.35, 0.35]` for a first-of-pair node whose value above 0.5 first loses 1); swap `0x4423f0`/`0x4425c0` → `0x4424e0` (stack index, array entries, model base height `+0x44` and loft `+0xE8` exchanged); move `0x446d50` (press), `0x443340` (the node follows the hovered cell through `0x442f40`, validator `0x434740` sets `0x4000`), `0x445a70` (release: the node and, on a complete track, the next one are re-costed and `0x4d0190`/`0x4d01f0` settle). The pylon flags `0x20`..`0x200` are set by `0x430590` from the model's animations (types 3, 10, 2, 9 and 10 index 1).
- **Enable mask** `0x445910` and **initial selection** `0x4455c0` as in docs/COASTERS.md. A complete track (controller flag 1) with flag 2 clear disables the build buttons and enables demolish; any other state disables demolish.
- **Cut** `0x446740` (see `CoasterTrack.Cut`): the ends are the clicked nodes ordered by ordinal, ordinal 0 standing for the last node; a first-of-pair node adjacent to its second widens the range, equal ends widen to the neighbours, the second node of a pair after the lower end moves it back, a first-of-pair node before the upper end moves it on; the nodes between are deleted from the upper end down by `0x444cb0`; the lower end gets `0x50000`, the upper `0xa0000`; controller flag 2 is set (cleared with flag 1 when the range reached the last node, `0x435530`); `0x435730(ctl, 1)` and the build button follow. Closing on the chain start `ctl+0x64` is `0x442c70` (state 2).
- **Placement area.** `0x434f10` sets the box (reference node: the last node, `ctl+0x60` while cut open, the moved node's predecessor in a move) of half size `trunc(sqrt(pylon+0xa0)) + 1`; `0x5234d0` (tool mode `0x15`) draws face `0x115` on the hovered cell (red when `0x434740` returns 0) and face `0x11f` on every cell of the box (upper edges exclusive) that passes `0x434740`, has cell flag `0x40` clear, whose object (or the owner of a type `0xc`/`0x11` object, `0x53ad90`) is none of the types `0x19` and `0xb`, `0xd`, `0x10`, `0x12` (`0x53ad30`), and whose cell type is none of 9, 10, 3, 4, 7, `0x1e`, nor a type 1 cell with flag `0x20`.
- **Delete last** `0x444f30` is called only from `0x52fe50` (tool mode `0x15`, the undo list of the ride removal `0x527ee0`); no key or button calling it was found. The `0x403be0`/`0x403d00`/`0x403ed0` calls in the handlers are action-record log writes (`"AR: %d"`), not sounds.
- **Not traced.** The renderer's use of node flags `0x8000`/`0x4000`/`0x800000`; the picture of face `0x11f`; the clip angles; the panel window's position.

### HUD action-cost visibility (UI-047, October 18 follow-up)

Static caller inspection refines the formerly generic "placement-in-progress" label for bit 0 of `0x79C658`:

- HUD cash refresh `0x4A0CEE` calls `0x447990` only for mode type 8; the latter returns bit 0. Mode type 3 is accepted separately.
- Panel button handlers `0x498333` and `0x498383` enter Build/Loop through `0x497D20`, constructing the ordinary tool-mode object with tool `0x15`. Move at `0x498407` uses `0x497E10`, constructing the same mode class with tool `0x16`.
- Height at `0x4984A4`, and the other edit buttons, enter `0x497BC0`, which constructs the mode-8 object through `0x46CFC0`.
- Height handler `0x441A00` sets bit 0 at `0x441A5D` after its capability/selection checks, clears it at `0x441ADE` on the second action, and clears it on the failed-start path `0x441C27`. Rotation and tilt use different low bits, so a generic "any drag" predicate would be wrong.

`CoasterToolPanel.ShowsActionCost` maps the existing host state to this distinction: Build/Loop/Move, or an active height drag. The HUD uses it only to hide control 48's cost; cost values, bank transactions and the separate cursor-cost presentation are unchanged. Initial/host mode-stack equivalence and the unidentified original type-4 mode remain UI-047 qualifications. No original execution or new rendered comparison is claimed.

## 24. Who reads the controller's track flags (SAVE-921, round 11)

The saver `0x437300` writes controller `+0x3C` bits 0 and 1 and the track flags (`+0` bits `0x20`..`0x800`) into the Coasters block header's flag word, and the loader `0x437B60` sets
them again [BIN:TPW-EXE:0x43743C..0x4374D0 the saver's tests, 0x4380D4..0x438335 the loader's sets]. OpenTPW keeps the word raw; whether anything else reads `0x20`, `0x40`, `0x80`, `0x100`,
`0x200` or `0x400` was open (an earlier scan covered immediate masks on the controller pointer only). `tools/ppc-analysis/controller_flag_readers.py` (capstone, dev-only; it skips without the identified
tp.exe, `--check` fails on any new hit) answers it in four layers, run on the identified PC `tp.exe`:

- **Pointer sources.** A controller is the `0x14C`-byte record of the only such allocation [BIN:TPW-EXE:0x4368FC `push 0x14c` for the allocator `0x45B9D0` in the constructor `0x4368F0`] and its pointer comes from the
  thing-pool table `0x790BD0` (15 inline lookups), the list head `0x790FE0` (next link `+0x18`) or the edited global `0x790FEC` (240 sites): every place of the image naming one of the three is inside the coaster
  module (`0x42F000..0x44C000`). The pool library `0x468760`/`0x468810` only stores and clears the pointer in the table it is given [BIN:TPW-EXE:0x468760 alloc stores arg 5 at table arg 4, 0x468810 stores 0].
- **Flow.** From those sites the scan follows the pointer to a fixpoint through registers, stack frames (exact esp offsets, out parameters, pointers to locals), direct calls (the module has no indirect call and
  23 switch tables), the controller's fields (only `+0x18` holds a controller), global holders (`0x790FE4` = controller `+0x3C`, `0x792218` the current controller) and the fields of the other constant-size
  records the code allocates (the `0x48`-byte descriptor of `0x432C10` keeps the edited controller in `+0x14`; the `0x104`-byte track nodes hold descriptors). 207 functions of the module and 106 outside it are followed.
  It finds 87 accesses at controller `+0..3`; each loaded word is followed to what it does with it (test, compare, mask, merge back, copy, return). Outside the saver and the loader they observe bits
  `0x1`, `0x4`, `0x8`, `0x10`, `0x800`, `0x1000`, `0x2000`, `0x4000` and `0x8000` and **none of `0x20`..`0x400`**, except one false hit read by hand (`0x43F641` tests the node walked from controller `+0x54`, not the controller; the
  controller is only the loop's end sentinel in a path on which the loop is left).
- **Residue, read by hand.** Two controller pointers the flow sees stored through an unknown base are artefacts (`0x43392A`: the last slot of the node-copy array `0x791D08 + n * 0x104`, n < 5; `0x43BA09`: an argument slot
  reused as an out parameter); 12 calls with a tracked pointer in a register or argument it cannot follow are the action-record writes (section 23), `GlobalFree`, `GetDC` and the save stream's read and write through its
  vtable (`0x5F5E20`, `0x5F5E50`), none of which reads controller memory.
- **Independent layer.** All 67 `test [reg+0..3], imm` instructions of the image whose immediate selects a track flag are listed with the last assignment of their base register: besides the saver's three and the
  loader's two (the descriptor `[controller+4]`, flag `0x20`) they test descriptors (`[x+4]`, `[x+0x18]`), section chunks and records of other systems (`0x462xxx`..`0x57xxxx`), none reached by the flow.

What the scan does **not** follow, so SAVE-921 stays open on exactly this: arrays inside records (indexed element stores and loads), the node pointer at `+0xD4` of the thing records (table `0x7A4610`) and
18 stores of node and descriptor pointers into records it has no type for (the report marks them as untyped; none stores a controller), and it assumes non-negative indexes and that a pointer
argument reaches 16 bytes of its caller's frame. A reader reaching the controller only through those containers, and by a pattern other than a `test` of the layer above, would be missed.
