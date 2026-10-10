# BOARD-R: boarding latency of BOUNCE rides and the queue gate spec

Lane: BOARD-R (research). Input: the M3 gate ends at 15 pass / 0 fail /
1 unresolved. The open row is `queues.no-stuck-queue`. It reports the
QUEUE-plan §9b wait bound with the boarding latency τ = 0 s
(`[APPROX:GATE-003]`). This lane traces the boarding path of the Feral Mac
`SimThemePark.data` (SHA-256 `04809cd4…e295f5`) and the four BOUNCE scripts of
the PC corpus. From those rules it derives a wait bound and τ_max. It names the
two terms that cannot be derived, and specifies the gate assertion.

Reproduce (private assets stay outside Git):

```sh
python3 -I tools/ppc-analysis/lanes/board/board_evidence.py /path/to/mac-feral/bin --pc-data /path/to/Data
python3 -I tools/ppc-analysis/run_evidence_checks.py --mac-bin /path/to/mac-feral/bin --pc-data /path/to/Data
```

Confidence follows QUEUE-plan. **High**: an identity-pinned instruction or
table entry whose operation was interpreted. **Medium**: a role inferred from
callers or data. **Blocked**: not derivable from traced rules. All results
are static. Nothing original was executed.

## 1. Result

For Belly Bounce (jungle, id 1100, CAP 5, DUR 30 s), in park turns (T = 248 ms):

- **Per-boarding latency** H = 21 + w + w₂ turns. The 21 turns are traced.
  w is the walk to the stand point (state 13) and w₂ the new head's walk to
  slot 0 (state 12). Both are **blocked** in the original (section 6).
- **Slot hold** R = 121 turns (30.008 s), from BOUNCE to the UNBOUNCE that frees
  the slot, under the nominal clock (section 5).
- **Wait bound** for a guest that joins at 0-based position p:
  `W(p) ≤ (p + 1)·H + (⌊p/CAP⌋ + 1)·R + 1` turns.
- **τ_max** in the §9b form `W(p) ≤ (⌊p/CAP⌋ + 1)·(DUR + 1 s + τ)`:
  `τ_max = CAP·H + R + 1 − (DUR + 1 s)/T` = **102 + 5·(w + w₂) turns =
  25.296 s + 1.24 s·(w + w₂)**.
- With OpenTPW's own walk model (w = 2, w₂ = 6, section 8): H = 29 turns,
  τ_max = 142 turns = **35.216 s**, and W_max (p = 99) = 5321 turns = **1319.608 s**.

This bound is loose. It assumes every boarding pays a full handshake *and*
every CAP-th boarding pays a full hold. A turn-by-turn model of the traced
rules (section 7.3) never exceeded it, and reached at most about 0.6 of it.

## 2. Assumptions (bounded claims)

- **A1 nominal clock.** Every script slice and park update reads a scheduler
  `now` on the 248 ms turn grid. The scheduler does 8 substeps of 31 ms per park
  turn; the park update runs on phase 0 mod 8, and an ordinary script runs on
  the pass whose ID matches the counter's low three bits (PPC-clock;
  `0xb28bc/0xb28c0` mask both to 3 bits). The real game samples a live clock
  in a catch-up loop, so its slices jitter. Section 5's release timing holds
  only on the grid. OpenTPW's fixed-step clock is this nominal model.
- **A2 cadence.** Every live guest runs its state handler, and the ride its
  update, once per park turn (QUEUE-plan §6; `[APPROX:QUEUE-016]`). The
  live-list eligibility flags are not traced.
- **A3 open ride.** For the whole wait, the ride is open and unbroken (ride
  `+408 == 0`, `VAR_RIDECLOSED` and `VAR_BREAKSTAT` are 0), its exit is
  connected (`0xeeb30` succeeds), and DUR/CAP do not change. The closed and
  broken branches of the script (`FORCEUNBOUNCE`, `WAITANIM 10`) are not timed.
- **A4 speed bias.** Script `+192` keeps its default of 50, so the script speed
  is 1. No BOUNCE script uses `TURBO`.
- **A5 head at its slot.** H assumes a new head already stands at its slot. A guest that joins an empty or short queue first walks from the join cell to slot 0 (up to 24 cells); that walk is not a term of W(p), so W(p) for small p is not an upper bound in that case (review GATE-V4 B2; the gate registers it as GATE-005).

## 3. The host handshake (high)

The table runs in order, one park turn per guest or ride update.

| Step | Code | Rule |
| --- | --- | --- |
| Call | ride update `0xe0a8c` → (`+408 == 0`) `0xe1864` → `0xe1404` | `LETMEON == 0`, `ONRIDE < CAP`, RUNNING gate bypassed by RunsContinuously, nobody pending, head in state 11 at `+497 == 0` → head `+504 = 1`, pending `+104 = head` (QUEUE-plan §6) |
| Notice | state 11 step 1 (`0xed2c8 → 0xe0620`) | next state-11 update → destination stand point (`0xde1d8`), state 13 |
| Walk | state 13 `0xed7e0`: `bl 0xe6454` at `0xed808` | **one navigation step per update** (`0xe6488 → 0xffd2c → 0xfec9c`). Result 0 = arrived. 2 is logged and treated as arrived (`0xed80c`). 1 = still walking |
| Offer | same update, on arrival: price check `0xea5a4`, then `0xe03d0` (`0xed94c`) | rejects when ride `+408` is 1 or 4 (`0xe03f0`); checks pending == guest (`0xe045c`); clears pending (`0xe051c`); reads variable 0 (`0xe0528`). If 0, **writes the guest id into LETMEON** (`0xe0544`, `0xe0550 → 0xb57d4`) and the guest enters state 14 (`0xed990`). Otherwise it re-queues (`0xed9a0 → 0xee604`) |
| Consumed | state 14 = table entry `0xef4f8` (data `0x41314 + 56`) | `0xe05c8` reads variable 0 (`0xe05e4`) and compares it with the queue head `+56` (`0xe05ec`). Once they differ, the guest is unlinked (`0xef548 → 0xdcebc`) and enters state 16 (`0xef570`) |
| Release | ride update `0xe1864`, after admission and after the breakdown variable 7 (`0xe189c`) | reads variable 1, LETMEOFF (`0xe1964`). `0xeeb30` places the guest at the exit appear point (`0xeebc0 → 0xde1d8`) and sets state 15 (`0xeec80`). On success, LETMEOFF is set to 0 (`0xe1a14..0xe1a1c`) |

Consequences:

- Only the walking guest writes LETMEON, and only when it is 0. The call
  requires LETMEON == 0, so a call is never re-queued in the open-ride case.
- The queue head stays in the list until the script consumes LETMEON. The next
  head cannot be called before that, so boardings are **serial**.
- `ONRIDE` is the count the script stored in its last release slice (section 4).
  Right after a BOUNCE it is one too low, so the host can call the next guest
  while all slots are taken. That guest then waits at the stand point for a free
  slot. This makes boarding earlier, never later.

## 4. The BOUNCE script loop (high, corpus + binary)

`jungle/rides/bouncy.wad:Bouncy.RSE` (SHA-256 `7f32699c…8677a35`, time slice
50, bounce size 10). Open-ride loop (word index, opcode, operands):

```
  20 BOUNCING VAR_RUNNING      ; release slice continues here after BRANCH @20
  22 JSR @193                  ; scream level, RETURN at 207
  24 BOUNCING VAR_ONRIDE
  26..43 LOOPANIM by ONRIDE
  46 WAIT 500                  ; release slice ends here; admission slice resumes here
  48..54 TEST VAR_RIDECLOSED / VAR_BREAKSTAT ; closed/broken branches (A3: not taken)
  56..75 optional wear EVENT (RAND)
  79 BOUNCING VAR_TEMP
  81 CMP VAR_CAPACITY VAR_TEMP ; 84 BRANCH_PV @88 ; 86 BRANCH @99
  88 CRIT_LOCK ; 89 TEST VAR_LETMEON ; 91 BRANCH_Z @99
  93 BOUNCE VAR_LETMEON VAR_DURATION
  96 COPY VAR_LETMEON 0
  99 CRIT_UNLOCK               ; admission slice ends here
 100 TEST VAR_LETMEOFF ; 102 BRANCH_NZ @20
 104 UNBOUNCE VAR_LETMEOFF ; 106 BRANCH @20
```

Binary rules used:

- `CRIT_UNLOCK` (case 2, `0xaf5e0`) clears the critical flag **and the
  remaining budget** (`stw 0,+152` at `0xaf5e8`), so it ends the slice. Both
  paths out of `@84` reach `@99`.
- `WAIT` (case 44, `0xb0658`). On its first encounter it stores
  `now + trunc(500/speed)` at `+160` (`0xb06f8`), rewinds two words (`0xb0700`)
  and clears the budget (`0xb0708`). On later slices it resumes once
  `now ≥ deadline` (unsigned, `0xb067c`; clears `+160` at `0xb0688`).
  Otherwise it rewinds and yields again (`0xb0698/0xb06a0`).
- The manager reloads the budget from the header slice (`+148 → +152`,
  `0xb28e0/0xb28e4`). It decrements the budget only when the critical flag is
  clear (`0xb2904`).

`bounce_loop` enumerates every open-ride path. Both closed-ride and
broken-ride guards take only their fall-through. JSR is followed into the
subroutine. Each path is checked to end where expected: 12 admission paths
end at the CRIT_UNLOCK `@99`, 12 release paths end at the WAIT `@46`, and the
longest slice is 23 instructions (≤ 50). A WAITANIM-class wait on the loop
path would make the result *untraced* rather than timed.

So an iteration is: **slice A** (resume, admission, yield), **slice B**
(UNBOUNCE, ONRIDE update, WAIT set), then 2 waiting slices (248 and 496 ms are
both < 500). Slice A comes back after 3 × 248 = 744 ms. The **loop period is
P = 1 + ⌈500/248⌉ = 4 turns**, and B always follows A by one turn.

All four BOUNCE scripts in the corpus have this shape:

| Theme | Script (SHA-256 prefix) | Ride id | CAP | DUR | RunsContinuously | P | R (turns) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| jungle | Bouncy.RSE `7f32699c` | 1100 | 5 | 30 | 1 | 4 | 121 |
| space | Bouncy.RSE `4fd934f7` | 3114 | 6 | 30 | 1 | 4 | 121 |
| fantasy | Jelly.RSE `c0150775` | 4104 | 3 | 10 | 1 | 4 | 41 |
| hallow | Brainb.RSE `cf82eaae` | 2110 | 5 | 10 | 1 | 4 | 41 |

## 5. BOUNCE slots and the nominal hold (high for the rules; A1 for the timing)

- **BOUNCE** (case 72 `0xb14c8` → `0xadf40`) takes the first free 16-byte slot
  of the script's table. The table is at `+40` and has `+100` slots, the
  header bounce size (`0xadf6c/0xadf80`). The slot gets visitor `+0`,
  deadline `now + 1000·dur` at `+8` (`0xadfc8`), and start `now` at `+12`
  (`0xadfd8`). The bouncing count `+108` goes up by one (`0xadfe0/0xadfe4`).
  BOUNCE has **no host side effect**. The guest's boarding is the state-14
  test of section 3.
- **UNBOUNCE** (case 73 → `0xae020`) scans the slots in index order. It
  releases the first slot with `deadline < now` (`0xae088`) and
  `trunc(((now − start) mod 1000) / 200) == 0`. The division uses the magic
  numbers `0x10624dd3 >> 6` for /1000 and `0x51eb851f >> 6` for /200
  (`0xae040..0xae0b0`). It clears the slot and lowers `+108` by one
  (`0xae0c0/0xae0cc`). It releases **one rider per call**.
- **BOUNCING** (case 75) returns `+108` (`0xb159c`).

Under A1, BOUNCE runs in slice A at time t. UNBOUNCE polls run only in later
B slices, at t + 248·(4j + 1). The slot is freed at the first j with
`248(4j+1) > 1000·DUR` and `248(4j+1) mod 1000 < 200`:

- **DUR ≤ 30: R = 4·DUR + 1 turns** (0.992·DUR + 0.248 s). DUR 30 gives 121
  turns = 30.008 s. DUR 10 gives 41 turns.
- **DUR 31..60: R = 529 turns** (131.192 s). Polls drift by −8 ms per
  iteration against the 1 s window, and the window comes back only after 125
  iterations.

This refines QUEUE-plan §7 ("release within 1 s after the deadline"). That
holds per poll, but polls are 992 ms apart and phase-locked on the grid. The
DUR ≥ 31 value is a property of the exact grid. With real-clock jitter the
window is hit at an unpredictable time, so no bound is claimed for it.
Belly Bounce's InitDuration of 30 is the largest DUR with the short hold.

Each iteration boards at most one rider (one A slice) and releases at most one
(one B slice). Riders therefore become due in distinct iterations. The host
clears LETMEOFF on its next update (≤ 1 turn), before the next B slice (4 turns
later), so releases never contend.

## 6. The walk terms are blocked (exactly these two)

- **w**: updates in state 13, from the call to arrival at the stand point.
- **w₂**: updates in state 12 for the new head to reach slot 0. Under the
  gap ≤ 2 rule this is at most 3 slots, which is ≤ 0.76 cell along the cell.

Why they block. The guest moves one **steering step** per update. `0xfec9c`
sums the behaviour forces, truncates them to navigation `+24` (`0xfed0c`),
adds them to the velocity, and truncates the velocity to `+28` (`0xfed98`).
`+28` is a *maximum*: `trunc(min(s, 2)·0.2·65536)`, at least 655 (PPC-guests).
There is no traced *minimum* speed, because arrival and steering can slow
the guest below it. The step count to a target is therefore not boundable
from static rules. The distance is only partly known. `0xde1d8` puts the
stand point in the entrance cell `+50` at the fractional EntryCellStandPos.
Belly Bounce comments that out, and the jungle `Rides.sam` default is
(0.5, 0.5), the cell centre (medium: assumes the catalog layering). Slot 0
lies in the neighbouring front cell, but the sub-cell axis of the slots
(`0xdde74`) is untraced. The distance therefore lies between 0.5 and about
1.6 cells, and the missing speed floor still blocks a step count.

OpenTPW's walk is not a traced substitute either.
`GuestSettings.WalkSpeedCellsPerSecond` is marked "approximation: no original
source". The stand point is `[APPROX:QUEUE-007]` and the slot axis is
`[APPROX:QUEUE-006]`. Under this lane's rule (use the implementation only if
it is traced), **w and w₂ are the only terms that keep τ from being fully
derived**. Everything else in H, and all of R, is traced.

## 7. Derivation

### 7.1 Per-boarding latency H (turns)

H runs from the later of (previous LETMEON consumed, a slot freed) to the next
BOUNCE.

| Term | Turns | Source |
| --- | --- | --- |
| previous guest notices consumption, leaves the list | 1 | state 14 `0xef528`, `0xef548` (A2: its next update) |
| new head's move-up delay at gap ≤ 2 | 2 | `trunc(1.2 × recorded)` with recorded ≤ 2 (`0xef8ac`, data `0x55a0`); decrement `0xed4e4` while gap ≤ 2 (`0xed4a4`) |
| `0xee604` sets state 12 | 1 | `0xed4c0` on the update after the delay |
| walk to slot 0 | **w₂** | state 12 `0xef4b8 → 0xe6454` (**blocked**) |
| one interlude | 11 | state 8 restores when turn > `+520 + 10` (`0xef6d8`). A second needs turn − `+520` > 30 (`0xed5a0`). The rest of the head-ready phase after a restore (3 + w₂ + 1 turns) fits in the remaining 19 turns when w₂ ≤ 15 |
| ride update calls the ready head | 1 | `0xe1404 → 0xee794` (live-list order) |
| head notices the call | 1 | state 11 step 1, `0xed2c8` |
| walk to the stand point; LETMEON written on arrival | **w** | state 13 (**blocked**), `0xe03d0` |
| script's next admission slice | P = 4 | section 4 |

**H = 21 + w + w₂.**

Slot freed. A slice-B release updates ONRIDE in the same slice, and the
host's next admission check is ≤ 1 turn later. That is covered by "ride update
calls". A guest called early (stale ONRIDE) is consumed at the next A slice,
3 turns after B, which is ≤ H.

### 7.2 Wait bound

B_n is the n-th BOUNCE counted from the moment the guest joins (time J).
Boarding n needs (a) the previous consumption and (b) a free slot. For n ≤ CAP
the slot frees from the riders on board at J, at most R after J. Otherwise it
frees from boarding n − CAP, at most R after it. So:

`B_n ≤ max(B_{n−1}, F_n) + H`, with `F_n ≤ J + R` for n ≤ CAP and
`F_n ≤ B_{n−CAP} + R` otherwise.

Induction gives `B_n ≤ J + n·H + ⌈n/CAP⌉·R`. The guest at position p is
boarding p + 1. It leaves the list ≤ 1 turn after its own BOUNCE. Therefore:

`W(p) ≤ (p + 1)·H + (⌊p/CAP⌋ + 1)·R + 1` turns.

A guest ahead that leaves the queue without boarding costs at most the
head-ready part of H and uses no slot, so this bound covers it too. Because
`p + 1 ≤ CAP·(⌊p/CAP⌋ + 1)`, the §9b form holds with
`τ_max = CAP·H + R + 1 − (DUR + 1 s)/T`.

Belly Bounce (CAP 5, R 121, DUR + 1 s = 125 turns):
`τ_max = 5(21 + w + w₂) + 122 − 125 = 102 + 5(w + w₂)` turns.

### 7.3 Model check

`board_evidence.simulate` models states 11, 8, 12, 13 and 14, the ride update,
and the BOUNCE loop (slice A/B with the WAIT rule and the UNBOUNCE window)
turn by turn. An adversary chooses:

- the live-list order of new guests;
- whether the script slice runs before or after the park update;
- the loop phase;
- interludes whenever allowed;
- the walk lengths.

A move-up walk costs w₂ per slot. Over 16 test runs (more than 200 boardings),
every wait was within W(p), and the largest ratio was above 0.4. In ad-hoc
runs during the trace (1,600 configurations), the worst non-walk latency
from the later of the previous BOUNCE and a slot release to the next BOUNCE
was 18 turns, against the derived 21. A bound without the hold term is
violated (test).

## 8. Gate spec for `queues.no-stuck-queue`

Scope: attractions whose script passes `bounce_loop` and whose `.sam` has
RunsContinuously 1 and DUR ≤ 30. That is all four BOUNCE rides at upgrade 0,
including Belly Bounce. Other attractions keep the existing §9a FAIL rules,
and their wait bound stays UNRESOLVED (section 9).

Runtime inputs (no new constants):

| Symbol | Value (Belly Bounce) | Read from |
| --- | --- | --- |
| T | 248 ms | `ParkCalendar.TurnMilliseconds` |
| CAP | 5 | `RideVisitorBridge.Parameters.Capacity` (= `VAR_CAPACITY`) |
| DUR | 30 s | `RideVisitorBridge.Parameters.Duration` (= `VAR_DURATION`) |
| P | 4 | `1 + ⌈WAIT operand / T⌉`. WAIT operand 500 from the loop WAIT of the ride's script (`RideScriptAnalysis` listing). The gate may also pin it to the Bouncy.RSE SHA above |
| R | 121 | `R(DUR) = 4·DUR + 1` for DUR ≤ 30 (section 5); out of scope otherwise |
| H₀ | 21 | 17 + P (section 7.1) |
| w | 2 | `⌈d_s / (0.7·v·T)⌉ + 1`. d_s = largest distance from a slot-0 point (`GuestSimulation.QueuePositionPoint(·, 0, 114..141)`) to `GuestSimulation.StandPoint` = 13.5/255 = 0.053 cell. v = `GuestSettings.WalkSpeedCellsPerSecond` (1.0). 0.7 is the low-energy factor of `GuestSimulation.Speed` |
| w₂ | 6 | the same formula with d_u = slot 3 → slot 0 = √((191/255)² + (27/255)²) = 0.757 cell |
| H | 29 | H₀ + w + w₂ |
| Qmax | 100 | `RideVisitorBridge.MaximumQueueLength` |

Assertions:

1. Unchanged: the §9a FAIL rules (head not ready beyond `HeadNotReadyBound`,
   blocked handshake on two consecutive evaluations).
2. Every completed wait (`GuestSimulation.QueueWaitCompleted`, W turns, join
   position p) that does not overlap an excluded turn must satisfy
   **`W ≤ (p + 1)·H + (⌊p/CAP⌋ + 1)·R + 1`**. A violation is a FAIL.
3. Every guest still queued at the end must satisfy the same bound on its age,
   using its join position. A violation is a FAIL (a stuck queue).
4. Excluded turns (A3): a turn where the ride is closed or broken
   (`RideVisitorBridge.IsBroken`, `VAR_BREAKSTAT ≠ 0`, `VAR_RIDECLOSED ≠ 0`, or
   not open), or where CAP or DUR changed during the wait. Waits that overlap
   one are counted and reported, not judged.
5. Verdict: **PASS** when (1) to (3) hold and at least one wait was judged.
   **UNRESOLVED** when none was judged.
6. Report: H, R, the τ_max line (`CAP·H + R + 1 − (DUR + 1 s)/T` = 142 turns =
   35.216 s) and W_max = W(Qmax − 1) = 5321 turns = 1319.608 s. Also report
   the measured values as evidence (the existing `tauMeasured`), not as
   thresholds.

The baseline (queue max 51, worst wait 254.4 s) would pass. At p = 50 the bound
is 51·29 + 11·121 + 1 = 2811 turns = 697 s.

**Approximation tag.** Only w and w₂ rest on OpenTPW's walk model. Replace
`[APPROX:GATE-003]` with a narrower tag: "walk terms w, w₂ of the boarding
bound use `WalkSpeedCellsPerSecond` and the QUEUE-006/007 geometry; evidence
needed: original steering speed floor and `0xde1d8`/`0xdde74` geometry". Add
the row to M3-GATE.md's list of passing rows that rest on approximations, as
the arrival and needs rows already are. `HeadNotReadyBound` already uses the
same walk speed. If the integrator does not accept an implementation-bounded
walk term, the row stays UNRESOLVED, and **w and w₂ are the only blockers**.

OpenTPW differs from the original in ways that only shorten waits, so the
bound stays a valid ceiling. `RideVisitorBridge.TryPerform` releases a slot
at `Until ≤ now` without the 1 s / 200 ms window. Its scripts run per 60 Hz
tick, not per 248 ms slice. It marks the guest boarded at BOUNCE. None of
these needs to change for the gate.

## 9. General rides

The skeleton `W(p) ≤ (p + 1)·H + (⌊p/CAP⌋ + 1)·R + 1` holds for any HasQueue
ride admitted through `0xe1404`, provided three things are known: the period P
at which its script consumes LETMEON when a place is free, a per-rider hold R
until the release frees capacity, and one rider released per release
opportunity. H's host terms (17 turns plus w and w₂) are the same for every
ride. P and R are derived here only for the four BOUNCE scripts. These are
**not instantiated**:

- LIMBO, HUSH and WALKON rides, and the TOUR, BUMP and COAST controllers.
  Their consumption and release loops contain animation waits or controller
  calls that are not timed (`bounce_loop` reports such a wait as untraced).
- Rides with RunsContinuously 0. The RUNNING gate batches admission.
- BOUNCE rides with DUR ≥ 31. R = 529 turns only on the exact grid.

## 10. Unresolved

- w and w₂ (section 6): steering speed floor, stand-point default, slot axis.
- Live-list eligibility (A2), the substep reset phase, and real-clock jitter
  (A1).
- Closed and broken branches: `WAITANIM 10` repair-animation length, and
  FORCEUNBOUNCE timing.
- Ride `+408` state names (1 and 4 reject admission). The failure exit of
  `0xeeb30` (`0xde54c`), which leaves LETMEOFF set.

## 11. Witnesses and tests

Lane `tools/ppc-analysis/lanes/board/`: `board_evidence.py` and
`test_board_evidence.py`. The runner discovers it.

- **Identified binary**: 13 block digests; 52 decoded witnesses (48 D-form
  fields, 2 `srawi`, 2 `clrlwi`); 30 call targets; 5 guest-state table entries;
  8 interpreter cases.
- **Corpus** (PC Data): the four BOUNCE scripts are parsed, their open-ride
  loops enumerated, and the loop shape, P, CAP, DUR, RunsContinuously and R
  checked. The Belly Bounce SHA is pinned.
- **Tests**: 20 in total. 14 run without assets: loop analysis on a synthetic
  RSE, including three structural mutations (an animation wait, a missing
  CRIT_UNLOCK, budget 5); timing; latency terms; bound arithmetic; the
  simulation; field decoders. 5 need the identified binary, 4 of them binary
  mutations: the /200 magic, the WAIT rewind, the state-14 head offset 56 → 54,
  and the state-14 table entry. 1 needs PC Data.
- **Module mutation check**: 10 mutations, each caught by at least one test:
  - interlude 11 → 10;
  - move-up gap 2 → 3;
  - loop period +1;
  - poll offset +1;
  - the /200 magic operand;
  - the closed-ride guard;
  - the CRIT_UNLOCK yield;
  - the (p + 1) factor;
  - the call term;
  - the state-14 handler.
