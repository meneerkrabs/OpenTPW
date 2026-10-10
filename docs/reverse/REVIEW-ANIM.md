# Review ANIM-V: UV tracks and the Mac semantics of the animation opcodes (b84bf58)

October 10, 2026. Independent review of `b84bf58` ("Play MD2 UV tracks and give the animation opcodes the
Mac handlers' semantics") on its parent `c74c402` (ANIM-R plan). Evidence: static decoding of the identified
Feral Mac PEF (`SimThemePark.data`, SHA-256 `04809cd4…5e295f5`; `engine_shared.data` `c549123f…`) with
`lanes/review/ppcdis.py` (llvm-mc) and this review's own field split
(`tools/ppc-analysis/lanes/review/test_anim_v1.py`). Nothing from the original was executed. Claims are
bounded to this Mac build, .NET 10 on one arm64 Mac, and the code at `b84bf58`.

## Verdict: ACCEPT-WITH-FIXES

No finding blocks correctness of the corpus runs: the gate, the smokes and all suites pass, and none of
the findings is reached by a corpus script on the measured paths. AV-1 and AV-2 are must-fix before merge
because they are an undisclosed and an unregistered divergence from the original (policy: approximations
are registered, never hidden). Both are small.

## Findings

| ID | Severity | Where | Finding | Fix |
| --- | --- | --- | --- | --- |
| AV-1 | Medium (must-fix) | `source/OpenTPW/VM/Handlers/Animation.cs` `WaitAnimChannel`; `RideVM.WaitFor` | WAITANIM_CH's deadline is a wrapping 32-bit add. 0xb0330–0xb0360: `addi r3,r3,-300`, unsigned clamp `cmplwi 300`, then `add r3,r4,r3` (wait + now) stored to +160, with no float step; resume compares `cmplw now, deadline` (0xb02bc). A clip under 300 ms gives wait ≈ 2³² − (300 − ret), so the deadline is `now − (300 − ret)`: the script continues at the next execution (once the clock passes 300 − ret ms). OpenTPW returns the same unsigned wait (`WaitAnimChannelDelay(299) = uint.MaxValue`, correct) but `WaitFor(double)` waits it in full: ~49.7 days. The ANIM-R model docstring (`anim_model.py` `waitanim_wait`: "WAITANIM_CH does the same without the speed division") is wrong too: WAITANIM saturates through `fctiwz` to INT32_MAX and does not wrap; WAITANIM_CH has no `fctiwz`. Scenario: `WAITANIM_CH 5 0 c` on a 166 ms clip → original continues after one slice, OpenTPW stalls the script. No corpus hit (ANIM-I: the only sub-300 ms request is a plain WAITANIM). | Model the u32 add: `deadline = (uint)(now + wait)`; wait while `(uint)now < deadline`, or equivalently, for `wait ≥ 2³¹`, treat as `now − (2³² − wait)`. Add a test for `WaitAnimChannelDelay(166)` at clock 10 s → continues; correct the `anim_model.py` docstring. |
| AV-2 | Medium (must-fix, policy) | `Animation.cs` `Trigger`/`TriggerAndWait`; `docs/RSE-VM.md:135`; `RideVMCorpusTests` Totem trace | "No animation host → missing member (1000 ms)" is not the original's behaviour and is not registered. Every trigger handler tests the script's object (+200) and, when it is null, uses `li r3,0` (0xafb1c, 0xafc74, 0xafe88, 0xb0078, 0xb01d4, 0xb032c, 0xb0510): ret 0, so TRIGANIM-family out = 300 (not 700), WAITANIM waits INT32_MAX (not 700), and TRIGWAITANIM stores its token and then compares against a stale stack word (0xafddc–0xafe18) instead of returning. The production path always has a host (`OriginalObjectEffects`), so only headless VMs (tests, the unused `Ride` class) reach it; the Totem trace's uniform +700 ms is this policy. It is documented as "OpenTPW policy" in RSE-VM.md and ANIM-plan §15 but has no `[APPROX:…]` tag, so `fidelity_register.py --check` cannot see it. | Tag it, e.g. `[APPROX:RIDES-036] A trigger without an animation host counts as a missing member (1000 ms); the original's null-object path returns 0 ms (out 300, WAITANIM INT32_MAX) — evidence needed: none (headless-only policy)`, at `Animation.Trigger`, and regenerate the register (190). Or return 0 to match the original and give the headless Totem test a host. |
| AV-3 | Low | `ObjectAnimator.cs:370`; `RIDES-031` text | The UV-pass gate is `((option +16396 & 0x10) == 0 && (node & 0x4000) == 0) \|\| header flag 0x4` (0xa5698–0xa56b8); the comment omits option bit 0x10. The setter of node bit 0x4000 is untraced (ANIM-plan §14), so "nothing in OpenTPW sets 0x4000" is an unregistered assumption. Also, with header flag 0x4 the UV pass runs in **add** mode (r5 = header & 4 at 0xa56d4; add loop with `fadds` at 0xa8420, set loop at 0xa84dc); RIDES-031 names only vertex tracks. Unreachable on the default load path (flag stripped), so no behaviour change. | Mention option bit 0x10 in the comment; add "node bit 0x4000 setter untraced" to an existing APPROX (RIDES-031 or RIDES-032) or a new one; extend RIDES-031 to "vertex and UV tracks". |
| AV-4 | Low | `tools/ppc-analysis/lanes/anim/anim_witness.py` | The D7 correction and the other ANIM-I static reads (§15 "not yet pinned") are not in the lane witness. They are now pinned by this review's `test_anim_v1.py` (runs in the evidence runner's review lane), so this is not a merge blocker; move them into `CHECKS` when the lane is next touched. | Add CHECKS for 0xafbb4/0xafcf8/0xaff24/0xb00ec/0xb31a0 (`stw 0xFFFF → +168`), the `_CH` handlers' absence of +168, 0xb00c4 `stwx`, 0xafd40 → exit. |
| AV-5 | Info | `TrigAnimSpeed` | A speed operand ≤ 0 throws `RideScriptException`. Zero is undefined on PowerPC (`divw`), but negative operands are defined in the original (signed `divw`, negative trigger speed). Corpus uses 1800–4000, so this is a stricter refusal, acceptable; say "negative is defined but untested" in the message or register it. | Optional. |
| AV-6 | Info (unverified) | `ObjectAnimator` copy-back after an ended clip | The start 0xa67d8 passes the *channel's current AnimID/sub* as the bind's old clip (0xa692c–0xa6948) without an existence check. After a clip ends, the channel holds AnimID 14, sub 0, so the next trigger's copy-back uses the object's AnimID-14 entry, not the ended clip. OpenTPW restores against the ended clip. Not traced further (what the AnimID-14 table slot holds for objects without such a member). | Record as an open question in ANIM-plan §14. |

## Claims

| # | Claim | Verdict | Evidence |
| --- | --- | --- | --- |
| 1 | UV tracks: decode/validation, 0xa8304 search, per instance after the vertex pass, set mode, stored UVs restored on clip change except fixed items | **CONFIRMED** (with AV-3) | 0xa8304: `bl 0x1c3fbc` truncates the float time; loop `cmplw tick, ticks[i]`, first key with `tick < ticks[i]` gives `k = i − 1`, fraction `(time − t[k]) / (t[i] − t[k])` in float time (`fdivs` 0xa83ac); none later gives `k = count − 2`, fraction 1.0 (TOC 0x51d0). Set loop 0xa84ec: `fmuls t·b` then `fmadds (1−t)·a + t·b`, matching `Sample`'s FMA order; destination +104, block `(i/4)·32`, lane `(i%4)·4`, V at +16. Called from the sampler tail 0xa56d8 after TRS and vertex. Copy-back 0xa5894 (called at 0xa69a4 with r4 = old clip from the channel, r5 = new clip): mask = old record flags (0xa5a00) `andc` new record flags (0xa5aa8), then for the old clip's records copies TRS (0x289), positions (0x1000) and UVs (0x10000, from the stored mesh +104). So a mesh whose UV track the new clip lacks gets its stored UVs, as OpenTPW does; gated by option bit 0 / object 0x00100000 without 0x8 (fixed items). Validation (count ≥ 2, starts at 0, increasing, finite) is stricter than the original and holds on the corpus (ANIM-I §15). |
| 2 | Metadata bits 0x10/0x20/0x40/0x100/0x800/0x2000/0x8000 unread | **CONFIRMED** (bounded) | Wider than the witness: every function in both containers that loads a clip record table (`lwz +44` with `lhz +18` within 16 words) — app 0x53fa0, 0x582fc, 0xa3e24, 0xa56f8, 0xa5894, 0xa5d88, 0xa6398, 0xa7190; engine 0x3f9d8 — was scanned for masks tested on words loaded from `+4` or from the per-node mask arrays (`lwzx`). The only unread-bit hit is 0x8000 at 0xa71c8 on r25, the object (passed as r3 to 0xa67d8 at 0xa7490), not a record. Bound: record pointers reached only through other structures (e.g. bound node +60) are not covered. |
| 3 | Ascending channel order; highest channel per node component wins (RIDES-002 retired) | **CONFIRMED** for order and for vertex/UV (set-mode writes into the live mesh; 0xa7a5c/0xa7a60 ascending, stride 56); **partly verified** for TRS (the sampler writes translation straight to node +64..+72 at 0xa5564–0xa55a0; per-component rotation/scale not re-derived here). |
| 4 | All 14 opcodes' arithmetic | **CONFIRMED except WAITANIM_CH (AV-1)** | Each handler re-read 0xafa68–0xb07e4. D7 correction: TRIGANIM 0xafbb4, WAITANIM 0xafcf8, TRIGWAITANIM 0xaff24, TRIGANIMSPEED 0xb00ec and script creation 0xb31a0 store `lis 1; addi −1` = 0xFFFF to +168; no `_CH` handler loads or stores +168; only LOOPANIM reads it. TRIGWAITANIM's trigger path (0xafe44–0xaff3c) rewinds PC by 4 without a +152 store; only the mismatch path (0xafe30) clears +152; token = first operand + 1 (0xafefc); query 0xa7dcc stores channel +4 (AnimID). TRIGANIMSPEED: `max(ret − 300, 300)` signed (0xb0088), `stwx` of +72 to the out variable at 0xb00c4, deadline `now + mulli(out, 1000) divw operand` (0xb00dc/0xb00e0), trigger speed `f31 × operand / 1000.0` (TOC 0x52c0 = 1000.0). LOOPANIM: equal token (`cmplw` 0xafd3c) branches to the common exit 0xb2354 before the +164 store at 0xafd88; LOOPANIM_CH re-triggers every call and zeroes +164 (0xb03fc). WAIT4ANIM: one deadline +164 per script. GETANIM(_CH): flag 0x4 → −1. |
| 4b | D7 correction not pinned in `anim_witness.py` — must-fix? | **Not a blocker** (AV-4) | Pinned now by `test_anim_v1.py`. |
| 5 | Clip end hold (AnimID 14, frozen, GETANIM −1), deferred clip, RIDES-003 citations | **CONFIRMED** | 0xa7498 `li r4,14; li r5,0; bl 0xa67d8` on the non-loop, no-defer path; 0xa68ac `ori 0x14`; 0xa694c branches past the bind for AnimID 13/14. Citations present at `ObjectAnimator.cs:337`. See AV-6 for the next trigger after an end. |
| 6 | Crys_a WAITANIM wrap (~24.8 days) is original; cannot break gates/smokes | **CONFIRMED** | 0xafc78–0xafcc0: `ret − 300` through the unsigned bias 0x4330000000000000 (TOC 0x5288), `fdivs` by script speed, `fctiwz` (saturates to 0x7FFFFFFF), `cmplwi 300`; deadline = now + that. 166 ms → INT32_MAX. Crys_a is a space feature; the M3 gate (jungle) is 16/0/0 and all smokes pass. The M4 gate is not on this branch or main (it lives under OPC-T's parent 27734f7). |
| 7 | No-host policy registered as APPROX? | **REFUTED** (AV-2) | Not tagged; the original's null-object result is 0, not 1000. |
| 8 | RIDES-029..035 new, 002/004/006/007 retired, ADVISOR-006 narrowed; register check | **CONFIRMED** | `--check` passes: 189 IDs (186 at c74c402 + 7 − 4). No retired ID remains in source. `test_fidelity_register.py` 8/8. |
| 9 | Determinism schema 6, pin, doc format, fields | **CONFIRMED** | `SchemaVersion = 6`; `0x0BC982AB91BE9A28` in `DeterminismTests` and DETERMINISM.md as "`0x0BC982AB91BE9A28`, schema 6"; reproduced in both test runs. The script digest hashes `Wait4AnimDeadline`, `LoopAnimationToken`, `TriggerWaitToken`, and root object scripts add the animator (clock, per channel AnimID/variant/loop/speed/start/ended/tick, deferred clip): everything GETANIM/TRIGWAITANIM read back. Poses, vertex/UV arrays and node bits are presentation and listed as not hashed. |
| 10 | Merge readiness onto main 8a000d8 | see Merge | |

## Merge

`git merge-tree --write-tree 8a000d8 b84bf58`: one conflict, `docs/FIDELITY-REGISTER.md` (generated).
Resolution: take either side, `python3 tools/fidelity_register.py --write`, then `--check`.
Trial merge in a scratch `git clone --shared` (aborted and deleted afterwards): with the register regenerated,
`--check` passes with **199** IDs (main 196 + 7 − 4); Release build 0 errors; `OpenTPW.Tests` without assets
1102 / 274 / 0, including the determinism pin `0x0BC982AB91BE9A28` (main's only hash-path change is a comment).

Interplay with the pending branches (merge-tree against `b84bf58`):

- **OPC-T `c84a647`**: conflicts only in `docs/FIDELITY-REGISTER.md`; `Game.cs` and `RideVM.cs` auto-merge.
  But four assertions in `RideOpcodeEffectsTests.TrigWaitAnimWaitsItsDurationAndTrigAnimSpeedFeedsWait4Anim`
  contradict the traced rules and will fail after both land:
  1. "TRIGANIMSPEED … writes no destination": the original writes `max(ret − 300, 300)` (0xb00c4); with
     ret 3000 → V0 = 2700, not 0.
  2. "TRIGWAITANIM writes the duration": out is `max(600 − 300, 300)` = 300, not 600.
  3. "TRIGWAITANIM waits for its own duration (~36 ticks)": TRIGWAITANIM waits on the channel's AnimID, not
     a duration; with a host that is not an `IRideAnimationQuery` it does not wait at all.
  4. "WAIT4ANIM waited for the 3000 ms … ~181 ticks": the deadline is per script and the later TRIGWAITANIM
     overwrites it (now + 300); TRIGANIMSPEED's own deadline would be `2700 × 1000 / 4000` = 675 ms.
  In `LoopAndChannelAnimationOpcodesPassThroughAndQuery` the assertions pass, but the message "loops are
  not tracked for WAIT4ANIM (docs assumption)" is wrong: WAIT4ANIM does not wait because LOOPANIM_CH zeroed
  the deadline TRIGANIM_CH had set. Whichever lands second must rewrite these tests to the traced rules
  (RIDES-007's removal is the reason for item 1).
- **SAVE-I `2ddf376`**: conflicts only in `docs/FIDELITY-REGISTER.md`; `README.md`, `docs/DETERMINISM.md`,
  `docs/LIPS.md` and `Advisor.cs` auto-merge. Regenerate the register after both. SAVE-I does not change the
  determinism pin line, so the pin stays with ANIM-I's value unless SAVE-I's hashed state differs (re-run
  `DeterminismTests` on the second merge).

## Numbers

SDK 10 (`~/.local/share/opentpw-dotnet10/dotnet`), Release, worktree at `b84bf58`.

| Check | Result |
| --- | --- |
| Build `source/OpenTPW.sln` Release | 0 errors |
| `OpenTPW.Tests` without assets | 1072 / 268 / 0 (passed / skipped / failed) |
| `OpenTPW.Tests` with `OPENTPW_GAME_PATH` | 1256 / 84 / 0 |
| `--m3-gate` (jungle, seed 6075451861746676596, 30 min) | **16 pass / 0 fail / 0 unresolved, exit 0**; run hashes guest 3DE1C0F27EB4A499, gate C9CDB7D4166216E5 (both runs equal) |
| Native smokes (`scripts/run.sh --game-path …/theme-park-world <flags> --smoke-test`) | all exit 0: `--front-end` 518 frames; `--sandbox` 1088 frames ("Original objects animating between frames: Sun God, Crazy Ape, Tom Tom Twister, Eruption, Inca Totem"); `--load-original-level jungle` 4296 frames (guests boarding/riding/released by Totem.RSE); `--advisor-say 1` 249 frames (sp_001) |
| `run_evidence_checks.py --mac-bin … --pc-data …/Data --dotnet <net8 SDK>` | OK: 16 Python suites, 984 tests, 152 skipped; with this review's test 997 (review lane 418 ran, 111 skipped) |
| `test_anim_v1.py` | 13 / 13 with `OPENTPW_PPC_BIN_ROOT`; 2 pass, 11 skip without |
| `fidelity_register.py --check` | pass, 189 IDs |
| `git diff --check c74c402 b84bf58` | clean |
| `--stat` vs `--ignore-cr-at-eol --stat` | identical (35 files, +2361 −459); no file has CR line endings on either side |

Not tested: Windows/x64; captures of the original (none exist for these paths); the object-list update route
(RIDES-032). Nothing from the game data or binaries is committed.
