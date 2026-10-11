# Review WALK-V: traced steering and the queue gate's walk terms (b505706)

October 10, 2026. Independent review of `b505706` ("Walk guests with the traced steering and derive the queue
gate's walk terms (WALK-I)") on `c6c19ea` (M3 acceptance). Spec: docs/reverse/WALK-plan.md (incl. §15) and
tools/ppc-analysis/lanes/walk/. No original instruction was executed except the isolated pure routines `0xffe38`
and `0xe6b28` in OpenTPW's own PPC interpreter (tools/ppc-analysis/oracle, `16bb38e`). Everything else is static
reading of the Feral Mac binary and OpenTPW builds in scratch worktrees. Pins live in
`tools/ppc-analysis/lanes/review/test_walk_v1.py`.

## Verdict: ACCEPT-WITH-FIXES

The port is faithful where it claims to be traced. The speed model is bit-exact against the original (see Oracle).
Gate, tests, smokes, register and evidence pass. Every M3 mutation that was caught at `c6c19ea` is still caught at
row level, once the v3/v4 suites are made to compile again (WV-1).

The gate change is a loosening, and it rests on approximations that the gate no longer names:

- HeadNotReadyBound: 194 / 50 → 281 / 65.
- Head check: up to +240 turns.
- W(p): +J(p).
- closest-to-bound in the jungle run: −69 → −310.

Fix WV-1..WV-6 and WV-12 before merging; all are gate, register or test fixes except WV-13, a contrived-geometry production bug.

## Answers to the gate questions

1. **Derived from the trace, not calibrated on OpenTPW's runtime?** Partly.
   - C = 9 comes from enumerating OpenTPW's *port* of the traced step. It was not fitted to the jungle run: the
     analytic floor slope, 65,536 / 7,733 = 8.47 updates per cell, is traced (`0xffe38` at s = 0.59).
   - The enumerated step includes untraced parts: WALK-002 (corner branches), WALK-003 (separation, see WV-5) and
     WALK-004 (routes). It also includes an untagged walkability model (WV-6).
   - So the bound holds for OpenTPW's walk. It holds for the original only as far as those parts match.
2. **Is the enumeration actually bounding?** For a single walk, yes: no counterexample in about 10⁹ adversarial
   walks, including 62-crossing, backward and folded routes. The head accounting is not sound for folded queues
   (WV-12). See "Adversarial enumeration".
3. **Is the W2 young-head skip a loosening that hides stuck queues?** Yes, by construction (WV-2).
   - `HeadYoung` is decided once, when the guest becomes head, and suppresses rule (a) for the whole streak. A head
     younger than 15 turns that never moves is never judged by rule (a).
   - Shops and toilets have no wait bound (`waitBound.judged = false`), so rule (a) is their only stuck-queue check.
   - Not triggered in the jungle run (`headNotReadyYoungHeadsNotJudged = 0` on all three queues). Arrival lanes make
     a head younger than 15 turns practically impossible there. That makes the skip a fail-open path with no benefit.
4. **Should the residual uncertainty be registered?** Yes (WV-4).
   - Retiring GATE-004 is right: its specific claim, OpenTPW speed × 0.7, is gone.
   - Retiring GATE-005 replaces a check that was registered as *stricter* than the derivation with the derived,
     looser one. That is a loosening under the "gates may only become stricter" policy. It needs an explicit
     owner sign-off and a register entry rather than a silent retirement.

## Findings

- **WV-1 (high, process; not a merge blocker after the fix).** Mutation suites v3/v4 do not compile at `b505706`.
  - `test_m3_gate_v3.py:132-134` and `test_m3_gate_v4.py:81-83` assign `Guest.X/Y`, which WALK-I made read-only
    (`Guest.cs:69-70`). Every v3 (21) and v4 (8) mutation errors in `setUpClass`: `CS0200` at the instrumented
    `M3Gate.cs:1657`.
  - The commit lists the suites as "Not-tested", but they were *unrunnable*, not merely unrun.
  - Shim: `test_walk_v1.MutationsWalkRound3/4` hold the guest through `Navigation.X/Y` (velocity zeroed).
  - Result:
    - v1 26/26, v2 10/10, v4 8/8 pass, all mutations still caught.
    - v3: 3 tests pin pre-WALK-I numbers. W_max 8021 → 8045, the 194/50 head bound, and `hold-slots-longer` now
      *fails* (179 > 178, stricter).
  - **No mutation that failed the gate at `c6c19ea` passes at `b505706`.**
  - Fix: port the hooks in v3/v4 (or adopt the shim) and update the three pins.
- **WV-2 (medium).** A young head is permanently exempt from rule (a) (`M3Gate.cs:943-953`).
  - Scenario: a guest under 15 turns old becomes head of a shop queue and freezes. Rule (a) never fires and no
    wait bound exists, so the queue row passes.
  - The same fail-open applies when the head has no track (`is not { } headTrack`).
  - Fix: judge the head from turn max(becameHead, FirstSeenTurn + 15), or add 15 to its bound. Treat a missing
    track as a failure.
- **WV-3 (medium).** Rule (a) lost sensitivity, and the loss is hidden by another rule.
  - `head-held-250-turns` was caught by rule (a) at `c6c19ea` (bound 194). At `b505706` its streak is 259 ≤ 281:
    zero rule (a) violations.
  - The row still fails, but only through the BOUNCE head wait bound W(0) (179 > 178), which shops and toilets do
    not have.
  - Pinned in `test_walk_v1.MutationsWalkRound3.test_head_bound_is_n_plus_four_cells`.
  - Fix: either accept it explicitly, or tighten rule (a) with the head's actual remaining walk. The head check
    already computes it (`HeadWalkTurns`); for most heads it is 0 instead of N × C + w₂.
- **WV-4 (medium, policy).** The loosening is unregistered.
  - `HeadNotReadyBound`'s derivation (`M3Gate.cs:1188-1199`) and the W(p) derivation carry no `[APPROX:]`.
  - `M3GateApproximations` keeps only GATE-001, yet C = 9 rests on WALK-002..004, the zero separation (WV-5) and the
    route-only walkability (WV-6). By the commit's own words it is "a bounded enumeration, not a proof".
  - Fix: register e.g. GATE-006: "C = 9 / J(p) / head walk from a bounded enumeration of OpenTPW's step (WALK-002..004,
    WV-5, WV-6); not a proof for the original". Tag it in `HeadNotReadyBound`, `JoinWalkTurns` and `HeadWalkTurns`,
    and record the GATE-005 loosening as an owner decision.
- **WV-5 (medium, fidelity register).** WALK-003 cites the wrong routine, and the separation force is traced and
  nonzero.
  - Separation passes the vector at data `0x7088` to `0x10087c`, which relocates to `0xfd5f4`, not `0xfd8a8`
    (`0xfd8a8` is the `0x7078` vector of another behaviour, whose query `0x100aa8` is a bare `blr`).
  - `0xfd5f4` is readable:
    - skip self;
    - off = self − other, d = octile(off);
    - add FixDiv(off, d) when d ≠ 0;
    - the neighbour query is a cell-box scan with a type filter, a (2r)² Euclidean test and at most 5 neighbours
      (`0x1008f0..0x100a58`).
  - Queue slots are about 0.25 cell apart, inside the neighbour radius, so the original pushes where OpenTPW does not.
  - In production the 0.1-weighted term multiplies a constant (0, 0) (`GuestSimulation.cs` `Step`, no
    `opposeSeparation`; `GuestNavigation.cs:297,312-314`).
  - The enumeration's "opposing" mode uses a nonzero push of max-force magnitude. That likely covers the magnitude,
    but not neighbour-induced wall contact.
  - Fix: retag WALK-003 at `0xfd5f4`, narrow its evidence to `0x10087c`'s type filter, and port it or keep the
    zero force as an explicit approximation.
- **WV-6 (medium, untagged approximations).**
  - (a) Walkability is "on this walk's route or AlsoOpen" (`GuestNavigation.cs:119-132`, `GuestSimulation.cs:439`).
    The original tests cell edge flags with mask `+180` (`0xd768c`) and commits the step through a transition call
    (`0xfee3c..0xfee58`). Adjacent path cells become walls.
  - (b) The queue walk's "cell ahead of the destination and behind the start are walkable" (`GuestSimulation.cs:847-861`)
    appears only in WALK-plan §15 prose.
  - (c) The map-edit reroute is missing: block stamp `0xd7e54` against `+72`, failure flag `+184`
    (`0xfe650..0xfe6f8`).
  - (d) `OffGrid` walking (no walls) is untagged.
  - (e) The progress history compares raw `remaining >= previous` and skips the first step. The original computes
    progress = ONE − FixDiv(rem, `+160`) ≤ (`0xfeed8`) and resets `+172` at route setup (`0xff6fc`).
  - All of these feed C = 9. Fix: tag them under WALK-00x, or fold them into WALK-002/004 with accurate wording.
- **WV-7 (low).** avoid_walls returns after the first triggering edge (+x, −x, +y, −y: `0xfde78`, `0xfdee4`, `0xfdf64`,
  `0xfdfe0` → `b 0xfe5f0`). C# sums both axes (`GuestNavigation.cs:216-247`). This differs near corners; WALK-002's
  "per axis" wording does not disclose it.
- **WV-8 (low).** The toilet term `+194` is written in the phase-gated needs block.
  - In the original, `0xeed40` skips `0xef0bc..0xef0e8` unless guest phase == counter & 3. It compares
    trunc(toilet) & 0xff unsigned > 80 (`0xef0cc`).
  - C# writes it every turn with a signed compare (`GuestSimulation.cs:296-297`). WALK-plan §4 ("every update")
    contradicts its own §2 W1.
- **WV-9 (low, determinism/merge).** Schema 6 is claimed three times.
  - WALK-I (`0x714AAFDCEC083403`), ANIM-I `b84bf58` (`0x0BC982AB91BE9A28`) and COAST-I `02712e0`
    (`0x5E1198F70F3F2B55`) all bump `SchemaVersion` 5 → 6 with different pins.
  - Whichever lands second must bump to 7 (third to 8) and re-pin.
- **WV-10 (low, merge).** The register sentence miscounts after a merge.
  - WALK-I and SAVE-I `2ddf376` both change "ten configured C# registers" to "eleven".
  - Merged, git keeps "eleven" without a conflict, but there are twelve. `--check` cannot see it because the document
    is regenerated from the same string.
- **WV-11 (nit).** `M3Gate.cs:1271` cites `WalkTests.QueueWalkTermIsDerivedByEnumeration` (the reduced test) as the
  §15 enumeration. The full run is `QueueWalkTermFullEnumeration`.

## Oracle (0xffe38, 0xe6b28)

Run with the `16bb38e` interpreter against the verbatim C# `GuestSpeed` in a scratch .NET 10 console.

| Routine | Inputs | Mismatches | Edge cases covered |
| --- | --- | --- | --- |
| Setter `0xffe38` | 668 | 0 | ±0, ±∞, NaN payloads, denormals, ±1e30, ±3 ULP around 0.0499 / 0.0249 / 0.05 / 0.59 / 0.6 / 2.0 |
| Smoothing `0xe6b28` | 632 | 0 | boost 0, 99–101, 660–663, 32767/8, 65534/5; s = denormal, FLT_MAX, ∞, NaN |

An unfused formula gives 217 mismatches on the same inputs, so the comparison does discriminate. This confirms:

- the base table {60, 80, 100, 120, 140} (data `0x4105e`, drawn mod 5 at `0xe48a4..0xe48d0`; the stream differs, DET-014);
- `fmadds` at `0xe6b9c` then × 0.25;
- the u16 boost × 99 / 100;
- floor 655 and the cap at 2.

The steering constants and operators check out: 0.9 / 0.5 / 0.1, truncation, arrival < 20971 (strict), waypoint
< 26214, the d/2 ramp, 6-of-15 stuck, bytes / 256. The stand point matches (rotation map `0xde4c8..0xde4fc`, 255.0
`fctiwz`), so the QUEUE-007 closure holds. WALK-001 (boost increments) is correctly registered.

## Adversarial enumeration

A separate harness replicated the production queue walk: `SetQueueDestination`, the route + ahead/behind open set,
`TrackQueueCell`, and route replacement on reroute. It ran about 10⁹ walks of random sampling and hill-climbing:

- routes up to 62 crossings: straight, zig-zag, snakes and switchbacks with adjacent rows, spirals, random bends;
- forward and backward walks;
- every depth and lateral byte;
- corner and edge starts with stale velocities up to the max speed in any direction;
- separation off and opposing;
- s from 0.59 to 2.0, plus a dynamic mode with real smoothing and the toilet term.

Results:

- **Single walk: k × 9 + 15 held, with no counterexample.**
  - Worst margin −4 at k = 0 and 1 (s = 0.59): 11 vs 15, 20 vs 24. It widens with k: −43 at k = 62 (530 vs 573).
  - Slope 8.55 per cell straight, 8.0–8.3 cornered.
- **Floor:** s < 0.59 is impossible for a guest at least 15 turns old.
  - Base 60 gives s₁₄ = 0.58931 and s₁₅ = 0.59198.
  - The toilet term is 0 or 25 and the boost is ≥ 0 (a u16 wrap resets it to 0). The minimum over 20,000 random
    histories was 0.59396.
- **Redirect chains** (up to 5 redirects, 6 walks): N × 9 + 30 held with margin **0**, e.g. 66 vs 66. The two-walk
  premise rests on the state machine.
- **WV-12 (medium, soundness of the head check).** `HeadWalkTurns` (`M3Gate.cs` `HeadWalkTurns`) charges
  `QueueCellIndex × C + w₂`.
  - A guest that starts within about 0.05–0.2 cell of an edge bordering a non-consecutive route cell can hop across
    the queue's fold. Examples: a join cell next to a fold, adjacent snake rows.
  - Its index then drops while its remaining walk stays long. Reproduction: join cell at (65534, 1), velocity
    (6619, −3997), s = 0.59, turns `[0,0,0,0,0,0,0,1,1,0,0,0,0,0,0,0,0]`, pos 5. The walk takes 161 updates against
    a charged 30.
  - An undercount makes the check *stricter* (a false FAIL, not a hidden stall), but the derivation is unsound for
    folded queues. The M3 jungle queue is not folded this way.
  - Fix: charge the remaining route length (N − slot cell) rather than the current index, or restrict the scope to
    unfolded queues as W3/W4 do.
- **WV-13 (medium, production bug, contrived geometry).** A guest that cuts a corner into a queue cell and drifts back
  to the join cell keeps its old `QueueCellIndex`.
  - A redirect then builds a route whose first hop is not 4-adjacent (`SetQueueDestination`, `from` from
    `IndexOfCell` / `QueueCellIndex`). The guest is fenced in and never arrives.
  - Reproduction: spiral queue with turns `[0,-1,0,0,-1,0,0,0,-1,0,0,-1,-1,0]`, startDir 3, entDir 0, joinPick 1,
    start (65495, 65534), velocity (12728, 14983), s = 1.5, redirects 4→0, 13→0.
  - Fix: on the join cell use `from = cells.Count`.
- Stand points at edge or corner bytes break the walk (stand = (171, 0) never arrives). This is outside the claim:
  the gate scopes it with W4, and the default (127, 127) has margin −8.

The per-walk term is bounding for the shapes tried. The enumeration in §15 did not cover backward walks, folded
queues or the `QueueCellIndex` accounting (WV-12, WV-13). Record that in §15.

## Behaviour

- Jungle boards 275 instead of 286. The shop goes from 1,165 to 648 boardings, the toilet from 161 to 156.
  - This is plausible per the traced stand point: each called guest now walks into the entrance cell
    (≥ 8.5 updates per cell at the floor, plus braking) instead of stopping at the front cell's edge.
  - The shop at about 11 turns per boarding is consistent with w = 10 to 20.
  - Not verifiable against the original's runtime.
- Head-not-ready maxima are 102 / 11 / 12 against bounds 281 / 65 / 65. The head-to-boarding maximum (108) is
  unchanged.

## Merge readiness (trial merges, aborted)

| Onto | Conflicts | After resolving |
| --- | --- | --- |
| fork main `8a000d8` | `docs/FIDELITY-REGISTER.md` only (generated) | Builds 0 errors, register 197; tests without assets 1082 / 273 / 0 (the `0x714AAFDCEC083403` pin holds); `--m3-gate` 16 / 0 / 0, exit 0 |
| SAVE-I `2ddf376` | `tools/fidelity_register.py` (`REGISTERS`: keep WALK and SAVE), `docs/FIDELITY-REGISTER.md` | WV-10 sentence |
| ORACLE-F `447b4c7` | `docs/FIDELITY-REGISTER.md` only | — |

No `.cs` conflicts. SAVE-I touches only `AdvisorScoreQueue.cs` among the guest/queue files. Schema: WV-9.

## Numbers

| Check | Result |
| --- | --- |
| Release build (SDK 10) | 0 errors |
| Tests without assets | 1052 / 267 / 0 |
| Tests with assets | 1234 / 85 / 0 (46 min under load ≈ 37) |
| `--m3-gate` × 2 | 16 / 0 / 0, exit 0, JSON identical apart from `wallSeconds` |
| Native smokes (osx-arm64 Release) | Front end 531 frames, sandbox 1073 frames, `--load-original-level jungle` 4664 frames: all passed |
| Evidence runner (`--mac-bin`, `--pc-data …/theme-park-world-patch2/data`) | OK, 15 suites, 989 tests, 181 skipped |
| `fidelity_register --check` | 187 |
| CRLF | Existing files keep their line endings; 4 new files LF like their neighbours |
| `git diff --check` | Clean |
| Static review lanes v1–v4, queue, path, det | OK |

The `theme-park-world` root lacks the lower-case `levels/` layout, so `--pc-data` must point at
`theme-park-world-patch2/data`.
