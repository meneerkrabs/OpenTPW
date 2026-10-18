# AUDIO-040: SoundQuality-controlled FADEOBJ decay

## Status

Evidence/design correction only. OpenTPW still stops held sounds for both KILLOBJ and FADEOBJ. AUDIO-040 remains open; no fade cadence or sentence-lifetime parity is claimed.

The earlier fixed-60%-forever description is contradicted by the sample-parameter handler. Do not implement or test that description.

## Established behavior

- KILLOBJ stops the matching sound. FADEOBJ posts a handle and the value 60 (`0x5523D0 -> 0x51C300 -> 0x6B8930`). When library settings bit `0x400` is clear, it uses the same stop path as KILLOBJ. When the bit is set, it calls the voice's fade entry through `0x6B65A0`. Both opcodes release the matching script records.
- SoundQuality controls that library bit through `0x51B920`: the comparison at `0x51BA6B..0x51BAA9` is quality greater than the threshold at `0x785920`. The threshold's zero-initialized storage supports the quality-zero stop branch, but does not establish the retail persisted options-file default.
- The options UI offers integer SoundQuality values from 0 through 100: `0x4A3D44..0x4A3D52` sends the quality value to the shared slider builder `0x4A4490`, whose range setup at `0x4A4554..0x4A455A` supplies 0 and 100. The handler calls the quality setter at `0x4A3087..0x4A3094 -> 0x423B40`. The options apply path calls `0x51B920` at `0x42384C..0x423850`; the loader stores the quality field at `0x42452A..0x424536`.
- The base fade entry `0x6B7130` stores 60 in placeholder byte `+0x44` and sets flag `0x400` at `+0x30`. This is a decay ratio, not an absolute target gain.
- The base fade update `0x6BCB80` uses `0x6C50D0`, which sets sample-parameter request mask `0x80000` and passes the ratio in `+0x9C`. At `0x6C570B..0x6C5737`, the sample handler reads the current effective integer volume using `0x6BB860`, multiplies it by the ratio, divides by 100 with integer truncation, and stores the result as the sample's own volume. It sends that result to the device and returns it to the updater (`0x6C5742..0x6C5775`). The ordinary absolute-volume setter is the separate `0x6C5130` / request-mask-2 path.
- With no enabled group factors and no other volume writes, successive base updates starting at 100 produce 60, 36, 21, 12, 7, 4, 2, 1, 0. At zero the updater clears fade flag `0x400` and sets stop flag `0x100` (`0x6BCCCD..0x6BCCDF`, `0x6BCC6E..0x6BCC77`); the manager subsequently dispatches stop (`0x6B62FB..0x6B6320`). A looping base voice therefore does not remain indefinitely at 60%.
- The base updater also marks stop when no playing sample handle exists, its parameter reply is missing, or placeholder flag `0x4000` is set. Pending decode must not be assumed to start later at an absolute gain of 0.60.
- Normal parameter re-evaluation suppresses a new volume calculation while fade flag `0x400` is set, but still recomputes pitch (`0x6BBC5E..0x6BBCA4`). An implementation must not reset the current decaying volume during such re-evaluation.
- Manager byte `+0x50` is initialized to 1 at `0x6B60F0`. The threaded-library initialization path writes it to 0 at `0x6B9F66` and starts a separate updater; `0x6BA080 -> 0x6B6260` invokes each fading voice independently of that byte. Therefore byte zero does not imply that fading is disabled.
- Clearing the library quality bit marks every currently fading voice for stop (`0x6B85CF -> 0x6B6240`). The library stop-all path also walks the manager's active voices (`0x6B85F0 -> 0x6B6550` with argument 0), including voices whose script records have already been released.

## Remaining evidence gates

1. Establish the selected library's complete virtual dispatch and timing before choosing a production fade interval. A separate updater loop at `0x6B9E30` calls `0x6BA080` and passes delay arguments 100 while fades exist and 250 otherwise. The sound initialization calls the corresponding factory at `0x51B6B1 -> 0x6B5D00`, but the required virtual-table and import entries are outside the available complete spans. This is not yet authority to implement a 100 ms schedule.
2. Resolve fade dispatch for sentence and sequence subtypes, especially virtual entries `+0x2C` and `+0x30` of tables `0x70A390` and `0x70A2E0`, and transitions between child samples. Do not generalize the base updater's zero-handle behavior to every sentence subtype without this mapping.
3. Establish pending-play/admission ordering and the effect of enabled group gains. `0x6BB860` can incorporate group factors before each decay step; simply multiplying a floating voice gain by 0.6 while applying mixer groups once is not a demonstrated equivalent.
4. Obtain the retail persisted SoundQuality default if default parity is claimed. Zero is a defensible explicit OpenTPW compatibility fallback; the in-memory zero initializer alone does not prove the retail configuration.

## Bounded implementation contract

- Correct the comments and approximation register now, without changing runtime behavior or retiring AUDIO-040.
- Settings support may be developed separately: integer range 0..100, explicit compatibility fallback 0, JSON persistence, CopyFrom/Cancel restoration and normalization. Do not present an exposed positive setting as a completed fade feature while its runtime behavior remains unimplemented.
- Keep the current stop fallback until timing and subtype gates are resolved. Do not replace it with fixed 0.60 gain, indefinite tails, a guessed frame-based schedule, or a guessed 100 ms schedule.
- Future runtime work must separate record release from audio ownership: remove all matching `(scriptId, slot)` records immediately; keep a released fading voice in the audio service until it finishes or a global cleanup stops it. Later slot moves, parameters and script cleanup must not regain the released record. KILLOBJ remains an immediate stop.
- The audio service must own cleanup of active and pending sounds. A mixer-only stop-all is insufficient if pending decode or sentence callbacks can start another voice afterward. Quality downgrade, leave-park/stop-all and shutdown must be covered explicitly.

## Acceptance cases

1. Settings: default/absent JSON field uses the declared compatibility fallback; values 0, 1 and 100 survive save/load and Clone/CopyFrom; invalid values normalize to 0..100; old volume-format migration does not scale SoundQuality.
2. Routing: quality-zero FADEOBJ and KILLOBJ stop all matching records; KILLOBJ still stops at positive quality; unrelated scripts and slots remain untouched; particle behavior is unchanged.
3. Arithmetic oracle independent of a clock: base voice without enabled group factors follows 100 -> 60 -> 36 -> 21 -> 12 -> 7 -> 4 -> 2 -> 1 -> 0; volume 68 starts 40 -> 24 -> 14. Assert stop-at-zero. A constant-60 or nonterminating floating-point decay implementation must fail this test.
4. Record ownership: releasing a faded record prevents later slot move/parameter calls from reaching it; repeated fade/kill/cleanup does not recreate or double-stop it; the service still owns the tail for final cleanup.
5. Re-evaluation: parameter changes may update pitch but must not restore full volume while the base fade flag is active.
6. Lifecycle, after the subtype gate is resolved: already-playing one-shot, looping sample, pending decode, failed/cancelled decode, sentence between samples and sentence with a pending next sample. Verify natural completion and no resurrection after stop-all, quality downgrade or shutdown.
7. Cadence and non-unity groups require their own proved expected traces before runtime parity can be claimed. Passing a build or synthetic arithmetic test alone does not close these gates.

## Evidence boundary

These findings come from static inspection of fully available mapped code spans. The available executable data is incomplete; this does not establish complete-binary identity, successful full extraction, or an observed original runtime. No original executable, media, instruction dump or private acquisition metadata is part of this proposal.
