## Sound chains, voice pool, rolloff and advisor help from tp.exe (October 16, 2026)

A second audio pass over tp.exe via REA, building on the earlier audio, advisor, effects and models sweep:

- **Retired AUDIO-034 (sound chains):** `SoundChain` now follows tp.exe. The link is walked when the next child is made, with the
  parameter of that moment (`0x6bdb50` -> `0x6bdbb0` -> `0x6c3e00`). A sound without links makes the chain start over at the first
  sound on the next tick. Links that exist but do not cover the parameter write 0x7f into the root's parameter byte and set root
  flag 0x40 (`0x6c3f49`); nothing in the sound library clears it, so the chain never makes another child. The pick is
  `ror13(state) % total`, drawn only when a link covers it. The old `Resume()` is gone.
- **Retired AUDIO-033 (rolloff), reversing the earlier fragment:** the earlier sweep
  (`2026-10-16-audio-advisor-effects-and-models-sweep.md`) said QMixer's distance law matches OpenTPW's form and left the rolloff value
  open at 0.8. The exponent is now 0.2: `0x6b9bb0` returns 0.2 (`0x70a624`) when the sound system object's flag 0x40 is set and
  0.8 (`0x70a628`) otherwise; `0x6b9006` sets the flag from descriptor bit 0x100 unless the capability filter `0x6b7d70` clears it;
  the driver factory `0x6c4190` builds the QMixer class first, whose +0x4c is `mov al,1` (`0x5d1400`), so the filter never clears it.
  Positioned voices are louder toward the edge of their range. The step "the factory builds QMixer first" comes from a short
  disassembly of `0x6c4190` and `0x6d2d70` and is not yet re-verified.
- **Narrowed AUDIO-038 (voice pool):** equal ranks keep the newer voice (`0x6b6a80`, `0x6b6bc0`); rank 0 never enters the pool
  (`0x6b6ba0`). Still unknown: how a stopped loop or sentence resumes.
- **Narrowed AUDIO-003 (sample delay):** the start delay is drawn after volume and pitch (`0x6bc2d0`, `0x6bc4f4`); a non-chain voice
  keeps its pool place until the sample's nominal end plus 250 ms plus the delay (`0x6bc01b`, `0x6bcb60`, `0x6b62b0`); events flagged
  0x4 or 0x8 are exempt (`0x6bbe90`). Reverb is unread. The sound chooser draws one extra delay per sample start, so audio draw
  sequences differ from before (presentation only).
- **Narrowed (register text only):** AUDIO-018 and ADVISOR-012 (QMixer's software pan and volume stage), AUDIO-004 and AUDIO-009
  (the sound entry's call sites, the lists' sliders), ADVISOR-005 and ADVISOR-017 (ambient words, pause callers), ADVISOR-026
  (node bit 0x10) and AUDIO-902 (the ribbon emitter in outline).
- **Narrowed ADVISOR-009:** help for the ride (207, repair 297), shop (209), sideshow (208) and research (200) windows.

The pinned run hash and saves are unchanged: no world or guest RNG draw was added, and the new voice creation counter is presentation
only and not saved.
