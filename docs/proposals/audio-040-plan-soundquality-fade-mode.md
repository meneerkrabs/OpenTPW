# AUDIO-040: SoundQuality-controlled FADEOBJ lifecycle

## Status

The bounded runtime lifecycle is implemented. AUDIO-040 remains open for the host scheduling policy below and the unrecoverable saved quality preference; the group arithmetic follows the worker thread's x87 precision since October 28, 2026. This supersedes the earlier evidence-only proposal and its immediate-stop fallback; it does not claim complete sound-library parity.

## Static evidence and configuration

- `0x5523D0 -> 0x51C300 -> 0x6B8930` posts ratio 60 when quality is strictly greater than the configured threshold; otherwise FADEOBJ uses KILLOBJ's immediate stop. Both release every matching script/slot record immediately.
- The sound configuration descriptors at `0x746C3C` and `0x746C78` bind DEFAULTQUALITY to `0x78591C` and FADETOGGLE to `0x785920`. The supplied sound.sam values are 50 and 30. The loader at `0x45B277` precedes option initialization; `0x4236E0` copies the configured default. Zero-filled storage is not the initialized default. With those supplied values, qualities 0..30 stop and 31..100 fade. A saved original user's quality is unavailable.
- Startup `0x54DE02..0x54DE22` uses the default for the normal initialized options branch, or integer-halves it for the alternate branch. OpenTPW's absent/null SoundQuality option uses the loaded configured default directly, without claiming to reconstruct that unmodelled startup branch or a persisted original option. Missing/unreadable SAM values use the explicit host fallback 50/30. Explicit saved values normalize to 0..100, survive Clone/CopyFrom/Cancel, and are independent of legacy volume migration.
- The existing Audio Quality row now edits this option. It controls only this recovered fade effect, not other original hardware/detail settings.

## Poll and ownership contract

The selected library factory `0x6B5D00` installs table `0x70A2A0`; initialization `0x6B9F40` starts `0x6B9E30`. The import `0x6FD1D4` is Sleep. The worker scans immediately, counts fading placeholders before updating them, then sleeps 100 ms if any were counted and 250 ms otherwise. The scan clearing the last fade therefore still chooses 100 ms. Posting a fade only sets its flag and ratio; it neither resets nor wakes the shared phase.

`HeldSoundService` owns released tails independently of script records. Its injected monotonic clock starts with an empty startup scan. The main audio host serializes group refresh, one due scan, stop processing, then decode readiness/admission. It runs at most one scan per callback and delays the next poll from that actual scan, with no fabricated catch-up steps after a stalled host. Simulation pause and time scale do not set this clock. This serialization and startup phase are a managed compatibility policy, not a measurement of original OS scheduling.

A never-admitted sample has no logical handle: if its first poll precedes readiness it becomes stop-pending and cannot be admitted. If readiness/admission precedes that poll it starts normally and then decays. The fade post itself does not cancel decoding. Stop-pending is handled before admission. Failed/cancelled decode, explicit stop, quality downgrade, mixer stop-all, leave-park, sound disable and shutdown all prevent late admission or completion callbacks from resurrecting the sound. A completed stale segment callback cannot replace the current segment.

## Integer volume and groups

`0x6B7130` sets ratio 60 and fade flag 0x400. `0x6BCB80 -> 0x6C50D0 -> 0x6C570B` computes effective integer sample volume, truncates its product with 60 / 100, stores that as the sample's new own volume, and writes that exact value to the device. At zero, absent sample/reply or suspended placeholder it clears fading and marks stop. Without groups, 100 decays through 60, 36, 21, 12, 7, 4, 2, 1, 0; 68 through 40, 24, 14, 8, 4, 2, 1, 0.

`HeldSoundLifetime` keeps own volume, last device volume and live logical sample/reply state separately from transient AudioVoice. Category record +8 supplies group bits: master 1, Music 2, Speech 4, Effects 8, in that order (`0x6C0510`, `0x6BBF91`, `0x6BB447`). Supplied category masks are 9/3/5; RSE types 3..9 use mask 9. Speech ducking and DIPMUSIC change these same factors. Ordinary group changes recompute device volume without overwriting own volume. A fade folds the enabled current group product back into own volume each poll. Exact injected Effects 0.5 gives 100 -> 30 -> 9. The mixer bypasses its usual channel multiplication for these final device writes.

Arithmetic (October 28, 2026): `0x6BB860` multiplies the own volume by the enabled group floats in the x87 registers, stores the product once as a float and converts it with `FISTP` (round to nearest even). The fade poll runs on the library's own `_beginthread` worker (`0x6B6260`, `0x6B9E30`; the manager tick `0x6B62B0` runs the update only when the manager byte `+0x50` is set, which `0x6B9F66` clears), a new thread at the operating system's control word `0x27F` (53-bit precision); the main thread is at `0x7F` (24 bits, `DDSCL_FPUSETUP` at `0x563914`), and the only other control-word writers are the CRT wrappers and the font generator (`0x591220`, `0x591240`). `HeldSoundLifetime.PollEffectiveVolume` therefore multiplies in doubles and rounds to a float once; group writes and sample starts (`MainEffectiveVolume`) multiply in floats. Master 0.75 and effects 0.7 on a volume of 60 are 32 for the poll and 31 for a main-thread computation (`AudioQmixerVoiceTests`). Nonfinite injected host factors still become zero before the conversion.

## Subtypes and continuation

The parser's existing priority remains intact: one-shot, then linear before the bit-2/container branch, then branching/shuffle/plain sample-backed sentence. All seven catalog types use fade entries `0x6B7130` / `0x6BCB80`.

- OneShot owns its sample and decays it.
- OneShotSentence and OneShotBranchingSentence have no parent sample. Their first fade poll marks the parent for stop; manager cleanup stops the currently represented child. It does not recursively fade children.
- Sentence, SentenceShuffle, LinearSentence and BranchingSentence retain logical sample volume through segment completion and pending next decode. Native continuation `0x6BDE00 -> 0x6BDF00 -> 0x6B9A60` updates the existing stream without absolute-volume mask 2. Fading continuation therefore neither redraws nor restores volume. Parameter re-evaluation skips volume while fading and still evaluates pitch (`0x6BBBE0`).
- Fresh ADDOBJ's queued flag-1 submission returns prior handle zero (`0x6B87D0`), so no script-held record is retained. The existing host decode queue handles that submission; this does not add native queued-pool scheduling parity.

An ordinary continuation gap retains a logical reply. An explicit host range stop instead loses the output reply while retaining placeholder identity; a subsequent fade poll marks stop. This is a bounded host mapping of the stopped/released mixer slot, not evidence that native range stop clears placeholder +0x24. Existing AUDIO-038 range/pool/admission and restart approximations remain unchanged. General sentence child concurrency and selection are not completed by this fade implementation.

## Verification map

`HeldSoundFadeTests` names the source audit's cases: Q1–Q6 quality/config, T1–T4 shared scheduling, A1–A3 integer feedback and pitch, G1–G5 groups, S1–S4 all seven subtypes and continuation, P1–P3 readiness order/decode gaps, L1–L2 terminal safety and R1 immediate kill. Extra tests cover speech start/end, DIPMUSIC changes, invalid host gains, range-stop versus decode-gap, stale completion, channel-specific stop and volume-draw suppression. `HeldSoundTests` covers O1–O2 script ownership, particle behavior and leave-park/disable. `AudioTests` covers category mask propagation across binary category records. `OriginalUiTests` covers the existing slider and Cancel restoration.

No original program was executed and no original assets, strings, instruction dumps or acquisition metadata are included. C# tests require the repository's .NET 11 preview SDK; local execution was attempted but the SDK is absent. A CI build/test run and independent review remain necessary before publication readiness.
