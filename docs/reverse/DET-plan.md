# Determinism and replay: evidence and DET-I contract

Static evidence for the identified Feral Mac release, 2026-10-10. No original
instruction was executed and no Windows/Patch 2 behaviour is claimed. Addresses
are unpacked PEF section offsets (code section 0; data section 1, TOC
`data:0x8000`), as in [PPC-clock.md](PPC-clock.md).

| Binary | SHA-256 |
| --- | --- |
| `SimThemePark.data` | `04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5` |
| `c_c++_shared.data` | `5e04f9c00c922dc78a787d1b93067c75d37a3e65b0a0202e50c2f449f131b27f` |

Witness: `tools/ppc-analysis/lanes/det/det_evidence.py` (174 pinned
instructions plus bounded scans). Reference arithmetic:
`tools/ppc-analysis/lanes/det/det_model.py`. Labels used below:

- **pinned**: an operand witness in `det_evidence.py` fails if the field, call
  target or constant differs.
- **scan**: a whole-code-section scan over one instruction form (direct `bl`
  targets, D-form displacements, LCG multiplier immediates, string literals). It
  follows no branch, CTR/pointer-glue call, tail branch or register flow. Any
  "only" in a scan result means *only among sites of that form*. It is never a
  claim about the only runtime route.
- **pinned (review)**: pinned by the review test
  `tools/ppc-analysis/lanes/review/test_det_v1.py`, not by `det_evidence.py`.
- **cited**: proved by another lane and not re-pinned here. Commits that are
  not yet in `main` are named; DET-I must not treat them as merged.

This document serves the M2 requirement in
[COMPLETION-PLAN.md](../COMPLETION-PLAN.md) (§"Evidence and determinism
contract", §M2): a fixed simulation tick, a stable update/event order, named
RNGs with serialized state, and canonical replay fields.

## 1. What was already proved

| Topic | Fact | Source |
| --- | --- | --- |
| Clock domains | Clock A (scheduler/RSE/animation default) is a rate-scaled, pause-aware ms accumulator that can be held; clock B (advisor LIP, advisor channels) is unscaled and pause-aware, with no hold. | PPC-clock §"Two elapsed millisecond clocks"; PPC-formats §"Clock selection", §"Scene clock controls" |
| Rate | Clock A's rate starts at 1.0 and steps ×1.25/÷1.25, clamped to [0.25, 2.0]. A direct setter (0x127c40) has no clamp. Rate scales accumulation from the last read, not a tick length. | PPC-formats §"Scene clock controls" item 4; PPC-clock §"Rollover, saturation and interval drops" |
| Pause | 0x110518/0x110598/0x110604 freeze/resume/toggle both clocks together, gated on host `+60 == 1`. | PPC-clock §"Pause and speed input records"; PPC-formats §"Scene clock controls" |
| Hold (fixed step) | `0x10ec60(obj, 32)` stores `1000 divwu 32 = 31` ms per pass; this runs at main-loop entry (released at 0x1c15d0) and on every pass while the capture flag (TOC −0x5e24) is set. | PPC-formats §"Scene clock controls"; PPC-clock §"Two elapsed millisecond clocks" |
| Scheduler | 31 ms substeps of clock A, park turn every 8th substep, signed backlog >2000 ms dropped, at most 3 park turns per callback, `repeat while (signed) now > previous`. | PPC-clock §"Scheduler, park turns…", §"Rollover…" |
| World gate | Substep work runs only when `application_flag8 \|\| !gameplay_flag1`; excluded substeps still advance time and the counter. | PPC-clock §"Startup and turn gates"; review round 4 witness |
| Game type | Mode 0/2 call turn 0x10536c directly, mode 1 via wrapper 0x10565c; other values run no turn. | PPC-clock §"Startup and turn gates" |
| RSE | Instruction budget per script per manager pass; ordinary scripts run when `id & 7 == pass & 7`; WAIT deadlines on clock A (ms). | PPC-clock §"RSE scheduling…", §"WAIT boundaries and ID phases" |
| Animation | `frame = (now − start) × 30 / 1000 × speed` on the selected clock; channel clocks cached once per update by 0xa6f70. | PPC-clock §"Animation delta…"; PPC-formats §"Animation clock" |
| Calendar | Virtual date = fixed 2000-01-01 epoch (`mFunnyTimeStart`, saved) + `turn × 15000 / 4` s; `mSessionStart` is host time and does not feed the date. | PPC-clock §"Calendar epoch reconciliation" |
| Guests | Live-list order is newest first; fixed need additions need `turn % 16 == 0 ∧ id % 4 == 0`; guest IDs are allocated sequentially from 1. | PPC-review §6 "Nested needs guard", "Update order is newest first"; PPC-guests |
| Sound RNG | `candidate = seed × 1664525 + 1013904223`; the bundle's choosers never store the successor; the seed (`mRandomSeed__17CAudioPlaceHolder`, sound data 0xc2e4, slot 0x348 is a data pointer to it) is initialized from `LbTime_GetClock`. | **cited, unmerged**: advisor tips `0899f19` (phase 9) and `526e2bf`/`836bb0b` (phase 10) |
| World RNG | Context generator 0x105328, setter 0x105360, `time(NULL)` seed, `mRandomSeed` save/load link, COAST and C-library generators separate. | **cited, unmerged**: rides tips `c6b0906`, `40d395e`, `76aaa62`. Re-pinned here (§2.3), so these facts no longer depend on that merge. |
| Restore limit | Only the `mRandomSeed` field transfer is a qualified restore checkpoint; post-load draw order is not proved ("Keep graph-only random edges out of restore claims"). | **cited, unmerged**: rides `76aaa62` |

### RNG instances

| Instance | Owner / state | Algorithm and projection | Seed source | Saved by original | Status |
| --- | --- | --- | --- | --- | --- |
| World RNG | world `+0x1da708` (uint32), next to `mGameTick` `+0x1da70c` | `s = s·1664525 + 1013904223`; returns wrapping signed abs of `s` | `time(NULL)` at world setup 0x10474c; **reseeded to a thing ID** at 7 sites, to an object field at 1 site | **yes**, label `mRandomSeed` (4 bytes) | pinned |
| Coaster boarding | app global data 622280 (TOC 0xcc0) | `s = s·214013 + 2531011`; `s >> 16` logical | one C-library `rand()` at 0x54bf4 | no label found | pinned |
| Kart object | object `+0x38` (subsystem 0x20688) | 214013/2531011; `s >> 16` arithmetic (`srawi`) | not traced | no label found | pinned (lane: state field, multiplier; review: multiplier and increment) |
| Particle object | object `+0x10` (subsystem 0x9f748) | 214013/2531011; arithmetic `>> 16` | not traced | no label found | pinned (lane: state field, multiplier only via the 214013 site count; review: multiplier and increment) |
| Weather object | object `+0x34`, step function 0x904a8 (caller imports `gei_Rain`) | 1664525/1013904223; returns the full new state | not traced | no label found | pinned (lane: state field, multiplier; review: multiplier and increment) |
| C library | `c_c++_shared` data 0x544c (static 1), shared by app and `engine_shared` | `s = s·1103515245 + 12345`; `(s >> 16) & 32767` | `srand(low32(UTimer::GetAbsolute()/1000))` at 0x1c0cc0 | no label found | pinned (app/clib); engine use not traced |
| Sound seed | sound data 0xc2e4 | candidate = `seed·1664525 + 1013904223`, **not stored back** | `LbTime_GetClock` | no | cited (unmerged) |

"No label found" rests on one scan: the application's string pool holds exactly
one `*Seed*`/`*Random*` identifier string (`mRandomSeed`). Unlabelled persistence
is not excluded.

## 2. Gaps closed in this lane

### 2.1 Substep order (pinned)

The catch-up loop in game-callback helper 0x1c1208 runs these steps, in this
order, for each 31 ms substep (`previous += 31; substep += 1` happen first,
before the world gate):

| Cadence | Site → target | Role (bounded) |
| --- | --- | --- |
| every | 0x1c230c → 0x9f748 | particle system (`PTCL:` strings), own 214013 LCGs |
| every | 0x1c2310 → 0x20688 | vehicle/kart subsystem (`GoKart Race Over`, `CRUNCH`, `TOOT!`), own 214013 LCG; its callees also draw from the world RNG |
| every | 0x1c2314 → 0xb2838 | RSE script-manager pass |
| `substep & 1 == 0` | 0x1c2330 → 0x63dd0 | flying-car/ambient pass (`FLY:` strings); 6 direct world-RNG draws |
| `substep & 1 == 0` | 0x1c2334 → 0xb7ac0 | reads the scheduler clock; role unqualified |
| `substep & 7 == 0`, cap < 3 | 0x1c23b8 → 0x10536c, or 0x1c23ec → 0x10565c | park turn (mode 0/2 direct, mode 1 wrapper) |
| `substep & 7 == 0` | 0x1c2424 → 0xb5da4 | runs even when the cap is spent; role unqualified |
| `substep & 31 == 0` | 0x1c246c → 0xbc1d4, 0x1c24b8 → 0xbc210, 0x1c24bc → 0x63d20 | audio controls from clamped counts (≤89, ≤100; zeroed in world state 4); role unqualified |

Before the even block and the turn block, the loop stores `previous` into two
globals (TOC −0x5e40 and −0x65e4). These are the phase origins of the
interpolation alphas below. The park-work counter is reset once per callback, at
0x1c27a4, after rendering.

Park turn 0x10536c (pinned): `mGameTick += 1` first; then, unless world state
(`+0x1da738`) is 4, it walks the live thing list. That list is newest first
(PPC-review §6), and the walk dispatches on thing type byte `+2` via 0xfa9b0;
guests are type handlers 0xeece4/0xef240. When `turn % 30 == 0` and further
guards hold, an extra block runs, ending at 0x1092bc. The fixed tail always runs
in this order: 0x1078cc (deferred thing removal), 0xd67f0 (world/economy and
calendar update), 0x10ce68 (action recorder/replayer).

### 2.2 Simulation rate versus render rate (pinned)

- The simulation rate is **31 ms of clock A per substep**, i.e. 32.26 substeps
  and 4.03 park turns per clock-A second at rate 1.0. Game speed changes how
  much clock A elapses per wall second (×0.25…×2). It never changes the
  substep length.
- **Once per callback, before** the loop: 0x1c22ac → 0xa6f70 refreshes the
  cached animation clocks (+16400/+16404/+16408). The cache is therefore the
  time at callback start, not per substep.
- **Once per callback, after** the loop, in this order: sound service 0xbb18c;
  advisor per-frame update 0x7434 (clock B, C-library `rand`); a walk over all
  scripts 0xb2aac; three interpolation passes with alpha =
  `(now − phase_origin) / {31, 62, 248}` (float constants at TOC
  −0x26a8/−0x26ac/−0x26b0); 0x483ac; **coaster motion manager 0x3864c**;
  `CMapWho::Render` (0x4d4c4); capture if flagged; `RenderSystem_FlipScreen`
  (0x4d4f0).
- Rendering is therefore decoupled from the simulation: a classic fixed step
  plus interpolation, with at most one render per callback and any number of
  substeps per callback, bounded by the 2000 ms backlog.
- **Exception: coaster motion is frame-coupled.** The tick 0x40650 integrates
  with the elapsed time of the cached clock-A timestamp since its last frame
  (PPC-rides §"Coaster schema…"), and 0x3864c is called only once per callback.
  A scan finds exactly one direct call, 0x1c262c, outside the substep loop. In
  the original, coaster physics therefore depends on the frame rate. Among the
  qualified post-loop calls it is the one frame-coupled path that is not
  replayable from substep inputs alone. The same block also calls 0xb2aac
  (per-frame walk over all scripts) and 0x483ac, whose roles are not qualified,
  so this is not a claim that no other frame-coupled simulation path exists.
- **Capture mode is the original's deterministic mode.** While TOC −0x5e24 is
  set, each callback holds clock A and advances it by exactly 31 ms
  (0x10ec60(…, 32); step 0x10ed10). The same flag calls the per-frame
  `Scr%05ld.tga` capture. Each frame then runs exactly one substep, and coaster
  motion sees a constant 31 ms delta. Clock B (advisor) keeps real time.

### 2.3 One shared gameplay RNG, plus local streams (pinned + scan)

- **The world RNG is the gameplay generator.** The scan finds 142 direct calls
  to 0x105328. Among the named consumers: guest creation 0xe7644 (10 draws),
  guest update 0xef240, staff candidate generator 0xf5b64 (10 draws), RSE RAND /
  FINDSCRIPTRAND (0xaf534), BUMP 0x2463c, TOUR/flying cars 0x63490/0x63dd0, and
  economy staff paths 0xf4900/0xf5928. Four inline advances (0x1056cc, 0x105848
  ×2, 0x109368) update the same field without calling the generator.
- **Writers of the field (scan, D-form displacement −0x58f8, non-TOC base, base
  register not followed):** seed store 0x104910; generator stores
  0x105344/0x105348; setter 0x105364; inline stores
  0x105760/0x105764/0x1058a0/0x1058a4/0x105934/0x105938/0x10598c/0x105990/0x109404/0x109408.
  Address taken only by the save (0x106014) and load (0x1069a4) paths. For
  `mGameTick` (−0x58f4): stores 0x104900 (reset to 0) and 0x1053a0 (increment);
  address taken only by save 0x105efc and load 0x10688c.
- **Reseeding by thing ID (pinned).** The setter stores the zero-extended u16 in
  r4. At 7 of its 8 direct callers, r4 is the halfword at `thing+0`, copied by
  0xfa9a4: 0xd86dc, 0xe79b8, 0xe8fd4, 0xeb160, 0xeb28c, 0xefa30, 0x1a9574. The
  same copy feeds the guest-phase test (`id & 3`, 0xeed18/0xeed34). The eighth
  caller, 0xd5b14, uses the object's u16 field `+0x214`. Guest creation
  therefore draws 8 values from the running stream (0xe7774…0xe78dc), resets
  the stream to the new guest's ID (0xe79b8), then draws twice more
  (0xe7a24/0xe7a44). The global stream after a creation is a function of that
  ID, not of earlier history.
- **Separate streams.** The scan of LCG multipliers finds 9 sites of 1664525
  (world generator, 4 inline world advances, 4 sites on object `+0x34` in the weather code) and 31 sites
  of 214013 (coaster, kart and particle objects). It finds no ANSI multiplier in
  the application. The C library's `rand` has 17 direct call sites (advisor
  0x6420/0x7780, coaster seed 0x54bf4, presentation code) and one `srand` site.
- **No RNG work in the loop body itself.** The loop range 0x1c22dc–0x1c24d0
  holds no direct call to the world generator, its setter, `rand`, coaster
  motion or 0xa6f70. All draws happen inside the subsystems listed in §2.1.
- **Original replay concept.** 0x10ce68 is an action recorder/replayer
  (`Start Action Recording`, `Replaying a timed event at time %d`,
  `Layout load in progress!  Next action in %d game turns`, plus an
  `ACTION_*` dictionary). It runs at the end of every park turn and decrements
  a countdown `+0x30` once per turn in its replay modes. The original thus
  indexes recorded actions by **park turns**. Its record format and modes are
  not traced.

## 3. OpenTPW divergences

| # | OpenTPW today | Original (Mac, this build) | Maps to |
| --- | --- | --- | --- |
| D1 | `FixedStepClock`: 60 Hz, 16-tick catch-up cap, drops the excess; `ParkCalendar` samples 248 ms turns from 60 Hz ticks | 31 ms substeps of clock A; turn every 8th substep; 2000 ms backlog drop; ≤3 turns per callback, later turn phases consumed without a turn | ECON-001 (this contract supplies the replacement) |
| D2 | `Level.Update`: `SimulationTimeScale × Time.Delta` into the clock; speeds ×2/×4 | Rate ×1.25 steps clamped to 0.25–2.0 on clock A; substep length fixed | ECON-004, UI-022 |
| D3 | All systems tick every 60 Hz step in the order economy sync → guests → ride → objects → park | Fixed order of §2.1: particles → vehicles → scripts → (even) ambient → (8th) park turn [tick++, things newest first, 30-turn block, removal, world/economy, recorder] | unregistered (proposed DET-001); guest order in GUESTS.md "approximations" |
| D4 | RSE: one slice per 60 Hz tick for every script (RSE-VM.md "Not original behaviour yet") | One manager pass per substep; ordinary scripts on `id & 7 == pass & 7` | unregistered; RSE-VM.md list |
| D5 | Guests: own SplitMix64 `GuestRandom` seeded with `Level.GuestSeed`, id order | Shared world LCG; reseed to the guest ID at creation; live list newest first | unregistered (GUESTS.md) |
| D6 | Economy: own SplitMix64 `DeterministicRandom`, saved as `RandomState` | Same shared world LCG (staff candidates 0xf5b64, staff RNG gates) | ECON-009, ECON-010 (formula IDs; the generator itself is unregistered) |
| D7 | `RideVM`: `new Random(seed)` or `new Random()` per VM; child VMs seeded from it | RSE RAND/FINDSCRIPTRAND/BUMP/TOUR draw from the one world RNG | RIDES (unregistered); PPC-rides RAND section |
| D8 | `Advisor`: `new AdvisorMouth(new Random())` | C-library `rand` in the per-frame update 0x7434 | ADVISOR-001 |
| D9 | `SoundEventSystem` advances `seed` on every draw | Sound seed never advanced by the bundle's choosers | AUDIO (unregistered; conflicts with unmerged phase 10) |
| D10 | `Extensions.RandomVector3` uses `Random.Shared` | presentation only; must never be reachable from simulation | n/a (guard) |
| D11 | Rides/objects advance by float `deltaTime` | Coaster motion uses variable per-frame elapsed ms (frame-coupled); clip frames from the cached clock | RIDES-001 |
| D12 | Save stores economy `RandomState` only | Original saves `mGameTick` and `mRandomSeed` (plus calendar fields) | unregistered (save format) |

## 4. DET-I implementation contract

### 4.1 Clock and tick

1. **Simulation time is an integer millisecond clock A.** Keep it as a 32-bit
   word, with the native signed `now > previous` comparison isolated in one
   function (see APPROX DET-004). `elapsed_A += trunc(rate × wall_ms)`,
   accumulated as in 0x127cd0. Pause freezes A and B.
2. **Fixed substep = 31 ms of clock A.** Per frame: if `(int)(now − previous) >
   2000`, set `previous = now − 2000`; then, while `(int)now > (int)previous`,
   run `previous += 31; substep += 1; body`. Reset the park-work counter after
   rendering. `det_model.catch_up` is the reference; port its vectors (1 ms gap
   → 1 substep, overshoot 30; 2000 ms → 65 substeps, turns at 8/16/24, capped
   at 32…64; 5000 ms → backlog drop to 2000).
3. **Body gate:** if `application_flag8 || !gameplay_flag1`, run the body;
   otherwise time and the counter still advance.
4. **Rate:** steps ×1.25/÷1.25, clamped to [0.25, 2.0], start 1.0. Do not map
   OpenTPW's ×2/×4 to anything except an explicit EXT label.
5. **Replay/headless mode = capture mode:** feed exactly 31 ms of clock A per
   frame (the original's hold). One frame then equals one substep, and
   frame-coupled paths (coaster motion) see a constant delta. The canonical
   replay is indexed by `substep_counter`, never by wall time.
6. Render once per frame after catch-up, with interpolation alphas
   `(now − previous)/31`, `(now − even_origin)/62`, `(now − turn_origin)/248`.
   Rendering must not mutate simulation state.

### 4.2 Update order

Per substep, in this order. Labels in brackets are this build's addresses:

1. particles [0x9f748] (presentation unless proven otherwise; keep its RNG
   separate),
2. vehicles/karts [0x20688],
3. RSE manager pass [0xb2838]: increment the pass counter, then run ordinary
   scripts with `id & 7 == pass & 7` and flagged (`+184`) scripts every pass,
4. if even: ambient/flying cars [0x63dd0], then [0xb7ac0],
5. if `substep & 7 == 0` and `park_work < 3`: `park_work += 1`, then a park turn
   for mode 0/1/2,
6. if `substep & 7 == 0`: [0xb5da4],
7. if `substep & 31 == 0`: audio-control updates (presentation).

Park turn: `tick += 1`; unless world state 4, walk the live thing list newest
first and dispatch on type; every 30 turns the guarded extra block; deferred
removals; world/economy/calendar update; input-log/recorder step.

Per frame after catch-up: sound service; advisor (clock B); per-frame script
walk; interpolation; coaster motion with the frame's clock-A delta (31 ms in
replay); render.

### 4.3 RNG classes

| Class | State | Step | Return | Seeding in DET-I |
| --- | --- | --- | --- | --- |
| `WorldRng` | `uint` | `s = s*1664525 + 1013904223` | wrapping abs (`int.MinValue` bits kept) | from the save/replay header. `Reseed(ushort thingId)` at entity creation/transition sites (§2.3) |
| `CoasterRng` | `uint` (one per process/world) | `s*214013 + 2531011` | `s >> 16` (logical) | one `ClibRng` draw at world init |
| `ObjectMsRng` | `uint` per kart/particle object | `s*214013 + 2531011` | `(int)s >> 16` (arithmetic) | unknown → DET-006 |
| `WeatherRng` | `uint` per weather object | `s*1664525 + 1013904223` | full `s` | unknown → DET-006 |
| `ClibRng` | `uint`, static initial 1 | `s*1103515245 + 12345` | `(s >> 16) & 32767` | from the header (the original uses timer ms) |
| `SoundSeed` | `uint` | none (candidate only) | `seed*1664525 + 1013904223` | from the header; never advanced by a draw |

Projections stay with each caller. RSE RAND is
`labs(labs(r >>> 1) % (int16(bound) + 1))`, and bound −1 is unsupported. BUMP
takes an unsigned remainder of `r`; TOUR uses `labs` then a signed remainder of
100. The coaster takes the high half modulo the eligible-car count. Test vectors
are in `test_det_model.py` (seed 0 → 1013904223; RAND seed 0 bound 10 → 6 then
8; coaster 12345 → 40352; C library 1 → 16838). `System.Random`, `Random.Shared`
and SplitMix64 must not be reachable from simulation code.

### 4.4 State to persist

Original import reads exactly these qualified fields: `mGameTick` (u32) and
`mRandomSeed` (u32, raw world-RNG state), plus the calendar fields
`mFunnyTimeStart`/`mSessionStart`/rate already proved in PPC-clock. OpenTPW's own
save format (EXT) additionally persists everything needed for exact
continuation: clock A value, `previous`, the substep counter, the manager pass
counter, park-work count, rate, pause state, both interpolation phase origins,
`CoasterRng`, `ClibRng`, `SoundSeed`, every `ObjectMsRng`/`WeatherRng`, the
live-list order, the input-log cursor and pending script deadlines. Restoring an
original save must not invent the non-saved streams silently: DET-I seeds them
from the header and tags the load (DET-005).

### 4.5 Canonical replay fields

Hash per park turn (and on demand per substep), in this order, little-endian:
`schema_version`, `scheduler_previous_ms`, `substep_counter`,
`manager_pass_counter`, `park_turn`, `world_rng_state`, `coaster_rng_state`,
`clib_rng_state`, `sound_seed`, `scale`, `paused`, `game_type_mode`,
`world_state`, `calendar_funny_start`, `calendar_rate`, `thing_table_digest`
(live-list order, each thing's ID, type and serialized fields),
`script_table_digest` (ID, PC, variables, deadlines), `economy_digest`,
`input_log_cursor`. The replay input log records `(substep_counter, action)`.
Actions apply at the start of a substep, before step 1; the original's own
recorder applies at park-turn granularity, which is a coarser subset of this.
Compare floats by bit pattern; no tolerance applies to canonical state.

### 4.6 What stays APPROX or unknown

Proposed new IDs. The register is not edited by this research lane.

| ID | Statement | Evidence needed |
| --- | --- | --- |
| DET-001 | Order inside subsystems we do not model yet (particles, karts, ambient, 0xb7ac0, 0xb5da4, the thirty-second block, 0x483ac, 0xb2aac) | their roles; whether particles/karts/ambient feed gameplay state |
| DET-002 | Initial seeds: the original seeds the world from `time(NULL)` and the C library from timer ms, so no original seed is "correct" | none possible; an EXT policy (explicit seed in the header) |
| DET-003 | Coaster motion is frame-coupled in the original; DET-I fixes it to 31 ms per substep-frame outside capture mode too | an original trace at different frame rates |
| DET-004 | Signed `now > previous` near 2³¹ does not terminate in the native loop; DET-I must pick a guarded policy | long-session runtime qualification |
| DET-005 | Original saves restore only `mRandomSeed`/`mGameTick`; post-load draw order (rides `76aaa62`) and the non-saved streams are reconstructed | load-path tracing of World/ThingArray readers |
| DET-006 | Seed sources and lifetimes of `ObjectMsRng` (kart/particle) and `WeatherRng` | their constructors |
| DET-007 | Reseed at 0xd5b14 uses object field `+0x214` (helper 0xc3304), meaning unknown | caller semantics |
| DET-008 | The 30-turn block's guards (0x1091ac/0x1091b8/0xc3684) and its second thing pass | their predicates |
| DET-009 | Recorder 0x10ce68: record format, modes 0/1/2, and the countdown clamp to 2 | reader 0x10d144 dispatch table |
| DET-010 | Windows/Patch 2 equivalence of every item here | PC binary or trace |
| DET-011 | `engine_shared` also imports `rand` and Toolbox `Random`; its draws share the C-library state | engine call sites |
| DET-012 | Sound-seed non-advancement rests on unmerged phase 10 | merge plus a re-pin on main |

## 5. Reproduction

```sh
python3 -I tools/ppc-analysis/lanes/det/det_evidence.py /path/to/mac-feral/bin
OPENTPW_PPC_BIN_ROOT=/path/to/mac-feral/bin python3 -I -m unittest discover -s tools/ppc-analysis/lanes/det -p 'test_*.py' -v
python3 -I tools/ppc-analysis/run_evidence_checks.py --mac-bin /path/to/mac-feral/bin
```

Tests: 33 in the lane. 11 need the PEFs; 8 of those are mutation tests that
perturb one expectation each (slice length, park cap, eighth mask, turn
interpolation divisor, world-RNG increment, a reseed site, substep call order, a
scan count) and require the witness to fail. A probe confirmed that a no-op
mutation is reported as a test failure, so these tests are not vacuous.
LLVM/Capstone disassembly was used only for transient inspection outside the
repository. No original bytes or listings are stored.
