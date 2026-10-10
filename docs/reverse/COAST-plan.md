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
`-13300(r2)`). When the node's pylon is a loop or trough pylon (`0xC000`), the length and height
rates fall back to the StdPylon under the conditions at `0x4b294..0x4b2d4`. Results are
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
16, 32, 64. Stopping a running coaster lets trains finish to the station (medium; not traced).
Breakdowns use the generic wear path (RIDE-WEAR). What a broken-down coaster does to its moving
trains is unresolved.

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
   climbs more than `0.01745` (unit Y of the normalised direction).
3. `a = (d_prev + d_next) / spacing · (−g)`, with `g = data:0x3d140` (1.0 in the image).
4. `v = max(mod·(PerHeight·(y_i − y_next)) + v, winch)` (`mod` uphill +40 / downhill +44),
   `× friction` on flag-8 samples, `max(·, winch)` again. If `v == winch`, the section gets
   flag `0x800` (**chain lift**). Then `v = min(v, ctl+52)`.
5. Sample `+28 = v`; `+32..+40 = a · v / 10`.
6. Rating step (6.3) with `(i, v, bank = sample+24, F = sample+32)`.
7. Track the maximum speed and the maximum sample Y; `t_lap += spacing / v`.
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
