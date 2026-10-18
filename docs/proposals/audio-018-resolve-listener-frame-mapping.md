# AUDIO-018: Resolve listener frame mapping

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
a positioned mono voice follows the software channel stage of QMixer.dll, which tp.exe selects for every voice (the request flag 1 is ORed in unconditionally at 0x6bc61b, so 0x6d23ab hands QSWaveMixConfigureChannel the mode 0x1000000, whose buffers have no DirectSound 3D object): the source's offset is rotated into the listener's basis (front and up from the camera matrix rows 0x7909b0 and 0x7909a0, right = up x front, 0x1800ce70, 0x1800cf10), the azimuth atan2(x, z) in degrees (0x1800cb10) becomes a DirectSound pan of 44.444 hundredths of a dB per degree mirrored behind the listener (0x18005730), the gain a volume of 2000 log10(gain) hundredths of a dB (0x1800ca40), and 0x18005830 sets both for a mono wave only (0x18005982 tests the format table 0x1803c6b4); the opposite channel is 10^(-|pan|/2000) of the level; the axis mapping of the camera rows into the listener (the swap flag of the tp.exe adapters 0x6d2aec and 0x6d2c68, byte +8 of the driver) and how QMixer places the flat voices that tp.exe positions at (0,1,0) (0x6bc51d, SetPosition 0x6d2640 against SetSourcePosition) are not read, so flat voices keep both channels at the voice's gain

## Source inspection
- `source/OpenTPW/Audio/AudioMixer.cs:114`: 115: var levelled = AudioListener.DecibelGain( AudioListener.GainHundredthsOfDecibel( distanceGain ) );; 116: (leftScale, rightScale) = channels == 1 ? AudioListener.PanGains( pan ) : (1f, 1f);; 117: leftScale *= levelled;; 118: rightScale *= levelled;

## Bounded work
Finish camera-axis swap and flat-placeholder source transforms without altering proven mono pan law.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace adapter byte +8, SetPosition versus SetSourcePosition and listener row mapping in QMixer.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. For front/back/left/right positioned mono sources assert mirrored azimuth and 44.444 dB-unit slope.
2. Rotate listener around flat (0,1,0); compare mono versus stereo behavior against original capture.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
