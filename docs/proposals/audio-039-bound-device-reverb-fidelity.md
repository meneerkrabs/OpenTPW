# AUDIO-039: Bound device reverb fidelity

## Status and classification
- **Superseded (October 19, 2026):** the user approved the reverb as a design choice ("nagalm mag EXT"). `APPROX:AUDIO-039` is retired and the stand-in is `[EXT:audio-reverb]` (docs/AUDIO.md, "Reverb"). The rest of this proposal is kept as history.
- Classification: approximation; characterization-test and evidence update.
- Current snapshot: public clone of `meneerkrabs/OpenTPW` main `cb625bfedb8bbcbf6d2e71e551a5772929700574` (as supplied for this task).
- Deliverables in this task: created `source/OpenTPW.Tests/ReverbUnitContractTests.cs` and updated this proposal. No runtime source was edited, including `AudioMixer` or `GameAudio`; no register or `APPROX` annotation was retired; no commit or push was made.
- Tests are **not executed**. No dotnet SDK is available in this audit environment. Exact-head CI must execute them later; this document does not claim that they pass.
- No claim is made that Freeverb-style constants, the delay network, or any DSP output equals the original audio device, EAX 2.0 backend, COM backend, or hardware.
- Historical: the previous version of this proposal was plan-only and cited snapshot `5ccddcab` (export without `.git`; commit not independently verified). That material is retained and explicitly labelled as historical below.

## Current findings at cb625bfe

### Record decoding
`ReverbSettings.FromRecord` accepts only a 13-word record and returns `null` for every other length (`source/OpenTPW/Audio/ReverbUnit.cs:8-17`). Words 0, 3, 4, 7 and 9 are integer fields; words 1, 2, 5, 6, 8, 10, 11 and 12 are single-precision floats. The record's field order is documented in the source comment as the order the original sends to its device (`ReverbUnit.cs:4-8`); this audit did not independently verify that historical claim.

### Network and parameter handling
- `ReverbUnit` is a stand-in network with eight damped combs and four allpasses per side and a mono send (`ReverbUnit.cs:20-26, 60-123`). Its comb/allpass lengths, stereo spread, wet scale, reflection scale and gain constants are OpenTPW's own implementation choices; the source labels the network and mappings `[APPROX:AUDIO-039]` (`ReverbUnit.cs:20-40`). Nothing here asserts that those constants came from, or match, original hardware or a backend.
- `Configure` clamps observable parameter ranges (`ReverbUnit.cs:197-247`):
  - environment size is clamped to `1.5..100` m, then converted to a delay-length scale `sqrt(size / 7.5)` clamped to `0.5..1.5`; a changed scale rebuilds both sides' delay lines (`ReverbUnit.cs:199-207`);
  - decay time is clamped to `0.1..20` s and converted to per-comb feedback capped at `0.985` (`ReverbUnit.cs:85-92, 208-211`);
  - high-frequency decay ratio is clamped to `0.1..2`; ratios at or above `1` disable damping (`ReverbUnit.cs:212-214`);
  - diffusion is clamped to `0..1` and mapped to an allpass gain in `0.35..0.65` (`ReverbUnit.cs:215`);
  - the room-plus-reverb millibel sum is floored at `-10000` mB (`ReverbUnit.cs:216-217`);
  - reverb delay is clamped to `0..0.1` s before the pre-delay buffer length is chosen (`ReverbUnit.cs:219-224`);
  - early reflections are enabled only when the reflection delay is at least `0.002` s and the reflection level is greater than `-10000` mB; the delay is clamped to `0..0.3` s and three taps are built (`ReverbUnit.cs:225-246`).

### State and reset behavior
- With no preset, `Process` returns without adding anything or clearing the caller's output (`ReverbUnit.cs:125-135, 151-156`).
- `Set(null)` makes the unit inactive and does not itself clear the delay lines. Any non-null `Set` marks the configuration as pending; the next `Process` calls `Configure` (`ReverbUnit.cs:137-160`).
- A configuration change that keeps the same size scale preserves the comb/allpass state and therefore the held tail; a changed reverb-delay length separately allocates a fresh pre-delay buffer (`ReverbUnit.cs:219-224`). A change of size scale rebuilds both delay-line sides and drops the old tail (`ReverbUnit.cs:137, 199-207`). Thus "reset" in this unit has two distinct current meanings: disabling output with `Set(null)`, and dropping state with a size-scale change. There is no public explicit clear-tail operation.

### Existing tests inspected
`source/OpenTPW.Tests/ReverbUnitTests.cs` already covers: record word order and wrong-length rejection; inactive unit and null preset; a longer decay preserving more impulse-tail energy; a lower reverb level being quieter and a lower high-frequency ratio darkening the tail; an early-reflection arrival at its configured delay; mixer routing of only positioned effects; and a sustained return staying below the dry level. It does not assert exact silence under a zero send, the pre-delay's full shift of the tail, the null/size reset distinction, or the clamp boundaries.

## Added characterization contracts

`source/OpenTPW.Tests/ReverbUnitContractTests.cs` uses a synthetic 44,100 Hz constructor rate and synthetic input only. It supplies the rate directly to `ReverbUnit`; it makes no claim that `AudioMixer.OutputRate` or runtime routing uses that value. Clamp-pair comparisons use exact sample equality because the two settings reach the same configured values, not as a claim of numerical equivalence to an external oracle.

Designed assertions, pending execution:

| Test | Exact source-defined behavior asserted (not yet executed) |
| --- | --- |
| `AnImpulseIsSilentBeforeTheDelayLinesAndThenDecays` | A synthetic impulse with no early reflections is exactly zero for the first 1000 frames, produces nonzero energy after the delay network, and has less than one tenth of the early-window energy in a window near one second with a 1 s decay. |
| `TheReverbDelayShiftsTheWholeTail` | With `reverbDelay = 0.02` s, output is exactly zero for 882 frames and is the sample-for-sample shift of the `reverbDelay = 0` response by 882 frames thereafter. |
| `AZeroSendProducesNoOutputEvenAtTheLongestDecay` | An all-zero send remains exactly zero for two seconds under size 100 m, diffusion 1, decay 20 s, high-frequency ratio 0.1 and reverb delay 0.1 s; the network is not self-excited by its clamped maximum settings. |
| `SetNullStopsTheReturnWithoutTouchingTheOutput` | After a tail exists, `Set(null)` plus a zero-send `Process` adds nothing and leaves a caller buffer prefilled with `0.25` exactly unchanged. |
| `AReenabledPresetKeepsTheTailUnlessTheEnvironmentSizeChanges` | After `Set(null)` interrupts processing, re-enabling the same size continues a nonzero tail; re-enabling with a changed size (7.5 m to 30 m) rebuilds the lines and yields exactly zero for a zero send. |
| `TheEnvironmentSizeIsClampedToItsConfiguredRange` | Sizes 0.1 and 1.5 produce identical samples; sizes 100 and 1000 produce identical samples. |
| `TheDecayTimeIsClampedToItsConfiguredRange` | Decay times 0.01 and 0.1 produce identical samples; 20 and 100 produce identical samples. |
| `TheHighFrequencyDecayRatioIsClampedAndSaturatesAtNoDamping` | Ratios 0.01 and 0.1 produce identical samples; ratios 1 and 10 produce identical samples, consistent with no damping at or above 1. |
| `TheDiffusionIsClampedToItsConfiguredRange` | Diffusion -1 and 0 produce identical samples; 1 and 2 produce identical samples. |
| `TheReverbDelayIsClampedToItsConfiguredRange` | Reverb delays -1 and 0 produce identical samples; 0.1 and 1 produce identical samples. |
| `ReflectionsRequireBothTheirDelayAndLevelThresholds` | With the wet level at its floor, a 0.0019 s reflection delay yields exactly zero early output, 0.002 s yields a nonzero tap; reflection level -10000 mB yields exactly zero early output, -9999 mB yields a nonzero tap. |
| `TheReflectionDelayIsClampedAtItsUpperBound` | Reflection delays 0.3 and 1 s produce identical samples. |
| `TheCombinedReverbLevelHasAMinus10000MillibelFloor` | The pairs (room -10000, reverb -10000) and (room -20000, reverb 0), plus (room -15000, reverb -5000) and (room -10000, reverb -10000), produce identical samples because all reach the floor. |

## Tested claims versus unproven cases

### Covered by the designed assertions (subject to later exact-head CI)
- Deterministic impulse and silence behavior of the current implementation.
- The pre-delay's frame shift and the current difference between disabling, preserving and dropping delay-line state.
- The clamp boundaries listed above, observed through output equality for settings that clamp to the same values.
- The current early-reflection delay and level gates, and the current room-plus-reverb millibel floor.

### Not established
- The tests have not been run; no pass, build, or CI result is claimed.
- No claim that the Freeverb-style lengths, damping, allpass gains, reflections, wet scale or any other DSP constant matches the original device, EAX 2.0, QMixer/COM backend, or original game output.
- No claim about the original executable's preset selection, preset 0/1/14 behavior, voice coverage, or gameplay integration. `AudioMixer` and `GameAudio` were not read in this task because they are outside the supplied allowlist.
- No claim about performance, denormal behavior, numerical parity with another implementation, or thread-safety beyond the source's lock being present.
- No claim that "reset" has a dedicated public operation; the tests characterize the two existing state transitions only.

## Missing evidence and exact paths
- EAX 2.0/COM-backend response evidence, original preset-zero behavior and original voice coverage remain unavailable in this read-only audit.
- If integration or routing claims are required in a later task, the exact additional public paths needed are:
  - `source/OpenTPW/Audio/AudioMixer.cs` (output rate, send/routing and `Reverb` ownership);
  - `source/OpenTPW/Audio/GameAudio.cs` (preset selection and record loading);
  - `source/OpenTPW.Files/Formats/Sound/SndReverbMap.cs` (the `RecordWords` constant used to validate the 13-word record).
- Original binary/device captures are not part of the public source and have no repository path to cite here. No credentials, original game data, accounts, billing, or sharing were used.

## Historical context (carried from the prior plan-only draft)
The following is historical material from the earlier version of this proposal. It is preserved for traceability and was not revalidated in this task.

- Historical snapshot claim: main `5ccddcab` (export had no `.git`; commit not independently verified). That draft was plan-only: no runtime implementation, annotation retirement, publication or merge.
- Historical source inspection cited `AudioMixer.cs` lines for `ReverbUnit Reverb` and channel gains, `GameAudio.cs` lines for `SelectReverb`/`ApplyReverb`/`LoadReverbMap`, and `ReverbUnit.cs` lines for the Freeverb-style lengths, size scale and test attributes. Those files were not read now.
- Historical bounded work: document and evaluate substitute DSP against device/backend behavior; do not label Freeverb constants original; preserve implemented behavior; restrict future changes to the named residual; establish an oracle before filing an implementation for evidence-only items.
- Historical missing evidence: EAX 2.0 or COM-backend response evidence, preset-zero behavior and voice coverage.
- Historical acceptance cases: record impulse responses for presets 0, 1 and 14; switch ridden-ride presets and exit the ride; add a reproducible synthetic fixture; re-run focused regression tests and document what remains unproven. These are not completed by this task.

## Bounded outcome and review boundary
- This task adds characterization tests and current-head documentation only. It does not complete gameplay behavior, retire `APPROX:AUDIO-039`, or establish fidelity.
- The tests require independent review and execution at the exact head in later CI. Until then they are design artifacts, not results.
- Stop at this reviewable evidence package. Do not publish original media or private game data in fixtures; no parity, hardware-equivalence, or merge-ready claim is made.
