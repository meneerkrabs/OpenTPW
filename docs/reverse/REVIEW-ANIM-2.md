# Review ANIM-V2: ANIM-F's fixes for the ANIM-V findings (65433c2)

October 11, 2026. Independent focused re-review of `65433c2` ("Fix the ANIM-V findings: wrapping WAITANIM_CH
deadlines and the original's null-object triggers") on its parent `e92fb23`, against review ANIM-V
(`4011278`, [REVIEW-ANIM.md](REVIEW-ANIM.md), `lanes/review/test_anim_v1.py`). Evidence: static decoding of
the identified Feral Mac PEF (`SimThemePark.data`, SHA-256 `04809cd4…5e295f5`) with `lanes/review/ppcdis.py`
(llvm-mc) and this review's own field split (`tools/ppc-analysis/lanes/review/test_anim_v2.py`); one scratch
C# test in a throwaway clone (AW-2, not committed). No pure routine applies to these handlers (they are
interpreter cases that call the scheduler clock and the object), so the PPC oracle was not used. Nothing from
the original was executed. Claims are bounded to this Mac build, .NET 10 on one arm64 Mac, and the code at
`65433c2`.

## Verdict: ACCEPT-WITH-FIXES

AV-1 and AV-2 are fixed as the binary says, AV-3..AV-6 are addressed, the OPC-T tests now assert the traced
rules, and every check passes with the claimed numbers. Nothing blocks correctness on the measured paths. Fix
before merging: AW-1 (an unregistered approximation, now observable through the AV-1 fix) and AW-4 (an
approximation ID collision with `origin/main` that the merge has to resolve). AW-2 and AW-3 are small and
can be fixed in the same pass.

## The three doubts ANIM-F raised

1. **Which clock, and does it start at script creation?** The shared scheduler clock, and no. The interpreter
   loads r30 from TOC slot 0xa28 (a data-section global, `sec1+0x11f340`) in its prologue (`0xaf55c`); no
   instruction from `0xaf560` to `0xb1650` writes r30, which covers GETTIME and all animation handlers.
   WAITANIM (`0xafbfc`, `0xafcc8`), WAITANIM_CH (`0xb02b4`, `0xb0350`), TRIGWAITANIM's deadline (`0xafedc`)
   and GETTIME (`0xaf6d8`) all call `0x10e844` with r3 = r30, and GETTIME stores the result unchanged
   (`0xaf6e0`, consistent with PPC-clock.md "no VM-creation timestamp subtraction"). OpenTPW's
   `RideVM.ClockMilliseconds32` is the per-VM clock that starts at 0. See AW-1.
2. **The folded re-execute step.** Faithful. The trigger execution of WAITANIM(_CH) stores the deadline,
   rewinds three words and clears the yield word +152 (`0xafce8`/`0xafcf0`, `0xb0368`/`0xb0370`). The next
   execution tests +160 ≠ 0 (`cmplwi`, 0 = not waiting), compares `cmplw now, deadline` (`0xafc04`,
   `0xb02bc`), and on "passed" clears +160 and leaves through the common exit without a yield, so the script
   continues in that slice. A wrapped deadline is already past at the next slice, so OpenTPW's
   `EndSlice()` without `RepeatInstruction()` gives the same position and the same slice; the only difference
   is the one re-execution that `ExecutedInstructions` (hashed) does not count, the same convention OpenTPW uses
   for every wait. A sum of exactly 0 re-triggers in both (pinned at clock 250 with a 50 ms clip). The
   boundary is exact: with a clip `ret < 300`, a clock above `300 − ret` wraps into the past, a clock equal to
   it gives 0, a clock below it waits 2³² − (300 − ret) − now ms.
3. **Hosts without the AnimID query.** Not settled by the change: OpenTPW decides "the script has an object"
   in two places with two different signals, which disagree for a visitor-wrapped host. See AW-2.

## Findings

| ID | Severity | Where | Finding | Fix |
| --- | --- | --- | --- | --- |
| AW-1 | Low (fix before merge, policy) | `source/OpenTPW/VM/RideVM.cs:91-92` `ClockMilliseconds32`; `Handlers/Scheduling.cs:23` GETTIME; `docs/RSE-VM.md:78-79`; `AnimationOpcodeTests.WaitAnimChannelWrapsAShortClipIntoThePast` | The script clock starts at VM creation; the original's waits and GETTIME read the shared scheduler clock (doubt 1). The `ClockMilliseconds32` summary calls it "the original's 32-bit millisecond word", and RSE-VM.md states "GETTIME is milliseconds since the VM was created" as fact. Before ANIM-F the base only shifted GETTIME's absolute value (the corpus uses it relatively); with the u32 deadline it decides the outcome: `WAITANIM_CH` on a clip of `ret < 300` ms in the first `300 − ret` ms of a script waits about 49.7 days in OpenTPW, while the original, whose scheduler clock has run since the park started, continues at the next slice. The test's `early` case ("at 16 ms the sum does not wrap") pins the OpenTPW-only outcome. No corpus hit (WAITANIM_CH has 0 corpus uses). Not registered. | Register it, e.g. `[APPROX:RIDES-039] The script clock (GETTIME, WAIT, WAITANIM(_CH) deadlines) starts at 0 when the VM is created; the original reads the shared scheduler clock (0x1010E844 on the global r30, 0x100AF55C) — evidence needed: the scheduler clock's value when a placed object's script starts`, at `ClockMilliseconds32` or GETTIME; correct the summary and RSE-VM.md; reword the `early` message to name the approximation. `test_aw1_…` turns green when a tag names the scheduler clock. |
| AW-2 | Low | `source/OpenTPW/World/Guests/RideVisitorBridge.cs:652-656` `VisitorRideScriptEffects`; `Animation.cs:180` | "Has an object" is decided by `TryEffect` for the trigger (answered → real `ret`) but by `vm.Effects is IRideAnimationQuery` for TRIGWAITANIM's resume. `VisitorRideScriptEffects` forwards triggers to its inner host but does not implement the query, so with an `OriginalObjectEffects` inside, TRIGWAITANIM plays the clip and then never resumes. Before ANIM-F it did not wait at all; now it re-executes every slice for good. Reproduced in a scratch test: the same host directly resumes (V2 = 1), wrapped it does not (V2 = 0 after 120 ticks). Production is not affected (`OriginalObjectEffects` routes visitors itself; the wrapper is used only in tests), and `GuestAssetTests.GuestsRideTheOriginalTotemScript` uses exactly this wrapping but Totem.RSE has no TRIGWAITANIM. | Make `VisitorRideScriptEffects` implement `IRideAnimationQuery` by forwarding to the inner host (and decide "no object" from one signal); add a test with a wrapped query host. `test_aw2_…` checks the declaration. |
| AW-3 | Low (coverage) | `source/OpenTPW.Tests/RideVMCorpusTests.cs` | `TotemScriptTraceIsPinned` also ran a closed Totem (`VAR_RIDECLOSED = 1`: idles in its ENDSLICE loop after WAITANIM, VAR_RUNNING stays 0). ANIM-F replaced the open trace with the hosted `TotemScriptTraceWithItsObjectIsPinned` but dropped the closed case without a replacement; headless it would now be trivially true (the first WAITANIM waits INT32_MAX), so it needs the hosted runtime (`open: false`). | Re-add the closed case on `OriginalObjectRuntime`. `test_aw3_…` looks for the assignment. |
| AW-4 | Medium (merge step, not a defect of 65433c2 alone) | `RidesApproximations.cs`, `docs/OBJECTS.md`, `docs/FIDELITY-REGISTER.md` | `origin/main` (`c09c6ba`, from `6820470` "experimental Theme Park Inc level viewer") already uses **RIDES-029** (`ObjectShape.cs:80`, the `@` shape symbol) and **RIDES-030** (`ObjectAssets.cs:71`, non-finite TPI texture coordinates) for other approximations; ANIM-I's RIDES-029..035 and ANIM-F's 036..038 collide with the first two. ANIM-V's merge section predates `6820470`. Textual conflicts in all three files; a naive union fails `fidelity_register.py --write/--check` ("duplicate declaration RIDES-029/030"), so the gate catches it, but the prose references in `docs/THEME-PARK-INC.md:98,121` are not checked by the tool. | At merge, renumber one pair (the trial below moved the TPI pair to RIDES-039/040) including THEME-PARK-INC.md, then `--write` and `--check`. If AW-1 takes RIDES-039, use 041/042 here. |

Info (no action needed):

- **I-1** RIDES-037 bound: the compare without an object is `code word + 1` against `AnimID + 1` (signed
  `cmpw`). A literal AnimID (sign-extended 16 bits) can never equal a variable's word (tag 0xC0 in the top
  byte), so "never resumes" holds for literal AnimIDs; a variable AnimID would have to hold the out operand's
  code word (a value between −2³⁰ and −2³⁰ + 2²⁴). For a literal out operand the in-memory word is untraced (the loader's tag), as registered.
- **I-2** The witness grew from 185 (e92fb23) to 224 checks: 35 new `CHECKS` + 4 `TOKEN_SCANS` = 39 (the
  brief said 38; the commit message only gives the total). The `_CH` token scan covers `lwz`/`stw` on r31 only.
- **I-3** `run_evidence_checks.py` parses `expected failures=3` as `failures=3` (regex `(\w+)=(\d+)`), so the
  review lane line shows "3 failed" while its status is OK. Pre-existing display quirk.
- **I-4** The corpus run's clip-less default host (1000 ms per trigger, channels on AnimID 12) is the
  original's missing-member behaviour on an object, not a weakening: both passes still assert no Faulted and no
  Halted script, `executed.Count >= 83` is over both passes, and the chaos pass (channels that show the last
  TRIGWAITANIM AnimID) still releases TRIGWAITANIM. The default pass now parks scripts at their first
  TRIGWAITANIM (20.4 M instructions instead of 23.8 M), so it reaches less code than before; the hosted trace
  and the chaos pass carry that coverage.

## Claims

| # | Claim | Verdict | Evidence |
| --- | --- | --- | --- |
| 1 | AV-1: WAITANIM_CH wraps (0xb035c), resumes unsigned (0xb02bc); WAITANIM saturates; `ClockMilliseconds32` | **CONFIRMED** (clock base: AW-1) | `0xb0330 addi -300`, `0xb033c cmplwi 300`, `0xb035c add r3,r4,r3`, `0xb0360 stw +160`; no float op but the trigger's `fmr`; WAITANIM `0xafca0 fctiwz`. `WaitUntilDeadline` matches the three outcomes (past → yield and continue, 0 → re-trigger, else wait); the reference in `test_anim_v2.py` agrees with `anim_model.waitanim_ch_deadline` over a grid. |
| 2 | AV-2: seven `li r3,0` null-object returns; out 300; WAITANIM INT32_MAX; TRIGWAITANIM never resumes (0xafde8); RIDES-037 | **CONFIRMED** (hosts: AW-2) | All seven addresses decode as `li r3,0` falling into `addi −300`. TRIGWAITANIM: third operand's raw word stored at `576(r1)` (`0xafdcc`); the null branch (`0xafdec` → `0xafe0c`) skips the query `0xa7dcc` that would overwrite it; `0xafe0c–0xafe18` compares word + 1 with +188. Trigger path: out 300, deadline, token, rewind 4 without +152 (no yield). |
| 3 | AV-3: gate comment, RIDES-036, RIDES-031 extended | **CONFIRMED** | Comment names option +16396 bit 0x10 and the 0xa5698–0xa56b8 gate; RIDES-036 and RIDES-031 texts in source, `RidesApproximations`, OBJECTS.md and the register. |
| 4 | AV-4: CHECKS in `anim_witness.py`, 224 total | **CONFIRMED** (I-2) | 224 against the Mac binary; synthetic mutations (addc, no-object 1000, `_CH` token store) fail as intended. |
| 5 | AV-5: RIDES-038 | **CONFIRMED** | Negative speed faults with a message naming RIDES-038; `SpeedTriggerDelay(700, −4000) = −175` (signed `divw`). |
| 6 | AV-6: open, RIDES-003 extended, ANIM-plan §14 | **CONFIRMED** | Text matches ANIM-V's reading (0xa692c–0xa6948, 0xa69a4, AnimID 12 skip). |
| 7 | OPC-T alignment, `RideOpcodeTestMap` | **CONFIRMED** | The four contradicting assertions ANIM-V listed are rewritten to the traced rules (2700, 300, AnimID wait, overwritten deadline, LOOPANIM_CH's clear named). The map names existing tests; `vm.opcodes` passes (84 with a unit test). |
| 8 | Totem re-pin; new corpus hosts don't weaken coverage or hide faults | **CONFIRMED with AW-3** | Hosted trace is the original object (`OriginalObjectRuntime`): 5 clips resolved, 0 unresolved; headless trace pins INT32_MAX. Corpus hosts: I-4. |
| 9 | Determinism unchanged 0x0BC982AB91BE9A28, schema 6 | **CONFIRMED** | `DeterminismTests` pass in both runs and in the trial merge; `SchemaVersion = 6`. |
| 10 | M4 15/11/1 identical to e92fb23; M3 16/0/0 | **CONFIRMED** | Both gates run here at `65433c2` and `e92fb23` (Release, same data): 27 rows, identical verdicts; the only evidence difference is the six test names in `vm.opcodes` (the map fix). M3 hashes 3DE1C0F27EB4A499 / C9CDB7D4166216E5 in both runs. |

## Merge (origin/main `c09c6ba`, fetched October 11, 2026)

`e92fb23` is not yet on `origin/main`. Trial merge of `65433c2` in a scratch `git clone --shared`
(`git merge --no-commit`, deleted afterwards; nothing committed): conflicts in `docs/FIDELITY-REGISTER.md`
(generated), `docs/OBJECTS.md` and `RidesApproximations.cs`, all from AW-4. `README.md`, `DETERMINISM.md`,
`DeterminismTests.cs`, `Game.cs`, `OriginalObject.cs` and `WorldStateHash.cs` auto-merge. Resolution used:
keep both sides, renumber main's TPI pair to RIDES-039/040 (source tags, `RidesApproximations`, OBJECTS.md),
`fidelity_register.py --write`, then `--check`: **207** IDs. Release build 0 errors; `OpenTPW.Tests` without
assets **1151 / 286 / 0**, including the determinism pin `0x0BC982AB91BE9A28` (schema 6). Not run on the
merge: tests with assets, the gates, smokes.

## Numbers

SDK 10 (`~/.local/share/opentpw-dotnet10/dotnet`), Release, worktree at `65433c2`.

| Check | Result |
| --- | --- |
| Build `source/OpenTPW.sln` Release | 0 errors |
| `OpenTPW.Tests` without assets | 1095 / 276 / 0 (passed / skipped / failed) |
| `OpenTPW.Tests` with `OPENTPW_GAME_PATH` | 1287 / 84 / 0 |
| `--m3-gate` | **16 / 0 / 0, exit 0**; guest 3DE1C0F27EB4A499, gate C9CDB7D4166216E5 (both runs equal) |
| `--m4-gate` | 15 pass / 11 fail / 1 unresolved, exit 1; same verdicts as `e92fb23` (run here too) |
| Native smokes, one at a time (`scripts/run.sh … --smoke-test`, osx-arm64) | `--load-original-level jungle` exit 0 (4264 frames, guests boarding/riding/released by Totem.RSE); `--sandbox` exit 0 (1058 frames, Inca Totem among the animating objects). `--front-end` and `--advisor-say 1` not rerun (untouched by the change) |
| `run_evidence_checks.py --mac-bin … --pc-data …/Data --dotnet <net8 SDK>` | OK: 16 Python suites, 1012 tests, 152 skipped (998 + this review's 14; review lane "3 failed" = the three expected failures, I-3) |
| `anim_witness.py` against the Mac bin | 224 checks |
| `test_anim_v2.py` | with `OPENTPW_PPC_BIN_ROOT`: 14 run, OK (3 expected failures = AW-1..AW-3); without: 7 skipped |
| `fidelity_register.py --check` | pass, 194 IDs |
| `git diff --check e92fb23 65433c2` (whitespace rules of agent-rules) | clean |
| `--stat` vs `--ignore-cr-at-eol --stat` | identical (18 files, +534 −156) |

Not tested: Windows/x64; captures of the original (none for these paths); the scheduler clock's value at a
script's start (AW-1 evidence); the RSE loader's literal operand word (RIDES-037). Nothing from the game data
or binaries is committed.
