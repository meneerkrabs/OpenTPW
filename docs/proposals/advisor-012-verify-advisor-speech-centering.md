# ADVISOR-012: Verify advisor speech centering

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
mono speech duplicated to both channels; the original submits it unpositioned (0x51BFC0 with 0,0,0) and the library places a voice with voice flag 8 (set from the event's any-distance flag 0x200) at (0,1,0) (0x6BC51D); the channel mode 0x1000000 or 0x2000000 passed to QSWaveMixConfigureChannel follows bit 0 of the voice's flags (0x6d23ab-0x6d23bb); QMixer's software stage levels a channel from the azimuth of its position against the mixer's listener (AUDIO-018), so a source at (0,1,0) is centred only if that listener and the source share a frame (the listener is set by 0x6d2aec), and which mode a speech voice gets and the frame of the flat placeholder are not read

## Source inspection
- `source/OpenTPW/Audio/AudioMixer.cs:135`: 136: left = right = samples[index] + (samples[next] - samples[index]) * fraction;; 137: }; 138: else; 139: {
- `source/OpenTPW/Client/SpeechAudioPlayer.cs:80`: 81: if ( audio.Channels == 2 ); 82: return audio.Samples;; 83: var stereo = new short[audio.Samples.Length * 2];; 84: for ( var index = 0; index < audio.Samples.Length; index++ )

## Bounded work
Resolve flat speech coordinate frame and channel-mode selection before changing mono duplication.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace speech voice flags through QMixer SetPosition and listener transforms; share basis evidence with AUDIO-018.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Compare left/right RMS for mono speech under rotated and translated listeners.
2. Stereo speech retains original channels and no accidental spatial panning is introduced.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- AUDIO-018 listener-frame evidence.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
